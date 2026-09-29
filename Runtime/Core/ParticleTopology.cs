using System.Collections.Generic;
using UnityEngine;

namespace ShanFlyer.UIEffects
{
    internal sealed class ParticleTopology
    {
        private readonly Dictionary<ParticleSystem, ParticleSystem> parents = new Dictionary<ParticleSystem, ParticleSystem>();
        internal void Rebuild(List<ParticleSystem> sources)
        {
            parents.Clear();
            // First parent in source order wins, including references across material runs.
            foreach (var parent in sources)
            {
                if (!parent) continue;
                var children = parent.subEmitters;
                if (!children.enabled) continue;
                int count = children.subEmittersCount;
                for (int slot = 0; slot < count; ++slot)
                {
                    var child = children.GetSubEmitterSystem(slot);
                    if (child && child != parent) parents.TryAdd(child, parent);
                }
            }
        }
        internal ParticleSystem ParentOf(ParticleSystem source) => source
            && parents.TryGetValue(source, out var parent) && parent ? parent : null;
    }
}
