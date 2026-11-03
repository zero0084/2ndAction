using UnityEngine;

// 背景の「旅の流れ」の設定(2026-11-03、エステルと相談して確定: Docs/Journey_2026-11-03)。
//  10km = 1章: 発見(0〜2km)→ 接近(2〜7)→ 目前(7〜9.5)→ 章の終わりの大ボス(門番)→ 撃破後に門をくぐる → 内部を 1.2km → 抜けると次の目的地。
//  Resources/Journey/<stageId>_journey(この asset。Inspector で直せる)。無ければ Default(stageId) のコードの既定値。絵は Resources/Journey/<stageId>/<名前>
[CreateAssetMenu(menuName = "OneMoreMile/Journey Profile")]
public class JourneyProfile : ScriptableObject
{
    [System.Serializable]
    public class Chapter
    {
        [Tooltip("この章で近づいていく目的地の絵(空 = 目的地なし)")]
        public string dest = "";
        public Color destTint = Color.white;
        public bool destFlip;
        [Tooltip("章の始めの目的地の位置(画面の割合。x = 左0〜右1、y = 下0〜上1。絵の中心)")]
        public Vector2 destStart = new Vector2(0.70f, 0.60f);
        [Tooltip("門の前(章の終わり)の位置")]
        public Vector2 destEnd = new Vector2(0.60f, 0.64f);
        [Tooltip("章の始めの目的地の高さ(画面の高さの割合)。この後 scaleCurve の倍率で大きくなる")]
        public float destHeight = 0.12f;
        [Tooltip("章の終わりの関門を突破した後にくぐる門(空 = 門なし)")]
        public string gate = "";
        public Color gateTint = Color.white;
        [Tooltip("門の後に走る内部の背景(横に繋がる絵。空 = 内部なし)")]
        public string interior = "";
        public Color interiorTint = new Color(0.82f, 0.82f, 0.86f, 1f);
        [Tooltip("章の中ほど(5km)で近くを通り過ぎる小さな目印(空 = なし)")]
        public string milestone = "";
        public Color milestoneTint = Color.white;
        [Tooltip("章の終わりで景色(SceneryCycle の A/B/C)が変わる: 変わり目を内部の間に隠す(突破するまで前の景色のまま)")]
        public bool sceneryChange;
        [Tooltip("この章の時間帯の光(昼/夕方/夜/夜明け。背景の景色の時間に合わせる)。目的地/門/内部/目印の色に掛ける")]
        public Color light = Color.white;
    }

    public Chapter[] chapters = new Chapter[0];
    public float chapterMeters = 10000f;
    [Tooltip("ここで旅を止める(90km の関門の先は今までの終盤の演出)")]
    public float endMeters = 90000f;
    [Tooltip("内部の長さ(撃破した地点から)")]
    public float interiorMeters = 1200f;
    [Tooltip("目的地の見かけの倍率(x = 章の中の km、y = 倍率)。エステルの試験案")]
    public Vector2[] scaleCurve = { new Vector2(0, 1f), new Vector2(2, 1.2f), new Vector2(4, 1.5f), new Vector2(6, 1.9f), new Vector2(8, 2.4f), new Vector2(9.5f, 3f), new Vector2(10, 3.2f) };
    [Tooltip("目的地の不透明度(x = 章の中の km)。洞窟は「奥の闇に光がにじみ出る」(2km で淡く、7km で形が分かる)")]
    public Vector2[] destAlphaCurve = { new Vector2(0, 1f) };
    [Tooltip("内部を抜けた後、次の目的地が見えてくるまでの距離")]
    public float destFadeInMeters = 400f;
    [Tooltip("門が流れる速さ(地面に対して。1 = 地面と同じ)。小さいほどゆっくりくぐる")]
    public float gateParallax = 0.7f;
    [Tooltip("門の高さ(画面の高さの割合)")]
    public float gateHeight = 1.45f;
    [Tooltip("門の中心の高さ(画面の高さの割合、上が +)。地面より上にアーチの開口部が見えるように")]
    public float gateOffsetY = 0.12f;
    public float interiorParallax = 0.5f;
    [Tooltip("内部の出口のなじませ(m)。時速100kmで 28m ≒ 1秒")]
    public float interiorFadeMeters = 120f;
    [Tooltip("内部の入口のなじませ(m)。門の中心が画面の中央を過ぎてから")]
    public float interiorFadeInMeters = 48f;
    [Tooltip("景色が変わる章(sceneryChange)の時間帯の光は、関門の手前ではなく内部の後半(この割合から出口まで)で次の章の光へ")]
    public float interiorLightFrom = 0.5f;
    public float milestoneParallax = 0.8f;
    public float milestoneHeight = 0.32f;
    [Tooltip("目印の足もとの高さ(画面の下からの割合)")]
    public float milestoneBaseY = 0.42f;
    [Tooltip("遠景(今の背景)を章の終わりまでに少しだけ大きくする(1 = しない)")]
    public float farZoomMax = 1.06f;
    [Tooltip("章の終わりのこの距離で次の章の光へ移る(背景の SceneryCycle の blendMeters と同じ)")]
    public float timeBlendMeters = 1000f;
    [Tooltip("背景の昼夜(WorldTimeCycle の NightAmount)に合わせて夜の光を掛ける(天空回廊。荒野は章ごとの light で表す)")]
    public bool followWorldNight;
    public Color nightLight = new Color(0.42f, 0.48f, 0.72f, 1f);
    [Tooltip("関門を突破したとみなす: 関門の距離をこれだけ越えて、ボス戦ではない")]
    public float clearMarginMeters = 40f;

