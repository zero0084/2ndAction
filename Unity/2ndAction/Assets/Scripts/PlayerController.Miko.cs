using System.Collections;
using UnityEngine;

// ===== 10人目 巫女(SHRINE MAIDEN / MIKO、役割 SEALER) 2026-09-28 ===== //
// 御札・式神・結界を「置いて」、敵が通る場所そのものを支配する。即効性は低い代わりに、敵の進路を
// 読めば大量の敵を結界へ巻き込める(魔法使い=自分から撃ち込む、との違い)。
//  前: 御札。命中した敵に貼り付き(Seal)、時間差で浄化爆発。貼られた敵へ2枚目を当てると即、大きく爆発。
//  後: 式神(紙の鳥)を後方へ。ゆらゆら進み、最初に触れた敵へダメージ。
//  上: ジャンプしながら御札を扇状に3枚(空中の敵の迎撃、1枚ずつは弱い)。
//  下: 結界。前方の地面に光の壁を張り、中の敵を遅くして少しずつダメージ(同時に1つ、置き直すと古い方は消える)。
public partial class PlayerController
{
    public int MikoOfudaThrown { get; private set; }
    public int MikoBarriersPlaced { get; private set; }

    void HandleMikoInput(FlickDirection f)
    {
        switch (f)
        {
            case FlickDirection.Forward:
                if (isAttacking || attackCooldownTimer > 0f) return;
                StartCoroutine(MikoOfuda());
                break;
            case FlickDirection.Backward:
                if (isAttacking || attackCooldownTimer > 0f) return;
                StartCoroutine(MikoShikigami());
                break;
            case FlickDirection.Down:
                if (!canUseDownAttack || isAttacking || attackCooldownTimer > 0f) return;
                StartCoroutine(MikoBarrier());
                break;
        }
    }

    KitBlast.Spec MikoBurst(bool big)
    {
        var p = kitDef.miko;
        return new KitBlast.Spec
        {
            radius = (big ? p.detonateRadius : p.burstRadius) * AttackRangeMultiplier,
            active = 0.1f,
            kind = PlayerAttackKind.Normal,
            damageScale = big ? p.detonateDamageScale : p.burstDamageScale,
            knockbackScale = big ? 1.6f : 1.1f,
            hitStop = big ? 0.08f : 0.04f,
            fx = KitProjectile.Burst,
            tint = new Color(1f, 0.92f, 0.6f, 0.95f),
            fxScale = 1.1f,
            shake = big ? 0.08f : 0.04f,
        };
    }

