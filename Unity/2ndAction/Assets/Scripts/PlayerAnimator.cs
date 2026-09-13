using UnityEngine;

// NOT [RequireComponent(typeof(SpriteRenderer))] - this component lives on
// Root, but the actual SpriteRenderer lives one level down on the "Visual"
// child (see SceneBuilder.CreatePlayer). That attribute used to be correct
// back when the SpriteRenderer sat directly on this same GameObject, but
// left in place after the Root/Visual split it silently auto-added a
// second, blank SpriteRenderer onto Root the moment this component was
// added there - which Awake()'s GetComponentInChildren then found FIRST
// (before descending into Visual), so this ended up driving/animating that
// stray Root-level renderer while Visual's real one sat frozen on its
// initial sprite - two overlapping player sprites, one animating, one
// permanently stuck. Confirmed and fixed by removing this attribute.
public class PlayerAnimator : MonoBehaviour
{
    public Sprite[] runFrames;
    public Sprite[] jumpFrames;
    public Sprite[] attackFrames;
    public Sprite[] attackFramesSmall;
    public Sprite[] attackFramesLarge;
    public Sprite[] jumpStartFrames;
    public Sprite[] doubleJumpFrames;
    public Sprite[] landFrames;
    // Direction Attack System Ver.2 (2026-09-07), item 3 - dedicated "下降
    // 攻撃" body animation, held for the whole PlayerController.IsDiveAttacking
    // duration (see Update()'s own State.DownAttack branch) rather than a
    // one-shot timer like jumpStart/doubleJump above, since a dive's actual
    // duration depends on how far the player is from the ground, not a
    // fixed clip length.
    public Sprite[] downAttackFrames;
    // 上下攻撃アニメーション差し替え(2026-09-08) - 下降攻撃の「着地専用
    // Frame」(衝撃エフェクト込みの1枚絵)。downAttackFramesとは別配列にし
    // てある - downAttackFramesは通常のState切替(hold last frame)に任せ
    // ると、急降下が長引いた場合に最終フレーム(=このLand Frameと誤認され
    // かねない絵)へ自然に到達してしまい、「空中なのに着地エフェクトが
    // 出る」というマスター指摘のバグを起こしうる。Land専用の別State
    // (DownAttackLand)・別タイマー(downAttackLandTimer、jumpStartTimer等
    // と同じ「実際にそのイベントが起きた瞬間だけ発火するone-shot」方式)
    // にすることで、実際に着地した瞬間にのみ表示されるよう保証する。
    public Sprite[] downAttackLandFrames;
    public float runFps = 10f;
    public float jumpFps = 10f;
    public float attackFps = 12f;
    public float attackFpsSmall = 14f;
    public float attackFpsLarge = 10f;
    public float jumpStartFps = 7f;
    public float doubleJumpFps = 9f;
    public float landFps = 7f;
    public float downAttackFps = 11f;
    // 着地専用Frameを表示し続ける実時間(秒) - フレーム数ベースではなく
    // 固定時間(landFps同様、短い一呼吸分だけ見せてRunへ戻る)。
    public float downAttackLandDuration = 0.22f;

    [Header("Brighten overlay (emphasizes white on the dark source art)")]
    public Color brightenColor = new Color(1f, 1f, 1f, 0.15f);

    enum State { Run, JumpStart, Jump, DoubleJump, Landing, Attack, DownAttack, DownAttackLand }

    // キャラクター専用アニメーション差し替え(2026-09-13) - PlayerController.
    // baseRunSpeed等と全く同じ理由の「素のスナップショット」。SceneBuilder
    // は黒剣士のアートを一度だけ焼き込むため、初回のApplyCharacterAnimationSet
    // 呼び出し時点のフィールド値=黒剣士の素のアニメーションとして保持し、
    // 以後は常にこのスナップショット+選択中キャラクターの上書きから再計算
    // する(蓄積的に上書きしない - 一度お嬢様騎士を選んだ後に黒剣士へ戻す、
    // といった切り替えでも正しく黒剣士本来のアートへ戻る)。
    Sprite[] defaultRunFrames, defaultJumpStartFrames, defaultJumpFrames, defaultLandFrames;
    Sprite[] defaultAttackFrames, defaultAttackFramesSmall, defaultAttackFramesLarge;
    // お嬢様騎士 二段ジャンプ演出バグ修正(2026-09-13) - 他のStateと同じ
    // スナップショット/上書きパターンをdoubleJumpFramesにも適用する。
    Sprite[] defaultDoubleJumpFrames;
    // お嬢様騎士Run読みやすさ改善(2026-09-13) - runFramesと同じ「スナップ
    // ショット→上書き」パターンでrunFpsも上書きできるようにした(コマ数が
    // 黒剣士と異なるキャラのため)。
    float defaultRunFps;
    bool defaultAnimationCaptured;

    SpriteRenderer sr;
    SpriteRenderer brightenOverlay;
    PlayerController controller;
    float frameTimer;
    int frameIndex;
    State state = State.Run;

