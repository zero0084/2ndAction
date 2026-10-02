using System.Collections;
using UnityEngine;

// ===== 竜騎士(2026-09-26) ===== //
// 5人目の主人公「巨大ランスの竜騎士」専用の4方向攻撃。CharacterDefinition.isLancer
// がtrueの間だけDoAttack/FireJump/HandleAttackInputからここへ分岐する。専用の
// 細長いHitbox(LanceHitbox)を実行時に1つだけ作って使い回し、既存の攻撃Hitbox/
// Slash VFX/コンボ処理(他4キャラの経路)には一切触れない。
//
//  前: 3段突き Quick Thrust → Step Thrust → Dragon Pierce(入力を続けると次の段、
//      一定時間入力が無ければ1段目へ)。どの段も細長く非常に長い前方判定・複数貫通。
//      懐(体に重なる距離)には判定が無い。3段目は最大威力/KB/HitStopの代わりに隙が大きい。
//  後: 石突きで後ろを小突く。短く・弱く・ノックバック弱め。
//  上: 通常ジャンプ+斜め上への突き上げ(打ち上げ/吸い込みは無し)。
//  下: 地上 = 前方下への突き / 空中 = 急降下突き(真下へ高速落下→突き刺して着地→衝撃波)。
//
// 状態管理(2026-09-26 第2弾、「上攻撃→横攻撃で停止」修正):
//  どの技も開始時にBeginLanceMove()で「前の技の状態を全部片付けてから」自分の状態を立て、
//  終了はEndLanceMove(token)に一本化する。後から始まった技(上攻撃のジャンプ等)に割り込ま
//  れた古い技はtokenが古いので何も触らずに抜ける(=新しい技の状態を壊さない)。以前は上攻撃が
//  前/下攻撃の世代だけを進めていたため、割り込まれた側がisAttacking=trueのまま終了処理を
//  飛ばして抜け、以後ずっと攻撃ポーズで固まり攻撃もできなくなっていた。
//  さらに、isAttackingを持つ技が実行中でないのにisAttackingだけ残る状態は毎フレーム
//  (LancerSafetyUpdate)と着地時に必ず解除する。被弾/死亡/復帰はCancelLanceMoves()。
public partial class PlayerController
{
    bool isLancerCharacter;
    CharacterDefinition lancerDef;
    BoxCollider2D lanceHitbox;
    PlayerAttackInfo lanceAttackInfo;
    int lanceGeneration;
    // 攻撃ごとの一時倍率(EffectiveAttackPower/KnockbackPowerMultiplierに竜騎士の
    // 間だけ掛かる。他キャラは常に1のまま)。
    float lanceDamageScale = 1f;
    float lanceKnockbackScale = 1f;
    // 命中した敵に掛けるHitStop(0=敵側の既定値のまま)。EnemyController.ProcessHitが参照。
    float lanceHitStop;
    public float AttackHitStopOverride => isLancerCharacter ? lanceHitStop : 0f;

    public enum LanceMoveKind { None, Forward, Backward, Down, Up, Dive }
    public LanceMoveKind LanceMove { get; private set; }
    // 0=構え/1=突き(落下)/2=突き刺し着地(PlayerAnimatorが攻撃フェーズに合わせてコマを選ぶ)。
    public int LanceFrameIndex { get; private set; }
    public bool IsLancer => isLancerCharacter;
    public BoxCollider2D LanceHitbox => lanceHitbox;
    // 前突きの現在の段(1〜3、突き中以外は0)。
    public int LanceComboStage { get; private set; }

    // isAttackingを立てている竜騎士の技が実行中か(Up突きは立てない)。
    bool lanceOwnsAttack;
    // 前突きの次の段と、その受付時間(突き終わってから)。
    int lanceNextStage = 1;
    float lanceComboGraceTimer;
    // 急降下突き
    bool lanceDiving;       // 構え〜落下中(Move()が縦速度/前進速度を上書きする)
    bool lanceDiveFalling;  // 落下中(構え中はfalse)
    bool lanceDiveLanded;   // 着地した(Move()の着地処理が立てる)
    float lanceMoveSlowFactor = 1f; // 技の最中の前進速度倍率(急降下/突き刺し中)
    public bool IsLanceDiving => lanceDiving;

