Shader "Custom/WorkableRangeOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex("Alpha Texture", 2D) = "white" {}
        _Color("Tint", Color) = (0.05, 1, 0.05, 0.1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardUnlit"

            Stencil
            {
                Ref 64
                Comp NotEqual
                Pass Replace
                ReadMask 64
                WriteMask 64
            }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 alphaMask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 color = input.color;
                color.a *= alphaMask.a;
                clip(color.a - 0.001h);
                return color;
            }
            ENDHLSL
        }
    }
}
