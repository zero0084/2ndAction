using System.Collections.Generic;
using UnityEngine;

// 自然洞窟ボス強化(2026-10-04) - 洞窟の「地形が攻撃してくる」部品(床/天井/落石/毒だまり/結晶柱/横切る体/地中の予兆)。
// すべて走行の座標系で流れる(TrackedHazard/BossProjectileと同じ: 走る速さが変わっても予告から発動までの見え方が同じ)。
//  ・Floor   … 床から突き出す(高さ1.2以下=跳ぶ / 背が高い=二段ジャンプ)。予告は赤(背が高いのは金)
//  ・Ceiling … 天井から下りてくる(地面から2.3より上=地面にいれば当たらない)。予告は紫+天井のひび+小石
//  ・Band    … 指定の高さの帯(視線/ブレス/魔法陣の柱など)
//  ・FallRock… 影+ひび+小石の予告 → 天井から落ちる → 着地した岩がプレイヤーへ転がってくる(跳ぶ)
//  ・Pool    … 毒だまり。前方に広がり、プレイヤーの方へ流れてくる(跳び越える)。上に乗っている間は一定間隔で被弾
//  ・Pillar  … せり上がる結晶柱。流れてくる障害物で、攻撃で壊せる(壊すか跳ぶ)
//  ・Wave    … 横切る体/衝撃波。見た目と当たり判定の大きさを分ける(判定は見た目より小さい)
//  ・Tell    … 当たり判定の無い予告(地面の盛り上がり/影/天井のひび/魔法陣)。ボスの位置に付いて動ける
// Android向け: 使い終わったら使い回す(シングル)。マルチのHOSTでは毎回壊す(NetAttackSyncの消滅通知を確実に送るため)。
public enum CaveHazardKind : byte { Floor, Ceiling, Band, FallRock, Pool, Pillar, Wave, Tell }
public enum CaveLook : byte { Rock, Crystal, Poison, Magic, Dirt, Flesh, Gaze, Fire, Bone }
public enum CaveTellStyle : byte { Bulge, Shadow, Crack, Circle }

public class CaveHazard : MonoBehaviour
{
    // ---- 統計(自動テスト) ----
    public static int Spawned, Reused, Violations, PlayerHits;
    public static int LiveCount => live.Count;
    public static IReadOnlyList<CaveHazard> Live => live;
    // 自動テスト: 攻撃が初めてプレイヤーの列に重なった時(天井側か, 出した物)。必殺技の「判断の回数」を数える
    public static System.Action<bool, string> OverPlayer;
    bool overReported;
    static int floorOverFrame = -1, ceilOverFrame = -1, violationFrame = -1;
    static CaveHazard lastFloorOver, lastCeilOver;

    static readonly Stack<CaveHazard> pool = new Stack<CaveHazard>();
    static readonly List<CaveHazard> live = new List<CaveHazard>();
    static bool warpHooked;
    const int PoolMax = 48;

    SpriteRenderer band, body, shadow, pebA, pebB;
    BoxCollider2D col;

    CaveHazardKind kind;
    CaveLook look;
    CaveTellStyle tell;
    float width, yLow, yHigh, warn, active, life, timer, drift, colShrink = 0.85f, fallDur = 0.35f;
    int damage, hitsLeft;
    bool heavy, strong, landed, released, activated;
    float lastHitTime = -9f, rot, slowFactor = 1f, slowDuration;
    Transform follow; float followOffset;
    string src = "Cave";
    public System.Action<CaveHazard> onActivate, onLand;

    public CaveHazardKind Kind => kind;
    public bool IsActive => col != null && col.enabled;
    public bool Damaging => damage > 0;
    static int serialNext;
    public int Serial { get; private set; }
    public float Width => width;
    public string Source => src;
    // 「天井から」(地面にいれば当たらない)か
    public bool CeilingType => kind == CaveHazardKind.Ceiling || (kind == CaveHazardKind.Band && yLow >= 1.9f);

    public static Color WarnColorFor(bool ceiling, bool tall) => ceiling ? WildBossBase.HighLaneColor : tall ? WildBossBase.TallLaneColor : WildBossBase.LowLaneColor;

    public static Color LookColor(CaveLook l)
    {
        switch (l)
        {
            case CaveLook.Crystal: return new Color(0.55f, 0.88f, 1f, 1f);
            case CaveLook.Poison: return new Color(0.45f, 1f, 0.35f, 0.9f);
            case CaveLook.Magic: return new Color(0.78f, 0.32f, 1f, 0.95f);
            case CaveLook.Dirt: return new Color(0.62f, 0.47f, 0.32f, 1f);
            case CaveLook.Flesh: return new Color(0.5f, 0.82f, 0.5f, 1f);
            case CaveLook.Gaze: return new Color(0.9f, 1f, 0.4f, 0.85f);
            case CaveLook.Fire: return new Color(1f, 0.52f, 0.2f, 0.95f);
            case CaveLook.Bone: return new Color(0.92f, 0.88f, 0.78f, 1f);
            default: return new Color(0.58f, 0.5f, 0.44f, 1f);
        }
    }

