using UnityEngine;
using UnityEngine.SceneManagement;

// 高速移動の演出「ソニックムーブ」(2026-10-02)。実際の速さ(km/h、SPEED UP等のカード込み)が上がるほど強くなる。
//  ・キャラ: 後ろに残像(Playerの今の絵を写した薄い青白い影が、走った跡に点々と残って消える)
//            前に衝撃波(体の前に弧を描く光の筋。加算合成)
//  ・背景:   背景の絵と地面の間に、長い流線(画面を右から左へ流れる)
// どれも見た目だけ(当たり判定なし)。Player/敵/障害物より奥か、Playerの体に重ならない位置に描く。
// 設定「発光演出」(GameSettings.GlowIntensity)で全体の濃さが変わる(0で出ない)。
[DefaultExecutionOrder(210)]
public class SonicMoveFx : MonoBehaviour
{
    public static SonicMoveFx Instance { get; private set; }
    public static float ForcedIntensity; // FINAL EVOLUTION(SPEED UP): 残像を強く(0=通常)
    [Header("速さ(km/h)")]
    public float startKmh = 60f;     // ここから出始める
    public float fullKmh = 130f;     // ここで最大
    // 2026-11-26 見た目の調整(依頼⑦): 大きすぎ・濃すぎ → 残像はキャラ本体と同じくらい〜少し小さく(0.9倍)、衝撃波の高さは
    //  体の当たり判定(武器や攻撃の絵を含まない)の 0.9 倍。透明度を上げ、高速でも重なって濃くなりすぎないよう間隔を広げた。
    //  速さ・効果時間・当たり判定は変えていない(この演出は見た目だけ)
    [Header("残像")]
    public float ghostIntervalSlow = 0.08f, ghostIntervalFast = 0.05f;
    public float ghostLife = 0.28f;
    public float ghostAlpha = 0.45f;
    public float ghostScale = 0.9f;
    public Color ghostColor = new Color(0.55f, 0.85f, 1f);
    [Header("衝撃波")]
    public float waveAlpha = 0.4f;
    public float waveAhead = 1.15f;
    public float waveBodyScale = 0.9f;
    [Header("背景の流線")]
    public float streakIntervalSlow = 0.07f, streakIntervalFast = 0.012f;
    public float streakAlpha = 0.38f;

    public float Intensity { get; private set; }   // 0..1(自動テスト用にも)
    public float Kmh { get; private set; }
    bool boomed;

