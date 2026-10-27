using System.Collections;
using UnityEngine;

// ===== 14人目 ペンタトゥットゥルー星人(役割 BOUNCE / STRETCH) 2026-10-27 ===== //
// マスターの学生時代からのオリジナルキャラ。もくもくの白い体と、ぐーんと伸びる手足。飛ばない(普通のジャンプと落下)。
//  前: のびーるパンチ → 反対の腕 → 両腕でぽかぽか(1・2段目は遠くへ届くが溜めと戻りの隙がある。3段目は近くの連打)
//  後: もくもくバンプ(体を膨らませて背中側を弾き飛ばす。ダメージ弱め・ノックバック強め)
//  上: 触角がぴんと立つ → 両腕が真上へ伸びる(上へ長く届く対空)
//  下: 空中=丸まってぽよんスタンプ(着地の瞬間に体が横へ広がって周りを弾く)/ 地上=のびーるキック
//  特性(ぽよんボディ): 下攻撃(スタンプ)が敵に当たった時だけ、ぽよんと跳ね返る。1回のジャンプにつき1回。
//        跳ね返った後も普通に操作できる。上に天井があれば跳ね返りを弱める(天井へ繰り返しぶつからない)
public partial class PlayerController
{
    int cloudNextStage = 1;
    float cloudGraceTimer;
    bool cloudBounceUsed;      // このジャンプで跳ね返った(着地で戻す)
    bool cloudBouncePending;   // スタンプが敵に当たった(次のフレームで跳ね返る)
    public int CloudComboStage { get; private set; }
    public int CloudBounceCount { get; private set; } // 確認用
    static readonly string[] CloudComboPoses = { "stretch1", "stretch2", "pokapoka" };

    void CloudReset() { CloudComboStage = 0; cloudNextStage = 1; cloudGraceTimer = 0f; cloudBouncePending = false; }

    void CloudTick(float dt)
    {
        if (cloudGraceTimer > 0f)
        {
            cloudGraceTimer -= dt;
            if (cloudGraceTimer <= 0f && !isAttacking) cloudNextStage = 1;
        }
    }

    void CloudLanded() { cloudBounceUsed = false; }

    void HandleCloudInput(FlickDirection f)
    {
        switch (f)
        {
            case FlickDirection.Forward:
                if (kitDiveActive) return;
                if (isAttacking && kitOwnsAttack)
                {
                    if (!comboBuffered && CloudComboStage > 0) { comboBuffered = true; bufferedDirection = AttackDirection.Forward; }
                    return;
                }
                if (isAttacking) return;
                if (attackCooldownTimer > 0f && cloudGraceTimer <= 0f) return;
                StartCoroutine(CloudCombo());
                break;
            case FlickDirection.Backward:
                if (isAttacking || attackCooldownTimer > 0f || kitDiveActive) return;
                StartCoroutine(CloudBump());
                break;
            case FlickDirection.Down:
                if (!canUseDownAttack || kitDiveActive) return;
                if (!isGrounded)
                {
                    if (isAttacking && CloudComboStage == 0) return;
                    StartCoroutine(CloudStomp());
                }
                else if (!isAttacking && attackCooldownTimer <= 0f) StartCoroutine(CloudKick());
                break;
        }
    }

    IEnumerator CloudCombo()
    {
        var p = kitDef.cloud;
        int stage = cloudGraceTimer > 0f ? Mathf.Clamp(cloudNextStage, 1, 3) : 1;
        cloudGraceTimer = 0f;
        int gen = attackGeneration;
        int token = BeginKitMove(CloudComboPoses[stage - 1], true);
        try
        {
            CloudComboStage = stage;
            comboCount = stage;
            SetSeqTag(ChainTag(stage, maxComboChain)); // AttackSeq
            float s = AttackSpeedMultiplier;
            float windup = ArcherPick(p.comboWindup, stage - 1, 0.1f) * s;
            float active = ArcherPick(p.comboActive, stage - 1, 0.1f) * s;
            float recovery = ArcherPick(p.comboRecovery, stage - 1, 0.16f) * s;
            attackCooldownTimer = windup + active + recovery;
            PlayKitPose(CloudComboPoses[stage - 1], windup + active, windup);
            float t = 0f;
            while (t < windup)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            bool poka = stage >= 3;
            float reach = ArcherPick(p.comboReach, stage - 1, 2f) * AttackRangeMultiplier;
            float thick = p.hitThickness + (poka ? 0.35f : 0f);
            ArmKitBox(new Vector2(0.3f + reach * 0.5f, p.hitHeight), new Vector2(reach, thick), 0f, PlayerAttackKind.Normal,
                ArcherPick(p.comboDamageScale, stage - 1, 1f), ArcherPick(p.comboKnockbackScale, stage - 1, 1f), ArcherPick(p.comboHitStop, stage - 1, 0.04f));
            lungeVelocityX = ArcherPick(p.comboLunge, stage - 1, 0f) / Mathf.Max(0.01f, active);
            Vector3 fist = KitWorld(new Vector2(0.3f + reach * 0.95f, p.hitHeight));
            if (poka) OneShotSpriteEffect.CreateTweened(KitProjectile.Burst, fist, new Color(0.92f, 0.97f, 1f, 0.9f), duration: 0.18f, startScale: 0.3f, endScale: 0.9f, sortingOrder: RenderOrder.SlashFx);
            else OneShotSpriteEffect.CreateTweened(KitProjectile.Burst, fist, new Color(0.9f, 0.96f, 1f, 0.8f), duration: 0.12f, startScale: 0.2f, endScale: 0.5f, sortingOrder: RenderOrder.SlashFx);
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
            float chainPoint = poka ? 1f : 0.45f;
            while (t < recovery)
            {
                if (!KitAlive(gen, token)) yield break;
                if (comboBuffered && t >= recovery * chainPoint) break;
                t += Time.deltaTime; yield return null;
            }
            cloudNextStage = poka ? 1 : stage + 1;
            cloudGraceTimer = p.comboGrace;
            bool chain = comboBuffered && !poka;
            CloudComboStage = 0;
            EndKitMove(token);
            comboBuffered = false;
            if (chain && gen == attackGeneration)
            {
                attackCooldownTimer = 0f;
                StartCoroutine(CloudCombo());
            }
        }
        finally
        {
            if (token == kitGeneration) CloudComboStage = 0;
            EndKitMove(token);
        }
    }

