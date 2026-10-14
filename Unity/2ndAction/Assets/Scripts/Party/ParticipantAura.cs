using System.Collections.Generic;
using UnityEngine;

// マルチ・CPU サポート共通の識別用オーラ(2026-10-14)。参加者ごとの固有の色で、誰がどこにいるかを分かるようにする。
//  ・色は参加番号(P1〜P8)で決まる(全端末で同じ番号 = 同じ色)。赤/橙/紫は使わない(敵の赤い警告・BREAK などと紛れないように)
//  ・輪郭の薄い光: 体の絵の後ろに、色付きの写しを少しずらして並べる(キャラ本来の色や絵は塗りつぶさない。黒い輪郭線のさらに外側)
//  ・足元の小さなリング: 地面に立っている時だけ(空中では輪郭の光だけ残す)
//  ・自分(YOU)は輪郭を少し強く。名前/番号の表示は ParticipantLabels(色だけに頼らない)
//  ・濃さは設定(GameSettings.AuraIntensity)。0 にすると光とリングは消え、名前と番号だけ残る
//  ・被弾の点滅/無敵の点滅は体の絵だけが点滅し、オーラは消えない(点滅が見える)
// ソロ(仲間なし)では付けない(従来の見た目のまま)。
public class ParticipantAura : MonoBehaviour
{
    // 8色(赤/橙/紫を避ける)。P1 空色 / P2 黄緑 / P3 黄 / P4 青緑 / P5 白 / P6 青 / P7 桃 / P8 砂色
    static readonly Color[] Palette =
    {
        new Color(0.31f, 0.76f, 0.97f), new Color(0.56f, 0.88f, 0.29f), new Color(1.00f, 0.88f, 0.30f), new Color(0.18f, 0.88f, 0.76f),
        new Color(0.96f, 0.96f, 0.96f), new Color(0.31f, 0.42f, 1.00f), new Color(1.00f, 0.62f, 0.85f), new Color(0.79f, 0.64f, 0.48f),
    };
    public static Color ColorOf(int pn) => Palette[Mathf.Clamp(pn, 1, Palette.Length) - 1];

    public static readonly List<ParticipantAura> All = new List<ParticipantAura>();

    public int Pn { get; private set; }
    public bool IsLocal { get; private set; }
    public bool IsCpu { get; private set; }
    public string Label { get; private set; } = "";
    public SpriteRenderer Body { get; private set; }
    public System.Func<bool> Hidden; // 倒れて消えている等(名前も出さない)
    public System.Func<int> PnSource; // マルチ: 番号を毎回読み直す(つなぎ直しで HOST が元の番号へ戻した時に追従)

    SpriteRenderer[] glow;
    SpriteRenderer ring;
    const int Copies = 8;

    public static ParticipantAura Attach(GameObject root, SpriteRenderer body, int pn, bool isLocal, bool isCpu, string label)
    {
        if (root == null || body == null) return null;
        var a = root.GetComponent<ParticipantAura>();
        if (a == null) a = root.AddComponent<ParticipantAura>();
        a.Pn = pn; a.IsLocal = isLocal; a.IsCpu = isCpu; a.Label = label ?? ""; a.Body = body;
        a.Build();
        return a;
    }

    // この端末のプレイヤーに付ける(CPU サポートは P1 / マルチは自分の番号)。ソロ(仲間なし)では付けない
    public static void AttachLocalPlayer()
    {
        var pc = PlayerController.Instance;
        if (pc == null) return;
        var body = pc.VisualRenderer;
        if (CpuParty.Active) Attach(pc.gameObject, body, 1, true, false, Codename.Current);
        else if (NetRunLauncher.IsMultiplayerRun && NetPlayer.Local != null && NetPlayer.Local.PlayerNumber > 0)
        {
            var me = NetPlayer.Local;
            var a = Attach(pc.gameObject, body, me.PlayerNumber, true, false, me.DisplayName);
            if (a != null) a.PnSource = () => me != null ? me.PlayerNumber : 0;
        }
        else RemoveFrom(pc.gameObject);
    }

    public static void RemoveFrom(GameObject root)
    {
        if (root == null) return;
        var a = root.GetComponent<ParticipantAura>();
        if (a != null) Destroy(a);
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }
    void OnDestroy()
    {
        All.Remove(this);
        if (glow != null) foreach (var g in glow) if (g != null) Destroy(g.gameObject);
        if (ring != null) Destroy(ring.gameObject);
    }

