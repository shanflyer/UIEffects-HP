using UnityEngine;

namespace ShanFlyer.UIEffects
{
    public partial class UIEffectRenderer
    {
        private bool _nativeCanvasBypass;
        [SerializeField, HideInInspector] private Canvas _generatedEffectCanvas;
        private bool _creatingEffectCanvas;

        private void EnsureEffectCanvas()
        {
            if (_creatingEffectCanvas || TryGetComponent<Canvas>(out _)) return;
            var ancestor = transform.parent ? transform.parent.GetComponentInParent<Canvas>() : null;
            if (!ancestor || ancestor.rootCanvas.renderMode == RenderMode.WorldSpace) return;
            _creatingEffectCanvas = true;
            try
            {
                // A nested Canvas owns its vertex layout without changing the parent.
                // Inherit sorting so parent Mask/RectMask2D and hierarchy order still apply.
                _generatedEffectCanvas = gameObject.AddComponent<Canvas>();
                _generatedEffectCanvas.overrideSorting = false;
                _generatedEffectCanvas.additionalShaderChannels = ancestor.additionalShaderChannels;
                _generatedEffectCanvas.vertexColorAlwaysGammaSpace = ancestor.vertexColorAlwaysGammaSpace;
            }
            finally { _creatingEffectCanvas = false; }
        }

        internal bool IsChannelIsolationCanvas(Canvas value)
            => value && value == _generatedEffectCanvas && !value.overrideSorting;

        internal void EnsureMeshCanvasChannels()
        {
            var local = canvas;
            // Never widen a shared/root Canvas, including when the component is
            // mistakenly placed directly on that root instead of below it.
            if (!local || local.transform != transform || local.isRootCanvas) return;
            var needed = AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            if (local.rootCanvas.renderMode == RenderMode.ScreenSpaceCamera)
                needed |= AdditionalCanvasShaderChannels.TexCoord1
                    | AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.TexCoord3;
            if ((local.additionalShaderChannels & needed) != needed) local.additionalShaderChannels |= needed;
        }

        /// <summary>World Space and unassigned Camera canvases retain native rendering.
        /// Camera canvases require a dedicated UI camera whose depth can be discarded.</summary>
        public bool supportsCanvasRendering
        {
            get
            {
                if (!canvas) return false;
                var root = canvas.rootCanvas;
                return root.renderMode == RenderMode.ScreenSpaceOverlay
                    || (root.renderMode == RenderMode.ScreenSpaceCamera && root.worldCamera);
            }
        }

        private bool RefreshCanvasSupport()
        {
            EnsureEffectCanvas();
            if (supportsCanvasRendering)
            {
                if (_nativeCanvasBypass)
                {
                    _nativeCanvasBypass = false;
                    _rebuildRenderers = true;
                    SpriteMaskNativeRendering.Register(this);
                }
                return true;
            }
            if (_nativeCanvasBypass && _activeRendererCount == 0) return false;
            _nativeCanvasBypass = true;
#if UNITY_EDITOR
            EditorPlayback.Set(this, false);
#endif
            for (int i = 0; i < _renderers.Count; ++i)
                if (_renderers[i]) _renderers[i].Reset(i, restoreNativePlayback: !isPaused);
            _activeRendererCount = 0;
            _simulationOwner = false;
            _outputPlan.Clear();
            _scaleState.Release();
            _bakeView.Dispose();
            ReleaseAutomaticSharingGroup();
            SpriteMaskNativeRendering.Unregister(this);
            _rebuildRenderers = true;
            return false;
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            if (!isActiveAndEnabled || _creatingEffectCanvas) return;
            RefreshCanvasSupport();
            _rebuildRenderers = true;
        }
    }
}
