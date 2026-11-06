#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// 共通カードの追加(2026-11-06、18 枚)とマップ分類の解禁の試験。 -qaNewCards <dir> [-ncOnly ABCD]
//  A: データ(アセット・名前・説明・レア度・解放・分類・Lv1/Lv9 の効果の行・英語の文・初期デッキが変わらない)→ newcards_values.tsv
//  B: 報酬(実際のゲームの経路: 命中/撃破/ボス撃破/距離/時間 → MILE/EXP が式どおり増えるか。Lv1 と Lv9)
//  C: リスク(高火力敵/空中雑魚/判断イベント/ボスのダメージが実際に増えるか)
//  D: マップ分類の解禁(到達距離・ラスダン・既存データの引き継ぎ・新規データ・ガチャの候補)
// 保存は試験の前に控え、最後に戻す(-memSave で動かすこと)
public partial class QaSweep
{
    static readonly int[] NcLvs = { 1, 9 };

    IEnumerator NewCardsMode()
    {
        var snapSave = SaveSystem.Capture();
        autoPickHold = true;
        string only = Arg("-ncOnly", "ABCD");
        CardRewards.ResetCounters();
        if (only.Contains('A')) NcData();
        if (only.Contains('D')) NcUnlock();
        if (only.Contains('B')) yield return NcRewards();
        if (only.Contains('C')) yield return NcRisk();
        GameManager.BlockExpGain = false;
        autoPickHold = false;
        SaveSystem.Restore(snapSave);
        CardInventory.ReloadFromPrefs();
        RunCheckpoint.Reload();
        CardMapUnlock.Reload(); UnlockRules.Reload(); ProgressStats.Reload();
        L("[newcards] test machine save restored");
    }

    static bool NcNear(float a, float b, float tol = 0.02f) => Mathf.Abs(a - b) <= Mathf.Max(tol, Mathf.Abs(b) * tol);

