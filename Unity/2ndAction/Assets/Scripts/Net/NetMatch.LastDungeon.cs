using Unity.Netcode;
using UnityEngine;

// 2026-11-28 マスター依頼④(ラスダン): マルチでも 100,000m の死神三姉妹戦 → 全員クリア → エンドロール → ONE MORE MILE? を動かす。
//  ・三姉妹は HOST が動かし、参加者の端末へは既存のボスの同期(NetCombat、BossMethod.Sister)で人形として映す。
//    参加者の攻撃は既存の経路(NetApplyRemoteHit)で HOST へ届くので、最後の一撃が参加者でも撃破が成立する。
//  ・三姉妹の撃破(勝利)を HOST が1回だけ確定し(ldClearDeclared)、全員へ知らせる(SyncLdClear)。
//    対象 = ランに残っている人(生存・DOWN)。DOWN の人はその場で立ち上がってエンドロールを走る。
//    すでに脱出・脱落・退出した人はその結果のまま。各自の距離・MILE・能力は各自のデータ(エンドロールの記録・YES/NO の確定も各端末)。
//    VERSUS: 確定の時点の既存の順位判定(BuildResults と同じ並べ方)で各自の順位を決めて知らせる(全員1位にはしない)。
//  ・ONE MORE MILE? は HOST の YES/NO を全員へ(SyncLdChoice)。参加者の YES/NO の石は攻撃で壊れない(HOST の答えを待つ)。
//    HOST の答えが先に届いていれば、着いた時にその答えで進む。HOST との接続が無い時は自分で選べる。
public partial class NetMatch
{
    const byte SyncLdFinale = 11, SyncLdClear = 12, SyncLdChoice = 13, SyncReaperOut = 14; // 版 18

    bool ldFinaleSent, ldClearDeclared, ldClearApplied;
    public bool LdClearApplied => ldClearApplied;
    public static int LdClearRank { get; private set; }     // VERSUS: クリアの時点の自分の順位(0 = なし)
    public static int LdClearPlayers { get; private set; }
    public static int LdChoiceSent { get; private set; } = -1; // HOST: 送った答え(1 = YES / 0 = NO)
    public static string LdLocalOutcome { get; private set; } = "";

    void OnSceneResetLastDungeon()
    {
        ldFinaleSent = ldClearDeclared = ldClearApplied = false; ldHostCleared = true;
        LdClearRank = LdClearPlayers = 0; LdChoiceSent = -1; LdLocalOutcome = "";
    }

    // HOST: 三姉妹戦が始まった(LastDungeonFlow.OnReach100k)
    public static void HostSendLdFinale()
    {
        var m = Instance;
        if (m == null || !NetCombat.Authority || m.ldFinaleSent) return;
        m.ldFinaleSent = true;
        m.Log("LAST DUNGEON: sisters finale started -> clients");
        SendToClients(w => w.WriteValueSafe(SyncLdFinale));
    }

