using System;
using System.Collections.Generic;
using ShanFlyer.UIEffects.Internal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ShanFlyer.UIEffects
{
    /// <summary>Stencil contract for shaders used by the Canvas SpriteMask bridge.</summary>
    public static class UIEffectSpriteMask
    {
        private static readonly HashSet<Shader> s_Shaders = new HashSet<Shader>();

        /// <summary>
        /// Opt in an existing shader after verifying that its rendered pass uses Ref [_Stencil],
        /// Comp [_StencilComp], Pass [_StencilOp], ReadMask [_StencilReadMask],
        /// WriteMask [_StencilWriteMask], and Keep for Fail/ZFail. Properties alone are not enough.
        /// Call at startup in players as well as in the Editor. No source material is modified.
        /// </summary>
        public static void RegisterStencilShader(Shader shader)
        {
            if (!shader) throw new ArgumentNullException(nameof(shader));
            s_Shaders.Add(shader);
        }

        internal static bool Supports(Material material)
        {
            if (!material || !material.shader) return false;
            var shader = material.shader;
            // Packaged and generated shaders declare the contract; other shaders register explicitly.
            if (shader.name != "ShanFlyer/UI Effects/Additive" && shader.name != "UI/Default"
                && !s_Shaders.Contains(shader) && material.GetTag("UIEffectsHPStencil", false) != "1") return false;
            return material.HasProperty("_Stencil") && material.HasProperty("_StencilComp")
                && material.HasProperty("_StencilOp") && material.HasProperty("_StencilReadMask")
                && material.HasProperty("_StencilWriteMask");
        }
    }

    internal static class SpriteMaskResolver
    {
        private static SpriteMask[] s_Masks;
        private static readonly Dictionary<Renderer, List<SpriteMask>> s_Resolved =
            new Dictionary<Renderer, List<SpriteMask>>();
        private static readonly Stack<List<SpriteMask>> s_Lists = new Stack<List<SpriteMask>>();

        // One scene query per HP update, lazy: effects without masking do not pay for it.
        internal static void BeginFrame()
        {
            s_Masks = null;
            foreach (var result in s_Resolved.Values) { result.Clear(); s_Lists.Push(result); }
            s_Resolved.Clear();
        }

        internal static SortingGroup Scope(Transform transform)
        {
            for (var t = transform; t; t = t.parent)
                if (t.TryGetComponent<SortingGroup>(out var group) && group.isActiveAndEnabled)
                    return group;
            return null;
        }

        internal static int Compare(int layerA, int orderA, int layerB, int orderB)
        {
            var layer = SortingLayer.GetLayerValueFromID(layerA).CompareTo(SortingLayer.GetLayerValueFromID(layerB));
            return layer != 0 ? layer : orderA.CompareTo(orderB);
        }

        internal static SpriteMaskInteraction Interaction(Renderer source) => source is ParticleSystemRenderer particle
            ? particle.maskInteraction : source is SpriteRenderer sprite ? sprite.maskInteraction : SpriteMaskInteraction.None;

        internal static bool Affects(SpriteMask mask, Renderer renderer)
        {
            if (!mask || !mask.enabled || !mask.gameObject.activeInHierarchy || !mask.sprite || !renderer) return false;
            if (SpriteMaskNativeRendering.WasForceRenderingOff(mask)) return false;
            var maskScope = Scope(mask.transform);
            var scope = Scope(renderer.transform);
            var layer = renderer.sortingLayerID;
            var order = renderer.sortingOrder;
            // Ancestor/global masks can affect a nested group as one sorted object. A mask
            // local to a sibling/descendant group cannot escape that group. sortAtRoot skips
            // the enclosing group, matching Unity's nested SortingGroup sorting boundary.
            while (scope != maskScope)
            {
                if (!scope) return false;
                layer = scope.sortingLayerID;
                order = scope.sortingOrder;
                scope = scope.sortAtRoot ? null : Scope(scope.transform.parent);
            }
            return !mask.isCustomRangeActive
                || (Compare(layer, order, mask.backSortingLayerID, mask.backSortingOrder) > 0
                    && Compare(layer, order, mask.frontSortingLayerID, mask.frontSortingOrder) <= 0);
        }

        internal static void Resolve(Renderer renderer, List<SpriteMask> results)
        {
            results.Clear();
            if (!renderer || Interaction(renderer) == SpriteMaskInteraction.None) return;
            if (s_Resolved.TryGetValue(renderer, out var cached))
            {
                results.AddRange(cached);
                UIEffectProfiler.current.maskResolveCacheHits++;
                return;
            }
            if (s_Masks == null)
            {
#if UNITY_6000_4_OR_NEWER
                s_Masks = UnityEngine.Object.FindObjectsByType<SpriteMask>();
#else
                s_Masks = UnityEngine.Object.FindObjectsByType<SpriteMask>(FindObjectsSortMode.None);
#endif
            }
            foreach (var mask in s_Masks)
                if (Affects(mask, renderer)) results.Add(mask);
            results.Sort((a, b) => EngineObjects.Identity(a).CompareTo(EngineObjects.Identity(b)));
            var snapshot = s_Lists.Count > 0 ? s_Lists.Pop() : new List<SpriteMask>();
            snapshot.AddRange(results);
            s_Resolved.Add(renderer, snapshot);
        }
    }

    /// <summary>Per-output state: never shared with the simulation owner's stencil materials.</summary>
    internal sealed class SpriteMaskDrawScope : IDisposable
    {
        internal const int Bit = 128;
        private readonly CanvasEffectOutput _owner;
        private readonly List<SpriteMask> _masks = new List<SpriteMask>();
        private readonly List<CanvasSpriteMaskGraphic> _writers = new List<CanvasSpriteMaskGraphic>();
        private CanvasSpriteMaskGraphic _before, _after;
        private Material _material;
        private string _error;
        private int _reference, _readMask;
        private bool _active;
        internal bool blocked { get; private set; }

        internal SpriteMaskDrawScope(CanvasEffectOutput owner) { _owner = owner; }

        internal void InvalidateGeometry()
        {
            foreach (var writer in _writers) if (writer) writer.InvalidateGeometry();
        }

        internal void Prepare(Renderer source, UIEffectRenderer parent, Matrix4x4 worldToOutput)
        {
            var previousBlocked = blocked;
            var previousActive = _active;
            var oldReference = _reference;
            var oldReadMask = _readMask;
            blocked = false;
            _active = false;
            string error = null;
            // Do not leave active stencil writers behind while their output is hidden.
            // Re-resolve on visibility recovery; native Trail/particle simulation is separate.
            if (!_owner.canvas || !_owner.canvas.isActiveAndEnabled
                || (Application.isPlaying && UIEffectRenderer.earlyCull > 0 && _owner.alphaHidden))
            {
                SetNodesActive(false);
                if (previousActive) _owner.SetMaterialDirty();
                return;
            }
            SpriteMaskResolver.Resolve(source, _masks);
            var interaction = SpriteMaskResolver.Interaction(source);
            if (interaction != SpriteMaskInteraction.None)
            {
                if (_masks.Count == 0)
                    blocked = interaction == SpriteMaskInteraction.VisibleInsideMask;
                else
                {
                    var depth = MaskUtilities.GetStencilDepth(_owner.transform,
                        MaskUtilities.FindRootSortOverrideCanvas(_owner.transform));
                    if (depth >= 8)
                        error = "SpriteMask needs one free stencil bit; eight parent UGUI Masks already use all bits.";
                    else if (!UIEffectSpriteMask.Supports(_owner.material))
                        error = "Shader '" + (_owner.material ? _owner.material.shader.name : "<null>")
                            + "' has no verified UI stencil contract. Keep its shading; add only stencil state if missing,"
                            + " then call UIEffectSpriteMask.RegisterStencilShader after auditing its pass.";
                    else if (!CanvasSpriteMaskGraphic.shader)
                        error = "Missing Resources/UIEffectSpriteMask shader.";
                    else
                    {
                        // All masks write the SAME bit: their overlap is a union, not parity/intersection.
                        var parentBits = _owner.maskable ? (1 << depth) - 1 : 0;
                        _reference = parentBits | (interaction == SpriteMaskInteraction.VisibleInsideMask ? Bit : 0);
                        _readMask = parentBits | Bit;
                        _active = parent.canRender && source.gameObject.activeInHierarchy;
                        if (_active)
                        {
                            EnsureNodes(parent);
                            SetNodesActive(true);
                            _before.Configure(null, Matrix4x4.identity, 0, 0, Bit, true);
                            for (var i = 0; i < _masks.Count; i++)
                                _writers[i].Configure(_masks[i], worldToOutput * _masks[i].transform.localToWorldMatrix,
                                    parentBits | Bit, parentBits, Bit, false);
                            _after.Configure(null, Matrix4x4.identity, 0, 0, Bit, true);
                            ArrangeNodes();
                        }
                    }
                }
            }
            if (error != null)
            {
                blocked = true;
                if (_error != error) Debug.LogError("[UIEffectRenderer SpriteMask] " + error + " Output suppressed.", parent);
            }
            _error = error;
            SetNodesActive(_active);
            if (previousBlocked != blocked || previousActive != _active || oldReference != _reference || oldReadMask != _readMask)
                _owner.SetMaterialDirty();
        }

        private void EnsureNodes(UIEffectRenderer parent)
        {
            if (!_before) _before = CanvasSpriteMaskGraphic.Create(parent, "Initialize");
            if (!_after) _after = CanvasSpriteMaskGraphic.Create(parent, "Clear");
            while (_writers.Count < _masks.Count)
                _writers.Add(CanvasSpriteMaskGraphic.Create(parent, "Write"));
        }

        private void ArrangeNodes()
        {
            // Each interval is contiguous in Canvas hierarchy order, including trails and replicas.
            var ownerIndex = _owner.transform.GetSiblingIndex();
            var ordered = _before.transform.GetSiblingIndex() == ownerIndex - _masks.Count - 1
                && _after.transform.GetSiblingIndex() == ownerIndex + 1;
            for (var i = 0; ordered && i < _masks.Count; i++)
                ordered &= _writers[i].transform.GetSiblingIndex() == ownerIndex - _masks.Count + i;
            if (ordered) return;
            PlaceBefore(_before.transform, _owner.transform);
            for (var i = 0; i < _masks.Count; i++) PlaceBefore(_writers[i].transform, _owner.transform);
            var target = _owner.transform.GetSiblingIndex();
            if (_after.transform.GetSiblingIndex() != target + 1)
            {
                var index = _after.transform.GetSiblingIndex() < target ? target : target + 1;
                _after.transform.SetSiblingIndex(index);
            }
        }

        private static void PlaceBefore(Transform node, Transform target)
        {
            var index = target.GetSiblingIndex();
            if (node.GetSiblingIndex() < index) index--;
            if (node.GetSiblingIndex() != index) node.SetSiblingIndex(index);
        }

        private void SetNodesActive(bool active)
        {
            SetActive(_before, active);
            SetActive(_after, active);
            for (var i = 0; i < _writers.Count; i++)
                SetActive(_writers[i], active && i < _masks.Count);
        }

        private static void SetActive(CanvasSpriteMaskGraphic node, bool active)
        {
            if (node && node.gameObject.activeSelf != active) node.gameObject.SetActive(active);
        }

        internal Material Modify(Material source)
        {
            if (!_active || !source) return source;
            if (!_material || _material.shader != source.shader)
            {
                EngineObjects.Destroy(_material);
                _material = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
            }
            _material.CopyPropertiesFromMaterial(source);
            ApplyStencilState(_material);
            return _material;
        }

        internal void ApplyStencilState(Material material)
        {
            if (!_active || !material) return;
            material.SetInt("_Stencil", _reference);
            material.SetInt("_StencilComp", (int)CompareFunction.Equal);
            material.SetInt("_StencilOp", (int)StencilOp.Keep);
            material.SetInt("_StencilReadMask", _readMask);
            material.SetInt("_StencilWriteMask", 0);
        }

        public void Dispose()
        {
            ReleaseNode(_before);
            ReleaseNode(_after);
            foreach (var node in _writers) ReleaseNode(node);
            _writers.Clear();
            EngineObjects.Destroy(_material);
            _material = null;
            _active = blocked = false;
        }

        private static void ReleaseNode(CanvasSpriteMaskGraphic node)
        {
            if (!node) return;
            node.canvasRenderer.Clear();
            node.enabled = false;
            // Reset/OnDisable can run while Unity is deactivating the entire parent. Destroying
            // its siblings immediately is illegal then; clear now, dispose after the callback.
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (node) UnityEngine.Object.DestroyImmediate(node.gameObject);
                };
                return;
            }
#endif
            UnityEngine.Object.Destroy(node.gameObject);
        }
    }
}
