#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// アニメーション強化の試作の確認動画(2026-10-17): -qaAnimDemo <dir> [-adChar ninja] [-adSpeed 1] [-adBoss Wild:Wolf|Cave:Centipede] [-adFight 12]
// 2026-10-20: -adBoss で他のボスも撮る(Cave は natural_cave で)。区切りは <dir>/demo_ranges.txt(名前 タブ 最初 タブ 最後)にも書く
// 30fps で毎フレーム書き出す(<dir>/frames/f00000.png)。区切りは qa_animdemo.txt に書く(ffmpeg で動画にする)。
//   1) 走り  2) オオカミと戦う(補助の自動攻撃: 通常攻撃・命中の衝撃・オオカミの走り/溜め/噛みつき)
//   3) オオカミの必殺技  4) BREAK  5) プレイヤーの被弾  6) オオカミの撃破  7) プレイヤーが倒れる
public partial class QaSweep
{
    int demoFrame;
    string demoDir;
    WildBossBase demoFocus;

    IEnumerator DemoRecord(string label, float seconds)
    {
        int n = Mathf.RoundToInt(seconds * 30f);
        L($"[demo] {label}: frames {demoFrame}-{demoFrame + n - 1} (t={demoFrame / 30f:F1}s)");
        System.IO.File.AppendAllText(System.IO.Path.Combine(outDir, "demo_ranges.txt"), $"{label.Split(' ')[0]}\t{demoFrame}\t{demoFrame + n - 1}\n");
        for (int i = 0; i < n; i++)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(demoDir, $"f{demoFrame:00000}.png"));
            if (demoFocus != null && Camera.main != null) // ボスのまわりを切り抜く一覧用(recsheet.py と同じ形: 画面の x / 上からの y / 体の高さ px)
            {
                Vector3 sp = Camera.main.WorldToScreenPoint(demoFocus.CenterWorld);
                float hpx = Mathf.Abs(Camera.main.WorldToScreenPoint(demoFocus.CenterWorld + Vector3.up * demoFocus.bodyHeight).y - sp.y) * 2f;
                System.IO.File.AppendAllText(System.IO.Path.Combine(outDir, "demo_pos.txt"), $"{demoFrame}\t{sp.x:F0}\t{Screen.height - sp.y:F0}\t{hpx:F0}\n");
            }
            demoFrame++;
        }
    }

    IEnumerator DemoSpawnMobs(string[] ids)
    {
        for (int i = 0; i < 40; i++)
        {
            var ed = EncounterDirector.Instance;
            var go = ed != null ? ed.DebugSpawnEnemy(ids[i % ids.Length], default) : null;
            StartCoroutine(DemoHitMob(go));
            yield return new WaitForSeconds(1.2f);
        }
    }

    // 出した雑魚を、少し近づいてから 1回叩いて(被弾の絵)、0.6秒後に倒す(撃破の絵)。仲間の攻撃と同じ入口(AllyApplyHit)
    IEnumerator DemoHitMob(GameObject go)
    {
        if (go == null) yield break;
        yield return new WaitForSeconds(0.9f);
        var ec = go != null ? go.GetComponent<EnemyController>() : null;
        if (ec == null) yield break;
        string id = ec.name;
        ec.AllyApplyHit(0, 1, default, go.transform.position, null);
        yield return new WaitForSeconds(0.1f);
        DemoMobShot(ec, "hit");
        yield return new WaitForSeconds(0.5f);
        if (ec != null && ec.isActiveAndEnabled)
        {
            ec.AllyApplyHit(0, 99999, default, go.transform.position, null);
            yield return new WaitForSeconds(0.08f);
            DemoMobShot(ec, "death");
        }
    }

    // 雑魚の被弾/撃破の瞬間のフレーム番号と画面の位置を書く(mob_shots.txt。撃破の後は分身が出るので、その位置の絵を見る)
    int mobShotN;
    void DemoMobShot(EnemyController ec, string what)
    {
        if (ec == null || Camera.main == null) return;
        var sr = ec.GetComponentInChildren<SpriteRenderer>();
        Vector3 sp = Camera.main.WorldToScreenPoint(sr != null ? sr.bounds.center : ec.transform.position);
        string fn = $"{what}_{mobShotN++:000}@f{demoFrame:00000}"; // 撮影は DemoRecord の毎フレームの絵(同じフレームに2枚は撮れない)
        System.IO.File.AppendAllText(System.IO.Path.Combine(outDir, "mob_shots.txt"), $"{fn}\t{ec.name}\t{sp.x:F0}\t{Screen.height - sp.y:F0}\t{(sr != null && sr.sprite != null ? sr.sprite.name : "-")}\n");
    }

    IEnumerator AnimDemoMode()
    {
        Application.targetFrameRate = 60;
        HookBossLogs();
        Random.InitState(20261017);
        BossManager.NetTestBossHpOverride = 4000000;
        string ch = Arg("-adChar", "ninja");
        string[] bossArg = Arg("-adBoss", "Wild:Wolf").Split(':');
        bool cave = bossArg[0] == "Cave";
        yield return BeginRun(ch, cave ? "natural_cave" : "wasteland_road");
        SetGod(true);
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);
        demoDir = System.IO.Path.Combine(outDir, "frames");
        System.IO.Directory.CreateDirectory(demoDir);
        WarpTo(1400f);
        yield return new WaitForSeconds(1.5f);
        PlayerController.DebugSpeedScale = float.Parse(Arg("-adSpeed", "1"), System.Globalization.CultureInfo.InvariantCulture);
        Time.captureFramerate = 30;

        // 2026-10-20: -adMobs goblin,flying_wyvern で、走りの間に雑魚を1.2秒ごとに目の前へ出す(被弾/撃破の絵の撮影)
        string[] mobs = Arg("-adMobs", "").Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (mobs.Length > 0) StartCoroutine(DemoSpawnMobs(mobs));
        yield return DemoRecord("run", float.Parse(Arg("-adRun", "3"), System.Globalization.CultureInfo.InvariantCulture)); // 2026-10-20: -adRun で長く(雑魚の被弾/撃破の撮影)
        if (Arg("-adRunOnly", "0") == "1") { Time.captureFramerate = 0; PlayerController.DebugSpeedScale = 1f; L($"[demo] total {demoFrame} frames"); yield break; }

        if (cave) Bm.DebugCaveEncounter((CaveBossKind)System.Enum.Parse(typeof(CaveBossKind), bossArg[1]), -1);
        else Bm.DebugWildEncounter((WildBossKind)System.Enum.Parse(typeof(WildBossKind), bossArg[1]), -1);
        yield return WaitBossSpawn(10f);
        var b = WildAlive().FirstOrDefault();
        Check(b != null, "demo: the boss appeared");
        if (b == null) { Time.captureFramerate = 0; yield return EndRun(); yield break; }
        demoFocus = b;
        yield return DemoRecord("fight (assist attacks, the boss runs / winds up / attacks)", float.Parse(Arg("-adFight", "12"), System.Globalization.CultureInfo.InvariantCulture));

        // 2026-10-20: -adUltCalm 1 で必殺技の間は補助の自動攻撃を止める(崩しで必殺技が途切れず、最後の隙の絵まで撮れる)
        bool calm = Arg("-adUltCalm", "0") == "1";
        if (calm && HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
        // 2026-10-21: -adPhase n = 必殺技の前に段階を n へ(岩ゴーレムのように第3段階からの必殺技を撮る)
        int adPhase = int.Parse(Arg("-adPhase", "0"));
        if (adPhase > 0) b.DebugSetPhase(adPhase);
        b.DebugForceUltimate();
        float w = 0f;
        while (w < 3f && !b.UltimateRunning) { w += Time.deltaTime; yield return null; }
        yield return DemoRecord("ultimate", float.Parse(Arg("-adUlt", "7"), System.Globalization.CultureInfo.InvariantCulture));
        if (calm && HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);

        b.DebugBreak();
        yield return DemoRecord("BREAK", 4f);

        SetGod(false);
        var lives = typeof(GameManager).GetProperty("Lives").GetSetMethod(true);
        lives.Invoke(gm, new object[] { 150 });
        SetPrivate(pc, "hitInvincibleTimer", 0f);
        pc.TakeDamage(false, "demo-hurt");
        yield return DemoRecord("player hurt", 1.6f);
        SetGod(true);

        b.TakeDamage(Mathf.Max(99999, b.Hp), b.CenterWorld);
        yield return DemoRecord("defeat", 3.5f);

        SetGod(false);
        w = 0f;
        while (!gm.IsGameOver && w < 3f)
        {
            lives.Invoke(gm, new object[] { 1 });
            var p = typeof(PlayerController).GetProperty("ShieldCharges"); if (p != null) p.SetValue(pc, 0);
            SetPrivate(pc, "hitInvincibleTimer", 0f);
            pc.TakeDamage(false, "demo-death");
            if (gm.IsGameOver) break;
            yield return new WaitForSeconds(0.2f); w += 0.2f;
        }
        yield return DemoRecord("player falls (game over)", 2.5f);
        Time.captureFramerate = 0;
        PlayerController.DebugSpeedScale = 1f;
        L($"[demo] total {demoFrame} frames");
        Check(demoFrame > 300, "demo: frames written");
    }
}
#endif
