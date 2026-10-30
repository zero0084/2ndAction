using System.Text;
using UnityEngine;

// 属性の実戦の計測(2026-10-30)。「発動しているが見えない」のか「そもそも弱い」のかを分けるための数え(性能には触れない)。
//  ・命中(Hits): プレイヤーの攻撃が敵/ボスに当たった回数(ElementSystem.OnPlayerHit = すべての命中が通る1か所)と、その直接のダメージ
//  ・属性ごと: 発動(Procs)/ 状態の付与(Status)/ 属性のダメージ(Damage)/ 属性のダメージで倒した数(Kills)/ 連鎖の数・回復の量など
//  ・属性ではないカードの追加攻撃(CHAIN EXPLOSION / COUNTER など)は DealQuiet の属性の札が付いていても Other に数える(Suppress)
public static class ElementTelemetry
{
    public const int N = 6; // ElementType の数(None/Thunder/Fire/Ice/Wind/Blood)
    public static long Hits, DirectDamage;
    public static readonly long[] Procs = new long[N], Status = new long[N], Damage = new long[N], Kills = new long[N];
    public static long EnemyDeaths; // このプレイヤーが倒した雑魚の数(CardProcs.OnEnemyKilled。穴への落下は除く)
    public static long Chains, Freezes, Shatters, Tornados, Infernos, LifestealHeals, BleedHealHp, OtherDamage, OtherKills;
    public static bool Suppress; // 属性ではない追加攻撃の間(CardProcs.ChainExplosion 等)
    public static float Since;

    public static void Reset()
    {
        Hits = DirectDamage = 0; EnemyDeaths = 0;
        for (int i = 0; i < N; i++) Procs[i] = Status[i] = Damage[i] = Kills[i] = 0;
        Chains = Freezes = Shatters = Tornados = Infernos = LifestealHeals = BleedHealHp = OtherDamage = OtherKills = 0;
        Since = Time.time;
    }

    public static void OnHit(int damage) { Hits++; DirectDamage += Mathf.Max(0, damage); }
    public static void Proc(ElementType e) => Procs[(int)e]++;
    public static void StatusOn(ElementType e) => Status[(int)e]++;

    public static void OnDamage(ElementType e, int dmg, bool killed)
    {
        if (Suppress || e == ElementType.None) { OtherDamage += dmg; if (killed) OtherKills++; return; }
        Damage[(int)e] += dmg;
        if (killed) Kills[(int)e]++;
    }

    static readonly string[] Names = { "None", "Thunder", "Fire", "Ice", "Wind", "Blood" };
    public static string Line(ElementType e)
    {
        int i = (int)e;
        float per100 = Hits > 0 ? Procs[i] * 100f / Hits : 0f;
        long total = DirectDamage + OtherDamage; for (int k = 0; k < N; k++) total += Damage[k];
        float share = total > 0 ? Damage[i] * 100f / total : 0f;
        return $"{Names[i]}: procs {Procs[i]} ({per100:0.#}/100 hits) status {Status[i]} damage {Damage[i]} ({share:0.#}% of all) kills {Kills[i]}";
    }

    public static string Report()
    {
        var sb = new StringBuilder();
        long total = DirectDamage + OtherDamage; for (int k = 0; k < N; k++) total += Damage[k];
        sb.Append($"hits {Hits} direct {DirectDamage} total {total} other {OtherDamage}/{OtherKills}k chains {Chains} freezes {Freezes} shatters {Shatters} tornados {Tornados} infernos {Infernos} lifesteal {LifestealHeals} bleedHeal {BleedHealHp}");
        foreach (ElementType e in new[] { ElementType.Fire, ElementType.Ice, ElementType.Thunder, ElementType.Wind, ElementType.Blood }) sb.Append("\n  ").Append(Line(e));
        return sb.ToString();
    }
}
