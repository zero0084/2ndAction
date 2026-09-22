#if UNITY_EDITOR
using System.Collections;
using System.Text;
using UnityEditor;
using UnityEngine;

// 高速走行中のフリーズ/ワープ調査(2026-09-22) - TimeControl/HitStopの
// レース条件修正を直接検証する自動テスト。実際の敵AI/カード演出の
// タイミングに依存させず、"HitStopが有効な間に、別の理由(Level Up相当)
// がTimeControlへ新たに登録され、HitStopの方が先に終わる"という、今回
// 特定した不具合の核心部分だけを再現する。旧HitStop実装(capturedTimeScale
// を自分で覚えて無条件に書き戻す方式)だとこのテストは失敗する
// (HitStop終了時にTime.timeScaleが1へ戻ってしまい、まだ他の理由が有効
// なのに再生されてしまう)。結果は TimeControlAutoTest.txt。
public class TimeControlAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("TimeControlTest", 0) != 1) return;
        EditorPrefs.SetInt("TimeControlTest", 0);
        new GameObject("TimeControlTest").AddComponent<TimeControlAutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    void L(string s) { log.AppendLine(s); Debug.Log("[TimeControlTest] " + s); }

    IEnumerator Start()
    {
        bool allOk = true;

        TimeControl.ResetAll();
        yield return null;

        // ----- Test 1: 単純なPause/Resume ----- //
        object ownerA = new object();
        TimeControl.Pause(ownerA);
        yield return null;
        bool t1a = Time.timeScale == 0f;
        L($"[T1a] single Pause -> timeScale==0 : {t1a} (actual={Time.timeScale})");
        allOk &= t1a;

        TimeControl.Resume(ownerA);
        yield return null;
        bool t1b = Time.timeScale == 1f;
        L($"[T1b] single Resume -> timeScale==1 : {t1b} (actual={Time.timeScale})");
        allOk &= t1b;

        // ----- Test 2: 核心 - HitStop(相当)が先に終わっても、まだ有効な
        // 別の理由(Level Up相当)があればtimeScaleは0のまま維持される ----- //
        object hitStopOwner = new object();
        object levelUpOwner = new object();
        TimeControl.Pause(hitStopOwner); // 敵の撃破によるHitStop開始 相当
        yield return null;
        L($"[T2] after HitStop-equivalent Pause: timeScale={Time.timeScale} (expect 0)");

        TimeControl.Pause(levelUpOwner); // HitStopがまだ有効な間にLevel Up選択が始まる 相当
        yield return null;
        L($"[T2] after LevelUp-equivalent Pause (both active): timeScale={Time.timeScale} (expect 0)");

        TimeControl.Resume(hitStopOwner); // HitStopの短い実時間Freezeが先に終わる 相当
        yield return null;
        bool t2 = Time.timeScale == 0f; // まだLevel Up側が有効なので0のままのはず
        L($"[T2] HitStop-equivalent resumed while LevelUp-equivalent still active -> timeScale STILL 0 : {t2} (actual={Time.timeScale})  <- 旧実装ではここが1へ戻って失敗していた不具合");
        allOk &= t2;

        TimeControl.Resume(levelUpOwner); // 最後にLevel Up側も解決
        yield return null;
        bool t2b = Time.timeScale == 1f;
        L($"[T2b] both resumed -> timeScale==1 : {t2b} (actual={Time.timeScale})");
        allOk &= t2b;

        // ----- Test 3: 実際のHitStop.Freeze()コルーチンでも同じことを確認 ----- //
        TimeControl.ResetAll();
        yield return null;
        object levelUpOwner2 = new object();
        StartCoroutine(HitStop.Freeze(0.05f)); // 実時間50ms
        yield return null; // Freeze()の入口(Pause)が実行されるフレーム
        bool t3a = Time.timeScale == 0f && HitStop.IsActive;
        L($"[T3a] HitStop.Freeze started -> timeScale==0 && HitStop.IsActive : {t3a} (timeScale={Time.timeScale}, HitStop.IsActive={HitStop.IsActive})");
        allOk &= t3a;

        TimeControl.Pause(levelUpOwner2); // Freeze実行中にLevel Up相当の理由が追加される
        yield return null;

        // HitStop.Freezeの実時間0.05秒が経過し、内部でTimeControl.Resumeが
        // 呼ばれるのを待つ(WaitForSecondsRealtime駆動なのでリアルタイムで待つ)。
        float waitStart = Time.realtimeSinceStartup;
        while (HitStop.IsActive && Time.realtimeSinceStartup - waitStart < 2f) yield return null;
        bool hitStopEnded = !HitStop.IsActive;
        bool t3b = Time.timeScale == 0f; // levelUpOwner2がまだ有効なので0のまま
        L($"[T3b] HitStop.Freeze自体は終了した({hitStopEnded}) が、LevelUp相当の理由がまだ有効なのでtimeScaleは0のまま : {t3b} (actual={Time.timeScale})");
        allOk &= hitStopEnded && t3b;

        TimeControl.Resume(levelUpOwner2);
        yield return null;
        bool t3c = Time.timeScale == 1f;
        L($"[T3c] LevelUp相当も解決 -> timeScale==1 : {t3c} (actual={Time.timeScale})");
        allOk &= t3c;

        // ----- Test 4: ResetAllで確実に全解除 ----- //
        TimeControl.Pause(new object());
        TimeControl.Pause(new object());
        TimeControl.ResetAll();
        bool t4 = Time.timeScale == 1f && !TimeControl.IsPaused;
        L($"[T4] ResetAll clears everything -> timeScale==1 && !IsPaused : {t4} (actual={Time.timeScale}, IsPaused={TimeControl.IsPaused})");
        allOk &= t4;

        // ----- Test 5: GameManager.Awakeで Time.maximumDeltaTime が
        // ヒッチ対策のため下げられていることの確認。 ----- //
        bool t5 = Time.maximumDeltaTime <= 0.15f;
        L($"[T5] Time.maximumDeltaTime lowered for hitch mitigation : {t5} (actual={Time.maximumDeltaTime:F3})");
        allOk &= t5;

        L("");
        L(allOk ? "ALL TESTS PASSED" : "SOME TESTS FAILED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../TimeControlAutoTest.txt"), log.ToString());

        TimeControl.ResetAll();
        if (Application.isBatchMode) EditorApplication.Exit(allOk ? 0 : 1); else EditorApplication.isPlaying = false;
    }
}

public static class TimeControlTestMenu
{
    [MenuItem("Tools/OneMoreMile/TimeControl Race Test (batch)")]
    public static void RunBatch()
    {
        EditorPrefs.SetInt("TimeControlTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif
