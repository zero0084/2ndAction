using System.Collections.Generic;
using System.Text;
using UnityEngine;

// キー/マウス/ゲームパッドの割り当て(2026-10-25、Android/Windows の操作・設定の整理)。
//  ゲームは GameAction だけを見る(GameInput)。ここは「どの入力がどの GameAction か」の表だけを持つ。
//   ・PC(PlatformCaps.SupportsKeyRebind): 初期の表 + 設定 → 操作 → キー設定 で変えた物(SettingsSave: InputBindingsV1)
//   ・Android など: 今までの固定の表のまま(Bluetooth のキーボード/パッドの割り当ても変えない。PC のキー設定は読まない)
//  1つの行動に キー2つ + マウス1つ + パッド2つ。ほかに「いつでも使える固定の入力」(Esc=戻る/一時停止、Enter=決定、矢印=メニューの移動、
//  左スティック/十字キー=メニューの移動)があり、キー設定をどう変えても設定画面へ戻れる。
//  左クリックはタッチと同じ道(PlayerController のフリック/タップ)を通る: ドラッグ=フリック(固定)、クリック=左クリックに割り当てた行動(離した時)
public enum BindGroup { Run, Menu }

public static class InputBindings
{
    public const string PrefKey = "InputBindingsV1";
    public const int KeySlots = 2, MouseSlot = 2, PadSlots = 2; // keys[a][0..1] = キーボード、keys[a][2] = マウス、pad[a][0..1]

    // KeyCode 以外の入力(保存する番号なので変えない)
    public const int WheelUp = 2001, WheelDown = 2002;
    public const int RSUp = 2101, RSDown = 2102, RSLeft = 2103, RSRight = 2104, LSUp = 2105, LSDown = 2106, LSLeft = 2107, LSRight = 2108;
    public const int LT = 2109, RT = 2110, DUp = 2111, DDown = 2112, DLeft = 2113, DRight = 2114;
    const float StickT = GamepadBindings.StickThreshold;

    public struct Row
    {
        public GameAction action; public BindGroup group; public string name; public bool mouse;
        public Row(GameAction a, BindGroup g, string n, bool m) { action = a; group = g; name = n; mouse = m; }
    }
    // キー設定の画面に出す行(この順)。名前は Loc のキー
    public static readonly Row[] Rows =
    {
        new Row(GameAction.Jump, BindGroup.Run, "ジャンプ", true),
        new Row(GameAction.AttackUp, BindGroup.Run, "上攻撃", true),
        new Row(GameAction.AttackForward, BindGroup.Run, "前攻撃", true),
        new Row(GameAction.AttackBack, BindGroup.Run, "後攻撃", true),
        new Row(GameAction.AttackDown, BindGroup.Run, "下攻撃", true),
        new Row(GameAction.Ultimate, BindGroup.Run, "ULTIMATE", true),
        new Row(GameAction.Hold, BindGroup.Run, "脱出(長押し)", true),
        new Row(GameAction.Pause, BindGroup.Run, "一時停止", true),
        new Row(GameAction.Confirm, BindGroup.Menu, "決定", false),
        new Row(GameAction.Cancel, BindGroup.Menu, "戻る", false),
        new Row(GameAction.NavUp, BindGroup.Menu, "上へ移動", false),
        new Row(GameAction.NavDown, BindGroup.Menu, "下へ移動", false),
        new Row(GameAction.NavLeft, BindGroup.Menu, "左へ移動", false),
        new Row(GameAction.NavRight, BindGroup.Menu, "右へ移動", false),
        new Row(GameAction.TabPrev, BindGroup.Menu, "前のタブ", false),
        new Row(GameAction.TabNext, BindGroup.Menu, "次のタブ", false),
    };
    public static bool IsRebindable(GameAction a) { foreach (var r in Rows) if (r.action == a) return true; return false; }
    public static Row RowOf(GameAction a) { foreach (var r in Rows) if (r.action == a) return r; return new Row(a, BindGroup.Menu, a.ToString(), false); }

