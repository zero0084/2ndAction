using System.Collections.Generic;
using UnityEngine;

// ボス戦の構図(2026-10-31、UI 全体の改善)で見る「交戦中のボス」の範囲。CameraFollow が毎フレーム読む。
//  ・BossBattle.Living(荒野/洞窟/天空/ラスダンの全種類が登録される)のうち、倒れておらず、プレイヤーから一定の距離にいるもの
//  ・範囲は被弾の当たり(打ち上げ中は体と一緒に上がる)。竜/魔人は体の当たり
//  ・カメラの見た目だけに使う(判定の画面幅 GameView は変えない)
public static class BossCamFraming
{
    public const float EngageRange = 30f; // プレイヤーからこの横の距離までのボスを画面に収める
    static readonly Dictionary<MonoBehaviour, Collider2D> cols = new Dictionary<MonoBehaviour, Collider2D>();

    // 構図を続ける距離(跳び退く/駆け抜けるボスで寄り/戻りを繰り返さない。画面に収める対象は EngageRange まで)
    public const float HoldRange = 60f;
    public static int NearCount { get; private set; }

    public static int Collect(float playerX, List<Bounds> into)
    {
        into.Clear();
        NearCount = 0;
        BossBattle.Living.RemoveWhere(b => b == null);
        foreach (var b in BossBattle.Living)
        {
            if (!b.isActiveAndEnabled) continue;
            Bounds bb;
            if (b is WildBossBase w) { if (w.IsDead) continue; bb = w.AssistBounds; }
            else if (b is DragonController d) { if (d.IsDead) continue; bb = BodyBounds(b); }
            else if (b is MajinController m) { if (m.IsDead) continue; bb = BodyBounds(b); }
            else continue;
            if (bb.size.sqrMagnitude <= 0f) continue;
            if (Mathf.Abs(bb.center.x - playerX) <= HoldRange) NearCount++;
            if (Mathf.Abs(bb.center.x - playerX) > EngageRange) continue;
            into.Add(bb);
        }
        return into.Count;
    }

    static Bounds BodyBounds(MonoBehaviour b)
    {
        if (cols.Count > 32) cols.Clear(); // 倒したボスの分を溜めない
        if (!cols.TryGetValue(b, out var c) || c == null)
        {
            c = null; float best = 0f;
            foreach (var x in b.GetComponentsInChildren<Collider2D>())
            {
                if (x == null || x.isTrigger && x.bounds.size.x > 30f) continue;
                float a = x.bounds.size.x * x.bounds.size.y;
                if (a > best) { best = a; c = x; }
            }
            cols[b] = c;
        }
        return c != null ? c.bounds : new Bounds(b.transform.position + Vector3.up * 1.5f, new Vector3(3f, 3f, 1f));
    }
}
