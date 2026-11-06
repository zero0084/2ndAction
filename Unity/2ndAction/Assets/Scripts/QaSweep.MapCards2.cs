#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

// マップ専用カードの追加の作業(2026-11-06 夜、マスターの依頼)の試験。
//  -qaRunOnce <dir> [-roPhase all|plant|check] [-roShared <dir>]
//     1ランに1回のカード(SECOND RISE / ONE MORE CHANCE)と CONTINUE、ラン中のレベルの最大HP(GameManager.LevelHp)と CONTINUE。
//     all = 1つの起動の中で(-memSave で)。plant/check = 本当の再起動(-fileSave で2回起動。plant で中断して終了 → check で CONTINUE)
//  -qaBloodHud <dir>   血の盾の HUD の撮影(横 1920x1080 / 縦 608x1080、表示モード 標準/シンプル/非表示)。窓の表示で起動する
//  -qaBigDetail <dir>  長押しの大きな詳細を全カード(横/縦)で開き、効果の文が重ならないか・窓に収まるか
public partial class QaSweep
{
    static readonly PropertyInfo RoLevelProp = typeof(GameManager).GetProperty("Level");
    static readonly MethodInfo RoApplyLevelHp = typeof(GameManager).GetMethod("ApplyLevelHp", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly MethodInfo RoApplyCard = typeof(GameManager).GetMethod("ApplyCardEffects", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(CardDefinition) }, null);
    static readonly MethodInfo RoSaveCheckpoint = typeof(GameManager).GetMethod("SaveCheckpoint", BindingFlags.NonPublic | BindingFlags.Instance);

    int roPass, roFail;
    void RO(bool ok, string what) { if (ok) { roPass++; L("[ok] " + what); } else roFail++; Check(ok, what); }

