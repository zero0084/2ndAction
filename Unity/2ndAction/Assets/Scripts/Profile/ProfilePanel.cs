using System.Globalization;
using UnityEngine;

// プロフィール画面(2026-11-07、IMGUI。ランキング/設定と同じ作り)。ホームの「プロフィール」から開く。
//  SNS のように情報を増やさず「どんな走りをしている人か分かるプレイヤーカード」: キャラ → 戦績 → 走る理由(大きめ) → バッヂ。
//   横画面: 左 = キャラ・名前 / 右 = 戦績(BEST・累計・最高速度・マップ別 100km・ラスダン) / 下の段 = 走る理由・装備バッヂ3個
//   縦画面: 上 = キャラ・名前・走る理由・バッヂ3個 / 下 = 戦績(収まらなければ縦にスクロール)
//  ページ: プロフィール / 走る理由を選ぶ(定型文から1つ。自由入力なし)/ バッヂとチャレンジ(一覧・装備3枠)
//  数値は保存された記録をそのまま読む(PlayerProfileCard.FromLocal。将来のランキングの表示と同じデータ)
public class ProfilePanel : MonoBehaviour
{
    public static ProfilePanel Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.open;
    public enum Page { Profile, Reason, Badges }
    public static Page CurrentPage => Instance != null ? Instance.page : Page.Profile;