    KitProjectile MikoThrowOfuda(Vector2 localFrom, float angleDeg, float speed, float damageScale, bool seals)
    {
        var p = kitDef.miko;
        float rad = angleDeg * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(rad) * KitFacing, Mathf.Sin(rad));
        var proj = KitProjectile.Create(KitArt.OfudaSprite(), KitWorld(localFrom), dir * speed, p.ofudaLifetime,
            new Vector2(0.48f, 0.48f), new Vector2(0.34f, 0.3f), Color.white, PlayerAttackKind.Normal,
            damageScale, 0.6f, 0f, new Color(1f, 0.85f, 0.55f, 0.45f), false);
        proj.spin = 720f;
        proj.name = seals ? "MikoOfuda" : "MikoOfudaFan";
        if (seals)
        {
            // 命中した敵に御札を貼る(貼られている敵なら即爆発)
            proj.onHitEnemy = (pr, other) =>
            {
                Component target = (Component)other.GetComponentInParent<EnemyController>() ?? other.GetComponentInParent<WildBossBase>();
                if (target == null) return;
                KitSealMark.Attach(target.gameObject, p.markDelay, MikoBurst(false), MikoBurst(true));
            };
        }
        MikoOfudaThrown++;
        return proj;
    }

    IEnumerator MikoOfuda()
    {
        var p = kitDef.miko;
        int gen = attackGeneration;
        int token = BeginKitMove("throw", true);
        try
        {
            float s = AttackSpeedMultiplier;
            attackCooldownTimer = p.ofudaCooldown * s;
            float t = 0f;
            while (t < p.ofudaWindup * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            SetKitPose("throw", 1);
            AttackStep(kitForwardStep, 0.12f); // 2026-09-30: 前攻撃で前進
            MikoThrowOfuda(new Vector2(0.5f, 0.8f), 0f, p.ofudaSpeed, p.ofudaDamageScale, true);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(1);
            t = 0f;
            while (t < (p.ofudaCooldown - p.ofudaWindup) * s * 0.7f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    IEnumerator MikoShikigami()
    {
        var p = kitDef.miko;
        int gen = attackGeneration;
        int token = BeginKitMove("shiki", true);
        try
        {
            transform.localScale = new Vector3(-1f, 1f, 1f); // 振り向いて後ろへ放つ
            float s = AttackSpeedMultiplier;
            attackCooldownTimer = p.shikiCooldown * s;
            float t = 0f;
            while (t < 0.1f * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            AttackStep(-kitBackStep, 0.16f); // 2026-09-30: 後ろ攻撃で後退
            var proj = KitProjectile.Create(KitArt.PaperBirdSprite(), KitWorld(new Vector2(0.4f, 0.85f)), new Vector2(-p.shikiSpeed, 0f), p.shikiLifetime,
                new Vector2(-0.7f, 0.55f), new Vector2(0.5f, 0.35f), Color.white, PlayerAttackKind.Normal,
                p.shikiDamageScale, 1f, 0.03f, new Color(1f, 1f, 1f, 0.35f), false);
            proj.wobbleAmp = 0.18f; proj.wobbleFreq = 2.2f;
            proj.name = "MikoShikigami";
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(1);
            t = 0f;
            while (t < 0.22f * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    // 上攻撃: FireJumpがジャンプを出した直後。isAttackingは持たない。
    void MikoUpFan()
    {
        var p = kitDef.miko;
        foreach (float a in p.upAngles) MikoThrowOfuda(new Vector2(0.35f, 1.0f), a, p.upSpeed, p.upDamageScale, false);
        FlashKitPose("upfan", 0.3f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
    }

    IEnumerator MikoBarrier()
    {
        var p = kitDef.miko;
        int gen = attackGeneration;
        int token = BeginKitMove("place", true);
        try
        {
            float s = AttackSpeedMultiplier;
            attackCooldownTimer = (p.barrierWindup + p.barrierCooldown) * s;
            if (!isGrounded) kitVerticalVelocity = -1.5f; // 空中なら少しだけ落下を遅くして御札を落とす
            float t = 0f;
            while (t < p.barrierWindup * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            // 前方の地面(穴なら足元寄りの地面)に結界を張る
            float x = transform.position.x + p.barrierForward;
            float? g = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(x) : null;
            if (!g.HasValue && TerrainManager.Instance != null) { x = transform.position.x + 1f; g = TerrainManager.Instance.GetHeightAt(x); }
            float gy = g ?? transform.position.y;
            KitZone.Create(KitZone.Kind.Barrier, new Vector3(x, gy, 0f), p.barrierWidth * AttackRangeMultiplier, p.barrierHeight,
                p.barrierDuration, p.barrierTick, p.barrierTickDamageScale, p.barrierSlow);
            MikoBarriersPlaced++;
            SetKitPose("place", 1);
            if (KitProjectile.Ring != null)
                OneShotSpriteEffect.CreateTweened(KitProjectile.Ring, new Vector3(x, gy + 0.1f, 0f), new Color(1f, 0.85f, 0.45f, 0.9f), duration: 0.35f, startScale: 0.3f, endScale: p.barrierWidth, sortingOrder: RenderOrder.SlashFx);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
            t = 0f;
            while (t < 0.22f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    // Run終了/死亡/復帰/キャラ切り替えで、置いた結界と貼った御札を必ず片付ける。
    static void MikoCleanup()
    {
        KitZone.ClearKind(KitZone.Kind.Barrier);
        KitSealMark.ClearAll();
    }
}
