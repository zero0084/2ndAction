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
                    if (role == "HOST" && gm != null) gm.DepartFromStageSelect(stage);
                    Next(Step.WaitRun);
                }
                else if (stepTime > 30f) Finish("TIMEOUT waiting for players");
                break;
            case Step.WaitRun:
                if (gm != null && gm.HasStarted && NetRunLauncher.IsMultiplayerRun && !gm.CountdownActive)
                {
                    L($"run started seed={NetRunLauncher.ActiveRunSeed} stage={gm.ActiveRunStageId}");
                    Next(Step.Running);
                }
                else if (stepTime > 30f) Finish("TIMEOUT waiting for run start");
                break;
            case Step.Running:
                runTime += Time.unscaledDeltaTime;
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
            if (hp > 0 && hp < 4 && hpRefillTimer <= 0f)
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
