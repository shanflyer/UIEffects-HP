# 接入与功能边界

兼容目标为 Unity 2022.3 LTS（UGUI 1.0）和 Unity 6（UGUI 2.0）。仓库根目录即独立包 `com.shanflyer.ui-effects`，C# 命名空间为 `ShanFlyer.UIEffects`，接口见 [独立包说明](IndependentPackage.md)。在自己的 Unity 工程中通过 Package Manager 安装，不要把包仓库作为完整 Unity 工程打开。不需要自定义渲染管线。

## 基本接入

支持 Screen Space - Overlay，以及绑定独立 UI 相机的 Screen Space - Camera。UI 相机开始时应清除深度，并在场景之后绘制；后续 Pass/相机不得依赖该 UI 阶段丢弃的深度。与场景共用相机、保留场景深度的流程不在支持范围，插件不自动修改相机设置。World Space 不接管：释放 UI 输出、原生 Renderer/SpriteMask 的抑制和根缩放控制，原生来源继续绘制。Camera 模式缺少 Render Camera 时也保留原生绘制；重新切换到受支持模式后自动重新接管。

1. 将特效放入 Canvas，在根节点添加 `UIEffectRenderer`，保留子 ParticleSystem 及其 Renderer。
2. 新组件默认 Unit conversion = Automatic、Size multiplier = 1。Camera 画布按绑定相机换算，也可指定 Reference camera；Overlay 无参考相机时按 Canvas Reference Pixels Per Unit 换算。Particle zoom origin 默认 Emitter，保留发射器的 Transform 位置。
3. 使用支持目标 UI 功能的材质。UGUI `Mask` 需要完整的 Stencil 属性和实际使用这些属性的绘制 Pass；`RectMask2D` 另需裁剪逻辑。仅添加属性不能实现遮罩，组件不会自动转换任意原生 Shader。每个材质槽及粒子 Trail 材质都需满足要求。完整代码片段及可直接使用的材质见 [Shader 接入示例](ShaderIntegration.md)。

粒子与 UI 按 Canvas 层级绘制；粒子几何由 Unity 的 `BakeMesh` / `BakeTrailsMesh` 生成。内部烘焙相机用于几何朝向等计算，不是额外的 Camera + RenderTexture 合成路径。

自动尺寸由相机投影、视口高度和 Canvas 实际缩放计算，透视相机以特效根节点深度为准；参考相机缺失或投影深度无效时回退到 Canvas Reference Pixels Per Unit。Overlay 的无相机回退是单位约定，并非测得的相机比例。屏幕空间自动模式统一控制根节点缩放，用户用 Size multiplier 调整大小；World Space 不启用桥接及自动缩放。旧组件保留 Manual、原 Effect scale 与 Canvas scaling；切换 Automatic 时倍率重置为 1。Sprite 资产 PPU 先决定源几何尺寸，再参与自动换算。

## Mesh 深度分组

按照实际输出层级，将连续的 MeshRenderer / SkinnedMeshRenderer 输出自动划为一组，可跨相邻 UIEffectRenderer。同一来源的多子网格、多材质输出保留在组内。普通 UI、其他特效类型、嵌套 Canvas 和启用的 Mask / RectMask2D 边界会断开组；不需要填写 GroupId，与粒子模拟共享组无关。

例如 `UI → Mesh1 → Mesh2 → UI → Mesh3 → UI` 分成两组：Mesh1 与 Mesh2 共享深度，Mesh3 单独一组。组内按三维深度遮挡，后面的兄弟节点不一定覆盖前面的；组间按 UI 绘制顺序覆盖。若要在不同 UI 元素之间穿插来源，使用分开的特效根节点；源收集仍然将输出放在所属特效根节点下，不会按源节点位置重排普通 UI。

组首、组尾各插入一次仅写深度的辅助绘制，保留颜色与全部 Stencil 位。辅助节点不受 RectMask2D 和 CanvasGroup 透明度影响；可见 Mesh 仍然由兼容材质完成遮罩与淡入淡出。保留源材质的 ZTest、ZWrite、Cull、Blend，不为实现 UI 排序关闭 Mesh 深度写入。透明材质自身的排序限制仍然存在。深度不备份、不恢复，不自动接管自定义渲染管线。

Mesh 提交材质使用独立副本，队列统一为 UI 的 3000，避免 Scene 相机将原始不透明队列的 Mesh 与深度辅助节点分到不同阶段；不修改源材质资产、表面类型和 Shader 关键字。同步材质属性后仍维持 UI 队列。输出保留完整三维包围盒，UGUI 裁剪矩形单独从三维包围盒计算；深度辅助节点的包围盒同时覆盖 Canvas 与所在 Mesh 组，避免 Scene 视图旋转、缩放后错误剔除。

