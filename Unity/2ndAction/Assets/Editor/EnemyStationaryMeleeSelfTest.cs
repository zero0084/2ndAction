using System.Reflection;
using UnityEditor;
using UnityEngine;

// 敵AI行動Tier試験実装(2026-09-16) - T1/T2の近接攻撃AI(EnemySpecialBehavior
// のStationaryMelee kind)が、Hit Reaction/Knockback/Launchによる中断
// (EnemyController.DisableMotionComponents/RestoreMotionComponentsが
// special.enabled=false/trueを切り替える)を挟んでも「AI Stateが壊れて
// 棒立ちになったり、永遠にAttack状態になったりしない」ことを直接検証する。
// マスターのAcceptance Test項目14/15に対応する再発防止テスト。
// EnemyGroundKnockbackSelfTest.cs等と同じ「Edit-mode単発実行、Awake/Start/
// OnEnable/OnDisableをreflectionで直接呼ぶ」パターン(Edit-modeではTime.
// deltaTimeが進まないため、実際のUpdate()連続実行によるタイマー消化では
// なく、状態そのものを直接書き換えて中断シナリオを模擬する)。
public static class EnemyStationaryMeleeSelfTest
{
    [MenuItem("Tools/2ndAction/Self-Test: Enemy Stationary Melee AI Interrupt-Safety")]
    public static void Run()
    {
        GameObject enemyGO = GroundFactory.CreateEnemy(
            parent: null,
            sprite: null,
            position: new Vector2(0f, 0f),
            color: Color.white,
            behaviorKind: EnemyBehaviorKind.StationaryMelee,
            aiTier: EnemyAiTier.T1);

        var special = enemyGO.GetComponent<EnemySpecialBehavior>();
        if (special == null)
        {
            Debug.LogError("[StationaryMeleeSelfTest] FAIL - EnemySpecialBehavior not found on the created enemy.");
            Object.DestroyImmediate(enemyGO);
            return;
        }

        InvokePrivate(special, "Start");

        // 「攻撃中に被弾した」状況を模擬する - Attack状態まで強制的に進め、
        // 攻撃判定(Hitbox)も実際にアクティブにする。
        SetPrivateField(special, "meleeAttackState", GetNestedEnumValue(special, "MeleeAttackState", "Attack"));
        var hitboxGO = (GameObject)GetPrivateField(special, "meleeHitboxGO");
        if (hitboxGO == null)
        {
            Debug.LogError("[StationaryMeleeSelfTest] FAIL - meleeHitboxGO was never created by InitStationaryMelee.");
            Object.DestroyImmediate(enemyGO);
            return;
        }
        hitboxGO.SetActive(true);

        // EnemyController.DisableMotionComponents/RestoreMotionComponentsが
        // 実際に行っているのと同じ操作 - special.enabled = false → true。
        // Play Mode中はこのenabled切り替えだけでUnityが自動的にOnDisable/
        // OnEnableを呼ぶが、このセルフテストはEdit-mode単発実行(Awake/
        // Start等を全てreflectionで手動起動している)であり、Unityの
        // メッセージシステム自体が動いていないためenabled代入だけでは
        // OnDisable/OnEnableが呼ばれない。実際のPlay Mode動作を模擬する
        // ため、他のメソッド同様reflectionで明示的に呼び出す。
        special.enabled = false;
        InvokePrivate(special, "OnDisable");
        special.enabled = true;
        InvokePrivate(special, "OnEnable");

        var attackStateAfter = GetPrivateField(special, "meleeAttackState");
        bool stateIsIdle = attackStateAfter.ToString() == "Idle";
        bool hitboxIsOff = !hitboxGO.activeSelf;

        bool pass = stateIsIdle && hitboxIsOff;
        string result = pass ? "PASS" : "FAIL";
        Debug.Log($"[StationaryMeleeSelfTest] {result} - stateAfterReenable={attackStateAfter} (expected Idle), hitboxActiveAfterReenable={hitboxGO.activeSelf} (expected False)");

        if (!pass)
        {
            Debug.LogError("[StationaryMeleeSelfTest] FAIL - re-enabling EnemySpecialBehavior after a hit-interrupt did not " +
                            "cleanly reset to Idle with the attack hitbox hidden - this is exactly the 'AI State壊れる/" +
                            "永遠にAttack状態' failure mode the brief explicitly warned against.");
        }

        Object.DestroyImmediate(enemyGO);
    }

    static void InvokePrivate(object target, string methodName, params object[] args)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (method == null)
        {
            Debug.LogError($"[StationaryMeleeSelfTest] Method not found: {target.GetType().Name}.{methodName}");
            return;
        }
        method.Invoke(target, args);
    }

    static object GetPrivateField(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
        {
            Debug.LogError($"[StationaryMeleeSelfTest] Field not found: {target.GetType().Name}.{fieldName}");
            return null;
        }
        return field.GetValue(target);
    }

    static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
        {
            Debug.LogError($"[StationaryMeleeSelfTest] Field not found: {target.GetType().Name}.{fieldName}");
            return;
        }
        field.SetValue(target, value);
    }

    // MeleeAttackState is a private nested enum on EnemySpecialBehavior -
    // resolve it by name via the declaring type's nested types rather than
    // referencing it directly (it isn't accessible from this Editor
    // assembly otherwise).
    static object GetNestedEnumValue(object target, string enumTypeName, string valueName)
    {
        System.Type declaringType = target.GetType();
        System.Type enumType = declaringType.GetNestedType(enumTypeName, BindingFlags.NonPublic);
        if (enumType == null)
        {
            Debug.LogError($"[StationaryMeleeSelfTest] Nested enum not found: {declaringType.Name}.{enumTypeName}");
            return null;
        }
        return System.Enum.Parse(enumType, valueName);
    }
}
