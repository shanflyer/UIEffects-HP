using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShanFlyer.UIEffects
{
    // Readable Mesh has no public content revision. Compare reusable buffers rather than
    // trusting its reference/bounds: scripts can edit vertices, UVs or indices in place.
    internal sealed class BridgeGeometrySnapshot
    {
        private readonly List<Vector3> _vertices = new List<Vector3>(), _normals = new List<Vector3>();
        private readonly List<Vector4> _tangents = new List<Vector4>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _indices = new List<int>();
        private readonly List<Vector4>[] _uv = new List<Vector4>[8];
        private readonly List<Vector3> _v3 = new List<Vector3>();
        private readonly List<Vector4> _v4 = new List<Vector4>();
        private readonly List<Color> _colorBuffer = new List<Color>();
        private readonly List<int> _indexBuffer = new List<int>();
        private MeshTopology _topology;
        private readonly List<VertexAttributeDescriptor> _layout = new List<VertexAttributeDescriptor>();
        private readonly List<VertexAttributeDescriptor> _layoutBuffer = new List<VertexAttributeDescriptor>();

        internal bool Capture(Mesh mesh, int submesh = 0)
        {
            mesh.GetVertexAttributes(_layoutBuffer); var changed = ReplaceIfDifferent(_layout, _layoutBuffer);
            mesh.GetVertices(_v3); changed |= ReplaceIfDifferent(_vertices, _v3);
            mesh.GetNormals(_v3); changed |= ReplaceIfDifferent(_normals, _v3);
            mesh.GetTangents(_v4); changed |= ReplaceIfDifferent(_tangents, _v4);
            mesh.GetColors(_colorBuffer); changed |= ReplaceIfDifferent(_colors, _colorBuffer);
            for (var channel = 0; channel < 8; channel++)
            {
                if (_uv[channel] == null && !mesh.HasVertexAttribute((VertexAttribute)((int)VertexAttribute.TexCoord0 + channel))) continue;
                if (_uv[channel] == null) _uv[channel] = new List<Vector4>();
                mesh.GetUVs(channel, _v4); changed |= ReplaceIfDifferent(_uv[channel], _v4);
            }
            mesh.GetIndices(_indexBuffer, submesh); changed |= ReplaceIfDifferent(_indices, _indexBuffer);
            var topology = mesh.GetTopology(submesh); changed |= topology != _topology; _topology = topology;
            return changed;
        }

        private static bool ReplaceIfDifferent<T>(List<T> previous, List<T> current)
        {
            var equal = previous.Count == current.Count;
            for (var i = 0; equal && i < current.Count; i++)
                equal = EqualityComparer<T>.Default.Equals(previous[i], current[i]);
            if (equal) return false;
            previous.Clear(); previous.AddRange(current);
            return true;
        }
    }
}
