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

public enum InputDeviceKind { Touch, Keyboard, Gamepad }

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
    public static bool UsingNonTouch => LastDevice != InputDeviceKind.Touch;
    public static string LastSourceName { get; private set; } = "";

    // 自動テスト用: 次のフレームにこの行動を押したことにする
    static readonly HashSet<GameAction> injected = new HashSet<GameAction>();
    public static void Inject(GameAction a) { injected.Add(a); }
    public static void DebugMarkDevice(InputDeviceKind k) { LastDevice = k; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[GameInput]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<GameInput>();
        if (sources.Count == 0) SetSources(DefaultSources());
        go.AddComponent<PadNav>();
    }

    // 機種ごとの入力元。家庭用機ではここを差し替える
    static IEnumerable<IInputSource> DefaultSources()
    {
        yield return new KeyboardInputSource();
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
        bool touch = Input.touchCount > 0 || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
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
        if (touch) LastDevice = InputDeviceKind.Touch;
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
}

// ================================================================= ボタン配置(仮。Xbox 配置の表記。PS: A=×, B=○, X=□, Y=△)
//  ラン中: A=ジャンプ(上) / X=前攻撃 / Y=後ろ攻撃 / B=下攻撃 / 右スティック=フリックと同じ4方向 / RB(または RT)=ULTIMATE / START=一時停止
//          BACK(View)長押し=脱出(1000m 以降)
//  メニュー: 左スティック・十字キー=移動 / A=決定 / B=戻る / LB・RB=タブ / 右スティック上下=スクロール / START=一時停止
//  キーボード: 矢印/WASD=移動 / Enter・Space=決定 / Esc・Backspace=戻る / P=一時停止 / Q・E=タブ
//              ラン中: Space・W・↑=ジャンプ / Z・→・D=前 / B・X・←・A=後ろ / S・↓=下 / C=ULTIMATE / H 長押し=脱出(今までのキーも残す)
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
    static bool K(KeyCode k) => Input.GetKey(k);

    public bool Held(GameAction a)
    {
        switch (a)
        {
            case GameAction.Jump: return K(KeyCode.Space) || K(KeyCode.W) || K(KeyCode.UpArrow);
            case GameAction.AttackForward: return K(KeyCode.Z) || K(KeyCode.D) || K(KeyCode.RightArrow);
            case GameAction.AttackBack: return K(KeyCode.B) || K(KeyCode.X) || K(KeyCode.A) || K(KeyCode.LeftArrow);
            case GameAction.AttackUp: return false;
            case GameAction.AttackDown: return K(KeyCode.S) || K(KeyCode.DownArrow);
            case GameAction.Ultimate: return K(KeyCode.C);
            case GameAction.Confirm: return K(KeyCode.Return) || K(KeyCode.KeypadEnter) || K(KeyCode.Space);
            case GameAction.Cancel: return K(KeyCode.Escape) || K(KeyCode.Backspace);
            case GameAction.Pause: return K(KeyCode.P);
            case GameAction.NavUp: return K(KeyCode.UpArrow) || K(KeyCode.W);
            case GameAction.NavDown: return K(KeyCode.DownArrow) || K(KeyCode.S);
            case GameAction.NavLeft: return K(KeyCode.LeftArrow) || K(KeyCode.A);
            case GameAction.NavRight: return K(KeyCode.RightArrow) || K(KeyCode.D);
            case GameAction.TabPrev: return K(KeyCode.Q);
            case GameAction.TabNext: return K(KeyCode.E);
            case GameAction.ScrollUp: return K(KeyCode.PageUp);
            case GameAction.ScrollDown: return K(KeyCode.PageDown);
            case GameAction.Hold: return K(KeyCode.H);
        }
        return false;
    }
    // Esc は Android の「戻る」でもあるので、機器の切り替えには数えない(スマホで戻るを押してもフォーカスの枠を出さない)
    public bool AnyActivity() => Input.anyKeyDown && !Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1) && !Input.GetKeyDown(KeyCode.Escape) && !PadKeyDown();
    static bool PadKeyDown() { for (int i = 0; i < 12; i++) if (Input.GetKeyDown(KeyCode.JoystickButton0 + i)) return true; return false; }
}

// 旧 Input Manager のジョイスティック(PC の XInput パッド)。軸は InputManager.asset の "Pad *"
public class LegacyGamepadInputSource : IInputSource
{
    public string Name => "gamepad";
    public InputDeviceKind Kind => InputDeviceKind.Gamepad;
    bool axesOk = true;
    float lx, ly, rx, ry, dx, dy, lt, rt;
    int frame = -1;

    float Axis(string n)
    {
        if (!axesOk) return 0f;
        try { return Input.GetAxisRaw(n); }
        catch (System.ArgumentException) { axesOk = false; Debug.LogWarning("[GameInput] gamepad axes are missing in InputManager.asset"); return 0f; }
    }
    void Read()
    {
        if (frame == Time.frameCount) return;
        frame = Time.frameCount;
        lx = Axis("Pad LX"); ly = Axis("Pad LY"); rx = Axis("Pad RX"); ry = Axis("Pad RY");
        dx = Axis("Pad DX"); dy = Axis("Pad DY"); lt = Axis("Pad LT"); rt = Axis("Pad RT");
    }
    static bool K(KeyCode k) => Input.GetKey(k);
    const float T = GamepadBindings.StickThreshold;

    public bool Held(GameAction a)
    {
        Read();
        switch (a)
        {
            case GameAction.Jump: return K(GamepadBindings.A) || ry > T;
            case GameAction.AttackForward: return K(GamepadBindings.X) || rx > T;
            case GameAction.AttackBack: return K(GamepadBindings.Y) || rx < -T;
            case GameAction.AttackUp: return false;
            case GameAction.AttackDown: return K(GamepadBindings.B) || ry < -T;
            case GameAction.Ultimate: return K(GamepadBindings.RB) || rt > 0.5f;
            case GameAction.Confirm: return K(GamepadBindings.A);
            case GameAction.Cancel: return K(GamepadBindings.B);
            case GameAction.Pause: return K(GamepadBindings.Start);
            case GameAction.NavUp: return ly > T || dy > 0.5f;
            case GameAction.NavDown: return ly < -T || dy < -0.5f;
            case GameAction.NavLeft: return lx < -T || dx < -0.5f;
            case GameAction.NavRight: return lx > T || dx > 0.5f;
            case GameAction.TabPrev: return K(GamepadBindings.LB);
            case GameAction.TabNext: return K(GamepadBindings.RB);
            case GameAction.ScrollUp: return ry > T;
            case GameAction.ScrollDown: return ry < -T;
            case GameAction.Hold: return K(GamepadBindings.Back);
        }
        return false;
    }
    public bool AnyActivity()
    {
        Read();
        for (int i = 0; i < 12; i++) if (Input.GetKey(KeyCode.JoystickButton0 + i)) return true;
        return Mathf.Abs(lx) > T || Mathf.Abs(ly) > T || Mathf.Abs(rx) > T || Mathf.Abs(ry) > T || Mathf.Abs(dx) > 0.5f || Mathf.Abs(dy) > 0.5f;
    }
}
