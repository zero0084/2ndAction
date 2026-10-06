#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// BOSS FINISH の確認用(開発版だけ): ランの中でボスを出し、登場が終わったら指定の攻撃で倒す(通常の撃破と同じ処理)。
public class BossFinishDebug : MonoBehaviour
{
    static BossFinishDebug runner;
    public static int Family; // 0 荒野 / 1 洞窟 / 2 天空
    public static int Kind;
    public static BossFinalAttack Attack = BossFinalAttack.Forward;
    public static string Status = "";

    public static void SpawnAndKill(int family, int kind, BossFinalAttack attack)
    {
        if (runner == null) runner = new GameObject("[BossFinishDebug]").AddComponent<BossFinishDebug>();
        runner.StopAllCoroutines();
        runner.StartCoroutine(runner.Run(family, kind, attack));
    }

    IEnumerator Run(int family, int kind, BossFinalAttack attack)
    {
        var bm = BossManager.Instance;
        if (bm == null) { Status = "ランの中で使ってください"; yield break; }
        bm.DebugSpawnBossForTest(family, kind);
        Status = "登場を待っています…";
        float w = 0f;
        Object target = null;
        while (w < 12f)
        {
            yield return null; w += Time.unscaledDeltaTime;
            var wb = FindObjectsByType<WildBossBase>(FindObjectsSortMode.None).FirstOrDefault(b => b != null && !b.IsDead && b.isActiveAndEnabled && !b.IsEntering);
            if (wb != null && w > 1.5f) { target = wb; break; }
            var dr = FindObjectsByType<DragonController>(FindObjectsSortMode.None).FirstOrDefault(b => b != null && b.isActiveAndEnabled);
            if (dr != null && w > 3f) { target = dr; break; }
            var mj = FindObjectsByType<MajinController>(FindObjectsSortMode.None).FirstOrDefault(b => b != null && b.isActiveAndEnabled);
            if (mj != null && w > 3f) { target = mj; break; }
        }
        yield return new WaitForSeconds(0.6f);
        bool ok = false;
        if (target is WildBossBase b1) ok = b1.DebugKillWithAttack(attack);
        else if (target is DragonController b2) ok = b2.DebugKillWithAttack(attack);
        else if (target is MajinController b3) ok = b3.DebugKillWithAttack(attack);
        Status = ok ? $"{attack} で倒しました" : "倒せませんでした(ボスが見つからない)";
    }
}
#endif
