Shader "Custom/ConveyorBeltCornerFlow"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        _StraightBeltMap("Straight Belt Map", 2D) = "white" {}
        _AlphaMask("Alpha Mask", 2D) = "white" {}
        _PathUvMap("Path UV Map", 2D) = "black" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        _ShadowColor("Shadow Color", Color) = (0.7,0.7,0.75,1)
        _ShadeThreshold("Shade Threshold", Range(0,1)) = 0.5
        _ShadeSmoothness("Shade Smoothness", Range(0.001,0.5)) = 0.05
        _UVScrollY("UV Scroll Y", Float) = -0.5
        _CornerRotationSteps("Corner Rotation Steps", Float) = 3
        _FlowRepeat("Flow Repeat", Float) = 1
        _PathWidthRange("Path Width Range", Vector) = (0,1,0,0)
        _AlphaMaskClip("Alpha Mask Clip", Range(0,1)) = 0.5

        _Surface("__surface", Float) = 0.0
        _Blend("__blend", Float) = 0.0
        _Cull("__cull", Float) = 0.0
        [ToggleUI] _AlphaClip("__clip", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _BlendModePreserveSpecular("_BlendModePreserveSpecular", Float) = 1.0
        [HideInInspector] _AlphaToMask("__alphaToMask", Float) = 0.0
        [ToggleUI] _ReceiveShadows("Receive Shadows", Float) = 1.0
        _QueueOffset("Queue offset", Float) = 0.0
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
        [HideInInspector] _Cutoff("Alpha Clipping", Range(0.0, 1.0)) = 0.5
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

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend[_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]
            ZWrite[_ZWrite]
            Cull[_Cull]
            Offset -1, -1
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

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_StraightBeltMap);
            SAMPLER(sampler_StraightBeltMap);
            TEXTURE2D(_AlphaMask);
            SAMPLER(sampler_AlphaMask);
            TEXTURE2D(_PathUvMap);
            SAMPLER(sampler_PathUvMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _ShadowColor;
                float4 _PathWidthRange;
                half _ShadeThreshold;
                half _ShadeSmoothness;
                float _UVScrollY;
                float _CornerRotationSteps;
                float _FlowRepeat;
                half _AlphaMaskClip;
                half _Surface;
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

            half4 ApplyBlueprintPreview(half4 baseSample)
            {
                half preview = saturate(_BlueprintPreview);
                half luma = dot(baseSample.rgb, half3(0.2126h, 0.7152h, 0.0722h));
                luma = saturate((luma - 0.5h) * _BlueprintContrast + 0.5h);
                half gray = saturate(0.08h + luma * max(_BlueprintBrightness, 1.0h) * 0.72h);
                half3 grayscalePattern = half3(gray, gray, gray);
                half3 bluePattern = grayscalePattern * _BlueprintTint.rgb;
                half3 preservedPattern = saturate(baseSample.rgb * 1.08h);
                half3 tinted = lerp(preservedPattern, bluePattern, 0.55h);
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

            float2 RotateUvQuarter(float2 uv, int steps)
            {
                steps = (steps % 4 + 4) % 4;
                float2 centered = uv - 0.5;
                if (steps == 1)
                {
                    centered = float2(centered.y, -centered.x);
                }
                else if (steps == 2)
                {
                    centered = float2(-centered.x, -centered.y);
                }
                else if (steps == 3)
                {
                    centered = float2(-centered.y, centered.x);
                }

                return centered + 0.5;
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
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

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                InputData inputData;
                InitializeInputDataCustom(input, inputData);

                int rotationSteps = (int)round(_CornerRotationSteps);
                float2 rotatedUv = RotateUvQuarter(input.uv, rotationSteps);
                float2 frameUv = rotatedUv;
                frameUv.y = 1.0 - frameUv.y;
                float2 alphaMaskUv = float2(frameUv.x, 1.0 - frameUv.y);
                half4 frameSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, frameUv) * _BaseColor;
                half4 alphaMaskSample = SAMPLE_TEXTURE2D(_AlphaMask, sampler_AlphaMask, alphaMaskUv);
                half alphaMaskLuma = dot(alphaMaskSample.rgb, half3(0.2126h, 0.7152h, 0.0722h));
                // Support both common mask styles:
                // 1) black/white RGB masks with opaque alpha
                // 2) white RGB textures that store the mask only in alpha
                // Using min() makes black RGB cut out even when alpha is 1, while alpha-only masks still work.
                half alphaMaskValue = min(alphaMaskSample.a, alphaMaskLuma);
                clip(alphaMaskValue - _AlphaMaskClip);

                half4 pathSample = SAMPLE_TEXTURE2D(_PathUvMap, sampler_PathUvMap, rotatedUv);
                float pathMask = smoothstep(0.015, 0.06, saturate(pathSample.a));
                float widthCoord = saturate(pathSample.r);
                float alongCoord = saturate(pathSample.g);

                // PathUV encodes the visible annulus from inner edge 0 to outer edge 1.
                // Keep the range configurable for compatibility with diagnostic textures.
                float pathWidthSpan = max(_PathWidthRange.y - _PathWidthRange.x, 0.001);
                float normalizedWidth = saturate((widthCoord - _PathWidthRange.x) / pathWidthSpan);
                // PathUV's G channel already follows the conveyor's input-to-output direction.
                float pathProgress = alongCoord;
                float alongFlow = frac(pathProgress * max(_FlowRepeat, 0.001) + _UVScrollY * _Time.y);
                float2 flowUv = float2(normalizedWidth, alongFlow);
                half4 flowSample = SAMPLE_TEXTURE2D(_StraightBeltMap, sampler_StraightBeltMap, flowUv) * _BaseColor;
                half3 pathColor = flowSample.rgb;

                half3 baseColor = lerp(frameSample.rgb, pathColor, pathMask);
                half alpha = lerp(frameSample.a, flowSample.a, pathMask) * alphaMaskValue;
                half4 baseSample = ApplyBlueprintPreview(half4(baseColor, alpha));
                baseColor = baseSample.rgb;
                alpha = baseSample.a;

                Light mainLight = GetMainLight(inputData.shadowCoord);

                half NdotL = saturate(dot(inputData.normalWS, mainLight.direction));
                half lightBand = smoothstep(_ShadeThreshold - _ShadeSmoothness, _ShadeThreshold + _ShadeSmoothness, NdotL * mainLight.shadowAttenuation);
                half3 direct = lerp(baseColor * _ShadowColor.rgb, baseColor, lightBand) * mainLight.color;
                half3 ambient = inputData.bakedGI * baseColor;
                half3 additional = inputData.vertexLighting * baseColor;

#if defined(_ADDITIONAL_LIGHTS) || defined(_CLUSTER_LIGHT_LOOP)
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, inputData.positionWS);
                    half additionalNdotL = saturate(dot(inputData.normalWS, light.direction));
                    half additionalBand = smoothstep(_ShadeThreshold - _ShadeSmoothness, _ShadeThreshold + _ShadeSmoothness, additionalNdotL * light.shadowAttenuation);
                    additional += lerp(baseColor * _ShadowColor.rgb, baseColor, additionalBand) * light.color * light.distanceAttenuation;
                LIGHT_LOOP_END
#endif

                half3 finalColor = ambient + direct + additional;
                finalColor = lerp(finalColor, baseSample.rgb, saturate(_BlueprintPreview) * 0.65h);
                finalColor = ApplyBlueprintRim(finalColor, inputData.normalWS, inputData.viewDirectionWS);
                finalColor = MixFog(finalColor, inputData.fogCoord);
                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
}
