using UnityEngine;

namespace ShanFlyer.UIEffects
{
    internal static class EffectUnitScale
    {
        internal static float Calculate(Canvas canvas, Camera reference, Vector3 pivot)
        {
            if (!canvas) return 1;
            var root = canvas.rootCanvas;
            // World-space UI already uses world units; its Transform is applied normally.
            if (root.renderMode == RenderMode.WorldSpace) return 1;
            var fallback = root.referencePixelsPerUnit;
            if (!float.IsFinite(fallback) || fallback <= 0) fallback = 100;
            var camera = reference ? reference
                : root.renderMode == RenderMode.ScreenSpaceCamera ? root.worldCamera : null;
            if (!camera) return fallback;
            double pixels = PixelsPerWorldUnit(camera, pivot);
            double canvasPixels = root.scaleFactor;
            if (root.renderMode == RenderMode.ScreenSpaceCamera && root.worldCamera)
                canvasPixels = PixelsPerWorldUnit(root.worldCamera, pivot)
                    * root.transform.TransformVector(Vector3.up).magnitude;
            // Use the actual Canvas plane conversion: scaleFactor alone describes its
            // plane, and would apply perspective depth twice for an offset effect root.
            if (!(canvasPixels > 0) || double.IsInfinity(canvasPixels)) return fallback;
            double units = pixels / canvasPixels;
            return units > 0 && units <= float.MaxValue ? (float)units : fallback;
        }

        private static double PixelsPerWorldUnit(Camera camera, Vector3 pivot)
        {
            double pixels = camera.pixelHeight * System.Math.Abs((double)camera.projectionMatrix.m11) * .5;
            if (camera.orthographic) return pixels;
            float depth = -camera.worldToCameraMatrix.MultiplyPoint3x4(pivot).z;
            return float.IsFinite(depth) && depth >= Mathf.Max(.0001f, camera.nearClipPlane) ? pixels / depth : 0;
        }
    }
}