    void ApplyLancerStats(CharacterDefinition def)
    {
        isLancerCharacter = def.isLancer;
        lancerDef = def.isLancer ? def : null;
        CancelLanceMoves();
        if (isLancerCharacter && lanceHitbox == null)
        {
            var go = new GameObject("LanceHitbox");
            go.transform.SetParent(transform, false);
            go.tag = "PlayerAttack";
            lanceAttackInfo = go.AddComponent<PlayerAttackInfo>();
            lanceAttackInfo.kind = PlayerAttackKind.Normal;
            lanceHitbox = go.AddComponent<BoxCollider2D>();
            lanceHitbox.isTrigger = true;
            lanceHitbox.enabled = false;
            go.AddComponent<ColliderDebugView>().color = new Color(0.3f, 1f, 0.85f);
        }
    }

    // 走行速度倍率が1を超えた分だけ突進/ノックバックを強める(将来の速度連動の入口)。
    float LanceSpeedFactor()
    {
        if (lancerDef == null) return 1f;
        float over = Mathf.Max(0f, GetSpeedMultiplier() - 1f);
        return 1f + Mathf.Min(lancerDef.lanceSpeedBonusMax, over * lancerDef.lanceSpeedBonusPerSpeed);
    }

    static float Pick(float[] a, int stage, float fallback)
    {
        if (a == null || a.Length == 0) return fallback;
        return a[Mathf.Clamp(stage - 1, 0, a.Length - 1)];
    }

    // ===================== 状態の開始/終了(一本化) ===================== //

    // 新しい技を始める: 前の技(実行中なら)の状態を全て片付け、世代を進めて新しいtokenを返す。
    int BeginLanceMove(LanceMoveKind kind, bool ownsAttack)
    {
        ResetLanceState();
        int token = ++lanceGeneration;
        LanceMove = kind;
        LanceFrameIndex = 0;
        lanceOwnsAttack = ownsAttack;
        if (ownsAttack)
        {
            isAttacking = true;
            comboWindowOpen = false;
            comboBuffered = false;
        }
        return token;
    }

    // 技の正常終了/途中終了(どちらでも呼ぶ)。tokenが最新の時だけ片付ける
    // (新しい技に割り込まれた古い技は、新しい技の状態に触らない)。
    void EndLanceMove(int token)
    {
        if (token != lanceGeneration) return;
        ResetLanceState();
    }

    // 竜騎士の技が立てうる全ての一時状態を既定へ戻す。
    void ResetLanceState()
    {
        if (lanceOwnsAttack)
        {
            isAttacking = false;
            comboWindowOpen = false;
            lanceOwnsAttack = false;
        }
        if (lanceHitbox != null) lanceHitbox.enabled = false;
        if (lanceAttackInfo != null) lanceAttackInfo.kind = PlayerAttackKind.Normal;
        lungeVelocityX = 0f;
        lanceDiving = false;
        lanceDiveFalling = false;
        lanceDiveLanded = false;
        lanceMoveSlowFactor = 1f;
        LanceMove = LanceMoveKind.None;
        LanceFrameIndex = 0;
        LanceComboStage = 0;
        lanceDamageScale = 1f;
        lanceKnockbackScale = 1f;
        lanceHitStop = 0f;
    }

    // 被弾/死亡/復帰/キャラ切り替え: 実行中の技を全て打ち切る。
    void CancelLanceMoves()
    {
        lanceGeneration++;
        ResetLanceState();
        lanceNextStage = 1;
        lanceComboGraceTimer = 0f;
        if (lanceFxRoutine != null) { StopCoroutine(lanceFxRoutine); lanceFxRoutine = null; }
        if (lanceFxRenderer != null) lanceFxRenderer.enabled = false;
        if (lanceDiveTrail != null) lanceDiveTrail.enabled = false;
    }

