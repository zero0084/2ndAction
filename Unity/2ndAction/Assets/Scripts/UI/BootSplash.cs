using System;
using UnityEngine;

// 起動時の開発者ロゴ(MIXGame、2026-10-24)。Unity の起動ロゴは出さない(Player Settings → Splash Image → Show Splash Screen を OFF)。
// アプリを起動した最初の1回だけ: 暗い背景 → ロゴがフェードイン → 短く表示 → フェードアウト → ホーム(全体 約1.6秒)。
//  ・ロゴの絵は Resources/Brand/MIXGame_Logo(Texture2D)。**正式なロゴが決まるまで置かない** → 絵が無ければ何もしない(今までどおりすぐホーム)
//  ・タップ/クリック/キーで飛ばせる。ただしホームの準備(GameManager)ができるまでは暗い背景のまま待つ(読み込みは省かない)
//  ・表示中はホームの操作を止める(UiInputGate)。飛ばした指はホームへ通さない(LatchUntilRelease)
//  ・IMGUI で描く(ホームの HUD は IMGUI で、uGUI より上に描かれるため)。一番手前(GUI.depth を最小)
//  ・シーンの読み直し(ラン → ホーム)では出さない。自動試験(-qa… / -storeCapture / -netAuto…)の時も出さない
//  ・開発版だけ: -bootSplashLogo <png のパス> で、プロジェクトに入れずに絵を試せる。-bootSplash 0 で出さない
public class BootSplash : MonoBehaviour
{
    public const string LogoResource = "Brand/MIXGame_Logo";
    public static bool Showing { get; private set; }
    public static bool BlocksInput => Showing;

    // 時間(秒)。毎回見るので短く・静かに
    const float FadeIn = 0.40f, Hold = 0.70f, FadeOut = 0.40f, BgOut = 0.25f;
    const float SkipAfter = 0.15f;                    // 起動直後の誤タップは数えない
    const float SkipFadeOut = 0.20f, SkipBgOut = 0.15f;
    const float MaxWaitReady = 15f;                   // 準備が終わらなくてもここで閉じる(固まらないように)
    static readonly Color Bg = new Color(0.035f, 0.04f, 0.065f, 1f);

    // 起動引数(開発版は Android の qa_args.txt も読む QaArgs。製品版はコマンドラインだけ。製品版で引数を使うのは無いが、DevGate の確認の前に読むため)
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    static string[] Args => QaArgs.All;
#else
    static string[] Args => Environment.GetCommandLineArgs();
#endif
    static bool shownOnce;
    Texture2D logo;
    float t0 = -1f;
    float outStart = -1f, outFade = FadeOut, outBg = BgOut;
    bool skipRequested;

    // 起動の記録(製品版も): Unity の起動ロゴが出たか(SplashScreen.isFinished)と、最初の画面までの時間
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    static void LogBeforeSplash() => Debug.Log($"[Boot] before splash t={Time.realtimeSinceStartup:F2}s unitySplash(show)={UnityEngine.Rendering.SplashScreen.isFinished == false}");
    static bool firstFrameLogged;
    static void LogFirstFrame()
    {
        if (firstFrameLogged) return;
        firstFrameLogged = true;
        Debug.Log($"[Boot] first frame t={Time.realtimeSinceStartup:F2}s unitySplashFinished={UnityEngine.Rendering.SplashScreen.isFinished}");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        Debug.Log($"[Boot] before scene load t={Time.realtimeSinceStartup:F2}s unitySplashFinished={UnityEngine.Rendering.SplashScreen.isFinished}");
        Application.onBeforeRender += LogFirstFrame;
        if (shownOnce) return;
        shownOnce = true;
        if (SkippedByArgs()) return;
        Texture2D tex = LoadLogo();
        if (tex == null) return; // 正式なロゴが無い: 出さない
        var go = new GameObject("[BootSplash]");
        DontDestroyOnLoad(go);
        var s = go.AddComponent<BootSplash>();
        s.logo = tex;
        Showing = true;
    }

    static bool SkippedByArgs()
    {
        string[] a = Args;
        if (DevGate.On)
        {
            int i = Array.IndexOf(a, "-bootSplash");
            if (i >= 0 && i + 1 < a.Length && a[i + 1] == "0") return true;
            if (Array.IndexOf(a, "-bootSplashLogo") >= 0) return false; // 試す時は自動試験の引数があっても出す
            foreach (var s in a) if (s.StartsWith("-qa") || s.StartsWith("-netAuto") || s == "-storeCapture") return true;
        }
        return false;
    }

    static Texture2D LoadLogo()
    {
        if (DevGate.On)
        {
            string[] a = Args;
            int i = Array.IndexOf(a, "-bootSplashLogo");
            if (i >= 0 && i + 1 < a.Length)
            {
                try
                {
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (t.LoadImage(System.IO.File.ReadAllBytes(a[i + 1]))) { t.wrapMode = TextureWrapMode.Clamp; return t; }
                }
                catch (Exception e) { Debug.LogWarning("[BootSplash] test logo: " + e.Message); }
            }
        }
        return Resources.Load<Texture2D>(LogoResource);
    }

    // ホームの準備ができた: GameManager が居て、数フレーム描いた後(最初のフレームの重い処理を暗い背景の下で済ませる)
    int readyFrames;
    bool Ready()
    {
        var gm = GameManager.Instance;
        if (gm == null || LoadingScreen.Busy) { readyFrames = 0; return false; }
        return ++readyFrames > 3;
    }

