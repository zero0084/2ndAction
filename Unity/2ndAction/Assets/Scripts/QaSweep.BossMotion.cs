#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// ボス戦のアニメーションの見直し(2026-11-02): -qaBossMotion <dir> -bmBoss Behemoth [-bmStage sky_corridor] [-bmChar swordsman]
//   [-bmFight 40] [-bmUlt 12] [-bmBreak 10] [-bmEvery 1] [-bmPhase 0]
// 実際のボス戦(足を止めて向き合うカメラ)を通常速度(30fps の固定時間)で撮る。プレイヤーは攻撃しない(崩しで技が切れない)・無敵。
//   fight = 普段の技を一巡 / ult = 必殺技 / break = BREAK に入る → 立ち直り
// <dir>/frames/f00000.jpg と <dir>/bm_log.tsv(フレームごとの姿勢・絵の名前・絵の位置/傾き/大きさ・攻撃判定の有無)
public partial class QaSweep
{
    IEnumerator BossMotionMode()
    {
        Application.targetFrameRate = 60;
        HookBossLogs();
        Random.InitState(int.Parse(Arg("-bmSeed", "20261102")));
        BossManager.NetTestBossHpOverride = 50000000;
        string bn = Arg("-bmBoss", "Behemoth");
        var kind = (SkyBossKind)System.Enum.Parse(typeof(SkyBossKind), bn);
        yield return BeginRun(Arg("-bmChar", "swordsman"), Arg("-bmStage", "sky_corridor"));
        SetGod(true);
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
        BossBattle.DebugNoStagger = true;
        string frames = System.IO.Path.Combine(outDir, "frames");
        System.IO.Directory.CreateDirectory(frames);
        string log = System.IO.Path.Combine(outDir, "bm_log.tsv");
        System.IO.File.WriteAllText(log, "frame\tseg\tstate\tart\tsprite\twx\twy\tsx\tsy\trot\tflip\tminx\tminy\tmaxx\tmaxy\thit\tthreats\tbroken\n");
        WarpTo(1500f);
        yield return new WaitForSeconds(1f);
        Bm.DebugSkyEncounter(kind, -1);
        yield return WaitSkySpawn();
        var b = SkyBossAlive();
        Check(b != null, $"bossmotion: {bn} appeared");
        if (b == null) { yield return EndRun(); yield break; }
        int ph = int.Parse(Arg("-bmPhase", "0"));
        if (ph > 0) b.DebugSetPhase(ph);
        var mb = (MonoBehaviour)b;
        var wild = b as WildBossBase;
        SpriteRenderer sr = wild != null && wild.DebugVisual != null ? wild.DebugVisual.GetComponentInChildren<SpriteRenderer>() : mb.GetComponent<SpriteRenderer>();
        var stateField = wild == null ? mb.GetType().GetField("state", NP) : null;
        var mposeField = mb.GetType().GetField("mpose", NP);
        int every = Mathf.Max(1, int.Parse(Arg("-bmEvery", "1")));
        int f = 0;
        Time.captureFramerate = 30;

        IEnumerator Rec(string seg, float seconds)
        {
            int n = Mathf.RoundToInt(seconds * 30f);
            L($"[bm] {seg}: frames {f}-{f + n - 1}");
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < n && b.DebugAlive; i++)
            {
                yield return new WaitForEndOfFrame();
                var cam = Camera.main;
                if (f % every == 0)
                {
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(frames, $"f{f:00000}.jpg"), tex.EncodeToJPG(88));
                    Destroy(tex);
                }
                string st = wild != null ? wild.DebugPose : stateField != null ? stateField.GetValue(mb).ToString() : "-";
                string art = b is SkyBossBase sk ? sk.DebugArtNames : "-";
                if (mposeField != null) art += "|" + (mposeField.GetValue(mb) ?? "-");
                Vector3 mn = Vector3.zero, mx = Vector3.zero;
                Bounds vb = sr != null ? sr.bounds : default;
                if (wild != null && wild.DebugVisual != null) foreach (var c in wild.DebugVisual.GetComponentsInChildren<SpriteRenderer>()) if (c.enabled && c.sprite != null && c.sortingOrder == sr.sortingOrder) vb.Encapsulate(c.bounds);
                if (cam != null && sr != null) { mn = cam.WorldToScreenPoint(vb.min); mx = cam.WorldToScreenPoint(vb.max); }
                var t = wild != null && wild.DebugVisual != null ? wild.DebugVisual : sr != null ? sr.transform : mb.transform;
                Sprite cur = wild != null ? wild.DebugSprite : sr != null ? sr.sprite : null;
                bool hit = mb.GetComponentsInChildren<BossHitbox>().Any(h => h.IsActive);
                int threats = CaveHazard.Live.Count + FindObjectsByType<BossProjectile>(FindObjectsSortMode.None).Length + FindObjectsByType<FireballController>(FindObjectsSortMode.None).Length + FindObjectsByType<SkyStrike>(FindObjectsSortMode.None).Length;
                sb.Append($"{f}\t{seg}\t{st}\t{art}\t{(cur != null ? cur.name : "-")}\t{t.position.x:F3}\t{t.position.y:F3}\t{t.lossyScale.x:F3}\t{t.lossyScale.y:F3}\t{t.eulerAngles.z:F2}\t{(sr != null && sr.flipX ? 1 : 0)}\t{mn.x:F0}\t{Screen.height - mx.y:F0}\t{mx.x:F0}\t{Screen.height - mn.y:F0}\t{(hit ? 1 : 0)}\t{threats}\t{(b.Broken ? 1 : 0)}\n");
                f++;
            }
            System.IO.File.AppendAllText(log, sb.ToString());
        }

        yield return Rec("fight", float.Parse(Arg("-bmFight", "30"), System.Globalization.CultureInfo.InvariantCulture));
        // 第2段階以降の技(特殊技・段階の移行の咆哮)も見る
        float f2 = float.Parse(Arg("-bmFight2", "20"), System.Globalization.CultureInfo.InvariantCulture);
        if (f2 > 0f && b.DebugAlive && b.PhaseCount >= 2) { b.DebugSetPhase(b.PhaseCount); yield return Rec("fight2", f2); }
        float ult = float.Parse(Arg("-bmUlt", "12"), System.Globalization.CultureInfo.InvariantCulture);
        if (ult > 0f && b.DebugAlive && b.DebugForceUltimate())
            yield return Rec("ult", ult);
        float brk = float.Parse(Arg("-bmBreak", "10"), System.Globalization.CultureInfo.InvariantCulture);
        if (brk > 0f && b.DebugAlive)
        {
            float w = 0f;
            while (b.UltimateRunning && w < 15f) { w += Time.deltaTime; yield return null; }
            yield return Rec("pre", 1f);
            b.DebugForceBreak();
            yield return Rec("break", brk);
        }
        Time.captureFramerate = 0;
        BossBattle.DebugNoStagger = false;
        L($"[bm] total {f} frames");
        Check(f > 300, "bossmotion: frames written");
        BossManager.NetTestBossHpOverride = 0;
        yield return EndRun();
    }
}
#endif
