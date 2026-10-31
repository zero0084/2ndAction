using System;
using UnityEngine;

// ゲームの終了・中断の入口(2026-10-31)。どの端末でも迷わずやめられるように、端末ごとに意味をそろえる。
//  ・PC(Windows など)     「ゲーム終了」= 中断の保存をしてからアプリのウィンドウを閉じる(Application.Quit)
//  ・Android              「端末のホームへ」= 中断の保存をしてから端末のホームへ(アプリはバックグラウンドへ: moveTaskToBack)。
//                          ゲームの中の「ホームへ戻る(RETURN TO HOME)」とは文言で分ける
//  ・iPhone / iPad        アプリを閉じるボタンは置かない(押しても閉じないため)。ラン中だけ「中断を保存」(保存して続ける。離れるのは端末の操作)
//  入口: タイトル(右下のボタン / Esc・戻る)、ホーム(右上の列のボタン / 一番上の階層での Esc・戻る・パッドの B)、ラン中は一時停止のメニュー。
//  確認の窓: 初めに選ばれているのは「キャンセル」。Esc / 戻る / パッドの B でキャンセル。開いた入力(押しっぱなし含む)では確定しない
//    (開いた時に押しているキー/指は離すまで無効 + 開いてから 0.35秒は確定のボタンを受け付けない)。
//  保存: 終了の前に GameManager.SaveForExit(既存の中断の保存 = RunCheckpoint)。中断は脱出の成功ではない(MILE/距離/報酬は確定しない)。
//    再開できるか(ソロのラン / マルチ / 闘技場・練習など記録しないモード)を窓の文で分けて出し、保存できない時に「保存しました」とは言わない。
//  ウィンドウの × / Alt+F4 も Application.wantsToQuit で同じ保存を通る(確認は出さない)。
public class AppExit : MonoBehaviour
{
    public enum Style { QuitApp, AndroidBackground, IosSuspend }
    enum Where { Title, Home, Run }
    enum RunKind { None, Resumable, NotResumable, MultiHost, MultiJoin }

    public static Style Current
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            string o = DevArg("-exitStyle");
            if (o == "android") return Style.AndroidBackground;
            if (o == "ios") return Style.IosSuspend;
            if (o == "pc") return Style.QuitApp;
#endif
            if (Application.platform == RuntimePlatform.Android) return Style.AndroidBackground;
            if (Application.platform == RuntimePlatform.IPhonePlayer) return Style.IosSuspend;
            return Style.QuitApp;
        }
    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    static string DevArg(string name)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }
