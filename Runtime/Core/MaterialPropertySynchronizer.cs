using System.Collections.Generic;
using UnityEngine;

namespace ShanFlyer.UIEffects
{
    /// <summary>Transfers only explicitly selected overrides. Does not modify the source material.</summary>
    internal sealed class MaterialPropertySynchronizer
    {
        private MaterialPropertyBlock overrides;
        private List<float> floats, previousFloats;
        private List<Vector4> vectors, previousVectors;
        private List<Matrix4x4> matrices, previousMatrices;

        internal int Apply(Renderer source, Material destination, MaterialPropertyBinding[] bindings, int materialSlot = -1)
        {
            if (!source || !destination || bindings == null || bindings.Length == 0) return 0;
            overrides ??= new MaterialPropertyBlock();
            overrides.Clear();
            if (materialSlot >= 0) source.GetPropertyBlock(overrides, materialSlot);
            if (materialSlot < 0 || overrides.isEmpty) source.GetPropertyBlock(overrides);
            if (overrides.isEmpty) return 0;
            int writes = 0;
            foreach (var binding in bindings)
            {
                if (binding == null || !binding.TryResolve(out int id) || !overrides.HasProperty(id)) continue;
                if (Transfer(binding.Kind, id, destination)) ++writes;
            }
            return writes;
        }

        private bool Transfer(MaterialPropertyBinding.ValueKind kind, int id, Material target)
        {
            switch (kind)
            {
                case MaterialPropertyBinding.ValueKind.Color:
                    var color = overrides.GetColor(id);
                    if (target.GetColor(id).Equals(color)) return false;
                    target.SetColor(id, color); return true;
                case MaterialPropertyBinding.ValueKind.Vector:
                    var vector = overrides.GetVector(id);
                    if (target.GetVector(id).Equals(vector)) return false;
                    target.SetVector(id, vector); return true;
                case MaterialPropertyBinding.ValueKind.Float:
                    var number = overrides.GetFloat(id);
                    if (target.GetFloat(id).Equals(number)) return false;
                    target.SetFloat(id, number); return true;
                case MaterialPropertyBinding.ValueKind.Integer:
                    int integer = overrides.GetInteger(id);
                    if (target.GetInteger(id) == integer) return false;
                    target.SetInteger(id, integer); return true;
                case MaterialPropertyBinding.ValueKind.Texture:
                    var texture = overrides.GetTexture(id);
                    if (target.GetTexture(id) == texture) return false;
                    target.SetTexture(id, texture); return true;
                case MaterialPropertyBinding.ValueKind.Matrix:
                    var matrix = overrides.GetMatrix(id);
                    if (target.GetMatrix(id).Equals(matrix)) return false;
                    target.SetMatrix(id, matrix); return true;
                case MaterialPropertyBinding.ValueKind.FloatArray:
                    floats ??= new List<float>(); previousFloats ??= new List<float>();
                    floats.Clear(); previousFloats.Clear();
                    overrides.GetFloatArray(id, floats); target.GetFloatArray(id, previousFloats);
                    if (floats.Count == 0 || Equal(floats, previousFloats)) return false;
                    target.SetFloatArray(id, floats); return true;
                case MaterialPropertyBinding.ValueKind.VectorArray:
                    vectors ??= new List<Vector4>(); previousVectors ??= new List<Vector4>();
                    vectors.Clear(); previousVectors.Clear();
                    overrides.GetVectorArray(id, vectors); target.GetVectorArray(id, previousVectors);
                    if (vectors.Count == 0 || Equal(vectors, previousVectors)) return false;
                    target.SetVectorArray(id, vectors); return true;
                case MaterialPropertyBinding.ValueKind.MatrixArray:
                    matrices ??= new List<Matrix4x4>(); previousMatrices ??= new List<Matrix4x4>();
                    matrices.Clear(); previousMatrices.Clear();
                    overrides.GetMatrixArray(id, matrices); target.GetMatrixArray(id, previousMatrices);
                    if (matrices.Count == 0 || Equal(matrices, previousMatrices)) return false;
                    target.SetMatrixArray(id, matrices); return true;
                default: return false;
            }
        }

        private static bool Equal<T>(List<T> a, List<T> b)
        {
            if (a.Count != b.Count) return false;
            var comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < a.Count; ++i)
                if (!comparer.Equals(a[i], b[i])) return false;
            return true;
        }
    }
}
