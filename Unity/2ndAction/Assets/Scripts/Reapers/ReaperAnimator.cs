using UnityEngine;

// 死神三姉妹の見た目(2026-09-29)。移動のコマ送り・上下のゆれ・スキップの跳ね・出現のフェードを、
// 走行速度とは無関係な一定のテンポで動かす(ここが「必死に追っていない」異質さの本体)。
// 追跡AI(ReaperBase)とは別コンポーネント: マルチのJOINではAIが止まり位置だけ届くが、見た目はこれがその場で動かす。
// 階層: Root(位置) → Visual(大きさ/向き。マルチで同期される) → Body(絵。ゆれ/跳ねはここだけ) / Shadow(足元の影)
[DefaultExecutionOrder(1180)]
public class ReaperAnimator : MonoBehaviour
{
    ReaperSisterData data;
    ReaperMotion motion;
    Transform body;
    SpriteRenderer sr, shadow;
    Sprite[] frames;
    Sprite fallback;
    bool fallbackFlip;
    float t, alpha, appearT = -1f;
    bool windup;
    public bool Appeared => appearT >= 0f;
    public int FrameIndex { get; private set; }
    public float Alpha => alpha;

    public void Setup(ReaperSisterData d, Transform visual, Sprite fallbackSprite, ReaperMotion m)
    {
        data = d; motion = m; fallback = fallbackSprite;
        frames = d.moveFrames != null && d.moveFrames.Length > 0 ? d.moveFrames : null;
        Sprite reference = frames != null ? frames[0] : d.idle != null ? d.idle : fallbackSprite;
        fallbackFlip = frames == null && d.idle == null; // 既存の死神の絵は左向き

        body = visual.Find("Body");
        if (body == null) { body = new GameObject("Body").transform; body.SetParent(visual, false); }
        sr = body.GetComponent<SpriteRenderer>();
        if (sr == null) sr = body.gameObject.AddComponent<SpriteRenderer>();
        sr.sortingOrder = RenderOrder.Boss;
        sr.sprite = reference;
        if (fallbackFlip) sr.color = d.placeholderTint;

        float h = reference != null ? Mathf.Max(0.01f, reference.bounds.size.y) : 1f;
        float s = d.heightWorld / h;
        visual.localScale = new Vector3(fallbackFlip ? -s : s, s, 1f);
        // 既存の死神の絵は中心基準なので足元へ下ろす
        if (fallbackFlip && reference != null) body.localPosition = new Vector3(0f, -reference.bounds.min.y, 0f);

        var sh = transform.Find("Shadow");
        if (sh == null)
        {
            sh = new GameObject("Shadow").transform;
            sh.SetParent(transform, false);
            shadow = sh.gameObject.AddComponent<SpriteRenderer>();
            shadow.sprite = OneShotSpriteEffect.SoftDotSprite();
            shadow.sortingOrder = RenderOrder.Boss - 2;
        }
        else shadow = sh.GetComponent<SpriteRenderer>();
        sh.localScale = new Vector3(d.heightWorld * 0.55f, d.heightWorld * 0.1f, 1f);

        // 洞窟: 画面全体を覆う暗闇(CaveLighting、描画順100)の手前に描き、暗闇の中に姿が浮かび上がるようにする
        // (少し暗めの色+淡い紫の光をまとう)。洞窟以外は従来の描画順のまま。
        if (TerrainManager.Instance != null && TerrainManager.Instance.HasCave)
        {
            baseTint = new Color(0.82f, 0.8f, 0.92f, 1f);
            sr.sortingOrder = 103;
            shadow.sortingOrder = 101;
            var g = new GameObject("DarkGlow").transform;
            g.SetParent(visual, false);
            glow = g.gameObject.AddComponent<SpriteRenderer>();
            glow.sprite = OneShotSpriteEffect.SoftDotSprite();
            glow.sortingOrder = 102;
            float inv = 1f / Mathf.Max(0.001f, Mathf.Abs(visual.localScale.y));
            g.localScale = new Vector3(d.heightWorld * 1.1f * inv, d.heightWorld * 1.3f * inv, 1f);
            g.localPosition = new Vector3(0f, d.heightWorld * 0.5f * inv, 0f);
        }
        if (!fallbackFlip) sr.color = baseTint;
        SetAlpha(0f);
    }

    Color baseTint = Color.white;
    SpriteRenderer glow;

    public void BeginAppear()
    {
        if (appearT >= 0f) return;
        appearT = 0f;
        ReaperAppearFx.Play();
    }

    public void SetStrikeWindup(bool on) => windup = on;

    // 標的が後ろへ戻った等で死神の位置が置き直された: いったん消えて、もう一度ゆっくり姿を現す(瞬間移動を見せない)。
    public void Reappear()
    {
        if (appearT < 0f) return;
        appearT = 0f;
        SetAlpha(0f);
        Reappears++;
    }
    public int Reappears { get; private set; }
    double lastX = double.NaN;

