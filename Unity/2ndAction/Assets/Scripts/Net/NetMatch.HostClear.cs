using Unity.Netcode;
using UnityEngine;

// 2026-11-28 マスター依頼④(決定: HOST の脱出成功 = マルチのクリア): CO-OP・VERSUS とも、HOST の脱出の成功が確定した時点で、
//  同じランを続けている全員(後ろを走っている・ボス戦中・カード選択中・一時停止中・DOWN で救助待ち)をクリア(脱出成功)にする。
//  ・確定は HOST が1回だけ(hostClearDeclared)。HOST の表で対象の人を Escaped にし(その時の HOST の知っている距離 = 終了時点の順位判定)、
//    全員へ知らせる(SyncHostClear)。届かなかった/遅れた時も、状態の表で自分が Escaped になっていれば同じ処理をする(どちらが先でも1回だけ)。
//  ・各端末は自分の記録(個人距離・取得能力・MILE)のまま脱出成功として終える(GameManager.TryConfirmEscape → 終わりの動き → Win)。
//    開いている画面(カード選択・一時停止)は閉じてから。DOWN の人は終わりの動きを飛ばしてそのまま RESULT へ。
//  ・すでに脱出・脱落した人、退出した人は、その結果のまま(Escaped / Eliminated / Out は触らない)。
//  ・JOIN の脱出は今までどおり本人だけ。
public partial class NetMatch
{
    const byte SyncHostClear = 10;         // HOST → 全員: HOST の脱出成功でランをクリア(版 18)

    bool hostClearDeclared;                // HOST: 確定した
    bool hostClearApplied;                 // この端末: 自分のランへ当てた(または当てる必要がなかった)
    public bool HostClearDeclared => hostClearDeclared;
    public static int HostClearPlayersMarked { get; private set; } // 確認用: HOST が Escaped にした人数
    public static string HostClearLocalOutcome { get; private set; } = ""; // 確認用: この端末でどうなったか
    public const string HostClearToast = "HOSTが脱出に成功しました"; // ID 11123(Tools/loc/keys_extra50.tsv)

    void OnSceneResetHostClear()
    {
        hostClearDeclared = false; hostClearApplied = false;
        HostClearPlayersMarked = 0; HostClearLocalOutcome = "";
    }

    // HOST: 自分の脱出の成功が確定した直後(ReportLocalEscape から)
    void HostDeclareClear()
    {
        if (!NetCombat.Authority || hostClearDeclared || runOver) return;
        hostClearDeclared = true;
        int local = NetCombat.LocalPlayerNumber, n = 0;
        var sb = new System.Text.StringBuilder();
        foreach (var r in recs.Values)
        {
            if (r.Pn == local) continue;
            if (r.State != PState.Alive && r.State != PState.Down) { sb.Append($" P{r.Pn}:{r.State}(kept)"); continue; }
            var was = r.State;
            r.FinalDistance = was == PState.Down ? r.DownDistance : r.Distance;
            r.State = PState.Escaped;
            n++;
            sb.Append($" P{r.Pn}:{was}{(r.Choosing ? "+choosing" : "")}->Escaped@{r.FinalDistance:F0}");
        }
        HostClearPlayersMarked = n;
        hostClearApplied = true; HostClearLocalOutcome = "host";
        dirty = true;
        Log($"HOST CLEAR declared (mode={Mode}): the HOST escaped -> {n} running player(s) cleared:{sb}");
        SendTable();
        SendToClients(w => w.WriteValueSafe(SyncHostClear));
    }

    // JOIN: HOST からの知らせ、または状態の表で自分が Escaped になった(自分で脱出していない)
    void ApplyHostClearLocal(string via)
    {
        if (hostClearApplied || NetCombat.Authority) return;
        var gm = GameManager.Instance; var pc = PlayerController.Instance;
        if (gm == null || pc == null || !gm.HasStarted) return;
        Rec me = Get(NetCombat.LocalPlayerNumber);
        hostClearApplied = true;
        if (me != null && (me.State == PState.Eliminated || me.State == PState.Out)) { HostClearLocalOutcome = $"kept {me.State}"; Log($"HOST CLEAR via {via}: this player is {me.State} -> result kept"); return; }
        if (gm.EscapeConfirmed || gm.IsGameOver) { HostClearLocalOutcome = gm.EscapeConfirmed ? "already escaped" : "already finished"; Log($"HOST CLEAR via {via}: this run already {(gm.EscapeConfirmed ? "escaped" : "ended")} -> nothing to do"); return; }
        bool down = pc.NetIsDowned, choosing = gm.IsLocalNetHoldOpen;
        gm.NetCloseScreensForRunEnd("HOST cleared the run");
        bool ok = pc.NetHostClearFinish();
        HostClearLocalOutcome = ok ? (down ? "cleared from DOWN" : choosing ? "cleared from a choice/pause" : "cleared") : "not cleared";
        Log($"HOST CLEAR via {via}: {HostClearLocalOutcome} (d={gm.MaxDistance:F1} lives={gm.Lives} mile={gm.RunMile})");
        if (ok) NetDebugUI.Toast(Loc.Ready(HostClearToast) ? Loc.T(HostClearToast) : "HOST ESCAPED");
    }

    // 状態の表を受け取るたび(ApplyLocalStatePhase3 から): 自分が Escaped なのに自分は脱出していない = HOST のクリア
    void CheckHostClearFromTable(GameManager gm, Rec me)
    {
        if (NetCombat.Authority || hostClearApplied || me == null || me.State != PState.Escaped || gm == null) return;
        if (gm.EscapeConfirmed || gm.IsGameOver) return;
        ApplyHostClearLocal("table");
    }
}
