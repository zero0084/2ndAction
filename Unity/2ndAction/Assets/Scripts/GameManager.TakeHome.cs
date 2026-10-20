using UnityEngine;

// 持ち帰り予定 MILE(2026-10-11、実機の要望): ラン中に今回どれだけ稼いだかを見て、ガチャを引くために帰還するか、もう少し走るかを決められるように。
//  ・HUD(左の列の SPEED の下): 「持ち帰り予定 ○○ MILE」= 帰還に成功した時に受け取る今回分。所持している MILE とは別
//  ・帰還(脱出の長押し)の間と一時停止の画面: 今回の持ち帰り / 帰還後の所持の合計 / その合計で引ける通常のガチャの回数 / 倒れると受け取れないこと
// 値は FinishRun の持ち帰りと同じ式(TakeHomeMile)。表示のために MILE を先に足すことはしない。
// 広告の「MILE 2倍」など、条件付きの追加分は含めない(結果の画面で別に出る)。
// 中断/再開(CONTINUE)は、敵/ボス/BONUS/リングの分と最高到達距離が保存されているので、再開後も同じ値になる(距離の分は毎回計算し直すので二重に足されない)。
public partial class GameManager
{
    // 距離の MILE(FinishRun でも同じ式を使う)。疾走で飛ばした距離は除く
    // 2026-10-15: 距離の MILE も参加人数で分配(100m ごとに 1 → 自分の補正 → 人数で割る)。
    // 第2弾: ほかの経路の端数(mileCarry)もここで足して整数にする → 持ち帰り = floor(全経路の正確な合計)
    int DistanceMileNow => Mathf.FloorToInt(DistanceMileExact + mileCarry + 1e-4f);

    // ラン中の画面中央の知らせ(ボスの登場/再開まで/COMBO/ラスダンの知らせ/必殺技の名前)が使ってよい一番上(実際の画面の座標)。
    // 2026-10-11(実機: イベント名/進行表示が上の HUD に重なった): 縦画面は左の列(BEST/距離/速度/持ち帰り予定)の下、
    // 横画面は中央の Lv/EXP の下より下へ置く。固定の割合(画面の 17% 等)だけで置くと、縦画面で左の列に重なっていた
    public static float NoticeTopPx
    {
        get
        {
            var gm = Instance;
            if (gm == null || !gm.HasStarted) return 0f;
            float b = HudStacked ? gm.HudLeftColumnBottom : gm.GetLevelExpPanelRect().yMax;
            return b + 10f * HudK;
        }
    }

    // 帰還に成功した時に受け取る今回分(広告の追加分は含まない)
    public int TakeHomeMile => DistanceMileNow + RunEnemyMile + RunBossMile + RunBonusMile + RunRingMile;

    // 今回分を持ち帰った後の所持 MILE と、それで引ける通常のガチャ(1回 GachaCostMile)の回数
    public int OwnedMileAfterReturn => TotalOwnedMile + TakeHomeMile;
    public int GachaPullsAfterReturn => GachaCostMile > 0 ? OwnedMileAfterReturn / GachaCostMile : 0;

    // 闘技場は進行を書かないので MILE も無い
    bool ShowTakeHomeHud => HasStarted && !IsGameOver && !ArenaMode.Active && !(HudStacked && IsRewardSequenceRunning); // 縦画面のカード選択の間は出さない(1枚目の枠に重なる)

    const float MilePanelHeight = 44f;
    Rect GetMilePanelRect() => HudReal(VSafeLeft + UiMargin, VBestY + (HudPanelHeight + HudPanelGap) * 2f + 30f + 6f, HudDistanceWidth(), MilePanelHeight);

    // 左の列(BEST/距離/速度/持ち帰り予定)のいちばん下(実際の画面の座標)。下に置く物はここから
    float HudLeftColumnBottom => ShowTakeHomeHud ? GetMilePanelRect().yMax : GetSpeedPanelRect().yMax;

