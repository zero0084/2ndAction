using System.Collections.Generic;
using UnityEngine;

// 入力の抽象化(2026-10-06、家庭用機への移植の準備)。
//  ゲームは「行動」(GameAction)だけを見る。キーボード/ゲームパッド/(将来)Input System はそれぞれ IInputSource として足す。
//  タッチ/マウスのフリック・タップは今までどおり各画面のポインタ処理(PointerInput)が扱う(スマホの操作は一切変えない)。
//
//  差し替え口:
//   ・Switch / PS5 など: Unity の家庭用機は Input System が前提なので、Input System の IInputSource を作って
//     GameInput.SetSources(...) で入れ替える(ゲーム側のコードは変えない)。今の PC 用は旧 Input Manager の
//     ジョイスティック(ProjectSettings/InputManager.asset の "Pad LX" 等の軸)で読む。
//   ・ボタン配置は GamepadBindings の表だけで決める(下)。
public enum GameAction
{
    Jump,           // 上フリック相当(ジャンプ/上攻撃)
    AttackForward,  // 前フリック
    AttackBack,     // 後ろフリック
    AttackUp,       // 上フリック(Jump と同じ動き。ボタンを分けたい時用)
    AttackDown,     // 下フリック
    Ultimate,       // ULTIMATE ボタン
    Confirm,        // 決定
    Cancel,         // 戻る
    Pause,          // 一時停止 / 再開
    NavUp, NavDown, NavLeft, NavRight, // メニューの移動
    TabPrev, TabNext,                  // タブ/ページの切り替え
    ScrollUp, ScrollDown,              // 一覧のスクロール
    Hold,           // 長押しの操作(1000m 以降の脱出。タッチの「画面を長押し」と同じ)
    Count
}

// Mouse(2026-10-25): PC でマウスを使った時。タッチと同じくフォーカスの枠は出さない(UsingNonTouch = キーボード/パッドだけ)
public enum InputDeviceKind { Touch, Keyboard, Gamepad, Mouse }

public interface IInputSource
{
    string Name { get; }
    InputDeviceKind Kind { get; }
    // このフレームで押されているか(押した瞬間の判定は GameInput がまとめて作る)
    bool Held(GameAction a);
    // 何か操作があったか(最後に使った機器の判定)
    bool AnyActivity();
}

[DefaultExecutionOrder(-900)]
public class GameInput : MonoBehaviour
{
    public static GameInput Instance { get; private set; }

    static readonly List<IInputSource> sources = new List<IInputSource>();
    static readonly bool[] held = new bool[(int)GameAction.Count];
    static readonly bool[] prevHeld = new bool[(int)GameAction.Count];
    static readonly bool[] consumed = new bool[(int)GameAction.Count];
    static readonly float[] repeatAt = new float[(int)GameAction.Count];
    static readonly bool[] repeatFire = new bool[(int)GameAction.Count];
    static int frame = -1;

    // 最後に使った機器。タッチ/マウスを使えばフォーカスの枠を消し、パッド/キーで出す
    public static InputDeviceKind LastDevice { get; private set; } = InputDeviceKind.Touch;
    public static bool UsingNonTouch => LastDevice == InputDeviceKind.Keyboard || LastDevice == InputDeviceKind.Gamepad;
    public static string LastSourceName { get; private set; } = "";

    // 自動テスト用: 次のフレームにこの行動を押したことにする
    static readonly HashSet<GameAction> injected = new HashSet<GameAction>();
    public static void Inject(GameAction a) { injected.Add(a); }
    // 左クリック(タップ)に割り当てた行動のうち、フリックでない物(ULTIMATE など)を次のフレームに1回押したことにする(機器はそのまま)
    static readonly HashSet<GameAction> pulsed = new HashSet<GameAction>();
    public static void Pulse(GameAction a) { pulsed.Add(a); }
    public static void DebugMarkDevice(InputDeviceKind k) { LastDevice = k; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[GameInput]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<GameInput>();
        InputBindings.EnsureLoaded();
        if (sources.Count == 0) SetSources(DefaultSources());
        go.AddComponent<PadNav>();
        Debug.Log("[GameInput] " + PlatformCaps.Describe());
    }

