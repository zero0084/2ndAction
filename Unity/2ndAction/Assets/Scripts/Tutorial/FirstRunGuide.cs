using UnityEngine;

// 初回の案内(2026-10-07)。どれもデータ(通常/テスト用)ごとに1回だけ(TutorialProgress)。マルチプレイ中は出さない。
//  ① 初めて扉を押した時: 「操作を練習する / そのまま始める」(新規のデータだけ)
//  ② 初めてのボスの報酬が終わって脱出できるようになった時: ゲームを止めて脱出の説明(脱出は強制しない)
//     規則どおりの書き方: 脱出 = このランのMILEを持ち帰る / 倒れる = このランのMILEは失う(BEST距離の記録は残る)
//  ③ 初めて脱出した後のホーム: MILEの使い道(ガチャ 1回 500 MILE)を短く
//  説明の間は時間を止める(①③はホームなので止める物は無い)。閉じた指は離すまで操作にしない。
public class FirstRunGuide : MonoBehaviour
{
    public static FirstRunGuide Instance { get; private set; }
    public enum Kind { None, DoorPrompt, EscapeGuide, MileGuide }
    public static Kind Showing => Instance != null ? Instance.showing : Kind.None;
    public static bool Open => Showing != Kind.None;
    Kind showing;
    readonly object pauseOwner = new object();
    bool paused;
    float idleFor;
    public static int EscapeGuidesShown { get; private set; }  // 確認用
    public static int MileGuidesShown { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[FirstRunGuide]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<FirstRunGuide>();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => { if (Instance != null) Instance.Close(false); };
    }

    // ① 扉から(GameManager.OnDoorTapped)。出したら true(扉の処理はここで止める)
    public static bool TryDoorPrompt()
    {
        if (Instance == null || Open || !TutorialProgress.ShouldOfferOnDoor || !TutorialMode.CanLaunchFromHome) return false;
        Instance.Show(Kind.DoorPrompt);
        return true;
    }

