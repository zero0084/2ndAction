using System;
using UnityEngine;

// タイトル画面(2026-10-31、仕上げ)。起動 →(起動ロゴ)→ タイトル → 入力 → ホーム。
//  ・アプリを起動した最初の1回だけ。ラン → ホームのシーンの読み直しでは出さない。自動試験(-qa… 等)では出さない(-titleScreen 1 で出す)
//  ・ホーム(GameManager)はこの下で普段どおり準備する(セーブ/設定/移行/サービス/ランキングの準備/画面の向き/Safe Area/入力機器)。
//    タイトルのために待たせる処理は無い: 入力を受け付けるのは、ホームの準備ができてから(早押しは準備が終わった時に受け付ける)
//  ・背景(2026-11-01 差し替え): 青い空と黒い地面の、横スクロール風の影絵(Resources/Title/title_bg、マスター提供の絵から人物を消して地面をつないだ物。
//    Tools/title/inpaint_bg.py)。無ければ暗い色だけ。縦画面は高さに合わせ、キャラが画面の左 30% に来るように横をずらす(右に道が見える)
//  ・キャラの影(2026-11-01): キャラ選択で最後に決めたキャラ(GameManager.SelectedCharacterId。初回/無効な保存値/未解放は黒剣士)を、
//    出発演出の1コマから作った黒一色の絵(Resources/Title/Silhouettes/<id>、Tools/title/silhouettes.py)で、左寄り・右向きに地面へ立たせる。
//    絵は「横の中心 = 足もと」に作ってある。大きさ/位置はキャラごとの表(背景の高さに対する割合)。ホームの準備で選択が読まれてから出す
//  ・ロゴ: ホームと同じ「ONE MORE MILE」のロゴ(GameManager.titleLogo)を空の中央に。無い時はエンディングの問いと同じ金の文字(GlyphFont)
//  ・入力: Android = タップ。Windows = キー/クリック/パッドのどれでも(Esc = Android の戻る は数えない)。案内の文は機器で切り替える(Loc)
//  ・入力の後 0.35 秒で消えてホームへ。押した指/キーはホームへ持ち越さない(UiInputGate のラッチ / GameInput.LatchHeld)
public class TitleScreen : MonoBehaviour
{
    public const string BgResource = "Title/title_bg";
    public const string TapToStart = LocDeckEdit.TapToStart;     // ID 10323
    public const string PressToStart = LocDeckEdit.PressToStart; // ID 10324
    public const string Title = "ONE MORE MILE"; // エンディングの問い「ONE MORE MILE?」と対(? なし)
    public static bool Showing { get; private set; }
    public static bool BlocksInput => Showing;
    public static int Starts { get; private set; } // 確認用: ホームへ進んだ回数
    public static string LastStartInput { get; private set; } = "";
    public static bool InputReady { get; private set; } // 確認用: ホームの準備ができて入力を受け付ける状態

    const float FadeIn = 0.45f, MinShow = 0.35f, FadeOut = 0.35f;
    static bool shownOnce;
    Texture2D bg;

