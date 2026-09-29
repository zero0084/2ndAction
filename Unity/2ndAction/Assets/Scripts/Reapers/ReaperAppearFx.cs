using System.Collections;
using UnityEngine;

// 死神が画面左端から姿を現す時の空気の変化(2026-09-29)。画面の左側が暗い紫にかげり、冷たい霧が流れて、
// 少しして姉妹が姿を見せる。1回のRunで1回だけ。見た目だけ(判定なし)。マルチでは各端末でそれぞれ出す。
public class ReaperAppearFx : MonoBehaviour
{
    public static int PlayCount;
    static float lastPlay = -99f;
    SpriteRenderer shade;
    int baseOrder;
    float t;
    const float Duration = 4.5f;

    public static void Play()
    {
        if (Time.time - lastPlay < 2f) return; // 同じ出現で二重に出さない
        lastPlay = Time.time;
        PlayCount++;
        var cam = Camera.main;
        if (cam == null) return;
        var go = new GameObject("[ReaperAppearFx]");
        go.transform.SetParent(cam.transform, false);
        var fx = go.AddComponent<ReaperAppearFx>();
        fx.shade = go.AddComponent<SpriteRenderer>();
        fx.shade.sprite = GradientSprite();
        bool cave = TerrainManager.Instance != null && TerrainManager.Instance.HasCaveDarkness;
        fx.baseOrder = cave ? 104 : RenderOrder.Boss + 19; // 洞窟は暗闇(描画順100)の手前に
        fx.shade.sortingOrder = fx.baseOrder + 1;
        fx.shade.color = new Color(0.08f, 0.02f, 0.12f, 0f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossWarning, 0.8f);
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null) { Destroy(gameObject); return; }
        t += Time.deltaTime;
        float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
        // 画面の左側35%を覆う(左ほど濃い)
        float w = halfW * 2f * 0.35f;
        transform.localPosition = new Vector3(-halfW + w * 0.5f, 0f, 10f);
        transform.localScale = new Vector3(w, halfH * 2f * 16f, 1f); // 絵は横1×縦1/16ワールド単位
        float a = t < 0.8f ? t / 0.8f : Mathf.Clamp01(1f - (t - 0.8f) / (Duration - 0.8f));
        shade.color = new Color(0.08f, 0.02f, 0.12f, 0.55f * a);
        // 冷たい霧(左端から右へゆっくり流れる)
        if (t < Duration - 1f && Random.value < Time.deltaTime * 14f)
        {
            Vector3 p = cam.transform.position + new Vector3(-halfW + Random.Range(0f, w * 0.7f), Random.Range(-halfH * 0.6f, halfH * 0.3f), 0f);
            p.z = 0f;
            OneShotSpriteEffect.CreateTweened(OneShotSpriteEffect.SoftDotSprite(), p, new Color(0.55f, 0.45f, 0.7f, 0.35f),
                duration: Random.Range(1.2f, 2.0f), startScale: Random.Range(1.2f, 2.2f), endScale: Random.Range(2.8f, 4.2f), sortingOrder: baseOrder, holdFraction: 0.2f);
        }
        if (t >= Duration) Destroy(gameObject);
    }

    static Sprite gradient;
    static Sprite GradientSprite()
    {
        if (gradient != null) return gradient;
        const int n = 64;
        var tex = new Texture2D(n, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int x = 0; x < n; x++)
        {
            float a = Mathf.Pow(1f - x / (float)(n - 1), 1.6f);
            for (int y = 0; y < 4; y++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        gradient = Sprite.Create(tex, new Rect(0, 0, n, 4), new Vector2(0.5f, 0.5f), n);
        return gradient;
    }
}
