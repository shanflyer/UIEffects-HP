using UnityEngine;

namespace ShanFlyer.UIEffects
{
    // Numerical policy for effect coordinate conversion; multiplication uses Unity's Vector3.Scale.
    internal static class EffectScale
    {
        internal static bool IsFinite(Vector3 value) => float.IsFinite(value.x)
            && float.IsFinite(value.y) && float.IsFinite(value.z);
        internal static bool HasVolume(Vector3 value) => IsFinite(value)
            && value.x != 0 && value.y != 0 && value.z != 0;
        internal static Vector3 Reciprocal(Vector3 value) => new Vector3(
            ReciprocalAxis(value.x), ReciprocalAxis(value.y), ReciprocalAxis(value.z));
        internal static Matrix4x4 ScaleAndOffset(Vector3 scale, Vector3 offset)
        {
            var result = Matrix4x4.identity;
            result.m00 = scale.x; result.m11 = scale.y; result.m22 = scale.z;
            result.m03 = offset.x; result.m13 = offset.y; result.m23 = offset.z;
            return result;
        }
        // Bounds are projected from the local XY plane, matching MaskableGraphic clipping.
        internal static Rect TransformRect(Bounds bounds, Matrix4x4 matrix)
        {
            var center = bounds.center; center.z = 0;
            center = matrix.MultiplyPoint3x4(center);
            var extents = bounds.extents;
            float x = Mathf.Abs(matrix.m00) * extents.x + Mathf.Abs(matrix.m01) * extents.y;
            float y = Mathf.Abs(matrix.m10) * extents.x + Mathf.Abs(matrix.m11) * extents.y;
            return new Rect(center.x - x, center.y - y, 2 * x, 2 * y);
        }
        private static float ReciprocalAxis(float value)
        {
            // A collapsed/invalid axis cannot be inverted. Do not inject infinity into UI matrices.
            if (!float.IsFinite(value) || value == 0) return 1;
            double inverse = 1.0 / value;
            return inverse > float.MaxValue || inverse < -float.MaxValue ? 1 : (float)inverse;
        }
    }
}
