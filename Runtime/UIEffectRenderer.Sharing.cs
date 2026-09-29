using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using ShanFlyer.UIEffects.Internal;

namespace ShanFlyer.UIEffects
{
    public partial class UIEffectRenderer
    {
        // Serialized identity is copied by Instantiate. Independently authored effects get distinct identities.
        [SerializeField, HideInInspector] private string m_AutomaticSharingKey = Guid.NewGuid().ToString("N");
        private string _automaticGroupKey;
        private string _resolvedTemplateKey;
        private string _sharingLayout;
        private readonly StringBuilder _sharingLayoutBuilder = new StringBuilder(256);
        private readonly List<ParticleSystemVertexStream> _sharingStreams = new List<ParticleSystemVertexStream>();
        private readonly List<Material> _sharingMaterials = new List<Material>();
        private Mesh[] _sharingMeshes = Array.Empty<Mesh>();

        private sealed class AutomaticGroup
        {
            public int id;
            public int users;
        }
        private static readonly Dictionary<string, AutomaticGroup> s_AutomaticGroups = new Dictionary<string, AutomaticGroup>();
        private static int s_NextAutomaticGroup = -1;

        private void ResolveAutomaticSharingGroup()
        {
            if (!isActiveAndEnabled || !useMeshSharing) { ReleaseAutomaticSharingGroup(); _groupId = 0; return; }
            if (string.IsNullOrEmpty(m_AutomaticSharingKey)) m_AutomaticSharingKey = Guid.NewGuid().ToString("N");
            var key = m_AutomaticSharingKey + ":" + _sharingLayout;
            if (_automaticGroupKey == key) return;
            ReleaseAutomaticSharingGroup();
            if (!s_AutomaticGroups.TryGetValue(key, out var group))
            {
                group = new AutomaticGroup { id = s_NextAutomaticGroup-- };
                s_AutomaticGroups.Add(key, group);
            }
            group.users++;
            _automaticGroupKey = key;
            _resolvedTemplateKey = m_AutomaticSharingKey;
            _groupId = group.id;
        }

        private void ReleaseAutomaticSharingGroup()
        {
            if (_automaticGroupKey == null) return;
            if (s_AutomaticGroups.TryGetValue(_automaticGroupKey, out var group) && --group.users == 0)
                s_AutomaticGroups.Remove(_automaticGroupKey);
            _automaticGroupKey = null;
        }