    // One-shot timers for the transient jump/land animations - set to the
    // clip's own length whenever the matching PlayerController event fires,
    // so each plays out fully once and then falls back to the ordinary
    // grounded/airborne state below (whichever timer is still running wins,
    // with double-jump taking priority over a still-running jump-start).
    float jumpStartTimer;
    float doubleJumpTimer;
    float landTimer;
    float downAttackLandTimer;

    void Awake()
    {
        // The SpriteRenderer lives on the "Visual" child, not this Root -
        // see SceneBuilder.CreatePlayer.
        sr = GetComponentInChildren<SpriteRenderer>();
        controller = GetComponent<PlayerController>();

        // Parented under the same Visual sr lives on (not this component's
        // own Root transform) so it stays exactly aligned with the main
        // sprite even if Visual's own localPosition is ever nudged for a
        // future asset.
        GameObject overlayGO = new GameObject("BrightenOverlay");
        overlayGO.transform.SetParent(sr.transform, false);
        brightenOverlay = overlayGO.AddComponent<SpriteRenderer>();
        brightenOverlay.color = brightenColor;
    }

    void OnEnable()
    {
        if (controller == null) return;
        controller.JumpStarted += OnJumpStarted;
        controller.DoubleJumped += OnDoubleJumped;
        controller.Landed += OnLanded;
        controller.DiveAttackLanded += OnDiveAttackLanded;
    }

    void OnDisable()
    {
        if (controller == null) return;
        controller.JumpStarted -= OnJumpStarted;
        controller.DoubleJumped -= OnDoubleJumped;
        controller.Landed -= OnLanded;
        controller.DiveAttackLanded -= OnDiveAttackLanded;
    }

    void OnJumpStarted()
    {
        if (jumpStartFrames != null && jumpStartFrames.Length > 0) jumpStartTimer = jumpStartFrames.Length / jumpStartFps;
    }

    void OnDoubleJumped()
    {
        if (doubleJumpFrames != null && doubleJumpFrames.Length > 0) doubleJumpTimer = doubleJumpFrames.Length / doubleJumpFps;
    }

    void OnLanded()
    {
        if (landFrames != null && landFrames.Length > 0) landTimer = landFrames.Length / landFps;
    }

    void OnDiveAttackLanded()
    {
        if (downAttackLandFrames != null && downAttackLandFrames.Length > 0) downAttackLandTimer = downAttackLandDuration;
    }

    Sprite[] GetAttackFrames(int stage)
    {
        if (stage <= 1 && attackFramesSmall != null && attackFramesSmall.Length > 0) return attackFramesSmall;
        if (stage >= 3 && attackFramesLarge != null && attackFramesLarge.Length > 0) return attackFramesLarge;
        return attackFrames;
    }

    float GetAttackFps(int stage)
    {
        if (stage <= 1) return attackFpsSmall;
        if (stage >= 3) return attackFpsLarge;
        return attackFps;
    }

    // キャラクター専用アニメーション差し替え(2026-09-13) - GameManager.
    // ApplyCharacterBaseStatsから、PlayerController.ApplyCharacterBaseStats
    // と同じタイミング(Run開始時、カード効果より前)に呼ばれる。defの
    // 該当フィールドが空なら黒剣士の素のアート(defaultXxxFrames)へ
    // フォールバックする。
    //
    // 黒剣士の既存State機構は「JumpStart=地上上攻撃の絵」「DoubleJump=
    // 空中上攻撃の絵」を兼ねている(SceneBuilder.CreatePlayerのコメント
    // 参照)が、上攻撃/空中攻撃を持たないキャラクター(canUseUpAttack/
    // canUseAirAttack=false)ではその意味が成立しない。CharacterDefinition.
    // jumpStartFramesはそういうキャラクター向けに「素のジャンプ演出」
    // として再定義したフィールドであり、黒剣士のjumpStartFrames(=上攻撃
    // の絵)とは意味が異なる点に注意 - この差し替えメソッドはあくまで
    // 「JumpStart StateでどのSpriteを表示するか」だけを差し替えており、
    // Stateそのものの発火条件(PlayerController.canUseUpAttack等)には
    // 一切関与しない。
    public void ApplyCharacterAnimationSet(CharacterDefinition def)
    {
        if (!defaultAnimationCaptured)
        {
            defaultAnimationCaptured = true;
            defaultRunFrames = runFrames;
            defaultJumpStartFrames = jumpStartFrames;
            defaultJumpFrames = jumpFrames;
            defaultLandFrames = landFrames;
            defaultAttackFrames = attackFrames;
            defaultAttackFramesSmall = attackFramesSmall;
            defaultAttackFramesLarge = attackFramesLarge;
            defaultRunFps = runFps;
            defaultDoubleJumpFrames = doubleJumpFrames;
        }
        if (def == null) return;

        runFrames = HasFrames(def.runFrames) ? def.runFrames : defaultRunFrames;
        runFps = def.runFps > 0f ? def.runFps : defaultRunFps;
        jumpStartFrames = HasFrames(def.jumpStartFrames) ? def.jumpStartFrames : defaultJumpStartFrames;
        jumpFrames = HasFrames(def.jumpFrames) ? def.jumpFrames : defaultJumpFrames;
        doubleJumpFrames = HasFrames(def.doubleJumpFrames) ? def.doubleJumpFrames : defaultDoubleJumpFrames;
        landFrames = HasFrames(def.landFrames) ? def.landFrames : defaultLandFrames;
        if (HasFrames(def.attackFrames))
        {
            attackFrames = def.attackFrames;
            // 3人目の主人公追加(2026-09-13、双剣士) - このキャラが専用の
            // Small/Largeを持っていればそれを使う(5段コンボを3段階アート
            // で表現、GetAttackFramesの既存フォールバックがstageに応じて
            // 選択する)。持っていなければ(=お嬢様騎士のような1段攻撃
            // 専用キャラ)nullのままにして、黒剣士のSmall/Largeが紛れ込む
            // ことを防ぐ(このキャラは常にstage<=1で止まるため実害は無い
            // が、意味的に正しい状態を保つ)。
            attackFramesSmall = HasFrames(def.attackFramesSmall) ? def.attackFramesSmall : null;
            attackFramesLarge = HasFrames(def.attackFramesLarge) ? def.attackFramesLarge : null;
        }
        else
        {
            attackFrames = defaultAttackFrames;
            attackFramesSmall = defaultAttackFramesSmall;
            attackFramesLarge = defaultAttackFramesLarge;
        }
    }

