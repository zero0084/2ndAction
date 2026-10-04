using System.Collections;
using UnityEngine;

// 自然洞窟ボス追加(2026-09-22) - 荒野街道ボス(WildBossBase/WildBosses.cs)をそのまま基盤として使い、
// 自然洞窟用の11体(1,000m〜90,000m)を追加する。100,000mの死神(次女)は既存のまま(再戦の対象外)。
//
// 自然洞窟ボス強化(2026-10-04): 荒野街道と同じ「ボス戦の強化」の仕組み(段階/特殊攻撃/必殺技/崩し/スーパーアーマー/
// 必殺技後の大きな隙/ラン再開/保留/再戦)をそのまま使い、別の仕組みは作らない(BossBattleTuning に洞窟の種類を足しただけ)。
// その上に、洞窟だけの層(CaveBossBase)を足す:
//   荒野 = ボスの体を見て避ける / 洞窟 = ボス+地形(床/地中/壁/天井)の予兆を見て避ける。「洞窟そのものが攻撃してくる」
//  ・地形の攻撃は CaveHazard(床から=赤で跳ぶ / 天井から=紫で地面にいる / 背が高い=金で二段ジャンプ / 落石=影+ひび+小石)。
//  ・予兆 → 攻撃 → 避けられる → 反撃のチャンス を必ず守る。床と天井が同時にプレイヤーを覆わないよう CaveBossSafety が調停。
//  ・必殺技は判断を2回以上求め、終わった後は種類ごとに見た目の違う大きな隙(CaveRecovery)。
//  ・再戦(BossRematchTuning)はHP以外も強くなる: 段階の追加(上位以上)/必殺技の追加の一手(強化以上)/第2段階の技を最初から(上位以上)。
// プレイヤーのジャンプは頂点約2.0(1段)/約4(2段)。地面の攻撃は高さ1.2以下、天井の攻撃は地面から2.3より上で止まる。
public enum CaveBossKind { Centipede, Scorpion, Mole, Troll, Worm, CrystalGolem, Bat, ScorpionKing, Basilisk, Drake, AncientDemon }

// ===================================================================== //
// 洞窟ボスの共通層(HazardBossBase の上に、洞窟の初期化の形だけ)
// ===================================================================== //
public abstract class CaveBossBase : HazardBossBase
{
    protected sealed override void OnInit()
    {
        HazardBattleSetup();
        CaveInit();
    }
    protected abstract void CaveInit();
}

// ===================================================================== //
// 1,000m 巨大ムカデ
// 第1段階: 噛みつき / 体当たり(体を丸めて薙ぐ) / 短い突進(低い=跳ぶ)
// 第2段階: 壁→天井へ登り、天井を並走しながら小石の落石 → 目の前へ落下(衝撃波=跳ぶ)
// 必殺技 CAVE CRAWLER: 壁を登る→天井が光る→天井を走って前へ→前方に落下(衝撃波=跳ぶ)→地上を逆走(低い=跳ぶ)
//   →後ろの壁を登る→最後の突進(背が高い=二段ジャンプ)。1回のジャンプでは全部を避けられない。隙: ひっくり返る(大きな隙)
// ===================================================================== //
public class CentipedeBoss : CaveBossBase
{
    BossHitbox bite, sweep, rush, tallRush;
    BossTelegraphMarker biteMark, sweepMark, rushMark;

    protected override void CaveInit()
    {
        locoStyle = LocoStyle.Crawl;
        enterSpeed = 8.5f;
        Vector2 c = new Vector2(FrontReach + 0.6f, bodyHeight * 0.35f), s = new Vector2(2.0f, 1.1f);
        bite = NewHitbox("Bite", c, s, BossFx.Fang(), new Color(1f, 1f, 1f, 0.95f));
        biteMark = NewMarker(c, s);
        Vector2 sc = new Vector2(FrontReach + 0.2f, bodyHeight * 0.3f), ss = new Vector2(2.6f, 1.4f);
        sweep = NewHitbox("Sweep", sc, ss, BossFx.Slash(), new Color(0.8f, 0.9f, 0.6f, 0.9f));
        sweepMark = NewMarker(sc, ss);
        rush = NewHitbox("Rush", new Vector2(FrontReach * 0.3f, 0.55f), new Vector2(Mathf.Max(1.6f, halfWidth * 1.5f), 1.1f), BossFx.Slash(), new Color(0.9f, 0.95f, 0.7f, 0.9f));
        rushMark = NewMarker(new Vector2(FrontReach + 2.5f, 0.55f), new Vector2(5f, 1.1f));
        tallRush = NewHitbox("TallRush", new Vector2(0f, 1.15f), new Vector2(Mathf.Max(1.6f, halfWidth * 1.4f), 2.3f), BossFx.Slash(), new Color(1f, 0.85f, 0.4f, 0.9f));
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (UltimateReady(2)) { yield return CaveCrawler(); continue; }
            if (SpecialReady(SpecialPhase)) { MarkSpecial(); yield return CeilingChase(); continue; }
            int pick = BossAiUtil.PickNoRepeat(3, ref lastPick);
            if (pick == 0) yield return Melee(bite, biteMark, 1.3f, 2.8f, 0.6f, 0.24f, 0.7f);
            else if (pick == 1)
            {
                yield return Approach(1.6f, 2.6f, 5f);
                sweep.damageAmount = HitDmg;
                yield return Telegraph(0.75f, sweepMark);
                GroundRing(FrontWorld(0.5f, 0f), 3f, new Color(0.85f, 0.9f, 0.6f, 0.6f));
                yield return Strike(sweep, 0.3f, 0.05f);
                yield return Recover(0.9f * RecoverMul);
            }
            else
            {
                yield return Rush(rush, rushMark, 6.5f, 0.8f, 0.45f, 11f, false);
                yield return Recover(0.8f * RecoverMul);
            }
        }
    }

    // 第2段階: 天井を並走して落石 → 目の前に落ちる
    IEnumerator CeilingChase()
    {
        yield return MoveToGap(5f, 6f, 1.4f);
        yield return ClimbToCeiling(0.55f);
        freeGap = true;
        int rocks = Enraged || Upgraded ? 3 : 2;
        for (int i = 0; i < rocks && !IsDead; i++)
        {
            yield return MoveToGap(i == 0 ? 3.5f : 2.2f, 7f, 0.5f);
            RockAt(3.6f + i * 0.5f, 0.95f, 0.8f, 6f);
            yield return Wait(0.8f);
        }
        float dropRel = 3.4f;
        yield return MoveToGap(dropRel, 8f, 0.6f);
        TellAt(dropRel, 0.7f, CaveTellStyle.Shadow);
        yield return Wait(0.7f);
        yield return DropFromCeiling(0.22f);
        LaneWarn(1.2f, 2.4f, 0f, 1.0f, 0.3f, LowLaneColor);
        ShockFromFront(1.0f, 8.5f, CaveLook.Dirt);
        freeGap = false;
        yield return Recover(1.0f * RecoverMul);
    }

    IEnumerator CaveCrawler()
    {
        if (!StartUlt("CAVE CRAWLER", new Color(0.9f, 1f, 0.5f))) yield break;
        freeGap = true;
        // ① 前方の壁を登る
        yield return MoveToGap(7f, 7f, 1.2f);
        yield return ClimbToCeiling(0.6f);
        // ② 天井が光る(この後天井を走る合図)
        TellAt(1.5f, 1.0f, CaveTellStyle.Crack, CaveLook.Rock, true);
        TellAt(-2f, 1.0f, CaveTellStyle.Crack, CaveLook.Rock, true);
        Glow(0.8f, new Color(1f, 1f, 0.5f));
        yield return Wait(0.7f);
        // ③ 天井を走る: 頭上を越えて後ろへ → 反転して前へ(頭上は安全。落ちてくる小石は見た目だけ)
        yield return MoveToGap(-5f, 13f, 1.2f);
        facing = 1f; facingLocked = true;
        if (Upgraded) RockAt(4.5f, 0.9f, 0.9f, 6f, true);
        yield return MoveToGap(4.2f, 14f, 1.2f);
        facing = -1f;
        // ④ 前方に落下 → 衝撃波(跳ぶ: 1回目)
        TellAt(Gap, 0.6f, CaveTellStyle.Shadow);
        yield return Wait(0.6f);
        yield return DropFromCeiling(0.2f);
        LaneWarn(1.5f, 3f, 0f, 1.0f, 0.3f, LowLaneColor);
        ShockFromFront(1.0f, 9f, CaveLook.Dirt, true);
        yield return Wait(0.35f);
        // ⑤ 地上を逆走(低い=跳ぶ: 2回目)
        rush.damageAmount = UltimateDamage;
        facing = -1f; facingLocked = true;
        yield return Telegraph(0.75f, rushMark);
        yield return Swoop(Gap, OffscreenBehindGap(), 13f, 0f, rush);
        // ⑥ 後ろの壁を登る
        yield return ClimbToCeiling(0.45f);
        yield return Wait(0.3f);
        // ⑦ 最後の突進(背が高い=二段ジャンプ: 3回目)
        yield return DropFromCeiling(0.2f);
        facing = 1f; facingLocked = true;
        LaneWarn(0f, 9f, 0f, 2.3f, 0.95f, TallLaneColor);
        Glow(0.9f, new Color(1f, 0.8f, 0.3f));
        yield return Wait(0.95f);
        tallRush.damageAmount = UltimateDamage;
        yield return Swoop(Gap, OffscreenAheadGap(), 15f, 0f, tallRush);
        facing = -1f; facingLocked = false;
        // 隙: ひっくり返る(大きな隙)
        yield return CaveRecovery(3.2f, "ひっくり返った! 大チャンス!", new Vector2(1.25f, 0.5f), CaveLook.Dirt, new Vector2(0f, 0.3f), 2.6f, 1.3f);
    }
}

