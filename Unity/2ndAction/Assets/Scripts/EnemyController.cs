using System.Collections;
using UnityEngine;

// How this enemy relates to the ground - the single source of truth other
// components (EnemyAnimator, TerrainManager's spawn placement) read instead
// of each independently guessing from a raw bool. Only Ground is actually
// used today (the goblin art always spawns as one - see TerrainManager),
// but the type exists so a future flying enemy species has a real place to
// plug into the same spawn/animation pipeline instead of needing its own.
public enum EnemyMovementType
{
    Ground,
    Flying
}

public class EnemyController : MonoBehaviour
{
    public EnemyMovementType movementType = EnemyMovementType.Ground;

    // Distance Level Design Ver.1 - "EnemyHP = 1 + floor(CurrentDistance /
    // 2000)" (see DistanceTierManager.CurrentEnemyHp) times the species'
    // own hpMultiplier (EnemyDefinition.hpMultiplier - Heavy's "追加倍率").
    // Set by GroundFactory.CreateEnemy right after spawn, BEFORE this
    // component's own Awake runs its first frame - never left at the
    // default 1 for a species that should be tougher.
    //
    // エリアルコンボ改修(2026-09-11) - 「ゴブリンを一撃で倒れないように」。
    // このmaxHp自体はDistanceTierManager.CurrentEnemyHp(=1+baseHpBonus+
    // 距離ボーナス)から来ており、baseHpBonusのデフォルトを1→4へ引き上げ
    // た(DistanceTierManager.cs参照、開始距離0でHP=5、AttackPower既定2
    // の攻撃で通常2〜3発)。将来の強敵はEnemyDefinition.hpMultiplierを
    // 上げるだけ(既存の仕組みのまま)で4〜6発相当にできる - 今回新しい
    // 敵種は追加していない。
    public int maxHp = 1;
    int hp = -1; // -1 sentinel: not yet initialized from maxHp (see EnsureHp)
    void EnsureHp() { if (hp < 0) hp = Mathf.Max(1, maxHp); }

    // Reward/MILE System Ver.1 - set by GroundFactory.CreateEnemy from this
    // spawn's EnemyDefinition.mileReward, handed to GameManager.
    // RegisterEnemyKill on the killing blow (see HitAndDie below).
    public int mileReward = 1;

    // Polish Pass 1, item 2 - makes the moment an attack actually lands
    // read clearly, without slowing the game down. Everything here is
    // individually toggleable/tunable and purely cosmetic.
    [Header("Hit Feedback (tunable)")]
    public bool hitFlashEnabled = true;
    public Color hitFlashColor = Color.white;
    // How long the flash+knockback hold before the explosion/despawn plays
    // - deliberately brief so the kill still feels instant, not delayed.
    public float hitFlashHoldDuration = 0.05f;
    public bool hitStopEnabled = true;
    // エリアルコンボ改修(2026-09-11), item 5 - 「通常ヒット：約0.03〜0.05秒」
    // に合わせて既定値を調整(旧0.03のまま、レンジのみ明示)。
    [Range(0.02f, 0.08f)] public float hitStopDuration = 0.04f;
    // Game Feel pass - re-enabled with an actual eased tween (see
    // KnockbackRoutine) instead of the old instant position snap.
    public bool hitKnockbackEnabled = true;
    // Heavy Enemy - "画面端まで吹っ飛ばすような感じ" は、この2つを
    // GroundFactory.CreateEnemyが大きく上書きすることで表現する(このク
    // ラス自身はどの種族か知らない)。エリアルコンボ改修(2026-09-11)で
    // KnockbackRoutineを非致死ヒットの主経路に戻したため、この上書きが
    // 実際に効くようになった(旧実装は非致死ヒットが専らLaunchAwayRoutine
    // を使っていたため、実は死亡時にしか反映されていなかった)。
    public float hitKnockbackDistance = 0.12f;
    public float hitKnockbackDuration = 0.1f;

    [Header("Ground Normal Attack Knockback (速度ベース、2026-09-12第3弾)")]
    // 実機フィードバック(2026-09-12第3弾) - 距離(位置Lerp)ベースの前回
    // 実装は、コルーチンの間だけ動いてすぐ静止するため、静止した瞬間から
    // 主人公の自動前進にすぐ追いつかれてしまい「押した→即密着」になって
    // いた、との報告。OneMoreMileは距離でPlayerの走行速度が上がり続ける
    // ため、固定の距離/速度ではいずれ機能しなくなる - 「敵を一定距離だけ
    // 瞬間移動させる」のではなく「主人公の現在速度を基準にした速度で、
    // 短時間だけ主人公より速く前進させる」方式に全面変更した(下記
    // ApplyGroundKnockback/Update参照)。速度を主人公の現在速度からの相対
    // 値で計算するため、距離によるSpeed Up後もこの仕組みは機能し続ける。
    public float groundKnockbackSpeedBonus = 2.5f;
    // ノックバック速度を維持する時間 - この間は主人公より速く前進し続け、
    // 0になった瞬間に通常状態へ戻る(以降は主人公が追いつく側に回る)。
    public float groundKnockbackDuration = 0.2f;

    // 実機フィードバック(2026-09-12第4弾) - 「攻撃後、敵との横方向の距離
    // が開きすぎて追撃しづらい」。上のノックバックが終わった瞬間に敵を
    // 完全静止させていたため、そこから先は主人公だけが進み続け、一度開い
    // た距離がそのまま維持/拡大してしまっていた。ノックバック終了直後、
    // 短時間だけ敵のX速度を「主人公の現在速度 × groundFollowAssistFactor」
    // へ切り替える追従補正フェーズを追加した - 敵を主人公へ完全吸着させる
    // のではなく(factor<1なら主人公がゆっくり追いつく、>1なら敵がわずか
    // に先行し続ける)、次の攻撃が届く程度の間合いを保つことが狙い。
    [Header("Ground Knockback Follow Assist (ノックバック後、短時間だけ間合いを維持)")]
    public float groundFollowAssistDuration = 0.2f;
    [Range(0.5f, 1.5f)] public float groundFollowAssistFactor = 1.0f;

    // 実機フィードバック(2026-09-12第4弾) - 「足場のない場所へ敵が吹き
    // 飛ばされた後もその場で立った状態になることがある」。従来、Launch/
    // Slam以外の状態(通常のノックバック・追従補正・単なる静止)では敵の
    // Y座標に対する接地判定が一切行われていなかった(EnemyAnimatorが
    // Spawn時の固定Yへピン留めしていただけ - 下記EnemyAnimator.csの修正
    // 参照)。UpdateNormalGroundCheck()が、Launch中でない間ずっと毎フレー
    // ム「今のX位置に地面があるか」を監視し、無ければ即座にAirborneへ
    // 切り替えて重力で落下させる(Movementの通常の地面判定に相当) -
    // ノックバック/追従補正中も含めて常時有効。
    [Header("Ground Check (足場のない場所で敵が浮いたまま/立ったままにならないように)")]
    public float groundFallGravity = 20f;
    // Player.failYと同じ考え方 - これを下回ったら落下死として処理する
    // (item 9 - 足場外へ落とすことを正式な戦闘手段として成立させる)。
    public float enemyDeathBelowY = -8f;
    public bool hitParticleEnabled = true;
    // Assets/Art/Effects/HitSpark.png (see SceneBuilder) - falls back to
    // the plain procedural dot if not assigned, so this never breaks an
    // older scene build.
    public Sprite hitSparkSprite;
    // エリアルコンボ改修(2026-09-11), item 6 - 「水色/シアン＋白の攻撃VFX
    // に合わせて」。旧・純白から、既存の斬撃VFX(SlashArcBlue等)と同系統
    // のシアン寄りの白へ変更 - 新規アート生成はせず、既存のSoftDotSprite/
    // HitSpark.pngを引き続き使い回す(サイズは攻撃VFXよりずっと小さいまま
    // で「攻撃エフェクト」と「命中エフェクト」が視覚的に混同しない)。
    public Color hitParticleColor = new Color(0.75f, 0.95f, 1f, 1f);
    public float hitParticleScale = 0.52f;
    public float hitParticleDuration = 0.2f;
    [Range(0f, 1f)] public float hitParticleHoldFraction = 0.25f;
    // Assets/Art/Effects/EnemyDeathSmoke.png - a brief accent alongside the
    // enemy's own Hit->Flash->Fade/Scale->消滅 sequence.
    public Sprite deathCloudSprite;
    public float deathCloudScale = 0.75f;
    public float deathCloudDuration = 0.5f;
    [Range(0f, 1f)] public float deathCloudHoldFraction = 0.3f;
    // 敵撃破時の飛散パーティクル(2026-09-10) - 撃破の瞬間に四方へ飛び散る
    // 粒のバースト(ExplosionEffect)。数・サイズ・飛散速度は敵のワールド
    // 高さに比例。色は種族ごと(雑魚敵の既定は青)。
    public bool deathBurstEnabled = true;
    public Color deathBurstColor = new Color(0.32f, 0.68f, 1f, 1f);
    // A short scale-down + fade on the enemy's own sprite right before it
    // disappears.
    public float dieFadeDuration = 0.14f;

