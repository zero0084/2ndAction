using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 障害物の被弾/破壊の演出(2026-09-29)。
// ・被弾: 小さな欠片(木=木片、岩=石片)。揺れはObstacleController。
// ・耐久力の低下: ひび(2段階)。障害物の絵の形に切り抜いて重ねる(SpriteMask)。
// ・破壊: 障害物の絵そのものを格子状に切り分けた破片が飛び散る(木=細かく軽く弾ける/岩=亀裂が走ってから砕ける/
//         大型=大きな塊が重く崩れ落ち、土煙と小さな画面の揺れ)。
// 破片は当たり判定を持たない装飾。数は上限付きで使い回す(高速で連続して壊しても画面を覆わない/処理落ちしない)。
public class ObstacleFx : MonoBehaviour
{
    static ObstacleFx instance;
    static ObstacleFx Runner()
    {
        if (instance == null) { var go = new GameObject("[ObstacleFx]"); instance = go.AddComponent<ObstacleFx>(); }
        return instance;
    }

    class Piece { public GameObject go; public SpriteRenderer sr; public Vector2 v; public float av, life, t, gravity, scale0; public bool dust; }
    readonly List<Piece> pool = new List<Piece>();
    int nextReuse;
    public static int BreakFxCount; // 破壊の演出を出した回数(1つの障害物で1回だけのはず)
    public static int ActivePieces { get { if (instance == null) return 0; int n = 0; foreach (var p in instance.pool) if (p.go.activeSelf) n++; return n; } }
    public static int PeakPieces { get; private set; }

    Piece Acquire()
    {
        int max = Mathf.Max(8, ObstacleBalance.Get().maxDebris);
        foreach (var p in pool) if (!p.go.activeSelf) { p.go.SetActive(true); return p; }
        if (pool.Count < max)
        {
            var go = new GameObject("Debris");
            go.transform.SetParent(transform, false);
            var p = new Piece { go = go, sr = go.AddComponent<SpriteRenderer>() };
            pool.Add(p);
            return p;
        }
        var r = pool[nextReuse % pool.Count]; nextReuse++;   // 上限: 古い物から使い回す
        return r;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        int active = 0;
        foreach (var p in pool)
        {
            if (!p.go.activeSelf) continue;
            active++;
            p.t += dt;
            if (p.t >= p.life) { p.go.SetActive(false); continue; }
            p.v.y -= p.gravity * dt;
            var tr = p.go.transform;
            tr.position += (Vector3)(p.v * dt);
            tr.Rotate(0f, 0f, p.av * dt);
            float k = p.t / p.life;
            var c = p.sr.color; c.a = k < 0.6f ? c.a : Mathf.Lerp(1f, 0f, (k - 0.6f) / 0.4f) * (p.dust ? 0.55f : 1f);
            if (p.dust) { float s = p.scale0 * (1f + k * 1.6f); tr.localScale = new Vector3(s, s, 1f); }
            p.sr.color = c;
        }
        if (active > PeakPieces) PeakPieces = active;
    }

    // ===================================================================== //
    // 素材(障害物の絵を格子状に切った破片 / 木片 / ひび)
    // ===================================================================== //
    static readonly Dictionary<Sprite, Sprite[]> chunkCache = new Dictionary<Sprite, Sprite[]>();
    static Sprite[] Chunks(Sprite s, int grid)
    {
        if (s == null) return null;
        if (chunkCache.TryGetValue(s, out var arr) && arr.Length == grid * grid) return arr;
        arr = new Sprite[grid * grid];
        Rect r = s.rect;
        float cw = r.width / grid, ch = r.height / grid;
        for (int y = 0; y < grid; y++)
            for (int x = 0; x < grid; x++)
                arr[y * grid + x] = Sprite.Create(s.texture, new Rect(r.x + x * cw, r.y + y * ch, cw, ch), new Vector2(0.5f, 0.5f), s.pixelsPerUnit);
        chunkCache[s] = arr;
        return arr;
    }

