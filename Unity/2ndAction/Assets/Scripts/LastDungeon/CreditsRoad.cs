using System.Collections.Generic;
using UnityEngine;

// エンドロールを「走るマップ」として道の上に組み立てる(2026-09-30)。
// StaffCreditsDataの各項目を、道の先へ順に巨大文字として並べる(スクロールする字幕ではなく、ゲーム世界の物):
//   Title    … 題字が頭上に浮かぶ(跳んで乗れる)
//   Standing … 名前が道の上に立つ(跳んで上に乗る/飛び越える/手前を横切る)
//   Arch     … 名前が頭上に浮かぶ(文字の隙間の下を走り抜ける/跳んで乗る)
//   Stairs   … 名前が階段状に浮かぶ(文字から文字へ跳び移る)
//   最後に THANK YOU FOR PLAYING の石板が道を塞ぐ(攻撃して壊すと先へ進める) → END → 広い静かな場所(ONE MORE MILE?)
// 敵も危険も無い。落ちる穴も無い(地形はLastCorridorDirectorが平らな一本道にしている)。
public class CreditsRoad : MonoBehaviour
{
    public StaffCreditsData Data { get; private set; }
    // 位置はすべて論理X(走行距離の座標。Floating Originの原点移動でずれない)
    public double StartX { get; private set; }
    public double WallX { get; private set; }
    public double EndX { get; private set; }
    public double ChoiceX { get; private set; }   // ここに着いたら選択エリア(自動前進を止める)
    public CreditWall Wall { get; private set; }
    public readonly List<CreditLetter> Letters = new List<CreditLetter>();
    public readonly List<(string label, double x0, double x1)> Sections = new List<(string, double, double)>();
    public const int LetterOrder = RenderOrder.Enemy;

    public static CreditsRoad Build(float startX, StaffCreditsData data)
    {
        var go = new GameObject("[CreditsRoad]");
        var r = go.AddComponent<CreditsRoad>();
        r.Data = data;
        r.StartX = FloatingOrigin.ToLogical(startX);
        r.Layout(startX);
        return r;
    }

    static float Ground(float x)
    {
        var tm = TerrainManager.Instance;
        float? g = tm != null ? tm.GetHeightAt(x) : null;
        return g ?? (tm != null ? tm.GetGroundLineAt(x) : 0f);
    }

    // 1行の文字を並べる(左端x、下端y、キャップハイト)。戻り値=右端x。yStepで階段状。
    float PlaceLine(string text, float x, float y, float cap, float extraGap, float yStep, bool platform, bool breakable, bool shadow, float bob, string section)
    {
        float x0 = x;
        int i = 0;
        foreach (char c in text)
        {
            var sp = GlyphFont.Get(c);
            if (sp == null) { x += GlyphFont.SpaceWidth * cap + extraGap; continue; }
            float w = sp.bounds.size.x * cap;
            float by = y + yStep * i;
            var l = CreditLetter.Create(transform, c, new Vector3(x + w * 0.5f, by, 0f), cap, LetterOrder, shadow);
            if (l != null)
            {
                l.platform = platform;
                l.breakable = breakable;
                l.hp = breakable ? 2 : 1;
                l.bobAmplitude = bob;
                Letters.Add(l);
            }
            x += w + GlyphFont.Tracking * cap + extraGap;
            i++;
        }
        Sections.Add((section, FloatingOrigin.ToLogical(x0), FloatingOrigin.ToLogical(x)));
        return x;
    }