    public static JourneyProfile Load(string stageId)
    {
        var p = Resources.Load<JourneyProfile>("Journey/" + stageId + "_journey");
        return p != null ? p : Default(stageId);
    }

    // コードの既定値(asset を作らなくても動く)。絵が無い章は何も出さない
    public static JourneyProfile Default(string stageId)
    {
        if (stageId == "natural_cave") return Cave();
        if (stageId == "sky_corridor") return Sky();
        if (stageId != "wasteland_road") return null;
        var p = CreateInstance<JourneyProfile>();
        // 時間帯の光は背景(SceneryCycle の 10km ごとの 昼→夕方→夜→夜明け)に合わせる。目的地の色(素材の違い)は destTint
        Color day = Color.white, eve = new Color(1f, 0.84f, 0.72f), night = new Color(0.5f, 0.56f, 0.76f), dawn = new Color(0.9f, 0.84f, 0.9f);
        Color stone = Color.white, worn = new Color(0.86f, 0.82f, 0.78f), grim = new Color(0.78f, 0.74f, 0.74f);
        Chapter C(string dest, Color light, Color tint, float h, float sx, bool flip = false, bool scenery = false) => new Chapter
        {
            dest = dest, destTint = tint, destHeight = h, destStart = new Vector2(sx, 0.60f), destEnd = new Vector2(sx - 0.10f, 0.64f), destFlip = flip,
            gate = "gate_castle", gateTint = Color.Lerp(tint, new Color(0.62f, 0.62f, 0.66f), 0.12f), // 門は地面よりわずかに暗く(エステル: 5〜10%)
            interior = "town_interior", interiorTint = Color.Lerp(tint, new Color(0.72f, 0.72f, 0.78f), 0.35f), light = light, sceneryChange = scenery,
            milestone = "mile_tower", milestoneTint = Color.Lerp(tint, new Color(0.7f, 0.7f, 0.74f), 0.15f), // 章の 5km で見張り塔を通り過ぎる
        };
        // 城は始めと終わりだけ。間は砦(低く横に長いシルエット)を、大きさ・左右・色で別の場所に見せる(エステルの最終案 2026-11-03)
        p.chapters = new[]
        {
            C("dest_castle", day,   stone, 0.12f, 0.70f),              //  0〜10km  城(背景Aの城に重ねる)→ 城門・城下
            C("dest_fort",   eve,   stone, 0.14f, 0.70f),              // 10〜20km  山間の砦(景色A の間は背景の城の位置に重ねて隠す)
            C("dest_fort",   night, worn,  0.13f, 0.70f, true),        // 20〜30km  古い関所(反転)
            C("dest_fort",   dawn,  stone, 0.13f, 0.70f, false, true), // 30〜40km  湖畔の村の門 → 抜けると景色B
            C("dest_fort",   day,   stone, 0.11f, 0.70f, true),        // 40〜50km  湖畔の見張り塔
            C("dest_fort",   eve,   worn,  0.12f, 0.76f),              // 50〜60km  山間の砦
            C("dest_fort",   night, grim,  0.10f, 0.72f, true),        // 60〜70km  荒れた関所
            C("dest_castle", dawn,  worn,  0.10f, 0.78f, true, true),  // 70〜80km  峡谷の城塞 → 抜けると景色C
            C("dest_castle", day,   grim,  0.14f, 0.70f),              // 80〜90km  最後の城塞(90km の関門の先は終盤の演出)
        };
        return p;
    }

