using UnityEngine;

// その環境が何に対応しているか(2026-10-25、Android/Windows の操作・設定の整理)。
//  設定画面の項目の出し分けと、キー設定を使うかどうかは OS の名前ではなくここを見る(将来 Steam Deck / 家庭用機を足す時はここだけ決める)。
//   ・Android: タッチ / 画面の向き / 振動(振動の中身は Platform.Haptics。今は何もしない)
//   ・Windows(PC): キーボード / マウス / ゲームパッド / キー設定 / 表示モード / 解像度 / 垂直同期・FPS 上限。タッチ画面の PC ではタッチも
//  開発版だけ -caps android|windows で見せ方を切り替えられる(Windows で Android の設定画面を確かめる用。入力の仕組み自体は変えない)
public static class PlatformCaps
{
    public static bool SupportsTouch { get; private set; }
    public static bool SupportsKeyboard { get; private set; }
    public static bool SupportsMouse { get; private set; }
    public static bool SupportsGamepad { get; private set; }
    public static bool SupportsKeyRebind { get; private set; }     // キー設定(保存した割り当てを使う)
    public static bool SupportsWindowMode { get; private set; }    // フルスクリーン / ボーダーレス / ウィンドウ
    public static bool SupportsResolutionChange { get; private set; }
    public static bool SupportsFrameRateSettings { get; private set; } // 垂直同期 / FPS 上限
    public static bool SupportsVibration { get; private set; }
    public static bool SupportsOrientation { get; private set; }   // 画面の向き(横/縦/自動)・縦画面のラン表示
    public static string Profile { get; private set; } = "";

    static bool ready;
    public static void EnsureReady() { if (!ready) Detect(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Detect()
    {
        ready = true;
        string profile = Application.isMobilePlatform ? "mobile" : "pc";
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-caps") profile = a[i + 1] == "android" ? "mobile" : "pc";
#endif
        Profile = profile;
        if (profile == "mobile")
        {
            SupportsTouch = true; SupportsVibration = true; SupportsOrientation = true;
            // Bluetooth のキーボード/パッドは今までどおり使える(割り当ては今までの固定の表)。キー設定は出さない
            SupportsKeyboard = false; SupportsMouse = false; SupportsGamepad = false; SupportsKeyRebind = false;
            SupportsWindowMode = false; SupportsResolutionChange = false; SupportsFrameRateSettings = false;
        }
        else
        {
            SupportsKeyboard = true; SupportsMouse = true; SupportsGamepad = true; SupportsKeyRebind = true;
            SupportsWindowMode = true; SupportsResolutionChange = true; SupportsFrameRateSettings = true;
            SupportsTouch = Input.touchSupported; SupportsVibration = false; SupportsOrientation = false;
        }
    }

    public static string Describe() =>
        $"profile={Profile} touch={SupportsTouch} keyboard={SupportsKeyboard} mouse={SupportsMouse} gamepad={SupportsGamepad} rebind={SupportsKeyRebind} " +
        $"window={SupportsWindowMode} resolution={SupportsResolutionChange} fps={SupportsFrameRateSettings} vibration={SupportsVibration} orientation={SupportsOrientation}";
}
