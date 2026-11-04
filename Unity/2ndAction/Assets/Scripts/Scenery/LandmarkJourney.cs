using System.Collections.Generic;
using UnityEngine;

// 背景の「旅の流れ」(2026-11-03、エステルと相談して確定: Docs/Journey_2026-11-03)。設定は JourneyProfile。
//  3層: 遠景 = 今の背景(章の終わりへ向けて最大 farZoomMax 倍)/ 中景 = 目的地(章の中の km で scaleCurve の倍率、左へ少し寄る)/
//       近景 = 門(撃破後に gateParallax の速さで流れてくぐる)・内部(横に繋がる絵、撃破地点から interiorMeters)・目印(章の 5km)。
//  関門の突破は「関門の距離を clearMarginMeters 越えて、ボス戦ではない」(ボス戦の間は距離が止まる)。CONTINUE/ワープで途中から始まった時は
//  その距離と章から作り直す(内部の途中なら内部から)。並び順は元の背景(-100)の少し上〜雲(-45)の上・-40 より奥(地面/敵/弾には重ねない)。
//  荒野の景色の変わり目(40/80km)は、内部が画面を覆うまで前の景色のまま(SceneryCycle が SceneryDistance を使う)。
[DefaultExecutionOrder(110)] // BackgroundFollower(100)の後
public class LandmarkJourney : MonoBehaviour
{
    public static LandmarkJourney Instance { get; private set; }
    public static bool Enabled = true; // 確認用(-journey 0 で旧来の見た目)
    public static bool Active => Instance != null && Instance.profile != null;

    const int DestOrder = -97, InteriorOrder = -44, MilestoneOrder = -43, GateOrder = -42;

    JourneyProfile profile; string stage = "";
    SpriteRenderer dest, gate, milestone; SpriteRenderer[] interior = new SpriteRenderer[3];
    readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
    // 章ごとの突破した距離(その章の終わりの関門)。-1 = まだ
    readonly Dictionary<int, float> clearedAt = new Dictionary<int, float>();
    readonly Dictionary<int, float> milestoneAt = new Dictionary<int, float>();
    float lastD = -1f, clearCandidateT;
    public static string State { get; private set; } = "-";
    public static float InteriorAlpha { get; private set; }
    // 関門のボスを倒してから門が画面を抜けるまで(BONUS ZONE はこの間は始めない: 門をくぐる演出を主役に。2026-11-04 エステル)
    //  門が出るのは突破の確定(関門 + clearMargin)の後なので、その手前の「突破待ち」も含める。90km の関門(終盤の演出)は含めない
    public static bool GateVisible { get; private set; }
    bool gateWasOn;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var a = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(a, "-journey");
        if (i >= 0 && i + 1 < a.Length) Enabled = a[i + 1] != "0";
#endif
        if (Instance != null) return;
        var go = new GameObject("[LandmarkJourney]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<LandmarkJourney>();
        TerrainManager.ThemeApplying += tm => { if (Instance != null) Instance.Deactivate(); };
        TerrainManager.ThemeApplied += (tm, stageId) => { if (Instance != null) Instance.Activate(tm, stageId); };
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => { if (Instance != null) Instance.Deactivate(); };
    }

    void Activate(TerrainManager tm, string stageId)
    {
        Deactivate();
        if (!Enabled || tm == null || tm.backgroundRenderer == null || string.IsNullOrEmpty(stageId)) return;
        var p = JourneyProfile.Load(stageId);
        if (p == null || p.chapters == null || p.chapters.Length == 0) return;
        profile = p; stage = stageId;
        int layer = tm.backgroundRenderer.sortingLayerID;
        dest = MakeRenderer("JourneyDest", layer, DestOrder, true); // 遠くの物なので背景と同じ見やすさの調整(彩度/コントラストを控えめに)
        gate = MakeRenderer("JourneyGate", layer, GateOrder, true);
        milestone = MakeRenderer("JourneyMilestone", layer, MilestoneOrder, true);
        for (int i = 0; i < interior.Length; i++) interior[i] = MakeRenderer("JourneyInterior" + i, layer, InteriorOrder, true);
        Debug.Log($"[Journey] Activate stage={stageId} chapters={p.chapters.Length} end={p.endMeters:F0}m interior={p.interiorMeters:F0}m");
    }

