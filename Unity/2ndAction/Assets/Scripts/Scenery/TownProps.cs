using System.Collections.Generic;
using UnityEngine;

// 城下町の道沿いのボロボロの家(2026-11-07 マスターの依頼、絵はエステルと相談: Resources/Journey/wasteland_road/town_house_0〜4)。
//  ・景観だけ(当たり判定・障害物ではない)。道(地面)と一緒に流れる = 世界に置く物。道の奥側(地面/敵/予兆/足場より奥、内部の背景より手前)
//  ・城下町の内部(LandmarkJourney.TownInside)で、門が画面を抜けてから置き始め、出口の手前(内部が薄くなる前に画面を抜ける所)で置くのをやめる
//  ・置くのは画面の右の外だけ = 画面の中で急に出たり消えたりしない。種類・大きさ・間隔は置く時に一度だけ乱数で決め、置いた家は動かさない
//    (2〜3軒が集まる所と空き地ができる並び)。穴/上の段の足場/段差の大きい所には置かない(そこは空き地になる)
//  ・根元は足もとの道の一番低い所より少し下(坂でも浮かない)。色は内部の背景と同じ時間帯の光を半分ほど(背景の色の調整は受けない)
[DefaultExecutionOrder(111)] // LandmarkJourney(110)の後
public class TownProps : MonoBehaviour
{
    public static TownProps Instance { get; private set; }
    public static bool Enabled = true; // 確認用(-townProps 0 で家なし)
    public const int SortOrder = -41;   // 内部(-44)/門(-42)より手前、地面/敵(0〜)より奥

    // 調整値
    public static float HouseHeight = 11f;           // いちばん背の高い絵(416px)の高さ(m)。横画面の普通の寄りで画面の高さの約2〜3割(4.8m では小さすぎて模型に見えた)
    public static Vector2 HeightJitter = new Vector2(0.9f, 1.1f);
    public static float EmptyLotChance = 0.3f;
    public static Vector2 EmptyLot = new Vector2(7f, 18f), ClusterGap = new Vector2(4f, 10f), InnerGap = new Vector2(0.2f, 1.4f);
    public static float[] ClusterWeights = { 0.4f, 0.4f, 0.2f }; // 1軒 / 2軒 / 3軒
    public static float Sink = 0.3f, MaxStep = 1.8f, SpawnAhead = 6f, ExitMargin = 140f;

