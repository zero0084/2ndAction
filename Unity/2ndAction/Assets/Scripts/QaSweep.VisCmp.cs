#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

// 画質の比較用の撮影(2026-10-25 v1.0 容量監査): -qaVisCmp <dir>
//  最適化の前後で同じ場面を撮る(止めて撮る。通常の画面 + 寄った画面)。
//  ホーム / デッキ編集(カード) / キャラ(黒剣士・お嬢様騎士・巫女・竜人)/ ステージ(荒野・洞窟・天空・ラスダン)/ 大コウモリ・バジリスク / 死神三姉妹
public partial class QaSweep
{
    IEnumerator VisFreezeShot(string name, Vector3? zoomAt, float zoomSize = 1.6f)
    {
        float ts = Time.timeScale; Time.timeScale = 0f;
        yield return new WaitForEndOfFrame();
        Shot(name);
        yield return new WaitForSecondsRealtime(0.6f);
        var cam = Camera.main; var cf = cam != null ? cam.GetComponent<CameraFollow>() : null;
        if (cam != null && zoomAt.HasValue)
        {
            bool cfWas = cf != null && cf.enabled; if (cf != null) cf.enabled = false;
            float size0 = cam.orthographicSize; var pos0 = cam.transform.position;
            cam.orthographicSize = zoomSize;
            cam.transform.position = new Vector3(zoomAt.Value.x, zoomAt.Value.y, pos0.z);
            yield return new WaitForEndOfFrame();
            Shot("zoom_" + name);
            yield return new WaitForSecondsRealtime(0.6f);
            cam.orthographicSize = size0; cam.transform.position = pos0; if (cf != null) cf.enabled = cfWas;
        }
        Time.timeScale = ts;
    }

    IEnumerator VisCmpMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(2.5f);
        gm = GameManager.Instance;
        MemRow("home_start");
        Shot("home");
        yield return new WaitForSecondsRealtime(0.6f);
        gm.OpenDeckEdit();
        yield return new WaitForSecondsRealtime(1.5f);
        Shot("deck_cards");
        yield return new WaitForSecondsRealtime(0.6f);
        gm.HandleBackButtonForTest();
        yield return new WaitForSecondsRealtime(1.2f);

        foreach (var ch in new[] { "swordsman", "noble_lady", "miko", "dragonkin" })
        {
            yield return BeginRun(ch, "wasteland_road");
            SetGod(true);
            if (BossManager.Instance != null) BossManager.Instance.enabled = false;
            yield return new WaitForSeconds(3f);
            yield return VisFreezeShot("char_" + ch, pc.transform.position + new Vector3(0f, 0.8f, 0f), 1.4f);
            yield return EndRun();
        }
        foreach (var st in new[] { "natural_cave", "sky_corridor", "last_corridor" })
        {
            yield return BeginRun("swordsman", st);
            SetGod(true);
            if (BossManager.Instance != null) BossManager.Instance.enabled = false;
            yield return new WaitForSeconds(4f);
            yield return VisFreezeShot("stage_" + st, null);
            yield return EndRun();
        }
        foreach (var (kind, name) in new[] { (6, "bat"), (8, "basilisk") })
        {
            yield return BeginRun("swordsman", "natural_cave");
            SetGod(true);
            yield return new WaitForSeconds(2f);
            BossManager.Instance.DebugSpawnBossForTest(1, kind);
            yield return new WaitForSeconds(5f);
            var b = NearestBoss();
            yield return VisFreezeShot("boss_" + name, b.HasValue ? b.Value + new Vector3(0f, 1.2f, 0f) : (Vector3?)null, 3.2f);
            yield return EndRun();
        }
        yield return BeginRun("swordsman", "wasteland_road");
        SetGod(true);
        yield return new WaitForSeconds(2f);
        BossManager.Instance.DebugSpawnReaper();
        yield return new WaitForSeconds(7f);
        var r = NearestBoss();
        yield return VisFreezeShot("reapers", r.HasValue ? r.Value + new Vector3(0f, 1.2f, 0f) : (Vector3?)null, 3.0f);
        yield return EndRun();
        yield return new WaitForSecondsRealtime(3f);
        MemRow("home_after_bosses");
        L($"[viscmp] boss frame sets cached {BossAnimSet.CachedSets}, released {BossAnimSet.Releases} times");
        Check(true, "vis shots done");
    }
}
#endif
