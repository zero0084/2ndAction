using System.Collections;
using UnityEngine;

// 天空ボス強化(2026-10-05): 5,000m 魔人を「ボス戦の強化」へ(荒野/洞窟と同じ BossBattleTuning の値・崩し・必殺技の順番取り)。
// 「プレイヤーと同じくらいの大きさなのに、空中の機動では圧倒的」。
//  第1段階: 既存(近接の反撃の時間/魔力弾の5パターン/空中移動)+近接の爪(急接近 → 低い薙ぎ払い=跳ぶ → その場にとどまる=反撃の時間)
//  第2段階: 短い瞬間移動(出現地点に必ず魔法陣の予兆)+魔法陣の攻撃(下から=跳ぶ → 上から=地面にいる)
//  必殺技 SKY MAGIC ASSAULT: 上空へ → 複数の魔法陣 → 時間差の魔力弾(跳ね返せる)→ 帯で動ける場所を制限(低い=跳ぶ → 高い=地面にいる)
//   → 最後に魔人自身が高速突進(低い=跳ぶ)。避けると地面の近くで短く硬直(攻撃のチャンス)
// 崩し(BREAK): 地面近くへ落ちて無防備。マルチは既存のパペット同期(位置/見た目)+地形攻撃/弾の同期(NetAttackSync)をそのまま使う。
public partial class MajinController : IBossBattleDebug
{
    BossBattleTuning.Entry battle;
    int phase = 1;
    float phaseAt, lastUltimateTime = -99f, lastSpecialTime = -99f, stagger, lastStaggerTime, pendingStagger, staggerMul = 1f;
    bool subBarReady;
    public int RematchTier { get; private set; } = -1;
    public int Phase => phase;
    public int PhaseCount => (battle != null && battle.phaseThresholds != null ? battle.phaseThresholds.Length : 0) + 1;
    public bool Broken { get; private set; }
    public int BreakCount { get; private set; }
    public int UltimatesUsed { get; private set; }
    public int SpecialsUsed { get; private set; }
    public bool UltimateRunning { get; private set; }
    public float StaggerFraction => battle != null && battle.staggerMax > 0f ? Mathf.Clamp01(stagger / battle.staggerMax) : 0f;
    public string DebugName => "Majin";
    public bool DebugAlive => state != State.Dead && isActiveAndEnabled;
    static readonly Color MagicCol = new Color(0.78f, 0.35f, 1f, 1f);

    public void EnableSkyBattle(BossBattleTuning.Entry entry, BossRematchTuning.Tier tier, int tierIndex)
    {
        if (entry == null) return;
        battle = entry.Clone();
        if (battle.hpScale > 0f && Mathf.Abs(battle.hpScale - 1f) > 0.001f) maxHp = Mathf.Max(1, Mathf.RoundToInt(maxHp * battle.hpScale));
        if (tier != null)
        {
            battle.staggerMax *= Mathf.Max(0.1f, tier.staggerMul);
            battle.specialCooldown *= Mathf.Max(0.1f, tier.cooldownMul);
            if (battle.ultimateCooldown > 0f) battle.ultimateCooldown *= Mathf.Max(0.1f, tier.cooldownMul);
            attackIntervalMin *= Mathf.Max(0.5f, tier.cooldownMul); attackIntervalMax *= Mathf.Max(0.5f, tier.cooldownMul);
            RematchTier = tierIndex;
        }
    }

    bool Upgraded => RematchTier >= 1;

    void BattleTick()
    {
        if (battle == null) return;
        if (!subBarReady && hpBar != null && battle.staggerMax > 0f) { hpBar.EnableSub(squareSprite, 0.08f); if (battle.phaseThresholds != null && battle.phaseThresholds.Length > 0) hpBar.SetPhaseTicks(squareSprite, battle.phaseThresholds); subBarReady = true; }
        if (!Broken && stagger > 0f && Time.time - lastStaggerTime > BossBattleTuning.I.staggerRecoveryDelay)
            stagger = Mathf.Max(0f, stagger - battle.staggerRecoveryPerSec * Time.deltaTime);
        if (hpBar != null && battle.staggerMax > 0f) hpBar.SetSub(StaggerFraction, Broken);
    }

