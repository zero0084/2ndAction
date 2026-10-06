using UnityEngine;

// カードバランス v3(2026-10-03): このランのカードの値の合計(EffectType ごと)。
// GameManager.RecomputeCardStats がカードのLvから毎回作り直す(足し込みではない)。読むのはプレイヤー/敵/属性など。
public sealed class CardTotals
{
    readonly float[] v = new float[512];
    public float Get(EffectType t) { int i = (int)t; return i >= 0 && i < v.Length ? v[i] : 0f; }
    public void Add(EffectType t, float x) { int i = (int)t; if (i >= 0 && i < v.Length) v[i] += x; }
    public void Clear() { System.Array.Clear(v, 0, v.Length); }
    public bool Has(EffectType t) => Mathf.Abs(Get(t)) > 1e-6f;
    public int Int(EffectType t) => Mathf.RoundToInt(Get(t));
}
