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
//
// 2026-10-26 Android の起動ロゴはこちら(方法B)。Windows は Unity の起動画面の背景の絵(方法A、BrandSplashSetup)のまま。
//  Android の実機で方法A のロゴがほとんど見えなかった(Unity の起動画面は最初のシーンの読み込みが終わると終わり、絵はその読み込みの後に
//  描かれる → 端末の速さとキャッシュ次第で 0〜一瞬)。Android のビルドは Unity の起動画面を背景色だけにする(BrandSplashSetup.AndroidScope)。
//  ・出すのは Android だけ(Windows で方法A と二重にしない)。開発版は -bootSplash 1 でどの機種でも出せる(確認用)
//  ・背景は起動画面と同じ #060810、ロゴの大きさも Windows の起動画面の絵と同じ(横: 高さの5割 / 縦: 高さの3割)
//  ・流れ: 背景のまま → ロゴが 0.3秒で出る → 1.2秒見せる → 0.35秒で消える → 背景が 0.2秒で消えてホーム(ロゴが見えている時間 約1.5秒)
//  ・時刻のログ(製品版も): [BootSplash] logo visible / logo hidden / done(Time.realtimeSinceStartup、[Boot] の行と同じ時計)
//  ・画面がまだ回っている間(設定は横なのに縦の窓、など)はロゴを出さず背景だけ(最大 RotateWait 秒)。縦持ちの端末で起動すると
//    ゲームが設定の向きへ回すので、その回転がロゴの途中に重ならないように(回る必要が無ければ待たない)
public class BootSplash : MonoBehaviour
{
    public const string LogoResource = "Brand/MIXGame_Logo";
    public static bool Showing { get; private set; }
    public static bool BlocksInput => Showing;

