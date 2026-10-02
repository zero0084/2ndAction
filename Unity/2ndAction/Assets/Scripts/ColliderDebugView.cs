using UnityEngine;

// Draws an outline matching this object's Collider2D bounds, visible only
// while GameManager.DebugMode is on - lets hit/hurt boxes be checked on an
// actual device without needing the Editor's Gizmos.
// 当たり判定基礎品質修整(2026-09-14) - 障害物のColliderをBoxCollider2D
// からPolygonCollider2D(スプライトの実シルエットに沿った形状)へ切り替えた
// 際、このデバッグ表示もBoxCollider2D専用のままだと[RequireComponent]が
// 無関係なBoxCollider2Dを勝手に追加してしまい、実機デバッグ表示が実際の
// 当たり判定と食い違う(まさにマスターが指摘している「見た目と当たり判定
// のズレ」をデバッグ表示自身が起こす)ため、Collider2Dベースの汎用実装に
// 変更 - BoxCollider2D/PolygonCollider2Dのどちらでも実際の形状をそのまま
// 描画する。
[RequireComponent(typeof(Collider2D))]
public class ColliderDebugView : MonoBehaviour
{
    public Color color = Color.red;
    public float lineWidth = 0.03f;

    Collider2D col;
    LineRenderer lr;
    Vector2 drawnSize, drawnOffset;

    void Awake()
    {
        col = GetComponent<Collider2D>();

        lr = gameObject.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = color;
        lr.endColor = color;
        lr.sortingOrder = 50;
        lr.loop = true;
        lr.enabled = false;

        UpdateShape();
    }

    void UpdateShape()
    {
        if (col is BoxCollider2D box)
        {
            Vector2 c = box.offset;
            Vector2 h = box.size * 0.5f;
            drawnSize = box.size; drawnOffset = box.offset;
            lr.loop = true;
            lr.positionCount = 4;
            lr.SetPosition(0, new Vector3(c.x - h.x, c.y - h.y, 0f));
            lr.SetPosition(1, new Vector3(c.x + h.x, c.y - h.y, 0f));
            lr.SetPosition(2, new Vector3(c.x + h.x, c.y + h.y, 0f));
            lr.SetPosition(3, new Vector3(c.x - h.x, c.y + h.y, 0f));
        }
        else if (col is PolygonCollider2D poly && poly.pathCount > 0)
        {
            // 複数pathがある形状は稀(このプロジェクトの障害物アートは単一
            // 輪郭)だが、念のため一番頂点数の多いpath(=主要な輪郭)を描画。
            int bestPath = 0;
            for (int i = 1; i < poly.pathCount; i++)
            {
                if (poly.GetPath(i).Length > poly.GetPath(bestPath).Length) bestPath = i;
            }
            Vector2[] points = poly.GetPath(bestPath);
            lr.loop = true;
            lr.positionCount = points.Length;
            for (int i = 0; i < points.Length; i++)
            {
                lr.SetPosition(i, new Vector3(points[i].x, points[i].y, 0f));
            }
        }
        else
        {
            // フォールバック(未知のCollider2D型) - バウンディングボックスを
            // ローカル座標で描画。
            Bounds b = col.bounds;
            Vector2 c = transform.InverseTransformPoint(b.center);
            Vector2 h = b.extents;
            lr.loop = true;
            lr.positionCount = 4;
            lr.SetPosition(0, new Vector3(c.x - h.x, c.y - h.y, 0f));
            lr.SetPosition(1, new Vector3(c.x + h.x, c.y - h.y, 0f));
            lr.SetPosition(2, new Vector3(c.x + h.x, c.y + h.y, 0f));
            lr.SetPosition(3, new Vector3(c.x - h.x, c.y + h.y, 0f));
        }
    }

    void Update()
    {
        bool on = GameManager.Instance != null && GameManager.Instance.DebugMode && col.enabled;
        if (lr.enabled != on) lr.enabled = on;
        // 2026-09-30: 判定の形を実行中に変える部品(MeleeReach)があるので、表示中は変わった時に描き直す。
        if (on && col is BoxCollider2D b && (b.size != drawnSize || b.offset != drawnOffset)) UpdateShape();
    }
}