#endif

    // 終了/端末のホームへのボタンを置くか(iOS は置かない)
    public static bool HasExitButton => Current != Style.IosSuspend;
    public static string ButtonLabel => Current == Style.QuitApp ? LocExit.QuitGame : LocExit.ToDeviceHome;
    public static string PauseLabel => Current == Style.QuitApp ? LocExit.QuitGame : Current == Style.AndroidBackground ? LocExit.SuspendToDeviceHome : LocExit.SaveSuspend;

    public static bool Open => inst != null && inst.open;
    public static int Opened { get; private set; }      // 確認用
    public static int Confirmed { get; private set; }
    public static string LastAction { get; private set; } = "";
    public static Rect CancelRect => inst != null ? inst.cancelRect : default; // GUI 座標(パッド/キーの初めの選択)
    public static Rect ConfirmRect => inst != null ? inst.confirmRect : default;
    public static string LastBody => inst != null ? inst.body : "";

    static AppExit inst;
    bool open; Where where; RunKind kind; float openedAt; string body = "";
    Rect cancelRect, confirmRect;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst != null) return;
        var go = new GameObject("[AppExit]");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<AppExit>();
        Application.wantsToQuit += OnWantsToQuit;
    }

    // ×ボタン / Alt+F4 / OS からの終了: 確認は出さずに、中断の保存だけ通す(false を返さない = 止めない)
    static bool quitSaved;
    static bool OnWantsToQuit()
    {
        if (!quitSaved)
        {
            quitSaved = true;
            if (DisplaySettings.PendingConfirm) DisplaySettings.Revert("app quit"); // 確定していない表示の設定は残さない
            SaveBeforeLeave("app quit (" + (inst != null && inst.confirming ? "button" : "window/OS") + ")");
        }
        return true;
    }

    // 中断の保存(既存の仕組み)。ラン中でない時は進行の記録だけ書く
    static void SaveBeforeLeave(string why)
    {
        var gm = GameManager.Instance;
        if (gm != null) gm.SaveForExit(why);
        else { ProgressStats.Flush(true); SaveStore.Save(); }
        RunCheckpoint.Reload();
        Debug.Log($"[Exit] saved before leaving ({why}): suspended run saved {RunCheckpoint.HasActiveRun}");
    }

    // ---------------------------------------------------------------- 入口
    public static void RequestFromTitle() => OpenDialog(Where.Title);
    public static void RequestFromHome() => OpenDialog(Where.Home);
    public static void RequestFromPause()
    {
        if (Current == Style.IosSuspend) { SuspendIos(); return; }
        OpenDialog(Where.Run);
    }

    static void OpenDialog(Where w)
    {
        if (inst == null || inst.open || !HasExitButton) return;
        inst.open = true; inst.where = w; inst.openedAt = Time.unscaledTime; inst.confirming = false;
        inst.kind = w == Where.Run ? ClassifyRun(out inst.cpMeters) : RunKind.None;
        inst.body = BodyFor(w, inst.kind, inst.cpMeters);
        Opened++;
        GameInput.LatchHeld();             // 開いた入力(押しっぱなし)を確定に使わない
        UiInputGate.LatchUntilRelease();
        PadNav.ForgetScreen("exit");       // 毎回「キャンセル」から
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
        Debug.Log($"[Exit] confirm opened from {w} ({Current}, run {inst.kind})");
    }

    public static void Cancel()
    {
        if (inst == null || !inst.open) return;
        inst.open = false;
        GameInput.LatchHeld();
        UiInputGate.LatchUntilRelease();
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Cancel);
        Debug.Log("[Exit] cancelled");
    }

    float cpMeters;
    static RunKind ClassifyRun(out float cp)
    {
        cp = 0f;
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted || gm.IsGameOver) return RunKind.None;
        if (NetRunLauncher.IsMultiplayerRun || NetSession.IsActive) return NetSession.IsHost ? RunKind.MultiHost : RunKind.MultiJoin;
        if (DebugRun.WritesBlocked) return RunKind.NotResumable; // 闘技場・操作の練習・開発の DEBUG RUN は記録しない
        var d = RunCheckpoint.Load();
        cp = gm.CheckpointSnapshotThisRun && d != null && d.active ? d.checkpointDistance : 0f;
        return RunKind.Resumable;
    }

    static string BodyFor(Where w, RunKind k, float cp)
    {
        bool android = Current == Style.AndroidBackground;
        string tail = android ? Loc.T(LocExit.BackgroundNote) : "";
        switch (k)
        {
            case RunKind.Resumable:
                // 既存の「ホームへ戻る」の説明と同じ約束(直前のチェックポイントから再開、それより後の獲得は取り消し)
                return (LocPendingReview.ReturnHomeReady
                    ? Loc.F(LocPendingReview.ReturnHomeLine2, Mathf.FloorToInt(cp).ToString("N0")) + "\n" + Loc.T(LocPendingReview.ReturnHomeLine3)
                    : Loc.F(LocExit.ResumeFrom, Mathf.FloorToInt(cp).ToString("N0"))) + (tail.Length > 0 ? "\n" + tail : "");
            case RunKind.NotResumable: return Loc.T(LocExit.NotResumable) + (tail.Length > 0 ? "\n" + tail : "");
            case RunKind.MultiHost: return Loc.T(LocExit.MultiHost);
            case RunKind.MultiJoin: return Loc.T(LocExit.MultiJoin);
        }
        return tail;
    }

    // ---------------------------------------------------------------- 確定
    bool confirming;
    void DoConfirm()
    {
        confirming = true;
        Confirmed++;
        open = false;
        var style = Current;
        LastAction = style + " from " + where + " (" + kind + ")";
        Debug.Log($"[Exit] confirmed: {LastAction}");
        if (kind == RunKind.MultiHost || kind == RunKind.MultiJoin)
        {
            // マルチ: 今の退出の仕組み。PC の終了はアプリの終了の時に OnlineServices が退出する(wantsToQuit)/LAN はプロセスが閉じれば切れる。
            //  Android は端末のホームへ行くだけでアプリが残るので、ここで退出する(NetSession.Leave: オンラインは部屋を退出/解散、LAN は停止)
            if (style != Style.QuitApp && NetSession.Instance != null) NetSession.Instance.Leave();
        }
        else SaveBeforeLeave("exit button");
        if (style == Style.QuitApp)
        {
            quitSaved = true; // 保存は済んだ(wantsToQuit でもう一度書かない)
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
            return;
        }
        if (style == Style.AndroidBackground)
        {
            if (kind == RunKind.MultiHost || kind == RunKind.MultiJoin) { var gm = GameManager.Instance; if (gm != null) gm.ReturnToHome(); }
            MoveTaskToBack();
            confirming = false;
        }
    }

    static int movedToBack;
    public static int MovedToBack => movedToBack; // 確認用
    static void MoveTaskToBack()
    {
        movedToBack++;
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                act.Call<bool>("moveTaskToBack", true);
            Debug.Log("[Exit] moved the task to the back (device home)");
        }
        catch (Exception e) { Debug.LogWarning("[Exit] moveTaskToBack failed: " + e.Message); }
#else
        Debug.Log("[Exit] (not Android) would move the task to the back");
