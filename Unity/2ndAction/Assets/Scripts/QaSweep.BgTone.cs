#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 背景を控えめにして敵を見やすく(2026-11-01)の前後比較: -qaBgTone <dir> [-btView side|fold|oblique] [-btScenes a,b] [-btVideo 0|1]
//  場面(ステージ×時間帯×キャラ)ごとに、見えにくい敵(小さい/暗い/飛ぶ/背景と色が近い)を前に出し、時間を止めて同じ瞬間を
//  「前(BackgroundTone.Enabled=false = 今までの見た目)/後」×「敵あり/敵なし」の4枚で撮る。敵の画面上の四角も記録する
//  → Tools/bgtone/measure.py が「敵の画素とその後ろの背景の差」「敵の周りの背景のごちゃつき」を前後で比べる。
//  通常速度と高速の2つ。-btVideo 1 で同じ条件の前/後の短い連番(30fps)も書き出す。
public partial class QaSweep
{
    static readonly (string key, string stage, int seg, float night, string ch, string[] enemies)[] BtScenes =
    {
        ("waste_day", "wasteland_road", 0, -1f, "swordsman", new[] { "goblin", "flying_wyvern", "irregular_imp", "shooter_archer" }),
        ("waste_evening", "wasteland_road", 1, -1f, "dual_blade", new[] { "goblin", "flying_wyvern", "irregular_imp", "chaser_runner" }),
        ("waste_night", "wasteland_road", 2, -1f, "vampire", new[] { "goblin", "flying_wyvern", "irregular_imp", "heavy_ogre" }),
        ("waste_dawn", "wasteland_road", 3, -1f, "archer", new[] { "goblin", "flying_wyvern", "irregular_imp", "rusher_runner" }),
        ("cave", "natural_cave", -1, -1f, "ninja", new[] { "cave_bat", "cave_ant", "burrow_worm", "soldier_ant" }),
        ("sky_day", "sky_corridor", -1, 0f, "miko", new[] { "sky_slime", "harpy", "storm_spirit", "gargoyle" }),
        ("sky_night", "sky_corridor", -1, 1f, "mage", new[] { "sky_slime", "harpy", "storm_spirit", "sky_hound" }),
        ("last", "last_corridor", -1, -1f, "noble_lady", new[] { "goblin", "cave_bat", "gargoyle", "harpy" }),
    };

    IEnumerator BgToneMode()
    {
        Application.targetFrameRate = 60;
        string view = Arg("-btView", "side");
        int viewId = view == "fold" ? PortraitRunView.Fold : view == "oblique" ? PortraitRunView.Oblique : PortraitRunView.Side;
        PortraitRunView.Set(viewId);
        var only = Arg("-btScenes", "").Split(',').Where(x => x != "").ToList();
        bool video = Arg("-btVideo", "0") == "1";
        string[] spName = { "low", "high" };
        var perf = new List<string>();
        foreach (var sc in BtScenes)
        {
            if (only.Count > 0 && !only.Contains(sc.key)) continue;
            yield return BeginRun(sc.ch, sc.stage);
            PrepSceneryRun();
            if (sc.seg >= 0) SceneryCycle.DebugPreviewDistance = SceneryCycle.DistanceForSegment(sc.seg);
            WorldTimeCycle.DebugNight = sc.night >= 0f ? sc.night : (float?)null;
            yield return new WaitForSeconds(2.4f);
            L($"[scene] {sc.key} stage={sc.stage} view={view} portrait={PortraitRunView.IsPortraitScreen} night={(BackgroundTone.Instance != null ? BackgroundTone.Instance.Night : -1f):F2} luma={(ReadabilityDirector.Instance != null ? ReadabilityDirector.Instance.Luma : -1f):F2}");
            for (int speed = 0; speed < 2; speed++)
            {
                PlayerController.DebugSpeedScale = speed == 0 ? 1f : 4.5f;
                yield return new WaitForSeconds(speed == 0 ? 0.8f : 1.2f);
                var spawned = new List<GameObject>();
                foreach (var id in sc.enemies)
                {
                    var go = EncounterDirector.Instance != null ? EncounterDirector.Instance.DebugSpawnEnemy(id, EnemyAiTier.T0) : null;
                    if (go != null) spawned.Add(go); else L($"[WARN-spawn] {sc.key}: {id} could not be placed");
                    yield return new WaitForSeconds(speed == 0 ? 0.32f : 0.1f);
                }
                // 出した敵が画面の中ほどに入るまで待つ(最大3秒)
                float w = 0f;
                while (w < 3f)
                {
                    int inView = spawned.Count(g => g != null && g.activeInHierarchy && InViewX(g, 0.12f, 0.88f));
                    if (inView >= Mathf.Max(1, spawned.Count - 1)) break;
                    yield return null; w += Time.deltaTime;
                }
                if (speed == 1) { pc.debugInjectFlick = PlayerController.FlickDirection.Up; yield return null; yield return null; pc.debugInjectFlick = null; yield return new WaitForSeconds(0.15f); }
                Time.timeScale = 0f;
                string baseName = $"bt_{view}_{sc.key}_{spName[speed]}";
                foreach (bool on in new[] { false, true })
                {
                    BackgroundTone.Enabled = on;
                    yield return null; yield return null;
                    yield return new WaitForEndOfFrame();
                    var enemies = VisibleEnemies();
                    string tag = on ? "new" : "legacy";
                    foreach (var e in enemies) L($"[enemy] {baseName}_{tag} {e.id} rect={e.rect.x:F0},{e.rect.y:F0},{e.rect.xMax:F0},{e.rect.yMax:F0} spawned={(spawned.Contains(e.go) ? 1 : 0)}");
                    Shot($"{baseName}_{tag}_e");
                    yield return null; yield return null;
                    SetEnemyRenderers(enemies, false);
                    yield return null; yield return null;
                    yield return new WaitForEndOfFrame();
                    Shot($"{baseName}_{tag}_n");
                    yield return null; yield return null;
                    SetEnemyRenderers(enemies, true);
                }
                Time.timeScale = 1f;
                BackgroundTone.Enabled = true;
                yield return new WaitForSeconds(0.3f);
            }
            // 背景の絵以外(地面/足場/天井/敵/予告…)に背景の描き方が付いていないこと
            var toneMat = BackgroundTone.SharedMaterial;
            var wrong = FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None).Where(r => r.sharedMaterial == toneMat && r.sortingOrder > -40).Select(r => r.name + "@" + r.sortingOrder).Distinct().ToList();
            Check(wrong.Count == 0, $"{sc.key}: only background art uses the tone ({string.Join(", ", wrong)})");
            int adopted = FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None).Count(r => r.sharedMaterial == toneMat);
            Check(toneMat == null || adopted >= 1, $"{sc.key}: the background art uses the tone ({adopted} renderers)");
            L($"[tone] {sc.key} adopted={adopted} desat={BackgroundTone.Instance?.Desat:F2} flatten={BackgroundTone.Instance?.Flatten:F2} soft={BackgroundTone.Instance?.Soft:F2} mid={BackgroundTone.Instance?.Mid} lightRim={SpriteOutline.EnemyLightRim}");

