using System.Collections.Generic;
using UnityEngine;

namespace ShanFlyer.UIEffects
{
    // Build the complete desired layout before releasing any source suppression.
    internal sealed class EffectOutputPlan
    {
        private sealed class Entry
        {
            internal ParticleSystem source, emitter;
            internal Renderer bridge;
            internal int materialSlot;
            internal bool trail, merged, retained;
            internal readonly List<ParticleSystem> sources = new List<ParticleSystem>();
            internal void Clear() { source = emitter = null; bridge = null; materialSlot = 0; trail = merged = retained = false; sources.Clear(); }
        }
        private readonly List<Entry> entries = new List<Entry>();
        private readonly List<ParticleSystem> run = new List<ParticleSystem>();
        private readonly HashSet<CanvasEffectOutput> retained = new HashSet<CanvasEffectOutput>();
        private int count;
        private Entry Next()
        {
            if (count == entries.Count) entries.Add(new Entry());
            var entry = entries[count++]; entry.Clear(); return entry;
        }
        internal void Build(UIEffectRenderer owner, List<ParticleSystem> sources, bool allowMerge)
        {
            Clear();
            foreach (var source in sources)
            {
                if (!source || !source.TryGetComponent<ParticleSystemRenderer>(out _)) continue;
                if (!allowMerge) { Single(owner, source); continue; }
                run.Add(source);
                if (CanvasEffectOutput.CanMerge(run)) continue;
                run.RemoveAt(run.Count - 1); Flush(owner); run.Add(source);
                if (!CanvasEffectOutput.CanMerge(run)) Flush(owner);
            }
            Flush(owner);
        }
        private void Single(UIEffectRenderer owner, ParticleSystem source)
        {
            var entry = Next(); entry.source = source; entry.emitter = owner.sourceTopology.ParentOf(source);
            if (!source.trails.enabled) return;
            entry = Next(); entry.source = source; entry.emitter = owner.sourceTopology.ParentOf(source); entry.trail = true;
        }
        private void Flush(UIEffectRenderer owner)
        {
            if (run.Count == 0) return;
            if (CanvasEffectOutput.CanMerge(run)) { var entry = Next(); entry.merged = true; entry.sources.AddRange(run); }
            else foreach (var source in run) Single(owner, source);
            run.Clear();
        }
        internal void AddBridge(Renderer source)
        {
            int count = CanvasEffectOutput.BridgeOutputCount(source);
            for (int slot = 0; slot < count; ++slot) { var entry = Next(); entry.bridge = source; entry.materialSlot = slot; }
        }
        internal void Apply(UIEffectRenderer owner)
        {
            try
            {
                for (int i = 0; i < count; ++i)
                {
                    var entry = entries[i]; var output = owner.GetRendererIfExists(i);
                    entry.retained = output && output.CanRetainBinding(owner, entry.source, entry.emitter,
                        entry.trail, entry.merged ? entry.sources : null, entry.bridge, entry.materialSlot);
                    if (entry.retained) retained.Add(output);
                }
                owner.ReleaseChangedOutputs(retained);
                for (int i = 0; i < count; ++i)
                {
                    var entry = entries[i]; var output = owner.GetRenderer(i);
                    if (entry.retained) { output.RefreshRetainedBinding(owner, i); continue; }
                    if (entry.bridge) output.SetBridge(owner, entry.bridge, entry.materialSlot);
                    else if (entry.merged) output.SetMerged(owner, entry.sources);
                    else output.Set(owner, entry.source, entry.trail, entry.emitter);
                }
            }
            finally { Clear(); }
        }
        internal void Clear()
        {
            for (int i = 0; i < count; ++i) entries[i].Clear();
            count = 0; run.Clear(); retained.Clear();
        }
    }
}
