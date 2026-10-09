using UnityEngine;

// 縦画面の「折り返して見る」表示(2026-10-09、依頼G-3 の代案・試作)。表示だけ(判定/位置/敵の順番/出現は変えない)。
//  画面を上下2段に分けて見せる(実際の画面は1枚。カメラを2台に):
//   下の段 = 今までの「横から見る」をそのまま(メインのカメラの映す範囲を下半分へ。大きさ=1m あたりの画素は同じ)
//   上の段 = 下の段の右端のさらに先(次の画面1枚分)を左右反転して映す。下の段の右端と上の段の右端が同じ場所 = 道が右端で折り返して上の段へ続く
//  → 先の敵/障害物/穴は、まず上の段の左から現れて右へ流れ、右端で折り返して下の段の右から入ってくる。
//  ・メインのカメラを下の段にする(位置/大きさは CameraFollow が決めた物から毎フレーム作り直し、次のフレームの初めに戻す = 追従の計算に混ざらない)。
//    世界の位置→画面の位置の計算(ダメージの数字など Camera.main を使う物)は下の段に正しく合う
//  ・背景(BackgroundFollower)は2段とも覆う広さにする(BgCoverShift/BgCoverScaleX)
//  ・設定「縦画面のラン表示」の「上下2段で見る」(PortraitRunView.Fold、2026-10-09 採用)。開発用の起動引数 -foldView 1 でも強制できる
[DefaultExecutionOrder(-40)] // CameraFollow(-50)の後、背景(100)の前
public class FoldView : MonoBehaviour
{
    public static FoldView Instance { get; private set; }
    public static bool Requested;   // 開発用: -foldView 1(設定に関係なく使う)
    public static bool Active => Instance != null && Instance.active;

    [Range(0.35f, 0.65f)] public float bottomFrac = 0.52f;   // 下の段の高さ(画面の割合)
    [Range(0.15f, 0.5f)] public float groundInBand = 0.30f;   // 走っている地表を段の下から何割の所に
    // 段が低くなった分、少し寄って キャラを大きく(1 = 「横から見る」と同じ大きさ)。先は上の段で見えるので、寄っても前方は広い
    public static float Zoom = 0.8f;
    public Color dividerColor = new Color(0.02f, 0.03f, 0.07f, 1f);
    public Color dividerGold = new Color(0.95f, 0.78f, 0.36f, 0.95f);

    Camera cam, upper;
    CameraFollow follow;
    bool active, saved;
    Vector3 savedPos;
    Rect savedRect;

    // 背景を2段とも覆うための、中心のずれ(m)と横幅の倍率
    public static float BgCoverShift { get; private set; }
    public static float BgCoverScaleX { get; private set; } = 1f;
    public static float HalfWidthWorld { get; private set; }
    // 画面を覆う物(背景/洞窟の暗さ/幕)用: 上下2段の時だけ中心のずれと横幅の倍率、それ以外は 0 と 1
    //  斜め上の上下2段(FoldViewOblique)では、上の段のカメラが道の先へ Ahead m ずれる分
    public static float CoverShiftX => Active ? BgCoverShift : FoldViewOblique.Active ? FoldViewOblique.Ahead * 0.5f : 0f;
    public static float CoverScaleX => Active ? BgCoverScaleX : FoldViewOblique.Active ? 1.6f : 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ReadArgs()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
        {
            if (a[i] == "-foldView") Requested = a[i + 1] != "0";
            if (a[i] == "-foldZoom") float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out Zoom);
        }