    static Sprite LookSprite(CaveLook l, CaveHazardKind k)
    {
        switch (l)
        {
            case CaveLook.Crystal: return CaveBossFx.CrystalShard();
            case CaveLook.Poison: return OneShotSpriteEffect.SoftDotSprite();
            case CaveLook.Magic: return k == CaveHazardKind.Floor || k == CaveHazardKind.Band || k == CaveHazardKind.Ceiling ? BossFx.Block() : BossFx.Ring();
            case CaveLook.Gaze: return BossFx.Block();
            case CaveLook.Fire: return BossFx.Orb();
            case CaveLook.Flesh: return BossFx.Orb();
            case CaveLook.Bone: return BossFx.Fang();
            default: return CaveBossFx.RockChunk();
        }
    }

    // ===================================================================== //
    // 生成(使い回し)
    // ===================================================================== //
    static CaveHazard Get(CaveHazardKind k, float sceneX, string source)
    {
        if (!warpHooked) { warpHooked = true; FloatingOrigin.Warped += _ => ClearAll(); }
        CaveHazard h = null;
        while (pool.Count > 0 && h == null) h = pool.Pop();
        if (h != null) { Reused++; h.gameObject.SetActive(true); }
        else { h = Build(); Spawned++; }
        h.ResetState(k, source);
        h.transform.position = new Vector3(sceneX, GroundAt(sceneX), 0f);
        live.Add(h);
        return h;
    }

    static CaveHazard Build()
    {
        var go = new GameObject("CaveHazard");
        var h = go.AddComponent<CaveHazard>();
        h.band = Child(go.transform, "Warn", RenderOrder.Boss - 1);
        h.body = Child(go.transform, "Body", RenderOrder.Boss + 1);
        h.shadow = Child(go.transform, "Shadow", RenderOrder.Boss - 2);
        h.pebA = Child(go.transform, "PebbleA", RenderOrder.Boss + 1);
        h.pebB = Child(go.transform, "PebbleB", RenderOrder.Boss + 1);
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        h.col = go.AddComponent<BoxCollider2D>();
        h.col.isTrigger = true;
        h.col.enabled = false;
        var dbg = go.AddComponent<ColliderDebugView>();
        dbg.color = new Color(1f, 0.1f, 0.1f);
        return h;
    }

