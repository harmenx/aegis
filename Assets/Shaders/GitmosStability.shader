Shader "Gitmos/Stability"
{
    Properties
    {
        _ColorA ("A", Color) = (0.25, 0.85, 1, 1)
        _ColorB ("B", Color) = (0.85, 0.35, 1, 1)
        _SpineColor ("Spine", Color) = (0.85, 1, 1, 1)
        _Alpha ("Alpha", Range(0, 1)) = 1
        _LinesX ("Lines X", Float) = 48
        _LinesY ("Lines Y", Float) = 10
        _Fill ("Fill", Float) = 0.06
        _Boost ("Boost", Float) = 1.6
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
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorA;
                float4 _ColorB;
                float4 _SpineColor;
                float _Alpha;
                float _LinesX;
                float _LinesY;
                float _Fill;
                float _Boost;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float ax = min(frac(input.uv.x * _LinesX), 1.0 - frac(input.uv.x * _LinesX));
                float ay = min(frac(input.uv.y * _LinesY), 1.0 - frac(input.uv.y * _LinesY));
                float wire = saturate(1.0 - min(ax, ay) * 18.0);
                wire = pow(wire, 3.0);

                float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                float ndv = abs(dot(normalize(input.normalWS), viewDir));
                float fres = pow(1.0 - ndv, 2.2);
                float spine = exp(-pow((input.uv.y - 0.5) * 7.0, 2.0));

                float3 grad = lerp(_ColorA.rgb, _ColorB.rgb, input.uv.x);
                grad = lerp(grad, _SpineColor.rgb, spine * 0.65);
                float intensity = (_Fill + wire * 0.85 + fres * 0.45 + spine * 0.25) * input.color.a;
                float3 col = grad * intensity * _Boost * _Alpha;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
