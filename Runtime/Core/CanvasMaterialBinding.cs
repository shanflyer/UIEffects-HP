using System;
using System.Collections.Generic;
using UnityEngine;
using ShanFlyer.UIEffects.Internal;

namespace ShanFlyer.UIEffects
{
    // Object references form the identity. Material content is refreshed without JSON/hash snapshots.
    internal sealed class CanvasMaterialBinding : IDisposable
    {
        private readonly struct Key : IEquatable<Key>
        {
            internal readonly Material source;
            internal readonly Texture texture;
            private readonly UnityEngine.Object owner;
            internal Key(Material source, Texture texture, UnityEngine.Object owner)
            { this.source = source; this.texture = texture; this.owner = owner; }
            public bool Equals(Key other) => ReferenceEquals(source, other.source)
                && ReferenceEquals(texture, other.texture) && ReferenceEquals(owner, other.owner);
            public override bool Equals(object other) => other is Key key && Equals(key);
            public override int GetHashCode() => HashCode.Combine(source, texture, owner);
        }
        private sealed class Variant { internal Material value; internal int users; }
        private static readonly Dictionary<Key, Variant> variants = new();
        private Key key;
        private Variant held;

        internal Material Resolve(Material source, Texture texture, UnityEngine.Object propertyOwner)
        {
            if (!source || (!texture && !propertyOwner)) { Dispose(); return source; }
            var requested = new Key(source, texture, propertyOwner);
            if (held == null || !held.value || !key.Equals(requested))
            {
                Dispose();
                key = requested;
                if (!variants.TryGetValue(key, out held) || !held.value)
                {
                    held = new Variant { value = new Material(source) { hideFlags = HideFlags.HideAndDontSave } };
                    variants[key] = held;
                }
                held.users++;
            }
            held.value.CopyPropertiesFromMaterial(source);
            if (texture) held.value.mainTexture = texture;
            return held.value;
        }
        public void Dispose()
        {
            if (held == null) return;
            if (--held.users == 0)
            {
                if (variants.TryGetValue(key, out var current) && ReferenceEquals(current, held)) variants.Remove(key);
                EngineObjects.Destroy(held.value);
            }
            held = null;
            key = default;
        }
    }
}
