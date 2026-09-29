using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShanFlyer.UIEffects
{
    public partial class UIEffectRenderer
    {
        // Serialized policy values are validated before scheduling or binding outputs.
        public enum ScaleMode { Hierarchy, Canvas, NormalizeRoot }
        public enum SimulationRole { Independent, Automatic, Producer, SimulationOnly, Consumer }
        public enum OriginMode { Effect, Emitter }
        public enum UnitConversion { Manual, Automatic }

        [SerializeField, Tooltip("Automatic converts world units to UI units. Manual keeps the authored scale and Canvas scaling policy.")]
        private UnitConversion unitConversionValue = UnitConversion.Automatic;
        [SerializeField, Tooltip("Optional reference for world-to-screen sizing. Otherwise uses the Canvas camera. Without a camera, uses Canvas Reference Pixels Per Unit.")]
        private Camera referenceCameraValue;
        [SerializeField, HideInInspector] private int unitConversionVersion;
        private float _automaticUnitScale = 1;
        [SerializeField, Tooltip("Size multiplier after unit conversion. Negative axes mirror the output; zero axes hide it.")]
        private Vector3 renderScaleValue = Vector3.one;
        [SerializeField, Tooltip("How the effect compensates Canvas and parent scaling.")]
        private ScaleMode scaleModeValue = ScaleMode.NormalizeRoot;
        [SerializeField, Tooltip("Emitter keeps each emitter at its Transform position. Effect also scales emitter offsets around the effect root.")]
        private OriginMode originModeValue = OriginMode.Emitter;
        [SerializeField, Min(0)] private float simulationSpeedValue = 1;
        [SerializeField] private List<ParticleSystem> particleSources = new List<ParticleSystem>();
        [SerializeField, Tooltip("Independent owns its simulation; Automatic elects a producer from matching templates.")]
        private SimulationRole simulationRoleValue;
        [SerializeField]
        [Tooltip("Transfer only the selected Renderer property overrides to the UI material.")]
        internal MaterialPropertyBinding[] propertyBindings = Array.Empty<MaterialPropertyBinding>();

        private bool _configurationPending = true;

        public UnitConversion unitConversion
        {
            get => unitConversionValue;
            set
            {
                CheckMode((int)value, 1);
                if (!SetConfiguration(ref unitConversionValue, value)) return;
                if (value == UnitConversion.Automatic) renderScaleValue = Vector3.one;
                _scaleState.Release();
            }
        }
        public Camera referenceCamera
        {
            get => referenceCameraValue;
            set => SetConfiguration(ref referenceCameraValue, value);
        }
        public float resolvedUnitScale => unitConversion == UnitConversion.Automatic ? _automaticUnitScale : 1;
        internal bool normalizesRoot => unitConversion == UnitConversion.Automatic
            ? canvas && canvas.rootCanvas.renderMode != RenderMode.WorldSpace
            : scaleMode == ScaleMode.NormalizeRoot;

        public float uniformScale { get => renderScaleValue.x; set => renderScale = Vector3.one * value; }
        public Vector3 renderScale
        {
            get => renderScaleValue;
            set
            {
                if (!EffectScale.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), "Effect scale must be finite.");
                SetConfiguration(ref renderScaleValue, value);
            }
        }
        public ScaleMode scaleMode
        {
            get => scaleModeValue;
            set
            {
                CheckMode((int)value, 2);
                if (!SetConfiguration(ref scaleModeValue, value)) return;
                if (value != ScaleMode.NormalizeRoot) _scaleState.Release();
            }
        }
        public OriginMode originMode
        {
            get => originModeValue;
            set { CheckMode((int)value, 1); SetConfiguration(ref originModeValue, value); }
        }
        public SimulationRole simulationRole
        {
            get => simulationRoleValue;
            set { CheckMode((int)value, 4); SetConfiguration(ref simulationRoleValue, value, true); }
        }
        public float simulationSpeed
        {
            get => simulationSpeedValue;
            set
            {
                CheckFinite(value);
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Simulation time cannot run backwards.");
                SetConfiguration(ref simulationSpeedValue, value);
            }
        }
        public Vector3 calculatedScale => unitConversion == UnitConversion.Automatic
            ? (normalizesRoot ? renderScaleValue * _automaticUnitScale
                : Vector3.Scale(renderScaleValue, EffectScale.Reciprocal(parentScale)))
            : scaleMode == ScaleMode.NormalizeRoot ? renderScaleValue
                : Vector3.Scale(Vector3.Scale(renderScaleValue, canvasScale), transform.localScale);
        public List<ParticleSystem> particles => particleSources;

        internal bool hasMaterialPropertyBindings => propertyBindings != null && propertyBindings.Length > 0;
        internal bool useMeshSharing => meshSharingEnabled && simulationRole != SimulationRole.Independent;
        internal bool isPrimary => simulationRole == SimulationRole.Producer || simulationRole == SimulationRole.SimulationOnly;
        internal bool canSimulate => !useMeshSharing || simulationRole != SimulationRole.Consumer;
        internal bool canRender => simulationRole != SimulationRole.SimulationOnly;

        internal int sharingGroup => _groupId;

        private static void CheckFinite(float value)
        {
            if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), "Configuration values must be finite.");
        }
        private static void CheckMode(int value, int maximum)
        {
            if (value < 0 || value > maximum) throw new ArgumentOutOfRangeException(nameof(value));
        }
        private bool SetConfiguration<T>(ref T field, T value, bool layout = false)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            QueueConfigurationRefresh(layout);
            return true;
        }
        private void QueueConfigurationRefresh(bool layout)
        {
            _configurationPending = true;
            _rebuildRenderers |= layout;
            InvalidateRendererCaches();
        }
        private void NormalizeConfiguration()
        {
            // This path also runs during deserialization: do not access native objects here.
            if ((uint)unitConversionValue > 1) unitConversionValue = UnitConversion.Automatic;
            unitConversionVersion = 1;
            if (!EffectScale.IsFinite(renderScaleValue)) renderScaleValue = Vector3.one
                * (unitConversion == UnitConversion.Manual ? 10 : 1);
            simulationSpeedValue = float.IsFinite(simulationSpeedValue) ? Math.Max(0, simulationSpeedValue) : 1;
            if ((uint)scaleModeValue > 2) scaleModeValue = ScaleMode.NormalizeRoot;
            if ((uint)originModeValue > 1) originModeValue = OriginMode.Emitter;
            if ((uint)simulationRoleValue > 4) simulationRoleValue = SimulationRole.Independent;
            particleSources ??= new List<ParticleSystem>();
            propertyBindings ??= Array.Empty<MaterialPropertyBinding>();
        }
        void ISerializationCallbackReceiver.OnBeforeSerialize() { unitConversionVersion = 1; }
        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            // Old scenes/prefabs already contain the full authored multiplier.
            // Preserve it rather than multiplying it by an automatic conversion again.
            if (unitConversionVersion == 0) unitConversionValue = UnitConversion.Manual;
            NormalizeConfiguration();
            _configurationPending = true;
        }
#if UNITY_EDITOR
        protected override void OnValidate()
        {
            NormalizeConfiguration();
            _configurationPending = true;
            _rebuildRenderers = true;
            base.OnValidate();
        }
#endif
    }
}
