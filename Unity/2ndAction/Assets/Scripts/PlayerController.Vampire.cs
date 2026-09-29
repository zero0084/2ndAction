using System.Collections;
using UnityEngine;

// ===== 11人目 吸血鬼(VAMPIRE、役割 BLOOD) 2026-09-28 ===== //
// 攻撃を当てるとBlood Gauge(0〜100)が溜まり、戦い続けるほど強くなる。当てない時間が続くと減っていく。
//  50以上: 攻撃が少し強く・速く / 100: Blood Rush(一定時間、攻撃がさらに速く強く、走りも少し速く、
//  命中で微量回復)。回復は「ハート1つ分が貯まったら1回復、かつ一定時間に1回まで」=永久回復にしない。
//  前: 右爪 → 左爪 → 血の斬撃(最後だけ前方へ長い)。 後: バックステップ+後方へコウモリの群れ。
//  上: 霧/コウモリに変化して斜め上へ移動(経路に判定)。 下: 空中=急降下吸血(命中でゲージ大幅増加、
//  ゲージが一定以上なら少量回復) / 地上=低い血の薙ぎ。
//  死亡/復帰/Run開始でゲージとRushは必ず片付ける(OnKitRespawn/ApplyKitStats/CancelKitMoves参照)。
public partial class PlayerController
{
    public float BloodGauge { get; private set; }
    public bool BloodRush => bloodRushTimer > 0f;
    public int BloodRushCount { get; private set; }
    public int VampireHealsGiven { get; private set; }
    float bloodRushTimer, bloodLastHitTime = -99f, bloodHealAcc, bloodNextHealTime;
    int vampireComboStage, vampireNextStage = 1;
    float vampireGraceTimer;
    public int VampireComboStage => vampireComboStage;
    bool vampireDiveHit;
    SpriteRenderer bloodBarBg, bloodBarFill, bloodAura;

    // 技の時間倍率(小さいほど速い)/威力倍率(ゲージ段階とRush)
    float BloodTime => kitDef == null ? 1f : BloodRush ? kitDef.vampire.rushSpeedScale : BloodGauge >= kitDef.vampire.tierThreshold ? kitDef.vampire.tierSpeedScale : 1f;
    float BloodPower => kitDef == null ? 1f : BloodRush ? kitDef.vampire.rushDamageScale : BloodGauge >= kitDef.vampire.tierThreshold ? kitDef.vampire.tierDamageScale : 1f;
    float VampireRunBoost => kit == CharacterKit.Vampire && BloodRush ? kitDef.vampire.rushMoveScale : 1f;
    public float VampireRunMultiplier => VampireRunBoost; // テスト/表示用(CurrentAutoRunSpeedはボスの追従基準なので含めない)

    void VampireReset(bool full)
    {
        bloodRushTimer = 0f;
        if (full) { BloodGauge = 0f; bloodHealAcc = 0f; }
        else BloodGauge = Mathf.Min(BloodGauge, 30f);
        vampireComboStage = 0; vampireNextStage = 1; vampireGraceTimer = 0f;
        if (sr != null && kit == CharacterKit.Vampire) sr.color = Color.white;
    }

    // 吸血鬼の攻撃判定が敵/ボスに当たった瞬間(PlayerAttackInfo.onHit)。
    void OnVampireHit(float gain)
    {
        if (kit != CharacterKit.Vampire || kitDef == null) return;
        var p = kitDef.vampire;
        bloodLastHitTime = Time.time;
        if (BloodRush)
        {
            VampireHealAdd(p.rushHealPerHit);
            return;
        }
        BloodGauge = Mathf.Min(100f, BloodGauge + gain);
        if (BloodGauge >= 100f)
        {
            bloodRushTimer = p.rushDuration;
            BloodRushCount++;
            if (KitProjectile.Ring != null) OneShotSpriteEffect.CreateTweened(KitProjectile.Ring, transform.position + new Vector3(0f, 0.7f, 0f), new Color(1f, 0.15f, 0.2f, 0.95f), duration: 0.35f, startScale: 0.3f, endScale: 2.6f, sortingOrder: RenderOrder.SlashFx);
            KitShake(0.06f, 0.12f);
        }
    }

