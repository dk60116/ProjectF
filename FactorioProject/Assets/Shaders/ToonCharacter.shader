Shader "Custom/ToonCharacter"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        _ShadowColor("Shadow Color", Color) = (0.7,0.7,0.75,1)
        _ShadeThreshold("Shade Threshold", Range(0,1)) = 0.5
        _ShadeSmoothness("Shade Smoothness", Range(0.001,0.5)) = 0.05
        [ToggleUI] _UseSpecular("Use Specular", Float) = 0
        [HDR] _SpecularColor("Specular Color", Color) = (1,1,1,1)
        _SpecularIntensity("Specular Intensity", Range(0,2)) = 0.5
        _SpecularPower("Specular Power", Range(1,128)) = 32
        _SpecularThreshold("Specular Threshold", Range(0,1)) = 0.5
        _SpecularSmoothness("Specular Smoothness", Range(0.001,0.5)) = 0.05
        _Surface("__surface", Float) = 0.0
        _Blend("__blend", Float) = 0.0
        _Cull("__cull", Float) = 2.0
        [ToggleUI] _AlphaClip("Alpha Cutout", Float) = 0.0
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [ToggleUI] _UseBlackCutout("Black Texture Cutout", Float) = 0.0
        _BlackCutoutThreshold("Black Cutout Threshold", Range(0.0, 1.0)) = 0.02
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _BlendModePreserveSpecular("_BlendModePreserveSpecular", Float) = 1.0
        [HideInInspector] _AlphaToMask("__alphaToMask", Float) = 0.0
        [ToggleUI] _ReceiveShadows("Receive Shadows", Float) = 1.0
        _QueueOffset("Queue offset", Float) = 0.0
        _DepthOffsetFactor("Depth Offset Factor", Float) = -1.0
        _DepthOffsetUnits("Depth Offset Units", Float) = -1.0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 4.0
        _VertexNormalOffset("Vertex Normal Offset", Float) = 0.0
        [ToggleUI] _BlueprintPreview("Blueprint Preview", Float) = 0.0
        _BlueprintTint("Blueprint Tint", Color) = (0.45, 0.95, 1, 1)
        _BlueprintBrightness("Blueprint Brightness", Range(0.5, 2.0)) = 1.8
        _BlueprintMinBrightness("Blueprint Min Brightness", Range(0.0, 1.0)) = 0.42
        _BlueprintContrast("Blueprint Contrast", Range(0.5, 5.0)) = 2.65
        _BlueprintAlpha("Blueprint Alpha", Range(0.0, 1.0)) = 0.95
        _BlueprintRimColor("Blueprint Rim Color", Color) = (0.03, 0.12, 0.52, 1)
        _BlueprintRimStrength("Blueprint Rim Strength", Range(0.0, 2.0)) = 0.8
        _BlueprintRimPower("Blueprint Rim Power", Range(0.5, 8.0)) = 2.2

        [HideInInspector] _MainTex("BaseMap", 2D) = "white" {}
        [HideInInspector] _Color("Base Color", Color) = (1, 1, 1, 1)
        [HideInInspector][NoScaleOffset] unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 300

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _ShadowColor;
                float4 _SpecularColor;
                half _ShadeThreshold;
                half _ShadeSmoothness;
                half _UseSpecular;
                half _SpecularIntensity;
                half _SpecularPower;
                half _SpecularThreshold;
                half _SpecularSmoothness;
                half _Surface;
                half _AlphaClip;
                half _Cutoff;
                half _UseBlackCutout;
                half _BlackCutoutThreshold;
                half _VertexNormalOffset;
                half _BlueprintPreview;
                half4 _BlueprintTint;
                half _BlueprintBrightness;
                half _BlueprintMinBrightness;
                half _BlueprintContrast;
                half _BlueprintAlpha;
                half4 _BlueprintRimColor;
                half _BlueprintRimStrength;
                half _BlueprintRimPower;
            CBUFFER_END

            half4 SampleToonBase(float2 uv, out half4 rawSample)
            {
                rawSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
                return rawSample * _BaseColor;
            }

            half4 ApplyBlueprintPreview(half4 baseSample)
            {
                half preview = saturate(_BlueprintPreview);
                half luma = dot(baseSample.rgb, half3(0.2126h, 0.7152h, 0.0722h));
                luma = saturate((luma - 0.5h) * _BlueprintContrast + 0.5h);
                half grayRange = max(_BlueprintBrightness - _BlueprintMinBrightness, 0.0h);
                half gray = saturate(_BlueprintMinBrightness + luma * grayRange);
                half3 brightGrayscale = half3(gray, gray, gray);
                half3 tinted = lerp(brightGrayscale, brightGrayscale * _BlueprintTint.rgb, 0.72h);
                baseSample.rgb = lerp(baseSample.rgb, tinted, preview);
                baseSample.a = lerp(baseSample.a, baseSample.a * saturate(_BlueprintAlpha), preview);
                return baseSample;
            }

            half3 ApplyBlueprintRim(half3 color, half3 normalWS, half3 viewDirectionWS)
            {
                half preview = saturate(_BlueprintPreview);
                half ndv = saturate(dot(normalWS, viewDirectionWS));
                half rim = pow(saturate(1.0h - ndv), max(_BlueprintRimPower, 0.001h));
                return color + _BlueprintRimColor.rgb * (rim * _BlueprintRimStrength * preview);
            }

            void ApplyToonCutout(half4 rawSample, half4 baseSample)
            {
                clip(lerp(1.0h, baseSample.a - _Cutoff, saturate(_AlphaClip)));

                half maxTextureChannel = max(max(rawSample.r, rawSample.g), rawSample.b);
                clip(lerp(1.0h, maxTextureChannel - _BlackCutoutThreshold, saturate(_UseBlackCutout)));
            }

            float3 ApplyVertexNormalOffset(float3 positionOS, float3 normalOS)
            {
                return positionOS + normalOS * _VertexNormalOffset;
            }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend[_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]
            Offset[_DepthOffsetFactor], [_DepthOffsetUnits]
            ZTest[_ZTest]
            ZWrite[_ZWrite]
            Cull[_Cull]
            AlphaToMask[_AlphaToMask]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 staticLightmapUV : TEXCOORD1;
                float2 dynamicLightmapUV : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
