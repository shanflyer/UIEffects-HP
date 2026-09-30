# UI shader integration

Rendering through Canvas does not automatically make a material support UI masks. Stencil properties are required for UGUI `Mask`, but declaring them without using them in the rendered pass does not enable masking. `RectMask2D` needs separate shader clipping support.

| Feature | Shader requirement |
| --- | --- |
| Canvas drawing and hierarchy order | A pass compatible with the submitted geometry, vertex streams and UI render state. Stencil is not required merely to display an object. |
| UGUI `Mask`, including nested masks | The stencil properties and render state shown below, plus `_ColorMask`. Keep **UGUI maskable** enabled on the effect. |
| `RectMask2D` | `UNITY_UI_CLIP_RECT`, `_ClipRect` and fragment coverage/clipping. Soft edges additionally use `_UIMaskSoftnessX/Y`. |
| Vertex tint and `CanvasGroup` fading | Consume the UI vertex color and alpha, with a blend mode that respects the resulting alpha. |
| Particle/Sprite `SpriteMask` bridge | The stencil contract below; stencil-enabled particle shader copies are recognized automatically. Other custom shaders must be registered with `UIEffectSpriteMask`. |

Canvas submission does not add missing shader features. Each material slot, including a particle's Trail material, must support the UI features it uses. Native lighting and Renderer-specific shader inputs are not automatically reproduced by adding stencil support.

## Mesh depth and Canvas support

Stencil is not required for an unmasked mesh. Ordinary materials can draw through Canvas, including textureless materials. Keep the source mesh material's depth test, depth writes, face culling and blending: disabling depth writes can break self-occlusion and occlusion between submeshes. Transparent materials retain their existing transparency/sorting limitations.

Mesh submission copies use the UI render queue (3000). This runtime-only change keeps meshes, depth resets and ordinary UI in the same queue so that Camera rendering can respect Canvas order. Source material assets retain their original queues; depth tests, depth writes, culling, blending and surface properties are preserved. Mesh bounds retain their complete 3D volume; clipping rectangles are calculated from that volume instead of flattening it. Depth-reset bounds include the mesh group's volume and the Canvas plane.

Consecutive mesh and skinned-mesh outputs form an automatic depth group. Depth-only helper draws reset depth before the first and after the last output, without changing color or stencil. Within the group, depth-writing surfaces occlude one another by geometry depth. Between groups and ordinary UI, Canvas draw order applies. Other effect types, authored nested Canvas and active Mask/RectMask2D boundaries separate groups; the automatically added channel-isolation Canvas preserves consecutive mesh groups; no numeric group setting is exposed. These helpers use a dedicated shader and do not modify source shaders or materials. They bypass RectMask2D and keep all stencil bits; the visible mesh itself still needs the normal masking contract.

Supported canvases are Screen Space - Overlay and Screen Space - Camera with an assigned, dedicated UI camera. Start the UI camera with cleared depth, render it after the scene, and do not require its discarded depth in subsequent passes/cameras. Shared scene/UI cameras and scene-depth preservation are outside this contract. World Space bypasses the bridge and restores native sources. Game and Scene views still use their own projections and lighting; this feature does not reproduce missing Renderer-specific shader inputs.

## Materials and lighting

Assign materials directly to the source Renderers. There is no material, shader or prefab conversion step. Canvas uses the assigned shader without automatic substitution or pass rewriting. Mesh submission only changes the render queue on a temporary material copy, as described above. Source material properties and shader assets remain unchanged.

| Canvas mode | Lighting setup |
| --- | --- |
| Screen Space - Overlay | Use Unlit or custom UI lighting. Standard Lit is not supported by this package in Overlay. Custom lighting must account for the UI coordinate and projection setup. |
| Screen Space - Camera | Lit is supported. Enable **Additional Shader Channels = Everything**: TexCoord1, TexCoord2, TexCoord3, Normal and Tangent. Mesh outputs enable these channels automatically only on the local nested Canvas attached to UIEffectRenderer; parent/root Canvas channels stay unchanged. |

Place UIEffectRenderer below the root Canvas. It adds a local Canvas with **Override Sorting** disabled, preserving parent masking and hierarchy order. An existing local Canvas is reused without changing its sorting settings. Extra channels increase vertex data size; local isolation avoids widening unrelated UI vertex buffers, but separate canvases can increase draw calls by preventing cross-Canvas batching. Existing parent channel settings are preserved, not automatically reduced. Removing UIEffectRenderer removes only the Canvas it generated.

A simple custom shader may use fewer channels, but full channels are the supported default for Camera-mode meshes. Missing channels can produce incorrect lighting. Canvas channels carry mesh attributes; they do not create missing mesh data or guarantee every Renderer-specific lighting feature for every custom shader.

In the reported Unity 6.6 / URP / Direct3D 12 setup, selecting a UI mesh draw using Unity's default URP/Lit shader in Frame Debugger crashes the Editor. The cause is unresolved; shader-specific behavior is a hypothesis, not a confirmed diagnosis. Custom lighting shaders still need separate verification. This issue does not establish that every shader pass is executed.

## Generate Unity particle shaders

Open **Project Settings → UI Effects HP → Generate Particle Shaders...**, or **Tools → UI Effects HP → Generate Particle Shaders**. Select **Built-in** or **URP** and click **Generate Shaders**. A different active pipeline prompts **Generate Anyway / Cancel**; it does not prevent generation.

