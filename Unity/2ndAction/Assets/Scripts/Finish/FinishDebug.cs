#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

// Enemy FINISH System の確認用(開発版だけ): ランの中でプレイヤーの前に敵を出して、指定の FINISH で倒す。
// DEBUG パネルの「FINISH TEST…」と自動テスト(-qaFinish)が使う。倒し方は通常の撃破と同じ処理(死亡確定/報酬/マルチの通知)。
public static class FinishDebug
{
    public static string EnemyId = "goblin";

    public static List<EnemyController> Spawn(string enemyId, int count, float ahead = 7f, float spacing = 1.6f, bool behind = false)
    {
        var list = new List<EnemyController>();
        var pc = PlayerController.Instance; var tm = TerrainManager.Instance;
        var def = EnemyDatabase.FindById(enemyId);
        if (pc == null || tm == null || def == null) return list;
        for (int i = 0; i < count; i++)
        {
            float x = pc.transform.position.x + (behind ? -1f : 1f) * (ahead + i * spacing);
            float gy = tm.GetHeightAt(x) ?? (pc.transform.position.y - pc.groundOffset);
            float y = gy + (def.movementType == EnemyMovementType.Flying ? 2.6f : 0f);
            var go = tm.SpawnEncounterEnemy(def, new Vector2(x, y), EnemyAiTier.T0, EnemyBehaviorKind.None);
            var en = go != null ? go.GetComponent<EnemyController>() : null;
            if (en != null) list.Add(en);
        }
        return list;
    }

    public static int Kill(List<EnemyController> list, PlayerAttackKind kind, bool heavy, bool overkill, bool aerial, float dirX = 0f)
    {
        int n = 0;
        foreach (var e in list) if (e != null && e.DebugKillWithFinish(kind, heavy, overkill, aerial, dirX)) n++;
        return n;
    }

    // プリセット(DEBUG パネルのボタン)
    public static void Preset(string what)
    {
        switch (what)
        {
            case "normal": Kill(Spawn(EnemyId, 1), PlayerAttackKind.Normal, false, false, false); break;
            case "heavy": Kill(Spawn(EnemyId, 1), PlayerAttackKind.Normal, true, false, false); break;
            case "overkill": Kill(Spawn(EnemyId, 1), PlayerAttackKind.Normal, false, true, false); break;
            case "up": Kill(Spawn(EnemyId, 1, 4f), PlayerAttackKind.Up, false, false, false); break;
            case "slam": Kill(Spawn(EnemyId, 4, 4f, 1.1f), PlayerAttackKind.Down, true, false, true); break;
            case "aerial": Kill(Spawn(EnemyId, 1, 4f), PlayerAttackKind.Normal, false, false, true); break;
            case "back": Kill(Spawn(EnemyId, 1, 3f, 1.6f, behind: true), PlayerAttackKind.Normal, false, false, false); break;
            case "five": Kill(Spawn(EnemyId, 5, 5f, 1.3f), PlayerAttackKind.Normal, true, false, false); break;
            case "ten": Kill(Spawn(EnemyId, 10, 4f, 1.0f), PlayerAttackKind.Down, true, true, true); break;
            case "flying": Kill(Spawn("harpy", 2, 5f, 2f), PlayerAttackKind.Normal, false, false, true); break;
            case "large": Kill(Spawn("heavy_ogre", 1, 6f), PlayerAttackKind.Normal, true, false, false); break;
        }
    }
}
#endif
