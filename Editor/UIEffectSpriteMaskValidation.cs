using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ShanFlyer.UIEffects
{
    /// <summary>GPU reference comparisons; run in a separate Unity project/process with graphics enabled.</summary>
    public static partial class UIEffectSpriteMaskValidation
    {
        private static readonly List<string> s_Results = new List<string>();
        private static readonly List<Object> s_Resources = new List<Object>();
        private static Camera s_Camera;
        private static RenderTexture s_Target;
        private static Sprite s_Sprite;
        private static GameObject s_Root;
        private static ParticleSystem s_Particle;
        private static ParticleSystemRenderer s_Renderer;
        private static UIEffectRenderer s_Ui;
        private static Canvas s_Canvas;
        private static SpriteMask s_Mask, s_Mask2;
        private static Material s_Native, s_Material;
        private static int s_Failures;
        private const int Size = 128;


        public static void RunBatch()
        {
            s_Results.Clear();
            s_Failures = 0;
            if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batchmode project with graphics enabled.");
            var originalPipeline = GraphicsSettings.defaultRenderPipeline;
            var qualityPipeline = QualitySettings.renderPipeline;
            var originalAntiAliasing = QualitySettings.antiAliasing;
            var originalScene = SceneManager.GetActiveScene();
            var testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(testScene);
            try
            {
                GraphicsSettings.defaultRenderPipeline = null;
                QualitySettings.renderPipeline = null;
                Setup();
                RunCases();
                RunAdditiveShaderCases();
                RunExtendedBridgeCases();
                RunSpriteRendererMaskCases();
            }
            catch (Exception e) { s_Failures++; s_Results.Add("EXCEPTION " + e); }
            finally
            {
                GraphicsSettings.defaultRenderPipeline = originalPipeline;
                QualitySettings.renderPipeline = qualityPipeline;
                QualitySettings.antiAliasing = originalAntiAliasing;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (originalScene.IsValid()) SceneManager.SetActiveScene(originalScene);
                foreach (var obj in s_Resources) if (obj) Object.DestroyImmediate(obj);
                s_Resources.Clear();
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/sprite-mask-validation.txt", s_Results);
                Debug.Log("SpriteMask validation: " + s_Results.Count + " checks, failures=" + s_Failures
                    + "\n" + string.Join("\n", s_Results));
            }
            if (Application.isBatchMode) EditorApplication.Exit(s_Failures == 0 ? 0 : 1);
        }

        private static void RunAdditiveShaderCases()
        {
            var shader = Shader.Find("ShanFlyer/UI Effects/Additive");
            Check(shader && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "Additive shader compiles on current graphics backend");
            var material = Keep(new Material(shader));
            var texture = Keep(new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true));
            texture.SetPixel(0, 0, new Color(.4f, .6f, .8f, .5f)); texture.Apply();
            material.mainTexture = texture;
            material.SetColor("_Color", new Color(1, 1, 1, .5f));
            material.SetInt("_UIVertexColorAlwaysGammaSpace", 0);
            material.SetInt("_StencilComp", (int)CompareFunction.Always);
            var quad = Keep(new Mesh());
            quad.vertices = new[] { new Vector3(-1,-1,0), new Vector3(-1,1,0), new Vector3(1,1,0), new Vector3(1,-1,0) };
            quad.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            var output = Keep(new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            output.Create();
            var readback = Keep(new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true));
            var background = new Color(.1f, .15f, .2f, 0);
            var increment = new Color(.4f, .6f, .8f, 0) * (.5f * .5f);
            void Render(int copies)
            {
                var commands = new CommandBuffer { name = "UI additive equation validation" };
                var oldScreen = Shader.GetGlobalVector("_ScreenParams");
                var oldDepth = Shader.GetGlobalFloat("unity_GUIZTestMode");
                try
                {
                    commands.SetRenderTarget(output);
                    commands.SetViewport(new Rect(0, 0, Size, Size));
                    commands.ClearRenderTarget(true, true, background);
                    commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(-1, 1, -1, 1, -1, 1));
                    commands.SetGlobalVector("_ScreenParams", new Vector4(Size, Size, 1f + 1f / Size, 1f + 1f / Size));
                    commands.SetGlobalFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
                    for (int i = 0; i < copies; ++i) commands.DrawMesh(quad, Matrix4x4.identity, material, 0, 0);
                    commands.SetGlobalVector("_ScreenParams", oldScreen);
                    commands.SetGlobalFloat("unity_GUIZTestMode", oldDepth);
                    Graphics.ExecuteCommandBuffer(commands);
                    var active = RenderTexture.active;
                    try { RenderTexture.active = output; readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); readback.Apply(); }
                    finally { RenderTexture.active = active; }
                }
                finally { commands.Release(); }
            }
            bool Near(Color actual, Color expected) => Mathf.Abs(actual.r - expected.r) < .003f
                && Mathf.Abs(actual.g - expected.g) < .003f && Mathf.Abs(actual.b - expected.b) < .003f;
            Render(1);
            Check(Near(readback.GetPixel(Size / 2, Size / 2), background + increment), "Additive fractional alpha adds to nonblack background");
            Render(2);
            Check(Near(readback.GetPixel(Size / 2, Size / 2), background + increment * 2), "Additive overlapping draws accumulate without attenuating background");
            material.EnableKeyword("UNITY_UI_CLIP_RECT");
            material.SetVector("_ClipRect", new Vector4(-.5f, -1, .5f, 1));
            material.SetFloat("_UIMaskSoftnessX", 0); material.SetFloat("_UIMaskSoftnessY", 0);
            Render(1);
            Check(Near(readback.GetPixel(Size / 2, Size / 2), background + increment)
                && Near(readback.GetPixel(Size - 4, Size / 2), background), "Additive local clip keyword retains inside and removes outside");
            float hardEdge = readback.GetPixel(3 * Size / 4 - 2, Size / 2).r;
            // The test quad spans two local units, rather than a pixel-sized Canvas.
            material.SetFloat("_UIMaskSoftnessX", 12f * 2f / Size);
            Render(1);
            float softEdge = readback.GetPixel(3 * Size / 4 - 2, Size / 2).r;
            Check(softEdge > background.r + .001f && softEdge < hardEdge - .001f, "Additive RectMask soft edge reduces RGB contribution hard=" + hardEdge + " soft=" + softEdge);
            material.DisableKeyword("UNITY_UI_CLIP_RECT");
            material.EnableKeyword("UNITY_UI_ALPHACLIP");
            material.SetColor("_Color", new Color(1, 1, 1, 0));
            Render(1);
            Check(Near(readback.GetPixel(Size / 2, Size / 2), background), "Additive alpha-clip variant preserves background for zero alpha");
            Check(!ShaderUtil.ShaderHasError(shader), "Additive tested shader variants have no compilation errors");
        }

        private static T Keep<T>(T obj) where T : Object { s_Resources.Add(obj); return obj; }
        private static void Check(bool condition, string label)
        {
            s_Results.Add((condition ? "PASS " : "FAIL ") + label);
            if (!condition) s_Failures++;
        }

        private static void Setup()
        {
            s_Camera = new GameObject("Reference camera", typeof(Camera)).GetComponent<Camera>();
            s_Camera.transform.position = new Vector3(0, 0, -10);
            s_Camera.orthographic = true;
            s_Camera.orthographicSize = 2;
            s_Camera.clearFlags = CameraClearFlags.SolidColor;
            s_Camera.backgroundColor = Color.black;
            s_Camera.allowHDR = false;
            s_Camera.allowMSAA = false;
            s_Camera.enabled = false;
            s_Target = Keep(new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32));
            s_Target.Create();
            s_Camera.targetTexture = s_Target;
            var texture = Keep(new Texture2D(32, 32, TextureFormat.RGBA32, false));
            texture.filterMode = FilterMode.Point;
            var colors = new Color[32 * 32];
            for (var y = 0; y < 32; y++)
                for (var x = 0; x < 32; x++)
                    colors[y * 32 + x] = new Color(1, 1, 1, x < 24 ? 1 : 0.25f);
            texture.SetPixels(colors);
            texture.Apply();
            s_Sprite = Keep(Sprite.Create(texture, new Rect(0, 0, 32, 32), new Vector2(.5f, .5f), 32));
            s_Root = new GameObject("Effect");
            s_Particle = new GameObject("Particle", typeof(ParticleSystem)).GetComponent<ParticleSystem>();
            s_Particle.transform.SetParent(s_Root.transform, false);
            s_Particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = s_Particle.main;
            main.playOnAwake = false;
            main.startSpeed = 0;
            main.startLifetime = 1000;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = s_Particle.emission; emission.enabled = false;
            var shape = s_Particle.shape; shape.enabled = false;
            s_Renderer = s_Particle.GetComponent<ParticleSystemRenderer>();
            s_Renderer.maxParticleSize = 10;
            s_Native = Keep(new Material(Shader.Find("Sprites/Default")));
            s_Material = Keep(new Material(Shader.Find("ShanFlyer/UI Effects/Additive")));
            s_Renderer.sharedMaterial = s_Native;
            s_Renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            s_Mask = MakeMask("Mask A", new Vector3(-.25f, 0, 0));
            s_Mask2 = MakeMask("Mask B", new Vector3(.25f, .25f, 0));
            s_Mask2.enabled = false;
            s_Canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
            s_Canvas.renderMode = RenderMode.WorldSpace;
            s_Canvas.worldCamera = s_Camera;
            ((RectTransform)s_Canvas.transform).sizeDelta = new Vector2(4, 4);
            s_Ui = new GameObject("HP", typeof(RectTransform)).AddComponent<UIEffectRenderer>(); s_Ui.unitConversion = global::ShanFlyer.UIEffects.UIEffectRenderer.UnitConversion.Manual;
            s_Ui.transform.SetParent(s_Canvas.transform, false);
            s_Ui.uniformScale = 1;
            s_Ui.scaleMode = UIEffectRenderer.ScaleMode.Hierarchy;
            s_Ui.enabled = false;
            ResetParticle();
        }

        private static SpriteMask MakeMask(string name, Vector3 position)
        {
            var mask = new GameObject(name, typeof(SpriteMask)).GetComponent<SpriteMask>();
            mask.transform.SetParent(s_Root.transform, false);
            mask.transform.localPosition = position;
            mask.sprite = s_Sprite;
            mask.alphaCutoff = .5f;
            return mask;
        }

        private static void ResetParticle()
        {
            s_Particle.SetParticles(new[] { new ParticleSystem.Particle {
                position = Vector3.zero, startSize = 3, startColor = Color.white,
                startLifetime = 1000, remainingLifetime = 1000 } }, 1);
            s_Particle.Pause();
        }

        private static Color32[] Capture(bool hp)
        {
            s_Ui.enabled = false;
            s_Renderer.sharedMaterial = hp ? s_Material : s_Native;
            if (hp)
            {
                s_Ui.enabled = true;
                s_Ui.RefreshSources(new List<ParticleSystem> { s_Particle });
                s_Ui.Pause();
            }
            else s_Renderer.enabled = true;
            ResetParticle();
            return CaptureCurrent(hp);
        }

        private static Color32[] CaptureCurrent(bool hp = true)
        {
            // Camera.Render does not execute the PlayerLoop's SortingGroup update in a batch test.
            SortingGroup.UpdateAllSortingGroups();
            if (hp)
            {
                Canvas.ForceUpdateCanvases();
                typeof(UIEffectScheduler).GetField("s_FrameCount", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, -1);
                typeof(UIEffectScheduler).GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
                Canvas.ForceUpdateCanvases();
            }
            s_Camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = s_Target;
            var readback = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            readback.Apply();
            var pixels = readback.GetPixels32();
            RenderTexture.active = previous;
            Object.DestroyImmediate(readback);
            return pixels;
        }

        private static int Lit(Color32[] pixels)
        {
            var count = 0;
            foreach (var pixel in pixels) if (pixel.r > 32) count++;
            return count;
        }

        private static void Compare(string label)
        {
            var native = Capture(false);
            var hp = Capture(true);
            ComparePixels(native, hp, label);
        }

        private static void ComparePixels(Color32[] native, Color32[] hp, string label)
        {
            var mismatch = 0;
            for (var i = 0; i < native.Length; i++)
                if ((native[i].r > 32) != (hp[i].r > 32)) mismatch++;
            Check(mismatch <= 8, label + " native=" + Lit(native) + " HP=" + Lit(hp) + " mismatch=" + mismatch);
        }

        private static void RunCases()
        {
            Compare("single / Inside");
            s_Renderer.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
            Compare("single / Outside");
            s_Renderer.maskInteraction = SpriteMaskInteraction.None;
            Compare("None preserves original shading");
            Check(Lit(Capture(true)) > 8000, "nonempty GPU output sanity check");
            s_Renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            s_Mask2.enabled = true;
            Compare("overlap is union / Inside");
            s_Renderer.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
            Compare("overlap / Outside");
            s_Mask.isCustomRangeActive = s_Mask2.isCustomRangeActive = true;
            s_Mask.backSortingOrder = -1; s_Mask.frontSortingOrder = 0;
            s_Mask2.backSortingOrder = 0; s_Mask2.frontSortingOrder = 1;
            s_Renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            Compare("overlapping masks / only A in range");
            s_Renderer.sortingOrder = 1;
            Compare("overlapping masks / only B in range");
            s_Renderer.sortingOrder = 0;
            s_Mask.isCustomRangeActive = s_Mask2.isCustomRangeActive = false;
            s_Mask2.enabled = false;
            s_Renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            s_Mask.isCustomRangeActive = true;
            s_Mask.backSortingOrder = -1;
            s_Mask.frontSortingOrder = 1;
            foreach (var order in new[] { -2, -1, 0, 1, 2 })
            {
                s_Renderer.sortingOrder = order;
                Compare("sorting order " + order);
            }
            s_Renderer.sortingOrder = 0;
            s_Mask.isCustomRangeActive = false;
            SortingLayerCases();
            s_Mask.transform.localRotation = Quaternion.Euler(0, 0, 33);
            s_Mask.transform.localScale = new Vector3(-1.3f, .7f, 1);
            Compare("rotated / flipped / nonuniform mask");
            s_Mask.alphaCutoff = .1f;
            Compare("animated alpha cutoff");
            var group = s_Root.AddComponent<SortingGroup>();
            Compare("shared SortingGroup");
            s_Mask.transform.SetParent(null, true);
            Compare("mask outside SortingGroup");
            s_Mask.isCustomRangeActive = true;
            s_Mask.backSortingOrder = -1;
            s_Mask.frontSortingOrder = 1;
            group.sortingOrder = 2;
            Compare("global range tests group order / excluded");
            group.sortingOrder = 0;
            s_Renderer.sortingOrder = 20;
            Compare("global range tests group order / included");
            s_Renderer.sortingOrder = 0;
            s_Mask.isCustomRangeActive = false;
            s_Mask.transform.SetParent(s_Root.transform, true);
            var nested = s_Particle.gameObject.AddComponent<SortingGroup>();
            Compare("nested SortingGroup");
            nested.sortAtRoot = true;
            Compare("nested sortAtRoot skips parent mask");
            Object.DestroyImmediate(nested);
            s_Mask.transform.SetParent(null, true);
            var maskGroup = s_Mask.gameObject.AddComponent<SortingGroup>();
            Compare("sibling mask scope does not escape");
            Object.DestroyImmediate(maskGroup);
            s_Mask.transform.SetParent(s_Root.transform, true);
            group.enabled = false;
            Compare("disabled SortingGroup");
            s_Mask.gameObject.SetActive(false);
            Compare("pooled mask disabled / Inside");
            s_Renderer.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
            Compare("pooled mask disabled / Outside");
            s_Mask.gameObject.SetActive(true);
            Compare("pooled mask enabled");
            UIEffectRenderer.mergeRenderers = true;
            UIEffectRenderer.fastBindingMode = true;
            Compare("merge + fast binding retain masking");
            Check(!s_Ui.GetRendererIfExists(0).isMerged, "masked systems use isolated draw intervals");
            Check(s_Material.GetInt("_StencilComp") == (int)CompareFunction.Always,
                "source material stencil unchanged");
            Check(s_Ui.GetRendererIfExists(0).materialForRendering.shader == s_Material.shader,
                "particle shader identity preserved");
            DynamicCases();
            AtlasAndScaleCases();
            ColorAndCleanupCases();
            SiblingScrollViewCases();
            ParentMaskCases();
            UnsupportedCases();
            SharingCases();
        }

        private static void DynamicCases()
        {
            UIEffectRenderer.staticMeshCache = true;
            UIEffectRenderer.updateRatePercent = 10;
            s_Renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            Capture(true);
            s_Mask.transform.localPosition += new Vector3(.25f, -.125f, 0);
            s_Mask.alphaCutoff = .8f;
            var hp = CaptureCurrent();
            ComparePixels(Capture(false), hp, "paused static mesh / moving mask / bake10");
            Capture(true);
            s_Renderer.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
            hp = CaptureCurrent();
            ComparePixels(Capture(false), hp, "live Inside to Outside without rebind");
            Capture(true);
            s_Mask.enabled = false;
            hp = CaptureCurrent();
            ComparePixels(Capture(false), hp, "live mask removal without rebind");
            s_Mask.enabled = true;
            Capture(true);
            s_Ui.gameObject.SetActive(false);
            s_Ui.gameObject.SetActive(true);
            s_Ui.RefreshSources(new List<ParticleSystem> { s_Particle });
            ResetParticle();
            s_Ui.Pause();
            hp = CaptureCurrent();
            ComparePixels(Capture(false), hp, "HP object pool disable / re-enable");
            UIEffectRenderer.staticMeshCache = false;
            UIEffectRenderer.updateRatePercent = 100;
        }

        private static void ParentMaskCases()
        {
            s_Renderer.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
            var native = Capture(false);
            var parent = new GameObject("UGUI parent mask", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            parent.transform.SetParent(s_Canvas.transform, false);
            ((RectTransform)parent.transform).sizeDelta = new Vector2(1.5f, 3);
            parent.GetComponent<Mask>().showMaskGraphic = false;
            s_Ui.transform.SetParent(parent.transform, false);
            var hp = Capture(true);
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                    if (x < 40 || x >= 88 || y < 16 || y >= 112) native[y * Size + x] = new Color32(0, 0, 0, 255);
            ComparePixels(native, hp, "Outside AND parent UGUI Mask");
            s_Renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            s_Ui.transform.SetParent(s_Canvas.transform, false);
            native = Capture(false);
            s_Ui.transform.SetParent(parent.transform, false);
            hp = Capture(true);
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                    if (x < 40 || x >= 88 || y < 16 || y >= 112) native[y * Size + x] = new Color32(0, 0, 0, 255);
            ComparePixels(native, hp, "Inside AND parent UGUI Mask");
            // Seven parent masks leave exactly bit 7 available. Eight must suppress output.
            var deepest = parent.transform;
            for (var i = 1; i < 8; i++)
            {
                var child = new GameObject("Stencil depth " + (i + 1), typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Image), typeof(Mask));
                child.transform.SetParent(deepest, false);
                ((RectTransform)child.transform).sizeDelta = new Vector2(1.5f, 3);
                child.GetComponent<Mask>().showMaskGraphic = false;
                deepest = child.transform;
                if (i == 6)
                {
                    s_Ui.transform.SetParent(deepest, false);
                    Check(Lit(Capture(true)) > 0, "seven parent masks retain one SpriteMask bit");
                    s_Ui.transform.SetParent(s_Canvas.transform, false);
                }
            }
            s_Ui.transform.SetParent(deepest, false);
            Check(Lit(Capture(true)) == 0, "eight parent masks explicitly suppress unsupported output");
            s_Ui.transform.SetParent(s_Canvas.transform, false);
            Object.DestroyImmediate(parent);
            Compare("stencil exhaustion recovery");
        }

        private static void SiblingScrollViewCases()
        {
            var view = new GameObject("Sibling Scroll View", typeof(RectTransform), typeof(ScrollRect));
            view.transform.SetParent(s_Canvas.transform, false);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(view.transform, false);
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.sizeDelta = new Vector2(.75f, 2);
            viewportRect.localPosition = new Vector3(1.25f, 0, 0);
            viewport.GetComponent<Mask>().showMaskGraphic = false;
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            ((RectTransform)content.transform).sizeDelta = new Vector2(4, 4);
            var toggle = new GameObject("Toggle outside viewport", typeof(RectTransform), typeof(Image), typeof(Toggle));
            toggle.transform.SetParent(content.transform, false);
            ((RectTransform)toggle.transform).sizeDelta = Vector2.one * .5f;
            toggle.transform.position = s_Mask.transform.position;
            toggle.GetComponent<Image>().color = Color.green;
            var scroll = view.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = (RectTransform)content.transform;
            scroll.horizontal = scroll.vertical = false;
            s_Material.color = Color.red;
            s_Renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            var pixels = Capture(true);
            var green = 0;
            foreach (var p in pixels) if (p.g > 32 && p.g > p.r * 2) green++;
            Check(green == 0, "sibling ScrollView clips Toggle overlapping SpriteMask, leaked=" + green);
            Check(s_Mask.forceRenderingOff && s_Mask.enabled, "HP suppresses native mask draw without changing enabled");
            s_Ui.enabled = false;
            Check(!s_Mask.forceRenderingOff, "HP disable restores source SpriteMask rendering");
            s_Root.transform.SetParent(s_Ui.transform, true);
            s_Renderer.maskInteraction = SpriteMaskInteraction.None;
            pixels = Capture(true);
            green = 0;
            foreach (var p in pixels) if (p.g > 32 && p.g > p.r * 2) green++;
            Check(green == 0, "unused owned SpriteMask cannot reveal sibling Toggle when Masking=None");
            s_Mask.isCustomRangeActive = true;
            s_Mask.backSortingOrder = 30;
            s_Mask.frontSortingOrder = 40;
            s_Renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            pixels = CaptureCurrent();
            green = 0;
            foreach (var p in pixels) if (p.g > 32 && p.g > p.r * 2) green++;
            Check(green == 0, "out-of-range owned SpriteMask cannot reveal sibling Toggle");
            s_Mask.isCustomRangeActive = false;
            s_Ui.enabled = false;
            s_Mask.forceRenderingOff = true;
            Capture(true);
            s_Ui.enabled = false;
            Check(s_Mask.forceRenderingOff, "original forceRenderingOff=true survives HP takeover and release");
            s_Mask.forceRenderingOff = false;
            s_Root.transform.SetParent(null, true);
            s_Material.color = Color.white;
            Object.DestroyImmediate(view);
        }

        private static void UnsupportedCases()
        {
            var supported = s_Material;
            s_Material = s_Native;
            Check(Lit(Capture(true)) == 0, "shader without contract fails closed");
            s_Material = supported;
            Compare("shader compatibility recovery");
        }

        private static void SharingCases()
        {
            s_Renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            var expectedInside = Capture(false);
            s_Renderer.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
            var expectedOutside = Capture(false);
            s_Ui.enabled = false;
            s_Renderer.sharedMaterial = s_Material;
            s_Renderer.maskInteraction = SpriteMaskInteraction.None;
            // A template clone must contain its source; earlier cases bind an external source.
            s_Particle.transform.SetParent(s_Ui.transform, true);
            s_Particle.transform.SetAsFirstSibling();
            var replica = Object.Instantiate(s_Ui, s_Canvas.transform);
            replica.name = "Replica";
            replica.transform.SetParent(s_Canvas.transform, false);
            replica.uniformScale = 1;
            replica.scaleMode = UIEffectRenderer.ScaleMode.Hierarchy;
            var ps = replica.GetComponentInChildren<ParticleSystem>(true);
            ps.GetComponent<ParticleSystemRenderer>().maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = s_Material;
            replica.RefreshSources(new List<ParticleSystem> { ps });
            replica.simulationRole = UIEffectRenderer.SimulationRole.Consumer;
            replica.enabled = true;
            s_Ui.simulationRole = UIEffectRenderer.SimulationRole.SimulationOnly;
            var pixels = Capture(true);
            Check(s_Ui.sharingGroup == replica.sharingGroup, "template clone resolves to the producer sharing group");
            ComparePixels(expectedInside, pixels, "shared replica Inside pixels use its own mask state");
            Check(!s_Ui.GetRendererIfExists(0).isMerged && !replica.GetRendererIfExists(0).isMerged,
                "masked replica forces consistent unmerged shared group layout");
            Check(replica.GetRendererIfExists(0).materialForRendering != s_Ui.GetRendererIfExists(0).materialForRendering,
                "shared geometry retains per-output stencil material");
            ps.GetComponent<ParticleSystemRenderer>().maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
            ComparePixels(expectedOutside, CaptureCurrent(), "shared replica live Outside transition");
            UIEffectRenderer.mergeRenderers = false;
            CaptureCurrent();
            UIEffectRenderer.mergeRenderers = true;
            ComparePixels(expectedOutside, CaptureCurrent(), "live merge toggle preserves shared mask layout");
            s_Ui.enabled = false;
            Check(s_Mask.forceRenderingOff, "shared mask stays native-suppressed until last HP consumer releases");
            replica.enabled = false;
            Check(!s_Mask.forceRenderingOff, "last HP consumer restores native shared mask");
            Object.DestroyImmediate(replica.gameObject);
            s_Ui.simulationRole = UIEffectRenderer.SimulationRole.Independent;
            s_Particle.transform.SetParent(s_Root.transform, true);
        }

        private static void AtlasAndScaleCases()
        {
            s_Renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            var atlasSprite = Keep(Sprite.Create(s_Sprite.texture, new Rect(16, 0, 16, 32),
                new Vector2(.2f, .7f), 24));
            s_Mask.sprite = atlasSprite;
            Compare("atlas subrect UV / custom pivot");
            Capture(true);
            s_Mask.sprite = s_Sprite;
            var hp = CaptureCurrent();
            ComparePixels(Capture(false), hp, "live sprite animation updates mesh and UV");
            s_Root.transform.localScale = Vector3.one * 1.5f;
            var expected = Capture(false);
            s_Root.transform.localScale = Vector3.one;
            s_Ui.uniformScale = 1.5f;
            ComparePixels(expected, Capture(true), "HP effect scale applies to mask and particles together");
            s_Ui.uniformScale = 1;
        }

        private static void SortingLayerCases()
        {
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = settings.FindProperty("m_SortingLayers");
            var originalCount = layers.arraySize;
            for (var i = 0; i < 2; i++)
            {
                layers.InsertArrayElementAtIndex(layers.arraySize);
                var layer = layers.GetArrayElementAtIndex(layers.arraySize - 1);
                layer.FindPropertyRelative("name").stringValue = "HP Mask Test " + i;
                // Deliberately reverse the IDs: comparisons must use layer VALUE, not ID.
                layer.FindPropertyRelative("uniqueID").intValue = i == 0 ? 900002 : 900001;
            }
            settings.ApplyModifiedPropertiesWithoutUndo();
            s_Mask.isCustomRangeActive = true;
            s_Mask.backSortingLayerID = 0;
            s_Mask.backSortingOrder = 0;
            s_Mask.frontSortingLayerID = 900002;
            s_Mask.frontSortingOrder = 1;
            s_Renderer.sortingLayerID = 900002;
            Compare("Sorting Layer value included (IDs intentionally reversed)");
            s_Renderer.sortingLayerID = 900001;
            Compare("Sorting Layer value excluded (IDs intentionally reversed)");
            s_Renderer.sortingLayerID = 0;
            s_Mask.frontSortingLayerID = 0;
            s_Mask.isCustomRangeActive = false;
            layers.arraySize = originalCount;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ColorAndCleanupCases()
        {
            s_Renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            var coverage = Capture(false);
            s_Material.color = new Color(.75f, .2f, .1f, .6f);
            s_Renderer.maskInteraction = SpriteMaskInteraction.None;
            var original = Capture(true);
            s_Renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            var masked = Capture(true);
            var mismatches = 0;
            for (var i = 0; i < masked.Length; i++)
            {
                var expected = coverage[i].r > 32 ? original[i] : new Color32(0, 0, 0, 255);
                if (masked[i].r != expected.r || masked[i].g != expected.g || masked[i].b != expected.b) mismatches++;
            }
            Check(mismatches == 0, "original tint / alpha / additive shading preserved pixel-for-pixel");
            var probeMaterial = Keep(new Material(s_Material));
            probeMaterial.color = Color.green;
            probeMaterial.SetInt("_Stencil", 128);
            probeMaterial.SetInt("_StencilComp", (int)CompareFunction.Equal);
            probeMaterial.SetInt("_StencilReadMask", 128);
            probeMaterial.SetInt("_StencilWriteMask", 0);
            var probe = new GameObject("Stencil leak probe", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            probe.transform.SetParent(s_Canvas.transform, false);
            probe.rectTransform.sizeDelta = Vector2.one * 4;
            probe.material = probeMaterial;
            var after = CaptureCurrent();
            mismatches = 0;
            for (var i = 0; i < after.Length; i++) if (after[i].g != masked[i].g) mismatches++;
            Check(mismatches == 0, "post-draw cleanup leaves no stencil bit for later UI");
            Object.DestroyImmediate(probe.gameObject);
            s_Material.color = Color.white;
        }
    }
}
