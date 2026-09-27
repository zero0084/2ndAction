using System.Collections;
using UnityEngine;

// ===== 8人目 格闘家(FIGHTER / BRAWLER、役割 CLOSE COMBAT / COUNTER) 2026-09-27 ===== //
// 全キャラで最も射程が短い(判定は体のすぐ前から約1unit)。代わりに1発ごとの手応え(HitStop)が強い。
//  前: 4段コンボ Punch → Punch → Kick → Heavy(入力を続けると次の段。双剣士のような
//      多段の斬撃ではなく、段ごとに「止め」が入る打撃。4段目は大きく吹き飛ばす)。
//  後: バックステップ+カウンター受付。受付中に被弾すると、ダメージを受けずに周囲へ強烈な反撃。
//      何も来なければ後ろへ肘打ち(背後の敵への対処)。
//  上: アッパー(敵を打ち上げる、自分は黒剣士のジャンプより低くしか浮かない)。
//  下: 空中=斜め下へのダイブキック(着地で小さな衝撃) / 地上=足払い。
public partial class PlayerController
{
    int fighterNextStage = 1;
    float fighterGraceTimer;
    float fighterCounterTimer;
    public int FighterComboStage { get; private set; }
    public int FighterCounterCount { get; private set; }
    public bool FighterCounterReady => fighterCounterTimer > 0f;

    static readonly string[] FighterComboPoses = { "jab", "straight", "kick", "heavy" };

    void HandleFighterInput(FlickDirection f)
    {
        switch (f)
        {
            case FlickDirection.Forward:
                if (kitDiveActive) return;
                if (isAttacking && kitOwnsAttack)
                {
                    // コンボ中の入力は1つだけ予約(次の段へ)
                    if (!comboBuffered && FighterComboStage > 0) { comboBuffered = true; bufferedDirection = AttackDirection.Forward; }
                    return;
                }
                if (isAttacking) return;
                if (attackCooldownTimer > 0f && fighterGraceTimer <= 0f) return;
                StartCoroutine(FighterCombo());
                break;
            case FlickDirection.Backward:
                if (isAttacking || attackCooldownTimer > 0f || kitDiveActive) return;
                StartCoroutine(FighterBackStep());
                break;
            case FlickDirection.Down:
                if (!canUseDownAttack || kitDiveActive) return;
                if (!isGrounded)
                {
                    if (isAttacking && FighterComboStage == 0) return;
                    StartCoroutine(FighterDiveKick());
                }
                else if (!isAttacking && attackCooldownTimer <= 0f) StartCoroutine(FighterSweep());
                break;
        }
    }

    IEnumerator FighterCombo()
    {
        var p = kitDef.fighter;
        int stage = fighterGraceTimer > 0f ? Mathf.Clamp(fighterNextStage, 1, 4) : 1;
        fighterGraceTimer = 0f;
        int gen = attackGeneration;
        int token = BeginKitMove(FighterComboPoses[stage - 1], true);
        try
        {
            FighterComboStage = stage;
            comboCount = stage;
            float s = AttackSpeedMultiplier;
            float windup = ArcherPick(p.comboWindup, stage - 1, 0.05f) * s;
            float active = ArcherPick(p.comboActive, stage - 1, 0.08f) * s;
            float recovery = ArcherPick(p.comboRecovery, stage - 1, 0.12f) * s;
            attackCooldownTimer = windup + active + recovery;
            float t = 0f;
            while (t < windup)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            SetKitPose(FighterComboPoses[stage - 1], 1);
            float reach = ArcherPick(p.comboReach, stage - 1, 1f) * AttackRangeMultiplier;
            float hitStop = ArcherPick(p.comboHitStop, stage - 1, 0.04f);
            float h = stage == 3 ? p.hitHeight - 0.08f : p.hitHeight;
            ArmKitBox(new Vector2(0.15f + reach * 0.5f, h), new Vector2(reach, p.hitThickness + (stage >= 4 ? 0.2f : 0f)), 0f,
                PlayerAttackKind.Normal, ArcherPick(p.comboDamageScale, stage - 1, 1f), ArcherPick(p.comboKnockbackScale, stage - 1, 1f), hitStop);
            lungeVelocityX = ArcherPick(p.comboLunge, stage - 1, 0.2f) / Mathf.Max(0.01f, active);
            Vector3 fist = KitWorld(new Vector2(0.15f + reach * 0.85f, h));
            if (stage >= 4)
            {
                OneShotSpriteEffect.CreateTweened(KitProjectile.Burst, fist, new Color(1f, 0.75f, 0.4f, 0.95f), duration: 0.18f, startScale: 0.35f, endScale: 1.0f, sortingOrder: RenderOrder.SlashFx);
                OneShotSpriteEffect.CreateTweened(KitProjectile.Ring, fist, new Color(1f, 0.85f, 0.6f, 0.9f), duration: 0.22f, startScale: 0.2f, endScale: 1.2f, sortingOrder: RenderOrder.SlashFx);
                KitShake(0.08f, 0.12f);
            }
            else
            {
                OneShotSpriteEffect.CreateTweened(KitProjectile.Burst, fist, new Color(1f, 0.95f, 0.85f, 0.8f), duration: 0.1f, startScale: 0.18f, endScale: 0.42f, sortingOrder: RenderOrder.SlashFx);
            }
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(Mathf.Min(stage, 3));
            t = 0f;
            while (t < active)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            lungeVelocityX = 0f;
            DisarmKitBox(token);
            // 硬直(1〜3段目は後半で次の段へつなげられる。4段目は最後まで硬直)
            t = 0f;
            float chainPoint = stage >= 4 ? 1f : 0.4f;
            while (t < recovery)
            {
                if (!KitAlive(gen, token)) yield break;
                if (comboBuffered && t >= recovery * chainPoint) break;
                t += Time.deltaTime; yield return null;
            }
            fighterNextStage = stage >= 4 ? 1 : stage + 1;
            fighterGraceTimer = p.comboGrace;
            bool chain = comboBuffered && stage < 4;
            FighterComboStage = 0;
            EndKitMove(token);
            comboBuffered = false;
            if (chain && gen == attackGeneration)
            {
                attackCooldownTimer = 0f;
                StartCoroutine(FighterCombo());
            }
        }
        finally
        {
            if (token == kitGeneration) FighterComboStage = 0;
            EndKitMove(token);
        }
    }

