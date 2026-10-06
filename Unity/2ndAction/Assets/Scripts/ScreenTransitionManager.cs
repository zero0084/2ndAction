using System;
using System.Collections;
using UnityEngine;

// OneMoreMile Presentation pass - a single shared full-screen transition used for every screen change in this project
// (TOP<->GAME, TOP<->menus, RESULT->TOP). Rendered entirely through OnGUI (no shader/extra camera) - GameManager calls
// DrawOverlay() as the LAST line of its own OnGUI, which draws on top of every uGUI canvas.
//
// 2026-10-01 整理: 遷移の種類を3つにまとめ、どの画面切り替えもこの1つの仕組みを通す(重複再生しない)。
//  ・Sweep(メニューへ進む): タップの反応を一瞬見せてから、細い金色の線が画面を斜めに横切り、線の後ろが暗くなる。
//    目立たない暗さになった瞬間に画面を切り替え、暗さが引いて次の画面が現れる。合計約0.45秒。
//  ・Fade(メニューから戻る): 短く暗くして切り替え、すぐ明るく戻る。線は出さない。合計約0.26秒。
//  ・DoorLight(ラン出発): 扉の隙間(または画面中央の縦の隙間)の光が広がって画面を包み、光が引くとコースが見える。合計約0.9秒。
//    走行は既存の開始カウントダウンが止めている(GameManagerは光が引き終わるまでカウントダウンを始めない)。
// IsTransitioning は最初から最後まで true(連打で二重に開かない/入力はこの間止める)。時間は実時間(ポーズ中も進む)。
public class ScreenTransitionManager : MonoBehaviour
{
    public static ScreenTransitionManager Instance { get; private set; }

    public enum Style { Sweep, Fade, DoorLight }

    [Header("Sweep (メニューへ進む)")]
    public float sweepTapDelay = 0.05f;   // タップした場所の反応を先に見せる
    public float sweepClose = 0.2f;
    public float sweepHold = 0.02f;
    public float sweepOpen = 0.18f;
    [Range(0f, 1f)] public float sweepDarkness = 0.93f; // 切り替える瞬間の暗さ(完全な黒にはしない=切り替えが目立たない程度)

    [Header("Fade (戻る)")]
    public float fadeClose = 0.1f;
    public float fadeHold = 0.02f;
    public float fadeOpen = 0.14f;

    [Header("DoorLight (ラン出発)")]
    public float doorClose = 0.42f;
    public float doorHold = 0.12f;
    public float doorOpen = 0.36f;
    public Color doorLightColor = new Color(1f, 0.93f, 0.76f, 1f);

    [Header("Gold Line")]
    public float sweepLineWidth = 3f;
    [Range(0f, 1f)] public float sweepLineAlpha = 0.95f;
    public float sweepGlowWidth = 18f;
    [Range(0f, 1f)] public float sweepGlowAlpha = 0.35f;
    public Color goldColor = new Color(0.95f, 0.83f, 0.45f);
    public float sweepBlueWidth = 40f;
    [Range(0f, 1f)] public float sweepBlueAlpha = 0.18f;
    public Color blueColor = new Color(0.55f, 0.85f, 1f);

    [Header("Navy")]
    public Color navyColor = new Color(0.06f, 0.08f, 0.17f, 1f);
    public float sweepAngle = 24f; // 線の傾き(度)

    [Header("SE (optional)")]
    public AudioClip closeSfx;
    public AudioClip openSfx;

    public bool IsTransitioning { get; private set; }
    public Style CurrentStyle { get; private set; }

    // 描画の状態
    float coverage;     // 0..1 暗さ/光の量(線の位置ではない)
    float sweepPos;     // Sweep: 線の位置 0(左の外)..1(右の外)
    bool closing;       // 閉じる途中(線を出す)
    Rect doorOrigin;    // DoorLight: 光が広がり始める矩形(スクリーン座標)

    // シーンの読み直しをまたいで、新しい画面を覆った状態から開く(RESULT→TOP/HOMEへ戻る/マルチの開始)
    static bool resumeOpenAfterReload;
    static Style resumeStyle;

