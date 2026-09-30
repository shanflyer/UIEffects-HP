using System.Collections.Generic;
using UnityEngine;

namespace ShanFlyer.UIEffects
{
    // Observations from normal captures; deciding whether to merge never performs a probe bake.
    internal struct ParticleMergeSample
    {
        internal int vertices, observations;
        internal long bytes;
        internal Bounds bounds;
        internal double sampledAt;
        internal bool hidden, direct;

        internal void Record(Mesh mesh, Matrix4x4 mapping, bool hidden)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (observations >= 3 && now - sampledAt < .1) return;
            vertices = mesh.vertexCount;
            long stride = 0;
            for (int i = 0; i < mesh.vertexBufferCount; ++i) stride += mesh.GetVertexBufferStride(i);
            bytes = stride * vertices;
            for (int i = 0; i < mesh.subMeshCount; ++i)
                bytes += (long)mesh.GetIndexCount(i) * (mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16 ? 2 : 4);
            bounds = EffectScale.TransformBounds(mesh.bounds, mapping);
            this.hidden = hidden;
            direct = mapping.Equals(Matrix4x4.identity);
            observations = vertices > 0 ? Mathf.Min(observations + 1, 3) : 0;
            sampledAt = now;
        }
    }

    internal static class ParticleMergePolicy
    {
        // Conservative workload budgets, not a claim about GPU time or guaranteed speedup.
        // Keeping an existing group gets more headroom than creating a new one (hysteresis).
        internal static bool WorthMerging(UIEffectRenderer owner, UIEffectRenderer sampleOwner,
            List<ParticleSystem> sources, bool keeping)
        {
            if (!sampleOwner || (!owner.canRender && !owner.useMeshSharing)) return false;
            // A paused, already merged group is cached. Rebinding it cannot save frame work.
            if (sampleOwner.isPaused) return keeping;
            int outputs = 0, direct = 0;
            long bytes = 0, vertices = 0;
            float area = 0;
            Bounds union = default;
            foreach (var source in sources)
            {
                if (!source || !source.gameObject.activeInHierarchy) return false;
                int index = owner.particles.IndexOf(source);
                if (index < 0 || index >= sampleOwner.particles.Count) return false;
                var binding = ParticleSourceBinding.Find(sampleOwner.particles[index]);
                if (binding == null || !binding.source.gameObject.activeInHierarchy) return false;
                if (!Add(binding.bodySample, ref outputs, ref direct, ref bytes, ref vertices, ref area, ref union)) return false;
                if (source.trails.enabled && !Add(binding.trailSample, ref outputs, ref direct,
                        ref bytes, ref vertices, ref area, ref union)) return false;
            }
            if (outputs < 2 || vertices > 48000) return false;
            long budget = (keeping ? 192L : 96L) * 1024 * (outputs - 1);
            // Direct individual captures avoid a geometry copy entirely. Merging loses that fast path.
            if (direct > 0) budget /= 2;
            if (bytes > System.Math.Min(budget, 1024L * 1024)) return false;
            float unionArea = union.size.x * union.size.y;
            return float.IsFinite(unionArea) && area > 0 && unionArea <= area * (keeping ? 3f : 2f);
        }

        private static bool Add(ParticleMergeSample sample, ref int outputs, ref int direct,
            ref long bytes, ref long vertices, ref float area, ref Bounds union)
        {
            if (sample.observations < 3 || sample.vertices == 0 || sample.hidden
                || Time.realtimeSinceStartupAsDouble - sample.sampledAt > 1.5) return false;
            if (outputs++ == 0) union = sample.bounds; else union.Encapsulate(sample.bounds);
            if (sample.direct) ++direct;
            vertices += sample.vertices;
            bytes += sample.bytes;
            area += sample.bounds.size.x * sample.bounds.size.y;
            return true;
        }
    }
}
