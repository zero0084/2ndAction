// 攻撃の派手さ(2026-09-30) - 絵の形(アルファ)だけを使い、頂点色で塗って加算合成する(光る残像/閃光/光の筋)。
// 暗い色のキャラクターの残像でも明るく光って見える。SpriteRendererのcolorで色と濃さを決める。
Shader "OneMoreMile/SpriteGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off ZWrite Off Lighting Off
        Blend SrcAlpha One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Zig.cginc" // 縦画面「斜め上から見る」のジグザグの道(表示だけ、2026-10-09)
            sampler2D _MainTex;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(ZigObject(v.vertex));
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                fixed a = tex2D(_MainTex, i.uv).a;
                return fixed4(i.color.rgb, a * i.color.a);
            }
            ENDCG
        }
    }
}
