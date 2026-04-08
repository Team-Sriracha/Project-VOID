Shader "Custom/Toon FOV"
{
    Properties
    {
        [MainTexture] _MainTex ("Texture", 2D) = "white" {}
        [MainColor] _Color ("Color", Color) = (1, 1, 1, 1)
        _ShadowColor ("Shadow Color", Color) = (0.4, 0.4, 0.5, 1)
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.5
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineWidth ("Outline Width", Range(0, 10)) = 1

        // FOV Properties
        _DimColor ("Dim Color", Color) = (0.3, 0.3, 0.4, 1)
        _FadeWidth ("Fade Width", Range(0.5, 3)) = 1
        [HideInInspector] _FOVRevealAnchorWS ("FOV Reveal Anchor WS", Vector) = (0, 0, 0, 0)
        [HideInInspector] _GlobalStencilComp ("Global Stencil Comp", Float) = 8
        // Blending state
        [HideInInspector] _Cull("__cull", Float) = 2.0
        [HideInInspector] _AlphaClip("__clip", Float) = 0.0
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline" 
            "UniversalMaterialType" = "Lit"
            "Queue" = "Geometry" 
        }
        LOD 200

        // ============================================
        // Outline Pass with FOV Stencil (Clip-Space Method for Hard Edges)
        // ============================================
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            
            Cull Front
            ZWrite On
            ZTest LEqual

            // FOV Stencil - only on this pass
            Stencil
            {
                Ref 3
                Comp [_GlobalStencilComp]
                ReadMask 1
                WriteMask 2
                Pass Replace
            }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment

            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half4 _ShadowColor;
                half _ShadowThreshold;
                half4 _OutlineColor;
                float _OutlineWidth;
                half4 _DimColor;
                half _FadeWidth;
                half _Cutoff;
            CBUFFER_END

            Varyings OutlineVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // Position-based outline: expand from object center
                float3 posOS = input.positionOS.xyz;
                
                // Use vertex position direction from center as expansion direction
                float3 expandDir = normalize(posOS);
                
                // If position is at origin, fallback to normal
                if (length(posOS) < 0.001)
                {
                    expandDir = input.normalOS;
                }
                
                // Expand outward
                posOS += expandDir * (_OutlineWidth * 0.02);
                
                output.positionCS = TransformObjectToHClip(posOS);
                return output;
            }

            half4 OutlineFragment(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }

        // ============================================
        // Forward Lit Pass (Toon Shading + FOV Dimming)
        // ============================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // FOV Stencil - only on this pass
            Stencil
            {
                Ref 3
                Comp [_GlobalStencilComp]
                ReadMask 1
                WriteMask 2
                Pass Replace
            }

            Cull[_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ToonVertex
            #pragma fragment ToonFragmentFOV

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half4 _ShadowColor;
                half _ShadowThreshold;
                half4 _OutlineColor;
                float _OutlineWidth;
                half4 _DimColor;
                half _FadeWidth;
                float4 _FOVRevealAnchorWS;
                half _Cutoff;
            CBUFFER_END

            // FOV Global Variables
            #define FOV_HIT_DISTANCE_CAPACITY 181
            float3 _FOVCenter;
            float _FOVRange;
            float _FOVEdgeSoftness;
            float _FOVEnabled; // 0 = disabled (preview/scene), 1 = enabled (game)
            float4 _FOVProjectionOffset;
            float _FOVBaseY;
            float4 _FOVForwardXZ;
            float _FOVStartAngle;
            float _FOVEndAngle;
            float _FOVHitDistanceCount;
            float _FOVHitDistances[FOV_HIT_DISTANCE_CAPACITY];

            float2 GetFOVForwardXZ()
            {
                float2 forwardXZ = _FOVForwardXZ.xy;
                float lengthSq = dot(forwardXZ, forwardXZ);
                return lengthSq > 0.00001 ? forwardXZ * rsqrt(lengthSq) : float2(0.0, 1.0);
            }

            float GetSignedAngleDegrees(float2 fromDir, float2 toDir)
            {
                float crossValue = fromDir.y * toDir.x - fromDir.x * toDir.y;
                return degrees(atan2(crossValue, dot(fromDir, toDir)));
            }

            float SampleFOVBoundaryDistance(float2 projectedOffset, float currentDistance)
            {
                if (_FOVHitDistanceCount < 2.0)
                {
                    return _FOVRange;
                }

                float2 forwardXZ = GetFOVForwardXZ();
                float2 direction = currentDistance > 0.0001 ? projectedOffset / currentDistance : forwardXZ;
                float angleSpan = max(_FOVEndAngle - _FOVStartAngle, 0.001);
                float signedAngle = GetSignedAngleDegrees(forwardXZ, direction);
                float samplePosition = saturate((signedAngle - _FOVStartAngle) / angleSpan) * (_FOVHitDistanceCount - 1.0);
                int lowerIndex = (int)floor(samplePosition);
                int upperIndex = min(lowerIndex + 1, (int)_FOVHitDistanceCount - 1);
                float interpolation = frac(samplePosition);
                return lerp(_FOVHitDistances[lowerIndex], _FOVHitDistances[upperIndex], interpolation);
            }

            float2 GetRevealProjectedOffset(float3 positionWS)
            {
                if (_FOVRevealAnchorWS.w > 0.5)
                {
                    return _FOVRevealAnchorWS.xz - _FOVCenter.xz;
                }

                float projectedHeight = max(positionWS.y - _FOVBaseY, 0.0);
                float2 projectedXZ = positionWS.xz + (_FOVProjectionOffset.xy * projectedHeight);
                return projectedXZ - _FOVCenter.xz;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 staticLightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float fogCoord : TEXCOORD3;
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 4);
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings ToonVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalInput.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.fogCoord = ComputeFogFactor(vertexInput.positionCS.z);

                OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
                OUTPUT_SH(output.normalWS, output.vertexSH);

                return output;
            }

            half4 ToonFragmentFOV(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 baseColor = texColor * _Color;

                #ifdef _ALPHATEST_ON
                    clip(baseColor.a - _Cutoff);
                #endif

                // Get main light with shadows
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                // Toon shading calculation
                float3 normalWS = normalize(input.normalWS);
                half NdotL = dot(normalWS, mainLight.direction) * 0.5 + 0.5;
                half shadowAtten = mainLight.shadowAttenuation;
                
                // Apply shadow attenuation to NdotL
                NdotL *= shadowAtten;
                
                // Toon step
                half3 toonLight = lerp(_ShadowColor.rgb, half3(1, 1, 1), step(_ShadowThreshold, NdotL));
                toonLight *= mainLight.color;

                // Additional lights
                #ifdef _ADDITIONAL_LIGHTS
                    uint additionalLightsCount = GetAdditionalLightsCount();
                    for (uint i = 0; i < additionalLightsCount; ++i)
                    {
                        Light light = GetAdditionalLight(i, input.positionWS);
                        half addNdotL = dot(normalWS, light.direction) * 0.5 + 0.5;
                        addNdotL *= light.shadowAttenuation * light.distanceAttenuation;
                        toonLight += lerp(half3(0, 0, 0), light.color, step(_ShadowThreshold * 0.5, addNdotL)) * 0.5;
                    }
                #endif

                // Ambient / GI
                half3 ambient = SampleSH(normalWS);
                
                half3 finalColor = baseColor.rgb * (toonLight + ambient * 0.3);

                // Apply FOV dimming - ONLY when FOV system is enabled (game view)
                if (_FOVEnabled > 0.5 && _FOVRange > 1.0)
                {
                    float2 projectedOffset = GetRevealProjectedOffset(input.positionWS);
                    float dist = length(projectedOffset);
                    float boundaryDistance = SampleFOVBoundaryDistance(projectedOffset, dist);
                    float dimWidth = max(_FOVEdgeSoftness + _FadeWidth, 0.1);
                    float dimStart = max(boundaryDistance - dimWidth, 0.0);
                    float dimFactor = saturate((dist - dimStart) / dimWidth);
                    finalColor = lerp(finalColor, finalColor * _DimColor.rgb, dimFactor);
                }

                finalColor = MixFog(finalColor, input.fogCoord);

                return half4(finalColor, baseColor.a);
            }
            ENDHLSL
        }

        // ============================================
        // ShadowCaster Pass
        // ============================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half4 _ShadowColor;
                half _ShadowThreshold;
                half4 _OutlineColor;
                float _OutlineWidth;
                half4 _DimColor;
                half _FadeWidth;
                half _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float3 _LightDirection;
            float3 _LightPosition;

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);

                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_TARGET
            {
                #ifdef _ALPHATEST_ON
                    half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                    clip(texColor.a * _Color.a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        // ============================================
        // DepthOnly Pass
        // ============================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Stencil
            {
                Ref 3
                Comp [_GlobalStencilComp]
                ReadMask 1
                WriteMask 2
                Pass Replace
            }

            ZWrite On
            ColorMask R
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half4 _ShadowColor;
                half _ShadowThreshold;
                half4 _OutlineColor;
                float _OutlineWidth;
                half4 _DimColor;
                half _FadeWidth;
                half _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 DepthOnlyFragment(Varyings input) : SV_TARGET
            {
                #ifdef _ALPHATEST_ON
                    half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                    clip(texColor.a * _Color.a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        // ============================================
        // DepthNormals Pass
        // ============================================
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            Stencil
            {
                Ref 3
                Comp [_GlobalStencilComp]
                ReadMask 1
                WriteMask 2
                Pass Replace
            }

            ZWrite On
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half4 _ShadowColor;
                half _ShadowThreshold;
                half4 _OutlineColor;
                float _OutlineWidth;
                half4 _DimColor;
                half _FadeWidth;
                half _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_TARGET
            {
                #ifdef _ALPHATEST_ON
                    half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                    clip(texColor.a * _Color.a - _Cutoff);
                #endif

                float3 normalWS = normalize(input.normalWS);
                return half4(normalWS * 0.5 + 0.5, 0);
            }
            ENDHLSL
        }

        // ============================================
        // Meta Pass (for lightmap baking)
        // ============================================
        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }

            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex MetaVertex
            #pragma fragment ToonMetaFragment

            #pragma shader_feature EDITOR_VISUALIZATION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half4 _ShadowColor;
                half _ShadowThreshold;
                half4 _OutlineColor;
                float _OutlineWidth;
                half4 _DimColor;
                half _FadeWidth;
                half _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                #ifdef EDITOR_VISUALIZATION
                    float2 VizUV : TEXCOORD1;
                    float4 LightCoord : TEXCOORD2;
                #endif
            };

            Varyings MetaVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityMetaVertexPosition(input.positionOS.xyz, input.uv1, input.uv2);
                output.uv = TRANSFORM_TEX(input.uv0, _MainTex);

                #ifdef EDITOR_VISUALIZATION
                    UnityEditorVizData(input.positionOS.xyz, input.uv0, input.uv1, input.uv2, output.VizUV, output.LightCoord);
                #endif

                return output;
            }

            half4 ToonMetaFragment(Varyings input) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half3 albedo = texColor.rgb * _Color.rgb;

                MetaInput metaInput;
                metaInput.Albedo = albedo;
                metaInput.Emission = 0;
                #ifdef EDITOR_VISUALIZATION
                    metaInput.VizUV = input.VizUV;
                    metaInput.LightCoord = input.LightCoord;
                #endif

                return UnityMetaFragment(metaInput);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
