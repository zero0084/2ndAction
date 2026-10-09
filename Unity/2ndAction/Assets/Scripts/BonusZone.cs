using System.Collections.Generic;
using UnityEngine;

// BONUS ZONE(2026-09-29) - 全ステージ共通のボーナス区画。
//
// 流れ: ボス撃破(ボス報酬の選択まで終わった)→ 安全区間 → 確率でBONUS ZONE → 「BONUS ZONE!」(走行は止めない)
//       → 報酬Enemyのwaveを流す(EncounterDirectorがBonus用のFormationを置く)→ 時間/距離で終了
//       → 「BONUS END」+ 残った報酬Enemyは逃げる/消える → BONUS RESULT(1〜2秒)→ 安全距離 → 通常Encounterへ戻る
// ・BONUS中(開始〜安全距離まで)は通常Encounter/壁/障害物/上ルートの敵/ボスの開始を止める(SuppressesNormalSpawns)。
// ・報酬は既存のRun Progressionへ: MILE=仮取得MILE(GameManager.AddRunBonusMile)、EXP=通常のEXP(GrantBonusExp)、
//   Card=既存の3枚Card Choiceを順番待ちへ1件追加(GrantBonusCardChoice)。GAME OVERなら仮取得MILEは通常どおり失う。
// ・マルチ: 報酬の同期は未対応のため自然発生させない(profile.allowInMultiplayer)。報酬は必ずRewardMile/Exp/Cardを通すので、
//   後から「報酬を受ける端末へ送る」形にできる。
// GameManagerと同じObjectに付く(シーン再読込=次Run/Homeで作り直される)。
public class BonusZone : MonoBehaviour
{
    public static BonusZone Instance { get; private set; }
    public enum Phase { Idle, Intro, Active, Ending, PostRest }

    public BonusZoneProfile Profile { get; private set; }
    public Phase State { get; private set; } = Phase.Idle;
    public BonusEncounterType Current { get; private set; }
    public bool IsJackpot { get; private set; }
    public int BonusMile { get; private set; }
    public float BonusExp { get; private set; }
    public int BonusCards { get; private set; }
    public float Elapsed { get; private set; }
    public float StartDistance { get; private set; }
    public float EndDistance { get; private set; }
    public int ZonesStarted { get; private set; }
    public int FairiesThisZone { get; private set; }
    // CLEAR / PERFECT(2026-10-01)。BonusMile/BonusExp はこの区画で得た全部(敵+CLEAR+PERFECT)。
    public int ClearMile { get; private set; }
    public float ClearExp { get; private set; }
    public int PerfectMile { get; private set; }
    public float PerfectExp { get; private set; }
    public bool PerfectAchieved { get; private set; }
    public string PerfectDetail { get; private set; } = "";
    public int MimicMile { get; private set; }
    readonly int[] seenByKind = new int[5], killedByKind = new int[5], goneByKind = new int[5];
    public int SeenCount(BonusEnemyKind k) => seenByKind[(int)k];
    public int KilledCount(BonusEnemyKind k) => killedByKind[(int)k];
    float IntroLength => IsJackpot && Profile.jackpotIntroSeconds > 0f ? Profile.jackpotIntroSeconds : Profile.introSeconds;
    public float RemainingSeconds => Profile == null ? 0f : Mathf.Max(0f, Profile.durationSeconds - Elapsed);
    public float RemainingDistance => Profile == null || GameManager.Instance == null ? 0f : Mathf.Max(0f, StartDistance + Profile.durationDistance - GameManager.Instance.MaxDistance);

    // 通常の敵の出現/ボスの開始を止めるか(Director・壁・障害物・上ルートの敵・BossManagerが見る)
    public static bool SuppressesNormalSpawns => Instance != null && Instance.State != Phase.Idle;
    // ボスの開始を待たせるか(区画の本体の間だけ。終了の表示/安全距離の間はボスを優先する)
    public bool BlocksBoss => State == Phase.Intro || State == Phase.Active;
    // Bonus用のwaveを出してよいか(開始〜終了まで)
    public bool SpawningAllowed => State == Phase.Intro || State == Phase.Active;

    readonly List<BonusEnemy> enemies = new List<BonusEnemy>();
    int waveIndex;
    float phaseTime;
    bool wasBossPhase;
    float pendingAtDistance = -1f;
    // #100 ULTIMATE: ボスの後の抽選の距離(無ければ -1)。前進はこの手前で止まる
    public float PendingStartDistance => pendingAtDistance;
    static System.Random rng = new System.Random();

    void Awake()
    {
        Instance = this;
        Profile = BonusZoneProfile.Load();
    }
    void OnDestroy() { if (Instance == this) Instance = null; RestoreBgm(); }

