#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

// 開発版だけの DEBUG パネル(2026-10-01)。ホームの「DEBUG」ボタンから開く。リリース版ではこのファイルごと存在しない
// (ボタンも起動経路も無い)。以前ホームの右上/BESTの下に直接並んでいた開発用の操作をここへ集めた。
//  ・無敵 / DEBUGモード(ラン中のSPD/RUN/ASSIST/CARD TEST/DEBUG TOOLS列の表示) / GAMEFEEL検証(演出の誇張) / VIEW(斜め上視点の試作カメラ)
//  ・BESTの距離を設定(ガチャの段階の確認) / ガチャ候補のログ / MILE追加
//  ・スコアリセット(確認あり)
// プレイヤー向けの音/表示/操作は通常の設定画面(SettingsPanel)へ移した。
public partial class DebugPanel : MonoBehaviour
{
    public static DebugPanel Instance { get; private set; }
    bool open;
    float t;
    bool confirmReset;
    int page;            // 0=一般 / 1=セーブ・進行(2026-10-01) / 2=ラスダン終盤(2026-10-02)
    int confirmSave;     // 0=なし / 1=進行だけ初期化 / 2=完全初期化

    public static bool IsOpen => Instance != null && Instance.open;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[DebugPanel]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<DebugPanel>();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => { if (Instance != null) Instance.SetOpen(false); };
    }

    public static void OpenStatic() { if (Instance != null) Instance.SetOpen(true); }
    // ラン中の「DEBUG RUN」表示の ≡ から: ラスダン終盤のページを直接開く
    public static void OpenEndgameStatic() { if (Instance == null) return; Instance.SetOpen(true); if (Instance.open) Instance.page = 2; }
    public static void OpenLongStatic() { if (Instance == null) return; Instance.SetOpen(true); if (Instance.open) Instance.page = 3; }
    public static void OpenUltimateStatic() { if (Instance == null) return; Instance.SetOpen(true); if (Instance.open) Instance.page = 4; }
    public static void CloseStatic() { if (Instance != null) Instance.SetOpen(false); }

    public void SetOpen(bool on)
    {
        if (on && ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning) return;
        if (open && !on) UiInputGate.LatchUntilRelease();
        if (!on) EndMasteryTest("panel closed"); // MASTERY TEST は閉じたら必ず元へ戻す
        open = on; t = 0f; confirmReset = false; confirmSave = 0; confirmTest = 0; if (on) page = 0;
        UiInputGate.DebugPanelOpen = on;
    }

    // Androidの戻る: 確認 → パネルの順に閉じる
    public bool Back()
    {
        if (!open) return false;
        if (confirmReset) confirmReset = false;
        else if (confirmSave != 0) confirmSave = 0;
        else if (confirmTest != 0) confirmTest = 0;
        else if (page != 0) page = 0;
        else SetOpen(false);
        return true;
    }

    void Update() { if (open) t += Time.unscaledDeltaTime; }

    float S => Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 1f, 2.6f);

    void OnGUI()
    {
        if (!open) return;
        var gm = GameManager.Instance;
        if (gm == null) return;
        GUI.depth = -2100;
        float s = S;
        Matrix4x4 keep = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float w = Screen.width / s, h = Screen.height / s;
        float k = Mathf.SmoothStep(0f, 1f, t / 0.15f);
        UiKit.Fill(new Rect(0f, 0f, w, h), new Color(0.05f, 0.02f, 0.02f, 0.5f * k));
        float pw = Mathf.Min(620f, w - 24f), ph = Mathf.Min(page == 0 ? 740f : page == 4 || page == 5 || page == 6 || page == 7 || page == 8 || page == 11 || page == 13 ? 600f : 470f, h - 24f);
        var p = new Rect((w - pw) * 0.5f, (h - ph) * 0.5f + (1f - k) * 12f, pw, ph);
        Color keepColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, k);
        OrnateUi.DrawPanel(p, 0.95f);
        GUI.Label(new Rect(p.x + 24f, p.y + 12f, 300f, 40f), "DEBUG(開発版のみ)", UiKit.Label(24f, TextAnchor.MiddleLeft, true, new Color(1f, 0.55f, 0.45f)));
        if (UiKit.Button(new Rect(p.xMax - 66f, p.y + 12f, 48f, 42f), "×", 24f, false, false)) SetOpen(false);
        if (UiKit.Button(new Rect(p.xMax - 200f, p.y + 12f, 126f, 42f), page == 0 ? "セーブ…" : "← 一般", 17f, page != 0, false)) { page = page == 0 ? 1 : 0; confirmSave = 0; confirmReset = false; }
        if (page == 0 && UiKit.Button(new Rect(p.xMax - 352f, p.y + 12f, 146f, 42f), "ラスダン終盤…", 16f, false, false)) { page = 2; confirmSave = 0; confirmReset = false; }
        if (page == 0 && UiKit.Button(new Rect(p.xMax - 504f, p.y + 12f, 146f, 42f), "画面の記録…", 16f, false, false)) { page = 12; confirmSave = 0; confirmReset = false; } // HUD/安全領域(2026-10-10)
        if (page == 0 && UiKit.Button(new Rect(p.xMax - 24f - 160f, p.y + 62f, 160f, 34f), "広告/課金(模擬)…", 14f, Monetization.Mode == MonetizationMode.Mock, false)) { page = 13; confirmSave = 0; confirmReset = false; } // 2026-10-10(依頼I)
        if (page == 0 && UiKit.Button(new Rect(p.x + 24f, p.y + 62f, p.width - 48f - 168f, 34f), SaveProfile.IsTest ? "テストデータ…(今: TEST DATA)" : "テストデータ…(新規ユーザーの状態で試す)", 15f, SaveProfile.IsTest, false)) { page = 11; confirmTest = 0; testNote = ""; }
        if (page != 0)
        {
            if (page == 1) DrawSavePage(p); else if (page == 3) DrawLongPage(p); else if (page == 4) DrawUltimatePage(p); else if (page == 5) DrawMasteryPage(p); else if (page == 6) DrawCaveBossPage(p); else if (page == 7) DrawFinalEvoPage(p); else if (page == 8) DrawComboPage(p); else if (page == 9) DrawFinishPage(p); else if (page == 10) DrawBossFinishPage(p); else if (page == 11) DrawTestDataPage(p); else if (page == 12) DrawScreenLogPage(p); else if (page == 13) DrawMonetizationPage(p); else DrawEndgamePage(p);
            GUI.color = keepColor;
            GUI.Button(new Rect(0f, 0f, w, h), GUIContent.none, GUIStyle.none); // 背後へ通さない
            if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.MouseDrag) Event.current.Use();
            GUI.matrix = keep;
            return;
        }

        float x = p.x + 24f, y = p.y + 104f, bw = (p.width - 48f - 12f) / 2f, bh = 46f;
        if (UiKit.Button(new Rect(x, y, bw, bh), $"無敵: {(gm.InvincibleMode ? "ON" : "OFF")}", 18f, gm.InvincibleMode, false)) gm.DebugToggleInvincible();
        if (UiKit.Button(new Rect(x + bw + 12f, y, bw, bh), $"DEBUGモード: {(gm.DebugMode ? "ON" : "OFF")}", 18f, gm.DebugMode, false)) gm.DebugToggleDebugMode();
        y += bh + 10f;
        if (UiKit.Button(new Rect(x, y, bw, bh), $"GAMEFEEL検証: {(GameFeelDebug.VisibilityBoost ? "ON" : "OFF")}", 17f, GameFeelDebug.VisibilityBoost, false)) GameFeelDebug.VisibilityBoost = !GameFeelDebug.VisibilityBoost;
        var view = FindFirstObjectByType<ViewModeToggle>();
        if (view != null && UiKit.Button(new Rect(x + bw + 12f, y, bw, bh), $"VIEW: {(view.PortraitActive ? "斜め上(試作)" : "通常")}", 17f, view.PortraitActive, false)) view.Toggle();
        y += bh + 6f;
        GUI.Label(new Rect(x, y, p.width - 48f, 24f), "GAMEFEEL検証=演出を誇張して確認する開発用(プレイヤー向けの強さは設定の「発光演出」)", UiKit.Label(13f, TextAnchor.UpperLeft, false, new Color(0.8f, 0.8f, 0.85f)));
        y += 28f;

        // 中断セーブからの再開直後の慣らし(2026-10-03、既定OFF)
        bool ease = gm.ResumeEaseSetting;
        if (UiKit.Button(new Rect(x, y, bw, 40f), $"再開の慣らし: {(ease ? "ON" : "OFF")}", 17f, ease, false))
        {
            PlayerPrefs.SetInt(GameManager.ResumeEaseDevKey, ease ? 0 : 1);
            PlayerPrefs.Save();
        }
        // 攻撃判定の可視化(2026-10-03): 赤=攻撃 / 緑=被弾 / 黄=敵の体 / 紫=敵の攻撃 / 水色=ボスの被弾範囲
        if (UiKit.Button(new Rect(x + bw + 12f, y, bw, 40f), $"判定表示: {(HitboxOverlay.Enabled ? "ON" : "OFF")}", 17f, HitboxOverlay.Enabled, false)) HitboxOverlay.Enabled = !HitboxOverlay.Enabled;
        y += 46f;
        // 長距離の確認(2026-10-04): 10〜100km の雑魚の硬さを実機で見る(DEBUG RUN)
        float tw3 = (p.width - 48f - 16f) / 3f;
        if (UiKit.Button(new Rect(x, y, tw3, 40f), "長距離の確認…", 15f, false, false)) { page = 3; confirmSave = 0; confirmReset = false; }
        // #100 ULTIMATE の確認(2026-10-04、DEBUG RUN)
        if (UiKit.Button(new Rect(x + tw3 + 8f, y, tw3, 40f), "ULTIMATE TEST…", 15f, false, false)) { page = 4; confirmSave = 0; confirmReset = false; }
        // カード長期育成の確認(2026-10-04、保存しない)
        if (UiKit.Button(new Rect(x + 2f * (tw3 + 8f), y, tw3, 40f), "MASTERY TEST…", 15f, false, false)) { page = 5; confirmSave = 0; confirmReset = false; }
        y += 46f;
        // 開発用の闘技場(2026-10-04): キャラ/カード/敵を好きな条件で戦わせ、同じ条件ですぐ再戦・計測(DEBUG RUN)
        if (UiKit.Button(new Rect(x, y, p.width - 48f, 40f), "闘技場(キャラ/カード/敵の試験、DEBUG RUN)", 16f, true, false)) { SetOpen(false); EndgameDebug.LaunchArena("debug panel"); }
        y += 46f;
        // 自然洞窟ボス強化の確認(2026-10-04、DEBUG RUN): 出現/段階/必殺技/BREAK/ラン再開の強制
        if (UiKit.Button(new Rect(x, y, p.width - 48f, 40f), "洞窟/天空ボス試験…(出現/段階/必殺技/BREAK/ラン再開)", 16f, false, false)) { page = 6; confirmSave = 0; confirmReset = false; }
        y += 46f;
        // FINAL EVOLUTION の確認(2026-10-04、DEBUG RUN)
        float tw2 = (p.width - 48f - 8f) / 2f;
        if (UiKit.Button(new Rect(x, y, tw2, 40f), "FINAL EVOLUTION TEST…", 16f, false, false)) { page = 7; confirmSave = 0; confirmReset = false; }
        if (UiKit.Button(new Rect(x + tw2 + 8f, y, tw2, 40f), "COMBO TEST…", 16f, false, false)) { page = 8; confirmSave = 0; confirmReset = false; }
        y += 46f;
        // Enemy FINISH System(2026-10-06): 撃破演出の確認(ランの中で)
        float fw2 = (p.width - 48f - 8f) / 2f;
        if (UiKit.Button(new Rect(x, y, fw2, 40f), "FINISH TEST…(雑魚の撃破演出)", 15f, false, false)) { page = 9; confirmSave = 0; confirmReset = false; }
        if (UiKit.Button(new Rect(x + fw2 + 8f, y, fw2, 40f), "BOSS FINISH TEST…", 15f, false, false)) { page = 10; confirmSave = 0; confirmReset = false; }
        y += 46f;
        // 疾走出発(2026-10-05 試作): 門番の撃破記録が無くても全部の行き先を選べる(ステージ選択の「疾走出発…」)
        if (UiKit.Button(new Rect(x, y, p.width - 48f, 40f), SprintRecords.DevUnlockAll ? "疾走出発の行き先: 全解放 ON(押すと記録どおりに戻す)" : "疾走出発の行き先: 記録どおり(押すと全解放)", 16f, SprintRecords.DevUnlockAll, false)) SprintRecords.DevUnlockAll = !SprintRecords.DevUnlockAll;
        y += 46f;

        GUI.Label(new Rect(x, y, 300f, 26f), "BESTを設定(ガチャの段階の確認)", UiKit.Label(16f, TextAnchor.MiddleLeft, true, new Color(1f, 0.85f, 0.5f)));
        y += 28f;
        float[] stops = { 0f, 5000f, 20000f, 50000f, 100000f };
        float sw = (p.width - 48f - 4f * 8f) / 5f;
        for (int i = 0; i < stops.Length; i++)
            if (UiKit.Button(new Rect(x + i * (sw + 8f), y, sw, 40f), stops[i] >= 1000f ? $"{stops[i] / 1000f:0}K" : "0", 16f, false, false)) gm.DebugSetBestDistance(stops[i]);
        y += 48f;
        if (UiKit.Button(new Rect(x, y, bw, bh), "ガチャ候補をログへ", 17f, false, false)) gm.DebugLogGachaPoolPublic();
        if (UiKit.Button(new Rect(x + bw + 12f, y, bw, bh), "MILE +5000", 17f, false, false)) gm.AddMile(5000);
        y += bh + 14f;

        if (!confirmReset)
        {
            if (UiKit.Button(new Rect(x, y, p.width - 48f, bh), "スコアをリセット…", 18f, false, false)) confirmReset = true;
        }
        else
        {
            // データを変える操作は確認してから
            GUI.Label(new Rect(x, y - 2f, p.width - 48f, 24f), "BEST記録(全ステージ)を消します。よろしいですか?", UiKit.Label(16f, TextAnchor.UpperLeft, true, new Color(1f, 0.6f, 0.5f)));
            if (UiKit.Button(new Rect(x, y + 24f, bw, 44f), "リセットする", 18f, true, false)) { gm.DebugResetHighScores(); confirmReset = false; }
            if (UiKit.Button(new Rect(x + bw + 12f, y + 24f, bw, 44f), "やめる", 18f, false, false)) confirmReset = false;
        }

        GUI.color = keepColor;
        GUI.Button(new Rect(0f, 0f, w, h), GUIContent.none, GUIStyle.none); // 背後へ通さない
        if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.MouseDrag) Event.current.Use();
        GUI.matrix = keep;
    }
}

