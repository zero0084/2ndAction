#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// プロフィール/走る理由/常設チャレンジ/バッヂ/定型コミュニケーション(2026-11-07)の自動テスト: -qaProfile <dir> -pfPhase <phase>
//  必ず -memSave か -fileSave と一緒に(本物のセーブに触らない)。画面ありの exe で(OnGUI の撮影)。
//  logic    … 判定(条件を満たした時だけ/報酬1回/ノーダメージは被弾で失敗/オート OFF/初期デッキ/コンボ/速さ/空中/BREAK/ボス/死神)、
//              新しいランでラン内だけ戻る、CONTINUE で壊れない、既存の記録からの復元(通知なし・1回だけ)、装備の決まり、通知でゲームが止まらない
//  persist1 / persist2 … -fileSave で2回起動: 1回目で達成・走る理由・装備 → 2回目で同じか、報酬を二重に受け取っていないか
//  shots    … 横 1280x720 / 縦 540x960 の撮影(ホームのボタン/プロフィール/走る理由/バッヂとチャレンジ/装備の枠/鍵の案内/達成の通知)
//  langs    … 全言語(-pfLangs ja,en,... で絞れる)×横/縦 のプロフィール/バッヂ/走る理由の撮影と、文字の見切れ(TextFit)の記録
public partial class QaSweep
{
    IEnumerator ProfileMode()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        gm = GameManager.Instance;
        TutorialProgress.MarkOffered();
        string ph = Arg("-pfPhase", "logic");
        L($"[profile] phase {ph}  screen {Screen.width}x{Screen.height}  lang {Loc.Current}");
        if (ph == "logic") yield return PfLogic();
        else if (ph == "persist1") yield return PfPersist(1);
        else if (ph == "persist2") yield return PfPersist(2);
        else if (ph == "shots") yield return PfShots();
        else if (ph == "langs") yield return PfLangs();
        else Check(false, "unknown -pfPhase " + ph);
    }

    static bool PfDone(string id) => ChallengeStore.IsDone(id);
    static int PfMile() => GameManager.Instance != null ? GameManager.Instance.TotalOwnedMile : SaveStore.GetInt(GameManager.TotalMileKey, 0);
    static int PfSumDoneMile() { int s = 0; foreach (var d in ChallengeDefs.All) if (ChallengeStore.IsDone(d.id)) s += d.mile; return s; }
    static IEnumerator PfFrames(int n) { for (int i = 0; i < n; i++) yield return null; }
    void PfClean()
    {
        ChallengeStore.DevResetAll();
        SaveStore.SetInt(ChallengeStore.InitKey, 1);
        ProfileStore.SetReason(0);
        for (int i = 0; i < ProfileStore.Slots; i++) ProfileStore.Unequip(i);
        ChallengeToast.ClearAll();
    }

    // ===================================================================== logic
    IEnumerator PfLogic()
    {
        PfClean();
        int mileStart = PfMile();
        int toastStart = ChallengeToast.ShownCount;
        var hsa = HighSpeedAssist.Instance;

        // ---- L1 装備の決まり(未獲得は装備できない)
        Check(ProfileStore.Equip(0, "dist_10k") == ProfileStore.EquipResult.NotOwned, "L1: an unearned badge cannot be equipped");
        Check(ProfileStore.Equip(0, "no_such_id") == ProfileStore.EquipResult.Unknown, "L1: an unknown id is refused");

        // ---- L2 ラン: オートは最初から OFF(保存しない)・初期デッキ
        if (hsa != null) hsa.assistEnabled = false;
        yield return BeginRun("swordsman", "wasteland_road");
        yield return PfFrames(3);
        var r = RunLedger.Current;
        if (r != null && r.debugUsed) { L("[L2] run was marked debug at start (" + r.debugWhat + ") - cleared for the test"); RunLedger.DevClearDebug(); }
        Check(r != null && r.ch != null && r.ch.v == 1 && !r.ch.hit && !r.ch.autoSeen, $"L2: a new run starts with fresh run counters (v {r?.ch?.v} hit {r?.ch?.hit} auto {r?.ch?.autoSeen})");
        Check(r != null && r.ch.starterDeck, $"L2: the test data deck counts as the starter deck ({string.Join(",", gm.DeckCards)})");
        Check(!ChallengeTracker.IsStarterDeck(new List<string> { "speed_up", "vampire" }), "L2: a deck with a non-starter card is not the starter deck");
        float ts0 = Time.timeScale;
        int m0 = PfMile();
        RunLedger.DevSet(x => { x.walked = 9990; x.maxReached = 9990; });
        yield return PfFrames(3);
        Check(!PfDone("nodmg_10k") && !PfDone("dist_10k"), "L2: 9,990m is not enough for 10km");
        RunLedger.DevSet(x => { x.walked = 10050; x.maxReached = 10050; });
        yield return PfFrames(3);
        Check(PfDone("nodmg_10k") && PfDone("dist_10k"), "L2: 10km reached with no damage -> UNTOUCHED 10 KM and REACH 10 KM");
        Check(PfMile() - m0 == 50 + 30, $"L2: MILE +80 for the two ({PfMile() - m0})");
        Check(Mathf.Approximately(Time.timeScale, ts0), $"L2: the completion notice does not change the time scale ({ts0} -> {Time.timeScale})");
        yield return new WaitForSecondsRealtime(0.5f);
        Check(ChallengeToast.ShownCount > toastStart, "L2: the CHALLENGE COMPLETE notice is shown");
        yield return ShotW("pf_toast_run");
        Check(!gm.IsGameOver && gm.HasStarted && Time.timeScale > 0.5f, "L2: the run keeps going while the notice is up");
        // 被弾 → ノーダメージ失敗
        gm.TryDamagePlayer(true, "QaProfile", CombatScale.PlayerHit);
        yield return PfFrames(2);
        Check(r.ch.hit && r.ch.hits >= 1, "L2: the hit is recorded at once");
        RunLedger.DevSet(x => { x.walked = 30050; x.maxReached = 50050; });
        yield return PfFrames(3);
        Check(!PfDone("nodmg_30k"), "L2: UNTOUCHED 30 KM fails after a hit");
        Check(PfDone("dist_50k"), "L2: REACH 50 KM");
        Check(PfDone("sp_noauto"), "L2: ON MY OWN FEET (auto OFF for the whole run, 30km walked)");
        RunLedger.DevSet(x => { x.walked = 50050; });
        yield return PfFrames(3);
        Check(PfDone("sp_starter"), "L2: STARTER'S ROAD (starter deck, 50km walked)");
        int m1 = PfMile();
        Check(!ChallengeStore.Complete("nodmg_10k") && PfMile() == m1, "L2: completing again gives nothing (reward once)");

        // コンボ
        var cc = ComboCounterUI.Instance;
        if (cc != null)
        {
            for (int i = 0; i < 99; i++) cc.RegisterHit();
            yield return PfFrames(2);
            Check(!PfDone("combo_100"), "L2: 99 HIT is not 100");
            cc.RegisterHit(); yield return PfFrames(2);
            Check(PfDone("combo_100"), "L2: 100 HIT");
            for (int i = 0; i < 400; i++) cc.RegisterHit();
            yield return PfFrames(2);
            Check(PfDone("combo_500") && !PfDone("combo_1000"), "L2: 500 HIT (not 1000)");
        }
        else Check(false, "L2: no ComboCounterUI");

        // BREAK
        ChallengeStore.DevSetCounter("breaks", 0);
        for (int i = 0; i < 4; i++) MapCardRules.OnBossBreak();
        Check(!PfDone("break_run5") && r.ch.breaks == 4, "L2: 4 breaks is not 5");
        MapCardRules.OnBossBreak();
        Check(PfDone("break_run5") && ChallengeStore.Counter("breaks") == 5, "L2: 5 BREAKS in a run (total counter 5)");
        ChallengeStore.DevSetCounter("breaks", 49);
        MapCardRules.OnBossBreak();
        Check(PfDone("break_total50"), "L2: 50 BREAKS TOTAL");

        // 空中
        float gw = 0f; while (!pc.IsGrounded && gw < 3f) { yield return null; gw += Time.unscaledDeltaTime; }
        gm.RegisterEnemyKill(1f, 1f, true);
        Check(!PfDone("air_kill"), "L2: a kill on the ground is not AERIAL KILL");
        yield return PfJump();
        Check(!pc.IsGrounded, "L2: the player is in the air for the test");
        gm.RegisterEnemyKill(1f, 1f, true);
        Check(PfDone("air_kill"), "L2: AERIAL KILL");
        ChallengeStore.DevSetCounter("air_kills", 99);
        yield return PfJump();
        gm.RegisterEnemyKill(1f, 1f, true);
        Check(PfDone("air_kill_100"), $"L2: 100 AERIAL KILLS ({ChallengeStore.Counter("air_kills")})");
        // 空中に6秒(上向きの速さを与え続ける)
        var vy = typeof(PlayerController).GetField("velocityY", NP);
        float t = 0f;
        yield return PfJump();
        while (t < 5.2f) { vy.SetValue(pc, 1.2f); yield return null; t += Time.deltaTime; }
        Check(!PfDone("air_stay"), "L2: 5.2s in the air is not 6s");
        while (t < 9.5f && !PfDone("air_stay")) { vy.SetValue(pc, 1.2f); yield return null; t += Time.deltaTime; }
        Check(PfDone("air_stay"), $"L2: 6 SEC AIRBORNE ({t:F1}s)");

        // ボス
        ChallengeTracker.OnBossDefeated("Wild/Wolf");
        Check(!PfDone("boss_garmud") && !PfDone("boss_rush"), "L2: a normal boss is not the wasteland king");
        ChallengeTracker.OnBossDefeated("Wild/Garmud");
        Check(PfDone("boss_garmud") && PfDone("boss_rush"), "L2: WASTELAND KING (and FINAL TRIAD: the final boss only appears after the set rush)");
        ChallengeTracker.OnBossDefeated("Cave/Noctaris"); ChallengeTracker.OnBossDefeated("Sky/Seraphiel");
        Check(PfDone("boss_noctaris") && PfDone("boss_seraphiel"), "L2: ABYSS SOVEREIGN / SKY EMPEROR");
        // ノーダメージのボス撃破: ボス戦の始まりからの被弾
        Bm.NetTestSpawnWild(WildBossKind.Wolf, 1);
        yield return PfFrames(3);
        Check(Bm.IsBossPhase && r.ch.hitsAtBoss == r.ch.hits, $"L2: the boss fight start is recorded (phase {Bm.IsBossPhase}, at {r.ch.hitsAtBoss}, hits {r.ch.hits})");
        gm.TryDamagePlayer(true, "QaProfile", CombatScale.PlayerHit);
        yield return PfFrames(2);
        ChallengeTracker.OnBossDefeated("Wild/Wolf");
        Check(!PfDone("nodmg_boss"), "L2: a boss defeated after a hit in the fight is not FLAWLESS BOSS");
        RunLedger.DevSet(x => x.ch.hitsAtBoss = x.ch.hits); // 次のボス戦の始まり(被弾なし)
        ChallengeTracker.OnBossDefeated("Wild/Wolf");
        Check(PfDone("nodmg_boss"), "L2: FLAWLESS BOSS");
        // 死神
        Bm.DebugMarkDeathSpawned();
        yield return PfFrames(3);
        Check(PfDone("reaper_meet"), "L2: REAPER SIGHTED");
        ChallengeTracker.OnRunCommitted(RunLedger.Current);
        Check(PfDone("reaper_escape"), "L2: REAPER ESCAPE (success after the reaper appeared)");

        // 速さ(ワープ = 記録対象外の印を消して「走った」扱い)
        WarpTo(5000f); RunLedger.DevClearDebug();
        yield return new WaitForSecondsRealtime(1.0f);
        float baseKmh = Mathf.Max(1f, pc.CurrentRunKmh); // カードの速さも入った今の速さを基準に、狙った速さへ
        Check(!PfDone("speed_130"), $"L2: no speed challenge at {baseKmh:F1} km/h");
        PlayerController.DebugSpeedScale = 140f / baseKmh;
        yield return new WaitForSecondsRealtime(1.0f);
        L($"[L2] speed {pc.CurrentRunKmh:F1} km/h (base {baseKmh:F1}, scale {PlayerController.DebugSpeedScale:F2})");
        Check(PfDone("speed_130") && !PfDone("speed_150"), $"L2: 130 KM/H only ({pc.CurrentRunKmh:F1})");
        PlayerController.DebugSpeedScale = 185f / baseKmh;
        yield return new WaitForSecondsRealtime(1.0f);
        L($"[L2] speed {pc.CurrentRunKmh:F1} km/h (scale {PlayerController.DebugSpeedScale:F2})");
        Check(PfDone("speed_150") && PfDone("speed_170"), "L2: 150 / 170 KM/H");
        PlayerController.DebugSpeedScale = 1f;
        Check(ProfileStore.TopSpeedKmh >= 170f, $"L2: the profile's top speed is kept ({ProfileStore.TopSpeedKmh:F1})");
        Check(PfDone("dist_10k") && !PfDone("dist_100k"), "L2: REACH 100 KM not yet");
        RunLedger.DevSet(x => x.maxReached = 100020);
        yield return PfFrames(3);
        Check(PfDone("dist_100k"), "L2: REACH 100 KM");

        // 報酬の合計 = 達成した物の MILE の合計(ランの MILE はまだ持ち帰っていない)
        Check(PfMile() - mileStart == PfSumDoneMile(), $"L2: MILE from challenges = the sum of done challenges ({PfMile() - mileStart} vs {PfSumDoneMile()})");
        L($"[L2] done {ChallengeStore.DoneCount}/{ChallengeStore.Total}, toasts shown {ChallengeToast.ShownCount - toastStart} + pending {ChallengeToast.Pending}");
        yield return EndRun();
        if (hsa != null) hsa.ReloadPrefs(); // 設定へ戻す(オート ON)

        // ---- L3 新しいラン: ラン内の数えだけ戻る / オートが一度でも ON なら記録
        yield return BeginRun("swordsman", "wasteland_road");
        yield return PfFrames(3);
        r = RunLedger.Current;
        if (r.debugUsed) RunLedger.DevClearDebug();
        Check(!r.ch.hit && r.ch.breaks == 0 && r.ch.maxCombo == 0, "L3: run-only counters reset in a new run");
        Check(ChallengeStore.Counter("breaks") >= 50 && ChallengeStore.Counter("air_kills") >= 100, "L3: lifetime counters are kept");
        Check(hsa == null || !hsa.assistEnabled || r.ch.autoSeen, "L3: auto ON at the start is seen");
        if (hsa != null) { hsa.assistEnabled = false; yield return PfFrames(2); }
        Check(r.ch.autoSeen, "L3: turning auto OFF later does not clear 'auto was used' for this run");

        // ---- L4 CONTINUE(中断 → 再開)で、ラン内の数えがそのまま
        RunLedger.DevSet(x => { x.walked = 3000; });
        gm.TryDamagePlayer(true, "QaProfile", CombatScale.PlayerHit);
        MapCardRules.OnBossBreak(); MapCardRules.OnBossBreak();
        yield return PfFrames(2);
        typeof(GameManager).GetMethod("SaveCheckpoint", NP).Invoke(gm, null);
        string runId = r.runId;
        yield return ResumeGoHome();
        yield return ResumeContinue("pf");
        yield return PfFrames(3);
        r = RunLedger.Current;
        Check(r != null && r.runId == runId && r.ch != null && r.ch.v == 1, "L4: CONTINUE keeps the same run and its counters");
        Check(r != null && r.ch.hit && r.ch.autoSeen, $"L4: after CONTINUE the hit / auto flags are kept (hit {r?.ch?.hit} auto {r?.ch?.autoSeen})");
        L($"[L4] after CONTINUE breaks {r?.ch?.breaks} walked {r?.walked:F0}");
        yield return EndRun();
        if (hsa != null) hsa.ReloadPrefs();

        // ---- L5 既存の記録からの復元(更新後の初回起動の処理を、記録を作ってから呼ぶ)
        ChallengeStore.DevResetAll();
        GameManager.WriteStageBest("natural_cave", 52000);
        ProgressStats.DevSetLifetime(1000500);
        ProgressStats.DevSetMet(ReaperSister.Eldest, true);
        int mR = PfMile(), tR = ChallengeToast.ShownCount + ChallengeToast.Pending;
        string rep = ChallengeStore.EnsureInitialized();
        L("[L5] " + rep);
        Check(PfDone("dist_10k") && PfDone("dist_50k") && !PfDone("dist_100k") && PfDone("dist_total") && PfDone("reaper_meet"), "L5: restored from records: 10km/50km (cave BEST 52km), 1,000,000m total, reaper met; not 100km");
        Check(ChallengeStore.WasRestored("dist_50k"), "L5: marked as restored");
        Check(ChallengeStore.DoneCount == 4, $"L5: nothing else is restored (no-damage/combo/auto/boss have no record) ({ChallengeStore.DoneCount})");
        Check(PfMile() - mR == 30 + 60 + 100 + 30, $"L5: restored rewards once ({PfMile() - mR})");
        Check(ChallengeToast.ShownCount + ChallengeToast.Pending == tR, "L5: no notice spam (no toasts for restored ones)");
        Check(NoticeQueue.Pending > 0 || NoticeQueue.CurrentId == "challenges_restored" || NoticeQueue.QaAutoAcknowledge, "L5: one summary notice at home");
        int mR2 = PfMile();
        Check(ChallengeStore.EnsureInitialized() == "" && PfMile() == mR2, "L5: the restore runs only once");

        // ---- L6 装備の決まり
        Check(ProfileStore.Equip(0, "dist_10k") == ProfileStore.EquipResult.Ok, "L6: equip an earned badge");
        ProfileStore.Equip(1, "dist_10k");
        Check(ProfileStore.Slot(0) == "" && ProfileStore.Slot(1) == "dist_10k", "L6: the same badge moves instead of being duplicated");
        Check(ProfileStore.Equip(2, "combo_100") == ProfileStore.EquipResult.NotOwned, "L6: an unearned badge cannot be equipped");
        ProfileStore.Equip(0, "dist_50k"); ProfileStore.Equip(2, "dist_total");
        ProfileStore.Reload();
        Check(ProfileStore.Slot(0) == "dist_50k" && ProfileStore.Slot(1) == "dist_10k" && ProfileStore.Slot(2) == "dist_total", "L6: three different badges, kept after reloading");
        SaveStore.SetString(ProfileStore.BadgesKey, "dist_10k,dist_10k,bogus"); ProfileStore.Reload();
        Check(ProfileStore.Slot(0) == "dist_10k" && ProfileStore.Slot(1) == "" && ProfileStore.Slot(2) == "", "L6: broken saved slots (duplicate/unknown) are dropped on load");
        SaveStore.SetString(ProfileStore.BadgesKey, "combo_1000,,"); ProfileStore.Reload();
        Check(ProfileStore.Slot(0) == "", "L6: an unearned badge in the save is not shown as equipped");
        Check(ProfileStore.SetReason(9) && ProfileStore.ReasonId == 9 && !ProfileStore.SetReason(99) && ProfileStore.ReasonId == 9, "L6: the reason is saved by ID (unknown IDs refused)");
        var card = PlayerProfileCard.FromLocal();
        var back = PlayerProfileCard.FromJson(card.ToJson());
        Check(back != null && back.reasonId == 9 && back.badgeCount == card.badgeCount && back.badges.Length == 3 && System.Math.Abs(back.totalMeters - ProgressStats.LifetimeDistance) < 0.01, "L6: the profile data survives JSON (for the future ranking view)");
        L("[L6] card " + card.ToJson());
        // 戦績が実データと一致
        Check(card.bestMeters >= 52000 - 0.5 && System.Math.Abs(card.bestMeters - PlayerProfileCard.BestOverall(gm)) < 0.01 && card.characterId == gm.SelectedCharacterId, $"L6: BEST (best of the map BESTs) / character match the saved records ({card.bestMeters:F0})");
        Check(card.cave100 == false && System.Math.Abs(PlayerProfileCard.MapReach("natural_cave") - 52000) < 0.5, "L6: map reach comes from the saved BEST");

        // ---- L7 画面を開いて閉じる(例外なし)
        ProfilePanel.OpenStatic();
        yield return new WaitForSecondsRealtime(0.4f);
        Check(ProfilePanel.IsOpen && UiInputGate.ModalOpen, "L7: the profile is open and blocks the home behind it");
        ProfilePanel.DebugGoPage(ProfilePanel.Page.Badges); yield return new WaitForSecondsRealtime(0.3f);
        ProfilePanel.DebugGoPage(ProfilePanel.Page.Reason); yield return new WaitForSecondsRealtime(0.3f);
        Check(ProfilePanel.HandleBack() && ProfilePanel.CurrentPage == ProfilePanel.Page.Profile, "L7: Back from a sub page returns to the profile");
        Check(ProfilePanel.HandleBack() && !ProfilePanel.IsOpen, "L7: Back on the profile closes it");
        // 定型コミュニケーション(オフラインでは自分の画面だけ)
        int qr0 = QuickChat.Received;
        Check(QuickChat.Send(1) && QuickChat.Received == qr0 + 1, "L7: quick chat shows locally when offline");
        Check(!QuickChat.Send(2), "L7: quick chat is rate limited");
        Check(!QuickChat.Send(200), "L7: unknown phrase ID refused");
        QuickChat.DebugInject(2, 9);
        yield return new WaitForSecondsRealtime(0.3f);
        yield return ShotW("pf_quickchat");
    }

    IEnumerator PfJump()
    {
        pc.debugInjectFlick = PlayerController.FlickDirection.Up;
        float w = 0f;
        while (pc.IsGrounded && w < 1.5f) { yield return null; w += Time.unscaledDeltaTime; }
        pc.debugInjectFlick = null;
        yield return null;
    }

    // ===================================================================== persist(-fileSave で2回)
    IEnumerator PfPersist(int phase)
    {
        string mark = System.IO.Path.Combine(outDir, "pf_persist.txt");
        if (phase == 1)
        {
            PfClean();
            int m0 = PfMile();
            ChallengeStore.Complete("combo_100"); ChallengeStore.Complete("air_kill"); ChallengeStore.Complete("speed_130");
            ProfileStore.SetReason(11);
            ProfileStore.Equip(0, "air_kill"); ProfileStore.Equip(2, "combo_100");
            ChallengeStore.AddCounter("breaks", 7);
            SaveStore.Save();
            System.IO.File.WriteAllText(mark, $"{PfMile()}\n{ChallengeStore.DoneCount}\n{m0}");
            L($"[P1] saved: done {ChallengeStore.DoneCount}, MILE {m0} -> {PfMile()}, reason 11, slots {string.Join(",", ProfileStore.EquippedSlots)}");
            Check(PfMile() - m0 == 30 + 20 + 30, "P1: rewards granted once");
            yield break;
        }
        var lines = System.IO.File.Exists(mark) ? System.IO.File.ReadAllLines(mark) : new string[0];
        Check(lines.Length >= 2, "P2: phase 1 marker exists");
        int mile1 = lines.Length > 0 ? int.Parse(lines[0]) : -1;
        L($"[P2] boot: {SaveSystem.LastBoot?.report}");
        Check(ChallengeStore.IsDone("combo_100") && ChallengeStore.IsDone("air_kill") && ChallengeStore.IsDone("speed_130") && ChallengeStore.DoneCount == 3, $"P2: completed challenges survive a restart ({ChallengeStore.DoneCount})");
        Check(ProfileStore.ReasonId == 11, $"P2: the reason survives a restart ({ProfileStore.ReasonId})");
        Check(ProfileStore.Slot(0) == "air_kill" && ProfileStore.Slot(1) == "" && ProfileStore.Slot(2) == "combo_100", $"P2: equipped badges survive a restart ({string.Join(",", ProfileStore.EquippedSlots)})");
        Check(ChallengeStore.Counter("breaks") == 7, "P2: lifetime counters survive a restart");
        Check(PfMile() == mile1, $"P2: no reward is granted again on restart (MILE {mile1} -> {PfMile()})");
        Check(ChallengeToast.ShownCount == 0, "P2: no completion notice on boot");
        ProfilePanel.OpenStatic();
        yield return new WaitForSecondsRealtime(0.6f);
        yield return ShotW("pf_persist_profile");
        ProfilePanel.CloseStatic();
    }

    // ===================================================================== 撮影
    void PfShotData()
    {
        PfClean();
        string[] ids = { "dist_10k", "dist_50k", "dist_100k", "dist_total", "nodmg_10k", "combo_100", "combo_500", "speed_130", "air_kill", "break_run5", "boss_garmud", "boss_rush", "reaper_meet", "sp_starter" };
        foreach (var id in ids) ChallengeStore.Complete(id, restored: true);
        ProfileStore.SetReason(9);
        ProfileStore.Equip(0, "boss_garmud"); ProfileStore.Equip(1, "reaper_meet"); ProfileStore.Equip(2, "dist_50k");
        if (string.IsNullOrEmpty(Codename.Current)) Codename.Set("MILE RUNNER");
        ProgressStats.DevSetLifetime(1234567);
        GameManager.WriteStageBest("wasteland_road", 100350); GameManager.WriteStageBest("natural_cave", 61200);
        ChallengeStore.DevSetCounter("air_kills", 37); ChallengeStore.DevSetCounter("breaks", 18);
        SaveStore.SetString(ProfileStore.TopSpeedKey, "142.6"); ProfileStore.Reload();
        ChallengeToast.ClearAll();
    }

    IEnumerator PfRes(bool land)
    {
        int W = land ? 1280 : 540, H = land ? 720 : 960;
        Screen.SetResolution(W, H, FullScreenMode.Windowed);
        float w = 0f; while ((Screen.width != W || Screen.height != H) && w < 3f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f);
        L($"[res] {Screen.width}x{Screen.height}");
    }

    IEnumerator PfShots()
    {
        PfShotData();
        // 戦績の値がゲームの持つ値と同じか(撮影のデータで)
        var card = PlayerProfileCard.FromLocal();
        Check(card.wasteland100 && !card.cave100 && card.topSpeedKmh > 142f && card.badgeCount == 14 && card.reasonId == 9 && System.Math.Abs(card.bestMeters - 100350) < 0.5, $"S: profile values from the saved records ({card.ToJson()})");
        foreach (bool land in new[] { true, false })
        {
            string o = land ? "land" : "port";
            yield return PfRes(land);
            yield return ShotW($"pf_{o}_home");
            ProfilePanel.OpenStatic();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return ShotW($"pf_{o}_profile");
            if (!land) { ProfilePanel.DebugScroll(400f); yield return new WaitForSecondsRealtime(0.3f); yield return ShotW($"pf_{o}_profile_scrolled"); ProfilePanel.DebugScroll(0f); }
            ProfilePanel.DebugGoPage(ProfilePanel.Page.Reason);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return ShotW($"pf_{o}_reason");
            ProfilePanel.DebugGoPage(ProfilePanel.Page.Badges);
            ProfilePanel.DebugSelectSlot(1);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return ShotW($"pf_{o}_badges");
            ProfilePanel.DebugScroll(land ? 700f : 1400f); yield return new WaitForSecondsRealtime(0.3f);
            yield return ShotW($"pf_{o}_badges_scrolled");
            ProfilePanel.DebugScroll(0f);
            ProfilePanel.DebugFilter(2); ProfilePanel.DebugLockedNote(); yield return new WaitForSecondsRealtime(0.3f);
            yield return ShotW($"pf_{o}_badges_locked");
            ProfilePanel.DebugFilter(1); yield return new WaitForSecondsRealtime(0.3f);
            yield return ShotW($"pf_{o}_badges_done");
            ProfilePanel.DebugFilter(0);
            ProfilePanel.CloseStatic();
            yield return new WaitForSecondsRealtime(0.4f);
            // 達成の通知(ホーム)
            ChallengeToast.Show(ChallengeDefs.Find("combo_1000"));
            yield return new WaitForSecondsRealtime(0.7f);
            yield return ShotW($"pf_{o}_toast_home");
            yield return new WaitForSecondsRealtime(3f);
            // 達成の通知(ラン中)
            yield return BeginRun("swordsman", "wasteland_road");
            if (RunLedger.Current != null && RunLedger.Current.debugUsed) RunLedger.DevClearDebug();
            yield return new WaitForSecondsRealtime(1.5f);
            ChallengeToast.Show(ChallengeDefs.Find("nodmg_30k"));
            yield return new WaitForSecondsRealtime(0.7f);
            yield return ShotW($"pf_{o}_toast_run");
            yield return EndRun();
        }
        // バッヂの絵(全部、獲得/未獲得)
        yield return PfRes(true);
        PfBadgeSheet();
    }

    // バッヂの仮の絵を1枚の表に(エステルへ見せる用)
    void PfBadgeSheet()
    {
        int n = ChallengeDefs.All.Length, cell = 120, cols = 9, rows = (n + cols - 1) / cols;
        var sheet = new Texture2D(cols * cell, rows * cell * 2, TextureFormat.RGBA32, false);
        var bg = new Color32[sheet.width * sheet.height]; for (int i = 0; i < bg.Length; i++) bg[i] = new Color32(24, 26, 40, 255);
        sheet.SetPixels32(bg);
        for (int i = 0; i < n; i++)
        {
            var d = ChallengeDefs.All[i];
            for (int v = 0; v < 2; v++)
            {
                var t = BadgeArt.Get(d, v == 0);
                int x0 = (i % cols) * cell + (cell - t.width) / 2, y0 = sheet.height - ((i / cols) + v * rows + 1) * cell + (cell - t.height) / 2;
                var px = t.GetPixels();
                for (int y = 0; y < t.height; y++) for (int x = 0; x < t.width; x++)
                {
                    var c = px[y * t.width + x]; if (c.a <= 0.01f) continue;
                    var b = sheet.GetPixel(x0 + x, y0 + y);
                    sheet.SetPixel(x0 + x, y0 + y, Color.Lerp(b, new Color(c.r, c.g, c.b, 1f), c.a));
                }
            }
        }
        sheet.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, "pf_badges_sheet.png"), sheet.EncodeToPNG());
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "pf_badges_sheet.txt"), string.Join("\n", ChallengeDefs.All.Select((d, i) => $"{i}\t{d.id}\t{d.badge}")));
        L($"[S] badge sheet {n} badges (top: earned / bottom: locked), generated {BadgeArt.Generated}");
    }

    // ===================================================================== 全言語
    IEnumerator PfLangs()
    {
        PfShotData();
        string only = Arg("-pfLangs", "");
        var langs = Loc.Languages.Select(l => l.code).Where(c => only == "" || only.Split(',').Contains(c)).ToList();
        TextFit.Issues.Clear();
        foreach (bool land in new[] { true, false })
        {
            string o = land ? "land" : "port";
            yield return PfRes(land);
            foreach (var code in langs)
            {
                Loc.Set(code, false);
                yield return new WaitForSecondsRealtime(0.2f);
                ProfilePanel.OpenStatic();
                yield return new WaitForSecondsRealtime(0.45f);
                yield return ShotW($"lang_{o}_profile_{code}");
                ProfilePanel.DebugGoPage(ProfilePanel.Page.Badges); ProfilePanel.DebugSelectSlot(1); ProfilePanel.DebugLockedNote();
                yield return new WaitForSecondsRealtime(0.35f);
                yield return ShotW($"lang_{o}_badges_{code}");
                ProfilePanel.DebugGoPage(ProfilePanel.Page.Reason);
                yield return new WaitForSecondsRealtime(0.35f);
                yield return ShotW($"lang_{o}_reason_{code}");
                ProfilePanel.CloseStatic();
                ChallengeToast.Show(ChallengeDefs.Find("break_total50"));
                yield return new WaitForSecondsRealtime(0.6f);
                yield return ShotW($"lang_{o}_toast_{code}");
                ChallengeToast.ClearAll();
                yield return new WaitForSecondsRealtime(0.2f);
            }
        }
        Loc.Set("ja", false);
        // 見切れの記録(このテストの画面に出た文だけ)
        var sb =new System.Text.StringBuilder("kind\tfont\tscreen\ttext\n");
        int overflow = 0;
        foreach (var kv in TextFit.Issues)
        {
            var i = kv.Value;
            sb.Append($"{i.kind}\t{i.fontFrom}->{i.fontTo}\t{kv.Key.Split('|').Last()}\t{i.text.Replace("\n", "\\n")}\n");
            if (i.overflow) overflow++;
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "pf_textfit.tsv"), sb.ToString());
        L($"[langs] {langs.Count} languages, text fit issues {TextFit.Issues.Count} (overflow {overflow}) -> pf_textfit.tsv");
        if (overflow > 0) Warn($"{overflow} text(s) still overflow after shrinking/wrapping (see pf_textfit.tsv)");
    }
}
#endif
