Shader "Gitmos/Fresnel"
{
    Properties
    {
        _BaseColor ("Base", Color) = (0.012, 0.012, 0.022, 1)
        _LandColor ("Land Tint", Color) = (0.2, 0.19, 0.3, 1)
        _CityColor ("City Lights", Color) = (1, 0.78, 0.55, 1)
        _RimColor ("Rim", Color) = (0.35, 0.42, 0.9, 1)
        _LandMap ("Land", 2D) = "black" {}
        _RimPower ("Rim Power", Float) = 3
        _CityBoost ("City Boost", Float) = 3
        _LightDir ("Light Direction", Vector) = (-0.5, 0.5, 0.7, 0)
        _Ambient ("Ambient", Range(0, 1)) = 0.28
        _Alpha ("Alpha", Range(0, 1)) = 1
        _Coverage ("Coverage", Range(0, 1)) = 1
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry+10"
            "RenderPipeline" = "UniversalPipeline"
        }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite [_ZWrite]
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
                float4 _CityColor;
                float4 _RimColor;
                float4 _LandMap_ST;
                float4 _LightDir;
                float _RimPower;
                float _CityBoost;
                float _Ambient;
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
                // atan2 wraps at the date line, so take derivatives from whichever
                // of two offset longitudes is continuous here. That keeps mips seam-free.
                float2 dx = ddx(uv);
                float2 dy = ddy(uv);
                float2 shifted = float2(frac(uv.x + 0.5), uv.y);
                float2 dxs = ddx(shifted);
                float2 dys = ddy(shifted);
                if (abs(dxs.x) + abs(dys.x) < abs(dx.x) + abs(dy.x))
                {
                    dx.x = dxs.x;
                    dy.x = dys.x;
                }
                float3 tex = SAMPLE_TEXTURE2D_GRAD(_LandMap, sampler_LandMap, uv, dx, dy).rgb;

                float3 normalWS = normalize(input.normalWS);
                float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                float facing = saturate(dot(normalWS, viewDir));
                float fres = pow(1.0 - facing, _RimPower);

                // The source map is a blue night earth with warm city lights.
                // Land and ice read as luminance, the warm excess reads as lights.
                float lum = dot(tex, float3(0.3, 0.45, 0.25));
                float warm = saturate((tex.r - tex.b) * 5.0) * saturate(tex.r * 2.2);

                float lambert = saturate(dot(normalWS, normalize(_LightDir.xyz)));
                float shade = _Ambient + (1.0 - _Ambient) * lambert;

                float3 col = _BaseColor.rgb + _LandColor.rgb * lum * 2.6 * shade;
                col += _CityColor.rgb * warm * _CityBoost * (0.55 + 0.45 * (1.0 - lambert));
                col += _RimColor.rgb * fres * (0.35 + 0.65 * shade);

                return half4(col, _Alpha);
            }
            ENDHLSL
        }
    }
}
