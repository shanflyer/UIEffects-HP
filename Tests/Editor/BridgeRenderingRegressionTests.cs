using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ShanFlyer.UIEffects.Editor.Tests
{
    public class BridgeRenderingRegressionTests
    {
        private readonly List<Object> objects = new List<Object>();
        private static readonly FieldInfo BakeFrame = typeof(CanvasEffectOutput).GetField("_lastBakeFrame", BindingFlags.Instance | BindingFlags.NonPublic);
        private Camera camera;
        private Canvas canvas;
        private UIEffectRenderer effect;
        private Material material;
        private int oldCull;

        private T Keep<T>(T value) where T : Object { objects.Add(value); return value; }

        [SetUp] public void Setup()
        {
            oldCull = UIEffectRenderer.earlyCull;
            UIEffectRenderer.earlyCull = 0;
            camera = Keep(new GameObject("Regression camera", typeof(Camera))).GetComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 3;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.allowHDR = camera.allowMSAA = false;
            camera.targetTexture = Keep(new RenderTexture(128, 128, 24));
            canvas = Keep(new GameObject("Regression canvas", typeof(Canvas))).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            var host = new GameObject("Effect", typeof(RectTransform)); host.transform.SetParent(canvas.transform, false);
            effect = host.AddComponent<UIEffectRenderer>();
            effect.unitConversion = UIEffectRenderer.UnitConversion.Manual;
            effect.scaleMode = UIEffectRenderer.ScaleMode.Hierarchy; effect.uniformScale = 1;
            material = Keep(new Material(Shader.Find("UI/Default")));
        }

        [TearDown] public void Cleanup()
        {
            UIEffectRenderer.earlyCull = oldCull;
            // Release source leases before destroying their assets.
            if (canvas) Object.DestroyImmediate(canvas.gameObject);
            if (camera) { camera.targetTexture = null; Object.DestroyImmediate(camera.gameObject); }
            for (int i = objects.Count - 1; i >= 0; --i) if (objects[i]) Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        private T Source<T>() where T : Renderer
        {
            var node = new GameObject(typeof(T).Name); node.transform.SetParent(effect.transform, false);
            var source = node.AddComponent<T>(); source.sharedMaterial = material; return source;
        }

        private CanvasEffectOutput Bind()
        {
            effect.RefreshSources(); Canvas.ForceUpdateCanvases(); effect.PrepareForUpdate();
            Assert.AreEqual(1, effect.activeRendererCount);
            return effect.GetRendererIfExists(0);
        }

        private void Draw(CanvasEffectOutput output)
        {
            BakeFrame.SetValue(output, -1); output.UpdateMesh(camera); Canvas.ForceUpdateCanvases();
        }

        private Mesh ReadMesh(CanvasEffectOutput output)
        {
            return (Mesh)typeof(CanvasEffectOutput).GetField("_bridgeOutput", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(output);
        }

        [TestCase(false, false)] [TestCase(true, false)]
        [TestCase(false, true)] [TestCase(true, true)]
        public void MeshWithoutVertexColorsIsVisibleOnGpu(bool skinned, bool modifier)
        {
            var mesh = Keep(new Mesh
            {
                vertices = new[] { new Vector3(-1,-1), new Vector3(-1,1), new Vector3(1,1), new Vector3(1,-1) },
                uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right },
                triangles = new[] { 0,1,2, 2,3,0 }
            });
            if (skinned)
            {
                var skin = Source<SkinnedMeshRenderer>();
                var bone = new GameObject("Bone").transform; bone.SetParent(skin.transform, false);
                mesh.bindposes = new[] { Matrix4x4.identity };
                var weights = new BoneWeight[4];
                for (int i = 0; i < weights.Length; ++i) weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1 };
                mesh.boneWeights = weights; skin.sharedMesh = mesh; skin.bones = new[] { bone }; skin.rootBone = bone;
            }
            else Source<MeshRenderer>().gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var output = Bind();
            if (modifier) output.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(.05f, -.05f);
            Draw(output);
            camera.Render();
            var readback = Keep(new Texture2D(128, 128, TextureFormat.RGBA32, false));
            var previous = RenderTexture.active;
            try { RenderTexture.active = camera.targetTexture; readback.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); readback.Apply(); }
            finally { RenderTexture.active = previous; }
            Assert.Greater(readback.GetPixel(64, 64).r, .8f, "Colorless source must render opaque white through CanvasRenderer.");
            Assert.AreEqual(0, mesh.colors32.Length, "Never modify the source asset.");
        }

        [TestCase(RenderMode.ScreenSpaceOverlay, false)] [TestCase(RenderMode.ScreenSpaceCamera, false)]
        [TestCase(RenderMode.ScreenSpaceOverlay, true)] [TestCase(RenderMode.ScreenSpaceCamera, true)]
        public void AutomaticWorldPathsKeepTheirRecordedUiPositions(RenderMode mode, bool trailSource)
        {
            canvas.renderMode = mode; canvas.planeDistance = 10;
            effect.unitConversion = UIEffectRenderer.UnitConversion.Automatic;
            Canvas.ForceUpdateCanvases(); effect.PrepareForUpdate();
            var a = canvas.transform.TransformPoint(new Vector3(15, 5, 0));
            var b = canvas.transform.TransformPoint(new Vector3(45, 5, 0));
            if (trailSource)
            {
                var trail = Source<TrailRenderer>(); trail.emitting = false; trail.time = 100;
                trail.transform.position = b; trail.alignment = LineAlignment.View;
                trail.widthCurve = AnimationCurve.Constant(0, 1, 1);
                trail.widthMultiplier = .1f; trail.Clear(); trail.AddPositions(new[] { a, b });
            }
            else
            {
                var line = Source<LineRenderer>(); line.useWorldSpace = true; line.alignment = LineAlignment.View;
                line.widthMultiplier = .1f; line.positionCount = 2; line.SetPositions(new[] { a, b });
            }
            var output = Bind(); Draw(output); var mesh = ReadMesh(output);
            Assert.Greater(mesh.vertexCount, 0);
            var bounds = new Bounds(canvas.transform.InverseTransformPoint(output.transform.TransformPoint(mesh.vertices[0])), Vector3.zero);
            foreach (var vertex in mesh.vertices) bounds.Encapsulate(canvas.transform.InverseTransformPoint(output.transform.TransformPoint(vertex)));
            Assert.That(bounds.center.x, Is.EqualTo(30).Within(.03f), "Recorded path positions must not be converted a second time.");
            Assert.That(bounds.size.x, Is.EqualTo(30).Within(.03f));
            // EditMode AddPositions has no elapsed sampling time and can produce a
            // collapsed native ribbon. Live trail area is checked by the runtime test.
            if (!trailSource) Assert.Greater(bounds.size.y, .01f);
        }

        [TestCase(false)] [TestCase(true)]
        public void LocalLineViewAlignmentRemainsVisibleWithRotatedScaledSource(bool mirrored)
        {
            var line = Source<LineRenderer>(); line.useWorldSpace = false; line.alignment = LineAlignment.View;
            line.transform.localPosition = new Vector3(.2f, .3f, 0);
            line.transform.localRotation = Quaternion.Euler(0, 0, 37);
            line.transform.localScale = new Vector3(mirrored ? -2 : 2, .5f, 1);
            line.widthMultiplier = .2f; line.positionCount = 2; line.SetPositions(new[] { Vector3.left, Vector3.right });
            var output = Bind(); Draw(output); var mesh = ReadMesh(output);
            Assert.Greater(mesh.vertexCount, 0);
            Assert.That(Vector3.Distance(line.transform.position, output.transform.TransformPoint(mesh.bounds.center)), Is.LessThan(.001f));
            foreach (var vertex in mesh.vertices) Assert.That(vertex.z, Is.EqualTo(0).Within(.001f));
            float area = 0; var points = mesh.vertices; var indices = mesh.triangles;
            for (int i = 0; i < indices.Length; i += 3)
                area += Vector3.Cross(points[indices[i + 1]] - points[indices[i]], points[indices[i + 2]] - points[indices[i]]).magnitude * .5f;
            Assert.Greater(area, .1f, "View alignment must not collapse a rotated line.");
        }

        [Test] public void MovingLineEndpointsDoNotWaitForTrailOutlierRecovery()
        {
            var line = Source<LineRenderer>(); line.useWorldSpace = true;
            line.widthMultiplier = .2f; line.positionCount = 2; line.SetPositions(new[] { Vector3.zero, Vector3.right });
            var output = Bind(); Draw(output);
            line.SetPositions(new[] { new Vector3(100, 0), new Vector3(120, 0) });
            Draw(output);
            Assert.That(ReadMesh(output).bounds.center.x, Is.EqualTo(110).Within(.01f));
        }

        [Test] public void InspectorIncludesInvalidAndInactiveSourcesButExcludesNestedOwners()
        {
            var mesh = Source<MeshRenderer>(); // No MeshFilter: must remain visible with a diagnosis.
            var skin = Source<SkinnedMeshRenderer>();
            var sprite = Source<SpriteRenderer>();
            var line = Source<LineRenderer>();
            var trail = Source<TrailRenderer>(); trail.gameObject.SetActive(false);
            var nested = new GameObject("Nested owner", typeof(RectTransform)); nested.transform.SetParent(effect.transform, false);
            nested.AddComponent<UIEffectRenderer>();
            var nestedSource = new GameObject("Nested mesh", typeof(MeshRenderer)); nestedSource.transform.SetParent(nested.transform, false);
            var collected = new List<Renderer>(); UIEffectRendererEditor.CollectOwnedSources(effect, collected);
            CollectionAssert.AreEquivalent(new Renderer[] { mesh, skin, sprite, line, trail }, collected);
            StringAssert.Contains("Assign a mesh", CanvasEffectOutput.BridgeIssue(mesh));
        }

        [TestCase(CameraType.Game)] [TestCase(CameraType.SceneView)]
        public void OrdinaryUnlitMeshIsVisibleToGameAndSceneCameras(CameraType cameraType)
        {
            camera.cameraType = cameraType;
            var mesh = Keep(new Mesh { vertices = new[] { new Vector3(-1,-1), new Vector3(-1,1), new Vector3(1,1), new Vector3(1,-1) },
                triangles = new[] { 0,1,2, 2,3,0 } });
            var source = Source<MeshRenderer>(); source.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            source.sharedMaterial = Keep(new Material(Shader.Find("Unlit/Color")) { color = Color.white });
            Draw(Bind()); camera.Render();
            var readback = Keep(new Texture2D(128, 128, TextureFormat.RGBA32, false));
            var previous = RenderTexture.active;
            try { RenderTexture.active = camera.targetTexture; readback.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); readback.Apply(); }
            finally { RenderTexture.active = previous; }
            Assert.Greater(readback.GetPixel(64, 64).r, .8f, "An ordinary unlit material must draw without any UI Mask properties.");
        }

        [TestCase("UI/Default", false)] [TestCase("UI/Default", true)]
        [TestCase("Unlit/Color", false)] [TestCase("Unlit/Color", true)]
        public void CubeRendersInsideScreenSpaceCameraCanvas(string shader, bool orthographic)
        {
            camera.orthographic = orthographic;
            camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
            effect.unitConversion = UIEffectRenderer.UnitConversion.Automatic;
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.SetParent(effect.transform, false);
            cube.GetComponent<MeshRenderer>().sharedMaterial = Keep(new Material(Shader.Find(shader)));
            var output = Bind(); Draw(output); camera.Render();
            var readback = Keep(new Texture2D(128, 128, TextureFormat.RGBA32, false));
            var previous = RenderTexture.active;
            try { RenderTexture.active = camera.targetTexture; readback.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); readback.Apply(); }
            finally { RenderTexture.active = previous; }
            Assert.Greater(readback.GetPixel(64, 64).r, .8f,
                "A 3D mesh inside the camera frustum must render through a Screen Space - Camera Canvas.");
        }

        [TestCase(false)] [TestCase(true)]
        public void ForegroundUiCoversMeshByHierarchy(bool sourceWritesDepth)
        {
            camera.orthographic = true;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
            effect.unitConversion = UIEffectRenderer.UnitConversion.Automatic;
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.SetParent(effect.transform, false);
            var sourceMaterial = Keep(new Material(Shader.Find("Standard")));
            sourceMaterial.color = Color.green;
            sourceMaterial.EnableKeyword("_EMISSION"); sourceMaterial.SetColor("_EmissionColor", Color.green);
            sourceMaterial.SetFloat("_ZWrite", sourceWritesDepth ? 1 : 0);
            cube.GetComponent<MeshRenderer>().sharedMaterial = sourceMaterial;
            var front = new GameObject("Foreground UI", typeof(RectTransform), typeof(Image));
            front.transform.SetParent(canvas.transform, false);
            front.GetComponent<Image>().color = Color.red;
            Draw(Bind()); camera.Render();
            var readback = Keep(new Texture2D(128, 128, TextureFormat.RGBA32, false));
            var previous = RenderTexture.active;
            try { RenderTexture.active = camera.targetTexture; readback.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); readback.Apply(); }
            finally { RenderTexture.active = previous; }
            Assert.Greater(readback.GetPixel(64, 64).r, .8f, "Later UI siblings must cover the preceding mesh.");
            Assert.Less(readback.GetPixel(64, 64).g, .1f);
            Assert.AreEqual(sourceWritesDepth ? 1 : 0, sourceMaterial.GetFloat("_ZWrite"), "Do not edit the authored material.");
        }

        [Test] public void EditModeCanvasRepaintRefreshesMeshWithoutAdvancingGameFrame()
        {
            Assert.IsFalse(Application.isPlaying);
            var mesh = Keep(new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0,1,2 } });
            var source = Source<MeshRenderer>(); source.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var output = Bind();
            Canvas.ForceUpdateCanvases();
            int frame = Time.frameCount;
            mesh.vertices = new[] { new Vector3(2,0), new Vector3(3,0), new Vector3(2,1) };
            mesh.RecalculateBounds();
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(frame, Time.frameCount);
            var submitted = ReadMesh(output);
            Assert.IsNotNull(submitted);
            Assert.That(submitted.bounds.center.x, Is.EqualTo(2.5f).Within(.001f), "Editor repaint cannot use the runtime frame counter as its update clock.");
        }
    }
}
