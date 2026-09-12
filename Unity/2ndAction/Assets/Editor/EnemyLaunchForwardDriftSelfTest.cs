using System.Reflection;
using UnityEditor;
using UnityEngine;

// 実機フィードバック追加調整(2026-09-12第2弾) - マスターから「Launch後に
// 主人公と敵の横方向の位置がズレやすい」との報告。原因は前回パスの実装
// (launchVelocityX = 主人公の自動前進速度 + 上乗せ分、をLaunch中ずっと
// 適用し続けていた)が、実質的に主人公より常に一定量速い速度で敵を進ませ
// 続けるものだったため、滞空時間が長いほど両者の距離が際限なく開いて
// いく設計ミスだったこと。修正: 上乗せ分は「Launchの瞬間だけ」の短時間
// バースト(launchForwardBurstDuration秒で線形に0へ減衰)にし、それ以外
// は常に主人公と全く同じ速度(パリティ)で並走するようにした。
//
// Play Modeで実際に時間経過を追って検証する手段がこのセッションには無い
// ため、代わりに「バーストタイマーが尽きた(=0になった)状態でUpdate()を
// 1回呼んだとき、launchVelocityXが主人公の速度ちょうどに戻るか(バースト
// 分が永久に残り続けていないか)」をEdit-mode単発実行で直接検査する。
public static class EnemyLaunchForwardDriftSelfTest
{
    [MenuItem("Tools/2ndAction/Self-Test: Enemy Launch Forward Drift")]
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
            Debug.LogError("[LaunchForwardDriftSelfTest] FAIL - EnemyController/EnemyAnimator not found on the created enemy.");
            Object.DestroyImmediate(enemyGO);
            return;
        }

        InvokePrivate(controller, "Awake");
        InvokePrivate(animator, "Start");

        // PlayerController.Instanceはこのテストの実行環境(Edit Mode、
        // シーン上のPlayerのAwakeは走っていない)ではnullになる - つまり
        // PlayerForwardSpeed()は0を返す。これはテストとして都合が良く、
        // 「バースト分が残っていないか」を「launchVelocityXがちょうど0
        // に戻るか」という単純な等値チェックで検証できる。
        InvokePrivate(controller, "LaunchUpward");

        float velocityXRightAfterLaunch = (float)GetPrivateField(controller, "launchVelocityX");
        float burstVelocity = (float)GetPrivateField(controller, "launchForwardBurstVelocity");

        // Update()は「着地判定(position.y <= floor)」も兼ねている - この
        // テストはEdit-mode単発呼び出しでTime.deltaTimeが0(または極小)の
        // ため、重力によるY上昇が実際には起きておらず、そのままUpdate()を
        // 呼ぶとスポーン時のY(=floor)とちょうど一致し、即座にLandFromLaunch
        // が起動してlaunchVelocityXが「着地したから」0になってしまい、
        // 検証したいバースト減衰の挙動と区別が付かなくなる。着地判定に
        // 引っかからないよう、Yを少し持ち上げてから検証する。
        enemyGO.transform.position += new Vector3(0f, 1f, 0f);

        // バーストが尽きた状態を直接模擬する(実時間の経過を待つ代わりに)。
        SetPrivateField(controller, "launchForwardBurstTimer", 0f);
        InvokePrivate(controller, "Update");

        float velocityXAfterBurstExpired = (float)GetPrivateField(controller, "launchVelocityX");

        bool pass = burstVelocity > 0f
            && Mathf.Abs(velocityXRightAfterLaunch - burstVelocity) < 0.01f
            && Mathf.Abs(velocityXAfterBurstExpired) < 0.0001f;

        string result = pass ? "PASS" : "FAIL";
        Debug.Log($"[LaunchForwardDriftSelfTest] {result} - burstVelocity={burstVelocity:F2} " +
                  $"velocityXRightAfterLaunch={velocityXRightAfterLaunch:F2} velocityXAfterBurstExpired={velocityXAfterBurstExpired:F2}");

        if (!pass)
        {
            Debug.LogError("[LaunchForwardDriftSelfTest] FAIL - launchVelocityX did not converge back to the " +
                            "player's speed (0 in this test, since PlayerController.Instance is null in Edit Mode) " +
                            "once the forward burst expired - this is exactly the bug that made the enemy drift " +
                            "further and further ahead of the player the longer it stayed airborne.");
        }

        Object.DestroyImmediate(enemyGO);
    }

    static void InvokePrivate(object target, string methodName, params object[] args)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (method == null)
        {
            Debug.LogError($"[LaunchForwardDriftSelfTest] Method not found: {target.GetType().Name}.{methodName}");
            return;
        }
        method.Invoke(target, args);
    }

    static object GetPrivateField(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
        {
            Debug.LogError($"[LaunchForwardDriftSelfTest] Field not found: {target.GetType().Name}.{fieldName}");
            return null;
        }
        return field.GetValue(target);
    }

    static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
        {
            Debug.LogError($"[LaunchForwardDriftSelfTest] Field not found: {target.GetType().Name}.{fieldName}");
            return;
        }
        field.SetValue(target, value);
    }
}