    void Show(Kind k)
    {
        showing = k;
        idleFor = 0f;
        if (k == Kind.EscapeGuide) SetPaused(true);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiOpen);
        Debug.Log($"[Guide] show {k}");
    }

    void Close(bool latch = true)
    {
        if (showing == Kind.None) return;
        Debug.Log($"[Guide] close {showing}");
        showing = Kind.None;
        SetPaused(false);
        if (latch)
        {
            UiInputGate.LatchUntilRelease();
            if (PlayerController.Instance != null) PlayerController.Instance.ClearPointerState();
        }
    }

    void SetPaused(bool on)
    {
        if (on == paused) return;
        paused = on;
        if (on) TimeControl.Pause(pauseOwner); else TimeControl.Resume(pauseOwner);
    }

    // ---------------------------------------------------------------- 出す時を見張る
    static bool RealRun(GameManager gm) =>
        gm.HasStarted && !gm.IsGameOver && !TutorialMode.Active && !ArenaMode.Active && !DebugRun.WritesBlocked
        && !NetRunLauncher.IsMultiplayerRun && !NetSession.IsActive;

    void Update()
    {
        var gm = GameManager.Instance;
        if (gm == null) { idleFor = 0f; return; }
        if (showing == Kind.EscapeGuide)
        {
            // ラン以外になった(倒れた/ホームへ): 説明は閉じる(出したことにはしない → 次のランで出す)
            if (!gm.HasStarted || gm.IsGameOver) Close(false);
            return;
        }
        if (showing != Kind.None) return;

        // ② 脱出の説明: 初めて脱出できるようになって、周りが落ち着いている時(報酬/レベルアップ/ボス戦/停止/遷移の最中は待つ)
        if (RealRun(gm) && gm.EscapeAvailable && !TutorialProgress.EscapeGuideShown)
        {
            var pc = PlayerController.Instance;
            bool calm = Time.timeScale > 0.01f && !gm.IsRewardSequenceRunning && !gm.LevelUpPending && !gm.PauseMenuOpen && !gm.ResumeGateActive
                && !(BossManager.Instance != null && BossManager.Instance.IsBossPhase) && !SettingsPanel.IsVisible && !UiInputGate.ModalOpen
                && !(ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning)
                && !(pc != null && (pc.IsFinishing || !pc.IsGrounded));
            idleFor = calm ? idleFor + Time.unscaledDeltaTime : 0f;
            if (idleFor > 1.0f) { EscapeGuidesShown++; Show(Kind.EscapeGuide); }
            return;
        }
        // ③ MILEの案内: 初めて脱出した後のホーム
        if (!gm.HasStarted && TutorialProgress.MileGuide == 1 && !NetSession.IsActive && !gm.IsOverlayOpen && !gm.HomeModalOpen
            && !SettingsPanel.IsVisible && !UiInputGate.ModalOpen && !TutorialLauncher.Covering && !ArenaLauncher.Covering
            && !(ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning))
        {
            idleFor += Time.unscaledDeltaTime;
            if (idleFor > 0.8f) { MileGuidesShown++; Show(Kind.MileGuide); }
            return;
        }
        idleFor = 0f;
    }

    // ボタンの処理(画面のボタンと自動テストで同じ)。choice 0 = 主ボタン / 1 = 2つ目
    public void DebugAnswer(int choice)
    {
        var k = showing;
        if (k == Kind.None) return;
        if (k == Kind.DoorPrompt)
        {
            TutorialProgress.MarkOffered();
            Close();
            var gm = GameManager.Instance;
            if (choice == 0) { if (!TutorialLauncher.Launch(true, "first door") && gm != null) gm.OpenStageSelect(); }
            else if (gm != null) gm.OpenStageSelect();
            return;
        }
        if (k == Kind.EscapeGuide) TutorialProgress.MarkEscapeGuideShown();
        else TutorialProgress.MarkMileGuideShown();
        Close();
    }

    // ---------------------------------------------------------------- 表示
    static string HoldIn()
    {
        switch (GameInput.LastDevice)
        {
            case InputDeviceKind.Keyboard: return "H キー(またはマウスのボタン)を3秒長押し";
            case InputDeviceKind.Gamepad: return "BACK ボタンを3秒長押し";
            default: return "画面を3秒長押し";
        }
    }

    GUIStyle titleSt, bodySt;
    void OnGUI()
    {
        if (showing == Kind.None) return;
        GUI.depth = -1800;
        float s = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 0.8f, 2.6f);
        if (titleSt == null)
        {
            titleSt = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
            bodySt = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
        }
        string title, body;
        switch (showing)
        {
            case Kind.DoorPrompt:
                title = "はじめての方へ";
                body = "最初に操作を練習できます(2〜3分・時間制限なし)。\n練習では報酬や記録は増えません。\n\n練習は、あとから設定の「遊び方」でいつでもできます。";
                break;
            case Kind.EscapeGuide:
                title = "脱出できるようになりました";
                body = HoldIn() + "すると脱出して、\nこのランで集めたMILEを持ち帰れます。\n\n倒れてしまうと、このランのMILEは失われます。\n(BEST距離の記録は残ります)\n\nこのまま走り続けてもOK。脱出するかどうかは自由です。";
                break;
            default:
                title = "MILEを持ち帰りました!";
                body = "MILEは、ホームのガチャで使えます(1回 " + GameManager.GachaCostMile + " MILE)。\n引いたカードはデッキに入れて、次のランを強くできます。";
                break;
        }

        // 画面を暗くする(ランの時はキャラが見えるよう薄く、板はキャラに重ならない側)
        bool inRun = showing == Kind.EscapeGuide;
        UiKit.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, inRun ? 0.3f : 0.55f));
        float pw = Mathf.Min(Screen.width - 32 * s, 900 * s), k = 1f;
        Rect p;
        if (inRun) p = GuidePlacement.Place(pw, Mathf.Min(Screen.height * 0.62f, 400 * s), 14 * s, 8 * s, out k);
        else { float ph = Mathf.Min(Screen.height * 0.7f, 400 * s); p = new Rect((Screen.width - pw) * 0.5f, (Screen.height - ph) * 0.5f, pw, ph); }
        titleSt.fontSize = Mathf.RoundToInt(32 * s * k); titleSt.normal.textColor = new Color(1f, 0.86f, 0.45f);
        bodySt.fontSize = Mathf.RoundToInt(23 * s * k); bodySt.normal.textColor = Color.white;
        OrnateUi.DrawPanel(p, 0.95f);
        GUI.Label(new Rect(p.x + 20 * s, p.y + 10 * s * k, p.width - 40 * s, 46 * s * k), title, titleSt);
        GUI.Label(new Rect(p.x + 28 * s, p.y + 54 * s * k, p.width - 56 * s, p.height - 130 * s * k), body, bodySt);
        float bw = 280 * s * k, bh = 58 * s * k, by = p.yMax - bh - 14 * s * k;
        int pl = PadNav.BeginLayer(60); // 手前の窓(パッド/キーのフォーカスはこの窓のボタンだけ)
        if (showing == Kind.DoorPrompt)
        {
            if (UiKit.Button(new Rect(p.center.x - bw - 10 * s, by, bw, bh), "操作を練習する", 23 * s, true)) DebugAnswer(0);
            else if (UiKit.Button(new Rect(p.center.x + 10 * s, by, bw, bh), "そのまま始める", 23 * s, false)) DebugAnswer(1);
        }
        else if (UiKit.Button(new Rect(p.center.x - bw * 0.5f, by, bw, bh), "わかった", 24 * s, true)) DebugAnswer(0);
        PadNav.EndLayer(pl);
        // 背後へ通さない
        if (Event.current != null && (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp)) Event.current.Use();
    }
}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
// 自動テスト用: 開いている案内のボタンを押したのと同じにする(0 = 主ボタン/わかった, 1 = 2つ目)
public static class FirstRunGuideDebug
{
    public static void Answer(int choice)
    {
        var g = FirstRunGuide.Instance;
        if (g == null) return;
        g.DebugAnswer(choice);
    }
}
#endif

