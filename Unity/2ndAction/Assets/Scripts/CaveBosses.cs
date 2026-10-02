using System.Collections;
using UnityEngine;

// 自然洞窟ボス追加(2026-09-22) - 荒野街道ボス(WildBossBase/WildBosses.cs)を
// そのまま基盤として使い、自然洞窟用の11体(1,000m〜90,000m)を追加する。
// 100,000mの死神は既存のGrimReaperController/BossManager.SpawnDeathをその
// まま流用する(ステージ非依存で元から実装済み)ため、ここには含めない。
//
// 洞窟特有の要素(天井/地中)は、WildBossBase.cs追加分のCeilingWorldYAt/
// PassageMinClearance(荒野街道ボスの動作には一切影響しない追加のみ)と、
// CaveBossFx.cs(手続き的な仮ボディ/CeilingFallRock)を使う。それ以外の
// AI構造(Approach/Telegraph/Strike/Recover/Stagger等)・複数体管理
// (slotIndex/BossAttackGate)・HP/Hit/Death・走行との相対移動は、荒野街道
// ボスと完全に同一の仕組みをそのまま使う。
public enum CaveBossKind { Centipede, Scorpion, Mole, Troll, Worm, CrystalGolem, Bat, ScorpionKing, Basilisk, Drake, AncientDemon }

// ============ 1,000m 巨大ムカデ ============
// 荒野街道の巨大オオカミに相当。距離が伸びると複数出現(BossManager側で
// count管理)。噛みつき中心、たまに身体を丸めて薙ぎ払う。
public class CentipedeBoss : WildBossBase
{
    BossHitbox bite, sweep;
    BossTelegraphMarker biteMark, sweepMark;

    protected override void OnInit()
    {
        interruptible = true;
        locoStyle = LocoStyle.Crawl;
        enterSpeed = 8.5f;
        Vector2 c = new Vector2(FrontReach + 0.6f, bodyHeight * 0.35f), s = new Vector2(2.0f, 1.1f);
        bite = NewHitbox("Bite", c, s, BossFx.Fang(), new Color(1f, 1f, 1f, 0.95f));
        biteMark = NewMarker(c, s);
        Vector2 sc = new Vector2(FrontReach + 0.2f, bodyHeight * 0.3f), ss = new Vector2(2.6f, 1.4f);
        sweep = NewHitbox("Sweep", sc, ss, BossFx.Slash(), new Color(0.8f, 0.9f, 0.6f, 0.9f));
        sweepMark = NewMarker(sc, ss);
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (Random.value < 0.25f)
            {
                yield return Approach(1.6f, 2.6f, 6f);
                yield return Telegraph(0.7f, sweepMark);
                if (interrupted) { yield return Stagger(0.9f); continue; }
                yield return Strike(sweep, 0.30f, 0.05f);
                yield return Recover(0.9f);
                continue;
            }
            yield return Approach(1.3f, 2.8f, 6f);
            yield return Telegraph(0.55f, biteMark);
            if (interrupted) { yield return Stagger(0.8f); continue; }
            yield return Strike(bite, 0.24f);
            yield return Recover(0.7f);
        }
    }
}

// ============ 5,000m 巨大サソリ ============
// 荒野街道のゴブリン・ウルフライダーに相当。近距離ハサミ/中距離毒針/
// 遠距離毒弾の3択。毒弾は複数体でも弾幕にならないよう共有Cooldownを使う。
static class ScorpionBoltGate { public static float NextTime; public static float Interval = 1.5f; }

public class ScorpionBoss : WildBossBase
{
    BossHitbox claw, sting;
    BossTelegraphMarker clawMark, stingMark;
    SpriteRenderer boltGlow;
    float lastBoltTime = -99f;
    public float boltCooldown = 3.6f;

    protected override void OnInit()
    {
        interruptible = true;
        locoStyle = LocoStyle.Crawl;
        enterSpeed = 8f;
        Vector2 cc = new Vector2(FrontReach + 0.5f, bodyHeight * 0.4f), cs = new Vector2(2.2f, 1.6f);
        claw = NewHitbox("Claw", cc, cs, BossFx.Fang(), new Color(0.9f, 0.7f, 1f, 0.95f));
        clawMark = NewMarker(cc, cs);
        Vector2 sc = new Vector2(FrontReach + 0.3f, bodyHeight * 0.75f), ss = new Vector2(2.6f, 1.0f);
        sting = NewHitbox("Sting", sc, ss, BossFx.Orb(), new Color(0.5f, 1f, 0.4f, 0.95f));
        stingMark = NewMarker(sc, ss);

        GameObject g = new GameObject("BoltGlow");
        g.transform.SetParent(transform, false);
        boltGlow = g.AddComponent<SpriteRenderer>();
        boltGlow.sprite = BossFx.Orb();
        boltGlow.color = new Color(0.4f, 1f, 0.35f, 0.95f);
        boltGlow.sortingOrder = RenderOrder.Boss + 1;
        boltGlow.enabled = false;
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (FrontDist > 6.5f && Time.time - lastBoltTime > boltCooldown && Time.time >= ScorpionBoltGate.NextTime)
            {
                lastBoltTime = Time.time;
                ScorpionBoltGate.NextTime = Time.time + ScorpionBoltGate.Interval;
                yield return PoisonBolt();
                continue;
            }
            if (FrontDist > 3.2f)
            {
                yield return Approach(2.8f, 2.4f, 5f);
                yield return Telegraph(0.7f, stingMark);
                if (interrupted) { yield return Stagger(0.8f); continue; }
                yield return Strike(sting, 0.24f);
                yield return Recover(0.9f);
                continue;
            }
            yield return Approach(1.3f, 2.6f, 5f);
            yield return Telegraph(0.6f, clawMark);
            if (interrupted) { yield return Stagger(0.8f); continue; }
            yield return Strike(claw, 0.26f);
            yield return Recover(0.8f);
        }
    }

    IEnumerator PoisonBolt()
    {
        boltGlow.enabled = true;
        yield return Telegraph(0.9f);
        boltGlow.enabled = false;
        if (interrupted) yield break;
        Vector3 from = FrontWorld(0.4f, bodyHeight * 0.7f);
        Vector2 dir = AimFrom(from);
        GameObject fb = BossProjectile.Create(BossFx.Orb(), new Color(0.4f, 1f, 0.35f), from, new Vector2(0.7f, 0.7f), dir * 8f, 3f, RenderOrder.Boss + 1).gameObject;
        PlayAttackPose(0.25f);
        yield return Wait(0.25f);
        yield return Recover(0.8f);
    }
}

