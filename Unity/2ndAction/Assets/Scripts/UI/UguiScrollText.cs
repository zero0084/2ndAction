using UnityEngine;
using UnityEngine.UI;

// uGUI の長い説明文を、元の枠の中で縦にスクロールして最後まで読めるようにする(2026-10-08、多言語の長文対策)。
//  ・Wrap(text) で、その Text を同じ場所の「窓」(RectMask2D)の中へ移す。Text は横幅で折り返し、縦は全文の高さになる(文字は縮めない)。
//    呼び出し側は今までどおり text.text を書き換えればよい(同じ Text のまま)。文が変わったら(言語/表示対象の切り替え)先頭へ戻る。
//  ・指のドラッグとタップを区別する: 押してから DragThreshold を越えて動いた時だけスクロールし、その指は PointerCaptured になる
//    (離した時にタップとして扱う画面は、PointerCaptured の間はタップにしない)。ホイール/ゲームパッドのスクロールにも対応。
//  ・続きがある時だけ右に細いバーを出す。
public class UguiScrollText : MonoBehaviour
{
    public const float DragThreshold = 14f;
    public static bool PointerCaptured { get; private set; }  // 今の指はどれかの説明文のスクロールに使われた(離すまで)

    RectTransform viewport, content;
    Text text;
    Image bar, track;
    string lastText; float lastWidth;
    float scrollY, contentH, viewH;
    bool pressed, dragging; Vector2 pressPos; float lastPointerY;
    Canvas canvas;

