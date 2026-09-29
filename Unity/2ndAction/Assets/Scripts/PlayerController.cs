using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(BoxCollider2D))]
public partial class PlayerController : MonoBehaviour
{
    // Operation System Ver.2 (2026-09-06) - "タップ=ジャンプ/前後スワイプ=
    // 攻撃" を廃止し、全操作を方向フリックによる「移動攻撃」へ統一。
    // Neutral (旧: キーボードZ/Bテスト用の「その場攻撃」)は廃止 - 新設計
    // では全ての攻撃が必ず移動を伴う("攻撃することで移動する")ため、方向
    // なしの攻撃という概念自体が存在しない。
    public enum AttackDirection { Forward, Backward }

    // 生のフリックジェスチャー分類 - Forward/Backwardはそのまま
    // AttackDirectionへ1:1で流れ込み既存のコンボ/Lunge処理(HandleAttackInput
    // /DoAttack)をそのまま再利用するが、UpはForward/Backwardの3段コンボ
    // チェーンとは完全に別系統として扱う(DoUpAttack参照) - 既存の
    // Forward/Backwardコンボを壊すリスクを避けるため。
    // 方向攻撃システム Ver.2(2026-09-07)、項目1 - Downを追加、4方向に拡張。
    // Downも(Up同様)Forward/Backwardのコンボ系統には一切関与しない、独立
    // した「空中下降攻撃」専用トリガー(Move()内で直接処理 - DoDiveAttack
    // 参照)。地上でのDownフリックは項目4の指示どおり無効(何もしない)。
    public enum FlickDirection { Up, Forward, Backward, Down }

    [Header("Move")]
    public float runSpeed = 5f;
    public float jumpForce = 9f;
    public float gravity = 20f;
    // Vertical distance from the ground line (TerrainManager.GetHeightAt)
    // up to where the player's transform sits when grounded. Left at 0
    // because every player sprite now uses a per-frame "foot pivot"
    // (ComputeLowestContentPivotY in SceneBuilder) - transform.position.y
    // IS the sprite's visible foot position, so any positive value here
    // would lift the visible foot that many units above the actual ground
    // surface. This used to be 0.5 back when the sprite had a dead-center
    // pivot and needed lifting so a 1-unit-tall BoxCollider2D's bottom
    // edge would land on the ground line - that assumption no longer
    // holds now that grounding is purely visual/math-driven (GetHeightAt),
    // not collider/physics-driven.
    public float groundOffset = 0f;
    public float failY = -8f;
    public int maxJumps = 2;

    // 方向攻撃システム Ver.2、項目5 - "ここより下へ落ちると死亡する"落下
    // デッドラインの視認性改善。下降攻撃(項目3)でプレイヤーが自分の意思
    // で下方向へ移動できるようになったため、常時ではなく「近づいたとき
    // だけ段階的に」警告を出す(常時大きな赤線などにはしない、との明示的
    // な指示)。Player自身のY座標基準(カメラのY追従はSmoothDampで多少遅
    // 延するため、下降攻撃のような速い下降中はカメラ側の値だと警告が実
    // 際の危険より遅れる恐れがある - 見た目の画面位置ではなく実際の危険
    // 度に忠実な基準を優先)。
    [Header("Fall Deadline Warning (Direction Attack System Ver.2, item 5)")]
    // failYまでこの距離を切ったら警告が出始める(この値より遠ければ完全
    // に非表示 - 通常プレイでは見えない、という要件)。
    public float deadlineWarningStartDistance = 5f;
    public Color deadlineWarningColor = new Color(0.75f, 0.12f, 0.12f);
    // Bugfix 2026-09-08, item6 - 「デッドラインが見ずらいため、デッドラ
    // インより下を赤い雲で覆い隠すように表示して」。上の4つは「画面下端
    // からの固定割合」でぼんやり滲ませるだけで、実際のデッドライン(failY)
    // の画面上の位置とは無関係だった - 見た目の警告と実際の危険度が視覚
    // 的に一致しないため分かりづらかった。以下は failY を実際にワールド
    // →スクリーン変換し、その線から下を丸ごと不透明に近い赤霧で覆う
    // (=文字通り"デッドラインより下"を覆い隠す)ための追加設定。
    // fadeBandHeight: 覆いの上端(デッドライン位置)にモヤ状のグラデーシ
    // ョンを付ける高さ(px) - 硬い直線で切り替わらないようにする。
    // solidAlpha: 覆いの本体(fadeBand より下)の不透明度 - 0.4程度だった
    // 旧実装よりずっと濃く、実質「見えなくする」レベルまで強める。
    public float deadlineFogFadeBandHeight = 180f;
    public float deadlineFogSolidAlpha = 0.92f;
    // Visually tilts the whole player to match the ground slope while
    // grounded (never while airborne/jumping, and never on the always-flat
    // sky path), so an upright sprite doesn't show a wedge-shaped gap on
    // one side of the feet on an incline. Degrees/sec for the smoothing.
    public float slopeTiltSpeed = 720f;

    // Kept on during the boss fight too - the dragon tracks the player's
    // base auto-run speed every frame, so it holds a constant distance
    // while running regardless.
    public bool autoRunEnabled = true;

    [Header("Speed Ramp")]
    public float speedUpStartDistance = 100f;
    public float speedUpPer100m = 0.05f;
    public float maxSpeedMultiplier = 2f;

    // エリアルコンボ改修(2026-09-11), item 4 - 「空中で攻撃が敵にヒットし
    // た瞬間、プレイヤーの落下速度を少しだけ弱める」。EnemyController.
    // OnTriggerEnter2Dが(プレイヤーが空中にいる間の命中で)毎回
    // NotifyAerialHit()を呼ぶ - どの攻撃種別か・敵が浮いているかどうかは
    // 一切問わない、「空中で当てた」という事実だけで発動するシンプルな
    // 仕組み。完全な空中停止にはせず、①落下速度を少しリセット、②短時間
    // だけ重力を弱める、の2つを組み合わせる。最大滞空時間(累積)の
    // ハードキャップも用意し、連打で無限に浮き続けられないようにする。
    [Header("Aerial Assist (空中攻撃時の滞空補助)")]
    // ヒットの瞬間、現在の落下速度がこれより速ければこの値まで戻す
    // (0にはしない = 完全な空中停止を避ける、"少しだけ"の補正)。
    public float aerialAssistFallResetSpeed = -2f;
    // 上のリセット直後から、この時間だけ重力の影響を弱める(次のヒットで
    // 上書き/延長される)。
    public float aerialAssistWindowDuration = 0.22f;
    // ウィンドウ中にかかる重力の割合(1=通常のまま、0=無重力)。
    [Range(0f, 1f)] public float aerialAssistGravityScale = 0.35f;
    // 安全装置 - 空中にいる間(着地するまで)にこの補助を使える合計時間の
    // 上限。連続ヒットでも無限に浮遊し続けないようにする。着地すると
    // リセットされる。
    public float aerialAssistMaxTotalDuration = 1.2f;

    // 実機フィードバック(2026-09-12第3弾) - 「敵側の速度ベースノックバック
    // だけで十分な間合いが作れない場合の補助策」としてマスターから提案
    // された、地上通常攻撃が命中した瞬間だけ主人公の前進速度をごく短時間
    // 弱める仕組み。「主人公を停止させない」程度に留めるため既定は無効
    // (factor=0)- Enemy側の調整だけで十分な間合いが確保できるか実機で
    // 確認した上で、必要ならInspectorから有効化する想定。
    [Header("Ground Hit Connect Assist (地上通常攻撃命中時、任意で主人公を一瞬だけ減速)")]
    // 0 = 無効(既定)、1 = その間完全停止。疾走感を壊さない範囲(0.1〜0.3
    // 程度)を目安に。
    [Range(0f, 1f)] public float groundHitConnectSlowdownFactor = 0f;
    public float groundHitConnectSlowdownDuration = 0.08f;
    float groundHitConnectSlowdownTimer;

    [Header("Attack")]
    public Collider2D attackHitbox;
    public AttackSlashVisual attackSlashVisual;
    public float attackActiveTime = 0.4f;
    public float attackCooldown = 0.3f;
    // Fraction of attackActiveTime after which a fresh attack input is
    // buffered to chain immediately into the next attack (the combo window).
    public float comboWindowStart = 0.5f;
    // After this many chained attacks in a row, the combo window is skipped
    // so the player is forced through the normal cooldown gap before
    // attacking again, instead of chaining forever.
    public int maxComboChain = 3;
    public float lungeDistance = 1.2f;
    public float recoilDistance = 1.0f;
    // Attack range grows across the combo chain (small -> medium -> large),
    // matching the growth of the slash FX, so a bigger-looking swing also
    // actually reaches further.
    public float hitboxScaleStep = 0.18f;
    public float hitboxReachStep = 0.25f;

    // Operation System Ver.2, item 2 - "上フリック=ジャンプ攻撃"。物理挙動
    // は既存のジャンプ(velocityY=jumpForce)をそのまま流用、見た目だけ
    // 「攻撃しながら上昇している」ように専用のHitbox+Slash FXを追加する。
    // isAttacking/comboCount/DoAttackの3段コンボ系統には一切触れない、
    // 完全に独立した仕組み(既存Forward/Backwardコンボを壊さないため) -
    // 詳細はDoUpAttackのコメント参照。地上からの1段目(通常ジャンプ相当)/
    // 空中での2段目(二段ジャンプ相当)でSlash FXのステージ・SEを変えて
    // 「上昇するにつれて強くなる」印象だけ出す(既存の
    // AttackSlashVisual.SetComboStage/AudioManager.PlayAttackをそのまま
    // 再利用、新規アセット不要)。
    [Header("Up Attack (Flick Ver.2 - jump-linked, separate from the Forward/Backward combo)")]
    public Collider2D upAttackHitbox;
    public AttackSlashVisual upAttackSlashVisual;
    public float upAttackActiveTime = 0.28f;

    // 実機フィードバック(2026-09-12第5弾) - 「上攻撃で主人公の真上付近の
    // Enemyも拾い直せるように」。upAttackHitbox(ダメージ判定、前方～斜め
    // 前上)とは別に、真上を中心とした「Pickup/Vacuum」範囲を追加。この
    // Colliderは通常のトリガー通知には使わず(EnemyController.OnTriggerEnter2D
    // が拾わないよう"PlayerAttack"タグは付けていない)、DoUpAttackが上攻撃
    // のたびにこの.boundsだけを読み取ってPhysics2D.OverlapBoxAllを手動実行
    // する - 既に空中(isLaunched)にいる敵だけを対象に、主人公の斜め前上
    // (vacuumTargetOffset)へ短時間(vacuumPullDuration)かけて引き寄せた上で
    // 再Launchする(EnemyController.TryVacuumPickup参照)。
    [Header("Up Attack Pickup / Vacuum (頭上のEnemyをコンボへ拾い直す)")]
    public Collider2D upAttackVacuumHitbox;
    // "瞬間移動にしない、0.08〜0.15秒程度でシュッと" - マスター指定のレンジ。
    public float vacuumPullDuration = 0.12f;
    // 引き寄せ後の目標位置(主人公からの相対オフセット、ワールド座標系) -
    // 「主人公→斜め前上にEnemy」という位置関係を作る。
    public Vector2 vacuumTargetOffset = new Vector2(1.0f, 1.2f);

    // 方向攻撃システム Ver.2、項目3 - "空中で↓フリック=下降攻撃"。上昇攻撃
    // (DoUpAttack)と同じ「isAttacking/comboCount/DoAttackの3段コンボ系統
    // には一切関与しない独立した仕組み」という設計方針をそのまま踏襲。
    // 上昇攻撃が「短いパルス」だったのに対し、下降攻撃は「着地するまで持
    // 続するHitbox+専用アニメーション」という違いがある(DoDiveAttack/
    // Move()の着地処理参照)。地上での↓フリックは項目4の指示どおり無効
    // (isGrounded中はこのHitbox/Stateが一切トリガーされない)。
    [Header("Down Attack (Flick Ver.2 - air-only dive attack, separate from the Forward/Backward combo)")]
    public Collider2D downAttackHitbox;
    public AttackSlashVisual downAttackSlashVisual;
    // "素早く下降" - 通常の重力落下より明確に速い、一定の下降速度に上書き
    // する(重力による自然加速ではなく、攻撃の勢いによる下降という体感を
    // 優先)。
    public float diveAttackSpeed = 16f;
    // 不具合修正(2026-09-10) - 「下攻撃の着地時に衝撃エフェクトを追加し、
    // それにも攻撃判定が入るように」。これまで下降攻撃のHitboxは着地の
    // 瞬間(EndDiveAttack)に無効化されるだけで、着地の衝撃そのものは誰に
    // もダメージを与えていなかった。着地の瞬間だけ短時間(diveImpact
    // HitboxDuration)有効になる別Hitbox+専用の衝撃VFXを追加し、着地の一瞬
    // だけ地面付近の敵をまとめて巻き込めるようにする(DoDiveAttack中の
    // Hitboxとは別オブジェクト、Forward/Backward・上/下降本体のコンボ系統
    // には一切触れない)。
    public Collider2D downAttackLandHitbox;
    public AttackSlashVisual downAttackLandSlashVisual;
    public float diveImpactHitboxDuration = 0.15f;

    // Operation System Ver.2 (2026-09-06) - 旧「タップ=ジャンプ/前後スワイ
    // プ=攻撃」を廃止し、全操作を上/前/後の3方向フリックに統一。
    // フリック成立は指を離すまで待たず(pointerDown中に閾値超過した瞬間に
    // 発動)、閾値は「距離」(flickDistanceThreshold、じっくりした操作向け)
    // と「速度」(flickVelocityThreshold、素早い短いフリック向け - 上攻撃
    // =実質ジャンプの反応遅延を大きくしないため特に重要)の2way判定。
    // 斜めフリックは3方向のうち最も近いものへ内積で吸着させる
    // (ClassifyFlickDirection参照) - 「厳密な方向入力を要求しない」との
    // 指示どおり、下向きフリックも自動的にForward/Backwardいずれかへ解決
    // される(Up方向とは常に90°以上離れるため誤ってUpと判定されることは
    // ない)。
    [Header("Touch Controls - Flick Ver.2")]
    public float flickDistanceThreshold = 60f;
    public float flickVelocityThreshold = 1400f;
    public float flickMinDistanceForVelocityTrigger = 16f;

    [Header("Death")]
    public Sprite explosionParticleSprite;
    public Color explosionColor = new Color(0.3f, 0.7f, 1f);

    [Header("Hit / Lives")]
    // 旧: 被弾直後から5秒の無敵点滅。2026-09-22の被弾リアクション導入で、被弾直後のHurt(下記)の後に続く
    // 無敵点滅は hurtInvincibleDuration に置き換えた(この値は互換のため残すが使用しない)。
    public float hitInvincibleDuration = 5f;
    public float hitFlickerInterval = 0.1f;

    // ===== 被弾リアクション(2026-09-22) =====
    // 通常被弾: Hurt(自動前進停止+入力不可+軽いノックバック+Hurtアニメ) → Run復帰 → 無敵点滅。
    // 穴からの復帰: Recovery(復帰位置で短い停止+着地アニメ) → Run再開 → 無敵点滅。
    // 数値はキャラクターごとにCharacterDefinitionで上書きできる(0/未指定ならこの既定値)。
    [Header("Hit Reaction (被弾リアクション)")]
    public float hurtDuration = 0.25f;            // Hurt中の停止時間
    public float hurtKnockbackSpeed = 4f;         // Hurt中に後方へ押し戻す初速(Hurt時間で減衰、約0.5ユニット)
    public float hurtInvincibleDuration = 0.7f;   // Hurt終了後の点滅無敵
    public float recoveryDuration = 0.45f;        // 落下復帰後のRecovery停止時間
    public float recoveryInvincibleDuration = 1.0f; // Recovery終了後の点滅無敵

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public FlickDirection? debugInjectFlick; // 自動テスト用: 毎フレームこの入力があったことにする(マルチプレイ自動テストのため開発ビルドでも有効)
#endif
    public enum ReactionKind { None, Hurt, Recovery }
    public ReactionKind Reaction { get; private set; }
    public bool IsReacting => Reaction != ReactionKind.None;
    public bool IsHurt => Reaction == ReactionKind.Hurt;
    public bool IsRecovering => Reaction == ReactionKind.Recovery;
    // 0(開始)〜1(終了)。アニメーション側が姿勢を補間するのに使う。
    public float ReactionProgress => reactionTotal > 0.0001f ? Mathf.Clamp01(1f - reactionTimer / reactionTotal) : 1f;
    float reactionTimer, reactionTotal;
    // キャラクター別の上書き(ApplyCharacterBaseStats)。0=既定値を使う、倍率は1=既定。
    float charHurtDuration, charHurtInvincible, charRecoveryDuration, charRecoveryInvincible;
    float charHurtKnockbackMultiplier = 1f;
    int attackGeneration; // 攻撃コルーチンの世代。被弾で進めると実行中のDoAttackが打ち切られる

    [Header("Game Feel - Damage Feedback (tunable)")]
    public bool damageFlashEnabled = true;
    public Color damageFlashColor = new Color(1f, 0.35f, 0.35f);
    public float damageFlashDuration = 0.12f;
    // Small - "小さなKnockback" per the brief, applied as a brief backward
    // velocity (see ApplyKnockback/Move) rather than a direct
    // transform.position write, since Move() overwrites transform.position
    // from scratch every frame anyway (a raw position offset would just get
    // stomped the very next frame).
    public bool damageKnockbackEnabled = true;
    public float damageKnockbackSpeed = 3.5f;
    public float damageKnockbackDuration = 0.15f;