## 独立 Mesh、SkinnedMesh、Sprite、Line、Trail

| 设置 | 默认值 | 作用 |
| --- | --- | --- |
| `effect.renderMeshes` | `true` | 接管 MeshRenderer，按材质槽拆分子网格输出 |
| `effect.renderSkinnedMeshes` | `true` | 接管 SkinnedMeshRenderer，支持骨骼和 BlendShape 烘焙 |
| `effect.renderSprites` | `true` | 接管 SpriteRenderer，支持 Simple / Sliced / Tiled |
| `effect.renderLines` | `true` | 接管符合条件的 LineRenderer / TrailRenderer |
| `effect.sortBySourceOrder` | `true` | 按源 Sorting Layer 排序值、Order、层级顺序组织输出 |

扫描包含 inactive 子节点；每个源只归最近的 UIEffectRenderer 所有。Inspector 分别列出 Mesh、SkinnedMesh、Sprite、Line、Trail，Include 开关控制接入类型。不支持的来源也保留在检查列表里，并说明缺失 Mesh、Read/Write 未启用或材质槽等问题；修复后点击 Collect sources。

- MeshRenderer 必须有 MeshFilter、可读 Mesh、三角形子网格及非空材质槽。导入模型需在 Import Settings 启用 Read/Write 并 Apply。按材质槽顺序生成独立 UI 输出；少于子网格数量的材质槽只绘制对应子网格，多出的材质槽重复绘制最后一个子网格，与原生对照一致。Mesh/SkinnedMesh 缺少顶点颜色时，在 UI 输出补充不透明白色，已有颜色保持不变；源 Mesh 不被修改。
- SkinnedMeshRenderer 通过 BakeMesh 获取骨骼/BlendShape 变形后的几何，按同样规则拆分材质输出。同一来源同帧复用一次烘焙；绑定期间开启 updateWhenOffscreen，最后解绑恢复原值。Animator 的剔除设置仍由项目控制：UI-only 动画需要避免因没有场景相机观察而停止更新。
- SpriteRenderer 读取 Sprite 几何与 UV，支持 Pivot、颜色、flipX/flipY、Sprite 替换、Simple、九宫格 Sliced、Continuous/Adaptive Tiled；帧间复用未变化的几何。原地 OverrideGeometry 后调用 MarkGeometryDirty 或 RefreshSources。Sliced/Tiled 资产使用 Full Rect 与正确 border；支持原生 SpriteMask 的 None/Inside/Outside、排序范围和 SortingGroup，材质须满足 UI Stencil 契约；2D SpriteSkin 变形与 2D 光照不在本次范围。
- Sprite 贴图由 Sprite/图集提供，材质不自动转换。使用 UGUI Mask/RectMask2D 时仍需 UI 兼容 Shader；secondary textures、自定义 Sprite Shader 的 Renderer 专有输入不会自动完整迁移。
- LineRenderer / TrailRenderer 必须只有一份非空材质；通过原生 BakeMesh 生成快照。
- 普通不可读 Mesh、非三角形子网格、包含空材质槽的来源不接管。不支持的源保留原生绘制，不会自动获得 UI 排序与遮罩。
- 被接管的源使用 `forceRenderingOff` 隐藏原生绘制。解绑、禁用或销毁输出时恢复原值；不改变源的 enabled、emitting、宽度和最小采样距离。原先已禁止原生绘制的源不会被强行显示。

新增源、修改支持条件或重新组织层级后调用：

```csharp
effect.RefreshSources();
```

`RefreshSources(selectedParticles)` 保留传入的粒子选择，并重新收集独立 Renderer。普通模式会复核已绑定源的材质、贴图、Mesh 和归属；Fast Binding 模式需要显式通知绑定变化。

## 自动分组与共享

选择 `simulationRole` 的 Automatic / Producer / Consumer 模式后，按模板自动分组：同一预制体或 `Instantiate` 克隆继承来源标识，渲染结构一致时自动共享；独立创建的 UIEffectRenderer 使用独立标识。Automatic 自动选模拟所有者，独立创建的模板不会混成一组。

不同预制体资产与 Variant 使用不同标识。编辑器导入和 Player 构建流程保存来源标识；粒子配置的 Prefab override 会区分模板。运行时新增/选择/重排粒子、变更材质、顶点流、Mesh 或子变换等，会重新检查共享结构。

