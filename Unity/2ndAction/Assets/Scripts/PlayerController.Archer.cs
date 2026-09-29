using System.Collections;
using UnityEngine;

// ===== 6人目 弓使い(ARCHER / SNIPER、役割 CHARGE) 2026-09-27 ===== //
//  前: チャージショット。前の射撃から時間が空くほど強い矢になる「自動引き絞り」
//      (長押しは脱出の長押し操作と衝突するため)。短い=普通の矢 / 中=高威力 / 最大=高威力+
//      複数貫通+強いHitStop。連打すると弱い矢しか出ない=連射キャラにしない。
//  後: 素早い後方射撃(弱め)。
//  上: ジャンプしながら斜め上へ。拳銃士のUp Shotより遅く・重く・ノックバックが大きい。
//  下: 空中=斜め下への強い矢(ごく短い滞空のみ、1回の滞空で2回まで) / 地上=低い矢。
//  弱点: 足が遅い・引き絞りの間は無防備。強み: 威力・射程・貫通・ノックバック。
public partial class PlayerController
{
    float archerLastShotTime;
    int archerDownShotsUsed;
    SpriteRenderer archerChargeGlow;
    int archerShownStage = -1;
    public int ArcherLastShotStage { get; private set; } = -1;
    public int ArcherShotsFired { get; private set; }

    // 現在の引き絞り段階(0=通常/1=中/2=最大)。
    public int ArcherChargeStage
    {
        get
        {
            if (kit != CharacterKit.Archer || kitDef == null) return 0;
            float held = Time.time - archerLastShotTime;
            var p = kitDef.archer;
            return held >= p.chargeMaxTime ? 2 : held >= p.chargeMidTime ? 1 : 0;
        }
    }
    public float ArcherCharge01 => kit == CharacterKit.Archer && kitDef != null ? Mathf.Clamp01((Time.time - archerLastShotTime) / Mathf.Max(0.01f, kitDef.archer.chargeMaxTime)) : 0f;

    void HandleArcherInput(FlickDirection f)
    {
        switch (f)
        {
            case FlickDirection.Forward:
                if (isAttacking || attackCooldownTimer > 0f) return;
                StartCoroutine(ArcherForward());
                break;
            case FlickDirection.Backward:
                if (isAttacking || attackCooldownTimer > 0f) return;
                StartCoroutine(ArcherBack());
                break;
            case FlickDirection.Down:
                if (!canUseDownAttack || isAttacking) return;
                if (!isGrounded)
                {
                    if (archerDownShotsUsed < kitDef.archer.downShotsPerAirtime) StartCoroutine(ArcherAirDown());
                }
                else if (attackCooldownTimer <= 0f) StartCoroutine(ArcherLowShot());
                break;
        }
    }

