#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ShanFlyer.UIEffects.Internal;
namespace ShanFlyer.UIEffects
{
    [InitializeOnLoad]
    internal static class EditorPlayback
    {
        private static readonly HashSet<UIEffectRenderer> Playing = new HashSet<UIEffectRenderer>();
        private static readonly List<UIEffectRenderer> Snapshot = new List<UIEffectRenderer>();
        private static double previous;
        static EditorPlayback()
        {
            EditorApplication.update += Advance;
            EditorApplication.playModeStateChanged += _ => Playing.Clear();
        }
        internal static void Set(UIEffectRenderer effect, bool playing)
        {
            if (Application.isPlaying) return;
            if (playing) { if (Playing.Count == 0) previous = EditorApplication.timeSinceStartup; Playing.Add(effect); }
            else Playing.Remove(effect);
        }
        private static void Advance()
        {
            if (Application.isPlaying || Playing.Count == 0) return;
            var now = EditorApplication.timeSinceStartup;
            float delta = Mathf.Min(.1f, (float)(now - previous)); previous = now;
            Snapshot.Clear(); Snapshot.AddRange(Playing);
            foreach (var effect in Snapshot)
            {
                if (!effect || !effect.isActiveAndEnabled) { Playing.Remove(effect); continue; }
                effect.sourceTopology.Rebuild(effect.particles);
                foreach (var source in effect.particles)
                    if (source && !effect.sourceTopology.ParentOf(source)) source.Simulate(delta * Mathf.Max(0, effect.simulationSpeed), false, false, false);
                effect.MarkGeometryDirty();
            }
            EditorApplication.QueuePlayerLoopUpdate(); SceneView.RepaintAll();
        }
    }
}
#endif
