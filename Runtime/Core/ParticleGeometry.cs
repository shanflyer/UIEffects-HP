using UnityEngine;

namespace ShanFlyer.UIEffects
{
    internal static class ParticleGeometry
    {
        // Capture has no authority to modify emission, shape, transform or simulation state.
        internal static void Capture(ParticleSystem source, ParticleSystemRenderer renderer,
            bool trail, Camera view, Mesh target)
        {
            target.Clear(false);
            if (!view || !renderer || !source || !source.gameObject.activeInHierarchy) return;
            // Trails can outlive their particles when Die With Particles is disabled.
            if (!trail && source.particleCount == 0) return;
            if (trail)
            {
                if (!source.trails.enabled) return;
#if UNITY_6000_0_OR_NEWER
                renderer.BakeTrailsMesh(target, view, ParticleSystemBakeMeshOptions.BakeRotationAndScale);
#else
                renderer.BakeTrailsMesh(target, view, true);
#endif
            }
            else
            {
                switch (renderer.renderMode)
                {
                    case ParticleSystemRenderMode.None: return;
                    case ParticleSystemRenderMode.Mesh when !renderer.mesh: return;
                }
#if UNITY_6000_0_OR_NEWER
                renderer.BakeMesh(target, view, ParticleSystemBakeMeshOptions.BakeRotationAndScale);
#else
                renderer.BakeMesh(target, view, true);
#endif
            }
            int count = target.vertexCount;
            UIEffectRenderer.s_FrameBakedVerts += count;
            UIEffectRenderer.s_FrameBakeOps++;
            UIEffectProfiler.current.bakedVertices += count;
            UIEffectProfiler.current.bakeOps++;
            // Do not send non-finite geometry into Canvas batching.
            if (count != 0 && (!EffectScale.IsFinite(target.bounds.center) || !EffectScale.IsFinite(target.bounds.extents)))
                target.Clear(false);
        }
    }
}