    // 金貨の絵(持ち帰り予定の印)。シーンを読み直しても消えないように HideAndDontSave
    static Texture2D mileCoin;
    static Texture2D MileCoinIcon()
    {
        if (mileCoin != null) return mileCoin;
        const int n = 32;
        mileCoin = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f - n / 2f) / (n / 2f), dy = (y + 0.5f - n / 2f) / (n / 2f);
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = 1f - Mathf.SmoothStep(0.86f, 1f, r);
                if (a <= 0f) { px[y * n + x] = new Color32(0, 0, 0, 0); continue; }
                float rim = Mathf.SmoothStep(0.62f, 0.8f, r);
                float hl = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(dx, dy), new Vector2(-0.3f, 0.35f)) * 1.7f);
                Color c = Color.Lerp(new Color(1f, 0.86f, 0.36f), new Color(0.72f, 0.5f, 0.1f), rim);
                c = Color.Lerp(c, new Color(1f, 0.97f, 0.72f), hl * 0.55f);
                px[y * n + x] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(a * 255f));
            }
        mileCoin.SetPixels32(px);
        mileCoin.Apply();
        return mileCoin;
    }

    // 金色の印(MILE)。金の縁 → 濃い金 → 明るい中心のひし形を重ねる(白い四角を回して塗る。ホームの札の印と同じ描き方)
    static void DrawMileMedal(Rect r)
    {
        // 2026-10-20: 正式な絵(金の菱形の硬貨、UiIcons.Mile)があればそれ。菱形が縦長なので少し大きめの枠に収める
        if (UiIcons.Mile != null) { UiIcons.Draw(new Rect(r.x - r.width * 0.1f, r.y - r.height * 0.15f, r.width * 1.2f, r.height * 1.3f), UiIcons.Mile); return; }
        Color keep = GUI.color; Matrix4x4 m = GUI.matrix;
        Vector2 c = r.center;
        GUIUtility.RotateAroundPivot(45f, c);
        float s0 = r.width * 0.74f, s1 = s0 * 0.78f, s2 = s0 * 0.42f;
        GUI.color = new Color(0.45f, 0.3f, 0.05f, 1f); GUI.DrawTexture(new Rect(c.x - s0 * 0.5f, c.y - s0 * 0.5f, s0, s0), Texture2D.whiteTexture);
        GUI.color = new Color(0.95f, 0.72f, 0.2f, 1f); GUI.DrawTexture(new Rect(c.x - s1 * 0.5f, c.y - s1 * 0.5f, s1, s1), Texture2D.whiteTexture);
        GUI.color = new Color(1f, 0.93f, 0.6f, 1f); GUI.DrawTexture(new Rect(c.x - s2 * 0.5f, c.y - s2 * 0.5f, s2, s2), Texture2D.whiteTexture);
        GUI.matrix = m; GUI.color = keep;
    }

    static string FormatMile(int v) => v.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture);

    static GUIStyle FitStyle(GUIStyle st, string text, float width, int minSize = 10)
    {
        Vector2 sz = st.CalcSize(new GUIContent(Loc.Auto(text)));
        if (sz.x > width && sz.x > 1f) st.fontSize = Mathf.Max(minSize, Mathf.FloorToInt(st.fontSize * width / sz.x));
        return st;
    }

    // HUD の1行(BeginHud の中で呼ぶ)
    void DrawTakeHomeHud()
    {
        if (!ShowTakeHomeHud) return;
        Rect r = V(GetMilePanelRect());
        UiBackdrop.Draw(r, 0.85f);
        const float pad = 10f, icon = 16f;
        DrawMileMedal(new Rect(r.x + pad, r.y + 5f, icon, icon));
        var labelStyle = FitStyle(new GUIStyle(GUI.skin.label) { fontSize = HudLabelFontSize, alignment = TextAnchor.MiddleLeft, normal = { textColor = HudLabelColor } },
            "持ち帰り予定", r.width - pad * 2f - icon - 4f);
        LocGUI.Label(new Rect(r.x + pad + icon + 4f, r.y + 3f, r.width - pad * 2f - icon - 4f, 20f), "持ち帰り予定", labelStyle);
        string value = FormatMile(TakeHomeMile) + " MILE";
        var valueStyle = FitStyle(new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = HudGoldColor } },
            value, r.width - pad * 2f);
        LocGUI.Label(new Rect(r.x + pad, r.y + 20f, r.width - pad * 2f, r.height - 22f), value, valueStyle);
    }

    // 帰還の判断のための内訳(脱出の長押しの間 / 一時停止の画面)。r は BeginHud の設計の単位
    public const float TakeHomeSummaryHeight = 166f, TakeHomeCompactHeight = 118f;
    void DrawTakeHomeSummary(Rect r, string title, bool compact = false)
    {
        if (compact) { DrawTakeHomeCompact(r); return; }
        OrnateUi.DrawPanel(r, 0.92f);
        float x = r.x + 22f, w = r.width - 44f, y = r.y + 14f; // 飾り枠(OrnateUi)の縁の内側
        var titleStyle = FitStyle(new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.85f, 0.9f, 1f) } }, title, w);
        LocGUI.Label(new Rect(x, y, w, 20f), title, titleStyle);
        y += 24f;
        void Row(string label, string value, Color vc, bool coin)
        {
            float lx = x + (coin ? 22f : 0f);
            if (coin) DrawMileMedal(new Rect(x, y + 3f, 18f, 18f));
            var vs = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = vc } };
            float vw = Mathf.Min(w * 0.55f, vs.CalcSize(new GUIContent(value)).x + 4f);
            var ls = FitStyle(new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } }, label, w - vw - (lx - x) - 6f);
            LocGUI.Label(new Rect(lx, y, w - vw - (lx - x) - 6f, 24f), label, ls);
            FitStyle(vs, value, vw);
            LocGUI.Label(new Rect(x + w - vw, y, vw, 24f), value, vs);
            y += 26f;
        }
        Row("今回の持ち帰り予定", "+" + FormatMile(TakeHomeMile) + " MILE", HudGoldColor, true);
        Row("脱出後の所持予定", FormatMile(OwnedMileAfterReturn) + " MILE", Color.white, false);
        Row(Loc.F("ガチャ({0} MILE)", GachaCostMile), Loc.F("{0}回分", GachaPullsAfterReturn), new Color(0.6f, 0.95f, 1f), false);
        var ns = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.UpperLeft, wordWrap = true, normal = { textColor = new Color(1f, 0.62f, 0.55f) } };
        string note = "倒れるかランを破棄すると、今回のMILEは受け取れません";
        float nh = ns.CalcHeight(new GUIContent(Loc.Auto(note)), w);
        if (nh > r.yMax - y - 8f) ns.fontSize = 11;
        LocGUI.Label(new Rect(x, y + 2f, w, r.yMax - y - 6f), note, ns);
    }

    // 戦闘中の長押し用の小さな内訳(飾り枠なし、3行、注意は1行)
    void DrawTakeHomeCompact(Rect r)
    {
        UiBackdrop.Draw(r, 0.85f);
        float x = r.x + 12f, w = r.width - 24f, y = r.y + 8f;
        void Row(string label, string value, Color vc, bool medal)
        {
            if (medal) DrawMileMedal(new Rect(x, y + 3f, 14f, 14f));
            float lx = x + (medal ? 18f : 0f);
            var vs = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = vc } };
            float vw = Mathf.Min(w * 0.55f, vs.CalcSize(new GUIContent(value)).x + 4f);
            var ls = FitStyle(new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.9f, 0.92f, 1f) } }, label, w - vw - (lx - x) - 4f);
            LocGUI.Label(new Rect(lx, y, w - vw - (lx - x) - 4f, 20f), label, ls);
            FitStyle(vs, value, vw);
            LocGUI.Label(new Rect(x + w - vw, y, vw, 20f), value, vs);
            y += 22f;
        }
        Row("今回の持ち帰り予定", "+" + FormatMile(TakeHomeMile) + " MILE", HudGoldColor, true);
        Row("脱出後の所持予定", FormatMile(OwnedMileAfterReturn) + " MILE", Color.white, false);
        Row(Loc.F("ガチャ({0} MILE)", GachaCostMile), Loc.F("{0}回分", GachaPullsAfterReturn), new Color(0.6f, 0.95f, 1f), false);
        var ns = FitStyle(new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(1f, 0.62f, 0.55f) } }, "倒れるかランを破棄すると、今回のMILEは受け取れません", w, 9);
        LocGUI.Label(new Rect(x, y + 2f, w, 18f), "倒れるかランを破棄すると、今回のMILEは受け取れません", ns);
    }

    // 脱出の長押しの間(1秒以上止めたまま押した時だけ。短いタップ/攻撃/フリックでは出さない。離したら短く消える)
    public const float TakeHomeDetailDelay = 1f;
    float takeHomeDetailA;
    void DrawEscapeTakeHome()
    {
        var pc = PlayerController.Instance;
        bool want = pc != null && ShowTakeHomeHud && pc.EscapeHoldSeconds >= TakeHomeDetailDelay;
        if (Event.current.type == EventType.Repaint) takeHomeDetailA = Mathf.MoveTowards(takeHomeDetailA, want ? 1f : 0f, Time.unscaledDeltaTime / (want ? 0.15f : 0.2f));
        if (pc == null || !ShowTakeHomeHud || takeHomeDetailA <= 0.01f) return;
        BeginHud();
        Color keepA = GUI.color; GUI.color = new Color(keepA.r, keepA.g, keepA.b, keepA.a * takeHomeDetailA);
        float w = Mathf.Min(300f, VW - VSafeLeft - VSafeRight - UiMargin * 2f);
        float y = HudStacked ? HudLeftColumnBottom / HudK + 12f : VSafeTop + UiMargin + HudPanelHeight + 16f;
        float x = (VW - w) * 0.5f;
        // 脱出の輪(キャラの上)に重ねない: 横画面は輪の右へ
        var cam = Camera.main;
        if (!HudStacked && cam != null)
        {
            float ringRight = (cam.WorldToScreenPoint(pc.transform.position).x + 45f + 30f) / HudK + 12f;
            x = Mathf.Max(x, ringRight);
        }
        x = Mathf.Min(x, VW - VSafeRight - UiMargin - w);
        DrawTakeHomeSummary(new Rect(x, y, w, TakeHomeCompactHeight), "帰還すると受け取れます", compact: true);
        GUI.color = keepA;
        EndHud();
    }
}
