using Unity.Netcode;
using UnityEngine;

// 協力マルチ(2026-11-28、マスター決定)
// ① 大きく遅れた人の合流補助(HOST が決める。VERSUS には使わない)
//  ・活動中の先頭(生きていて選択/一時停止でない一番前の人)から CatchupGap(仮 500m)以上遅れた人を、安全な位置へ移す。
//    速さの差(カードの速度ダウン等)で開いた差も対象(カード選択の間に開いた差だけを戻す既存の復帰 HostEvaluateRejoin とは別)。
//  ・先頭がボス戦で止まっている時は、その戦闘の並び(共通の止まる位置)の少し後ろ = 着けばそのまま並んで一緒に戦える所。それ以外は先頭の CatchupBehind 後ろ。
//  ・足場の安全確認・移動・移動後の短い保護は既存の復帰と同じ(RejoinSafety.FindSpot / PlayerController.NetRejoinTo / ReportRejoinDone)。
//    移した距離は距離の除外へ(個人距離・距離の MILE・EXP・累計・BEST・解放に入らない。関門は先頭の距離で決まるので二重にならない。
//    チーム距離は先頭の値なので、先頭より後ろへ移す限り増えない)。
//  ・生きている人だけ(DOWN/脱出/退出は動かさない)。カード選択中・一時停止中は動かさず、閉じてから(ChoiceEndedAt + 2秒)判定する。
//  ・同じ人は CatchupCooldown 秒に1回まで(移動だけを繰り返していないかは Catchups と記録で確かめる)。
// ② 退出/切断が確定した人を、それ以降の MILE の分配の人数から外す(すでに配った分は計算し直さない: GameManager.MpReduceShare)。
//    DOWN・カード選択・一時停止・通信の遅れだけでは外さない(状態が Out になった時だけ。Out は切断の確定で付く)。
//    撃破の報酬は HOST が撃破を確定した時の人数を知らせに載せる(NetCombat、通信の版 17)ので、退出と重なっても全員が同じ人数で受け取る。
public partial class NetMatch
{
    public static float CatchupGap = 500f;       // 仮の調整値(開発版は -coopCatchupGap <m>、0 で止める)
    public const float CatchupBehind = 30f;      // 先頭のこれだけ後ろへ
    public const float CatchupCooldown = 12f;    // 同じ人への合流補助の間隔(秒)
    public const string RejoinedToast = "仲間に合流しました"; // ID 11118(Tools/loc/keys_extra49.tsv、エステルのレビュー待ち。訳が入るまでは日本語以外では出さない)
    public static int CatchupOrders { get; private set; } // 確認用
    public static int CatchupSkipped { get; private set; }
    float catchupTick;
    static bool catchupArgsRead;

    static void ReadCatchupArgs()
    {
        catchupArgsRead = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-coopCatchupGap" && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float g)) { CatchupGap = g; Debug.Log($"[NET][CATCHUP] gap = {g:F0}m (dev arg)"); }
#endif
    }

    void UpdateCoopCatchup()
    {
        if (!catchupArgsRead) ReadCatchupArgs();
        if (!NetCombat.Authority || Mode != MultiplayerGameMode.Coop || RunOver || CatchupGap <= 0f) return;
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted) return;
        catchupTick -= Time.unscaledDeltaTime;
        if (catchupTick > 0f) return;
        catchupTick = 1f;
        var now = AllLogicalX();
        // 活動中の先頭
        int fpn = 0; float fx = float.MinValue;
        foreach (var kv in now)
        {
            var r = Get(kv.Key);
            if (r == null || r.State != PState.Alive || r.Choosing) continue;
            if (kv.Value > fx) { fx = kv.Value; fpn = kv.Key; }
        }
        if (fpn == 0) return;
        float t = Time.realtimeSinceStartup;
        var bm = BossManager.Instance;
        bool bossLine = bm != null && bm.HoldsRun && BossHalt.SharedStop && NetCombat.HoldAnchorLX > 0.0;
        foreach (var kv in now)
        {
            if (kv.Key == fpn) continue;
            var rec = Get(kv.Key);
            if (rec == null || rec.State != PState.Alive || rec.Choosing) continue;
            if (t - rec.ChoiceEndedAt < 2f || t - rec.LastCatchupAt < CatchupCooldown) continue;
            float meX = kv.Value, gap = fx - meX;
            if (gap < CatchupGap) continue;
            // 移す先(論理X): ボス戦の並びの少し後ろ(着けば並んで止まる)/ 先頭の後ろ
            float targetLogical = bossLine ? (float)NetCombat.HoldAnchorLX - BossHalt.LineSpacing * 8f - 6f : fx - CatchupBehind;
            float sceneTarget = targetLogical - (float)FloatingOrigin.Offset;
            float sceneFloor = Mathf.Max(targetLogical - 80f, meX + 100f) - (float)FloatingOrigin.Offset; // 100m より短い移動はしない
            if (sceneFloor > sceneTarget) continue;
            float? spot = RejoinSafety.FindSpot(sceneTarget, sceneFloor, out string why);
            if (!spot.HasValue)
            {
                CatchupSkipped++; rec.LastCatchupAt = t - CatchupCooldown + 3f; // 3秒後にもう一度探す
                Log($"[CATCHUP] P{rec.Pn} {gap:F0}m behind P{fpn} - no safe footing near {targetLogical:F0} ({why}) -> retry later");
                continue;
            }
            float finalLogical = FloatingOrigin.ToLogical(spot.Value);
            int serial = ++rejoinSerial;
            CatchupOrders++; rec.Catchups++; rec.LastCatchupAt = t;
            Log($"[CATCHUP] P{rec.Pn} {gap:F0}m behind P{fpn}{(bossLine ? " (boss fight: to the line)" : "")} -> place at {finalLogical:F0} (moved {finalLogical - meX:F0}m, #{rec.Catchups}) serial {serial}");
            if (rec.Pn == NetCombat.LocalPlayerNumber)
            {
                var pc = PlayerController.Instance;
                if (pc != null) pc.NetRejoinTo(spot.Value, serial);
            }
            else
            {
                byte pn = (byte)rec.Pn;
                SendToClients(w => { w.WriteValueSafe(SyncRejoin); w.WriteValueSafe(pn); w.WriteValueSafe(finalLogical); w.WriteValueSafe(serial); }, rec.ClientId);
            }
        }
    }

    // ② 分配の人数(HOST も参加者も、HOST の表の状態から): 退出/切断が確定した人(Out)を除いた人数まで減らす(増やさない)
    void TickCoopShare(GameManager gm)
    {
        if (Mode != MultiplayerGameMode.Coop || gm == null || !gm.HasStarted || gm.IsGameOver || !MpRun.On) return;
        int n = 0, outN = 0;
        foreach (var r in recs.Values) { if (r.State == PState.Out) outN++; else n++; }
        if (outN == 0 || n <= 0 || n >= gm.MileShareCount) return;
        gm.MpReduceShare(n, $"{outN} player(s) left/disconnected");
    }
}