    // ラン中の取得と同じ経路(取得の履歴にも入る = CONTINUE で取り直される)
    void RoPick(params string[] ids)
    {
        var hist = (List<CardDefinition>)typeof(GameManager).GetField("upgradeHistory", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(gm);
        foreach (var id in ids) { var c = CardDatabase.FindById(id); if (c == null) { Warn($"unknown card {id}"); continue; } RoApplyCard.Invoke(gm, new object[] { c }); hist.Add(c); }
    }
    void RoSetLevel(int lv) { RoLevelProp.GetSetMethod(true).Invoke(gm, new object[] { lv }); RoApplyLevelHp.Invoke(gm, null); }
    void RoLives(int v) => V3LivesSet.Invoke(gm, new object[] { v });
    void RoCheckpoint() => RoSaveCheckpoint.Invoke(gm, null);
    // 倒れる被弾(ONE MORE CHANCE の確認)/ HP25% 以下へ(SECOND RISE)
    void RoLethal() { RoLives(CombatScale.HpPerHeart); gm.TryDamagePlayer(false, "qa-runonce", 3 * CombatScale.HpPerHeart); }
    void RoLow() { RoLives(Mathf.RoundToInt(gm.maxLives * 0.25f) + CombatScale.HpPerHeart); gm.TryDamagePlayer(false, "qa-runonce", 2 * CombatScale.HpPerHeart); }

    IEnumerator RunOnceMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        gm = GameManager.Instance;
        TutorialProgress.MarkOffered();
        string phase = Arg("-roPhase", "all");
        L($"[runonce] phase {phase} levelHp every {GameManager.LevelHpEvery} max +{GameManager.LevelHpMaxHearts} (fileSave {FileSaveStore.Active})");
        if (phase == "plant" || phase == "check") { yield return RunOnceRestart(phase); L($"[runonce] checks passed {roPass} / failed {roFail}"); yield break; }
        var snap = SaveSystem.Capture();
        autoPickHold = true;
        int per = CombatScale.HpPerHeart;

        // ---- U: 使わずに中断 → CONTINUE: まだ使える
        yield return BeginRun("swordsman", "wasteland_road");
        stopKeepAlive = true; SetGod(false);
        RoPick("one_more_chance", "second_rise", "heart_up");
        RoCheckpoint();
        RO(!gm.OneMoreChanceUsed && !gm.SecondRiseUsed, "[U] new run: both unused");
        yield return ResumeGoHome(); yield return ResumeContinue("U");
        RO(gm.CardLevel("one_more_chance") == 1 && gm.CardLevel("second_rise") == 1, $"[U] CONTINUE: cards replayed (OMC Lv{gm.CardLevel("one_more_chance")}, SR Lv{gm.CardLevel("second_rise")})");
        RO(!gm.OneMoreChanceUsed && !gm.SecondRiseUsed, "[U] unused -> suspend -> CONTINUE: still available");
        // ---- A: 使ってから中断(チェックポイントは使う前のまま = 中断の保存は台帳だけ)→ CONTINUE: 使ったまま
        RoLow(); RO(gm.SecondRiseUsed, $"[A] SECOND RISE used (lives {gm.Lives}/{gm.maxLives})");
        yield return new WaitForSecondsRealtime(0.2f);
        RoLethal(); RO(gm.OneMoreChanceUsed && gm.Lives == per, $"[A] ONE MORE CHANCE used (lives {gm.Lives})");
        yield return new WaitForSecondsRealtime(1.7f);
        yield return ResumeGoHome();
        var cp = RunCheckpoint.Load();
        RO(cp.mapOneMoreChanceUsed && cp.mapSecondRiseUsed && gm.InterruptSavesLedgerOnly >= 0, $"[A] the interrupt save keeps 'used' (OMC {cp.mapOneMoreChanceUsed}, SR {cp.mapSecondRiseUsed})");
        yield return ResumeContinue("A");
        RO(gm.OneMoreChanceUsed && gm.SecondRiseUsed, "[A] used -> suspend -> CONTINUE: stays used");
        int omc0 = MapCardRules.OneMoreChances;
        RoLives(per); var r = gm.TryDamagePlayer(false, "qa-runonce", 3 * per);
        RO(MapCardRules.OneMoreChances == omc0 && (gm.IsGameOver || gm.Lives <= 0 || r == GameManager.DamageResult.GameOver), $"[A] after CONTINUE ONE MORE CHANCE does not fire again ({r})");
        yield return EndRun();
        RO(!RunCheckpoint.HasActiveRun, "[D] death clears the run (no CONTINUE)");

        // ---- N: 新しいラン = 未使用
        yield return BeginRun("swordsman", "wasteland_road");
        stopKeepAlive = true;
        RoPick("one_more_chance", "second_rise");
        RO(!gm.OneMoreChanceUsed && !gm.SecondRiseUsed, "[N] a new run starts with both unused");
        // ---- B: 使ってからチェックポイント(ボスの報酬など)→ 中断 → CONTINUE: 使ったまま
        RoLethal(); yield return new WaitForSecondsRealtime(1.7f);
        RoCheckpoint();
        yield return ResumeGoHome(); yield return ResumeContinue("B");
        RO(gm.OneMoreChanceUsed && gm.SecondRiseUsed, $"[B] used before a checkpoint -> CONTINUE: stays used (OMC {gm.OneMoreChanceUsed}, SR {gm.SecondRiseUsed})");
        // ---- H: ホームへ戻ってから「はじめから」(中断を捨てる)= 新しいラン
        yield return ResumeGoHome();
        RunCheckpoint.Clear();
        yield return BeginRun("swordsman", "wasteland_road");
        stopKeepAlive = true;
        RoPick("one_more_chance");
        RO(!gm.OneMoreChanceUsed, "[H] return home -> new run: reset");
        yield return EndRun();

        // ---- L: ラン中のレベルの最大HP と CONTINUE
        if (GameManager.LevelHpEvery > 0)
        {
            // L1: カードあり(取得のやり直しの経路)
            yield return BeginRun("swordsman", "wasteland_road");
            stopKeepAlive = true;
            int baseMax = gm.maxLives;
            RoSetLevel(1 + 3 * GameManager.LevelHpEvery); // +3 ハート
            RO(gm.maxLives == baseMax + 3 * per && gm.LevelHpApplied == 3, $"[L1] level HP: Lv{gm.Level} max {baseMax} -> {gm.maxLives} (+3 hearts)");
            RoPick("heart_up", "heart_up");
            int maxWith = gm.maxLives;
            RO(maxWith == baseMax + 5 * per, $"[L1] HEART UP adds on top of level HP ({gm.maxLives}, want {baseMax + 5 * per})");
            RoLives(maxWith - per);
            RoCheckpoint();
            yield return ResumeGoHome(); yield return ResumeContinue("L1");
            RO(gm.maxLives == maxWith && gm.LevelHpApplied == 3 && gm.Lives == maxWith - per, $"[L1] CONTINUE restores max HP {gm.maxLives} (want {maxWith}), lives {gm.Lives}, level hearts {gm.LevelHpApplied}");
            RoSetLevel(1 + 4 * GameManager.LevelHpEvery);
            RO(gm.maxLives == maxWith + per && gm.LevelHpApplied == 4, $"[L1] next level step after CONTINUE: +1 heart only ({gm.maxLives})");
            RoSetLevel(1 + 20 * GameManager.LevelHpEvery);
            int capHearts = GameManager.LevelHpMaxHearts;
            RO(gm.LevelHpApplied == capHearts && gm.maxLives == maxWith + (capHearts - 3) * per, $"[L1] cap +{capHearts} kept ({gm.LevelHpApplied}, max {gm.maxLives})");
            int maxCap = gm.maxLives;
            RoCheckpoint();
            yield return ResumeGoHome(); yield return ResumeContinue("L1b");
            RO(gm.maxLives == maxCap && gm.LevelHpApplied == capHearts, $"[L1] CONTINUE at the cap: max {gm.maxLives} (want {maxCap}), no double grant");
            RoPick("heart_up");
            RO(gm.maxLives == maxCap + per, $"[L1] HEART UP after CONTINUE still adds ({gm.maxLives})");
            yield return ResumeGoHome();
            RunCheckpoint.Clear();
            // L2: 新しいラン = レベルの最大HP は無い
            yield return BeginRun("swordsman", "wasteland_road");
            stopKeepAlive = true;
            RO(gm.LevelHpApplied == 0 && gm.maxLives == baseMax, $"[L2] new run: no level HP carried ({gm.LevelHpApplied}, max {gm.maxLives})");
            // L3: カードなし(古い形の経路: 保存した最大HPをそのまま使う)
            int cardsNow = gm.CardLevelOrder.Count;
            RoSetLevel(1 + 2 * GameManager.LevelHpEvery);
            int max0 = gm.maxLives;
            RoCheckpoint();
            yield return ResumeGoHome(); yield return ResumeContinue("L3");
            RO(gm.maxLives == max0 && gm.LevelHpApplied == 2, $"[L3] CONTINUE without picked cards (character cards {cardsNow}): max {gm.maxLives} (want {max0})");
            yield return ResumeGoHome();
            RunCheckpoint.Clear();
        }
        else Warn("level HP is disabled (levelHpEvery 0) - L skipped");

        autoPickHold = false;
        SaveSystem.Restore(snap);
        CardInventory.ReloadFromPrefs(); RunCheckpoint.Reload();
        L($"[runonce] checks passed {roPass} / failed {roFail}");
        L("[runonce] test machine save restored");
    }