    // 攻撃の開始の前(Update)。必殺技/第2段階の技を始めたら true
    bool BattleTryStart()
    {
        if (battle == null || player == null) return false;
        if (phase >= 2 && battle.ultimateCooldown > 0f && Time.time - lastUltimateTime >= battle.ultimateCooldown
            && Time.time - phaseAt >= battle.firstUltimateDelay && BossBattle.TryBeginUltimate(this))
        {
            StartCoroutine(SkyMagicAssault());
            return true;
        }
        // 近接の爪(第1段階から): 漂って撃つだけでなく、自分から間合いを詰めてくる(近接キャラの攻撃が届く時間にもなる)
        if (Time.time - lastClawTime >= clawEvery && Time.time - lastSpecialTime >= 2.5f)
        {
            lastClawTime = Time.time;
            StartCoroutine(ClawSwoop());
            return true;
        }
        int sp = RematchTier >= 2 ? 1 : 2;
        if (phase >= sp && Time.time - lastSpecialTime >= battle.specialCooldown)
        {
            lastSpecialTime = Time.time;
            SpecialsUsed++;
            StartCoroutine(TeleportCircles());
            return true;
        }
        return false;
    }

    int ScaleIncoming(int amount) => Broken || staggerMul > 1.01f ? Mathf.CeilToInt(amount * BossBattleTuning.I.breakDamageScale) : amount;

    void BattleOnDamaged()
    {
        if (battle == null) return;
        float stg = pendingStagger; pendingStagger = 0f;
        CheckBattlePhase();
        if (stg > 0f) AddStagger(stg);
    }

    void CheckBattlePhase()
    {
        if (battle.phaseThresholds == null || battle.phaseThresholds.Length == 0) return;
        float f = (float)Hp / Mathf.Max(1, maxHp);
        int p = 1;
        foreach (float th in battle.phaseThresholds) if (f <= th) p++;
        if (p <= phase) return;
        phase = p;
        phaseAt = Time.time;
        attackIntervalMin *= 0.8f; attackIntervalMax *= 0.8f;
        Debug.Log($"[BossBattle] Majin PHASE {phase} (hp {Hp}/{maxHp})");
        if (hpBar != null) hpBar.Flash(0.8f);
        OneShotSpriteEffect.CreateTweened(BossFx.Ring(), transform.position, MagicCol, 0.5f, 1f, 5f, 0.9f, 0f, default, 0f, RenderOrder.CombatFx, 0.1f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossWarning);
        BossBattleHud.Banner(phase >= PhaseCount ? "最終段階!" : "激昂!", MagicCol, 1.2f);
        lastSpecialTime = Time.time - battle.specialCooldown + 0.6f; // 新しい技をすぐ見せる
    }

    void AddStagger(float v)
    {
        if (BossBattle.DebugNoStagger) return;
        if (battle == null || battle.staggerMax <= 0f || Broken || state == State.Entering || state == State.Dead) return;
        stagger += v * staggerMul;
        lastStaggerTime = Time.time;
        if (stagger < battle.staggerMax) return;
        stagger = battle.staggerMax;
        BreakCount++;
        Debug.Log($"[BossBattle] Majin BREAK #{BreakCount}");
        BossBattleHud.Banner("BREAK!", new Color(1f, 0.85f, 0.3f), 1.0f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossFinalHit);
        StopAllCoroutines();
        if (flashOverlay != null) flashOverlay.enabled = false;
        if (UltimateRunning) { UltimateRunning = false; BossBattle.EndUltimate(this); lastUltimateTime = Time.time; }
        if (sr != null) sr.color = Color.white;
        StartCoroutine(BreakRoutine());
    }

