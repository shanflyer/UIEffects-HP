using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShanFlyer.UIEffects.Editor.Tests
{
    public class BridgePositionTests
    {
        public enum SourceKind { Mesh, SkinnedMesh, Sprite, LocalLine }
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [TestCase(RenderMode.ScreenSpaceOverlay, SourceKind.Mesh)]
        [TestCase(RenderMode.ScreenSpaceOverlay, SourceKind.SkinnedMesh)]
        [TestCase(RenderMode.ScreenSpaceOverlay, SourceKind.Sprite)]
        [TestCase(RenderMode.ScreenSpaceOverlay, SourceKind.LocalLine)]
        [TestCase(RenderMode.ScreenSpaceCamera, SourceKind.Mesh)]
        [TestCase(RenderMode.ScreenSpaceCamera, SourceKind.SkinnedMesh)]
        [TestCase(RenderMode.ScreenSpaceCamera, SourceKind.Sprite)]
        [TestCase(RenderMode.ScreenSpaceCamera, SourceKind.LocalLine)]
        public void SourceTranslationIsNotMultipliedByEffectScale(RenderMode mode, SourceKind kind)
        {
            var root = new GameObject("bridge canvas", typeof(Canvas));
            var viewObject = new GameObject("bridge view", typeof(Camera));
            var material = new Material(Shader.Find("UI/Default"));
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(-1,-1), new Vector3(-1,1), new Vector3(1,1), new Vector3(1,-1) },
                triangles = new[] { 0,1,2, 2,3,0 },
                uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right }
            };
            mesh.RecalculateBounds();
            var texture = new Texture2D(16,16);
            var sprite = UnityEngine.Sprite.Create(texture, new Rect(0,0,16,16), Vector2.one * .5f, 8);
            var bakeView = new EffectBakeView();
            int previousCull = UIEffectRenderer.earlyCull;
            try
            {
                UIEffectRenderer.earlyCull = 0;
                var camera = viewObject.GetComponent<Camera>();
                camera.transform.position = new Vector3(0,0,-10);
                camera.orthographic = true; camera.orthographicSize = 5;
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = mode; canvas.worldCamera = camera; canvas.planeDistance = 10;
                Canvas.ForceUpdateCanvases();
                var panel = new GameObject("scaled panel", typeof(RectTransform));
                panel.transform.SetParent(root.transform, false);
                panel.transform.localScale = new Vector3(1.5f,.75f,1);
                panel.transform.localRotation = Quaternion.Euler(0,0,25);
                var host = new GameObject("effect", typeof(RectTransform));
                host.transform.SetParent(panel.transform, false);
                var node = new GameObject("source"); node.transform.SetParent(host.transform, false);
                Renderer source;
                if (kind == SourceKind.Mesh)
                {
                    node.AddComponent<MeshFilter>().sharedMesh = mesh;
                    source = node.AddComponent<MeshRenderer>();
                }
                else if (kind == SourceKind.SkinnedMesh)
                {
                    var skin = node.AddComponent<SkinnedMeshRenderer>();
                    var bone = new GameObject("bone").transform; bone.SetParent(node.transform, false);
                    mesh.bindposes = new[] { Matrix4x4.identity };
                    var weights = new BoneWeight[mesh.vertexCount];
                    for (int i = 0; i < weights.Length; ++i) weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1 };
                    mesh.boneWeights = weights;
                    skin.sharedMesh = mesh; skin.bones = new[] { bone }; skin.rootBone = bone;
                    source = skin;
                }
                else if (kind == SourceKind.Sprite)
                {
                    var renderer = node.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
                    renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                    var maskObject = new GameObject("mask", typeof(SpriteMask));
                    maskObject.transform.SetParent(node.transform, false);
                    maskObject.GetComponent<SpriteMask>().sprite = sprite;
                    source = renderer;
                }
                else
                {
                    var line = node.AddComponent<LineRenderer>(); line.useWorldSpace = false;
                    line.alignment = LineAlignment.TransformZ; line.widthMultiplier = .1f;
                    line.positionCount = 2; line.SetPositions(new[] { Vector3.left, Vector3.right });
                    source = line;
                }
                source.sharedMaterial = material;
                var effect = host.AddComponent<UIEffectRenderer>(); effect.unitConversion = global::ShanFlyer.UIEffects.UIEffectRenderer.UnitConversion.Manual; effect.uniformScale = 10;
                effect.RefreshSources();
                foreach (var scaling in new[] { UIEffectRenderer.ScaleMode.NormalizeRoot,
                    UIEffectRenderer.ScaleMode.Hierarchy, UIEffectRenderer.ScaleMode.Canvas })
                {
                    effect.scaleMode = scaling;
                    for (int move = 0; move < 3; ++move)
                    {
                        host.transform.localPosition = new Vector3(30 + move * 11, -10 + move * 7, 0);
                        node.transform.localPosition = new Vector3(3 + move * 5, -2 - move, 0);
                        node.transform.localRotation = Quaternion.Euler(0,0,move * 17);
                        node.transform.localScale = new Vector3(1.2f,.7f,1);
                        SpriteMaskResolver.BeginFrame(); effect.PrepareForUpdate();
                        var output = effect.GetRendererIfExists(0);
                        typeof(CanvasEffectOutput).GetField("_lastBakeFrame", Private).SetValue(output, -1);
                        output.UpdateMesh(bakeView.Resolve(effect, canvas));
                        var baked = (Mesh)typeof(CanvasEffectOutput).GetField("_bridgeOutput", Private).GetValue(output);
                        Assert.IsNotNull(baked); Assert.Greater(baked.vertexCount, 0);
                        var expected = root.transform.InverseTransformPoint(node.transform.position);
                        var actual = root.transform.InverseTransformPoint(output.transform.TransformPoint(baked.bounds.center));
                        Assert.That(Vector3.Distance(expected, actual), Is.LessThan(.05f),
                            "mode=" + mode + " source=" + kind + " scaling=" + scaling + " move=" + move);
                        if (kind == SourceKind.Sprite)
                        {
                            int writers = 0;
                            foreach (var writer in effect.GetComponentsInChildren<CanvasSpriteMaskGraphic>())
                            {
                                if ((bool)typeof(CanvasSpriteMaskGraphic).GetField("_fullscreen", Private).GetValue(writer)) continue;
                                var maskMesh = (Mesh)typeof(CanvasSpriteMaskGraphic).GetField("_mesh", Private).GetValue(writer);
                                Assert.IsNotNull(maskMesh);
                                var maskPosition = root.transform.InverseTransformPoint(writer.transform.TransformPoint(maskMesh.bounds.center));
                                Assert.That(Vector3.Distance(expected, maskPosition), Is.LessThan(.05f), "SpriteMask must use the same pivot as its sprite.");
                                ++writers;
                            }
                            Assert.Greater(writers, 0);
                        }
                    }
                }
            }
            finally
            {
                bakeView.Dispose(); Object.DestroyImmediate(root); Object.DestroyImmediate(viewObject);
                Object.DestroyImmediate(material); Object.DestroyImmediate(mesh); Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture);
                UIEffectRenderer.earlyCull = previousCull;
            }
        }
    }
}