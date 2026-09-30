using UnityEngine;

namespace ShanFlyer.UIEffects
{
    internal sealed class CanvasScaleState
    {
        private RectTransform held;
        private Vector3 authoredScale, drivenScale;
        private DrivenRectTransformTracker tracker;
        internal Vector3 ParentScale { get; private set; } = Vector3.one;
        internal Vector3 CanvasScale { get; private set; } = Vector3.one;

        internal void Capture(UIEffectRenderer owner, Canvas root)
        {
            CanvasScale = root ? EffectScale.Reciprocal(root.transform.localScale) : Vector3.one;
            ParentScale = owner.transform.parent ? owner.transform.parent.lossyScale : Vector3.one;
            Apply(owner, ParentScale, owner.normalizesRoot);
        }

        internal void Apply(UIEffectRenderer owner, Vector3 parentScale, bool normalize)
        {
            var target = owner.rectTransform;
            if (!normalize) { Release(); return; }
            if (held != target)
            {
                Release();
                held = target;
                authoredScale = target.localScale;
                tracker.Add(owner, target, DrivenTransformProperties.Scale);
            }
            var required = EffectScale.Reciprocal(parentScale);
            if (target.localScale != required) target.localScale = required;
            drivenScale = required;
        }
        internal void Release()
        {
            tracker.Clear();
            // A Canvas mode change and an authored scale edit can precede the next
            // editor repaint. Restore only the value we still own, not that new edit.
            if (held && held.localScale == drivenScale) held.localScale = authoredScale;
            held = null;
        }
    }
}
