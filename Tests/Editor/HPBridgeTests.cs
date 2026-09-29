using System.Collections.Generic;
using System.Reflection;
using ShanFlyer.UIEffects;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ShanFlyer.UIEffects.Editor.Tests
{
    public class HPBridgeRuntimeTests
    {
        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator RuntimeCadenceAndAlphaRecoveryPreserveNativeSource()
        {
            yield return new UnityEngine.TestTools.EnterPlayMode();
            var root = new GameObject("runtime cache test", typeof(Canvas));
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            var material = new Material(Shader.Find("UI/Default"));
            var oldFps = global::ShanFlyer.UIEffects.UIEffectRenderer.updateRatePercent;
            var oldCull = global::ShanFlyer.UIEffects.UIEffectRenderer.earlyCull;
            try
            {
                var go = new GameObject("effect", typeof(RectTransform)); go.transform.SetParent(root.transform, false);
                var effect = go.AddComponent<global::ShanFlyer.UIEffects.UIEffectRenderer>(); effect.unitConversion = global::ShanFlyer.UIEffects.UIEffectRenderer.UnitConversion.Manual;
                effect.uniformScale = 1; effect.scaleMode = global::ShanFlyer.UIEffects.UIEffectRenderer.ScaleMode.Hierarchy;
                var source = new GameObject("source", typeof(MeshFilter), typeof(MeshRenderer));
                source.transform.SetParent(go.transform, false);
                source.GetComponent<MeshFilter>().sharedMesh = mesh;
                var native = source.GetComponent<MeshRenderer>(); native.sharedMaterial = material;
                effect.RefreshSources(); effect.PrepareForUpdate();
                var output = effect.GetRendererIfExists(0);
                var stamp = typeof(CanvasEffectOutput).GetField("_lastBakeFrame", BindingFlags.Instance | BindingFlags.NonPublic);
                global::ShanFlyer.UIEffects.UIEffectRenderer.updateRatePercent = 1; global::ShanFlyer.UIEffects.UIEffectRenderer.earlyCull = 1;
                output.UpdateMesh(null);
                var submissions = UIEffectProfiler.current.setMeshOps;
                source.transform.localPosition = Vector3.right * 4;
                stamp.SetValue(output, -1); output.UpdateMesh(null);
                Assert.AreEqual(submissions, UIEffectProfiler.current.setMeshOps, "same tick must not submit");
                var group = go.AddComponent<CanvasGroup>(); group.alpha = 0;
                Canvas.ForceUpdateCanvases();
                Assert.That(output.canvasRenderer.GetInheritedAlpha(), Is.LessThanOrEqualTo(.001f));
                stamp.SetValue(output, -1); output.UpdateMesh(null);
                Assert.AreEqual(submissions, UIEffectProfiler.current.setMeshOps, "hidden output must not submit");
                Assert.IsTrue(native.enabled, "native sampling stays enabled");
                group.alpha = 1;
                Canvas.ForceUpdateCanvases();
                stamp.SetValue(output, -1); output.UpdateMesh(null);
                Assert.AreEqual(submissions + 1, UIEffectProfiler.current.setMeshOps, "recovery refreshes within the same tick");
                var outputMesh = (Mesh)typeof(CanvasEffectOutput).GetField("_bridgeOutput", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(output);
                Assert.AreEqual(Vector3.right * 4, outputMesh.vertices[0]);
            }
            finally
            {
                global::ShanFlyer.UIEffects.UIEffectRenderer.updateRatePercent = oldFps; global::ShanFlyer.UIEffects.UIEffectRenderer.earlyCull = oldCull;
                Object.DestroyImmediate(root); Object.DestroyImmediate(mesh); Object.DestroyImmediate(material);
            }
            yield return new UnityEngine.TestTools.ExitPlayMode();
        }
    }

    public class HPBridgeTests
    {
        private GameObject _root;
        private global::ShanFlyer.UIEffects.UIEffectRenderer _effect;
        private Material _material;
        private Mesh _mesh;
        private Camera _camera;
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp]
        public void Setup()
        {
            global::ShanFlyer.UIEffects.UIEffectRenderer.mergeRenderers = false;
            global::ShanFlyer.UIEffects.UIEffectRenderer.fastBindingMode = false;
            global::ShanFlyer.UIEffects.UIEffectRenderer.staticMeshCache = false;
            global::ShanFlyer.UIEffects.UIEffectRenderer.earlyCull = 0;
            _root = new GameObject("HP test Canvas", typeof(Canvas));
            var go = new GameObject("effect", typeof(RectTransform));
            go.transform.SetParent(_root.transform, false);
            _effect = go.AddComponent<global::ShanFlyer.UIEffects.UIEffectRenderer>(); _effect.unitConversion = global::ShanFlyer.UIEffects.UIEffectRenderer.UnitConversion.Manual;
            _effect.scaleMode = global::ShanFlyer.UIEffects.UIEffectRenderer.ScaleMode.Hierarchy;
            _effect.uniformScale = 1;
            _material = new Material(Shader.Find("UI/Default"));
            _mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            var camera = new GameObject("camera", typeof(Camera));
            camera.transform.SetParent(_root.transform, false);
            camera.transform.position = new Vector3(0, 0, -10);
            _camera = camera.GetComponent<Camera>();
            _camera.orthographic = true;
        }

        [TearDown]
        public void Cleanup()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_mesh);
            Object.DestroyImmediate(_material);
            global::ShanFlyer.UIEffects.UIEffectRenderer.mergeRenderers = false;
            global::ShanFlyer.UIEffects.UIEffectRenderer.fastBindingMode = false;
        }

        private MeshRenderer AddMesh(Transform parent = null)
        {
            var go = new GameObject("mesh", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent ? parent : _effect.transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            return renderer;
        }

        private CanvasEffectOutput Bridge()
        {
            _effect.RefreshSources(_effect.particles);
            _effect.PrepareForUpdate();
            for (var i = 0; i < _effect.activeRendererCount; i++)
                if (_effect.GetRendererIfExists(i).isBridge) return _effect.GetRendererIfExists(i);
            Assert.Fail("No bridge found"); return null;
        }

        private Mesh Output(CanvasEffectOutput renderer)
        {
            return (Mesh)typeof(CanvasEffectOutput).GetField("_bridgeOutput", Private).GetValue(renderer);
        }

        private void NextBridgeUpdate(CanvasEffectOutput renderer)
        {
            typeof(CanvasEffectOutput).GetField("_lastBakeFrame", Private).SetValue(renderer, -1);
            renderer.UpdateMesh(_camera);
        }

        [Test]
        public void ClockCadenceSwitchDrainsPendingTimeAndDoesNotReuseOldTick()
        {
            var clock = new EffectUpdateClock();
            clock.AdvanceAtTick(.01f, .02f, 10, false, out _, out _);
            Assert.IsFalse(clock.AdvanceAtTick(.03f, .04f, 10, false, out _, out _));
            clock.AdvanceEveryFrame(.05f, .06f, out var scaled, out var unscaled);
            Assert.AreEqual(.08f, scaled, .00001f); Assert.AreEqual(.1f, unscaled, .00001f);
            Assert.IsTrue(clock.AdvanceAtTick(.01f, .02f, 10, false, out scaled, out unscaled));
            Assert.AreEqual(.01f, scaled, .00001f); Assert.AreEqual(.02f, unscaled, .00001f);
        }

        [Test]
        public void UnchangedMeshSkipsSubmissionButInPlaceEditsAndTransformAreDetected()
        {
            var source = AddMesh(); var renderer = Bridge();
            UIEffectProfiler.current = default;
            renderer.UpdateMesh(_camera);
            var submissions = UIEffectProfiler.current.setMeshOps;
            NextBridgeUpdate(renderer);
            Assert.AreEqual(submissions, UIEffectProfiler.current.setMeshOps);
            Assert.AreEqual(1, UIEffectProfiler.current.bridgeCacheHits);
            _mesh.vertices = new[] { Vector3.zero, Vector3.right * 2, Vector3.up };
            NextBridgeUpdate(renderer);
            Assert.AreEqual(++submissions, UIEffectProfiler.current.setMeshOps);
            Assert.AreEqual(Vector3.right * 2, Output(renderer).vertices[1]);
            _mesh.uv = new[] { Vector2.one, Vector2.right, Vector2.up };
            NextBridgeUpdate(renderer);
            Assert.AreEqual(++submissions, UIEffectProfiler.current.setMeshOps);
            Assert.AreEqual(Vector2.one, Output(renderer).uv[0]);
            source.transform.localPosition = Vector3.right * 3;
            NextBridgeUpdate(renderer);
            Assert.AreEqual(++submissions, UIEffectProfiler.current.setMeshOps);
            Assert.AreEqual(Vector3.right * 3, Output(renderer).vertices[0]);
        }

        [Test]
        public void DisabledCanvasSkipsBridgeAndResumesWithCurrentSource()
        {
            var source = AddMesh(); var renderer = Bridge(); renderer.UpdateMesh(_camera);
            var submissions = UIEffectProfiler.current.setMeshOps;
            _root.GetComponent<Canvas>().enabled = false;
            source.transform.localPosition = Vector3.up * 4;
            NextBridgeUpdate(renderer);
            Assert.AreEqual(submissions, UIEffectProfiler.current.setMeshOps);
            _root.GetComponent<Canvas>().enabled = true;
            NextBridgeUpdate(renderer);
            Assert.AreEqual(Vector3.up * 4, Output(renderer).vertices[0]);
        }

        [Test]
        public void DisabledCanvasIsHiddenForSharingVisibility()
        {
            AddMesh(); Bridge();
            _root.GetComponent<Canvas>().enabled = false;
            _effect.GetOutputVisibility(out var output, out var hidden, out var clipped);
            Assert.IsTrue(output); Assert.IsTrue(hidden); Assert.IsTrue(clipped);
            _root.GetComponent<Canvas>().enabled = true;
            _effect.GetOutputVisibility(out output, out hidden, out clipped);
            Assert.IsTrue(output); Assert.IsFalse(hidden);
        }

        [Test]
        public void ClearedBridgeRepopulatesEvenWhenGeometryIsUnchanged()
        {
            var source = AddMesh(); var renderer = Bridge(); renderer.UpdateMesh(_camera);
            source.enabled = false; NextBridgeUpdate(renderer);
            source.enabled = true;
            var submissions = UIEffectProfiler.current.setMeshOps;
            NextBridgeUpdate(renderer);
            Assert.AreEqual(submissions + 1, UIEffectProfiler.current.setMeshOps);
            Assert.AreEqual(1, renderer.canvasRenderer.materialCount);
        }

        [Test]
        public void MeshSnapshotDetectsColorsIndicesAndHighUvChannels()
        {
            var snapshot = new BridgeGeometrySnapshot();
            Assert.IsTrue(snapshot.Capture(_mesh)); Assert.IsFalse(snapshot.Capture(_mesh));
            _mesh.colors = new[] { Color.red, Color.green, Color.blue };
            Assert.IsTrue(snapshot.Capture(_mesh)); Assert.IsFalse(snapshot.Capture(_mesh));
            _mesh.SetUVs(7, new List<Vector4> { Vector4.one, Vector4.zero, Vector4.one });
            Assert.IsTrue(snapshot.Capture(_mesh)); Assert.IsFalse(snapshot.Capture(_mesh));
            _mesh.triangles = new[] { 2, 1, 0 };
            Assert.IsTrue(snapshot.Capture(_mesh));
        }

        [Test]
        public void SynchronizedClockPreservesElapsedTimeAndForceDoesNotDoubleSimulate()
        {
            var clock = new EffectUpdateClock();
            Assert.IsTrue(clock.AdvanceAtTick(.01f, .02f, 10, false, out var scaled, out _));
            Assert.That(scaled, Is.EqualTo(.01f).Within(.00001f));
            Assert.IsFalse(clock.AdvanceAtTick(.01f, .02f, 10, false, out _, out _));
            Assert.IsTrue(clock.AdvanceAtTick(.01f, .02f, 11, false, out scaled, out var unscaled));
            Assert.That(scaled, Is.EqualTo(.02f).Within(.00001f));
            Assert.That(unscaled, Is.EqualTo(.04f).Within(.00001f));
            Assert.IsTrue(clock.AdvanceAtTick(0, 0, 11, true, out scaled, out _));
            Assert.AreEqual(0, scaled);
            var late = new EffectUpdateClock();
            late.AdvanceAtTick(0, 0, 11, false, out _, out _);
            Assert.IsFalse(late.AdvanceAtTick(0, 0, 11, false, out _, out _));
            Assert.IsTrue(late.AdvanceAtTick(0, 0, 12, false, out _, out _));
            Assert.IsTrue(clock.AdvanceAtTick(0, 0, 12, false, out _, out _));
        }

        [Test]
        public void MaskUnchangedAndMaterialOnlyUpdatesDoNotResubmitGeometry()
        {
            var graphic = CanvasSpriteMaskGraphic.Create(_effect, "cache test");
            UIEffectProfiler.current = default;
            graphic.Configure(null, Matrix4x4.identity, 0, 0, 128, true);
            Assert.AreEqual(1, UIEffectProfiler.current.maskMeshSubmissions);
            graphic.Configure(null, Matrix4x4.identity, 0, 0, 128, true);
            Assert.AreEqual(1, UIEffectProfiler.current.maskMeshSubmissions);
            graphic.Configure(null, Matrix4x4.identity, 128, 128, 128, true);
            Assert.AreEqual(1, UIEffectProfiler.current.maskMeshSubmissions);
            Assert.AreEqual(128, graphic.canvasRenderer.GetMaterial().GetInt("_Stencil"));
            graphic.gameObject.SetActive(false); graphic.gameObject.SetActive(true);
            graphic.Configure(null, Matrix4x4.identity, 128, 128, 128, true);
            Assert.AreEqual(2, UIEffectProfiler.current.maskMeshSubmissions);
            graphic.InvalidateGeometry();
            graphic.Configure(null, Matrix4x4.identity, 128, 128, 128, true);
            Assert.AreEqual(3, UIEffectProfiler.current.maskMeshSubmissions);
        }

        [TestCase(false)] [TestCase(true)]
        public void SuppressionRestoresOriginalValue(bool original)
        {
            var source = AddMesh(); source.forceRenderingOff = original;
            var renderer = Bridge();
            Assert.IsTrue(source.forceRenderingOff);
            Assert.IsTrue(source.enabled);
            source.forceRenderingOff = false;
            _effect.PrepareForUpdate();
            Assert.IsTrue(source.forceRenderingOff);
            _effect.enabled = false;
            Assert.AreEqual(original, source.forceRenderingOff);
        }

        [Test]
        public void ListRefreshAndReenableDiscoverMeshesWithoutChangingParticleSelection()
        {
            var go = new GameObject("particle", typeof(ParticleSystem)); go.transform.SetParent(_effect.transform);
            var ps = go.GetComponent<ParticleSystem>();
            _effect.RefreshSources(new List<ParticleSystem> { ps });
            AddMesh(); _effect.enabled = false; _effect.enabled = true;
            Assert.AreEqual(2, _effect.activeRendererCount);
            CollectionAssert.AreEqual(new[] { ps }, _effect.particles);
        }

        [Test]
        public void NestedEffectsOwnTheirSources()
        {
            var nested = new GameObject("nested", typeof(RectTransform)); nested.transform.SetParent(_effect.transform);
            var child = nested.AddComponent<global::ShanFlyer.UIEffects.UIEffectRenderer>(); child.unitConversion = global::ShanFlyer.UIEffects.UIEffectRenderer.UnitConversion.Manual;
            var source = AddMesh(nested.transform);
            _effect.RefreshSources(); Assert.AreEqual(0, _effect.activeRendererCount);
            child.RefreshSources(); Assert.AreEqual(1, child.activeRendererCount);
            Assert.IsTrue(source.forceRenderingOff);
        }

        [Test]
        public void UnsupportedMaterialLayoutReleasesSource()
        {
            var source = AddMesh(); Bridge();
            source.sharedMaterials = new Material[] { _material, null };
            _effect.PrepareForUpdate();
            Assert.AreEqual(0, _effect.activeRendererCount);
            Assert.IsFalse(source.forceRenderingOff);
        }

        [Test]
        public void RuntimeSwitchRestoresAndRecollects()
        {
            var source = AddMesh(); Bridge();
            _effect.renderMeshes = false; _effect.PrepareForUpdate();
            Assert.IsFalse(source.forceRenderingOff); Assert.AreEqual(0, _effect.activeRendererCount);
            _effect.renderMeshes = true; _effect.PrepareForUpdate();
            Assert.IsTrue(source.forceRenderingOff); Assert.AreEqual(1, _effect.activeRendererCount);
        }

        [Test]
        public void RemovingOnlyEffectRestoresSourcesAndRemovesGeneratedOutputs()
        {
            var source = AddMesh(); Bridge();
            var parent = _effect.transform;
            Object.DestroyImmediate(_effect);
            Assert.IsFalse(source.forceRenderingOff);
            Assert.AreEqual(0, parent.GetComponentsInChildren<CanvasEffectOutput>(true).Length);
        }

        [Test]
        public void BridgeReadsSourcePropertyBlockWithoutMutatingSharedMaterial()
        {
            var source = AddMesh();
            var property = new MaterialPropertyBinding("_Color", MaterialPropertyBinding.ValueKind.Color);
            _effect.propertyBindings = new[] { property };
            var block = new MaterialPropertyBlock(); block.SetColor("_Color", Color.red);
            source.SetPropertyBlock(block);
            var renderer = Bridge(); renderer.UpdateMesh(_camera);
            Assert.AreEqual(Color.red, renderer.materialForRendering.GetColor("_Color"));
            Assert.AreEqual(Color.white, _material.GetColor("_Color"));
            source.SetPropertyBlock(null);
            typeof(CanvasEffectOutput).GetField("_lastBakeFrame", Private).SetValue(renderer, -1);
            renderer.UpdateMesh(_camera);
            Assert.AreEqual(Color.white, renderer.materialForRendering.GetColor("_Color"));
        }

        [Test]
        public void BridgeAppliesMeshModifierAndRecalculatesBounds()
        {
            AddMesh(); var renderer = Bridge();
            var modifier = renderer.gameObject.AddComponent<HPTestMeshModifier>();
            renderer.UpdateMesh(_camera);
            Assert.That(Output(renderer).bounds.center.x, Is.EqualTo(4.5f).Within(0.001f));
            Assert.AreEqual(Vector3.zero, _mesh.vertices[0]);
            Object.DestroyImmediate(modifier);
            NextBridgeUpdate(renderer);
            Assert.AreEqual(Vector3.zero, Output(renderer).vertices[0], "removing a modifier invalidates its output");
        }

        [Test]
        public void MeshUsesPrivateOutputAndScalesGeometryWithoutScalingPlacement()
        {
            var source = AddMesh();
            _effect.transform.position = new Vector3(10, 20, 0);
            source.transform.localPosition = new Vector3(2, 0, 0);
            _effect.uniformScale = 3;
            var renderer = Bridge(); renderer.UpdateMesh(_camera);
            var output = Output(renderer);
            Assert.AreNotSame(_mesh, output);
            Assert.That(Vector3.Distance(output.vertices[0], new Vector3(2, 0, 0)), Is.LessThan(0.001));
            Assert.That(Vector3.Distance(output.vertices[1], new Vector3(5, 0, 0)), Is.LessThan(0.001));
            Assert.AreEqual(Vector3.zero, _mesh.vertices[0]);
        }

        [TestCase(true)] [TestCase(false)]
        public void LineBakeHasCorrectSpaceUnderRotatedScaledSource(bool worldSpace)
        {
            var go = new GameObject("line", typeof(LineRenderer)); go.transform.SetParent(_effect.transform, false);
            var line = go.GetComponent<LineRenderer>();
            line.sharedMaterial = _material; line.useWorldSpace = worldSpace;
            line.alignment = LineAlignment.TransformZ; line.widthMultiplier = 0.1f;
            line.positionCount = 2;
            _effect.transform.position = new Vector3(10, 20, 0);
            line.transform.localPosition = new Vector3(3, 4, 0);
            line.transform.localRotation = Quaternion.Euler(0, 0, 37);
            line.transform.localScale = new Vector3(2, 0.5f, 1);
            var a = new Vector3(1, 1, 0); var b = new Vector3(3, 1, 0);
            line.SetPosition(0, a); line.SetPosition(1, b);
            var expected = worldSpace ? (a + b) * 0.5f : line.transform.TransformPoint((a + b) * 0.5f);
            _effect.uniformScale = 3;
            var origin = worldSpace ? _effect.transform.position : line.transform.position;
            expected = origin + (expected - origin) * 3;
            var renderer = Bridge(); renderer.UpdateMesh(_camera);
            var output = Output(renderer); Assert.IsNotNull(output);
            var actual = renderer.transform.TransformPoint(output.bounds.center);
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(0.01f), "BakeMesh coordinate convention changed");
        }

        [Test]
        public void EmptyLineClearsOldCanvasOutput()
        {
            var go = new GameObject("line", typeof(LineRenderer)); go.transform.SetParent(_effect.transform, false);
            var line = go.GetComponent<LineRenderer>(); line.sharedMaterial = _material;
            line.positionCount = 2; line.SetPosition(0, Vector3.zero); line.SetPosition(1, Vector3.right);
            var renderer = Bridge(); renderer.UpdateMesh(_camera);
            Assert.IsNotNull(Output(renderer));
            line.positionCount = 0;
            typeof(CanvasEffectOutput).GetField("_lastBakeFrame", Private).SetValue(renderer, -1);
            renderer.UpdateMesh(_camera);
            Assert.AreEqual(0, renderer.canvasRenderer.materialCount);
        }

        [Test]
        public void TrailHistoryDoesNotRotateWithItsHead()
        {
            var go = new GameObject("trail", typeof(TrailRenderer)); go.transform.SetParent(_effect.transform, false);
            var trail = go.GetComponent<TrailRenderer>(); trail.sharedMaterial = _material;
            trail.emitting = false; trail.time = 100; trail.alignment = LineAlignment.TransformZ;
            trail.widthMultiplier = 0.1f;
            _effect.transform.position = new Vector3(10, 20, 0);
            // Unity keeps the last trail point at its live head even with emitting=false.
            trail.transform.position = new Vector3(3, 1, 0);
            trail.transform.localRotation = Quaternion.Euler(0, 0, 37);
            trail.transform.localScale = new Vector3(2, 0.5f, 1);
            trail.Clear(); trail.AddPositions(new[] { new Vector3(1, 1, 0), new Vector3(3, 1, 0) });
            _effect.uniformScale = 3;
            var renderer = Bridge(); renderer.UpdateMesh(_camera);
            var output = Output(renderer); Assert.IsNotNull(output);
            var expected = _effect.transform.position + (new Vector3(2, 1, 0) - _effect.transform.position) * 3;
            var scratch = (Mesh)typeof(CanvasEffectOutput).GetField("_lineScratch", Private).GetValue(renderer);
            Assert.That(Vector3.Distance(expected, renderer.transform.TransformPoint(output.bounds.center)), Is.LessThan(0.01f),
                "expected=" + expected + " actual=" + renderer.transform.TransformPoint(output.bounds.center)
                + " raw=" + scratch.bounds.center + " source=" + trail.transform.position);
        }

        [Test]
        public void WorldParticleMappingDoesNotRewriteNativeParticlePositions()
        {
            var go = new GameObject("particle", typeof(ParticleSystem)); go.transform.SetParent(_effect.transform);
            var ps = go.GetComponent<ParticleSystem>(); var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            go.transform.position = new Vector3(10, 0, 0);
            _effect.RefreshSources(); _effect.PrepareForUpdate();
            var renderer = _effect.GetRendererIfExists(0);
            var matrix = ParticleCoordinates.BakeToWorld(ps, false, Vector3.one * 3,
                _effect.transform.position, UIEffectRenderer.OriginMode.Emitter);
            Assert.AreEqual(new Vector3(16, 0, 0), matrix.MultiplyPoint3x4(new Vector3(12, 0, 0)));
            ps.Play(); ps.Pause();
            ps.SetParticles(new[] { new ParticleSystem.Particle { position = new Vector3(2, 0, 0), remainingLifetime = 10, startLifetime = 10 } });
            Assert.AreEqual(1, ps.particleCount, "Test particle must be alive before remapping");
            var state = new ParticleSimulation();
            state.Advance(ps, _effect, 0);
            var values = new ParticleSystem.Particle[1]; ps.GetParticles(values);
            Assert.AreEqual(new Vector3(2, 0, 0), values[0].position);
        }

        [Test]
        public void OrphanReplicaUpdatesItsOwnBridge()
        {
            AddMesh(); var renderer = Bridge();
            _effect.simulationRole = global::ShanFlyer.UIEffects.UIEffectRenderer.SimulationRole.Consumer;
            _effect.UpdateBridgeRenderers();
            Assert.IsNotNull(Output(renderer));
            _effect.ClearRendererMeshes();
            Assert.AreEqual(1, renderer.canvasRenderer.materialCount);
        }

        [TestCase(false)] [TestCase(true)]
        public void BodyAndTrailRestoreOneSourceEnabledState(bool original)
        {
            var go = new GameObject("particle", typeof(ParticleSystem)); go.transform.SetParent(_effect.transform);
            var ps = go.GetComponent<ParticleSystem>(); var trails = ps.trails; trails.enabled = true;
            var source = go.GetComponent<ParticleSystemRenderer>(); source.enabled = original;
            _effect.RefreshSources(); Assert.AreEqual(2, _effect.activeRendererCount);
            _effect.enabled = false;
            Assert.AreEqual(original, source.enabled);
        }

        [Test]
        public void SortingInterleavesMeshAndParticlesWithoutChangingSlots()
        {
            var go = new GameObject("particle", typeof(ParticleSystem)); go.transform.SetParent(_effect.transform);
            go.GetComponent<ParticleSystemRenderer>().sortingOrder = 20;
            var source = AddMesh(); source.sortingOrder = 10;
            _effect.sortBySourceOrder = true;
            _effect.RefreshSources(); _effect.PrepareForUpdate();
            Assert.Less(_effect.GetRendererIfExists(1).transform.GetSiblingIndex(), _effect.GetRendererIfExists(0).transform.GetSiblingIndex());
        }

        [Test]
        public void SnapshotRejectsInvalidIndicesAndNonfiniteVertices()
        {
            var validator = new LineSnapshotValidator();
            var vertices = new List<Vector3> { Vector3.zero, Vector3.right, Vector3.up };
            Assert.AreEqual(LineSnapshotValidator.Result.Corrupt, validator.Evaluate(vertices, new List<int> { 0, 1, 9 }));
            vertices[0] = new Vector3(float.NaN, 0, 0);
            Assert.AreEqual(LineSnapshotValidator.Result.Corrupt, validator.Evaluate(vertices, new List<int> { 0, 1, 2 }));
        }

        [Test]
        public void SnapshotRebasesAfterBoundedRejectionAndEmptyResets()
        {
            var validator = new LineSnapshotValidator();
            var vertices = new List<Vector3> { Vector3.zero, Vector3.right, Vector3.up };
            var indices = new List<int> { 0, 1, 2 };
            Assert.AreEqual(LineSnapshotValidator.Result.Valid, validator.Evaluate(vertices, indices));
            for (var i = 0; i < vertices.Count; i++) vertices[i] += Vector3.right * 100;
            for (var i = 0; i < 3; i++) Assert.AreEqual(LineSnapshotValidator.Result.Discontinuity, validator.Evaluate(vertices, indices));
            Assert.IsTrue(validator.expired);
            Assert.AreEqual(LineSnapshotValidator.Result.Valid, validator.Evaluate(vertices, indices));
            Assert.AreEqual(LineSnapshotValidator.Result.Empty, validator.Evaluate(new List<Vector3>(), indices));
            Assert.IsFalse(validator.expired);
        }
    }

    public sealed class HPTestMeshModifier : BaseMeshEffect
    {
        public override void ModifyMesh(VertexHelper helper)
        {
            for (var i = 0; i < helper.currentVertCount; i++)
            {
                var vertex = new UIVertex(); helper.PopulateUIVertex(ref vertex, i);
                vertex.position += Vector3.right * 4; helper.SetUIVertex(vertex, i);
            }
        }
    }
}
