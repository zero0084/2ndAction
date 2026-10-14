using UnityEngine;

// ホームの各入口の名前の札(2026-10-11、実機の要望「初めての人にも、どこを押すと何ができるか分かるように」)。
//  ・部屋の押せる物の近くに、行き先の名前を小さな札で常に出す(暗い下地 + 金の縁 + 明るい文字。既存の金の飾りと同じ色)。
//    名前は実際の行き先の画面の名前に合わせる: キャラ選択 / デッキ編集 / カード合成 / ガチャ / 出発(扉 → マップ選択 → 出発)。
//    縦画面の施設のボタンの列も同じ名前にした(DrawPortraitFacilityButtons)。
//  ・札を押しても、その物を押したのと同じ(同じ短い光の反応のあと、同じ画面へ)。
//  ・札の縁はゆっくり明るくなったり戻ったりする(札ごとに周期と位相をずらして、全部が同時に光らない)。
//    物ごとの小さな動き(扉の光/ガチャの揺れ/カードが浮く/本の光)は HomeIdleFx の既存の演出をそのまま使う。
//  ・2026-10-14(マスターの要望): 中断中のランがある時は、扉の下に「続きから」(主な選択肢。大きく、何m から再開かを2行目に)と、
//    少し間を空けて「はじめから」(新しいランを出発。中断中のランを終える確認を出す)。どちらも札の範囲より少し広く押せる。
//    「続きから」も扉も同じ再開。マルチの接続中(中断データからは再開しない)と中断データが無い時は、今までどおり「出発」。
//  ・札の位置は物の矩形(部屋の絵 bgRoomRect の割合)から決めるので、縦横/回転の後も物と札とタップの範囲が一致する。画面の外へは出さない。
public partial class GameManager
{
    public static string[] HomeSignLabels => new[] { "キャラ選択", "デッキ編集", "カード合成", "ガチャ", "出発" };
    public static int HomeSignTaps { get; private set; } // 確認用
    public Rect[] LastHomeSignRects { get; private set; } = new Rect[6];      // 4 = 出発 / 続きから、5 = はじめから
    public Rect[] LastHomeSignHitRects { get; private set; } = new Rect[6];
    public bool HomeShowsContinueChoice => RunCheckpoint.HasActiveRun && !NetSession.IsActive;

    float HomeSignFont => Mathf.Clamp(bgRoomRect.height * 0.03f, 13f, 30f);

    // 札を1枚描く。押されたら true(タップの範囲 = 札)
    bool DrawHomeSign(int idx, Vector2 anchor, bool below, string label, bool interactable, float fade, float phase, string sub = null, float scale = 1f, bool primary = false)
    {
        float fs = HomeSignFont * scale;
        float padX = scale > 1.01f ? 2.2f : 1.4f, padY = scale > 1.01f ? 1.0f : 0.5f; // 大きい札は文字の周りの余白も広く
        var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(fs), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
        string shown = Loc.Auto(label);
        float maxW = Mathf.Min(bgRoomRect.width * 0.3f, Screen.width - SafeLeft() - SafeRight() - 12f);
        Vector2 sz = st.CalcSize(new GUIContent(shown));
        if (scale > 1.01f) maxW = Mathf.Min(Mathf.Max(maxW, bgRoomRect.width * 0.42f), Screen.width - SafeLeft() - SafeRight() - 12f);
        if (sz.x + fs * padX > maxW) { st.fontSize = Mathf.Max(11, Mathf.FloorToInt(st.fontSize * (maxW - fs * padX) / Mathf.Max(1f, sz.x))); sz = st.CalcSize(new GUIContent(shown)); }
        // 2行目(2026-10-11、実機で「キャラ選択」の札と肖像画の下の名前が重なっていた → 名前を札の中の2行目へまとめた)
        GUIStyle subSt = null; Vector2 subSz = Vector2.zero;
        if (!string.IsNullOrEmpty(sub))
        {
            subSt = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(fs * 0.72f), alignment = TextAnchor.MiddleCenter, wordWrap = false };
            subSz = subSt.CalcSize(new GUIContent(sub));
            if (subSz.x + fs * padX > maxW) { subSt.fontSize = Mathf.Max(10, Mathf.FloorToInt(subSt.fontSize * (maxW - fs * padX) / Mathf.Max(1f, subSz.x))); subSz = subSt.CalcSize(new GUIContent(sub)); }
        }
        float w = Mathf.Max(sz.x, subSz.x) + fs * padX, h = sz.y + subSz.y + fs * padY;
        Rect r = new Rect(anchor.x - w * 0.5f, below ? anchor.y : anchor.y - h, w, h);
        // 画面(安全な範囲)の中へ
        r.x = Mathf.Clamp(r.x, SafeLeft() + 4f, Screen.width - SafeRight() - 4f - r.width);
        r.y = Mathf.Clamp(r.y, SafeTop() + 4f, Screen.height - SafeBottom() - 4f - r.height);
        LastHomeSignRects[idx] = r;

