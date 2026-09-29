using System;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif
namespace ShanFlyer.UIEffects.Internal
{
    // Install after UGUI's registry. No reflection into Canvas or TMP internals.
    internal static class CanvasFramePump
    {
        public static event Action AfterLayout;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            CanvasUpdateRegistry.IsRebuildingLayout();
            Canvas.willRenderCanvases -= Dispatch;
            Canvas.willRenderCanvases += Dispatch;
        }
#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void InstallEditor()
        {
            Install();
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }
        private static void OnPlayMode(PlayModeStateChange state) => Install();
#endif
        private static void Dispatch() => AfterLayout?.Invoke();
    }
}
