using System;
using UnityEngine;

namespace ShanFlyer.UIEffects
{
    /// <summary>Selects a source Renderer property override to transfer to the UI material.</summary>
    [Serializable]
    public sealed class MaterialPropertyBinding
    {
        public enum ValueKind
        {
            Disabled, Float, Integer, Color, Vector, Texture, Matrix, FloatArray, VectorArray, MatrixArray
        }

        [SerializeField]
        private string propertyName = "";
        [SerializeField]
        private ValueKind valueKind = ValueKind.Vector;
        [NonSerialized] private string cachedName;
        [NonSerialized] private int cachedId;

        public string PropertyName { get => propertyName; set => propertyName = value; }
        public ValueKind Kind { get => valueKind; set => valueKind = value; }

        public MaterialPropertyBinding() { }
        public MaterialPropertyBinding(string name, ValueKind kind)
        { propertyName = name; valueKind = kind; }

        internal bool TryResolve(out int id)
        {
            id = 0;
            if (valueKind == ValueKind.Disabled || string.IsNullOrWhiteSpace(propertyName)) return false;
            if (!string.Equals(cachedName, propertyName, StringComparison.Ordinal))
            {
                cachedName = propertyName;
                cachedId = Shader.PropertyToID(propertyName);
            }
            id = cachedId;
            return true;
        }
    }
}
