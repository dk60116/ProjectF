Shader "Hidden/ProjectF/AnimalScreenSpaceOutlineComposite"
{
    Properties
    {
        [HDR] _OutlineColor("Outline Color", Color) = (1,1,1,1)
        _OutlineWidthPixels("Outline Width (Pixels)", Range(1,8)) = 4
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "AnimalScreenSpaceOutlineComposite"

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_AnimalOutlineMask);
            SAMPLER(sampler_AnimalOutlineMask);

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidthPixels;
            CBUFFER_END

            float4 _AnimalOutlineMask_TexelSize;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                output.uv = GetFullScreenTriangleTexCoord(vertexID);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float originalMask = SAMPLE_TEXTURE2D_X(
                    _AnimalOutlineMask,
                    sampler_AnimalOutlineMask,
                    input.uv).r;
                float originalPresence = step(0.01, originalMask);
                float dilatedPresence = originalPresence;
                float outerRadius = _OutlineWidthPixels + 0.5;

                UNITY_UNROLL
                for (int y = -8; y <= 8; y++)
                {
                    UNITY_UNROLL
                    for (int x = -8; x <= 8; x++)
                    {
                        float distancePixels = length(float2(x, y));
                        if (distancePixels > outerRadius)
                        {
                            continue;
                        }

                        float sampleMask = SAMPLE_TEXTURE2D_X(
                            _AnimalOutlineMask,
                            sampler_AnimalOutlineMask,
                            input.uv + float2(x, y) * _AnimalOutlineMask_TexelSize.xy).r;
                        float edgeCoverage = saturate(outerRadius - distancePixels);
                        float samplePresence = step(0.01, sampleMask);
                        dilatedPresence = max(dilatedPresence, samplePresence * edgeCoverage);
                    }
                }

                float outerEdge = saturate(dilatedPresence - originalPresence);
                float outlineAlpha = outerEdge * _OutlineColor.a;
                return half4(_OutlineColor.rgb, outlineAlpha);
            }
            ENDHLSL
        }
    }
}