// DebugPanel: セーブ/進行のページ(2026-10-01)
public partial class DebugPanel
{
    string cardNote = "";
    // 画面の記録(2026-10-10): HUD の位置/安全領域が変わった時の記録(直近20件)。実機で HUD がずれた時にここを撮ってもらう
    void DrawScreenLogPage(Rect p)
    {
        float x = p.x + 24f, y = p.y + 64f, full = p.width - 48f;
        var lab = new GUIStyle(UiKit.Label(11f, TextAnchor.UpperLeft, false, new Color(0.9f, 0.92f, 1f))) { wordWrap = true };
        GUI.Label(new Rect(x, y, full, 36f), $"今: 画面 {Screen.width}x{Screen.height} {Screen.orientation}  安全領域(端末) {Screen.safeArea}  使っている値 {StableSafeArea.Rect}  切り欠き {Screen.cutouts.Length}  変化 {StableSafeArea.Changes}回", lab);
        y += 40f;
        for (int i = StableSafeArea.Recent.Count - 1; i >= 0 && y < p.yMax - 24f; i--)
        {
            float h = lab.CalcHeight(new GUIContent(StableSafeArea.Recent[i]), full);
            GUI.Label(new Rect(x, y, full, h), StableSafeArea.Recent[i], lab);
            y += h + 2f;
        }
    }