    const int N = (int)GameAction.Count;
    static int[][] keys = new int[N][], pad = new int[N][];
    static readonly int[][] fixedKeys = new int[N][], fixedPad = new int[N][];
    public static bool Rebindable { get; private set; }   // PC の表(保存した割り当て)を使っている
    public static int Version { get; private set; }       // 変わるたびに増える(操作の案内の作り直し用)

    static int K(KeyCode k) => (int)k;
    static int[] A(params int[] v) => v;

    static InputBindings()
    {
        for (int i = 0; i < N; i++) { fixedKeys[i] = new int[0]; fixedPad[i] = new int[0]; }
        // いつでも使える固定の入力(キー設定で消せない。戻れなくならないように)
        fixedKeys[(int)GameAction.Confirm] = A(K(KeyCode.Return), K(KeyCode.KeypadEnter));
        fixedKeys[(int)GameAction.Cancel] = A(K(KeyCode.Escape));
        fixedKeys[(int)GameAction.NavUp] = A(K(KeyCode.UpArrow));
        fixedKeys[(int)GameAction.NavDown] = A(K(KeyCode.DownArrow));
        fixedKeys[(int)GameAction.NavLeft] = A(K(KeyCode.LeftArrow));
        fixedKeys[(int)GameAction.NavRight] = A(K(KeyCode.RightArrow));
        fixedKeys[(int)GameAction.ScrollUp] = A(K(KeyCode.PageUp));
        fixedKeys[(int)GameAction.ScrollDown] = A(K(KeyCode.PageDown));
        fixedPad[(int)GameAction.NavUp] = A(LSUp, DUp);
        fixedPad[(int)GameAction.NavDown] = A(LSDown, DDown);
        fixedPad[(int)GameAction.NavLeft] = A(LSLeft, DLeft);
        fixedPad[(int)GameAction.NavRight] = A(LSRight, DRight);
        fixedPad[(int)GameAction.ScrollUp] = A(RSUp);
        fixedPad[(int)GameAction.ScrollDown] = A(RSDown);
        SetLegacy();
    }

    // ---------------------------------------------------------------- 表
    // 今までの固定の表(2026-10-06〜。Android と、キー設定の無い環境)。固定の入力と合わせて、以前の KeyboardInputSource / LegacyGamepadInputSource と同じ
    static void SetLegacy()
    {
        for (int i = 0; i < N; i++) { keys[i] = new int[0]; pad[i] = new int[0]; }
        void S(GameAction a, int[] k, int[] p) { keys[(int)a] = k; pad[(int)a] = p; }
        S(GameAction.Jump, A(K(KeyCode.Space), K(KeyCode.W), K(KeyCode.UpArrow)), A(K(GamepadBindings.A), RSUp));
        S(GameAction.AttackForward, A(K(KeyCode.Z), K(KeyCode.D), K(KeyCode.RightArrow)), A(K(GamepadBindings.X), RSRight));
        S(GameAction.AttackBack, A(K(KeyCode.B), K(KeyCode.X), K(KeyCode.A), K(KeyCode.LeftArrow)), A(K(GamepadBindings.Y), RSLeft));
        S(GameAction.AttackDown, A(K(KeyCode.S), K(KeyCode.DownArrow)), A(K(GamepadBindings.B), RSDown));
        S(GameAction.Ultimate, A(K(KeyCode.C)), A(K(GamepadBindings.RB), RT));
        S(GameAction.Confirm, A(K(KeyCode.Space)), A(K(GamepadBindings.A)));
        S(GameAction.Cancel, A(K(KeyCode.Backspace)), A(K(GamepadBindings.B)));
        S(GameAction.Pause, A(K(KeyCode.P)), A(K(GamepadBindings.Start)));
        S(GameAction.NavUp, A(K(KeyCode.W)), A());
        S(GameAction.NavDown, A(K(KeyCode.S)), A());
        S(GameAction.NavLeft, A(K(KeyCode.A)), A());
        S(GameAction.NavRight, A(K(KeyCode.D)), A());
        S(GameAction.TabPrev, A(K(KeyCode.Q)), A(K(GamepadBindings.LB)));
        S(GameAction.TabNext, A(K(KeyCode.E)), A(K(GamepadBindings.RB)));
        S(GameAction.Hold, A(K(KeyCode.H)), A(K(GamepadBindings.Back)));
    }