    void Layout(float startSceneX)
    {
        float x = startSceneX;
        foreach (var e in Data.entries)
        {
            string label = string.IsNullOrEmpty(e.role) ? (e.names.Length > 0 ? e.names[0] : "") : e.role;
            float nameCap = Mathf.Max(0.6f, e.nameHeight);
            float roleCap = Mathf.Max(0.45f, nameCap * 0.36f);
            float nameW = 0f;
            foreach (var n in e.names) nameW = Mathf.Max(nameW, GlyphFont.Measure(n) * nameCap);
            float g = Ground(x + nameW * 0.5f);
            float right = x;
            switch (e.layout)
            {
                case StaffCreditsData.Layout.Title:
                {
                    float y = g + 3.1f;
                    foreach (var n in e.names) { right = Mathf.Max(right, PlaceLine(n, x, y, nameCap, 0.1f, 0f, true, e.breakable, false, 0.12f, label)); y += nameCap * 1.4f; }
                    break;
                }
                case StaffCreditsData.Layout.Arch:
                {
                    // 名前は頭上(プレイヤーの頭より上)に浮かび、文字の隙間の下を走り抜けられる
                    float y = g + 2.1f; // 頭(約1.7m)より上。上面は2段ジャンプで届く高さ
                    foreach (var n in e.names) right = Mathf.Max(right, PlaceLine(n, x, y, nameCap, nameCap * 0.35f, 0f, true, e.breakable, false, 0.08f, label));
                    if (!string.IsNullOrEmpty(e.role)) PlaceLine(e.role, x, y + nameCap * 1.45f + 0.4f, roleCap, 0f, 0f, false, false, false, 0.06f, label + " (role)");
                    break;
                }
                case StaffCreditsData.Layout.Stairs:
                {
                    // 名前の文字が階段状に浮かぶ(文字から文字へ跳び移る)
                    float y = g + 0.3f; // 1段目は1段ジャンプで乗れる高さ
                    foreach (var n in e.names) right = Mathf.Max(right, PlaceLine(n, x, y, nameCap, nameCap * 0.9f, 0.55f, true, e.breakable, false, 0.05f, label));
                    if (!string.IsNullOrEmpty(e.role)) PlaceLine(e.role, x, g + 4.9f, roleCap, 0f, 0f, false, false, false, 0.06f, label + " (role)");
                    break;
                }
                default:
                {
                    // 名前が道の上に立つ(上に乗る/飛び越える/手前を横切る)
                    foreach (var n in e.names) right = Mathf.Max(right, PlaceLine(n, x, g, nameCap, nameCap * 0.28f, 0f, true, e.breakable, true, 0f, label));
                    if (!string.IsNullOrEmpty(e.role)) PlaceLine(e.role, x, g + nameCap * 1.3f + 1.8f, roleCap, 0f, 0f, false, false, false, 0.06f, label + " (role)");
                    break;
                }
            }
            x = right + Mathf.Max(6f, e.gapAfter);
        }

        // THANK YOU FOR PLAYING の石板(道を塞ぐ壁。攻撃して壊すと先へ進める)
        WallX = FloatingOrigin.ToLogical(x);
        Wall = CreditWall.Build(transform, x, Ground(x + 5f), Data.wallText, Data.wallHp);
        x = x + Wall.Width + 18f;

        // END
        float endCap = 2.3f;
        float endW = GlyphFont.Measure(Data.endText) * endCap;
        EndX = FloatingOrigin.ToLogical(x);
        PlaceLine(Data.endText, x, Ground(x + endW * 0.5f), endCap, 0.4f, 0f, true, false, true, 0f, "END");
        // 広い静かな場所(ここで止まる)
        ChoiceX = EndX + endW + 34.0;
        Debug.Log($"[LastDungeon][Credits] built {Letters.Count} letters, wall at {WallX:F0}m, END at {EndX:F0}m, choice at {ChoiceX:F0}m (logical)");
    }

    // 今プレイヤーがいる区間の名前(テスト/演出用)。xはシーン座標
    public string SectionAt(float sceneX)
    {
        double x = FloatingOrigin.ToLogical(sceneX);
        foreach (var s in Sections) if (x >= s.x0 - 1.0 && x <= s.x1 + 1.0) return s.label;
        return "";
    }
}

