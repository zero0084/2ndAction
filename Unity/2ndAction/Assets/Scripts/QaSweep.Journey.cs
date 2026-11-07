#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

// 背景の「旅の流れ」の相談・確認用の撮影(2026-11-03): -qaJourney <dir> [-jnStages wasteland_road,natural_cave,sky_corridor]
//  [-jnKm 0,5,10,...] [-jnVideoKm 0,30,60] [-jnBoss 0]
//  ステージごとに距離へワープして(ボスは止めて)走っている画面を撮る。-jnVideoKm の地点では 30fps の時間で 4 秒の連番も書く(動画用)
public partial class QaSweep
{
    IEnumerator JourneyMode()
    {
        Application.targetFrameRate = 60;
        if (Arg("-jnGate", "") != "") { yield return JourneyGateMode(); yield break; }
        string[] stages = Arg("-jnStages", "wasteland_road,natural_cave,sky_corridor").Split(',');
        string[] kms = Arg("-jnKm", "0,5,10,15,20,25,30,40,50,60,70,80,90").Split(',');
        string[] vkm = Arg("-jnVideoKm", "").Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);
        bool keepBoss = Arg("-jnBoss", "0") == "1";
        foreach (var st in stages)
        {
            yield return BeginRun(Arg("-jnChar", "swordsman"), st);
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            if (!keepBoss && BossManager.Instance != null) BossManager.Instance.enabled = false;
            if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);
            yield return new WaitForSeconds(1.5f);
            foreach (var k in kms)
            {
                float km = float.Parse(k, System.Globalization.CultureInfo.InvariantCulture);
                if (km > 0f) { WarpTo(km * 1000f + 150f); yield return new WaitForSeconds(2.2f); }
                else yield return new WaitForSeconds(0.5f);
                L($"[jn] {st} {km:F0}km d={gm.MaxDistance:F0} scenery={(SceneryCycle.Active ? SceneryCycle.CurrentName : "-")} journey={LandmarkJourney.Describe()}");
                Shot($"jn_{st}_{km:000}km"); yield return null; yield return null;
                if (System.Array.IndexOf(vkm, k) >= 0)
                {
                    Time.captureDeltaTime = 1f / 30f;
                    for (int f = 0; f < 120; f++) { Shot($"jnv_{st}_{km:000}_{f:000}"); yield return null; }
                    Time.captureDeltaTime = 0f;
                }
            }
            yield return EndRun();
        }
    }

    // -jnGate <km>(10 など): 関門の手前 -jnLead m(既定 700)から走り、本物のボス戦 → 4秒後に倒す → 門 → 内部 → 外へ を 15fps の連番で撮る(動画用)。
    //  [-jnFps 15] [-jnAfter 600(内部を抜けた後に撮る m)] 状態は [jn] の行に1秒ごと(LandmarkJourney.Describe)
    IEnumerator JourneyGateMode()
    {
        string st = Arg("-jnStages", "wasteland_road").Split(',')[0];
        float gateKm = float.Parse(Arg("-jnGate", "10"), System.Globalization.CultureInfo.InvariantCulture);
        float lead = float.Parse(Arg("-jnLead", "700"), System.Globalization.CultureInfo.InvariantCulture);
        float after = float.Parse(Arg("-jnAfter", "600"), System.Globalization.CultureInfo.InvariantCulture);
        int every = Mathf.Max(1, 30 / Mathf.Max(1, int.Parse(Arg("-jnFps", "15"))));
        yield return BeginRun(Arg("-jnChar", "swordsman"), st);
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        var a = HighSpeedAssist.Instance;
        if (a != null) { a.SetEnabled(true); a.SetAutoInBoss(true); a.SetAutoAttack(true); a.SetAutoAvoid(true); }
        float killAt = float.Parse(Arg("-jnKillAt", "4"), System.Globalization.CultureInfo.InvariantCulture); // ボス戦が始まってから倒すまでの秒(撃破が遅い場合の確認)
        GameManager.BlockExpGain = Arg("-jnNoExp", "1") == "1"; // 撮影中にカードの選択画面を出さない
        PlayerController.DebugSpeedScale = float.Parse(Arg("-jnSpeedScale", "1"), System.Globalization.CultureInfo.InvariantCulture); // 2026-11-07 城下町: 通常速度/高速の確認
        float gate = gateKm * 1000f;
        WarpTo(gate - lead); yield return new WaitForSeconds(1.5f);
        Time.captureDeltaTime = 1f / 30f;
        int f = 0; float bossT = 0f, logT = 0f, t = 0f, clearedD = -1f;
        while (t < 240f)
        {
            yield return null; t += 1f / 30f;
            bool boss = Bm != null && Bm.IsBossPhase;
            // 2026-11-07: ボス戦ごとに数える(11km のボスは城下町の中で始まる = 街中のボス戦も撮る)
            if (boss) { bossT += 1f / 30f; if (bossT > killAt && bossT < killAt + 0.1f) { L($"[jn] killing the gate boss at {gm.MaxDistance:F0}m"); KillAllBosses(); } }
            else bossT = 0f;
            if (clearedD < 0f && gm.MaxDistance > gate + 60f && !boss) clearedD = gm.MaxDistance;
            if ((f++ % every) == 0) Shot($"jg_{st}_{f:00000}");
            if ((logT += 1f / 30f) >= 1f) { logT = 0f; L($"[jn] t={t:F0}s d={gm.MaxDistance:F0} boss={boss} scenery={(SceneryCycle.Active ? SceneryCycle.CurrentName : "-")} journey={LandmarkJourney.Describe()} town={TownProps.Describe()} lights={LandmarkJourney.TownInside}"); }
            if (clearedD > 0f && gm.MaxDistance > clearedD + 1200f + after) break;
        }
        Time.captureDeltaTime = 0f;
        GameManager.BlockExpGain = false;
        PlayerController.DebugSpeedScale = 1f;
        Check(clearedD > 0f, $"journey gate {gateKm}km: passed (cleared at {clearedD:F0}m)");
        yield return EndRun();
    }
}
#endif
