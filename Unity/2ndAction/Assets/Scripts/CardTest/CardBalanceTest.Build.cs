#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// CARD BALANCE TEST「ビルド」タブ(2026-10-02、カードバランス v3 で 2026-10-03 に作り直し)。
//  ・代表的な構成(CardBuildPresets、自動テストと共通)を Lv1/5/9 で、ゲームのカード適用処理(ApplyCardEffectsStacked)そのままで掛ける(先に RESET)。
//    カードはラン中の値だけを変え、所持/デッキ/保存は変えない。新しいラン(シーンの読み直し)で全部消える。
//  ・今の能力値: 実効攻撃(無条件 A / 条件 C)・攻撃速度・実効範囲・実速度・HP(封印)・Shield・EXP/MILE/出現・初撃/連撃中/締め
//    ・属性(炎/氷/雷/風/血)・吸収/回復・低HP・PHOENIX。
public partial class CardBalanceTest
{
    const int BuildTabRows = 22;
    int buildLv = 9;
    int presetPage;
    string buildNote = "";

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
        if (B(new Rect(bx + 136f, y, 150f, rowH), HitboxOverlay.Enabled ? "判定表示 ON" : "判定表示 OFF", HitboxOverlay.Enabled)) HitboxOverlay.Enabled = !HitboxOverlay.Enabled;
        y += rowH + gap;
        // 構成(5つずつのページ)
        var presets = CardBuildPresets.All;
        int pages = (presets.Length + 4) / 5;
        bx = x;
        if (B(new Rect(bx, y, 36f, rowH), "◀")) presetPage = (presetPage + pages - 1) % pages; bx += 40f;
        for (int i = presetPage * 5; i < Mathf.Min(presets.Length, presetPage * 5 + 5); i++)
        {
            if (B(new Rect(bx, y, 146f, rowH), presets[i].name)) ApplyBuild(presets[i].name, presets[i].cards);
            bx += 150f;
        }
        if (B(new Rect(bx, y, 36f, rowH), "▶")) presetPage = (presetPage + 1) % pages;
        y += rowH + gap;
        // 1枚だけ(カードLvタブと同じ選択)
        var cards = CardDatabase.AllCards.OrderBy(c => c.sortOrder).ToList();
        if (cards.Count > 0)
        {
            cardIndex = Mathf.Clamp(cardIndex, 0, cards.Count - 1);
            var c = cards[cardIndex];
            GUI.Label(new Rect(x, y, 92f, rowH), "1枚だけ", sLabel);
            if (B(new Rect(x + 92f, y, 32f, rowH), "◀")) cardIndex = (cardIndex + cards.Count - 1) % cards.Count;
            GUI.Label(new Rect(x + 128f, y, 190f, rowH), $"#{cardIndex + 1} {c.cardName}", sTitle);
            if (B(new Rect(x + 320f, y, 32f, rowH), "▶")) cardIndex = (cardIndex + 1) % cards.Count;
            if (B(new Rect(x + 360f, y, 150f, rowH), $"このカードをLv{buildLv}")) ApplyBuild(c.cardName, new[] { c.cardId + ":" + buildLv });
            GUI.Label(new Rect(x + 516f, y, W - 530f, rowH), c.description, sSmall);
        }
        y += rowH + gap;
        GUI.Label(new Rect(x, y, W - 16f, rowH), string.IsNullOrEmpty(buildNote) ? "構成を押すと RESET してから掛ける(全キャラ共通)" : "適用中: " + buildNote, sSmall);
        y += rowH + gap;
        if (pc == null || gm == null || !InRun()) { GUI.Label(new Rect(x, y, W - 16f, rowH), "ラン中に使う", sSmall); y += rowH + gap; return; }

