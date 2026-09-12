using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(BoxCollider2D))]
public class PlayerController : MonoBehaviour
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
    public float hitInvincibleDuration = 5f;
    public float hitFlickerInterval = 0.1f;

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
    public bool IsHitInvincible => hitInvincibleTimer > 0f;
    public bool IsAscending => isAscending;
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
    public float CurrentAutoRunSpeed => autoRunEnabled ? runSpeed * GetSpeedMultiplier() : 0f;

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
    // 不具合修正(2026-09-08) - 「下攻撃→着地→上攻撃」の入力バッファ。
    // 下降攻撃中(jumpsUsedが既にmaxJumpsで即座にはジャンプできない状態)
    // に上フリックした場合、この時間だけ「地上上攻撃をしたがっている」
    // ことを覚えておき、着地でjumpsUsedが0に戻った瞬間に自動でFireJump()
    // する(Move()のjumpPressed分岐、および着地処理側のバッファ消化を参照)。
    public float upAttackBufferWindow = 0.15f;
    float bufferedUpAttackTimer;
    float attackCooldownTimer;
    float startX;
    bool hasDied;
    float lungeVelocityX;
    float hitInvincibleTimer;
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
    }

    void Update()
    {
        bool hasStarted = GameManager.Instance == null || GameManager.Instance.HasStarted;
        if (!hasStarted)
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

        if (hitInvincibleTimer > 0f)
        {
            hitInvincibleTimer = Mathf.Max(0f, hitInvincibleTimer - Time.deltaTime);
        }

        if (knockbackTimer > 0f) knockbackTimer = Mathf.Max(0f, knockbackTimer - Time.deltaTime);

        if (isAscending) return; // the ascend coroutine drives position directly

        // Item 2 - snapshot BEFORE this frame's UpdateEscapeInput() runs
        // (which may transition escapeHoldTimer back to 0 this very frame,
        // e.g. on release) - see wasEscapeChargingLastFrame's own comment
        // for why the ordering matters.
        wasEscapeChargingLastFrame = IsEscapeCharging;

        UpdatePointerInput();
        Move(allowJump: !wasEscapeChargingLastFrame);
        if (!wasEscapeChargingLastFrame) HandleAttackInput();

        UpdateEscapeInput();
        UpdateEscapeVisuals();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.ReportDistance(transform.position.x - startX);
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

    float GetSpeedMultiplier()
    {
        float distance = transform.position.x - startX;
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
        float autoSpeed = autoRunEnabled ? runSpeed * GetSpeedMultiplier() : 0f;
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
            StartCoroutine(DoUpAttack(true));
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
        else if (allowJump && !isGrounded && !isDiveAttacking && requestedFlick == FlickDirection.Down)
        {
            DoDiveAttack();
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
            }
            newY = prevY + velocityY * dt;

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
                // エリアルコンボ改修(2026-09-11) - 着地でAerial Assistの
                // 累積使用量をリセット(次に空中へ出た時、また上限いっぱい
                // まで使えるようにする)。
                aerialAssistTimer = 0f;
                aerialAssistTotalUsed = 0f;
                onSky = true;
                bool wasDiveAttacking = isDiveAttacking;
                EndDiveAttack();
                if (wasDiveAttacking) { DiveAttackLanded?.Invoke(); TriggerDiveImpact(); }
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
                aerialAssistTimer = 0f;
                aerialAssistTotalUsed = 0f;
                onSky = false;
                bool wasDiveAttacking = isDiveAttacking;
                EndDiveAttack();
                if (wasDiveAttacking) { DiveAttackLanded?.Invoke(); TriggerDiveImpact(); }
                if (AudioManager.Instance != null) AudioManager.Instance.PlayLand();
                Landed?.Invoke();
                if (bufferedUpAttackTimer > 0f)
                {
                    bufferedUpAttackTimer = 0f;
                    FireJump();
                }
            }
        }

        transform.position = new Vector3(newX, newY, 0f);
        UpdateSlopeTilt(newX);

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
    public void TakeDamage(bool isFall = false)
    {
        if (hasDied) return;
        // A fall past failY must always respawn the player, even mid-flicker
        // from a previous hit - otherwise falling while still hit-invincible
        // silently no-ops every frame and the player free-falls forever
        // instead of ever landing back on solid ground.
        if (!isFall && hitInvincibleTimer > 0f) return;
        if (GameManager.Instance == null) return;

        // Bugfix 2026-09-06, item 2 - GameOverReason passthrough for the
        // debug log in GameManager.TryDamagePlayer (isFall is already the
        // one signal this project has to distinguish a fall death from
        // every other damage source - enemy/boss/fireball contact all call
        // TakeDamage() with isFall left at its false default).
        GameManager.DamageResult result = GameManager.Instance.TryDamagePlayer(bypassInvincibleMode: isFall, reason: isFall ? "DeathY" : "HPZero");
        if (result != GameManager.DamageResult.Hit) return;

        RespawnAtCurrentPosition();
        hitInvincibleTimer = hitInvincibleDuration;
        StartCoroutine(FlickerWhileInvincible());

        // Game Feel pass - flash/knockback/SE synchronized with this same
        // "the hit actually landed" moment, same as EnemyController's own
        // hit feedback (see section 20's "Feedbackの同期" brief).
        if (AudioManager.Instance != null) AudioManager.Instance.PlayPlayerDamage();
        if (damageFlashEnabled && sr != null) StartCoroutine(DamageFlashRoutine());
        if (damageKnockbackEnabled) ApplyKnockback(-damageKnockbackSpeed, damageKnockbackDuration);
    }

    // A brief backward push, decayed over its own duration rather than
    // fighting Move()'s own per-frame position write (see
    // knockbackVelocityX's field comment for why a raw transform.position
    // offset wouldn't survive the next frame here).
    public void ApplyKnockback(float velocityX, float duration)
    {
        knockbackVelocityX = velocityX;
        knockbackDuration = Mathf.Max(0.001f, duration);
        knockbackTimer = knockbackDuration;
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
    void RespawnAtCurrentPosition()
    {
        velocityY = 0f;
        isGrounded = true;
        jumpsUsed = 0;
        aerialAssistTimer = 0f;
        aerialAssistTotalUsed = 0f;
        lungeVelocityX = 0f;
        onSky = false;
        transform.localScale = Vector3.one;
        EndDiveAttack();

        float x = transform.position.x;
        if (TerrainManager.Instance != null)
        {
            x = TerrainManager.Instance.FindSafeRespawnX(x);
        }
        float groundY = TerrainManager.Instance != null ? (TerrainManager.Instance.GetHeightAt(x) ?? 0f) : 0f;
        transform.position = new Vector3(x, groundY + groundOffset, 0f);
    }

    IEnumerator FlickerWhileInvincible()
    {
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
            StartCoroutine(DoEscapeSuccess());
        }
    }

    // Item 2 - "3秒完了 -> 魔法陣完成 -> Playerを光で包む -> FINISH演出へ".
    // Directly reuses the original Ascension fly-up-and-off-screen visual
    // (camera-relative rise) as that "FINISH演出" - Win() is the same
    // method a Boss-clear win already called, so the Result screen's
    // existing "GAME CLEAR" headline and the full-RunMile-banking behavior
    // (see GameManager.FinishRun) both already apply correctly here too.
    IEnumerator DoEscapeSuccess()
    {
        isAscending = true;
        escapeHoldTimer = 0f;
        if (escapeRingRenderer != null) escapeRingRenderer.enabled = false;
        lungeVelocityX = 0f;
        if (attackHitbox != null) attackHitbox.enabled = false;
        if (upAttackHitbox != null) upAttackHitbox.enabled = false;
        EndDiveAttack();

        // CameraFollow freezes the camera the instant isAscending flips true
        // (above), so its current framing IS the frame the player needs to
        // rise clear of. Deriving the target from it (rather than a fixed
        // distance) means this clears the top of the screen regardless of
        // orientation or aspect ratio.
        Camera cam = Camera.main;
        float targetY = cam != null
            ? cam.transform.position.y + cam.orthographicSize + ascendClearMargin
            : transform.position.y + 20f;

        float t = 0f;
        while (transform.position.y < targetY && t < ascendMaxDuration)
        {
            t += Time.deltaTime;
            transform.position += Vector3.up * ascendRiseSpeed * Time.deltaTime;
            yield return null;
        }

        if (GameManager.Instance != null) GameManager.Instance.Win();
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
        if (sr != null) sr.enabled = false;
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

        if (explosionParticleSprite != null)
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
        AttackDirection? requested = requestedFlick switch
        {
            FlickDirection.Forward => AttackDirection.Forward,
            FlickDirection.Backward => AttackDirection.Backward,
            _ => (AttackDirection?)null
        };
        if (requested == null) return;

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
        StartCoroutine(DoUpAttack(jumpsUsed >= 2));
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
        if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(isAirborne ? 2 : 1);
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

        yield return new WaitForSeconds(upAttackActiveTime);

        if (upAttackHitbox != null) upAttackHitbox.enabled = false;
    }

    // 方向攻撃システム Ver.2、項目3 - 上昇攻撃(短いパルス)とは違い、下降
    // 攻撃は「着地するまで持続する」ため、コルーチンではなく単純にフラグ
    // /Hitboxを立てるだけ(Move()自身が毎フレームisDiveAttackingを見て
    // velocityYを上書きし続ける)。EndDiveAttack()が着地/Fall死亡/GAME
    // OVER/ESCAPE成功のいずれからも呼ばれ、後始末を一箇所に集約している。
    void DoDiveAttack()
    {
        isDiveAttacking = true;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
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
}
