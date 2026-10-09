using UnityEngine;

// 走っているPlayerを見失わないための補助(2026-10-01)。どのシーンでも自動で1つ置かれ、何もしなくても働く。
//  ・Playerの輪郭: 絵の外側に2重の細い縁(内側=暗い縁 / 外側=明るい縁)。背景が明るいほど暗い縁を、暗いほど明るい縁を強める。
//    常に光って見えないよう、太さは画面の高さ720pxあたり1.5px/3px相当、濃さも控えめ(Shaders/SpriteRim)。
//  ・背景の幕: Playerの周り(横長の楕円)だけ、背景の明暗差を少し落とす。背景の絵より手前、地面/敵/障害物/Playerより奥
//    (=危険物や地形の見え方は変えない)。明るい背景では少し暗く、暗い背景では少し持ち上げる(Shaders/BackgroundVeil)。
//  ・背景の明るさはステージ/時間帯(SceneryCycle.NightAmount)から決め、ゆっくり追従させる(昼夜の切り替わりで急に変えない)。
//  ・高速時(90km/h〜)は縁を少しだけ強める。
// 負荷: 追加の描画はPlayerの周りの四角1枚(17回のテクスチャ読み)と、画面の一部を覆う四角1枚だけ。全画面の後処理は使わない。
[DefaultExecutionOrder(200)]
public class ReadabilityDirector : MonoBehaviour
{
    public static ReadabilityDirector Instance { get; private set; }
    // 確認用(撮影の前後比較)。通常は常にtrue
    public static bool OutlineEnabled = true, VeilEnabled = true;

    // ---- 調整値 ----
    public static float Ring1Px = 1.5f, Ring2Px = 3.0f;            // 720pxの画面での太さ
    public static Color DarkRim = new Color(0.02f, 0.03f, 0.08f);
    public static Color LightRim = new Color(0.94f, 0.96f, 1f);
    public static Vector2 VeilRadii = new Vector2(6f, 3.2f);         // world
    public static Vector2 VeilOffset = new Vector2(1.2f, 1.0f);      // Playerの足元から(少し前・体の中心)

    public float Luma { get; private set; } = 0.6f;   // 背景の明るさ(0=暗い..1=明るい)、追従後
    public float TargetLuma { get; private set; } = 0.6f;
    public float DarkA { get; private set; }
    public float LightA { get; private set; }
    public float VeilA { get; private set; }
    Color veilColor; float veilBaseA;

    // 幕の色と濃さ(全体 / Playerの周り)。色は「明るい背景は少し暗く(紺)、暗い背景は少し持ち上げる(灰青)」、
    // 明暗の差が強いラスダンは中間の灰青で明暗差そのものを縮める。
    public static void StageVeil(string stageId, float luma, out Color color, out float baseA, out float centerA)
    {
        color = Color.Lerp(new Color(0.42f, 0.47f, 0.58f), new Color(0.1f, 0.12f, 0.19f), luma);
        switch (stageId)
        {
            case "wasteland_road": baseA = Mathf.Lerp(0.05f, 0.1f, luma); centerA = Mathf.Lerp(0.24f, 0.3f, luma); break;
            case "natural_cave": baseA = 0f; centerA = 0.22f; break;                 // 暗さは雰囲気なので全体は触らない
            case "sky_corridor": baseA = 0.1f; centerA = 0.32f; break;               // 白い雲/明るい空で明るい色のキャラが埋もれないように
            case "last_corridor": color = new Color(0.24f, 0.27f, 0.35f); baseA = 0.12f; centerA = 0.36f; break;
            default: baseA = 0.06f; centerA = 0.26f; break;
        }
    }

