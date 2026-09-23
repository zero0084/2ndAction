using System.Collections;
using UnityEngine;

// 荒野街道ボス追加(2026-09-20) - 新ボス群(WildBossBase系)が共通で使う小部品。
// 専用のVFX画像は用意せず、噛みつき/斬撃/衝撃波などは実行時に生成する
// 手続き的スプライト(BossFx)で表現する - 「まず遊べる形」優先。

// ---- 手続き的スプライト(1unit=128px、生成は初回のみ) ----
public static class BossFx
{
    static Sprite fang, slash, ring, block, orb;

    // 攻撃エフェクト本番素材化(2026-09-23) - Fang/Slash/Ring/Orbは白色基調の
    // ChatGPT生成イラスト(Assets/Resources/Effects/<name>.png、SceneBuilder
    // が編集時にSprite/PPU=512へ設定)があればそれを使い、無ければ従来の
    // 手続き的シルエットへフォールバックする(caveArt[]/wildArt[]と同じ
    // 安全策)。呼び出し側は各攻撃ごとに任意の色でSpriteRenderer.colorを
    // 乗算するため(例: 氷attackは青、溶岩attackは橙)、素材は白基調のまま
    // にしてある - 色を焼き込んだ絵にすると乗算時に濁るため。Block()は
    // 警告ゾーン/岩などの単色矩形として機能上ずっと使われる(参照比較で
    // 判定している箇所もある、BossCombatParts.cs BossProjectile参照)ため
    // 対象外。
    static Sprite LoadEffectArt(string name)
    {
        return Resources.Load<Sprite>("Effects/" + name);
    }

    static Sprite Make(int size, System.Func<float, float, float> alphaAt)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float a = Mathf.Clamp01(alphaAt((x + 0.5f) / size, (y + 0.5f) / size));
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    // 上下2列の牙(口を閉じる瞬間の形)。u,v=0..1。
    public static Sprite Fang()
    {
        if (fang != null) return fang;
        fang = LoadEffectArt("fang");
        if (fang != null) return fang;
        fang = Make(128, (u, v) =>
        {
            const int teeth = 4;
            float cell = u * teeth;
            float tri = 1f - Mathf.Abs(cell - Mathf.Floor(cell) - 0.5f) * 2f; // 0..1..0
            float upper = (v > 0.5f) ? (v - 0.5f) * 2f : 0f;   // 上側は下向きの牙
            float lower = (v < 0.5f) ? (0.5f - v) * 2f : 0f;   // 下側は上向きの牙
            bool inUpper = v > 0.5f && (1f - upper) < tri * 0.95f;
            bool inLower = v < 0.5f && (1f - lower) < tri * 0.95f;
            // 上顎・下顎の根元の帯
            bool bandUpper = v > 0.86f;
            bool bandLower = v < 0.14f;
            return (inUpper || inLower || bandUpper || bandLower) ? 1f : 0f;
        });
        return fang;
    }

    // 右向きの三日月斬撃。
    public static Sprite Slash()
    {
        if (slash != null) return slash;
        slash = LoadEffectArt("slash");
        if (slash != null) return slash;
        slash = Make(128, (u, v) =>
        {
            float dx = u - 0.15f, dy = v - 0.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float outer = 0.85f, inner = 0.62f;
            if (dx < 0f) return 0f;
            float band = Mathf.InverseLerp(inner, outer, r);
            if (band <= 0f || band >= 1f) return 0f;
            float fade = Mathf.Sin(band * Mathf.PI);
            float tail = 1f - Mathf.Abs(dy) * 1.3f;
            return fade * Mathf.Clamp01(tail * 1.6f);
        });
        return slash;
    }

    // 地面の衝撃波リング(横長に潰して使う)。
    public static Sprite Ring()
    {
        if (ring != null) return ring;
        ring = LoadEffectArt("ring");
        if (ring != null) return ring;
        ring = Make(128, (u, v) =>
        {
            float dx = (u - 0.5f) * 2f, dy = (v - 0.5f) * 2f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Clamp01(1f - Mathf.Abs(r - 0.82f) / 0.14f);
        });
        return ring;
    }

