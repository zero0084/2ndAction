using UnityEngine;

// 自然洞窟(2026-09-21) - 薄暗さ+プレイヤー周辺の明かり+たいまつ。
// カメラを覆う1枚のクアッド(OneMoreMile/CaveDarkness)だけで描くので、
// ライト数が増えても描画コストはほぼ一定。UI(IMGUI/uGUI Overlay)は
// このワールド空間のクアッドより後ろに描かれるため暗くならない。
//
// プレイヤーの明かりは前方へ伸ばした楕円で、速度が上がるほど前方の照射範囲を
// 広げる(速度上昇後も穴/針を見てから避けられるように)。
public class CaveLighting : MonoBehaviour
{
    public Material material;
    public Camera cam;

    [Header("暗さ (調整用)")]
    [Range(0f, 1f)] public float maxDarkness = 0.74f;
    public Color darkColor = new Color(0.02f, 0.03f, 0.07f, 1f);

    [Header("プレイヤーの明かり (調整用)")]
    public float lightBackReach = 6.5f;
    // 前方到達距離 = forwardReachBase + (現在の自動走行速度 - baseRunSpeed) * forwardReachPerSpeed
    public float forwardReachBase = 14f;
    public float forwardReachPerSpeed = 1.8f;
    public float forwardReachMax = 26f;
    public float lightVerticalRadius = 8.5f;
    [Range(0f, 0.9f)] public float lightSoftness = 0.2f;
    public float baseRunSpeed = 5f;

    [Header("たいまつ (調整用)")]
    public float torchRadiusScale = 1f;
    [Range(0f, 1f)] public float torchWarmAdd = 0.32f;
    [Range(0f, 0.5f)] public float flickerAmount = 0.12f;
    public CaveStage stage;

    MeshRenderer quadRenderer;
    Transform quad;
    readonly Vector4[] torchBuf = new Vector4[8];
    static readonly int PlayerId = Shader.PropertyToID("_Player");
    static readonly int TorchCountId = Shader.PropertyToID("_TorchCount");
    static readonly int TorchId = Shader.PropertyToID("_Torch");
    float smoothedForward;

    void Awake()
    {
        var go = new GameObject("CaveDarknessQuad");
        go.transform.SetParent(transform, false);
        var mf = go.AddComponent<MeshFilter>();
        var mesh = new Mesh { name = "CaveQuad" };
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(1000f, 1000f, 1f));
        mf.sharedMesh = mesh;
        quadRenderer = go.AddComponent<MeshRenderer>();
        quadRenderer.sharedMaterial = material;
        quadRenderer.sortingOrder = 100; // 世界内の全スプライトより手前(UIはOnGUI/Overlayなので別)
        quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        quadRenderer.receiveShadows = false;
        quad = go.transform;
        go.SetActive(false);
    }

    public void SetActive(bool on)
    {
        if (quad != null) quad.gameObject.SetActive(on);
        smoothedForward = 0f;
    }

    void LateUpdate()
    {
        if (quad == null || !quad.gameObject.activeSelf || cam == null || material == null) return;

        // クアッドはカメラを覆う大きさで追従(余白を持たせる)。
        float h = cam.orthographicSize * 2f * 1.08f;
        float w = h * cam.aspect;
        Vector3 cp = cam.transform.position;
        quad.position = new Vector3(cp.x, cp.y, 0f);
        quad.localScale = new Vector3(w, h, 1f);

        PlayerController pc = PlayerController.Instance;
        Vector3 pp = pc != null ? pc.transform.position : new Vector3(cp.x, cp.y, 0f);
        float speed = pc != null ? pc.CurrentAutoRunSpeed : baseRunSpeed;
        float targetForward = Mathf.Min(forwardReachMax, forwardReachBase + Mathf.Max(0f, speed - baseRunSpeed) * forwardReachPerSpeed);
        smoothedForward = smoothedForward <= 0f ? targetForward : Mathf.Lerp(smoothedForward, targetForward, Time.unscaledDeltaTime * 3f);

        // 楕円の中心を前後の到達距離の中間へずらす(後ろ=lightBackReach、前=smoothedForward)。
        float rx = (lightBackReach + smoothedForward) * 0.5f;
        float cx = pp.x + (smoothedForward - lightBackReach) * 0.5f;
        material.SetVector(PlayerId, new Vector4(cx, pp.y + 1.5f, rx, lightVerticalRadius));
        material.SetFloat("_PlayerSoft", lightSoftness);
        material.SetFloat("_Dark", maxDarkness);
        material.SetColor("_DarkColor", darkColor);
        material.SetFloat("_TorchWarm", torchWarmAdd);

        // 画面内(+半径)のたいまつだけを最大8本ぶん送る。
        int n = 0;
        if (stage != null)
        {
            float viewHalf = cam.orthographicSize * cam.aspect;
            var list = stage.Torches;
            for (int i = 0; i < list.Count && n < torchBuf.Length; i++)
            {
                CaveStage.Torch t = list[i];
                float r = stage.torchLightRadius * torchRadiusScale;
                if (Mathf.Abs(t.lightPos.x - cp.x) > viewHalf + r) continue;
                float flick = 1f - flickerAmount * Mathf.PerlinNoise(t.phase, Time.time * 6f);
                torchBuf[n++] = new Vector4(t.lightPos.x, t.lightPos.y, r, flick);
            }
        }
        material.SetFloat(TorchCountId, n);
        material.SetVectorArray(TorchId, torchBuf);
    }
}
