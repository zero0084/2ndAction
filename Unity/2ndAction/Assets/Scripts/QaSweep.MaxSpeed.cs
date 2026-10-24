#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 最高速度の設定(2026-10-24、GameSettings.MaxSpeedPercent)の確認。専用の保存(-fileSave)で動かすこと。
//   -qaMaxSpeed <dir> [-msPhase all|restart1|restart2] [-msParts SPEED,ASSIST,CARD,CONT,SPRINT,PLAY,LD]
//  A 値の無い保存 → 100% / B〜E 60・80・100・120% と3キャラ(遅め/標準/速め)の自然加速の最高速度 / F 再起動(restart1 で 80 を保存 → restart2 で読む)
//  G/H/I 高速補助(OFF のまま / ON は今の設定どおり / 開始速度を変えない)/ J 速さのカード・FINAL EVOLUTION の一時的な加速
//  K CONTINUE(中断 → 設定を下げる → 再開は今の設定の速さへ)/ L 疾走出発 / M 60%・120% の通常のラン / N ラスダン
public partial class QaSweep
{
    static readonly (string id, float mob)[] MsChars = { ("archer", 0.93f), ("swordsman", 1f), ("dual_blade", 1.15f) };

    IEnumerator MaxSpeedMode()
    {
        string phase = Arg("-msPhase", "all");
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        yield return new WaitForSecondsRealtime(2f);
        Check(FileSaveStore.Active || MemorySaveStore.Active, "runs on a dedicated save only");
        if (!(FileSaveStore.Active || MemorySaveStore.Active)) yield break;
        if (phase == "restart1")
        {
            GameSettings.SetMaxSpeedPercent(80);
            Check(SaveStore.GetInt(GameSettings.MaxSpeedKey, -1) == 80, "F: 80% is written to the settings save");
            yield break;
        }
        if (phase == "ui") { yield return MsUiShots(); yield break; }
        if (phase == "restart2")
        {
            Check(GameSettings.MaxSpeedPercent == 80, $"F: after restarting the app the setting is still 80% ({GameSettings.MaxSpeedPercent}%)");
            yield break;
        }
        // A: 値の無い保存 → 100%
        Check(!SaveStore.HasKey(GameSettings.MaxSpeedKey) && GameSettings.MaxSpeedPercent == 100, $"A: a save without the value reads 100% ({GameSettings.MaxSpeedPercent}%)");
        Check(SaveKeys.All.Any(e => e.key == GameSettings.MaxSpeedKey && e.cat == SaveCategory.Settings), "A: the key is registered as a setting (SaveKeys)");
        // 5% 刻みへの丸め
        GameSettings.SetMaxSpeedPercent(63); int r63 = GameSettings.MaxSpeedPercent;
        GameSettings.SetMaxSpeedPercent(87); int r87 = GameSettings.MaxSpeedPercent;
        GameSettings.SetMaxSpeedPercent(40); int r40 = GameSettings.MaxSpeedPercent;
        GameSettings.SetMaxSpeedPercent(150); int r150 = GameSettings.MaxSpeedPercent;
        Check(r63 == 65 && r87 == 85 && r40 == 60 && r150 == 120, $"steps: 63->{r63}, 87->{r87}, 40->{r40}, 150->{r150}");
        GameSettings.SetMaxSpeedPercent(100);
        string parts = Arg("-msParts", "SPEED,ASSIST,CARD,CONT,SPRINT,PLAY,LD");
        if (parts.Contains("SPEED")) yield return MsSpeeds();
        if (parts.Contains("ASSIST")) yield return MsAssist();
        if (parts.Contains("CARD")) yield return MsCards();
        if (parts.Contains("CONT")) yield return MsContinue();
        if (parts.Contains("SPRINT")) yield return MsSprint();
        if (parts.Contains("PLAY")) { yield return MsPlay(60); yield return MsPlay(120); }
        if (parts.Contains("LD")) yield return MsLastDungeon();
        GameSettings.SetMaxSpeedPercent(100);
    }

