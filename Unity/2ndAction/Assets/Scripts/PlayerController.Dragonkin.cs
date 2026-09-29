using System.Collections;
using UnityEngine;

// ===== 12人目 竜人(DRAGONKIN、役割 BRUTE) 2026-09-28 ===== //
// 竜の血を引く本人が、武器なしで爪・尾・翼・炎で戦う(竜騎士=人間+竜の鎧+ランス、との違い)。
//  前: 爪3段(格闘家より遅く、1発が重い。3段目は大きく吹き飛ばす)。
//  後: 尾で後方を大きく薙ぎ払う(範囲は前より広く、威力は少し低い。敵を打ち上げる)。
//  上: 翼で強く羽ばたいて通常のジャンプより高く上昇+爪のアッパー。
//  下: 空中=斜め下へ炎のブレス(ブレス中は少しだけ滞空) / 地上=前方の地面へ低いブレス。炎が地面に
//      当たると短時間「燃える地面」(同時に1つ、細かい継続ダメージ)。
//  滑空: 常時飛行はしない。ジャンプの頂点を過ぎて落ち始めた時、1回のジャンプにつき1回だけ、
//      glideTime秒だけ落下を遅くする(=二段ジャンプを使っても合計はその2回分まで)。
//  体が大きい: プレイヤーの当たり判定を少し大きくする(キャラを切り替えると元に戻す)。
public partial class PlayerController
{
    int dragonComboStage, dragonNextStage = 1;
    float dragonGraceTimer;
    float dragonGlideTimer;
    bool dragonGlideAvailable;
    public bool IsDragonGliding => dragonGlideTimer > 0f;
    public int DragonGlideCount { get; private set; }
    public int DragonComboStage => dragonComboStage;
    public int DragonBurnsCreated { get; private set; }
    BoxCollider2D bodyCol;
    Vector2 bodyColBaseSize, bodyColBaseOffset;
    bool bodyColCaptured;

    // 体の当たり判定の大きさ(竜人だけ大きく、他は元の大きさ)。
    void ApplyBodyScale(float scale)
    {
        if (bodyCol == null) bodyCol = GetComponent<BoxCollider2D>();
        if (bodyCol == null) return;
        if (!bodyColCaptured) { bodyColCaptured = true; bodyColBaseSize = bodyCol.size; bodyColBaseOffset = bodyCol.offset; }
        bodyCol.size = bodyColBaseSize * scale;
        // 足元の位置は変えない(下端を揃えて上へ大きくする)
        bodyCol.offset = new Vector2(bodyColBaseOffset.x, bodyColBaseOffset.y + (bodyColBaseSize.y * scale - bodyColBaseSize.y) * 0.5f);
    }

    void DragonReset()
    {
        dragonGlideTimer = 0f;
        dragonGlideAvailable = false;
        dragonComboStage = 0; dragonNextStage = 1; dragonGraceTimer = 0f;
    }

    void DragonUpdate()
    {
        float dt = Time.deltaTime;
        var p = kitDef.dragonkin;
        if (dragonGraceTimer > 0f) { dragonGraceTimer -= dt; if (dragonGraceTimer <= 0f && !isAttacking) dragonNextStage = 1; }
        // 滑空: 落ち始めた瞬間に1回だけ(技の最中・被弾中・地上では発動しない)
        if (dragonGlideTimer > 0f)
        {
            dragonGlideTimer -= dt;
            if (isGrounded || IsReacting || kitOwnsAttack) dragonGlideTimer = 0f;
            if (dragonGlideTimer <= 0f && KitPoseName == "glide") { KitPoseFrames = null; KitPoseName = null; }
        }
        else if (dragonGlideAvailable && !isGrounded && !IsReacting && !kitOwnsAttack && velocityY < -0.5f)
        {
            dragonGlideAvailable = false;
            dragonGlideTimer = p.glideTime;
            DragonGlideCount++;
            if (!kitOwnsAttack) { SetKitPose("glide", 0); kitPoseTimer = p.glideTime; }
        }
        if (isGrounded) dragonGlideAvailable = false;
    }

    // Move()の重力計算の直後から(竜人の滑空中だけ落下速度を上限で止める)。他キャラは何もしない。
    void KitClampFall()
    {
        if (kit != CharacterKit.Dragonkin || dragonGlideTimer <= 0f || kitDef == null) return;
        float cap = -kitDef.dragonkin.glideFallSpeed;
        if (velocityY < cap) velocityY = cap;
    }

    void HandleDragonInput(FlickDirection f)
    {
        switch (f)
        {
            case FlickDirection.Forward:
                if (kitDiveActive) return;
                if (isAttacking && kitOwnsAttack)
                {
                    if (!comboBuffered && dragonComboStage > 0) { comboBuffered = true; bufferedDirection = AttackDirection.Forward; }
                    return;
                }
                if (isAttacking) return;
                if (attackCooldownTimer > 0f && dragonGraceTimer <= 0f) return;
                StartCoroutine(DragonClaw());
                break;
            case FlickDirection.Backward:
                if (isAttacking || attackCooldownTimer > 0f) return;
                StartCoroutine(DragonTail());
                break;
            case FlickDirection.Down:
                if (!canUseDownAttack || isAttacking || attackCooldownTimer > 0f) return;
                StartCoroutine(DragonBreath(!isGrounded));
                break;
        }
    }

