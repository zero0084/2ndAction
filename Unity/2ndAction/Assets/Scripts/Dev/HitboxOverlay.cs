#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

// 攻撃判定の可視化(2026-10-03、開発版のみ)。色分けして、回転した判定もそのままの形で描く。
//   赤   = プレイヤーの攻撃判定(近接)        橙 = プレイヤーの飛び道具/爆発/ゾーン
//   緑   = プレイヤーの被弾判定(体。敵の攻撃/接触はこの箱で受ける)
//   青   = 地形の基準(足元の点。プレイヤーの移動は物理のColliderではなく、足元の点と地面の線で決まる)
//   黄   = 敵の体(接触ダメージ/被弾)            紫 = 敵/ボスの攻撃判定
//   水色 = ボスの被弾範囲
// 切り替え: DEBUGパネル「判定表示」 / CARD BALANCE TEST「ビルド」タブ。保存しない(起動ごとにOFF)。
public class HitboxOverlay : MonoBehaviour
{
    public static bool Enabled;
    static HitboxOverlay instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("[HitboxOverlay]");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<HitboxOverlay>();
    }

    static readonly Color Red = new Color(1f, 0.15f, 0.15f, 1f), Orange = new Color(1f, 0.6f, 0.1f, 1f), Green = new Color(0.2f, 1f, 0.3f, 1f),
        Blue = new Color(0.3f, 0.6f, 1f, 1f), Yellow = new Color(1f, 0.95f, 0.2f, 1f), Magenta = new Color(1f, 0.3f, 1f, 1f), Cyan = new Color(0.3f, 1f, 1f, 1f);

    Camera cam;
    static Texture2D px;

    void OnGUI()
    {
        if (!Enabled || Event.current.type != EventType.Repaint) return;
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        if (px == null) { px = new Texture2D(1, 1); px.SetPixel(0, 0, Color.white); px.Apply(); }
        GUI.depth = -1000;

        var pc = PlayerController.Instance;
        if (pc != null)
        {
            var body = pc.GetComponent<Collider2D>();
            if (body != null) Draw(body, Green, 2f);
            Vector3 feet = pc.transform.position - new Vector3(0f, pc.groundOffset, 0f);
            Cross(feet, Blue);
        }
        foreach (var info in PlayerAttackInfo.Active)
        {
            if (info == null || !info.isActiveAndEnabled) continue;
            var c = info.col != null ? info.col : info.GetComponent<Collider2D>();
            if (c == null || !c.enabled) continue;
            bool proj = info.GetComponent<KitProjectile>() != null || info.GetComponent<PlayerBullet>() != null || info.transform.parent == null;
            Draw(c, proj ? Orange : Red, 3f);
        }
        foreach (var en in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
        {
            if (!en.isActiveAndEnabled) continue;
            foreach (var c in en.GetComponents<Collider2D>()) if (c.enabled) Draw(c, Yellow, 2f);
        }
        foreach (var hb in FindObjectsByType<EnemyMeleeHitbox>(FindObjectsSortMode.None))
        {
            var c = hb.GetComponent<Collider2D>(); if (c != null && c.enabled) Draw(c, Magenta, 2f);
        }
        foreach (var hb in FindObjectsByType<BossHitbox>(FindObjectsSortMode.None))
        {
            var c = hb.GetComponent<Collider2D>(); if (c != null && c.enabled) Draw(c, Magenta, 2f);
        }
        foreach (var h in FindObjectsByType<BossHurtbox>(FindObjectsSortMode.None))
        {
            var c = h.GetComponent<Collider2D>(); if (c != null && c.enabled) Draw(c, Cyan, 2f);
        }
        GUI.color = Color.white;
    }

    readonly Vector3[] corners = new Vector3[4];
    void Draw(Collider2D c, Color color, float w)
    {
        if (c is BoxCollider2D b)
        {
            Vector2 o = b.offset, h = b.size * 0.5f;
            corners[0] = b.transform.TransformPoint(new Vector3(o.x - h.x, o.y - h.y, 0f));
            corners[1] = b.transform.TransformPoint(new Vector3(o.x + h.x, o.y - h.y, 0f));
            corners[2] = b.transform.TransformPoint(new Vector3(o.x + h.x, o.y + h.y, 0f));
            corners[3] = b.transform.TransformPoint(new Vector3(o.x - h.x, o.y + h.y, 0f));
        }
        else
        {
            var bb = c.bounds;
            corners[0] = new Vector3(bb.min.x, bb.min.y); corners[1] = new Vector3(bb.max.x, bb.min.y);
            corners[2] = new Vector3(bb.max.x, bb.max.y); corners[3] = new Vector3(bb.min.x, bb.max.y);
        }
        for (int i = 0; i < 4; i++) Line(corners[i], corners[(i + 1) % 4], color, w);
    }

    void Cross(Vector3 p, Color color)
    {
        Line(p + new Vector3(-0.25f, 0f), p + new Vector3(0.25f, 0f), color, 2f);
        Line(p + new Vector3(0f, -0.25f), p + new Vector3(0f, 0.25f), color, 2f);
    }

    void Line(Vector3 a, Vector3 b, Color color, float w)
    {
        Vector3 sa = cam.WorldToScreenPoint(a), sb = cam.WorldToScreenPoint(b);
        if (sa.z < 0f || sb.z < 0f) return;
        Vector2 pa = new Vector2(sa.x, Screen.height - sa.y), pb = new Vector2(sb.x, Screen.height - sb.y);
        Vector2 d = pb - pa;
        float len = d.magnitude;
        if (len < 0.5f) return;
        float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        Matrix4x4 keep = GUI.matrix;
        GUIUtility.RotateAroundPivot(ang, pa);
        GUI.color = color;
        GUI.DrawTexture(new Rect(pa.x, pa.y - w * 0.5f, len, w), px);
        GUI.matrix = keep;
    }
}
#endif