// THANK YOU FOR PLAYING の石板。道を塞ぐ壁(プレイヤーは手前で止まる)。攻撃の回数(攻撃力は無関係)で壊れる。
// 当てるたびに石板が揺れ、刻まれた文字が少しずつ剥がれ落ち、最後に砕けて道が開く。
public class CreditWall : MonoBehaviour, IWorldSolid
{
    public int MaxHp { get; private set; }
    public int Hp { get; private set; }
    public bool Broken { get; private set; }
    public float Width { get; private set; }
    public float Height { get; private set; }
    public static int Hits;
    SpriteRenderer slab;
    BoxCollider2D col;
    readonly List<CreditLetter> letters = new List<CreditLetter>();
    float shakeT, breakT = -1f;
    Vector3 slabBase;
    readonly Collider2D[] hitCols = new Collider2D[6];
    readonly int[] hitSwings = new int[6];
    int hitNext;

    public static CreditWall Build(Transform parent, float leftX, float groundY, string text, int hp)
    {
        var go = new GameObject("CreditWall");
        go.transform.SetParent(parent, false);
        var w = go.AddComponent<CreditWall>();
        w.MaxHp = w.Hp = Mathf.Max(1, hp);
        w.Width = 11f; w.Height = 5.4f;
        go.transform.position = new Vector3(leftX + w.Width * 0.5f, groundY, 0f);
        w.Construct(text);
        return w;
    }

    void Construct(string text)
    {
        var st = new GameObject("Slab").transform;
        st.SetParent(transform, false);
        slab = st.gameObject.AddComponent<SpriteRenderer>();
        slab.sprite = Resources.Load<Sprite>("LastDungeon/Fx/slab");
        if (slab.sprite == null) slab.sprite = BossFx.Block();
        slab.sortingOrder = CreditsRoad.LetterOrder - 1;
        Vector2 ss = slab.sprite.bounds.size;
        st.localScale = new Vector3(Width / Mathf.Max(0.01f, ss.x), Height / Mathf.Max(0.01f, ss.y), 1f);
        st.localPosition = new Vector3(0f, Height * 0.5f - slab.sprite.bounds.center.y * st.localScale.y, 0f);
        slabBase = st.localPosition;
        // 刻まれた文字: 「THANK YOU」「FOR PLAYING」の2行(文字数が多ければ単語で折り返す)
        var lines = SplitLines(text);
        float cap = 0.78f;
        float lineH = cap * 1.55f;
        float top = Height * 0.5f + lineH * (lines.Count - 1) * 0.5f;
        for (int li = 0; li < lines.Count; li++)
        {
            float w = GlyphFont.Measure(lines[li]) * cap;
            float x = transform.position.x - w * 0.5f;
            float y = transform.position.y + top - li * lineH - cap * 0.55f;
            foreach (char c in lines[li])
            {
                var sp = GlyphFont.Get(c);
                if (sp == null) { x += GlyphFont.SpaceWidth * cap; continue; }
                float cw = sp.bounds.size.x * cap;
                var l = CreditLetter.Create(transform, c, new Vector3(x + cw * 0.5f, y, 0f), cap, CreditsRoad.LetterOrder, false);
                if (l != null) { l.platform = false; l.attackable = false; l.HitRouter = (letter, atk) => { OnAttack(atk); return true; }; letters.Add(l); }
                x += cw + GlyphFont.Tracking * cap;
            }
        }
        var rb = gameObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        col = gameObject.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(Width, Height);
        col.offset = new Vector2(0f, Height * 0.5f);
        WorldPlatforms.Register(this);
    }

    static List<string> SplitLines(string text)
    {
        var words = text.Split(' ');
        var lines = new List<string>();
        if (words.Length <= 2) { lines.Add(text); return lines; }
        // 前半/後半へ(THANK YOU / FOR PLAYING)
        int half = words.Length / 2;
        lines.Add(string.Join(" ", words, 0, half));
        lines.Add(string.Join(" ", words, half, words.Length - half));
        return lines;
    }

    void OnDestroy() { WorldPlatforms.Unregister(this); }

