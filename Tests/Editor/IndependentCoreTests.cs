using System;
using System.Reflection;
using NUnit.Framework;
using ShanFlyer.UIEffects.Internal;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
namespace ShanFlyer.UIEffects.Editor.Tests
{
    public class IndependentCoreTests
    {
        private GameObject root;
        private Material material;
        private UIEffectRenderer effect;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [SetUp] public void Setup()
        {
            root = new GameObject("Independent tests", typeof(Canvas));
            var go = new GameObject("effect", typeof(RectTransform)); go.transform.SetParent(root.transform, false);
            var emitter = new GameObject("source", typeof(ParticleSystem)); emitter.transform.SetParent(go.transform, false);
            material = new Material(Shader.Find("UI/Default")); emitter.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            effect = go.AddComponent<UIEffectRenderer>(); effect.unitConversion = global::ShanFlyer.UIEffects.UIEffectRenderer.UnitConversion.Manual; effect.uniformScale = 1; effect.scaleMode = UIEffectRenderer.ScaleMode.Hierarchy;
            UIEffectRenderer.mergeRenderers = false; UIEffectRenderer.staticMeshCache = false; UIEffectRenderer.updateRatePercent = 100; UIEffectRenderer.earlyCull = 0;
            effect.RefreshSources(); effect.PrepareForUpdate();
            var ps = effect.particles[0]; var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.Local;
            ps.Play(); ps.Pause();
            ps.SetParticles(new[] { new ParticleSystem.Particle { position = Vector3.zero, startSize = 1, startColor = Color.white, remainingLifetime = 10, startLifetime = 10 } });
        }
        [TearDown] public void Cleanup() { Object.DestroyImmediate(root); Object.DestroyImmediate(material); }
        private void Tick()
        {
            typeof(UIEffectScheduler).GetField("s_FrameCount", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, -1);
            typeof(UIEffectScheduler).GetMethod("Refresh", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        }
        [Test] public void RuntimeAssemblyUsesOnlyEngineAndSystemDependencies()
        {
            Assert.AreEqual("ShanFlyer.UIEffects", typeof(UIEffectRenderer).Assembly.GetName().Name);
            foreach (var reference in typeof(UIEffectRenderer).Assembly.GetReferencedAssemblies())
            {
                string name = reference.Name;
                Assert.IsTrue(name.StartsWith("UnityEngine", StringComparison.Ordinal)
                    || name.StartsWith("UnityEditor", StringComparison.Ordinal)
                    || name.StartsWith("Unity.", StringComparison.Ordinal)
                    || name == "mscorlib" || name == "netstandard" || name == "System"
                    || name.StartsWith("System.", StringComparison.Ordinal), name);
            }
        }
        [Test] public void IdentitySourceUsesDirectBakeAndSubmitsVertices()
        {
            Tick(); Assert.Greater(UIEffectProfiler.completed.directBakeOps, 0);
            var mesh = (Mesh)typeof(CanvasEffectOutput).GetField("_outputMesh", Private).GetValue(effect.GetRendererIfExists(0)); Assert.Greater(mesh.vertexCount, 0); Assert.Greater(UIEffectProfiler.completed.setMeshOps, 0);
        }
        [Test] public void ScaledSourceUsesTransformPath()
        {
            effect.uniformScale = 7; Tick(); Assert.AreEqual(0, UIEffectProfiler.completed.directBakeOps);
            Assert.Greater(UIEffectProfiler.completed.bakeOps, 0);
        }
        [Test] public void IndependentProducersOwnDifferentMeshes()
        {
            var second = Object.Instantiate(effect, root.transform); second.RefreshSources(); second.PrepareForUpdate();
            var ps = second.particles[0]; ps.Play(); ps.Pause(); ps.SetParticles(new[] { new ParticleSystem.Particle { startSize = 1, startColor = Color.white, remainingLifetime = 10, startLifetime = 10 } }); Tick();
            var field = typeof(CanvasEffectOutput).GetField("_outputMesh", Private);
            var firstMesh = field.GetValue(effect.GetRendererIfExists(0)); var secondMesh = field.GetValue(second.GetRendererIfExists(0));
            Assert.IsNotNull(firstMesh); Assert.IsNotNull(secondMesh); Assert.AreNotSame(firstMesh, secondMesh);
        }
        [Test] public void MaterialLeaseLivesUntilFinalConsumerReleases()
        {
            var first = new CanvasMaterialBinding(); var second = new CanvasMaterialBinding();
            var a = first.Resolve(material, Texture2D.whiteTexture, null);
            var b = second.Resolve(material, Texture2D.whiteTexture, null);
            Assert.AreSame(a, b); first.Dispose(); Assert.IsTrue(b);
            second.Dispose(); Assert.IsFalse(b);
        }
        [Test] public void DirectBakeStillAppliesUiMeshModifier()
        {
            var output = effect.GetRendererIfExists(0); output.gameObject.AddComponent<ShiftOutput>();
            Tick();
            var mesh = (Mesh)typeof(CanvasEffectOutput).GetField("_outputMesh", Private).GetValue(output); Assert.Greater(mesh.bounds.center.x, 9);
        }
        [Test] public void SharedOutputKeepsItsOwnMaskableSetting()
        {
            effect.simulationRole = UIEffectRenderer.SimulationRole.Automatic;
            var copy = Object.Instantiate(effect, root.transform); copy.RefreshSources(); copy.maskable = false; copy.SetMaterialDirty();
            Tick(); Assert.AreEqual(effect.sharingGroup, copy.sharingGroup); Assert.IsFalse(copy.GetRendererIfExists(0).maskable);
        }
        [Test] public void DisablingOwnerElectsRemainingAutoInstance()
        {
            effect.simulationRole = UIEffectRenderer.SimulationRole.Automatic;
            var copy = Object.Instantiate(effect, root.transform); copy.RefreshSources(); Tick();
            effect.enabled = false; Tick(); Assert.AreSame(copy, UIEffectScheduler.GetPrimary(copy.sharingGroup));
        }
        [Test] public void IndependentEffectMergesConsecutiveMaterialRuns()
        {
            var alternate = new Material(material);
            try
            {
                for (int i = 0; i < 3; ++i)
                {
                    var source = new GameObject("additional " + i, typeof(ParticleSystem));
                    source.transform.SetParent(effect.transform, false);
                    source.GetComponent<ParticleSystemRenderer>().sharedMaterial = i == 0 ? material : alternate;
                }
                UIEffectRenderer.mergeRenderers = true; effect.RefreshSources();
                Assert.AreEqual(2, effect.activeRendererCount);
                Assert.AreEqual(2, effect.mergedRendererCount);
            }
            finally { UIEffectRenderer.mergeRenderers = false; Object.DestroyImmediate(alternate); }
        }
        [Test] public void SharedIncompatibleMaterialsRetainSeparateSlots()
        {
            var alternate = new Material(material);
            try
            {
                var source = new GameObject("alternate", typeof(ParticleSystem)); source.transform.SetParent(effect.transform, false);
                source.GetComponent<ParticleSystemRenderer>().sharedMaterial = alternate;
                effect.simulationRole = UIEffectRenderer.SimulationRole.Automatic; UIEffectRenderer.mergeRenderers = true; effect.RefreshSources();
                var copy = Object.Instantiate(effect, root.transform); copy.RefreshSources(); Tick();
                Assert.AreEqual(0, effect.mergedRendererCount); Assert.AreEqual(0, copy.mergedRendererCount);
                Assert.AreEqual(effect.activeRendererCount, copy.activeRendererCount);
            }
            finally { UIEffectRenderer.mergeRenderers = false; Object.DestroyImmediate(alternate); }
        }
        private sealed class ShiftOutput : BaseMeshEffect
        {
            public override void ModifyMesh(VertexHelper helper)
            {
                for (int i = 0; i < helper.currentVertCount; ++i)
                { var vertex = new UIVertex(); helper.PopulateUIVertex(ref vertex, i); vertex.position.x += 10; helper.SetUIVertex(vertex, i); }
            }
        }
    }
}