    // 設定画面の撮影: -msPhase ui [-msLangs ja,en,...]。各言語で 100%(標準)/ 60%+補助ON(補助の注意)/ 120%、最後にラン中(変更不可)
    IEnumerator MsUiShots()
    {
        var hsa = HighSpeedAssist.Instance;
        foreach (var lang in Arg("-msLangs", "ja,en").Split(','))
        {
            Loc.Set(lang, false);
            yield return new WaitForSecondsRealtime(0.3f);
            foreach (int pct in new[] { 100, 60, 120 })
            {
                GameSettings.SetMaxSpeedPercent(pct);
                if (hsa != null) { hsa.SetEnabled(true); hsa.SetEngageKmh(100f); }
                SettingsPanel.OpenStatic();
                yield return new WaitForSecondsRealtime(0.5f);
                MsSetScroll(Screen.height > Screen.width ? MsPortraitScroll : MsLandScroll);
                yield return new WaitForSecondsRealtime(0.4f);
                Shot($"maxspeed_{lang}_{pct}");
                yield return null; yield return null;
                SettingsPanel.CloseStatic();
                yield return new WaitForSecondsRealtime(0.3f);
            }
        }
        // ラン中: 変えられない(−/+ が押せない、次のランから)
        GameSettings.SetMaxSpeedPercent(80);
        yield return BeginRun("swordsman", "wasteland_road");
        SettingsPanel.OpenStatic();
        yield return new WaitForSecondsRealtime(0.5f);
        MsSetScroll(Screen.height > Screen.width ? MsPortraitScroll : MsLandScroll);
        yield return new WaitForSecondsRealtime(0.4f);
        Shot($"maxspeed_{Loc.Current}_inrun");
        yield return null; yield return null;
        Check(GameSettings.MaxSpeedPercent == 80, "the setting stays during a run");
        SettingsPanel.CloseStatic();
        yield return EndRun();
        Loc.Set("ja", false);
        GameSettings.SetMaxSpeedPercent(100);
    }
    void MsSetScroll(float y)
    {
        var sp = SettingsPanel.Instance; if (sp == null) return;
        var f = typeof(SettingsPanel).GetField("scroll", NP); if (f != null) f.SetValue(sp, new Vector2(0f, y)); // 描画の時に上限へ丸まる
    }
    float MsLandScroll => float.Parse(Arg("-msScroll", "0"), System.Globalization.CultureInfo.InvariantCulture);
    float MsPortraitScroll => float.Parse(Arg("-msScrollP", "900"), System.Globalization.CultureInfo.InvariantCulture);

    // 平らな所で自然加速の上限まで走らせて、今の速さ(km/h)
    IEnumerator MsTopSpeed(System.Action<float> done, float at = 6500f)
    {
        WarpTo(at);
        yield return new WaitForSeconds(1.5f);
        done(GameManager.SpeedKmh(pc.CurrentAutoRunSpeed));
    }

    // B〜E: 60/80/100/120% × 遅め/標準/速め
    IEnumerator MsSpeeds()
    {
        L("== B-E top speed by setting and character ==");
        var table = new Dictionary<(string, int), float>();
        foreach (int pct in new[] { 60, 80, 100, 120 })
            foreach (var (id, mob) in MsChars)
            {
                GameSettings.SetMaxSpeedPercent(pct);
                yield return BeginRun(id, "wasteland_road");
                SetGod(true);
                if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
                float kmh = 0f; yield return MsTopSpeed(x => kmh = x);
                float want = 100f * mob * pct / 100f;
                table[(id, pct)] = kmh;
                Check(Mathf.Abs(kmh - want) < 0.6f, $"{(pct == 60 ? "B" : pct == 100 ? "C" : pct == 120 ? "D" : "B")}: {id} at {pct}% -> {kmh:F1} km/h (expected {want:F1})");
                SetGod(false);
                yield return EndRun();
            }
        foreach (int pct in new[] { 60, 80, 100, 120 })
            L($"[speed] {pct}%: " + string.Join("  ", MsChars.Select(c => $"{c.id} {table[(c.id, pct)]:F1}")));
        // E: キャラの差が残る(比が同じ)
        foreach (int pct in new[] { 60, 120 })
        {
            float a = table[("archer", pct)] / table[("swordsman", pct)], b = table[("dual_blade", pct)] / table[("swordsman", pct)];
            Check(Mathf.Abs(a - 0.93f) < 0.01f && Mathf.Abs(b - 1.15f) < 0.01f, $"E: character differences stay at {pct}% (archer x{a:F3}, dual_blade x{b:F3})");
        }
        GameSettings.SetMaxSpeedPercent(100);
    }