    bool open;
    Page page;
    int selSlot;
    int filter; // 0 すべて / 1 達成済み / 2 未達成
    float lockedNoteT;
    PlayerProfileCard card;
    readonly UiScroll mainScroll = new UiScroll(), listScroll = new UiScroll(), reasonScroll = new UiScroll();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[ProfilePanel]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<ProfilePanel>();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => { if (Instance != null) Instance.open = false; };
    }

    public static void OpenStatic(Page p = Page.Profile) { if (Instance != null) Instance.Open(p); }
    public static void CloseStatic() { if (Instance != null && Instance.open) Instance.Close(); }
    // Android の戻る/Esc: サブページ → プロフィール → 閉じる
    public static bool HandleBack()
    {
        if (!IsOpen) return false;
        if (Instance.page != Page.Profile) Instance.GoPage(Page.Profile); else Instance.Close();
        return true;
    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static void DebugGoPage(Page p) { if (Instance != null) Instance.GoPage(p); }
    public static void DebugSelectSlot(int i) { if (Instance != null) Instance.selSlot = Mathf.Clamp(i, 0, ProfileStore.Slots - 1); }
    public static void DebugFilter(int f) { if (Instance != null) Instance.filter = f; }
    public static void DebugScroll(float y) { if (Instance != null) { Instance.mainScroll.y = y; Instance.listScroll.y = y; Instance.reasonScroll.y = y; } }
    public static void DebugLockedNote() { if (Instance != null) Instance.lockedNoteT = 3f; }
#endif

    void Open(Page p)
    {
        open = true; card = PlayerProfileCard.FromLocal();
        mainScroll.Reset(); listScroll.Reset(); reasonScroll.Reset();
        selSlot = FirstEmptySlot(); filter = 0; lockedNoteT = 0f;
        page = p;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiOpen);
        Debug.Log("[Profile] open " + card.ToJson());
    }
    void Close()
    {
        open = false; UiInputGate.LatchUntilRelease();
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiClose);
    }
    void GoPage(Page p)
    {
        page = p; card = PlayerProfileCard.FromLocal(); listScroll.Reset(); reasonScroll.Reset();
        if (p == Page.Badges) selSlot = FirstEmptySlot();
        UiInputGate.LatchUntilRelease();
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiTab);
    }
    static int FirstEmptySlot() { for (int i = 0; i < ProfileStore.Slots; i++) if (string.IsNullOrEmpty(ProfileStore.Slot(i))) return i; return 0; }

    void Update() { if (lockedNoteT > 0f) lockedNoteT -= Time.unscaledDeltaTime; }

    float S => UiScalePref.Menu(Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 1f, 2.6f));

    void OnGUI()
    {
        if (!open) return;
        GUI.depth = -1900;
        int padLayer = PadNav.BeginLayer(9);
        float s = S;
        Matrix4x4 keep = GUI.matrix;
        float w = Screen.width / s, h = Screen.height / s;
        Rect safe = StableSafeArea.Rect;
        float sl = safe.xMin / s, sr = (Screen.width - safe.xMax) / s, st = (Screen.height - safe.yMax) / s, sb = safe.yMin / s;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        UiKit.Fill(new Rect(0f, 0f, w, h), new Color(0.02f, 0.03f, 0.08f, 0.66f));
        bool land = w > h;
        float pw = Mathf.Min(land ? 1220f : 760f, w - sl - sr - 16f), ph = Mathf.Min(h - st - sb - 12f, land ? 740f : 1400f);
        var panel = new Rect(sl + (w - sl - sr - pw) * 0.5f, st + (h - st - sb - ph) * 0.5f, pw, ph);
        OrnateUi.DrawPanel(panel, 0.96f);
        string title = page == Page.Reason ? LocProfile.ReasonPickTitle : page == Page.Badges ? LocProfile.OpenBadges : LocProfile.Title;
        LocGUI.Label(new Rect(panel.x + 28f, panel.y + 12f, panel.width - 110f, 44f), title, UiKit.Label(28f, TextAnchor.MiddleLeft, true, UiTheme.GoldText));
        if (UiIcons.CloseButton(new Rect(panel.xMax - 68f, panel.y + 12f, 50f, 46f))) Close();
        if (card == null) card = PlayerProfileCard.FromLocal();

        var body = new Rect(panel.x + 22f, panel.y + 64f, panel.width - 44f, panel.height - 64f - 14f);
        if (page == Page.Profile) { if (land) DrawProfileLandscape(body); else DrawProfilePortrait(body); }
        else
        {
            var inner = new Rect(body.x, body.y, body.width, body.height - 62f);
            if (page == Page.Reason) DrawReasons(inner, land); else DrawBadges(inner, land);
            if (UiKit.Button(new Rect(body.center.x - 110f, body.yMax - 54f, 220f, 52f), "戻る", 22f, false) && open) GoPage(Page.Profile);
        }

        GUI.Button(new Rect(0f, 0f, w, h), GUIContent.none, GUIStyle.none); // 背後へタップを通さない
        if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.MouseDrag) Event.current.Use();
        GUI.matrix = keep;
        PadNav.EndLayer(padLayer);
    }

    // ===================================================================== プロフィール
    void DrawProfileLandscape(Rect b)
    {
        const float band = 236f;
        float top = b.height - band - 12f;
        float lw = Mathf.Min(360f, b.width * 0.31f);
        DrawCharCard(new Rect(b.x, b.y, lw, top));
        DrawRecords(new Rect(b.x + lw + 22f, b.y, b.width - lw - 22f, top));
        float wy = b.y + top + 12f;
        float ww = Mathf.Round(b.width * 0.52f);
        DrawWhy(new Rect(b.x, wy, ww, band));
        DrawBadgeSlots(new Rect(b.x + ww + 16f, wy, b.width - ww - 16f, band), true);
    }

    const float PCharH = 250f, PWhyH = 150f, PBadgeH = 236f;
    void DrawProfilePortrait(Rect b)
    {
        float recH = RecordsHeight(b.width);
        float contentH = PCharH + 12f + PWhyH + 12f + PBadgeH + 16f + recH + 8f;
        mainScroll.Begin(b, contentH, "profile");
        float cw = b.width - 10f, y = 0f;
        DrawCharCard(new Rect(0f, y, cw, PCharH)); y += PCharH + 12f;
        DrawWhy(new Rect(0f, y, cw, PWhyH)); y += PWhyH + 12f;
        DrawBadgeSlots(new Rect(0f, y, cw, PBadgeH), true); y += PBadgeH + 16f;
        DrawRecords(new Rect(0f, y, cw, recH));
        mainScroll.End(b, contentH);
    }

    // キャラの絵と名前(縦長なら絵の下に名前 / 横長なら絵の右に名前)
    void DrawCharCard(Rect r)
    {
        UiBackdrop.Draw(r, 0.55f);
        var def = CharacterDatabase.FindById(card.characterId);
        bool wide = r.width > r.height * 1.25f;
        Rect art = wide ? new Rect(r.x + 8f, r.y + 8f, Mathf.Min(r.width * 0.42f, r.height * 0.8f), r.height - 16f) : new Rect(r.x + 8f, r.y + 8f, r.width - 16f, r.height - 96f);
        UiKit.Fill(art, new Color(0.12f, 0.14f, 0.26f, 0.85f));
        Texture2D tex = def != null ? (def.portrait != null ? def.portrait : def.mainVisual) : null;
        if (tex != null && Event.current.type == EventType.Repaint) GUI.DrawTexture(art, tex, ScaleMode.ScaleToFit);
        Rect plate = wide ? new Rect(art.xMax + 14f, r.y + 10f, r.xMax - art.xMax - 22f, r.height - 20f) : new Rect(r.x + 10f, art.yMax + 6f, r.width - 20f, 80f);
        bool named = !string.IsNullOrEmpty(card.name);
        float nameH = wide ? Mathf.Min(64f, plate.height * 0.4f) : 44f;
        if (named) GUI.Label(new Rect(plate.x, plate.y, plate.width, nameH), Loc.Shape(card.name), FitStyle(card.name, 30f, plate.width, TextAnchor.MiddleLeft, Color.white));
        else LocGUI.Label(new Rect(plate.x, plate.y, plate.width, nameH), LocProfile.NameUnset, UiKit.Label(22f, TextAnchor.MiddleLeft, false, UiTheme.TextSub));
        string cname = def != null ? def.displayName : card.characterId;
        LocGUI.Label(new Rect(plate.x, plate.y + nameH, plate.width, 30f), cname, UiKit.Label(19f, TextAnchor.MiddleLeft, true, UiTheme.GoldText));
        if (wide && def != null && !string.IsNullOrEmpty(def.subtitle))
            LocGUI.Label(new Rect(plate.x, plate.y + nameH + 30f, plate.width, 26f), def.subtitle, UiKit.Label(15f, TextAnchor.MiddleLeft, false, UiTheme.TextSub));
    }

    // 戦績
    const float RecHead = 40f;
    static float RecordsHeight(float w) => RecHead + 7f * 40f;
    void DrawRecords(Rect r)
    {
        Head(new Rect(r.x, r.y, r.width, RecHead), LocProfile.Records, null);
        float rowH = Mathf.Clamp((r.height - RecHead) / 7f, 30f, 46f), y = r.y + RecHead;
        var inv = CultureInfo.InvariantCulture;
        Row(r.x, ref y, r.width, rowH, LocProfile.StatBest, Meters(card.bestMeters), UiTheme.Mile);
        Row(r.x, ref y, r.width, rowH, LocProfile.StatTotal, Meters(card.totalMeters), UiTheme.Text);
        Row(r.x, ref y, r.width, rowH, LocProfile.StatTopSpeed, card.topSpeedKmh > 0.05f ? card.topSpeedKmh.ToString("0.0", inv) + " km/h" : LocProfile.NoSpeedYet, UiTheme.Text);
        MapRow(r.x, ref y, r.width, rowH, UnlockRules.Wasteland, card.wasteland100);
        MapRow(r.x, ref y, r.width, rowH, UnlockRules.Cave, card.cave100);
        MapRow(r.x, ref y, r.width, rowH, UnlockRules.Sky, card.sky100);
        string ld = BossManager.LastStageId;
        bool ldKnown = ProgressStats.FinalDungeonUnlocked || card.lastReached || card.lastCleared;
        string ldVal = card.lastCleared ? LocProfile.Cleared : card.lastReached ? LocProfile.Reached : LocProfile.NotYet;
        Row(r.x, ref y, r.width, rowH, ldKnown ? UnlockRules.StageName(ld) : LocProfile.Unknown, ldVal, card.lastCleared ? UiTheme.Mile : card.lastReached ? UiTheme.Text : UiTheme.Locked);
    }
    void MapRow(float x, ref float y, float w, float h, string stage, bool reached)
    {
        double reach = PlayerProfileCard.MapReach(stage);
        bool known = UnlockRules.OfficialStageUnlocked(stage) || reach > 0;
        string label = known ? Loc.Src("{0}  100km", Loc.Plain(UnlockRules.StageName(stage))) : LocProfile.Unknown;
        string val = reached ? LocProfile.Reach100 : reach > 0 ? Meters(reach) : LocProfile.NotYet;
        Row(x, ref y, w, h, label, val, reached ? UiTheme.Mile : reach > 0 ? UiTheme.TextSub : UiTheme.Locked, labelRaw: known);
    }
    static void Row(float x, ref float y, float w, float h, string label, string value, Color vc, bool labelRaw = false)
    {
        UiKit.Fill(new Rect(x, y + h - 1f, w, 1f), new Color(1f, 0.85f, 0.45f, 0.18f));
        float lw = w * 0.5f;
        if (labelRaw) GUI.Label(new Rect(x + 4f, y, lw - 8f, h), Loc.Shape(label), FitStyle(label, 19f, lw - 8f, TextAnchor.MiddleLeft, UiTheme.TextSubBlue, false));
        else LocGUI.Label(new Rect(x + 4f, y, lw - 8f, h), label, UiKit.Label(19f, TextAnchor.MiddleLeft, false, UiTheme.TextSubBlue));
        LocGUI.Label(new Rect(x + lw, y, w - lw - 6f, h), value, UiKit.Label(21f, TextAnchor.MiddleRight, true, vc));
        y += h;
    }
    static string Meters(double m) => m > 0.0 ? System.Math.Floor(m).ToString("N0", CultureInfo.InvariantCulture) + " m" : "0 m";

    // 走る理由(大きめ)
    void DrawWhy(Rect r)
    {
        UiBackdrop.Draw(r, 0.6f);
        float bw = 130f;
        Head(new Rect(r.x + 14f, r.y + 6f, r.width - bw - 30f, 38f), LocProfile.WhyIRun, null);
        if (UiKit.Button(new Rect(r.xMax - bw - 12f, r.y + 8f, bw, 40f), LocProfile.Change, 18f, false) && open) GoPage(Page.Reason);
        int id = card.reasonId;
        string text = LocProfile.ReasonText(id);
        var qr = new Rect(r.x + 20f, r.y + 50f, r.width - 40f, r.height - 60f);
        if (text == null) LocGUI.Label(qr, LocProfile.ReasonNone, UiKit.Label(24f, TextAnchor.MiddleCenter, false, UiTheme.TextSub));
        else
        {
            // エステル(11-07): 文は少し下げ、金の線は文のすぐ下へ(枠の中で浮いて見えないように)
            float fs = Mathf.Min(40f, qr.height * 0.42f);
            var tr = new Rect(qr.x, qr.y + qr.height * 0.08f, qr.width, qr.height * 0.84f);
            var stl = new GUIStyle(UiKit.Label(fs, TextAnchor.MiddleCenter, true, new Color(1f, 0.93f, 0.72f)));
            LocGUI.Label(tr, text, stl);
            float th = Mathf.Min(tr.height, stl.CalcHeight(new GUIContent(Loc.Auto(text)), tr.width)); // 折り返して2行になる言語でも線が文に重ならない
            float lineY = Mathf.Min(qr.yMax - 4f, tr.center.y + Mathf.Max(fs * 0.85f, th * 0.5f + 6f));
            UiKit.Fill(new Rect(r.center.x - r.width * 0.18f, lineY, r.width * 0.36f, 2f), new Color(1f, 0.8f, 0.4f, 0.55f));
        }
    }

    // 装備中のバッヂ3個(+獲得数と一覧へのボタン)
    void DrawBadgeSlots(Rect r, bool withButton)
    {
        UiBackdrop.Draw(r, 0.6f);
        Head(new Rect(r.x + 14f, r.y + 6f, r.width * 0.5f, 38f), LocProfile.BadgesHeader, null);
        LocGUI.Label(new Rect(r.x + r.width * 0.5f, r.y + 6f, r.width * 0.5f - 14f, 38f), Loc.Src(LocProfile.BadgeCount, card.badgeCount, ChallengeStore.Total), UiKit.Label(17f, TextAnchor.MiddleRight, false, UiTheme.TextSub));
        float btnH = withButton ? 46f : 0f;
        float areaY = r.y + 48f, areaH = r.height - 48f - btnH - (withButton ? 14f : 6f);
        float gap = 12f, sw = (r.width - 28f - gap * 2f) / 3f;
        for (int i = 0; i < ProfileStore.Slots; i++)
        {
            var sr = new Rect(r.x + 14f + i * (sw + gap), areaY, sw, areaH);
            DrawSlot(sr, ProfileStore.Slot(i), false);
            bool hit = GUI.Button(sr, GUIContent.none, GUIStyle.none) | PadNav.Button(sr);
            if (hit && open && !mainScroll.ConsumedPointer) { GoPage(Page.Badges); selSlot = i; }
        }
        if (withButton && UiKit.Button(new Rect(r.center.x - 150f, r.yMax - btnH - 8f, 300f, btnH), LocProfile.OpenBadges, 19f, true) && open) GoPage(Page.Badges);
    }

    // 枠1つ(バッヂの絵+名前)。空き = 点線風の枠
    static void DrawSlot(Rect sr, string id, bool selected)
    {
        var d = ChallengeDefs.Find(id);
        UiKit.Fill(sr, new Color(0.04f, 0.05f, 0.11f, 0.7f));
        if (selected) { UiKit.ButtonStateOverlay(sr, true, true); }
        float icon = Mathf.Min(sr.width - 16f, sr.height - 30f);
        var ir = new Rect(sr.center.x - icon * 0.5f, sr.y + 4f, icon, icon);
        if (d != null)
        {
            BadgeArt.Draw(ir, d, true);
            GUI.Label(new Rect(sr.x + 2f, ir.yMax, sr.width - 4f, 24f), d.badge, FitStyle(d.badge, 14f, sr.width - 6f, TextAnchor.MiddleCenter, UiTheme.GoldText));
        }
        else
        {
            Color c = new Color(1f, 0.85f, 0.45f, 0.35f);
            float t = 2f; var e = new Rect(ir.x + icon * 0.12f, ir.y + icon * 0.12f, icon * 0.76f, icon * 0.76f);
            UiKit.Fill(new Rect(e.x, e.y, e.width, t), c); UiKit.Fill(new Rect(e.x, e.yMax - t, e.width, t), c);
            UiKit.Fill(new Rect(e.x, e.y, t, e.height), c); UiKit.Fill(new Rect(e.xMax - t, e.y, t, e.height), c);
            LocGUI.Label(new Rect(sr.x + 2f, ir.yMax, sr.width - 4f, 24f), LocProfile.SlotEmpty, UiKit.Label(14f, TextAnchor.MiddleCenter, false, UiTheme.TextSub));
        }
    }

    // ===================================================================== 走る理由を選ぶ
    void DrawReasons(Rect r, bool land)
    {
        int cols = land ? 3 : 1;
        float gap = 12f, bh = land ? 84f : 60f;
        int n = LocProfile.Reasons.Length, rows = (n + cols - 1) / cols;
        float contentH = rows * (bh + gap);
        reasonScroll.Begin(r, contentH, "reasons");
        float bw = (r.width - 10f - gap * (cols - 1)) / cols;
        int cur = ProfileStore.ReasonId;
        for (int i = 0; i < n; i++)
        {
            var (id, text) = LocProfile.Reasons[i];
            var br = new Rect((i % cols) * (bw + gap), (i / cols) * (bh + gap), bw, bh);
            if (UiKit.ButtonCore(br, text, 22f, id == cur, true, true, id == cur, 0f) && open && !reasonScroll.ConsumedPointer)
            {
                ProfileStore.SetReason(id);
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
                GoPage(Page.Profile);
            }
        }
        reasonScroll.End(r, contentH);
    }

    // ===================================================================== バッヂとチャレンジ
    void DrawBadges(Rect r, bool land)
    {
        // 上: 装備3枠(選んでいる枠に金の縁)+ 案内 + 絞り込み
        float slotsH = land ? 132f : 150f;
        float slotsW = land ? Mathf.Min(520f, r.width * 0.46f) : r.width;
        float gap = 10f, sw = (slotsW - gap * 2f) / 3f;
        for (int i = 0; i < ProfileStore.Slots; i++)
        {
            var sr = new Rect(r.x + i * (sw + gap), r.y, sw, slotsH);
            string id = ProfileStore.Slot(i);
            DrawSlot(sr, id, i == selSlot);
            if (GUI.Button(sr, GUIContent.none, GUIStyle.none) && open) { selSlot = i; if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiTab); }
            if (PadNav.Button(sr) && open) selSlot = i;
        }
        float infoX = land ? r.x + slotsW + 20f : r.x, infoY = land ? r.y : r.y + slotsH + 8f, infoW = land ? r.width - slotsW - 20f : r.width;
        var note = new GUIStyle(UiKit.Label(16f, TextAnchor.UpperLeft, false, lockedNoteT > 0f ? UiTheme.Warning : UiTheme.TextSubBlue)) { wordWrap = true };
        LocGUI.Label(new Rect(infoX, infoY, infoW, 44f), lockedNoteT > 0f ? LocProfile.LockedNote : LocProfile.EquipHint, note);
        LocGUI.Label(new Rect(infoX, infoY + 44f, infoW * 0.5f, 28f), Loc.Src(LocProfile.SelectedSlot, selSlot + 1), UiKit.Label(16f, TextAnchor.MiddleLeft, true, UiTheme.GoldText));
        LocGUI.Label(new Rect(infoX + infoW * 0.5f, infoY + 44f, infoW * 0.5f, 28f), Loc.Src(LocProfile.BadgeCount, ChallengeStore.DoneCount, ChallengeStore.Total), UiKit.Label(16f, TextAnchor.MiddleRight, false, UiTheme.TextSub));
        string[] fl = { LocProfile.AllCategory, LocProfile.ShowDone, LocProfile.ShowLocked };
        float fy = infoY + 76f, fw = (infoW - 16f) / 3f;
        for (int i = 0; i < 3; i++)
            if (UiKit.Button(new Rect(infoX + i * (fw + 8f), fy, fw, 42f), fl[i], 16f, filter == i, false) && open && filter != i) { filter = i; listScroll.Reset(); }

        // 一覧
        float ly = (land ? r.y + slotsH : fy + 42f) + 10f;
        var view = new Rect(r.x, ly, r.width, r.yMax - ly);
        int cols = land ? 2 : 1;
        const float rowH = 116f, headH = 34f, cg = 14f; // 条件の文は3行まで(長い言語: クメール語/タミル語)
        float cw = (view.width - 12f - cg * (cols - 1)) / cols;
        // 高さを先に測る(カテゴリの見出し + 行)
        float contentH = 0f;
        foreach (ChallengeCategory c in System.Enum.GetValues(typeof(ChallengeCategory)))
        {
            int k = 0; foreach (var d in ChallengeDefs.All) if (d.cat == c && Shown(d)) k++;
            if (k == 0) continue;
            contentH += headH + ((k + cols - 1) / cols) * (rowH + 8f) + 6f;
        }
        listScroll.Begin(view, Mathf.Max(contentH, 1f), "badges" + filter);
        float y = 0f;
        foreach (ChallengeCategory c in System.Enum.GetValues(typeof(ChallengeCategory)))
        {
            int k = 0; foreach (var d in ChallengeDefs.All) if (d.cat == c && Shown(d)) k++;
            if (k == 0) continue;
            Color cc = ChallengeDefs.CategoryColor(c);
            UiKit.Fill(new Rect(0f, y + 8f, 6f, headH - 12f), cc);
            GUI.Label(new Rect(14f, y, cw, headH), ChallengeDefs.CategoryName(c), UiKit.Label(19f, TextAnchor.MiddleLeft, true, cc));
            y += headH;
            int j = 0;
            foreach (var d in ChallengeDefs.All)
            {
                if (d.cat != c || !Shown(d)) continue;
                var rr = new Rect((j % cols) * (cw + cg), y + (j / cols) * (rowH + 8f), cw, rowH);
                DrawChallengeRow(rr, d);
                j++;
            }
            y += ((k + cols - 1) / cols) * (rowH + 8f) + 6f;
        }
        listScroll.End(view, Mathf.Max(contentH, 1f));
    }

    bool Shown(ChallengeDef d) => filter == 0 || (filter == 1) == ChallengeStore.IsDone(d.id);

    void DrawChallengeRow(Rect r, ChallengeDef d)
    {
        bool done = ChallengeStore.IsDone(d.id);
        int eq = ProfileStore.SlotOf(d.id);
        UiKit.Fill(r, done ? new Color(0.09f, 0.11f, 0.22f, 0.92f) : new Color(0.05f, 0.06f, 0.1f, 0.88f));
        if (eq >= 0) UiKit.ButtonStateOverlay(r, true, true);
        float icon = r.height - 16f;
        BadgeArt.Draw(new Rect(r.x + 8f, r.y + 8f, icon, icon), d, done);
        float bw = 112f, tx = r.x + icon + 18f, tw = r.width - icon - 18f - bw - 16f;
        GUI.Label(new Rect(tx, r.y + 4f, tw, 26f), d.name, FitStyle(d.name, 19f, tw, TextAnchor.MiddleLeft, done ? Color.white : UiTheme.Disabled));
        GUI.Label(new Rect(tx, r.y + 28f, tw, 18f), d.badge, FitStyle(d.badge, 13f, tw, TextAnchor.MiddleLeft, done ? UiTheme.GoldText : UiTheme.Locked));
        var cs = new GUIStyle(UiKit.Label(15f, TextAnchor.UpperLeft, false, done ? UiTheme.TextSubBlue : UiTheme.TextSub)) { wordWrap = true };
        LocGUI.Label(new Rect(tx, r.y + 47f, tw, r.height - 51f), d.condition, cs);
        // 右: 状態と操作
        var right = new Rect(r.xMax - bw - 8f, r.y + 6f, bw, r.height - 12f);
        string prog = ProgressText(d);
        if (done || prog != null) LocGUI.Label(new Rect(right.x, right.y, right.width, 22f), done ? LocProfile.Done : prog, UiKit.Label(15f, TextAnchor.MiddleCenter, true, done ? UiTheme.Success : UiTheme.TextSub)); // 未獲得は下のボタンで分かる(同じ言葉を2回出さない)
        LocGUI.Label(new Rect(right.x - 10f, right.y + 22f, right.width + 10f, 20f), Loc.Src(LocProfile.Reward, d.mile), UiKit.Label(14f, TextAnchor.MiddleCenter, false, done ? UiTheme.Mile : UiTheme.Locked));
        var br = new Rect(right.x, right.yMax - 42f, right.width, 42f);
        if (done)
        {
            bool isEq = eq >= 0;
            if (UiKit.Button(br, isEq ? LocProfile.Unequip : LocProfile.Equip, 17f, !isEq, false) && open && !listScroll.ConsumedPointer)
            {
                if (isEq) { ProfileStore.Unequip(eq); selSlot = eq; }
                else { ProfileStore.Equip(selSlot, d.id); selSlot = FirstEmptySlotAfter(selSlot); }
                card = PlayerProfileCard.FromLocal();
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
            }
        }
        else
        {
            UiKit.Button(br, LocProfile.Locked, 16f, false, false, false); // 未獲得は装備できない(押せない見た目)
            if (GUI.Button(br, GUIContent.none, GUIStyle.none) && open && !listScroll.ConsumedPointer)
            {
                lockedNoteT = 2.5f;
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiDeny);
            }
        }
    }
    static int FirstEmptySlotAfter(int cur)
    {
        for (int k = 1; k <= ProfileStore.Slots; k++) { int i = (cur + k) % ProfileStore.Slots; if (string.IsNullOrEmpty(ProfileStore.Slot(i))) return i; }
        return cur;
    }

    // 累計の数えがあるチャレンジの進み(未達成の時だけ)。ランの中だけの物は出さない
    static string ProgressText(ChallengeDef d)
    {
        if (ChallengeStore.IsDone(d.id)) return null;
        var inv = CultureInfo.InvariantCulture;
        switch (d.id)
        {
            case "dist_total": return System.Math.Floor(System.Math.Min(ProgressStats.LifetimeDistance, d.target) / 1000.0).ToString("N0", inv) + " / " + (d.target / 1000.0).ToString("N0", inv) + " km";
            case "air_kill_100": return Mathf.Min(ChallengeStore.Counter("air_kills"), (int)d.target) + " / " + (int)d.target;
            case "break_total50": return Mathf.Min(ChallengeStore.Counter("breaks"), (int)d.target) + " / " + (int)d.target;
        }
        return null;
    }

    // ===================================================================== 小物
    static void Head(Rect r, string text, string right)
    {
        LocGUI.Label(new Rect(r.x, r.y, r.width, r.height - 4f), text, UiKit.Label(21f, TextAnchor.MiddleLeft, true, new Color(1f, 0.82f, 0.4f)));
        UiKit.Fill(new Rect(r.x, r.yMax - 3f, r.width, 2f), new Color(1f, 0.8f, 0.4f, 0.4f));
    }

    // 訳さない文字(英字のバッヂ名・コードネーム)を幅に収める(縮めるだけ。折り返さない)
    static GUIStyle fit;
    static GUIStyle FitStyle(string text, float size, float width, TextAnchor anchor, Color color, bool bold = true)
    {
        if (fit == null) fit = new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Overflow, fontStyle = FontStyle.Bold }; // 背の高い字(ミャンマー語など)で上が切れないよう、はみ出しを許す(幅は縮めて収める)
        fit.alignment = anchor; fit.normal.textColor = color; fit.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        fit.fontSize = Mathf.RoundToInt(size);
        float w = fit.CalcSize(new GUIContent(text ?? "")).x;
        if (w > width && w > 1f) fit.fontSize = Mathf.Max(9, Mathf.FloorToInt(size * width / w));
        return fit;
    }
}
