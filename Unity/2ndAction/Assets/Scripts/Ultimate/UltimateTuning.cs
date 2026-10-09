using UnityEngine;

// #100 ULTIMATE(2026-10-04)の調整値。Resources/Ultimate/UltimateTuning.asset があればそれを使い、無ければコードの既定値。
// 値はすべて仮(マスターの実機確認で決める)。Lv は 1〜9。Lv1/Lv5/Lv9 の3点を決めて間は直線で結ぶ(At)。
[CreateAssetMenu(menuName = "OneMoreMile/Ultimate Tuning", fileName = "UltimateTuning")]
public class UltimateTuning : ScriptableObject
{
    [Header("Gauge(%、100で発動できる)")]
    [Tooltip("走った距離 1m ごと(%)")] public float gaugePerMeter = 0.05f;
    [Tooltip("距離から入る量の上限(%/秒)。高速の距離だけで連発しないように")] public float gaugeDistanceMaxPerSecond = 0.55f;
    [Tooltip("通常の敵を倒した時(%)")] public float gaugePerKill = 1.2f;
    [Tooltip("精鋭(ELITE/WANTED)を倒した時(%)")] public float gaugePerEliteKill = 4f;
    [Tooltip("ボスに与えたダメージ: ボスのHP1本分で何%か")] public float gaugePerBossBar = 30f;
    [Tooltip("ボスへの1発で入る上限(%)")] public float gaugeBossHitMax = 3f;
    [Tooltip("ボスを倒した時(%)")] public float gaugePerBossKill = 20f;
    [Tooltip("Lv が1上がるごとの溜まりやすさ(+割合)")] public float gaugeLevelBonus = 0.04f;
    [Tooltip("BUFF 中の溜まり方(倍率)。発動中は常に0")] public float gaugeDuringBuff = 0.5f;

    [Header("突破(前進)の距離(m) Lv1 / Lv5 / Lv9")]
    // 2026-10-09(依頼H-A 第2段階): 100/150/200 → 300/600/1000
    public Vector3 advance = new Vector3(300f, 600f, 1000f);
    [Tooltip("(旧)前進にかける時間(秒)。今は dashSecondsByLevel を使う")] public float dashSeconds = 1.9f;
    [Tooltip("突破にかける時間(秒) Lv1 / Lv5 / Lv9。長い距離ほど少し長く(速さが極端にならないように)")] public Vector3 dashSecondsByLevel = new Vector3(1.9f, 2.4f, 3.0f);
    [Tooltip("次のボス関門の何m手前で止めるか")] public float stopBeforeGate = 30f;
    [Tooltip("着地の後、敵/障害物を出さない距離(m)")] public float landingSafeMeters = 25f;

    [Header("殲滅(発動した場所の通常の敵を一撃で倒す、2026-10-09)")]
    [Tooltip("判定の画面(GameView)の右端より何m先まで")] public float annihilateAheadMargin = 4f;
    [Tooltip("プレイヤーより何m上まで(飛ぶ敵)")] public float annihilateUp = 18f;
    [Tooltip("プレイヤーより何m下まで")] public float annihilateDown = 10f;
    [Tooltip("突破の道筋の敵を報酬なしで消す範囲: 着地点の何m先まで")] public float passClearBeyond = 40f;

    [Header("(旧)通常の敵へのダメージ = その距離の HP倍率1 の雑魚のHP × 倍率(Lv1 / Lv5 / Lv9)。2026-10-09 から通常の敵は殲滅(一撃)なので使わない")]
    public Vector3 mobDamage = new Vector3(1.8f, 2.8f, 4.0f);
    [Tooltip("カードの攻撃力(A)が効く割合: 1 + この値 × SoftAttack(A)")] public float buildAttackShare = 0.35f;
    [Tooltip("カードの攻撃力による倍率の上限")] public float buildFactorMax = 1.8f;
    [Tooltip("1体あたりのダメージを何回に分けるか(ゲーム上のダメージの回数)")] public int mobDamageEvents = 2;
    [Tooltip("1回目に入る割合(前進で画面から外れた敵にも大部分が入るように)。残りは画面に残っていれば次の当たりで")] public float mobFirstShare = 0.75f;

    [Header("ボス: 最大HPの何割(Lv1 / Lv5 / Lv9)。1回で戦闘を終わらせない")]
    // 2026-10-09(依頼H-A): 10/14/18% → 20/30/40%。カードの攻撃力では増やさない(この割合がそのまま上限)
    public Vector3 bossDamageFraction = new Vector3(0.20f, 0.30f, 0.40f);
    [Tooltip("1回の ULTIMATE でボスに入る上限(最大HPの割合、ULTIMATE 専用)")] public float bossDamageCap = 0.40f;
    [Tooltip("ボス戦: ボスを通り抜けた先(ボスの端から m)")] public float arenaPassBeyond = 3f;
    [Tooltip("ボス戦: 戻る位置(ボスの手前の端から m)")] public float arenaReturnGap = 6f;

    [Header("BUFF(余韻) Lv1 / Lv5 / Lv9")]
    public Vector3 buffSeconds = new Vector3(5f, 8f, 12f);
    public Vector3 buffAttack = new Vector3(0.10f, 0.15f, 0.20f);
    public Vector3 buffAttackSpeed = new Vector3(0.08f, 0.12f, 0.15f);
    public Vector3 buffRunSpeed = new Vector3(0.06f, 0.09f, 0.12f);

    [Header("守り")]
    [Tooltip("終わった後も接触/ノックバックを受けない時間(秒)")] public float afterProtectSeconds = 0.8f;

    [Header("演出の長さ(秒)")]
    public float cutStartup = 0.45f;
    public float cutBurst = 0.45f;
    public float cutFinish = 0.5f;
    [Tooltip("前進中の当たり判定の間隔(秒)")] public float pulseInterval = 0.35f;

    // Lv1/5/9 の3点を直線で結ぶ
    public static float At(Vector3 v, int lv)
    {
        lv = Mathf.Clamp(lv, 1, 9);
        return lv <= 5 ? Mathf.Lerp(v.x, v.y, (lv - 1) / 4f) : Mathf.Lerp(v.y, v.z, (lv - 5) / 4f);
    }

    static UltimateTuning cached;
    static bool loaded;
    public static UltimateTuning I
    {
        get
        {
            if (!loaded || cached == null)
            {
                loaded = true;
                cached = Resources.Load<UltimateTuning>("Ultimate/UltimateTuning");
                if (cached == null) { cached = CreateInstance<UltimateTuning>(); cached.hideFlags = HideFlags.DontSave; }
            }
            return cached;
        }
    }
}
