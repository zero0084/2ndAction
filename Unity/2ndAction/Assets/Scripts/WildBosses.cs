using System.Collections;
using UnityEngine;

// 荒野街道ボス追加(2026-09-20) - 各ボスの行動(AI)。共通基盤はWildBossBase。
// 全ボスに共通する原則: 攻撃前に必ず「予備動作(赤い予告ゾーン+姿勢変化+点滅)」
// を入れ、ダメージは予告ゾーンと同範囲のBossHitboxだけが与える(本体は無害)。
// 数値は「まず遊べる」ための暫定値 - 実機確認後に調整する前提。

static class BossAiUtil
{
    // 直前と同じ番号を連続で選ばない抽選。
    public static int PickNoRepeat(int count, ref int last)
    {
        int n = Random.Range(0, count);
        if (count > 1 && n == last) n = (n + 1 + Random.Range(0, count - 1)) % count;
        last = n;
        return n;
    }
}

// ============ 1,000m 巨大オオカミ ============
// 接近 → 停止 → 噛みつき予備動作(1.3秒、移動しない) → 噛みつき → 硬直 → 再接近。
// 対処: ①ジャンプ/後退で範囲外へ ②予備動作中に攻撃を当てて中断(怯み)。
public class WolfBoss : WildBossBase
{
    BossHitbox bite;
    BossTelegraphMarker biteMark;
    bool fromBehind;
    public float wolfFromBehindChance = 0.5f;

    // 後方から: 高速で追い抜き、前へ出てから振り向く。前方から: 高速で走り込む。
    protected override IEnumerator Enter()
    {
        SetPose(Pose.Move);
        if (!fromBehind) { yield return base.Enter(); yield break; }
        float safety = 0f;
        while (Gap < startGap * 0.8f && safety < 6f)
        {
            relVelocity = 10f;
            safety += Time.deltaTime;
            yield return null;
        }
        relVelocity = 0f;
    }

    protected override void OnInit()
    {
        interruptible = true;
        locoStyle = LocoStyle.Gallop;
        enterSpeed = 9f;
        // 登場: 半分の確率で後方から追い抜いて前に出る(高速疾走で接近)。
        fromBehind = Random.value < wolfFromBehindChance;
        if (fromBehind) worldX = PlayerX - 15f - slotIndex * 3f;
        Vector2 c = new Vector2(FrontReach + 0.8f, 0.7f), s = new Vector2(2.2f, 1.3f);
        bite = NewHitbox("Bite", c, s, BossFx.Fang(), new Color(1f, 1f, 1f, 0.95f));
        biteMark = NewMarker(c, s);
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            yield return Approach(1.4f, 2.4f, 8f);
            yield return Telegraph(1.3f, biteMark);
            if (interrupted) { yield return Stagger(1.0f); continue; }
            yield return Strike(bite, 0.28f);
            yield return Recover(0.9f);
        }
    }
}

// ============ 5,000m ゴブリン・ウルフライダー ============
// 近距離=棍棒、遠距離=魔法弾(反射可能)。1体のEnemyとして完結。
public class GoblinRiderBoss : WildBossBase
{
    BossHitbox club;
    BossTelegraphMarker clubMark;
    SpriteRenderer orb;
    float lastMagicTime = -99f;
    public float magicCooldown = 3.2f; // 魔法弾の発射間隔(複数体でも弾幕にならないように)

