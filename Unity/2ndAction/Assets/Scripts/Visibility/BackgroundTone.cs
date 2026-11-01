using UnityEngine;

// 背景を控えめにして、Player と敵を見やすくする(2026-11-01)。どのシーンでも自動で1つ置かれる。
//  ・対象は「背景の絵」だけ: 背景の1枚絵(TerrainManager.backgroundRenderer)、昼夜/景色の重ね絵(SceneryOverlay・天空の夜・ラスダンの重ね絵)、
//    空の雲(ForegroundCloudLayer)。地面/足場/穴/障害物/天井の針/敵/攻撃の予告/エフェクトは一切触らない(並び順 -40 より手前は対象外)。
//  ・やること: 彩度を少し落とす / 明暗差をステージの中間の色へ寄せて縮める / 細かい模様を少しなだらかに(Shaders/SpriteBgTone)。
//    一律に暗くはしない(中間の色はその場面の背景の平均に近い色 → 全体の明るさは大きく変えない)。
//  ・暗い場面(洞窟/夜/ラスダン)では、敵の縁取りを黒から淡い色に替える(SpriteOutline.EnemyLightRim)。明るい場面は今までどおり黒。
//  ・値はステージ/時間帯(夜の度合い)で決め、ゆっくり追従する。Enabled=false で元の見た目(撮影の前後比較用)。
// 負荷: 背景の絵の画素ごとに数回の読み込みと数行の計算だけ(全画面の後処理は使わない)。
[DefaultExecutionOrder(210)]
public class BackgroundTone : MonoBehaviour
{
    public static BackgroundTone Instance { get; private set; }
    public static bool Enabled = true;   // 確認用(前後比較)。通常は常に true

    public float Desat { get; private set; }
    public float Flatten { get; private set; }
    public float Soft { get; private set; }
    public Color Mid { get; private set; } = new Color(0.5f, 0.5f, 0.5f);
    public float Night { get; private set; }

    static readonly int DesatId = Shader.PropertyToID("_OmmBgDesat"), FlattenId = Shader.PropertyToID("_OmmBgFlatten"),
        SoftId = Shader.PropertyToID("_OmmBgSoft"), MidId = Shader.PropertyToID("_OmmBgMid"),
        BandId = Shader.PropertyToID("_OmmBgBand"), BandAmtId = Shader.PropertyToID("_OmmBgBandAmt");

    // 敵が通る高さの帯(Player の足元から上へ)。帯の中だけ、彩度/明暗差/細部をもう少し落とす
    public static float BandUp = 1.6f, BandHalf = 3.4f, BandSoft = 2.8f;
    public Vector3 BandAmt { get; private set; }
    public float BandCenter { get; private set; }

    // ステージ/夜の度合いごとの値。mid はその場面の背景の平均に近い色(撮影した画面から計測、Docs/BgTone_2026-11-01)。
    public static void StageTone(string stageId, float night, out float desat, out float flatten, out float soft, out Color mid, out Vector3 band)
    {
        switch (stageId)
        {
            case "wasteland_road":
                desat = Mathf.Lerp(0.26f, 0.20f, night); flatten = Mathf.Lerp(0.20f, 0.14f, night); soft = 1.2f;
                mid = Color.Lerp(new Color(0.53f, 0.57f, 0.58f), new Color(0.17f, 0.19f, 0.27f), night);
                band = Vector3.Lerp(new Vector3(0.20f, 0.18f, 1.3f), new Vector3(0.12f, 0.12f, 1.0f), night); break;
            case "natural_cave":
                desat = 0.22f; flatten = 0.12f; soft = 1.0f; mid = new Color(0.15f, 0.14f, 0.14f); band = new Vector3(0.10f, 0.08f, 0.8f); break;
            case "sky_corridor":
                desat = Mathf.Lerp(0.22f, 0.20f, night); flatten = Mathf.Lerp(0.18f, 0.16f, night); soft = 1.2f;
                mid = Color.Lerp(new Color(0.62f, 0.70f, 0.80f), new Color(0.18f, 0.22f, 0.36f), night);
                band = Vector3.Lerp(new Vector3(0.18f, 0.16f, 1.3f), new Vector3(0.12f, 0.12f, 1.0f), night); break;
            case "last_corridor":
                desat = 0.20f; flatten = 0.22f; soft = 1.0f; mid = new Color(0.20f, 0.21f, 0.26f); band = new Vector3(0.10f, 0.10f, 0.8f); break;
            default:
                desat = 0f; flatten = 0f; soft = 0f; mid = new Color(0.5f, 0.5f, 0.5f); band = Vector3.zero; break;
        }
    }

    // 敵の縁取りを淡い色にする暗さの境目(ReadabilityDirector.Luma)
    public const float LightRimLuma = 0.36f;

