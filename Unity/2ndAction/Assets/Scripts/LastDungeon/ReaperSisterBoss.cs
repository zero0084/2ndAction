using System.Collections;
using UnityEngine;

// ラストダンジョン 100,000m: 死神三姉妹との正式な戦闘(2026-09-30)。
// これまでプレイヤーを追ってきた三姉妹(ReaperSisterData: 絵/歩き・浮遊・スキップのテンポ)を、既存のボス戦の仕組み
// (WildBossBase: HP/被弾/予告/攻撃判定/撃破演出/プレイヤーと並走する間合い)に載せたもの。
//  長女(Eldest) … ゆっくり歩く。大鎌の横薙ぎ / 地を這う斬撃(跳んで越える) / 距離を詰める踏み込み
//  次女(Second) … 低空に浮かぶ。上空から急降下して斬る / 魂の弾(3発、上下にずれる)
//  三女(Youngest) … 楽しそうにスキップ。跳びかかり(着地に衝撃) / 前後の往復突進 / 回転斬り
// 前半(ソロ)はHPが0になると倒れず「退く」(RetreatOnLethal)。後半(三人同時)は通常どおり撃破演出→DefeatOverride。
// 移動のコマ送りは走行速度と無関係の一定テンポ(ReaperAnimatorと同じ考え方)。
public class ReaperSisterBoss : WildBossBase
{
    public ReaperSisterData data;
    public ReaperSister sister;
    public bool retreatOnLethal;              // 前半: HP0で倒れずに退く
    public System.Action<ReaperSisterBoss> Retreated;
    public bool Retreating { get; private set; }
    public float aggression = 1f;             // 後半は少し間隔を詰める(三人同時の分、個々は控えめ)

    BossHitbox scythe, dive, slam, spin;
    BossTelegraphMarker scytheMark, diveMark, slamMark, spinMark;
    bool bob = true;
    float bobT;
    float baseHover;
    Color tint = Color.white;

    public static ReaperSisterBoss Create(ReaperSister sister, Transform player, int slot, int hp, float gap)
    {
        string name = sister == ReaperSister.Eldest ? "ReaperEldest" : sister == ReaperSister.Second ? "ReaperSecond" : "ReaperYoungest";
        var data = Resources.Load<ReaperSisterData>("Reapers/" + sister + "Data"); // EldestData / SecondData / YoungestData
        ProgressStats.MarkReaperMet(sister); // 進行(2026-10-01): 遭遇の記録
        var go = new GameObject("FinalBoss_" + name);
        go.tag = "Boss";
        var b = go.AddComponent<ReaperSisterBoss>();
        b.sister = sister;
        b.data = data;
        b.bossName = data != null && !string.IsNullOrEmpty(data.displayName) ? data.displayName : name;
        b.maxHp = hp;
        b.mileReward = 800;
        b.bodyHeight = data != null ? Mathf.Max(3.8f, data.heightWorld * 1.75f) : 4.2f; // 追ってきた時より大きく(最後に真正面から向き合う相手)
        b.slotIndex = slot;
        b.slotSpacing = 3.6f;
        b.initialDelayPerSlot = 1.4f;
        b.startGap = gap;
        b.maxGap = 18f;
        b.minGap = -5f;
        b.enterSpeed = 6f;
        b.artFacesLeft = false; // 三姉妹の絵は右向き
        b.hurtWidthRatio = 0.7f;
        b.hurtHeightRatio = 0.85f;
        b.locoStyle = LocoStyle.None;
        b.defeatBurstColor = sister == ReaperSister.Eldest ? new Color(0.7f, 0.45f, 1f) : sister == ReaperSister.Second ? new Color(0.45f, 0.75f, 1f) : new Color(1f, 0.55f, 0.85f);
        var bm = BossManager.Instance;
        if (bm != null)
        {
            b.squareSprite = bm.squareSprite;
            b.hitSparkSprite = bm.bossHitSparkSprite;
            b.deathSmokeSprite = bm.bossDeathSmokeSprite;
            b.finalHitSe = AudioManager.Se(SeId.BossFinalHit, bm.bossFinalHitSe);
            b.defeatSe = AudioManager.Se(SeId.BossDefeat, bm.bossDefeatSe);
        }
        Sprite idle = data != null ? (data.idle != null ? data.idle : (data.moveFrames != null && data.moveFrames.Length > 0 ? data.moveFrames[0] : null)) : null;
        if (idle == null && bm != null) idle = bm.deathSprite;
        b.idleSprite = idle;
        b.windupSprite = idle;
        b.attackSprite = idle;
        b.Init(player);
        return b;
    }