    // HOST: 三姉妹を全員倒した(勝利の演出の後、エンドロールの前)。true = この端末(HOST)もクリアとしてエンドロールへ進む
    public static bool HostDeclareLdClear()
    {
        var m = Instance;
        if (m == null || !NetCombat.Authority) return true;
        if (m.ldClearDeclared) return m.ldClearApplied;
        m.ldClearDeclared = true;
        // VERSUS の順位: 確定の時点(BuildResults と同じ: 脱落/脱出/退出 = 確定した距離、DOWN = 倒れた距離、走っている人 = 今の距離)
        m.BuildResults();
        int players = 0; foreach (var row in m.results) if (row.State != PState.Out) players++;
        var sb = new System.Text.StringBuilder();
        foreach (var r in m.recs.Values)
        {
            if (r.State == PState.Down) { r.State = PState.Alive; r.Hp = Mathf.Max(r.Hp, Rules.ReviveHp); sb.Append($" P{r.Pn}:Down->cleared"); }
            else if (r.State == PState.Alive) sb.Append($" P{r.Pn}:cleared");
            else sb.Append($" P{r.Pn}:{r.State}(kept)");
        }
        m.dirty = true;
        m.Log($"LAST DUNGEON CLEAR declared (mode={Mode}):{sb}");
        m.SendTable();
        var rows = m.results.ToArray();
        Rec hostRec = Get(NetCombat.LocalPlayerNumber);
        bool hostCleared = hostRec == null || hostRec.State == PState.Alive;
        // 2026-11-28(L3 で発見): 先に脱落していた人(VERSUS)は、クリアした人が NO でホームへ戻ると試合の終わりが来ず待ち続けた
        //  → クリアの時点の順位で、その人たちの試合の結果(順位・VERSUS の MILE ボーナス)を確定して送る。クリアした人の試合(エンドロール/BEYOND)は続く
        if (Mode == MultiplayerGameMode.Versus)
        {
            m.ComputeVersusPay();
            foreach (var row in rows)
            {
                if (row.State != PState.Eliminated) continue;
                Rec er = Get(row.Pn);
                if (er == null) continue;
                m.Log($"LAST DUNGEON CLEAR: P{row.Pn} was eliminated before the clear -> match result #{row.Rank}/{players} sent to that player");
                if (er.Pn == NetCombat.LocalPlayerNumber) { m.runOver = true; m.runOverAt = Time.realtimeSinceStartup + 1.5f; m.TakeLocalPay(); }
                else m.SendRunOverTo(er.ClientId);
            }
        }
        SendToClients(w =>
        {
            w.WriteValueSafe(SyncLdClear);
            w.WriteValueSafe(hostCleared); // HOST がエンドロールへ進まない(脱落していた)時は、参加者は自分で YES/NO を選ぶ
            w.WriteValueSafe((byte)players);
            w.WriteValueSafe((byte)rows.Length);
            foreach (var row in rows) { w.WriteValueSafe((byte)row.Pn); w.WriteValueSafe((byte)row.Rank); }
        });
        return m.ApplyLdClearLocal(players, rows, "host");
    }

    // 各端末: クリアを自分のランへ当てる。true = エンドロールへ進む(ランに残っていた)
    bool ApplyLdClearLocal(int players, ResultRow[] rows, string via)
    {
        if (ldClearApplied) return true;
        var gm = GameManager.Instance; var pc = PlayerController.Instance;
        Rec me = Get(NetCombat.LocalPlayerNumber);
        if (gm == null || pc == null || gm.IsGameOver || gm.EscapeConfirmed || (me != null && me.State != PState.Alive && me.State != PState.Down))
        {
            LdLocalOutcome = $"kept ({(me != null ? me.State.ToString() : "-")}{(gm != null && gm.IsGameOver ? ", run ended" : "")})";
            Log($"LAST DUNGEON CLEAR via {via}: {LdLocalOutcome}");
            return false;
        }
        ldClearApplied = true;
        if (Mode == MultiplayerGameMode.Versus && rows != null)
            foreach (var row in rows) if (row.Pn == NetCombat.LocalPlayerNumber) { LdClearRank = row.Rank; LdClearPlayers = players; }
        gm.NetCloseScreensForRunEnd("last dungeon clear");
        gm.DropPendingChoicesForFinale();
        GameManager.BlockExpGain = true;
        if (pc.NetIsDowned) { pc.NetRevive(); if (NetCombat.Authority) gm.NetApplyAuthoritativeLives(Mathf.Max(gm.Lives, Rules.ReviveHp), gm.MaxLives); }
        LdLocalOutcome = "cleared" + (LdClearRank > 0 ? $" (VERSUS #{LdClearRank}/{LdClearPlayers})" : "");
        Log($"LAST DUNGEON CLEAR via {via}: {LdLocalOutcome} d={gm.MaxDistance:F1} mile={gm.RunMile}");
        if (LdClearRank > 0) NetDebugUI.Toast($"VERSUS  #{LdClearRank} / {LdClearPlayers}");
        return true;
    }

