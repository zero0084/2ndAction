using System.Collections.Generic;
using UnityEngine;

// ゲームパッド/キーボードでのメニュー操作(2026-10-06、家庭用機への移植の準備)。
//  ・画面ごとの作り直しをしないで済むように、押せる物(IMGUI のボタン / uGUI の画面のタップ判定の枠)を毎フレーム
//    「候補」として集め、十字キー/スティックで画面上の位置関係から次の候補へフォーカスを動かし、A(決定)で押す。
//  ・IMGUI: ボタンの描画関数(DrawStyledButton / UiKit.Button など)が PadNav.Button(rect) を呼ぶ。決定で true を返す。
//  ・uGUI(デッキ編集/キャラ選択/ステージ選択/カード合成/報酬カード/確認): 各画面のタップ判定(UiHit.Hit)を毎フレーム
//    「判定だけ」で一度走らせて枠を集め(UiHit の probe)、決定の時はフォーカスの中心を叩いたことにする(PointerInput の仮想タップ)。
//  ・層: 手前の画面/確認の窓ほど大きい層。いちばん大きい層の候補だけを動かす(後ろの画面へフォーカスが行かない)。
//    ラン中の HUD のボタン(II / 闘技場の左のボタン)は Hud 層(-1): ほかに候補が無い時(=ラン中)はメニュー操作をしない。
//  ・枠はパッド/キーボードを使っている時だけ出す(タッチ/マウスを使うと消える。スマホの見た目は変わらない)。
[DefaultExecutionOrder(-890)]
public class PadNav : MonoBehaviour
{
    public const int HudLayer = -1;

    struct Cand
    {
        public Rect r;            // GUI 座標(左上が原点)
        public int layer;
        public bool ugui;
        public bool slider;       // 左右は値の変更に使う
        public Object owner;      // (uGUI)判定の枠
        public Rect clip;         // 2026-10-31: 一覧の見えている範囲(無ければ幅 0)。枠がはみ出ていたら一覧を動かして見せる
    }

    static readonly List<Cand> cur = new List<Cand>(64), prev = new List<Cand>(64);
    static int curFrame = -1;
    static int layer;                     // 今登録している層
    static readonly Stack<Rect> clips = new Stack<Rect>();

    static bool hasFocus;
    static Vector2 focusPoint;
    static Rect focusRect;
    static int focusLayer;
    static bool focusUgui, focusSlider;
    static Rect focusClip;
    static int confirmFrame = -1;         // このフレームに決定が押された(まだ誰も使っていない)
    static bool confirmPending;
    public static int ScrollRequest { get; private set; } // 一覧の外へ進もうとした時: -1 上 / +1 下(一覧の側で使う)
    public static Rect ScrollRequestRect { get; private set; }

    // メニュー操作が有効か(パッド/キーを使っていて、HUD 以外の候補がある)
    public static bool MenuActive { get; private set; }
    public static bool Suspended; // 2026-10-25: キー設定の待ち受け中はメニュー操作を止める
    public static bool ShowFocus => MenuActive && hasFocus && GameInput.UsingNonTouch;
    public static int Candidates => prev.Count;
    public static Rect FocusRect => focusRect;
    public static bool HasFocus => hasFocus;

    static void EnsureFrame()
    {
        if (curFrame == Time.frameCount) return;
        curFrame = Time.frameCount;
        prev.Clear(); prev.AddRange(cur);
        cur.Clear();
        layer = 0;
        clips.Clear();
    }

    void Update()
    {
        EnsureFrame();
        Step();
    }

    // 2026-10-31(Windows の操作): 画面ごとのフォーカスの記憶。画面を開いて戻った時に、前に選んでいた項目へ戻す。
    //  ScreenKey は今の画面の名前(GameManager が渡す。ホーム/デッキ編集/設定/一時停止…)
    public static System.Func<string> ScreenKey;
    public static System.Func<string, Vector2?> DefaultFocus; // 初めて開いた画面で最初に選ぶ位置(GUI 座標。無ければ左上の項目)
    static string lastScreenKey, pendingDefault;
    static readonly Dictionary<string, Vector2> screenFocus = new Dictionary<string, Vector2>();
    public static int TopLayer { get; private set; } = int.MinValue; // 前のフレームの一番手前の層(ホイールを受け取る一覧の判定)