    IEnumerator FighterBackStep()
    {
        var p = kitDef.fighter;
        int gen = attackGeneration;
        int token = BeginKitMove("backstep", true);
        try
        {
            float s = AttackSpeedMultiplier;
            attackCooldownTimer = (p.backStepTime + p.counterWindow + p.backRecovery) * s;
            fighterCounterTimer = p.counterWindow + p.backStepTime;
            // 構えの間は踏みとどまる(自動前進をほぼ止める)。止めないと受付の間に2m以上前へ進んでしまい、
            // 後ろ向きの肘打ち/カウンターが「さっきまで後ろにいた敵」に届かない。
            kitMoveSlowFactor = p.stanceMoveFactor;
            lungeVelocityX = -p.backStepDistance / Mathf.Max(0.01f, p.backStepTime);
            OneShotSpriteEffect.CreateTweened(KitProjectile.Puff, transform.position + new Vector3(0.2f, 0.1f, 0f), new Color(0.85f, 0.82f, 0.75f, 0.5f), duration: 0.25f, startScale: 0.2f, endScale: 0.5f, sortingOrder: RenderOrder.CombatFx);
            float t = 0f;
            while (t < p.backStepTime)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            lungeVelocityX = 0f;
            // カウンター受付(構えたまま)。ここで被弾するとTriggerFighterCounterが新しい技として割り込む。
            t = 0f;
            while (t < p.counterWindow)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            fighterCounterTimer = 0f;
            // 何も来なかった: 後ろへ肘打ち
            SetKitPose("backstep", 1);
            float reach = p.rearStrikeReach * AttackRangeMultiplier;
            ArmKitBox(new Vector2(-0.1f - reach * 0.5f, p.hitHeight), new Vector2(reach, p.hitThickness), 0f, PlayerAttackKind.Normal, p.rearStrikeDamageScale, 1f, 0.04f);
            OneShotSpriteEffect.CreateTweened(KitProjectile.Burst, KitWorld(new Vector2(-0.1f - reach * 0.8f, p.hitHeight)), new Color(1f, 0.95f, 0.85f, 0.8f), duration: 0.1f, startScale: 0.18f, endScale: 0.42f, sortingOrder: RenderOrder.SlashFx);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(1);
            t = 0f;
            while (t < 0.1f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            DisarmKitBox(token);
            t = 0f;
            while (t < p.backRecovery * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    // カウンター成立(KitInterceptDamageから): 被弾を無かったことにして、周囲へ強烈な反撃。
    void TriggerFighterCounter(string source)
    {
        fighterCounterTimer = 0f;
        FighterCounterCount++;
        FreezeDiagnostics.LogEvent($"[Fighter] COUNTER source={source} x={transform.position.x:F2}");
        StartCoroutine(FighterCounterStrike());
    }

    IEnumerator FighterCounterStrike()
    {
        var p = kitDef.fighter;
        int gen = attackGeneration;
        int token = BeginKitMove("counter", true);
        try
        {
            kitIFrameTimer = Mathf.Max(kitIFrameTimer, p.counterInvincible);
            Vector3 c = transform.position + new Vector3(0f, p.hitHeight, 0f);
            OneShotSpriteEffect.CreateTweened(KitProjectile.Ring, c, new Color(1f, 0.9f, 0.5f, 1f), duration: 0.25f, startScale: 0.3f, endScale: p.counterRadius * 2.2f, sortingOrder: RenderOrder.SlashFx);
            StartCoroutine(HitStop.Freeze(0.06f));
            float t = 0f;
            while (t < 0.05f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            SetKitPose("counter", 1);
            KitBlast.Create(c, new KitBlast.Spec { radius = p.counterRadius * AttackRangeMultiplier, active = 0.12f, kind = PlayerAttackKind.Normal, damageScale = p.counterDamageScale, knockbackScale = p.counterKnockbackScale, hitStop = p.counterHitStop, fx = KitProjectile.Burst, tint = new Color(1f, 0.7f, 0.35f, 0.95f), fxScale = 1.1f, shake = 0.14f });
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(3);
            t = 0f;
            while (t < 0.28f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            attackCooldownTimer = 0.1f;
        }
        finally { EndKitMove(token); }
    }

    // 上: アッパー(FireJumpが出したジャンプを低い跳躍に置き換える)。
    IEnumerator FighterUppercut(bool airborne)
    {
        var p = kitDef.fighter;
        velocityY = jumpForce * p.uppercutHop * (airborne ? 0.95f : 1f);
        int gen = attackGeneration;
        int token = BeginKitMove("uppercut", false);
        try
        {
            kitPoseTimer = p.uppercutActive + 0.16f;
            ArmKitBox(new Vector2(0.55f, 0.95f), new Vector2(0.95f * AttackRangeMultiplier, 1.35f), 0f, PlayerAttackKind.Up, p.uppercutDamageScale, 1f, p.uppercutHitStop);
            OneShotSpriteEffect.CreateTweened(KitProjectile.Slash, KitWorld(new Vector2(0.55f, 1.0f)), new Color(1f, 0.85f, 0.6f, 0.85f), duration: 0.16f, startScale: 0.5f, endScale: 0.9f, rotationDegrees: KitFacing > 0f ? 90f : 90f, sortingOrder: RenderOrder.SlashFx);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
            float t = 0f;
            while (t < p.uppercutActive)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            DisarmKitBox(token);
            t = 0f;
            while (t < 0.16f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    IEnumerator FighterDiveKick()
    {
        var p = kitDef.fighter;
        int gen = attackGeneration;
        int token = BeginKitMove("divekick", true);
        try
        {
            kitDiveActive = true;
            kitVerticalVelocity = -p.diveKickSpeedY;
            lungeVelocityX = p.diveKickSpeedX;
            ArmKitBox(new Vector2(0.35f, 0.15f), new Vector2(0.8f, 0.7f), -35f, PlayerAttackKind.Down, p.diveKickDamageScale, 1.4f, p.diveKickHitStop);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
            float t = 0f;
            while (!kitDiveLanded && t < 0.9f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            if (!kitDiveLanded) yield break;
            kitDiveActive = false;
            kitVerticalVelocity = null;
            lungeVelocityX = 0f;
            DisarmKitBox(token);
            SetKitPose("divekick", 1);
            KitBlast.Create(transform.position + new Vector3(0.3f, 0.3f, 0f), new KitBlast.Spec { radius = p.diveKickImpactRadius * AttackRangeMultiplier, active = 0.1f, kind = PlayerAttackKind.DownImpact, damageScale = p.diveKickImpactDamageScale, knockbackScale = 1.5f, hitStop = p.diveKickHitStop, fx = KitProjectile.Burst, tint = new Color(1f, 0.8f, 0.55f, 0.9f), fxScale = 1f, shake = 0.1f });
            OneShotSpriteEffect.CreateScatterBurst(KitProjectile.Puff, transform.position + new Vector3(0f, 0.1f, 0f), new Color(0.85f, 0.82f, 0.75f, 0.55f), 4, 0.45f, 0.3f, 0.55f, 2f, 2.5f, RenderOrder.EnvironmentFx);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayLand();
            attackCooldownTimer = 0.2f;
            t = 0f;
            while (t < 0.18f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    IEnumerator FighterSweep()
    {
        var p = kitDef.fighter;
        int gen = attackGeneration;
        int token = BeginKitMove("sweep", true);
        try
        {
            float s = AttackSpeedMultiplier;
            attackCooldownTimer = (0.06f + p.sweepActive + p.sweepRecovery) * s;
            float t = 0f;
            while (t < 0.06f * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            SetKitPose("sweep", 1);
            float reach = p.sweepReach * AttackRangeMultiplier;
            ArmKitBox(new Vector2(0.1f + reach * 0.5f, 0.2f), new Vector2(reach, 0.42f), 0f, PlayerAttackKind.Normal, p.sweepDamageScale, p.sweepKnockbackScale, 0.05f);
            OneShotSpriteEffect.CreateTweened(KitProjectile.Slash, KitWorld(new Vector2(0.1f + reach * 0.6f, 0.2f)), new Color(1f, 0.9f, 0.75f, 0.8f), duration: 0.14f, startScale: 0.5f, endScale: 0.85f, sortingOrder: RenderOrder.SlashFx);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(1);
            t = 0f;
            while (t < p.sweepActive * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            DisarmKitBox(token);
            t = 0f;
            while (t < p.sweepRecovery * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }
}
