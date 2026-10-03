#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// CARD BALANCE TEST「ビルド」タブ(2026-10-02 カードバランス調査)。
//  ・決まった構成(なし/代表的な数枚/攻撃 恒常最大/ボス実戦最大/速度特化)を Lv1/5/9 で、ゲームのカード適用処理(ApplyCardEffectsStacked)
//    そのままで掛ける(先に RESET)。キャラカード3枠+デッキ10枚 = 13枚ぶん。カードはラン中の値だけを変え、所持/デッキ/保存は変えない。
//  ・今の能力値を表示: 攻撃(条件ごと)・1発・速さ(km/h)・攻撃時間/範囲・ジャンプ・HP・Shield。
//  ・新しいラン(シーンの読み直し)で全部消える(通常のランへ持ち越さない)。
public partial class CardBalanceTest
{
    const int BuildTabRows = 18;
    int buildLv = 9;
    string buildNote = "";

    static readonly (string name, string[] cards)[] BuildPresets =
    {
        ("代表的な数枚", new[] { "attack_up:2", "thunder_strike:1", "boss_killer:1" }),
        ("攻撃 恒常最大", new[] { "deaths_contract", "giant_slayer", "ground_zero", "glass_cannon", "heavy_impact", "heart_breaker", "ground_breaker", "berserker", "high_voltage", "reverse_gear", "ultimate", "ground_fighter", "thunder_strike" }),
        ("ボス実戦最大", new[] { "overdrive", "combo_rush", "momentum", "deaths_contract", "ground_zero", "glass_cannon", "heavy_impact", "heart_breaker", "ground_breaker", "hunter", "berserker", "boss_killer", "blood_blade" }),
        ("速度特化", new[] { "speed_up", "greed", "no_turning_back", "close_call", "ultimate" }),
    };

    // 構成を掛ける("id" は buildLv、"id:n" は n)
    public void ApplyBuild(string name, string[] list)
    {
        var gm = GameManager.Instance;
        if (!InRun() || gm == null) return;
        ResetToBase();
        var applied = new List<string>();
        foreach (var e in list)
        {
            var kv = e.Split(':');
            var c = CardDatabase.FindBaseById(kv[0]);
            if (c == null) continue;
            int lv = kv.Length > 1 ? int.Parse(kv[1]) : buildLv;
            gm.ApplyCardEffectsStacked(c, lv);
            applied.Add($"{c.cardName} Lv{lv}");
        }
        buildNote = $"{name}: {string.Join(", ", applied)}";
        lastAction = $"ビルド「{name}」を適用(ゲームのカード処理そのまま、ラン中だけ)";
    }

