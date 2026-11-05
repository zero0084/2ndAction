using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// マルチプレイPhase 3(2026-09-27) - CO-OP / VERSUS のゲームルール。
//
// モードはHOSTが選び、Run開始の通知(NetRunLauncher)に載せて全員へ配る = セッション全体の正解。
// 共通: 同じステージ/敵/ボス/HP/ラストヒット。報酬はラストヒット本人だけ(既存ルールのまま)。
//
// CO-OP:
//  - HP0 → DOWN(その瞬間の距離をDownDistanceとして固定)。DOWN中は操作不可・狙われない・被弾しない・
//    攻撃しない・走行停止。ネットワーク上のプレイヤー(NetPlayer)は消さない。
//  - 復活(2026-11-05 変更): DOWNした本人が長押し → 長押しが終わった瞬間にHOSTが「一番前の生存プレイヤー(FRONT)」を選び、
//    FRONTのHPが2以上ならHPを1もらってHP1で復活(NetMatch.Rules2)。復活はDOWNした地点(その場)。
//  - 全員がDOWNしたらRun終了。
// VERSUS:
//  - HP0 → ELIMINATED(復活なし)。その瞬間の距離をFinalDistanceとして固定。
//  - 最後の1人が脱落するまでRunは続く。結果は脱落順ではなくFinalDistanceの大きい順。
//
// 判定はすべてHOSTが行い、状態表(Table)とRunOver(結果)を全員へ配る。
public partial class NetMatch
{
    public static MultiplayerGameMode Mode => NetRunLauncher.ActiveMode;

    public class ResultRow
    {
        public int Pn;
        public double Distance;
        public int Kills, BossLastHits;
        public PState State;
        public int Rank;
        // 2026-11-05 VERSUS の MILE ボーナス(HOST が決める)
        public bool Eligible, ReaperReached;
        public float RankBonus, ReaperBonus;
    }

    bool runOver;
    float runOverAt = -1f;
    readonly List<ResultRow> results = new List<ResultRow>();
    public bool RunOver => runOver;
    public IReadOnlyList<ResultRow> Results => results;
    public int ReviveCount, ReviveRejected;
    float reviveLogTimer;
    readonly Dictionary<long, bool> reviveAvailLogged = new Dictionary<long, bool>();

    void OnSceneResetPhase3()
    {
        runOver = false;
        runOverAt = -1f;
        results.Clear();
        ReviveCount = ReviveRejected = 0;
        reviveAvailLogged.Clear();
    }

    // ===================================================================== //
    // HP0の扱い(HOST)
    // ===================================================================== //

    bool HandleHpZeroPhase3(Rec rec, string source)
    {
        rec.Hp = 0;
        if (Mode == MultiplayerGameMode.Versus)
        {
            rec.State = PState.Eliminated;
            rec.FinalDistance = rec.Distance;
            Log($"P{rec.Pn} ELIMINATED (VERSUS) FinalDistance={rec.FinalDistance:F1}m src={source}");
        }
        else
        {
            rec.State = PState.Down;
            rec.DownDistance = rec.Distance;
            Log($"P{rec.Pn} DOWN (CO-OP) DownDistance={rec.DownDistance:F1}m src={source}");
        }
        dirty = true;
        SendTable();
        if (rec.Pn == NetCombat.LocalPlayerNumber) ApplyLocalStatePhase3(GameManager.Instance, PlayerController.Instance);
        return true;
    }

    // ===================================================================== //
    // 毎フレーム(HOST)
    // ===================================================================== //

    void UpdatePhase3Host(GameManager gm, PlayerController pc)
    {
        // 切断した人は以降のルール(ターゲット/復活/Run終了)から外す。
        NetworkManager nm = NetSession.Manager;
        foreach (var r in recs.Values)
        {
            if (r.Pn == NetCombat.LocalPlayerNumber || r.State == PState.Out || r.State == PState.Eliminated || r.State == PState.Escaped) continue;
            bool connected = false;
            if (nm != null) foreach (ulong id in nm.ConnectedClientsIds) if (id == r.ClientId) { connected = true; break; }
            if (!connected && r.Known)
            {
                if (r.FinalDistance <= 0) r.FinalDistance = r.State == PState.Down ? r.DownDistance : r.Distance;
                r.State = PState.Out;
                dirty = true;
                Log($"P{r.Pn} disconnected -> excluded (state=Out, distance={r.FinalDistance:F1}m)");
            }
        }

        ApplyLocalStatePhase3(gm, pc);
        LogReviveChecks();

        if (!runOver && recs.Count > 0)
        {
            bool anyAlive = false;
            foreach (var r in recs.Values) if (r.State == PState.Alive) { anyAlive = true; break; }
            if (!anyAlive) HostEndRun();
        }
    }