    // HOST: ONE MORE MILE? の答え
    public static void HostSendLdChoice(bool yes)
    {
        if (Instance == null || !NetCombat.Authority || LdChoiceSent >= 0) return;
        LdChoiceSent = yes ? 1 : 0;
        Instance.Log($"LAST DUNGEON: ONE MORE MILE? HOST chose {(yes ? "YES" : "NO")} -> clients");
        SendToClients(w => { w.WriteValueSafe(SyncLdChoice); w.WriteValueSafe(yes); });
    }

    // 参加者の端末で YES/NO を自分で選べるか(HOST と繋がっている間は HOST の答えを待つ)
    public static bool LdChoiceFollowsHost => Active && NetCombat.Replica && NetSession.IsConnected && ldHostCleared;
    static bool ldHostCleared = true;

    // HOST: 死神に倒されて復活できない脱落になった人を全員へ(本人と仲間の画面に出す)
    void HostAnnounceReaperOut(Rec rec)
    {
        SendToClients(w => { w.WriteValueSafe(SyncReaperOut); w.WriteValueSafe((byte)rec.Pn); });
        OnReaperOutLocal(rec.Pn);
    }

    void OnReaperOutLocal(int pn)
    {
        var r = Get(pn);
        if (r != null) r.ReaperFatal = true;
        bool self = pn == NetCombat.LocalPlayerNumber;
        string text = self ? (Loc.Ready(ReaperOutSelfKey) ? Loc.T(ReaperOutSelfKey) : "Defeated by the Reaper - no revive")
                           : (Loc.Ready(ReaperOutOtherKey) ? Loc.F(ReaperOutOtherKey, NetPlayer.TagOf(pn)) : $"{NetPlayer.TagOf(pn)}: defeated by the Reaper - no revive");
        Log($"REAPER OUT P{pn} (no revive) shown on this device{(self ? " (self)" : "")}");
        NetDebugUI.Toast(text);
    }
    public const string ReaperOutSelfKey = "死神に倒されました(復活できません)";       // ID 11125
    public const string ReaperOutOtherKey = "{0}が死神に倒されました(復活できません)"; // ID 11126
    public const string LdWaitHostKey = "HOSTの選択を待っています";                    // ID 11124

    // JOIN: 受け取り
    void OnLastDungeonMessage(byte op, FastBufferReader r)
    {
        switch (op)
        {
            case SyncLdFinale:
                Log("LAST DUNGEON: sisters finale started (from HOST)");
                if (LastDungeonFlow.Instance != null) LastDungeonFlow.Instance.NetFinaleStarted();
                break;
            case SyncLdClear:
            {
                r.ReadValueSafe(out bool hostCleared); ldHostCleared = hostCleared;
                r.ReadValueSafe(out byte players); r.ReadValueSafe(out byte n);
                var rows = new ResultRow[n];
                for (int i = 0; i < n; i++) { r.ReadValueSafe(out byte pn); r.ReadValueSafe(out byte rank); rows[i] = new ResultRow { Pn = pn, Rank = rank }; }
                if (ApplyLdClearLocal(players, rows, "message") && LastDungeonFlow.Instance != null) LastDungeonFlow.Instance.NetClear();
                break;
            }
            case SyncLdChoice:
            {
                r.ReadValueSafe(out bool yes);
                Log($"LAST DUNGEON: HOST chose {(yes ? "YES" : "NO")} (from HOST)");
                if (LastDungeonFlow.Instance != null) LastDungeonFlow.Instance.NetHostChoice(yes);
                break;
            }
            case SyncReaperOut:
            {
                r.ReadValueSafe(out byte pn);
                OnReaperOutLocal(pn);
                break;
            }
        }
    }
}