    // 機種ごとの入力元。家庭用機ではここを差し替える。割り当ては InputBindings の表(PC はキー設定、ほかは今までの固定の表)
    static IEnumerable<IInputSource> DefaultSources()
    {
        yield return new KeyboardInputSource();
        if (PlatformCaps.SupportsMouse) yield return new MouseInputSource();
        yield return new LegacyGamepadInputSource();
    }

    public static void SetSources(IEnumerable<IInputSource> list)
    {
        sources.Clear();
        sources.AddRange(list);
    }

    void Update() { Refresh(); }

    static void Refresh()
    {
        if (frame == Time.frameCount) return;
        frame = Time.frameCount;
        // タッチ: 画面に指。マウスのある環境(PC)ではマウスの操作は Mouse(2026-10-25)。マウスの無い環境は今までどおりクリックもタッチ扱い
        bool touch = Input.touchCount > 0;
        bool mouse = false;
        if (!PlatformCaps.SupportsMouse) touch |= Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
        else if (!touch) mouse = MouseActivity();
        for (int i = 0; i < held.Length; i++) { prevHeld[i] = held[i]; held[i] = false; consumed[i] = false; repeatFire[i] = false; }
        foreach (var s in sources)
        {
            bool any = false;
            try
            {
                for (int i = 0; i < held.Length; i++) if (s.Held((GameAction)i)) { held[i] = true; any = true; }
                any |= s.AnyActivity();
            }
            catch (System.Exception e) { Debug.LogWarning($"[GameInput] source {s.Name} failed: {e.Message}"); }
            if (any) { LastDevice = s.Kind; LastSourceName = s.Name; }
        }
        foreach (var a in injected) { held[(int)a] = true; prevHeld[(int)a] = false; LastDevice = InputDeviceKind.Gamepad; LastSourceName = "inject"; }
        injected.Clear();
        foreach (var a in pulsed) { held[(int)a] = true; prevHeld[(int)a] = false; }
        pulsed.Clear();
        if (touch) LastDevice = InputDeviceKind.Touch;
        else if (mouse) { LastDevice = InputDeviceKind.Mouse; LastSourceName = "mouse"; }
        // 押し続けた時の繰り返し(メニューの移動/スクロール)
        float now = Time.unscaledTime;
        for (int i = 0; i < held.Length; i++)
        {
            if (held[i] && !prevHeld[i]) { repeatFire[i] = true; repeatAt[i] = now + 0.38f; }
            else if (held[i] && now >= repeatAt[i]) { repeatFire[i] = true; repeatAt[i] = now + 0.09f; }
        }
    }

    public static bool Held(GameAction a) { Refresh(); return held[(int)a]; }
    // 押した瞬間(このフレームだけ)
    public static bool Down(GameAction a) { Refresh(); return held[(int)a] && !prevHeld[(int)a]; }
    // 押した瞬間 + 押し続けた時の繰り返し(メニューの移動用)
    public static bool DownRepeat(GameAction a) { Refresh(); return repeatFire[(int)a]; }
    // 押した瞬間を1か所だけで使う(同じフレームに2つの画面が反応しない)
    public static bool Consume(GameAction a)
    {
        Refresh();
        int i = (int)a;
        if (!held[i] || prevHeld[i] || consumed[i]) return false;
        consumed[i] = true;
        return true;
    }
    public static bool IsConsumed(GameAction a) { Refresh(); return consumed[(int)a]; }

