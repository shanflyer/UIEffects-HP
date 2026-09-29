using System;
using UnityEngine;
using Object = UnityEngine.Object;
namespace ShanFlyer.UIEffects
{
    public static partial class UIEffectSpriteMaskValidation
    {
        private static void RunExtendedBridgeCases()
        {
            s_Canvas.gameObject.SetActive(false); s_Root.SetActive(false); s_Mask.enabled = false; s_Mask2.enabled = false;
            var canvas = Keep(new GameObject("Extended bridge GPU", typeof(Canvas))).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = s_Camera;
            var host = new GameObject("effect", typeof(RectTransform)); host.transform.SetParent(canvas.transform, false);
            var effect = host.AddComponent<UIEffectRenderer>(); effect.unitConversion = global::ShanFlyer.UIEffects.UIEffectRenderer.UnitConversion.Manual; effect.enabled = false;
            effect.uniformScale = 1; effect.scaleMode = UIEffectRenderer.ScaleMode.Hierarchy;
            var child = new GameObject("sprite", typeof(SpriteRenderer)); child.transform.SetParent(host.transform, false);
            var source = child.GetComponent<SpriteRenderer>();
            var material = Keep(new Material(Shader.Find("Sprites/Default"))); source.sharedMaterial = material;
            var texture = Keep(new Texture2D(16,16, TextureFormat.RGBA32,false)); texture.filterMode = FilterMode.Point;
            var pixels = new Color[256];
            for (int y = 0; y < 16; ++y) for (int x = 0; x < 16; ++x)
                pixels[y*16+x] = x % 5 < 3 && y % 7 < 4 ? Color.white : Color.clear;
            texture.SetPixels(pixels); texture.Apply();
            var sprite = Keep(Sprite.Create(texture, new Rect(0,0,16,16), new Vector2(.3f,.7f),16,0,SpriteMeshType.FullRect,new Vector4(2,3,4,2)));
            source.sprite = sprite;
            void CompareBridge(string name)
            {
                effect.enabled = false; var native = CaptureCurrent(false);
                effect.enabled = true; effect.RefreshSources(); var ui = CaptureCurrent(true);
                Check(Lit(native) > 0, name + " native output is visible");
                ComparePixels(native, ui, name);
                if (name.StartsWith("Mesh") || name.StartsWith("Skinned") || name.StartsWith("Sprite tint"))
                {
                    int colorErrors = 0;
                    for (int i = 0; i < native.Length; ++i)
                        if (Mathf.Abs(native[i].r - ui[i].r) > 4 || Mathf.Abs(native[i].g - ui[i].g) > 4 || Mathf.Abs(native[i].b - ui[i].b) > 4) ++colorErrors;
                    Check(colorErrors <= 8, name + " material colors errors=" + colorErrors);
                }
            }
            CompareBridge("Sprite Simple custom pivot");
            source.flipX = source.flipY = true; CompareBridge("Sprite Simple flipped");
            source.flipX = source.flipY = false;
            source.color = new Color(.5f,.25f,.75f,.65f); CompareBridge("Sprite tint and alpha"); source.color = Color.white;
            var atlasRegion = Keep(Sprite.Create(texture, new Rect(4,0,8,16), new Vector2(.7f,.2f),16));
            source.sprite = atlasRegion; CompareBridge("Sprite atlas subrect animation"); source.sprite = sprite;
            source.drawMode = SpriteDrawMode.Sliced; source.size = new Vector2(2.1f,1.7f); CompareBridge("Sprite Sliced borders");
            source.size = new Vector2(.3f,.25f); CompareBridge("Sprite Sliced undersized borders");
            source.drawMode = SpriteDrawMode.Tiled; source.size = new Vector2(2.1f,1.7f);
            source.tileMode = SpriteTileMode.Continuous; CompareBridge("Sprite Tiled Continuous");
            source.tileMode = SpriteTileMode.Adaptive; source.adaptiveModeThreshold = .5f; CompareBridge("Sprite Tiled Adaptive");
            effect.enabled = false; Object.DestroyImmediate(child);
            var mesh = Keep(new Mesh());
            mesh.vertices = new[] { new Vector3(-1,-.5f), new Vector3(-1,.5f), new Vector3(0,0), new Vector3(0,-.5f), new Vector3(0,.5f), new Vector3(1,0) };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.right, Vector2.zero, Vector2.up, Vector2.right };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white, Color.white, Color.white };
            mesh.subMeshCount = 2; mesh.SetTriangles(new[] {0,1,2},0); mesh.SetTriangles(new[] {3,4,5},1);
            var uiMaterial = Keep(new Material(Shader.Find("UI/Default"))); var red = Keep(new Material(uiMaterial)); red.color = Color.red;
            child = new GameObject("mesh",typeof(MeshFilter),typeof(MeshRenderer)); child.transform.SetParent(host.transform,false);
            child.GetComponent<MeshFilter>().sharedMesh = mesh; child.GetComponent<MeshRenderer>().sharedMaterials = new[] {uiMaterial,red};
            CompareBridge("Mesh two submeshes and materials");
            child.GetComponent<MeshRenderer>().sharedMaterials = new[] {uiMaterial}; CompareBridge("Mesh fewer materials than submeshes");
            child.GetComponent<MeshRenderer>().sharedMaterials = new[] {uiMaterial,red,uiMaterial}; CompareBridge("Mesh extra material overlays last submesh");
            effect.enabled = false; Object.DestroyImmediate(child);
            child = new GameObject("skin",typeof(SkinnedMeshRenderer)); child.transform.SetParent(host.transform,false);
            var skin = child.GetComponent<SkinnedMeshRenderer>();
            var bone = new GameObject("bone").transform; bone.SetParent(child.transform,false);
            mesh.bindposes = new[] {Matrix4x4.identity}; var weights = new BoneWeight[6];
            for (int i = 0; i < 6; ++i) weights[i] = new BoneWeight {boneIndex0=0,weight0=1};
            mesh.boneWeights = weights; skin.sharedMesh = mesh; skin.bones = new[] {bone}; skin.rootBone = bone;
            skin.sharedMaterials = new[] {uiMaterial,red}; skin.updateWhenOffscreen = true;
            bone.localPosition = new Vector3(.2f,.3f); CompareBridge("Skinned mesh bone deformation");
            // Every material output must independently participate in UGUI parent clipping/stencil.
            effect.enabled = true; effect.RefreshSources(); var unclipped = CaptureCurrent(true);
            var maskHost = new GameObject("UI clip", typeof(RectTransform)); maskHost.transform.SetParent(canvas.transform,false);
            ((RectTransform)maskHost.transform).sizeDelta = new Vector2(.7f,.7f); host.transform.SetParent(maskHost.transform,false);
            var rectMask = maskHost.AddComponent<UnityEngine.UI.RectMask2D>(); var clipped = CaptureCurrent(true);
            Check(Lit(clipped) > 0 && Lit(clipped) < Lit(unclipped), "Skinned material outputs obey RectMask2D");
            Object.DestroyImmediate(rectMask);
            var image = maskHost.AddComponent<UnityEngine.UI.Image>(); image.color = Color.white;
            var mask = maskHost.AddComponent<UnityEngine.UI.Mask>(); mask.showMaskGraphic = false;
            var stencil = CaptureCurrent(true);
            Check(Lit(stencil) > 0 && Lit(stencil) < Lit(unclipped), "Skinned material outputs obey UGUI Mask");
            effect.enabled = false; canvas.gameObject.SetActive(false);
        }
    }
}
