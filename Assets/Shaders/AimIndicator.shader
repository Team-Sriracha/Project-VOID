Shader "Custom/AimIndicator"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        _Emission ("Emission", Range(0, 2)) = 1.0
        _OutlineWidth ("Outline Width", Range(0, 0.5)) = 0.1
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+100" "RenderType" = "Transparent" }
        LOD 100

        ZWrite Off
        ZTest LEqual  // Why: 기본 깊이 테스트 사용 (벽에 가려짐)
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _Emission;
            float _OutlineWidth;
            float4 _OutlineColor;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * _Color;
                col.rgb *= _Emission;

                // Why: UV 경계선 감지하여 외곽선 그리기
                float2 uv = i.uv;
                float edgeX = min(uv.x, 1.0 - uv.x);
                float edgeY = min(uv.y, 1.0 - uv.y);
                float edge = min(edgeX, edgeY);

                // Why: 외곽선 영역이면 외곽선 색상 적용
                if (edge < _OutlineWidth)
                {
                    float outlineFactor = smoothstep(0, _OutlineWidth, edge);
                    col = lerp(_OutlineColor, col, outlineFactor);
                }

                return col;
            }
            ENDCG
        }
    }
}
