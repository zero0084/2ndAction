using UnityEngine;

// 「斜め上から見る」を上下2段にする試作(2026-10-09、マスターの依頼: 斜め上も上下で見て判断する)。表示だけ。
//  下の段 = 斜めのカメラ(PortraitCameraRig)の映す絵の下の部分(キャラのいる所)を、大きさを変えずに画面の下半分へ
//  上の段 = 同じ向きのカメラを道の先(+X に Ahead m)へずらし、左右反転して画面の上半分へ
//   → 下の段では道が右上へ、上の段では右から左上へ続く(右端で折り返すジグザグ)
//  開発用の起動引数 -foldObq 1 の時だけ(-foldObqAhead m / -foldObqFrac 0.5 で調整)。判定/出現は変えない(GameView のまま)
[DefaultExecutionOrder(-40)] // PortraitCameraRig(-50)が射影を作った後
public class FoldViewOblique : MonoBehaviour
{
    public static bool Requested;
    public static float Ahead = 22f, Frac = 0.52f, UpperLift = 0f;
    public static bool Active => instance != null && instance.active;
    static FoldViewOblique instance;

    PortraitCameraRig rig;
    Camera cam, upper;
    bool active;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ReadArgs()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var a = System.Environment.GetCommandLineArgs();
        var ci = System.Globalization.CultureInfo.InvariantCulture; var ns = System.Globalization.NumberStyles.Float;
        for (int i = 0; i < a.Length - 1; i++)
        {
            if (a[i] == "-foldObq") Requested = a[i + 1] != "0";
            if (a[i] == "-foldObqAhead") float.TryParse(a[i + 1], ns, ci, out Ahead);
            if (a[i] == "-foldObqFrac") float.TryParse(a[i + 1], ns, ci, out Frac);
            if (a[i] == "-foldObqLift") float.TryParse(a[i + 1], ns, ci, out UpperLift);
        }
#endif
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => Attach();
    }

    static void Attach()
    {
        if (!Requested) return;
        var r = Object.FindFirstObjectByType<PortraitCameraRig>(FindObjectsInactive.Include);
        if (r != null && r.cam != null && r.cam.GetComponent<FoldViewOblique>() == null) r.cam.gameObject.AddComponent<FoldViewOblique>();
    }

    void Awake()
    {
        instance = this;
        rig = GetComponent<PortraitCameraRig>() ?? FindFirstObjectByType<PortraitCameraRig>(FindObjectsInactive.Include);
        cam = GetComponent<Camera>();
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        if (upper != null) Destroy(upper.gameObject);
    }

    bool Want()
    {
        if (!Requested || rig == null || cam == null || !cam.enabled || !rig.IsActive) return false;
        var gm = GameManager.Instance;
        return gm != null && gm.HasStarted;
    }

    // 画面全体の時の射影(PortraitCameraRig と同じ: 視野角 + 中心ずらし)
    Matrix4x4 FullProjection()
    {
        float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 0.5625f;
        var m = Matrix4x4.Perspective(cam.fieldOfView, aspect, cam.nearClipPlane, cam.farClipPlane);
        m[0, 2] = rig.lensShift.x * 2f; m[1, 2] = rig.lensShift.y * 2f;
        return m;
    }

    // 画面全体の絵の、縦の [c - h, c + h](NDC)の部分を、そのままの大きさで高さ h*2 の段へ
    static Matrix4x4 Band(float centerNdc, float halfNdc)
    {
        var b = Matrix4x4.identity;
        b[1, 1] = 1f / halfNdc;
        b[1, 3] = -centerNdc / halfNdc;
        return b;
    }

    void LateUpdate()
    {
        if (!Want())
        {
            if (active)
            {
                active = false;
                if (cam != null) { cam.rect = new Rect(0, 0, 1, 1); cam.ResetProjectionMatrix(); }
                if (upper != null) upper.enabled = false;
            }
            return;
        }
        active = true;
        float f = Mathf.Clamp(Frac, 0.3f, 0.7f);
        var full = FullProjection();
        // 下の段: 画面全体の絵の下から f の部分(キャラは画面の下寄り)
        cam.rect = new Rect(0f, 0f, 1f, f);
        cam.projectionMatrix = Band(-1f + f, f) * full;

        if (upper == null)
        {
            var go = new GameObject("FoldObliqueUpperCamera");
            DontDestroyOnLoad(go);
            upper = go.AddComponent<Camera>();
            upper.CopyFrom(cam);
            upper.depth = cam.depth + 0.5f;
            go.AddComponent<MirrorCull>();
        }
        upper.enabled = true;
        upper.fieldOfView = cam.fieldOfView;
        upper.rect = new Rect(0f, f, 1f, 1f - f);
        upper.transform.SetPositionAndRotation(transform.position + new Vector3(Ahead, UpperLift, 0f), transform.rotation);
        float h = 1f - f;
        upper.projectionMatrix = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f)) * Band(-1f + h, h) * full; // 同じ構図の下の部分、左右反転
    }

    class MirrorCull : MonoBehaviour
    {
        void OnPreRender() { GL.invertCulling = true; }
        void OnPostRender() { GL.invertCulling = false; }
    }

    void OnGUI()
    {
        if (!active || Event.current.type != EventType.Repaint) return;
        GUI.depth = 900;
        float yLine = Screen.height * (1f - Mathf.Clamp(Frac, 0.3f, 0.7f));
        float th = Mathf.Max(3f, Screen.height * 0.004f);
        UiKit.Fill(new Rect(0f, yLine - th * 1.5f, Screen.width, th * 3f), new Color(0.02f, 0.03f, 0.07f, 1f));
        UiKit.Fill(new Rect(0f, yLine - th * 0.5f, Screen.width, Mathf.Max(1f, th * 0.6f)), new Color(0.95f, 0.78f, 0.36f, 0.95f));
        float w = Mathf.Max(4f, Screen.width * 0.012f), hh = Screen.height * 0.12f;
        UiKit.Fill(new Rect(Screen.width - w, yLine - hh * 0.5f, w, hh), new Color(0.95f, 0.78f, 0.36f, 0.7f));
    }
}
