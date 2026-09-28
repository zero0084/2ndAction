using System;
using UnityEngine;

// ===== 新4人(2026-09-27): 弓使い / 魔法使い / 格闘家 / 忍者 ===== //
// CharacterDefinition.kitがStandard以外の間だけ、4方向攻撃・移動の一部が
// PlayerController.Kit*.cs(キャラごとのpartial)へ分岐する。既存5人は
// kit=Standard(既定値)のままなので、ここにある値は一切参照されない。
// 調整値はすべてInspectorで変えられる(CharacterDefinitionの各パラメータ)。
// 10〜12人目(2026-09-28)は末尾に追加(アセットには番号で保存されるため、既存の並びは変えない)。
public enum CharacterKit { Standard, Archer, Mage, Fighter, Ninja, Miko, Vampire, Dragonkin }

// 攻撃ごとのポーズ(名前で引く)。frames が空なら通常の攻撃絵/走り絵のまま。
[Serializable]
public class KitPose
{
    public string name;
    public Sprite[] frames;
}

// 6人目 弓使い(CHARGE / SNIPER)。前攻撃は「引き絞り」: 前の射撃から時間が空くほど矢が強くなる
// (長押しは脱出操作と衝突するため、長押しの代わりに自動で引き絞る方式)。連射すると弱い矢しか出ない。
[Serializable]
public class ArcherKitParams
{
    [Header("前: チャージショット(自動引き絞り)")]
    [Tooltip("前の前射撃からこの秒数で「中」段階")] public float chargeMidTime = 0.55f;
    [Tooltip("前の前射撃からこの秒数で「最大」段階")] public float chargeMaxTime = 1.2f;
    public float forwardWindup = 0.12f;      // 弓を引く(通常)
    public float forwardWindupMax = 0.2f;    // 最大段階は少し長く引く(重さ)
    public float forwardRecovery = 0.3f;
    public float arrowSpeed = 20f;
    public float arrowSpeedMax = 30f;
    public float arrowLifetime = 1.2f;
    public float[] chargeDamageScale = { 1f, 2f, 3.2f };
    public float[] chargeKnockbackScale = { 1f, 1.7f, 2.6f };
    public float[] chargeHitStop = { 0f, 0.05f, 0.13f };
    [Tooltip("最大段階の貫通数(-1=無制限)")] public int maxChargePierce = -1;
    public float[] chargeArrowScale = { 1f, 1.2f, 1.6f };
    [Header("後: 素早い後方射撃")]
    public float backWindup = 0.05f;
    public float backRecovery = 0.22f;
    public float backDamageScale = 0.8f;
    public float backKnockbackScale = 1.1f;
    [Header("上: ジャンプ+斜め上の矢(拳銃士より遅く重い)")]
    public float upAngle = 40f;
    public float upArrowSpeed = 15f;
    public float upDamageScale = 1.8f;
    public float upKnockbackScale = 2.2f;
    public float upHitStop = 0.05f;
    [Header("下: 空中=強い斜め下の矢 / 地上=低い矢")]
    public float downAngle = -38f;
    public float downArrowSpeed = 22f;
    public float downDamageScale = 2f;
    public float downKnockbackScale = 1.8f;
    public float downHitStop = 0.05f;
    [Tooltip("空中の下射撃で落下を止める短い時間(長いホバーはしない)")] public float downHangTime = 0.1f;
    public int downShotsPerAirtime = 2;
    public float lowShotHeight = 0.28f;
    public float lowDamageScale = 1.2f;
    public float lowKnockbackScale = 1.5f;
    [Header("共通")]
    [Tooltip("矢の発射位置(弓の位置、体の前方)")] public Vector2 muzzle = new Vector2(0.45f, 0.62f);
}