// ============ 10,000m 巨大モグラ ============
// 地中移動を初めて本格利用。地上→飛び出し(攻撃)→爪→再潜行→地中追跡、を繰り返す。
public class MoleBoss : WildBossBase
{
    BossHitbox claw, emerge;
    BossTelegraphMarker clawMark;
    bool buried;

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Crawl;
        enterSpeed = 7f;
        Vector2 cc = new Vector2(FrontReach + 0.4f, bodyHeight * 0.4f), cs = new Vector2(2.2f, 1.6f);
        claw = NewHitbox("Claw", cc, cs, BossFx.Slash(), new Color(0.7f, 0.55f, 0.4f, 0.95f));
        clawMark = NewMarker(cc, cs);
        emerge = NewHitbox("Emerge", new Vector2(0f, bodyHeight * 0.5f), new Vector2(2.6f, bodyHeight * 1.3f), BossFx.Ring(), new Color(0.7f, 0.55f, 0.4f, 0.9f));
    }

    protected override IEnumerator AI()
    {
        // 開始直後は地上に出た状態(Enterで出現済み)なので、まず一度地中へ。
        yield return Submerge(0.35f);
        while (true)
        {
            float targetGap = Random.Range(1.5f, 3.5f);
            yield return TrackUnderground(targetGap, 6.5f, 5f);

            float emergeX = worldX;
            TrackedHazard hz = TrackedHazard.Create(emergeX, 2.6f, 0.25f, 0.6f, 0f, new Color(0.75f, 0.55f, 0.35f, 0.55f));
            yield return Wait(0.6f);
            yield return Emerge(0.3f);
            if (IsDead) yield break;
            yield return Strike(emerge, 0.22f, 0.08f);
            yield return Recover(0.5f);
            if (IsDead) yield break;

            yield return Telegraph(0.6f, clawMark);
            if (interrupted) { yield return Stagger(0.8f); }
            else
            {
                yield return Strike(claw, 0.26f);
                yield return Recover(0.6f);
            }

            yield return Submerge(0.4f);
        }
    }

    IEnumerator Submerge(float duration)
    {
        buried = true;
        invulnerable = true;
        SetHurtboxEnabled(false);
        float t = 0f, startY = yOffset;
        while (t < duration) { t += Time.deltaTime; float f = t / duration; yOffset = Mathf.Lerp(startY, -bodyHeight * 0.95f, f); SetAlpha(1f - f); yield return null; }
        SetAlpha(0f);
    }

    IEnumerator Emerge(float duration)
    {
        float t = 0f;
        while (t < duration) { t += Time.deltaTime; float f = t / duration; yOffset = Mathf.Lerp(-bodyHeight * 0.95f, 0f, f); SetAlpha(f); yield return null; }
        yOffset = 0f; SetAlpha(1f);
        SetHurtboxEnabled(true);
        invulnerable = false;
        buried = false;
    }

    IEnumerator TrackUnderground(float targetGap, float speed, float maxDuration)
    {
        float t = 0f, dustTimer = 0f;
        while (t < maxDuration && Mathf.Abs(Gap - targetGap) > 0.5f)
        {
            relVelocity = Mathf.Sign(targetGap - Gap) * speed;
            dustTimer -= Time.deltaTime;
            if (dustTimer <= 0f) { ImpactDust(new Vector3(worldX, GroundY + 0.05f, 0f), 2, 0.5f); dustTimer = 0.14f; }
            t += Time.deltaTime;
            yield return null;
        }
        relVelocity = 0f;
    }
}

