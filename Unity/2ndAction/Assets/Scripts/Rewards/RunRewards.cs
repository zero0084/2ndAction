using UnityEngine;

// ラン中の報酬の決まり(2026-10-15、マスターの依頼「距離に応じた報酬・ボス報酬の増加・参加人数での MILE の分配」)。
//  ・雑魚の MILE: その敵が出た時の距離での強さ(HP の倍率 = 距離の段)に合わせて増やす。序盤(倍率 1)は今までと同じ。
//      係数 = 1 + 0.5 ×(HP の倍率 − 1)。0km 1.0 / 20km 2.0 / 50km 2.6 / 100km 4.0(HP は 1 / 3 / 4.2 / 7 倍)
//  ・ボスの MILE: 種類ごとの基本 × 1.5 ×(強さの係数)。強さの係数 = 1 + 0.5 ×(HP の倍率 − 1)、1〜2.5 に収める
//      (HP の倍率 = 距離で伸びる小ボスの倍率 / 再戦の強化 / ラスダンの倍率。ボスごとの難しさは種類の基本値に入っている)
//  ・ボスの EXP: 「次のレベルまでに要る EXP」に対する割合(どのレベルでも同じくらいの手応え。下限は以前の 100)
//      1km の小ボス 0.2 / 5km 0.35 / 10km の大ボス 0.8 / メカドラゴン・死神 1.2
//  ・MILE の分配: (基本 × 距離/強さの係数)÷ 参加人数 × 自分の補正(MILE 系のカード / GREED)。端数は種類ごとに内部で貯めて、1 になったら足す
//      人数 = ソロ 1 / CPU サポート: 自分 + 仲間(ランの始めの編成)/ 協力マルチ: 参加した人間(ランの始めの人数)。対戦は 1(今までどおりラストヒットの人だけ)
//      倒れている仲間・画面の外の仲間も数える。途中で抜けた人がいても、もらった分を計算し直さない(ランの始めの人数のまま)
//  ・EXP は分けない。
public static class RunRewards
{
    // ---- 雑魚 ----
    public const float EnemyDistanceWeight = 0.5f;
    public static float EnemyMileFactor(float hpRatio) => 1f + EnemyDistanceWeight * Mathf.Max(0f, hpRatio - 1f);

    // ---- ボス ----
    public const float BossMileBase = 1.5f, BossStrengthWeight = 0.5f, BossStrengthMax = 2.5f;
    public static float BossStrengthFactor(float hpScale) => Mathf.Clamp(1f + BossStrengthWeight * (hpScale - 1f), 1f, BossStrengthMax);
    public static int BossMile(int specMile, float hpScale) => Mathf.Max(0, Mathf.RoundToInt(specMile * BossMileBase * BossStrengthFactor(hpScale)));

    public enum BossClass { Small, Mid, Big, Elite }
    public static float BossExpFraction(BossClass c) => c == BossClass.Small ? 0.2f : c == BossClass.Mid ? 0.35f : c == BossClass.Big ? 0.8f : 1.2f;
    public const float BossExpMin = 100f; // 以前のボスの EXP(序盤はこれより下げない)

    // DeathKey("Wild/Wolf" 等)からボスの格。1km/5km の小ボス、10km の大ボス、特別な強敵
    public static BossClass ClassOf(string key)
    {
        if (string.IsNullOrEmpty(key)) return BossClass.Big;
        if (key == "Reaper" || key.EndsWith("/MechDragon")) return BossClass.Elite;
        string k = key.Substring(key.LastIndexOf('/') + 1);
        if (k == "Wolf" || k == "Centipede") return BossClass.Small;
        if (k == "GoblinRider" || k == "Scorpion" || k == "Majin") return BossClass.Mid;
        return BossClass.Big;
    }
}