    protected override void OnInit()
    {
        interruptible = true;
        locoStyle = LocoStyle.Gallop;
        enterSpeed = 8f;
        Vector2 c = new Vector2(FrontReach + 0.5f, 1.2f), s = new Vector2(2.6f, 2.4f);
        club = NewHitbox("Club", c, s, BossFx.Slash(), new Color(1f, 0.85f, 0.5f, 0.95f));
        clubMark = NewMarker(c, s);

        GameObject o = new GameObject("MagicOrb");
        o.transform.SetParent(transform, false);
        orb = o.AddComponent<SpriteRenderer>();
        orb.sprite = BossFx.Orb();
        orb.color = new Color(0.75f, 0.3f, 1f, 0.95f);
        orb.sortingOrder = RenderOrder.Boss + 1;
        orb.enabled = false;
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (FrontDist > 6f && Time.time - lastMagicTime > magicCooldown && Time.time >= RiderMagicGate.NextTime)
            {
                lastMagicTime = Time.time;
                RiderMagicGate.NextTime = Time.time + RiderMagicGate.Interval;
                yield return CastMagic();
                if (interrupted) { yield return Stagger(0.8f); }
                continue;
            }

            yield return Approach(1.7f, 3.2f, 6f);
            yield return Telegraph(0.9f, clubMark);
            if (interrupted) { yield return Stagger(0.9f); continue; }
            yield return Strike(club, 0.25f, 0.06f);
            yield return Recover(0.8f);

            // 距離を空けて魔法を撃つ(遠=魔法の切替を作る)
            if (Random.value < 0.6f) yield return Retreat(3.4f, 1.7f);
        }
    }

    IEnumerator CastMagic()
    {
        StartCoroutine(ChargeOrb(1.0f));
        yield return Telegraph(1.0f);
        orb.enabled = false;
        if (interrupted) yield break;

        Vector3 from = OrbWorldPos();
        Vector2 dir = AimFrom(from);
        GameObject fb = FireballController.Create(BossFx.Orb(), from, dir * 7f);
        fb.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
        fb.GetComponent<SpriteRenderer>().color = new Color(0.75f, 0.3f, 1f);
        PlayAttackPose(0.3f);
        yield return Wait(0.3f);
        yield return Recover(0.8f);
    }

    Vector3 OrbWorldPos() => new Vector3(worldX + facing * FrontReach * 0.6f, GroundY + yOffset + bodyHeight * 0.95f, 0f);

    IEnumerator ChargeOrb(float duration)
    {
        orb.enabled = true;
        float t = 0f;
        while (t < duration && !interrupted)
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / duration);
            orb.transform.position = OrbWorldPos();
            orb.transform.localScale = Vector3.one * Mathf.Lerp(0.15f, 0.95f, f);
            yield return null;
        }
        orb.enabled = false;
    }
}

// ============ 10,000m 巨大蛇 ============
// 噛みつき / 頭を引いてからの突進 / 地面へ潜って別位置から奇襲。
public class SerpentBoss : WildBossBase
{
    BossHitbox bite, dash, erupt;
    BossTelegraphMarker biteMark, dashMark, eruptMark;
    int lastPick = -1;

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Slither;
        enterSpeed = 8f;
        windupMoveFactor = 0.6f;
        float r = FrontReach;
        // 素材の蛇は頭が高い位置にある(とぐろ+鎌首)ので、噛みつきは頭の高さに出す
        Vector2 bc = new Vector2(r + 0.9f, 1.6f), bs = new Vector2(2.2f, 1.8f);
        bite = NewHitbox("Bite", bc, bs, BossFx.Fang(), new Color(1f, 1f, 1f, 0.95f));
        biteMark = NewMarker(bc, bs);

        Vector2 dc = new Vector2(r + 0.4f, 0.8f), ds = new Vector2(2.6f, 1.5f);
        dash = NewHitbox("Dash", dc, ds, BossFx.Slash(), new Color(0.6f, 1f, 0.5f, 0.85f));
        dashMark = NewMarker(new Vector2(r + 4.6f, 0.5f), new Vector2(9f, 0.9f));

        Vector2 ec = new Vector2(0f, bodyHeight * 0.5f), es = new Vector2(2.8f, bodyHeight + 0.6f);
        erupt = NewHitbox("Erupt", ec, es, BossFx.Block(), new Color(0.75f, 0.6f, 0.35f, 0.6f));
        eruptMark = NewMarker(new Vector2(0f, 0.9f), new Vector2(2.8f, 1.8f));
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            int pick = BossAiUtil.PickNoRepeat(4, ref lastPick);
            if (pick == 0 || pick == 1) yield return BiteAttack();
            else if (pick == 2) yield return ChargeAttack();
            else yield return BurrowAttack();
        }
    }

    IEnumerator BiteAttack()
    {
        yield return Approach(1.6f, 2.6f, 7f);
        yield return Telegraph(1.0f, biteMark);
        yield return Strike(bite, 0.3f);
        yield return Recover(0.9f);
    }

    IEnumerator ChargeAttack()
    {
        if (FrontDist > 7f) yield return Approach(6f, 3f, 5f);
        yield return Telegraph(1.3f, dashMark);      // 頭を引いて溜める(可視の予備動作)
        StartCoroutine(DashMove(0.6f, 11f));
        yield return Strike(dash, 0.6f, 0.1f);
        yield return Recover(1.2f);
    }

    IEnumerator BurrowAttack()
    {
        // 潜る(潜行中は被弾判定なし)
        SetPose(Pose.Move);
        SetHurtboxEnabled(false);
        invulnerable = true;
        ImpactDust(new Vector3(worldX, GroundY, 0f), 8, 0.9f);
        float t = 0f;
        while (t < 0.6f)
        {
            t += Time.deltaTime;
            float f = t / 0.6f;
            yOffset = -bodyHeight * 0.6f * f; // 沈みながらフェード
            SetAlpha(1f - f);
            yield return null;
        }
        yOffset = 0f; // 完全に透明になってから位置を戻す(潜行中は見えない)

        // 別位置へ(プレイヤー前方/後方どちらか) - 出現位置を予告してから出る
        float side = Random.value < 0.5f ? 1f : -1f;
        worldX = PlayerX + side * Random.Range(2.5f, 4.0f);
        facingLocked = true;
        facing = PlayerX < worldX ? -1f : 1f;
        eruptMark.Show(facing);
        float w = 0f;
        while (w < 0.95f)
        {
            w += Time.deltaTime;
            eruptMark.SetProgress(w / 0.95f);
            yield return null;
        }
        eruptMark.Hide();

        // 飛び出して噛みつく(飛び出し範囲だけが判定)
        SetHurtboxEnabled(true);
        invulnerable = false;
        SetAlpha(1f);
        SetPose(Pose.Attack);
        ImpactDust(new Vector3(worldX, GroundY, 0f), 10, 1.1f);
        Shake(0.12f, 0.2f);
        StartCoroutine(SetAltitude(0f, 0.2f));
        yield return Strike(erupt, 0.35f);
        yield return Recover(1.0f);
    }
}