    // 時間(秒)。毎回見るので短く・静かに
    const float FadeIn = 0.30f, Hold = 1.20f, FadeOut = 0.35f, BgOut = 0.20f;
    const float SkipAfter = 0.80f;                    // 起動直後の誤タップは数えない(ロゴを少なくとも一度は見せる)
    const float SkipFadeOut = 0.20f, SkipBgOut = 0.15f;
    const float MaxWaitReady = 15f;                   // 準備が終わらなくてもここ(出始めからの実時間)で閉じる(固まらないように)。ただしロゴを見せ終わってから
    const float MaxStep = 0.10f;                      // 演出の時計を1フレームで進める上限(秒)
    const float RotateWait = 1.5f;                    // 画面の向きがそろうのを待つ上限(実時間、秒)
    static readonly Color Bg = new Color(6f / 255f, 8f / 255f, 16f / 255f, 1f); // #060810(Unity の起動画面の背景色と同じ)

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
        if (!PlatformUsesGameLogo()) return;
        Texture2D tex = LoadLogo();
        if (tex == null) return; // 正式なロゴが無い: 出さない
        var go = new GameObject("[BootSplash]");
        DontDestroyOnLoad(go);
        var s = go.AddComponent<BootSplash>();
        s.logo = tex;
        Showing = true;
    }

    // この機種で出すか: Android だけ(Windows/Editor は Unity の起動画面にロゴがある)。開発版は -bootSplash 1 / -bootSplashLogo で強制
    static bool PlatformUsesGameLogo()
    {
        if (Application.platform == RuntimePlatform.Android) return true;
        if (DevGate.On)
        {
            string[] a = Args;
            int i = Array.IndexOf(a, "-bootSplash");
            if (i >= 0 && i + 1 < a.Length && a[i + 1] == "1") return true;
            if (Array.IndexOf(a, "-bootSplashLogo") >= 0) return true;
        }
        return false;
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

    // 画面の向きが設定どおりか(横の設定なのに縦の窓 = まだ回っている途中)。自動の設定は端末に任せるので待たない
    static bool OrientationSettled()
    {
        bool portraitNow = Screen.height > Screen.width;
        switch (OrientationControl.Current)
        {
            case OrientationControl.Mode.Landscape: return !portraitNow;
            case OrientationControl.Mode.Portrait: return portraitNow;
            default: return true;
        }
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

    // 演出の時計: 1フレームで最大 MaxStep(0.1秒)だけ進める(起動直後の重いフレームでフェードインやロゴが飛ばないように。
    // 2026-10-26 1/30秒 → 0.1秒: 遅い端末で最初の数フレームが重いと、1/30秒では演出が何倍にも延びるため)。
    // Time.unscaledDeltaTime は Unity がなめらかにするので、実時間(realtimeSinceStartup)の差で測る
    float clock, lastReal = -1f, realStart = -1f;
    float Clock => clock;
    string shotDir; float nextShot; int shotN;
    float testSkipAt = -2f; bool testSkipped;
    void Update()
    {
        float real = Time.realtimeSinceStartup;
        if (lastReal >= 0f) clock += Mathf.Min(real - lastReal, MaxStep);
        lastReal = real;
        if (realStart < 0f) realStart = real;
        float now = Clock;
        if (DevGate.On && shotDir == null) { int si = Array.IndexOf(Args, "-bootSplashShots"); shotDir = si >= 0 && si + 1 < Args.Length ? Args[si + 1] : ""; if (shotDir != "") System.IO.Directory.CreateDirectory(shotDir); }
        if (!string.IsNullOrEmpty(shotDir) && now >= nextShot) { nextShot = now + 0.1f; ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(shotDir, $"bs_{shotN++:000}_{now - Mathf.Max(0f, t0):F2}.png")); } // 開発版だけ: 確認の撮影
        if (t0 < 0f)
        {
            if (!OrientationSettled() && real - realStart < RotateWait) { clock = 0f; return; } // 回り終えるまで背景だけ
            t0 = now; Debug.Log($"[BootSplash] start real={real:F2}s screen={Screen.width}x{Screen.height} (waited {real - realStart:F2}s for orientation)");
        }
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
            // 安全装置: ロゴを見せ終わってから(timeUp)、さらに準備を待っても終わらない時だけ閉じる。ロゴの時間は削らない(2026-10-26)
            else if (timeUp && real - realStart >= MaxWaitReady) { Debug.LogWarning("[BootSplash] home was not ready in time - closing anyway"); Begin(false, t); }
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
        Debug.Log($"[BootSplash] done at {Clock - t0:F2}s (real {Time.realtimeSinceStartup - realStart:F2}s, real={Time.realtimeSinceStartup:F2}s) logo visible {(visibleAt >= 0f && hiddenAt >= 0f ? hiddenAt - visibleAt : -1f):F2}s");
        if (logo != null && !ReferenceEquals(logo, Resources.Load<Texture2D>(LogoResource))) Destroy(logo);
        Destroy(gameObject);
    }

    // ロゴが「見えている」時間の記録(濃さ半分を超えた時刻〜下回った時刻、実時間)
    float visibleAt = -1f, hiddenAt = -1f;
    void NoteAlpha(float a)
    {
        if (Event.current.type != EventType.Repaint) return;
        float r = Time.realtimeSinceStartup;
        if (visibleAt < 0f && a >= 0.5f) { visibleAt = r; Debug.Log($"[BootSplash] logo visible real={r:F2}s"); }
        else if (visibleAt >= 0f && hiddenAt < 0f && a < 0.5f) { hiddenAt = r; Debug.Log($"[BootSplash] logo hidden real={r:F2}s (shown {r - visibleAt:F2}s)"); }
    }

    void OnGUI()
    {
        if (t0 < 0f)
        {
            if (realStart < 0f) return;
            GUI.depth = -32000; // 向きがそろうのを待つ間: 背景だけ
            Color p0 = GUI.color; GUI.color = Bg;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = p0;
            return;
        }
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
            // Windows の起動画面の絵(Assets/Art/Brand)と同じ大きさ: 横画面は高さの5割、縦画面は高さの3割。幅が足りなければ幅に合わせる
            bool portrait = H > W;
            float maxW = portrait ? W * 0.85f : W * 0.80f, maxH = portrait ? H * 0.30f : H * 0.50f;
            float aspect = logo.width / (float)Mathf.Max(1, logo.height);
            float h = maxH, w = h * aspect;
            if (w > maxW) { w = maxW; h = w / aspect; }
            var r = new Rect(Mathf.Round((W - w) * 0.5f), Mathf.Round((H - h) * 0.5f), Mathf.Round(w), Mathf.Round(h));
            GUI.color = new Color(1f, 1f, 1f, logoA * bgA);
            GUI.DrawTexture(r, logo, ScaleMode.ScaleToFit, true);
        }
        NoteAlpha(logo != null ? logoA * bgA : 0f);
        GUI.color = prev;
    }

    static float Smooth(float x) => x * x * (3f - 2f * x);
}