    void DrawSavePage(Rect p)
    {
        float x = p.x + 24f, y = p.y + 62f, full = p.width - 48f, bh = 44f;
        var boot = SaveSystem.LastBoot;
        string info = $"{(SaveProfile.IsTest ? "[TEST DATA] " : "")}セーブ形式 v{SaveStore.GetInt(SaveKeys.SchemaVersion, 0)} / リリース世代 {SaveStore.GetInt(SaveKeys.ReleaseGeneration, 0)}(このビルド {SaveSystem.BuildReleaseGeneration})"
            + (boot != null ? $" / 起動時: {boot.kind}" : "");
        GUI.Label(new Rect(x, y, full, 22f), info, UiKit.Label(14f, TextAnchor.MiddleLeft, false, new Color(0.8f, 0.85f, 0.95f)));
        y += 26f;

        // 累計走行距離
        GUI.Label(new Rect(x, y, full, 24f), $"累計走行距離 {ProgressStats.LifetimeDistance:N0} m", UiKit.Label(16f, TextAnchor.MiddleLeft, true, new Color(1f, 0.85f, 0.5f)));
        y += 26f;
        double[] stops = { 0, 999999, 1000000 };
        string[] names = { "0", "999,999", "1,000,000" };
        float sw = (full - 16f) / 3f;
        for (int i = 0; i < 3; i++)
            if (UiKit.Button(new Rect(x + i * (sw + 8f), y, sw, 40f), names[i], 16f, false, false)) ProgressStats.DevSetLifetime(stops[i]);
        y += 48f;

        // 三姉妹の遭遇
        string[] jp = { "長女", "次女", "三女" };
        for (int i = 0; i < 3; i++)
        {
            var sister = ProgressStats.Sisters[i];
            bool met = ProgressStats.HasMet(sister);
            if (UiKit.Button(new Rect(x + i * (sw + 8f), y, sw, 40f), $"{jp[i]}: {(met ? "遭遇済" : "未遭遇")}", 15f, met, false)) ProgressStats.DevSetMet(sister, !met);
        }
        y += 48f;

        // ラスダン
        float bw = (full - 8f) / 2f;
        bool unlocked = ProgressStats.FinalDungeonUnlocked;
        if (UiKit.Button(new Rect(x, y, bw, bh), $"ラスダン解放: {(unlocked ? "ON" : "OFF")}", 16f, unlocked, false)) ProgressStats.DevSetFinalDungeonUnlocked(!unlocked);
        bool always = ProgressStats.DevAlwaysOpen;
        if (UiKit.Button(new Rect(x + bw + 8f, y, bw, bh), $"ラスダン常に選択: {(always ? "ON" : "OFF")}", 15f, always, false)) ProgressStats.DevAlwaysOpen = !always;
        y += bh + 4f;
        GUI.Label(new Rect(x, y, full, 20f), "解放の条件: 累計1,000,000m + 三姉妹全員と遭遇(一度解放したら戻らない)。「常に選択」は開発版だけ", UiKit.Label(12f, TextAnchor.MiddleLeft, false, new Color(0.75f, 0.75f, 0.8f)));
        y += 26f;

        // カード全開放(2026-10-02)
        float tw = (full - 16f) / 3f;
        bool open = GachaStage.DevAllCardsOpen;
        if (UiKit.Button(new Rect(x, y, tw, bh), $"全カード開放: {(open ? "ON" : "OFF")}", 15f, open, false))
        {
            GachaStage.DevAllCardsOpen = !open;
            if (!open) { int n = CardInventory.DebugOwnEveryMissing(); cardNote = $"全{CardDatabase.AllCards.Count}枚を解放、未所持{n}枚をLv1で追加"; }
            else cardNote = "全カード開放OFF(所持したカードはそのまま)";
        }
        if (UiKit.Button(new Rect(x + tw + 8f, y, tw, bh), "全カード Lv9×3枚", 15f, false, false))
        {
            int n = CardInventory.DebugOwnEveryAtLevel(9, 3);
            cardNote = $"全カードをLv9で3枚ずつ所持({n}種を追加)";
        }
        if (UiKit.Button(new Rect(x + (tw + 8f) * 2f, y, tw, bh), "全カード Lv1×1枚", 15f, false, false))
        {
            int n = CardInventory.DebugOwnEveryMissing();
            cardNote = $"未所持{n}枚をLv1で追加";
        }
        y += bh + 2f;
        GUI.Label(new Rect(x, y, full, 18f), string.IsNullOrEmpty(cardNote) ? "全カード開放=距離/ガチャ段階に関係なく全カードを解放済みにして、未所持のカードを1枚ずつ渡す(開発版のみ)" : cardNote,
            UiKit.Label(12f, TextAnchor.MiddleLeft, false, new Color(1f, 0.85f, 0.5f)));
        y += 22f;

        // 初期化(確認あり)
        if (confirmSave == 0)
        {
            if (UiKit.Button(new Rect(x, y, bw, bh), "進行だけ初期化…", 16f, false, false)) confirmSave = 1;
            if (UiKit.Button(new Rect(x + bw + 8f, y, bw, bh), "完全初期化…", 16f, false, false)) confirmSave = 2;
        }
        else
        {
            string msg = confirmSave == 1 ? "キャラ/カード/MILE/BEST/累計距離/遭遇/解放を消します(設定は残る)。よろしいですか?" : "設定も含めて全部消します(新規インストールと同じ)。よろしいですか?";
            GUI.Label(new Rect(x, y - 4f, full, 22f), msg, UiKit.Label(13f, TextAnchor.MiddleLeft, true, new Color(1f, 0.6f, 0.5f)));
            if (UiKit.Button(new Rect(x, y + 18f, bw, 40f), "初期化する", 17f, true, false))
            {
                if (confirmSave == 1) SaveSystem.DevResetProgress(); else SaveSystem.DevResetAll();
                confirmSave = 0;
                SetOpen(false);
                // シーン上の読み込み済みの値(GameManager等)を読み直す
                UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
            }
            if (UiKit.Button(new Rect(x + bw + 8f, y + 18f, bw, 40f), "やめる", 17f, false, false)) confirmSave = 0;
        }
    }
}