    [Header("Escape (Run Continuation/Checkpoint Ver.1, items 2-4)")]
    // Renamed in spirit from the original "Ascension" win condition (which
    // this directly evolved from - see DoEscapeSuccess's own comment) to
    // match the brief's "1000m以降、3秒間長押しすると脱出" wording.
    // Gate is GameManager.EscapeAvailable - originally purely distance-
    // based (MaxDistance>=1000m), changed 2026-09-06 to require the first
    // Boss Reward to have actually completed (see EscapeAvailable's own
    // comment) - was BossesDefeated>0 even further back, before that.
    public float escapeHoldDuration = 3f;
    // Bugfix 2026-09-05, items 1/2 - root cause of "Jump/Attack stop
    // responding past 1000m (Boss出現後)" and "everything needs a long
    // press": escapeHoldTimer used to start accumulating the instant ANY
    // touch went down (see UpdateEscapeInput), and IsEscapeCharging used to
    // go true from escapeHoldTimer>0 - i.e. after a SINGLE frame of any
    // ordinary tap/swipe. Since wasEscapeChargingLastFrame is snapshotted
    // at the top of the NEXT frame, a normal tap's own release frame (which
    // is when touchJumpRequested actually gets set) almost always landed on
    // a frame where the PREVIOUS frame had already made escapeHoldTimer
    // positive - silently blocking Move's jump and skipping
    // HandleAttackInput() entirely for what was really just a normal tap or
    // swipe, not a genuine Escape attempt. escapeHoldTimer itself still
    // starts counting from the very first frame of any touch (needed so a
    // genuine hold's total real-world time to success is still exactly
    // escapeHoldDuration), but IsEscapeCharging - the ONLY thing that gates
    // Jump/Attack and drives the ring/gauge visuals - now requires the hold
    // to have already outlasted this grace window before it's treated as a
    // real Escape attempt. Comfortably longer than any real tap or swipe
    // takes to complete, comfortably shorter than escapeHoldDuration.
    public float escapeChargeConfirmDelay = 0.3f;
    public float ascendRiseSpeed = 11f;
    // Extra clearance above the (frozen) camera's top edge to rise past,
    // so the player visibly clears the screen instead of just touching the
    // edge - the actual rise distance is computed at runtime from the
    // camera's current framing, so it works regardless of orientation/aspect.
    public float ascendClearMargin = 3f;
    // Safety cap in case the camera can't be found, so the coroutine can't
    // spin forever.
    public float ascendMaxDuration = 6f;
    // RUN正常終了演出(2026-09-23) - 脱出成功後は上昇して消える代わりに、
    // 減速して停止→距離Tier別のFinish Animation→余韻、という地面ベースの
    // 演出へ切り替える(DoFinishSequence参照)。距離境界は3値で4段階を
    // 定義(後から調整できるようハードコードしない)。
    [Header("Run Finish (正常終了) - 距離Tier境界(m)")]
    public float[] finishTierBoundaries = new float[] { 1000f, 10000f, 50000f };
    public float finishDecelDuration = 0.22f;
    public float[] finishHoldDurationByTier = new float[] { 1.0f, 1.3f, 1.6f, 2.0f };
    // Item 4 - Dark Navy/Gold/Cyan magic circle at the player's feet,
    // brightening/scaling up as the charge progresses. Reuses the existing
    // Double Jump Ring effect sprite (already in the project, see
    // CardFusionUI/DeckEditUI's own "shared magic circle" comment for the
    // same reuse elsewhere) rather than new dedicated art.
    public Sprite escapeRingSprite;
    SpriteRenderer escapeRingRenderer;

    public static PlayerController Instance { get; private set; }

    public bool IsGrounded => isGrounded;
    // Bugfix 2026-09-08 (Bug #001 診断フェーズ) - for BossDiagnostics'
    // Freeze Snapshot ("PlayerVelocity"). Only the vertical component is a
    // real tracked field (horizontal speed is a local in Move(), never
    // stored) - BossDiagnostics derives an approximate horizontal speed
    // itself from frame-to-frame position deltas, which is plenty accurate
    // for a diagnostic dump.
    public float VerticalVelocity => velocityY;
    public bool IsAttacking => isAttacking;
    // 方向攻撃システム Ver.2、項目3 - PlayerAnimatorのState.DownAttack選択
    // と、着地/死亡/脱出時のHitbox後始末の両方から参照される。
    public bool IsDiveAttacking => isDiveAttacking;
    // 二丁拳銃士(2026-09-23) - Down Shot(空中で斜め下射撃+短時間だけ落下
    // 速度低下)専用の別フラグ。isDiveAttacking(急降下)とは物理挙動が違う
    // ため完全に独立させているが、PlayerAnimatorの見た目Stateだけは
    // downAttackFrames/State.DownAttackを共用する(両フラグをORで見る)。
    public bool IsRangedHoverShooting => isHoverShooting;
    // "ジャンプしながら斜め上へ射撃"のポーズ表示期間中だけtrue(実際の
    // ジャンプ物理・判定には関与しない、見た目State切り替え専用)。
    public bool IsRangedUpShooting => upShotVisualTimer > 0f;
    public bool IsHitInvincible => hitInvincibleTimer > 0f;
    public bool IsAscending => isAscending;
    // RUN開始準備/正常終了演出(2026-09-23) - IsPreparingStartはStart
    // Animation(カウントダウン中の準備ポーズ)、IsFinishing以下3つは
    // Finish Animation(距離Tier別の正常終了リアクション)用。どちらも
    // PlayerAnimatorが見た目Stateを選ぶためだけに参照する。
    public bool IsPreparingStart => GameManager.Instance != null && GameManager.Instance.CountdownActive;
    public bool IsFinishing { get; private set; }
    public int FinishTierIndex { get; private set; } = -1; // 0=Short,1=Medium,2=Long,3=Extreme
    public float FinishProgress { get; private set; }
    // Item 3/4 - true while actively holding the escape charge (not yet
    // committed to the fly-up itself). 0..1 progress for the circular
    // gauge/magic circle brightness.
    // Bugfix 2026-09-05, items 1/2 - gated on escapeChargeConfirmDelay, not
    // 0f, so a normal tap/swipe (which resolves well within that window)
    // never trips this - see escapeChargeConfirmDelay's own comment.
    public bool IsEscapeCharging => escapeHoldTimer > escapeChargeConfirmDelay && !isAscending;
    public float EscapeChargeProgress01 => Mathf.Clamp01(escapeHoldTimer / Mathf.Max(0.01f, escapeHoldDuration));
    // The combo stage (1-3) of the attack currently playing - stays at
    // whatever the last attack's stage was in between attacks, so animator
    // code reading it exactly while isAttacking is true always gets the
    // right value.
    public int CurrentAttackStage => comboCount;

    // Grown by the "ATTACK UP" card (see GameManager) - read by
    // DragonController/MajinController at hit time instead of a fixed
    // damage value.
    public int AttackPower { get; private set; } = 2;
    public void AddAttackPower(int amount) => AttackPower += amount;

    // Grown by "ATTACK RANGE UP" - multiplies both the hitbox scale-up and
    // the reach offset that already grow across the combo chain (see
    // ApplyComboStageToHitbox), so a bigger range card makes every combo
    // stage reach further, not just add a flat bonus to one stage.
    public float AttackRangeMultiplier { get; private set; } = 1f;
    public void AddAttackRangeBonus(float delta) => AttackRangeMultiplier = Mathf.Max(0.1f, AttackRangeMultiplier + delta);

    // Grown by "ATTACK SPEED UP" - shrinks both attackActiveTime and
    // attackCooldown by the same factor (see DoAttack), so the whole combo
    // tempo speeds up without changing the relative timing of the combo
    // window inside it. Stacks multiplicatively (diminishing returns) and
    // is floored so it can never reach zero/negative duration.
    public float AttackSpeedMultiplier { get; private set; } = 1f;
    public void AddAttackSpeedBonus(float fractionFaster) => AttackSpeedMultiplier = Mathf.Max(0.25f, AttackSpeedMultiplier * (1f - fractionFaster));

    // プレイアブル主人公追加(2026-09-12、お嬢様騎士) - Enemy側の
    // groundKnockbackSpeedBonus(EnemyController.ApplyGroundKnockback)へ
    // 掛ける倍率。AttackPower等と同じ「カードで積み上げるベース値」では
    // なく、キャラクターごとの固定ベース(ApplyCharacterBaseStats)のみが
    // 触る - 現時点でこの値を伸ばすカードは存在しない。
    // 竜騎士(2026-09-26) - 攻撃ごとの一時倍率(lanceKnockbackScale、後ろ攻撃は弱く・
    // 前突きは速度連動で強く)を竜騎士の間だけ掛ける。他キャラは常に基準値そのまま。
    float knockbackPowerBase = 1f;
    public float KnockbackPowerMultiplier
    {
        get => isLancerCharacter ? knockbackPowerBase * lanceKnockbackScale : knockbackPowerBase;
        private set => knockbackPowerBase = value;
    }

    // 同上、項目4「アクロバット技(上/空中/下攻撃)を持たせない/接続しない」
    // - 通常のジャンプ物理(velocityY/jumpsUsed/JumpStarted等)やDoAttackの
    // 3段コンボには一切関与しない、上/空中/下の3攻撃それぞれの発動可否の
    // みを個別に止めるフラグ(FireJump/DoDiveAttack側の各トリガーで参照)。
    public bool canUseUpAttack = true;
    public bool canUseAirAttack = true;
    public bool canUseDownAttack = true;

    // ApplyCharacterBaseStatsがrunSpeed/jumpForce/gravityを上書きする際の
    // 「乗算元」- SceneBuilderはこれらのフィールドを一切上書きしないため
    // (黒剣士の性能そのもの)、Awake()時点の値=黒剣士の素の性能として保持
    // しておく。CharacterDefinitionの各Multiplierは常にこの値からの相対
    // 倍率として適用するため、複数回ApplyCharacterBaseStatsを呼んでも
    // (例:一度NEW RUNしてから別キャラでもう一度)値が際限なく縮小/増大
    // することはない。
    float baseRunSpeed;
    float baseJumpForce;
    float baseGravity;

    // Priority 3 - GameManager.ApplyCharacterBaseStatsから、Run開始時
    // (StartGame/BeginContinuedRunの両方、ApplyCharacterCardEffectsより
    // 前)に一度だけ呼ばれる。カード効果は全てこの後に「現在値を追加で
    // 変更する」形で積み重なる(pc.runSpeed *= 1f+effect.value 等)ため、
    // 呼び出し順を間違えるとキャラクターのハンデがカード効果を丸ごと
    // 消し飛ばしてしまう - 必ずカード再生よりも前に呼ぶこと。
    // 黒剣士のSpec値は全倍率=1.0/現行のAttackPower=2等そのままなので、
    // 黒剣士自身の性能はこの仕組みを通しても一切変化しない。
    public void ApplyCharacterBaseStats(CharacterDefinition def)
    {
        if (def == null) return;
        AttackPower = def.attackPower;
        AttackRangeMultiplier = def.attackRangeMultiplier;
        AttackSpeedMultiplier = def.attackSpeedMultiplier;
        KnockbackPowerMultiplier = def.knockbackPowerMultiplier;
        maxComboChain = Mathf.Max(1, def.attackComboCount);
        maxJumps = Mathf.Max(1, def.jumpCount);
        runSpeed = baseRunSpeed * def.groundMobilityMultiplier;
        jumpForce = baseJumpForce * def.jumpForceMultiplier;
        gravity = baseGravity * def.airControlMultiplier;
        canUseUpAttack = def.canUseUpAttack;
        canUseAirAttack = def.canUseAirAttack;
        canUseDownAttack = def.canUseDownAttack;
        charHurtDuration = def.hurtDuration;
        charHurtInvincible = def.hurtInvincibleDuration;
        charRecoveryDuration = def.recoveryDuration;
        charRecoveryInvincible = def.recoveryInvincibleDuration;
        charHurtKnockbackMultiplier = def.hurtKnockbackMultiplier > 0f ? def.hurtKnockbackMultiplier : 1f;

        // 二丁拳銃士(2026-09-23) - isRanged==trueの間だけForward/Backward/
        // Up/Downの4攻撃すべてが専用の弾丸ロジックへ分岐する(DoAttack/
        // FireJump/Move()の各分岐参照)。他3キャラはisRanged=falseのまま
        // なので既存の挙動に一切影響しない。
        isRangedCharacter = def.isRanged;
        rangedBulletSprite = def.bulletSprite;
        rangedBulletSpeed = def.bulletSpeed > 0f ? def.bulletSpeed : rangedBulletSpeed;
        rangedBulletLifetime = def.bulletLifetime > 0f ? def.bulletLifetime : rangedBulletLifetime;
        rangedHoverDuration = def.hoverDuration > 0f ? def.hoverDuration : rangedHoverDuration;
        rangedHoverFallSpeed = def.hoverFallSpeed > 0f ? def.hoverFallSpeed : rangedHoverFallSpeed;

        // 竜騎士(2026-09-26) - isLancer==trueの間だけ4方向攻撃がPlayerController.Lancer.csへ分岐。
        ApplyLancerStats(def);
        // 新4人(2026-09-27) - kit!=Standardの間だけ入力/上攻撃/空中の下/魔法使いの浮遊がPlayerController.Kit*.csへ分岐。
        ApplyKitStats(def);
        charHasDeathFrames = def.deathFrames != null && def.deathFrames.Length > 0;
    }

    // 死亡時の専用ポーズ(CharacterDefinition.deathFrames)を持つキャラは、消えて爆散する
    // 代わりにその場でポーズを見せる(PlayerAnimatorがState.Deathで再生)。
    bool charHasDeathFrames;
    public bool IsDeadPosing => hasDied && charHasDeathFrames;
    public bool HasDied => hasDied; // BGM: 倒れている間は曲を戻さない(CO-OPの復活で戻す)

    // Grown by "AIR ATTACK UP" - only added on top of AttackPower while
    // airborne (see EffectiveAttackPower); grounded attacks are unaffected.
    public int AirAttackPowerBonus { get; private set; }
    public void AddAirAttackPowerBonus(int amount) => AirAttackPowerBonus += amount;

    // ===== Card Expansion/Gacha Evolution Ver.1 additions ===== //
    // Mirrors AirAttackPowerBonus exactly, just the GROUNDED-only half
    // ("Ground Zero"/"Heavy Impact"/"Ground Fighter").
    public int GroundAttackPowerBonus { get; private set; }
    public void AddGroundAttackPowerBonus(int amount) => GroundAttackPowerBonus += amount;

    // "Combo Master"/"Combo Edge" - only on the LAST stage of the combo
    // chain (comboCount reaches maxComboChain).
    public int ComboFinalStageBonus { get; private set; }
    public void AddComboFinalStageBonus(int amount) => ComboFinalStageBonus += amount;

    // "First Strike"/"Sonic Blade" - only on the FIRST hit of a fresh combo.
    public int FirstHitBonus { get; private set; }
    public void AddFirstHitBonus(int amount) => FirstHitBonus += amount;

    // "Last Stand"/"Berserk Drive"/"Blood Rush"/"Adrenaline" - scales from
    // 0 (full HP) up to its full value (0 HP), via GameManager.Lives/
    // maxLives - a "fights harder while hurt" berserk-style bonus.
    public int LowHpAttackBonus { get; private set; }
    public void AddLowHpAttackBonus(int amount) => LowHpAttackBonus += amount;

    // "Iron Will"/"Blood Blade" - only while at full HP.
    public int FullHpAttackBonus { get; private set; }
    public void AddFullHpAttackBonus(int amount) => FullHpAttackBonus += amount;

    // "Momentum"/"Overdrive" - scales with how much of the Speed Up ramp
    // (GetSpeedMultiplier) has accumulated so far.
    public int MomentumBonus { get; private set; }
    public void AddMomentumBonus(int amount) => MomentumBonus += amount;

    // "Boss Killer" - only added against a Boss (see EffectiveBossAttackPower,
    // which DragonController/MajinController read instead of
    // EffectiveAttackPower directly).
    public int BossDamageBonus { get; private set; }
    public void AddBossDamageBonus(int amount) => BossDamageBonus += amount;

    // What DragonController/MajinController should actually read instead of
    // AttackPower directly, so AIR ATTACK UP/GROUND ATTACK/combo-stage/HP-
    // conditional/Momentum bonuses all take effect without any of them
    // needing to know about the player's current state themselves.
    public int EffectiveAttackPower
    {
        get
        {
            int power = AttackPower + (!isGrounded ? AirAttackPowerBonus : GroundAttackPowerBonus);
            if (comboCount == 1) power += FirstHitBonus;
            if (comboCount >= maxComboChain) power += ComboFinalStageBonus;

            GameManager gm = GameManager.Instance;
            if (gm != null && gm.maxLives > 0)
            {
                if (gm.Lives >= gm.maxLives)
                {
                    power += FullHpAttackBonus;
                }
                else
                {
                    float missingFraction = 1f - (float)gm.Lives / gm.maxLives;
                    power += Mathf.RoundToInt(LowHpAttackBonus * missingFraction);
                }
            }

            power += Mathf.RoundToInt(MomentumBonus * Mathf.Max(0f, GetSpeedMultiplier() - 1f));
            // 竜騎士(2026-09-26) - 後ろ攻撃(石突き)だけ威力を下げる。他キャラは常に1倍。
            if (isLancerCharacter && lanceDamageScale != 1f) power = Mathf.Max(1, Mathf.RoundToInt(power * lanceDamageScale));
            return power;
        }
    }

    // "Boss Killer" - Dragon/Majin/Mechanical Dragon damage calculations
    // use this instead of EffectiveAttackPower.
    public int EffectiveBossAttackPower => EffectiveAttackPower + BossDamageBonus;

    // Grown by "SHIELD" - each charge absorbs exactly one hit (see
    // GameManager.TryDamagePlayer) before any life is lost.
    public int ShieldCharges { get; private set; }
    public void AddShieldCharges(int amount) => ShieldCharges += amount;
    public bool TryConsumeShield()
    {
        if (ShieldCharges <= 0) return false;
        ShieldCharges--;
        return true;
    }

