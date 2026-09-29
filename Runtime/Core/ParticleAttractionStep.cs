using UnityEngine;

namespace ShanFlyer.UIEffects
{
    // Target-relative steering: translation of the whole scene cannot change the path.
    internal static class ParticleAttractionStep
    {
        internal enum Result { Unchanged, Moved, Arrived }

        internal static Result Apply(ref ParticleSystem.Particle particle, Vector3 target,
            float radius, float delay, float speed, float delta,
            UIParticleAttractor.AttractionPath path, float curvature)
        {
            if (delta <= 0 || !float.IsFinite(delta) || particle.remainingLifetime <= 0
                || !EffectScale.IsFinite(particle.position) || !EffectScale.IsFinite(target)) return Result.Unchanged;
            float lifetime = particle.startLifetime;
            float age = lifetime - particle.remainingLifetime;
            if (!float.IsFinite(lifetime) || lifetime <= 0 || !float.IsFinite(age) || age < lifetime * delay)
                return Result.Unchanged;
            var offset = target - particle.position;
            float distance = offset.magnitude;
            if (!float.IsFinite(distance)) return Result.Unchanged;
            if (distance <= radius) { particle.remainingLifetime = 0; return Result.Arrived; }
            float travel = speed * delta;
            if (travel <= 0 || !float.IsFinite(travel)) return Result.Unchanged;
            if (path == UIParticleAttractor.AttractionPath.Eased)
            {
                float progress = Mathf.Clamp01((age / lifetime - delay) / (1 - delay));
                travel *= progress * progress * (3 - 2 * progress);
            }
            if (travel <= 0) return Result.Unchanged;
            if (travel >= distance - radius)
            {
                particle.position += offset * ((distance - radius) / distance);
                particle.remainingLifetime = 0;
                return Result.Arrived;
            }
            Vector3 radial = offset / distance;
            Vector3 heading = radial;
            if (path == UIParticleAttractor.AttractionPath.Curved && curvature > 0)
            {
                // Preserve the velocity's lateral direction; radial attraction still dominates.
                Vector3 lateral = EffectScale.IsFinite(particle.velocity)
                    ? particle.velocity - Vector3.Dot(particle.velocity, radial) * radial : Vector3.zero;
                if (lateral.sqrMagnitude < .000001f)
                    lateral = Vector3.Cross(radial, Mathf.Abs(radial.z) < .9f ? Vector3.forward : Vector3.up);
                heading = (radial + lateral.normalized * curvature).normalized;
                // Stay inside the current distance sphere even for a long frame.
                travel = Mathf.Min(travel, distance * Vector3.Dot(heading, radial));
            }
            particle.position += heading * travel;
            // Stable time-based damping, independent of frame rate at a stationary target.
            particle.velocity = EffectScale.IsFinite(particle.velocity)
                ? particle.velocity * Mathf.Exp(-8 * delta) : Vector3.zero;
            if ((target - particle.position).sqrMagnitude <= radius * radius)
            { particle.remainingLifetime = 0; return Result.Arrived; }
            return Result.Moved;
        }
    }
}