    // ===================================================================== //
    // 開始/終了
    // ===================================================================== //
    void Update()
    {
        var gm = GameManager.Instance;
        if (gm == null || Profile == null) return;
        if (!gm.HasStarted) return;
        if (gm.IsGameOver) { if (State != Phase.Idle) Abort(); return; }

        // ボス区間の終わり(ボス報酬の選択まで済んだ)を見て、安全区間の後に抽選する
        var bm = BossManager.Instance;
        bool boss = bm != null && bm.IsBossPhase;
        if (wasBossPhase && !boss && State == Phase.Idle) pendingAtDistance = gm.MaxDistance + Profile.delayAfterBoss;
        wasBossPhase = boss;
        if (pendingAtDistance >= 0f && State == Phase.Idle && gm.MaxDistance >= pendingAtDistance)
        {
            pendingAtDistance = -1f;
            if (CanStartNaturally(gm) && Rand() < Profile.chanceAfterBoss) Begin(null, false);
        }

        FlushGone();
        float dt = Time.deltaTime; // Level Up選択/ポーズ中は止まる(残り時間が減らない)
        phaseTime += dt;
        switch (State)
        {
            case Phase.Intro:
                Elapsed += dt;
                if (phaseTime >= IntroLength) { State = Phase.Active; phaseTime = 0f; }
                break;
            case Phase.Active:
                Elapsed += dt;
                if (Elapsed >= Profile.durationSeconds || gm.MaxDistance >= StartDistance + Profile.durationDistance) End();
                break;
            case Phase.Ending:
                if (phaseTime >= 0.6f + Profile.resultSeconds) { State = Phase.PostRest; phaseTime = 0f; }
                break;
            case Phase.PostRest:
                // 残りの報酬Enemyが居なくなり、安全距離を走ったら通常へ
                // (逃げ遅れた報酬Enemyが居ても、少し経てば片付けて通常へ戻す)
                if (phaseTime > 6f) foreach (var e in enemies) if (e != null && e.isActiveAndEnabled) e.gameObject.SetActive(false);
                if (gm.MaxDistance >= EndDistance + Profile.postRestDistance && ActiveEnemyCount() == 0)
                {
                    State = Phase.Idle;
                    Debug.Log($"[BONUS] back to normal encounters at {gm.MaxDistance:F0}m");
                }
                break;
        }
        // 次のボスが近づいたら区画を先に終える(ボスと重ねない。Debugで強制した時など)
        if ((State == Phase.Intro || State == Phase.Active) && bm != null && bm.enabled && bm.NextBossDistance > 0f)
        {
            float toBoss = bm.NextBossDistance - gm.MaxDistance;
            if (toBoss >= 0f && toBoss < 60f) End();
        }
        // 開始直後(0.6秒)は、同じフレームに置かれた/分岐の中身として後から出た通常の敵も片付ける
        if (State == Phase.Intro && phaseTime < 0.6f) ClearNormalEnemiesAhead();
        if (State == Phase.Intro || State == Phase.Active) UpdateBgm(true);
    }

    // 自動テスト(batchmode)では自然発生させない(既存のテストの前提=ボス後に通常の敵/障害物が続く、を変えない)。
    // BONUS ZONEのテストだけがtrueにする。実機/Editorでの通常プレイは常に自然発生する。
    public static bool AllowNaturalInBatch;
    // ラストダンジョン(2026-09-30): この距離では自然発生させない
    public static System.Func<float, bool> BlockedAt;

    bool CanStartNaturally(GameManager gm)
    {
        if (Application.isBatchMode && !AllowNaturalInBatch) return false;
        if (NetMatch.Active && !Profile.allowInMultiplayer) return false;
        if (BlockedAt != null && BlockedAt(gm.MaxDistance)) return false; // ラストダンジョンの終盤(ボスラッシュ〜エンディング)
        var bm = BossManager.Instance;
        if (bm != null && bm.NextBossDistance > 0f && bm.NextBossDistance - gm.MaxDistance < Profile.minDistanceToNextBoss) return false;
        var pc = PlayerController.Instance;
        return pc != null && !pc.IsFinishing;
    }

