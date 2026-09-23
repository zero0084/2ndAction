#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// RUN開始準備/正常終了演出(2026-09-23) - Editor専用の自動確認。
// 1) NEW RUN開始直後、カウントダウン中はIsPreparingStart=true・距離/
//    自動前進が始まらないこと、GO(CountdownActive=false)の直後フレームで
//    IsPreparingStart=false・その後実際に前進が始まることを確認。
// 2) ResolveFinishTier(距離Tier境界の解決)を複数の距離で確認(純粋関数、
//    副作用なし)。
// 3) DoFinishSequenceを直接起動(privateコルーチン、reflection経由 -
//    HitReactionAutoTestと同じ手法)し、Finish中はPresentationDamageLock=
//    true・TakeDamageが無効・入力停止(isAscending経由でMove/HandleAttack
//    Inputごと止まる)であること、既定の余韻(finishHoldDurationByTier)後に
//    GameManager.Win()が実際に呼ばれる(IsGameOver=true, IsWin=true)ことを
//    最後まで確認する。
// 死亡→GameOverの既存経路自体は今回のTakeDamage変更が「if (hasDied ||
// IsFinishing) return;」という追加条件のみ(通常の死亡シナリオでは
// IsFinishingは構造的に常にfalseなので絶対に発火しない)であるため、GameManager
// が一度きりの終端状態(IsGameOver)を持つ制約上、同一セッション内で死亡と
// Win両方を実際にシミュレートするのは避け、この点はコードレビューで保証する
// (ログにその旨を明記する)。
// 結果は RunStartFinishAutoTest.txt。
public class RunStartFinishAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("RunStartFinishTest", 0) != 1) return;
        EditorPrefs.SetInt("RunStartFinishTest", 0);
        new GameObject("RunStartFinishTest").AddComponent<RunStartFinishAutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    void L(string s) { log.AppendLine(s); Debug.Log("[RunStartFinishTest] " + s); }

    static FieldInfo F(System.Type t, string name) => t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
    static MethodInfo M(System.Type t, string name) => t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1.5f);
        var gm = GameManager.Instance;
        var pc = PlayerController.Instance;

        bool anyException = false;
        Application.LogCallback handler = (cond, trace, type) =>
        {
            if (type == LogType.Exception || type == LogType.Error) { anyException = true; L("[EXC] " + cond); }
        };
        Application.logMessageReceived += handler;

        yield return TestStartPrepAndCountdown(gm, pc);
        yield return TestFinishTierResolution(pc);
        yield return TestFinishSequenceEndToEnd(gm, pc);

        L("");
        L("[Note] Death->GameOver path is untouched code (TakeDamage's only change is an `|| IsFinishing` guard, structurally false during any real death) - verified by code review, not re-simulated here (GameManager's IsGameOver is a one-shot terminal state per scene load).");

        Application.logMessageReceived -= handler;

        L("");
        L(anyException ? "SOME EXCEPTIONS LOGGED (see [EXC] lines above)" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../RunStartFinishAutoTest.txt"), log.ToString());

        if (Application.isBatchMode) EditorApplication.Exit(anyException ? 1 : 0); else EditorApplication.isPlaying = false;
    }

    IEnumerator TestStartPrepAndCountdown(GameManager gm, PlayerController pc)
    {
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float t0 = Time.time;
        while (!gm.HasStarted && Time.time - t0 < 5f) yield return null;

        L($"[Setup] HasStarted={gm.HasStarted} CountdownActive={gm.CountdownActive} character={gm.SelectedCharacterId}");
        L($"[StartPrep] IsPreparingStart={pc.IsPreparingStart} (expect True, while CountdownActive)");

        float xAtCountdownStart = pc.transform.position.x;
        yield return new WaitForSecondsRealtime(0.5f);
        float xMidCountdown = pc.transform.position.x;
        L($"[StartPrep] x unchanged mid-countdown: {Mathf.Approximately(xAtCountdownStart, xMidCountdown)} (expect True - auto-run withheld)");

        float t1 = Time.time;
        while (gm.CountdownActive && Time.time - t1 < 8f) yield return null;
        L($"[StartPrep] IsPreparingStart immediately after GO={pc.IsPreparingStart} (expect False)");

        float xAtGo = pc.transform.position.x;
        yield return new WaitForSeconds(0.4f);
        float xAfterGo = pc.transform.position.x;
        L($"[StartPrep] x increased after GO: {xAfterGo > xAtGo} (expect True - normal Run started)");
    }

    IEnumerator TestFinishTierResolution(PlayerController pc)
    {
        MethodInfo resolve = M(typeof(PlayerController), "ResolveFinishTier");
        int[] expected = { 0, 0, 1, 1, 2, 2, 3 };
        float[] distances = { 0f, 999f, 1000f, 9999f, 10000f, 49999f, 60000f };
        bool allMatch = true;
        var sb = new StringBuilder();
        for (int i = 0; i < distances.Length; i++)
        {
            int tier = (int)resolve.Invoke(pc, new object[] { distances[i] });
            sb.Append($"{distances[i]}m->{tier}(exp {expected[i]}) ");
            if (tier != expected[i]) allMatch = false;
        }
        L($"[TierResolve] {sb} allMatch={allMatch} (expect True)");
        yield return null;
    }

    IEnumerator TestFinishSequenceEndToEnd(GameManager gm, PlayerController pc)
    {
        const float testDistance = 25000f; // Tier2 (Long)
        gm.DebugWarpToDistance(testDistance);

        MethodInfo doFinish = M(typeof(PlayerController), "DoFinishSequence");
        pc.StartCoroutine((IEnumerator)doFinish.Invoke(pc, null));
        yield return null;

        L($"[Finish] IsFinishing={pc.IsFinishing} FinishTierIndex={pc.FinishTierIndex} (expect True, 2)");
        L($"[Finish] PresentationDamageLock={gm.PresentationDamageLock} (expect True)");

        int livesBefore = gm.Lives;
        pc.TakeDamage();
        yield return null;
        L($"[Finish] TakeDamage during Finish has no effect: livesBefore={livesBefore} livesAfter={gm.Lives} (expect equal)");

        float t = 0f;
        while (t < 5f && !gm.IsGameOver) { yield return null; t += Time.deltaTime; }
        L($"[Finish] after hold: IsGameOver={gm.IsGameOver} IsWin={gm.IsWin} PresentationDamageLock={gm.PresentationDamageLock} elapsed={t:F2}s (expect True,True,False)");
    }
}

public static class RunStartFinishTestMenu
{
    [MenuItem("Tools/OneMoreMile/Run Start-Finish Test (batch)")]
    public static void RunBatch()
    {
        EditorPrefs.SetInt("RunStartFinishTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif
