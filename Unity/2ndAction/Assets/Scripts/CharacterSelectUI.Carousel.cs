using UnityEngine;
using UnityEngine.UI;

// キャラ選択のカードとカルーセル(2026-11-03)。
//  カード: 以前は1人ずつ別の1枚絵(<id>_card.png、枠・名前・役割を焼き込み済み)を縦横比を保って出していた → 絵の縦横比(0.49〜0.60)が
//   そのままカードの外寸の差になり、枠の太さ・飾り・名前の板も絵ごとに違って見えた。
//   今は全員同じ規格: 共通の枠(UI で組む)+ 立ち絵(<id>_main.png、紙の地の1枚絵)の「体」の部分を窓へ + 共通の名前の板。
//   新しいキャラは立ち絵と BodyBox の1行を足せば同じカードになる(焼き込みの絵は使わない)。
//  カルーセル: 中央からの距離で連続して大きさ/明るさを変える。指/マウスを離すと一番近い(または払った向きの)カードへ吸着し、
//   吸着が終わった時に選択を確定する(立ち絵・名前・能力・キャラカードはこの時に切り替わる)。矢印・ホイール・キー/パッドの左右も同じ動き。
public partial class CharacterSelectUI
{
    // ---- 中央からの大きさ(中央 1.0 / 隣 0.84 / さらに外 0.68)。カード自体は全員同じ外寸で、差は位置による倍率だけ ----
    public const float CenterBoost = 1.10f;           // 中央のカードの拡大(枠の外寸 210×388 に対して)
    public const float NeighborScale = 0.84f, OuterScale = 0.68f;
    public const float FlickVelocity = 500f, FastFlickVelocity = 2600f; // 払いの速さ(カルーセルの座標/秒)。速い払いでも2人まで(Windows の本物のマウスの軽い払いが約 590/秒)
    public const float DistanceStep = 0.28f;          // 払わずに離した時: カード幅のこの割合を超えて動かしたら次へ
    public const float SnapMin = 0.16f, SnapMax = 0.30f;
    public const float MainVisualFade = 0.16f;        // 確定した時の大きい立ち絵の切り替え

    // 立ち絵(1024×1536)の中の体: 中心の x、頭の上、足もと(画素、上が 0)。武器/翼/尾/マントは数えない。
    //  ref = 窓の高さに合わせる「体の高さ」(ルカ/ペンタは小柄さを残しつつ極端に小さくしない、忍者は低い構えのまま)
    static readonly System.Collections.Generic.Dictionary<string, Vector4> BodyBox = new System.Collections.Generic.Dictionary<string, Vector4>
    {
        { "swordsman",     new Vector4(560f, 116f, 1428f, 0f) },
        { "dual_blade",    new Vector4(515f, 228f, 1428f, 0f) },
        { "gunslinger",    new Vector4(512f, 150f, 1420f, 0f) },
        { "dragon_lancer", new Vector4(470f, 364f, 1428f, 1150f) }, // エステル: 槍の形が分かるよう少し引く
        { "noble_lady",    new Vector4(522f, 170f, 1400f, 0f) },
        { "archer",        new Vector4(480f, 373f, 1428f, 1120f) }, // エステル: 弓の上端が窮屈 → 約5% 引く
        { "mage",          new Vector4(490f, 160f, 1400f, 0f) },
        { "fighter",       new Vector4(500f, 140f, 1410f, 0f) },
        { "ninja",         new Vector4(515f, 600f, 1330f, 1150f) },
        { "miko",          new Vector4(512f, 200f, 1428f, 0f) },
        { "vampire",       new Vector4(544f, 100f, 1410f, 0f) },
        { "dragonkin",     new Vector4(522f, 236f, 1428f, 0f) },
        { "luka",          new Vector4(600f, 760f, 1340f, 1060f) },
        { "pentatuttulu",  new Vector4(500f, 560f, 1300f, 1060f) },
    };
    const float BodyFill = 0.86f;     // 体の高さ = 窓の高さのこの割合
    const float FeetMargin = 0.035f;  // 足もとの下の余白(窓の高さに対する割合)

    class Face { public RectTransform rt; public RawImage art; public Image rim, glow, backLight; public Text name, role; public string id; }
    Face[] faces;

