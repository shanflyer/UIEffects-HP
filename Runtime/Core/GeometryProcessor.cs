using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ShanFlyer.UIEffects.Internal
{
    // A per-output processor: storage survives frames but is never shared with another UI graphic.
    // Unity produces the geometry; this stage only adapts it to UGUI's vertex contract.
    internal sealed class GeometryProcessor : IDisposable
    {
        private readonly List<Component> modifiers = new List<Component>();
        private readonly List<Color32> colors = new List<Color32>();
        private readonly List<Vector3> positions = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector4> tangents = new List<Vector4>();
        private readonly List<Vector4>[] uv = { new List<Vector4>(), new List<Vector4>(), new List<Vector4>(), new List<Vector4>() };
        private readonly List<int> indices = new List<int>();
        private readonly VertexHelper vertices = new VertexHelper();
        private static readonly byte[] Gamma = CreateGammaTable();
        private static byte[] CreateGammaTable()
        {
            var table = new byte[256];
            for (int i = 0; i != table.Length; ++i) table[i] = (byte)(Mathf.LinearToGammaSpace(i / 255f) * 255f);
            return table;
        }
        internal bool ScanModifiers(Component owner)
        {
            modifiers.Clear();
            owner.GetComponents(typeof(IMeshModifier), modifiers);
            return modifiers.Count != 0;
        }
        internal bool Finish(Mesh mesh, bool gamma, int limit, out Bounds bounds)
        {
            bounds = default;
            // Model meshes commonly omit COLOR. UI shaders multiply by it, so the
            // output must provide opaque white without changing the source asset.
            bool missingColors = !mesh.HasVertexAttribute(VertexAttribute.Color);
            if (missingColors)
            {
                colors.Clear();
                for (int i = 0; i < mesh.vertexCount; ++i) colors.Add(new Color32(255, 255, 255, 255));
            }
            else if (gamma || modifiers.Count > 0) mesh.GetColors(colors);
            if (gamma)
            {
                for (int i = 0; i < colors.Count; ++i)
                {
                    var c = colors[i]; colors[i] = new Color32(Gamma[c.r], Gamma[c.g], Gamma[c.b], c.a);
                }
            }
            // Modifiers consume this buffer directly, avoiding a SetColors/GetColors round trip.
            if (modifiers.Count == 0 && (gamma || missingColors)) mesh.SetColors(colors);
            if (modifiers.Count > 0)
            {
                mesh.GetVertices(positions); mesh.GetNormals(normals); mesh.GetTangents(tangents);
                for (int channel = 0; channel < uv.Length; ++channel) mesh.GetUVs(channel, uv[channel]);
                vertices.Clear();
                for (int i = 0; i < positions.Count; ++i)
                    vertices.AddVert(positions[i], At(colors, i), At(uv[0], i), At(uv[1], i),
                        At(uv[2], i), At(uv[3], i), At(normals, i), At(tangents, i));
                // The UI modifier API has a single triangle stream.
                for (int sub = 0; sub < mesh.subMeshCount; ++sub)
                {
                    mesh.GetTriangles(indices, sub);
                    for (int i = 0; i + 2 < indices.Count; i += 3) vertices.AddTriangle(indices[i], indices[i + 1], indices[i + 2]);
                }
                foreach (var component in modifiers)
                    if (component) ((IMeshModifier)component).ModifyMesh(vertices);
                if (vertices.currentVertCount > limit) { mesh.Clear(); return false; }
                vertices.FillMesh(mesh);
                mesh.RecalculateBounds();
            }
            bounds = mesh.bounds;
            // Keep the actual 3D bounds for camera/Scene-view frustum culling.
            // Project to a rectangle only when UGUI asks for clipping bounds.
            return true;
        }
        private static T At<T>(List<T> values, int index) => index < values.Count ? values[index] : default;
        public void Dispose() => vertices.Dispose();
    }
}
