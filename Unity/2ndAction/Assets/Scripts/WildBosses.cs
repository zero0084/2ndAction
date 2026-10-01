using System.Collections;
using UnityEngine;

// 荒野街道ボス追加(2026-09-20) - 各ボスの行動(AI)。共通基盤はWildBossBase。
// 全ボスに共通する原則: 攻撃前に必ず「予備動作(赤い予告ゾーン+姿勢変化+点滅)」
// を入れ、ダメージは予告ゾーンと同範囲のBossHitboxだけが与える(本体は無害)。
//
// ボス戦の強化(2026-10-01): 「HPの多い雑魚」にしない。
//  ・段階(Phase): HPの境目(BossBattleTuning)で咆哮→今までに無かった攻撃を解禁(SpecialReady/UltimateReady)。
//  ・必殺技: 名前の表示→大きな予告→複数の波(低い=跳ぶ/高い=地面にいる・下攻撃で降りる/背が高い=二段ジャンプ)→重い一撃(ハート2)。
//    終わった後は必ず大きな隙(Exhausted: 手前へ滑って来て崩れる。崩しが溜まりやすくダメージ増し)。
//  ・スーパーアーマー: 通常の攻撃で予備動作が止まらない(以前のinterruptibleは無効)。崩し(Stagger)が満タンでBREAK。
//  ・走行を使う攻撃: 追い越し→反転突進(オオカミ)、画面を横切る滑空/突進(グリフォン/黒騎士)、前方からの波(大蛇)。
// プレイヤーのジャンプは頂点約2.0(1段)/約4(2段)。高さはこれに合わせてある。

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
// 第1段階: 高速並走 → 噛みつき。
// 第2段階(咆哮): 追い越し突進 - 後ろへ下がる → プレイヤーの頭上を跳び越えて前へ → 反転して構える → 高速突進。
//   かわされると滑って転倒(攻撃チャンス)。複数体でも同時に突進しない(BossBattleの順番取り)。
public class WolfBoss : WildBossBase
{
    BossHitbox bite, charge;
    BossTelegraphMarker biteMark, chargeMark;
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
        interruptible = false;    // スーパーアーマー(崩しで止める)
        supportsInterrupt = true;
        locoStyle = LocoStyle.Gallop;
        enterSpeed = 9f;
        // 登場: 半分の確率で後方から追い抜いて前に出る(高速疾走で接近)。
        fromBehind = Random.value < wolfFromBehindChance;
        if (fromBehind) worldX = PlayerX - 15f - slotIndex * 3f;
        Vector2 c = new Vector2(FrontReach + 0.8f, 0.7f), s = new Vector2(2.2f, 1.3f);
        bite = NewHitbox("Bite", c, s, BossFx.Fang(), new Color(1f, 1f, 1f, 0.95f));
        biteMark = NewMarker(c, s);
        charge = NewHitbox("Charge", new Vector2(FrontReach * 0.3f, 0.6f), new Vector2(Mathf.Max(1.6f, halfWidth * 1.6f), 1.2f), BossFx.Slash(), new Color(1f, 0.92f, 0.8f, 0.9f));
        chargeMark = NewMarker(new Vector2(FrontReach + 5f, 0.55f), new Vector2(10f, 1.1f));
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (SpecialReady(2) && BeginUltimate("疾風の追い越し突進", new Color(1f, 0.85f, 0.4f)))
            {
                MarkSpecial();
                yield return OvertakeCharge();
                continue;
            }
            bool p2 = Phase >= 2;
            yield return Approach(1.4f, p2 ? 3.4f : 2.4f, 8f);
            yield return Telegraph(p2 ? 1.0f : 1.3f, biteMark);
            yield return Strike(bite, 0.28f);
            yield return Recover(p2 ? 0.6f : 0.9f);
        }
    }

    IEnumerator OvertakeCharge()
    {
        freeGap = true;
        // ① 後ろへ下がる
        SetPose(Pose.Move);
        float t = 0f;
        while (Gap > -7f && t < 2.5f) { t += Time.deltaTime; relVelocity = -13f; yield return null; }
        // ② 後ろで並走しながら目を光らせる(予兆)
        relVelocity = 0f;
        t = 0f;
        while (t < 0.45f) { t += Time.deltaTime; SetBodyTint(Color.Lerp(Color.white, new Color(1f, 0.55f, 0.3f), Mathf.PingPong(t * 8f, 1f))); yield return null; }
        SetBodyTint(Color.white);
        // ③ 追い越す(プレイヤーの頭上を跳び越えて前へ)
        t = 0f;
        while (Gap < 12f && t < 2.5f)
        {
            t += Time.deltaTime;
            relVelocity = 18f;
            float g = Gap;
            yOffset = Mathf.Abs(g) < 3.5f ? 3.0f * (1f - (g / 3.5f) * (g / 3.5f)) : 0f;
            yield return null;
        }
        yOffset = 0f;
        relVelocity = 0f;
        // ④ 反転して構える(進路いっぱいの予告)
        facing = -1f; facingLocked = true;
        ImpactDust(new Vector3(worldX, GroundY, 0f), 10, 1.0f);
        yield return Telegraph(0.85f, chargeMark);
        // ⑤ 突進(跳んでかわす)
        bool hit = false;
        charge.onHitPlayer = _ => hit = true;
        StartCoroutine(DashMove(0.8f, 19f));
        yield return Strike(charge, 0.8f, 0.1f);
        charge.onHitPlayer = null;
        freeGap = false;
        if (!hit)
        {
            // かわされた: 勢い余って滑り、転倒する(攻撃チャンス)
            SetPose(Pose.Landing);
            t = 0f;
            while (t < 0.45f) { t += Time.deltaTime; relVelocity = -7f * (1f - t / 0.45f); if (Random.value < 0.4f) ImpactDust(new Vector3(worldX, GroundY, 0f), 2, 0.7f); yield return null; }
            BossBattleHud.Banner("チャンス!", new Color(0.6f, 1f, 0.6f), 0.9f);
            yield return Exhausted(2.4f, 2f, 1.2f);
        }
        else
        {
            EndUltimate();
            yield return Recover(0.7f);
        }
    }
}

