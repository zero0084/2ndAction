using System.Collections.Generic;
using UnityEngine;

// PC の画面設定(2026-10-25、Android/Windows の操作・設定の整理 → 2026-10-31 画面設定の仕上げ)。PlatformCaps.SupportsWindowMode の環境だけ動く。
//  ・表示モード: ウィンドウ / 枠なし全画面(全画面のウィンドウ。今までの「フルスクリーン」)/ 全画面(排他)。
//    排他の全画面は、使っているグラフィックスで本当に排他になる時だけ出す(DirectX 12 は Unity では指定しても枠なしになる → 出さない)
//  ・画面サイズ: 1280x720 / 1600x900 / 1920x1080 / 2560x1440 / 3840x2160 と画面が対応する大きさから、モニターに収まる物
//  ・表示モード/画面サイズは「適用」→ 15秒以内に「維持する」を押さないと元に戻す(ポーズ中も実時間で数える。画面が見えなくなっても戻れる)
//  ・ウィンドウの端のドラッグで大きさを変えられる(Player 設定 resizableWindow)。変えた大きさは止まってから保存し、次の起動で戻す
//  ・Alt+Enter: ウィンドウ ⇔ 枠なし全画面(自前。Unity の標準の切り替えは止めてある = allowFullscreenSwitch 0)。その Enter は決定に使わない。
//    全画面からウィンドウへ戻る時は、直前のウィンドウの大きさへ
//  ・モニターの構成が変わった/保存の大きさが今のモニターより大きい: 画面に収まる大きさへ直す(ウィンドウや確認のボタンが見えなくならないように)
//  ・垂直同期 / FPS 上限: すぐ反映して保存
//  保存は SettingsSave(SaveKeys の設定)。保存が無い間は何もしない(Unity の既定のまま)
public class DisplaySettings : MonoBehaviour
{
    public const string ModeKey = "DisplayModeV1", ResKey = "DisplayResolutionV1", VSyncKey = "VSyncV1", FpsKey = "FpsCapV1", WindowKey = "DisplayWindowSizeV1";
    public enum Mode { Fullscreen = 0, Windowed = 1, Exclusive = 2 } // Fullscreen = 枠なし全画面(保存の値は今までと同じ)
    public static readonly int[] FpsOptions = { 60, 120, 144, 0 }; // 0 = 無制限
    public const float ConfirmSeconds = 15f;
    public static readonly Vector2Int MinWindow = new Vector2Int(640, 360); // 短い辺 360 / 長い辺 640 まで(縦長のウィンドウも可)
    static readonly Vector2Int[] Standard = { new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(3840, 2160) };

    public static DisplaySettings Instance { get; private set; }
    public static bool PendingConfirm { get; private set; }
    public static float ConfirmLeft => PendingConfirm ? Mathf.Max(0f, confirmUntil - Time.realtimeSinceStartup) : 0f;
    public static int Reverts { get; private set; }  // 確認用
    public static int Toggles { get; private set; }  // 確認用(Alt+Enter)
    public static int WindowSaves { get; private set; } // 確認用(ウィンドウの大きさの保存)
    static float confirmUntil;
    static Mode prevMode; static Vector2Int prevRes;
    static Mode pendMode; static Vector2Int pendRes;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        PlatformCaps.EnsureReady();
        if (!PlatformCaps.SupportsWindowMode || Instance != null) return;
        var go = new GameObject("[DisplaySettings]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<DisplaySettings>();
        ApplySaved();
    }

    // 排他の全画面が本当に使えるか(DirectX 11 / Vulkan / OpenGL。DirectX 12 は枠なしになる)
    public static bool ExclusiveSupported
    {
        get
        {
            var t = SystemInfo.graphicsDeviceType;
            return Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor
                ? t == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 || t == UnityEngine.Rendering.GraphicsDeviceType.Vulkan || t == UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore
                : false;
        }
    }
    public static Mode[] Modes => ExclusiveSupported ? new[] { Mode.Windowed, Mode.Fullscreen, Mode.Exclusive } : new[] { Mode.Windowed, Mode.Fullscreen };

