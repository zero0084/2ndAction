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
//  - 復活 = 「DOWNした人の地点(DownDistance)以上にいる、HP2以上の生存プレイヤー」がHPを1つ渡す。
//    先行プレイヤーが倒れたら、後続はその地点まで走って行かないと助けられない。
//    判定(CanRevive)と実行(ExecuteRevive)は分離し、実行は助ける側が選ぶ(REVIVEボタン)。
//    Donor HP-1 / Down HP=1。復活はDOWNした地点(その場)で、既存の復帰(Recovery)の無敵だけを使う。
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
            if (r.Pn == NetCombat.LocalPlayerNumber || r.State == PState.Out || r.State == PState.Eliminated) continue;
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

    // 1秒ごとに、DOWN中の人ごとの復活判定を記録する(テスト/デバッグ用。成立の瞬間は必ず記録)。
    void LogReviveChecks()
    {
        if (Mode != MultiplayerGameMode.Coop) return;
        reviveLogTimer += Time.unscaledDeltaTime;
        bool periodic = reviveLogTimer >= 1f;
        if (periodic) reviveLogTimer = 0f;
        foreach (var down in recs.Values)
        {
            if (down.State != PState.Down) continue;
            foreach (var donor in recs.Values)
            {
                if (donor == down) continue;
                bool ok = CanRevive(down, donor, out string why);
                long key = ((long)down.Pn << 16) | (uint)donor.Pn;
                bool was = reviveAvailLogged.TryGetValue(key, out bool w) && w;
                if (ok != was) { reviveAvailLogged[key] = ok; Log($"REVIVE {(ok ? "AVAILABLE" : "unavailable")} down=P{down.Pn} donor=P{donor.Pn} {why}"); }
                else if (periodic) Log($"revive check down=P{down.Pn} donor=P{donor.Pn} -> {(ok ? "YES" : "NO")} {why}");
            }
        }
    }

    // ===================================================================== //
    // 復活(CO-OP)
    // ===================================================================== //

    // 判定のみ(状態は変えない)。why = 理由/数値(ログ/表示用)。
    public static bool CanRevive(Rec down, Rec donor, out string why)
    {
        why = "";
        if (Mode != MultiplayerGameMode.Coop) { why = "not CO-OP"; return false; }
        if (down == null || donor == null) { why = "no player"; return false; }
        if (down.State != PState.Down) { why = $"P{down.Pn} is {down.State}"; return false; }
        if (donor.State != PState.Alive) { why = $"donor P{donor.Pn} is {donor.State}"; return false; }
        double gap = down.DownDistance - donor.Distance;
        string nums = $"DownDistance={down.DownDistance:F1}m donorDistance={donor.Distance:F1}m donorHp={donor.Hp}";
        if (donor.Hp < 2) { why = $"donor HP {donor.Hp} < 2 (cannot give the last HP) {nums}"; return false; }
        if (gap > 0) { why = $"donor is {gap:F1}m behind the down point {nums}"; return false; }
        why = nums;
        return true;
    }

    // 助ける側の端末から呼ぶ(REVIVEボタン/自動テスト)。
    public static void RequestRevive(int downPn)
    {
        if (Instance == null || !Active) return;
        if (NetCombat.Authority) Instance.HostHandleReviveRequest(NetCombat.LocalPlayerNumber, downPn);
        else SendToHost(w => { w.WriteValueSafe(ReqRevive); w.WriteValueSafe((byte)downPn); });
        Instance.Log($"REVIVE requested by P{NetCombat.LocalPlayerNumber} for P{downPn}");
    }

    void HostHandleReviveRequest(int requesterPn, int downPn)
    {
        Rec down = Get(downPn), donor = Get(requesterPn);
        if (!CanRevive(down, donor, out string why))
        {
            ReviveRejected++;
            Log($"REVIVE REJECTED down=P{downPn} donor=P{requesterPn}: {why}");
            return;
        }
        ExecuteRevive(down, donor);
    }

    // 実行(HOSTのみ)。Donor HP-1、Down HP=1、DownはDOWNした地点で復帰する。
    void ExecuteRevive(Rec down, Rec donor)
    {
        GameManager gm = GameManager.Instance;
        int local = NetCombat.LocalPlayerNumber;
        int donorBefore = donor.Hp;
        donor.Hp -= 1;
        down.Hp = 1;
        down.State = PState.Alive;
        down.InvulnUntil = Time.realtimeSinceStartup + 1.5f;
        if (gm != null)
        {
            if (donor.Pn == local) gm.NetSetLocalLives(donor.Hp);
            if (down.Pn == local) gm.NetSetLocalLives(1);
        }
        ReviveCount++;
        Log($"REVIVE EXECUTED down=P{down.Pn} donor=P{donor.Pn}: donor hp {donorBefore} -> {donor.Hp}, down hp 0 -> 1, at DownDistance={down.DownDistance:F1}m (donorDistance={donor.Distance:F1}m)");
        SendToClients(w => { w.WriteValueSafe(SyncRevived); w.WriteValueSafe((byte)down.Pn); w.WriteValueSafe((byte)donor.Pn); });
        dirty = true;
        SendTable();
        if (down.Pn == local) ApplyLocalStatePhase3(gm, PlayerController.Instance);
    }

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
                gm.NetSetLocalLives(0);
            }
            else if (me.State == PState.Alive && pc.NetIsDowned)
            {
                pc.NetRevive();
                if (NetCombat.Replica) gm.NetApplyAuthoritativeLives(me.Hp, me.MaxHp, fromHit: true);
            }
            else if (me.State == PState.Out && NetCombat.Replica && !gm.IsGameOver) gm.NetForceGameOver("HP0(HOST)");
        }
        if (runOver && !gm.IsGameOver && runOverAt > 0f && Time.realtimeSinceStartup >= runOverAt)
            gm.NetForceGameOver(Mode == MultiplayerGameMode.Versus ? "VERSUS RUN OVER (last player out)" : "CO-OP RUN OVER (all players down)");
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
            double d = rec.State == PState.Eliminated || rec.State == PState.Out ? rec.FinalDistance
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
            results.Add(row);
        }
        runOver = true;
        runOverAt = Time.realtimeSinceStartup + 1.5f;
        OnRunFinished("RunOver from HOST");
        foreach (var row in results) Log($"RESULT #{row.Rank} P{row.Pn} {row.Distance:F1}m kills={row.Kills} bossLastHits={row.BossLastHits} state={row.State} (from HOST)");
        Log($"RUN OVER mode={Mode} (from HOST)");
    }

    void WritePhase3Header(FastBufferWriter w)
    {
        w.WriteValueSafe((byte)Mode);
        w.WriteValueSafe(runOver);
    }

    void ReadPhase3Header(FastBufferReader r)
    {
        r.ReadValueSafe(out byte mode);
        r.ReadValueSafe(out bool over);
        if ((MultiplayerGameMode)mode != Mode) Log($"WARNING mode mismatch: HOST={(MultiplayerGameMode)mode} local={Mode} (HOST wins)");
        NetRunLauncher.ForceActiveMode((MultiplayerGameMode)mode);
    }

    // ===================================================================== //
    // 簡易HUD(CO-OP: ALLY DOWN / DISTANCE TO ALLY / REVIVE、VERSUS: 脱落/結果)
    // ===================================================================== //

    GUIStyle hudStyle, hudBig, hudButton;

    void OnGUI()
    {
        if (!Active) return;
        GameManager gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted) return;
        if (hudStyle == null)
        {
            hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, richText = true, wordWrap = true };
            hudStyle.normal.textColor = Color.white;
            hudBig = new GUIStyle(hudStyle) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
            hudButton = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
        }
        float s = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 720f);
        Matrix4x4 prev = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
        float w = Screen.width / s, h = Screen.height / s;
        GUI.depth = -900;

        int local = NetCombat.LocalPlayerNumber;
        Rec me = Get(local);
        var sb = new StringBuilder();
        sb.Append(Mode == MultiplayerGameMode.Coop ? "<color=#8fe3ff>CO-OP</color>" : "<color=#ffb070>VERSUS</color>");
        if (me != null) sb.Append($"  P{local} HP {me.Hp}/{me.MaxHp}  {me.State}");
        int reviveTarget = 0; string reviveText = null;
        foreach (var r in recs.Values)
        {
            if (r.Pn == local) continue;
            sb.Append($"\nP{r.Pn}: {r.State} HP {r.Hp}  {(r.State == PState.Down ? r.DownDistance : r.State == PState.Eliminated ? r.FinalDistance : r.Distance):F0}m");
            if (Mode == MultiplayerGameMode.Coop && r.State == PState.Down)
            {
                sb.Append("\n<color=#ff8080>ALLY DOWN</color>");
                if (me != null && me.State == PState.Alive)
                {
                    double gap = r.DownDistance - me.Distance;
                    sb.Append(gap > 0 ? $"\nDISTANCE TO ALLY: {gap:F0}m" : $"\nALLY IS {-gap:F0}m BEHIND YOU");
                    if (CanRevive(r, me, out string why)) { reviveTarget = r.Pn; reviveText = $"REVIVE AVAILABLE  Donor HP = {me.Hp}"; }
                    else if (me.Hp < 2) sb.Append($"\nREVIVE: NOT POSSIBLE (Donor HP = {me.Hp}, need 2+)");
                }
            }
        }
        if (me != null && me.State == PState.Down)
            sb.Append($"\n<color=#ff8080>YOU ARE DOWN at {me.DownDistance:F0}m</color> - waiting for an ally with HP 2+ to reach this point");
        if (me != null && me.State == PState.Eliminated)
            sb.Append($"\n<color=#ff8080>ELIMINATED</color>  FinalDistance = {me.FinalDistance:F0}m");
        if (reviveText != null) sb.Append($"\n<color=#80ff80>{reviveText}</color>");
        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        float py = h * 0.30f;
        GUI.DrawTexture(new Rect(10f, py, 440f, 170f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(18f, py + 4f, 430f, 170f), sb.ToString(), hudStyle);

        if (reviveTarget > 0 && !runOver)
        {
            if (GUI.Button(new Rect(18f, py + 178f, 240f, 60f), $"REVIVE P{reviveTarget}", hudButton)) RequestRevive(reviveTarget);
        }

        if (runOver && results.Count > 0)
        {
            float pw = 460f, ph = 90f + results.Count * 40f;
            Rect panel = new Rect((w - pw) * 0.5f, h * 0.18f, pw, ph);
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(panel.x, panel.y + 8f, pw, 40f), Mode == MultiplayerGameMode.Versus ? "VERSUS RESULT" : "CO-OP RESULT", hudBig);
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                GUI.Label(new Rect(panel.x + 30f, panel.y + 56f + i * 40f, pw - 60f, 40f),
                    $"{r.Rank}.  P{r.Pn}   {r.Distance:N0}m   KILL {r.Kills}   BOSS {r.BossLastHits}", hudStyle);
            }
        }
        GUI.matrix = prev;
    }
}