    // G/H/I: 高速補助
    IEnumerator MsAssist()
    {
        L("== G-I high-speed assist ==");
        var hsa = HighSpeedAssist.Instance;
        hsa.SetEngageKmh(100f); float engage0 = hsa.EngageSettingKmh;
        foreach (int pct in new[] { 60, 120 })
        {
            // G: OFF のまま
            hsa.SetEnabled(false);
            GameSettings.SetMaxSpeedPercent(pct);
            yield return BeginRun("swordsman", "wasteland_road"); SetGod(true);
            hsa.SetEnabled(false);
            float k = 0f; yield return MsTopSpeed(x => k = x);
            // Engaged は「速さが開始速度を超えた」の状態だけ。OFF の時は CurrentStatus が Off で何も入力しない
            L($"[assist] OFF {pct}%: {k:F1} km/h enabled={hsa.assistEnabled} status={hsa.CurrentStatus}");
            Check(!hsa.assistEnabled && hsa.CurrentStatus == HighSpeedAssist.Status.Off, $"G: assist OFF stays off at {pct}% ({k:F0} km/h, {hsa.CurrentStatus})");
            SetGod(false); yield return EndRun();
            // H: ON は今の設定どおり(開始速度 100km/h: 120% なら働く、60% なら届かないので働かない)
            hsa.SetEnabled(true);
            yield return BeginRun("swordsman", "wasteland_road"); SetGod(true);
            hsa.SetEnabled(true);
            yield return MsTopSpeed(x => k = x);
            bool engaged = hsa.Engaged;
            L($"[assist] ON {pct}%: {k:F1} km/h start {hsa.EngageSettingKmh:F0} engaged={engaged} status={hsa.CurrentStatus}");
            Check(hsa.assistEnabled && engaged == (k >= hsa.EngageSettingKmh), $"H: assist ON works as set at {pct}% ({k:F0} km/h vs start {hsa.EngageSettingKmh:F0} -> engaged {engaged})");
            SetGod(false); yield return EndRun();
        }
        // I: 開始速度は変えない(60% にして、開始速度 60km/h にすれば働く)
        Check(Mathf.Abs(hsa.EngageSettingKmh - engage0) < 0.01f, $"I: the assist start speed is not changed by the speed setting ({hsa.EngageSettingKmh:F0})");
        GameSettings.SetMaxSpeedPercent(60); hsa.SetEngageKmh(60f); hsa.SetEnabled(true);
        yield return BeginRun("swordsman", "wasteland_road"); SetGod(true);
        float k6 = 0f; yield return MsTopSpeed(x => k6 = x);
        L($"[assist] 60% start 60: {k6:F1} km/h engaged={hsa.Engaged} status={hsa.CurrentStatus}");
        Check(hsa.Engaged && hsa.CurrentStatus != HighSpeedAssist.Status.Off, $"I: at 60% with start speed 60 km/h the assist works ({k6:F1} km/h)");
        SetGod(false); yield return EndRun();
        hsa.SetEngageKmh(100f);
        GameSettings.SetMaxSpeedPercent(100);
    }

    // J: 速さのカード(上限の後で掛かる)/ FINAL EVOLUTION の一時的な加速(上限で止めない)
    IEnumerator MsCards()
    {
        L("== J speed cards / temporary boost ==");
        var res = new Dictionary<int, (float baseK, float cardK, float feK)>();
        foreach (int pct in new[] { 80, 100 })
        {
            GameSettings.SetMaxSpeedPercent(pct);
            yield return BeginRun("swordsman", "wasteland_road"); SetGod(true);
            if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
            float b = 0f; yield return MsTopSpeed(x => b = x);
            var su = CardDatabase.FindBaseById("speed_up"); gm.ApplyCardEffectsStacked(su, 9);
            yield return new WaitForSeconds(0.5f);
            float c = GameManager.SpeedKmh(pc.CurrentAutoRunSpeed);
            gm.FinalEvoTestPrepare("speed_up"); FinalEvolution.DebugMakeReady("speed_up");
            FinalEvolution.Activate("speed_up");
            yield return new WaitForSeconds(0.5f);
            float f = GameManager.SpeedKmh(pc.CurrentAutoRunSpeed);
            res[pct] = (b, c, f);
            L($"[card] {pct}%: natural {b:F1} -> SPEED UP Lv9 {c:F1} -> + FINAL EVOLUTION {f:F1} km/h");
            FinalEvolution.DebugEnd("speed_up");
            SetGod(false); yield return EndRun();
        }
        float r80 = res[80].cardK / res[80].baseK, r100 = res[100].cardK / res[100].baseK;
        Check(res[80].cardK > res[80].baseK + 1f && Mathf.Abs(r80 - r100) < 0.01f, $"J: SPEED UP works the same at 80% (x{r80:F3}) as at 100% (x{r100:F3})");
        Check(res[80].feK > res[80].cardK + 0.5f, $"J: the FINAL EVOLUTION boost is not stopped at the 80% top speed ({res[80].cardK:F1} -> {res[80].feK:F1})");
        GameSettings.SetMaxSpeedPercent(100);
    }

