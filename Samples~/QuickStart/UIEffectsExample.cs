using ShanFlyer.UIEffects;
using UnityEngine;
using UnityEngine.UI;

public sealed class UIEffectsExample : MonoBehaviour
{
    private GameObject canvasRoot;
    private Material effectMaterial;
    private void Start()
    {
        canvasRoot = new GameObject("UI Effects sample", typeof(Canvas), typeof(CanvasScaler));
        canvasRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        effectMaterial = new Material(Shader.Find("UI/Default"));
        var root = new GameObject("Shared effect", typeof(RectTransform));
        root.transform.SetParent(canvasRoot.transform, false);
        var particles = new GameObject("Particles", typeof(ParticleSystem));
        particles.transform.SetParent(root.transform, false);
        particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = effectMaterial;
        var effect = root.AddComponent<UIEffectRenderer>();
        effect.uniformScale = .3f; effect.simulationRole = UIEffectRenderer.SimulationRole.Automatic;
        effect.RefreshSources();
        var copy = Instantiate(root, canvasRoot.transform);
        ((RectTransform)root.transform).anchoredPosition = new Vector2(-120, 0);
        ((RectTransform)copy.transform).anchoredPosition = new Vector2(120, 0);
    }
    private void OnDestroy()
    {
        if (canvasRoot) Destroy(canvasRoot);
        if (effectMaterial) Destroy(effectMaterial);
    }
}