// ============ 20,000m 巨人/サイクロプス ============
// 棍棒の振り下ろし / 踏みつけ / 前方薙ぎ払い。広範囲・重い予備動作。
public class CyclopsBoss : WildBossBase
{
    BossHitbox slam, stomp, sweep;
    BossTelegraphMarker slamMark, stompMark, sweepMark;
    int lastPick = -1;

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Stride;
        footstepShake = true;
        enterSpeed = 6.5f;
        windupMoveFactor = 0.25f;
        float r = FrontReach;
        Vector2 a = new Vector2(r + 1.0f, 1.3f), sa = new Vector2(3.2f, 2.6f);
        slam = NewHitbox("Slam", a, sa, BossFx.Slash(), new Color(1f, 0.7f, 0.3f, 0.9f));
        slamMark = NewMarker(a, sa);

        Vector2 b = new Vector2(r + 0.4f, 0.6f), sb = new Vector2(4.6f, 1.2f);
        stomp = NewHitbox("Stomp", b, sb, BossFx.Ring(), new Color(1f, 0.75f, 0.4f, 0.9f));
        stompMark = NewMarker(b, sb);

        Vector2 c = new Vector2(r + 1.8f, 0.75f), sc = new Vector2(5.6f, 1.5f);
        sweep = NewHitbox("Sweep", c, sc, BossFx.Slash(), new Color(1f, 0.85f, 0.6f, 0.9f));
        sweepMark = NewMarker(c, sc);
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            yield return Approach(2.2f, 1.7f, 8f);
            int pick = BossAiUtil.PickNoRepeat(3, ref lastPick);
            if (pick == 0)
            {
                yield return Telegraph(1.4f, slamMark);
                StartCoroutine(Impact(0.05f, 3.2f));
                yield return Strike(slam, 0.3f, 0.22f, 0.06f);
            }
            else if (pick == 1)
            {
                yield return Telegraph(1.1f, stompMark);
                StartCoroutine(Impact(0.05f, 4.6f));
                yield return Strike(stomp, 0.3f, 0.25f, 0.06f);
            }
            else
            {
                yield return Telegraph(1.2f, sweepMark);
                yield return Strike(sweep, 0.35f, 0.1f);
            }
            yield return Recover(1.1f);
        }
    }

    IEnumerator Impact(float delay, float width)
    {
        yield return Wait(delay);
        Vector3 p = FrontWorld(1.0f, 0f);
        ImpactDust(p, 10, 1.1f);
        GroundRing(p, width, new Color(1f, 0.9f, 0.7f, 0.9f));
    }
}

// ============ 30,000m 巨大蜘蛛 ============
// 噛みつき / 前脚 / ジャンプ攻撃(着地点予告) / 糸(ダメージなし・短時間の減速)。
public class SpiderBoss : WildBossBase
{
    BossHitbox bite, leg, land;
    BossTelegraphMarker biteMark, legMark;
    int lastPick = -1;

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Crawl;
        enterSpeed = 10f;
        windupMoveFactor = 0.3f;
        float r = FrontReach;
        Vector2 a = new Vector2(r + 0.8f, 0.7f), sa = new Vector2(1.9f, 1.3f);
        bite = NewHitbox("Bite", a, sa, BossFx.Fang(), new Color(1f, 1f, 1f, 0.95f));
        biteMark = NewMarker(a, sa);