#endif
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => Attach();
    }

    static void Attach()
    {
        var cf = Object.FindFirstObjectByType<CameraFollow>();
        if (cf != null && cf.GetComponent<FoldView>() == null) cf.gameObject.AddComponent<FoldView>();
    }

    void Awake()
    {
        Instance = this;
        cam = GetComponent<Camera>();
        follow = GetComponent<CameraFollow>();
        savedRect = cam != null ? cam.rect : new Rect(0, 0, 1, 1);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (upper != null) Destroy(upper.gameObject);
    }

    bool Want()
    {
        if (!(Requested || PortraitRunView.UseFold) || cam == null || follow == null || !cam.enabled) return false;
        if (!follow.PortraitSide) return false; // 縦画面の「横から見る」の時だけ
        var gm = GameManager.Instance;
        return gm != null && gm.HasStarted;
    }

    // 次のフレームの追従の計算(CameraFollow)には、元の位置を渡す
    void Update()
    {
        if (saved) { transform.position = savedPos; saved = false; }
    }

    void LateUpdate()
    {
        bool want = Want();
        if (!want)
        {
            if (active) TurnOff();
            return;
        }
        active = true;
        float f = bottomFrac;
        float ortho = cam.orthographicSize; // CameraFollow がこのフレームに決めた値
        float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 0.5625f;
        float z = Mathf.Clamp(Zoom, 0.5f, 1.2f);
        float halfW = ortho * aspect * z;    // 横幅の半分(m)
        float orthoB = ortho * f * z;

        savedPos = transform.position; saved = true;
        // 地表の高さ = CameraFollow の置き方(地表を画面の上から portraitGroundFromTop の所)から逆算
        float surfaceY = savedPos.y - ortho * (2f * follow.portraitGroundFromTop - 1f);
        float y = surfaceY + orthoB * (1f - 2f * groundInBand);
        var pc = PlayerController.Instance;
        if (pc != null) y = Mathf.Max(y, pc.transform.position.y + 3f - orthoB * 0.85f); // 高く跳んでも頭が段の上から出ない
        transform.position = new Vector3(savedPos.x, y, savedPos.z);
        cam.rect = new Rect(0f, 0f, 1f, f);
        cam.orthographicSize = orthoB;

        HalfWidthWorld = halfW;
        BgCoverShift = halfW;      // 下の段の左端 〜 上の段の左端(= 右へ画面2枚分)を覆う
        BgCoverScaleX = 2.2f;

        EnsureUpper();
        upper.enabled = true;
        upper.orthographicSize = orthoB;
        upper.rect = new Rect(0f, f, 1f, 1f - f);
        upper.transform.position = new Vector3(savedPos.x + 2f * halfW, y, savedPos.z);
        upper.transform.rotation = transform.rotation;
        upper.ResetProjectionMatrix();
        upper.projectionMatrix = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f)) * upper.projectionMatrix; // 左右反転
    }

    void TurnOff()
    {
        active = false;
        if (saved) { transform.position = savedPos; saved = false; }
        if (cam != null) cam.rect = savedRect;
        if (upper != null) upper.enabled = false;
        BgCoverShift = 0f; BgCoverScaleX = 1f;
    }

    void EnsureUpper()
    {
        if (upper != null) return;
        var go = new GameObject("FoldUpperCamera");
        go.transform.SetParent(null, false);
        DontDestroyOnLoad(go);
        upper = go.AddComponent<Camera>();
        upper.CopyFrom(cam);
        upper.depth = cam.depth + 0.5f;
        upper.clearFlags = cam.clearFlags;
        go.AddComponent<Mirror>();
    }

    // 反転したカメラでは面の表裏が逆になる(裏面を描かない物が消えないように)
    class Mirror : MonoBehaviour
    {
        void OnPreRender() { GL.invertCulling = true; }
        void OnPostRender() { GL.invertCulling = false; }
    }

    // 2段の境目(細い影+金の線)と、右端の折り返しの印
    void OnGUI()
    {
        if (!active || Event.current.type != EventType.Repaint) return;
        GUI.depth = 900; // HUD より後ろ
        float yLine = Screen.height * (1f - bottomFrac);
        float th = Mathf.Max(3f, Screen.height * 0.004f);
        UiKit.Fill(new Rect(0f, yLine - th * 1.5f, Screen.width, th * 3f), dividerColor);
        UiKit.Fill(new Rect(0f, yLine - th * 0.5f, Screen.width, Mathf.Max(1f, th * 0.6f)), dividerGold);
        // 右端: 道が折り返す所(上下の段をつなぐ金の縦の印)
        float w = Mathf.Max(4f, Screen.width * 0.012f), h = Screen.height * 0.12f;
        UiKit.Fill(new Rect(Screen.width - w, yLine - h * 0.5f, w, h), new Color(dividerGold.r, dividerGold.g, dividerGold.b, 0.7f));
    }
}
