using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ShanFlyer.UIEffects.Internal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

[assembly: InternalsVisibleTo("ShanFlyer.UIEffects.Editor")]
[assembly: InternalsVisibleTo("ShanFlyer.UIEffects.Editor.Tests")]

namespace ShanFlyer.UIEffects
{
    /// <summary>
    /// Render particle, mesh, skinned mesh, sprite, line and trail sources through UGUI.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("UI/Effects/UI Effect Renderer")]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    public partial class UIEffectRenderer : MaskableGraphic, ISerializationCallbackReceiver
    {
        internal readonly ParticleTopology sourceTopology = new ParticleTopology();
        private readonly List<CanvasEffectOutput> _renderers = new List<CanvasEffectOutput>();
        private int _activeRendererCount;
        private readonly EffectOutputPlan _outputPlan = new EffectOutputPlan();
        private bool _simulationOwner;
        internal double independentBakePhase, particleBakePhase;
        internal bool groupAllAlphaHidden;
        internal bool groupAllClipped;
        internal int activeRendererCount => _activeRendererCount;
        internal int mergedRendererCount
        {
            get { int count = 0; for (int i = 0; i < _activeRendererCount; ++i) if (_renderers[i] && _renderers[i].isMerged) ++count; return count; }
        }
        private bool _mergeModeStamp;
        private bool _sharingEnabledStamp;
        private bool _fallbackToUnmerged;
        private bool _rebuildRenderers;
        private bool _rendererBindingsDirty;
        private SimulationRole _sharingModeStamp;
        private int _sharingGroupStamp;
        private readonly List<ParticleSystem> _particleBindings = new List<ParticleSystem>();
        private readonly EffectBakeView _bakeView = new EffectBakeView();
        private int _groupId;
        private readonly CanvasScaleState _scaleState = new CanvasScaleState();
        private static int s_RootCanvasFrame = -1;
        private static readonly Dictionary<Canvas, Canvas> s_RootCanvases = new Dictionary<Canvas, Canvas>();

        private static Canvas RootCanvas(Canvas value)
        {
            if (s_RootCanvasFrame != Time.frameCount)
            {
                s_RootCanvasFrame = Time.frameCount;
                s_RootCanvases.Clear();
            }
            if (!s_RootCanvases.TryGetValue(value, out var root))
                s_RootCanvases[value] = root = value.rootCanvas;
            return root;
        }

        /// <summary>
        /// Should this graphic be considered a target for ray-casting?
        /// </summary>
        public override bool raycastTarget
        {
            get => false;
            set { }
        }

        /// <summary>
        /// Global UI geometry update percentage (1–100). 100 updates every frame; 50 averages every other frame.
        /// Creation, invalidation and visibility recovery may refresh immediately.
        /// </summary>
        public static int updateRatePercent { get => _updateRateOverride ?? UIEffectSettings.Current.UpdateRatePercent; set => _updateRateOverride = Mathf.Clamp(value, 1, 100); }

        /// <summary>Spread limited-rate baking across stable effect phases. Every-frame mode is unchanged.</summary>
        public static bool staggerBaking { get => _staggerOverride ?? UIEffectSettings.Current.StaggerUpdates; set => _staggerOverride = value; }

        /// <summary>
        /// Merge consecutive compatible particle outputs. Sharing groups retain a common layout.
        /// Material property synchronization, source sorting and incompatible inputs use separate outputs.
        /// </summary>
        public static bool mergeRenderers { get => _mergeOverride ?? UIEffectSettings.Current.MergeParticleOutputs; set => _mergeOverride = value; }

        /// <summary>
        /// Early Culling 档位:0 = 关(不因不可见而跳过烘焙);
        /// 1 = RenderCull(UGUI 已判裁剪/出屏时仅停止 Bake/Combine/SetMesh,模拟继续保证时间连续);
        /// 2 = RenderCull + FullCull(CanvasGroup 累积 alpha≈0 的整页隐藏时连模拟一起停,恢复可见后从停点继续)。
        /// SimulationRole 按整组消费者可见性裁剪,任一副本可见时保持共享输出。
        /// </summary>
        public static int earlyCull { get => _cullOverride ?? UIEffectSettings.Current.InvisibleWorkMode; set => _cullOverride = Mathf.Clamp(value, 0, 2); }

        /// <summary>
        /// 静态网格缓存:开 = 全部系统暂停且相关 Transform/Canvas 状态未变时,
        /// 整帧跳过 Simulate/Bake/Combine/SetMesh(网格零成本保持);关 = 暂停时仍按更新频率烘焙。
        /// </summary>
        public static bool staticMeshCache { get => _staticOverride ?? UIEffectSettings.Current.CachePausedParticles; set => _staticOverride = value; }

        /// <summary>
        /// Binding Fast Mode(默认关):开 = 把运行时绑定状态的“自动侦测”
        /// 改为“调用方显式通知”——跳过每帧对材质/贴图/trail/renderer 的复核,只在绑定时校验一次;
        /// 关 = 每帧校验绑定，自动检测运行时材质和贴图变更。
        /// 开启后,下列运行时改动不再被自动侦测,必须调用 <see cref="MarkBindingDirty"/> 才会生效:
        /// ParticleSystemRenderer.sharedMaterial / trailMaterial、材质 mainTexture、trails.enabled、
        /// TextureSheetAnimation 的 sprite/来源贴图、绑定槽位内的 renderer 替换。
        /// ParticleSystem 列表本身的增删/替换仍会自动侦测重建。
        /// simulationSpace 切换不依赖此开关(烘焙时实时读取),无需 MarkBindingDirty。
        /// 仅当项目承诺运行期基本不修改 UI 粒子的材质/贴图/trail 时开启。
        /// </summary>
        public static bool fastBindingMode { get => _bindingOverride ?? UIEffectSettings.Current.SkipBindingChecks; set => _bindingOverride = value; }

        /// <summary>Project-wide permission; each component still selects its own simulation role.</summary>
        public static bool meshSharingEnabled { get => _sharingOverride ?? UIEffectSettings.Current.AllowParticleSharing; set => _sharingOverride = value; }

        private static int? _updateRateOverride, _cullOverride;
        private static bool? _staggerOverride, _mergeOverride, _staticOverride, _bindingOverride, _sharingOverride;
        public static bool hasPerformanceOverrides => _updateRateOverride.HasValue || _cullOverride.HasValue
            || _staggerOverride.HasValue || _mergeOverride.HasValue || _staticOverride.HasValue
            || _bindingOverride.HasValue || _sharingOverride.HasValue;

        // Runtime assignments never modify the saved project asset. Clear even with domain reload disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetPerformanceOverrides()
        {
            _updateRateOverride = _cullOverride = null;
            _staggerOverride = _mergeOverride = _staticOverride = _bindingOverride = _sharingOverride = null;
        }

        // 烘焙诊断:每帧由测试控制器读取并清零。
        public static int s_FrameBakedVerts;
        public static int s_FrameBakeOps;

        /// <summary>
        /// Paused.
        /// </summary>
        private readonly ParticlePlayback _playback = new ParticlePlayback();
        public bool isPaused => _playback.Paused;
        internal uint playbackCycle => _playback.Cycle;

        public Vector3 parentScale => _scaleState.ParentScale;

        public Vector3 canvasScale => _scaleState.CanvasScale;

        protected override void OnEnable()
        {
            base.OnEnable();
            NormalizeConfiguration();
            RegisterDirtyMaterialCallback(UpdateRendererMaterial);
            if (particles.Count == 0) RefreshSources();
            else RefreshSources(particles);
            RefreshSharingIdentity();
            SpriteMaskNativeRendering.Register(this);
            UIEffectScheduler.Register(this);
        }

        /// <summary>
        /// This function is called when the behaviour becomes disabled.
        /// </summary>
        protected override void OnDisable()
        {
#if UNITY_EDITOR
            EditorPlayback.Set(this, false);
#endif
            _bakeView.Dispose();
            ReleaseAutomaticSharingGroup();
            SpriteMaskNativeRendering.Unregister(this);
            _scaleState.Release();
            UIEffectScheduler.Unregister(this);
            _outputPlan.Clear();
            for (int i = 0; i < _renderers.Count; ++i) if (_renderers[i]) _renderers[i].Reset(i);
            _activeRendererCount = 0;
            _simulationOwner = false;
            UnregisterDirtyMaterialCallback(UpdateRendererMaterial);

            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            ReleaseAutomaticSharingGroup();
            SpriteMaskNativeRendering.Unregister(this);
            // Removing just the UIEffectRenderer component must also release its generated
            // children. Scene/GameObject destruction already removes them naturally.
            for (var i = 0; i < _renderers.Count; i++)
                if (_renderers[i] != null) EngineObjects.Destroy(_renderers[i].gameObject);
            _renderers.Clear();
            _bakeView.Dispose();
            base.OnDestroy();
        }

        /// <summary>
        /// Callback for when properties have been changed by animation.
        /// </summary>
        protected override void OnDidApplyAnimationProperties()
        {
            NormalizeConfiguration();
            QueueConfigurationRefresh(true);
        }

        /// <summary>Restart the bound particle sources. Other Renderer types keep their own animation.</summary>
        public void Play() => _playback.Execute(this, ParticleCommand.Restart);
        public void Pause() => _playback.Execute(this, ParticleCommand.Pause);
        public void Resume() => _playback.Execute(this, ParticleCommand.Resume);
        public void Stop() => _playback.Execute(this, ParticleCommand.Stop);
        public void Clear() => _playback.Execute(this, ParticleCommand.Clear);
        public void StartEmission() => _playback.Execute(this, ParticleCommand.EnableEmission);
        public void StopEmission() => _playback.Execute(this, ParticleCommand.DisableEmission);

        /// <summary>Replace authored child roots with an existing effect instance; generated UI nodes are retained.</summary>
        public void InstallEffect(GameObject instance, bool destroyPrevious = true)
        {
            if (!instance) throw new ArgumentNullException(nameof(instance));
            if (instance.transform == transform || transform.IsChildOf(instance.transform))
                throw new ArgumentException("An effect cannot install itself or an ancestor.", nameof(instance));
            var retired = new List<GameObject>();
            foreach (Transform child in transform)
            {
                if (instance.transform.IsChildOf(child) || child.gameObject == instance) continue;
                if (child.TryGetComponent<CanvasEffectOutput>(out _) || child.TryGetComponent<CanvasSpriteMaskGraphic>(out _)) continue;
                if (_bakeView.Owns(child)) continue;
                retired.Add(child.gameObject);
            }
            foreach (var previous in retired) previous.SetActive(false);
            instance.transform.SetParent(transform, false);
            instance.transform.localPosition = Vector3.zero;
            RefreshSources();
            if (destroyPrevious) foreach (var previous in retired) EngineObjects.Destroy(previous);
        }

        /// <summary>
        /// Collect child particle sources and rebuild outputs for all enabled source types.
        /// </summary>
        public void RefreshSources()
        {
            RefreshSources(gameObject);
        }

        /// <summary>
        /// Collect particle sources owned by this effect, then rebuild source outputs.
        /// </summary>
        private void RefreshSources(GameObject root)
        {
            if (!root) return;
            var found = Buffers<ParticleSystem>.Rent();
            try
            {
                root.GetComponentsInChildren(true, found);
                particles.Clear();
                foreach (var source in found)
                {
                    if (!source || source.GetComponentInParent<UIEffectRenderer>(true) != this) continue;
                    if (source.gameObject.CompareTag("EditorOnly")) continue;
                    particles.Add(source);
                }
                RefreshSources(particles);
            }
            finally { Buffers<ParticleSystem>.Return(ref found); }
        }

        /// <summary>
        /// Replace the particle source list and rebuild outputs, including enabled renderer sources.
        /// </summary>
        public void RefreshSources(List<ParticleSystem> particleSystems)
        {
            if (particleSystems == null) throw new System.ArgumentNullException(nameof(particleSystems));
            if (!ReferenceEquals(particleSystems, particles))
            {
                particles.Clear();
                particles.AddRange(particleSystems);
                particleSystems = particles;
            }
            _particleBindings.Clear();
            _particleBindings.AddRange(particleSystems);
            // Preserve slot identity despite Canvas sibling reordering; discover orphan outputs only on refresh.
            for (int i = 0; i < transform.childCount; ++i)
                if (transform.GetChild(i).TryGetComponent<CanvasEffectOutput>(out var output)
                    && !_renderers.Contains(output)) _renderers.Add(output);

            RefreshAutomaticSharingLayout();
            CollectBridgeSources();

            _mergeModeStamp = mergeRenderers;
            _sharingEnabledStamp = meshSharingEnabled;
            _rendererBindingsDirty = true;
            _rebuildRenderers = false;

            bool allowMerge = mergeRenderers && !m_SortBySourceOrder && !_fallbackToUnmerged
                && !hasMaterialPropertyBindings;
            // Shared effects retain one common layout. Independent effects may merge consecutive runs.
            if (useMeshSharing && !CanvasEffectOutput.CanMerge(particleSystems)) allowMerge = false;
            sourceTopology.Rebuild(particleSystems);
            try
            {
                _outputPlan.Build(this, particleSystems, allowMerge);
                PlanBridgeSources(_outputPlan);
                _outputPlan.Apply(this);
            }
            catch { _outputPlan.Clear(); _rebuildRenderers = true; throw; }
        }

        internal void ReleaseChangedOutputs(HashSet<CanvasEffectOutput> retained)
        {
            for (int i = 0; i < _renderers.Count; ++i)
                if (_renderers[i] && !retained.Contains(_renderers[i])) _renderers[i].Reset(i);
            _activeRendererCount = 0;
        }

        internal bool SetSimulationOwner(bool value)
        {
            if (_simulationOwner == value) return false;
            _simulationOwner = value;
            for (var i = 0; i < _activeRendererCount; i++)
                if (_renderers[i] != null)
                {
                    if (!value) _renderers[i].ReleaseBakeResources();
                    else _renderers[i].InvalidateMeshCache();
                }
            return true;
        }

        internal void GetOutputVisibility(out bool hasOutput, out bool alphaHidden, out bool clipped)
        {
            hasOutput = canRender && _activeRendererCount > 0;
            alphaHidden = clipped = true;
            if (!hasOutput || !canvas || !canvas.isActiveAndEnabled) return;
            for (var i = 0; i < _activeRendererCount; i++)
            {
                var r = _renderers[i];
                if (r == null || !r.isActiveAndEnabled) continue;
                var alpha = r.alphaHidden;
                alphaHidden &= alpha;
                clipped &= alpha || r.clipHidden;
            }
        }

        internal bool PrepareForUpdate()
        {
            if (_configurationPending)
            {
                NormalizeConfiguration();
                InvalidateRendererCaches();
                _configurationPending = false;
            }
            if (_renderSkinnedStamp != m_RenderSkinnedMeshes || _renderSpritesStamp != m_RenderSprites
                || _renderMeshesStamp != m_RenderMeshes || _renderLinesStamp != m_RenderLines
                || _sourceSortStamp != m_SortBySourceOrder)
                _rebuildRenderers = true;
            if (_mergeModeStamp != mergeRenderers || _sharingEnabledStamp != meshSharingEnabled)
            {
                _fallbackToUnmerged = false;
                _rebuildRenderers = true;
            }

            if (_particleBindings.Count != particles.Count) _rebuildRenderers = true;
            else
                for (var i = 0; i < particles.Count; i++)
                    if (_particleBindings[i] != particles[i]) { _rebuildRenderers = true; break; }
            // Binding Fast Mode: skip the per-frame material/texture/trail/renderer
            // revalidation. The particle list check above still runs and a destroyed child
            // renderer is still detected; runtime binding edits must call MarkBindingDirty().
            if (fastBindingMode)
            {
                for (var i = 0; i < _activeRendererCount; i++)
                    if (_renderers[i] == null) { _rebuildRenderers = true; break; }
            }
            else
            {
                for (var i = 0; i < _activeRendererCount; i++)
                    if (_renderers[i] == null || _renderers[i].bindingIsInvalid) _rebuildRenderers = true;
            }
            if (_rebuildRenderers) RefreshSources(particles);

            SpriteMaskNativeRendering.Sync(this);
            UpdateTransformScale();
            if (_bakeView.Refresh(this, canvas ? RootCanvas(canvas) : null)) InvalidateRendererCaches();
            OrderSourceOutputs();
            for (var i = 0; i < _activeRendererCount; i++)
                if (_renderers[i] != null)
                {
                    _renderers[i].MaintainBridgeSuppression();
                    _renderers[i].PrepareSpriteMask();
                }
            RefreshAutomaticSharingLayout();
            var changed = _rendererBindingsDirty || _sharingModeStamp != simulationRole || _sharingGroupStamp != sharingGroup;
            _rendererBindingsDirty = false;
            _sharingModeStamp = simulationRole;
            _sharingGroupStamp = sharingGroup;
            return changed;
        }

        internal void RequestUnmergedFallback()
        {
            if (_fallbackToUnmerged && _mergeModeStamp == mergeRenderers) return;
            _fallbackToUnmerged = true;
            _mergeModeStamp = mergeRenderers;
            _rebuildRenderers = true;
        }

        internal bool requiresSeparateSharingLayout => needsSpriteMaskIsolation
            || hasMaterialPropertyBindings || !CanvasEffectOutput.CanMerge(particles);

        internal bool hasUnmergedFallback => _fallbackToUnmerged && _mergeModeStamp == mergeRenderers;

        internal bool needsSpriteMaskIsolation
        {
            get
            {
                if (m_SortBySourceOrder) return true; // Group-wide separate slots also preserve mixed source ordering.
                foreach (var ps in particles)
                    if (ps && ps.TryGetComponent<ParticleSystemRenderer>(out var renderer)
                        && renderer.maskInteraction != SpriteMaskInteraction.None) return true;
                return false;
            }
        }

        /// <summary>
        /// Call on the main thread after externally changing source geometry or particle data,
        /// especially while paused with static caching. Invalidates output geometry caches
        /// for the next eligible update without restarting particle simulation.
        /// Shared particle groups still use their selected producer's data.
        /// Hidden output still obeys culling.
        /// </summary>
        public void MarkGeometryDirty()
        {
            UIEffectScheduler.MarkGeometryDirty(this);
        }

        // Ongoing particle edits expire static snapshots without bypassing the bake clock.
        internal void InvalidateAnimatedParticleContent()
        {
            for (int slot = 0; slot < _activeRendererCount; ++slot)
                if (_renderers[slot] && !_renderers[slot].isBridge) _renderers[slot].ExpireParticleSnapshot();
        }

        /// <summary>
        /// 在运行时修改了绑定状态后,且 <see cref="fastBindingMode"/> 为开时必须调用。
        /// fast mode 把“自动侦测”换成“显式通知”,下列改动不会再自动生效:
        /// ParticleSystemRenderer.sharedMaterial / trailMaterial、材质 mainTexture、trails.enabled、
        /// TextureSheetAnimation 的 sprite/来源贴图、绑定槽位内的 renderer 替换。
        /// (ParticleSystem 列表的增删/替换、renderer 被销毁仍会自动侦测。)
        /// 调用后会在下一次 update 重绑并拾取改动,等价于补上 fast mode 跳过的那次校验。
        /// fast mode 关闭时调用是安全的(仅多一次重绑)。
        /// </summary>
        public void MarkBindingDirty()
        {
            _rebuildRenderers = true;
        }

        internal void InvalidateRendererCaches()
        {
            for (var i = 0; i < _activeRendererCount; i++)
                if (_renderers[i] != null) _renderers[i].InvalidateMeshCache();
        }

        internal void ClearRendererMeshes()
        {
            for (var i = 0; i < _activeRendererCount; i++)
                if (_renderers[i] != null && !_renderers[i].isBridge && _renderers[i].isActiveAndEnabled) _renderers[i].ClearMesh();
        }

        private void UpdateTransformScale()
        {
            var root = canvas ? RootCanvas(canvas) : null;
            _scaleState.Capture(this, root);
            var units = unitConversion == UnitConversion.Automatic
                ? EffectUnitScale.Calculate(root, referenceCamera, transform.position) : 1;
            if (_automaticUnitScale != units)
            {
                _automaticUnitScale = units;
                InvalidateRendererCaches();
            }
        }

        internal void UpdateRenderers()
        {
            if (!isActiveAndEnabled) return;

            // FastPath:全部渲染器空闲(刀6 静态命中/刀5 FullCull 命中)时,
            // 跳过 GetBakeCamera 与逐渲染器 UpdateMesh 调用链。UpdateTransformScale 仍由
            // UIEffectScheduler.Refresh 正常驱动(IsStaticFrameCommon 依赖新鲜的 parentScale)。
            var allIdle = true;
            for (var i = 0; i < _activeRendererCount; i++)
            {
                var r = _renderers[i];
                if (r == null || r.isBridge || r.IsIdleForFastPath()) continue;

                allIdle = false;
                break;
            }

            if (allIdle) return;

            var bakeCamera = GetBakeCamera();
            for (var i = 0; i < _activeRendererCount; i++)
            {
                var r = _renderers[i];
                if (r == null || r.isBridge) continue;

                r.UpdateMesh(bakeCamera);
            }
        }

        internal void RefreshSharingIdentity() => ResolveAutomaticSharingGroup();

        protected override void UpdateMaterial()
        {
        }

        /// <summary>
        /// Call to update the geometry of the Graphic onto the CanvasRenderer.
        /// </summary>
        protected override void UpdateGeometry()
        {
        }

        private void UpdateRendererMaterial()
        {
            for (int slot = 0; slot < activeRendererCount; ++slot)
                GetRendererIfExists(slot)?.ApplyMaskPolicy(maskable);
        }

        internal CanvasEffectOutput GetRenderer(int index)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            while (_renderers.Count <= index) _renderers.Add(null);
            var output = _renderers[index];
            if (!output) _renderers[index] = output = CanvasEffectOutput.AddRenderer(this, index);
            _activeRendererCount = Math.Max(_activeRendererCount, index + 1);
            return output;
        }

        internal CanvasEffectOutput GetRendererIfExists(int index)
        {
            return 0 <= index && index < _activeRendererCount ? _renderers[index] : null;
        }

        internal uint bakeViewRevision => _bakeView.Revision;
        private Camera GetBakeCamera() => _bakeView.Resolve(this, canvas ? RootCanvas(canvas) : null);
    }
}
