using System.Collections.Generic;
using UnityEngine;

// 属性の視認性の改修(2026-10-30、マスター依頼)。性能値(確率/ダメージ/時間/倍率)は変えず、「発動した」「状態が付いている」を見えるようにする。
//  ・付与の瞬間の演出 / 状態中の体の表示(ElementAura)/ 頭上の小さな属性マーク(ElementMarks)/ 短い SE(間引き)
//  ・上位カードほど少しだけ強く(Tier: 入口=1 / 中位=2 / 上位=3。性能ではなく見た目だけ)
//  ・敵の大きさで演出の大きさを変える(大きなボスで体に埋もれない)
//  ・負荷: 粒は1つの更新ループでまとめて動かす(プール、上限 MaxParticles)、稲妻はプール、同じ敵への連続は間引き、画面外は出さない
//  ・旧表示との比較: 開発版の起動引数 -elemVfx old(Legacy = true)で前の見た目(頭上の点・細い稲妻)だけになる
public class ElementVfx : MonoBehaviour
{
    public static bool Legacy;            // 前の見た目(比較用。開発版の -elemVfx old)
    public const int MaxParticles = 260;  // 同時に出す粒の上限(90〜95km の雑魚の大群でも増え過ぎない)
    public const int MaxBolts = 18;
    static ElementVfx inst;
    static bool argsRead;

    // 確認用(-qaElemVis / -qaElemPerf)
    public static int Spawned, Dropped, BoltsDrawn, SfxPlayed, SfxThinned, PeakParticles;

    public static ElementVfx I
    {
        get
        {
            if (!argsRead) ReadArgs();
            if (inst == null)
            {
                var go = new GameObject("[ElementVfx]");
                DontDestroyOnLoad(go);
                inst = go.AddComponent<ElementVfx>();
            }
            return inst;
        }
    }

    static void ReadArgs()
    {
        argsRead = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var a = QaArgs.All;
        for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-elemVfx") Legacy = a[i + 1] == "old";
#endif
    }
    public static bool New { get { if (!argsRead) ReadArgs(); return !Legacy; } }

    // ===================================================================== 色
    public static readonly Color FireCore = new Color(1f, 0.85f, 0.35f), FireEdge = new Color(1f, 0.38f, 0.08f);
    public static readonly Color IceCore = new Color(0.9f, 0.98f, 1f), IceEdge = new Color(0.45f, 0.8f, 1f);
    public static readonly Color BoltCore = new Color(1f, 0.98f, 0.78f), BoltEdge = new Color(1f, 0.78f, 0.12f);
    public static readonly Color WindCore = new Color(0.85f, 1f, 0.95f), WindEdge = new Color(0.25f, 0.85f, 0.7f);
    public static readonly Color BloodCore = new Color(0.95f, 0.2f, 0.3f), BloodEdge = new Color(0.45f, 0.02f, 0.1f);

    // ===================================================================== 上位カードほど少し強く(見た目だけ)
    // 入口のカードだけ = 1 / 中位を持つ = 2 / 上位を持つ = 3(入口が無くても、持っているカードの段で決める)
    public static int Tier(ElementType e)
    {
        var gm = GameManager.Instance;
        if (gm == null) return 1;
        bool Has(string id) => gm.CardLevel(id) > 0;
        switch (e)
        {
            // 2026-10-31 カード拡張: 完成(★4)の COMBUSTION / SHATTER STRIKE / JUDGMENT BOLT / TEMPEST は段階3、発展(★3)は段階2
            case ElementType.Fire: return Has("inferno") || Has("combustion") ? 3 : Has("burning_soul") || Has("fire_whirl") ? 2 : 1;
            case ElementType.Ice: return Has("absolute_zero") || Has("shatter_strike") ? 3 : Has("ice_prison") || Has("glacier_guard") ? 2 : 1;
            case ElementType.Thunder: return Has("thunder_lord") || Has("judgment_bolt") ? 3 : Has("chain_lightning") || Has("high_voltage") || Has("volt_surge") ? 2 : 1;
            case ElementType.Wind: return Has("tornado") || Has("tempest") ? 3 : Has("gale") || Has("updraft") ? 2 : 1;
            case ElementType.Blood: return Has("phoenix") || Has("blood_blade") ? 3 : Has("predator") || Has("blood_rush") || Has("overheal") ? 2 : 1;
        }
        return 1;
    }

