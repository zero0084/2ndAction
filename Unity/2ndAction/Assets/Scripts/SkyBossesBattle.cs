using System.Collections;
using UnityEngine;

// 天空回廊ボス強化(2026-10-05)。荒野街道/自然洞窟と同じ「ボス戦の強化」(段階/特殊攻撃/必殺技/崩し/スーパーアーマー/
// 必殺技後の大きな隙/ラン再開/保留/再戦)と、洞窟で作った地形/予兆の共通層(HazardBossBase/CaveHazard/CaveBossSafety)を
// そのまま使う。天空専用の仕組みは複製しない。
//   天空回廊 = 「空そのものが攻撃になる災害戦」。落雷/光柱(SkyStrike: 予兆の地点を見て、前後の攻撃の踏み込みで位置をずらす)、
//   帯(赤=低い→跳ぶ / 紫=高い→地面にいる / 金=背が高い→二段ジャンプ)、横切る巨体(見た目と当たり判定は別)。
//   各ボスの既存の攻撃(SkyBosses.cs)は第1段階として残し、ここで第2段階の技・必殺技・必殺技後の隙を足す。
//   10,000m以降の専用ボスの必殺技は判断を3回以上(1回のジャンプでは避けきれない)。再戦の段階で一手増える(Upgraded)。
public abstract partial class SkyBossBase
{
    // 段階の咆哮/BREAK/強制操作の割り込みで止まった見た目の処理(周りの雲/雷など)を戻す
    protected override void OnInterrupted()
    {
        base.OnInterrupted();
        SkyRestore();
    }
    protected virtual void SkyRestore() { }

    protected static readonly Color SkyLightning = new Color(0.6f, 0.85f, 1f, 1f);
    protected static readonly Color SkyHoly = new Color(1f, 0.93f, 0.7f, 1f);

    // 予兆の地点攻撃(落雷/光柱/拳/岩): プレイヤーからの距離で。走行と一緒に流れる(避け方=前後の攻撃の踏み込みで位置をずらす)
    protected SkyStrike StrikeAt(float relX, float width, float height, float warn, Color c, SkyStrike.Look look)
    {
        return SkyStrike.Create(PlayerX + relX, width, height, warn, 0.3f, c, look);
    }
}

// ============ 10,000m 雷獣ベヒーモス ============
// 第2段階: 全身帯電 — 突進の後に雷が残る(突進=跳ぶ → 残った雷の床=もう一度跳ぶ)
// 必殺技 THUNDER APOCALYPSE: 空が暗くなる → 咆哮 → 周りに落雷の予兆(時間差。前後の踏み込みで位置をずらす) → 地面を走る電撃(跳ぶ)
//   →(再戦の強化: 後ろからも電撃 / 空中の雷=地面にいる)→ 雷をまとった高速突進(跳ぶ)。隙: 帯電が消えて疲労
public partial class BehemothBoss
{
    IEnumerator ElectricTrail()
    {
        yield return MoveToGap(10f, 6f, 2f);
        hornGlow.enabled = true;
        StartCoroutine(FollowGlow(hornGlow, 0.9f, 0.55f, 0.4f, 1.6f, 0.9f));
        charge.damageAmount = HitDmg;
        yield return Telegraph(0.9f, chargeMark);
        hornGlow.enabled = false;
        Sfx(SkyBossSfx.Whoosh(), 0.9f);
        yield return Swoop(Gap, OffscreenBehindGap(), 17f, 0f, charge);
        // 走った後に雷が残る(通った床が光る → 有効)
        FloorAt(0f, 7f, 1.0f, 0.55f, 0.6f, CaveLook.Lightning);
        SkyBossFx.Sparks(new Vector3(PlayerX, PlayerGroundY + 0.2f, 0f), Bolt, 10, 0.7f);
        yield return Wait(0.6f);
        facing = -1f; facingLocked = false;
        yield return MoveToGap(7f, 16f, 2.5f);
        freeGap = false;
        yield return Recover(0.8f * RecoverMul);
    }

    IEnumerator ThunderApocalypse()
    {
        if (!StartUlt("THUNDER APOCALYPSE", new Color(0.6f, 0.85f, 1f))) yield break;
        CaveDarkness.Set(0.32f);
        yield return MoveToGap(9f, 6f, 1.5f);
        Sfx(SkyBossSfx.Roar(), 1f);
        Shake(0.12f, 0.6f);
        yield return Telegraph(1.0f);
        // 落雷の予兆(時間差。最初の2つは周り、3つ目がプレイヤーの位置 → 前後へずらす)
        StrikeAt(3.6f, 1.8f, 7f, 1.2f, Bolt, SkyStrike.Look.Bolt);
        StrikeAt(-3.6f, 1.8f, 7f, 1.5f, Bolt, SkyStrike.Look.Bolt);
        StrikeAt(0.2f, 1.8f, 7f, 1.9f, Bolt, SkyStrike.Look.Bolt);
        if (Upgraded) { StrikeAt(6.8f, 1.8f, 7f, 2.3f, Bolt, SkyStrike.Look.Bolt); StrikeAt(-6.8f, 1.8f, 7f, 2.6f, Bolt, SkyStrike.Look.Bolt); }
        PlayAttackPose(0.6f);
        yield return Wait(Upgraded ? 2.8f : 2.3f);
        // 地面を走る電撃(跳ぶ)
        LaneWarn(2f, 8f, 0f, 1.0f, 0.7f, LowLaneColor);
        yield return Wait(0.7f);
        WaveFrom(OffscreenAheadGap(), 1.6f, 0f, 1.0f, 10f, CaveLook.Lightning, true, 0.72f);
        if (Upgraded)
        {
            yield return Wait(1.0f);
            WaveFrom(OffscreenBehindGap(), 1.6f, 0f, 1.0f, -10f, CaveLook.Lightning, true, 0.72f); // 左右から
            yield return Wait(1.0f);
            BandAt(0f, 9f, 2.3f, 4.2f, 0.85f, 0.4f, CaveLook.Lightning, true);                  // 空中の雷(地面にいる)
        }
        yield return Wait(1.4f);
        // 雷をまとった高速突進(跳ぶ)
        yield return MoveToGap(11f, 8f, 1.2f);
        hornGlow.enabled = true;
        StartCoroutine(FollowGlow(hornGlow, 0.9f, 0.55f, 0.6f, 2.2f, 0.9f));
        charge.damageAmount = UltimateDamage;
        yield return Telegraph(0.95f, chargeMark);
        hornGlow.enabled = false;
        Sfx(SkyBossSfx.Whoosh(), 1f);
        yield return Swoop(Gap, OffscreenBehindGap(), 20f, 0f, charge);
        charge.damageAmount = HitDmg;
        CaveDarkness.Set(0f);
        facing = -1f; facingLocked = false;
        yield return MoveToGap(3.5f, 18f, 1.5f);
        yield return CaveRecovery(3.4f, "帯電が消えた! 大きな反撃のチャンス!", new Vector2(1.05f, 0.85f), CaveLook.Lightning, new Vector2(0f, 0.8f));
    }