        // ---- 今の能力値(v3)
        var T = gm.Card;
        float A = T.Get(EffectType.AttackPct), cs = pc.CardStateCondition();
        Line(x, ref y, rowH, gap, $"実効攻撃 {pc.EffectiveAttackPower}(基礎 {pc.AttackPower} × 無条件 {1f + CardRules.SoftAttack(A):0.00}[生{A * 100f:0}%] × 今の条件 {CardRules.CondMultiplier(cs):0.00}[生{cs * 100f:0}%])  ボス1発見積 {pc.BossHitEstimate}  連撃平均 {pc.BossHitComboAverage}");
        Line(x, ref y, rowH, gap, $"条件: 空中{T.Get(EffectType.AirPct) * 100f:0}% 地上{T.Get(EffectType.GroundPct) * 100f:0}% 初撃{T.Get(EffectType.FirstPct) * 100f:0}% 連撃中{T.Get(EffectType.ComboPct) * 100f:0}% 締め{T.Get(EffectType.FinisherPct) * 100f:0}% ボス{T.Get(EffectType.BossPct) * 100f:0}% 雑魚{T.Get(EffectType.MobPct) * 100f:0}% 空中の敵{T.Get(EffectType.AntiAirPct) * 100f:0}% 下{T.Get(EffectType.DownPct) * 100f:0}% 満HP{T.Get(EffectType.FullHpPct) * 100f:0}% 低HP{T.Get(EffectType.LowHp50Pct) * 100f:0}/+{T.Get(EffectType.LowHp25Pct) * 100f:0}%");
        Line(x, ref y, rowH, gap, $"攻撃速度: 攻撃時間 ×{pc.AttackSpeedMultiplier:0.00}(主攻撃 ×{pc.MainAttackSpeedMultiplier:0.00})  単発の流れの窓 {pc.SequenceResetWindow:0.00}s  実効範囲 ×{pc.AttackRangeMultiplier:0.00}(カード ×{pc.CardRangeFactor:0.00}、飛び道具の射程 ×{PlayerController.ProjectileTravelFactor:0.00})  追加攻撃 {T.Get(EffectType.DoubleAttackChance) * 100f:0}%");
        DrawBuildSpeedAndExtras(x, ref y, rowH, gap, pc, gm);
        Line(x, ref y, rowH, gap, $"HP {gm.Lives}/{gm.maxLives}(封印前 {gm.CardMaxLivesBeforeSeal}、封印 ハート{gm.SealedHearts}、上限 {gm.maxLivesCap})  Shield {pc.ShieldCharges}/{pc.ShieldCapacity}(回復 {pc.ShieldRechargeSeconds:0.0}s)  ジャンプ力 {pc.jumpForce:0.0}(×{pc.CardJumpFactor:0.00}) 回数 {pc.maxJumps}");
        Line(x, ref y, rowH, gap, $"吸収 {T.Get(EffectType.LifestealChance) * 100f:0}%(HP25%以下 +{T.Get(EffectType.LowHp25LifestealChance) * 100f:0}%) 回復 ハート{gm.HealHearts(1)}  PHOENIX Charge {gm.PhoenixCharges}(Lv{gm.CardLevel("phoenix")}、使用{gm.PhoenixConsumedCount})  SECOND WIND {gm.SecondWindReadyDistance:0}m〜  LAST CHANCE {(gm.LastChanceArmed ? "待機" : "使用済み")}  OVERDRIVE {(pc.OverdriveActive ? "発動中" : $"{pc.OverdriveCharge01 * 100f:0}%")}");
        Line(x, ref y, rowH, gap, $"敵: HP×{gm.EnemyHpMultiplier:0.00} 行動×{ChallengeSystem.EnemyActionScale:0.00} 精鋭{ChallengeSystem.EliteChance * 100f:0}%(出現{ChallengeSystem.EliteSpawned}) WANTED出現{ChallengeSystem.WantedSpawned}/撃破{ChallengeSystem.WantedKilled}  ボスHP×{gm.BossHpMultiplier:0.00}  生存の雑魚 {ChallengeSystem.LivingEnemies}(上限{CardRules.MaxLivingEnemiesForSpawn})");
        Line(x, ref y, rowH, gap, $"追加攻撃: 二重{CardProcs.DoubleAttacks} 衝撃波{CardProcs.Shockwaves} 貫通{CardProcs.PierceHits} 空中{CardProcs.AerialSlashes} 締め{CardProcs.ComboMasterHits} 地面{CardProcs.GroundBreakers} 音速{CardProcs.SonicSlashes} 爆発{CardProcs.ChainExplosions} 炎{CardProcs.Infernos} 竜巻{CardProcs.Tornados} 反撃{CardProcs.Counters}/{CardProcs.FlameCounters}  予算で省略 {CardProcs.ProcBudgetDrops}/{CardProcs.FxBudgetDrops}");
    }

    void Line(float x, ref float y, float rowH, float gap, string s)
    {
        GUI.Label(new Rect(x, y, W - 16f, rowH), s, sSmall);
        y += rowH + gap;
    }
}
#endif
