using System;
using UnityEngine;
using ShanFlyer.UIEffects.Internal;

namespace ShanFlyer.UIEffects
{
    // Owns only the fallback view. Never modifies or destroys a Canvas camera.
    internal sealed class EffectBakeView : IDisposable
    {
        private const string NodeName = "[generated] UI effect bake view";
        private Camera synthetic;
        private Camera current;
        private Matrix4x4 previousView, previousProjection;
        private Rect previousPixels;
        private bool initialized;
        internal uint Revision { get; private set; }

        internal bool Owns(Transform node) => synthetic && synthetic.transform == node;

        internal Camera Resolve(UIEffectRenderer owner, Canvas root)
        {
            Refresh(owner, root);
            return current;
        }

        internal bool Refresh(UIEffectRenderer owner, Canvas root)
        {
            var next = Select(owner, root);
            var view = next ? next.worldToCameraMatrix : Matrix4x4.zero;
            var projection = next ? next.projectionMatrix : Matrix4x4.zero;
            var pixels = next ? next.pixelRect : default;
            if (initialized && current == next && previousView == view
                && previousProjection == projection && previousPixels == pixels) return false;
            current = next;
            previousView = view;
            previousProjection = projection;
            previousPixels = pixels;
            initialized = true;
            unchecked { Revision++; }
            return true;
        }

        internal Matrix4x4 SharingView(UIEffectRenderer owner)
        {
            if (!current) return Matrix4x4.zero;
            var view = current.worldToCameraMatrix * Matrix4x4.TRS(owner.transform.position,
                owner.transform.rotation, Vector3.one);
            bool positionDependent = !current.orthographic;
            foreach (var source in owner.particles)
                if (source && source.TryGetComponent<ParticleSystemRenderer>(out var renderer)
                    && (renderer.alignment == ParticleSystemRenderSpace.Facing
                        || renderer.sortMode == ParticleSystemSortMode.Distance)) positionDependent = true;
            // View-aligned orthographic geometry does not depend on screen placement.
            if (!positionDependent) view.SetColumn(3, new Vector4(0, 0, 0, 1));
            return current.projectionMatrix * view;
        }

        private Camera Select(UIEffectRenderer owner, Canvas root)
        {
            if (!root) { ReleaseSynthetic(); return null; }
            var supplied = root.worldCamera;
            if (root.renderMode != RenderMode.ScreenSpaceOverlay && supplied)
            {
                ReleaseSynthetic();
                return supplied;
            }
            var rectTransform = (RectTransform)root.transform;
            var rect = rectTransform.rect;
            float width = Mathf.Abs(rect.width) * rectTransform.TransformVector(Vector3.right).magnitude;
            float height = Mathf.Abs(rect.height) * rectTransform.TransformVector(Vector3.up).magnitude;
            float size = height * .5f;
            float aspect = width / height;
            float depth = Mathf.Max(width, height, 1);
            var center = rectTransform.TransformPoint(rect.center);
            if (!float.IsFinite(size) || size <= 0 || !float.IsFinite(aspect) || aspect <= 0
                || !float.IsFinite(depth * 4) || !EffectScale.IsFinite(center)) return null;
            if (!synthetic)
            {
                // Recover the non-serialized handle after an editor assembly reload.
                for (int slot = owner.transform.childCount - 1; slot >= 0; --slot)
                {
                    var node = owner.transform.GetChild(slot);
                    if (node.name == NodeName && node.TryGetComponent(out synthetic)) break;
                }
                if (!synthetic)
                {
                    var node = new GameObject(NodeName);
                    node.SetActive(false);
                    node.transform.SetParent(owner.transform, false);
                    synthetic = node.AddComponent<Camera>();
                }
                synthetic.enabled = false;
                synthetic.gameObject.SetActive(false);
                synthetic.orthographic = true;
                synthetic.nearClipPlane = .01f;
                synthetic.cullingMask = 0;
            }
            synthetic.gameObject.hideFlags = UIEffectSettings.GeneratedObjectFlags;
            if (synthetic.orthographicSize != size) synthetic.orthographicSize = size;
            if (synthetic.aspect != aspect) synthetic.aspect = aspect;
            float far = depth * 4 + 1;
            if (synthetic.farClipPlane != far) synthetic.farClipPlane = far;
            if (synthetic.targetDisplay != root.targetDisplay) synthetic.targetDisplay = root.targetDisplay;
            var position = center - rectTransform.forward * depth;
            if (synthetic.transform.position != position || synthetic.transform.rotation != rectTransform.rotation)
                synthetic.transform.SetPositionAndRotation(position, rectTransform.rotation);
            return synthetic;
        }

        public void Dispose()
        {
            ReleaseSynthetic();
            current = null;
            initialized = false;
        }

        private void ReleaseSynthetic()
        {
            var previous = synthetic;
            synthetic = null;
            if (previous)
            {
                previous.name = "[retired] UI effect bake view";
                EngineObjects.Destroy(previous.gameObject);
            }
        }
    }
}
