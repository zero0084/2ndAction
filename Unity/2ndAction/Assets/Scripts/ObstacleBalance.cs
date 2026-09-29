using System;
using System.Collections.Generic;
using UnityEngine;

// 障害物の耐久力と壊れ方(2026-09-29)。Resources/Obstacles/ObstacleBalance.asset(無ければコードの既定値)。
// 独立した障害物(ObstacleSpawnerが置く石/木/壁/大岩)だけが対象。足場・坂・天井・穴の壁・針は対象外。
// 攻撃力の目安: キャラの基礎攻撃力1〜4(多くは2)+ ATTACK UPカード1枚につき+1。
//   木(低耐久)  : 小木は1発(攻撃力1のキャラ/威力の低い技でも壊せる)、壊せる木(大きい木)は2
//   岩(中耐久)  : 未強化(2)では2発、強化(4)で1発
//   大岩/壁(高耐久): 複数回の攻撃や高い攻撃力で突破
public enum ObstacleMaterial { Wood, Rock, Heavy }

[CreateAssetMenu(menuName = "OneMoreMile/Obstacle Balance", fileName = "ObstacleBalance")]
public class ObstacleBalance : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        [Tooltip("ObstacleSpawnerのspecの名前(Rock/SmallTree/BreakableTree/Wall/GiantRock)")]
        public string kind = "";
        public ObstacleMaterial material = ObstacleMaterial.Rock;
        [Min(1)] public int durability = 3;
        [Tooltip("耐久力が残ったまま体当たりした時、その場から消える(従来の石/壁/大岩の挙動)")]
        public bool vanishOnContact = true;
    }

    public List<Entry> entries = new List<Entry>();

    [Header("演出")]
    [Tooltip("同時に出ている破片の上限(超えたら古い物から再利用)")] public int maxDebris = 72;
    [Tooltip("破片の表示時間(秒)")] public float debrisLife = 0.75f;
    [Tooltip("被弾時の揺れ(秒/幅)")] public float hitShakeTime = 0.14f;
    public float hitShakeAmount = 0.07f;
    [Tooltip("大型が崩れる時の画面の揺れ")] public float heavyBreakCameraShake = 0.12f;

    public Entry Find(string kind)
    {
        foreach (var e in entries) if (e != null && e.kind == kind) return e;
        return null;
    }

    static ObstacleBalance cached;
    public static ObstacleBalance Get()
    {
        if (cached != null) return cached;
        cached = Resources.Load<ObstacleBalance>("Obstacles/ObstacleBalance");
        if (cached == null) cached = CreateDefault();
        return cached;
    }

    public static ObstacleBalance CreateDefault()
    {
        var b = CreateInstance<ObstacleBalance>();
        b.entries.Add(new Entry { kind = "SmallTree", material = ObstacleMaterial.Wood, durability = 1, vanishOnContact = true });
        b.entries.Add(new Entry { kind = "BreakableTree", material = ObstacleMaterial.Wood, durability = 2, vanishOnContact = false });
        b.entries.Add(new Entry { kind = "Rock", material = ObstacleMaterial.Rock, durability = 4, vanishOnContact = true });
        b.entries.Add(new Entry { kind = "Wall", material = ObstacleMaterial.Heavy, durability = 7, vanishOnContact = true });
        b.entries.Add(new Entry { kind = "GiantRock", material = ObstacleMaterial.Heavy, durability = 10, vanishOnContact = true });
        return b;
    }
}
