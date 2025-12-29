Shader "Custom/Toon"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _ShadowColor ("Shadow Color", Color) = (0.4, 0.4, 0.5, 1)
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.5
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.1)) = 0.02
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        // Outline Pass
        Pass
        {
            Name "Outline"
            Cull Front
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _OutlineWidth;
            half4 _OutlineColor;

            struct Attributes { float4 pos : POSITION; float3 normal : NORMAL; };
            struct Varyings { float4 pos : SV_POSITION; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                // 월드 위치에서 카메라까지 거리 계산
                float3 worldPos = TransformObjectToWorld(i.pos.xyz);
                float dist = distance(worldPos, _WorldSpaceCameraPos);
                
                // 거리에 비례하여 외곽선 확장 (가까우면 작게, 멀면 크게)
                float scaledWidth = _OutlineWidth * max(dist * 0.1, 1.0);
                
                float3 pos = i.pos.xyz + i.normal * scaledWidth;
                o.pos = TransformObjectToHClip(pos);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return _OutlineColor; }
            ENDHLSL
        }

        // Main Pass
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            half4 _Color, _ShadowColor;
            half _ShadowThreshold;

            struct Attributes { float4 pos : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 normal : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.pos = TransformObjectToHClip(i.pos.xyz);
                o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                o.normal = TransformObjectToWorldNormal(i.normal);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Color;
                Light light = GetMainLight();
                half NdotL = dot(normalize(i.normal), light.direction) * 0.5 + 0.5;
                half3 toon = lerp(_ShadowColor.rgb, half3(1,1,1), step(_ShadowThreshold, NdotL)) * light.color;
                return half4(col.rgb * toon, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
