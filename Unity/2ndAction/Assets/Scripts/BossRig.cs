using System.Collections.Generic;
using UnityEngine;

// ボスの移動アニメーション(2026-09-22) - 1枚絵(idle/move/windup/attack)の素材を格子状のセルに
// 分割して、走行(脚のスイング/上下動)・這い(うねり)・羽ばたき・ローブのなびきを
// 「実際に高速移動している」と分かる形で見せる。素材は増やさずに既存画像を流用する。
// セルは元画像の矩形をそのまま切り出したSpriteで、位置のオフセットだけで変形する
// (メッシュ生成なし、Collider無し=見た目専用)。
public enum LocoStyle
{
    None,
    Gallop,   // 四足/騎乗の疾走(脚を前後に振る+跳ねる): オオカミ、ウルフライダー
    Run,      // 二足の走り: 黒騎士
    Stride,   // 重い大股/歩行(足音で揺れる): サイクロプス、ゴーレム
    Crawl,    // 多脚の高速這い: 蜘蛛
    Slither,  // 身体をくねらせる: 蛇、ヒュドラ
    Wing,     // 羽ばたき(低空飛行): グリフォン、デーモン、ドラゴン
    Cloth,    // ローブ/布のなびき(高速浮遊): 死神
}

public class BossRig
{
    readonly Transform parent;
    readonly int cols, rows, order;
    readonly List<SpriteRenderer> cells = new List<SpriteRenderer>();
    readonly List<Vector2> baseUv = new List<Vector2>();
    // 2026-10-08(メモリの漏れの修正): 切り分けた絵は全員で共有する(同じ絵・同じ格子なら1回だけ作る)。
    // 以前はボスを出すたびに自分用に作り直し、ボスが消えても切り分けた Sprite は残り続けていた(長いランで数千枚)
    static readonly Dictionary<(Sprite, int, int), Sprite[]> sharedCache = new Dictionary<(Sprite, int, int), Sprite[]>();
    static readonly Dictionary<(Sprite, int, int), Vector2[]> sharedPos = new Dictionary<(Sprite, int, int), Vector2[]>();
    bool TryPos(Sprite s, out Vector2[] pos) { pos = null; return s != null && sharedPos.TryGetValue((s, cols, rows), out pos); }
    Sprite current;
    Vector2 spriteSize = Vector2.one;
    float amount;

    public float Phase;

