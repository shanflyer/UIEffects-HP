Shader "Hidden/ShanFlyer/UIEffects/DepthReset"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZTest Always
        ZWrite On
        ColorMask 0
        // Never consume or change the stencil bits owned by UGUI / SpriteMask.
        Stencil { Comp Always Pass Keep Fail Keep ZFail Keep WriteMask 0 }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f { float4 vertex : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // An ordered depth-only draw, not a CPU-side render-target clear.
                // UVs are independent of effect transforms and CanvasGroup opacity.
                #if defined(UNITY_REVERSED_Z)
                    const float farDepth = 0;
                #else
                    const float farDepth = 1;
                #endif
                o.vertex = float4(v.uv * 2 - 1, farDepth, 1);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target { return 0; }
            ENDCG
        }
    }
}
