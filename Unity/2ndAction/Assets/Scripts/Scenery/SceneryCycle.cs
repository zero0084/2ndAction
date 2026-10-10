using System.Collections.Generic;
using UnityEngine;

// 走行距離で進む昼夜と景色(2026-10-01)。SceneryProfile を持つステージ(今は荒野街道)だけ背景を担当する。
//  ・毎フレーム「距離→区間→次の区間への移り具合」を計算し直すだけ(状態を溜めない)。距離ワープ/リトライ/再開でも即座に正しい見た目になる。
//  ・背景は従来の1枚絵(TerrainManager.backgroundRenderer、カメラ追従+画面を覆う拡大)。同じ構図で描いた時間帯/景色の絵を
//    その真上の1枚(overlay)に重ねて濃さを上げるだけなので、位置ずれ/二重像/スクロールの戻り/端の隙間が起きない。
//  ・景色が変わる時(夜明け→次の景色の昼)は、境目の手前で「朝もや」の層をかぶせながら入れ替える(暗転はしない)。
//  ・背景の層だけを変える(Player/敵/障害物/穴/地面/HUD/カードは別の描画で、暗くならない)。
//  ・画像は今の区間/次の区間/もうすぐ始まる区間の分だけ Resources から非同期で読み、要らなくなったら解放する。
//  ・旧 WorldTimeCycle(天空回廊の昼↔夜)は、このクラスが担当している間は夜の層を止め、NightAmount をこちらの値で出す(二重に暗くならない)。
//  ・距離は「カメラが追っているPlayer」のもの(マルチでも各自の距離。他人を見ている時はその人の距離)。時間/当たり判定/同期には関与しない。
[DefaultExecutionOrder(90)] // BackgroundFollower(100)より前に絵を決める
public class SceneryCycle : MonoBehaviour
{
    public static SceneryCycle Instance { get; private set; }
    public static bool Active => Instance != null && Instance.profile != null;

    // 天気/雲など、背景に合わせて色を変えるもの向け
    public static float NightAmount => Active ? Instance.nightAmount : 0f;
    public static Color CloudTint => Active ? Instance.cloudTint : Color.white;
    public static string CurrentName => Active ? Instance.currentName : "";

    // 開発用: 背景だけをこの距離として表示する(ゲームの距離は変えない)。null=通常
    public static float? DebugPreviewDistance;

    SceneryProfile profile;
    SpriteRenderer baseBg, overlay, mist;
    Sprite themeSprite;
    Color themeTint = Color.white;
    float nightAmount;
    Color cloudTint = Color.white;
    string currentName = "";
    string loggedName = "";

    // 読み込み済み/読み込み中の画像(パス→)
    readonly Dictionary<string, Sprite> loaded = new Dictionary<string, Sprite>();
    readonly Dictionary<string, ResourceRequest> loading = new Dictionary<string, ResourceRequest>();
    readonly Dictionary<string, float> loadStartedAt = new Dictionary<string, float>();
    readonly HashSet<string> wanted = new HashSet<string>();
    readonly List<string> scratch = new List<string>();

    // 計測(開発用の表示/QaSweep)
    public static int SyncLoadCount { get; private set; }
    public static float LastSyncLoadMs { get; private set; }
    public static float LastAsyncLoadSeconds { get; private set; }
    public static int LoadedCount => Active ? Instance.loaded.Count : 0;
    public static long LoadedTextureBytes
    {
        get
        {
            if (Instance == null) return 0;
            long b = 0;
            foreach (var kv in Instance.loaded) if (kv.Value != null && kv.Value.texture != null) b += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(kv.Value.texture);
            return b;
        }
    }
    public static int CurrentSegment { get; private set; } = -1;
    public static float CurrentBlend { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[SceneryCycle]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<SceneryCycle>();
        TerrainManager.ThemeApplying += tm => { if (Instance != null) Instance.Deactivate(); };
        TerrainManager.ThemeApplied += (tm, stageId) => { if (Instance != null) Instance.OnThemeApplied(tm, stageId); };
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => { if (Instance != null) Instance.Deactivate(); };
    }

    void OnThemeApplied(TerrainManager tm, string stageId)
    {
        Deactivate();
        if (tm == null || string.IsNullOrEmpty(stageId)) return;
        var p = Resources.Load<SceneryProfile>("Scenery/" + stageId + "_scenery");
        if (p == null || p.segments == null || p.segments.Length == 0) return;
        baseBg = tm.backgroundRenderer;
        if (baseBg == null) return;
        profile = p;
        themeSprite = baseBg.sprite;
        themeTint = baseBg.color;
        EnsureLayers();
        Debug.Log($"[Scenery] Activate stage={stageId} segments={p.segments.Length} loop={p.TotalLength:F0}m blend={p.blendMeters:F0}m");
        Apply(DistanceNow(), true);
    }