    // エリアルコンボ改修(2026-09-11) - 「敵を倒した攻撃：通常ヒットより
    // 大きく吹き飛ばす」。非致死ヒットと同じKnockbackRoutineを使うが、
    // 距離/時間をこの倍率ぶん大きくする。
    [Header("Death Knockback (通常ヒットより大きく吹き飛ばす)")]
    public float deathKnockbackDistanceMultiplier = 2.5f;
    public float deathKnockbackDurationMultiplier = 1.4f;

    [Header("Aerial Combo - Up Attack (敵を斜め前方へ打ち上げる)")]
    // 不具合修正(2026-09-12) - 「上攻撃を当ててもゴブリンがほぼ浮かない」。
    // 根本原因は打ち上げ物理そのものではなく、下のDisableMotionComponents
    // が実際には一度も効いていなかったこと(Awakeの説明コメント参照) -
    // EnemyAnimatorが毎フレームtransform.position=spawn時の位置へ戻し
    // 続け、Update()側の上昇/落下を全て打ち消していた。修正済みだが、
    // 「最初は多少大げさなくらい高く」という指示に合わせて数値も底上げ
    // (旧8/18→9/15、山なり頂点で約2.7ワールド単位=キャラ身長の2倍以上、
    // 頂点到達まで約0.6秒でプレイヤーが二段ジャンプで追いつきやすい)。
    public float launchUpSpeed = 9f;
    public float launchGravity = 15f;
    // アエリアルコンボ追加調整(2026-09-12第2弾) - 前回パスで「launchVelocityX
    // = 主人公の自動前進速度 + 上乗せ分」を"Launch中ずっと"適用する実装に
    // したが、これは実質的に主人公より常に速い一定速度で敵が進み続けると
    // いうことであり、滞空時間が長いほど敵と主人公の横方向のズレが際限
    // なく開いていく不具合だった(マスター報告「Launch後に横方向の位置が
    // ズレやすい」の直接原因)。修正: Update()側は常に主人公の速度と完全
    // に同じ速度(パリティ、下記PlayerForwardSpeed参照)で並走させ、この
    // フィールドは「Launchの瞬間だけ」の短時間バースト(launchForwardBurst
    // Duration秒かけて線形に0まで減衰)として上乗せする - 敵は最初の一瞬
    // だけ斜め前方へ弾き出され、その後はズレが拡大しないまま主人公と並走
    // し続ける。
    public float launchForwardBurstSpeed = 6f;
    // バースト(Launch/空中通常攻撃どちらも共通)が0まで減衰するまでの時間。
    public float launchForwardBurstDuration = 0.2f;

    [Header("Aerial Combo - Air Re-Launch (空中の敵に上攻撃を当てた場合、地上より弱く浮かせ直す)")]
    // 今回追加(2026-09-12) - 「上攻撃→Launch→追撃→空中上攻撃→再度浮かせ
    // る→さらに追撃」を可能にする。地上からの初回Launch(launchUpSpeed)
    // より弱めにすることで「地上上攻撃：大きく打ち上げる/空中上攻撃：
    // 落ちてきた敵をもう一度軽く浮かせ直す」という強弱の差を付ける。
    // 再Launch自体の回数制限は設けない(マスター指示:「無限に敵を浮かせ
    // 続けられる状態でも問題ない、まずは気持ちよくつながることを優先」)。
    public float airLaunchUpSpeed = 6f;
    public float airLaunchForwardBurstSpeed = 3f;

    [Header("Aerial Combo - Air Hit (浮いている敵を追撃)")]
    // 空中ヒットのたびに落下速度をこの値まで戻す(0にはしない = 「簡単に
    // 地面へ落ちない」程度に留め、完全な空中停止は避ける)。
    public float juggleHoverFallSpeed = -1.5f;
    // アエリアルコンボ追加調整(2026-09-12), item 5 - 「空中通常攻撃は敵を
    // 前へ運ぶ」。上のlaunchForwardBurstSpeedと同じ「短時間バースト」方式
    // (launchForwardBurstDurationを共有)、ヒットのたびにバーストを再ス
    // タートする(ExtendJuggle参照)。実機フィードバック(2026-09-12第4弾)
    // item 4「空中コンボ時は追従を少し強めても構わない」を受けて4→5に
    // 微調整(地上の追従補正と違い、空中は既にパリティ+バーストで常時
    // 主人公と並走しているため、この値は「並走速度への上乗せ分」のみ)。
    public float juggleForwardBurstSpeed = 5f;
    // 安全装置 - 永久に空中へ拘束しない。打ち上げ開始からこの時間を過ぎ
    // ると、以降の空中"通常"攻撃では滞空を延長できなくなり、重力に任せて
    // 自然落下する(上攻撃による再Launchはこの制限を受けない - 上のAir
    // Re-Launchヘッダー参照)。
    public float maxJuggleDuration = 3f;

    [Header("Aerial Combo - Down Attack Slam (空中の敵を叩き落とす)")]
    // 通常の重力加速ではなく、一定の速い下降速度に固定(プレイヤー自身の
    // 下降攻撃と同じ考え方)。
    public float slamSpeed = 20f;
    // エリアルコンボ改修(2026-09-11), item 5 - 「強攻撃／叩き落とし：約
    // 0.05〜0.08秒」。
    [Range(0.04f, 0.1f)] public float slamHitStopDuration = 0.07f;
    // 地面到達時の衝撃VFXのスケール(TerrainManager.enemyGroundImpactSprite
    // - プレイヤー自身の下攻撃着地と同じImpactBurstBlue.pngを流用)。
    public float groundImpactScale = 1f;
    // 生存した場合(このヒットでは倒せなかった場合)の着地後の軽い跳ね -
    // 大きすぎるとそのまま追撃困難な位置まで転がってしまうため小さめ。
    public float slamSurviveBounce = 0.15f;

    SpriteRenderer sr;
    bool dying;