    // 死神は後ろへは進まない: 1フレームで後ろへ大きく動いた=置き直し(マルチのJOINのパペットにはHOSTの位置が届く)→ 現れ直す。
    // 位置が確定した後(NetCombatの反映=実行順1150より後)に確かめ、置き直したフレームを見せない。
    void LateUpdate()
    {
        double x = transform.position.x + FloatingOrigin.Offset;
        if (!double.IsNaN(lastX) && x - lastX < -2.0) Reappear();
        lastX = x;
    }

    void SetAlpha(float a)
    {
        alpha = a;
        if (sr != null) { var c = sr.color; c.a = a; sr.color = c; }
        if (shadow != null) shadow.color = new Color(0.05f, 0f, 0.08f, 0.45f * a);
        if (glow != null) glow.color = new Color(0.55f, 0.35f, 0.8f, 0.22f * a);
    }

    void Update()
    {
        if (data == null || sr == null) return;
        float dt = Time.deltaTime; // ゲーム内時間(一時停止中は止まる)。走行速度は一切使わない。
        t += dt;
        // マルチのJOIN(AIが止まっているパペット)でも同じ間合いで姿を見せる
        if (appearT < 0f && t >= data.chase.appearDelay) BeginAppear();
        if (appearT >= 0f) { appearT += dt; SetAlpha(Mathf.Clamp01(appearT / Mathf.Max(0.01f, data.fadeInTime))); }

        float y = 0f, rot = 0f, sx = 1f, sy = 1f;
        switch (motion)
        {
            case ReaperMotion.Walk:
            {
                // 一定のゆっくりした歩幅。一歩ごとにわずかに上下、上体はほとんど揺れない。
                float step = t * data.moveFps * 0.5f; // 2コマで一歩
                y = Mathf.Abs(Mathf.Sin(step * Mathf.PI)) * data.walkBob;
                rot = Mathf.Sin(step * Mathf.PI) * data.walkSwayDeg;
                break;
            }
            case ReaperMotion.Float:
            {
                // 一定のゆっくりした上下(必死さの無い浮遊)
                y = Mathf.Sin(t * data.bobFrequency * Mathf.PI * 2f) * data.bobAmount;
                rot = Mathf.Sin(t * data.bobFrequency * Mathf.PI * 2f + 1.1f) * 1.5f;
                break;
            }
            case ReaperMotion.Skip:
            {
                // 軽く跳ねるスキップ。何回かに1回はふわっと長く浮く(天空回廊らしい軽さ)。着地の瞬間だけ少しつぶれる。
                float hops = t * data.skipRate;
                int n = Mathf.FloorToInt(hops);
                float ph = hops - n;
                bool floaty = data.skipFloatEvery > 0 && n % data.skipFloatEvery == data.skipFloatEvery - 1;
                float hgt = data.skipHeight * (floaty ? data.skipFloatHeightMul : 1f);
                float arc = floaty ? Mathf.Sin(ph * Mathf.PI) : 4f * ph * (1f - ph); // ふわっと=頂点で長く
                y = arc * hgt;
                float land = Mathf.Clamp01(1f - ph * 8f) + Mathf.Clamp01((ph - 0.9f) * 10f);
                sy = 1f - 0.06f * land; sx = 1f + 0.04f * land;
                rot = Mathf.Sin(ph * Mathf.PI * 2f) * 3f;
                break;
            }
        }
        if (windup) rot += -6f; // 大鎌を振りかぶる(わずかに反る)
        Vector3 basePos = body.localPosition; basePos.y = BaseY() + y / Mathf.Max(0.001f, Mathf.Abs(body.parent.lossyScale.y));
        body.localPosition = basePos;
        body.localRotation = Quaternion.Euler(0f, 0f, rot);
        body.localScale = new Vector3(sx, sy, 1f);

        // コマ送り(一定のfps)。スキップは跳ねる周期に合わせる。
        if (frames != null)
        {
            float f = motion == ReaperMotion.Skip ? t * data.skipRate * frames.Length / 2f : t * data.moveFps;
            FrameIndex = Mathf.FloorToInt(f) % frames.Length;
            sr.sprite = frames[FrameIndex];
        }
        // 影は跳ねると薄く小さく
        if (shadow != null)
        {
            float k = Mathf.Clamp01(1f - y / 1.2f);
            shadow.transform.localScale = new Vector3(data.heightWorld * 0.55f * (0.7f + 0.3f * k), data.heightWorld * 0.1f, 1f);
            shadow.transform.localPosition = new Vector3(0f, motion == ReaperMotion.Float ? -data.floatHeight : 0f, 0f);
        }
    }

    float baseY = float.NaN;
    float BaseY()
    {
        if (float.IsNaN(baseY)) baseY = body.localPosition.y;
        return baseY;
    }
}