    // テーマ切り替えの直前/シーン読み込み: 元の背景へ戻して、読み込んだ画像を解放する
    void Deactivate()
    {
        if (profile != null && baseBg != null)
        {
            baseBg.sprite = themeSprite;
            baseBg.color = themeTint;
        }
        profile = null;
        if (overlay != null) overlay.enabled = false;
        if (mist != null) mist.enabled = false;
        foreach (var kv in loaded) UnloadSprite(kv.Value);
        loaded.Clear();
        loading.Clear(); // 読み込み中のものは完了しても参照しない(Unityが後で回収する)
        loadStartedAt.Clear();
        CurrentSegment = -1;
        currentName = ""; loggedName = "";
        nightAmount = 0f; cloudTint = Color.white;
    }

    void EnsureLayers()
    {
        var bf = baseBg.GetComponent<BackgroundFollower>();
        Camera cam = bf != null && bf.cam != null ? bf.cam : Camera.main;
        if (overlay == null)
        {
            var go = new GameObject("SceneryOverlay");
            DontDestroyOnLoad(go);
            go.AddComponent<FloatingOriginExempt>();
            overlay = go.AddComponent<SpriteRenderer>();
            go.AddComponent<BackgroundFollower>();
        }
        if (mist == null)
        {
            var go = new GameObject("SceneryMist");
            DontDestroyOnLoad(go);
            go.AddComponent<FloatingOriginExempt>();
            mist = go.AddComponent<SpriteRenderer>();
            mist.sprite = BuildMistSprite();
            go.AddComponent<BackgroundFollower>();
        }
        overlay.GetComponent<BackgroundFollower>().cam = cam;
        mist.GetComponent<BackgroundFollower>().cam = cam;
        // 元の背景(-100)の真上。ゲーム中の物はすべてこれより手前。
        overlay.sortingLayerID = baseBg.sortingLayerID;
        mist.sortingLayerID = baseBg.sortingLayerID;
        overlay.sortingOrder = baseBg.sortingOrder + 1;
        mist.sortingOrder = baseBg.sortingOrder + 2;
        overlay.enabled = false;
        mist.enabled = false;
    }

    // 先に読んでおく距離(疾走出発の到着地点。2026-10-10)。その区間と次の区間の背景を非同期で読み、到着まで解放しない
    public static float? ExtraPrefetchDistance;

    void LateUpdate() { using (FrameCost.Scope("Scenery")) LateUpdateMeasured(); }
    void LateUpdateMeasured()
    {
        if (profile == null) return;
        if (baseBg == null) { Deactivate(); return; }
        Apply(DistanceNow(), false);
    }

