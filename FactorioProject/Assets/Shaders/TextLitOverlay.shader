Shader "ProjectF/UI/Box SDF Lit Overlay"
{
    Properties
    {
        _FaceColor("Face Color", Color) = (1,1,1,1)
        _FaceDilate("Face Dilate", Range(-1,1)) = 0
        _OutlineColor("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth("Outline Thickness", Range(0,1)) = 0
        _OutlineSoftness("Outline Softness", Range(0,1)) = 0
        _WeightNormal("Weight Normal", Float) = 0
        _WeightBold("Weight Bold", Float) = 0.5
        _ShaderFlags("Flags", Float) = 0
        _ScaleRatioA("Scale Ratio A", Float) = 1
        _ScaleRatioB("Scale Ratio B", Float) = 1
        _ScaleRatioC("Scale Ratio C", Float) = 1
        _MainTex("Font Atlas", 2D) = "white" {}
        _TextureWidth("Texture Width", Float) = 512
        _TextureHeight("Texture Height", Float) = 512
        _GradientScale("Gradient Scale", Float) = 5
        _ScaleX("Scale X", Float) = 1
        _ScaleY("Scale Y", Float) = 1
        _PerspectiveFilter("Perspective Correction", Range(0,1)) = 0.875
        _Sharpness("Sharpness", Range(-1,1)) = 0
        _VertexOffsetX("Vertex Offset X", Float) = 0
        _VertexOffsetY("Vertex Offset Y", Float) = 0
        _StencilComp("Stencil Comparison", Float) = 8
        _Stencil("Stencil ID", Float) = 0
        _StencilOp("Stencil Operation", Float) = 0
        _StencilWriteMask("Stencil Write Mask", Float) = 255
        _StencilReadMask("Stencil Read Mask", Float) = 255
        _CullMode("Cull Mode", Float) = 0
        _ColorMask("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull [_CullMode]
        ZWrite Off
        ZTest LEqual
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local __ OUTLINE_ON
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "BoxOverlayLighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _FaceColor;
                half4 _OutlineColor;
                float _FaceDilate;
                float _OutlineWidth;
                float _OutlineSoftness;
                float _WeightNormal;
                float _WeightBold;
                float _ScaleRatioA;
                float _GradientScale;
                float _ScaleX;
                float _ScaleY;
                float _PerspectiveFilter;
                float _Sharpness;
                float _VertexOffsetX;
                float _VertexOffsetY;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half3 normalOS : NORMAL;
                half4 color : COLOR;
                float4 texcoord0 : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 faceColor : COLOR;
                half4 outlineColor : COLOR1;
                half4 sdfParam : TEXCOORD1;
                half3 lighting : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionOS = input.positionOS.xyz;
                positionOS.x += _VertexOffsetX;
                positionOS.y += _VertexOffsetY;

                VertexPositionInputs positionInputs = GetVertexPositionInputs(positionOS);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                half3 normalWS = NormalizeNormalPerVertex(normalInputs.normalWS);
                output.uv = input.texcoord0.xy;

                float2 pixelSize = positionInputs.positionCS.w;
                pixelSize /= float2(_ScaleX, _ScaleY)
                    * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float scale = rsqrt(dot(pixelSize, pixelSize));
                scale *= abs(input.texcoord0.w) * _GradientScale * (_Sharpness + 1.0);

                if (UNITY_MATRIX_P[3][3] == 0.0)
                {
                    half3 viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionInputs.positionWS);
                    scale = lerp(
                        abs(scale) * (1.0 - _PerspectiveFilter),
                        scale,
                        abs(dot(normalWS, viewDirectionWS)));
                }

                float bold = step(input.texcoord0.w, 0.0);
                float weight = lerp(_WeightNormal, _WeightBold, bold) * 0.25;
                weight = (weight + _FaceDilate) * _ScaleRatioA * 0.5;
                scale /= 1.0 + (_OutlineSoftness * _ScaleRatioA * scale);
                float bias = (0.5 - weight) * scale - 0.5;
                float outline = _OutlineWidth * _ScaleRatioA * 0.5 * scale;

                output.faceColor = input.color * _FaceColor;
                output.faceColor.rgb *= output.faceColor.a;
                output.outlineColor = _OutlineColor;
                output.outlineColor.a *= input.color.a;
                output.outlineColor.rgb *= output.outlineColor.a;
                output.outlineColor = lerp(
                    output.faceColor,
                    output.outlineColor,
                    sqrt(min(1.0, outline * 2.0)));
                output.sdfParam = half4(scale, bias - outline, bias + outline, bias);
                output.lighting = GetBoxOverlayVertexLighting(
                    positionInputs.positionWS,
                    normalWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half sdfDistance = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a
                    * input.sdfParam.x;
                half4 color = input.faceColor * saturate(sdfDistance - input.sdfParam.w);
#if defined(OUTLINE_ON)
                color = lerp(
                    input.outlineColor,
                    input.faceColor,
                    saturate(sdfDistance - input.sdfParam.z));
                color *= saturate(sdfDistance - input.sdfParam.y);
#endif

                color.rgb *= input.lighting;
                return color;
            }
            ENDHLSL
        }
    }

    CustomEditor "TMPro.EditorUtilities.TMP_SDFShaderGUI"
}
