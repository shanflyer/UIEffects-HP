using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UI;
using UnityEngine;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("ShanFlyer.UIEffects.Editor.Tests")]

namespace ShanFlyer.UIEffects
{
    [CustomEditor(typeof(UIEffectRenderer)), CanEditMultipleObjects]
    internal sealed class UIEffectRendererEditor : GraphicEditor
    {
        private readonly List<Renderer> sources = new List<Renderer>();
        private readonly bool[] expanded = { true, true, true, true, true };
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("UI Effects HP", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Supported: Screen Space - Overlay, or Screen Space - Camera with a dedicated UI camera that starts with cleared depth and renders after the scene. Do not share it with scene rendering or later passes/cameras that need its depth. Camera settings are not changed automatically. World Space keeps native rendering.", MessageType.Info);
            if (targets.Length == 1)
            {
                var current = (UIEffectRenderer)target;
                if (current.canvas && current.canvas.rootCanvas.renderMode == RenderMode.WorldSpace)
                    EditorGUILayout.HelpBox("World Space is not supported. UI outputs and depth resets are disabled; source renderers keep native rendering.", MessageType.Warning);
                else if (current.canvas && current.canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceCamera
                    && !current.canvas.rootCanvas.worldCamera)
                    EditorGUILayout.HelpBox("Assign a dedicated UI Render Camera to the root Canvas. Sources keep native rendering until a camera is assigned.", MessageType.Warning);
            }
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
                EditorGUILayout.HelpBox("Screen-space UI converts world units using the reference camera, or the Canvas camera. Without a reference camera, Overlay uses Canvas Reference Pixels Per Unit. Perspective sizing is measured at the effect root. Entering Automatic resets Size multiplier to 1.", MessageType.Info);
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
            Draw("m_RenderMeshes", "Include meshes"); Draw("m_RenderSkinnedMeshes", "Include skinned meshes");
            EditorGUILayout.HelpBox("Consecutive mesh and skinned-mesh outputs share depth automatically. Ordinary UI, other effect types, Canvas and mask boundaries end a group. Depth resets before and after each group preserve color and stencil. Keep the mesh material's depth test/write settings; depth-writing surfaces occlude each other within the group, regardless of sibling order. No group ID is required.", MessageType.Info);
            Draw("m_RenderSprites", "Include sprites"); Draw("m_RenderLines", "Include lines and trails");
            Draw("particleSources", "Particle sources");
            if (targets.Length == 1)
            {
                var effect = (UIEffectRenderer)target;
                CollectOwnedSources(effect, sources);
                EditorGUILayout.HelpBox("The lists below are collected from this object and its children. Nested UI Effect Renderers own their own sources. Click Collect sources after adding or repairing a source.", MessageType.Info);
                DrawSources<MeshRenderer>("Mesh sources", 0, "m_RenderMeshes");
                DrawSources<SkinnedMeshRenderer>("Skinned mesh sources", 1, "m_RenderSkinnedMeshes");
                DrawSources<SpriteRenderer>("Sprite sources", 2, "m_RenderSprites");
                DrawSources<LineRenderer>("Line sources", 3, "m_RenderLines");
                DrawSources<TrailRenderer>("Trail sources", 4, "m_RenderLines");
            }
            else EditorGUILayout.HelpBox("Select one UI Effect Renderer to inspect its collected source lists.", MessageType.Info);
            if (GUILayout.Button("Collect sources"))
            {
                ApplyConfiguration();
                Each(p => p.RefreshSources());
                serializedObject.Update();
            }
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
            ApplyConfiguration();
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Play particles")) Each(p => p.Play());
                if (GUILayout.Button("Pause particles")) Each(p => p.Pause());
                if (GUILayout.Button("Stop particles")) Each(p => p.Stop());
            }
        }
        internal static void CollectOwnedSources(UIEffectRenderer owner, List<Renderer> result)
        {
            owner.GetComponentsInChildren(true, result);
            for (int i = result.Count - 1; i >= 0; --i)
            {
                var source = result[i];
                if (!source || source is ParticleSystemRenderer
                    || source.GetComponentInParent<UIEffectRenderer>(true) != owner
                    || (source.hideFlags & HideFlags.DontSave) != 0 || source.CompareTag("EditorOnly")) result.RemoveAt(i);
            }
        }
        private void DrawSources<T>(string label, int index, string inclusionField) where T : Renderer
        {
            int count = 0;
            foreach (var source in sources) if (source is T) ++count;
            expanded[index] = EditorGUILayout.Foldout(expanded[index], label + " (" + count + ")", true);
            if (!expanded[index]) return;
            ++EditorGUI.indentLevel;
            if (count == 0) EditorGUILayout.LabelField("None");
            else if (typeof(T) == typeof(LineRenderer) || typeof(T) == typeof(TrailRenderer))
                EditorGUILayout.HelpBox("Automatic keeps world-space path positions and widths. Local-space lines use unit conversion. Keep Size multiplier at 1 to follow recorded world positions.", MessageType.Info);
            bool included = serializedObject.FindProperty(inclusionField).boolValue;
            foreach (var source in sources)
            {
                if (!(source is T)) continue;
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField(source, typeof(T), true);
                var issue = CanvasEffectOutput.BridgeIssue(source);
                if (!included) EditorGUILayout.LabelField("Excluded by the Include setting.");
                else if (issue != null) EditorGUILayout.HelpBox(issue, MessageType.Warning);
                else if (!source.enabled || !source.gameObject.activeInHierarchy) EditorGUILayout.LabelField("Inactive source.");
                else if (source is SpriteRenderer sprite && !sprite.sprite) EditorGUILayout.HelpBox("Assign a Sprite to this source.", MessageType.Warning);
                else if (source is LineRenderer line && line.positionCount < 2) EditorGUILayout.LabelField("Needs at least two line positions.");
                else if (source is TrailRenderer trail && trail.positionCount < 2) EditorGUILayout.LabelField("Waiting for trail points; move the source while playing.");
            }
            --EditorGUI.indentLevel;
        }
        private void ApplyConfiguration()
        {
            if (!serializedObject.ApplyModifiedProperties()) return;
            foreach (var item in targets)
            {
                var effect = (UIEffectRenderer)item;
                effect.RefreshSharingIdentity(); effect.MarkBindingDirty(); effect.SetMaterialDirty();
                EditorUtility.SetDirty(effect);
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
