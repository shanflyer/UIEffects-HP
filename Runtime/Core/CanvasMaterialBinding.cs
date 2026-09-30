using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
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
            internal readonly bool meshCopy;
            private readonly UnityEngine.Object owner;
            internal Key(Material source, Texture texture, UnityEngine.Object owner, bool meshCopy)
            { this.source = source; this.texture = texture; this.owner = owner; this.meshCopy = meshCopy; }
            public bool Equals(Key other) => ReferenceEquals(source, other.source)
                && ReferenceEquals(texture, other.texture) && ReferenceEquals(owner, other.owner)
                && meshCopy == other.meshCopy;
            public override bool Equals(object other) => other is Key key && Equals(key);
            public override int GetHashCode() => HashCode.Combine(source, texture, owner, meshCopy);
        }
        private sealed class Variant
        {
            internal Material value;
            internal int users;
        }
        private static readonly Dictionary<Key, Variant> variants = new();
        private Key key;
        private Variant held;

        internal Material Resolve(Material source, Texture texture, UnityEngine.Object propertyOwner,
            bool meshCopy = false)
        {
            if (!source || (!texture && !propertyOwner && !meshCopy))
            { Dispose(); return source; }
            var requested = new Key(source, texture, propertyOwner, meshCopy);
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
            CopyToCanvas(held.value, source, meshCopy);
            if (texture) held.value.mainTexture = texture;
            return held.value;
        }

        internal static void CopyToCanvas(Material destination, Material source, bool meshCopy = false)
        {
            // Keep the assigned shader, including when the source changes at runtime.
            // Canvas submission must not substitute a generated shader dependency.
            var shader = source.shader;
            if (destination.shader != shader) destination.shader = shader;
            destination.CopyPropertiesFromMaterial(source);
            // Camera rendering sorts queues before Canvas hierarchy order. A mesh
            // left in Geometry can draw before both its depth reset and preceding
            // UI. Only the transient Canvas copy joins the UI queue; source assets
            // and the shader's depth, cull and blend state stay unchanged.
            if (meshCopy) destination.renderQueue = (int)RenderQueue.Transparent;
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