    // 敵の大きさ(演出の大きさの倍率)。雑魚 ≈ 1、大きなボス ≈ 2〜3
    public static float SizeOf(Component c, out Bounds b)
    {
        b = BoundsOf(c);
        return Mathf.Clamp(Mathf.Max(b.size.x, b.size.y) / 1.6f, 0.8f, 3.2f);
    }
    public static Bounds BoundsOf(Component c)
    {
        if (c == null) return new Bounds(Vector3.zero, Vector3.one);
        if (c is WildBossBase wb && wb.DebugHurtCol != null) return wb.DebugHurtCol.bounds;
        var col = c.GetComponentInChildren<Collider2D>();
        return col != null ? col.bounds : new Bounds(c.transform.position + Vector3.up, Vector3.one * 1.2f);
    }

    static bool OnScreen(Vector3 p, float margin = 2f)
    {
        var cam = Camera.main;
        if (cam == null) return true;
        Vector3 v = cam.WorldToViewportPoint(p);
        float m = margin / Mathf.Max(1f, cam.orthographicSize * 2f);
        return v.x > -m && v.x < 1f + m && v.y > -m && v.y < 1f + m;
    }

    // ===================================================================== 粒(まとめて1つのループで動かす)
    struct P
    {
        public SpriteRenderer sr; public Vector3 pos, vel; public float age, life, s0, s1, a0, a1, rot, spin, grav;
        public Color col; public Transform follow; public Vector3 followOff; public bool toPlayer;
    }
    readonly List<P> live = new List<P>(MaxParticles);
    readonly Stack<SpriteRenderer> pool = new Stack<SpriteRenderer>();
    static Material addMat;
    public static Material AddMat
    {
        get
        {
            if (addMat == null) addMat = Resources.Load<Material>("Effects/SpriteAdd");
            return addMat != null ? addMat : SharedMaterials.SpritesDefault;
        }
    }

    SpriteRenderer Take(Sprite s, bool add, int order)
    {
        SpriteRenderer sr = pool.Count > 0 ? pool.Pop() : null;
        if (sr == null)
        {
            var go = new GameObject("ElemFx");
            go.transform.SetParent(transform, false);
            sr = go.AddComponent<SpriteRenderer>();
        }
        sr.gameObject.SetActive(true);
        sr.sprite = s;
        sr.sharedMaterial = add ? AddMat : SharedMaterials.SpritesDefault;
        sr.sortingOrder = order;
        return sr;
    }

    // 1つの粒。drift = 1秒あたりの移動、grav = 下向きの加速(負なら上へ)
    public static bool Spawn(Sprite s, Vector3 at, Color c, float life, float s0, float s1, Vector3 drift, float a0 = 1f, float a1 = 0f,
        bool add = true, float spin = 0f, float grav = 0f, int order = RenderOrder.CombatFx, Transform follow = null, bool toPlayer = false, bool low = false)
    {
        var v = I;
        if (v.live.Count >= MaxParticles || (low && v.live.Count >= MaxParticles * 7 / 10)) { Dropped++; return false; }
        var sr = v.Take(s, add, order);
        var p = new P { sr = sr, pos = at, vel = drift, life = Mathf.Max(0.02f, life), s0 = s0, s1 = s1, a0 = a0, a1 = a1, col = c, rot = Random.Range(0f, 360f) * (spin != 0f ? 1f : 0f), spin = spin, grav = grav, follow = follow, toPlayer = toPlayer };
        if (follow != null) p.followOff = at - follow.position;
        v.live.Add(p);
        Spawned++;
        if (v.live.Count > PeakParticles) PeakParticles = v.live.Count;
        v.Apply(ref p, 0f);
        return true;
    }

