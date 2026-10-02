// 自然洞窟(2026-09-21) - 画面全体を覆う1枚のクアッドで「薄暗さ+プレイヤー周辺の
// 明かり+たいまつの暖色光」を描く。ライトごとの重ね描きや動的ライトは使わず、
// 1ドロー・軽量なフラグメント計算のみ(スマホ負荷対策)。
// パス1: 暗さ(通常アルファ合成)。パス2: たいまつの暖色を加算。
Shader "OneMoreMile/CaveDarkness"
{
    Properties
    {
        _DarkColor ("Dark Color", Color) = (0.02, 0.03, 0.06, 1)
        _Dark ("Max Darkness", Range(0,1)) = 0.72
        _Player ("Player Light (x,y,rx,ry)", Vector) = (0,0,8,6)
        _PlayerSoft ("Player Softness (inner ratio)", Range(0,0.95)) = 0.25
        _PlayerBoost ("Player Intensity", Range(0,1)) = 1
        _PlayerSpot ("Player Spot (x,y,radius,intensity)", Vector) = (0,0,1,0)
        _TorchCount ("Torch Count", Float) = 0
        _TorchColor ("Torch Color", Color) = (1.0, 0.55, 0.2, 1)
        _TorchWarm ("Torch Warm Add", Range(0,1)) = 0.35
    }
    SubShader
    {
        Tags { "Queue"="Transparent+50" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"
        fixed4 _DarkColor;
        float _Dark;
        float4 _Player;
        float _PlayerSoft;
        float _PlayerBoost;
        float _TorchCount;
        fixed4 _TorchColor;
        float _TorchWarm;
        float4 _Torch[8]; // x,y,radius,intensity
        float4 _PlayerSpot; // 2026-10-01: Playerの体のまわりだけの小さな明かり(暖色は足さない)

        struct v2f { float4 pos : SV_POSITION; float2 wpos : TEXCOORD0; };

        v2f vert (appdata_base v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.wpos = mul(unity_ObjectToWorld, v.vertex).xy;
            return o;
        }

        float Falloff(float dist, float inner)
        {
            // 内側は満点、外周へ向かって滑らかに0へ(境界の柔らかい光)
            float t = saturate((dist - inner) / max(0.001, 1.0 - inner));
            float s = 1.0 - t;
            return s * s * (3.0 - 2.0 * s);
        }

        float LitAmount(float2 p)
        {
            float2 d = (p - _Player.xy) / max(float2(0.001, 0.001), _Player.zw);
            float lit = Falloff(length(d), _PlayerSoft) * _PlayerBoost;
            for (int i = 0; i < 8; i++)
            {
                if (i >= (int)_TorchCount) break;
                float2 t = (p - _Torch[i].xy) / max(0.001, _Torch[i].z);
                lit = max(lit, Falloff(length(t), 0.0) * _Torch[i].w);
            }
            lit = max(lit, Falloff(length((p - _PlayerSpot.xy) / max(0.001, _PlayerSpot.z)), 0.35) * _PlayerSpot.w);
            return saturate(lit);
        }
        ENDCG

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target
            {
                float lit = LitAmount(i.wpos);
                return fixed4(_DarkColor.rgb, _Dark * (1.0 - lit));
            }
            ENDCG
        }

        Pass
        {
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target
            {
                float3 warm = 0;
                for (int k = 0; k < 8; k++)
                {
                    if (k >= (int)_TorchCount) break;
                    float2 t = (i.wpos - _Torch[k].xy) / max(0.001, _Torch[k].z);
                    float f = Falloff(length(t), 0.0);
                    warm += _TorchColor.rgb * f * f * _Torch[k].w;
                }
                return fixed4(warm * _TorchWarm, 0);
            }
            ENDCG
        }
    }
}
