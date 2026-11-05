using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// マルチのルールの強化(2026-11-05、クローズドテスト前。マスター依頼):
//  1. CO-OP の復活: DOWN した本人が長押し → 長押しが終わった瞬間に HOST が「一番前を走っている生存プレイヤー(FRONT)」を選び、
//     その人の HP を 1 もらって HP 1 で復活。FRONT の HP が 2 未満なら復活できない。複数 DOWN は 1 人ずつ。全員 DOWN で終了(今までどおり)
//  2. VERSUS の死神の乱入時間(部屋を作る時に HOST が選ぶ): 時間か 100,000m の早い方で死神が 1 体だけ出る(WorldProgress の "death_100k" を共有)。
//     予告(残り 60/30/10 秒)。乱入した時に生き残っていた人に REAPER BONUS
//  3. VERSUS の MILE ボーナス: 試合が終わったら、脱落した人も脱落までに稼いだ MILE を持ち帰り、順位/上位率/REAPER BONUS を足す(HOST が割合を決める)。
//     切断/途中退出(Out)は持ち帰りなし。脱出した人(Escaped)は脱出の MILE を今までどおり受け取り、ボーナスは無し
//  4. 脱出の通知: 脱出した人を HOST が知り、生存者から外す(以前は生存のままで、試合の終わりの判定が進まなかった)
//  ルールの数値は MultiplayerRules。判定はすべて HOST。LOCAL / ONLINE 共通(接続の違いだけ)。2 人専用にしない(recs の全員から決める)
public partial class NetMatch
{
    const byte ReqEscaped = 10;            // JOIN → HOST: この人は脱出した(距離)
    const byte SyncReviveRejected = 9;     // HOST → 本人: 復活できなかった(理由)

    static MultiplayerRules Rules => MultiplayerRules.I;

    // ===================================================================== //
    // FRONT(一番前を走っている生存プレイヤー)。同じ距離は番号の小さい方
    // ===================================================================== //

    public static Rec FrontAlive()
    {
        if (Instance == null) return null;
        Rec best = null;
        foreach (var r in Instance.recs.Values)
        {
            if (r.State != PState.Alive) continue;
            if (best == null || r.Distance > best.Distance || (r.Distance == best.Distance && r.Pn < best.Pn)) best = r;
        }
        return best;
    }

    public enum ReviveBlock : byte { None = 0, NotCoop = 1, NotDown = 2, NoAlive = 3, FrontHpLow = 4, RunOver = 5 }

    // 判定のみ(状態は変えない)。donor = その時の FRONT
    public static bool CanRevive(Rec down, out Rec donor, out ReviveBlock block, out string why)
    {
        donor = FrontAlive();
        block = ReviveBlock.None; why = "";
        if (Mode != MultiplayerGameMode.Coop) { block = ReviveBlock.NotCoop; why = "not CO-OP"; return false; }
        if (Instance != null && Instance.runOver) { block = ReviveBlock.RunOver; why = "run over"; return false; }
        if (down == null || down.State != PState.Down) { block = ReviveBlock.NotDown; why = down == null ? "no player" : $"P{down.Pn} is {down.State}"; return false; }
        if (donor == null) { block = ReviveBlock.NoAlive; why = "no alive player"; return false; }
        if (donor.Hp < Rules.DonorMinHp) { block = ReviveBlock.FrontHpLow; why = $"FRONT P{donor.Pn} hp {donor.Hp} < {Rules.DonorMinHp}"; return false; }
        why = $"FRONT P{donor.Pn} hp {donor.Hp} dist {donor.Distance:F1}m";
        return true;
    }

    // DOWN した本人の端末から(長押しが終わった時)。HOST が受け取った瞬間の FRONT から HP をもらう
    public static void RequestRevive(int downPn)
    {
        if (Instance == null || !Active) return;
        if (NetCombat.Authority) Instance.HostHandleReviveRequest(NetCombat.LocalPlayerNumber, downPn);
        else SendToHost(w => { w.WriteValueSafe(ReqRevive); w.WriteValueSafe((byte)downPn); });
        Instance.Log($"REVIVE requested by P{NetCombat.LocalPlayerNumber} (hold complete) for P{downPn}");
    }

    void HostHandleReviveRequest(int requesterPn, int downPn)
    {
        Rec down = Get(downPn);
        ReviveBlock block; string why; Rec donor;
        if (requesterPn != downPn) { block = ReviveBlock.NotDown; why = $"P{requesterPn} asked for P{downPn} (only the downed player revives themselves)"; donor = null; }
        else if (CanRevive(down, out donor, out block, out why)) { ExecuteRevive(down, donor); return; }
        ReviveRejected++;
        Log($"REVIVE REJECTED down=P{downPn} requester=P{requesterPn}: {why}");
        if (down != null && down.Pn != NetCombat.LocalPlayerNumber)
            SendToClients(w => { w.WriteValueSafe(SyncReviveRejected); w.WriteValueSafe((byte)downPn); w.WriteValueSafe((byte)block); }, down.ClientId);
        else if (down != null) { lastReviveBlock = block; lastReviveBlockAt = Time.realtimeSinceStartup; }
    }