    public bool SolidActive => !Broken && isActiveAndEnabled;
    public bool IsPlatform => false;
    public bool IsWall => true;
    public Rect SolidRect
    {
        get
        {
            Vector3 p = transform.position;
            return Rect.MinMaxRect(p.x - Width * 0.5f, p.y, p.x + Width * 0.5f, p.y + Height);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!Broken && other.CompareTag("PlayerAttack")) OnAttack(other);
    }

    void OnAttack(Collider2D atk)
    {
        if (Broken || atk == null) return;
        var info = atk.GetComponent<PlayerAttackInfo>();
        int swing = info != null ? info.SwingId : 0;
        for (int i = 0; i < hitCols.Length; i++) if (hitCols[i] == atk && hitSwings[i] == swing) return;
        hitCols[hitNext] = atk; hitSwings[hitNext] = swing; hitNext = (hitNext + 1) % hitCols.Length;
        Hits++;
        Hp--;
        shakeT = 0.22f;
        Vector3 at = atk.bounds.ClosestPoint(transform.position + new Vector3(-Width * 0.5f, Height * 0.5f, 0f));
        OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), at, new Color(0.7f, 0.72f, 0.82f, 1f), 10, 0.45f, 0.2f, 0.55f, 5f, 1.6f, RenderOrder.CombatFx);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.StrongHit, 0.7f);
        var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cf != null) cf.Shake(0.07f, 0.12f);
        // 刻まれた文字が少しずつ剥がれ落ちる
        int shouldRemain = Mathf.CeilToInt(letters.Count * Mathf.Clamp01((float)Hp / MaxHp));
        int remain = 0; foreach (var l in letters) if (!l.Broken) remain++;
        foreach (var l in letters)
        {
            if (remain <= shouldRemain) break;
            if (l.Broken || Random.value < 0.35f) continue;
            l.Shatter(-1f); remain--;
        }
        foreach (var l in letters) if (!l.Broken) l.Kick(0.5f);
        slab.color = Color.Lerp(Color.white, new Color(0.62f, 0.6f, 0.62f), 1f - (float)Hp / MaxHp);
        if (Hp <= 0) Break();
    }

    void Break()
    {
        Broken = true;
        WorldPlatforms.Unregister(this);
        if (col != null) col.enabled = false;
        foreach (var l in letters) if (!l.Broken) l.Shatter();
        Vector3 c = transform.position + new Vector3(0f, Height * 0.5f, 0f);
        OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), c, new Color(0.75f, 0.78f, 0.9f, 1f), 30, 0.9f, 0.3f, 1.1f, 9f, 3f, RenderOrder.CombatFx);
        OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), c, new Color(1f, 0.86f, 0.45f, 1f), 24, 0.9f, 0.2f, 0.8f, 8f, 2f, RenderOrder.CombatFx);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossDefeat, 0.8f);
        var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cf != null) cf.Shake(0.2f, 0.35f);
        breakT = 0f;
        Debug.Log("[LastDungeon][Credits] THANK YOU FOR PLAYING wall broken");
    }

    void Update()
    {
        if (slab == null) return;
        if (breakT >= 0f)
        {
            breakT += Time.deltaTime;
            float k = Mathf.Clamp01(breakT / 0.9f);
            slab.transform.localPosition = slabBase + new Vector3(0f, -Height * 0.6f * k * k, 0f);
            slab.transform.localRotation = Quaternion.Euler(0f, 0f, -8f * k);
            var cc = slab.color; cc.a = 1f - k; slab.color = cc;
            if (k >= 1f) slab.gameObject.SetActive(false);
            return;
        }
        if (shakeT > 0f)
        {
            shakeT -= Time.deltaTime;
            slab.transform.localPosition = slabBase + new Vector3(Mathf.Sin(Time.time * 80f) * 0.08f * Mathf.Clamp01(shakeT / 0.22f), 0f, 0f);
        }
        else slab.transform.localPosition = slabBase;
    }
}