// ===================================================================== //
// 5,000m 巨大サソリ
// 第1段階: ハサミ / 突進(低い=跳ぶ) / 毒針 / 毒弾
// 第2段階: 毒だまり(前方に広がって流れてくる=跳び越える)
// 必殺技 SCORPION DEATH ZONE: ハサミの薙ぎ払い(跳ぶ)→頭上からの毒針(地面にいる)→毒だまり(跳ぶ)→高速突進(跳ぶ)。隙: 尻尾が地面に刺さる
// ===================================================================== //
static class ScorpionBoltGate { public static float NextTime; public static float Interval = 1.5f; }

public class ScorpionBoss : CaveBossBase
{
    BossHitbox claw, sting, rush;
    BossTelegraphMarker clawMark, stingMark, rushMark, sweepMark;
    SpriteRenderer boltGlow;
    float lastBoltTime = -99f;
    public float boltCooldown = 3.6f;

    protected override void CaveInit()
    {
        locoStyle = LocoStyle.Crawl;
        enterSpeed = 8f;
        Vector2 cc = new Vector2(FrontReach + 0.5f, bodyHeight * 0.4f), cs = new Vector2(2.2f, 1.6f);
        claw = NewHitbox("Claw", cc, cs, BossFx.Fang(), new Color(0.9f, 0.7f, 1f, 0.95f));
        clawMark = NewMarker(cc, cs);
        Vector2 sc = new Vector2(FrontReach + 0.3f, bodyHeight * 0.75f), ss = new Vector2(2.6f, 1.0f);
        sting = NewHitbox("Sting", sc, ss, BossFx.Orb(), new Color(0.5f, 1f, 0.4f, 0.95f));
        stingMark = NewMarker(sc, ss);
        rush = NewHitbox("Rush", new Vector2(FrontReach * 0.3f, 0.55f), new Vector2(Mathf.Max(1.6f, halfWidth * 1.5f), 1.1f), BossFx.Slash(), new Color(0.8f, 1f, 0.6f, 0.9f));
        rushMark = NewMarker(new Vector2(FrontReach + 2.8f, 0.55f), new Vector2(5.6f, 1.1f));
        sweepMark = NewMarker(new Vector2(FrontReach + 2f, 0.55f), new Vector2(4.5f, 1.1f));
        boltGlow = MakeGlow(new Color(0.4f, 1f, 0.35f, 0.95f));
    }

    SpriteRenderer MakeGlow(Color c)
    {
        GameObject g = new GameObject("BoltGlow");
        g.transform.SetParent(transform, false);
        var sr = g.AddComponent<SpriteRenderer>();
        sr.sprite = BossFx.Orb(); sr.color = c; sr.sortingOrder = RenderOrder.Boss + 1; sr.enabled = false;
        return sr;
    }

    protected override void OnInterrupted() { base.OnInterrupted(); if (boltGlow != null) boltGlow.enabled = false; }

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (UltimateReady(2)) { yield return DeathZone(); continue; }
            if (SpecialReady(SpecialPhase)) { MarkSpecial(); yield return PoisonRain(Enraged || Upgraded ? 3 : 2); continue; }
            if (FrontDist > 6.5f && Time.time - lastBoltTime > boltCooldown && Time.time >= ScorpionBoltGate.NextTime)
            {
                lastBoltTime = Time.time;
                ScorpionBoltGate.NextTime = Time.time + ScorpionBoltGate.Interval;
                yield return PoisonBolt();
                continue;
            }
            int pick = BossAiUtil.PickNoRepeat(3, ref lastPick);
            if (pick == 0) yield return Melee(claw, clawMark, 1.3f, 2.6f, 0.6f, 0.26f, 0.8f);
            else if (pick == 1) yield return Melee(sting, stingMark, 2.8f, 2.4f, 0.7f, 0.24f, 0.9f);
            else { yield return Rush(rush, rushMark, 7f, 0.8f, 0.5f, 12f, false); yield return Recover(0.8f * RecoverMul); }
        }
    }

    IEnumerator PoisonBolt()
    {
        boltGlow.enabled = true;
        yield return Telegraph(0.9f);
        boltGlow.enabled = false;
        Vector3 from = FrontWorld(0.4f, bodyHeight * 0.7f);
        Projectile(BossFx.Orb(), new Color(0.4f, 1f, 0.35f), from, new Vector2(0.7f, 0.7f), AimFrom(from) * 8f, 3f, false);
        PlayAttackPose(0.25f);
        yield return Wait(0.25f);
        yield return Recover(0.8f * RecoverMul);
    }

    // 尻尾から毒を飛ばし、前方に毒だまり(流れてくる=跳び越える)
    IEnumerator PoisonRain(int count)
    {
        yield return MoveToGap(7.5f, 5f, 1.2f);
        boltGlow.enabled = true;
        yield return Telegraph(0.75f);
        boltGlow.enabled = false;
        for (int i = 0; i < count && !IsDead; i++)
        {
            float rel = 4.2f + i * 3.4f;
            FakeShot(new Color(0.4f, 1f, 0.35f), rel);
            PoolAt(rel, 1.7f, 3.4f, 4f);
            PlayAttackPose(0.2f);
            yield return Wait(0.3f);
        }
        yield return Recover(1.0f * RecoverMul);
    }

    IEnumerator DeathZone()
    {
        if (!StartUlt("SCORPION DEATH ZONE", new Color(0.5f, 1f, 0.4f))) yield break;
        // ① ハサミの薙ぎ払い → 地面すれすれの衝撃(跳ぶ)
        yield return MoveToGap(4f, 6f, 1.2f);
        yield return Telegraph(0.9f, sweepMark);
        GroundRing(FrontWorld(0.8f, 0f), 4f, new Color(0.7f, 1f, 0.5f, 0.7f));
        ShockFromFront(1.1f, 9f, CaveLook.Poison, true, 2.0f);
        PlayAttackPose(0.3f);
        yield return Wait(0.25f);
        // ② 頭上からの毒針(地面にいる)
        boltGlow.enabled = true;
        CeilAt(0f, 2.2f, 0.9f, 0.35f, CaveLook.Bone, true);
        if (Upgraded) CeilAt(2.8f, 2.0f, 1.2f, 0.35f, CaveLook.Bone, true);
        yield return Wait(1.1f);
        boltGlow.enabled = false;
        // ③ 毒だまり(跳ぶ)
        yield return MoveToGap(8f, 7f, 0.8f);
        for (int i = 0; i < (Upgraded ? 3 : 2); i++) { float rel = 4.5f + i * 3.6f; FakeShot(new Color(0.4f, 1f, 0.35f), rel); PoolAt(rel, 1.7f, 3.4f, 4f); }
        yield return Wait(1.6f);
        // ④ 高速突進(跳ぶ)
        yield return Rush(rush, rushMark, 8.5f, 0.85f, 0.6f, 16f, true);
        // 隙: 尻尾が地面に刺さる
        yield return CaveRecovery(3.0f, "尻尾が地面に刺さった! 攻撃のチャンス!", new Vector2(1.08f, 0.82f), CaveLook.Dirt, new Vector2(0.7f, 0.1f));
    }
}

// ===================================================================== //
// 10,000m 巨大モグラ
// 第1段階: 爪 / 短い突進 / 潜って飛び出す(砂煙+盛り上がり+小石が位置を知らせる=跳ぶ)
// 第2段階: 偽の予兆(本物の方が少し強い)
// 必殺技 UNDERGROUND HUNT: 動く予兆3つ→2回の飛び出し(跳ぶ×2)→小さな落石(跳ぶ)→目の前で巨大な飛び出し(二段ジャンプ)。隙: 目を回す
// ===================================================================== //
public class MoleBoss : CaveBossBase
{
    BossHitbox claw, rush;
    BossTelegraphMarker clawMark, rushMark;

