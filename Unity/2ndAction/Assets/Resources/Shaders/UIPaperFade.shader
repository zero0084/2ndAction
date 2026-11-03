// キャラ選択の立ち絵(紙に描いた幻想画)の外周を、黒い背景へ水彩が薄れるように透かす(2026-11-01)。元の絵はそのまま、表示だけ。
//  ・外周: 四辺から _Fade(絵の高さに対する割合)の幅で透明へ。縁は低い周波数のむらで少し不揃いにする(_Ragged)。角は2辺の掛け算で丸く
//  ・保護: 紙の色(_Paper)から離れた所(線・色・武器・布)と、紙より白い所(ペンタトゥットゥルー星人の体)は透かさない。
//    その周り(8px / 18px の輪の平均)にも淡い紙を残す → 黒い剣先などが黒い背景に溶けず、紙の上に描かれたまま見える
//  ・キャラごと(2026-11-01 ペンタ): 上の何も描いていない余白だけ内側から溶かす(_FadeTop)、紙の粒を残す所と見なさない(_InkLow を上げる)
//  ・見えている範囲(2026-11-03): キャラごとに人物の大きさをそろえるため絵を表示枠より大きく出す時は、外周の透かしを「表示枠の中で見えている所」の
//    四辺から測る(_Vis = 見えている所の UV の範囲、_VisOn = 1)。枠で切れる所は線・布・武器も含めて内側 _VisCut の幅で薄れる(硬い切り口を作らない)
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
        _Vis ("Visible UV rect (u0, v0, u1, v1)", Vector) = (0, 0, 1, 1)
        _VisOn ("Fade from the visible rect", Float) = 0
        _VisCut ("Fade width at a cut edge (fraction of height)", Float) = 0.035

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
            float _Fade, _FadeTop, _InkLow, _Ragged, _Aspect, _Protect, _VisOn, _VisCut;
            float4 _Vis;

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
                // 外周(高さを1とした距離)。_VisOn の時は見えている範囲の四辺から(vuv = 見えている範囲の中の位置、asp = その縦横比)
                float2 vsz = max(_Vis.zw - _Vis.xy, 1e-4);
                float2 vuv = _VisOn > 0.5 ? (uv - _Vis.xy) / vsz : uv;
                float asp = _VisOn > 0.5 ? _Aspect * vsz.x / vsz.y : _Aspect;
                float ex = min(vuv.x, 1.0 - vuv.x) * asp;
                float2 np = float2(uv.x * _Aspect, uv.y) * 9.0;
                float n = vnoise(np) * 0.65 + vnoise(np * 2.7) * 0.35;
                float j = (n - 0.5) * _Fade * _Ragged;
                float vign = smoothstep(0.0, _Fade, ex + j) * smoothstep(0.0, _Fade, vuv.y + j) * smoothstep(0.0, _FadeTop, 1.0 - vuv.y + j);
                // 枠で切れる辺(絵の端より内側にある辺)だけ、線・布・武器も薄れさせる
                float cut = 1.0;
                if (_VisOn > 0.5)
                {
                    float cj = (n - 0.5) * _VisCut * 0.8;
                    if (_Vis.x > 0.001) cut *= smoothstep(0.0, _VisCut, vuv.x * asp + cj);
                    if (_Vis.z < 0.999) cut *= smoothstep(0.0, _VisCut, (1.0 - vuv.x) * asp + cj);
                    if (_Vis.y > 0.001) cut *= smoothstep(0.0, _VisCut, vuv.y + cj);
                    if (_Vis.w < 0.999) cut *= smoothstep(0.0, _VisCut, 1.0 - vuv.y + cj);
                }
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
                col.a *= max(vign, prot * _Protect) * cut;
                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                return col;
            }
            ENDCG
        }
    }
}
