using UnityEngine;
using UnityEngine.UI;

// 画面の向き(2026-10-08): 横画面は左右どちらの横持ちにも回る(スマホを 180 度ひっくり返しても正しい向き)。縦にはしない。
//  ・設定で縦画面を選んだ時だけ縦に固定(今までどおり)。
//  ・回った瞬間: 触っていた指の状態を捨てる(誤フリック/入力の残りを防ぐ)。ラン/入力内容/画面はリセットしない。
//  ・安全領域(カメラ穴): IMGUI の画面は毎回 Screen.safeArea を読む。uGUI のメニュー画面(キャラ/ステージ選択・デッキ編集・合成・確認)は
//    SafeAreaFitter で中身を左右の安全領域の内側へ寄せる(背景は画面いっぱいのまま)。
// 2026-10-09(依頼G): 「横画面」を選んでも、端末の自動回転が OFF だと縦のままのことがあった。
//  横の AutoRotation(横だけ許可)は Android では端末の回転の固定に従うことがあり、縦で固定されている端末では横へ回らない。
//  → まず横に「固定」して必ず回し(端末の設定に関係なく回る)、回り終えてから左右どちらの横持ちにも回れるようにする。
//  縦: 縦に固定。自動: 端末の回転の設定に従う(縦も横も)。選んだ向きは保存され、起動時/復帰時にもう一度当てる。
public static class OrientationControl
{
    public enum Mode { Landscape = 0, Portrait = 1, Auto = 2 }
    public static Mode Current { get; private set; } = Mode.Landscape;

    public static Mode FromSaved(ScreenOrientation o) => o == ScreenOrientation.AutoRotation ? Mode.Auto : (o == ScreenOrientation.Portrait || o == ScreenOrientation.PortraitUpsideDown) ? Mode.Portrait : Mode.Landscape;
    public static ScreenOrientation ToSaved(Mode m) => m == Mode.Auto ? ScreenOrientation.AutoRotation : m == Mode.Portrait ? ScreenOrientation.Portrait : ScreenOrientation.LandscapeLeft;

    public static void Apply(ScreenOrientation preferred) => Apply(FromSaved(preferred));
    public static void Apply(Mode m)
    {
        Current = m;
        landscapeUnlockAt = -1f;
        if (m == Mode.Portrait)
        {
            Screen.autorotateToPortrait = true; Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = false; Screen.autorotateToLandscapeRight = false;
            Screen.orientation = ScreenOrientation.Portrait;
            return;
        }
        if (m == Mode.Auto)
        {
            Screen.autorotateToPortrait = true; Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true; Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;
            return;
        }
        // 横: いったん横へ固定(今が右向きの横ならそのまま右向き)→ 少し後に左右どちらの横にも回れるようにする
        Screen.autorotateToPortrait = false; Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = true; Screen.autorotateToLandscapeRight = true;
        Screen.orientation = Screen.orientation == ScreenOrientation.LandscapeRight ? ScreenOrientation.LandscapeRight : ScreenOrientation.LandscapeLeft;
        landscapeUnlockAt = Time.unscaledTime + 0.8f;
    }

    // 横に固定してから、横の範囲での自動回転に切り替える時刻(OrientationWatcher が見る)
    static float landscapeUnlockAt = -1f;
    public static void Tick()
    {
        if (landscapeUnlockAt < 0f || Time.unscaledTime < landscapeUnlockAt) return;
        landscapeUnlockAt = -1f;
        if (Current != Mode.Landscape) return;
        if (Screen.width < Screen.height) { landscapeUnlockAt = Time.unscaledTime + 0.5f; Screen.orientation = ScreenOrientation.LandscapeLeft; return; } // まだ回っていない: もう一度固定して待つ
        Screen.orientation = ScreenOrientation.AutoRotation;
    }

    // アプリへ戻った時(バックグラウンドからの復帰): 端末側で向きが変わっていることがあるので、選んでいる向きを当て直す
    public static void Reapply() => Apply(Current);
}

