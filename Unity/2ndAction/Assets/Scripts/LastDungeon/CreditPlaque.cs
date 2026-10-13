using UnityEngine;
using UnityEngine.UI;

// エンドロールの日本語の名前(2026-10-13 第3弾)。足場の巨大文字(GlyphFont)は英数字だけなので、
// 日本語の名前は「石の銘板(乗れる足場)」+「銘板に刻んだ名前(ゲーム世界の中の文字)」に分ける。表記は指定のまま(ローマ字にしない)。
// 文字は OS/端末のフォント(他の画面の日本語と同じ)。縦画面の「斜め上から」でもカメラの方を向く(FaceObliqueCamera)。
public class CreditPlaque : MonoBehaviour, IWorldSolid
{
    public float Width { get; private set; }
    public float Height { get; private set; }
    public string Text { get; private set; }
    public CreditText Label { get; private set; }
    float x0, y0;

    // 左端 x・地面 y・文字の高さ capH。戻り値は銘板(右端は Width から)
    public static CreditPlaque Create(Transform parent, string text, float leftX, float groundY, float capH, int order)
    {
        var go = new GameObject("CreditPlaque_" + text);
        go.transform.SetParent(parent, false);
        var p = go.AddComponent<CreditPlaque>();
        p.Text = text;
        p.Build(leftX, groundY, capH, order);
        return p;
    }

    void Build(float leftX, float groundY, float capH, int order)
    {
        // 先に文字を作って幅を測る
        Label = CreditText.Create(transform, Text, Vector3.zero, capH, new Color(1f, 0.93f, 0.74f), order + 1, true);
        float textW = Label.Width;
        Width = Mathf.Max(capH * 2.2f, textW + capH * 1.3f);
        Height = capH * 1.75f;
        x0 = leftX; y0 = groundY;
        transform.position = new Vector3(leftX + Width * 0.5f, groundY, 0f);
        var st = new GameObject("Slab").transform;
        st.SetParent(transform, false);
        var slab = st.gameObject.AddComponent<SpriteRenderer>();
        slab.sprite = Resources.Load<Sprite>("LastDungeon/Fx/slab");
        if (slab.sprite == null) slab.sprite = BossFx.Block();
        slab.sortingOrder = order;
        Vector2 ss = slab.sprite.bounds.size;
        st.localScale = new Vector3(Width / Mathf.Max(0.01f, ss.x), Height / Mathf.Max(0.01f, ss.y), 1f);
        st.localPosition = new Vector3(0f, Height * 0.5f - slab.sprite.bounds.center.y * st.localScale.y, 0f);
        // 影
        var sh = new GameObject("Shadow").transform;
        sh.SetParent(transform, false);
        var shadow = sh.gameObject.AddComponent<SpriteRenderer>();
        shadow.sprite = OneShotSpriteEffect.SoftDotSprite();
        shadow.sortingOrder = RenderOrder.EnvironmentFx;
        sh.localScale = new Vector3(Width * 1.15f, 0.4f, 1f);
        sh.localPosition = new Vector3(0f, 0.04f, 0f);
        shadow.color = new Color(0f, 0f, 0f, 0.35f);
        // 文字は銘板の正面(中央)
        Label.transform.position = new Vector3(transform.position.x, groundY + Height * 0.5f, 0f);
        WorldPlatforms.Register(this);
    }

    void OnDestroy() { WorldPlatforms.Unregister(this); }

    // ---- IWorldSolid: 上に乗れる(横からは止めない。立っている名前の文字と同じ) ----
    public bool SolidActive => isActiveAndEnabled;
    public bool IsPlatform => true;
    public bool IsWall => false;
    public Rect SolidRect => Rect.MinMaxRect(transform.position.x - Width * 0.5f, transform.position.y, transform.position.x + Width * 0.5f, transform.position.y + Height);
}

// ゲーム世界の中に置く文字(日本語の名前/役職の説明)。uGUI の世界空間の Canvas(他の画面と同じフォントの仕組み)
public class CreditText : MonoBehaviour
{
    public float Width { get; private set; }
    public float CapHeight { get; private set; }
    Text text;

    public static Font DefaultFont()
    {
        try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        try { return Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
        return null;
    }

    // center = 文字列の中心。capH = 文字の高さ(m)
    public static CreditText Create(Transform parent, string s, Vector3 center, float capH, Color color, int order, bool outline)
    {
        const int FontPx = 64;
        var go = new GameObject("CreditText_" + s);
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        var ct = go.AddComponent<CreditText>();
        ct.CapHeight = capH;
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = order;
        float scale = capH / (FontPx * 0.78f); // 漢字/かなの見た目の高さ ≒ capH(フォントの大きさの約8割)
        var crt = go.GetComponent<RectTransform>();
        crt.sizeDelta = new Vector2(4000f, FontPx * 2f);
        go.transform.localScale = new Vector3(scale, scale, 1f);
        var tgo = new GameObject("Text");
        tgo.transform.SetParent(go.transform, false);
        var t = tgo.AddComponent<Text>();
        t.font = DefaultFont();
        t.fontSize = FontPx;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.color = color;
        t.text = s;
        t.raycastTarget = false;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(4000f, FontPx * 2f);
        if (outline)
        {
            var o = tgo.AddComponent<Outline>();
            o.effectColor = new Color(0.12f, 0.08f, 0.04f, 0.9f);
            o.effectDistance = new Vector2(2.5f, -2.5f);
        }
        ct.text = t;
        ct.Width = t.preferredWidth * scale;
        go.AddComponent<FaceObliqueCamera>();
        return ct;
    }
}

// 縦画面の「斜め上から見る」(PortraitCameraRig)の間だけ、カメラの方を向く(横から見る時は向きを変えない)
public class FaceObliqueCamera : MonoBehaviour
{
    void LateUpdate()
    {
        var rig = PortraitCameraRig.Instance;
        if (rig != null && rig.IsActive && rig.cam != null) transform.rotation = rig.cam.transform.rotation;
        else if (transform.rotation != Quaternion.identity) transform.rotation = Quaternion.identity;
    }
}
