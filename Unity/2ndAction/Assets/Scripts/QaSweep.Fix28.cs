#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// 2026-10-28 実機の不具合12件の確認: -qaFix28 <dir> [-f28Parts dup,hp,form,fight] [-f28FormKm 92,65,65,89] [-f28FightKm 35,65,91]
//  dup  : 3択の候補に同じ強化が2枠出ない(デッキに同じカードが2枚・合成カードあり)。候補が少ない時は少ない枚数のまま
//  hp   : 最大HPの上限 = ハート99個(990)。今のHPは最大を超えない。HP の FINAL EVOLUTION の取得前後の最大HP/今のHP
//  form : 距離の予算で複数のボス(30km未満は1体、30km〜 予算4/5/6・最大3体・同じボスなし・巨体は1体)。全員を倒すまで距離が止まり、終われば再開
//  fight: 無敵なしで複数のボスの関門を通常の被弾で戦う(デッキ B Lv7、自動の補助)。撃破/倒れた/時間切れ と被弾を記録
public partial class QaSweep
{
    IEnumerator Fix28Mode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();
        UnlockRules.DevUnlockAll = true;
        var parts = Arg("-f28Parts", "dup,hp,form,fight").Split(',');
        if (parts.Contains("dup")) yield return F28Dup();
        if (parts.Contains("hp")) yield return F28Hp();
        if (parts.Contains("form")) yield return F28Form();
        if (parts.Contains("fight")) yield return F28Fight();
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }

    IEnumerator F28Dup()
    {
        L("== dup: no duplicate offers ==");
        var shuffle = typeof(GameManager).GetMethod("ShuffleDistinctChoices", BindingFlags.NonPublic | BindingFlags.Static);
        var a = CardDatabase.FindBaseById("attack_range_up"); var b = CardDatabase.FindBaseById("attack_up"); var c = CardDatabase.FindBaseById("heart_up"); var d = CardDatabase.FindBaseById("speed_up");
        Check(a != null && b != null && c != null && d != null, "dup: test cards exist");
        int dupSeen = 0, aFirst = 0, n = 400;
        for (int i = 0; i < n; i++)
        {
            var pool = new List<CardDefinition> { a, a, a, b, c, d }; // 同じカードが3枚デッキにある
            shuffle.Invoke(null, new object[] { pool, "qa" });
            var top = pool.Take(3).Select(x => x.cardId).ToList();
            if (top.Distinct().Count() != top.Count) dupSeen++;
            if (top.Contains(a.cardId)) aFirst++;
        }
        Check(dupSeen == 0, $"dup: {n} draws with the same card 3 times in the deck -> never two of it in the 3 slots ({dupSeen})");
        L($"[dup] the card held 3 times appears in the 3 choices {aFirst}/{n} times (weighted by copies, cap 1 per screen)");
        // 候補が少ない: 同じカードしか無い → 1枚だけ(水増しも、止まりもしない)
        var small = new List<CardDefinition> { a, a };
        shuffle.Invoke(null, new object[] { small, "qa-small" });
        Check(small.Count == 1, $"dup: only duplicates of one card -> 1 offer, no padding ({small.Count})");
        // ゲームの経路: デッキに同じカードを2枚入れて LEVEL UP の3択を何度も作る
        yield return BeginRun("swordsman", "wasteland_road");
        var compose = typeof(GameManager).GetMethod("ComposeChoice", NP);
        var deckField = typeof(GameManager).GetField("deckCards", NP);
        var deck = deckField.GetValue(gm) as List<string>;
        var keep = deck != null ? new List<string>(deck) : null;
        if (deck != null) { deck.Clear(); deck.AddRange(new[] { "attack_range_up", "attack_range_up", "attack_up", "heart_up" }); }
        int dupGame = 0;
        for (int i = 0; i < 200; i++)
        {
            var pool = new List<CardDefinition>();
            foreach (var id in deck) { var cd = CardDatabase.FindById(id); if (cd != null) pool.Add(cd); }
            var res = (CardDefinition[])compose.Invoke(gm, new object[] { pool, "qa" });
            var ids = res.Where(x => x != null).Select(x => x.cardId).ToList();
            if (ids.Distinct().Count() != ids.Count) dupGame++;
        }
        Check(dupGame == 0, $"dup: ComposeChoice with ATTACK RANGE UP twice in the deck -> no duplicate in 200 screens ({dupGame})");
        if (deck != null && keep != null) { deck.Clear(); deck.AddRange(keep); }
        yield return EndRun();
    }

    IEnumerator F28Hp()
    {
        L("== hp: heart cap 99 / FINAL EVOLUTION HP ==");
        int cap = CardRules.MaxHeartsCap * CombatScale.HpPerHeart;
        Check(cap == 990, $"hp: cap = 99 hearts = {cap}");
        yield return BeginRun("dragonkin", "wasteland_road"); // 基本 HP が一番多いキャラ
        stopKeepAlive = true;
        var lives = typeof(GameManager).GetProperty("Lives"); var setLives = lives.GetSetMethod(true);
        foreach (var id in new[] { "heart_up", "fortress", "iron_will", "long_haul", "the_long_road", "heavy_armor" })
        {
            var cd = CardDatabase.FindBaseById(id);
            if (cd != null) gm.DebugApplyRunCard(cd, GameManager.MaxRunCardLevel);
            L($"[hp] +{id} Lv9 -> max {gm.maxLives} HP {gm.Lives} (before seal {gm.CardMaxLivesBeforeSeal})");
        }
        gm.RecomputeCardStats();
        Check(gm.maxLives <= cap && gm.Lives <= gm.maxLives, $"hp: all HP cards Lv9 -> max {gm.maxLives} <= {cap}, HP {gm.Lives} <= max");
        Check(gm.maxLives > 200, $"hp: the old 20-heart limit no longer stops growth ({gm.maxLives})");
        Shot("hp_all_cards");
        // FINAL EVOLUTION: 鉄の心臓(heart_up)
        setLives.Invoke(gm, new object[] { gm.maxLives });
        int max0 = gm.maxLives, hp0 = gm.Lives;
        FinalEvolution.DebugMakeReady("heart_up");
        bool act = FinalEvolution.Activate("heart_up");
        yield return null;
        int max1 = gm.maxLives, hp1 = gm.Lives;
        L($"[hp] FE heart_up activate={act}: max {max0} -> {max1} (+{max1 - max0}), HP {hp0} -> {hp1} (+{hp1 - hp0})");
        Check(act && max1 > max0, $"hp: FE heart_up raises max HP ({max0} -> {max1})");
        Check(hp1 - hp0 == max1 - max0, $"hp: the added hearts are filled, not shown empty (HP +{hp1 - hp0} for max +{max1 - max0})");
        Shot("hp_fe_heart_up");
        FinalEvolution.DebugEnd("heart_up");
        yield return null;
        Check(gm.maxLives == max0 && gm.Lives <= gm.maxLives, $"hp: FE end -> max back to {gm.maxLives} (was {max0}), HP {gm.Lives} <= max");
        // 上限で止まる: 増えた分が上限を越えないこと
        Check(gm.maxLives <= cap, "hp: never above the cap");
        yield return EndRun();
        // 単独の HP カードだけの増え方(普通のデッキに近い)
        yield return BeginRun("swordsman", "wasteland_road");
        stopKeepAlive = true;
        var hu = CardDatabase.FindBaseById("heart_up");
        gm.DebugApplyRunCard(hu, GameManager.MaxRunCardLevel);
        gm.RecomputeCardStats();
        setLives.Invoke(gm, new object[] { gm.maxLives });
        int m0 = gm.maxLives;
        FinalEvolution.DebugMakeReady("heart_up"); FinalEvolution.Activate("heart_up"); yield return null;
        L($"[hp] swordsman HEART UP Lv9: max {m0} -> FE {gm.maxLives} (+{gm.maxLives - m0}), HP {gm.Lives}");
        Check(gm.maxLives - m0 == Mathf.RoundToInt(9 * 0.6f * CombatScale.HpPerHeart), $"hp: FE ×1.6 of the HEART UP growth (9 hearts) = +{9 * 0.6f:0.0} hearts ({gm.maxLives - m0})");
        FinalEvolution.DebugEnd("heart_up");
        yield return EndRun();
    }

    struct FormRec { public int km; public string formation; public List<string> keys; public int bodies; public float distBefore, distAfter; public bool held; }

    IEnumerator F28Form()
    {
        L("== form: bosses by distance budget ==");
        string[] stages = Arg("-f28FormStages", "wasteland_road,natural_cave,sky_corridor,last_corridor").Split(',');
        string[] kms = Arg("-f28FormKm", "92,65,65,89").Split(',');
        var ld = LastDungeonBossTuning.I;
        for (int si = 0; si < stages.Length; si++)
        {
            string st = stages[si];
            int limitKm = si < kms.Length ? int.Parse(kms[si]) : 40;
            yield return BeginRun("swordsman", st);
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            var bm = BossManager.Instance; bm.enabled = true;
            var recs = new List<FormRec>();
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 1500f)
            {
                float w = 0f;
                while (bm.IsBossPhase && w < 30f) { yield return null; w += Time.unscaledDeltaTime; }
                if (bm.NextBossDistance > limitKm * 1000f + 10f) break;
                if (gm.MaxDistance < bm.NextBossDistance - 60f) gm.DebugWarpToDistance(bm.NextBossDistance - 40f);
                w = 0f;
                while (!(bm.IsBossPhase && bm.AliveBossCount > 0) && w < 25f) { yield return null; w += Time.unscaledDeltaTime; }
                if (!bm.IsBossPhase) { L($"[form:{st}] no encounter near {bm.NextBossDistance:F0}m"); continue; }
                yield return new WaitForSeconds(0.6f);
                w = 0f;
                while (BossObjectsAlive() == 0 && w < 8f) { yield return null; w += Time.unscaledDeltaTime; }
                yield return new WaitForSeconds(0.5f);
                var keys = FindObjectsByType<WildBossBase>(FindObjectsSortMode.None).Where(b => !b.IsDead).Select(b => b.DeathKey).ToList();
                int dragons = FindObjectsByType<DragonController>(FindObjectsSortMode.None).Count(b => !b.IsDead) + FindObjectsByType<MajinController>(FindObjectsSortMode.None).Count(b => !b.IsDead);
                var r = new FormRec { km = bm.CurrentGateIndex, formation = BossManager.LastFormation, keys = keys, bodies = keys.Count + dragons, distBefore = gm.MaxDistance };
                if (r.km >= 30 && (r.km == 30 || r.km == 31 || r.km == 35 || r.km == 60 || r.km == 61 || r.km == 65 || r.km == 80 || r.km == 90 || r.km == 89)) Shot($"form_{st}_{r.km}km");
                yield return new WaitForSeconds(1.2f);
                r.held = Mathf.Abs(gm.MaxDistance - r.distBefore) < 1f; // 戦闘中は距離が止まる
                L($"[form:{st}] {r.km,3}km bodies={r.bodies} [{string.Join(", ", keys)}]{(dragons > 0 ? $" +{dragons} dragon/majin" : "")} held={r.held} | {(r.km >= 30 ? r.formation : "")}");
                w = 0f;
                while (bm.IsBossPhase && w < 40f) { if (bm.AliveBossCount > 0) KillAllBosses(); yield return new WaitForSeconds(0.25f); w += 0.25f; }
                r.distAfter = gm.MaxDistance;
                recs.Add(r);
            }
            bool phaseEnded = !bm.IsBossPhase;
            var early = recs.Where(x => x.km < 30).ToList();
            var late = recs.Where(x => x.km >= 30 && x.km % 10 != 0 || x.km >= 30).ToList();
            Check(recs.Count > 5, $"{st}: gates recorded ({recs.Count})");
            Check(early.All(x => x.bodies <= 4), $"{st}: before 30km the gates keep their usual count");
            Check(late.All(x => x.bodies <= 3), $"{st}: from 30km never more than 3 bosses together ({string.Join(",", late.Where(x => x.bodies > 3).Select(x => x.km + "km=" + x.bodies))})");
            Check(late.All(x => x.keys.Distinct().Count() == x.keys.Count), $"{st}: no duplicate boss in one gate");
            Check(late.All(x => x.keys.Count(k => (ld.TagsOf(k) & LastDungeonBossTuning.Tag.Large) != 0) <= 1), $"{st}: at most one large boss per gate");
            int multi = late.Count(x => x.bodies >= 2);
            Check(late.Count == 0 || multi > 0, $"{st}: from 30km some gates have 2-3 bosses ({multi}/{late.Count})");
            Check(recs.All(x => x.held), $"{st}: distance stops while the bosses are alive ({string.Join(",", recs.Where(x => !x.held).Select(x => x.km + "km"))})");
            Check(phaseEnded, $"{st}: after all are defeated the run resumes");
            if (st == "last_corridor") Check(recs.All(x => !x.keys.Contains("Sky/MechDragon") || x.bodies == 1), "last_corridor: the Mecha Dragon is always alone");
            else Check(recs.All(x => !x.keys.Contains("Sky/MechDragon")), $"{st}: no Mecha Dragon outside the last dungeon");
            yield return EndRun();
        }
    }

    IEnumerator F28Fight()
    {
        L("== fight: multi-boss gates with real damage ==");
        string[] kms = Arg("-f28FightKm", "35,65,91").Split(',');
        var deck = DuelDecks["B"];
        foreach (var kmS in kms)
        {
            int km = int.Parse(kmS);
            yield return BeginRun("swordsman", "wasteland_road");
            var bm = BossManager.Instance; bm.enabled = true;
            // そこまでの関門を通常どおり倒して来た状態(再戦プール)にする: 疾走出発と同じ扱いで飛ばした関門をプールへ
            bm.SprintMarkSkippedGates(km - 1);
            foreach (var id in deck.Distinct()) { var c = CardDatabase.FindBaseById(id); if (c != null) gm.DebugApplyRunCard(c, 7); }
            gm.RecomputeCardStats();
            gm.DebugWarpToDistance(km * 1000f - 60f);
            float w = 0f;
            while (!(bm.IsBossPhase && bm.AliveBossCount > 0) && w < 30f) { yield return null; w += Time.unscaledDeltaTime; }
            stopKeepAlive = true; // ここから通常の被弾
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
            var lives = typeof(GameManager).GetProperty("Lives"); lives.GetSetMethod(true).Invoke(gm, new object[] { gm.maxLives });
            int hpStart = gm.Lives, bodies = BossObjectsAlive();
            string form = BossManager.LastFormation;
            float t0 = Time.realtimeSinceStartup;
            Shot($"fight_{km}km_start");
            while (bm.IsBossPhase && !gm.IsGameOver && Time.realtimeSinceStartup - t0 < 240f) yield return new WaitForSeconds(0.25f);
            string res = gm.IsGameOver ? "DOWN" : bm.IsBossPhase ? "TIMEOUT" : "CLEARED";
            L($"[fight] {km}km {res} in {Time.realtimeSinceStartup - t0:F0}s bodies={bodies} HP {hpStart} -> {gm.Lives}/{gm.maxLives} | {form}");
            Shot($"fight_{km}km_end");
            Check(res != "TIMEOUT", $"fight: {km}km ends within 240s ({res})");
            yield return EndRun();
        }
    }
}
#endif
