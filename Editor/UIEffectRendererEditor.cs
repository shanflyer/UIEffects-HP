using UnityEditor;
using UnityEditor.UI;
using UnityEngine;

namespace ShanFlyer.UIEffects
{
    [CustomEditor(typeof(UIEffectRenderer)), CanEditMultipleObjects]
    internal sealed class UIEffectRendererEditor : GraphicEditor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("UI Effects HP", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("General rendering", EditorStyles.boldLabel);
            var conversion = serializedObject.FindProperty("unitConversionValue");
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(conversion, new GUIContent("Unit conversion"));
            if (EditorGUI.EndChangeCheck() && conversion.enumValueIndex == (int)UIEffectRenderer.UnitConversion.Automatic)
                serializedObject.FindProperty("renderScaleValue").vector3Value = Vector3.one;
            bool automatic = !conversion.hasMultipleDifferentValues
                && conversion.enumValueIndex == (int)UIEffectRenderer.UnitConversion.Automatic;
            Draw("renderScaleValue", automatic ? "Size multiplier" : "Effect scale");
            if (automatic)
            {
                Draw("referenceCameraValue", "Reference camera");
                EditorGUILayout.HelpBox("Screen-space UI converts world units using the reference camera, or the Canvas camera. Without a camera it uses Canvas Reference Pixels Per Unit. Perspective sizing is measured at the effect root. World-space UI keeps native world units. Entering Automatic resets Size multiplier to 1.", MessageType.Info);
                if (targets.Length == 1)
                {
                    var effect = (UIEffectRenderer)target;
                    float units = EffectUnitScale.Calculate(effect.canvas, effect.referenceCamera, effect.transform.position);
                    EditorGUILayout.LabelField("UI units / world unit", units.ToString("0.###"));
                }
            }
            else Draw("scaleModeValue", "Canvas scaling");
            Draw("m_Maskable", "UGUI maskable");
            Draw("m_SortBySourceOrder", "Source sorting");
            Draw("propertyBindings", "Material property synchronization");
            EditorGUILayout.HelpBox("UI Mask requires stencil properties and a pass that uses them. RectMask2D also requires clip-rect support. Apply a UI-compatible material to every source slot, including particle trails.", MessageType.Info);
            if (GUILayout.Button("Generate Particle Shaders...")) ParticleShaderGenerator.Open();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Renderer sources", EditorStyles.boldLabel);
            Draw("m_RenderSkinnedMeshes", "Skinned mesh sources"); Draw("m_RenderSprites", "Sprite sources");
            Draw("m_RenderMeshes", "Mesh sources"); Draw("m_RenderLines", "Line and trail sources");
            Draw("particleSources", "Particle sources");
            if (GUILayout.Button("Collect sources")) Each(p => p.RefreshSources());
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Particle simulation and sharing", EditorStyles.boldLabel);
            Draw("originModeValue", "Particle zoom origin");
            if (serializedObject.FindProperty("originModeValue").enumValueIndex == (int)UIEffectRenderer.OriginMode.Effect)
                EditorGUILayout.HelpBox("Effect also scales emitter positions around the effect root. Choose Emitter to keep particles anchored to each emitter's Transform when moving it.", MessageType.Info);
            Draw("simulationSpeedValue", "Particle time scale");
            Draw("simulationRoleValue", "Simulation role");
            if (!UIEffectRenderer.meshSharingEnabled)
                EditorGUILayout.HelpBox("Particle sharing is disabled in Project Settings / UI Effects HP. Instances simulate independently; SimulationOnly remains hidden.", MessageType.Info);
            EditorGUILayout.HelpBox("Matching templates can share particle simulation and geometry. Other renderer sources update independently. SimulationOnly hides all outputs of this instance.", MessageType.Info);
            if (serializedObject.ApplyModifiedProperties()) foreach (var item in targets)
            {
                var effect = (UIEffectRenderer)item;
                effect.RefreshSharingIdentity(); effect.MarkBindingDirty(); effect.SetMaterialDirty();
                EditorUtility.SetDirty(effect);
            }
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Play particles")) Each(p => p.Play());
                if (GUILayout.Button("Pause particles")) Each(p => p.Pause());
                if (GUILayout.Button("Stop particles")) Each(p => p.Stop());
            }
        }
        private void Draw(string field, string label)
        {
            var property = serializedObject.FindProperty(field);
            if (property != null) EditorGUILayout.PropertyField(property, new GUIContent(label), true);
        }
        private void Each(System.Action<UIEffectRenderer> action)
        {
            foreach (var item in targets) { Undo.RecordObject(item, "Edit UI effect"); action((UIEffectRenderer)item); }
            SceneView.RepaintAll();
        }
    }
}
