using UnityEngine;

namespace ShanFlyer.UIEffects
{
    public partial class UIEffectRenderer
    {
        private bool _nativeCanvasBypass;

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
            if (!isActiveAndEnabled) return;
            RefreshCanvasSupport();
            _rebuildRenderers = true;
        }
    }
}