    // ---- キャラの影(2026-11-01)
    public const string SilhouetteFolder = "Title/Silhouettes/";
    public const string DefaultCharacter = "swordsman";
    public static string ShownCharacterId { get; private set; } = ""; // 確認用: 出した影のキャラ
    // 背景の絵の中の、足もとの位置(横)と地面の高さ(上から)。人物を消した所の地面の線(x 0.23 で 672/941)に合わせた
    const float FootX = 0.228f, GroundY = 0.716f;
    // キャラごとの大きさ(絵の高さ = 背景の高さ × h)と横のずれ(背景の幅に対する割合)。人の背丈は添付の絵の黒剣士(0.16)に合わせる。
    //  槍/杖/翼の分だけ絵が高い人は大きめ、猫(ルカ)と雲(ペンタトゥットゥルー星人)は小さめ
    static readonly System.Collections.Generic.Dictionary<string, Vector2> SilhouetteFit = new System.Collections.Generic.Dictionary<string, Vector2>
    {
        { "swordsman", new Vector2(0.165f, 0f) }, { "dual_blade", new Vector2(0.165f, 0f) }, { "noble_lady", new Vector2(0.165f, 0f) },
        { "gunslinger", new Vector2(0.165f, 0f) }, { "dragon_lancer", new Vector2(0.19f, 0f) }, { "archer", new Vector2(0.16f, 0f) },
        { "mage", new Vector2(0.185f, 0f) }, { "fighter", new Vector2(0.155f, 0f) }, { "ninja", new Vector2(0.17f, 0f) },
        { "miko", new Vector2(0.165f, 0f) }, { "vampire", new Vector2(0.165f, 0f) }, { "dragonkin", new Vector2(0.18f, 0f) },
        { "luka", new Vector2(0.10f, 0f) }, { "pentatuttulu", new Vector2(0.12f, 0f) },
    };
    Texture2D sil;
    float silT = -1f; // 影を出し始めた時刻(t)
    float t, outT = -1f, outStart;
    bool ready;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    static string[] Args => QaArgs.All;
#else
    static string[] Args => Environment.GetCommandLineArgs();
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (shownOnce) return;
        shownOnce = true;
        if (SkippedByArgs()) return;
        var go = new GameObject("[TitleScreen]");
        DontDestroyOnLoad(go);
        go.AddComponent<TitleScreen>();
        Showing = true;
        Debug.Log($"[Title] boot t={Time.realtimeSinceStartup:F2}s");
    }

    static bool SkippedByArgs()
    {
        if (!DevGate.On) return false;
        string[] a = Args;
        int i = Array.IndexOf(a, "-titleScreen");
        if (i >= 0 && i + 1 < a.Length) return a[i + 1] != "1";
        foreach (var s in a) if (s.StartsWith("-qa") || s.StartsWith("-netAuto") || s == "-storeCapture" || s.StartsWith("-ldQa") || s.StartsWith("-lcTour")) return true;
        return false;
    }

    void Awake() { bg = Resources.Load<Texture2D>(BgResource); }

    // 案内の文: タッチの機器(Android など)は「タップして開始」。PC は「キー / クリック / パッドで開始」
    public static string PromptText => PlatformCaps.SupportsMouse || PlatformCaps.SupportsKeyRebind ? PressToStart : TapToStart;

    bool HomeReady()
    {
        var gm = GameManager.Instance;
        return gm != null && !LoadingScreen.Busy && !BootSplash.Showing && !(ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning);
    }

    // 右下の「ゲーム終了 / 端末のホームへ」(GUI 座標)。ここを押した指/クリックは開始に使わない(2026-10-31)
    Rect exitRect;
    bool OnExitButton(Vector2 screenPos) => exitRect.width > 0f && exitRect.Contains(new Vector2(screenPos.x, Screen.height - screenPos.y));

    string InputDown()
    {
        for (int i = 0; i < Input.touchCount; i++) if (Input.GetTouch(i).phase == TouchPhase.Began && !OnExitButton(Input.GetTouch(i).position)) return "touch";
        if (Input.touchCount == 0 && !OnExitButton(Input.mousePosition))
            for (int b = 0; b < 7; b++) if (Input.GetMouseButtonDown(b)) return "mouse" + b;
        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) || Input.GetKey(KeyCode.AltGr); // Alt+Enter(全画面)/ Alt+F4(終了)では始めない
        if (Input.anyKeyDown && !Input.GetKeyDown(KeyCode.Escape) && !alt)
        {
            for (int k = 0; k < 20; k++) if (Input.GetKeyDown(KeyCode.JoystickButton0 + k)) return "pad";
            return "key";
        }
        if (Mathf.Abs(Input.GetAxisRaw("Pad DX")) > 0.5f || Mathf.Abs(Input.GetAxisRaw("Pad DY")) > 0.5f) return "pad";
        return null;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static bool DebugPress; // 自動試験: 次のフレームに入力があったことにする