    protected override void SkyRestore() { StartCoroutine(Ambient()); if (hornGlow != null) hornGlow.enabled = false; charge.damageAmount = HitDmg; }
}

// ============ 20,000m 天空タイタン ============
// 第2段階: 雷の槍の雨(時間差で3本=踏み込みで位置をずらす)+巨大エネルギー波(跳ぶ)
// 必殺技 JUDGMENT OF TITAN: 腕を上げる → 画面に大きな影 → 拳を浮島へ(前方)→ 第一衝撃波(跳ぶ)→ 足場の亀裂(跳ぶ)
//   → 上空から雷の槍(位置をずらす)→ 巨大な手の薙ぎ払い(背が高い=二段ジャンプ)。安全地帯は必ず残る。隙: 巨大な手が足場付近に残る
public partial class SkyTitanBoss
{
    IEnumerator SpearRain()
    {
        handGlow.enabled = true;
        StartCoroutine(FollowGlow(handGlow, 0.55f, 0.78f, 0.4f, 2.6f, 1.0f));
        yield return Telegraph(1.0f);
        handGlow.enabled = false;
        float[] rel = { 2.6f, -0.2f, -2.8f };
        for (int i = 0; i < rel.Length; i++)
        {
            var st = StrikeAt(rel[i], 2.0f, 3.2f, 1.3f + i * 0.55f, new Color(0.7f, 0.9f, 1f, 1f), SkyStrike.Look.Burst);
            StartCoroutine(SpearFlight(BodyPoint(0.55f, 0.78f), st, 0.72f + i * 0.55f));
        }
        PlayAttackPose(0.6f);
        yield return Wait(2.8f);
        // 巨大エネルギー波(跳ぶ)
        LaneWarn(2f, 8f, 0f, 1.1f, 0.7f, LowLaneColor);
        yield return Wait(0.7f);
        WaveFrom(Mathf.Max(6f, Gap - 2f), 1.8f, 0f, 1.1f, 9f, CaveLook.Lightning, false, 0.72f);
        yield return Recover(1.4f * RecoverMul);
    }

    IEnumerator JudgmentOfTitan()
    {
        if (!StartUlt("JUDGMENT OF TITAN", new Color(0.85f, 0.9f, 1f))) yield break;
        // 巨大な腕を上げる → 画面へ大きな影(拳は前方の浮島へ)
        Glow(1.4f, new Color(0.85f, 0.95f, 1f));
        StrikeAt(3.4f, 4.0f, 2.6f, 2.0f, new Color(0.88f, 0.84f, 0.8f, 1f), SkyStrike.Look.Fist);
        TellAt(3.4f, 2.0f, CaveTellStyle.Shadow, CaveLook.Rock, true);
        Sfx(SkyBossSfx.Roar(), 1f);
        yield return Telegraph(1.4f);
        PlayAttackPose(1.0f);
        yield return Wait(0.6f);
        Shake(0.22f, 0.4f);
        // 第一衝撃波(跳ぶ)
        WaveFrom(1.6f, 1.6f, 0f, 1.1f, 9f, CaveLook.Rock, true, 0.72f);
        yield return Wait(1.0f);
        // 足場の亀裂(跳ぶ)
        FloorAt(0f, 2.6f, 1.1f, 0.85f, 0.4f, CaveLook.Lightning, true);
        FloorAt(3.6f, 2.4f, 1.1f, 0.85f, 0.4f, CaveLook.Lightning);
        FloorAt(-3.6f, 2.4f, 1.1f, 0.85f, 0.4f, CaveLook.Lightning);
        yield return Wait(1.4f);
        // 上空から雷の槍(時間差。プレイヤーの位置へは最後=踏み込みでずらす)
        StrikeAt(-3f, 2.0f, 6f, 1.2f, SkyLightning, SkyStrike.Look.Burst);
        StrikeAt(3f, 2.0f, 6f, 1.5f, SkyLightning, SkyStrike.Look.Burst);
        StrikeAt(0f, 2.0f, 6f, 2.0f, SkyLightning, SkyStrike.Look.Burst);
        if (Upgraded) StrikeAt(-6f, 2.0f, 6f, 2.4f, SkyLightning, SkyStrike.Look.Burst);
        yield return Wait(Upgraded ? 2.7f : 2.3f);
        // 巨大な手で前方を薙ぎ払う(背が高い=二段ジャンプ)
        LaneWarn(2f, 9f, 0f, 2.3f, 1.0f, TallLaneColor);
        Glow(1.0f, new Color(1f, 0.9f, 0.7f));
        yield return Wait(1.0f);
        WaveFrom(OffscreenAheadGap(), 3.2f, 0f, 2.3f, 10f, CaveLook.Rock, true, 0.72f);
        yield return Wait(1.5f);
        // 隙: 巨大な手が足場付近に残る(身をかがめて回廊のすぐ前。腕/胸を攻撃できる)
        float closeGap = kneelReachGap + Mathf.Max(3f, hurtWidth) * 0.5f;
        float keepMin = minGap;
        minGap = Mathf.Min(minGap, closeGap - 0.5f);
        IsKneeling = true;
        yield return MoveToGap(closeGap, 9f, 1.4f);
        yield return CaveRecovery(4.0f, "巨大な手が足場に残った! 大きなチャンス!", new Vector2(1f, 0.95f), CaveLook.Rock, new Vector2(0.6f, 0.35f), 2.4f, 1.3f, RiseY - kneelLower);
        IsKneeling = false;
        minGap = keepMin;
        yield return MoveToGap(startGap, 6f, 2f);
    }

