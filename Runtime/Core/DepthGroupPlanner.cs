using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ShanFlyer.UIEffects
{
    // Reconcile after source sorting and SpriteMask nodes, before Canvas renders.
    // Groups follow draw hierarchy, not source IDs or particle sharing groups.
    internal static class DepthGroupPlanner
    {
        private static readonly HashSet<Canvas> Roots = new HashSet<Canvas>();
        private static readonly List<CanvasEffectOutput> Outputs = new List<CanvasEffectOutput>();
        private static Shader s_Shader;
        internal static Shader ResetShader => s_Shader ? s_Shader : (s_Shader = Resources.Load<Shader>("UIEffectDepthReset"));

        internal static void Refresh(List<UIEffectRenderer> effects)
        {
            UIEffectProfiler.current.meshDepthGroups = 0;
            Roots.Clear();
            Outputs.Clear();
            try
            {
                foreach (var effect in effects)
                {
                    if (!effect || !effect.isActiveAndEnabled) continue;
                    for (int i = 0; i < effect.activeRendererCount; ++i)
                    {
                        var output = effect.GetRendererIfExists(i);
                        if (!output) continue;
                        output.depthStart = output.depthEnd = false;
                        Outputs.Add(output);
                        if (effect.supportsCanvasRendering && output.isDepthMesh && output.isActiveAndEnabled)
                            Roots.Add(effect.canvas.rootCanvas);
                    }
                }
                foreach (var root in Roots)
                {
                    if (!root || !root.isActiveAndEnabled) continue;
                    CanvasEffectOutput first = null, last = null;
                    var bounds = new Bounds();
                    Visit(root.transform, root.transform, ref first, ref last, ref bounds);
                    EndGroup(ref first, ref last, bounds);
                }
                // Do not mutate the hierarchy during traversal. Retain helpers between
                // frames and only reposition / resubmit them when something changes.
                foreach (var output in Outputs)
                    if (output) output.ApplyDepthBoundaries();
            }
            finally { Roots.Clear(); Outputs.Clear(); }
        }

        private static void EndGroup(ref CanvasEffectOutput first, ref CanvasEffectOutput last, Bounds bounds)
        {
            if (first)
            {
                first.depthStart = true;
                first.depthGroupBounds = bounds;
                ++UIEffectProfiler.current.meshDepthGroups;
            }
            if (last) { last.depthEnd = true; last.depthGroupBounds = bounds; }
            first = last = null;
        }

        private static void Visit(Transform node, Transform root, ref CanvasEffectOutput first, ref CanvasEffectOutput last, ref Bounds bounds)
        {
            if (!node.gameObject.activeInHierarchy || node.TryGetComponent<CanvasDepthResetGraphic>(out _)) return;
            bool canvasBoundary = node != root && node.TryGetComponent<Canvas>(out var nested) && nested.isActiveAndEnabled;
            bool maskBoundary = (node.TryGetComponent<Mask>(out var mask) && mask.isActiveAndEnabled)
                || (node.TryGetComponent<RectMask2D>(out var rectMask) && rectMask.isActiveAndEnabled);
            bool boundary = canvasBoundary || maskBoundary;
            if (boundary) EndGroup(ref first, ref last, bounds);

            if (node.TryGetComponent<CanvasEffectOutput>(out var output) && output.isActiveAndEnabled)
            {
                if (output.isDepthMesh && output.canJoinDepthGroup)
                {
                    if (!first) { first = output; bounds = output.depthWorldBounds; }
                    else bounds.Encapsulate(output.depthWorldBounds);
                    last = output;
                }
                else EndGroup(ref first, ref last, bounds);
            }
            else if (node.TryGetComponent<Graphic>(out var graphic))
            {
                // UIEffectRenderer is a non-drawing container. Normal UI, including
                // SpriteMask's generated stencil draws, separates depth groups.
                if (graphic.isActiveAndEnabled && !(graphic is UIEffectRenderer)) EndGroup(ref first, ref last, bounds);
            }
            else if (node.TryGetComponent<CanvasRenderer>(out var renderer) && renderer.materialCount > 0)
                EndGroup(ref first, ref last, bounds);

            for (int i = 0; i < node.childCount; ++i) Visit(node.GetChild(i), root, ref first, ref last, ref bounds);
            if (boundary) EndGroup(ref first, ref last, bounds);
        }
    }
}
