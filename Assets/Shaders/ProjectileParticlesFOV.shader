Shader "Custom/FOV/ProjectileParticlesFOV"
{
    Properties
    {
        [MainTexture] _BaseTexture("Base Texture", 2D) = "white" {}
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainTexture] _MainTex("Main Tex", 2D) = "white" {}
        _TextureSample0("Texture Sample 0", 2D) = "white" {}

        [MainColor] _Color("Color", Color) = (1,1,1,1)
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        [HDR] _EmissionColor("Emission Color", Color) = (0,0,0,0)

        [ToggleUI] _AlphaClip("Alpha Clip", Float) = 0.0
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5

        [HideInInspector] _BlendOp("__blendop", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 5.0
        [HideInInspector] _DstBlend("__dst", Float) = 10.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 10.0
        [HideInInspector] _Cull("__cull", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 0.0
        [HideInInspector] _AlphaToMask("__alphaToMask", Float) = 0.0

        [HideInInspector] _GlobalStencilComp("Global Stencil Comp", Float) = 8.0
        [HideInInspector] _FOVRevealAnchorWS("FOV Reveal Anchor WS", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Stencil
            {
                Ref 3
                Comp [_GlobalStencilComp]
                ReadMask 1
                WriteMask 2
                Pass Replace
            }

            BlendOp[_BlendOp]
            Blend[_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]
            ZWrite[_ZWrite]
            Cull[_Cull]
            AlphaToMask[_AlphaToMask]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseTexture);
            SAMPLER(sampler_BaseTexture);
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_TextureSample0);
            SAMPLER(sampler_TextureSample0);

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseTexture_ST;
            float4 _BaseMap_ST;
            float4 _MainTex_ST;
            float4 _TextureSample0_ST;
            half4 _Color;
            half4 _BaseColor;
            half4 _EmissionColor;
            half _AlphaClip;
            half _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uvBaseTexture : TEXCOORD0;
                float2 uvBaseMap : TEXCOORD1;
                float2 uvMainTex : TEXCOORD2;
                float2 uvTextureSample0 : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            inline half4 SampleBestParticleTexture(Varyings input)
            {
                half4 baseTexture = SAMPLE_TEXTURE2D(_BaseTexture, sampler_BaseTexture, input.uvBaseTexture);
                half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uvBaseMap);
                half4 mainTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uvMainTex);
                half4 textureSample0 = SAMPLE_TEXTURE2D(_TextureSample0, sampler_TextureSample0, input.uvTextureSample0);

                half baseTextureScore = abs(baseTexture.r - 1.0h) + abs(baseTexture.g - 1.0h) + abs(baseTexture.b - 1.0h) + abs(baseTexture.a - 1.0h);
                half baseMapScore = abs(baseMap.r - 1.0h) + abs(baseMap.g - 1.0h) + abs(baseMap.b - 1.0h) + abs(baseMap.a - 1.0h);
                half mainTexScore = abs(mainTex.r - 1.0h) + abs(mainTex.g - 1.0h) + abs(mainTex.b - 1.0h) + abs(mainTex.a - 1.0h);
                half sample0Score = abs(textureSample0.r - 1.0h) + abs(textureSample0.g - 1.0h) + abs(textureSample0.b - 1.0h) + abs(textureSample0.a - 1.0h);

                half4 selected = baseTexture;
                half bestScore = baseTextureScore;

                if (baseMapScore > bestScore)
                {
                    selected = baseMap;
                    bestScore = baseMapScore;
                }

                if (mainTexScore > bestScore)
                {
                    selected = mainTex;
                    bestScore = mainTexScore;
                }

                if (sample0Score > bestScore)
                {
                    selected = textureSample0;
                }

                return selected;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uvBaseTexture = TRANSFORM_TEX(input.uv, _BaseTexture);
                output.uvBaseMap = TRANSFORM_TEX(input.uv, _BaseMap);
                output.uvMainTex = TRANSFORM_TEX(input.uv, _MainTex);
                output.uvTextureSample0 = TRANSFORM_TEX(input.uv, _TextureSample0);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 tex = SampleBestParticleTexture(input);
                half4 tint = tex * input.color * _Color * _BaseColor;
                tint.rgb += _EmissionColor.rgb * tex.a;

                if (_AlphaClip > 0.5h && tint.a < _Cutoff)
                {
                    discard;
                }

                return tint;
            }
            ENDHLSL
        }
    }
}