    // 毎フレーム(Update)の安全装置: isAttackingを持つ技が無いのにisAttackingが残っていたら解除する。
    // 竜騎士以外は何もしない。
    void LancerSafetyUpdate()
    {
        if (!isLancerCharacter) return;
        if (lanceComboGraceTimer > 0f)
        {
            lanceComboGraceTimer -= Time.deltaTime;
            if (lanceComboGraceTimer <= 0f && !isAttacking) lanceNextStage = 1; // 入力が無ければ1段目へ戻る
        }
        if (isAttacking && !lanceOwnsAttack)
        {
            isAttacking = false;
            comboWindowOpen = false;
            comboBuffered = false;
            lungeVelocityX = 0f;
        }
        if (!lanceOwnsAttack && LanceMove != LanceMoveKind.Up && LanceMove != LanceMoveKind.None && lanceHitbox != null && !lanceHitbox.enabled)
        {
            LanceMove = LanceMoveKind.None;
        }
    }

    // 着地の瞬間(Move()の着地処理から)。急降下中なら突き刺し着地へ、そうでなければ
    // 技の実行中でないのに残った状態だけを片付ける(正常な技/コンボは壊さない)。
    void OnLancerLanded()
    {
        if (!isLancerCharacter) return;
        if (lanceDiving) { lanceDiveLanded = true; return; }
        if (!lanceOwnsAttack)
        {
            if (isAttacking) { isAttacking = false; comboWindowOpen = false; comboBuffered = false; }
            lungeVelocityX = 0f;
            lanceMoveSlowFactor = 1f;
        }
    }

    // ===================== 見た目(突きの衝撃波) ===================== //

    // 突きの衝撃波(Resources/Effects/lancethrust、PPU512の正方形・中心ピボット・右向き)。
    // 絵のランスの穂先より先は、判定の終端までこの衝撃波で「届いている」ことを見せる。
    static Sprite lanceThrustArt;
    static bool lanceThrustLoaded;
    SpriteRenderer lanceFxRenderer;
    Coroutine lanceFxRoutine;
    // 絵の穂先の位置(判定の向きに沿った距離) - 衝撃波はここから判定の終端まで伸ばす。
    public float lanceArtTipDistance = 0.95f;
    static readonly Color LanceFxJade = new Color(0.55f, 1f, 0.86f, 1f);

    static Sprite LanceThrustArt()
    {
        if (!lanceThrustLoaded) { lanceThrustLoaded = true; lanceThrustArt = Resources.Load<Sprite>("Effects/lancethrust"); }
        return lanceThrustArt;
    }

    void ShowLanceThrustFx(Vector2 origin, Vector2 dir, float deg, float from, float to, float duration, float thickness = 1f, Color? tint = null)
    {
        if (LanceThrustArt() == null || to <= from) return;
        if (lanceFxRenderer == null)
        {
            var go = new GameObject("LanceThrustFx");
            go.transform.SetParent(transform, false);
            lanceFxRenderer = go.AddComponent<SpriteRenderer>();
            lanceFxRenderer.sprite = lanceThrustArt;
            lanceFxRenderer.sortingOrder = RenderOrder.SlashFx;
        }
        var t = lanceFxRenderer.transform;
        Vector2 c = origin + dir * ((from + to) * 0.5f);
        t.localPosition = new Vector3(c.x, c.y, 0f);
        t.localRotation = Quaternion.Euler(0f, 0f, deg);
        if (lanceFxRoutine != null) StopCoroutine(lanceFxRoutine);
        lanceFxRoutine = StartCoroutine(LanceThrustFxRoutine(to - from, duration, thickness, tint ?? Color.white));
    }

