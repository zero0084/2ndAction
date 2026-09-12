using System.Reflection;
using UnityEditor;
using UnityEngine;

// 実機フィードバック(2026-09-12第4弾) - 「足場のない場所へ敵が吹き飛ば
// された後もその場で立った状態になることがある」の修正
// (EnemyController.UpdateNormalGroundCheck新設)を検証する。この環境には
// TerrainManagerのインスタンスが存在しない(Edit-mode、シーンのAwakeが
// 走っていない)ため、GetHeightAtは常にnullを返す - これは「地面が全く
// 存在しない状況」を模擬するのに好都合で、①足元の地面判定が無ければ
// 即座にAirborneへ切り替わるか②デッドラインを下回ったら落下死処理が
// 走るか、の2点を直接検査できる(逆に「着地」は地面が要るため、この
// 環境では検証できない - 他の自己診断テスト同様、複数フレームの物理
// 挙動そのものはPlay Modeでしか確認できない、という既知の制約)。
public static class EnemyGroundCheckSelfTest
{
    [MenuItem("Tools/2ndAction/Self-Test: Enemy Ground Check Fall")]
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
            Debug.LogError("[GroundCheckSelfTest] FAIL - EnemyController/EnemyAnimator not found on the created enemy.");
            Object.DestroyImmediate(enemyGO);
            return;
        }

        InvokePrivate(controller, "Awake");
        InvokePrivate(animator, "Start");

        bool normalGroundedBefore = (bool)GetField(controller, "normalGrounded");
        bool animatorEnabledBefore = animator.enabled;

        // TerrainManager.Instanceがnullのこの環境では、GetHeightAtは常に
        // nullを返す(=地面が存在しない状況を模擬)。
        InvokePrivate(controller, "UpdateNormalGroundCheck");

        bool normalGroundedAfterFirstCall = (bool)GetField(controller, "normalGrounded");
        bool animatorEnabledAfterFirstCall = animator.enabled;

        bool fallDetectionPass = normalGroundedBefore && !normalGroundedAfterFirstCall
            && animatorEnabledBefore && !animatorEnabledAfterFirstCall;

        // デッドライン判定 - YをenemyDeathBelowYより下へ強制的に動かしてから
        // もう一度呼び、落下死処理(dying=true+非アクティブ化)が走るかを見る。
        float deathBelowY = (float)GetField(controller, "enemyDeathBelowY");
        enemyGO.transform.position = new Vector3(0f, deathBelowY - 1f, 0f);
        InvokePrivate(controller, "UpdateNormalGroundCheck");

        bool dyingAfter = (bool)GetField(controller, "dying");
        bool activeAfter = enemyGO.activeSelf;
        bool deathPass = dyingAfter && !activeAfter;

        bool pass = fallDetectionPass && deathPass;
        string result = pass ? "PASS" : "FAIL";
        Debug.Log($"[GroundCheckSelfTest] {result} - " +
                  $"fallDetection(normalGrounded {normalGroundedBefore}->{normalGroundedAfterFirstCall}, " +
                  $"animatorEnabled {animatorEnabledBefore}->{animatorEnabledAfterFirstCall}) " +
                  $"deathBelowLine(dying={dyingAfter}, activeSelf={activeAfter})");

        if (!pass)
        {
            Debug.LogError("[GroundCheckSelfTest] FAIL - either losing ground didn't flip normalGrounded/disable " +
                            "EnemyAnimator (the enemy would keep 'standing' with no ground beneath it), or falling " +
                            "below the deadline didn't trigger the fall-death path.");
        }

        // activeAfterがfalseの場合、既に非アクティブ化されているため
        // DestroyImmediateは冪等に安全に呼べる。
        Object.DestroyImmediate(enemyGO);
    }

    static void InvokePrivate(object target, string methodName, params object[] args)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (method == null)
        {
            Debug.LogError($"[GroundCheckSelfTest] Method not found: {target.GetType().Name}.{methodName}");
            return;
        }
        method.Invoke(target, args);
    }

    static object GetField(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (field == null)
        {
            Debug.LogError($"[GroundCheckSelfTest] Field not found: {target.GetType().Name}.{fieldName}");
            return null;
        }
        return field.GetValue(target);
    }
}
