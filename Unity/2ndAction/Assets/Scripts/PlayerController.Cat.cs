using System.Collections;
using UnityEngine;

// ===== 13人目 ルカ(猫、役割 AGILE / POUNCE) 2026-10-27 ===== //
// マスターの飼い猫がモデルの、クリア後のご褒美キャラ。四足の小さな体で手数を出し、飛びつきで高い所にも届く。
// 無敵の技は持たない(小さな体で攻撃を避けやすいのは個性。代わりに HP は少なめ)。
//  前: 猫パンチ → 猫パンチ → 飛びかかり(3段目は前へ短く跳んで両前足で押さえ込む)
//  後: 小さな後ろ跳び+しっぽ払い(穴へ飛び込まないよう小さく。空中では後ろへ下がらない)
//  上: 斜め上への飛びつき(ジャンプが出た瞬間。空中は1回のジャンプにつき1回 = 二段ジャンプと同じ回数)
//  下: 空中=前脚を突き出して飛びかかるように急降下、着地で衝撃 / 地上=低い引っかき
//  特性(ねこの身のこなし): 被弾の硬直が短い。無敵の長さは足して他のキャラと同じ(連続で被弾しやすくならない)。
//        CharacterDefinition.hurtDuration / hurtInvincibleDuration で持つ(CharacterDatabaseBuilder)
public partial class PlayerController
{
    int catNextStage = 1;
    float catGraceTimer;
    public int CatComboStage { get; private set; }
    static readonly string[] CatComboPoses = { "paw1", "paw2", "pounce" };

    void CatReset() { CatComboStage = 0; catNextStage = 1; catGraceTimer = 0f; }

    // 次に前を押した時に出る段(補助の予測用。猶予の間は続きの段、それ以外は1段目)
    public int CatAssistNextStage => catGraceTimer > 0f ? Mathf.Clamp(catNextStage, 1, 3) : 1;

    // 2026-11-07(初期デッキで 10km まで届かない件): 猫パンチ(1・2段目)と飛びかかり(3段目)は判定の出る時間を長く、戻りを同じだけ短く(1回の長さは同じ)。
    // 判定が 0.07秒(飛びかかり 0.14秒の後に 0.3秒の戻り)だけだと、約 110km/h では間隔 3〜5m で並ぶ雑魚の2体目が
    // 戻りの間に体へ届いて体当たりになっていた(BalanceTuning.CatPawActive)
    public float CatPawActiveTime(int stage)
    {
        var p = kitDef.cat; float s = AttackSpeedMultiplier;
        float active = ArcherPick(p.comboActive, stage - 1, 0.08f) * s;
        if (BalanceTuning.CatPawActive)
            active += Mathf.Min((stage < 3 ? BalanceTuning.CatPawActiveAdd : BalanceTuning.CatPounceActiveAdd) * s, ArcherPick(p.comboRecovery, stage - 1, 0.1f) * s * 0.9f);
        return active;
    }

    // 2026-11-07(同上): 猫パンチ(1・2段目)は序盤の小型の雑魚を1発で倒せる威力に(0.75 → 1.0 = 黒剣士の1段目と同じ)、
    // その分 飛びかかり(3段目)を 1.7 → 1.3 に(3段の合計 3.2 → 3.3 = ボスへの火力はほぼ同じ)。
    // 以前はパンチ1発で倒れず(序盤の上限 HP は黒剣士の1段目で1発の値)、2発目を同じ敵に使う間に次の雑魚が体へ届いていた(BalanceTuning.CatPawOneShot)
    // 2026-11-07(同上): ボス戦の猫パンチ(1・2段目)の踏み込み 0.25 → 0.7m(ボス戦の伸び BossBattle.LungeScale ×1.4〜2.6 が掛かる)。
    // 間合いが 1.05m と短いのに踏み込みも小さく、ゴブリンライダーの棍棒の届く 2.5m 前後から空振りを続けて殴られていた
    // (5〜9km のゴブリンライダー1戦で失うハート 黒剣士 0〜1.3 / ルカ 2.0〜3.7)。当てた相手の手前で止まる(LungeStopX)のは今までどおり
    public float CatComboLunge(int stage)
    {
        float v = ArcherPick(kitDef.cat.comboLunge, stage - 1, 0.25f);
        return BalanceTuning.CatBossLunge && stage < 3 && BossBattle.AnyBossFighting ? Mathf.Max(v, BalanceTuning.CatBossLungeDistance) : v;
    }