        Vector2 b = new Vector2(r + 1.3f, 0.9f), sb = new Vector2(2.8f, 1.5f);
        leg = NewHitbox("Leg", b, sb, BossFx.Slash(), new Color(0.85f, 0.85f, 1f, 0.9f));
        legMark = NewMarker(b, sb);

        land = NewHitbox("Land", new Vector2(0f, 0.7f), new Vector2(3.2f, 1.4f), BossFx.Ring(), new Color(0.9f, 0.9f, 1f, 0.85f));
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            int pick = BossAiUtil.PickNoRepeat(4, ref lastPick);
            if (pick == 0)
            {
                yield return Approach(1.4f, 3.0f, 6f);
                yield return Telegraph(0.7f, biteMark);
                StartCoroutine(DashMove(0.18f, 6f)); // 牙で短く飛び込む
                yield return Strike(bite, 0.25f);
                yield return Recover(0.7f);
            }
            else if (pick == 1)
            {
                yield return Approach(1.5f, 3.0f, 6f);
                yield return Telegraph(0.65f, legMark);
                yield return Strike(leg, 0.25f);
                yield return Recover(0.7f);
            }
            else if (pick == 2) yield return JumpAttack();
            else yield return WebShot();
        }
    }

    IEnumerator JumpAttack()
    {
        // 着地点(=プレイヤーの現在位置)を地面に予告してから跳ぶ
        const float landGap = 0.3f;
        TrackedHazard.Create(PlayerX + landGap, 3.2f, 1.4f, 1.75f, 0f, Color.clear);
        interrupted = false;
        windingUp = true;
        facingLocked = true;
        relVelocity = 0f;
        SetPose(Pose.Windup);
        float t = 0f;
        while (t < 0.9f)
        {
            t += Time.deltaTime;
            windupProgress = t / 0.9f;
            yield return null;
        }
        windingUp = false;
        windupProgress = 0f;

        yield return Leap(0.8f, 3.0f, landGap - Gap);
        SetPose(Pose.Landing);
        ImpactDust(new Vector3(worldX, GroundY, 0f), 9, 1.0f);
        yield return Strike(land, 0.3f, 0.15f);
        yield return Recover(1.0f);
    }

    IEnumerator WebShot()
    {
        yield return Telegraph(0.8f);
        Vector3 from = new Vector3(worldX + facing * FrontReach, GroundY + bodyHeight * 0.55f, 0f);
        Vector2 dir = AimFrom(from);
        var web = BossProjectile.Create(BossFx.Orb(), new Color(0.92f, 0.95f, 1f, 0.95f), from, new Vector2(0.9f, 0.9f), dir * 8f, 3f, RenderOrder.Boss + 1);
        web.damage = false;
        web.slowFactor = 0.45f;
        web.slowDuration = 1.8f;
        PlayAttackPose(0.3f);
        yield return Wait(0.3f);
        yield return Recover(0.9f);
    }
}

// ============ 40,000m ゴーレム ============
// 拳の叩きつけ / 前方パンチ / 地面を叩いて衝撃波。遅く重い。
public class GolemBoss : WildBossBase
{
    BossHitbox slam, punch, pound, dbl;
    BossTelegraphMarker slamMark, punchMark, poundMark, dblMark;
    int lastPick = -1;

