using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

// BONUS ZONEの見た目の確認用(2026-09-29)。実行ファイルに -bonusShots を付けて起動すると、
// 荒野でRunを始め、MILE RUSH → JACKPOT を強制して、開始/報酬の表示/終了/RESULTを BonusZoneShots/ へ撮って終了する。
// (IMGUIはbatchmodeでは描かれないので、見た目は実行ファイルで撮る)
public class BonusZoneShots : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!System.Environment.GetCommandLineArgs().Contains("-bonusShots") || FindFirstObjectByType<BonusZoneShots>() != null) return;
        Application.runInBackground = true;
        var go = new GameObject("BonusZoneShots");
        DontDestroyOnLoad(go);
        go.AddComponent<BonusZoneShots>();
    }

    string dir; int n;
    static readonly BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    IEnumerator Start()
    {
        dir = System.IO.Path.Combine(Application.dataPath, "../BonusZoneShots");
        System.IO.Directory.CreateDirectory(dir);
        foreach (var f in System.IO.Directory.GetFiles(dir, "*.png")) System.IO.File.Delete(f);
        var saved = RunCheckpoint.Load();
        yield return new WaitForSecondsRealtime(1.5f);
        var gm = GameManager.Instance;
        gm.SetSelectedCharacter("swordsman");
        gm.SetSelectedStage(System.Environment.GetCommandLineArgs().Contains("-bonusSky") ? "sky_corridor" : "wasteland_road");
        float w = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 20f) { if (!gm.HasStarted) typeof(GameManager).GetMethod("StartGame", NP).Invoke(gm, null); yield return new WaitForSecondsRealtime(0.5f); w += 0.5f; }
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        typeof(GameManager).GetProperty("Lives").SetValue(gm, 999);
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        gm.DebugWarpToDistance(1100f); // 走る速さが上がった所で(追いついて殴る場面を撮る)
        var zone = BonusZone.Instance;
        StartCoroutine(Bot());
        yield return new WaitForSeconds(1f);
        zone.Force("mile_rush");
        yield return new WaitForSeconds(0.35f); yield return Shot("intro");
        yield return new WaitForSeconds(4.5f); yield return Shot("chase");
        // 追いついて殴っている場面(+MILEの表示)を待って撮る
        float cw = 0f; int m0 = zone.BonusMile;
        while (zone.BonusMile == m0 && cw < 10f) { yield return null; cw += Time.deltaTime; }
        yield return new WaitForSeconds(0.15f); yield return Shot("chase_hit");
        yield return new WaitForSeconds(1.5f); yield return Shot("chase2");
        zone.End();
        yield return new WaitForSeconds(0.3f); yield return Shot("end");
        yield return new WaitForSeconds(0.9f); yield return Shot("result");
        w = 0f; while (zone.State != BonusZone.Phase.Idle && w < 30f) { yield return null; w += Time.deltaTime; }
        zone.Force("jackpot");
        yield return new WaitForSeconds(0.4f); yield return Shot("jackpot_intro");
        yield return new WaitForSeconds(5f); yield return Shot("jackpot_mid");
        zone.Force("mimic_bash");
        yield return new WaitForSeconds(5f); yield return Shot("mimic");
        cw = 0f; m0 = zone.BonusMile;
        while (zone.BonusMile == m0 && cw < 10f) { yield return null; cw += Time.deltaTime; }
        yield return new WaitForSeconds(0.15f); yield return Shot("mimic_hit");
        zone.Force("card_hunt");
        yield return new WaitForSeconds(4.5f); yield return Shot("card_hunt");
        if (saved != null) { if (saved.active) RunCheckpoint.Save(saved); else RunCheckpoint.Clear(); }
        Application.Quit(0);
    }

    // 近くの報酬Enemyを殴る(穴は跳ぶ)
    IEnumerator Bot()
    {
        while (true)
        {
            var p = PlayerController.Instance; var t = TerrainManager.Instance; var z = BonusZone.Instance;
            if (p == null || t == null || z == null || Time.timeScale <= 0f) { yield return null; continue; }
            float x = p.transform.position.x;
            PlayerController.FlickDirection? f = null;
            if (p.IsGrounded && t.IsNearPit(x + 1.1f * Mathf.Max(1f, p.CurrentAutoRunSpeed / 5f), 0.4f)) f = PlayerController.FlickDirection.Up;
            else if (!p.IsGrounded && !t.GetHeightAt(x).HasValue) f = PlayerController.FlickDirection.Up;
            else if (z.Enemies.Any(e => e != null && e.isActiveAndEnabled && e.transform.position.x - x > -0.4f && e.transform.position.x - x < 2.6f)) f = PlayerController.FlickDirection.Forward;
            if (f.HasValue) { p.debugInjectFlick = f.Value; yield return null; p.debugInjectFlick = null; yield return new WaitForSeconds(0.12f); continue; }
            yield return null;
        }
    }

    IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"{n++:00}_{name}.png"));
        yield return null; yield return null;
    }
}
