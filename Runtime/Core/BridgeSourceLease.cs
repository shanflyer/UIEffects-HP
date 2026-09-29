using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using ShanFlyer.UIEffects.Internal;
namespace ShanFlyer.UIEffects
{
    // Multiple material outputs share suppression ownership and one skinned snapshot.
    internal sealed class BridgeSourceLease
    {
        private static readonly Dictionary<Renderer, BridgeSourceLease> leases = new Dictionary<Renderer, BridgeSourceLease>();
        private Renderer source;
        private int owners, capturedFrame = -1;
        internal bool originallyHidden;
        private bool offscreen;
        private Mesh skinMesh;
        internal static BridgeSourceLease Acquire(Renderer source)
        {
            if (!leases.TryGetValue(source, out var lease))
            {
                lease = new BridgeSourceLease { source = source, originallyHidden = source.forceRenderingOff };
                if (source is SkinnedMeshRenderer skin) { lease.offscreen = skin.updateWhenOffscreen; skin.updateWhenOffscreen = true; }
                leases.Add(source, lease);
            }
            ++lease.owners; source.forceRenderingOff = true; return lease;
        }
        internal void Invalidate() { capturedFrame = -1; }
        internal Mesh Capture(SkinnedMeshRenderer skin)
        {
            if (!skinMesh) skinMesh = new Mesh { name = "UI skinned snapshot", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            if (capturedFrame != Time.frameCount)
            {
                skinMesh.Clear(false);
                var started = UIEffectProfiler.Timestamp();
                skin.BakeMesh(skinMesh, false);
                UIEffectProfiler.EndStage(2, started);
                ++UIEffectProfiler.current.bakeOps; ++UIEffectRenderer.s_FrameBakeOps;
                UIEffectProfiler.current.bakedVertices += skinMesh.vertexCount; UIEffectRenderer.s_FrameBakedVerts += skinMesh.vertexCount;
                capturedFrame = Time.frameCount;
            }
            return skinMesh;
        }
        internal void Release()
        {
            if (--owners != 0) return;
            leases.Remove(source);
            if (source)
            {
                source.forceRenderingOff = originallyHidden;
                if (source is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = offscreen;
            }
            EngineObjects.Destroy(skinMesh); skinMesh = null; source = null;
        }
    }
}