The generator obtains Unity's shader source automatically, adds stencil properties and render state, and writes copies to `Assets/UIEffectsGenerated/ParticleShaders`. Built-in source matches the editor version; URP source comes from the installed package, the editor's bundled package, or Unity's Graphics repository branch for the editor release (2022.3 uses URP 14; Unity 6 uses URP 17). Repeated generation creates a new folder. The source shaders, materials, packages and pipeline settings are unchanged.

Assign the generated shaders to your own materials. Their original vertex stream requirements, depth and blend settings remain in effect, with Unity scene fog disabled. This operation adds stencil support, not `RectMask2D` clipping or replacement lighting. URP copies still require the corresponding URP package to compile and render; files are generated even when that dependency is absent. Each output includes the Unity source license and generation report.

## Working example

Use **ShanFlyer/UI Effects/Additive**, implemented in [UIAdditive.shader](../Shaders/UIAdditive.shader). The ready-to-use material is [UIAdditive.mat](../Shaders/UIAdditive.mat).

1. Assign that material, or a copy with your texture, to the source Renderer. Assign it to each relevant material slot and to the particle Trail material when trails are enabled.
2. Place the effect under `Canvas → Panel → Effect` and add `UIEffectRenderer` to Effect. Keep **UGUI maskable** enabled.
3. Add `Image` and `Mask` to Panel. Moving the effect across the panel edge should clip it to the Image shape. Nest another masked panel to check nested masks.
4. Replace the panel's `Mask` with `RectMask2D` to check rectangular clipping and Softness. Add `CanvasGroup` to Panel and vary Alpha to check fading.

This shader is additive. For regular transparency, Unity's `UI/Default` is another compatible starting material; neither preserves an unrelated shader's lighting or special effects automatically.

## Adding stencil support to a custom shader

Add these entries to its existing `Properties` block:

```shaderlab
_Stencil ("Stencil Reference", Float) = 0
_StencilComp ("Stencil Comparison", Float) = 8
_StencilOp ("Stencil Pass", Float) = 0
_StencilReadMask ("Stencil Read Mask", Float) = 255
_StencilWriteMask ("Stencil Write Mask", Float) = 255
_ColorMask ("Color Mask", Float) = 15
[Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("UI Alpha Clip", Float) = 0
```

Make the pass that draws the effect use these values. Place this in its `SubShader` or drawing `Pass`, ensuring the pass does not override it with incompatible state:

```shaderlab
Stencil
{
    Ref [_Stencil]
    Comp [_StencilComp]
    Pass [_StencilOp]
    ReadMask [_StencilReadMask]
    WriteMask [_StencilWriteMask]
    Fail Keep
    ZFail Keep
}
ColorMask [_ColorMask]
```

Keep the existing mesh pass's depth, culling and blending state. For flat transparent sprites/particles, `ZWrite Off` and `ZTest [unity_GUIZTestMode]` are common UI settings; they are not a requirement for opaque meshes. UGUI and the SpriteMask bridge supply stencil values at runtime: do not assign fixed stencil IDs or synchronize these reserved properties through Material Property Synchronization.

## RectMask2D and alpha clipping

Stencil does not implement `RectMask2D`. Add the variants to the drawing program:

```hlsl
#pragma multi_compile_local _ UNITY_UI_CLIP_RECT
#pragma multi_compile_local _ UNITY_UI_ALPHACLIP
```

The vertex program must pass the UI position before projection to the fragment program. It must be in the coordinate space expected by `_ClipRect`, not `SV_POSITION` screen coordinates. For a simple hard rectangle, merge this logic into the existing fragment program after computing its tinted color:

```hlsl
// Declare outside the fragment function.
float4 _ClipRect;

// Inside the fragment function: uiPosition is the interpolated UI position;
// color is the texture/effect result multiplied by the incoming vertex color.
#ifdef UNITY_UI_CLIP_RECT
    float2 inside = min(uiPosition.xy - _ClipRect.xy,
                       _ClipRect.zw - uiPosition.xy);
    color.a *= step(0.0, min(inside.x, inside.y));
#endif
#ifdef UNITY_UI_ALPHACLIP
    clip(color.a - 0.001);
#endif
return color;
```

This snippet assumes straight-alpha or alpha-weighted additive blending. For premultiplied alpha, apply the same coverage to RGB as well. The complete packaged shader shows vertex-color handling, clip coordinates and soft-edge coverage using `_UIMaskSoftnessX/Y`. Keep alpha clipping if the material is also used to write a texture-shaped UGUI Mask.

## Custom shaders with SpriteMask

The packaged Additive shader, Unity `UI/Default` and shaders produced by the generator are recognized automatically. For another shader, verify its drawing pass uses the stencil state above, then register it before enabling the effect:

```csharp
using ShanFlyer.UIEffects;

// uiMaterial is the UI-compatible material assigned to your source Renderer.
UIEffectSpriteMask.RegisterStencilShader(uiMaterial.shader);
```

Registration only declares compatibility; it does not add stencil code or repair a shader. It is required by this package's SpriteMask bridge, not by ordinary UGUI Mask/RectMask2D.

References: [Unity UGUI Mask](https://github.com/Unity-Technologies/uGUI/blob/main/com.unity.ugui/Documentation~/script-Mask.md), [Unity ShaderLab Stencil](https://docs.unity.com/en-us/engine/6000.0/manual/materials-and-shaders/shaders/reference/sl-reference/sl-commands/sl-stencil).
