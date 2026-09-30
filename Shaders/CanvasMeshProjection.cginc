#ifndef UI_EFFECTS_CANVAS_MESH_PROJECTION_INCLUDED
#define UI_EFFECTS_CANVAS_MESH_PROJECTION_INCLUDED

#include "Packages/com.shanflyer.ui-effects/Shaders/CanvasNoFog.cginc"

#if defined(UI_EFFECTS_URP_PROJECTION)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#else
#include "UnityCG.cginc"
#endif

// UGUI sets this per draw: Always for Overlay, LEqual for camera geometry.
// Overlay's orthographic depth direction is opposite to a camera looking along
// +Z. Correct clip depth only; keep the model, normals and lighting positions.
float unity_GUIZTestMode;
float4 UIEffectsCanvasClipDepth(float4 positionCS)
{
    if (unity_GUIZTestMode > 7.5)
    {
#if defined(UNITY_REVERSED_Z)
        positionCS.z = positionCS.w - positionCS.z;
#else
        positionCS.z = (1.0 + UNITY_NEAR_CLIP_VALUE) * positionCS.w - positionCS.z;
#endif
    }
    return positionCS;
}

#if defined(UI_EFFECTS_URP_PROJECTION)
VertexPositionInputs UIEffectsCanvasVertexPosition(float3 positionOS)
{
    VertexPositionInputs result = GetVertexPositionInputs(positionOS);
    result.positionCS = UIEffectsCanvasClipDepth(result.positionCS);
    result.positionNDC.z = result.positionCS.z;
    return result;
}
float4 UIEffectsCanvasObjectToClip(float3 positionOS)
{
    return UIEffectsCanvasClipDepth(TransformObjectToHClip(positionOS));
}
float4 UIEffectsCanvasWorldToClip(float3 positionWS)
{
    return UIEffectsCanvasClipDepth(TransformWorldToHClip(positionWS));
}
#define GetVertexPositionInputs UIEffectsCanvasVertexPosition
#define TransformObjectToHClip UIEffectsCanvasObjectToClip
#define TransformWorldToHClip UIEffectsCanvasWorldToClip
#else
float4 UIEffectsCanvasObjectToClip(float3 positionOS)
{
    return UIEffectsCanvasClipDepth(UnityObjectToClipPos(positionOS));
}
float4 UIEffectsCanvasObjectToClip(float4 positionOS)
{
    return UIEffectsCanvasClipDepth(UnityObjectToClipPos(positionOS));
}
#define UnityObjectToClipPos UIEffectsCanvasObjectToClip
#endif
#endif
