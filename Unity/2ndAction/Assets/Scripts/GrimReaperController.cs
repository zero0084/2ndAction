using System.Collections;
using UnityEngine;

// 100,000mの死神(2026-09-22 改修)。従来は「出現して浮いているだけの置物」だったが、
// 後方から高速で浮遊して追跡し、プレイヤーが走り続けている間も徐々に距離を詰める。
// ローブは格子リグ(BossRig, Cloth)で大きくなびく。距離が詰まりきると(捕捉)、一定間隔で
// 予備動作(赤い予告ゾーン)つきの大鎌を振る。ダメージは大鎌のHitboxだけ(本体は無害)。
// HPは持たず倒せない(従来コンセプト: どこまで逃げられるか)。
public class GrimReaperController : MonoBehaviour
{
    [Header("追跡 (調整用)")]
    public float startGap = 16f;         // 出現時にプレイヤーの後方どれだけ離れているか
    public float minGap = 2.8f;          // 詰めきったときの最小間隔(=捕捉)
    public float closeSpeed = 0.38f;     // 1秒あたりの間隔の縮み(基本走行速度に対する相対)
    public float hoverHeight = 1.15f;
    public float scytheInterval = 4.6f;  // 捕捉後、大鎌を振る間隔
    public float scytheWindup = 1.0f;
    public float scytheActive = 0.28f;

    Transform player;
    Transform visual;
    BossRig rig;
    float gap;
    float fwdSign = 1f;
    float artSign = 1f;
    float scale = 1f;
    float speedLineTimer;
    float t;
    float nextScythe;
    bool scything;
    BossHitbox scythe;
    BossTelegraphMarker scytheMark;
    float lastGround;

    public static GameObject Create(Sprite sprite, Vector3 position, float scale, bool defaultFacingRight = true, Transform player = null)
    {
        GameObject go = new GameObject("GrimReaper");
        go.transform.position = position;

        var c = go.AddComponent<GrimReaperController>();
        c.player = player != null ? player : (PlayerController.Instance != null ? PlayerController.Instance.transform : null);
        c.scale = scale;
        c.fwdSign = defaultFacingRight ? 1f : -1f;
        c.artSign = c.fwdSign; // 画面上は常にプレイヤー側(+x)を向く

        GameObject v = new GameObject("Visual");
        v.transform.SetParent(go.transform, false);
        v.transform.localScale = new Vector3(scale * c.artSign, scale, 1f);
        c.visual = v.transform;
        c.rig = new BossRig(v.transform, 8, 8, RenderOrder.Boss);
        c.rig.SetSprite(sprite);

        c.scythe = BossHitbox.Create(go.transform, BossFx.Slash(), new Color(0.75f, 0.5f, 1f, 0.95f), "Scythe", RenderOrder.Boss + 1);
        Vector2 sc = new Vector2(1.9f, 1.2f), ss = new Vector2(3.2f, 2.8f);
        c.scythe.Configure(sc, ss);
        c.scytheMark = BossTelegraphMarker.Create(go.transform, RenderOrder.Boss - 1);
        c.scytheMark.Configure(sc, ss);

        c.gap = startGapDefault; // 画面外(後方)から追いかけて来る
        // マルチプレイPhase 2 - HOSTでは共有ボスとして登録(JOINのパペットはNetCombat側で止める)。
        NetCombat.OnBossInit(c);
        return go;
    }

    const float startGapDefault = 16f;

    void Start()
    {
        nextScythe = Time.time + 3f;
    }

    void Update()
    {
        if (player == null || rig == null) return;
        if (GameManager.Instance != null && (!GameManager.Instance.HasStarted || GameManager.Instance.IsGameOver)) return;
        float dt = Time.deltaTime;
        t += dt;

        gap = Mathf.Max(minGap, gap - closeSpeed * dt);
        float sway = Mathf.Sin(t * 0.8f) * 0.5f * Mathf.Clamp01((gap - minGap) / 4f + 0.25f);
        float x = player.position.x - gap + sway;
        float g = lastGround;
        if (TerrainManager.Instance != null)
        {
            float? h = TerrainManager.Instance.GetHeightAt(x);
            if (h.HasValue) lastGround = g = h.Value;
        }
        float bob = Mathf.Sin(t * 2.2f) * 0.28f;
        transform.position = new Vector3(x, g + hoverHeight + bob, 0f);

        // ローブのなびき(常時全開)+傾き(前傾して追う)
        rig.Update(LocoStyle.Cloth, scything ? 0.6f : 1f, dt, fwdSign);
        visual.localRotation = Quaternion.Euler(0f, 0f, -8f);

        // 高速で浮遊している速度線
        speedLineTimer -= dt;
        if (speedLineTimer <= 0f)
        {
            speedLineTimer = 0.09f;
            Vector3 p = new Vector3(x - Random.Range(0.6f, 2.4f), g + Random.Range(0.4f, 3.4f), 0f);
            SpeedLine.Spawn(p, Random.Range(1.6f, 3.2f), new Color(0.75f, 0.65f, 1f, 0.26f));
        }

        // 捕捉後の大鎌
        if (!scything && gap <= minGap + 0.4f && Time.time >= nextScythe)
        {
            StartCoroutine(ScytheSwing());
        }
    }

    IEnumerator ScytheSwing()
    {
        scything = true;
        scytheMark.Show(1f);
        float w = 0f;
        while (w < scytheWindup)
        {
            w += Time.deltaTime;
            scytheMark.SetProgress(w / scytheWindup);
            rig.SetColor(Color.Lerp(Color.white, new Color(1f, 0.6f, 0.9f, 1f), 0.5f + 0.5f * Mathf.Sin(w * 25f)));
            yield return null;
        }
        rig.SetColor(Color.white);
        scytheMark.Hide();
        yield return scythe.Strike(1f, scytheActive);
        scything = false;
        nextScythe = Time.time + scytheInterval;
    }
}
