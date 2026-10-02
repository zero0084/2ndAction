using System.Reflection;
using UnityEditor;
using UnityEngine;

// 実機フィードバック(2026-09-12第3弾) - 「地上通常攻撃のノックバックが
// 距離ベースだと、押された直後にすぐ止まり、主人公の自動前進速度にすぐ
// 追いつかれてしまう」との報告を受け、EnemyController.ApplyGroundKnockback
// を「一定距離だけ瞬間移動」から「主人公の現在速度を基準にした速度を、
// 短時間だけ主人公より速く与える」方式へ全面変更した。この変更の核心
// (固定値ではなく主人公の現在速度を基準にする、という設計そのもの)を
// Edit-mode単発実行で直接検査する。Play Modeでの複数フレームにわたる
// 減衰・着地判定等はこのセッションで検証できないため、EnemyGroundKnockback
// SelfTest.cs同様「1回の呼び出し直後の状態」だけを対象にする。
public static class EnemyGroundKnockbackVelocitySelfTest
{
    [MenuItem("Tools/2ndAction/Self-Test: Enemy Ground Knockback Velocity")]
    public static void Run()
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
            Debug.LogError("[GroundKnockbackVelocitySelfTest] FAIL - EnemyController/EnemyAnimator not found on the created enemy.");
            Object.DestroyImmediate(enemyGO);
            return;
        }

        InvokePrivate(controller, "Awake");
        InvokePrivate(animator, "Start");

        bool animatorEnabledBefore = animator.enabled;

        // PlayerController.Instanceはこのテスト環境(Edit Mode、シーン上
        // のPlayerのAwakeは走っていない)ではnullになるため、期待される
        // 速度は「0(主人公の速度) + groundKnockbackSpeedBonus」になる -
        // これ自体が「固定値ではなく主人公の速度を基準にする」という式の
        // 配線を直接検査していることになる(実機ではPlayerController.
        // CurrentAutoRunSpeedがそのまま上乗せされる)。
        float speedBonus = (float)GetPrivateOrPublicField(controller, "groundKnockbackSpeedBonus");
        float duration = (float)GetPrivateOrPublicField(controller, "groundKnockbackDuration");

        InvokePrivate(controller, "ApplyGroundKnockback");

        float velocityXAfter = (float)GetPrivateOrPublicField(controller, "groundKnockbackVelocityX");
        float timerAfter = (float)GetPrivateOrPublicField(controller, "groundKnockbackTimer");
        bool animatorEnabledAfter = animator.enabled;

        bool pass = Mathf.Abs(velocityXAfter - speedBonus) < 0.01f
            && Mathf.Abs(timerAfter - duration) < 0.01f
            && animatorEnabledBefore && !animatorEnabledAfter;

        string result = pass ? "PASS" : "FAIL";
        Debug.Log($"[GroundKnockbackVelocitySelfTest] {result} - speedBonus={speedBonus:F2} " +
                  $"velocityXAfter={velocityXAfter:F2} timerAfter={timerAfter:F2} " +
                  $"animatorEnabled(before={animatorEnabledBefore}, after={animatorEnabledAfter})");

        if (!pass)
        {
            Debug.LogError("[GroundKnockbackVelocitySelfTest] FAIL - ApplyGroundKnockback did not set " +
                            "groundKnockbackVelocityX/Timer as expected, or did not disable EnemyAnimator/" +
                            "EnemySpecialBehavior (which would let them fight the new Update()-driven X movement, " +
                            "the same failure pattern as the earlier Launch/EnemyAnimator bugs this session).");
        }

        Object.DestroyImmediate(enemyGO);
    }

    static void InvokePrivate(object target, string methodName, params object[] args)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (method == null)
        {
            Debug.LogError($"[GroundKnockbackVelocitySelfTest] Method not found: {target.GetType().Name}.{methodName}");
            return;
        }
        method.Invoke(target, args);
    }

    static object GetPrivateOrPublicField(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (field == null)
        {
            Debug.LogError($"[GroundKnockbackVelocitySelfTest] Field not found: {target.GetType().Name}.{fieldName}");
            return null;
        }
        return field.GetValue(target);
    }
}