这是按来源共享模拟结果，不是逐帧比较所有粒子模块的完整配置。分别 `AddComponent` 构造的两个特效不会仅因参数相似自动共享；要共享可克隆同一模板。若同源实例在运行时需要独立模拟参数、发射或时间进度，应使用 `SimulationRole.Independent`。独立 Mesh/SkinnedMesh/Sprite/Line/Trail 不共享模拟结果。

共享实例使用生产者正常烘焙所得的工作量数据评估合并，并逐项核对候选粒子槽位、来源索引及合并内部顺序。候选布局一致时允许连续段合并；不一致时全组使用分离粒子槽位，避免把错误的网格交给消费者。恢复兼容布局后可以重新合并，无需手动重置开关。

没有公开数字分组 API，也没有手动/随机分组回退分支。

## 排序、共享与暂停

新组件默认开启 `sortBySourceOrder`，已有组件保留序列化的开关值。开启时，同一组件内的输出按源 Sorting Layer 排序值、Order in Layer、源层级顺序排列；关闭时恢复内部输出槽位顺序（粒子、Mesh/SkinnedMesh/Sprite、Line/Trail）。它不改变组件与普通 UI 之间的 Canvas 层级关系，连续 Mesh 组内仍保留真实深度遮挡。

开启源排序后先排序，再对连续且兼容的粒子段进行收益估算，只有通过估算的段才合并。其他 Renderer、SpriteMask interaction 或不兼容材质/顶点流等会阻断合并，不会跨越交错的输出。合并与粒子共享仍是不同功能。源排序不复现相机距离透明排序，也不完整模拟嵌套 SortingGroup 的原生绘制顺序。

Mesh Sharing 只共享粒子结果。每个实例的独立 Mesh/SkinnedMesh/Sprite/Line/Trail 仍单独更新，包括 Automatic 的非模拟所有者与孤立 Consumer；SimulationOnly 不显示自己的桥接输出。

独立 Renderer 不随 UIEffectRenderer 的粒子暂停而冻结，仍读取源动画和轨迹。它们与粒子共用全局 `updateRatePercent`，限频时默认启用 `staggerBaking`：共享粒子按组同步，独立桥接按实例错开；编辑器预览、暂停粒子刷新及强制失效不属于严格限频承诺，详见[性能说明](Performance.md)。

独立 Mesh/SkinnedMesh/Sprite/Line/Trail 可配合父级 UGUI Mask / RectMask2D；其中 SpriteRenderer 还支持 [SpriteMask 桥接](SpriteMask.md)，沿用源 maskInteraction，不需要额外组件。

## 运行时修改

| 修改 | 通知方式 |
| --- | --- |
| 新增源、重建绑定、Fast Binding 下修改材质/贴图/trail 设置 | `MarkBindingDirty()` 或 `RefreshSources()` |
| 外部改粒子数据，要求缓存立即失效 | `MarkGeometryDirty()` |
| 原地改同一个 Sprite 的网格 / UV | `MarkSpriteMaskDirty()` |
| 原地改桥接 Mesh 的顶点 / UV / 索引 | 正常更新会比较内容；要求立即失效可用 `MarkGeometryDirty()` |

桥接材质动画通过 UIEffectRenderer 的 Material property synchronization 从源 MaterialPropertyBlock 读取指定属性。不是自动复制任意 MPB。输出支持 IMeshModifier；存在修改器时保守重建几何。

## 坐标、快照与顶点限制

Mesh、SkinnedMesh、Sprite 和局部空间 Line 按源 localToWorld 转换，并围绕各自 Transform 原点缩放几何，再转换到 UI 局部空间。Effect scale 不再缩放源物体的位置偏移；SpriteMask 使用对应 Sprite 的同一缩放原点。保留三轴缩放与镜像。世界空间 Line/Trail 的路径和宽度已使用场景坐标，Automatic 不再重复进行单位换算；Size multiplier = 1 时保持记录的位置和宽度。显式调整 Size multiplier 仍围绕效果根节点缩放路径，Manual 保留原缩放规则。世界空间 Line/Trail 的 `BakeMesh(..., useTransform:false)` 输出直接按世界坐标处理；只有局部空间 Line 再应用源 Transform。

Line/Trail 快照检查有限坐标、三角形和索引范围。Trail 额外过滤历史突变：短暂不连续会保留上一份有效输出，空轨迹清空，持续异常不会无限保留旧画面。Line 的端点允许即时跳变，不使用 Trail 历史突变过滤。低帧率或单帧卡顿不会单独阻止有效 Trail 烘焙。Line 至少需要两个不同的有效点及非零宽度；Trail 需要已有采样点及非零宽度曲线。`emitting=false` 不代表 Trail 头部停止跟随 Transform；不可见时原生轨迹仍可继续采样。