    bool AnyInputDown()
    {
        if (Input.anyKeyDown) return true; // クリック/キー/パッドのボタン
        for (int i = 0; i < Input.touchCount; i++) if (Input.GetTouch(i).phase == TouchPhase.Began) return true;
        return false;
    }

    // 演出の時計: 1フレームで最大 1/30 秒だけ進める(起動直後の重いフレームでフェードインが飛ばないように)。
    // Time.unscaledDeltaTime は Unity がなめらかにするので、実時間(realtimeSinceStartup)の差で測る
    float clock, lastReal = -1f, realStart = -1f;
    float Clock => clock;
    string shotDir; float nextShot; int shotN;
    float testSkipAt = -2f; bool testSkipped;
    void Update()
    {
        float real = Time.realtimeSinceStartup;
        if (lastReal >= 0f) clock += Mathf.Min(real - lastReal, 1f / 30f);
        lastReal = real;
        if (realStart < 0f) realStart = real;
        float now = Clock;
        if (DevGate.On && shotDir == null) { int si = Array.IndexOf(Args, "-bootSplashShots"); shotDir = si >= 0 && si + 1 < Args.Length ? Args[si + 1] : ""; if (shotDir != "") System.IO.Directory.CreateDirectory(shotDir); }
        if (!string.IsNullOrEmpty(shotDir) && now >= nextShot) { nextShot = now + 0.1f; ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(shotDir, $"bs_{shotN++:000}_{now - Mathf.Max(0f, t0):F2}.png")); } // 開発版だけ: 確認の撮影
        if (t0 < 0f) { t0 = now; Debug.Log("[BootSplash] start"); }
        float t = now - t0;
        bool ready = Ready();
        if (DevGate.On && testSkipAt == -2f) { int ki = Array.IndexOf(Args, "-bootSplashTestSkipAt"); testSkipAt = ki >= 0 && ki + 1 < Args.Length ? float.Parse(Args[ki + 1], System.Globalization.CultureInfo.InvariantCulture) : -1f; }
        bool testSkip = testSkipAt >= 0f && t >= testSkipAt && !testSkipped; // 開発版だけ: 決まった時刻に「押した」ことにする(飛ばす試験)
        if (testSkip) testSkipped = true;
        if (outStart < 0f && (testSkip || (t >= SkipAfter && AnyInputDown()))) { skipRequested = true; Debug.Log($"[BootSplash] skip requested at {t:F2}s (ready {ready})"); }
        if (outStart < 0f)
        {
            bool timeUp = t >= FadeIn + Hold;
            if ((timeUp || skipRequested) && ready) Begin(skipRequested, t);
            else if (real - realStart >= MaxWaitReady) { Debug.LogWarning("[BootSplash] home was not ready in time - closing anyway"); Begin(true, t); }
        }
        else if (now - outStart >= outFade + outBg) Finish();
    }

    void Begin(bool quick, float t)
    {
        outStart = Clock;
        if (quick) { outFade = SkipFadeOut; outBg = SkipBgOut; }
        Debug.Log($"[BootSplash] fade out at {t:F2}s{(quick ? " (skipped)" : "")}");
    }

    void Finish()
    {
        Showing = false;
        UiInputGate.LatchUntilRelease(); // 飛ばした指はホームのボタンへ通さない
        Debug.Log($"[BootSplash] done at {Clock - t0:F2}s (real {Time.realtimeSinceStartup - realStart:F2}s)");
        if (logo != null && !ReferenceEquals(logo, Resources.Load<Texture2D>(LogoResource))) Destroy(logo);
        Destroy(gameObject);
    }

    void OnGUI()
    {
        if (t0 < 0f) return;
        GUI.depth = -32000; // 一番手前(ホームの HUD/画面の切り替えより上)
        float now = Clock, t = now - t0;
        float logoA, bgA = 1f;
        if (outStart < 0f) logoA = Smooth(Mathf.Clamp01(t / FadeIn));
        else
        {
            float o = now - outStart;
            float startA = Smooth(Mathf.Clamp01((outStart - t0) / FadeIn));
            logoA = startA * (1f - Smooth(Mathf.Clamp01(o / outFade)));
            bgA = 1f - Smooth(Mathf.Clamp01((o - outFade) / outBg));
        }
        float W = Screen.width, H = Screen.height;
        Color prev = GUI.color;
        GUI.color = new Color(Bg.r, Bg.g, Bg.b, bgA);
        GUI.DrawTexture(new Rect(0f, 0f, W, H), Texture2D.whiteTexture);
        if (logo != null && logoA > 0.001f)
        {
            // 画面の比率に合わせた大きさ: 横画面は高さの約4割、縦画面は幅の約8割まで(切れない・小さすぎない)
            bool portrait = H > W;
            float maxW = portrait ? W * 0.80f : W * 0.46f, maxH = portrait ? H * 0.30f : H * 0.42f;
            float aspect = logo.width / (float)Mathf.Max(1, logo.height);
            float w = maxW, h = w / aspect;
            if (h > maxH) { h = maxH; w = h * aspect; }
            float scale = Mathf.Lerp(0.97f, 1f, Smooth(Mathf.Clamp01(t / (FadeIn + Hold)))); // ほんの少し寄る
            w *= scale; h *= scale;
            var r = new Rect((W - w) * 0.5f, (H - h) * 0.5f, w, h);
            GUI.color = new Color(1f, 1f, 1f, logoA * bgA);
            GUI.DrawTexture(r, logo, ScaleMode.ScaleToFit, true);
        }
        GUI.color = prev;
    }

    static float Smooth(float x) => x * x * (3f - 2f * x);
}