    public BossRig(Transform parent, int cols, int rows, int sortingOrder)
    {
        this.parent = parent; this.cols = cols; this.rows = rows; order = sortingOrder; SortingOrder = sortingOrder;
        for (int j = 0; j < rows; j++)
        {
            for (int i = 0; i < cols; i++)
            {
                var go = new GameObject("Cell_" + i + "_" + j);
                go.transform.SetParent(parent, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = order;
                cells.Add(sr);
                baseUv.Add(new Vector2((i + 0.5f) / cols, (j + 0.5f) / rows));
            }
        }
    }

    public void SetSprite(Sprite s)
    {
        if (s == null || s == current) return;
        current = s;
        spriteSize = s.bounds.size;
        if (!sharedCache.TryGetValue((s, cols, rows), out Sprite[] set))
        {
            set = new Sprite[cols * rows];
            var pos = new Vector2[cols * rows];
            Rect r = s.textureRect;
            float ppu = s.pixelsPerUnit;
            Vector2 pv = s.pivot;
            float cw = r.width / cols, ch = r.height / rows;
            for (int j = 0; j < rows; j++)
            {
                for (int i = 0; i < cols; i++)
                {
                    // 隣のセルと1pxだけ重ねて継ぎ目(隙間)を隠す
                    float x0 = r.x + i * cw, y0 = r.y + j * ch;
                    float w = Mathf.Min(cw + 1f, r.xMax - x0), h = Mathf.Min(ch + 1f, r.yMax - y0);
                    var sub = new Rect(Mathf.Floor(x0), Mathf.Floor(y0), Mathf.Ceil(w), Mathf.Ceil(h));
                    int k = j * cols + i;
                    set[k] = Sprite.Create(s.texture, sub, Vector2.zero, ppu, 0, SpriteMeshType.FullRect);
                    pos[k] = new Vector2((sub.x - r.x - pv.x) / ppu, (sub.y - r.y - pv.y) / ppu);
                }
            }
            sharedCache[(s, cols, rows)] = set; sharedPos[(s, cols, rows)] = pos;
        }
        for (int k = 0; k < cells.Count; k++) cells[k].sprite = set[k];
    }

    public void SetColor(Color c)
    {
        for (int k = 0; k < cells.Count; k++) cells[k].color = c;
    }

    // BOSS FINISH(2026-10-06): 撃破の見た目でセルを1つずつ動かす(崩れる/列ごとに沈む等)。基準の位置は今の絵の格子の位置
    public int Cols => cols;
    public int Rows => rows;
    public int CellCount => cells.Count;
    public Vector2 CellUv(int k) => baseUv[k];
    public SpriteRenderer Cell(int k) => cells[k];
    public Vector2 CellBase(int k)
    {
        if (current != null && TryPos(current, out Vector2[] pos) && k < pos.Length)
        {
            // セルの絵の左下が原点なので、中心は半セル分ずらす
            return pos[k] + new Vector2(spriteSize.x / cols, spriteSize.y / rows) * 0.5f;
        }
        return Vector2.zero;
    }
    // セルの左下基準の位置(SetCell の offset はここからのずれ)
    Vector2 CellOrigin(int k) => current != null && TryPos(current, out Vector2[] pos) && k < pos.Length ? pos[k] : Vector2.zero;
    public void SetCell(int k, Vector2 offset, float rotDeg, Color c)
    {
        var t = cells[k].transform;
        Vector2 o = CellOrigin(k);
        t.localPosition = new Vector3(o.x + offset.x, o.y + offset.y, 0f);
        t.localRotation = Quaternion.Euler(0f, 0f, rotDeg);
        cells[k].color = c;
    }
    public void ResetCells()
    {
        for (int k = 0; k < cells.Count; k++) { Vector2 o = CellOrigin(k); cells[k].transform.localPosition = new Vector3(o.x, o.y, 0f); cells[k].transform.localRotation = Quaternion.identity; }
    }

    public void SetEnabled(bool on)
    {
        for (int k = 0; k < cells.Count; k++) cells[k].enabled = on;
    }

    // 天空回廊ボス追加(2026-09-25) - 雲海/回廊の奥を並走する超大型ボス(天空タイタン等)
    // を地面より奥のレイヤーへ置くため。既存ボスは呼ばないので従来どおり。
    public void SetSortingOrder(int sortingOrder)
    {
        SortingOrder = sortingOrder;
        for (int k = 0; k < cells.Count; k++) cells[k].sortingOrder = sortingOrder;
    }

    // マルチプレイPhase 2 - 現在の描画順(共有ボスの見た目の同期用)。
    public int SortingOrder { get; private set; }

    static float Freq(LocoStyle s)
    {
        switch (s)
        {
            case LocoStyle.Gallop: return 13f;
            case LocoStyle.Run: return 12f;
            case LocoStyle.Stride: return 5.2f;
            case LocoStyle.Crawl: return 17f;
            case LocoStyle.Slither: return 6.5f;
            case LocoStyle.Wing: return 9f;
            case LocoStyle.Cloth: return 3.6f;
            default: return 0f;
        }
    }

    // 1歩あたりの位相(Stride系の足音判定用)
    public static float StepPhase(LocoStyle s) => Freq(s);

    // targetAmount: 0=静止, 1=全力。fwdSign: 画像ローカルでの「前」の向き(左向き素材=-1)。
    public void Update(LocoStyle style, float targetAmount, float dt, float fwdSign)
    {
        amount = Mathf.MoveTowards(amount, targetAmount, dt * 5f);
        Phase += dt * Freq(style) * Mathf.Lerp(0.6f, 1f, amount);
        if (current == null || !TryPos(current, out Vector2[] pos)) return;
        float W = spriteSize.x, H = spriteSize.y;
        float a = amount;
        float ph = Phase;

        for (int k = 0; k < cells.Count; k++)
        {
            Vector2 uv = baseUv[k];
            float u = uv.x, v = uv.y;
            float hd = fwdSign < 0f ? 1f - u : u;   // 0=尻尾側, 1=頭側
            float dx = 0f, dy = 0f;

            switch (style)
            {
                case LocoStyle.Gallop:
                case LocoStyle.Run:
                case LocoStyle.Stride:
                {
                    bool stride = style == LocoStyle.Stride;
                    float bob = stride ? -Mathf.Abs(Mathf.Sin(ph)) * 0.030f : Mathf.Abs(Mathf.Sin(ph)) * 0.040f;
                    dy += bob * H;
                    float legFrac = style == LocoStyle.Run ? 0.45f : 0.40f;
                    if (v < legFrac)
                    {
                        float kk = 1f - v / legFrac;
                        float side = hd > 0.5f ? 0f : Mathf.PI;
                        float lp = ph + side;
                        float swing = stride ? 0.10f : (style == LocoStyle.Run ? 0.13f : 0.17f);
                        dx += fwdSign * Mathf.Sin(lp) * swing * W * kk * kk;
                        dy += Mathf.Max(0f, Mathf.Cos(lp)) * (stride ? 0.05f : 0.09f) * H * kk;
                    }
                    else if (hd > 0.7f && v > 0.5f)
                    {
                        dy += Mathf.Sin(ph + 0.7f) * 0.025f * H; // 頭の上下
                    }
                    break;
                }
                case LocoStyle.Crawl:
                {
                    dy += Mathf.Abs(Mathf.Sin(ph * 0.5f)) * 0.012f * H;
                    if (v < 0.4f)
                    {
                        float kk = 1f - v / 0.4f;
                        float lp = ph + hd * 9f;
                        dx += fwdSign * Mathf.Sin(lp) * 0.06f * W * kk;
                        dy += Mathf.Max(0f, Mathf.Sin(lp + 1.5f)) * 0.06f * H * kk;
                    }
                    break;
                }
                case LocoStyle.Slither:
                {
                    float wv = ph - hd * 5.5f;
                    float lowW = 0.45f + 0.55f * (1f - v * 0.6f);
                    dy += Mathf.Sin(wv) * 0.055f * H * lowW;
                    dx += Mathf.Cos(wv) * 0.020f * W;
                    // 頭が左右(前後)に振れる
                    if (hd > 0.6f) dx += fwdSign * Mathf.Sin(ph * 0.9f) * 0.03f * W * (hd - 0.6f) * 2.5f;
                    break;
                }
                case LocoStyle.Wing:
                {
                    float ky = Mathf.Clamp01((v - 0.30f) / 0.70f);
                    float wingK = ky * ky * (0.4f + Mathf.Abs(u - 0.5f) * 1.6f);
                    dy += Mathf.Sin(ph) * 0.11f * H * wingK;
                    dy += Mathf.Sin(ph * 0.5f) * 0.03f * H;
                    dx -= fwdSign * 0.01f * W * Mathf.Sin(ph);
                    break;
                }
                case LocoStyle.Cloth:
                {
                    float lowK = Mathf.Clamp01(1f - v / 0.75f);
                    dx += -fwdSign * 0.05f * W * lowK;                                  // 後ろへなびく
                    dx += Mathf.Sin(ph + v * 7f + hd * 2f) * 0.045f * W * lowK;        // ひらひら
                    dy += Mathf.Sin(ph * 0.7f + hd * 3f) * 0.02f * H;
                    dy += Mathf.Sin(ph * 0.5f) * 0.03f * H;
                    break;
                }
            }

            Vector2 p = pos[k];
            cells[k].transform.localPosition = new Vector3(p.x + dx * a, p.y + dy * a, 0f);
        }
    }
}

// ワールドに置かれる短い速度線(ボス/プレイヤーが走り抜けた軌跡)。世界に固定されて
// 画面上は後ろへ流れる。
public class SpeedLine : MonoBehaviour
{
    float life, t;
    SpriteRenderer sr;
    Color baseColor;

    public static void Spawn(Vector3 pos, float length, Color color, float lifetime = 0.32f, int sortingOrder = int.MinValue, float thickness = 0.05f)
    {
        var go = new GameObject("SpeedLine");
        go.transform.position = pos;
        go.transform.localScale = new Vector3(length, thickness, 1f);
        var s = go.AddComponent<SpeedLine>();
        s.life = lifetime;
        s.sr = go.AddComponent<SpriteRenderer>();
        s.sr.sprite = BossFx.Block();
        s.sr.sortingOrder = sortingOrder == int.MinValue ? RenderOrder.Boss - 1 : sortingOrder;
        s.baseColor = color;
        s.sr.color = color;
    }

    void Update()
    {
        t += Time.deltaTime;
        float f = t / life;
        if (f >= 1f) { Destroy(gameObject); return; }
        Color c = baseColor; c.a = baseColor.a * (1f - f);
        sr.color = c;
        Vector3 sc = transform.localScale; sc.x *= 1f + Time.deltaTime * 1.5f; transform.localScale = sc;
    }
}
