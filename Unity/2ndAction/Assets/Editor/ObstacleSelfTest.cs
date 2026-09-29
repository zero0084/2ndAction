using System.Reflection;
using UnityEditor;
using UnityEngine;

// Stage01 荒野街道 最小実装(2026-09-13) - ObstacleController(石/小木/壁/
// 壊せる木/巨大石の共通ランタイム)の核心ロジックをEdit-mode単発実行で
// 直接検証する。OnTriggerEnter2D/LateUpdateを実際の物理コールバックではなく
// reflection経由で直接呼び出す。
// 2026-09-29 障害物の耐久力: すべての障害物が壊せる前提へ更新。
//  ・種類ごとの耐久力(ObstacleBalanceの既定値)、同じ振り(SwingId)では1回だけ減る、
//  ・0になった瞬間に当たり判定が無効、同じフレームに壊れたら体当たりの被弾処理をしない、
//  ・耐久力が残る間の体当たりは従来どおり(石/壁/大岩は消える、壊せる木は残る)、
//  ・Setupを通らない(breakable=false)物は攻撃に反応しない(互換)。
public static class ObstacleSelfTest
{
    [MenuItem("Tools/2ndAction/Self-Test: Obstacle")]
    public static void Run()
    {
        var bal = ObstacleBalance.Get();
        GameObject attackerGO = CreateTaggedCollider("PlayerAttack");
        var info = attackerGO.AddComponent<PlayerAttackInfo>();
        info.NewSwing();
        Collider2D atk = attackerGO.GetComponent<Collider2D>();

        // 1) breakable=false(Setupを通らない古い作り方) - 攻撃に反応しない、体当たりで退場
        GameObject solidGO = CreateObstacleGO(breakable: false, hp: 1);
        ObstacleController solid = solidGO.GetComponent<ObstacleController>();
        InvokeTrigger(solid, atk);
        bool ignoredAttackWhenNotBreakable = solidGO.activeSelf && !solid.Broken;
        GameObject playerGO = CreateTaggedCollider("Player");
        InvokeTrigger(solid, playerGO.GetComponent<Collider2D>());
        InvokeLate(solid);
        bool solidDeactivatedOnPlayerContact = !solidGO.activeSelf;

        // 2) 岩(Setup "Rock"): 同じ振りでは1回だけ減る/新しい振りでまた減る
        GameObject rockGO = CreateObstacleGO(true, 1);
        var rock = rockGO.GetComponent<ObstacleController>();
        rock.Setup("Rock");
        int rockMax = rock.Hp;
        InvokeTrigger(rock, atk);
        InvokeTrigger(rock, atk); // 同じ振り(判定の重複)
        bool sameSwingOnce = rock.Hp == rockMax - 1;
        info.NewSwing();
        InvokeTrigger(rock, atk);
        bool newSwingHits = rock.Hp == rockMax - 2;
        // 残りを削り切る → 0の瞬間に当たり判定が無効/Broken
        int guard = 0;
        while (!rock.Broken && guard++ < 20) { info.NewSwing(); InvokeTrigger(rock, atk); }
        bool brokeAtZero = rock.Broken && rock.Hp == 0;
        bool collidersOff = true;
        foreach (var c in rockGO.GetComponentsInChildren<Collider2D>(true)) if (c.enabled) collidersOff = false;
        // 壊れた後の攻撃/体当たりは何も起こさない
        info.NewSwing();
        InvokeTrigger(rock, atk);
        InvokeTrigger(rock, playerGO.GetComponent<Collider2D>());
        bool noHitAfterBreak = rock.Hp == 0;

        // 3) 同じフレームに「体当たり」と「攻撃で破壊」が重なった: 被弾処理(消滅)をしない
        GameObject woodGO = CreateObstacleGO(true, 1);
        var wood = woodGO.GetComponent<ObstacleController>();
        wood.Setup("BreakableTree");
        InvokeTrigger(wood, playerGO.GetComponent<Collider2D>()); // 接触(LateUpdateまで保留)
        guard = 0;
        while (!wood.Broken && guard++ < 10) { info.NewSwing(); InvokeTrigger(wood, atk); }
        bool pendingCleared = !(bool)GetField(wood, "pendingContact");

        // 4) 耐久力が残る間の体当たり: 壊せる木は残る/石は消える
        GameObject treeGO = CreateObstacleGO(true, 1);
        var tree = treeGO.GetComponent<ObstacleController>();
        tree.Setup("BreakableTree");
        InvokeTrigger(tree, playerGO.GetComponent<Collider2D>());
        InvokeLate(tree);
        bool treeStays = treeGO.activeSelf && !tree.Broken;
        GameObject rock2GO = CreateObstacleGO(true, 1);
        var rock2 = rock2GO.GetComponent<ObstacleController>();
        rock2.Setup("Rock");
        InvokeTrigger(rock2, playerGO.GetComponent<Collider2D>());
        InvokeLate(rock2);
        bool rockVanishes = !rock2GO.activeSelf;

        // 5) 種類ごとの耐久力の段階(攻撃力2=未強化の目安、4=強化の目安)
        int H(string k) { var e = bal.Find(k); return e != null ? e.durability : -1; }
        int Hits(string k, int power) => Mathf.CeilToInt(H(k) / (float)power);
        bool tiers = Hits("SmallTree", 2) <= 1 && Hits("BreakableTree", 2) <= 1
            && Hits("Rock", 2) >= 2 && Hits("Rock", 4) == 1
            && Hits("Wall", 2) >= 3 && Hits("GiantRock", 2) >= 4 && H("GiantRock") > H("Wall") && H("Wall") > H("Rock") && H("Rock") > H("SmallTree");

        bool pass = ignoredAttackWhenNotBreakable && solidDeactivatedOnPlayerContact && sameSwingOnce && newSwingHits
            && brokeAtZero && collidersOff && noHitAfterBreak && pendingCleared && treeStays && rockVanishes && tiers;

        Debug.Log($"[ObstacleSelfTest] {(pass ? "PASS" : "FAIL")} - ignoredAttackWhenNotBreakable={ignoredAttackWhenNotBreakable} " +
                  $"solidDeactivatedOnPlayerContact={solidDeactivatedOnPlayerContact} sameSwingOnce={sameSwingOnce} newSwingHits={newSwingHits} " +
                  $"brokeAtZero={brokeAtZero} collidersOff={collidersOff} noHitAfterBreak={noHitAfterBreak} pendingCleared={pendingCleared} " +
                  $"treeStays={treeStays} rockVanishes={rockVanishes} tiers={tiers} " +
                  $"(Wood={H("SmallTree")}/{H("BreakableTree")} Rock={H("Rock")} Wall={H("Wall")} GiantRock={H("GiantRock")})");
        if (!pass) Debug.LogError("[ObstacleSelfTest] FAIL - see individual flags above for which check failed.");

        foreach (var go in new[] { solidGO, rockGO, woodGO, treeGO, rock2GO, attackerGO, playerGO }) if (go != null) Object.DestroyImmediate(go);
        var fx = GameObject.Find("[ObstacleFx]");
        if (fx != null) Object.DestroyImmediate(fx);
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
        method.Invoke(target, new object[] { other });
    }

    static void InvokeLate(ObstacleController target)
    {
        MethodInfo method = typeof(ObstacleController).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(target, null);
    }

    static object GetField(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        return field != null ? field.GetValue(target) : null;
    }
}
