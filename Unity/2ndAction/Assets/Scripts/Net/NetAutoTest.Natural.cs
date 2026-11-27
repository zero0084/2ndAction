#if UNITY_EDITOR || DEVELOPMENT_BUILD
// 開発版(Editor / Development Build)だけ(NetAutoTest 本体と同じ)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// 2026-11-27 4人マルチの通常条件の通し試験(マスター依頼)。
//  -netAutoNatural <1..4>     この端末の「人」の性格(キャラ/カードの選び方/迷う時間/反応の遅さ/一時停止/脱出の予定)。
//                              攻撃力/HP/速度/報酬の上乗せ・無敵・ワープ・強制撃破・技の直接呼び出しは使わない(KeepAlive/Bot を通らない)。
//                              操作は本物の入力の経路: 仮想のゲームパッド(IInputSource)を GameInput に足して、PC のパッド/キーと同じ所から入る
//                              (前攻撃/上/下/ジャンプ/長押し(脱出・復活)/一時停止/決定)。カードは画面のカードのタップと同じ OnCardClicked。
//                              自動操作補助(HighSpeedAssist)はこの保存の設定のまま(新しい保存は ON = 普通のプレイヤーと同じ)。
//  -netAutoNatSeed 1           この保存が初めてなら試験の前の準備をする(キャラの解放/選択・ガチャ/おすすめ編成)。準備は通常の関数を通す
//  -netAutoNatSnapOnly 1       起動してホームで保存の照合用の値を出して終わる(再起動後の確認)
//  -netAutoNatMaxMin 75        上限(分)。上限で止めた時は SUMMARY の reason に出す(自然な終わりと区別)
//  -netAutoNatEscapeAt m[,m2]  この距離(m)を過ぎたら脱出を溜める(1回目のラン, 2回目のラン)。溜める→離す→溜め直す の順
//  -netAutoNatPauseAt s -netAutoNatPauseFor s   ラン開始から s 秒で一時停止を開いて閉じる(パッドの START と同じ)
//  -netAutoNatHold lv,boss     カードを選ぶまで迷う秒(レベルアップ, ボスの報酬)
//  -netAutoNatPick i|rand      何番目のカードを選ぶか
//  -netAutoNatHomeDelay s      RESULT を読んでから「ホームへ戻る」を押すまで
//  -netAutoNatRuns 2           (HOST) 全員がホームへ戻った後、同じ部屋でもう1回出発する
//  -netAutoNatLeave boss|choice|down|result|time:N|home  その状態になったら部屋を出る(途中退出。プロセスの強制終了=切断はスクリプト側)
//  -netAutoNatBg s:dur         s 秒で窓のフォーカスを失った/裏へ回った時の処理(OnApplicationPause/Focus と同じ経路)を dur 秒
//  -netAutoNatChar id          キャラ(既定は性格ごと)
//  -netAutoNatReaperMin n      (HOST, VERSUS) 部屋の死神の乱入時間(部屋の設定と同じ値 5/10/20/30/0)
// 記録: NAT(0.5秒ごとの自分と見えている他人)/ NATENT(敵の位置)/ NATEV(出来事)/ NATSNAP(保存の値)/ NATRESULT。判定は Tools/net/nat4.py
public partial class NetAutoTest
{
    // ----------------------------------------------------------------- 仮想のパッド
    sealed class QaPadSource : IInputSource
    {
        public readonly bool[] held = new bool[(int)GameAction.Count];
        public string Name => "qa-pad";
        public InputDeviceKind Kind => InputDeviceKind.Gamepad;
        public bool Held(GameAction a) => held[(int)a];
        public bool AnyActivity() { for (int i = 0; i < held.Length; i++) if (held[i]) return true; return false; }
    }
    static QaPadSource natPad;
    readonly int[] natPulse = new int[(int)GameAction.Count];   // 残りの押すフレーム(押した後は同じ数だけ離す)
    readonly int[] natRelease = new int[(int)GameAction.Count];

    // ----------------------------------------------------------------- 設定
    int natProfile; bool natSeed, natSnapOnly; float natMaxMin = 75f;
    float[] natEscapeAt = { -1f, -1f }; float natPauseAt = -1f, natPauseFor = 20f;
    float natHoldLv = 1f, natHoldBoss = 3f; int natPick = 0; bool natPickRand; string[] natPrefer = new string[0];
    string natDoneDir = ""; // 試験の道具の待ち合わせ(同じ PC の時だけ): 最後のランの後、ホームへ戻った印のファイルを置く
    float natHomeDelay = 3f; int natRuns = 1; string natLeave = ""; float natLeaveTime = -1f;
    float natBgAt = -1f, natBgFor = 0f; string natChar = ""; int natReaperMin = -1;
    float natReact = 0.15f, natAtkGap = 0.32f;
    bool Natural => natProfile > 0;

