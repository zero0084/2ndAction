using System.Collections;
using UnityEngine;

// ===================================================================== //
// 地形/予兆で戦うボスの共通層(自然洞窟 2026-10-04 → 天空回廊 2026-10-05 でも使う。同じ仕組みを複製しない)
// ===================================================================== //
public abstract class HazardBossBase : WildBossBase
{
    protected int RT => Mathf.Max(0, RematchTierApplied);   // 再戦の段階(0=初登場/通常再戦)
    protected bool Upgraded => RT >= 1;                       // 強化再戦以上: 必殺技に一手追加
    protected int SpecialPhase => RT >= 2 ? 1 : 2;            // 上位強化再戦以上: 第2段階の技を最初から使う
    protected bool Enraged => Phase >= 3;                     // 第3段階: 隙(回復)が短くなる(予兆の長さは変えない)
    protected float RecoverMul => Enraged ? 0.75f : 1f;
    protected int HitDmg => tune != null ? Mathf.Max(CombatScale.PlayerHit, tune.normalDamage) : CombatScale.PlayerHit;
    protected int Dmg(bool heavy) => heavy ? UltimateDamage : HitDmg;
    protected string Src(string what) => bossName + ":" + what;

    protected bool onCeiling, buried;
    protected CaveHazard tunnelTell;
    protected int lastPick = -1;

    protected float CeilY(float x) => CaveHazard.CeilingAt(x);

    // スーパーアーマー(崩しで止める)+段階の咆哮/BREAK の割り込み。各ボスの OnInit の最初に呼ぶ
    protected void HazardBattleSetup() { interruptible = false; supportsInterrupt = true; }


    protected override void OnBattleTick(float dt)
    {
        // 天井を這っている間は天井の高さに沿う。本物の天井が途切れたら(天井の無い区間へ出た等)、その場から降りて通常の行動へ(2026-10-07)
        if (onCeiling && !climbing)
        {
            if (RealCeilingSpace(worldX) > 0f) yOffset = Mathf.MoveTowards(yOffset, CeilY(worldX) - GroundY, 10f * dt);
            else if (!ceilingLost && !IsDead && supportsInterrupt)
            {
                ceilingLost = true; CeilingLostCount++;
                Debug.Log($"[BossBattle] {bossName} lost the ceiling at x={worldX:F1}: dropping and back to normal moves");
                InterruptAI(DropAfterLostCeiling());
            }
        }
    }

    // ---- 地形に合った行動(2026-10-07): 天井へ張り付く技は、本物の天井があり、ボスが収まる空間がある時だけ ----
    public static int CeilingLostCount, CeilingSkipCount; // 確認用
    public static string LastCeilingSkip = "";
    const float CeilingMinSpace = 4.2f;
    protected override bool IntentionallyAway => buried; // 地中に潜っている間は、画面から外れても戻さない(技の一部)
    bool ceilingLost;
    // その位置の本物の天井までの高さ(m)。天井が無い/低すぎる/高すぎる(画面の外)なら -1
    protected float RealCeilingSpace(float x)
    {
        var tm = TerrainManager.Instance;
        if (tm == null) return -1f;
        float? c = tm.GetEffectiveCeilingHeightAt(x);
        if (!c.HasValue) return -1f;
        float g = tm.GetHeightAt(x) ?? GroundY; // 天井の下が穴でも、天井そのものには張り付ける(高さはボスの足元の地面から)
        float space = c.Value - g;
        // ぶら下がる高さは元から地面+4.6m以上(CaveHazard.CeilingAt)。それより低い天井(4.2m未満)や、高すぎて画面の外の天井は使わない
        return space >= CeilingMinSpace && space <= 9.5f ? space : -1f;
    }
    // 張り付く場所(今の位置の前後)に、張り付ける天井があるか。技の途中で天井が低くなる/途切れる所は、
    // 張り付いた後に OnBattleTick が見つけて降ろす(技の全区間まで求めると、低い天井が混ざる洞窟ではほとんど選べなくなる)
    protected bool CanUseCeiling(float seconds)
    {
        float ahead = 12f + TargetBaseSpeed() * Mathf.Min(seconds, 0.6f);
        for (float x = worldX - 3f; x <= worldX + ahead; x += 2.5f)
            if (RealCeilingSpace(x) < 0f)
            {
                CeilingSkipCount++;
                var tm = TerrainManager.Instance;
                float? c = tm != null ? tm.GetEffectiveCeilingHeightAt(x) : null, g = tm != null ? tm.GetHeightAt(x) : null;
                LastCeilingSkip = $"x={x:F1} (boss {worldX:F1}, ahead {ahead:F0}m) ceiling={(c.HasValue ? c.Value.ToString("F1") : "none")} ground={(g.HasValue ? g.Value.ToString("F1") : "none")} need>={CeilingMinSpace:F1}";
                return false;
            }
        return true;
    }
    IEnumerator DropAfterLostCeiling()
    {
        onCeiling = true; extraScale = new Vector2(1f, -1f); freeGap = false; facingLocked = false;
        yield return DropFromCeiling(0.3f);
        ceilingLost = false;
        yield return Recover(0.5f);
    }

