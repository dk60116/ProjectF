Shader "ProjectF/Farmland Surface"
{
    Properties
    {
        _BaseMap("Base Texture", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (0.88, 0.72, 0.56, 0.94)
        _EdgeFeather("Edge Feather", Range(0.01, 0.35)) = 0.16
        _NeighborMask("Neighbor Mask", Vector) = (0, 0, 0, 0)
        _DiagonalMask("Diagonal Mask", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-20"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _EdgeFeather;
                float4 _NeighborMask;
                float4 _DiagonalMask;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = NormalizeNormalPerVertex(normalInputs.normalWS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 textureUv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                half4 soilTexture = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, textureUv);

                float irregularity = (soilTexture.r - 0.5) * 0.055;
                float feather = max(_EdgeFeather, 0.001);
                float leftAlpha = lerp(
                    smoothstep(0.0, feather, input.uv.x + irregularity),
                    1.0,
                    step(0.5, _NeighborMask.x));
                float rightAlpha = lerp(
                    smoothstep(0.0, feather, 1.0 - input.uv.x + irregularity),
                    1.0,
                    step(0.5, _NeighborMask.y));
                float bottomAlpha = lerp(
                    smoothstep(0.0, feather, input.uv.y + irregularity),
                    1.0,
                    step(0.5, _NeighborMask.z));
                float topAlpha = lerp(
                    smoothstep(0.0, feather, 1.0 - input.uv.y + irregularity),
                    1.0,
                    step(0.5, _NeighborMask.w));
                float edgeAlpha = leftAlpha * rightAlpha * bottomAlpha * topAlpha;

                float4 connected = step(0.5, _NeighborMask);
                float4 diagonal = step(0.5, _DiagonalMask);
                float4 concaveCorner = float4(
                    connected.x * connected.z * (1.0 - diagonal.x),
                    connected.y * connected.z * (1.0 - diagonal.y),
                    connected.x * connected.w * (1.0 - diagonal.z),
                    connected.y * connected.w * (1.0 - diagonal.w));
                float cornerFeather = feather * 0.72;
                float4 cornerAlpha = smoothstep(
                    0.0,
                    cornerFeather,
                    float4(
                        length(input.uv),
                        length(float2(1.0 - input.uv.x, input.uv.y)),
                        length(float2(input.uv.x, 1.0 - input.uv.y)),
                        length(1.0 - input.uv)));
                edgeAlpha *= lerp(1.0, cornerAlpha.x, concaveCorner.x);
                edgeAlpha *= lerp(1.0, cornerAlpha.y, concaveCorner.y);
                edgeAlpha *= lerp(1.0, cornerAlpha.z, concaveCorner.z);
                edgeAlpha *= lerp(1.0, cornerAlpha.w, concaveCorner.w);

                float waviness = sin(input.uv.y * 19.0) * 0.018;
                float furrowWave = abs(sin((input.uv.x + waviness) * 15.707963));
                float furrow = smoothstep(0.70, 0.98, furrowWave);
                half3 soilColor = soilTexture.rgb * _BaseColor.rgb;
                soilColor *= lerp(1.04, 0.78, furrow * 0.58);

                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                half3 lighting = SampleSH(normalWS);
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half mainNdotL = saturate(dot(normalWS, mainLight.direction));
                lighting += mainLight.color
                            * mainNdotL
                            * mainLight.distanceAttenuation
                            * mainLight.shadowAttenuation;

#if defined(_ADDITIONAL_LIGHTS) || defined(_CLUSTER_LIGHT_LOOP)
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, input.positionWS);
                    half additionalNdotL = saturate(dot(normalWS, light.direction));
                    lighting += light.color
                                * additionalNdotL
                                * light.distanceAttenuation
                                * light.shadowAttenuation;
                LIGHT_LOOP_END
#endif
                soilColor *= lighting;

                half alpha = soilTexture.a * _BaseColor.a * edgeAlpha;
                clip(alpha - 0.015);
                return half4(soilColor, alpha);
            }
            ENDHLSL
        }
    }
}