    Material rimMat, veilMat;
    MeshRenderer rimRenderer, veilRenderer;
    MeshFilter rimFilter;
    Mesh rimMesh, veilMesh;
    SpriteRenderer playerSr;
    PlayerController boundPlayer;
    MaterialPropertyBlock mpb;
    static readonly int MainTexId = Shader.PropertyToID("_MainTex"), UvRectId = Shader.PropertyToID("_UvRect"),
        Step1Id = Shader.PropertyToID("_Step1"), Step2Id = Shader.PropertyToID("_Step2"), DarkId = Shader.PropertyToID("_DarkColor"),
        LightId = Shader.PropertyToID("_LightColor"), AlphaId = Shader.PropertyToID("_Alpha"),
        ColorId = Shader.PropertyToID("_Color"), BaseAId = Shader.PropertyToID("_BaseA"), CenterId = Shader.PropertyToID("_Center"), RadiiId = Shader.PropertyToID("_Radii");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => Ensure();
        Ensure();
    }

    static void Ensure()
    {
        if (Instance != null) return;
        var go = new GameObject("ReadabilityDirector");
        go.AddComponent<ReadabilityDirector>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        mpb = new MaterialPropertyBlock();
        var rm = Resources.Load<Material>("Effects/SpriteRim");
        var vm = Resources.Load<Material>("Effects/BackgroundVeil");
        if (rm != null) rimMat = new Material(rm);
        if (vm != null) veilMat = new Material(vm);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ===================================================================== //
    // 背景の明るさ(ステージ/時間帯)
    // ===================================================================== //
    public static float StageLuma(string stageId, float night)
    {
        switch (stageId)
        {
            case "wasteland_road": return Mathf.Lerp(0.74f, 0.2f, night);    // 昼は明るい空と草原、夜は暗い
            case "natural_cave": return 0.12f;                                // 暗い岩肌(たいまつ以外ほぼ黒)
            case "sky_corridor": return Mathf.Lerp(0.86f, 0.4f, night);      // 白い雲と明るい空
            case "last_corridor": return 0.32f;                               // 黒地に白/金/シアンの強い明暗
            default: return 0.55f;
        }
    }

    void LateUpdate()
    {
        var gm = GameManager.Instance;
        var pc = PlayerController.Instance;
        var cam = Camera.main;
        if (pc != boundPlayer) Bind(pc);
        bool running = gm != null && gm.HasStarted && pc != null && cam != null;

        string stage = gm != null ? gm.ActiveRunStageId : "";
        TargetLuma = StageLuma(stage, SceneryCycle.NightAmount);
        // 昼夜の切り替え等はゆっくり(約1.5秒)、ステージが変わった直後(Run開始)はすぐ合わせる
        if (!running) Luma = TargetLuma;
        else Luma = Mathf.Lerp(Luma, TargetLuma, 1f - Mathf.Exp(-Time.unscaledDeltaTime / 1.5f));

        float kmh = pc != null ? GameManager.SpeedKmh(pc.CurrentAutoRunSpeed) : 0f;
        float fast = Mathf.Clamp01((kmh - 90f) / 60f) * 0.12f;
        DarkA = Mathf.Clamp01(Mathf.Lerp(0.32f, 0.68f, Luma) + fast);
        LightA = Mathf.Clamp01(Mathf.Lerp(0.5f, 0.12f, Luma) + fast);
        StageVeil(stage, Luma, out veilColor, out veilBaseA, out float centerA);
        VeilA = centerA;

        UpdateRim(running, cam);
        UpdateVeil(running, cam, pc, stage);
    }

    void Bind(PlayerController pc)
    {
        boundPlayer = pc;
        playerSr = null;
        if (rimRenderer != null) Destroy(rimRenderer.gameObject);
        rimRenderer = null;
        if (pc == null || rimMat == null) return;
        var anim = pc.GetComponent<PlayerAnimator>();
        playerSr = anim != null && anim.VisualRenderer != null ? anim.VisualRenderer : pc.GetComponentInChildren<SpriteRenderer>();
        if (playerSr == null) return;
        var go = new GameObject("ReadabilityRim");
        go.transform.SetParent(playerSr.transform, false);
        rimFilter = go.AddComponent<MeshFilter>();
        rimMesh = new Mesh { name = "RimQuad" };
        rimMesh.MarkDynamic();
        rimFilter.sharedMesh = rimMesh;
        rimRenderer = go.AddComponent<MeshRenderer>();
        rimRenderer.sharedMaterial = rimMat;
        rimRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rimRenderer.receiveShadows = false;
    }

    readonly Vector3[] verts = new Vector3[4];
    readonly Vector2[] uvs = new Vector2[4];
    static readonly int[] Tris = { 0, 2, 1, 2, 3, 1 };
    Sprite lastSprite;
    float lastPadX = -1f, lastPadY = -1f;

    void UpdateRim(bool running, Camera cam)
    {
        if (rimRenderer == null || playerSr == null) return;
        Sprite s = playerSr.sprite;
        bool show = OutlineEnabled && running && playerSr.enabled && playerSr.gameObject.activeInHierarchy && s != null && playerSr.color.a > 0.05f;
        rimRenderer.enabled = show;
        if (!show) return;

        // 太さ: 画面の高さ720pxあたりのpx → world → 絵のローカル単位
        float worldPerPx720 = cam.orthographicSize * 2f / 720f;
        Vector3 ls = playerSr.transform.lossyScale;
        float sx = Mathf.Max(0.0001f, Mathf.Abs(ls.x)), sy = Mathf.Max(0.0001f, Mathf.Abs(ls.y));
        float r2w = Ring2Px * worldPerPx720;
        float padX = r2w / sx * 1.3f, padY = r2w / sy * 1.3f;

        // 絵の頂点(ぴったり型の絵でも頂点とUVは1対1)の範囲とUVの範囲を対応させる。Sprite.boundsは絵の四角全体のことがあり、
        // UV(頂点の範囲だけ)とずれて縁が大きく/ずれて出るので使わない。
        Vector2 vMin = new Vector2(float.MaxValue, float.MaxValue), vMax = new Vector2(float.MinValue, float.MinValue);
        Vector2 uvMin = vMin, uvMax = vMax;
        var sv = s.vertices; var su = s.uv;
        for (int i = 0; i < sv.Length; i++) { vMin = Vector2.Min(vMin, sv[i]); vMax = Vector2.Max(vMax, sv[i]); uvMin = Vector2.Min(uvMin, su[i]); uvMax = Vector2.Max(uvMax, su[i]); }
        Bounds b = new Bounds((vMin + vMax) * 0.5f, vMax - vMin);
        Vector2 uvPerLocal = new Vector2((uvMax.x - uvMin.x) / Mathf.Max(0.0001f, b.size.x), (uvMax.y - uvMin.y) / Mathf.Max(0.0001f, b.size.y));
        if (s != lastSprite || Mathf.Abs(padX - lastPadX) > 1e-5f || Mathf.Abs(padY - lastPadY) > 1e-5f)
        {
            lastSprite = s; lastPadX = padX; lastPadY = padY;
            float x0 = b.min.x - padX, x1 = b.max.x + padX, y0 = b.min.y - padY, y1 = b.max.y + padY;
            verts[0] = new Vector3(x0, y0, 0f); verts[1] = new Vector3(x1, y0, 0f); verts[2] = new Vector3(x0, y1, 0f); verts[3] = new Vector3(x1, y1, 0f);
            for (int i = 0; i < 4; i++)
                uvs[i] = new Vector2(uvMin.x + (verts[i].x - b.min.x) * uvPerLocal.x, uvMin.y + (verts[i].y - b.min.y) * uvPerLocal.y);
            rimMesh.Clear();
            rimMesh.vertices = verts;
            rimMesh.uv = uvs;
            rimMesh.triangles = Tris;
            rimMesh.RecalculateBounds();
        }
        var t = rimRenderer.transform;
        t.localScale = new Vector3(playerSr.flipX ? -1f : 1f, playerSr.flipY ? -1f : 1f, 1f);
        rimRenderer.sortingLayerID = playerSr.sortingLayerID;
        rimRenderer.sortingOrder = playerSr.sortingOrder - 1;

        float r1w = Ring1Px * worldPerPx720;
        mpb.Clear();
        mpb.SetTexture(MainTexId, s.texture);
        mpb.SetVector(UvRectId, new Vector4(uvMin.x, uvMin.y, uvMax.x, uvMax.y));
        mpb.SetVector(Step1Id, new Vector4(r1w / sx * uvPerLocal.x, r1w / sy * uvPerLocal.y, 0f, 0f));
        mpb.SetVector(Step2Id, new Vector4(r2w / sx * uvPerLocal.x, r2w / sy * uvPerLocal.y, 0f, 0f));
        mpb.SetColor(DarkId, new Color(DarkRim.r, DarkRim.g, DarkRim.b, DarkA));
        mpb.SetColor(LightId, new Color(LightRim.r, LightRim.g, LightRim.b, LightA));
        mpb.SetFloat(AlphaId, playerSr.color.a);
        rimRenderer.SetPropertyBlock(mpb);
    }

    void UpdateVeil(bool running, Camera cam, PlayerController pc, string stage)
    {
        if (veilMat == null) return;
        if (veilRenderer == null)
        {
            var go = new GameObject("BackgroundVeil");
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            veilMesh = new Mesh { name = "VeilQuad" };
            veilMesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            veilMesh.triangles = Tris;
            veilMesh.bounds = new Bounds(Vector3.zero, new Vector3(1000f, 1000f, 1f));
            mf.sharedMesh = veilMesh;
            veilRenderer = go.AddComponent<MeshRenderer>();
            veilRenderer.sharedMaterial = veilMat;
            veilRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            veilRenderer.receiveShadows = false;
        }
        bool show = VeilEnabled && running && pc != null && !GameManager.Instance.IsGameOver;
        veilRenderer.enabled = show;
        if (!show) return;
        // 背景の絵(-100..-97)より手前、地面の断面/地形/障害物(-3〜)より奥。
        // ラスダンは中景の柱/飾り(-6)も背景として扱う。天空回廊の奥のボス(-40/-30)は幕より手前に残す。
        veilRenderer.sortingOrder = stage == "last_corridor" ? -5 : -50;
        float h = cam.orthographicSize * 2f * 1.1f;
        Vector3 cp = cam.transform.position;
        veilRenderer.transform.position = new Vector3(cp.x + FoldView.CoverShiftX, cp.y, 0f);
        veilRenderer.transform.localScale = new Vector3(h * cam.aspect * FoldView.CoverScaleX, h, 1f);
        Color c = veilColor;
        c.a = VeilA;
        veilMat.SetFloat(BaseAId, veilBaseA);
        Vector3 pp = pc.transform.position;
        veilMat.SetColor(ColorId, c);
        veilMat.SetVector(CenterId, new Vector4(pp.x + VeilOffset.x, pp.y + VeilOffset.y, 0f, 0f));
        veilMat.SetVector(RadiiId, new Vector4(VeilRadii.x, VeilRadii.y, 0f, 0f));
    }
}