    void DrawBuildTab(float x, ref float y, float rowH, float gap)
    {
        var pc = PlayerController.Instance; var gm = GameManager.Instance;
        GUI.Label(new Rect(x, y, 92f, rowH), "Lv", sLabel);
        float bx = x + 92f;
        foreach (int lv in new[] { 1, 5, 9 }) { if (B(new Rect(bx, y, 56f, rowH), $"Lv{lv}", buildLv == lv)) buildLv = lv; bx += 60f; }
        if (B(new Rect(bx + 8f, y, 120f, rowH), "カードなし")) { ResetToBase(); buildNote = "カードなし(キャラの基準値)"; }
        if (B(new Rect(bx + 136f, y, 150f, rowH), HitboxOverlay.Enabled ? "判定表示 ON" : "判定表示 OFF", HitboxOverlay.Enabled)) HitboxOverlay.Enabled = !HitboxOverlay.Enabled; // 赤=攻撃 緑=被弾 黄=敵 紫=敵の攻撃
        y += rowH + gap;
        bx = x;
        foreach (var p in BuildPresets) { if (B(new Rect(bx, y, 200f, rowH), p.name)) ApplyBuild(p.name, p.cards); bx += 204f; }
        y += rowH + gap;
        // 1枚だけ(カードLvタブと同じ選択)
        var cards = CardDatabase.AllCards.OrderBy(c => c.sortOrder).ToList();
        if (cards.Count > 0)
        {
            cardIndex = Mathf.Clamp(cardIndex, 0, cards.Count - 1);
            var c = cards[cardIndex];
            GUI.Label(new Rect(x, y, 92f, rowH), "1枚だけ", sLabel);
            if (B(new Rect(x + 92f, y, 32f, rowH), "◀")) cardIndex = (cardIndex + cards.Count - 1) % cards.Count;
            GUI.Label(new Rect(x + 128f, y, 190f, rowH), c.cardName, sTitle);
            if (B(new Rect(x + 320f, y, 32f, rowH), "▶")) cardIndex = (cardIndex + 1) % cards.Count;
            if (B(new Rect(x + 360f, y, 150f, rowH), $"このカードをLv{buildLv}")) ApplyBuild(c.cardName, new[] { c.cardId + ":" + buildLv });
            GUI.Label(new Rect(x + 516f, y, W - 530f, rowH), string.Join(", ", c.effects.Select(e => $"{e.type} {(e.value >= 0 ? "+" : "")}{e.value:0.##}")), sSmall);
        }
        y += rowH + gap;
        GUI.Label(new Rect(x, y, W - 16f, rowH), string.IsNullOrEmpty(buildNote) ? "構成を押すと RESET してから13枚ぶんを掛ける(全キャラ共通の構成。魔法は空中の構成の方が強い)" : "適用中: " + buildNote, sSmall);
        y += rowH + gap;
        if (pc == null || gm == null || !InRun()) { GUI.Label(new Rect(x, y, W - 16f, rowH), "ラン中に使う", sSmall); y += rowH + gap; return; }

        // ---- 今の能力値
        float mom = Mathf.Max(0f, pc.NaturalMultiplierAt(pc.DistanceFromStart) - 1f);
        int baseAtk = pc.AttackPower;
        int ground = baseAtk + pc.GroundAttackPowerBonus, air = baseAtk + pc.AirAttackPowerBonus;
        int momB = Mathf.RoundToInt(pc.MomentumBonus * mom);
        int full = pc.FullHpAttackBonus, low = pc.LowHpAttackBonus;
        int boss = pc.BossDamageBonus;
        Line(x, ref y, rowH, gap, $"攻撃力 {baseAtk}  地上 {ground} / 空中 {air}  初撃 +{pc.FirstHitBonus}  締め +{pc.ComboFinalStageBonus}  満HP +{full}  瀕死 +{low}×減った割合  加速 +{momB}(MOMENTUM {pc.MomentumBonus}×{mom:0.00})  ボス +{boss}");
        int hitBoss = Mathf.Max(1, ground + momB + full + boss);
        int hitBig = Mathf.Max(1, ground + momB + full + boss + Mathf.Max(pc.FirstHitBonus, pc.ComboFinalStageBonus));
        Line(x, ref y, rowH, gap, $"1発(技の倍率×1): ボス・地上・満HP・今の速度 {hitBoss:N0} / +初撃か締めの大きい方 {hitBig:N0} / BREAK×1.35 {Mathf.RoundToInt(hitBig * 1.35f):N0}   今の1発(実際の状態) {pc.EffectiveBossAttackPower:N0}");
        DrawBuildSpeedAndExtras(x, ref y, rowH, gap, pc, gm);
        Line(x, ref y, rowH, gap, $"ジャンプ力 {pc.jumpForce:0.0}(高さ約×{Mathf.Pow(pc.jumpForce / Mathf.Max(0.01f, pc.CardTestBaseJumpForce), 2f):0.0})  ジャンプ回数 {pc.maxJumps}  HP {gm.Lives}/{gm.maxLives}(上限{gm.maxLivesCap})  Shield {pc.ShieldCharges}  ボスHP×{gm.BossHpMultiplier:0.0}");
        Line(x, ref y, rowH, gap, "理論DPS = 1発 × 技の倍率 × 1秒の攻撃回数(標準の剣は 1発/0.4秒×攻撃時間)。実測は QaSweep -qaBossKill -qaBkHp 999999");
    }

    void Line(float x, ref float y, float rowH, float gap, string s)
    {
        GUI.Label(new Rect(x, y, W - 16f, rowH), s, sSmall);
        y += rowH + gap;
    }
}
#endif