    // 登場: 地面の岩が集まって形成される(砂埃と揺れ + 下からせり上がって実体化)。
    protected override IEnumerator Enter()
    {
        SetPose(Pose.Move);
        float t = 0f;
        const float dur = 1.7f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / dur);
            yOffset = Mathf.Lerp(-bodyHeight * 0.85f, 0f, Mathf.SmoothStep(0f, 1f, f));
            SetAlpha(Mathf.Clamp01(f * 1.6f));
            relVelocity = -1.5f;
            if (Random.value < 0.15f) ImpactDust(new Vector3(worldX + Random.Range(-halfWidth, halfWidth), GroundY, 0f), 4, 0.9f);
            if (Random.value < 0.08f) Shake(0.06f, 0.12f);
            yield return null;
        }
        yOffset = 0f;
        SetAlpha(1f);
        relVelocity = 0f;
        float g = 0f;
        while (Gap > startGap && g < 5f) { g += Time.deltaTime; relVelocity = -enterSpeed; yield return null; }
    }

    protected override void OnInit()
    {
        hitStopOnHit = 0.045f;
        locoStyle = LocoStyle.Stride;
        footstepShake = true;
        enterSpeed = 3f;
        windupMoveFactor = 0.2f;
        yOffset = -bodyHeight * 0.85f;
        SetAlpha(0f);
        float r = FrontReach;
        Vector2 a = new Vector2(r + 1.3f, 1.0f), sa = new Vector2(3.2f, 2.2f);
        slam = NewHitbox("Slam", a, sa, BossFx.Block(), new Color(0.8f, 0.7f, 0.55f, 0.6f));
        slamMark = NewMarker(a, sa);

        Vector2 b = new Vector2(r + 1.3f, 0.8f), sb = new Vector2(3.6f, 1.5f);
        punch = NewHitbox("Punch", b, sb, BossFx.Slash(), new Color(1f, 0.9f, 0.7f, 0.9f));
        punchMark = NewMarker(b, sb);

        Vector2 c = new Vector2(r + 0.9f, 0.8f), sc = new Vector2(2.4f, 1.6f);
        pound = NewHitbox("Pound", c, sc, BossFx.Ring(), new Color(1f, 0.85f, 0.6f, 0.9f));
        poundMark = NewMarker(c, sc);

        // HP半分以下の両拳叩きつけ(長い溜め+衝撃波)
        Vector2 d = new Vector2(r + 1.2f, 1.0f), sd = new Vector2(4.8f, 2.4f);
        dbl = NewHitbox("DoubleSlam", d, sd, BossFx.Block(), new Color(1f, 0.8f, 0.5f, 0.65f));
        dblMark = NewMarker(d, sd);
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            yield return Approach(2.3f, 1.2f, 9f);
            int pick = BossAiUtil.PickNoRepeat(Hp <= maxHp / 2 ? 4 : 3, ref lastPick);
            if (pick == 3)
            {
                yield return Telegraph(2.3f, dblMark);
                StartCoroutine(Impact(4.8f));
                SpawnShockwave();
                yield return Strike(dbl, 0.4f, 0.4f, 0.12f);
            }
            else if (pick == 0)
            {
                yield return Telegraph(1.6f, slamMark);
                StartCoroutine(Impact(3.2f));
                yield return Strike(slam, 0.35f, 0.3f, 0.09f);
            }
            else if (pick == 1)
            {
                yield return Telegraph(1.3f, punchMark);
                yield return Strike(punch, 0.3f, 0.15f, 0.07f);
            }
            else
            {
                yield return Telegraph(1.6f, poundMark);
                StartCoroutine(Impact(2.6f));
                SpawnShockwave();
                yield return Strike(pound, 0.3f, 0.3f, 0.09f);
            }
            yield return Recover(1.4f);
        }
    }

    IEnumerator Impact(float width)
    {
        yield return null;
        Vector3 p = FrontWorld(1.2f, 0f);
        ImpactDust(p, 14, 1.3f);
        GroundRing(p, width, new Color(1f, 0.9f, 0.7f, 0.9f));
    }

    void SpawnShockwave()
    {
        Vector3 p = FrontWorld(1.4f, 0.55f);
        var w = BossProjectile.Create(BossFx.Ring(), new Color(1f, 0.85f, 0.6f, 0.9f), p, new Vector2(1.6f, 1.2f), new Vector2(facing * 6f, 0f), 3.2f, RenderOrder.Boss + 1);
        w.hugGround = true;
        w.groundOffset = 0.55f;
    }
}

// ============ 50,000m グリフォン ============
// 地上(爪) → 低空突進 → 上空へ → 急降下(着地点予告) → 着地後に長い隙。
public class GriffinBoss : WildBossBase
{
    BossHitbox claw, charge, dive;
    BossTelegraphMarker clawMark, chargeMark;

    // 登場: 上空から飛来して低空へ降りる。
    protected override IEnumerator Enter()
    {
        SetPose(Pose.Fly);
        float t = 0f;
        while ((Gap > startGap || yOffset > 0.95f) && t < 5f)
        {
            t += Time.deltaTime;
            relVelocity = Gap > startGap ? -9f : 0f;
            yOffset = Mathf.MoveTowards(yOffset, 0.9f, 7f * Time.deltaTime);
            yield return null;
        }
        relVelocity = 0f;
    }

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Wing;
        yOffset = 7f;
        windupMoveFactor = 0.6f;
        float r = FrontReach;
        Vector2 a = new Vector2(r + 0.9f, 1.0f), sa = new Vector2(2.2f, 1.6f);
        claw = NewHitbox("Claw", a, sa, BossFx.Slash(), new Color(1f, 1f, 0.8f, 0.95f));
        clawMark = NewMarker(a, sa);

