using System;
using UnityEngine;

namespace ShanFlyer.UIEffects
{
    // Owned by one simulation or attractor; never exposes a globally shared writable array.
    internal sealed class ParticleReadback
    {
        internal ParticleSystem.Particle[] Values { get; private set; } = Array.Empty<ParticleSystem.Particle>();
        internal int Read(ParticleSystem source)
        {
            if (!source) return 0;
            int required = source.particleCount;
            if (required == 0) return 0;
            if (Values.Length < required)
            {
                int capacity = Mathf.NextPowerOfTwo(required);
                Values = new ParticleSystem.Particle[Math.Max(required, capacity)];
            }
            return source.GetParticles(Values);
        }
    }
}
