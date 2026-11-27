using System.Collections.Generic;
using UnityEngine;

// マルチのボス戦の距離(2026-11-27、マスター要望「ボス戦は必ず 1000/5000/10000 の距離。ボス戦の後に距離がずれる」)
//  ・HOST がボス戦ごとに「関門に着いた所(最前の人の論理X)〜 全員が止まる共通の位置」の区間を決めて全員へ送る(NetCombat の OpBossHold、通信の版 16)。
//  ・この区間の中の移動は、誰の距離にも数えない(論理X で決まるので、先に着いた人も後から追いついた人も同じ)。
//    → 全員が関門の距離(例 4,000m)で戦い、ボスの後も同じ距離からそろって進む(以前は関門から止まるまでに進んだ分 0〜200m が人ごとに違って数えられていた)
//  ・距離の物差し(WorldRange: 他の人の距離 = 自分の距離 + 位置の差)も、2人の間にある区間の分を引く(チーム距離・敵の出現・次の関門が区間の分ずれない)
//  ・置き直し(協力の復帰)で区間を飛び越えた時は、置き直しの除外から区間の分を引く(二重に除かない)
//  ソロは今まで通り(関門で距離を止める BeginBossDistanceExclusion)。
public partial class GameManager
{
    static readonly List<Vector2d> mpBossSegs = new List<Vector2d>();
    static GameManager mpSegOwner;
    double mpSegApplied; // 距離の除外に入れた区間の分(= 区間の合計のうち、自分の位置より後ろの分)
    public static int MpBossSegmentCount { get { EnsureSegRun(); return mpBossSegs.Count; } }
    public double MpBossSkipped => mpSegApplied; // 確認用

    struct Vector2d { public double A, B; }

    static void EnsureSegRun()
    {
        if (mpSegOwner == Instance) return;
        mpSegOwner = Instance; mpBossSegs.Clear();
    }

    // ボス戦の始まり: 関門(a)から先は、ボス戦が終わるまで誰の距離にも数えない(終わりの無い区間。戦いの最中の踏み込み/ノックバックも数えない = ソロと同じ)
    public void MpOpenBossSegment(double a)
    {
        EnsureSegRun();
        if (!MpRun.On) return;
        foreach (var s in mpBossSegs)
        {
            if (System.Math.Abs(s.A - a) < 0.5) return; // 同じ遭遇の送り直し
            if (double.IsPositiveInfinity(s.B)) { Debug.LogWarning($"[Distance] boss segment at {a:F1} ignored: one is already open at {s.A:F1}"); return; } // 開いている区間は1つだけ
        }
        mpBossSegs.Add(new Vector2d { A = a, B = double.PositiveInfinity });
        mpSegJustAdded = true;
        TeamDistance.NoteBossSegmentOpened();
        Debug.Log($"[Distance] boss segment opened at {a:F1} - nobody's distance counts past the gate until the fight ends (multiplayer boss line)");
    }
    // ボス戦の終わり: HOST が決めた終わり(b = 止まる所と、終わった時の最前の人の大きい方)で閉じる。ここから先はまた数える
    public void MpCloseBossSegment(double a, double b)
    {
        EnsureSegRun();
        if (!MpRun.On) return;
        for (int i = 0; i < mpBossSegs.Count; i++)
        {
            var s = mpBossSegs[i];
            if (System.Math.Abs(s.A - a) >= 0.5 && !double.IsPositiveInfinity(s.B)) continue;
            if (!double.IsPositiveInfinity(s.B)) return; // 閉じ済み(送り直し)
            // 開いている区間は1つだけ = この遭遇のもの(始まりの位置が少し違っても閉じる。開いたままにしない)
            s.B = System.Math.Max(a, b); mpBossSegs[i] = s;
            Debug.Log($"[Distance] boss segment closed {s.A:F1}..{s.B:F1} ({s.B - s.A:F1}m not counted)");
            return;
        }
        // 始まりを受け取っていない(途中で入った等): 閉じた区間として足す
        mpBossSegs.Add(new Vector2d { A = a, B = System.Math.Max(a, b) });
        Debug.Log($"[Distance] boss segment {a:F1}..{b:F1} added closed (the start was not received)");
    }
    // 開いている区間を、この位置(論理X)で閉じる(HOST との接続が切れた時など)
    public void MpCloseOpenBossSegment(double lx)
    {
        EnsureSegRun();
        for (int i = 0; i < mpBossSegs.Count; i++)
        {
            if (!double.IsPositiveInfinity(mpBossSegs[i].B)) continue;
            var g = mpBossSegs[i]; g.B = System.Math.Max(g.A, lx); mpBossSegs[i] = g;
            Debug.Log($"[Distance] boss segment closed {g.A:F1}..{g.B:F1} (connection lost)");
        }
    }
    // 開いている区間(ボス戦の最中)があるか
    public static bool MpBossSegmentOpen { get { EnsureSegRun(); foreach (var s in mpBossSegs) if (double.IsPositiveInfinity(s.B)) return true; return false; } }

