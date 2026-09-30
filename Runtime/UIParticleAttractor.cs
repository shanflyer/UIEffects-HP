using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using ShanFlyer.UIEffects.Internal;
namespace ShanFlyer.UIEffects
{
    [ExecuteAlways]
    public sealed class UIParticleAttractor : MonoBehaviour
    {
        public enum AttractionPath { Linear, Eased, Curved }
        public enum ClockMode { Scaled, Unscaled }
        [SerializeField] private List<ParticleSystem> sources = new List<ParticleSystem>();
        [SerializeField, Min(.1f)] private float arrivalRadius = 1;
        [SerializeField, Range(0, .95f)] private float delayFraction;
        [SerializeField, Min(0)] private float speed = 60;
        [SerializeField] private AttractionPath motion;
        [SerializeField] private ClockMode clock;
        [SerializeField, Range(0, 1)] private float curvature = 1;
        [SerializeField] private UnityEvent arrivedEvent = new UnityEvent();
        private readonly ParticleReadback readback = new ParticleReadback();
        private readonly List<ParticleSystem> snapshot = new List<ParticleSystem>();
        private readonly HashSet<ParticleSystem> visited = new HashSet<ParticleSystem>();
        private bool processing;
        public float ArrivalRadius { get => arrivalRadius; set => arrivalRadius = FiniteRange(value, .1f, float.MaxValue); }
        public float StartDelay { get => delayFraction; set => delayFraction = FiniteRange(value, 0, .95f); }
        public float Speed { get => speed; set => speed = FiniteRange(value, 0, float.MaxValue); }
        public AttractionPath Path { get => motion; set { ValidateMode((int)value, 2); motion = value; } }
        public ClockMode Clock { get => clock; set { ValidateMode((int)value, 1); clock = value; } }
        public float Curvature { get => curvature; set => curvature = FiniteRange(value, 0, 1); }
        public UnityEvent Arrived { get => arrivedEvent; set => arrivedEvent = value; }
        public IReadOnlyList<ParticleSystem> Sources => sources;
        public void AddSource(ParticleSystem source) { if (source && !sources.Contains(source)) sources.Add(source); }
        public void RemoveSource(ParticleSystem source) => sources.Remove(source);
        private void OnEnable() { OnValidate(); UIEffectScheduler.Register(this); }
        private void OnDisable() => UIEffectScheduler.Unregister(this);
        private static float FiniteRange(float value, float minimum, float maximum)
        {
            if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            return Mathf.Clamp(value, minimum, maximum);
        }
        private static void ValidateMode(int value, int maximum)
        {
            if (value < 0 || value > maximum) throw new ArgumentOutOfRangeException(nameof(value));
        }
        private void OnValidate()
        {
            arrivalRadius = float.IsFinite(arrivalRadius) ? Mathf.Max(.1f, arrivalRadius) : 1;
            delayFraction = float.IsFinite(delayFraction) ? Mathf.Clamp(delayFraction, 0, .95f) : 0;
            speed = float.IsFinite(speed) ? Mathf.Max(0, speed) : 60;
            curvature = float.IsFinite(curvature) ? Mathf.Clamp01(curvature) : 1;
            if ((uint)motion > 2) motion = AttractionPath.Linear;
            if ((uint)clock > 1) clock = ClockMode.Scaled;
            sources ??= new List<ParticleSystem>();
        }
        internal void Attract()
        {
            if (processing || !isActiveAndEnabled) return;
            float delta = clock == ClockMode.Unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
            if (!float.IsFinite(delta) || delta <= 0) return;
            processing = true;
            try
            {
                snapshot.Clear(); snapshot.AddRange(sources);
                visited.Clear();
                int arrived = 0;
                Vector3 worldTarget = transform.position;
                foreach (var source in snapshot)
                {
                    if (!source || !source.gameObject.activeInHierarchy || !visited.Add(source)) continue;
                    var effect = source.GetComponentInParent<UIEffectRenderer>(true);
                    if (effect && !effect.particles.Contains(source)) effect = null;
                    // Unsupported canvases use native coordinates and playback, not
                    // the UI bridge's cached scale or particle sharing ownership.
                    if (effect && !effect.supportsCanvasRendering) effect = null;
                    if (effect && effect.isActiveAndEnabled)
                    {
                        if (effect.isPaused) continue;
                        if (effect.useMeshSharing && UIEffectScheduler.GetPrimary(effect.sharingGroup) != effect) continue;
                    }
                    if (!ParticleCoordinates.TryTargetInSimulation(effect, source, worldTarget, out var target)) continue;
                    int count = readback.Read(source);
                    bool changed = false;
                    for (int index = 0; index < count; ++index)
                    {
                        var result = ParticleAttractionStep.Apply(ref readback.Values[index], target,
                            arrivalRadius, delayFraction, speed, delta, motion, curvature);
                        changed |= result != ParticleAttractionStep.Result.Unchanged;
                        if (result == ParticleAttractionStep.Result.Arrived) ++arrived;
                    }
                    if (!changed) continue;
                    source.SetParticles(readback.Values, count);
                    if (effect && effect.isActiveAndEnabled) effect.InvalidateAnimatedParticleContent();
                }
                // User callbacks run after every source write; reentry cannot reuse the live buffers.
                for (int index = 0; index < arrived && this && isActiveAndEnabled; ++index)
                    try { arrivedEvent?.Invoke(); } catch (Exception error) { Debug.LogException(error, this); }
            }
            finally { processing = false; snapshot.Clear(); visited.Clear(); }
        }


    }
}
