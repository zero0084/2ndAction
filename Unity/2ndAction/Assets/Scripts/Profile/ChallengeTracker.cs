using UnityEngine;

// 常設チャレンジの判定(2026-11-07)。ラン中は毎フレーム状態を見て、出来事(被弾/撃破/BREAK/ボス撃破/成功の確定)は各所から呼ばれる。
//  対象のラン: 記録対象のラン(闘技場/練習/DEBUG RUN = DebugRun.WritesBlocked と、ワープ/無敵を使ったラン = RunLedger.debugUsed は対象外)。
//  ラン内の数え(被弾/オート/初期デッキ/BREAK の回数)は RunLedger.Run.ch = 中断データ(CONTINUE)に一緒に残り、新しいランでは新しく作られる。
//  ・距離: このランで到達した一番遠い距離(RunLedger.maxReached。疾走出発の到着地点を含む)
//  ・ノーダメージ/初期デッキ/オート OFF: 実際に走った距離(RunLedger.walked。疾走で飛ばした区間・CONTINUE で戻って走り直した区間は含まない)
//    被弾 = HP が減った瞬間(GameManager.TryDamagePlayer)。Shield/血の盾で防いだ物は被弾にしない
//  ・オート OFF: HighSpeedAssist の全体の設定(assistEnabled)をラン中ずっと見る。一度でも ON(または補助が一度でも動いた)なら、そのランは達成しない
//  ・速さ: PlayerController.CurrentRunKmh(能力としての速さ。停止戦/踏み込みでは揺れない)。出発前のカウントダウン中は見ない
//  ・空中: 敵を倒した瞬間にプレイヤーが接地していない(GameManager.RegisterEnemyKill の最後の一撃の人)/ 接地しない時間が続いた秒数(時間が止まっている間は進まない)
//  ・BREAK: MapCardRules.OnBossBreak(ボスの BreakCount++ の所。JOIN はパペットの BREAK の開き)
//  ・ボス: BossManager の撃破(OnWildBossDefeated の DeathKey)。ノーダメージのボス撃破 = ボス戦(IsBossPhase)が始まってから被弾していない
//  ・ボスラッシュ: 95〜97km の組を抜けて 98km のラスボスが出た(BossManager.FinalSpawnedAt)。ラスボスを倒しても達成
//  ・死神から逃げ切る: 通常マップで死神が出た(BossManager.DeathSpawned)ランが成功として確定した(RunLedger.CommitSuccess)
public class ChallengeTracker : MonoBehaviour
{
    public static ChallengeTracker Instance { get; private set; }