    // ===== エリアルコンボ状態(Launched/Slamming) =====
    bool isLaunched;
    bool isSlamming;
    // Downが致死だった場合、演出上「地面へ叩きつけてから死なせる」ため、
    // 判定自体(hp<=0)はヒットの瞬間に確定させつつ、実際の死亡演出は着地
    // まで遅延させる(item 8「地面到達→地面衝撃VFX→強めのヒットストップ
    // →死亡していれば大きく吹き飛ぶ」の順序を再現するため)。
    bool pendingDeathOnLand;
    float launchVelocityY;
    // アエリアルコンボ追加調整(2026-09-12) - Launch中の水平方向の速度。
    // Update()で毎フレーム「主人公の現在速度(パリティ) + 減衰中のバースト
    // 分」として再計算される(下記launchForwardBurstVelocity/Timer参照) -
    // 固定値を積分し続ける旧実装(横方向のズレが際限なく開く不具合の原因
    // だった)ではなく、常に主人公との相対速度が最終的に0へ収束する設計。
    float launchVelocityX;
    // アエリアルコンボ追加調整(2026-09-12第2弾) - Launch/空中通常攻撃の
    // 瞬間に(再)スタートする短時間の前方バースト。launchForwardBurstVelocity
    // を初速として、launchForwardBurstTimerが0になるまで線形に減衰する
    // (Update()参照)。
    float launchForwardBurstVelocity;
    float launchForwardBurstTimer;
    float juggleElapsed;

    // 実機フィードバック(2026-09-12第3弾) - 地上ノックバックの速度ベース
    // 状態。isLaunchedがfalseの間だけUpdate()で積分される(ApplyGround
    // Knockback/Update参照)。
    float groundKnockbackVelocityX;
    float groundKnockbackTimer;
    // 実機フィードバック(2026-09-12第4弾) - ノックバック終了後の追従補正
    // タイマー(groundKnockbackTimerが0になった瞬間にこちらへ引き継ぐ)。
    float groundFollowAssistTimer;
    // 実機フィードバック(2026-09-12第4弾) - Launch/Slamとは独立した、
    // 「今、足元に地面があるか」の状態。trueの間はY座標に一切触れない
    // (地形追従は各Behavior/既存配置ロジックの責務のまま)。falseの間は
    // UpdateNormalGroundCheckが重力で落下させる。
    bool normalGrounded = true;
    float normalFallVelocityY;
    // 実機フィードバック(2026-09-12第5弾) - 上攻撃のPickup/Vacuumで引き
    // 寄せられている間、trueになる。この間はUpdate()側の通常のLaunch物理
    // (launchVelocityX/Y積分)を止め、VacuumPickupRoutineだけがこの敵の
    // 位置を専有する(TryVacuumPickup/VacuumPickupRoutine参照)。
    bool beingVacuumed;

    // 不具合修正(2026-09-12) - 「上攻撃で敵を明確に打ち上げる」が実機で
    // 機能しなかった根本原因。GroundFactory.CreateEnemyはEnemyController
    // を先にAddComponentし、EnemyAnimator/EnemySpecialBehaviorは後から
    // 追加する - UnityはAddComponent時点でその場でAwake()を同期実行する
    // ため、EnemyController.Awake()が走った瞬間にはEnemyAnimator/
    // EnemySpecialBehaviorはまだGameObjectに存在せず、ここでGetComponent
    // していた旧実装(cachedAnimator/cachedSpecialBehaviorをAwakeで一度
    // だけ取得)は常にnullを掴んでいた。結果、DisableMotionComponentsが
    // 何もせず、EnemyAnimatorの毎フレームtransform.position=spawn位置への
    // 上書き(Update内、待機の揺れ用)がLaunch中もずっと効き続け、Y軸の
    // 上昇/落下を毎フレーム打ち消していた("ほぼ浮かない"の直接原因)。
    // 修正: Awake時点でのキャッシュをやめ、実際に必要になった瞬間
    // (DisableMotionComponents/RestoreMotionComponents呼び出し時 - 既に
    // 敵が何フレームも存在した後のヒット処理タイミング)に都度GetComponent
    // する(旧HitAndDieが元々そうしていた、実際に動いていたパターンへ戻す)。

    public enum EnemyAerialState { Grounded, Launched, Slamming }
    // 項目3「Grounded/Launched/Airborne/Slammingの状態管理」に対応する
    // 読み取り専用の公開プロパティ(デバッグ表示・将来の拡張用)。内部の
    // 実装はisLaunched/isSlammingの2フラグのままだが、外部からは単純な
    // 状態として見える。
    public EnemyAerialState AerialState => isSlamming ? EnemyAerialState.Slamming : (isLaunched ? EnemyAerialState.Launched : EnemyAerialState.Grounded);

    void Awake()
    {
        // The SpriteRenderer lives on the "Visual" child, not this Root -
        // see GroundFactory.CreateEnemy.
        sr = GetComponentInChildren<SpriteRenderer>();
    }

    // エリアルコンボ改修(2026-09-11) - 打ち上げ/叩き落とし中のY軸物理の
    // みここで積分する。他のあらゆる移動(EnemyAnimatorの待機bob、
    // EnemySpecialBehaviorの各種挙動)は、Launch開始時にDisableMotion
    // Componentsで止めているため、Transformの取り合いは起きない。
    //
    // 実機フィードバック(2026-09-12第3弾) - Launch中でない場合も、地上
    // ノックバック(速度ベース)の水平方向の積分をここで行うよう拡張した。
    void Update()
    {
        // マルチプレイPhase 2 - JOIN側のパペットは物理/AIを持たない(位置はHOSTから補間表示)。
        if (NetReplica) return;
        if (dying) return;

        // 実機フィードバック(2026-09-12第5弾) - Pickup/Vacuum中は
        // VacuumPickupRoutineがこの敵の位置を専有する(通常のLaunch物理と
        // 同時に書き込むと競合するため)。
        if (beingVacuumed) return;

        if (isLaunched)
        {
            juggleElapsed += Time.deltaTime;
            launchVelocityY -= launchGravity * Time.deltaTime;

            // アエリアルコンボ追加調整(2026-09-12第2弾) - Slam中(縦の叩き
            // 落とし)以外は、毎フレーム「主人公の現在速度(パリティ)+減衰
            // 中のバースト」へ再計算する。パリティ部分を毎フレーム主人公
            // から直接読むことで、Speed Upで主人公が加速してもズレが生じ
            // ず、バースト部分は時間経過で必ず0へ減衰するため横方向のズレ
            // が際限なく開かない。
            if (!isSlamming)
            {
                float burst = 0f;
                if (launchForwardBurstTimer > 0f)
                {
                    burst = launchForwardBurstVelocity * Mathf.Clamp01(launchForwardBurstTimer / launchForwardBurstDuration);
                    launchForwardBurstTimer -= Time.deltaTime;
                }
                launchVelocityX = PlayerForwardSpeed() + burst;
            }

            transform.position += new Vector3(launchVelocityX * Time.deltaTime, launchVelocityY * Time.deltaTime, 0f);

            // 実機フィードバック(2026-09-12第4弾) - 「Launchによって敵が
            // 足場の外へ移動した後でも、地面がない位置で立った状態になる
            // ことがある」。旧実装は現在X位置に地面が無い場合、打ち上げ
            // 開始時点の地面高さ(launchBaseGroundY)を代替の床として着地
            // させてしまっていた(=何もない場所での見えない床)。修正:
            // 実在する地面が見つかった時だけ着地させ、見つからない間は
            // そのまま重力に従って落下を続けさせ、デッドラインを超えた
            // 時点で通常のノックバック経由の落下死と同じ扱いで撃破する。
            float? groundY = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(transform.position.x) : (float?)null;
            if (groundY.HasValue && transform.position.y <= groundY.Value)
            {
                LandFromLaunch(groundY.Value);
            }
            else if (transform.position.y < enemyDeathBelowY)
            {
                dying = true;
                gameObject.SetActive(false);
                RegisterKillReward(fallDeath: true);
            }
            return;
        }

        // 実機フィードバック(2026-09-12第3弾/第4弾) - 地上ノックバック
        // (速度ベース)→ノックバック後の追従補正、の2段階。ApplyGround
        // Knockbackが設定したgroundKnockbackVelocityXを、groundKnockback
        // Timerが尽きるまで毎フレーム積分し続ける - 「一定距離だけ瞬間
        // 移動」ではなく「短時間、主人公より速く前進」という質感の違いが
        // ここに表れる。タイマーが尽きたら即座に静止させるのではなく、
        // groundFollowAssistDuration秒だけ「敵のX速度を主人公の現在速度
        // ×groundFollowAssistFactorへ寄せる」追従補正フェーズへ引き継ぐ
        // (第4弾で追加 - 「攻撃後、距離が開きすぎて追撃しづらい」の対策)。
        // どちらのフェーズも完全な吸着(同じ座標への固定)ではなく、あくま
        // で速度の一時的な上乗せ/追従であることに注意。
        if (groundKnockbackTimer > 0f)
        {
            groundKnockbackTimer -= Time.deltaTime;
            transform.position += new Vector3(groundKnockbackVelocityX * Time.deltaTime, 0f, 0f);
            if (groundKnockbackTimer <= 0f)
            {
                groundKnockbackVelocityX = 0f;
                groundFollowAssistTimer = groundFollowAssistDuration;
            }
        }
        else if (groundFollowAssistTimer > 0f)
        {
            groundFollowAssistTimer -= Time.deltaTime;
            float followSpeed = PlayerForwardSpeed() * groundFollowAssistFactor;
            transform.position += new Vector3(followSpeed * Time.deltaTime, 0f, 0f);
            if (groundFollowAssistTimer <= 0f && normalGrounded)
            {
                // 接地中であればモーション処理を復帰する - 追従補正が
                // 終わった時点でまだ落下中(normalGrounded=false)なら
                // ここでは復帰せず、UpdateNormalGroundCheckの着地処理に
                // 委ねる(EnemySpecialBehavior/EnemyAnimatorが落下中に
                // 動き出してしまうのを防ぐため)。
                RestoreMotionComponents();
            }
        }

        UpdateNormalGroundCheck();
    }

