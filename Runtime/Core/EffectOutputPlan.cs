using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShanFlyer.UIEffects
{
    // Decide in draw order, then store particle slots in canonical source order for sharing.
    internal sealed class EffectOutputPlan
    {
        private sealed class Source
        {
            internal Renderer renderer;
            internal ParticleSystem particle;
            internal int ordinal;
        }
        private sealed class Entry
        {
            internal ParticleSystem source, emitter;
            internal Renderer bridge;
            internal int materialSlot, ordinal;
            internal bool trail, retained;
            internal readonly List<ParticleSystem> sources = new List<ParticleSystem>();
            internal void Clear()
            { source = emitter = null; bridge = null; materialSlot = ordinal = 0; trail = retained = false; sources.Clear(); }
        }
        private readonly List<Source> ordered = new List<Source>();
        private readonly Stack<Source> spareSources = new Stack<Source>();
        private readonly List<Entry> entries = new List<Entry>(), applied = new List<Entry>();
        private readonly List<ParticleSystem> run = new List<ParticleSystem>();
        private readonly List<ParticleSourceBinding> leases = new List<ParticleSourceBinding>();
        private readonly HashSet<CanvasEffectOutput> retained = new HashSet<CanvasEffectOutput>();
        private readonly HashSet<ParticleSystem> unique = new HashSet<ParticleSystem>();
        private UIEffectRenderer owner;
        private readonly Comparison<Source> compareSources;
        private int count, appliedCount, nextOrdinal;
        private bool allowMerge;
        private double nextReview;
        internal int particleOutputCount { get; private set; }

        internal EffectOutputPlan() { compareSources = CompareSources; }
        private int CompareSources(Source a, Source b)
        {
            int order = owner.CompareSourceRenderers(a.renderer, b.renderer);
            return order != 0 ? order : a.ordinal.CompareTo(b.ordinal);
        }
        private static Entry Next(List<Entry> pool, ref int used)
        {
            if (used == pool.Count) pool.Add(new Entry());
            var entry = pool[used++]; entry.Clear(); return entry;
        }
        internal void Build(UIEffectRenderer owner, List<ParticleSystem> sources, bool allowMerge)
        {
            Clear();
            this.owner = owner;
            this.allowMerge = allowMerge;
            for (int i = 0; i < sources.Count; ++i)
            {
                var source = sources[i];
                if (!source || !source.TryGetComponent<ParticleSystemRenderer>(out var renderer) || !unique.Add(source)) continue;
                AddSource(renderer, source, i);
                particleOutputCount += source.trails.enabled ? 2 : 1;
            }
            nextOrdinal = sources.Count;
        }
        private void AddSource(Renderer renderer, ParticleSystem particle, int ordinal)
        {
            var source = spareSources.Count > 0 ? spareSources.Pop() : new Source();
            source.renderer = renderer; source.particle = particle; source.ordinal = ordinal; ordered.Add(source);
        }
        internal void AddBridge(Renderer source) => AddSource(source, null, nextOrdinal++);

        internal void Finish()
        {
            if (owner.sortBySourceOrder) ordered.Sort(compareSources);
            var sampleOwner = owner.useMeshSharing ? UIEffectScheduler.GetMergeSampleOwner(owner) : owner;
            foreach (var source in ordered)
            {
                if (!source.particle)
                {
                    Flush(sampleOwner);
                    int slots = CanvasEffectOutput.BridgeOutputCount(source.renderer);
                    for (int i = 0; i < slots; ++i)
                    {
                        var entry = Next(entries, ref count);
                        entry.bridge = source.renderer; entry.materialSlot = i; entry.ordinal = source.ordinal;
                    }
                    continue;
                }
                if (!allowMerge) { Single(source.particle, source.ordinal); continue; }
                run.Add(source.particle);
                // Split incompatible runs, and split large workloads instead of rejecting all small neighbours.
                if (CanvasEffectOutput.CanMerge(run) && (run.Count == 1
                    || ParticleMergePolicy.WorthMerging(owner, sampleOwner, run, owner.ContainsMergedRun(run)))) continue;
                run.RemoveAt(run.Count - 1);
                Flush(sampleOwner);
                run.Add(source.particle);
            }
            Flush(sampleOwner);
            entries.Sort(0, count, EntryComparer.Instance);
            nextReview = Time.realtimeSinceStartupAsDouble + .5;
        }
        private sealed class EntryComparer : IComparer<Entry>
        {
            internal static readonly EntryComparer Instance = new EntryComparer();
            public int Compare(Entry a, Entry b)
            {
                int order = a.ordinal.CompareTo(b.ordinal);
                if (order != 0) return order;
                order = a.trail.CompareTo(b.trail);
                return order != 0 ? order : a.materialSlot.CompareTo(b.materialSlot);
            }
        }
        private void Single(ParticleSystem source, int ordinal)
        {
            var entry = Next(entries, ref count);
            entry.source = source; entry.emitter = owner.sourceTopology.ParentOf(source); entry.ordinal = ordinal;
            if (!source.trails.enabled) return;
            entry = Next(entries, ref count);
            entry.source = source; entry.emitter = owner.sourceTopology.ParentOf(source); entry.trail = true; entry.ordinal = ordinal;
        }
        private void Flush(UIEffectRenderer sampleOwner)
        {
            if (run.Count == 0) return;
            if (CanvasEffectOutput.CanMerge(run)
                && ParticleMergePolicy.WorthMerging(owner, sampleOwner, run, owner.ContainsMergedRun(run)))
            {
                var entry = Next(entries, ref count);
                entry.sources.AddRange(run);
                entry.ordinal = int.MaxValue;
                foreach (var source in run) entry.ordinal = Math.Min(entry.ordinal, owner.particles.IndexOf(source));
            }
            else foreach (var source in run) Single(source, owner.particles.IndexOf(source));
            run.Clear();
        }
        internal bool NeedsReview(UIEffectRenderer effect)
        {
            // Sorting changes must split a merged group before it crosses another output.
            if (effect.sortBySourceOrder)
                for (int i = 1; i < ordered.Count; ++i)
                    if (!ordered[i - 1].renderer || !ordered[i].renderer
                        || CompareSources(ordered[i - 1], ordered[i]) > 0) return true;
            return allowMerge && particleOutputCount > 1 && Time.realtimeSinceStartupAsDouble >= nextReview;
        }
        internal bool SameParticleLayout(EffectOutputPlan other)
        {
            int left = 0, right = 0;
            while (true)
            {
                while (left < count && entries[left].bridge) ++left;
                while (right < other.count && other.entries[right].bridge) ++right;
                if (left == count || right == other.count) return left == count && right == other.count;
                var a = entries[left++]; var b = other.entries[right++];
                if (a.trail != b.trail || a.ordinal != b.ordinal || a.sources.Count != b.sources.Count) return false;
                for (int i = 0; i < a.sources.Count; ++i)
                    if (owner.particles.IndexOf(a.sources[i]) != other.owner.particles.IndexOf(b.sources[i])) return false;
            }
        }
        private void PrepareApplied(bool separate)
        {
            foreach (var entry in applied) entry.Clear();
            appliedCount = 0;
            for (int i = 0; i < count; ++i)
            {
                var entry = entries[i];
                if (separate && entry.sources.Count > 0)
                {
                    foreach (var source in entry.sources)
                    {
                        var single = Next(applied, ref appliedCount);
                        single.source = source; single.emitter = owner.sourceTopology.ParentOf(source);
                        single.ordinal = owner.particles.IndexOf(source);
                        if (!source.trails.enabled) continue;
                        single = Next(applied, ref appliedCount);
                        single.source = source; single.emitter = owner.sourceTopology.ParentOf(source);
                        single.ordinal = owner.particles.IndexOf(source); single.trail = true;
                    }
                }
                else
                {
                    var target = Next(applied, ref appliedCount);
                    target.source = entry.source; target.emitter = entry.emitter; target.bridge = entry.bridge;
                    target.materialSlot = entry.materialSlot; target.ordinal = entry.ordinal; target.trail = entry.trail;
                    target.sources.AddRange(entry.sources);
                }
            }
            // Expanding a group must restore original particle slots even if its sources were sorted differently.
            applied.Sort(0, appliedCount, EntryComparer.Instance);
        }
        internal bool Apply(UIEffectRenderer owner, bool separate = false, bool invalidate = true)
        {
            PrepareApplied(separate);
            bool changed = owner.activeRendererCount != appliedCount;
            try
            {
                for (int i = 0; i < appliedCount; ++i)
                {
                    var entry = applied[i]; var output = owner.GetRendererIfExists(i);
                    entry.retained = output && output.CanRetainBinding(owner, entry.source, entry.emitter,
                        entry.trail, entry.sources.Count > 0 ? entry.sources : null, entry.bridge, entry.materialSlot);
                    if (entry.retained) retained.Add(output); else changed = true;
                }
                if (!changed && !invalidate) return false;
                // Keep source simulation sessions alive while outputs change their grouping/slots.
                foreach (var source in owner.particles)
                {
                    if (!source) continue;
                    var lease = ParticleSourceBinding.Acquire(source);
                    if (lease != null) leases.Add(lease);
                }
                owner.ReleaseChangedOutputs(retained);
                for (int i = 0; i < appliedCount; ++i)
                {
                    var entry = applied[i]; var output = owner.GetRenderer(i);
                    if (entry.retained)
                    {
                        if (invalidate) output.RefreshRetainedBinding(owner, i);
                        continue;
                    }
                    if (entry.bridge) output.SetBridge(owner, entry.bridge, entry.materialSlot);
                    else if (entry.sources.Count > 0) output.SetMerged(owner, entry.sources);
                    else output.Set(owner, entry.source, entry.trail, entry.emitter);
                }
                if (changed) ++UIEffectProfiler.current.mergeLayoutChanges;
                return changed;
            }
            finally
            {
                foreach (var lease in leases) lease.Release();
                leases.Clear(); retained.Clear();
            }
        }
        internal void Clear()
        {
            foreach (var source in ordered)
            { source.renderer = null; source.particle = null; spareSources.Push(source); }
            ordered.Clear();
            foreach (var entry in entries) entry.Clear();
            foreach (var entry in applied) entry.Clear();
            count = appliedCount = particleOutputCount = 0;
            run.Clear(); unique.Clear(); retained.Clear();
        }
    }
}
