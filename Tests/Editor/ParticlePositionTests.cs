using NUnit.Framework;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShanFlyer.UIEffects.Editor.Tests
{
    public class ParticlePositionTests
    {
        [TestCase(RenderMode.ScreenSpaceOverlay, ParticleSystemSimulationSpace.Local, false)]
        [TestCase(RenderMode.ScreenSpaceOverlay, ParticleSystemSimulationSpace.World, false)]
        [TestCase(RenderMode.ScreenSpaceOverlay, ParticleSystemSimulationSpace.Custom, false)]
        [TestCase(RenderMode.ScreenSpaceCamera, ParticleSystemSimulationSpace.Local, false)]
        [TestCase(RenderMode.ScreenSpaceCamera, ParticleSystemSimulationSpace.World, false)]
        [TestCase(RenderMode.ScreenSpaceCamera, ParticleSystemSimulationSpace.Custom, false)]
        [TestCase(RenderMode.ScreenSpaceOverlay, ParticleSystemSimulationSpace.Local, true)]
        [TestCase(RenderMode.ScreenSpaceOverlay, ParticleSystemSimulationSpace.World, true)]
        [TestCase(RenderMode.ScreenSpaceOverlay, ParticleSystemSimulationSpace.Custom, true)]
        [TestCase(RenderMode.ScreenSpaceCamera, ParticleSystemSimulationSpace.Local, true)]
        [TestCase(RenderMode.ScreenSpaceCamera, ParticleSystemSimulationSpace.World, true)]
        [TestCase(RenderMode.ScreenSpaceCamera, ParticleSystemSimulationSpace.Custom, true)]
        public void MovingEmitterKeepsNewParticlesAtItsTransform(RenderMode mode, ParticleSystemSimulationSpace space, bool merged)
        {
            var root = new GameObject("position canvas", typeof(Canvas));
            var cameraObject = new GameObject("position camera", typeof(Camera));
            var frame = new GameObject("custom frame");
            var material = new Material(Shader.Find("UI/Default"));
            var bakeView = new EffectBakeView();
            bool previousMerge = UIEffectRenderer.mergeRenderers;
            int previousCull = UIEffectRenderer.earlyCull;
            try
            {
                UIEffectRenderer.mergeRenderers = merged;
                UIEffectRenderer.earlyCull = 0;
                var camera = cameraObject.GetComponent<Camera>();
                camera.transform.position = new Vector3(0, 0, -10);
                camera.orthographic = true; camera.orthographicSize = 5;
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = mode; canvas.worldCamera = camera; canvas.planeDistance = 10;
                Canvas.ForceUpdateCanvases();
                frame.transform.position = new Vector3(7, -11, 0);
                frame.transform.rotation = Quaternion.Euler(0, 0, 23);
                var host = new GameObject("effect", typeof(RectTransform));
                host.transform.SetParent(root.transform, false);
                var node = new GameObject("emitter", typeof(ParticleSystem));
                node.transform.SetParent(host.transform, false);
                var ps = node.GetComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main; main.simulationSpace = space;
                main.customSimulationSpace = frame.transform;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                node.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
                if (merged)
                {
                    var other = new GameObject("empty emitter", typeof(ParticleSystem));
                    other.transform.SetParent(host.transform, false);
                    other.GetComponent<ParticleSystem>().Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    other.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
                }
                var effect = host.AddComponent<UIEffectRenderer>(); effect.unitConversion = global::ShanFlyer.UIEffects.UIEffectRenderer.UnitConversion.Manual;
                Assert.AreEqual(UIEffectRenderer.OriginMode.Emitter, effect.originMode,
                    "New effects must not scale emitter placement by default.");
                effect.uniformScale = 10;
                effect.RefreshSources(); effect.PrepareForUpdate();
                Assert.AreEqual(merged ? 1 : 0, effect.mergedRendererCount);
                var output = effect.GetRendererIfExists(0);
                var particles = new ParticleSystem.Particle[1];
                for (int move = 0; move < 3; ++move)
                {
                    host.transform.localPosition = new Vector3(40 + move * 35, -20 + move * 7, 0);
                    node.transform.localPosition = new Vector3(2 + move * 3, -1 + move, 0);
                    effect.PrepareForUpdate();
                    var emitterPosition = node.transform.position;
                    var simulationPosition = space == ParticleSystemSimulationSpace.World ? emitterPosition
                        : space == ParticleSystemSimulationSpace.Custom ? frame.transform.InverseTransformPoint(emitterPosition)
                        : Vector3.zero;
                    ps.Play(); ps.Pause();
                    ps.SetParticles(new[] { new ParticleSystem.Particle { position = simulationPosition,
                        startSize = .1f, startColor = Color.white, startLifetime = 10, remainingLifetime = 10 } });
                    foreach (var origin in new[] { UIEffectRenderer.OriginMode.Emitter, UIEffectRenderer.OriginMode.Effect })
                    {
                        effect.originMode = origin;
                        effect.MarkGeometryDirty();
                        output.UpdateMesh(bakeView.Resolve(effect, canvas));
                        var outputMesh = (Mesh)typeof(CanvasEffectOutput).GetField("_outputMesh",
                            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(output);
                        Assert.Greater(outputMesh.vertexCount, 0);
                        outputMesh.RecalculateBounds();
                        var actual = output.transform.TransformPoint(outputMesh.bounds.center);
                        var expected = emitterPosition;
                        if (origin == UIEffectRenderer.OriginMode.Effect)
                            expected = host.transform.position + Vector3.Scale(emitterPosition - host.transform.position,
                                ParticleCoordinates.Scale(effect, ps));
                        var canvasTransform = root.transform;
                        Assert.That(Vector3.Distance(canvasTransform.InverseTransformPoint(expected),
                            canvasTransform.InverseTransformPoint(actual)), Is.LessThan(.05f),
                            "mode=" + mode + " space=" + space + " origin=" + origin + " move=" + move);
                        Assert.IsTrue(ParticleCoordinates.TryTargetInSimulation(effect, ps, actual, out var target));
                        Assert.That(Vector3.Distance(simulationPosition, target), Is.LessThan(.01f));
                        ps.GetParticles(particles);
                        Assert.AreEqual(simulationPosition, particles[0].position, "Rendering must not rewrite native particles.");
                    }
                }
            }
            finally
            {
                bakeView.Dispose();
                Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(frame);
                Object.DestroyImmediate(material);
                UIEffectRenderer.mergeRenderers = previousMerge; UIEffectRenderer.earlyCull = previousCull;
            }
        }
    }
}