    static Sprite splinter, crack1, crack2;
    static Sprite Splinter()
    {
        if (splinter != null) return splinter;
        var t = new Texture2D(16, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        for (int y = 0; y < 4; y++) for (int x = 0; x < 16; x++) t.SetPixel(x, y, new Color(1f, 1f, 1f, (y == 0 || y == 3) ? 0.5f : 1f) * (x < 2 || x > 13 ? 0.6f : 1f));
        t.Apply();
        return splinter = Sprite.Create(t, new Rect(0, 0, 16, 4), new Vector2(0.5f, 0.5f), 40f);
    }

    // ひびの模様(中心から枝分かれする暗い線)。段階2は線を増やす。
    static Sprite Crack(int stage)
    {
        if (stage == 1 && crack1 != null) return crack1;
        if (stage >= 2 && crack2 != null) return crack2;
        const int n = 128;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        var rnd = new System.Random(stage == 1 ? 11 : 23);
        void Line(float x0, float y0, float ang, float len, int depth)
        {
            float x = x0, y = y0;
            for (int i = 0; i < len; i++)
            {
                ang += (float)(rnd.NextDouble() - 0.5) * 0.5f;
                x += Mathf.Cos(ang); y += Mathf.Sin(ang);
                int ix = (int)x, iy = (int)y;
                for (int oy = -1; oy <= 1; oy++) for (int ox = -1; ox <= 1; ox++)
                {
                    int px_ = ix + ox, py_ = iy + oy;
                    if (px_ < 0 || py_ < 0 || px_ >= n || py_ >= n) continue;
                    float a = (ox == 0 && oy == 0) ? 0.95f : 0.45f;
                    int k = py_ * n + px_;
                    px[k] = new Color(0.08f, 0.06f, 0.05f, Mathf.Max(px[k].a, a));
                }
                if (depth < 2 && rnd.NextDouble() < 0.05) Line(x, y, ang + (rnd.NextDouble() < 0.5 ? 0.9f : -0.9f), len * 0.5f, depth + 1);
            }
        }
        int arms = stage == 1 ? 3 : 6;
        float cx = n * 0.5f + (float)(rnd.NextDouble() - 0.5) * 20f, cy = n * 0.55f;
        for (int i = 0; i < arms; i++) Line(cx, cy, i * Mathf.PI * 2f / arms + (float)rnd.NextDouble(), stage == 1 ? 34 : 52, 0);
        t.SetPixels(px); t.Apply();
        var s = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        if (stage == 1) crack1 = s; else crack2 = s;
        return s;
    }

    // ===================================================================== //
    // 被弾 / ひび / 破壊
    // ===================================================================== //
    public static void HitChips(ObstacleController o, Vector3 at)
    {
        var r = Runner();
        int n = o.material == ObstacleMaterial.Heavy ? 3 : 2;
        for (int i = 0; i < n; i++) r.Spawn(o, at, small: true);
    }

    public static void ShowCracks(ObstacleController o, int stage)
    {
        var vis = o.Visual;
        if (vis == null || vis.sprite == null) return;
        Transform existing = vis.transform.Find("Cracks");
        SpriteRenderer sr;
        if (existing == null)
        {
            var maskGo = new GameObject("CrackMask");
            maskGo.transform.SetParent(vis.transform, false);
            var mask = maskGo.AddComponent<SpriteMask>();
            mask.sprite = vis.sprite;
            // この障害物のひびだけに効かせる(他の障害物/スプライトに影響しない)
            mask.isCustomRangeActive = true;
            mask.frontSortingLayerID = mask.backSortingLayerID = vis.sortingLayerID;
            mask.backSortingOrder = vis.sortingOrder;
            mask.frontSortingOrder = vis.sortingOrder + 1;
            var go = new GameObject("Cracks");
            go.transform.SetParent(vis.transform, false);
            sr = go.AddComponent<SpriteRenderer>();
            sr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            sr.sortingLayerID = vis.sortingLayerID;
            sr.sortingOrder = vis.sortingOrder + 1;
            // 絵の範囲に合わせる(スプライトの中心へ)
            Bounds b = vis.sprite.bounds;
            go.transform.localPosition = b.center;
            go.transform.localScale = new Vector3(b.size.x, b.size.y, 1f);
        }
        else sr = existing.GetComponent<SpriteRenderer>();
        sr.sprite = Crack(stage);
        sr.color = o.material == ObstacleMaterial.Wood ? new Color(1f, 1f, 1f, 0.75f) : Color.white;
    }

    public static void Break(ObstacleController o, Vector3 at)
    {
        BreakFxCount++;
        Runner().StartCoroutine(Runner().BreakRoutine(o, at));
    }

    IEnumerator BreakRoutine(ObstacleController o, Vector3 at)
    {
        var vis = o.Visual;
        // 岩/大型: 亀裂が走ってから砕ける(当たり判定はもう無い)
        float preCrack = o.material == ObstacleMaterial.Wood ? 0f : o.material == ObstacleMaterial.Heavy ? 0.07f : 0.04f; // 高速で走り抜けても「すり抜けて見える」時間が長くならない長さ
        if (preCrack > 0f && vis != null)
        {
            ShowCracks(o, 2);
            var c0 = vis.color;
            float t = 0f;
            while (t < preCrack && vis != null)
            {
                t += Time.deltaTime;
                vis.color = Color.Lerp(c0, Color.white, 0.35f * Mathf.Sin(t / preCrack * Mathf.PI));
                vis.transform.localPosition += new Vector3(Random.Range(-0.03f, 0.03f), 0f, 0f);
                yield return null;
            }
            if (vis != null) vis.color = c0;
        }
        Shatter(o, at);
        if (o.material == ObstacleMaterial.Heavy && Camera.main != null) { var cf = Camera.main.GetComponent<CameraFollow>(); if (cf != null) cf.Shake(ObstacleBalance.Get().heavyBreakCameraShake, 0.25f); }
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(o.material == ObstacleMaterial.Wood ? SeId.EnemyDefeat : SeId.StrongHit, o.material == ObstacleMaterial.Heavy ? 1.2f : 0.9f);
        if (o != null) o.FinishBreak();
    }

    void Shatter(ObstacleController o, Vector3 at)
    {
        var vis = o.Visual;
        if (vis == null || vis.sprite == null) { for (int i = 0; i < 6; i++) Spawn(o, at, small: false); return; }
        int grid = o.material == ObstacleMaterial.Heavy ? 4 : 3;
        var chunks = Chunks(vis.sprite, grid);
        Sprite s = vis.sprite;
        Vector2 pivotPx = s.pivot;
        float ppu = s.pixelsPerUnit;
        Vector3 center = vis.bounds.center;
        float heavy = o.material == ObstacleMaterial.Heavy ? 1f : 0f, wood = o.material == ObstacleMaterial.Wood ? 1f : 0f;
        float inherit = Mathf.Max(0f, PlayerController.RunFrameSpeed) * 0.45f;
        for (int y = 0; y < grid; y++)
            for (int x = 0; x < grid; x++)
            {
                var p = Acquire();
                p.dust = false;
                p.sr.sprite = chunks[y * grid + x];
                p.sr.color = vis.color;
                p.sr.sortingLayerID = vis.sortingLayerID; p.sr.sortingOrder = vis.sortingOrder + 2;
                Vector2 cellCenterPx = new Vector2((x + 0.5f) * s.rect.width / grid, (y + 0.5f) * s.rect.height / grid);
                Vector3 local = (Vector3)((cellCenterPx - pivotPx) / ppu);
                var tr = p.go.transform;
                tr.position = vis.transform.TransformPoint(local);
                tr.rotation = vis.transform.rotation;
                tr.localScale = vis.transform.lossyScale * (1f - 0.05f * heavy);
                Vector2 dir = ((Vector2)(tr.position - center)).normalized + new Vector2(0.35f, 0.25f);
                float speed = Mathf.Lerp(4.5f, 3.5f, heavy) * (1f + 0.5f * wood) * Random.Range(0.7f, 1.2f);
                // 打撃の勢い: 走ってきた方向へ走行速度の一部で弾き飛ばす(高速では、その場に残ると元の形のまま後ろへ流れて「壊れた」ように見えない)
                p.v = dir * speed + Vector2.up * Mathf.Lerp(3.0f, 2.2f, heavy) + new Vector2(inherit * Random.Range(0.6f, 1.0f), 0f);
                p.av = Random.Range(-420f, 420f) * (1f - 0.4f * heavy);
                p.gravity = Mathf.Lerp(14f, 20f, heavy);
                p.life = ObstacleBalance.Get().debrisLife * Mathf.Lerp(0.85f, 1.25f, heavy);
                p.t = 0f;
            }
        // 木片 / 土煙
        int extra = wood > 0f ? 6 : heavy > 0f ? 5 : 3;
        for (int i = 0; i < extra; i++)
        {
            if (wood > 0f) Spawn(o, center, small: false, splinterOnly: true);
            else SpawnDust(o, center + new Vector3(Random.Range(-0.5f, 0.5f) * vis.bounds.size.x * 0.5f, -vis.bounds.size.y * 0.3f, 0f), 0.6f + heavy * 0.6f);
        }
    }

    // 小さな欠片(被弾)/木片
    void Spawn(ObstacleController o, Vector3 at, bool small, bool splinterOnly = false)
    {
        var p = Acquire();
        p.dust = false;
        var vis = o.Visual;
        bool wood = o.material == ObstacleMaterial.Wood;
        if (wood || splinterOnly || vis == null || vis.sprite == null)
        {
            p.sr.sprite = Splinter();
            p.sr.color = vis != null ? Color.Lerp(vis.color, new Color(0.55f, 0.36f, 0.2f), 0.7f) : new Color(0.55f, 0.36f, 0.2f);
            p.go.transform.localScale = Vector3.one * (small ? 0.5f : 0.8f) * Random.Range(0.8f, 1.2f);
        }
        else
        {
            var chunks = Chunks(vis.sprite, 4);
            p.sr.sprite = chunks[Random.Range(0, chunks.Length)];
            p.sr.color = vis.color;
            p.go.transform.localScale = vis.transform.lossyScale * (small ? 0.35f : 0.6f);
        }
        if (vis != null) { p.sr.sortingLayerID = vis.sortingLayerID; p.sr.sortingOrder = vis.sortingOrder + 2; }
        p.go.transform.position = at;
        p.go.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        p.v = new Vector2(Random.Range(0.5f, 3.2f), Random.Range(1.5f, 4.2f)) * (small ? 0.8f : 1.2f) + new Vector2(Mathf.Max(0f, PlayerController.RunFrameSpeed) * (small ? 0.25f : 0.45f), 0f);
        p.av = Random.Range(-600f, 600f);
        p.gravity = 16f;
        p.life = ObstacleBalance.Get().debrisLife * (small ? 0.6f : 0.9f);
        p.t = 0f;
    }

    void SpawnDust(ObstacleController o, Vector3 at, float size)
    {
        var p = Acquire();
        p.dust = true;
        p.sr.sprite = OneShotSpriteEffect.SoftDotSprite();
        var vis = o.Visual;
        Color c = vis != null ? Color.Lerp(vis.color, new Color(0.62f, 0.56f, 0.48f), 0.6f) : new Color(0.62f, 0.56f, 0.48f);
        c.a = 0.6f;
        p.sr.color = c;
        if (vis != null) { p.sr.sortingLayerID = vis.sortingLayerID; p.sr.sortingOrder = vis.sortingOrder + 3; }
        p.go.transform.position = at;
        p.go.transform.rotation = Quaternion.identity;
        p.scale0 = size * Random.Range(0.8f, 1.3f);
        p.go.transform.localScale = Vector3.one * p.scale0;
        p.v = new Vector2(Random.Range(-0.6f, 1.2f), Random.Range(0.3f, 1.0f));
        p.av = 0f; p.gravity = 0f;
        p.life = ObstacleBalance.Get().debrisLife * 1.2f;
        p.t = 0f;
    }
}