#ifdef _ADDITIONAL_LIGHTS_VERTEX
                half4 fogFactorAndVertexLight : TEXCOORD3;
#else
                half fogFactor : TEXCOORD3;
#endif
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD4;
#endif
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 5);
#ifdef DYNAMICLIGHTMAP_ON
                float2 dynamicLightmapUV : TEXCOORD6;
#endif
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionOS = ApplyVertexNormalOffset(input.positionOS.xyz, input.normalOS);
                VertexPositionInputs vertexInput = GetVertexPositionInputs(positionOS);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.positionWS = vertexInput.positionWS;
                output.normalWS = NormalizeNormalPerVertex(normalInput.normalWS);

                half fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
#ifdef DYNAMICLIGHTMAP_ON
                output.dynamicLightmapUV = input.dynamicLightmapUV.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
#endif
                OUTPUT_SH(output.normalWS.xyz, output.vertexSH);

#ifdef _ADDITIONAL_LIGHTS_VERTEX
                half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);
                output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
#else
                output.fogFactor = fogFactor;
#endif

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                output.shadowCoord = GetShadowCoord(vertexInput);
#endif
                output.positionCS = vertexInput.positionCS;
                return output;
            }

            void InitializeInputDataCustom(Varyings input, out InputData inputData)
            {
                inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = NormalizeNormalPerPixel(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                inputData.shadowCoord = input.shadowCoord;
#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
#else
                inputData.shadowCoord = float4(0, 0, 0, 0);
#endif

#ifdef _ADDITIONAL_LIGHTS_VERTEX
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactorAndVertexLight.x);
                inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
#else
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
                inputData.vertexLighting = half3(0, 0, 0);
#endif

#if defined(DYNAMICLIGHTMAP_ON)
                inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.dynamicLightmapUV, input.vertexSH, inputData.normalWS);
#else
                inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
