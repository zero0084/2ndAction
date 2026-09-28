using System;
using System.Reflection;
using UnityEngine;

// マルチプレイ対応Phase 1(2026-09-25) - 2プロセス(HOST/JOIN)による自動通信テスト。
// コマンドライン引数に -netAuto... がある時だけNetSessionが追加する(通常起動では存在しない)。
//
//   -netAutoHost                     HOSTとして開始し、2人揃ったら自動で出発
//   -netAutoJoin 127.0.0.1           JOINとして接続
//   -netAutoStage wasteland_road     出発するステージ
//   -netAutoSpeed 3                  走行速度の倍率(高速同期のテスト用)
//   -netAutoRunSeconds 40            Run開始から終了までの秒数
//   -netAutoLeaveAt 30               (JOIN側)Run開始からこの秒数で切断する(切断テスト)
//   -netLoadDelay 3                  (NetRunLauncher)この端末のシーン読み込みをわざと遅らせる(開始同期テスト)
//   -netAutoForceOutAt 9             この秒数で自分をHP0扱いにする(カード選択中でも。結果競合テスト)
//   -netAutoQueueAt 8                レベルアップ選択を開き、さらにレベルアップ1つ+ボス報酬をキューに積む
//   -netAutoLateChoiceAt 12          (脱落/Run終了後に)レベルアップ/ボス報酬を出そうとして、出ないことを確認する
//
// 自分のプレイヤーは簡易ボットが操作する(穴の手前でジャンプ、定期的に二段ジャンプ/攻撃)。
// 毎秒[NETTEST]行を出し、終了時に[NETTEST] SUMMARYで例外数と同期品質の集計を出す。
[DefaultExecutionOrder(1200)] // NetPlayer(1100)が分身を置いた後に測る
public class NetAutoTest : MonoBehaviour
{
    string role = "";
    string joinIp = "127.0.0.1";
    string stage = "wasteland_road";
    float speedMul = 1f;
    float runSeconds = 40f;
    float leaveAt = -1f;
    // FloatingOrigin(長距離でシーン全体を戻す処理)を短い間隔で頻繁に起こし、端末ごとに
    // 異なるタイミングで起きるシフトの下でも相手の表示がずれないかを確かめる。
    bool shiftTest;
    // Phase 2: 共有の敵を狙って攻撃するボット+戦闘ログ。-netAutoBossAt N でHOSTがN秒後にボスを出す。
    bool combat;
    float slowOffAt = -1f;
    bool slowToggled;
    bool combatMix; // 上攻撃(打ち上げ)/下攻撃(叩き落とし)も混ぜる
    int mixStep;
    float bossAt = -1f;
    int bossHp = 20;
    bool bossSpawned;
    float combatAttackTimer;
    // Phase 2.5: 被弾テスト(無敵を切り、HPが減りすぎたら補充して最後まで走る) / 攻撃しないボット /
    // ボスの種類 / 強制レベルアップを一定時間選ばずに保持する(選択中も世界が進むかの確認)
    bool damageTest;
    bool passive;
    string bossKind = "Wolf";
    float choiceAt = -1f, choiceHold = 0f;
    bool choiceForced;
    float choiceHoldUntil = -1f;
    float hpRefillTimer;
    float choiceLogTimer;
    double choiceLastRemoteX, choiceLastEnemySum;
    int choiceLastAttackCount;
    // Phase 3: モード / 指定時刻に自分を倒す / 自動で復活させる / 指定時刻にHPを設定 / 結果が出たら終了
    string modeArg = "";
    float killAt = -1f;
    bool killFall;
    bool autoRevive;
    float hpAtTime = -1f; int hpAtValue;
    bool hpAtDone;
    int startHp = 0;
    bool startHpDone;
    bool killStarted, killDone;
    readonly System.Collections.Generic.List<float> killTimes = new System.Collections.Generic.List<float>();
    int killIndex;
    float killTimer;
    float reviveTimer = -1f;
    float runOverSeen = -1f;
    float p3LogTimer;
    bool p3Hp => killAt >= 0f || hpAtTime >= 0f || startHp > 0;

    // 2026-09-28: 開始同期 / カード選択中は本人だけ停止 / 結果の優先 の検証
    float forceOutAt = -1f; bool forceOutDone;
    float queueAt = -1f; bool queueDone;
    float lateChoiceAt = -1f; bool lateChoiceDone; float lateChoiceCheckAt = -1f;
    // 開始前(GO!まで)の確認
    bool preGoInit; Vector2 preGoPos; float preGoMaxMove, preGoMaxDist, preGoEnemyTimer; int preGoMaxEnemies, preGoInputs;
    NetRunState lastRunState = NetRunState.None;
    string runStateTrail = "";
    bool distStartLogged;
    // 自分のカード選択の窓
    bool chOpen, chReported, chResumed, chProbeDone;
    float chOpenAt = -1f, chCloseAt = -1f, chRetargetAt = -1f, chSampleTimer, chProbeCheckAt = -1f;
    double chX0, chRemote0, chEnemy0, chCloseX;
    float chMaxMove, chDistGain, chDist0;
    double chRemoteMove, chEnemyMove;
    int chTargetedSamples, chHpLoss, chLastHp = -1, chProbeHpBefore, chProbeRejBefore;
    string chProbe = "";
    // 相手のカード選択(この端末から見て)
    bool rcActive; double rcX0; float rcMaxMove, rcStart, rcSampleTimer; int rcTargeted, rcPn;
    // 結果競合
    float violationTime; int violationFrames; bool violationLogged;

    enum Step { Connect, WaitPlayers, WaitRun, Running, AfterLeave, Done }
    Step step = Step.Connect;
    float stepTime;
    float runTime;
    bool speedApplied;
    bool left;

    int exceptions, errors;
    float logTimer;
    float botTimer;
    int botPhase;

    // 相手の表示品質(1秒ごとにリセット)
    bool haveLastRemote;
    float lastRemoteX;
    float secMaxStepErr, secMaxStep;
    int secBackSteps, secFrames;
    // 全体の集計
    float totalMaxStepErr, totalMaxStep;
    int totalBackSteps, totalFrames, remoteShownSeconds;
    string sigA = "", sigB = "";

    // -netAutoTrace path: 毎フレームの自分/相手の論理位置をCSVに書く(2プロセスのUTC時刻で突き合わせ、
    // 「相手に表示された位置」と「本人の実際の位置」の誤差をフレーム単位で求めるため)。
    string tracePath;
    System.IO.StreamWriter trace;
    // -netAutoEnemyTrace path: 毎フレーム、共有の敵/ボスの論理位置(HOST=正解/JOIN=表示)をUTC付きで書く。
    string enemyTracePath;
    System.IO.StreamWriter enemyTrace;