    static FullScreenMode ToUnity(Mode m) => m == Mode.Windowed ? FullScreenMode.Windowed : m == Mode.Exclusive && ExclusiveSupported ? FullScreenMode.ExclusiveFullScreen : FullScreenMode.FullScreenWindow;
    public static Mode CurrentMode => Screen.fullScreenMode == FullScreenMode.Windowed ? Mode.Windowed : Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen ? Mode.Exclusive : Mode.Fullscreen;
    public static Vector2Int CurrentRes => new Vector2Int(Screen.width, Screen.height);

    // 起動時: 保存があれば反映(試験の起動引数 -screen-width 等の時は、保存の無い試験用データなので何もしない)
    static void ApplySaved()
    {
        bool any = false;
        if (SaveStore.HasKey(ModeKey) || SaveStore.HasKey(ResKey))
        {
            var m = (Mode)Mathf.Clamp(SaveStore.GetInt(ModeKey, (int)CurrentMode), 0, 2);
            if (m == Mode.Exclusive && !ExclusiveSupported) m = Mode.Fullscreen;
            var r = ParseRes(SaveStore.GetString(m == Mode.Windowed && SaveStore.HasKey(WindowKey) ? WindowKey : ResKey, ""), CurrentRes);
            r = ClampToDisplay(r, m);
            Screen.SetResolution(r.x, r.y, ToUnity(m));
            any = true;
            Debug.Log($"[Display] saved mode {m} {r.x}x{r.y} (desktop {DesktopSize.x}x{DesktopSize.y})");
        }
        if (SaveStore.HasKey(VSyncKey) || SaveStore.HasKey(FpsKey)) { ApplyFrameRate(); any = true; }
        if (!any) Debug.Log($"[Display] no saved display settings (mode {CurrentMode} {Screen.width}x{Screen.height}, vSync {QualitySettings.vSyncCount})");
        lastSize = CurrentRes; lastMode = CurrentMode;
    }

    static Vector2Int ParseRes(string s, Vector2Int def)
    {
        var p = s.Split('x');
        if (p.Length == 2 && int.TryParse(p[0], out int w) && int.TryParse(p[1], out int h) && Mathf.Max(w, h) >= MinWindow.x && Mathf.Min(w, h) >= MinWindow.y) return new Vector2Int(w, h);
        return def;
    }

    static Vector2Int DesktopSize => new Vector2Int(Display.main.systemWidth, Display.main.systemHeight);
    // モニターに収まる大きさ: 全画面はモニターより大きくしない / ウィンドウはモニターの 95%(枠・タスクバーの分)まで、最小の大きさは守る
    public static Vector2Int ClampToDisplay(Vector2Int r, Mode m)
    {
        var d = DesktopSize;
        if (d.x <= 0 || d.y <= 0) return r;
        float lim = m == Mode.Windowed ? 0.95f : 1f;
        if (r.x > d.x * lim || r.y > d.y * lim) { float k = Mathf.Min(d.x * lim / r.x, d.y * lim / r.y); r = new Vector2Int(Mathf.RoundToInt(r.x * k), Mathf.RoundToInt(r.y * k)); }
        if (m == Mode.Windowed)
        {
            bool tall = r.y > r.x;
            int minW = tall ? MinWindow.y : MinWindow.x, minH = tall ? MinWindow.x : MinWindow.y;
            r = new Vector2Int(Mathf.Max(r.x, minW), Mathf.Max(r.y, minH));
        }
        return r;
    }

    // 選べる画面サイズ(重複なし・小さい順)。決まった候補 + 画面が対応する大きさ。モニターに収まらない物は出さない。今の大きさは必ず入れる
    public static List<Vector2Int> Resolutions(Mode m)
    {
        var list = new List<Vector2Int>();
        var d = DesktopSize;
        float lim = m == Mode.Windowed ? 0.95f : 1f;
        void Add(Vector2Int v)
        {
            if (v.x < 960 || v.y < 540) return;
            if (d.x > 0 && (v.x > d.x * lim + 0.5f || v.y > d.y * lim + 0.5f)) return;
            if (!list.Contains(v)) list.Add(v);
        }
        foreach (var v in Standard) Add(v);
        foreach (var r in Screen.resolutions) Add(new Vector2Int(r.width, r.height));
        if (m != Mode.Windowed && d.x > 0) Add(d);
        if (list.Count == 0 && d.x > 0) list.Add(ClampToDisplay(d, m));
        if (!list.Contains(CurrentRes) && CurrentRes.x >= 640) list.Add(CurrentRes);
        list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        return list;
    }

