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

        // Start/Finish自然化(2026-09-24)の回帰確認用ハンドル。visualT/
        // visualBaseRotはPlayerAnimatorのprivateフィールド、SpriteRendererは
        // 公開APIのGetComponentInChildrenでそのまま取れる。
        PlayerAnimator anim = pc.GetComponent<PlayerAnimator>();
        FieldInfo visualTField = F(typeof(PlayerAnimator), "visualT");
        FieldInfo visualBaseRotField = F(typeof(PlayerAnimator), "visualBaseRot");
        SpriteRenderer visualSr = pc.GetComponentInChildren<SpriteRenderer>();
        float countdownStart = Time.unscaledTime;

        float xAtCountdownStart = pc.transform.position.x;
        yield return new WaitForSecondsRealtime(0.5f);
        float xMidCountdown = pc.transform.position.x;
        L($"[StartPrep] x unchanged mid-countdown: {Mathf.Approximately(xAtCountdownStart, xMidCountdown)} (expect True - auto-run withheld)");

        // 二重変形バグの回帰確認(2026-09-24) - 実Start絵表示中はVisualの
        // 回転が基準Transformのまま(手続き的な傾きが乗っていない)である
        // ことを確認する。ApplyStartFinishPoseの実イラスト判定分岐の
        // 直接的なガード。
        Transform visualT = (Transform)visualTField.GetValue(anim);
        Quaternion visualBaseRot = (Quaternion)visualBaseRotField.GetValue(anim);
        bool noDoubleTransform = visualT != null && Quaternion.Angle(visualT.localRotation, visualBaseRot) < 0.01f;
        L($"[StartPrep] no double-transform on real Start art (visualT.localRotation==base): {noDoubleTransform} (expect True)");

        // 2段階コマホールドの直接検証(2026-09-24) - カウントダウン総3.0秒
        // (0.8秒×3+0.6秒)に対し、25%時点(0.75秒)はコマ0、90%時点(2.7秒)
        // ではコマ1へ切り替わっていることを確認する。
        while (Time.unscaledTime - countdownStart < 0.75f) yield return null;
        Sprite spriteEarly = visualSr != null ? visualSr.sprite : null;
        while (Time.unscaledTime - countdownStart < 2.7f && gm.CountdownActive) yield return null;
        Sprite spriteLate = visualSr != null ? visualSr.sprite : null;
        L($"[StartPrep] frame differs between 25%/90% of countdown: {spriteEarly != spriteLate} (expect True - held frame0 then switched to frame1)");

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

        PlayerAnimator anim = pc.GetComponent<PlayerAnimator>();
        FieldInfo visualTField = F(typeof(PlayerAnimator), "visualT");
        FieldInfo visualBaseScaleField = F(typeof(PlayerAnimator), "visualBaseScale");

        MethodInfo doFinish = M(typeof(PlayerController), "DoFinishSequence");
        pc.StartCoroutine((IEnumerator)doFinish.Invoke(pc, null));
        yield return null;

        L($"[Finish] IsFinishing={pc.IsFinishing} FinishTierIndex={pc.FinishTierIndex} (expect True, 2)");
        L($"[Finish] PresentationDamageLock={gm.PresentationDamageLock} (expect True)");

        int livesBefore = gm.Lives;
        pc.TakeDamage();
        yield return null;
        L($"[Finish] TakeDamage during Finish has no effect: livesBefore={livesBefore} livesAfter={gm.Lives} (expect equal)");

        // 二重変形バグの回帰確認、Finish側(2026-09-24) - 実Finish絵表示中は
        // Visualのスケールが基準Transformのまま(手続き的な縦つぶれが乗って
        // いない)ことを確認する。
        Transform visualT = (Transform)visualTField.GetValue(anim);
        Vector3 visualBaseScale = (Vector3)visualBaseScaleField.GetValue(anim);
        bool noDoubleTransformFinish = visualT != null && Vector3.Distance(visualT.localScale, visualBaseScale) < 0.001f;
        L($"[Finish] no double-transform on real Finish art (visualT.localScale==base): {noDoubleTransformFinish} (expect True)");

        // Finish→Result間の間(ま)の直接検証(2026-09-24) - 余韻(hold)が
        // 終わりPresentationDamageLockが解除された瞬間から、実際に
        // IsGameOverがtrueになるまでの間にScreenTransitionManagerのclose
        // 分(約0.25秒)以上のギャップがあること、その間IsTransitioning==
        // trueであることを確認する(Win()が画面遷移経由で呼ばれている
        // ことの直接的な証拠)。
        float tLock = 0f;
        while (tLock < 5f && gm.PresentationDamageLock) { yield return null; tLock += Time.deltaTime; }
        float finishSequenceCompleteTime = Time.unscaledTime;

        // IsTransitioningは短時間(合計約0.58秒)しかtrueのままでないため、
        // フラグが立った瞬間をたまたま外すと1回のサンプリングだけでは
        // 見逃しうる。IsGameOverになるまでの間、毎フレームチェックして
        // 「一度でもtrueだった」ことを記録する方式にする(PresentationDamageLock
        // が外れてからIsGameOverになるまでの間、実際に画面遷移が進行して
        // いたことの直接的な証拠)。
        bool transitionManagerNull = ScreenTransitionManager.Instance == null;
        bool sawTransitioning = !transitionManagerNull && ScreenTransitionManager.Instance.IsTransitioning;

        float t = 0f;
        while (t < 5f && !gm.IsGameOver)
        {
            if (!transitionManagerNull && ScreenTransitionManager.Instance.IsTransitioning) sawTransitioning = true;
            yield return null;
            t += Time.deltaTime;
        }
        float resultGap = Time.unscaledTime - finishSequenceCompleteTime;
        L($"[Finish] after hold: IsGameOver={gm.IsGameOver} IsWin={gm.IsWin} PresentationDamageLock={gm.PresentationDamageLock} elapsed={t:F2}s (expect True,True,False)");
        L($"[Finish] Result transition gap={resultGap:F2}s (expect >=0.2s) sawTransitioning={sawTransitioning} (expect True)");
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
