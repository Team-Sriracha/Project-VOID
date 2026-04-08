Shader "Custom/FOV/FOVOverlay"
{
    Properties
    {
        _FogColor ("Fog Color", Color) = (0, 0, 0, 0.7)
        _FogIntensity ("Fog Intensity", Range(0, 1)) = 0.7
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "RenderType" = "Transparent"
        }

        // Stencil: FOV 영역(1)이 아닌 곳만 렌더링
        Stencil
        {
            Ref 2
            Comp NotEqual
            Pass Keep
            ReadMask 2
        }

        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 _FogColor;
            float _FogIntensity;

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            // Fullscreen Triangle
            v2f vert (uint vertexID : SV_VertexID)
            {
                v2f o;
                
                float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
                o.vertex = float4(uv * 2.0 - 1.0, 0.0, 1.0);
                
                #if UNITY_UV_STARTS_AT_TOP
                o.vertex.y = -o.vertex.y;
                #endif
                
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = _FogColor;
                col.a *= _FogIntensity;
                return col;
            }
            ENDCG
        }
    }

    FallBack Off
}
