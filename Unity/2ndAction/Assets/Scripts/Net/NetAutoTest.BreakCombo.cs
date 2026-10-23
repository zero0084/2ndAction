#if UNITY_EDITOR || DEVELOPMENT_BUILD
// 2026-10-23: 開発版(Editor / Development Build)だけ。製品版では起動引数で動かせない(Steam の起動オプションから入れてしまうため)
using System;
using UnityEngine;

// 2026-10-21 コンボのつながり改修: 協力の HOST / JOIN の両方が BREAK 中のボスに空中コンボを入れられるか。
//  -netAutoBreakAt t と一緒に: -netAutoBreakCombo HOST|JOIN|BOTH [-netAutoComboGap 0.3] [-netAutoComboSeq U,F,F,F,U,F,F,F,D]
//  BREAK の 0.6秒後から、指定の端末(BOTH は JOIN が 0.15秒遅れて)が自分のプレイヤーに入力の列を入れる(本物の1回のフリックと同じ)。
//  両方の端末で 0.1秒ごとに COMBO 行(基準の人/浮き/締めの合図/ボスとの距離)、終わりに COMBOSUM 行
//  (HOST: 打ち上げ/JOIN の打ち上げ/JOIN の命中/引き継ぎ/基準を替えなかった命中/1フレームの横の跳び、JOIN: 送った命中の要求)
public partial class NetAutoTest
{
    string comboWho = "";
    float comboGap = 0.3f;
    string[] comboSeq = { "U", "F", "F", "F", "U", "F", "F", "F", "D" };
    int comboIdx = -1; float comboNext, comboLogNext;
    int comboSent0, comboApplied0, comboL0, comboRL0, comboRH0, comboHo0, comboKeep0, comboFin0;
    bool comboStarted, comboSummarized;
    string comboShotDir; int comboShotN; float comboShotNext;
    float comboMaxJump; float comboPrevRel = float.NaN; Component comboBoss;

    void ParseBreakComboArgs(string a, string next)
    {
        if (a == "-netAutoBreakCombo") comboWho = next.ToUpperInvariant();
        else if (a == "-netAutoComboLegacy") ComboRevision.Legacy = next == "1"; // 改修前の動き(比較用)
        else if (a == "-netAutoComboShots") comboShotDir = next; // 2026-10-21: BREAK の前後の画面を毎フレーム書き出す(動画用)
        else if (a == "-netAutoComboGap") float.TryParse(next, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out comboGap);
        else if (a == "-netAutoComboSeq") comboSeq = next.Split(',');
    }

    bool ComboWindowActive => !string.IsNullOrEmpty(comboWho) && breakAt > 0f && runTime >= breakAt - 0.5f && runTime < breakAt + 6.5f;

    static BossJuggle JuggleOf(Component c) =>
        c is WildBossBase w ? w.Juggle : c is DragonController d ? d.Juggle : c is MajinController m ? m.Juggle : null;

    Component FindComboBoss()
    {
        foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (mb is IBossBattleDebug b && mb.isActiveAndEnabled && (mb is WildBossBase || mb is DragonController || mb is MajinController)) return mb;
        return null;
    }

