using System.Collections.Generic;
using UnityEngine;

namespace ShanFlyer.UIEffects
{
    internal enum ParticleCommand { Restart, Pause, Resume, Stop, Clear, EnableEmission, DisableEmission }
    // One control state per effect; only explicitly bound sources receive a command.
    internal sealed class ParticlePlayback
    {
        private readonly HashSet<ParticleSystem> visited = new HashSet<ParticleSystem>();
        internal uint Cycle { get; private set; }
        internal bool Paused { get; private set; }
        internal void Execute(UIEffectRenderer owner, ParticleCommand command)
        {
            bool emissionOnly = command == ParticleCommand.EnableEmission || command == ParticleCommand.DisableEmission;
            if (!emissionOnly)
            {
                Paused = command != ParticleCommand.Restart && command != ParticleCommand.Resume;
#if UNITY_EDITOR
                EditorPlayback.Set(owner, !Paused);
#endif
            }
            if (command == ParticleCommand.Restart) unchecked { ++Cycle; }
            visited.Clear();
            try
            {
                foreach (var source in owner.particles)
                {
                    if (!source || !visited.Add(source)) continue;
                    switch (command)
                    {
                        case ParticleCommand.Restart: source.Simulate(0, false, true, false); break;
                        case ParticleCommand.Pause: source.Pause(false); break;
                        case ParticleCommand.Stop: source.Stop(false, ParticleSystemStopBehavior.StopEmitting); break;
                        case ParticleCommand.Clear: source.Clear(false); break;
                        case ParticleCommand.EnableEmission:
                        case ParticleCommand.DisableEmission:
                            var emission = source.emission;
                            emission.enabled = command == ParticleCommand.EnableEmission;
                            break;
                        // Native simulation is owned by the frame scheduler, including resume.
                        case ParticleCommand.Resume: break;
                    }
                }
            }
            finally
            {
                visited.Clear();
                owner.InvalidateRendererCaches();
            }
        }
    }
}