    const int Variants = 5;
    const float RefPixels = 416f;
    readonly Sprite[] sprites = new Sprite[Variants];
    bool loaded;
    class House { public GameObject go; public SpriteRenderer sr; public float halfW; }
    readonly List<House> houses = new List<House>();
    float nextX = float.NaN; int lastVariant = -1; bool lastWasEmpty;
    System.Random rng = new System.Random();
    public static int Placed, SkippedGround; // 確認用

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var a = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(a, "-townProps");
        if (i >= 0 && i + 1 < a.Length) Enabled = a[i + 1] != "0";
        int j = System.Array.IndexOf(a, "-mileGround");
        if (j >= 0 && j + 1 < a.Length) LandmarkJourney.MilestoneGrounded = a[j + 1] != "0";
#endif
        if (Instance != null) return;
        var go = new GameObject("[TownProps]");
        DontDestroyOnLoad(go);
        go.AddComponent<FloatingOriginExempt>();
        Instance = go.AddComponent<TownProps>();
        FloatingOrigin.Shifted += s => { if (Instance != null && !float.IsNaN(Instance.nextX)) Instance.nextX -= s; }; // 家は FloatingOrigin が一緒に戻す
        FloatingOrigin.Warped += d => { if (Instance != null) Instance.ClearAll(); };
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => { if (Instance != null) { Instance.houses.Clear(); Instance.nextX = float.NaN; } };
    }

    void LoadSprites()
    {
        if (loaded) return;
        loaded = true;
        for (int i = 0; i < Variants; i++) sprites[i] = Resources.Load<Sprite>("Journey/wasteland_road/town_house_" + i);
    }

    void ClearAll()
    {
        foreach (var h in houses) if (h.go != null) Destroy(h.go);
        houses.Clear(); nextX = float.NaN; lastVariant = -1; lastWasEmpty = false;
    }

    void LateUpdate()
    {
        var cam = Camera.main; var gm = GameManager.Instance; var tm = TerrainManager.Instance;
        if (!Enabled || cam == null || gm == null || !gm.HasStarted || tm == null) { if (houses.Count > 0 && (gm == null || !gm.HasStarted)) ClearAll(); return; }
        float left = GameView.Left(cam), right = GameView.Right(cam);
        // 画面の左の外へ出た家を片付ける / 色(時間帯の光)を合わせる
        Color tint = LandmarkJourney.TownInside ? LandmarkJourney.TownLight : lastTint;
        lastTint = tint;
        Color c = Color.Lerp(Color.white, tint, 0.55f); c.a = 1f; // 背景の絵より手前 = 光は半分ほど(背景の色の調整は受けない。薄すぎて幽霊のように見えた)
        for (int i = houses.Count - 1; i >= 0; i--)
        {
            var h = houses[i];
            if (h.go == null) { houses.RemoveAt(i); continue; }
            if (h.go.transform.position.x + h.halfW < left - 4f) { Destroy(h.go); houses.RemoveAt(i); continue; }
            h.sr.color = c;
        }
        bool spawn = LandmarkJourney.TownInside && LandmarkJourney.TownGateGone && LandmarkJourney.TownRemainMeters > (right - left) + ExitMargin;
        if (!spawn) { if (!LandmarkJourney.TownInside && houses.Count == 0) nextX = float.NaN; return; }
        LoadSprites();
        if (sprites[0] == null) return;
        if (float.IsNaN(nextX) || nextX < right) nextX = right + 2f; // 置き始め/置き残し: 必ず画面の右の外から
        int guard = 0;
        while (nextX < right + SpawnAhead && guard++ < 8)
        {
            if (!tm.IsGenerated(nextX + 12f)) break; // 地面がまだ無い(次のフレームで)
            PlaceNext(tm, c);
        }
    }
    Color lastTint = Color.white;

    float R01() => (float)rng.NextDouble();
    float Range(Vector2 v) => v.x + (v.y - v.x) * R01();

    void PlaceNext(TerrainManager tm, Color c)
    {
        if (!lastWasEmpty && R01() < EmptyLotChance) { nextX += Range(EmptyLot); lastWasEmpty = true; return; }
        lastWasEmpty = false;
        float w = R01(); int count = w < ClusterWeights[0] ? 1 : w < ClusterWeights[0] + ClusterWeights[1] ? 2 : 3;
        for (int k = 0; k < count; k++)
        {
            int v = rng.Next(Variants);
            if (v == lastVariant) v = (v + 1 + rng.Next(Variants - 1)) % Variants;
            var s = sprites[v];
            if (s == null) continue;
            float scale = HouseHeight / (RefPixels / s.pixelsPerUnit) * Range(HeightJitter); // 絵の画素の大きさをそろえる(背の低い絵は低いまま)
            float hw = s.bounds.size.x * scale * 0.5f;
            float x0 = nextX, x1 = nextX + hw * 2f;
            if (!GroundOk(tm, x0, x1, out float footY)) { SkippedGround++; nextX += 3f; lastWasEmpty = true; return; } // 置けない所 = 空き地
            var go = new GameObject("TownHouse" + v);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s; sr.sortingOrder = SortOrder;
            if (tm.backgroundRenderer != null) sr.sortingLayerID = tm.backgroundRenderer.sortingLayerID;
            sr.color = c;
            go.transform.localScale = new Vector3(scale, scale, 1f);
            float bottomOff = (s.bounds.center.y - s.bounds.min.y) * scale;
            go.transform.position = new Vector3(x0 + hw, footY - Sink + bottomOff, 0f);
            houses.Add(new House { go = go, sr = sr, halfW = hw });
            Placed++; lastVariant = v;
            nextX = x1 + Range(InnerGap);
        }
        nextX += Range(ClusterGap);
    }

    // 家の幅の下が全部道(穴/上の段の足場なし)で、段差が小さいか。根元は一番低い所
    bool GroundOk(TerrainManager tm, float x0, float x1, out float footY)
    {
        footY = 0f;
        float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
        for (float x = x0 - 0.5f; x <= x1 + 0.5f; x += 0.5f)
        {
            if (!tm.TryGetGroundFast(x, out float gh, out bool pit) || pit) return false;
            if (tm.GetSkyHeightFast(x).HasValue) return false; // 上の段の足場の下には置かない(足場を見やすく)
            lo = Mathf.Min(lo, gh); hi = Mathf.Max(hi, gh);
        }
        if (tm.IsNearPit((x0 + x1) * 0.5f, (x1 - x0) * 0.5f + 1f) || tm.IsBranchNear((x0 + x1) * 0.5f, (x1 - x0) * 0.5f + 1f)) return false;
        if (hi - lo > MaxStep) return false;
        footY = lo;
        return true;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static string Describe() => Instance == null ? "-" : $"houses {Instance.houses.Count} placed {Placed} skipped {SkippedGround}";
#endif
}
