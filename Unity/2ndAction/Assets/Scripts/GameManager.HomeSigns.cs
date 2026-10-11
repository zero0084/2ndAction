using UnityEngine;

// ホームの各入口の名前の札(2026-10-11、実機の要望「初めての人にも、どこを押すと何ができるか分かるように」)。
//  ・部屋の押せる物の近くに、行き先の名前を小さな札で常に出す(暗い下地 + 金の縁 + 明るい文字。既存の金の飾りと同じ色)。
//    名前は実際の行き先の画面の名前に合わせる: キャラ選択 / デッキ編集 / カード合成 / ガチャ / 出発(扉 → マップ選択 → 出発)。
//    縦画面の施設のボタンの列も同じ名前にした(DrawPortraitFacilityButtons)。
//  ・札を押しても、その物を押したのと同じ(同じ短い光の反応のあと、同じ画面へ)。
//  ・札の縁はゆっくり明るくなったり戻ったりする(札ごとに周期と位相をずらして、全部が同時に光らない)。
//    物ごとの小さな動き(扉の光/ガチャの揺れ/カードが浮く/本の光)は HomeIdleFx の既存の演出をそのまま使う。
//  ・中断中のランがある時の扉は「CONTINUE」の表示が既にあるので「出発」の札は出さない。
//  ・札の位置は物の矩形(部屋の絵 bgRoomRect の割合)から決めるので、縦横/回転の後も物と札とタップの範囲が一致する。画面の外へは出さない。
public partial class GameManager
{
    public static string[] HomeSignLabels => new[] { "キャラ選択", "デッキ編集", "カード合成", "ガチャ", "出発" };
    public static int HomeSignTaps { get; private set; } // 確認用
    public Rect[] LastHomeSignRects { get; private set; } = new Rect[5];

    float HomeSignFont => Mathf.Clamp(bgRoomRect.height * 0.03f, 13f, 30f);

    // 札を1枚描く。押されたら true(タップの範囲 = 札)
    bool DrawHomeSign(int idx, Vector2 anchor, bool below, string label, bool interactable, float fade, float phase)
    {
        float fs = HomeSignFont;
        var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(fs), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
        string shown = Loc.Auto(label);
        float maxW = Mathf.Min(bgRoomRect.width * 0.3f, Screen.width - SafeLeft() - SafeRight() - 12f);
        Vector2 sz = st.CalcSize(new GUIContent(shown));
        if (sz.x + fs * 1.4f > maxW) { st.fontSize = Mathf.Max(11, Mathf.FloorToInt(st.fontSize * (maxW - fs * 1.4f) / Mathf.Max(1f, sz.x))); sz = st.CalcSize(new GUIContent(shown)); }
        float w = sz.x + fs * 1.4f, h = sz.y + fs * 0.5f;
        Rect r = new Rect(anchor.x - w * 0.5f, below ? anchor.y : anchor.y - h, w, h);
        // 画面(安全な範囲)の中へ
        r.x = Mathf.Clamp(r.x, SafeLeft() + 4f, Screen.width - SafeRight() - 4f - r.width);
        r.y = Mathf.Clamp(r.y, SafeTop() + 4f, Screen.height - SafeBottom() - 4f - r.height);
        LastHomeSignRects[idx] = r;

        float t = Time.unscaledTime;
        float pulse = 0.5f + 0.5f * Mathf.Sin(t * (Mathf.PI * 2f / (3.2f + phase * 0.7f)) + phase * 2.1f); // 札ごとに周期と位相をずらす
        Color keep = GUI.color;
        GUI.color = new Color(0.04f, 0.05f, 0.12f, 0.82f * fade); GUI.DrawTexture(r, Texture2D.whiteTexture);
        Color gold = new Color(HudGoldColor.r, HudGoldColor.g, HudGoldColor.b, fade * Mathf.Lerp(0.55f, 1f, pulse));
        GUI.color = gold;
        float b = Mathf.Max(1.5f, fs * 0.09f);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, b), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(r.x, r.yMax - b, r.width, b), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, b, r.height), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(r.xMax - b, r.y, b, r.height), Texture2D.whiteTexture);
        // 物の方を指す小さな印(札と物の対応)
        float tri = fs * 0.32f, cx = Mathf.Clamp(anchor.x, r.x + tri * 2f, r.xMax - tri * 2f);
        Rect dot = below ? new Rect(cx - tri * 0.5f, r.y - tri * 0.55f, tri, tri) : new Rect(cx - tri * 0.5f, r.yMax - tri * 0.45f, tri, tri);
        Matrix4x4 m = GUI.matrix; GUIUtility.RotateAroundPivot(45f, dot.center); GUI.DrawTexture(dot, Texture2D.whiteTexture); GUI.matrix = m;
        st.normal.textColor = new Color(1f, 0.93f, 0.7f, fade);
        GUI.color = Color.white;
        GUI.Label(r, shown, st);
        GUI.color = keep;
        bool hit = interactable && fade > 0.99f && GUI.Button(r, GUIContent.none, GUIStyle.none);
        if (hit) HomeSignTaps++;
        return hit;
    }

    void DrawHomeSigns(bool interactable, float fade, Rect doorRect, Rect characterRect, Rect bedRect, Rect bookRect, Rect machineRect)
    {
        if (fade < 0.05f) return;
        for (int i = 0; i < LastHomeSignRects.Length; i++) LastHomeSignRects[i] = Rect.zero;
        // キャラ選択: 肖像画の下(名前の下)
        if (DrawHomeSign(0, new Vector2(characterRect.center.x, characterRect.yMax + 4f), true, HomeSignLabels[0], interactable, fade, 0f))
        { characterHotspotFlashTimer = roomHotspotFlashDuration; PlayRoomTap(); OpenCharacterSelect(); }
        // デッキ編集: ベッドの上のカードの上
        Vector2 deckAt = new Vector2(bgRoomRect.x + bgRoomRect.width * 0.17f, bgRoomRect.y + bgRoomRect.height * 0.63f);
        if (DrawHomeSign(1, deckAt, false, HomeSignLabels[1], interactable, fade, 1f))
        { bedHotspotFlashTimer = roomHotspotFlashDuration; PlayRoomTap(); OpenDeckEdit(); }
        // カード合成: 本の山の上
        if (DrawHomeSign(2, new Vector2(bookRect.center.x, bookRect.y + bookRect.height * 0.12f), false, HomeSignLabels[2], interactable, fade, 2f))
        { bookHotspotFlashTimer = roomHotspotFlashDuration; PlayRoomTap(); OpenCardFusion(); }
        // ガチャ: 機械の下(上は横画面の設定/マルチのボタンと重なる)
        if (machineRect.width > 1f && !gachaResultOpen && DrawHomeSign(3, new Vector2(machineRect.center.x, machineRect.yMax + 4f), true, HomeSignLabels[3], interactable, fade, 3f))
        { deskHotspotFlashTimer = roomHotspotFlashDuration; OnGachaMachineTapped(); }
        // 出発: 扉の下(中断中のランがある時は CONTINUE の表示があるので出さない)
        if (!RunCheckpoint.HasActiveRun && DrawHomeSign(4, new Vector2(doorRect.center.x, doorRect.yMax + 6f), true, HomeSignLabels[4], interactable, fade, 4f))
        { doorHotspotFlashTimer = roomHotspotFlashDuration; lastDoorRect = doorRect; OnDoorTapped(); }
    }

    void PlayRoomTap() { if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx(roomTapSe); }
}
