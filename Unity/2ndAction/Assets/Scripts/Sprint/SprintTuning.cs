using UnityEngine;

// 疾走出発(2026-10-05 試作)の調整値。Resources/Sprint/SprintTuning(無ければコードの既定値)。
[CreateAssetMenu(menuName = "OneMoreMile/Sprint Tuning")]
public class SprintTuning : ScriptableObject
{
    [Header("行き先")]
    [Tooltip("行き先の刻み(m)")]
    public int stepMeters = 10000;
    [Tooltip("行き先の上限(m)。100,000m の死神/三姉妹、ラスダンの 90km〜(ボスラッシュ/静寂/最終戦)を飛び越えない")]
    public int maxDestination = 90000;
    [Tooltip("目的地の関門のこの距離(m)手前で疾走を終える(関門のボスと戦えるように)")]
    public float arriveBeforeMeters = 250f;

    [Header("演出の所要時間(リングのカード選択の時間は含まない)")]
    [Tooltip("基本の秒数")]
    public float secondsBase = 4f;
    [Tooltip("10,000m あたりの秒数")]
    public float secondsPer10km = 7f;
    [Tooltip("目的地別の上書き(10km, 20km, … 90km の順。0 = 上の式)")]
    public float[] secondsOverride = new float[9];

    [Header("自動取得の表示")]
    [Tooltip("取得したカードを画面に残す秒数")]
    public float grantFeedSeconds = 3.2f;

    [Header("リング(5000m ごと、追加のカード選択)")]
    public int ringEveryMeters = 5000;
    [Tooltip("リングの予告を出す秒数(景色の速さとは無関係の実時間)")]
    public float ringTelegraphSeconds = 2.6f;
    [Tooltip("成功判定の幅(秒): くぐる瞬間の前後にこの秒数だけ、高さが合っていれば成功")]
    public float ringWindowSeconds = 0.45f;
    [Tooltip("リングの高さの段(上/中/下)")]
    [Range(2, 3)] public int lanes = 3;
    [Tooltip("リング同士の最低の間隔(秒)。距離に対して短すぎる時は景色の流れの方を遅くする")]
    public float ringMinGapSeconds = 3.4f;

    public float SecondsFor(int destination)
    {
        int i = Mathf.Clamp(destination / Mathf.Max(1, stepMeters) - 1, 0, 8);
        if (secondsOverride != null && i < secondsOverride.Length && secondsOverride[i] > 0f) return secondsOverride[i];
        return Mathf.Max(3f, secondsBase + secondsPer10km * destination / 10000f);
    }

    static SprintTuning cached;
    static bool loaded;
    public static SprintTuning I
    {
        get
        {
            if (!loaded)
            {
                loaded = true;
                cached = Resources.Load<SprintTuning>("Sprint/SprintTuning");
                if (cached == null) { cached = CreateInstance<SprintTuning>(); cached.hideFlags = HideFlags.DontSave; }
            }
            return cached;
        }
    }
}