    protected override void SkyRestore()
    {
        yOffset = restAltitude;
        SetVisualSortingOrder(BehindGround);
        ConfigureHurtbox(new Vector2(0f, -RiseY + 2.8f), new Vector2(hurtWidth, 4.4f));
        IsKneeling = false;
        if (mouthGlow != null) mouthGlow.enabled = false;
        if (handGlow != null) handGlow.enabled = false;
        StartCoroutine(Ambient());
    }
}

// ============ 30,000m 天空クラゲ ============
// 「静かなのに危険」。第2段階: 複数の触手を別の位置へ(下から=跳ぶ / 上から=地面にいる。同時には来ない)
// 必殺技 HEAVENLY PULSE: 大きく発光 → 触手が上下へ広がる → 第一電撃波(低い=跳ぶ)→ 第二電撃波(高い=地面にいる)
//   → 電撃球が漂う → 最後に全周 Pulse(低い輪 → 高い輪の順。高さとタイミングで避ける)。隙: コアが暗くなる(崩しやすい)
public partial class SkyJellyfishBoss
{
    bool lowHold;

    IEnumerator TentacleField()
    {
        coreGlow.enabled = true;
        StartCoroutine(FollowGlow(coreGlow, 0f, 0.55f, 0.5f, 2f, 0.8f));
        yield return Telegraph(0.8f);
        coreGlow.enabled = false;
        FloorAt(-3.2f, 2f, 1.1f, 0.85f, 0.4f, CaveLook.Lightning);
        BandAt(3.2f, 2.2f, 2.3f, 4.2f, 0.85f, 0.4f, CaveLook.Lightning);
        yield return Wait(1.0f);
        FloorAt(3.2f, 2f, 1.1f, 0.85f, 0.4f, CaveLook.Lightning);
        BandAt(-3.2f, 2.2f, 2.3f, 4.2f, 0.85f, 0.4f, CaveLook.Lightning);
        yield return Wait(1.0f);
        FloorAt(0f, 2.2f, 1.1f, 0.9f, 0.4f, CaveLook.Lightning);
        yield return Wait(1.0f);
        yield return Recover(1.0f * RecoverMul);
    }

    IEnumerator HeavenlyPulse()
    {
        if (!StartUlt("HEAVENLY PULSE", new Color(0.65f, 0.95f, 1f))) yield break;
        yield return MoveToGap(7f, 3f, 1.5f);
        coreGlow.enabled = true;
        StartCoroutine(FollowGlow(coreGlow, 0f, 0.55f, 0.8f, 3.4f, 1.2f));
        Glow(1.2f, new Color(0.7f, 1f, 1f));
        TellAt(3f, 1.2f, CaveTellStyle.Circle, CaveLook.Lightning); TellAt(-3f, 1.2f, CaveTellStyle.Circle, CaveLook.Lightning);
        yield return Telegraph(1.2f);   // 触手が上下へ広がる
        coreGlow.enabled = false;
        // 第一電撃波(低い=跳ぶ)
        LaneWarn(2f, 7f, 0f, 1.1f, 0.6f, LowLaneColor);
        yield return Wait(0.6f);
        WaveFrom(Gap - 1f, 1.4f, 0f, 1.1f, 8f, CaveLook.Lightning, true, 0.7f);
        yield return Wait(1.2f);
        // 第二電撃波(高い=地面にいる)
        LaneWarn(2f, 7f, 2.3f, 3.6f, 0.7f, HighLaneColor);
        yield return Wait(0.7f);
        WaveFrom(Gap - 1f, 1.4f, 2.3f, 3.6f, 8f, CaveLook.Lightning, true, 0.7f);
        if (Upgraded) { yield return Wait(1.0f); LaneWarn(2f, 7f, 0f, 1.1f, 0.6f, LowLaneColor); yield return Wait(0.6f); WaveFrom(Gap - 1f, 1.4f, 0f, 1.1f, 8f, CaveLook.Lightning, true, 0.7f); }
        yield return Wait(1.0f);
        // 電撃球が漂う(遅い)
        Vector3 from = BodyPoint(0.2f, 0.5f);
        for (int i = -1; i <= 1; i++)
        {
            Vector2 d = Quaternion.Euler(0f, 0f, i * 20f) * AimFrom(from);
            BossProjectile.Create(BossFx.Orb(), new Color(0.65f, 0.92f, 1f, 1f), from, new Vector2(0.9f, 0.9f), d * 2.6f, 5f, RenderOrder.Boss + 1);
        }
        yield return Wait(1.4f);
        // 全周 Pulse: 低い輪 → 少し遅れて高い輪
        BandAt(0f, 14f, 0f, 1.0f, 1.0f, 0.35f, CaveLook.Lightning, true);
        BandAt(0f, 14f, 2.2f, 4.2f, 1.0f, 0.35f, CaveLook.Lightning, true);   // CaveBossSafety が床側の後へずらす
        OneShotSpriteEffect.CreateTweened(BossFx.Ring(), CenterWorld, Pale, 1.8f, 1f, 14f, 0.8f, 0f, default, 0f, RenderOrder.CombatFx, 0.4f);
        yield return Wait(2.4f);
        // 隙: コアが暗くなる(低く降りてくる)
        lowHold = true;
        yield return CaveRecovery(3.8f, "コアが暗くなった! 崩しやすい!", new Vector2(1.05f, 0.9f), CaveLook.Water, new Vector2(0f, 0.55f), 3f, 1.35f, 0.4f);
        lowHold = false;
    }