    IEnumerator BreakRoutine()
    {
        Broken = true;
        float keepHold = exposeHoldTime;
        exposeHoldTime = battle != null ? battle.breakDuration : 3f;
        yield return DescendExposed();
        exposeHoldTime = keepHold;
        Broken = false; stagger = 0f;
        if (state == State.Dead) yield break;
        if (sr != null) sr.color = Color.white;
        state = State.Idle;
        SetFrames(idleFrames);
        ScheduleNextAttack();
    }

    // プレイヤー基準の位置(瞬間移動/必殺技の間は追従の代わりにこれで置く)
    Vector3 RelPos(float dx, float alt) { float x = player.position.x + dx; return new Vector3(x, GroundYAt(x) + alt, 0f); }

    IEnumerator FadeTo(float a, float dur)
    {
        float t = 0f; float from = sr != null ? sr.color.a : 1f;
        while (t < dur && state != State.Dead) { t += Time.deltaTime; if (sr != null) { var c = sr.color; c.a = Mathf.Lerp(from, a, t / dur); sr.color = c; } yield return null; }
    }

    static int MagicDmg(bool heavy) => heavy ? Mathf.Max(1, BossBattleTuning.I.ultimateDamage) : CombatScale.PlayerHit;

    // 第2段階: 短い瞬間移動(出現地点に魔法陣の予兆)→ 魔法陣の攻撃(下から → 上から)
    public float clawEvery = 5.5f;
    float lastClawTime = -4f;
    public int ClawCount { get; private set; }

    IEnumerator ClawSwoop()
    {
        state = State.Special;
        ClawCount++;
        SetFrames(attackFrames);
        float dx = 3.0f, alt = Mathf.Max(1.2f, (hoverHeight - groundClearance) + 0.2f);
        // 予兆: 体が紫に光る → 急接近
        if (flashOverlay != null) { flashOverlay.color = MagicCol; flashOverlay.enabled = true; }
        Vector3 from = transform.position; float t = 0f;
        while (t < 0.45f && state == State.Special) { t += Time.deltaTime; transform.position = Vector3.Lerp(from, RelPos(dx, alt), Mathf.SmoothStep(0f, 1f, t / 0.45f)); yield return null; }
        if (flashOverlay != null) flashOverlay.enabled = false;
        // 低い薙ぎ払い(跳ぶ)。予告の帯 0.7 秒
        float d = CaveBossSafety.Reserve(false, 0.7f, 0.3f);
        CaveHazard.Band(player.position.x + 0.8f, 4.2f, 0f, 1.15f, 0.7f + d, 0.3f, CaveLook.Magic, MagicDmg(false), false, "Majin:claw");
        t = 0f;
        while (t < 1.0f + d && state == State.Special) { t += Time.deltaTime; transform.position = RelPos(dx, alt); yield return null; }
        // 振り抜いた後はその場にとどまる(反撃の時間、暗い色=今は撃ってこない)
        if (sr != null) sr.color = recoveryTint;
        t = 0f;
        while (t < 1.4f && state == State.Special) { t += Time.deltaTime; transform.position = RelPos(dx, alt); yield return null; }
        if (sr != null) sr.color = Color.white;
        if (state != State.Special) yield break;
        yield return ReturnToHome(0.5f);
        state = State.Idle;
        SetFrames(idleFrames);
        ScheduleNextAttack();
    }