    protected override void OnInterrupted()
    {
        if (ceilingLost) { climbing = false; return; } // 天井を見失った: 今の高さから滑らかに降りる(DropAfterLostCeiling)
        if (onCeiling || buried) { yOffset = restAltitude; onCeiling = false; buried = false; }
        climbing = false;
        SetHpBarOffset(bodyHeight + 0.6f);
        if (tunnelTell != null) { tunnelTell.EndEarly(); tunnelTell = null; }
        CaveDarkness.Set(0f);
    }

    // ---------------- 地形の攻撃(relX = プレイヤーからの距離) ----------------
    static bool Covers(float relX, float width) => Mathf.Abs(relX) < width * 0.5f + CaveBossSafety.ColumnHalf;

    protected CaveHazard FloorAt(float relX, float width, float height, float warn, float active, CaveLook look, bool heavy = false)
    {
        float extra = Covers(relX, width) ? CaveBossSafety.Reserve(false, warn, active) : 0f;
        return CaveHazard.Floor(PlayerX + relX, width, height, warn + extra, active, look, Dmg(heavy), heavy, Src("floor"));
    }

    // 天井から(地面にいれば当たらない)。プレイヤーの列を覆う時、前方に穴があれば出さない(穴を跳べなくなるため): 予告のひびだけ
    protected CaveHazard CeilAt(float relX, float width, float warn, float active, CaveLook look, bool heavy = false, float bottom = 2.3f)
    {
        bool cover = Covers(relX, width);
        if (cover && CaveBossSafety.PitDuring(warn, warn + active)) { CaveBossSafety.PitSkips++; return TellAt(relX, warn, CaveTellStyle.Crack, CaveLook.Rock, false); }
        float extra = cover ? CaveBossSafety.Reserve(true, warn, active) : 0f;
        return CaveHazard.Ceiling(PlayerX + relX, width, bottom, warn + extra, active, look, Dmg(heavy), heavy, Src("ceiling"));
    }

    // 指定の高さの帯(視線/ブレス)。yLow>=1.9は天井側(地面にいる)、それ以外は床側(跳ぶ)
    protected CaveHazard BandAt(float relX, float width, float yLow, float yHigh, float warn, float active, CaveLook look, bool heavy = false, int damage = -1, float slow = 1f, float slowDur = 0f)
    {
        bool ceil = yLow >= 1.9f, cover = Covers(relX, width);
        if (cover && ceil && CaveBossSafety.PitDuring(warn, warn + active)) { CaveBossSafety.PitSkips++; return TellAt(relX, warn, CaveTellStyle.Crack, CaveLook.Rock, false); }
        float extra = cover ? CaveBossSafety.Reserve(ceil, warn, active) : 0f;
        var h = CaveHazard.Band(PlayerX + relX, width, yLow, yHigh, warn + extra, active, look, damage >= 0 ? damage : Dmg(heavy), heavy, Src("band"), slow, slowDur);
        return h;
    }

    // 落石: 天井から落ち、着地した岩がプレイヤーへ転がってくる(普通=跳ぶ / 巨大=二段ジャンプ)
    protected CaveHazard RockAt(float relX, float size, float warn, float roll = 6.5f, bool heavy = false, CaveLook look = CaveLook.Rock)
    {
        float arrive = warn + 0.35f + Mathf.Max(0f, relX - size * 0.5f - 0.4f) / Mathf.Max(1f, roll);
        float extra = CaveBossSafety.ReserveDrifting(false, arrive, (size + 0.8f) / Mathf.Max(1f, roll));
        return CaveHazard.FallRock(PlayerX + relX, size, warn + extra, roll, look, Dmg(heavy), heavy, Src("rock"));
    }

