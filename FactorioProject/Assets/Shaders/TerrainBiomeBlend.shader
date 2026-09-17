Shader "ProjectF/Terrain/BiomeBlend"
{
    Properties
    {
        [NoScaleOffset] _SandMap("Sand Map", 2D) = "white" {}
        [NoScaleOffset] _SandMap2("Sand Map 2", 2D) = "white" {}
        [NoScaleOffset] _SandMap3("Sand Map 3", 2D) = "white" {}
        [NoScaleOffset] _SandMap4("Sand Map 4", 2D) = "white" {}
        [NoScaleOffset] _DirtMap("Dirt Map", 2D) = "white" {}
        [NoScaleOffset] _DirtMap2("Dirt Map 2", 2D) = "white" {}
        [NoScaleOffset] _DirtMap3("Dirt Map 3", 2D) = "white" {}
        [NoScaleOffset] _DirtMap4("Dirt Map 4", 2D) = "white" {}
        [NoScaleOffset] _GrassMap("Grass Map", 2D) = "white" {}
        [NoScaleOffset] _GrassMap2("Grass Map 2", 2D) = "white" {}
        [NoScaleOffset] _GrassMap3("Grass Map 3", 2D) = "white" {}
        [NoScaleOffset] _GrassMap4("Grass Map 4", 2D) = "white" {}
        [NoScaleOffset] _ForestMap("Forest Map", 2D) = "white" {}
        [NoScaleOffset] _ForestMap2("Forest Map 2", 2D) = "white" {}
        [NoScaleOffset] _ForestMap3("Forest Map 3", 2D) = "white" {}
        [NoScaleOffset] _ForestMap4("Forest Map 4", 2D) = "white" {}
        _SandColor("Sand Color", Color) = (1, 1, 1, 1)
        _DirtColor("Dirt Color", Color) = (1, 1, 1, 1)
        _GrassColor("Grass Color", Color) = (1, 1, 1, 1)
        _ForestColor("Forest Color", Color) = (1, 1, 1, 1)
        _TextureTiling("Texture Tiling", Float) = 0.28
        [HideInInspector] _TerrainSeed("Terrain Seed", Float) = 0
        _NoiseScale("Noise Scale", Float) = 0.11
        _NoiseStrength("Noise Strength", Range(0, 0.5)) = 0.18
        [Toggle] _BlendEnabled("Biome Blend Enabled", Float) = 1
        _ShadowColor("Shadow Color", Color) = (0.7, 0.7, 0.75, 1)
        _ShadeThreshold("Shade Threshold", Range(0, 1)) = 0.5
        _ShadeSmoothness("Shade Smoothness", Range(0.001, 0.5)) = 0.05

        _Surface("__surface", Float) = 0.0
        _Blend("__blend", Float) = 0.0
        _Cull("__cull", Float) = 2.0
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
            AlphaToMask[_AlphaToMask]

            HLSLPROGRAM
            #pragma target 3.5
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
                float4 color : COLOR;
                float2 staticLightmapUV : TEXCOORD1;
                float2 dynamicLightmapUV : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 blendWeights : TEXCOORD2;
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

            TEXTURE2D(_SandMap);
            SAMPLER(sampler_SandMap);
            TEXTURE2D(_SandMap2);
            TEXTURE2D(_SandMap3);
            TEXTURE2D(_SandMap4);
            TEXTURE2D(_DirtMap);
            TEXTURE2D(_DirtMap2);
            TEXTURE2D(_DirtMap3);
            TEXTURE2D(_DirtMap4);
            TEXTURE2D(_GrassMap);
            TEXTURE2D(_GrassMap2);
            TEXTURE2D(_GrassMap3);
            TEXTURE2D(_GrassMap4);
            TEXTURE2D(_ForestMap);
            TEXTURE2D(_ForestMap2);
            TEXTURE2D(_ForestMap3);
            TEXTURE2D(_ForestMap4);

            CBUFFER_START(UnityPerMaterial)
                float4 _SandColor;
                float4 _DirtColor;
                float4 _GrassColor;
                float4 _ForestColor;
                float4 _ShadowColor;
                float _TerrainSeed;
                half _TextureTiling;
                half _NoiseScale;
                half _NoiseStrength;
                half _BlendEnabled;
                half _ShadeThreshold;
                half _ShadeSmoothness;
                half _Surface;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

                output.positionWS = vertexInput.positionWS;
                output.normalWS = NormalizeNormalPerVertex(normalInput.normalWS);
                output.blendWeights = saturate(input.color);

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

            half4 NormalizeTerrainWeights(half4 weights)
            {
                half4 normalizedWeights = max(weights, half4(0.0001, 0.0001, 0.0001, 0.0001));
                normalizedWeights /= max(normalizedWeights.r + normalizedWeights.g + normalizedWeights.b + normalizedWeights.a, 0.0001);
                return normalizedWeights;
            }

            half HashNoise(float2 value)
            {
                return frac(sin(dot(value, float2(12.9898, 78.233))) * 43758.5453);
            }

            half4 ApplyNoiseToWeights(half4 weights, float2 noiseUV)
            {
                half4 normalizedWeights = NormalizeTerrainWeights(weights);

                half dominantWeight = max(max(normalizedWeights.r, normalizedWeights.g), max(normalizedWeights.b, normalizedWeights.a));
                half boundaryStrength = saturate(1.0h - dominantWeight);
                if (boundaryStrength <= 0.0001h || _NoiseStrength <= 0.0001h)
                {
                    return normalizedWeights;
                }

                half sandNoise = HashNoise(noiseUV + float2(0.31, 0.19)) * 2.0h - 1.0h;
                half dirtNoise = HashNoise(noiseUV + float2(0.17, 0.41)) * 2.0h - 1.0h;
                half grassNoise = HashNoise(noiseUV + float2(0.43, 0.29)) * 2.0h - 1.0h;
                half forestNoise = HashNoise(noiseUV + float2(0.11, 0.53)) * 2.0h - 1.0h;

                normalizedWeights += half4(sandNoise, dirtNoise, grassNoise, forestNoise) * boundaryStrength * _NoiseStrength;
                return NormalizeTerrainWeights(normalizedWeights);
            }

            float2 GetTerrainSurfaceProjection(float3 positionWS, half3 normalWS)
            {
                half3 axisWeight = abs(normalWS);
                float2 projection = positionWS.xz;

                if (axisWeight.y < axisWeight.x || axisWeight.y < axisWeight.z)
                {
                    projection = axisWeight.x > axisWeight.z
                        ? positionWS.zy
                        : positionWS.xy;
                }

                return projection;
            }

            float2 GetTerrainSurfaceUV(float3 positionWS, half3 normalWS, float tiling)
            {
                return GetTerrainSurfaceProjection(positionWS, normalWS) * tiling;
            }

            half4 GetTerrainBlendWeights(float3 positionWS, half3 normalWS, half4 blendWeights)
            {
                float2 noiseUV = GetTerrainSurfaceUV(positionWS, normalWS, _NoiseScale);
                half4 weights = ApplyNoiseToWeights(blendWeights, noiseUV);
                if (_BlendEnabled > 0.5h)
                {
                    return weights;
                }

                half dominantWeight = max(max(weights.r, weights.g), max(weights.b, weights.a));
                return half4(
                    weights.r >= dominantWeight ? 1.0h : 0.0h,
                    weights.g >= dominantWeight && weights.r < dominantWeight ? 1.0h : 0.0h,
                    weights.b >= dominantWeight && max(weights.r, weights.g) < dominantWeight ? 1.0h : 0.0h,
                    max(weights.r, max(weights.g, weights.b)) < dominantWeight ? 1.0h : 0.0h);
            }

            int GetTerrainTextureVariant(float2 tiledWorldPosition)
            {
                float normalizedSeed = fmod(abs(_TerrainSeed), 8191.0);
                float2 seedOffset = float2(normalizedSeed * 0.1031, normalizedSeed * 0.0973);
                half selector = HashNoise(floor(tiledWorldPosition) + seedOffset);
                return min((int)floor(selector * 4.0h), 3);
            }

            half4 SampleSandVariant(float2 uv, int variantIndex)
            {
                UNITY_BRANCH
                if (variantIndex == 1)
                {
                    return SAMPLE_TEXTURE2D(_SandMap2, sampler_SandMap, uv);
                }

                UNITY_BRANCH
                if (variantIndex == 2)
                {
                    return SAMPLE_TEXTURE2D(_SandMap3, sampler_SandMap, uv);
                }

                UNITY_BRANCH
                if (variantIndex == 3)
                {
                    return SAMPLE_TEXTURE2D(_SandMap4, sampler_SandMap, uv);
                }

                return SAMPLE_TEXTURE2D(_SandMap, sampler_SandMap, uv);
            }

            half4 SampleDirtVariant(float2 uv, int variantIndex)
            {
                UNITY_BRANCH
                if (variantIndex == 1)
                {
                    return SAMPLE_TEXTURE2D(_DirtMap2, sampler_SandMap, uv);
                }

                UNITY_BRANCH
                if (variantIndex == 2)
                {
                    return SAMPLE_TEXTURE2D(_DirtMap3, sampler_SandMap, uv);
                }

                UNITY_BRANCH
                if (variantIndex == 3)
                {
                    return SAMPLE_TEXTURE2D(_DirtMap4, sampler_SandMap, uv);
                }

                return SAMPLE_TEXTURE2D(_DirtMap, sampler_SandMap, uv);
            }

            half4 SampleGrassVariant(float2 uv, int variantIndex)
            {
                UNITY_BRANCH
                if (variantIndex == 1)
                {
                    return SAMPLE_TEXTURE2D(_GrassMap2, sampler_SandMap, uv);
                }

                UNITY_BRANCH
                if (variantIndex == 2)
                {
                    return SAMPLE_TEXTURE2D(_GrassMap3, sampler_SandMap, uv);
                }

                UNITY_BRANCH
                if (variantIndex == 3)
                {
                    return SAMPLE_TEXTURE2D(_GrassMap4, sampler_SandMap, uv);
                }

                return SAMPLE_TEXTURE2D(_GrassMap, sampler_SandMap, uv);
            }

            half4 SampleForestVariant(float2 uv, int variantIndex)
            {
                UNITY_BRANCH
                if (variantIndex == 1)
                {
                    return SAMPLE_TEXTURE2D(_ForestMap2, sampler_SandMap, uv);
                }

                UNITY_BRANCH
                if (variantIndex == 2)
                {
                    return SAMPLE_TEXTURE2D(_ForestMap3, sampler_SandMap, uv);
                }

                UNITY_BRANCH
                if (variantIndex == 3)
                {
                    return SAMPLE_TEXTURE2D(_ForestMap4, sampler_SandMap, uv);
                }

                return SAMPLE_TEXTURE2D(_ForestMap, sampler_SandMap, uv);
            }

            half4 SampleBlendedBase(float3 positionWS, half3 normalWS, half4 weights)
            {
                float2 projection = GetTerrainSurfaceProjection(positionWS, normalWS);
                float2 uv = projection * _TextureTiling;
                int variantIndex = GetTerrainTextureVariant(uv);
                half4 sandSample = SampleSandVariant(uv, variantIndex) * _SandColor;
                half4 dirtSample = SampleDirtVariant(uv, variantIndex) * _DirtColor;
                half4 grassSample = SampleGrassVariant(uv, variantIndex) * _GrassColor;
                half4 forestSample = SampleForestVariant(uv, variantIndex) * _ForestColor;

                return (sandSample * weights.r)
                     + (dirtSample * weights.g)
                     + (grassSample * weights.b)
                     + (forestSample * weights.a);
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                InputData inputData;
                InitializeInputDataCustom(input, inputData);
                half4 terrainWeights = GetTerrainBlendWeights(input.positionWS, inputData.normalWS, input.blendWeights);

                half4 baseSample = SampleBlendedBase(input.positionWS, inputData.normalWS, terrainWeights);
                Light mainLight = GetMainLight(inputData.shadowCoord);

                half NdotL = saturate(dot(inputData.normalWS, mainLight.direction));
                half lightBand = smoothstep(
                    _ShadeThreshold - _ShadeSmoothness,
                    _ShadeThreshold + _ShadeSmoothness,
                    NdotL * mainLight.shadowAttenuation);
                half3 direct = lerp(baseSample.rgb * _ShadowColor.rgb, baseSample.rgb, lightBand) * mainLight.color;
                half3 ambient = inputData.bakedGI * baseSample.rgb;
                half3 additional = inputData.vertexLighting * baseSample.rgb;

#if defined(_ADDITIONAL_LIGHTS) || defined(_CLUSTER_LIGHT_LOOP)
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, inputData.positionWS);
                    half additionalNdotL = saturate(dot(inputData.normalWS, light.direction));
                    half additionalBand = smoothstep(
                        _ShadeThreshold - _ShadeSmoothness,
                        _ShadeThreshold + _ShadeSmoothness,
                        additionalNdotL * light.shadowAttenuation);
                    half3 additionalDiffuse = lerp(baseSample.rgb * _ShadowColor.rgb, baseSample.rgb, additionalBand)
                        * light.color
                        * light.distanceAttenuation;
                    additional += additionalDiffuse;
                LIGHT_LOOP_END
#endif

                half3 finalColor = ambient + direct + additional;
                finalColor = MixFog(finalColor, inputData.fogCoord);
                return half4(finalColor, baseSample.a);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Simple Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Simple Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Simple Lit/DepthNormals"
        UsePass "Universal Render Pipeline/Simple Lit/Meta"
    }
}
