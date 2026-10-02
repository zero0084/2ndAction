#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 距離で進む昼夜と景色(2026-10-01)の確認。
//  -qaScenery <dir>       … 予定表の計算 / 各景色×時間帯の撮影 / 移り変わり途中 / 夜の見やすさ / 距離ワープ / リトライ /
//                            全境目を高速で通過(継ぎ目・逆戻り・読み込みの引っかかり・メモリ)
//  -qaSceneryVideo <dir>  … A夜明け→B昼の移り変わりを実際に走って毎フレーム書き出す(動画用)
public partial class QaSweep
{
    IEnumerator SceneryMode()
    {
        Application.targetFrameRate = 60;
        // ---- 1) 予定表の計算(画面に関係なく)
        var prof = Resources.Load<SceneryProfile>("Scenery/wasteland_road_scenery");
        Check(prof != null, "scenery profile exists");
        if (prof == null) yield break;
        (float d, string want)[] cases =
        {
            (0f, "A 昼"), (8999f, "A 昼"), (9500f, "A 昼"), (10000f, "A 夕方"), (19999f, "A 夕方"), (25000f, "A 夜"), (35000f, "A 夜明け"),
            (40000f, "B 昼"), (55000f, "B 夕方"), (65000f, "B 夜"), (75000f, "B 夜明け"), (85000f, "C 昼"), (95000f, "C 夕方"),
            (105000f, "C 夜"), (115000f, "C 夜明け"), (120000f, "A 昼"), (130000f, "A 夕方"), (245000f, "A 昼"),
        };
        foreach (var c in cases)
        {
            int s = prof.SegmentAt(c.d, out float a, out float b);
            Check(prof.Describe(s) == c.want, $"segment at {c.d}m = {prof.Describe(s)} (want {c.want}, {a}-{b})");
        }
        L($"[plan] loop={prof.TotalLength}m blend={prof.blendMeters}m segments={prof.segments.Length}");

        // ---- 2) 実際のRun: 開始=A昼、旧夜レイヤー停止
        yield return BeginRun("swordsman", "wasteland_road");
        PrepSceneryRun();
        yield return new WaitForSeconds(1f);
        Check(SceneryCycle.Active, "SceneryCycle active on wasteland_road");
        Check(SceneryCycle.CurrentSegment == 0, $"run starts at segment 0 (got {SceneryCycle.CurrentSegment} {SceneryCycle.CurrentName})");
        var wtc = WorldTimeCycle.Instance;
        if (wtc != null && wtc.nightLayer != null) Check(!wtc.nightLayer.enabled, "old WorldTimeCycle night layer is off while SceneryCycle runs");
        L($"[start] {SceneryCycle.CurrentName} loaded={SceneryCycle.LoadedCount} tex={SceneryCycle.LoadedTextureBytes / 1024}KB");
        Shot("run_start_A_day"); yield return null; yield return null;

        // ---- 3) 各景色×時間帯(背景だけのプレビュー)と移り変わり途中
        int n = SceneryCycle.SegmentCount;
        for (int i = 0; i < n; i++)
        {
            SceneryCycle.DebugPreviewDistance = SceneryCycle.DistanceForSegment(i);
            yield return null; yield return null; yield return null;
            var seg = prof.segments[i];
            Shot($"pure_{prof.sceneries[seg.scenery].id}_{(int)seg.time}{seg.time}");
            yield return null; yield return null;
        }
        foreach (int i in new[] { 0, 1, 2, 3, 7 })
            foreach (float bl in new[] { 0.25f, 0.5f, 0.75f })
            {
                SceneryCycle.DebugPreviewDistance = SceneryCycle.DistanceForSegment(i, bl);
                yield return null; yield return null; yield return null;
                Shot($"blend_{i:00}_{Mathf.RoundToInt(bl * 100)}");
                yield return null; yield return null;
            }
        SceneryCycle.DebugPreviewDistance = null;
        yield return null;

        // ---- 4) 夜の見やすさ(実際に敵/障害物/穴が出ている所)
        foreach (float d in new[] { 25000f, 65000f, 105000f, 4000f, 45000f, 85000f })
        {
            gm.DebugWarpToDistance(d);
            yield return new WaitForSeconds(5f);
            Shot($"play_{d / 1000f:0}k_{SceneryCycle.CurrentName.Replace(' ', '_')}");
            yield return null; yield return null;
            // 同じ場面(敵/障害物/穴がある)のまま、背景だけ夕方/夜/夜明けにして撮る(見やすさの比較)
            int baseSeg = SceneryCycle.CurrentSegment;
            if (prof.segments[baseSeg].time == SceneryTime.Day)
                for (int k = 1; k <= 3; k++)
                {
                    SceneryCycle.DebugPreviewDistance = SceneryCycle.DistanceForSegment(baseSeg + k);
                    yield return null; yield return null;
                    Shot($"play_{d / 1000f:0}k_as_{SceneryCycle.CurrentName.Replace(' ', '_')}");
                    yield return null; yield return null;
                }
            SceneryCycle.DebugPreviewDistance = null;
        }

        // ---- 5) 距離ワープ: どこへ飛んでも1フレームで正しい区間
        foreach (float d in new[] { 72000f, 3000f, 118500f, 39400f, 9100f })
        {
            gm.DebugWarpToDistance(d);
            yield return null; yield return null;
            int want = prof.SegmentAt(gm.MaxDistance, out _, out _);
            Check(SceneryCycle.CurrentSegment == want, $"after warp to {d}: segment {SceneryCycle.CurrentSegment} == {want} ({SceneryCycle.CurrentName})");
            Check(SceneryCycle.LoadedCount <= 4, $"after warp to {d}: loaded {SceneryCycle.LoadedCount} <= 4");
        }

        // ---- 6) リトライで最初(A昼)に戻る
        yield return EndRun();
        yield return BeginRun("swordsman", "wasteland_road");
        PrepSceneryRun();
        yield return new WaitForSeconds(0.5f);
        Check(SceneryCycle.CurrentSegment == 0 && SceneryCycle.CurrentBlend == 0f, $"after retry: segment 0 (got {SceneryCycle.CurrentName})");
        Check(SceneryCycle.LoadedCount <= 2, $"after retry: loaded {SceneryCycle.LoadedCount} <= 2");
        var bg = TerrainManager.Instance.backgroundRenderer;
        Check(bg != null && bg.sprite != null && bg.sprite.name == "WastelandBackground", $"after retry: base background is the original ({(bg != null && bg.sprite != null ? bg.sprite.name : "null")})");

        // ---- 7) 全部の境目を高速で通過: 移り具合が逆戻りしない/継ぎ目(絵の入れ替わりの瞬間の色の跳び)/読み込みの引っかかり/メモリ
        SetKmh(float.Parse(Arg("-qaSceneryKmh", "320")));
        int syncBefore = 0;
        long maxTex = 0; int maxLoaded = 0;
        float worstFrame = 0f, worstBlendFrame = 0f;
        var frameTimes = new List<float>();
        var blendFrameTimes = new List<float>();
        for (int k = 1; k <= n; k++)
        {
            float boundary = k * 10000f;
            gm.DebugWarpToDistance(boundary - 1600f);
            SetKmh(float.Parse(Arg("-qaSceneryKmh", "320")));
            yield return new WaitForSeconds(0.5f);
            syncBefore = SceneryCycle.SyncLoadCount;
            float lastBlend = -1f; int lastSeg = SceneryCycle.CurrentSegment;
            Color lastPix = BgColorProbe();
            float maxJump = 0f;
            float w = 0f;
            while (gm.MaxDistance < boundary + 150f && w < 60f)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime;
                w += dt;
                frameTimes.Add(dt);
                if (SceneryCycle.CurrentBlend > 0f) blendFrameTimes.Add(dt);
                worstFrame = Mathf.Max(worstFrame, dt);
                if (SceneryCycle.CurrentBlend > 0f) worstBlendFrame = Mathf.Max(worstBlendFrame, dt);
                int seg = SceneryCycle.CurrentSegment;
                float bl = SceneryCycle.CurrentBlend;
                if (seg == lastSeg && bl + 1e-4f < lastBlend) Check(false, $"blend went backwards near {boundary}: {lastBlend:F3} -> {bl:F3}");
                Color pix = BgColorProbe();
                if (seg != lastSeg) maxJump = Mathf.Max(maxJump, ColorDist(pix, lastPix));
                lastPix = pix; lastBlend = bl; lastSeg = seg;
                maxTex = System.Math.Max(maxTex, SceneryCycle.LoadedTextureBytes);
                maxLoaded = Mathf.Max(maxLoaded, SceneryCycle.LoadedCount);
            }
            int wantSeg = prof.SegmentAt(gm.MaxDistance, out _, out _);
            Check(SceneryCycle.CurrentSegment == wantSeg, $"crossed {boundary}: now {SceneryCycle.CurrentName}");
            Check(SceneryCycle.SyncLoadCount == syncBefore, $"crossed {boundary}: no synchronous load while running (sync {SceneryCycle.SyncLoadCount - syncBefore})");
            Check(maxJump < 0.02f, $"crossed {boundary}: no visible jump at the swap moment (color delta {maxJump:F4})");
            L($"[cross] {boundary}m -> {SceneryCycle.CurrentName}  swapJump={maxJump:F4} lastAsyncLoad={SceneryCycle.LastAsyncLoadSeconds * 1000f:F0}ms loaded={SceneryCycle.LoadedCount}");
        }
        frameTimes.Sort(); blendFrameTimes.Sort();
        float p99 = frameTimes.Count > 0 ? frameTimes[(int)(frameTimes.Count * 0.99f)] : 0f;
        float bp99 = blendFrameTimes.Count > 0 ? blendFrameTimes[(int)(blendFrameTimes.Count * 0.99f)] : 0f;
        L($"[perf] frames={frameTimes.Count} p99={p99 * 1000f:F1}ms worst={worstFrame * 1000f:F1}ms | during blends: frames={blendFrameTimes.Count} p99={bp99 * 1000f:F1}ms worst={worstBlendFrame * 1000f:F1}ms");
        L($"[mem] scenery textures resident max={maxLoaded} ({maxTex / 1024f / 1024f:F2}MB), syncLoadsTotal={SceneryCycle.SyncLoadCount} (warps only), lastSyncLoad={SceneryCycle.LastSyncLoadMs:F0}ms");
        Check(maxLoaded <= 3, $"at most 3 scenery textures resident while running (max {maxLoaded})");
        yield return EndRun();
    }

    void PrepSceneryRun()
    {
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
    }

    // 背景だけの色(画面上部の空の中央付近)を、背景の2枚(元+重ね)の合成として計算する(画面の読み取りはしない)
    Color BgColorProbe()
    {
        var bg = TerrainManager.Instance != null ? TerrainManager.Instance.backgroundRenderer : null;
        if (bg == null || bg.sprite == null) return Color.black;
        Color a = SampleSprite(bg.sprite, 0.5f, 0.45f) * bg.color;
        var ov = GameObject.Find("SceneryOverlay");
        var osr = ov != null ? ov.GetComponent<SpriteRenderer>() : null;
        if (osr != null && osr.enabled && osr.sprite != null)
        {
            Color b = SampleSprite(osr.sprite, 0.5f, 0.45f) * osr.color;
            a = Color.Lerp(a, new Color(b.r, b.g, b.b, 1f), osr.color.a);
        }
        return a;
    }

    readonly Dictionary<Texture2D, Color> sampleCache = new Dictionary<Texture2D, Color>();
    Color SampleSprite(Sprite s, float u, float v)
    {
        var tex = s.texture;
        if (tex == null) return Color.black;
        if (sampleCache.TryGetValue(tex, out Color c)) return c;
        // 読み取り不可のテクスチャなので、小さなRenderTextureへ写して平均色を取る
        var rt = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(tex, rt);
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var t2 = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        t2.ReadPixels(new Rect(0, 0, 16, 16), 0, 0); t2.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        c = t2.GetPixel(Mathf.FloorToInt(u * 15), Mathf.FloorToInt(v * 15));
        Destroy(t2);
        sampleCache[tex] = c;
        return c;
    }

    static float ColorDist(Color a, Color b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));

    // 他のステージは従来どおりか(荒野街道でB夜まで進めた後に別ステージを開始)。 -qaSceneryStages <dir>
    IEnumerator SceneryStagesMode()
    {
        Application.targetFrameRate = 60;
        foreach (string st in new[] { "sky_corridor", "natural_cave", "last_corridor", "wasteland_road" })
        {
            yield return BeginRun("swordsman", "wasteland_road");
            PrepSceneryRun();
            gm.DebugWarpToDistance(65000f);
            yield return new WaitForSeconds(1f);
            var bg = TerrainManager.Instance.backgroundRenderer;
            string before = bg != null && bg.sprite != null ? bg.sprite.name : "null";
            Check(SceneryCycle.Active && before == "scn_B_night", $"wasteland at 65k shows B night (got {before})");
            yield return EndRun();
            yield return BeginRun("swordsman", st);
            PrepSceneryRun();
            yield return new WaitForSeconds(1.5f);
            bg = TerrainManager.Instance.backgroundRenderer;
            string now = bg != null && bg.sprite != null ? bg.sprite.name : "null";
            var wtc = WorldTimeCycle.Instance;
            bool nightOn = wtc != null && wtc.nightLayer != null && wtc.nightLayer.enabled;
            var ov = GameObject.Find("SceneryOverlay");
            bool ovOn = ov != null && ov.GetComponent<SpriteRenderer>().enabled;
            L($"[stage] {st}: background={now} sceneryActive={SceneryCycle.Active} overlay={ovOn} oldNightLayerEnabled={nightOn} loaded={SceneryCycle.LoadedCount}");
            if (st == "wasteland_road") Check(SceneryCycle.Active && now == "WastelandBackground" && !ovOn, "wasteland new run starts at A day");
            else
            {
                Check(!SceneryCycle.Active && !ovOn && SceneryCycle.LoadedCount == 0, $"{st}: scenery system idle");
                Check(!now.StartsWith("scn_"), $"{st}: background is not a wasteland scenery image ({now})");
                if (st == "sky_corridor") Check(nightOn, "sky_corridor keeps the old day/night layer");
            }
            Shot($"stage_{st}");
            yield return null; yield return null;
            yield return EndRun();
        }
    }

    // A夜明け→B昼(39,000〜40,000m)を実際に走って毎フレーム書き出す。Time.captureFramerate で30fps固定。
    IEnumerator SceneryVideoMode()
    {
        float from = float.Parse(Arg("-qaVideoFrom", "38850"));
        float to = float.Parse(Arg("-qaVideoTo", "40100"));
        float kmh = float.Parse(Arg("-qaVideoKmh", "200"));
        yield return BeginRun("swordsman", "wasteland_road");
        PrepSceneryRun();
        gm.expPerMeter = 0f; // 撮影中はレベルアップのカード選択で背景が隠れないように
        yield return new WaitForSeconds(1f);
        gm.DebugWarpToDistance(from);
        SetKmh(kmh);
        yield return new WaitForSeconds(1.5f);
        Time.captureFramerate = 30;
        int f = 0;
        string vdir = System.IO.Path.Combine(outDir, "video");
        System.IO.Directory.CreateDirectory(vdir);
        while (gm.MaxDistance < to && f < 2400)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(vdir, $"f{f++:00000}.png"));
            if (f % 30 == 0) L($"[video] f={f} d={gm.MaxDistance:F0} {SceneryCycle.CurrentName}");
        }
        Time.captureFramerate = 0;
        L($"[video] frames={f}");
        yield return EndRun();
    }
}
#endif