    IEnumerator ArcherForward()
    {
        var p = kitDef.archer;
        int gen = attackGeneration;
        int token = BeginKitMove("draw", true);
        try
        {
            int stage = ArcherChargeStage;
            float s = AttackSpeedMultiplier;
            float windup = (stage >= 2 ? p.forwardWindupMax : p.forwardWindup) * s;
            float recovery = p.forwardRecovery * s;
            attackCooldownTimer = windup + recovery;
            float t = 0f;
            while (t < windup)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            SetKitPose("release", 0);
            AttackStep(kitForwardStep, 0.12f); // 2026-09-30: 前攻撃で前進
            FireArcherArrow(stage);
            t = 0f;
            while (t < recovery)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    void FireArcherArrow(int stage)
    {
        var p = kitDef.archer;
        int i = Mathf.Clamp(stage, 0, 2);
        float speed = Mathf.Lerp(p.arrowSpeed, p.arrowSpeedMax, i / 2f);
        float sc = ArcherPick(p.chargeArrowScale, i, 1f);
        Color tint = i == 0 ? Color.white : i == 1 ? new Color(1f, 0.92f, 0.7f) : new Color(1f, 0.85f, 0.45f);
        Color trail = i == 0 ? new Color(1f, 1f, 1f, 0.35f) : i == 1 ? new Color(1f, 0.85f, 0.45f, 0.7f) : new Color(1f, 0.78f, 0.3f, 0.95f);
        Vector3 pos = KitWorld(p.muzzle);
        var proj = KitProjectile.Create(KitProjectile.Arrow, pos, new Vector2(speed, 0f), p.arrowLifetime,
            new Vector2(0.95f * sc, 0.95f * sc), new Vector2(0.8f * sc, 0.16f + 0.06f * i), tint, PlayerAttackKind.Normal,
            ArcherPick(p.chargeDamageScale, i, 1f) , ArcherPick(p.chargeKnockbackScale, i, 1f), ArcherPick(p.chargeHitStop, i, 0f), trail);
        proj.pierce = i >= 2 ? p.maxChargePierce : 0;
        proj.bossHitStop = i >= 2 ? ArcherPick(p.chargeHitStop, i, 0f) : 0f;
        proj.name = "ArcherArrow_" + (i == 0 ? "Normal" : i == 1 ? "Mid" : "Max");
        ArcherAfterShot(i);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(i + 1);
        if (i >= 1)
        {
            // 放った瞬間の手応え(最大段階は光の輪+画面の揺れ)
            OneShotSpriteEffect.CreateTweened(KitProjectile.Burst, pos, new Color(1f, 0.85f, 0.45f, 0.9f), duration: 0.16f, startScale: 0.25f, endScale: 0.55f + 0.25f * i, sortingOrder: RenderOrder.SlashFx);
            if (i >= 2)
            {
                OneShotSpriteEffect.CreateTweened(KitProjectile.Ring, pos, new Color(1f, 0.9f, 0.55f, 0.9f), duration: 0.22f, startScale: 0.2f, endScale: 1.1f, sortingOrder: RenderOrder.SlashFx);
                KitShake(0.07f, 0.1f);
            }
        }
    }

    static float ArcherPick(float[] a, int i, float fallback) => a == null || a.Length == 0 ? fallback : a[Mathf.Clamp(i, 0, a.Length - 1)];

    void ArcherAfterShot(int stage)
    {
        archerLastShotTime = Time.time;
        ArcherLastShotStage = stage;
        ArcherShotsFired++;
    }

    IEnumerator ArcherBack()
    {
        var p = kitDef.archer;
        int gen = attackGeneration;
        int token = BeginKitMove("draw", true);
        try
        {
            transform.localScale = new Vector3(-1f, 1f, 1f); // 振り向いて後ろへ
            float s = AttackSpeedMultiplier;
            attackCooldownTimer = (p.backWindup + p.backRecovery) * s;
            float t = 0f;
            while (t < p.backWindup * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            SetKitPose("release", 0);
            AttackStep(-kitBackStep, 0.16f); // 2026-09-30: 後ろ攻撃で後退
            Vector3 pos = KitWorld(p.muzzle);
            var proj = KitProjectile.Create(KitProjectile.Arrow, pos, new Vector2(-p.arrowSpeed, 0f), p.arrowLifetime * 0.8f,
                new Vector2(0.85f, 0.85f), new Vector2(0.72f, 0.16f), Color.white, PlayerAttackKind.Normal,
                p.backDamageScale, p.backKnockbackScale, 0f, new Color(1f, 1f, 1f, 0.35f));
            proj.name = "ArcherArrow_Back";
            ArcherAfterShot(0);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(1);
            t = 0f;
            while (t < p.backRecovery * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    // 上攻撃: FireJumpがジャンプを出した直後。isAttackingは持たない(ジャンプ中に前/後を撃てる)。
    void ArcherUpShot()
    {
        var p = kitDef.archer;
        float rad = p.upAngle * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        Vector3 pos = KitWorld(p.muzzle + new Vector2(0f, 0.15f));
        var proj = KitProjectile.Create(KitProjectile.Arrow, pos, dir * p.upArrowSpeed, p.arrowLifetime,
            new Vector2(1.05f, 1.05f), new Vector2(0.85f, 0.2f), new Color(1f, 0.95f, 0.8f), PlayerAttackKind.Normal,
            p.upDamageScale, p.upKnockbackScale, p.upHitStop, new Color(1f, 0.9f, 0.6f, 0.6f));
        proj.name = "ArcherArrow_Up";
        ArcherAfterShot(0);
        FlashKitPose("upshot", 0.3f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
    }

    // 空中の下: 一瞬だけ落下を止めて、斜め下へ強い矢。
    IEnumerator ArcherAirDown()
    {
        var p = kitDef.archer;
        int gen = attackGeneration;
        int token = BeginKitMove("downshot", true);
        archerDownShotsUsed++;
        try
        {
            kitVerticalVelocity = 0f;
            float t = 0f;
            while (t < 0.05f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            float rad = p.downAngle * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            Vector3 pos = KitWorld(p.muzzle + new Vector2(0.05f, -0.1f));
            var proj = KitProjectile.Create(KitProjectile.Arrow, pos, dir * p.downArrowSpeed, p.arrowLifetime,
                new Vector2(1.05f, 1.05f), new Vector2(0.85f, 0.2f), new Color(1f, 0.92f, 0.75f), PlayerAttackKind.Down,
                p.downDamageScale, p.downKnockbackScale, p.downHitStop, new Color(1f, 0.85f, 0.5f, 0.65f));
            proj.stopAtGround = true;
            proj.onGround = (pr, at) => OneShotSpriteEffect.CreateTweened(KitProjectile.Puff, at, new Color(0.85f, 0.8f, 0.72f, 0.7f), duration: 0.3f, startScale: 0.2f, endScale: 0.55f, sortingOrder: RenderOrder.CombatFx);
            proj.name = "ArcherArrow_Down";
            ArcherAfterShot(0);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
            t = 0f;
            while (t < p.downHangTime)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            kitVerticalVelocity = null;
            t = 0f;
            while (t < 0.12f && !isGrounded)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    // 地上の下: 片膝をついて低い矢(背の低い敵/地を這う敵向け)。
    IEnumerator ArcherLowShot()
    {
        var p = kitDef.archer;
        int gen = attackGeneration;
        int token = BeginKitMove("lowshot", true);
        try
        {
            float s = AttackSpeedMultiplier;
            attackCooldownTimer = (0.08f + p.forwardRecovery) * s;
            float t = 0f;
            while (t < 0.08f * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            Vector3 pos = KitWorld(new Vector2(p.muzzle.x, p.lowShotHeight));
            var proj = KitProjectile.Create(KitProjectile.Arrow, pos, new Vector2(p.arrowSpeed, 0f), p.arrowLifetime,
                new Vector2(0.95f, 0.95f), new Vector2(0.8f, 0.2f), Color.white, PlayerAttackKind.Normal,
                p.lowDamageScale, p.lowKnockbackScale, 0.03f, new Color(1f, 1f, 1f, 0.4f));
            proj.name = "ArcherArrow_Low";
            ArcherAfterShot(0);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(1);
            t = 0f;
            while (t < p.forwardRecovery * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    // 引き絞りの見た目: 弓の位置の光が段階ごとに強くなる(最大になった瞬間に光の輪)。
    void ArcherUpdate()
    {
        if (KitProjectile.Orb == null) return;
        if (archerChargeGlow == null)
        {
            var go = new GameObject("ArcherChargeGlow");
            go.transform.SetParent(transform, false);
            archerChargeGlow = go.AddComponent<SpriteRenderer>();
            archerChargeGlow.sprite = KitProjectile.Orb;
            archerChargeGlow.sortingOrder = RenderOrder.SlashFx;
        }
        bool show = !IsReacting && !hasDied && !IsFinishing && !IsPreparingStart;
        int stage = ArcherChargeStage;
        if (!show || stage == 0)
        {
            archerChargeGlow.enabled = false;
            archerShownStage = stage;
            return;
        }
        var p = kitDef.archer;
        archerChargeGlow.enabled = true;
        archerChargeGlow.transform.localPosition = new Vector3(p.muzzle.x, p.muzzle.y, 0f);
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * (stage >= 2 ? 14f : 7f));
        float size = stage >= 2 ? 0.42f + 0.06f * pulse : 0.26f + 0.03f * pulse;
        archerChargeGlow.transform.localScale = new Vector3(size, size, 1f);
        archerChargeGlow.color = stage >= 2 ? new Color(1f, 0.82f, 0.35f, 0.75f + 0.2f * pulse) : new Color(1f, 0.95f, 0.75f, 0.45f);
        if (stage >= 2 && archerShownStage < 2)
            OneShotSpriteEffect.CreateTweened(KitProjectile.Ring, KitWorld(p.muzzle), new Color(1f, 0.85f, 0.4f, 0.9f), duration: 0.25f, startScale: 0.15f, endScale: 0.8f, sortingOrder: RenderOrder.SlashFx);
        archerShownStage = stage;
    }
}