    bool ParseNaturalArg(string a, string next)
    {
        switch (a)
        {
            case "-netAutoNatural": int.TryParse(next, out natProfile); NatDefaults(); runOverHold = 1e9f; return true;
            case "-netAutoNatSeed": natSeed = next != "0"; return true;
            case "-netAutoNatSnapOnly": natSnapOnly = next != "0"; return true;
            case "-netAutoNatMaxMin": float.TryParse(next, out natMaxMin); return true;
            case "-netAutoNatEscapeAt": { var p = next.Split(','); for (int i = 0; i < p.Length && i < 2; i++) float.TryParse(p[i], out natEscapeAt[i]); return true; }
            case "-netAutoNatPauseAt": float.TryParse(next, out natPauseAt); return true;
            case "-netAutoNatPauseFor": float.TryParse(next, out natPauseFor); return true;
            case "-netAutoNatHold": { var p = next.Split(','); if (p.Length > 0) float.TryParse(p[0], out natHoldLv); if (p.Length > 1) float.TryParse(p[1], out natHoldBoss); return true; }
            case "-netAutoNatPrefer": natPrefer = next.Split(','); return true;
            case "-netAutoShareOverlap": overlapTest = next != "0"; return true;
            case "-netAutoNatPick": if (next == "rand") natPickRand = true; else { natPickRand = false; int.TryParse(next, out natPick); } return true;
            case "-netAutoNatHomeDelay": float.TryParse(next, out natHomeDelay); return true;
            case "-netAutoNatDoneDir": natDoneDir = next; return true;
            case "-netAutoNatRuns": int.TryParse(next, out natRuns); return true;
            case "-netAutoNatLeave": natLeave = next; if (next.StartsWith("time:")) float.TryParse(next.Substring(5), out natLeaveTime); return true;
            case "-netAutoNatBg": { var p = next.Split(':'); if (p.Length == 2) { float.TryParse(p[0], out natBgAt); float.TryParse(p[1], out natBgFor); } return true; }
            case "-netAutoNatChar": natChar = next; return true;
            case "-netAutoNatReaperMin": int.TryParse(next, out natReaperMin); return true;
        }
        return ParseCondArg(a, next); // 2026-11-28: 条件を作る補助試験(NetAutoTest.Cond)
    }

    // 性格(4人で、キャラ・選び方・迷う時間・反応・一時停止を変える。引数で上書きできる)
    void NatDefaults()
    {
        switch (natProfile)
        {
            case 1: natChar = "swordsman"; natPick = 0; natHoldLv = 0.8f; natHoldBoss = 8f; natReact = 0.12f; natAtkGap = 0.30f; natMiss = 0.01f; natJumpLag = 0.03f; natHomeDelay = 4f; break;      // HOST: ボスの報酬は長く迷う
            case 2: natChar = "dual_blade"; natPick = 1; natHoldLv = 5f; natHoldBoss = 10f; natReact = 0.20f; natAtkGap = 0.26f; natMiss = 0.03f; natJumpLag = 0.05f; natHomeDelay = 0.6f; break;   // 迷う人・RESULT をすぐ閉じる
            case 3: natChar = "gunslinger"; natPick = 2; natHoldLv = 1.5f; natHoldBoss = 2f; natReact = 0.15f; natAtkGap = 0.40f; natMiss = 0.02f; natJumpLag = 0.04f; natHomeDelay = 8f; break;   // 遠距離
            default: natChar = "ninja"; natPickRand = true; natHoldLv = 2.5f; natHoldBoss = 4f; natReact = 0.30f; natAtkGap = 0.45f; natMiss = 0.05f; natJumpLag = 0.07f; natHomeDelay = 12f; break; // 反応が遅い
        }
    }

    // ----------------------------------------------------------------- 出発前(準備/スナップショット)
    bool natPreDone, natSeedDone, natSnapDone; float natPreT;
    // true で接続へ進む
    bool NatPreConnect(GameManager gm)
    {
        if (!Natural || natPreDone) return true;
        if (natPad == null) { natPad = new QaPadSource(); GameInput.QaAddSource(natPad); }
        natPreT += Time.unscaledDeltaTime;
        if (gm == null || natPreT < 3f || TitleScreen.Showing) return false; // タイトルは -titleScreen 0 で出さない
        bool safe = FileSaveStore.Active || MemorySaveStore.Active;
        if (!natSnapDone)
        {
            natSnapDone = true;
            L($"NATBOOT profile={natProfile} safeSave={safe} file={FileSaveStore.Active} mem={MemorySaveStore.Active} invincible={gm.InvincibleMode} assist={(HighSpeedAssist.Instance != null && HighSpeedAssist.Instance.assistEnabled)} tutorialReward={TutorialClearReward.State}");
            NatSnap("boot");
        }
        if (!safe) { Finish("NAT refused: not a dedicated test save"); return false; }
        if (natSnapOnly) { NatSnap("restart"); Finish("NAT snapshot only"); return false; }
        if (natSeed && !natSeedDone)
        {
            natSeedDone = true;
            if (SaveStore.GetInt("QA.NatSeedV1", 0) == 0) NatSeedSave(gm);
            else L($"NATSEED already seeded (profile {SaveStore.GetInt("QA.NatSeedV1", 0)})");
        }
        // キャラはこの保存の選択(準備で選んだ物)。起動の処理が選び直すので、出発までの間は確かめるだけ
        if (role == "HOST" && natReaperMin >= 0 && NetRunLauncher.SelectedReaperMinutes != natReaperMin) { NetRunLauncher.SelectedReaperMinutes = natReaperMin; L($"NATEV reaperMin set {natReaperMin} (room rule)"); }
        NatSnap("before");
        natPreDone = true;
        return true;
    }