// ============ 20,000m ケイブトロル ============
// 大股の重量級。棍棒叩きつけ/薙ぎ払い/落石(天井)の3択。
public class TrollBoss : WildBossBase
{
    BossHitbox slam, sweep;
    BossTelegraphMarker slamMark, sweepMark;
    int lastAttack = -1;

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Stride;
        footstepShake = true;
        Vector2 sc = new Vector2(FrontReach + 0.6f, 0.3f), ss = new Vector2(2.6f, 1.8f);
        slam = NewHitbox("Slam", sc, ss, BossFx.Ring(), new Color(1f, 0.75f, 0.3f, 0.95f));
        slamMark = NewMarker(sc, ss);
        Vector2 wc = new Vector2(FrontReach - 0.4f, bodyHeight * 0.45f), ws = new Vector2(3.2f, 1.6f);
        sweep = NewHitbox("Sweep", wc, ws, BossFx.Slash(), new Color(1f, 0.6f, 0.3f, 0.95f));
        sweepMark = NewMarker(wc, ws);
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            yield return Approach(2.2f, 1.8f, 6f);
            int pick = BossAiUtil.PickNoRepeat(3, ref lastAttack);
            if (pick == 0)
            {
                yield return Telegraph(1.0f, slamMark);
                if (interrupted) { yield return Stagger(1.0f); continue; }
                StartCoroutine(GroundRingDelayed(0.05f));
                yield return Strike(slam, 0.24f, 0.1f);
                yield return Recover(0.9f);
            }
            else if (pick == 1)
            {
                yield return Telegraph(0.8f, sweepMark);
                if (interrupted) { yield return Stagger(0.9f); continue; }
                yield return Strike(sweep, 0.3f, 0.08f);
                yield return Recover(0.8f);
            }
            else
            {
                yield return RockfallAttack();
            }
        }
    }

    IEnumerator GroundRingDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (!IsDead) GroundRing(FrontWorld(0.8f, 0f), 3.5f, new Color(1f, 0.7f, 0.3f, 0.6f));
    }

    // 天井を棍棒で叩き、プレイヤー前方の天井に亀裂→少し遅れて岩が落下。
    // 天井が存在しない区間(通常天井の生成が間に合っていない等)では安全側
    // として棍棒叩きつけ攻撃で代替する。
    IEnumerator RockfallAttack()
    {
        float targetX = PlayerX + Random.Range(2.5f, 5.5f);
        float? ceilY = CeilingWorldYAt(targetX);
        if (!ceilY.HasValue) { yield return Telegraph(1.0f, slamMark); if (!interrupted) { yield return Strike(slam, 0.24f, 0.1f); } yield return Recover(0.9f); yield break; }

        float floorY = TerrainManager.Instance != null ? TerrainManager.Instance.GetFloorTopAt(targetX) : GroundY;
        TrackedHazard.Create(targetX, 2.2f, 0.2f, 0.9f, 0f, new Color(1f, 0.5f, 0.15f, 0.5f));

        windingUp = true; interrupted = false; facingLocked = true; SetPose(Pose.Windup);
        float t = 0f, dur = 0.9f;
        while (t < dur) { t += Time.deltaTime; windupProgress = t / dur; if (interrupted) break; yield return null; }
        windingUp = false; facingLocked = false;
        if (interrupted) { yield return Stagger(1.0f); yield break; }

        SetPose(Pose.Idle);
        CeilingFallRock.Create(targetX, ceilY.Value, floorY, 0.3f, 2.2f, 1.4f);
        Shake(0.08f, 0.15f);
        yield return Recover(0.8f);
    }
}

// ============ 30,000m 巨大地底ワーム ============
// 地中移動中は本体を非表示(専用地面VFXで位置を伝える)。垂直/前方/横断の
// 3種の飛び出し攻撃。
public class WormBoss : WildBossBase
{
    BossHitbox erupt, cross;
    int lastAttack = -1;

    protected override IEnumerator Enter()
    {
        SetPose(Pose.Move);
        yield return base.Enter();
        yield return Submerge(0.4f);
    }

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Slither;
        Vector2 ec = new Vector2(0f, bodyHeight * 0.5f), es = new Vector2(2.6f, bodyHeight * 1.4f);
        erupt = NewHitbox("Erupt", ec, es, BossFx.Ring(), new Color(0.4f, 0.9f, 0.5f, 0.9f));
        Vector2 cc = new Vector2(0f, bodyHeight * 0.4f), cs = new Vector2(bodyHeight * 2.6f, bodyHeight * 1.1f);
        cross = NewHitbox("Cross", cc, cs, BossFx.Slash(), new Color(0.5f, 0.85f, 0.55f, 0.9f));
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            int pick = BossAiUtil.PickNoRepeat(3, ref lastAttack);
            if (pick == 0) yield return VerticalBurst();
            else if (pick == 1) yield return ForwardBurst();
            else yield return CrossSweep();
        }
    }

    IEnumerator VerticalBurst()
    {
        yield return TrackUnderground(0f, 7f, 5f);
        TrackedHazard.Create(worldX, 2.8f, 0.25f, 0.7f, 0f, new Color(0.4f, 0.9f, 0.5f, 0.5f));
        yield return Wait(0.7f);
        yield return Emerge(0.3f);
        if (IsDead) yield break;
        yield return Strike(erupt, 0.26f, 0.1f);
        yield return Recover(0.6f);
        if (!IsDead) yield return Submerge(0.4f);
    }

    IEnumerator ForwardBurst()
    {
        yield return TrackUnderground(Random.Range(4f, 6.5f), 8f, 5f);
        TrackedHazard.Create(worldX, 2.8f, 0.25f, 0.7f, 0f, new Color(0.4f, 0.9f, 0.5f, 0.5f));
        yield return Wait(0.7f);
        yield return Emerge(0.28f);
        if (IsDead) yield break;
        StartCoroutine(DashMove(0.35f, 4f));
        yield return Strike(erupt, 0.3f, 0.1f);
        yield return Recover(0.7f);
        if (!IsDead) yield return Submerge(0.4f);
    }

    // 身体そのものが一時的な障害物として横切る。回避不能にならないよう
    // 予兆(TrackedHazardの警告時間)を長めに取る。
    IEnumerator CrossSweep()
    {
        yield return TrackUnderground(Random.Range(9f, 12f), 9f, 5f);
        TrackedHazard.Create(worldX, bodyHeight * 2.8f, 0.3f, 1.1f, 0f, new Color(0.4f, 0.9f, 0.5f, 0.5f));
        yield return Wait(1.1f);
        yield return Emerge(0.25f);
        if (IsDead) yield break;
        StartCoroutine(DashMove(0.9f, -10f));
        yield return Strike(cross, 0.85f, 0.1f);
        relVelocity = 0f;
        yield return Recover(0.6f);
        if (!IsDead) yield return Submerge(0.4f);
    }

    IEnumerator Submerge(float duration)
    {
        invulnerable = true;
        SetHurtboxEnabled(false);
        float t = 0f, startY = yOffset;
        while (t < duration) { t += Time.deltaTime; float f = t / duration; yOffset = Mathf.Lerp(startY, -bodyHeight * 1.1f, f); SetAlpha(1f - f); yield return null; }
        SetAlpha(0f);
    }

    IEnumerator Emerge(float duration)
    {
        float t = 0f;
        while (t < duration) { t += Time.deltaTime; float f = t / duration; yOffset = Mathf.Lerp(-bodyHeight * 1.1f, 0f, f); SetAlpha(f); yield return null; }
        yOffset = 0f; SetAlpha(1f);
        SetHurtboxEnabled(true);
        invulnerable = false;
    }

    IEnumerator TrackUnderground(float targetGap, float speed, float maxDuration)
    {
        float t = 0f, dustTimer = 0f;
        while (t < maxDuration && Mathf.Abs(Gap - targetGap) > 0.6f)
        {
            relVelocity = Mathf.Sign(targetGap - Gap) * speed;
            dustTimer -= Time.deltaTime;
            if (dustTimer <= 0f) { ImpactDust(new Vector3(worldX, GroundY + 0.05f, 0f), 3, 0.7f); dustTimer = 0.12f; }
            t += Time.deltaTime;
            yield return null;
        }
        relVelocity = 0f;
    }
}