        Vector2 c = new Vector2(r + 0.3f, 1.0f), sc = new Vector2(2.4f, 1.8f);
        charge = NewHitbox("Charge", c, sc, BossFx.Slash(), new Color(1f, 0.95f, 0.7f, 0.85f));
        chargeMark = NewMarker(new Vector2(r + 5f, 0.9f), new Vector2(10f, 1.8f));

        dive = NewHitbox("Dive", new Vector2(0f, bodyHeight * 0.4f), new Vector2(2.8f, 2.4f), BossFx.Ring(), new Color(1f, 0.9f, 0.6f, 0.9f));
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            // A: 地上で爪
            yield return Approach(1.7f, 3.2f, 6f);
            yield return Telegraph(0.7f, clawMark);
            yield return Strike(claw, 0.25f);
            yield return Recover(0.6f);

            // B: 低空突進
            yield return Telegraph(0.9f, chargeMark);
            SetPose(Pose.Fly);
            StartCoroutine(SetAltitude(0.9f, 0.25f));
            StartCoroutine(DashMove(0.7f, 11f));
            yield return Strike(charge, 0.7f, 0.1f);
            yield return SetAltitude(0f, 0.3f);
            yield return Recover(0.7f);

            // C: 上空 → 急降下
            yield return DiveSequence();
        }
    }

    IEnumerator DiveSequence()
    {
        facingLocked = false;
        SetPose(Pose.Fly);
        StartCoroutine(SetAltitude(3.8f, 0.7f));
        yield return MoveToGap(4f, 6f, 1.3f);
        SetPose(Pose.Fly);
        yield return Wait(0.5f);

        // 急降下の着地点を地面に予告(プレイヤーの現在位置)
        const float landGap = 0.5f;
        TrackedHazard.Create(PlayerX + landGap, 3.4f, 2.4f, 1.0f, 0f, Color.clear);
        facingLocked = true;
        float t = 0f;
        windingUp = true;
        while (t < 1.0f)
        {
            t += Time.deltaTime;
            windupProgress = t;
            float target = landGap + 0f;
            relVelocity = Mathf.Clamp((target - Gap) * 3f, -8f, 8f);
            yield return null;
        }
        windingUp = false;
        windupProgress = 0f;
        relVelocity = 0f;

        // 降下(判定は降下〜着地の間だけ)
        StartCoroutine(SetAltitude(0f, 0.3f));
        yield return Strike(dive, 0.5f, 0.25f, 0.08f);
        ImpactDust(new Vector3(worldX, GroundY, 0f), 12, 1.2f);
        GroundRing(new Vector3(worldX, GroundY, 0f), 3.4f, new Color(1f, 0.95f, 0.75f, 0.9f));

        // 着地: 長い隙(通常攻撃/コンボを当てるチャンス)
        EndAttack();
        SetPose(Pose.Landing);
        SetBodyTint(new Color(0.75f, 0.85f, 1f));
        yield return Wait(3.0f);
        SetBodyTint(Color.white);
        SetPose(Pose.Idle);
    }
}

// ============ 60,000m ヒュドラ ============
// 3つの頭が時間差で噛みつく。各頭は個別の予告ゾーン。同時ヒットはなく、
// ゾーン間に隙間があるので順番に避けられる。
public class HydraBoss : WildBossBase
{
    readonly BossHitbox[] heads = new BossHitbox[3];
    readonly BossTelegraphMarker[] marks = new BossTelegraphMarker[3];

    // 登場: 前方の地面から巨体が這い出てくる。
    protected override IEnumerator Enter()
    {
        SetPose(Pose.Move);
        float t = 0f;
        while (t < 1.8f)
        {
            t += Time.deltaTime;
            yOffset = Mathf.Lerp(-bodyHeight * 0.85f, 0f, Mathf.SmoothStep(0f, 1f, t / 1.8f));
            SetAlpha(Mathf.Clamp01(t / 0.9f));
            relVelocity = -2.2f;
            if (Random.value < 0.08f) ImpactDust(new Vector3(worldX, GroundY, 0f), 4, 0.9f);
            if (Random.value < 0.05f) Shake(0.05f, 0.12f);
            yield return null;
        }
        SetAlpha(1f);
        yOffset = 0f;
        relVelocity = 0f;
        while (Gap > startGap && t < 6f) { t += Time.deltaTime; relVelocity = -3f; yield return null; }
    }

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Slither;
        SetAlpha(0f);
        yOffset = -bodyHeight * 0.85f;
        windupMoveFactor = 0.6f;
        float r = FrontReach;
        float[] xs = { r + 0.4f, r + 3.0f, r + 5.6f };
        for (int i = 0; i < 3; i++)
        {
            Vector2 c = new Vector2(xs[i], 0.9f), s = new Vector2(1.5f, 1.6f);
            heads[i] = NewHitbox("Head" + i, c, s, BossFx.Fang(), new Color(0.8f, 1f, 0.7f, 0.95f));
            marks[i] = NewMarker(c, s);
        }
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            yield return Approach(3.0f, 1.5f, 9f);
            bool lowHp = Hp <= maxHp / 2;
            float stagger = lowHp ? 0.6f : 0.75f;