    // ---------------------------------------------------------------- A
    void NcData()
    {
        var sb = new StringBuilder("id\tname\tnameJa\trarity\tstage\tunlock\tcategory\tmap\tlv1\tlv9\tlv1En\tlv9En\tprimary\n");
        for (int i = 0; i < LocCardCommon.Ids.Length; i++)
        {
            string id = LocCardCommon.Ids[i];
            var c = CardDatabase.FindBaseById(id);
            Check(c != null, $"[A] asset exists: {id}");
            if (c == null) continue;
            var t = LocCardCommon.Find(id);
            int wantR = i < 10 ? 1 : i < 14 ? 3 : 4;
            Check(c.cardName == t.name, $"[A] {id} name {c.cardName} == {t.name}");
            Check(c.description == t.desc, $"[A] {id} description from LocCardCommon");
            Check(c.rarity == wantR, $"[A] {id} rarity {c.rarity} == {wantR}");
            Check(c.unlockDistance == (wantR >= 4 ? 20000f : wantR == 3 ? 5000f : 0f) && c.gachaStage == (wantR >= 4 ? 3 : wantR == 3 ? 2 : 1), $"[A] {id} unlock {c.unlockDistance}m stage {c.gachaStage}");
            Check(CardMapCategories.Of(c) == CardMapCategory.Common, $"[A] {id} is Common");
            Check(c.sortOrder > 195, $"[A] {id} sortOrder {c.sortOrder} after the existing 112 (initial deck unchanged)");
            Check(c.icon != null, $"[A] {id} has an icon (placeholder ok)");
            string l1 = CardVariant.AbilityEffectTextAt(id, 1), l9 = CardVariant.AbilityEffectTextAt(id, 9);
            Check(!string.IsNullOrEmpty(l1) && !string.IsNullOrEmpty(l9) && l1 != l9, $"[A] {id} effect line Lv1 '{l1}' / Lv9 '{l9}'");
            Check(Loc.EnglishOf(c.description) != c.description, $"[A] {id} English description connected");
            string prev = Loc.Current; Loc.Set("en", false);
            string e1 = Loc.Auto(l1), e9 = Loc.Auto(l9);
            Loc.Set(prev, false);
            Check(!System.Text.RegularExpressions.Regex.IsMatch(e1 + e9, "[぀-ヿ㐀-鿿]"), $"[A] {id} English effect lines have no Japanese left: '{e1}' / '{e9}'");
            sb.Append(id).Append('\t').Append(c.cardName).Append('\t').Append(t.nameJa).Append('\t').Append(c.rarity).Append('\t').Append(c.gachaStage).Append('\t').Append(c.unlockDistance)
              .Append('\t').Append(c.category).Append('\t').Append(CardMapCategories.Of(c)).Append('\t').Append(l1).Append('\t').Append(l9).Append('\t').Append(e1).Append('\t').Append(e9)
              .Append('\t').Append(CardEffectFormat.FormatPrimaryValue(c)).Append('\n');
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "newcards_values.tsv"), sb.ToString());
        // 全カードが分類の表にある(書き忘れは Common 扱いになるので、表に無いカードを数える)
        var missing = CardDatabase.AllCards.Where(c => !CardMapCategoryTable.Rows.Any(r => r.id == c.cardId)).Select(c => c.cardId).ToList();
        Check(missing.Count == 0, $"[A] every card is in CardMapCategoryTable (missing: {string.Join(",", missing)})");
        var counts = CardDatabase.AllCards.GroupBy(c => CardMapCategories.Of(c)).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}");
        L($"[A] cards={CardDatabase.AllCards.Count} map categories: {string.Join(" ", counts)}");
        // 初期デッキ(新規データ = BEST 0m)は分類の前と同じ 12 枚
        string[] want = { "speed_up", "speed_down", "attack_up", "jump_power_up", "jump_count_up", "heart_up", "attack_range_up", "attack_speed_up", "air_attack_up", "shield", "exp_up", "vampire" };
        var deck = DefaultSave.StartingDeck(GameManager.DeckCapacity);
        Check(deck.SequenceEqual(want), $"[A] initial deck unchanged: {string.Join(",", deck)}");
        Check(LocPendingReview.Pending.Length == 0 && Loc.Ready("連続ヒット(画面の「N HIT」)が10回に届くたびにMILE+1(1Lvごとに+1)。20回・30回…と続けるほど何度ももらえる(1回の連続ヒットで100HITまで)") , $"[A] new strings translated (pending {LocPendingReview.Pending.Length})"); // 2026-11-06: 28 言語へ反映済み
    }

    // ---------------------------------------------------------------- D
    void NcUnlock()
    {
        bool devOpen = GachaStage.DevAllCardsOpen;
        GachaStage.DevAllCardsOpen = false;
        void Reach(string stage, double m) { SaveStore.SetString(UnlockRules.ReachPrefix + stage, m.ToString("R", System.Globalization.CultureInfo.InvariantCulture)); UnlockRules.Reload(); }
        void Grand(string ids) { SaveStore.SetString(CardMapUnlock.GrandfatherKey, ids); CardMapUnlock.Reload(); }
        void LastDungeon(bool on) { SaveStore.SetInt(SaveKeys.FinalDungeonUnlocked, on ? 1 : 0); ProgressStats.Reload(); }
        bool Open(string id, float best = 200000f) { var c = CardDatabase.FindBaseById(id); return c != null && GachaStage.IsCardEligible(c, best, GachaStage.StageForDistance(best)); }

        Grand(""); LastDungeon(false);
        foreach (var m in new[] { "wasteland_road", "natural_cave", "sky_corridor" }) Reach(m, 0);
        Check(Open("speed_up") && Open("combo_pay", 0f), "[D] Common cards follow the old rule (combo_pay at BEST 0m)");
        Check(Open("flame_blade"), "[D] Wilderness card with unlock 0m is open from the start");
        Check(!Open("burning_soul"), "[D] Wilderness 5,000m card needs 5,000m on Wasteland Road (BEST elsewhere is not enough)");
        Reach("wasteland_road", 4999); Check(!Open("burning_soul"), "[D] burning_soul locked at 4,999m");
        Reach("wasteland_road", 5000); Check(Open("burning_soul"), "[D] burning_soul opens at 5,000m on Wasteland Road");
        Check(!Open("shield"), "[D] Cave card (shield) locked before the Natural Cave");
        Reach("natural_cave", 999); Check(!Open("shield"), "[D] shield locked at 999m on the cave");
        Reach("natural_cave", 1000); Check(Open("shield"), "[D] shield opens at 1,000m (first boss) on the cave");
        Check(!Open("ice_prison"), "[D] ice_prison (5,000m) still locked at 1,000m on the cave");
        Reach("natural_cave", 5000); Check(Open("ice_prison"), "[D] ice_prison opens at 5,000m on the cave");
        Check(!Open("ice_prison", 4000f), "[D] the old BEST/gacha-stage rule still applies too");
        Check(!Open("air_attack_up"), "[D] Sky card locked before the Sky Corridor");
        Reach("sky_corridor", 1000); Check(Open("air_attack_up"), "[D] air_attack_up opens at 1,000m on the Sky Corridor");
        Check(!Open("character_ultimate"), "[D] Last Dungeon card locked while the Last Dungeon is locked");
        LastDungeon(true); Check(Open("character_ultimate"), "[D] Last Dungeon card opens with the Last Dungeon");
        LastDungeon(false);
        foreach (var m in new[] { "wasteland_road", "natural_cave", "sky_corridor" }) Reach(m, 0);
        Grand("shield,ultimate"); Check(Open("shield") && Open("ultimate"), "[D] grandfathered cards stay unlocked");
        Grand("");
        // 起動時の引き継ぎ: 遊んだデータ(BEST 6,000m)
        SaveStore.DeleteKey(CardMapUnlock.InitKey);
        SaveStore.SetFloat("BestDistance", 6000f); SaveStore.SetString(SaveKeys.LifetimeDistance, "6000");
        string note = CardMapUnlock.EnsureInitialized();
        Check(CardMapUnlock.IsGrandfathered("shield") && CardMapUnlock.IsGrandfathered("ice_prison") && !CardMapUnlock.IsGrandfathered("absolute_zero") && !CardMapUnlock.IsGrandfathered("speed_up"),
            $"[D] existing data keeps what it could use before (shield, ice_prison yes / absolute_zero no / common not listed): {note}");
        Check(CardMapUnlock.EnsureInitialized() == "", "[D] the migration runs only once");
        // 新しいデータ
        SaveStore.DeleteKey(CardMapUnlock.InitKey);
        SaveStore.SetFloat("BestDistance", 0f); SaveStore.SetString(SaveKeys.LifetimeDistance, "0");
        note = CardMapUnlock.EnsureInitialized();
        Check(!CardMapUnlock.IsGrandfathered("shield"), $"[D] new data: nothing grandfathered: {note}");
        // ガチャの候補(新規データ = BEST 0m、どのマップも未到達)
        var pool0 = CardDatabase.AllCards.Where(c => GachaStage.IsCardEligible(c, 0f, 1)).Select(c => c.cardId).ToList();
        var old0 = CardDatabase.AllCards.Where(c => GachaStage.IsCardEligibleByDistance(c, 0f, 1)).Select(c => c.cardId).ToList();
        L($"[D] new player gacha pool: {pool0.Count} cards (old rule {old0.Count}). removed: {string.Join(",", old0.Except(pool0))}");
        L($"[D] new player gacha pool: {string.Join(",", pool0)}");
        Check(pool0.Count >= 25, $"[D] new player gacha pool not too small ({pool0.Count})");
        var cs = CardDatabase.FindBaseById("shield");
        L($"[D] condition text: shield='{CardMapUnlock.ConditionSrc(cs)}' / ultimate='{CardMapUnlock.ConditionSrc(CardDatabase.FindBaseById("character_ultimate"))}' / burning_soul='{CardMapUnlock.ConditionSrc(CardDatabase.FindBaseById("burning_soul"))}'");
        GachaStage.DevAllCardsOpen = devOpen;
    }

    // ---------------------------------------------------------------- B
    IEnumerator NcRewards()
    {
        yield return V3Begin("swordsman");
        V3FlatStretch();
        yield return new WaitForSecondsRealtime(1.7f); // 連続ヒットが切れるまで
        var combo = ComboCounterUI.Instance;
        Check(combo != null, "[B] ComboCounterUI exists");
        foreach (int lv in NcLvs)
        {
            // 連続ヒット 10/20 回
            V3Reset(); V3Apply("combo_pay", lv); V3Apply("combo_training", lv);
            yield return new WaitForSecondsRealtime(1.7f);
            CardRewards.QaResetRunState(); CardRewards.ResetCounters();
            float x0 = X3(); int bonus0 = gm.RunBonusMile;
            for (int i = 0; i < 20 && combo != null; i++) combo.RegisterHit();
            Check(CardRewards.ComboPays == 2, $"[B] Lv{lv} COMBO: 20 HIT -> 2 payouts ({CardRewards.ComboPays})");
            Check(NcNear(X3() - x0, 2 * CardRewards.ComboPayMile(lv)) && Mathf.Abs(gm.RunBonusMile - bonus0 - 2 * CardRewards.ComboPayMile(lv)) <= 1.01f, $"[B] Lv{lv} COMBO PAY: +{X3() - x0:F2} MILE (RunBonusMile +{gm.RunBonusMile - bonus0}, want {2 * CardRewards.ComboPayMile(lv)})");
            Check(NcNear(CardRewards.ExpPaid, 2 * CardRewards.ComboTrainingExp(lv)), $"[B] Lv{lv} COMBO TRAINING: EXP +{CardRewards.ExpPaid} (want {2 * CardRewards.ComboTrainingExp(lv)})");
            // 撃破 10 体
            V3Reset(); V3Apply("hunt_pay", lv); V3Apply("hunt_training", lv);
            CardRewards.QaResetRunState(); CardRewards.ResetCounters(); x0 = X3();
            for (int i = 0; i < 10; i++) gm.RegisterEnemyKill(1f);
            Check(CardRewards.HuntPays == 1 && NcNear(X3() - x0, CardRewards.HuntPayMile(lv)) && NcNear(CardRewards.ExpPaid, CardRewards.HuntTrainingExp(lv)),
                $"[B] Lv{lv} HUNT PAY/TRAINING: 10 kills -> {CardRewards.HuntPays} payout, +{X3() - x0:F2} MILE, EXP +{CardRewards.ExpPaid}");
            // ボス撃破(数値のカード: 倍率)
            V3Reset();
            float m0 = gm.MileExactDbg[2]; gm.RegisterBossDefeat(100f, 0.5f); float baseMile = gm.MileExactDbg[2] - m0, baseExp = gm.LastBossExpGranted;
            V3Apply("boss_pay", lv); V3Apply("boss_training", lv);
            m0 = gm.MileExactDbg[2]; gm.RegisterBossDefeat(100f, 0.5f); float mile = gm.MileExactDbg[2] - m0, exp = gm.LastBossExpGranted;
            Check(NcNear(mile / Mathf.Max(1e-4f, baseMile), 1f + 0.12f * lv), $"[B] Lv{lv} BOSS PAY: boss MILE x{mile / Mathf.Max(1e-4f, baseMile):F2} (want x{1f + 0.12f * lv:F2})");
            Check(NcNear(exp / Mathf.Max(1e-4f, baseExp), CardRules.ExpMultiplier(0.15f * lv) / CardRules.ExpMultiplier(0f)), $"[B] Lv{lv} BOSS TRAINING: boss EXP x{exp / Mathf.Max(1e-4f, baseExp):F2} (want x{CardRules.ExpMultiplier(0.15f * lv):F2})");
            // 30秒(毎フレームの数えの経路)
            V3Reset(); V3Apply("survival_pay", lv); V3Apply("survival_training", lv);
            CardRewards.QaResetRunState(); CardRewards.ResetCounters(); x0 = X3();
            CardRewards.Tick(29f);
            Check(CardRewards.SurvivalPays == 0, "[B] SURVIVAL: nothing before 30 s");
            CardRewards.Tick(1.5f);
            Check(CardRewards.SurvivalPays == 1 && NcNear(X3() - x0, CardRewards.SurvivalPayMile(lv)) && NcNear(CardRewards.ExpPaid, CardRewards.SurvivalTrainingExp(lv)),
                $"[B] Lv{lv} SURVIVAL PAY/TRAINING: 30 s -> +{X3() - x0:F2} MILE, EXP +{CardRewards.ExpPaid}");
            // TROPHY HUNT: 雑魚 20 体 → ボス
            V3Reset(); V3Apply("trophy_hunt", lv);
            CardRewards.QaResetRunState(); CardRewards.ResetCounters();
            for (int i = 0; i < 20; i++) gm.RegisterEnemyKill(1f);
            CardRewards.ResetCounters(); x0 = X3();
            gm.RegisterBossDefeat(100f, 0.2f);
            Check(CardRewards.TrophyPays == 1 && NcNear(X3() - x0, 20 * CardRewards.TrophyMilePerKill(lv)) && NcNear(CardRewards.ExpPaid, 20 * CardRewards.TrophyExpPerKill(lv)),
                $"[B] Lv{lv} TROPHY HUNT: 20 kills then boss -> +{X3() - x0:F2} MILE, EXP +{CardRewards.ExpPaid} (want {20 * CardRewards.TrophyMilePerKill(lv)} / {20 * CardRewards.TrophyExpPerKill(lv)})");
            // CHAIN BOUNTY: 10HIT 未満 / 以上の撃破
            V3Reset(); V3Apply("chain_bounty", lv);
            yield return new WaitForSecondsRealtime(1.7f);
            float k0 = gm.MileExactDbg[1]; gm.RegisterEnemyKill(1f); float plain = gm.MileExactDbg[1] - k0;
            for (int i = 0; i < 10 && combo != null; i++) combo.RegisterHit();
            k0 = gm.MileExactDbg[1]; gm.RegisterEnemyKill(1f); float chained = gm.MileExactDbg[1] - k0;
            Check(NcNear(chained / Mathf.Max(1e-4f, plain), 1f + CardRewards.ChainBountyPct(lv)), $"[B] Lv{lv} CHAIN BOUNTY: kill MILE x{chained / Mathf.Max(1e-4f, plain):F2} during 10+ HIT (want x{1f + CardRewards.ChainBountyPct(lv):F2})");
        }
        // 距離: 1km の境目(ワープで手前へ → 実際に走って越える)
        foreach (int lv in NcLvs)
        {
            V3Reset(); V3Apply("road_pay", lv); V3Apply("road_training", lv);
            float target = (Mathf.Floor(gm.MaxDistance / 1000f) + 2f) * 1000f - 40f;
            gm.DebugWarpToDistance(target);
            yield return null; yield return null;
            V3FlatStretch();
            CardRewards.ResetCounters(); float x0 = X3();
            PlayerController.DebugRunOnlyScale = 3f;
            float w = 0f;
            while (CardRewards.RoadPays == 0 && w < 25f) { yield return null; w += Time.unscaledDeltaTime; }
            Check(CardRewards.RoadPays == 1 && NcNear(X3() - x0, CardRewards.RoadPayMile(lv)) && NcNear(CardRewards.ExpPaid, CardRewards.RoadTrainingExp(lv)),
                $"[B] Lv{lv} ROAD PAY/TRAINING: crossing {target + 40f:F0}m -> {CardRewards.RoadPays} payout, +{X3() - x0:F2} MILE, EXP +{CardRewards.ExpPaid} (the warp itself paid nothing)");
        }
        // IRON MARATHON: 被弾せずに 1km(実際に走る)/ 被弾で数え直し
        foreach (int lv in NcLvs)
        {
            V3Reset(); V3Apply("iron_marathon", lv);
            V3FlatStretch();
            CardRewards.QaResetRunState(); CardRewards.ResetCounters(); float x0 = X3();
            PlayerController.DebugRunOnlyScale = 6f;
            float d0 = gm.MaxDistance, w = 0f;
            if (lv == 9) { while (gm.MaxDistance - d0 < 600f && w < 60f) { yield return null; w += Time.unscaledDeltaTime; } gm.NoteHudDamage(); d0 = gm.MaxDistance; }
            while (CardRewards.IronPays == 0 && w < 120f) { yield return null; w += Time.unscaledDeltaTime; }
            float ran = gm.MaxDistance - d0;
            Check(CardRewards.IronPays == 1 && NcNear(X3() - x0, CardRewards.IronMarathonMile(lv)) && NcNear(CardRewards.ExpPaid, CardRewards.IronMarathonExp(lv)) && ran >= 995f,
                $"[B] Lv{lv} IRON MARATHON: {ran:F0}m since {(lv == 9 ? "the hit" : "start")} -> {CardRewards.IronPays} payout, +{X3() - x0:F2} MILE, EXP +{CardRewards.ExpPaid}");
        }
        PlayerController.DebugRunOnlyScale = 1f;
        // HORIZON: 30km(3段)
        foreach (int lv in NcLvs)
        {
            V3Reset();
            gm.DebugWarpToDistance(30500f);
            yield return null;
            float k0 = gm.MileExactDbg[1]; gm.RegisterEnemyKill(1f); float plain = gm.MileExactDbg[1] - k0; float bucket0 = gm.ExpBucketKill;
            V3Apply("horizon", lv);
            k0 = gm.MileExactDbg[1]; gm.RegisterEnemyKill(1f); float hz = gm.MileExactDbg[1] - k0;
            float want = 1f + 0.02f * lv * 3;
            Check(NcNear(hz / Mathf.Max(1e-4f, plain), want) && NcNear(gm.ExpBucketKill - bucket0, 0.06f * lv), $"[B] Lv{lv} HORIZON at 30.5km: kill MILE x{hz / Mathf.Max(1e-4f, plain):F2} (want x{want:F2}), EXP bucket +{(gm.ExpBucketKill - bucket0) * 100f:F0}%");
        }
        // EXP が実際に入る(固定の EXP の経路: BlockExpGain を外して TotalExpEarned を見る)
        V3Reset(); V3Apply("hunt_training", 9);
        CardRewards.QaResetRunState();
        GameManager.BlockExpGain = false; autoPickHold = false;
        yield return new WaitForSecondsRealtime(0.3f);
        float e0 = gm.TotalExpEarned;
        for (int i = 0; i < 10; i++) gm.RegisterEnemyKill(1f);
        // レベルアップの選択中は固定の EXP を預かり、選び終えてから入れる(GainBossExp)。自動で選ばれるのを待つ
        float wt = 0f;
        while ((gm.IsRewardSequenceRunning || gm.TotalExpEarned - e0 < 1f) && wt < 8f) { yield return null; wt += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        float gained = gm.TotalExpEarned - e0;
        float killExp = gm.enemyKillExp * gm.ExpMultKill * 10f;
        Check(gained >= killExp + CardRewards.HuntTrainingExp(9) - 1f, $"[B] EXP really added: +{gained:F0} for 10 kills (kill EXP {killExp:F0} + HUNT TRAINING {CardRewards.HuntTrainingExp(9)})");
        yield return new WaitForSecondsRealtime(1.5f);
        GameManager.BlockExpGain = true; autoPickHold = true;
        yield return V3End();
    }

    // ---------------------------------------------------------------- C
    IEnumerator NcRisk()
    {
        // HIGH STAKES: ボスの攻撃(同じ理由の文字列で TryDamagePlayer を 300 回)
        yield return V3Begin("swordsman");
        V3FlatStretch();
        stopKeepAlive = true;
        float Avg(string reason, int n)
        {
            int sum = 0;
            for (int i = 0; i < n; i++)
            {
                V3LivesSet.Invoke(gm, new object[] { gm.maxLives });
                int before = gm.Lives;
                gm.TryDamagePlayer(false, reason, CombatScale.PlayerHit);
                sum += before - gm.Lives;
            }
            V3LivesSet.Invoke(gm, new object[] { gm.maxLives });
            return sum / (float)n;
        }
        V3Reset();
        float b0 = Avg("WildBoss:QaTest", 300), m0 = Avg("EnemyMelee:QaTest", 100);
        V3Apply("high_stakes", 9);
        float b9 = Avg("WildBoss:QaTest", 300), m9 = Avg("EnemyMelee:QaTest", 100);
        Check(NcNear(b9 / Mathf.Max(1e-4f, b0), 1.9f, 0.12f), $"[C] HIGH STAKES Lv9: boss hit {b0 / CombatScale.HpPerHeart:F2} -> {b9 / CombatScale.HpPerHeart:F2} hearts (x{b9 / Mathf.Max(1e-4f, b0):F2}, want x1.9)");
        Check(NcNear(m9, m0, 0.01f), $"[C] HIGH STAKES does not change enemy hits ({m0} -> {m9})");
        V3Reset();
        stopKeepAlive = false; StartCoroutine(KeepAlive());
        yield return V3End();

        // DANGER BOUNTY / SKY SIEGE: 普通に走って(0km〜)数える。カードなし → Lv9
        yield return BeginRun("swordsman", "wasteland_road");
        GameManager.BlockExpGain = true;
        BossManager.SuppressGates = true; // ボス戦で止まらない(雑魚の出方だけを見る)
        gm.DebugWarpToDistance(3000f);
        float secs = float.Parse(Arg("-ncRiskSecs", "100"), System.Globalization.CultureInfo.InvariantCulture);
        int h0 = DecisionEvents.HeavyInNormal, a0 = EncounterDirector.AirMinionsPlaced, r0 = AirRoll();
        int sp0 = EncounterDirector.Instance != null ? EncounterDirector.Instance.SpawnedEnemies : 0;
        yield return new WaitForSecondsRealtime(secs);
        int hBase = DecisionEvents.HeavyInNormal - h0, aBase = EncounterDirector.AirMinionsPlaced - a0, rBase = AirRoll() - r0;
        float dBase = gm.MaxDistance;
        V3Reset(); V3Apply("danger_bounty", 9); V3Apply("sky_siege", 9);
        h0 = DecisionEvents.HeavyInNormal; a0 = EncounterDirector.AirMinionsPlaced; r0 = AirRoll();
        yield return new WaitForSecondsRealtime(secs);
        int hCard = DecisionEvents.HeavyInNormal - h0, aCard = EncounterDirector.AirMinionsPlaced - a0, rCard = AirRoll() - r0;
        float rateBase = aBase / Mathf.Max(1f, aBase + rBase), rateCard = aCard / Mathf.Max(1f, aCard + rCard);
        L($"[C] enemies spawned by the director: {(EncounterDirector.Instance != null ? EncounterDirector.Instance.SpawnedEnemies - sp0 : -1)}");
        L($"[C] {secs:F0}s each (3000 -> {dBase:F0} -> {gm.MaxDistance:F0}m): heavy hitters {hBase} -> {hCard}; air minions placed {aBase} (roll-off {rBase}, {rateBase:P0}) -> {aCard} (roll-off {rCard}, {rateCard:P0})");
        Check(hBase == 0 && hCard > 0, $"[C] DANGER BOUNTY Lv9: heavy hitters appear before 50km ({hBase} -> {hCard})");
        Check(rateCard > rateBase + 0.15f || (aCard > aBase && rCard == 0), $"[C] SKY SIEGE Lv9: air slots get air minions more often ({rateBase:P0} -> {rateCard:P0})");
        // CROSSROADS: 判断イベントの始まり(通常 50km → Lv9 で 14km)
        V3Reset();
        Check(!DecisionEvents.InRange(20000f), "[C] decision events normally start at 50km");
        V3Apply("crossroads", 9);
        Check(DecisionEvents.InRange(20000f) && DecisionEvents.InRange(14000f) && !DecisionEvents.InRange(13000f), $"[C] CROSSROADS Lv9: decision events from {CardRewards.CrossroadsFromKm(9)}km");
        Check(NcNear(CardRewards.DecisionGapMul, 0.55f), $"[C] CROSSROADS Lv9: gap x{CardRewards.DecisionGapMul:F2}");
        gm.DebugWarpToDistance(16000f);
        int st0 = DecisionEvents.Started, ro0 = DecisionEvents.Rolled;
        yield return new WaitForSecondsRealtime(secs);
        L($"[C] CROSSROADS Lv9 at {16000:F0} -> {gm.MaxDistance:F0}m: decision events rolled {DecisionEvents.Rolled - ro0}, started {DecisionEvents.Started - st0}");
        Check(DecisionEvents.Rolled - ro0 > 0, $"[C] CROSSROADS Lv9: decision events roll at 16km ({DecisionEvents.Rolled - ro0} rolls, {DecisionEvents.Started - st0} started)");
        V3Reset();
        GameManager.BlockExpGain = false;
        BossManager.SuppressGates = false;
        yield return EndRun();
    }

    float X3() => gm.MileExactDbg[3]; // BONUS の経路(カードの固定の MILE もここ)の正確な合計

    static int AirRoll() => EncounterDirector.AirMinionRejects.TryGetValue("roll", out int n) ? n : 0;
}
#endif
