using System.Collections;
using UnityEngine;

// ===== 7人目 魔法使い(MAGE / SORCERER、役割 FLIGHT / MAGIC) 2026-09-27 ===== //
// 地面から少し浮いたまま自動前進する。ジャンプは無く、代わりに
//  上: 高度を1段上げる+斜め上への雷撃(命中で小爆発)
//  下: 高度を1段下げる+真下への魔法(地面/敵で爆発)
//  前: 魔法弾(命中で小爆発) / 後: 弱い魔法弾(爆発なし)
// 高度は「足元の地面(無ければ直前の地面)」基準なので穴を越えられる。ただし洞窟の天井
// (TerrainManager.GetCeilingLimitY)で頭打ちになり、天井の針(IsCeilingSpikeHit)にも普通に当たる
// (Move()共通の後処理FinishMoveをそのまま通すため、天井ダメージの判定は他キャラと全く同じ)。
// 被弾/復帰/死亡では高度を最低段へ戻す(OnKitRespawn)。
public partial class PlayerController
{
    int mageLevel;
    float mageVelY;
    float mageSurface;
    bool mageSurfaceKnown;
    float mageBobT;
    public int MageAltitudeLevel => mageLevel;
    public bool MageCeilingBlocked { get; private set; }
    // 高度変更の総数(テスト/デバッグ用)
    public int MageAltitudeChanges { get; private set; }

    void MageReset()
    {
        mageLevel = 0;
        mageVelY = 0f;
        mageSurfaceKnown = false;
        MageCeilingBlocked = false;
    }

    // Move()から(魔法使いの間だけ、ジャンプ/重力/着地の代わりにこれを使う)。
    void MageFlightMove(float dt, float newX, float prevY, float? groundHeight, float? skyHeight)
    {
        var p = kitDef.mage;
        // 基準の地面: 空中ルート(上ルート)の上にいればそちら、無ければ下の地面、どちらも無い(穴の上)なら直前の地面。
        float? surf = null;
        if (skyHeight.HasValue && prevY >= skyHeight.Value + groundOffset - 0.15f) { surf = skyHeight; onSky = true; }
        else if (groundHeight.HasValue) { surf = groundHeight; onSky = false; }
        if (surf.HasValue) { mageSurface = surf.Value; mageSurfaceKnown = true; }
        float baseY = (mageSurfaceKnown ? mageSurface : prevY - p.hoverBase) + groundOffset;

        mageBobT += dt;
        float bob = Mathf.Sin(mageBobT * Mathf.PI * 2f * p.bobFrequency) * p.bobAmplitude;
        float target = baseY + p.hoverBase + mageLevel * p.altitudeStep + bob;
        float newY = Mathf.SmoothDamp(prevY, target, ref mageVelY, Mathf.Max(0.01f, p.altitudeSmoothTime), Mathf.Infinity, Mathf.Max(0.0001f, dt));
        // 上り坂で地面に潜らない(足元は常に地面より上)。
        if (surf.HasValue && newY < surf.Value + groundOffset) { newY = surf.Value + groundOffset; if (mageVelY < 0f) mageVelY = 0f; }

        // 洞窟の天井: 他キャラと同じ上限で頭打ち(ダメージ無し)。天井の針はFinishMoveの共通判定で当たる。
        MageCeilingBlocked = false;
        float? caveLimitY = TerrainManager.Instance != null ? TerrainManager.Instance.GetCeilingLimitY(newX) : null;
        if (caveLimitY.HasValue && newY > caveLimitY.Value)
        {
            float lim = caveLimitY.Value;
            if (surf.HasValue) lim = Mathf.Max(lim, surf.Value + groundOffset);
            newY = Mathf.Min(newY, lim);
            if (mageVelY > 0f) mageVelY = 0f;
            MageCeilingBlocked = true;
        }

        velocityY = mageVelY;
        isGrounded = mageLevel == 0 && surf.HasValue;
        jumpsUsed = 0;
        FinishMove(newX, newY, dt, tilt: false);
    }

    void MageFinishTick(float dt)
    {
        mageLevel = 0;
        float? g = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(transform.position.x) : null;
        if (!g.HasValue) return;
        float target = g.Value + groundOffset + kitDef.mage.hoverBase;
        float y = Mathf.SmoothDamp(transform.position.y, target, ref mageVelY, 0.12f, Mathf.Infinity, Mathf.Max(0.0001f, dt));
        transform.position = new Vector3(transform.position.x, y, 0f);
    }

    void HandleMageInput(FlickDirection f)
    {
        var p = kitDef.mage;
        switch (f)
        {
            case FlickDirection.Up:
                if (mageLevel < p.maxAltitudeLevel) { mageLevel++; MageAltitudeChanges++; }
                if (canUseUpAttack && attackCooldownTimer <= 0f && !isAttacking) MageCastUp();
                break;
            case FlickDirection.Down:
                if (mageLevel > 0) { mageLevel--; MageAltitudeChanges++; }
                if (canUseDownAttack && attackCooldownTimer <= 0f && !isAttacking) MageCastDown();
                break;
            case FlickDirection.Forward:
                if (isAttacking || attackCooldownTimer > 0f) return;
                StartCoroutine(MageBolt(false));
                break;
            case FlickDirection.Backward:
                if (isAttacking || attackCooldownTimer > 0f) return;
                StartCoroutine(MageBolt(true));
                break;
        }
    }

