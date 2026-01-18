Shader "Hidden/PostProcess/Outline"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineScale ("Outline Scale", Float) = 1
        _DepthThreshold ("Depth Threshold", Float) = 1.5
        _NormalThreshold ("Normal Threshold", Float) = 0.4
        _DepthNormalThreshold ("Depth Normal Threshold", Float) = 0.5
        _DepthNormalThresholdScale ("Depth Normal Threshold Scale", Float) = 7
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "OutlinePass"
            
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_BlitTexture);
            SAMPLER(sampler_BlitTexture);
            
            float4 _BlitTexture_TexelSize;
            float4 _OutlineColor;
            float _OutlineScale;
            float _DepthThreshold;
            float _NormalThreshold;
            float _DepthNormalThreshold;
            float _DepthNormalThresholdScale;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }



            TEXTURE2D(_OutlineColorMap); 
            SAMPLER(sampler_OutlineColorMap);

            // --- MAIN FUNCTION ---
            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                
                // 1. Center Depth/Normal
                float halfScaleFloor = floor(_OutlineScale * 0.5);
                float halfScaleCeil = ceil(_OutlineScale * 0.5);
                
                // Use _BlitTexture_TexelSize
                float2 texelSize = _BlitTexture_TexelSize.xy;
                
                // Sample offsets
                float2 bottomLeftUV = uv - float2(texelSize.x, texelSize.y) * halfScaleFloor;
                float2 topRightUV = uv + float2(texelSize.x, texelSize.y) * halfScaleCeil;  
                float2 bottomRightUV = uv + float2(texelSize.x * halfScaleCeil, -texelSize.y * halfScaleFloor);
                float2 topLeftUV = uv + float2(-texelSize.x * halfScaleFloor, texelSize.y * halfScaleCeil);

                // Depth Samples
                float depth0 = LinearEyeDepth(SampleSceneDepth(bottomLeftUV), _ZBufferParams);
                float depth1 = LinearEyeDepth(SampleSceneDepth(topRightUV), _ZBufferParams);
                float depth2 = LinearEyeDepth(SampleSceneDepth(bottomRightUV), _ZBufferParams);
                float depth3 = LinearEyeDepth(SampleSceneDepth(topLeftUV), _ZBufferParams);

                // Normal Samples
                float3 normal0 = SampleSceneNormals(bottomLeftUV);
                float3 normal1 = SampleSceneNormals(topRightUV);
                float3 normal2 = SampleSceneNormals(bottomRightUV);
                float3 normal3 = SampleSceneNormals(topLeftUV);

                // Calculate Edge
                
                // Depth Diff
                float depthFiniteDifference0 = depth1 - depth0;
                float depthFiniteDifference1 = depth3 - depth2;
                float edgeDepth = sqrt(pow(depthFiniteDifference0, 2) + pow(depthFiniteDifference1, 2)) * 100;
                float depthThreshold = _DepthThreshold * depth0;
                edgeDepth = edgeDepth > depthThreshold ? 1 : 0;

                // Normal Diff
                float3 normalFiniteDifference0 = normal1 - normal0;
                float3 normalFiniteDifference1 = normal3 - normal2;
                float edgeNormal = sqrt(dot(normalFiniteDifference0, normalFiniteDifference0) + dot(normalFiniteDifference1, normalFiniteDifference1));
                edgeNormal = edgeNormal > _NormalThreshold ? 1 : 0;

                float edge = max(edgeDepth, edgeNormal);

                float4 sceneColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, uv);
                
                // Sample Per-Object Outline Color Map
                float4 objectColor = SAMPLE_TEXTURE2D(_OutlineColorMap, sampler_OutlineColorMap, uv);
                
                // --- NEIGHBOR SEARCH (For Outer Outline) ---
                // If the center pixel has no color (background), we check neighbors to see if we belong to an object's outline.
                float4 targetOutlineColor = objectColor;

                if (targetOutlineColor.a <= 0.0)
                {
                   float4 c0 = SAMPLE_TEXTURE2D(_OutlineColorMap, sampler_OutlineColorMap, bottomLeftUV);
                   float4 c1 = SAMPLE_TEXTURE2D(_OutlineColorMap, sampler_OutlineColorMap, topRightUV);
                   float4 c2 = SAMPLE_TEXTURE2D(_OutlineColorMap, sampler_OutlineColorMap, bottomRightUV);
                   float4 c3 = SAMPLE_TEXTURE2D(_OutlineColorMap, sampler_OutlineColorMap, topLeftUV);
                   
                   if (c0.a > 0) targetOutlineColor = c0;
                   else if (c1.a > 0) targetOutlineColor = c1;
                   else if (c2.a > 0) targetOutlineColor = c2;
                   else if (c3.a > 0) targetOutlineColor = c3;
                }

                // Final Check: If we still have no color, it means this edge is NOT part of a Toon object.
                // In that case, we should NOT draw an outline.
                if (targetOutlineColor.a <= 0.0)
                    return sceneColor;

                return lerp(sceneColor, targetOutlineColor, edge);
            }
            ENDHLSL
        }
    }
}
