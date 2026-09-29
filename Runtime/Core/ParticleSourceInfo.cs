using UnityEngine;
using ShanFlyer.UIEffects.Internal;

namespace ShanFlyer.UIEffects
{
    internal static class ParticleSourceInfo
    {
        internal static ParticleSystemSimulationSpace Space(ParticleSystem source)
        {
            var settings = source.main;
            return settings.simulationSpace == ParticleSystemSimulationSpace.Custom && !settings.customSimulationSpace
                ? ParticleSystemSimulationSpace.Local : settings.simulationSpace;
        }

        internal static Texture2D SpriteSheetTexture(ParticleSystem source)
        {
            if (!source) return null;
            var sheet = source.textureSheetAnimation;
            if (!sheet.enabled || sheet.mode != ParticleSystemAnimationMode.Sprites) return null;
            int count = sheet.spriteCount;
            for (int slot = 0; slot < count; ++slot)
            {
                var sprite = sheet.GetSprite(slot);
                if (sprite) return SpriteTexture.Resolve(sprite);
            }
            return null;
        }
    }
}