    public static bool ShouldRun => Array.Exists(Environment.GetCommandLineArgs(), a => a.StartsWith("-netAuto"));

    void Awake()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string next = i + 1 < args.Length ? args[i + 1] : "";
            if (a == "-netAutoHost") role = "HOST";
            else if (a == "-netAutoJoin") { role = "JOIN"; joinIp = next; }
            else if (a == "-netAutoStage") stage = next;
            else if (a == "-netAutoSpeed") float.TryParse(next, out speedMul);
            else if (a == "-netAutoRunSeconds") float.TryParse(next, out runSeconds);
            else if (a == "-netAutoLeaveAt") float.TryParse(next, out leaveAt);
            else if (a == "-netAutoShiftTest") shiftTest = true;
            else if (a == "-netAutoTrace") tracePath = next;
            else if (a == "-netAutoEnemyTrace") enemyTracePath = next;
            else if (a == "-netAutoCombat") combat = true;
            else if (a == "-netAutoSlowOffAt") float.TryParse(next, out slowOffAt);
            else if (a == "-netAutoCombatMix") { combat = true; combatMix = true; }
            else if (a == "-netAutoBossAt") float.TryParse(next, out bossAt);
            else if (a == "-netAutoBossHp") int.TryParse(next, out bossHp);
            else if (a == "-netAutoDamage") damageTest = true;
            else if (a == "-netAutoPassive") passive = true;
            else if (a == "-netAutoBossKind") bossKind = next;
            else if (a == "-netAutoChoiceAt") float.TryParse(next, out choiceAt);
            else if (a == "-netAutoChoiceHold") float.TryParse(next, out choiceHold);
            else if (a == "-netAutoMode") modeArg = next.ToLowerInvariant();
            else if (a == "-netAutoKillAt") { foreach (string k in next.Split(',')) if (float.TryParse(k, out float kt)) killTimes.Add(kt); if (killTimes.Count > 0) killAt = killTimes[0]; damageTest = true; }
            else if (a == "-netAutoKillFall") killFall = true;
            else if (a == "-netAutoRevive") autoRevive = true;
            else if (a == "-netAutoHpAt") { var parts = next.Split(':'); if (parts.Length == 2) { float.TryParse(parts[0], out hpAtTime); int.TryParse(parts[1], out hpAtValue); } damageTest = true; }
            else if (a == "-netAutoStartHp") { int.TryParse(next, out startHp); damageTest = true; }
            else if (a == "-netAutoForceOutAt") float.TryParse(next, out forceOutAt);
            else if (a == "-netAutoQueueAt") float.TryParse(next, out queueAt);
            else if (a == "-netAutoLateChoiceAt") float.TryParse(next, out lateChoiceAt);
        }
        if (!string.IsNullOrEmpty(tracePath))
        {
            try { trace = new System.IO.StreamWriter(tracePath, false); trace.WriteLine("utc,localX,localY,remoteShown,remoteX,remoteY,lag"); }
            catch (Exception e) { Debug.LogWarning("[NETTEST] trace open failed: " + e.Message); trace = null; }
        }
        if (!string.IsNullOrEmpty(enemyTracePath))
        {
            try { enemyTrace = new System.IO.StreamWriter(enemyTracePath, false); enemyTrace.WriteLine("utc,id,x,y,localX,localSpeed"); }
            catch (Exception e) { Debug.LogWarning("[NETTEST] enemy trace open failed: " + e.Message); enemyTrace = null; }
        }
        Application.logMessageReceived += OnLog;
        L($"start role={role} stage={stage} speed={speedMul} runSeconds={runSeconds} leaveAt={leaveAt}");
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
        if (trace != null) { trace.Dispose(); trace = null; }
    }

    void WriteTrace()
    {
        if (trace == null) return;
        PlayerController pc = PlayerController.Instance;
        if (pc == null) return;
        NetPlayer remote = null;
        foreach (NetPlayer p in NetPlayer.All) if (!p.IsOwner) { remote = p; break; }
        RemotePlayerAvatar a = remote != null ? remote.Avatar : null;
        bool shown = a != null && a.IsShown;
        double utc = (DateTime.UtcNow - DateTime.UtcNow.Date).TotalSeconds;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        trace.WriteLine(string.Format(ci, "{0:F4},{1:F3},{2:F3},{3},{4:F3},{5:F3},{6:F3}",
            utc, pc.transform.position.x + FloatingOrigin.Offset, pc.transform.position.y,
            shown ? 1 : 0,
            shown ? a.transform.position.x + FloatingOrigin.Offset : 0.0, shown ? a.transform.position.y : 0f,
            remote != null ? remote.PlaybackLag : 0f));
    }

    void WriteEnemyTrace()
    {
        if (enemyTrace == null || NetCombat.Instance == null) return;
        PlayerController pc = PlayerController.Instance;
        if (pc == null) return;
        double utc = (DateTime.UtcNow - DateTime.UtcNow.Date).TotalSeconds;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        float px = pc.transform.position.x;
        foreach (var e in NetCombat.Instance.Entities.Values)
        {
            if (e.Go == null || !e.Go.activeInHierarchy) continue;
            Vector3 p = e.Go.transform.position;
            if (Mathf.Abs(p.x - px) > 25f) continue;
            enemyTrace.WriteLine(string.Format(ci, "{0:F4},{1},{2:F3},{3:F3},{4:F3},{5:F2}", utc, e.Id, p.x + FloatingOrigin.Offset, p.y, px + FloatingOrigin.Offset, pc.CurrentAutoRunSpeed));
        }
    }

    void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Exception) exceptions++;
        else if (type == LogType.Error || type == LogType.Assert) errors++;
    }

    static void L(string s) => Debug.Log($"[NETTEST] utc={DateTime.UtcNow:HH:mm:ss.fff} {s}");

    void Update()
    {
        stepTime += Time.unscaledDeltaTime;
        GameManager gm = GameManager.Instance;
        switch (step)
        {
            case Step.Connect:
                if (stepTime < 2f) return;
                if (role == "HOST") NetSession.Instance.StartHost(NetSession.DefaultPort);
                else NetSession.Instance.StartClient(joinIp, NetSession.DefaultPort);
                Next(Step.WaitPlayers);
                break;
            case Step.WaitPlayers:
                if (NetSession.ConnectedPlayerCount >= 2 && stepTime > 2f)
                {
                    L($"both players connected (players={NetSession.ConnectedPlayerCount})");
                    if (role == "HOST" && modeArg != "")
                    {
                        NetRunLauncher.SelectedMode = modeArg == "versus" ? MultiplayerGameMode.Versus : MultiplayerGameMode.Coop;
                        L($"mode selected by HOST: {NetRunLauncher.SelectedMode}");
                    }
                    if (role == "HOST" && gm != null) gm.DepartFromStageSelect(stage);
                    Next(Step.WaitRun);
                }
                else if (stepTime > 30f) Finish("TIMEOUT waiting for players");
                break;
            case Step.WaitRun:
                PreGoCheck(gm);
                if (gm != null && gm.HasStarted && NetRunLauncher.IsMultiplayerRun && !gm.CountdownActive)
                {
                    L($"run started seed={NetRunLauncher.ActiveRunSeed} stage={gm.ActiveRunStageId} mode={NetRunLauncher.ActiveMode}");
                    PlayerController pc0 = PlayerController.Instance;
                    Vector2 p0 = pc0 != null ? new Vector2((float)(pc0.transform.position.x + FloatingOrigin.Offset), pc0.transform.position.y) : Vector2.zero;
                    L($"STARTSYNC role={role} me=P{NetCombat.LocalPlayerNumber} runningUtc={NetRunLauncher.LocalRunningUtc:HH:mm:ss.fff} serverAtRunning={NetRunLauncher.LocalRunningServerTime:F4} runStartNetworkTime={NetRunLauncher.RunStartNetworkTime:F4} states=[{runStateTrail.Trim()}] preGoMaxMove={preGoMaxMove:F3} preGoMaxDist={preGoMaxDist:F3} preGoMaxEnemies={preGoMaxEnemies} preGoInputsInjected={preGoInputs} posAtGo=({p0.x:F2},{p0.y:F2}) startPos=({preGoPos.x:F2},{preGoPos.y:F2})");
                    Next(Step.Running);
                }
                else if (stepTime > 40f) Finish("TIMEOUT waiting for run start");
                break;
            case Step.Running:
                runTime += Time.unscaledDeltaTime;
                if (!distStartLogged && PlayerController.Instance != null && PlayerController.Instance.DistanceExact > 0.01f)
                {
                    distStartLogged = true;
                    L($"DISTSTART role={role} me=P{NetCombat.LocalPlayerNumber} runTime={runTime:F3} dist={PlayerController.Instance.DistanceExact:F3}");
                }
                KeepAlive(gm);
                PickLevelUpCard(gm);
                Bot();
                if (!left && leaveAt > 0f && runTime >= leaveAt)
                {
                    left = true;
                    L("leaving session (disconnect test)");
                    NetSession.Instance.Leave();
                    Next(Step.AfterLeave);
                    break;
                }
                PeriodicLog(gm);
                ChoiceTest(gm);
                ChoiceMonitor(gm);
                RemoteChoiceMonitor();
                ResultConflictTest(gm);
                Phase3Test(gm);
                if (step == Step.Done) break;
                if (role == "HOST" && bossAt > 0f && !bossSpawned && runTime >= bossAt) SpawnTestBoss();
                // 自動スロー(2026-09-27): HOSTがOFFにしたら全員に反映されるか / 参加側は切り替えられないか
                if (slowOffAt > 0f && !slowToggled && runTime >= slowOffAt && AutoSlowMotion.Instance != null)
                {
                    slowToggled = true;
                    bool before = AutoSlowMotion.Instance.autoSlowEnabled;
                    AutoSlowMotion.Instance.SetEnabled(false);
                    L($"slow toggle OFF requested by {role}: canToggle={AutoSlowMotion.Instance.CanToggle} before={before} after={AutoSlowMotion.Instance.autoSlowEnabled}");
                }
                if (runTime >= runSeconds) Finish("run time elapsed");
                break;
            case Step.AfterLeave:
                KeepAlive(gm);
                PickLevelUpCard(gm);
                Bot();
                PeriodicLog(gm);
                if (stepTime > 4f) Finish("after leave");
                break;
        }
    }

    void LateUpdate()
    {
        if (step == Step.Running || step == Step.AfterLeave) { MeasureRemote(); WriteTrace(); WriteEnemyTrace(); }
    }

    // レベルアップ/ボス報酬のカード選択(ゲームが一時停止する)を、実プレイヤーと同じ
    // タップ経路(1回目=選択、2回目=確定)で自動的に解決する。
    float cardClickTimer = -1f;
    int levelUps;
    void PickLevelUpCard(GameManager gm)
    {
        if (gm == null || !gm.IsRewardSequenceWaitingForSelection) { cardClickTimer = -1f; return; }
        if (choiceHoldUntil > 0f && runTime < choiceHoldUntil) return; // 選択を保持中(世界が進み続けるかの確認)
        RewardCardSequence seq = FindFirstObjectByType<RewardCardSequence>();
        if (seq == null) return;
        if (cardClickTimer < 0f)
        {
            seq.OnCardClicked(0);
            cardClickTimer = 0f;
            levelUps++;
            L($"card choice #{levelUps} (auto pick)");
            return;
        }
        cardClickTimer += Time.unscaledDeltaTime;
        if (cardClickTimer >= 0.3f)
        {
            seq.OnCardClicked(0);
            cardClickTimer = 0f;
        }
    }

    void Next(Step s) { step = s; stepTime = 0f; }

    // ===================================================================== //
    // 2026-09-28: 開始同期(GO!までは何も進まない)の確認
    // ===================================================================== //
    void PreGoCheck(GameManager gm)
    {
        NetRunState st = NetRunLauncher.RunState;
        if (st != lastRunState)
        {
            double tg = NetRunLauncher.SecondsToGo;
            runStateTrail += $"{st}@{stepTime:F2}s ";
            L($"runstate -> {st} secondsToGo={(double.IsInfinity(tg) ? -1.0 : tg):F3}");
            lastRunState = st;
        }
        PlayerController pc = PlayerController.Instance;
        if (gm == null || pc == null || !gm.HasStarted || !NetRunLauncher.IsMultiplayerRun || !gm.CountdownActive) return;
        Vector2 pos = new Vector2((float)(pc.transform.position.x + FloatingOrigin.Offset), pc.transform.position.y);
        if (!preGoInit) { preGoInit = true; preGoPos = pos; }
        preGoMaxMove = Mathf.Max(preGoMaxMove, (pos - preGoPos).magnitude);
        preGoMaxDist = Mathf.Max(preGoMaxDist, Mathf.Max(gm.MaxDistance, (float)pc.DistanceExact));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // GO!前のジャンプ/攻撃の入力は受け付けないこと(毎フレーム入力を入れ続けて、動かないことを見る)
        // (GO!直前の0.15秒は入れない: 解放されたフレームで入力が1回だけ通ってしまい、開始位置の比較が乱れるため)
        double toGo = NetRunLauncher.SecondsToGo;
        if (toGo > 0.15) pc.debugInjectFlick = (preGoInputs++ % 2 == 0) ? PlayerController.FlickDirection.Up : PlayerController.FlickDirection.Forward;
        else pc.debugInjectFlick = null;
#endif
        preGoEnemyTimer -= Time.unscaledDeltaTime;
        if (preGoEnemyTimer <= 0f)
        {
            preGoEnemyTimer = 0.2f;
            int n = FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Length;
            if (NetCombat.Instance != null) n = Mathf.Max(n, NetCombat.Instance.Entities.Count);
            preGoMaxEnemies = Mathf.Max(preGoMaxEnemies, n);
        }
    }

    // ===================================================================== //
    // 2026-09-28: カード選択中は本人だけ停止(位置/距離/狙い/被弾)と、終了後の復帰
    // ===================================================================== //
    int LocalHp(GameManager gm)
    {
        var me = NetMatch.Get(NetCombat.LocalPlayerNumber);
        return NetCombat.Replica ? (me != null ? me.Hp : gm.Lives) : gm.Lives;
    }

    static bool AnyEntityTargets(int pn)
    {
        if (NetCombat.Instance == null) return false;
        foreach (var e in NetCombat.Instance.Entities.Values)
            if (e.Go != null && !e.Dead && e.Target == pn) return true;
        return false;
    }

    void ChoiceMonitor(GameManager gm)
    {
        PlayerController pc = PlayerController.Instance;
        if (gm == null || pc == null) return;
        int me = NetCombat.LocalPlayerNumber;
        bool open = pc.NetIsChoosing;
        double x = pc.transform.position.x + FloatingOrigin.Offset;
        if (open && !chOpen)
        {
            chOpen = true; chReported = false; chResumed = false; chProbeDone = false;
            chOpenAt = runTime; chCloseAt = -1f; chRetargetAt = -1f;
            chX0 = x; chDist0 = (float)pc.DistanceExact; chMaxMove = 0f; chDistGain = 0f;
            SnapshotWorld(out chRemote0, out chEnemy0, out _);
            chTargetedSamples = 0; chHpLoss = 0; chLastHp = LocalHp(gm); chProbe = "";
            L($"CHOICE OPEN me=P{me} x={x:F2} dist={pc.DistanceExact:F2} ts={Time.timeScale:F2}");
        }
        if (chOpen && open)
        {
            chMaxMove = Mathf.Max(chMaxMove, (float)Math.Abs(x - chX0));
            chDistGain = (float)pc.DistanceExact - chDist0;
            int hp = LocalHp(gm);
            if (hp < chLastHp) chHpLoss += chLastHp - hp;
            chLastHp = hp;
            chSampleTimer -= Time.unscaledDeltaTime;
            if (chSampleTimer <= 0f)
            {
                chSampleTimer = 0.1f;
                // 選択開始から0.6秒(狙いの見直し間隔+通信の遅れ)を過ぎても自分を狙う敵がいたら失敗として数える
                if (runTime - chOpenAt > 0.6f && AnyEntityTargets(me)) chTargetedSamples++;
            }
            // 被弾の確認: ローカルの被弾(無視されること)と、JOINは判定を通さない直接の申告(HOSTが拒否すること)
            if (!chProbeDone && runTime - chOpenAt > Mathf.Max(1f, choiceHold * 0.4f))
            {
                chProbeDone = true;
                chProbeHpBefore = LocalHp(gm);
                chProbeRejBefore = NetMatch.Instance != null ? NetMatch.Instance.StatClaimsRejected : 0;
                pc.TakeDamage(false, "AutoTestChoiceProbe");
                if (NetCombat.Replica) NetMatch.RouteLocalDamage(false, 1f, "AutoTestChoiceClaim");
                chProbeCheckAt = runTime + 0.6f;
            }
            if (chProbeCheckAt > 0f && runTime >= chProbeCheckAt)
            {
                chProbeCheckAt = -1f;
                int rej = (NetMatch.Instance != null ? NetMatch.Instance.StatClaimsRejected : 0) - chProbeRejBefore;
                chProbe = $"hp {chProbeHpBefore}->{LocalHp(gm)} claimRejected={rej}";
                L($"CHOICE PROBE me=P{me} {chProbe}");
            }
        }
        if (chOpen && !open)
        {
            chOpen = false;
            chCloseAt = runTime;
            chCloseX = x;
            SnapshotWorld(out double rx, out double es, out _);
            chRemoteMove = rx - chRemote0; chEnemyMove = Math.Abs(es - chEnemy0);
            L($"CHOICE CLOSED me=P{me} after {chCloseAt - chOpenAt:F2}s x={x:F2} maxMove={chMaxMove:F3} distGain={chDistGain:F3} remoteMove={chRemoteMove:F2} enemyMove={chEnemyMove:F2} ts={Time.timeScale:F2}");
        }
        if (chCloseAt > 0f && !chReported)
        {
            if (!chResumed && x - chCloseX > 1.0) { chResumed = true; L($"CHOICE RESUMED me=P{me} {runTime - chCloseAt:F2}s after close (moved {x - chCloseX:F2})"); }
            if (chRetargetAt < 0f && AnyEntityTargets(me)) chRetargetAt = runTime;
            if (runTime - chCloseAt > 8f || (chResumed && chRetargetAt > 0f) || step == Step.Done)
            {
                chReported = true;
                var rec = NetMatch.Get(me);
                L($"CHOICECHK role={role} me=P{me} window={chCloseAt - chOpenAt:F2}s localMaxMove={chMaxMove:F3} localDistGain={chDistGain:F3} remoteMove={chRemoteMove:F2} enemyMove={chEnemyMove:F2} targetedWhileChoosing={chTargetedSamples} hpLossWhileChoosing={chHpLoss} probe=[{chProbe}] resumed={chResumed} retargetedAfter={(chRetargetAt > 0f ? (chRetargetAt - chCloseAt).ToString("F2") + "s" : "no")} tableChoosingNow={(rec != null && rec.Choosing)}");
            }
        }
    }

    // この端末から見た相手のカード選択: 相手の分身が止まっているか / 敵が相手を狙っていないか
    void RemoteChoiceMonitor()
    {
        NetPlayer remote = null;
        foreach (NetPlayer p in NetPlayer.All) if (p != null && !p.IsOwner) { remote = p; break; }
        if (remote == null || remote.Avatar == null) return;
        int pn = remote.PlayerNumber;
        bool choosing = NetMatch.IsPlayerChoosing(pn);
        double x = remote.Avatar.transform.position.x + FloatingOrigin.Offset;
        if (choosing && !rcActive) { rcActive = true; rcPn = pn; rcX0 = x; rcStart = runTime; rcMaxMove = 0f; rcTargeted = 0; }
        if (rcActive && choosing)
        {
            // 状態表が届いた時点の位置から測る(補間の遅れの分として0.3秒後から)
            if (runTime - rcStart < 0.3f) rcX0 = x;
            else rcMaxMove = Mathf.Max(rcMaxMove, (float)Math.Abs(x - rcX0));
            rcSampleTimer -= Time.unscaledDeltaTime;
            if (rcSampleTimer <= 0f) { rcSampleTimer = 0.1f; if (runTime - rcStart > 0.6f && AnyEntityTargets(pn)) rcTargeted++; }
        }
        if (rcActive && !choosing)
        {
            rcActive = false;
            L($"REMOTECHOICE P{rcPn} seen by {role}: window={runTime - rcStart:F2}s remoteMaxMove={rcMaxMove:F3} targetedSamples={rcTargeted}");
        }
    }

    // ===================================================================== //
    // 2026-09-28: 結果の優先(脱落/Run終了でカード選択を閉じる・後から出さない)
    // ===================================================================== //
    void ResultConflictTest(GameManager gm)
    {
        if (gm == null || NetMatch.Instance == null) return;
        var nmi = NetMatch.Instance;
        int local = NetCombat.LocalPlayerNumber;
        var me = NetMatch.Get(local);
        PlayerController pc = PlayerController.Instance;
        if (forceOutAt >= 0f && !forceOutDone && runTime >= forceOutAt)
        {
            forceOutDone = true;
            L($"FORCE OUT requested me=P{local} choosing={(pc != null && pc.NetIsChoosing)} uiOpen={gm.IsLocalChoiceOpen} seqRunning={gm.IsRewardSequenceRunning}");
            NetMatch.RequestDebugForceOut();
        }
        if (queueAt >= 0f && !queueDone && runTime >= queueAt)
        {
            queueDone = true;
            choiceHoldUntil = runTime + 999f; // 選ばずに開いたままにする
            MethodInfo m = typeof(GameManager).GetMethod("TriggerLevelUpChoice", BindingFlags.Instance | BindingFlags.NonPublic);
            if (m != null) { m.Invoke(gm, null); m.Invoke(gm, null); }
            gm.NetOfferBossReward();
            L($"QUEUE me=P{local} open={gm.IsLocalChoiceOpen} pendingLevelUps={gm.PendingLevelUpCount} bossRewardQueued={GetPrivateField(gm, "bossRewardDeferredPending")}");
        }
        if (lateChoiceAt >= 0f && !lateChoiceDone && runTime >= lateChoiceAt)
        {
            lateChoiceDone = true;
            MethodInfo m = typeof(GameManager).GetMethod("TriggerLevelUpChoice", BindingFlags.Instance | BindingFlags.NonPublic);
            if (m != null) m.Invoke(gm, null);
            gm.NetOfferBossReward();
            lateChoiceCheckAt = runTime + 0.8f;
            L($"LATECHOICE attempt me=P{local} state={(me != null ? me.State.ToString() : "?")} runOver={nmi.RunOver} gameOver={gm.IsGameOver}");
        }
        if (lateChoiceCheckAt > 0f && runTime >= lateChoiceCheckAt)
        {
            lateChoiceCheckAt = -1f;
            L($"LATECHOICE result me=P{local} uiShown={gm.IsLocalChoiceOpen || gm.IsRewardSequenceRunning} pendingLevelUps={gm.PendingLevelUpCount} bossRewardQueued={GetPrivateField(gm, "bossRewardDeferredPending")}");
        }
        // 上位の状態(Run終了/脱落/DOWN)なのに選択UIが出ている時間を数える(1フレームの行き違いは除く)
        bool blocked = gm.IsGameOver || nmi.RunOver || (me != null && me.State != NetMatch.PState.Alive);
        bool ui = gm.IsLocalChoiceOpen || gm.IsRewardSequenceRunning;
        if (blocked && ui)
        {
            violationFrames++;
            if (violationFrames > 1) violationTime += Time.unscaledDeltaTime;
            if (!violationLogged && violationFrames > 2) { violationLogged = true; L($"RESULT VIOLATION: choice UI open while blocked (state={(me != null ? me.State.ToString() : "?")} runOver={nmi.RunOver} gameOver={gm.IsGameOver})"); }
        }
        else violationFrames = 0;
    }

    static object GetPrivateField(object target, string name)
    {
        FieldInfo f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return f != null ? f.GetValue(target) : null;
    }

    // Phase 2.5: 指定時刻にこの端末でレベルアップのカード選択を強制的に開き、一定時間選ばずに保持する。
    // その間の自分/相手/敵/攻撃の進み具合と timeScale を記録する(「選択中も世界が止まらない」の確認)。
    void ChoiceTest(GameManager gm)
    {
        if (gm == null || choiceAt < 0f) return;
        if (!choiceForced && runTime >= choiceAt)
        {
            choiceForced = true;
            choiceHoldUntil = runTime + choiceHold;
            MethodInfo m = typeof(GameManager).GetMethod("TriggerLevelUpChoice", BindingFlags.Instance | BindingFlags.NonPublic);
            if (m != null) m.Invoke(gm, null);
            L($"choice FORCED on {role} (me=P{NetCombat.LocalPlayerNumber}) hold={choiceHold:F1}s open={gm.IsLocalChoiceOpen} ts={Time.timeScale:F2}");
            SnapshotWorld(out choiceLastRemoteX, out choiceLastEnemySum, out choiceLastAttackCount);
            choiceLogTimer = 0f;
        }
        if (choiceForced && (gm.IsLocalChoiceOpen || runTime < choiceHoldUntil + 0.5f) && runTime < choiceHoldUntil + 1.5f)
        {
            choiceLogTimer += Time.unscaledDeltaTime;
            if (choiceLogTimer < 0.5f) return;
            choiceLogTimer = 0f;
            SnapshotWorld(out double rx, out double es, out int ac);
            PlayerController pc = PlayerController.Instance;
            L($"choice-window t={runTime:F1} open={gm.IsLocalChoiceOpen} ts={Time.timeScale:F2} localX={(pc != null ? pc.transform.position.x + FloatingOrigin.Offset : 0):F1} remoteDX={rx - choiceLastRemoteX:F2} enemyMove={Math.Abs(es - choiceLastEnemySum):F2} newAttacks={ac - choiceLastAttackCount} table=[{(NetMatch.Instance != null ? NetMatch.Instance.DebugDescribe() : "")}]");
            choiceLastRemoteX = rx; choiceLastEnemySum = es; choiceLastAttackCount = ac;
        }
    }

    // ===================================================================== //
    // Phase 3: CO-OP / VERSUS の自動テスト操作
    // ===================================================================== //
    void Phase3Test(GameManager gm)
    {
        if (gm == null || NetMatch.Instance == null) return;
        PlayerController pc = PlayerController.Instance;
        if (pc == null) return;
        var nmi = NetMatch.Instance;
        int local = NetCombat.LocalPlayerNumber;
        var me = NetMatch.Get(local);

        // 開始時のHP(被弾で早く倒れすぎないように)
        if (startHp > 0 && !startHpDone && runTime > 0.5f)
        {
            startHpDone = true;
            SetOwnHp(gm, startHp);
            L($"p3 start hp set to {startHp}");
        }
        // 指定時刻にHPを設定(Donor HP=1の確認など)
        if (hpAtTime >= 0f && !hpAtDone && runTime >= hpAtTime)
        {
            hpAtDone = true;
            SetOwnHp(gm, hpAtValue);
            L($"p3 hp set to {hpAtValue} at t={runTime:F1}");
        }
        // 指定時刻に自分を倒す(HP1にしてから環境ダメージ/落下)
        if (killAt >= 0f && !killDone && runTime >= killAt && (killStarted || (me != null && me.State == NetMatch.PState.Alive)))
        {
            if (!killStarted) { killStarted = true; SetOwnHp(gm, 1); killTimer = 0f; L($"p3 KILL start at t={runTime:F1} fall={killFall} dist={pc.DistanceExact:F1}"); }
            killTimer += Time.unscaledDeltaTime;
            bool hpIsOne = (NetCombat.Replica ? (me != null ? me.Hp : 99) : gm.Lives) <= 1;
            if (hpIsOne && killTimer > 0.3f && me != null && me.State == NetMatch.PState.Alive)
            {
                pc.TakeDamage(isFall: killFall, source: "AutoTestKill");
            }
            if (me != null && me.State != NetMatch.PState.Alive)
            {
                killDone = true;
                L($"p3 KILL done state={me.State} hp={me.Hp} downDist={me.DownDistance:F1} finalDist={me.FinalDistance:F1}");
                // 次の指定時刻があれば、復活後にもう一度倒す(連続復活/全員DOWNの確認用)
                if (++killIndex < killTimes.Count) { killAt = killTimes[killIndex]; killDone = false; killStarted = false; }
            }
        }
        // 自動で復活させる(条件が揃ったら0.4秒後にREVIVE)
        if (autoRevive && me != null && me.State == NetMatch.PState.Alive)
        {
            int target = 0;
            foreach (var r in nmi.Records.Values)
                if (r.Pn != local && NetMatch.CanRevive(r, me, out _)) { target = r.Pn; break; }
            if (target > 0)
            {
                if (reviveTimer < 0f) reviveTimer = 0f;
                reviveTimer += Time.unscaledDeltaTime;
                if (reviveTimer >= 0.4f) { reviveTimer = -1f; L($"p3 REVIVE press (donor=P{local} hp={me.Hp} dist={me.Distance:F1}) -> P{target}"); NetMatch.RequestRevive(target); }
            }
            else reviveTimer = -1f;
        }
        // 1秒ごとの状態(モード/表/自分の距離・EXP・撃破)
        p3LogTimer += Time.unscaledDeltaTime;
        if (p3LogTimer >= 1f)
        {
            p3LogTimer = 0f;
            string targets = "";
            if (NetCombat.Instance != null)
                foreach (var e in NetCombat.Instance.Entities.Values)
                    if (e.Go != null && !e.Dead && e.Target > 0) targets += $"P{e.Target} ";
            L($"p3 t={runTime:F1} mode={NetRunLauncher.ActiveMode} me=P{local} lives={gm.Lives} downed={pc.NetIsDowned} dist={pc.DistanceExact:F1} exp={gm.TotalExpEarned:F1} lv={gm.Level} kills={gm.EnemyKillCount} bossKills={gm.BossKillCount} gameOver={gm.IsGameOver} table=[{nmi.DebugDescribe().Trim()}] targets=[{targets.Trim()}] runOver={nmi.RunOver}");
        }
        if (nmi.RunOver && runOverSeen < 0f) { runOverSeen = runTime; L($"p3 RUN OVER seen at t={runTime:F1} gameOver={gm.IsGameOver}"); }
        if (runOverSeen >= 0f && runTime - runOverSeen > 4f)
        {
            L($"p3 final gameOver={gm.IsGameOver} lives={gm.Lives} exp={gm.TotalExpEarned:F1} dist={pc.DistanceExact:F1} kills={gm.EnemyKillCount} bossKills={gm.BossKillCount}");
            L($"RESULTCHK role={role} me=P{local} violationTime={violationTime:F3}s choicesClosed={gm.NetChoicesClosedCount} uiOpenNow={gm.IsLocalChoiceOpen || gm.IsRewardSequenceRunning} pendingLevelUps={gm.PendingLevelUpCount} bossRewardQueued={GetPrivateField(gm, "bossRewardDeferredPending")} runState={NetRunLauncher.RunState} state={(me != null ? me.State.ToString() : "?")} gameOver={gm.IsGameOver}");
            Finish("run over (mode rule)");
        }
    }

    void SetOwnHp(GameManager gm, int hp)
    {
        if (NetCombat.Replica) NetMatch.RequestDebugSetHp(hp);
        else
        {
            if (gm.MaxLives < hp) SetPrivateField(gm, "maxLives", hp);
            SetPrivateProperty(gm, "Lives", hp);
        }
    }

    static void SnapshotWorld(out double remoteX, out double enemySum, out int attackCount)
    {
        remoteX = 0; enemySum = 0; attackCount = 0;
        foreach (NetPlayer p in NetPlayer.All)
            if (!p.IsOwner && p.Avatar != null) remoteX = p.Avatar.transform.position.x + FloatingOrigin.Offset;
        if (NetCombat.Instance != null)
            foreach (var e in NetCombat.Instance.Entities.Values)
                if (e.Go != null && !e.Dead) enemySum += e.Go.transform.position.x + FloatingOrigin.Offset + e.Go.transform.position.y;
        if (NetAttackSync.Instance != null)
            attackCount = NetCombat.Authority ? NetAttackSync.Instance.StatRegistered : NetAttackSync.Instance.StatSpawnsReceived;
    }

    // 自動テスト中はゲームオーバーにならないようにする(このプロセスのメモリ上だけ、保存はしない)。
    bool slowInit;
    void KeepAlive(GameManager gm)
    {
        if (gm == null) return;
        // 以前のテスト(自動スローOFFの切り替え)の保存値が残っていても、既定(ON)の状態で測る。
        if (!slowInit && role == "HOST" && slowOffAt < 0f && AutoSlowMotion.Instance != null)
        {
            slowInit = true;
            if (!AutoSlowMotion.Instance.autoSlowEnabled) { AutoSlowMotion.Instance.SetEnabled(true); L("auto slow re-enabled (default ON) for this test"); }
        }
        if (damageTest)
        {
            // 被弾テスト: 無敵は切る。HPが減りすぎたら補充する(HOST=自分の値、JOIN=HOSTへの要求)。
            SetPrivateProperty(gm, "InvincibleMode", false);
            hpRefillTimer -= Time.unscaledDeltaTime;
            var me = NetMatch.Get(NetCombat.LocalPlayerNumber);
            int hp = NetCombat.Replica ? (me != null ? me.Hp : gm.Lives) : gm.Lives;
            if (!p3Hp && hp > 0 && hp < 4 && hpRefillTimer <= 0f)
            {
                hpRefillTimer = 2f;
                if (NetCombat.Replica) NetMatch.RequestDebugSetHp(30);
                else { SetPrivateProperty(gm, "maxLives", 30); SetPrivateField(gm, "maxLives", 30); SetPrivateProperty(gm, "Lives", 30); }
                L($"hp refill requested (hp was {hp})");
            }
        }
        else
        {
            SetPrivateProperty(gm, "InvincibleMode", true);
            if (gm.Lives < 50) SetPrivateProperty(gm, "Lives", 99);
        }
        if (shiftTest && FloatingOrigin.Instance != null && FloatingOrigin.Instance.shiftThreshold > 400f)
        {
            FloatingOrigin.Instance.shiftThreshold = role == "HOST" ? 300f : 380f; // 端末ごとにわざとずらす
            FloatingOrigin.Instance.keepPlayerAt = 100f;
            FloatingOrigin.Instance.shiftStep = 128f;
            L("floating origin shift test enabled");
        }
        PlayerController pc = PlayerController.Instance;
        if (!speedApplied && pc != null && speedMul > 0f && Mathf.Abs(speedMul - 1f) > 0.01f)
        {
            pc.runSpeed *= speedMul;
            speedApplied = true;
            L($"speed multiplier applied x{speedMul} runSpeed={pc.runSpeed:F1}");
        }
    }

    static void SetPrivateField(object target, string name, object value)
    {
        FieldInfo f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null) f.SetValue(target, value);
    }

    static void SetPrivateProperty(object target, string name, object value)
    {
        PropertyInfo p = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        MethodInfo setter = p != null ? p.GetSetMethod(true) : null;
        if (setter != null) setter.Invoke(target, new[] { value });
    }

    void SpawnTestBoss()
    {
        bossSpawned = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        BossManager bm = BossManager.Instance;
        if (bm == null || bm.IsBossPhase) { L("test boss skipped (boss phase already running)"); return; }
        BossManager.NetTestBossHpOverride = bossHp;
        WildBossKind kind = Enum.TryParse(bossKind, out WildBossKind k) ? k : WildBossKind.Wolf;
        bm.NetTestSpawnWild(kind, 1);
        BossManager.NetTestBossHpOverride = 0;
        L($"test boss spawned ({kind} hp={bossHp})");
#endif
    }

    // 共有の敵/ボスが攻撃の届く距離にいれば前攻撃する(両プレイヤーがほぼ同時に同じ敵を叩く状況を作る)。
    bool CombatBot(PlayerController pc)
    {
        if (!combat || NetCombat.Instance == null) return false;
        combatAttackTimer -= Time.unscaledDeltaTime;
        if (combatAttackTimer > 0f) return false;
        float px = pc.transform.position.x, py = pc.transform.position.y;
        foreach (var e in NetCombat.Instance.Entities.Values)
        {
            if (e.Dead || e.Go == null || !e.Go.activeInHierarchy) continue;
            Vector3 ep = e.Go.transform.position;
            float dx = ep.x - px;
            float reach = e.Kind == NetCombat.Kind.Boss ? 4.5f : 2.4f;
            if (dx > -0.4f && dx < reach && Mathf.Abs(ep.y - py) < (e.Kind == NetCombat.Kind.Boss ? 4f : 1.6f))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                PlayerController.FlickDirection dir = PlayerController.FlickDirection.Forward;
                if (combatMix)
                {
                    // 地上: 前→上(打ち上げ+ジャンプ) / 空中: 前→下(叩き落とし) を順に
                    mixStep++;
                    if (pc.IsGrounded) dir = mixStep % 3 == 0 ? PlayerController.FlickDirection.Up : PlayerController.FlickDirection.Forward;
                    else dir = mixStep % 2 == 0 ? PlayerController.FlickDirection.Down : PlayerController.FlickDirection.Forward;
                }
                pc.debugInjectFlick = dir;
#endif
                combatAttackTimer = 0.3f;
                return true;
            }
        }
        return false;
    }

    void Bot()
    {
        PlayerController pc = PlayerController.Instance;
        TerrainManager tm = TerrainManager.Instance;
        if (pc == null || tm == null) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        pc.debugInjectFlick = null;
        float px = pc.transform.position.x;
        float lead = 1.1f * Mathf.Max(1f, pc.CurrentAutoRunSpeed / 5f);
        if (pc.IsGrounded && tm.IsNearPit(px + lead, 0.4f))
        {
            pc.debugInjectFlick = PlayerController.FlickDirection.Up;
            return;
        }
        if (passive)
        {
            // 被弾テスト: 攻撃はしない。壁/障害物で止まり続けないよう一定間隔でジャンプだけする。
            botTimer += Time.unscaledDeltaTime;
            if (botTimer >= 1.3f && pc.IsGrounded) { botTimer = 0f; pc.debugInjectFlick = PlayerController.FlickDirection.Up; }
            return;
        }
        if (CombatBot(pc)) return;
        if (combat && damageTest)
        {
            // 被弾テストでは無敵が無いので、壁/障害物で止まり続けないよう定期的にジャンプだけ混ぜる。
            botTimer += Time.unscaledDeltaTime;
            if (botTimer >= 1.3f && pc.IsGrounded) { botTimer = 0f; pc.debugInjectFlick = PlayerController.FlickDirection.Up; }
            return;
        }
        if (combat) return; // 戦闘テストでは敵を狙う攻撃だけにする(ジャンプの乱入で当たり方がばらつかないように)
        botTimer += Time.unscaledDeltaTime;
        if (botTimer < 0.9f) return;
        botTimer = 0f;
        botPhase = (botPhase + 1) % 4;
        pc.debugInjectFlick = botPhase switch
        {
            0 => PlayerController.FlickDirection.Forward,   // 地上攻撃
            1 => PlayerController.FlickDirection.Up,        // ジャンプ
            2 => PlayerController.FlickDirection.Up,        // 二段ジャンプ(空中なら)
            _ => PlayerController.FlickDirection.Forward,   // 空中/地上攻撃
        };
#endif
    }

    void MeasureRemote()
    {
        NetPlayer remote = null;
        foreach (NetPlayer p in NetPlayer.All) if (!p.IsOwner) { remote = p; break; }
        RemotePlayerAvatar a = remote != null ? remote.Avatar : null;
        if (a == null || !a.IsShown) { haveLastRemote = false; return; }

        // FloatingOriginで戻した量を足した論理Xで測る(シーンのシフトを移動と誤認しない)。
        float x = (float)(a.transform.position.x + FloatingOrigin.Offset);
        float dt = Time.unscaledDeltaTime;
        if (haveLastRemote && dt > 0f)
        {
            float step = x - lastRemoteX;
            if (Mathf.Abs(step) < 5f) // 落下復帰などの瞬間移動は除外
            {
                // 表示位置の平滑な速度(指数平滑)から見た、このフレームの移動量のずれ = ガタつきの大きさ。
                float expected = smoothVx * dt;
                float err = Mathf.Abs(step - expected);
                if (secFrames > 0 || totalFrames > 0)
                {
                    secMaxStepErr = Mathf.Max(secMaxStepErr, err);
                    if (step < -0.02f && smoothVx > 1f) secBackSteps++;
                }
                secMaxStep = Mathf.Max(secMaxStep, Mathf.Abs(step));
                secFrames++;
                smoothVx = Mathf.Lerp(smoothVx, step / dt, 0.15f);
            }
        }
        lastRemoteX = x;
        haveLastRemote = true;
    }

    float smoothVx;

    void PeriodicLog(GameManager gm)
    {
        logTimer += Time.unscaledDeltaTime;
        PlayerController pc = PlayerController.Instance;
        NetPlayer remote = null;
        foreach (NetPlayer p in NetPlayer.All) if (!p.IsOwner) { remote = p; break; }
        RemotePlayerAvatar a = remote != null ? remote.Avatar : null;
        if (logTimer < 1f) return;
        logTimer = 0f;

        double lx = pc != null ? pc.transform.position.x + FloatingOrigin.Offset : 0;
        // 自動スロー(2026-09-27): 両端末の倍率が同じか、速い人の前進の優位が保たれているかの確認用
        var slow = AutoSlowMotion.Instance;
        if (slow != null)
            L($"slow t={runTime:F1} ts={Time.timeScale:F3} auto={TimeControl.AutoScale:F3} target={slow.TargetScale:F3} vmax={slow.JudgedSpeed:F2} own={(pc != null ? pc.CurrentAutoRunSpeed : 0f):F2} n={slow.ContributingPlayers} follower={slow.IsNetworkFollower} enabled={slow.autoSlowEnabled} X={lx:F1}");
        string remoteStr = "none";
        if (a != null)
        {
            double rx = a.transform.position.x + FloatingOrigin.Offset;
            remoteStr = $"shown={a.IsShown} X={rx:F2} Y={a.transform.position.y:F2} lagMs={remote.PlaybackLag * 1000f:F0} snaps={remote.SnapshotsReceived} maxStepErr={secMaxStepErr:F3} maxStep={secMaxStep:F3} backSteps={secBackSteps} frames={secFrames}";
            if (a.IsShown) remoteShownSeconds++;
        }
        totalMaxStepErr = Mathf.Max(totalMaxStepErr, secMaxStepErr);
        totalMaxStep = Mathf.Max(totalMaxStep, secMaxStep);
        totalBackSteps += secBackSteps;
        totalFrames += secFrames;
        secMaxStepErr = secMaxStep = 0f; secBackSteps = secFrames = 0;

        if (combat && NetCombat.Instance != null)
        {
            var nc = NetCombat.Instance;
            L($"combat t={runTime:F1} me=P{NetCombat.LocalPlayerNumber} {nc.DebugSignature()} spawns={nc.StatSpawns} dmgEvents={nc.StatDamageEvents} deaths={nc.StatDeaths} reqSent={nc.StatHitRequestsSent} reqApplied={nc.StatHitRequestsApplied} reqIgnored={nc.StatHitRequestsIgnored} dup={nc.StatDuplicateHits} kills(local)={(gm != null ? gm.EnemyKillCount : 0)} bossKills(local)={(gm != null ? gm.BossKillCount : 0)}");
        }
        if (NetMatch.Instance != null && (damageTest || choiceAt >= 0f || passive))
        {
            var nm = NetMatch.Instance;
            string targets = "";
            if (NetCombat.Instance != null)
                foreach (var e in NetCombat.Instance.Entities.Values)
                    if (e.Go != null && !e.Dead && e.Target > 0) targets += $"{e.Id}:P{e.Target} ";
            L($"p25 t={runTime:F1} me=P{NetCombat.LocalPlayerNumber} lives={(gm != null ? gm.Lives : -1)} table=[{nm.DebugDescribe().Trim()}] claims sent={nm.StatClaimsSent} acc={nm.StatClaimsAccepted} rej={nm.StatClaimsRejected} confirmed={nm.StatHitsConfirmed} hostRemote={nm.StatHostRemoteHits} ts={Time.timeScale:F2} choosing={(gm != null && gm.IsLocalChoiceOpen)} attacks=[{(NetAttackSync.Instance != null ? NetAttackSync.Instance.DebugSummary() : "")}] targets=[{targets.Trim()}]");
        }
        L($"t={runTime:F1} local X={lx:F2} Y={(pc != null ? pc.transform.position.y : 0f):F2} speed={(pc != null ? pc.CurrentAutoRunSpeed : 0f):F1} grounded={(pc != null && pc.IsGrounded)} dist={(gm != null ? gm.MaxDistance : 0f):F0} offset={FloatingOrigin.Offset:F0} | remote {remoteStr} | connected={NetSession.IsConnected}");

        TerrainManager tm = TerrainManager.Instance;
        if (tm != null && pc != null)
        {
            // 地形の生成済み末尾(論理X)がその区間を超えた直後に取る(古い区間は後で破棄されるため)。
            float genEnd = FloatingOrigin.ToLogical(tm.GeneratedEndX);
            if (sigA == "" && genEnd > 420f) { sigA = tm.DebugTerrainSignature(100f, 400f); L($"terrain signature [100,400] = {sigA}"); }
            if (sigB == "" && genEnd > 1520f) { sigB = tm.DebugTerrainSignature(1200f, 1500f); L($"terrain signature [1200,1500] = {sigB}"); }
        }
    }

    void Finish(string reason)
    {
        if (step == Step.Done) return;
        step = Step.Done;
        if (NetCombat.Instance != null)
        {
            foreach (string k in NetCombat.Instance.KillLog) L("KILL " + k);
        }
        L($"SUMMARY reason={reason} role={role} exceptions={exceptions} errors={errors} remoteShownSeconds={remoteShownSeconds} maxStepErr={totalMaxStepErr:F3} maxStep={totalMaxStep:F3} backSteps={totalBackSteps}/{totalFrames} sigA={sigA} sigB={sigB}");
        Invoke(nameof(Quit), 1f);
    }

    void Quit()
    {
        if (trace != null) { trace.Dispose(); trace = null; }
        if (enemyTrace != null) { enemyTrace.Dispose(); enemyTrace = null; }
        if (NetSession.IsActive) NetSession.Instance.Leave();
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