    // 実機フィードバック(2026-09-12第4弾) - 「足場のない場所へ敵が吹き
    // 飛ばされた後もその場で立った状態になることがある」の修正。Launch/
    // Slam中(専用のY軸物理を持つ)を除く、通常状態(静止/ノックバック/
    // 追従補正いずれの間も)で毎フレーム「今のX位置に地面があるか」を
    // 監視する。地面が無くなった瞬間に即Airborneへ切り替えて重力落下させ、
    // 着地したら元の状態へ戻す/デッドラインを超えたら撃破処理する
    // (item 9 - 足場外への落下を正式な戦闘手段として成立させる)。
    // Flyingは対象外(EnemySpecialBehavior.UpdateFlyingが独自の高度管理を
    // 持ち、そもそも「地面」という概念に縛られないため)。
    void UpdateNormalGroundCheck()
    {
        if (movementType == EnemyMovementType.Flying) return;

        float? groundY = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(transform.position.x) : (float?)null;

        if (normalGrounded)
        {
            if (!groundY.HasValue)
            {
                normalGrounded = false;
                normalFallVelocityY = 0f;
                // 二重に無効化しても副作用はない(GetComponent<T>().enabled
                // =falseの再代入なだけ) - ノックバック/追従補正中に足場が
                // 切れた場合も含め、確実にモーション処理を止める。
                DisableMotionComponents();
                if (GameManager.Instance != null && GameManager.Instance.DebugMode)
                {
                    Debug.Log($"[GroundCheck] {name} lost ground at x={transform.position.x:F2} - falling");
                }
            }
            return;
        }

        // Airborne(通常落下) - Player.Move()の落下処理と同じ考え方で、
        // 一定の重力加速度に従って落下させる。
        normalFallVelocityY -= groundFallGravity * Time.deltaTime;
        transform.position += new Vector3(0f, normalFallVelocityY * Time.deltaTime, 0f);

        if (groundY.HasValue && transform.position.y <= groundY.Value)
        {
            Vector3 pos = transform.position;
            pos.y = groundY.Value;
            transform.position = pos;
            normalGrounded = true;
            normalFallVelocityY = 0f;
            // ノックバック/追従補正がまだ進行中でなければここでモーション
            // 処理を復帰する(進行中ならそちらの終了時に復帰される)。
            if (groundKnockbackTimer <= 0f && groundFollowAssistTimer <= 0f) RestoreMotionComponents();
            if (GameManager.Instance != null && GameManager.Instance.DebugMode)
            {
                Debug.Log($"[GroundCheck] {name} landed at y={groundY.Value:F2}");
            }
            return;
        }

        if (transform.position.y < enemyDeathBelowY)
        {
            // item 9 - 「足場外への落下は不具合として防ぐのではなく、
            // 戦闘上の正式な選択肢として成立させる」。通常のHitAndDie系の
            // 演出(Flash/Knockback/Burst/Fade)は経由せず、即座に撃破扱い
            // にする - 画面外へ落下していく最中なのでこれらの演出自体が
            // 見えないため、必要最小限の処理に留めた。
            if (GameManager.Instance != null && GameManager.Instance.DebugMode)
            {
                Debug.Log($"[GroundCheck] {name} fell past deadline y={transform.position.y:F2} - fall death");
            }
            dying = true;
            gameObject.SetActive(false);
            RegisterKillReward(fallDeath: true);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (dying) return;

        if (other.CompareTag("PlayerAttack"))
        {
            // マルチプレイPhase 2 - JOIN側のパペットはHPを持たない: ダメージ要求をHOSTへ送り、
            // 手応え(ヒットスパーク/SE/ヒットストップ/コンボ)だけをこの端末で出す。
            if (NetReplica) { NetReplicaHit(other); return; }

            EnsureHp();
            // The actual point the two colliders meet, not either object's
            // center - reads as "where the blade actually reached".
            Vector3 contactPoint = other.ClosestPoint(transform.position);
            int damage = PlayerController.Instance != null ? PlayerController.Instance.EffectiveAttackPower : 1;
            hp -= Mathf.Max(1, damage);
            bool killed = hp <= 0;

            PlayerAttackKind kind = PlayerAttackKind.Normal;
            var info = other.GetComponent<PlayerAttackInfo>();
            if (info != null) kind = info.kind;
            netReactionAttacker = 0; // この端末のプレイヤーの攻撃
            NetCombat.AuthorityDamaged(NetId, 0, Mathf.Max(1, damage), hp, (byte)kind, contactPoint, killed);

            // エリアルコンボ改修(2026-09-11), item 4 - 「空中で攻撃が敵に
            // ヒットした瞬間、プレイヤーの落下速度を少しだけ弱める」。
            // 敵の生死や種類を問わず、プレイヤーが空中にいる間の命中で
            // 常に発動する。
            if (PlayerController.Instance != null && !PlayerController.Instance.IsGrounded)
            {
                PlayerController.Instance.NotifyAerialHit();
            }
            // item 7 - コンボカウンター。命中のたびに必ず加算(倒した/倒し
            // ていないに関わらず「連続して当てた」という事実がコンボ)。
            if (ComboCounterUI.Instance != null) ComboCounterUI.Instance.RegisterHit();

            ProcessHit(kind, contactPoint, killed);
            return;
        }

        if (other.CompareTag("Player"))
        {
            // 実機フィードバック(2026-09-12第5弾) - 「Playerの攻撃がEnemy
            // へ命中し、Enemyが吹き飛ばされている最中なのに、その身体へ
            // 触れたことでPlayerもダメージを受ける」という相打ちの防止。
            // Enemy自身の攻撃HitBox(FireballController等、別の仕組み)は
            // 一切対象外 - あくまで「Enemy本体との接触ダメージ」だけを、
            // HitReaction/Knockback/Launched/Airborne/Slam中に限って無効化
            // する。
            if (IsReactingToHit) return;
            if (NetReplica && (dying || NetRemoteReacting)) return;
            if (PlayerController.Instance != null)
            {
                // マルチプレイPhase 2.5: JOINのパペットとの接触は「この敵(NetId)との接触」としてHOSTへ申告する。
                if (NetReplica) NetMatch.SetClaimContext(NetMatch.ClaimKind.EnemyContact, NetId);
                try { PlayerController.Instance.TakeDamage(source: "Enemy:" + name); }
                finally { NetMatch.ClearClaimContext(); }
            }
        }
    }

    // 実機フィードバック(2026-09-12第5弾) - 「Playerの攻撃が正常に命中して
    // いる最中のEnemy本体」との接触ダメージを無効化する条件。isSlammingは
    // isLaunchedの部分状態なので個別チェック不要。normalGroundedがfalse
    // (足場を失って落下中)も含める - これも広義の「攻撃によって行動を
    // 潰されている最中」であるため。dyingは呼び出し元のOnTriggerEnter2D
    // 冒頭で既にガード済みだが、他の場所からも安全に参照できるようここに
    // 含めておく。
    bool IsReactingToHit => dying || isLaunched || beingVacuumed
        || groundKnockbackTimer > 0f || groundFollowAssistTimer > 0f || !normalGrounded;

    // エリアルコンボ改修(2026-09-11) - 攻撃種別・現在の空中状態・致死判定
    // の組み合わせから、実際のリアクションを振り分ける中心メソッド。
    void ProcessHit(PlayerAttackKind kind, Vector3 contactPoint, bool killed)
    {
        // item 8 - 下攻撃フィニッシュ。浮いている敵への下攻撃は、致死でも
        // 即座には死なせず、地面へ叩き落としてから結果を出す。
        if (kind == PlayerAttackKind.Down && isLaunched)
        {
            StartCoroutine(ReactToHit(contactPoint, slamHitStopDuration));
            pendingDeathOnLand = killed;
            if (killed) dying = true; // これ以上の被弾判定は無視する(演出は着地までお預け)
            StartSlam();
            return;
        }

        if (killed)
        {
            dying = true;
            DisableMotionComponents();
            StartCoroutine(HitAndDie(contactPoint, viaSlam: false));
            return;
        }

        switch (kind)
        {
            case PlayerAttackKind.Up:
                // item 2 - 「敵を上方向へ打ち上げる、ここから空中コンボへ
                // 移行可能」。
                StartCoroutine(ReactToHit(contactPoint, hitStopDuration));
                LaunchUpward();
                break;

            case PlayerAttackKind.Down:
                // isLaunchedでない(浮いていない)敵への下攻撃 - 叩き落とす
                // 対象がないので通常ヒットと同じ扱い。
                StartCoroutine(ReactToHit(contactPoint, HitStopForPlayerAttack()));
                StartCoroutine(KnockbackRoutine(AwayDirFromPlayer(), hitKnockbackDistance, hitKnockbackDuration));
                break;

            case PlayerAttackKind.Normal:
            case PlayerAttackKind.DownImpact:
            default:
                StartCoroutine(ReactToHit(contactPoint, HitStopForPlayerAttack()));
                if (isLaunched)
                {
                    // item 2 - 「浮いている敵を追撃、少し前方向へ運ぶ、
                    // 攻撃がつながっている間は簡単に地面へ落ちない」。
                    ExtendJuggle();
                }
                else
                {
                    // 実機フィードバック(2026-09-12第3弾) - 「地上通常攻撃
                    // にも軽いノックバックを追加、敵をその場に固定しない」
                    // →「まだ密着してしまう」との追加報告を受け、距離ベース
                    // (KnockbackRoutine)から速度ベース(ApplyGroundKnockback)
                    // へ全面変更。
                    ApplyGroundKnockback();
                    if (PlayerController.Instance != null) PlayerController.Instance.NotifyGroundHitConnect();
                }
                break;
        }
    }

    // 竜騎士の3段目/急降下の着地など「強めのHitStop」を持つ攻撃(この端末のプレイヤーの攻撃だけ)。
    // 他キャラはAttackHitStopOverride=0なので従来のhitStopDurationのまま。
    float HitStopForPlayerAttack()
    {
        if (netReactionAttacker > 0 || PlayerController.Instance == null) return hitStopDuration;
        return Mathf.Max(hitStopDuration, PlayerController.Instance.AttackHitStopOverride);
    }

    float AwayDirFromPlayer()
    {
        if (netReactionAttacker > 0 && NetCombat.TryGetAttacker(netReactionAttacker, out Vector3 ap, out _)) return Mathf.Sign(transform.position.x - ap.x);
        return PlayerController.Instance != null ? Mathf.Sign(transform.position.x - PlayerController.Instance.transform.position.x) : 1f;
    }

    // 実機フィードバック(2026-09-12第3弾) - 「主人公の現在速度 + 上乗せ
    // 分」を短時間だけ与える、速度ベースの地上ノックバック。距離ベース
    // (旧KnockbackRoutine呼び出し)は「コルーチンの間だけ動いてすぐ止ま
    // る」ため、止まった瞬間から主人公の自動前進にすぐ追いつかれてしまい
    // 「攻撃→即密着」になっていた、というマスターの実機報告が根本原因。
    // OneMoreMileは距離でPlayerの走行速度が上がり続けるため、固定値では
    // いずれ機能しなくなる - PlayerController.CurrentAutoRunSpeedを毎回
    // 基準にすることで、どの速度状態でも「敵が主人公より少し速く前へ進む
    // 時間」を作れる。EnemySpecialBehavior(Chaser等)の毎フレーム上書きと
    // 競合しないよう、Launch開始時と同じDisableMotionComponentsを流用する
    // (Update()側のタイマー終了時にRestoreMotionComponentsで戻す)。
    void ApplyGroundKnockback()
    {
        // プレイアブル主人公追加(2026-09-12、お嬢様騎士) - KnockbackPower
        // Multiplier(既定1、弱いキャラほど<1)はEnemy側のこのボーナス分にの
        // み掛かる - PlayerForwardSpeed()自体(=主人公の現在の自動前進速度)
        // には触れないので、「敵に追いつかれない」ための速度パリティ自体
        // は弱いキャラでも常に保たれる(=一瞬で密着してしまうことはない)。
        float bonus = groundKnockbackSpeedBonus;
        if (PlayerController.Instance != null) bonus *= PlayerController.Instance.KnockbackPowerMultiplier;
        groundKnockbackVelocityX = PlayerForwardSpeed() + bonus;
        groundKnockbackTimer = groundKnockbackDuration;
        DisableMotionComponents();

        if (GameManager.Instance != null && GameManager.Instance.DebugMode)
        {
            Debug.Log($"[GroundKnockback] {name} velocityX={groundKnockbackVelocityX:F2} duration={groundKnockbackTimer:F2}");
        }
    }

    // 生存ヒット共通の「命中演出」(ヒットスパーク+ヒットストップ+被弾
    // フラッシュ) - 旧NonLethalHit/HitAndDie前半の共通部分を1箇所に統合。
    // 物理的なノックバック/打ち上げ/叩き落としは呼び出し側が別途担当する
    // (演出とリアクション物理を分離、Slam等で組み合わせを変えやすくする
    // ため)。
    IEnumerator ReactToHit(Vector3 contactPoint, float hitStopDur)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayAttackHit();

        if (hitParticleEnabled)
        {
            Sprite spark = hitSparkSprite != null ? hitSparkSprite : OneShotSpriteEffect.SoftDotSprite();
            OneShotSpriteEffect.CreateTweened(spark, contactPoint, hitParticleColor, duration: hitParticleDuration, startScale: hitParticleScale * 0.7f, endScale: hitParticleScale, sortingOrder: RenderOrder.CombatFx, holdFraction: hitParticleHoldFraction);
        }

        if (hitStopEnabled && hitStopDur > 0f) yield return HitStop.Freeze(hitStopDur);

        // Flash to hitFlashColor and back - a surviving enemy needs to
        // visibly return to normal, or every subsequent hit would just
        // look like nothing changed.
        if (hitFlashEnabled && sr != null)
        {
            Color normal = sr.color;
            sr.color = hitFlashColor;
            yield return new WaitForSecondsRealtime(Mathf.Max(0.04f, hitFlashHoldDuration));
            if (sr != null) sr.color = normal;
        }
    }