    // ウィンドウへ切り替える時の大きさ: 前のウィンドウの大きさ(保存)。無ければモニターの約7割の16:9
    public static Vector2Int LastWindowSize
    {
        get
        {
            var r = ParseRes(SaveStore.GetString(WindowKey, ""), new Vector2Int(0, 0));
            if (r.x <= 0) { var d = DesktopSize; r = d.x > 0 ? new Vector2Int(Mathf.RoundToInt(d.x * 0.7f), Mathf.RoundToInt(d.x * 0.7f * 9f / 16f)) : new Vector2Int(1280, 720); }
            return ClampToDisplay(r, Mode.Windowed);
        }
    }

    // 表示モード/画面サイズを試す(確定は Keep)。15秒で戻る
    public static void Request(Mode m, Vector2Int res)
    {
        if (!PlatformCaps.SupportsWindowMode) return;
        if (m == Mode.Exclusive && !ExclusiveSupported) m = Mode.Fullscreen;
        if (!PendingConfirm) { prevMode = CurrentMode; prevRes = CurrentRes; if (prevMode == Mode.Windowed) SaveWindowSize(prevRes, "before change"); }
        res = ClampToDisplay(res, m);
        pendMode = m; pendRes = res;
        Screen.SetResolution(res.x, res.y, ToUnity(m));
        PendingConfirm = true;
        confirmUntil = Time.realtimeSinceStartup + ConfirmSeconds; // 実時間(一時停止中も進む)
        ignoreResizeUntil = Time.realtimeSinceStartup + 1f;
        Debug.Log($"[Display] try {m} {res.x}x{res.y} (was {prevMode} {prevRes.x}x{prevRes.y})");
    }

    public static void Keep()
    {
        if (!PendingConfirm) return;
        PendingConfirm = false;
        SaveStore.SetInt(ModeKey, (int)pendMode);
        SaveStore.SetString(ResKey, pendRes.x + "x" + pendRes.y);
        if (pendMode == Mode.Windowed) SaveStore.SetString(WindowKey, pendRes.x + "x" + pendRes.y);
        SaveStore.Save();
        lastSize = pendRes; lastMode = pendMode;
        Debug.Log($"[Display] kept {pendMode} {pendRes.x}x{pendRes.y}");
    }

    public static void Revert(string why)
    {
        if (!PendingConfirm) return;
        PendingConfirm = false;
        Reverts++;
        Screen.SetResolution(prevRes.x, prevRes.y, ToUnity(prevMode));
        ignoreResizeUntil = Time.realtimeSinceStartup + 1f;
        lastSize = prevRes; lastMode = prevMode;
        Debug.Log($"[Display] reverted to {prevMode} {prevRes.x}x{prevRes.y} ({why})");
    }