    // Fired at the moment a jump input actually launches the player, so
    // PlayerAnimator can play a short one-shot animation instead of just
    // reacting to "is grounded" every frame. JumpStarted = first jump (feet
    // leave the ground), DoubleJumped = second jump (already airborne).
    public event System.Action JumpStarted;
    public event System.Action DoubleJumped;
    public event System.Action Landed;
    // 上下攻撃アニメーション差し替え(2026-09-08) - 下降攻撃(DoDiveAttack)
    // の着地専用Frame(Frame3、衝撃エフェクト込みの1枚絵)をLanded(通常の
    // 着地でも常に発火する)とは区別して表示するための専用イベント。
    // 着地した"実際のその瞬間"にのみ発火するよう、Move()の着地判定内
    // (isDiveAttackingがまだtrueの間)からのみ呼ぶ - OnDeath/DoEscapeSuccess
    // のようなクリーンアップ経由のEndDiveAttack()呼び出しでは発火させない
    // (あれらは本当の着地ではないため)。
    public event System.Action DiveAttackLanded;
    // The player's base auto-scroll speed this frame, NOT including attack
    // lunge/recoil. Used by the boss to keep pace with ordinary running
    // without also cancelling out the player's attack-driven movement.
    public float CurrentAutoRunSpeed => autoRunEnabled ? runSpeed * EffectiveSpeedMultiplier() : 0f;

    // 弾速の走行補正(2026-09-26) - 弾/飛び道具はすべて「プレイヤーの基本走行速度で一緒に流れる
    // 座標系」の中を、それぞれの設計速度で飛ぶ(=画面上の見た目の速さが走行速度に左右されない)。
    // これが無いと、高速走行中にプレイヤーが自分の弾や跳ね返した火球を追い越してしまい、
    // 逆に正面から来る敵弾は走行速度ぶん速く迫って避けられなくなる。ボス自身もこの速度で
    // 並走しているため、ボスの弾・跳ね返した弾の当たり方も設計どおりに保たれる。
    public static float RunFrameSpeed => Instance != null ? Instance.CurrentAutoRunSpeed : 0f;
    // 高速走行の視認性補正(2026-09-22) - 基礎速度に対する現在のAuto Run速度の倍率(1.0〜maxSpeedMultiplier)。
    // 表示/カメラ補正/配置間隔が参照するだけで、実際の移動速度計算には一切影響しない。
    public float SpeedRatio => autoRunEnabled ? EffectiveSpeedMultiplier() : 1f;
    public float MaxSpeedRatio => Mathf.Max(1.01f, maxSpeedMultiplier);
    // 走行開始位置からの論理距離(Floating Originで座標を戻しても連続)。
    public float DistanceFromStart => (float)(transform.position.x - startX);
    // cm単位の表示/保存用(floatだと100,000m超でcm精度が保てないため、startXをdoubleで持つ)。
    public double DistanceExact => transform.position.x - startX;

    void OnEnable()
    {
        FloatingOrigin.Shifted += OnOriginShifted;
        FloatingOrigin.Warped += OnOriginWarped;
    }

    void OnDisable()
    {
        FloatingOrigin.Shifted -= OnOriginShifted;
        FloatingOrigin.Warped -= OnOriginWarped;
    }

    // Floating Origin: 座標をs戻したので、走行距離の基準(startX)も同じだけ戻して論理距離を保つ。
    void OnOriginShifted(float s) { startX -= s; }
    void OnOriginWarped(float d) { startX -= d; }

    Rigidbody2D rb;
    SpriteRenderer sr;
    float velocityY;
    // エリアルコンボ改修(2026-09-11) - Aerial Assist(NotifyAerialHit参照)
    // の残り時間(>0の間、重力にaerialAssistGravityScaleがかかる)と、
    // 空中にいる間の累積使用量(着地でリセット、aerialAssistMaxTotal
    // Durationの上限管理用)。
    float aerialAssistTimer;
    float aerialAssistTotalUsed;
    // Game Feel pass - a brief backward velocity on taking damage (see
    // ApplyKnockback), decayed linearly to 0 over knockbackTimer rather
    // than cut off sharply, folded into Move()'s own newX alongside
    // lungeVelocityX.
    float knockbackVelocityX;
    float knockbackTimer;
    float knockbackDuration = 1f;
    bool isGrounded;
    // Which surface isGrounded currently refers to - the main ground path,
    // or an elevated sky-path platform (see TerrainManager.GetSkyHeightAt).
    bool onSky;
    int jumpsUsed;
    bool isAttacking;
    // 方向攻撃システム Ver.2、項目3 - trueの間、Move()の落下速度がgravity
    // 積分の代わりにdiveAttackSpeedへ上書きされ、downAttackHitboxが有効に
    // なる。着地(landedSky/landedGround)・Fall死亡・GAME OVER・ESCAPE成功
    // のいずれかで必ずfalseへ戻され、Hitboxも無効化される。
    bool isDiveAttacking;
    // 二丁拳銃士(2026-09-23) - isDiveAttackingとは完全に独立したDown Shot
    // 専用の状態(Move()のvelocityY上書き分岐/PlayerAnimatorの見た目State
    // 判定の両方で参照)。hoverShotsUsedThisAirtimeは「1回の滞空中に
    // maxHoverShotsPerAirtime回まで」制限用(2026-09-24、マスター指示で
    // 二丁拳銃士のみ1回→3回へ拡張)で、着地の瞬間(jumpsUsed=0に戻る箇所)
    // にのみ0へ戻す。次弾は前弾のホバー終了(EndHoverShotAfterDelay経由の
    // isHoverShooting=false)を待ってから、という既存の間隔を維持したまま
    // 回数だけ増やす方式(ホバー中の割り込み再発射は不可、hoverGenerationの
    // 排他制御を変えずに済む)。
    bool isRangedCharacter;
    bool isHoverShooting;
    int hoverShotsUsedThisAirtime;
    const int maxHoverShotsPerAirtime = 3;
    int hoverGeneration;
    float rangedBulletSpeed = 15f;
    float rangedBulletLifetime = 1.6f;
    float rangedHoverDuration = 0.22f;
    float rangedHoverFallSpeed = 0.6f;
    Sprite rangedBulletSprite;
    // Root基準のローカルオフセット(銃口位置) - transform.localScale.xの
    // 符号で自動的に左右ミラーされる(ApplyAttackDirectionと同じ考え方)。
    public Vector2 rangedMuzzleOffset = new Vector2(0.55f, 0.55f);
    // 前方/後方射撃専用の銃口位置(2026-09-26) - 真横へ腕を伸ばす走り撃ち
    // 素材(GunslingerAttack*_v1)の実測銃口(3ポーズ平均)。上/下撃ちは
    // 従来のrangedMuzzleOffsetのまま。
    public Vector2 rangedForwardMuzzleOffset = new Vector2(0.62f, 0.71f);
    // "ジャンプしながら斜め上へ射撃"のポーズ表示用ワンショットタイマー
    // (jumpStartTimer等と同じ方式、Move()側で毎フレーム減算)。
    float upShotVisualTimer;
    public float rangedUpShotPoseDuration = 0.22f;
    // 不具合修正(2026-09-08) - 「下攻撃→着地→上攻撃」の入力バッファ。
    // 下降攻撃中(jumpsUsedが既にmaxJumpsで即座にはジャンプできない状態)
    // に上フリックした場合、この時間だけ「地上上攻撃をしたがっている」
    // ことを覚えておき、着地でjumpsUsedが0に戻った瞬間に自動でFireJump()
    // する(Move()のjumpPressed分岐、および着地処理側のバッファ消化を参照)。
    public float upAttackBufferWindow = 0.15f;
    float bufferedUpAttackTimer;
    float attackCooldownTimer;
    double startX;
    bool hasDied;
    float lungeVelocityX;
    float hitInvincibleTimer;
    // Cave spike contact cooldown: guarantees one spike touch is one damage event even when TakeDamage is ignored (shield etc.).
    float caveSpikeCooldown;
    float escapeHoldTimer;
    bool isAscending;
    // Item 2 - "脱出チャージ中はPlayerは通常操作を行えない" - snapshotted at
    // the TOP of Update() (before this frame's escape-charge state is
    // itself updated - see Update()'s own comment for why the ordering
    // matters), so the exact frame a charge is cancelled-by-release still
    // correctly suppresses that frame's jump, rather than un-suppressing
    // it one frame early just because escapeHoldTimer already reset to 0.
    bool wasEscapeChargingLastFrame;

    bool comboWindowOpen;
    bool comboBuffered;
    AttackDirection bufferedDirection;
    int comboCount;

    Vector3 hitboxBaseScale = Vector3.one;
    Vector3 hitboxBaseLocalPos;
    // 不具合修正(2026-09-09) - 「通常攻撃の斬撃エフェクトを追加、攻撃範囲
    // がちゃんと見えるように」。以前はattackSlashVisualのtransform位置が
    // AttackHitboxと違って完全固定(コンボ段/Attack Range Upで一切動かな
    // い)だったため、Hitbox自体はApplyComboStageToHitboxでどんどん前方へ
    // 伸びていくのに、見た目のVFXはその場でScaleが大きくなるだけで「攻撃
    // 範囲が伸びている」という実感に繋がっていなかった。当初は別途
    // slashBaseLocalPosを持たせていたが、AttackVfxCapture(Editor専用デバ
    // ッグツール)での実機相当の直接検証で「Hitboxの基準位置(1.0,0.5)と
    // Slashの基準位置(0.3,0.5)がそもそもズレていた」ことが判明したため、
    // SceneBuilder側でSlashの初期位置をHitboxと完全に同じ値へ揃えた上で、
    // ここでも同じhitboxBaseLocalPosを共有基準として使う形に統一 - 別々の
    // 定数を持たないことで、今後Hitbox側だけ調整してVFXとズレる、という
    // 事態が構造的に起こらないようにする。
    // Bugfix 2026-09-08 - 「攻撃範囲拡張カードが上/下攻撃に効かない」修正
    // 用。Forward/Backward側はApplyComboStageToHitboxが毎回attackHitboxの
    // localScale/localPositionをAttackRangeMultiplier込みで再計算していた
    // が、DoUpAttack/DoDiveAttackはSlash FXの見た目(SetComboStage)にしか
    // AttackRangeMultiplierを渡しておらず、実際のupAttackHitbox/
    // downAttackHitboxのCollider2Dサイズは常に固定のままだった(見た目だけ
    // 伸びて実際の判定は伸びない)。Forwardと同じ「Awakeで基準値を保存→
    // 発動時にAttackRangeMultiplierを掛けて上書き」の形に揃える。
    Vector3 upHitboxBaseScale = Vector3.one;
    Vector3 upHitboxBaseLocalPos;
    Vector3 downHitboxBaseScale = Vector3.one;
    Vector3 downHitboxBaseLocalPos;
    // 不具合修正(2026-09-09) - 新しい縦長トレイルVFX(DiveTrailBlue.png)を
    // downHitboxBaseLocalPosからこの分だけ上にずらして表示する(トレイル
    // が衝撃点=Hitbox付近から上へ伸びているように見せるため)。
    public float downSlashUpwardOffset = 1.1f;

    // Operation System Ver.2 - タップ=ジャンプが廃止されたことで、旧
    // jumpSuppressionAfterAttack(「連続攻撃中の誤ジャンプ」抑制タイマー)
    // はそもそも起こり得ないバグへの対処だったため丸ごと削除した - タップ
    // という入力解釈自体が存在しない以上、フリック後の短いタップがジャン
    // プに化けることも構造的になくなった。
    Vector2 touchStartPos;
    Vector2 lastPointerPos;
    bool touchActive;
    // 不具合修正(2026-09-09) - 「初期の二段ジャンプができなくなっている」
    // の原因。前回パス(2026-09-08)で「指を離さず連続スワイプしても方向
    // 転換を検出できるように」touchStartPosをflick発火のたびにリセット
    // する方式へ変更したが、これにより"1本の長い連続上スワイプ"が閾値
    // (flickDistanceThreshold=60px)を2回以上跨いでしまうケースで、
    // ユーザーが1回だけ振ったつもりでも上フリックが2回検出され、1回の
    // ジェスチャーでjumpsUsedが0→1→2まで一気に進んでしまっていた(=二段
    // ジャンプが「初手で両方消費される」ため、2回目の入力をしても何も
    // 起きないように見える)。flick発火後は短いクールダウンを設け、"同じ
    // 連続ドラッグの続き"では次のflickを検出しないようにしつつ、指を
    // 離さない方向転換(下攻撃→上攻撃)は引き続き検出できるようにした
    // (クールダウンは方向転換に要する現実的な時間より十分短い)。
    public float flickCooldown = 0.15f;
    float flickCooldownTimer;
    // その場フレームだけ有効な"リクエスト" - UpdatePointerInput()の先頭で
    // 毎フレームnullへ戻し、そのフレーム内でMove()(Up方向のみ消費)と
    // HandleAttackInput()(Forward/Backwardのみ消費)の両方から参照される。
    FlickDirection? requestedFlick;
    bool wasStarted;

    void Awake()
    {
        Instance = this;
        baseRunSpeed = runSpeed;
        baseJumpForce = jumpForce;
        baseGravity = gravity;
        rb = GetComponent<Rigidbody2D>();
        // The SpriteRenderer lives on the "Visual" child, not this Root -
        // see SceneBuilder.CreatePlayer. Only used here for the hit-
        // invincibility flicker and hiding the sprite on death.
        sr = GetComponentInChildren<SpriteRenderer>();
        startX = transform.position.x;
        isGrounded = true;

        if (attackHitbox != null)
        {
            attackHitbox.enabled = false;
            hitboxBaseScale = attackHitbox.transform.localScale;
            hitboxBaseLocalPos = attackHitbox.transform.localPosition;
        }
        if (upAttackHitbox != null)
        {
            upAttackHitbox.enabled = false;
            upHitboxBaseScale = upAttackHitbox.transform.localScale;
            upHitboxBaseLocalPos = upAttackHitbox.transform.localPosition;
        }
        if (downAttackHitbox != null)
        {
            downAttackHitbox.enabled = false;
            downHitboxBaseScale = downAttackHitbox.transform.localScale;
            downHitboxBaseLocalPos = downAttackHitbox.transform.localPosition;
        }
        if (downAttackLandHitbox != null) downAttackLandHitbox.enabled = false;
        if (upAttackVacuumHitbox != null) upAttackVacuumHitbox.enabled = false;
    }