    // PC の初期の表: キー1 / キー2 / マウス、パッド1 / パッド2(0 = なし)
    //  WASD + Space(左手)とマウス(右手)、または矢印キーだけでも遊べる。上攻撃はジャンプと同じ動き(地上では上へ攻撃しながら跳ぶ)
    static void SetPcDefaults()
    {
        for (int i = 0; i < N; i++) { keys[i] = new int[KeySlots + 1]; pad[i] = new int[PadSlots]; }
        void S(GameAction a, int k0, int k1, int m, int p0, int p1) { keys[(int)a] = A(k0, k1, m); pad[(int)a] = A(p0, p1); }
        S(GameAction.Jump, K(KeyCode.Space), 0, K(KeyCode.Mouse1), K(GamepadBindings.A), 0);
        S(GameAction.AttackUp, K(KeyCode.W), K(KeyCode.UpArrow), WheelUp, RSUp, 0);
        S(GameAction.AttackForward, K(KeyCode.D), K(KeyCode.RightArrow), K(KeyCode.Mouse0), K(GamepadBindings.X), RSRight);
        S(GameAction.AttackBack, K(KeyCode.A), K(KeyCode.LeftArrow), K(KeyCode.Mouse3), K(GamepadBindings.Y), RSLeft);
        S(GameAction.AttackDown, K(KeyCode.S), K(KeyCode.DownArrow), WheelDown, K(GamepadBindings.B), RSDown);
        S(GameAction.Ultimate, K(KeyCode.C), 0, K(KeyCode.Mouse2), K(GamepadBindings.RB), RT);
        S(GameAction.Hold, K(KeyCode.H), 0, 0, K(GamepadBindings.Back), 0);
        S(GameAction.Pause, K(KeyCode.P), 0, 0, K(GamepadBindings.Start), 0);
        S(GameAction.Confirm, K(KeyCode.Space), 0, 0, K(GamepadBindings.A), 0);
        S(GameAction.Cancel, K(KeyCode.Backspace), 0, 0, K(GamepadBindings.B), 0);
        S(GameAction.NavUp, K(KeyCode.W), 0, 0, 0, 0);
        S(GameAction.NavDown, K(KeyCode.S), 0, 0, 0, 0);
        S(GameAction.NavLeft, K(KeyCode.A), 0, 0, 0, 0);
        S(GameAction.NavRight, K(KeyCode.D), 0, 0, 0, 0);
        S(GameAction.TabPrev, K(KeyCode.Q), 0, 0, K(GamepadBindings.LB), 0);
        S(GameAction.TabNext, K(KeyCode.E), 0, 0, K(GamepadBindings.RB), 0);
    }

    static bool loaded;
    public static void EnsureLoaded() { if (!loaded) Load(); }

    public static void Load()
    {
        loaded = true;
        PlatformCaps.EnsureReady();
        Rebindable = PlatformCaps.SupportsKeyRebind;
        if (!Rebindable) { SetLegacy(); Version++; return; }
        SetPcDefaults();
        string s = SaveStore.GetString(PrefKey, "");
        if (!string.IsNullOrEmpty(s) && !Parse(s)) { Debug.LogWarning("[InputBindings] saved bindings could not be read; using the defaults"); SetPcDefaults(); }
        Version++;
        Debug.Log("[InputBindings] " + (string.IsNullOrEmpty(s) ? "defaults" : "saved") + " " + Serialize());
    }

    public static void ResetToDefaults()
    {
        if (!Rebindable) return;
        SetPcDefaults();
        SaveStore.DeleteKey(PrefKey);
        SaveStore.Save();
        Version++;
        Debug.Log("[InputBindings] reset to defaults");
    }

    public static void Save()
    {
        if (!Rebindable) return;
        SaveStore.SetString(PrefKey, Serialize());
        SaveStore.Save();
    }