    // カメラが追っているPlayerの距離。ホーム(Run開始前)は0=最初の景色の昼。
    public static float DistanceNow()
    {
        if (DebugPreviewDistance.HasValue) return DebugPreviewDistance.Value;
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted) return 0f;
        var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cf != null && cf.target != null && PlayerController.Instance != null && cf.target != PlayerController.Instance.transform)
        {
            var avatar = cf.target.GetComponentInParent<RemotePlayerAvatar>();
            if (avatar != null && avatar.Owner != null) return (float)avatar.Owner.RemoteDistance;
        }
        return gm.MaxDistance;
    }

    void Apply(float d, bool force)
    {
        int seg = profile.SegmentAt(d, out float segStart, out float segEnd);
        int next = profile.NextSegment(seg);
        float blendLen = Mathf.Clamp(profile.blendMeters, 1f, Mathf.Max(1f, segEnd - segStart));
        float t = Mathf.Clamp01((d - (segEnd - blendLen)) / blendLen);
        // ゆっくり始まりゆっくり終わる(境目のはっきりした瞬間を作らない)
        float te = t * t * (3f - 2f * t);
        CurrentSegment = seg;
        CurrentBlend = t;

        string curPath = profile.ResourceFor(seg);
        string nextPath = profile.ResourceFor(next);

        // 読み込み計画: 今/次 + 先読み範囲に始まる区間
        wanted.Clear();
        wanted.Add(curPath);
        wanted.Add(nextPath);
        float look = segEnd;
        int k = next;
        for (int guard = 0; guard < 8 && look < d + profile.prefetchMeters + blendLen; guard++)
        {
            if (k < 0) break;
            look += Mathf.Max(1f, profile.segments[k].length);
            k = profile.NextSegment(k);
            wanted.Add(profile.ResourceFor(k));
        }
        if (ExtraPrefetchDistance.HasValue)
        {
            int es = profile.SegmentAt(ExtraPrefetchDistance.Value, out _, out _);
            wanted.Add(profile.ResourceFor(es));
            wanted.Add(profile.ResourceFor(profile.NextSegment(es)));
        }
        RequestLoads();

        Sprite cur = Need(curPath, true);
        Sprite nxt = te > 0.0001f ? Need(nextPath, true) : Need(nextPath, false);
        if (cur == null) cur = themeSprite;
        if (nxt == null) nxt = te > 0.0001f ? themeSprite : null;

        var segA = profile.segments[seg];
        var segB = profile.segments[next];
        Color tintA = TintFor(segA.time), tintB = TintFor(segB.time);

        if (baseBg.sprite != cur) baseBg.sprite = cur;
        baseBg.color = tintA;
        bool showOverlay = te > 0.0001f && nxt != null && (nxt != cur || tintA != tintB);
        overlay.enabled = showOverlay;
        if (showOverlay)
        {
            if (overlay.sprite != nxt) overlay.sprite = nxt;
            overlay.color = new Color(tintB.r, tintB.g, tintB.b, te);
        }

        // 景色が変わる時だけ朝もや(移り変わりの中ほどで一番濃い)
        bool sceneryChange = segA.scenery != segB.scenery;
        float m = sceneryChange ? Mathf.Sin(t * Mathf.PI) : 0f;
        m = m * m * (3f - 2f * m);
        float mistA = profile.mistPeakAlpha * m;
        mist.enabled = mistA > 0.002f;
        if (mist.enabled) { Color mc = profile.mistColor; mc.a = mistA; mist.color = mc; }

        nightAmount = Mathf.Lerp(Pick(profile.nightAmount, (int)segA.time, 0f), Pick(profile.nightAmount, (int)segB.time, 0f), te);
        cloudTint = Color.Lerp(PickC(profile.cloudTint, (int)segA.time), PickC(profile.cloudTint, (int)segB.time), te);

        string name = t <= 0.0001f ? profile.Describe(seg) : $"{profile.Describe(seg)}→{profile.Describe(next)} {Mathf.RoundToInt(t * 100f)}%";
        currentName = name;
        string coarse = t <= 0.0001f ? profile.Describe(seg) : (t >= 0.999f ? profile.Describe(next) : $"{profile.Describe(seg)}→{profile.Describe(next)}");
        if (coarse != loggedName)
        {
            loggedName = coarse;
            Debug.Log($"[Scenery] {coarse} @ {d:F0}m (loaded={loaded.Count} loading={loading.Count} tex={LoadedTextureBytes / 1024}KB)");
        }
    }

    Color TintFor(SceneryTime time)
    {
        int i = (int)time;
        if (profile.timeTint != null && i < profile.timeTint.Length && profile.timeTint[i].a > 0f) return profile.timeTint[i];
        return themeTint;
    }

    static float Pick(float[] a, int i, float def) => a != null && i < a.Length ? a[i] : def;
    static Color PickC(Color[] a, int i) => a != null && i < a.Length && a[i].a > 0f ? a[i] : Color.white;

    void RequestLoads()
    {
        foreach (string path in wanted)
        {
            if (string.IsNullOrEmpty(path) || loaded.ContainsKey(path) || loading.ContainsKey(path)) continue;
            loading[path] = Resources.LoadAsync<Sprite>(path);
            loadStartedAt[path] = Time.realtimeSinceStartup;
        }
        // 読み込みが終わったもの
        scratch.Clear();
        foreach (var kv in loading) if (kv.Value.isDone) scratch.Add(kv.Key);
        foreach (string path in scratch)
        {
            var sp = loading[path].asset as Sprite;
            loading.Remove(path);
            if (sp != null) loaded[path] = sp;
            if (loadStartedAt.TryGetValue(path, out float t0)) { LastAsyncLoadSeconds = Time.realtimeSinceStartup - t0; loadStartedAt.Remove(path); }
            Debug.Log($"[Scenery] loaded {path} in {LastAsyncLoadSeconds * 1000f:F0}ms (async)");
        }
        // 要らなくなったもの(今/次/先読みのどれでもない)を解放
        scratch.Clear();
        foreach (var kv in loaded) if (!wanted.Contains(kv.Key)) scratch.Add(kv.Key);
        foreach (string path in scratch)
        {
            if (baseBg != null && baseBg.sprite == loaded[path]) continue;
            if (overlay != null && overlay.sprite == loaded[path]) { overlay.sprite = null; }
            UnloadSprite(loaded[path]);
            loaded.Remove(path);
            Debug.Log($"[Scenery] unloaded {path}");
        }
    }

    // 今すぐ表示に要る画像。読み込みが間に合っていなければ(距離ワープ直後など)その場で読む。
    Sprite Need(string path, bool required)
    {
        if (string.IsNullOrEmpty(path)) return themeSprite;
        if (loaded.TryGetValue(path, out Sprite sp)) return sp;
        if (!required) return null;
        float t0 = Time.realtimeSinceStartup;
        if (loading.TryGetValue(path, out ResourceRequest rq))
        {
            sp = rq.asset as Sprite; // 完了を待つ
            loading.Remove(path);
        }
        else sp = Resources.Load<Sprite>(path);
        loadStartedAt.Remove(path);
        LastSyncLoadMs = (Time.realtimeSinceStartup - t0) * 1000f;
        SyncLoadCount++;
        Debug.Log($"[Scenery] loaded {path} in {LastSyncLoadMs:F0}ms (sync - distance jump or no time to prefetch)");
        if (sp != null) loaded[path] = sp;
        return sp;
    }

    void UnloadSprite(Sprite sp)
    {
        if (sp == null || sp == themeSprite) return;
        if (baseBg != null && baseBg.sprite == sp) baseBg.sprite = themeSprite;
        if (overlay != null && overlay.sprite == sp) overlay.sprite = null;
        Texture2D tex = sp.texture;
        Resources.UnloadAsset(sp);
        if (tex != null) Resources.UnloadAsset(tex);
    }

    // 朝もや: 下半分〜地平線あたりが濃く、上へ向かって薄くなる、ゆるい横ムラのある霧(実行時に作る小さな画像)
    static Sprite BuildMistSprite()
    {
        const int W = 256, H = 144;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        {
            float v = y / (float)(H - 1); // 0=下
            // 地平線(画面の下から約55%)付近を中心に厚い帯、下端はやや薄く、上は空まで薄く残す
            float band = Mathf.Exp(-Mathf.Pow((v - 0.5f) / 0.22f, 2f));
            float baseFog = Mathf.Lerp(0.55f, 0.18f, Mathf.Clamp01((v - 0.2f) / 0.8f));
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)W;
                float n = Mathf.PerlinNoise(u * 3.2f + 7.1f, v * 2.2f + 1.3f) * 0.65f + Mathf.PerlinNoise(u * 7.5f + 2.4f, v * 5.0f + 8.8f) * 0.35f;
                float a = Mathf.Clamp01((band * 0.85f + baseFog) * Mathf.Lerp(0.7f, 1.15f, n));
                px[y * W + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // ===== 開発用 =====
    public static int SegmentCount => Active ? Instance.profile.segments.Length : 0;
    public static SceneryProfile Profile => Active ? Instance.profile : null;

    // 区間 i の「純粋な」見た目(移り変わりの前)になる距離
    public static float DistanceForSegment(int i, float blend01 = 0f)
    {
        if (!Active) return 0f;
        var p = Instance.profile;
        i = Mathf.Clamp(i, 0, p.segments.Length - 1);
        float start = 0f;
        for (int j = 0; j < i; j++) start += Mathf.Max(1f, p.segments[j].length);
        float len = Mathf.Max(1f, p.segments[i].length);
        float bl = Mathf.Clamp(p.blendMeters, 1f, len);
        if (blend01 <= 0f) return start + Mathf.Min(len * 0.5f, len - bl - 1f);
        return start + len - bl + bl * Mathf.Clamp01(blend01);
    }

    // 次の境目(移り変わりの始まり)の距離
    public static float NextBlendStart(float d)
    {
        if (!Active) return d;
        var p = Instance.profile;
        p.SegmentAt(d, out float s, out float e);
        float b = e - Mathf.Clamp(p.blendMeters, 1f, e - s);
        if (d >= b) { p.SegmentAt(e + 0.5f, out s, out e); b = e - Mathf.Clamp(p.blendMeters, 1f, e - s); }
        return b;
    }
#endif
}
