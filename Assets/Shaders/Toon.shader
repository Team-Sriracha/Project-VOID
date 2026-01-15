Shader "Custom/Toon"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _ShadowColor ("Shadow Color", Color) = (0.4, 0.4, 0.5, 1)
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.5
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineWidth ("Outline Width", Range(0, 10)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        // --- OUTLINE PASS ---
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes 
            {
                float4 pos : POSITION;
                float3 normal : NORMAL;
            };

            struct Varyings 
            {
                float4 pos : SV_POSITION;
            };
            
            half4 _OutlineColor;
            float _OutlineWidth;

            Varyings vert(Attributes i) 
            {
                Varyings o;
                float3 normalDir = i.normal;
                float3 pos = i.pos.xyz + normalDir * (_OutlineWidth * 0.02); 
                o.pos = TransformObjectToHClip(pos);
                return o;
            }
            
            half4 frag(Varyings i) : SV_Target 
            {
                return _OutlineColor;
            }
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
