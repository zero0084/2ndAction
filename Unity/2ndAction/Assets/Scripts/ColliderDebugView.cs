using UnityEngine;

// Draws an outline matching this object's BoxCollider2D bounds, visible only
// while GameManager.DebugMode is on - lets hit/hurt boxes be checked on an
// actual device without needing the Editor's Gizmos.
[RequireComponent(typeof(BoxCollider2D))]
public class ColliderDebugView : MonoBehaviour
{
    public Color color = Color.red;
    public float lineWidth = 0.03f;

    BoxCollider2D col;
    LineRenderer lr;

    void Awake()
    {
        col = GetComponent<BoxCollider2D>();

        lr = gameObject.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.positionCount = 5;
        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = color;
        lr.endColor = color;
        lr.sortingOrder = 50;
        lr.enabled = false;

        UpdateShape();
    }

    void UpdateShape()
    {
        Vector2 c = col.offset;
        Vector2 h = col.size * 0.5f;
        lr.SetPosition(0, new Vector3(c.x - h.x, c.y - h.y, 0f));
        lr.SetPosition(1, new Vector3(c.x + h.x, c.y - h.y, 0f));
        lr.SetPosition(2, new Vector3(c.x + h.x, c.y + h.y, 0f));
        lr.SetPosition(3, new Vector3(c.x - h.x, c.y + h.y, 0f));
        lr.SetPosition(4, new Vector3(c.x - h.x, c.y - h.y, 0f));
    }

    void Update()
    {
        bool on = GameManager.Instance != null && GameManager.Instance.DebugMode && col.enabled;
        if (lr.enabled != on) lr.enabled = on;
    }
}