#endif

    void Update()
    {
        if (BootSplash.Showing) return; // 起動ロゴの間は待つ(ロゴの下に描いておく)
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        t += dt;
        if (!ready && HomeReady()) { ready = true; InputReady = true; Debug.Log($"[Title] home ready t={Time.realtimeSinceStartup:F2}s (shown {t:F2}s)"); }
        if (silT < 0f) PickSilhouette();
        if (outT < 0f)
        {
            string inp = null;
            try { inp = InputDown(); } catch (ArgumentException) { inp = Input.anyKeyDown ? "key" : null; } // パッドの軸が無い環境
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (DebugPress) { DebugPress = false; inp = "debug"; }
#endif
            if (AppExit.Open || UiInputGate.Latched) { inp = null; pending = null; } // 終了の確認の間/その窓を閉じた指が離れるまでは始めない
            if (inp != null) pending = inp;
            if (pending != null && ready && t >= MinShow)
            {
                outT = 0f; outStart = Time.realtimeSinceStartup; LastStartInput = pending;
                Debug.Log($"[Title] start by {pending} at {t:F2}s real={Time.realtimeSinceStartup:F2}s");
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
                UiInputGate.LatchUntilRelease(); GameInput.LatchHeld(); // 押した瞬間から、その指/キーをホームへ渡さない(消える間に決定にならないように)
            }
        }
        else
        {
            outT = Time.realtimeSinceStartup - outStart; // 実時間(ホームの初回の重いフレームで消えるのが遅れないように)
            if (outT >= FadeOut) Finish();
        }
    }
    string pending;

    // 選択中のキャラが読まれたら(ホームの準備の途中)、その影を決める。GameManager が初回/無効な保存値/未解放を
    //  既定のキャラ(黒剣士)へ戻した後の値を使う。絵が無いキャラも黒剣士にする
    void PickSilhouette()
    {
        var gm = GameManager.Instance;
        string id = gm != null ? gm.SelectedCharacterId : null;
        if (string.IsNullOrEmpty(id)) { if (!ready) return; id = DefaultCharacter; }
        sil = Resources.Load<Texture2D>(SilhouetteFolder + id);
        if (sil == null || !SilhouetteFit.ContainsKey(id)) { id = DefaultCharacter; sil = Resources.Load<Texture2D>(SilhouetteFolder + id); }
        ShownCharacterId = sil != null ? id : "";
        silT = t;
        Debug.Log($"[Title] silhouette {id} (selected {(gm != null ? gm.SelectedCharacterId : "-")}) t={t:F2}s");
    }

    // 背景の絵を置く範囲(画面の座標)。横は画面を覆う(中央)。縦は高さに合わせ、足もとが画面の左 30% に来るように横をずらす
    Rect BgRect(float W, float H)
    {
        float bw = bg != null ? bg.width : 1920f, bh = bg != null ? bg.height : 1080f;
        if (H > W)
        {
            float s = H / bh, x = Mathf.Clamp(W * 0.30f - FootX * bw * s, W - bw * s, 0f);
            return new Rect(x, 0f, bw * s, bh * s);
        }
        float k = Mathf.Max(W / bw, H / bh);
        return new Rect((W - bw * k) * 0.5f, (H - bh * k) * 0.5f, bw * k, bh * k);
    }

    void Finish()
    {
        Showing = false;
        Starts++;
        UiInputGate.LatchUntilRelease(); // 押した指はホームのボタンへ通さない
        GameInput.LatchHeld();           // 押したキー/パッドのボタンをホームの決定に持ち越さない
        Debug.Log($"[Title] done real={Time.realtimeSinceStartup:F2}s");
        Destroy(gameObject);
    }

    static GUIStyle promptStyle;
    void OnGUI()
    {
        GUI.depth = -31990; // 起動ロゴ(-32000)のすぐ後ろ、ホームのすべてより手前
        float W = Screen.width, H = Screen.height;
        float a = outT < 0f ? 1f : 1f - Smooth(Mathf.Clamp01(outT / FadeOut));
        Color keep = GUI.color;
        // 背景(横は画面を覆う。縦は高さに合わせて、キャラの右に道が見えるようにずらす)
        GUI.color = new Color(0.024f, 0.031f, 0.063f, a);
        GUI.DrawTexture(new Rect(0f, 0f, W, H), Texture2D.whiteTexture);
        Rect br = BgRect(W, H);
        if (bg != null)
        {
            GUI.color = new Color(1f, 1f, 1f, a * Smooth(Mathf.Clamp01(t / FadeIn)));
            GUI.DrawTexture(br, bg);
        }
        bool portrait = H > W;
        // キャラの影(黒一色、透過のまま。絵の横の中心 = 足もと)
        if (sil != null && SilhouetteFit.TryGetValue(ShownCharacterId, out var fit))
        {
            float sh = fit.x * br.height, sw = sh * sil.width / Mathf.Max(1f, sil.height);
            float fx = br.x + (FootX + fit.y) * br.width, gy = br.y + GroundY * br.height;
            GUI.color = new Color(0f, 0f, 0f, a * Smooth(Mathf.Clamp01((t - silT) / FadeIn)));
            GUI.DrawTexture(new Rect(fx - sw * 0.5f, gy - sh, sw, sh), sil);
        }
        // ロゴ(ホームと同じ絵)を空の中央に。無い時は金の文字
        float fadeTitle = Smooth(Mathf.Clamp01((t - 0.1f) / FadeIn));
        var logo = GameManager.Instance != null ? GameManager.Instance.titleLogo : null;
        float cy = H * 0.30f;
        if (logo != null)
        {
            float lw = W * (portrait ? 0.86f : 0.40f), lh = lw * logo.height / Mathf.Max(1f, logo.width);
            GUI.color = new Color(1f, 1f, 1f, a * fadeTitle);
            GUI.DrawTexture(new Rect(W * 0.5f - lw * 0.5f, cy - lh * 0.5f, lw, lh), logo);
        }
        else
        {
            float capH = portrait ? Mathf.Min(W * 0.085f, H * 0.06f) : Mathf.Min(H * 0.11f, W * 0.07f);
            capH = Mathf.Min(capH, W * (portrait ? 0.86f : 0.66f) / Mathf.Max(1f, GlyphFont.Measure(Title))); // 横幅に収める(縦画面は幅いっぱい近く)
            DrawGlyphs(Title, W * 0.5f, cy, capH, a * fadeTitle);
        }
        // 案内(ゆっくり明滅)。準備が終わるまでは薄く
        float pulse = 0.55f + 0.45f * Mathf.Sin(t * 2.4f);
        float pa = a * Smooth(Mathf.Clamp01((t - 0.35f) / 0.4f)) * (ready ? pulse : 0.25f);
        if (promptStyle == null) promptStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
        float k = UiScalePref.Menu(Mathf.Clamp(Mathf.Min(W, H) / 720f, 1f, 2.6f));
        promptStyle.fontSize = Mathf.RoundToInt(26f * k);
        Rect pr = new Rect(W * 0.1f, br.y + br.height * 0.80f - 30f * k, W * 0.8f, 60f * k); // 地面(黒)の上。キャラの足もとより下
        promptStyle.normal.textColor = new Color(0f, 0f, 0f, 0.7f * pa);
        LocGUI.Label(new Rect(pr.x + 2f, pr.y + 2f, pr.width, pr.height), PromptText, promptStyle);
        promptStyle.normal.textColor = new Color(1f, 0.92f, 0.72f, pa);
        LocGUI.Label(pr, PromptText, promptStyle);
        // 右下: ゲーム終了(PC)/ 端末のホームへ(Android)。iOS には置かない(押してもアプリは閉じないため)
        exitRect = default;
        if (AppExit.HasExitButton && ready && outT < 0f && !AppExit.Open)
        {
            Rect safe = StableSafeArea.Rect;
            float bh = 54f * k, bw = Mathf.Min(W * 0.4f, 230f * k), m = 18f * k;
            exitRect = new Rect(safe.xMax - m - bw, (Screen.height - safe.y) - m - bh, bw, bh);
            GUI.color = new Color(1f, 1f, 1f, a * Smooth(Mathf.Clamp01((t - 0.35f) / 0.4f)) * 0.85f);
            var bs = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(19f * k), fontStyle = FontStyle.Bold };
            bs.normal.textColor = bs.hover.textColor = new Color(0.95f, 0.92f, 0.85f);
            if (GUI.Button(exitRect, Loc.T(AppExit.ButtonLabel), bs)) AppExit.RequestFromTitle();
        }
        GUI.color = keep;
    }

    // GlyphFont の文字の絵を中央揃えで1行に(キャップハイト capH の画素)。文字の絵は「キャップハイト = 1 単位」で作ってあり、
    //  幅/高さ/ベースラインは GlyphFont.Measure と同じ測り方(bounds)にする
    static void DrawGlyphs(string text, float cx, float cy, float capH, float alpha)
    {
        if (alpha <= 0.001f) return;
        float total = GlyphFont.Measure(text) * capH;
        float x = cx - total * 0.5f, baseY = cy + capH * 0.5f;
        Color keep = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        foreach (char ch in text)
        {
            var sp = GlyphFont.Get(ch);
            if (sp == null) { x += GlyphFont.SpaceWidth * capH; continue; }
            Bounds b = sp.bounds;
            Rect tr = sp.textureRect;
            var uv = new Rect(tr.x / sp.texture.width, tr.y / sp.texture.height, tr.width / sp.texture.width, tr.height / sp.texture.height);
            float top = baseY - (b.max.y - GlyphFont.BaselineLift) * capH;
            GUI.DrawTextureWithTexCoords(new Rect(x, top, b.size.x * capH, b.size.y * capH), sp.texture, uv, true); // 絵は文字の枠いっぱい(ピボットは下端中央なので、左端 = x)
            x += (b.size.x + GlyphFont.Tracking) * capH;
        }
        GUI.color = keep;
    }

    static float Smooth(float x) => x * x * (3f - 2f * x);
}