    protected override void OnInit()
    {
        if (data != null && data.moveFrames != null && data.moveFrames.Length > 0)
        {
            loopFrames = data.moveFrames;
            loopFps = data.motion == ReaperMotion.Skip ? Mathf.Max(1f, data.skipRate * data.moveFrames.Length / 2f) : Mathf.Max(1f, data.moveFps);
            loopTimeOffset = (int)sister * 0.37f;
        }
        baseHover = sister == ReaperSister.Second ? 1.1f : 0f;
        yOffset = baseHover;
        tint = sister == ReaperSister.Eldest ? new Color(0.92f, 0.88f, 1f) : sister == ReaperSister.Second ? new Color(0.86f, 0.92f, 1f) : new Color(1f, 0.9f, 0.96f);
        SetBodyTint(tint);
        Color purple = new Color(0.8f, 0.45f, 0.95f, 0.95f);
        float h = bodyHeight;
        scythe = NewHitbox("ReaperScythe", new Vector2(-(halfWidth * 0.5f + 1.5f), h * 0.45f), new Vector2(3.2f, h * 0.8f), BossFx.Slash(), purple);
        scytheMark = NewMarker(new Vector2(-(halfWidth * 0.5f + 1.5f), h * 0.45f), new Vector2(3.2f, h * 0.8f));
        dive = NewHitbox("ReaperDive", new Vector2(-1.2f, 0.9f), new Vector2(2.6f, 2.0f), BossFx.Slash(), new Color(0.5f, 0.8f, 1f, 0.95f));
        diveMark = NewMarker(new Vector2(-1.2f, 0.9f), new Vector2(2.6f, 2.0f));
        slam = NewHitbox("ReaperSlam", new Vector2(0f, 0.5f), new Vector2(4.2f, 1.1f), BossFx.Ring(), new Color(1f, 0.6f, 0.9f, 0.95f));
        slamMark = NewMarker(new Vector2(0f, 0.5f), new Vector2(4.2f, 1.1f));
        spin = NewHitbox("ReaperSpin", new Vector2(0f, h * 0.45f), new Vector2(3.6f, h * 0.9f), BossFx.Slash(), new Color(1f, 0.5f, 0.85f, 0.95f));
        spinMark = NewMarker(new Vector2(0f, h * 0.45f), new Vector2(3.6f, h * 0.9f));
        StartCoroutine(MotionLoop());
    }

    // 移動のテンポ(浮遊の上下/スキップの跳ね/歩きの揺れ)を、攻撃中以外は常に付ける
    IEnumerator MotionLoop()
    {
        while (!IsDead)
        {
            bobT += Time.deltaTime;
            if (bob && !Retreating)
            {
                switch (sister)
                {
                    case ReaperSister.Second:
                        yOffset = baseHover + Mathf.Sin(bobT * 0.55f * Mathf.PI * 2f) * 0.18f;
                        break;
                    case ReaperSister.Youngest:
                    {
                        float rate = data != null ? data.skipRate : 2.2f;
                        float hops = bobT * rate; float ph = hops - Mathf.Floor(hops);
                        bool floaty = ((int)hops) % 4 == 3;
                        yOffset = (floaty ? Mathf.Sin(ph * Mathf.PI) * 0.7f : 4f * ph * (1f - ph) * 0.32f);
                        break;
                    }
                    default:
                        yOffset = Mathf.Abs(Mathf.Sin(bobT * 2.5f * Mathf.PI * 0.5f)) * 0.05f;
                        break;
                }
            }
            yield return null;
        }
    }

    protected override IEnumerator Enter()
    {
        // 画面右外から、それぞれの歩き方のまま間合いへ(走り込まない。「必死さの無い」近づき方)
        SetPose(Pose.Move);
        float safety = 0f;
        while (Gap > startGap + slotOffset && safety < 7f)
        {
            relVelocity = -enterSpeed;
            safety += Time.deltaTime;
            yield return null;
        }
        relVelocity = 0f;
    }

    protected override IEnumerator AI()
    {
        yield return Wait(0.6f);
        int n = 0;
        while (!IsDead)
        {
            if (Retreating) { yield return null; continue; }
            n++;
            switch (sister)
            {
                case ReaperSister.Eldest:
                    if (n % 3 == 0) yield return ReapingWave();
                    else if (n % 3 == 1) yield return ScytheSweep(0.75f);
                    else yield return StepAndSweep();
                    break;
                case ReaperSister.Second:
                    if (n % 2 == 0) yield return SoulOrbs();
                    else yield return DiveSlash();
                    break;
                default:
                    if (n % 3 == 0) yield return SkipRush();
                    else if (n % 3 == 1) yield return HopSlam();
                    else yield return SpinSlash();
                    break;
            }
            yield return Wait(Mathf.Lerp(1.6f, 1.0f, Mathf.Clamp01(aggression - 1f)) * (slotIndex > 0 ? 1.2f : 1f));
        }
    }

    // ---- 長女 ----
    IEnumerator ScytheSweep(float windup)
    {
        yield return MoveToGap(3.4f, 3.5f, 3f);
        yield return Telegraph(windup, scytheMark);
        yield return Strike(scythe, 0.26f, 0.08f);
        yield return Recover(0.7f);
    }

