Shader "ProjectF/Terrain/WaterSurfaceGlint"
{
    Properties
    {
        _GlintColor("Glint Color", Color) = (0.86, 0.96, 1, 0.30)
        _GlintDirection("Glint Direction", Vector) = (1, 0.18, 0, 0)
        _GlintScale("Glint Scale", Float) = 1.35
        _LineWidth("Line Width", Range(0.005, 0.5)) = 0.16
        _Breakup("Breakup", Range(0, 1)) = 0.33
        _FlowSpeed("Flow Speed", Float) = 0.28
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _GlintColor;
                float4 _GlintDirection;
                half _GlintScale;
                half _LineWidth;
                half _Breakup;
                half _FlowSpeed;
            CBUFFER_END

            half StableGlintNoise(float2 value)
            {
                half waveA = 0.5h + 0.5h * sin(value.x * 12.9898h + value.y * 78.233h);
                half waveB = 0.5h + 0.5h * sin(value.x * 4.113h - value.y * 15.719h + 1.37h);
                return saturate((waveA * 0.58h) + (waveB * 0.42h));
            }

            half FlowLayer(float along, float across, half scale, half lineWidth, half phase, half speed)
            {
                float time = _Time.y * speed;
                float warpedAcross = (across * scale)
                    + sin((along * 0.58) + time + phase) * 0.18
                    + sin((along * 1.43) - (time * 0.72) + (phase * 1.37)) * 0.07;
                half lane = smoothstep(lineWidth, 0.0h, abs(frac(warpedAcross) - 0.5h));

                half dash = 0.5h + 0.5h * sin((along * scale * 1.15) - (time * 2.35) + phase);
                dash = smoothstep(0.36h, 0.92h, dash);

                half noise = StableGlintNoise(float2((along * 0.19) - (time * 0.24) + phase, (across * 0.31) + phase));
                half breakup = lerp(1.0h, smoothstep(_Breakup, 1.0h, noise), 0.62h);
                return lane * dash * breakup;
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
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 direction = _GlintDirection.xy;
                direction = dot(direction, direction) > 0.0001 ? normalize(direction) : float2(1, 0);
                float2 perpendicular = float2(-direction.y, direction.x);
                float2 world = input.positionWS.xz;
                float along = dot(world, direction);
                float across = dot(world, perpendicular);

                half layerA = FlowLayer(along, across, max(_GlintScale, 0.01h), _LineWidth, 0.0h, _FlowSpeed);
                half layerB = FlowLayer(along + 3.7h, across - 1.9h, max(_GlintScale * 0.73h, 0.01h), _LineWidth * 0.74h, 2.11h, _FlowSpeed * 1.37h);

                float time = _Time.y * _FlowSpeed;
                half softRipple = 0.5h + 0.5h * sin((along * 1.1h) - (time * 1.45h) + sin(across * 1.7h) * 0.35h);
                softRipple = smoothstep(0.82h, 1.0h, softRipple) * 0.18h;

                half alpha = saturate((layerA + (layerB * 0.58h) + softRipple) * _GlintColor.a);

                half3 color = MixFog(_GlintColor.rgb, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