    public float CatComboDamageScale(int stage)
    {
        float v = ArcherPick(kitDef.cat.comboDamageScale, stage - 1, 1f);
        if (!BalanceTuning.CatPawOneShot) return v;
        return stage < 3 ? Mathf.Max(v, BalanceTuning.CatPawDamage) : Mathf.Min(v, BalanceTuning.CatPounceDamage);
    }

    void CatTick(float dt)
    {
        if (catGraceTimer > 0f)
        {
            catGraceTimer -= dt;
            if (catGraceTimer <= 0f && !isAttacking) catNextStage = 1;
        }
    }

    void HandleCatInput(FlickDirection f)
    {
        switch (f)
        {
            case FlickDirection.Forward:
                if (kitDiveActive) return;
                if (isAttacking && kitOwnsAttack)
                {
                    if (!comboBuffered && CatComboStage > 0) { comboBuffered = true; bufferedDirection = AttackDirection.Forward; }
                    return;
                }
                if (isAttacking) return;
                if (attackCooldownTimer > 0f && catGraceTimer <= 0f) return;
                StartCoroutine(CatCombo());
                break;
            case FlickDirection.Backward:
                if (isAttacking || attackCooldownTimer > 0f || kitDiveActive) return;
                StartCoroutine(CatBackHop());
                break;
            case FlickDirection.Down:
                if (!canUseDownAttack || kitDiveActive) return;
                if (!isGrounded)
                {
                    if (isAttacking && CatComboStage == 0) return;
                    StartCoroutine(CatPounceDive());
                }
                else if (!isAttacking && attackCooldownTimer <= 0f) StartCoroutine(CatScratch());
                break;
        }
    }

    IEnumerator CatCombo()
    {
        var p = kitDef.cat;
        int stage = catGraceTimer > 0f ? Mathf.Clamp(catNextStage, 1, 3) : 1;
        catGraceTimer = 0f;
        int gen = attackGeneration;
        int token = BeginKitMove(CatComboPoses[stage - 1], true);
        try
        {
            CatComboStage = stage;
            comboCount = stage;
            SetSeqTag(ChainTag(stage, maxComboChain)); // AttackSeq
            float s = AttackSpeedMultiplier;
            float windup = ArcherPick(p.comboWindup, stage - 1, 0.05f) * s;
            float active = CatPawActiveTime(stage);
            float recovery = ArcherPick(p.comboRecovery, stage - 1, 0.1f) * s - (active - ArcherPick(p.comboActive, stage - 1, 0.08f) * s);
            attackCooldownTimer = windup + active + recovery;
            PlayKitPose(CatComboPoses[stage - 1], windup + active, windup);
            float t = 0f;
            while (t < windup)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            float reach = ArcherPick(p.comboReach, stage - 1, 0.9f) * AttackRangeMultiplier;
            bool pounce = stage >= 3;
            // 3段目の飛びかかり: 地上なら小さく跳び上がって前へ(体ごと押さえ込む)。判定は前足の高さから少し上まで
            if (pounce && isGrounded) { velocityY = jumpForce * p.pounceHop; isGrounded = false; } // ジャンプの回数には数えない
            float h = pounce ? p.hitHeight + 0.08f : p.hitHeight;
            float thick = p.hitThickness + (pounce ? 0.25f : 0f);
            ArmKitBox(new Vector2(0.2f + reach * 0.5f, h), new Vector2(reach, thick), 0f, PlayerAttackKind.Normal,
                CatComboDamageScale(stage), ArcherPick(p.comboKnockbackScale, stage - 1, 1f), ArcherPick(p.comboHitStop, stage - 1, 0.03f));
            lungeVelocityX = CatComboLunge(stage) / Mathf.Max(0.01f, active);
            Vector3 paw = KitWorld(new Vector2(0.2f + reach * 0.85f, h));
            if (pounce)
            {
                OneShotSpriteEffect.CreateTweened(KitProjectile.Burst, paw, new Color(1f, 0.9f, 0.7f, 0.9f), duration: 0.16f, startScale: 0.3f, endScale: 0.8f, sortingOrder: RenderOrder.SlashFx);
                KitShake(0.06f, 0.1f);
            }
            else OneShotSpriteEffect.CreateTweened(KitProjectile.Slash, paw, new Color(1f, 0.96f, 0.88f, 0.75f), duration: 0.1f, startScale: 0.3f, endScale: 0.55f, rotationDegrees: stage == 1 ? -20f : 20f, sortingOrder: RenderOrder.SlashFx);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(Mathf.Min(stage, 3));
            t = 0f;
            while (t < active)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            lungeVelocityX = 0f;
            DisarmKitBox(token);
            t = 0f;
            float chainPoint = pounce ? 1f : 0.4f;
            while (t < recovery)
            {
                if (!KitAlive(gen, token)) yield break;
                if (comboBuffered && t >= recovery * chainPoint) break;
                t += Time.deltaTime; yield return null;
            }
            catNextStage = pounce ? 1 : stage + 1;
            catGraceTimer = p.comboGrace;
            bool chain = comboBuffered && !pounce;
            CatComboStage = 0;
            EndKitMove(token);
            comboBuffered = false;
            if (chain && gen == attackGeneration)
            {
                attackCooldownTimer = 0f;
                StartCoroutine(CatCombo());
            }
        }
        finally
        {
            if (token == kitGeneration) CatComboStage = 0;
            EndKitMove(token);
        }
    }