    // 形: v1|Jump=32,0,324/330,0|AttackUp=...(キー1,キー2,マウス/パッド1,パッド2)
    public static string Serialize()
    {
        var sb = new StringBuilder("v1");
        foreach (var r in Rows)
        {
            int i = (int)r.action;
            sb.Append('|').Append(r.action).Append('=');
            for (int j = 0; j < keys[i].Length; j++) { if (j > 0) sb.Append(','); sb.Append(keys[i][j]); }
            sb.Append('/');
            for (int j = 0; j < pad[i].Length; j++) { if (j > 0) sb.Append(','); sb.Append(pad[i][j]); }
        }
        return sb.ToString();
    }

    static bool Parse(string s)
    {
        var parts = s.Split('|');
        if (parts.Length < 2 || parts[0] != "v1") return false;
        for (int p = 1; p < parts.Length; p++)
        {
            int eq = parts[p].IndexOf('=');
            if (eq <= 0) return false;
            if (!System.Enum.TryParse(parts[p].Substring(0, eq), out GameAction a) || !IsRebindable(a)) continue; // 知らない行は飛ばす(古い/新しい版)
            var kp = parts[p].Substring(eq + 1).Split('/');
            if (kp.Length != 2) return false;
            var k = ParseInts(kp[0], KeySlots + 1); var pd = ParseInts(kp[1], PadSlots);
            if (k == null || pd == null) return false;
            keys[(int)a] = k; pad[(int)a] = pd;
        }
        return true;
    }

    static int[] ParseInts(string s, int len)
    {
        var v = s.Split(',');
        if (v.Length != len) return null;
        var r = new int[len];
        for (int i = 0; i < len; i++) if (!int.TryParse(v[i], out r[i]) || r[i] < 0) return null;
        return r;
    }

    // ---------------------------------------------------------------- 読む
    public static int[] Keys(GameAction a) => keys[(int)a];
    public static int[] Pad(GameAction a) => pad[(int)a];
    public static int[] FixedKeys(GameAction a) => fixedKeys[(int)a];
    public static int[] FixedPad(GameAction a) => fixedPad[(int)a];

    public static bool IsKeyboardCode(int c) => c > 0 && c < (int)KeyCode.Mouse0;
    public static bool IsMouseCode(int c) => (c >= (int)KeyCode.Mouse0 && c <= (int)KeyCode.Mouse6) || c == WheelUp || c == WheelDown;
    public static bool IsPadCode(int c) => (c >= (int)KeyCode.JoystickButton0 && c <= (int)KeyCode.JoystickButton19) || (c >= RSUp && c <= DRight);

    // 自動テスト用: 押されていることにする入力(本物の割り当ての表を通す)
    static readonly HashSet<int> debugHeld = new HashSet<int>();
    public static void DebugPress(int code) { debugHeld.Add(code); }
    public static void DebugRelease(int code) { debugHeld.Remove(code); }
    public static void DebugReleaseAll() { debugHeld.Clear(); }

    static int axFrame = -1;
    static bool axesOk = true;
    static float lx, ly, rx, ry, dx, dy, lt, rt, wheel;
    static float Axis(string n)
    {
        if (!axesOk) return 0f;
        try { return Input.GetAxisRaw(n); }
        catch (System.ArgumentException) { axesOk = false; Debug.LogWarning("[GameInput] gamepad axes are missing in InputManager.asset"); return 0f; }
    }
    static void ReadAxes()
    {
        if (axFrame == Time.frameCount) return;
        axFrame = Time.frameCount;
        lx = Axis("Pad LX"); ly = Axis("Pad LY"); rx = Axis("Pad RX"); ry = Axis("Pad RY");
        dx = Axis("Pad DX"); dy = Axis("Pad DY"); lt = Axis("Pad LT"); rt = Axis("Pad RT");
        wheel = Input.mouseScrollDelta.y;
    }

    public static bool CodeHeld(int c)
    {
        if (c <= 0) return false;
        if (debugHeld.Count > 0 && debugHeld.Contains(c)) return true;
        if (c < 2000) return Input.GetKey((KeyCode)c);
        ReadAxes();
        switch (c)
        {
            case WheelUp: return wheel > 0.01f;
            case WheelDown: return wheel < -0.01f;
            case RSUp: return ry > StickT;
            case RSDown: return ry < -StickT;
            case RSLeft: return rx < -StickT;
            case RSRight: return rx > StickT;
            case LSUp: return ly > StickT;
            case LSDown: return ly < -StickT;
            case LSLeft: return lx < -StickT;
            case LSRight: return lx > StickT;
            case LT: return lt > 0.5f;
            case RT: return rt > 0.5f;
            case DUp: return dy > 0.5f;
            case DDown: return dy < -0.5f;
            case DLeft: return dx < -0.5f;
            case DRight: return dx > 0.5f;
        }
        return false;
    }