    public static UguiScrollText Wrap(Text t)
    {
        if (t == null) return null;
        var existing = t.GetComponentInParent<UguiScrollText>(true);
        if (existing != null) return existing;
        var trt = t.rectTransform;
        var parent = trt.parent;
        var vp = new GameObject(t.gameObject.name + "_Scroll", typeof(RectTransform));
        var vrt = vp.GetComponent<RectTransform>();
        vrt.SetParent(parent, false);
        vrt.SetSiblingIndex(trt.GetSiblingIndex());
        vrt.anchorMin = trt.anchorMin; vrt.anchorMax = trt.anchorMax; vrt.pivot = trt.pivot;
        vrt.anchoredPosition = trt.anchoredPosition; vrt.sizeDelta = trt.sizeDelta;
        vrt.localScale = trt.localScale; vrt.localRotation = trt.localRotation;
        vp.AddComponent<RectMask2D>();
        var cgo = new GameObject("Content", typeof(RectTransform));
        var crt = cgo.GetComponent<RectTransform>();
        crt.SetParent(vrt, false);
        crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f); crt.pivot = new Vector2(0.5f, 1f);
        crt.offsetMin = new Vector2(0f, 0f); crt.offsetMax = new Vector2(-12f, 0f); crt.anchoredPosition = Vector2.zero;
        trt.SetParent(crt, false);
        trt.anchorMin = new Vector2(0f, 0f); trt.anchorMax = new Vector2(1f, 1f); trt.pivot = new Vector2(0.5f, 1f); // 中身(Content)いっぱい
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
        t.resizeTextForBestFit = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        // バー
        var tgo = new GameObject("Track", typeof(RectTransform));
        var tr = tgo.GetComponent<RectTransform>(); tr.SetParent(vrt, false);
        tr.anchorMin = new Vector2(1f, 0f); tr.anchorMax = new Vector2(1f, 1f); tr.pivot = new Vector2(1f, 0.5f); tr.sizeDelta = new Vector2(6f, 0f); tr.anchoredPosition = Vector2.zero;
        var timg = tgo.AddComponent<Image>(); timg.color = new Color(0f, 0f, 0f, 0.35f); timg.raycastTarget = false;
        var bgo = new GameObject("Bar", typeof(RectTransform));
        var br = bgo.GetComponent<RectTransform>(); br.SetParent(tr, false);
        br.anchorMin = new Vector2(0f, 1f); br.anchorMax = new Vector2(1f, 1f); br.pivot = new Vector2(0.5f, 1f); br.sizeDelta = new Vector2(0f, 40f);
        var bimg = bgo.AddComponent<Image>(); bimg.color = new Color(1f, 0.85f, 0.45f, 0.8f); bimg.raycastTarget = false;
        var s = vp.AddComponent<UguiScrollText>();
        s.viewport = vrt; s.content = crt; s.text = t; s.bar = bimg; s.track = timg;
        return s;
    }

    public void ResetTop() { scrollY = 0f; Apply(); }

    void OnDisable() { pressed = dragging = false; }

    void LateUpdate()
    {
        if (text == null) return;
        if (text.text != lastText || Mathf.Abs(content.rect.width - lastWidth) > 0.5f)
        {
            if (text.text != lastText) scrollY = 0f; // 表示対象/言語が変わった: 先頭から
            lastText = text.text; lastWidth = content.rect.width;
            float w = content.rect.width > 1f ? content.rect.width : viewport.rect.width - 12f;
            var settings = text.GetGenerationSettings(new Vector2(w, 0f));
            settings.generateOutOfBounds = true;
            contentH = text.cachedTextGeneratorForLayout.GetPreferredHeight(text.text, settings) / Mathf.Max(0.01f, text.pixelsPerUnit) + 4f;
            if (contentH < 1f || float.IsNaN(contentH)) contentH = viewport.rect.height;
        }
        viewH = viewport.rect.height;
        float max = Mathf.Max(0f, contentH - viewH);
        HandlePointer(max);
        scrollY = Mathf.Clamp(scrollY, 0f, max);
        Apply();
    }

    void Apply()
    {
        if (content == null) return;
        content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(contentH, viewH));
        content.anchoredPosition = new Vector2(0f, scrollY);
        float max = Mathf.Max(0f, contentH - viewH);
        bool show = max > 1f;
        if (track != null && track.enabled != show) track.enabled = show;
        if (bar != null)
        {
            if (bar.enabled != show) bar.enabled = show;
            if (show)
            {
                float barH = Mathf.Max(24f, viewH * viewH / Mathf.Max(1f, contentH));
                var br = bar.rectTransform;
                br.sizeDelta = new Vector2(0f, barH);
                br.anchoredPosition = new Vector2(0f, -(viewH - barH) * (scrollY / max));
            }
        }
    }

    Camera Cam()
    {
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        var root = canvas != null ? canvas.rootCanvas : null;
        return root != null && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
    }

    void HandlePointer(float max)
    {
        bool down = false, held = false; Vector2 pos = default;
        if (Input.touchCount > 0)
        {
            var t = Input.GetTouch(0); pos = t.position;
            down = t.phase == TouchPhase.Began; held = t.phase != TouchPhase.Ended && t.phase != TouchPhase.Canceled;
        }
        else { pos = Input.mousePosition; down = Input.GetMouseButtonDown(0); held = Input.GetMouseButton(0); }
        bool inside = RectTransformUtility.RectangleContainsScreenPoint(viewport, pos, Cam());
        if (down) { pressed = inside && max > 0.5f; dragging = false; pressPos = pos; lastPointerY = pos.y; if (!inside) PointerCaptured = false; }
        if (pressed && held)
        {
            if (!dragging && Mathf.Abs(pos.y - pressPos.y) > DragThreshold) { dragging = true; PointerCaptured = true; }
            if (dragging)
            {
                float scale = canvas != null && canvas.rootCanvas != null ? Mathf.Max(0.01f, canvas.rootCanvas.scaleFactor) : 1f;
                scrollY += (pos.y - lastPointerY) / scale; // 指を上へ動かす = 下の文を読む
                lastPointerY = pos.y;
            }
        }
        if (!held) { if (pressed || dragging) { pressed = false; dragging = false; } if (Input.touchCount == 0 && !Input.GetMouseButton(0)) PointerCaptured = false; }
        if (inside && Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f) scrollY -= Input.mouseScrollDelta.y * 40f;
        int req = PadNav.ScrollRequestForUgui(viewport);
        if (req != 0) scrollY += req * viewH * 0.5f;
    }
}
