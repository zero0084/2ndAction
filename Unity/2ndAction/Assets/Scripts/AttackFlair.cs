using System.Collections.Generic;
using UnityEngine;

// 攻撃の派手さ(2026-09-30)。全キャラ共通の「上乗せの演出」だけを担当する(判定/ダメージ/既存の演出には触れない)。
//  ・命中: 中心の閃光 + 衝撃の輪 + 攻撃方向へ飛び散る光の筋(強い攻撃/ボスは大きく+画面の揺れ)
//  ・踏み込み/後退(攻撃の前進・後退): 残像3枚 + 後ろへ流れる速度線 + 足元の土煙
//  ・コンボの締め: 大きな斬撃の光
// 色はキャラクターごと(剣士=青白、竜騎士=翡翠、吸血鬼=紅…)。
// Android向けに、1つのUpdateでまとめて動かす使い回し(プール)式。1フレームに出す量にも上限を付ける。
[DefaultExecutionOrder(1300)]
public class AttackFlair : MonoBehaviour
{
    public static AttackFlair Instance { get; private set; }
    public static bool Enabled = true;
    public static int HitCount, StepCount, FinisherCount; // テスト/診断用

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("AttackFlair");
        go.AddComponent<FloatingOriginExempt>(); // 粒は自分で座標を戻す
        DontDestroyOnLoad(go);
        go.AddComponent<AttackFlair>();
    }

    const int PoolSize = 160;
    const int MaxHitsPerFrame = 3;

    struct P
    {
        public SpriteRenderer sr; public Transform t;
        public bool on; public float age, life, delay;
        public Vector3 vel; public float s0, s1, sx, sy; public float a0; public Color col;
        public bool keepSize; // 残像: 元の大きさのまま
    }
    // 光る表現(加算合成、絵の形だけを色で塗る: Shaders/SpriteGlow)。読めなければ通常の絵のまま。
    Material glowMat, normalMat;
    readonly P[] pool = new P[PoolSize];
    int next, hitsThisFrame, frame;
    float lastShake;

    void Awake()
    {
        Instance = this;
        glowMat = Resources.Load<Material>("Effects/SpriteGlow");
        for (int i = 0; i < PoolSize; i++)
        {
            var g = new GameObject("fx");
            g.transform.SetParent(transform, false);
            var sr = g.AddComponent<SpriteRenderer>();
            sr.enabled = false;
            if (normalMat == null) normalMat = sr.sharedMaterial;
            pool[i] = new P { sr = sr, t = g.transform };
        }
        FloatingOrigin.Shifted += OnShift;
    }

    void OnDestroy() { FloatingOrigin.Shifted -= OnShift; if (Instance == this) Instance = null; }

    void OnShift(float s)
    {
        for (int i = 0; i < PoolSize; i++) if (pool[i].on) { var p = pool[i].t.position; p.x -= s; pool[i].t.position = p; }
    }

    // ===================================================================== //
    // 色
    // ===================================================================== //
    public static Color AccentFor(string id)
    {
        switch (id)
        {
            case "swordsman": return new Color(0.55f, 0.85f, 1f);
            case "dual_blade": return new Color(0.4f, 1f, 0.9f);
            case "noble_lady": return new Color(1f, 0.85f, 0.45f);
            case "gunslinger": return new Color(1f, 0.65f, 0.25f);
            case "dragon_lancer": return new Color(0.45f, 1f, 0.7f);
            case "archer": return new Color(1f, 0.9f, 0.5f);
            case "mage": return new Color(0.75f, 0.6f, 1f);
            case "fighter": return new Color(1f, 0.5f, 0.25f);
            case "ninja": return new Color(0.7f, 0.5f, 1f);
            case "miko": return new Color(1f, 0.45f, 0.5f);
            case "vampire": return new Color(1f, 0.25f, 0.35f);
            case "dragonkin": return new Color(1f, 0.55f, 0.2f);
            default: return new Color(0.6f, 0.9f, 1f);
        }
    }
    static Color Accent => AccentFor(GameManager.Instance != null ? GameManager.Instance.SelectedCharacterId : "");

    // ===================================================================== //
    // 命中
    // ===================================================================== //
    public static void Hit(Vector3 pos, float dirX, bool strong)
    {
        if (!Enabled || Instance == null) return;
        Instance.DoHit(pos, dirX, strong);
    }

    void DoHit(Vector3 pos, float dirX, bool strong)
    {
        if (frame != Time.frameCount) { frame = Time.frameCount; hitsThisFrame = 0; }
        if (hitsThisFrame++ >= MaxHitsPerFrame) return;
        HitCount++;
        Color acc = Accent;
        pos.z = 0f;
        // 閃光(白い芯 + キャラの色の光)
        Glow(Spawn(OneShotSpriteEffect.SoftDotSprite(), pos, Vector3.zero, 0.12f, 0.8f, strong ? 3.6f : 2.5f, 1f, 1f, Color.white, 1f, RenderOrder.SlashFx + 1, 0f));
        Glow(Spawn(OneShotSpriteEffect.SoftDotSprite(), pos, Vector3.zero, 0.24f, 1.2f, strong ? 5.0f : 3.4f, 1f, 1f, acc, 0.6f, RenderOrder.SlashFx, 0f));
        // 衝撃の輪
        if (KitProjectile.Ring != null)
        {
            Spawn(KitProjectile.Ring, pos, Vector3.zero, strong ? 0.3f : 0.22f, 0.3f, strong ? 2.8f : 1.8f, 1f, 1f, acc, 0.95f, RenderOrder.SlashFx, 0f);
            if (strong) Glow(Spawn(KitProjectile.Ring, pos, Vector3.zero, 0.36f, 0.5f, 3.6f, 1f, 1f, Color.white, 0.6f, RenderOrder.SlashFx, 0.04f));
        }
        // 光の筋(攻撃の向きへ多め)
        int n = strong ? 11 : 7;
        for (int i = 0; i < n; i++)
        {
            float ang = Random.Range(-70f, 70f) + (dirX < 0f ? 180f : 0f);
            if (Random.value < 0.3f) ang = Random.Range(0f, 360f);
            float rad = ang * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
            float spd = Random.Range(7f, strong ? 15f : 11f);
            Color c = Color.Lerp(Color.white, acc, Random.value * 0.7f);
            var idx = Glow(Spawn(StreakSprite(), pos + dir * 0.2f, dir * spd, Random.Range(0.14f, 0.24f), 1f, 0.45f, strong ? 2.3f : 1.7f, strong ? 2.0f : 1.6f, c, 1f, RenderOrder.SlashFx + 1, 0f));
            if (idx >= 0) pool[idx].t.rotation = Quaternion.Euler(0f, 0f, ang);
        }
        if (strong && Time.unscaledTime - lastShake > 0.12f)
        {
            lastShake = Time.unscaledTime;
            var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (cf != null) cf.Shake(0.09f, 0.1f);
        }
    }

    // ===================================================================== //
    // 踏み込み/後退
    // ===================================================================== //
    public static void Step(PlayerController pc, float sign)
    {
        if (!Enabled || Instance == null || pc == null) return;
        Instance.DoStep(pc, sign);
    }

    void DoStep(PlayerController pc, float sign)
    {
        StepCount++;
        Color acc = Accent;
        var anim = pc.GetComponent<PlayerAnimator>();
        SpriteRenderer vr = anim != null ? anim.VisualRenderer : pc.GetComponentInChildren<SpriteRenderer>();
        if (vr != null && vr.sprite != null)
        {
            for (int i = 0; i < 3; i++)
            {
                // 動く向きの後ろへ少しずつずらして置く(キャラ本体に重なって白く潰れないように)
                Vector3 gp = vr.transform.position + new Vector3(-sign * (0.3f + i * 0.28f), 0f, 0f);
                int idx = Glow(Spawn(vr.sprite, gp, Vector3.zero, 0.22f, 1f, 1f, 1f, 1f, acc, 0.5f - i * 0.12f, RenderOrder.Player - 1, i * 0.04f));
                if (idx < 0) break;
                pool[idx].keepSize = true;
                pool[idx].t.rotation = vr.transform.rotation;
                pool[idx].t.localScale = vr.transform.lossyScale;
                pool[idx].sr.flipX = vr.flipX;
            }
        }
        // 速度線(進む向きと逆へ流れる)
        Vector3 c = pc.transform.position + new Vector3(0f, 0.9f, 0f);
        for (int i = 0; i < 4; i++)
        {
            Vector3 p = c + new Vector3(-sign * Random.Range(0.3f, 1.1f), Random.Range(-0.7f, 0.7f), 0f);
            int idx = Glow(Spawn(StreakSprite(), p, new Vector3(-sign * Random.Range(9f, 14f), 0f, 0f), 0.16f, 1f, 1.6f, 1.8f, 1.1f, Color.Lerp(Color.white, acc, 0.5f), 0.7f, RenderOrder.EnvironmentFx, 0f));
            if (idx >= 0) pool[idx].t.rotation = Quaternion.identity;
        }
        // 足元の土煙
        if (pc.IsGrounded && KitProjectile.Puff != null)
            for (int i = 0; i < 2; i++)
                Spawn(KitProjectile.Puff, pc.transform.position + new Vector3(-sign * (0.2f + i * 0.3f), 0.15f, 0f), new Vector3(-sign * Random.Range(1.5f, 3f), Random.Range(0.3f, 0.9f), 0f),
                    0.3f, 0.25f, 0.55f, 1f, 1f, new Color(0.9f, 0.88f, 0.85f), 0.55f, RenderOrder.EnvironmentFx, 0f);
    }

    // ===================================================================== //
    // コンボの締め
    // ===================================================================== //
    public static void Finisher(Vector3 pos, float dirX)
    {
        if (!Enabled || Instance == null || KitProjectile.Slash == null) return;
        FinisherCount++;
        Color acc = Accent;
        var i = Instance;
        int a = i.Glow(i.Spawn(KitProjectile.Slash, pos, new Vector3(dirX * 2f, 0f, 0f), 0.24f, 2.2f, 3.6f, 1f, 1f, acc, 0.9f, RenderOrder.SlashFx, 0f));
        if (a >= 0) i.pool[a].t.rotation = Quaternion.Euler(0f, 0f, dirX < 0f ? 180f : 0f);
        int b = i.Glow(i.Spawn(KitProjectile.Slash, pos, new Vector3(dirX * 3f, 0f, 0f), 0.18f, 1.7f, 2.8f, 1f, 1f, Color.white, 0.9f, RenderOrder.SlashFx + 1, 0.03f));
        var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cf != null) cf.Shake(0.07f, 0.1f);
        if (b >= 0) i.pool[b].t.rotation = Quaternion.Euler(0f, 0f, dirX < 0f ? 180f : 0f);
    }

    // ===================================================================== //
    // 粒
    // ===================================================================== //
    int Spawn(Sprite sp, Vector3 pos, Vector3 vel, float life, float s0, float s1, float sx, float sy, Color col, float alpha, int order, float delay)
    {
        if (sp == null) return -1;
        // 空きを探す(無ければ一番古いものを使い回す)
        int idx = -1;
        for (int k = 0; k < PoolSize; k++)
        {
            int j = (next + k) % PoolSize;
            if (!pool[j].on) { idx = j; break; }
        }
        if (idx < 0) idx = next;
        next = (idx + 1) % PoolSize;
        ref P p = ref pool[idx];
        p.on = true; p.age = 0f; p.life = Mathf.Max(0.01f, life); p.delay = delay;
        p.vel = vel; p.s0 = s0; p.s1 = s1; p.sx = sx; p.sy = sy; p.col = col; p.a0 = alpha; p.keepSize = false;
        p.sr.sprite = sp; p.sr.sortingOrder = order; p.sr.flipX = false; p.sr.enabled = delay <= 0f;
        if (normalMat != null && p.sr.sharedMaterial != normalMat) p.sr.sharedMaterial = normalMat;
        Color c = col; c.a = alpha; p.sr.color = c;
        p.t.position = pos; p.t.rotation = Quaternion.identity;
        p.t.localScale = new Vector3(s0 * sx, s0 * sy, 1f);
        return idx;
    }

    int Glow(int idx)
    {
        if (idx >= 0 && glowMat != null) pool[idx].sr.sharedMaterial = glowMat;
        if (idx >= 0) pool[idx].a0 *= GameSettings.GlowIntensity; // 設定「発光演出」の強さ(2026-10-01)
        return idx;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        for (int i = 0; i < PoolSize; i++)
        {
            ref P p = ref pool[i];
            if (!p.on) continue;
            if (p.delay > 0f) { p.delay -= dt; if (p.delay <= 0f) p.sr.enabled = true; else continue; }
            p.age += dt;
            float f = p.age / p.life;
            if (f >= 1f) { p.on = false; p.sr.enabled = false; continue; }
            float e = 1f - (1f - f) * (1f - f);
            if (!p.keepSize)
            {
                float s = Mathf.Lerp(p.s0, p.s1, e);
                p.t.localScale = new Vector3(s * p.sx, s * p.sy, 1f);
            }
            if (p.vel != Vector3.zero) { p.t.position += p.vel * dt; p.vel *= Mathf.Max(0f, 1f - 6f * dt); }
            Color c = p.col; c.a = p.a0 * (1f - f * f);
            p.sr.color = c;
        }
    }

    // 光の筋: 横長で中心が明るいグラデーション(実行時に作る)
    static Sprite streak;
    static Sprite StreakSprite()
    {
        if (streak != null) return streak;
        const int w = 64, h = 8;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)(w - 1), v = Mathf.Abs(y - (h - 1) * 0.5f) / (h * 0.5f);
                float a = Mathf.Clamp01(1f - Mathf.Abs(u - 0.6f) / 0.6f) * Mathf.Clamp01(1f - v * v);
                px[y * w + x] = new Color32(255, 255, 255, (byte)(255 * a));
            }
        tex.SetPixels32(px);
        tex.Apply();
        streak = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 64f);
        streak.name = "streak(runtime)";
        return streak;
    }
}