    protected override void CaveInit()
    {
        locoStyle = LocoStyle.Crawl;
        enterSpeed = 7f;
        Vector2 cc = new Vector2(FrontReach + 0.4f, bodyHeight * 0.4f), cs = new Vector2(2.2f, 1.6f);
        claw = NewHitbox("Claw", cc, cs, BossFx.Slash(), new Color(0.7f, 0.55f, 0.4f, 0.95f));
        clawMark = NewMarker(cc, cs);
        rush = NewHitbox("Rush", new Vector2(FrontReach * 0.3f, 0.55f), new Vector2(Mathf.Max(1.6f, halfWidth * 1.5f), 1.1f), BossFx.Slash(), new Color(0.8f, 0.65f, 0.45f, 0.9f));
        rushMark = NewMarker(new Vector2(FrontReach + 2.2f, 0.55f), new Vector2(4.4f, 1.1f));
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (UltimateReady(2)) { yield return UndergroundHunt(); continue; }
            if (SpecialReady(SpecialPhase)) { MarkSpecial(); yield return FakeBurrow(Enraged ? 3 : 2); continue; }
            int pick = BossAiUtil.PickNoRepeat(3, ref lastPick);
            if (pick == 0) yield return Melee(claw, clawMark, 1.3f, 2.6f, 0.6f, 0.26f, 0.7f);
            else if (pick == 1) { yield return Rush(rush, rushMark, 5.5f, 0.75f, 0.35f, 10f, false); yield return Recover(0.8f * RecoverMul); }
            else yield return BurrowEmerge();
        }
    }

    IEnumerator BurrowEmerge()
    {
        yield return Burrow(0.4f);
        yield return TunnelTo(0.3f, 7f, 3f);
        yield return EmergeStrike(2.4f, 1.2f, 0.75f, CaveLook.Dirt);
        yield return Recover(0.9f * RecoverMul); // 飛び出した直後=反撃のチャンス
    }

    // 偽の予兆: 盛り上がりが複数。本物(ボスの真上)の方が大きく、小石も多い。偽物からは何も出ない
    IEnumerator FakeBurrow(int tells)
    {
        yield return Burrow(0.4f, false);
        bool realUnder = Random.value < 0.6f;
        float realRel = realUnder ? 0.3f : 3.6f;
        yield return TunnelTo(realRel, 8f, 2.5f);
        tunnelTell = TellAt(0f, 30f, CaveTellStyle.Bulge, CaveLook.Dirt, true, true);
        for (int i = 1; i < tells; i++) TellAt(realUnder ? 3.2f * i : -2.5f * i + 0.6f, 1.2f, CaveTellStyle.Bulge, CaveLook.Dirt, false);
        yield return Wait(0.4f);
        yield return EmergeStrike(2.4f, 1.2f, 0.8f, CaveLook.Dirt);
        yield return Recover(0.9f * RecoverMul);
    }

    IEnumerator UndergroundHunt()
    {
        if (!StartUlt("UNDERGROUND HUNT", new Color(0.85f, 0.65f, 0.4f))) yield break;
        yield return Burrow(0.4f, false);
        // ① 動く予兆3つ(前方からプレイヤーへ近づく。本物はボスの位置)
        freeGap = true;
        worldX = PlayerX + 9f;
        tunnelTell = TellAt(0f, 30f, CaveTellStyle.Bulge, CaveLook.Dirt, true, true);
        TellAt(6f, 1.4f, CaveTellStyle.Bulge, CaveLook.Dirt, false).SetDrift(3f);
        TellAt(12f, 1.4f, CaveTellStyle.Bulge, CaveLook.Dirt, false).SetDrift(6f);
        yield return TunnelTo(0.2f, 7f, 2f);
        // ② 飛び出し1回目(跳ぶ)
        yield return EmergeStrike(2.3f, 1.2f, 0.7f, CaveLook.Dirt, true);
        yield return Burrow(0.25f);
        // ③ 飛び出し2回目(跳ぶ)
        yield return TunnelTo(0.5f, 8f, 0.8f);
        yield return EmergeStrike(2.3f, 1.2f, 0.75f, CaveLook.Dirt, true);
        yield return Burrow(0.25f);
        // ④ 小さな落石(跳ぶ)
        RockAt(5f, 0.8f, 0.9f, 6f);
        if (Upgraded) RockAt(8f, 0.8f, 1.2f, 6f);
        yield return TunnelTo(3f, 8f, 1f);
        yield return Wait(0.8f);
        // ⑤ 目の前で巨大な飛び出し(背が高い=二段ジャンプ)
        yield return TunnelTo(0.8f, 8f, 1f);
        yield return EmergeStrike(3.2f, 2.4f, 1.1f, CaveLook.Dirt, true, 0.4f);
        freeGap = false;
        yield return CaveRecovery(3.2f, "目を回している! 攻撃のチャンス!", new Vector2(1.05f, 0.9f), CaveLook.Bone, new Vector2(0f, 1.0f));
    }
}

// ===================================================================== //
// 20,000m ケイブトロル
// 第1段階: 棍棒叩きつけ(衝撃波=跳ぶ) / 薙ぎ払い / 足踏み(小石→落石)
// 第2段階: 天井を叩く → ひび → 時間差の落石(影/砂/小石/ひびで場所を知らせる。転がってくる=跳ぶ)
// 必殺技 CAVE COLLAPSE: 天井の崩落(地面にいる)と床の亀裂(跳ぶ)が交互に=安全地帯が移動 → 時間差の落石 → 巨大な岩(二段ジャンプ)
//   → 最後の叩きつけ(衝撃波=跳ぶ)。隙: 棍棒が抜けない
// ===================================================================== //
public class TrollBoss : CaveBossBase
{
    BossHitbox slam, sweep, megaSlam;
    BossTelegraphMarker slamMark, sweepMark, megaMark;

    protected override void CaveInit()
    {
        locoStyle = LocoStyle.Stride;
        footstepShake = true;
        windupMoveFactor = 0.25f;
        Vector2 sc = new Vector2(FrontReach + 0.6f, 0.3f), ss = new Vector2(2.6f, 1.8f);
        slam = NewHitbox("Slam", sc, ss, BossFx.Ring(), new Color(1f, 0.75f, 0.3f, 0.95f));
        slamMark = NewMarker(sc, ss);
        Vector2 wc = new Vector2(FrontReach - 0.4f, bodyHeight * 0.45f), ws = new Vector2(3.2f, 1.6f);
        sweep = NewHitbox("Sweep", wc, ws, BossFx.Slash(), new Color(1f, 0.6f, 0.3f, 0.95f));
        sweepMark = NewMarker(wc, ws);
        Vector2 m = new Vector2(FrontReach + 1.2f, 1.4f), sm = new Vector2(3.8f, 2.8f);
        megaSlam = NewHitbox("MegaSlam", m, sm, BossFx.Block(), new Color(1f, 0.6f, 0.25f, 0.7f));
        megaMark = NewMarker(m, sm);
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (UltimateReady(2)) { yield return CaveCollapse(); continue; }
            if (SpecialReady(SpecialPhase)) { MarkSpecial(); yield return CeilingStrike(Enraged || Upgraded ? 4 : 3); continue; }
            yield return Approach(2.2f, 1.8f, 6f);
            int pick = BossAiUtil.PickNoRepeat(3, ref lastPick);
            if (pick == 0)
            {
                slam.damageAmount = HitDmg;
                yield return Telegraph(1.0f, slamMark);
                GroundRing(FrontWorld(0.8f, 0f), 3.5f, new Color(1f, 0.7f, 0.3f, 0.6f));
                ShockFromFront(1.0f, 8.5f, CaveLook.Dirt);
                yield return Strike(slam, 0.24f, 0.1f);
                yield return Recover(0.9f * RecoverMul);
            }
            else if (pick == 1)
            {
                sweep.damageAmount = HitDmg;
                yield return Telegraph(0.8f, sweepMark);
                yield return Strike(sweep, 0.3f, 0.08f);
                yield return Recover(0.8f * RecoverMul);
            }
            else
            {
                // 足踏み: 天井から小石 → 前方に1つ落石
                yield return Telegraph(0.8f);
                Shake(0.1f, 0.2f);
                ImpactDust(new Vector3(worldX, GroundY, 0f), 10, 1.1f);
                RockAt(Random.Range(4f, 6f), 0.95f, 0.9f, 6f);
                yield return Recover(0.8f * RecoverMul);
            }
        }
    }

    IEnumerator CeilingStrike(int rocks)
    {
        yield return MoveToGap(5f, 4f, 1.2f);
        yield return Telegraph(0.9f);
        PlayAttackPose(0.3f);
        Shake(0.14f, 0.25f);
        for (int i = 0; i < rocks; i++) RockAt(3.4f + i * 2.4f, 1.0f, 0.9f + i * 0.35f, 6.5f);
        yield return Wait(0.5f);
        yield return Recover(1.0f * RecoverMul);
    }

    IEnumerator CaveCollapse()
    {
        if (!StartUlt("CAVE COLLAPSE", new Color(1f, 0.6f, 0.3f))) yield break;
        yield return MoveToGap(7f, 5f, 1.4f);
        // ① 棍棒で天井を叩く(長い溜め)
        Glow(1.0f, new Color(1f, 0.5f, 0.25f));
        yield return Telegraph(1.0f);
        PlayAttackPose(0.3f);
        Shake(0.2f, 0.4f);
        // ② 安全地帯が移動: 天井の崩落(紫=地面にいる)→床の亀裂(赤=跳ぶ)→天井の崩落。安全な場所が少しずつずれていく
        CeilAt(0f, 2.8f, 0.9f, 0.45f, CaveLook.Rock, true);
        FloorAt(3.6f, 2.4f, 1.1f, 0.9f, 0.45f, CaveLook.Dirt);
        CeilAt(-3.4f, 2.4f, 0.9f, 0.45f, CaveLook.Rock);
        yield return Wait(1.5f);
        FloorAt(0f, 2.8f, 1.1f, 0.8f, 0.4f, CaveLook.Dirt, true);
        CeilAt(3.6f, 2.4f, 0.8f, 0.4f, CaveLook.Rock);
        yield return Wait(1.3f);
        CeilAt(0f, 2.8f, 0.85f, 0.45f, CaveLook.Rock, true);
        FloorAt(-3.4f, 2.4f, 1.1f, 0.85f, 0.45f, CaveLook.Dirt);
        yield return Wait(1.4f);
        // ③ 時間差の落石 / ④ 巨大な岩(二段ジャンプ)
        RockAt(4.2f, 1.0f, 0.9f, 6.5f);
        if (Upgraded) RockAt(6.6f, 1.0f, 1.15f, 6.5f);
        RockAt(9f, 2.2f, 1.4f, 7f, true);
        yield return Wait(2.6f);
        // ⑤ 最後の叩きつけ(衝撃波=跳ぶ)
        yield return MoveToGap(4f, 5f, 0.8f);
        megaSlam.damageAmount = UltimateDamage;
        yield return Telegraph(1.0f, megaMark);
        ShockFromFront(1.1f, 9f, CaveLook.Dirt, true, 1.6f);
        yield return Strike(megaSlam, 0.3f, 0.2f, 0.06f);
        yield return CaveRecovery(3.4f, "棍棒が抜けない! 攻撃のチャンス!", new Vector2(1.04f, 0.88f), CaveLook.Dirt, new Vector2(0.9f, 0.05f));
    }
}