// DebugPanel: ラスダン終盤/エンディングのワープ(2026-10-02)。押すたびにシーンを読み直して、記録されない DEBUG RUN として始める。
public partial class DebugPanel
{
    void DrawEndgamePage(Rect p)
    {
        float x = p.x + 24f, y = p.y + 60f, full = p.width - 48f;
        var gm = GameManager.Instance;
        GUI.Label(new Rect(x, y, full, 36f), "押すたびにラスダンを新しく始めて、その地点へ移動します(DEBUG RUN)。BEST / MILE / カード / 累計距離 / 三姉妹の遭遇 / ラスダン解放 / CONTINUE は変わりません",
            UiKit.Label(12f, TextAnchor.UpperLeft, false, new Color(0.85f, 0.85f, 0.9f)));
        y += 38f;
        // テスト用の性能
        float tw = (full - 16f) / 3f;
        var profs = new[] { EndgameDebug.Profile.Normal, EndgameDebug.Profile.Sturdy, EndgameDebug.Profile.SturdyStrong };
        for (int i = 0; i < 3; i++)
        {
            bool on = EndgameDebug.SelectedProfile == profs[i];
            if (UiKit.Button(new Rect(x + i * (tw + 8f), y, tw, 36f), EndgameDebug.ProfileLabel(profs[i]), 14f, on, false)) EndgameDebug.SelectedProfile = profs[i];
        }
        y += 42f;
        var points = new[]
        {
            (EndgameDebug.Point.LastDungeon0, "開始地点"), (EndgameDebug.Point.LastDungeon90, "ボスラッシュの直前"),
            (EndgameDebug.Point.LastDungeon99, "静寂区間の直前"), (EndgameDebug.Point.ReaperSisters, "三姉妹戦の直前"),
            (EndgameDebug.Point.EndingCredits, "三姉妹撃破後から"), (EndgameDebug.Point.OneMoreMile, "YES / NO から"),
            (EndgameDebug.Point.EndingFlow, "99km→三姉妹→エンドロール→YES/NO"),
        };
        float bw = (full - 8f) / 2f, bh = 42f;
        bool busy = EndgameDebug.Instance != null && EndgameDebug.Instance.Launching;
        for (int i = 0; i < points.Length; i++)
        {
            var r = new Rect(x + (i % 2) * (bw + 8f), y + (i / 2) * (bh + 6f), bw, bh);
            string label = EndgameDebug.Label(points[i].Item1) + "\n" + points[i].Item2;
            if (UiKit.Button(r, label, 13f, false, false) && !busy) EndgameDebug.Launch(points[i].Item1, EndgameDebug.SelectedProfile);
        }
        // NEXT BOSS(ボスラッシュ中)
        {
            var r = new Rect(x + bw + 8f, y + 3 * (bh + 6f), bw, bh);
            bool inRush = gm != null && gm.HasStarted && !gm.IsGameOver && LastDungeonFlow.Instance != null && LastDungeonFlow.Instance.Current == LastDungeonFlow.State.Rush;
            if (UiKit.Button(r, "NEXT BOSS\nボスラッシュ中: 今のボスを倒して次へ", 13f, false, false))
            {
                if (inRush) { EndgameDebug.NextBoss(); SetOpen(false); }
                else if (EndgameDebug.Instance != null) EndgameDebug.Instance.SetStatus("NEXT BOSS はボスラッシュ中だけ使えます");
            }
        }
        y += 4 * (bh + 6f) + 4f;
        string st = EndgameDebug.Instance != null ? EndgameDebug.Instance.Status : "";
        string run = DebugRun.IsActive ? $"DEBUG RUN 中: {DebugRun.What}(保存を止めた回数 {DebugRun.BlockedWrites})" : "通常の状態(DEBUG RUN ではありません)";
        string last = DebugRun.LastRestoredKeys < 0 ? "" : DebugRun.LastRestoredKeys == 0 ? " / 前回の DEBUG RUN: 進行の変化なし" : $" / 前回の DEBUG RUN: {DebugRun.LastRestoredKeys}項目を元へ戻した";
        GUI.Label(new Rect(x, y, full, 20f), run + last, UiKit.Label(12f, TextAnchor.MiddleLeft, true, DebugRun.IsActive ? new Color(1f, 0.7f, 0.4f) : new Color(0.7f, 0.9f, 0.7f)));
        y += 20f;
        if (!string.IsNullOrEmpty(st)) GUI.Label(new Rect(x, y, full, 20f), st, UiKit.Label(12f, TextAnchor.MiddleLeft, false, new Color(1f, 0.85f, 0.5f)));
    }
}

// DebugPanel: 長距離の確認のページ(2026-10-04)
public partial class DebugPanel
{
    void DrawLongPage(Rect p)
    {
        float x = p.x + 24f, y = p.y + 60f, full = p.width - 48f;
        GUI.Label(new Rect(x, y, full, 36f), "押すたびにステージを新しく始めて、その距離の関門の直後へ移動します(DEBUG RUN)。BEST / MILE / カード / 累計距離 / 解放 / CONTINUE は変わりません",
            UiKit.Label(12f, TextAnchor.UpperLeft, false, new Color(0.85f, 0.85f, 0.9f)));
        y += 38f;
        var stages = new[] { ("wasteland_road", "荒野街道"), ("natural_cave", "自然洞窟"), ("sky_corridor", "天空回廊") };
        float sw = (full - 16f) / 3f;
        for (int i = 0; i < stages.Length; i++)
            if (UiKit.Button(new Rect(x + i * (sw + 8f), y, sw, 34f), stages[i].Item2, 14f, EndgameDebug.SelectedLongStage == stages[i].Item1, false)) EndgameDebug.SelectedLongStage = stages[i].Item1;
        y += 40f;
        var builds = new[] { EndgameDebug.LongBuild.None, EndgameDebug.LongBuild.Mix, EndgameDebug.LongBuild.Attack, EndgameDebug.LongBuild.Fire, EndgameDebug.LongBuild.Lightning };
        float bw5 = (full - 32f) / 5f;
        for (int i = 0; i < builds.Length; i++)
            if (UiKit.Button(new Rect(x + i * (bw5 + 8f), y, bw5, 34f), EndgameDebug.LongBuildLabel(builds[i]), 12f, EndgameDebug.SelectedLongBuild == builds[i], false)) EndgameDebug.SelectedLongBuild = builds[i];
        y += 40f;
        var profs = new[] { EndgameDebug.Profile.Normal, EndgameDebug.Profile.Sturdy };
        float pw2 = (full - 8f) / 2f;
        for (int i = 0; i < profs.Length; i++)
            if (UiKit.Button(new Rect(x + i * (pw2 + 8f), y, pw2, 34f), EndgameDebug.ProfileLabel(profs[i]), 14f, EndgameDebug.SelectedProfile == profs[i], false)) EndgameDebug.SelectedProfile = profs[i];
        y += 44f;
        bool busy = EndgameDebug.Instance != null && EndgameDebug.Instance.Launching;
        var ds = EndgameDebug.LongDistances;
        float dw = (full - 8f * (ds.Length - 1)) / ds.Length;
        var dtm = DistanceTierManager.Instance;
        for (int i = 0; i < ds.Length; i++)
        {
            int baseBonus = dtm != null ? dtm.baseHpBonus : 4;
            float step = dtm != null ? Mathf.Max(1f, dtm.hpIncreaseDistance) : 2000f;
            int hp = (1 + baseBonus + Mathf.FloorToInt((ds[i] + 150f) / step)) * CombatScale.K;
            if (UiKit.Button(new Rect(x + i * (dw + 8f), y, dw, 56f), $"{EndgameDebug.LongDistanceLabel(ds[i])}\n雑魚HP約{hp}", 14f, false, false) && !busy)
                EndgameDebug.LaunchLong(EndgameDebug.SelectedLongStage, ds[i], EndgameDebug.SelectedLongBuild, EndgameDebug.SelectedProfile == EndgameDebug.Profile.Normal ? EndgameDebug.Profile.Normal : EndgameDebug.Profile.Sturdy);
        }
        y += 64f;
        GUI.Label(new Rect(x, y, full, 36f), "カードは Lv9 で付ける(CARD BALANCE TEST と同じ構成)。雑魚HPは「HP倍率1の雑魚」の目安。カードの能力値は DEBUGモード ON → ラン中の CARD TEST の「ビルド」タブで見られる",
            UiKit.Label(12f, TextAnchor.UpperLeft, false, new Color(0.8f, 0.8f, 0.85f)));
        y += 38f;
        string st = EndgameDebug.Instance != null ? EndgameDebug.Instance.Status : "";
        string run = DebugRun.IsActive ? $"DEBUG RUN 中: {DebugRun.What}(保存を止めた回数 {DebugRun.BlockedWrites})" : "通常の状態(DEBUG RUN ではありません)";
        GUI.Label(new Rect(x, y, full, 20f), run, UiKit.Label(12f, TextAnchor.MiddleLeft, true, DebugRun.IsActive ? new Color(1f, 0.7f, 0.4f) : new Color(0.7f, 0.9f, 0.7f)));
        y += 20f;
        if (!string.IsNullOrEmpty(st)) GUI.Label(new Rect(x, y, full, 20f), st, UiKit.Label(12f, TextAnchor.MiddleLeft, false, new Color(1f, 0.85f, 0.5f)));
    }
}