// ============ 5,000m ゴブリン・ウルフライダー ============
// 近距離=棍棒、遠距離=魔法弾(反射可能)。
// 第2段階: 騎乗突撃 - 距離を取って魔法弾3発(低/中/高。反射で崩しが大きく溜まる) → 正面から突撃。かわされると落馬しかける(隙)。
public class GoblinRiderBoss : WildBossBase
{
    BossHitbox club, ride;
    BossTelegraphMarker clubMark, rideMark;
    SpriteRenderer orb;
    float lastMagicTime = -99f;
    public float magicCooldown = 3.2f; // 魔法弾の発射間隔(複数体でも弾幕にならないように)

    protected override void OnInit()
    {
        interruptible = false;
        supportsInterrupt = true;
        locoStyle = LocoStyle.Gallop;
        enterSpeed = 8f;
        Vector2 c = new Vector2(FrontReach + 0.5f, 1.2f), s = new Vector2(2.6f, 2.4f);
        club = NewHitbox("Club", c, s, BossFx.Slash(), new Color(1f, 0.85f, 0.5f, 0.95f));
        clubMark = NewMarker(c, s);
        ride = NewHitbox("Ride", new Vector2(FrontReach * 0.4f, 0.7f), new Vector2(2.4f, 1.3f), BossFx.Slash(), new Color(1f, 0.9f, 0.6f, 0.9f));
        rideMark = NewMarker(new Vector2(FrontReach + 5f, 0.6f), new Vector2(10f, 1.2f));

        GameObject o = new GameObject("MagicOrb");
        o.transform.SetParent(transform, false);
        orb = o.AddComponent<SpriteRenderer>();
        orb.sprite = BossFx.Orb();
        orb.color = new Color(0.75f, 0.3f, 1f, 0.95f);
        orb.sortingOrder = RenderOrder.Boss + 1;
        orb.enabled = false;
    }

    protected override void OnInterrupted() { if (orb != null) orb.enabled = false; }

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (SpecialReady(2) && BeginUltimate("騎乗突撃", new Color(0.7f, 1f, 0.4f)))
            {
                MarkSpecial();
                yield return RideCharge();
                continue;
            }
            if (FrontDist > 6f && Time.time - lastMagicTime > magicCooldown && Time.time >= RiderMagicGate.NextTime)
            {
                lastMagicTime = Time.time;
                RiderMagicGate.NextTime = Time.time + RiderMagicGate.Interval;
                yield return CastMagic(1);
                continue;
            }

            yield return Approach(1.7f, 3.2f, 6f);
            yield return Telegraph(0.9f, clubMark);
            yield return Strike(club, 0.25f, 0.06f);
            yield return Recover(0.8f);