    // ===== 打ち上げ/空中追撃/叩き落とし =====

    void LaunchUpward()
    {
        // アエリアルコンボ追加調整(2026-09-12) - 「空中上攻撃の再Launchは
        // 無限に可能でよい」との明示的な指示のため、maxJuggleDurationに
        // よる打ち止めはここでは行わない(ExtendJuggle側の通常空中攻撃の
        // 滞空延長のみ、既存どおりこの安全装置の対象のまま)。

        bool wasAlreadyLaunched = isLaunched;
        isLaunched = true;
        isSlamming = false;

        // 地上からの初回Launchは大きく、空中での再Launch("空中上攻撃")は
        // それより弱く - 「地上上攻撃：大きく打ち上げる/空中上攻撃：もう
        // 一度軽く浮かせ直す」という強弱の差(項目4)。launchVelocityYを
        // 無条件に上書きするため、再Launch時点の落下速度も同時にリセット
        // される(項目4「現在の下降速度を一度弱める、またはリセット」)。
        float upSpeed = wasAlreadyLaunched ? airLaunchUpSpeed : launchUpSpeed;
        launchVelocityY = upSpeed;

        // 「斜め前方へ」は短時間バーストで表現する(Update()参照) - 主人公
        // との相対速度がLaunch中ずっと開き続けないよう、パリティ速度への
        // 一時的な上乗せとして(再)スタートする。
        launchForwardBurstVelocity = wasAlreadyLaunched ? airLaunchForwardBurstSpeed : launchForwardBurstSpeed;
        launchForwardBurstTimer = launchForwardBurstDuration;
        launchVelocityX = PlayerForwardSpeed() + launchForwardBurstVelocity;

        if (!wasAlreadyLaunched)
        {
            juggleElapsed = 0f;
            DisableMotionComponents();
        }

        // 不具合修正(2026-09-12) - 「実際にLaunch処理が適用されているか」
        // を実機のlogcatだけでも確認できるように(見た目が直った後の再発
        // 検知・今後の数値調整の当たりを付ける用途)。
        if (GameManager.Instance != null && GameManager.Instance.DebugMode)
        {
            Debug.Log($"[AerialCombo] LaunchUpward {name} y={transform.position.y:F2} velocityY={launchVelocityY:F2} velocityX={launchVelocityX:F2} alreadyLaunched={wasAlreadyLaunched}");
        }
    }