    IEnumerator StepAndSweep()
    {
        yield return MoveToGap(6.5f, 4f, 2.5f);
        yield return Telegraph(0.6f, scytheMark);
        yield return DashMove(0.25f, 12f); // 一歩で間合いを詰める
        yield return Strike(scythe, 0.24f, 0.1f);
        yield return Recover(0.8f);
    }

    // 地を這う斬撃: 地面に沿って進む紫の刃。跳んで越える(上攻撃/空中でも越えられる高さ)
    IEnumerator ReapingWave()
    {
        yield return MoveToGap(8.5f, 4f, 2.5f);
        yield return Telegraph(0.7f);
        PlayAttackPose(0.4f);
        Shake(0.06f, 0.15f);
        var p = BossProjectile.Create(BossFx.Slash(), new Color(0.75f, 0.4f, 1f, 0.95f), FrontWorld(0.3f, 0.55f), new Vector2(1.3f, 1.0f), new Vector2(facing * 7.5f, 0f), 3.2f, RenderOrder.CombatFx);
        p.hugGround = true; p.groundOffset = 0.5f;
        yield return Recover(0.9f);
    }

    // ---- 次女 ----
    IEnumerator DiveSlash()
    {
        bob = false;
        yield return SetAltitude(3.6f, 0.5f);
        yield return MoveToGap(3.0f, 5f, 2.5f);
        yield return Telegraph(0.6f, diveMark);
        StartCoroutine(SetAltitude(0.2f, 0.28f));
        yield return DashMove(0.2f, 6f);
        yield return Strike(dive, 0.24f, 0.1f);
        yield return Recover(0.5f);
        yield return SetAltitude(baseHover, 0.5f);
        bob = true;
        yield return Wait(0.3f);
    }

    IEnumerator SoulOrbs()
    {
        yield return MoveToGap(9f, 4f, 2.5f);
        yield return Telegraph(0.55f);
        for (int i = 0; i < 3 && !IsDead; i++)
        {
            PlayAttackPose(0.25f);
            Vector3 from = FrontWorld(0.2f, bodyHeight * 0.55f);
            Vector2 aim = AimFrom(from);
            aim = Quaternion.Euler(0f, 0f, (i - 1) * 9f) * aim;
            BossProjectile.Create(BossFx.Orb(), new Color(0.55f, 0.85f, 1f, 0.95f), from, new Vector2(0.7f, 0.7f), aim * 6.5f, 3f, RenderOrder.CombatFx);
            yield return Wait(0.32f);
        }
        yield return Recover(0.8f);
    }

    // ---- 三女 ----
    IEnumerator HopSlam()
    {
        bob = false;
        yield return MoveToGap(7f, 4f, 2f);
        yield return Telegraph(0.5f, slamMark);
        float gd = Mathf.Clamp(-(Gap - 1.0f - slotOffset), -9f, 0f);
        yield return Leap(0.62f, 3.2f, gd);
        SetPose(Pose.Landing);
        Shake(0.1f, 0.18f);
        ImpactDust(new Vector3(worldX, GroundY + 0.1f, 0f), 8, 1f);
        yield return Strike(slam, 0.2f, 0.1f);
        yield return Recover(0.6f);
        bob = true;
    }

    IEnumerator SkipRush()
    {
        yield return MoveToGap(8f, 4f, 2f);
        yield return Telegraph(0.5f, spinMark);
        yield return DashMove(0.35f, 16f);
        yield return Strike(spin, 0.22f, 0.08f);
        yield return Retreat(8f, 0.45f);
        yield return Recover(0.6f);
    }

    IEnumerator SpinSlash()
    {
        yield return MoveToGap(3.2f, 4.5f, 2.5f);
        yield return Telegraph(0.55f, spinMark);
        yield return Strike(spin, 0.3f, 0.08f);
        yield return Recover(0.7f);
    }

    // ---- HP0の扱い ----
    protected override bool OnLethalDamage()
    {
        if (!retreatOnLethal) return false;
        if (!Retreating) StartCoroutine(RetreatRoutine());
        return true;
    }

    IEnumerator RetreatRoutine()
    {
        Retreating = true;
        invulnerable = true;
        DisableCombatParts();
        SetHurtboxEnabled(false);
        Shake(0.12f, 0.2f);
        var cam = Camera.main;
        OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), CenterWorld, defeatBurstColor, 14, 0.6f, 0.3f, 0.9f, 5f, 1.5f, RenderOrder.CombatFx);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossHit);
        // 膝をつく → 後ろへ退きながら薄れて消える(倒しきれない=まだ終わりではない)
        float t = 0f;
        while (t < 1.4f)
        {
            t += Time.deltaTime;
            relVelocity = 7f; // 画面右(奥)へ離れていく
            SetAlpha(Mathf.Lerp(1f, 0f, t / 1.4f));
            yield return null;
        }
        relVelocity = 0f;
        Retreated?.Invoke(this);
        gameObject.SetActive(false);
    }
}
