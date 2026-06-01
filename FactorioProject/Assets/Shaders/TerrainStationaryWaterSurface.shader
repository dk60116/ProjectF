Shader "ProjectF/Terrain/StationaryWaterSurface"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.18, 0.56, 0.72, 0.82)
        _DeepColor("Deep Color", Color) = (0.06, 0.28, 0.44, 0.82)
        _NormalStrength("Normal Strength", Range(0, 1)) = 0.26
        _RippleScaleA("Ripple Scale A", Float) = 1.35
        _RippleScaleB("Ripple Scale B", Float) = 2.15
        _RippleSpeedA("Ripple Speed A", Float) = 1.10
        _RippleSpeedB("Ripple Speed B", Float) = 1.65
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
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD3;
#endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _DeepColor;
                half _NormalStrength;
                half _RippleScaleA;
                half _RippleScaleB;
                half _RippleSpeedA;
                half _RippleSpeedB;
            CBUFFER_END

            float StationaryWaveHeight(float2 worldXZ, float time)
            {
                float2 waveA = float2(0.82, 0.57);
                float2 waveB = float2(-0.39, 0.92);
                float2 waveC = float2(0.18, -0.98);

                float standingA = sin(dot(worldXZ, waveA) * _RippleScaleA + 0.43) * sin(time * _RippleSpeedA);
                float standingB = sin(dot(worldXZ, waveB) * _RippleScaleB + 1.71) * sin(time * _RippleSpeedB + 0.85);
                float standingC = sin(dot(worldXZ, waveC) * (_RippleScaleA + _RippleScaleB) * 0.62 + 2.29)
                    * sin(time * (_RippleSpeedA + _RippleSpeedB) * 0.53 + 1.35);

                return (standingA * 0.52) + (standingB * 0.34) + (standingC * 0.14);
            }

            half3 GetStationaryWaterNormal(float3 positionWS, half3 meshNormalWS)
            {
                float time = _Time.y;
                float2 worldXZ = positionWS.xz;
                float sampleDistance = 0.035;
                float heightX1 = StationaryWaveHeight(worldXZ + float2(sampleDistance, 0), time);
                float heightX0 = StationaryWaveHeight(worldXZ - float2(sampleDistance, 0), time);
                float heightZ1 = StationaryWaveHeight(worldXZ + float2(0, sampleDistance), time);
                float heightZ0 = StationaryWaveHeight(worldXZ - float2(0, sampleDistance), time);
                float dX = (heightX1 - heightX0) / (sampleDistance * 2.0);
                float dZ = (heightZ1 - heightZ0) / (sampleDistance * 2.0);

                half3 rippleNormal = normalize(half3(-dX * _NormalStrength, 1.0h, -dZ * _NormalStrength));
                return normalize(lerp(meshNormalWS, rippleNormal, saturate(_NormalStrength)));
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = NormalizeNormalPerVertex(normalInput.normalWS);
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                output.shadowCoord = GetShadowCoord(vertexInput);
#endif
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 normalWS = GetStationaryWaterNormal(input.positionWS, normalize(input.normalWS));

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                Light mainLight = GetMainLight(input.shadowCoord);
#else
                Light mainLight = GetMainLight();
#endif
                half3 lightDirectionWS = mainLight.direction;

                float waterNoise = StationaryWaveHeight(input.positionWS.xz * 0.58, _Time.y * 0.35);
                half waterBlend = saturate(0.5h + (waterNoise * 0.28h));
                half3 baseColor = lerp(_DeepColor.rgb, _BaseColor.rgb, waterBlend);

                half lightFacing = saturate(dot(normalWS, lightDirectionWS) * 0.35h + 0.65h);
                half shadowAttenuation = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half3 color = baseColor * lerp(0.78h, 1.05h, lightFacing * shadowAttenuation);
                color = MixFog(color, input.fogFactor);

                return half4(color, _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