    static readonly Color MageViolet = new Color(0.72f, 0.6f, 1f, 1f);
    static readonly Color MageCyan = new Color(0.55f, 0.9f, 1f, 1f);
    // 前/後の魔法弾を放つ位置。最低高度で地上の敵(高さ約1.2)に確実に当たる高さ。高度を上げると地上の敵の上を
    // 越えていく(その時は下の魔法で狙う)=高度の上げ下げに意味を持たせる。
    Vector2 MageStaff => new Vector2(0.5f, 0.5f);

    IEnumerator MageBolt(bool back)
    {
        var p = kitDef.mage;
        int gen = attackGeneration;
        int token = BeginKitMove("cast", true);
        try
        {
            if (back) transform.localScale = new Vector3(-1f, 1f, 1f);
            float s = AttackSpeedMultiplier;
            attackCooldownTimer = p.boltCooldown * s;
            float t = 0f;
            while (t < p.boltCastTime * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            Vector3 pos = KitWorld(MageStaff);
            float dirX = back ? -1f : 1f;
            KitProjectile proj;
            if (!back)
            {
                proj = KitProjectile.Create(KitProjectile.Orb, pos, new Vector2(p.boltSpeed * dirX, 0f), p.boltLifetime,
                    new Vector2(0.5f, 0.5f), new Vector2(0.36f, 0.36f), MageCyan, PlayerAttackKind.Normal,
                    p.boltDamageScale, 1f, 0f, new Color(0.6f, 0.8f, 1f, 0.7f), false);
                proj.blast = new KitBlast.Spec { radius = p.blastRadius, active = 0.1f, kind = PlayerAttackKind.Normal, damageScale = p.blastDamageScale, knockbackScale = p.blastKnockbackScale, hitStop = 0.04f, fx = KitProjectile.Burst, tint = new Color(0.65f, 0.85f, 1f, 0.95f), fxScale = 1.2f, shake = 0.04f };
                proj.name = "MageBolt";
            }
            else
            {
                proj = KitProjectile.Create(KitProjectile.Orb, pos, new Vector2(p.backBoltSpeed * dirX, 0f), p.boltLifetime * 0.7f,
                    new Vector2(0.36f, 0.36f), new Vector2(0.28f, 0.28f), MageViolet, PlayerAttackKind.Normal,
                    p.backDamageScale, 0.8f, 0f, new Color(0.7f, 0.6f, 1f, 0.55f), false);
                proj.name = "MageBolt_Back";
            }
            proj.spin = 360f;
            OneShotSpriteEffect.CreateTweened(KitProjectile.Orb, pos, new Color(0.7f, 0.85f, 1f, 0.8f), duration: 0.14f, startScale: 0.15f, endScale: 0.45f, sortingOrder: RenderOrder.SlashFx);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(back ? 1 : 2);
            SetKitPose("cast", 1);
            t = 0f;
            float recovery = Mathf.Max(0f, p.boltCooldown * s - p.boltCastTime * s) * 0.6f;
            while (t < recovery)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    // 上: 斜め上の雷撃(isAttackingを持たない一瞬のポーズ)。
    void MageCastUp()
    {
        var p = kitDef.mage;
        attackCooldownTimer = p.boltCooldown * AttackSpeedMultiplier;
        float rad = p.upAngle * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        Vector3 pos = KitWorld(MageStaff + new Vector2(0f, 0.15f));
        var proj = KitProjectile.Create(KitProjectile.ThunderSpear, pos, dir * p.upBoltSpeed, p.boltLifetime * 0.8f,
            new Vector2(0.9f, 0.6f), new Vector2(0.7f, 0.3f), new Color(0.85f, 0.8f, 1f), PlayerAttackKind.Normal,
            p.upDamageScale, 1.2f, 0.04f, new Color(0.8f, 0.75f, 1f, 0.6f));
        proj.blast = new KitBlast.Spec { radius = p.upBlastRadius, active = 0.1f, kind = PlayerAttackKind.Normal, damageScale = 1f, knockbackScale = 1.2f, hitStop = 0.04f, fx = KitProjectile.SkyBolt, tint = new Color(0.85f, 0.8f, 1f, 0.95f), fxScale = 1.4f, shake = 0.04f };
        proj.name = "MageBolt_Up";
        FlashKitPose("castUp", 0.28f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
    }

    // 下: 真下へ落ちる魔法(地面か敵で爆発)。
    void MageCastDown()
    {
        var p = kitDef.mage;
        attackCooldownTimer = p.boltCooldown * AttackSpeedMultiplier;
        float rad = p.downAngle * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        Vector3 pos = KitWorld(new Vector2(0.35f, 0.35f));
        var proj = KitProjectile.Create(KitProjectile.Orb, pos, dir * p.downBoltSpeed, 1.2f,
            new Vector2(0.55f, 0.55f), new Vector2(0.4f, 0.4f), MageViolet, PlayerAttackKind.Down,
            p.downDamageScale, 1f, 0.03f, new Color(0.7f, 0.55f, 1f, 0.7f), false);
        proj.stopAtGround = true;
        proj.spin = -540f;
        proj.blast = new KitBlast.Spec { radius = p.downBlastRadius, active = 0.12f, kind = PlayerAttackKind.DownImpact, damageScale = p.downBlastDamageScale, knockbackScale = 1.4f, hitStop = 0.05f, fx = KitProjectile.Burst, tint = new Color(0.75f, 0.6f, 1f, 0.95f), fxScale = 1.25f, shake = 0.08f };
        proj.name = "MageBolt_Down";
        FlashKitPose("castDown", 0.28f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(3);
    }
}
