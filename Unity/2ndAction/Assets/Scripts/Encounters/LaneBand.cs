using UnityEngine;

// 判断型の道中イベント(2026-11-05)の危険帯。赤い帯 = 危ない所 / 帯の中の矢印 = 行く方向(上 = 跳ぶ、下 = 地上にいる)。
//  ・画面に固定(CreateScreen): 画面の左右いっぱい・プレイヤーの足元から測った高さ。予兆の間は上の縁の線が右から縮む(残り時間)、
//    来る瞬間に一度明るくなって消える。速さに関係なく同じ秒数
//  ・世界に固定(CreateWorld): 区間(論理 X)の上だけ。区間がまだ画面の外の間は、画面の右の縁に同じ色の帯(予告)を出す
//  目立ちすぎないよう塗りは薄く(DecisionEventTuning.bandAlpha)、縁の線と矢印で形を見せる。縁と矢印の色はマップの色
public class LaneBand : MonoBehaviour
{
    static readonly Color Danger = new Color(1f, 0.16f, 0.12f);
    bool screen, ground;
    double x0L, x1L;
    float bottom, top, warn, born, strikeAt = -1f, strikeLen;
    float baseY = float.NaN;
    DecisionEvents.MapStyle style;
    SpriteRenderer fill, edgeTop, edgeBottom, timer;
    readonly SpriteRenderer[] arrows = new SpriteRenderer[3]; // エステル: 画面に 2〜3 組

    static Sprite white;
    static Sprite White()
    {
        if (white == null) white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        return white;
    }

    public static LaneBand CreateScreen(float bottom, float top, float warn, DecisionEvents.MapStyle style, bool ground)
    {
        var b = Make("LaneBand(screen)", bottom, top, style, ground);
        b.screen = true; b.warn = warn;
        return b;
    }

    public static LaneBand CreateWorld(float sceneX0, float sceneX1, float bottom, float top, DecisionEvents.MapStyle style, bool ground, float? surfaceY = null)
    {
        var b = Make("LaneBand(world)", bottom, top, style, ground);
        b.x0L = FloatingOrigin.ToLogical(sceneX0); b.x1L = FloatingOrigin.ToLogical(sceneX1);
        var tm = TerrainManager.Instance;
        if (surfaceY.HasValue) b.baseY = surfaceY.Value;
        else if (tm != null) b.baseY = tm.GetGroundLineAt((sceneX0 + sceneX1) * 0.5f);
        return b;
    }

    static LaneBand Make(string name, float bottom, float top, DecisionEvents.MapStyle style, bool ground)
    {
        var go = new GameObject(name);
        var b = go.AddComponent<LaneBand>();
        b.bottom = bottom; b.top = top; b.style = style; b.ground = ground; b.born = Time.time;
        b.fill = b.Part("fill", White(), RenderOrder.Ground);
        b.edgeTop = b.Part("edgeTop", White(), RenderOrder.EnvironmentFx);
        b.edgeBottom = b.Part("edgeBottom", White(), RenderOrder.EnvironmentFx);
        b.timer = b.Part("timer", White(), RenderOrder.EnvironmentFx);
        for (int i = 0; i < b.arrows.Length; i++) b.arrows[i] = b.Part("arrow" + i, DecisionEnemy.ChevronSprite(), RenderOrder.EnvironmentFx);
        return b;
    }