    protected override void SkyRestore() { lowHold = false; if (coreGlow != null) coreGlow.enabled = false; StartCoroutine(Ambient()); }
}

// ============ 40,000m 雲海リヴァイアサン ============
// 第2段階: 画面奥から巨体が迫る(雲海の影が近づく → 頭部の飛び出しとブレスの連続)
// 必殺技 LEVIATHAN CROSSING: 雲海の巨大な影 → 頭部が下から飛び出す(前方)→ 頭が頭上を横断(高い=地面にいる)
//   → 胴体が時間差で通過(低い節=跳ぶ×3)→ 尾の薙ぎ払い(背が高い=二段)→ 雲海からブレス(低い=跳ぶ)。
//   見えている巨体全体は判定にしない(頭/節/尾の帯だけ)。隙: 頭部がプレイヤーの近くへ戻る
public partial class LeviathanBoss
{
    IEnumerator DeepApproach()
    {
        // 雲海の影が奥から迫る
        for (int i = 0; i < 4; i++) TellAt(10f - i * 2.5f, 0.9f + i * 0.2f, CaveTellStyle.Shadow, CaveLook.Water, i == 3);
        Shake(0.08f, 0.8f);
        yield return Wait(1.0f);
        yield return BurstUp();
        yield return SwimTo(8f, 10f);
        yield return CloudBreath();
    }

    IEnumerator LeviathanCrossing()
    {
        if (!StartUlt("LEVIATHAN CROSSING", new Color(0.7f, 0.9f, 1f))) yield break;
        yield return SwimTo(14f, 14f);
        // 雲海に巨大な影
        for (int i = 0; i < 5; i++) TellAt(2f + i * 2.4f, 1.2f, CaveTellStyle.Shadow, CaveLook.Water, true);
        finVisible = true;
        Shake(0.12f, 1.0f);
        Sfx(SkyBossSfx.Roar(), 1f);
        yield return Wait(1.2f);
        finVisible = false;
        // 頭部が下から飛び出す(前方)
        yield return SwimTo(6f, 16f, 1f);
        SkyWarnBand.Create(PlayerX + 4f, PlayerX + 8f, -0.8f, 0.5f, 0.7f);
        yield return Wait(0.7f);
        yield return Emerge(0.28f);
        Shake(0.14f, 0.3f);
        yield return Wait(0.5f);
        yield return Submerge(0.4f);
        // 頭が頭上を横断(高い=地面にいる)
        LaneWarn(2f, 9f, 2.4f, 4.4f, 0.85f, HighLaneColor);
        yield return Wait(0.85f);
        WaveFrom(OffscreenAheadGap(), 3f, 2.4f, 4.4f, 12f, CaveLook.Water, true, 0.68f);
        yield return Wait(1.3f);
        // 胴体が時間差で通過(低い節)
        int segs = Upgraded ? 4 : 3;
        LaneWarn(2f, 9f, 0f, 1.1f, 0.7f, LowLaneColor);
        yield return Wait(0.7f);
        for (int i = 0; i < segs; i++) WaveFrom(OffscreenAheadGap() + i * 9f, 1.8f, 0f, 1.1f, 9f, CaveLook.Water, true, 0.68f);
        yield return Wait(segs * 1.0f + 0.8f);
        // 尾の薙ぎ払い(背が高い)
        LaneWarn(2f, 9f, 0f, 2.3f, 0.9f, TallLaneColor);
        yield return Wait(0.9f);
        WaveFrom(OffscreenAheadGap(), 2.4f, 0f, 2.3f, 9f, CaveLook.Water, true, 0.7f);
        yield return Wait(1.6f);
        // 雲海からブレス(低い=跳ぶ)
        yield return SwimTo(9f, 14f, 1.2f);
        yield return Emerge(0.5f);
        mouthGlow.enabled = true;
        StartCoroutine(FollowGlow(mouthGlow, 0.9f, 0.6f, 0.5f, 2.6f, 1.0f));
        BandAt(0f, 12f, 0f, 1.05f, 1.0f, 0.5f, CaveLook.Water, true);
        yield return Telegraph(1.0f);
        mouthGlow.enabled = false;
        PlayAttackPose(0.5f);
        yield return Wait(0.8f);
        yield return Submerge(0.4f);
        // 隙: 頭部がプレイヤーの近くへ戻る
        facing = -1f;
        yield return SwimTo(1.2f + FrontReach * 0.55f, 12f, 1.5f);
        yield return Emerge(0.4f);
        yield return CaveRecovery(3.6f, "頭部が近くに! 攻撃のチャンス!", new Vector2(1.04f, 0.9f), CaveLook.Water, new Vector2(0.6f, 0.6f));
        yield return Submerge(0.5f);
    }

    protected override void SkyRestore()
    {
        if (mouthGlow != null) mouthGlow.enabled = false;
        SetVisualSortingOrder(BehindGround);
        StartCoroutine(Ambient());
    }
}

// ============ 50,000m 天空魔狼フェンリル ============
// 第2段階: プレイヤーより上の空間も走る(魔力の足場が一瞬光る)→ 上から噛みつき
// 必殺技 FENRIR SKY HUNT: 追い越す(跳ぶ)→ 空中へ → 前方上空を走る(魔力の足跡=次に来る方向)→ 画面外へ → 影/魔力の予兆
//   → 後方から高速横断(跳ぶ)→ 再び上空 → 正面から巨大噛みつき(背が高い=二段)。隙: 着地で滑って体勢を崩す
public partial class FenrirBoss
{
    BossHitbox giantBite;