    // 本当の再起動(-fileSave): plant = 使って中断して終了 / check = 起動 → CONTINUE → 同じか
    IEnumerator RunOnceRestart(string phase)
    {
        string shared = Arg("-roShared", outDir);
        System.IO.Directory.CreateDirectory(shared);
        string expectPath = System.IO.Path.Combine(shared, "runonce_expect.txt");
        RO(FileSaveStore.Active, "runs on the dedicated save file (-fileSave), not the normal save");
        if (!FileSaveStore.Active) yield break;
        int per = CombatScale.HpPerHeart;
        if (phase == "plant")
        {
            RunCheckpoint.Clear();
            yield return BeginRun("swordsman", "wasteland_road");
            stopKeepAlive = true; SetGod(false);
            RoPick("one_more_chance", "second_rise", "heart_up");
            if (GameManager.LevelHpEvery > 0) RoSetLevel(1 + 3 * GameManager.LevelHpEvery);
            RoCheckpoint();
            RoLow(); // SECOND RISE を使う(ONE MORE CHANCE は使わない)
            yield return new WaitForSecondsRealtime(0.3f);
            int lives = gm.Lives, max = gm.maxLives, lvHp = gm.LevelHpApplied;
            RO(gm.SecondRiseUsed && !gm.OneMoreChanceUsed, $"[R] plant: SECOND RISE used, ONE MORE CHANCE unused (lives {lives}/{max}, level hearts {lvHp})");
            yield return ResumeGoHome();
            SaveStore.Save();
            System.IO.File.WriteAllText(expectPath, $"{lives}\n{max}\n{lvHp}");
            RO(RunCheckpoint.HasActiveRun, "[R] plant: the run is suspended (CONTINUE is offered on the next start)");
            yield break;
        }
        // check(起動し直した後)
        var lines = System.IO.File.ReadAllLines(expectPath);
        int eLives = int.Parse(lines[0]), eMax = int.Parse(lines[1]), eLv = int.Parse(lines[2]);
        RO(RunCheckpoint.HasActiveRun, "[R] check: the suspended run survived the app restart");
        yield return ResumeContinue("R");
        RO(gm.SecondRiseUsed && !gm.OneMoreChanceUsed, $"[R] after restart + CONTINUE: SECOND RISE used {gm.SecondRiseUsed}, ONE MORE CHANCE unused {!gm.OneMoreChanceUsed}");
        RO(gm.maxLives == eMax && gm.Lives == eLives && gm.LevelHpApplied == eLv, $"[R] after restart + CONTINUE: lives {gm.Lives}/{gm.maxLives} (want {eLives}/{eMax}), level hearts {gm.LevelHpApplied} (want {eLv})");
        stopKeepAlive = true; SetGod(false);
        int omc0 = MapCardRules.OneMoreChances;
        RoLethal();
        RO(MapCardRules.OneMoreChances == omc0 + 1 && gm.Lives == per, "[R] the unused ONE MORE CHANCE still works after the restart");
        yield return new WaitForSecondsRealtime(1.7f);
        yield return ResumeGoHome();
        RunCheckpoint.Clear(); SaveStore.Save();
    }

