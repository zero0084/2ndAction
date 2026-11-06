#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

// マップ専用カード 20 枚(2026-11-06。Docs/Cards_2026-11-06/map_cards_v2.md)の試験。 -qaMapCards <dir> [-mcOnly ADF]
//  A: データ(アセット・名前・説明・レア度・解放・カテゴリ・マップの分類・分類の表・Lv1/Lv5/Lv9 の効果の行が日英で出る・合計 150 枚・訳の待ち)→ mapcards_values.tsv
//  D: 解禁(各マップの到達距離 / ラスダンの解放。資料の決まりどおり)
//  F: 新しい仕組みの動き(実際のゲームの入口: 被弾/回復/BREAK/撃破/状態)。一時シールド・血の盾・強化・上限・1ランに1回・復活の順番
// 保存は試験の前に控え、最後に戻す(-memSave で動かすこと)
public partial class QaSweep
{
    // 資料の表(LocCardMap とは別に、ここで資料から書き写して照らし合わせる): id, ★, 分類, カテゴリ, 到達距離(ラスダンは -1)
    static readonly (string id, int rar, CardMapCategory map, float reach)[] McSpecs =
    {
        ("last_bastion", 3, CardMapCategory.Cave, 5000f), ("overflow_ward", 4, CardMapCategory.Cave, 20000f), ("revenge_armor", 3, CardMapCategory.Cave, 5000f),
        ("break_feast", 3, CardMapCategory.Cave, 5000f), ("iron_heart", 4, CardMapCategory.Cave, 20000f),
        ("air_combo", 2, CardMapCategory.Sky, 1000f), ("sky_breaker", 3, CardMapCategory.Sky, 5000f), ("dive_boost", 3, CardMapCategory.Sky, 5000f),
        ("launch_master", 3, CardMapCategory.Sky, 5000f), ("sonic_edge", 4, CardMapCategory.Sky, 20000f),
        ("second_rise", 4, CardMapCategory.LastDungeon, -1f), ("shield_burst", 4, CardMapCategory.LastDungeon, -1f), ("blood_armor", 4, CardMapCategory.LastDungeon, -1f),
        ("elemental_chaos", 4, CardMapCategory.LastDungeon, -1f), ("elemental_focus", 4, CardMapCategory.LastDungeon, -1f), ("break_dominion", 4, CardMapCategory.LastDungeon, -1f),
        ("endless_growth", 3, CardMapCategory.LastDungeon, -1f), ("final_drive", 5, CardMapCategory.LastDungeon, -1f), ("last_gamble", 5, CardMapCategory.LastDungeon, -1f),
        ("one_more_chance", 5, CardMapCategory.LastDungeon, -1f),
    };

    int mcPass, mcFail;
    void MC(bool ok, string what) { if (ok) { mcPass++; L("[ok] " + what); } else mcFail++; Check(ok, what); }
    static bool McNear(float a, float b, float tol = 0.002f) => Mathf.Abs(a - b) <= tol;

    IEnumerator MapCardsMode()
    {
        var snapSave = SaveSystem.Capture();
        autoPickHold = true;
        string only = Arg("-mcOnly", "ADF");
        if (only.Contains('A')) McData();
        if (only.Contains('D')) McUnlock();
        if (only.Contains('F')) yield return McFunctions();
        GameManager.BlockExpGain = false;
        autoPickHold = false;
        SaveSystem.Restore(snapSave);
        CardInventory.ReloadFromPrefs();
        RunCheckpoint.Reload();
        CardMapUnlock.Reload(); UnlockRules.Reload(); ProgressStats.Reload();
        L($"[mapcards] checks passed {mcPass} / failed {mcFail}");
        L("[mapcards] test machine save restored");
    }