    // 単色ブロック(警告ゾーン・柱・岩など)。
    public static Sprite Block()
    {
        if (block != null) return block;
        block = Make(8, (u, v) => 1f);
        return block;
    }

    public static Sprite Orb()
    {
        if (orb != null) return orb;
        orb = LoadEffectArt("orb");
        if (orb != null) return orb;
        orb = Make(64, (u, v) =>
        {
            float dx = (u - 0.5f) * 2f, dy = (v - 0.5f) * 2f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Clamp01((1f - r) * 2.2f);
        });
        return orb;
    }
}

// ---- ボス攻撃判定(見えている攻撃VFXと同じ範囲だけを判定) ----
// 独立のkinematic Rigidbody2Dを持つ - 親(ボス)のRigidbody2D複合コライダー
// 扱いにならず、プレイヤー攻撃との重なりがボス本体の被弾扱いにならない。
public class BossHitbox : MonoBehaviour
{
    public bool damagesPlayer = true;
    public System.Action<PlayerController> onHitPlayer; // 追加効果(スロー等)

    BoxCollider2D col;
    SpriteRenderer vfx;
    Vector2 relCenter;
    Vector2 size;
    Transform followParent;
    float vfxBaseAlpha = 0.85f;
    Coroutine running;
    bool hitThisActivation;

    public bool IsActive => col != null && col.enabled;

    public static BossHitbox Create(Transform parent, Sprite vfxSprite, Color vfxColor, string name, int sortingOrder)
    {
        GameObject go = new GameObject("Hitbox_" + name);
        go.transform.SetParent(parent, false);

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        BossHitbox hb = go.AddComponent<BossHitbox>();
        hb.followParent = parent;
        hb.col = go.AddComponent<BoxCollider2D>();
        hb.col.isTrigger = true;
        hb.col.size = Vector2.one;
        hb.col.enabled = false;

        var dbg = go.AddComponent<ColliderDebugView>();
        dbg.color = new Color(1f, 0.1f, 0.1f);

        GameObject v = new GameObject("Vfx");
        v.transform.SetParent(go.transform, false);
        hb.vfx = v.AddComponent<SpriteRenderer>();
        hb.vfx.sprite = vfxSprite != null ? vfxSprite : BossFx.Block();
        hb.vfx.color = vfxColor;
        hb.vfx.sortingOrder = sortingOrder;
        hb.vfx.enabled = false;
        hb.vfxBaseAlpha = vfxColor.a;
        return hb;
    }

    // relCenter/sizeはボスの「正面が+x」座標系(facing=-1なら反転して配置)。
    public void Configure(Vector2 relCenter, Vector2 size)
    {
        this.relCenter = relCenter;
        this.size = size;
    }

    // duration秒だけ判定+VFXを有効化(呼び出し側がyield returnして待つ)。
    public IEnumerator Strike(float facing, float duration, bool vfxFlipsWithFacing = true)
    {
        Place(facing);
        hitThisActivation = false;
        col.enabled = true;
        vfx.enabled = true;
        vfx.transform.localScale = new Vector3(vfxFlipsWithFacing ? Mathf.Sign(facing) * size.x : size.x, size.y, 1f);

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / Mathf.Max(0.01f, duration));
            Color c = vfx.color; c.a = vfxBaseAlpha * Mathf.Lerp(1f, 0.25f, f * f);
            vfx.color = c;
            // 一瞬だけ大きく見せる(叩きつけ感) - 判定サイズは変えない
            float punch = 1f + 0.18f * (1f - f);
            vfx.transform.localScale = new Vector3((vfxFlipsWithFacing ? Mathf.Sign(facing) : 1f) * size.x * punch, size.y * punch, 1f);
            yield return null;
        }
        Deactivate();
    }

    public void Place(float facing)
    {
        transform.localPosition = new Vector3(relCenter.x * Mathf.Sign(facing), relCenter.y, 0f);
        col.size = new Vector2(size.x, size.y);
        vfx.transform.localPosition = Vector3.zero;
    }

    public void Deactivate()
    {
        if (col != null) col.enabled = false;
        if (vfx != null) vfx.enabled = false;
    }

    void OnDisable()
    {
        Deactivate();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!damagesPlayer || !col.enabled) return;
        if (!other.CompareTag("Player") || PlayerController.Instance == null) return;
        if (hitThisActivation) return; // 1回の攻撃判定でダメージは最大1回
        hitThisActivation = true;
        PlayerController.Instance.TakeDamage(source: "BossCombatPart:" + name);
        onHitPlayer?.Invoke(PlayerController.Instance);
    }
}

