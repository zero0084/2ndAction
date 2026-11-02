#if UNITY_EDITOR || DEVELOPMENT_BUILD || OMM_RELEASE_PROBE
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// 敵の出現の自動確認(2026-11-03、ct416 の不具合「操作の練習の後に雑魚が1匹も出ない」の再発防止)。
// 開発版に加えて、製品版と同じ中身の確認用ビルド(OMM_RELEASE_PROBE = BuildWindowsProductSandbox.BuildReleaseProbe、配らない・
// 製品名「One More Mile Test」で本物の保存と別)でも動く。開発版だけの試験の仕組み(QaSweep/-fileSave)は使わない。
//   起動引数: -spawnProbe <出力フォルダ>   → <出力フォルダ>/spawn_probe.txt に PASS/FAIL を1行ずつ、最後に DONE
// ケース(どれも実際の画面の流れ = シーンの読み直しを通す):
//   A 起動直後のラン  B 操作の練習 → やめる → ラン  C ゲームオーバー → ホーム → もう一度ラン  D 練習(設定の遊び方)→ ホーム → ラン
//   E BONUS ZONE の敵  F 1,000m のボス  G 障害物  H 脱出(長押しの完了と同じ入口)
// 雑魚の確認 = EncounterDirector が動いていて(enabled)、決めた Encounter の雑魚が実際に場に出ること
public class ReleaseSpawnProbe : MonoBehaviour
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    string outPath;
    int fails;
    readonly List<string> lines = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var a = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(a, "-spawnProbe");
        if (i < 0 || i + 1 >= a.Length) return;
        Application.runInBackground = true; // 製品版は窓が裏へ回ると止まる → 試験の時間切れにしない(2026-11-03: 137秒止まって A が不合格になった)
        var go = new GameObject("[ReleaseSpawnProbe]");
        DontDestroyOnLoad(go);
        var p = go.AddComponent<ReleaseSpawnProbe>();
        EncounterDirector.ProbeLegacyNoReenable = System.Array.IndexOf(a, "-probeLegacy") >= 0;
        System.IO.Directory.CreateDirectory(a[i + 1]);
        p.outPath = System.IO.Path.Combine(a[i + 1], "spawn_probe.txt");
        p.StartCoroutine(p.Run());
    }

    void L(string s) { lines.Add(s); Debug.Log("[SpawnProbe] " + s); System.IO.File.WriteAllLines(outPath + ".part", lines); }
    void Check(bool ok, string what) { if (!ok) fails++; L((ok ? "[PASS] " : "[FAIL] ") + what); }

    IEnumerator WaitFor(System.Func<bool> cond, float sec)
    {
        float t = 0f;
        while (!cond() && t < sec) { yield return null; t += Time.unscaledDeltaTime; }
    }

    GameManager Gm => GameManager.Instance;
    static EncounterDirector Dir => EncounterDirector.Instance;

    IEnumerator Home()
    {
        yield return WaitFor(() => Gm != null && !Gm.HasStarted && TutorialLauncher.Instance != null && !TutorialLauncher.Instance.Launching && !TutorialMode.Active && !TutorialMode.PendingStart, 40f);
        yield return new WaitForSecondsRealtime(1.5f);
    }

    // 出発(ステージ選択の「出発」と同じ StartGame)。走るのは自動、穴/障害物では倒れないように無敵 + 自動操作補助
    IEnumerator StartRun(string stage)
    {
        yield return Home();
        var gm = Gm;
        gm.SetSelectedCharacter("swordsman");
        gm.SetSelectedStage(stage);
        float w = 0f, retry = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 25f)
        {
            if (!gm.HasStarted && retry <= 0f) { typeof(GameManager).GetMethod("StartGame", NP).Invoke(gm, null); retry = 0.6f; }
            yield return null; w += Time.unscaledDeltaTime; retry -= Time.unscaledDeltaTime;
        }
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);
    }

    IEnumerator EndRun()
    {
        var gm = Gm;
        if (gm != null && gm.HasStarted)
        {
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
            gm.Retry();
            yield return WaitFor(() => GameManager.Instance != null && GameManager.Instance != gm, 20f);
        }
        yield return Home();
    }

    static int LiveZako() => FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Count(e => e != null && e.isActiveAndEnabled && e.GetComponent<BonusEnemy>() == null);
    static int LiveBonus() => FindObjectsByType<BonusEnemy>(FindObjectsSortMode.None).Count(e => e != null && e.isActiveAndEnabled);
    static int LiveObstacles() => FindObjectsByType<ObstacleController>(FindObjectsSortMode.None).Count(o => o != null && o.isActiveAndEnabled);

    // 雑魚が出るか: 30秒以内に Encounter の雑魚が決まって(SpawnedEnemies)、場に出る(EnemyController)
    IEnumerator ZakoCase(string name)
    {
        int seen = 0, obst = 0;
        float t = 0f;
        while (t < 30f && (seen == 0 || Dir == null || Dir.SpawnedEnemies == 0))
        {
            seen = Mathf.Max(seen, LiveZako()); obst = Mathf.Max(obst, LiveObstacles());
            yield return null; t += Time.unscaledDeltaTime;
        }
        var d = Dir;
        L($"{name}: director enabled={(d != null && d.enabled)} spawned={(d != null ? d.SpawnedEnemies : -1)} encounters={(d != null ? d.EncounterIndex : -1)} liveZako={seen} obstacles={obst} t={t:F1}s d={Gm.MaxDistance:F0}m wait=[{EncounterDirector.WaitReason}]");
        Check(d != null && d.enabled, $"{name}: the encounter director is running");
        Check(d != null && d.SpawnedEnemies > 0 && seen > 0, $"{name}: normal enemies appear ({(d != null ? d.SpawnedEnemies : 0)} placed, {seen} on the field)");
        lastObstacles = obst;
    }
    int lastObstacles;

    IEnumerator Run()
    {
        L($"start legacyNoReenable={EncounterDirector.ProbeLegacyNoReenable} debugBuild={Debug.isDebugBuild} devGate={DevGate.On} product={Application.productName} version={Application.version}");
        yield return Home();

        // A 起動直後
        yield return StartRun("wasteland_road");
        yield return ZakoCase("A fresh run");
        // G 障害物(雑魚より先の距離で置かれるので、別に待つ)
        int obst = lastObstacles; float ot = 0f;
        while (obst == 0 && ot < 40f) { obst = Mathf.Max(obst, LiveObstacles()); yield return null; ot += Time.unscaledDeltaTime; }
        Check(obst > 0, $"G obstacles appear ({obst} on the field, t={ot:F1}s d={Gm.MaxDistance:F0}m)");

        // E BONUS ZONE(区画を今すぐ始める。中の敵は Encounter の BONUS 用の Formation)
        var bz = BonusZone.Instance;
        bool forced = bz != null && bz.Force();
        int bonusSeen = 0; float bt = 0f;
        while (forced && bt < 20f && bonusSeen == 0) { bonusSeen = Mathf.Max(bonusSeen, LiveBonus()); yield return null; bt += Time.unscaledDeltaTime; }
        L($"E bonus: forced={forced} state={(bz != null ? bz.State.ToString() : "-")} bonusEnemies={bonusSeen} t={bt:F1}s");
        Check(forced && bonusSeen > 0, $"E bonus zone enemies appear ({bonusSeen})");
        yield return EndRun();

        // C ゲームオーバー/ホームからもう一度
        yield return StartRun("wasteland_road");
        yield return ZakoCase("C second run after returning home");
        yield return EndRun();

        // B 操作の練習(初回の扉の「練習する」と同じ)→ 練習をやめる(ステージ選択へ)→ 出発
        bool launched = TutorialLauncher.Launch(true, "spawn probe");
        Check(launched, "B tutorial launched");
        yield return WaitFor(() => TutorialRun.Instance != null && TutorialMode.Active, 40f);
        yield return new WaitForSecondsRealtime(3f);
        L($"B in tutorial: director enabled={(Dir != null && Dir.enabled)} (expected false while practicing)");
        TutorialLauncher.Exit(true, "spawn probe");
        yield return WaitFor(() => !TutorialMode.Active && GameManager.Instance != null && !GameManager.Instance.HasStarted, 40f);
        yield return StartRun("wasteland_road");
        yield return ZakoCase("B run after the tutorial");
        yield return EndRun();

        // D 練習(設定の「遊び方」と同じ)→ ホームへ → 出発
        launched = TutorialLauncher.Launch(false, "spawn probe (how to play)");
        yield return WaitFor(() => TutorialRun.Instance != null && TutorialMode.Active, 40f);
        yield return new WaitForSecondsRealtime(3f);
        TutorialLauncher.Exit(false, "spawn probe");
        yield return WaitFor(() => !TutorialMode.Active && GameManager.Instance != null && !GameManager.Instance.HasStarted, 40f);
        yield return StartRun("wasteland_road");
        yield return ZakoCase("D run after how-to-play");

        // F 1,000m のボス(この確認用ビルドだけ距離を進められる: DevGate)
        if (DevGate.On)
        {
            Gm.DebugWarpToDistance(950f);
            var bm = BossManager.Instance;
            yield return WaitFor(() => bm != null && bm.IsBossPhase && bm.AliveBossCount > 0, 40f);
            Check(bm != null && bm.IsBossPhase && bm.AliveBossCount > 0, $"F the 1,000m boss appears (phase={(bm != null && bm.IsBossPhase)} alive={(bm != null ? bm.AliveBossCount : 0)} d={Gm.MaxDistance:F0}m)");
        }
        else L("F skipped (distance warp needs DevGate)");

        // H 脱出(長押しが完了した時と同じ: TryConfirmEscape → 演出)
        var pc = PlayerController.Instance;
        bool ok = Gm.TryConfirmEscape("spawn probe");
        if (ok && pc != null) pc.StartCoroutine((IEnumerator)typeof(PlayerController).GetMethod("DoFinishSequence", NP).Invoke(pc, null));
        yield return WaitFor(() => Gm != null && Gm.IsGameOver, 30f);
        Check(ok && Gm != null && Gm.IsGameOver && Gm.IsWin, $"H escape succeeds (confirmed={ok} over={(Gm != null && Gm.IsGameOver)} win={(Gm != null && Gm.IsWin)})");
        yield return EndRun();

        L($"failures={fails}");
        L("DONE");
        System.IO.File.WriteAllLines(outPath, lines);
        yield return new WaitForSecondsRealtime(1f);
        Application.Quit();
    }
}
#endif
