using System.Collections.Generic;
using ShanFlyer.UIEffects.Internal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ShanFlyer.UIEffects
{
    internal partial class CanvasEffectOutput
    {
        // Explicit tag survives Unity's destroyed-object null semantics until Reset.
        internal bool isBridge { get; private set; }
        internal Renderer sourceRenderer => isBridge ? _bridgeSource : _renderer;
        internal int outputIndex => _index;
        private Renderer _bridgeSource;
        private MeshFilter _bridgeFilter;
        private Mesh _boundSourceMesh, _bridgeOutput, _lineScratch;
        private bool _originalForceRenderingOff;
        private BridgeSourceLease _bridgeLease;
        private int _bridgeMaterialSlot, _bridgeSubmesh, _bridgeMaterialCount, _bridgeSubmeshCount;
        private SpriteBridgeGeometry _spriteGeometry;
        private Mesh _spriteMesh;
        internal static bool IsMeshSource(Renderer source) => source is MeshRenderer || source is SkinnedMeshRenderer;
        internal static Mesh SourceMesh(Renderer source)
        {
            if (source is SkinnedMeshRenderer skin) return skin.sharedMesh;
            return source && source.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null;
        }
        internal static int BridgeOutputCount(Renderer source)
        {
            if (!CanBridge(source)) return 0;
            if (!IsMeshSource(source)) return 1;
            source.GetSharedMaterials(s_Materials); int count = s_Materials.Count; s_Materials.Clear(); return count;
        }
        private Material BridgeMaterial()
        {
            if (!_bridgeSource) return null;
            _bridgeSource.GetSharedMaterials(s_Materials);
            var result = _bridgeMaterialSlot < s_Materials.Count ? s_Materials[_bridgeMaterialSlot] : null;
            s_Materials.Clear(); return result;
        }
        private Texture BridgeTexture => _bridgeSource is SpriteRenderer sprite && sprite.sprite
            ? ShanFlyer.UIEffects.Internal.SpriteTexture.Resolve(sprite.sprite) : material ? material.mainTexture : null;
        // Local geometry scales around its own Transform, without scaling placement.
        // World-space paths retain a common effect frame; moving their Transform must
        // not drag recorded points or make a trail rotate around its live head.
        private Vector3 BridgeScaleOrigin => _bridgeSource is TrailRenderer
            || (_bridgeSource is LineRenderer line && line.useWorldSpace)
                ? _parent.transform.position : _bridgeSource.transform.position;
        private int _lineRecoveryFrames;
        private CombineInstance[] _bridgeCombine;
        private List<Vector3> _lineVertices;
        private List<int> _lineIndices;
        private LineSnapshotValidator _lineValidator;

        private BridgeGeometrySnapshot _bridgeSnapshot;
        private Matrix4x4 _bridgeMatrix;
        private bool _bridgeGeometryValid, _bridgeGamma, _bridgeWasHidden, _bridgeHadModifiers;
        private EffectUpdateClock _bridgeClock;

        internal void InvalidateBridgeCache() { _bridgeGeometryValid = false; _bridgeSubmittedTexture = null; _bridgeLease?.Invalidate(); _spriteGeometry?.Invalidate(); }

        internal void InvalidateSpriteMaskGeometry() { _spriteMask?.InvalidateGeometry(); }

        internal static bool CanBridge(Renderer source)
        {
            if (!source) return false;
            if (IsMeshSource(source))
            {
                var mesh = SourceMesh(source);
                if (!mesh || mesh.subMeshCount == 0 || (source is MeshRenderer && !mesh.isReadable)) return false;
                for (int i = 0; i < mesh.subMeshCount; ++i) if (mesh.GetTopology(i) != MeshTopology.Triangles) return false;
            }
            else if (!(source is SpriteRenderer) && !(source is TrailRenderer) && !(source is LineRenderer)) return false;
            source.GetSharedMaterials(s_Materials);
            bool supported = s_Materials.Count > 0 && (IsMeshSource(source) || s_Materials.Count == 1);
            foreach (var mat in s_Materials) supported &= mat;
            s_Materials.Clear(); return supported;
        }

        internal void SetBridge(UIEffectRenderer parent, Renderer source, int materialSlot = 0)
        {
            _parent = parent;
            if (_bridgeCombine == null) _bridgeCombine = new CombineInstance[1];
            isBridge = true; _bridgeSource = source; _bridgeMaterialSlot = materialSlot;
            source.TryGetComponent(out _bridgeFilter);
            _boundSourceMesh = SourceMesh(source);
            _bridgeSubmeshCount = _boundSourceMesh ? _boundSourceMesh.subMeshCount : 1;
            _bridgeMaterialCount = BridgeOutputCount(source);
            _bridgeSubmesh = Mathf.Min(materialSlot, _bridgeSubmeshCount - 1);
            _bridgeLease = BridgeSourceLease.Acquire(source);
            _originalForceRenderingOff = _bridgeLease.originallyHidden;
            maskable = parent.maskable; gameObject.layer = parent.gameObject.layer;
            material = _boundMaterial = BridgeMaterial(); _boundTexture = mainTexture;
            _isTrail = false; _lastBakeFrame = -1; _bridgeGeometryValid = false;
            _bridgeSubmittedTexture = null; _bridgeClock.Reset(); _bridgeWasHidden = false;
            enabled = true; RecalculateClipping();
        }

        internal void MaintainBridgeSuppression()
        {
            if (isBridge && _bridgeSource) _bridgeSource.forceRenderingOff = true;
        }

        private bool BridgeBindingIsInvalid()
        {
            return !CanBridge(_bridgeSource)
                || _bridgeSource.GetComponentInParent<UIEffectRenderer>(true) != _parent
                || _bridgeMaterialCount != BridgeOutputCount(_bridgeSource)
                || _boundMaterial != BridgeMaterial()
                || (!(_bridgeSource is SpriteRenderer) && _boundTexture != mainTexture)
                || (IsMeshSource(_bridgeSource) && (SourceMesh(_bridgeSource) != _boundSourceMesh
                    || !_boundSourceMesh || _boundSourceMesh.subMeshCount != _bridgeSubmeshCount));
        }

        private void ReleaseBridge()
        {
            _bridgeLease?.Release(); _bridgeLease = null;
            _spriteGeometry?.Invalidate();
            _bridgeSource = null;
            _bridgeFilter = null;
            _boundSourceMesh = null;
            if (_bridgeCombine != null) _bridgeCombine[0].mesh = null;
            isBridge = false;
            _bridgeGeometryValid = false;
            _bridgeSnapshot = null;
            _bridgeClock.Reset();
            _lineValidator?.Reset();
            _lineRecoveryFrames = 0;
        }

        private void DestroyBridgeMeshes()
        {
            EngineObjects.Destroy(_bridgeOutput);
            EngineObjects.Destroy(_lineScratch);
            EngineObjects.Destroy(_spriteMesh); _spriteMesh = null;
            _bridgeOutput = _lineScratch = null;
        }

        private static Mesh CreateBridgeMesh(string name, IndexFormat format)
        {
            UIEffectProfiler.current.meshesCreated++;
            return new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave, indexFormat = format };
        }

        private void ClearBridgeOutput()
        {
            _bridgeGeometryValid = false;
            _bridgeSubmittedTexture = null;
            if (!_meshCleared) ClearCanvas();
            _meshCleared = true;
            _lastBounds = new Bounds();
            _lineValidator?.Reset();
        }

        private void UpdateBridge(Camera camera)
        {
            if (!_parent || !canvas || !isActiveAndEnabled || !_bridgeSource
                || !_bridgeSource.enabled || !_bridgeSource.gameObject.activeInHierarchy
                || _originalForceRenderingOff || !_parent.canRender)
            { ClearBridgeOutput(); return; }
            if (_lastBakeFrame == Time.frameCount) return;
            _lastBakeFrame = Time.frameCount;
            // A bridge is local to this consumer, so group visibility is irrelevant.
            if (!canvas.isActiveAndEnabled
                || (Application.isPlaying && UIEffectRenderer.earlyCull > 0 && alphaHidden))
            {
                _bridgeWasHidden = true;
                return; // Native Line/Trail source remains enabled and continues sampling.
            }
            var resumed = _bridgeWasHidden;
            _bridgeWasHidden = false;
            var probe = false;
            if (Application.isPlaying && UIEffectRenderer.earlyCull > 0 && clipHidden)
            {
                if (Time.unscaledTime < _nextCullProbeTime) return;
                _nextCullProbeTime = Time.unscaledTime + 0.1f;
                probe = true;
            }
            if (Application.isPlaying && UIEffectRenderer.updateRatePercent < 100
                && !_bridgeClock.AdvanceScheduled(0, 0, Time.frameCount, UIEffectRenderer.updateRatePercent,
                    UIEffectRenderer.staggerBaking ? _parent.independentBakePhase : 0,
                    !_bridgeGeometryValid || resumed || probe || _forceBake, out _, out _)) return;
            if (UIEffectRenderer.updateRatePercent == 100) _bridgeClock.Reset();
            _forceBake = false;
            var scale = Vector3.Scale(_parent.calculatedScale, _parent.parentScale);
            if (!EffectScale.HasVolume(scale)) { ClearBridgeOutput(); return; }
            var origin = BridgeScaleOrigin;
            var matrix = transform.worldToLocalMatrix * Matrix4x4.Translate(origin)
                * Matrix4x4.Scale(scale) * Matrix4x4.Translate(-origin);
            Mesh input;
            if (_bridgeSource is MeshRenderer)
            {
                input = _bridgeFilter ? _bridgeFilter.sharedMesh : null;
                if (!input || !input.isReadable || _bridgeSubmesh >= input.subMeshCount) { ClearBridgeOutput(); return; }
                matrix *= _bridgeSource.localToWorldMatrix;
            }
            else if (_bridgeSource is SkinnedMeshRenderer skin)
            {
                if (!skin.sharedMesh) { ClearBridgeOutput(); return; }
                input = _bridgeLease.Capture(skin);
                matrix *= skin.localToWorldMatrix;
            }
            else if (_bridgeSource is SpriteRenderer sprite)
            {
                _spriteGeometry ??= new SpriteBridgeGeometry();
                if (!_spriteMesh) _spriteMesh = CreateBridgeMesh("UI sprite snapshot", IndexFormat.UInt16);
                if (!_spriteGeometry.Capture(sprite, _spriteMesh, MaxUiVertices)) { ClearBridgeOutput(); return; }
                input = _spriteMesh; matrix *= sprite.localToWorldMatrix;
                if (_boundTexture != mainTexture) { _boundTexture = mainTexture; SetMaterialDirty(); }
            }
            else
            {
                if (!camera) { ClearBridgeOutput(); return; }
                var trail = _bridgeSource as TrailRenderer;
                var line = _bridgeSource as LineRenderer;
                if ((trail && trail.positionCount < 2) || (line && line.positionCount < 2))
                { ClearBridgeOutput(); return; }
                if (trail && Application.isPlaying)
                {
                    if (Mathf.Max(Time.deltaTime, Time.unscaledDeltaTime) > 0.1f)
                    { _lineRecoveryFrames = 3; return; }
                    if (_lineRecoveryFrames > 0) { _lineRecoveryFrames--; return; }
                }
                if (!_lineScratch) _lineScratch = CreateBridgeMesh("UIEffectRenderer Line Scratch", IndexFormat.UInt32);
                if (_lineValidator == null)
                {
                    _lineValidator = new LineSnapshotValidator();
                    _lineVertices = new List<Vector3>();
                    _lineIndices = new List<int>();
                }
                _lineScratch.Clear(false);
                var started = UIEffectProfiler.Timestamp();
                // Unity 6.4: without useTransform, world-space lines/trails are already
                // world geometry. Only a local-space LineRenderer needs its source matrix.
                if (trail) trail.BakeMesh(_lineScratch, camera, false);
                else line.BakeMesh(_lineScratch, camera, false);
                UIEffectProfiler.EndStage(2, started);
                UIEffectProfiler.current.bakeOps++;
                UIEffectProfiler.current.bakedVertices += _lineScratch.vertexCount;
                UIEffectRenderer.s_FrameBakeOps++;
                UIEffectRenderer.s_FrameBakedVerts += _lineScratch.vertexCount;
                if (_lineScratch.vertexCount > MaxUiVertices)
                { ReportVertexLimit(_lineScratch.vertexCount); ClearBridgeOutput(); return; }
                _lineScratch.GetVertices(_lineVertices);
                _lineIndices.Clear();
                if (_lineScratch.subMeshCount > 0) _lineScratch.GetIndices(_lineIndices, 0);
                var result = _lineValidator.Evaluate(_lineVertices, _lineIndices);
                if (result == LineSnapshotValidator.Result.Empty) { ClearBridgeOutput(); return; }
                if (result != LineSnapshotValidator.Result.Valid)
                {
                    if (_lineValidator.expired)
                    {
                        ClearCanvas(); _meshCleared = true; _lastBounds = new Bounds();
                        // Keep rejection history: next finite snapshot may rebase.
                    }
                    return;
                }
                input = _lineScratch;
                if (line && !line.useWorldSpace) matrix *= _bridgeSource.localToWorldMatrix;
            }
            if (input.vertexCount == 0) { ClearBridgeOutput(); return; }
            if (input.vertexCount > MaxUiVertices)
            { ReportVertexLimit(input.vertexCount); ClearBridgeOutput(); return; }
            if (!_bridgeOutput) _bridgeOutput = CreateBridgeMesh("UIEffectRenderer Bridge Output", IndexFormat.UInt16);
            if (_bridgeSnapshot == null) _bridgeSnapshot = new BridgeGeometrySnapshot();
            var compareStarted = UIEffectProfiler.Timestamp();
            var contentChanged = _bridgeSnapshot.Capture(input, _bridgeSubmesh);
            UIEffectProfiler.current.bridgeCompareMs += UIEffectProfiler.Milliseconds(compareStarted);
            var gamma = UIEffectSettings.ConvertVertexColors(canvas);
            var hasModifiers = _geometry.ScanModifiers(this);
            if (_bridgeGeometryValid && !contentChanged && _bridgeMatrix.Equals(matrix)
                && _bridgeGamma == gamma && !hasModifiers && !_bridgeHadModifiers)
            {
                UpdateBridgeMaterial();
                UIEffectProfiler.current.bridgeCacheHits++;
                return;
            }
            var combineStarted = UIEffectProfiler.Timestamp();
            _bridgeOutput.Clear(false);
            _bridgeCombine[0].mesh = input;
            _bridgeCombine[0].subMeshIndex = _bridgeSubmesh;
            _bridgeCombine[0].transform = matrix;
            try { _bridgeOutput.CombineMeshes(_bridgeCombine, true, true); }
            finally { _bridgeCombine[0].mesh = null; }
            var validGeometry = _geometry.Finish(_bridgeOutput, gamma, MaxUiVertices, out _lastBounds);
            UIEffectProfiler.EndStage(3, combineStarted);
            if (!validGeometry) { ReportVertexLimit(MaxUiVertices + 1); ClearBridgeOutput(); return; }
            if (!_parent || !_bridgeSource || !isActiveAndEnabled) return;
            UpdateBridgeMaterial();
            var submitStarted = UIEffectProfiler.Timestamp();
            canvasRenderer.SetMesh(_bridgeOutput);
            _bridgeGeometryValid = true;
            _bridgeMatrix = matrix;
            _bridgeGamma = gamma;
            _bridgeHadModifiers = hasModifiers;
            _meshCleared = false;
            UIEffectProfiler.current.setMeshOps++;
            UIEffectProfiler.EndStage(4, submitStarted);
        }
        private void UpdateBridgeMaterial()
        {
            // Material animation is independent from geometry reuse, including MPB removal.
            if (_parent.hasMaterialPropertyBindings && materialForRendering)
                materialForRendering.CopyPropertiesFromMaterial(base.GetModifiedMaterial(material));
            UpdateMaterialProperties();
            _spriteMask?.ApplyStencilState(materialForRendering);
            var texture = _bridgeSource is SpriteRenderer ? mainTexture : materialForRendering ? materialForRendering.mainTexture : mainTexture;
            SetCanvasRendererMaterials(canvasRenderer);
            if (_bridgeSubmittedTexture != texture)
            {
                canvasRenderer.SetTexture(texture);
                _bridgeSubmittedTexture = texture;
            }
        }
        private Texture _bridgeSubmittedTexture;
    }
}
