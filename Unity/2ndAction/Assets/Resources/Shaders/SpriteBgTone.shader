// 背景の色味を控えめにする(2026-11-01、敵を見やすく)。背景の1枚絵/昼夜の重ね絵/空の雲だけが使う(BackgroundTone が付け替える)。
// 描き方は SpriteZig(= Sprites/Default + 縦画面の斜め表示の曲げ)と同じで、最後に色だけを整える:
//  ・細部をなだらかに(_OmmBgSoft テクセルぶん周りと混ぜる。0 なら何もしない)
//  ・彩度を落とす(_OmmBgDesat、0=元のまま)
//  ・明暗差を中間の色(_OmmBgMid)へ寄せて縮める(_OmmBgFlatten、0=元のまま)。一律に暗くはしない
//  ・敵が通る高さ(Player の足元あたりの横の帯、_OmmBgBand)だけは、上の3つを少し強める(_OmmBgBandAmt)。空の上の方は雰囲気を残す
// 値は全体共通(Shader.SetGlobal*)。設定されていない時は 0 = 元の絵と同じ結果になる。
Shader "OneMoreMile/SpriteBgTone"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex ZigSpriteVert
            #pragma fragment ToneFrag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"
            #include "../../Shaders/Zig.cginc"
            float4 _MainTex_TexelSize;
            float _OmmBgDesat, _OmmBgFlatten, _OmmBgSoft;
            float4 _OmmBgMid, _OmmBgBand, _OmmBgBandAmt;   // band = (中心の高さ world y, 半分の高さ, ふちの幅) / amt = (彩度, 明暗差, なだらか)
            struct v2fTone { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 texcoord : TEXCOORD0; float worldY : TEXCOORD1; };
            v2fTone ZigSpriteVert(appdata_t IN)
            {
                IN.vertex = ZigObject(IN.vertex);
                v2f s = SpriteVert(IN);
                v2fTone o;
                o.vertex = s.vertex; o.color = s.color; o.texcoord = s.texcoord;
                o.worldY = mul(unity_ObjectToWorld, IN.vertex).y;
                return o;
            }
            fixed4 ToneFrag(v2fTone IN) : SV_Target
            {
                float band = 1 - smoothstep(_OmmBgBand.y, _OmmBgBand.y + max(_OmmBgBand.z, 0.01), abs(IN.worldY - _OmmBgBand.x));
                float desat = _OmmBgDesat + _OmmBgBandAmt.x * band;
                float flatten = _OmmBgFlatten + _OmmBgBandAmt.y * band;
                float soft = _OmmBgSoft + _OmmBgBandAmt.z * band;
                fixed4 c = SampleSpriteTexture(IN.texcoord);
                if (soft > 0.001)
                {
                    float2 o = _MainTex_TexelSize.xy * soft;
                    fixed4 s = SampleSpriteTexture(IN.texcoord + float2(o.x, 0)) + SampleSpriteTexture(IN.texcoord - float2(o.x, 0))
                             + SampleSpriteTexture(IN.texcoord + float2(0, o.y)) + SampleSpriteTexture(IN.texcoord - float2(0, o.y));
                    c = c * 0.4 + s * 0.15;
                }
                c *= IN.color;
                float l = dot(c.rgb, float3(0.299, 0.587, 0.114));
                c.rgb = lerp(c.rgb, l.xxx, saturate(desat));
                c.rgb = lerp(c.rgb, _OmmBgMid.rgb, saturate(flatten));
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