    // 回復はハート1つ分が貯まり、かつ前回の回復からhealCooldown秒経っている時だけ(永久回復にしない)。
    void VampireHealAdd(float amount)
    {
        var p = kitDef.vampire;
        bloodHealAcc = Mathf.Min(1.5f, bloodHealAcc + amount);
        if (bloodHealAcc >= 1f && Time.time >= bloodNextHealTime && GameManager.Instance != null && GameManager.Instance.Lives < GameManager.Instance.maxLives)
        {
            bloodHealAcc -= 1f;
            bloodNextHealTime = Time.time + p.healCooldown;
            VampireHealsGiven++;
            GameManager.Instance.KitHeal(1);
            OneShotSpriteEffect.CreateTweened(KitProjectile.Orb, transform.position + new Vector3(0f, 1.0f, 0f), new Color(1f, 0.3f, 0.35f, 0.9f), duration: 0.4f, startScale: 0.2f, endScale: 0.8f, sortingOrder: RenderOrder.SlashFx);
        }
    }

    void VampireUpdate()
    {
        var p = kitDef.vampire;
        float dt = Time.deltaTime;
        if (vampireGraceTimer > 0f) { vampireGraceTimer -= dt; if (vampireGraceTimer <= 0f && !isAttacking) vampireNextStage = 1; }
        if (bloodRushTimer > 0f)
        {
            bloodRushTimer -= dt;
            if (bloodRushTimer <= 0f) { bloodRushTimer = 0f; BloodGauge = p.gaugeAfterRush; }
        }
        else if (Time.time - bloodLastHitTime > p.decayDelay && BloodGauge > 0f)
        {
            BloodGauge = Mathf.Max(0f, BloodGauge - p.decayPerSecond * dt);
        }
        VampireDrawGauge();
    }