    // 実機フィードバック(2026-09-12第5弾) - 上攻撃のPickup/Vacuum。既に
    // 空中(isLaunched)にいる敵だけを対象にする - 地上の敵をここから引き
    // 寄せることはしない(それはMain HitBoxの役目)。PlayerController.
    // DoUpAttackが、上攻撃のたびにVacuum判定範囲内の対象へこれを呼ぶ。
    public void TryVacuumPickup(Vector2 offsetFromPlayer, float pullDuration)
    {
        if (NetReplica) { if (!dying) NetCombat.RequestVacuum(NetId, offsetFromPlayer, pullDuration); return; }
        if (dying || !isLaunched || beingVacuumed) return;
        beingVacuumed = true;
        StartCoroutine(VacuumPickupRoutine(offsetFromPlayer, pullDuration));
    }

    // 「瞬間移動にしない、0.08〜0.15秒程度でシュッと吸い込まれるように」。
    // 目標位置は主人公の"今の"位置を毎フレーム基準にする(固定座標を1回
    // だけ計算するのではなく) - 主人公は自動前進を続けるため、固定座標
    // だと吸い込み完了時には既にPlayerの後方に取り残されてしまう
    // (このセッションで繰り返し出てきた「相対速度は毎フレーム再計算する」
    // 教訓と同じ理由)。吸い込み終了時にLaunchUpward()を呼ぶことで、
    // 既存の「空中再Launch」の強さ(地上の初回Launchより弱い)をそのまま
    // 再利用する - item 5「斜め前上へ再Launch」に対応。
    IEnumerator VacuumPickupRoutine(Vector2 offsetFromPlayer, float pullDuration)
    {
        Vector3 start = transform.position;
        float t = 0f;
        while (t < pullDuration)
        {
            // 吸い込み中に別の攻撃で撃破された場合、このコルーチンは
            // Update()のdyingガードの外側で独立して動き続けてしまう
            // (StartCoroutineはUpdate()に紐づいていないため) - 死亡演出
            // (HitAndDie/DieFadeRoutine)と位置の取り合いにならないよう
            // 即座に打ち切る。
            if (dying) yield break;
            t += Time.deltaTime;
            float frac = Mathf.Clamp01(t / Mathf.Max(0.001f, pullDuration));
            // Ease-out - 「シュッと」勢いよく吸い込まれ、最後は緩やかに収まる。
            float eased = 1f - Mathf.Pow(1f - frac, 2f);
            Vector3 attackerPos = NetCombat.TryGetAttacker(netReactionAttacker, out Vector3 ap, out _) ? ap
                : (PlayerController.Instance != null ? PlayerController.Instance.transform.position : transform.position);
            Vector3 targetNow = attackerPos + new Vector3(offsetFromPlayer.x, offsetFromPlayer.y, 0f);
            transform.position = Vector3.Lerp(start, targetNow, eased);
            yield return null;
        }
        beingVacuumed = false;
        LaunchUpward();
    }

    // 主人公の現在の自動前進速度(距離によるSpeed Up込み) - 0ならPlayer
    // Controllerが見つからない場合のフォールバック。Update()で毎フレーム
    // 呼ばれ、Launch中の敵が常に主人公と同じ速度で並走する基準となる
    // (パリティ、上のlaunchVelocityXの説明コメント参照)。
    // マルチプレイPhase 2 - ノックバック/打ち上げの基準は「この敵を最後に攻撃したプレイヤー」
    // (シングル/HOST自身の攻撃では従来どおりこの端末のプレイヤー)。
    float PlayerForwardSpeed()
    {
        if (netReactionAttacker > 0 && NetCombat.TryGetAttacker(netReactionAttacker, out _, out float sp)) return sp;
        return PlayerController.Instance != null ? PlayerController.Instance.CurrentAutoRunSpeed : 0f;
    }

    void ExtendJuggle()
    {
        if (!isLaunched || juggleElapsed >= maxJuggleDuration) return;
        launchVelocityY = Mathf.Max(launchVelocityY, juggleHoverFallSpeed);
        // item 5 - 「空中通常攻撃は敵を前へ運ぶ」。Launchと同じ短時間
        // バースト方式(Update()参照) - ヒットのたびに再スタートする。
        launchForwardBurstVelocity = juggleForwardBurstSpeed;
        launchForwardBurstTimer = launchForwardBurstDuration;
    }

    void StartSlam()
    {
        isSlamming = true;
        launchVelocityY = -slamSpeed;
        // item 6 - 下攻撃はコンボ終了用の縦の叩き落とし。水平方向の
        // ドリフトが残っていると斜めに落ちてしまい「叩き落とす」フィニ
        // ッシュの見た目を損なうため、Slam開始時にゼロへ戻す(バーストも
        // 明示的に打ち切り、Update()の!isSlammingガードと合わせて二重に
        // 保証する)。
        launchVelocityX = 0f;
        launchForwardBurstTimer = 0f;
    }

