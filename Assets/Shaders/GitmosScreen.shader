Shader "Gitmos/Screen"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "black" {}
        _HotColor ("Hot", Color) = (1, 0.75, 0.95, 1)
        _Alpha ("Alpha", Range(0, 1)) = 1
        _Boost ("Boost", Float) = 2
        _Hot ("Hot", Float) = 1.2
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _HotColor;
                float _Alpha;
                float _Boost;
                float _Hot;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input, float facing : VFACE) : SV_Target
            {
                float2 uv = input.uv;
                if (facing < 0)
                    uv.x = 1.0 - uv.x;
                half3 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
                float dx = uv.x - 0.5;
                float dy = uv.y - 0.5;
                float hot = exp(-(dx * dx * 5.5 + dy * dy * 14.0)) * _Hot;
                half3 col = tex * _Boost + _HotColor.rgb * hot;
                col *= _Alpha;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
