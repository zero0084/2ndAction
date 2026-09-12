using System.Reflection;
using UnityEditor;
using UnityEngine;

// Stage01 荒野街道 最小実装(2026-09-13) - ObstacleController(石/小木/壁/
// 壊せる木/巨大石の共通ランタイム)の核心ロジックをEdit-mode単発実行で
// 直接検証する。他の自己診断テストと同じ「1回の呼び出し直後の状態」だけ
// を対象にする制約はここでも同じ - OnTriggerEnter2Dを実際の物理コール
// バックではなく直接reflection経由で呼び出す。
public static class ObstacleSelfTest
{
    [MenuItem("Tools/2ndAction/Self-Test: Obstacle")]
    public static void Run()
    {
        // 1) breakable=false(石/小木/壁/巨大石相当) - PlayerAttackタグには
        // 一切反応せず、Playerタグとの接触で即座に退場する。
        GameObject solidGO = CreateObstacleGO(breakable: false, hp: 1);
        ObstacleController solid = solidGO.GetComponent<ObstacleController>();
        GameObject attackerGO = CreateTaggedCollider("PlayerAttack");
        InvokeTrigger(solid, attackerGO.GetComponent<Collider2D>());
        bool ignoredAttackWhenNotBreakable = solidGO.activeSelf; // PlayerAttackでは反応しないので生きたまま

        GameObject playerGO = CreateTaggedCollider("Player");
        InvokeTrigger(solid, playerGO.GetComponent<Collider2D>());
        bool solidDeactivatedOnPlayerContact = !solidGO.activeSelf;

        // 2) breakable=true(壊せる木) hp=2 - PlayerAttack2回で破壊される
        // (1回目では破壊されない)。Player本体との接触だけでは破壊されない
        // (「攻撃でも道を開ける」役割分担 - 破壊は攻撃でのみ)。
        GameObject breakableGO = CreateObstacleGO(breakable: true, hp: 2);
        ObstacleController breakable = breakableGO.GetComponent<ObstacleController>();
        InvokeTrigger(breakable, attackerGO.GetComponent<Collider2D>());
        bool aliveAfterFirstHit = breakableGO.activeSelf;
        int hpAfterFirstHit = (int)GetPrivateOrPublicField(breakable, "hp");

        GameObject playerGO2 = CreateTaggedCollider("Player");
        InvokeTrigger(breakable, playerGO2.GetComponent<Collider2D>());
        bool aliveAfterPlayerBodyContact = breakableGO.activeSelf; // 破壊されない(攻撃でのみ破壊される)

        InvokeTrigger(breakable, attackerGO.GetComponent<Collider2D>());
        bool destroyedAfterSecondHit = !breakableGO.activeSelf;

        bool pass = ignoredAttackWhenNotBreakable && solidDeactivatedOnPlayerContact
            && aliveAfterFirstHit && hpAfterFirstHit == 1 && aliveAfterPlayerBodyContact && destroyedAfterSecondHit;

        string result = pass ? "PASS" : "FAIL";
        Debug.Log($"[ObstacleSelfTest] {result} - ignoredAttackWhenNotBreakable={ignoredAttackWhenNotBreakable} " +
                  $"solidDeactivatedOnPlayerContact={solidDeactivatedOnPlayerContact} aliveAfterFirstHit={aliveAfterFirstHit} " +
                  $"hpAfterFirstHit={hpAfterFirstHit} aliveAfterPlayerBodyContact={aliveAfterPlayerBodyContact} " +
                  $"destroyedAfterSecondHit={destroyedAfterSecondHit}");

        if (!pass)
        {
            Debug.LogError("[ObstacleSelfTest] FAIL - see individual flags above for which check failed.");
        }

        Object.DestroyImmediate(solidGO);
        Object.DestroyImmediate(breakableGO);
        Object.DestroyImmediate(attackerGO);
        Object.DestroyImmediate(playerGO);
        Object.DestroyImmediate(playerGO2);
    }

    static GameObject CreateObstacleGO(bool breakable, int hp)
    {
        GameObject go = new GameObject("TestObstacle", typeof(BoxCollider2D));
        go.GetComponent<BoxCollider2D>().isTrigger = true;
        ObstacleController controller = go.AddComponent<ObstacleController>();
        controller.breakable = breakable;
        controller.hp = hp;
        return go;
    }

    static GameObject CreateTaggedCollider(string tag)
    {
        GameObject go = new GameObject("TestCollider_" + tag, typeof(BoxCollider2D));
        go.tag = tag;
        go.GetComponent<BoxCollider2D>().isTrigger = true;
        return go;
    }

    static void InvokeTrigger(ObstacleController target, Collider2D other)
    {
        MethodInfo method = typeof(ObstacleController).GetMethod("OnTriggerEnter2D", BindingFlags.NonPublic | BindingFlags.Instance);
        if (method == null)
        {
            Debug.LogError("[ObstacleSelfTest] Method not found: ObstacleController.OnTriggerEnter2D");
            return;
        }
        method.Invoke(target, new object[] { other });
    }

    static object GetPrivateOrPublicField(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (field == null)
        {
            Debug.LogError($"[ObstacleSelfTest] Field not found: {target.GetType().Name}.{fieldName}");
            return null;
        }
        return field.GetValue(target);
    }
}
