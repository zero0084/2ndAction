using UnityEngine;

// ボス戦の停止戦の見張り(2026-11-25、マスター報告「ボス戦でたまに前進しっぱなし/これ以上進めない」)。
// 記録だけで、ゲームの動きは変えない。BossHalt.Tick の最後に呼ぶ。次の4つを [HaltWatch] の行で残す:
//  ・seek     … 止まる所を探して走り続けた(穴/分岐/上の道で止まれない)。1秒以上の時だけ
//  ・runBoss  … 戦っているボスがいるのに止まらずに走っている(ラン再開/マルチの範囲外などの理由つき)。3秒以上
//  ・reach    … 止まっていて、いちばん近いボスが届かない所(体の端まで 7m 超)にいる。5秒以上
//  ・noBoss   … 止まったままなのに戦っているボスがいない(報酬の選択の画面を除く)。4秒以上
//  ・ahead    … 止まった所から 25m 以上前へずれた(攻撃の踏み込みの重ね)
public static class BossHaltWatch
{
    public static int SeekLong, RunBossLong, ReachLong, NoBossLong, AheadLong; // 確認用(回数)
    public static float SeekMaxSec, SeekMaxM, RunBossMaxSec, ReachMaxSec, NoBossMaxSec;

    static bool seeking; static float seekT0; static float seekX0; static string seekWhy = "";
    static float runBossT; static string runBossWhy = "";
    static float reachT; static float reachMaxGap;
    static float noBossT;
    static bool aheadLogged;
    static bool wasHolding;
    static float nextTrace;
    static bool? trace;
    static bool Trace
    {
        get
        {
            if (trace == null)
            {
                trace = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                var a = System.Environment.GetCommandLineArgs();
                for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-haltTrace" && a[i + 1] == "1") trace = true;
#endif
            }
            return trace.Value;
        }
    }
    // 1回の止まった戦いのまとめ(止まり終わりに1行): 長さ・減速の滑り・止まった所からの最大の前のずれ・前にボスがいるのに踏み込みが止まった時間(壁)
    static float holdT, wallT, maxAhead, bossFrontMax; static bool sumOn;
    public static float WallMaxSec, AheadMaxM; public static int Walls; // 確認用

    public static void ResetCounters()
    {
        SeekLong = RunBossLong = ReachLong = NoBossLong = AheadLong = Walls = 0; WallMaxSec = AheadMaxM = 0f;
        SeekMaxSec = SeekMaxM = RunBossMaxSec = ReachMaxSec = NoBossMaxSec = 0f;
    }

    static string Where(PlayerController pc)
    {
        var gm = GameManager.Instance; var bm = BossManager.Instance;
        return $"d={(gm != null ? gm.MaxDistance : 0f):F0} x={(pc != null ? pc.transform.position.x : 0f):F1} boss={(bm != null ? bm.BossMusicKey : "")} alive={(bm != null ? bm.AliveBossCount : 0)} mp={NetRunLauncher.IsMultiplayerRun}";
    }

