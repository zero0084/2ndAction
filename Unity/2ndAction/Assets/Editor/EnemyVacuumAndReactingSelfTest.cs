using System.Reflection;
using UnityEditor;
using UnityEngine;

// 実機フィードバック(2026-09-12第5弾) - 「Playerの上攻撃が命中している
// のに、ほぼ同時にPlayer側もダメージを受ける」相打ちの防止
// (EnemyController.IsReactingToHit)と、頭上のEnemyを拾い直すPickup/
// Vacuum(EnemyController.TryVacuumPickup)を検証する。Play Modeでの
// 複数フレームにわたる吸い込み移動そのものはこのセッションで確認でき
// ないため、他の自己診断テスト同様「呼び出し直後の状態」だけを対象に
// する。
public static class EnemyVacuumAndReactingSelfTest
{
    [MenuItem("Tools/2ndAction/Self-Test: Enemy Vacuum And Reacting-To-Hit")]
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
            Debug.LogError("[VacuumAndReactingSelfTest] FAIL - EnemyController/EnemyAnimator not found.");
            Object.DestroyImmediate(enemyGO);
            return;
        }

        InvokePrivate(controller, "Awake");
        InvokePrivate(animator, "Start");

        // ①IsReactingToHit - 完全に静止した通常の敵は「攻撃に反応中」では
        // ないはず(=接触ダメージは通常どおり発生してよい)。ここがtrueに
        // なっていると、今回の相打ち対策が誤って常時ダメージを無効化して
        // しまっていることになる。
        bool reactingWhileIdle = (bool)GetProperty(controller, "IsReactingToHit");

        // ②上攻撃で打ち上げ、その状態でIsReactingToHitがtrueになるか
        // (Launch中の接触ダメージ無効化)。
        InvokePrivate(controller, "LaunchUpward");
        bool reactingWhileLaunched = (bool)GetProperty(controller, "IsReactingToHit");

        // ③Vacuum - 既に空中(isLaunched=true)の敵に対してTryVacuumPickup
        // を呼ぶと、beingVacuumedがtrueになり、Update()の通常Launch物理が
        // スキップされる(=launchVelocityYがこの1フレームでは変化しない)
        // ことを確認する。
        float launchVelocityYBeforeVacuum = (float)GetField(controller, "launchVelocityY");
        controller.TryVacuumPickup(new Vector2(1f, 1f), 0.12f);
        bool beingVacuumedAfterCall = (bool)GetField(controller, "beingVacuumed");
        bool reactingWhileVacuumed = (bool)GetProperty(controller, "IsReactingToHit");

        InvokePrivate(controller, "Update");
        float launchVelocityYAfterVacuumUpdate = (float)GetField(controller, "launchVelocityY");

        bool pass = !reactingWhileIdle
            && reactingWhileLaunched
            && beingVacuumedAfterCall
            && reactingWhileVacuumed
            && Mathf.Approximately(launchVelocityYBeforeVacuum, launchVelocityYAfterVacuumUpdate);

        string result = pass ? "PASS" : "FAIL";
        Debug.Log($"[VacuumAndReactingSelfTest] {result} - reactingWhileIdle={reactingWhileIdle} " +
                  $"reactingWhileLaunched={reactingWhileLaunched} beingVacuumedAfterCall={beingVacuumedAfterCall} " +
                  $"reactingWhileVacuumed={reactingWhileVacuumed} " +
                  $"launchVelocityY(before={launchVelocityYBeforeVacuum:F2}, afterUpdate={launchVelocityYAfterVacuumUpdate:F2})");

        if (!pass)
        {
            Debug.LogError("[VacuumAndReactingSelfTest] FAIL - either the idle-enemy contact-damage regression " +
                            "guard failed (reactingWhileIdle should be false), the Launched-state contact-damage " +
                            "suppression didn't engage, TryVacuumPickup didn't set beingVacuumed, or Update() still " +
                            "ran the normal Launch physics while beingVacuumed (which would fight the vacuum pull " +
                            "coroutine for the same Transform).");
        }

        Object.DestroyImmediate(enemyGO);
    }

    static void InvokePrivate(object target, string methodName, params object[] args)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (method == null)
        {
            Debug.LogError($"[VacuumAndReactingSelfTest] Method not found: {target.GetType().Name}.{methodName}");
            return;
        }
        method.Invoke(target, args);
    }

    static object GetField(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (field == null)
        {
            Debug.LogError($"[VacuumAndReactingSelfTest] Field not found: {target.GetType().Name}.{fieldName}");
            return null;
        }
        return field.GetValue(target);
    }

    static object GetProperty(object target, string propertyName)
    {
        PropertyInfo prop = target.GetType().GetProperty(propertyName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (prop == null)
        {
            Debug.LogError($"[VacuumAndReactingSelfTest] Property not found: {target.GetType().Name}.{propertyName}");
            return null;
        }
        return prop.GetValue(target);
    }
}