    IEnumerator SkyRun()
    {
        yield return Telegraph(0.6f);
        freeGap = true;
        yield return SetAltitude(3.4f, 0.4f);
        float t = 0f, step = 0f;
        while (t < 1.4f && !IsDead)
        {
            t += Time.deltaTime; step -= Time.deltaTime;
            relVelocity = Mathf.Clamp((-2.5f - Gap) * 2.5f, -14f, 14f);
            if (step <= 0f) { step = 0.18f; OneShotSpriteEffect.CreateTweened(SkyBossFx.GroundGlow(), new Vector3(worldX, GroundY + yOffset, 0f), Flame, 0.35f, 1.2f, 2.2f, 0.9f, 0f, default, 0f, RenderOrder.Boss - 1, 0.1f); }
            yield return null;
        }
        // 上から噛みつき(着地点を表示 → 前後へずらす)
        float landGap = 1.6f;
        StrikeAt(landGap, 3f, 1.4f, 0.9f, Flame, SkyStrike.Look.Burst);
        yield return Wait(0.6f);
        yield return Leap(0.35f, 0.2f, landGap - Gap);
        yOffset = 0f;
        ImpactDust(new Vector3(worldX, GroundY, 0f), 12, 1.2f);
        freeGap = false;
        SetPose(Pose.Landing);
        yield return Wait(0.9f * RecoverMul);
        yield return MoveToGap(6f, 10f, 2f);
    }

    IEnumerator FenrirSkyHunt()
    {
        if (!StartUlt("FENRIR SKY HUNT", new Color(0.6f, 0.85f, 1f))) yield break;
        if (giantBite == null)
        {
            Vector2 gc = new Vector2(FrontReach + 0.4f, 1.15f), gs = new Vector2(2.6f, 2.3f);
            giantBite = NewHitbox("GiantBite", gc, gs, BossFx.Fang(), new Color(0.75f, 0.9f, 1f, 0.95f));
        }
        // 追い越す(低い=跳ぶ)
        yield return MoveToGap(-9f, 16f, 2f);
        pass.damageAmount = UltimateDamage;
        facing = 1f; facingLocked = true;
        LaneWarn(0f, 9f, 0f, 1.24f, 0.7f, LowLaneColor);
        yield return Wait(0.7f);
        yield return Swoop(Gap, 12f, 22f, 0f, pass);
        // 空中へ → 前方上空を走る(魔力の足跡が次の方向を示す: 後ろへ回る)
        yield return SetAltitude(3.6f, 0.35f);
        facing = -1f;
        for (int i = 0; i < 4; i++) TellAt(9f - i * 3f, 1.0f + i * 0.15f, CaveTellStyle.Circle, CaveLook.Lightning, false);
        yield return MoveToGap(-4f, 18f, 1.2f);
        // 画面外へ → 影/魔力の予兆(後ろ)
        yield return ExitScreen(false, 20f, 3.6f, 1.2f);
        yOffset = 0f;
        TellAt(-5f, 0.9f, CaveTellStyle.Circle, CaveLook.Magic);
        LaneWarn(-3f, 9f, 0f, 1.24f, 0.9f, LowLaneColor);
        yield return Wait(0.9f);
        // 後方から高速横断(跳ぶ)
        yield return Swoop(OffscreenBehindGap(), OffscreenAheadGap(), 24f, 0f, pass);
        pass.damageAmount = HitDmg;
        // 再び上空 → 正面から巨大噛みつき(背が高い=二段)
        facing = -1f; facingLocked = false;
        yield return SetAltitude(3f, 0.3f);
        yield return MoveToGap(7f, 16f, 1.6f);
        yield return SetAltitude(0f, 0.3f);
        mouthGlow.enabled = true;
        StartCoroutine(FollowGlow(mouthGlow, 0.95f, 0.45f, 0.6f, 2.2f, 1.0f));
        LaneWarn(2f, 8f, 0f, 2.3f, 1.0f, TallLaneColor);
        giantBite.damageAmount = UltimateDamage;
        yield return Telegraph(1.0f);
        mouthGlow.enabled = false;
        Sfx(SkyBossSfx.Whoosh(), 1f);
        StartCoroutine(DashMove(0.35f, 18f));
        yield return Strike(giantBite, 0.35f, 0.12f);
        // 隙: 着地で滑る → 体勢を崩す
        SetPose(Pose.Landing);
        float t = 0f;
        while (t < 0.5f) { t += Time.deltaTime; relVelocity = -7f * (1f - t / 0.5f); if (Random.value < 0.4f) ImpactDust(new Vector3(worldX, GroundY, 0f), 2, 0.7f); yield return null; }
        yield return CaveRecovery(3.2f, "体勢を崩した! 反撃のチャンス!", new Vector2(1.15f, 0.7f), CaveLook.Lightning, new Vector2(0f, 0.3f));
    }

    protected override void SkyRestore()
    {
        yOffset = 0f;
        if (mouthGlow != null) mouthGlow.enabled = false;
        pass.damageAmount = HitDmg;
        StartCoroutine(Ambient());
    }
}

// ============ 60,000m 天空ゴーレム ============
// 第2段階: 身体を一部分解して攻撃(岩が飛ぶ 低い/高い + 上空からの落石)
// 必殺技 SKY FORTRESS: 身体が岩へ分解 → 岩がプレイヤーの周りへ(影)→ 時間差で突進(低い=跳ぶ / 高い=地面にいる)
//   → 巨大な拳を再形成(前方へ叩きつけ → 衝撃波=跳ぶ)→ Core Beam(低い=跳ぶ)→ 全岩が再集結して叩きつけ(巨大な岩=二段)。
//   隙: 再構築に失敗してコア露出(崩しやすい)
public partial class SkyGolemBoss
{
    IEnumerator Disassemble()
    {
        yield return Telegraph(0.7f);
        SetAlpha(0.6f);
        LaneWarn(2f, 7f, 0f, 1.1f, 0.6f, LowLaneColor);
        yield return Wait(0.6f);
        WaveFrom(Gap - FrontReach, 1.2f, 0f, 1.1f, 10f, CaveLook.Rock, false, 0.75f);
        yield return Wait(0.9f);
        LaneWarn(2f, 7f, 2.3f, 3.4f, 0.6f, HighLaneColor);
        yield return Wait(0.6f);
        WaveFrom(Gap - FrontReach, 1.2f, 2.3f, 3.4f, 10f, CaveLook.Rock, false, 0.75f);
        RockAt(5f, 1.0f, 1.2f, 6.5f);
        SetAlpha(1f);
        yield return Wait(1.0f);
        yield return Recover(1.0f * RecoverMul);
    }

