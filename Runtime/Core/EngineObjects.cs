using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
namespace ShanFlyer.UIEffects.Internal
{
    internal static class Buffers<T>
    {
        public static List<T> Rent() => ListPool<T>.Get();
        public static void Return(ref List<T> value)
        {
            if (value == null) return;
            ListPool<T>.Release(value);
            value = null;
        }
    }
    internal static class EngineObjects
    {
        // Identity is only used within this process, never serialized across editor versions.
        public static ulong Identity(Object value)
        {
            if (!value) return 0;
#if UNITY_6000_4_OR_NEWER
            return EntityId.ToULong(value.GetEntityId());
#else
            return unchecked((uint)value.GetInstanceID());
#endif
        }

        public static void Destroy(Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }
    }
    internal static class SpriteTexture
    {
        public static Texture2D Resolve(Sprite sprite)
        {
            // Use the texture paired with sprite.uv. Editor atlas preview data may
            // exist without a runtime atlas binding, or may not exist at all.
            return sprite ? sprite.texture : null;
        }
    }

    internal static class MaterialTexture
    {
        // Material.mainTexture reports an error for valid textureless shaders.
        // Respect [MainTexture], including SRP/custom names, before the legacy fallback.
        internal static Texture Resolve(Material material)
        {
            if (!material || !material.shader) return null;
            if (material.HasProperty("_MainTex")) return material.mainTexture;
            var shader = material.shader;
            for (int i = 0; i < shader.GetPropertyCount(); ++i)
                if (shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Texture
                    && (shader.GetPropertyFlags(i) & UnityEngine.Rendering.ShaderPropertyFlags.MainTexture) != 0)
                    return material.GetTexture(shader.GetPropertyNameId(i));
            return null;
        }
    }
}
