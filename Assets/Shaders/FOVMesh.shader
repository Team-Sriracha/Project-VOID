Shader "Custom/FOV/FOVMesh"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _EdgeColor ("Edge Color", Color) = (0, 0, 0, 0.7)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
        }

        // Stencil: FOV 영역에 1 쓰기
        Stencil
        {
            Ref 1
            Comp Always
            Pass Replace
        }

        // Why: FOV 경계의 soft edge를 위해 알파 블렌딩 사용
        ColorMask RGBA
        ZWrite Off
        ZTest LEqual // Depth Test 활성화: 벽 뒤/캐릭터 뒤는 Stencil 안 씀
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 _EdgeColor;
            float _ViewRadius; // C#에서 전달받음 (최대 시야 거리)
            float _FalloffExp; // C#에서 전달받음 (감쇠 곡선 지수)

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 objPos : TEXCOORD1; // Object Space 위치
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.objPos = v.vertex.xyz; // Object Space 위치 저장
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 안개 색상 (검은색 추천) - Alpha 값이 높을수록 어두워짐
                fixed4 col = _EdgeColor;
                
                // 1. Geometry Fade (벽 너머 Soft Edge)
                // UV.x: 0(Body) -> 1(Extension)
                // Extension 영역은 무조건 어두워짐(1.0)
                float geomFade = i.uv.x;

                // 2. Distance Fade (원거리 감쇠)
                // 중심(0)에서 반경(_ViewRadius)까지 0->1로 어두워짐
                float dist = length(i.objPos);
                float distFade = _ViewRadius > 0 ? (dist / _ViewRadius) : 0;
                distFade = pow(saturate(distFade), _FalloffExp); // 지수 파라미터 적용

                // 3. Side Fade (부채꼴 양옆 감쇠)
                // UV.y: 0(Left) -> 1(Right). 
                // SideLogic: 중심부(1) -> 가장자리(0). 어두워지려면 반전(1 - logic)
                float sideLogic = smoothstep(0.0, 0.15, i.uv.y) * smoothstep(1.0, 0.85, i.uv.y);
                float sideFade = 1.0 - sideLogic;

                // 최종 Alpha: 셋 중 하나라도 '어둠'이면 어두워짐 (Max 연산)
                // 기존 Overlay(어둠)와 자연스럽게 연결됨
                float finalAlpha = max(max(geomFade, distFade), sideFade);
                
                col.a *= finalAlpha;
                
                return col;
            }
            ENDCG
        }
    }

    FallBack Off
}
