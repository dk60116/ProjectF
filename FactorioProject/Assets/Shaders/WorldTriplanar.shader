Shader "Custom/WorldTriplanar"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _Tiling("Tiling", Float) = 1
        _BlendSharpness("Blend Sharpness", Float) = 4
        _ShadowColor("Shadow Color", Color) = (0.7, 0.7, 0.75, 1.0)
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
        [HideInInspector] _SpecGlossMap("Specular Map", 2D) = "white" {}
        [HideInInspector] _GlossinessSource("GlossinessSource", Float) = 0.0
        [HideInInspector] _SpecSource("SpecularHighlights", Float) = 0.0
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
            "UniversalMaterialType" = "SimpleLit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            Blend[_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]
            ZWrite[_ZWrite]
            Cull[_Cull]
            AlphaToMask[_AlphaToMask]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
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
                float2 staticLightmapUV : TEXCOORD1;
                float2 dynamicLightmapUV : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
#ifdef _ADDITIONAL_LIGHTS_VERTEX
                half4 fogFactorAndVertexLight : TEXCOORD2;
#else
                half fogFactor : TEXCOORD2;
#endif
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD3;
#endif
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 4);
#ifdef DYNAMICLIGHTMAP_ON
                float2 dynamicLightmapUV : TEXCOORD5;
#endif
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float4 _ShadowColor;
                half _Tiling;
                half _BlendSharpness;
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

            float4 SampleTriplanar(float3 positionWS, float3 normalWS)
            {
                float3 blendWeights = abs(normalize(normalWS));
                blendWeights = pow(blendWeights, _BlendSharpness);
                blendWeights /= max(blendWeights.x + blendWeights.y + blendWeights.z, 0.0001);

                float2 uvX = positionWS.zy * _Tiling;
                float2 uvY = positionWS.xz * _Tiling;
                float2 uvZ = positionWS.xy * _Tiling;

                float4 xProjection = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX);
                float4 yProjection = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvY);
                float4 zProjection = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ);

                return xProjection * blendWeights.x +
                       yProjection * blendWeights.y +
                       zProjection * blendWeights.z;
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

                float4 baseSample = SampleTriplanar(input.positionWS, inputData.normalWS);
                half3 albedo = baseSample.rgb * _BaseColor.rgb;
                half alpha = baseSample.a * _BaseColor.a;

                Light mainLight = GetMainLight(inputData.shadowCoord);
                half NdotL = saturate(dot(inputData.normalWS, mainLight.direction));
                half mainBand = smoothstep(
                    _ShadeThreshold - _ShadeSmoothness,
                    _ShadeThreshold + _ShadeSmoothness,
                    NdotL * mainLight.shadowAttenuation
                );

                half3 direct = lerp(albedo * _ShadowColor.rgb, albedo, mainBand) * mainLight.color;
                half3 ambient = inputData.bakedGI * albedo;
                half3 additional = inputData.vertexLighting * albedo;

#if defined(_ADDITIONAL_LIGHTS) || defined(_CLUSTER_LIGHT_LOOP)
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, inputData.positionWS);
                    half additionalNdotL = saturate(dot(inputData.normalWS, light.direction));
                    half additionalBand = smoothstep(
                        _ShadeThreshold - _ShadeSmoothness,
                        _ShadeThreshold + _ShadeSmoothness,
                        additionalNdotL * light.shadowAttenuation
                    );
                    additional += lerp(albedo * _ShadowColor.rgb, albedo, additionalBand) * light.color * light.distanceAttenuation;
                LIGHT_LOOP_END
#endif

                half3 finalColor = ambient + direct + additional;
                finalColor = MixFog(finalColor, inputData.fogCoord);
                return half4(finalColor, OutputAlpha(alpha, IsSurfaceTypeTransparent(_Surface)));
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Simple Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Simple Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Simple Lit/DepthNormals"
        UsePass "Universal Render Pipeline/Simple Lit/Meta"
    }
}