    // マウスを使ったか(PC): ボタン/ホイール、または少し大きく動かした(机の振動くらいでは切り替えない)
    static float mouseMoveAccum, mouseMoveSince;
    static Vector3 lastMousePos;
    static bool MouseActivity()
    {
        if (!Input.mousePresent) return false;
        for (int b = 0; b < 7; b++) if (Input.GetMouseButtonDown(b)) return true;
        if (Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f) return true;
        Vector3 p = Input.mousePosition;
        float d = (p - lastMousePos).magnitude;
        lastMousePos = p;
        if (LastDevice == InputDeviceKind.Mouse || d > 400f) { mouseMoveAccum = 0f; return false; } // 窓の外から戻った時の飛びは数えない
        float now = Time.unscaledTime;
        if (now - mouseMoveSince > 0.5f) { mouseMoveAccum = 0f; mouseMoveSince = now; }
        mouseMoveAccum += d;
        return mouseMoveAccum > 40f;
    }
}

// ================================================================= 割り当て(2026-10-25 から InputBindings の表を読む)
//  PC の初期配置(キー設定で変えられる): ジャンプ Space/右クリック/A、上攻撃 W・↑/ホイール上/右スティック↑、前 D・→/左クリック/X・右スティック→、
//   後ろ A・←/マウス4/Y・右スティック←、下 S・↓/ホイール下/B・右スティック↓、ULTIMATE C/中クリック/RB・RT、脱出 H/BACK、一時停止 P/START
//  メニュー: 決定 Space/A(Enter は固定)、戻る Backspace/B(Esc は固定)、移動 WASD(矢印・左スティック・十字キーは固定)、タブ Q・E/LB・RB、スクロール PgUp・PgDn/右スティック上下
//  Android など(キー設定なし): 2026-10-06 からの固定の表のまま(InputBindings.SetLegacy)
public static class GamepadBindings
{
    public const KeyCode A = KeyCode.JoystickButton0, B = KeyCode.JoystickButton1, X = KeyCode.JoystickButton2, Y = KeyCode.JoystickButton3;
    public const KeyCode LB = KeyCode.JoystickButton4, RB = KeyCode.JoystickButton5, Back = KeyCode.JoystickButton6, Start = KeyCode.JoystickButton7;
    public const float StickThreshold = 0.55f;
}

public class KeyboardInputSource : IInputSource
{
    public string Name => "keyboard";
    public InputDeviceKind Kind => InputDeviceKind.Keyboard;
    public bool Held(GameAction a) => InputBindings.KeyboardHeld(a);
    // Esc は Android の「戻る」でもあるので、機器の切り替えには数えない(スマホで戻るを押してもフォーカスの枠を出さない)。マウスのボタンも数えない
    public bool AnyActivity() => Input.anyKeyDown && !MouseKeyDown() && !Input.GetKeyDown(KeyCode.Escape) && !PadKeyDown();
    static bool PadKeyDown() { for (int i = 0; i < 12; i++) if (Input.GetKeyDown(KeyCode.JoystickButton0 + i)) return true; return false; }
    static bool MouseKeyDown() { for (int i = 0; i < 7; i++) if (Input.GetMouseButtonDown(i)) return true; return false; }
}

// マウスのボタン(右/中/4/5…)とホイール(PC だけ。左クリックはタッチと同じ道 = PlayerController)
public class MouseInputSource : IInputSource
{
    public string Name => "mouse";
    public InputDeviceKind Kind => InputDeviceKind.Mouse;
    public bool Held(GameAction a) => InputBindings.MouseHeld(a);
    public bool AnyActivity() => false; // 機器の切り替えは GameInput.MouseActivity(動かしただけでも)
}

// 旧 Input Manager のジョイスティック(PC の XInput パッド)。軸は InputManager.asset の "Pad *"
public class LegacyGamepadInputSource : IInputSource
{
    public string Name => "gamepad";
    public InputDeviceKind Kind => InputDeviceKind.Gamepad;
    public bool Held(GameAction a) => InputBindings.PadHeld(a);
    public bool AnyActivity() => InputBindings.PadAnyActivity();
}
