using System.Collections.Generic;
using UnityEngine;

namespace ShanFlyer.UIEffects
{
    // Body, trails and merged outputs borrow one source state; the last release restores it.
    internal sealed class ParticleSourceBinding
    {
        private static readonly Dictionary<ParticleSystemRenderer, ParticleSourceBinding> active = new();
        internal readonly ParticleSystem source;
        internal readonly ParticleSystemRenderer renderer;
        private readonly bool wasEnabled;
        private int users;
        private UnityEngine.Rendering.UVChannelFlags authoredChannels, appliedChannels;
        private bool ownsChannels;
        internal readonly ParticleSimulation simulation = new ParticleSimulation();

        private ParticleSourceBinding(ParticleSystem source, ParticleSystemRenderer renderer)
        {
            this.source = source;
            this.renderer = renderer;
            wasEnabled = renderer.enabled;
            if (Application.isPlaying && source.isPlaying) source.Pause(false);
            EnsureVertexContract();
        }
        internal void EnsureVertexContract()
        {
            if (!source) return;
            var sheet = source.textureSheetAnimation;
            bool required = sheet.enabled && sheet.mode == ParticleSystemAnimationMode.Sprites;
            if (ownsChannels && (!required || sheet.uvChannelMask != appliedChannels))
            {
                if (sheet.uvChannelMask == appliedChannels) sheet.uvChannelMask = authoredChannels;
                ownsChannels = false;
            }
            if (!required || (sheet.uvChannelMask & UnityEngine.Rendering.UVChannelFlags.UV0) != 0) return;
            authoredChannels = sheet.uvChannelMask;
            appliedChannels = authoredChannels | UnityEngine.Rendering.UVChannelFlags.UV0;
            sheet.uvChannelMask = appliedChannels;
            ownsChannels = true;
        }
        internal static ParticleSourceBinding Acquire(ParticleSystem source)
        {
            if (!source || !source.TryGetComponent<ParticleSystemRenderer>(out var renderer)) return null;
            if (!active.TryGetValue(renderer, out var state))
            {
                state = new ParticleSourceBinding(source, renderer);
                active.Add(renderer, state);
            }
            state.users++;
            renderer.enabled = false;
            return state;
        }
        internal void Release()
        {
            if (--users != 0) return;
            active.Remove(renderer);
            if (source && ownsChannels)
            {
                var sheet = source.textureSheetAnimation;
                if (sheet.uvChannelMask == appliedChannels) sheet.uvChannelMask = authoredChannels;
            }
            ownsChannels = false;
            if (renderer) renderer.enabled = wasEnabled;
        }
    }
}