    IEnumerator TeleportCircles()
    {
        state = State.Special;
        SetFrames(attackFrames);
        float dx = Random.value < 0.5f ? 5.5f : 8.5f, alt = hoverHeight * 0.8f;
        CaveHazard.Tell(player.position.x + dx, 0.75f, CaveTellStyle.Circle, CaveLook.Magic, true, null, "Majin:teleport");
        yield return FadeTo(0f, 0.25f);
        yield return new WaitForSeconds(0.45f);
        float t = 0f;
        transform.position = RelPos(dx, alt);
        yield return FadeTo(1f, 0.2f);
        // 魔法陣: 下から(跳ぶ)→ 上から(地面にいる)。床と天井は CaveBossSafety が間を空ける
        float d1 = CaveBossSafety.Reserve(false, 0.9f, 0.35f);
        CaveHazard.Floor(player.position.x, 2.2f, 1.2f, 0.9f + d1, 0.35f, CaveLook.Magic, MagicDmg(false), false, "Majin:circle");
        while (t < 1.0f && state == State.Special) { t += Time.deltaTime; transform.position = RelPos(dx, alt); yield return null; }
        float d2 = CaveBossSafety.Reserve(true, 0.9f, 0.35f);
        if (!CaveBossSafety.PitDuring(0.9f + d2, 1.25f + d2)) CaveHazard.Band(player.position.x, 2.4f, 2.3f, 4.2f, 0.9f + d2, 0.35f, CaveLook.Magic, MagicDmg(false), false, "Majin:circleHigh");
        t = 0f;
        while (t < 1.4f && state == State.Special) { t += Time.deltaTime; transform.position = RelPos(dx, alt); yield return null; }
        if (state != State.Special) yield break;
        yield return ReturnToHome(0.5f);
        state = State.Idle;
        SetFrames(idleFrames);
        ScheduleNextAttack();
    }