    // K: CONTINUE(100% で中断 → ホームで 60% → 再開)
    IEnumerator MsContinue()
    {
        L("== K CONTINUE ==");
        autoPickHold = true;
        GameSettings.SetMaxSpeedPercent(100);
        yield return BeginRun("swordsman", "wasteland_road");
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
        WarpTo(6000f);
        yield return new WaitForSecondsRealtime(1.5f);
        typeof(GameManager).GetMethod("SaveCheckpoint", NP).Invoke(gm, null);
        float before = GameManager.SpeedKmh(pc.CurrentAutoRunSpeed);
        yield return ResumeGoHome();
        GameSettings.SetMaxSpeedPercent(60);
        yield return ResumeContinue("K");
        float target = GameManager.SpeedKmh(pc.NormalAutoRunSpeed);
        float w = 0f, maxSeen = 0f;
        while (w < 8f) { maxSeen = Mathf.Max(maxSeen, GameManager.SpeedKmh(pc.CurrentAutoRunSpeed)); yield return null; w += Time.deltaTime; }
        float after = GameManager.SpeedKmh(pc.CurrentAutoRunSpeed);
        L($"[continue] 100% -> home -> 60% -> CONTINUE: before {before:F1}, target {target:F1}, after 8s {after:F1}, max {maxSeen:F1} km/h, ledger {RunLedger.Current?.speedSettingPercent}%/{RunLedger.Current?.speedSettingMax}%");
        Check(Mathf.Abs(target - 60f) < 0.6f && Mathf.Abs(after - 60f) < 0.6f && maxSeen < 61f, $"K: CONTINUE after lowering to 60% runs at the current setting ({before:F1} before -> target {target:F1}, after 8s {after:F1}, max {maxSeen:F1} km/h)");
        Check(RunLedger.Current != null && RunLedger.Current.speedSettingPercent == 60 && RunLedger.Current.speedSettingMax == 100, $"K: the run record keeps the setting ({RunLedger.Current?.speedSettingPercent}%, max used {RunLedger.Current?.speedSettingMax}%)");
        autoPickHold = false;
        yield return EndRun();
        GameSettings.SetMaxSpeedPercent(100);
    }

    // L: 疾走出発(10km)— 到着後は設定の最高速度で走る
    IEnumerator MsSprint()
    {
        L("== L sprint departure ==");
        SprintRecords.DevUnlockAll = true; // 専用の保存: 疾走の行き先を選べるように
        foreach (int pct in new[] { 80, 120 })
        {
            GameSettings.SetMaxSpeedPercent(pct);
            yield return WaitHome();
            SprintRecords.MarkGateCleared("wasteland_road", 10);
            gm.SetSelectedCharacter("swordsman");
            SprintRunner.QaRingPolicy = 1; SprintRunner.QaTimeScale = 3f;
            bool ok = gm.DepartSprint("wasteland_road", 10000);
            Check(ok, $"L: sprint departure accepted at {pct}%");
            float w = 0f;
            while (w < 120f && !(gm.HasStarted && !gm.CountdownActive && !gm.SprintActive && !gm.ResumeGateActive))
            {
                if (gm.IsRewardSequenceWaitingForSelection) { var seq = FindFirstObjectByType<RewardCardSequence>(); if (seq != null) { seq.OnCardClicked(0); yield return new WaitForSecondsRealtime(0.25f); seq.OnCardClicked(0); } }
                yield return null; w += Time.unscaledDeltaTime;
            }
            pc = PlayerController.Instance;
            yield return new WaitForSeconds(3f);
            // 道中で自動で取った速さのカード(SPEED UP/DOWN)は runSpeed に入る: 期待値 = 設定 × (runSpeed / 5m/s)
            float k = GameManager.SpeedKmh(pc.CurrentAutoRunSpeed), want = pct * pc.runSpeed / 5f;
            L($"[sprint] {pct}%: after arrival {gm.MaxDistance:F0}m {k:F1} km/h (expected {want:F1} = {pct}% x cards {pc.runSpeed / 5f:F3})");
            Check(gm.MaxDistance > 9700f && Mathf.Abs(k - want) < 1f, $"L: after the sprint the run goes at the {pct}% top speed ({k:F1} vs {want:F1} km/h)");
            SprintRunner.QaRingPolicy = 0; SprintRunner.QaTimeScale = 1f;
            yield return EndRun();
        }
        SprintRecords.DevUnlockAll = false;
        GameSettings.SetMaxSpeedPercent(100);
    }

