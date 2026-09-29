using System;
using System.Collections.Generic;
using ShanFlyer.UIEffects.Internal;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ShanFlyer.UIEffects
{
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    [AddComponentMenu("")]
    internal partial class CanvasEffectOutput : MaskableGraphic
    {
        private CombineInstance[] _singleCombine;
        private Mesh _singleBake;
        private static readonly List<Material> s_Materials = new List<Material>(2);
        private MaterialPropertySynchronizer _propertySynchronizer;
        private readonly GeometryProcessor _geometry = new GeometryProcessor();
        // CanvasRenderer only accepts 16-bit indices. Do not change Graphic.workerMesh:
        // that mesh is shared with ordinary UI components throughout the application.
        internal const int MaxUiVertices = 64999;
        private Mesh _outputMesh;
        private Mesh particleMesh
        {
            get
            {
                if (_outputMesh == null)
                {
                    _outputMesh = new Mesh
                    {
                        name = "[CanvasEffectOutput] UI Output Mesh",
                        hideFlags = HideFlags.HideAndDontSave,
                        indexFormat = IndexFormat.UInt16
                    };
                }
                return _outputMesh;
            }
        }
        private bool _vertexLimitReported;
        private int _index;
        private bool _isTrail;
        private bool _meshCleared;
        private Bounds _lastBounds;
        private Material _materialForRendering;
        private uint _materialRevision;
        private uint _resolvedMaterialRevision = uint.MaxValue;
        private readonly CanvasMaterialBinding _materialBinding = new CanvasMaterialBinding();
        private SpriteMaskDrawScope _spriteMask;
        private UIEffectRenderer _parent;
        private ParticleSystem _particleSystem;
        private ParticleSystemRenderer _renderer;
        private ParticleSourceBinding _sourceBinding;
        private Material _boundMaterial;
        private Texture _boundTexture;
        private bool _boundTrails;
        private ParticleSystem _mainEmitter;
        private EffectUpdateClock _bakeClock;
        private bool _forceBake = true;
        private bool _materialsDirty = true;
        private Material _submittedMaterial;
        private int _lastBakeFrame = -1;
        // Merged mode: one renderer binds all non-trail ParticleSystems of the same UIEffectRenderer.
        private ParticleSystem[] _mergedSystems;
        private ParticleSystemRenderer[] _mergedPsRenderers;
        private ParticleSourceBinding[] _sourceBindings;
        private ParticleSystem[] _mergedMainEmitters;
        private Material[] _mergedMaterials;
        private Texture[] _mergedTextures;
        // [FxUIEffects 刀7] Trail 并入合并渲染器:每系统 [quad, trail] 交错子网格,不再为 trail 单建 renderer。
        private bool[] _mergedIsTrail;
        private int[] _mergedQuadIdx;
        private int[] _mergedTrailIdx;
        private Material[] _mergedSubmeshMaterials;
        private CombineInstance[] _mergedCombines;
        private bool _mergedUniform;
        internal bool isMerged => _mergedSystems != null;
        // [FxUIEffects 刀5] Early Culling:UGUI Cull 回调缓存的几何裁剪结论(不含 bounds-empty,避免首帧被永久跳过烘焙)。
        private bool _uguiClipCulled;
        private float _nextCullProbeTime;
        // [FxUIEffects 刀6] 静态网格缓存:暂停且全部相关 Transform/Canvas 未变时整帧跳过烘焙链。
        private bool _staticValid;
        private Vector3 _staticPsPos;
        private Quaternion _staticPsRot;
        private Vector3 _staticPsScale;
        private Vector3 _staticRenPos;
        private Quaternion _staticRenRot;
        private Vector3 _staticRenScale;
        private Vector3 _staticParentScale;
        private Vector3 _staticRootScale;
        private Vector2Int _staticScreen;
        private float _staticCanvasScale;
        private Vector3 _staticParticleScale;
        private UIEffectRenderer.OriginMode _staticPositionMode;
        private uint _staticViewRevision;
        private bool _staticGamma;
        // [刀6] IsIdleForFastPath and the matching UpdateMesh* evaluate the same static-frame
        // predicate against identical inputs within one frame; cache the result (keyed by mode).
        private int _staticFrameCacheFrame = -1;
        private bool _staticFrameCacheMerged;
        private bool _staticFrameCacheValue;
        private Vector3[] _mergedStaticPos;
        private Quaternion[] _mergedStaticRot;
        private Vector3[] _mergedStaticScale;
        private bool[] _mergedStaticActive;

        public override Texture mainTexture => isBridge ? BridgeTexture
            : _isTrail ? null : ParticleSourceInfo.SpriteSheetTexture(_particleSystem);

        public override bool raycastTarget => false;

        private Rect rootCanvasRect
        {
            get
            {
                var matrix = transform.localToWorldMatrix;
                var currentCanvas = canvas;
                if (currentCanvas) matrix = currentCanvas.rootCanvas.transform.worldToLocalMatrix * matrix;
                return EffectScale.TransformRect(_lastBounds, matrix);
            }
        }

        public override Material materialForRendering
        {
            get
            {
                bool destroyed = !ReferenceEquals(_materialForRendering, null) && !_materialForRendering;
                if (_resolvedMaterialRevision != _materialRevision || destroyed)
                {
                    _materialForRendering = base.materialForRendering;
                    _resolvedMaterialRevision = _materialRevision;
                }
                return _materialForRendering;
            }
        }

        private void InvalidateResolvedMaterial()
        {
            unchecked { ++_materialRevision; }
            _materialForRendering = null;
        }

        internal void ExpireParticleSnapshot()
        {
            _staticValid = false;
            _staticFrameCacheFrame = -1;
        }

        internal bool CanRetainBinding(UIEffectRenderer owner, ParticleSystem source, ParticleSystem emitter,
            bool trail, List<ParticleSystem> merged, Renderer bridge, int materialSlot = 0)
        {
            if (!isActiveAndEnabled || _parent != owner || bindingIsInvalid) return false;
            if (bridge) return isBridge && _bridgeSource == bridge && _bridgeMaterialSlot == materialSlot;
            if (isBridge) return false;
            if (merged == null)
                return _mergedSystems == null && _particleSystem == source && _mainEmitter == emitter
                    && _isTrail == trail && source && source.GetComponent<ParticleSystemRenderer>() == _renderer;
            if (_mergedSystems == null || _mergedSystems.Length != merged.Count) return false;
            for (int i = 0; i < merged.Count; ++i)
                if (_mergedSystems[i] != merged[i] || !merged[i]
                    || _mergedPsRenderers[i] != merged[i].GetComponent<ParticleSystemRenderer>()
                    || _mergedMainEmitters[i] != owner.sourceTopology.ParentOf(merged[i])) return false;
            return true;
        }
        internal void RefreshRetainedBinding(UIEffectRenderer owner, int index)
        {
            _index = index; gameObject.layer = owner.gameObject.layer;
            if (maskable != owner.maskable) { maskable = owner.maskable; RecalculateClipping(); }
            InvalidateMeshCache(); SetMaterialDirty();
        }

        public void Reset(int index = -1)
        {
            // Retire UI submission before returning source leases. Rebinding starts from
            // the same state whether this component was active, pooled or externally disabled.
            enabled = false;
            if (canvasRenderer) ClearCanvas();
            material = null;
            _materialBinding.Dispose();
            InvalidateResolvedMaterial();
            ReleaseBridge();
            ReleaseSpriteMask();
            ReleaseParticleBindings();
            _parent = null;
            _boundMaterial = null;
            _boundTexture = null;
            _boundTrails = false;
            _isTrail = false;
            _lastBounds = default;
            _uguiClipCulled = _renderCullWasHidden = false;
            _nextCullProbeTime = 0;
            _staticValid = false;
            _staticFrameCacheFrame = -1;
            _meshCleared = false;
            _vertexLimitReported = false;
            _bakeClock.Reset();
            _forceBake = true;
            _lastBakeFrame = -1;
            if (index >= 0) _index = index;
        }

        private void ReleaseParticleBindings()
        {
            _sourceBinding?.Release();
            _sourceBinding = null;
            if (_sourceBindings != null)
                foreach (var binding in _sourceBindings) binding?.Release();
            _sourceBindings = null;
            _particleSystem = null;
            _renderer = null;
            _mainEmitter = null;
            ReleaseMergedMeshes();
            if (_singleCombine != null) _singleCombine[0].mesh = null;
            _mergedSystems = null;
            _mergedPsRenderers = null;
            _mergedMainEmitters = null;
            _mergedMaterials = null;
            _mergedTextures = null;
            _mergedIsTrail = null;
            _mergedQuadIdx = null;
            _mergedTrailIdx = null;
            _mergedSubmeshMaterials = null;
            _mergedUniform = false;
            _mergedStaticPos = null;
            _mergedStaticRot = null;
            _mergedStaticScale = null;
            _mergedStaticActive = null;
        }

        private void ReleaseMergedMeshes()
        {
            if (_mergedCombines == null) return;
            for (var i = 0; i < _mergedCombines.Length; i++)
                EngineObjects.Destroy(_mergedCombines[i].mesh);
            _mergedCombines = null;
        }

        internal void ReleaseBakeResources() { ReleaseMergedMeshes(); }

        private void EnsureMergedMeshes()
        {
            if (_mergedCombines == null) _mergedCombines = new CombineInstance[_mergedSubmeshMaterials.Length];
            for (var i = 0; i < _mergedCombines.Length; i++)
                if (_mergedCombines[i].mesh == null)
                {
                    UIEffectProfiler.current.meshesCreated++;
                    _mergedCombines[i].mesh = new Mesh
                    {
                        name = "[CanvasEffectOutput] Merged Combine Instance Mesh",
                        hideFlags = HideFlags.HideAndDontSave,
                        indexFormat = IndexFormat.UInt16
                    };
                }
        }

        protected override void OnDestroy()
        {
            Reset();
            DestroyBridgeMeshes();
            EngineObjects.Destroy(_outputMesh);
            _outputMesh = null;
            _geometry.Dispose();
            EngineObjects.Destroy(_singleBake); _singleBake = null;
            base.OnDestroy();
        }

        internal void InvalidateMeshCache()
        {
            _staticValid = false;
            InvalidateBridgeCache();
            _staticFrameCacheFrame = -1;
            _meshCleared = false;
            // Refill immediately after invalidation, even with a reduced update percentage.
            _forceBake = true;
            _materialsDirty = true;
        }

        private bool AdvanceBakeClock(out float scaledStep, out float unscaledStep)
        {
            if (_parent.isPaused)
            {
                _bakeClock.Reset();
                scaledStep = unscaledStep = 0;
                _forceBake = false;
                return true;
            }
            // Render culling retains the same frame ratio while skipping geometry work.
            var percentage = _isTrail || !Application.isPlaying ? 100 : UIEffectRenderer.updateRatePercent;
            bool ready = true;
            if (percentage < 100)
                ready = _bakeClock.AdvanceScheduled(Time.deltaTime, Time.unscaledDeltaTime,
                    Time.frameCount, percentage, UIEffectRenderer.staggerBaking ? _parent.particleBakePhase : 0, _forceBake,
                    out scaledStep, out unscaledStep);
            else
                _bakeClock.AdvanceEveryFrame(Time.deltaTime, Time.unscaledDeltaTime, out scaledStep, out unscaledStep);
            if (ready) _forceBake = false;
            return ready;
        }

        private bool _renderCullWasHidden;
        private bool ShouldSkipCulledBake()
        {
            // Hidden consumers need no geometry probes; check their visibility directly.
            // A sharing owner must still render for visible consumers on other Canvases.
            var hidden = _parent.useMeshSharing ? _parent.groupAllAlphaHidden
                : (!canvas.isActiveAndEnabled || (UIEffectRenderer.earlyCull > 0 && alphaHidden));
            if (Application.isPlaying && hidden)
            {
                _renderCullWasHidden = true;
                return true;
            }
            if (_renderCullWasHidden)
            {
                _renderCullWasHidden = false;
                _forceBake = true;
            }
            if (UIEffectRenderer.earlyCull <= 0 || !(_parent.useMeshSharing ? _parent.groupAllClipped : _uguiClipCulled)
                || !Application.isPlaying) return false;
            // Bounds describe the last baked frame. Moving particles can re-enter
            // the clip rect without moving their emitter; periodically refresh them.
            if (_lastBounds.extents == Vector3.zero) return false;
            if (Time.unscaledTime >= _nextCullProbeTime)
            {
                _nextCullProbeTime = Time.unscaledTime + 0.1f;
                // Force this probe frame to bake even if the bake clock is not due,
                // otherwise the throttled frame would drop the probe refresh.
                _forceBake = true;
                return false;
            }
            return true;
        }

        internal bool alphaHidden => canvasRenderer.GetInheritedAlpha() <= 0.001f;
        internal bool clipHidden => _uguiClipCulled && _lastBounds.extents != Vector3.zero;
        private bool FullCullHidden => _parent.useMeshSharing ? _parent.groupAllAlphaHidden : alphaHidden;

        private void ClearMeshAndReplicas()
        {
            var renderers = Buffers<CanvasEffectOutput>.Rent();
            try
            {
                if (_parent != null && _parent.useMeshSharing && _parent.canSimulate)
                    UIEffectScheduler.GetGroupedRenderers(_parent.sharingGroup, _index, renderers);
                if (!renderers.Contains(this)) renderers.Add(this);
                for (var i = 0; i < renderers.Count; i++)
                {
                    var r = renderers[i];
                    if (r == null) continue;
                    r.ClearCanvas();
                    r._lastBounds = new Bounds();
                    r._meshCleared = true;
                    r._staticValid = false;
                }
            }
            finally { Buffers<CanvasEffectOutput>.Return(ref renderers); }
        }

        internal void ClearMesh()
        {
            if (!_meshCleared) ClearMeshAndReplicas();
        }

        private static readonly List<ParticleSystemVertexStream> s_MergeStreams = new List<ParticleSystemVertexStream>();
        private static readonly List<ParticleSystemVertexStream> s_MergeFirstStreams = new List<ParticleSystemVertexStream>();
        private static readonly List<ParticleSystemVertexStream> s_MergeTrailStreams = new List<ParticleSystemVertexStream>();

        private static MaterialPropertyBlock s_MergePropertyBlock;
        internal static bool HasUserPropertyBlock(Renderer renderer)
        {
            if (!renderer.HasPropertyBlock()) return false;
            if (s_MergePropertyBlock == null) s_MergePropertyBlock = new MaterialPropertyBlock();
            renderer.GetSharedMaterials(s_Materials);
            try
            {
                renderer.GetPropertyBlock(s_MergePropertyBlock);
                for (var i = 0; i < s_Materials.Count; i++)
                    if (HasDeclaredOverride(s_Materials[i], s_MergePropertyBlock)) return true;
                for (var i = 0; i < s_Materials.Count; i++)
                {
                    renderer.GetPropertyBlock(s_MergePropertyBlock, i);
                    if (HasDeclaredOverride(s_Materials[i], s_MergePropertyBlock)) return true;
                }
                return false;
            }
            finally { s_Materials.Clear(); }
        }

        private static bool HasDeclaredOverride(Material material, MaterialPropertyBlock block)
        {
            // Engine-owned particle blocks need not contain material-declared overrides.
            if (!material || !material.shader || block.isEmpty) return false;
            var shader = material.shader;
            for (var i = 0; i < shader.GetPropertyCount(); i++)
                if (block.HasProperty(shader.GetPropertyNameId(i))) return true;
            return false;
        }

        private static bool SameVertexStreams(List<ParticleSystemVertexStream> a, List<ParticleSystemVertexStream> b)
        {
            if (a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        internal static bool CanMerge(List<ParticleSystem> systems)
        {
            Material firstMaterial = null;
            Texture firstTexture = null;
            var first = true;
            var firstMode = ParticleSystemRenderMode.Billboard;
            for (var i = 0; i < systems.Count; i++)
            {
                var ps = systems[i];
                if (ps == null || !ps.TryGetComponent<ParticleSystemRenderer>(out var renderer)) continue;
                if (renderer.maskInteraction != SpriteMaskInteraction.None || HasUserPropertyBlock(renderer)) return false;
                renderer.GetActiveVertexStreams(s_MergeStreams);
                if (first)
                {
                    firstMode = renderer.renderMode;
                    s_MergeFirstStreams.Clear();
                    s_MergeFirstStreams.AddRange(s_MergeStreams);
                }
                else if (renderer.renderMode != firstMode || !SameVertexStreams(s_MergeFirstStreams, s_MergeStreams)) return false;
                if (ps.trails.enabled)
                {
                    renderer.GetActiveTrailVertexStreams(s_MergeTrailStreams);
                    if (!SameVertexStreams(s_MergeFirstStreams, s_MergeTrailStreams)) return false;
                }
                var mat = renderer.sharedMaterial;
                var texture = ParticleSourceInfo.SpriteSheetTexture(ps);
                var effectiveTexture = texture != null ? texture : mat != null ? mat.mainTexture : null;
                if (!first && (mat != firstMaterial || effectiveTexture != firstTexture)) return false;
                first = false;
                firstMaterial = mat;
                firstTexture = effectiveTexture;
                // CanvasRenderer has a single texture override. Mixed draw materials
                // and sprite/trail textures require separate MaskableGraphics.
                if (ps.trails.enabled && (renderer.trailMaterial != mat
                    || (mat != null && mat.mainTexture != effectiveTexture))) return false;
            }
            return !first;
        }

        internal bool bindingIsInvalid
        {
            get
            {
                if (_parent == null) return false; // Spare renderer retained for reuse.
                if (isBridge) return BridgeBindingIsInvalid();
                if (_particleSystem == null || _renderer == null) return true;
                if (_mergedSystems == null)
                    return _boundTrails != _particleSystem.trails.enabled
                           || _boundMaterial != (_isTrail ? _renderer.trailMaterial : _renderer.sharedMaterial)
                           || _boundTexture != mainTexture;
                if (_parent.hasMaterialPropertyBindings) return true;
                Texture boundSprite = null;
                var foundBoundSystem = false;
                Material lastMaterial = null;
                Texture lastMaterialTexture = null;
                var hasMaterialTexture = false;
                for (var i = 0; i < _mergedSystems.Length; i++)
                {
                    var ps = _mergedSystems[i];
                    var renderer = _mergedPsRenderers[i];
                    var hasTrails = _mergedIsTrail[i];
                    if (ps == null || renderer == null
                        || ps.trails.enabled != hasTrails
                        || renderer.sharedMaterial != _mergedMaterials[i]) return true;
                    var sprite = ParticleSourceInfo.SpriteSheetTexture(ps);
                    if (ReferenceEquals(ps, _particleSystem))
                    {
                        boundSprite = sprite;
                        foundBoundSystem = true;
                    }
                    var mat = _mergedMaterials[i];
                    // Reuse only within this validation pass, so runtime texture edits
                    // are still observed next time, even within the same Unity frame.
                    if ((sprite == null || hasTrails)
                        && (!hasMaterialTexture || !ReferenceEquals(mat, lastMaterial)))
                    {
                        lastMaterialTexture = mat != null ? mat.mainTexture : null;
                        lastMaterial = mat;
                        hasMaterialTexture = true;
                    }
                    if ((sprite != null ? sprite : lastMaterialTexture) != _mergedTextures[i]) return true;
                    if (hasTrails && (renderer.trailMaterial != _mergedSubmeshMaterials[_mergedTrailIdx[i]]
                        || (mat != null && lastMaterialTexture != _mergedTextures[i]))) return true;
                }
                return _boundTexture != (_isTrail ? null : foundBoundSystem ? boundSprite : mainTexture);
            }
        }

        private void ReportVertexLimit(int vertexCount)
        {
            if (_vertexLimitReported) return;
            _vertexLimitReported = true;
            Debug.LogWarningFormat(this,
                "[UIEffectRenderer] UI mesh vertex limit exceeded ({0} > {1}). Reduce particles/trails; oversized meshes are not submitted.",
                vertexCount, MaxUiVertices);
        }

        internal void ApplyMaskPolicy(bool value)
        {
            bool changed = maskable != value;
            maskable = value;
            if (changed) RecalculateClipping();
            SetMaterialDirty();
        }

        protected override void OnEnable()
        {
            hideFlags = UIEffectSettings.GeneratedObjectFlags;
            base.OnEnable();
        }

        private Mesh IntermediateBakeMesh()
        {
            if (!_singleBake) _singleBake = new Mesh { name = "UI effect intermediate", hideFlags = HideFlags.HideAndDontSave };
            _singleCombine ??= new CombineInstance[1];
            return _singleBake;
        }

        protected override void OnDisable()
        {
            ReleaseSpriteMask();
            base.OnDisable();

            _materialBinding.Dispose();
            InvalidateResolvedMaterial();
        }

        public static CanvasEffectOutput AddRenderer(UIEffectRenderer parent, int index)
        {
            var node = new GameObject("[generated] Canvas effect output", typeof(RectTransform), typeof(CanvasRenderer));
            node.SetActive(false);
            node.transform.SetParent(parent.transform, false);
            node.layer = parent.gameObject.layer;
            node.hideFlags = UIEffectSettings.GeneratedObjectFlags;
            var output = node.AddComponent<CanvasEffectOutput>();
            output._parent = parent;
            output._index = index;
            node.SetActive(true);
            return output;
        }

        // Resolve the UGUI stencil base, this output's material overrides, then SpriteMask.
        public override Material GetModifiedMaterial(Material baseMaterial)
        {
            if (!_parent || !IsActive()) { _materialBinding.Dispose(); return baseMaterial; }
            var stencilBase = base.GetModifiedMaterial(baseMaterial);
            var resolved = _materialBinding.Resolve(stencilBase, mainTexture,
                _parent.hasMaterialPropertyBindings ? this : null);
            return _spriteMask == null ? resolved : _spriteMask.Modify(resolved);
        }

        private void ReleaseSpriteMask()
        {
            _spriteMask?.Dispose();
            _spriteMask = null;
            InvalidateResolvedMaterial();
            _materialsDirty = true;
        }

        internal void PrepareSpriteMask()
        {
            if (!_parent || isMerged) return;
            Renderer source = isBridge ? _bridgeSource as SpriteRenderer : _renderer;
            if (!source) return;
            if (SpriteMaskResolver.Interaction(source) == SpriteMaskInteraction.None && _spriteMask == null) return;
            _spriteMask ??= new SpriteMaskDrawScope(this);
            // Mask writers must use the same scale origin as their source geometry.
            var anchor = isBridge ? BridgeScaleOrigin
                : _parent.originMode == UIEffectRenderer.OriginMode.Emitter
                    ? _particleSystem.transform.position : _parent.transform.position;
            var scale = isBridge ? Vector3.Scale(_parent.calculatedScale, _parent.parentScale) : GetWorldScale();
            var matrix = transform.worldToLocalMatrix * EffectScale.ScaleAndOffset(scale, anchor - Vector3.Scale(anchor, scale));
            if (isBridge && (!source.enabled || !source.gameObject.activeInHierarchy || _originalForceRenderingOff
                || !((SpriteRenderer)source).sprite)) source = null;
            _spriteMask.Prepare(source, _parent, matrix);
            SetCanvasRendererMaterials(canvasRenderer);
        }

        public void Set(UIEffectRenderer parent, ParticleSystem ps, bool isTrail, ParticleSystem mainEmitter)
        {
            var binding = ParticleSourceBinding.Acquire(ps);
            if (binding == null) { Reset(); return; }
            _sourceBinding = binding;
            _parent = parent;
            _particleSystem = binding.source;
            _renderer = binding.renderer;
            _mainEmitter = mainEmitter;
            _isTrail = isTrail;
            maskable = parent.maskable;
            gameObject.layer = parent.gameObject.layer;
            _boundTrails = ps.trails.enabled;
            material = _boundMaterial = isTrail ? _renderer.trailMaterial : _renderer.sharedMaterial;
            _boundTexture = mainTexture;
            canvasRenderer.SetTexture(null);
            enabled = true;
            RecalculateClipping();
        }

        /// <summary>
        /// Bind compatible ParticleSystems and their trails to a single renderer.
        /// The caller checks material/texture compatibility and animatable properties.
        /// </summary>
        public void SetMerged(UIEffectRenderer parent, List<ParticleSystem> particleSystems)
        {
            _parent = parent;
            maskable = parent.maskable;

            gameObject.layer = parent.gameObject.layer;

            var count = 0;
            var trailCount = 0;
            for (var i = 0; i < particleSystems.Count; i++)
            {
                var ps = particleSystems[i];
                if (ps == null) continue;
                count++;
                if (ps.trails.enabled) trailCount++;
            }

            _mergedSystems = new ParticleSystem[count];
            _mergedPsRenderers = new ParticleSystemRenderer[count];
            _sourceBindings = new ParticleSourceBinding[count];
            _mergedMainEmitters = new ParticleSystem[count];
            _mergedMaterials = new Material[count];
            _mergedTextures = new Texture[count];
            _mergedIsTrail = new bool[count];
            _mergedQuadIdx = new int[count];
            _mergedTrailIdx = new int[count];
            _mergedStaticPos = new Vector3[count];
            _mergedStaticRot = new Quaternion[count];
            _mergedStaticScale = new Vector3[count];
            _mergedStaticActive = new bool[count];
            // Sub-meshes are interleaved per system ([quad, trail]) so the combine order
            // matches the original per-system draw order (quads first, then its trail).
            _mergedSubmeshMaterials = new Material[count + trailCount];

            var uniform = true;
            Material firstMat = null;
            var k = 0;
            var submesh = 0;
            for (var i = 0; i < particleSystems.Count; i++)
            {
                var ps = particleSystems[i];
                if (ps == null) continue;

                _mergedSystems[k] = ps;
                _mergedMainEmitters[k] = parent.sourceTopology.ParentOf(ps);

                var binding = ParticleSourceBinding.Acquire(ps);
                if (binding == null) throw new InvalidOperationException("A planned particle output lost its renderer.");
                _sourceBindings[k] = binding;
                _mergedPsRenderers[k] = binding.renderer;
                _particleSystem = ps;

                _mergedPsRenderers[k].GetSharedMaterials(s_Materials);
                _mergedMaterials[k] = 0 < s_Materials.Count ? s_Materials[0] : null;
                var spriteTexture = ParticleSourceInfo.SpriteSheetTexture(ps);
                _mergedTextures[k] = spriteTexture != null ? spriteTexture
                    : _mergedMaterials[k] != null ? _mergedMaterials[k].mainTexture : null;
                if (firstMat == null)
                {
                    firstMat = _mergedMaterials[k];
                }
                else if (_mergedMaterials[k] != firstMat)
                {
                    uniform = false;
                }

                _mergedQuadIdx[k] = submesh;
                _mergedSubmeshMaterials[submesh++] = _mergedMaterials[k];

                // [FxUIEffects 刀7] Trail-enabled systems get an extra trail sub-mesh here
                // instead of a separate CanvasEffectOutput (original merged mode).
                _mergedTrailIdx[k] = -1;
                if (ps.trails.enabled)
                {
                    _mergedIsTrail[k] = true;
                    _mergedTrailIdx[k] = submesh;
                    var trailMat = 1 < s_Materials.Count ? s_Materials[1] : _mergedMaterials[k];
                    _mergedSubmeshMaterials[submesh++] = trailMat;
                    if (trailMat != firstMat) uniform = false;
                }
                s_Materials.Clear();


                _particleSystem = ps;
                k++;
            }

            _mergedUniform = uniform;
            _isTrail = false;
            _mainEmitter = null;
            _particleSystem = 0 < count ? _mergedSystems[0] : null;
            _renderer = 0 < count ? _mergedPsRenderers[0] : null;
            material = firstMat;
            _boundTexture = mainTexture;

            canvasRenderer.SetTexture(null);

            enabled = true;
            // Binding must establish a valid clipping state even when the parent maskable value did not change.
            RecalculateClipping();
        }

        private readonly struct ParticleTick
        {
            internal readonly bool merged, simulationOnly;
            internal readonly float scaledSeconds, unscaledSeconds;
            internal ParticleTick(bool merged, bool simulationOnly, float scaled, float unscaled)
            { this.merged = merged; this.simulationOnly = simulationOnly; scaledSeconds = scaled; unscaledSeconds = unscaled; }
            internal float Seconds(ParticleSystem source) => source.main.useUnscaledTime ? unscaledSeconds : scaledSeconds;
        }

        public void UpdateMesh(Camera view)
        {
            if (isBridge) { UpdateBridge(view); return; }
            if (!TryScheduleCapture(view, out var tick)) return;
            using (UIEffectProfiler.Measure(1)) AdvanceSources(tick);
            if (tick.simulationOnly || !_parent || !isActiveAndEnabled) return;

            bool direct = false;
            using (UIEffectProfiler.Measure(2))
            {
                var worldToOutput = transform.worldToLocalMatrix;
                if (tick.merged)
                {
                    EnsureMergedMeshes();
                    for (int sourceIndex = 0; sourceIndex < _sourceBindings.Length; ++sourceIndex)
                    {
                        CaptureSlot(_mergedQuadIdx[sourceIndex], _sourceBindings[sourceIndex], false, worldToOutput, view);
                        CaptureSlot(_mergedTrailIdx[sourceIndex], _sourceBindings[sourceIndex], true, worldToOutput, view);
                    }
                }
                else
                {
                    var mapping = OutputMapping(_particleSystem, _isTrail, worldToOutput);
                    direct = mapping.Equals(Matrix4x4.identity);
                    Mesh destination = direct ? particleMesh : IntermediateBakeMesh();
                    ParticleGeometry.Capture(_particleSystem, _renderer, _isTrail, view, destination);
                    ValidateCapturedMesh(destination);
                    if (!direct) _singleCombine[0] = new CombineInstance { mesh = destination, transform = mapping };
                    else UIEffectProfiler.current.directBakeOps++;
                }
            }
            using (UIEffectProfiler.Measure(3))
            {
                if (!ComposeGeometry(tick.merged, direct)) return;
                _geometry.ScanModifiers(this);
                if (!_geometry.Finish(particleMesh, UIEffectSettings.ConvertVertexColors(canvas), MaxUiVertices, out _lastBounds))
                    ReportVertexLimit(MaxUiVertices + 1);
            }
            if (!_parent || !isActiveAndEnabled) return;
            UpdateMaterialProperties();
            if (!_parent || !isActiveAndEnabled) return;
            using (UIEffectProfiler.Measure(4)) PublishGeometry();
            _lastBakeFrame = Time.frameCount;
            if (!UIEffectRenderer.staticMeshCache) return;
            if (tick.merged) TakeStaticSnapshotMerged(); else TakeStaticSnapshotSingle();
        }

        private bool TryScheduleCapture(Camera view, out ParticleTick tick)
        {
            tick = default;
            bool merged = isMerged;
            bool bound = merged ? _sourceBindings != null && _sourceBindings.Length > 0 : _sourceBinding != null && _particleSystem;
            bool ready = isActiveAndEnabled && _parent && canvas && canvasRenderer && view && bound;
            if (ready) ready = _parent.canSimulate && EffectScale.HasVolume(Vector3.Scale(transform.lossyScale, _parent.calculatedScale));
            if (ready && !merged) ready = _particleSystem.gameObject.activeInHierarchy;
            if (!ready)
            {
                if (!_meshCleared) ClearMeshAndReplicas();
                return false;
            }
            if (UIEffectRenderer.earlyCull > 1 && FullCullHidden) return false;
            if (UIEffectRenderer.staticMeshCache && _staticValid && EvaluateStaticFrameCached(merged)) return false;
            bool simulationOnly = ShouldSkipCulledBake();
            if (simulationOnly) _staticValid = false;
            if (!merged && _isTrail && Application.isPlaying && !_forceBake && !simulationOnly)
            {
                var body = _parent.GetRendererIfExists(_index - 1);
                if (body && body._particleSystem == _particleSystem && body._lastBakeFrame != Time.frameCount) return false;
            }
            if (!AdvanceBakeClock(out float scaled, out float unscaled)) return false;
            tick = new ParticleTick(merged, simulationOnly, scaled, unscaled);
            _meshCleared = false;
            return true;
        }

        private void AdvanceSources(ParticleTick tick)
        {
            if (!tick.merged)
            {
                _sourceBinding.EnsureVertexContract();
                if (!_isTrail && !_mainEmitter) _sourceBinding.simulation.Advance(_particleSystem, _parent, tick.Seconds(_particleSystem));
                return;
            }
            for (int index = 0; index < _sourceBindings.Length; ++index)
            {
                var binding = _sourceBindings[index];
                if (binding == null || !binding.source || !binding.source.gameObject.activeInHierarchy) continue;
                binding.EnsureVertexContract();
                if (!_mergedMainEmitters[index]) binding.simulation.Advance(binding.source, _parent, tick.Seconds(binding.source));
            }
        }

        private Matrix4x4 OutputMapping(ParticleSystem source, bool trail, Matrix4x4 worldToOutput) =>
            worldToOutput * ParticleCoordinates.BakeToWorld(source, trail, ParticleCoordinates.Scale(_parent, source),
                _parent.transform.position, _parent.originMode);

        private void CaptureSlot(int slot, ParticleSourceBinding binding, bool trail, Matrix4x4 worldToOutput, Camera view)
        {
            if (slot < 0) return;
            var mesh = _mergedCombines[slot].mesh;
            if (binding == null || !binding.source || !binding.source.gameObject.activeInHierarchy)
            { mesh.Clear(false); return; }
            ParticleGeometry.Capture(binding.source, binding.renderer, trail, view, mesh);
            ValidateCapturedMesh(mesh);
            _mergedCombines[slot].transform = OutputMapping(binding.source, trail, worldToOutput);
        }

        private bool ComposeGeometry(bool merged, bool direct)
        {
            if (direct) return true;
            if (!merged)
            {
                try { particleMesh.CombineMeshes(_singleCombine, true, true); }
                finally { _singleCombine[0].mesh = null; }
                return true;
            }
            long count = 0;
            foreach (var part in _mergedCombines) count += part.mesh.vertexCount;
            if (count > MaxUiVertices)
            {
                UIEffectScheduler.RequestUnmergedFallback(_parent);
                ReportVertexLimit((int)System.Math.Min(int.MaxValue, count));
                ClearMeshAndReplicas();
                return false;
            }
            particleMesh.CombineMeshes(_mergedCombines, _mergedUniform, true);
            return true;
        }

        private void ValidateCapturedMesh(Mesh mesh)
        {
            if (mesh.vertexCount <= MaxUiVertices) return;
            ReportVertexLimit(mesh.vertexCount);
            mesh.Clear(false);
        }

        private void PublishGeometry()
        {
            var consumers = Buffers<CanvasEffectOutput>.Rent();
            try
            {
                if (_parent.useMeshSharing) UIEffectScheduler.GetGroupedRenderers(_parent.sharingGroup, _index, consumers);
                foreach (var output in consumers)
                {
                    if (!output || output == this || !output.isActiveAndEnabled || !output._parent || !output._parent.canRender) continue;
                    output.AcceptGeometry(particleMesh, _lastBounds);
                }
                if (_parent && _parent.canRender) AcceptGeometry(particleMesh, _lastBounds);
                else ClearCanvas();
            }
            finally { Buffers<CanvasEffectOutput>.Return(ref consumers); }
        }
        private void AcceptGeometry(Mesh mesh, Bounds bounds)
        {
            canvasRenderer.SetMesh(mesh);
            _lastBounds = bounds;
            SetCanvasRendererMaterials(canvasRenderer);
            UIEffectProfiler.current.setMeshOps++;
        }

        private bool EvaluateStaticFrameCached(bool merged)
        {
            if (_staticFrameCacheFrame == Time.frameCount && _staticFrameCacheMerged == merged)
                return _staticFrameCacheValue;
            _staticFrameCacheFrame = Time.frameCount;
            _staticFrameCacheMerged = merged;
            _staticFrameCacheValue = merged ? IsStaticFrameMerged() : IsStaticFrameSingle();
            return _staticFrameCacheValue;
        }

        // [FxUIEffects 刀6] 静态网格缓存:比较自上次烘焙以来一切影响网格输出的状态。
        // 暂停信号必须是 _parent.isPaused(UIEffectRenderer 标志,Simulate 据此传 dt=0);
        // ps.isPaused 在 Set/SetMerged 绑定时就被 Pause() 恒置真(手动模拟驱动),不可用。
        private bool IsStaticFrameSingle()
        {
            if (!_parent.isPaused || _particleSystem == null) return false;

            var t = _particleSystem.transform;
            if ((t.position - _staticPsPos).sqrMagnitude > 1e-8f) return false;
            if (Mathf.Abs(Quaternion.Dot(t.rotation, _staticPsRot)) < 1f - 1e-6f) return false;
            if ((t.lossyScale - _staticPsScale).sqrMagnitude > 1e-8f) return false;

            return IsStaticFrameCommon();
        }

        private bool IsStaticFrameMerged()
        {
            if (!_parent.isPaused) return false;

            for (var k = 0; k < _mergedSystems.Length; k++)
            {
                if (_mergedSystems[k] == null) return false;
                if (_mergedSystems[k].gameObject.activeInHierarchy != _mergedStaticActive[k]) return false;
                var t = _mergedSystems[k].transform;
                if ((t.position - _mergedStaticPos[k]).sqrMagnitude > 1e-8f) return false;
                if (Mathf.Abs(Quaternion.Dot(t.rotation, _mergedStaticRot[k])) < 1f - 1e-6f) return false;
                if ((t.lossyScale - _mergedStaticScale[k]).sqrMagnitude > 1e-8f) return false;
            }

            return IsStaticFrameCommon();
        }

        private bool IsStaticFrameCommon()
        {
            if (canvas == null || _parent.hasMaterialPropertyBindings) return false;
            if (_parent.calculatedScale != _staticParticleScale
                || _parent.originMode != _staticPositionMode
                || _parent.bakeViewRevision != _staticViewRevision
                || (UIEffectSettings.ConvertVertexColors(canvas)) != _staticGamma)
                return false;
            var rt = canvasRenderer.transform;
            if ((rt.position - _staticRenPos).sqrMagnitude > 1e-8f) return false;
            if (Mathf.Abs(Quaternion.Dot(rt.rotation, _staticRenRot)) < 1f - 1e-6f) return false;
            if ((rt.lossyScale - _staticRenScale).sqrMagnitude > 1e-8f) return false;
            if ((_parent.parentScale - _staticParentScale).sqrMagnitude > 1e-8f) return false;

            var rootT = canvas.rootCanvas.transform;
            if ((rootT.localScale - _staticRootScale).sqrMagnitude > 1e-8f) return false;
            if (new Vector2Int(Screen.width, Screen.height) != _staticScreen) return false;
            if (!Mathf.Approximately(canvas.scaleFactor, _staticCanvasScale)) return false;

            return true;
        }

        private void TakeStaticSnapshotSingle()
        {
            var t = _particleSystem.transform;
            _staticPsPos = t.position;
            _staticPsRot = t.rotation;
            _staticPsScale = t.lossyScale;
            TakeStaticSnapshotCommon();
        }

        private void TakeStaticSnapshotMerged()
        {
            for (var k = 0; k < _mergedSystems.Length; k++)
            {
                if (!_mergedSystems[k]) continue;
                var t = _mergedSystems[k].transform;
                _mergedStaticPos[k] = t.position;
                _mergedStaticRot[k] = t.rotation;
                _mergedStaticScale[k] = t.lossyScale;
                _mergedStaticActive[k] = _mergedSystems[k].gameObject.activeInHierarchy;
            }

            TakeStaticSnapshotCommon();
        }

        private void TakeStaticSnapshotCommon()
        {
            var rt = canvasRenderer.transform;
            _staticRenPos = rt.position;
            _staticRenRot = rt.rotation;
            _staticRenScale = rt.lossyScale;
            _staticParentScale = _parent.parentScale;
            _staticRootScale = canvas.rootCanvas.transform.localScale;
            _staticScreen = new Vector2Int(Screen.width, Screen.height);
            _staticCanvasScale = canvas.scaleFactor;
            _staticParticleScale = _parent.calculatedScale;
            _staticPositionMode = _parent.originMode;
            _staticViewRevision = _parent.bakeViewRevision;
            _staticGamma = UIEffectSettings.ConvertVertexColors(canvas);
            _staticValid = true;
        }

        // [FxUIEffects 刀8] FastPath 空闲预判定:UIEffectRenderer.UpdateRenderers 据此跳过
        // GetBakeCamera 与本渲染器的 UpdateMesh 调用链。条件集是 UpdateMesh 内部短路
        // 条件的严格子集且每帧重新评估(无跨帧缓存),命中时与内部立即 return 等价;
        // 未命中时照常进入 UpdateMesh(清空检查/裁剪/烘焙门控全部照旧)。
        // 不以 _meshCleared 作为空闲条件:恢复显示后仍需进入 UpdateMesh 重烘。
        internal bool IsIdleForFastPath()
        {
            if (_parent == null || !isActiveAndEnabled) return true;
            if (_mergedSystems == null && (_particleSystem == null || !_particleSystem.gameObject.activeInHierarchy))
                return false;

            // [刀6] 静态网格缓存命中:暂停且相关 Transform/Canvas 全部未变。
            if (UIEffectRenderer.staticMeshCache && _staticValid
                && EvaluateStaticFrameCached(_mergedSystems != null))
            {
                return true;
            }

            // [刀5] FullCull:CanvasGroup 累积 alpha≈0(整页隐藏)。恢复可见后预判定
            // 立即失效,清空/重烘由 UpdateMesh 内部逻辑接管。
            if (UIEffectRenderer.earlyCull > 1 && canvasRenderer && FullCullHidden)
            {
                return true;
            }

            return false;
        }

        private void SetCanvasRendererMaterials(CanvasRenderer cr)
        {
            if (_spriteMask != null && _spriteMask.blocked)
            {
                cr.materialCount = 0;
                _materialsDirty = true;
                return;
            }
            var firstMaterial = materialForRendering;
            var count = _mergedSystems == null || _mergedUniform ? 1 : _mergedSubmeshMaterials.Length;
            if (!_materialsDirty && _submittedMaterial == firstMaterial && cr.materialCount == count) return;
            if (count == 1)
            {
                cr.materialCount = 1;
                cr.SetMaterial(firstMaterial, 0);
            }
            else
            {
                cr.materialCount = count;
                for (var i = 0; i < count; i++)
                    cr.SetMaterial(i == 0 ? firstMaterial : _mergedSubmeshMaterials[i], i);
            }
            UIEffectProfiler.current.materialUpdates++;
            _submittedMaterial = firstMaterial;
            _materialsDirty = false;
        }

        private void ClearCanvas()
        {
            if (isBridge) InvalidateBridgeCache();
            canvasRenderer.Clear();
            _materialsDirty = true;
            _submittedMaterial = null;
        }

        protected override void UpdateMaterial()
        {
            if (!IsActive()) return;
            SetCanvasRendererMaterials(canvasRenderer);
            canvasRenderer.SetTexture(mainTexture);
        }

        public override void SetMaterialDirty()
        {
            _materialsDirty = true;
            InvalidateResolvedMaterial();
            InvalidateMeshCache();
            base.SetMaterialDirty();
        }

        // The scheduler submits captured geometry; Graphic's generated quad is unused.
        protected override void UpdateGeometry()
        {
        }

        public override void Cull(Rect clipRect, bool validRect)
        {
            bool outside = !validRect || !clipRect.Overlaps(rootCanvasRect, true);
            bool hidden = outside || _lastBounds.extents == Vector3.zero;
            bool recovered = !outside && (_uguiClipCulled || canvasRenderer.cull);
            _uguiClipCulled = UIEffectRenderer.earlyCull > 0 && outside;
            if (recovered) { InvalidateMeshCache(); _nextCullProbeTime = 0; }
            if (canvasRenderer.cull == hidden) return;
            canvasRenderer.cull = hidden;
            UISystemProfilerApi.AddMarker("Canvas effect visibility", this);
            // UGUI must receive the state transition even if a caller's event handler throws.
            try { onCullStateChanged.Invoke(hidden); }
            finally { OnCullingChanged(); }
        }

        private Vector3 GetWorldScale() => ParticleCoordinates.Scale(_parent, _particleSystem);



        private void UpdateMaterialProperties()
        {
            if (!_parent.hasMaterialPropertyBindings) return;
            _propertySynchronizer ??= new MaterialPropertySynchronizer();
            _propertySynchronizer.Apply(isBridge ? _bridgeSource : _renderer,
                materialForRendering, _parent.propertyBindings, isBridge ? _bridgeMaterialSlot : -1);
        }
    }
}