// DebugPanel: ULTIMATE TEST のページ(#100 ULTIMATE、2026-10-04)
public partial class DebugPanel
{
    void DrawUltimatePage(Rect p)
    {
        float x = p.x + 24f, y = p.y + 60f, full = p.width - 48f;
        GUI.Label(new Rect(x, y, full, 36f), "押すたびにシーンを読み直して、ULTIMATE だけを付けたランを始めます(Gauge 100%、DEBUG RUN = BEST / MILE / 解放 / CONTINUE / コレクションは変わりません)",
            UiKit.Label(12f, TextAnchor.UpperLeft, false, new Color(0.85f, 0.85f, 0.9f)));
        y += 38f;
        var chars = EndgameDebug.UltCharacters;
        float cw = (full - 3f * 6f) / 4f;
        for (int i = 0; i < chars.Length; i++)
        {
            int col = i % 4, row = i / 4;
            if (UiKit.Button(new Rect(x + col * (cw + 6f), y + row * 38f, cw, 34f), EndgameDebug.UltCharLabel(chars[i]), 12f, EndgameDebug.SelectedUltChar == chars[i], false)) EndgameDebug.SelectedUltChar = chars[i];
        }
        y += 3 * 38f + 4f;
        int[] lvs = { 1, 5, 9 };
        var stages = new[] { "wasteland_road", "natural_cave", "sky_corridor" };
        float sw = (full - 5f * 6f) / 6f;
        for (int i = 0; i < 3; i++)
            if (UiKit.Button(new Rect(x + i * (sw + 6f), y, sw, 34f), $"Lv{lvs[i]}", 14f, EndgameDebug.SelectedUltLevel == lvs[i], false)) EndgameDebug.SelectedUltLevel = lvs[i];
        for (int i = 0; i < 3; i++)
            if (UiKit.Button(new Rect(x + (3 + i) * (sw + 6f), y, sw, 34f), EndgameDebug.StageLabel(stages[i]), 12f, EndgameDebug.SelectedUltStage == stages[i], false)) EndgameDebug.SelectedUltStage = stages[i];
        y += 42f;
        bool busy = EndgameDebug.Instance != null && EndgameDebug.Instance.Launching;
        float hw = (full - 8f) / 2f;
        if (UiKit.Button(new Rect(x, y, hw, 44f), "通常の道で開始", 16f, false, false) && !busy) EndgameDebug.LaunchUltimate(EndgameDebug.SelectedUltChar, EndgameDebug.SelectedUltLevel, EndgameDebug.SelectedUltStage, false);
        if (UiKit.Button(new Rect(x + hw + 8f, y, hw, 44f), "ボス戦で開始", 16f, false, false) && !busy) EndgameDebug.LaunchUltimate(EndgameDebug.SelectedUltChar, EndgameDebug.SelectedUltLevel, EndgameDebug.SelectedUltStage, true);
        y += 52f;
        var gm = GameManager.Instance; var ua = UltimateArt.Instance;
        bool running = gm != null && gm.HasStarted && !gm.IsGameOver && ua != null;
        float tw = (full - 16f) / 3f;
        if (UiKit.Button(new Rect(x, y, tw, 40f), "Gauge 100%", 15f, false, false) && running) ua.DebugSetGauge(100f);
        if (UiKit.Button(new Rect(x + tw + 8f, y, tw, 40f), "発動", 15f, running && ua.Ready, false) && running) { SetOpen(false); ua.TryActivate("debug panel"); }
        if (UiKit.Button(new Rect(x + 2f * (tw + 8f), y, tw, 40f), "BUFFを終える", 15f, false, false) && running) ua.DebugEndBuff();
        y += 48f;
        var lab = UiKit.Label(12f, TextAnchor.MiddleLeft, false, new Color(1f, 0.88f, 0.6f));
        if (running)
        {
            ua.CanActivate(out string why);
            GUI.Label(new Rect(x, y, full, 18f), $"ULTIMATE Lv{UltimateArt.Level}  Gauge {ua.Gauge:F0}%  {(ua.Active ? "発動中 " + ua.Phase : ua.BuffActive ? $"BUFF 残り {ua.BuffRemaining:F1}s(攻撃 x{UltimateArt.BuffAttackMul:F2} / 速さ x{UltimateArt.BuffRunSpeedMul:F2})" : "")}  {(string.IsNullOrEmpty(why) ? "発動できます" : why)}", lab);
            y += 18f;
            int ulv = Mathf.Max(1, UltimateArt.Level);
            GUI.Label(new Rect(x, y, full, 18f), $"Gauge の内訳: 距離 {ua.GaugeFromDistance:F0} / 撃破 {ua.GaugeFromKills:F0} / ボス {ua.GaugeFromBoss:F0}  突破 {UltimateTuning.At(UltimateTuning.I.advance, ulv):F0}m  殲滅: 範囲内の通常の敵を一撃  ボス 最大HPの{UltimateTuning.At(UltimateTuning.I.bossDamageFraction, ulv) * 100f:F0}%", lab);
            y += 18f;
        }
        var r = ua != null ? ua.Last : null;
        if (r != null)
        {
            GUI.Label(new Rect(x, y, full, 18f), $"前回: {r.character} Lv{r.level} {(r.arena ? "ボス戦(アリーナ)" : $"突破 {r.d1 - r.d0:F0}m(予定 {r.plannedAdvance:F0}{(string.IsNullOrEmpty(r.limitReason) ? "" : " / 手前で停止: " + r.limitReason)})")} {r.seconds:F1}秒", lab);
            y += 18f;
            GUI.Label(new Rect(x, y, full, 18f), $"殲滅 {r.annihilated}体(報酬あり) 突破で消した {r.passCleared}体(報酬なし) 対象外 {r.eventSkipped} ボス {r.bossDamage}({r.bossFractionMax * 100f:F0}%) BUFF {r.buffSeconds:F0}秒{(r.aborted ? " 途中で終了: " + r.abortReason : "")}", lab);
            y += 18f;
        }
        string st = EndgameDebug.Instance != null ? EndgameDebug.Instance.Status : "";
        if (!string.IsNullOrEmpty(st)) GUI.Label(new Rect(x, y, full, 18f), st, UiKit.Label(12f, TextAnchor.MiddleLeft, false, new Color(1f, 0.85f, 0.5f)));
    }
}