        float t = Time.unscaledTime;
        float pulse = 0.5f + 0.5f * Mathf.Sin(t * (Mathf.PI * 2f / (3.2f + phase * 0.7f)) + phase * 2.1f); // 札ごとに周期と位相をずらす
        Color keep = GUI.color;
        GUI.color = primary ? new Color(0.32f, 0.22f, 0.05f, 0.92f * fade) : new Color(0.04f, 0.05f, 0.12f, 0.82f * fade); GUI.DrawTexture(r, Texture2D.whiteTexture);
        Color gold = new Color(HudGoldColor.r, HudGoldColor.g, HudGoldColor.b, fade * Mathf.Lerp(0.55f, 1f, pulse));
        GUI.color = gold;
        float b = Mathf.Max(1.5f, fs * (primary ? 0.12f : 0.09f));
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, b), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(r.x, r.yMax - b, r.width, b), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, b, r.height), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(r.xMax - b, r.y, b, r.height), Texture2D.whiteTexture);
        // 物の方を指す小さな印(札と物の対応)
        float tri = fs * 0.32f, cx = Mathf.Clamp(anchor.x, r.x + tri * 2f, r.xMax - tri * 2f);
        Rect dot = below ? new Rect(cx - tri * 0.5f, r.y - tri * 0.55f, tri, tri) : new Rect(cx - tri * 0.5f, r.yMax - tri * 0.45f, tri, tri);
        Matrix4x4 m = GUI.matrix; GUIUtility.RotateAroundPivot(45f, dot.center); GUI.DrawTexture(dot, Texture2D.whiteTexture); GUI.matrix = m;
        st.normal.textColor = new Color(1f, 0.93f, 0.7f, fade);
        GUI.color = Color.white;
        if (primary) st.normal.textColor = new Color(1f, 0.97f, 0.82f, fade);
        if (subSt == null) GUI.Label(r, shown, st);
        else
        {
            float top = r.y + fs * padY * 0.4f;
            GUI.Label(new Rect(r.x, top, r.width, sz.y), shown, st);
            subSt.normal.textColor = new Color(HudGoldColor.r, HudGoldColor.g, HudGoldColor.b, 0.85f * fade);
            GUI.Label(new Rect(r.x, top + sz.y - fs * 0.05f, r.width, subSz.y), sub, subSt);
        }
        GUI.color = keep;
        // タップの範囲: 大きい札は見た目より少し広く(縦の余白ぶん。隣の札とは重ならない間隔で並べる)
        Rect hitR = scale > 1.01f ? new Rect(r.x - fs * 0.3f, r.y - fs * 0.25f, r.width + fs * 0.6f, r.height + fs * 0.5f) : r;
        if (scale > 1.01f) LastHomeSignHitRects[idx] = hitR;
        bool hit = interactable && fade > 0.99f && GUI.Button(hitR, GUIContent.none, GUIStyle.none);
        if (hit) HomeSignTaps++;
        return hit;
    }

    void DrawHomeSigns(bool interactable, float fade, Rect doorRect, Rect characterRect, Rect bedRect, Rect bookRect, Rect machineRect)
    {
        if (fade < 0.05f) return;
        for (int i = 0; i < LastHomeSignRects.Length; i++) { LastHomeSignRects[i] = Rect.zero; LastHomeSignHitRects[i] = Rect.zero; }
        // キャラ選択: 肖像画の下(2行目に今のキャラの名前。以前の名前の表示はここへまとめた)
        var cdef = CharacterDatabase.FindById(SelectedCharacterId);
        if (DrawHomeSign(0, new Vector2(characterRect.center.x, characterRect.yMax + 4f), true, HomeSignLabels[0], interactable, fade, 0f, cdef != null ? cdef.displayName : null))
        { characterHotspotFlashTimer = roomHotspotFlashDuration; PlayRoomTap(); OpenCharacterSelect(); }
        // デッキ編集: ベッドの上のカードの上
        Vector2 deckAt = new Vector2(bgRoomRect.x + bgRoomRect.width * 0.17f, bgRoomRect.y + bgRoomRect.height * 0.63f);
        if (DrawHomeSign(1, deckAt, false, HomeSignLabels[1], interactable, fade, 1f))
        { bedHotspotFlashTimer = roomHotspotFlashDuration; PlayRoomTap(); OpenDeckEdit(); }
        // カード合成: 本の山の上
        if (DrawHomeSign(2, new Vector2(bookRect.center.x, bookRect.y + bookRect.height * 0.12f), false, HomeSignLabels[2], interactable, fade, 2f))
        { bookHotspotFlashTimer = roomHotspotFlashDuration; PlayRoomTap(); OpenCardFusion(); }
        // ガチャ: 機械の下(上は横画面の設定/マルチのボタンと重なる)。横画面で「広告で1回引く」が機械の下にある時は、その下
        float gachaY = machineRect.yMax + 4f;
        if (!HomePortrait && AdGachaAvailable) gachaY = machineRect.yMax + 6f + 54f * GachaUiScale + 6f;
        if (machineRect.width > 1f && !gachaResultOpen && DrawHomeSign(3, new Vector2(machineRect.center.x, gachaY), true, HomeSignLabels[3], interactable, fade, 3f))
        { deskHotspotFlashTimer = roomHotspotFlashDuration; OnGachaMachineTapped(); }
        if (HomeShowsContinueChoice)
        {
            // 続きから(主): 扉の下。2行目に再開の地点。押すと扉と同じ再開
            int m = Mathf.FloorToInt(RunCheckpoint.Load().checkpointDistance);
            if (DrawHomeSign(4, new Vector2(doorRect.center.x, doorRect.yMax + 6f), true, "続きから", interactable, fade, 4f, Loc.F("{0}m から再開", m.ToString("N0")), 1.3f, true))
            { doorHotspotFlashTimer = roomHotspotFlashDuration; lastDoorRect = doorRect; OnDoorTapped(); }
            // はじめから: 続きからの下、押し間違えない間隔を空けて(タップの範囲どうしが重ならない)
            Rect c = LastHomeSignHitRects[4];
            float gapY = HomeSignFont * 0.9f;
            if (DrawHomeSign(5, new Vector2(doorRect.center.x, c.yMax + gapY + HomeSignFont * 0.25f), true, "はじめから", interactable, fade, 5f, null, 1.15f, false))
            { PlayRoomTap(); showNewRunConfirm = true; }
        }
        // 出発: 扉の下(中断中のランが無い時)
        else if (DrawHomeSign(4, new Vector2(doorRect.center.x, doorRect.yMax + 6f), true, HomeSignLabels[4], interactable, fade, 4f))
        { doorHotspotFlashTimer = roomHotspotFlashDuration; lastDoorRect = doorRect; OnDoorTapped(); }
    }

    void PlayRoomTap() { if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx(roomTapSe); }
}
