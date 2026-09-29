using System.Reflection;
using ShanFlyer.UIEffects;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ShanFlyer.UIEffects.Editor.Tests
{
    public class HPSharingTests
    {
        private GameObject _canvas;
        private Material _material;
        [SetUp] public void Setup()
        {
            _canvas = new GameObject("sharing tests", typeof(Canvas));
            _material = new Material(Shader.Find("UI/Default"));
            global::ShanFlyer.UIEffects.UIEffectRenderer.mergeRenderers = false;
        }
        [TearDown] public void Cleanup()
        {
            Object.DestroyImmediate(_canvas); Object.DestroyImmediate(_material);
            global::ShanFlyer.UIEffects.UIEffectRenderer.mergeRenderers = false;
        }
        private global::ShanFlyer.UIEffects.UIEffectRenderer Create()
        {
            var root = new GameObject("effect", typeof(RectTransform));
            root.transform.SetParent(_canvas.transform, false);
            for (var i = 0; i < 2; i++)
            {
                var child = new GameObject("particle " + i, typeof(ParticleSystem));
                child.transform.SetParent(root.transform, false);
                child.GetComponent<ParticleSystemRenderer>().sharedMaterial = _material;
            }
            var effect = root.AddComponent<global::ShanFlyer.UIEffects.UIEffectRenderer>(); effect.unitConversion = global::ShanFlyer.UIEffects.UIEffectRenderer.UnitConversion.Manual;
            effect.simulationRole = global::ShanFlyer.UIEffects.UIEffectRenderer.SimulationRole.Automatic;
            effect.RefreshSources();
            return effect;
        }
        private global::ShanFlyer.UIEffects.UIEffectRenderer Clone(global::ShanFlyer.UIEffects.UIEffectRenderer effect)
        {
            var clone = Object.Instantiate(effect, _canvas.transform);
            clone.RefreshSources(); return clone;
        }
        private static string GroupDetails(global::ShanFlyer.UIEffects.UIEffectRenderer p)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            return typeof(global::ShanFlyer.UIEffects.UIEffectRenderer).GetField("m_AutomaticSharingKey", flags).GetValue(p) + " | "
                + typeof(global::ShanFlyer.UIEffects.UIEffectRenderer).GetField("_sharingLayout", flags).GetValue(p);
        }
        private static void Refresh()
        {
            typeof(UIEffectScheduler).GetField("s_FrameCount", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, -1);
            typeof(UIEffectScheduler).GetMethod("Refresh", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        }

        [Test] public void DifferentAutomaticViewsCannotShareAndMatchingViewsRejoin()
        {
            _canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)_canvas.transform).sizeDelta = new Vector2(800, 400);
            var other = new GameObject("other canvas", typeof(Canvas));
            try
            {
                other.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                var rect = (RectTransform)other.transform; rect.sizeDelta = new Vector2(800, 400);
                var a = Create();
                var b = Object.Instantiate(a, other.transform); b.RefreshSources();
                a.PrepareForUpdate(); b.PrepareForUpdate();
                Assert.AreEqual(a.sharingGroup, b.sharingGroup);
                rect.sizeDelta *= 2; b.PrepareForUpdate();
                Assert.AreNotEqual(a.sharingGroup, b.sharingGroup);
                rect.sizeDelta *= .5f; b.PrepareForUpdate();
                Assert.AreEqual(a.sharingGroup, b.sharingGroup);
            }
            finally { Object.DestroyImmediate(other); }
        }

        [Test] public void AutomaticUnitConversionSeparatesDifferentScalesAndRejoinsMatchingScales()
        {
            _canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var a = Create(); a.unitConversion = UIEffectRenderer.UnitConversion.Automatic;
            var b = Clone(a);
            var view = new GameObject("unit reference", typeof(Camera));
            var texture = new RenderTexture(1000, 1000, 16);
            try
            {
                var camera = view.GetComponent<Camera>(); camera.targetTexture = texture;
                camera.orthographic = true; camera.orthographicSize = 10;
                Canvas.ForceUpdateCanvases();
                a.PrepareForUpdate(); b.PrepareForUpdate();
                Assert.AreEqual(a.sharingGroup, b.sharingGroup);
                b.referenceCamera = camera; b.PrepareForUpdate();
                Assert.AreNotEqual(a.sharingGroup, b.sharingGroup);
                b.referenceCamera = null; b.PrepareForUpdate();
                Assert.AreEqual(a.sharingGroup, b.sharingGroup);
            }
            finally { Object.DestroyImmediate(view); Object.DestroyImmediate(texture); }
        }

        [Test] public void ProjectSharingSwitchMakesConsumersIndependentAndRejoinsOnEnable()
        {
            bool previous = UIEffectRenderer.meshSharingEnabled;
            try
            {
                UIEffectRenderer.meshSharingEnabled = true;
                var producer = Create(); var consumer = Clone(producer);
                consumer.simulationRole = UIEffectRenderer.SimulationRole.Consumer;
                Refresh(); Assert.IsTrue(consumer.useMeshSharing); Assert.IsFalse(consumer.canSimulate);
                UIEffectRenderer.meshSharingEnabled = false; Refresh();
                Assert.IsFalse(producer.useMeshSharing); Assert.IsFalse(consumer.useMeshSharing);
                Assert.IsTrue(consumer.canSimulate); Assert.AreEqual(0, consumer.sharingGroup);
                UIEffectRenderer.meshSharingEnabled = true; Refresh();
                Assert.AreEqual(producer.sharingGroup, consumer.sharingGroup);
                Assert.IsTrue(consumer.useMeshSharing); Assert.IsFalse(consumer.canSimulate);
            }
            finally { UIEffectRenderer.meshSharingEnabled = previous; }
        }
        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator AutomaticSharingWorksInPlayMode()
        {
            yield return new UnityEngine.TestTools.EnterPlayMode();
            // EditMode fixture objects are not preserved across the mode transition.
            _canvas = new GameObject("runtime sharing tests", typeof(Canvas));
            _material = new Material(Shader.Find("UI/Default"));
            var a = Create(); var b = Clone(a); var independent = Create();
            yield return null;
            Assert.AreEqual(a.sharingGroup, b.sharingGroup);
            Assert.AreNotEqual(a.sharingGroup, independent.sharingGroup);
            Object.DestroyImmediate(_canvas); Object.DestroyImmediate(_material);
            yield return new UnityEngine.TestTools.ExitPlayMode();
        }

        private static System.Collections.IList CachedMembers(int id)
        {
            var groups = (System.Collections.IDictionary)typeof(UIEffectScheduler)
                .GetField("groups", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            if (!groups.Contains(id)) return null;
            return (System.Collections.IList)groups[id].GetType()
                .GetField("members", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(groups[id]);
        }
        [Test] public void StableGroupsReusePlanAcrossFrames()
        {
            var a = Create(); var b = Clone(a); Refresh();
            var members = CachedMembers(a.sharingGroup);
            Refresh(); Refresh();
            Assert.AreEqual(0, UIEffectProfiler.completed.groupPlanRebuilds);
            Assert.AreSame(members, CachedMembers(a.sharingGroup)); Assert.AreEqual(2, members.Count);
        }
        [Test] public void CachedMembershipTracksDisableReenableAndDestroy()
        {
            var a = Create(); var b = Clone(a); Refresh();
            b.enabled = false; Refresh(); Assert.AreEqual(1, CachedMembers(a.sharingGroup).Count);
            b.enabled = true; Refresh(); Assert.AreEqual(2, CachedMembers(a.sharingGroup).Count);
            Object.DestroyImmediate(b.gameObject); Refresh(); Assert.AreEqual(1, CachedMembers(a.sharingGroup).Count);
            int id = a.sharingGroup; Object.DestroyImmediate(a.gameObject); Refresh(); Assert.IsNull(CachedMembers(id));
        }
        [Test] public void CachedMembershipTracksAutomaticSplitAndRejoin()
        {
            var a = Create(); var b = Clone(a); Refresh();
            var source = b.particles[0].transform; var position = source.localPosition;
            source.localPosition += Vector3.right; Refresh();
            Assert.AreNotEqual(a.sharingGroup, b.sharingGroup);
            Assert.AreEqual(1, CachedMembers(a.sharingGroup).Count); Assert.AreEqual(1, CachedMembers(b.sharingGroup).Count);
            source.localPosition = position; Refresh();
            Assert.AreEqual(a.sharingGroup, b.sharingGroup); Assert.AreEqual(2, CachedMembers(a.sharingGroup).Count);
        }
        [Test] public void OwnerPriorityChangesWithoutStaleOwnership()
        {
            var a = Create(); var b = Clone(a); Refresh();
            b.simulationRole = UIEffectRenderer.SimulationRole.Producer; Refresh();
            Assert.AreSame(b, UIEffectScheduler.GetPrimary(b.sharingGroup));
            b.simulationRole = UIEffectRenderer.SimulationRole.Consumer; Refresh();
            Assert.AreSame(a, UIEffectScheduler.GetPrimary(a.sharingGroup));
            a.simulationRole = UIEffectRenderer.SimulationRole.Consumer; Refresh();
            Assert.IsNull(UIEffectScheduler.GetPrimary(a.sharingGroup));
        }
        [Test] public void CachedSlotsRecoverFromRendererReplacement()
        {
            var a = Create(); var b = Clone(a); Refresh();
            var old = b.GetRendererIfExists(0); Object.DestroyImmediate(old.gameObject);
            Refresh(); Assert.IsNotNull(b.GetRendererIfExists(0));
            Assert.AreNotSame(old, b.GetRendererIfExists(0));
            Assert.Greater(UIEffectProfiler.completed.groupPlanRebuilds, 0);
            Refresh(); Assert.AreEqual(0, UIEffectProfiler.completed.groupPlanRebuilds);
        }
        [Test] public void MergeToggleRebuildsSlotsThenSettles()
        {
            var a = Create(); var b = Clone(a); Refresh();
            UIEffectRenderer.mergeRenderers = true; Refresh();
            Assert.AreEqual(a.activeRendererCount, b.activeRendererCount);
            Refresh(); Assert.AreEqual(0, UIEffectProfiler.completed.groupPlanRebuilds);
            UIEffectRenderer.mergeRenderers = false; Refresh();
            Assert.AreEqual(2, a.activeRendererCount); Assert.AreEqual(2, b.activeRendererCount);
            Refresh(); Assert.AreEqual(0, UIEffectProfiler.completed.groupPlanRebuilds);
        }

        [Test] public void FailedPreparationIsExcludedAndCanRecover()
        {
            var a = Create(); var b = Clone(a); Refresh();
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception,
                new System.Text.RegularExpressions.Regex("InvalidOperationException: injected group failure"));
            System.Action<UIEffectRenderer> fail = _ => throw new System.InvalidOperationException("injected group failure");
            typeof(UIEffectScheduler).GetMethod("Guard", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { a, fail });
            Refresh(); Assert.AreEqual(1, CachedMembers(b.sharingGroup).Count);
            Refresh(); Assert.AreEqual(2, CachedMembers(b.sharingGroup).Count);
            Refresh(); Assert.AreEqual(0, UIEffectProfiler.completed.groupPlanRebuilds);
        }
        [Test] public void AlphaVisibilityUpdatesWithoutStructuralRebuild()
        {
            int previous = UIEffectRenderer.earlyCull;
            try
            {
                UIEffectRenderer.earlyCull = 1;
                var a = Create(); var b = Clone(a); Refresh();
                var alpha = _canvas.AddComponent<CanvasGroup>(); alpha.alpha = 0; Canvas.ForceUpdateCanvases(); Refresh();
                Assert.IsTrue(a.groupAllAlphaHidden); Assert.IsTrue(b.groupAllAlphaHidden);
                Assert.AreEqual(0, UIEffectProfiler.completed.groupPlanRebuilds);
                alpha.alpha = 1; Canvas.ForceUpdateCanvases(); Refresh();
                Assert.IsFalse(a.groupAllAlphaHidden); Assert.IsFalse(b.groupAllAlphaHidden);
                Assert.AreEqual(0, UIEffectProfiler.completed.groupPlanRebuilds);
            }
            finally { UIEffectRenderer.earlyCull = previous; }
        }

        [Test] public void SharedPhaseSurvivesOwnerChangeWhileBridgesKeepInstancePhases()
        {
            var a = Create(); var b = Clone(a); Refresh();
            Assert.AreEqual(a.particleBakePhase, b.particleBakePhase);
            Assert.AreNotEqual(a.independentBakePhase, b.independentBakePhase);
            double phase = b.particleBakePhase;
            a.enabled = false; Refresh();
            Assert.AreEqual(phase, b.particleBakePhase);
            Assert.AreSame(b, UIEffectScheduler.GetPrimary(b.sharingGroup));
        }
        [Test] public void IndependentEffectsDoNotShareDefaultZero()
        {
            var a = Create(); var b = Create();
            Assert.AreNotEqual(a.sharingGroup, b.sharingGroup);
        }
        [Test] public void ClonesShareWithoutNumericIds()
        {
            var a = Create(); var b = Clone(a);
            Refresh();
            Assert.AreEqual(a.sharingGroup, b.sharingGroup, GroupDetails(a) + "\n" + GroupDetails(b));
            var targets = new System.Collections.Generic.List<CanvasEffectOutput>();
            UIEffectScheduler.GetGroupedRenderers(a.sharingGroup, 0, targets);
            Assert.AreEqual(2, targets.Count);
        }
        [Test] public void ChangedParticleSubsetSplitsAutomaticGroup()
        {
            var a = Create(); var b = Clone(a);
            b.RefreshSources(new System.Collections.Generic.List<ParticleSystem> { b.particles[1] });
            Assert.AreNotEqual(a.sharingGroup, b.sharingGroup);
        }
        [TestCase(false)] [TestCase(true)] public void PropertyBlocksPreventMerging(bool indexed)
        {
            var a = Create(); Assert.IsTrue(CanvasEffectOutput.CanMerge(a.particles), GroupDetails(a));
            var block = new MaterialPropertyBlock(); block.SetColor("_Color", Color.red);
            if (indexed) a.particles[1].GetComponent<ParticleSystemRenderer>().SetPropertyBlock(block, 0);
            else a.particles[1].GetComponent<ParticleSystemRenderer>().SetPropertyBlock(block);
            Assert.IsFalse(CanvasEffectOutput.CanMerge(a.particles));
        }
        [Test] public void VertexStreamDifferencePreventsMerging()
        {
            var a = Create();
            a.particles[1].GetComponent<ParticleSystemRenderer>().SetActiveVertexStreams(
                new System.Collections.Generic.List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color });
            Assert.IsFalse(CanvasEffectOutput.CanMerge(a.particles));
        }
        [Test]
        public void AnimatedReplicaForcesUniformUnmergedLayout()
        {
            global::ShanFlyer.UIEffects.UIEffectRenderer.mergeRenderers = true;
            var a = Create(); var b = Clone(a);
            b.propertyBindings = new[] { new MaterialPropertyBinding() };
            b.MarkBindingDirty();
            Refresh();
            Assert.AreEqual(a.sharingGroup, b.sharingGroup, GroupDetails(a) + "\n" + GroupDetails(b));
            Assert.AreEqual(0, a.mergedRendererCount);
            Assert.AreEqual(0, b.mergedRendererCount);
            Assert.AreEqual(a.activeRendererCount, b.activeRendererCount);
        }
        [Test] public void PrefabInstancesShareButDuplicatedPrefabAssetsDoNot()
        {
            const string path = "Assets/HPSharingTemporary.prefab";
            const string copyPath = "Assets/HPSharingTemporaryCopy.prefab";
            try
            {
                var template = Create();
                var asset = UnityEditor.PrefabUtility.SaveAsPrefabAsset(template.gameObject, path);
                Assert.IsNotNull(asset);
                var a = ((GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(asset, _canvas.transform)).GetComponent<global::ShanFlyer.UIEffects.UIEffectRenderer>();
                var b = ((GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(asset, _canvas.transform)).GetComponent<global::ShanFlyer.UIEffects.UIEffectRenderer>();
                a.RefreshSources(); b.RefreshSources();
                Assert.AreEqual(a.sharingGroup, b.sharingGroup);
                var main = b.particles[0].main; main.startLifetime = 7;
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(b.particles[0]);
                b.RefreshSources(); Assert.AreNotEqual(a.sharingGroup, b.sharingGroup, "prefab simulation override must be isolated");
                Assert.IsTrue(UnityEditor.AssetDatabase.CopyAsset(path, copyPath));
                var copy = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(copyPath);
                var c = ((GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(copy, _canvas.transform)).GetComponent<global::ShanFlyer.UIEffects.UIEffectRenderer>();
                c.RefreshSources(); Assert.AreNotEqual(a.sharingGroup, c.sharingGroup);
            }
            finally { UnityEditor.AssetDatabase.DeleteAsset(copyPath); UnityEditor.AssetDatabase.DeleteAsset(path); }
        }
        [Test] public void ChangedChildTransformSplitsAutomaticGroup()
        {
            var a = Create(); var b = Clone(a);
            b.particles[0].transform.localPosition = Vector3.right;
            b.PrepareForUpdate();
            Assert.AreNotEqual(a.sharingGroup, b.sharingGroup);
        }

        [Test] public void PooledCloneRejoinsOriginalGroup()
        {
            var a = Create(); var b = Clone(a);
            b.gameObject.SetActive(false); b.gameObject.SetActive(true); b.PrepareForUpdate();
            Assert.AreEqual(a.sharingGroup, b.sharingGroup, GroupDetails(a) + "\n" + GroupDetails(b));
        }
    }
}