当前实现将单个 UI 输出限制为 64,999 顶点。合并后超限会请求整组回退为分离输出；单个输出仍超限时不会提交该网格。合并和共享都不能消除这个限制。

## 材质属性同步

在 UIEffectRenderer Inspector 的 Material property synchronization 中指定属性名与值类型。配置类为 `MaterialPropertyBinding`；执行由每个 UI 输出的 `MaterialPropertySynchronizer` 负责，读取源 Renderer 的普通 MaterialPropertyBlock。不会自动枚举全部属性，桥接输出优先读取材质槽属性块，槽为空时读取 Renderer 级属性块。所选类型必须与源属性存储类型相符。

- 颜色、向量、浮点数、贴图、矩阵与数组可按类型同步。`Integer` 使用 Unity 的 GetInteger / SetInteger 整数存储接口；浮点属性统一使用 Float，不提供旧式浮点取整类型。
- 空名称、空条目、Disabled 与未知类型会跳过；属性名在运行时变化后会重新解析 ID。
- 写入前对比目标材质当前值；值相同时跳过 Set 调用。数组读取采用可复用 List，首次使用及容量增长仍有分配。增加 Get 调用与比较，因此不承诺所有属性规模下都更快。
- 属性不在源属性块中时，同步器不写入。桥接渲染器保留原有的基材质恢复步骤，因此移除桥接 MPB 后恢复基材质；粒子路径保持既有值保留行为。数组仍遵循 Unity Material 的长度限制，建议固定数组长度。
- 配置同步属性仍会隔离 UI 材质，并保守限制合并和几何缓存。同步器不修改源 sharedMaterial，也不负责播放动画。

属性同步配置使用当前序列化格式，不读取旧字段或旧类型编号。

## 配置与粒子辅助模块

配置改为本包独立字段与接口，详见[接口说明](IndependentPackage.md)。无旧字段迁移和接口别名。实际变化会使输出缓存失效，重复赋相同值不触发失效。

公开配置 setter 拒绝 NaN / Infinity、未知枚举及负时间倍率（抛出 ArgumentOutOfRangeException）；负缩放用于镜像、零缩放用于隐藏，仍允许。烘焙视图尺寸自动匹配 Canvas，无手动尺寸配置。损坏的序列化数据在导入时修正为有效值；该步骤不访问 Unity 原生对象，后续主线程更新再应用缓存失效。

原 Vector3Extensions / ParticleSystemExtensions 是内部辅助集合，现已删除。向量相乘使用 Unity Vector3.Scale；不可逆、非有限或倒数溢出的缩放轴按单位轴补偿，避免将 Infinity / NaN 带入 UI 矩阵。全零或含零轴的输出仍按原有体积判定隐藏。

粒子读回缓冲按模拟器或吸引器持有，首次需要数据才分配数组，容量不足才增长。绑定时按完整源列表建立子发射器父级索引，不因材质拆段丢失父级；运行时修改 subEmitters 关系后调用 MarkBindingDirty() 或 RefreshSources() 重新绑定，编辑器预览每次推进前刷新索引。排序继续使用现有输出排序路径，已删除未被调用的旧 SortForRendering 方法。

项目设置资产位于 `Assets/Resources/UIEffectSettings.asset`。吸引器通过统一坐标映射的逆矩阵寻找目标，退化矩阵不执行吸引；Speed 的单位为模拟空间单位/秒。

### 新桥接的边界与成本

每个材质槽是一个独立 UI 输出，不承诺单 Draw Call。单个输出仍受 64,999 顶点限制；当前不会将一个超大源网格自动裁成更小的顶点块。Sprite 过密平铺会受同样的顶点预算保护。蒙皮 BakeMesh 是额外的 CPU 工作，错峰和降频可以控制调用频率，但不能消除其成本。

显式材质属性同步支持按材质槽读取 MPB；该槽无属性块时回退 Renderer 级属性块。设置的同步属性以外不会自动迁移。源抑制由引用计数管理，只有最后一个输出释放时才恢复原始 forceRenderingOff。

API 依据：[Unity SkinnedMeshRenderer.BakeMesh](https://docs.unity.com/en-us/engine/6000.5/script-reference/unityengine/skinnedmeshrenderer/bakemesh)、[Sprite drawMode](https://docs.unity.com/en-us/engine/6000.5/script-reference/unityengine/spriterenderer/drawmode)。实际回归使用项目安装的 Unity 6000.4.7f1，参见 Validation.md。