    SpriteRenderer Part(string n, Sprite s, int order)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s; sr.sortingOrder = order;
        go.transform.localPosition = new Vector3(0f, 0f, -0.05f);
        return sr;
    }

    // 帯が来た瞬間(群れが横切る間)
    public void Strike(float seconds) { strikeAt = Time.time; strikeLen = Mathf.Max(0.1f, seconds); }

    // プレイヤーが立っている面の高さ(上の道にいる時はその面)
    public static float PlayerBaseY()
    {
        var pc = PlayerController.Instance; var tm = TerrainManager.Instance;
        if (pc == null || tm == null) return 0f;
        float px = pc.transform.position.x;
        if (pc.AssistOnSky) { var s = tm.GetSkyHeightAt(px); if (s.HasValue) return s.Value; }
        var h = tm.GetHeightAt(px);
        return h.HasValue ? h.Value : tm.GetGroundLineAt(px);
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        var pc = PlayerController.Instance;
        if (cam == null || pc == null) { Destroy(gameObject); return; }
        float left = GameView.Left(cam), right = GameView.Right(cam);
        float a = DecisionEventTuning.I.bandAlpha, t = Time.time - born;
        float pulse = 0.5f + 0.5f * Mathf.Sin(t * 9f);
        float x0, x1, by;
        bool preview = false;
        if (screen)
        {
            float target = PlayerBaseY();
            baseY = float.IsNaN(baseY) ? target : Mathf.Lerp(baseY, target, 1f - Mathf.Exp(-10f * Time.deltaTime));
            by = baseY; x0 = left; x1 = right;
            if (strikeAt < 0f && t > warn + 3f) { Destroy(gameObject); return; }
            if (strikeAt >= 0f && Time.time > strikeAt + strikeLen) { Destroy(gameObject); return; }
        }
        else
        {
            by = baseY;
            x0 = (float)(x0L - FloatingOrigin.Offset); x1 = (float)(x1L - FloatingOrigin.Offset);
            if (x1 < left - 4f) { Destroy(gameObject); return; }
            if (x0 > right) { preview = true; x1 = right; x0 = right - 0.55f; } // まだ画面の外: 右の縁に予告
        }
        float y0 = by + bottom, y1 = by + top;
        float w = Mathf.Max(0.1f, x1 - x0), h = Mathf.Max(0.1f, y1 - y0), cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;

        float fillA = a * (0.7f + 0.3f * pulse);
        if (screen && strikeAt >= 0f) { float k = 1f - (Time.time - strikeAt) / strikeLen; fillA = a * 1.8f * Mathf.Clamp01(k); }
        else if (screen && t > warn - 0.25f) fillA = a * 1.6f; // 来る直前は明るく
        if (preview) fillA = a * 2.2f * (0.6f + 0.4f * pulse);
        Place(fill, cx, cy, w, h, new Color(Danger.r, Danger.g, Danger.b, fillA));
        var ac = style.accent; float ea = preview ? 0.9f : 0.75f;
        if (screen && strikeAt >= 0f) ea *= Mathf.Clamp01(1f - (Time.time - strikeAt) / strikeLen);
        Place(edgeTop, cx, y1, w, 0.06f, new Color(ac.r, ac.g, ac.b, ea));
        Place(edgeBottom, cx, y0, w, 0.06f, new Color(ac.r, ac.g, ac.b, ground ? ea * 0.4f : ea));
        // 残り時間(画面の帯の予兆の間だけ): 上の縁の明るい線が右から縮む
        if (screen && strikeAt < 0f && warn > 0f)
        {
            float rem = Mathf.Clamp01(1f - t / warn);
            float tw = w * rem;
            bool last = warn - t < 0.2f; // 最後の 0.2 秒は線だけ少し強く(帯全体は点滅させない = エステル)
            Place(timer, x0 + tw * 0.5f, y1 + 0.075f, Mathf.Max(0.01f, tw), last ? 0.22f : 0.15f, last ? new Color(1f, 1f, 0.95f, 1f) : new Color(1f, 1f, 0.92f, 1f));
        }
        else timer.enabled = false;
        // 矢印: 地上の帯 = 上向き(跳ぶ)/ 空の帯 = 下向き(地上にいる)。帯の中に等間隔
        int na = preview ? 1 : arrows.Length;
        for (int i = 0; i < arrows.Length; i++)
        {
            var ar = arrows[i];
            if (i >= na || (screen && strikeAt >= 0f)) { ar.enabled = false; continue; }
            ar.enabled = true;
            float ax = preview ? cx : x0 + w * (i + 0.5f) / na;
            float bob = 0.05f * Mathf.Sin(t * 2.5f + i); // ゆっくり
            float ay = ground ? y1 + 0.38f + bob : y0 - 0.38f - bob; // 帯の安全側の縁(地上の帯 = 上の縁の上 / 空の帯 = 下の縁の下)
            float size = Mathf.Clamp(h * 0.54f, 0.42f, 0.72f); // 1.2 倍
            ar.transform.position = new Vector3(ax, ay, -0.06f);
            ar.transform.rotation = Quaternion.Euler(0f, 0f, ground ? 0f : 180f);
            ar.transform.localScale = Vector3.one * size / Mathf.Max(0.01f, ar.sprite.bounds.size.y);
            ar.color = new Color(ac.r, ac.g, ac.b, preview ? 0.95f : 0.85f); // 点滅させない
        }
    }

    static void Place(SpriteRenderer sr, float cx, float cy, float w, float h, Color c)
    {
        sr.enabled = c.a > 0.003f;
        sr.color = c;
        sr.transform.position = new Vector3(cx, cy, -0.05f);
        var b = sr.sprite.bounds.size;
        sr.transform.localScale = new Vector3(w / Mathf.Max(0.0001f, b.x), h / Mathf.Max(0.0001f, b.y), 1f);
    }
}

