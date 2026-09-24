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
    // 二丁拳銃士(2026-09-23) - "ジャンプしながら斜め上へ射撃"専用ポーズ。
    // PlayerController.IsRangedUpShootingがtrueの間だけ選ばれる、新設の
    // State.UpShot用(空なら他Stateと同様フォールバックし、従来の3キャラ
    // には一切表示されない)。
    public Sprite[] upShotFrames;
    public float runFps = 10f;
    public float jumpFps = 10f;
    public float attackFps = 12f;
    public float attackFpsSmall = 14f;
    public float attackFpsLarge = 10f;
    public float jumpStartFps = 7f;
    public float doubleJumpFps = 9f;
    public float landFps = 7f;
    public float downAttackFps = 11f;
    public float upShotFps = 12f;
    // 着地専用Frameを表示し続ける実時間(秒) - フレーム数ベースではなく
    // 固定時間(landFps同様、短い一呼吸分だけ見せてRunへ戻る)。
    public float downAttackLandDuration = 0.22f;

    [Header("Brighten overlay (emphasizes white on the dark source art)")]
    public Color brightenColor = new Color(1f, 1f, 1f, 0.15f);

    // ===== 被弾リアクション(2026-09-22) =====
    // 専用のHurt/Recovery絵(hurtFrames/recoveryFrames)があればそれを再生。無ければ既存の絵(Hurt=Runの先頭コマ、
    // Recovery=着地コマ)に姿勢変化(のけぞり/よろけ/沈み込み)を付けた簡易アニメーションで表現する。
    // 見せ方はCharacterDefinition(hurtLeanDegrees等)でキャラごとに変えられる。
    public Sprite[] hurtFrames;
    public Sprite[] recoveryFrames;
    public float hurtFps = 12f;
    public float recoveryFps = 8f;
    public float hurtLeanDegrees = 12f;
    public float hurtStaggerDistance = 0.12f;
    public float recoveryCrouchDepth = 0.14f;
    Sprite[] defaultHurtFrames, defaultRecoveryFrames;
    Transform visualT;
    Vector3 visualBasePos, visualBaseScale;
    Quaternion visualBaseRot;
    bool reactionPoseApplied;

    // ===== RUN開始準備/正常終了演出(2026-09-23) =====
    // StartPrep: 開始カウントダウン中(PlayerController.IsPreparingStart)に
    // 再生する準備ポーズ。Finish: 正常終了時(IsFinishing)の距離Tier別
    // リアクション。専用絵が無ければ、Hurt/Recoveryと同じ考え方で
    // 走りの先頭コマ+手続き的な姿勢変化(ApplyStartFinishPose)を使う。
    public Sprite[] startFrames;
    public Sprite[] finishShortFrames, finishMediumFrames, finishLongFrames, finishExtremeFrames;
    public float startFps = 6f;
    public float finishFps = 6f;
    public float startLeanDegrees = 8f;
    public float[] finishTierCrouchDepth = new float[] { 0.03f, 0.08f, 0.16f, 0.30f };
    // カウントダウン実時間(3・2・1=0.8秒×3=2.4秒)に合わせた既定値 - 準備
    // ポーズがGO!までに自然に「走り出す構え」へ到達するよう調整する。
    public float startPrepPoseDuration = 2.2f;
    // Start/Finish自然化(2026-09-24) - 実イラスト(各State2コマ)表示中の
    // 2段階ホールドの配分。進行度pがこの値未満はコマ0(準備中)を保持し、
    // 以降はコマ1(構え/結果)へ切り替える。Startは「カウントダウン終盤で
    // まもなくGOという予兆」、Finishは「Tierの余韻の大半はまだ息を整えて
    // いる最中、終盤で結果の姿勢に落ち着く」という狙いの数値(Update()参照)。
    public float startFrame0HoldFraction = 0.75f;
    public float finishFrame0HoldFraction = 0.6f;
    // StartPrep/Finishを抜ける瞬間、基準Transformへ瞬時にリセットせず
    // 短時間でLerpする(ApplyStartFinishPose参照)。
    public float poseExitDuration = 0.15f;
    Sprite[] defaultStartFrames, defaultFinishShortFrames, defaultFinishMediumFrames, defaultFinishLongFrames, defaultFinishExtremeFrames;
    float startPrepElapsed;
    bool startFinishPoseApplied;
    float poseExitElapsed = -1f; // -1 = 抜けるアニメーション中でない
    Vector3 poseExitFromPos;
    Quaternion poseExitFromRot;
    Vector3 poseExitFromScale;

    enum State { Run, JumpStart, Jump, DoubleJump, Landing, Attack, DownAttack, DownAttackLand, UpShot, Hurt, Recovery, StartPrep, Finish }

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
    // 双剣士専用アニメ追加(2026-09-13深夜) - canUseDownAttack=trueの
    // キャラは黒剣士のdownAttackFrames/downAttackLandFramesをそのまま
    // 表示してしまっていた(この2つには元々per-character上書き経路が
    // 無かった)。他のStateと同じスナップショット/上書きパターンを追加。
    Sprite[] defaultDownAttackFrames, defaultDownAttackLandFrames;
    // 二丁拳銃士(2026-09-23) - upShotFramesも他Stateと同じスナップショット
    // /上書きパターン(既存3キャラはこのフィールドを持たないため常に空の
    // まま=State.UpShotはそもそも選ばれない、Update()参照)。
    Sprite[] defaultUpShotFrames;
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
        visualT = sr.transform;
        visualBasePos = visualT.localPosition;
        visualBaseRot = visualT.localRotation;
        visualBaseScale = visualT.localScale;

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
            defaultDownAttackFrames = downAttackFrames;
            defaultDownAttackLandFrames = downAttackLandFrames;
            defaultUpShotFrames = upShotFrames;
            defaultHurtFrames = hurtFrames;
            defaultRecoveryFrames = recoveryFrames;
            defaultStartFrames = startFrames;
            defaultFinishShortFrames = finishShortFrames;
            defaultFinishMediumFrames = finishMediumFrames;
            defaultFinishLongFrames = finishLongFrames;
            defaultFinishExtremeFrames = finishExtremeFrames;
        }
        if (def == null) return;

        hurtFrames = HasFrames(def.hurtFrames) ? def.hurtFrames : defaultHurtFrames;
        recoveryFrames = HasFrames(def.recoveryFrames) ? def.recoveryFrames : defaultRecoveryFrames;
        hurtLeanDegrees = def.hurtLeanDegrees;
        hurtStaggerDistance = def.hurtStaggerDistance;
        recoveryCrouchDepth = def.recoveryCrouchDepth;

        startFrames = HasFrames(def.startFrames) ? def.startFrames : defaultStartFrames;
        startFps = def.startFps > 0f ? def.startFps : 6f;
        startLeanDegrees = def.startLeanDegrees;
        finishShortFrames = HasFrames(def.finishShortFrames) ? def.finishShortFrames : defaultFinishShortFrames;
        finishMediumFrames = HasFrames(def.finishMediumFrames) ? def.finishMediumFrames : defaultFinishMediumFrames;
        finishLongFrames = HasFrames(def.finishLongFrames) ? def.finishLongFrames : defaultFinishLongFrames;
        finishExtremeFrames = HasFrames(def.finishExtremeFrames) ? def.finishExtremeFrames : defaultFinishExtremeFrames;
        finishFps = def.finishFps > 0f ? def.finishFps : 6f;
        if (def.finishTierCrouchDepth != null && def.finishTierCrouchDepth.Length == 4) finishTierCrouchDepth = def.finishTierCrouchDepth;

        runFrames = HasFrames(def.runFrames) ? def.runFrames : defaultRunFrames;
        runFps = def.runFps > 0f ? def.runFps : defaultRunFps;
        jumpStartFrames = HasFrames(def.jumpStartFrames) ? def.jumpStartFrames : defaultJumpStartFrames;
        jumpFrames = HasFrames(def.jumpFrames) ? def.jumpFrames : defaultJumpFrames;
        doubleJumpFrames = HasFrames(def.doubleJumpFrames) ? def.doubleJumpFrames : defaultDoubleJumpFrames;
        landFrames = HasFrames(def.landFrames) ? def.landFrames : defaultLandFrames;
        downAttackFrames = HasFrames(def.downAttackFrames) ? def.downAttackFrames : defaultDownAttackFrames;
        downAttackLandFrames = HasFrames(def.downAttackLandFrames) ? def.downAttackLandFrames : defaultDownAttackLandFrames;
        upShotFrames = HasFrames(def.upShotFrames) ? def.upShotFrames : defaultUpShotFrames;
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

    // 専用絵が無い間の代用: Hurtは走りの先頭コマ(=通常姿勢)、Recoveryは着地コマがあればその最後、無ければ走りの先頭コマ。
    readonly Sprite[] fallbackOne = new Sprite[1];
    Sprite[] FallbackReactionFrames(bool recovery)
    {
        Sprite s = recovery && HasFrames(landFrames) ? landFrames[landFrames.Length - 1] : (HasFrames(runFrames) ? runFrames[0] : null);
        if (s == null) return null;
        fallbackOne[0] = s;
        return fallbackOne;
    }

    // StartPrep/Finish専用絵が無い間の代用: 走りの先頭コマを保持し、
    // ApplyStartFinishPoseの手続き的な姿勢変化だけで見せる。
    Sprite[] FallbackSingleFrame()
    {
        if (!HasFrames(runFrames)) return null;
        fallbackOne[0] = runFrames[0];
        return fallbackOne;
    }

    Sprite[] GetFinishFrames(int tier) => tier switch
    {
        0 => finishShortFrames,
        1 => finishMediumFrames,
        2 => finishLongFrames,
        _ => finishExtremeFrames,
    };

    // 簡易の姿勢アニメーション(Visualの子Transformに、リアクションの進行度に応じた回転/位置/縦つぶれを乗せる)。
    // Hurt: 1コマ目=のけぞる → 2コマ目=後方へよろける → 3コマ目=姿勢を戻し始める、を進行度0〜1で表現。
    // Recovery: 着地して沈み込み、ゆっくり立ち上がる。リアクション以外の時は元の姿勢へ戻す(他のStateの見た目は変えない)。
    void ApplyReactionPose()
    {
        if (visualT == null) return;
        bool hurt = state == State.Hurt, recov = state == State.Recovery;
        if (!hurt && !recov)
        {
            if (reactionPoseApplied)
            {
                visualT.localPosition = visualBasePos; visualT.localRotation = visualBaseRot; visualT.localScale = visualBaseScale;
                reactionPoseApplied = false;
            }
            return;
        }
        float p = controller != null ? controller.ReactionProgress : 1f;
        Vector3 pos = visualBasePos; Quaternion rot = visualBaseRot; Vector3 scl = visualBaseScale;
        if (hurt)
        {
            // 0〜0.3: 素早くのけぞる / 0.3〜0.65: 後ろへよろける / 0.65〜1: 戻し始める
            float lean = p < 0.3f ? Mathf.SmoothStep(0f, 1f, p / 0.3f)
                       : p < 0.65f ? 1f - 0.15f * ((p - 0.3f) / 0.35f)
                       : 0.85f * (1f - Mathf.SmoothStep(0f, 1f, (p - 0.65f) / 0.35f));
            float stagger = Mathf.Sin(Mathf.Clamp01(p) * Mathf.PI);
            rot = visualBaseRot * Quaternion.Euler(0f, 0f, hurtLeanDegrees * lean); // 右向きの体は+Zで後ろへ倒れる
            pos += new Vector3(-hurtStaggerDistance * stagger, 0f, 0f);
            scl = new Vector3(visualBaseScale.x, visualBaseScale.y * (1f - 0.05f * lean), visualBaseScale.z);
        }
        else
        {
            float crouch = Mathf.Sin(Mathf.Clamp01(p) * Mathf.PI * 0.85f); // 沈んで、ゆっくり戻る
            float s = 1f - recoveryCrouchDepth * crouch;
            scl = new Vector3(visualBaseScale.x * (1f + recoveryCrouchDepth * 0.4f * crouch), visualBaseScale.y * s, visualBaseScale.z);
            // 足元が浮かないよう、スプライトの下端を固定するぶん位置を下げる(pivotが中央でも足元でも成立)。
            if (sr != null && sr.sprite != null) pos += new Vector3(0f, sr.sprite.bounds.min.y * (1f - s) * visualBaseScale.y, 0f);
        }
        visualT.localPosition = pos; visualT.localRotation = rot; visualT.localScale = scl;
        reactionPoseApplied = true;
    }

    // RUN開始準備/正常終了演出(2026-09-23、2026-09-24自然化改修) -
    // ApplyReactionPoseと同じ手法(Visualの子Transformへ進行度に応じた
    // 回転/位置/縦つぶれを乗せる)を使うが、これは専用絵(startFrames/
    // finishXxxFrames)が無い間だけの代用に限定する。実イラスト表示中は
    // そのポーズ絵自体が進行度を運んでいるため、この手続き的な傾き/つぶれ
    // を重ねて適用しない(以前はここが無条件に効いており、今回追加した
    // 実イラストの上から余計な回転/スケール変形が二重に掛かっていた -
    // マスター報告の「見た目がうまく機能していない」の主因だった)。
    void ApplyStartFinishPose()
    {
        if (visualT == null) return;
        bool starting = state == State.StartPrep, finishing = state == State.Finish;

        if (!starting && !finishing)
        {
            if (startFinishPoseApplied)
            {
                // 瞬時リセットではなく短時間でLerpして基準Transformへ戻す
                // (抜け際の"ポン"を和らげる、2026-09-24追加)。
                poseExitFromPos = visualT.localPosition;
                poseExitFromRot = visualT.localRotation;
                poseExitFromScale = visualT.localScale;
                poseExitElapsed = 0f;
                startFinishPoseApplied = false;
            }
            if (poseExitElapsed >= 0f)
            {
                poseExitElapsed += Time.deltaTime;
                float et = Mathf.Clamp01(poseExitElapsed / Mathf.Max(0.01f, poseExitDuration));
                visualT.localPosition = Vector3.Lerp(poseExitFromPos, visualBasePos, et);
                visualT.localRotation = Quaternion.Slerp(poseExitFromRot, visualBaseRot, et);
                visualT.localScale = Vector3.Lerp(poseExitFromScale, visualBaseScale, et);
                if (et >= 1f) poseExitElapsed = -1f;
            }
            return;
        }

        // startPrepElapsedはUpdate()の2段階コマホールド計算が参照するため、
        // 実イラスト表示中かどうかに関わらず必ず進める。
        startPrepElapsed += Time.deltaTime;

        bool usingRealArt = starting
            ? HasFrames(startFrames)
            : HasFrames(GetFinishFrames(controller != null ? controller.FinishTierIndex : 0));
        if (usingRealArt)
        {
            // Visualは基準Transformのまま(何も変形しない) - ポーズの
            // 進行はコマ0→コマ1の切り替わり(Update()側)だけで表現する。
            startFinishPoseApplied = false;
            return;
        }

        float p = starting
            ? Mathf.Clamp01(startPrepElapsed / Mathf.Max(0.01f, startPrepPoseDuration))
            : (controller != null ? controller.FinishProgress : 1f);
        Vector3 pos = visualBasePos; Quaternion rot = visualBaseRot; Vector3 scl = visualBaseScale;
        if (starting)
        {
            float lean = Mathf.SmoothStep(0f, 1f, p);
            rot = visualBaseRot * Quaternion.Euler(0f, 0f, -startLeanDegrees * lean); // 前傾=-Z(右向きの体)
        }
        else
        {
            int tier = controller != null ? controller.FinishTierIndex : 0;
            float depth = finishTierCrouchDepth[Mathf.Clamp(tier, 0, finishTierCrouchDepth.Length - 1)];
            float crouch = Mathf.SmoothStep(0f, 1f, p);
            float s = 1f - depth * crouch;
            scl = new Vector3(visualBaseScale.x * (1f + depth * 0.3f * crouch), visualBaseScale.y * s, visualBaseScale.z);
            if (sr != null && sr.sprite != null) pos += new Vector3(0f, sr.sprite.bounds.min.y * (1f - s) * visualBaseScale.y, 0f);
        }
        visualT.localPosition = pos; visualT.localRotation = rot; visualT.localScale = scl;
        startFinishPoseApplied = true;
    }

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
        // 二丁拳銃士(2026-09-23) - Down Shot(空中で斜め下射撃+短時間だけ
        // 落下速度低下)もIsDiveAttackingとは別のisHoverShooting経由だが、
        // 見た目のポーズはdownAttackFrames(既存フィールド)をそのまま
        // 流用するため、ここでOR条件にするだけでよい(専用のポーズ
        // フィールドを新設していない)。
        bool diveAttacking = controller != null && (controller.IsDiveAttacking || controller.IsRangedHoverShooting) && downAttackFrames != null && downAttackFrames.Length > 0;
        bool upShooting = controller != null && controller.IsRangedUpShooting && upShotFrames != null && upShotFrames.Length > 0;

        State newState;
        // RUN開始準備/正常終了演出(2026-09-23) - 他の全Stateより最優先。
        // IsFinishing/IsPreparingStart中はHurt/Recovery等が同時に成立する
        // ことはない(ダメージはPresentationDamageLock/IsFinishingで防がれ、
        // カウントダウン中は敵/障害物自体が出現しないため)が、念のため
        // 一番上でチェックする。
        if (controller != null && controller.IsFinishing) newState = State.Finish;
        else if (controller != null && controller.IsPreparingStart) newState = State.StartPrep;
        else if (controller != null && controller.IsHurt) newState = State.Hurt;
        else if (controller != null && controller.IsRecovering) newState = State.Recovery;
        else if (attacking) newState = State.Attack;
        // 着地専用Frame(downAttackLandTimer)は通常のLandingより優先 - 下降
        // 攻撃からの着地の瞬間は両タイマーが同時にセットされうるため、
        // より具体的な方(衝撃エフェクト込みの絵)を優先して表示する。
        else if (grounded && downAttackLandTimer > 0f) newState = State.DownAttackLand;
        else if (grounded && landTimer > 0f) newState = State.Landing;
        else if (!grounded && diveAttacking) newState = State.DownAttack;
        else if (!grounded && upShooting) newState = State.UpShot;
        else if (!grounded && doubleJumpTimer > 0f) newState = State.DoubleJump;
        else if (!grounded && jumpStartTimer > 0f) newState = State.JumpStart;
        else if (!grounded) newState = State.Jump;
        else newState = State.Run;

        if (newState != state)
        {
            state = newState;
            frameIndex = 0;
            frameTimer = 0f;
            if (newState == State.StartPrep || newState == State.Finish) startPrepElapsed = 0f;
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
            State.UpShot => upShotFrames,
            State.Hurt => HasFrames(hurtFrames) ? hurtFrames : FallbackReactionFrames(false),
            State.Recovery => HasFrames(recoveryFrames) ? recoveryFrames : FallbackReactionFrames(true),
            State.StartPrep => HasFrames(startFrames) ? startFrames : FallbackSingleFrame(),
            State.Finish => HasFrames(GetFinishFrames(controller != null ? controller.FinishTierIndex : 0)) ? GetFinishFrames(controller.FinishTierIndex) : FallbackSingleFrame(),
            _ => runFrames
        };
        ApplyReactionPose();
        ApplyStartFinishPose();
        if (frames == null || frames.Length == 0) return;

        // Start/Finish自然化(2026-09-24) - 実イラスト表示中は「fps任せの
        // 一瞬切り替え+長い静止」ではなく、進行度pに応じた明示的な2段階
        // ホールド(コマ0=準備中/コマ1=構え・結果)へ切り替える。Startは
        // カウントダウン終盤でコマ1へ切り替わるため「まもなくGO」という
        // 予兆になり、FinishはTierが長いほどhold自体が長い
        // (PlayerController.finishHoldDurationByTier)ため、コマ0を見せる
        // 時間もTierに応じて自動的に伸びる(=Tier差の手がかりになる)。
        // 専用絵が無い間のフォールバック(FallbackSingleFrame、常に1枚)は
        // このぶんを通らず、従来どおり下のfps任せパスを通る(1枚しか無い
        // ため実質何も変わらない)。
        if (state == State.StartPrep && HasFrames(startFrames))
        {
            float p = Mathf.Clamp01(startPrepElapsed / Mathf.Max(0.01f, startPrepPoseDuration));
            frameIndex = p < startFrame0HoldFraction ? 0 : Mathf.Min(1, frames.Length - 1);
            sr.sprite = frames[frameIndex];
        }
        else if (state == State.Finish && HasFrames(GetFinishFrames(controller != null ? controller.FinishTierIndex : 0)))
        {
            float p = controller != null ? controller.FinishProgress : 1f;
            frameIndex = p < finishFrame0HoldFraction ? 0 : Mathf.Min(1, frames.Length - 1);
            sr.sprite = frames[frameIndex];
        }
        else
        {
            float fps = state switch
            {
                State.Attack => GetAttackFps(attackStage),
                State.Jump => jumpFps,
                State.JumpStart => jumpStartFps,
                State.DoubleJump => doubleJumpFps,
                State.Landing => landFps,
                State.DownAttack => downAttackFps,
                State.UpShot => upShotFps,
                State.Hurt => hurtFps,
                State.Recovery => recoveryFps,
                State.StartPrep => startFps,
                State.Finish => finishFps,
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
            }
            // 不具合修正(2026-09-13深夜) - 実機のDevelopment Console上で
            // 「IndexOutOfRangeException: Index was outside the bounds of the
            // array.」が走行開始直後から繰り返し出ていた根本原因。従来はこの
            // クランプ処理がframeTimerが閾値を超えた「進むタイミング」の中に
            // しかなく、frames[frameIndex]自体は毎フレーム無条件に実行されて
            // いた。ApplyCharacterAnimationSetでキャラを切り替えた際、state
            // (Run/Jump等)自体は変化しないままrunFrames等の配列だけがより短い
            // ものに差し替わるケース(例: 6コマの配列を使っていた直後に2コマの
            // 配列へ切り替わる)で、frameIndexが古い(長い)配列基準の値のまま
            // 残ってしまい、次に「進むタイミング」が来るまでの間、毎フレーム
            // frames[frameIndex]が新しい(短い)配列の範囲外を指して例外を投げて
            // いた。クランプをif文の外(毎フレーム必ず実行)へ移動し、フレーム
            // が進んだかどうかに関係なく常にその時点のframes.Lengthへ合わせて
            // 補正するよう修正。
            frameIndex = state == State.Run
                ? frameIndex % frames.Length // loop while running
                : Mathf.Min(frameIndex, frames.Length - 1); // hold last frame otherwise

            sr.sprite = frames[frameIndex];
        }

        if (brightenOverlay != null)
        {
            brightenOverlay.sprite = sr.sprite;
            brightenOverlay.sortingOrder = sr.sortingOrder + 1;
            brightenOverlay.enabled = sr.enabled;
        }
    }
}
