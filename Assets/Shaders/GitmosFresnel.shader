Shader "Gitmos/Fresnel"
{
    Properties
    {
        _BaseColor ("Base", Color) = (0.012, 0.016, 0.028, 1)
        _LandColor ("Land", Color) = (0.055, 0.07, 0.1, 1)
        _RimColor ("Rim", Color) = (0.35, 0.62, 1, 1)
        _LandMap ("Land", 2D) = "black" {}
        _RimPower ("Rim Power", Float) = 2.4
        _Alpha ("Alpha", Range(0, 1)) = 1
        _Coverage ("Coverage", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 normalOS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            TEXTURE2D(_LandMap);
            SAMPLER(sampler_LandMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _LandColor;
                float4 _RimColor;
                float4 _LandMap_ST;
                float _RimPower;
                float _Alpha;
                float _Coverage;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.normalOS = input.normalOS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalOS);
                float lat = asin(clamp(n.y, -1.0, 1.0));
                float lon = atan2(n.x, n.z);
                float2 uv = float2(lon / (2.0 * PI) + 0.5, lat / PI + 0.5);
                float3 mapped = SAMPLE_TEXTURE2D(_LandMap, sampler_LandMap, uv).rgb;
                float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                float fres = pow(1.0 - saturate(dot(normalize(input.normalWS), viewDir)), _RimPower);
                float3 col = lerp(_BaseColor.rgb, mapped * 1.65, _Coverage);
                col += _RimColor.rgb * fres * lerp(1.0, 0.28, _Coverage);
                float cover = lerp(saturate(fres), 1.0, _Coverage);
                return half4(col, _Alpha * cover);
            }
            ENDHLSL
        }
    }
}
