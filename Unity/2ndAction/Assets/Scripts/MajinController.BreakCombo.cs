using System.Collections;
using UnityEngine;

// 魔人(天空 5,000m)の怯み / BREAK 中のエリアルコンボ / BREAK の終わり(2026-10-18)。考え方は WildBossBase.BreakCombo.cs と同じ:
//  ・怯み: 崩しのゲージが 40% / 75% を越えた時、待っている間(Idle)なら次の攻撃を 0.7秒遅らせてよろける。技の間は怯まない
//  ・BREAK: 地面近く(手前の端がプレイヤーの前 0.8m)へ降りて無防備(今までどおり)。↑で体ごと打ち上がる(BossJuggle)
//  ・終わり: 残り 0.8秒で回復の予告 → その場で短く立ち直る(0.5秒)→ 元の高さへ戻る → 次の攻撃(すぐには撃たない)
public partial class MajinController
{
    BossJuggle juggle;
    public BossJuggle Juggle => juggle; // 確認用(QA)
    PlayerAttackKind pendingKind;
    bool pendingManual, breakWarned, breakRecovering;
    float breakExtra, breakElapsed, breakResistUntil = -99f, flinchReadyAt = -99f;
    public int FlinchCount { get; private set; }
    public float LastBreakSeconds { get; private set; }
    public bool BreakRecovering => breakRecovering;
    public bool BreakWarned => breakWarned;
    public float LastBreakExtra => breakExtra;
    public float BreakEndedAt { get; private set; } = -99f;
    public float AttackDelayAfterBreak { get; private set; } = -1f; // BREAK が終わってから次の攻撃を始められるまでの秒数(確認用)
    public float DebugBreakMax => (battle != null ? battle.breakDuration : 3f) + BreakExtendCap;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static System.Action<Component, PlayerAttackKind> QaLocalHit; // 確認用: この端末のプレイヤーの手動の攻撃が当たった(倒した時も)
    public void DebugScaleHp(float mul) { maxHp = Mathf.Max(1, Mathf.RoundToInt(maxHp * mul)); Hp = maxHp; if (hpBar != null) hpBar.SetFraction(1f); }
    public void DebugSetHp(int hp) { Hp = Mathf.Clamp(hp, 1, Mathf.Max(1, maxHp)); if (hpBar != null) hpBar.SetFraction((float)Hp / Mathf.Max(1, maxHp)); }
    public float DebugStaggerMax => battle != null ? battle.staggerMax : 0f;
    public void DebugAddStaggerQa(float v) => AddStagger(v);
    public void DebugResetResist() { breakResistUntil = -99f; }
    public float DebugBreakDuration => battle != null ? battle.breakDuration : 0f;
#endif
    void QaNoteHit(PlayerAttackKind kind)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        QaLocalHit?.Invoke(this, kind);
#endif
    }

    BossJuggle EnsureJuggle()
    {
        if (juggle == null)
        {
            var box = GetComponent<BoxCollider2D>();
            juggle = new BossJuggle(BossJuggle.Style.Launch, () => box != null ? BossJuggle.LiveBounds(box) : new Bounds(Vector3.zero, new Vector3(1.5f, 2f, 1f)), "Majin", this);
        }
        return juggle;
    }

    float BreakExtendCap => Mathf.Max((battle != null ? battle.breakDuration : 3f) * WildBossBase.BreakExtendMaxRatio, WildBossBase.BreakExtendMinCap);

    void TryFlinchSimple(float before, float after)
    {
        bool crossed = (before < 0.4f && after >= 0.4f) || (before < 0.75f && after >= 0.75f);
        if (!crossed || Time.time < flinchReadyAt) return;
        flinchReadyAt = Time.time + WildBossBase.FlinchCooldown;
        if (state == State.Idle && !UltimateRunning)
        {
            FlinchCount++; WildBossBase.FlinchesTotal++;
            nextAttackTime = Mathf.Max(nextAttackTime, Time.time + 0.7f);
            var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (cf != null) cf.Shake(0.07f, 0.12f);
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySeAt(SeId.StrongHit, transform.position);
            StartCoroutine(HitFlash());
        }
        else
        {
            WildBossBase.FlinchArmoredTotal++;
            OneShotSpriteEffect.CreateTweened(BossFx.Ring(), transform.position, new Color(1f, 1f, 1f, 0.55f), 0.25f, 0.6f, 2f, 0.6f, 0f, default, 0f, RenderOrder.CombatFx, 0.05f);
        }
    }

    void BreakComboOnHit(PlayerAttackKind kind)
    {
        if (juggle == null || !juggle.Open || !Broken) return;
        int l0 = BossJuggle.Launches, up0 = BossJuggle.Launches + BossJuggle.Relaunches, down0 = BossJuggle.DownPushes;
        juggle.OnLocalHit(kind);
        float cap = BreakExtendCap, baseDur = battle != null ? battle.breakDuration : 3f;
        if (juggle.Anchored && juggle.Accepting) breakExtra = Mathf.Min(breakExtra + WildBossBase.BreakExtendPerHit, cap);
        if (BossJuggle.Launches > l0) breakExtra = Mathf.Clamp(breakElapsed + WildBossBase.BreakLaunchMinRemain - baseDur, breakExtra, cap);
        if (BossJuggle.Launches + BossJuggle.Relaunches > up0)
        {
            AerialComboFx.Launch(juggle.ComboAimPoint, 1.1f);
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySeAt(SeId.HitLaunch, transform.position);
        }
        else if (BossJuggle.DownPushes > down0) AerialComboFx.DownHit(juggle.ComboAimPoint, 1.1f);
    }

    // BREAK 中の位置: 打ち上げ/落下の間は BossJuggle、それ以外は今までどおり手前の届く所(ExposedPosition)
    void BreakPlace()
    {
        Vector3 rest = ExposedPosition();
        if (juggle == null || !juggle.Open || (!juggle.Anchored && !juggle.Airborne)) { transform.position = rest; return; }
        var box = GetComponent<BoxCollider2D>();
        if (box == null) { transform.position = rest; return; }
        Bounds b = BossJuggle.LiveBounds(box);
        float dt = Time.deltaTime;
        Vector3 off = b.center - transform.position;
        float x = transform.position.x + TargetBaseSpeed() * dt; // 走行に合わせて運ぶ
        Vector2 center = new Vector2(x + off.x, transform.position.y + off.y);
        float ceilMax = float.PositiveInfinity;
        float? c = TerrainManager.Instance != null ? TerrainManager.Instance.GetEffectiveCeilingHeightAt(center.x) : null;
        if (c.HasValue) ceilMax = c.Value - b.extents.y - 0.1f;
        float restY = GroundYAt(x) + (rest.y - GroundYAt(rest.x));
        Vector2 d = juggle.Step(center, b.extents, restY + off.y, ceilMax, dt, out bool slammed, out bool landed);
        transform.position = new Vector3(x + d.x, Mathf.Max(restY, transform.position.y + d.y), 0f);
        if (slammed)
        {
            AerialComboFx.BossSlam(b.center, new Vector3(b.center.x, GroundYAt(x), 0f), 2.6f);
            var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (cf != null) cf.Shake(0.16f, 0.22f);
            if (GameManager.Instance != null) HitStop.Begin(0.05f);
        }
        else if (landed)
        {
            var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (cf != null) cf.Shake(0.07f, 0.12f);
        }
    }

    IEnumerator BreakRoutine()
    {
        Broken = true;
        state = State.Exposed;
        SetFrames(idleFrames);
        if (sr != null) sr.color = recoveryTint;
        EnsureJuggle().OpenBreak(false);
        breakWarned = false; breakExtra = 0f;
        AerialComboFx.BreakBurst(transform.position, 2.6f);
        // 届く所まで降りる(今までどおりプレイヤーに対する位置で。打ち上げられたらそこで BossJuggle へ)
        Vector3 startRel = transform.position - PlayerXOffset();
        float t = 0f, dur = battle != null ? battle.breakDuration : 3f;
        float dsc = 0f;
        while (dsc < 1f && state == State.Exposed && player != null && !juggle.Airborne)
        {
            dsc += Time.deltaTime / Mathf.Max(0.05f, breakDescendTime);
            t += Time.deltaTime; breakElapsed = t;
            transform.position = Vector3.Lerp(startRel + PlayerXOffset(), ExposedPosition(), Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(dsc)));
            yield return null;
        }
        while (state == State.Exposed && player != null)
        {
            float total = dur + breakExtra;
            if (t >= total && !juggle.Airborne) break;
            if (t >= total + 1.5f) break; // 安全装置
            t += Time.deltaTime; breakElapsed = t;
            if (!breakWarned && t >= total - WildBossBase.BreakWarnSeconds)
            {
                breakWarned = true;
                juggle.StopAccepting();
                AerialComboFx.RecoverWarn(transform.position, 2.6f);
            }
            if (sr != null) sr.color = breakWarned ? Color.Lerp(Color.white, new Color(0.7f, 0.85f, 1f), Mathf.PingPong(t * 9f, 1f)) : recoveryTint;
            BreakPlace();
            yield return null;
        }
        LastBreakSeconds = t;
        juggle.CloseBreak();
        Broken = false; stagger = 0f;
        BreakEndedAt = Time.time;
        if (state == State.Dead) yield break;
        // 立ち直り(攻撃しない・崩しは溜まらない)
        breakRecovering = true;
        BossVoice.Growl(this, "Majin");
        float r = 0f;
        while (r < WildBossBase.BreakRecoverSeconds && state == State.Exposed && player != null)
        {
            r += Time.deltaTime;
            transform.position = ExposedPosition();
            if (sr != null) sr.color = Color.Lerp(new Color(1f, 0.95f, 0.85f), Color.white, r / WildBossBase.BreakRecoverSeconds);
            yield return null;
        }
        breakRecovering = false;
        breakResistUntil = Time.time + WildBossBase.PostBreakResistSeconds;
        flinchReadyAt = Time.time + WildBossBase.FlinchCooldown;
        if (state == State.Dead) yield break;
        if (sr != null) sr.color = Color.white;
        yield return ReturnToHome(exposeReturnTime); // BREAK の後に、今の位置から(プレイヤー基準で)元の位置へ
        if (state == State.Dead) yield break;
        state = State.Idle;
        SetFrames(idleFrames);
        ScheduleNextAttack();
        nextAttackTime = Mathf.Max(nextAttackTime, Time.time + WildBossBase.BreakGraceSeconds);
        AttackDelayAfterBreak = nextAttackTime - BreakEndedAt;
    }
}