    IEnumerator SkyFortress()
    {
        if (!StartUlt("SKY FORTRESS", new Color(0.55f, 0.95f, 1f))) yield break;
        yield return MoveToGap(9f, 3f, 1.2f);
        // 身体が岩へ分解 → プレイヤーの周りへ配置(影)
        invulnerable = true; SetHurtboxEnabled(false);
        float t = 0f;
        while (t < 0.5f) { t += Time.deltaTime; SetAlpha(1f - t); yield return null; }
        SetAlpha(0.35f);
        TellAt(8f, 1.0f, CaveTellStyle.Shadow, CaveLook.Rock); TellAt(-8f, 1.6f, CaveTellStyle.Shadow, CaveLook.Rock);
        yield return Wait(0.6f);
        // 時間差の突進: 前から低い(跳ぶ)→ 後ろから高い(地面にいる)
        LaneWarn(2f, 8f, 0f, 1.1f, 0.6f, LowLaneColor);
        yield return Wait(0.6f);
        WaveFrom(OffscreenAheadGap(), 1.4f, 0f, 1.1f, 10f, CaveLook.Rock, true, 0.72f);
        yield return Wait(1.0f);
        LaneWarn(-2f, 8f, 2.3f, 3.4f, 0.7f, HighLaneColor);
        yield return Wait(0.7f);
        WaveFrom(OffscreenBehindGap(), 1.4f, 2.3f, 3.4f, -10f, CaveLook.Rock, true, 0.72f);
        if (Upgraded) { yield return Wait(1.0f); LaneWarn(2f, 8f, 0f, 1.1f, 0.6f, LowLaneColor); yield return Wait(0.6f); WaveFrom(OffscreenAheadGap(), 1.4f, 0f, 1.1f, 10f, CaveLook.Rock, true, 0.72f); }
        yield return Wait(1.2f);
        // 巨大な拳を再形成 → 前方へ叩きつけ → 衝撃波(跳ぶ)
        SetAlpha(1f); invulnerable = false; SetHurtboxEnabled(true);
        fistGlow.enabled = true;
        StartCoroutine(FollowGlow(fistGlow, 0.85f, 0.75f, 0.6f, 2.6f, 1.0f));
        StrikeAt(3.2f, 3f, 2.4f, 1.2f, new Color(0.9f, 0.86f, 0.8f, 1f), SkyStrike.Look.Fist);
        yield return Telegraph(1.0f, fistMark);
        fistGlow.enabled = false;
        yield return Wait(0.2f);
        WaveFrom(1.6f, 1.4f, 0f, 1.1f, 9f, CaveLook.Rock, true, 0.72f);
        yield return Wait(1.1f);
        // Core Beam(低い=跳ぶ)
        coreGlow.enabled = true;
        beam.damageAmount = UltimateDamage;
        yield return Telegraph(1.2f, beamMark);
        Sfx(SkyBossSfx.Thunder(), 0.7f);
        yield return Strike(beam, 0.5f, 0.12f);
        beam.damageAmount = HitDmg;
        yield return Wait(0.6f);
        // 全岩が再集結して叩きつけ(巨大な岩=二段ジャンプ)
        RockAt(6.5f, 2.2f, 1.3f, 7f, true);
        yield return Wait(2.6f);
        // 隙: 再構築に失敗してコア露出
        coreGlow.enabled = true;
        yield return CaveRecovery(3.8f, "再構築に失敗! コア露出!", new Vector2(1.06f, 0.88f), CaveLook.Crystal, new Vector2(0.4f, 0.6f), 3f, 1.4f);
    }

    protected override void SkyRestore() { SetAlpha(1f); beam.damageAmount = HitDmg; if (fistGlow != null) fistGlow.enabled = false; if (coreGlow != null) coreGlow.enabled = true; StartCoroutine(Ambient()); }
}

// ============ 70,000m フェニックス ============
// 第2段階: 炎が強化され、急降下の後に短時間炎が残る(跳ぶ)
// 必殺技 PHOENIX REBIRTH: 上空へ → 大量の炎羽根(降る=地面にいる)→ 時間差の落下 → 炎の壁が流れてくる(低い=跳ぶ / 背が高い=二段)
//   → 炎をまとって突進(低い炎の波=跳ぶ → 身体=地面にいる)。隙: 炎が弱まって低く降りる
//   撃破時の復活は Data(BossBattleTuning.phoenixRebirth*)で: 初登場は復活 / 再戦は低い確率。復活後は攻撃的な最終段階
public partial class PhoenixBoss
{
    IEnumerator FlameTrail()
    {
        yield return DiveAttack();
        FloorAt(0.3f, 3.2f, 1.0f, 0.5f, 0.8f, CaveLook.Fire);   // 急降下の後に炎が残る
        yield return Wait(0.6f);
    }