            int[] order = { 0, 1, 2 };
            for (int i = 2; i > 0; i--) { int j = Random.Range(0, i + 1); (order[i], order[j]) = (order[j], order[i]); }
            int count = Random.value < 0.4f ? 2 : 3; // 2頭だけの軽い波状も混ぜる
            yield return Volley(order, count, stagger);
            yield return Recover(1.3f);
        }
    }

    IEnumerator Volley(int[] order, int count, float stagger)
    {
        const float windup = 1.0f;
        facingLocked = true;
        relVelocity = 0f;
        SetPose(Pose.Windup);
        for (int k = 0; k < count; k++) StartCoroutine(HeadAttack(order[k], k * stagger, windup));

        float total = windup + stagger * (count - 1) + 0.35f;
        float t = 0f;
        while (t < total)
        {
            t += Time.deltaTime;
            windupProgress = Mathf.Clamp01(t / windup);
            if (t > windup) SetPose(Pose.Attack);
            yield return null;
        }
        windupProgress = 0f;
    }

    IEnumerator HeadAttack(int i, float delay, float windup)
    {
        yield return Wait(delay);
        marks[i].Show(facing);
        float t = 0f;
        while (t < windup)
        {
            t += Time.deltaTime;
            marks[i].SetProgress(t / windup);
            yield return null;
        }
        marks[i].Hide();
        attackProgress = 1f;
        yield return heads[i].Strike(facing, 0.28f);
        attackProgress = 0f;
    }
}

// ============ 70,000m デーモン ============
// 爪の斬撃 / 魔法弾3連(反射可能) / 地面魔法(柱が2か所で時間差に立つ)。
public class DemonBoss : WildBossBase
{
    BossHitbox claw;
    BossTelegraphMarker clawMark;
    int lastPick = -1;

    // 登場: 魔法的な出現(上空に現れて低空浮遊位置まで降りる)。
    protected override IEnumerator Enter()
    {
        SetPose(Pose.Fly);
        float t = 0f;
        while ((Gap > startGap || yOffset > 0.65f) && t < 5f)
        {
            t += Time.deltaTime;
            relVelocity = Gap > startGap ? -8f : 0f;
            yOffset = Mathf.MoveTowards(yOffset, 0.6f, 5f * Time.deltaTime);
            yield return null;
        }
        relVelocity = 0f;
    }

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Wing;
        yOffset = 5f;
        windupMoveFactor = 0.6f;
        Vector2 c = new Vector2(FrontReach + 1.0f, 1.6f), s = new Vector2(2.8f, 2.4f);
        claw = NewHitbox("Claw", c, s, BossFx.Slash(), new Color(0.9f, 0.4f, 1f, 0.95f));
        clawMark = NewMarker(c, s);
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            int pick = BossAiUtil.PickNoRepeat(3, ref lastPick);
            if (pick == 0)
            {
                yield return Approach(1.9f, 2.6f, 6f);
                yield return Telegraph(0.6f, clawMark);
                yield return Strike(claw, 0.25f, 0.06f);
                yield return Recover(0.6f);
            }
            else if (pick == 1) yield return MagicVolley();
            else yield return GroundMagic();
        }
    }

    Vector3 HandPos() => new Vector3(worldX + facing * FrontReach * 0.5f, GroundY + bodyHeight * 0.65f, 0f);

    IEnumerator MagicVolley()
    {
        yield return Telegraph(0.9f);
        for (int i = 0; i < 3; i++)
        {
            Vector3 from = HandPos();
            Vector2 dir = AimFrom(from);
            GameObject fb = FireballController.Create(BossFx.Orb(), from, dir * 7f);
            fb.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
            fb.GetComponent<SpriteRenderer>().color = new Color(0.8f, 0.25f, 1f);
            PlayAttackPose(0.25f);
            yield return Wait(0.3f);
        }
        yield return Recover(0.8f);
    }

    IEnumerator GroundMagic()
    {
        // 予告(赤いゾーン)が出てから柱が立つ。2本は時間差。
        TrackedHazard.Create(PlayerX + 1.0f, 1.6f, 4.5f, 1.0f, 0.35f, new Color(0.85f, 0.3f, 1f, 0.8f));
        TrackedHazard.Create(PlayerX + 4.2f, 1.6f, 4.5f, 1.5f, 0.35f, new Color(0.85f, 0.3f, 1f, 0.8f));
        yield return Telegraph(1.0f);
        PlayAttackPose(0.4f);
        yield return Wait(0.6f);
        yield return Recover(0.9f);
    }
}

