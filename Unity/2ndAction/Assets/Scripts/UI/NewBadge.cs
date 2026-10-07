using UnityEngine;
using UnityEngine.UI;

// 「NEW」の小さな札(2026-10-08)。解放したばかりのマップ/キャラの枠に付けて、ゆっくり明滅させる(uGUI)。
// IMGUI のホームの入口には GameManager が DrawNewDot で光る点を描く。
public class NewBadge : MonoBehaviour
{
    Graphic bg, label;
    float phase;

    public static NewBadge Ensure(RectTransform parent, Vector2 anchor, Vector2 offset, float scale = 1f)
    {
        if (parent == null) return null;
        var t = parent.Find("NewBadge");
        if (t != null) return t.GetComponent<NewBadge>();
        var go = new GameObject("NewBadge", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(96f, 40f) * scale;
        rt.anchoredPosition = offset;
        var img = go.AddComponent<Image>();
        img.color = new Color(1f, 0.78f, 0.18f, 0.95f);
        img.raycastTarget = false;
        var tgo = new GameObject("Label", typeof(RectTransform));
        var trt = tgo.GetComponent<RectTransform>();
        trt.SetParent(rt, false);
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        var txt = tgo.AddComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.text = "NEW";
        txt.fontSize = Mathf.RoundToInt(26 * scale);
        txt.fontStyle = FontStyle.Bold;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = new Color(0.18f, 0.08f, 0.02f);
        txt.raycastTarget = false;
        var b = go.AddComponent<NewBadge>();
        b.bg = img; b.label = txt;
        return b;
    }

    public static void Set(RectTransform parent, bool on, Vector2 anchor, Vector2 offset, float scale = 1f)
    {
        if (parent == null) return;
        if (on) { var b = Ensure(parent, anchor, offset, scale); if (b != null) { b.gameObject.SetActive(true); b.transform.SetAsLastSibling(); } }
        else { var t = parent.Find("NewBadge"); if (t != null) t.gameObject.SetActive(false); }
    }

    void Update()
    {
        phase += Time.unscaledDeltaTime * 3.2f;
        float k = 0.5f + 0.5f * Mathf.Sin(phase);
        if (bg != null) bg.color = new Color(1f, Mathf.Lerp(0.70f, 0.92f, k), Mathf.Lerp(0.12f, 0.35f, k), 0.95f);
        transform.localScale = Vector3.one * Mathf.Lerp(0.96f, 1.06f, k);
    }
}