    // その行動に割り当てたキーボードのキー(固定の物も)が押されているか
    public static bool KeyboardHeld(GameAction a)
    {
        foreach (int c in keys[(int)a]) if (IsKeyboardCode(c) && CodeHeld(c)) return true;
        foreach (int c in fixedKeys[(int)a]) if (CodeHeld(c)) return true;
        return false;
    }
    // マウス(左クリックは PlayerController のタップ/フリックの道を通るのでここでは見ない)
    public static bool MouseHeld(GameAction a)
    {
        foreach (int c in keys[(int)a]) if (IsMouseCode(c) && c != (int)KeyCode.Mouse0 && CodeHeld(c)) return true;
        return false;
    }
    public static bool PadHeld(GameAction a)
    {
        foreach (int c in pad[(int)a]) if (CodeHeld(c)) return true;
        foreach (int c in fixedPad[(int)a]) if (CodeHeld(c)) return true;
        return false;
    }
    public static bool PadAnyActivity()
    {
        ReadAxes();
        if (debugHeld.Count > 0) foreach (int c in debugHeld) if (IsPadCode(c)) return true;
        for (int i = 0; i < 12; i++) if (Input.GetKey(KeyCode.JoystickButton0 + i)) return true;
        return Mathf.Abs(lx) > StickT || Mathf.Abs(ly) > StickT || Mathf.Abs(rx) > StickT || Mathf.Abs(ry) > StickT || Mathf.Abs(dx) > 0.5f || Mathf.Abs(dy) > 0.5f;
    }

    // 左クリック(タップ)に割り当てた行動(無ければ null)。PC の表の時だけ。Android は今までどおり(タップ=前)
    public static GameAction? Mouse0Action()
    {
        if (!Rebindable) return GameAction.AttackForward;
        foreach (var r in Rows) foreach (int c in keys[(int)r.action]) if (c == (int)KeyCode.Mouse0) return r.action;
        return null;
    }

    // ---------------------------------------------------------------- 変える(キー設定の画面)
    public enum Column { Key0, Key1, Mouse, Pad0, Pad1 }
    public static bool IsPadColumn(Column c) => c == Column.Pad0 || c == Column.Pad1;
    static int[] Slots(GameAction a, Column col) => IsPadColumn(col) ? pad[(int)a] : keys[(int)a];
    static int SlotIndex(Column col) => col == Column.Key0 ? 0 : col == Column.Key1 ? 1 : col == Column.Mouse ? 2 : col == Column.Pad0 ? 0 : 1;
    public static int Get(GameAction a, Column col) { var s = Slots(a, col); int i = SlotIndex(col); return i < s.Length ? s[i] : 0; }

    public struct Conflict { public bool any; public GameAction action; public bool isFixed; public Column column; }

    // 同じ場面(ラン中/メニュー)・同じ種類(キーボードとマウス / パッド)の中で、ほかの行動がもう使っているか
    public static Conflict FindConflict(GameAction a, Column col, int code)
    {
        var g = RowOf(a).group;
        bool padCol = IsPadColumn(col);
        foreach (var r in Rows)
        {
            if (r.group != g) continue;
            int i = (int)r.action;
            foreach (int c in padCol ? fixedPad[i] : fixedKeys[i]) if (c == code) return new Conflict { any = true, action = r.action, isFixed = true, column = col };
            if (r.action == a) continue;
            var cols = padCol ? new[] { Column.Pad0, Column.Pad1 } : new[] { Column.Key0, Column.Key1, Column.Mouse };
            foreach (var cc in cols) if (Get(r.action, cc) == code) return new Conflict { any = true, action = r.action, isFixed = false, column = cc };
        }
        return new Conflict();
    }