// ============ 40,000m クリスタルゴーレム ============
// 重量感のある歩行。拳叩きつけ/結晶生成(予兆→突き出し)/結晶弾の3択。
public class CrystalGolemBoss : WildBossBase
{
    BossHitbox punch;
    BossTelegraphMarker punchMark;
    int lastAttack = -1;

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Stride;
        footstepShake = true;
        hitStopOnHit = 0.045f;
        Vector2 pc = new Vector2(FrontReach + 0.5f, 0.2f), ps = new Vector2(2.8f, 2.0f);
        punch = NewHitbox("Punch", pc, ps, BossFx.Ring(), new Color(0.6f, 0.9f, 1f, 0.95f));
        punchMark = NewMarker(pc, ps);
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            yield return Approach(2.4f, 1.6f, 6f);
            int pick = BossAiUtil.PickNoRepeat(3, ref lastAttack);
            if (pick == 0)
            {
                yield return Telegraph(1.1f, punchMark);
                if (interrupted) { yield return Stagger(1.0f); continue; }
                yield return Strike(punch, 0.24f, 0.1f);
                GroundRing(FrontWorld(0.8f, 0f), 3.5f, new Color(0.6f, 0.9f, 1f, 0.6f));
                yield return Recover(1.0f);
            }
            else if (pick == 1)
            {
                yield return CrystalSpikeAttack();
            }
            else
            {
                yield return CrystalBoltAttack();
            }
        }
    }

    IEnumerator CrystalSpikeAttack()
    {
        float targetX = PlayerX + Random.Range(1.5f, 3.5f) * facing;
        TrackedHazard.Create(targetX, 1.6f, 0.2f, 0.8f, 0f, new Color(0.6f, 0.9f, 1f, 0.5f));

        windingUp = true; interrupted = false; SetPose(Pose.Windup);
        float t = 0f;
        while (t < 0.8f) { t += Time.deltaTime; windupProgress = t / 0.8f; if (interrupted) break; yield return null; }
        windingUp = false;
        if (interrupted) { yield return Stagger(0.9f); yield break; }

        SetPose(Pose.Idle);
        StartCoroutine(SpawnRisingCrystal(targetX));
        yield return Recover(0.7f);
    }

    IEnumerator SpawnRisingCrystal(float x)
    {
        float floorY = TerrainManager.Instance != null ? TerrainManager.Instance.GetFloorTopAt(x) : GroundY;
        GameObject go = new GameObject("RisingCrystal");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = CaveBossFx.CrystalShard();
        sr.color = new Color(0.55f, 0.85f, 1f, 0.95f);
        sr.sortingOrder = RenderOrder.Boss;
        go.transform.position = new Vector3(x, floorY, 0f);
        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(1.2f, 1.6f);
        // マルチプレイPhase 2.5: せり上がる結晶をJOINにも見せる(当たりはHOSTが分身の位置で確定)。
        NetAttackSync.Register(go, NetAttackSync.AType.RisingCrystal, noDamage: true);
        int crystalKey = NetMatch.NewHostAttackKey();
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic; rb.gravityScale = 0f;

        float t = 0f, dur = 0.25f;
        while (t < dur)
        {
            t += Time.deltaTime; float f = t / dur;
            go.transform.localScale = new Vector3(1.4f, Mathf.Lerp(0f, 1.6f, f), 1f);
            go.transform.position = new Vector3(x, floorY + Mathf.Lerp(0f, 0.8f, f), 0f);
            if (PlayerController.Instance != null && f > 0.5f && Mathf.Abs(PlayerController.Instance.transform.position.x - x) < 0.7f)
            {
                PlayerController.Instance.TakeDamage(source: "CrystalGolem:RisingCrystal", amount: BossManager.ScaleDamage(CombatScale.PlayerHit));
            }
            if (f > 0.5f) NetMatch.HostDamageRemoteInRange(x - 0.7f, x + 0.7f, float.MinValue, float.MaxValue, "CrystalGolem:RisingCrystal", crystalKey);
            yield return null;
        }
        yield return new WaitForSeconds(1.2f);
        t = 0f;
        while (t < 0.3f) { t += Time.deltaTime; sr.color = new Color(sr.color.r, sr.color.g, sr.color.b, Mathf.Lerp(0.95f, 0f, t / 0.3f)); yield return null; }
        Destroy(go);
    }

    IEnumerator CrystalBoltAttack()
    {
        yield return Retreat(2f, 0.6f);
        windingUp = true; interrupted = false; facingLocked = true; SetPose(Pose.Windup);
        float t = 0f;
        while (t < 1.0f) { t += Time.deltaTime; windupProgress = t / 1.0f; if (interrupted) break; yield return null; }
        windingUp = false; facingLocked = false;
        if (interrupted) { yield return Stagger(1.0f); yield break; }

        Vector3 from = FrontWorld(0.4f, bodyHeight * 0.7f);
        Vector2 dir = AimFrom(from);
        BossProjectile.Create(CaveBossFx.CrystalShard(), new Color(0.55f, 0.85f, 1f), from, new Vector2(1.1f, 1.1f), dir * 5f, 3.5f, RenderOrder.Boss + 1);
        PlayAttackPose(0.3f);
        yield return Wait(0.3f);
        yield return Recover(1.0f);
    }
}