    // ===================================================================== 血の盾の HUD
    IEnumerator BloodHudMode()
    {
        var snap = SaveSystem.Capture();
        autoPickHold = true;
        yield return V3Begin("swordsman");
        V3FlatStretch();
        stopKeepAlive = true; SetGod(true);
        var modeKeep = HudPrefs.CurrentMode;
        string[] sizes = Arg("-bhSizes", "1920x1080,608x1080").Split(',');
        foreach (var size in sizes)
        {
            var wh = size.Split('x'); int w = int.Parse(wh[0]), h = int.Parse(wh[1]);
            Screen.SetResolution(w, h, FullScreenMode.Windowed);
            float t = 0f; while ((Screen.width > Screen.height) != (w > h) && t < 3f) { yield return null; t += Time.unscaledDeltaTime; }
            yield return new WaitForSecondsRealtime(1f);
            string tag = (w > h ? "land_" : "port_") + $"{Screen.width}x{Screen.height}";
            foreach (var m in new[] { HudPrefs.Mode.Standard, HudPrefs.Mode.Simple, HudPrefs.Mode.Hidden })
            {
                HudPrefs.SetMode(m);
                V3Reset(); V3Apply("shield", 6); V3Apply("blood_armor", 9); V3Apply("heart_up", 2);
                gm.QaResetMapCooldowns();
                V3LivesSet.Invoke(gm, new object[] { gm.maxLives - 2 * CombatScale.HpPerHeart });
                gm.QaSetBloodShield(2);
                if (m == HudPrefs.Mode.Hidden) gm.NoteHudDamage(); // 非表示モード: 被弾の直後だけ HP の枠が出る
                int d0 = GameManager.BloodHudDraws;
                yield return new WaitForSecondsRealtime(0.6f);
                if (m == HudPrefs.Mode.Hidden) gm.NoteHudDamage();
                yield return ShotW($"blood_{tag}_{m}");
                Check(GameManager.BloodHudDraws > d0, $"[bh] {tag} {m}: the blood shield badge is drawn (shield {pc.ShieldCharges}/{pc.ShieldCapacity}, blood {gm.BloodShieldHearts})");
            }
            // 0 の時は出ない
            HudPrefs.SetMode(HudPrefs.Mode.Standard);
            gm.QaSetBloodShield(0);
            int d1 = GameManager.BloodHudDraws;
            yield return new WaitForSecondsRealtime(0.4f);
            yield return ShotW($"blood_{tag}_zero");
            Check(GameManager.BloodHudDraws == d1, $"[bh] {tag}: hidden when the blood shield is 0");
        }
        HudPrefs.SetMode(modeKeep);
        V3Reset(); SetGod(false);
        stopKeepAlive = false; StartCoroutine(KeepAlive());
        yield return V3End();
        autoPickHold = false;
        SaveSystem.Restore(snap);
        L("[bh] test machine save restored");
    }

