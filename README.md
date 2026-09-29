# UI Effects HP

A UGUI effects package for **Unity 2022.3 LTS and Unity 6**. Package version: **1.0.0**.

## Features

- Render ParticleSystem, MeshRenderer, SkinnedMeshRenderer, SpriteRenderer, LineRenderer and TrailRenderer geometry through Canvas.
- Follow UI hierarchy order and support CanvasGroup fading, Mask and RectMask2D with compatible shaders.
- Support multiple submeshes and materials for MeshRenderer and SkinnedMeshRenderer, plus particle trails.
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

1. Place the effect under a Canvas and add `UIEffectRenderer` to its root.
2. Assign UI-compatible materials to every source material slot, including particle trails. The package includes **ShanFlyer/UI Effects/Additive** and `UIAdditive.mat`.
3. Keep **Unit conversion = Automatic** and **Size multiplier = 1** to use automatic sizing. Adjust the multiplier for artistic sizing. The bake view size is calculated automatically.
4. Leave **Simulation role** at **Independent** for separate particle playback. Choose **Automatic** on compatible instances of the same template to share playback and geometry.

Create a ready-to-use example from **GameObject > UI (Canvas) > UI Effects HP > Particle example**.

**Automatic sizing:** Screen Space - Camera uses the Canvas camera; **Reference camera** can override it. Overlay can also use a reference camera. Camera-based sizing accounts for projection, viewport height and Canvas scaling; perspective sizing uses the effect root's depth. Without a reference camera, Overlay uses the Canvas **Reference Pixels Per Unit** convention (normally 100), not a measured camera conversion. World Space keeps native world units and Transform scale. Screen-space Automatic controls the effect root's scale; use **Size multiplier** to resize the output. The Inspector shows the calculated **UI units / world unit**.

Existing components retain **Manual**, their saved **Effect scale**, and **Canvas scaling**. Selecting **Automatic** resets **Size multiplier** to 1. A Sprite's own pixels-per-unit still defines its source geometry; automatic sizing then converts that geometry to UI units.

**Particle placement:** **Particle zoom origin > Emitter** (the default for new components) keeps each emitter at its Transform position while scaling its particles. **Effect** also scales emitter offsets around the effect root. Existing components retain their saved setting; select **Emitter** if moving a child emitter produces exaggerated or reduced movement. World-space particles still retain their native simulation history.

**SpriteRenderer:** place the source under `UIEffectRenderer`, enable **Sprite sources**, assign a sprite and a UI-compatible material (for example, a material using **UI/Default**), then click **Collect sources**. The bridge preserves sprite color, flip and Simple/Sliced/Tiled geometry. Start with **Automatic** and **Size multiplier = 1**.

Mesh, skinned mesh, sprite and local-space line geometry scales around each source's Transform origin; output sizing does not multiply the source's placement offset. World-space lines and trails keep their world-space points and scale around the effect root.

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

## Particle shader generation

Open **Project Settings > UI Effects HP > Generate Particle Shaders...**. Choose **Built-in** or **URP**, then click **Generate Shaders**. A pipeline mismatch prompts **Generate Anyway / Cancel**.

The generator obtains Unity shader source and writes stencil-enabled copies to `Assets/UIEffectsGenerated/ParticleShaders`. Repeated generation creates a new folder. Assign the generated shaders to your materials; generation does not change materials, install packages or switch pipelines. URP copies require the corresponding URP package to compile.

UGUI **Mask** requires stencil properties and a drawing pass that uses them. **RectMask2D** requires separate clipping code, which the generator does not add. Original shading and vertex-stream requirements still apply. See [Shader setup and examples](Documentation~/ShaderIntegration.md).

## Supported source requirements

MeshRenderer requires a MeshFilter and a readable mesh with triangle submeshes. Standalone LineRenderer and TrailRenderer require exactly one non-null material. Unsupported sources retain native rendering. SpriteSkin deformation and 2D lighting are not supported.
