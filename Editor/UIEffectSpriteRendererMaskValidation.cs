using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;
namespace ShanFlyer.UIEffects
{
    public static partial class UIEffectSpriteMaskValidation
    {
        private static void RunSpriteRendererMaskCases()
        {
            var canvas = Keep(new GameObject("Sprite masking GPU",typeof(Canvas))).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = s_Camera;
            var host = new GameObject("effect",typeof(RectTransform)); host.transform.SetParent(canvas.transform,false);
            var effect = host.AddComponent<UIEffectRenderer>(); effect.unitConversion = global::ShanFlyer.UIEffects.UIEffectRenderer.UnitConversion.Manual; effect.enabled = false; effect.uniformScale = 1;
            effect.scaleMode = UIEffectRenderer.ScaleMode.Hierarchy;
            var child = new GameObject("sprite",typeof(SpriteRenderer)); child.transform.SetParent(host.transform,false);
            var source = child.GetComponent<SpriteRenderer>();
            var texture = Keep(new Texture2D(32,32)); var pixels = new Color[1024];
            for (int i = 0; i < pixels.Length; ++i) pixels[i] = Color.white; texture.SetPixels(pixels); texture.Apply();
            var sprite = Keep(Sprite.Create(texture,new Rect(0,0,32,32),Vector2.one*.5f,16)); source.sprite = sprite;
            var nativeMaterial = Keep(new Material(Shader.Find("Sprites/Default")));
            var uiMaterial = Keep(new Material(Shader.Find("UI/Default")));
            var firstMask = new GameObject("mask A",typeof(SpriteMask)).GetComponent<SpriteMask>();
            firstMask.transform.SetParent(host.transform,false); firstMask.transform.localPosition = new Vector3(-.25f,0);
            firstMask.sprite = s_Sprite;
            var secondMask = new GameObject("mask B",typeof(SpriteMask)).GetComponent<SpriteMask>();
            secondMask.transform.SetParent(host.transform,false); secondMask.transform.localPosition = new Vector3(.25f,.25f);
            secondMask.sprite = s_Sprite; secondMask.enabled = false;
            Color32[] CompareSprite(string name)
            {
                effect.enabled = false; source.sharedMaterial = nativeMaterial;
                var native = CaptureCurrent(false);
                source.sharedMaterial = uiMaterial; effect.enabled = true; effect.RefreshSources();
                var ui = CaptureCurrent(true); ComparePixels(native,ui,"SpriteRenderer " + name); return ui;
            }
            source.maskInteraction = SpriteMaskInteraction.VisibleInsideMask; var inside = CompareSprite("Inside");
            Check(Lit(inside)>0,"SpriteRenderer Inside is visible");
            source.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask; CompareSprite("Outside");
            secondMask.enabled = true; CompareSprite("overlapping Outside union");
            source.maskInteraction = SpriteMaskInteraction.VisibleInsideMask; CompareSprite("overlapping Inside union");
            firstMask.alphaCutoff = .5f; CompareSprite("alpha cutoff");
            firstMask.enabled = secondMask.enabled = false; var empty = CompareSprite("Inside empty masks"); Check(Lit(empty)==0,"SpriteRenderer empty Inside is hidden");
            source.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask; CompareSprite("Outside empty masks");
            firstMask.enabled = true; source.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            firstMask.isCustomRangeActive = true; firstMask.backSortingOrder = 0; firstMask.frontSortingOrder = 3;
            source.sortingOrder = 0; CompareSprite("exclusive back sorting boundary");
            source.sortingOrder = 3; CompareSprite("inclusive front sorting boundary");
            var group = host.AddComponent<SortingGroup>();
            firstMask.transform.SetParent(canvas.transform,true); group.sortingOrder = 4; CompareSprite("external mask excludes sorted group");
            group.sortingOrder = 2; CompareSprite("external mask includes sorted group");
            firstMask.isCustomRangeActive = false;
            effect.enabled = false; Object.DestroyImmediate(group);
            firstMask.transform.SetParent(host.transform,true); source.sortingOrder = 0;
            // Compare effect zoom against an equivalent native hierarchy transform.
            effect.enabled = false; source.sharedMaterial = nativeMaterial;
            host.transform.localScale = Vector3.one*1.3f; var scaledNative = CaptureCurrent(false);
            host.transform.localScale = Vector3.one; effect.uniformScale = 1.3f;
            source.sharedMaterial = uiMaterial; effect.enabled = true; effect.RefreshSources();
            ComparePixels(scaledNative,CaptureCurrent(true),"SpriteRenderer effect scale transforms sprite and mask together");
            effect.uniformScale = 1;
            CompareSprite("reset hierarchy");
            // Live mode switches update stencil even without a fresh source bake.
            source.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask; var liveOutside = CaptureCurrent(true);
            ComparePixels(CompareSprite("Outside after live switch"),liveOutside,"SpriteRenderer live interaction update");
            source.maskInteraction = SpriteMaskInteraction.VisibleInsideMask; CompareSprite("Inside restore");
            effect.propertyBindings = new[] { new MaterialPropertyBinding("_Color",MaterialPropertyBinding.ValueKind.Color) };
            effect.MarkBindingDirty(); var block = new MaterialPropertyBlock(); block.SetColor("_Color",new Color(.7f,.2f,.4f,1)); source.SetPropertyBlock(block);
            CompareSprite("MPB retains mask"); source.SetPropertyBlock(null);
            var uncut = CaptureCurrent(true);
            var clipHost = new GameObject("UI mask",typeof(RectTransform)); clipHost.transform.SetParent(canvas.transform,false);
            ((RectTransform)clipHost.transform).sizeDelta = new Vector2(.6f,.6f); host.transform.SetParent(clipHost.transform,false);
            var rectMask = clipHost.AddComponent<RectMask2D>(); var rect = CaptureCurrent(true);
            Check(Lit(rect)>0 && Lit(rect)<Lit(uncut),"SpriteRenderer SpriteMask intersects RectMask2D");
            Object.DestroyImmediate(rectMask); clipHost.AddComponent<Image>(); var stencilMask = clipHost.AddComponent<Mask>(); stencilMask.showMaskGraphic = false;
            var stencil = CaptureCurrent(true);
            Check(Lit(stencil)>0 && Lit(stencil)<Lit(uncut),"SpriteRenderer SpriteMask intersects UGUI Mask");
            source.maskInteraction = SpriteMaskInteraction.None; var plain = CaptureCurrent(true);
            Check(Lit(plain)>=Lit(stencil),"SpriteRenderer removing SpriteMask preserves parent Mask");
            effect.enabled = false; Check(!firstMask.forceRenderingOff,"SpriteRenderer releases native mask suppression");
            canvas.gameObject.SetActive(false);
        }
    }
}
