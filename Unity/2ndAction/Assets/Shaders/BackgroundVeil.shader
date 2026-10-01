// 背景の薄い幕(2026-10-01)。背景全体をごく薄く、Playerの周り(横長の楕円、ふちはなだらか)はもう少し、背景の明暗差と彩度を落とす。
// 背景の絵より手前・地面/敵/障害物/Playerより奥に置く(BackgroundVeilが並び順を決める)。1枚の四角、計算は数行だけ。
Shader "OneMoreMile/BackgroundVeil"
{
    Properties
    {
        _Color ("Color (a = strength at the center)", Color) = (0.1,0.12,0.18,0.2)
        _Center ("Center (world xy)", Vector) = (0,0,0,0)
        _Radii ("Radii (world xy)", Vector) = (6,3,0,0)
        _Soft ("Edge softness 0..1", Float) = 0.6
        _BaseA ("Strength everywhere (whole background)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off Lighting Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            float4 _Center, _Radii;
            float _Soft, _BaseA;
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float2 world : TEXCOORD0; };
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xy;
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float2 q = (i.world - _Center.xy) / max(_Radii.xy, 0.001);
                float d = length(q);
                // 画面全体にうすく(_BaseA) + Playerの周りほど濃く(_Color.a)
                float a = max(_BaseA, _Color.a * (1 - smoothstep(1 - _Soft, 1, d)));
                return fixed4(_Color.rgb, a);
            }
            ENDCG
        }
    }
}
