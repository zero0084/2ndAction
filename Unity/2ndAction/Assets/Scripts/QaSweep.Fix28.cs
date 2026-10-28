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
        if (parts.Contains("formsim")) yield return F28FormSim();
        if (parts.Contains("hpcards")) yield return F28HpCards();
        if (parts.Contains("fight")) yield return F28Fight();
        if (parts.Contains("ui")) yield return F28Ui();
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

    // 画面: カード合成の背景(-fakeSafeLeft で左に切り欠きがあるつもり)と DEBUG パネルのスクロール
    IEnumerator F28Ui()
    {
        L("== ui: fusion backdrop / debug scroll ==");
        var open = typeof(GameManager).GetMethod("OpenCardFusion", NP | BindingFlags.Public);
        open.Invoke(gm, null);
        yield return new WaitForSecondsRealtime(2.0f);
        Shot($"ui_fusion_{Screen.width}x{Screen.height}_safeL{StableSafeArea.Left:0}");
        var fusion = FindFirstObjectByType<CardFusionUI>();
        var bd = fusion != null ? fusion.transform.Find("Backdrop") as RectTransform : null;
        if (bd != null)
        {
            var corners = new Vector3[4]; bd.GetWorldCorners(corners);
            var cv = bd.GetComponentInParent<Canvas>();
            Vector2 mn = RectTransformUtility.WorldToScreenPoint(cv != null && cv.renderMode != RenderMode.ScreenSpaceOverlay ? cv.worldCamera : null, corners[0]);
            L($"[ui] fusion backdrop screen min {mn} (screen {Screen.width}x{Screen.height}, safe left {StableSafeArea.Left})");
            Check(mn.x <= 1f && mn.y <= 1f, $"ui: the fusion backdrop covers the screen edge ({mn})");
        }
        typeof(GameManager).GetMethod("CloseCardFusion", NP | BindingFlags.Public)?.Invoke(gm, null);
        yield return new WaitForSecondsRealtime(1.5f);
        DebugPanel.OpenStatic();
        yield return new WaitForSecondsRealtime(0.6f);
        Shot($"ui_debug_top_{Screen.width}x{Screen.height}");
        var sc = typeof(DebugPanel).GetField("scroll", NP).GetValue(DebugPanel.Instance) as UiScroll;
        if (sc != null) { sc.y = 10000f; yield return null; yield return new WaitForSecondsRealtime(0.4f); Shot($"ui_debug_bottom_{Screen.width}x{Screen.height}"); Check(sc.CanScroll, $"ui: the debug panel scrolls on a {Screen.width}x{Screen.height} window (y={sc.y:0})"); }
        DebugPanel.CloseStatic();
        yield return new WaitForSecondsRealtime(0.5f);
    }

    struct FormRec { public int km; public string formation; public List<string> keys; public int bodies; public float distBefore, distAfter; public bool held; }

    // hpcards: 最大HPが変わるカード(増える/封印する)を Lv1〜9 まで1枚ずつ取り、取る前後の最大HP/現在HPを記録する(2026-10-28 マスターの依頼④)。
    //  取る前は毎回ハート2つ分減らしておく(「増えた分だけ増える」か「全回復」かを見分ける)。Lv9 の後に最終進化を発動して、その前後も記録
    IEnumerator F28HpCards()
    {
        L("== hpcards: max/current HP before and after each pick ==");
        var NPS = BindingFlags.NonPublic | BindingFlags.Instance;
        var apply = typeof(GameManager).GetMethod("ApplyCardEffects", NPS);
        var livesSet = typeof(GameManager).GetProperty("Lives").GetSetMethod(true);
        string[] ids = Arg("-f28HpCards", "heart_up,fortress,heavy_armor,iron_will,long_haul,the_long_road,ultimate,berserker,deaths_contract,glass_cannon,heart_breaker").Split(',');
        int per = CombatScale.HpPerHeart;
        foreach (var id in ids)
        {
            yield return BeginRun("swordsman", "wasteland_road");
            stopKeepAlive = true; // 試験の「HP を 990 に戻す」見張りを止める(回復量を測るので)
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
            if (BossManager.Instance != null) BossManager.Instance.enabled = false;
            Time.timeScale = 0f; // 走らない(敵や障害物で HP が変わらない)
            var card = CardDatabase.FindBaseById(id);
            if (card == null) { L($"[hpcards] {id}: no card"); Time.timeScale = 1f; yield return EndRun(); continue; }
            var sb = new System.Text.StringBuilder();
            for (int lv = 1; lv <= 9; lv++)
            {
                livesSet.Invoke(gm, new object[] { Mathf.Max(per, gm.maxLives - 2 * per) });
                int m0 = gm.maxLives, h0 = gm.Lives;
                apply.Invoke(gm, new object[] { card });
                int m1 = gm.maxLives, h1 = gm.Lives;
                string kind = h1 == m1 && h0 < m0 && m1 - m0 < h1 - h0 ? "全回復" : h1 - h0 == m1 - m0 && m1 != m0 ? "増えた分だけ" : h1 == h0 ? "変わらない" : "その他";
                sb.Append($" Lv{lv}: 最大 {m0 / per}→{m1 / per}♥ 現在 {h0 / per}→{h1 / per}♥({kind})");
                if (lv == 1 || lv == 9) L($"[hpcards] {id} Lv{lv}: max {m0}->{m1} hp {h0}->{h1} ({kind})");
                // 2026-10-28 夜(案A): 最大HPが増えた Lv だけ全回復。増えない Lv は今のHPのまま(最大を超える分だけ切る)
                if (m1 > m0) Check(h1 == m1, $"hpcards {id} Lv{lv}: max HP grew ({m0}->{m1}) -> full heal ({h0}->{h1})");
                else Check(h1 == Mathf.Min(h0, m1), $"hpcards {id} Lv{lv}: max HP did not grow ({m0}->{m1}) -> no heal ({h0}->{h1})");
                yield return null;
            }
            L($"[hpcards] {id}:{sb}");
            if (id == "heart_up")
            {
                // 上限(ハート99個)で増えなかった時も全回復しない: 上限を今の最大HPに一時的に下げて、もう1枚取る
                int keepCap = CardRules.MaxHeartsCap;
                var lvBefore = typeof(GameManager).GetField("cardLevels", NPS)?.GetValue(gm) as System.Collections.Generic.Dictionary<string, int>;
                if (lvBefore != null && lvBefore.ContainsKey(id)) lvBefore[id] = Mathf.Min(lvBefore[id], 8); // Lv9 の上限で止まらないように1つ戻して作り直す
                gm.RecomputeCardStats();
                CardRules.MaxHeartsCap = Mathf.Max(1, gm.maxLives / per); // 上限 = 今の最大HP(次の1枚は上限で増えない)
                livesSet.Invoke(gm, new object[] { Mathf.Max(per, gm.maxLives - 2 * per) });
                int cm0 = gm.maxLives, ch0 = gm.Lives;
                apply.Invoke(gm, new object[] { card });
                L($"[hpcards] heart_up at the cap: max {cm0}->{gm.maxLives} hp {ch0}->{gm.Lives}");
                Check(gm.maxLives == cm0 && gm.Lives == ch0, $"hpcards heart_up at the heart cap: no growth -> no heal ({cm0}/{ch0} -> {gm.maxLives}/{gm.Lives})");
                CardRules.MaxHeartsCap = keepCap;
                apply.Invoke(gm, new object[] { card }); // 上限を戻して作り直す(以後の計測に影響しないように)
            }
            // 最終進化(Lv9 の後)
            FinalEvolution.DebugMakeReady(id);
            livesSet.Invoke(gm, new object[] { Mathf.Max(per, gm.maxLives - 2 * per) });
            int fm0 = gm.maxLives, fh0 = gm.Lives;
            bool ok = FinalEvolution.Activate(id);
            yield return null; yield return null;
            L($"[hpcards] {id} FE activate={ok}: max {fm0 / per}→{gm.maxLives / per}♥ hp {fh0 / per}→{gm.Lives / per}♥ ({gm.Lives - fh0:+0;-0;0} HP / max {gm.maxLives - fm0:+0;-0;0})");
            if (ok)
            {
                // 中断・再開(FE の状態を書き出して読み直す = CONTINUE と同じ経路)で最大HP/今のHPが変わらない
                int rm = gm.maxLives, rh = gm.Lives;
                FinalEvolution.Import(FinalEvolution.Export());
                Check(gm.maxLives == rm && gm.Lives <= gm.maxLives, $"hpcards {id}: FE state reload keeps max/current HP ({rm}/{rh} -> {gm.maxLives}/{gm.Lives})");
                // 終わり: 最大HPは発動前へ戻り、今のHPは最大を超えない
                FinalEvolution.DebugEnd(id);
                yield return null;
                Check(gm.maxLives == fm0 && gm.Lives <= gm.maxLives, $"hpcards {id}: FE end returns max HP and keeps current <= max ({gm.maxLives}/{fm0}, hp {gm.Lives})");
                L($"[hpcards] {id} FE end: max {gm.maxLives / per}♥ hp {gm.Lives / per}♥");
            }
            Time.timeScale = 1f;
            yield return EndRun(); // 次のカードは新しいランで(前のカードの最大HPを持ち越さない)
        }
    }

    // formsim: 生成なしで関門の抽選だけを N 回通し、距離帯ごとの体数の割合と3体にならない理由を数える(BossManager.DevFormationSim)
    IEnumerator F28FormSim()
    {
        L("== formsim: bodies per band (lottery only) ==");
        int runs = int.Parse(Arg("-f28SimRuns", "400"));
        foreach (var st in Arg("-f28FormStages", "wasteland_road,natural_cave,sky_corridor,last_corridor").Split(','))
        {
            if (st == BossManager.LastStageId) SaveStore.SetInt(SaveKeys.DevFinalDungeonAlwaysOpen, 1); // 試験用データではラスダンが未解放(試験用の保存だけに書く)
            yield return BeginRun("swordsman", st);
            var bm = BossManager.Instance;
            foreach (var v in Arg("-f28SimVariants", "0,4").Split(','))
            {
                BossManager.DevFormationVariant = int.Parse(v);
                L($"[formsim] stage {st} (active {gm.ActiveRunStageId}) variant {v} ({(v == "0" ? "採用済み(慣れたボスの割引)" : v == "4" ? "割引なし(採用前)" : v == "1" ? "+小ボスの重ね" : v == "2" ? "+ラスダンにも割引" : "?")}) runs={runs}");
                foreach (var line in bm.DevFormationSim(runs).Split('\n')) if (line.Trim().Length > 0) L(line.TrimEnd());
            }
            BossManager.DevFormationVariant = 0;
            yield return EndRun(); // 次のステージの BeginRun は前のランが終わるのを待つ
        }
    }

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
        BossManager.DevFormationVariant = int.Parse(Arg("-f28FightVariant", "0")); // 比較用: 4 = 割引なし(採用前の編成)
        foreach (var st in Arg("-f28FightStages", "wasteland_road").Split(','))   // 2026-10-28 夜: 洞窟/天空でも(慣れたボスの割引の後の実戦)
        foreach (var kmS in kms)
        {
            int km = int.Parse(kmS);
            yield return BeginRun("swordsman", st);
            var bm = BossManager.Instance; bm.enabled = true;
            // そこまでの関門を通常どおり倒して来た状態(再戦プール)にする: 疾走出発と同じ扱いで飛ばした関門をプールへ
            bm.SprintMarkSkippedGates(km - 1);
            int flv = int.Parse(Arg("-f28FightLv", "7"));
            foreach (var id in deck.Distinct()) { var c = CardDatabase.FindBaseById(id); if (c != null) gm.DebugApplyRunCard(c, flv); }
            gm.RecomputeCardStats();
            // 2026-10-28 夜: 操作する人がいないと、ボス戦の間に穴や壁で倒れていた(9戦中5戦)。闘技場の試験(-qaDuel)と同じく、
            //  ゲームの補助(HighSpeedAssist: ボス戦の中も自動で避ける/攻撃する)で戦い、足場は平らにする(地形ではなくボスとの戦いを見る)。
            //  関門の手前 400m へ飛び、そこから先を平らにしてから関門へ走る(関門の場所の地形が生成される前に)
            gm.DebugWarpToDistance(km * 1000f - 400f);
            yield return new WaitForSeconds(0.5f);
            var hsa = HighSpeedAssist.Instance;
            if (hsa != null) { hsa.SetEnabled(true); hsa.SetAutoInBoss(true); hsa.SetAutoAttack(true); hsa.SetAutoAvoid(true); }
            var tmf = TerrainManager.Instance;
            if (tmf != null) { float lx = FloatingOrigin.ToLogical(pc.transform.position.x); tmf.SetResumeFlatZone(lx - 5f, lx + 3000f); }
            foreach (var ob in FindObjectsByType<ObstacleController>(FindObjectsSortMode.None)) Destroy(ob.gameObject);
            var os = FindFirstObjectByType<ObstacleSpawner>(); if (os != null) os.enabled = false;
            if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false; // 雑魚なし(ボスとの戦いだけ)
            float w = 0f;
            while (!(bm.IsBossPhase && bm.AliveBossCount > 0) && w < 60f) { yield return null; w += Time.unscaledDeltaTime; }
            stopKeepAlive = true; // ここから通常の被弾
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
            var lives = typeof(GameManager).GetProperty("Lives"); lives.GetSetMethod(true).Invoke(gm, new object[] { gm.maxLives });
            int hpStart = gm.Lives, bodies = BossObjectsAlive();
            string form = BossManager.LastFormation;
            float t0 = Time.realtimeSinceStartup;
            Shot($"fight_{st}_{km}km_start");
            while (bm.IsBossPhase && !gm.IsGameOver && Time.realtimeSinceStartup - t0 < 240f) yield return new WaitForSeconds(0.25f);
            string res = gm.IsGameOver ? "DOWN(" + gm.DeathReason + ")" : bm.IsBossPhase ? "TIMEOUT" : "CLEARED";
            if (os != null) os.enabled = true;
            if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = true;
            L($"[fight] {st} {km}km {res} in {Time.realtimeSinceStartup - t0:F0}s bodies={bodies} HP {hpStart} -> {gm.Lives}/{gm.maxLives} | {form}");
            Shot($"fight_{st}_{km}km_end");
            Check(res != "TIMEOUT", $"fight: {km}km ends within 240s ({res})");
            yield return EndRun();
        }
        BossManager.DevFormationVariant = 0;
    }
}
#endif