    // 枠/窓/名前の板を作る(1回だけ。シーンを作り直さなくても実行時に組む)
    void EnsureCardFaces()
    {
        var all = CharacterDatabase.AllCharacters;
        if (faces != null && faces.Length == cardSlotRects.Length) return;
        faces = new Face[cardSlotRects.Length];
        Font font = titleText != null ? titleText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        for (int i = 0; i < cardSlotRects.Length && i < all.Count; i++)
        {
            var slot = cardSlotRects[i];
            if (slot == null) continue;
            var f = new Face { id = all[i].characterId };
            var go = new GameObject("Face", typeof(RectTransform));
            f.rt = (RectTransform)go.transform;
            f.rt.SetParent(slot, false);
            f.rt.SetAsFirstSibling(); // NEW の印は上に
            f.rt.anchorMin = f.rt.anchorMax = f.rt.pivot = new Vector2(0.5f, 0.5f);
            f.rt.sizeDelta = slot.rect.size.sqrMagnitude > 1f ? slot.rect.size : new Vector2(cardWidth, cardWidth * 1.85f);
            f.rt.anchoredPosition = Vector2.zero;
            Vector2 size = f.rt.sizeDelta;
            Sprite rounded = i < cardGlowImages.Length && cardGlowImages[i] != null ? cardGlowImages[i].sprite : null;

            // 背面の薄い光(中央だけ)と、選択中の金の縁(以前の SelectionGlow をこの中へ)
            f.backLight = Part(f.rt, "BackLight", rounded, new Color(1f, 0.82f, 0.45f, 0f), -26f);
            if (i < cardGlowImages.Length && cardGlowImages[i] != null)
            {
                f.glow = cardGlowImages[i];
                var g = f.glow.rectTransform;
                g.SetParent(f.rt, false); g.SetSiblingIndex(1);
                g.anchorMin = Vector2.zero; g.anchorMax = Vector2.one; g.pivot = new Vector2(0.5f, 0.5f);
                g.offsetMin = new Vector2(-7f, -7f); g.offsetMax = new Vector2(7f, 7f); g.localScale = Vector3.one;
            }
            // 枠: 金の縁 → 紺の地 → 窓(立ち絵)→ 窓の内側の細い金の線
            f.rim = Part(f.rt, "Rim", rounded, UiTheme.GoldEdge, 0f);
            Part(f.rt, "Base", rounded, UiTheme.NavyDeep, -3f);
            float plateH = Mathf.Round(size.y * 0.17f), pad = 8f;
            var artGo = new GameObject("Art", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var art = (RectTransform)artGo.transform; art.SetParent(f.rt, false);
            art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(pad, pad + plateH + 2f); art.offsetMax = new Vector2(-pad, -pad);
            f.art = artGo.GetComponent<RawImage>(); f.art.raycastTarget = false;
            Line(f.rt, art, UiTheme.GoldEdge * new Color(1f, 1f, 1f, 0.85f), 1.5f);
            // 名前の板(下)
            var plate = Part(f.rt, "Plate", rounded, UiTheme.Navy, 0f).rectTransform;
            plate.anchorMin = new Vector2(0f, 0f); plate.anchorMax = new Vector2(1f, 0f); plate.pivot = new Vector2(0.5f, 0f);
            plate.offsetMin = new Vector2(pad - 1f, pad - 1f); plate.offsetMax = new Vector2(-(pad - 1f), pad - 1f + plateH);
            var plateLine = Part(f.rt, "PlateLine", null, UiTheme.GoldEdge, 0f).rectTransform;
            plateLine.anchorMin = new Vector2(0f, 0f); plateLine.anchorMax = new Vector2(1f, 0f); plateLine.pivot = new Vector2(0.5f, 0.5f);
            plateLine.offsetMin = new Vector2(pad + 6f, pad + plateH - 1f); plateLine.offsetMax = new Vector2(-(pad + 6f), pad + plateH + 1f);
            Diamond(f.rt, new Vector2(0f, -size.y * 0.5f + pad + plateH), 11f);   // 板と窓の境目の金の菱形
            Diamond(f.rt, new Vector2(0f, size.y * 0.5f - 2f), 13f);             // 上の飾り
            f.name = Label(f.rt, "Name", font, Mathf.RoundToInt(size.x * 0.105f), FontStyle.Bold, UiTheme.GoldText,
                new Vector2(pad + 4f, pad + plateH * 0.40f), new Vector2(-(pad + 4f), pad + plateH - 3f));
            f.role = Label(f.rt, "Role", font, Mathf.RoundToInt(size.x * 0.077f), FontStyle.Normal, UiTheme.TextSubBlue,
                new Vector2(pad + 4f, pad + 2f), new Vector2(-(pad + 4f), pad + plateH * 0.42f));
            // 以前の焼き込みのカードの絵は使わない(残っていても見えない)
            var oldPortrait = slot.Find("Portrait"); if (oldPortrait != null) { var im = oldPortrait.GetComponent<Image>(); if (im != null) im.enabled = false; }
            faces[i] = f;
        }
        RefreshCardFaces();
    }

    // 名前/役割/窓の中の立ち絵(言語の切り替え・開いた時)
    void RefreshCardFaces()
    {
        if (faces == null) return;
        var all = CharacterDatabase.AllCharacters;
        for (int i = 0; i < faces.Length && i < all.Count; i++)
        {
            var f = faces[i]; if (f == null) continue;
            var def = all[i];
            f.name.text = Loc.Auto(def.displayName);
            f.role.text = Loc.Auto(def.role);
            f.art.texture = def.mainVisual;
            f.art.uvRect = BodyUv(def.characterId, def.mainVisual, f.art.rectTransform.rect.size);
        }
    }

    // 窓(縦横比)に、体が同じ割合で入る立ち絵の範囲(UV)
    public static Rect BodyUv(string id, Texture tex, Vector2 window)
    {
        if (tex == null || window.x < 1f || window.y < 1f) return new Rect(0f, 0f, 1f, 1f);
        float W = tex.width, H = tex.height, k = H / 1536f; // 表は 1536 の高さの画素
        var b = BodyBox.TryGetValue(id, out var v) ? v : new Vector4(512f, 150f, 1420f, 0f);
        float cx = b.x * k, top = b.y * k, feet = b.z * k;
        float body = Mathf.Max(feet - top, (b.w > 0f ? b.w : 0f) * k);
        float aspect = window.x / window.y;
        float vh = body / BodyFill;
        vh = Mathf.Min(vh, H, W / aspect);         // 絵の外は見せない(縦横とも)
        float vw = vh * aspect;
        float bottom = Mathf.Min(H, feet + FeetMargin * vh), topY = bottom - vh;
        if (topY < 0f) { bottom -= topY; topY = 0f; }
        float left = Mathf.Clamp(cx - vw * 0.5f, 0f, W - vw);
        return new Rect(left / W, (H - bottom) / H, vw / W, vh / H);
    }

    // 中央からの距離 d(カードの間隔が1)→ 倍率。連続(位置と大きさが一緒に動く)
    public static float ScaleAt(float d)
    {
        d = Mathf.Abs(d);
        float s = d <= 1f ? Mathf.Lerp(1f, NeighborScale, d) : Mathf.Lerp(NeighborScale, OuterScale, Mathf.Min(1f, d - 1f));
        return CenterBoost * s;
    }

    // 毎フレーム: 各カードの大きさ・内側への寄せ・明るさ・縁の光を、ビューの中央からの距離で決める(選択中かどうかではない)
    void UpdateCarouselFaces()
    {
        if (faces == null || carouselContent == null || cardStride <= 0f) return;
        float centerX = carouselViewportWidth * 0.5f;
        for (int i = 0; i < faces.Length; i++)
        {
            var f = faces[i]; if (f == null || cardSlotRects[i] == null || !cardSlotRects[i].gameObject.activeSelf) continue;
            float cx = carouselContent.anchoredPosition.x + cardSlotRects[i].anchoredPosition.x + cardWidth * 0.5f;
            float d = (cx - centerX) / cardStride;
            float ad = Mathf.Abs(d);
            if (ad > 3.2f) { if (f.rt.gameObject.activeSelf) f.rt.gameObject.SetActive(false); continue; } // 画面の外は描かない
            if (!f.rt.gameObject.activeSelf) f.rt.gameObject.SetActive(true);
            float s = ScaleAt(d);
            f.rt.localScale = new Vector3(s, s, 1f);
            // 小さくなった分の隙間を詰める(隣どうしの間が広がりすぎない)
            f.rt.anchoredPosition = new Vector2(-Mathf.Sign(d) * (CenterBoost - s) * cardWidth * 0.45f, 0f);
            float focus = Mathf.Clamp01(1f - ad);
            float tone = Mathf.Lerp(0.80f, 1f, Mathf.Clamp01(1.6f - ad)); // 隣は少しだけ暗く(読める明るさ)
            f.art.color = new Color(tone, tone, tone, 1f);
            f.rim.color = Color.Lerp(UiTheme.GoldEdge * new Color(0.8f, 0.8f, 0.8f, 1f), UiTheme.GoldBright, focus);
            if (f.glow != null) f.glow.color = new Color(1f, 0.85f, 0.4f, 0.9f * focus * focus);
            if (f.backLight != null) f.backLight.color = new Color(1f, 0.82f, 0.45f, 0.16f * focus);
            // 中央を手前に(拡大した中央のカードが隣の上に重なる)
            if (focus > 0.5f && cardSlotRects[i].GetSiblingIndex() != cardSlotRects[i].parent.childCount - 1) cardSlotRects[i].SetAsLastSibling();
        }
    }

    // ---- ドラッグ/払い ----
    int dragStartIndex; float dragStartContentX, dragVelocity, lastDragMoveTime;
    public int LastReleaseTarget { get; private set; } = -1; // 確認用

    void DragBegin()
    {
        dragStartIndex = TargetIndex; // 吸着の途中で掴んだ時は、向かっていた先から数える
        snapping = false;
        dragStartContentX = carouselContent != null ? carouselContent.anchoredPosition.x : 0f;
        dragVelocity = 0f; lastDragMoveTime = Time.unscaledTime;
    }

    void DragMove(float deltaX)
    {
        if (carouselContent == null) return;
        float dt = Mathf.Max(1e-3f, Time.unscaledDeltaTime);
        Vector2 pos = carouselContent.anchoredPosition;
        pos.x = ClampContentX(pos.x + deltaX);
        carouselContent.anchoredPosition = pos;
        dragVelocity = Mathf.Lerp(dragVelocity, deltaX / dt, 0.55f);
        lastDragMoveTime = Time.unscaledTime;
    }

    void DragEnd()
    {
        float v = Time.unscaledTime - lastDragMoveTime > 0.08f ? 0f : dragVelocity; // 止めてから離した = 払っていない
        int target = ReleaseTarget(v);
        LastReleaseTarget = target;
        if (Debug.isDebugBuild) Debug.Log($"[Carousel] release: from {dragStartIndex} moved {(carouselContent != null ? carouselContent.anchoredPosition.x : 0f) - dragStartContentX:F0} v {v:F0}/s -> {target}");
        BeginSnap(target);
    }

    // 離した時の行き先: 払った(速い)→ 払った向きへ1人(とても速い時は2人まで)/ 払っていない → 一番近いカード
    //  (ただしカードの間隔の 28% を超えて動かしていれば次へ)。誤操作で何人も飛ばない
    int ReleaseTarget(float v)
    {
        float moved = (carouselContent != null ? carouselContent.anchoredPosition.x : 0f) - dragStartContentX;
        int dir = moved < 0f ? 1 : -1; // 列を左へ動かす = 次のキャラ
        int nearest = ComputeNearestCardIndex();
        int target;
        if (Mathf.Abs(v) > FlickVelocity && Mathf.Sign(v) == Mathf.Sign(moved) && Mathf.Abs(moved) > 4f)
        {
            int steps = Mathf.Abs(v) > FastFlickVelocity ? 2 : 1;
            int byFlick = dragStartIndex + dir * steps;
            target = dir > 0 ? Mathf.Max(nearest, byFlick) : Mathf.Min(nearest, byFlick);
        }
        else if (nearest == dragStartIndex && Mathf.Abs(moved) > DistanceStep * cardStride) target = dragStartIndex + dir;
        else target = nearest;
        return Mathf.Clamp(target, 0, Mathf.Max(0, VisibleCount - 1));
    }

    // 吸着の行き先(吸着中はその先、止まっている時は選択中)。矢印/キーを続けて押した時はここから次へ
    int TargetIndex => snapping ? snapIndex : selectedIndex;
    public int DetailRefreshes { get; private set; } // 確認用: 立ち絵/説明を切り替えた回数(ドラッグ中は増えない)
    int snapIndex;

    // キー/パッドの左右(キーボードの ←/→・A/D、パッドの十字/左スティック)。PadNav の左右の移動はこの画面では使わない
    void HandleCarouselKeys()
    {
        PadNav.HorizontalOwnerFrame = Time.frameCount;
        if (VisibleCount <= 0) return;
        if (GameInput.DownRepeat(GameAction.NavLeft)) { if (TargetIndex > 0) BeginSnap(TargetIndex - 1); }
        else if (GameInput.DownRepeat(GameAction.NavRight)) { if (TargetIndex < VisibleCount - 1) BeginSnap(TargetIndex + 1); }
    }

    static Image Part(RectTransform parent, string name, Sprite sprite, Color c, float inset)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-inset, -inset); rt.offsetMax = new Vector2(inset, inset);
        var im = go.GetComponent<Image>(); im.sprite = sprite; im.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple; im.color = c; im.raycastTarget = false;
        return im;
    }

    static void Line(RectTransform parent, RectTransform around, Color c, float w)
    {
        for (int k = 0; k < 4; k++)
        {
            var go = new GameObject("ArtLine" + k, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
            rt.anchorMin = around.anchorMin; rt.anchorMax = around.anchorMax;
            Vector2 a = around.offsetMin, b = around.offsetMax;
            if (k == 0) { rt.offsetMin = new Vector2(a.x, a.y - w); rt.offsetMax = new Vector2(b.x, 0f); rt.anchorMax = new Vector2(1f, 0f); }
            else if (k == 1) { rt.anchorMin = new Vector2(0f, 1f); rt.offsetMin = new Vector2(a.x, b.y); rt.offsetMax = new Vector2(b.x, b.y + w); }
            else if (k == 2) { rt.anchorMax = new Vector2(0f, 1f); rt.offsetMin = new Vector2(a.x - w, a.y); rt.offsetMax = new Vector2(a.x, b.y); }
            else { rt.anchorMin = new Vector2(1f, 0f); rt.offsetMin = new Vector2(b.x, a.y); rt.offsetMax = new Vector2(b.x + w, b.y); }
            var im = go.GetComponent<Image>(); im.color = c; im.raycastTarget = false;
        }
    }

    static void Diamond(RectTransform parent, Vector2 pos, float size)
    {
        var go = new GameObject("Gem", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size); rt.anchoredPosition = pos; rt.localRotation = Quaternion.Euler(0f, 0f, 45f);
        var im = go.GetComponent<Image>(); im.color = UiTheme.GoldEdge; im.raycastTarget = false;
        var inner = new GameObject("In", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var irt = (RectTransform)inner.transform; irt.SetParent(rt, false);
        irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one; irt.offsetMin = new Vector2(2.5f, 2.5f); irt.offsetMax = new Vector2(-2.5f, -2.5f);
        var iim = inner.GetComponent<Image>(); iim.color = new Color(0.25f, 0.55f, 0.95f); iim.raycastTarget = false;
    }

    static Text Label(RectTransform parent, string name, Font font, int size, FontStyle style, Color c, Vector2 offMin, Vector2 offMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f);
        rt.offsetMin = offMin; rt.offsetMax = new Vector2(offMax.x, offMax.y);
        var t = go.GetComponent<Text>(); t.font = font; t.fontSize = size; t.fontStyle = style; t.color = c;
        t.alignment = TextAnchor.MiddleCenter; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
        t.resizeTextForBestFit = true; t.resizeTextMinSize = Mathf.Max(8, size / 2); t.resizeTextMaxSize = size; t.raycastTarget = false;
        var o = go.AddComponent<Outline>(); o.effectColor = new Color(0f, 0f, 0f, 0.7f); o.effectDistance = new Vector2(1f, -1f);
        return t;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 確認用(-qaCarousel): 実際の入力と同じ処理を通す
    public void DebugDragBegin() => DragBegin();
    public void DebugDragMove(float dx) => DragMove(dx);
    public void DebugDragEnd() => DragEnd();
    public void DebugTapCard(int pos) { var slot = SlotAt(pos); if (slot != null) HandleTap(RectTransformUtility.WorldToScreenPoint(null, slot.TransformPoint(slot.rect.center))); }
    public bool DebugSnapping => snapping;
    public int DebugSelected => selectedIndex;
    public int DebugTarget => TargetIndex;
    public string DebugSelectedId => selectedIndex >= 0 && selectedIndex < VisibleCount ? DefAt(selectedIndex).characterId : "";
    public float DebugScaleOfPos(int pos) { int i = pos >= 0 && pos < order.Length ? order[pos] : -1; return i >= 0 && faces != null && faces[i] != null ? faces[i].rt.localScale.x : -1f; }
    public Vector2 DebugFaceSize(int pos) { int i = pos >= 0 && pos < order.Length ? order[pos] : -1; return i >= 0 && faces != null && faces[i] != null ? faces[i].rt.rect.size : Vector2.zero; }
    public RectTransform DebugFace(int pos) { int i = pos >= 0 && pos < order.Length ? order[pos] : -1; return i >= 0 && faces != null && faces[i] != null ? faces[i].rt : null; }
    public Rect DebugArtUv(int pos) { int i = pos >= 0 && pos < order.Length ? order[pos] : -1; return i >= 0 && faces != null && faces[i] != null ? faces[i].art.uvRect : default; }
    public float CardStrideDebug => cardStride;
#endif
}