// ============ 50,000m 巨大コウモリ ============
// 天井を本格利用する飛行ボス。急降下/爪/音波の3択+天井付近での間。
public class BatBoss : WildBossBase
{
    BossHitbox claw, dive;
    BossTelegraphMarker diveMark;
    SpriteRenderer sonicGlow;
    int lastAttack = -1;

    protected override IEnumerator Enter()
    {
        yOffset = 8f;
        SetPose(Pose.Fly);
        float t = 0f;
        while (Gap > startGap || yOffset > 1.0f)
        {
            relVelocity = Gap > startGap ? -enterSpeed : 0f;
            yOffset = Mathf.MoveTowards(yOffset, 1.0f, Time.deltaTime * 4f);
            t += Time.deltaTime;
            if (t > 6f) break;
            yield return null;
        }
        relVelocity = 0f;
    }

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Wing;
        yOffset = 8f;
        Vector2 cc = new Vector2(FrontReach + 0.3f, bodyHeight * 0.4f), cs = new Vector2(2.4f, 1.6f);
        claw = NewHitbox("Claw", cc, cs, BossFx.Fang(), new Color(0.7f, 0.6f, 0.9f, 0.95f));
        Vector2 dc = new Vector2(0f, bodyHeight * 0.45f), ds = new Vector2(2.0f, bodyHeight * 1.3f);
        dive = NewHitbox("Dive", dc, ds, BossFx.Ring(), new Color(0.7f, 0.6f, 0.9f, 0.9f));
        diveMark = NewMarker(new Vector2(0f, -0.4f), new Vector2(2.4f, 0.4f));

        GameObject g = new GameObject("SonicGlow");
        g.transform.SetParent(transform, false);
        sonicGlow = g.AddComponent<SpriteRenderer>();
        sonicGlow.sprite = BossFx.Ring();
        sonicGlow.color = new Color(0.8f, 0.7f, 1f, 0.9f);
        sonicGlow.sortingOrder = RenderOrder.Boss + 1;
        sonicGlow.enabled = false;
    }

    float MaxSafeAltitude()
    {
        float? ceil = CeilingWorldY;
        if (!ceil.HasValue) return 6.5f;
        return Mathf.Max(1.5f, ceil.Value - GroundY - bodyHeight * 0.6f);
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            int pick = BossAiUtil.PickNoRepeat(3, ref lastAttack);
            if (pick == 0) yield return ClawPass();
            else if (pick == 1) yield return DiveAttack();
            else yield return SonicAttack();
        }
    }

    IEnumerator ClawPass()
    {
        yield return SetAltitude(Mathf.Min(1.1f, MaxSafeAltitude()), 0.4f);
        yield return Approach(1.6f, 3.0f, 5f);
        yield return Telegraph(0.6f);
        if (interrupted) { yield return Stagger(0.8f); yield break; }
        yield return Strike(claw, 0.24f);
        yield return Recover(0.8f);
    }

    IEnumerator DiveAttack()
    {
        float safeAlt = MaxSafeAltitude();
        yield return SetAltitude(Mathf.Min(safeAlt, 4.2f), 0.5f);
        yield return MoveToGap(startGap + 3f, 3.5f, 3f);
        float landX = PlayerX + Random.Range(-1.5f, 1.5f);
        TrackedHazard.Create(landX, 2.2f, 0.25f, 0.4f, 0f, new Color(0.7f, 0.6f, 0.9f, 0.5f));
        diveMark.Show(facing);
        float t = 0f;
        while (t < 0.7f) { t += Time.deltaTime; diveMark.SetProgress(t / 0.7f); yield return null; }
        diveMark.Hide();
        if (IsDead) yield break;
        yield return SetAltitude(1.0f, 0.28f);
        yield return Strike(dive, 0.22f, 0.1f);
        Shake(0.1f, 0.15f);
        yield return Recover(0.9f);
        yield return SetAltitude(Mathf.Min(3f, safeAlt), 0.5f);
    }

    IEnumerator SonicAttack()
    {
        sonicGlow.enabled = true;
        yield return Telegraph(0.9f);
        sonicGlow.enabled = false;
        if (interrupted) yield break;
        Vector3 from = FrontWorld(0.6f, bodyHeight * 0.5f);
        Vector2 dir = AimFrom(from);
        BossProjectile.Create(BossFx.Ring(), new Color(0.8f, 0.7f, 1f), from, new Vector2(1.6f, 1.6f), dir * 9f, 2.5f, RenderOrder.Boss + 1);
        PlayAttackPose(0.3f);
        yield return Wait(0.3f);
        yield return Recover(0.9f);
    }
}