    // 実行(HOST のみ)。FRONT の HP -1 ハート、DOWN の人は HP 1 ハートで、DOWN した地点から復帰
    void ExecuteRevive(Rec down, Rec donor)
    {
        GameManager gm = GameManager.Instance;
        int local = NetCombat.LocalPlayerNumber;
        int gift = Rules.ReviveHp;
        int donorBefore = donor.Hp;
        donor.Hp -= gift;
        down.Hp = gift;
        down.State = PState.Alive;
        down.InvulnUntil = Time.realtimeSinceStartup + Rules.reviveInvulnSeconds;
        if (gm != null)
        {
            if (donor.Pn == local) gm.NetSetLocalLives(donor.Hp);
            if (down.Pn == local) gm.NetSetLocalLives(gift);
        }
        ReviveCount++;
        Log($"REVIVE EXECUTED down=P{down.Pn} donor(FRONT)=P{donor.Pn}: donor hp {donorBefore} -> {donor.Hp}, down hp 0 -> {down.Hp}, at DownDistance={down.DownDistance:F1}m (FRONT distance={donor.Distance:F1}m)");
        SendToClients(w => { w.WriteValueSafe(SyncRevived); w.WriteValueSafe((byte)down.Pn); w.WriteValueSafe((byte)donor.Pn); });
        dirty = true;
        SendTable();
        if (down.Pn == local) ApplyLocalStatePhase3(gm, PlayerController.Instance);
        if (donor.Pn == local && gm != null) gm.NetApplyAuthoritativeLives(donor.Hp, donor.MaxHp);
    }

    ReviveBlock lastReviveBlock; float lastReviveBlockAt = -99f;
    void OnReviveRejectedClient(FastBufferReader r)
    {
        r.ReadValueSafe(out byte pn); r.ReadValueSafe(out byte block);
        if (pn != NetCombat.LocalPlayerNumber) return;
        lastReviveBlock = (ReviveBlock)block; lastReviveBlockAt = Time.realtimeSinceStartup;
        Log($"REVIVE rejected by HOST: {lastReviveBlock}");
    }

    // ===================================================================== //
    // 長押し(DOWN した本人の端末)
    // ===================================================================== //

    float reviveHoldT; bool reviveHolding, reviveSent; float reviveSentAt = -99f;
    public float ReviveHoldProgress => Rules.reviveHoldSeconds > 0f ? Mathf.Clamp01(reviveHoldT / Rules.reviveHoldSeconds) : 1f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static bool QaHoldRevive;          // 自動テスト: 押し続ける
    public static float QaHoldReleaseAt = -1f; // 自動テスト: この進み(0〜1)で離す(取り消しの確認)
    public static bool QaHoldIgnoreBlock;     // 自動テスト: 画面で「出来ない」時も長押しを最後まで進めて HOST に送る(HOST 側で断られるかの確認)
#endif
    public int ReviveHoldsStarted, ReviveHoldsCancelled, ReviveHoldsCompleted; // 確認用

    void UpdateReviveHold(Rect buttonScreenRect, bool pointerOverButton)
    {
        Rec me = Get(NetCombat.LocalPlayerNumber);
        bool down = Mode == MultiplayerGameMode.Coop && me != null && me.State == PState.Down && !runOver;
        if (!down) { reviveHoldT = 0f; reviveHolding = false; reviveSent = false; return; }
        if (reviveSent && Time.realtimeSinceStartup - reviveSentAt < 1.5f) return; // HOST の返事待ち(届かなければまた押せる)
        reviveSent = false;
        bool possible = CanRevive(me, out _, out _, out _); // 出来ない間(FRONT の HP が足りない等)は進めない。HOST も同じ判定で断る
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (QaHoldIgnoreBlock) possible = true;
#endif
        if (!possible) { if (reviveHolding) ReviveHoldsCancelled++; reviveHolding = false; reviveHoldT = 0f; return; }
        bool held = GameInput.ReviveHeld || (pointerOverButton && (Input.GetMouseButton(0) || Input.touchCount > 0));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (QaHoldRevive) held = QaHoldReleaseAt < 0f || ReviveHoldProgress < QaHoldReleaseAt;
#endif
        if (held)
        {
            if (!reviveHolding) { reviveHolding = true; ReviveHoldsStarted++; }
            reviveHoldT += Time.unscaledDeltaTime;
            if (reviveHoldT >= Rules.reviveHoldSeconds)
            {
                ReviveHoldsCompleted++;
                reviveHoldT = 0f; reviveHolding = false; reviveSent = true; reviveSentAt = Time.realtimeSinceStartup;
                RequestRevive(NetCombat.LocalPlayerNumber);
            }
        }
        else
        {
            if (reviveHolding) { ReviveHoldsCancelled++; Log($"REVIVE hold cancelled at {ReviveHoldProgress * 100f:F0}% (no HP moved)"); }
            reviveHolding = false; reviveHoldT = 0f;
        }
    }

