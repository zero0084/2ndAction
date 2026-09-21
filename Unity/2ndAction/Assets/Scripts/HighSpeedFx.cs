using UnityEngine;
using UnityEngine.SceneManagement;

// 高速走行の視認性補正(2026-09-22) - 背景を高速で流す代わりに、軽いVFXで疾走感を補う。
// Playerの速度倍率が上がるほど、(a)足元〜胴の高さの細いスピードライン、(b)画面の上下端の淡い流線 を
// 少しずつ増やす。どちらも細く薄く、プレイヤー/敵/障害物より奥(EnvironmentFx)に描くので、
// 読みやすさを損なわない。Motion Blurのような画面全体のぼかしは一切使わない。
// 見た目だけ(コライダー無し)。速度の計算には触れない。
[DefaultExecutionOrder(200)]
public class HighSpeedFx : MonoBehaviour
{
    [Header("開始/強さ")]
    public float startRatio = 1.12f;          // この速度倍率から出始める
    public float minInterval = 0.10f;         // 最高速時の生成間隔(秒)
    public float maxInterval = 0.42f;         // 出始めの生成間隔(秒)
    [Header("見た目")]
    public float lineAlpha = 0.20f;           // 足元スピードラインの濃さ(最高速時)
    public float edgeAlpha = 0.11f;           // 画面端の流線の濃さ(最高速時)
    public Color lineColor = new Color(1f, 0.97f, 0.9f);

    Camera cam;
    float timer;

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
        if (FindFirstObjectByType<HighSpeedFx>() != null) return;
        new GameObject("HighSpeedFx").AddComponent<HighSpeedFx>();
    }

    void Update()
    {
        var pc = PlayerController.Instance;
        var gm = GameManager.Instance;
        if (pc == null || gm == null || !gm.HasStarted || gm.IsGameOver) return;
        if (Time.timeScale <= 0f) return;
        if (cam == null) { var cf = FindFirstObjectByType<CameraFollow>(); if (cf != null) cam = cf.GetComponent<Camera>(); if (cam == null) return; }

        float ratio = pc.SpeedRatio;
        if (ratio < startRatio) return;
        float hi = Mathf.Clamp01((ratio - startRatio) / Mathf.Max(0.05f, pc.MaxSpeedRatio - startRatio));

        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = Mathf.Lerp(maxInterval, minInterval, hi) * Random.Range(0.8f, 1.2f);

        Vector3 c = cam.transform.position;
        float halfH = cam.orthographicSize;
        float halfW = halfH * cam.aspect;
        Vector3 pp = pc.transform.position;

        // (a) 足元〜胴の高さの細い線(Playerの少し前後にも出て、風を切る感じ)
        float x = c.x + Random.Range(-halfW * 0.9f, halfW * 1.0f);
        float y = pp.y + Random.Range(-0.15f, 1.5f);
        Color col = lineColor; col.a = lineAlpha * Mathf.Lerp(0.45f, 1f, hi);
        SpeedLine.Spawn(new Vector3(x, y, 0f), Random.Range(1.4f, 3.2f), col, 0.28f, RenderOrder.EnvironmentFx, 0.035f);

        // (b) 画面の上下端の淡い流線(視野の端で速さを感じさせる。中央には出さない)
        if (Random.value < 0.6f)
        {
            float sign = Random.value < 0.5f ? 1f : -1f;
            float ey = c.y + sign * halfH * Random.Range(0.72f, 0.95f);
            float ex = c.x + Random.Range(-halfW, halfW);
            Color ecol = lineColor; ecol.a = edgeAlpha * Mathf.Lerp(0.45f, 1f, hi);
            SpeedLine.Spawn(new Vector3(ex, ey, 0f), Random.Range(3f, 6.5f), ecol, 0.34f, RenderOrder.EnvironmentFx, 0.03f);
        }
    }
}
