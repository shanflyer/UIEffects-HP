using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShanFlyer.UIEffects
{
    /// <summary>
    /// HP redraws SpriteMask geometry through CanvasRenderer. The source SpriteMask must not
    /// ALSO submit Unity's native stencil draws: those use UI's low bits, independently of
    /// our reserved bit, and can reveal unrelated masked UI outside its viewport.
    /// </summary>
    internal static class SpriteMaskNativeRendering
    {
        private sealed class State
        {
            internal int users;
            internal bool originalForceRenderingOff;
        }

        private sealed class Owner
        {
            internal readonly HashSet<SpriteMask> masks = new HashSet<SpriteMask>();
        }

        private static readonly Dictionary<UIEffectRenderer, Owner> s_Owners = new Dictionary<UIEffectRenderer, Owner>();
        private static readonly Dictionary<SpriteMask, State> s_States = new Dictionary<SpriteMask, State>();
        private static readonly HashSet<SpriteMask> s_Desired = new HashSet<SpriteMask>();
        private static readonly List<SpriteMask> s_Scratch = new List<SpriteMask>();
        private static readonly List<SpriteMask> s_Removed = new List<SpriteMask>();
        private static bool s_Hooked;

        internal static bool WasForceRenderingOff(SpriteMask mask)
        {
            return s_States.TryGetValue(mask, out var state) ? state.originalForceRenderingOff : mask.forceRenderingOff;
        }

        internal static void Register(UIEffectRenderer particle)
        {
            if (!s_Owners.ContainsKey(particle)) s_Owners.Add(particle, new Owner());
            if (!s_Hooked)
            {
                Camera.onPreCull += BeforeCamera;
                RenderPipelineManager.beginCameraRendering += BeforeSrpCamera;
#if UNITY_EDITOR
                UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += RestoreAll;
#endif
                s_Hooked = true;
            }
            Sync(particle);
        }

        internal static void Unregister(UIEffectRenderer particle)
        {
            if (!s_Owners.TryGetValue(particle, out var owner)) return;
            foreach (var mask in owner.masks) Release(mask);
            s_Owners.Remove(particle);
            if (s_Owners.Count == 0) Unhook();
        }

        private static void BeforeSrpCamera(ScriptableRenderContext context, Camera camera) { BeforeCamera(camera); }

        private static void BeforeCamera(Camera camera)
        {
            // Must run BEFORE native culling, not only in the late Canvas rebuild callback.
            // This also catches spawned/reparented/animated masks and pooled effects.
            SpriteMaskResolver.BeginFrame();
            foreach (var pair in s_Owners)
                if (pair.Key && pair.Key.isActiveAndEnabled) Sync(pair.Key);
        }

        internal static void Sync(UIEffectRenderer particle)
        {
            if (!s_Owners.TryGetValue(particle, out var owner)) return;
            s_Desired.Clear();
            if (particle.isActiveAndEnabled && particle.canvas)
            {
                // Take over every source mask in this effect, including masks not currently in
                // any particle's sorting range. Unused native masks can leak into UI as well.
                particle.GetComponentsInChildren(true, s_Scratch);
                foreach (var mask in s_Scratch)
                    if (mask && mask.GetComponentInParent<UIEffectRenderer>() == particle) s_Desired.Add(mask);
                // Also take over external masks affecting particle or sprite outputs.
                foreach (var ps in particle.particles)
                {
                    if (!ps || !ps.TryGetComponent<ParticleSystemRenderer>(out var renderer)) continue;
                    SpriteMaskResolver.Resolve(renderer, s_Scratch);
                    foreach (var mask in s_Scratch) s_Desired.Add(mask);
                }
                for (int i = 0; i < particle.activeRendererCount; ++i)
                {
                    var output = particle.GetRendererIfExists(i);
                    if (!output || !output.isBridge || !(output.sourceRenderer is SpriteRenderer sprite)) continue;
                    SpriteMaskResolver.Resolve(sprite, s_Scratch);
                    foreach (var mask in s_Scratch) s_Desired.Add(mask);
                }
            }
            s_Removed.Clear();
            foreach (var mask in owner.masks)
                if (!mask || !s_Desired.Contains(mask)) s_Removed.Add(mask);
            foreach (var mask in s_Removed)
            {
                Release(mask);
                owner.masks.Remove(mask);
            }
            foreach (var mask in s_Desired)
            {
                if (owner.masks.Add(mask))
                {
                    if (!s_States.TryGetValue(mask, out var state))
                    {
                        state = new State { originalForceRenderingOff = mask.forceRenderingOff };
                        s_States.Add(mask, state);
                    }
                    state.users++;
                }
                // Do not change SpriteMask.enabled: it remains the authored/animated input to
                // our resolver. forceRenderingOff only prevents the native renderer submitting it.
                mask.forceRenderingOff = true;
            }
            s_Desired.Clear();
            s_Scratch.Clear();
            s_Removed.Clear();
        }

        private static void Release(SpriteMask mask)
        {
            if (!s_States.TryGetValue(mask, out var state) || --state.users != 0) return;
            if (mask) mask.forceRenderingOff = state.originalForceRenderingOff;
            s_States.Remove(mask);
        }

        private static void Unhook()
        {
            if (!s_Hooked) return;
            Camera.onPreCull -= BeforeCamera;
            RenderPipelineManager.beginCameraRendering -= BeforeSrpCamera;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= RestoreAll;
#endif
            s_Hooked = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RestoreAll()
        {
            foreach (var pair in s_States)
                if (pair.Key) pair.Key.forceRenderingOff = pair.Value.originalForceRenderingOff;
            s_States.Clear();
            s_Owners.Clear();
            Unhook();
        }
    }
}
