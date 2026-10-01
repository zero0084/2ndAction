// Playerの輪郭補助(2026-10-01)。絵の外側だけに2重の細い縁を描く(内側=暗い縁 / 外側=明るい縁)。
// 本体の絵より1つ奥に、絵より少し大きい四角(PlayerReadabilityが作る)で描く。
// 絵の範囲(_UvRect)の外は透明として扱う(アトラスの隣の絵を拾わない)。
// 本数: 中心1 + 8方向×2重 = 17回のテクスチャ読み(Playerの周りの小さな四角だけ。全画面の後処理ではない)。
Shader "OneMoreMile/SpriteRim"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _UvRect ("UV rect (min xy, max zw)", Vector) = (0,0,1,1)
        _Step1 ("Inner ring step (uv)", Vector) = (0.004,0.004,0,0)
        _Step2 ("Outer ring step (uv)", Vector) = (0.008,0.008,0,0)
        _DarkColor ("Inner (dark) color, a=strength", Color) = (0.02,0.03,0.08,0.5)
        _LightColor ("Outer (light) color, a=strength", Color) = (0.92,0.95,1,0.3)
        _Alpha ("Overall alpha", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Cull Off ZWrite Off Lighting Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _UvRect, _Step1, _Step2;
            fixed4 _DarkColor, _LightColor;
            float _Alpha;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            float A(float2 uv)
            {
                float2 inside = step(_UvRect.xy, uv) * step(uv, _UvRect.zw);
                return tex2D(_MainTex, uv).a * inside.x * inside.y;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float c = A(i.uv);
                float r1 = 0, r2 = 0;
                const float d = 0.7071;
                float2 dirs[8] = { float2(1,0), float2(-1,0), float2(0,1), float2(0,-1), float2(d,d), float2(-d,d), float2(d,-d), float2(-d,-d) };
                [unroll] for (int k = 0; k < 8; k++)
                {
                    r1 = max(r1, A(i.uv + dirs[k] * _Step1.xy));
                    r2 = max(r2, A(i.uv + dirs[k] * _Step2.xy));
                }
                float ring1 = r1 * (1 - c);
                float ring2 = saturate(r2 - r1) * (1 - c);
                float a1 = ring1 * _DarkColor.a;
                float a2 = ring2 * _LightColor.a;
                float a = a1 + a2;
                fixed3 rgb = (_DarkColor.rgb * a1 + _LightColor.rgb * a2) / max(a, 0.0001);
                return fixed4(rgb, saturate(a) * _Alpha);
            }
            ENDCG
        }
    }
}