    static Material glowMat; static bool glowTried;
    // 絵の形を1色で塗る(普通の重ね方: Resources/Shaders/SpriteSolid)。無ければ加算の SpriteGlow
    static Material GlowMat()
    {
        if (glowTried) return glowMat;
        glowTried = true;
        var sh = Resources.Load<Shader>("Shaders/SpriteSolid");
        if (sh != null && sh.isSupported) glowMat = new Material(sh) { name = "ParticipantAura Solid" };
        else glowMat = Resources.Load<Material>("Effects/SpriteGlow");
        return glowMat;
    }

    void Build()
    {
        if (glow != null) foreach (var g in glow) if (g != null) Destroy(g.gameObject);
        if (ring != null) Destroy(ring.gameObject);
        glow = new SpriteRenderer[Copies];
        // 体の絵の子に置く(絵の向き/拡大/揺れにそのまま付いていく)。黒い輪郭線(順番 -1)のさらに後ろ(-2)
        float th = IsLocal ? 0.075f : 0.06f;
        for (int i = 0; i < Copies; i++)
        {
            float ang = i / (float)Copies * Mathf.PI * 2f;
            var go = new GameObject("AuraGlow");
            go.transform.SetParent(Body.transform, false);
            go.transform.localPosition = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0.001f) * th / Mathf.Max(0.01f, Mathf.Abs(Body.transform.lossyScale.x));
            var sr = go.AddComponent<SpriteRenderer>();
            if (GlowMat() != null) sr.sharedMaterial = GlowMat(); // 絵の形だけを色で塗る(暗い色のキャラクターでも色が見える。普通の色の掛け算だと黒いまま)
            sr.sortingLayerID = Body.sortingLayerID;
            sr.sortingOrder = Body.sortingOrder - 2;
            glow[i] = sr;
        }
        var rg = new GameObject("AuraRing");
        rg.transform.SetParent(transform, false);
        ring = rg.AddComponent<SpriteRenderer>();
        ring.sprite = RingSprite();
        ring.sortingLayerID = Body.sortingLayerID;
        ring.sortingOrder = RenderOrder.Enemy; // 地面より手前、体より奥
        rg.transform.localScale = new Vector3(1.1f, 0.32f, 1f);
    }

    void LateUpdate()
    {
        if (Body == null) return;
        if (PnSource != null) { int p = PnSource(); if (p > 0) Pn = p; }
        float k = GameSettings.AuraIntensity;
        bool hidden = Hidden != null && Hidden();
        Color c = ColorOf(Pn);
        float a = Mathf.Clamp01((IsLocal ? 0.9f : 0.75f) * k); // 普通の重ね方(SpriteSolid)。仲間は自分より少し控えめ
        bool show = !hidden && k > 0.01f && Body.gameObject.activeInHierarchy;
        for (int i = 0; i < glow.Length; i++)
        {
            var g = glow[i];
            if (g == null) continue;
            if (g.sprite != Body.sprite) g.sprite = Body.sprite;
            g.flipX = Body.flipX; g.flipY = Body.flipY;
            g.color = new Color(c.r, c.g, c.b, a);
            g.enabled = show && Body.sprite != null; // 体の点滅(enabled)には合わせない: オーラが残るので点滅が見える
        }
        // 足元のリング: 地面に立っている時だけ
        float feetY = transform.position.y;
        bool onGround = false;
        var tm = TerrainManager.Instance;
        if (tm != null)
        {
            float? g = tm.GetSkyHeightAt(transform.position.x, feetY + 0.3f) ?? tm.GetHeightAt(transform.position.x);
            if (g.HasValue && Mathf.Abs(feetY - g.Value) < 0.9f) { onGround = true; feetY = g.Value; }
        }
        ring.enabled = show && onGround;
        if (ring.enabled)
        {
            ring.transform.position = new Vector3(transform.position.x, feetY + 0.02f, transform.position.z + 0.01f);
            ring.color = new Color(c.r, c.g, c.b, Mathf.Clamp01((IsLocal ? 0.8f : 0.65f) * k));
        }
    }

    // 名前を出す位置: 体の当たり判定の上端(絵の上の透明な余白に左右されない。斜めの見せ方で近いと名前が離れすぎていた)。無ければ絵の上端
    public Vector3 HeadWorld
    {
        get
        {
            if (Body == null) return transform.position + Vector3.up * 2f;
            if (bodyBox == null) bodyBox = GetComponent<BoxCollider2D>();
            if (bodyBox != null && bodyBox.enabled) { var bb = bodyBox.bounds; return new Vector3(bb.center.x, bb.max.y, Body.transform.position.z); }
            if (HeadLocalY > 0f && Body.sprite != null) return new Vector3(Body.bounds.center.x, transform.position.y + HeadLocalY, Body.transform.position.z);
            if (Body.sprite != null) return new Vector3(Body.bounds.center.x, Body.bounds.max.y, Body.transform.position.z);
            return transform.position + Vector3.up * 2f;
        }
    }
    BoxCollider2D bodyBox;
    public float HeadLocalY; // 当たり判定を持たない体(マルチの相手の分身): 足元からの頭の高さ

    static Sprite ringSprite;
    static Sprite RingSprite()
    {
        if (ringSprite != null) return ringSprite;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        float c0 = (n - 1) * 0.5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float r = Mathf.Sqrt((x - c0) * (x - c0) + (y - c0) * (y - c0)) / c0;
                float band = Mathf.Clamp01(1f - Mathf.Abs(r - 0.8f) / 0.16f); // 細い輪
                float fill = r < 0.8f ? 0.18f * (1f - r / 0.8f) : 0f;         // 内側をうっすら
                byte al = (byte)(Mathf.Clamp01(band + fill) * 255f);
                px[y * n + x] = new Color32(255, 255, 255, al);
            }
        tex.SetPixels32(px); tex.Apply();
        ringSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        ringSprite.name = "aura_ring(runtime)";
        return ringSprite;
    }
}