    // 後: 体をぷくっと膨らませて背中側を弾く(その場。後ろへは下がらない)
    IEnumerator CloudBump()
    {
        var p = kitDef.cloud;
        int gen = attackGeneration;
        int token = BeginKitMove("bump", true);
        try
        {
            float s = AttackSpeedMultiplier;
            attackCooldownTimer = (p.bumpWindup + p.bumpActive + p.bumpRecovery) * s;
            kitMoveSlowFactor = 0.4f;
            PlayKitPose("bump", (p.bumpWindup + p.bumpActive) * s, p.bumpWindup * s);
            float t = 0f;
            while (t < p.bumpWindup * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            float reach = p.bumpReach * AttackRangeMultiplier;
            ArmKitBox(new Vector2(-reach * 0.5f, 0.45f), new Vector2(reach, 0.85f), 0f, PlayerAttackKind.Normal, p.bumpDamageScale, p.bumpKnockbackScale, 0.05f);
            OneShotSpriteEffect.CreateTweened(KitProjectile.Ring, KitWorld(new Vector2(-0.3f, 0.45f)), new Color(0.9f, 0.96f, 1f, 0.85f), duration: 0.2f, startScale: 0.3f, endScale: 1.1f, sortingOrder: RenderOrder.SlashFx);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(1);
            t = 0f;
            while (t < p.bumpActive * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            DisarmKitBox(token);
            t = 0f;
            while (t < p.bumpRecovery * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    // 上: 触角がぴんと立つ(予備動作)→ 両腕が真上へ伸びる。FireJump のジャンプは少し低めに置き換える
    IEnumerator CloudUpReach(bool airborne)
    {
        var p = kitDef.cloud;
        velocityY = jumpForce * p.upHop * (airborne ? 0.9f : 1f);
        int gen = attackGeneration;
        int token = BeginKitMove("upreach", false);
        try
        {
            kitPoseTimer = p.upWindup + p.upActive + 0.12f;
            PlayKitPose("upreach", p.upWindup + p.upActive, p.upWindup);
            float t = 0f;
            while (t < p.upWindup)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            float reach = p.upReach * AttackRangeMultiplier;
            // 腕は真上へ伸びるが、横長の体の前の端から上へ。前にいる近くの敵にも当たって打ち上げる(空中のコンボの始まり)。
            // 2026-10-27 監査: 真上だけ(幅 0.95)だと前の敵に一度も当たらず、打ち上げられなかった
            ArmKitBox(new Vector2(0.5f, 0.25f + reach * 0.5f), new Vector2(1.3f, reach), 0f, PlayerAttackKind.Up, p.upDamageScale, 1f, 0.05f);
            OneShotSpriteEffect.CreateTweened(KitProjectile.Burst, KitWorld(new Vector2(0.15f, 0.6f + reach * 0.9f)), new Color(0.9f, 0.96f, 1f, 0.85f), duration: 0.14f, startScale: 0.25f, endScale: 0.6f, sortingOrder: RenderOrder.SlashFx);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
            t = 0f;
            while (t < p.upActive)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            DisarmKitBox(token);
            t = 0f;
            while (t < 0.12f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    // 下(空中): 丸まって落ちる。敵に当たったら(1回のジャンプにつき1回)ぽよんと跳ね返る。
    // 地面に着いたら、体が横へ広がって周りを弾く
    IEnumerator CloudStomp()
    {
        var p = kitDef.cloud;
        int gen = attackGeneration;
        int token = BeginKitMove("stomp", true);
        try
        {
            kitDiveActive = true;
            kitVerticalVelocity = -p.stompSpeedY;
            lungeVelocityX = p.stompSpeedX;
            SetKitPose("stomp", 0);
            ArmKitBox(new Vector2(0.15f, 0.2f), new Vector2(1.05f, 0.6f), 0f, PlayerAttackKind.Down, p.stompDamageScale, 1.2f, p.stompHitStop);
            cloudBouncePending = false;
            if (kitHitInfo != null) kitHitInfo.onHit = () => { if (!cloudBounceUsed) cloudBouncePending = true; };
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
            float t = 0f;
            while (!kitDiveLanded && t < 0.9f)
            {
                if (!KitAlive(gen, token)) yield break;
                if (cloudBouncePending)
                {
                    // ぽよん: 急降下をやめて跳ね返る(この後は普通に操作できる)
                    cloudBouncePending = false;
                    cloudBounceUsed = true;
                    CloudBounceCount++;
                    kitDiveActive = false;
                    kitVerticalVelocity = null;
                    lungeVelocityX = 0f;
                    DisarmKitBox(token);
                    velocityY = CloudBounceVelocity(p);
                    SetKitPose("stomp", 1);
                    OneShotSpriteEffect.CreateTweened(KitProjectile.Ring, transform.position + new Vector3(0f, 0.1f, 0f), new Color(0.9f, 0.96f, 1f, 0.85f), duration: 0.18f, startScale: 0.3f, endScale: 1f, sortingOrder: RenderOrder.SlashFx);
                    if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Jump);
                    attackCooldownTimer = 0.12f;
                    t = 0f;
                    while (t < 0.12f)
                    {
                        if (!KitAlive(gen, token)) yield break;
                        t += Time.deltaTime; yield return null;
                    }
                    yield break;
                }
                t += Time.deltaTime; yield return null;
            }
            if (!kitDiveLanded) yield break;
            kitDiveActive = false;
            kitVerticalVelocity = null;
            lungeVelocityX = 0f;
            DisarmKitBox(token);
            SetKitPose("stomp", 2);
            KitBlast.Create(transform.position + new Vector3(0f, 0.3f, 0f), new KitBlast.Spec { radius = p.stompImpactRadius * AttackRangeMultiplier, active = 0.1f, kind = PlayerAttackKind.DownImpact, damageScale = p.stompImpactDamageScale, knockbackScale = 1.5f, hitStop = p.stompHitStop, fx = KitProjectile.Ring, tint = new Color(0.9f, 0.96f, 1f, 0.9f), fxScale = 1.1f, shake = 0.08f }, owner: this);
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.LandHeavy);
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

    // 跳ね返りの初速。上に天井があれば、頭が天井に届かない高さまで弱める(低い天井では跳ねない)
    float CloudBounceVelocity(CloudKitParams p)
    {
        float v = jumpForce * p.bounceJump;
        var tm = TerrainManager.Instance;
        float? limit = tm != null ? tm.GetCeilingLimitY(transform.position.x) : null;
        if (limit.HasValue && gravity > 0f)
        {
            float room = limit.Value - transform.position.y - p.bounceCeilingMargin;
            if (room <= 0.15f) return 0f;
            v = Mathf.Min(v, Mathf.Sqrt(2f * gravity * room));
        }
        return v;
    }

    // 下(地上): 足をぐーんと伸ばして低く遠くを払う
    IEnumerator CloudKick()
    {
        var p = kitDef.cloud;
        int gen = attackGeneration;
        int token = BeginKitMove("kick", true);
        try
        {
            float s = AttackSpeedMultiplier;
            attackCooldownTimer = (0.08f + p.kickActive + p.kickRecovery) * s;
            PlayKitPose("kick", (0.08f + p.kickActive) * s, 0.08f * s);
            float t = 0f;
            while (t < 0.08f * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            float reach = p.kickReach * AttackRangeMultiplier;
            ArmKitBox(new Vector2(0.2f + reach * 0.5f, 0.18f), new Vector2(reach, 0.4f), 0f, PlayerAttackKind.Normal, p.kickDamageScale, 1.3f, 0.05f);
            OneShotSpriteEffect.CreateTweened(KitProjectile.Burst, KitWorld(new Vector2(0.2f + reach * 0.95f, 0.18f)), new Color(0.9f, 0.96f, 1f, 0.8f), duration: 0.12f, startScale: 0.2f, endScale: 0.5f, sortingOrder: RenderOrder.SlashFx);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(1);
            t = 0f;
            while (t < p.kickActive * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            DisarmKitBox(token);
            t = 0f;
            while (t < p.kickRecovery * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }
}
