using UnityEngine;

// Enemy FINISH System(2026-10-06)の調整値。Resources/Finish/FinishTuning(無ければコードの既定値)。
// 雑魚(EnemyController)の撃破演出だけ。ボス(WildBossBase/Dragon/Majin/洞窟/天空/死神)は対象外(各ボスの撃破演出のまま)。
[CreateAssetMenu(menuName = "OneMoreMile/Finish Tuning")]
public class FinishTuning : ScriptableObject
{
    [Tooltip("FINISH の撃破演出を使う(false = 従来の撃破演出: その場で煙/飛散/フェード)")]
    public bool enabled = true;

    [Header("HitStop(既存の HitStop を使う。同時/短時間の複数撃破は最大値だけ。積み重ねない)")]
    public float killHitStop = 0.07f;
    public float heavyHitStop = 0.10f;
    public float overkillHitStop = 0.12f;
    public float slamHitStop = 0.12f;

    [Header("吹っ飛び(画面に対する速さ。走行速度とは無関係: カメラ基準で動かす)")]
    [Tooltip("通常の FINISH の初速(ワールド単位/秒)")]
    public float finishKnockback = 34f;
    [Tooltip("上向きの成分(前/後ろの攻撃。0 = 真横)")]
    public float finishVerticalForce = 0.45f;
    [Tooltip("重力(ワールド単位/秒²)。空中の敵には掛けない")]
    public float finishGravity = 14f;
    [Tooltip("回転(度/秒、重さで割る)")]
    public float finishRotation = 900f;
    [Tooltip("画面の端に着かなくても、この秒数で弾ける")]
    public float finishDuration = 0.7f;
    [Tooltip("体の大きさの変化(飛んでいく間に少し小さく)")]
    public float finishShrink = 0.15f;

    [Header("軌跡(残像)")]
    public float trailDuration = 0.28f;
    [Tooltip("残像を出す間隔(秒)")]
    public float ghostInterval = 0.022f;
    public float ghostAlpha = 0.45f;

    [Header("弾ける光(画面の端/時間切れ)")]
    public float burstSize = 1.6f;
    [Tooltip("粒の数(通常)")]
    public int particleAmount = 14;
    [Tooltip("画面の端からこの割合だけ内側で弾ける(画面外で見えなくならないように)")]
    public float edgeMargin = 0.04f;

    [Header("HEAVY / OVERKILL")]
    [Tooltip("HEAVY の倍率(初速/軌跡/弾ける大きさ/粒)")]
    public float heavyFinishMultiplier = 1.35f;
    [Tooltip("最後の一撃のダメージ ÷ その直前の残りHP がこの値以上なら OVERKILL")]
    public float overkillThreshold = 3f;
    [Tooltip("OVERKILL の倍率")]
    public float overkillMultiplier = 1.8f;
    [Tooltip("HEAVY とみなす判定のノックバック倍率(PlayerAttackInfo.knockbackScale)")]
    public float heavyKnockbackScale = 1.4f;
    [Tooltip("HEAVY とみなす判定の HitStop(PlayerAttackInfo.hitStop)")]
    public float heavyAttackHitStop = 0.08f;

    [Header("上攻撃 FINISH(星になる)")]
    public float upSpeedMultiplier = 1.25f;
    [Tooltip("上空で小さくなる割合")]
    public float upShrink = 0.7f;

    [Header("叩きつけ FINISH")]
    [Tooltip("地面へ叩きつけるまでの秒数")]
    public float slamDownSeconds = 0.07f;
    [Tooltip("跳ね返りの角度(度、攻撃の向きの斜め上)")]
    public float slamBounceAngle = 55f;
    [Tooltip("複数を倒した時の角度の散らし(度)")]
    public float slamScatter = 16f;
    public float slamBounceSpeedMultiplier = 1.15f;

    [Header("重さ(EnemyDefinition.finishMass が 0 の時の自動値)")]
    public float massDefault = 1f;
    public float massHeavyCategory = 2.2f;
    public float massFlying = 0.8f;
    [Tooltip("この重さ以上は吹っ飛ばずに、短く大きく後退/転倒してその場で弾ける(巨大な敵)")]
    public float massTopple = 3.5f;

    [Header("カメラの揺れ(上限あり。重なっても大きくならない)")]
    public float shakeNormal = 0f;
    public float shakeHeavy = 0.06f;
    public float shakeOverkill = 0.12f;
    public float shakeSlam = 0.1f;
    public float shakeCap = 0.16f;

    [Header("連続撃破(FINISH STREAK)")]
    [Tooltip("この秒数の中の撃破を連続として数える")]
    public float streakWindow = 1.2f;
    [Tooltip("連続撃破で弾ける大きさ/粒を強くする量(3〜4体で ×(1+この値/2)、5体以上で ×(1+この値))")]
    public float streakEffectMultiplier = 0.5f;

    [Header("負荷")]
    [Tooltip("同時に使う絵の最大数(体/残像/粒/光の合計。足りない時は粒を減らす。Instantiate はしない)")]
    public int poolSize = 700;

    static FinishTuning cached; static bool loaded;
    public static FinishTuning I
    {
        get
        {
            if (!loaded)
            {
                loaded = true;
                cached = Resources.Load<FinishTuning>("Finish/FinishTuning");
                if (cached == null) { cached = CreateInstance<FinishTuning>(); cached.hideFlags = HideFlags.DontSave; }
            }
            return cached;
        }
    }
}