    static readonly string[] DragonComboPoses = { "claw1", "claw2", "claw3" };

    IEnumerator DragonClaw()
    {
        var p = kitDef.dragonkin;
        int stage = dragonGraceTimer > 0f ? Mathf.Clamp(dragonNextStage, 1, 3) : 1;
        dragonGraceTimer = 0f;
        int gen = attackGeneration;
        int token = BeginKitMove(DragonComboPoses[stage - 1], true);
        try
        {
            dragonComboStage = stage;
            comboCount = stage;
            float s = AttackSpeedMultiplier;
            float windup = ArcherPick(p.comboWindup, stage - 1, 0.1f) * s;
            float active = ArcherPick(p.comboActive, stage - 1, 0.1f) * s;
            float recovery = ArcherPick(p.comboRecovery, stage - 1, 0.16f) * s;
            attackCooldownTimer = windup + active + recovery;
            float t = 0f;
            while (t < windup)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            SetKitPose(DragonComboPoses[stage - 1], 1);
            float reach = ArcherPick(p.comboReach, stage - 1, 1.3f) * AttackRangeMultiplier;
            ArmKitBox(new Vector2(0.2f + reach * 0.5f, 0.7f), new Vector2(reach, stage >= 3 ? 1.2f : 0.9f), 0f, PlayerAttackKind.Normal,
                ArcherPick(p.comboDamageScale, stage - 1, 1.3f), ArcherPick(p.comboKnockbackScale, stage - 1, 1f), ArcherPick(p.comboHitStop, stage - 1, 0.05f));
            lungeVelocityX = ArcherPick(p.comboLunge, stage - 1, 0.2f) / Mathf.Max(0.01f, active);
            Vector3 at = KitWorld(new Vector2(0.2f + reach * 0.6f, 0.7f));
            Color claw = new Color(1f, 0.6f, 0.25f, 0.9f);
            if (stage >= 3)
            {
                OneShotSpriteEffect.CreateTweened(KitProjectile.Slash, at, claw, duration: 0.2f, startScale: 0.7f, endScale: 1.3f, rotationDegrees: -20f, sortingOrder: RenderOrder.SlashFx, holdFraction: 0.3f);
                OneShotSpriteEffect.CreateTweened(KitProjectile.Slash, at + new Vector3(0f, 0.2f, 0f), claw, duration: 0.2f, startScale: 0.7f, endScale: 1.3f, rotationDegrees: 20f, sortingOrder: RenderOrder.SlashFx, holdFraction: 0.3f);
                KitShake(0.08f, 0.14f);
            }
            else OneShotSpriteEffect.CreateTweened(KitProjectile.Slash, at, claw, duration: 0.14f, startScale: 0.55f, endScale: 0.9f, rotationDegrees: stage == 1 ? -40f : 40f, sortingOrder: RenderOrder.SlashFx);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(stage);
            t = 0f;
            while (t < active)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            lungeVelocityX = 0f;
            DisarmKitBox(token);
            t = 0f;
            float chainPoint = stage >= 3 ? 1f : 0.45f;
            while (t < recovery)
            {
                if (!KitAlive(gen, token)) yield break;
                if (comboBuffered && t >= recovery * chainPoint) break;
                t += Time.deltaTime; yield return null;
            }
            dragonNextStage = stage >= 3 ? 1 : stage + 1;
            dragonGraceTimer = p.comboGrace;
            bool chain = comboBuffered && stage < 3;
            dragonComboStage = 0;
            EndKitMove(token);
            comboBuffered = false;
            if (chain && gen == attackGeneration) { attackCooldownTimer = 0f; StartCoroutine(DragonClaw()); }
        }
        finally
        {
            if (token == kitGeneration) dragonComboStage = 0;
            EndKitMove(token);
        }
    }

