// 識別用オーラ(2026-10-14): 絵の形(アルファ)だけを使い、頂点色の1色で塗る(普通の重ね方)。
// SpriteGlow(加算)だと明るい空の上で色が白く飛んで見分けにくかったため。SpriteRenderer の color で色と濃さを決める。
Shader "OneMoreMile/SpriteSolid"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off ZWrite Off Lighting Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "../../Shaders/Zig.cginc" // 縦画面「斜め上から見る」のジグザグの道(表示だけ)
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
