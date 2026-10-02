using System.Collections.Generic;
using UnityEngine;

// 高速時の攻撃のすり抜け対策(2026-09-29)。
// 物理(50Hz、離散判定)だけだと、高速走行中の近接判定や速い弾が1フレームの間に障害物/雑魚を
// 通り過ぎて当たらないことがある。攻撃判定が「前のフレームから有効なまま」動いた時だけ、
// 前の位置→今の位置の間を判定そのままの形(大きさ・角度は変えない=範囲を広げない)で細かく刻んで確かめ、
// 通り過ぎた相手へ当てる。
//  ・対象: タグPlayerAttack+PlayerAttackInfoを持つ判定(剣/技/槍/弾/爆発/範囲)。相手は雑魚(EnemyController)と障害物のみ
//    (ボスは大きく、すり抜けが起きないので従来どおり物理だけ)。
//  ・同じ振り(PlayerAttackInfo.SwingId)では相手ごとに1回だけ(物理の接触と二重にならない)。
//  ・判定が有効になった瞬間/出し直した瞬間は掃引しない(前の位置から「振った」ことにはしない)。
//  ・弾は進行方向の手前の相手から順に当て、止まった所で終わる(壁の向こうへは当たらない)。
//  ・座標はFloatingOriginの論理座標で比べる(原点移動をまたいでも誤って長い掃引をしない)。ワープ/極端な移動は掃引しない。
public class PlayerAttackSweeper : MonoBehaviour
{
    public static PlayerAttackSweeper Instance { get; private set; }
    public static int SweepsRun, SweptTargets;
    // 1フレームの移動がこれ以下なら物理に任せる(低速では従来と全く同じ)
    public static float minSweepDistance = 0.2f;
    // これ以上はワープ/再配置とみなして掃引しない
    public static float maxSweepDistance = 15f;
    // 前後比較の自動テスト専用(改修前=掃引なし)。通常は常にtrue。
    public static bool Enabled = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[PlayerAttackSweeper]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<PlayerAttackSweeper>();
    }

    void OnEnable() { FloatingOrigin.Warped += OnWarp; }
    void OnDisable() { FloatingOrigin.Warped -= OnWarp; }
    void OnWarp(float d) { foreach (var a in PlayerAttackInfo.Active) if (a != null) a.wasEnabled = false; }

    readonly Collider2D[] buf = new Collider2D[32];
    readonly List<(float t, Collider2D c)> found = new List<(float, Collider2D)>();
    readonly HashSet<Collider2D> seen = new HashSet<Collider2D>();
    readonly List<PlayerAttackInfo> snapshot = new List<PlayerAttackInfo>();

    void LateUpdate()
    {
        if (!Enabled) { foreach (var a in PlayerAttackInfo.Active) if (a != null) a.wasEnabled = false; return; }
        snapshot.Clear();
        snapshot.AddRange(PlayerAttackInfo.Active);
        float ox = (float)FloatingOrigin.Offset;
        foreach (var a in snapshot)
        {
            if (a == null) continue;
            if (a.col == null) a.col = a.GetComponent<Collider2D>();
            var col = a.col;
            bool on = col != null && col.enabled && col.gameObject.activeInHierarchy && col.CompareTag("PlayerAttack");
            if (!on) { a.wasEnabled = false; continue; }
            Vector2 cur = Center(col); cur.x += ox;
            if (a.wasEnabled)
            {
                Vector2 prev = a.prevBounds.center; // 論理座標で保存してある
                float dist = Vector2.Distance(prev, cur);
                if (dist > minSweepDistance && dist < maxSweepDistance && !(Time.timeScale <= 0f)) Sweep(a, col, prev - new Vector2(ox, 0f), cur - new Vector2(ox, 0f), dist);
            }
            if (a == null || col == null) continue; // 弾が消えた
            a.prevBounds = new Bounds(cur, Vector3.zero);
            a.wasEnabled = col.enabled;
        }
    }

    static Vector2 Center(Collider2D c)
    {
        if (c is BoxCollider2D b) return b.transform.TransformPoint(b.offset);
        if (c is CircleCollider2D ci) return ci.transform.TransformPoint(ci.offset);
        return c.bounds.center;
    }

    void Sweep(PlayerAttackInfo a, Collider2D col, Vector2 from, Vector2 to, float dist)
    {
        SweepsRun++;
        int mask = Physics2D.GetLayerCollisionMask(col.gameObject.layer);
        Vector2 size; float radius = 0f; float angle = col.transform.eulerAngles.z;
        if (col is BoxCollider2D b)
        {
            Vector3 ls = b.transform.lossyScale;
            size = new Vector2(Mathf.Abs(b.size.x * ls.x), Mathf.Abs(b.size.y * ls.y));
        }
        else if (col is CircleCollider2D c)
        {
            Vector3 ls = c.transform.lossyScale;
            radius = c.radius * Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y));
            size = Vector2.one * radius * 2f;
        }
        else { size = col.bounds.size; angle = 0f; }
        float step = Mathf.Max(0.05f, Mathf.Min(size.x, size.y) * 0.5f);
        int n = Mathf.Clamp(Mathf.CeilToInt(dist / step), 1, 64);

        found.Clear(); seen.Clear();
        // t=0(前のフレーム)は前回の掃引/物理で確かめ済み。t=(0,1)の途中だけを見る(t=1は今の位置=次の物理更新が担当するが、念のため含める)
        for (int i = 1; i <= n; i++)
        {
            float t = i / (float)n;
            Vector2 p = Vector2.Lerp(from, to, t);
            int count = radius > 0f ? Physics2D.OverlapCircleNonAlloc(p, radius, buf, mask) : Physics2D.OverlapBoxNonAlloc(p, size, angle, buf, mask);
            for (int k = 0; k < count; k++)
            {
                var hit = buf[k];
                if (hit == null || hit == col || !seen.Add(hit)) continue;
                if (hit.GetComponent<ObstacleController>() == null && hit.GetComponent<EnemyController>() == null) continue; // 物理の接触と同じく判定の付いた本体だけ
                found.Add((t, hit));
            }
        }
        if (found.Count == 0) return;
        found.Sort((x, y) => x.t.CompareTo(y.t));

        var kit = a.GetComponent<KitProjectile>();
        var bullet = kit == null ? a.GetComponent<PlayerBullet>() : null;
        foreach (var (t, hit) in found)
        {
            if (hit == null || !hit.enabled) continue;
            var obs = hit.GetComponent<ObstacleController>();
            var enemy = obs == null ? hit.GetComponent<EnemyController>() : null;
            SweptTargets++;
            if (kit != null)
            {
                // 弾: 弾側の処理(障害物で止まる/貫通回数)がダメージも含めて行う。雑魚は相手側が受ける。
                if (enemy != null) enemy.ReceiveSweptAttack(col);
                if (kit.SweptHit(hit)) return;
                continue;
            }
            if (bullet != null)
            {
                if (enemy != null) enemy.ReceiveSweptAttack(col);
                if (bullet.SweptHit(hit)) return;
                continue;
            }
            // 近接/爆発/範囲: 通り過ぎた相手すべて(物理で当たるはずだった範囲と同じ)
            if (obs != null) obs.ReceiveAttack(col);
            else if (enemy != null) enemy.ReceiveSweptAttack(col);
        }
    }
}