// DebugPanel: MASTERY TEST のページ(カード長期育成、2026-10-04)。テストの間は DEBUG RUN(保存を止める)。
// 終わる(または DEBUG パネルを閉じる)と、始める前の所持カード/Mastery へ戻す。実際のセーブは汚さない。
public partial class DebugPanel
{
    static int masteryCardIndex;
    static string masteryNote = "";

    void DrawMasteryPage(Rect p)
    {
        float x = p.x + 24f, y = p.y + 60f, full = p.width - 48f;
        bool on = MasteryTestRunning;
        GUI.Label(new Rect(x, y, full, 36f), "所持Lv(1〜9)とは別の Mastery ★1〜5 / AWAKENED を短時間で確かめます。テストの間は保存しません(DEBUG RUN)。終わると元の所持カードと Mastery へ戻ります",
            UiKit.Label(12f, TextAnchor.UpperLeft, false, new Color(0.85f, 0.85f, 0.9f)));
        y += 38f;
        float hw = (full - 8f) / 2f;
        if (UiKit.Button(new Rect(x, y, hw, 40f), on ? "テスト中(保存しない)" : "テストを始める", 15f, on, false) && !on) StartMasteryTest();
        if (UiKit.Button(new Rect(x + hw + 8f, y, hw, 40f), "テストを終える(元に戻す)", 15f, false, false) && on) EndMasteryTest("button");
        y += 48f;
        var cards = new System.Collections.Generic.List<CardDefinition>();
        foreach (var c in CardDatabase.AllCards) if (c != null && !CardVariant.IsVariantKey(c.cardId)) cards.Add(c);
        if (cards.Count == 0) return;
        masteryCardIndex = (masteryCardIndex % cards.Count + cards.Count) % cards.Count;
        var card = cards[masteryCardIndex];
        if (UiKit.Button(new Rect(x, y, 60f, 36f), "◀", 18f, false, false)) masteryCardIndex--;
        GUI.Label(new Rect(x + 66f, y, full - 132f, 36f), $"{card.cardName}  ({card.cardId})", UiKit.Label(16f, TextAnchor.MiddleCenter, true, new Color(1f, 0.88f, 0.55f)));
        if (UiKit.Button(new Rect(x + full - 60f, y, 60f, 36f), "▶", 18f, false, false)) masteryCardIndex++;
        y += 44f;
        string id = card.cardId;
        string k8 = CardDataMigration.LegacyToKey(id, 8), k9 = CardDataMigration.LegacyToKey(id, 9), k5 = CardDataMigration.LegacyToKey(id, 5);
        float tw = (full - 16f) / 3f;
        GUI.enabled = on;
        if (UiKit.Button(new Rect(x, y, tw, 38f), "Lv8×1 + Lv1×3 を付与", 13f, false, false)) { CardInventory.AddCard(k8, 8, 1); CardInventory.AddCard(id, 1, 3); masteryNote = "Lv8 を1枚、Lv1 を3枚 付与"; }
        if (UiKit.Button(new Rect(x + tw + 8f, y, tw, 38f), "合成 Lv8 + Lv1 → Lv9", 13f, false, false)) masteryNote = MasteryFuse(k8, id);
        if (UiKit.Button(new Rect(x + 2f * (tw + 8f), y, tw, 38f), "合成 Lv9 + Lv1(+1)", 13f, false, false)) masteryNote = MasteryFuse(k9, id);
        y += 44f;
        if (UiKit.Button(new Rect(x, y, tw, 38f), "Lv5×2 → 合成(余り繰越)", 13f, false, false)) { CardInventory.AddCard(k5, 5, 2); masteryNote = MasteryFuse(k5, k5); }
        if (UiKit.Button(new Rect(x + tw + 8f, y, tw, 38f), "Lv9 + Lv9(+9)", 13f, false, false)) { CardInventory.AddCard(k9, 9, 2); masteryNote = MasteryFuse(k9, k9); }
        if (UiKit.Button(new Rect(x + 2f * (tw + 8f), y, tw, 38f), "★を0へ", 13f, false, false)) { CardMastery.DebugResetCard(id); masteryNote = "★を0へ戻しました(Lv9 到達の記録は所持から作り直し)"; }
        y += 44f;
        float qw = (full - 24f) / 4f;
        int[] adds = { 1, 3, 5, 15 };
        for (int i = 0; i < adds.Length; i++)
            if (UiKit.Button(new Rect(x + i * (qw + 8f), y, qw, 38f), $"Mastery +{adds[i]}", 13f, false, false))
            {
                var g = CardMastery.AddProgress(id, adds[i], "debug");
                masteryNote = $"+{adds[i]}: ★{g.levelBefore} {g.progressBefore} → ★{g.levelAfter} {g.progressAfter}{(g.awakenedNow ? "  AWAKENED!" : "")}{(g.overflowAdded > 0 ? $"  保管+{g.overflowAdded}" : "")}";
            }
        y += 44f;
        if (UiKit.Button(new Rect(x, y, hw, 38f), "Save → Load の一致を確認", 13f, false, false))
            masteryNote = CardMastery.RoundTripEquals(out string det) ? $"一致({det})" : $"不一致: {det}";
        GUI.enabled = true;
        y += 46f;
        var lab = UiKit.Label(13f, TextAnchor.MiddleLeft, false, new Color(1f, 0.9f, 0.65f));
        var sb = new System.Text.StringBuilder();
        foreach (var s in CardInventory.Stacks) if (s.count > 0 && CardMastery.BaseIdOf(s.cardId) == id) sb.Append($"Lv{s.level}×{s.count}  ");
        GUI.Label(new Rect(x, y, full, 20f), "所持: " + (sb.Length > 0 ? sb.ToString() : "なし"), lab); y += 20f;
        GUI.Label(new Rect(x, y, full, 20f), $"Lv9 MAX: {(CardMastery.IsMaxReached(id) ? "到達" : "未到達")}   Mastery ★{CardMastery.MasteryLevel(id)}  {CardMastery.MasteryProgress(id)} / {CardMastery.NeedForNext(id)}   {(CardMastery.IsAwakened(id) ? "AWAKENED" : "")}   ★5後の保管 {CardMastery.Overflow(id)}", lab); y += 20f;
        GUI.Label(new Rect(x, y, full, 20f), $"全体: MAX {CardMastery.MaxCount} / {CardMastery.TotalCards}   AWAKENED {CardMastery.AwakenedCount} / {CardMastery.TotalCards}   必要量 {string.Join("/", MasteryTuning.I.need)}(合計 {MasteryTuning.TotalToAwaken})", lab); y += 20f;
        if (!string.IsNullOrEmpty(masteryNote)) GUI.Label(new Rect(x, y, full, 20f), masteryNote, UiKit.Label(13f, TextAnchor.MiddleLeft, true, new Color(0.7f, 1f, 0.8f)));
    }

