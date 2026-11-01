// キャラ選択の立ち絵(紙に描いた幻想画)の外周を、黒い背景へ水彩が薄れるように透かす(2026-11-01)。元の絵はそのまま、表示だけ。
//  ・外周: 四辺から _Fade(絵の高さに対する割合)の幅で透明へ。縁は低い周波数のむらで少し不揃いにする(_Ragged)。角は2辺の掛け算で丸く
//  ・保護: 紙の色(_Paper)から離れた所(線・色・武器・布)と、紙より白い所(ペンタトゥットゥルー星人の体)は透かさない。
//    その周り(8px / 18px の輪の平均)にも淡い紙を残す → 黒い剣先などが黒い背景に溶けず、紙の上に描かれたまま見える
//  ・キャラごと(2026-11-01 ペンタ): 上の何も描いていない余白だけ内側から溶かす(_FadeTop)、紙の粒を残す所と見なさない(_InkLow を上げる)
//  ・uGUI の Image 用(UI/Default と同じ並べ方・ステンシル・RectMask2D の切り抜き)
Shader "UI/PaperFade"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Paper ("Paper color", Color) = (0.98, 0.925, 0.855, 1)
        _Fade ("Edge fade (fraction of height)", Float) = 0.09
        _FadeTop ("Top edge fade (fraction of height)", Float) = 0.09
        _InkLow ("Ink threshold (distance from paper)", Float) = 0.10
        _Ragged ("Ragged edge", Float) = 0.6
        _Aspect ("Width / height of the picture", Float) = 0.6667
        _Protect ("Protect ink / white (0-1)", Float) = 1

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 texcoord : TEXCOORD0; float4 worldPosition : TEXCOORD1; };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color, _Paper;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float _Fade, _FadeTop, _InkLow, _Ragged, _Aspect, _Protect;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            float hash (float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float vnoise (float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), u.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
            }
            // 紙から離れている度合い(線・色)と、紙より白い度合い(白い体)
            float Ink (float2 uv)
            {
                float3 c = tex2D(_MainTex, uv).rgb;
                float3 d = abs(c - _Paper.rgb);
                float ink = smoothstep(_InkLow, 0.32, max(d.r, max(d.g, d.b)));
                float white = smoothstep(0.04, 0.10, min(c.r, min(c.g, c.b)) - _Paper.b);
                return max(ink, white);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = (tex2D(_MainTex, i.texcoord) + _TextureSampleAdd) * i.color;
                float2 uv = i.texcoord;
                // 外周(高さを1とした距離)
                float ex = min(uv.x, 1.0 - uv.x) * _Aspect;
                float2 np = float2(uv.x * _Aspect, uv.y) * 9.0;
                float n = vnoise(np) * 0.65 + vnoise(np * 2.7) * 0.35;
                float j = (n - 0.5) * _Fade * _Ragged;
                float vign = smoothstep(0.0, _Fade, ex + j) * smoothstep(0.0, _Fade, uv.y + j) * smoothstep(0.0, _FadeTop, 1.0 - uv.y + j);
                // 保護: 中心は5点の平均(紙の粒を拾わない)、周りは輪の平均でなめらかな紙のふち
                float2 t = _MainTex_TexelSize.xy;
                float ink = (Ink(uv) + Ink(uv + float2(2, 0) * t) + Ink(uv - float2(2, 0) * t) + Ink(uv + float2(0, 2) * t) + Ink(uv - float2(0, 2) * t)) * 0.2;
                float prot = smoothstep(0.25, 0.75, ink);
                float r1 = 0, r2 = 0;
                const float s = 0.7071;
                float2 dirs[8] = { float2(1, 0), float2(-1, 0), float2(0, 1), float2(0, -1), float2(s, s), float2(-s, s), float2(s, -s), float2(-s, -s) };
                [unroll] for (int k = 0; k < 8; k++)
                {
                    r1 += Ink(uv + dirs[k] * 8.0 * t);
                    r2 += Ink(uv + dirs[k] * 18.0 * t);
                }
                prot = max(prot, max(saturate(r1 / 8.0 * 1.6), saturate(r2 / 8.0 * 1.2)));
                col.a *= max(vign, prot * _Protect);
                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                return col;
            }
            ENDCG
        }
    }
}
