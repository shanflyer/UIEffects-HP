using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShanFlyer.UIEffects
{
    // Persist prefab identities so Player builds use the same grouping as the Editor.
    internal sealed class UIEffectSharingIdentity : AssetPostprocessor, IPreprocessBuildWithReport, IProcessSceneWithReport
    {
        public int callbackOrder => 0;
        private static bool s_Updating;

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (s_Updating) return;
            try
            {
                s_Updating = true;
                foreach (var path in imported) StampPrefab(path);
            }
            finally { s_Updating = false; }
        }

        private static void StampPrefab(string path)
        {
            if (!path.StartsWith("Assets/") || !path.EndsWith(".prefab")) return;
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!root) return;
            var changed = false;
            foreach (var effect in root.GetComponentsInChildren<UIEffectRenderer>(true))
                if (effect.RefreshEditorSharingIdentity()) { EditorUtility.SetDirty(effect); changed = true; }
            if (changed) UnityEditor.PrefabUtility.SavePrefabAsset(root);
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
                StampPrefab(AssetDatabase.GUIDToAssetPath(guid));
        }

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var effect in root.GetComponentsInChildren<UIEffectRenderer>(true))
                    effect.RefreshEditorSharingIdentity();
        }
    }
}
