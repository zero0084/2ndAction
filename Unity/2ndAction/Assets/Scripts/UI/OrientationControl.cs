using UnityEngine;
using UnityEngine.UI;

// 画面の向き(2026-10-08): 横画面は左右どちらの横持ちにも回る(スマホを 180 度ひっくり返しても正しい向き)。縦にはしない。
//  ・設定で縦画面を選んだ時だけ縦に固定(今までどおり)。
//  ・回った瞬間: 触っていた指の状態を捨てる(誤フリック/入力の残りを防ぐ)。ラン/入力内容/画面はリセットしない。
//  ・安全領域(カメラ穴): IMGUI の画面は毎回 Screen.safeArea を読む。uGUI のメニュー画面(キャラ/ステージ選択・デッキ編集・合成・確認)は
//    SafeAreaFitter で中身を左右の安全領域の内側へ寄せる(背景は画面いっぱいのまま)。
public static class OrientationControl
{
    public static void Apply(ScreenOrientation preferred)
    {
        if (preferred == ScreenOrientation.Portrait || preferred == ScreenOrientation.PortraitUpsideDown)
        {
            Screen.orientation = ScreenOrientation.Portrait;
            return;
        }
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;
        Screen.orientation = ScreenOrientation.AutoRotation;
    }
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
    void Snap() { lastSafe = Screen.safeArea; lastW = Screen.width; lastH = Screen.height; lastOri = Screen.orientation; }

    void Update()
    {
        if (Screen.safeArea == lastSafe && Screen.width == lastW && Screen.height == lastH && Screen.orientation == lastOri) return;
        Debug.Log($"[Orientation] {lastOri} {lastW}x{lastH} -> {Screen.orientation} {Screen.width}x{Screen.height} safe={Screen.safeArea}");
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
    RectTransform rt; Vector2 baseMin, baseMax; Rect applied; bool init;
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
    void LateUpdate() { if (Screen.safeArea != applied) Fit(); }

    void Fit()
    {
        if (rt == null) return;
        applied = Screen.safeArea;
        var canvas = GetComponentInParent<Canvas>();
        float scale = canvas != null && canvas.rootCanvas != null ? Mathf.Max(0.01f, canvas.rootCanvas.scaleFactor) : 1f;
        float left = Screen.safeArea.xMin / scale, right = (Screen.width - Screen.safeArea.xMax) / scale;
        rt.offsetMin = new Vector2(baseMin.x + left, baseMin.y);
        rt.offsetMax = new Vector2(baseMax.x - right, baseMax.y);
        foreach (var b in fullBleed)
        {
            if (b == null) continue;
            b.offsetMin = new Vector2(-left, b.offsetMin.y);
            b.offsetMax = new Vector2(right, b.offsetMax.y);
        }
    }
}
