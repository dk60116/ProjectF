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
        [Toggle] _GlintEnabled("Glint Enabled", Float) = 0
        _GlintColor("Glint Color", Color) = (0.86, 0.96, 1, 0.30)
        _GlintDirection("Glint Direction", Vector) = (1, 0.18, 0, 0)
        _GlintScale("Glint Scale", Float) = 1.35
        _GlintLineWidth("Glint Line Width", Range(0.005, 0.5)) = 0.16
        _GlintBreakup("Glint Breakup", Range(0, 1)) = 0.33
        _GlintFlowSpeed("Glint Flow Speed", Float) = 0.28
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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                half waterDepth : TEXCOORD4;
#if defined(_ADDITIONAL_LIGHTS_VERTEX)
                half3 vertexLighting : TEXCOORD5;
#endif
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
                half _GlintEnabled;
                half4 _GlintColor;
                float4 _GlintDirection;
                half _GlintScale;
                half _GlintLineWidth;
                half _GlintBreakup;
                half _GlintFlowSpeed;
            CBUFFER_END

            #include "TerrainWaterLighting.hlsl"

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

            half StableGlintNoise(float2 value)
            {
                half waveA = 0.5h + 0.5h * sin(value.x * 12.9898h + value.y * 78.233h);
                half waveB = 0.5h + 0.5h * sin(value.x * 4.113h - value.y * 15.719h + 1.37h);
                return saturate((waveA * 0.58h) + (waveB * 0.42h));
            }

            half StationaryGlintPulse(float along, float across, half phase, half speed)
            {
                float time = _Time.y * speed;
                half phaseA = StableGlintNoise(float2((along * 0.071) + phase, (across * 0.113) - phase)) * 6.28318h;
                half phaseB = StableGlintNoise(float2((along * 0.193) - phase, (across * 0.157) + phase)) * 6.28318h;
                half pulseA = 0.5h + 0.5h * sin(time + phaseA);
                half pulseB = 0.5h + 0.5h * sin((time * 1.37h) + phaseB);
                return saturate((pulseA * 0.64h) + (pulseB * 0.36h));
            }

            half GlintFlowLayer(float along, float across, half scale, half lineWidth, half phase, half speed)
            {
                half localPulse = StationaryGlintPulse(along, across, phase, speed);
                half animatedLineWidth = lineWidth * lerp(0.55h, 1.55h, localPulse);
                float warpedAcross = (across * scale)
                    + sin((along * 0.58) + phase) * (0.10 + (0.10 * localPulse))
                    + sin((along * 1.43) + (phase * 1.37)) * (0.035 + (0.045 * localPulse));
                half lane = smoothstep(animatedLineWidth, 0.0h, abs(frac(warpedAcross) - 0.5h));

                half dash = 0.5h + 0.5h * sin((along * scale * 1.15) + phase);
                dash = smoothstep(0.36h, 0.92h, dash);
                dash *= lerp(0.25h, 1.35h, localPulse);

                half noise = StableGlintNoise(float2((along * 0.19) + phase, (across * 0.31) + phase));
                half breakup = lerp(1.0h, smoothstep(_GlintBreakup, 1.0h, noise), 0.62h);
                return lane * dash * breakup;
            }

            half GetWaterGlintAlpha(float3 positionWS, half waterBrightness)
            {
                float2 direction = _GlintDirection.xy;
                direction = dot(direction, direction) > 0.0001 ? normalize(direction) : float2(1, 0);
                float2 perpendicular = float2(-direction.y, direction.x);
                float along = dot(positionWS.xz, direction);
                float across = dot(positionWS.xz, perpendicular);

                half layerA = GlintFlowLayer(
                    along,
                    across,
                    max(_GlintScale, 0.01h),
                    _GlintLineWidth,
                    0.0h,
                    _GlintFlowSpeed);
                half layerB = GlintFlowLayer(
                    along + 3.7h,
                    across - 1.9h,
                    max(_GlintScale * 0.73h, 0.01h),
                    _GlintLineWidth * 0.74h,
                    2.11h,
                    _GlintFlowSpeed * 1.37h);

                half softRipple = 0.5h + 0.5h * sin((along * 1.1h) + sin(across * 1.7h) * 0.35h);
                half softPulse = StationaryGlintPulse(along + 11.3h, across - 5.7h, 4.9h, _GlintFlowSpeed * 1.45h);
                softRipple = smoothstep(0.78h, 1.0h, softRipple) * lerp(0.0h, 0.28h, softPulse);

                half surfacePulse = StationaryGlintPulse(along - 2.1h, across + 4.3h, 7.4h, _GlintFlowSpeed * 0.83h);
                return saturate((layerA + (layerB * 0.58h) + softRipple)
                    * _GlintColor.a
                    * lerp(0.45h, 1.65h, surfacePulse)
                    * waterBrightness);
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
                output.waterDepth = saturate(input.color.r);
#if defined(_ADDITIONAL_LIGHTS_VERTEX)
                output.vertexLighting = VertexLighting(vertexInput.positionWS, output.normalWS);
#endif
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
                half depthBlend = saturate(input.waterDepth);
                half waveBrightness = lerp(0.95h, 1.07h, saturate(0.5h + (waterNoise * 0.28h)));
                half3 baseColor = lerp(_BaseColor.rgb, _DeepColor.rgb, depthBlend) * waveBrightness;

                half lightFacing = saturate(dot(normalWS, lightDirectionWS) * 0.35h + 0.65h);
                half shadowAttenuation = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half3 color = baseColor * lerp(0.78h, 1.05h, lightFacing * shadowAttenuation);
                half waterBrightness = GetWorldWaterBrightness();
                color *= waterBrightness;

#if defined(_ADDITIONAL_LIGHTS_VERTEX)
                color += baseColor * input.vertexLighting;
#elif defined(_ADDITIONAL_LIGHTS) || defined(_CLUSTER_LIGHT_LOOP)
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light additionalLight = GetAdditionalLight(lightIndex, input.positionWS, half4(1.0h, 1.0h, 1.0h, 1.0h));
                    half3 attenuatedLightColor = additionalLight.color
                        * additionalLight.distanceAttenuation
                        * additionalLight.shadowAttenuation;
                    color += baseColor * LightingLambert(attenuatedLightColor, additionalLight.direction, normalWS);
                LIGHT_LOOP_END
#endif

                color = MixFog(color, input.fogFactor);
                half outputAlpha = _BaseColor.a;
                if (_GlintEnabled > 0.5h && _GlintColor.a > 0.0001h)
                {
                    half glintAlpha = GetWaterGlintAlpha(input.positionWS, waterBrightness);
                    half3 glintColor = MixFog(_GlintColor.rgb * waterBrightness, input.fogFactor);
                    half combinedAlpha = glintAlpha + outputAlpha * (1.0h - glintAlpha);
                    color = ((glintColor * glintAlpha) + (color * outputAlpha * (1.0h - glintAlpha)))
                        / max(combinedAlpha, 0.0001h);
                    outputAlpha = combinedAlpha;
                }

                return half4(color, outputAlpha);
            }
            ENDHLSL
        }
    }
}