        private void RefreshAutomaticSharingLayout()
        {
            if (!useMeshSharing) { ReleaseAutomaticSharingGroup(); _groupId = 0; return; }
#if UNITY_EDITOR
            RefreshEditorSharingIdentity();
#endif
            var b = _sharingLayoutBuilder;
            b.Clear();
            b.Append(particles.Count).Append('|').Append((int)originMode).Append('|');
            AppendSharingVector(b, calculatedScale);
            AppendSharingVector(b, parentScale);
            _bakeView.Resolve(this, canvas ? RootCanvas(canvas) : null);
            var view = _bakeView.SharingView(this);
            for (var element = 0; element < 16; element++) AppendSharingFloat(b, view[element]);
            for (var i = 0; i < particles.Count; i++)
            {
                var ps = particles[i];
                if (!ps || !ps.TryGetComponent<ParticleSystemRenderer>(out var r)) { b.Append("missing;"); continue; }
                // Ordered paths prevent a subset/reorder from silently changing shared slots.
                AppendSharingPath(b, ps.transform);
                var relative = transform.worldToLocalMatrix * ps.transform.localToWorldMatrix;
                for (var element = 0; element < 16; element++) AppendSharingFloat(b, relative[element]);
                AppendSharingVector(b, r.pivot);
                AppendSharingVector(b, r.flip);
                AppendSharingFloat(b, r.lengthScale);
                AppendSharingFloat(b, r.velocityScale);
                AppendSharingFloat(b, r.cameraVelocityScale);
                AppendSharingFloat(b, r.minParticleSize);
                AppendSharingFloat(b, r.maxParticleSize);
                b.Append((int)r.alignment).Append(':').Append((int)r.sortMode);
                b.Append(':').Append((int)r.renderMode).Append(':').Append(ps.trails.enabled).Append(':');
                r.GetSharedMaterials(_sharingMaterials);
                foreach (var material in _sharingMaterials) b.Append(material ? EngineObjects.Identity(material) : 0).Append(',');
                _sharingMaterials.Clear();
                var texture = ParticleSourceInfo.SpriteSheetTexture(ps);
                b.Append(texture ? EngineObjects.Identity(texture) : 0).Append(':');
                // Per-instance material animation does not change the shared geometry.
                r.GetActiveVertexStreams(_sharingStreams);
                foreach (var stream in _sharingStreams) b.Append((int)stream).Append(',');
                _sharingStreams.Clear();
                if (ps.trails.enabled)
                {
                    r.GetActiveTrailVertexStreams(_sharingStreams);
                    foreach (var stream in _sharingStreams) b.Append((int)stream).Append(',');
                    _sharingStreams.Clear();
                }
                if (r.renderMode == ParticleSystemRenderMode.Mesh)
                {
                    if (_sharingMeshes.Length != r.meshCount) _sharingMeshes = new Mesh[r.meshCount];
                    var count = r.GetMeshes(_sharingMeshes);
                    for (var j = 0; j < count; j++) b.Append(_sharingMeshes[j] ? EngineObjects.Identity(_sharingMeshes[j]) : 0).Append(',');
                }
                b.Append(';');
            }
            var equal = _sharingLayout != null && _sharingLayout.Length == b.Length;
            for (var i = 0; equal && i < b.Length; i++) equal = _sharingLayout[i] == b[i];
            if (!equal)
            {
                _sharingLayout = b.ToString();
                RefreshSharingIdentity();
                InvalidateRendererCaches();
            }
            else if (_automaticGroupKey == null || _resolvedTemplateKey != m_AutomaticSharingKey) ResolveAutomaticSharingGroup();
        }

#if UNITY_EDITOR
        internal bool RefreshEditorSharingIdentity()
        {
            var previous = m_AutomaticSharingKey;
            if (Application.isPlaying) return false;
            // Prefab asset identity also distinguishes duplicated assets and variants, whose
            // serialized template token may initially have been copied from the original.
            var path = UnityEditor.PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
            if (string.IsNullOrEmpty(path) && UnityEditor.EditorUtility.IsPersistent(this))
                path = UnityEditor.AssetDatabase.GetAssetPath(this);
            if (!string.IsNullOrEmpty(path))
            {
                var source = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(this);
                var asset = source ? source : this;
                if (UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out long localId))
                {
                    m_AutomaticSharingKey = UnityEditor.AssetDatabase.AssetPathToGUID(path) + ":" + localId;
                    var instanceRoot = UnityEditor.PrefabUtility.GetNearestPrefabInstanceRoot(gameObject);
                    var modifications = instanceRoot ? UnityEditor.PrefabUtility.GetPropertyModifications(instanceRoot) : null;
                    if (modifications != null)
                    {
                        var overrides = new List<string>();
                        foreach (var modification in modifications)
                        {
                            // UI placement/visibility is consumer-specific. Particle authoring overrides
                            // change the simulation template and must not borrow the base result.
                            if (!(modification.target is ParticleSystem) && !(modification.target is ParticleSystemRenderer)) continue;
                            UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(modification.target, out string targetGuid, out long targetId);
                            var reference = modification.objectReference;
                            var referenceId = reference ? EngineObjects.Identity(reference).ToString() : "0";
                            if (reference && UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(reference, out string referenceGuid, out long referenceLocalId))
                                referenceId = referenceGuid + ":" + referenceLocalId;
                            overrides.Add(targetGuid + ":" + targetId + ":" + modification.propertyPath + ":" + modification.value + ":" + referenceId);
                        }
                        overrides.Sort(StringComparer.Ordinal);
                        if (overrides.Count > 0)
                        {
                            using (var hash = System.Security.Cryptography.SHA256.Create())
                                m_AutomaticSharingKey += ":" + Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", overrides))));
                        }
                    }
                }
            }
            return previous != m_AutomaticSharingKey;
        }
#endif

        private static void AppendSharingFloat(StringBuilder builder, float value)
        {
            var bits = unchecked((uint)BitConverter.SingleToInt32Bits(value == 0 ? 0 : value));
            const string hex = "0123456789abcdef";
            for (var shift = 28; shift >= 0; shift -= 4) builder.Append(hex[(int)((bits >> shift) & 15)]);
            builder.Append(',');
        }
        private static void AppendSharingVector(StringBuilder builder, Vector3 value)
        {
            AppendSharingFloat(builder, value.x); AppendSharingFloat(builder, value.y); AppendSharingFloat(builder, value.z);
        }

        private void AppendSharingPath(StringBuilder builder, Transform source)
        {
            if (source == transform) { builder.Append('r'); return; }
            if (!source || !source.IsChildOf(transform)) { builder.Append("external:").Append(source ? EngineObjects.Identity(source) : 0); return; }
            AppendSharingPath(builder, source.parent);
            builder.Append('/').Append(source.GetSiblingIndex());
        }
    }
}