// ===================================================================== //
// 30,000m 巨大地底ワーム
// 第1段階: 地中の追跡→真下から飛び出す(跳ぶ) / 前方への飛び出し+噛みつき / 噛みつき
// 第2段階: 体が画面を横切る(低い=跳ぶ)
// 必殺技 EARTH BREAKER: 地面の盛り上がり→頭が頭上を通過(地面にいる)→体の節が次々(跳ぶ×3)→尻尾の薙ぎ払い(二段ジャンプ)。
//   体は見た目と当たり判定を分ける(判定は見た目より小さい)。隙: 頭が地面に残る
// ===================================================================== //
public class WormBoss : CaveBossBase
{
    BossHitbox bite;
    BossTelegraphMarker biteMark;

    protected override IEnumerator Enter()
    {
        SetPose(Pose.Move);
        yield return base.Enter();
        yield return Burrow(0.4f);
    }

    protected override void CaveInit()
    {
        locoStyle = LocoStyle.Slither;
        Vector2 bc = new Vector2(FrontReach + 0.4f, bodyHeight * 0.3f), bs = new Vector2(2.4f, 1.3f);
        bite = NewHitbox("Bite", bc, bs, BossFx.Fang(), new Color(0.5f, 0.9f, 0.55f, 0.95f));
        biteMark = NewMarker(bc, bs);
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (!buried) yield return Burrow(0.35f);
            if (UltimateReady(2)) { yield return EarthBreaker(); continue; }
            if (SpecialReady(SpecialPhase)) { MarkSpecial(); yield return CrossBody(); continue; }
            int pick = BossAiUtil.PickNoRepeat(3, ref lastPick);
            if (pick == 0)
            {
                // 地中の追跡 → 真下から(跳ぶ)
                yield return TunnelTo(0.2f, 7f, 3f);
                yield return EmergeStrike(2.6f, 1.2f, 0.75f, CaveLook.Flesh);
                yield return Recover(0.9f * RecoverMul);
            }
            else if (pick == 1)
            {
                // 前方に飛び出して噛みつき
                yield return TunnelTo(Random.Range(3.5f, 5f), 8f, 3f);
                yield return EmergeStrike(2.4f, 1.2f, 0.7f, CaveLook.Flesh);
                yield return Melee(bite, biteMark, 1.2f, 3f, 0.55f, 0.24f, 0.8f);
            }
            else
            {
                yield return TunnelTo(3f, 8f, 3f);
                yield return Surface(0.3f);
                yield return Melee(bite, biteMark, 1.2f, 3f, 0.6f, 0.24f, 0.9f);
            }
        }
    }

    // 体が画面を横切る(低い=跳ぶ)。見た目は大きく、判定は小さい
    IEnumerator CrossBody()
    {
        yield return TunnelTo(OffscreenAheadGap() - 2f, 12f, 2f);
        TellAt(3f, 1.0f, CaveTellStyle.Bulge); TellAt(6f, 1.0f, CaveTellStyle.Bulge); TellAt(9f, 1.0f, CaveTellStyle.Bulge);
        LaneWarn(3f, 7f, 0f, 1.1f, 1.0f, LowLaneColor);
        yield return Wait(1.0f);
        WaveFrom(OffscreenAheadGap(), 4.5f, 0f, 1.25f, 10f, CaveLook.Flesh, false, 0.72f);
        yield return Wait(1.6f);
        yield return Recover(0.4f);
    }

    IEnumerator EarthBreaker()
    {
        if (!StartUlt("EARTH BREAKER", new Color(0.5f, 1f, 0.55f))) yield break;
        float ahead = OffscreenAheadGap();
        // ① 地面の盛り上がり
        TellAt(2f, 1.0f, CaveTellStyle.Bulge); TellAt(5f, 1.0f, CaveTellStyle.Bulge); TellAt(8f, 1.0f, CaveTellStyle.Bulge);
        Shake(0.1f, 0.6f);
        yield return Wait(0.8f);
        // ② 頭が頭上を通過(紫=地面にいる)
        LaneWarn(2f, 8f, 2.4f, 4.4f, 0.85f, HighLaneColor);
        yield return Wait(0.85f);
        WaveFrom(ahead, 2.6f, 2.4f, 4.4f, 11f, CaveLook.Flesh, true, 0.7f);
        yield return Wait(1.2f);
        // ③ 体の節が次々(低い=跳ぶ)。節の間隔は1回ずつ跳べる長さ
        int segs = Upgraded ? 4 : 3;
        LaneWarn(2f, 8f, 0f, 1.1f, 0.7f, LowLaneColor);
        yield return Wait(0.7f);
        for (int i = 0; i < segs; i++) WaveFrom(ahead + i * 9f, 1.6f, 0f, 1.1f, 9f, CaveLook.Flesh, true, 0.7f);
        yield return Wait(segs * 1.0f + 0.8f);
        // ④ 尻尾の薙ぎ払い(背が高い=二段ジャンプ)
        LaneWarn(2f, 8f, 0f, 2.3f, 0.9f, TallLaneColor);
        yield return Wait(0.9f);
        WaveFrom(ahead, 2.0f, 0f, 2.3f, 9f, CaveLook.Flesh, true, 0.72f);
        yield return Wait(1.6f);
        // 頭が地面に残る(目の前に出てくる)
        freeGap = true;
        worldX = PlayerX + 3.5f;
        yield return Surface(0.3f);
        yield return CaveRecovery(3.4f, "頭が地面に残った! 攻撃のチャンス!", new Vector2(1.3f, 0.45f), CaveLook.Flesh, new Vector2(0.8f, 0.2f));
    }
}

// ===================================================================== //
// 40,000m クリスタルゴーレム
// 第1段階: 拳 / 叩きつけ(衝撃波=跳ぶ) / 結晶弾
// 第2段階: コアが光る(この間は少し弱い)→床の結晶(跳ぶ)/天井の結晶(地面にいる)/流れてくる結晶柱(壊すか跳ぶ)
// 必殺技 CRYSTAL PRISON: 位置の予告→床→天井→横から結晶弾(跳ぶ)→巨大な柱(3回で壊れる/二段ジャンプ)。隙: コア露出(崩しやすい)
// ===================================================================== //
public class CrystalGolemBoss : CaveBossBase
{
    BossHitbox punch, slamHb;
    BossTelegraphMarker punchMark, slamMark;
    SpriteRenderer core;

    protected override void CaveInit()
    {
        locoStyle = LocoStyle.Stride;
        footstepShake = true;
        hitStopOnHit = 0.045f;
        windupMoveFactor = 0.25f;
        Vector2 pc = new Vector2(FrontReach + 0.5f, 0.9f), ps = new Vector2(2.6f, 1.8f);
        punch = NewHitbox("Punch", pc, ps, BossFx.Ring(), new Color(0.6f, 0.9f, 1f, 0.95f));
        punchMark = NewMarker(pc, ps);
        Vector2 sc = new Vector2(FrontReach + 0.8f, 0.4f), ss = new Vector2(3f, 1.2f);
        slamHb = NewHitbox("Slam", sc, ss, BossFx.Ring(), new Color(0.6f, 0.9f, 1f, 0.9f));
        slamMark = NewMarker(sc, ss);
        GameObject g = new GameObject("Core");
        g.transform.SetParent(transform, false);
        g.transform.localPosition = new Vector3(0f, bodyHeight * 0.55f, 0f);
        core = g.AddComponent<SpriteRenderer>();
        core.sprite = OneShotSpriteEffect.SoftDotSprite();
        core.color = new Color(0.5f, 0.95f, 1f, 0f);
        core.sortingOrder = RenderOrder.Boss + 1;
        Vector2 b = core.sprite.bounds.size;
        g.transform.localScale = new Vector3(1.6f / Mathf.Max(0.01f, b.x), 1.6f / Mathf.Max(0.01f, b.y), 1f);
    }

    protected override void OnInterrupted() { base.OnInterrupted(); if (core != null) core.color = new Color(0.5f, 0.95f, 1f, 0f); }