    static string MasteryFuse(string main, string material)
    {
        var r = CardFusionLogic.Execute(main, material, out string err);
        if (r == null) return "合成できません: " + err;
        return r.kind == CardFusionLogic.Kind.Mastery || r.masteryGain > 0
            ? $"{r.kind}: Lv{r.result.level}  Mastery +{r.masteryGain} → ★{r.mastery.levelAfter} {r.mastery.progressAfter}{(r.mastery.awakenedNow ? " AWAKENED!" : "")}"
            : $"{r.kind}: Lv{r.result?.level}";
    }

    public static bool MasteryTestRunning { get; private set; }
    public static void StartMasteryTest()
    {
        if (MasteryTestRunning || DebugRun.IsActive) { masteryNote = DebugRun.IsActive ? "他の DEBUG RUN 中は使えません" : masteryNote; return; }
        DebugRun.Begin("MASTERY TEST");
        MasteryTestRunning = true;
        masteryNote = "テスト開始(保存しない)";
    }
    public static void EndMasteryTest(string why)
    {
        if (!MasteryTestRunning) return;
        MasteryTestRunning = false;
        int n = DebugRun.End("mastery test " + why);
        CardInventory.ReloadFromPrefs();
        CardMastery.ReloadFromPrefs();
        masteryNote = $"テスト終了: 元に戻しました(戻したキー {n})";
    }
}