    // ---------------------------------------------------------------- Alt+Enter / ウィンドウの大きさの変更 / モニターの変化
    static Vector2Int lastSize; static Mode lastMode;
    static float stableSince = -1f, ignoreResizeUntil, nextMonitorCheck;
    public static bool AltEnterDown => (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) || Input.GetKey(KeyCode.AltGr)) && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter));

    public static void ToggleFullscreen(string why)
    {
        if (!PlatformCaps.SupportsWindowMode) return;
        if (PendingConfirm) Revert("alt+enter");
        Mode from = CurrentMode;
        Mode to; Vector2Int size;
        if (from == Mode.Windowed) { SaveWindowSize(CurrentRes, "before fullscreen"); to = Mode.Fullscreen; size = DesktopSize.x > 0 ? DesktopSize : CurrentRes; }
        else { to = Mode.Windowed; size = LastWindowSize; }
        Screen.SetResolution(size.x, size.y, ToUnity(to));
        SaveStore.SetInt(ModeKey, (int)to);
        SaveStore.SetString(ResKey, size.x + "x" + size.y);
        SaveStore.Save();
        Toggles++;
        ignoreResizeUntil = Time.realtimeSinceStartup + 1f;
        lastSize = size; lastMode = to;
        Debug.Log($"[Display] toggle ({why}) {from} -> {to} {size.x}x{size.y}");
    }

    static void SaveWindowSize(Vector2Int r, string why)
    {
        if (r.x < 320 || r.y < 200) return;
        SaveStore.SetString(WindowKey, r.x + "x" + r.y);
        if (lastMode == Mode.Windowed || CurrentMode == Mode.Windowed) SaveStore.SetString(ResKey, r.x + "x" + r.y);
        SaveStore.Save();
        WindowSaves++;
        Debug.Log($"[Display] window size saved {r.x}x{r.y} ({why})");
    }

    void Update()
    {
        if (PendingConfirm && Time.realtimeSinceStartup >= confirmUntil) Revert("timeout");
        if (AltEnterDown)
        {
            GameInput.LatchAction(GameAction.Confirm); // この Enter を決定に使わない
            GameInput.LatchHeld();
            ToggleFullscreen("alt+enter");
            return;
        }
        float now = Time.realtimeSinceStartup;
        var cur = CurrentRes; var mode = CurrentMode;
        // 端のドラッグでウィンドウの大きさが変わった: 止まって 0.6秒たったら保存(小さすぎる時は最小へ)
        if (mode == Mode.Windowed && !PendingConfirm && now >= ignoreResizeUntil && (cur != lastSize || mode != lastMode))
        {
            if (stableSince < 0f || cur != pendingSize) { stableSince = now; pendingSize = cur; }
            else if (now - stableSince >= 0.6f)
            {
                var fixedSize = ClampToDisplay(cur, Mode.Windowed);
                if (fixedSize != cur) { Screen.SetResolution(fixedSize.x, fixedSize.y, FullScreenMode.Windowed); ignoreResizeUntil = now + 1f; Debug.Log($"[Display] window {cur.x}x{cur.y} -> {fixedSize.x}x{fixedSize.y} (min/monitor)"); }
                SaveWindowSize(fixedSize, "resized");
                SaveStore.SetInt(ModeKey, (int)Mode.Windowed); SaveStore.Save();
                lastSize = fixedSize; lastMode = mode; stableSince = -1f;
            }
        }
        else if (mode != Mode.Windowed) { lastSize = cur; lastMode = mode; stableSince = -1f; }
        // モニターの構成が変わった: 2秒ごとに、ウィンドウがモニターより大きくないか見る
        if (now >= nextMonitorCheck)
        {
            nextMonitorCheck = now + 2f;
            if (mode == Mode.Windowed && !PendingConfirm)
            {
                var fit = ClampToDisplay(cur, Mode.Windowed);
                if (fit != cur && (cur.x > DesktopSize.x || cur.y > DesktopSize.y)) { Screen.SetResolution(fit.x, fit.y, FullScreenMode.Windowed); ignoreResizeUntil = now + 1f; Debug.Log($"[Display] window larger than the monitor {cur.x}x{cur.y} -> {fit.x}x{fit.y}"); }
            }
        }
    }
    static Vector2Int pendingSize;

    // ---------------------------------------------------------------- 垂直同期 / FPS 上限
    public static bool VSync => SaveStore.GetInt(VSyncKey, 1) == 1;
    public static int FpsCap => SaveStore.GetInt(FpsKey, 0);
    public static void SetVSync(bool on) { SaveStore.SetInt(VSyncKey, on ? 1 : 0); SaveStore.Save(); ApplyFrameRate(); }
    public static void SetFpsCap(int fps) { SaveStore.SetInt(FpsKey, fps); SaveStore.Save(); ApplyFrameRate(); }
    static void ApplyFrameRate()
    {
        bool v = VSync;
        QualitySettings.vSyncCount = v ? 1 : 0;
        Application.targetFrameRate = v ? -1 : (FpsCap > 0 ? FpsCap : -1);
        Debug.Log($"[Display] vSync {(v ? "on" : "off")} fpsCap {(v ? "(vsync)" : FpsCap > 0 ? FpsCap.ToString() : "unlimited")}");
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static void DebugSetConfirmLeft(float sec) { if (PendingConfirm) confirmUntil = Time.realtimeSinceStartup + sec; }
#endif
}