    void UpdatePhase3Client(GameManager gm, PlayerController pc)
    {
        ApplyLocalStatePhase3(gm, pc);
    }

    // 1秒ごとに、DOWN中の人ごとの復活判定(FRONT から HP をもらえるか)を記録する(テスト/デバッグ用。変化した時は必ず記録)。
    void LogReviveChecks()
    {
        if (Mode != MultiplayerGameMode.Coop) return;
        reviveLogTimer += Time.unscaledDeltaTime;
        bool periodic = reviveLogTimer >= 1f;
        if (periodic) reviveLogTimer = 0f;
        foreach (var down in recs.Values)
        {
            if (down.State != PState.Down) continue;
            bool ok = CanRevive(down, out Rec donor, out _, out string why);
            long key = ((long)down.Pn << 16) | (uint)(donor != null ? donor.Pn : 0);
            bool was = reviveAvailLogged.TryGetValue(key, out bool w) && w;
            if (ok != was || !reviveAvailLogged.ContainsKey(key)) { reviveAvailLogged[key] = ok; Log($"REVIVE {(ok ? "AVAILABLE" : "unavailable")} down=P{down.Pn} {why}"); }
            else if (periodic && recs.Count <= 2) Log($"revive check down=P{down.Pn} -> {(ok ? "YES" : "NO")} {why}");
        }
    }

    // 復活(CO-OP)は NetMatch.Rules2(2026-11-05: DOWN した本人の長押し + 完了した瞬間の FRONT から HP をもらう)

    void OnRevivedClient(byte downPn, byte donorPn)
    {
        Log($"REVIVE P{downPn} by P{donorPn} (from HOST)");
        ApplyLocalStatePhase3(GameManager.Instance, PlayerController.Instance);
    }

    // ===================================================================== //
    // 自分のプレイヤーへの反映(両端末)
    // ===================================================================== //