// 7人目 魔法使い(FLIGHT / MAGIC)。地面から少し浮いたまま自動前進。上下フリックで高度を変えながら魔法。
// 穴は越えられるが、天井(洞窟)で頭打ちになり、天井の針にも普通に当たる。
[Serializable]
public class MageKitParams
{
    [Header("浮遊・高度")]
    [Tooltip("最低高度(足元と地面の距離)")] public float hoverBase = 0.4f;
    [Tooltip("上下フリック1回で変わる高度")] public float altitudeStep = 1.2f;
    public int maxAltitudeLevel = 3;
    [Tooltip("目標高度へ近づく速さ(秒)")] public float altitudeSmoothTime = 0.16f;
    public float bobAmplitude = 0.05f;
    public float bobFrequency = 1.6f;
    [Header("前: 魔法弾(命中で小爆発)")]
    public float boltSpeed = 13f;
    public float boltLifetime = 1.1f;
    public float boltCooldown = 0.36f;
    public float boltCastTime = 0.1f;
    public float boltDamageScale = 1f;
    public float blastRadius = 0.85f;
    public float blastDamageScale = 1f;
    public float blastKnockbackScale = 1.2f;
    [Header("後: 弱い魔法弾(爆発なし)")]
    public float backBoltSpeed = 11f;
    public float backDamageScale = 0.7f;
    [Header("上: 高度上昇+斜め上の雷撃")]
    public float upAngle = 55f;
    public float upBoltSpeed = 14f;
    public float upDamageScale = 1.2f;
    public float upBlastRadius = 0.75f;
    [Header("下: 高度下降+真下への魔法(地面で爆発)")]
    public float downAngle = -72f;
    public float downBoltSpeed = 14f;
    public float downDamageScale = 1.2f;
    public float downBlastRadius = 1.15f;
    public float downBlastDamageScale = 1.3f;
}

// 8人目 格闘家(CLOSE COMBAT / COUNTER)。全キャラで最も射程が短い。4段コンボは段ごとにHitStopが強い。
[Serializable]
public class FighterKitParams
{
    [Header("前: 4段コンボ Punch → Punch → Kick → Heavy")]
    public float[] comboWindup = { 0.04f, 0.04f, 0.07f, 0.12f };
    public float[] comboActive = { 0.08f, 0.08f, 0.1f, 0.12f };
    public float[] comboRecovery = { 0.1f, 0.1f, 0.14f, 0.34f };
    public float[] comboReach = { 0.95f, 1.0f, 1.15f, 1.2f };
    public float[] comboLunge = { 0.18f, 0.2f, 0.3f, 0.5f };
    public float[] comboDamageScale = { 0.8f, 0.8f, 1.1f, 2.2f };
    public float[] comboKnockbackScale = { 0.35f, 0.35f, 0.8f, 2.6f };
    public float[] comboHitStop = { 0.04f, 0.04f, 0.06f, 0.15f };
    public float comboGrace = 0.32f;
    public float hitHeight = 0.62f;
    public float hitThickness = 0.62f;
    [Header("後: バックステップ+カウンター受付")]
    public float backStepDistance = 1.1f;
    public float backStepTime = 0.16f;
    [Tooltip("この間に被弾するとダメージを受けずにカウンター")] public float counterWindow = 0.32f;
    public float counterRadius = 1.35f;
    public float counterDamageScale = 3f;
    public float counterKnockbackScale = 3f;
    public float counterHitStop = 0.18f;
    [Tooltip("カウンター直後の短い無敵")] public float counterInvincible = 0.3f;
    public float rearStrikeDamageScale = 1f;
    public float rearStrikeReach = 0.9f;
    public float backRecovery = 0.2f;
    [Tooltip("バックステップ〜カウンター受付〜肘打ちの間の前進速度倍率(踏みとどまる)")] public float stanceMoveFactor = 0.25f;
    [Header("上: アッパー(敵を打ち上げ、自分は少しだけ浮く)")]
    public float uppercutHop = 0.9f;    // jumpForceに対する倍率(黒剣士のジャンプより低いが、1回で穴を越えられる滞空時間はお嬢様騎士並みに残す)
    public float uppercutActive = 0.16f;
    public float uppercutDamageScale = 1.4f;
    public float uppercutHitStop = 0.07f;
    [Header("下: 空中=ダイブキック / 地上=足払い")]
    public float diveKickSpeedY = 15f;
    public float diveKickSpeedX = 5f;
    public float diveKickDamageScale = 1.6f;
    public float diveKickImpactRadius = 1.0f;
    public float diveKickImpactDamageScale = 1.2f;
    public float diveKickHitStop = 0.08f;
    public float sweepReach = 1.25f;
    public float sweepActive = 0.12f;
    public float sweepRecovery = 0.22f;
    public float sweepDamageScale = 1.1f;
    public float sweepKnockbackScale = 1.6f;
}