    // 割り当てる。swap=true なら、使っていた行動へこの欄の前の入力を渡す(入れ替え)。同じ行動のほかの欄に同じ物があればその欄と入れ替える
    public static bool Assign(GameAction a, Column col, int code, bool swap)
    {
        if (!Rebindable) return false;
        var slots = Slots(a, col); int idx = SlotIndex(col);
        if (idx >= slots.Length) return false;
        int old = slots[idx];
        if (old == code) return true;
        var cf = FindConflict(a, col, code);
        if (cf.any)
        {
            if (cf.isFixed || !swap) return false;
            Slots(cf.action, cf.column)[SlotIndex(cf.column)] = old;
        }
        // 同じ行動のほかの欄
        var other = IsPadColumn(col) ? new[] { Column.Pad0, Column.Pad1 } : new[] { Column.Key0, Column.Key1, Column.Mouse };
        foreach (var oc in other) if (oc != col && Get(a, oc) == code) Slots(a, oc)[SlotIndex(oc)] = (IsMouseCode(old) == (oc == Column.Mouse)) ? old : 0;
        slots[idx] = code;
        Version++;
        Save();
        Debug.Log($"[InputBindings] {a} {col} = {CodeName(code)} (was {CodeName(old)}){(cf.any ? " swapped with " + cf.action : "")}");
        return true;
    }

    public static void Clear(GameAction a, Column col)
    {
        if (!Rebindable) return;
        var slots = Slots(a, col); int idx = SlotIndex(col);
        if (idx >= slots.Length || slots[idx] == 0) return;
        slots[idx] = 0; Version++; Save();
    }

    // この欄に入れてよい入力か
    public static bool Accepts(Column col, int code)
    {
        if (code <= 0 || code == (int)KeyCode.Escape) return false;
        if (IsPadColumn(col)) return IsPadCode(code);
        if (col == Column.Mouse) return IsMouseCode(code);
        return IsKeyboardCode(code);
    }

    // 待ち受け: このフレームに新しく押された入力(無ければ 0、Esc = -1)
    static readonly Dictionary<int, bool> axisPrev = new Dictionary<int, bool>();
    static readonly int[] axisCodes = { RSUp, RSDown, RSLeft, RSRight, LSUp, LSDown, LSLeft, LSRight, LT, RT, DUp, DDown, DLeft, DRight };
    public static int PollPress(bool keyboard, bool mouse, bool padIn)
    {
        int found = 0;
        if (Input.GetKeyDown(KeyCode.Escape)) return -1;
        if (keyboard) for (int c = 1; c < (int)KeyCode.Mouse0 && found == 0; c++) if (System.Enum.IsDefined(typeof(KeyCode), c) && Input.GetKeyDown((KeyCode)c)) found = c;
        if (mouse && found == 0)
        {
            for (int c = (int)KeyCode.Mouse0; c <= (int)KeyCode.Mouse6 && found == 0; c++) if (Input.GetKeyDown((KeyCode)c)) found = c;
            ReadAxes();
            if (found == 0 && wheel > 0.01f) found = WheelUp; else if (found == 0 && wheel < -0.01f) found = WheelDown;
        }
        if (padIn)
        {
            for (int c = (int)KeyCode.JoystickButton0; c <= (int)KeyCode.JoystickButton19 && found == 0; c++) if (Input.GetKeyDown((KeyCode)c)) found = c;
            foreach (int c in axisCodes)
            {
                bool h = CodeHeld(c);
                bool was = axisPrev.TryGetValue(c, out bool p) && p;
                axisPrev[c] = h;
                if (found == 0 && h && !was) found = c;
            }
        }
        if (debugPoll != 0) { found = debugPoll; debugPoll = 0; }
        return found;
    }
    static int debugPoll;
    public static void DebugPoll(int code) { debugPoll = code; } // 自動テスト: 次の待ち受けにこの入力が来たことにする

    // 何か押されたままか(待ち受けを始める前に、押した指/ボタンが離れるのを待つ)
    public static bool AnyHeld()
    {
        if (Input.anyKey) return true;
        foreach (int c in axisCodes) if (CodeHeld(c)) return true;
        return false;
    }