// ============ 90,000m 黒騎士 ============
// プレイヤーに近い戦い方の人型。短い予備動作: 斬撃 / 二連斬り / 踏み込み斬り /
// 切り上げ / 空中攻撃。
public class BlackKnightBoss : WildBossBase
{
    BossHitbox slash, dash, up, air;
    BossTelegraphMarker slashMark, dashMark, upMark;
    int lastPick = -1;

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Run;
        enterSpeed = 10f;
        float r = FrontReach;
        Vector2 a = new Vector2(r + 0.9f, 1.0f), sa = new Vector2(2.4f, 1.9f);
        slash = NewHitbox("Slash", a, sa, BossFx.Slash(), new Color(0.6f, 0.8f, 1f, 0.95f));
        slashMark = NewMarker(a, sa);

        Vector2 b = new Vector2(r + 0.9f, 0.9f), sb = new Vector2(2.6f, 1.7f);
        dash = NewHitbox("Dash", b, sb, BossFx.Slash(), new Color(0.6f, 0.8f, 1f, 0.95f));
        dashMark = NewMarker(new Vector2(r + 2.6f, 0.6f), new Vector2(6f, 1.2f));

        Vector2 c = new Vector2(r + 0.6f, 1.9f), sc = new Vector2(2.0f, 3.4f);
        up = NewHitbox("Up", c, sc, BossFx.Slash(), new Color(0.75f, 0.6f, 1f, 0.95f));
        upMark = NewMarker(c, sc);

        air = NewHitbox("Air", new Vector2(r + 0.7f, 0.8f), new Vector2(2.4f, 2.4f), BossFx.Slash(), new Color(0.6f, 0.8f, 1f, 0.95f));
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            int pick = BossAiUtil.PickNoRepeat(5, ref lastPick);
            switch (pick)
            {
                case 0: // 通常斬撃
                    yield return Approach(1.9f, 3.6f, 5f);
                    yield return Telegraph(0.45f, slashMark);
                    yield return Strike(slash, 0.2f, 0.05f);
                    yield return Recover(0.55f);
                    break;
                case 1: // 二連斬り(2撃目はさらに短い予備動作)
                    yield return Approach(1.9f, 3.6f, 5f);
                    yield return Telegraph(0.45f, slashMark);
                    yield return Strike(slash, 0.2f, 0.05f);
                    EndAttack();
                    yield return Telegraph(0.35f, slashMark);
                    yield return Strike(slash, 0.2f, 0.05f);
                    yield return Recover(0.8f);
                    break;
                case 2: // 前方への踏み込み斬り
                    if (FrontDist > 6f) yield return Approach(5f, 3.6f, 4f);
                    yield return Telegraph(0.6f, dashMark);
                    StartCoroutine(DashMove(0.32f, 12f));
                    yield return Strike(dash, 0.32f, 0.08f);
                    yield return Recover(0.7f);
                    break;
                case 3: // 切り上げ(空中のプレイヤーも狙う)
                    yield return Approach(1.6f, 3.6f, 5f);
                    yield return Telegraph(0.5f, upMark);
                    yield return Strike(up, 0.25f, 0.06f);
                    yield return Recover(0.7f);
                    break;
                default: // 空中攻撃(跳び込んで降下斬り)
                    yield return AirAttack();
                    break;
            }
        }
    }

    IEnumerator AirAttack()
    {
        const float landGap = 1.0f;
        TrackedHazard.Create(PlayerX + landGap, 2.6f, 1.2f, 1.0f, 0f, Color.clear);
        yield return Telegraph(0.5f);
        StartCoroutine(Leap(0.75f, 3.2f, landGap - Gap));
        yield return Wait(0.4f);
        yield return Strike(air, 0.35f, 0.1f);
        yield return Recover(0.8f);
    }
}

// 複数のウルフライダーが同時に魔法弾を撃たないための共有ゲート(弾幕防止)。
static class RiderMagicGate
{
    public static float NextTime;
    public static float Interval = 1.4f;
}