    // M: 60% / 120% の通常のラン(0m から、補助なし・無敵): 雑魚・障害物・ボスの関門・距離が進む
    IEnumerator MsPlay(int pct)
    {
        L($"== M normal run at {pct}% ==");
        GameSettings.SetMaxSpeedPercent(pct);
        yield return BeginRun("swordsman", "wasteland_road"); SetGod(true);
        if (HighSpeedAssist.Instance != null) { HighSpeedAssist.Instance.SetEnabled(true); HighSpeedAssist.Instance.SetAutoAttack(true); }
        int bosses0 = Bm.BossesDefeated, kills0 = gm.EnemyKillCount;
        int enemiesSeen = 0, obstaclesSeen = 0; float t0 = Time.time, maxKmh = 0f, maxNat = 0f, lastD = gm.MaxDistance; int backwards = 0;
        while (gm.MaxDistance < 4600f && Time.time - t0 < 800f)
        {
            if (Bm.IsBossPhase && Bm.AliveBossCount > 0 && Time.time - t0 > 0f) { yield return new WaitForSeconds(6f); KillAllBosses(); }
            enemiesSeen = Mathf.Max(enemiesSeen, EnemyController.ActiveCount);
            obstaclesSeen = Mathf.Max(obstaclesSeen, FindObjectsByType<ObstacleController>(FindObjectsSortMode.None).Length);
            float kmhNow = GameManager.SpeedKmh(pc.CurrentAutoRunSpeed);
            maxKmh = Mathf.Max(maxKmh, kmhNow);
            maxNat = Mathf.Max(maxNat, kmhNow / Mathf.Max(0.01f, pc.runSpeed / 5f)); // 道中で取った速さのカードを除いた分(自然加速の上限)
            if (gm.MaxDistance < lastD - 0.5f) backwards++;
            lastD = gm.MaxDistance;
            yield return new WaitForSeconds(0.5f);
        }
        int gates = Bm.BossesDefeated - bosses0;
        L($"[play {pct}%] {gm.MaxDistance:F0}m in {Time.time - t0:F0}s, top {maxKmh:F1} km/h (without speed cards {maxNat:F1}), bosses down {gates}, kills {gm.EnemyKillCount - kills0}, max enemies alive {enemiesSeen}, obstacles {obstaclesSeen}");
        Check(gm.MaxDistance >= 4600f && !gm.IsGameOver, $"M {pct}%: the run reaches 4.6km ({gm.MaxDistance:F0}m)");
        Check(Mathf.Abs(maxNat - pct) < 1f, $"M {pct}%: natural top speed {maxNat:F1} km/h (with cards {maxKmh:F1})");
        Check(gates >= 4 && enemiesSeen > 0 && obstaclesSeen > 0 && backwards == 0, $"M {pct}%: bosses/enemies/obstacles/distance all work (bosses {gates}, enemies {enemiesSeen}, obstacles {obstaclesSeen}, backwards {backwards})");
        SetGod(false); yield return EndRun();
        GameSettings.SetMaxSpeedPercent(100);
    }

    // N: ラスダン(80%): 90km のラッシュは今までどおり、速さは設定どおり
    IEnumerator MsLastDungeon()
    {
        L("== N last dungeon ==");
        GameSettings.SetMaxSpeedPercent(80);
        yield return BeginRun("swordsman", BossManager.LastStageId); SetGod(true);
        float k = 0f; yield return MsTopSpeed(x => k = x, 50000f);
        WarpTo(89900f);
        float w = 0f; while (!Bm.RushActive && w < 40f) { yield return null; w += Time.unscaledDeltaTime; }
        Check(Mathf.Abs(k - 80f) < 1f && Bm.RushActive, $"N: last dungeon at 80% runs at {k:F1} km/h and the 90km rush starts ({Bm.RushActive})");
        SetGod(false); yield return EndRun();
        GameSettings.SetMaxSpeedPercent(100);
    }
}
#endif
