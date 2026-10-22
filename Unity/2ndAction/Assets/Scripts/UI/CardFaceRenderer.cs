using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 共通のカード面を IMGUI の画面でも出す(2026-10-22 画面側の完成度向上 第2段階)。
//  ・ガチャ結果などの IMGUI の画面は、今まで絵とレア度の枠を自分で重ねていたので、デッキ編集/合成/3択のカード(uGUI の RewardCardUI)と見た目が違った。
//  ・ここでは デッキ編集の詳細のカード(GameManager.deckEditUI.detailPreviewCard)を写して、専用のカメラで RenderTexture へ1回描き、
//    IMGUI は GUI.DrawTexture でその絵を出す。カードの部品・枠・文字は uGUI と全く同じ(作りを二重に持たない)。
//  ・描くのは頼まれた次の LateUpdate(写したカードの Start を済ませてから)。まだ無い間は Get が null を返す → 呼ぶ側は今までの簡易表示
//  ・最近の数枚だけ持つ(MaxCache)。言語を切り替えたら作り直す
public class CardFaceRenderer : MonoBehaviour
{
    const int Layer = 31;           // 使っていない層(このカメラだけが描く)
    const int TexW = 400, TexH = 600; // カードの縦横比(2:3)
    const int MaxCache = 12;

    static CardFaceRenderer inst;
    Camera cam;
    Canvas canvas;
    RewardCardUI face;
    float faceH;
    readonly Dictionary<string, RenderTexture> cache = new Dictionary<string, RenderTexture>();
    readonly List<string> order = new List<string>();
    readonly List<(string key, CardDefinition card, int level, int count, bool plain)> pending = new List<(string, CardDefinition, int, int, bool)>();
    int locVersion = -1;

    // 共通のカード面の絵(まだ描けていなければ null。次のフレーム以降に用意される)
    // plain: 所持の状態の札(NEW)を出さない(RESULT のラン中の強化など、所持と関係の無い所)
    public static Texture Get(CardDefinition card, int level, int count = 0, bool plain = false)
    {
        if (card == null) return null;
        var r = Ensure();
        if (r == null) return null;
        if (r.locVersion != Loc.Version) r.ClearCache();
        string key = card.cardId + "#" + level + "#" + count + (plain ? "#P" : CardInventory.IsNewUnconfirmed(card.cardId) ? "#N" : ""); // NEW の札の有無も絵に入る
        if (r.cache.TryGetValue(key, out var tex) && tex != null && tex.IsCreated()) return tex;
        foreach (var p in r.pending) if (p.key == key) return null;
        r.pending.Add((key, card, level, count, plain));
        return null;
    }

    // 先に描いておく(ガチャで引いた時点など)
    public static void Prewarm(CardDefinition card, int level, int count = 0) => Get(card, level, count);

    static CardFaceRenderer Ensure()
    {
        if (inst != null) return inst;
        var gm = GameManager.Instance;
        var template = gm != null && gm.deckEditUI != null ? gm.deckEditUI.detailPreviewCard : null;
        if (template == null) return null;
        var go = new GameObject("CardFaceRenderer");
        DontDestroyOnLoad(go);
        go.AddComponent<FloatingOriginExempt>();
        go.transform.position = new Vector3(0f, -50000f, 0f); // 画面のカメラから遠く(重ねて写らない)
        inst = go.AddComponent<CardFaceRenderer>();
        inst.Build(template);
        return inst;
    }

    void Build(RewardCardUI template)
    {
        var camGO = new GameObject("Cam");
        camGO.transform.SetParent(transform, false);
        cam = camGO.AddComponent<Camera>();
        cam.enabled = false; // 手で Render する
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.cullingMask = 1 << Layer;
        cam.nearClipPlane = 0.1f; cam.farClipPlane = 100f;

        var canvasGO = new GameObject("Canvas");
        canvasGO.transform.SetParent(transform, false);
        canvasGO.layer = Layer;
        canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 10f;

        var clone = Instantiate(template.gameObject, canvasGO.transform, false);
        clone.name = "Face";
        SetLayer(clone.transform);
        face = clone.GetComponent<RewardCardUI>();
        var rt = face.rect != null ? face.rect : (RectTransform)clone.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        faceH = Mathf.Max(1f, template.rect.sizeDelta.y);
        clone.SetActive(true);
    }

    static void SetLayer(Transform t)
    {
        t.gameObject.layer = Layer;
        for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i));
    }

    void LateUpdate()
    {
        if (pending.Count == 0 || face == null) return;
        var gm = GameManager.Instance;
        if (gm == null) return;
        var p = pending[0];
        pending.RemoveAt(0);
        var tex = new RenderTexture(TexW, TexH, 16, RenderTextureFormat.ARGB32) { name = "CardFace_" + p.card.cardId, antiAliasing = 1 };
        tex.Create();
        cam.targetTexture = tex;
        // 文字がぼけないよう、拡大はキャンバスの倍率で(縦いっぱい、上下に少しの余白。FocusScale の分も入れる)
        canvas.scaleFactor = TexH / (faceH * 1.08f * Mathf.Max(1f, face.rect.localScale.y));
        var data = gm.MakeOwnedCardData(p.card, Mathf.Max(1, p.level), p.count);
        if (p.plain) data.ShowNewBadge = false;
        face.ShowFrontImmediate(data);
        face.SetInteractable(false);
        canvas.scaleFactor = TexH / (faceH * 1.08f * Mathf.Max(1f, face.rect.localScale.y));
        Canvas.ForceUpdateCanvases();
        cam.Render();
        cam.targetTexture = null;
        cache[p.key] = tex;
        order.Remove(p.key); order.Add(p.key);
        while (order.Count > MaxCache)
        {
            string old = order[0]; order.RemoveAt(0);
            if (cache.TryGetValue(old, out var o) && o != null) o.Release();
            cache.Remove(old);
        }
        locVersion = Loc.Version;
    }

    void ClearCache()
    {
        foreach (var kv in cache) if (kv.Value != null) kv.Value.Release();
        cache.Clear(); order.Clear();
        locVersion = Loc.Version;
    }

    void OnDestroy() { ClearCache(); if (inst == this) inst = null; }

    // IMGUI で出す共通の形: 描けていればカード面、まだなら絵+レア度の枠(今までの簡易表示)。r はカードの縦横比(2:3)の枠
    public static void Draw(Rect r, CardDefinition card, int level, int count = 0, bool plain = false)
    {
        if (card == null) return;
        var tex = Get(card, level, count, plain);
        if (tex != null) { GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit, true); return; }
        Sprite frame = CardRarityFrames.GetFrame(card.rarity, null);
        float ic = Mathf.Min(r.width, r.height) * 0.78f;
        var iconRect = new Rect(r.center.x - ic * 0.5f, r.center.y - ic * 0.5f, ic, ic);
        if (frame != null) GUI.DrawTexture(r, frame.texture, ScaleMode.ScaleToFit);
        if (card.icon != null) GUI.DrawTexture(iconRect, card.icon, ScaleMode.ScaleToFit);
    }
}