// ============ 60,000m スコーピオンキング(地下甲殻獣) ============
// 巨大サソリの発展形。ハサミ/毒針/毒弾に加え、複数箇所からの毒噴出を追加。
static class ScorpionKingBoltGate { public static float NextTime; public static float Interval = 1.6f; }

public class ScorpionKingBoss : WildBossBase
{
    BossHitbox claw, sting;
    BossTelegraphMarker clawMark, stingMark;
    SpriteRenderer boltGlow;
    int lastAttack = -1;
    float lastBoltTime = -99f;
    public float boltCooldown = 4.2f;

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Crawl;
        footstepShake = true;
        Vector2 cc = new Vector2(FrontReach + 0.5f, bodyHeight * 0.4f), cs = new Vector2(2.6f, 2.0f);
        claw = NewHitbox("Claw", cc, cs, BossFx.Fang(), new Color(0.9f, 0.5f, 0.6f, 0.95f));
        clawMark = NewMarker(cc, cs);
        Vector2 sc = new Vector2(FrontReach + 0.3f, bodyHeight * 0.85f), ss = new Vector2(3.0f, 1.2f);
        sting = NewHitbox("Sting", sc, ss, BossFx.Orb(), new Color(0.5f, 1f, 0.4f, 0.95f));
        stingMark = NewMarker(sc, ss);

        GameObject g = new GameObject("BoltGlow");
        g.transform.SetParent(transform, false);
        boltGlow = g.AddComponent<SpriteRenderer>();
        boltGlow.sprite = BossFx.Orb();
        boltGlow.color = new Color(0.4f, 1f, 0.35f, 0.95f);
        boltGlow.sortingOrder = RenderOrder.Boss + 1;
        boltGlow.enabled = false;
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            int pick = BossAiUtil.PickNoRepeat(4, ref lastAttack);
            if (pick == 0)
            {
                yield return Approach(1.6f, 2.6f, 5f);
                yield return Telegraph(0.55f, clawMark);
                if (interrupted) { yield return Stagger(0.8f); continue; }
                yield return Strike(claw, 0.26f);
                yield return Recover(0.7f);
            }
            else if (pick == 1)
            {
                yield return Approach(3.0f, 2.4f, 5f);
                yield return Telegraph(0.6f, stingMark);
                if (interrupted) { yield return Stagger(0.8f); continue; }
                yield return Strike(sting, 0.24f);
                yield return Recover(0.8f);
            }
            else if (pick == 2 && Time.time - lastBoltTime > boltCooldown && Time.time >= ScorpionKingBoltGate.NextTime)
            {
                lastBoltTime = Time.time;
                ScorpionKingBoltGate.NextTime = Time.time + ScorpionKingBoltGate.Interval;
                boltGlow.enabled = true;
                yield return Telegraph(0.8f);
                boltGlow.enabled = false;
                if (!interrupted)
                {
                    Vector3 from = FrontWorld(0.4f, bodyHeight * 0.7f);
                    Vector2 dir = AimFrom(from);
                    BossProjectile.Create(BossFx.Orb(), new Color(0.4f, 1f, 0.35f), from, new Vector2(0.8f, 0.8f), dir * 8f, 3f, RenderOrder.Boss + 1);
                    PlayAttackPose(0.25f);
                    yield return Wait(0.25f);
                }
                yield return Recover(0.9f);
            }
            else
            {
                yield return TailSpikeAttack();
            }
        }
    }

    // 尻尾を地面へ突き刺し、プレイヤー前方の複数箇所から毒噴出。
    IEnumerator TailSpikeAttack()
    {
        float baseX = PlayerX + Random.Range(2f, 4f);
        int spots = 3;
        for (int i = 0; i < spots; i++)
        {
            TrackedHazard.Create(baseX + i * 2.2f, 1.6f, 0.2f, 0.75f + i * 0.12f, 0.5f, new Color(0.4f, 1f, 0.35f, 0.55f));
        }
        windingUp = true; interrupted = false; facingLocked = true; SetPose(Pose.Windup);
        float t = 0f;
        while (t < 0.7f) { t += Time.deltaTime; windupProgress = t / 0.7f; if (interrupted) break; yield return null; }
        windingUp = false; facingLocked = false;
        if (interrupted) { yield return Stagger(0.9f); yield break; }
        SetPose(Pose.Idle);
        Shake(0.06f, 0.12f);
        yield return Recover(1.0f);
    }
}

// ============ 70,000m バジリスク ============
// 低い姿勢での高速走行。噛みつき/尻尾/毒ブレス/石化視線(減速デバフのみ)。
public class BasiliskBoss : WildBossBase
{
    BossHitbox bite, tail, breath;
    BossTelegraphMarker biteMark, tailMark, breathMark;
    SpriteRenderer gazeGlow;
    int lastAttack = -1;

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Slither;
        enterSpeed = 10f;
        Vector2 bc = new Vector2(FrontReach + 0.5f, bodyHeight * 0.3f), bs = new Vector2(2.4f, 1.2f);
        bite = NewHitbox("Bite", bc, bs, BossFx.Fang(), new Color(0.6f, 0.9f, 0.4f, 0.95f));
        biteMark = NewMarker(bc, bs);
        Vector2 tc = new Vector2(-1.2f, bodyHeight * 0.3f), ts = new Vector2(3.0f, 1.2f);
        tail = NewHitbox("Tail", tc, ts, BossFx.Slash(), new Color(0.5f, 0.8f, 0.4f, 0.95f));
        tailMark = NewMarker(tc, ts);
        Vector2 rc = new Vector2(FrontReach + 1.2f, bodyHeight * 0.45f), rs = new Vector2(3.2f, 1.1f);
        breath = NewHitbox("Breath", rc, rs, BossFx.Orb(), new Color(0.4f, 0.9f, 0.35f, 0.85f));
        breathMark = NewMarker(rc, rs);

