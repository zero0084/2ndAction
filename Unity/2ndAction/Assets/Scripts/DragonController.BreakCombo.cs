using System.Collections;
using UnityEngine;

// ドラゴン(荒野 80,000m / 天空 1,000m)の怯み / BREAK 中のエリアルコンボ / BREAK の終わり(2026-10-18)。考え方は WildBossBase.BreakCombo.cs と同じ:
//  ・怯み: 崩しのゲージが 40% / 75% を越えた時、待っている間(Idle)なら次の攻撃を 0.7秒遅らせてよろける。攻撃/突進/必殺技の間は怯まない
//  ・BREAK: 地面近くへ墜ちる(今までどおり)。手前の端がプレイヤーの前 約0.9m まで寄り、↑で打ち上がる(BossJuggle、体ごと)
//  ・終わり: 残り 0.8秒で回復の予告 → 地面の近くで短く立ち直る(0.5秒)→ 元の高さへ戻る → 次の攻撃(すぐには撃たない)
public partial class DragonController
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
            juggle = new BossJuggle(BossJuggle.Style.Launch, () => box != null ? BossJuggle.LiveBounds(box) : new Bounds(Vector3.zero, new Vector3(2f, 2f, 1f)), "Dragon", this);
        }
        return juggle;
    }

    float BreakExtendCap => Mathf.Max((battle != null ? battle.breakDuration : 3f) * WildBossBase.BreakExtendMaxRatio, WildBossBase.BreakExtendMinCap);

    void TryFlinchSimple(float before, float after)
    {
        bool crossed = (before < 0.4f && after >= 0.4f) || (before < 0.75f && after >= 0.75f);
        if (!crossed || Time.time < flinchReadyAt) return;
        flinchReadyAt = Time.time + WildBossBase.FlinchCooldown;
        if (state == State.Idle && !ultimateRunningFlag)
        {
            FlinchCount++; WildBossBase.FlinchesTotal++;
            nextAttackTime = Mathf.Max(nextAttackTime, Time.time + 0.7f); // 次の攻撃まで少し待つ(短い攻撃のチャンス)
            var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (cf != null) cf.Shake(0.07f, 0.12f);
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySeAt(SeId.StrongHit, transform.position);
            StartCoroutine(HitFlash());
        }
        else
        {
            WildBossBase.FlinchArmoredTotal++;
            OneShotSpriteEffect.CreateTweened(BossFx.Ring(), transform.position, new Color(1f, 1f, 1f, 0.55f), 0.25f, 0.8f, 2.6f, 0.6f, 0f, default, 0f, RenderOrder.CombatFx, 0.05f);
        }
    }

    // TakeDamage から(この端末のプレイヤーの手動の攻撃)
    void BreakComboOnHit(PlayerAttackKind kind, int who = BossJuggle.OwnerLocal) // who: 0 = この端末 / 1以上 = 協力の相手(HOST、2026-10-21)
    {
        if (juggle == null || !juggle.Open || state != State.Stunned) return;
        int l0 = BossJuggle.Launches, up0 = BossJuggle.Launches + BossJuggle.Relaunches, down0 = BossJuggle.DownPushes;
        juggle.OnHit(kind, who);
        float cap = BreakExtendCap, baseDur = battle != null ? battle.breakDuration : 3f;
        if (juggle.Anchored && juggle.Accepting && !breakWarned) breakExtra = Mathf.Min(breakExtra + WildBossBase.BreakExtendPerHit, cap);
        if (BossJuggle.Launches > l0) breakExtra = Mathf.Clamp(breakElapsed + WildBossBase.BreakLaunchMinRemain - baseDur, breakExtra, cap);
        if (BossJuggle.Launches + BossJuggle.Relaunches > up0)
        {
            AerialComboFx.Launch(juggle.ComboAimPoint, 1.4f);
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySeAt(SeId.HitLaunch, transform.position);
        }
        else if (BossJuggle.DownPushes > down0) AerialComboFx.DownHit(juggle.ComboAimPoint, 1.4f);
    }

    // BREAK 中の位置(StunFollow の代わり)。打ち上げ/落下の間は true
    bool StunJuggleStep(float x, float gy)
    {
        if (juggle == null || !juggle.Open || (!juggle.Anchored && !juggle.Airborne)) return false;
        var box = GetComponent<BoxCollider2D>();
        if (box == null) return false;
        Bounds b = BossJuggle.LiveBounds(box);
        float dt = Time.deltaTime;
        Vector3 off = b.center - transform.position;
        Vector2 center = new Vector2(x + off.x, transform.position.y + off.y);
        float ceilMax = float.PositiveInfinity;
        float? c = TerrainManager.Instance != null ? TerrainManager.Instance.GetEffectiveCeilingHeightAt(center.x) : null;
        if (c.HasValue) ceilMax = c.Value - b.extents.y - 0.1f;
        Vector2 d = juggle.Step(center, b.extents, gy + off.y, ceilMax, dt, out bool slammed, out bool landed, TargetBaseSpeed());
        // 2026-10-21: 床は動いた後の x で見る(高速の坂で、動く前の x の床に合わせて体が地面へ入っていた)
        float floorOff = gy - GroundYAt(x);
        float gyAfter = Mathf.Max(gy, GroundYAt(x + d.x) + floorOff);
        float ny = Mathf.Max(gyAfter, transform.position.y + d.y);
        transform.position = new Vector3(x + d.x, ny, 0f);
        stunY = ny;
        if (slammed)
        {
            AerialComboFx.BossSlam(b.center, new Vector3(b.center.x, GroundYAt(x), 0f), 3.5f);
            var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (cf != null) cf.Shake(0.16f, 0.22f);
            if (GameManager.Instance != null) HitStop.Begin(0.05f);
        }
        else if (landed)
        {
            var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (cf != null) cf.Shake(0.07f, 0.12f);
        }
        if (BreakDiag && player != null) breakDiagGap = transform.position.x - player.position.x;
        return true;
    }

    IEnumerator StunRoutine()
    {
        var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cf != null) cf.Shake(0.18f, 0.25f);
        EnsureJuggle().OpenBreak(false);
        breakWarned = false; breakExtra = 0f;
        AerialComboFx.BreakBurst(transform.position, 3.5f);
        float t = 0f, star = 0f;
        float dur = battle != null ? battle.breakDuration : 3f;
        while (true)
        {
            float total = dur + breakExtra;
            if (t >= total && juggle.Anchored) juggle.EndCombo("breakEnd"); // 2026-10-21: 時間が尽きたら寄せを終える
            if (t >= total && !juggle.Airborne) break;
            if (t >= total + 1.5f) break; // 安全装置
            t += Time.deltaTime; star -= Time.deltaTime;
            breakElapsed = t;
            if (!breakWarned && t >= total - WildBossBase.BreakWarnSeconds)
            {
                breakWarned = true;
                juggle.BeginFinish(); // 2026-10-21: 締めの合図(→ / ↓ は届く、打ち上げ直しはしない)
                AerialComboFx.FinishCue(this, () => transform.position, 3.5f, WildBossBase.BreakWarnSeconds, () => Broken && !IsDead);
            }
            if (sr != null) sr.color = breakWarned ? Color.Lerp(Color.white, AerialComboFx.FinishTint, Mathf.PingPong(t * 9f, 1f)) : Color.white;
            if (star <= 0f && !breakWarned)
            {
                star = 0.25f;
                OneShotSpriteEffect.CreateTweened(OneShotSpriteEffect.SoftDotSprite(), transform.position + new Vector3(Random.Range(-1f, 1f), Random.Range(0.6f, 1.4f), 0f), new Color(1f, 0.95f, 0.45f, 1f), 0.4f, 0.25f, 0.05f, 1f, 0f, default, 0f, RenderOrder.CombatFx, 0.2f);
            }
            yield return null;
        }
        LastBreakSeconds = t;
        juggle.CloseBreak();
        if (sr != null) sr.color = Color.white;
        stagger = 0f;
        BreakEndedAt = Time.time;
        Debug.Log($"[BossBattle] Dragon BREAK end after {t:F2}s (duration {dur:F2}s + {breakExtra:F2}s)");
        // 立ち直り: 地面の近くで短く(攻撃しない・崩しは溜まらない)
        state = State.Landing; // 戻りの間は通常の追従をしない(体当たり判定の無い状態)
        breakRecovering = true;
        BossVoice.Growl(this, "Dragon");
        float r = 0f;
        while (r < WildBossBase.BreakRecoverSeconds)
        {
            r += Time.deltaTime;
            float x = transform.position.x + TargetBaseSpeed() * Time.deltaTime; // 走行に合わせて運ぶ
            // 2026-10-21 追加調査: 立ち直りの間も地面に沿う(以前は高さを止めたまま前へ運んでいたので、150km/h の上り坂で
            // 0.5秒の間に体が地面へ最大 1.3m 入っていた)。下り坂ではなめらかに下がる
            float y = transform.position.y;
            var rbox = GetComponent<BoxCollider2D>();
            if (rbox != null && ComboRevision.Followup)
            {
                float bottomOffset = transform.position.y - BossJuggle.LiveBounds(rbox).min.y;
                float bodyX = x + (BossJuggle.LiveBounds(rbox).center.x - transform.position.x);
                float rest = Mathf.Max(GroundYAt(x), GroundYAt(bodyX)) + bottomOffset + breakBottomClearance;
                y = Mathf.Max(rest, Mathf.Lerp(y, rest, 1f - Mathf.Exp(-Time.deltaTime / 0.1f)));
            }
            transform.position = new Vector3(x, y, 0f);
            yield return null;
        }
        breakRecovering = false;
        breakResistUntil = Time.time + WildBossBase.PostBreakResistSeconds;
        flinchReadyAt = Time.time + WildBossBase.FlinchCooldown;
        yield return ReturnToHome(0.6f);
        state = State.Idle;
        ScheduleNextAttack();
        nextAttackTime = Mathf.Max(nextAttackTime, Time.time + WildBossBase.BreakGraceSeconds);
        AttackDelayAfterBreak = nextAttackTime - BreakEndedAt;
    }
}
