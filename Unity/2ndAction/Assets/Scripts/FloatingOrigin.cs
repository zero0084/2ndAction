using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// 高速走行/長距離対応(2026-09-22) - Floating Origin。
// 走行距離(=論理距離)をそのままUnityのWorld Xにすると、50,000mで座標の最小刻みが約0.004、
// 100,000mで約0.008ユニットになる(float精度)。1フレームの移動量は0.05〜0.2ユニットしかない
// ため、移動量の量子化(速度の細かい不揃い)とスプライトの位置ゆらぎ(画面のプルプル)が出る。
// そこで論理距離とシーン内の座標を分離する: プレイヤーが一定距離(shiftThreshold)を超えたら、
// シーンの全オブジェクトをまとめて-shiftだけ戻し、Xを保持している各システムへShiftedで通知する。
// 論理距離 = Transformの位置 + Offset(=これまでに戻した合計)。
// 既存のDistance/Boss Gate/MILEは「player.x - startX」または GameManager.MaxDistance 基準のままで、
// startX側も同じだけ戻すので値は変わらない(論理距離は連続)。
[DefaultExecutionOrder(1000)]
public class FloatingOrigin : MonoBehaviour
{
    public static FloatingOrigin Instance { get; private set; }

    // これまでにシーンを戻した合計(論理X = Transform.x + Offset)。デバッグワープでも加算される。
    public static double Offset { get; private set; }

    // 実際にシーンを戻した(全Transformを-shift)。購読側はX座標のキャッシュを-shiftする。
    public static event Action<float> Shifted;
    // 論理距離だけを+dワープした(Transformは動かない。デバッグ用)。購読側はstartX等を-dする。
    public static event Action<float> Warped;

    public float shiftThreshold = 3000f;  // プレイヤーXがこれを超えたら戻す
    public float keepPlayerAt = 1000f;    // 戻した後のプレイヤーXの下限の目安
    public float shiftStep = 1024f;       // 戻す単位(2の冪 = float上で誤差なく戻せる)
    public float forceShiftThresholdInBoss = 12000f; // ボス戦中はこれを超えるまで先送り

    public int ShiftCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Ensure();
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        Offset = 0.0;
        Ensure();
    }

    static void Ensure()
    {
        if (Instance != null) return;
        var go = new GameObject("FloatingOrigin");
        go.AddComponent<FloatingOrigin>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // 論理X(World Xに戻し済み量を足した値)。
    public static float ToLogical(float worldX) => (float)(worldX + Offset);

    void LateUpdate()
    {
        var pc = PlayerController.Instance;
        var gm = GameManager.Instance;
        if (pc == null || gm == null || !gm.HasStarted || gm.IsGameOver) return;
        if (pc.IsAscending) return;
        float px = pc.transform.position.x;
        if (px < shiftThreshold) return;
        bool boss = BossManager.Instance != null && BossManager.Instance.HoldsRun;
        if (boss && px < forceShiftThresholdInBoss) return;

        float step = Mathf.Max(64f, shiftStep);
        float shift = Mathf.Floor(Mathf.Max(0f, px - keepPlayerAt) / step) * step;
        if (shift <= 0f) return;
        Shift(shift);
    }

    public void Shift(float s)
    {
        Scene scene = SceneManager.GetActiveScene();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root == gameObject) continue;
            ShiftTransform(root.transform, s);
        }
        Offset += s;
        ShiftCount++;
        ShiftParticles(s);
        foreach (var tr in FindObjectsByType<TrailRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) tr.Clear();
        Physics2D.SyncTransforms();
        Shifted?.Invoke(s);
    }

    // 論理距離だけを+dワープ(デバッグ用)。地形の再生成を伴わないので何万mでも一瞬で飛べる。
    public static void LogicalWarp(float d)
    {
        Offset += d;
        Warped?.Invoke(d);
    }

    static bool IsTransparentContainer(Transform t)
    {
        if (t.childCount == 0) return false;
        Vector3 p = t.position;
        if (p.x != 0f || p.y != 0f || p.z != 0f) return false;
        if (t.rotation != Quaternion.identity) return false;
        Vector3 ls = t.lossyScale;
        if (ls.x != 1f || ls.y != 1f || ls.z != 1f) return false;
        if (t.GetComponent<Renderer>() != null || t.GetComponent<Collider2D>() != null || t.GetComponent<Camera>() != null) return false;
        if (t.GetComponent<Rigidbody2D>() != null) return false;
        return true;
    }

    static void ShiftTransform(Transform t, float s)
    {
        if (t.GetComponent<FloatingOriginExempt>() != null) return;
        var canvas = t.GetComponent<Canvas>();
        if (canvas != null && canvas.renderMode != RenderMode.WorldSpace) return;
        if (t is RectTransform) return;
        if (IsTransparentContainer(t))
        {
            // 原点にある単なる入れ物(TerrainManagerなど): 中の子が「ワールド座標で配置した物」なので、
            // 入れ物自体は動かさず子を個別に戻す(子のlocalPositionは元のまま、入れ物の位置ずれが起きない)。
            for (int i = 0; i < t.childCount; i++) ShiftTransform(t.GetChild(i), s);
            return;
        }
        Vector3 p = t.position;
        p.x -= s;
        t.position = p;
    }

    static void ShiftParticles(float s)
    {
        ParticleSystem.Particle[] buf = null;
        foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (ps.main.simulationSpace != ParticleSystemSimulationSpace.World) continue;
            int max = ps.main.maxParticles;
            if (buf == null || buf.Length < max) buf = new ParticleSystem.Particle[max];
            int n = ps.GetParticles(buf);
            if (n <= 0) continue;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = buf[i].position; p.x -= s; buf[i].position = p;
            }
            ps.SetParticles(buf, n);
        }
    }
}

// これを付けたルートオブジェクトはFloating Originで動かさない(原点固定の物用)。
public class FloatingOriginExempt : MonoBehaviour { }
