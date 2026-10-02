#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Reflection;
using UnityEngine;

// 荒野街道のEncounter確認動画用(開発ビルド専用、2026-09-28)。起動引数:
//   -encounterDemo <出力フォルダ> <ステージID> <秒数> <距離Band用の距離> [強制するFormationをカンマ区切り]
// 毎フレームをPNGに書き出す(Time.captureFramerate=30)。簡単なボットが走る:
//   穴の手前でジャンプ / 分岐は交互に上ルート(ジャンプで乗る)・下ルート / 近くの敵には前攻撃・空中の敵には上攻撃。
// Encounterのデバッグ表示(ENC)を開いた状態で撮る。
public class EncounterDemoCapture : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 4; i++)
        {
            if (args[i] != "-encounterDemo") continue;
            var go = new GameObject("EncounterDemoCapture");
            DontDestroyOnLoad(go);
            var c = go.AddComponent<EncounterDemoCapture>();
            c.outDir = args[i + 1]; c.stageId = args[i + 2];
            float.TryParse(args[i + 3], out c.seconds);
            float.TryParse(args[i + 4], out c.bandDistance);
            c.forceList = i + 5 < args.Length && !args[i + 5].StartsWith("-") ? args[i + 5].Split(',') : new string[0];
            return;
        }
    }

    string outDir, stageId;
    float seconds, bandDistance;
    string[] forceList;
    int frame;
    bool capturing;
    PlayerController pc;
    readonly System.Text.StringBuilder times = new System.Text.StringBuilder();

    void LateUpdate()
    {
        if (!capturing) return;
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, $"f_{frame:00000}.png"));
        frame++;
    }

    IEnumerator Start()
    {
        System.IO.Directory.CreateDirectory(outDir);
        yield return new WaitForSecondsRealtime(2f);
        var gm = GameManager.Instance;
        gm.SetSelectedCharacter("swordsman");
        gm.SetSelectedStage(stageId);
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        pc = PlayerController.Instance;
        float w = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        typeof(GameManager).GetProperty("InvincibleMode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetSetMethod(true)?.Invoke(gm, new object[] { true });
        typeof(GameManager).GetField("expGainMultiplier", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)?.SetValue(gm, 0f);
        typeof(GameManager).GetProperty("DebugMode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetSetMethod(true)?.Invoke(gm, new object[] { true });
        bool clean = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-encDemoClean") >= 0; // 詳細パネルを閉じたまま(1行表示だけ)撮る
        typeof(EncounterDirector).GetField("debugExpanded", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, !clean);
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        EncounterDirector.DebugDistanceOffset = bandDistance - gm.MaxDistance;
        PlayerController.DebugSpeedScale = 1.3f;
        StartCoroutine(AutoPickLevelUp());
        StartCoroutine(Bot());
        StartCoroutine(Forcer());
        Time.captureFramerate = 30;
        capturing = true;
        float t = 0f;
        while (t < seconds) { yield return null; t += Time.deltaTime; }
        capturing = false;
        var dir = EncounterDirector.Instance;
        if (dir != null) foreach (var r in dir.Recent) times.AppendLine($"#{r.index} d={r.distance:F0} {r.intensity} {r.formation} UPPER=[{r.upper}] LOWER=[{r.lower}]");
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "encounters.txt"), times.ToString());
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "done.txt"), "done");
        yield return new WaitForSecondsRealtime(1f);
        Application.Quit();
    }

    // 指定Formationを順番に1回ずつ強制(前のものが出てから次へ)
    IEnumerator Forcer()
    {
        var dir = EncounterDirector.Instance;
        foreach (string id in forceList)
        {
            if (string.IsNullOrEmpty(id)) continue;
            int before = dir.Recent.Count;
            EncounterDirector.ForceFormation(id, 1);
            float t = 0f;
            while (t < 60f) { yield return null; t += Time.deltaTime; bool seen = false; for (int i = before; i < dir.Recent.Count; i++) if (dir.Recent[i].formation == id) seen = true; if (seen) break; }
            EncounterDirector.ForcedFormation = null;
            yield return new WaitForSeconds(3f);
        }
    }

    IEnumerator Bot()
    {
        var tm = TerrainManager.Instance;
        int branchIndex = 0;
        double lastFork = double.NegativeInfinity;
        bool wantUpper = false;
        float attackCd = 0f;
        while (true)
        {
            yield return null;
            attackCd -= Time.deltaTime;
            float px = pc.transform.position.x;
            float lead = 1.1f * Mathf.Max(1f, pc.CurrentAutoRunSpeed / 5f);
            // 分岐: 交互に上ルートへ(forkの少し手前でジャンプ)
            if (tm.TryGetBranchAfter(px, out float fork, out float merge, out bool gen) && gen)
            {
                double fl = FloatingOrigin.ToLogical(fork);
                if (fl > lastFork + 1.0 && fork - px < 30f) { lastFork = fl; wantUpper = branchIndex++ % 2 == 0; }
                if (wantUpper && pc.IsGrounded && fork - px < lead + 1.2f && fork - px > 0.2f) { yield return Flick(PlayerController.FlickDirection.Up); continue; }
            }
            // 穴
            if (pc.IsGrounded && !pc.IsReacting && tm.IsNearPit(px + lead, 0.4f)) { yield return Flick(PlayerController.FlickDirection.Up); continue; }
            // 敵
            if (attackCd <= 0f)
            {
                foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
                {
                    Vector3 ep = e.transform.position;
                    float dx = ep.x - px, dy = ep.y - pc.transform.position.y;
                    if (dx < -0.5f || dx > 2.4f) continue;
                    if (dy > 1.4f) { yield return Flick(PlayerController.FlickDirection.Up); attackCd = 0.35f; break; }
                    if (Mathf.Abs(dy) < 1.2f) { yield return Flick(PlayerController.FlickDirection.Forward); attackCd = 0.18f; break; }
                }
            }
        }
    }

    IEnumerator Flick(PlayerController.FlickDirection dir)
    {
        pc.debugInjectFlick = dir;
        do { yield return null; } while (Time.timeScale <= 0f);
        pc.debugInjectFlick = null;
    }

    IEnumerator AutoPickLevelUp()
    {
        while (true)
        {
            var gm = GameManager.Instance;
            if (gm != null && gm.IsRewardSequenceWaitingForSelection)
            {
                var seq = FindFirstObjectByType<RewardCardSequence>();
                if (seq != null) { seq.OnCardClicked(0); yield return new WaitForSecondsRealtime(0.15f); seq.OnCardClicked(0); }
                yield return new WaitForSecondsRealtime(0.3f);
                continue;
            }
            yield return null;
        }
    }
}
#endif