    // 後: 小さな後ろ跳び → 振り返りざまにしっぽ払い。地上ではわずかに浮く、空中では後ろへ下がらない(穴の上で後ろへ流れない)
    // 2026-10-29 ボス戦の停止戦: しっぽの届く長さを TailReachScale 倍に(以前は跳びを足しても後ろ 1.7m まで、空中は 1.15m で、止まった所の後ろの敵に届かなかった)
    public const float TailReachScale = 1.4f;
    IEnumerator CatBackHop()
    {
        var p = kitDef.cat;
        int gen = attackGeneration;
        int token = BeginKitMove("tail", true);
        try
        {
            float s = AttackSpeedMultiplier;
            attackCooldownTimer = (p.backHopTime + 0.12f + p.backRecovery) * s;
            // 後ろが穴なら下がらない(その場でしっぽ払い)。穴を越えた直後の着地で後ろを押しても落ちない
            bool backSafe = true;
            var tm = TerrainManager.Instance;
            if (tm != null && tm.TryGetGroundFast(transform.position.x - KitFacing * (p.backHopDistance + 0.3f), out _, out bool pitBehind) && pitBehind) backSafe = false;
            if (isGrounded && backSafe)
            {
                velocityY = jumpForce * p.backHopUp; isGrounded = false; // ジャンプの回数には数えない
                lungeVelocityX = -p.backHopDistance / Mathf.Max(0.01f, p.backHopTime);
            }
            kitMoveSlowFactor = 0.35f; // 跳んでいる間は自動の前進を弱める(下がった分がすぐ戻らない)
            PlayKitPose("tail", p.backHopTime + 0.12f, p.backHopTime);
            float t = 0f;
            while (t < p.backHopTime)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            lungeVelocityX = 0f;
            float reach = p.tailReach * TailReachScale * AttackRangeMultiplier;
            ArmKitBox(new Vector2(-0.1f - reach * 0.5f, 0.4f), new Vector2(reach, 0.6f), 0f, PlayerAttackKind.Normal, p.tailDamageScale, p.tailKnockbackScale, 0.04f);
            OneShotSpriteEffect.CreateTweened(KitProjectile.Slash, KitWorld(new Vector2(-0.1f - reach * 0.7f, 0.4f)), new Color(1f, 0.92f, 0.8f, 0.75f), duration: 0.12f, startScale: 0.4f, endScale: 0.75f, rotationDegrees: KitFacing > 0f ? 180f : 0f, sortingOrder: RenderOrder.SlashFx);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(1);
            t = 0f;
            while (t < 0.12f)
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

    // 上: 斜め上への飛びつき(FireJump が出したジャンプを飛びつきに置き換える)。空中の敵・飛ぶボスに届かせる技
    IEnumerator CatLeap(bool airborne)
    {
        var p = kitDef.cat;
        velocityY = p.leapSpeedY * (airborne ? 0.9f : 1f);
        int gen = attackGeneration;
        int token = BeginKitMove("leap", false);
        try
        {
            kitPoseTimer = p.leapTime + 0.16f;
            PlayKitPose("leap", p.leapTime + 0.16f);
            lungeVelocityX = p.leapDistanceX / Mathf.Max(0.01f, p.leapTime);
            // 前足の届く範囲: 体の前から斜め上(頭の上まで)。PlayerAttackKind.Up = 打ち上げ(空中のコンボの始まり)
            ArmKitBox(new Vector2(0.45f, 0.75f), new Vector2(1.05f * AttackRangeMultiplier, 1.25f), 35f, PlayerAttackKind.Up, p.leapDamageScale, 1f, 0.04f);
            OneShotSpriteEffect.CreateTweened(KitProjectile.Slash, KitWorld(new Vector2(0.5f, 0.85f)), new Color(1f, 0.95f, 0.85f, 0.8f), duration: 0.14f, startScale: 0.45f, endScale: 0.8f, rotationDegrees: KitFacing > 0f ? 45f : 135f, sortingOrder: RenderOrder.SlashFx);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
            float t = 0f;
            while (t < p.leapTime)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            lungeVelocityX = 0f;
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

    // 下(空中): 前脚を突き出して飛びかかるように落ち、着地で衝撃
    IEnumerator CatPounceDive()
    {
        var p = kitDef.cat;
        int gen = attackGeneration;
        int token = BeginKitMove("dive", true);
        try
        {
            kitDiveActive = true;
            kitVerticalVelocity = -p.diveSpeedY;
            lungeVelocityX = p.diveSpeedX;
            PlayKitPose("dive", 0.12f, 0.06f, 0, 1); // 0 = 空中で前足を構える(一瞬)→ 1 = 前足を突き出して下へ飛びかかる
            ArmKitBox(new Vector2(0.4f, 0.2f), new Vector2(0.85f, 0.6f), -30f, PlayerAttackKind.Down, p.diveDamageScale, 1.3f, p.diveHitStop);
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
            SetKitPose("dive", 2); // 2 = 着地して押さえ込む
            KitBlast.Create(transform.position + new Vector3(0.35f, 0.25f, 0f), new KitBlast.Spec { radius = p.diveImpactRadius * AttackRangeMultiplier, active = 0.1f, kind = PlayerAttackKind.DownImpact, damageScale = p.diveImpactDamageScale, knockbackScale = 1.4f, hitStop = p.diveHitStop, fx = KitProjectile.Burst, tint = new Color(1f, 0.88f, 0.65f, 0.9f), fxScale = 0.9f, shake = 0.08f }, owner: this);
            OneShotSpriteEffect.CreateScatterBurst(KitProjectile.Puff, transform.position + new Vector3(0f, 0.08f, 0f), new Color(0.88f, 0.84f, 0.76f, 0.5f), 4, 0.4f, 0.25f, 0.5f, 1.8f, 2.2f, RenderOrder.EnvironmentFx);
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.LandHeavy);
            attackCooldownTimer = 0.18f;
            t = 0f;
            while (t < 0.16f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    // 下(地上): 姿勢を低くして前足で素早く引っかく(足元の小さい敵)
    IEnumerator CatScratch()
    {
        var p = kitDef.cat;
        int gen = attackGeneration;
        int token = BeginKitMove("scratch", true);
        try
        {
            float s = AttackSpeedMultiplier;
            attackCooldownTimer = (0.05f + p.scratchActive + p.scratchRecovery) * s;
            PlayKitPose("scratch", 0.05f * s + p.scratchActive * s, 0.05f * s);
            float t = 0f;
            while (t < 0.05f * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            float reach = p.scratchReach * AttackRangeMultiplier;
            ArmKitBox(new Vector2(0.15f + reach * 0.5f, 0.18f), new Vector2(reach, 0.4f), 0f, PlayerAttackKind.Normal, p.scratchDamageScale, 1.2f, 0.04f);
            OneShotSpriteEffect.CreateTweened(KitProjectile.Slash, KitWorld(new Vector2(0.15f + reach * 0.6f, 0.18f)), new Color(1f, 0.95f, 0.85f, 0.75f), duration: 0.12f, startScale: 0.4f, endScale: 0.7f, sortingOrder: RenderOrder.SlashFx);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(1);
            t = 0f;
            while (t < p.scratchActive * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            DisarmKitBox(token);
            t = 0f;
            while (t < p.scratchRecovery * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }
}