    // Debug/テスト用: 種類を指定して今すぐ始める(nullなら抽選)。
    public bool Force(string typeId = null)
    {
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted || gm.IsGameOver || Profile == null) return false;
        if (NetMatch.Active && !Profile.allowInMultiplayer) return false; // マルチは報酬の同期が未対応(Debugでも始めない)
        if (State != Phase.Idle) Abort();
        return Begin(typeId, true);
    }

    bool Begin(string typeId, bool forced)
    {
        var gm = GameManager.Instance;
        BonusEncounterType t = !string.IsNullOrEmpty(typeId) ? Profile.FindType(typeId) : RollType();
        if (t == null || t.waves.Count == 0) return false;
        Current = t;
        IsJackpot = t.id == Profile.jackpotTypeId;
        State = Phase.Intro; phaseTime = 0f; Elapsed = 0f; waveIndex = 0;
        BonusMile = 0; BonusExp = 0f; BonusCards = 0; FairiesThisZone = 0;
        ClearMile = 0; ClearExp = 0f; PerfectMile = 0; PerfectExp = 0f; PerfectAchieved = false; PerfectDetail = ""; MimicMile = 0;
        System.Array.Clear(seenByKind, 0, 5); System.Array.Clear(killedByKind, 0, 5); System.Array.Clear(goneByKind, 0, 5); pendingGone.Clear();
        StartDistance = gm.MaxDistance;
        ZonesStarted++;
        ClearNormalEnemiesAhead();
        introFlash = 1f;
        PlayChime(IsJackpot);
        if (IsJackpot) StartCoroutine(JackpotFanfare());
        Debug.Log($"[BONUS] start {t.id}{(IsJackpot ? " (JACKPOT)" : "")}{(forced ? " (forced)" : "")} at {StartDistance:F0}m");
        return true;
    }

    // 種類の抽選: 低確率でJACKPOT、それ以外は重み付き(テストでは開始せずに抽選だけ確かめる)
    public BonusEncounterType RollType()
    {
        BonusEncounterType t = null;
        if (Rand() < Profile.jackpotChance) t = Profile.FindType(Profile.jackpotTypeId);
        return t ?? PickType();
    }

    BonusEncounterType PickType()
    {
        float total = 0f;
        foreach (var t in Profile.types) if (t != null && t.id != Profile.jackpotTypeId && t.weight > 0f) total += t.weight;
        if (total <= 0f) return null;
        float r = Rand() * total;
        foreach (var t in Profile.types)
        {
            if (t == null || t.id == Profile.jackpotTypeId || t.weight <= 0f) continue;
            if (r < t.weight) return t;
            r -= t.weight;
        }
        return null;
    }

    public void End()
    {
        if (State != Phase.Intro && State != Phase.Active) return;
        var gm = GameManager.Instance;
        State = Phase.Ending; phaseTime = 0f;
        EndDistance = gm != null ? gm.MaxDistance : 0f;
        // 走り切った報酬(BONUS CLEAR)と、条件を満たした時だけのPERFECT BONUS。どちらも仮取得MILE/通常のEXPへ。
        FlushGone();
        PerfectAchieved = EvaluatePerfect(out string detail);
        PerfectDetail = detail;
        GrantEndRewards();
        foreach (var e in enemies) if (e != null && e.isActiveAndEnabled && !e.Dying) e.Leave(); // 倒した直後(死亡演出中)の敵の撃破報酬は出す
        RestoreBgm();
        if (PerfectAchieved) PlayPerfect(); else PlayEndSe();
        Debug.Log($"[BONUS] end {Current?.id}: MILE +{BonusMile} EXP +{BonusExp:F0} CARD +{BonusCards} (clear +{ClearMile}/+{ClearExp:F0}, perfect={PerfectAchieved} +{PerfectMile}/+{PerfectExp:F0} [{PerfectDetail}]) ({Elapsed:F1}s, {EndDistance - StartDistance:F0}m)");
    }

    void GrantEndRewards()
    {
        var gm = GameManager.Instance;
        if (gm == null || Current == null) return;
        float etn = gm.ExpToNext;
        ClearMile = RewardMile(Current.clearMile, Vector3.zero, null, silent: true);
        float clearExp = Current.clearExp + Current.clearExpPerLevel * etn;
        float perfectExp = 0f;
        if (PerfectAchieved)
        {
            PerfectMile = RewardMile(Current.perfectMile, Vector3.zero, null, silent: true);
            perfectExp = Current.perfectExp + Current.perfectExpPerLevel * etn;
        }
        // EXPはまとめて1回で入れる(途中でLevel Upの選択が開くと、後から入れるEXPが捨てられるため)
        float got = RewardExp(clearExp + perfectExp, Vector3.zero, null, silent: true);
        float total = clearExp + perfectExp;
        ClearExp = total > 0f ? got * clearExp / total : 0f;
        PerfectExp = got - ClearExp;
    }

    // PERFECT の判定。対象の報酬Enemyのうち「画面に現れた」もの(終了直前に現れたものは除く)を、決めた割合以上倒したか。
    // Mimicの条件は「引き出したMILEが規定以上、または撃破」。
    bool EvaluatePerfect(out string detail)
    {
        detail = "";
        if (Current == null || Profile == null) return false;
        bool any = false, ok = true;
        var parts = new List<string>();
        if (Current.perfectKinds != null && Current.perfectKinds.Count > 0)
        {
            any = true;
            int kills = 0, req = 0;
            foreach (var k in Current.perfectKinds) { kills += killedByKind[(int)k]; req += killedByKind[(int)k] + goneByKind[(int)k]; }
            foreach (var e in enemies)
            {
                if (e == null || !e.isActiveAndEnabled || e.Killed || !Current.perfectKinds.Contains(e.kind)) continue;
                if (e.Dying) { kills++; req++; continue; } // 倒した直後(死亡演出中)
                if (e.Seen && Time.time - e.FirstSeenTime >= Profile.perfectGraceSeconds) req++;
            }
            int need = Mathf.Max(1, Mathf.Max(Current.perfectMinKills, Mathf.CeilToInt(Current.perfectKillRatio * req - 1e-4f)));
            ok &= kills >= need;
            parts.Add($"{KindsLabel()} {kills}/{need}");
        }
        if (Current.perfectMimicMile > 0)
        {
            any = true;
            bool ko = killedByKind[(int)BonusEnemyKind.Mimic] > 0;
            foreach (var e in enemies) if (e != null && e.kind == BonusEnemyKind.Mimic && e.Dying) ko = true;
            bool m = MimicMile >= Current.perfectMimicMile || ko;
            ok &= m;
            parts.Add(ko ? "MIMIC KO" : $"MIMIC {MimicMile}/{Current.perfectMimicMile} MILE");
        }
        detail = string.Join("  ", parts);
        return any && ok;
    }

    string KindsLabel()
    {
        var names = new List<string>();
        foreach (var k in Current.perfectKinds)
            names.Add(k == BonusEnemyKind.TreasureGoblin ? "GOBLIN" : k == BonusEnemyKind.GoldenSlime ? "SLIME" : k == BonusEnemyKind.CardFairy ? "FAIRY" : k == BonusEnemyKind.Mimic ? "MIMIC" : k.ToString());
        return string.Join("+", names);
    }

    // BONUS中の簡易表示: PERFECT条件の進み具合(画面に現れた数を基準にした目安)
    public string PerfectProgress(out bool met)
    {
        met = false;
        if (Current == null) return "";
        var parts = new List<string>();
        bool ok = true, any = false;
        if (Current.perfectKinds != null && Current.perfectKinds.Count > 0)
        {
            any = true;
            int kills = 0, seen = 0;
            foreach (var k in Current.perfectKinds) { kills += killedByKind[(int)k]; seen += seenByKind[(int)k]; }
            int need = Mathf.Max(1, Mathf.Max(Current.perfectMinKills, Mathf.CeilToInt(Current.perfectKillRatio * seen - 1e-4f)));
            ok &= kills >= need;
            parts.Add($"{KindsLabel()} {kills}/{need}");
        }
        if (Current.perfectMimicMile > 0)
        {
            any = true;
            bool m = MimicMile >= Current.perfectMimicMile || killedByKind[(int)BonusEnemyKind.Mimic] > 0;
            ok &= m;
            parts.Add(killedByKind[(int)BonusEnemyKind.Mimic] > 0 ? "MIMIC KO" : $"MIMIC {Mathf.Min(MimicMile, Current.perfectMimicMile)}/{Current.perfectMimicMile} MILE");
        }
        met = any && ok;
        return any ? string.Join("  ", parts) : "";
    }

    public void NotifySeen(BonusEnemy e) { if (e != null && (State == Phase.Intro || State == Phase.Active)) seenByKind[(int)e.kind]++; }
    public void NotifyKilled(BonusEnemy e) { if (e != null && (State == Phase.Intro || State == Phase.Active)) killedByKind[(int)e.kind]++; }
    // 倒されずに居なくなった(逃げ切った)。画面に現れていた分だけPERFECTの対象に数える。
    // 敵は撃破の時も「非表示 → 撃破の報酬」の順に処理されるので、ここでは保留にして次のフレームで撃破済みかを確かめる。
    readonly List<BonusEnemy> pendingGone = new List<BonusEnemy>();
    public void NotifyGone(BonusEnemy e) { if (e != null && e.Seen && (State == Phase.Intro || State == Phase.Active) && !pendingGone.Contains(e)) pendingGone.Add(e); }
    void FlushGone()
    {
        foreach (var e in pendingGone) if (e != null && !e.Killed && e.kind != BonusEnemyKind.None) goneByKind[(int)e.kind]++;
        pendingGone.Clear();
    }
    public void AddMimicMile(int n) { if (State == Phase.Intro || State == Phase.Active) MimicMile += n; }

    // 撃破の手応え(小さな粒。Combatや次の敵を隠さない量)
    public void KillBurst(Vector3 pos, Color color, bool coins)
    {
        OneShotSpriteEffect.CreateScatterBurst(SoftCircleSprite(), pos, color, coins ? 10 : 8, 0.45f, 0.1f, 0.22f, 3.2f, 1f, RenderOrder.CombatFx);
    }

    System.Collections.IEnumerator JackpotFanfare()
    {
        var cam = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cam != null) cam.Shake(0.12f, 0.35f);
        for (int i = 0; i < 6; i++)
        {
            yield return new WaitForSecondsRealtime(0.09f);
            PlayCoin(i + 2, true);
        }
    }

    // Run終了/Game Over: 何も残さず止める
    void Abort()
    {
        foreach (var e in enemies) if (e != null) e.Leave();
        State = Phase.Idle; Current = null;
        RestoreBgm();
    }

    // 開始の瞬間、前方で待っている通常の敵を片付ける(BONUS中に通常の敵に襲われない)。
    // 目の前(6m以内)の敵だけは残す(急に消えると不自然、すぐ通り過ぎる)。
    void ClearNormalEnemiesAhead()
    {
        var pc = PlayerController.Instance;
        if (pc == null) return;
        float px = pc.transform.position.x;
        int n = 0;
        foreach (var ec in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
        {
            if (ec == null || ec.bonus != null || !ec.gameObject.activeInHierarchy) continue;
            if (ec.transform.position.x > px + 6f) { ec.gameObject.SetActive(false); n++; }
        }
        if (n > 0) Debug.Log($"[BONUS] cleared {n} normal enemies waiting ahead");
    }

    // ===================================================================== //
    // EncounterDirectorから: 次に置くFormation
    // ===================================================================== //
    public EncounterFormation NextFormation(out Vector2 gap)
    {
        gap = Current != null ? Current.waveGap : new Vector2(8f, 12f);
        if (Current == null || Current.waves.Count == 0) return null;
        string id = Current.waves[waveIndex % Current.waves.Count];
        waveIndex++;
        return Profile.FindFormation(id);
    }

    public void RegisterEnemy(BonusEnemy e) { if (e != null && !enemies.Contains(e)) enemies.Add(e); }
    public void UnregisterEnemy(BonusEnemy e) { enemies.Remove(e); }
    public int ActiveEnemyCount() { int n = 0; foreach (var e in enemies) if (e != null && e.isActiveAndEnabled) n++; return n; }
    public IReadOnlyList<BonusEnemy> Enemies => enemies;

    public bool TryReserveFairy()
    {
        if (Profile == null || FairiesThisZone >= Profile.maxFairiesPerZone || !SpawningAllowed) return false;
        FairiesThisZone++;
        return true;
    }

    // ===================================================================== //
    // 報酬(既存のRun Progressionへ)
    // ===================================================================== //
    // 報酬MILE(Run中の仮取得MILEへ。持ち帰れるのは正常終了の時だけ=通常のルール)。combo=同じ敵へ続けて当てた数(SEが上がっていく)
    public int RewardMile(int amount, Vector3 at, BonusEnemy src, bool kill = false, int combo = 0, bool silent = false)
    {
        var gm = GameManager.Instance;
        if (gm == null || amount <= 0) return 0;
        float mul = Current != null ? Current.mileMultiplier : 1f;
        int got = gm.AddRunBonusMile(Mathf.Max(1, Mathf.RoundToInt(amount * mul)));
        if (got <= 0) return 0;
        BonusMile += got;
        if (silent) return got;
        Popup(at, $"+{got} MILE", new Color(1f, 0.86f, 0.3f), src, false, got, kill);
        if (kill) PlayKillJingle(); else PlayCoin(combo);
        return got;
    }

    public float RewardExp(float amount, Vector3 at, BonusEnemy src, bool kill = false, bool silent = false)
    {
        var gm = GameManager.Instance;
        if (gm == null || amount <= 0f) return 0f;
        float mul = Current != null ? Current.expMultiplier : 1f;
        float got = gm.GrantBonusExp(amount * mul);
        if (got <= 0f) return 0f;
        BonusExp += got;
        if (silent) return got;
        Popup(at, $"+{Mathf.RoundToInt(got)} EXP", new Color(0.55f, 1f, 0.6f), src, kill, Mathf.RoundToInt(got), kill);
        PlayExp(kill);
        return got;
    }

    public void RewardCard(Vector3 at, BonusEnemy src)
    {
        var gm = GameManager.Instance;
        if (gm == null || Profile == null) return;
        for (int i = 0; i < Mathf.Max(1, Profile.fairyCardChoices); i++) gm.GrantBonusCardChoice();
        BonusCards += Mathf.Max(1, Profile.fairyCardChoices);
        Popup(at, "CARD CHOICE!", new Color(1f, 0.6f, 1f), src, true);
        cardFlash = 1f;
        PlayChime(false);
    }

    // ===================================================================== //
    // 表示(IMGUI、既存HUDと同じScreen Space)
    // ===================================================================== //
    class Pop { public Vector3 world; public string text; public Color color; public float t; public BonusEnemy src; public int amount; public bool big; public string unit; }
    readonly List<Pop> pops = new List<Pop>();
    const int MaxPops = 8;
    const float PopLife = 0.9f, MergeWindow = 0.45f;
    float introFlash, cardFlash;

    // 同じ敵から短い間に続いた「+N MILE」は1つにまとめる(Multi Hitで数字が何十個も重ならない)
    public void Popup(Vector3 world, string text, Color color, BonusEnemy src, bool big, int amount = 0, bool kill = false)
    {
        string unit = text.EndsWith(" MILE") ? "MILE" : text.EndsWith(" EXP") ? "EXP" : null;
        if (unit != null && src != null && !kill)
        {
            foreach (var p in pops)
                if (p.src == src && p.unit == unit && p.t < MergeWindow)
                {
                    p.amount += amount; p.text = $"+{p.amount} {unit}"; p.t = 0.05f; p.world = world; return;
                }
        }
        if (pops.Count >= MaxPops) pops.RemoveAt(0);
        pops.Add(new Pop { world = world, text = text, color = color, t = 0f, src = src, amount = amount, big = big || kill, unit = unit });
    }

    static Texture2D white;
    static Texture2D White() { if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); white.hideFlags = HideFlags.HideAndDontSave; } return white; }
    static Texture2D edgeTex;
    static Texture2D EdgeTex()
    {
        if (edgeTex == null)
        {
            edgeTex = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int i = 0; i < 64; i++) edgeTex.SetPixel(i, 0, new Color(1f, 1f, 1f, Mathf.Pow(1f - i / 63f, 2f)));
            edgeTex.Apply();
        }
        return edgeTex;
    }

    void OnGUI()
    {
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted || gm.IsOverlayOpen) return;
        float dtu = Time.unscaledDeltaTime;
        if (!gm.IsGameOver && State != Phase.Idle && Current != null)
        {
            DrawEdgeGlow();
            if (State == Phase.Intro) DrawIntroBanner();
            if (State == Phase.Intro || State == Phase.Active) DrawStatusStrip();
            if (State == Phase.Ending) DrawResult();
        }
        DrawPops(dtu);
        if (gm.DebugMode && !gm.IsGameOver) DrawDebug(gm);
        introFlash = Mathf.Max(0f, introFlash - dtu * 1.5f);
        cardFlash = Mathf.Max(0f, cardFlash - dtu * 1.8f);
    }

    Color ThemeColor(float t)
    {
        if (IsJackpot) return Color.Lerp(new Color(1f, 0.85f, 0.3f), Color.HSVToRGB(Mathf.Repeat(t * 0.6f, 1f), 0.6f, 1f), 0.45f); // JACKPOTは金に虹色が巡る
        return Current != null ? Current.color : new Color(1f, 0.84f, 0.3f);
    }

    void DrawEdgeGlow()
    {
        // 画面の左右の端に金色の光(開始直後は強く、区画の間は薄く脈打つ)。中央は空ける。
        float t = Time.unscaledTime;
        float a = State == Phase.Intro ? 0.55f : State == Phase.Active ? 0.16f + 0.06f * Mathf.Sin(t * 3f) : 0.1f;
        a = Mathf.Max(a, introFlash * 0.7f) + cardFlash * 0.4f;
        Color c = cardFlash > 0.01f ? Color.Lerp(ThemeColor(t), new Color(1f, 0.6f, 1f), cardFlash) : ThemeColor(t);
        c.a = a * Mathf.Lerp(0.25f, 1f, GameSettings.GlowIntensity); // 設定「発光演出」(区画中であることは弱めても分かる)
        Color prev = GUI.color; GUI.color = c;
        float w = Screen.width * (IsJackpot ? 0.12f : 0.08f);
        GUI.DrawTexture(new Rect(0f, 0f, w, Screen.height), EdgeTex());
        GUIUtility.RotateAroundPivot(180f, new Vector2(Screen.width - w * 0.5f, Screen.height * 0.5f));
        GUI.DrawTexture(new Rect(Screen.width - w, 0f, w, Screen.height), EdgeTex());
        GUI.matrix = Matrix4x4.identity;
        // 開始時の金色の粒(画面の端から上へ)
        if (State == Phase.Intro || introFlash > 0f)
        {
            int n = IsJackpot ? 56 : 16;
            for (int i = 0; i < n; i++)
            {
                float seed = i * 12.9898f;
                float side = (i % 2 == 0) ? Mathf.Repeat(seed * 0.37f, 1f) * w * 1.4f : Screen.width - Mathf.Repeat(seed * 0.53f, 1f) * w * 1.4f;
                float y = Screen.height * (1f - Mathf.Repeat(phaseTime * (0.35f + Mathf.Repeat(seed, 0.3f)) + Mathf.Repeat(seed * 0.11f, 1f), 1f));
                float s = 3f + Mathf.Repeat(seed * 7.7f, 4f);
                Color pc = ThemeColor(t + i * 0.05f); pc.a = 0.85f;
                GUI.color = pc;
                GUI.DrawTexture(new Rect(side, y, s, s), White());
            }
        }
        GUI.color = prev;
    }

    GUIStyle bannerStyle, subStyle, stripStyle, popStyle;
    void EnsureStyles()
    {
        if (bannerStyle != null) return;
        bannerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        subStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        stripStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        popStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };
    }

    static void Outlined(Rect r, string text, GUIStyle st, Color c, float o = 2f)
    {
        Color keep = st.normal.textColor;
        st.normal.textColor = new Color(0f, 0f, 0f, 0.85f * c.a);
        LocGUI.Label(new Rect(r.x - o, r.y, r.width, r.height), text, st); LocGUI.Label(new Rect(r.x + o, r.y, r.width, r.height), text, st);
        LocGUI.Label(new Rect(r.x, r.y - o, r.width, r.height), text, st); LocGUI.Label(new Rect(r.x, r.y + o, r.width, r.height), text, st);
        st.normal.textColor = c;
        LocGUI.Label(r, text, st);
        st.normal.textColor = keep;
    }

    void DrawIntroBanner()
    {
        EnsureStyles();
        float k = Mathf.Clamp01(phaseTime / Mathf.Max(0.1f, IntroLength));
        if (IsJackpot && phaseTime < 0.45f)
        {
            // JACKPOT: 開始の瞬間に画面全体が金色に光る(0.45秒で消える。走行は止めない)
            Color fc = new Color(1f, 0.85f, 0.35f, 0.45f * (1f - phaseTime / 0.45f) * GameSettings.GlowIntensity);
            Color keepColor = GUI.color; GUI.color = fc;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), White());
            GUI.color = keepColor;
        }
        float pop = k < 0.18f ? Mathf.Lerp(1.5f, 1f, k / 0.18f) : 1f;
        float alpha = k > 0.8f ? Mathf.Clamp01((1f - k) / 0.2f) : 1f;
        float h = Screen.height;
        bannerStyle.fontSize = Mathf.RoundToInt(h * (IsJackpot ? 0.11f : 0.09f) * pop);
        subStyle.fontSize = Mathf.RoundToInt(h * 0.042f);
        Color c = ThemeColor(Time.unscaledTime); c.a = alpha;
        float y = h * 0.18f; // プレイヤー(画面の高さの約半分)に重ならない高さ
        Outlined(new Rect(0f, y, Screen.width, h * 0.13f), IsJackpot ? "JACKPOT!!" : "BONUS ZONE!", bannerStyle, c, 3f);
        Color w = new Color(1f, 1f, 1f, alpha);
        Outlined(new Rect(0f, y + h * 0.12f, Screen.width, h * 0.06f), IsJackpot ? $"ALL REWARDS ×{Current.mileMultiplier:0.#}" : Current.displayName, subStyle, w);
        if (!string.IsNullOrEmpty(Current.perfectLabel)) Outlined(new Rect(0f, y + h * 0.175f, Screen.width, h * 0.05f), "PERFECT: " + Current.perfectLabel, subStyle, new Color(1f, 0.95f, 0.6f, alpha * 0.9f), 1.5f);
    }

    // 区画の間: 画面上の中央(Lv/EXPの下)に小さく「種類・残り・稼ぎ」
    void DrawStatusStrip()
    {
        EnsureStyles();
        float h = Screen.height;
        stripStyle.fontSize = Mathf.Max(12, Mathf.RoundToInt(h * 0.024f));
        Rect safe = StableSafeArea.Rect;
        float top = Screen.height - (safe.y + safe.height) + 28f + 54f + 8f;
        string txt = $"{(IsJackpot ? $"JACKPOT ×{Current.mileMultiplier:0.#}" : "BONUS  " + Current.displayName)}   {RemainingSeconds:0}s   MILE +{BonusMile}   EXP +{Mathf.RoundToInt(BonusExp)}{(BonusCards > 0 ? $"   CARD +{BonusCards}" : "")}";
        Vector2 sz = stripStyle.CalcSize(new GUIContent(txt));
        var r = new Rect((Screen.width - sz.x) * 0.5f - 12f, top, sz.x + 24f, sz.y + 6f);
        UiBackdrop.Draw(r, 0.5f);
        Outlined(r, txt, stripStyle, ThemeColor(Time.unscaledTime), 1f);
        // PERFECTの進み具合(小さく1行。BONUS中だけ)
        string prog = PerfectProgress(out bool met);
        if (prog.Length > 0)
        {
            string pt = met ? $"PERFECT OK!  {prog}" : $"PERFECT  {prog}";
            int fs = stripStyle.fontSize;
            stripStyle.fontSize = Mathf.Max(11, Mathf.RoundToInt(fs * 0.8f));
            Vector2 ps = stripStyle.CalcSize(new GUIContent(pt));
            var pr = new Rect((Screen.width - ps.x) * 0.5f - 10f, r.yMax + 3f, ps.x + 20f, ps.y + 4f);
            UiBackdrop.Draw(pr, 0.4f);
            Outlined(pr, pt, stripStyle, met ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 1f, 1f, 0.9f), 1f);
            stripStyle.fontSize = fs;
        }
    }

    void DrawResult()
    {
        EnsureStyles();
        float h = Screen.height;
        float a = phaseTime < 0.15f ? phaseTime / 0.15f : Mathf.Clamp01((0.6f + Profile.resultSeconds - phaseTime) / 0.3f);
        if (phaseTime < 0.6f)
        {
            // BONUS CLEAR!(PERFECTなら続けて PERFECT BONUS!)
            bannerStyle.fontSize = Mathf.RoundToInt(h * 0.075f);
            Outlined(new Rect(0f, h * 0.15f, Screen.width, h * 0.12f), IsJackpot ? "JACKPOT CLEAR!" : "BONUS CLEAR!", bannerStyle, new Color(1f, 1f, 1f, a), 3f);
            if (PerfectAchieved && phaseTime > 0.25f)
            {
                float pk = Mathf.Clamp01((phaseTime - 0.25f) / 0.12f);
                bannerStyle.fontSize = Mathf.RoundToInt(h * 0.085f * Mathf.Lerp(1.4f, 1f, pk));
                Outlined(new Rect(0f, h * 0.26f, Screen.width, h * 0.12f), "PERFECT BONUS!", bannerStyle, new Color(1f, 0.85f, 0.3f, a * pk), 3f);
            }
            return;
        }
        // 今回の稼ぎ: 敵 / CLEAR / PERFECT / TOTAL(TOTALは数え上げ)
        float rowH = h * 0.04f;
        int rows = 4;
        // プレイヤー(画面の高さの約半分)と走る先を覆わないよう、画面の上寄り(Lv/EXPの下)に詰めて出す
        var panel = new Rect(Screen.width * 0.5f - h * 0.4f, h * 0.17f, h * 0.8f, rowH * (rows + 1.3f));
        Color prev = GUI.color; GUI.color = new Color(1f, 1f, 1f, a);
        UiBackdrop.Draw(panel, 0.78f);
        GUI.color = prev;
        Color c = ThemeColor(Time.unscaledTime); c.a = a;
        subStyle.fontSize = Mathf.RoundToInt(h * 0.03f);
        Outlined(new Rect(panel.x, panel.y + rowH * 0.15f, panel.width, rowH), PerfectAchieved ? (IsJackpot ? "JACKPOT  PERFECT!" : "PERFECT BONUS!") : (IsJackpot ? "JACKPOT RESULT" : "BONUS RESULT"), subStyle, PerfectAchieved ? new Color(1f, 0.85f, 0.3f, a) : c, 2f);
        stripStyle.fontSize = Mathf.RoundToInt(h * 0.026f);
        int enemyMile = BonusMile - ClearMile - PerfectMile;
        float enemyExp = BonusExp - ClearExp - PerfectExp;
        float y0 = panel.y + rowH * 1.2f;
        ResultRow(panel, y0, "ENEMY", $"MILE +{enemyMile}   EXP +{Mathf.RoundToInt(enemyExp)}{(BonusCards > 0 ? $"   CARD +{BonusCards}" : "")}", new Color(1f, 1f, 1f, a));
        ResultRow(panel, y0 + rowH, "CLEAR", $"MILE +{ClearMile}   EXP +{Mathf.RoundToInt(ClearExp)}", new Color(0.85f, 0.95f, 1f, a));
        ResultRow(panel, y0 + rowH * 2f, "PERFECT", PerfectAchieved ? $"MILE +{PerfectMile}{(PerfectExp > 0.5f ? $"   EXP +{Mathf.RoundToInt(PerfectExp)}" : "")}" : $"—  ({PerfectDetail})", PerfectAchieved ? new Color(1f, 0.85f, 0.3f, a) : new Color(0.7f, 0.7f, 0.75f, a));
        float count = Mathf.Clamp01((phaseTime - 0.6f) / 0.6f); // TOTALを0.6秒で数え上げる
        stripStyle.fontSize = Mathf.RoundToInt(h * 0.032f);
        ResultRow(panel, y0 + rowH * 3.05f, "TOTAL", $"MILE +{Mathf.RoundToInt(BonusMile * count)}   EXP +{Mathf.RoundToInt(BonusExp * count)}{(BonusCards > 0 ? $"   CARD +{BonusCards}" : "")}", new Color(1f, 0.92f, 0.5f, a));
    }

    void ResultRow(Rect panel, float y, string head, string body, Color color)
    {
        var st = stripStyle;
        var keep = st.alignment;
        st.alignment = TextAnchor.MiddleLeft;
        Outlined(new Rect(panel.x + panel.width * 0.06f, y, panel.width * 0.26f, st.fontSize * 1.4f), head, st, color, 1.5f);
        Outlined(new Rect(panel.x + panel.width * 0.32f, y, panel.width * 0.66f, st.fontSize * 1.4f), body, st, color, 1.5f);
        st.alignment = keep;
    }

    void DrawPops(float dtu)
    {
        if (pops.Count == 0) return;
        EnsureStyles();
        Camera cam = Camera.main;
        for (int i = pops.Count - 1; i >= 0; i--)
        {
            var p = pops[i];
            p.t += Time.timeScale > 0f ? dtu : 0f;
            if (p.t >= PopLife || cam == null) { pops.RemoveAt(i); continue; }
            Vector3 sp = cam.WorldToScreenPoint(p.world + Vector3.up * (0.9f + p.t * 1.1f));
            if (sp.z < 0f) continue;
            float a = p.t < 0.1f ? p.t / 0.1f : Mathf.Clamp01((PopLife - p.t) / 0.3f);
            popStyle.fontSize = Mathf.RoundToInt(Screen.height * (p.big ? 0.046f : 0.034f));
            Color c = p.color; c.a = a;
            Outlined(new Rect(sp.x - 120f, Screen.height - sp.y - 20f, 240f, 40f), p.text, popStyle, c, 1.5f);
        }
    }

    bool debugOpen;
    void DrawDebug(GameManager gm)
    {
        EnsureStyles();
        var btn = new GUIStyle(GUI.skin.button) { fontSize = 13 };
        Rect safe = StableSafeArea.Rect;
        float x = Screen.width * 0.30f, y = Screen.height - safe.y - 28f - 36f;
        if (GUI.Button(new Rect(x, y, 96f, 34f), debugOpen ? "BONUS ▼" : "BONUS ▲", btn)) debugOpen = !debugOpen;
        var info = new GUIStyle(GUI.skin.label) { fontSize = 12 };
        info.normal.textColor = new Color(1f, 0.9f, 0.5f);
        string st = State == Phase.Idle ? $"BONUS idle (zones {ZonesStarted}){(pendingAtDistance >= 0f ? $" roll at {pendingAtDistance:F0}m" : "")}"
            : $"BONUS {State} {Current?.id} {RemainingSeconds:F1}s/{RemainingDistance:F0}m MILE+{BonusMile} EXP+{BonusExp:F0} CARD+{BonusCards} enemies={ActiveEnemyCount()}";
        LocGUI.Label(new Rect(x + 102f, y + 8f, 560f, 22f), st, info);
        if (!debugOpen || Profile == null) return;
        float by = y - 38f;
        if (GUI.Button(new Rect(x, by, 96f, 32f), "RANDOM", btn)) Force();
        if (GUI.Button(new Rect(x + 100f, by, 96f, 32f), "END", btn)) End();
        by -= 36f;
        int col = 0;
        foreach (var t in Profile.types)
        {
            if (t == null) continue;
            if (GUI.Button(new Rect(x + col * 124f, by, 120f, 32f), t.id.ToUpperInvariant(), btn)) Force(t.id);
            if (++col >= 4) { col = 0; by -= 36f; }
        }
    }

    // ===================================================================== //
    // 音(専用の短いSE。BGMは区画の間だけ少し速く)
    // ===================================================================== //
    static AudioClip chime, jackpotChime, coin, expSe, endSe;
    float coinCooldownUntil;
    bool bgmRaised;

    void UpdateBgm(bool on)
    {
        if (on && !bgmRaised && AudioManager.Instance != null) { AudioManager.Instance.SetBgmPitch(IsJackpot ? 1.1f : 1.06f); bgmRaised = true; }
    }
    void RestoreBgm()
    {
        if (bgmRaised && AudioManager.Instance != null) AudioManager.Instance.SetBgmPitch(1f);
        bgmRaised = false;
    }

    static AudioClip Tone(string name, float[] freqs, float noteLen, float decay, float vol)
    {
        int rate = 44100;
        int n = Mathf.CeilToInt(rate * (noteLen * freqs.Length + decay));
        var data = new float[n];
        for (int k = 0; k < freqs.Length; k++)
        {
            int start = Mathf.RoundToInt(k * noteLen * rate);
            for (int i = start; i < n; i++)
            {
                float t = (i - start) / (float)rate;
                float env = Mathf.Exp(-t / decay) * Mathf.Clamp01(t * 200f);
                data[i] += vol * env * (Mathf.Sin(2f * Mathf.PI * freqs[k] * t) + 0.35f * Mathf.Sin(4f * Mathf.PI * freqs[k] * t));
            }
        }
        var clip = AudioClip.Create(name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    void PlayChime(bool jackpot)
    {
        if (AudioManager.Instance == null) return;
        if (jackpot) { if (jackpotChime == null) jackpotChime = Tone("bonus_jackpot", new[] { 784f, 988f, 1175f, 1568f, 1976f, 2349f }, 0.07f, 0.35f, 0.22f); AudioManager.Instance.PlaySfx(jackpotChime); }
        else { if (chime == null) chime = Tone("bonus_chime", new[] { 1047f, 1319f, 1568f, 2093f }, 0.075f, 0.3f, 0.25f); AudioManager.Instance.PlaySfx(chime); }
    }
    // コイン音。combo(同じ敵へ続けて当てた数)が増えるほど音が上がっていく(ミミックへのコンボで「連続して稼いでいる」音)
    static AudioClip[] coinSteps;
    static AudioClip killJingle, expKillSe, perfectSe;
    void PlayCoin(int combo = 0, bool force = false)
    {
        if (AudioManager.Instance == null || (!force && Time.unscaledTime < coinCooldownUntil)) return; // 連打でもうるさくしない
        coinCooldownUntil = Time.unscaledTime + 0.06f;
        if (coinSteps == null)
        {
            coinSteps = new AudioClip[9];
            for (int i = 0; i < coinSteps.Length; i++)
            {
                float k = Mathf.Pow(2f, i / 12f * 2f); // 1段ごとに全音ずつ上がる
                coinSteps[i] = Tone("bonus_coin" + i, new[] { 1760f * k, 2637f * k }, 0.045f, 0.09f, 0.2f);
            }
        }
        AudioManager.Instance.PlaySfxVolume(coinSteps[Mathf.Clamp(combo - 1, 0, coinSteps.Length - 1)], 0.8f);
    }
    void PlayKillJingle()
    {
        if (AudioManager.Instance == null) return;
        if (killJingle == null) killJingle = Tone("bonus_kill", new[] { 1319f, 1760f, 2093f, 2637f }, 0.05f, 0.2f, 0.24f);
        AudioManager.Instance.PlaySfx(killJingle);
    }
    void PlayExp(bool kill = false)
    {
        if (AudioManager.Instance == null) return;
        if (kill)
        {
            if (expKillSe == null) expKillSe = Tone("bonus_expkill", new[] { 880f, 1175f, 1568f }, 0.045f, 0.18f, 0.2f);
            AudioManager.Instance.PlaySfxVolume(expKillSe, 0.85f);
            return;
        }
        if (Time.unscaledTime < coinCooldownUntil) return;
        coinCooldownUntil = Time.unscaledTime + 0.08f;
        if (expSe == null) expSe = Tone("bonus_exp", new[] { 880f, 1320f }, 0.05f, 0.12f, 0.18f);
        AudioManager.Instance.PlaySfxVolume(expSe, 0.8f);
    }
    void PlayPerfect()
    {
        if (AudioManager.Instance == null) return;
        if (perfectSe == null) perfectSe = Tone("bonus_perfect", new[] { 1047f, 1319f, 1568f, 2093f, 2637f, 3136f }, 0.07f, 0.45f, 0.24f);
        AudioManager.Instance.PlaySfx(perfectSe);
    }
    void PlayEndSe()
    {
        if (AudioManager.Instance == null) return;
        if (endSe == null) endSe = Tone("bonus_end", new[] { 1568f, 1175f, 1319f }, 0.09f, 0.3f, 0.2f);
        AudioManager.Instance.PlaySfx(endSe);
    }

    // カード妖精の光などに使う柔らかい円
    static Sprite softCircle;
    public static Sprite SoftCircleSprite()
    {
        if (softCircle != null) return softCircle;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d)));
        }
        tex.Apply();
        softCircle = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n / 1.2f);
        softCircle.hideFlags = HideFlags.HideAndDontSave;
        return softCircle;
    }

    static float Rand() => (float)rng.NextDouble();
}