            // 負荷の目安(制限なしのフレーム時間、前/後 各2秒)
            if (sc.key == "waste_day" || sc.key == "sky_day")
            {
                PlayerController.DebugSpeedScale = 4.5f;
                float[] ms = new float[2];
                for (int on = 0; on < 2; on++)
                {
                    BackgroundTone.Enabled = on == 1;
                    Application.targetFrameRate = 1000; QualitySettings.vSyncCount = 0;
                    yield return new WaitForSecondsRealtime(0.5f);
                    int n = 0; float t = 0f;
                    while (t < 2f) { yield return null; t += Time.unscaledDeltaTime; n++; }
                    ms[on] = t / n * 1000f;
                }
                Application.targetFrameRate = 60; BackgroundTone.Enabled = true;
                perf.Add($"{sc.key}/{view}: {ms[0]:F2}ms legacy -> {ms[1]:F2}ms new");
            }

            if (video)
            {
                PlayerController.DebugSpeedScale = 2.5f;
                foreach (bool on in new[] { false, true })
                {
                    BackgroundTone.Enabled = on;
                    yield return new WaitForSeconds(0.6f);
                    string vdir = System.IO.Path.Combine(outDir, $"video_{view}_{sc.key}_{(on ? "new" : "legacy")}");
                    System.IO.Directory.CreateDirectory(vdir);
                    Time.captureFramerate = 30;
                    int ei = 0;
                    for (int f = 0; f < 90; f++)
                    {
                        if (f % 14 == 0 && EncounterDirector.Instance != null) EncounterDirector.Instance.DebugSpawnEnemy(sc.enemies[ei++ % sc.enemies.Length], EnemyAiTier.T1);
                        pc.debugInjectFlick = f % 20 == 8 ? (f % 40 == 8 ? PlayerController.FlickDirection.Up : PlayerController.FlickDirection.Forward) : (PlayerController.FlickDirection?)null;
                        yield return new WaitForEndOfFrame();
                        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(vdir, $"f{f:00000}.png"));
                    }
                    pc.debugInjectFlick = null;
                    Time.captureFramerate = 0;
                }
                BackgroundTone.Enabled = true;
            }
            SceneryCycle.DebugPreviewDistance = null;
            WorldTimeCycle.DebugNight = null;
            PlayerController.DebugSpeedScale = 1f;
            yield return EndRun();
        }
        foreach (var p in perf) L("[perf] " + p);
        PortraitRunView.Set(PortraitRunView.Side);
    }

    bool InViewX(GameObject g, float x0, float x1)
    {
        var cam = Camera.main; if (cam == null) return false;
        Vector3 s = cam.WorldToViewportPoint(g.transform.position);
        return s.z > 0f && s.x >= x0 && s.x <= x1 && s.y > 0.05f && s.y < 0.95f;
    }

    struct BtEnemy { public string id; public GameObject go; public Rect rect; public Renderer[] rs; }
    List<BtEnemy> VisibleEnemies()
    {
        var list = new List<BtEnemy>();
        var cam = Camera.main;
        foreach (var ec in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
        {
            if (ec == null || !ec.isActiveAndEnabled) continue;
            var rs = ec.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray(); // 消えている物(被弾の光など)は後で点けない
            var main = ec.GetComponentsInChildren<SpriteOutline>().Select(o => o.GetComponent<SpriteRenderer>()).FirstOrDefault(r => r != null && r.enabled);
            if (main == null) continue;
            Bounds b = main.bounds;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue; bool front = true;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 s = cam.WorldToScreenPoint(c);
                if (s.z <= 0f) front = false;
                x0 = Mathf.Min(x0, s.x); x1 = Mathf.Max(x1, s.x); y0 = Mathf.Min(y0, Screen.height - s.y); y1 = Mathf.Max(y1, Screen.height - s.y);
            }
            if (!front || x1 < 0 || x0 > Screen.width || y1 < 0 || y0 > Screen.height) continue;
            string id = ec.name.Replace("(Clone)", "");
            list.Add(new BtEnemy { id = id, go = ec.gameObject, rect = Rect.MinMaxRect(x0, y0, x1, y1), rs = rs });
        }
        return list;
    }

    void SetEnemyRenderers(List<BtEnemy> list, bool on)
    {
        foreach (var e in list) foreach (var r in e.rs) if (r != null) r.enabled = on;
    }
}
#endif
