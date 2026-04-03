Shader "Hidden/ProjectVOID/FOVStencilWriter"
{
    SubShader
    {
        Tags
        {
            "Queue" = "Geometry-10"
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Stencil
        {
            Ref 1
            Comp Always
            Pass Replace
        }

        ColorMask 0
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