    // 試験の前の準備: キャラの解放(試験の前提)→ キャラ選択の SELECT と同じ関数 → ガチャを通常の1回の処理で → デッキ編集の「おすすめ編成」と同じ関数
    void NatSeedSave(GameManager gm)
    {
        string ch = string.IsNullOrEmpty(natChar) ? "swordsman" : natChar;
        string chars = SaveStore.GetString(UnlockRules.CharsKey, "");
        if (!(("," + chars + ",").Contains("," + ch + ","))) SaveStore.SetString(UnlockRules.CharsKey, string.IsNullOrEmpty(chars) ? (ch == "swordsman" ? ch : "swordsman," + ch) : chars + "," + ch);
        UnlockRules.Reload();
        gm.SetSelectedCharacter(ch);
        int pulls = 6 + natProfile * 3; // 人ごとに所持の数を変える(9/12/15/18 回)
        var pullM = typeof(GameManager).GetMethod("GachaPullOnce", BindingFlags.Instance | BindingFlags.NonPublic);
        var setMile = typeof(GameManager).GetProperty("TotalOwnedMile").GetSetMethod(true);
        int mile0 = gm.TotalOwnedMile;
        setMile.Invoke(gm, new object[] { mile0 + pulls * GameManager.GachaCostMile });
        SaveStore.SetInt(GameManager.TotalMileKey, gm.TotalOwnedMile);
        int ok = 0;
        for (int i = 0; i < pulls; i++) if (pullM != null && (bool)pullM.Invoke(gm, null)) ok++;
        var deck = CardDatabase.BuildRecommendedDeck(GameManager.DeckCapacity);
        gm.SetDeck(deck);
        // 人ごとにデッキを変える: おすすめ編成の後ろから profile 枚を外し、ガチャで出た(デッキに無い)カードを デッキ編集の追加と同じ AddToDeck で入れる
        var cur = new List<string>(gm.DeckCards);
        for (int i = 0; i < natProfile && cur.Count > 0; i++) { gm.RemoveFromDeck(cur[cur.Count - 1]); cur.RemoveAt(cur.Count - 1); }
        int added = 0;
        foreach (var st in CardInventory.Stacks) { if (added >= natProfile + 1) break; if (gm.DeckCards.Contains(st.cardId)) continue; if (gm.AddToDeck(st.cardId)) added++; }
        deck = new List<string>(gm.DeckCards);
        SaveStore.SetInt("QA.NatSeedV1", natProfile);
        SaveStore.Save();
        L($"NATSEED profile={natProfile} char={ch} selected={gm.SelectedCharacterId} gacha={ok}/{pulls} mile {mile0}->{gm.TotalOwnedMile} deck={deck.Count} [{string.Join(",", deck)}]");
    }