    // ---------------------------------------------------------------- 表示
    // 入力の名前(画面用)。マウスの左右/中/ホイールだけ訳す。キーとパッドは英字のまま
    public static string CodeName(int c)
    {
        if (c <= 0) return "—";
        switch (c)
        {
            case WheelUp: return Loc.T("ホイール上");
            case WheelDown: return Loc.T("ホイール下");
            case RSUp: return "RS ↑"; case RSDown: return "RS ↓"; case RSLeft: return "RS ←"; case RSRight: return "RS →";
            case LSUp: return "LS ↑"; case LSDown: return "LS ↓"; case LSLeft: return "LS ←"; case LSRight: return "LS →";
            case LT: return "LT"; case RT: return "RT";
            case DUp: return "D-PAD ↑"; case DDown: return "D-PAD ↓"; case DLeft: return "D-PAD ←"; case DRight: return "D-PAD →";
        }
        var k = (KeyCode)c;
        switch (k)
        {
            case KeyCode.Mouse0: return Loc.T("左クリック");
            case KeyCode.Mouse1: return Loc.T("右クリック");
            case KeyCode.Mouse2: return Loc.T("中クリック");
            case KeyCode.Mouse3: return "MOUSE 4";
            case KeyCode.Mouse4: return "MOUSE 5";
            case KeyCode.Mouse5: return "MOUSE 6";
            case KeyCode.Mouse6: return "MOUSE 7";
            case KeyCode.UpArrow: return "↑";
            case KeyCode.DownArrow: return "↓";
            case KeyCode.LeftArrow: return "←";
            case KeyCode.RightArrow: return "→";
            case KeyCode.Return: return "ENTER";
            case KeyCode.KeypadEnter: return "NUM ENTER";
            case KeyCode.Escape: return "ESC";
            case KeyCode.LeftShift: return "L-SHIFT";
            case KeyCode.RightShift: return "R-SHIFT";
            case KeyCode.LeftControl: return "L-CTRL";
            case KeyCode.RightControl: return "R-CTRL";
            case KeyCode.LeftAlt: return "L-ALT";
            case KeyCode.RightAlt: return "R-ALT";
        }
        if (c >= (int)KeyCode.Alpha0 && c <= (int)KeyCode.Alpha9) return ((char)('0' + c - (int)KeyCode.Alpha0)).ToString();
        if (c >= (int)KeyCode.Keypad0 && c <= (int)KeyCode.Keypad9) return "NUM " + (c - (int)KeyCode.Keypad0);
        if (c >= (int)KeyCode.JoystickButton0 && c <= (int)KeyCode.JoystickButton19)
        {
            int b = c - (int)KeyCode.JoystickButton0;
            string[] names = { "A", "B", "X", "Y", "LB", "RB", "BACK", "START", "LS", "RS" };
            return b < names.Length ? names[b] : "PAD " + b;
        }
        return k.ToString().ToUpperInvariant();
    }

    // 操作の案内: その行動に今割り当てている入力(最後に使った機器の物)。例: "[SPACE] [右クリック]"。無ければ null
    public static string HintFor(GameAction a, InputDeviceKind dev)
    {
        EnsureLoaded();
        var list = new List<int>();
        if (dev == InputDeviceKind.Gamepad) { list.AddRange(pad[(int)a]); }
        else if (dev == InputDeviceKind.Mouse)
        {
            foreach (int c in keys[(int)a]) if (IsMouseCode(c)) list.Add(c);
            foreach (int c in keys[(int)a]) if (IsKeyboardCode(c)) list.Add(c);
        }
        else foreach (int c in keys[(int)a]) if (IsKeyboardCode(c)) list.Add(c);
        var sb = new StringBuilder();
        foreach (int c in list) { if (c <= 0) continue; if (sb.Length > 0) sb.Append(' '); sb.Append('[').Append(CodeName(c)).Append(']'); }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    // ジャンプと上攻撃は同じ動き: 案内は両方の入力をまとめる
    public static string HintForUp(InputDeviceKind dev)
    {
        string a = HintFor(GameAction.Jump, dev), b = HintFor(GameAction.AttackUp, dev);
        return a == null ? b : b == null ? a : a + " " + b;
    }
}