// 帯を横切る敵の群れの1体(画面に固定して右から左へ。速さに関係なく同じ秒数)。普通の敵(EnemyController)ではない:
//  体当たりで1回だけダメージ(群れ全体で1回)。突進の群れなので攻撃では止まらない(火花が出るだけ = 跳ぶ/地上にいるで避ける)。
//  2026-11-05: 最初は攻撃で消えたが、オートが前の敵へ振った攻撃で群れまで消え、放置でも当たらなかった → 止まらない群れに
public class LaneSweeper : MonoBehaviour
{
    public class Group { public bool hit; public int kills; }
    Group group; EnemyDefinition def;
    bool ground; float bottom, top, y01, delay, secs, born, height, offsetY;
    Sprite[] frames; SpriteRenderer sr; BoxCollider2D col;
    DecisionEvents.MapStyle style; float fxT, nextSpark;

    public static LaneSweeper Create(EnemyDefinition def, Group group, bool ground, float bottom, float top, float y01, float delay, float secs, float height, DecisionEvents.MapStyle style)
    {
        if (def == null) return null;
        var frames = def.runFrames != null && def.runFrames.Length > 0 ? def.runFrames : new[] { def.sprite };
        if (frames[0] == null) return null;
        var go = new GameObject("LaneSweeper " + def.enemyId);
        var s = go.AddComponent<LaneSweeper>();
        s.def = def; s.group = group; s.ground = ground; s.bottom = bottom; s.top = top; s.y01 = y01; s.delay = delay; s.secs = Mathf.Max(0.2f, secs); s.height = height; s.style = style;
        s.frames = frames; s.born = Time.time;
        var vis = new GameObject("Visual"); vis.transform.SetParent(go.transform, false);
        s.sr = vis.AddComponent<SpriteRenderer>();
        s.sr.sprite = frames[0]; s.sr.color = def.tint; s.sr.sortingOrder = RenderOrder.Enemy;
        float k = height / Mathf.Max(0.01f, frames[0].bounds.size.y);
        vis.transform.localScale = new Vector3(def.defaultFacingRight ? -k : k, k, 1f); // 左へ走る
        s.offsetY = -frames[0].bounds.min.y * k; // 足元 = 位置
        vis.transform.localPosition = new Vector3(0f, s.offsetY, 0f);
        s.col = go.AddComponent<BoxCollider2D>();
        s.col.isTrigger = true;
        var bsz = frames[0].bounds.size * k;
        s.col.size = new Vector2(bsz.x * 0.7f, bsz.y * 0.75f);
        s.col.offset = new Vector2(0f, s.offsetY + (frames[0].bounds.center.y * k));
        s.sr.enabled = false; s.col.enabled = false;
        return s;
    }