#endif

                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
            }

            half3 GetToonSpecular(
                half3 normalWS,
                half3 viewDirectionWS,
                half3 lightDirectionWS,
                half3 lightColor,
                half lightAttenuation)
            {
                half3 halfDirection = SafeNormalize(lightDirectionWS + viewDirectionWS);
                half specular = pow(saturate(dot(normalWS, halfDirection)), _SpecularPower);
                specular = smoothstep(
                    _SpecularThreshold - _SpecularSmoothness,
                    _SpecularThreshold + _SpecularSmoothness,
                    specular);
                specular *= saturate(_UseSpecular) * _SpecularIntensity * lightAttenuation;
                return _SpecularColor.rgb * lightColor * specular;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 rawBaseSample;
                half4 baseSample = SampleToonBase(input.uv, rawBaseSample);
                ApplyToonCutout(rawBaseSample, baseSample);
                baseSample = ApplyBlueprintPreview(baseSample);

                InputData inputData;
                InitializeInputDataCustom(input, inputData);

                Light mainLight = GetMainLight(inputData.shadowCoord);

                half NdotL = saturate(dot(inputData.normalWS, mainLight.direction));
                half lightBand = smoothstep(_ShadeThreshold - _ShadeSmoothness, _ShadeThreshold + _ShadeSmoothness, NdotL * mainLight.shadowAttenuation);
                half3 direct = lerp(baseSample.rgb * _ShadowColor.rgb, baseSample.rgb, lightBand) * mainLight.color;
                half3 ambient = inputData.bakedGI * baseSample.rgb;
                half3 additional = inputData.vertexLighting * baseSample.rgb;
                half3 specular = GetToonSpecular(
                    inputData.normalWS,
                    inputData.viewDirectionWS,
                    mainLight.direction,
                    mainLight.color,
                    mainLight.shadowAttenuation);

#if defined(_ADDITIONAL_LIGHTS) || defined(_CLUSTER_LIGHT_LOOP)
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, inputData.positionWS);
                    half additionalNdotL = saturate(dot(inputData.normalWS, light.direction));
                    half additionalBand = smoothstep(_ShadeThreshold - _ShadeSmoothness, _ShadeThreshold + _ShadeSmoothness, additionalNdotL * light.shadowAttenuation);
                    additional += lerp(baseSample.rgb * _ShadowColor.rgb, baseSample.rgb, additionalBand) * light.color * light.distanceAttenuation;
                    specular += GetToonSpecular(
                        inputData.normalWS,
                        inputData.viewDirectionWS,
                        light.direction,
                        light.color,
                        light.distanceAttenuation * light.shadowAttenuation);
                LIGHT_LOOP_END
#endif

                half3 finalColor = ambient + direct + additional + specular;
                finalColor = ApplyBlueprintRim(finalColor, inputData.normalWS, inputData.viewDirectionWS);
                finalColor = MixFog(finalColor, inputData.fogCoord);
                return half4(finalColor, baseSample.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            Offset[_DepthOffsetFactor], [_DepthOffsetUnits]
            ZTest[_ZTest]
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing

            struct DepthOnlyAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthOnlyVaryings
            {
                float2 uv : TEXCOORD0;
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthOnlyVaryings DepthOnlyVertex(DepthOnlyAttributes input)
            {
                DepthOnlyVaryings output = (DepthOnlyVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.positionCS = TransformObjectToHClip(ApplyVertexNormalOffset(input.positionOS.xyz, input.normalOS));
                return output;
            }

            half DepthOnlyFragment(DepthOnlyVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 rawBaseSample;
                half4 baseSample = SampleToonBase(input.uv, rawBaseSample);
                ApplyToonCutout(rawBaseSample, baseSample);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Offset[_DepthOffsetFactor], [_DepthOffsetUnits]
            ZTest[_ZTest]
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing

            struct DepthNormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthNormalsVaryings
            {
                float2 uv : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthNormalsVaryings DepthNormalsVertex(DepthNormalsAttributes input)
            {
                DepthNormalsVaryings output = (DepthNormalsVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.positionCS = TransformObjectToHClip(ApplyVertexNormalOffset(input.positionOS.xyz, input.normalOS));
                output.normalWS = NormalizeNormalPerVertex(TransformObjectToWorldNormal(input.normalOS));
                return output;
            }

            half4 DepthNormalsFragment(DepthNormalsVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 rawBaseSample;
                half4 baseSample = SampleToonBase(input.uv, rawBaseSample);
                ApplyToonCutout(rawBaseSample, baseSample);

                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                return half4(normalWS, 0.0h);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Simple Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Simple Lit/Meta"
    }
}