    void BreakComboTick()
    {
        if (string.IsNullOrEmpty(comboWho) || breakAt <= 0f) return;
        float t = runTime - breakAt;
        if (t < -0.5f) return;
        var me = PlayerController.Instance;
        if (me == null) return;
        if (comboBoss == null) comboBoss = FindComboBoss();
        var nc = NetCombat.Instance;
        if (!comboStarted && t >= 0.6f)
        {
            comboStarted = true;
            bool mine = comboWho == "BOTH" || comboWho == role;
            comboIdx = mine ? 0 : -1;
            comboNext = runTime + (comboWho == "BOTH" && role == "JOIN" ? 0.15f : 0f);
            comboSent0 = nc != null ? nc.StatHitRequestsSent : 0; comboApplied0 = nc != null ? nc.StatHitRequestsApplied : 0;
            comboL0 = BossJuggle.Launches + BossJuggle.Relaunches; comboRL0 = BossJuggle.RemoteLaunches; comboRH0 = BossJuggle.RemoteHits;
            comboHo0 = BossJuggle.Handoffs; comboKeep0 = BossJuggle.OwnerKeptOnOtherHit; comboFin0 = BossJuggle.FinishHits;
            L($"COMBOSTART role={role} me=P{NetCombat.LocalPlayerNumber} inputs={(mine ? string.Join("", comboSeq) : "-")} gap={comboGap:F2} boss={(comboBoss != null ? comboBoss.name : "-")}");
        }
        if (comboIdx >= 0 && comboIdx < comboSeq.Length && runTime >= comboNext)
        {
            string s = comboSeq[comboIdx];
            me.debugInjectOnce = s == "U" ? PlayerController.FlickDirection.Up : s == "D" ? PlayerController.FlickDirection.Down : PlayerController.FlickDirection.Forward;
            comboIdx++; comboNext = runTime + comboGap;
        }
        // 横の跳び(ボス - 自分のプレイヤー の1フレームの変化。走行の分は消える)
        if (comboBoss != null && comboBoss.gameObject.activeInHierarchy)
        {
            float rel = comboBoss.transform.position.x - me.transform.position.x;
            if (!float.IsNaN(comboPrevRel)) { float j = Mathf.Abs(rel - comboPrevRel) * 0.0167f / Mathf.Max(0.0167f, Time.unscaledDeltaTime); if (j < 20f) comboMaxJump = Mathf.Max(comboMaxJump, j); }
            comboPrevRel = rel;
        }
        // 動画用: BREAK の0.3秒前から6秒、1/30秒ごとに画面を書き出す(実時間。2つの端末の時間は進めたまま)
        if (!string.IsNullOrEmpty(comboShotDir) && t >= -0.3f && t < 6f && Time.unscaledTime >= comboShotNext)
        {
            if (comboShotN == 0) System.IO.Directory.CreateDirectory(comboShotDir);
            comboShotNext = Time.unscaledTime + 1f / 30f;
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(comboShotDir, $"{role.ToLowerInvariant()}_{comboShotN++:00000}.png"));
        }
        if (t < 6f && runTime >= comboLogNext && comboBoss != null)
        {
            comboLogNext = runTime + 0.1f;
            var j = JuggleOf(comboBoss);
            var b = comboBoss as IBossBattleDebug;
            Vector3 bp = comboBoss.transform.position, mp = me.transform.position;
            L($"COMBO t={t:F2} role={role} broken={(b != null && b.Broken)} owner={(j != null ? j.Owner : -9)} anchored={(j != null && j.Anchored)} mine={(j != null && j.AnchoredToLocal)} air={(j != null && j.Airborne)} fin={(j != null && j.Finishing)} replica={(j != null && j.IsReplica)} dx={bp.x - mp.x:F2} dy={bp.y - mp.y:F2} meAir={!me.IsGrounded}");
        }
        if (!comboSummarized && t >= 6.5f)
        {
            comboSummarized = true;
            L($"COMBOSUM role={role} me=P{NetCombat.LocalPlayerNumber} sentHits={(nc != null ? nc.StatHitRequestsSent - comboSent0 : 0)} appliedRemoteHits={(nc != null ? nc.StatHitRequestsApplied - comboApplied0 : 0)} " +
              $"launches={BossJuggle.Launches + BossJuggle.Relaunches - comboL0} remoteLaunches={BossJuggle.RemoteLaunches - comboRL0} remoteComboHits={BossJuggle.RemoteHits - comboRH0} handoffs={BossJuggle.Handoffs - comboHo0} keptOwner={BossJuggle.OwnerKeptOnOtherHit - comboKeep0} finishHits={BossJuggle.FinishHits - comboFin0} maxRelJump={comboMaxJump:F2}");
        }
    }
}
#endif