// 回転/画面の大きさ/安全領域が変わったのを見張る
public class OrientationWatcher : MonoBehaviour
{
    public static int Changes { get; private set; }
    public static event System.Action Changed;
    Rect lastSafe; int lastW, lastH; ScreenOrientation lastOri;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("[OrientationWatcher]");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideInHierarchy;
        go.AddComponent<OrientationWatcher>();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => SafeAreaFitter.AttachToMenus();
        SafeAreaFitter.AttachToMenus();
    }

    void Start() { Snap(); }
    // 安全領域の記録([SafeArea])に「何の後か」を残す(2026-10-10、HUD のずれを実機で追えるように)
    void OnApplicationPause(bool paused) { StableSafeArea.LastReason = paused ? "pause" : "resume"; if (!paused) OrientationControl.Reapply(); }
    void OnApplicationFocus(bool focus) { StableSafeArea.LastReason = focus ? "focus" : "focus lost"; if (focus) OrientationControl.Reapply(); }
    void Snap() { lastSafe = Screen.safeArea; lastW = Screen.width; lastH = Screen.height; lastOri = Screen.orientation; }

    void Update()
    {
        OrientationControl.Tick();
        if (Screen.safeArea == lastSafe && Screen.width == lastW && Screen.height == lastH && Screen.orientation == lastOri) return;
        Debug.Log($"[Orientation] {lastOri} {lastW}x{lastH} -> {Screen.orientation} {Screen.width}x{Screen.height} safe={Screen.safeArea} used={StableSafeArea.Rect}");
        if (Screen.orientation != lastOri || Screen.width != lastW) StableSafeArea.LastReason = "rotation";
        Snap();
        Changes++;
        // 回っている間に触っていた指は捨てる(離すまで入力にしない)
        if (PlayerController.Instance != null) PlayerController.Instance.ClearPointerState();
        UiInputGate.LatchUntilRelease();
        Changed?.Invoke();
    }
}

// uGUI の画面の中身を左右の安全領域の内側へ寄せる(背景 "Background"/"Backdrop" は画面いっぱいのまま)
public class SafeAreaFitter : MonoBehaviour
{
    RectTransform rt; Vector2 baseMin, baseMax; Rect applied; Vector2Int appliedSize; bool init;
    RectTransform[] fullBleed;

    public static void AttachToMenus()
    {
        void Add(Component c)
        {
            if (c == null) return;
            var r = c.transform as RectTransform;
            if (r == null || c.GetComponent<Canvas>() != null || c.GetComponent<SafeAreaFitter>() != null) return;
            c.gameObject.AddComponent<SafeAreaFitter>();
        }
        foreach (var c in Object.FindObjectsByType<StageSelectUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Add(c);
        foreach (var c in Object.FindObjectsByType<CharacterSelectUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Add(c);
        foreach (var c in Object.FindObjectsByType<DeckEditUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Add(c);
        foreach (var c in Object.FindObjectsByType<CardFusionUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Add(c);
        foreach (var c in Object.FindObjectsByType<ConfirmDialogUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Add(c);
    }

    void Awake() { Init(); }
    void Init()
    {
        if (init) return;
        init = true;
        rt = transform as RectTransform;
        baseMin = rt.offsetMin; baseMax = rt.offsetMax;
        var list = new System.Collections.Generic.List<RectTransform>();
        foreach (Transform ch in transform) if (ch.name == "Background" || ch.name == "Backdrop") list.Add(ch as RectTransform);
        fullBleed = list.ToArray();
    }

    void OnEnable() { Init(); applied = default; Fit(); }
    void LateUpdate() { if (StableSafeArea.Rect != applied || appliedSize.x != Screen.width || appliedSize.y != Screen.height) Fit(); }

    void Fit()
    {
        if (rt == null) return;
        applied = StableSafeArea.Rect; appliedSize = new Vector2Int(Screen.width, Screen.height);
        var canvas = GetComponentInParent<Canvas>();
        float scale = canvas != null && canvas.rootCanvas != null ? Mathf.Max(0.01f, canvas.rootCanvas.scaleFactor) : 1f;
        float left = StableSafeArea.Rect.xMin / scale, right = (Screen.width - StableSafeArea.Rect.xMax) / scale;
        // 2026-10-08(依頼E-1): 縦画面の切り欠き/システムバーのため上下も
        float bottom = StableSafeArea.Rect.yMin / scale, top = (Screen.height - StableSafeArea.Rect.yMax) / scale;
        rt.offsetMin = new Vector2(baseMin.x + left, baseMin.y + bottom);
        rt.offsetMax = new Vector2(baseMax.x - right, baseMax.y - top);
        foreach (var b in fullBleed)
        {
            if (b == null) continue;
            b.offsetMin = new Vector2(-left, -bottom);
            b.offsetMax = new Vector2(right, top);
        }
    }
}
