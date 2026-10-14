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
        else if (NetRunLauncher.IsMultiplayerRun && NetPlayer.Local != null && NetPlayer.Local.PlayerNumber > 0) Attach(pc.gameObject, body, NetPlayer.Local.PlayerNumber, true, false, NetPlayer.Local.DisplayName);
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
        float k = GameSettings.AuraIntensity;
        bool hidden = Hidden != null && Hidden();
        Color c = ColorOf(Pn);
        float a = Mathf.Clamp01((IsLocal ? 0.75f : 0.55f) * k);
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

    // 頭の上(名前の表示の位置)
    public Vector3 HeadWorld => Body != null && Body.sprite != null ? new Vector3(Body.bounds.center.x, Body.bounds.max.y, Body.transform.position.z) : transform.position + Vector3.up * 2f;

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

// 名前と番号(色だけに頼らない)。自分は「YOU」、人間の仲間は「P3 コードネーム」、CPU は「P2 キャラ名 CPU」。
// IMGUI で頭の上に小さく。敵の警告(!)や HUD より奥(GUI.depth 10)。斜め上から見る表示ではそのカメラで位置を求める。
public class ParticipantLabels : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<ParticipantLabels>() != null) return;
        var go = new GameObject("[ParticipantLabels]");
        DontDestroyOnLoad(go);
        go.AddComponent<ParticipantLabels>();
    }

    GUIStyle style, shadow;

    void OnGUI()
    {
        if (ParticipantAura.All.Count == 0 || Event.current.type != EventType.Repaint) return;
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted) return;
        Camera cam = ObstacleWarning.ActiveCamera();
        if (cam == null) return;
        GUI.depth = 10;
        float s = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 0.8f, 2.4f);
        if (style == null) { style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, fontStyle = FontStyle.Bold, wordWrap = false }; shadow = new GUIStyle(style); }
        style.fontSize = shadow.fontSize = Mathf.RoundToInt(15f * s);
        foreach (var a in ParticipantAura.All)
        {
            if (a == null || a.Body == null || !a.Body.gameObject.activeInHierarchy) continue;
            if (a.Hidden != null && a.Hidden()) continue;
            Vector3 sp = cam.WorldToScreenPoint(a.HeadWorld + Vector3.up * 0.25f);
            if (sp.z <= 0f) continue;
            float x = sp.x, y = Screen.height - sp.y;
            if (x < -100f || x > Screen.width + 100f || y < -40f || y > Screen.height + 40f) continue;
            string text = a.IsLocal ? $"P{a.Pn} YOU" : a.IsCpu ? $"P{a.Pn} {a.Label} CPU" : $"P{a.Pn} {a.Label}";
            Color c = ParticipantAura.ColorOf(a.Pn);
            var r = new Rect(x - 160f * s, y - 30f * s, 320f * s, 28f * s);
            shadow.normal.textColor = new Color(0f, 0f, 0f, 0.75f);
            GUI.Label(new Rect(r.x + 1.5f * s, r.y + 1.5f * s, r.width, r.height), text, shadow);
            style.normal.textColor = Color.Lerp(c, Color.white, 0.35f);
            GUI.Label(r, text, style);
        }
    }
}