    // 頭上の小さなゲージ(赤)と、Rush中の赤いオーラ。
    void VampireDrawGauge()
    {
        if (bloodBarBg == null)
        {
            bloodBarBg = MakeBar("BloodGaugeBg", new Color(0.05f, 0.02f, 0.03f, 0.75f), RenderOrder.SlashFx);
            bloodBarFill = MakeBar("BloodGaugeFill", new Color(0.85f, 0.08f, 0.15f, 1f), RenderOrder.SlashFx + 1);
            var ag = new GameObject("BloodAura");
            ag.transform.SetParent(transform, false);
            ag.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            bloodAura = ag.AddComponent<SpriteRenderer>();
            bloodAura.sprite = KitProjectile.Orb;
            bloodAura.sortingOrder = RenderOrder.Player - 1;
        }
        bool show = !hasDied && !IsFinishing && !IsPreparingStart;
        bloodBarBg.enabled = bloodBarFill.enabled = show;
        const float w = 0.9f, h = 0.08f;
        float yTop = 1.62f;
        bloodBarBg.transform.localPosition = new Vector3(0f, yTop, 0f);
        bloodBarBg.transform.localScale = new Vector3(w + 0.04f, h + 0.04f, 1f);
        float frac = BloodRush ? bloodRushTimer / Mathf.Max(0.01f, kitDef.vampire.rushDuration) : BloodGauge / 100f;
        bloodBarFill.transform.localPosition = new Vector3(-w * 0.5f + w * frac * 0.5f, yTop, 0f);
        bloodBarFill.transform.localScale = new Vector3(w * frac, h, 1f);
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 12f);
        bloodBarFill.color = BloodRush ? Color.Lerp(new Color(1f, 0.2f, 0.25f), new Color(1f, 0.75f, 0.75f), pulse)
            : BloodGauge >= kitDef.vampire.tierThreshold ? new Color(0.95f, 0.12f, 0.2f) : new Color(0.6f, 0.06f, 0.12f);
        bloodAura.enabled = show && BloodRush;
        if (bloodAura.enabled)
        {
            bloodAura.transform.localScale = Vector3.one * (1.5f + 0.15f * pulse);
            bloodAura.color = new Color(1f, 0.1f, 0.15f, 0.35f + 0.15f * pulse);
        }
    }

    SpriteRenderer MakeBar(string name, Color c, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var r = go.AddComponent<SpriteRenderer>();
        r.sprite = KitArt.WhiteSprite();
        r.color = c;
        r.sortingOrder = order;
        return r;
    }

    void HandleVampireInput(FlickDirection f)
    {
        switch (f)
        {
            case FlickDirection.Forward:
                if (kitDiveActive) return;
                if (isAttacking && kitOwnsAttack)
                {
                    if (!comboBuffered && vampireComboStage > 0) { comboBuffered = true; bufferedDirection = AttackDirection.Forward; }
                    return;
                }
                if (isAttacking) return;
                if (attackCooldownTimer > 0f && vampireGraceTimer <= 0f) return;
                StartCoroutine(VampireCombo());
                break;
            case FlickDirection.Backward:
                if (isAttacking || attackCooldownTimer > 0f || kitDiveActive) return;
                StartCoroutine(VampireBats());
                break;
            case FlickDirection.Down:
                if (!canUseDownAttack || kitDiveActive || isAttacking) return;
                if (!isGrounded) StartCoroutine(VampireDive());
                else if (attackCooldownTimer <= 0f) StartCoroutine(VampireLowSweep());
                break;
        }
    }

    // 近接判定に「命中したらBloodが増える」を付けて出す。
    void ArmVampireBox(Vector2 center, Vector2 size, float deg, PlayerAttackKind kind, float dmg, float kb, float stop, float gain)
    {
        ArmKitBox(center, size, deg, kind, dmg * BloodPower, kb, stop);
        if (kitHitInfo != null) kitHitInfo.onHit = () => OnVampireHit(gain);
    }

    static readonly string[] VampireComboPoses = { "claw1", "claw2", "bloodslash" };

    IEnumerator VampireCombo()
    {
        var p = kitDef.vampire;
        int stage = vampireGraceTimer > 0f ? Mathf.Clamp(vampireNextStage, 1, 3) : 1;
        vampireGraceTimer = 0f;
        int gen = attackGeneration;
        int token = BeginKitMove(VampireComboPoses[stage - 1], true);
        try
        {
            vampireComboStage = stage;
            comboCount = stage;
            float s = AttackSpeedMultiplier * BloodTime;
            float windup = ArcherPick(p.comboWindup, stage - 1, 0.05f) * s;
            float active = ArcherPick(p.comboActive, stage - 1, 0.08f) * s;
            float recovery = ArcherPick(p.comboRecovery, stage - 1, 0.1f) * s;
            attackCooldownTimer = windup + active + recovery;
            float t = 0f;
            while (t < windup)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            SetKitPose(VampireComboPoses[stage - 1], 1);
            float reach = ArcherPick(p.comboReach, stage - 1, 1.1f) * AttackRangeMultiplier;
            ArmVampireBox(new Vector2(0.2f + reach * 0.5f, 0.65f), new Vector2(reach, stage >= 3 ? 0.9f : 0.75f), 0f, PlayerAttackKind.Normal,
                ArcherPick(p.comboDamageScale, stage - 1, 1f), ArcherPick(p.comboKnockbackScale, stage - 1, 1f), ArcherPick(p.comboHitStop, stage - 1, 0.03f), p.gainPerHit);
            lungeVelocityX = (stage >= 3 ? 1.0f : 0.6f) / Mathf.Max(0.01f, active); // 2026-09-30: 0.15/0.35 → 0.6/1.0(前進が見えるように)
            Color blood = BloodRush ? new Color(1f, 0.25f, 0.3f, 0.95f) : new Color(0.85f, 0.1f, 0.18f, 0.9f);
            if (stage >= 3)
            {
                OneShotSpriteEffect.CreateTweened(KitProjectile.Slash, KitWorld(new Vector2(0.2f + reach * 0.55f, 0.7f)), blood, duration: 0.2f, startScale: reach * 0.6f, endScale: reach * 0.95f, rotationDegrees: KitFacing > 0f ? 0f : 180f, sortingOrder: RenderOrder.SlashFx, holdFraction: 0.3f);
                KitShake(0.05f, 0.1f);
            }
            else
            {
                OneShotSpriteEffect.CreateTweened(KitProjectile.Slash, KitWorld(new Vector2(0.2f + reach * 0.6f, 0.7f)), blood, duration: 0.12f, startScale: 0.45f, endScale: 0.75f, rotationDegrees: stage == 1 ? -35f : 35f, sortingOrder: RenderOrder.SlashFx);
            }
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
            float chainPoint = stage >= 3 ? 1f : 0.4f;
            while (t < recovery)
            {
                if (!KitAlive(gen, token)) yield break;
                if (comboBuffered && t >= recovery * chainPoint) break;
                t += Time.deltaTime; yield return null;
            }
            vampireNextStage = stage >= 3 ? 1 : stage + 1;
            vampireGraceTimer = p.comboGrace;
            bool chain = comboBuffered && stage < 3;
            vampireComboStage = 0;
            EndKitMove(token);
            comboBuffered = false;
            if (chain && gen == attackGeneration) { attackCooldownTimer = 0f; StartCoroutine(VampireCombo()); }
        }
        finally
        {
            if (token == kitGeneration) vampireComboStage = 0;
            EndKitMove(token);
        }
    }

    IEnumerator VampireBats()
    {
        var p = kitDef.vampire;
        int gen = attackGeneration;
        int token = BeginKitMove("bats", true);
        try
        {
            transform.localScale = new Vector3(-1f, 1f, 1f);
            float s = AttackSpeedMultiplier * BloodTime;
            attackCooldownTimer = 0.4f * s;
            lungeVelocityX = -p.backStep / 0.14f; // 画面上は後ろ(左)へ下がる=向きは左なので、ワールドでは減速方向
            Vector3 from = KitWorld(new Vector2(0.3f, 0.8f));
            for (int i = 0; i < p.batCount; i++)
            {
                float ang = 180f + (i - (p.batCount - 1) * 0.5f) * 11f;
                Vector2 v = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad)) * p.batSpeed * (1f + 0.08f * i);
                var bat = KitProjectile.Create(KitArt.BatSprite(), from + new Vector3(0f, (i - 1) * 0.12f, 0f), v, p.batLifetime,
                    new Vector2(-0.5f, 0.32f), new Vector2(0.4f, 0.25f), Color.white, PlayerAttackKind.Normal,
                    p.batDamageScale * BloodPower, 0.6f, 0f, new Color(0.5f, 0.05f, 0.1f, 0.35f), false);
                bat.wobbleAmp = 0.08f; bat.wobbleFreq = 5f + i;
                bat.name = "VampireBat";
                var info = bat.GetComponent<PlayerAttackInfo>();
                info.onHit = () => OnVampireHit(p.gainPerHit * 0.5f);
            }
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(1);
            float t = 0f;
            while (t < 0.14f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            lungeVelocityX = 0f;
            t = 0f;
            while (t < 0.16f * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    // 上: 霧/コウモリに変化して斜め上へ(FireJumpのジャンプを置き換える=ジャンプ回数を消費)。
    IEnumerator VampireMist()
    {
        var p = kitDef.vampire;
        velocityY = p.mistSpeedY;
        int gen = attackGeneration;
        int token = BeginKitMove("mist", false);
        try
        {
            kitPoseTimer = p.mistTime + 0.12f;
            lungeVelocityX = p.mistDistanceX / Mathf.Max(0.01f, p.mistTime);
            ArmVampireBox(new Vector2(0.3f, 0.75f), new Vector2(1.2f, 1.3f), 30f, PlayerAttackKind.Normal, p.mistDamageScale, 0.8f, 0.03f, p.gainPerHit);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
            float t = 0f, img = 0f;
            while (t < p.mistTime)
            {
                if (!KitAlive(gen, token)) yield break;
                if (sr != null) sr.color = new Color(1f, 0.75f, 0.8f, 0.35f); // 霧のように透ける
                img -= Time.deltaTime;
                if (img <= 0f)
                {
                    img = 0.05f;
                    OneShotSpriteEffect.CreateTweened(KitProjectile.Puff, transform.position + new Vector3(Random.Range(-0.3f, 0.2f), Random.Range(0.3f, 1.1f), 0f), new Color(0.25f, 0.08f, 0.14f, 0.6f), duration: 0.35f, startScale: 0.2f, endScale: 0.5f, sortingOrder: RenderOrder.CombatFx);
                    OneShotSpriteEffect.CreateTweened(KitArt.BatSprite(), transform.position + new Vector3(Random.Range(-0.4f, 0.3f), Random.Range(0.4f, 1.2f), 0f), Color.white, duration: 0.3f, startScale: 0.25f, endScale: 0.35f, drift: new Vector3(Random.Range(-2f, 1f), Random.Range(0.5f, 2f), 0f), sortingOrder: RenderOrder.CombatFx);
                }
                t += Time.deltaTime; yield return null;
            }
            lungeVelocityX = 0f;
            DisarmKitBox(token);
            if (sr != null) sr.color = Color.white;
            t = 0f;
            while (t < 0.12f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally
        {
            if (sr != null && kit == CharacterKit.Vampire) sr.color = Color.white;
            EndKitMove(token);
        }
    }

    IEnumerator VampireDive()
    {
        var p = kitDef.vampire;
        int gen = attackGeneration;
        int token = BeginKitMove("dive", true);
        try
        {
            kitDiveActive = true;
            vampireDiveHit = false;
            kitVerticalVelocity = -p.diveSpeedY;
            lungeVelocityX = p.diveSpeedX;
            float gaugeBefore = BloodGauge;
            ArmKitBox(new Vector2(0.35f, 0.35f), new Vector2(0.9f, 0.9f), -35f, PlayerAttackKind.Down, p.diveDamageScale * BloodPower, 1.2f, 0.06f);
            if (kitHitInfo != null) kitHitInfo.onHit = () =>
            {
                if (vampireDiveHit) return; // 1回の急降下で吸血は1回
                vampireDiveHit = true;
                bool canHeal = gaugeBefore >= p.diveHealThreshold || BloodRush;
                OnVampireHit(p.gainDiveHit);
                if (canHeal) VampireHealAdd(p.diveHeal);
                OneShotSpriteEffect.CreateScatterBurst(KitProjectile.Orb, transform.position + new Vector3(0.4f, 0.5f, 0f), new Color(0.9f, 0.08f, 0.15f, 0.9f), 6, 0.35f, 0.08f, 0.16f, 3f, 1f, RenderOrder.SlashFx);
            };
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
            float t = 0f;
            while (!kitDiveLanded && t < 0.8f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            kitDiveActive = false;
            kitVerticalVelocity = null;
            lungeVelocityX = 0f;
            DisarmKitBox(token);
            if (!kitDiveLanded) yield break;
            SetKitPose("dive", 1);
            OneShotSpriteEffect.CreateScatterBurst(KitProjectile.Puff, transform.position + new Vector3(0f, 0.1f, 0f), new Color(0.3f, 0.08f, 0.12f, 0.55f), 4, 0.4f, 0.25f, 0.5f, 2f, 2.5f, RenderOrder.EnvironmentFx);
            attackCooldownTimer = 0.15f;
            t = 0f;
            while (t < 0.16f)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }

    IEnumerator VampireLowSweep()
    {
        var p = kitDef.vampire;
        int gen = attackGeneration;
        int token = BeginKitMove("bloodslash", true);
        try
        {
            float s = AttackSpeedMultiplier * BloodTime;
            attackCooldownTimer = 0.42f * s;
            float t = 0f;
            while (t < 0.06f * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            SetKitPose("bloodslash", 1);
            float reach = p.lowReach * AttackRangeMultiplier;
            ArmVampireBox(new Vector2(0.15f + reach * 0.5f, 0.25f), new Vector2(reach, 0.5f), 0f, PlayerAttackKind.Normal, p.lowDamageScale, 1.2f, 0.03f, p.gainPerHit);
            OneShotSpriteEffect.CreateTweened(KitProjectile.Slash, KitWorld(new Vector2(0.15f + reach * 0.6f, 0.25f)), new Color(0.85f, 0.1f, 0.18f, 0.85f), duration: 0.14f, startScale: 0.5f, endScale: 0.85f, sortingOrder: RenderOrder.SlashFx);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(1);
            t = 0f;
            while (t < 0.12f * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
            DisarmKitBox(token);
            t = 0f;
            while (t < 0.2f * s)
            {
                if (!KitAlive(gen, token)) yield break;
                t += Time.deltaTime; yield return null;
            }
        }
        finally { EndKitMove(token); }
    }
}
