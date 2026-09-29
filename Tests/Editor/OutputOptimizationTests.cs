using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShanFlyer.UIEffects.Editor.Tests
{
    public class OutputOptimizationTests
    {
        private GameObject root;
        private UIEffectRenderer effect;
        private Material material, replacement;
        private ParticleSystem a, b;
        private bool previousMerge;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [SetUp] public void Setup()
        {
            previousMerge = UIEffectRenderer.mergeRenderers; UIEffectRenderer.mergeRenderers = false;
            root = new GameObject("output tests", typeof(Canvas));
            var go = new GameObject("effect", typeof(RectTransform)); go.transform.SetParent(root.transform, false);
            material = new Material(Shader.Find("UI/Default")); replacement = new Material(material);
            a = Source(go.transform, "a"); b = Source(go.transform, "b");
            effect = go.AddComponent<UIEffectRenderer>(); effect.unitConversion = global::ShanFlyer.UIEffects.UIEffectRenderer.UnitConversion.Manual; effect.uniformScale = 1;
            effect.RefreshSources(); effect.PrepareForUpdate();
        }
        private ParticleSystem Source(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(ParticleSystem)); go.transform.SetParent(parent, false);
            var ps = go.GetComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material; return ps;
        }
        [TearDown] public void Cleanup()
        {
            Object.DestroyImmediate(root); Object.DestroyImmediate(material); Object.DestroyImmediate(replacement);
            UIEffectRenderer.mergeRenderers = previousMerge;
        }
        private static object History(CanvasEffectOutput output) =>
            ((ParticleSourceBinding)typeof(CanvasEffectOutput).GetField("_sourceBinding", Private).GetValue(output)).simulation;
        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator RuntimeRefreshPreservesNativeParticleTimeAndParticles()
        {
            yield return new UnityEngine.TestTools.EnterPlayMode();
            // Scene reload replaces the EditMode fixture objects.
            Setup();
            var main = a.main; main.loop = true; main.prewarm = true; main.startLifetime = 20;
            a.Simulate(.25f, false, true, false); a.Emit(3); a.Pause(false);
            float time = a.time; int count = a.particleCount; Assert.Greater(count, 0);
            effect.RefreshSources();
            Assert.AreEqual(time, a.time, .00001f); Assert.AreEqual(count, a.particleCount);
            Assert.IsFalse(a.GetComponent<ParticleSystemRenderer>().enabled);
            Cleanup();
            yield return new UnityEngine.TestTools.ExitPlayMode();
        }
        [Test] public void UnchangedRefreshPreservesSimulationHistoryAndSuppression()
        {
            var output = effect.GetRendererIfExists(0); var history = History(output);
            effect.RefreshSources(); Assert.AreSame(history, History(output));
            Assert.IsFalse(a.GetComponent<ParticleSystemRenderer>().enabled);
            effect.enabled = false; Assert.IsTrue(a.GetComponent<ParticleSystemRenderer>().enabled);
        }
        [Test] public void MaterialChangeRebindsOnlyAffectedOutput()
        {
            var first = effect.GetRendererIfExists(0); var second = effect.GetRendererIfExists(1);
            var firstHistory = History(first); var secondHistory = History(second);
            b.GetComponent<ParticleSystemRenderer>().sharedMaterial = replacement;
            effect.PrepareForUpdate();
            Assert.AreSame(firstHistory, History(first)); Assert.AreNotSame(secondHistory, History(second));
            Assert.AreSame(replacement, second.material);
        }
        [Test] public void ReorderedSourcesRestoreSuppressionBeforeRebinding()
        {
            effect.enabled = false; a.GetComponent<ParticleSystemRenderer>().enabled = false; effect.enabled = true;
            effect.RefreshSources(new List<ParticleSystem> { b, a });
            Assert.AreSame(b.GetComponent<ParticleSystemRenderer>(), effect.GetRendererIfExists(0).sourceRenderer);
            Assert.IsFalse(a.GetComponent<ParticleSystemRenderer>().enabled); Assert.IsFalse(b.GetComponent<ParticleSystemRenderer>().enabled);
            effect.enabled = false;
            Assert.IsFalse(a.GetComponent<ParticleSystemRenderer>().enabled); Assert.IsTrue(b.GetComponent<ParticleSystemRenderer>().enabled);
        }
        [Test] public void StableMergedRefreshRetainsSourceArrays()
        {
            UIEffectRenderer.mergeRenderers = true; effect.PrepareForUpdate();
            var output = effect.GetRendererIfExists(0);
            var field = typeof(CanvasEffectOutput).GetField("_mergedSystems", Private);
            var sources = field.GetValue(output); Assert.IsNotNull(sources);
            effect.RefreshSources(); Assert.AreSame(sources, field.GetValue(output));
            UIEffectRenderer.mergeRenderers = false; effect.PrepareForUpdate();
            effect.enabled = false;
            Assert.IsTrue(a.GetComponent<ParticleSystemRenderer>().enabled); Assert.IsTrue(b.GetComponent<ParticleSystemRenderer>().enabled);
        }
        [Test] public void AddingAndRemovingTrailsKeepsBodyOwnership()
        {
            var trails = a.trails; trails.enabled = true;
            a.GetComponent<ParticleSystemRenderer>().trailMaterial = material;
            effect.PrepareForUpdate(); Assert.AreEqual(3, effect.activeRendererCount);
            trails.enabled = false; effect.PrepareForUpdate(); Assert.AreEqual(2, effect.activeRendererCount);
            effect.enabled = false; Assert.IsTrue(a.GetComponent<ParticleSystemRenderer>().enabled);
        }
        [Test] public void UnsafeInstanceReplacementDoesNotDeactivateChildren()
        {
            Assert.Throws<ArgumentException>(() => effect.InstallEffect(effect.gameObject));
            Assert.Throws<ArgumentException>(() => effect.InstallEffect(root));
            Assert.IsTrue(a.gameObject.activeSelf); Assert.IsTrue(b.gameObject.activeSelf);
        }
        [Test] public void DisablingSourceSortingRestoresSlotDrawOrder()
        {
            a.GetComponent<ParticleSystemRenderer>().sortingOrder = 20;
            b.GetComponent<ParticleSystemRenderer>().sortingOrder = 0;
            effect.sortBySourceOrder = true; effect.PrepareForUpdate();
            Assert.Greater(effect.GetRendererIfExists(0).transform.GetSiblingIndex(), effect.GetRendererIfExists(1).transform.GetSiblingIndex());
            effect.sortBySourceOrder = false; effect.PrepareForUpdate();
            Assert.Less(effect.GetRendererIfExists(0).transform.GetSiblingIndex(), effect.GetRendererIfExists(1).transform.GetSiblingIndex());
        }
        [Test] public void RetainedBridgeRestoresOriginalSuppressionOnDisable()
        {
            var source = new GameObject("line", typeof(LineRenderer)); source.transform.SetParent(effect.transform, false);
            var line = source.GetComponent<LineRenderer>(); line.sharedMaterial = material; line.forceRenderingOff = false;
            effect.RefreshSources(); var output = effect.GetRendererIfExists(2);
            effect.RefreshSources(); Assert.AreSame(output, effect.GetRendererIfExists(2)); Assert.IsTrue(line.forceRenderingOff);
            effect.enabled = false; Assert.IsFalse(line.forceRenderingOff);
        }
        private Camera BakeCamera() => (Camera)typeof(UIEffectRenderer).GetMethod("GetBakeCamera", Private).Invoke(effect, null);

        [Test] public void BakeCameraIsReusedAndTracksCanvasRectAndScale()
        {
            root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(800, 400);
            rect.localScale = new Vector3(2, 3, 1);
            var camera = BakeCamera();
            Assert.AreEqual(600, camera.orthographicSize, .001f);
            Assert.AreEqual(1600f / 1200, camera.aspect, .0001f);
            rect.sizeDelta = new Vector2(400, 200);
            Assert.AreSame(camera, BakeCamera()); Assert.AreEqual(300, camera.orthographicSize, .001f);
            Object.DestroyImmediate(camera.gameObject);
            var restored = BakeCamera(); Assert.IsTrue(restored); Assert.AreNotSame(camera, restored);
            Assert.IsFalse(restored.gameObject.activeSelf);
            effect.enabled = false; Assert.IsFalse(restored);
        }

        [Test] public void OverlayBakeViewMatchesCanvasAfterCanvasScaler()
        {
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
            foreach (float scale in new[] { 1f, 2f })
            {
                scaler.scaleFactor = scale;
                typeof(UnityEngine.UI.CanvasScaler).GetMethod("Handle", Private).Invoke(scaler, null);
                Canvas.ForceUpdateCanvases();
                var camera = BakeCamera(); var rect = (RectTransform)canvas.transform;
                Assert.AreEqual(rect.rect.height * rect.TransformVector(Vector3.up).magnitude * .5f,
                    camera.orthographicSize, .001f);
                Assert.AreEqual(canvas.pixelRect.height * .5f, camera.orthographicSize, .01f);
            }
        }

        [TestCase(RenderMode.ScreenSpaceCamera)] [TestCase(RenderMode.WorldSpace)]
        public void CanvasCameraIsBorrowedWithoutModificationAndReplacementInvalidates(RenderMode mode)
        {
            var cameraNode = new GameObject("Canvas camera", typeof(Camera));
            var replacementNode = new GameObject("Replacement camera", typeof(Camera));
            try
            {
                var supplied = cameraNode.GetComponent<Camera>(); supplied.enabled = false;
                supplied.fieldOfView = 42; supplied.transform.position = new Vector3(4, 5, -20);
                var originalProjection = supplied.projectionMatrix;
                var originalView = supplied.worldToCameraMatrix;
                var canvas = root.GetComponent<Canvas>(); canvas.renderMode = mode; canvas.worldCamera = supplied;
                Assert.AreSame(supplied, BakeCamera());
                effect.PrepareForUpdate(); uint revision = effect.bakeViewRevision;
                supplied.fieldOfView = 58; effect.PrepareForUpdate();
                Assert.AreNotEqual(revision, effect.bakeViewRevision);
                canvas.worldCamera = replacementNode.GetComponent<Camera>();
                Assert.AreSame(canvas.worldCamera, BakeCamera());
                supplied.fieldOfView = 42;
                effect.enabled = false;
                Assert.IsTrue(supplied); Assert.IsTrue(canvas.worldCamera);
                Assert.AreEqual(originalProjection, supplied.projectionMatrix);
                Assert.AreEqual(originalView, supplied.worldToCameraMatrix);
            }
            finally { Object.DestroyImmediate(cameraNode); Object.DestroyImmediate(replacementNode); }
        }

        [Test] public void AutomaticViewChangesInvalidatePausedOutputButStableViewDoesNot()
        {
            root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)root.transform; rect.sizeDelta = new Vector2(800, 400);
            effect.PrepareForUpdate(); effect.Pause();
            var output = effect.GetRendererIfExists(0);
            var force = typeof(CanvasEffectOutput).GetField("_forceBake", Private);
            force.SetValue(output, false);
            uint revision = effect.bakeViewRevision;
            effect.PrepareForUpdate();
            Assert.AreEqual(revision, effect.bakeViewRevision); Assert.IsFalse((bool)force.GetValue(output));
            rect.sizeDelta *= 2; effect.PrepareForUpdate();
            Assert.AreNotEqual(revision, effect.bakeViewRevision); Assert.IsTrue((bool)force.GetValue(output));
        }

        [Test] public void EmptyCanvasViewRecoversWithoutManualConfiguration()
        {
            root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = Vector2.zero; Assert.IsNull(BakeCamera());
            rect.sizeDelta = new Vector2(640, 480);
            Assert.AreEqual(240, BakeCamera().orthographicSize, .001f);
            root.transform.rotation = Quaternion.Euler(20, 30, 40);
            Assert.Less(Quaternion.Angle(root.transform.rotation, BakeCamera().transform.rotation), .001f);
        }

        [Test] public void NativeParticleBakeTracksAutomaticViewSizeLimits()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Run this native-geometry check with --graphics.");
            root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)root.transform; rect.sizeDelta = new Vector2(200, 100);
            a.Play(false); a.Pause(false);
            a.SetParticles(new[] { new ParticleSystem.Particle { position = Vector3.zero,
                startSize = 1000, startColor = Color.white, startLifetime = 100, remainingLifetime = 100 } }, 1);
            Assert.AreEqual(1, a.particleCount);
            var renderer = a.GetComponent<ParticleSystemRenderer>(); renderer.maxParticleSize = .1f;
            var mesh = new Mesh();
            try
            {
                ParticleGeometry.Capture(a, renderer, false, BakeCamera(), mesh);
                Assert.Greater(mesh.vertexCount, 0, "Native bake must contain vertices");
                mesh.RecalculateBounds(); float height = mesh.bounds.size.y;
                Assert.Greater(height, 0); Assert.Less(height, 1000);
                rect.sizeDelta *= 2;
                ParticleGeometry.Capture(a, renderer, false, BakeCamera(), mesh);
                mesh.RecalculateBounds();
                Assert.AreEqual(height * 2, mesh.bounds.size.y, .01f);
            }
            finally { Object.DestroyImmediate(mesh); }
        }


        [Test] public void AffineRectMatchesFourCornerReferenceForRotationsMirrorsAndShear()
        {
            var random = new System.Random(9876);
            for (int i = 0; i < 100; ++i)
            {
                var matrix = Matrix4x4.TRS(new Vector3(i, -i, 3), Quaternion.Euler(i * 3, i, i * 7), new Vector3(-2, .5f, 3))
                    * Matrix4x4.TRS(Vector3.one, Quaternion.Euler(13, 27, 39), new Vector3(1, -3, 2));
                var bounds = new Bounds(new Vector3((float)random.NextDouble(), 2, 9), new Vector3(i % 7, 3, 8));
                var actual = EffectScale.TransformRect(bounds, matrix);
                Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
                for (int c = 0; c < 4; ++c)
                {
                    Vector2 point = matrix.MultiplyPoint(new Vector3((c & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (c & 2) == 0 ? bounds.min.y : bounds.max.y, 0));
                    min = Vector2.Min(min, point); max = Vector2.Max(max, point);
                }
                Assert.That(Vector2.Distance(min, actual.min), Is.LessThan(.0001f));
                Assert.That(Vector2.Distance(max, actual.max), Is.LessThan(.0001f));
            }
        }
        [TestCase(ParticleSystemSimulationSpace.Local)]
        [TestCase(ParticleSystemSimulationSpace.World)]
        [TestCase(ParticleSystemSimulationSpace.Custom)]
        public void DirectSpaceMatrixMatchesComposedReference(ParticleSystemSimulationSpace space)
        {
            var main = a.main; main.customSimulationSpace = root.transform; main.simulationSpace = space;
            root.transform.position = new Vector3(3, -4, 5);
            var position = new Vector3(7, 8, 9); var scale = new Vector3(-2, 0, 3);
            a.transform.position = position;
            var actual = ParticleCoordinates.BakeToWorld(a, false, scale, effect.transform.position, UIEffectRenderer.OriginMode.Emitter);
            var expected = space == ParticleSystemSimulationSpace.Local ? Matrix4x4.Translate(position) * Matrix4x4.Scale(scale)
                : space == ParticleSystemSimulationSpace.World ? Matrix4x4.Translate(position) * Matrix4x4.Scale(scale) * Matrix4x4.Translate(-position)
                : Matrix4x4.Translate(position) * Matrix4x4.Scale(scale)
                    * Matrix4x4.Translate(root.transform.position - position);
            for (int i = 0; i < 16; ++i) Assert.AreEqual(expected[i], actual[i], .00001f);
        }
    }
}