    static void Step()
    {
        ScrollRequest = 0;
        confirmPending = false;
        if (Suspended) { MenuActive = false; return; } // キー設定の待ち受け中(押したボタン/スティックを割り当てに使う。フォーカスを動かさない)
        if (TitleScreen.BlocksInput && !AppExit.Open) { MenuActive = false; return; } // 2026-10-31: タイトルの間は後ろのホームの項目を選ばない/決定しない(タイトルを始めた Enter で「設定」が開いていた)
        int top = int.MinValue;
        foreach (var c in prev) if (c.layer > top) top = c.layer;
        TopLayer = top;
        string key = ScreenKey != null ? ScreenKey() : null;
        // 手前の窓(お知らせ/確認)の層は、同じ画面の上に重なる別の「画面」として覚える(閉じた時に下の画面の前の項目へ戻す)
        if (key != null && top > 0) key += "#" + top;
        if (key != lastScreenKey)
        {
            if (lastScreenKey != null && hasFocus) screenFocus[lastScreenKey] = focusPoint;
            if (key != null && screenFocus.TryGetValue(key, out var p)) { hasFocus = true; focusPoint = p; focusAny = true; }
            else { hasFocus = false; pendingDefault = key; } // 初めての画面: 主な操作(DefaultFocus)が描かれたらそこへ。無ければ左上から
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[KeyNav] screen {lastScreenKey} -> {key} remembered={(key != null && screenFocus.ContainsKey(key))} focus={hasFocus}");
#endif
            lastScreenKey = key;
        }
        // 初めての画面の主な操作: 描かれて位置が分かったフレームで1回だけそこへ(開いた直後は位置がまだ無いことがある)
        bool wasActive = MenuActive;
        MenuActive = prev.Count > 0 && top > HudLayer && GameInput.UsingNonTouch;
        if (!MenuActive) { if (!GameInput.UsingNonTouch) hasFocus = false; return; }
        // マウス/タッチからキー/パッドへ持ち替えた最初の押下は、枠を出すだけ(押した矢印で今の項目から動かない)
        bool justActivated = !wasActive || !hasFocus;
        // 初めての画面の主な操作: キー/パッドの操作が始まって、主な操作の位置が分かったフレームで1回だけそこへ
        //  (マウス/タッチで開いた画面でも、後からキーを押した時に主な操作から始まる)
        // その位置に押せる物が描かれてから(画面が出てくる途中は、まだ別の物しか無い)
        if (pendingDefault != null && pendingDefault == key && DefaultFocus != null && DefaultFocus(key.Split('#')[0]) is Vector2 dfp && prev.Exists(c => c.layer == top && c.r.Contains(dfp)))
        {
            hasFocus = true; focusPoint = dfp; focusAny = true; pendingDefault = null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[KeyNav] default focus for {key} at {dfp}");
#endif
        }

        // フォーカスを今の候補へ合わせ直す(画面が変わった/動いた時は一番近い物、何も無ければ左上)
        int best = -1; float bestD = float.MaxValue;
        for (int i = 0; i < prev.Count; i++)
        {
            if (prev[i].layer != top) continue;
            float d = hasFocus && (focusLayer == top || focusAny) ? (prev[i].r.center - focusPoint).sqrMagnitude : prev[i].r.y * 4f + prev[i].r.x;
            if (d < bestD) { bestD = d; best = i; }
        }
        if (best < 0) { hasFocus = false; return; }
        SetFocus(prev[best]);

        // Tab / Shift+Tab: 読む順(上から、同じ行は左から)の次/前の項目へ。端まで行ったら反対の端へ
        if (!GameInput.TextInputActive && Input.GetKeyDown(KeyCode.Tab))
        {
            bool back = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            int next = FindTabNext(top, back);
            if (next >= 0) { SetFocus(prev[next]); RevealFocus(); if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiTap); }
        }

