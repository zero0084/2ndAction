using UnityEngine;

// 走行距離で進む「景色×時間帯」の予定表(2026-10-01)。ステージごとに1つ、Resources/Scenery/<stageId>_scenery に置く。
// 置いたステージだけ SceneryCycle が背景を担当する(無いステージ=天空回廊/洞窟/ラストダンジョンは従来どおり)。
//  ・sceneries … 景色(A/B/C…)。時間帯ごとの背景画像を Resources のパスで持つ(必要な時だけ読み込む=全部を常駐させない)。
//    パスが空 = そのステージの元の背景(TerrainThemeSet.backgroundSprite)を使う。
//  ・segments  … 区間の並び(景色/時間帯/長さm)。最後まで行ったら loopFromSegment から繰り返す。
//  ・blendMeters … 区間の境目の何m手前から次の区間へ移り始めるか(境目ちょうどで次の区間100%)。
public enum SceneryTime { Day = 0, Evening = 1, Night = 2, Dawn = 3 }

[CreateAssetMenu(menuName = "OneMoreMile/Scenery Profile")]
public class SceneryProfile : ScriptableObject
{
    public string stageId;

    [System.Serializable]
    public class Scenery
    {
        public string id = "A";
        public string label = "荒野";
        // [Day, Evening, Night, Dawn] の Resources パス(拡張子なし)。空=元の背景。
        public string[] timeResources = new string[4];
    }

    [System.Serializable]
    public struct Segment
    {
        public int scenery;
        public SceneryTime time;
        public float length;
    }

    public Scenery[] sceneries;
    public Segment[] segments;
    public int loopFromSegment = 0;

    [Header("Transition")]
    public float blendMeters = 1000f;
    // 景色が変わる時(A夜明け→B昼など)の朝もや。境目の手前 blend の中ほどで一番濃い。
    public float mistPeakAlpha = 0.6f;
    public Color mistColor = new Color(1f, 0.96f, 0.9f, 1f);
    // 先読み: 今の区間・次の区間に加え、この距離以内に始まる区間の画像も読み込んでおく
    public float prefetchMeters = 2500f;

    [Header("Look per time (Day, Evening, Night, Dawn)")]
    // 背景への乗算。alpha=0 ならステージの元の backgroundTint を使う。
    public Color[] timeTint = new Color[4];
    // 雲(ForegroundCloudLayer)の色
    public Color[] cloudTint = { Color.white, new Color(1f, 0.82f, 0.72f, 1f), new Color(0.55f, 0.62f, 0.85f, 1f), new Color(0.95f, 0.88f, 0.9f, 1f) };
    // 夜らしさ(雲の流れ/濃さを抑える度合い。WorldTimeCycle.NightAmount として公開)
    public float[] nightAmount = { 0f, 0.3f, 1f, 0.45f };

    public static readonly string[] TimeLabels = { "昼", "夕方", "夜", "夜明け" };

    public float TotalLength
    {
        get { float t = 0f; if (segments != null) foreach (var s in segments) t += Mathf.Max(1f, s.length); return t; }
    }

    // 距離 d → 区間の番号と、その区間の開始/終了距離(繰り返しを含めた実距離)
    public int SegmentAt(float d, out float segStart, out float segEnd)
    {
        segStart = 0f; segEnd = 0f;
        if (segments == null || segments.Length == 0) return -1;
        d = Mathf.Max(0f, d);
        float firstPass = TotalLength;
        int loopFrom = Mathf.Clamp(loopFromSegment, 0, segments.Length - 1);
        float baseOffset = 0f;
        int startIndex = 0;
        if (d >= firstPass)
        {
            float loopLen = 0f;
            for (int i = loopFrom; i < segments.Length; i++) loopLen += Mathf.Max(1f, segments[i].length);
            float over = d - firstPass;
            int laps = Mathf.FloorToInt(over / loopLen);
            baseOffset = firstPass + laps * loopLen;
            startIndex = loopFrom;
        }
        float x = baseOffset;
        for (int i = startIndex; i < segments.Length; i++)
        {
            float len = Mathf.Max(1f, segments[i].length);
            if (d < x + len || i == segments.Length - 1) { segStart = x; segEnd = x + len; return i; }
            x += len;
        }
        return segments.Length - 1;
    }

    public int NextSegment(int i)
    {
        if (segments == null || segments.Length == 0) return -1;
        return i + 1 < segments.Length ? i + 1 : Mathf.Clamp(loopFromSegment, 0, segments.Length - 1);
    }

    public string ResourceFor(int segIndex)
    {
        if (segIndex < 0 || segments == null || segIndex >= segments.Length) return "";
        var seg = segments[segIndex];
        if (sceneries == null || seg.scenery < 0 || seg.scenery >= sceneries.Length) return "";
        var res = sceneries[seg.scenery].timeResources;
        int t = (int)seg.time;
        return res != null && t < res.Length && res[t] != null ? res[t] : "";
    }

    public string Describe(int segIndex)
    {
        if (segIndex < 0 || segments == null || segIndex >= segments.Length) return "-";
        var seg = segments[segIndex];
        string sc = sceneries != null && seg.scenery >= 0 && seg.scenery < sceneries.Length ? sceneries[seg.scenery].id : "?";
        return $"{sc} {TimeLabels[(int)seg.time]}";
    }
}