    // 自然洞窟: 奥へ進むほど未知の空間へ。目的地は「奥の闇から光がにじみ出る」(2km で淡く、7km で形が分かる)。
    //  結晶の色は 青 → 紫 → 赤 と進み、終盤は光が減る(結晶の色違いは Tools/journey/hue.py で作った別の絵: 目的地・門・内部とも <名前>_blue/teal/purple/red。光の強さは light)
    static JourneyProfile Cave()
    {
        var p = CreateInstance<JourneyProfile>();
        p.destAlphaCurve = new[] { new Vector2(0, 0f), new Vector2(2, 0.15f), new Vector2(7, 0.8f), new Vector2(9.5f, 1f) };
        p.destFadeInMeters = 1f; // 光の出方は destAlphaCurve に任せる
        p.interiorFadeInMeters = 12f; p.interiorFadeMeters = 20f; // 2026-11-04 エステル: 暗い洞窟は門の直後 0.3〜0.5秒で内部へ、出口は 0.5〜0.8秒で戻す(ゆっくりだと「入った」が弱い)
        p.farZoomMax = 1.04f;
        Color lit = Color.white, dim = new Color(0.8f, 0.82f, 0.9f), dark = new Color(0.62f, 0.62f, 0.72f);
        Chapter C(string col, Color light, float h, float sx, bool flip = false) => new Chapter
        {
            dest = "dest_crystal_" + col, destHeight = h, destStart = new Vector2(sx, 0.50f), destEnd = new Vector2(sx - 0.08f, 0.53f), destFlip = flip,
            gate = "gate_ruin_" + col, gateTint = new Color(0.86f, 0.86f, 0.9f), interior = "ruin_interior_" + col, interiorTint = new Color(0.78f, 0.8f, 0.86f), light = light,
            milestone = "mile_crystal_" + col, milestoneTint = new Color(0.85f, 0.85f, 0.9f), // 章の 5km で結晶の柱を通り過ぎる
        };
        p.chapters = new[]
        {
            C("blue",   lit,  0.14f, 0.70f),        //  0〜10km  奥に青白い結晶の光 → 遺跡の石門・地下通路
            C("blue",   dim,  0.12f, 0.74f, true),  // 10〜20km  結晶洞から暗い通路へ
            C("teal",   lit,  0.13f, 0.70f),        // 20〜30km  古代遺跡の入口(地底湖へ)
            C("teal",   dim,  0.15f, 0.76f, true),  // 30〜40km  地底湖と結晶
            C("purple", lit,  0.13f, 0.72f),        // 40〜50km  紫の結晶地帯
            C("purple", dim,  0.16f, 0.70f, true),  // 50〜60km  大きな地下神殿(専用の絵が入るまで紫の結晶)
            C("red",    lit,  0.13f, 0.74f),        // 60〜70km  崩れた遺跡と深い空洞
            C("red",    dim,  0.16f, 0.70f, true),  // 70〜80km  巨大結晶の聖域
            C("red",    dark, 0.12f, 0.72f),        // 80〜90km  最深部へ、光が少なくなる
        };
        return p;
    }

    // 天空回廊: 浮遊神殿を巡る旅。遠くの神殿が近づきながら横へ流れて通り過ぎる → 門番 → 神殿のアーチ → 回廊(柱の間から空)→ 抜けると雲海が開ける。
    //  背景の昼夜(20km 周期の WorldTimeCycle)に合わせて夜の光を掛ける。章ごとの違いは大きさ・高さ・反転と素材の色(雲の色は背景のまま)
    static JourneyProfile Sky()
    {
        var p = CreateInstance<JourneyProfile>();
        p.followWorldNight = true;
        p.farZoomMax = 1.05f;
        Color white = Color.white, warm = new Color(1f, 0.94f, 0.86f), pale = new Color(0.9f, 0.92f, 1f), aged = new Color(0.84f, 0.82f, 0.86f);
        Chapter C(Color tint, float h, float sx, float sy, bool flip = false) => new Chapter
        {
            dest = "dest_temple", destTint = tint, destHeight = h, destStart = new Vector2(sx, sy), destEnd = new Vector2(sx - 0.16f, sy + 0.02f), destFlip = flip, // 横へ流れて通り過ぎる
            gate = "gate_temple", gateTint = Color.Lerp(tint, new Color(0.7f, 0.72f, 0.8f), 0.12f), interior = "temple_interior", interiorTint = Color.Lerp(tint, new Color(0.8f, 0.82f, 0.9f), 0.25f),
            milestone = "mile_tower_sky", milestoneTint = Color.Lerp(tint, new Color(0.72f, 0.74f, 0.82f), 0.15f), // 章の 5km で浮島の石塔を通り過ぎる
        };
        p.chapters = new[]
        {
            C(white, 0.10f, 0.57f, 0.64f),        //  0〜10km  遠くの浮遊神殿(背景の遠くの城 = 横57%・上から36% に重ねる)
            C(pale, 0.10f, 0.57f, 0.64f, true),  // 10〜20km  浮遊する石塔
            C(white, 0.11f, 0.57f, 0.64f),        // 20〜30km  神殿群
            C(warm, 0.12f, 0.57f, 0.64f, true),  // 30〜40km  空中の大回廊
            C(pale, 0.10f, 0.57f, 0.64f),        // 40〜50km  浮島遺跡
            C(white, 0.13f, 0.57f, 0.64f, true),  // 50〜60km  巨大天空神殿
            C(aged, 0.10f, 0.57f, 0.64f),        // 60〜70km  崩れた浮島群
            C(warm, 0.14f, 0.57f, 0.64f, true),  // 70〜80km  雲上の大聖堂(専用の絵が入るまで大きな神殿)
            C(aged, 0.12f, 0.57f, 0.64f),        // 80〜90km  天空の果ての神殿
        };
        return p;
    }

    public static float Eval(Vector2[] keys, float x)
    {
        if (keys == null || keys.Length == 0) return 1f;
        if (x <= keys[0].x) return keys[0].y;
        for (int i = 1; i < keys.Length; i++)
            if (x <= keys[i].x) return Mathf.Lerp(keys[i - 1].y, keys[i].y, Mathf.InverseLerp(keys[i - 1].x, keys[i].x, x));
        return keys[keys.Length - 1].y;
    }
}