// 9人目 忍者(EVASION / MOBILITY)。「速い連撃」ではなく「位置を素早く変える」キャラ。
// 瞬身の無敵はごく短時間だけ、さらに連打しても無敵は一定間隔でしか付かない(永久無敵にならない)。
[Serializable]
public class NinjaKitParams
{
    [Header("前: 瞬身+抜刀斬り(敵をすり抜ける)")]
    public float dashDistance = 3.0f;
    public float dashTime = 0.13f;
    public float dashRecovery = 0.16f;
    public float dashCooldown = 0.42f;
    [Tooltip("瞬身中の無敵(ごく短時間)")] public float dashInvincible = 0.16f;
    [Tooltip("無敵が付いてから次に無敵が付くまでの最短間隔(連打しても永久無敵にならない)")] public float invincibleInterval = 0.8f;
    public float slashDamageScale = 1.1f;
    // すり抜けた敵は前方(プレイヤーの背中側)へ押し出されるので、追いつかれないよう弱めにする。
    public float slashKnockbackScale = 0.3f;
    public float slashHitStop = 0.04f;
    public float slashHeight = 0.6f;
    public float slashThickness = 0.9f;
    [Header("後: 手裏剣(+小さな後退)")]
    public float shurikenSpeed = 17f;
    public float shurikenLifetime = 0.9f;
    public float shurikenDamageScale = 0.7f;
    public float shurikenCooldown = 0.3f;
    public float backHop = 0.7f;
    [Header("上: 斜め上への跳躍斬り(空中の使用回数に制限)")]
    public float upDashSpeedY = 10.5f;
    public float upDashDistanceX = 1.6f;
    public float upDashTime = 0.18f;
    public float upDamageScale = 1f;
    [Header("下: 空中=煙から斜め下へ急降下斬り / 地上=低い滑り斬り")]
    public float downDashSpeedY = 17f;
    public float downDashSpeedX = 9f;
    public float downDashMaxTime = 0.5f;
    public float downDamageScale = 1.3f;
    public float landSlashRadius = 1.05f;
    public float landSlashDamageScale = 1f;
    public float slideDistance = 1.8f;
    public float slideTime = 0.16f;
}

// 10人目 巫女(SHRINE MAIDEN、役割 SEALER)。御札・式神・結界を置いて「敵が通る場所」を支配する。
// 即効性は低いが、敵の進路を読めば強い(魔法使い=自分から撃ち込む、との違い)。
[Serializable]
public class MikoKitParams
{
    [Header("前: 御札(命中で貼り付き → 時間差で浄化爆発。貼られた敵に2枚目で即爆発)")]
    public float ofudaSpeed = 11f;
    public float ofudaLifetime = 1.1f;
    public float ofudaWindup = 0.12f;
    public float ofudaCooldown = 0.42f;
    public float ofudaDamageScale = 0.6f;
    [Tooltip("貼り付いてから浄化爆発までの秒数")] public float markDelay = 1.3f;
    public float burstRadius = 0.95f;
    public float burstDamageScale = 1.6f;
    [Tooltip("貼られた敵へ2枚目を当てた時の即時爆発(大きめ)")] public float detonateRadius = 1.25f;
    public float detonateDamageScale = 2.2f;
    [Header("後: 式神(紙の鳥、最初に触れた敵へ)")]
    public float shikiSpeed = 8f;
    public float shikiLifetime = 1.5f;
    public float shikiDamageScale = 1f;
    public float shikiCooldown = 0.45f;
    [Header("上: 御札を扇状に展開(空中の敵の迎撃)")]
    public float[] upAngles = { 28f, 50f, 72f };
    public float upSpeed = 12f;
    public float upDamageScale = 0.5f;
    [Header("下: 結界(同時に1つ、置き直すと古い方は消える)")]
    public float barrierForward = 3.4f;     // 足元から前方へどれだけ先に置くか(中心)
    public float barrierWidth = 4.2f;
    public float barrierHeight = 1.9f;
    public float barrierDuration = 2.6f;
    public float barrierTick = 0.45f;
    public float barrierTickDamageScale = 0.35f;
    [Tooltip("結界内の敵の横移動をこの倍率まで遅くする")] public float barrierSlow = 0.3f;
    public float barrierWindup = 0.22f;
    public float barrierCooldown = 0.8f;
}