    // ===================================================================== //
    // 脱出の通知
    // ===================================================================== //

    bool escapeReported;
    void ReportLocalEscape(GameManager gm)
    {
        if (escapeReported || gm == null || !gm.EscapeConfirmed) return;
        escapeReported = true;
        double d = gm.EscapeConfirmedDistance;
        if (NetCombat.Authority) HostMarkEscaped(Get(NetCombat.LocalPlayerNumber), d);
        else SendToHost(w => { w.WriteValueSafe(ReqEscaped); w.WriteValueSafe(d); });
        Log($"ESCAPE reported at {d:F1}m");
    }

    void HostMarkEscaped(Rec rec, double distance)
    {
        if (rec == null || rec.State == PState.Escaped || rec.State == PState.Out || rec.State == PState.Eliminated) return;
        rec.State = PState.Escaped;
        rec.FinalDistance = System.Math.Max(rec.Distance, distance);
        dirty = true;
        Log($"P{rec.Pn} ESCAPED FinalDistance={rec.FinalDistance:F1}m (no longer FRONT/alive)");
        SendTable();
    }

    // ===================================================================== //
    // VERSUS: 死神の乱入時間(HOST)
    // ===================================================================== //

    float runStartRt = -1f;       // HOST: 走り始め(Running)の実時間
    float reaperLeft = -1f;       // HOST が配る残り秒(-1 = 時間の乱入なし/乱入済み)
    public bool ReaperTriggered { get; private set; }
    public float ReaperLeft => reaperLeft;
    public int ReaperSeconds => reaperSecondsCached;
    int reaperSecondsCached = -1;
    public string ReaperTriggerSource { get; private set; } = "";
    int lastWarnShown = int.MaxValue; float warnShownAt = -99f; int warnValue;

