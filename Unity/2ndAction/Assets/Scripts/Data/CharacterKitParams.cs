using System;
using UnityEngine;

// ===== 新4人(2026-09-27): 弓使い / 魔法使い / 格闘家 / 忍者 ===== //
// CharacterDefinition.kitがStandard以外の間だけ、4方向攻撃・移動の一部が
// PlayerController.Kit*.cs(キャラごとのpartial)へ分岐する。既存5人は
// kit=Standard(既定値)のままなので、ここにある値は一切参照されない。
// 調整値はすべてInspectorで変えられる(CharacterDefinitionの各パラメータ)。
public enum CharacterKit { Standard, Archer, Mage, Fighter, Ninja }

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