    static Texture2D whiteTex, softTex;
    static Texture2D WhiteTex()
    {
        if (whiteTex == null) { whiteTex = new Texture2D(1, 1); whiteTex.SetPixel(0, 0, Color.white); whiteTex.Apply(); whiteTex.hideFlags = HideFlags.HideAndDontSave; }
        return whiteTex;
    }
    // 横方向に縁がぼける帯(扉の光)
    static Texture2D SoftTex()
    {
        if (softTex == null)
        {
            softTex = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int i = 0; i < 64; i++) { float d = Mathf.Abs(i - 31.5f) / 31.5f; softTex.SetPixel(i, 0, new Color(1f, 1f, 1f, Mathf.SmoothStep(1f, 0f, d))); }
            softTex.Apply();
        }
        return softTex;
    }

    void Awake()
    {
        Instance = this;
        if (resumeOpenAfterReload)
        {
            resumeOpenAfterReload = false;
            CurrentStyle = resumeStyle;
            coverage = 1f; sweepPos = 1f; closing = false;
            doorOrigin = CenterSlit();
            IsTransitioning = true;
            StartCoroutine(OpenRoutine());
        }
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // 見張り(2026-10-06): 遷移は長くても数秒。止まったまま IsTransitioning が残ると、あらゆるタップ(結果画面の Retry 等)が通らなくなる
    // → 一定時間を超えたら外す(覆いも消す)。正常な遷移はここに来ない。
    float startedRealtime = -1f;
    public static int StuckResets;
    void Update()
    {
        if (!IsTransitioning) { startedRealtime = -1f; return; }
        if (startedRealtime < 0f) startedRealtime = Time.realtimeSinceStartup;
        if (Time.realtimeSinceStartup - startedRealtime < 8f) return;
        StuckResets++;
        Debug.LogWarning($"[Transition] stuck for {Time.realtimeSinceStartup - startedRealtime:F1}s (style={CurrentStyle}, coverage={coverage:F2}) - reset so input is not blocked");
        StopAllCoroutines();
        IsTransitioning = false; coverage = 0f; closing = false;
        startedRealtime = -1f;
    }

    // onFullyCovered は画面が覆われた瞬間に1回だけ呼ばれる(ここで画面/状態を切り替える)。
    public void PlayTransition(Action onFullyCovered) => PlayTransition(onFullyCovered, Style.Sweep);

    public void PlayTransition(Action onFullyCovered, Style style, Rect? origin = null)
    {
        if (IsTransitioning) return; // 連打で二重に走らせない
        StartCoroutine(FullRoutine(onFullyCovered, style, origin));
    }

    // シーンを読み直す切り替え: 閉じて覆った状態で onReload(SceneManager.LoadScene)を呼ぶ。開くのは新しいシーンのこの部品。
    public void PlayCloseThenReload(Action onReload) => PlayCloseThenReload(onReload, Style.Sweep);

    public void PlayCloseThenReload(Action onReload, Style style)
    {
        if (IsTransitioning) return;
        StartCoroutine(CloseThenReloadRoutine(onReload, style));
    }

    IEnumerator FullRoutine(Action onFullyCovered, Style style, Rect? origin)
    {
        IsTransitioning = true;
        CurrentStyle = style;
        doorOrigin = origin ?? CenterSlit();
        yield return CloseRoutine();
        // 切り替えの処理が例外で止まっても、覆ったまま(入力不能)にしない
        try { onFullyCovered?.Invoke(); }
        catch (Exception ex) { Debug.LogException(ex); }
        yield return new WaitForSecondsRealtime(Hold());
        yield return OpenRoutine();
    }

    IEnumerator CloseThenReloadRoutine(Action onReload, Style style)
    {
        IsTransitioning = true;
        CurrentStyle = style;
        doorOrigin = CenterSlit();
        yield return CloseRoutine();
        yield return new WaitForSecondsRealtime(Hold());
        resumeOpenAfterReload = true;
        resumeStyle = style;
        onReload?.Invoke(); // この部品は直後に破棄される
    }

    float Hold() => CurrentStyle == Style.Sweep ? sweepHold : CurrentStyle == Style.Fade ? fadeHold : doorHold;

    IEnumerator CloseRoutine()
    {
        closing = true;
        if (CurrentStyle == Style.Sweep && sweepTapDelay > 0f) yield return new WaitForSecondsRealtime(sweepTapDelay);
        var am = AudioManager.Instance;
        if (am != null && CurrentStyle != Style.Fade) am.PlaySfx(AudioManager.Se(CurrentStyle == Style.DoorLight ? SeId.ScreenOpen : SeId.ScreenClose, closeSfx));
        float dur = CurrentStyle == Style.Sweep ? sweepClose : CurrentStyle == Style.Fade ? fadeClose : doorClose;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, dur);
            float p = Mathf.Clamp01(t);
            if (CurrentStyle == Style.Sweep) { sweepPos = EaseInOut(p); coverage = Mathf.Lerp(0f, sweepDarkness, EaseIn(p)); }
            else if (CurrentStyle == Style.Fade) coverage = Mathf.Lerp(0f, 0.94f, EaseOut(p));
            else coverage = EaseIn(p);
            yield return null;
        }
        sweepPos = 1f;
        coverage = CurrentStyle == Style.Sweep ? sweepDarkness : CurrentStyle == Style.Fade ? 0.94f : 1f;
        closing = false;
    }

    IEnumerator OpenRoutine()
    {
        float start = coverage;
        var am = AudioManager.Instance;
        if (am != null && CurrentStyle == Style.Sweep) am.PlaySfx(AudioManager.Se(SeId.ScreenOpen, openSfx));
        float dur = CurrentStyle == Style.Sweep ? sweepOpen : CurrentStyle == Style.Fade ? fadeOpen : doorOpen;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, dur);
            coverage = start * (1f - EaseOut(Mathf.Clamp01(t)));
            yield return null;
        }
        coverage = 0f;
        IsTransitioning = false;
    }

    static float EaseIn(float x) => x * x;
    static float EaseOut(float x) => 1f - (1f - x) * (1f - x);
    static float EaseInOut(float x) => x < 0.5f ? 2f * x * x : 1f - Mathf.Pow(-2f * x + 2f, 2f) * 0.5f;

    // 扉が見えていない時(ステージ選択からの出発/読み直し)の光の始まり: 画面中央の縦の細い隙間
    static Rect CenterSlit() => new Rect(Screen.width * 0.5f - Screen.width * 0.01f, Screen.height * 0.2f, Screen.width * 0.02f, Screen.height * 0.6f);

    // GameManagerのOnGUIの最後(どの画面でも一番手前)から呼ばれる。
    public void DrawOverlay()
    {
        if (!IsTransitioning && coverage <= 0.0001f) return;
        Color saved = GUI.color;
        Matrix4x4 savedMatrix = GUI.matrix;
        switch (CurrentStyle)
        {
            case Style.Sweep: DrawSweep(); break;
            case Style.Fade: DrawFill(navyColor, coverage); break;
            case Style.DoorLight: DrawDoorLight(); break;
        }
        GUI.color = saved;
        GUI.matrix = savedMatrix;
    }

    void DrawFill(Color c, float a)
    {
        if (a <= 0.001f) return;
        GUI.color = new Color(c.r, c.g, c.b, a);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), WhiteTex());
    }

    void DrawSweep()
    {
        // 画面全体が線の進みに合わせて少し暗くなり、線の後ろはさらに暗い(線が通り過ぎた所から暗くなる)
        float diag = Mathf.Sqrt(Screen.width * (float)Screen.width + Screen.height * (float)Screen.height);
        Vector2 pivot = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        float big = diag * 2.2f;
        float x = Mathf.Lerp(-diag * 0.6f, diag * 0.6f, sweepPos); // 線の位置(回転した座標で)
        if (!closing)
        {
            DrawFill(navyColor, coverage); // 覆った後/開く: 全体が均一に明るく戻る
            return;
        }
        DrawFill(navyColor, coverage * 0.45f);
        GUIUtility.RotateAroundPivot(sweepAngle, pivot);
        GUI.color = new Color(navyColor.r, navyColor.g, navyColor.b, Mathf.Clamp01(coverage * 1.1f));
        GUI.DrawTexture(new Rect(pivot.x - big, pivot.y - big * 0.5f, big + x, big), WhiteTex()); // 線より後ろ(左)
        Rect Strip(float w) => new Rect(pivot.x + x - w * 0.5f, pivot.y - big * 0.5f, w, big);
        GUI.color = new Color(blueColor.r, blueColor.g, blueColor.b, sweepBlueAlpha);
        GUI.DrawTexture(Strip(sweepBlueWidth), SoftTex());
        GUI.color = new Color(goldColor.r, goldColor.g, goldColor.b, sweepGlowAlpha);
        GUI.DrawTexture(Strip(sweepGlowWidth), SoftTex());
        GUI.color = new Color(goldColor.r, goldColor.g, goldColor.b, sweepLineAlpha);
        GUI.DrawTexture(Strip(sweepLineWidth), WhiteTex());
    }

    void DrawDoorLight()
    {
        float a = coverage;
        if (a <= 0.001f) return;
        // 光の矩形: 扉の隙間(細い縦帯) → 画面全体へ。閉じる途中は広がり、覆った後/開く時は全面の光が薄れていく。
        Rect full = new Rect(-Screen.width * 0.1f, -Screen.height * 0.1f, Screen.width * 1.2f, Screen.height * 1.2f);
        Rect slit = new Rect(doorOrigin.center.x - doorOrigin.width * 0.08f, doorOrigin.y, doorOrigin.width * 0.16f, doorOrigin.height);
        if (closing)
        {
            float e = coverage; // 0..1(EaseIn)
            Rect r = Lerp(slit, full, Mathf.SmoothStep(0f, 1f, e));
            // 周りが少し暗くなり、光が浮かび上がる
            DrawFill(navyColor, 0.35f * Mathf.Sin(Mathf.Clamp01(e) * Mathf.PI));
            // 光のにじみ(大きく薄い) → 本体
            Rect halo = new Rect(r.x - r.width * 0.6f - 40f, r.y - 30f, r.width * 2.2f + 80f, r.height + 60f);
            GUI.color = new Color(doorLightColor.r, doorLightColor.g, doorLightColor.b, 0.45f * Mathf.Clamp01(e * 3f));
            GUI.DrawTexture(halo, SoftTex());
            GUI.color = new Color(doorLightColor.r, doorLightColor.g, doorLightColor.b, Mathf.Lerp(0.85f, 1f, e));
            GUI.DrawTexture(r, e > 0.85f ? WhiteTex() : SoftTex());
            if (e > 0.6f) DrawFill(doorLightColor, (e - 0.6f) / 0.4f);
        }
        else DrawFill(doorLightColor, a);
    }

    static Rect Lerp(Rect a, Rect b, float t) => new Rect(Mathf.Lerp(a.x, b.x, t), Mathf.Lerp(a.y, b.y, t), Mathf.Lerp(a.width, b.width, t), Mathf.Lerp(a.height, b.height, t));
}
