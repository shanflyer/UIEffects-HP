using UnityEditor;
using UnityEngine;

namespace ShanFlyer.UIEffects
{
    internal static class UIEffectSettingsProvider
    {
        private static Vector2 scroll;
        private static readonly string[] CullModes = { "Off", "Skip Rendering", "Skip Rendering and Pause Simulation" };

        [SettingsProvider]
        private static SettingsProvider Create() => new SettingsProvider("Project/UI Effects HP", SettingsScope.Project)
        {
            keywords = new[] { "particle", "performance", "update", "percentage", "stagger", "merge", "cache", "sharing" },
            guiHandler = _ => DrawSettings()
        };

        private static void DrawSettings()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            var asset = UIEffectSettings.Current;
            using (var data = new SerializedObject(asset))
            {
                data.Update();
                EditorGUILayout.LabelField("Performance", EditorStyles.boldLabel);
                var rate = data.FindProperty("updateRatePercent");
                rate.intValue = EditorGUILayout.IntSlider(new GUIContent("Update Rate (%)",
                    "Percentage of actual frames used for updates. 100% = every frame; 50% = every other frame on average. Lower values reduce smoothness. Initial and invalidated outputs can update immediately."), rate.intValue, 1, 100);
                Note("100%: every frame. 50%: every other frame on average.");
                Field(data, "staggerUpdates", "Stagger Updates",
                    "Spread reduced-rate updates across frames. Shared particles remain synchronized. Only applies below 100%.");
                Field(data, "mergeParticleOutputs", "Merge Particle Outputs",
                    "Merge compatible particle outputs to reduce UI submissions. Each particle system still bakes separately. Disable for custom MPB uniforms not declared in shader Properties.");
                var cull = data.FindProperty("invisibleWorkMode");
                cull.intValue = EditorGUILayout.Popup(new GUIContent("Hidden Output Handling",
                    "Skip Rendering keeps particle simulation running. Pause Simulation also pauses transparent-hidden particles. Fully clipped outputs are checked periodically for visibility recovery."), Mathf.Clamp(cull.intValue, 0, 2), CullModes);
                Field(data, "cachePausedParticles", "Cache Paused Particles",
                    "Reuse unchanged paused particle meshes. Call MarkGeometryDirty() after editing paused particles or modules through code.");
                Field(data, "allowParticleSharing", "Allow Particle Sharing",
                    "Allow compatible instances to share particle simulation and geometry. Set the component's Simulation Role to Automatic to participate. Disabling this makes instances simulate independently; SimulationOnly remains hidden.");
                Field(data, "skipBindingChecks", "Fast Binding",
                    "Skip routine binding checks. Call MarkBindingDirty() after changing materials, textures or trails through code.");
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Rendering", EditorStyles.boldLabel);
                Field(data, "vertexColors", "Vertex Colors",
                    "Match Canvas applies Canvas color-space handling. Preserve keeps source vertex colors unchanged.");
                Field(data, "showGeneratedObjects", "Show Generated Objects",
                    "Show internal output objects and the bake camera in the Hierarchy.");
                if (GUILayout.Button("Generate Particle Shaders...")) ParticleShaderGenerator.Open();
                if (data.ApplyModifiedProperties())
                {
                    EditorUtility.SetDirty(asset);
                    if (AssetDatabase.Contains(asset)) AssetDatabase.SaveAssetIfDirty(asset);
                }
            }
            if (UIEffectRenderer.hasPerformanceOverrides)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("Code overrides are active. Clear them to use the project settings.", MessageType.Warning);
                Note($"Active: {UIEffectRenderer.updateRatePercent}% | Stagger: {UIEffectRenderer.staggerBaking} | Merge: {UIEffectRenderer.mergeRenderers} | Hidden: {CullModes[UIEffectRenderer.earlyCull]} | Cache: {UIEffectRenderer.staticMeshCache} | Sharing: {UIEffectRenderer.meshSharingEnabled} | Fast Binding: {UIEffectRenderer.fastBindingMode}");
                if (GUILayout.Button("Clear Code Overrides")) UIEffectRenderer.ResetPerformanceOverrides();
            }
            if (!AssetDatabase.Contains(asset))
            {
                EditorGUILayout.HelpBox("Save these settings to persist them and include them in builds.", MessageType.Info);
                if (GUILayout.Button("Save Project Settings")) Save(asset);
            }
            EditorGUILayout.EndScrollView();
        }

        private static void Field(SerializedObject data, string property, string label, string description)
        {
            EditorGUILayout.PropertyField(data.FindProperty(property), new GUIContent(label, description));
        }
        private static void Note(string text) => EditorGUILayout.LabelField(text, EditorStyles.wordWrappedMiniLabel);

        private static void Save(UIEffectSettings asset)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            const string path = "Assets/Resources/UIEffectSettings.asset";
            if (AssetDatabase.LoadMainAssetAtPath(path))
            {
                Debug.LogError("Settings path is already occupied: " + path);
                return;
            }
            asset.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
        }
    }
}
