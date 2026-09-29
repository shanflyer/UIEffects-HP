using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
namespace ShanFlyer.UIEffects.Editor.Tests
{
    public class ExtendedBridgeTests
    {
        private GameObject root;
        private UIEffectRenderer effect;
        private Material first, second;
        private Mesh mesh;
        private Texture2D texture;
        private Sprite sprite;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [SetUp] public void Setup()
        {
            UIEffectRenderer.mergeRenderers = false; UIEffectRenderer.earlyCull = 0; UIEffectRenderer.fastBindingMode = false;
            root = new GameObject("extended bridge", typeof(Canvas));
            var child = new GameObject("effect", typeof(RectTransform)); child.transform.SetParent(root.transform, false);
            effect = child.AddComponent<UIEffectRenderer>(); effect.unitConversion = global::ShanFlyer.UIEffects.UIEffectRenderer.UnitConversion.Manual; effect.uniformScale = 1; effect.scaleMode = UIEffectRenderer.ScaleMode.Hierarchy;
            first = new Material(Shader.Find("UI/Default")); second = new Material(first); second.color = Color.red;
            mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, new Vector3(2,0), new Vector3(3,0), new Vector3(2,1) },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.zero, Vector2.right, Vector2.up }, subMeshCount = 2 };
            mesh.SetTriangles(new[] { 0,1,2 }, 0); mesh.SetTriangles(new[] { 3,4,5 }, 1);
            texture = new Texture2D(16, 16); sprite = Sprite.Create(texture, new Rect(0,0,16,16), new Vector2(.25f,.75f), 16, 0, SpriteMeshType.FullRect, new Vector4(2,2,2,2));
        }
        [TearDown] public void Cleanup()
        {
            Object.DestroyImmediate(root); Object.DestroyImmediate(first); Object.DestroyImmediate(second);
            Object.DestroyImmediate(mesh); Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture);
        }
        private T Source<T>() where T : Renderer
        {
            var go = new GameObject(typeof(T).Name); go.transform.SetParent(effect.transform, false); return go.AddComponent<T>();
        }
        private MeshRenderer MeshSource()
        {
            var source = Source<MeshRenderer>(); source.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            source.sharedMaterials = new[] { first, second }; return source;
        }
        private void Bind() { effect.RefreshSources(); effect.PrepareForUpdate(); }
        private Mesh Draw(int slot)
        {
            var output = effect.GetRendererIfExists(slot);
            typeof(CanvasEffectOutput).GetField("_lastBakeFrame", Private).SetValue(output, -1); output.UpdateMesh(null);
            return (Mesh)typeof(CanvasEffectOutput).GetField("_bridgeOutput", Private).GetValue(output);
        }
        [Test] public void EachSubmeshHasItsOwnMaterialAndIndices()
        {
            MeshSource(); Bind(); Assert.AreEqual(2, effect.activeRendererCount);
            Assert.AreSame(first, effect.GetRendererIfExists(0).material); Assert.AreSame(second, effect.GetRendererIfExists(1).material);
            var left = Draw(0); var right = Draw(1);
            Assert.AreEqual(3, left.GetIndexCount(0)); Assert.AreEqual(3, right.GetIndexCount(0));
            var vertices = right.vertices; foreach (var index in right.triangles) Assert.GreaterOrEqual(vertices[index].x, 2);
        }
        [Test] public void ExtraMaterialRepeatsLastSubmeshAndPartialReleaseKeepsSuppression()
        {
            var source = MeshSource(); source.sharedMaterials = new[] { first, second, first }; Bind();
            Assert.AreEqual(3, effect.activeRendererCount); Draw(2);
            effect.GetRendererIfExists(0).Reset(); Assert.IsTrue(source.forceRenderingOff);
            effect.enabled = false; Assert.IsFalse(source.forceRenderingOff);
        }
        [Test] public void MaterialCountChangeReplansAndRestoresOriginalHiddenState()
        {
            var source = MeshSource(); source.forceRenderingOff = true; Bind();
            source.sharedMaterials = new[] { first }; effect.PrepareForUpdate(); Assert.AreEqual(1, effect.activeRendererCount);
            source.sharedMaterials = new[] { first, second }; effect.PrepareForUpdate(); Assert.AreEqual(2, effect.activeRendererCount);
            effect.enabled = false; Assert.IsTrue(source.forceRenderingOff);
        }
        [Test] public void MaterialSlotOverridesAreIndependent()
        {
            var source = MeshSource(); effect.propertyBindings = new[] { new MaterialPropertyBinding("_Color", MaterialPropertyBinding.ValueKind.Color) };
            var block = new MaterialPropertyBlock(); block.SetColor("_Color", Color.green); source.SetPropertyBlock(block, 1);
            Bind(); Draw(0); Draw(1);
            Assert.AreEqual(Color.white, effect.GetRendererIfExists(0).materialForRendering.color);
            Assert.AreEqual(Color.green, effect.GetRendererIfExists(1).materialForRendering.color);
            source.SetPropertyBlock(null, 1); Draw(1); Assert.AreEqual(Color.red, effect.GetRendererIfExists(1).materialForRendering.color);
        }
        [Test] public void SecondSubmeshIndexEditsInvalidateOutput()
        {
            MeshSource(); Bind(); Draw(1); mesh.SetTriangles(new[] { 0,1,2 }, 1); var output = Draw(1);
            var vertices = output.vertices; foreach (var index in output.triangles) Assert.LessOrEqual(vertices[index].x, 1);
        }
        [Test] public void SkinnedSnapshotIsSharedAndTracksBoneAndBlendshape()
        {
            var skin = Source<SkinnedMeshRenderer>(); var bone = new GameObject("bone").transform; bone.SetParent(skin.transform, false);
            mesh.bindposes = new[] { Matrix4x4.identity };
            var weights = new BoneWeight[mesh.vertexCount]; for (int i = 0; i < weights.Length; ++i) weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1 };
            mesh.boneWeights = weights;
            var delta = new Vector3[mesh.vertexCount]; for (int i = 0; i < delta.Length; ++i) delta[i] = Vector3.up;
            mesh.AddBlendShapeFrame("raise", 100, delta, new Vector3[delta.Length], new Vector3[delta.Length]);
            skin.sharedMesh = mesh; skin.bones = new[] { bone }; skin.rootBone = bone; skin.sharedMaterials = new[] { first, second };
            skin.updateWhenOffscreen = false; Bind(); Assert.IsTrue(skin.updateWhenOffscreen);
            int before = UIEffectProfiler.current.bakeOps; Draw(0); Draw(1); Assert.AreEqual(before + 1, UIEffectProfiler.current.bakeOps);
            bone.localPosition = Vector3.right; skin.SetBlendShapeWeight(0, 100); effect.MarkGeometryDirty();
            var output = Draw(0); Assert.GreaterOrEqual(output.bounds.min.x, .99f); Assert.GreaterOrEqual(output.bounds.min.y, .99f);
            effect.enabled = false; Assert.IsFalse(skin.updateWhenOffscreen); Assert.IsFalse(skin.forceRenderingOff);
        }
        [TestCase(SpriteDrawMode.Simple)] [TestCase(SpriteDrawMode.Sliced)] [TestCase(SpriteDrawMode.Tiled)]
        public void SpriteGeometryPreservesPivotColorAndFlips(SpriteDrawMode mode)
        {
            var source = Source<SpriteRenderer>(); source.sprite = sprite; source.sharedMaterial = first; source.drawMode = mode;
            source.size = new Vector2(3, 2); source.color = Color.green; Bind(); var output = Draw(0);
            Assert.IsNotNull(output); Assert.Greater(output.vertexCount, 0); Assert.AreEqual(Color.green, output.colors[0]);
            var original = output.bounds; source.flipX = true; source.flipY = true; output = Draw(0);
            Assert.That(Vector3.Distance(-original.center, output.bounds.center), Is.LessThan(.0001f));
            Assert.AreEqual(original.size, output.bounds.size);
            Assert.AreSame(texture, effect.GetRendererIfExists(0).mainTexture);
        }
        [Test] public void SpriteNullAndAnimationRecoverWithoutRebinding()
        {
            var source = Source<SpriteRenderer>(); source.sharedMaterial = first; Bind(); var renderer = effect.GetRendererIfExists(0); Draw(0);
            source.sprite = sprite; Assert.Greater(Draw(0).vertexCount, 0);
            source.sprite = null; Draw(0); Assert.AreEqual(0, renderer.canvasRenderer.materialCount);
            source.sprite = sprite; source.color = Color.red; Assert.AreEqual(Color.red, Draw(0).colors[0]);
            Assert.AreSame(renderer, effect.GetRendererIfExists(0));
        }
        [Test] public void SpriteReadbackIsReusedUntilExplicitInvalidation()
        {
            var source = Source<SpriteRenderer>(); source.sprite = sprite; source.sharedMaterial = first; Bind(); Draw(0);
            var geometry = typeof(CanvasEffectOutput).GetField("_spriteGeometry", Private).GetValue(effect.GetRendererIfExists(0));
            var points = geometry.GetType().GetField("points", Private);
            var snapshot = points.GetValue(geometry); Draw(0); Assert.AreSame(snapshot, points.GetValue(geometry));
            effect.MarkGeometryDirty(); Draw(0); Assert.AreNotSame(snapshot, points.GetValue(geometry));
            Assert.AreEqual(sprite.vertices.Length, ((Vector2[])points.GetValue(geometry)).Length);
        }
        [Test] public void ExtendedTypeSwitchesRestoreNativeSources()
        {
            var source = Source<SpriteRenderer>(); source.sprite = sprite; source.sharedMaterial = first; Bind();
            effect.renderSprites = false; effect.PrepareForUpdate(); Assert.IsFalse(source.forceRenderingOff);
            effect.renderSprites = true; effect.PrepareForUpdate(); Assert.IsTrue(source.forceRenderingOff);
            source.maskInteraction = SpriteMaskInteraction.VisibleInsideMask; effect.PrepareForUpdate(); Assert.IsTrue(source.forceRenderingOff);
            Assert.AreEqual(0, effect.GetRendererIfExists(0).canvasRenderer.materialCount);
        }
        [Test] public void ExcessiveTilingIsBounded()
        {
            var source = Source<SpriteRenderer>(); source.sprite = sprite; source.sharedMaterial = first;
            source.drawMode = SpriteDrawMode.Tiled; source.size = new Vector2(100000,100000); Bind(); Draw(0);
            Assert.AreEqual(0, effect.GetRendererIfExists(0).canvasRenderer.materialCount);
        }
    }
}