    void Apply(ref P p, float t)
    {
        float k = Mathf.Clamp01(t);
        Vector3 pos = p.follow != null ? p.follow.position + p.followOff : p.pos;
        p.sr.transform.position = pos;
        p.sr.transform.localScale = Vector3.one * Mathf.Lerp(p.s0, p.s1, k);
        p.sr.transform.rotation = Quaternion.Euler(0f, 0f, p.rot);
        Color c = p.col; c.a *= Mathf.Lerp(p.a0, p.a1, k);
        p.sr.color = c;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        var pc = PlayerController.Instance;
        Vector3 pTarget = pc != null ? pc.transform.position + Vector3.up * 0.9f : Vector3.zero;
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var p = live[i];
            p.age += dt;
            if (p.age >= p.life || p.sr == null)
            {
                if (p.sr != null) { p.sr.gameObject.SetActive(false); pool.Push(p.sr); }
                live.RemoveAt(i);
                continue;
            }
            if (p.toPlayer && pc != null)
            {
                // 血: 敵からプレイヤーへ戻ってくる(だんだん強く引き寄せる)
                Vector3 to = pTarget - p.pos;
                float pull = Mathf.Lerp(4f, 30f, p.age / p.life);
                p.vel = Vector3.Lerp(p.vel, to.normalized * Mathf.Max(6f, to.magnitude * 4f), Mathf.Clamp01(pull * dt));
                if (to.sqrMagnitude < 0.09f) p.age = p.life; // 届いた
            }
            p.vel.y -= p.grav * dt;
            p.pos += p.vel * dt;
            p.rot += p.spin * dt;
            Apply(ref p, p.age / p.life);
            live[i] = p;
        }
        // 稲妻(短く残して消える)
        for (int i = bolts.Count - 1; i >= 0; i--)
        {
            var b = bolts[i];
            b.age += dt;
            float a = 1f - b.age / b.life;
            if (a <= 0f) { b.core.gameObject.SetActive(false); boltPool.Push(b); bolts.RemoveAt(i); continue; }
            b.core.startColor = b.core.endColor = new Color(BoltCore.r, BoltCore.g, BoltCore.b, a);
            b.glow.startColor = b.glow.endColor = new Color(BoltEdge.r, BoltEdge.g, BoltEdge.b, 0.85f * a);
            bolts[i] = b;
        }
        sfxWindowAge += dt;
        if (sfxWindowAge > 0.25f) { sfxWindowAge = 0f; sfxInWindow = 0; }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        PerfLog();
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 開発版: -elemPerfLog 1 で10秒ごとに fps / 粒 / 体の表示 / マーク / SE を出す(大群・ラッシュ・ラスダンの既存の試験を属性ありで走らせて負荷を見る)
    static int perfLog = -1; float perfT, perfMinFps = 999f; int perfFrames, perfPeakP;
    void PerfLog()
    {
        if (perfLog < 0) { perfLog = 0; var a = QaArgs.All; for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-elemPerfLog" && a[i + 1] == "1") perfLog = 1; }
        if (perfLog == 0) return;
        float udt = Time.unscaledDeltaTime;
        perfT += udt; perfFrames++; perfMinFps = Mathf.Min(perfMinFps, 1f / Mathf.Max(1e-4f, udt)); perfPeakP = Mathf.Max(perfPeakP, live.Count);
        if (perfT < 10f) return;
        var gm = GameManager.Instance;
        Debug.Log($"[ElemPerf] d={(gm != null ? gm.MaxDistance : 0f):F0}m fps avg {perfFrames / perfT:F1} min {perfMinFps:F1} particles peak {perfPeakP} auras {ElementAura.PeakAuras} marks {ElementAura.PeakMarked} bolts {BoltsDrawn} sfx {SfxPlayed}/{SfxThinned} dropped {Dropped} enemies {EnemyController.ActiveCount} net q/sent/msg/drop/bytes={NetElementFx.Queued}/{NetElementFx.Sent}/{NetElementFx.Messages}/{NetElementFx.Dropped}/{NetElementFx.BytesSent} recv/played/missing={NetElementFx.Received}/{NetElementFx.Played}/{NetElementFx.Missing} mem {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576}MB | {ElementTelemetry.Report().Replace("\n", " |")}");
        perfT = 0f; perfFrames = 0; perfMinFps = 999f; perfPeakP = 0; ElementAura.PeakAuras = 0; ElementAura.PeakMarked = 0;
    }
#endif

    public static int LiveParticles => inst != null ? inst.live.Count : 0;

    // ===================================================================== 稲妻(太い芯 + 光の縁。出すたびに作らずプール)
    class BoltFx { public LineRenderer core, glow; public float age, life; }
    readonly List<BoltFx> bolts = new List<BoltFx>();
    readonly Stack<BoltFx> boltPool = new Stack<BoltFx>();
    BoltFx TakeBolt()
    {
        if (boltPool.Count > 0) { var b = boltPool.Pop(); b.core.gameObject.SetActive(true); b.age = 0f; return b; }
        var go = new GameObject("ElemBolt");
        go.transform.SetParent(transform, false);
        var core = go.AddComponent<LineRenderer>();
        var g2 = new GameObject("Glow"); g2.transform.SetParent(go.transform, false);
        var glow = g2.AddComponent<LineRenderer>();
        foreach (var lr in new[] { core, glow })
        {
            lr.sharedMaterial = AddMat; lr.useWorldSpace = true; lr.numCapVertices = 2; lr.numCornerVertices = 1;
        }
        core.sortingOrder = RenderOrder.CombatFx + 2; glow.sortingOrder = RenderOrder.CombatFx + 1;
        glow.sharedMaterial = SharedMaterials.SpritesDefault; // 外側は普通の合成(白く飛ばない)
        return new BoltFx { core = core, glow = glow };
    }

    // tier: 太さ。chain = 敵から敵への連鎖の線(少し長く残して追えるように)
    public static void Bolt(Vector3 a, Vector3 b, int tier, bool chain)
    {
        var v = I;
        if (v.bolts.Count >= MaxBolts) { Dropped++; return; }
        BoltsDrawn++;
        var fx = v.TakeBolt();
        fx.life = chain ? 0.24f : 0.16f;
        float w = 0.07f + 0.025f * (tier - 1);     // 芯は細く(上位ほど少しだけ太い)
        int n = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(a, b) / 0.45f), 4, 14);
        fx.core.positionCount = n; fx.glow.positionCount = n;
        float jag = chain ? 0.18f : 0.28f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)(n - 1);
            Vector3 p = Vector3.Lerp(a, b, t);
            if (i > 0 && i < n - 1) p += new Vector3(Random.Range(-jag, jag), Random.Range(-jag, jag), 0f);
            fx.core.SetPosition(i, p); fx.glow.SetPosition(i, p);
        }
        fx.core.startWidth = w; fx.core.endWidth = w * 0.6f;
        fx.glow.startWidth = w * 2.4f; fx.glow.endWidth = w * 1.6f; // 外側はくっきりした金黄(広いにじみにしない)
        v.bolts.Add(fx);
    }

    // ===================================================================== SE(小さめ・間引き)
    float sfxWindowAge; int sfxInWindow;
    static readonly float[] lastSfx = new float[8];
    public static void Sfx(ElementType e, float minGap = 0.1f)
    {
        if (!New || AudioManager.Instance == null) return;
        var v = I;
        int k = (int)e;
        if (Time.unscaledTime - lastSfx[k] < minGap || v.sfxInWindow >= 4) { SfxThinned++; return; } // 同じ属性は 0.1 秒に1回・全属性で 0.25 秒に4回まで
        lastSfx[k] = Time.unscaledTime; v.sfxInWindow++;
        SeId id;
        switch (e)
        {
            case ElementType.Fire: id = SeId.ElemFire; break;
            case ElementType.Ice: id = SeId.ElemIce; break;
            case ElementType.Thunder: id = SeId.ElemThunder; break;
            case ElementType.Wind: id = SeId.ElemWind; break;
            case ElementType.Blood: id = SeId.ElemBlood; break;
            default: return;
        }
        SfxPlayed++;
        AudioManager.Instance.PlaySe(id);
    }

    // ===================================================================== 同じ敵への連続の間引き
    static readonly Dictionary<(Component, int), float> lastOn = new Dictionary<(Component, int), float>();
    static bool Gate(Component c, int kind, float gap)
    {
        if (c == null) return false;
        var key = (c, kind);
        float now = Time.time;
        if (lastOn.TryGetValue(key, out float t) && now - t < gap) return false;
        if (lastOn.Count > 512) lastOn.Clear();
        lastOn[key] = now;
        return true;
    }

    // ===================================================================== 付与の瞬間
    // 炎: 少し大きめの炎がぼっと上がる(上位ほど大きく、粒が多い)
    public static void BurnApplied(Component victim)
    {
        if (!New || victim == null || !Gate(victim, 0, 0.35f)) return;
        float s = SizeOf(victim, out Bounds b);
        if (!OnScreen(b.center)) return;
        int tier = Tier(ElementType.Fire);
        Vector3 at = new Vector3(b.center.x, Mathf.Lerp(b.min.y, b.center.y, 0.6f), 0f);
        Spawn(ElementArt.Flame, at, new Color(1f, 0.4f, 0.06f, 0.95f), 0.4f, 0.8f * s, (1.5f + 0.2f * tier) * s, Vector3.up * 1.6f, 0.95f, 0f, false);
        Spawn(ElementArt.Flame, at + Vector3.up * 0.05f * s, FireCore, 0.32f, 0.5f * s, (0.95f + 0.12f * tier) * s, Vector3.up * 2f, 1f, 0f);
        int n = 3 + 2 * tier;
        for (int i = 0; i < n; i++)
            Spawn(ElementArt.Spark, at + (Vector3)(Random.insideUnitCircle * 0.3f * s), Color.Lerp(FireCore, FireEdge, Random.value), Random.Range(0.35f, 0.6f), 0.16f, 0.05f,
                new Vector3(Random.Range(-1.6f, 1.6f), Random.Range(1.5f, 3.5f), 0f) * Mathf.Sqrt(s), 1f, 0f, true, 0f, 2f);
        Sfx(ElementType.Fire);
    }

    // 炎の継続ダメージの瞬間: 小さな火の粉
    public static void BurnTick(Component victim, Bounds b, float s, int tier)
    {
        if (!New || !OnScreen(b.center)) return;
        int n = 1 + tier;
        for (int i = 0; i < n; i++)
        {
            Vector3 p = new Vector3(Random.Range(b.min.x, b.max.x), Random.Range(b.min.y + b.size.y * 0.2f, b.max.y), 0f);
            Spawn(ElementArt.Spark, p, Color.Lerp(FireCore, FireEdge, Random.value), 0.45f, 0.13f * Mathf.Sqrt(s), 0.04f, new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(1.2f, 2.4f), 0f), 1f, 0f, true, 0f, -0.5f, low: true);
        }
    }

    // 氷: 青白い結晶が一瞬走る / 凍結は結晶が大きく広がる
    public static void ChillApplied(Component victim, bool froze)
    {
        if (!New || victim == null) return;
        if (!froze && !Gate(victim, 1, 0.3f)) return;
        float s = SizeOf(victim, out Bounds b);
        if (!OnScreen(b.center)) return;
        int tier = Tier(ElementType.Ice);
        Vector3 at = b.center;
        if (froze)
        {
            Spawn(ElementArt.Crystal, at, IceCore, 0.42f, 0.5f * s, (1.5f + 0.2f * tier) * s, Vector3.zero, 1f, 0f, true, 40f);
            for (int i = 0; i < 6 + 2 * tier; i++)
            {
                float ang = i * Mathf.PI * 2f / (6 + 2 * tier);
                Vector3 d = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
                Spawn(ElementArt.Shard, at + d * 0.3f * s, Color.Lerp(IceCore, IceEdge, 0.4f), 0.32f, 0.3f * s, 0.12f * s, d * 2.4f * Mathf.Sqrt(s), 1f, 0f, true, 0f);
            }
            Sfx(ElementType.Ice, 0.06f);
        }
        else
        {
            Spawn(ElementArt.Crystal, at, IceEdge, 0.24f, 0.35f * s, (0.9f + 0.1f * tier) * s, Vector3.zero, 0.85f, 0f, true, 90f);
            Sfx(ElementType.Ice);
        }
    }

    // 凍結が砕ける(ABSOLUTE ZERO / COMBO SHATTER): 氷片を飛ばす
    public static void Shatter(Vector3 at, float s)
    {
        if (!New || !OnScreen(at)) return;
        for (int i = 0; i < 10; i++)
        {
            Vector2 d = Random.insideUnitCircle.normalized;
            Spawn(ElementArt.Shard, at + (Vector3)(d * 0.2f * s), Color.Lerp(IceCore, IceEdge, Random.value * 0.6f), Random.Range(0.4f, 0.65f), 0.32f * Mathf.Sqrt(s), 0.15f,
                new Vector3(d.x * 4f, d.y * 3f + 2.5f, 0f) * Mathf.Sqrt(s), 1f, 0.2f, false, Random.Range(-540f, 540f), 9f);
        }
        Spawn(ElementArt.Ring, at, IceCore, 0.3f, 0.4f * s, 1.8f * s, Vector3.zero, 0.9f, 0f);
        Sfx(ElementType.Ice, 0.05f);
    }

    // 雷: 落ちた所に小さな光と放電
    static int impactFrame = -1, impactsThisFrame;
    public static void StrikeImpact(Component victim, int tier)
    {
        if (!New || victim == null || !Gate(victim, 2, 0.15f)) return;
        if (impactFrame != Time.frameCount) { impactFrame = Time.frameCount; impactsThisFrame = 0; }
        if (++impactsThisFrame > 3) { Sfx(ElementType.Thunder, 0.08f); return; } // 同じフレームの光は3つまで(連鎖の線は全部出す)
        float s = SizeOf(victim, out Bounds b);
        if (!OnScreen(b.center)) return;
        Spawn(ElementArt.Glow, b.center, BoltEdge, 0.12f, 0.45f * s, (0.75f + 0.1f * tier) * s, Vector3.zero, 0.4f, 0f); // 短く弱く(画面を白くしない)
        for (int i = 0; i < 2 + tier; i++)
        {
            Vector2 d = Random.insideUnitCircle.normalized;
            Spawn(ElementArt.Spark, b.center + (Vector3)(d * 0.25f * s), BoltCore, 0.22f, 0.14f, 0.04f, (Vector3)(d * 5f), 1f, 0f);
        }
        Sfx(ElementType.Thunder, 0.08f);
    }

    // 風: 風刃を出した時、プレイヤーの体に短い風の線(空中戦の助け)
    public static void WindRelease(Vector3 at, float dir)
    {
        if (!New) return;
        int tier = Tier(ElementType.Wind);
        for (int i = 0; i < 2 + tier; i++)
        {
            Vector3 p = at + new Vector3(-dir * Random.Range(0.1f, 0.6f), Random.Range(-0.6f, 0.5f), 0f);
            Spawn(ElementArt.Streak, p, WindEdge, 0.22f, 0.45f, 0.3f, new Vector3(dir * 9f, 0f, 0f), 0.9f, 0f);
        }
        Sfx(ElementType.Wind, 0.12f);
    }

    // 風刃の当たった所の小さな風圧(渦)
    public static void WindHit(Component victim)
    {
        if (!New || victim == null || !Gate(victim, 3, 0.2f)) return;
        float s = SizeOf(victim, out Bounds b);
        if (!OnScreen(b.center)) return;
        Spawn(ElementArt.Swirl, b.center, WindEdge, 0.3f, 0.4f * s, 0.95f * s, Vector3.zero, 0.95f, 0f, false, 720f);
        Spawn(ElementArt.Swirl, b.center, WindCore, 0.25f, 0.3f * s, 0.6f * s, Vector3.zero, 0.45f, 0f, true, 720f); // 白い芯は控えめ(エステル: 白い面積を増やさない)
    }

    // 竜巻(TORNADO / COMBO CYCLONE): 渦の柱と、巻き込んだ敵の周りに風の輪
    public static void Tornado(Vector3 at, float radius, int pulse)
    {
        if (!New || !OnScreen(at)) return;
        for (int i = 0; i < 3; i++)
            Spawn(ElementArt.Swirl, at + Vector3.up * (0.2f + 0.55f * i), i % 2 == 0 ? WindEdge : WindCore, 0.42f, radius * (0.7f + 0.25f * i), radius * (0.9f + 0.3f * i),
                Vector3.up * 0.4f, 0.9f, 0f, i % 2 == 1, 900f - 120f * i);
        for (int i = 0; i < 4; i++)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            Vector3 p = at + new Vector3(Mathf.Cos(ang) * radius, Mathf.Sin(ang) * radius * 0.6f + 0.5f, 0f);
            Spawn(ElementArt.Streak, p, WindEdge, 0.3f, 0.35f, 0.2f, (at + Vector3.up * 0.6f - p) * 2.5f, 0.9f, 0f, false);
        }
        if (pulse == 0) Sfx(ElementType.Wind, 0.15f);
    }
    public static void WindRing(Component c)
    {
        if (!New || c == null || !Gate(c, 4, 0.35f)) return;
        float s = SizeOf(c, out Bounds b);
        Spawn(ElementArt.Ring, b.center, WindEdge, 0.35f, 0.6f * s, 1.2f * s, Vector3.zero, 0.9f, 0f, false);
    }

    // 血: 出血の付与(赤黒い滴が落ちる)
    public static void BleedApplied(Component victim)
    {
        if (!New || victim == null || !Gate(victim, 5, 0.4f)) return;
        float s = SizeOf(victim, out Bounds b);
        if (!OnScreen(b.center)) return;
        for (int i = 0; i < 4; i++)
            Spawn(ElementArt.Drop, b.center + (Vector3)(Random.insideUnitCircle * 0.3f * s), Color.Lerp(BloodCore, BloodEdge, Random.value * 0.6f), 0.5f, 0.2f * Mathf.Sqrt(s), 0.14f,
                new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(0.5f, 2f), 0f), 1f, 0.2f, false, 0f, 8f);
        Sfx(ElementType.Blood, 0.15f);
    }

    // 血: 敵からプレイヤーへ戻ってくる(吸収/出血の回復)。届いた時にプレイヤーに小さな回復の光
    public static void BloodReturn(Vector3 from, int amount)
    {
        if (!New) return;
        var pc = PlayerController.Instance;
        if (pc == null) return;
        int n = Mathf.Clamp(3 + amount, 3, 8);
        for (int i = 0; i < n; i++)
            Spawn(ElementArt.Drop, from + (Vector3)(Random.insideUnitCircle * 0.35f), Color.Lerp(BloodCore, BloodEdge, Random.value * 0.4f), 0.75f, 0.2f, 0.12f,
                new Vector3(Random.Range(-2f, 2f), Random.Range(1.5f, 4f), 0f), 1f, 0.6f, false, 0f, 0f, RenderOrder.CombatFx, null, true);
        I.Invoke(nameof(HealGlowLater), 0.35f);
        Sfx(ElementType.Blood, 0.12f);
    }
    void HealGlowLater()
    {
        var pc = PlayerController.Instance;
        if (pc == null) return;
        Spawn(ElementArt.Ring, pc.transform.position + Vector3.up * 0.9f, BloodCore, 0.35f, 0.6f, 1.6f, Vector3.zero, 0.8f, 0f, true, 0f, 0f, RenderOrder.PlayerOverlay + 1, pc.transform);
        Spawn(ElementArt.Glow, pc.transform.position + Vector3.up * 0.9f, new Color(1f, 0.5f, 0.55f), 0.3f, 0.8f, 1.4f, Vector3.zero, 0.5f, 0f, true, 0f, 0f, RenderOrder.PlayerOverlay + 1, pc.transform);
    }

    // マルチ JOIN(2026-10-30): HOST の風刃の見た目(当たり判定なし。縁の付いた筋が前へ流れる)
    public static void RemoteWindBlade(Vector3 at, float dir, float range)
    {
        if (!New || !OnScreen(at, 6f)) return;
        float speed = 18f, life = Mathf.Clamp(range / speed, 0.15f, 0.8f);
        Spawn(ElementArt.Streak, at, new Color(0.05f, 0.42f, 0.36f, 0.8f), life, 1.1f, 1f, new Vector3(dir * speed, 0f, 0f), 0.85f, 0.3f, false, 0f, 0f, RenderOrder.CombatFx);
        Spawn(ElementArt.Streak, at, WindCore, life, 0.95f, 0.9f, new Vector3(dir * speed, 0f, 0f), 1f, 0.3f, true, 0f, 0f, RenderOrder.CombatFx + 1);
        Sfx(ElementType.Wind, 0.12f);
    }
    // マルチ JOIN: HOST の吸収(血が浮き上がって消える。回復したのは HOST のプレイヤー)
    public static void RemoteBlood(Vector3 at)
    {
        if (!New || !OnScreen(at)) return;
        for (int i = 0; i < 4; i++)
            Spawn(ElementArt.Drop, at + (Vector3)(Random.insideUnitCircle * 0.3f), Color.Lerp(BloodCore, BloodEdge, Random.value * 0.4f), 0.6f, 0.2f, 0.1f,
                new Vector3(Random.Range(-1f, 1f), Random.Range(2f, 3.5f), 0f), 1f, 0f, false);
        Sfx(ElementType.Blood, 0.2f);
    }

    // 撃破の位置(VAMPIRE の吸収は撃破で判定するので、その敵から戻す)
    public static Vector3 LastKillPos { get; private set; }
    public static float LastKillTime { get; private set; } = -9f;
    public static void NoteKill(Vector3 at) { LastKillPos = at; LastKillTime = Time.time; }
    public static Vector3 LastHitPos { get; set; }

    // INFERNO の延焼: 炎の柱が少し大きく上がる(上位の印)
    public static void InfernoBurst(Vector3 at, float radius)
    {
        if (!New || !OnScreen(at)) return;
        Spawn(ElementArt.Flame, at, FireEdge, 0.5f, radius * 0.5f, radius * 1.15f, Vector3.up * 1.2f, 1f, 0f);
        Spawn(ElementArt.Flame, at + Vector3.up * 0.2f, FireCore, 0.4f, radius * 0.3f, radius * 0.75f, Vector3.up * 1.6f, 1f, 0f);
        Spawn(ElementArt.Ring, at, FireEdge, 0.32f, radius * 0.4f, radius * 1.9f, Vector3.zero, 0.85f, 0f);
        for (int i = 0; i < 8; i++)
            Spawn(ElementArt.Spark, at, Color.Lerp(FireCore, FireEdge, Random.value), 0.55f, 0.18f, 0.05f, new Vector3(Random.Range(-4f, 4f), Random.Range(2f, 6f), 0f), 1f, 0f, true, 0f, 6f);
        Sfx(ElementType.Fire, 0.08f);
    }
}
