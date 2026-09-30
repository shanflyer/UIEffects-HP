using ShanFlyer.UIEffects.Internal;
using UnityEngine;
using UnityEngine.UI;

namespace ShanFlyer.UIEffects
{
    // Deliberately not MaskableGraphic: resetting depth must not be clipped by a
    // RectMask2D or change a Mask's stencil state. It never writes visible color.
    [ExecuteAlways, AddComponentMenu("")]
    internal sealed class CanvasDepthResetGraphic : Graphic
    {
        private Mesh _mesh;
        private Material _resetMaterial;
        private readonly Vector3[] _corners = new Vector3[4];
        private readonly Vector3[] _vertices = new Vector3[4];
        private bool _geometryValid;
        private bool _submitted;
        private static readonly Vector2[] Uv = { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
        private static readonly int[] Indices = { 0, 1, 2, 2, 3, 0 };
        private static readonly Color32[] Colors = {
            new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255),
            new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };

        public override bool raycastTarget => false;

        internal static CanvasDepthResetGraphic Create(Transform parent, int layer)
        {
            var node = new GameObject("[generated] UI depth reset", typeof(RectTransform), typeof(CanvasRenderer), typeof(LayoutElement));
            node.SetActive(false);
            node.hideFlags = UIEffectSettings.GeneratedObjectFlags;
            node.layer = layer;
            node.GetComponent<LayoutElement>().ignoreLayout = true;
            node.transform.SetParent(parent, false);
            var reset = node.AddComponent<CanvasDepthResetGraphic>();
            node.SetActive(true);
            return reset;
        }

        internal void Place(CanvasEffectOutput output, bool before)
        {
            var target = output.transform;
            if (transform.parent != target.parent) transform.SetParent(target.parent, false);
            gameObject.layer = output.gameObject.layer;
            int targetIndex = target.GetSiblingIndex();
            int current = transform.GetSiblingIndex();
            int desired = before ? targetIndex - (current < targetIndex ? 1 : 0)
                : targetIndex + (current < targetIndex ? 0 : 1);
            if (current != desired) transform.SetSiblingIndex(desired);
        }

        internal void Submit(Canvas root, Bounds groupWorldBounds)
        {
            if (!root || !root.isActiveAndEnabled) { StopDrawing(); return; }
            var shader = DepthGroupPlanner.ResetShader;
            if (!shader) { StopDrawing(); return; }
            if (!_resetMaterial)
            {
                _resetMaterial = new Material(shader) { name = "UI depth reset", hideFlags = HideFlags.HideAndDontSave };
                _submitted = false;
            }
            if (!_mesh)
            {
                _mesh = new Mesh { name = "UI depth reset bounds", hideFlags = HideFlags.HideAndDontSave };
                _geometryValid = false;
            }

            // Include both the Canvas and the group's true 3D extent. In a Scene
            // camera, a mesh may be visible while the flat Canvas plane is offscreen;
            // culling its depth reset in that case leaves stale depth in front of it.
            ((RectTransform)root.transform).GetWorldCorners(_corners);
            var inverse = transform.worldToLocalMatrix;
            bool changed = !_geometryValid;
            for (int i = 0; i < 4; ++i)
            {
                var vertex = inverse.MultiplyPoint3x4(_corners[i]);
                if (!EffectScale.IsFinite(vertex)) { _geometryValid = false; StopDrawing(); return; }
                changed |= _vertices[i] != vertex;
                _vertices[i] = vertex;
            }
            if (changed)
            {
                _mesh.Clear();
                _mesh.vertices = _vertices;
                _mesh.uv = Uv;
                _mesh.colors32 = Colors;
                _mesh.triangles = Indices;
                _mesh.RecalculateBounds();
                _geometryValid = true;
            }
            var requiredBounds = new Bounds(_vertices[0], Vector3.zero);
            for (int i = 1; i < _vertices.Length; ++i) requiredBounds.Encapsulate(_vertices[i]);
            requiredBounds.Encapsulate(EffectScale.TransformBounds(groupWorldBounds, inverse));
            bool boundsChanged = _mesh.bounds != requiredBounds;
            if (boundsChanged) _mesh.bounds = requiredBounds;
            if (!enabled) enabled = true;
            canvasRenderer.cull = false;
            canvasRenderer.cullTransparentMesh = false;
            canvasRenderer.DisableRectClipping();
            if (changed || boundsChanged || !_submitted || canvasRenderer.materialCount != 1 || canvasRenderer.GetMaterial(0) != _resetMaterial)
            {
                canvasRenderer.SetMesh(_mesh);
                canvasRenderer.materialCount = 1;
                canvasRenderer.SetMaterial(_resetMaterial, 0);
                _submitted = true;
            }
        }

        internal void StopDrawing()
        {
            if (canvasRenderer) canvasRenderer.Clear();
            _submitted = false;
            if (enabled) enabled = false;
        }

        protected override void OnEnable() { _submitted = false; base.OnEnable(); }
        protected override void OnDisable() { _submitted = false; base.OnDisable(); }
        // Only the depth planner submits; Graphic must not replace the custom mesh.
        protected override void UpdateGeometry() { }
        protected override void UpdateMaterial() { }
        protected override void OnDestroy()
        {
            EngineObjects.Destroy(_mesh);
            EngineObjects.Destroy(_resetMaterial);
            base.OnDestroy();
        }

        internal static void Release(ref CanvasDepthResetGraphic node)
        {
            if (!node) { node = null; return; }
            var retired = node;
            node = null;
            retired.StopDrawing();
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // Sibling destruction is illegal during a parent's OnDisable.
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (retired) Object.DestroyImmediate(retired.gameObject);
                };
                return;
            }
#endif
            Object.Destroy(retired.gameObject);
        }
    }
}