    SpriteRenderer MakeRenderer(string name, int layer, int order, bool tone)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.AddComponent<FloatingOriginExempt>();
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingLayerID = layer; sr.sortingOrder = order; sr.enabled = false;
        if (ZigRoad.NoZigMaterial != null) sr.sharedMaterial = ZigRoad.NoZigMaterial;
        if (tone) BackgroundTone.Adopt(sr); // 近景は背景の見やすさの調整(彩度/コントラスト)を受ける
        return sr;
    }

    void Deactivate()
    {
        profile = null; stage = "";
        foreach (Transform c in transform) Destroy(c.gameObject);
        dest = gate = milestone = null; for (int i = 0; i < interior.Length; i++) interior[i] = null;
        foreach (var kv in sprites) if (kv.Value != null) Resources.UnloadAsset(kv.Value.texture);
        sprites.Clear(); clearedAt.Clear(); milestoneAt.Clear();
        lastD = -1f; State = "-"; InteriorAlpha = 0f; GateVisible = false;
        BackgroundFollower.JourneyZoom = 1f;
    }

    Sprite Get(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (sprites.TryGetValue(name, out var s)) return s;
        s = Resources.Load<Sprite>("Journey/" + stage + "/" + name);
        sprites[name] = s;
        if (s == null) Debug.Log($"[Journey] missing sprite Journey/{stage}/{name}");
        return s;
    }

    JourneyProfile.Chapter Ch(int c) => profile != null && c >= 0 && c < profile.chapters.Length ? profile.chapters[c] : null;
    float GateD(int c) => (c + 1) * profile.chapterMeters; // 章 c の終わりの関門

    // 章 c の終わりの関門を突破した距離(突破していなければ -1)
    float Cleared(int c) => clearedAt.TryGetValue(c, out float v) ? v : -1f;

    void LateUpdate()
    {
        if (profile == null) return;
        var gm = GameManager.Instance;
        var cam = Camera.main;
        if (gm == null || cam == null || !gm.HasStarted) { HideAll(); State = "idle"; return; }
        using (FrameCost.Scope("Journey")) Tick(gm, cam);
    }

    void HideAll()
    {
        if (dest != null) dest.enabled = false;
        if (gate != null) gate.enabled = false;
        if (milestone != null) milestone.enabled = false;
        foreach (var r in interior) if (r != null) r.enabled = false;
        BackgroundFollower.JourneyZoom = 1f; InteriorAlpha = 0f; GateVisible = false;
    }

    void Tick(GameManager gm, Camera cam)
    {
        float d = SceneryCycle.DistanceNow();
        bool boss = BossManager.Instance != null && BossManager.Instance.IsBossPhase;
        float L = profile.chapterMeters;
        int ch = Mathf.FloorToInt(d / L);
        // 途中から始まった(CONTINUE/ワープ/最初のフレーム): 今より前の関門は突破済み(今の章の始まりの関門は始まりで突破 = 内部の途中なら内部から)
        if (lastD < 0f || Mathf.Abs(d - lastD) > 400f)
        {
            for (int c = 0; c < ch; c++) if (!clearedAt.ContainsKey(c)) clearedAt[c] = GateD(c);
            Debug.Log($"[Journey] start/jump {lastD:F0} -> {d:F0}m: gates before chapter {ch} treated as cleared");
        }
        lastD = d;
        // 章の始まりの関門(= 章 ch-1 の終わり)の突破: ボス戦の間は距離が止まる。越えてボス戦でなければ突破
        bool holding = ch >= 1 && !clearedAt.ContainsKey(ch - 1);
        if (holding)
        {
            if (!boss && d >= GateD(ch - 1) + profile.clearMarginMeters) { clearCandidateT += Time.deltaTime; if (clearCandidateT > 0.15f) { clearedAt[ch - 1] = d; holding = false; Debug.Log($"[Journey] gate {GateD(ch - 1) / 1000f:F0}km cleared at {d:F0}m"); } }
            else clearCandidateT = 0f;
        }

        float screenH = cam.orthographicSize * 2f, screenW = screenH * cam.aspect * FoldView.CoverScaleX;
        Vector3 cp = cam.transform.position;
        string state = holding ? "holding " : "";

        // ---- 門と内部: 章の始まりの関門を突破した後
        var prev = Ch(ch - 1);
        float sinceClear = !holding && ch >= 1 ? d - Cleared(ch - 1) : -1f;
        bool endGate = ch >= 1 && GateD(ch - 1) >= profile.endMeters - 1f; // 90km の関門の先は終盤の演出(門も内部もなし)
        float interiorA = 0f;
        if (prev != null && sinceClear >= 0f && !endGate)
        {
            Sprite gs = Get(prev.gate);
            float enterAt = 0f;
            if (gs != null)
            {
                float gh = screenH * profile.gateHeight, gsc = gh / gs.bounds.size.y, gw = gs.bounds.size.x * gsc;
                float par = Mathf.Max(0.05f, profile.gateParallax);
                float travel = (screenW + gw) / par;
                enterAt = (screenW * 0.5f + gw * 0.5f) / par; // 門の中心が画面の中央を過ぎたら内部へ(門が切り替わりを隠す)
                if (sinceClear < travel)
                {
                    float gx = screenW * 0.5f + gw * 0.5f - sinceClear * par;
                    gate.sprite = gs; gate.color = prev.gateTint * LightAt(d); gate.enabled = true;
                    gate.transform.position = new Vector3(cp.x + gx, cp.y + profile.gateOffsetY * screenH, 0f);
                    gate.transform.localScale = new Vector3(gsc, gsc, 1f);
                    state += $"gate {sinceClear:F0}/{travel:F0}m ";
                }
                else gate.enabled = false;
            }
            else gate.enabled = false;
            Sprite isp = Get(prev.interior);
            interiorA = isp != null ? InteriorAlphaAt(sinceClear - enterAt, profile.interiorMeters - enterAt) : 0f;
            DrawInterior(isp, prev.interiorTint * LightAt(d), interiorA, d, cp, screenW, screenH);
            if (interiorA > 0f) state += $"interior {interiorA:F2} ";
        }
        else { if (gate != null) gate.enabled = false; DrawInterior(null, Color.white, 0f, d, cp, screenW, screenH); }
        InteriorAlpha = interiorA;
        GateVisible = !endGate && (holding || (gate != null && gate.enabled));
        // 2026-11-04: 門をくぐる時の風の音(門が画面に入った時に1回。小さく)
        bool gateOn = gate != null && gate.enabled;
        if (gateOn && !gateWasOn) SkyBossSfx.Play(SkyBossSfx.Whoosh(), 0.4f);
        gateWasOn = gateOn;

        // ---- 目的地: 突破までは前の章の目的地を門の前の大きさで。突破の直後 40m で消え、内部を抜けたら次の目的地が見えてくる
        int dc; float rKm, destA;
        if (holding) { dc = ch - 1; rKm = L / 1000f; destA = 1f; }
        else if (sinceClear >= 0f && sinceClear < 40f) { dc = ch - 1; rKm = L / 1000f; destA = 1f - sinceClear / 40f; }
        else
        {
            dc = ch; rKm = Mathf.Clamp((d - ch * L) / 1000f, 0f, L / 1000f);
            float exitD = ch * L;
            if (ch >= 1 && prev != null && !string.IsNullOrEmpty(prev.interior) && !endGate) exitD = Cleared(ch - 1) + profile.interiorMeters;
            destA = ch == 0 ? 1f : Mathf.Clamp01((d - exitD) / Mathf.Max(1f, profile.destFadeInMeters));
            destA *= Mathf.Clamp01(JourneyProfile.Eval(profile.destAlphaCurve, rKm));
        }
        var cur = Ch(dc);
        Sprite ds = cur != null && GateD(dc) <= profile.endMeters + 1f ? Get(cur.dest) : null;
        if (ds != null)
        {
            float k = Mathf.SmoothStep(0f, 1f, rKm / (L / 1000f));
            float mul = JourneyProfile.Eval(profile.scaleCurve, rKm);
            float sc = screenH * cur.destHeight * mul / ds.bounds.size.y;
            Vector2 v = Vector2.Lerp(cur.destStart, cur.destEnd, k);
            dest.sprite = ds; dest.flipX = cur.destFlip;
            dest.transform.position = new Vector3(cp.x + (v.x - 0.5f) * screenW, cp.y + (v.y - 0.5f) * screenH, 0f);
            dest.transform.localScale = new Vector3(sc, sc, 1f);
            Color c = cur.destTint * LightAt(d); c.a = cur.destTint.a * destA * (1f - interiorA);
            dest.color = c; dest.enabled = c.a > 0.003f;
            if (dest.enabled) state += $"dest{dc} r={rKm:F1}km x{mul:F2} a={c.a:F2} ";
            BackgroundFollower.JourneyZoom = Mathf.Lerp(1f, profile.farZoomMax, k);
        }
        else { if (dest != null) dest.enabled = false; BackgroundFollower.JourneyZoom = 1f; }

        // ---- 目印(章の 5km。5km のボスがいればその後)
        DrawMilestone(ch, d, boss, cp, screenW, screenH, ref state);

        State = $"ch{ch} {state.Trim()}".Trim();
    }

    // 時間帯の光(章ごと)。章の終わりの timeBlendMeters で次の章の光へ(背景の昼→夕方の移り変わりと同じ所)。ボス戦で関門に止まっている間は次の章の光
    //  景色が変わる章の終わり(荒野 40/80km)は、背景が関門まで前の景色の時間帯のままなので、光も関門までは前の章のまま →
    //  内部の後半(interiorLightFrom〜出口)で次の章の光へ(出口で新しい土地の時間帯が開ける。2026-11-04 エステル)
    Color LightAt(float d)
    {
        float L = profile.chapterMeters; int c = Mathf.FloorToInt(d / L);
        var a = Ch(c); var b = Ch(c + 1); var p = Ch(c - 1);
        Color la = a != null ? a.light : (p != null ? p.light : Color.white), lb = b != null ? b.light : la;
        float t = a != null && a.sceneryChange ? 0f : Mathf.InverseLerp(GateD(c) - profile.timeBlendMeters, GateD(c), d);
        Color r = Color.Lerp(la, lb, t);
        if (p != null && p.sceneryChange && a != null)
        {
            float cl = Cleared(c - 1);
            if (cl < 0f) r = p.light; // 関門で止まっている間(ボス戦)
            else if (d < cl + profile.interiorMeters)
                r = Color.Lerp(p.light, a.light, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cl + profile.interiorMeters * profile.interiorLightFrom, cl + profile.interiorMeters, d)));
        }
        if (profile.followWorldNight && WorldTimeCycle.Instance != null) r *= Color.Lerp(Color.white, profile.nightLight, WorldTimeCycle.Instance.NightAmount);
        r.a = 1f; return r;
    }

    float InteriorAlphaAt(float s, float len)
    {
        float fo = Mathf.Max(1f, profile.interiorFadeMeters), fi = Mathf.Max(1f, profile.interiorFadeInMeters);
        if (s < 0f || s > len) return 0f;
        return Mathf.Clamp01(Mathf.Min(s / fi, (len - s) / fo)); // 入口は門の陰で速く、出口はゆっくり開ける
    }

    void DrawInterior(Sprite s, Color tint, float a, float d, Vector3 cp, float screenW, float screenH)
    {
        if (s == null || a <= 0.003f) { foreach (var r in interior) if (r != null) r.enabled = false; return; }
        float sc = screenH / s.bounds.size.y, w = s.bounds.size.x * sc;
        float off = Mathf.Repeat(d * profile.interiorParallax, w);
        float left = -screenW * 0.5f - off;
        for (int i = 0; i < interior.Length; i++)
        {
            var r = interior[i];
            float x = left + w * (i + 0.5f);
            bool need = x - w * 0.5f < screenW * 0.5f;
            r.enabled = need;
            if (!need) continue;
            r.sprite = s;
            r.transform.position = new Vector3(cp.x + x, cp.y, 0f);
            r.transform.localScale = new Vector3(sc * 1.002f, sc, 1f); // 継ぎ目の1画素の隙間を防ぐ
            Color c = tint; c.a = a; r.color = c;
        }
    }

    void DrawMilestone(int ch, float d, bool boss, Vector3 cp, float screenW, float screenH, ref string state)
    {
        var cur = Ch(ch);
        Sprite s = cur != null ? Get(cur.milestone) : null;
        if (s == null || milestone == null) { if (milestone != null) milestone.enabled = false; return; }
        float mark = ch * profile.chapterMeters + profile.chapterMeters * 0.5f;
        if (!milestoneAt.ContainsKey(ch) && d >= mark && !boss) milestoneAt[ch] = d;
        if (!milestoneAt.TryGetValue(ch, out float at)) { milestone.enabled = false; return; }
        float h = screenH * profile.milestoneHeight, sc = h / s.bounds.size.y, w = s.bounds.size.x * sc;
        float x = screenW * 0.5f + w * 0.5f - (d - at) * profile.milestoneParallax;
        if (x < -screenW * 0.5f - w * 0.5f) { milestone.enabled = false; return; }
        milestone.sprite = s; milestone.color = cur.milestoneTint * LightAt(d); milestone.enabled = true;
        milestone.transform.position = new Vector3(cp.x + x, cp.y + (profile.milestoneBaseY - 0.5f) * screenH + h * 0.5f, 0f);
        milestone.transform.localScale = new Vector3(sc, sc, 1f);
        state += "milestone ";
    }

    // SceneryCycle 用: 景色が変わる章の終わりは、内部が画面を覆うまで前の景色のまま(変わり目を内部の間に隠す)
    public static float SceneryDistance(float d)
    {
        if (!Active || !Enabled) return d;
        var p = Instance.profile; float L = p.chapterMeters;
        var sp = SceneryCycle.Profile;
        float blend = sp != null ? sp.blendMeters : 1000f;
        int ch = Mathf.FloorToInt((d + blend) / L) - 1; // d が章 ch の終わりの関門の手前 blend m 〜 関門の先
        var c = Instance.Ch(ch);
        if (c == null || !c.sceneryChange) return d;
        float gd = Instance.GateD(ch);
        if (d < gd - blend) return d;
        float cl = Instance.Cleared(ch);
        bool covered = cl >= 0f && (d > cl + Instance.profile.interiorMeters * 0.5f || InteriorAlpha >= 0.98f);
        return covered ? Mathf.Max(d, gd + 1f) : gd - blend - 1f;
    }

    public static string Describe() => !Enabled ? "off" : Active ? State : "-";
}
