using System.Reflection;
using UnityEditor;
using UnityEngine;

// 不具合修正(2026-09-12) - 「上攻撃を当ててもゴブリンがほぼ浮かない」の
// 根本原因(EnemyController.AwakeでEnemyAnimator/EnemySpecialBehaviorを
// キャッシュしていたが、GroundFactory.CreateEnemyの追加順(EnemyController
// が先、EnemyAnimatorが後)によりAwake時点では常にnullだった - Unityは
// AddComponent時にAwakeを同期実行するため)を修正した際、Play Modeでの
// 複数フレーム物理を自動検証する手段がこのセッションには無かったため、
// 代わりに「打ち上げ処理そのもの(EnemyController.ProcessHit)を1回呼んだ
// 直後に、実際にEnemyAnimatorが無効化されているか」をEdit-mode単発実行
// で直接検査するセルフテストとして作成した。AttackVfxCapture.cs/
// PortraitPreviewCapture.cs同様、今後も再発防止の確認用に常設しておく。
public static class EnemyAerialComboSelfTest
{
    [MenuItem("Tools/2ndAction/Self-Test: Enemy Aerial Combo Launch")]
    public static void Run()
    {
        // 不具合修正(2026-09-12) - 初版はProcessHit経由でReactToHit
        // (StartCoroutine→HitStop.Freezeを含む)まで実行してしまい、Edit
        // Mode下ではPlayer Loopが回っていないためHitStop側のyield以降が
        // 二度と再開せず、Time.timeScale=0のままProjectSettings/
        // TimeManager.assetへ永続保存される事故が起きた(このツール自体の
        // 実行が原因でゲーム起動時に画面が固まる不具合を生みかねなかった)。
        // 対策として①検査対象をLaunchUpward単体(HitStopを含まない)に絞り
        // ②念のためTime.timeScaleを実行前後で明示的に保存/復元する。
        float savedTimeScale = Time.timeScale;
        try
        {
            RunInner();
        }
        finally
        {
            Time.timeScale = savedTimeScale;
        }
    }

    static void RunInner()
    {
        GameObject enemyGO = GroundFactory.CreateEnemy(
            parent: null,
            sprite: null,
            position: new Vector2(0f, 0f),
            color: Color.white);

        var controller = enemyGO.GetComponent<EnemyController>();
        var animator = enemyGO.GetComponent<EnemyAnimator>();
        if (controller == null || animator == null)
        {
            Debug.LogError("[AerialComboSelfTest] FAIL - EnemyController/EnemyAnimator not found on the created enemy.");
            Object.DestroyImmediate(enemyGO);
            return;
        }

        // Edit-mode生成なのでAwake/Startがまだ走っていない - AttackVfxCapture
        // と同じ手法(リフレクションで強制的にAwake()を呼ぶ)を使う。
        InvokePrivate(controller, "Awake");
        InvokePrivate(animator, "Start");

        bool animatorEnabledBefore = animator.enabled;
        float yBefore = enemyGO.transform.position.y;

        // OnTriggerEnter2D/ProcessHitを経由せず、打ち上げ物理そのもの
        // (LaunchUpward)を直接呼ぶ - Collider2D/PlayerController.Instance
        // 等の実機セットアップや、ReactToHit内のHitStop.Freeze(Edit Mode
        // ではPlayer Loopが回らずyield以降が再開しない)に一切依存せず、
        // 「打ち上げロジック自体が正しく状態を変更するか」だけを検査する。
        InvokePrivate(controller, "LaunchUpward");

        bool isLaunchedAfter = (bool)GetPrivateField(controller, "isLaunched");
        float launchVelocityYAfter = (float)GetPrivateField(controller, "launchVelocityY");
        bool animatorEnabledAfter = animator.enabled;

        bool pass = isLaunchedAfter && launchVelocityYAfter > 0f && animatorEnabledBefore && !animatorEnabledAfter;

        string result = pass ? "PASS" : "FAIL";
        Debug.Log($"[AerialComboSelfTest] {result} - " +
                  $"animatorEnabled(before={animatorEnabledBefore}, after={animatorEnabledAfter}) " +
                  $"isLaunched={isLaunchedAfter} launchVelocityY={launchVelocityYAfter:F2} " +
                  $"y(before={yBefore:F2})");

        if (!pass)
        {
            Debug.LogError("[AerialComboSelfTest] FAIL - Launch state did not apply correctly, or EnemyAnimator " +
                            "was not disabled (it would keep resetting transform.position every frame and cancel " +
                            "the launch, which is exactly the bug this test guards against).");
        }

        Object.DestroyImmediate(enemyGO);
    }

    static void InvokePrivate(object target, string methodName, params object[] args)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (method == null)
        {
            Debug.LogError($"[AerialComboSelfTest] Method not found: {target.GetType().Name}.{methodName}");
            return;
        }
        method.Invoke(target, args);
    }

    static object GetPrivateField(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
        {
            Debug.LogError($"[AerialComboSelfTest] Field not found: {target.GetType().Name}.{fieldName}");
            return null;
        }
        return field.GetValue(target);
    }
}