    // 毒だまり: 前方に広がり、プレイヤーの方へ流れてくる(跳び越える)
    protected CaveHazard PoolAt(float relX, float width, float duration = 3.2f, float drift = 4f)
    {
        float arrive = Mathf.Max(0.35f, Mathf.Max(0f, relX - width * 0.5f) / Mathf.Max(0.5f, drift)); // 広がる間も流れている
        float extra = CaveBossSafety.ReserveDrifting(false, arrive, (width + 0.8f) / Mathf.Max(0.5f, drift));
        return CaveHazard.Pool(PlayerX + relX + extra * drift, width, duration, drift, HitDmg, Src("pool")); // 遅らせる分だけ遠くに
    }

    // 結晶柱/石柱: せり上がって流れてくる障害物。攻撃で壊せる(壊すか跳ぶ)
    protected CaveHazard PillarAt(float relX, float height, float warn, int hits, float drift = 4f, CaveLook look = CaveLook.Crystal, bool heavy = false)
    {
        relX = Mathf.Max(relX, warn * drift + 1.5f); // せり上がる前にプレイヤーへ届かない距離から(予告中も流れている)
        float arrive = Mathf.Max(0f, relX - 0.7f) / Mathf.Max(0.5f, drift);
        float extra = CaveBossSafety.ReserveDrifting(false, arrive, 2f / Mathf.Max(0.5f, drift));
        return CaveHazard.Pillar(PlayerX + relX + extra * drift, 1.1f, height, warn, hits, drift, look, Dmg(heavy), heavy, Src("pillar")); // 遅らせる分だけ遠くに
    }

    // 横切る体/衝撃波(speed>0: 前方からプレイヤーへ / speed<0: 後方から前へ)。判定は見た目の shrink 倍
    protected CaveHazard WaveFrom(float startRelX, float length, float yLow, float yHigh, float speed, CaveLook look, bool heavy = false, float shrink = 0.8f)
    {
        float sp = Mathf.Max(1f, Mathf.Abs(speed));
        float extra = CaveBossSafety.ReserveDrifting(yLow >= 1.9f, Mathf.Max(0f, Mathf.Abs(startRelX) - length * 0.5f) / sp, (length * shrink + 0.8f) / sp);
        return CaveHazard.Wave(PlayerX + startRelX + Mathf.Sign(startRelX == 0f ? speed : startRelX) * extra * sp, length, yLow, yHigh, speed, look, Dmg(heavy), heavy, shrink, Src("wave"));
    }

    protected CaveHazard TellAt(float relX, float dur, CaveTellStyle style, CaveLook look = CaveLook.Dirt, bool strong = true, bool followMe = false)
    {
        return CaveHazard.Tell(followMe ? worldX : PlayerX + relX, dur, style, look, strong, followMe ? transform : null, Src("tell"));
    }

    // 地面すれすれの衝撃波(跳ぶ)。ボスの正面から
    protected CaveHazard ShockFromFront(float height, float speed, CaveLook look, bool heavy = false, float length = 1.3f)
    {
        float rel = worldX + facing * (FrontReach + 0.6f) - PlayerX;
        return WaveFrom(rel, length, 0f, height, speed * -Mathf.Sign(facing), look, heavy, 0.75f);
    }