    // ===================================================================== 長押しの大きな詳細
    IEnumerator BigDetailMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.2f);
        gm = GameManager.Instance;
        var snap = SaveSystem.Capture();
        TutorialProgress.MarkOffered();
        SaveStore.SetInt(DeckCapacityNotice.Key, 1);
        int pass = 0, fail = 0, warnTop = 0;
        var cards = CardDatabase.AllCards.Where(c => !CardVariant.IsVariantKey(c.cardId)).ToList();
        L($"[bd] cards {cards.Count}");
        string[] shotIds = Arg("-bdShots", "attack_up,shield,vampire,last_bastion,elemental_chaos,last_gamble,one_more_chance").Split(',');
        var sb = new StringBuilder("orient\tid\tlevel\tpanel\ttitle\ttop\ttopH\tviewH\tdupLine\tdupDesc\tmain\n");
        foreach (bool land in new[] { true, false })
        {
            yield return ClRes(land);
            yield return ClOpenDeck(land ? "bd_land" : "bd_port");
            var ui = gm.deckEditUI;
            if (ui == null) { Check(false, "[bd] deck edit opened"); break; }
            string orient = land ? "land" : "port";
            var keys = new List<(string key, int lv, CardDefinition baseCard)>();
            foreach (var c in cards) keys.Add((c.cardId, 1, c));
            // 合成/Lv5 のカード(主能力 Lv5 + 副能力)も少し
            foreach (var id in new[] { "attack_up", "last_bastion", "final_drive" })
            {
                var v = new CardVariant { mainId = id, level = 5, rarity = Mathf.Clamp(CardDatabase.FindBaseById(id).rarity, 1, 5) };
                v.AddAbility(id, 5); v.AddAbility("speed_up", 3);
                keys.Add((v.ToKey(), 5, CardDatabase.FindBaseById(id)));
            }
            foreach (var k in keys)
            {
                ui.OpenBigDetail(k.key, k.lv);
                yield return null;
                string info = ui.DebugBigInfo, plain = Strip(info);
                string mainId = CardMastery.BaseIdOf(k.key);
                int cur = k.lv;
                string lineCur = Strip(Loc.Auto(CardVariant.AbilityEffectTextAt(mainId, cur)));
                string line1 = Strip(Loc.Auto(CardVariant.AbilityEffectTextAt(mainId, 1))), line9 = Strip(Loc.Auto(CardVariant.AbilityEffectTextAt(mainId, 9)));
                string desc = Strip(Loc.Auto(k.baseCard.DisplayDescription));
                int Count(string s, string sub) { if (string.IsNullOrEmpty(sub)) return 0; int n = 0, i = 0; while ((i = s.IndexOf(sub, i, System.StringComparison.Ordinal)) >= 0) { n++; i += sub.Length; } return n; }
                // 2026-11-06 夜: Lv ごとに見出しの行 → 効果の部品を1行ずつ。② の区切り(レベル別の効果 〜 COMBO)の中だけに出る・① ③ に重ならない
                string hLv = Loc.T(LocDeckEdit.LvEffects), hCombo = Loc.T(LocDeckEdit.ComboHead);
                int s0 = plain.IndexOf(hLv, System.StringComparison.Ordinal), s1 = s0 >= 0 ? plain.IndexOf(hCombo, s0, System.StringComparison.Ordinal) : -1;
                string section = s0 >= 0 && s1 > s0 ? plain.Substring(s0, s1 - s0) : "";
                string outside = s0 >= 0 && s1 > s0 ? plain.Substring(0, s0) + plain.Substring(s1) : plain;
                var parts = lineCur.Split(new[] { " / " }, System.StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
                string block = $"Lv{cur}({Loc.T(LocDeckEdit.Current)})\n" + string.Join("\n", parts.Select(x => "・" + x));
                bool lineShown = section.Contains(block);
                bool oldStyle = System.Text.RegularExpressions.Regex.IsMatch(section, @"(?m)^Lv\d+[^\n]*: ");
                int dupLine = parts.Where(x => x.Length >= 5).Sum(x => Count(outside, x));
                if (oldStyle) dupLine += 100;
                int dupDesc = Count(plain, desc) - 1;
                bool mainTag = plain.Contains("MAIN");
                ui.DebugBigLayout(out bool panelIn, out bool titleOk, out bool topOk, out float topH, out float viewH);
                bool ok = panelIn && titleOk && dupLine <= 0 && dupDesc == 0 && !mainTag && !string.IsNullOrEmpty(desc) && lineShown;
                if (ok) pass++; else { fail++; L($"[FAIL] [bd] {orient} {k.key} Lv{k.lv}: panel {panelIn} title {titleOk} dupLine {dupLine} dupDesc {dupDesc} MAIN {mainTag} lineShown {lineShown} oldStyle {oldStyle}"); failures++; }
                if (!topOk) { warnTop++; L($"[bd-note] {orient} {k.key}: summary + level comparison {topH:F0}px > view {viewH:F0}px (the rest scrolls)"); }
                sb.Append(orient).Append('\t').Append(k.key).Append('\t').Append(k.lv).Append('\t').Append(panelIn).Append('\t').Append(titleOk).Append('\t').Append(topOk).Append('\t').Append(topH.ToString("F0"))
                  .Append('\t').Append(viewH.ToString("F0")).Append('\t').Append(dupLine).Append('\t').Append(dupDesc).Append('\t').Append(mainTag).Append('\n');
                if (shotIds.Contains(k.key) || (CardVariant.IsVariantKey(k.key) && mainId == "last_bastion")) yield return ShotW($"bd_{orient}_{(CardVariant.IsVariantKey(k.key) ? mainId + "_v5" : k.key)}");
                ui.CloseBigDetail();
            }
            ui.Close();
            yield return new WaitForSecondsRealtime(1.2f);
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "bigdetail.tsv"), sb.ToString());
        L($"[bd] opened {pass + fail}: pass {pass} / fail {fail}; summary+levels taller than the view (scrolls): {warnTop}");
        SaveSystem.Restore(snap);
    }
}
#endif