    static SpriteRenderer Child(Transform parent, string name, int order)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        var sr = g.AddComponent<SpriteRenderer>();
        sr.sortingOrder = order;
        sr.enabled = false;
        return sr;
    }

    void ResetState(CaveHazardKind k, string source)
    {
        Serial = ++serialNext;
        kind = k; src = source; timer = 0f; landed = false; released = false; activated = false;
        follow = null; followOffset = 0f; drift = 0f; damage = CombatScale.PlayerHit; heavy = false; strong = true;
        hitsLeft = 0; lastHitTime = -9f; rot = 0f; colShrink = 0.85f; fallDur = 0.35f; slowFactor = 1f; slowDuration = 0f;
        onActivate = null; onLand = null; overReported = false;
        col.enabled = false;
        foreach (var sr in new[] { band, body, shadow, pebA, pebB }) { sr.enabled = false; sr.transform.localPosition = Vector3.zero; sr.transform.localRotation = Quaternion.identity; sr.transform.localScale = Vector3.one; }
        gameObject.name = "CaveHazard_" + k;
    }

    // マルチのHOST: 見た目の構成が決まってから登録(JOINに同じ形のミラーを作る)
    void RegisterNet()
    {
        if (!NetCombat.Authority) return;
        // 予告だけの物(当たり判定なし)は見た目だけ送る
        bool hurts = (damage > 0 || slowFactor < 1f) && kind != CaveHazardKind.Tell;
        NetAttackSync.Register(gameObject, hurts ? NetAttackSync.AType.TrackedHazard : NetAttackSync.AType.WarnBand, default, false, slowFactor, slowDuration, noDamage: !hurts || damage <= 0);
    }

    void Release()
    {
        if (released) return;
        released = true;
        live.Remove(this);
        if (NetCombat.Authority || pool.Count >= PoolMax) { Destroy(gameObject); return; }
        col.enabled = false;
        gameObject.SetActive(false);
        pool.Push(this);
    }

    public static void ClearAll()
    {
        for (int i = live.Count - 1; i >= 0; i--) { var h = live[i]; if (h == null) live.RemoveAt(i); else h.Release(); }
        CaveBossSafety.ResetAll();
    }

    static float GroundAt(float x)
    {
        var tm = TerrainManager.Instance;
        return tm != null ? (tm.GetHeightAt(x) ?? 0f) : 0f;
    }

    // 天井の高さ(無い所=闘技場等は地面+7の仮の天井)。極端に低い/高い所は丸める。
    public static float CeilingAt(float x)
    {
        float g = GroundAt(x);
        var tm = TerrainManager.Instance;
        float? c = tm != null ? tm.GetEffectiveCeilingHeightAt(x) : null;
        float v = c.HasValue ? c.Value : g + 7f;
        return Mathf.Clamp(v, g + 4.6f, g + 8.5f);
    }

    static void SetSized(SpriteRenderer sr, float w, float h)
    {
        if (sr.sprite == null) return;
        Vector2 b = sr.sprite.bounds.size;
        sr.transform.localScale = new Vector3(w / Mathf.Max(0.01f, b.x), h / Mathf.Max(0.01f, b.y), 1f);
    }

    // ===================================================================== //
    // 作り方(ボスはCaveBossBaseの補助から呼ぶ)
    // ===================================================================== //
    public static CaveHazard Floor(float sceneX, float width, float height, float warn, float active, CaveLook look, int damage, bool heavy, string source)
    {
        var h = Get(CaveHazardKind.Floor, sceneX, source);
        h.look = look; h.width = width; h.yLow = 0f; h.yHigh = height; h.warn = warn; h.active = active; h.damage = damage; h.heavy = heavy;
        h.body.sprite = LookSprite(look, CaveHazardKind.Floor); h.body.color = LookColor(look);
        h.band.sprite = BossFx.Block();
        h.shadow.sprite = CaveBossFx.RockChunk(); h.shadow.color = LookColor(CaveLook.Dirt);
        h.pebA.sprite = h.pebB.sprite = CaveBossFx.RockChunk();
        h.pebA.color = h.pebB.color = LookColor(CaveLook.Dirt);
        h.life = warn + active + 0.25f;
        h.RegisterNet();
        return h;
    }

    public static CaveHazard Ceiling(float sceneX, float width, float bottom, float warn, float active, CaveLook look, int damage, bool heavy, string source)
    {
        var h = Get(CaveHazardKind.Ceiling, sceneX, source);
        h.look = look; h.width = width; h.yLow = bottom; h.yHigh = 99f; h.warn = warn; h.active = active; h.damage = damage; h.heavy = heavy;
        h.body.sprite = LookSprite(look, CaveHazardKind.Ceiling); h.body.color = LookColor(look);
        h.band.sprite = BossFx.Block();
        h.shadow.sprite = BossFx.Block(); h.shadow.color = new Color(0.08f, 0.06f, 0.05f, 0.9f); // 天井のひび
        h.pebA.sprite = h.pebB.sprite = CaveBossFx.RockChunk();
        h.pebA.color = h.pebB.color = LookColor(CaveLook.Rock);
        h.life = warn + active + 0.25f;
        h.RegisterNet();
        return h;
    }

    public static CaveHazard Band(float sceneX, float width, float yLow, float yHigh, float warn, float active, CaveLook look, int damage, bool heavy, string source, float slow = 1f, float slowDur = 0f)
    {
        var h = Get(CaveHazardKind.Band, sceneX, source);
        h.look = look; h.width = width; h.yLow = yLow; h.yHigh = yHigh; h.warn = warn; h.active = active; h.damage = damage; h.heavy = heavy;
        h.slowFactor = slow; h.slowDuration = slowDur;
        h.body.sprite = LookSprite(look, CaveHazardKind.Band); h.body.color = LookColor(look);
        h.band.sprite = BossFx.Block();
        h.life = warn + active + 0.2f;
        h.RegisterNet();
        return h;
    }

    public static CaveHazard FallRock(float sceneX, float size, float warn, float rollSpeed, CaveLook look, int damage, bool heavy, string source)
    {
        var h = Get(CaveHazardKind.FallRock, sceneX, source);
        h.look = look; h.width = size; h.yLow = 0f; h.yHigh = size; h.warn = warn; h.active = 0f; h.damage = damage; h.heavy = heavy;
        h.drift = 0f;
        h.body.sprite = LookSprite(look, CaveHazardKind.FallRock); h.body.color = LookColor(look);
        h.band.sprite = BossFx.Block();
        h.shadow.sprite = OneShotSpriteEffect.SoftDotSprite(); h.shadow.color = new Color(0f, 0f, 0f, 0f);
        h.pebA.sprite = h.pebB.sprite = CaveBossFx.RockChunk();
        h.pebA.color = h.pebB.color = LookColor(CaveLook.Rock);
        h.followOffset = rollSpeed; // 着地後に転がる速さ(followは使わない)
        h.life = warn + 8f;
        h.RegisterNet();
        return h;
    }

    public static CaveHazard Pool(float sceneX, float width, float duration, float driftSpeed, int damage, string source)
    {
        var h = Get(CaveHazardKind.Pool, sceneX, source);
        h.look = CaveLook.Poison; h.width = width; h.yLow = 0f; h.yHigh = 0.5f; h.warn = 0.35f; h.active = duration; h.damage = damage;
        h.drift = driftSpeed;
        h.body.sprite = OneShotSpriteEffect.SoftDotSprite(); h.body.color = LookColor(CaveLook.Poison);
        h.band.sprite = BossFx.Block();
        h.pebA.sprite = h.pebB.sprite = OneShotSpriteEffect.SoftDotSprite();
        h.pebA.color = h.pebB.color = new Color(0.6f, 1f, 0.5f, 0.8f); // 泡
        h.life = h.warn + duration;
        h.RegisterNet();
        return h;
    }

    public static CaveHazard Pillar(float sceneX, float width, float height, float warn, int hits, float driftSpeed, CaveLook look, int damage, bool heavy, string source)
    {
        var h = Get(CaveHazardKind.Pillar, sceneX, source);
        h.look = look; h.width = width; h.yLow = 0f; h.yHigh = height; h.warn = warn; h.active = 9f; h.damage = damage; h.heavy = heavy;
        h.drift = driftSpeed; h.hitsLeft = Mathf.Max(1, hits);
        h.body.sprite = LookSprite(look, CaveHazardKind.Pillar); h.body.color = LookColor(look);
        h.band.sprite = BossFx.Block();
        h.life = warn + 9f;
        h.RegisterNet();
        return h;
    }

    // 横切る体/衝撃波: startSceneXから speed で「プレイヤーの方へ」(負=後ろから前へ)。見た目 length×(yHigh-yLow)、判定は shrink 倍。
    public static CaveHazard Wave(float sceneX, float length, float yLow, float yHigh, float speed, CaveLook look, int damage, bool heavy, float shrink, string source)
    {
        var h = Get(CaveHazardKind.Wave, sceneX, source);
        h.look = look; h.width = length; h.yLow = yLow; h.yHigh = yHigh; h.warn = 0f; h.active = 7f; h.damage = damage; h.heavy = heavy;
        h.drift = speed; h.colShrink = Mathf.Clamp(shrink, 0.3f, 1f);
        h.body.sprite = LookSprite(look, CaveHazardKind.Wave); h.body.color = LookColor(look);
        h.life = 7f;
        h.RegisterNet();
        return h;
    }

    public static CaveHazard Tell(float sceneX, float dur, CaveTellStyle style, CaveLook look, bool strong, Transform follow, string source)
    {
        var h = Get(CaveHazardKind.Tell, sceneX, source);
        h.look = look; h.tell = style; h.strong = strong; h.warn = dur; h.active = 0f; h.damage = 0; h.width = strong ? 2.4f : 1.7f;
        h.follow = follow; if (follow != null) h.followOffset = sceneX - follow.position.x;
        h.body.sprite = style == CaveTellStyle.Circle ? BossFx.Ring() : style == CaveTellStyle.Shadow ? OneShotSpriteEffect.SoftDotSprite() : style == CaveTellStyle.Crack ? BossFx.Block() : CaveBossFx.RockChunk();
        h.body.color = style == CaveTellStyle.Shadow ? new Color(0f, 0f, 0f, 0.5f) : style == CaveTellStyle.Crack ? new Color(0.08f, 0.06f, 0.05f, 0.9f) : LookColor(look);
        h.pebA.sprite = h.pebB.sprite = CaveBossFx.RockChunk();
        h.pebA.color = h.pebB.color = LookColor(style == CaveTellStyle.Crack ? CaveLook.Rock : CaveLook.Dirt);
        h.life = dur;
        h.RegisterNet();
        return h;
    }

    public void EndEarly() { if (!released) life = Mathf.Min(life, timer + 0.15f); }
    public void SetDrift(float speed) { drift = speed; }
    public void SetSlow(float factor, float duration) { slowFactor = factor; slowDuration = duration; }
    public void SetTint(Color c) { body.color = c; }

    // ===================================================================== //
    // 毎フレーム
    // ===================================================================== //
    void Update()
    {
        if (released) return;
        float dt = Time.deltaTime;
        timer += dt;
        Vector3 p = transform.position;
        float frame = NetTargets.FrameSpeedNear(p);
        // 石化の視線などで走りが鈍っている間も、予告した場所(プレイヤーからの距離)がずれないように(シングル)
        if (!NetTargets.IsMulti && PlayerController.Instance != null) frame *= PlayerController.Instance.MoveSlowFactor;
        if (follow != null) p.x = follow.position.x + followOffset;
        else
        {
            float d = drift;
            if (kind == CaveHazardKind.FallRock) d = landed ? followOffset : 0f;
            p.x += (frame - d) * dt;
        }
        float g = GroundAt(p.x);
        p.y = g;
        transform.position = p;

        switch (kind)
        {
            case CaveHazardKind.Floor: UpdateFloor(g); break;
            case CaveHazardKind.Ceiling: UpdateCeiling(g, CeilingAt(p.x)); break;
            case CaveHazardKind.Band: UpdateBand(); break;
            case CaveHazardKind.FallRock: UpdateFallRock(g, CeilingAt(p.x), dt); break;
            case CaveHazardKind.Pool: UpdatePool(); break;
            case CaveHazardKind.Pillar: UpdatePillar(); break;
            case CaveHazardKind.Wave: UpdateWave(dt); break;
            default: UpdateTell(g, CeilingAt(p.x)); break;
        }

        // 回避不能の検出: 床側と天井側の攻撃が同じフレームにプレイヤーの列で有効
        if (col.enabled && damage > 0) CountOverPlayer(p.x);

        if (timer >= life) { Release(); return; }
        // 置き去り(プレイヤーのずっと後ろ)
        var pc = PlayerController.Instance;
        if (pc != null && follow == null && p.x < pc.transform.position.x - 18f && timer > 0.5f) Release();
    }

    void CountOverPlayer(float x)
    {
        var pc = PlayerController.Instance;
        if (pc == null) return;
        float half = width * colShrink * 0.5f;
        if (Mathf.Abs(pc.transform.position.x - x) > half + CaveBossSafety.ColumnHalf * 0.5f) return;
        int f = Time.frameCount;
        if (!overReported) { overReported = true; OverPlayer?.Invoke(CeilingType, src); }
        if (CeilingType) { ceilOverFrame = f; lastCeilOver = this; } else if (yLow < 1.3f) { floorOverFrame = f; lastFloorOver = this; }
        if (ceilOverFrame == f && floorOverFrame == f && violationFrame != f)
        {
            violationFrame = f;
            Violations++;
            Debug.LogWarning($"[CaveSafety] floor+ceiling attack over the player in the same frame ({src}) floor={Describe(lastFloorOver)} ceiling={Describe(lastCeilOver)}");
        }
    }

    static string Describe(CaveHazard h)
    {
        if (h == null) return "-";
        var pc = PlayerController.Instance;
        float rel = pc != null ? h.transform.position.x - pc.transform.position.x : 0f;
        return $"{h.src}/{h.kind} rel={rel:F2} t={h.timer:F2} warn={h.warn:F2} act={h.active:F2} w={h.width:F1} y={h.yLow:F1}-{h.yHigh:F1}";
    }

    float WarnProgress => Mathf.Clamp01(timer / Mathf.Max(0.01f, warn));

    void ShowBand(float yA, float yB, Color tint)
    {
        float f = WarnProgress;
        float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(8f, 26f, f));
        float a = Mathf.Lerp(0.12f, 0.42f, f) * Mathf.Lerp(0.7f, 1f, blink);
        band.enabled = true;
        band.color = new Color(tint.r, tint.g, tint.b, a * 1.1f);
        band.transform.localPosition = new Vector3(0f, (yA + yB) * 0.5f, 0f);
        SetSized(band, width, Mathf.Max(0.15f, yB - yA));
    }

    void SetCollider(float yA, float yB, float shrinkX = -1f)
    {
        float s = shrinkX > 0f ? shrinkX : colShrink;
        col.offset = new Vector2(0f, (yA + yB) * 0.5f);
        col.size = new Vector2(Mathf.Max(0.2f, width * s), Mathf.Max(0.2f, (yB - yA) * 0.92f));
        if (!col.enabled) { col.enabled = true; if (!activated) { activated = true; onActivate?.Invoke(this); } }
    }

    // 小石(天井から落ちる / 地面で跳ねる)
    void Pebbles(float fromY, float toY, bool hop)
    {
        float span = width * 0.4f;
        for (int i = 0; i < 2; i++)
        {
            var sr = i == 0 ? pebA : pebB;
            sr.enabled = true;
            float ph = Mathf.Repeat(timer * (hop ? 2.4f : 1.9f) + i * 0.5f, 1f);
            float x = (i == 0 ? -1f : 1f) * span * (0.3f + 0.5f * Mathf.Repeat(i * 0.37f + Mathf.Floor(timer * 1.9f + i * 0.5f) * 0.61f, 1f));
            float y = hop ? fromY + Mathf.Sin(ph * Mathf.PI) * 0.7f : Mathf.Lerp(fromY, toY, ph * ph);
            sr.transform.localPosition = new Vector3(x, y, 0f);
            float s = strong ? 0.22f : 0.15f;
            SetSized(sr, s, s);
        }
    }

    void HidePebbles() { pebA.enabled = false; pebB.enabled = false; }

    void UpdateFloor(float g)
    {
        bool tall = yHigh > 2.0f;
        if (timer < warn)
        {
            ShowBand(0f, yHigh, WarnColorFor(false, tall));
            // 地面の盛り上がり
            float f = WarnProgress;
            shadow.enabled = true;
            shadow.transform.localPosition = new Vector3(0f, 0.05f + 0.1f * f, 0f);
            SetSized(shadow, width * 0.6f, 0.15f + 0.3f * f);
            if (f > 0.45f) Pebbles(0.1f, 0f, true); else HidePebbles();
            return;
        }
        band.enabled = false; shadow.enabled = false; HidePebbles();
        float t = timer - warn;
        if (t <= active)
        {
            float rise = Mathf.Clamp01(t / 0.1f);
            body.enabled = true;
            float h = yHigh * rise;
            body.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
            SetSized(body, width * 0.8f, Mathf.Max(0.05f, h));
            SetCollider(0f, yHigh);
        }
        else
        {
            col.enabled = false;
            float k = Mathf.Clamp01((t - active) / 0.2f);
            float h = yHigh * (1f - k);
            body.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
            SetSized(body, width * 0.8f, Mathf.Max(0.02f, h));
        }
    }

    void UpdateCeiling(float g, float c)
    {
        float top = c - g;
        float bottom = Mathf.Min(yLow, top - 0.4f);
        if (timer < warn)
        {
            ShowBand(bottom, top, WarnColorFor(true, false));
            // 天井のひび
            shadow.enabled = true;
            shadow.transform.localPosition = new Vector3(0f, top - 0.05f, 0f);
            SetSized(shadow, width * Mathf.Lerp(0.2f, 0.9f, WarnProgress), 0.12f);
            Pebbles(top, 0f, false);
            return;
        }
        band.enabled = false; HidePebbles();
        float t = timer - warn;
        if (t <= active)
        {
            float drop = Mathf.Clamp01(t / 0.12f);
            float len = (top - bottom) * drop;
            body.enabled = true;
            body.transform.localPosition = new Vector3(0f, top - len * 0.5f, 0f);
            SetSized(body, width * 0.8f, Mathf.Max(0.05f, len));
            SetCollider(top - len, top);
        }
        else
        {
            col.enabled = false;
            float k = Mathf.Clamp01((t - active) / 0.2f);
            float len = (top - bottom) * (1f - k);
            body.transform.localPosition = new Vector3(0f, top - len * 0.5f, 0f);
            SetSized(body, width * 0.8f, Mathf.Max(0.02f, len));
            shadow.enabled = k < 0.9f;
        }
    }

    void UpdateBand()
    {
        bool ceilingish = yLow >= 1.9f;
        bool tall = !ceilingish && yHigh > 2.0f;
        if (timer < warn) { ShowBand(yLow, yHigh, look == CaveLook.Gaze ? new Color(0.95f, 1f, 0.35f) : WarnColorFor(ceilingish, tall)); return; }
        band.enabled = false;
        float t = timer - warn;
        if (t <= active)
        {
            body.enabled = true;
            float k = Mathf.Clamp01(t / 0.1f);
            Color c = LookColor(look); c.a *= Mathf.Lerp(1f, 0.55f, t / Mathf.Max(0.01f, active));
            body.color = c;
            body.transform.localPosition = new Vector3(0f, (yLow + yHigh) * 0.5f, 0f);
            SetSized(body, width * Mathf.Lerp(0.4f, 1f, k), Mathf.Max(0.1f, (yHigh - yLow) * 0.85f));
            SetCollider(yLow, yHigh);
        }
        else { col.enabled = false; body.enabled = false; }
    }

    void UpdateFallRock(float g, float c, float dt)
    {
        float top = c - g;
        if (timer < warn)
        {
            float f = WarnProgress;
            // 影(地面) + 天井のひび + 落ちてくる小石
            shadow.enabled = true;
            shadow.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.15f, 0.55f, f));
            shadow.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            SetSized(shadow, width * Mathf.Lerp(0.5f, 1.3f, f), 0.32f);
            ShowBand(0f, 0.25f, heavy || yHigh > 2f ? WildBossBase.TallLaneColor : WildBossBase.LowLaneColor);
            Pebbles(top, 0f, false);
            body.enabled = f > 0.7f; // 天井の岩が見え始める
            if (body.enabled) { body.transform.localPosition = new Vector3(Mathf.Sin(timer * 60f) * 0.04f, top - yHigh * 0.5f, 0f); SetSized(body, width, yHigh); }
            return;
        }
        HidePebbles();
        band.enabled = false;
        float t = timer - warn;
        if (!landed)
        {
            float k = Mathf.Clamp01(t / fallDur);
            float y = Mathf.Lerp(top - yHigh * 0.5f, yHigh * 0.5f, k * k);
            body.enabled = true;
            body.transform.localPosition = new Vector3(0f, y, 0f);
            SetSized(body, width, yHigh);
            SetCollider(y - yHigh * 0.5f, y + yHigh * 0.5f, 0.8f);
            shadow.color = new Color(0f, 0f, 0f, 0.6f);
            if (k >= 1f)
            {
                landed = true;
                shadow.enabled = false;
                Vector3 lp = transform.position;
                OneShotSpriteEffect.CreateScatterBurst(CaveBossFx.RockChunk(), lp + new Vector3(0f, 0.2f, 0f), LookColor(look), heavy ? 10 : 6, 0.5f, 0.12f, 0.3f, 3.5f, 2f, RenderOrder.CombatFx);
                var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
                if (cf != null) cf.Shake(heavy ? 0.14f : 0.06f, 0.15f);
                onLand?.Invoke(this);
                if (followOffset <= 0.01f) life = Mathf.Min(life, timer + 0.5f); // 転がらない岩はすぐ消える
            }
            return;
        }
        // 転がる
        rot -= followOffset * dt * 90f / Mathf.Max(0.5f, width);
        body.transform.localRotation = Quaternion.Euler(0f, 0f, -rot);
        body.transform.localPosition = new Vector3(0f, yHigh * 0.5f, 0f);
        SetCollider(0.05f, yHigh * 0.92f, 0.78f);
    }

    void UpdatePool()
    {
        float f = WarnProgress;
        body.enabled = true;
        float fade = Mathf.Clamp01((life - timer) / 0.4f);
        Color c = LookColor(CaveLook.Poison); c.a *= fade * (timer < warn ? f : 1f);
        body.color = c;
        body.transform.localPosition = new Vector3(0f, 0.12f, 0f);
        SetSized(body, width * (timer < warn ? Mathf.Lerp(0.3f, 1f, f) : 1f), 0.42f);
        if (timer < warn) { col.enabled = false; return; }
        // 泡
        for (int i = 0; i < 2; i++)
        {
            var sr = i == 0 ? pebA : pebB;
            sr.enabled = true;
            float ph = Mathf.Repeat(timer * 1.3f + i * 0.5f, 1f);
            sr.color = new Color(0.65f, 1f, 0.55f, 0.85f * (1f - ph) * fade);
            sr.transform.localPosition = new Vector3((i == 0 ? -0.25f : 0.3f) * width, 0.15f + ph * 0.6f, 0f);
            SetSized(sr, 0.22f, 0.22f);
        }
        if (timer < life - 0.3f) SetCollider(0f, 0.5f, 0.78f); else col.enabled = false;
    }

    void UpdatePillar()
    {
        if (timer < warn) { ShowBand(0f, yHigh, WarnColorFor(false, yHigh > 2f)); return; }
        band.enabled = false;
        float t = timer - warn;
        float rise = Mathf.Clamp01(t / 0.18f);
        body.enabled = true;
        float h = yHigh * rise;
        body.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
        SetSized(body, width, Mathf.Max(0.05f, h));
        if (rise >= 0.5f) SetCollider(0f, yHigh, 0.8f);
        float fade = Mathf.Clamp01((life - timer) / 0.3f);
        Color c = LookColor(look); c.a *= fade;
        if (Time.time - lastHitTime < 0.12f) c = Color.Lerp(c, Color.white, 0.6f);
        body.color = c;
    }

    void UpdateWave(float dt)
    {
        body.enabled = true;
        body.transform.localPosition = new Vector3(0f, (yLow + yHigh) * 0.5f, 0f);
        SetSized(body, width, Mathf.Max(0.2f, yHigh - yLow));
        SetCollider(yLow + (yHigh - yLow) * (1f - colShrink) * 0.5f, yHigh - (yHigh - yLow) * (1f - colShrink) * 0.5f);
    }

    void UpdateTell(float g, float c)
    {
        float f = WarnProgress;
        float pulse = 0.5f + 0.5f * Mathf.Sin(timer * (strong ? 18f : 11f));
        body.enabled = true;
        switch (tell)
        {
            case CaveTellStyle.Bulge:
            {
                float h = (strong ? 0.55f : 0.35f) * (0.6f + 0.4f * pulse) * Mathf.Clamp01(f * 3f);
                body.transform.localPosition = new Vector3(Mathf.Sin(timer * 40f) * 0.03f, h * 0.4f, 0f);
                SetSized(body, width * 0.8f, Mathf.Max(0.05f, h));
                Pebbles(0.15f, 0f, true);
                break;
            }
            case CaveTellStyle.Shadow:
                body.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.15f, strong ? 0.6f : 0.4f, f));
                body.transform.localPosition = new Vector3(0f, 0.06f, 0f);
                SetSized(body, width * Mathf.Lerp(0.5f, 1.2f, f), 0.3f);
                break;
            case CaveTellStyle.Crack:
                body.transform.localPosition = new Vector3(0f, c - g - 0.05f, 0f);
                SetSized(body, width * Mathf.Lerp(0.2f, 1f, f), 0.12f);
                Pebbles(c - g, 0f, false);
                break;
            default:
            {
                Color cc = LookColor(look); cc.a = Mathf.Lerp(0.3f, strong ? 0.95f : 0.6f, f) * (0.7f + 0.3f * pulse);
                body.color = cc;
                body.transform.localPosition = new Vector3(0f, 0.1f, 0f);
                SetSized(body, width * Mathf.Lerp(0.6f, 1.1f, f), 0.45f);
                break;
            }
        }
    }

    // ===================================================================== //
    // 当たり
    // ===================================================================== //
    void OnTriggerEnter2D(Collider2D other) { Touch(other); }
    void OnTriggerStay2D(Collider2D other) { if (kind == CaveHazardKind.Pool) Touch(other); }

    void Touch(Collider2D other)
    {
        if (released || !col.enabled) return;
        if (kind == CaveHazardKind.Pillar && other.CompareTag("PlayerAttack"))
        {
            if (Time.time - lastHitTime < 0.15f) return;
            lastHitTime = Time.time;
            hitsLeft--;
            if (hitsLeft <= 0)
            {
                OneShotSpriteEffect.CreateScatterBurst(CaveBossFx.CrystalShard(), transform.position + new Vector3(0f, yHigh * 0.5f, 0f), LookColor(look), 9, 0.5f, 0.15f, 0.35f, 4f, 1.6f, RenderOrder.CombatFx);
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossAttack);
                Release();
            }
            return;
        }
        if ((damage <= 0 && slowFactor >= 1f) || !other.CompareTag("Player") || PlayerController.Instance == null) return;
        if (kind == CaveHazardKind.Pool && Time.time - lastHitTime < 0.7f) return;
        lastHitTime = Time.time;
        if (slowFactor < 1f) PlayerController.Instance.ApplyMoveSlow(slowFactor, slowDuration); // 石化の視線: 短く鈍るだけ
        if (damage <= 0) return;
        PlayerHits++;
        PlayerController.Instance.TakeDamage(source: "CaveBoss:" + src, amount: BossManager.ScaleDamage(damage));
    }
}