    const int GhostPool = 14;
    readonly SpriteRenderer[] ghosts = new SpriteRenderer[GhostPool];
    readonly float[] ghostAge = new float[GhostPool];
    int ghostNext;
    float ghostTimer, streakTimer;
    SpriteRenderer wave;
    Material glowMat;
    PlayerController boundPc;
    SpriteRenderer playerSr;
    Camera cam;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        Create();
    }
    static void OnLoaded(Scene s, LoadSceneMode m) { Create(); }
    static void Create()
    {
        if (FindFirstObjectByType<SonicMoveFx>() != null) return;
        new GameObject("SonicMoveFx").AddComponent<SonicMoveFx>();
    }

    void Awake()
    {
        Instance = this;
        glowMat = Resources.Load<Material>("Effects/SpriteGlow");
        for (int i = 0; i < GhostPool; i++)
        {
            var go = new GameObject("SonicGhost");
            go.transform.SetParent(transform, false);
            ghosts[i] = go.AddComponent<SpriteRenderer>();
            if (glowMat != null) ghosts[i].sharedMaterial = glowMat; // 絵の形を青白く光らせる(暗い色のキャラでも見える)
            ghosts[i].enabled = false;
            ghostAge[i] = 99f;
        }
        var wgo = new GameObject("SonicWave");
        wgo.transform.SetParent(transform, false);
        wave = wgo.AddComponent<SpriteRenderer>();
        wave.sprite = WaveSprite();
        if (glowMat != null) wave.sharedMaterial = glowMat;
        wave.enabled = false;
    }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void LateUpdate()
    {
        var pc = PlayerController.Instance;
        var gm = GameManager.Instance;
        float dt = Time.deltaTime;
        AgeGhosts(dt);
        bool run = pc != null && gm != null && gm.HasStarted && !gm.IsGameOver && Time.timeScale > 0f && !pc.IsFinishing;
        if (pc != boundPc) { boundPc = pc; playerSr = null; if (pc != null) { var an = pc.GetComponent<PlayerAnimator>(); playerSr = an != null ? an.VisualRenderer : pc.GetComponentInChildren<SpriteRenderer>(); } }
        Kmh = pc != null ? GameManager.SpeedKmh(pc.CurrentAutoRunSpeed) : 0f;
        float k = run ? Mathf.Max(ForcedIntensity, Mathf.Clamp01((Kmh - startKmh) / Mathf.Max(1f, fullKmh - startKmh))) : 0f;
        float glow = GameSettings.GlowIntensity;
        Intensity = Mathf.MoveTowards(Intensity, k, dt * 2.5f); // 急に出たり消えたりしない
        // 音の再設計(2026-10-06): 音速の域に入った瞬間に1回だけ衝撃音(下がってから入り直すまで鳴らさない)
        if (!boomed && Intensity >= 0.55f && ForcedIntensity <= 0f) { boomed = true; if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.SonicBoom); }
        else if (boomed && Intensity < 0.2f) boomed = false;
        if (Intensity <= 0.01f || glow <= 0.01f || playerSr == null)
        {
            if (wave != null) wave.enabled = false;
            return;
        }
        if (cam == null) cam = Camera.main;

        // ---- 残像
        ghostTimer -= dt;
        if (ghostTimer <= 0f && playerSr.enabled && playerSr.sprite != null && playerSr.color.a > 0.2f)
        {
            ghostTimer = Mathf.Lerp(ghostIntervalSlow, ghostIntervalFast, Intensity);
            var g = ghosts[ghostNext]; ghostNext = (ghostNext + 1) % GhostPool;
            var t = playerSr.transform;
            g.transform.SetPositionAndRotation(t.position, t.rotation);
            Vector3 ls = t.lossyScale * ghostScale; ls.x *= 1f + 0.05f * Intensity; // 本体より少し小さく、横にわずかに伸ばす(流れる感じ)
            ls.z = 1f;
            g.transform.localScale = ls;
            g.sprite = playerSr.sprite; g.flipX = playerSr.flipX; g.flipY = playerSr.flipY;
            g.sortingLayerID = playerSr.sortingLayerID;
            g.sortingOrder = playerSr.sortingOrder - 2;
            g.enabled = true;
            ghostAge[System.Array.IndexOf(ghosts, g)] = 0f;
        }
        for (int i = 0; i < GhostPool; i++)
        {
            if (!ghosts[i].enabled) continue;
            float f = ghostAge[i] / Mathf.Max(0.01f, ghostLife);
            Color c = ghostColor; c.a = ghostAlpha * Intensity * glow * (1f - f) * (1f - f);
            ghosts[i].color = c;
        }

        // ---- 衝撃波(体の前)
        // 大きさの基準は体(当たり判定の箱)。武器・攻撃の絵・効果を含む絵の範囲は使わない(大柄/小柄なキャラでも本体に合う)
        Bounds pb = BodyBounds(playerSr);
        float h = Mathf.Max(0.5f, pb.size.y) * waveBodyScale;
        wave.enabled = true;
        wave.transform.position = new Vector3(pb.max.x + waveAhead * (0.6f + 0.4f * Intensity) * h * 0.35f, pb.center.y, 0f);
        float pulse = 1f + 0.04f * Mathf.Sin(Time.time * 38f);
        wave.transform.localScale = new Vector3(h * 1.1f * pulse, h * (0.9f + 0.1f * Intensity), 1f);
        wave.sortingLayerID = playerSr.sortingLayerID;
        wave.sortingOrder = playerSr.sortingOrder - 1;
        Color wc = new Color(0.75f, 0.92f, 1f, waveAlpha * Intensity * glow);
        wave.color = wc;

        // ---- 背景の流線(背景の絵と地面の間。画面全体に長い線)
        streakTimer -= dt;
        if (streakTimer <= 0f && cam != null)
        {
            streakTimer = Mathf.Lerp(streakIntervalSlow, streakIntervalFast, Intensity) * Random.Range(0.7f, 1.3f);
            float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
            Vector3 c = cam.transform.position;
            // 半分はPlayerの高さの帯(速さが一番伝わる)、残りは画面全体
            float y = Random.value < 0.5f ? playerSr.bounds.center.y + Random.Range(-1.8f, 2.2f) : c.y + Random.Range(-halfH * 0.85f, halfH * 0.9f);
            float x = c.x + Random.Range(-halfW * 0.6f, halfW * 1.1f);
            Color sc = new Color(1f, 1f, 1f, streakAlpha * Mathf.Lerp(0.4f, 1f, Intensity) * glow);
            SpeedLine.Spawn(new Vector3(x, y, 0f), Random.Range(6f, 14f) * (0.7f + 0.6f * Intensity), sc, Random.Range(0.18f, 0.32f), RenderOrder.SkyCloud + 1, Random.Range(0.05f, 0.11f));
        }
    }

    // 体の範囲 = Player の BoxCollider2D(体の当たり判定)。Collider2D.bounds は物理の更新まで古い位置のことがあるので、
    //  箱の大きさ/ずれと今の Transform から求める。縦は絵の範囲より大きくしない。箱が無い時は絵の範囲
    BoxCollider2D bodyBox;
    Bounds BodyBounds(SpriteRenderer sr)
    {
        Bounds sb = sr.bounds;
        if (boundPc == null) return sb;
        if (bodyBox == null || bodyBox.gameObject != boundPc.gameObject) bodyBox = boundPc.GetComponent<BoxCollider2D>();
        if (bodyBox == null) return sb;
        Transform t = bodyBox.transform;
        Vector3 c = t.TransformPoint(bodyBox.offset);
        Vector3 s = Vector3.Scale(bodyBox.size, t.lossyScale); s.x = Mathf.Abs(s.x); s.y = Mathf.Min(Mathf.Abs(s.y), sb.size.y); s.z = 1f;
        return new Bounds(new Vector3(c.x, c.y, 0f), s);
    }

    void AgeGhosts(float dt)
    {
        for (int i = 0; i < GhostPool; i++)
        {
            if (!ghosts[i].enabled) continue;
            ghostAge[i] += dt;
            if (ghostAge[i] >= ghostLife) ghosts[i].enabled = false;
        }
    }

    // 体の前の弧(右向きの「)))」)。白い絵を色で塗る(加算)
    static Sprite waveSprite;
    static Sprite WaveSprite()
    {
        if (waveSprite != null) return waveSprite;
        const int W = 64, H = 128;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float v = (y + 0.5f) / H * 2f - 1f;           // -1..1
                float u = (x + 0.5f) / W;                     // 0..1(右が前)
                float a = 0f;
                // 3本の弧: 中心を左に置いた楕円の一部
                for (int k = 0; k < 3; k++)
                {
                    float r = 0.42f + k * 0.24f;
                    float cx = -0.15f - k * 0.05f;
                    float d = Mathf.Sqrt((u - cx) * (u - cx) / (r * r) + v * v * 0.95f);
                    float ring = Mathf.Exp(-Mathf.Pow((d - 1f) / (0.05f + 0.02f * k), 2f));
                    a = Mathf.Max(a, ring * (1f - k * 0.25f));
                }
                a *= Mathf.Clamp01(1f - Mathf.Abs(v) * 0.9f);   // 上下の端は薄く
                px[y * W + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        waveSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), H);
        return waveSprite;
    }
}
