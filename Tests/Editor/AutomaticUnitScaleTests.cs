using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShanFlyer.UIEffects.Editor.Tests
{
    public class AutomaticUnitScaleTests
    {
        private GameObject root, viewObject;
        private Canvas canvas;
        private Camera camera;
        private UIEffectRenderer effect;
        private Mesh mesh;
        private Material material;
        private RenderTexture target;
        private Transform source;
        private EffectBakeView bakeView;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp] public void Setup()
        {
            root = new GameObject("automatic units", typeof(Canvas)); canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.referencePixelsPerUnit = 100;
            canvas.scaleFactor = 1;
            viewObject = new GameObject("reference", typeof(Camera)); camera = viewObject.GetComponent<Camera>();
            camera.transform.position = new Vector3(0,0,-10); camera.orthographic = true; camera.orthographicSize = 5;
            target = new RenderTexture(1000,1000,16); camera.targetTexture = target;
            var host = new GameObject("effect", typeof(RectTransform)); host.transform.SetParent(root.transform, false);
            var node = new GameObject("mesh", typeof(MeshFilter), typeof(MeshRenderer)); node.transform.SetParent(host.transform, false);
            source = node.transform;
            mesh = new Mesh { vertices = new[] { new Vector3(-.5f,-.5f), new Vector3(-.5f,.5f), new Vector3(.5f,.5f), new Vector3(.5f,-.5f) },
                triangles = new[] { 0,1,2,2,3,0 } }; mesh.RecalculateBounds();
            node.GetComponent<MeshFilter>().sharedMesh = mesh;
            material = new Material(Shader.Find("UI/Default")); node.GetComponent<MeshRenderer>().sharedMaterial = material;
            effect = host.AddComponent<UIEffectRenderer>(); bakeView = new EffectBakeView();
            Canvas.ForceUpdateCanvases(); effect.RefreshSources();
        }
        [TearDown] public void Cleanup()
        {
            bakeView.Dispose(); Object.DestroyImmediate(root); Object.DestroyImmediate(viewObject);
            Object.DestroyImmediate(mesh); Object.DestroyImmediate(material); Object.DestroyImmediate(target);
        }
        private float DrawWorldWidth()
        {
            Canvas.ForceUpdateCanvases(); effect.PrepareForUpdate();
            var output = effect.GetRendererIfExists(0);
            typeof(CanvasEffectOutput).GetField("_lastBakeFrame", Private).SetValue(output, -1);
            output.UpdateMesh(bakeView.Resolve(effect, canvas));
            var result = (Mesh)typeof(CanvasEffectOutput).GetField("_bridgeOutput", Private).GetValue(output);
            Assert.IsNotNull(result); Assert.Greater(result.vertexCount, 0);
            Assert.That(Vector3.Distance(output.transform.TransformPoint(result.bounds.center), source.position), Is.LessThan(.001f));
            return output.transform.TransformVector(Vector3.right * result.bounds.size.x).magnitude;
        }
        [Test] public void NewComponentUsesAutomaticUnitsAndMultiplierOne()
        {
            Assert.AreEqual(UIEffectRenderer.UnitConversion.Automatic, effect.unitConversion);
            Assert.AreEqual(Vector3.one, effect.renderScale);
            Assert.AreEqual(100, DrawWorldWidth(), .01f);
            Assert.AreEqual(100, effect.resolvedUnitScale);
            canvas.referencePixelsPerUnit = 64;
            Assert.AreEqual(64, DrawWorldWidth(), .01f);
            effect.uniformScale = .5f;
            Assert.AreEqual(32, DrawWorldWidth(), .01f);
        }
        [Test] public void OverlayReferenceTracksCameraZoomAndCanvasPixelScaleOnce()
        {
            effect.referenceCamera = camera; canvas.scaleFactor = 2;
            Assert.AreEqual(100, DrawWorldWidth(), .01f);
            Assert.AreEqual(50, effect.resolvedUnitScale, .01f);
            camera.orthographicSize = 10;
            Assert.AreEqual(50, DrawWorldWidth(), .01f);
            Assert.AreEqual(25, effect.resolvedUnitScale, .01f);
            canvas.scaleFactor = 1;
            Assert.AreEqual(50, DrawWorldWidth(), .01f);
            Assert.AreEqual(50, effect.resolvedUnitScale, .01f);
        }
        [TestCase(false)] [TestCase(true)]
        public void CameraCanvasMatchesNativeWorldSizeWithoutDoubleScaling(bool perspective)
        {
            camera.orthographic = !perspective; camera.fieldOfView = 60;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 20;
            foreach (float factor in new[] { 1f, 2f })
            {
                canvas.scaleFactor = factor;
                foreach (float offset in new[] { 0f, 5f })
                {
                    effect.transform.localPosition = new Vector3(20,-10,offset);
                    Assert.AreEqual(1, DrawWorldWidth(), .001f, "Native world geometry must remain one unit wide.");
                }
            }
            camera.orthographicSize = 8; camera.fieldOfView = 40;
            Assert.AreEqual(1, DrawWorldWidth(), .001f);
        }
        [Test] public void OverlayPerspectiveUsesRootDepthAndHasFiniteFallbackBehindCamera()
        {
            camera.orthographic = false; camera.fieldOfView = 60; effect.referenceCamera = camera;
            effect.transform.position = Vector3.zero;
            float width = DrawWorldWidth();
            Assert.AreEqual(1000f / (20 * Mathf.Tan(30 * Mathf.Deg2Rad)), width, .02f);
            effect.transform.position += Vector3.forward * 10;
            Assert.AreEqual(width * .5f, DrawWorldWidth(), .02f);
            effect.transform.position = new Vector3(0,0,-20);
            Assert.AreEqual(100, DrawWorldWidth(), .02f);
        }
        [Test] public void WorldSpaceKeepsNativeTransformScale()
        {
            canvas.renderMode = RenderMode.WorldSpace; root.transform.localScale = Vector3.one * .2f;
            effect.transform.localScale = Vector3.one * 3;
            source.localScale = Vector3.one * 2;
            Assert.AreEqual(1.2f, DrawWorldWidth(), .001f);
            Assert.AreEqual(Vector3.one * 3, effect.transform.localScale);
            Assert.AreEqual(1, effect.resolvedUnitScale);
        }
        [Test] public void LegacySerializedMultiplierStaysManualAndNewSerializationStaysAutomatic()
        {
            var json = EditorJsonUtility.ToJson(effect);
            var clone = Object.Instantiate(effect, root.transform);
            Assert.AreEqual(UIEffectRenderer.UnitConversion.Automatic, clone.unitConversion);
            Object.DestroyImmediate(clone.gameObject);
            const string path = "Assets/AutomaticUnitScaleLegacyTest.prefab";
            try
            {
                effect.uniformScale = 100;
                PrefabUtility.SaveAsPrefabAsset(effect.gameObject, path);
                var yaml = System.IO.File.ReadAllText(path);
                yaml = System.Text.RegularExpressions.Regex.Replace(yaml,
                    @"^  (unitConversionValue|unitConversionVersion|referenceCameraValue):.*\r?\n", "",
                    System.Text.RegularExpressions.RegexOptions.Multiline);
                System.IO.File.WriteAllText(path, yaml);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var legacy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), root.transform);
                try
                {
                    var restored = legacy.GetComponent<UIEffectRenderer>();
                    Assert.AreEqual(UIEffectRenderer.UnitConversion.Manual, restored.unitConversion);
                    Assert.AreEqual(Vector3.one * 100, restored.renderScale);
                }
                finally { Object.DestroyImmediate(legacy); }
            }
            finally { AssetDatabase.DeleteAsset(path); }
            EditorJsonUtility.FromJsonOverwrite(json, effect);
            Assert.AreEqual(UIEffectRenderer.UnitConversion.Automatic, effect.unitConversion);
            Assert.AreEqual(Vector3.one, effect.renderScale);
        }
        [Test] public void SwitchingToAutomaticResetsAuthoredMultiplierAndRestoresScaleOnDisable()
        {
            effect.unitConversion = UIEffectRenderer.UnitConversion.Manual; effect.uniformScale = 100;
            effect.unitConversion = UIEffectRenderer.UnitConversion.Automatic;
            Assert.AreEqual(Vector3.one, effect.renderScale);
            root.transform.localScale = Vector3.one * 2;
            effect.transform.localScale = Vector3.one * 3;
            effect.PrepareForUpdate();
            effect.enabled = false;
            Assert.AreEqual(Vector3.one * 3, effect.transform.localScale);
        }
        [Test] public void ParticleAndMeshUseTheSameAutomaticUnitConversion()
        {
            var node = new GameObject("particle", typeof(ParticleSystem)); node.transform.SetParent(effect.transform, false);
            var ps = node.GetComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.Local; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            node.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            effect.RefreshSources(); effect.PrepareForUpdate();
            ps.Play(); ps.Pause(); ps.SetParticles(new[] { new ParticleSystem.Particle { startSize = 1, startColor = Color.white, startLifetime = 10, remainingLifetime = 10 } });
            var output = effect.GetRendererIfExists(0); output.UpdateMesh(bakeView.Resolve(effect, canvas));
            var result = (Mesh)typeof(CanvasEffectOutput).GetField("_outputMesh", Private).GetValue(output);
            Assert.IsNotNull(result); Assert.Greater(result.vertexCount, 0);
            Assert.AreEqual(100, output.transform.TransformVector(Vector3.right * result.bounds.size.x).magnitude, .1f);
            effect.referenceCamera = camera;
            camera.orthographicSize = 10;
            effect.PrepareForUpdate(); output.UpdateMesh(bakeView.Resolve(effect, canvas));
            Assert.AreEqual(50, output.transform.TransformVector(Vector3.right * result.bounds.size.x).magnitude, .1f,
                "A camera zoom must invalidate paused particle geometry.");
            effect.referenceCamera = null;
            effect.PrepareForUpdate(); output.UpdateMesh(bakeView.Resolve(effect, canvas));
            Assert.AreEqual(100, output.transform.TransformVector(Vector3.right * result.bounds.size.x).magnitude, .1f);
        }
    }
}