    void CoreGlow(float a) { if (core != null) core.color = new Color(0.5f, 0.95f, 1f, a); }

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (UltimateReady(2)) { yield return CrystalPrison(); continue; }
            if (SpecialReady(SpecialPhase)) { MarkSpecial(); yield return CrystalField(); continue; }
            yield return Approach(2.4f, 1.6f, 6f);
            int pick = BossAiUtil.PickNoRepeat(3, ref lastPick);
            if (pick == 0)
            {
                punch.damageAmount = HitDmg;
                yield return Telegraph(1.0f, punchMark);
                yield return Strike(punch, 0.24f, 0.1f);
                yield return Recover(1.0f * RecoverMul);
            }
            else if (pick == 1)
            {
                slamHb.damageAmount = HitDmg;
                yield return Telegraph(1.1f, slamMark);
                GroundRing(FrontWorld(0.8f, 0f), 3.5f, new Color(0.6f, 0.9f, 1f, 0.6f));
                ShockFromFront(1.0f, 8.5f, CaveLook.Crystal);
                yield return Strike(slamHb, 0.24f, 0.1f);
                yield return Recover(1.0f * RecoverMul);
            }
            else yield return CrystalBolt();
        }
    }

    IEnumerator CrystalBolt()
    {
        yield return Retreat(2f, 0.6f);
        CoreGlow(0.5f);
        yield return Telegraph(1.0f);
        CoreGlow(0f);
        Vector3 from = FrontWorld(0.4f, bodyHeight * 0.7f);
        Projectile(CaveBossFx.CrystalShard(), new Color(0.55f, 0.85f, 1f), from, new Vector2(1.1f, 1.1f), AimFrom(from) * 5f, 3.5f, false);
        PlayAttackPose(0.3f);
        yield return Wait(0.3f);
        yield return Recover(1.0f * RecoverMul);
    }

    // コアが光る(この間はダメージ1.2倍)→床/天井の結晶→結晶柱
    IEnumerator CrystalField()
    {
        vulnerableScale = 1.2f;
        CoreGlow(0.9f);
        yield return Telegraph(0.6f);
        FloorAt(0f, 1.8f, 1.2f, 0.85f, 0.35f, CaveLook.Crystal);
        yield return Wait(0.6f);
        CeilAt(0f, 2.0f, 0.9f, 0.4f, CaveLook.Crystal);
        PillarAt(6.5f, 1.1f, 0.6f, 2, 4f);
        yield return Wait(1.4f);
        CoreGlow(0f);
        vulnerableScale = 1f;
        yield return Recover(0.9f * RecoverMul);
    }

    IEnumerator CrystalPrison()
    {
        if (!StartUlt("CRYSTAL PRISON", new Color(0.55f, 0.9f, 1f))) yield break;
        yield return MoveToGap(8f, 4f, 1.4f);
        CoreGlow(1f);
        // ① 位置の予告(床の魔法陣と天井のひび)
        TellAt(0f, 1.0f, CaveTellStyle.Circle, CaveLook.Crystal); TellAt(3.4f, 1.0f, CaveTellStyle.Circle, CaveLook.Crystal); TellAt(-3.4f, 1.0f, CaveTellStyle.Circle, CaveLook.Crystal);
        yield return Telegraph(0.8f);
        // ② 床の結晶(跳ぶ)
        FloorAt(0f, 2.2f, 1.2f, 0.6f, 0.4f, CaveLook.Crystal, true);
        FloorAt(3.4f, 2.0f, 1.2f, 0.6f, 0.4f, CaveLook.Crystal);
        FloorAt(-3.4f, 2.0f, 1.2f, 0.6f, 0.4f, CaveLook.Crystal);
        yield return Wait(1.2f);
        // ③ 天井の結晶(地面にいる)。安全な場所が動く(左右の天井も落ちる)
        CeilAt(0f, 2.4f, 0.9f, 0.45f, CaveLook.Crystal, true);
        CeilAt(3.6f, 2.2f, 1.2f, 0.45f, CaveLook.Crystal);
        CeilAt(-3.6f, 2.2f, 0.7f, 0.45f, CaveLook.Crystal);
        yield return Wait(1.5f);
        // ④ 横から結晶弾(低い=跳ぶ)
        for (int i = 0; i < (Upgraded ? 3 : 2); i++)
        {
            LaneWarn(2f, 6f, 0.2f, 1.1f, 0.6f, LowLaneColor);
            yield return Wait(0.6f);
            PlayAttackPose(0.25f);
            WaveFrom(Gap - FrontReach, 1.0f, 0.2f, 1.1f, 10f, CaveLook.Crystal, true, 0.75f);
            yield return Wait(0.6f);
        }
        // ⑤ 巨大な柱(3回で壊れる / 二段ジャンプ)
        PillarAt(7f, 2.4f, 0.9f, 3, 4.5f, CaveLook.Crystal, true);
        yield return Wait(2.6f);
        CoreGlow(0f);
        // 隙: コア露出(崩しやすい)
        CoreGlow(1f);
        yield return CaveRecovery(3.6f, "コア露出! 崩しやすい!", new Vector2(1.02f, 0.92f), CaveLook.Crystal, new Vector2(0f, 0.55f), 3.0f, 1.4f);
        CoreGlow(0f);
    }
}

// ===================================================================== //
// 50,000m 巨大コウモリ
// 第1段階: 低空の急降下(着地点の影→地面すれすれの衝撃=跳ぶ) / 爪 / 超音波
// 第2段階: 天井にぶら下がって追跡 → 真下への超音波(地面にいる)
// 必殺技 ECHO HUNT: 少し暗くなる(真っ暗にはしない)→プレイヤーを狙う音波の輪(低い=跳ぶ/高い=地面にいる)→最後の急降下(跳ぶ)。隙: 地面に激突
// ===================================================================== //
public class BatBoss : CaveBossBase
{
    BossHitbox claw, skid;
    SpriteRenderer sonicGlow;

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

    protected override void CaveInit()
    {
        locoStyle = LocoStyle.Wing;
        yOffset = 8f;
        restAltitude = 1.0f;
        Vector2 cc = new Vector2(FrontReach + 0.3f, bodyHeight * 0.4f), cs = new Vector2(2.4f, 1.6f);
        claw = NewHitbox("Claw", cc, cs, BossFx.Fang(), new Color(0.7f, 0.6f, 0.9f, 0.95f));
        skid = NewHitbox("Skid", new Vector2(0f, 0.55f), new Vector2(Mathf.Max(1.6f, halfWidth * 1.4f), 1.1f), BossFx.Slash(), new Color(0.75f, 0.65f, 0.95f, 0.9f));
        GameObject g = new GameObject("SonicGlow");
        g.transform.SetParent(transform, false);
        sonicGlow = g.AddComponent<SpriteRenderer>();
        sonicGlow.sprite = BossFx.Ring();
        sonicGlow.color = new Color(0.8f, 0.7f, 1f, 0.9f);
        sonicGlow.sortingOrder = RenderOrder.Boss + 1;
        sonicGlow.enabled = false;
    }

    protected override void OnInterrupted() { base.OnInterrupted(); if (sonicGlow != null) sonicGlow.enabled = false; restAltitude = 1.0f; }

    float MaxSafeAltitude() => Mathf.Max(1.5f, CeilY(worldX) - GroundY - bodyHeight * 0.6f);

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (UltimateReady(2)) { yield return EchoHunt(); continue; }
            if (SpecialReady(SpecialPhase)) { MarkSpecial(); yield return CeilingHang(); continue; }
            int pick = BossAiUtil.PickNoRepeat(3, ref lastPick);
            if (pick == 0) yield return ClawPass();
            else if (pick == 1) yield return DiveAttack(false);
            else yield return SonicAttack();
        }
    }

    IEnumerator ClawPass()
    {
        yield return SetAltitude(Mathf.Min(1.1f, MaxSafeAltitude()), 0.4f);
        claw.damageAmount = HitDmg;
        yield return Approach(1.6f, 3.0f, 5f);
        yield return Telegraph(0.6f);
        yield return Strike(claw, 0.24f);
        yield return Recover(0.8f * RecoverMul);
    }

    // 急降下: 前方の着地点(影)に降り、地面すれすれを滑ってくる(跳ぶ)
    IEnumerator DiveAttack(bool heavy)
    {
        float safeAlt = MaxSafeAltitude();
        yield return SetAltitude(Mathf.Min(safeAlt, 4.2f), 0.5f);
        yield return MoveToGap(5f, 4f, 2f);
        TellAt(Gap, 0.75f, CaveTellStyle.Shadow);
        LaneWarn(Gap * 0.5f, Gap + 1f, 0f, 1.1f, 0.75f, LowLaneColor);
        yield return Wait(0.75f);
        yield return SetAltitude(0.05f, 0.25f);
        Shake(0.1f, 0.15f);
        ImpactDust(new Vector3(worldX, GroundY, 0f), 8, 1f);
        skid.damageAmount = Dmg(heavy);
        facing = -1f; facingLocked = true;
        yield return Swoop(Gap, OffscreenBehindGap(), 11f, 0.05f, skid);
        // 画面の外から戻る
        yield return SetAltitude(Mathf.Min(3f, safeAlt), 0.1f);
        worldX = PlayerX + OffscreenAheadGap();
        facing = -1f; facingLocked = false; freeGap = false;
        yield return MoveToGap(startGap, 9f, 1.5f);
        yield return Recover(0.6f * RecoverMul);
    }

    IEnumerator SonicAttack()
    {
        sonicGlow.enabled = true;
        yield return Telegraph(0.9f);
        sonicGlow.enabled = false;
        Vector3 from = FrontWorld(0.6f, bodyHeight * 0.5f);
        Projectile(BossFx.Ring(), new Color(0.8f, 0.7f, 1f), from, new Vector2(1.6f, 1.6f), AimFrom(from) * 9f, 2.5f, false);
        PlayAttackPose(0.3f);
        yield return Wait(0.3f);
        yield return Recover(0.9f * RecoverMul);
    }

    // 天井にぶら下がり、真上から追跡 → 真下への超音波(紫=地面にいる)
    IEnumerator CeilingHang()
    {
        yield return ClimbToCeiling(0.5f);
        freeGap = true;
        float t = 0f;
        var shadow = TellAt(0f, 1.6f, CaveTellStyle.Shadow, CaveLook.Dirt, true, true);
        while (t < 1.2f && !IsDead) { t += Time.deltaTime; relVelocity = Mathf.Clamp((0.6f - Gap) * 3f, -8f, 8f); yield return null; }
        relVelocity = 0f;
        sonicGlow.enabled = true;
        CeilAt(Gap, 2.4f, 0.85f, 0.4f, CaveLook.Magic);
        yield return Wait(1.3f);
        sonicGlow.enabled = false;
        if (shadow != null) shadow.EndEarly();
        yield return MoveToGap(4f, 8f, 0.8f);
        yield return DropFromCeiling(0.35f);
        freeGap = false;
        yield return Recover(0.8f * RecoverMul);
    }

    IEnumerator EchoHunt()
    {
        if (!StartUlt("ECHO HUNT", new Color(0.8f, 0.7f, 1f))) yield break;
        CaveDarkness.Set(0.35f);
        yield return ExitScreen(true, 12f, 2.5f);
        // 音波の輪: 低い(跳ぶ)/高い(地面にいる)を交互に。必ず色の予告が先
        bool[] low = Upgraded ? new[] { true, false, true, false } : new[] { true, false, true };
        foreach (bool l in low)
        {
            float y0 = l ? 0f : 2.3f, y1 = l ? 1.1f : 3.6f;
            LaneWarn(3f, 7f, y0, y1, 0.75f, l ? LowLaneColor : HighLaneColor);
            yield return Wait(0.75f);
            WaveFrom(OffscreenAheadGap(), 1.4f, y0, y1, 9f, CaveLook.Magic, true, 0.7f);
            yield return Wait(0.75f);
        }
        // 最後の急降下(跳ぶ)
        freeGap = true;
        facing = -1f; facingLocked = false;
        yield return MoveToGap(6f, 12f, 1.5f);
        yield return DiveAttack(true);
        CaveDarkness.Set(0f);
        // 隙: 地面に激突
        restAltitude = 0f;
        yield return SetAltitude(0f, 0.2f);
        Shake(0.15f, 0.25f);
        yield return CaveRecovery(3.0f, "地面に激突! 攻撃のチャンス!", new Vector2(1.2f, 0.6f), CaveLook.Dirt, new Vector2(0f, 0.2f));
        restAltitude = 1.0f;
        yield return SetAltitude(1.0f, 0.4f);
    }
}

