using UnityEngine;

namespace ShanFlyer.UIEffects
{
    // A source session advances native simulation. Rendering never rewrites source coordinates.
    internal sealed class ParticleSimulation
    {
        private UIEffectRenderer controller;
        private uint cycle;
        private bool prepareCycle = true;
        private int advancedFrame = -1;
        private float carriedSeconds;
        private uint carriedCycle;

        internal void Carry(float seconds, uint playbackCycle)
        {
            if (carriedCycle != playbackCycle) carriedSeconds = 0;
            carriedCycle = playbackCycle;
            if (seconds > 0 && float.IsFinite(seconds)) carriedSeconds += seconds;
        }

        internal void Advance(ParticleSystem source, UIEffectRenderer owner, float elapsed)
        {
            if (!Application.isPlaying || !source || !owner || owner.isPaused) return;
            float seconds = (elapsed + (carriedCycle == owner.playbackCycle ? carriedSeconds : 0)) * owner.simulationSpeed;
            if (!(seconds > 0) || !float.IsFinite(seconds)) return;
            if (controller != owner || cycle != owner.playbackCycle)
            {
                controller = owner;
                cycle = owner.playbackCycle;
                prepareCycle = true;
            }
            if (advancedFrame == Time.frameCount) return;
            carriedSeconds = 0;
            var settings = source.main;
            if (prepareCycle)
            {
                // Existing native state is adopted; only an empty, new looping cycle is warmed.
                if (settings.loop && settings.prewarm && source.time == 0 && source.particleCount == 0)
                {
                    float warmSeconds = settings.duration;
                    if (warmSeconds > 0 && float.IsFinite(warmSeconds))
                        source.Simulate(warmSeconds, withChildren: false, restart: false, fixedTimeStep: true);
                }
                prepareCycle = false;
            }
            // Native looping, bursts, emission distance, custom space and completion remain native.
            source.Simulate(seconds, withChildren: false, restart: false, fixedTimeStep: false);
            advancedFrame = Time.frameCount;
        }
    }
}