// 説明の板をキャラに重ねない置き場所(2026-10-07)。キャラの画面上の上下の範囲を測り、空いている広い側(上/下)へ置く。
// 入りきらない時は板を低くし、文字の倍率 k(0.6〜1)を返す。
public static class GuidePlacement
{
    public static bool PlayerBand(out float top, out float bottom)
    {
        top = bottom = 0f;
        var pc = PlayerController.Instance; var cam = Camera.main;
        if (pc == null || cam == null) return false;
        var sr = pc.GetComponentInChildren<SpriteRenderer>();
        Bounds b = sr != null && sr.enabled ? sr.bounds : new Bounds(pc.transform.position + Vector3.up * 0.9f, new Vector3(1f, 1.8f, 0f));
        float yMax = cam.WorldToScreenPoint(new Vector3(b.center.x, b.max.y, 0f)).y;
        float yMin = cam.WorldToScreenPoint(new Vector3(b.center.x, b.min.y, 0f)).y;
        top = Screen.height - yMax; bottom = Screen.height - yMin;
        return true;
    }

    public static Rect Place(float pw, float desiredH, float margin, float topReserve, out float k)
    {
        float x = (Screen.width - pw) * 0.5f;
        if (!PlayerBand(out float top, out float bottom)) { k = 1f; return new Rect(x, topReserve, pw, desiredH); }
        float above = top - margin - topReserve, below = Screen.height - bottom - margin * 2f;
        bool up = above >= below;
        float h = Mathf.Min(desiredH, Mathf.Max(desiredH * 0.6f, up ? above : below));
        k = Mathf.Clamp(h / desiredH, 0.6f, 1f);
        float y = up ? Mathf.Max(topReserve, top - margin - h) : Mathf.Min(Screen.height - h - margin, bottom + margin);
        return new Rect(x, y, pw, h);
    }
}
