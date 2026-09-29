using UnityEngine;

namespace ShanFlyer.UIEffects
{
    // Both drawing and attraction use this mapping; there is no separate editor-only inverse.
    internal static class ParticleCoordinates
    {
        internal static Vector3 Scale(UIEffectRenderer effect, ParticleSystem source)
        {
            var result = Vector3.Scale(effect.calculatedScale, effect.parentScale);
            if (effect.unitConversion == UIEffectRenderer.UnitConversion.Manual
                && effect.scaleMode == UIEffectRenderer.ScaleMode.Canvas && effect.canvas
                && source.main.scalingMode == ParticleSystemScalingMode.Local)
                result = Vector3.Scale(result, effect.canvas.rootCanvas.transform.localScale);
            return result;
        }

        internal static Matrix4x4 BakeToWorld(ParticleSystem source, bool trail, Vector3 scale,
            Vector3 effectOrigin, UIEffectRenderer.OriginMode origin)
        {
            var emitter = source.transform.position;
            var space = trail && source.trails.worldSpace ? ParticleSystemSimulationSpace.World : ParticleSourceInfo.Space(source);
            // BakeRotationAndScale includes the simulation frame's rotation/scale, but
            // its translation still has to be supplied (World already has world positions).
            var frameOrigin = space == ParticleSystemSimulationSpace.Local ? emitter
                : space == ParticleSystemSimulationSpace.Custom ? source.main.customSimulationSpace.position
                : Vector3.zero;
            var anchor = origin == UIEffectRenderer.OriginMode.Effect ? effectOrigin : emitter;
            return EffectScale.ScaleAndOffset(scale, anchor + Vector3.Scale(frameOrigin - anchor, scale));
        }

        internal static bool TryTargetInSimulation(UIEffectRenderer effect, ParticleSystem source, Vector3 target, out Vector3 result)
        {
            var main = source.main;
            var space = ParticleSourceInfo.Space(source);
            Matrix4x4 mapping;
            if (!effect || !effect.isActiveAndEnabled)
                mapping = space == ParticleSystemSimulationSpace.World ? Matrix4x4.identity
                    : space == ParticleSystemSimulationSpace.Custom ? main.customSimulationSpace.localToWorldMatrix
                    : source.transform.localToWorldMatrix;
            else
            {
                mapping = BakeToWorld(source, false, Scale(effect, source), effect.transform.position, effect.originMode);
                if (space == ParticleSystemSimulationSpace.Local)
                {
                    var size = main.scalingMode == ParticleSystemScalingMode.Hierarchy ? source.transform.lossyScale
                        : main.scalingMode == ParticleSystemScalingMode.Local ? source.transform.localScale : Vector3.one;
                    mapping *= Matrix4x4.TRS(Vector3.zero, source.transform.rotation, size);
                }
                else if (space == ParticleSystemSimulationSpace.Custom)
                {
                    var frame = main.customSimulationSpace;
                    mapping *= Matrix4x4.TRS(Vector3.zero, frame.rotation, frame.lossyScale);
                }
            }
            float determinant = mapping.determinant;
            result = default;
            if (!float.IsFinite(determinant) || determinant == 0) return false;
            result = mapping.inverse.MultiplyPoint3x4(target);
            return EffectScale.IsFinite(result);
        }
    }
}
