using System.Collections.Generic;
using UnityEngine;

namespace ShanFlyer.UIEffects
{
    public partial class UIEffectRenderer
    {
        [SerializeField, Tooltip("Render owned MeshRenderers, including multiple submeshes/materials, through this Canvas.")]
        private bool m_RenderMeshes = true;
        [SerializeField, Tooltip("Bake owned skinned meshes into UI outputs.")]
        private bool m_RenderSkinnedMeshes = true;
        [SerializeField, Tooltip("Render owned SpriteRenderers through this Canvas, including SpriteMask with compatible stencil materials.")]
        private bool m_RenderSprites = true;
        [SerializeField, Tooltip("Render owned single-material TrailRenderers and LineRenderers through this Canvas.")]
        private bool m_RenderLines = true;
        [SerializeField, Tooltip("Order all outputs by source sorting layer/order, then hierarchy. Disables particle merging to preserve interleaving.")]
        private bool m_SortBySourceOrder;
        private bool _renderMeshesStamp, _renderLinesStamp, _sourceSortStamp, _renderSkinnedStamp, _renderSpritesStamp;
        private readonly List<Renderer> _bridgeSources = new List<Renderer>();
        private readonly Dictionary<Renderer, int> _sourceOrder = new Dictionary<Renderer, int>();
        private readonly List<CanvasEffectOutput> _drawOrder = new List<CanvasEffectOutput>();
        private System.Comparison<CanvasEffectOutput> _compareDrawOrder;

        public bool renderSkinnedMeshes
        {
            get => m_RenderSkinnedMeshes;
            set { if (m_RenderSkinnedMeshes == value) return; m_RenderSkinnedMeshes = value; MarkBindingDirty(); }
        }
        public bool renderSprites
        {
            get => m_RenderSprites;
            set { if (m_RenderSprites == value) return; m_RenderSprites = value; MarkBindingDirty(); }
        }
        private bool BridgeTypeEnabled(Renderer source) => source is MeshRenderer ? m_RenderMeshes
            : source is SkinnedMeshRenderer ? m_RenderSkinnedMeshes : source is SpriteRenderer ? m_RenderSprites
            : (source is LineRenderer || source is TrailRenderer) && m_RenderLines;

        public bool sortBySourceOrder
        {
            get => m_SortBySourceOrder;
            set { if (m_SortBySourceOrder == value) return; m_SortBySourceOrder = value; MarkBindingDirty(); }
        }

        public bool renderMeshes
        {
            get => m_RenderMeshes;
            set { if (m_RenderMeshes == value) return; m_RenderMeshes = value; MarkBindingDirty(); }
        }

        public bool renderLines
        {
            get => m_RenderLines;
            set { if (m_RenderLines == value) return; m_RenderLines = value; MarkBindingDirty(); }
        }

        /// <summary>Call after mutating a Sprite's geometry/UVs in place.</summary>
        public void MarkSpriteMaskDirty()
        {
            for (var i = 0; i < _activeRendererCount; i++)
                if (_renderers[i]) _renderers[i].InvalidateSpriteMaskGeometry();
        }

        // Called by both refresh overloads, including OnEnable with a serialized particle list.
        // Recollect only non-particle sources; never overwrite a caller's particle selection.
        private void CollectBridgeSources()
        {
            _renderMeshesStamp = m_RenderMeshes;
            _renderSkinnedStamp = m_RenderSkinnedMeshes; _renderSpritesStamp = m_RenderSprites;
            _renderLinesStamp = m_RenderLines;
            _sourceSortStamp = m_SortBySourceOrder;
            _bridgeSources.Clear();
            _sourceOrder.Clear();
            if (!m_RenderMeshes && !m_RenderLines && !m_RenderSkinnedMeshes && !m_RenderSprites && !m_SortBySourceOrder) return;
            GetComponentsInChildren(true, _bridgeSources);
            for (var i = 0; i < _bridgeSources.Count; i++) _sourceOrder[_bridgeSources[i]] = i;
            for (var i = _bridgeSources.Count - 1; i >= 0; i--)
            {
                var source = _bridgeSources[i];
                if (!source || source.GetComponentInParent<UIEffectRenderer>(true) != this
#if UNITY_EDITOR
                    || (source.hideFlags & HideFlags.DontSave) != 0 || source.CompareTag("EditorOnly")
#endif
                    || !BridgeTypeEnabled(source)
                    || !CanvasEffectOutput.CanBridge(source))
                    _bridgeSources.RemoveAt(i);
            }
        }

        private void OrderSourceOutputs()
        {
            if (!m_SortBySourceOrder)
            {
                int previous = -1;
                for (int i = 0; i < _activeRendererCount; ++i)
                {
                    if (!_renderers[i]) continue;
                    int sibling = _renderers[i].transform.GetSiblingIndex();
                    if (sibling < previous)
                    {
                        for (int j = 0; j < _activeRendererCount; ++j)
                            if (_renderers[j]) _renderers[j].transform.SetAsLastSibling();
                        break;
                    }
                    previous = sibling;
                }
                return;
            }
            _drawOrder.Clear();
            for (var i = 0; i < _activeRendererCount; i++)
                if (_renderers[i]) _drawOrder.Add(_renderers[i]);
            if (_compareDrawOrder == null) _compareDrawOrder = CompareSourceOrder;
            _drawOrder.Sort(_compareDrawOrder);
            // Keep sharing indices unchanged; only the Canvas draw order changes.
            var ordered = true;
            for (var i = 1; i < _drawOrder.Count; i++)
                if (_drawOrder[i - 1].transform.GetSiblingIndex() > _drawOrder[i].transform.GetSiblingIndex())
                { ordered = false; break; }
            if (ordered) return;
            for (var i = 0; i < _drawOrder.Count; i++)
                _drawOrder[i].transform.SetAsLastSibling();
        }

        private int CompareSourceOrder(CanvasEffectOutput a, CanvasEffectOutput b)
        {
            var left = a.sourceRenderer; var right = b.sourceRenderer;
            if (!left || !right) return a.outputIndex.CompareTo(b.outputIndex);
            var order = SpriteMaskResolver.Compare(left.sortingLayerID, left.sortingOrder, right.sortingLayerID, right.sortingOrder);
            if (order != 0) return order;
            _sourceOrder.TryGetValue(left, out var li); _sourceOrder.TryGetValue(right, out var ri);
            return li != ri ? li.CompareTo(ri) : a.outputIndex.CompareTo(b.outputIndex);
        }

        private void PlanBridgeSources(EffectOutputPlan plan)
        {
            // Preserve existing particle slot indices, including for SimulationRole.
            for (var i = 0; i < _bridgeSources.Count; i++)
                if (CanvasEffectOutput.IsMeshSource(_bridgeSources[i]) || _bridgeSources[i] is SpriteRenderer)
                    plan.AddBridge(_bridgeSources[i]);
            for (var i = 0; i < _bridgeSources.Count; i++)
                if (!CanvasEffectOutput.IsMeshSource(_bridgeSources[i]) && !(_bridgeSources[i] is SpriteRenderer))
                    plan.AddBridge(_bridgeSources[i]);
        }

        // Every member updates its own animated geometry, including Auto non-owners and
        // orphan Replicas. Particle sharing must not freeze independent source animation.
        internal void UpdateBridgeRenderers()
        {
            Camera camera = null;
            for (var i = 0; i < _activeRendererCount; i++)
            {
                var renderer = _renderers[i];
                if (!renderer || !renderer.isBridge) continue;
                if (!camera) camera = GetBakeCamera();
                renderer.UpdateMesh(camera);
            }
        }
    }
}
