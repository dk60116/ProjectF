Shader "ProjectF/ResourceGrowthGauge"
{
    Properties { [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 8 }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest [_ZTest]
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _GrowthData)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Requirements)
            UNITY_INSTANCING_BUFFER_END(Props)
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v,o);
                float4 d = UNITY_ACCESS_INSTANCED_PROP(Props, _GrowthData);
                if (d.w < 0) o.vertex = UnityObjectToClipPos(v.vertex);
                else
                {
                    float3 center = mul(unity_ObjectToWorld, float4(0,0,0,1)).xyz;
                    float scale = length(unity_ObjectToWorld._m00_m10_m20);
                    float3 world = center + UNITY_MATRIX_V[0].xyz * v.vertex.x * scale
                        + UNITY_MATRIX_V[1].xyz * v.vertex.y * scale;
                    o.vertex = mul(UNITY_MATRIX_VP, float4(world,1));
                }
                o.uv = v.uv;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 d = UNITY_ACCESS_INSTANCED_PROP(Props, _GrowthData);
                float4 req = UNITY_ACCESS_INSTANCED_PROP(Props, _Requirements);
                float2 p = (i.uv - 0.5) * 2;
                float radius = length(p);
                clip(1-radius);
                if (d.w < 0) return fixed4(0,0,0,lerp(0.7,0,smoothstep(0.76,1,radius)));
                float angle = frac(atan2(p.x,p.y) / (2*UNITY_PI) + 1);
                if (radius < 26.0/72.0) discard;
                fixed3 color = fixed3(0.025,0.025,0.025);
                if (radius < 64.0/72.0 && radius > 49.0/72.0)
                {
                    if (d.w > 0.5) color = angle <= d.z ? fixed3(1,1,1) : fixed3(0.45,0.45,0.45);
                    else color = req.x < 0.5 ? fixed3(0,0,0)
                        : angle <= d.x ? fixed3(0.08,0.48,1) : fixed3(0.3465,0.3915,0.45);
                }
                if (radius < 45.0/72.0 && radius > 30.0/72.0 && d.w < 0.5)
                    color = req.y < 0.5 ? fixed3(0,0,0)
                        : angle <= d.y ? fixed3(0.16,0.82,0.28) : fixed3(0.29475,0.369,0.30825);
                return fixed4(color,1);
            }
            ENDCG
        }
    }
}