// 名前と番号(色だけに頼らない)。自分は「YOU」、CPU は「CPU1」〜「CPU3」、人間の仲間は「P3 コードネーム」。
// IMGUI で頭の上に小さく。敵の警告(!)や HUD より奥(GUI.depth 10)。斜め上から見る表示ではそのカメラで位置を求める。
public class ParticipantLabels : MonoBehaviour
{
    public static int LastLabelFrame { get; private set; } = -100; // 確認用(2026-10-14)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<ParticipantLabels>() != null) return;
        var go = new GameObject("[ParticipantLabels]");
        DontDestroyOnLoad(go);
        go.AddComponent<ParticipantLabels>();
    }

    GUIStyle style, shadow;
    struct Tag { public ParticipantAura a; public string text; public Rect rect; }
    readonly System.Collections.Generic.List<Tag> placed = new System.Collections.Generic.List<Tag>();

    void OnGUI()
    {
        if (ParticipantAura.All.Count == 0 || Event.current.type != EventType.Repaint) return;
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted) return;
        // カードの選択/報酬/一時停止/結果/設定などの画面が出ている間は出さない(IMGUI は uGUI の画面より上に描かれるので、名前が画面に重なっていた)
        if (gm.RunScreenOpen) return; // カード選択/ポーズ/設定などの画面・結果の間は出さない(GameManager.RunScreenOpen にまとめた)
        Camera cam = ObstacleWarning.ActiveCamera();
        if (cam == null) return;
        GUI.depth = 10;
        LastLabelFrame = Time.frameCount; // 確認用(2026-10-14): 名前の札を描いたフレーム
        float s = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 0.8f, 2.4f);
        if (style == null) { style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, fontStyle = FontStyle.Bold, wordWrap = false }; shadow = new GUIStyle(style); }
        style.fontSize = shadow.fontSize = Mathf.RoundToInt(15f * s);
        // 近くに並んだ時に名前が重ならないよう、重なる分は上へ1段ずつずらす(自分 → 番号の小さい順に場所を取る)
        placed.Clear();
        foreach (var a in ParticipantAura.All)
        {
            if (a == null || a.Body == null || !a.Body.gameObject.activeInHierarchy) continue;
            if (a.Hidden != null && a.Hidden()) continue;
            Vector3 sp = cam.WorldToScreenPoint(a.HeadWorld + Vector3.up * 0.1f);
            if (sp.z <= 0f) continue;
            float x = sp.x, y = Screen.height - sp.y;
            if (x < -100f || x > Screen.width + 100f || y < -40f || y > Screen.height + 40f) continue;
            // 2026-10-14(マスター指定): 短く。自分は YOU、CPU は CPU1〜3(P2 = CPU1)。人間の仲間は番号とコードネーム
            string text = a.IsLocal ? "YOU" : a.IsCpu ? $"CPU{Mathf.Max(1, a.Pn - 1)}" : HumanLabel(a);
            float w = style.CalcSize(new GUIContent(text)).x + 6f * s;
            placed.Add(new Tag { a = a, text = text, rect = new Rect(x - w * 0.5f, y - 24f * s, w, 22f * s) });
        }
        // 重なりの解消: 同じ高さ(縦に重なる)の名前は横へ押し広げる(仲間の真上から大きく離れないよう、まとまりの中心は元の位置の平均)。
        // 横に広げても収まらない(ほぼ真上に重なった)時だけ上へずらす
        placed.Sort((p, q) => p.rect.center.x.CompareTo(q.rect.center.x));
        float gap = 4f * s;
        int start = 0;
        while (start < placed.Count)
        {
            int end = start + 1;
            while (end < placed.Count && Mathf.Abs(placed[end].rect.y - placed[start].rect.y) < placed[start].rect.height) end++;
            Spread(start, end, gap);
            start = end;
        }
        placed.Sort((p, q) => p.a.IsLocal != q.a.IsLocal ? (p.a.IsLocal ? -1 : 1) : p.a.Pn.CompareTo(q.a.Pn));
        float step = 22f * s;
        // ボス戦中は名前を薄く(ボスの HP/予兆の表示に重なっても読めるように)
        float alpha = BossBattle.AnyBossFighting ? 0.55f : 1f;
        for (int i = 0; i < placed.Count; i++)
        {
            var t = placed[i];
            for (int guard = 0; guard < 8; guard++)
            {
                bool hit = false;
                for (int j = 0; j < i; j++) if (placed[j].rect.Overlaps(t.rect)) { hit = true; break; }
                if (!hit) break;
                t.rect.y -= step;
            }
            placed[i] = t;
            Color c = ParticipantAura.ColorOf(t.a.Pn);
            var r = t.rect;
            shadow.normal.textColor = new Color(0f, 0f, 0f, 0.75f * alpha);
            GUI.Label(new Rect(r.x + 1.5f * s, r.y + 1.5f * s, r.width, r.height), t.text, shadow);
            Color tc = Color.Lerp(c, Color.white, 0.35f); tc.a = alpha;
            style.normal.textColor = tc;
            GUI.Label(r, t.text, style);
        }
    }
    // 人間の仲間: 「P3 コードネーム」。コードネームが無い人(既定の表示名 = "P3" 等)は番号だけ
    static string HumanLabel(ParticipantAura a)
    {
        string n = a.Label ?? "";
        bool numberOnly = n.Length == 0 || (n.Length >= 2 && n[0] == 'P' && int.TryParse(n.Substring(1), out _));
        return numberOnly ? $"P{a.Pn}" : $"P{a.Pn} {n}";
    }

    // placed[start..end) (左から順) を、重ならない間隔で横に並べ直す。かたまりごとに中心を元の位置の平均に合わせる
    void Spread(int start, int end, float gap)
    {
        if (end - start < 2) return;
        // かたまり(cluster)を左から作り、重なったら結合して中心を合わせ直す
        clusters.Clear();
        for (int i = start; i < end; i++)
        {
            var c = new Cluster { first = i, last = i, sumCenter = placed[i].rect.center.x, width = placed[i].rect.width };
            c.Place();
            while (clusters.Count > 0)
            {
                var prev = clusters[clusters.Count - 1];
                if (prev.left + prev.width + gap <= c.left) break;
                clusters.RemoveAt(clusters.Count - 1);
                c = new Cluster { first = prev.first, last = c.last, sumCenter = prev.sumCenter + c.sumCenter, width = prev.width + gap + c.width };
                c.Place();
            }
            clusters.Add(c);
        }
        foreach (var c in clusters)
        {
            float x = c.left;
            for (int i = c.first; i <= c.last; i++)
            {
                var t = placed[i];
                t.rect.x = x;
                x += t.rect.width + gap;
                placed[i] = t;
            }
        }
    }
    struct Cluster
    {
        public int first, last; public float sumCenter, width, left;
        public void Place() { int n = last - first + 1; left = sumCenter / n - width * 0.5f; }
    }
    readonly System.Collections.Generic.List<Cluster> clusters = new System.Collections.Generic.List<Cluster>();
}