    IEnumerator SkyMagicAssault()
    {
        state = State.Special;
        UltimatesUsed++;
        UltimateRunning = true;
        lastUltimateTime = Time.time;
        Debug.Log($"[BossBattle] Majin ULTIMATE 'SKY MAGIC ASSAULT' #{UltimatesUsed}");
        BossBattleHud.Banner("SKY MAGIC ASSAULT", MagicCol, 1.6f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossWarning);
        SetFrames(attackFrames);
        // 上空へ
        Vector3 start = transform.position; float t = 0f;
        while (t < 0.6f) { t += Time.deltaTime; transform.position = Vector3.Lerp(start, RelPos(7f, 4.8f), t / 0.6f); yield return null; }
        // 複数の魔法陣(空中)→ 時間差の魔力弾(跳ね返せる)
        int n = Upgraded ? 5 : 4;
        var circles = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            circles[i] = new Vector3(2.5f + i * 1.8f, 3.6f + (i % 2) * 1.2f, 0f);
            Vector3 cp = player.position + circles[i];
            OneShotSpriteEffect.CreateTweened(BossFx.Ring(), cp, MagicCol, 1.2f + i * 0.45f, 1.4f, 1.8f, 0.95f, 0.6f, default, 0f, RenderOrder.CombatFx, 0.8f);
        }
        t = 0f;
        while (t < 1.0f) { t += Time.deltaTime; transform.position = RelPos(7f, 4.8f); yield return null; }
        for (int i = 0; i < n; i++)
        {
            Vector3 cp = player.position + circles[i];
            Vector2 dir = ((Vector2)(player.position + Vector3.up * 0.6f) - (Vector2)cp).normalized;
            FireballController.Create(squareSprite, cp, dir * (fireballSpeed * 0.8f));
            float w = 0f; while (w < 0.45f) { w += Time.deltaTime; transform.position = RelPos(7f, 4.8f); yield return null; }
        }
        // 動ける場所を制限する帯: 低い(跳ぶ)→ 高い(地面にいる)
        float dl = CaveBossSafety.Reserve(false, 0.9f, 0.4f);
        CaveHazard.Band(player.position.x, 12f, 0f, 1.05f, 0.9f + dl, 0.4f, CaveLook.Magic, MagicDmg(true), true, "Majin:bandLow");
        float dh = CaveBossSafety.Reserve(true, 0.9f, 0.4f);
        if (!CaveBossSafety.PitDuring(0.9f + dh, 1.3f + dh)) CaveHazard.Band(player.position.x, 12f, 2.3f, 4.2f, 0.9f + dh, 0.4f, CaveLook.Magic, MagicDmg(true), true, "Majin:bandHigh");
        t = 0f;
        while (t < 2.4f) { t += Time.deltaTime; transform.position = RelPos(7f, 4.8f); yield return null; }
        // 最後に魔人自身が高速突進(低い=跳ぶ): 予告 → 体ごと画面を横切る
        CaveHazard.Band(player.position.x, 12f, 0f, 1.2f, 0.9f, 0f, CaveLook.Magic, 0, false, "Majin:dashWarn");
        t = 0f; Vector3 from = transform.position;
        while (t < 0.9f) { t += Time.deltaTime; transform.position = Vector3.Lerp(from, RelPos(12f, 0.9f), t / 0.9f); yield return null; }
        float speed = 16f, dx = 12f;
        CaveBossSafety.Reserve(false, 0.6f, 0.4f);
        CaveHazard.Wave(player.position.x + 12f, 2.2f, 0f, 1.2f, speed, CaveLook.Magic, MagicDmg(true), true, 0.72f, "Majin:dash");
        while (dx > -12f && state == State.Special) { dx -= speed * Time.deltaTime; transform.position = RelPos(dx, 0.9f); yield return null; }
        // 避けた → 地面の近くで短く硬直(攻撃のチャンス)
        UltimateRunning = false;
        BossBattle.EndUltimate(this);
        lastUltimateTime = Time.time;
        t = 0f; from = transform.position;
        while (t < 0.5f) { t += Time.deltaTime; transform.position = Vector3.Lerp(from, RelPos(1.6f, 0.9f), t / 0.5f); yield return null; }
        BossBattleHud.Banner("魔人が硬直! 攻撃のチャンス!", new Color(0.6f, 1f, 0.6f), 1.2f);
        Debug.Log("[CaveBoss] Majin RECOVERY 'SKY MAGIC ASSAULT'");
        staggerMul = 2.2f;
        float keepHold = exposeHoldTime;
        exposeHoldTime = 3.0f;
        yield return DescendExposed();
        exposeHoldTime = keepHold;
        staggerMul = 1f;
        if (state == State.Dead) yield break;
        state = State.Idle;
        SetFrames(idleFrames);
        ScheduleNextAttack();
    }

    // ---- 開発用(IBossBattleDebug) ----
    public void DebugSetPhase(int p)
    {
        if (battle == null || state == State.Dead) return;
        p = Mathf.Clamp(p, 1, PhaseCount);
        float frac = p == 1 ? 1f : battle.phaseThresholds[p - 2] - 0.03f;
        Hp = Mathf.Clamp(Mathf.FloorToInt(maxHp * frac), 1, maxHp);
        if (hpBar != null) hpBar.SetFraction((float)Hp / Mathf.Max(1, maxHp));
        if (p < phase) phase = p; else CheckBattlePhase();
    }
    public bool DebugForceUltimate()
    {
        if (battle == null || state == State.Dead || battle.ultimateCooldown <= 0f) return false;
        if (phase < 2) DebugSetPhase(2);
        StopAllCoroutines();
        if (UltimateRunning) { UltimateRunning = false; BossBattle.EndUltimate(this); }
        Broken = false; staggerMul = 1f;
        if (sr != null) sr.color = Color.white;
        lastUltimateTime = -99f; phaseAt = Time.time - 99f;
        BossBattle.LastUltimateEnd = -99f;
        state = State.Idle;
        nextAttackTime = Time.time;
        return true;
    }
    public void DebugForceBreak() { if (battle != null && battle.staggerMax > 0f) { bool keep = BossBattle.DebugNoStagger; BossBattle.DebugNoStagger = false; AddStagger(battle.staggerMax * 1.5f); BossBattle.DebugNoStagger = keep; } }
}