    int ActiveReaperSeconds()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        int qa = MultiplayerRules.QaReaperSeconds;
        if (qa > 0) return qa;
#endif
        return NetRunLauncher.ActiveReaperMinutes > 0 ? NetRunLauncher.ActiveReaperMinutes * 60 : -1;
    }

    void UpdateReaperHost()
    {
        reaperSecondsCached = Mode == MultiplayerGameMode.Versus ? ActiveReaperSeconds() : -1;
        if (runStartRt < 0f && NetRunLauncher.RunState >= NetRunState.Running) runStartRt = Time.realtimeSinceStartup;
        bool triggered = WorldProgress.IsTriggered("death_100k");
        if (triggered && !ReaperTriggered) OnReaperTriggered(ReaperTriggerSource == "" ? "100,000m" : ReaperTriggerSource);
        if (reaperSecondsCached <= 0 || runStartRt < 0f || ReaperTriggered || runOver) { reaperLeft = -1f; return; }
        float elapsed = Time.realtimeSinceStartup - runStartRt;
        float left = reaperSecondsCached - elapsed;
        float prev = reaperLeft;
        reaperLeft = Mathf.Max(0f, left);
        if (Mathf.Abs(Mathf.Ceil(prev) - Mathf.Ceil(reaperLeft)) >= 1f) dirty = true;
        if (left <= 0f)
        {
            var bm = BossManager.Instance;
            if (bm != null && bm.TryForceReaperByTime())
            {
                ReaperTriggerSource = $"time {reaperSecondsCached}s";
                OnReaperTriggered(ReaperTriggerSource);
            }
        }
    }

    // 乱入した瞬間(時間/100,000m のどちらでも1回): その時に生き残っている人に REAPER BONUS の資格
    void OnReaperTriggered(string source)
    {
        if (ReaperTriggered) return;
        ReaperTriggered = true;
        reaperLeft = -1f;
        int n = 0;
        foreach (var r in recs.Values) if (r.State == PState.Alive) { r.ReaperReached = true; n++; }
        dirty = true;
        Log($"REAPER TRIGGERED by {source}: {n} alive player(s) reached the reaper phase");
        SendTable();
    }

    // 予告(両端末): 残り 60/30/10 秒になった時に数秒だけ
    void UpdateReaperWarnClient()
    {
        if (Mode != MultiplayerGameMode.Versus || reaperLeft < 0f || runOver) return;
        int[] marks = Rules.reaperWarnSeconds;
        if (marks == null) return;
        foreach (int m in marks)
        {
            if (reaperSecondsCached > 0 && m >= reaperSecondsCached) continue; // 全体より長い予告(試験の短い時間など)は出さない
            if (reaperLeft <= m && m < lastWarnShown)
            {
                lastWarnShown = m; warnShownAt = Time.realtimeSinceStartup; warnValue = m;
                Log($"REAPER APPROACHING warning {m}s (left {reaperLeft:F1}s)");
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.EnemyTelegraph);
                break;
            }
        }
    }
    public int ReaperWarningsShown => lastWarnShown == int.MaxValue ? 0 : 1;

    // ===================================================================== //
    // VERSUS: MILE ボーナス(HOST が割合を決めて RunOver に載せる。受け取った各端末が自分の MILE に掛ける)
    // ===================================================================== //

    public class VersusPay
    {
        public int Rank, Players;
        public float RankBonus, ReaperBonus;
        public bool Eligible;      // false = 切断/途中退出/脱出(脱出は通常の MILE を受け取る)
        public bool ReaperReached;
    }
    public VersusPay LocalPay { get; private set; }

    void ComputeVersusPay()
    {
        if (Mode != MultiplayerGameMode.Versus) return;
        int players = 0;
        foreach (var row in results) if (row.State != PState.Out) players++;
        foreach (var row in results)
        {
            Rec rec = Get(row.Pn);
            bool eligible = row.State == PState.Eliminated || row.State == PState.Alive || row.State == PState.Down;
            row.ReaperReached = rec != null && rec.ReaperReached;
            row.Eligible = eligible;
            row.RankBonus = eligible ? Rules.RankBonus(row.Rank, players) : 0f;
            row.ReaperBonus = eligible && row.ReaperReached ? Rules.reaperBonus : 0f;
            float total = Mathf.Min(Rules.maxTotalBonus, row.RankBonus + row.ReaperBonus);
            if (row.RankBonus + row.ReaperBonus > total) row.ReaperBonus = Mathf.Max(0f, total - row.RankBonus); // 上限は REAPER の方を削る
            Log($"VERSUS PAY P{row.Pn} rank {row.Rank}/{players} state={row.State} eligible={eligible} rankBonus=+{row.RankBonus * 100f:F1}% reaperBonus=+{row.ReaperBonus * 100f:F1}%");
        }
    }

    void TakeLocalPay()
    {
        LocalPay = null;
        if (Mode != MultiplayerGameMode.Versus) return;
        int local = NetCombat.LocalPlayerNumber;
        int players = 0;
        foreach (var row in results) if (row.State != PState.Out) players++;
        foreach (var row in results)
        {
            if (row.Pn != local) continue;
            LocalPay = new VersusPay { Rank = row.Rank, Players = players, RankBonus = row.RankBonus, ReaperBonus = row.ReaperBonus, Eligible = row.Eligible, ReaperReached = row.ReaperReached };
        }
        if (LocalPay != null) Log($"VERSUS PAY (this device) rank {LocalPay.Rank}/{LocalPay.Players} eligible={LocalPay.Eligible} +{LocalPay.RankBonus * 100f:F1}% +REAPER {LocalPay.ReaperBonus * 100f:F1}%");
    }

    // ===================================================================== //
    // 毎フレーム(両端末)
    // ===================================================================== //

    void UpdateRules2(GameManager gm)
    {
        ReportLocalEscape(gm);
        // 長押しは Update で数える(画面の欄の場所は前のフレームの OnGUI が覚えた物。描画の無い試験でもキー/QA で進む)
        Rect rr = LastRevivePanelRect; bool over = false;
        if (rr.width > 0f)
        {
            Vector2 mp = Input.mousePosition; mp.y = Screen.height - mp.y;
            if (Input.GetMouseButton(0) && rr.Contains(mp)) over = true;
            for (int i = 0; i < Input.touchCount; i++) { Vector2 tp = Input.GetTouch(i).position; tp.y = Screen.height - tp.y; if (rr.Contains(tp)) over = true; }
        }
        UpdateReviveHold(rr, over);
        if (NetCombat.Authority) UpdateReaperHost();
        UpdateReaperWarnClient();
    }

    void OnSceneResetRules2()
    {
        runStartRt = -1f; reaperLeft = -1f; ReaperTriggered = false; ReaperTriggerSource = ""; reaperSecondsCached = -1;
        lastWarnShown = int.MaxValue; warnShownAt = -99f; reaperArrivedSeen = false; reaperArrivedShownAt = -99f;
        escapeReported = false; LocalPay = null;
        reviveHoldT = 0f; reviveHolding = false; reviveSent = false; lastReviveBlockAt = -99f;
        ReviveHoldsStarted = ReviveHoldsCancelled = ReviveHoldsCompleted = 0;
    }
}
