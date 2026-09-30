# UI Effects HP

A UGUI effects package for **Unity 2022.3 LTS and Unity 6**. Package version: **1.0.0**.

## Features

- Render ParticleSystem, MeshRenderer, SkinnedMeshRenderer, SpriteRenderer, LineRenderer and TrailRenderer geometry through Canvas.
- Follow UI hierarchy order and support CanvasGroup fading, Mask and RectMask2D with compatible shaders.
- Support multiple submeshes and materials for MeshRenderer and SkinnedMeshRenderer, plus particle trails.
- Keep mesh depth testing and writing: consecutive mesh outputs share depth, with automatic depth resets between UI layers.
- Share particle simulation and geometry automatically between compatible template instances, without manual group IDs.
- Support SpriteMask for particles and SpriteRenderer, with a compatible stencil shader.

## Installation

Requires Unity 2022.3 or later. Use UGUI 1.0 with Unity 2022.3 and UGUI 2.0 with Unity 6; keep the UGUI version supplied by your editor.

This repository is the UPM package itself. In **Package Manager > Install package from Git URL**, enter:

```text
https://github.com/shanflyer/UIEffects-HP.git#v1.0.0
```

Alternatively, extract the [release package](https://github.com/shanflyer/UIEffects-HP/releases/tag/v1.0.0), then select its root `package.json` using **Install package from disk**, or copy the extracted `com.shanflyer.ui-effects` folder into your project's `Packages` directory. Create a separate Unity project to use or test the package.

## Usage

1. Use a **Screen Space - Overlay** Canvas, or **Screen Space - Camera** with a dedicated UI camera. Place the effect under the Canvas and add `UIEffectRenderer` to its root.
2. Assign UI-compatible materials to every source material slot, including particle trails. The package includes **ShanFlyer/UI Effects/Additive** and `UIAdditive.mat`.
3. Keep **Unit conversion = Automatic** and **Size multiplier = 1** to use automatic sizing. Adjust the multiplier for artistic sizing. The bake view size is calculated automatically.
4. Leave **Simulation role** at **Independent** for separate particle playback. Choose **Automatic** on compatible instances of the same template to share playback and geometry.

Use the source materials directly; no material, shader or prefab conversion is required. Canvas keeps each assigned shader. Only temporary mesh submission materials use render queue 3000; source assets retain their original properties and queues. Masking and fading require shader support. See [Shader setup and examples](Documentation~/ShaderIntegration.md).

**Mesh lighting:** Overlay supports Unlit or custom UI lighting; standard Lit is not supported. Screen Space - Camera supports Lit. UIEffectRenderer adds a nested Canvas on its own GameObject when placed below a screen-space Canvas. Camera-mode mesh outputs enable all **Additional Shader Channels** (TexCoord1, TexCoord2, TexCoord3, Normal and Tangent) only on that local Canvas. Parent/root Canvas settings and sibling UI vertex layouts stay unchanged. Custom lighting for Overlay must handle the UI coordinate/projection setup explicitly.

**Frame Debugger:** In the reported Unity 6.6 / URP / Direct3D 12 setup, selecting a UI mesh draw using Unity's default URP/Lit shader crashes the Editor. The cause is unresolved. Custom lighting shaders still need separate verification.

Create a ready-to-use example from **GameObject > UI (Canvas) > UI Effects HP > Particle example**.

**Canvas support:** World Space is not supported and keeps native rendering. Camera mode requires an assigned, dedicated UI camera that starts with cleared depth and renders after the scene; subsequent passes/cameras must not need the depth discarded by this UI stage. Sharing a camera with scene rendering is outside the supported setup. The package does not inspect custom render passes, change camera settings or back up scene depth.

**Mesh depth groups:** consecutive MeshRenderer and SkinnedMeshRenderer outputs share depth automatically, including across adjacent effect components. `UI → Mesh1 → Mesh2 → UI → Mesh3 → UI` creates two groups. Mesh1 and Mesh2 occlude each other by depth; their sibling order does not force one in front. Ordinary UI, other effect types, authored nested Canvas and active Mask/RectMask2D boundaries end a group. The automatically added effect Canvas does not split a consecutive mesh group while it inherits sorting. All material slots of a source stay together. The package resets depth before and after each group, preserving color and stencil; it does not disable the source material's depth writes. No group IDs are required. Place separate effect roots between UI elements to interleave them; source collection does not move an individual source into an authored UI sibling position.

**Automatic sizing:** Screen Space - Camera uses the Canvas camera; **Reference camera** can override it. Overlay can also use a reference camera. Camera-based sizing accounts for projection, viewport height and Canvas scaling; perspective sizing uses the effect root's depth. Without a reference camera, Overlay uses the Canvas **Reference Pixels Per Unit** convention (normally 100), not a measured camera conversion. Screen-space Automatic controls the effect root's scale; use **Size multiplier** to resize the output. The Inspector shows the calculated **UI units / world unit**.

Existing components retain **Manual**, their saved **Effect scale**, and **Canvas scaling**. Selecting **Automatic** resets **Size multiplier** to 1. A Sprite's own pixels-per-unit still defines its source geometry; automatic sizing then converts that geometry to UI units.

**Particle placement:** **Particle zoom origin > Emitter** (the default for new components) keeps each emitter at its Transform position while scaling its particles. **Effect** also scales emitter offsets around the effect root. Existing components retain their saved setting; select **Emitter** if moving a child emitter produces exaggerated or reduced movement. World-space particles still retain their native simulation history.

**Renderer sources:** the Inspector lists meshes, skinned meshes, sprites, lines and trails separately, including inactive or unsupported sources with their status. Enable the corresponding **Include** option and click **Collect sources** after adding a source or fixing its setup. Sources below a nested `UIEffectRenderer` belong to that component.

**MeshRenderer / SkinnedMeshRenderer:** assign the source mesh and UI-compatible materials to every slot. For imported static meshes, enable **Read/Write** in the model's import settings and click **Apply**. Skinned meshes use their bones and blend shapes through `BakeMesh`. Multiple submeshes/materials are supported. Missing vertex colors become opaque white in the UI output; existing colors and the original mesh asset are preserved.

Mesh outputs preserve depth test/write, culling and blending. Only the runtime Canvas material copy uses the UI render queue (3000), keeping mesh groups, depth resets and ordinary UI in Canvas order. Source material assets retain their original queues. Output bounds retain their full 3D extent for camera culling.

**SpriteRenderer:** place the source under `UIEffectRenderer`, enable **Include sprites**, assign a sprite and a UI-compatible material (for example, a material using **UI/Default**), then click **Collect sources**. The bridge preserves sprite color, flip and Simple/Sliced/Tiled geometry. Start with **Automatic** and **Size multiplier = 1**.

Mesh and skinned-mesh parts scale together in the `UIEffectRenderer` frame, including their child offsets, so an assembled model keeps its shape. Move the effect's RectTransform to position the whole model in UI; child transforms remain model-space transforms. Use separate effect roots for independently placed models. Sprites and local-space lines scale around their own Transform origins.

**LineRenderer / TrailRenderer:** enable **Include lines and trails** and assign exactly one UI-compatible material. Lines need at least two positions and a nonzero width; trails need recorded points and a nonzero width curve. Use a local-space line for geometry authored in effect units. World-space lines and trails already contain world coordinates: **Automatic** preserves their positions and widths without another unit conversion. An explicit **Size multiplier** zooms these paths around the effect root; keep it at 1 to follow the recorded positions. **Manual** retains its existing path scaling. Trails keep sampling while hidden and resume scheduled baking even during long frames; malformed snapshots are still rejected.

After adding or removing sources at runtime, call `RefreshSources()`. Call `MarkGeometryDirty()` to invalidate cached geometry after external edits.

```csharp
using ShanFlyer.UIEffects;

var effect = GetComponent<UIEffectRenderer>();
effect.unitConversion = UIEffectRenderer.UnitConversion.Automatic;
effect.uniformScale = 1f; // Multiplier after automatic unit conversion.
effect.Play();

// Global override: update on 50% of actual frames, not at 50 Hz.
UIEffectRenderer.updateRatePercent = 50;
UIEffectRenderer.staggerBaking = true;
```

## Performance settings

Configure **Project Settings > UI Effects HP**. Defaults are **100% Update Rate**, merging enabled, paused-particle caching enabled, **Skip Rendering** for hidden outputs, and **Fast Binding** disabled. Staggering is enabled and takes effect below 100%. Particle sharing is allowed globally, but components default to **Independent**. Shared instances reuse the same particle playback; other Renderer types update separately.

New components enable **Source sorting** by default: outputs within the component follow the source **Sorting Layer**, **Order in Layer**, then hierarchy order. Consecutive compatible particles can merge after sorting; another Renderer or an incompatible particle breaks the run. Existing components retain their saved setting. Consecutive meshes still use depth occlusion within their group, and the component's position among ordinary UI elements follows the Canvas hierarchy.

**Merge Particle Outputs** allows adaptive merging rather than forcing it. The planner uses normal bake results to estimate copy workload, output savings and bounds growth, and periodically rechecks the decision with hysteresis. Empty, hidden, oversized or widely separated outputs stay separate. No extra probe bakes are performed, and individual systems still bake separately. Shared instances merge only with matching particle layouts; otherwise they keep separate shared slots. This is a conservative workload estimate, not a measured guarantee of lower frame time.

## Particle shader generation

Open **Project Settings > UI Effects HP > Generate Particle Shaders...**. Choose **Built-in** or **URP**, then click **Generate Shaders**. A pipeline mismatch prompts **Generate Anyway / Cancel**.

The generator obtains Unity shader source and writes stencil-enabled copies without Unity scene fog to `Assets/UIEffectsGenerated/ParticleShaders`. Repeated generation creates a new folder. Assign the generated shaders to your materials; generation does not change materials, install packages or switch pipelines. URP copies require the corresponding URP package to compile.

UGUI **Mask** requires stencil properties and a drawing pass that uses them. **RectMask2D** requires separate clipping code, which the generator does not add. Original shading and vertex-stream requirements still apply. See [Shader setup and examples](Documentation~/ShaderIntegration.md).

## Supported source requirements

MeshRenderer requires a MeshFilter and a readable mesh with triangle submeshes. Standalone LineRenderer and TrailRenderer require exactly one non-null material. Unsupported sources retain native rendering. SpriteSkin deformation and 2D lighting are not supported.