    static bool HasFrames(Sprite[] frames) => frames != null && frames.Length > 0;

    void Update()
    {
        float dt = Time.deltaTime;
        if (jumpStartTimer > 0f) jumpStartTimer -= dt;
        if (doubleJumpTimer > 0f) doubleJumpTimer -= dt;
        if (landTimer > 0f) landTimer -= dt;
        if (downAttackLandTimer > 0f) downAttackLandTimer -= dt;

        bool attacking = controller != null && controller.IsAttacking && attackFrames != null && attackFrames.Length > 0;
        bool grounded = controller == null || controller.IsGrounded;
        int attackStage = controller != null ? controller.CurrentAttackStage : 2;
        // Direction Attack System Ver.2, item 3 - checked below Landing (so
        // touching ground always overrides it the instant it happens, same
        // priority slot JumpStart/DoubleJump already use) but above the
        // plain Jump fallback, so a dive-attack always shows its own pose
        // rather than the generic falling loop.
        bool diveAttacking = controller != null && controller.IsDiveAttacking && downAttackFrames != null && downAttackFrames.Length > 0;

        State newState;
        if (attacking) newState = State.Attack;
        // 着地専用Frame(downAttackLandTimer)は通常のLandingより優先 - 下降
        // 攻撃からの着地の瞬間は両タイマーが同時にセットされうるため、
        // より具体的な方(衝撃エフェクト込みの絵)を優先して表示する。
        else if (grounded && downAttackLandTimer > 0f) newState = State.DownAttackLand;
        else if (grounded && landTimer > 0f) newState = State.Landing;
        else if (!grounded && diveAttacking) newState = State.DownAttack;
        else if (!grounded && doubleJumpTimer > 0f) newState = State.DoubleJump;
        else if (!grounded && jumpStartTimer > 0f) newState = State.JumpStart;
        else if (!grounded) newState = State.Jump;
        else newState = State.Run;

        if (newState != state)
        {
            state = newState;
            frameIndex = 0;
            frameTimer = 0f;
        }

        Sprite[] frames = state switch
        {
            State.Attack => GetAttackFrames(attackStage),
            State.Jump => jumpFrames,
            State.JumpStart => jumpStartFrames,
            State.DoubleJump => doubleJumpFrames,
            State.Landing => landFrames,
            State.DownAttack => downAttackFrames,
            State.DownAttackLand => downAttackLandFrames,
            _ => runFrames
        };
        if (frames == null || frames.Length == 0) return;

        float fps = state switch
        {
            State.Attack => GetAttackFps(attackStage),
            State.Jump => jumpFps,
            State.JumpStart => jumpStartFps,
            State.DoubleJump => doubleJumpFps,
            State.Landing => landFps,
            State.DownAttack => downAttackFps,
            // 1フレームだけの絵をdownAttackLandDuration秒キープするだけな
            // ので、fps自体は「Duration中に次のフレームへ進まない」程度に
            // 低ければ何でもよい(frames.Length==1なら実質参照されない)。
            State.DownAttackLand => Mathf.Max(1f, downAttackLandFrames != null ? downAttackLandFrames.Length / Mathf.Max(0.01f, downAttackLandDuration) : 1f),
            _ => runFps
        };

        frameTimer += dt;
        if (frameTimer >= 1f / fps)
        {
            frameTimer = 0f;
            frameIndex++;
            frameIndex = state == State.Run
                ? frameIndex % frames.Length // loop while running
                : Mathf.Min(frameIndex, frames.Length - 1); // hold last frame otherwise
        }

        sr.sprite = frames[frameIndex];

        if (brightenOverlay != null)
        {
            brightenOverlay.sprite = sr.sprite;
            brightenOverlay.sortingOrder = sr.sortingOrder + 1;
            brightenOverlay.enabled = sr.enabled;
        }
    }
}