        // 移動
        Vector2 dir = Vector2.zero;
        if (GameInput.DownRepeat(GameAction.NavUp)) dir = Vector2.down;      // GUI 座標は下が +y
        else if (GameInput.DownRepeat(GameAction.NavDown)) dir = Vector2.up;
        else if (!focusSlider && GameInput.DownRepeat(GameAction.NavLeft)) dir = Vector2.left;
        else if (!focusSlider && GameInput.DownRepeat(GameAction.NavRight)) dir = Vector2.right;
        if (dir != Vector2.zero && justActivated) dir = Vector2.zero;
        if (dir != Vector2.zero)
        {
            pendingDefault = null; // 自分で動かした後は、主な操作の位置へ引き戻さない
            int next = FindNext(top, dir);
            if (next >= 0) { SetFocus(prev[next]); RevealFocus(); if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiTap); }
            else if (dir.y != 0f) { ScrollRequest = dir.y > 0f ? 1 : -1; ScrollRequestRect = focusRect; }
        }
        if (GameInput.Down(GameAction.ScrollDown)) { ScrollRequest = 1; ScrollRequestRect = focusRect; }
        else if (GameInput.Down(GameAction.ScrollUp)) { ScrollRequest = -1; ScrollRequestRect = focusRect; }

        if (GameInput.Consume(GameAction.Confirm))
        {
            confirmPending = true; confirmFrame = Time.frameCount;
            GameInput.LatchHeld(); // 押したままの Enter / Space を次の画面(次の決定・ラン開始のジャンプ)へ持ち越さない
        }
    }

    // フォーカスした枠が一覧の見えている範囲からはみ出ていたら、その向きへ一覧を動かす(画面外の項目へ移った時に見せる)
    static void RevealFocus()
    {
        if (focusClip.width <= 0f) return;
        if (focusRect.yMax > focusClip.yMax + 1f) { ScrollRequest = 1; ScrollRequestRect = focusRect; }
        else if (focusRect.yMin < focusClip.yMin - 1f) { ScrollRequest = -1; ScrollRequestRect = focusRect; }
    }

    static int FindTabNext(int top, bool back)
    {
        var order = new List<int>();
        for (int i = 0; i < prev.Count; i++) if (prev[i].layer == top) order.Add(i);
        if (order.Count == 0) return -1;
        // 行は高さの半分ほどで丸める(少しずれて並んだ物も同じ行)
        order.Sort((a, b) =>
        {
            Rect ra = prev[a].r, rb = prev[b].r;
            float row = Mathf.Min(ra.height, rb.height) * 0.5f;
            if (Mathf.Abs(ra.center.y - rb.center.y) > row) return ra.center.y.CompareTo(rb.center.y);
            return ra.center.x.CompareTo(rb.center.x);
        });
        int cur = -1; float bestD = float.MaxValue;
        if (hasFocus) for (int k = 0; k < order.Count; k++) { float d = (prev[order[k]].r.center - focusRect.center).sqrMagnitude; if (d < bestD) { bestD = d; cur = k; } }
        if (cur < 0) return order[back ? order.Count - 1 : 0];
        int n = (cur + (back ? -1 : 1) + order.Count) % order.Count;
        return order[n];
    }

    // 自動テスト用: この点に一番近い候補へフォーカス / タッチを使ったことにする
    static bool focusAny;
    // その画面の選択の記憶を消す(次に開いた時は DefaultFocus から。終了の確認は毎回「キャンセル」から、2026-10-31)
    public static void ForgetScreen(string baseKey)
    {
        var drop = new List<string>();
        foreach (var k in screenFocus.Keys) if (k == baseKey || k.StartsWith(baseKey + "#")) drop.Add(k);
        foreach (var k in drop) screenFocus.Remove(k);
        if (lastScreenKey != null && (lastScreenKey == baseKey || lastScreenKey.StartsWith(baseKey + "#"))) lastScreenKey = null;
    }
    public static void DebugFocusNear(Vector2 guiPoint) { hasFocus = true; focusPoint = guiPoint; focusAny = true; }
    public static void DebugTouchUsed() { GameInput.DebugMarkDevice(InputDeviceKind.Touch); }
    public static string DebugCandidates() { var sb = new System.Text.StringBuilder(); foreach (var c in prev) sb.Append($"[{c.layer}{(c.ugui ? "u" : "")} {c.r.center.x:F0},{c.r.center.y:F0} {c.r.width:F0}x{c.r.height:F0}] "); return sb.ToString(); }
    public static void DebugTab(bool back) { int top = TopLayer; int n = FindTabNext(top, back); if (n >= 0) SetFocus(prev[n]); }

    static void SetFocus(Cand c)
    {
        focusAny = false;
        hasFocus = true;
        focusRect = c.r; focusPoint = c.r.center; focusLayer = c.layer; focusUgui = c.ugui; focusSlider = c.slider; focusClip = c.clip;
    }

    // 画面上の位置関係で、その向きにある一番近い候補(向きの距離 + 横ずれ×2)
    static int FindNext(int top, Vector2 dir)
    {
        int best = -1; float bestScore = float.MaxValue;
        for (int i = 0; i < prev.Count; i++)
        {
            var c = prev[i];
            if (c.layer != top) continue;
            Vector2 d = c.r.center - focusRect.center;
            float along = Vector2.Dot(d, dir);
            // 重なり具合: 向きの方向に、今の枠の端を越えている物だけ
            float edge = dir.x != 0f ? (focusRect.width + c.r.width) * 0.25f : (focusRect.height + c.r.height) * 0.25f;
            if (along <= edge) continue;
            float across = Mathf.Abs(dir.x != 0f ? d.y : d.x);
            // 横ずれは、帯の中(重なっている)ならほぼ無視
            float overlap = dir.x != 0f ? Mathf.Max(0f, across - (focusRect.height + c.r.height) * 0.5f) : Mathf.Max(0f, across - (focusRect.width + c.r.width) * 0.5f);
            float score = along + overlap * 3f + across * 0.15f;
            if (score < bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    // ===================================================================== 登録(IMGUI)
    // 層: 手前の窓を描く前に BeginLayer(大きい数)、描き終えたら EndLayer(戻り値)
    public static int BeginLayer(int l) { EnsureFrame(); int p = layer; layer = l; return p; }
    public static void EndLayer(int previous) { layer = previous; }
    public static int CurrentLayer => layer;

    // IMGUI の矩形 → 画面上の矩形(GUI 座標)。描画は「グループ/スクロールの位置を足した座標」に GUI.matrix(拡大)を掛けたもの。
    // GUIToScreenPoint は拡大の入り方が場合によって違うので、拡大を外して位置だけを求め、拡大は自分で掛ける
    public static Rect ToScreen(Rect guiRect)
    {
        Matrix4x4 m = GUI.matrix;
        bool scaled = m != Matrix4x4.identity;
        if (scaled) GUI.matrix = Matrix4x4.identity;
        Vector2 a = GUIUtility.GUIToScreenPoint(new Vector2(guiRect.xMin, guiRect.yMin));
        Vector2 b = GUIUtility.GUIToScreenPoint(new Vector2(guiRect.xMax, guiRect.yMax));
        if (scaled) { GUI.matrix = m; a = m.MultiplyPoint3x4(a); b = m.MultiplyPoint3x4(b); }
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    // IMGUI の一覧(スクロール)の見えている範囲。範囲外の物は候補にしない
    public static void PushClip(Rect guiRect) { EnsureFrame(); clips.Push(PadNav.ToScreen(guiRect)); }
    public static void PopClip() { if (clips.Count > 0) clips.Pop(); }

    // IMGUI のボタン: 候補として登録し、フォーカス中に決定が押されたら true(1フレームに1つだけ)
    public static bool Button(Rect guiRect, bool slider = false)
    {
        EnsureFrame();
        if (!GUI.enabled) return false;
        Rect s = PadNav.ToScreen(guiRect);
        if (s.width < 2f || s.height < 2f) return false; // (GUILayout の Layout の時の仮の枠)
        if (clips.Count > 0 && !clips.Peek().Contains(s.center)) return false;
        AddUnique(new Cand { r = s, layer = layer, slider = slider, clip = clips.Count > 0 ? clips.Peek() : default }); // 1フレームに何度呼ばれても(Layout/Repaint/入力)1つだけ
        if (!confirmPending || focusUgui || layer != focusLayer || confirmFrame != Time.frameCount) return false;
        if ((s.center - focusRect.center).sqrMagnitude > 9f) return false;
        confirmPending = false; // (音は押された側の処理が鳴らす。タップと同じ)
        return true;
    }

    static void AddUnique(Cand c)
    {
        for (int i = cur.Count - 1, n = 0; i >= 0 && n < 256; i--, n++)
            if (cur[i].layer == c.layer && !cur[i].ugui && (cur[i].r.center - c.r.center).sqrMagnitude < 1f && Mathf.Abs(cur[i].r.width - c.r.width) < 1f) return;
        cur.Add(c);
    }

    // GUILayout のボタン(マルチの画面): 枠は描いた後にしか分からないので、押した直後に呼ぶ。
    // 決定は Repaint の時に分かるが、その場で画面を変えると GUILayout が崩れるので、次のフレームの同じ順番のボタンで true を返す
    static int layoutIndex, layoutFireIndex = -1, layoutFireFrame = -1, layoutPassFrame = -1; static EventType layoutPassType;
    public static void BeginLayoutButtons()
    {
        if (Event.current == null) return;
        if (layoutPassFrame != Time.frameCount || layoutPassType != Event.current.type) { layoutIndex = 0; layoutPassFrame = Time.frameCount; layoutPassType = Event.current.type; }
    }
    public static bool LayoutButton(bool clicked)
    {
        int idx = layoutIndex++;
        if (Event.current == null) return clicked;
        if (Event.current.type == EventType.Repaint)
        {
            Rect r = GUILayoutUtility.GetLastRect();
            if (Button(r)) { layoutFireIndex = idx; layoutFireFrame = Time.frameCount + 1; }
            return clicked;
        }
        if (Event.current.type == EventType.Layout && layoutFireFrame == Time.frameCount && idx == layoutFireIndex) { layoutFireIndex = -1; return true; }
        return clicked;
    }

    // スライダーがフォーカス中なら左右で値を動かす(押し続けで繰り返す)
    public static float SliderNudge(Rect guiRect)
    {
        if (!ShowFocus || !focusSlider) return 0f;
        Rect s = PadNav.ToScreen(guiRect);
        if ((s.center - focusRect.center).sqrMagnitude > 9f) return 0f;
        if (Event.current == null || Event.current.type != EventType.Layout) return 0f; // 1フレームに1回
        if (GameInput.DownRepeat(GameAction.NavLeft)) return -1f;
        if (GameInput.DownRepeat(GameAction.NavRight)) return 1f;
        return 0f;
    }

    // ===================================================================== 登録(uGUI)
    public static void RegisterUgui(RectTransform rt, int l = 0)
    {
        EnsureFrame();
        if (rt == null || !rt.gameObject.activeInHierarchy) return;
        Rect s = ScreenRectOf(rt);
        if (s.width < 2f || s.height < 2f) return;
        // 画面の外 / マスク(スクロールの窓)の外は候補にしない
        Vector2 c = s.center;
        if (c.x < 0f || c.y < 0f || c.x > Screen.width || c.y > Screen.height) return;
        Rect clip = default;
        for (var t = rt.parent; t != null; t = t.parent)
        {
            if (t.GetComponent<UnityEngine.UI.RectMask2D>() == null && t.GetComponent<UnityEngine.UI.Mask>() == null) continue;
            Rect m = ScreenRectOf((RectTransform)t);
            if (!m.Contains(c)) return;
            if (clip.width <= 0f) clip = m; // 一番内側の窓
        }
        cur.Add(new Cand { r = s, layer = l, ugui = true, owner = rt, clip = clip });
    }

    // uGUI の枠の画面上の矩形(GUI 座標: 左上が原点)。Screen Space Overlay のキャンバス前提
    public static Rect ScreenRectOf(RectTransform rt)
    {
        var w = new Vector3[4];
        rt.GetWorldCorners(w);
        float xMin = Mathf.Min(w[0].x, w[2].x), xMax = Mathf.Max(w[0].x, w[2].x);
        float yMin = Mathf.Min(w[0].y, w[2].y), yMax = Mathf.Max(w[0].y, w[2].y);
        return new Rect(xMin, Screen.height - yMax, xMax - xMin, yMax - yMin);
    }

    // uGUI の画面が使う: 決定でフォーカスの中心を叩く(画面座標: 左下が原点)
    public static bool TakeVirtualTap(out Vector2 screenPos)
    {
        screenPos = Vector2.zero;
        if (!confirmPending || !focusUgui || confirmFrame != Time.frameCount) return false;
        confirmPending = false;
        screenPos = new Vector2(focusRect.center.x, Screen.height - focusRect.center.y);
        return true;
    }

    // スクロールの要求がこの枠(一覧の窓, GUI/画面どちらでも)の中のフォーカスから来たか
    // 2026-10-31: フォーカスが一覧の中に無い時(ボタンの無い長い説明文など)のスクロール(PageUp/PageDown・上下の端)を、
    //  一番手前の層の文章が1つだけ受け取る(キーボードだけで最後まで読めるように)
    static int fallbackClaimFrame = -1;
    public static int ClaimFallbackScroll()
    {
        if (ScrollRequest == 0 || focusClip.width > 0f || fallbackClaimFrame == Time.frameCount) return 0;
        if (layer < TopLayer) return 0;
        fallbackClaimFrame = Time.frameCount;
        return ScrollRequest;
    }

    public static int ScrollRequestFor(Rect guiScreenRect)
    {
        if (ScrollRequest == 0 || !guiScreenRect.Contains(ScrollRequestRect.center)) return 0;
        return ScrollRequest;
    }
    public static int ScrollRequestForUgui(RectTransform viewport)
    {
        if (viewport == null) return 0;
        return ScrollRequestFor(ScreenRectOf(viewport));
    }

    // ===================================================================== 枠の表示
    // 文字の入力欄(IMGUI)を描いた時に呼ぶ(描かれなくなった入力欄にキーの焦点が残ったままにしない)
    static int textFieldFrame = -10;
    public static void NoteTextField() { textFieldFrame = Time.frameCount; }

    static Texture2D tex;
    void OnGUI()
    {
        GUI.depth = AppExit.Open ? -31999 : -3000; // 一番手前(どのイベントでも設定する)。2026-10-31: 終了の確認(-31995)の上にも出す
        // 2026-10-31: 文字の入力中か(キーの焦点が入力欄にある)。入力中はキーで項目を動かしたりゲームの操作をしない(GameInput.TextInputActive)。
        //  Enter で入力を終える(次の Enter から項目の決定)。入力欄が描かれなくなったら焦点を外す
        int kc = GUIUtility.keyboardControl;
        bool editing = kc != 0 && GUIUtility.QueryStateObject(typeof(TextEditor), kc) != null;
        if (editing && Time.frameCount - textFieldFrame > 2) { GUIUtility.keyboardControl = 0; editing = false; }
        if (editing && Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
        {
            GUIUtility.keyboardControl = 0; editing = false;
            GameInput.LatchAction(GameAction.Confirm); // この Enter を項目の決定に使わない(入力中は押されていない扱いなので、名指しで止める)
        }
        GameInput.TextInputActive = editing;
        if (!ShowFocus || Event.current.type != EventType.Repaint) return;
        if (tex == null) { tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave }; tex.SetPixel(0, 0, Color.white); tex.Apply(); }
        var m = GUI.matrix; GUI.matrix = Matrix4x4.identity;
        float pulse = 0.82f + 0.18f * Mathf.Sin(Time.unscaledTime * 6f);
        float t = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 160f, 4f, 10f);
        Rect r = new Rect(focusRect.x - t, focusRect.y - t, focusRect.width + t * 2f, focusRect.height + t * 2f);
        Color keep = GUI.color;
        GUI.color = new Color(1f, 0.9f, 0.5f, 0.22f * pulse);   // 中を明るく(2026-10-31: 明るい背景でも分かるよう 0.12 → 0.22)
        GUI.DrawTexture(focusRect, tex);
        // 2026-10-31: 左に小さな ▶ の印(枠の色が背景に溶けても、どれを選んでいるか分かる)
        {
            float a = Mathf.Clamp(Mathf.Min(focusRect.height, 40f) * 0.45f, 8f, 18f);
            float ax = r.x - a - 4f - Mathf.Sin(Time.unscaledTime * 6f) * 2f, ay = focusRect.center.y;
            for (int i = 0; i < (int)a; i++)
            {
                float hh = (a - i) * 0.9f;
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(new Rect(ax + i - 1f, ay - hh - 1f, 1f, hh * 2f + 2f), tex);
                GUI.color = new Color(1f, 0.84f, 0.3f, pulse);
                GUI.DrawTexture(new Rect(ax + i, ay - hh, 1f, hh * 2f), tex);
            }
        }
        GUI.color = new Color(0f, 0f, 0f, 0.6f);               // 外側の黒い縁(明るい背景でも見える)
        Frame(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), t + 3f);
        GUI.color = new Color(1f, 0.84f, 0.3f, pulse);
        Frame(r, t);
        GUI.color = keep;
        GUI.matrix = m;
    }
    static void Frame(Rect r, float t)
    {
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), tex);
        GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), tex);
        GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), tex);
        GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), tex);
    }
}