    bool ApplyLocalStatePhase3(GameManager gm, PlayerController pc)
    {
        if (gm == null || pc == null) return true;
        Rec me = Get(NetCombat.LocalPlayerNumber);
        if (me != null)
        {
            if ((me.State == PState.Down || me.State == PState.Eliminated) && !pc.NetIsDowned)
            {
                // 状態の優先順位(2026-09-28): Eliminated/Down > ChoosingCard - 表示中の選択を閉じてから倒れる。
                gm.NetCloseAllChoices(me.State == PState.Eliminated ? "local player ELIMINATED" : "local player DOWN", me.State == PState.Eliminated);
                pc.NetEnterDown(me.State == PState.Eliminated);
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.NetDown); // 2026-10-06
                gm.NetSetLocalLives(0);
            }
            else if (me.State == PState.Alive && pc.NetIsDowned)
            {
                pc.NetRevive();
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.NetRevive); // 2026-10-06
                if (NetCombat.Replica) gm.NetApplyAuthoritativeLives(me.Hp, me.MaxHp, fromHit: true);
            }
            else if (me.State == PState.Out && NetCombat.Replica && !gm.IsGameOver) gm.NetForceGameOver("HP0(HOST)");
        }
        if (runOver && !gm.IsGameOver && runOverAt > 0f && Time.realtimeSinceStartup >= runOverAt)
        {
            if (LocalPay != null && LocalPay.Eligible) gm.NetSetVersusPay(LocalPay.Rank, LocalPay.Players, LocalPay.RankBonus, LocalPay.ReaperBonus); // 試合の完了: 脱落までの MILE を持ち帰る + ボーナス
            gm.NetForceGameOver(Mode == MultiplayerGameMode.Versus ? "VERSUS RUN OVER (last player out)" : "CO-OP RUN OVER (all players down)");
        }
        return true;
    }

    // ===================================================================== //
    // Run終了と結果(HOSTが決めて配る)
    // ===================================================================== //

    void HostEndRun()
    {
        runOver = true;
        runOverAt = Time.realtimeSinceStartup + 1.5f; // 最後の被弾の演出を少し見せてから結果へ
        OnRunFinished("HOST: all players out");
        BuildResults();
        ComputeVersusPay();
        TakeLocalPay();
        foreach (var r in results) Log($"RESULT #{r.Rank} P{r.Pn} {r.Distance:F1}m kills={r.Kills} bossLastHits={r.BossLastHits} state={r.State}");
        Log($"RUN OVER mode={Mode} players={results.Count}");
        SendToClients(w =>
        {
            w.WriteValueSafe(SyncRunOver);
            w.WriteValueSafe((byte)results.Count);
            foreach (var r in results)
            {
                w.WriteValueSafe((byte)r.Pn); w.WriteValueSafe(r.Distance); w.WriteValueSafe(r.Kills); w.WriteValueSafe(r.BossLastHits);
                w.WriteValueSafe((byte)r.State); w.WriteValueSafe((byte)r.Rank);
                w.WriteValueSafe(r.Eligible); w.WriteValueSafe(r.ReaperReached); w.WriteValueSafe(r.RankBonus); w.WriteValueSafe(r.ReaperBonus);
            }
        });
        dirty = true;
        SendTable();
    }

    // RunState=Finished(2026-09-28): 結果を最優先にする - この端末の選択UI(レベルアップ/ボス報酬)を
    // すぐ閉じ、キュー中の選択も捨てる(RESULTの後に何も出さない)。
    void OnRunFinished(string reason)
    {
        NetRunLauncher.MarkFinished(reason);
        GameManager gm = GameManager.Instance;
        if (gm != null && gm.HasStarted) gm.NetCloseAllChoices("run finished: " + reason, true);
    }

    void BuildResults()
    {
        results.Clear();
        foreach (var rec in recs.Values)
        {
            double d = rec.State == PState.Eliminated || rec.State == PState.Out || rec.State == PState.Escaped ? rec.FinalDistance
                : rec.State == PState.Down ? rec.DownDistance : rec.Distance;
            results.Add(new ResultRow { Pn = rec.Pn, Distance = d, Kills = rec.Kills, BossLastHits = rec.BossLastHits, State = rec.State });
        }
        // 勝敗は生存順ではなく最終距離(同距離はプレイヤー番号の小さい方を上)。
        results.Sort((a, b) => a.Distance != b.Distance ? b.Distance.CompareTo(a.Distance) : a.Pn.CompareTo(b.Pn));
        for (int i = 0; i < results.Count; i++) results[i].Rank = i + 1;
    }

    void ReadRunOver(FastBufferReader r)
    {
        r.ReadValueSafe(out byte n);
        results.Clear();
        for (int i = 0; i < n; i++)
        {
            var row = new ResultRow();
            r.ReadValueSafe(out byte pn); row.Pn = pn;
            r.ReadValueSafe(out row.Distance); r.ReadValueSafe(out row.Kills); r.ReadValueSafe(out row.BossLastHits);
            r.ReadValueSafe(out byte st); row.State = (PState)st;
            r.ReadValueSafe(out byte rank); row.Rank = rank;
            r.ReadValueSafe(out row.Eligible); r.ReadValueSafe(out row.ReaperReached); r.ReadValueSafe(out row.RankBonus); r.ReadValueSafe(out row.ReaperBonus);
            results.Add(row);
        }
        runOver = true;
        runOverAt = Time.realtimeSinceStartup + 1.5f;
        TakeLocalPay();
        OnRunFinished("RunOver from HOST");
        foreach (var row in results) Log($"RESULT #{row.Rank} P{row.Pn} {row.Distance:F1}m kills={row.Kills} bossLastHits={row.BossLastHits} state={row.State} (from HOST)");
        Log($"RUN OVER mode={Mode} (from HOST)");
    }

    void WritePhase3Header(FastBufferWriter w)
    {
        w.WriteValueSafe((byte)Mode);
        w.WriteValueSafe(runOver);
        // 2026-10-11: 共有世界の進行距離と、起きた世界のイベント(全員で同じ状態にする)
        w.WriteValueSafe(WorldProgress.FrontHigh);
        w.WriteValueSafe(reaperLeft);        // 2026-11-05 VERSUS: 死神の乱入までの残り秒(-1 = なし/乱入済み)
        w.WriteValueSafe((short)reaperSecondsCached);
        var ev = WorldProgress.Triggered;
        w.WriteValueSafe((byte)Mathf.Min(255, ev.Count));
        int n = 0;
        foreach (var id in ev) { if (n++ >= 255) break; w.WriteValueSafe(new FixedString32Bytes(id)); }
    }

    void ReadPhase3Header(FastBufferReader r)
    {
        r.ReadValueSafe(out byte mode);
        r.ReadValueSafe(out bool over);
        r.ReadValueSafe(out double frontHigh);
        WorldProgress.SetFromHost(frontHigh);
        r.ReadValueSafe(out reaperLeft);
        r.ReadValueSafe(out short rsec); reaperSecondsCached = rsec;
        r.ReadValueSafe(out byte evn);
        for (int i = 0; i < evn; i++) { r.ReadValueSafe(out FixedString32Bytes id); WorldProgress.NoteFromHost(id.ToString()); }
        if ((MultiplayerGameMode)mode != Mode) Log($"WARNING mode mismatch: HOST={(MultiplayerGameMode)mode} local={Mode} (HOST wins)");
        NetRunLauncher.ForceActiveMode((MultiplayerGameMode)mode);
    }

    // 画面(参加者の欄・復活・予告・結果)は NetMatch.Hud(2026-11-05)
}
