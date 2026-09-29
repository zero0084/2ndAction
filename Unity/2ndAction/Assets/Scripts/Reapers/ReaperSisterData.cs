using System;
using UnityEngine;

// 死神三姉妹(2026-09-29)の1人分のデータ。Resources/Reapers/<Eldest|Second|Youngest>Data.asset。
// 見た目(絵・アニメの速さ・浮遊/スキップの量)と、100,000m以降の追跡の調整値をまとめて持つ。
// 追跡の速さとアニメの速さは完全に別物: プレイヤーが何km/hで走っていても、姉妹の歩き/浮遊/スキップの
// テンポはここで決めた一定の値のまま(「必死に追っていないのに追いつかれる」異質さ)。
// 将来のラストダンジョン戦(三人同時の通常ボス)で使う攻撃/被弾/撃破の絵もここへ足していく想定。
public enum ReaperSister { Eldest, Second, Youngest }
public enum ReaperMotion { Walk, Float, Skip }

[CreateAssetMenu(menuName = "OneMoreMile/Reaper Sister Data", fileName = "ReaperSisterData")]
public class ReaperSisterData : ScriptableObject
{
    [Header("誰か")]
    public ReaperSister sister;
    public string displayName = "";
    [Tooltip("担当ステージ(GameManager.ActiveRunStageId)")] public string stageId = "";

    [Header("絵(右向き、足元基準)")]
    public Sprite idle;
    [Tooltip("移動のコマ(長女=歩き / 次女=浮遊 / 三女=スキップ)")] public Sprite[] moveFrames = new Sprite[0];
    [Tooltip("将来用: 攻撃の構え/攻撃/被弾/撃破(今回は未使用)")] public Sprite[] attackPrepareFrames = new Sprite[0];
    public Sprite[] attackFrames = new Sprite[0];
    public Sprite[] hitFrames = new Sprite[0];
    public Sprite[] deathFrames = new Sprite[0];
    [Tooltip("画面上の身長(ワールド単位)。プレイヤーは約1.6")] public float heightWorld = 2.6f;
    [Tooltip("絵が無い時の仮表示の色(既存の死神の絵に掛ける)")] public Color placeholderTint = Color.white;

    [Header("アニメーション(走行速度とは無関係に一定)")]
    public ReaperMotion motion = ReaperMotion.Walk;
    [Tooltip("移動のコマ送りの速さ(コマ/秒)")] public float moveFps = 6f;
    [Tooltip("歩き: 一歩ごとの上下(m)")] public float walkBob = 0.05f;
    [Tooltip("歩き: 体の揺れ(度)")] public float walkSwayDeg = 1.2f;
    [Tooltip("浮遊: 地面からの高さ(m)")] public float floatHeight = 0.35f;
    [Tooltip("浮遊: 上下のゆれ幅(m)と周期(回/秒)")] public float bobAmount = 0.12f;
    public float bobFrequency = 0.55f;
    [Tooltip("スキップ: 1秒あたりの跳ねる回数")] public float skipRate = 2.2f;
    [Tooltip("スキップ: 跳ねる高さ(m)")] public float skipHeight = 0.28f;
    [Tooltip("スキップ: 何回に1回、ふわっと長く浮くか(0=しない)")] public int skipFloatEvery = 4;
    [Tooltip("スキップ: ふわっと浮く時の高さの倍率")] public float skipFloatHeightMul = 2.4f;
    [Tooltip("出現時に姿が濃くなるまでの秒数")] public float fadeInTime = 1.6f;

    [Header("追跡")]
    public ReaperChaseSettings chase = new ReaperChaseSettings();
}

// 追跡の調整値(姉妹ごと)。「どれくらい逃げられるか」「何秒後にまた来るか」はここで調整する。
[Serializable]
public class ReaperChaseSettings
{
    [Tooltip("出現: 死神の開始演出から姿を見せ始めるまで(秒)")] public float appearDelay = 1.6f;
    [Tooltip("出現: 画面左端のさらに外側、何m後ろから来るか")] public float appearOffscreenMargin = 3f;
    [Tooltip("出現: 近づいてくる速さ(プレイヤーに対して、m/s)")] public float appearApproachSpeed = 2.4f;
    [Tooltip("落ち着く位置: プレイヤーから画面左端までの距離に対する割合(0=プレイヤー位置、1=画面左端)")]
    [Range(0.05f, 1f)] public float targetScreenFraction = 0.6f;
    [Tooltip("通常時にじわじわ詰める速さ(m/s)")] public float closeSpeed = 0.4f;
    [Tooltip("登場から1分ごとに詰める速さが何倍分増えるか")] public float closeSpeedGrowthPerMinute = 0.6f;
    [Tooltip("加速への追従の遅れ(秒)。大きいほど急加速で引き離しやすい")] public float accelFollowTime = 1.5f;
    [Tooltip("減速への追従の遅れ(秒)。小さいほど減速した時に急に迫ってこない")] public float decelFollowTime = 0.25f;
    [Tooltip("この割合より後ろ(画面左端付近/画面外)に下がったら『引き離された』とみなす")]
    [Range(0.3f, 2f)] public float escapeScreenFraction = 0.9f;
    [Tooltip("引き離されてから再追跡を始めるまで(秒)")] public float reacquireDelay = 2.5f;
    [Tooltip("再追跡の強さが最大になるまで(秒)")] public float reacquireRampTime = 1.5f;
    [Tooltip("再追跡の強さ(1/秒): 落ち着く位置までの残り距離×この値の速さで詰める")] public float reacquireStrength = 0.75f;
    [Tooltip("再追跡の最大の相対速度(m/s)")] public float maxCatchupSpeed = 70f;
    [Tooltip("これより離れたら待たずに最大の強さで再追跡(m)")] public float hardLeashDistance = 250f;
    [Tooltip("プレイヤーが被弾で止まっている間に詰める速さ(m/s、プレイヤーの実際の速さに加える)")] public float stunCloseSpeed = 4f;
    [Tooltip("標的がこれ以上一気に後ろへ戻ったら(復帰など)、引き戻さずに消えて左から現れ直す(m)")] public float reappearBackDistance = 8f;
    [Tooltip("捕捉する距離(m)")] public float captureGap = 1.3f;
    [Tooltip("捕捉してから大鎌を振るまで(秒)")] public float captureWindup = 0.45f;
    [Tooltip("大鎌が外れた時、次に振るまで(秒)")] public float captureRetry = 1.0f;
    [Tooltip("シングルプレイ: 捕捉の一撃でRun終了(false=通常の被弾1回)")] public bool captureLethalSolo = true;
}
