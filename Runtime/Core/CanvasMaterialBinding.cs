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
            internal readonly bool meshUiQueue;
            private readonly UnityEngine.Object owner;
            internal Key(Material source, Texture texture, UnityEngine.Object owner, bool meshUiQueue)
            { this.source = source; this.texture = texture; this.owner = owner; this.meshUiQueue = meshUiQueue; }
            public bool Equals(Key other) => ReferenceEquals(source, other.source)
                && ReferenceEquals(texture, other.texture) && ReferenceEquals(owner, other.owner)
                && meshUiQueue == other.meshUiQueue;
            public override bool Equals(object other) => other is Key key && Equals(key);
            public override int GetHashCode() => HashCode.Combine(source, texture, owner, meshUiQueue);
        }
        private sealed class Variant { internal Material value; internal int users; }
        private static readonly Dictionary<Key, Variant> variants = new();
        private Key key;
        private Variant held;

        internal Material Resolve(Material source, Texture texture, UnityEngine.Object propertyOwner, bool meshUiQueue = false)
        {
            if (!source || (!texture && !propertyOwner && !meshUiQueue)) { Dispose(); return source; }
            var requested = new Key(source, texture, propertyOwner, meshUiQueue);
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
            if (meshUiQueue) ApplyMeshQueue(held.value);
            return held.value;
        }

        internal static void ApplyMeshQueue(Material material)
        {
            // Scene-view UI is emitted as camera geometry. An opaque queue can put
            // the mesh before the UI depth-reset draws (and into depth priming).
            // Queue selection does NOT change ZWrite/ZTest, Blend, Cull or keywords.
            if (material && material.renderQueue != (int)UnityEngine.Rendering.RenderQueue.Transparent)
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
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