    void LandFromLaunch(float landY)
    {
        isLaunched = false;
        bool wasSlamming = isSlamming;
        isSlamming = false;
        launchVelocityX = 0f;
        launchForwardBurstTimer = 0f;
        transform.position = new Vector3(transform.position.x, landY, transform.position.z);

        if (GameManager.Instance != null && GameManager.Instance.DebugMode)
        {
            Debug.Log($"[AerialCombo] LandFromLaunch {name} y={landY:F2} wasSlamming={wasSlamming} pendingDeath={pendingDeathOnLand}");
        }

        if (wasSlamming && pendingDeathOnLand)
        {
            pendingDeathOnLand = false;
            // dyingは既にProcessHitの時点でtrue - HitAndDieが以降の演出を
            // 引き継ぐ(DisableMotionComponentsは打ち上げ開始時に済んでい
            // るので再度呼ぶ必要はない)。
            StartCoroutine(HitAndDie(transform.position, viaSlam: true));
            return;
        }

        RestoreMotionComponents();

        if (wasSlamming)
        {
            // item 8 - 生存した場合も、地面到達の衝撃自体は演出として出す。
            StartCoroutine(SlamImpactRoutine());
        }
    }

    // item 8 - 「地面到達 -> 地面衝撃VFX -> 通常より強めのヒットストップ」
    // を、致死ではなく生存した場合にも(小さめに)再現する。
    IEnumerator SlamImpactRoutine()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayAttackHit();

        Sprite impactSprite = TerrainManager.Instance != null ? TerrainManager.Instance.enemyGroundImpactSprite : null;
        if (impactSprite != null)
        {
            OneShotSpriteEffect.CreateTweened(impactSprite, transform.position, Color.white, duration: 0.3f, startScale: groundImpactScale * 0.7f, endScale: groundImpactScale, sortingOrder: RenderOrder.CombatFx, holdFraction: 0.2f);
        }

        if (hitStopEnabled) yield return HitStop.Freeze(slamHitStopDuration);