    // 後: 尾の薙ぎ払い。向きは変えない(体の後ろの尾で払う)。後ろの敵は打ち上げる(前へ押し出さない)。
    IEnumerator DragonTail()
    {
        var p = kitDef.dragonkin;
        int gen = attackGeneration;
        int token = BeginKitMove("tail", true);
        try
        {
            float s = AttackSpeedMultiplier;
            attackCooldownTimer = (p.tailWindup + p.tailActive + p.tailRecovery) * s;
            float t = 0f;
            while (t < p.tailWindup * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            SetKitPose("tail", 1);
            AttackStep(-kitBackStep, 0.16f); // 2026-09-30: 後ろ攻撃で後退
            float reach = p.tailReach * AttackRangeMultiplier;
            ArmKitBox(new Vector2(-0.1f - reach * 0.5f + 0.4f, 0.45f), new Vector2(reach + 0.8f, 1.0f), 0f, PlayerAttackKind.Up, p.tailDamageScale, 1f, 0.06f);
            OneShotSpriteEffect.CreateTweened(KitProjectile.Slash, KitWorld(new Vector2(-reach * 0.55f, 0.45f)), new Color(1f, 0.55f, 0.3f, 0.85f), duration: 0.2f, startScale: reach * 0.5f, endScale: reach * 0.8f, rotationDegrees: 180f, sortingOrder: RenderOrder.SlashFx, holdFraction: 0.25f);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
            t = 0f;
            while (t < p.tailActive * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            DisarmKitBox(token);
            t = 0f;
            while (t < p.tailRecovery * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    // 上: 羽ばたき(FireJumpのジャンプより高く上がる)+爪のアッパー。
    IEnumerator DragonFlap(bool airborne)
    {
        var p = kitDef.dragonkin;
        velocityY = jumpForce * p.flapJump * (airborne ? 0.85f : 1f);
        dragonGlideAvailable = true; // このジャンプの落ち際に1回だけ滑空できる
        int gen = attackGeneration;
        int token = BeginKitMove("flap", false);
        try
        {
            kitPoseTimer = p.flapActive + 0.18f;
            ArmKitBox(new Vector2(0.55f, 1.0f), new Vector2(1.2f * AttackRangeMultiplier, 1.6f), 0f, PlayerAttackKind.Up, p.flapDamageScale, 1f, 0.06f);
            OneShotSpriteEffect.CreateScatterBurst(KitProjectile.Puff, transform.position + new Vector3(-0.2f, 0.2f, 0f), new Color(0.8f, 0.7f, 0.6f, 0.5f), 4, 0.35f, 0.25f, 0.5f, 3f, 2f, RenderOrder.EnvironmentFx);
            OneShotSpriteEffect.CreateTweened(KitProjectile.Slash, KitWorld(new Vector2(0.6f, 1.1f)), new Color(1f, 0.6f, 0.25f, 0.85f), duration: 0.16f, startScale: 0.5f, endScale: 0.95f, rotationDegrees: 90f, sortingOrder: RenderOrder.SlashFx);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
            float t = 0f;
            while (t < p.flapActive)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            DisarmKitBox(token);
            t = 0f;
            while (t < 0.18f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    IEnumerator DragonBreath(bool airborne)
    {
        var p = kitDef.dragonkin;
        int gen = attackGeneration;
        int token = BeginKitMove("breath", true);
        try
        {
            if (airborne) kitVerticalVelocity = -p.breathHoverFall; // ブレス中は少しだけ滞空(長いホバーはしない)
            float s = AttackSpeedMultiplier;
            attackCooldownTimer = (p.breathTime + 0.35f) * s;
            float ang = airborne ? p.breathAngleAir : p.breathAngleGround;
            Sprite flame = Resources.Load<Sprite>("Effects/fireball");
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(3);
            for (int i = 0; i < p.breathShots; i++)
            {
                if (!KitAlive(gen, token)) yield break;
                float a = (ang + Random.Range(-6f, 6f)) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(a) * KitFacing, Mathf.Sin(a));
                var f = KitProjectile.Create(flame != null ? flame : KitProjectile.Orb, KitWorld(new Vector2(0.55f, airborne ? 0.9f : 1.0f)), dir * p.breathSpeed, 0.9f,
                    new Vector2(0.55f, 0.55f), new Vector2(0.4f, 0.4f), new Color(1f, 0.6f + 0.1f * i, 0.25f, 1f), PlayerAttackKind.Normal,
                    p.breathDamageScale, 0.7f, 0.02f, new Color(1f, 0.45f, 0.1f, 0.6f), true);
                f.name = "DragonBreath";
                f.stopAtGround = true;
                f.pierce = -1; // 炎は敵を焼きながら抜けて地面まで届く(敵に当たって消えると燃える地面ができない)
                f.onGround = (pr, at) => DragonIgnite(at);
                float w = 0f;
                while (w < p.breathTime / p.breathShots)
                {
                    if (!KitAlive(gen, token)) yield break;
                    w += Time.deltaTime; yield return null;
                }
            }
            kitVerticalVelocity = null;
            float t = 0f;
            while (t < 0.25f * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    // ブレスが地面に当たった所を燃やす(既に燃えている範囲の近くなら、その範囲を燃やし直すだけ=同時に1つ)。
    void DragonIgnite(Vector3 at)
    {
        if (kit != CharacterKit.Dragonkin || kitDef == null) return;
        var p = kitDef.dragonkin;
        var cur = KitZone.Find(KitZone.Kind.Fire);
        if (cur != null && Mathf.Abs(cur.transform.position.x - at.x) < p.burnWidth * 0.5f && cur.Age < 0.6f) return;
        float? g = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(at.x) : null;
        KitZone.Create(KitZone.Kind.Fire, new Vector3(at.x, g ?? at.y, 0f), p.burnWidth, 0.8f, p.burnDuration, p.burnTick, p.burnTickDamageScale, 1f);
        DragonBurnsCreated++;
    }

    static void DragonCleanup() => KitZone.ClearKind(KitZone.Kind.Fire);
}
