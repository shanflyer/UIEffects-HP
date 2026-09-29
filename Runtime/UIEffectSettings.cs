using System;
using UnityEngine;

namespace ShanFlyer.UIEffects
{
    [CreateAssetMenu(menuName = "UI Effects HP/Settings")]
    public sealed class UIEffectSettings : ScriptableObject
    {
        public enum VertexColorPolicy { MatchCanvas, Preserve }
        [SerializeField] private VertexColorPolicy vertexColors;
        [SerializeField] private bool showGeneratedObjects;
        [SerializeField, Range(1, 100)] private int updateRatePercent = 100;
        [SerializeField] private bool staggerUpdates = true;
        [SerializeField] private bool mergeParticleOutputs = true;
        [SerializeField, Range(0, 2)] private int invisibleWorkMode = 1;
        [SerializeField] private bool cachePausedParticles = true;
        [SerializeField] private bool skipBindingChecks;
        [SerializeField] private bool allowParticleSharing = true;
        private static UIEffectSettings loaded;

        public static UIEffectSettings Current
        {
            get
            {
                if (loaded) return loaded;
                loaded = Resources.Load<UIEffectSettings>("UIEffectSettings");
                if (loaded) return loaded;
                loaded = CreateInstance<UIEffectSettings>();
                loaded.hideFlags = HideFlags.HideAndDontSave;
                return loaded;
            }
        }

        public VertexColorPolicy ColorPolicy
        {
            get => vertexColors;
            set
            {
                if ((uint)value > 1) throw new ArgumentOutOfRangeException(nameof(value));
                vertexColors = value;
            }
        }
        public bool ShowGeneratedObjects { get => showGeneratedObjects; set => showGeneratedObjects = value; }

        public int UpdateRatePercent => Mathf.Clamp(updateRatePercent, 1, 100);
        public bool StaggerUpdates => staggerUpdates;
        public bool MergeParticleOutputs => mergeParticleOutputs;
        public int InvisibleWorkMode => Mathf.Clamp(invisibleWorkMode, 0, 2);
        public bool CachePausedParticles => cachePausedParticles;
        public bool SkipBindingChecks => skipBindingChecks;
        public bool AllowParticleSharing => allowParticleSharing;

        internal static bool ConvertVertexColors(Canvas target) => target
            && Current.ColorPolicy == VertexColorPolicy.MatchCanvas
            && QualitySettings.activeColorSpace == ColorSpace.Linear && !target.vertexColorAlwaysGammaSpace;

        internal static HideFlags GeneratedObjectFlags
        {
            get
            {
                var flags = HideFlags.DontSave | HideFlags.NotEditable;
                return Current.ShowGeneratedObjects ? flags : flags | HideFlags.HideInHierarchy | HideFlags.HideInInspector;
            }
        }

        private void OnValidate()
        {
            if ((uint)vertexColors > 1) vertexColors = VertexColorPolicy.MatchCanvas;
            updateRatePercent = Mathf.Clamp(updateRatePercent, 1, 100);
            invisibleWorkMode = Mathf.Clamp(invisibleWorkMode, 0, 2);
        }
    }
}