        if (hitKnockbackEnabled && slamSurviveBounce > 0f)
        {
            StartCoroutine(KnockbackRoutine(AwayDirFromPlayer(), slamSurviveBounce, hitKnockbackDuration));
        }
    }

    // 不具合修正(2026-09-12) - Awakeキャッシュをやめ、呼ばれるたびに
    // GetComponentする(このメソッド自体は打ち上げ開始/着地の2回程度しか
    // 呼ばれないため、毎回のGetComponentコストは無視できる)。
    void DisableMotionComponents()
    {
        var animator = GetComponent<EnemyAnimator>();
        if (animator != null) animator.enabled = false;
        var special = GetComponent<EnemySpecialBehavior>();
        if (special != null) special.enabled = false;
    }

    void RestoreMotionComponents()
    {
        // dying中(死亡演出突入後)は絶対に復帰させない - HitAndDie/
        // DieFadeRoutineが引き続きこのTransformを排他的に握っている。
        if (dying) return;
        var animator = GetComponent<EnemyAnimator>();
        if (animator != null) animator.enabled = true;
        var special = GetComponent<EnemySpecialBehavior>();
        if (special != null) special.enabled = true;
    }

    // ===== 撃破 =====

    // viaSlam=true - item 8の下攻撃フィニッシュ経由(地面衝撃VFX+強め
    // ヒットストップを先に処理してから、通常の撃破演出(Flash/Knockback/
    // Burst/Fade)へ合流する)。
    IEnumerator HitAndDie(Vector3 contactPoint, bool viaSlam)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayAttackHit();

        if (viaSlam)
        {
            Sprite impactSprite = TerrainManager.Instance != null ? TerrainManager.Instance.enemyGroundImpactSprite : null;
            if (impactSprite != null)
            {
                OneShotSpriteEffect.CreateTweened(impactSprite, transform.position, Color.white, duration: 0.3f, startScale: groundImpactScale * 0.7f, endScale: groundImpactScale * 1.15f, sortingOrder: RenderOrder.CombatFx, holdFraction: 0.2f);
            }
            if (hitStopEnabled) yield return HitStop.Freeze(slamHitStopDuration);
        }

        // item 2/最終確認3 - 「敵を倒した攻撃：通常ヒットより大きく吹き
        // 飛ぶ」。非致死ヒットと同じKnockbackRoutineを、距離/時間だけ
        // 大きくして再利用する。
        if (hitKnockbackEnabled)
        {
            StartCoroutine(KnockbackRoutine(AwayDirFromPlayer(), hitKnockbackDistance * deathKnockbackDistanceMultiplier, hitKnockbackDuration * deathKnockbackDurationMultiplier));
        }

        if (hitParticleEnabled)
        {
            Sprite spark = hitSparkSprite != null ? hitSparkSprite : OneShotSpriteEffect.SoftDotSprite();
            OneShotSpriteEffect.CreateTweened(spark, contactPoint, hitParticleColor, duration: hitParticleDuration, startScale: hitParticleScale * 0.7f, endScale: hitParticleScale, sortingOrder: RenderOrder.CombatFx, holdFraction: hitParticleHoldFraction);
        }

        if (hitFlashEnabled && sr != null) sr.color = hitFlashColor;

        // viaSlamの場合は上で既に強めのHitStopを消化済み - 通常の
        // hitStopDurationを二重にかけると「叩き落とし」の一撃なのに
        // 停止が長すぎてテンポを損なうため、ここではスキップする。
        if (!viaSlam && hitStopEnabled) yield return HitStop.Freeze(hitStopDuration);
        if (hitFlashHoldDuration > 0f) yield return new WaitForSecondsRealtime(hitFlashHoldDuration);

        if (deathCloudSprite != null)
        {
            OneShotSpriteEffect.CreateTweened(deathCloudSprite, transform.position, Color.white, duration: deathCloudDuration, startScale: deathCloudScale * 0.7f, endScale: deathCloudScale, sortingOrder: RenderOrder.CombatFx, holdFraction: deathCloudHoldFraction);
        }

        // 敵撃破時の飛散パーティクル(2026-09-10) - 敵のワールド高さ(sr.
        // boundsはlossyScale込みの実寸)を渡して、数/粒サイズ/飛散速度を
        // 大きさに比例させる。色は種族ごと(雑魚敵の既定は青)。
        if (deathBurstEnabled)
        {
            float subjectHeight = sr != null ? sr.bounds.size.y : 1f;
            ExplosionEffect.CreateForDefeat(transform.position, deathBurstColor, subjectHeight, sortingOrder: RenderOrder.CombatFx);
        }

        if (AudioManager.Instance != null) AudioManager.Instance.PlayEnemyDefeat();

        // The enemy's own sprite side of "Hit -> Flash -> Fade/Scale ->
        // 消滅".
        if (sr != null) yield return DieFadeRoutine();

        gameObject.SetActive(false);
        RegisterKillReward(fallDeath: false);
    }

    IEnumerator DieFadeRoutine()
    {
        Color startColor = sr.color;
        Vector3 startScale = sr.transform.localScale;
        float t = 0f;
        while (t < dieFadeDuration)
        {
            t += Time.unscaledDeltaTime;
            float frac = Mathf.Clamp01(t / dieFadeDuration);
            Color c = startColor;
            c.a = Mathf.Lerp(startColor.a, 0f, frac);
            sr.color = c;
            sr.transform.localScale = Vector3.Lerp(startScale, startScale * 0.6f, frac);
            yield return null;
        }
    }

    // A short eased punch-back-and-settle instead of an instant position
    // snap - out fast, drifts back only partway (doesn't undo the whole hit,
    // reads as "knocked" not "teleported and un-teleported"). distance/
    // duration are now explicit params (エリアルコンボ改修 2026-09-11 -
    // 以前はhitKnockbackDistance/Durationフィールドを直接読んでいたが、
    // 撃破時に別の倍率をかけた値で同じ演出を再利用したいため引数化した)。
    //
    // retainFraction(2026-09-12第2弾追加) - 最終的にpeakのうち何割を保持
    // するか。デフォルト0.5は既存呼び出し(下攻撃/死亡時/Slam生存時)の
    // 挙動を完全に維持する(＝旧来の「半分だけ戻る」)。地上通常攻撃だけは
    // 1.0(完全保持、戻らない)で呼び出す - 「攻撃するたびに敵と主人公が
    // 一緒に少しずつ前へ移動する」という積み上げ式の前進に、戻りの曖昧さ
    // を持ち込まないため。
    IEnumerator KnockbackRoutine(float dir, float distance, float duration, float retainFraction = 0.5f)
    {
        Vector3 start = transform.position;
        Vector3 peak = start + new Vector3(distance * dir, 0f, 0f);
        float t = 0f;
        while (t < duration)
        {
            // Bugfix 2026-09-06, item 1 - "Level Up Card選択中にEnemyが動き
            // 続ける" - Time.timeScaleに従う(HitStop.cs参照)。
            t += Time.deltaTime;
            float frac = Mathf.Clamp01(t / duration);
            // Out quickly (eased), then settle back toward (1-retainFraction)
            // of the peak - never fully undoes the punch, so it still reads
            // as a net shove.
            float outFrac = 1f - Mathf.Pow(1f - Mathf.Clamp01(frac / 0.4f), 2f);
            float settleFrac = frac > 0.4f ? Mathf.Clamp01((frac - 0.4f) / 0.6f) * (1f - retainFraction) : 0f;
            transform.position = Vector3.Lerp(start, peak, Mathf.Clamp01(outFrac) - settleFrac);
            yield return null;
        }
    }

    // ===================================================================== //
    // マルチプレイPhase 2 - 共有敵(HOST=本体/JOIN=パペット)
    // ===================================================================== //

    [System.NonSerialized] public int NetId;
    [System.NonSerialized] public bool NetReplica;
    [System.NonSerialized] public bool NetRemoteReacting;
    [System.NonSerialized] public bool NetLocalFlashActive;
    int netReactionAttacker; // 0 = この端末のプレイヤー / それ以外 = プレイヤー番号
    int netDisplayHp = -1;
    float netLocalHitCooldown;
    Collider2D netLastHitCollider;

    public int NetHp => NetReplica ? netDisplayHp : (hp < 0 ? Mathf.Max(1, maxHp) : Mathf.Max(0, hp));
    public bool NetIsReacting => IsReactingToHit;

    // 撃破報酬の付与(シングル/HOST自身がラストヒットなら従来どおりこの端末で付与)。
    void RegisterKillReward(bool fallDeath)
    {
        if (NetCombat.RouteEnemyKillReward(NetId, fallDeath)) return;
        if (GameManager.Instance != null) GameManager.Instance.RegisterEnemyKill(mileReward);
    }

    // JOIN: HOSTから届いた敵を「見た目と当たり判定だけ」のパペットにする。
    public void NetMakeReplica(int id, int currentHp)
    {
        NetId = id;
        NetReplica = true;
        netDisplayHp = currentHp;
        var animator = GetComponent<EnemyAnimator>();
        if (animator != null) animator.enabled = false;
        var special = GetComponent<EnemySpecialBehavior>();
        if (special != null) special.enabled = false;
        var facing = GetComponent<EnemyFacing>();
        if (facing != null) facing.enabled = false;
    }

    // JOIN: 自分の攻撃がパペットに当たった。
    void NetReplicaHit(Collider2D other)
    {
        if (dying) return;
        // 同じ攻撃判定が補間による位置更新で出入りを繰り返しても1回として扱う(1Hitが複数ダメージに
        // ならないように)。別の攻撃(判定の出し直し)は十分後なので通る。
        if (other == netLastHitCollider && netLocalHitCooldown > Time.time) return;
        netLastHitCollider = other;
        netLocalHitCooldown = Time.time + 0.18f;

        Vector3 contactPoint = other.ClosestPoint(transform.position);
        int damage = PlayerController.Instance != null ? PlayerController.Instance.EffectiveAttackPower : 1;
        PlayerAttackKind kind = PlayerAttackKind.Normal;
        var info = other.GetComponent<PlayerAttackInfo>();
        if (info != null) kind = info.kind;
        NetCombat.RequestHit(NetId, Mathf.Max(1, damage), kind, contactPoint);

        if (PlayerController.Instance != null && !PlayerController.Instance.IsGrounded) PlayerController.Instance.NotifyAerialHit();
        if (ComboCounterUI.Instance != null) ComboCounterUI.Instance.RegisterHit();
        StartCoroutine(NetLocalHitFx(contactPoint, hitStopDuration));
    }

    IEnumerator NetLocalHitFx(Vector3 contactPoint, float hitStopDur)
    {
        NetLocalFlashActive = true;
        yield return ReactToHit(contactPoint, hitStopDur);
        NetLocalFlashActive = false;
    }

    // HOST: JOINのプレイヤーの攻撃を、この端末の攻撃と全く同じ被弾処理へ流す
    // (ノックバック/打ち上げ/叩き落としの基準だけ攻撃したプレイヤーにする)。
    public void NetApplyRemoteHit(int attacker, int damage, PlayerAttackKind kind, Vector3 contactPoint)
    {
        if (dying || NetReplica) return;
        EnsureHp();
        hp -= Mathf.Max(1, damage);
        bool killed = hp <= 0;
        netReactionAttacker = attacker;
        NetCombat.AuthorityDamaged(NetId, attacker, Mathf.Max(1, damage), hp, (byte)kind, contactPoint, killed);
        ProcessHit(kind, contactPoint, killed);
    }

    public void NetRemoteVacuum(int attacker, Vector2 offset, float duration)
    {
        if (dying || NetReplica || !isLaunched || beingVacuumed) return;
        netReactionAttacker = attacker;
        beingVacuumed = true;
        StartCoroutine(VacuumPickupRoutine(offset, duration));
    }

    // JOIN: HOSTが確定したHP。他のプレイヤーの攻撃なら手応え(スパーク/フラッシュ)をここで出す。
    public void NetOnAuthoritativeHp(int newHp, bool showHitFx, Vector3 contactPoint)
    {
        netDisplayHp = newHp;
        if (showHitFx && !dying && isActiveAndEnabled) StartCoroutine(NetLocalHitFx(contactPoint, 0f));
    }

    // JOIN: HOSTが死亡を確定した(以降は当たり判定も接触ダメージも無し)。
    public void NetMarkDead()
    {
        dying = true;
        netDisplayHp = 0;
        var col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;
        if (sr != null && hitFlashEnabled) { NetLocalFlashActive = true; sr.color = hitFlashColor; }
    }

    // JOIN: HOSTの撃破演出が終わって消えた瞬間に、同じ撃破演出(煙/飛散/SE/フェード)を出して消す。
    public void NetPlayDeathVisualAndRemove()
    {
        if (!isActiveAndEnabled) { Destroy(gameObject); return; }
        StartCoroutine(NetDeathRoutine());
    }

    IEnumerator NetDeathRoutine()
    {
        if (deathCloudSprite != null)
            OneShotSpriteEffect.CreateTweened(deathCloudSprite, transform.position, Color.white, duration: deathCloudDuration, startScale: deathCloudScale * 0.7f, endScale: deathCloudScale, sortingOrder: RenderOrder.CombatFx, holdFraction: deathCloudHoldFraction);
        if (deathBurstEnabled)
        {
            float subjectHeight = sr != null ? sr.bounds.size.y : 1f;
            ExplosionEffect.CreateForDefeat(transform.position, deathBurstColor, subjectHeight, sortingOrder: RenderOrder.CombatFx);
        }
        if (AudioManager.Instance != null) AudioManager.Instance.PlayEnemyDefeat();
        if (sr != null) yield return DieFadeRoutine();
        Destroy(gameObject);
    }
}
