using System.Collections;
using UnityEngine;

// ===== 9人目 忍者(NINJA / SHINOBI、役割 EVASION / MOBILITY) 2026-09-27 ===== //
// 「速い連撃」ではなく「位置を素早く変える」キャラ。
//  前: 瞬身。前方へ一瞬で踏み込み、通り抜けた敵を抜刀斬り(すり抜け)。無敵はごく短時間だけで、
//      無敵が付くのは一定間隔(invincibleInterval)ごと=連打しても永久無敵にはならない。
//  後: 手裏剣(後方へ2枚)+小さく後ろへ跳ぶ。
//  上: 斜め上への跳躍斬り(ジャンプ回数を消費=空中での使用回数に制限)。
//  下: 空中=煙を残して斜め下へ急降下斬り(着地でも斬る) / 地上=低い滑り斬り。
//  LIFEは低め、火力は中程度。
public partial class PlayerController
{
    float ninjaLastIFrameTime = -99f;
    public int NinjaDashCount { get; private set; }
    public int NinjaIFrameGrants { get; private set; }

    void HandleNinjaInput(FlickDirection f)
    {
        switch (f)
        {
            case FlickDirection.Forward:
                if (isAttacking || attackCooldownTimer > 0f || kitDiveActive) return;
                StartCoroutine(NinjaDash());
                break;
            case FlickDirection.Backward:
                if (isAttacking || attackCooldownTimer > 0f || kitDiveActive) return;
                StartCoroutine(NinjaShuriken());
                break;
            case FlickDirection.Down:
                if (!canUseDownAttack || kitDiveActive) return;
                if (!isGrounded) { if (!isAttacking) StartCoroutine(NinjaDownDash()); }
                else if (!isAttacking && attackCooldownTimer <= 0f) StartCoroutine(NinjaSlide());
                break;
        }
    }

    static readonly Color NinjaCrimson = new Color(1f, 0.45f, 0.45f, 0.9f);

    // 残像(今の絵を半透明で置いていく)。
    void NinjaAfterImage(float alpha)
    {
        if (sr == null || sr.sprite == null) return;
        var fx = OneShotSpriteEffect.CreateTweened(sr.sprite, sr.transform.position, new Color(0.55f, 0.6f, 1f, alpha), duration: 0.22f, startScale: 1f, endScale: 1f, sortingOrder: RenderOrder.Player - 1);
        if (fx != null) fx.transform.localScale = sr.transform.lossyScale;
    }