    static Material sharedMat; static bool matTried;
    public static Material SharedMaterial
    {
        get
        {
            if (!matTried)
            {
                matTried = true;
                var sh = Resources.Load<Shader>("Shaders/SpriteBgTone");
                if (sh != null && sh.isSupported) sharedMat = new Material(sh) { name = "Background Tone" };
                else Debug.LogWarning("[BgTone] shader not available - backgrounds keep the original look");
            }
            return sharedMat;
        }
    }

    // 背景の絵をこの描き方にする(元に戻すことはしない: 値が 0 の時は元の絵と同じ結果)
    public static void Adopt(SpriteRenderer sr)
    {
        if (sr == null) return;
        var m = SharedMaterial;
        if (m != null && sr.sharedMaterial != m) sr.sharedMaterial = m;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => Ensure();
        Ensure();
    }

    static void Ensure()
    {
        if (Instance != null) return;
        new GameObject("BackgroundTone").AddComponent<BackgroundTone>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SetGlobals(0f, 0f, 0f, Mid);
    }

    void OnDestroy() { if (Instance == this) { Instance = null; SetGlobals(0f, 0f, 0f, Color.gray); Shader.SetGlobalVector(BandAmtId, Vector4.zero); SpriteOutline.EnemyLightRim = false; } }

    static void SetGlobals(float desat, float flatten, float soft, Color mid)
    {
        Shader.SetGlobalFloat(DesatId, desat);
        Shader.SetGlobalFloat(FlattenId, flatten);
        Shader.SetGlobalFloat(SoftId, soft);
        Shader.SetGlobalColor(MidId, mid);
    }

    SpriteRenderer sceneryOverlay; float nextFind;
    bool wasRunning;

    void LateUpdate()
    {
        var gm = GameManager.Instance;
        bool running = gm != null && gm.HasStarted;
        string stage = gm != null ? gm.ActiveRunStageId : "";
        Night = SceneryCycle.Active ? SceneryCycle.NightAmount : (WorldTimeCycle.Instance != null ? WorldTimeCycle.Instance.NightAmount : 0f);

        float desat = 0f, flatten = 0f, soft = 0f; Color mid = Mid; Vector3 band = Vector3.zero;
        if (Enabled && running) StageTone(stage, Night, out desat, out flatten, out soft, out mid, out band);
        // 帯は横から見る画面だけ(縦画面の「斜め」は透視のカメラ、「上下2段」は画面の位置と高さが対応しないので、全体の値だけにする)
        var cam = Camera.main;
        if (cam == null || !cam.orthographic || FoldView.Active) band = Vector3.zero;
        var pc = PlayerController.Instance;
        // 昼夜の移り変わりはゆっくり、Run の開始/前後比較の切り替えはすぐ合わせる
        bool snap = running != wasRunning || !Enabled || Time.unscaledDeltaTime <= 0f || Time.timeScale <= 0f;
        wasRunning = running;
        float k = snap ? 1f : 1f - Mathf.Exp(-Time.unscaledDeltaTime / 1.5f);
        Desat = Mathf.Lerp(Desat, desat, k); Flatten = Mathf.Lerp(Flatten, flatten, k); Soft = Mathf.Lerp(Soft, soft, k); Mid = Color.Lerp(Mid, mid, k);
        BandAmt = Vector3.Lerp(BandAmt, band, k);
        // 帯の高さは Player の足元にゆっくり付いていく(ジャンプで背景が揺れて見えないよう、約0.8秒で追従)
        if (pc != null)
        {
            float target = pc.transform.position.y + BandUp;
            float kb = snap ? 1f : 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.8f);
            BandCenter = Mathf.Lerp(BandCenter, target, kb);
        }
        SetGlobals(Desat, Flatten, Soft, Mid);
        Shader.SetGlobalVector(BandId, new Vector4(BandCenter, BandHalf, BandSoft, 0f));
        Shader.SetGlobalVector(BandAmtId, new Vector4(BandAmt.x, BandAmt.y, BandAmt.z, 0f));

        var rd = ReadabilityDirector.Instance;
        SpriteOutline.EnemyLightRim = Enabled && running && rd != null && rd.Luma < LightRimLuma;

        if (!running) return;
        // 対象の背景の絵(作られ直すことがあるので毎フレーム確かめる。参照を比べるだけ)
        var tm = TerrainManager.Instance;
        if (tm != null) Adopt(tm.backgroundRenderer);
        var wt = WorldTimeCycle.Instance;
        if (wt != null) { Adopt(wt.dayLayer); Adopt(wt.nightLayer); }
        if (LastCorridorDirector.Instance != null) Adopt(LastCorridorDirector.Instance.OverlayBg);
        if (sceneryOverlay == null && Time.unscaledTime >= nextFind)
        {
            nextFind = Time.unscaledTime + 2f;
            var go = GameObject.Find("SceneryOverlay");
            if (go != null) sceneryOverlay = go.GetComponent<SpriteRenderer>();
        }
        Adopt(sceneryOverlay);
    }
}