    public static void Tick(PlayerController pc, bool want, string why)
    {
        if (pc == null) return;
        var gm = GameManager.Instance; var bm = BossManager.Instance;
        if (gm == null || bm == null || !gm.HasStarted || gm.IsGameOver) { seeking = false; runBossT = reachT = noBossT = 0f; return; }
        float dt = Time.deltaTime;
        if (dt <= 0f) return; // 一時停止/カード選択の間は数えない
        float x = pc.transform.position.x;
        bool fighting = BossBattle.AnyBossFighting;

        // seek
        if (BossHalt.SeekingFlat && !seeking) { seeking = true; seekT0 = Time.time; seekX0 = x; seekWhy = BossHalt.SeekReason; }
        else if (seeking && BossHalt.SeekingFlat) { if (seekWhy.IndexOf(BossHalt.SeekReason, System.StringComparison.Ordinal) < 0) seekWhy += "," + BossHalt.SeekReason; }
        if (seeking && !BossHalt.SeekingFlat)
        {
            seeking = false;
            float sec = Time.time - seekT0, m = x - seekX0;
            SeekMaxSec = Mathf.Max(SeekMaxSec, sec); SeekMaxM = Mathf.Max(SeekMaxM, m);
            if (sec >= 1f) { if (sec >= 2f) SeekLong++; Debug.Log($"[HaltWatch] seek {sec:F1}s {m:F0}m why={seekWhy} stillHolding={want} {Where(pc)}"); }
        }

        // runBoss: 戦っているボスがいるのに実際に走っている
        bool running = !want && pc.HaltFactor > 0.9f && bm.IsBossPhase && fighting && !bm.DeathSpawned;
        if (running)
        {
            if (runBossT <= 0f) runBossWhy = $"{why} resumed={bm.RunResumed} holds={bm.HoldsRun}";
            runBossT += dt;
        }
        else if (runBossT > 0f)
        {
            RunBossMaxSec = Mathf.Max(RunBossMaxSec, runBossT);
            if (runBossT >= 3f) { RunBossLong++; Debug.Log($"[HaltWatch] runBoss {runBossT:F1}s why={runBossWhy} {Where(pc)}"); }
            runBossT = 0f;
        }

        // reach: 止まっていて、ボスが届かない所にいる
        float front = fighting ? BossBattle.NearestBossFrontAheadPublic(x) : float.MaxValue;
        float gap = front == float.MaxValue ? float.MaxValue : (front >= 0f ? front : -front);
        bool far = want && pc.HaltFactor < 0.05f && fighting && gap != float.MaxValue && gap > 7f;
        if (far) { reachT += dt; reachMaxGap = Mathf.Max(reachMaxGap, gap); }
        else if (reachT > 0f)
        {
            ReachMaxSec = Mathf.Max(ReachMaxSec, reachT);
            if (reachT >= 5f) { ReachLong++; Debug.Log($"[HaltWatch] reach {reachT:F1}s maxGap={reachMaxGap:F1}m front={(front == float.MaxValue ? 0f : front):F1} act={NearestAct(x)} {Where(pc)}"); }
            reachT = 0f; reachMaxGap = 0f;
        }

        // noBoss: 止まったままでボスがいない(報酬の選択は時間が止まるので dt=0 で数えない)
        bool noBoss = want && !fighting && !gm.LevelUpPending && !gm.IsRewardSequenceRunning;
        if (noBoss) noBossT += dt;
        else if (noBossT > 0f)
        {
            NoBossMaxSec = Mathf.Max(NoBossMaxSec, noBossT);
            if (noBossT >= 4f) { NoBossLong++; Debug.Log($"[HaltWatch] noBoss {noBossT:F1}s phase={bm.IsBossPhase} defeated={bm.BossDefeatedThisPhase} {Where(pc)}"); }
            noBossT = 0f;
        }

        // まとめ
        if (want && !wasHolding) { holdT = wallT = 0f; maxAhead = 0f; bossFrontMax = -99f; sumOn = true; }
        if (want)
        {
            holdT += dt;
            if (BossHalt.Stopped || !BossHalt.ArenaV2)
            {
                float ahd = (float)BossHalt.AheadOfAnchor(x); maxAhead = Mathf.Max(maxAhead, ahd);
                if (front != float.MaxValue) bossFrontMax = Mathf.Max(bossFrontMax, ahd + front);
                if (front != float.MaxValue && front > 1f && BossHalt.ForwardLungeLimit(x) < 0.1f) wallT += dt;
            }
        }
        else if (sumOn && wasHolding)
        {
            sumOn = false;
            AheadMaxM = Mathf.Max(AheadMaxM, maxAhead); WallMaxSec = Mathf.Max(WallMaxSec, wallT); if (wallT >= 1f) Walls++;
            Debug.Log($"[HaltWatch] fight hold={holdT:F1}s slide={BossHalt.AnchorLX - BossHalt.HoldStartLX:F1}m maxAhead={maxAhead:F1}m bossFront={bossFrontMax:F1}m wall={wallT:F1}s clamps={BossHalt.BossFrontClamps} {Where(pc)}");
        }
        // -haltTrace 1(開発版): 止まっている間 0.25 秒ごとに、止まった所からのプレイヤー/ボスの位置
        if (Trace && want && Time.time >= nextTrace)
        {
            nextTrace = Time.time + 0.25f;
            var sb = new System.Text.StringBuilder();
            foreach (var lb in BossBattle.Living)
            {
                if (lb is WildBossBase w && w != null && !w.IsDead && w.isActiveAndEnabled)
                    sb.Append($" {w.GetType().Name}:front={(float)(w.transform.position.x - w.HalfWidth + FloatingOrigin.Offset - BossHalt.AnchorLX):F1} gap={w.DebugGap:F1} ent={w.IsEntering} free={w.DebugFreeGap} brk={w.Broken} act={w.DebugAct} split={w.DebugMoveSplit.x:F2}/{w.DebugMoveSplit.y:F2}/{w.DebugMoveSplit.z:F2}/{w.DebugMoveSplit.w:F2}");
            }
            Debug.Log($"[HaltTrace] t={Time.time:F2} stopped={BossHalt.Stopped} F={BossHalt.Factor:F2} ahead={(float)BossHalt.AheadOfAnchor(x):F1} lim={BossHalt.ForwardLungeLimit(x):F2} clamps={BossHalt.BossFrontClamps}{sb}");
        }
        // ahead
        if (want && !wasHolding) aheadLogged = false;
        double ah = BossHalt.AheadOfAnchor(x);
        if (want && !aheadLogged && ah > 25.0) { aheadLogged = true; AheadLong++; Debug.Log($"[HaltWatch] ahead {ah:F1}m from the stop point {Where(pc)}"); }
        wasHolding = want;
    }

    static string NearestAct(float x)
    {
        string act = ""; float best = float.MaxValue;
        foreach (var b in BossBattle.Living)
        {
            if (b is WildBossBase w && w != null && !w.IsDead && w.isActiveAndEnabled)
            {
                float d = Mathf.Abs(w.transform.position.x - x);
                if (d < best) { best = d; act = $"{w.GetType().Name}:{w.DebugAct}"; }
            }
            else if (b != null && !(b is WildBossBase)) { float d = Mathf.Abs(b.transform.position.x - x); if (d < best) { best = d; act = b.GetType().Name; } }
        }
        return act;
    }

    public static string Summary() => $"seekLong={SeekLong} (max {SeekMaxSec:F1}s/{SeekMaxM:F0}m) runBoss={RunBossLong} (max {RunBossMaxSec:F1}s) reach={ReachLong} (max {ReachMaxSec:F1}s) noBoss={NoBossLong} (max {NoBossMaxSec:F1}s) ahead={AheadLong} walls={Walls} (max {WallMaxSec:F1}s) maxAhead={AheadMaxM:F1}m arenaV2={BossHalt.ArenaV2}";
}