    IEnumerator NinjaDash()
    {
        var p = kitDef.ninja;
        int gen = attackGeneration;
        int token = BeginKitMove("dashslash", true);
        try
        {
            NinjaDashCount++;
            attackCooldownTimer = p.dashCooldown * AttackSpeedMultiplier;
            // 無敵は一定間隔ごとにだけ付ける(連打で永久無敵にしない)。
            if (Time.time - ninjaLastIFrameTime >= p.invincibleInterval)
            {
                kitIFrameTimer = Mathf.Max(kitIFrameTimer, p.dashInvincible);
                ninjaLastIFrameTime = Time.time;
                NinjaIFrameGrants++;
            }
            if (!isGrounded) kitVerticalVelocity = 0f; // 空中の瞬身は水平に
            float dist = p.dashDistance;
            lungeVelocityX = dist / Mathf.Max(0.01f, p.dashTime);
            OneShotSpriteEffect.CreateTweened(KitProjectile.Puff, transform.position + new Vector3(0f, 0.5f, 0f), new Color(0.35f, 0.35f, 0.42f, 0.6f), duration: 0.3f, startScale: 0.35f, endScale: 0.8f, sortingOrder: RenderOrder.CombatFx);
            // 体の後ろ〜前をまとめて覆う判定(踏み込みながら通り抜けた敵を斬る)
            ArmKitBox(new Vector2(-0.3f, p.slashHeight), new Vector2(1.9f, p.slashThickness), 0f, PlayerAttackKind.Normal, p.slashDamageScale, p.slashKnockbackScale, p.slashHitStop);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
            float t = 0f, img = 0f;
            while (t < p.dashTime)
            {
                if (!KitAlive(gen, token)) yield break;
                img -= Time.deltaTime;
                if (img <= 0f) { NinjaAfterImage(0.45f); img = 0.035f; }
                t += Time.deltaTime; yield return null;
            }
            lungeVelocityX = 0f;
            SetKitPose("slashend", 0);
            OneShotSpriteEffect.CreateTweened(KitProjectile.Slash, KitWorld(new Vector2(-dist * 0.45f, p.slashHeight)), new Color(1f, 1f, 1f, 0.9f), duration: 0.18f, startScale: dist * 0.45f, endScale: dist * 0.6f, rotationDegrees: 0f, sortingOrder: RenderOrder.SlashFx, holdFraction: 0.3f);
            OneShotSpriteEffect.CreateTweened(KitProjectile.Slash, KitWorld(new Vector2(-dist * 0.45f, p.slashHeight)), NinjaCrimson, duration: 0.22f, startScale: dist * 0.3f, endScale: dist * 0.5f, rotationDegrees: 8f, sortingOrder: RenderOrder.SlashFx);
            t = 0f;
            while (t < 0.05f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            DisarmKitBox(token);
            kitVerticalVelocity = null;
            t = 0f;
            while (t < p.dashRecovery * AttackSpeedMultiplier)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    IEnumerator NinjaShuriken()
    {
        var p = kitDef.ninja;
        int gen = attackGeneration;
        int token = BeginKitMove("throw", true);
        try
        {
            transform.localScale = new Vector3(-1f, 1f, 1f);
            attackCooldownTimer = p.shurikenCooldown * AttackSpeedMultiplier;
            lungeVelocityX = -p.backHop / 0.12f;
            Vector3 pos = KitWorld(new Vector2(0.35f, 0.7f));
            for (int i = 0; i < 2; i++)
            {
                float ang = 180f + (i == 0 ? 3f : -5f);
                Vector2 v = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad)) * p.shurikenSpeed;
                var proj = KitProjectile.Create(KitProjectile.ShurikenSprite(), pos + new Vector3(0f, i * 0.12f, 0f), v, p.shurikenLifetime,
                    new Vector2(0.32f, 0.32f), new Vector2(0.28f, 0.28f), Color.white, PlayerAttackKind.Normal,
                    p.shurikenDamageScale, 0.7f, 0f, new Color(0.8f, 0.85f, 1f, 0.35f), false);
                proj.spin = 1440f;
                proj.name = "NinjaShuriken";
            }
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(1);
            float t = 0f;
            while (t < 0.12f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            lungeVelocityX = 0f;
            t = 0f;
            while (t < 0.12f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    // 上: FireJumpのジャンプを「斜め上への跳躍斬り」に置き換える(ジャンプ回数を消費=空中で使える回数に制限)。
    IEnumerator NinjaUpDash()
    {
        var p = kitDef.ninja;
        velocityY = p.upDashSpeedY;
        int gen = attackGeneration;
        int token = BeginKitMove("updash", false);
        try
        {
            kitPoseTimer = p.upDashTime + 0.15f;
            lungeVelocityX = p.upDashDistanceX / Mathf.Max(0.01f, p.upDashTime);
            ArmKitBox(new Vector2(0.45f, 0.85f), new Vector2(1.3f * AttackRangeMultiplier, 1.4f), 35f, PlayerAttackKind.Normal, p.upDamageScale, 1f, 0.03f);
            OneShotSpriteEffect.CreateTweened(KitProjectile.Slash, KitWorld(new Vector2(0.45f, 0.95f)), new Color(1f, 1f, 1f, 0.85f), duration: 0.16f, startScale: 0.6f, endScale: 1f, rotationDegrees: KitFacing > 0f ? 40f : 140f, sortingOrder: RenderOrder.SlashFx);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
            float t = 0f, img = 0f;
            while (t < p.upDashTime)
            {
                if (!KitAlive(gen, token)) yield break;
                img -= Time.deltaTime;
                if (img <= 0f) { NinjaAfterImage(0.35f); img = 0.05f; }
                t += Time.deltaTime; yield return null;
            }
            lungeVelocityX = 0f;
            DisarmKitBox(token);
            t = 0f;
            while (t < 0.15f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    IEnumerator NinjaDownDash()
    {
        var p = kitDef.ninja;
        int gen = attackGeneration;
        int token = BeginKitMove("downdash", true);
        try
        {
            // 開始位置に煙(ここから消えたように見せる)
            OneShotSpriteEffect.CreateScatterBurst(KitProjectile.Puff, transform.position + new Vector3(0f, 0.6f, 0f), new Color(0.32f, 0.32f, 0.4f, 0.7f), 5, 0.4f, 0.3f, 0.6f, 1.6f, 1.5f, RenderOrder.CombatFx);
            kitDiveActive = true;
            kitVerticalVelocity = -p.downDashSpeedY;
            lungeVelocityX = p.downDashSpeedX;
            ArmKitBox(new Vector2(0.3f, 0.45f), new Vector2(1.0f, 1.0f), -40f, PlayerAttackKind.Down, p.downDamageScale, 1.2f, 0.04f);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
            float t = 0f, img = 0f;
            while (!kitDiveLanded && t < p.downDashMaxTime)
            {
                if (!KitAlive(gen, token)) yield break;
                img -= Time.deltaTime;
                if (img <= 0f) { NinjaAfterImage(0.4f); img = 0.04f; }
                t += Time.deltaTime; yield return null;
            }
            kitDiveActive = false;
            kitVerticalVelocity = null;
            lungeVelocityX = 0f;
            DisarmKitBox(token);
            if (!kitDiveLanded) yield break; // 着地しないまま(穴の上など)は何もしない
            SetKitPose("downdash", 1);
            KitBlast.Create(transform.position + new Vector3(0.2f, 0.45f, 0f), new KitBlast.Spec { radius = p.landSlashRadius * AttackRangeMultiplier, active = 0.08f, kind = PlayerAttackKind.DownImpact, damageScale = p.landSlashDamageScale, knockbackScale = 1.2f, hitStop = 0.04f, fx = KitProjectile.Slash, tint = new Color(1f, 1f, 1f, 0.9f), fxScale = 0.9f, shake = 0.05f });
            OneShotSpriteEffect.CreateTweened(KitProjectile.Slash, transform.position + new Vector3(0.2f, 0.45f, 0f), NinjaCrimson, duration: 0.2f, startScale: 0.6f, endScale: 1.2f, rotationDegrees: 180f, sortingOrder: RenderOrder.SlashFx);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayLand();
            attackCooldownTimer = 0.15f;
            t = 0f;
            while (t < 0.14f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    IEnumerator NinjaSlide()
    {
        var p = kitDef.ninja;
        int gen = attackGeneration;
        int token = BeginKitMove("downdash", true);
        try
        {
            attackCooldownTimer = (p.slideTime + 0.2f) * AttackSpeedMultiplier;
            lungeVelocityX = p.slideDistance / Mathf.Max(0.01f, p.slideTime);
            ArmKitBox(new Vector2(0.2f, 0.25f), new Vector2(1.3f, 0.5f), 0f, PlayerAttackKind.Normal, p.slashDamageScale, 1.2f, 0.03f);
            OneShotSpriteEffect.CreateTweened(KitProjectile.Puff, transform.position + new Vector3(-0.2f, 0.1f, 0f), new Color(0.85f, 0.82f, 0.75f, 0.5f), duration: 0.25f, startScale: 0.2f, endScale: 0.55f, sortingOrder: RenderOrder.CombatFx);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(1);
            float t = 0f;
            while (t < p.slideTime)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            lungeVelocityX = 0f;
            DisarmKitBox(token);
            SetKitPose("downdash", 1);
            t = 0f;
            while (t < 0.18f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }
}