// DebugPanel から使う GameManager の開発用操作の入口(開発版のみ)
public partial class GameManager
{
    public void DebugToggleInvincible() => ToggleInvincible();
    public void DebugToggleDebugMode() => ToggleDebugMode();
    public void DebugResetHighScores() => ResetHighScores();
    public void DebugLogGachaPoolPublic() => DebugLogGachaPool();
}
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
// DebugPanel: 洞窟ボス試験のページ(自然洞窟ボス強化、2026-10-04)
public partial class DebugPanel
{
    void DrawCaveBossPage(Rect p)
    {
        float x = p.x + 24f, y = p.y + 64f, full = p.width - 48f;
        var lab = UiKit.Label(13f, TextAnchor.UpperLeft, false, new Color(0.85f, 0.85f, 0.9f));
        GUI.Label(new Rect(x, y, full, 36f), "押すとシーンを読み直し、DEBUG RUN(保存しない)で選んだボスを関門と同じ流れで出します。出した後は下の操作で段階/必殺技/BREAK/ラン再開を強制できます", lab);
        y += 36f;
        float fw = (full - 6f) / 2f;
        if (UiKit.Button(new Rect(x, y, fw, 30f), "自然洞窟", 13f, EndgameDebug.SelectedBossFamily == 0, false)) EndgameDebug.SelectedBossFamily = 0;
        if (UiKit.Button(new Rect(x + fw + 6f, y, fw, 30f), "天空回廊", 13f, EndgameDebug.SelectedBossFamily == 1, false)) EndgameDebug.SelectedBossFamily = 1;
        y += 34f;
        bool skyFam = EndgameDebug.SelectedBossFamily == 1;
        var kindNames = skyFam ? System.Enum.GetNames(typeof(SkyBossKind)) : System.Enum.GetNames(typeof(CaveBossKind));
        float cw = (full - 3f * 6f) / 4f;
        for (int i = 0; i < kindNames.Length; i++)
        {
            int col = i % 4, row = i / 4;
            bool on = skyFam ? (int)EndgameDebug.SelectedSkyBoss == i : (int)EndgameDebug.SelectedCaveBoss == i;
            if (UiKit.Button(new Rect(x + col * (cw + 6f), y + row * 36f, cw, 32f), kindNames[i], 12f, on, false))
            {
                if (skyFam) EndgameDebug.SelectedSkyBoss = (SkyBossKind)i; else EndgameDebug.SelectedCaveBoss = (CaveBossKind)i;
            }
        }
        y += 3 * 36f + 4f;
        int nt = BossRematchTuning.I.tiers.Count;
        float tw = (full - nt * 6f) / (nt + 1);
        for (int i = -1; i < nt; i++)
            if (UiKit.Button(new Rect(x + (i + 1) * (tw + 6f), y, tw, 34f), EndgameDebug.CaveTierLabel(i), 12f, EndgameDebug.SelectedCaveTier == i, false)) EndgameDebug.SelectedCaveTier = i;
        y += 40f;
        bool busy = EndgameDebug.Instance != null && EndgameDebug.Instance.Launching;
        string selName = skyFam ? EndgameDebug.SelectedSkyBoss.ToString() : EndgameDebug.SelectedCaveBoss.ToString();
        if (UiKit.Button(new Rect(x, y, full, 44f), $"出す: {selName}({EndgameDebug.CaveTierLabel(EndgameDebug.SelectedCaveTier)})", 16f, true, false) && !busy)
        {
            if (skyFam) EndgameDebug.LaunchSkyBoss(EndgameDebug.SelectedSkyBoss, EndgameDebug.SelectedCaveTier, EndgameDebug.SelectedCaveChar);
            else EndgameDebug.LaunchCaveBoss(EndgameDebug.SelectedCaveBoss, EndgameDebug.SelectedCaveTier, EndgameDebug.SelectedCaveChar);
        }
        y += 52f;
        var b = EndgameDebug.FirstLivingBoss();
        var bm = BossManager.Instance;
        string st = b == null ? "戦闘中のボスはいません" : $"{b.DebugName}  段階 {b.Phase}/{b.PhaseCount}  崩し {b.StaggerFraction * 100f:F0}%{(b.Broken ? " BREAK" : "")}  必殺技 {b.UltimatesUsed}回{(b.UltimateRunning ? "(発動中)" : "")}  {(bm != null && bm.RunResumed ? "ラン再開済み" : bm != null ? $"再開まで {bm.ResumeSecondsLeft:F0}秒" : "")}";
        GUI.Label(new Rect(x, y, full, 20f), st, lab);
        y += 24f;
        float bw = (full - 2f * 6f) / 3f;
        for (int ph = 1; ph <= 3; ph++)
            if (UiKit.Button(new Rect(x + (ph - 1) * (bw + 6f), y, bw, 38f), $"段階{ph}へ", 14f, b != null && b.Phase == ph, false) && b != null) b.DebugSetPhase(ph);
        y += 44f;
        if (UiKit.Button(new Rect(x, y, bw, 38f), "必殺技", 14f, false, false) && b != null) b.DebugForceUltimate();
        if (UiKit.Button(new Rect(x + bw + 6f, y, bw, 38f), "BREAK", 14f, false, false) && b != null) b.DebugForceBreak();
        if (UiKit.Button(new Rect(x + 2f * (bw + 6f), y, bw, 38f), "ラン再開", 14f, false, false) && bm != null) bm.DebugForceResume();
        y += 44f;
        GUI.Label(new Rect(x, y, full, 36f), $"地形の攻撃: 生成{CaveHazard.Spawned} 使い回し{CaveHazard.Reused} 表示中{CaveHazard.LiveCount}  床と天井の重なり{CaveHazard.Violations}件  遅らせた{CaveBossSafety.Delayed}  穴で中止{CaveBossSafety.PitSkips}", lab);
    }
}
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
// DebugPanel: FINAL EVOLUTION TEST(2026-10-04)
public partial class DebugPanel
{
    void DrawFinalEvoPage(Rect p)
    {
        float x = p.x + 24f, y = p.y + 64f, full = p.width - 48f;
        var lab = UiKit.Label(12f, TextAnchor.UpperLeft, false, new Color(0.85f, 0.85f, 0.9f));
        GUI.Label(new Rect(x, y, full, 30f), "シーンを読み直して DEBUG RUN(保存/記録しない)で開始。カードを Lv9 / READY にしてから走ります。「LEVEL UP」で3択を開けます", lab);
        y += 32f;
        var chars = EndgameDebug.UltCharacters;
        float cw = (full - 5f * 6f) / 6f;
        for (int i = 0; i < chars.Length; i++)
        {
            int col = i % 6, row = i / 6;
            if (UiKit.Button(new Rect(x + col * (cw + 6f), y + row * 34f, cw, 30f), EndgameDebug.UltCharLabel(chars[i]), 11f, EndgameDebug.SelectedFeChar == chars[i], false)) EndgameDebug.SelectedFeChar = chars[i];
        }
        y += 2 * 34f + 4f;
        string[] stages = { "wasteland_road", "natural_cave", "sky_corridor" };
        float sw = (full - 2f * 6f) / 3f;
        for (int i = 0; i < 3; i++) if (UiKit.Button(new Rect(x + i * (sw + 6f), y, sw, 30f), EndgameDebug.StageLabel(stages[i]), 12f, EndgameDebug.SelectedFeStage == stages[i], false)) EndgameDebug.SelectedFeStage = stages[i];
        y += 36f;
        var cards = EndgameDebug.FeCards;
        // 全対象カード(2026-10-05): 10枚ずつのページ
        int pages = Mathf.Max(1, (cards.Length + 9) / 10);
        EndgameDebug.FeCardPage = Mathf.Clamp(EndgameDebug.FeCardPage, 0, pages - 1);
        float kw = (full - 4f * 6f - 2f * 40f) / 5f;
        if (UiKit.Button(new Rect(x, y, 36f, 64f), "◀", 14f, false, false)) EndgameDebug.FeCardPage = (EndgameDebug.FeCardPage + pages - 1) % pages;
        if (UiKit.Button(new Rect(x + full - 36f, y, 36f, 64f), "▶", 14f, false, false)) EndgameDebug.FeCardPage = (EndgameDebug.FeCardPage + 1) % pages;
        for (int k = 0; k < 10; k++)
        {
            int i = EndgameDebug.FeCardPage * 10 + k;
            if (i >= cards.Length) break;
            int col = k % 5, row = k / 5;
            var c = CardDatabase.FindBaseById(cards[i]);
            if (UiKit.Button(new Rect(x + 40f + col * (kw + 6f), y + row * 34f, kw, 30f), c != null ? c.cardName : cards[i], 9f, EndgameDebug.SelectedFeCard == cards[i], false)) EndgameDebug.SelectedFeCard = cards[i];
        }
        y += 2 * 34f + 4f;
        GUI.Label(new Rect(x, y - 4f, full, 16f), $"{EndgameDebug.FeCardPage + 1}/{pages} ページ  全{cards.Length}枚", UiKit.Label(10f, TextAnchor.MiddleCenter, false, new Color(0.8f, 0.8f, 0.9f)));
        y += 12f;
        float tw = (full - 3f * 6f) / 4f;
        if (UiKit.Button(new Rect(x, y, tw, 30f), $"AWAKENED {(EndgameDebug.FeAwakened ? "ON" : "OFF")}", 11f, EndgameDebug.FeAwakened, false)) EndgameDebug.FeAwakened = !EndgameDebug.FeAwakened;
        if (UiKit.Button(new Rect(x + (tw + 6f), y, tw, 30f), $"Lv9開始 {(EndgameDebug.FeLv9 ? "ON" : "OFF")}", 11f, EndgameDebug.FeLv9, false)) EndgameDebug.FeLv9 = !EndgameDebug.FeLv9;
        if (UiKit.Button(new Rect(x + 2f * (tw + 6f), y, tw, 30f), $"READY開始 {(EndgameDebug.FeReady ? "ON" : "OFF")}", 11f, EndgameDebug.FeReady, false)) EndgameDebug.FeReady = !EndgameDebug.FeReady;
        if (UiKit.Button(new Rect(x + 3f * (tw + 6f), y, tw, 30f), $"ボス {(EndgameDebug.FeBoss ? "あり" : "なし")}", 11f, EndgameDebug.FeBoss, false)) EndgameDebug.FeBoss = !EndgameDebug.FeBoss;
        y += 36f;
        bool busy = EndgameDebug.Instance != null && EndgameDebug.Instance.Launching;
        if (UiKit.Button(new Rect(x, y, full, 40f), "開始(DEBUG RUN)", 16f, true, false) && !busy)
            EndgameDebug.LaunchFinalEvo(EndgameDebug.SelectedFeChar, EndgameDebug.SelectedFeStage, EndgameDebug.SelectedFeCard, EndgameDebug.FeAwakened, EndgameDebug.FeLv9, EndgameDebug.FeReady, EndgameDebug.FeBoss);
        y += 46f;
        var gm = GameManager.Instance;
        string id = EndgameDebug.SelectedFeCard;
        float bw = (full - 3f * 6f) / 4f;
        FinalEvolution.DebugForceCandidate = EndgameDebug.FeForceCandidate ? id : null;
        if (UiKit.Button(new Rect(x, y, bw, 34f), "LEVEL UP(3択)", 12f, false, false) && gm != null && gm.HasStarted) { SetOpen(false); gm.DebugTriggerLevelUp(); }
        if (UiKit.Button(new Rect(x + bw + 6f, y, bw, 34f), "今すぐ READY", 12f, false, false) && gm != null && gm.HasStarted) { gm.FinalEvoTestPrepare(id); FinalEvolution.DebugMakeReady(id); }
        if (UiKit.Button(new Rect(x + 2f * (bw + 6f), y, bw, 34f), "今すぐ終了", 12f, false, false)) FinalEvolution.DebugEnd(id);
        // 再使用の確認: ACTIVE を即終了 → READY → すぐ LEVEL UP(同じ FE を候補に固定していれば同じ FE を選び直せる)
        if (UiKit.Button(new Rect(x + 3f * (bw + 6f), y, bw, 34f), "終了→LEVEL UP", 12f, true, false) && gm != null && gm.HasStarted) { FinalEvolution.DebugEnd(id); SetOpen(false); gm.DebugTriggerLevelUp(); }
        y += 40f;
        if (UiKit.Button(new Rect(x, y, full, 26f), EndgameDebug.FeForceCandidate ? "選んだカードの FE を候補に固定: ON(READY の時)" : "選んだカードの FE を候補に固定: OFF(通常の公平な抽選)", 11f, EndgameDebug.FeForceCandidate, false)) EndgameDebug.FeForceCandidate = !EndgameDebug.FeForceCandidate;
        y += 30f;
        if (gm != null && gm.HasStarted)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in EndgameDebug.FeCards)
            {
                var st = FinalEvolution.StageOf(c);
                if (st == FinalEvolution.Stage.None && gm.GetAbilityRunStack(c) == 0) continue;
                sb.Append($"{c} Lv{gm.GetAbilityRunStack(c)} {st}");
                if (st == FinalEvolution.Stage.Eligible) sb.Append($"(READY まで {Mathf.Max(0f, FinalEvolutionTuning.I.readyMeters - (gm.MaxDistance - FinalEvolution.EligibleAt(c))):0}m)");
                if (st == FinalEvolution.Stage.Active) sb.Append($"(残り {FinalEvolution.Remaining(c):0.#})");
                if (FinalEvolution.Uses(c) > 0) sb.Append($" 発動{FinalEvolution.Uses(c)}回");
                sb.Append("   ");
            }
            GUI.Label(new Rect(x, y, full, 60f), sb.Length > 0 ? sb.ToString() : "状態なし", lab);
        }
    }
}
#endif