// uGUI の画面のタップ判定。PadNav の probe 中は「判定せずに枠を候補として集める」
public static class UiHit
{
    static int probeLayer;
    public static bool Probing { get; private set; }

    public static bool Hit(RectTransform r, Vector2 screenPos)
    {
        if (r == null) return false;
        if (Probing) { PadNav.RegisterUgui(r, probeLayer); return false; }
        return RectTransformUtility.RectangleContainsScreenPoint(r, screenPos, null);
    }

    // 画面の Update から: メニュー操作中なら、タップ処理を判定だけで一度走らせて押せる枠を集める
    public static void Probe(System.Action<Vector2> handleTap, int layer = 0)
    {
        if (!GameInput.UsingNonTouch || handleTap == null) return;
        Probing = true; probeLayer = layer;
        try { handleTap(new Vector2(-99999f, -99999f)); }
        finally { Probing = false; }
    }
}

// タッチ/マウス + パッドの仮想タップをまとめて読む(各 uGUI 画面の「押した/離した/押している/位置」)。
//  仮想タップは「押した」(このフレーム)→「離した」(次のフレーム、同じ画面だけ)の2フレームで渡す
//  (押した時に return する画面があるため。指の速いタップと同じ形)。
public static class PointerInput
{
    static object vOwner; static int vUpFrame = -1; static Vector2 vPos;

    public static void Read(object owner, out bool down, out bool up, out bool held, out Vector2 screenPos)
    {
        if (LoadingScreen.BlocksInput) { down = up = held = false; screenPos = Vector2.zero; return; } // 読み込み中(2026-10-11)
        down = Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began);
        up = Input.GetMouseButtonUp(0) || (Input.touchCount > 0 && (Input.GetTouch(0).phase == TouchPhase.Ended || Input.GetTouch(0).phase == TouchPhase.Canceled));
        held = !down && !up && (Input.GetMouseButton(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase != TouchPhase.Ended && Input.GetTouch(0).phase != TouchPhase.Canceled));
        screenPos = Input.touchCount > 0 ? (Vector2)Input.GetTouch(0).position : (Vector2)Input.mousePosition;
        if (vUpFrame >= 0 && ReferenceEquals(owner, vOwner))
        {
            if (Time.frameCount >= vUpFrame) { vUpFrame = -1; if (!down && !held) { up = true; screenPos = vPos; } }
            else { held = true; screenPos = vPos; }
            return;
        }
        if (!down && !up && !held && PadNav.TakeVirtualTap(out var v)) { down = true; screenPos = v; vPos = v; vOwner = owner; vUpFrame = Time.frameCount + 1; }
    }
}
