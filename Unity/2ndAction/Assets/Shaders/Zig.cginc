// 縦画面「斜め上から見る」のジグザグの道(2026-10-09、依頼G-3)。表示だけ: 頂点の世界座標の Z を、プレイヤーから先へ行くほど
// 左右(斜めカメラでは画面の横)へずらす。判定(2D の X/Y)・位置・順番は変えない。_ZigOn が 0 の間は何もしない(通常の表示と同じ)。
//  ずらす量 = _ZigAmp × 立ち上がり(プレイヤーから _ZigStart 先から _ZigRamp かけて 0→1)× sin(2π × 論理X / _ZigWave)
//  曲がりは道の位置(論理X = 世界X + _ZigPhase)に固定 → 近づくほど立ち上がりが下がって自然にまっすぐになる(急に飛ばない)
#ifndef OMM_ZIG_INCLUDED
#define OMM_ZIG_INCLUDED
float _ZigOn, _ZigAmp, _ZigStart, _ZigRamp, _ZigWave, _ZigOriginX, _ZigPhase;

inline float3 ZigWorld(float3 w)
{
    if (_ZigOn < 0.5) return w;
    float d = w.x - _ZigOriginX;
    float f = smoothstep(_ZigStart, _ZigStart + max(0.01, _ZigRamp), d);
    w.z += _ZigAmp * f * sin((w.x + _ZigPhase) * 6.2831853 / max(1.0, _ZigWave));
    return w;
}

inline float4 ZigObject(float4 v)
{
    if (_ZigOn < 0.5) return v;
    float4 w = mul(unity_ObjectToWorld, v);
    w.xyz = ZigWorld(w.xyz);
    return mul(unity_WorldToObject, w);
}
#endif
