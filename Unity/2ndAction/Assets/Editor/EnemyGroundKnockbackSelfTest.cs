using System.Reflection;
using UnityEditor;
using UnityEngine;

// 不具合修正(2026-09-12) - 「地上通常攻撃のノックバックが効かない、敵がそ
// の場に留まりやすい」の根本原因(EnemyAnimator.Updateが非Flying・非
// SpecialBehaviorの敵に対し、毎フレームtransform.position全体を無条件で
// Start()時のbasePosへ書き戻していたため、EnemyController.KnockbackRoutine
// によるX方向の移動がコルーチン終了直後の次フレームで即座に巻き戻されて
// いた)を修正した際、Play Modeでの複数フレームにわたるコルーチン実行を
// このセッションで自動検証する手段が無いため、代わりに「EnemyAnimator.
// Update()を1回呼んだだけで、直前に外部から動かしたX座標が保持されるか」
// をEdit-mode単発実行で直接検査するセルフテストとして作成した。
// EnemyAerialComboSelfTest.cs同様、今後も再発防止の確認用に常設しておく。
public static class EnemyGroundKnockbackSelfTest
{
    [MenuItem("Tools/2ndAction/Self-Test: Enemy Ground Knockback Position")]
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
            Debug.LogError("[GroundKnockbackSelfTest] FAIL - EnemyController/EnemyAnimator not found on the created enemy.");
            Object.DestroyImmediate(enemyGO);
            return;
        }

        // Edit-mode生成なのでAwake/Startがまだ走っていない - basePosは
        // EnemyAnimator.Start()内でtransform.positionをキャプチャする
        // ため、必ずスポーン位置(x=0)より先に呼ぶ。
        InvokePrivate(controller, "Awake");
        InvokePrivate(animator, "Start");

        float spawnX = enemyGO.transform.position.x;

        // EnemyController.KnockbackRoutineが1フレーム分位置を書き込んだ
        // 状況を模擬する(実際のコルーチンを複数フレーム回すことはこの
        // 環境ではできないため、「その1回の書き込みが次のUpdate呼び出し
        // で保持されるか」だけを直接検査する)。
        float knockedX = spawnX + 0.2f;
        enemyGO.transform.position = new Vector3(knockedX, enemyGO.transform.position.y, 0f);

        InvokePrivate(animator, "Update");

        float xAfterUpdate = enemyGO.transform.position.x;
        bool pass = Mathf.Abs(xAfterUpdate - knockedX) < 0.0001f;

        string result = pass ? "PASS" : "FAIL";
        Debug.Log($"[GroundKnockbackSelfTest] {result} - spawnX={spawnX:F2} knockedX={knockedX:F2} xAfterAnimatorUpdate={xAfterUpdate:F2}");

        if (!pass)
        {
            Debug.LogError("[GroundKnockbackSelfTest] FAIL - EnemyAnimator.Update() reset the X position back toward " +
                            "spawn instead of preserving it - this is exactly the bug that made ground knockback " +
                            "invisible (KnockbackRoutine's displacement gets erased the frame after it finishes).");
        }

        Object.DestroyImmediate(enemyGO);
    }

    static void InvokePrivate(object target, string methodName, params object[] args)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (method == null)
        {
            Debug.LogError($"[GroundKnockbackSelfTest] Method not found: {target.GetType().Name}.{methodName}");
            return;
        }
        method.Invoke(target, args);
    }
}