    IEnumerator PhoenixRebirthUlt()
    {
        if (!StartUlt("PHOENIX REBIRTH", new Color(1f, 0.55f, 0.2f))) yield break;
        yield return SetAltitude(6.5f, 0.6f);
        yield return MoveToGap(6f, 8f, 1f);
        SetBodyTint(new Color(1f, 0.7f, 0.4f));
        Sfx(SkyBossSfx.Flame(), 1f);
        yield return Telegraph(0.9f);
        // 大量の炎羽根: 頭上へ降る(地面にいる)+ 前後に時間差の落下
        CeilAt(0f, 6f, 0.9f, 0.6f, CaveLook.Fire, true);
        StrikeAt(4.5f, 1.4f, 5f, 1.4f, Fire, SkyStrike.Look.Burst);
        StrikeAt(-4.5f, 1.4f, 5f, 1.7f, Fire, SkyStrike.Look.Burst);
        PlayAttackPose(0.8f);
        yield return Wait(1.9f);
        // 炎の壁が流れてくる: 背が高い(二段)→ 低い(跳ぶ)
        PillarAt(6.5f, 2.4f, 0.8f, 999, 4.5f, CaveLook.Fire, true);
        PillarAt(15f, 1.1f, 0.8f, 999, 4.5f, CaveLook.Fire, false);
        if (Upgraded) PillarAt(22f, 2.4f, 0.8f, 999, 4.5f, CaveLook.Fire, true);
        yield return Wait(Upgraded ? 5.6f : 4.0f);
        // 炎をまとって突進: 低い炎の波(跳ぶ)→ 身体が高い帯を横断(地面にいる)
        LaneWarn(2f, 9f, 0f, 1.2f, 0.7f, LowLaneColor);
        yield return Wait(0.7f);
        WaveFrom(OffscreenAheadGap(), 2.2f, 0f, 1.2f, 13f, CaveLook.Fire, true, 0.7f);
        yield return Wait(1.0f);
        rush.damageAmount = UltimateDamage;
        yield return FlameRush();
        rush.damageAmount = HitDmg;
        SetBodyTint(new Color(1f, 0.95f, 0.85f));
        // 隙: 炎が弱まって低く降りる
        yield return CaveRecovery(3.4f, "炎が弱まった! 攻撃のチャンス!", new Vector2(1.05f, 0.85f), CaveLook.Fire, new Vector2(0f, 0.6f), 2.2f, 1.25f, 0.4f);
    }

    protected override void SkyRestore() { rush.damageAmount = HitDmg; SetBodyTint(new Color(1f, 0.95f, 0.85f)); StartCoroutine(Ambient()); }
}

// ============ 80,000m 天空大蛇 ============
// 第2段階: 嵐を起こす(強風=移動が少し鈍るだけ。操作不能にはしない)+雷雲
// 必殺技 HEAVEN SERPENT STORM: 空が暗くなる → 雲が高速で流れる → 雲の中を影が移動 → 頭部が一度出現 → 落雷(位置をずらす)
//   → 胴体が別方向(後ろ)から横断(跳ぶ)→ 強風 → 巨大な頭部+雷ブレス(低い=跳ぶ → 高い=地面にいる)。隙: 頭が雲から低く出て疲労
public partial class SkySerpentBoss
{
    IEnumerator StormSurge()
    {
        CaveDarkness.Set(0.2f);
        StartCoroutine(StormWind(1.4f, 0.85f));
        yield return ThunderClouds();
    }

    // 強風: 移動へ少し影響するだけ(減速。押し戻しはしない)
    IEnumerator StormWind(float dur, float slow)
    {
        float t = 0f, spawn = 0f;
        Camera cam = Camera.main;
        while (t < dur && !IsDead)
        {
            t += Time.deltaTime; spawn -= Time.deltaTime;
            if (spawn <= 0f && cam != null)
            {
                spawn = 0.04f;
                Vector3 p = cam.ViewportToWorldPoint(new Vector3(1.05f, Random.Range(0.1f, 0.9f), 10f)); p.z = 0f;
                SkyDrift.Spawn(BossFx.Block(), p, new Vector2(Random.Range(1.5f, 3f), 0.05f), new Color(0.9f, 0.95f, 1f, 0.45f), new Vector3(-26f, 0f, 0f), 0.9f, RenderOrder.CombatFx, true);
            }
            if (pc != null && !pc.IsFinishing) pc.ApplyMoveSlow(slow, 0.15f);
            yield return null;
        }
    }

    IEnumerator HeavenSerpentStorm()
    {
        if (!StartUlt("HEAVEN SERPENT STORM", new Color(0.75f, 0.85f, 1f))) yield break;
        CaveDarkness.Set(0.35f);
        StartCoroutine(StormWind(0.8f, 0.92f));   // 雲が高速で流れる
        yield return Vanish(0.35f);
        // 雲の中を影が移動
        for (int i = 0; i < 4; i++) TellAt(9f - i * 2.5f, 0.8f + i * 0.2f, CaveTellStyle.Shadow, CaveLook.Wind, i == 3);
        yield return Wait(1.0f);
        // 頭部が一度出現
        worldX = PlayerX + 6f; yOffset = 1.8f; facing = -1f;
        yield return Appear(0.25f);
        Sfx(SkyBossSfx.Roar(), 1f);
        yield return Wait(0.6f);
        yield return Vanish(0.25f);
        // 落雷(時間差。最後がプレイヤーの位置)
        StrikeAt(-3.2f, 1.8f, 5.5f, 1.1f, Storm, SkyStrike.Look.Bolt);
        StrikeAt(3.2f, 1.8f, 5.5f, 1.4f, Storm, SkyStrike.Look.Bolt);
        StrikeAt(0f, 1.8f, 5.5f, 1.9f, Storm, SkyStrike.Look.Bolt);
        if (Upgraded) StrikeAt(6.2f, 1.8f, 5.5f, 2.3f, Storm, SkyStrike.Look.Bolt);
        yield return Wait(Upgraded ? 2.6f : 2.2f);
        // 胴体が後ろから横断(低い=跳ぶ)
        LaneWarn(-2f, 9f, 0f, 1.1f, 0.85f, LowLaneColor);
        yield return Wait(0.85f);
        WaveFrom(OffscreenBehindGap(), 3.4f, 0f, 1.1f, -11f, CaveLook.Wind, true, 0.68f);
        yield return Wait(1.3f);
        // 強風(移動へ影響するだけ)
        yield return StormWind(1.2f, 0.8f);
        // 巨大な頭部+雷ブレス(低い → 高い)
        worldX = PlayerX + 10f; yOffset = HoverAlt; facing = -1f;
        yield return Appear(0.3f);
        mouthGlow.enabled = true;
        StartCoroutine(FollowGlow(mouthGlow, 0.95f, 0.55f, 0.5f, 2.8f, 1.2f));
        BandAt(0f, 13f, 0f, 1.05f, 1.1f, 0.45f, CaveLook.Lightning, true);
        BandAt(0f, 13f, 2.3f, 3.8f, 1.1f, 0.45f, CaveLook.Lightning, true);
        yield return Telegraph(1.1f);
        mouthGlow.enabled = false;
        Sfx(SkyBossSfx.Thunder(), 0.9f);
        PlayAttackPose(0.6f);
        yield return Wait(1.6f);
        CaveDarkness.Set(0f);
        // 隙: 頭が雲から低く出て疲労
        yield return MoveToGap(3.5f, 12f, 1.2f);
        yield return CaveRecovery(3.6f, "頭が雲から低く出た! 攻撃のチャンス!", new Vector2(1.06f, 0.85f), CaveLook.Wind, new Vector2(0.7f, 0.5f), 2.2f, 1.25f, 0.4f);
    }