    void Update()
    {
        var cam = Camera.main;
        if (cam == null) { Destroy(gameObject); return; }
        float t = Time.time - born - delay;
        if (t < 0f) return;
        float p = t / secs;
        if (p > 1.05f) { Destroy(gameObject); return; }
        sr.enabled = true; col.enabled = true;
        float left = GameView.Left(cam), right = GameView.Right(cam);
        float x = Mathf.Lerp(right + 1.2f, left - 1.2f, p);
        float by = LaneBand.PlayerBaseY();
        float y = ground ? by + bottom : by + Mathf.Lerp(bottom + 0.05f, top - height - 0.05f, y01);
        transform.position = new Vector3(x, y, 0f);
        // 2026-11-05: 群れは画面を約 50m/秒で横切る → 物理の更新(50 回/秒)の間に 1m 以上進み、当たり判定(トリガー)をすり抜けていた。
        //  前のフレームの位置から今の位置までの通り道で、プレイヤーの体と重なったかを自分で確かめる
        if (!group.hit && !float.IsNaN(prevX)) SweptHitCheck(prevX, x, y);
        // 確認用: プレイヤーの位置を通った瞬間の高さ(当たらなかった理由の切り分け)
        var pcd = PlayerController.Instance;
        if (Debug.isDebugBuild && pcd != null && !float.IsNaN(prevX) && prevX >= pcd.transform.position.x && x < pcd.transform.position.x && !loggedPass)
        {
            loggedPass = true;
            Debug.Log($"[Decision] sweeper pass ground={ground} sweeperY={y:F2}..{y + height:F2} playerY={pcd.transform.position.y:F2} grounded={pcd.IsGrounded} reacting={pcd.IsReacting} hit={group.hit}");
        }
        prevX = x;
        sr.sprite = frames[Mathf.FloorToInt(t * 14f) % frames.Length];
        fxT -= Time.deltaTime;
        if (fxT <= 0f)
        {
            fxT = 0.07f;
            var at = new Vector3(x + 0.4f, ground ? y + 0.15f : y + height * 0.5f, 0f);
            OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), at, style.dust, style.kind == 1 ? 2 : 3, 0.35f, 0.25f, 0.55f, 1.2f, 2.2f, RenderOrder.CombatFx - 2);
        }
    }

    float prevX = float.NaN; bool loggedPass;
    static Collider2D playerCol; static PlayerController playerColOwner;
    void SweptHitCheck(float xa, float xb, float y)
    {
        var pc = PlayerController.Instance;
        if (pc == null) return;
        if (playerColOwner != pc) { playerColOwner = pc; playerCol = null; foreach (var c in pc.GetComponents<Collider2D>()) if (c != null && !c.isTrigger) { playerCol = c; break; } if (playerCol == null) playerCol = pc.GetComponent<Collider2D>(); }
        if (playerCol == null || !playerCol.enabled) return;
        var pb = EnemyController.FreshBounds(playerCol);
        Vector2 off = col.offset * (Vector2)transform.lossyScale, half = col.size * 0.5f * new Vector2(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
        float cy = y + off.y;
        float minX = Mathf.Min(xa, xb) + off.x - half.x, maxX = Mathf.Max(xa, xb) + off.x + half.x;
        if (maxX < pb.min.x || minX > pb.max.x || cy + half.y < pb.min.y || cy - half.y > pb.max.y) return;
        HitPlayer();
    }
    void HitPlayer()
    {
        if (group.hit || PlayerController.Instance == null) return;
        group.hit = true;
        float hpMul = def != null ? def.hpMultiplier : 1f;
        var gm = GameManager.Instance;
        if (gm != null && gm.MaxDistance >= DecisionEventTuning.I.sweepHeavyFromKm * 1000f) hpMul = Mathf.Max(hpMul, DifficultyTuning.I.largeHpMultiplier); // 群れの体当たりは重い(1回 2〜3 ハート)
        DifficultyTuning.NextAttackerHpMul = hpMul;
        PlayerController.Instance.TakeDamage(source: "Enemy:LaneSweep");
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("PlayerAttack"))
        {
            group.kills++; // 当てた回数(止まらない)
            if (Time.time >= nextSpark)
            {
                nextSpark = Time.time + 0.12f;
                OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), transform.position + Vector3.up * height * 0.6f, new Color(1f, 0.95f, 0.75f, 0.9f), 5, 0.25f, 0.2f, 0.45f, 3f, 1f, RenderOrder.CombatFx);
            }
            return;
        }
        if (other.CompareTag("Player")) HitPlayer();
    }
}