    string runId = "";
    float runBeganAt;
    bool lastBossPhase;
    float airTime;
    float homePollT;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[ChallengeTracker]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<ChallengeTracker>();
    }

    // 記録対象のランか
    public static bool Eligible(RunLedger.Run r) => r != null && !r.debugUsed && !DebugRun.WritesBlocked;

    static ChallengeRunStats Stats(RunLedger.Run r)
    {
        if (r.ch == null) r.ch = new ChallengeRunStats();
        var st = r.ch;
        if (st.v == 0)
        {
            // この仕組みより前の中断データから再開した: 中断前の被弾/オート/デッキが分からないので、そのランでは達成させない
            st.v = 1; st.hit = true; st.autoSeen = true; st.starterKnown = true; st.starterDeck = false;
            Debug.Log("[Challenge] resumed a run from before the challenge system - no-damage / auto-off / starter-deck are off for this run");
        }
        return st;
    }

    void Update()
    {
        var gm = GameManager.Instance;
        var r = RunLedger.Current;
        bool inRun = gm != null && gm.HasStarted && !gm.IsGameOver && r != null;
        if (!inRun) { airTime = 0f; HomePoll(gm); return; }
        if (r.runId != runId) { runId = r.runId; runBeganAt = Time.time; lastBossPhase = false; airTime = 0f; }
        if (!Eligible(r)) return;
        var st = Stats(r);
        if (gm.IsWin) return; // 脱出の確定の後は数えない

        if (!st.starterKnown) { st.starterKnown = true; st.starterDeck = IsStarterDeck(gm.DeckCards); }
        // オート: 設定の状態をラン中ずっと見る(一時停止中も)。補助が一度でも動いた印(usedAuto)も見る
        var hsa = HighSpeedAssist.Instance;
        if (!st.autoSeen && (hsa == null || hsa.assistEnabled || r.usedAuto)) { st.autoSeen = true; Debug.Log("[Challenge] auto was ON in this run (auto-off challenge not possible)"); }

        // 距離
        if (r.maxReached >= ChallengeDefs.Reach1) Try("dist_10k");
        if (r.maxReached >= ChallengeDefs.Reach2) Try("dist_50k");
        if (r.maxReached >= ChallengeDefs.Reach3) Try("dist_100k");
        if (!st.hit && r.walked >= ChallengeDefs.NoDamage1) Try("nodmg_10k");
        if (!st.hit && r.walked >= ChallengeDefs.NoDamage2) Try("nodmg_30k");
        if (st.starterDeck && r.walked >= ChallengeDefs.StarterGoal) Try("sp_starter");
        if (!st.autoSeen && r.walked >= ChallengeDefs.NoAutoGoal) Try("sp_noauto");

        // コンボ
        var cc = ComboCounterUI.Instance;
        if (cc != null && cc.ComboCount > st.maxCombo)
        {
            st.maxCombo = cc.ComboCount;
            if (st.maxCombo >= ChallengeDefs.Combo1) Try("combo_100");
            if (st.maxCombo >= ChallengeDefs.Combo2) Try("combo_500");
            if (st.maxCombo >= ChallengeDefs.Combo3) Try("combo_1000");
        }

        var pc = PlayerController.Instance;
        bool running = pc != null && !gm.CountdownActive && Time.timeScale > 0f;
        if (running)
        {
            // 速さ
            float kmh = pc.CurrentRunKmh;
            if (kmh > st.maxKmh && kmh < 2000f)
            {
                st.maxKmh = kmh;
                ProfileStore.NoteSpeed(kmh);
                if (kmh >= ChallengeDefs.Speed1) Try("speed_130");
                if (kmh >= ChallengeDefs.Speed2) Try("speed_150");
                if (kmh >= ChallengeDefs.Speed3) Try("speed_170");
            }
            // 空中にいる時間(着地で0へ)
            if (pc.IsGrounded) airTime = 0f;
            else { airTime += Time.deltaTime; if (airTime >= ChallengeDefs.AirStaySeconds) Try("air_stay"); }
        }

        // ボス戦の始まり(ノーダメージのボス撃破の基準)/ボスラッシュ
        var bm = BossManager.Instance;
        bool phase = bm != null && bm.IsBossPhase;
        if (phase && !lastBossPhase) st.hitsAtBoss = st.hits;
        if (!phase) st.hitsAtBoss = -1;
        lastBossPhase = phase;
        if (bm != null && bm.FinalSpawnedAt >= 0f && bm.FinalSpawnedAt >= runBeganAt) Try("boss_rush");
        if (bm != null && bm.DeathSpawned) Try("reaper_meet");
    }

    // ホーム(ランの外): 保存された記録で達成と言える物(落ちた確定の復旧などで、ランの中の判定を通らなかった時の受け皿)
    void HomePoll(GameManager gm)
    {
        homePollT -= Time.unscaledDeltaTime;
        if (homePollT > 0f) return;
        homePollT = 1f;
        if (gm == null || DebugRun.WritesBlocked || SaveStore.GetInt(ChallengeStore.InitKey, 0) == 0) return;
        foreach (var d in ChallengeDefs.All) if (d.restorable && !ChallengeStore.IsDone(d.id) && ChallengeStore.RecordSays(d.id)) ChallengeStore.Complete(d.id);
    }

    static void Try(string id) { if (!ChallengeStore.IsDone(id)) ChallengeStore.Complete(id); }

    public static bool IsStarterDeck(System.Collections.Generic.IReadOnlyList<string> deck)
    {
        if (deck == null || deck.Count == 0) return false;
        foreach (var id in deck) if (System.Array.IndexOf(DefaultSave.StarterIds, id) < 0) return false; // 合成した物(v2| キー)/ほかのカードが1枚でもあれば違う
        return true;
    }

    // ===================================================================== 出来事(各所から)
    static RunLedger.Run LiveRun()
    {
        var r = RunLedger.Current;
        var gm = GameManager.Instance;
        return gm != null && gm.HasStarted && Eligible(r) ? r : null;
    }

    // GameManager.TryDamagePlayer: HP が減った(Shield/血の盾で防いだ物は来ない)
    public static void OnPlayerDamaged(string reason, int amount)
    {
        var r = LiveRun(); if (r == null) return;
        var st = Stats(r);
        if (!st.hit) Debug.Log($"[Challenge] hit ({reason}, {amount}) - no-damage challenges failed for this run (walked {r.walked:F0}m)");
        st.hit = true; st.hits++;
    }

    // GameManager.RegisterEnemyKill(最後の一撃の人): 空中で倒した
    public static void OnEnemyKilled()
    {
        var r = LiveRun(); if (r == null) return;
        var pc = PlayerController.Instance;
        if (pc == null || pc.IsGrounded) return;
        Try("air_kill");
        if (ChallengeStore.AddCounter("air_kills", 1) >= ChallengeDefs.AirKillsGoal) Try("air_kill_100");
    }

    // MapCardRules.OnBossBreak: ボスを BREAK させた
    public static void OnBossBreak()
    {
        var r = LiveRun(); if (r == null) return;
        var st = Stats(r);
        st.breaks++;
        if (st.breaks >= ChallengeDefs.BreaksInRun) Try("break_run5");
        if (ChallengeStore.AddCounter("breaks", 1) >= ChallengeDefs.BreaksTotal) Try("break_total50");
    }

    // BossManager: ボスを倒した(key = DeathKey "Wild/Garmud" など。竜/魔人は "Dragon"/"Majin"、不明は null)
    public static void OnBossDefeated(string key)
    {
        var r = LiveRun(); if (r == null) return;
        var st = Stats(r);
        if (key == "Wild/Garmud") { Try("boss_garmud"); Try("boss_rush"); }
        else if (key == "Cave/Noctaris") { Try("boss_noctaris"); Try("boss_rush"); }
        else if (key == "Sky/Seraphiel") { Try("boss_seraphiel"); Try("boss_rush"); }
        var bm = BossManager.Instance;
        if (bm != null && bm.IsBossPhase && st.hitsAtBoss >= 0 && st.hits == st.hitsAtBoss && key != "Reaper") Try("nodmg_boss");
    }

    // RunLedger.CommitSuccess: 成功として確定した(記録対象のランだけ来る)
    public static void OnRunCommitted(RunLedger.Run r)
    {
        if (!Eligible(r)) return;
        // 確定の保存(CommitSuccess の最後の1回の SaveStore.Save)と一緒に書く
        var bm = BossManager.Instance;
        if (r.stageId != BossManager.LastStageId && bm != null && bm.DeathSpawned) ChallengeStore.Complete("reaper_escape", false, save: false);
        if (ProgressStats.LifetimeDistance >= ChallengeDefs.LifetimeGoal) ChallengeStore.Complete("dist_total", false, save: false);
        ProfileStore.FlushTopSpeed();
    }
}