    protected override void SkyRestore()
    {
        if (mouthGlow != null) mouthGlow.enabled = false;
        SetVisualSortingOrder(RenderOrder.Boss);
        ConfigureHurtbox(new Vector2(0f, bodyHeight * 0.5f), new Vector2(halfWidth * 2f * hurtWidthRatio, bodyHeight * hurtHeightRatio));
        CaveDarkness.Set(0f);
        StartCoroutine(Ambient());
    }
}

// ============ 90,000m 天界の守護者 ============
// 第2段階: 光柱+高速の空中移動からの連続攻撃 / 第3段階: 翼/光輪が強く発光(隙が短くなる)
// 必殺技 HEAVEN'S JUDGMENT(最終試験): 上空へ → 巨大な光輪 → 前後へ光柱の予兆(時間差。最後はプレイヤーの位置=踏み込みでずらす)
//   → 光柱の間を高速移動して斬撃(低い=跳ぶ → 高い=地面にいる)→ 床一面の光(空中にいる=跳ぶ/二段)
//   → 着地の後に巨大斬撃波(背が高い=二段。下攻撃で早く降りると間に合う)。隙: 光輪が消えて降りてくる(長いBREAKのチャンス)
public partial class CelestialGuardianBoss
{
    IEnumerator GuardianCombo()
    {
        if (Enraged)
        {
            for (int i = 0; i < 3 && !IsDead; i++) yield return LightSword();
            yield break;
        }
        yield return LightPillars();
        yield return AerialStrike();
    }

    IEnumerator HeavensJudgment()
    {
        if (!StartUlt("HEAVEN'S JUDGMENT", new Color(1f, 0.93f, 0.6f))) yield break;
        yield return SetAltitude(4.5f, 0.4f);
        yield return MoveToGap(6f, 8f, 1f);
        TellAt(0f, 1.3f, CaveTellStyle.Circle, CaveLook.Holy, true, true);   // 巨大な光輪
        Glow(1.2f, Holy);
        yield return Telegraph(1.1f);
        // 前後へ光柱の予兆(時間差)
        StrikeAt(-3f, 1.6f, 6f, 1.2f, Holy, SkyStrike.Look.Pillar);
        StrikeAt(3f, 1.6f, 6f, 1.2f, Holy, SkyStrike.Look.Pillar);
        StrikeAt(0f, 1.6f, 6f, 2.0f, Holy, SkyStrike.Look.Pillar);
        if (Upgraded) { StrikeAt(-6f, 1.6f, 6f, 2.4f, Holy, SkyStrike.Look.Pillar); StrikeAt(6f, 1.6f, 6f, 2.4f, Holy, SkyStrike.Look.Pillar); }
        yield return Wait(Upgraded ? 2.7f : 2.3f);
        // 光柱の間を高速移動しながら斬撃: 低い(跳ぶ)→ 高い(地面にいる)
        LaneWarn(2f, 8f, 0f, 1.2f, 0.6f, LowLaneColor);
        yield return Wait(0.6f);
        WaveFrom(OffscreenAheadGap(), 1.8f, 0f, 1.2f, 12f, CaveLook.Holy, true, 0.72f);
        yield return Wait(1.0f);
        LaneWarn(-2f, 8f, 2.3f, 3.6f, 0.7f, HighLaneColor);
        yield return Wait(0.7f);
        WaveFrom(OffscreenBehindGap(), 1.8f, 2.3f, 3.6f, -12f, CaveLook.Holy, true, 0.72f);
        yield return Wait(1.2f);
        // 床一面の光(空中にいる)
        BossBattleHud.Banner("跳べ!", new Color(1f, 0.85f, 0.4f), 0.8f);
        FloorAt(0f, 7f, 0.9f, 1.0f, 0.7f, CaveLook.Holy, true);
        yield return Wait(1.7f + 0.9f);
        // 巨大斬撃波(背が高い=二段)
        swordGlow.enabled = true;
        StartCoroutine(FollowGlow(swordGlow, 0.1f, 1.0f, 0.6f, 2.8f, 0.9f));
        LaneWarn(2f, 9f, 0f, 2.3f, 0.9f, TallLaneColor);
        yield return Wait(0.9f);
        swordGlow.enabled = false;
        WaveFrom(Mathf.Max(4f, Gap - 1f), 2.4f, 0f, 2.3f, 10f, CaveLook.Holy, true, 0.72f);
        yield return Wait(1.6f);
        // 隙: 光輪が消えて降りてくる(長いBREAKのチャンス)
        yield return SetAltitude(HoverAlt, 0.4f);
        yield return MoveToGap(3f, 9f, 1.2f);
        yield return CaveRecovery(4.5f, "光輪が消えた! 長い反撃のチャンス!", new Vector2(1f, 0.9f), CaveLook.Holy, new Vector2(0f, 0.9f), 3f, 1.3f);
    }

    protected override void SkyRestore() { if (swordGlow != null) swordGlow.enabled = false; StartCoroutine(Ambient()); }
}