    void Update()
    {
#if UNITY_EDITOR
        // RUN開始/終了演出 動画撮影用の一時デバッグキー(2026-09-23) -
        // マスターの動画確認が終わったら残すか削除するか相談する。
        if (Input.GetKeyDown(KeyCode.Comma) && GameManager.Instance != null) GameManager.Instance.DebugWarpToDistance(500f);
        if (Input.GetKeyDown(KeyCode.Period) && GameManager.Instance != null) GameManager.Instance.DebugWarpToDistance(60000f);
        if (Input.GetKeyDown(KeyCode.Slash) && !isAscending) StartCoroutine(DoFinishSequence());
#endif
        bool hasStarted = GameManager.Instance == null || GameManager.Instance.HasStarted;
        // Stage01地形挙動修整(2026-09-17), item4 - Run開始カウントダウン中
        // (GameManager.CountdownActive)は、HasStartedが既にtrueでも
        // HasStarted=false相当として扱い、移動/入力/距離加算(この早期
        // returnより先には進めない)を止める。
        bool countdownActive = GameManager.Instance != null && GameManager.Instance.CountdownActive;
        if (!hasStarted || countdownActive)
        {
            wasStarted = false;
            return;
        }
        if (!wasStarted)
        {
            // Just started this frame: discard any pointer state left over
            // from the tap that started the game, so it doesn't also count
            // as a flick input.
            touchActive = false;
            wasStarted = true;
        }

        if (GameManager.Instance != null && GameManager.Instance.IsGameOver)
        {
            if (!hasDied)
            {
                hasDied = true;
                if (!GameManager.Instance.IsWin) OnDeath();
            }
            return;
        }

        // Paused for a level-up choice (see GameManager) - skip reading
        // input entirely rather than just letting Move() no-op on a zero
        // deltaTime, so a tap on a choice card doesn't also get recorded as
        // a leftover jump/attack gesture that fires the instant play resumes.
        if (Time.timeScale <= 0f) return;

        // マルチプレイPhase 3: DOWN(CO-OP)/脱落(VERSUS)中は走行も入力も止める(その場に留まる)。
        if (netDowned) { NetDownedTick(); return; }
        // マルチ(2026-09-28): カード選択中は選んでいる本人だけその場で一時停止する(世界は止めない)。
        if (NetIsChoosing) { NetChoosingTick(); return; }
        if (netChoosingHeld) NetChoosingRelease();

        if (hitInvincibleTimer > 0f)
        {
            hitInvincibleTimer = Mathf.Max(0f, hitInvincibleTimer - Time.deltaTime);
        }
        // 表示の安全装置(2026-09-26、竜騎士Sprite消失対策) - 絵を意図的に消すのは被弾後の無敵点滅
        // (hitInvincibleTimer中)と死亡時だけ。それ以外でSpriteRendererが無効のまま残っていたら
        // (点滅の途中で処理が打ち切られた等)必ず表示へ戻す。
        if (sr != null && !sr.enabled && hitInvincibleTimer <= 0f && !hasDied) sr.enabled = true;

        if (knockbackTimer > 0f) knockbackTimer = Mathf.Max(0f, knockbackTimer - Time.deltaTime);

        if (Reaction != ReactionKind.None)
        {
            reactionTimer -= Time.deltaTime;
            if (reactionTimer <= 0f) { reactionTimer = 0f; Reaction = ReactionKind.None; }
        }

        if (isAscending) return; // the ascend coroutine drives position directly

        // Item 2 - snapshot BEFORE this frame's UpdateEscapeInput() runs
        // (which may transition escapeHoldTimer back to 0 this very frame,
        // e.g. on release) - see wasEscapeChargingLastFrame's own comment
        // for why the ordering matters.
        wasEscapeChargingLastFrame = IsEscapeCharging;

        UpdatePointerInput();
        // Hurt/Recovery中は新規の攻撃/ジャンプ入力を受け付けない(入力は捨てる=終了後に暴発しない)。
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (debugInjectFlick.HasValue) { requestedFlick = debugInjectFlick; }
#endif
        // 高速時の自動操作補助(2026-09-28): 手動入力が無いフレームだけ、補助の判断を同じ入力経路へ入れる。
        // (ここへ来るのは停止/カード選択/カウントダウン/死亡/ダウン/終了のどれでもない時だけ)
        ApplyHighSpeedAssist();
        bool reactionBlocked = IsReacting;
        // 診断ログの詳細画面を開いている間は、スクロール操作がジャンプ/攻撃にならないよう入力を受け付けない。
        if (DiagnosticsOverlay.DetailOpen) reactionBlocked = true;
        if (reactionBlocked) { requestedFlick = null; bufferedUpAttackTimer = 0f; }
        Move(allowJump: !wasEscapeChargingLastFrame && !reactionBlocked);
        if (!wasEscapeChargingLastFrame && !reactionBlocked) HandleAttackInput();
        LancerSafetyUpdate(); // 竜騎士: 実行中の技が無いのに攻撃状態だけ残らないようにする(他キャラは何もしない)
        KitUpdate(); // 新4人: タイマー/引き絞り表示/安全装置(既存5人は何もしない)

        UpdateEscapeInput();
        UpdateEscapeVisuals();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.ReportDistance((float)(transform.position.x - startX), transform.position.x - startX);
        }
    }

    // Operation System Ver.2 - reads either the first touch (on device) or
    // the mouse (in the Editor, for easy testing) and classifies it into one
    // of exactly 3 flick directions (Up/Forward/Backward - see
    // ClassifyFlickDirection). A flick fires the moment either the total
    // drag distance crosses flickDistanceThreshold OR (for a fast short
    // flick, so the "上攻撃=実質ジャンプ" input-to-action delay stays low)
    // this frame's instantaneous speed crosses flickVelocityThreshold while
    // already past a small minimum distance - in both cases WHILE the
    // finger is still down, never waiting for release (item 6). Releasing
    // without ever crossing either threshold now does nothing at all (item
    // 5 - tap-jump is gone).
    void UpdatePointerInput()
    {
        requestedFlick = null;

        Vector2 pointerPos;
        bool pointerJustDown, pointerJustUp, pointerDown;

        if (Input.touchCount > 0)
        {
            Touch t = Input.GetTouch(0);
            pointerPos = t.position;
            pointerJustDown = t.phase == TouchPhase.Began;
            pointerJustUp = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
            pointerDown = !pointerJustUp;
        }
        else
        {
            pointerPos = Input.mousePosition;
            pointerJustDown = Input.GetMouseButtonDown(0);
            pointerJustUp = Input.GetMouseButtonUp(0);
            pointerDown = Input.GetMouseButton(0);
        }

        if (pointerJustDown)
        {
            touchStartPos = pointerPos;
            lastPointerPos = pointerPos;
            touchActive = true;
            flickCooldownTimer = 0f; // 新しいタッチ開始 - 前のタッチのクールダウンを引きずらない
        }
        // Bugfix 2026-09-08 - 「下攻撃を使用したあと、上攻撃ができなくなる」
        // の実装調査で発見した実際の原因の1つ: 指を離さず連続でスワイプ
        // する操作(下攻撃→着地→そのまま同じ指で上へ振り返す、という自然
        // な操作)では、下攻撃のフリックが発火した時点でflickFiredThisTouch
        // がtrueになり、"同じタッチが続く限り"二度とflickを検出しなくなっ
        // ていた(pointerJustUpで指を一度完全に離すまでロックされる設計
        // だった)。元々のflickFiredThisTouchガード自体の目的は「1回の連続
        // ドラッグ動作が閾値を満たし続ける間、毎フレーム再発火してしまう
        // のを防ぐ」ことであり、"タッチ中は1回しかflickできない"という制
        // 限は意図した仕様ではなかった(コード中に明示的な設計意図のコメ
        // ントはない)。修正: flickFiredThisTouchで永続ロックする代わりに、
        // 発火のたびにtouchStartPos/lastPointerPosをその場の位置へリセッ
        // トする - 同じ指を離さずに振り続けても、次のflickは"そこから新た
        // に閾値を超える動き"を要求されるため、同一ドラッグの連射防止(
        // 元々の目的)は保たれたまま、指を離さない連続スワイプでの方向転
        // 換(下攻撃→上攻撃 等)が可能になる。
        else if (pointerDown && touchActive && flickCooldownTimer <= 0f)
        {
            Vector2 totalDelta = pointerPos - touchStartPos;
            float totalDist = totalDelta.magnitude;
            float frameDist = (pointerPos - lastPointerPos).magnitude;
            float frameSpeed = Time.unscaledDeltaTime > 0f ? frameDist / Time.unscaledDeltaTime : 0f;

            bool distanceTrigger = totalDist >= flickDistanceThreshold;
            bool velocityTrigger = totalDist >= flickMinDistanceForVelocityTrigger && frameSpeed >= flickVelocityThreshold;

            if (distanceTrigger || velocityTrigger)
            {
                requestedFlick = ClassifyFlickDirection(totalDelta);
                touchStartPos = pointerPos;
                flickCooldownTimer = flickCooldown;
            }
        }
        if (flickCooldownTimer > 0f) flickCooldownTimer -= Time.unscaledDeltaTime;

        if (pointerDown) lastPointerPos = pointerPos;
        if (pointerJustUp) touchActive = false;

        // Editor/keyboard test convenience (mirrors the old Space=jump/
        // Z,B=attack shortcuts) - bypasses the drag-distance system
        // entirely, since a key press has no drag distance to measure.
        if (Input.GetKeyDown(KeyCode.Space)) requestedFlick = FlickDirection.Up;
        else if (Input.GetKeyDown(KeyCode.Z)) requestedFlick = FlickDirection.Forward;
        else if (Input.GetKeyDown(KeyCode.B)) requestedFlick = FlickDirection.Backward;
    }

    // 方向攻撃システム Ver.2、項目7 - 4方向化後も「斜めフリックなどは、最
    // も近い基本方向へ吸着させる...厳密な方向入力を要求するゲームにはし
    // ないでください」は不変。4方向それぞれの単位ベクトルとの内積(=cos
    // (なす角)相当)が最大のものを選ぶだけで、角度計算なしに「最も近い方
    // 向」への吸着が実現できる(downDot = -upDotなので実質2回のDot計算で
    // 4方向すべて判定可能)。Forward/Backwardはワールド+X(自動前進方向)
    // を基準にしており、画面の向き・回転に依存しない。
    static FlickDirection ClassifyFlickDirection(Vector2 delta)
    {
        Vector2 dir = delta.normalized;
        float upDot = Vector2.Dot(dir, Vector2.up);
        float downDot = -upDot;
        float fwdDot = dir.x; // Vector2.Dot(dir, Vector2.right)
        float backDot = -fwdDot;

        float best = Mathf.Max(Mathf.Max(upDot, downDot), Mathf.Max(fwdDot, backDot));
        if (best == upDot) return FlickDirection.Up;
        if (best == downDot) return FlickDirection.Down;
        return best == fwdDot ? FlickDirection.Forward : FlickDirection.Backward;
    }

    // デバッグ用の走行速度倍率(2026-09-26) - DEBUG ON時のSPD -/+ボタンで変更する(GameManager.
    // DrawDistanceWarpDebugUI)。DEBUGをOFFにすると1へ戻る。移動速度だけに掛け、攻撃力の
    // 速度ボーナス(MomentumBonus)は距離由来のGetSpeedMultiplierのまま変えない。
    public static float DebugSpeedScale = 1f;
    float EffectiveSpeedMultiplier() => GetSpeedMultiplier() * DebugSpeedScale;

    float GetSpeedMultiplier()
    {
        float distance = (float)(transform.position.x - startX);
        if (distance <= speedUpStartDistance) return 1f;

        float multiplier = 1f + (distance - speedUpStartDistance) / 100f * speedUpPer100m;
        return Mathf.Min(multiplier, maxSpeedMultiplier);
    }

    // Item 2 - allowJump=false while escape-charging (or just released one
    // this same frame - see the snapshot in Update()) skips ONLY the jump
    // trigger below; auto-run/gravity/landing all keep running normally
    // (the brief's "無防備になるリスク" wouldn't make sense if the player
    // could also just stand still safely during the charge).
    void Move(bool allowJump = true)
    {
        float dt = Time.deltaTime;
        if (bufferedUpAttackTimer > 0f) bufferedUpAttackTimer -= dt;
        if (upShotVisualTimer > 0f) upShotVisualTimer -= dt;
        float autoSpeed = autoRunEnabled ? runSpeed * EffectiveSpeedMultiplier() : 0f;
        // 荒野街道ボス追加(2026-09-20) - 巨大蜘蛛の糸による短時間の移動妨害。
        // CurrentAutoRunSpeed(ボス側の追従基準)には含めない - ボスは通常速度で
        // 走り続けるので、糸を受けたプレイヤーは相対的に後ろへ取り残される。
        if (moveSlowTimer > 0f)
        {
            moveSlowTimer -= dt;
            autoSpeed *= moveSlowFactor;
        }
        // 実機フィードバック(2026-09-12第3弾) - Ground Hit Connect Assist
        // (既定は無効、groundHitConnectSlowdownFactor>0の場合のみ)。
        // 主人公を完全に停止させることはない(autoSpeedを弱めるだけで
        // lungeVelocityX/effectiveKnockback等は一切触らない)。
        if (groundHitConnectSlowdownTimer > 0f)
        {
            groundHitConnectSlowdownTimer -= dt;
            autoSpeed *= 1f - groundHitConnectSlowdownFactor;
        }
        // Linear ease-out over knockbackDuration, not a flat velocity for
        // the whole window - reads as a shove that fades, not a sustained
        // shove-then-stop.
        if (IsReacting) autoSpeed = 0f; // Hurt/Recovery中は自動前進を一時停止(重力/着地/ノックバックは通常どおり)
        // 竜騎士の急降下突き/突き刺し着地の間は前進をほぼ止める(ほぼ真下へ落ちる)。他キャラは常に1。
        if (isLancerCharacter) autoSpeed *= lanceMoveSlowFactor;
        if (HasKit) autoSpeed *= kitMoveSlowFactor * VampireRunBoost; // 新キャラの技の最中/吸血鬼のBlood Rush(既存5人は対象外)
        float knockbackFrac = knockbackDuration > 0f ? knockbackTimer / knockbackDuration : 0f;
        float effectiveKnockback = knockbackVelocityX * knockbackFrac;
        float newX = transform.position.x + (autoSpeed + lungeVelocityX + effectiveKnockback) * dt;
        float prevX = transform.position.x;

        // Two independent, parallel surfaces the player can stand on - the
        // main ground path, and (optionally) an elevated sky-path platform
        // floating above it. Which one is actually "the ground" beneath the
        // player depends on which they last landed on (onSky).
        float? groundHeight = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(newX) : null;
        float? prevGroundHeight = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(prevX) : null;
        float? skyHeight = TerrainManager.Instance != null ? TerrainManager.Instance.GetSkyHeightAt(newX) : null;
        float? prevSkyHeight = TerrainManager.Instance != null ? TerrainManager.Instance.GetSkyHeightAt(prevX) : null;
        float prevY = transform.position.y;

        // 魔法使い(2026-09-27) - ジャンプ/重力/着地の代わりに浮遊(高度段階)。天井の上限・天井の針・
        // 位置の確定は下の共通処理(FinishMove)をそのまま通す。
        if (kit == CharacterKit.Mage)
        {
            MageFlightMove(dt, newX, prevY, groundHeight, skyHeight);
            return;
        }

        // Operation System Ver.2, item 2 - 旧タップ判定(touchJumpRequested)
        // を廃止し、上フリック(requestedFlick==Up)のみがジャンプを起動す
        // る。物理挙動(velocityY/jumpsUsed等)自体は完全に既存のまま - 上
        // フリックは「ジャンプの新しい起動トリガー」であって、ジャンプの
        // 挙動そのものを変えるものではない。
        bool jumpPressed = allowJump && requestedFlick == FlickDirection.Up;
        // 不具合修正(2026-09-09) - 「下攻撃中に上攻撃を押してもキャンセル
        // されず、そのまま下攻撃のままになる」。下降攻撃中はjumpsUsedが
        // 既にmaxJumpsに達しているのが通常のため、下のjumpPressed&&
        // jumpsUsed<maxJumpsを満たせず、また(前回パスで追加した)着地バッ
        // ファへ回されるだけで、実際には着地するまで何も起きなかった -
        // 「それぞれ各攻撃が反映される」ようにするため、下降攻撃中の上フ
        // リックは最優先で判定し、下降攻撃を即キャンセル(強制急降下速度
        // を止め、通常の重力へ戻す)した上でその場で上昇攻撃(空中版の
        // Hitbox+VFXのみ - ジャンプ物理・jumpsUsedには触れない、あくまで
        // 「攻撃の切り替え」であって追加のジャンプ高度を与えるものではな
        // い)を発動する。
        if (jumpPressed && isDiveAttacking)
        {
            EndDiveAttack();
            velocityY = 0f;
            if (canUseAirAttack) StartCoroutine(DoUpAttack(true));
        }
        // 二丁拳銃士(2026-09-23) - Down Shotのホバー中に上フリックした場合も、
        // 剣士の「急降下→即キャンセルしてその場で上昇攻撃」と同じ考え方で
        // ホバーを終了し、即座にUp Shotへ切り替える(ジャンプ物理自体は
        // 使わない、あくまで攻撃の切り替え)。
        else if (jumpPressed && isHoverShooting)
        {
            EndDiveAttack();
            velocityY = 0f;
            if (canUseAirAttack) DoRangedUpShot();
        }
        else if (jumpPressed && jumpsUsed < maxJumps)
        {
            FireJump();
        }
        // 不具合修正(2026-09-08) - 「下攻撃を使用したあと、上攻撃ができな
        // くなる」の依頼に含まれていた「入力バッファ」対応(任意実装扱い)。
        // 下降攻撃中(≒jumpsUsedが既にmaxJumps)に上フリックしても、上の
        // 条件を満たせず何も起きずそのまま入力が失われていた(次フレーム
        // でrequestedFlickはnullへ戻る) - 着地直前〜着地直後の一瞬だけ狙う
        // のは実機ではシビアなので、短時間だけ「地上上攻撃をしたがってい
        // る」ことを覚えておき、着地でjumpsUsedが0に戻った瞬間に自動的に
        // FireJump()(=Ground Up Attack)を発動する。
        else if (jumpPressed)
        {
            bufferedUpAttackTimer = upAttackBufferWindow;
        }
        // 方向攻撃システム Ver.2、項目3/4 - "空中で↓フリック=下降攻撃"、
        // "地上での↓フリックは無効"。!isGroundedガードがそのまま項目4の
        // 「地上では無効」要件を実現する - isGrounded中はこの分岐に到達
        // すらしない。既に下降攻撃中(isDiveAttacking)の再トリガーは無視
        // (Hitbox/SE/Slash FXの再スタートによる違和感を避けるため)。
        else if (allowJump && !isGrounded && canUseDownAttack && requestedFlick == FlickDirection.Down)
        {
            // 二丁拳銃士(2026-09-23) - 急降下(isDiveAttacking)ではなく、
            // 斜め下射撃+短時間のホバーへ分岐(isHoverShooting、DoRangedDownShot
            // 参照)。「1回の滞空中にmaxHoverShotsPerAirtime回まで」制限は
            // hoverShotsUsedThisAirtime(着地の瞬間にのみ0へリセット)で管理する。
            if (isRangedCharacter)
            {
                if (!isHoverShooting && hoverShotsUsedThisAirtime < maxHoverShotsPerAirtime) DoRangedDownShot();
            }
            else if (isLancerCharacter)
            {
                // 竜騎士の下攻撃はHandleAttackInputで処理(急降下はしない)。
            }
            else if (HasKit)
            {
                // 新4人の下攻撃もHandleAttackInput(HandleKitInput)で処理。
            }
            else if (!isDiveAttacking)
            {
                DoDiveAttack();
            }
        }
        else if (isGrounded)
        {
            float? currentSurface = onSky ? skyHeight : groundHeight;
            if (!currentSurface.HasValue)
            {
                velocityY = 0f;
                isGrounded = false;
            }
        }

        float newY;
        if (isGrounded)
        {
            float currentSurfaceHeight = (onSky ? skyHeight : groundHeight) ?? (prevY - groundOffset);
            newY = currentSurfaceHeight + groundOffset;
        }
        else
        {
            // 項目3 - 下降攻撃中は通常の重力加速ではなく、一定の速い下降
            // 速度に固定("単純に落下速度を上げるだけではなく...攻撃した
            // 結果、その勢いで下降している"という体感を優先 - 毎フレーム
            // 同じ速度を再代入することで、通常落下との違いを明確にする)。
            if (isDiveAttacking)
            {
                velocityY = -diveAttackSpeed;
            }
            // 二丁拳銃士(2026-09-23) - Down Shot中は「Player共通のGravity
            // 値そのものは変えず」、急降下と同じ"一定速度への直接上書き"
            // パターンだけを流用して落下速度を短時間だけ大幅に弱める。
            // isHoverShooting自体がEndDiveAttack()で必ずfalseへ戻る(着地/
            // Hurt/Death/Respawnのいずれでも)ため、通常のGravity計算へ
            // 必ず復帰する。
            else if (isHoverShooting)
            {
                velocityY = -rangedHoverFallSpeed;
            }
            // 竜騎士(2026-09-26 第2弾) - 急降下突き: 構え中は空中で一瞬止まり、落下中は一定の速い速度で真下へ。
            else if (isLancerCharacter && lanceDiving)
            {
                velocityY = LanceDiveVelocityY();
            }
            // 新4人(2026-09-27) - ダイブキック/急降下斬り/空中で矢を放つ一瞬の滞空など、技が縦速度を決める間。
            else if (HasKit && kitVerticalVelocity.HasValue)
            {
                velocityY = kitVerticalVelocity.Value;
            }
            else
            {
                // エリアルコンボ改修(2026-09-11), item 4 - Aerial Assist
                // ウィンドウ中は重力を弱める。ウィンドウ自体の残り時間は
                // ここで消費し、消費した分だけ累積使用量に積む(着地時に
                // リセット - 下のisGrounded分岐参照)。
                float gravityScale = 1f;
                if (aerialAssistTimer > 0f)
                {
                    gravityScale = aerialAssistGravityScale;
                    float used = Mathf.Min(dt, aerialAssistTimer);
                    aerialAssistTimer -= used;
                    aerialAssistTotalUsed += used;
                }
                velocityY -= gravity * gravityScale * dt;
                KitClampFall(); // 竜人の短い滑空中だけ落下速度を抑える(他キャラは何もしない)
            }
            newY = prevY + velocityY * dt;

            // 自然洞窟(2026-09-21) - 天井にぶつかったら上昇を止める(ダメージ無し)。
            // 速度を0にするので、張り付かず次フレームから通常どおり落下に移れる。
            // 洞窟以外のステージではnullなので何も起きない。
            float? caveLimitY = TerrainManager.Instance != null ? TerrainManager.Instance.GetCeilingLimitY(newX) : null;
            if (caveLimitY.HasValue && newY > caveLimitY.Value)
            {
                // 通路の空間が足りない場所(生成側で保証しているが念のため)でも、足元より下へは押し込まない。
                // 押し込むと「床の下」に入って着地判定をすり抜け、挟まる/落ち続ける原因になる。
                float lim = caveLimitY.Value;
                if (skyHeight.HasValue && prevY >= skyHeight.Value + groundOffset - 0.05f) lim = Mathf.Max(lim, skyHeight.Value + groundOffset);
                if (groundHeight.HasValue && prevY >= groundHeight.Value + groundOffset - 0.05f) lim = Mathf.Max(lim, groundHeight.Value + groundOffset);
                newY = Mathf.Min(newY, lim);
                if (velocityY > 0f) velocityY = 0f;
                // 天井に触れたら空中滞空補助(重力低下)を打ち切り、通常の重力で自然に落ちる(張り付き防止)。
                aerialAssistTimer = 0f;
            }

            // Only land if we actually crossed a surface this frame. We
            // compare *surface-relative* height (position minus the terrain
            // height directly below) rather than raw Y, because on an
            // upward slope the terrain itself is rising each frame -
            // comparing raw Y against only this frame's (higher) surface
            // let the player clip straight through it. Checked against
            // BOTH surfaces since either could be what's below the player
            // right now; if both qualify, land on whichever is higher
            // (physically reached first while falling).
            bool landedSky = false;
            float skySurfaceY = 0f;
            if (skyHeight.HasValue)
            {
                float newSurfaceY = skyHeight.Value + groundOffset;
                float prevSurfaceY = (prevSkyHeight ?? skyHeight.Value) + groundOffset;
                if (prevY - prevSurfaceY >= 0f && newY - newSurfaceY <= 0f)
                {
                    landedSky = true;
                    skySurfaceY = newSurfaceY;
                }
            }

            bool landedGround = false;
            float groundSurfaceY = 0f;
            if (groundHeight.HasValue)
            {
                float newSurfaceY = groundHeight.Value + groundOffset;
                float prevSurfaceY = (prevGroundHeight ?? groundHeight.Value) + groundOffset;
                if (prevY - prevSurfaceY >= 0f && newY - newSurfaceY <= 0f)
                {
                    landedGround = true;
                    groundSurfaceY = newSurfaceY;
                }
            }

            if (landedSky && (!landedGround || skySurfaceY >= groundSurfaceY))
            {
                newY = skySurfaceY;
                velocityY = 0f;
                isGrounded = true;
                jumpsUsed = 0;
                hoverShotsUsedThisAirtime = 0; // 二丁拳銃士: Down Shotは着地で再使用可能に
                // エリアルコンボ改修(2026-09-11) - 着地でAerial Assistの
                // 累積使用量をリセット(次に空中へ出た時、また上限いっぱい
                // まで使えるようにする)。
                aerialAssistTimer = 0f;
                aerialAssistTotalUsed = 0f;
                onSky = true;
                bool wasDiveAttacking = isDiveAttacking;
                EndDiveAttack();
                if (wasDiveAttacking) { DiveAttackLanded?.Invoke(); TriggerDiveImpact(); }
                OnLancerLanded(); // 竜騎士: 急降下の着地/残った攻撃状態の安全な解除(他キャラは何もしない)
                OnKitLanded(); // 新4人: ダイブキック/急降下斬りの着地など(既存5人は何もしない)
                if (AudioManager.Instance != null) AudioManager.Instance.PlayLand();
                Landed?.Invoke();
                // 不具合修正(2026-09-08) - 着地直前に上フリックした分の
                // バッファ消化(入力バッファ、上のbufferedUpAttackTimerの
                // コメント参照)。
                if (bufferedUpAttackTimer > 0f)
                {
                    bufferedUpAttackTimer = 0f;
                    FireJump();
                }
            }
            else if (landedGround)
            {
                newY = groundSurfaceY;
                velocityY = 0f;
                isGrounded = true;
                jumpsUsed = 0;
                hoverShotsUsedThisAirtime = 0; // 二丁拳銃士: Down Shotは着地で再使用可能に
                aerialAssistTimer = 0f;
                aerialAssistTotalUsed = 0f;
                onSky = false;
                bool wasDiveAttacking = isDiveAttacking;
                EndDiveAttack();
                if (wasDiveAttacking) { DiveAttackLanded?.Invoke(); TriggerDiveImpact(); }
                OnLancerLanded(); // 竜騎士: 急降下の着地/残った攻撃状態の安全な解除(他キャラは何もしない)
                OnKitLanded(); // 新4人: ダイブキック/急降下斬りの着地など(既存5人は何もしない)
                if (AudioManager.Instance != null) AudioManager.Instance.PlayLand();
                Landed?.Invoke();
                if (bufferedUpAttackTimer > 0f)
                {
                    bufferedUpAttackTimer = 0f;
                    FireJump();
                }
            }
        }

        FinishMove(newX, newY, dt, tilt: true);
    }

    // Move()の最後の共通処理(位置の確定・坂の傾き・天井の針・落下判定)。魔法使いの浮遊(MageFlightMove)も
    // 同じここを通るので、天井の針は飛行中でも他キャラと全く同じ条件で当たる(2026-09-27に抽出、処理内容は無変更)。
    void FinishMove(float newX, float newY, float dt, bool tilt)
    {
        transform.position = new Vector3(newX, newY, 0f);
        if (tilt) UpdateSlopeTilt(newX);
        else transform.rotation = Quaternion.identity;

        // 自然洞窟(2026-09-21) - 天井の針。既存のTakeDamage(無敵時間+安全地点復帰)を
        // そのまま使うので、接触し続けても毎フレームのダメージや挟まりは起きない。
        if (caveSpikeCooldown > 0f) caveSpikeCooldown -= dt;
        if (caveSpikeCooldown <= 0f && hitInvincibleTimer <= 0f && !hasDied && TerrainManager.Instance != null && TerrainManager.Instance.IsCeilingSpikeHit(newX, newY))
        {
            caveSpikeCooldown = 1.5f;
            CaveStage.SpikeHitCount++;
            Debug.Log($"[Cave] Spike hit x={newX:F1} feetY={newY:F1}");
            kitHazardDamage = true; // 地形の針は忍者の瞬身の無敵/格闘家のカウンターで防げない
            try { TakeDamage(); } finally { kitHazardDamage = false; }
        }

        // Bugfix 2026-09-06, item 2 - "下り坂走行中に突然GAME OVER". Root
        // cause: this check used to fire on raw newY alone, with no
        // isGrounded guard. TerrainManager's downhill-slope RNG walk isn't
        // hard-capped (PickNextType only gently biases back toward Y=0,
        // never forces it), so a real, unbroken, fully-grounded downhill
        // run can legitimately drift the ground height itself below failY -
        // and every single frame of that ordinary walk was tripping this
        // exact "fall death" check even though the player never left the
        // ground even once. A genuine fall (a Pit, the only place ground
        // height isn't continuously sampled under the player) always shows
        // up as isGrounded=false first - gating on that alone fixes this
        // without needing failY itself to become relative/dynamic.
        if (!isGrounded && newY < failY)
        {
            TakeDamage(isFall: true);
        }
    }

    // Rotates the player to visually sit flush against a sloped ground
    // segment - only while grounded on the main path (never mid-jump, and
    // never on the flat sky path, both of which snap back to upright).
    // Smoothed rather than snapped so a chunk boundary between a flat and
    // sloped segment doesn't pop instantly.
    void UpdateSlopeTilt(float x)
    {
        float targetAngle = 0f;
        if (isGrounded && !onSky && TerrainManager.Instance != null)
        {
            targetAngle = TerrainManager.Instance.GetSlopeAngleAt(x);
        }

        float currentAngle = transform.eulerAngles.z;
        float newAngle = Mathf.LerpAngle(currentAngle, targetAngle, Time.deltaTime * slopeTiltSpeed);
        transform.rotation = Quaternion.Euler(0f, 0f, newAngle);
    }

    // Single entry point for every "the player got hurt" source (enemy
    // contact, boss contact, fireball, falling). Non-fatal hits snap the
    // player back onto solid ground right where the hit happened, with a
    // few seconds of flickering invincibility; running out of lives lets
    // GameManager end the run, which the normal per-frame IsGameOver check
    // above then reacts to.
    // source: 高速走行中のフリーズ/ワープ調査(2026-09-22)向けの診断専用
    // パラメータ - 呼び出し元(敵/ボス/障害物/地形など)を識別するための
    // 短い文字列。省略可能(既存呼び出し全て無変更のままコンパイル通る)で、
    // 挙動には一切影響しない。GameManager.TryDamagePlayerのreasonへ渡す。
    public void TakeDamage(bool isFall = false, string source = null)
    {
        // GameManager.PresentationDamageLockでも防いでいるが、Finish演出中
        // (RUN正常終了)はPlayerController側でも二重に無敵化しておく。
        if (hasDied || IsFinishing) return;
        // A fall past failY must always respawn the player, even mid-flicker
        // from a previous hit - otherwise falling while still hit-invincible
        // silently no-ops every frame and the player free-falls forever
        // instead of ever landing back on solid ground.
        if (!isFall && (hitInvincibleTimer > 0f || IsReacting)) return;
        if (GameManager.Instance == null) return;
        // 新4人(2026-09-27) - 忍者の瞬身のごく短い無敵/格闘家のカウンター成立(既存5人は常にfalse)。
        if (!isFall && KitInterceptDamage(source)) return;
        // マルチプレイ: ダウン/脱落中は被弾しない。
        if (NetMatch.Active && !NetMatch.IsLocalAlive) return;
        // マルチ(2026-09-28): カード選択中の本人は敵/ボスの攻撃を受けない(その場で一時停止中のため)。
        if (!isFall && NetIsChoosing) return;

        // Bugfix 2026-09-06, item 2 - GameOverReason passthrough for the
        // debug log in GameManager.TryDamagePlayer (isFall is already the
        // one signal this project has to distinguish a fall death from
        // every other damage source - enemy/boss/fireball contact all call
        // TakeDamage() with isFall left at its false default).
        string reason = (isFall ? "DeathY" : "HPZero") + (string.IsNullOrEmpty(source) ? "" : ":" + source);

        // マルチプレイPhase 2.5: JOINのHPはHOSTが決める。ここではHPを減らさずにHOSTへ被弾を申告し、
        // 確定(NetConfirmHit)を受けてから既存と同じ被弾リアクションを行う(1Hit=1Damageの一本化)。
        if (NetMatch.ClientRoutesHp)
        {
            NetRouteDamage(isFall, reason);
            return;
        }

        GameManager.DamageResult result = GameManager.Instance.TryDamagePlayer(bypassInvincibleMode: isFall, reason: reason);
        if (result != GameManager.DamageResult.Hit) return;
        ApplyDamageReaction(isFall);
    }

    // ===== マルチプレイPhase 2.5: JOINの被弾申告 =====
    float netClaimPendingUntil;

    void NetRouteDamage(bool isFall, string reason)
    {
        if (!NetMatch.IsLocalAlive) return; // ダウン/脱落中は被弾しない
        if (!GameManager.Instance.NetPrecheckDamage(isFall, reason)) return;
        // 申告の返事を待つ間(通信の往復)に別の攻撃で重ねて申告しない(どのみちHOSTが無敵時間で弾く)。
        if (!isFall && Time.realtimeSinceStartup < netClaimPendingUntil) return;
        NetMatch.RouteLocalDamage(isFall, ExpectedHitInvulnerability(isFall), reason);
        if (isFall)
        {
            // 落下はその場で復帰させないと落ち続けるため、リアクションは即座に行う(HPはHOSTの確定に従う)。
            ApplyDamageReaction(true);
        }
        else netClaimPendingUntil = Time.realtimeSinceStartup + 0.6f;
    }

    // 被弾から次に被弾できるまでの秒数(リアクション+無敵)。HOSTがJOINの無敵時間を再現するのに使う。
    float ExpectedHitInvulnerability(bool isFall)
    {
        float dur = !isFall ? (charHurtDuration > 0f ? charHurtDuration : hurtDuration) : (charRecoveryDuration > 0f ? charRecoveryDuration : recoveryDuration);
        float inv = !isFall ? (charHurtInvincible > 0f ? charHurtInvincible : hurtInvincibleDuration) : (charRecoveryInvincible > 0f ? charRecoveryInvincible : recoveryInvincibleDuration);
        return Mathf.Max(0.05f, dur) + inv;
    }

    // HOSTが被弾を確定した(JOIN)。落下は申告時に復帰済み。
    public void NetConfirmHit(bool isFall, float slowFactor, float slowDuration)
    {
        netClaimPendingUntil = 0f;
        if (isFall || hasDied || IsFinishing) return;
        ApplyDamageReaction(false);
    }

    public void NetClaimRejected(int seq) { netClaimPendingUntil = 0f; }

    // ===== マルチ(2026-09-28): カード選択中の本人だけ一時停止 =====
    // 選択UIが開いている間(GameManager.IsLocalChoiceOpen)、この端末のプレイヤーだけを止める:
    //  自動前進/移動/ジャンプ/攻撃の入力/距離の加算(=距離EXP)を行わない。位置・空中の速度はそのまま保持し、
    //  重力も掛けない(穴の上で選択が始まっても落ちない)。ネットワーク上のプレイヤーは消さず、瞬間移動もしない。
    //  選択が終わった次のフレームから、保持していた状態のまま通常の走行へ戻る(追加の無敵は付けない)。
    // 敵/ボスの狙い(NetTargets)と被弾(TakeDamage/HOSTの判定)からは、選択中の間だけ外れる。
    // シングルプレイは従来どおりTimeControlの一時停止で世界ごと止める(ここは通らない)。
    bool netChoosingHeld;
    public bool NetIsChoosing
    {
        get
        {
            if (!NetMatch.Active || netDowned || hasDied || IsFinishing) return false;
            GameManager gm = GameManager.Instance;
            return gm != null && gm.HasStarted && !gm.IsGameOver && gm.IsLocalChoiceOpen;
        }
    }

    void NetChoosingTick()
    {
        if (!netChoosingHeld)
        {
            netChoosingHeld = true;
            CancelAttacksForReaction(); // 出しかけの攻撃判定を残さない
            FreezeDiagnostics.LogEvent($"[Net] CHOOSING CARD - hold at x={transform.position.x:F2} dist={DistanceExact:F1}");
        }
        requestedFlick = null;
        bufferedUpAttackTimer = 0f;
        touchActive = false; // 選択のタップをジャンプ/攻撃の操作として拾わない
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        debugInjectFlick = null;
#endif
        // 被弾後の無敵/リアクションの時間は選択中も通常どおり減らす(選択で無敵が延びない・
        // 無敵点滅が選択の間じゅう続いて相手の画面で消えたり点いたりしない)。
        float dt = Time.deltaTime;
        if (hitInvincibleTimer > 0f) hitInvincibleTimer = Mathf.Max(0f, hitInvincibleTimer - dt);
        if (knockbackTimer > 0f) knockbackTimer = Mathf.Max(0f, knockbackTimer - dt);
        if (Reaction != ReactionKind.None)
        {
            reactionTimer -= dt;
            if (reactionTimer <= 0f) { reactionTimer = 0f; Reaction = ReactionKind.None; }
        }
        if (sr != null && !sr.enabled && hitInvincibleTimer <= 0f && !hasDied) sr.enabled = true;
    }

    void NetChoosingRelease()
    {
        netChoosingHeld = false;
        touchActive = false;
        requestedFlick = null;
        FreezeDiagnostics.LogEvent($"[Net] CHOICE DONE - resume at x={transform.position.x:F2} dist={DistanceExact:F1}");
    }

    // ===== マルチプレイPhase 3: DOWN(CO-OP) / 脱落(VERSUS) =====
    bool netDowned, netEliminated;
    Color netColorBeforeDown = Color.white;
    public bool NetIsDowned => netDowned;

    // HOSTがこのプレイヤーをDOWN/脱落と判定した。操作不可・走行停止・被弾しない(TakeDamage側で除外)。
    public void NetEnterDown(bool eliminated)
    {
        if (netDowned) return;
        netDowned = true;
        netEliminated = eliminated;
        CancelAttacksForReaction();
        velocityY = 0f;
        knockbackTimer = 0f;
        lungeVelocityX = 0f;
        moveSlowTimer = 0f;
        netClaimPendingUntil = 0f;
        // 空中/穴の上で倒れた時だけ、その地点の足場へ寄せる(前方へは進めない)。
        if (!isGrounded) RespawnAtCurrentPosition(true);
        // 倒れている間はHurtの姿勢のまま(入力/攻撃も既存のリアクション中と同じく受け付けない)。
        Reaction = ReactionKind.Hurt;
        reactionTotal = reactionTimer = 9999f;
        hitInvincibleTimer = 0f;
        if (sr != null) { netColorBeforeDown = sr.color; sr.enabled = true; }
        FreezeDiagnostics.LogEvent($"[Net] {(eliminated ? "ELIMINATED" : "DOWN")} at x={transform.position.x:F2} dist={DistanceExact:F1}");
    }

    void NetDownedTick()
    {
        if (sr == null) return;
        sr.enabled = true;
        sr.color = netEliminated ? new Color(1f, 1f, 1f, 0.3f) : new Color(0.55f, 0.55f, 0.68f, 0.85f);
    }

    // CO-OPの復活: DOWNした地点のまま、既存の復帰(Recovery)リアクションとその無敵だけで再開する。
    public void NetRevive()
    {
        if (!netDowned) return;
        netDowned = false;
        netEliminated = false;
        Reaction = ReactionKind.None;
        reactionTimer = 0f;
        if (sr != null) { sr.color = netColorBeforeDown; sr.enabled = true; }
        BeginReaction(ReactionKind.Recovery);
        StartCoroutine(FlickerWhileInvincible());
        FreezeDiagnostics.LogEvent($"[Net] REVIVED at x={transform.position.x:F2} dist={DistanceExact:F1}");
    }

    void ApplyDamageReaction(bool isFall)
    {
        RespawnAtCurrentPosition(isFall);
        // 被弾リアクション: 通常被弾=Hurt、落下復帰=Recovery。無敵時間はリアクション中から数え始め、
        // リアクションが終わってから点滅する(Hurt=被弾の瞬間、点滅=その後の無敵)。
        BeginReaction(isFall ? ReactionKind.Recovery : ReactionKind.Hurt);
        StartCoroutine(FlickerWhileInvincible());

        // Game Feel pass - flash/knockback/SE synchronized with this same
        // "the hit actually landed" moment, same as EnemyController's own
        // hit feedback (see section 20's "Feedbackの同期" brief).
        if (AudioManager.Instance != null) AudioManager.Instance.PlayPlayerDamage();
        if (damageFlashEnabled && sr != null) StartCoroutine(DamageFlashRoutine());
        if (!isFall && !damageKnockbackEnabled) { /* ノックバック無効設定でもHurt停止は行う */ }
    }

    // 被弾リアクションの開始。実行中の攻撃はキャンセルし、Run/攻撃へは一度Normalを経由して戻す。
    void BeginReaction(ReactionKind kind)
    {
        CancelAttacksForReaction();
        Reaction = kind;
        float dur = kind == ReactionKind.Hurt
            ? (charHurtDuration > 0f ? charHurtDuration : hurtDuration)
            : (charRecoveryDuration > 0f ? charRecoveryDuration : recoveryDuration);
        float inv = kind == ReactionKind.Hurt
            ? (charHurtInvincible > 0f ? charHurtInvincible : hurtInvincibleDuration)
            : (charRecoveryInvincible > 0f ? charRecoveryInvincible : recoveryInvincibleDuration);
        reactionTotal = Mathf.Max(0.05f, dur);
        reactionTimer = reactionTotal;
        // 無敵はリアクション中から効かせる(Hurt終了直後に接触中の敵から二重に被弾しない)。
        hitInvincibleTimer = Mathf.Max(hitInvincibleTimer, reactionTotal + inv);
        velocityY = 0f;
        lungeVelocityX = 0f;
        moveSlowTimer = 0f;
        if (kind == ReactionKind.Hurt && damageKnockbackEnabled)
            ApplyKnockback(-hurtKnockbackSpeed * charHurtKnockbackMultiplier, reactionTotal);
        else
            knockbackTimer = 0f; // Recoveryは復帰位置でその場停止(押し戻さない)
    }

    // 実行中の攻撃(通常コンボ/上/下)を止める。死亡やボス演出などは呼び出し側(TakeDamage)が既に除外している。
    void CancelAttacksForReaction()
    {
        attackGeneration++;
        isAttacking = false;
        comboWindowOpen = false;
        comboBuffered = false;
        comboCount = 0;
        lungeVelocityX = 0f;
        transform.localScale = Vector3.one;
        if (attackHitbox != null) attackHitbox.enabled = false;
        if (upAttackHitbox != null) upAttackHitbox.enabled = false;
        if (upAttackVacuumHitbox != null) upAttackVacuumHitbox.enabled = false;
        if (downAttackLandHitbox != null) downAttackLandHitbox.enabled = false;
        EndDiveAttack();
        CancelLanceMoves();
        CancelKitMoves();
    }

    // A brief backward push, decayed over its own duration rather than
    // fighting Move()'s own per-frame position write (see
    // knockbackVelocityX's field comment for why a raw transform.position
    // offset wouldn't survive the next frame here).
    float moveSlowFactor = 1f;
    float moveSlowTimer;
    public bool IsMoveSlowed => moveSlowTimer > 0f;

    // 一時的な走行速度低下(ダメージなし・操作不能にはならない)。既に
    // 減速中なら弱い方で上書きせず、長い方の残り時間/強い方の係数を採用。
    public void ApplyMoveSlow(float factor, float duration)
    {
        factor = Mathf.Clamp(factor, 0.2f, 1f);
        moveSlowFactor = moveSlowTimer > 0f ? Mathf.Min(moveSlowFactor, factor) : factor;
        moveSlowTimer = Mathf.Max(moveSlowTimer, duration);
    }

    public void ApplyKnockback(float velocityX, float duration)
    {
        knockbackVelocityX = velocityX;
        knockbackDuration = Mathf.Max(0.001f, duration);
        knockbackTimer = knockbackDuration;
        FreezeDiagnostics.LogEvent($"[Knockback] velocityX={velocityX:F2} duration={duration:F2} pos=({transform.position.x:F2},{transform.position.y:F2}) timeScale={Time.timeScale:F2}");
    }

    // エリアルコンボ改修(2026-09-11), item 4 - EnemyController.
    // OnTriggerEnter2Dが、プレイヤーが空中(!isGrounded)にいる間に攻撃が
    // 命中するたびに呼ぶ。地上にいる間の呼び出しは無視する(このAssist自体
    // が「空中攻撃時の滞空補助」なので、地上ヒットに意味はない)。
    public void NotifyAerialHit()
    {
        if (isGrounded) return;
        if (aerialAssistTotalUsed >= aerialAssistMaxTotalDuration) return; // 安全装置 - 使い切ったら以降は効かない

        // ①落下速度を少しリセット(既にこれより遅い=上昇中/緩やかな場合は
        // 触らない)。
        if (velocityY < aerialAssistFallResetSpeed) velocityY = aerialAssistFallResetSpeed;
        // ②短時間だけ重力を弱める(連続ヒットで延長 - 上限はMove()側の
        // 累積カウントで別途キャップする)。
        aerialAssistTimer = aerialAssistWindowDuration;
    }

    // 実機フィードバック(2026-09-12第3弾) - EnemyController.ProcessHitが
    // 地上通常攻撃の命中時に毎回呼ぶ。groundHitConnectSlowdownFactorが0
    // (既定)の間は完全に無効 - Enemy側の速度ベースノックバックだけで
    // 間合いが作れない場合の補助として、必要ならInspectorで有効化する。
    public void NotifyGroundHitConnect()
    {
        if (groundHitConnectSlowdownFactor <= 0f) return;
        groundHitConnectSlowdownTimer = groundHitConnectSlowdownDuration;
    }

    IEnumerator DamageFlashRoutine()
    {
        Color normal = sr.color;
        sr.color = damageFlashColor;
        yield return new WaitForSecondsRealtime(damageFlashDuration);
        // FlickerWhileInvincible (started alongside this) only ever toggles
        // sr.enabled, never sr.color, so restoring the normal color here is
        // safe regardless of which one finishes first.
        sr.color = normal;
    }

    // Snaps back onto solid ground at (roughly) the X position where the hit
    // happened, rather than sending the player all the way back to the start
    // of the run. If that X is inside a pit (the falling case), it finds the
    // solid ground just behind the pit instead of respawning into empty air.
    // Stage01地形挙動修整(2026-09-17), item2 - マスター指摘「上ルートで
    // 被弾しても下ルートへ強制移動しない」。以前はここで無条件にonSky=
    // falseへ戻し、GetHeightAt(下ルートの地面高さ)だけを見ていたため、
    // 上ルート(分岐区間)上で通常被弾すると即座に下ルートへ落とされて
    // いた。isFall=falseかつ被弾時点でonSky=true(かつ実際に分岐区間内)
    // の場合だけ上ルート上に留める - isFall=true(穴への落下)は必ず
    // 下ルートの穴からしか発生しない(上ルートにはPitが存在しない)ため、
    // 従来どおり下ルート側の安全地点(FindSafeRespawnX)を使う。
    void RespawnAtCurrentPosition(bool isFall = false)
    {
        bool recoverOnSky = !isFall && onSky && TerrainManager.Instance != null && TerrainManager.Instance.IsInBranchRoute(transform.position.x);
        Vector3 beforePos = transform.position;
        FreezeDiagnostics.NoteIntendedMove(isFall ? "fall recovery (respawn)" : "hit respawn");

        velocityY = 0f;
        isGrounded = true;
        jumpsUsed = 0;
        hoverShotsUsedThisAirtime = 0; // 二丁拳銃士: Down Shotは着地(復帰)で再使用可能に
        aerialAssistTimer = 0f;
        aerialAssistTotalUsed = 0f;
        lungeVelocityX = 0f;
        transform.localScale = Vector3.one;
        EndDiveAttack();
        if (isLancerCharacter) CancelLanceMoves();
        if (HasKit) OnKitRespawn(); // 新4人: 技の打ち切り+魔法使いの高度を最低段へ

        float x = transform.position.x;
        if (!recoverOnSky && TerrainManager.Instance != null)
        {
            x = TerrainManager.Instance.FindSafeRespawnX(x);
        }

        if (recoverOnSky)
        {
            onSky = true;
            float skyY = TerrainManager.Instance.GetSkyHeightAt(x) ?? (TerrainManager.Instance.GetHeightAt(x) ?? 0f);
            transform.position = new Vector3(x, skyY + groundOffset, 0f);
        }
        else
        {
            onSky = false;
            float groundY = TerrainManager.Instance != null ? (TerrainManager.Instance.GetHeightAt(x) ?? 0f) : 0f;
            transform.position = new Vector3(x, groundY + groundOffset, 0f);
        }

        FreezeDiagnostics.LogEvent($"[Respawn] isFall={isFall} recoverOnSky={recoverOnSky} before=({beforePos.x:F2},{beforePos.y:F2}) after=({transform.position.x:F2},{transform.position.y:F2}) timeScale={Time.timeScale:F2}");
    }

    IEnumerator FlickerWhileInvincible()
    {
        // Hurt/Recovery中は点滅せず(リアクション姿勢を見せる)、終わってから無敵点滅に入る。
        while (IsReacting) yield return null;
        while (hitInvincibleTimer > 0f)
        {
            if (sr != null) sr.enabled = !sr.enabled;
            yield return new WaitForSeconds(hitFlickerInterval);
        }
        if (sr != null) sr.enabled = true;
    }

    // Item 2 - "1000m以降、3秒間長押しすると脱出", distance-gated via
    // GameManager.EscapeAvailable instead of the original Ascension's
    // BossesDefeated>0. Releasing early ("途中で指を離した場合") just
    // resets the timer back to 0 with no separate cancel step needed -
    // UpdateEscapeVisuals/OnGUI below already read escapeHoldTimer<=0 as
    // "not charging" and disappear on their own the very next frame.
    void UpdateEscapeInput()
    {
        if (isAscending) return;

        bool canEscape = GameManager.Instance != null && GameManager.Instance.EscapeAvailable;
        bool down = Input.touchCount > 0
            ? Input.GetTouch(0).phase != TouchPhase.Ended && Input.GetTouch(0).phase != TouchPhase.Canceled
            : Input.GetMouseButton(0);

        if (!canEscape || !down)
        {
            escapeHoldTimer = 0f;
            return;
        }

        escapeHoldTimer += Time.deltaTime;
        if (escapeHoldTimer >= escapeHoldDuration)
        {
            StartCoroutine(DoFinishSequence());
        }
    }

    // RUN正常終了演出(2026-09-23) - 旧"3秒完了→魔法陣→上昇して画面外へ"
    // (元DoEscapeSuccess)を、"減速→停止→距離Tier別Finish Animation→
    // 余韻→Win()"という地面ベースの演出へ置き換え(マスター確認済み、
    // 上昇ビジュアルは廃止)。isAscendingは既存の意味(Move/HandleAttack
    // Input/UpdateEscapeInputを止め、CameraFollowを固定する)のまま流用 -
    // 新しい別フラグを増やさず既存ガード全てを無改造で通す。Win()は
    // GameManager.FinishRun(IsGameOver/IsWin/DrawResults()の"GAME CLEAR")
    // を無改造で呼ぶだけ。
    IEnumerator DoFinishSequence()
    {
        isAscending = true;
        escapeHoldTimer = 0f;
        if (escapeRingRenderer != null) escapeRingRenderer.enabled = false;
        lungeVelocityX = 0f;
        if (attackHitbox != null) attackHitbox.enabled = false;
        if (upAttackHitbox != null) upAttackHitbox.enabled = false;
        EndDiveAttack();
        if (HasKit) { CancelKitMoves(); KitOnRunEnd(); }

        IsFinishing = true;
        if (GameManager.Instance != null) GameManager.Instance.SetPresentationDamageLock(true);
        FinishTierIndex = ResolveFinishTier(GameManager.Instance != null ? GameManager.Instance.MaxDistance : 0f);
        FinishProgress = 0f;

        // 減速(既定0.22秒、0.15〜0.30秒枠): 現在の自動前進速度から自然に0へ。
        float startSpeed = runSpeed * EffectiveSpeedMultiplier();
        float t = 0f;
        while (t < finishDecelDuration)
        {
            t += Time.deltaTime;
            float speed = Mathf.Lerp(startSpeed, 0f, Mathf.Clamp01(t / finishDecelDuration));
            transform.position += Vector3.right * speed * Time.deltaTime;
            KitFinishTick(Time.deltaTime); // 魔法使い: 高く飛んでいても浮遊の最低高度へ降りる(他キャラは何もしない)
            yield return null;
        }

        // FinishAnimation + 余韻。距離が長いほど長め(全Tier約1〜2秒枠)。
        // PlayerAnimatorはFinishProgressを見て手続き的ポーズを進める。
        float hold = finishHoldDurationByTier[Mathf.Clamp(FinishTierIndex, 0, finishHoldDurationByTier.Length - 1)];
        float poseT = 0f;
        while (poseT < hold)
        {
            poseT += Time.deltaTime;
            FinishProgress = Mathf.Clamp01(poseT / hold);
            KitFinishTick(Time.deltaTime);
            yield return null;
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.SetPresentationDamageLock(false);
            // Finish→Result間の間(ま)追加(2026-09-24) - 以前は余韻の直後に
            // Win()を同期的に呼ぶだけで、Result画面が無演出のまま瞬間的に
            // 切り替わっていた(マスター報告「Result遷移が不自然」の原因)。
            // 既存のScreenTransitionManager(TOP↔GAME等の画面遷移で実績
            // あり、画面を覆う→コールバック→開く、合計約0.58秒)を再利用し、
            // 画面が完全に覆われた瞬間にWin()を呼ぶことで、テンポを壊さない
            // 程度の短い暗転を挟む。CharacterSelectUI.Close()と同じ、nullなら
            // 直接Win()を呼ぶ防御パターン。
            if (ScreenTransitionManager.Instance != null)
                ScreenTransitionManager.Instance.PlayTransition(() => GameManager.Instance.Win());
            else
                GameManager.Instance.Win();
        }
    }

    int ResolveFinishTier(float distance)
    {
        for (int i = 0; i < finishTierBoundaries.Length; i++)
            if (distance < finishTierBoundaries[i]) return i;
        return finishTierBoundaries.Length;
    }

    // Item 4 - "長押し開始 -> 足元付近に薄い青+金の帰還魔法陣...0秒->1秒->
    // 2秒->3秒と進むにつれて...円形Gaugeが埋まる/魔法陣が明るくなる".
    // Lazily creates one reusable world-space ring (Assets/Art/Effects/
    // DoubleJumpRing.png via escapeRingSprite), tinted/scaled by charge
    // progress, hidden whenever not actively charging.
    void UpdateEscapeVisuals()
    {
        if (escapeRingSprite == null) return;

        if (!IsEscapeCharging)
        {
            if (escapeRingRenderer != null) escapeRingRenderer.enabled = false;
            return;
        }

        if (escapeRingRenderer == null)
        {
            GameObject go = new GameObject("EscapeRing");
            go.transform.SetParent(transform, false);
            escapeRingRenderer = go.AddComponent<SpriteRenderer>();
            escapeRingRenderer.sprite = escapeRingSprite;
            escapeRingRenderer.sortingOrder = RenderOrder.WorldUi;
        }

        escapeRingRenderer.enabled = true;
        float progress = EscapeChargeProgress01;
        // Dark Navy -> Gold/Cyan per the brief's palette, brightening as
        // the charge nears completion ("中央へ光が集まる" approximated by
        // the ring itself brightening/growing rather than a separate
        // particle count, per the brief's own "重いPost Processing等は不
        // 要" - a simple color/scale lerp instead).
        Color navy = new Color(0.2f, 0.35f, 0.55f, 0.5f);
        Color goldCyan = new Color(0.75f, 0.95f, 1f, 0.95f);
        escapeRingRenderer.color = Color.Lerp(navy, goldCyan, progress);
        escapeRingRenderer.transform.localScale = Vector3.one * Mathf.Lerp(0.55f, 1.25f, progress);
        escapeRingRenderer.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        escapeRingRenderer.transform.Rotate(Vector3.forward, 70f * Time.deltaTime);
    }

    void OnGUI()
    {
        if (GameManager.Instance == null || GameManager.Instance.IsGameOver) return;

        DrawFallDeadlineWarning();

        if (IsEscapeCharging)
        {
            DrawEscapeChargeGauge();
        }
    }

    // 方向攻撃システム Ver.2、項目5/6 - 画面下部を何本もの水平ストリップ
    // に分けてAlphaを線形補間するだけの実装(専用シェーダー/グラデーショ
    // ンテクスチャ不要、GUI.DrawTexture+GUI.colorのみ - DrawEscapeChargeGauge
    // と同じ既存IMGUIの使い方に揃えている)。「下ほど濃い霧」+「近づくほ
    // ど全体のIntensityが強くなる」の2軸で「段階的な表示」(項目5の要件)
    // を表現する。deadlineWarningStartDistanceより遠ければ即return - 常
    // 時は完全に非表示。
    void DrawFallDeadlineWarning()
    {
        if (isAscending) return; // ESCAPE成功後の上昇中は無関係な警告を出さない

        float distance = transform.position.y - failY;
        float intensity = Mathf.Clamp01((deadlineWarningStartDistance - distance) / Mathf.Max(0.01f, deadlineWarningStartDistance));
        if (intensity <= 0.001f) return;

        // Bugfix 2026-09-08, item6 - failYをワールド→スクリーンY(GUI座標、
        // 0=画面最上部)へ変換し、その線から画面最下部までを丸ごと覆う。
        // Camera.mainが取れない/デッドラインがまだ画面下端より下(=遠く
        // てまだ見えないはず)の時は何も描かない。
        Camera cam = Camera.main;
        if (cam == null) return;

        float camTopWorldY = cam.transform.position.y + cam.orthographicSize;
        float worldHeight = cam.orthographicSize * 2f;
        if (worldHeight <= 0.0001f) return;
        float deadlineScreenY = (camTopWorldY - failY) / worldHeight * Screen.height;

        float topY = Mathf.Clamp(deadlineScreenY, 0f, Screen.height);
        float coverHeight = Screen.height - topY;
        if (coverHeight <= 0f) return; // デッドラインがまだ画面下端より下 - 覆う範囲なし

        Color prev = GUI.color;

        // モヤ状のグラデーション帯(デッドライン直上、intensityで滲みの
        // 濃さも一緒に強める) - coverHeightがfadeBandより浅い(デッドラ
        // インが画面のほぼ下端にある)場合はcoverHeight全体をグラデーシ
        // ョンにする。
        const int fadeStrips = 16;
        float fadeBandHeight = Mathf.Min(deadlineFogFadeBandHeight, coverHeight);
        float fadeStripHeight = fadeBandHeight / fadeStrips;
        for (int i = 0; i < fadeStrips; i++)
        {
            float fadeT = fadeStrips > 1 ? (float)i / (fadeStrips - 1) : 0f; // 0=デッドライン直上(透明)->1=帯の下端(ほぼ本体濃度)
            float alpha = Mathf.Lerp(0f, deadlineFogSolidAlpha, fadeT) * intensity;
            float y = topY + i * fadeStripHeight;
            GUI.color = new Color(deadlineWarningColor.r, deadlineWarningColor.g, deadlineWarningColor.b, alpha);
            GUI.DrawTexture(new Rect(0f, y, Screen.width, fadeStripHeight + 1f), Texture2D.whiteTexture);
        }

        // 帯の下、画面最下部までは本体(ほぼ不透明) - "デッドラインより下
        // を覆い隠す"の本体部分。
        float solidTop = topY + fadeBandHeight;
        float solidHeight = Screen.height - solidTop;
        if (solidHeight > 0f)
        {
            GUI.color = new Color(deadlineWarningColor.r, deadlineWarningColor.g, deadlineWarningColor.b, deadlineFogSolidAlpha * intensity);
            GUI.DrawTexture(new Rect(0f, solidTop, Screen.width, solidHeight), Texture2D.whiteTexture);
        }

        GUI.color = prev;
    }

    // Item 3 - "実際には文字主体ではなく、円形ゲージを中心にしてくださ
    // い". IMGUI has no native radial/pie-slice fill, so this approximates
    // "円形Gaugeが埋まる" with the same ring sprite scaling up and
    // brightening (Dark Navy -> Gold/Cyan) as progress increases, with the
    // %/remaining-seconds text as secondary annotation around it rather
    // than the main event - a disclosed simplification, not a true radial
    // wipe.
    void DrawEscapeChargeGauge()
    {
        float progress = EscapeChargeProgress01;
        float remaining = Mathf.Max(0f, escapeHoldDuration - escapeHoldTimer);

        Camera cam = Camera.main;
        Vector3 screenPos = cam != null ? cam.WorldToScreenPoint(transform.position) : new Vector3(Screen.width / 2f, Screen.height / 2f, 0f);
        float guiY = Screen.height - screenPos.y;

        const float ringSize = 90f;
        Rect ringRect = new Rect(screenPos.x - ringSize / 2f, guiY - ringSize - 90f, ringSize, ringSize);

        if (escapeRingSprite != null)
        {
            Color prev = GUI.color;
            GUI.color = Color.Lerp(new Color(0.55f, 0.75f, 1f, 0.6f), new Color(1f, 0.92f, 0.5f, 1f), progress);
            GUI.DrawTexture(ringRect, escapeRingSprite.texture, ScaleMode.ScaleToFit);
            GUI.color = prev;
        }

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 16;
        labelStyle.fontStyle = FontStyle.Bold;
        labelStyle.alignment = TextAnchor.MiddleCenter;
        labelStyle.normal.textColor = new Color(0.75f, 0.95f, 1f);
        GUI.Label(new Rect(ringRect.x - 30f, ringRect.y - 26f, ringRect.width + 60f, 24f), "ESCAPE", labelStyle);

        GUIStyle pctStyle = new GUIStyle(GUI.skin.label);
        pctStyle.fontSize = 20;
        pctStyle.fontStyle = FontStyle.Bold;
        pctStyle.alignment = TextAnchor.MiddleCenter;
        pctStyle.normal.textColor = Color.white;
        GUI.Label(ringRect, $"{Mathf.RoundToInt(progress * 100f)}%", pctStyle);

        GUIStyle secStyle = new GUIStyle(GUI.skin.label);
        secStyle.fontSize = 14;
        secStyle.alignment = TextAnchor.MiddleCenter;
        secStyle.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
        GUI.Label(new Rect(ringRect.x - 30f, ringRect.yMax + 2f, ringRect.width + 60f, 22f), $"{remaining:0.0}s", secStyle);
    }

    void OnDeath()
    {
        CancelLanceMoves();
        if (HasKit) { CancelKitMoves(); KitOnRunEnd(); }
        // 専用の死亡ポーズを持つキャラは消さずにその場でポーズを見せる(PlayerAnimator.State.Death)。
        if (sr != null && !charHasDeathFrames) sr.enabled = false;
        if (attackHitbox != null) attackHitbox.enabled = false;
        if (upAttackHitbox != null) upAttackHitbox.enabled = false;
        EndDiveAttack();

        // "最後の被弾 -> Player死亡状態 -> Death SE -> 短い死亡Visual ->
        // GAME OVER" (Game Feel pass, section 12) - Death SE plays instead
        // of another Player Damage SE for this final hit (see section 13's
        // role separation), and the BGM fades out alongside it rather than
        // cutting or continuing to play under the results screen.
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayPlayerDeath();
            AudioManager.Instance.FadeOutBgm();
        }

        if (explosionParticleSprite != null && !charHasDeathFrames)
        {
            // 0.6s (was ExplosionEffect.Create's own 3s default) - "短い死
            // 亡Visual" per the brief; GameManager's own game-over flow
            // doesn't wait on this either way (see EnemyController's
            // matching comment on its own death burst).
            ExplosionEffect.Create(explosionParticleSprite, transform.position, explosionColor, count: 16, duration: 0.6f);
        }
    }

    // Operation System Ver.2, item 3/4 - Up方向は既存のForward/Backwardコ
    // ンボチェーンに一切関与しない(Move()側のDoUpAttackで独立に処理済み -
    // その専用コメント参照)ので、ここではForward/Backwardだけをそのまま
    // AttackDirectionへ1:1変換する。既存の3段コンボ/Lunge/Recoil処理
    // (DoAttack以下)はコード変更なしでそのまま動く。
    void HandleAttackInput()
    {
        attackCooldownTimer -= Time.deltaTime;
        // 新4人(2026-09-27) - 前/後/下(魔法使いは上も)を専用処理へ(既存5人は下の従来処理のまま)。
        if (HasKit)
        {
            HandleKitInput();
            return;
        }
        // 竜騎士(2026-09-26) - 下攻撃は地上でも空中でも「前方下への突き」(Move()の
        // 空中↓フリック分岐=急降下/ホバーは竜騎士では何もしない)。
        if (isLancerCharacter && requestedFlick == FlickDirection.Down)
        {
            if (canUseDownAttack) TryLanceDownThrust();
            return;
        }
        AttackDirection? requested = requestedFlick switch
        {
            FlickDirection.Forward => AttackDirection.Forward,
            FlickDirection.Backward => AttackDirection.Backward,
            _ => (AttackDirection?)null
        };
        if (requested == null) return;
        // 竜騎士(2026-09-26 第2弾) - 3段突きの予約/受付時間は専用処理で(他キャラは下の従来処理のまま)。
        if (isLancerCharacter)
        {
            HandleLanceHorizontalInput(requested.Value);
            return;
        }

        if (!isAttacking && attackCooldownTimer <= 0f)
        {
            comboCount = 0;
            StartCoroutine(DoAttack(requested.Value));
        }
        else if (isAttacking && comboWindowOpen && !comboBuffered)
        {
            // Buffer only one input at a time - timing it right chains the
            // next attack immediately, but you can't stack up a queue of
            // unlimited attacks by mashing.
            comboBuffered = true;
            bufferedDirection = requested.Value;
        }
    }

    IEnumerator DoAttack(AttackDirection dir)
    {
        // 二丁拳銃士(2026-09-23) - Forward/Backwardとも「面で斬る」既存の
        // 剣士コンボ(Lunge/Recoil/成長するHitbox)には一切乗せず、専用の
        // 弾丸ロジックへ完全に分岐する。isRanged=falseの既存3キャラの
        // この先の処理(コンボ/Lunge/Hitbox)は一切変更していない。
        if (isRangedCharacter)
        {
            yield return DoRangedForwardBackShot(dir);
            yield break;
        }
        if (isLancerCharacter)
        {
            yield return DoLanceHorizontal(dir);
            yield break;
        }

        int gen = attackGeneration;
        isAttacking = true;
        comboWindowOpen = false;
        comboBuffered = false;
        comboCount++;
        attackCooldownTimer = attackCooldown * AttackSpeedMultiplier;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(comboCount);

        ApplyAttackDirection(dir);
        if (attackHitbox != null) attackHitbox.enabled = true;
        ApplyComboStageToHitbox(comboCount);
        // 品質改善 Bug #002(2026-09-09), item 8/9/11/12 - 通常攻撃も旧
        // 「巨大な紫剣」(framesベースのSetComboStage)から、上/空中/下降
        // 攻撃と同じ青白いVFX(PlaySingle、1枚絵をScale/Alphaで演出)へ
        // 統一。段階ごとのScaleは「通常攻撃0.8-1.3倍/Combo Final 1.3-1.6
        // 倍」の目安どおり(1段目0.9/2段目1.15/3段目1.5)。rangeMultiplier
        // は従来どおりHitboxと完全に同じAttackRangeMultiplierを渡すので、
        // Attack Range Upで見た目も一緒に大きくなる(item 12)。
        // 不具合修正(2026-09-09) - 「攻撃範囲がちゃんと見えるように」。
        // ApplyComboStageToHitbox(上)と全く同じhitboxBaseLocalPos基準+
        // step*hitboxReachStep*AttackRangeMultiplierの位置オフセットを
        // VFXにも与え、Hitboxが実際に前方へ伸びる分だけVFXの表示位置も
        // 一緒に前へ出すことで、Scaleが大きくなるだけでなく「間合いその
        // ものが伸びている」ことが見た目でも分かるようにする(Hitboxと
        // VFXが同じhitboxBaseLocalPos・同じ式を共有しているので、両者が
        // 視覚的にズレることは構造的に起こらない)。
        if (attackSlashVisual != null)
        {
            float stageScale = comboCount switch { 1 => 0.9f, 2 => 1.15f, _ => 1.5f };
            int reachStep = Mathf.Max(0, comboCount - 1);
            attackSlashVisual.transform.localPosition =
                hitboxBaseLocalPos + new Vector3(reachStep * hitboxReachStep * AttackRangeMultiplier, 0f, 0f);
            // 派手なアニメーション化(2026-09-10) - マスターの指示で、1枚絵
            // をScale/Alphaで演出するPlaySingleから、ChatGPT生成の5コマ
            // スプライトシートを実コマ送りするPlayFramesへ差し替え(位置
            // オフセット・stageScale・AttackRangeMultiplierの扱いは一切
            // 変えず、見た目の再生方式だけを差し替える)。
            attackSlashVisual.PlayFrames(stageScale, AttackRangeMultiplier);
        }

        // Once the chain has reached the cap, skip re-opening the combo
        // window: no input gets buffered this swing, so the player falls
        // through to the normal cooldown gap before the next attack.
        bool allowChain = comboCount < maxComboChain;

        // AttackSpeedMultiplier (from "ATTACK SPEED UP") shrinks the whole
        // swing uniformly, so the combo window still opens at the same
        // relative point in the (now shorter) swing.
        float effectiveActiveTime = attackActiveTime * AttackSpeedMultiplier;
        float t = 0f;
        while (t < effectiveActiveTime)
        {
            t += Time.deltaTime;
            if (allowChain && t >= effectiveActiveTime * comboWindowStart) comboWindowOpen = true;
            if (gen != attackGeneration) yield break; // 被弾でキャンセルされた(後始末はCancelAttacksForReaction済み)
            yield return null;
        }

        if (attackHitbox != null) attackHitbox.enabled = false;
        lungeVelocityX = 0f;
        transform.localScale = Vector3.one;

        isAttacking = false;
        comboWindowOpen = false;

        if (comboBuffered)
        {
            comboBuffered = false;
            StartCoroutine(DoAttack(bufferedDirection));
        }
    }

    // 不具合修正(2026-09-08) - 上フリックによるジャンプ発動本体をMove()
    // から抜き出したもの(通常の即時発動と、着地バッファ消化からの発動の
    // 両方で使う - 重複を避けるための単純な抽出、挙動自体は無変更)。
    void FireJump()
    {
        // 二丁拳銃士(2026-09-23) - 念のための安全装置。Move()側の分岐で
        // ホバー中の上フリックは既にDoRangedUpShot直行(EndDiveAttack経由
        // でisHoverShooting解除済み)へ振り分けているため通常ここには来
        // ないが、万一isHoverShootingが残ったままjumpForceが入ると、この
        // フレームのMove()内で「Move()のisHoverShooting分岐がvelocityYを
        // 再度落下速度へ上書きしてジャンプが無かったことになる」事故を防ぐ。
        if (isHoverShooting) { isHoverShooting = false; hoverGeneration++; }
        velocityY = jumpForce;
        isGrounded = false;
        jumpsUsed++;
        if (jumpsUsed == 1)
        {
            JumpStarted?.Invoke();
            if (AudioManager.Instance != null) AudioManager.Instance.PlayJump();
        }
        else
        {
            DoubleJumped?.Invoke();
            if (AudioManager.Instance != null) AudioManager.Instance.PlayDoubleJump();
        }
        // Item 2 - "上フリック=ジャンプ攻撃"/"空中でのもう一度=空中上昇
        // 攻撃"。ジャンプが実際に発動した場合のみ(=jumpsUsed<maxJumpsの
        // ガードを通過した場合のみ)発火するので、既にmaxJumps使い切っ
        // ている状態でのUpフリックは何も起きない(仕様どおり)。
        // プレイアブル主人公追加(2026-09-12、お嬢様騎士) - canUseUpAttack/
        // canUseAirAttackはこのDoUpAttack(見た目+Hitboxのみ)を止めるだけで、
        // 直前のvelocityY=jumpForce/jumpsUsed++/JumpStarted等のジャンプ物理
        // 自体には一切関与しない - ジャンプそのものは常に正常に機能する。
        bool isAirborneUpAttack = jumpsUsed >= 2;
        if (isAirborneUpAttack ? canUseAirAttack : canUseUpAttack)
        {
            // 二丁拳銃士(2026-09-23) - "ジャンプしながら斜め上へ射撃"。
            // ジャンプ物理(上の velocityY=jumpForce 等)には一切関与しない、
            // 見た目Stateの切り替え+弾の発射のみ(敵をLaunchしない=既存の
            // Up Hitbox/Vacuumを一切使わない)。
            if (isRangedCharacter) DoRangedUpShot();
            else if (isLancerCharacter) StartCoroutine(DoLanceUpThrust());
            else if (HasKit) OnKitJump(isAirborneUpAttack); // 新4人: 斜め上の矢/アッパー/跳躍斬り
            else StartCoroutine(DoUpAttack(isAirborneUpAttack));
        }
    }

    // Operation System Ver.2, item 2 - "上フリック=ジャンプ攻撃"/"空中で
    // もう一度=空中上昇攻撃"。isAttacking/comboCount/DoAttackの3段コンボ系
    // 統には一切触れない、独立した短いHitbox+Slash FXパルスとして実装 -
    // 上フリックは仕様上「ジャンプ回数(1段目=地上発射、2段目=空中)」に直
    // 結しており、Forward/Backwardのような3段コンボではないため、既存コ
    // ンボチェーンへ無理に統合するより完全に分離した方が既存処理を壊すリ
    // スクがない(brief item 3の"既存処理を優先して流用"の精神)。
    // キャラクター本体のポーズは新規アニメーションを起こさず、既存の
    // JumpStart/DoubleJumpアニメーション(PlayerAnimator、JumpStarted/
    // DoubleJumped イベント経由・本メソッドとは無関係に既に再生される)を
    // そのまま「上昇攻撃の見た目」として流用し、このHitbox+回転させた
    // Slash FX(既存のAttackSlashFxスプライトをそのまま再利用、新規アート
    // 不要)を重ねることで「攻撃している」印象を追加する、という設計判断
    // - 現時点で新規手描きアニメーションを起こす手段がないため、既存素材
    // の組み合わせで違和感なく繋がる形を優先した(マスターへの開示事項)。
    IEnumerator DoUpAttack(bool isAirborne)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.AttackUp); // 上攻撃/Launch(共通)
        // 攻撃エフェクト全面調整(2026-09-08) - 旧SetComboStage(巨大な紫剣
        // AttackSlashFx流用)から、剣の軌跡に沿った控えめな青白い三日月
        // VFX(PlaySingle、1枚絵をScale/Alphaで演出)へ切り替え。空中版は
        // 地上版よりわずかに大きい(1.15倍)程度に留め、「巨大化させない」
        // 指示どおり控えめに。
        // 不具合修正(2026-09-09) - 通常攻撃と同じ「HitboxとVFXの表示位置が
        // ズレていた」バグが上攻撃にも存在していた(upAttackSlashVisualの
        // transform位置が固定のままだった)ため、upAttackHitboxと全く同じ
        // upHitboxBaseLocalPos*AttackRangeMultiplierをVFXの位置にも適用した
        // が、これはHitbox本体には正しくても見た目には別の問題を生んだ。
        // 不具合修正(2026-09-10) - 「空中上攻撃のエフェクトが攻撃範囲拡張
        // とともにプレイヤーから離れてしまう」。基準位置をそのままAttack
        // RangeMultiplier倍すると、Range Upを積むほどVFX全体(位置ごと)が
        // プレイヤーから遠くへ移動し、キャラクターと繋がって見えなくなる
        // (Hitbox自体は当たり判定なので離れて問題ないが、VFXは「プレイヤ
        // ーから攻撃範囲まで」を見せる役割のため、離れて浮くのはNG)。
        // 位置の移動量はAttackRangeMultiplierの半分だけに抑え(positionRange
        // Factor)、Scale自体は従来どおりPlaySingle側でAttackRangeMultiplier
        // ぶん丸ごと大きくする - 結果、近い側の端はプレイヤーの近くに留ま
        // りつつ、遠い側の端(=剣が実際に届く範囲)だけが伸びるように見える。
        if (upAttackSlashVisual != null)
        {
            float positionRangeFactor = 1f + (AttackRangeMultiplier - 1f) * 0.5f;
            upAttackSlashVisual.transform.localPosition = upHitboxBaseLocalPos * positionRangeFactor;
            // 派手なアニメーション化(2026-09-10) - 通常攻撃と同様、PlaySingle
            // からChatGPT生成5コマの実コマ送りPlayFramesへ差し替え(位置・
            // Scale・rangeの扱いは不変、再生方式のみ差し替え)。
            upAttackSlashVisual.PlayFrames(isAirborne ? 1.15f : 1f, AttackRangeMultiplier);
        }
        if (upAttackHitbox != null)
        {
            upAttackHitbox.transform.localScale = upHitboxBaseScale * AttackRangeMultiplier;
            upAttackHitbox.transform.localPosition = upHitboxBaseLocalPos * AttackRangeMultiplier;
            upAttackHitbox.enabled = true;
        }

        // 実機フィードバック(2026-09-12第5弾) - Pickup/Vacuum。剣を振り上げ
        // た瞬間の一度だけ、頭上付近の空中Enemyを巻き込む(持続的な吸引に
        // はしない - "剣の勢いに巻き込まれた"という一瞬の出来事として扱う)。
        // 他のHitboxと同じ慣習でupAttackActiveTimeの間だけenabled=true(実際
        // の判定はPhysics2D.OverlapBoxAllで一度きり行うため機能上は必須では
        // ないが、DebugMode時のColliderDebugView表示に必要)。
        if (upAttackVacuumHitbox != null) upAttackVacuumHitbox.enabled = true;
        TriggerUpAttackVacuum();

        yield return new WaitForSeconds(upAttackActiveTime);

        if (upAttackHitbox != null) upAttackHitbox.enabled = false;
        if (upAttackVacuumHitbox != null) upAttackVacuumHitbox.enabled = false;
    }

    // Main HitBox(通常のダメージ判定)とは別に、真上を中心としたPickup/
    // Vacuum範囲内にいる「既に空中の敵」だけを、主人公の斜め前上へ引き
    // 寄せる。upAttackVacuumHitboxはPlayerAttackタグを持たない(=通常の
    // OnTriggerEnter2D経由のダメージ判定には一切関与しない)ため、ここで
    // Physics2D.OverlapBoxAllを直接呼んで手動で対象を探す。
    void TriggerUpAttackVacuum()
    {
        if (upAttackVacuumHitbox == null) return;
        Bounds b = upAttackVacuumHitbox.bounds;
        Collider2D[] hits = Physics2D.OverlapBoxAll(b.center, b.size, 0f);
        foreach (Collider2D col in hits)
        {
            if (!col.CompareTag("Enemy")) continue;
            var enemy = col.GetComponent<EnemyController>();
            if (enemy == null) continue;
            enemy.TryVacuumPickup(vacuumTargetOffset, vacuumPullDuration);
        }
    }

    // 方向攻撃システム Ver.2、項目3 - 上昇攻撃(短いパルス)とは違い、下降
    // 攻撃は「着地するまで持続する」ため、コルーチンではなく単純にフラグ
    // /Hitboxを立てるだけ(Move()自身が毎フレームisDiveAttackingを見て
    // velocityYを上書きし続ける)。EndDiveAttack()が着地/Fall死亡/GAME
    // OVER/ESCAPE成功のいずれからも呼ばれ、後始末を一箇所に集約している。
    void DoDiveAttack()
    {
        isDiveAttacking = true;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.AttackDown); // 下攻撃/Slam(共通)
        // 攻撃エフェクト全面調整(2026-09-08) - 旧SetComboStage(巨大な紫剣、
        // 一度再生して消えるだけ)から、着地まで持続表示するShowSustained
        // (細い縦方向トレイル)へ切り替え。EndDiveAttack()側で必ず
        // HideSustained()するので、着地後に残り続けることはない。
        // 不具合修正(2026-09-09) - 上と同じ理由。トレイルは(演出上)Hitbox
        // の真上から伸びる形にしたいので、downHitboxBaseLocalPosそのままで
        // はなく、そこから一定量(downSlashUpwardOffset)だけ上にずらした
        // 位置を基準にする - ただしAttackRangeMultiplierによる拡縮はHitbox
        // と共有しているので、Range Upカードを積んだ時にHitboxとVFXが
        // 一緒に動く/伸びる関係は保たれる。
        if (downAttackSlashVisual != null)
        {
            downAttackSlashVisual.transform.localPosition =
                downHitboxBaseLocalPos * AttackRangeMultiplier + new Vector3(0f, downSlashUpwardOffset, 0f);
            downAttackSlashVisual.ShowSustained(AttackRangeMultiplier);
        }
        if (downAttackHitbox != null)
        {
            downAttackHitbox.transform.localScale = downHitboxBaseScale * AttackRangeMultiplier;
            downAttackHitbox.transform.localPosition = downHitboxBaseLocalPos * AttackRangeMultiplier;
            downAttackHitbox.enabled = true;
        }
    }

    void EndDiveAttack()
    {
        isDiveAttacking = false;
        if (downAttackHitbox != null) downAttackHitbox.enabled = false;
        // 攻撃エフェクト全面調整(2026-09-08) - ShowSustained側の後始末。
        // 着地/Fall死亡/GAME OVER/ESCAPE成功のいずれのEndDiveAttack()呼び
        // 出し経路でも必ず呼ばれるため、トレイルVFXが表示されたまま残る
        // ことはない。
        if (downAttackSlashVisual != null) downAttackSlashVisual.HideSustained();
        // 二丁拳銃士(2026-09-23) - Down Shotのホバー状態も、急降下と全く同じ
        // 4つの後始末経路(着地/Hurt(CancelAttacksForReaction)/Death/
        // Respawn)を通して必ず解除する。hoverGenerationを進めることで、
        // 実行中のEndHoverShotAfterDelay(古い世代)がこの後で誤って
        // isHoverShooting=falseへ"戻す"だけの無害な二重書きに留まる
        // (次のホバー開始と競合しない)。
        isHoverShooting = false;
        hoverGeneration++;
    }

    // 不具合修正(2026-09-10) - 「下攻撃の着地時に衝撃エフェクトを追加し、
    // それにも攻撃判定が入るように」。DiveAttackLandedと同じ「実際に急降下
    // 中だった場合の着地」でのみ呼ばれる(Move()の2箇所、landedSky/
    // landedGround)。ダイブ本体のHitboxは既にEndDiveAttack()で無効化済み
    // なので、ここでは全く別の新しいHitboxを短時間だけ有効にする。
    void TriggerDiveImpact()
    {
        if (downAttackLandHitbox != null)
        {
            downAttackLandHitbox.enabled = true;
            StartCoroutine(DisableDiveImpactHitboxAfterDelay());
        }
        if (downAttackLandSlashVisual != null)
        {
            downAttackLandSlashVisual.PlaySingle(1f, AttackRangeMultiplier);
        }
    }

    IEnumerator DisableDiveImpactHitboxAfterDelay()
    {
        yield return new WaitForSeconds(diveImpactHitboxDuration);
        if (downAttackLandHitbox != null) downAttackLandHitbox.enabled = false;
    }

    // Grows the (invisible) attack hitbox across the combo chain - stage 1
    // is the base size/reach, each further stage scales it up and pushes it
    // a bit further out, matching the growing slash FX so the visible reach
    // and the actual hit reach agree.
    void ApplyComboStageToHitbox(int stage)
    {
        if (attackHitbox == null) return;
        int step = Mathf.Max(0, stage - 1);
        float scaleMul = (1f + step * hitboxScaleStep) * AttackRangeMultiplier;
        attackHitbox.transform.localScale = hitboxBaseScale * scaleMul;
        attackHitbox.transform.localPosition = hitboxBaseLocalPos + new Vector3(step * hitboxReachStep * AttackRangeMultiplier, 0f, 0f);
    }

    // Backward attacks flip the whole player transform, which also mirrors
    // the (forward-facing) hitbox/slash child objects onto the back side for
    // free - no separate backward hitbox needed.
    void ApplyAttackDirection(AttackDirection dir)
    {
        if (dir == AttackDirection.Backward)
        {
            transform.localScale = new Vector3(-1f, 1f, 1f);
            lungeVelocityX = -recoilDistance / attackActiveTime;
        }
        else
        {
            transform.localScale = Vector3.one;
            lungeVelocityX = dir == AttackDirection.Forward ? lungeDistance / attackActiveTime : 0f;
        }
    }

    // ===== 二丁拳銃士(2026-09-23) ===== //
    // 「面で攻撃する剣士」に対する「点で攻撃する遠距離キャラクター」。
    // Forward/Backward/Up/Downの4方向すべてが、既存の剣士Hitbox(近接・
    // 太い判定)ではなくPlayerBullet(細い直線・高速・射程長)を撃つ専用
    // ロジックへ完全に分岐する。ダメージ適用自体は既存のEnemyController/
    // WildBossBase/DragonController/MajinControllerの各OnTriggerEnter2D
    // (タグ"PlayerAttack"+PlayerAttackInfoを読む既存の仕組み)がそのまま
    // 処理するため、Hit Stop/Hit VFX/コンボカウンター/被ダメージリアク
    // ションは変更なしで機能する。

    // Forward/Backward - 水平射撃。既存のApplyAttackDirection(左右反転+
    // Forward/Backwardの区別)はそのまま流用するが、"敵の攻撃範囲外から
    // 一方的に攻撃できる"という武器特性を活かすため、剣士のようなLunge/
    // Recoil(踏み込み)は与えない - 自動前進を一切乱さない。
    IEnumerator DoRangedForwardBackShot(AttackDirection dir)
    {
        int gen = attackGeneration;
        isAttacking = true;
        comboWindowOpen = false;
        comboBuffered = false;
        comboCount++;
        attackCooldownTimer = attackCooldown * AttackSpeedMultiplier;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(comboCount);

        ApplyAttackDirection(dir);
        lungeVelocityX = 0f; // 「銃を撃つたびに完全停止/踏み込みする仕様にはしない」- 自動前進のみ維持

        float facing = transform.localScale.x >= 0f ? 1f : -1f;
        FireRangedBullet(new Vector2(facing, 0f), rangedForwardMuzzleOffset);

        // 攻撃モーション見直し(2026-09-26) - 以前は剣のSlash VFXを小さく
        // 流用していたが「撃っている」と読めなかったため、銃口位置に専用の
        // マズルフラッシュを出す(素材が無ければ従来のSlash流用へフォールバック)。
        if (!PlayMuzzleFlash() && attackSlashVisual != null)
        {
            attackSlashVisual.transform.localPosition = hitboxBaseLocalPos;
            attackSlashVisual.PlayFrames(0.6f, 1f);
        }

        bool allowChain = comboCount < maxComboChain;
        float effectiveActiveTime = attackActiveTime * AttackSpeedMultiplier;
        float t = 0f;
        while (t < effectiveActiveTime)
        {
            t += Time.deltaTime;
            if (allowChain && t >= effectiveActiveTime * comboWindowStart) comboWindowOpen = true;
            if (gen != attackGeneration) yield break; // 被弾でキャンセルされた
            yield return null;
        }

        isAttacking = false;
        comboWindowOpen = false;

        if (comboBuffered)
        {
            comboBuffered = false;
            StartCoroutine(DoAttack(bufferedDirection));
        }
    }

    // Up - "ジャンプしながら斜め上へ射撃"。ジャンプ自体はFireJump側で既に
    // 発動済み(この呼び出しより前)なので、ここでは見た目Stateの切り替え
    // (upShotVisualTimer)と弾の発射だけを行う。剣士のUp Attackと違い、
    // 敵を打ち上げるHitbox/Vacuumは一切使わない(弾自体もPlayerAttackKind.
    // Normalなので、EnemyController側でもUp Launchは発生しない)。
    void DoRangedUpShot()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(1);
        float facing = transform.localScale.x >= 0f ? 1f : -1f;
        FireRangedBullet(new Vector2(facing, 1f));
        upShotVisualTimer = rangedUpShotPoseDuration;
    }

    // Down - 空中で斜め下へ撃ちながら、短時間だけ落下速度を大幅に弱める
    // ("急降下"の逆)。Player共通のGravity値そのものは一切変更せず、
    // Move()側でisHoverShooting中だけvelocityYを直接上書きする方式
    // (既存のisDiveAttackingと全く同じパターン)。EndDiveAttack()が着地/
    // Hurt/Death/Respawnのいずれからも呼ばれるため、ホバー状態が残る
    // ことはない。「1回の滞空中にmaxHoverShotsPerAirtime回まで」は
    // hoverShotsUsedThisAirtime(着地の瞬間にのみリセット)で管理する。
    void DoRangedDownShot()
    {
        isHoverShooting = true;
        hoverShotsUsedThisAirtime++;
        hoverGeneration++;
        int gen = hoverGeneration;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
        float facing = transform.localScale.x >= 0f ? 1f : -1f;
        FireRangedBullet(new Vector2(facing, -1f));
        StartCoroutine(EndHoverShotAfterDelay(gen));
    }

    IEnumerator EndHoverShotAfterDelay(int gen)
    {
        yield return new WaitForSeconds(rangedHoverDuration);
        // 世代が変わっていたら(=着地/被弾/死亡/復帰で既に別の経路から
        // EndDiveAttack()済み、または既に次のホバーが始まっている)何もしない。
        if (gen == hoverGeneration) EndDiveAttack();
    }

    // 前方射撃のマズルフラッシュ(Resources/Effects/muzzleflash、中心Pivot・
    // 右向きの炎)。Player子として出すので自動前進中も銃口に張り付いたまま、
    // 左右反転もRootのlocalScale.xで自動的に付いてくる。
    static Sprite muzzleFlashArt;
    static bool muzzleFlashLoaded;
    public float muzzleFlashDuration = 0.09f;
    public float muzzleFlashSize = 0.8f; // 炎の全長(world unit)。素材は正方形の横いっぱいに炎が伸びる
    SpriteRenderer muzzleFlashRenderer;
    Coroutine muzzleFlashRoutine;

    bool PlayMuzzleFlash()
    {
        if (!muzzleFlashLoaded) { muzzleFlashLoaded = true; muzzleFlashArt = Resources.Load<Sprite>("Effects/muzzleflash"); }
        if (muzzleFlashArt == null) return false;
        if (muzzleFlashRenderer == null)
        {
            var go = new GameObject("MuzzleFlash");
            go.transform.SetParent(transform, false);
            muzzleFlashRenderer = go.AddComponent<SpriteRenderer>();
            muzzleFlashRenderer.sprite = muzzleFlashArt;
            muzzleFlashRenderer.sortingOrder = RenderOrder.SlashFx;
        }
        if (muzzleFlashRoutine != null) StopCoroutine(muzzleFlashRoutine);
        muzzleFlashRoutine = StartCoroutine(MuzzleFlashRoutine());
        return true;
    }

    IEnumerator MuzzleFlashRoutine()
    {
        var t = muzzleFlashRenderer.transform;
        // 素材は幅いっぱいに炎が伸びる正方形 - 中心を銃口から半径ぶん前へ出す。
        t.localPosition = new Vector3(rangedForwardMuzzleOffset.x + muzzleFlashSize * 0.5f, rangedForwardMuzzleOffset.y, 0f);
        t.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-6f, 6f));
        muzzleFlashRenderer.enabled = true;
        float e = 0f;
        while (e < muzzleFlashDuration)
        {
            float k = e / muzzleFlashDuration;
            t.localScale = Vector3.one * muzzleFlashSize * Mathf.Lerp(1.15f, 0.7f, k);
            muzzleFlashRenderer.color = new Color(1f, 1f, 1f, 1f - k * k);
            e += Time.deltaTime;
            yield return null;
        }
        muzzleFlashRenderer.enabled = false;
        muzzleFlashRoutine = null;
    }

    // 銃口位置(rangedMuzzleOffset)からワールド空間の方向へ1発発射する。
    // rangedBulletSpriteが未設定(専用素材未生成)の間は安全に何もしない。
    void FireRangedBullet(Vector2 worldDir) => FireRangedBullet(worldDir, rangedMuzzleOffset);

    void FireRangedBullet(Vector2 worldDir, Vector2 muzzleOffset)
    {
        if (rangedBulletSprite == null) return;
        float facing = transform.localScale.x >= 0f ? 1f : -1f;
        Vector3 spawnPos = transform.position + new Vector3(muzzleOffset.x * facing, muzzleOffset.y, 0f);
        PlayerBullet.Create(rangedBulletSprite, spawnPos, worldDir.normalized * rangedBulletSpeed, rangedBulletLifetime);
    }
}
