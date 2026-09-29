using System.Collections.Generic;
using UnityEngine;
using ShanFlyer.UIEffects.Internal;
namespace ShanFlyer.UIEffects
{
    // Sprite geometry is cached independently of the source Transform and Canvas conversion.
    internal sealed class SpriteBridgeGeometry
    {
        private Sprite sprite;
        private Texture texture;
        private Vector2[] points, texcoords;
        private ushort[] triangles;
        private bool valid, flipX, flipY;
        private Color color;
        private Vector2 size;
        private SpriteDrawMode mode;
        private SpriteTileMode tileMode;
        private float threshold;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector2> uv = new List<Vector2>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> indices = new List<int>();
        private struct Span { internal float start, end, uvStart, uvEnd; }
        private readonly List<Span> xs = new List<Span>(), ys = new List<Span>();
        internal void Invalidate() { valid = false; sprite = null; }
        internal bool Capture(SpriteRenderer source, Mesh mesh, int limit)
        {
            var next = source.sprite;
            if (!next) { mesh.Clear(false); valid = false; return false; }
            var nextTexture = SpriteTexture.Resolve(next);
            if (sprite != next || texture != nextTexture)
            {
                sprite = next; texture = nextTexture; points = next.vertices; texcoords = next.uv; triangles = next.triangles; valid = false;
            }
            if (valid && flipX == source.flipX && flipY == source.flipY && color.Equals(source.color)
                && size == source.size && mode == source.drawMode && tileMode == source.tileMode && threshold == source.adaptiveModeThreshold) return true;
            flipX = source.flipX; flipY = source.flipY; color = source.color; size = source.size;
            mode = source.drawMode; tileMode = source.tileMode; threshold = source.adaptiveModeThreshold;
            vertices.Clear(); uv.Clear(); colors.Clear(); indices.Clear(); mesh.Clear(false); valid = false;
            if (points.Length != texcoords.Length || points.Length == 0) return false;
            if (mode == SpriteDrawMode.Simple)
            {
                if (points.Length > limit) return false;
                for (int i = 0; i < points.Length; ++i) Vertex(points[i], texcoords[i]);
                for (int i = 0; i + 2 < triangles.Length; i += 3) Triangle(triangles[i], triangles[i + 1], triangles[i + 2]);
            }
            else
            {
                if (!float.IsFinite(size.x) || !float.IsFinite(size.y) || size.x <= 0 || size.y <= 0) return false;
                var rect = sprite.rect; var border = sprite.border; float ppu = sprite.pixelsPerUnit;
                if (rect.width <= 0 || rect.height <= 0 || ppu <= 0) return false;
                if (!Axis(xs, size.x, rect.width / ppu, border.x / ppu, border.z / ppu, sprite.pivot.x / rect.width, limit)
                    || !Axis(ys, size.y, rect.height / ppu, border.y / ppu, border.w / ppu, sprite.pivot.y / rect.height, limit)
                    || (long)xs.Count * ys.Count * 4 > limit) return false;
                if (!UVBasis(out var origin, out var u, out var v)) return false;
                foreach (var y in ys) foreach (var x in xs)
                {
                    int first = vertices.Count;
                    Vertex(new Vector2(x.start, y.start), origin + u * x.uvStart + v * y.uvStart);
                    Vertex(new Vector2(x.start, y.end), origin + u * x.uvStart + v * y.uvEnd);
                    Vertex(new Vector2(x.end, y.end), origin + u * x.uvEnd + v * y.uvEnd);
                    Vertex(new Vector2(x.end, y.start), origin + u * x.uvEnd + v * y.uvStart);
                    Triangle(first, first + 1, first + 2); Triangle(first + 2, first + 3, first);
                }
            }
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetColors(colors); mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
            valid = vertices.Count > 0; return valid;
        }
        private void Vertex(Vector2 p, Vector2 t)
        {
            vertices.Add(new Vector3(flipX ? -p.x : p.x, flipY ? -p.y : p.y, 0)); uv.Add(t); colors.Add(color);
        }
        private void Triangle(int a, int b, int c)
        {
            indices.Add(a); indices.Add(flipX != flipY ? c : b); indices.Add(flipX != flipY ? b : c);
        }
        private bool Axis(List<Span> axis, float length, float original, float near, float far, float pivot, int limit)
        {
            axis.Clear(); float sum = near + far, factor = sum > length ? length / sum : 1;
            float left = near * factor, right = length - far * factor, origin = -length * pivot;
            if (left > 0) axis.Add(new Span { start = origin, end = origin + left, uvStart = 0, uvEnd = near / original });
            float middle = right - left, nativeMiddle = original - sum;
            if (middle > 0 && nativeMiddle > 0)
            {
                float uvStart = near / original, uvEnd = 1 - far / original;
                if (mode == SpriteDrawMode.Sliced)
                    axis.Add(new Span { start = origin + left, end = origin + right, uvStart = uvStart, uvEnd = uvEnd });
                else
                {
                    float ratio = middle / nativeMiddle;
                    if (!float.IsFinite(ratio) || ratio > limit / 4f) return false;
                    int count = Mathf.Max(1, Mathf.CeilToInt(ratio - (tileMode == SpriteTileMode.Adaptive ? threshold : 0)));
                    float step = tileMode == SpriteTileMode.Adaptive ? middle / count : nativeMiddle;
                    for (int i = 0; i < count; ++i)
                    {
                        float start = left + i * step, end = Mathf.Min(right, start + step);
                        axis.Add(new Span { start = origin + start, end = origin + end, uvStart = uvStart,
                            uvEnd = Mathf.Lerp(uvStart, uvEnd, tileMode == SpriteTileMode.Adaptive ? 1 : (end - start) / step) });
                    }
                }
            }
            if (right < length) axis.Add(new Span { start = origin + right, end = origin + length, uvStart = 1 - far / original, uvEnd = 1 });
            return true;
        }
        private bool UVBasis(out Vector2 origin, out Vector2 u, out Vector2 v)
        {
            origin = u = v = default;
            var rect = sprite.rect; var pivot = sprite.pivot; float ppu = sprite.pixelsPerUnit;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                var ab = points[b] - points[a]; var ac = points[c] - points[a];
                float determinant = ab.x * ac.y - ab.y * ac.x;
                if (Mathf.Abs(determinant) < 1e-12f) continue;
                var tab = texcoords[b] - texcoords[a]; var tac = texcoords[c] - texcoords[a];
                var dx = (tab * ac.y - tac * ab.y) / determinant;
                var dy = (tac * ab.x - tab * ac.x) / determinant;
                origin = texcoords[a] + dx * (-pivot.x / ppu - points[a].x) + dy * (-pivot.y / ppu - points[a].y);
                u = dx * (rect.width / ppu); v = dy * (rect.height / ppu); return true;
            }
            return false;
        }
    }
}