    // ---------------------------------------------------------------- A
    void McData()
    {
        MC(McSpecs.Length == 20 && LocCardMap.Cards.Length == 20, $"[A] 20 map cards ({McSpecs.Length} / {LocCardMap.Cards.Length})");
        var sb = new StringBuilder("id\tname\tnameJa\trarity\tstage\tunlock\treach\tcategory\tmap\telement\tlv1\tlv5\tlv9\tlv1En\tlv5En\tlv9En\ticon\n");
        string prev = Loc.Current;
        foreach (var s in McSpecs)
        {
            var c = CardDatabase.FindBaseById(s.id);
            MC(c != null, $"[A] asset exists: {s.id}");
            if (c == null) continue;
            var t = LocCardMap.Find(s.id);
            MC(c.cardName == t.name && !string.IsNullOrEmpty(t.nameJa), $"[A] {s.id} name {c.cardName} ({t.nameJa})");
            MC(c.description == t.desc, $"[A] {s.id} description from LocCardMap");
            MC(c.rarity == s.rar, $"[A] {s.id} rarity {c.rarity} == {s.rar}");
            MC(c.unlockDistance == (s.rar >= 4 ? 20000f : s.rar == 3 ? 5000f : 0f) && c.gachaStage == (s.rar >= 4 ? 3 : s.rar == 3 ? 2 : 1), $"[A] {s.id} unlock {c.unlockDistance}m stage {c.gachaStage}");
            MC(CardMapCategories.Of(c) == s.map && CardMapCategoryTable.Rows.Any(r => r.id == s.id && r.cat == s.map), $"[A] {s.id} map category {CardMapCategories.Of(c)} == {s.map} (in CardMapCategoryTable)");
            MC(Mathf.Abs(CardMapUnlock.RequiredMeters(c) - s.reach) < 0.5f, $"[A] {s.id} unlock condition: reach {CardMapUnlock.RequiredMeters(c)} == {s.reach} ({CardMapUnlock.ConditionSrc(c)})");
            MC(c.sortOrder > 213, $"[A] {s.id} sortOrder {c.sortOrder} after the existing cards (initial deck unchanged)");
            MC(c.icon != null, $"[A] {s.id} has an icon (placeholder ok until the art is made: {(c.icon != null ? c.icon.name : "none")})");
            // 効果の行(カード詳細の Lv1 / Lv5 / Lv9)が日本語・英語で出る
            string l1 = CardVariant.AbilityEffectTextAt(s.id, 1), l5 = CardVariant.AbilityEffectTextAt(s.id, 5), l9 = CardVariant.AbilityEffectTextAt(s.id, 9);
            MC(!string.IsNullOrEmpty(l1) && !string.IsNullOrEmpty(l5) && !string.IsNullOrEmpty(l9) && l1 != l9 && l1 != l5, $"[A] {s.id} effect line Lv1 '{l1}' / Lv5 '{l5}' / Lv9 '{l9}'");
            MC(Loc.EnglishOf(c.description) != c.description, $"[A] {s.id} English description connected");
            Loc.Set("en", false);
            string e1 = Loc.Auto(l1), e5 = Loc.Auto(l5), e9 = Loc.Auto(l9), ed = Loc.Auto(c.description);
            Loc.Set(prev, false);
            MC(!System.Text.RegularExpressions.Regex.IsMatch(e1 + e5 + e9 + ed, "[぀-ヿ㐀-鿿]"), $"[A] {s.id} English lines have no Japanese left: '{e1}' / '{e5}' / '{e9}'");
            sb.Append(s.id).Append('\t').Append(c.cardName).Append('\t').Append(t.nameJa).Append('\t').Append(c.rarity).Append('\t').Append(c.gachaStage).Append('\t').Append(c.unlockDistance)
              .Append('\t').Append(CardMapUnlock.RequiredMeters(c)).Append('\t').Append(c.category).Append('\t').Append(CardMapCategories.Of(c)).Append('\t').Append(c.element)
              .Append('\t').Append(l1).Append('\t').Append(l5).Append('\t').Append(l9).Append('\t').Append(e1).Append('\t').Append(e5).Append('\t').Append(e9).Append('\t').Append(c.icon != null ? c.icon.name : "").Append('\n');
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "mapcards_values.tsv"), sb.ToString());
        // レア度の分布(★2 ×1 / ★3 ×7 / ★4 ×9 / ★5 ×3)
        var byR = McSpecs.Select(s => CardDatabase.FindBaseById(s.id)).Where(c => c != null).GroupBy(c => c.rarity).ToDictionary(g => g.Key, g => g.Count());
        int R(int r) => byR.TryGetValue(r, out int n) ? n : 0;
        MC(R(2) == 1 && R(3) == 7 && R(4) == 9 && R(5) == 3, $"[A] rarity of the 20: ★2 {R(2)} / ★3 {R(3)} / ★4 {R(4)} / ★5 {R(5)} (want 1/7/9/3)");
        // 全カードが分類の表にある / マップごとの枚数 / 合計 150
        var missing = CardDatabase.AllCards.Where(c => !CardMapCategoryTable.Rows.Any(r => r.id == c.cardId)).Select(c => c.cardId).ToList();
        MC(missing.Count == 0, $"[A] every card is in CardMapCategoryTable (missing: {string.Join(",", missing)})");
        var counts = CardDatabase.AllCards.GroupBy(c => CardMapCategories.Of(c)).ToDictionary(g => g.Key, g => g.Count());
        int Cnt(CardMapCategory k) => counts.TryGetValue(k, out int n) ? n : 0;
        L($"[A] cards={CardDatabase.AllCards.Count} map categories: {string.Join(" ", counts.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"))}");
        MC(CardDatabase.AllCards.Count == 150, $"[A] total cards {CardDatabase.AllCards.Count} == 150");
        MC(Cnt(CardMapCategory.Common) == 43 && Cnt(CardMapCategory.Wilderness) == 32 && Cnt(CardMapCategory.Cave) == 30 && Cnt(CardMapCategory.Sky) == 30 && Cnt(CardMapCategory.LastDungeon) == 15,
            $"[A] per map: common {Cnt(CardMapCategory.Common)} (43) / wilderness {Cnt(CardMapCategory.Wilderness)} (32) / cave {Cnt(CardMapCategory.Cave)} (30) / sky {Cnt(CardMapCategory.Sky)} (30) / last dungeon {Cnt(CardMapCategory.LastDungeon)} (15)");
        var deck = DefaultSave.StartingDeck(GameManager.DeckCapacity);
        MC(deck.SequenceEqual(DefaultSave.StarterIds), $"[A] initial deck unchanged: {string.Join(",", deck)}");
        var pend = LocCardMap.PendingList();
        MC(pend.Length == 40 && LocPendingReview.Pending.Length == 0 && pend.All(p => Loc.Ready(p.key)), $"[A] 40 strings translated (pending {LocPendingReview.Pending.Length}, {pend.First().id}..{pend.Last().id})"); // 2026-11-06: エステルの訳を反映
        MC(pend.All(p => Loc.HasTranslation("en", p.key)), "[A] every pending string has English");
        MC(LocCardMap.Cards.All(c => Loc.HasTranslation("de", c.name)), "[A] card names are in the other language tables as-is (no missing warnings)");
        // 補間(Lv1 / Lv5 / Lv9)
        MC(McNear(MapCardRules.At3(1, 0.3f, 0.5f, 0.7f), 0.3f) && McNear(MapCardRules.At3(3, 0.3f, 0.5f, 0.7f), 0.4f) && McNear(MapCardRules.At3(9, 0.3f, 0.5f, 0.7f), 0.7f)
            && Enumerable.Range(1, 9).Select(l => MapCardRules.LastBastionShields(l)).SequenceEqual(new[] { 1, 1, 1, 1, 2, 2, 2, 2, 3 })
            && MapCardRules.AirComboHits(1) == 5 && MapCardRules.AirComboHits(5) == 4 && MapCardRules.AirComboHits(9) == 3, "[A] Lv1/Lv5/Lv9 interpolation (linear, steps 1/2/3, hits 5/4/3)");
    }

    // ---------------------------------------------------------------- D
    void McUnlock()
    {
        bool devOpen = GachaStage.DevAllCardsOpen;
        GachaStage.DevAllCardsOpen = false;
        void Reach(string stage, double m) { SaveStore.SetString(UnlockRules.ReachPrefix + stage, m.ToString("R", System.Globalization.CultureInfo.InvariantCulture)); UnlockRules.Reload(); }
        void LastDungeon(bool on) { SaveStore.SetInt(SaveKeys.FinalDungeonUnlocked, on ? 1 : 0); ProgressStats.Reload(); }
        bool Open(string id, float best = 200000f) { var c = CardDatabase.FindBaseById(id); return c != null && GachaStage.IsCardEligible(c, best, GachaStage.StageForDistance(best)); }
        SaveStore.SetString(CardMapUnlock.GrandfatherKey, ""); CardMapUnlock.Reload();
        LastDungeon(false);
        string[] maps = { "wasteland_road", "natural_cave", "sky_corridor" };
        foreach (var m in maps) Reach(m, 0);
        foreach (var s in McSpecs)
        {
            if (s.map == CardMapCategory.LastDungeon)
            {
                MC(!Open(s.id), $"[D] {s.id} locked while the Last Dungeon is locked");
                LastDungeon(true); MC(Open(s.id), $"[D] {s.id} opens with the Last Dungeon"); LastDungeon(false);
                continue;
            }
            string stage = CardMapCategories.StageIdOf(s.map);
            Reach(stage, s.reach - 1f); MC(!Open(s.id), $"[D] {s.id} locked at {s.reach - 1f:N0}m on {stage}");
            foreach (var m in maps) if (m != stage) Reach(m, 200000);
            MC(!Open(s.id), $"[D] {s.id} stays locked when only other maps are reached");
            foreach (var m in maps) if (m != stage) Reach(m, 0);
            Reach(stage, s.reach); MC(Open(s.id), $"[D] {s.id} opens at {s.reach:N0}m on {stage}");
            Reach(stage, 0);
        }
        var pool0 = CardDatabase.AllCards.Where(c => GachaStage.IsCardEligible(c, 0f, 1)).Select(c => c.cardId).ToList();
        MC(!pool0.Any(id => LocCardMap.Ids.Contains(id)), $"[D] new player gacha pool has no new map card (pool {pool0.Count})");
        GachaStage.DevAllCardsOpen = devOpen;
    }

    // ---------------------------------------------------------------- F
    static readonly FieldInfo McDeckField = typeof(GameManager).GetField("deckCards", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly FieldInfo McGroundField = typeof(PlayerController).GetField("isGrounded", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly FieldInfo McWasGroundField = typeof(PlayerController).GetField("cardWasGrounded", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly FieldInfo McDiveSeenField = typeof(PlayerController).GetField("diveSeenAt", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly MethodInfo McAirHit = typeof(PlayerController).GetMethod("MapCardsOnAirHit", BindingFlags.NonPublic | BindingFlags.Instance);

    void McLives(int v) => V3LivesSet.Invoke(gm, new object[] { v });
    void McFresh(params (string id, int lv)[] cards)
    {
        V3Reset();
        foreach (var c in cards) V3Apply(c.id, c.lv);
        gm.QaResetMapCooldowns();
        McLives(gm.maxLives);
    }
    GameManager.DamageResult McHit(int hearts, string reason = "qa-map") => gm.TryDamagePlayer(false, reason, hearts * CombatScale.HpPerHeart);

    IEnumerator McFunctions()
    {
        int per = CombatScale.HpPerHeart;
        yield return V3Begin("swordsman");
        V3FlatStretch();
        stopKeepAlive = true;
        SetGod(false);
        yield return null;
        MapCardRules.ResetCounters();

        // ---- LAST BASTION: HP30% 以下になった瞬間に一時シールド(Lv1 = 1枚 / Lv9 = 3枚)。一時シールドが被弾を防ぐ・間隔
        McFresh(("last_bastion", 1), ("heart_up", 5));
        int max = gm.maxLives;
        McLives(Mathf.RoundToInt(max * 0.3f) + per); McHit(1);
        MC(pc.TempShieldCount == 1, $"[F] LAST BASTION Lv1: crossing 30% gives 1 temp shield ({pc.TempShieldCount}, lives {gm.Lives}/{max})");
        int lv0 = gm.Lives; var r = McHit(1);
        MC(gm.Lives == lv0 && gm.LastDamageShielded && pc.TempShieldCount == 0, $"[F] temp shield blocks a hit ({r}, lives {lv0} -> {gm.Lives})");
        McLives(max); McLives(Mathf.RoundToInt(max * 0.3f) + per); McHit(1);
        MC(pc.TempShieldCount == 0, $"[F] LAST BASTION cooldown (90 s at Lv1): no shield on the second crossing ({pc.TempShieldCount})");
        McFresh(("last_bastion", 9), ("heart_up", 5));
        McLives(Mathf.RoundToInt(gm.maxLives * 0.3f) + per); McHit(1);
        MC(pc.TempShieldCount == 3, $"[F] LAST BASTION Lv9: 3 temp shields ({pc.TempShieldCount})");
        float t0 = Time.time; while (Time.time - t0 < 8.2f) yield return null;
        MC(pc.TempShieldCount == 0, $"[F] LAST BASTION shields expire after 8 s ({pc.TempShieldCount})");

        // ---- OVERFLOW WARD: 超過の 70%(Lv9)→ 一時シールド。3 秒に1回・最大 3・合計 3
        McFresh(("overflow_ward", 9));
        gm.KitHeal(3 * per); // 30 の超過 × 0.7 = 21 → 2 枚
        MC(pc.TempShieldCount == 2, $"[F] OVERFLOW WARD Lv9: 3 hearts overflow -> 2 temp shields ({pc.TempShieldCount})");
        gm.KitHeal(3 * per);
        MC(pc.TempShieldCount == 2, $"[F] OVERFLOW WARD converts once per 3 s ({pc.TempShieldCount})");
        t0 = Time.time; while (Time.time - t0 < 3.2f) yield return null;
        gm.KitHeal(9 * per);
        t0 = Time.time; while (Time.time - t0 < 3.2f) yield return null;
        gm.KitHeal(9 * per);
        MC(pc.TempShieldCount == 3, $"[F] OVERFLOW WARD caps at 3 ({pc.TempShieldCount}, conversions {MapCardRules.OverflowConversions})");
        McFresh(("overflow_ward", 1), ("last_bastion", 9), ("heart_up", 5));
        McLives(Mathf.RoundToInt(gm.maxLives * 0.3f) + per); McHit(1); // LAST BASTION 3 枚
        McLives(gm.maxLives); gm.KitHeal(9 * per);
        MC(pc.TempShieldCount == 3, $"[F] temp shields LAST BASTION + OVERFLOW WARD share the total cap 3 ({pc.TempShieldCount})");

        // ---- REVENGE ARMOR(Lv9): ハート2つ以上の被弾の後 5 秒 -1ハート・攻撃 +40%。ハート1つの被弾では出ない
        McFresh(("revenge_armor", 9), ("heart_up", 5));
        float cs0 = pc.CardStateCondition();
        McHit(1);
        MC(!pc.RevengeActive, "[F] REVENGE ARMOR: a 1-heart hit does not trigger");
        McHit(2);
        float cs1 = pc.CardStateCondition();
        MC(pc.RevengeActive && McNear(cs1 - cs0, 0.40f), $"[F] REVENGE ARMOR Lv9: after a 2-heart hit attack +{cs1 - cs0:0.###} (want +0.40)");
        lv0 = gm.Lives; McHit(2);
        MC(lv0 - gm.Lives == per, $"[F] REVENGE ARMOR: next 2-heart hit takes 1 heart ({lv0 - gm.Lives})");
        lv0 = gm.Lives; McHit(1);
        MC(lv0 - gm.Lives == per, $"[F] REVENGE ARMOR: a 1-heart hit stays 1 heart (min 1)");

        // ---- IRON HEART(Lv9 + HEART UP 9 = 14 ハート → 9 × 4% = 36%)
        McFresh(("iron_heart", 9), ("heart_up", 9), ("shield", 1));
        float bonus = MapCardRules.IronHeartBonus(9, gm.maxLives);
        McLives(per);
        gm.KitHeal(5 * per); int healed = gm.Lives - per;
        float wantB = Mathf.Min(0.5f, (gm.maxLives / per - 5) * 0.04f);
        MC(bonus > 0f && McNear(bonus, wantB) && healed >= Mathf.FloorToInt(5 * per * (1f + bonus)) - 1, $"[F] IRON HEART Lv9 at {gm.maxLives / per} hearts: bonus +{bonus:P0} (want {wantB:P0}), heal 50 -> {healed}");
        float rs = pc.ShieldRechargeSeconds; V3Reset(); V3Apply("heart_up", 9); V3Apply("shield", 1); float rs0 = pc.ShieldRechargeSeconds;
        MC(McNear(rs0 / Mathf.Max(0.01f, rs), 1f + bonus, 0.02f), $"[F] IRON HEART: Shield recharge {rs0:0.00}s -> {rs:0.00}s (x{1f + bonus:0.00} faster)");
        McFresh(("iron_heart", 9), ("heart_up", 9)); V3Apply("heart_up", 9); // 23 ハート → 上限 +50%
        MC(McNear(MapCardRules.IronHeartBonus(9, gm.maxLives), 0.50f), $"[F] IRON HEART cap +50% ({MapCardRules.IronHeartBonus(9, gm.maxLives):P0} at {gm.maxLives / per} hearts)");

        // ---- AIR COMBO(Lv9: 3回)/ SKY BREAKER / LAUNCH MASTER の落下
        McFresh(("air_combo", 9));
        cs0 = pc.CardStateCondition();
        for (int i = 0; i < 3; i++) { McAirHit.Invoke(pc, null); t0 = Time.time; while (Time.time - t0 < 0.17f) yield return null; }
        MC(pc.AirComboActive && McNear(pc.CardStateCondition() - cs0, 0.26f), $"[F] AIR COMBO Lv9: 3 air hits -> attack +{pc.CardStateCondition() - cs0:0.###} (want +0.26)");
        McFresh(("air_combo", 1));
        for (int i = 0; i < 4; i++) { McAirHit.Invoke(pc, null); t0 = Time.time; while (Time.time - t0 < 0.17f) yield return null; }
        MC(!pc.AirComboActive && pc.AirComboHitCount == 4, $"[F] AIR COMBO Lv1: 4 hits not enough (needs 5)");
        McWasGroundField.SetValue(pc, false); yield return null; yield return null;
        MC(pc.AirComboHitCount == 0, "[F] AIR COMBO: landing resets the count");
        McFresh(("sky_breaker", 9));
        McGroundField.SetValue(pc, false); float skyB = pc.MapBreakBonus; McGroundField.SetValue(pc, true); float gndB = pc.MapBreakBonus;
        MC(McNear(skyB, 0.55f) && gndB == 0f, $"[F] SKY BREAKER Lv9: BREAK +{skyB:0.##} in the air, +{gndB} on the ground");
        McFresh(("launch_master", 9));
        MC(McNear(MapCardRules.LaunchFallMul, 0.70f), $"[F] LAUNCH MASTER Lv9: launched enemies fall with gravity x{MapCardRules.LaunchFallMul:0.00}");
        V3Reset(); MC(MapCardRules.LaunchFallMul == 1f, "[F] LAUNCH MASTER: no change without the card");

        // ---- DIVE BOOST(Lv9): 急降下の着地 → 2.5 秒 速度 +26%・攻撃 +39%
        McFresh(("dive_boost", 9));
        cs0 = pc.CardStateCondition();
        McDiveSeenField.SetValue(pc, Time.time); McWasGroundField.SetValue(pc, false);
        yield return null; yield return null;
        MC(pc.DiveBoostActive && McNear(pc.CardStateCondition() - cs0, 0.39f) && McNear(pc.MapCardsSpeedMulNow, 1.26f), $"[F] DIVE BOOST Lv9: after a dive landing attack +{pc.CardStateCondition() - cs0:0.###}, speed x{pc.MapCardsSpeedMulNow:0.00}");
        t0 = Time.time; while (Time.time - t0 < 2.7f) yield return null;
        MC(!pc.DiveBoostActive, "[F] DIVE BOOST ends after 2.5 s");

        // ---- SONIC EDGE(Lv9): 130km/h 以上
        McFresh(("sonic_edge", 9));
        cs0 = pc.CardStateCondition(); float kmh0 = pc.CurrentRunKmh;
        MC(kmh0 >= 130f || McNear(pc.MapBreakBonus, 0f), $"[F] SONIC EDGE: nothing below 130 km/h ({kmh0:F0} km/h)");
        PlayerController.DebugRunOnlyScale = 8f; yield return null;
        float kmh1 = pc.CurrentRunKmh;
        if (kmh1 >= 130f) MC(McNear(pc.MapBreakBonus, 0.42f) && pc.CardStateCondition() - cs0 >= 0.46f, $"[F] SONIC EDGE Lv9 at {kmh1:F0} km/h: attack +{pc.CardStateCondition() - cs0:0.##}, BREAK +{pc.MapBreakBonus:0.##}");
        else Warn($"SONIC EDGE: could not reach 130 km/h in the test ({kmh1:F0})");
        PlayerController.DebugRunOnlyScale = 1f; yield return null;

        // ---- SECOND RISE(Lv9): 1ランに1回、HP25% 以下で最大HPの 60% 回復 + 8 秒 +40%
        McFresh(("second_rise", 9), ("heart_up", 5));
        max = gm.maxLives;
        McLives(Mathf.RoundToInt(max * 0.25f) + per); int before = gm.Lives; McHit(2);
        MC(gm.SecondRiseUsed && pc.SecondRiseActive && gm.Lives >= before - 2 * per + Mathf.RoundToInt(max * 0.6f) - 1, $"[F] SECOND RISE Lv9: lives {before} -> {gm.Lives} (max {max}), buff {pc.SecondRiseActive}");
        MC(McNear(pc.MapCardsSpeedMulNow, 1.40f), $"[F] SECOND RISE speed x{pc.MapCardsSpeedMulNow:0.00}");
        McLives(Mathf.RoundToInt(max * 0.25f) + per); before = gm.Lives; McHit(2);
        MC(MapCardRules.SecondRises == 1 && gm.Lives == before - 2 * per, $"[F] SECOND RISE only once per run ({MapCardRules.SecondRises})");

        // ---- SHIELD BURST(Lv9): Shield が壊れると衝撃(3 秒に1回)
        McFresh(("shield_burst", 9), ("shield", 9));
        int sb0 = MapCardRules.ShieldBursts;
        McHit(1); McHit(1);
        MC(MapCardRules.ShieldBursts == sb0 + 1, $"[F] SHIELD BURST: 2 shields broken at once -> 1 burst (interval 3 s) ({MapCardRules.ShieldBursts - sb0})");
        t0 = Time.time; while (Time.time - t0 < 3.1f) yield return null;
        McHit(1);
        MC(MapCardRules.ShieldBursts == sb0 + 2, "[F] SHIELD BURST: again after 3 s");
        McFresh(("shield_burst", 9), ("last_bastion", 1), ("heart_up", 5));
        McLives(Mathf.RoundToInt(gm.maxLives * 0.3f) + per); McHit(1); sb0 = MapCardRules.ShieldBursts; McHit(1);
        MC(MapCardRules.ShieldBursts == sb0 + 1, "[F] SHIELD BURST also on a temp shield");

        // ---- BLOOD ARMOR(Lv9): 吸収の超過 100% → 血の盾(最大 3)、ハート単位で消す・吸収以外の超過は入らない・10 秒で減る
        McFresh(("blood_armor", 9));
        gm.KitHeal(3 * per);
        MC(gm.BloodShieldHearts == 0, "[F] BLOOD ARMOR: a normal heal overflow does not make blood shield");
        gm.DrainHeal(5 * per);
        MC(gm.BloodShieldHearts == 3, $"[F] BLOOD ARMOR Lv9: drain overflow -> blood shield {gm.BloodShieldHearts} (cap 3)");
        MC(gm.Elements.BleedChance >= 0.30f - 0.001f, $"[F] BLOOD ARMOR: bleed chance +30% while shielded ({gm.Elements.BleedChance:P0})");
        lv0 = gm.Lives; r = McHit(2);
        MC(gm.Lives == lv0 && gm.BloodShieldHearts == 1, $"[F] blood shield absorbs a 2-heart hit ({r}, blood {gm.BloodShieldHearts})");
        lv0 = gm.Lives; McHit(2);
        MC(gm.Lives == lv0 - per && gm.BloodShieldHearts == 0, $"[F] blood shield absorbs part of a hit (lives -{lv0 - gm.Lives})");
        gm.QaSetBloodShield(2);
        t0 = Time.time; while (Time.time - t0 < 10.3f) yield return null;
        MC(gm.BloodShieldHearts == 1, $"[F] blood shield decays 1 per 10 s ({gm.BloodShieldHearts})");
        McFresh(("blood_armor", 9), ("overflow_ward", 9));
        gm.DrainHeal(3 * per);
        MC(gm.BloodShieldHearts == 3 && pc.TempShieldCount == 0, $"[F] drain overflow goes to BLOOD ARMOR first (blood {gm.BloodShieldHearts}, temp {pc.TempShieldCount})");

        // ---- ELEMENTAL CHAOS / ELEMENTAL FOCUS(デッキの属性の種類)
        var deck = (List<string>)McDeckField.GetValue(gm);
        var deckBackup = new List<string>(deck);
        deck.Clear(); deck.Add("flame_blade"); MapCardRules.InvalidateDeck();
        McFresh(("flame_blade", 1));
        float bc0 = gm.Elements.BurnChance, bd0 = gm.Elements.BurnDps;
        V3Apply("elemental_focus", 9);
        MC(MapCardRules.DeckElementKinds == 1 && McNear(gm.Elements.BurnChance, bc0 + 0.30f) && McNear(gm.Elements.BurnDps / Mathf.Max(1e-4f, bd0), 1.70f, 0.01f),
            $"[F] ELEMENTAL FOCUS Lv9 (1 element): burn chance {bc0:P0} -> {gm.Elements.BurnChance:P0}, burn x{gm.Elements.BurnDps / Mathf.Max(1e-4f, bd0):0.00}");
        deck.Add("frost_edge"); MapCardRules.InvalidateDeck();
        MC(McNear(gm.Elements.BurnDps / Mathf.Max(1e-4f, bd0), 1.35f, 0.01f), $"[F] ELEMENTAL FOCUS with 2 elements: half (x{gm.Elements.BurnDps / Mathf.Max(1e-4f, bd0):0.00})");
        deck.Add("thunder_strike"); deck.Add("wind_cutter"); deck.Add("vampire"); MapCardRules.InvalidateDeck();
        MC(McNear(gm.Elements.BurnDps / Mathf.Max(1e-4f, bd0), 1f, 0.01f), "[F] ELEMENTAL FOCUS with 5 elements: nothing");
        V3Apply("elemental_chaos", 9);
        MC(MapCardRules.DeckElementKinds == 5 && McNear(gm.Elements.BurnChance / Mathf.Max(1e-4f, bc0), 1.5f, 0.01f) && McNear(gm.Elements.BurnDps / Mathf.Max(1e-4f, bd0), 1.5f, 0.01f),
            $"[F] ELEMENTAL CHAOS Lv9 (5 elements): chance x{gm.Elements.BurnChance / Mathf.Max(1e-4f, bc0):0.00}, power x{gm.Elements.BurnDps / Mathf.Max(1e-4f, bd0):0.00}");
        deck.Clear(); deck.Add("flame_blade"); MapCardRules.InvalidateDeck();
        MC(McNear(gm.Elements.BurnDps / Mathf.Max(1e-4f, bd0), 1.70f, 0.01f), "[F] CHAOS + FOCUS: only the higher one (focus x1.70 with 1 element, not added)");
        deck.Clear(); deck.AddRange(deckBackup); MapCardRules.InvalidateDeck();

        // ---- ENDLESS GROWTH(Lv9: 0.4% / 撃破、上限 40%)
        McFresh(("endless_growth", 9));
        cs0 = pc.CardStateCondition();
        for (int i = 0; i < 50; i++) gm.MapOnKill();
        float g50 = pc.CardStateCondition() - cs0;
        for (int i = 0; i < 200; i++) gm.MapOnKill();
        float g250 = pc.CardStateCondition() - cs0;
        MC(McNear(g50, 0.20f) && McNear(g250, 0.40f), $"[F] ENDLESS GROWTH Lv9: 50 kills +{g50:0.###}, 250 kills +{g250:0.###} (cap 0.40)");

        // ---- LAST GAMBLE: 報酬(MILE +100/300%・EXP +60/180%)・危険(敵の攻撃 +1 ハート 30%・高火力敵 +10%・判断イベント ×0.7・敵の出現 +15%・敵の HP は変えない)
        McFresh(("last_gamble", 1));
        MC(McNear(gm.Card.Get(EffectType.MileGainMultiplier), 1.0f) && McNear(gm.Card.Get(EffectType.ExpGain), 0.6f) && McNear(gm.Card.Get(EffectType.BossMileGainMultiplier), 1.0f), "[F] LAST GAMBLE Lv1: MILE +100%, EXP +60%");
        McFresh(("last_gamble", 9));
        MC(McNear(gm.Card.Get(EffectType.MileGainMultiplier), 3.0f) && McNear(gm.Card.Get(EffectType.ExpGain), 1.8f), "[F] LAST GAMBLE Lv9: MILE +300%, EXP +180%");
        MC(McNear(CardRewards.HeavyChanceBonus, 0.10f) && McNear(CardRewards.DecisionGapMul, 0.70f) && McNear(gm.EnemySpawnRateMultiplier, 1.15f) && McNear(gm.EnemyHpMultiplier, 1f),
            $"[F] LAST GAMBLE risks: heavy +{CardRewards.HeavyChanceBonus:0.##}, decision gap x{CardRewards.DecisionGapMul:0.##}, spawn x{gm.EnemySpawnRateMultiplier:0.##}, enemy HP x{gm.EnemyHpMultiplier:0.##}");
        int extra0 = MapCardRules.GambleExtraHearts, n = 400, sum = 0;
        for (int i = 0; i < n; i++) { McLives(gm.maxLives); int b = gm.Lives; gm.TryDamagePlayer(false, "EnemyMelee:QaTest", CombatScale.PlayerHit); sum += b - gm.Lives; }
        int extraEnemy = MapCardRules.GambleExtraHearts;
        int obstacle = 0; for (int i = 0; i < 100; i++) { McLives(gm.maxLives); int b = gm.Lives; gm.TryDamagePlayer(false, "Obstacle:QaTest", CombatScale.PlayerHit); obstacle += b - gm.Lives; }
        int extraObstacle = MapCardRules.GambleExtraHearts - extraEnemy;
        McFresh();
        int base0 = 0; for (int i = 0; i < n; i++) { McLives(gm.maxLives); int b = gm.Lives; gm.TryDamagePlayer(false, "EnemyMelee:QaTest", CombatScale.PlayerHit); base0 += b - gm.Lives; }
        float extraRate = (extraEnemy - extra0) / (float)n;
        MC(extraRate > 0.22f && extraRate < 0.38f && sum > base0, $"[F] LAST GAMBLE: enemy hits +1 heart {extraRate:P0} of the time (want 30%), avg {base0 / (float)n / per:0.00} -> {sum / (float)n / per:0.00} hearts");
        MC(extraObstacle == 0, $"[F] LAST GAMBLE does not touch obstacle hits (+1 on {extraObstacle} of 100; obstacle avg {obstacle / 100f / per:0.00} hearts)");

        // ---- BREAK FEAST / BREAK DOMINION(本物のボスの BREAK)
        McFresh(("break_feast", 9), ("break_dominion", 9), ("heart_up", 5));
        SetGod(true);
        BossManager.NetTestBossHpOverride = 400000;
        Bm.DebugWildEncounter(WildBossKind.Wolf, -1);
        yield return WaitBossSpawn(12f);
        var boss = WildAlive().FirstOrDefault();
        MC(boss != null, "[F] test boss appeared");
        if (boss != null)
        {
            McLives(per);
            int breaks0 = MapCardRules.BossBreaksSeen;
            boss.DebugForceBreak();
            yield return null;
            MC(boss.Broken && MapCardRules.BossBreaksSeen == breaks0 + 1 && gm.Lives == per + 3 * per, $"[F] BREAK FEAST Lv9: boss BREAK heals 3 hearts (lives {per} -> {gm.Lives}, broken {boss.Broken})");
            var info = new GameObject("qa-attack").AddComponent<PlayerAttackInfo>();
            float ct = pc.CardTargetCondition(info, boss);
            Object.Destroy(info.gameObject);
            MC(ct >= 0.47f - 0.001f, $"[F] BREAK DOMINION Lv9: attack vs a BREAK boss +{ct:0.###} (want +0.47)");
            MC(MapCardRules.ElementDamageOnBroken(boss, 100, ElementType.Fire) == 142 && MapCardRules.ElementDamageOnBroken(boss, 100, ElementType.None) == 100, "[F] BREAK DOMINION: element damage x1.42 on a BREAK boss");
            int dk0 = MapCardRules.DominionKills;
            MapCardRules.NoteHit(boss); // とどめの一撃が BREAK 中の命中
            autoPickHold = false;
            KillAllBosses();
            float w = 0f; while (MapCardRules.DominionKills == dk0 && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
            MC(MapCardRules.DominionKills == dk0 + 1, $"[F] BREAK DOMINION: boss killed during BREAK -> MILE/EXP x{1f + MapCardRules.DominionReward(9):0.00}");
            w = 0f; while ((gm.IsRewardSequenceRunning || Bm.IsBossPhase) && w < 20f) { yield return null; w += Time.unscaledDeltaTime; }
            autoPickHold = true;
        }
        BossManager.NetTestBossHpOverride = 0;
        SetGod(false);
        McFresh(("break_feast", 9));
        McLives(per); MapCardRules.OnBossBreak(); int h1 = gm.Lives; MapCardRules.OnBossBreak();
        MC(h1 == 4 * per && gm.Lives == h1, "[F] BREAK FEAST: once per 20 s");

        // ---- FINAL DRIVE(Lv9): 80km 以降(移動速度 +20%・攻撃 +70%・BREAK +60%)
        McFresh(("final_drive", 9));
        MC(pc.MapBreakBonus == 0f && pc.MapCardsSpeedMulNow == 1f, $"[F] FINAL DRIVE: nothing before 80 km ({gm.MaxDistance:F0}m)");
        WarpTo(80500f); yield return null; yield return null;
        V3FlatStretch();
        MC(McNear(pc.MapBreakBonus, 0.60f) && McNear(pc.MapCardsSpeedMulNow, 1.20f) && pc.MapCardsStatePctNow >= 0.70f - 0.001f, $"[F] FINAL DRIVE Lv9 at {gm.MaxDistance:F0}m: speed x{pc.MapCardsSpeedMulNow:0.00}, attack +{pc.MapCardsStatePctNow:0.##}, BREAK +{pc.MapBreakBonus:0.##}");

        // ---- ONE MORE CHANCE(Lv9)+ PHOENIX: 倒れる被弾でまず ONE MORE CHANCE(PHOENIX は使わない)→ 次の倒れる被弾は PHOENIX
        McFresh(("one_more_chance", 9), ("phoenix", 1));
        int ph0 = GameManager.PhoenixRevives, omc0 = MapCardRules.OneMoreChances, phc = gm.PhoenixCharges;
        McLives(per); r = McHit(3);
        MC(r == GameManager.DamageResult.Hit && gm.Lives == per && gm.OneMoreChanceUsed && GameManager.PhoenixRevives == ph0 && gm.PhoenixCharges == phc && MapCardRules.OneMoreChances == omc0 + 1,
            $"[F] ONE MORE CHANCE first: survives with 1 heart ({gm.Lives}), PHOENIX not used (revives {GameManager.PhoenixRevives - ph0}, charges {gm.PhoenixCharges})");
        MC(gm.CardInvincibleActive && pc.OneMoreActive && pc.MapCardsStatePctNow >= 1.0f - 0.001f && McNear(pc.MapCardsSpeedMulNow, 1.30f), $"[F] ONE MORE CHANCE: 1.5 s invincible, attack +{pc.MapCardsStatePctNow:0.##}, speed x{pc.MapCardsSpeedMulNow:0.00}");
        t0 = Time.time; while (Time.time - t0 < 1.6f) yield return null;
        McLives(per); r = McHit(3);
        MC(!gm.IsGameOver && GameManager.PhoenixRevives == ph0 + 1 && MapCardRules.OneMoreChances == omc0 + 1, $"[F] second lethal hit: PHOENIX revives (ONE MORE CHANCE only once) ({r})");

        V3Reset();
        McLives(gm.maxLives);
        stopKeepAlive = false; StartCoroutine(KeepAlive());
        yield return V3End();
    }
}
#endif
