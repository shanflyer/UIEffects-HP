using UnityEditor;
using UnityEngine;
namespace ShanFlyer.UIEffects
{
    internal static class UIEffectMenu
    {
        [MenuItem("GameObject/UI (Canvas)/UI Effects HP/Effect renderer", false, 2019)]
        private static void CreateRenderer(MenuCommand command) => CreateRoot(command);

        [MenuItem("GameObject/UI (Canvas)/UI Effects HP/Particle example", false, 2020)]
        private static void CreateParticleExample(MenuCommand command)
        {
            var root = CreateRoot(command);
            var emitter = new GameObject("Particles", typeof(ParticleSystem));
            emitter.transform.SetParent(root.transform, false);
            var material = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.shanflyer.ui-effects/Shaders/UIAdditive.mat");
            emitter.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            root.GetComponent<UIEffectRenderer>().RefreshSources();
        }

        private static GameObject CreateRoot(MenuCommand command)
        {
            var parent = command.context as GameObject;
            if (!parent || !parent.GetComponentInParent<Canvas>())
            {
                parent = new GameObject("Effects Canvas", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
                parent.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                Undo.RegisterCreatedObjectUndo(parent, "Create effects canvas");
            }
            var root = new GameObject("UI Effect", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(root, "Create UI effect");
            GameObjectUtility.SetParentAndAlign(root, parent);
            root.AddComponent<UIEffectRenderer>();
            Selection.activeGameObject = root;
            return root;
        }
    }
}