#endif
    }

    // iOS: 中断を保存して、そのまま一時停止に残る(保存できたかを正直に出す)
    static void SuspendIos()
    {
        var k = ClassifyRun(out float cp);
        if (k == RunKind.Resumable)
        {
            SaveBeforeLeave("ios suspend");
            bool ok = RunCheckpoint.HasActiveRun;
            NoticeQueue.Toast(ok ? Loc.F(LocExit.SuspendSaved, Mathf.FloorToInt(cp).ToString("N0")) : Loc.T(LocExit.NotResumable));
            LastAction = "ios suspend " + (ok ? "saved" : "not saved");
        }
        else
        {
            NoticeQueue.Toast(Loc.T(k == RunKind.MultiHost || k == RunKind.MultiJoin ? LocExit.MultiNoSave : LocExit.NotResumable));
            LastAction = "ios suspend refused (" + k + ")";
        }
        Debug.Log("[Exit] " + LastAction);
    }

    // ---------------------------------------------------------------- 窓
    const float ArmSeconds = 0.35f;
    void OnGUI()
    {
        if (!open) return;
        GUI.depth = -31995; // タイトル(-31990)の手前、起動ロゴの後ろ
        float W = Screen.width, H = Screen.height;
        float s = UiScalePref.Menu(Mathf.Clamp(Mathf.Min(W, H) / 720f, 1f, 2.6f));
        Matrix4x4 keep = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float vw = W / s, vh = H / s;
        UiKit.Fill(new Rect(0f, 0f, vw, vh), new Color(0f, 0f, 0.02f, 0.72f));
        int layer = PadNav.BeginLayer(30);
        float pw = Mathf.Min(600f, vw - 32f);
        var title = new GUIStyle(UiKit.Label(26f, TextAnchor.MiddleCenter, true, UiTheme.GoldText)) { wordWrap = true };
        var bodySt = new GUIStyle(UiKit.Label(18f, TextAnchor.UpperCenter, false, UiTheme.Text)) { wordWrap = true };
        string head = Loc.T(Current == Style.QuitApp ? LocExit.QuitAsk : LocExit.DeviceHomeAsk);
        float th = title.CalcHeight(new GUIContent(head), pw - 48f);
        string shown = Loc.Shape(body ?? "");
        float bh = string.IsNullOrEmpty(shown) ? 0f : bodySt.CalcHeight(new GUIContent(shown), pw - 48f);
        float maxBody = vh - 32f - th - 140f;
        if (bh > maxBody) { bodySt.fontSize = Mathf.Max(12, Mathf.FloorToInt(bodySt.fontSize * maxBody / bh)); bh = Mathf.Min(maxBody, bodySt.CalcHeight(new GUIContent(shown), pw - 48f)); }
        float ph = 28f + th + (bh > 0f ? 12f + bh : 0f) + 28f + 58f + 24f;
        var panel = new Rect((vw - pw) * 0.5f, (vh - ph) * 0.5f, pw, ph);
        OrnateUi.DrawPanel(panel, 0.97f);
        GUI.Label(new Rect(panel.x + 24f, panel.y + 26f, pw - 48f, th), head, title);
        if (bh > 0f) GUI.Label(new Rect(panel.x + 24f, panel.y + 26f + th + 12f, pw - 48f, bh), shown, bodySt);
        float bw = (pw - 24f * 2f - 14f) * 0.5f, by = panel.yMax - 24f - 58f;
        var conf = new Rect(panel.x + 24f, by, bw, 58f);
        var canc = new Rect(conf.xMax + 14f, by, bw, 58f);
        confirmRect = new Rect(conf.x * s, conf.y * s, conf.width * s, conf.height * s);
        cancelRect = new Rect(canc.x * s, canc.y * s, canc.width * s, canc.height * s);
        bool armed = Time.unscaledTime - openedAt >= ArmSeconds && !UiInputGate.Latched;
        string cl = where == Where.Run ? PauseLabel : ButtonLabel;
        if (UiKit.Button(conf, cl, Fit(cl, bw, 20f), false, true, armed) && armed) DoConfirm();
        if (UiKit.Button(canc, LocExit.Cancel, Fit(LocExit.Cancel, bw, 20f), true)) Cancel();
        GUI.Button(new Rect(0f, 0f, vw, vh), GUIContent.none, GUIStyle.none); // 窓の外の押下を後ろ(ホーム/タイトル)へ通さない(ボタンの後に置く = ボタンが先に受ける)
        PadNav.EndLayer(layer);
        GUI.matrix = keep;
    }

    static float Fit(string text, float width, float size)
    {
        string t = Loc.Auto(text);
        var st = new GUIStyle(UiKit.Label(size, TextAnchor.MiddleCenter, true)) { wordWrap = false };
        while (st.fontSize > 12 && st.CalcSize(new GUIContent(t)).x > width - 18f) st.fontSize--;
        return st.fontSize;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static void DebugConfirm() { if (inst != null && inst.open) inst.DoConfirm(); }
    public static float DebugArmLeft => inst != null && inst.open ? Mathf.Max(0f, ArmSeconds - (Time.unscaledTime - inst.openedAt)) : 0f;
#endif
}