        GameObject g = new GameObject("GazeGlow");
        g.transform.SetParent(transform, false);
        gazeGlow = g.AddComponent<SpriteRenderer>();
        gazeGlow.sprite = BossFx.Orb();
        gazeGlow.color = new Color(0.8f, 0.9f, 0.3f, 0.95f);
        gazeGlow.sortingOrder = RenderOrder.Boss + 1;
        gazeGlow.enabled = false;
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            int pick = BossAiUtil.PickNoRepeat(4, ref lastAttack);
            if (pick == 0)
            {
                yield return Approach(1.4f, 3.0f, 5f);
                yield return Telegraph(0.5f, biteMark);
                if (interrupted) { yield return Stagger(0.8f); continue; }
                yield return Strike(bite, 0.24f);
                yield return Recover(0.7f);
            }
            else if (pick == 1)
            {
                yield return Telegraph(0.6f, tailMark);
                if (interrupted) { yield return Stagger(0.8f); continue; }
                yield return Strike(tail, 0.3f, 0.08f);
                yield return Recover(0.8f);
            }
            else if (pick == 2)
            {
                yield return Telegraph(0.7f, breathMark);
                if (interrupted) { yield return Stagger(0.9f); continue; }
                yield return Strike(breath, 0.5f, 0.05f);
                yield return Recover(1.0f);
            }
            else
            {
                yield return GazeAttack();
            }
        }
    }

    // 石化視線: 完全操作不能にはせず、命中時は短時間の減速デバフのみ。
    IEnumerator GazeAttack()
    {
        gazeGlow.enabled = true;
        yield return Telegraph(0.9f);
        gazeGlow.enabled = false;
        if (interrupted) yield break;
        Vector3 from = FrontWorld(0.5f, bodyHeight * 0.6f);
        Vector2 dir = AimFrom(from);
        GameObject go = BossProjectile.Create(BossFx.Orb(), new Color(0.85f, 0.95f, 0.35f), from, new Vector2(1.0f, 1.0f), dir * 10f, 2f, RenderOrder.Boss + 1).gameObject;
        BossProjectile bp = go.GetComponent<BossProjectile>();
        bp.damage = false;
        bp.slowFactor = 0.5f;
        bp.slowDuration = 1.6f;
        PlayAttackPose(0.3f);
        yield return Wait(0.3f);
        yield return Recover(0.9f);
    }
}

// ============ 80,000m 地底竜 ============
// 翼の小さい/無い四足の地底竜。噛みつき/爪/地底ブレス/地中突進。
public class DrakeBoss : WildBossBase
{
    BossHitbox bite, claw, breath, burst;
    BossTelegraphMarker biteMark, clawMark, breathMark;
    int lastAttack = -1;

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Stride;
        footstepShake = true;
        enterSpeed = 9f;
        Vector2 bc = new Vector2(FrontReach + 0.6f, bodyHeight * 0.55f), bs = new Vector2(2.6f, 1.4f);
        bite = NewHitbox("Bite", bc, bs, BossFx.Fang(), new Color(1f, 0.6f, 0.3f, 0.95f));
        biteMark = NewMarker(bc, bs);
        Vector2 cc = new Vector2(FrontReach + 0.3f, bodyHeight * 0.35f), cs = new Vector2(2.8f, 1.5f);
        claw = NewHitbox("Claw", cc, cs, BossFx.Slash(), new Color(1f, 0.55f, 0.25f, 0.95f));
        clawMark = NewMarker(cc, cs);
        Vector2 rc = new Vector2(FrontReach + 1.4f, bodyHeight * 0.5f), rs = new Vector2(3.6f, 1.3f);
        breath = NewHitbox("Breath", rc, rs, BossFx.Orb(), new Color(1f, 0.4f, 0.15f, 0.9f));
        breathMark = NewMarker(rc, rs);
        burst = NewHitbox("Burst", new Vector2(0f, bodyHeight * 0.5f), new Vector2(2.8f, bodyHeight * 1.3f), BossFx.Ring(), new Color(1f, 0.5f, 0.2f, 0.9f));
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            int pick = BossAiUtil.PickNoRepeat(4, ref lastAttack);
            if (pick == 0)
            {
                yield return Approach(1.5f, 3.0f, 5f);
                yield return Telegraph(0.55f, biteMark);
                if (interrupted) { yield return Stagger(0.8f); continue; }
                yield return Strike(bite, 0.24f);
                yield return Recover(0.8f);
            }
            else if (pick == 1)
            {
                yield return Telegraph(0.5f, clawMark);
                if (interrupted) { yield return Stagger(0.8f); continue; }
                StartCoroutine(DashMove(0.3f, 5f));
                yield return Strike(claw, 0.26f);
                yield return Recover(0.8f);
            }
            else if (pick == 2)
            {
                yield return Telegraph(0.9f, breathMark);
                if (interrupted) { yield return Stagger(1.0f); continue; }
                yield return Strike(breath, 0.55f, 0.06f);
                yield return Recover(1.1f);
            }
            else
            {
                yield return UndergroundCharge();
            }
        }
    }

    IEnumerator UndergroundCharge()
    {
        yield return Submerge(0.35f);
        float target = Random.Range(4f, 7f);
        float t = 0f, dustTimer = 0f;
        while (t < 4f && Mathf.Abs(Gap - target) > 0.5f)
        {
            relVelocity = Mathf.Sign(target - Gap) * 8.5f;
            dustTimer -= Time.deltaTime;
            if (dustTimer <= 0f) { ImpactDust(new Vector3(worldX, GroundY + 0.05f, 0f), 2, 0.6f); dustTimer = 0.13f; }
            t += Time.deltaTime;
            yield return null;
        }
        relVelocity = 0f;
        TrackedHazard.Create(worldX, 2.6f, 0.25f, 0.6f, 0f, new Color(1f, 0.5f, 0.2f, 0.5f));
        yield return Wait(0.6f);
        yield return Emerge(0.28f);
        if (IsDead) yield break;
        yield return Strike(burst, 0.24f, 0.1f);
        yield return Recover(0.7f);
    }

    IEnumerator Submerge(float duration)
    {
        invulnerable = true;
        SetHurtboxEnabled(false);
        float t = 0f, startY = yOffset;
        while (t < duration) { t += Time.deltaTime; float f = t / duration; yOffset = Mathf.Lerp(startY, -bodyHeight * 0.9f, f); SetAlpha(1f - f); yield return null; }
        SetAlpha(0f);
    }

    IEnumerator Emerge(float duration)
    {
        float t = 0f;
        while (t < duration) { t += Time.deltaTime; float f = t / duration; yOffset = Mathf.Lerp(-bodyHeight * 0.9f, 0f, f); SetAlpha(f); yield return null; }
        yOffset = 0f; SetAlpha(1f);
        SetHurtboxEnabled(true);
        invulnerable = false;
    }
}

