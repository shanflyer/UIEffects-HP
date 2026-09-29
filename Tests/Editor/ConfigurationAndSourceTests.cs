using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShanFlyer.UIEffects.Editor.Tests
{
    public class ConfigurationAndSourceTests
    {
        private GameObject root;
        private UIEffectRenderer effect;
        private ParticleSystem source;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [SetUp] public void Setup()
        {
            root = new GameObject("Configuration tests", typeof(RectTransform), typeof(Canvas));
            var child = new GameObject("Effect", typeof(RectTransform)); child.transform.SetParent(root.transform, false);
            var particles = new GameObject("Source", typeof(ParticleSystem)); particles.transform.SetParent(child.transform, false);
            source = particles.GetComponent<ParticleSystem>(); source.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            effect = child.AddComponent<UIEffectRenderer>(); effect.unitConversion = global::ShanFlyer.UIEffects.UIEffectRenderer.UnitConversion.Manual;
        }
        [TearDown] public void Cleanup() => Object.DestroyImmediate(root);
        private void Set(string field, object value) => typeof(UIEffectRenderer).GetField(field, Private).SetValue(effect, value);

        [Test] public void InvalidPublicConfigurationIsRejectedWithoutChangingState()
        {
            Vector3 original = effect.renderScale;
            Assert.Throws<ArgumentOutOfRangeException>(() => effect.uniformScale = float.PositiveInfinity);
            Assert.Throws<ArgumentOutOfRangeException>(() => effect.renderScale = new Vector3(float.NaN, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => effect.simulationSpeed = -1);
            Assert.Throws<ArgumentOutOfRangeException>(() => effect.simulationRole = (UIEffectRenderer.SimulationRole)999);
            Assert.AreEqual(original, effect.renderScale);
            effect.renderScale = new Vector3(-2, 0, 3); Assert.AreEqual(-2, effect.renderScale.x);
        }
        [Test] public void DeserializationNormalizesMalformedFieldsWithoutNativeAccess()
        {
            Set("renderScaleValue", new Vector3(float.NaN, 1, 1)); Set("simulationSpeedValue", -1f);
            Set("simulationRoleValue", (UIEffectRenderer.SimulationRole)(-1));
            Set("particleSources", null); effect.propertyBindings = null;
            ((ISerializationCallbackReceiver)effect).OnAfterDeserialize();
            Assert.AreEqual(Vector3.one * 10, effect.renderScale); Assert.AreEqual(0, effect.simulationSpeed);
            Assert.AreEqual(UIEffectRenderer.SimulationRole.Independent, effect.simulationRole);
            Assert.IsNotNull(effect.particles); Assert.IsFalse(effect.hasMaterialPropertyBindings);
        }
        [Test] public void ConfigurationChangeInvalidatesGeometryButEqualAssignmentDoesNot()
        {
            var renderer = effect.GetRendererIfExists(0);
            var flag = typeof(CanvasEffectOutput).GetField("_forceBake", Private);
            flag.SetValue(renderer, false);
            effect.renderScale = effect.renderScale; Assert.IsFalse((bool)flag.GetValue(renderer));
            effect.uniformScale = 2; Assert.IsTrue((bool)flag.GetValue(renderer));
        }
        [TestCase(0f)] [TestCase(float.Epsilon)] [TestCase(float.PositiveInfinity)] [TestCase(float.NaN)]
        public void ScaleReciprocalCannotProduceNonfiniteValues(float value)
        {
            var inverse = EffectScale.Reciprocal(new Vector3(value, -2, 4));
            Assert.IsTrue(EffectScale.IsFinite(inverse)); Assert.AreEqual(1, inverse.x);
            Assert.AreEqual(-.5f, inverse.y); Assert.AreEqual(.25f, inverse.z);
        }
        [Test] public void TinyRepresentableScaleRetainsInverseAndCollapsedScaleIsNotVisible()
        {
            Assert.AreEqual(1e20f, EffectScale.Reciprocal(new Vector3(1e-20f, 1, 1)).x, 1e14f);
            Assert.IsFalse(EffectScale.HasVolume(new Vector3(1, 0, 1)));
            Assert.IsFalse(EffectScale.HasVolume(new Vector3(1, float.NaN, 1)));
            Assert.IsTrue(EffectScale.HasVolume(new Vector3(-1, 1, 1)));
        }
        [Test] public void ReadbackBuffersAreLazyIndependentAndReused()
        {
            var a = new ParticleReadback(); var b = new ParticleReadback();
            Assert.AreEqual(0, a.Read(source)); Assert.AreEqual(0, a.Values.Length);
            source.Play(); source.Pause();
            source.SetParticles(new[] { new ParticleSystem.Particle { startLifetime = 5, remainingLifetime = 5, position = Vector3.one } });
            Assert.AreEqual(1, a.Read(source)); Assert.AreEqual(1, b.Read(source));
            Assert.AreNotSame(a.Values, b.Values);
            a.Values[0].position = Vector3.zero; Assert.AreEqual(Vector3.one, b.Values[0].position);
            var previous = a.Values; a.Read(source); Assert.AreSame(previous, a.Values);
        }
        [Test] public void SubEmitterIndexRebuildDropsOldLinksAndDoesNotDependOnOutputOrder()
        {
            var child = new GameObject("Sub emitter", typeof(ParticleSystem)); child.transform.SetParent(source.transform, false);
            var sub = child.GetComponent<ParticleSystem>(); sub.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var module = source.subEmitters; module.enabled = true;
            module.AddSubEmitter(sub, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
            var graph = new ParticleTopology(); graph.Rebuild(new List<ParticleSystem> { sub, source });
            Assert.AreSame(source, graph.ParentOf(sub)); Assert.IsNull(graph.ParentOf(source));
            module.enabled = false; graph.Rebuild(new List<ParticleSystem> { source, sub }); Assert.IsNull(graph.ParentOf(sub));
        }
        [Test] public void OwnerBindingsUseSubEmitterIndexAcrossSeparateOutputs()
        {
            var child = new GameObject("Sub emitter", typeof(ParticleSystem)); child.transform.SetParent(source.transform, false);
            var sub = child.GetComponent<ParticleSystem>(); sub.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var module = source.subEmitters; module.enabled = true;
            module.AddSubEmitter(sub, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
            effect.RefreshSources();
            Assert.AreSame(source, effect.sourceTopology.ParentOf(sub));
        }
        [Test] public void EffectiveSpaceHandlesMissingCustomTransform()
        {
            var main = source.main; main.simulationSpace = ParticleSystemSimulationSpace.Custom; main.customSimulationSpace = null;
            Assert.AreEqual(ParticleSystemSimulationSpace.Local, ParticleSourceInfo.Space(source));
            main.customSimulationSpace = root.transform;
            Assert.AreEqual(ParticleSystemSimulationSpace.Custom, ParticleSourceInfo.Space(source));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            Assert.AreEqual(ParticleSystemSimulationSpace.World, ParticleSourceInfo.Space(source));
        }
        [Test] public void CapturingWithoutViewDoesNotRewriteAuthoredShape()
        {
            var shape = source.shape; shape.enabled = true; shape.alignToDirection = true;
            var authored = new Vector3(0, -2, 3); shape.scale = authored;
            var mesh = new Mesh();
            try
            {
                ParticleGeometry.Capture(source, source.GetComponent<ParticleSystemRenderer>(), false, null, mesh);
                Assert.AreEqual(authored, shape.scale);
                Assert.Zero(mesh.vertexCount);
            }
            finally { Object.DestroyImmediate(mesh); }
        }
        [Test] public void SpriteSheetTextureUsesSpriteModeAndSkipsEmptySlots()
        {
            var texture = new Texture2D(2, 2); var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero);
            try
            {
                var sheet = source.textureSheetAnimation; sheet.enabled = true; sheet.mode = ParticleSystemAnimationMode.Sprites;
                var removed = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero);
                sheet.AddSprite(removed); sheet.AddSprite(sprite); Object.DestroyImmediate(removed);
                Assert.AreSame(texture, ParticleSourceInfo.SpriteSheetTexture(source));
                sheet.enabled = false; Assert.IsNull(ParticleSourceInfo.SpriteSheetTexture(source));
            }
            finally { Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture); }
        }
    }
    public class PerformanceSettingsTests
    {
        private UIEffectSettings settings, previous;
        private object[] overrides;
        private static readonly string[] Fields = { "_updateRateOverride", "_cullOverride", "_staggerOverride",
            "_mergeOverride", "_staticOverride", "_bindingOverride", "_sharingOverride" };
        private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        private static FieldInfo Loaded => typeof(UIEffectSettings).GetField("loaded", StaticPrivate);
        [SetUp] public void Setup()
        {
            previous = (UIEffectSettings)Loaded.GetValue(null);
            overrides = new object[Fields.Length];
            for (int i = 0; i < Fields.Length; i++) overrides[i] = typeof(UIEffectRenderer).GetField(Fields[i], StaticPrivate).GetValue(null);
            settings = ScriptableObject.CreateInstance<UIEffectSettings>(); Loaded.SetValue(null, settings);
            UIEffectRenderer.ResetPerformanceOverrides();
        }
        [TearDown] public void Cleanup()
        {
            Loaded.SetValue(null, previous);
            for (int i = 0; i < Fields.Length; i++) typeof(UIEffectRenderer).GetField(Fields[i], StaticPrivate).SetValue(null, overrides[i]);
            Object.DestroyImmediate(settings);
        }
        [Test] public void DefaultsEnableConservativeOptimizationsWithoutReducingCadence()
        {
            Assert.AreEqual(100, UIEffectRenderer.updateRatePercent);
            Assert.IsTrue(UIEffectRenderer.staggerBaking); Assert.IsTrue(UIEffectRenderer.mergeRenderers);
            Assert.AreEqual(1, UIEffectRenderer.earlyCull); Assert.IsTrue(UIEffectRenderer.staticMeshCache);
            Assert.IsTrue(UIEffectRenderer.meshSharingEnabled); Assert.IsFalse(UIEffectRenderer.fastBindingMode);
            Assert.IsFalse(UIEffectRenderer.hasPerformanceOverrides);
        }
        [Test] public void SerializedProjectChangesAreReadLiveAndSurviveRoundTrip()
        {
            using (var data = new SerializedObject(settings))
            {
                data.FindProperty("updateRatePercent").intValue = 24;
                data.FindProperty("staggerUpdates").boolValue = false;
                data.FindProperty("mergeParticleOutputs").boolValue = false;
                data.FindProperty("invisibleWorkMode").intValue = 2;
                data.FindProperty("cachePausedParticles").boolValue = false;
                data.FindProperty("skipBindingChecks").boolValue = true;
                data.FindProperty("allowParticleSharing").boolValue = false;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            Assert.AreEqual(24, UIEffectRenderer.updateRatePercent); Assert.AreEqual(2, UIEffectRenderer.earlyCull);
            Assert.IsFalse(UIEffectRenderer.mergeRenderers); Assert.IsFalse(UIEffectRenderer.staggerBaking);
            Assert.IsFalse(UIEffectRenderer.staticMeshCache); Assert.IsTrue(UIEffectRenderer.fastBindingMode);
            Assert.IsFalse(UIEffectRenderer.meshSharingEnabled);
            var copy = ScriptableObject.CreateInstance<UIEffectSettings>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(settings), copy);
                Assert.AreEqual(24, copy.UpdateRatePercent); Assert.AreEqual(2, copy.InvisibleWorkMode);
                Assert.IsFalse(copy.StaggerUpdates); Assert.IsFalse(copy.MergeParticleOutputs);
                Assert.IsFalse(copy.CachePausedParticles); Assert.IsTrue(copy.SkipBindingChecks);
                Assert.IsFalse(copy.AllowParticleSharing);
            }
            finally { Object.DestroyImmediate(copy); }
        }
        [Test] public void CodeOverridesDoNotWriteProjectAssetAndCanBeCleared()
        {
            string before = EditorJsonUtility.ToJson(settings);
            UIEffectRenderer.updateRatePercent = 30; UIEffectRenderer.earlyCull = 2;
            UIEffectRenderer.staggerBaking = false; UIEffectRenderer.mergeRenderers = false;
            UIEffectRenderer.staticMeshCache = false; UIEffectRenderer.fastBindingMode = true;
            UIEffectRenderer.meshSharingEnabled = false;
            Assert.IsTrue(UIEffectRenderer.hasPerformanceOverrides);
            Assert.AreEqual(30, UIEffectRenderer.updateRatePercent); Assert.AreEqual(2, UIEffectRenderer.earlyCull);
            Assert.IsFalse(UIEffectRenderer.staggerBaking); Assert.IsFalse(UIEffectRenderer.mergeRenderers);
            Assert.IsFalse(UIEffectRenderer.staticMeshCache); Assert.IsTrue(UIEffectRenderer.fastBindingMode);
            Assert.IsFalse(UIEffectRenderer.meshSharingEnabled);
            Assert.AreEqual(before, EditorJsonUtility.ToJson(settings));
            UIEffectRenderer.ResetPerformanceOverrides(); DefaultsEnableConservativeOptimizationsWithoutReducingCadence();
        }
        [Test] public void InvalidPerformanceValuesAreClamped()
        {
            UIEffectRenderer.updateRatePercent = -1; UIEffectRenderer.earlyCull = 99;
            Assert.AreEqual(1, UIEffectRenderer.updateRatePercent); Assert.AreEqual(2, UIEffectRenderer.earlyCull);
            UIEffectRenderer.earlyCull = -1; Assert.AreEqual(0, UIEffectRenderer.earlyCull);
            UIEffectRenderer.updateRatePercent = 200; Assert.AreEqual(100, UIEffectRenderer.updateRatePercent);
        }
    }

}
