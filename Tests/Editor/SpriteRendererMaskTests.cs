using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
namespace ShanFlyer.UIEffects.Editor.Tests
{
    public class SpriteRendererMaskTests
    {
        private GameObject root;
        private UIEffectRenderer effect;
        private SpriteRenderer source;
        private SpriteMask mask;
        private Material material;
        private Texture2D texture;
        private Sprite sprite;
        [SetUp] public void Setup()
        {
            root = new GameObject("sprite mask tests", typeof(Canvas));
            var host = new GameObject("effect", typeof(RectTransform)); host.transform.SetParent(root.transform,false);
            effect = host.AddComponent<UIEffectRenderer>(); effect.unitConversion = global::ShanFlyer.UIEffects.UIEffectRenderer.UnitConversion.Manual; effect.uniformScale = 1; effect.scaleMode = UIEffectRenderer.ScaleMode.Hierarchy;
            texture = new Texture2D(32,32); sprite = Sprite.Create(texture,new Rect(0,0,32,32),Vector2.one*.5f,32);
            material = new Material(Shader.Find("UI/Default"));
            var child = new GameObject("sprite",typeof(SpriteRenderer)); child.transform.SetParent(host.transform,false);
            source = child.GetComponent<SpriteRenderer>(); source.sprite = sprite; source.sharedMaterial = material;
            source.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            var maskObject = new GameObject("external mask",typeof(SpriteMask)); maskObject.transform.SetParent(root.transform,false);
            mask = maskObject.GetComponent<SpriteMask>(); mask.sprite = sprite;
            effect.RefreshSources(); Refresh();
        }
        private void Refresh() { SpriteMaskResolver.BeginFrame(); effect.PrepareForUpdate(); }
        private CanvasEffectOutput Output => effect.GetRendererIfExists(0);
        [TearDown] public void Cleanup()
        {
            Object.DestroyImmediate(root); Object.DestroyImmediate(material); Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture);
        }
        [Test] public void ExternalMaskIsSuppressedAndRestoredWithTheLastOwner()
        {
            var clone = Object.Instantiate(effect,root.transform); clone.RefreshSources();
            SpriteMaskResolver.BeginFrame(); clone.PrepareForUpdate();
            Assert.IsTrue(mask.forceRenderingOff);
            effect.enabled = false; Assert.IsTrue(mask.forceRenderingOff);
            clone.enabled = false; Assert.IsFalse(mask.forceRenderingOff);
        }
        [Test] public void OriginallySuppressedMaskRemainsExcludedAndRestoresItsState()
        {
            effect.enabled = false; mask.forceRenderingOff = true; effect.enabled = true; Refresh();
            Assert.AreEqual(0,Output.canvasRenderer.materialCount);
            effect.enabled = false; Assert.IsTrue(mask.forceRenderingOff);
        }
        [Test] public void LiveInteractionAndEmptySetUpdateMaterialState()
        {
            Assert.AreEqual(128,Output.materialForRendering.GetInt("_Stencil"));
            source.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask; Refresh();
            Assert.AreEqual(0,Output.materialForRendering.GetInt("_Stencil"));
            Assert.AreEqual(128,Output.materialForRendering.GetInt("_StencilReadMask"));
            mask.enabled = false; Refresh(); Assert.AreEqual(1,Output.canvasRenderer.materialCount);
            source.maskInteraction = SpriteMaskInteraction.VisibleInsideMask; Refresh(); Assert.AreEqual(0,Output.canvasRenderer.materialCount);
            source.maskInteraction = SpriteMaskInteraction.None; Refresh(); Assert.AreEqual(1,Output.canvasRenderer.materialCount);
        }
        [Test] public void PropertySynchronizationPreservesStencilAndRestoresBaseColor()
        {
            effect.propertyBindings = new[] {new MaterialPropertyBinding("_Color",MaterialPropertyBinding.ValueKind.Color)};
            effect.MarkBindingDirty(); Refresh();
            var block = new MaterialPropertyBlock(); block.SetColor("_Color",Color.red); source.SetPropertyBlock(block);
            Output.UpdateMesh(null);
            Assert.AreEqual(Color.red,Output.materialForRendering.color);
            Assert.AreEqual(128,Output.materialForRendering.GetInt("_Stencil"));
            Assert.AreEqual(128,Output.materialForRendering.GetInt("_StencilReadMask"));
            source.SetPropertyBlock(null); typeof(CanvasEffectOutput).GetField("_lastBakeFrame",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(Output,-1);
            Output.UpdateMesh(null); Assert.AreEqual(Color.white,Output.materialForRendering.color);
            Assert.AreEqual(128,Output.materialForRendering.GetInt("_Stencil"));
        }
        [Test] public void SortRangeAndSortingGroupChangesResolveAgain()
        {
            mask.isCustomRangeActive = true; mask.backSortingOrder = 0; mask.frontSortingOrder = 3;
            source.sortingOrder = 0; Refresh(); Assert.AreEqual(0,Output.canvasRenderer.materialCount);
            source.sortingOrder = 3; Refresh(); Assert.AreEqual(1,Output.canvasRenderer.materialCount);
            var group = source.gameObject.AddComponent<SortingGroup>(); group.sortingOrder = 4;
            Refresh(); Assert.AreEqual(0,Output.canvasRenderer.materialCount);
            group.sortingOrder = 2; Refresh(); Assert.AreEqual(1,Output.canvasRenderer.materialCount);
        }
        [Test] public void DisabledOrEmptySpriteHasNoActiveStencilWriters()
        {
            source.enabled = false; Refresh();
            foreach (var writer in effect.GetComponentsInChildren<CanvasSpriteMaskGraphic>(true)) Assert.IsFalse(writer.gameObject.activeSelf);
            source.enabled = true; source.sprite = null; Refresh();
            foreach (var writer in effect.GetComponentsInChildren<CanvasSpriteMaskGraphic>(true)) Assert.IsFalse(writer.gameObject.activeSelf);
            source.sprite = sprite; Refresh();
            Assert.Greater(effect.GetComponentsInChildren<CanvasSpriteMaskGraphic>().Length,0);
        }
        [Test] public void EffectScopeDoesNotCaptureSiblingGroupMasks()
        {
            mask.gameObject.AddComponent<SortingGroup>(); Refresh(); Assert.AreEqual(0,Output.canvasRenderer.materialCount);
            Object.DestroyImmediate(mask.GetComponent<SortingGroup>()); Refresh(); Assert.AreEqual(1,Output.canvasRenderer.materialCount);
        }
    }
}
