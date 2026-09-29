Shader "Hidden/ShanFlyer/UIEffects/SpriteMask"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite alpha", 2D) = "white" {}
        _AlphaTex ("Split alpha", 2D) = "white" {}
        _UseAlphaTex ("Use split alpha", Float) = 0
        _Cutoff ("Alpha cutoff", Float) = 0.5
        _Fullscreen ("Clear fullscreen", Float) = 0
        _Stencil ("Reference", Float) = 0
        _StencilReadMask ("Read mask", Float) = 0
        _StencilWriteMask ("Write mask", Float) = 128
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        ColorMask 0
        Stencil
        {
            Ref [_Stencil]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
            Comp Equal
            Pass Replace
            Fail Keep
            ZFail Keep
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex, _AlphaTex;
            float _Cutoff, _Fullscreen, _UseAlphaTex;
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = _Fullscreen > 0.5 ? float4(v.uv * 2 - 1, 0, 1) : UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                if (_Fullscreen < 0.5)
                {
                    fixed a = tex2D(_MainTex, i.uv).a;
                    if (_UseAlphaTex > 0.5) a = tex2D(_AlphaTex, i.uv).r;
                    clip(a - _Cutoff);
                }
                return 0;
            }
            ENDCG
        }
    }
}
