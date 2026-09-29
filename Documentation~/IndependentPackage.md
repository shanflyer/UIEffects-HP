# UI Effects HP 接口

包名 `com.shanflyer.ui-effects`，命名空间和程序集 `ShanFlyer.UIEffects`，兼容 Unity 2022.3 LTS 和 Unity 6，最低版本 2022.3。分发包仅声明 UGUI 和 Unity ParticleSystem 模块依赖，不包含自定义渲染管线代码。

## 配置接口

使用下列配置与策略；共享分组由系统自动确定。

| 配置 | 当前接口 |
| --- | --- |
| 单位换算 | `unitConversion`：新组件默认 Automatic；旧组件保留 Manual；切入 Automatic 时倍率重置为 1 |
| 效果缩放 | `uniformScale` / `renderScale`：Automatic 下为换算后的用户倍率，默认 1 |
| 参考相机 | `referenceCamera`：可选覆盖；否则使用 Camera 画布相机，Overlay 无相机时按 Canvas Reference Pixels Per Unit 换算 |
| 换算结果 | `resolvedUnitScale`：最近一次更新计算的 UI 单位 / 世界单位，Manual 为 1 |
| 手动缩放策略 | `scaleMode`：Hierarchy / Canvas / NormalizeRoot，仅 Manual 使用 |
| 输出原点 | `originMode`：Effect / Emitter |
| 模拟角色 | `simulationRole`：Independent / Automatic / Producer / SimulationOnly / Consumer |
| 烘焙视图 | 自动匹配根 Canvas 尺寸或其绑定相机，无需配置 |
| 模拟速度 | `simulationSpeed` |
| 替换内容 | `InstallEffect(instance, destroyPrevious)`；调用方自行实例化预制体 |
| 材质属性 | `MaterialPropertyBinding`：Float / Integer / Color / Vector / Texture / Matrix 及数组 |

共享关系只由模板身份与输出结构决定，内部编号不是公开配置。需要独立运行时选择 Independent；需要共享时克隆同一模板。Producer 等角色只控制组内职责，不绕过结构检查。

## 适配实现

- `ParticleSourceBinding` 按源管理接管引用，粒子本体、Trails 和合并输出统一获取与释放；最后一个输出释放时恢复原生 enabled 状态。绑定已有源不清空粒子；Sprite UV0 要求随接管获取与释放。
- `ParticleCoordinates` 明确烘焙空间到世界空间的映射，吸引器通过其逆映射取得目标；不可逆坐标变换不执行吸引。
- `CanvasScaleState` 独立持有根节点缩放控制及恢复状态，退出控制时恢复实际记录的 authored scale，包括零轴。
- `CanvasMaterialBinding` 以材质、贴图和属性所有者的对象引用组成键，管理材质变体生命周期；不使用编辑器 JSON 哈希作为材质身份。
- 裁剪使用一条状态转换路径；输出槽位按需增长并恢复缺失节点。烘焙中间网格延迟按输出创建，中间网格由各输出持有。
- 吸引器接口为 ArrivalRadius / StartDelay / Speed / Path / Clock / Curvature / Arrived / Sources。Speed 表示模拟空间单位每秒，曲线路径相对于目标与速度计算，速度阻尼随经过时间计算。

源模拟会话只调用原生 Simulate；输出缩放不反写粒子状态。单源与合并输出共用更新流程，错峰调度、几何加工、自动分组、SpriteMask 与多种 Renderer 桥接继续使用。

## 吸引器与播放语义

吸引器的 ArrivalRadius 和 Speed 分别使用模拟空间长度与单位每秒；StartDelay 是生命周期比例。延迟结束后才检测捕获。Linear 以恒定速度接近；Eased 用平滑进度调整移动量；Curved 使用速度侧向分量弯曲接近路径，不围绕世界原点插值。

到达时终止粒子寿命，统一提交后触发 Arrived。重复来源只处理一次；暂停效果和共享组非生产者不被吸引器写入。持续吸引使静态快照失效，但仍遵守烘焙限频。

播放命令只控制显式绑定的粒子来源，避免递归影响嵌套的其他 UIEffectRenderer。源列表中重复项只执行一次。

## 生命周期

EffectBakeView 只持有自建相机，不修改外部 Canvas 相机；组件停用、销毁或切换为外部视图时释放自建相机。CanvasScaleState 同时保存缩放快照和根缩放恢复状态。

Unity BakeMesh、CanvasRenderer、模拟空间与 UGUI 遮罩是渲染基础。包内 Additive Shader 提供加法混合、Stencil 和矩形软裁剪。当前验证范围见 [Validation](Validation.md)。

## 全局设置

通过 UIEffectSettings.Current 访问 ColorPolicy（MatchCanvas / Preserve）、ShowGeneratedObjects 及项目性能配置。性能默认值、代码覆盖与恢复方式见 [性能说明](Performance.md)。配置保存在 Resources/UIEffectSettings.asset，编辑器入口为 Project Settings/UI Effects HP。

显示缩放只影响输出矩阵；World 粒子位置和 rateOverDistance 遵循原生模拟空间。Shape 不会被自动修正。预热仅发生于启用 prewarm 的空循环源初始周期，并单独调用原生模拟。

烘焙视图优先使用非 Overlay 根 Canvas 绑定的相机；Overlay 或未绑定相机的 Canvas，按根 Canvas 的实际宽高、缩放和朝向生成内部正交视图。Canvas 或相机变化会自动刷新视图、使几何缓存失效并重新匹配共享分组。特效大小仍通过 renderScale 调整。