// ===================================================================== //
// 60,000m スコーピオンキング
// 巨大サソリの上位: ハサミ/毒針/毒弾 + 地面の針の列(手前へ順に=1回のジャンプ) + 毒弾の複数発射 + 尻尾の高速連続(上/下/上)
// 必殺技 KING'S EXECUTION: 薙ぎ払い(跳ぶ)→針の野と頭上の毒針(安全な道が必ず残る)→毒の雨(跳ぶ)→処刑の一突き(二段ジャンプ)。
//   隙: 尻尾を叩きつけて硬直
// ===================================================================== //
static class ScorpionKingBoltGate { public static float NextTime; public static float Interval = 1.6f; }

public class ScorpionKingBoss : CaveBossBase
{
    BossHitbox claw, sting;
    BossTelegraphMarker clawMark, stingMark, sweepMark;
    SpriteRenderer boltGlow;
    float lastBoltTime = -99f;
    public float boltCooldown = 4.2f;

    protected override void CaveInit()
    {
        locoStyle = LocoStyle.Crawl;
        footstepShake = true;
        Vector2 cc = new Vector2(FrontReach + 0.5f, bodyHeight * 0.4f), cs = new Vector2(2.6f, 2.0f);
        claw = NewHitbox("Claw", cc, cs, BossFx.Fang(), new Color(0.9f, 0.5f, 0.6f, 0.95f));
        clawMark = NewMarker(cc, cs);
        Vector2 sc = new Vector2(FrontReach + 0.3f, bodyHeight * 0.85f), ss = new Vector2(3.0f, 1.2f);
        sting = NewHitbox("Sting", sc, ss, BossFx.Orb(), new Color(0.5f, 1f, 0.4f, 0.95f));
        stingMark = NewMarker(sc, ss);
        sweepMark = NewMarker(new Vector2(FrontReach + 2f, 0.55f), new Vector2(4.5f, 1.1f));
        GameObject g = new GameObject("BoltGlow");
        g.transform.SetParent(transform, false);
        boltGlow = g.AddComponent<SpriteRenderer>();
        boltGlow.sprite = BossFx.Orb();
        boltGlow.color = new Color(0.4f, 1f, 0.35f, 0.95f);
        boltGlow.sortingOrder = RenderOrder.Boss + 1;
        boltGlow.enabled = false;
    }

    protected override void OnInterrupted() { base.OnInterrupted(); if (boltGlow != null) boltGlow.enabled = false; }

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (UltimateReady(2)) { yield return KingsExecution(); continue; }
            if (SpecialReady(SpecialPhase))
            {
                MarkSpecial();
                if (Random.value < 0.5f) yield return TailCombo(); else yield return MultiPoison();
                continue;
            }
            int pick = BossAiUtil.PickNoRepeat(4, ref lastPick);
            if (pick == 0) yield return Melee(claw, clawMark, 1.6f, 2.6f, 0.55f, 0.26f, 0.7f);
            else if (pick == 1) yield return Melee(sting, stingMark, 3.0f, 2.4f, 0.6f, 0.24f, 0.8f);
            else if (pick == 2 && Time.time - lastBoltTime > boltCooldown && Time.time >= ScorpionKingBoltGate.NextTime)
            {
                lastBoltTime = Time.time;
                ScorpionKingBoltGate.NextTime = Time.time + ScorpionKingBoltGate.Interval;
                boltGlow.enabled = true;
                yield return Telegraph(0.8f);
                boltGlow.enabled = false;
                Vector3 from = FrontWorld(0.4f, bodyHeight * 0.7f);
                Projectile(BossFx.Orb(), new Color(0.4f, 1f, 0.35f), from, new Vector2(0.8f, 0.8f), AimFrom(from) * 8f, 3f, false);
                PlayAttackPose(0.25f);
                yield return Wait(0.25f);
                yield return Recover(0.9f * RecoverMul);
            }
            else yield return SpikeRow(false);
        }
    }

    // 地面の針の列: 前方から手前へ順に突き出す(1回のジャンプで越える)
    IEnumerator SpikeRow(bool heavy)
    {
        yield return Telegraph(0.6f);
        PlayAttackPose(0.25f);
        Shake(0.06f, 0.12f);
        for (int i = 0; i < 3; i++) FloorAt(4.4f - i * 2.2f, 1.7f, 1.1f, 0.75f + i * 0.14f, 0.35f, CaveLook.Bone, heavy);
        yield return Wait(0.6f);
        yield return Recover(0.9f * RecoverMul);
    }

    IEnumerator MultiPoison()
    {
        yield return MoveToGap(7.5f, 5f, 1.2f);
        boltGlow.enabled = true;
        yield return Telegraph(0.75f);
        boltGlow.enabled = false;
        for (int i = 0; i < 3; i++) { float rel = 4f + i * 3.4f; FakeShot(new Color(0.4f, 1f, 0.35f), rel); PoolAt(rel, 1.6f, 3.4f, 4f); PlayAttackPose(0.15f); yield return Wait(0.25f); }
        yield return Recover(1.0f * RecoverMul);
    }

    // 尻尾の高速連続: 上(地面にいる)→下(跳ぶ)→上。床と天井は必ず間を空ける(CaveBossSafety)
    IEnumerator TailCombo()
    {
        yield return MoveToGap(3.5f, 5f, 1f);
        yield return Telegraph(0.6f, stingMark);
        int hits = Upgraded ? 4 : 3;
        for (int i = 0; i < hits; i++)
        {
            bool high = i % 2 == 0;
            if (high) BandAt(0f, 2.2f, 2.3f, 4.2f, 0.7f, 0.3f, CaveLook.Bone);
            else BandAt(0f, 2.2f, 0f, 1.1f, 0.7f, 0.3f, CaveLook.Bone);
            PlayAttackPose(0.2f);
            yield return Wait(0.85f);
        }
        yield return Recover(1.0f * RecoverMul);
    }

    IEnumerator KingsExecution()
    {
        if (!StartUlt("KING'S EXECUTION", new Color(1f, 0.45f, 0.55f))) yield break;
        // ① 薙ぎ払い(跳ぶ)
        yield return MoveToGap(4f, 6f, 1.2f);
        yield return Telegraph(0.85f, sweepMark);
        ShockFromFront(1.1f, 9f, CaveLook.Bone, true, 2.0f);
        PlayAttackPose(0.3f);
        yield return Wait(0.5f);
        // ② 針の野と頭上の毒針。安全な道は「針の後に地面へ戻る」「毒針の間は地面にいる」(床と天井は同時に来ない)
        FloorAt(-3.2f, 2f, 1.1f, 0.8f, 0.4f, CaveLook.Bone);
        FloorAt(0f, 2.2f, 1.1f, 0.9f, 0.4f, CaveLook.Bone, true);
        CeilAt(3.2f, 2f, 0.9f, 0.4f, CaveLook.Bone);
        yield return Wait(1.2f);
        CeilAt(0f, 2.2f, 0.8f, 0.4f, CaveLook.Bone, true);
        FloorAt(3.2f, 2f, 1.1f, 0.8f, 0.4f, CaveLook.Bone);
        yield return Wait(1.3f);
        // ③ 毒の雨(跳ぶ)
        yield return MoveToGap(8f, 7f, 0.8f);
        for (int i = 0; i < (Upgraded ? 3 : 2); i++) { float rel = 4.5f + i * 3.6f; FakeShot(new Color(0.4f, 1f, 0.35f), rel); PoolAt(rel, 1.6f, 3.2f, 4f); }
        yield return Wait(1.8f);
        // ④ 処刑の一突き(背が高い=二段ジャンプ)
        yield return MoveToGap(5f, 6f, 0.8f);
        LaneWarn(2f, 6f, 0f, 2.4f, 1.0f, TallLaneColor);
        Glow(1.0f, new Color(1f, 0.4f, 0.5f));
        yield return Telegraph(1.0f, stingMark);
        PlayAttackPose(0.35f);
        WaveFrom(Gap - FrontReach, 2.2f, 0f, 2.4f, 10f, CaveLook.Bone, true, 0.72f);
        yield return Wait(0.8f);
        yield return CaveRecovery(3.2f, "尻尾を叩きつけて硬直! 攻撃のチャンス!", new Vector2(1.06f, 0.84f), CaveLook.Bone, new Vector2(0.8f, 0.1f));
    }
}

