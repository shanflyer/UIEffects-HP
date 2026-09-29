Shader "ShanFlyer/UI Effects/Additive"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _ColorMask ("Output channels", Float) = 15
        _Stencil ("Stencil reference", Float) = 0
        _StencilComp ("Stencil comparison", Float) = 8
        _StencilOp ("Stencil pass", Float) = 0
        _StencilReadMask ("Stencil read bits", Float) = 255
        _StencilWriteMask ("Stencil write bits", Float) = 255
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Discard transparent pixels", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha One, One One
        ColorMask [_ColorMask]
        Stencil
        {
            Ref [_Stencil]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
            Comp [_StencilComp]
            Pass [_StencilOp]
        }
        Pass
        {
            Name "CanvasAdditive"
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex CanvasVertex
            #pragma fragment AdditivePixel
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST, _ClipRect;
            half4 _Color, _TextureSampleAdd;
            float _UIMaskSoftnessX, _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            struct CanvasInput
            {
                float4 position : POSITION;
                float2 uv : TEXCOORD0;
                half4 tint : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct PixelInput
            {
                float4 position : SV_POSITION;
                float4 coordinates : TEXCOORD0;
                half4 tint : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            PixelInput CanvasVertex(CanvasInput source)
            {
                PixelInput pixel;
                UNITY_SETUP_INSTANCE_ID(source);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(pixel);
                pixel.position = UnityObjectToClipPos(source.position);
                pixel.coordinates = float4(source.uv * _MainTex_ST.xy + _MainTex_ST.zw, source.position.xy);
                #ifndef UNITY_COLORSPACE_GAMMA
                if (_UIVertexColorAlwaysGammaSpace != 0)
                    source.tint.rgb = GammaToLinearSpace(source.tint.rgb);
                #endif
                pixel.tint = source.tint * _Color;
                return pixel;
            }
            half RectangleCoverage(float2 canvasPosition)
            {
                float2 inside = min(canvasPosition - _ClipRect.xy, _ClipRect.zw - canvasPosition);
                // Derivatives express the local size of a screen pixel, including perspective.
                float2 footprint = max(fwidth(canvasPosition), float2(0.000001, 0.000001));
                float2 feather = footprint + max(float2(_UIMaskSoftnessX, _UIMaskSoftnessY), 0) * 0.5;
                float2 coverage = saturate(inside / feather);
                return coverage.x * coverage.y;
            }
            half4 AdditivePixel(PixelInput pixel) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(pixel);
                half4 texel = tex2D(_MainTex, pixel.coordinates.xy) + _TextureSampleAdd;
                half4 output = texel * pixel.tint;
                #ifdef UNITY_UI_CLIP_RECT
                output.a *= RectangleCoverage(pixel.coordinates.zw);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(output.a - 0.001);
                #endif
                return output;
            }
            ENDCG
        }
    }
}
