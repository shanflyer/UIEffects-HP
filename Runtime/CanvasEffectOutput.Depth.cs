using UnityEngine;

namespace ShanFlyer.UIEffects
{
    internal partial class CanvasEffectOutput
    {
        private CanvasDepthResetGraphic _depthBefore, _depthAfter;
        internal bool depthStart, depthEnd;
        internal Bounds depthGroupBounds;
        internal Bounds depthWorldBounds => EffectScale.TransformBounds(_lastBounds, transform.localToWorldMatrix);
        internal bool isDepthMesh => isBridge && IsMeshSource(_bridgeSource);
        internal bool canJoinDepthGroup => _parent && _parent.supportsCanvasRendering && _parent.canRender
            && _bridgeSource && _bridgeSource.enabled && _bridgeSource.gameObject.activeInHierarchy
            && !_originalForceRenderingOff && _bridgeOutput && _bridgeOutput.vertexCount > 0
            && canvasRenderer.materialCount > 0 && !canvasRenderer.cull && !alphaHidden;

        internal void ApplyDepthBoundaries()
        {
            bool active = isActiveAndEnabled && isDepthMesh && canJoinDepthGroup;
            ApplyBoundary(ref _depthBefore, active && depthStart, true);
            ApplyBoundary(ref _depthAfter, active && depthEnd, false);
        }

        private void ApplyBoundary(ref CanvasDepthResetGraphic reset, bool active, bool before)
        {
            if (!active)
            {
                if (reset && reset.enabled) reset.StopDrawing();
                return;
            }
            if (!reset) reset = CanvasDepthResetGraphic.Create(transform.parent, gameObject.layer);
            reset.Place(this, before);
            reset.Submit(canvas.rootCanvas, depthGroupBounds);
        }

        private void ReleaseDepthBoundaries()
        {
            depthStart = depthEnd = false;
            CanvasDepthResetGraphic.Release(ref _depthBefore);
            CanvasDepthResetGraphic.Release(ref _depthAfter);
        }
    }
}