// ===================================================================== //
// 70,000m バジリスク
// 第1段階: 噛みつき / 尻尾 / 毒ブレス
// 第2段階: 石化の視線(当たっても短時間動きが鈍るだけ。即死/完全停止はしない。視線の向き=帯の予告がはっきり見える)
// 必殺技 PETRIFYING GAZE: 視線の向きを見せる→視線の波(跳ぶ)→石柱(壊すか跳ぶ)→ブレス(高い=地面にいる)→突進(跳ぶ)。
//   隙: 目が弱る(頭が弱点=ダメージ増し)
// ===================================================================== //
public class BasiliskBoss : CaveBossBase
{
    BossHitbox bite, tail, breath, rush;
    BossTelegraphMarker biteMark, tailMark, breathMark, rushMark;
    SpriteRenderer gazeGlow;

    protected override void CaveInit()
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
        rush = NewHitbox("Rush", new Vector2(FrontReach * 0.3f, 0.55f), new Vector2(Mathf.Max(1.6f, halfWidth * 1.5f), 1.1f), BossFx.Slash(), new Color(0.7f, 0.95f, 0.5f, 0.9f));
        rushMark = NewMarker(new Vector2(FrontReach + 2.8f, 0.55f), new Vector2(5.6f, 1.1f));
        GameObject g = new GameObject("GazeGlow");
        g.transform.SetParent(transform, false);
        g.transform.localPosition = new Vector3(0f, bodyHeight * 0.6f, 0f);
        gazeGlow = g.AddComponent<SpriteRenderer>();
        gazeGlow.sprite = BossFx.Orb();
        gazeGlow.color = new Color(0.8f, 0.9f, 0.3f, 0.95f);
        gazeGlow.sortingOrder = RenderOrder.Boss + 1;
        gazeGlow.enabled = false;
    }

    protected override void OnInterrupted() { base.OnInterrupted(); if (gazeGlow != null) gazeGlow.enabled = false; }

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (UltimateReady(2)) { yield return PetrifyingGaze(); continue; }
            if (SpecialReady(SpecialPhase)) { MarkSpecial(); yield return Gaze(Random.value < 0.5f); continue; }
            int pick = BossAiUtil.PickNoRepeat(3, ref lastPick);
            if (pick == 0) yield return Melee(bite, biteMark, 1.4f, 3.0f, 0.5f, 0.24f, 0.7f);
            else if (pick == 1)
            {
                tail.damageAmount = HitDmg;
                yield return Telegraph(0.6f, tailMark);
                yield return Strike(tail, 0.3f, 0.08f);
                yield return Recover(0.8f * RecoverMul);
            }
            else
            {
                breath.damageAmount = HitDmg;
                yield return Telegraph(0.7f, breathMark);
                yield return Strike(breath, 0.5f, 0.05f);
                yield return Recover(1.0f * RecoverMul);
            }
        }
    }

    // 石化の視線: 目が光る→ボスからプレイヤーへの帯(低い=跳ぶ/高い=地面にいる)。当たると短く動きが鈍るだけ(ダメージなし)
    IEnumerator Gaze(bool low)
    {
        gazeGlow.enabled = true;
        float span = Mathf.Max(3f, Gap + 1.5f);
        BandAt(span * 0.5f - 1f, span, low ? 0f : 2.3f, low ? 1.1f : 3.8f, 0.9f, 0.35f, CaveLook.Gaze, false, 0, 0.75f, 0.8f);
        yield return Telegraph(0.9f);
        PlayAttackPose(0.3f);
        yield return Wait(0.35f);
        gazeGlow.enabled = false;
        yield return Recover(0.9f * RecoverMul);
    }

    IEnumerator PetrifyingGaze()
    {
        if (!StartUlt("PETRIFYING GAZE", new Color(0.9f, 1f, 0.4f))) yield break;
        yield return MoveToGap(6f, 6f, 1.2f);
        // ① 視線の向きをはっきり見せる(目の光+帯)→ 低い視線(跳ぶ)
        gazeGlow.enabled = true;
        yield return Gaze(true);
        gazeGlow.enabled = true;
        // ② 視線の波(低い=跳ぶ。当たると鈍る+ダメージ)
        LaneWarn(2f, 6f, 0f, 1.1f, 0.7f, LowLaneColor);
        yield return Wait(0.7f);
        var w = WaveFrom(Gap - FrontReach, 1.3f, 0f, 1.1f, 9f, CaveLook.Gaze, false, 0.75f);
        w.SetSlow(0.8f, 0.6f);
        yield return Wait(0.8f);
        // ③ 石柱(壊すか跳ぶ)
        PillarAt(5f, 1.1f, 0.7f, 2, 4f, CaveLook.Rock);
        PillarAt(9.5f, 1.1f, 0.9f, 2, 4f, CaveLook.Rock);
        if (Upgraded) PillarAt(14f, 1.1f, 1.1f, 2, 4f, CaveLook.Rock);
        yield return Wait(1.8f);
        // ④ ブレス(高い帯=地面にいる)
        BandAt(1.5f, 7f, 2.2f, 4.2f, 0.9f, 0.6f, CaveLook.Poison, true);
        yield return Telegraph(0.9f, breathMark);
        PlayAttackPose(0.6f);
        yield return Wait(0.8f);
        gazeGlow.enabled = false;
        // ⑤ 突進(跳ぶ)
        yield return Rush(rush, rushMark, 8f, 0.8f, 0.55f, 15f, true);
        yield return CaveRecovery(3.4f, "目が弱った! 頭が弱点!", new Vector2(1.05f, 0.85f), CaveLook.Gaze, new Vector2(0.85f, 0.45f), 2.2f, 1.5f);
    }
}

// ===================================================================== //
// 80,000m 地底竜
// 第1段階: 噛みつき / 爪 / 突進(跳ぶ) / ブレス
// 第2段階: 潜って真下から(跳ぶ) / 尻尾で天井を叩く→落石(跳ぶ)
// 必殺技 CAVERN DRAGON RAGE(長すぎない): 咆哮→崩落→床の亀裂(跳ぶ)→ブレス(高い=地面にいる)→潜る→巨大な飛び出し(二段ジャンプ)→突進(跳ぶ)。
//   隙: 頭を下げる(長い反撃の時間)
// ===================================================================== //
public class DrakeBoss : CaveBossBase
{
    BossHitbox bite, claw, breath, rush;
    BossTelegraphMarker biteMark, clawMark, breathMark, rushMark;

    protected override void CaveInit()
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
        rush = NewHitbox("Rush", new Vector2(FrontReach * 0.3f, 0.55f), new Vector2(Mathf.Max(1.6f, halfWidth * 1.5f), 1.1f), BossFx.Slash(), new Color(1f, 0.6f, 0.3f, 0.9f));
        rushMark = NewMarker(new Vector2(FrontReach + 2.8f, 0.55f), new Vector2(5.6f, 1.1f));
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (UltimateReady(2)) { yield return DragonRage(); continue; }
            if (SpecialReady(SpecialPhase))
            {
                MarkSpecial();
                if (Random.value < 0.5f) yield return UndergroundBurst(); else yield return TailCeiling();
                continue;
            }
            int pick = BossAiUtil.PickNoRepeat(4, ref lastPick);
            if (pick == 0) yield return Melee(bite, biteMark, 1.5f, 3.0f, 0.55f, 0.24f, 0.8f);
            else if (pick == 1)
            {
                claw.damageAmount = HitDmg;
                yield return Telegraph(0.5f, clawMark);
                StartCoroutine(DashMove(0.3f, 5f));
                yield return Strike(claw, 0.26f);
                yield return Recover(0.8f * RecoverMul);
            }
            else if (pick == 2)
            {
                breath.damageAmount = HitDmg;
                yield return Telegraph(0.9f, breathMark);
                yield return Strike(breath, 0.55f, 0.06f);
                yield return Recover(1.1f * RecoverMul);
            }
            else { yield return Rush(rush, rushMark, 7.5f, 0.8f, 0.5f, 13f, false); yield return Recover(0.9f * RecoverMul); }
        }
    }

    IEnumerator UndergroundBurst()
    {
        yield return Burrow(0.35f);
        yield return TunnelTo(0.3f, 8.5f, 3f);
        yield return EmergeStrike(2.6f, 1.2f, 0.75f, CaveLook.Fire);
        yield return Recover(1.0f * RecoverMul);
    }

    IEnumerator TailCeiling()
    {
        yield return Telegraph(0.8f);
        PlayAttackPose(0.3f);
        Shake(0.14f, 0.25f);
        RockAt(4f, 1.0f, 0.9f, 6.5f, false, CaveLook.Rock);
        RockAt(7f, 1.0f, 1.25f, 6.5f, false, CaveLook.Rock);
        yield return Wait(0.6f);
        yield return Recover(0.9f * RecoverMul);
    }

    IEnumerator DragonRage()
    {
        if (!StartUlt("CAVERN DRAGON RAGE", new Color(1f, 0.5f, 0.2f))) yield break;
        yield return MoveToGap(7f, 6f, 1.2f);
        // ① 咆哮(天井にひび)
        Glow(0.9f, new Color(1f, 0.45f, 0.2f));
        TellAt(2f, 0.9f, CaveTellStyle.Crack, CaveLook.Rock); TellAt(5f, 0.9f, CaveTellStyle.Crack, CaveLook.Rock);
        yield return Telegraph(0.9f);
        Shake(0.2f, 0.5f);
        // ② 崩落(跳ぶ)
        RockAt(4f, 1.0f, 0.8f, 6.5f);
        RockAt(7.5f, 1.0f, 1.1f, 6.5f);
        yield return Wait(1.6f);
        // ③ 床の亀裂(跳ぶ)
        FloorAt(0f, 2.4f, 1.2f, 0.85f, 0.35f, CaveLook.Fire, true);
        yield return Wait(1.1f);
        // ④ ブレス(高い帯=地面にいる)
        BandAt(1.5f, 7.5f, 2.3f, 4.2f, 0.85f, 0.7f, CaveLook.Fire, true);
        yield return Telegraph(0.85f, breathMark);
        PlayAttackPose(0.7f);
        yield return Wait(0.8f);
        // ⑤ 潜る → 巨大な飛び出し(背が高い=二段ジャンプ)
        yield return Burrow(0.3f);
        yield return TunnelTo(0.8f, 9f, 1.2f);
        yield return EmergeStrike(3.2f, 2.4f, 1.1f, CaveLook.Fire, true, 0.4f);
        if (Upgraded) { RockAt(6f, 1.0f, 0.9f, 6.5f); yield return Wait(0.6f); }
        // ⑥ 突進(跳ぶ)
        yield return Rush(rush, rushMark, 7.5f, 0.8f, 0.55f, 15f, true);
        yield return CaveRecovery(4.0f, "頭が下がった! 長い反撃のチャンス!", new Vector2(1.1f, 0.75f), CaveLook.Fire, new Vector2(0.85f, 0.3f), 2.4f, 1.3f);
    }
}