    IEnumerator LanceThrustFxRoutine(float length, float duration, float thickness, Color tint)
    {
        var t = lanceFxRenderer.transform;
        lanceFxRenderer.enabled = true;
        float total = duration + 0.12f, e = 0f;
        while (e < total)
        {
            // 最初の0.06秒で前へ伸び切り、判定が消えた後0.12秒でフェードアウト。
            float grow = Mathf.Clamp01(e / 0.06f);
            float fade = e <= duration ? 1f : 1f - (e - duration) / 0.12f;
            t.localScale = new Vector3(length * Mathf.Lerp(0.55f, 1f, grow), length * thickness, 1f);
            lanceFxRenderer.color = new Color(tint.r, tint.g, tint.b, fade);
            e += Time.deltaTime;
            yield return null;
        }
        lanceFxRenderer.enabled = false;
        lanceFxRoutine = null;
    }

    // 判定を「開始位置startX〜長さlength、中心高さy、太さthick、角度deg」の帯に設定して有効化する。
    // いったん無効化してから有効化し直すので、前の攻撃で重なっていた敵にも改めて当たる。
    // fxDurationが正なら、絵の穂先から判定の終端まで突きの衝撃波を出す。
    void ArmLanceHitbox(float startX, float length, float y, float thick, float deg, float fxDuration = 0f, float fxThickness = 1f, Color? fxTint = null)
    {
        if (lanceHitbox == null) return;
        lanceHitbox.enabled = false;
        var t = lanceHitbox.transform;
        Vector2 dir = new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));
        Vector2 origin = new Vector2(0f, y);
        Vector2 center = origin + dir * (startX + length * 0.5f);
        t.localPosition = new Vector3(center.x, center.y, 0f);
        t.localRotation = Quaternion.Euler(0f, 0f, deg);
        // 大きさはTransformのScaleで持つ(ColliderDebugViewは生成時のsizeで枠を描くため)。
        t.localScale = new Vector3(length, thick, 1f);
        lanceHitbox.size = Vector2.one;
        lanceHitbox.offset = Vector2.zero;
        lanceHitbox.enabled = true;
        { var li = lanceHitbox.GetComponent<PlayerAttackInfo>(); if (li != null) li.Rearm(); }
        if (fxDuration > 0f) ShowLanceThrustFx(origin, dir, deg, lanceArtTipDistance, startX + length, fxDuration, fxThickness, fxTint);
    }

    void DisarmLanceHitbox(int token)
    {
        if (token != lanceGeneration) return;
        if (lanceHitbox != null) lanceHitbox.enabled = false;
    }

    // ===================== 入力 ===================== //

    // 前/後の入力(HandleAttackInputから)。突きの最中の入力は1つだけ予約し、突き終わりで次の段へ。
    void HandleLanceHorizontalInput(AttackDirection dir)
    {
        if (lanceDiving) return;
        if (isAttacking && lanceOwnsAttack)
        {
            if (!comboBuffered && (LanceMove == LanceMoveKind.Forward || LanceMove == LanceMoveKind.Backward))
            {
                comboBuffered = true;
                bufferedDirection = dir;
            }
            return;
        }
        if (isAttacking) return;
        // 受付時間内の入力は次の段として即座に出す(硬直明けの待ち時間を挟まない)。
        if (attackCooldownTimer > 0f && lanceComboGraceTimer <= 0f) return;
        StartCoroutine(DoAttack(dir));
    }

    // 前/後攻撃(DoAttackから分岐)。
    IEnumerator DoLanceHorizontal(AttackDirection dir)
    {
        var d = lancerDef;
        bool back = dir == AttackDirection.Backward;
        int stage = back ? 0 : (lanceComboGraceTimer > 0f ? Mathf.Clamp(lanceNextStage, 1, 3) : 1);
        lanceComboGraceTimer = 0f;
        int gen = attackGeneration;
        int token = BeginLanceMove(back ? LanceMoveKind.Backward : LanceMoveKind.Forward, true);
        try
        {
            comboCount = Mathf.Max(1, stage);
            LanceComboStage = stage;
            transform.localScale = Vector3.one; // 後ろ攻撃でも向きは変えない(背中側を石突きで突く)
            float speedFactor = back ? 1f : LanceSpeedFactor();
            lanceDamageScale = back ? d.lanceBackDamageScale : Pick(d.lanceComboDamageScale, stage, 1f);
            lanceKnockbackScale = back ? d.lanceBackKnockbackScale : speedFactor * Pick(d.lanceComboKnockbackScale, stage, 1f);

            float s = AttackSpeedMultiplier;
            float windup = (back ? d.lanceWindup : Pick(d.lanceComboWindup, stage, d.lanceWindup)) * s;
            float active = (back ? d.lanceThrustActive : Pick(d.lanceComboActive, stage, d.lanceThrustActive)) * s;
            float recovery = (back ? d.lanceRecovery : Pick(d.lanceComboRecovery, stage, d.lanceRecovery)) * s;
            attackCooldownTimer = (windup + active + recovery) + attackCooldown * s;

            // 構え
            float t = 0f;
            while (t < windup)
            {
                if (gen != attackGeneration || token != lanceGeneration) yield break;
                t += Time.deltaTime; yield return null;
            }

            // 突き
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(back ? 1 : Mathf.Clamp(stage, 1, 3));
            LanceFrameIndex = 1;
            if (back)
            {
                ArmLanceHitbox(0.3f, d.lanceBackReach * AttackRangeMultiplier, d.lanceHeight, d.lanceThickness, 180f);
                lungeVelocityX = -kitBackStep / Mathf.Max(0.01f, active); // 2026-09-30: 後ろ攻撃で後退(以前は移動なし)
            }
            else
            {
                float reach = d.lanceReach * Pick(d.lanceComboReachScale, stage, 1f) * AttackRangeMultiplier;
                lanceHitStop = Pick(d.lanceComboHitStop, stage, 0f);
                Color tint = stage >= 3 ? LanceFxJade : Color.Lerp(Color.white, LanceFxJade, 0.35f * (stage - 1));
                ArmLanceHitbox(d.lanceReachStart, reach, d.lanceHeight, d.lanceThickness, 0f, active, Pick(d.lanceComboFxThickness, stage, 1f), tint);
                lungeVelocityX = d.lanceDashDistance * Pick(d.lanceComboDashScale, stage, 1f) * speedFactor / Mathf.Max(0.01f, active);
                if (stage >= 3) SpawnDragonPierceWake(reach);
            }
            t = 0f;
            while (t < active)
            {
                if (gen != attackGeneration || token != lanceGeneration) yield break;
                t += Time.deltaTime; yield return null;
            }
            lungeVelocityX = 0f;
            DisarmLanceHitbox(token);

            // 硬直(1・2段目は後半で次の段へつなげられる。3段目は最後まで硬直)
            t = 0f;
            float chainPoint = stage >= 3 || back ? 1f : 0.45f;
            while (t < recovery)
            {
                if (gen != attackGeneration || token != lanceGeneration) yield break;
                if (comboBuffered && t >= recovery * chainPoint) break;
                t += Time.deltaTime; yield return null;
            }

            // 次の段の受付(3段目・後ろ攻撃の後は1段目から)
            lanceNextStage = back || stage >= 3 ? 1 : stage + 1;
            lanceComboGraceTimer = d.lanceComboGrace;
            bool chain = comboBuffered;
            AttackDirection next = bufferedDirection;
            EndLanceMove(token);
            if (chain && gen == attackGeneration)
            {
                comboBuffered = false;
                attackCooldownTimer = 0f;
                StartCoroutine(DoAttack(next));
            }
        }
        finally
        {
            EndLanceMove(token); // 途中で抜けた場合(被弾/別の技)も必ず片付く(最新の技の時だけ)
        }
    }

    // 3段目 Dragon Pierce: 槍先の先へ細長い風圧が走る(判定は突きの帯と同じ)。
    void SpawnDragonPierceWake(float reach)
    {
        Sprite art = LanceThrustArt();
        if (art == null) return;
        Vector3 basePos = transform.position + new Vector3(0f, lancerDef.lanceHeight, 0f);
        for (int i = 0; i < 3; i++)
        {
            float x = lancerDef.lanceReachStart + reach * (0.45f + 0.22f * i);
            float yOff = (i - 1) * 0.16f;
            OneShotSpriteEffect.CreateTweened(art, basePos + new Vector3(x, yOff, 0f), new Color(LanceFxJade.r, LanceFxJade.g, LanceFxJade.b, 0.75f),
                duration: 0.22f + 0.05f * i, startScale: 0.5f, endScale: 1.1f, drift: new Vector3(6f + 2f * i, 0f, 0f), sortingOrder: RenderOrder.SlashFx);
        }
        var camFollow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (camFollow != null) camFollow.Shake(0.06f, 0.12f);
    }

    // 上攻撃(FireJumpから分岐、ジャンプ自体はFireJumpが既に発動済み)。
    // isAttackingは立てない(ジャンプ中に前/後攻撃を出すのは正常な操作)。
    IEnumerator DoLanceUpThrust()
    {
        var d = lancerDef;
        int token = BeginLanceMove(LanceMoveKind.Up, false);
        try
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
            upShotVisualTimer = Mathf.Max(upShotVisualTimer, d.lanceUpActive + 0.12f);
            ArmLanceHitbox(0.35f, d.lanceUpReach * AttackRangeMultiplier, d.lanceHeight + 0.15f, d.lanceThickness, d.lanceUpAngle, d.lanceUpActive);
            float t = 0f;
            while (t < d.lanceUpActive)
            {
                if (token != lanceGeneration) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally
        {
            EndLanceMove(token);
        }
    }

    // 下攻撃(HandleAttackInputから)。地上 = 前方下への突き / 空中 = 急降下突き。
    void TryLanceDownThrust()
    {
        if (lanceDiving) return;
        if (!isGrounded)
        {
            // 空中: 前/後突きの硬直中でも急降下へ切り替えられる(突きの判定が出ている最中は不可)
            bool inRecovery = lanceOwnsAttack && (LanceMove == LanceMoveKind.Forward || LanceMove == LanceMoveKind.Backward) && (lanceHitbox == null || !lanceHitbox.enabled) && LanceFrameIndex == 1;
            if (isAttacking && !inRecovery) return;
            StartCoroutine(DoLanceDive());
            return;
        }
        if (isAttacking || attackCooldownTimer > 0f) return;
        StartCoroutine(DoLanceDownThrust());
    }

    IEnumerator DoLanceDownThrust()
    {
        var d = lancerDef;
        int gen = attackGeneration;
        int token = BeginLanceMove(LanceMoveKind.Down, true);
        try
        {
            comboCount = 1;
            transform.localScale = Vector3.one;
            float s = AttackSpeedMultiplier;
            float windup = d.lanceWindup * 0.7f * s, active = d.lanceDownActive * s, recovery = d.lanceRecovery * 0.8f * s;
            attackCooldownTimer = windup + active + recovery + attackCooldown * s;

            float t = 0f;
            while (t < windup)
            {
                if (gen != attackGeneration || token != lanceGeneration) yield break;
                t += Time.deltaTime; yield return null;
            }
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
            LanceFrameIndex = 1;
            ArmLanceHitbox(0.3f, d.lanceDownReach * AttackRangeMultiplier, d.lanceDownHeight + 0.12f, d.lanceThickness * 0.9f, -8f, active);
            t = 0f;
            while (t < active)
            {
                if (gen != attackGeneration || token != lanceGeneration) yield break;
                t += Time.deltaTime; yield return null;
            }
            DisarmLanceHitbox(token);
            t = 0f;
            while (t < recovery)
            {
                if (gen != attackGeneration || token != lanceGeneration) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally
        {
            EndLanceMove(token);
        }
    }

    // ===================== 急降下突き ===================== //

    SpriteRenderer lanceDiveTrail;

    // Move()から: 急降下中の縦速度(構え中は空中で一瞬止まる)。
    float LanceDiveVelocityY() => lanceDiveFalling ? -lancerDef.lanceDiveSpeed : 0f;

    IEnumerator DoLanceDive()
    {
        var d = lancerDef;
        int gen = attackGeneration;
        int token = BeginLanceMove(LanceMoveKind.Dive, true);
        try
        {
            comboCount = 1;
            transform.localScale = Vector3.one;
            lanceDiving = true;
            lanceMoveSlowFactor = d.lanceDiveHorizontalScale;
            velocityY = 0f;
            aerialAssistTimer = 0f;

            // 構え: 空中で一瞬止まり、穂先を真下へ向ける
            float t = 0f;
            while (t < d.lanceDiveWindup)
            {
                if (gen != attackGeneration || token != lanceGeneration) yield break;
                if (lanceDiveLanded) break; // 地面すれすれで出した場合
                t += Time.deltaTime; yield return null;
            }

            // 落下: 真下への細長い判定(浮いている敵は叩き落とす)
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(3);
            LanceFrameIndex = 1;
            lanceDiveFalling = true;
            if (lanceAttackInfo != null) lanceAttackInfo.kind = PlayerAttackKind.Down;
            lanceDamageScale = 1f;
            lanceKnockbackScale = 1f;
            ArmLanceHitbox(-0.2f, 1.25f, 0.35f, d.lanceThickness * 1.1f, -90f);
            ShowDiveTrail(true);
            t = 0f;
            while (!lanceDiveLanded && t < d.lanceDiveMaxTime)
            {
                if (gen != attackGeneration || token != lanceGeneration) yield break;
                t += Time.deltaTime; yield return null;
            }
            ShowDiveTrail(false);
            if (!lanceDiveLanded) yield break; // 着地しないまま上限(穴の底へ等) - 何もせず終了

            // 突き刺して着地: 衝撃波+範囲判定+強めのHitStop
            lanceDiving = false;
            lanceDiveFalling = false;
            LanceFrameIndex = 2;
            lanceMoveSlowFactor = 0.25f;
            if (lanceAttackInfo != null) lanceAttackInfo.kind = PlayerAttackKind.DownImpact;
            lanceDamageScale = d.lanceDiveImpactDamageScale;
            lanceKnockbackScale = d.lanceDiveImpactKnockbackScale;
            lanceHitStop = d.lanceDiveImpactHitStop;
            float r = d.lanceDiveImpactRadius * AttackRangeMultiplier;
            ArmLanceHitbox(-r, r * 2f, d.lanceDiveImpactHeight * 0.45f, d.lanceDiveImpactHeight, 0f);
            LanceImpactFx.Spawn(transform.position, r, LanceThrustArt());
            if (AudioManager.Instance != null) AudioManager.Instance.PlayLand();
            var camFollow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (camFollow != null) camFollow.Shake(0.18f, 0.22f);
            StartCoroutine(HitStop.Freeze(d.lanceDiveImpactHitStop));
            t = 0f;
            while (t < d.lanceDiveImpactActive)
            {
                if (gen != attackGeneration || token != lanceGeneration) yield break;
                t += Time.deltaTime; yield return null;
            }
            DisarmLanceHitbox(token);
            float recovery = d.lanceDiveRecovery * AttackSpeedMultiplier;
            attackCooldownTimer = recovery + attackCooldown * AttackSpeedMultiplier * 0.5f;
            t = 0f;
            while (t < recovery)
            {
                if (gen != attackGeneration || token != lanceGeneration) yield break;
                lanceMoveSlowFactor = Mathf.Lerp(0.25f, 1f, t / Mathf.Max(0.01f, recovery));
                t += Time.deltaTime; yield return null;
            }
        }
        finally
        {
            ShowDiveTrail(false);
            EndLanceMove(token);
        }
    }

    // 落下中、穂先の下へ伸びる青緑の風圧
    void ShowDiveTrail(bool on)
    {
        if (!on)
        {
            if (lanceDiveTrail != null) lanceDiveTrail.enabled = false;
            return;
        }
        if (LanceThrustArt() == null) return;
        if (lanceDiveTrail == null)
        {
            var go = new GameObject("LanceDiveTrail");
            go.transform.SetParent(transform, false);
            lanceDiveTrail = go.AddComponent<SpriteRenderer>();
            lanceDiveTrail.sprite = lanceThrustArt;
            lanceDiveTrail.sortingOrder = RenderOrder.SlashFx;
        }
        lanceDiveTrail.transform.localPosition = new Vector3(0f, -0.35f, 0f);
        lanceDiveTrail.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
        lanceDiveTrail.transform.localScale = new Vector3(1.5f, 1.2f, 1f);
        lanceDiveTrail.color = new Color(LanceFxJade.r, LanceFxJade.g, LanceFxJade.b, 0.85f);
        lanceDiveTrail.enabled = true;
    }
}

// 急降下突きの着地の衝撃(地面を走る青緑の衝撃波・広がる輪・岩片・土煙)。
public static class LanceImpactFx
{
    static Sprite ring, rock, puff;
    static bool loaded;

    public static void Spawn(Vector3 feet, float radius, Sprite waveArt)
    {
        if (!loaded)
        {
            loaded = true;
            ring = Resources.Load<Sprite>("Effects/ring");
            rock = Resources.Load<Sprite>("Effects/rockchunk");
            puff = Resources.Load<Sprite>("Effects/cloudpuff");
        }
        Color jade = new Color(0.45f, 1f, 0.82f, 0.95f);
        // 地面を左右へ走る衝撃波
        if (waveArt != null)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                OneShotSpriteEffect.CreateTweened(waveArt, feet + new Vector3(side * radius * 0.35f, 0.18f, 0f), jade,
                    duration: 0.3f, startScale: radius * 0.4f, endScale: radius * 0.95f, drift: new Vector3(side * radius * 3.2f, 0f, 0f),
                    rotationDegrees: side > 0 ? 0f : 180f, sortingOrder: RenderOrder.SlashFx);
            }
        }
        // 広がる輪(地面に沿って平たく見せるため縦を潰したものを2枚)
        if (ring != null)
        {
            var e = OneShotSpriteEffect.CreateTweened(ring, feet + new Vector3(0f, 0.12f, 0f), jade, duration: 0.32f, startScale: 0.5f, endScale: radius * 1.3f, sortingOrder: RenderOrder.SlashFx);
            if (e != null) e.transform.localScale = new Vector3(e.transform.localScale.x, e.transform.localScale.y * 0.35f, 1f);
            OneShotSpriteEffect.CreateTweened(ring, feet + new Vector3(0f, 0.3f, 0f), new Color(0.8f, 1f, 0.95f, 0.8f), duration: 0.2f, startScale: 0.3f, endScale: radius * 0.8f, sortingOrder: RenderOrder.SlashFx);
        }
        // 岩片/破片
        if (rock != null) OneShotSpriteEffect.CreateScatterBurst(rock, feet + new Vector3(0f, 0.15f, 0f), new Color(0.75f, 0.72f, 0.68f, 1f), 7, 0.45f, 0.12f, 0.24f, 5.5f, 1.6f, RenderOrder.CombatFx);
        // 土煙
        if (puff != null) OneShotSpriteEffect.CreateScatterBurst(puff, feet + new Vector3(0f, 0.1f, 0f), new Color(0.85f, 0.82f, 0.75f, 0.55f), 5, 0.5f, 0.35f, 0.6f, 2.2f, 2.5f, RenderOrder.EnvironmentFx);
    }
}