// 自然洞窟ボス強化(2026-10-04): ECHO HUNT の「少し暗くなる」。真っ暗にはしない(最大0.4)。
// ボス/プレイヤー/攻撃/予告より後ろ(背景と地面だけ)を暗くする。必殺技が終わる(倒された/割り込まれた)と自動で明るく戻る。
public class CaveDarkness : MonoBehaviour
{
    static CaveDarkness inst;
    static float target;
    float current;
    SpriteRenderer sr;
    public static float Current => inst != null ? inst.current : 0f;

    public static void Set(float a)
    {
        target = Mathf.Clamp(a, 0f, 0.4f);
        if (inst == null && target > 0f)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var go = new GameObject("CaveDarkness");
            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 10.5f); // 同じ描画順の物より奥
            inst = go.AddComponent<CaveDarkness>();
            inst.sr = go.AddComponent<SpriteRenderer>();
            inst.sr.sprite = BossFx.Block();
            inst.sr.sortingOrder = RenderOrder.EnvironmentFx;
            inst.sr.color = new Color(0f, 0f, 0.02f, 0f);
        }
    }

    void Update()
    {
        if (!BossBattle.UltimateActive) target = 0f;
        current = Mathf.MoveTowards(current, target, Time.deltaTime * 1.2f);
        var cam = Camera.main;
        if (cam != null) transform.localScale = new Vector3(cam.orthographicSize * 2f * cam.aspect + 6f, cam.orthographicSize * 2f + 6f, 1f);
        sr.color = new Color(0f, 0f, 0.02f, current);
        sr.enabled = current > 0.001f;
    }
}