// ===================================================================== //
// 90,000m 古代地底悪魔
// 第1段階: 爪 / 魔法弾 / 地面の魔法陣(跳ぶ) / 短い瞬間移動(移動先に魔法陣+影)
// 第2段階: 床と天井の魔法(交互。同時には来ない)
// 第3段階(残りHP少): 暴走 - 隙が短くなり、攻撃の後に魔法陣を続けることがある
// 必殺技 ABYSSAL CAVERN: 安全な場所が 地面(天井の魔法)→空中(床の魔法=跳ぶ/二段ジャンプ)→地面(下攻撃で早く降りる) と変わる
//   →魔法弾の連射。隙: 魔力切れ(長いBREAK)
// ===================================================================== //
public class AncientDemonBoss : CaveBossBase
{
    BossHitbox claw;
    BossTelegraphMarker clawMark;

    protected override IEnumerator Enter()
    {
        yOffset = 4f;
        SetPose(Pose.Fly);
        yield return base.Enter();
        yield return SetAltitude(1.4f, 0.6f);
    }

    protected override void CaveInit()
    {
        locoStyle = LocoStyle.Wing;
        yOffset = 4f;
        restAltitude = 1.4f;
        Vector2 cc = new Vector2(FrontReach + 0.5f, bodyHeight * 0.5f), cs = new Vector2(2.8f, 1.8f);
        claw = NewHitbox("Claw", cc, cs, BossFx.Slash(), new Color(0.7f, 0.2f, 1f, 0.95f));
        clawMark = NewMarker(cc, cs);
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (UltimateReady(2)) { yield return AbyssalCavern(); continue; }
            if (SpecialReady(SpecialPhase)) { MarkSpecial(); yield return FloorCeilingMagic(); continue; }
            int pick = BossAiUtil.PickNoRepeat(4, ref lastPick);
            if (pick == 0) yield return Melee(claw, clawMark, 1.8f, 2.6f, 0.6f, 0.26f, 0.8f, 0.06f);
            else if (pick == 1) yield return MagicShots(Enraged ? 3 : 2, false);
            else if (pick == 2) yield return GroundCircle(false);
            else yield return Teleport();
            // 第3段階の暴走: 攻撃の後に魔法陣を続ける
            if (Enraged && Random.value < 0.4f) yield return GroundCircle(false);
        }
    }

    IEnumerator GroundCircle(bool heavy)
    {
        TellAt(0f, 0.9f, CaveTellStyle.Circle, CaveLook.Magic);
        FloorAt(0f, 2.2f, 1.2f, 0.9f, 0.35f, CaveLook.Magic, heavy);
        yield return Telegraph(0.9f);
        Shake(0.08f, 0.15f);
        PlayAttackPose(0.25f);
        yield return Wait(0.35f);
        yield return Recover(0.9f * RecoverMul);
    }

    IEnumerator MagicShots(int n, bool heavy)
    {
        yield return Telegraph(0.75f);
        for (int i = 0; i < n && !IsDead; i++)
        {
            Vector3 from = FrontWorld(0.4f, bodyHeight * 0.6f);
            Projectile(BossFx.Orb(), new Color(0.75f, 0.3f, 1f), from, new Vector2(0.8f, 0.8f), AimFrom(from) * 7.5f, 3f, heavy);
            PlayAttackPose(0.2f);
            yield return Wait(0.35f);
        }
        yield return Recover(0.8f * RecoverMul);
    }

    // 短い瞬間移動: 移動先に魔法陣と影 → 消える → 現れる → 爪
    IEnumerator Teleport()
    {
        float dest = Random.value < 0.5f ? 3.2f : 6.5f;
        TellAt(dest, 0.7f, CaveTellStyle.Circle, CaveLook.Magic);
        TellAt(dest, 0.7f, CaveTellStyle.Shadow);
        invulnerable = true;
        float t = 0f;
        while (t < 0.3f && !IsDead) { t += Time.deltaTime; SetAlpha(1f - t / 0.3f); yield return null; }
        yield return Wait(0.4f);
        freeGap = true;
        worldX = PlayerX + dest;
        t = 0f;
        while (t < 0.25f && !IsDead) { t += Time.deltaTime; SetAlpha(t / 0.25f); yield return null; }
        SetAlpha(1f);
        invulnerable = false;
        freeGap = false;
        yield return Melee(claw, clawMark, 1.8f, 3.2f, 0.6f, 0.26f, 0.8f, 0.06f);
    }

    // 床と天井の魔法(交互。CaveBossSafetyが必ず間を空ける)
    IEnumerator FloorCeilingMagic()
    {
        yield return Telegraph(0.6f);
        FloorAt(0f, 2.2f, 1.2f, 0.85f, 0.35f, CaveLook.Magic);
        yield return Wait(0.9f);
        CeilAt(0f, 2.4f, 0.85f, 0.4f, CaveLook.Magic);
        if (Enraged || Upgraded) { yield return Wait(0.9f); FloorAt(0f, 2.2f, 1.2f, 0.85f, 0.35f, CaveLook.Magic); }
        yield return Wait(1.0f);
        yield return Recover(0.9f * RecoverMul);
    }

    IEnumerator AbyssalCavern()
    {
        if (!StartUlt("ABYSSAL CAVERN", new Color(0.8f, 0.35f, 1f))) yield break;
        yield return MoveToGap(7f, 6f, 1.2f);
        yield return SetAltitude(2.6f, 0.4f);
        Glow(1.0f, new Color(0.8f, 0.3f, 1f));
        // A: 地面が安全(天井の魔法=紫)
        CeilAt(0f, 3.2f, 0.9f, 0.5f, CaveLook.Magic, true);
        CeilAt(3.6f, 2.4f, 0.9f, 0.5f, CaveLook.Magic);
        CeilAt(-3.6f, 2.4f, 0.9f, 0.5f, CaveLook.Magic);
        yield return Telegraph(0.9f);
        yield return Wait(0.8f);
        // B: 空中が安全(床一面の魔法=赤。跳ぶ/二段ジャンプで有効時間をやり過ごす)
        BossBattleHud.Banner("跳べ!", new Color(1f, 0.5f, 0.4f), 0.8f);
        FloorAt(0f, 4.2f, 0.9f, 1.0f, 0.7f, CaveLook.Magic, true);
        FloorAt(4.2f, 4.2f, 0.9f, 1.0f, 0.7f, CaveLook.Magic);
        yield return Wait(1.9f);
        // C: また地面が安全(天井の魔法。下攻撃で早く降りられる)。Bの後は着地の時間を十分に取る
        BossBattleHud.Banner("降りろ!", new Color(0.8f, 0.6f, 1f), 0.8f);
        CeilAt(0f, 3.2f, 1.0f, 0.5f, CaveLook.Magic, true);
        if (Upgraded) CeilAt(3.6f, 2.4f, 1.2f, 0.5f, CaveLook.Magic);
        yield return Wait(1.7f);
        // D: 魔法弾の連射
        yield return SetAltitude(1.4f, 0.3f);
        yield return MagicShots(Upgraded ? 4 : 3, true);
        yield return CaveRecovery(4.5f, "魔力切れ! 長い攻撃のチャンス!", new Vector2(1.0f, 0.85f), CaveLook.Magic, new Vector2(0f, 0.7f), 3.0f, 1.3f);
    }
}