    // 論理X より後ろにある区間の長さの合計
    public static double MpSegOffset(double lx)
    {
        EnsureSegRun();
        double s = 0;
        foreach (var g in mpBossSegs) if (lx > g.A) s += System.Math.Min(lx, g.B) - g.A;
        return s;
    }
    // x0 → x1 の間にある区間の長さ(向き付き)
    public static double MpSegBetween(double x0, double x1) => MpSegOffset(x1) - MpSegOffset(x0);

    // ReportDistance から毎回(マルチだけ): 区間の分を距離の除外へ(後ろへ戻れば戻す)
    void MpSegmentTick()
    {
        if (!MpRun.On) return;
        var pc = PlayerController.Instance;
        if (pc == null) return;
        // 安全装置: ボス戦で止めていないのに区間が開いたまま 5秒(閉じる知らせが来ない等) → 自分の位置で閉じる(距離が止まり続けない)
        if (MpBossSegmentOpen)
        {
            bool holdOn = NetCombat.Authority ? (BossManager.Instance != null && BossManager.Instance.HoldsRun) : NetCombat.RemoteBossHold;
            if (holdOn) mpSegNoHoldSince = -1f;
            else if (mpSegNoHoldSince < 0f) mpSegNoHoldSince = Time.realtimeSinceStartup;
            else if (Time.realtimeSinceStartup - mpSegNoHoldSince > 5f)
            {
                double lx0 = pc.transform.position.x + FloatingOrigin.Offset;
                for (int i = 0; i < mpBossSegs.Count; i++) if (double.IsPositiveInfinity(mpBossSegs[i].B)) { var g = mpBossSegs[i]; g.B = System.Math.Max(g.A, lx0); mpBossSegs[i] = g; }
                Debug.LogWarning($"[Distance] boss segment left open 5s after the fight - closed at {lx0:F1} (safety)");
                mpSegNoHoldSince = -1f; MpSegSafetyCloses++;
            }
        }
        double now = MpSegOffset(pc.transform.position.x + FloatingOrigin.Offset);
        double d = now - mpSegApplied;
        bool justAdded = mpSegJustAdded; mpSegJustAdded = false;
        if (System.Math.Abs(d) < 1e-6) return;
        distanceExclusionOffset += (float)d;
        distanceExclusionOffsetExact += d;
        mpSegApplied = now;
        // 区間が届いた時に、すでに関門を越えていた分(通信の遅れの間に進んだ分。数 m)は距離を関門まで戻す(ソロの関門で距離をそろえるのと同じ)
        if (justAdded && d > 0 && d < 30)
        {
            MaxDistance = Mathf.Max(0f, MaxDistance - (float)d);
            MaxDistanceExact = System.Math.Max(0.0, MaxDistanceExact - d);
            Debug.Log($"[Distance] boss segment arrived {d:F2}m after the gate - distance set back to {MaxDistanceExact:F1}");
        }
    }
    static bool mpSegJustAdded;
    float mpSegNoHoldSince = -1f;
    public static int MpSegSafetyCloses; // 確認用

    // 置き直しの距離 d のうち、区間に入る分(MpSegmentTick が除くので、置き直しの除外からは引く)
    float MpSegPartOfWarp(float d)
    {
        if (!MpRun.On || mpBossSegs.Count == 0) return 0f;
        var pc = PlayerController.Instance;
        if (pc == null) return 0f;
        double x = pc.transform.position.x + FloatingOrigin.Offset;
        return (float)MpSegBetween(x, x + d);
    }
}