// ---- 攻撃予告ゾーン(赤い半透明の矩形、判定と同じ範囲に出す) ----
public class BossTelegraphMarker : MonoBehaviour
{
    SpriteRenderer sr;
    Vector2 relCenter, size;
    public static BossTelegraphMarker Create(Transform parent, int sortingOrder)
    {
        GameObject go = new GameObject("TelegraphMarker");
        go.transform.SetParent(parent, false);
        var m = go.AddComponent<BossTelegraphMarker>();
        m.sr = go.AddComponent<SpriteRenderer>();
        m.sr.sprite = BossFx.Block();
        m.sr.sortingOrder = sortingOrder;
        m.sr.color = new Color(1f, 0.12f, 0.08f, 0f);
        m.sr.enabled = false;
        return m;
    }

    public void Configure(Vector2 relCenter, Vector2 size)
    {
        this.relCenter = relCenter;
        this.size = size;
    }

    public void Show(float facing)
    {
        transform.localPosition = new Vector3(relCenter.x * Mathf.Sign(facing), relCenter.y, 0f);
        // Block()は8x8px/8ppu=1unit
        transform.localScale = new Vector3(size.x, size.y, 1f);
        sr.enabled = true;
    }

    // progress 0..1 - 進むほど濃く、点滅が速くなる。
    public void SetProgress(float p)
    {
        if (!sr.enabled) return;
        float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(8f, 26f, p));
        float a = Mathf.Lerp(0.10f, 0.42f, p) * Mathf.Lerp(0.7f, 1f, blink);
        sr.color = new Color(1f, Mathf.Lerp(0.5f, 0.1f, p), 0.08f, a);
    }

    public void Hide()
    {
        if (sr != null) sr.enabled = false;
    }
}

// ---- 直進する攻撃弾/衝撃波(地面沿い・スロー付与にも対応) ----
public class BossProjectile : MonoBehaviour
{
    public Vector2 velocity;
    public float lifetime = 3f;
    public bool hugGround;      // trueなら地形の高さに沿って進む(衝撃波)
    public float groundOffset;
    public float slowFactor = 1f;   // <1でヒット時にプレイヤーを減速
    public float slowDuration;
    public bool damage = true;
    float age;

    public static BossProjectile Create(Sprite sprite, Color color, Vector3 pos, Vector2 size, Vector2 velocity, float lifetime, int sortingOrder)
    {
        GameObject go = new GameObject("BossProjectile");
        go.transform.position = pos;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = sortingOrder;
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = Vector2.one * (sprite == BossFx.Block() ? 1f : 0.8f);
        var dbg = go.AddComponent<ColliderDebugView>();
        dbg.color = new Color(1f, 0.1f, 0.1f);
        var p = go.AddComponent<BossProjectile>();
        p.velocity = velocity;
        p.lifetime = lifetime;
        return p;
    }

    void Update()
    {
        transform.position += (Vector3)(velocity * Time.deltaTime);
        if (hugGround && TerrainManager.Instance != null)
        {
            float? h = TerrainManager.Instance.GetHeightAt(transform.position.x);
            if (h.HasValue)
            {
                Vector3 p = transform.position;
                p.y = h.Value + groundOffset;
                transform.position = p;
            }
        }
        age += Time.deltaTime;
        if (age > lifetime) Destroy(gameObject);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player") || PlayerController.Instance == null) return;
        if (damage) PlayerController.Instance.TakeDamage(source: "BossProjectile:" + name);
        if (slowFactor < 1f) PlayerController.Instance.ApplyMoveSlow(slowFactor, slowDuration);
        Destroy(gameObject);
    }
}