// 11人目 吸血鬼(VAMPIRE、役割 BLOOD)。攻撃を当てるとBlood Gaugeが溜まり、戦い続けるほど強くなる。
[Serializable]
public class VampireKitParams
{
    [Header("Blood Gauge (0〜100)")]
    public float gainPerHit = 7f;
    public float gainDiveHit = 35f;
    [Tooltip("最後に当ててからこの秒数後にゲージが減り始める")] public float decayDelay = 2.5f;
    public float decayPerSecond = 9f;
    [Tooltip("この値以上で攻撃が少し強化")] public float tierThreshold = 50f;
    public float tierDamageScale = 1.2f;
    public float tierSpeedScale = 0.88f;     // 技の時間倍率(小さいほど速い)
    [Header("Blood Rush (100で発動)")]
    public float rushDuration = 5f;
    public float rushSpeedScale = 0.7f;      // 技の時間倍率
    public float rushMoveScale = 1.12f;      // 走行速度
    public float rushDamageScale = 1.3f;
    [Tooltip("Rush中の命中1回ごとの回復(ハート単位で貯め、1貯まったら1回復)")] public float rushHealPerHit = 0.06f;
    public float gaugeAfterRush = 30f;
    [Header("回復の上限(永久回復にしない)")]
    [Tooltip("急降下吸血の命中でゲージがこれ以上あれば少し回復")] public float diveHealThreshold = 50f;
    public float diveHeal = 0.34f;
    [Tooltip("実際にハートが1つ回復した後、次に回復できるまでの秒数")] public float healCooldown = 12f;
    [Header("前: 右爪 → 左爪 → 血の斬撃")]
    public float[] comboWindup = { 0.05f, 0.05f, 0.1f };
    public float[] comboActive = { 0.08f, 0.08f, 0.12f };
    public float[] comboRecovery = { 0.1f, 0.1f, 0.26f };
    public float[] comboReach = { 1.1f, 1.1f, 2.1f };
    public float[] comboDamageScale = { 0.9f, 0.9f, 1.6f };
    public float[] comboKnockbackScale = { 0.5f, 0.5f, 1.4f };
    public float[] comboHitStop = { 0.03f, 0.03f, 0.07f };
    public float comboGrace = 0.35f;
    [Header("後: バックステップ+コウモリの群れ")]
    public float backStep = 0.9f;
    public float batSpeed = 10f;
    public float batLifetime = 0.9f;
    public float batDamageScale = 0.4f;
    public int batCount = 3;
    [Header("上: 霧/コウモリ化して斜め上へ移動")]
    public float mistSpeedY = 9.5f;
    public float mistDistanceX = 2.2f;
    public float mistTime = 0.22f;
    public float mistDamageScale = 0.8f;
    [Header("下: 空中=急降下吸血 / 地上=低い血の薙ぎ")]
    public float diveSpeedY = 16f;
    public float diveSpeedX = 6f;
    public float diveDamageScale = 1.4f;
    public float lowReach = 1.3f;
    public float lowDamageScale = 1f;
}

// 12人目 竜人(DRAGONKIN、役割 BRUTE)。武器なし、自分の爪・尾・翼・炎で戦う。
[Serializable]
public class DragonkinKitParams
{
    [Header("前: 爪3段(格闘家より遅く、1発が重い)")]
    public float[] comboWindup = { 0.1f, 0.1f, 0.18f };
    public float[] comboActive = { 0.1f, 0.1f, 0.14f };
    public float[] comboRecovery = { 0.16f, 0.16f, 0.36f };
    public float[] comboReach = { 1.3f, 1.3f, 1.6f };
    public float[] comboLunge = { 0.2f, 0.2f, 0.4f };
    public float[] comboDamageScale = { 1.3f, 1.3f, 2.3f };
    public float[] comboKnockbackScale = { 1f, 1f, 2.8f };
    public float[] comboHitStop = { 0.05f, 0.05f, 0.12f };
    public float comboGrace = 0.35f;
    [Header("後: 尾の薙ぎ払い(後方に広い)")]
    public float tailWindup = 0.1f;
    public float tailActive = 0.16f;
    public float tailRecovery = 0.3f;
    public float tailReach = 2.4f;
    public float tailDamageScale = 1f;
    [Header("上: 翼で強く羽ばたいて上昇+爪のアッパー")]
    [Tooltip("jumpForceに対する倍率(通常のジャンプより高く上がる)")] public float flapJump = 1.18f;
    public float flapActive = 0.18f;
    public float flapDamageScale = 1.2f;
    [Header("短い滑空(常時飛行ではない)")]
    [Tooltip("ジャンプの頂点を過ぎて落ち始めたら、1回のジャンプにつき1回だけ落下を遅くする秒数")] public float glideTime = 0.45f;
    public float glideFallSpeed = 1.3f;
    [Header("下: 炎のブレス(地面に当たると燃える地面)")]
    public float breathTime = 0.36f;
    public int breathShots = 5;
    public float breathSpeed = 12f;
    public float breathAngleAir = -58f;
    public float breathAngleGround = -14f;
    public float breathDamageScale = 0.6f;
    public float breathHoverFall = 0.8f;
    public float burnWidth = 2.6f;
    public float burnDuration = 1.5f;
    public float burnTick = 0.3f;
    public float burnTickDamageScale = 0.35f;
    [Tooltip("体が大きい=当たり判定が大きい")] public float bodyScale = 1.15f;
}