    // 保存の値(照合用)。ホーム/再起動後/ラン後で同じ形
    void NatSnap(string tag)
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        var owned = new List<string>();
        int ownedCount = 0;
        foreach (var s in CardInventory.Stacks) { owned.Add($"{s.cardId}:{s.level}x{s.count}"); ownedCount += s.count; }
        owned.Sort(StringComparer.Ordinal);
        string ownedHash = Hash(string.Join("|", owned));
        var deck = new List<string>(gm.DeckCards);
        var best = new List<string>();
        foreach (var st in UnlockRules.NormalMaps) best.Add($"{st}={SaveStore.GetString(SaveKeys.StageBestPrefix + st, "0")}");
        L($"NATSNAP tag={tag} profile={natProfile} mile={gm.TotalOwnedMile} savedMile={SaveStore.GetInt(GameManager.TotalMileKey, -1)} char={gm.SelectedCharacterId} deck={deck.Count}:{Hash(string.Join(",", deck))} owned={ownedCount}/{owned.Count}:{ownedHash} gacha={gm.GachaPulls} " +
          $"lifetime={SaveStore.GetString(SaveKeys.LifetimeDistance, "0")} best=[{string.Join(" ", best)}] chars=[{SaveStore.GetString(UnlockRules.CharsKey, "")}] stages=[{SaveStore.GetString(UnlockRules.StagesKey, "")}] tutorialReward={TutorialClearReward.State} activeRun={RunCheckpoint.HasActiveRun} netActive={NetSession.IsActive}");
    }

    static string Hash(string s)
    {
        unchecked { uint h = 2166136261; foreach (char c in s) { h ^= c; h *= 16777619; } return h.ToString("x8"); }
    }

    // ----------------------------------------------------------------- ラン中
    float natAtkCool, natSeenAt = -1f, natJumpCool, natChoiceSeenAt = -1f; int natChoiceClicks; bool natChoiceLogged;
    int natRunIndex; bool natResultLogged, natHomePressed, natHomeReached; float natResultAt = -1f, natHomeAt = -1f, natHomeConfirmNext;
    int natEscPhase; float natEscT; bool natEscLogged, natEscDone;
    bool natPauseOpened, natPauseClosed; float natPauseBase = -1f;
    bool natWasDown; float natDownAt = -1f; bool natLeft; float natNextLog, natNextEnt; float natRunStartUnscaled = -1f;
    bool natBgDone, natBgOn; int natLastLv = 1; int natLastBossKills; int natLastChoices;
    readonly System.Random natRng = new System.Random(12345);
    static readonly FieldInfo EscTimerF = typeof(PlayerController).GetField("escapeHoldTimer", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo PauseF = typeof(GameManager).GetField("showPauseMenu", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    static readonly FieldInfo ChoiceKindF = typeof(GameManager).GetField("pendingChoiceKind", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    void NatPress(GameAction a, int frames = 2) { if (natRelease[(int)a] <= 0 && natPulse[(int)a] <= 0) natPulse[(int)a] = frames; }

    void NatPadFrame(bool holdHold)
    {
        if (natPad == null) return;
        for (int i = 0; i < natPad.held.Length; i++)
        {
            if (natPulse[i] > 0) { natPad.held[i] = true; if (--natPulse[i] == 0) natRelease[i] = 2; }
            else { natPad.held[i] = false; if (natRelease[i] > 0) natRelease[i]--; }
        }
        natPad.held[(int)GameAction.Hold] = holdHold;
    }

    void NatTick(GameManager gm)
    {
        if (overlapTest && NetMatch.QaOutHook == null) OverlapSetup(); // 補助試験: 退出と撃破が重なる場合
        OverlapTick();
        CondTick(gm); // 2026-11-28: 条件を作る補助試験
        var pc = PlayerController.Instance;
        bool hold = false;
        if (gm == null) { NatPadFrame(false); return; }
        // お知らせの板(解放の知らせ等)は人と同じく「わかった」を押して閉じる(パッドの決定)。練習の誘いの板は押さずに記録だけ
        if (NoticeQueue.Open && Time.unscaledTime >= natNoticeNext) { natNoticeNext = Time.unscaledTime + 1.5f; L($"NATEV notice confirm id={NoticeQueue.CurrentId} t={runTime:F1} started={gm.HasStarted}"); GameInput.Inject(GameAction.Confirm); }
        // HOST との接続が切れた案内(ラン中は止まる): 人と同じく少し読んでから「このまま走る」
        if (NetSession.Instance != null && NetSession.Instance.ConnectionLostPending && gm.HasStarted && !gm.IsGameOver)
        {
            if (natConnLostAt < 0f) { natConnLostAt = Time.unscaledTime; L($"NATEV connection lost banner t={runTime:F1} paused={NetDebugUI.ConnLostPausedNow} ts={Time.timeScale:F2}"); }
            else if (Time.unscaledTime - natConnLostAt > 2f) { L($"NATEV connection lost: continue t={runTime:F1} paused={NetDebugUI.ConnLostPausedNow} ts={Time.timeScale:F2}"); NetDebugUI.ContinueAfterConnectionLost(); natConnLostAt = -1f; }
        }
        if (UiInputGate.ModalOpen && !NoticeQueue.Open && Time.unscaledTime >= natModalLogNext) { natModalLogNext = Time.unscaledTime + 5f; L($"NATEV modal open (not a notice) t={runTime:F1} guide={FirstRunGuide.Open} settings={SettingsPanel.IsVisible} tutorial={TutorialRun.PanelOpen} transfer={SaveTransferUi.IsOpen} loading={LoadingScreen.BlocksInput}"); }
        if (natRunStartUnscaled < 0f && gm.HasStarted) natRunStartUnscaled = Time.unscaledTime;
        float tRun = natRunStartUnscaled < 0f ? 0f : Time.unscaledTime - natRunStartUnscaled;
        if (runTime > natMaxMin * 60f) { NatSnap("limit"); Finish($"NAT time limit {natMaxMin:F0} min (not a natural end)"); return; }

        // 途中退出(状態で)
        if (!natLeft && !string.IsNullOrEmpty(natLeave) && NatLeaveNow(gm, pc, tRun))
        {
            natLeft = true;
            L($"NATEV leave on={natLeave} t={runTime:F1} state={NatState(gm, pc)} dist={(pc != null ? pc.DistanceExact : 0):F1} runMile={gm.RunMile} wallet={gm.TotalOwnedMile}");
            NetSession.Instance.Leave();
            natLeaveAt = runTime;
        }
        if (natLeft && runTime - natLeaveAt > 25f) { NatSnap("afterLeave"); Finish("NAT left (scenario)"); return; }

        if (gm.IsGameOver) { NatResult(gm); NatPadFrame(false); NatLog(gm, pc); return; }
        if (!gm.HasStarted || pc == null)
        {
            // ホーム(ラン後)
            if (natResultLogged && natHomeTries > 0 && !natHomeReached && !gm.HasStarted && !TitleScreen.Showing) NatAtHome(gm); // 「ホームへ戻る」の後、ホームの画面(シーンの読み直しの後)
            NatLog(gm, pc);
            if (natHomeReached) NatHomeTick(gm);
            NatPadFrame(false); return;
        }
        if (natHomeReached && gm.HasStarted) NatNewRunStarted(gm);

        // 窓が裏へ(フォーカスを失った)
        if (natBgAt > 0f && !natBgDone && tRun >= natBgAt) { natBgDone = natBgOn = true; L($"NATEV background on t={runTime:F1}"); gm.SendMessage("OnApplicationFocus", false, SendMessageOptions.DontRequireReceiver); gm.SendMessage("OnApplicationPause", true, SendMessageOptions.DontRequireReceiver); }
        if (natBgOn && tRun >= natBgAt + natBgFor) { natBgOn = false; L($"NATEV background off t={runTime:F1}"); gm.SendMessage("OnApplicationPause", false, SendMessageOptions.DontRequireReceiver); gm.SendMessage("OnApplicationFocus", true, SendMessageOptions.DontRequireReceiver); }

        // 一時停止(パッドの START)。マルチは本人だけ止まる
        bool paused = PauseF != null && (bool)PauseF.GetValue(gm);
        if (natPauseAt > 0f && natRunIndex == 0)
        {
            if (!natPauseOpened && tRun >= natPauseAt && !gm.IsRewardSequenceRunning && !gm.IsLocalChoiceOpen && !pc.NetIsDowned) { natPauseOpened = true; natPauseBase = tRun; NatPress(GameAction.Pause); L($"NATEV pause press t={runTime:F1} dist={pc.DistanceExact:F1}"); }
            if (natPauseOpened && !natPauseClosed && tRun >= natPauseBase + natPauseFor) { natPauseClosed = true; NatPress(GameAction.Pause); L($"NATEV unpause press t={runTime:F1} paused={paused}"); }
        }

        // カードの選択(画面のカードのタップと同じ)
        NatChoice(gm);

        bool down = pc.NetIsDowned;
        if (down && !natWasDown) { natDownAt = runTime; L($"NATEV down t={runTime:F1} dist={pc.DistanceExact:F1} hp={gm.Lives} mode={NetMatch.Mode}"); }
        if (!down && natWasDown) L($"NATEV revived t={runTime:F1} after={runTime - natDownAt:F1}s hp={gm.Lives}/{gm.MaxLives}");
        natWasDown = down;

        if (down)
        {
            // CO-OP の復活: 本人の長押し(パッドの BACK = 長押しの操作)。反応の遅さの分だけ待ってから押し続ける
            hold = NetMatch.Mode == MultiplayerGameMode.Coop && runTime - natDownAt > 0.8f + natReact * 3f && !condNoRevive; // 補助試験: 救助を押さずに DOWN のまま待つ
        }
        else if (!paused && !gm.IsRewardSequenceRunning)
        {
            hold = NatEscape(gm, pc);
            if (!hold) NatFight(pc);
        }
        NatPadFrame(hold);
        NatLog(gm, pc);
        if (Time.unscaledTime >= natDiagNext)
        {
            natDiagNext = Time.unscaledTime + 2f;
            L($"NATDIAG t={runTime:F1} decisions={natDecisions} pressed={natPressed} missed={natMissed} padFlicks={pc.PadFlicks} lastPad={pc.LastPadFlick} menu={PadNav.MenuActive} modal={UiInputGate.ModalOpen} src={GameInput.LastSourceName} planner={(natPlanner != null ? natPlanner.CurrentStatus + "/" + natPlanner.BlockedReason + " plan=" + natPlanner.PlanText + " fail=" + natPlanner.LastFailure : "-")} kmh={pc.CurrentRunKmh:F0} hold={hold} paused={paused}");
        }
        if (gm.Level != natLastLv) { L($"NATEV levelup {natLastLv}->{gm.Level} t={runTime:F1} exp={gm.TotalExpEarned:F0} hp={gm.Lives}/{gm.MaxLives}"); natLastLv = gm.Level; }
        if (gm.BossKillCount != natLastBossKills) { L($"NATEV bossKill local count {natLastBossKills}->{gm.BossKillCount} t={runTime:F1} bossMile={gm.RunBossMile}"); natLastBossKills = gm.BossKillCount; }
    }
    float natLeaveAt, natDiagNext, natNoticeNext, natModalLogNext;

    bool NatLeaveNow(GameManager gm, PlayerController pc, float tRun)
    {
        if (natLeave.StartsWith("time:")) return tRun >= natLeaveTime && gm.HasStarted;
        if (pc == null) return natLeave == "home" && natHomeReached && runTime - natHomeAt > 3f;
        switch (natLeave)
        {
            // 参加者は BossManager.IsBossPhase が立たない(ボスは HOST から) → HOST から届く「ボス戦で止まっている」で見る(D1 で参加者の退出が起きなかった)
            case "boss": return tRun > 5f && (NetCombat.Authority ? BossManager.Instance != null && BossManager.Instance.IsBossPhase && BossBattle.AnyBossFighting : NetCombat.RemoteBossHold && BossHalt.Holding);
            case "choice": return gm.IsRewardSequenceWaitingForSelection;
            case "down": return pc.NetIsDowned && runTime - natDownAt > 1.5f;
            case "result": return gm.IsGameOver && gm.ResultShown && runTime - natResultAt > 1f;
        }
        return false;
    }

    // 操作の判断: ゲームの CPU 仲間と同じ判断役(HighSpeedAssist の判断役。地形の先読み/届く時だけ攻撃/ボスの攻撃の回避)を、
    //  人ごとの反応の遅れ・見落とし(natMiss)を付けて、仮想のパッドの押下にする(=本物のパッドと同じ入力の経路)。
    //  画面の補助(HighSpeedAssist.Instance)は保存の設定のまま別に働く(100km/h から。人の入力があるフレームは人が優先)
    HighSpeedAssist natPlanner; PlayerController.FlickDirection? natPending; float natPendingAt; int natDecisions, natMissed, natPressed;
    float natMiss = 0.02f, natJumpLag = 0.04f;
    void NatFight(PlayerController pc)
    {
        if (natPlanner == null)
        {
            var go = new GameObject("[NatPlanner]");
            DontDestroyOnLoad(go);
            HighSpeedAssist.CreatingPlanner = true;
            try { natPlanner = go.AddComponent<HighSpeedAssist>(); } finally { HighSpeedAssist.CreatingPlanner = false; }
            L($"NATEV planner created react={natReact:F2} jumpLag={natJumpLag:F2} miss={natMiss:P0}");
        }
        var d = natPlanner.Decide(pc);
        if (d.HasValue && !natPending.HasValue)
        {
            natDecisions++;
            if (natRng.NextDouble() < natMiss) { natMissed++; }
            else { natPending = d; natPendingAt = Time.unscaledTime + (d.Value == PlayerController.FlickDirection.Up ? natJumpLag : natReact * 0.5f) * (0.7f + (float)natRng.NextDouble() * 0.6f); }
        }
        if (natPending.HasValue && Time.unscaledTime >= natPendingAt)
        {
            var a = natPending.Value switch
            {
                PlayerController.FlickDirection.Up => GameAction.Jump,
                PlayerController.FlickDirection.Down => GameAction.AttackDown,
                PlayerController.FlickDirection.Backward => GameAction.AttackBack,
                _ => GameAction.AttackForward,
            };
            NatPress(a); natPressed++;
            natPending = null;
        }
    }

    // 脱出: 決めた距離を過ぎたら、溜める(1.4秒)→離す(1.0秒)→溜め直す(脱出まで)
    bool NatEscape(GameManager gm, PlayerController pc)
    {
        float at = natEscapeAt[Mathf.Min(natRunIndex, 1)];
        if (at <= 0f || natEscDone || !gm.EscapeAvailable || pc.DistanceExact < at || pc.IsFinishing) return false;
        if (!natEscLogged) { natEscLogged = true; natEscPhase = 0; natEscT = 0f; L($"NATEV escape start t={runTime:F1} dist={pc.DistanceExact:F1} gauge={(float)EscTimerF.GetValue(pc):F2}"); }
        natEscT += Time.deltaTime;
        float g = (float)EscTimerF.GetValue(pc);
        if (natEscPhase == 0 && natEscT > 1.4f) { natEscPhase = 1; natEscT = 0f; L($"NATEV escape release t={runTime:F1} gauge={g:F2}"); }
        else if (natEscPhase == 1 && natEscT > 1.0f) { natEscPhase = 2; natEscT = 0f; L($"NATEV escape repress t={runTime:F1} gauge={g:F2}"); }
        else if (natEscPhase == 2 && natEscT > 12f) { natEscPhase = 1; natEscT = 0f; L($"NATEV escape gave up a hold (12s) t={runTime:F1} gauge={g:F2} needRelease={pc.EscapeNeedsRelease}"); }
        return natEscPhase != 1;
    }

    void NatChoice(GameManager gm)
    {
        if (!gm.IsRewardSequenceWaitingForSelection) { natChoiceSeenAt = -1f; natChoiceClicks = 0; natChoiceLogged = false; return; }
        var seq = FindFirstObjectByType<RewardCardSequence>();
        if (seq == null) return;
        string kind = ChoiceKindF != null ? ChoiceKindF.GetValue(gm).ToString() : "?";
        if (natChoiceSeenAt < 0f) natChoiceSeenAt = Time.unscaledTime;
        var ids = new List<string>();
        foreach (var c in seq.cards) if (c != null && c.gameObject.activeInHierarchy) ids.Add(c.Data.CardId);
        if (!natChoiceLogged) { natChoiceLogged = true; L($"NATEV choice open kind={kind} t={runTime:F1} options=[{string.Join(",", ids)}] lv={gm.Level} others=[{NatOthers()}]"); }
        float wait = kind == "BossReward" ? natHoldBoss : natHoldLv;
        if (Time.unscaledTime - natChoiceSeenAt < wait) return;
        if (ids.Count == 0) return;
        int idx = natPickRand ? natRng.Next(ids.Count) : Mathf.Clamp(natPick, 0, ids.Count - 1);
        // -netAutoNatPrefer a,b: この中のカードが出ていれば先に取る(遅い人/速い人を作る試験用。例 speed_down / speed_up)
        if (natPrefer.Length > 0) for (int i = 0; i < ids.Count; i++) if (System.Array.IndexOf(natPrefer, ids[i]) >= 0) { idx = i; break; }
        if (natChoiceClicks == 0 || Time.unscaledTime - natChoiceSeenAt - wait > 0.35f * natChoiceClicks)
        {
            if (natChoiceClicks == 0) L($"NATEV choice pick kind={kind} idx={idx} card={ids[idx]} waited={Time.unscaledTime - natChoiceSeenAt:F1}s");
            seq.OnCardClicked(idx);
            natChoiceClicks++;
        }
    }

    string NatOthers()
    {
        var sb = new System.Text.StringBuilder();
        if (NetMatch.Instance == null) return "";
        foreach (var r in NetMatch.Instance.Records.Values) if (r.Pn != NetCombat.LocalPlayerNumber) sb.Append($"P{r.Pn}:{r.State}{(r.Choosing ? "+choosing" : "")}@{r.Distance:F0} ");
        return sb.ToString().Trim();
    }

    string NatState(GameManager gm, PlayerController pc)
    {
        if (gm.IsGameOver) return "over";
        if (pc == null) return "none";
        if (pc.IsFinishing) return "escaping";
        if (pc.NetIsDowned) return "down";
        if (gm.IsRewardSequenceRunning || gm.IsLocalChoiceOpen) return "choosing";
        if (PauseF != null && (bool)PauseF.GetValue(gm)) return "paused";
        var bm = BossManager.Instance;
        if (bm != null && bm.IsBossPhase) return "boss";
        return "run";
    }

    // 0.5秒ごと: 自分(論理X/Y/地面/HP/Lv/EXP/能力/MILE)と、見えている他の人(表示の位置/表の HP/状態/距離)
    void NatLog(GameManager gm, PlayerController pc)
    {
        if (Time.unscaledTime < natNextLog) return;
        natNextLog = Time.unscaledTime + 0.5f;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string utc = DateTime.UtcNow.ToString("HH:mm:ss.fff");
        var tm = TerrainManager.Instance;
        string self = "-";
        if (pc != null && gm.HasStarted)
        {
            float gy = tm != null && tm.GetHeightAt(pc.transform.position.x) is float h ? h : float.NaN;
            var hud = RunBuildHud.Instance;
            string build = "";
            if (hud != null) { var sl = new List<string>(); foreach (var s in hud.Slots) sl.Add($"{s.cardId}:{s.level}"); sl.Sort(StringComparer.Ordinal); build = string.Join(",", sl); }
            self = string.Format(ci, "x={0:F2} y={1:F2} gnd={2:F2} grounded={3} hp={4}/{5} lv={6} exp={7:F0} up={8} build={9} runMile={10} eMile={11} bMile={12} bonus={13} kills={14} bkills={15} own={16:F1} hud={17:F1} spd={18:F1} esc={19:F2} st={20} char={21}",
                pc.transform.position.x + FloatingOrigin.Offset, pc.transform.position.y, gy, pc.IsGrounded ? 1 : 0, gm.Lives, gm.MaxLives, gm.Level, gm.TotalExpEarned, gm.UpgradeCount, Hash(build),
                gm.RunMile, gm.RunEnemyMile, gm.RunBossMile, gm.RunBonusMile, gm.EnemyKillCount, gm.BossKillCount, gm.MaxDistanceExact, gm.HudDistanceExact, pc.CurrentAutoRunSpeed, (float)EscTimerF.GetValue(pc), NatState(gm, pc), pc.CharId);
        }
        var others = new System.Text.StringBuilder();
        foreach (var np in NetPlayer.All)
        {
            if (np == null || np.IsOwner) continue;
            var a = np.Avatar; bool shown = a != null && a.IsShown;
            var rec = NetMatch.Get(np.PlayerNumber);
            float ax = shown ? a.transform.position.x : 0f;
            float agy = shown && tm != null && ax >= tm.FirstChunkStartX && ax <= tm.GeneratedEndX && tm.GetHeightAt(ax) is float h2 ? h2 : float.NaN; // 自分の端末で地形を作った範囲だけ(範囲の外は最後の高さが返り、比べられない)
            others.Append(string.Format(ci, " P{0}[shown={1} x={2:F2} y={3:F2} gnd={4:F2} ag={5} lag={6:F0} rd={7:F1} hp={8}/{9} st={10}{11}]", np.PlayerNumber, shown ? 1 : 0,
                shown ? ax + FloatingOrigin.Offset : 0.0, shown ? a.transform.position.y : 0f, agy, shown && a.Grounded ? 1 : 0, np.PlaybackLag * 1000f, np.RemoteDistance,
                rec != null ? rec.Hp : -1, rec != null ? rec.MaxHp : -1, rec != null ? rec.State.ToString() : "?", rec != null && rec.Choosing ? "+ch" : ""));
        }
        L($"NAT utc={utc} t={runTime:F1} run={natRunIndex} me=P{NetCombat.LocalPlayerNumber} role={role} {self} front={WorldProgress.FrontHigh:F1} team={(TeamDistance.Active ? TeamDistance.Display : -1):F1} conn={(NetSession.IsConnected ? 1 : 0)} n={NetPlayer.All.Count} rank='{NetMatch.ComputeRank().Text}'{others}");
        if (Time.unscaledTime >= natNextEnt && NetCombat.Instance != null && pc != null)
        {
            natNextEnt = Time.unscaledTime + 1f;
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (var e in NetCombat.Instance.Entities.Values)
            {
                if (e.Go == null || !e.Go.activeInHierarchy || e.Dead) continue;
                Vector3 p = e.Go.transform.position;
                if (Mathf.Abs(p.x - pc.transform.position.x) > 30f || n++ > 24) continue;
                sb.Append(string.Format(ci, " {0}:{1}:{2:F2}:{3:F2}", e.Id, e.Kind == NetCombat.Kind.Boss ? "B" : "E", p.x + FloatingOrigin.Offset, p.y));
            }
            L($"NATENT utc={utc} me=P{NetCombat.LocalPlayerNumber} auth={(NetCombat.Authority ? 1 : 0)}{sb}");
        }
    }

    // RESULT: 表示の額と保存の額を記録 → 読んでから「ホームへ戻る」(パッドの決定)
    void NatResult(GameManager gm)
    {
        if (!gm.ResultShown) return;
        if (!natResultLogged)
        {
            natResultLogged = true; natResultAt = runTime;
            bool vsPay = (bool)typeof(GameManager).GetField("versusPaid", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(gm);
            int shown = !gm.IsWin && vsPay ? gm.VersusTotalMile : gm.RunMile;
            string kind = gm.IsWin ? "win(take home)" : vsPay ? "versus-pay(take home)" : "lost";
            var rows = new System.Text.StringBuilder();
            if (NetMatch.Instance != null) foreach (var r in NetMatch.Instance.Records.Values) rows.Append($" P{r.Pn}:{r.State}:{r.FinalDistance:F1}/{r.Distance:F1}:k{r.Kills}:b{r.BossLastHits}");
            L($"NATRESULT run={natRunIndex} me=P{NetCombat.LocalPlayerNumber} kind={kind} shownMile={shown} walletShown={gm.TotalOwnedMile} savedMile={SaveStore.GetInt(GameManager.TotalMileKey, -1)} runMile={gm.RunMile} dist={gm.MaxDistanceExact:F1} hud={gm.HudDistanceExact:F1} lv={gm.Level} up={gm.UpgradeCount} kills={gm.EnemyKillCount} bkills={gm.BossKillCount} reason={gm.DeathReason} share={gm.MileShareCount} vs={(vsPay ? $"#{gm.VersusRank}/{gm.VersusPlayers} base={gm.VersusBaseMile} rank={gm.VersusRankMile} reaper={gm.VersusReaperMile}" : "-")} rows=[{rows.ToString().Trim()}] ops={natPressed}/{natDecisions}/miss{natMissed}");
            L($"REWARD mode={NetMatch.Mode} {RewardState(gm)}");
            NatSnap("result");
        }
        if (natHomePressed) return;
        if (runTime - natResultAt < natHomeDelay || gm.SecondsSinceGameOver < 3.5f) return;
        if (Time.unscaledTime < natHomeConfirmNext) return;
        natHomeConfirmNext = Time.unscaledTime + 0.8f;
        GameInput.Inject(GameAction.Confirm); // パッドの決定 = 「ホームへ戻る」
        L($"NATEV result home press t={runTime:F1} taps={gm.ResultTapsAccepted}/{gm.ResultTapsIgnored} block='{gm.LastResultTapBlock}'");
        natHomeTries++;
        if (natHomeTries >= 1) natHomePressed = gm.ResultTapsAccepted > 0 || natHomeTries > 12;
    }
    int natHomeTries;

    void NatAtHome(GameManager gm)
    {
        natHomeReached = true; natHomeAt = runTime;
        L($"NATEV home reached run={natRunIndex} t={runTime:F1} netActive={NetSession.IsActive} connected={NetSession.IsConnected} players={NetSession.ConnectedPlayerCount} runState={NetRunLauncher.RunState}");
        NatSnap("home");
    }

    bool natDeparted2, natDoneWritten; float natHomeLogNext; float natConnLostAt = -1f;
    void NatHomeTick(GameManager gm)
    {
        float since = runTime - natHomeAt;
        // HOST: もう1回(全員がホームへ戻ったら同じ部屋で出発)
        bool roomBusy = NetSession.IsActive && NetMatch.Instance != null && !NetMatch.Instance.RunOver && NetMatch.Instance.Records.Count > 0;
        if (role == "HOST" && natRunIndex + 1 < natRuns && !natDeparted2 && since > 8f && NetSession.ConnectedPlayerCount >= 2 && !roomBusy)
        {
            bool allHome = NatAllHome();
            if (allHome || since > 240f)
            {
                natDeparted2 = true;
                L($"NATEV depart again (run {natRunIndex + 1}) allHome={allHome} players={NetSession.ConnectedPlayerCount}");
                gm.DepartFromStageSelect(stage);
            }
        }
        // 自分だけ先に終わった(脱出/脱落)の時は、部屋のランが終わるまでホームで待つ(普通の人と同じく部屋に残る)
        // 2026-11-27: ホームでは NetMatch が空の表(RunOver=false)で残り、部屋のランが続いているかは分からない
        //  → 最後のランの後は、試験の道具の印のファイルで待ち合わせる(ゲームの判定ではない)。HOST は全員の印がそろってから部屋を閉じ、参加者は HOST が閉じるまで部屋に残る
        bool lastRun = natRunIndex + 1 >= natRuns;
        if (!string.IsNullOrEmpty(natDoneDir) && !natDoneWritten)
        {
            natDoneWritten = true;
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(natDoneDir, $"home_r{natRunIndex}_p{natProfile}.txt"), runTime.ToString("F1")); } catch (System.Exception e) { L("NATEV done file failed " + e.Message); }
        }
        if (Time.unscaledTime >= natHomeLogNext) { natHomeLogNext = Time.unscaledTime + 10f; L($"NATHOME t={runTime:F1} since={since:F0} netActive={NetSession.IsActive} connected={NetSession.IsConnected} players={NetSession.ConnectedPlayerCount} netMatch={(NetMatch.Instance != null)} roomRunOver={(NetMatch.Instance != null && NetMatch.Instance.RunOver)} runState={NetRunLauncher.RunState} mp={NetRunLauncher.IsMultiplayerRun} others=[{NatOthers()}] wallet={gm.TotalOwnedMile}"); }
        if (lastRun && NetSession.IsActive && !string.IsNullOrEmpty(natDoneDir))
        {
            if (role == "HOST")
            {
                if (NatHomeMarks() < Mathf.Max(1, NetSession.ConnectedPlayerCount)) { natHomeAt = runTime; return; }
            }
            else if (since < 1200f) return; // 参加者: HOST が部屋を閉じる(切断の案内)まで残る
        }
        if (lastRun || !NetSession.IsActive)
        {
            float stay = role == "HOST" ? 45f : 20f; // HOST は他の人が RESULT を読み終わるまで部屋を残す
            if (since > stay) { NatSnap("final"); Finish(natLeft ? "NAT left" : "NAT run over (natural end)"); }
        }
        else if (since > 240f) { NatSnap("final"); Finish("NAT waited too long for the next run"); }
    }

    int NatHomeMarks()
    {
        int done = 0; for (int i = 1; i <= 8; i++) if (System.IO.File.Exists(System.IO.Path.Combine(natDoneDir, $"home_r{natRunIndex}_p{i}.txt"))) done++;
        return done;
    }

    bool NatAllHome()
    {
        // 試験の道具の印がある時: 部屋の全員がこのランのホームへ戻った(同じ PC の試験だけ)
        if (!string.IsNullOrEmpty(natDoneDir)) return NatHomeMarks() >= NetSession.ConnectedPlayerCount && runTime - natHomeAt > 8f;
        // 部屋の全員のランの状態(各自の表の RunOver の後、ホームへ戻ったか)は直接は分からない → 自分以外が RESULT を閉じて戻るまでの時間を待つ
        return runTime - natHomeAt > 20f;
    }

    void NatNewRunStarted(GameManager gm)
    {
        natRunIndex++;
        natHomeReached = natHomePressed = natResultLogged = natDoneWritten = false; natHomeTries = 0;
        natEscDone = natEscLogged = false; natEscPhase = 0; natWasDown = false; natLastLv = 1; natLastBossKills = 0;
        natRunStartUnscaled = Time.unscaledTime;
        L($"NATEV new run started run={natRunIndex} lv={gm.Level} exp={gm.TotalExpEarned:F0} runMile={gm.RunMile} kills={gm.EnemyKillCount} bkills={gm.BossKillCount} up={gm.UpgradeCount} hp={gm.Lives}/{gm.MaxLives} dist={gm.MaxDistanceExact:F1}");
        NatSnap("run2start");
    }
}
#endif