            // 距離を空けて魔法を撃つ(遠=魔法の切替を作る)
            if (Random.value < 0.6f) yield return Retreat(3.4f, 1.7f);
        }
    }

    IEnumerator RideCharge()
    {
        freeGap = true;
        yield return MoveToGap(12f, 7f, 2.2f);
        yield return CastMagic(3);
        facing = -1f; facingLocked = true;
        yield return Telegraph(0.75f, rideMark);
        bool hit = false;
        ride.onHitPlayer = _ => hit = true;
        StartCoroutine(DashMove(0.75f, 18f));
        yield return Strike(ride, 0.75f, 0.1f);
        ride.onHitPlayer = null;
        freeGap = false;
        if (!hit) { BossBattleHud.Banner("チャンス!", new Color(0.6f, 1f, 0.6f), 0.9f); yield return Exhausted(2.0f, 2f, 1.2f); }
        else { EndUltimate(); yield return Recover(0.7f); }
    }

    IEnumerator CastMagic(int count)
    {
        StartCoroutine(ChargeOrb(1.0f));
        yield return Telegraph(1.0f);
        orb.enabled = false;
        float[] lift = count >= 3 ? new[] { -0.7f, 0.5f, 1.7f } : new[] { 0f };
        for (int i = 0; i < count; i++)
        {
            Vector3 from = OrbWorldPos();
            Vector3 target = player != null ? player.position + new Vector3(0f, 0.9f + lift[i], 0f) : from + Vector3.left;
            Vector2 dir = ((Vector2)(target - from)).normalized;
            GameObject fb = FireballController.Create(BossFx.Orb(), from, dir * 7f);
            fb.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
            fb.GetComponent<SpriteRenderer>().color = new Color(0.75f, 0.3f, 1f);
            PlayAttackPose(0.3f);
            yield return Wait(count > 1 ? 0.42f : 0.3f);
        }
        yield return Recover(count > 1 ? 0.3f : 0.8f);
    }

    Vector3 OrbWorldPos() => new Vector3(worldX + facing * FrontReach * 0.6f, GroundY + yOffset + bodyHeight * 0.95f, 0f);

    IEnumerator ChargeOrb(float duration)
    {
        orb.enabled = true;
        float t = 0f;
        while (t < duration)
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
// 第2段階: 尾の薙ぎ払い(低い・長い=跳ぶ)。
// 必殺技「大地を裂く大蛇」(第2段階〜): 潜る → 土煙が前方へ走る → 前方で飛び出し、頭(低い)→胴体(高い)→尾(低い)が時間差で進路を横切る。
//   頭を跳んでかわしたら、空中のまま胴体に当たらないよう下攻撃で降りる → 尾をもう一度跳ぶ。
public class SerpentBoss : WildBossBase
{
    BossHitbox bite, dash, erupt, tail;
    BossTelegraphMarker biteMark, dashMark, eruptMark, tailMark;
    int lastPick = -1;

    protected override void OnInit()
    {
        supportsInterrupt = true;
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

        Vector2 tc = new Vector2(r + 3.2f, 0.5f), ts = new Vector2(7.5f, 1.0f);
        tail = NewHitbox("Tail", tc, ts, BossFx.Slash(), new Color(0.7f, 1f, 0.55f, 0.9f));
        tailMark = NewMarker(tc, ts);
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (UltimateReady(2)) { yield return EarthRender(); continue; }
            if (SpecialReady(2)) { MarkSpecial(); yield return TailSweep(); continue; }
            int pick = BossAiUtil.PickNoRepeat(4, ref lastPick);
            if (pick == 0 || pick == 1) yield return BiteAttack();
            else if (pick == 2) yield return ChargeAttack();
            else yield return BurrowAttack();
        }
    }

    IEnumerator BiteAttack()
    {
        yield return Approach(1.6f, 2.6f, 7f);
        yield return Telegraph(Phase >= 2 ? 0.8f : 1.0f, biteMark);
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

    IEnumerator TailSweep()
    {
        yield return Approach(2.0f, 2.6f, 6f);
        yield return Telegraph(0.9f, tailMark);
        yield return Strike(tail, 0.35f, 0.12f);
        yield return Recover(0.8f);
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

    IEnumerator EarthRender()
    {
        if (!BeginUltimate("大地を裂く大蛇", new Color(0.6f, 1f, 0.45f))) { lastUltimateTime = Time.time - 10f; yield break; }
        // 潜る
        SetPose(Pose.Move);
        SetHurtboxEnabled(false);
        invulnerable = true;
        ImpactDust(new Vector3(worldX, GroundY, 0f), 12, 1.1f);
        float t = 0f;
        while (t < 0.6f) { t += Time.deltaTime; float f = t / 0.6f; yOffset = -bodyHeight * 0.7f * f; SetAlpha(1f - f); yield return null; }
        yOffset = 0f;
        freeGap = true;
        // 地中を前方へ走る土煙(予兆)
        t = 0f;
        float puff = 0f;
        while (t < 1.2f)
        {
            t += Time.deltaTime; puff -= Time.deltaTime;
            worldX = PlayerX + 3f + t * 9f;
            if (puff <= 0f) { puff = 0.06f; ImpactDust(new Vector3(worldX, GroundY, 0f), 3, 0.9f); Shake(0.03f, 0.08f); }
            yield return null;
        }
        // 前方で飛び出す
        worldX = PlayerX + 13f;
        facing = -1f; facingLocked = true;
        SetAlpha(1f);
        SetHurtboxEnabled(true);
        SetPose(Pose.Attack);
        attackProgress = 1f;
        ImpactDust(new Vector3(worldX, GroundY, 0f), 16, 1.4f);
        Shake(0.2f, 0.35f);
        // 頭(低い=跳ぶ) → 胴体(高い=地面にいる/下攻撃で降りる) → 尾(低い=もう一度跳ぶ)
        Color scale = new Color(0.55f, 0.85f, 0.4f, 0.9f);
        const float sp = 12f;
        LaneWarn(1.2f, 5f, 0f, 1.25f, 0.7f, LowLaneColor);
        LaneWave(BossFx.Fang(), new Color(1f, 1f, 1f, 0.95f), 0f, 1.25f, 2.4f, 12f, sp, true);
        yield return Wait(0.30f);
        LaneWarn(1.2f, 5f, 2.3f, 4.6f, 0.75f, HighLaneColor);
        // 胴体: 鱗の節が連なって頭上を通る(丸い節を少しずつ上下にずらして、うねる体に見せる)
        for (int seg = 0; seg < 5; seg++)
        {
            float lift = 0.25f * Mathf.Sin(seg * 1.3f);
            LaneWave(BossFx.Orb(), Color.Lerp(scale, new Color(0.35f, 0.6f, 0.25f, 0.95f), seg % 2 == 0 ? 0f : 0.5f), 2.3f + lift, 4.6f + lift, 1.6f, 12f + seg * 1.15f, sp, true);
        }
        yield return Wait(0.75f);
        LaneWarn(1.2f, 5f, 0f, 1.1f, 0.6f, LowLaneColor);
        LaneWave(BossFx.Slash(), scale, 0f, 1.1f, 3.2f, 12f, sp * 1.15f, true);
        yield return Wait(1.1f);
        invulnerable = false;
        attackProgress = 0f;
        // 疲れて地表に横たわる(隙)
        yield return Exhausted(3.2f, 2.2f, 1.25f);
    }
}

// ============ 20,000m 巨人/サイクロプス ============
// 棍棒の振り下ろし / 踏みつけ / 前方薙ぎ払い。広範囲・重い予備動作。
// 第2段階: 二連踏みつけ(地を這う衝撃波が2つ=2回跳ぶ)。
// 必殺技「天崩しの大棍棒」: 下がって棍棒を頭上へ → 長い溜め → 叩きつけ → 第1衝撃波(低い=跳ぶ) → 第2衝撃波(背が高い=二段ジャンプ)
//   → 落石(跳ねながら転がる=着地の場所を選ぶ)。棍棒が地面に刺さって大きな隙。
public class CyclopsBoss : WildBossBase
{
    BossHitbox slam, stomp, sweep, megaSlam;
    BossTelegraphMarker slamMark, stompMark, sweepMark, megaMark;
    int lastPick = -1;

    protected override void OnInit()
    {
        supportsInterrupt = true;
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

        Vector2 m = new Vector2(r + 1.4f, 1.6f), sm = new Vector2(4.2f, 3.2f);
        megaSlam = NewHitbox("MegaSlam", m, sm, BossFx.Block(), new Color(1f, 0.6f, 0.25f, 0.7f));
        megaSlam.damageAmount = 2;
        megaMark = NewMarker(m, sm);
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (UltimateReady(2)) { yield return SkyfallClub(); continue; }
            if (SpecialReady(2)) { MarkSpecial(); yield return DoubleStomp(); continue; }
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

    IEnumerator DoubleStomp()
    {
        yield return MoveToGap(8f, 3f, 2f);
        for (int i = 0; i < 2; i++)
        {
            yield return Telegraph(i == 0 ? 1.0f : 0.55f, stompMark);
            StartCoroutine(Impact(0.02f, 4.6f));
            Shockwave(1.1f, i == 0 ? 9f : 11f, false);
            yield return Strike(stomp, 0.25f, 0.25f, 0.05f);
            EndAttack();
        }
        yield return Recover(1.2f);
    }

    void Shockwave(float height, float speed, bool heavy)
    {
        Vector3 p = FrontWorld(1.2f, height * 0.5f);
        var w = Projectile(BossFx.Ring(), new Color(1f, 0.8f, 0.5f, 0.9f), p, new Vector2(1.5f, height), new Vector2(facing * speed, 0f), 3.5f, heavy);
        w.hugGround = true;
        w.groundOffset = height * 0.5f;
        w.passThrough = true;
    }

    IEnumerator SkyfallClub()
    {
        if (!BeginUltimate("天崩しの大棍棒", new Color(1f, 0.6f, 0.25f))) { lastUltimateTime = Time.time - 10f; yield break; }
        yield return MoveToGap(10f, 4f, 2.2f);
        // 棍棒を頭上へ: 長い溜め(身体が赤熱)
        StartCoroutine(GlowFor(2.0f, new Color(1f, 0.5f, 0.25f)));
        yield return Telegraph(2.0f, megaMark);
        StartCoroutine(Impact(0.0f, 6f));
        yield return Strike(megaSlam, 0.3f, 0.45f, 0.12f);
        EndAttack();
        // 第1衝撃波(低い) → 第2衝撃波(背が高い)
        LaneWarn(1.0f, 4f, 0f, 1.15f, 0.6f, LowLaneColor);
        Shockwave(1.15f, 10f, true);
        yield return Wait(0.6f);
        LaneWarn(1.0f, 4f, 0f, 2.4f, 0.7f, TallLaneColor);
        Shockwave(2.4f, 10f, true);
        yield return Wait(0.55f);
        // 落石: 跳ねながら転がってくる
        for (int i = 0; i < 3; i++)
        {
            Vector3 p = FrontWorld(1.5f + i * 1.2f, 0.45f);
            var rock = Projectile(CaveBossFx.RockChunk(), new Color(0.95f, 0.9f, 0.85f, 1f), p, new Vector2(1.1f, 1.1f), new Vector2(facing * Random.Range(6.5f, 8.5f), 0f), 4f, false);
            rock.hugGround = true; rock.groundOffset = 0.45f;
            rock.bounceHeight = Random.Range(1.4f, 2.4f); rock.bouncePeriod = Random.Range(0.45f, 0.7f);
            rock.spin = Random.Range(-360f, 360f);
            yield return Wait(0.25f);
        }
        yield return Wait(0.6f);
        // 棍棒が地面に刺さって抜けない(大きな隙)
        yield return Exhausted(3.0f, 2f, 1.2f);
    }

    IEnumerator GlowFor(float seconds, Color c)
    {
        float t = 0f;
        while (t < seconds && !IsDead) { t += Time.deltaTime; SetBodyTint(Color.Lerp(Color.white, c, Mathf.PingPong(t * 4f, 1f) * Mathf.Clamp01(t / seconds + 0.3f))); yield return null; }
        SetBodyTint(Color.white);
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
// 第2段階: 糸 → 連続跳びかかり(2回)。
// 必殺技「八脚の死の舞踏」: 糸の連射(当たると減速) → 地面を這って画面を横切る(低い=跳ぶ) → 後ろから頭上を跳び越える(高い=地面にいる)
//   → 最後に目の前へ跳びかかる(着地の瞬間に跳ぶ)。終わると脚がもつれて大きな隙。
public class SpiderBoss : WildBossBase
{
    BossHitbox bite, leg, land, skitter, overleap;
    BossTelegraphMarker biteMark, legMark;
    int lastPick = -1;

    protected override void OnInit()
    {
        supportsInterrupt = true;
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
        skitter = NewHitbox("Skitter", new Vector2(0f, 0.6f), new Vector2(Mathf.Max(2.4f, halfWidth * 1.6f), 1.15f), BossFx.Slash(), new Color(0.85f, 0.85f, 1f, 0.85f));
        skitter.damageAmount = 2;
        overleap = NewHitbox("Overleap", new Vector2(0f, 0.9f), new Vector2(Mathf.Max(2.4f, halfWidth * 1.6f), 1.7f), BossFx.Slash(), new Color(0.8f, 0.7f, 1f, 0.85f));
        overleap.damageAmount = 2;
    }

    IEnumerator DeathDance()
    {
        if (!BeginUltimate("八脚の死の舞踏", new Color(0.85f, 0.75f, 1f))) { lastUltimateTime = Time.time - 10f; yield break; }
        // 糸の連射(低/中/高)。当たっても傷はないが足が鈍る
        yield return MoveToGap(9f, 6f, 1.5f);
        yield return Telegraph(0.7f);
        float[] lift = { -0.6f, 0.6f, 1.8f };
        for (int i = 0; i < 3; i++)
        {
            Vector3 from = new Vector3(worldX + facing * FrontReach, GroundY + bodyHeight * 0.55f, 0f);
            Vector3 target = player.position + new Vector3(0f, 0.9f + lift[i], 0f);
            var web = BossProjectile.Create(BossFx.Orb(), new Color(0.92f, 0.95f, 1f, 0.95f), from, new Vector2(0.9f, 0.9f), ((Vector2)(target - from)).normalized * 8.5f, 3f, RenderOrder.Boss + 1);
            web.damage = false; web.slowFactor = 0.5f; web.slowDuration = 1.4f;
            PlayAttackPose(0.2f);
            yield return Wait(0.3f);
        }
        yield return ExitScreen(true, 20f, 0f);
        const float sp = 25f;
        // 地面を這って横切る(跳ぶ)
        LaneWarn(1.5f, 7f, 0f, 1.25f, 0.95f, LowLaneColor);
        yield return Wait(0.45f);
        yield return Swoop(OffscreenAheadGap(), OffscreenBehindGap(), sp, 0f, skitter);
        // 後ろから頭上を跳び越える(地面にいる)
        LaneWarn(1.5f, 7f, 2.5f, 4.4f, 0.95f, HighLaneColor);
        yield return Wait(0.45f);
        yield return Swoop(OffscreenBehindGap(), OffscreenAheadGap(), sp, 2.5f, overleap);
        yOffset = 0f;
        freeGap = false;
        // 目の前へ跳びかかる(着地の瞬間に跳ぶ)
        yield return ReturnToBattle(7f, 16f);
        yield return JumpAttack(0.5f);
        BossBattleHud.Banner("チャンス!", new Color(0.6f, 1f, 0.6f), 0.9f);
        yield return Exhausted(2.8f, 2f, 1.2f);
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (UltimateReady(2)) { yield return DeathDance(); continue; }
            if (SpecialReady(2))
            {
                MarkSpecial();
                yield return WebShot();
                yield return JumpAttack(0.6f);
                yield return JumpAttack(0.45f);
                yield return Exhausted(1.4f, 1.6f, 1.1f);
                continue;
            }
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
            else if (pick == 2) yield return JumpAttack(0.9f);
            else yield return WebShot();
        }
    }

    IEnumerator JumpAttack(float windup)
    {
        // 着地点(=プレイヤーの現在位置)を地面に予告してから跳ぶ
        const float landGap = 0.3f;
        TrackedHazard.Create(PlayerX + landGap, 3.2f, 1.4f, windup + 0.85f, 0f, Color.clear);
        interrupted = false;
        windingUp = true;
        facingLocked = true;
        relVelocity = 0f;
        SetPose(Pose.Windup);
        float t = 0f;
        while (t < windup)
        {
            t += Time.deltaTime;
            windupProgress = t / windup;
            yield return null;
        }
        windingUp = false;
        windupProgress = 0f;

        yield return Leap(0.8f, 3.0f, landGap - Gap);
        SetPose(Pose.Landing);
        ImpactDust(new Vector3(worldX, GroundY, 0f), 9, 1.0f);
        yield return Strike(land, 0.3f, 0.15f);
        yield return Recover(windup < 0.8f ? 0.4f : 1.0f);
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
// 第2段階: 岩投げ(跳ねる岩)。第3段階(HP30%以下): 身体のコアが発光。
// 必殺技「大地の怒り」(第3段階): 両拳を地面へ → 巨大衝撃 → 岩柱が前方からプレイヤーへ向かって連続で突き上がる(足元の柱の瞬間に跳ぶ)
//   → 最後に巨大な岩を前方へ射出(背が高い=二段ジャンプ)。終了後コアが露出し、数秒間崩し/ダメージが大きく入る。
public class GolemBoss : WildBossBase
{
    BossHitbox slam, punch, pound, dbl;
    BossTelegraphMarker slamMark, punchMark, poundMark, dblMark;
    SpriteRenderer core;
    float coreBoost;
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
        supportsInterrupt = true;
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

        // 第3段階で光るコア(胸のあたり)
        var co = new GameObject("Core");
        co.transform.SetParent(transform, false);
        co.transform.localPosition = new Vector3(0f, bodyHeight * 0.58f, 0f);
        core = co.AddComponent<SpriteRenderer>();
        core.sprite = OneShotSpriteEffect.SoftDotSprite();
        core.color = new Color(1f, 0.55f, 0.15f, 0f);
        core.sortingOrder = RenderOrder.Boss + 2;
    }

    protected override void OnBattleTick(float dt)
    {
        if (core == null) return;
        if (IsDead) { core.enabled = false; return; }
        float want = Phase >= 3 ? 0.55f + 0.25f * Mathf.Sin(Time.time * 6f) : 0f;
        want = Mathf.Max(want, coreBoost);
        Color c = core.color; c.a = Mathf.MoveTowards(c.a, want, dt * 2f); core.color = c;
        float s = bodyHeight * (0.22f + 0.2f * coreBoost + 0.04f * Mathf.Sin(Time.time * 9f));
        core.transform.localScale = new Vector3(s, s, 1f);
    }

    protected override void OnInterrupted() { coreBoost = 0f; }

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (UltimateReady(3)) { yield return EarthWrath(); continue; }
            if (SpecialReady(2)) { MarkSpecial(); yield return BoulderToss(); continue; }
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

    IEnumerator BoulderToss()
    {
        yield return MoveToGap(9f, 3f, 2f);
        yield return Telegraph(1.1f);
        for (int i = 0; i < 2; i++)
        {
            var rock = Projectile(CaveBossFx.RockChunk(), new Color(0.95f, 0.9f, 0.85f, 1f), FrontWorld(0.8f, 0.6f), new Vector2(1.5f, 1.5f), new Vector2(facing * (7f + i * 1.5f), 0f), 4f, false);
            rock.hugGround = true; rock.groundOffset = 0.6f;
            rock.bounceHeight = i == 0 ? 2.2f : 1.3f; rock.bouncePeriod = i == 0 ? 0.75f : 0.5f;
            rock.spin = 240f * -facing;
            PlayAttackPose(0.3f);
            yield return Wait(0.55f);
        }
        yield return Recover(1.0f);
    }

    IEnumerator EarthWrath()
    {
        if (!BeginUltimate("大地の怒り", new Color(1f, 0.6f, 0.2f))) { lastUltimateTime = Time.time - 10f; yield break; }
        yield return MoveToGap(10f, 3f, 2.4f);
        coreBoost = 0.9f;
        yield return Telegraph(1.8f, dblMark);
        StartCoroutine(Impact(6f));
        yield return Strike(dbl, 0.4f, 0.5f, 0.14f);
        EndAttack();
        // 岩柱: 前方からプレイヤーへ向かって順に突き上がる(足元の柱が立つ瞬間に跳ぶ)
        Color rockC = new Color(0.75f, 0.6f, 0.4f, 0.95f);
        for (int i = 0; i < 7; i++)
        {
            float rel = 8f - i * 1.6f;
            Hazard(PlayerX + rel, 1.3f, 1.4f, 0.45f, 0.3f, rockC, true);
            yield return Wait(0.16f);
        }
        yield return Wait(0.75f);
        // 最後に巨大な岩を前方へ(背が高い=二段ジャンプ)
        LaneWarn(1.0f, 4f, 0f, 2.3f, 0.8f, TallLaneColor);
        var boulder = Projectile(CaveBossFx.RockChunk(), new Color(0.95f, 0.9f, 0.85f, 1f), FrontWorld(1.2f, 1.15f), new Vector2(2.9f, 2.9f), new Vector2(facing * 8f, 0f), 4f, true);
        boulder.hugGround = true; boulder.groundOffset = 1.15f; boulder.passThrough = true; boulder.spin = 200f * -facing;
        yield return Wait(1.1f);
        // コア露出: 崩し/ダメージが大きく入る
        coreBoost = 1f;
        BossBattleHud.Banner("コア露出!", new Color(1f, 0.7f, 0.3f), 1.0f);
        yield return Exhausted(4.0f, 2.5f, 1.5f);
        coreBoost = 0f;
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
// 第2段階: 羽根の嵐(上空から羽根を扇状に)。
// 必殺技「嵐の三連滑空」: 画面の外へ消え、低空(跳ぶ)→高空・後ろから(地面にいる)→低空・速い(跳ぶ)の順で画面を横切る。
//   最後は勢い余って墜落(大きな隙)。
public class GriffinBoss : WildBossBase
{
    BossHitbox claw, charge, dive, swoopLow, swoopHigh;
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
        supportsInterrupt = true;
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
        swoopLow = NewHitbox("SwoopLow", new Vector2(0f, 0.65f), new Vector2(Mathf.Max(2.4f, halfWidth * 1.6f), 1.15f), BossFx.Slash(), new Color(1f, 0.95f, 0.7f, 0.8f));
        swoopLow.damageAmount = 2;
        swoopHigh = NewHitbox("SwoopHigh", new Vector2(0f, 0.9f), new Vector2(Mathf.Max(2.4f, halfWidth * 1.6f), 1.7f), BossFx.Slash(), new Color(0.9f, 0.8f, 1f, 0.8f));
        swoopHigh.damageAmount = 2;
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (UltimateReady(2)) { yield return TripleSwoop(); continue; }
            if (SpecialReady(2)) { MarkSpecial(); yield return FeatherStorm(); continue; }

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

    IEnumerator FeatherStorm()
    {
        SetPose(Pose.Fly);
        yield return SetAltitude(3.6f, 0.5f);
        yield return MoveToGap(7f, 5f, 1.5f);
        SetPose(Pose.Fly);
        yield return Telegraph(0.8f);
        for (int wave = 0; wave < 2; wave++)
        {
            Vector3 from = new Vector3(worldX + facing * FrontReach * 0.4f, GroundY + yOffset + bodyHeight * 0.4f, 0f);
            for (int i = 0; i < 5; i++)
            {
                Vector3 target = player.position + new Vector3(-1.6f + i * 0.8f + wave * 0.4f, 0.5f, 0f);
                Vector2 dir = ((Vector2)(target - from)).normalized;
                var f = Projectile(BossFx.Slash(), new Color(1f, 0.95f, 0.75f, 0.95f), from, new Vector2(0.7f, 0.35f), dir * 9f, 3f, false);
                f.transform.right = dir;
            }
            PlayAttackPose(0.3f);
            yield return Wait(0.6f);
        }
        yield return SetAltitude(0f, 0.4f);
        yield return Recover(0.6f);
    }

    IEnumerator TripleSwoop()
    {
        if (!BeginUltimate("嵐の三連滑空", new Color(1f, 0.9f, 0.5f))) { lastUltimateTime = Time.time - 10f; yield break; }
        // 上空前方へ消える
        yield return ExitScreen(true, 18f, 4.5f);
        const float sp = 26f;
        // 1: 低空・前から(跳ぶ)
        LaneWarn(1.5f, 7f, 0f, 1.3f, 1.0f, LowLaneColor);
        yield return Wait(0.45f);
        yield return Swoop(OffscreenAheadGap(), OffscreenBehindGap(), sp, 0f, swoopLow);
        // 2: 高空・後ろから(地面にいる/下攻撃で降りる)
        LaneWarn(1.5f, 7f, 2.5f, 4.4f, 1.0f, HighLaneColor);
        yield return Wait(0.5f);
        yield return Swoop(OffscreenBehindGap(), OffscreenAheadGap(), sp, 2.5f, swoopHigh);
        // 3: 低空・前から速く → 勢い余って墜落
        LaneWarn(1.5f, 7f, 0f, 1.3f, 0.8f, LowLaneColor);
        yield return Wait(0.35f);
        yield return Swoop(OffscreenAheadGap(), 1.2f, sp * 1.15f, 0f, swoopLow);
        yOffset = 0f;
        ImpactDust(new Vector3(worldX, GroundY, 0f), 16, 1.3f);
        Shake(0.18f, 0.3f);
        BossBattleHud.Banner("チャンス!", new Color(0.6f, 1f, 0.6f), 0.9f);
        yield return Exhausted(3.0f, 2.2f, 1.25f);
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
// 第2段階: 毒液(跳ねながら転がる毒の塊)。
// 必殺技「三首の連撃」: 1頭目=地上の噛みつき(赤=跳ぶ) → 2頭目=空中の噛みつき(紫=地面にいる/下攻撃で降りる)
//   → 3頭目=ブレス(橙=二段ジャンプで上を越える)。予告の色で「次に何が来るか」を見て対応する。
public class HydraBoss : WildBossBase
{
    readonly BossHitbox[] heads = new BossHitbox[3];
    readonly BossTelegraphMarker[] marks = new BossTelegraphMarker[3];
    BossHitbox ultLow, ultHigh;

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
        supportsInterrupt = true;
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
        ultLow = NewHitbox("UltLow", new Vector2(r + 4f, 0.6f), new Vector2(8f, 1.2f), BossFx.Fang(), new Color(0.85f, 1f, 0.7f, 0.95f));
        ultLow.damageAmount = 2;
        ultHigh = NewHitbox("UltHigh", new Vector2(r + 4f, 3.4f), new Vector2(8f, 2.1f), BossFx.Fang(), new Color(0.9f, 0.75f, 1f, 0.95f));
        ultHigh.damageAmount = 2;
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (UltimateReady(2)) { yield return TriHeadAssault(); continue; }
            if (SpecialReady(2)) { MarkSpecial(); yield return VenomSpit(); continue; }
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

    IEnumerator VenomSpit()
    {
        yield return MoveToGap(8f, 2.5f, 2f);
        yield return Telegraph(0.9f);
        for (int i = 0; i < 3; i++)
        {
            var blob = Projectile(BossFx.Orb(), new Color(0.55f, 1f, 0.3f, 0.95f), FrontWorld(0.5f, 0.5f), new Vector2(0.9f, 0.9f), new Vector2(facing * (6f + i), 0f), 4f, false);
            blob.hugGround = true; blob.groundOffset = 0.45f; blob.bounceHeight = 1.0f + i * 0.5f; blob.bouncePeriod = 0.5f + i * 0.08f;
            blob.slowFactor = 0.6f; blob.slowDuration = 1.2f;
            PlayAttackPose(0.25f);
            yield return Wait(0.35f);
        }
        yield return Recover(1.0f);
    }

    IEnumerator TriHeadAssault()
    {
        if (!BeginUltimate("三首の連撃", new Color(0.7f, 1f, 0.6f))) { lastUltimateTime = Time.time - 10f; yield break; }
        yield return MoveToGap(FrontReach + 1.5f, 3f, 2f);
        facingLocked = true;
        relVelocity = 0f;
        SetPose(Pose.Windup);
        float baseRel = Gap - FrontReach; // 頭の届く範囲の起点(プレイヤーから見た前方の距離)
        // 1頭目: 地上(赤) / 2頭目: 空中(紫) / 3頭目: ブレス(橙)
        LaneWarn(baseRel - 4f, 8f, 0f, 1.2f, 0.9f, LowLaneColor);
        float t = 0f;
        bool highShown = false;
        while (t < 0.9f)
        {
            t += Time.deltaTime; windupProgress = t / 0.9f;
            if (!highShown && t >= 0.5f) { highShown = true; LaneWarn(baseRel - 4f, 8f, 2.35f, 4.45f, 0.95f, HighLaneColor); }
            yield return null;
        }
        windupProgress = 0f;
        StartCoroutine(Strike(ultLow, 0.3f, 0.15f));
        yield return Wait(0.5f);
        EndAttack(); facingLocked = true;
        StartCoroutine(Strike(ultHigh, 0.3f, 0.12f));
        yield return Wait(0.35f);
        // ブレス(背が高い帯が続く=二段ジャンプで上を越える)
        LaneWarn(baseRel - 2.5f, 5f, 0f, 2.3f, 0.6f, TallLaneColor);
        SetPose(Pose.Windup);
        yield return Wait(0.6f);
        PlayAttackPose(0.8f);
        for (int i = 0; i < 6; i++)
        {
            var fire = Projectile(BossFx.Orb(), new Color(1f, 0.55f, 0.15f, 0.95f), FrontWorld(0.2f, 1.15f), new Vector2(1.4f, 2.3f), new Vector2(facing * 13f, 0f), 2f, true);
            fire.hugGround = true; fire.groundOffset = 1.15f; fire.passThrough = true;
            yield return Wait(0.11f);
        }
        yield return Wait(0.5f);
        yield return Exhausted(3.0f, 2f, 1.25f);
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
// 第2段階: 魔柱の行進(前方からプレイヤーへ向かって柱が順に立つ=足元の瞬間に跳ぶ)+ 魔法弾5連。
// 必殺技「冥界の魔柱陣」: 巨大な闇の魔弾(跳んでかわす or 攻撃で跳ね返す=大きく崩れる) → 前から魔柱の行進(跳ぶ)
//   → 後ろから魔柱の行進(もう一度跳ぶ)。終わると地上へ降りて大きな隙。
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
        supportsInterrupt = true;
        restAltitude = 0.6f;
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
            if (UltimateReady(2)) { yield return InfernalCircle(); continue; }
            if (SpecialReady(2)) { MarkSpecial(); yield return PillarMarch(); continue; }
            int pick = BossAiUtil.PickNoRepeat(3, ref lastPick);
            if (pick == 0)
            {
                yield return Approach(1.9f, 2.6f, 6f);
                yield return Telegraph(0.6f, clawMark);
                yield return Strike(claw, 0.25f, 0.06f);
                yield return Recover(0.6f);
            }
            else if (pick == 1) yield return MagicVolley(Phase >= 2 ? 5 : 3);
            else yield return GroundMagic();
        }
    }

    Vector3 HandPos() => new Vector3(worldX + facing * FrontReach * 0.5f, GroundY + bodyHeight * 0.65f, 0f);

    IEnumerator MagicVolley(int count)
    {
        yield return Telegraph(0.9f);
        for (int i = 0; i < count; i++)
        {
            Vector3 from = HandPos();
            Vector2 dir = AimFrom(from);
            GameObject fb = FireballController.Create(BossFx.Orb(), from, dir * 7f);
            fb.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
            fb.GetComponent<SpriteRenderer>().color = new Color(0.8f, 0.25f, 1f);
            PlayAttackPose(0.25f);
            yield return Wait(count > 3 ? 0.24f : 0.3f);
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

    IEnumerator InfernalCircle()
    {
        if (!BeginUltimate("冥界の魔柱陣", new Color(0.85f, 0.4f, 1f))) { lastUltimateTime = Time.time - 10f; yield break; }
        yield return MoveToGap(9f, 5f, 1.5f);
        // 巨大な闇の魔弾(反射できる)
        yield return Telegraph(1.2f);
        Vector3 from = HandPos();
        Vector2 dir = AimFrom(from);
        GameObject fb = FireballController.Create(BossFx.Orb(), from, dir * 6f);
        var fbc = fb.GetComponent<FireballController>();
        fbc.ScaleUp(2.6f);
        fbc.damageAmount = 2;
        fb.GetComponent<SpriteRenderer>().color = new Color(0.65f, 0.2f, 1f);
        PlayAttackPose(0.4f);
        Shake(0.12f, 0.25f);
        yield return Wait(1.6f);
        // 前から/後ろから魔柱の行進
        Color c = new Color(0.85f, 0.3f, 1f, 0.9f);
        for (int side = 0; side < 2; side++)
        {
            float dirSign = side == 0 ? 1f : -1f;
            for (int i = 0; i < 6; i++)
            {
                float rel = dirSign * (8f - i * 1.8f);
                Hazard(PlayerX + rel, 1.2f, 1.4f, 0.45f, 0.3f, c, true);
                yield return Wait(0.16f);
            }
            yield return Wait(0.7f);
        }
        // 地上へ降りて大きな隙
        yield return SetAltitude(0f, 0.3f);
        yield return Exhausted(3.0f, 2f, 1.25f);
        yield return SetAltitude(restAltitude, 0.4f);
    }

    IEnumerator PillarMarch()
    {
        yield return Telegraph(0.8f);
        PlayAttackPose(0.6f);
        for (int i = 0; i < 6; i++)
        {
            float rel = 8f - i * 1.8f;
            TrackedHazard.Create(PlayerX + rel, 1.2f, 1.4f, 0.45f, 0.3f, new Color(0.85f, 0.3f, 1f, 0.85f));
            yield return Wait(0.17f);
        }
        yield return Wait(0.8f);
        yield return Recover(0.8f);
    }
}

// ============ 90,000m 黒騎士 ============
// プレイヤーに近い戦い方の人型。短い予備動作: 斬撃 / 二連斬り / 踏み込み斬り / 切り上げ / 空中攻撃。
// 第2段階: プレイヤーの操作を真似たエアリアルコンボ(地上斬り→切り上げ→跳躍→空中追撃→叩きつけ)。入力は読まず、各段に予備動作。
// 必殺技「黒刃三連閃」: 画面の外へ下がり、低い突進(跳ぶ)→後ろから高い斬り抜け(地面にいる)→低い突進・速い(跳ぶ)で画面を横切る。
//   最後は膝をついて大きな隙。
public class BlackKnightBoss : WildBossBase
{
    BossHitbox slash, dash, up, air, slamHb, rushLow, rushHigh;
    BossTelegraphMarker slashMark, dashMark, upMark, airMark, slamMark;
    int lastPick = -1;

    protected override void OnInit()
    {
        supportsInterrupt = true;
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
        airMark = NewMarker(new Vector2(r + 0.7f, 0.8f), new Vector2(2.4f, 2.4f));
        slamHb = NewHitbox("Slam", new Vector2(r * 0.5f, 0.5f), new Vector2(5.5f, 1.0f), BossFx.Ring(), new Color(0.6f, 0.7f, 1f, 0.9f));
        slamMark = NewMarker(new Vector2(r * 0.5f, 0.5f), new Vector2(5.5f, 1.0f));
        rushLow = NewHitbox("RushLow", new Vector2(0f, 0.6f), new Vector2(2.6f, 1.15f), BossFx.Slash(), new Color(0.55f, 0.7f, 1f, 0.9f));
        rushLow.damageAmount = 2;
        rushHigh = NewHitbox("RushHigh", new Vector2(0f, 3.3f), new Vector2(2.8f, 2.0f), BossFx.Slash(), new Color(0.75f, 0.55f, 1f, 0.9f));
        rushHigh.damageAmount = 2;
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (UltimateReady(2)) { yield return DarkTripleRush(); continue; }
            if (SpecialReady(2)) { MarkSpecial(); yield return MimicAerialCombo(); continue; }
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

    // プレイヤーと同じ形のエアリアルコンボ(各段に予備動作。入力は読まない)
    IEnumerator MimicAerialCombo()
    {
        yield return Approach(1.9f, 3.8f, 5f);
        yield return Telegraph(0.45f, slashMark);          // 地上斬り
        yield return Strike(slash, 0.2f, 0.05f);
        EndAttack();
        yield return Telegraph(0.4f, upMark);              // 切り上げ
        yield return Strike(up, 0.22f, 0.06f);
        EndAttack();
        SetPose(Pose.Fly);                                  // 跳躍
        yield return SetAltitude(2.6f, 0.3f);
        facingLocked = true;
        yield return Telegraph(0.35f, airMark);            // 空中追撃(高い所=地面にいれば当たらない)
        yield return Strike(air, 0.22f, 0.06f);
        EndAttack();
        facingLocked = true;
        slamMark.Show(facing);                              // 叩きつけ(着地の衝撃は低い=跳ぶ)
        float t = 0f;
        while (t < 0.35f) { t += Time.deltaTime; slamMark.SetProgress(t / 0.35f); yield return null; }
        slamMark.Hide();
        yield return SetAltitude(0f, 0.14f);
        ImpactDust(new Vector3(worldX, GroundY, 0f), 12, 1.1f);
        GroundRing(new Vector3(worldX, GroundY, 0f), 5.5f, new Color(0.7f, 0.8f, 1f, 0.9f));
        yield return Strike(slamHb, 0.25f, 0.18f, 0.05f);
        yield return Exhausted(1.3f, 1.6f, 1.1f);
    }

    IEnumerator DarkTripleRush()
    {
        if (!BeginUltimate("黒刃三連閃", new Color(0.6f, 0.7f, 1f))) { lastUltimateTime = Time.time - 10f; yield break; }
        yield return ExitScreen(true, 20f, 0f);
        const float sp = 28f;
        LaneWarn(1.5f, 7f, 0f, 1.25f, 0.95f, LowLaneColor);
        yield return Wait(0.45f);
        yield return Swoop(OffscreenAheadGap(), OffscreenBehindGap(), sp, 0f, rushLow);
        LaneWarn(1.5f, 7f, 2.3f, 4.3f, 0.95f, HighLaneColor);
        yield return Wait(0.45f);
        yield return Swoop(OffscreenBehindGap(), OffscreenAheadGap(), sp, 0f, rushHigh);
        LaneWarn(1.5f, 7f, 0f, 1.25f, 0.75f, LowLaneColor);
        yield return Wait(0.3f);
        yield return Swoop(OffscreenAheadGap(), 1.4f, sp * 1.15f, 0f, rushLow);
        ImpactDust(new Vector3(worldX, GroundY, 0f), 12, 1.0f);
        BossBattleHud.Banner("チャンス!", new Color(0.6f, 1f, 0.6f), 0.9f);
        yield return Exhausted(3.0f, 2f, 1.25f);
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
