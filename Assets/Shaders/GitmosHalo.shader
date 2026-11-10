Shader "Gitmos/Halo"
{
    Properties
    {
        _Color ("Color", Color) = (0.32, 0.36, 0.95, 1)
        _Inner ("Globe Radius / Shell Radius", Range(0.1, 0.99)) = 0.85
        _Falloff ("Falloff", Float) = 2.6
        _InnerGlow ("Inner Glow", Float) = 0.45
        _Alpha ("Alpha", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-10"
            "RenderPipeline" = "UniversalPipeline"
        }
        Blend One One
        ZWrite Off
        ZTest Always
        Cull Front
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Inner;
                float _Falloff;
                float _InnerGlow;
                float _Alpha;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Distance of the view ray from the centre, in shell radii.
                float3 cam = TransformWorldToObject(_WorldSpaceCameraPos);
                float3 dir = normalize(input.positionOS - cam);
                float3 closest = cam - dot(cam, dir) * dir;
                float d = length(closest) / 0.5;

                float glow;
                if (d < _Inner)
                {
                    float x = d / _Inner;
                    glow = pow(x, 7.0) * _InnerGlow;
                }
                else
                {
                    float t = saturate((d - _Inner) / (1.0 - _Inner));
                    glow = pow(1.0 - t, _Falloff);
                }
                return half4(_Color.rgb * glow * _Alpha, 1);
            }
            ENDHLSL
        }
    }
}
