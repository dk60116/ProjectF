Shader "ProjectF/Terrain/WaterFoamOverlay"
{
    Properties
    {
        _FoamColor("Foam Color", Color) = (0.72, 0.9, 1, 0)
        _NoiseScale("Noise Scale", Float) = 5.5
        _NoiseStrength("Noise Strength", Range(0, 1)) = 0.42
        _FlowSpeed("Flow Speed", Float) = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "ForwardUnlit"

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half4 color : COLOR;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _FoamColor;
                half _NoiseScale;
                half _NoiseStrength;
                half _FlowSpeed;
            CBUFFER_END

            half StableFoamNoise(float2 value)
            {
                half waveA = 0.5h + 0.5h * sin(value.x + value.y * 0.37h);
                half waveB = 0.5h + 0.5h * sin(value.x * 0.41h - value.y * 0.83h + 1.7h);
                return saturate((waveA * 0.65h) + (waveB * 0.35h));
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.uv = input.uv;
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half edgeFade = saturate(input.color.a);
                float2 noiseUV = (input.positionWS.xz + float2(_Time.y * _FlowSpeed, -_Time.y * _FlowSpeed * 0.37)) * _NoiseScale;
                half noise = StableFoamNoise(noiseUV + input.uv.xx * 0.23h);
                half brokenFoam = lerp(1.0h, lerp(0.82h, 1.08h, noise), _NoiseStrength);
                half alpha = saturate(edgeFade * _FoamColor.a * brokenFoam);

                half3 color = MixFog(_FoamColor.rgb * input.color.rgb, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
