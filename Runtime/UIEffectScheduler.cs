using System;
using System.Collections.Generic;
using UnityEngine;
using ShanFlyer.UIEffects.Internal;
#if UNITY_EDITOR
using UnityEditor;
#endif
namespace ShanFlyer.UIEffects
{
    // Cache structural membership/slots; refresh ownership and visibility every frame.
    internal static class UIEffectScheduler
    {
        private sealed class Group
        {
            internal readonly List<UIEffectRenderer> members = new List<UIEffectRenderer>();
            internal readonly List<List<(UIEffectRenderer effect, CanvasEffectOutput renderer)>> slots = new List<List<(UIEffectRenderer, CanvasEffectOutput)>>();
            internal UIEffectRenderer owner;
            internal bool separate, hidden, clipped;
            internal void Clear()
            {
                members.Clear(); foreach (var slot in slots) slot.Clear();
                owner = null; separate = false; hidden = clipped = true;
            }
        }
        private static readonly List<UIEffectRenderer> registered = new List<UIEffectRenderer>();
        private static readonly List<UIParticleAttractor> attractors = new List<UIParticleAttractor>();
        private static readonly List<UIEffectRenderer> frame = new List<UIEffectRenderer>();
        private static readonly List<UIParticleAttractor> frameAttractors = new List<UIParticleAttractor>();
        private static readonly Dictionary<int, Group> groups = new Dictionary<int, Group>();
        private static readonly Stack<Group> spareGroups = new Stack<Group>();
        private static readonly Dictionary<UIEffectRenderer, (int group, bool shared)> bindings = new Dictionary<UIEffectRenderer, (int, bool)>();
        private static readonly HashSet<UIEffectRenderer> failed = new HashSet<UIEffectRenderer>(), reported = new HashSet<UIEffectRenderer>();
        private static readonly HashSet<int> dirtyGroups = new HashSet<int>();
        private static int s_FrameCount = -1;
        private static bool executing;
        private static bool planDirty = true;
        private sealed class Layout
        {
            internal int group;
            internal bool shared;
            internal readonly List<CanvasEffectOutput> renderers = new List<CanvasEffectOutput>();
        }
        private static readonly Dictionary<UIEffectRenderer, Layout> layouts = new Dictionary<UIEffectRenderer, Layout>();
        private static CanvasEffectOutput SharedRenderer(UIEffectRenderer p, int slot)
        {
            var r = p.GetRendererIfExists(slot);
            return r && r.isActiveAndEnabled && !r.isBridge ? r : null;
        }
        private static void CheckLayout(UIEffectRenderer p)
        {
            if (!p) { planDirty = true; return; }
            if (!layouts.TryGetValue(p, out var layout))
            { layout = new Layout(); layouts.Add(p, layout); planDirty = true; }
            bool shared = Eligible(p) && p.useMeshSharing;
            int count = shared ? p.activeRendererCount : 0;
            bool changed = layout.shared != shared || (shared && layout.group != p.sharingGroup)
                || layout.renderers.Count != count;
            if (!changed) for (int i = 0; i < count; ++i)
                if (!ReferenceEquals(layout.renderers[i], SharedRenderer(p, i))) { changed = true; break; }
            if (!changed) return;
            planDirty = true; layout.shared = shared; layout.group = p.sharingGroup;
            layout.renderers.Clear();
            for (int i = 0; i < count; ++i) layout.renderers.Add(SharedRenderer(p, i));
        }
        private static void RebuildPlanIfNeeded()
        {
            if (!planDirty) return;
            ClearPlan();
            foreach (var p in frame)
            {
                if (!Eligible(p) || !p.useMeshSharing) continue;
                if (!groups.TryGetValue(p.sharingGroup, out var g))
                { g = spareGroups.Count == 0 ? new Group() : spareGroups.Pop(); groups.Add(p.sharingGroup, g); }
                g.members.Add(p);
                for (int slot = 0; slot < p.activeRendererCount; ++slot)
                {
                    while (g.slots.Count <= slot) g.slots.Add(new List<(UIEffectRenderer, CanvasEffectOutput)>());
                    var r = SharedRenderer(p, slot); if (r) g.slots[slot].Add((p, r));
                }
            }
            planDirty = false; ++UIEffectProfiler.current.groupPlanRebuilds;
        }
        public static void Register(UIEffectRenderer p) { if (p && !registered.Contains(p)) { p.independentBakePhase = BakeStagger.Allocate(); p.particleBakePhase = p.independentBakePhase; registered.Add(p); planDirty = true; MarkGeometryDirty(p); } }
        public static void Unregister(UIEffectRenderer p)
        {
            if (ReferenceEquals(p, null)) return;
            DirtyPrevious(p); registered.Remove(p); bindings.Remove(p); reported.Remove(p); layouts.Remove(p); planDirty = true;
        }
        public static void Register(UIParticleAttractor p) { if (p && !attractors.Contains(p)) attractors.Add(p); }
        public static void Unregister(UIParticleAttractor p) => attractors.Remove(p);
        private static void DirtyPrevious(UIEffectRenderer p)
        {
            if (bindings.TryGetValue(p, out var old) && old.shared) dirtyGroups.Add(old.group);
            if (p && p.useMeshSharing) dirtyGroups.Add(p.sharingGroup);
        }
        internal static void MarkGeometryDirty(UIEffectRenderer p)
        {
            if (!p) return;
            p.InvalidateRendererCaches();
            if (!registered.Contains(p)) return;
            DirtyPrevious(p);
            if (p.useMeshSharing) foreach (var member in registered)
                if (member && member.useMeshSharing && member.sharingGroup == p.sharingGroup) member.InvalidateRendererCaches();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            registered.Clear(); attractors.Clear(); frame.Clear(); frameAttractors.Clear();
            ClearPlan(); bindings.Clear(); dirtyGroups.Clear(); reported.Clear(); failed.Clear();
            s_FrameCount = -1; executing = false; BakeStagger.Reset(); layouts.Clear(); planDirty = true;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install() { CanvasFramePump.AfterLayout -= Refresh; CanvasFramePump.AfterLayout += Refresh; }
#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void InstallEditor() { Install(); EditorApplication.playModeStateChanged += _ => { s_FrameCount = -1; Install(); }; }
#endif
        private static bool Eligible(UIEffectRenderer p) => p && p.isActiveAndEnabled && p.supportsCanvasRendering && !failed.Contains(p);
        private static void ClearPlan()
        {
            foreach (var group in groups.Values) { group.Clear(); spareGroups.Push(group); }
            groups.Clear();
        }
        private static void Refresh()
        {
            // Scene/Game repaints and Inspector edits can happen without a game frame
            // advancing. Only runtime simulation uses the once-per-frame gate.
            if (executing) return;
            if (Application.isPlaying && s_FrameCount == Time.frameCount)
            {
                // Canvas.ForceUpdateCanvases can reorder UI more than once in a game
                // frame. Reconcile boundaries without simulating/baking twice.
                executing = true;
                try { DepthGroupPlanner.Refresh(registered); }
                finally { executing = false; }
                return;
            }
            s_FrameCount = Time.frameCount; executing = true; UIEffectProfiler.BeginFrame(Time.frameCount);
            try { ExecuteFrame(); }
            catch { planDirty = true; throw; }
            finally
            {
                executing = false; failed.Clear(); frame.Clear(); frameAttractors.Clear(); UIEffectProfiler.EndFrame();
            }
        }
        private static void Guard(UIEffectRenderer p, Action<UIEffectRenderer> action)
        {
            try { action(p); }
            catch (Exception error)
            {
                failed.Add(p); DirtyPrevious(p);
                if (p) p.ClearRendererMeshes();
                if (p && reported.Add(p)) Debug.LogException(error, p);
            }
        }
        private static void ExecuteFrame()
        {
            SpriteMaskResolver.BeginFrame(); var start = UIEffectProfiler.Timestamp();
            for (int i = registered.Count - 1; i >= 0; --i)
                if (!registered[i]) Unregister(registered[i]);
            frame.AddRange(registered); frameAttractors.AddRange(attractors);
            foreach (var p in frame)
            {
                // Also prepare unsupported canvases once to release native source leases
                // after a runtime Canvas mode/camera change.
                if (p && p.isActiveAndEnabled) Guard(p, Prepare);
                CheckLayout(p);
            }
            RebuildPlanIfNeeded();
            foreach (var g in groups.Values)
            {
                g.separate = false;
                UIEffectRenderer first = null;
                foreach (var p in g.members) if (Eligible(p))
                {
                    if (!first) first = p;
                    else g.separate |= !first.HasSameParticlePlan(p);
                }
                foreach (var p in g.members)
                {
                    if (!Eligible(p)) continue;
                    Guard(p, g.separate ? ApplySeparateLayout : ApplyCandidateLayout);
                    CheckLayout(p);
                }
            }
            // Fallback can replace renderers or change the automatic group identity.
            RebuildPlanIfNeeded();
            foreach (var g in groups.Values)
            {
                g.owner = null; g.hidden = g.clipped = true;
                foreach (var p in g.members)
                {
                    if (!Eligible(p)) continue;
                    if (p.canSimulate && (!g.owner || (!g.owner.isPrimary && p.isPrimary))) g.owner = p;
                    if (UIEffectRenderer.earlyCull > 0)
                    { p.GetOutputVisibility(out var output, out var hidden, out var clipped); if (output) { g.hidden &= hidden; g.clipped &= clipped; } }
                    else g.hidden = g.clipped = false;
                }
            }
            BakeStagger.Prepare(frame);
            foreach (var p in frame)
            {
                if (!Eligible(p)) continue;
                Group g = null; if (p.useMeshSharing) groups.TryGetValue(p.sharingGroup, out g);
                if (p.SetSimulationOwner(!p.useMeshSharing || (g != null && g.owner == p))) p.InvalidateRendererCaches();
                bool hidden = g != null && g.hidden, clipped = g != null && g.clipped;
                if ((p.groupAllAlphaHidden && !hidden) || (p.groupAllClipped && !clipped) || dirtyGroups.Contains(p.sharingGroup)) p.InvalidateRendererCaches();
                p.groupAllAlphaHidden = hidden; p.groupAllClipped = clipped;
                UIEffectProfiler.current.activeRenderers += p.activeRendererCount; UIEffectProfiler.current.mergedRenderers += p.mergedRendererCount;
                if (p.hasUnmergedFallback) UIEffectProfiler.current.fallbackEffects++;
            }
            dirtyGroups.Clear(); UIEffectProfiler.current.prepareMs = UIEffectProfiler.Milliseconds(start);
            foreach (var p in frame) if (Eligible(p)) Guard(p, x => x.UpdateBridgeRenderers());
            foreach (var p in frame)
            {
                if (!Eligible(p)) continue;
                if (!p.useMeshSharing) Guard(p, Update);
                else if (groups.TryGetValue(p.sharingGroup, out var g))
                {
                    if (g.owner == p) Guard(p, Update);
                    else if (!g.owner || !Eligible(g.owner)) p.ClearRendererMeshes();
                }
            }
            // A producer may fail after earlier replicas have been visited. Clear the whole group.
            foreach (var g in groups.Values)
                if (!g.owner || !Eligible(g.owner))
                    foreach (var p in g.members) if (p) p.ClearRendererMeshes();
            foreach (var a in frameAttractors) if (a && a.isActiveAndEnabled) a.Attract();
            DepthGroupPlanner.Refresh(frame);
        }
        private static void Update(UIEffectRenderer p) { p.UpdateRenderers(); reported.Remove(p); }
        private static void ApplySeparateLayout(UIEffectRenderer p)
        { if (p.ApplySharingLayout(true)) MarkGeometryDirty(p); }
        private static void ApplyCandidateLayout(UIEffectRenderer p)
        { if (p.ApplySharingLayout(false)) MarkGeometryDirty(p); }

        // All consumers evaluate the producer's captured workload, not their unsimulated local particles.
        internal static UIEffectRenderer GetMergeSampleOwner(UIEffectRenderer source)
        {
            UIEffectRenderer candidate = null;
            foreach (var p in registered)
            {
                if (!Eligible(p) || !p.useMeshSharing || p.sharingGroup != source.sharingGroup || !p.canSimulate) continue;
                if (!candidate || (!candidate.isPrimary && p.isPrimary)) candidate = p;
            }
            return candidate;
        }
        private static void Prepare(UIEffectRenderer p)
        {
            bool changed = p.PrepareForUpdate(); var state = (p.sharingGroup, p.useMeshSharing);
            if (changed || !bindings.TryGetValue(p, out var old) || old != state) { DirtyPrevious(p); p.InvalidateRendererCaches(); }
            bindings[p] = state;
        }
        public static void GetGroupedRenderers(int id, int slot, List<CanvasEffectOutput> result)
        {
            result.Clear();
            if (executing && groups.TryGetValue(id, out var g))
            {
                if (slot >= 0 && slot < g.slots.Count) foreach (var consumer in g.slots[slot])
                {
                    var p = consumer.effect; var r = consumer.renderer;
                    if (Eligible(p) && p.useMeshSharing && p.sharingGroup == id
                        && r && r.isActiveAndEnabled && !r.isBridge
                        && p.GetRendererIfExists(slot) == r) result.Add(r);
                }
                return;
            }
            foreach (var p in registered)
            {
                if (!Eligible(p) || !p.useMeshSharing || p.sharingGroup != id) continue;
                var r = p.GetRendererIfExists(slot); if (r && r.isActiveAndEnabled && !r.isBridge) result.Add(r);
            }
        }
        internal static UIEffectRenderer GetPrimary(int id)
        {
            if (executing && groups.TryGetValue(id, out var g))
                return Eligible(g.owner) && g.owner.useMeshSharing && g.owner.sharingGroup == id && g.owner.canSimulate ? g.owner : null;
            UIEffectRenderer candidate = null;
            foreach (var p in registered)
            {
                if (!Eligible(p) || !p.useMeshSharing || p.sharingGroup != id || !p.canSimulate) continue;
                if (p.isPrimary) return p; if (!candidate) candidate = p;
            }
            return candidate;
        }
        internal static void RequestUnmergedFallback(UIEffectRenderer source)
        {
            foreach (var p in registered) if (p && (p == source || (source.useMeshSharing && p.useMeshSharing && p.sharingGroup == source.sharingGroup)))
            { p.RequestUnmergedFallback(); DirtyPrevious(p); }
        }
    }
}
