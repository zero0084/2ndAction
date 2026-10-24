using System.Collections.Generic;
using UnityEngine;

// PC の画面設定(2026-10-25、Android/Windows の操作・設定の整理)。PlatformCaps.SupportsWindowMode の環境だけ動く。
//  ・表示モード: フルスクリーン(全画面のウィンドウ = ボーダーレス。今までの既定)/ ウィンドウ
//    排他のフルスクリーンは出さない: このゲームの Windows は DirectX 12 で、Unity では排他を指定してもボーダーレスになる(試験で確認)
//  ・解像度: 画面が対応している大きさの一覧から(ウィンドウはデスクトップより大きい物を出さない)
//  ・表示モード/解像度は「適用」→ 10秒以内に「このままにする」を押さないと元に戻す(画面が見えなくなっても戻れる)。保存は確定した時だけ
//  ・垂直同期 / FPS 上限: すぐ反映して保存。ゲームの動きは実時間(deltaTime)で進むので、FPS で速さは変わらない(-qaFps で確認)
//  保存は SettingsSave(SaveKeys の設定)。保存が無い間は何もしない(Unity の既定 = ボーダーレス・画面の解像度・垂直同期 ON のまま)
public class DisplaySettings : MonoBehaviour
{
    public const string ModeKey = "DisplayModeV1", ResKey = "DisplayResolutionV1", VSyncKey = "VSyncV1", FpsKey = "FpsCapV1";
    public enum Mode { Fullscreen = 0, Windowed = 1 }
    public static readonly int[] FpsOptions = { 60, 120, 144, 0 }; // 0 = 無制限
    public const float ConfirmSeconds = 10f;

    public static DisplaySettings Instance { get; private set; }
    public static bool PendingConfirm { get; private set; }
    public static float ConfirmLeft => PendingConfirm ? Mathf.Max(0f, confirmUntil - Time.unscaledTime) : 0f;
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

    static FullScreenMode ToUnity(Mode m) => m == Mode.Windowed ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
    public static Mode CurrentMode => Screen.fullScreenMode == FullScreenMode.Windowed ? Mode.Windowed : Mode.Fullscreen;
    public static Vector2Int CurrentRes => new Vector2Int(Screen.width, Screen.height);

    // 起動時: 保存があれば反映(試験の起動引数 -screen-width 等の時は、保存の無い試験用データなので何もしない)
    static void ApplySaved()
    {
        bool any = false;
        if (SaveStore.HasKey(ModeKey) || SaveStore.HasKey(ResKey))
        {
            var m = (Mode)Mathf.Clamp(SaveStore.GetInt(ModeKey, (int)CurrentMode), 0, 1);
            var r = ParseRes(SaveStore.GetString(ResKey, ""), CurrentRes);
            r = ClampToDisplay(r, m);
            Screen.SetResolution(r.x, r.y, ToUnity(m));
            any = true;
            Debug.Log($"[Display] saved mode {m} {r.x}x{r.y}");
        }
        if (SaveStore.HasKey(VSyncKey) || SaveStore.HasKey(FpsKey)) { ApplyFrameRate(); any = true; }
        if (!any) Debug.Log($"[Display] no saved display settings (mode {CurrentMode} {Screen.width}x{Screen.height}, vSync {QualitySettings.vSyncCount})");
    }

    static Vector2Int ParseRes(string s, Vector2Int def)
    {
        var p = s.Split('x');
        if (p.Length == 2 && int.TryParse(p[0], out int w) && int.TryParse(p[1], out int h) && w >= 640 && h >= 360) return new Vector2Int(w, h);
        return def;
    }

    static Vector2Int DesktopSize => new Vector2Int(Display.main.systemWidth, Display.main.systemHeight);
    static Vector2Int ClampToDisplay(Vector2Int r, Mode m)
    {
        var d = DesktopSize;
        if (d.x <= 0 || d.y <= 0) return r;
        if (m == Mode.Windowed && (r.x > d.x || r.y > d.y)) { float k = Mathf.Min(d.x * 0.9f / r.x, d.y * 0.9f / r.y); r = new Vector2Int(Mathf.RoundToInt(r.x * k), Mathf.RoundToInt(r.y * k)); }
        return r;
    }

    // 選べる解像度(重複なし・小さい順)。ウィンドウではデスクトップより大きい物を除く。今の大きさは必ず入れる
    public static List<Vector2Int> Resolutions(Mode m)
    {
        var list = new List<Vector2Int>();
        var d = DesktopSize;
        foreach (var r in Screen.resolutions)
        {
            var v = new Vector2Int(r.width, r.height);
            if (v.x < 960 || v.y < 540) continue;
            if (m == Mode.Windowed && d.x > 0 && (v.x > d.x || v.y > d.y)) continue;
            if (!list.Contains(v)) list.Add(v);
        }
        if (list.Count == 0 && d.x > 0) list.Add(d);
        if (!list.Contains(CurrentRes) && CurrentRes.x >= 640) list.Add(CurrentRes);
        list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        return list;
    }

    // 表示モード/解像度を試す(確定は Keep)。10秒で戻る
    public static void Request(Mode m, Vector2Int res)
    {
        if (!PlatformCaps.SupportsWindowMode) return;
        if (!PendingConfirm) { prevMode = CurrentMode; prevRes = CurrentRes; }
        res = ClampToDisplay(res, m);
        pendMode = m; pendRes = res;
        Screen.SetResolution(res.x, res.y, ToUnity(m));
        PendingConfirm = true;
        confirmUntil = Time.unscaledTime + ConfirmSeconds;
        Debug.Log($"[Display] try {m} {res.x}x{res.y} (was {prevMode} {prevRes.x}x{prevRes.y})");
    }

    public static void Keep()
    {
        if (!PendingConfirm) return;
        PendingConfirm = false;
        SaveStore.SetInt(ModeKey, (int)pendMode);
        SaveStore.SetString(ResKey, pendRes.x + "x" + pendRes.y);
        SaveStore.Save();
        Debug.Log($"[Display] kept {pendMode} {pendRes.x}x{pendRes.y}");
    }

    public static void Revert(string why)
    {
        if (!PendingConfirm) return;
        PendingConfirm = false;
        Screen.SetResolution(prevRes.x, prevRes.y, ToUnity(prevMode));
        Debug.Log($"[Display] reverted to {prevMode} {prevRes.x}x{prevRes.y} ({why})");
    }

    void Update()
    {
        if (PendingConfirm && Time.unscaledTime >= confirmUntil) Revert("timeout");
    }

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
}