    // ---------------- 動き ----------------
    bool climbing;
    // 壁を登って天井へ(天井では体を上下逆に。天井にいる間は攻撃が届かない=無敵)
    protected IEnumerator ClimbToCeiling(float dur)
    {
        // 2026-10-07: 天井にいる間も攻撃が届けば当たる(以前は登る時から無敵+被弾判定を切っていた)。被弾判定は体の向きに合わせて上下反転する
        onCeiling = true; climbing = true; ceilingLost = false;
        SetPose(Pose.Move);
        float start = yOffset, t = 0f;
        while (t < dur && !IsDead)
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / dur);
            float target = Mathf.Max(2.8f, CeilY(worldX) - GroundY);
            yOffset = Mathf.Lerp(start, target, f * f * (3f - 2f * f));
            extraScale = new Vector2(1f, Mathf.Lerp(1f, -1f, Mathf.Clamp01((f - 0.35f) / 0.5f)));
            if (Random.value < 0.25f) ImpactDust(new Vector3(worldX, GroundY + yOffset, 0f), 2, 0.6f);
            yield return null;
        }
        extraScale = new Vector2(1f, -1f);
        SetHpBarOffset(-bodyHeight - 0.5f);
        climbing = false;
    }

    protected IEnumerator DropFromCeiling(float dur)
    {
        climbing = true;
        float start = yOffset, t = 0f;
        SetPose(Pose.Fly);
        while (t < dur && !IsDead)
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / dur);
            yOffset = Mathf.Lerp(start, restAltitude, f * f);
            extraScale = new Vector2(1f, Mathf.Lerp(-1f, 1f, Mathf.Clamp01(f * 1.6f)));
            yield return null;
        }
        yOffset = restAltitude; extraScale = Vector2.one;
        onCeiling = false; climbing = false;
        SetHurtboxEnabled(true); invulnerable = false;
        SetHpBarOffset(bodyHeight + 0.6f);
        SetPose(Pose.Landing);
        Shake(0.12f, 0.2f);
        ImpactDust(new Vector3(worldX, GroundY, 0f), 10, 1.1f);
    }

    // 地中へ(無敵・見えない)。地面の盛り上がりの予兆(tunnelTell)が位置を知らせる
    protected IEnumerator Burrow(float dur, bool tell = true)
    {
        buried = true; invulnerable = true; SetHurtboxEnabled(false);
        ImpactDust(new Vector3(worldX, GroundY, 0f), 8, 0.9f);
        float t = 0f, startY = yOffset;
        while (t < dur && !IsDead) { t += Time.deltaTime; float f = Mathf.Clamp01(t / dur); yOffset = Mathf.Lerp(startY, -bodyHeight * 1.05f, f); SetAlpha(1f - f); yield return null; }
        SetAlpha(0f);
        if (tell && tunnelTell == null) tunnelTell = TellAt(0f, 30f, CaveTellStyle.Bulge, CaveLook.Dirt, true, true);
    }

    protected IEnumerator Surface(float dur)
    {
        if (tunnelTell != null) { tunnelTell.EndEarly(); tunnelTell = null; }
        ImpactDust(new Vector3(worldX, GroundY, 0f), 12, 1.2f);
        float t = 0f, startY = yOffset;
        while (t < dur && !IsDead) { t += Time.deltaTime; float f = Mathf.Clamp01(t / dur); yOffset = Mathf.Lerp(startY, restAltitude, f); SetAlpha(f); yield return null; }
        yOffset = restAltitude; SetAlpha(1f);
        SetHurtboxEnabled(true); invulnerable = false; buried = false;
    }

    // 地中を移動(間合い targetGap へ)
    protected IEnumerator TunnelTo(float targetGap, float speed, float maxDur)
    {
        freeGap = true;
        float t = 0f;
        while (t < maxDur && !IsDead && Mathf.Abs(Gap - targetGap) > 0.35f)
        {
            relVelocity = Mathf.Sign(targetGap - Gap) * Mathf.Min(speed, Mathf.Abs(targetGap - Gap) * 6f + 1f);
            t += Time.deltaTime;
            yield return null;
        }
        relVelocity = 0f;
        freeGap = false;
    }

    // 地中から飛び出す攻撃: 位置の予兆(盛り上がり)→床の予告(赤/金)→飛び出し(床の攻撃と同時に体が出る)
    protected IEnumerator EmergeStrike(float width, float height, float warn, CaveLook look, bool heavy = false, float active = 0.3f)
    {
        var hz = FloorAt(Gap, width, height, warn, active, look, heavy);
        float t = 0f;
        while (t < warn - 0.12f && !IsDead) { t += Time.deltaTime; relVelocity = 0f; yield return null; }
        if (height > 2f) extraScale = new Vector2(1.25f, 1.25f);
        yield return Surface(0.2f);
        SetPose(Pose.Attack);
        PlayAttackPose(0.3f);
        Shake(heavy ? 0.16f : 0.08f, 0.2f);
        yield return Wait(active);
        extraScale = Vector2.one;
    }

    // 近接の基本形
    protected IEnumerator Melee(BossHitbox hb, BossTelegraphMarker mk, float stopDist, float speed, float warn, float active, float recover, float shake = 0f)
    {
        hb.damageAmount = HitDmg;
        yield return Approach(stopDist, speed, 5f);
        yield return Telegraph(warn, mk);
        yield return Strike(hb, active, shake);
        yield return Recover(recover * RecoverMul);
    }

    // 低い突進(跳ぶ)
    protected IEnumerator Rush(BossHitbox hb, BossTelegraphMarker mk, float fromGap, float warn, float dur, float speed, bool heavy)
    {
        yield return MoveToGap(fromGap, 6f, 1.6f);
        hb.damageAmount = Dmg(heavy);
        yield return Telegraph(warn, mk);
        StartCoroutine(DashMove(dur, speed));
        yield return Strike(hb, dur, 0.05f);
        relVelocity = 0f;
    }

    protected void Glow(float seconds, Color c) { StartCoroutine(GlowRoutine(seconds, c)); }
    IEnumerator GlowRoutine(float seconds, Color c)
    {
        float t = 0f;
        while (t < seconds && !IsDead) { t += Time.deltaTime; SetBodyTint(Color.Lerp(Color.white, c, Mathf.PingPong(t * 4f, 1f))); yield return null; }
        SetBodyTint(Color.white);
    }

    protected void FakeShot(Color c, float relX)
    {
        Vector3 from = FrontWorld(0.3f, bodyHeight * 0.75f);
        var p = Projectile(BossFx.Orb(), c, from, new Vector2(0.6f, 0.6f), new Vector2((PlayerX + relX - from.x) * 1.6f, 4f), 0.5f, false);
        p.damage = false;
    }

    // 必殺技の後の大きな隙(種類ごとに見た目を変える)。崩しが溜まりやすく、ダメージも少し増える
    // groundAlt >= 0 または 負の値の指定: 隙の間だけその高さまで降りる(飛ぶボス/雲海のボスを攻撃の届く所へ)。float.NaN = 動かさない
    protected IEnumerator CaveRecovery(float seconds, string banner, Vector2 squash, CaveLook fx, Vector2 fxAt, float staggerMul = 2.2f, float dmgMul = 1.25f, float groundAlt = float.NaN)
    {
        EndUltimate();
        if (onCeiling) yield return DropFromCeiling(0.25f);
        if (buried) yield return Surface(0.25f);
        facingLocked = false;
        if (player != null) facing = PlayerX < worldX ? -1f : 1f;
        freeGap = false;
        BossBattleHud.Banner(banner, new Color(0.6f, 1f, 0.6f), 1.2f);
        Debug.Log($"[CaveBoss] {bossName} RECOVERY '{banner}' {seconds:F1}s");
        StartCoroutine(RecoveryLook(seconds, squash, fx, fxAt));
        float keepRest = restAltitude;
        bool lower = !float.IsNaN(groundAlt);
        if (lower) { restAltitude = groundAlt; StartCoroutine(SetAltitude(groundAlt, 0.35f)); }
        yield return Exhausted(seconds * (Enraged ? 0.85f : 1f), staggerMul, dmgMul);
        extraScale = Vector2.one;
        if (lower) { restAltitude = keepRest; yield return SetAltitude(keepRest, 0.5f); }
    }

    IEnumerator RecoveryLook(float seconds, Vector2 squash, CaveLook fx, Vector2 fxAt)
    {
        float t = 0f, fxT = 0f;
        Color c = CaveHazard.LookColor(fx);
        while (t < seconds && !IsDead)
        {
            t += Time.deltaTime; fxT -= Time.deltaTime;
            float k = Mathf.Clamp01(t / 0.25f) * Mathf.Clamp01((seconds - t) / 0.3f);
            extraScale = Vector2.Lerp(Vector2.one, squash, k);
            if (fxT <= 0f)
            {
                fxT = 0.3f;
                Vector3 p = transform.position + new Vector3(fxAt.x * halfWidth * (facing < 0f ? -1f : 1f), fxAt.y * bodyHeight * Mathf.Abs(extraScale.y), 0f);
                OneShotSpriteEffect.CreateTweened(OneShotSpriteEffect.SoftDotSprite(), p, c, 0.45f, 0.35f, 0.9f, 0.9f, 0f, new Vector3(0f, 0.8f, 0f), 0f, RenderOrder.CombatFx, 0.1f);
            }
            yield return null;
        }
        extraScale = Vector2.one;
    }

    // 必殺技の始まり(他のボスが必殺技中なら後回し)
    protected bool StartUlt(string title, Color c)
    {
        if (!BeginUltimate(title, c)) { lastUltimateTime = Time.time - 10f; return false; }
        return true;
    }
}