// ============ 90,000m 古代地底悪魔 ============
// 自然洞窟の「床と天井の両方が危険」を最終盤で使う。近接/地面魔法/天井魔法/突進。
public class AncientDemonBoss : WildBossBase
{
    BossHitbox claw, groundBurst, charge;
    BossTelegraphMarker clawMark, chargeMark;
    int lastAttack = -1;

    protected override IEnumerator Enter()
    {
        yOffset = 4f;
        SetPose(Pose.Fly);
        yield return base.Enter();
        yield return SetAltitude(1.4f, 0.6f);
    }

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Wing;
        yOffset = 4f;
        Vector2 cc = new Vector2(FrontReach + 0.5f, bodyHeight * 0.5f), cs = new Vector2(2.8f, 1.8f);
        claw = NewHitbox("Claw", cc, cs, BossFx.Slash(), new Color(0.7f, 0.2f, 1f, 0.95f));
        clawMark = NewMarker(cc, cs);
        groundBurst = NewHitbox("GroundBurst", new Vector2(0f, 0.2f), new Vector2(2.6f, 2.0f), BossFx.Ring(), new Color(0.8f, 0.3f, 1f, 0.9f));
        Vector2 dc = new Vector2(FrontReach, bodyHeight * 0.5f), ds = new Vector2(2.2f, bodyHeight);
        charge = NewHitbox("Charge", dc, ds, BossFx.Slash(), new Color(0.75f, 0.25f, 1f, 0.95f));
        chargeMark = NewMarker(dc, ds);
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            int pick = BossAiUtil.PickNoRepeat(4, ref lastAttack);
            if (pick == 0)
            {
                yield return Approach(1.8f, 2.6f, 5f);
                yield return Telegraph(0.6f, clawMark);
                if (interrupted) { yield return Stagger(0.9f); continue; }
                yield return Strike(claw, 0.26f, 0.06f);
                yield return Recover(0.8f);
            }
            else if (pick == 1)
            {
                yield return GroundMagic();
            }
            else if (pick == 2)
            {
                yield return CeilingMagic();
            }
            else
            {
                yield return ChargeAttack();
            }
        }
    }

    IEnumerator GroundMagic()
    {
        float x = PlayerX + Random.Range(1.5f, 4f);
        TrackedHazard.Create(x, 2.4f, 0.25f, 0.9f, 0f, new Color(0.8f, 0.3f, 1f, 0.5f));
        yield return Telegraph(0.9f);
        if (interrupted) yield break;
        Shake(0.08f, 0.15f);
        yield return Strike(groundBurst, 0.22f, 0.05f);
        yield return Recover(0.9f);
    }

    // 洞窟最深部ならではの天井魔法(落石と同じ仕組みを紫の魔力塊で流用)。
    IEnumerator CeilingMagic()
    {
        float targetX = PlayerX + Random.Range(2f, 5f);
        float? ceilY = CeilingWorldYAt(targetX);
        if (!ceilY.HasValue) { yield return GroundMagic(); yield break; }
        float floorY = TerrainManager.Instance != null ? TerrainManager.Instance.GetFloorTopAt(targetX) : GroundY;
        TrackedHazard.Create(targetX, 2.2f, 0.2f, 0.85f, 0f, new Color(0.8f, 0.3f, 1f, 0.5f));

        windingUp = true; interrupted = false; SetPose(Pose.Windup);
        float t = 0f;
        while (t < 0.85f) { t += Time.deltaTime; windupProgress = t / 0.85f; if (interrupted) break; yield return null; }
        windingUp = false;
        if (interrupted) { yield return Stagger(1.0f); yield break; }
        SetPose(Pose.Idle);
        CeilingFallRock.Create(targetX, ceilY.Value, floorY, 0.3f, 2.0f, 1.3f, new Color(0.7f, 0.25f, 0.95f, 1f));
        yield return Recover(0.8f);
    }

    IEnumerator ChargeAttack()
    {
        yield return Retreat(2.2f, 0.5f);
        yield return Telegraph(0.9f, chargeMark);
        if (interrupted) { yield return Stagger(1.0f); yield break; }
        StartCoroutine(DashMove(0.5f, 12f));
        yield return Strike(charge, 0.4f, 0.1f);
        relVelocity = 0f;
        yield return Recover(1.0f);
    }
}
