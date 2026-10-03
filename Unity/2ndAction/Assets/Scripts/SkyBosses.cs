using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 天空回廊ボス追加(2026-09-25) - 荒野街道(WildBosses.cs)・自然洞窟(CaveBosses.cs)と
// 全く同じ基盤(WildBossBase: Root/Visual(BossRig)/Hurtbox/Hitbox、Approach/Telegraph/
// Strike/Recover、複数体のslotIndex/BossAttackGate、HP/被弾/撃破演出、走行との相対移動)
// をそのまま使い、天空回廊の10,000m〜90,000mの専用大型ボス9体を追加する。
// 1,000m(ドラゴン)/5,000m(魔人)は既存のDragonController/MajinControllerをそのまま使う
// (BossManager.ResolveSkyGate参照)。100,000mの死神は既存のSpawnDeath(ステージ非依存)。
//
// 天空回廊のコンセプト: 「空・雲海・画面奥・上下方向から戦闘空間へ侵入してくる敵」。
//  - 登場は「遠方のシルエット(背景レイヤー、小さく霞んだ色)→ 高速接近 → 手前レイヤーへ」
//    (SkyBossBase.DistantApproach / SkyFlyby)。最初から画面内に棒立ちでは出さない。
//  - 雲海を並走する超大型(タイタン/リヴァイアサン)は地面より奥のレイヤーに描き、
//    「見えている部分=被弾範囲」になるようHurtboxを差し替える(ConfigureHurtbox)。
//  - 攻撃は必ず予告(赤い予告ゾーン/地面の光る楕円/空中の帯)→ 見えている攻撃と同範囲の判定。
//  - 回避の考え方(既存ボスと同じ): 地面沿い/低い帯(LowBand)=ジャンプ、高い帯(HighBand)=
//    跳ばずに走り続ける、地面の着弾点=攻撃の踏み込み/後退で位置をずらす。
public enum SkyBossKind { Dragon, Majin, Behemoth, Titan, Jellyfish, Leviathan, Fenrir, SkyGolem, Phoenix, SkySerpent, Guardian }

public abstract class SkyBossBase : WildBossBase
{
    protected const int FarLayer = -40;       // 空の背景(-100)より手前、回廊(0)より奥
    protected const int BehindGround = -5;    // 回廊の足場より奥(雲海の中)
    // 地面からの高さの帯。Low=ジャンプで越えられる、High=地上にいれば当たらない。
    protected static readonly Vector2 LowBand = new Vector2(0f, 1.25f);
    protected static readonly Vector2 HighBand = new Vector2(1.9f, 4.4f);

    protected bool approachDone;
    float airStepTimer;

    protected Sprite[] BodyFrames => moveSprite != null ? new[] { idleSprite, moveSprite } : new[] { idleSprite };

    protected void Sfx(AudioClip clip, float volume = 0.8f) => SkyBossSfx.Play(clip, volume);

    // 身体上の位置(fwd: 正面方向への半幅比、h: 身体の高さ比)のワールド座標。
    protected Vector3 BodyPoint(float fwd, float h)
    {
        return new Vector3(worldX + facing * halfWidth * fwd, GroundY + yOffset + bodyHeight * h, 0f);
    }

    // プレイヤーの足場の高さ(空中の帯の基準)。
    protected float PlayerGroundY
    {
        get
        {
            if (TerrainManager.Instance != null)
            {
                float? h = TerrainManager.Instance.GetHeightAt(PlayerX);
                if (h.HasValue) return h.Value;
            }
            return PlayerY;
        }
    }

    protected SpriteRenderer MakeGlow(string name, Color color, int order = RenderOrder.Boss + 1)
    {
        var g = new GameObject(name);
        g.transform.SetParent(transform, false);
        var s = g.AddComponent<SpriteRenderer>();
        s.sprite = BossFx.Orb();
        s.color = color;
        s.sortingOrder = order;
        s.enabled = false;
        return s;
    }

    // glowを身体上の点に追従させる(有効な間)。sizeFromTo: 溜めに合わせて大きくなる。
    protected IEnumerator FollowGlow(SpriteRenderer g, float fwd, float h, float sizeFrom, float sizeTo, float grow)
    {
        float t = 0f;
        while (g != null && g.enabled && !IsDead)
        {
            t += Time.deltaTime;
            float s = Mathf.Lerp(sizeFrom, sizeTo, Mathf.Clamp01(t / Mathf.Max(0.01f, grow))) * (1f + 0.12f * Mathf.Sin(Time.time * 30f));
            g.transform.position = BodyPoint(fwd, h);
            g.transform.localScale = new Vector3(s / Mathf.Max(0.01f, transform.lossyScale.x), s / Mathf.Max(0.01f, transform.lossyScale.y), 1f);
            yield return null;
        }
    }

    // 穴の上を走る時は、魔力/雷の足場が一瞬光る(地上ボスが空中を走って見えないように)。
    protected void AirStep(Color color)
    {
        if (yOffset > 0.4f || TerrainManager.Instance == null) return;
        if (TerrainManager.Instance.GetHeightAt(worldX) != null) return;
        airStepTimer -= Time.deltaTime;
        if (airStepTimer > 0f) return;
        airStepTimer = 0.14f;
        OneShotSpriteEffect.CreateTweened(SkyBossFx.GroundGlow(), new Vector3(worldX - facing * halfWidth * Random.Range(-0.3f, 0.5f), GroundY + 0.05f, 0f),
            color, 0.35f, 1.2f, 2.0f, 0.9f, 0f, default, 0f, RenderOrder.Boss - 1, 0.1f);
    }

    // 遠方のシルエット(背景レイヤー・小さく霞んだ色)→ 加速しながら接近 → 手前レイヤーで実体化。
    // 本体はこの間透明・無敵・Hurtbox無効(実体化の瞬間から戦闘に参加)。
    protected IEnumerator DistantApproach(float duration, float targetGap, float targetAlt, Vector2 startViewport, float startScaleMul, Color farTint, int finalOrder = RenderOrder.Boss)
    {
        invulnerable = true;
        SetHurtboxEnabled(false);
        SetAlpha(0f);
        SetPose(Pose.Move);
        yOffset = targetAlt;

        var go = new GameObject(bossName + "_Silhouette");
        var s = go.AddComponent<SpriteRenderer>();
        s.sortingOrder = FarLayer;
        Sprite[] frames = BodyFrames;
        float artSign = artFacesLeft ? 1f : -1f; // プレイヤー(左)を向く
        float t = 0f;
        Vector3 from = Vector3.zero;
        bool haveFrom = false;
        while (t < duration && !IsDead)
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / duration);
            worldX = PlayerX + targetGap;
            relVelocity = 0f;
            Camera cam = Camera.main;
            Vector3 to = new Vector3(worldX, GroundY + yOffset, 0f);
            if (cam != null)
            {
                from = cam.ViewportToWorldPoint(new Vector3(startViewport.x, startViewport.y, 10f));
                from.z = 0f;
                if (pc != null) from.x += PlayerX - pc.transform.position.x; // マルチ: 狙いの相手の画面から(自分が狙いなら0)
                haveFrom = true;
            }
            else if (!haveFrom) from = to + new Vector3(10f, 5f, 0f);

            float e = f * f * (3f - 2f * f);
            float ep = f * f; // 位置は加速しながら迫る
            Vector3 p = Vector3.Lerp(from, to, ep);
            p.y += Mathf.Sin(Time.time * 4f) * 0.12f * (1f - f);
            go.transform.position = p;
            float sc = Mathf.Lerp(startScaleMul, 1f, e) * scaleFactor;
            go.transform.localScale = new Vector3(sc * artSign, sc, 1f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 6f) * 2.5f);
            s.sprite = frames[(int)(Time.time * 7f) % frames.Length];

            Color c = Color.Lerp(farTint, Color.white, Mathf.InverseLerp(0.55f, 1f, f));
            c.a = Mathf.Clamp01(f * 5f) * Mathf.Lerp(farTint.a, 1f, Mathf.InverseLerp(0.55f, 1f, f));
            s.color = c;
            if (f > 0.72f) s.sortingOrder = finalOrder; // 手前のレイヤーへ
            yield return null;
        }
        Destroy(go);
        SetAlpha(1f);
        SetHurtboxEnabled(true);
        invulnerable = false;
        Shake(0.08f, 0.2f);
    }

    // 身体(hb)で画面を横断する攻撃。fromGapからtoGapへ一定速度で移動し、その間だけ判定。
    protected IEnumerator CrossBody(BossHitbox hb, float fromGap, float toGap, float speed)
    {
        worldX = PlayerX + fromGap;
        facingLocked = true;
        facing = Mathf.Sign(toGap - fromGap);
        float dist = Mathf.Abs(toGap - fromGap);
        float dur = dist / Mathf.Max(1f, speed);
        SetPose(Pose.Attack);
        attackProgress = 1f;
        StartCoroutine(hb.Strike(facing, dur));
        float t = 0f;
        while (t < dur && !IsDead)
        {
            t += Time.deltaTime;
            relVelocity = facing * speed;
            yield return null;
        }
        relVelocity = 0f;
        hb.Deactivate();
        attackProgress = 0f;
    }

}

// 共有Cooldown(複数体/連続使用で弾幕化しないため)。
static class SkyProjectileGate { public static float NextTime; public static float Interval = 1.6f; }

// ============ 10,000m 雷獣ベヒーモス ============
// 遠方の落雷 → 雲の向こうを疾走する巨大シルエット → 後方から高速疾走で追いつき前へ。
// 突進(身体を低く→角に雷→溜め→突進)/落雷(咆哮→前方に落雷予告→落雷)/雷撃衝撃波(前脚→叩きつけ)。
public class BehemothBoss : SkyBossBase
{
    BossHitbox charge, stomp;
    BossTelegraphMarker chargeMark, stompMark;
    SpriteRenderer hornGlow;
    int last = -1;
    static readonly Color Bolt = new Color(0.6f, 0.85f, 1f, 1f);

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Gallop;
        footstepShake = true;
        enterSpeed = 13f;
        hitStopOnHit = 0.04f;
        windupMoveFactor = 0.35f;
        minGap = -12f; maxGap = 18f;
        Vector2 cc = new Vector2(FrontReach * 0.55f, 0.62f), cs = new Vector2(halfWidth * 1.3f, 1.25f);
        charge = NewHitbox("Charge", cc, cs, BossFx.Slash(), new Color(0.6f, 0.85f, 1f, 0.9f));
        chargeMark = NewMarker(new Vector2(FrontReach + 4.5f, 0.62f), new Vector2(9f, 1.25f));
        Vector2 sc = new Vector2(FrontReach + 1.0f, 0.5f), ss = new Vector2(3.0f, 1.0f);
        stomp = NewHitbox("Stomp", sc, ss, BossFx.Ring(), new Color(0.7f, 0.9f, 1f, 0.95f));
        stompMark = NewMarker(sc, ss);
        hornGlow = MakeGlow("HornGlow", new Color(0.55f, 0.85f, 1f, 0.95f));
        SetAlpha(0f);
        StartCoroutine(Ambient());
    }

    protected override IEnumerator Enter()
    {
        invulnerable = true;
        SetHurtboxEnabled(false);
        SetAlpha(0f);
        SkyBossFx.FarLightning(new Vector2(0.84f, 0.42f));
        yield return Wait(0.35f);
        SkyBossFx.FarLightning(new Vector2(0.7f, 0.4f));
        // 雲の向こう(背景)を右奥から左奥へ疾走していく大型シルエット
        SkyFlyby.Create(BodyFrames, new Vector2(1.15f, 0.6f), new Vector2(-0.2f, 0.46f), scaleFactor * 0.3f, scaleFactor * 0.55f, 1.5f, new Color(0.3f, 0.38f, 0.58f, 0.75f), !artFacesLeft);
        yield return Wait(1.35f);
        SkyBossFx.FarLightning(new Vector2(0.08f, 0.4f));

        // 後方から高速疾走で追いつき、追い越して前へ出てから振り返る
        worldX = PlayerX - 15f;
        yOffset = 0f;
        SetAlpha(1f);
        SetPose(Pose.Move);
        Sfx(SkyBossSfx.Roar(), 0.7f);
        float t = 0f;
        while (Gap < startGap && t < 5f)
        {
            relVelocity = 14f;
            t += Time.deltaTime;
            yield return null;
        }
        relVelocity = 0f;
        invulnerable = false;
        SetHurtboxEnabled(true);
        approachDone = true;
    }

    IEnumerator Ambient()
    {
        float spark = 0f;
        while (!IsDead)
        {
            spark -= Time.deltaTime;
            if (spark <= 0f && approachDone)
            {
                spark = Random.Range(0.15f, 0.4f);
                Vector3 p = CenterWorld + new Vector3(Random.Range(-halfWidth, halfWidth) * 0.8f, Random.Range(-0.4f, 0.45f) * bodyHeight, 0f);
                SkyBossFx.Sparks(p, Bolt, 3, 0.35f);
                if (Random.value < 0.25f) SkyDrift.Spawn(SkyBossFx.Bolt(), p, new Vector2(0.5f, 1.2f), Bolt, Vector3.zero, 0.12f, RenderOrder.Boss + 1, true, Random.Range(-60f, 60f));
            }
            // 一歩ごとの小さな雷(疾走中)
            if (approachDone && (pose == Pose.Move || pose == Pose.Idle) && Random.value < Time.deltaTime * 3f)
            {
                SkyBossFx.Sparks(new Vector3(worldX - facing * halfWidth * Random.Range(-0.4f, 0.6f), GroundY + 0.1f, 0f), Bolt, 4, 0.4f);
            }
            AirStep(new Color(0.6f, 0.85f, 1f, 0.8f));
            yield return null;
        }
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            int pick = BossAiUtil.PickNoRepeat(3, ref last);
            if (pick == 0) yield return ChargeAttack();
            else if (pick == 1) yield return ThunderCall();
            else yield return ShockStomp();
        }
    }

    IEnumerator ChargeAttack()
    {
        yield return MoveToGap(9f, 5f, 2.5f);
        hornGlow.enabled = true;
        StartCoroutine(FollowGlow(hornGlow, 0.9f, 0.55f, 0.4f, 1.6f, 1.1f));
        yield return Telegraph(1.1f, chargeMark);
        Sfx(SkyBossSfx.Whoosh(), 0.8f);
        StartCoroutine(DashMove(0.8f, 16f));
        yield return Strike(charge, 0.8f, 0.08f);
        hornGlow.enabled = false;
        yield return Recover(0.9f);
        yield return MoveToGap(7f, 6f, 3f);
    }

    IEnumerator ThunderCall()
    {
        yield return Telegraph(0.9f);
        Sfx(SkyBossSfx.Roar(), 0.9f);
        Shake(0.08f, 0.35f);
        OneShotSpriteEffect.CreateTweened(BossFx.Ring(), BodyPoint(0.8f, 0.7f), new Color(0.7f, 0.9f, 1f, 0.8f), 0.4f, 1f, 3.5f, 0.9f, 0f, default, 0f, RenderOrder.CombatFx, 0.1f);
        PlayAttackPose(0.6f);
        float baseX = PlayerX;
        SkyStrike.Create(baseX + Random.Range(-0.4f, 0.4f), 1.6f, 7f, 1.1f, 0.25f, Bolt, SkyStrike.Look.Bolt);
        SkyStrike.Create(baseX + 3.8f, 1.6f, 7f, 1.45f, 0.25f, Bolt, SkyStrike.Look.Bolt);
        if (Hp <= maxHp / 2) SkyStrike.Create(baseX - 3.8f, 1.6f, 7f, 1.8f, 0.25f, Bolt, SkyStrike.Look.Bolt);
        yield return Wait(0.6f);
        yield return Recover(1.2f);
    }

    IEnumerator ShockStomp()
    {
        yield return Approach(3.0f, 3f, 4f);
        yield return Telegraph(1.0f, stompMark);
        var w = BossProjectile.Create(BossFx.Ring(), new Color(0.65f, 0.88f, 1f, 0.95f), FrontWorld(1.2f, 0.6f), new Vector2(1.3f, 1.2f), new Vector2(facing * 9f, 0f), 2.6f, RenderOrder.Boss + 1);
        w.hugGround = true;
        w.groundOffset = 0.6f;
        SkyBossFx.Sparks(FrontWorld(0.8f, 0.1f), Bolt, 10, 0.6f);
        Sfx(SkyBossSfx.Thunder(), 0.6f);
        ImpactDust(FrontWorld(0.8f, 0f), 12, 1.2f);
        yield return Strike(stomp, 0.22f, 0.14f, 0.06f);
        yield return Recover(1.1f);
    }
}

// ============ 20,000m 天空タイタン ============
// 雲海の中を回廊と並行して進む神話級巨人。地面より奥のレイヤーに上半身だけが見える。
// 拳(腕を振り上げ→攻撃地点に影→叩きつけ)/暴風(息を吸う→口元に風→横風で移動妨害、ダメージなし)/
// 雷槍(手に雷→雷槍形成→投擲、着弾点を事前表示)。
public class SkyTitanBoss : SkyBossBase
{
    SpriteRenderer mouthGlow, handGlow;
    int last = -1;
    float RiseY => -bodyHeight * 0.55f;

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Stride;
        suppressLocoDust = true;
        hitStopOnHit = 0.05f;
        windupMoveFactor = 0.3f;
        startGap = 9f; minGap = 5f; maxGap = 14f;
        SetVisualSortingOrder(BehindGround);
        mouthGlow = MakeGlow("MouthGlow", new Color(0.85f, 0.95f, 1f, 0.8f));
        handGlow = MakeGlow("HandGlow", new Color(0.6f, 0.85f, 1f, 0.95f));
        // 被弾範囲: 回廊の高さから届く胸〜肩(ジャンプで届く範囲)
        hurtWidth = Mathf.Max(3f, halfWidth * 0.9f);
        ConfigureHurtbox(new Vector2(0f, -RiseY + 2.8f), new Vector2(hurtWidth, 4.4f));
        SetAlpha(0f);
        StartCoroutine(Ambient());
    }

    protected override IEnumerator Enter()
    {
        // 背景の雲の中に巨大な影 → 迫ってくる → 雲海の中から巨体がせり上がる
        yield return DistantApproach(2.6f, startGap, -bodyHeight * 0.85f, new Vector2(0.95f, 0.22f), 0.45f, new Color(0.26f, 0.32f, 0.46f, 0.6f), BehindGround);
        SetVisualSortingOrder(BehindGround);
        Sfx(SkyBossSfx.Roar(), 1f);
        Shake(0.15f, 0.9f);
        yield return SetAltitude(RiseY, 1.1f);
        approachDone = true;
    }

    IEnumerator Ambient()
    {
        float cloud = 0f;
        while (!IsDead)
        {
            SetHpBarOffset(6.4f - yOffset);
            cloud -= Time.deltaTime;
            if (cloud <= 0f)
            {
                cloud = 0.12f;
                // 身体の周りに渦巻く雲(雲海を掻き分けて進む)
                Vector3 p = new Vector3(worldX + Random.Range(-halfWidth, halfWidth), GroundY - Random.Range(0.3f, 2.2f), 0f);
                SkyDrift.Spawn(SkyBossFx.CloudPuff(), p, Vector2.one * Random.Range(1.2f, 2.4f), new Color(0.92f, 0.95f, 1f, 0.55f), new Vector3(-2.5f, 0.3f, 0f), 1.2f, BehindGround + 1, true);
            }
            yield return null;
        }
    }

    // 近接キャラの反撃の時間(2026-10-03)。以前は常にプレイヤーの5〜14m前に立ち、被弾範囲(胸〜肩)の手前の端も
    // 数m先までしか来ないため、近接キャラ(お嬢様騎士/格闘/忍者/竜人)は300秒で1発も当てられなかった。
    // kneelEveryAttacks 回の攻撃ごとに、身をかがめて回廊のすぐ前まで寄り、拳を振り下ろした後
    // kneelHoldSeconds 秒そのまま低い姿勢でとどまる(被弾範囲の手前の端がプレイヤーの kneelReachGap m先)。HPは変えていない。
    public int kneelEveryAttacks = 2;
    public float kneelReachGap = 0.8f;
    public float kneelLower = 1.0f;
    public float kneelHoldSeconds = 3.2f;
    int attacksSinceKneel;
    float hurtWidth;
    public int KneelCount { get; private set; } // 確認用
    public bool IsKneeling { get; private set; }

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (kneelEveryAttacks > 0 && attacksSinceKneel >= kneelEveryAttacks)
            {
                attacksSinceKneel = 0;
                yield return KneelAndSlam();
                continue;
            }
            int pick = BossAiUtil.PickNoRepeat(3, ref last);
            if (pick == 0) yield return FistSlam();
            else if (pick == 1) yield return Gust();
            else yield return ThunderSpear();
            attacksSinceKneel++;
        }
    }

    IEnumerator KneelAndSlam()
    {
        KneelCount++;
        float closeGap = kneelReachGap + Mathf.Max(3f, hurtWidth) * 0.5f;
        float keepMin = minGap;
        minGap = Mathf.Min(minGap, closeGap - 0.5f);
        IsKneeling = true;
        try
        {
            // 身をかがめながら回廊のすぐ前へ
            StartCoroutine(SetAltitude(RiseY - kneelLower, 0.9f));
            yield return MoveToGap(closeGap, 9f, 1.6f);
            // 目の前の回廊へ拳を振り下ろす(予告あり)
            float x = PlayerX + Random.Range(0.4f, 1.6f);
            SkyStrike.Create(x, 2.6f, 2.2f, 1.6f, 0.3f, new Color(0.88f, 0.84f, 0.8f, 1f), SkyStrike.Look.Fist);
            yield return Telegraph(0.9f);
            PlayAttackPose(0.9f);
            yield return Wait(0.8f);
            // 拳を回廊に突いたまま、低い姿勢で隙を見せる(近接キャラの反撃の時間)
            yield return Recover(kneelHoldSeconds);
        }
        finally
        {
            IsKneeling = false;
            minGap = keepMin;
        }
        StartCoroutine(SetAltitude(RiseY, 0.9f));
        yield return MoveToGap(startGap, 6f, 2f);
    }

    IEnumerator FistSlam()
    {
        float x = PlayerX + Random.Range(-0.3f, 2.2f);
        var strike = SkyStrike.Create(x, 2.8f, 2.4f, 2.0f, 0.3f, new Color(0.88f, 0.84f, 0.8f, 1f), SkyStrike.Look.Fist);
        yield return Telegraph(1.1f);   // 巨大な腕を振り上げる
        PlayAttackPose(1.1f);
        yield return Wait(1.0f);
        yield return Recover(1.4f);
    }

    IEnumerator Gust()
    {
        mouthGlow.enabled = true;
        StartCoroutine(FollowGlow(mouthGlow, 0.35f, 0.86f, 0.5f, 2.2f, 1.3f));
        // 吸い込み: 周囲の雲が口元へ集まる
        StartCoroutine(Inhale(1.3f));
        yield return Telegraph(1.3f);
        mouthGlow.enabled = false;
        Sfx(SkyBossSfx.Wind(), 1f);
        PlayAttackPose(1.8f);
        yield return WindGust(1.8f);
        yield return Recover(1.2f);
    }

    IEnumerator Inhale(float dur)
    {
        float t = 0f;
        while (t < dur && !IsDead)
        {
            t += Time.deltaTime;
            if (Random.value < Time.deltaTime * 14f)
            {
                Vector3 mouth = BodyPoint(0.35f, 0.86f);
                Vector3 p = mouth + new Vector3(facing * Random.Range(3f, 7f), Random.Range(-2.5f, 2.5f), 0f);
                SkyDrift.Spawn(SkyBossFx.CloudPuff(), p, Vector2.one * Random.Range(0.6f, 1.1f), new Color(1f, 1f, 1f, 0.5f), (mouth - p) / 0.6f, 0.6f, RenderOrder.CombatFx, true);
            }
            yield return null;
        }
    }

    // 強烈な横風: ダメージは与えず、前進を弱めて少し押し戻す(即死させない)。
    IEnumerator WindGust(float dur)
    {
        float t = 0f, spawn = 0f;
        bool pushed = false;
        Camera cam = Camera.main;
        while (t < dur && !IsDead)
        {
            t += Time.deltaTime;
            spawn -= Time.deltaTime;
            if (spawn <= 0f && cam != null)
            {
                spawn = 0.03f;
                Vector3 p = cam.ViewportToWorldPoint(new Vector3(1.05f, Random.Range(0.1f, 0.9f), 10f)); p.z = 0f;
                SkyDrift.Spawn(BossFx.Block(), p, new Vector2(Random.Range(1.5f, 3.5f), 0.05f), new Color(1f, 1f, 1f, 0.5f), new Vector3(-30f, 0f, 0f), 0.9f, RenderOrder.CombatFx, true);
            }
            if (pc != null && !pc.IsFinishing)
            {
                pc.ApplyMoveSlow(0.62f, 0.15f);
                if (!pushed) { pc.ApplyKnockback(-3f, 0.35f); pushed = true; }
            }
            yield return null;
        }
    }

    IEnumerator ThunderSpear()
    {
        float x = PlayerX + Random.Range(-0.8f, 1.6f);
        // 着弾位置は溜めの開始時点から表示する(雷槍が飛んでくるまで約1.8秒)
        var strike = SkyStrike.Create(x, 2.2f, 3.2f, 1.85f, 0.3f, new Color(0.7f, 0.9f, 1f, 1f), SkyStrike.Look.Burst);
        handGlow.enabled = true;
        StartCoroutine(FollowGlow(handGlow, 0.55f, 0.78f, 0.4f, 2.4f, 1.1f));
        yield return Telegraph(1.1f);
        handGlow.enabled = false;
        StartCoroutine(SpearFlight(BodyPoint(0.55f, 0.78f), strike, 0.72f));
        Sfx(SkyBossSfx.Whoosh(), 0.8f);
        PlayAttackPose(0.6f);
        yield return Wait(0.8f);
        yield return Recover(1.3f);
    }

    IEnumerator SpearFlight(Vector3 from, SkyStrike strike, float flight)
    {
        var go = new GameObject("ThunderSpear");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = SkyBossFx.Spear();
        sr.color = new Color(0.75f, 0.92f, 1f, 1f);
        sr.sortingOrder = RenderOrder.CombatFx;
        float t = 0f;
        while (t < flight && strike != null)
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / flight);
            Vector3 to = new Vector3(strike.WorldX, strike.GroundY + 0.5f, 0f);
            float baseSpeed = TargetBaseSpeed();
            from.x += baseSpeed * Time.deltaTime;
            Vector3 p = Vector3.Lerp(from, to, f * f);
            Vector3 dir = (to - from).normalized;
            go.transform.position = p;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            go.transform.localScale = new Vector3(3.2f, 1.2f, 1f);
            if (Random.value < 0.5f) SkyBossFx.Sparks(p, new Color(0.7f, 0.9f, 1f, 1f), 1, 0.3f);
            yield return null;
        }
        Destroy(go);
    }
}

// ============ 30,000m 天空クラゲ ============
// 透明感のある身体が脈動しながら雲の中を漂う。横滑りさせず、収縮/触手の揺れ/発光で動きを見せる。
// 触手(後方へ引く→伸ばす)/電撃(内部発光→触手に電気→前方へ)/雷球(中央へ光→遅い雷球を少数)。
public class SkyJellyfishBoss : SkyBossBase
{
    BossHitbox tentacle, shock;
    BossTelegraphMarker tentacleMark, shockMark;
    SpriteRenderer coreGlow;
    int last = -1;
    const float HoverAlt = 1.5f;
    static readonly Color Pale = new Color(0.72f, 0.92f, 1f, 1f);

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Cloth;
        hitStopOnHit = 0.03f;
        startGap = 8f; minGap = 1.5f; maxGap = 14f;
        Vector2 tc = new Vector2(FrontReach + 2.1f, 0.65f - HoverAlt), ts = new Vector2(4.4f, 1.1f);
        tentacle = NewHitbox("Tentacle", tc, ts, SkyBossFx.Tentacle(), new Color(0.75f, 0.9f, 1f, 0.95f));
        tentacleMark = NewMarker(tc, ts);
        Vector2 sc = new Vector2(FrontReach + 1.6f, 1.8f - HoverAlt), ss = new Vector2(3.4f, 3.2f);
        shock = NewHitbox("Shock", sc, ss, SkyBossFx.Bolt(), new Color(0.75f, 0.95f, 1f, 0.95f));
        shockMark = NewMarker(sc, ss);
        coreGlow = MakeGlow("CoreGlow", new Color(0.6f, 0.95f, 1f, 0.85f));
        SetAlpha(0f);
        StartCoroutine(Ambient());
    }

    protected override IEnumerator Enter()
    {
        yield return DistantApproach(2.0f, startGap, HoverAlt, new Vector2(0.9f, 0.82f), 0.3f, new Color(0.65f, 0.8f, 1f, 0.5f));
        SetAlpha(0.88f);
        approachDone = true;
    }

    IEnumerator Ambient()
    {
        float mote = 0f;
        while (!IsDead)
        {
            float s = Mathf.Sin(Time.time * 2.2f);
            extraScale = new Vector2(1f - 0.06f * s, 1f + 0.09f * s);   // 傘の収縮
            float glow = 0.5f + 0.5f * Mathf.Sin(Time.time * 3.1f);
            SetBodyTint(Color.Lerp(Color.white, new Color(0.7f, 0.95f, 1f), glow * 0.6f));
            if (approachDone && !windingUp) yOffset = HoverAlt + 0.25f * Mathf.Sin(Time.time * 1.3f);
            mote -= Time.deltaTime;
            if (mote <= 0f && approachDone)
            {
                mote = 0.12f;
                Vector3 p = CenterWorld + new Vector3(Random.Range(-halfWidth, halfWidth) * 0.7f, Random.Range(-0.5f, 0.3f) * bodyHeight, 0f);
                SkyDrift.Spawn(BossFx.Orb(), p, Vector2.one * Random.Range(0.12f, 0.25f), new Color(0.7f, 0.95f, 1f, 0.8f), new Vector3(0f, 0.6f, 0f), 1.0f, RenderOrder.Boss + 1, true);
            }
            yield return null;
        }
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            int pick = BossAiUtil.PickNoRepeat(3, ref last);
            if (pick == 2 && Time.time < SkyProjectileGate.NextTime) pick = 0;
            if (pick == 0) yield return TentacleLash();
            else if (pick == 1) yield return ElectricShock();
            else yield return LightningOrbs();
        }
    }

    IEnumerator TentacleLash()
    {
        yield return Approach(2.2f, 2.6f, 5f);
        yield return Telegraph(0.8f, tentacleMark);   // 触手を後方へ引く
        yield return Strike(tentacle, 0.35f, 0.04f);
        yield return Recover(0.8f);
    }

    IEnumerator ElectricShock()
    {
        yield return Approach(2.0f, 2.4f, 5f);
        coreGlow.enabled = true;
        StartCoroutine(FollowGlow(coreGlow, 0f, 0.55f, 0.6f, 2.4f, 1.0f));
        yield return Telegraph(1.0f, shockMark);      // 身体内部が発光 → 触手へ電気
        coreGlow.enabled = false;
        Sfx(SkyBossSfx.Thunder(), 0.5f);
        SkyBossFx.Sparks(FrontWorld(0.6f, 1.8f - HoverAlt), Pale, 12, 0.5f);
        yield return Strike(shock, 0.3f, 0.06f);
        yield return Recover(1.0f);
    }

    IEnumerator LightningOrbs()
    {
        SkyProjectileGate.NextTime = Time.time + SkyProjectileGate.Interval + 2f;
        coreGlow.enabled = true;
        StartCoroutine(FollowGlow(coreGlow, 0f, 0.55f, 0.4f, 1.8f, 1.1f));
        yield return Telegraph(1.1f);                   // 身体中央へ光を集める
        coreGlow.enabled = false;
        Vector3 from = BodyPoint(0.2f, 0.5f);
        Vector2 dir = AimFrom(from);
        for (int i = -1; i <= 1; i++)
        {
            Vector2 d = Quaternion.Euler(0f, 0f, i * 16f) * dir;
            BossProjectile.Create(BossFx.Orb(), new Color(0.65f, 0.92f, 1f, 1f), from, new Vector2(0.9f, 0.9f), d * 3.4f, 5.5f, RenderOrder.Boss + 1);
        }
        Sfx(SkyBossSfx.Whoosh(), 0.5f);
        PlayAttackPose(0.4f);
        yield return Wait(0.4f);
        yield return Recover(1.2f);
    }
}

// ============ 40,000m 雲海リヴァイアサン ============
// 天空回廊の下の雲海を本物の海のように泳ぐ超巨大生物。通常は雲海の下の巨大な影+背びれ
// だけが見え、攻撃時だけ身体を大きく露出する(露出中だけ被弾する)。
// 飛び出し(雲海が盛り上がる→下から巨大な頭部)/横断(身体の通過位置を事前表示→画面を横断)/
// 雲海ブレス(頭部を持ち上げる→長い溜め→巨大ブレス)。
public class LeviathanBoss : SkyBossBase
{
    BossHitbox erupt, cross, breath;
    SpriteRenderer shadowSr, finSr, mouthGlow;
    int last = -1;
    bool submerged = true;
    bool finVisible;
    float SubmergedY => -bodyHeight * 1.25f;
    float EmergedY => -bodyHeight * 0.2f;

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Slither;
        suppressLocoDust = true;
        hitStopOnHit = 0.05f;
        startGap = 7f; minGap = -26f; maxGap = 28f;
        SetVisualSortingOrder(BehindGround);
        erupt = NewHitbox("Erupt", new Vector2(FrontReach * 0.55f, bodyHeight * 0.55f), new Vector2(Mathf.Max(2.4f, halfWidth * 0.8f), bodyHeight * 0.9f), BossFx.Ring(), new Color(0.85f, 0.95f, 1f, 0.9f));
        cross = NewHitbox("Cross", Vector2.zero, Vector2.one, BossFx.Slash(), new Color(0.8f, 0.92f, 1f, 0.6f));
        breath = NewHitbox("Breath", Vector2.zero, Vector2.one, SkyBossFx.Beam(), new Color(0.8f, 0.95f, 1f, 0.95f));
        mouthGlow = MakeGlow("MouthGlow", new Color(0.7f, 0.95f, 1f, 0.95f));

        // 影/背びれはRootの子(撃破で一緒に消える)。位置は毎フレーム雲海の高さへワールド座標で置く。
        // ※WildBossBaseのOnDestroy/OnDisable(private)を隠さないよう、サブクラスでは定義しない。
        shadowSr = new GameObject("LeviathanShadow").AddComponent<SpriteRenderer>();
        shadowSr.transform.SetParent(transform, false);
        shadowSr.sprite = SkyBossFx.Shadow();
        shadowSr.sortingOrder = BehindGround - 1;
        finSr = new GameObject("LeviathanFin").AddComponent<SpriteRenderer>();
        finSr.transform.SetParent(transform, false);
        finSr.sprite = SkyBossFx.Fin();
        finSr.sortingOrder = BehindGround;
        finSr.color = new Color(0.22f, 0.27f, 0.4f, 0f);

        yOffset = SubmergedY;
        SetAlpha(0f);
        invulnerable = true;
        SetHurtboxEnabled(false);
        StartCoroutine(Ambient());
    }

    protected override IEnumerator Enter()
    {
        // 雲海の中に巨大な影 → 背びれだけ見える → 並走 → 巨大な頭部が雲海から出現
        worldX = PlayerX + 24f;
        float t = 0f;
        while (Gap > 8f && t < 6f)
        {
            t += Time.deltaTime;
            relVelocity = -9f;
            finVisible = Gap < 18f;
            yield return null;
        }
        relVelocity = 0f;
        finVisible = true;
        yield return Wait(1.0f);   // 並走
        finVisible = false;
        Sfx(SkyBossSfx.Roar(), 1f);
        Shake(0.14f, 0.8f);
        yield return Emerge(0.9f);
        yield return Wait(0.8f);
        yield return Submerge(0.6f);
        approachDone = true;
    }

    IEnumerator Ambient()
    {
        float puff = 0f;
        while (!IsDead)
        {
            SetHpBarOffset(5.8f - yOffset);
            float submergedK = Mathf.InverseLerp(EmergedY, SubmergedY, yOffset);
            if (shadowSr != null)
            {
                shadowSr.enabled = true;
                shadowSr.transform.position = new Vector3(worldX, GroundY - 2.6f + Mathf.Sin(Time.time * 1.2f) * 0.2f, 0f);
                shadowSr.transform.localScale = new Vector3(halfWidth * 2.4f, bodyHeight * 0.7f, 1f);
                shadowSr.color = new Color(0.12f, 0.16f, 0.28f, 0.5f * submergedK);
            }
            if (finSr != null)
            {
                finSr.enabled = true;
                float target = finVisible && submerged ? 1f : 0f;
                Color c = finSr.color;
                c = new Color(0.22f, 0.27f, 0.4f, Mathf.MoveTowards(c.a, target * 0.95f, Time.deltaTime * 2f));
                finSr.color = c;
                finSr.transform.position = new Vector3(worldX - facing * halfWidth * 0.15f, GroundY - 1.3f + c.a * 0.9f + Mathf.Sin(Time.time * 3f) * 0.12f, 0f);
                finSr.transform.localScale = new Vector3(2.2f * (facing < 0 ? 1f : -1f), 2.2f, 1f);
                puff -= Time.deltaTime;
                if (c.a > 0.3f && puff <= 0f)
                {
                    puff = 0.07f;
                    SkyDrift.Spawn(SkyBossFx.CloudPuff(), finSr.transform.position + new Vector3(-facing * 0.8f, -0.6f, 0f), Vector2.one * Random.Range(0.6f, 1.2f), new Color(1f, 1f, 1f, 0.6f), new Vector3(-facing * 1.5f, 0.4f, 0f), 0.7f, BehindGround + 1, true);
                }
            }
            yield return null;
        }
    }

    IEnumerator Emerge(float duration)
    {
        submerged = false;
        float start = yOffset;
        float t = 0f;
        while (t < duration && !IsDead)
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / duration);
            yOffset = Mathf.Lerp(start, EmergedY, Mathf.SmoothStep(0f, 1f, f));
            SetAlpha(Mathf.Clamp01(f * 1.6f));
            if (Random.value < 0.5f) SkyDrift.Spawn(SkyBossFx.CloudPuff(), new Vector3(worldX + Random.Range(-halfWidth, halfWidth), GroundY - 0.6f, 0f), Vector2.one * Random.Range(1f, 2f), new Color(1f, 1f, 1f, 0.7f), new Vector3(0f, 2f, 0f), 0.8f, RenderOrder.CombatFx, true);
            yield return null;
        }
        yOffset = EmergedY;
        SetAlpha(1f);
        // 露出している頭部〜首だけが被弾範囲
        ConfigureHurtbox(new Vector2(facing * FrontReach * 0.35f, bodyHeight * 0.62f), new Vector2(Mathf.Max(3f, halfWidth * 1.2f), bodyHeight * 0.55f));
        SetHurtboxEnabled(true);
        invulnerable = false;
    }

    IEnumerator Submerge(float duration)
    {
        invulnerable = true;
        SetHurtboxEnabled(false);
        float start = yOffset;
        float t = 0f;
        while (t < duration && !IsDead)
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / duration);
            yOffset = Mathf.Lerp(start, SubmergedY, f * f);
            SetAlpha(1f - f);
            yield return null;
        }
        yOffset = SubmergedY;
        SetAlpha(0f);
        submerged = true;
    }

    IEnumerator SwimTo(float targetGap, float speed, float timeout = 4f)
    {
        float t = 0f;
        finVisible = true;
        while (t < timeout && Mathf.Abs(Gap - targetGap) > 0.4f && !IsDead)
        {
            t += Time.deltaTime;
            relVelocity = Mathf.Sign(targetGap - Gap) * speed;
            yield return null;
        }
        relVelocity = 0f;
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            int pick = BossAiUtil.PickNoRepeat(3, ref last);
            if (pick == 0) yield return BurstUp();
            else if (pick == 1) yield return CrossSweep();
            else yield return CloudBreath();
            yield return SwimTo(7f, 8f);
            yield return Wait(0.6f);
        }
    }

    IEnumerator BurstUp()
    {
        facing = -1f;
        float headFwd = FrontReach * 0.55f;
        yield return SwimTo(1.2f + headFwd, 10f);
        finVisible = false;
        float headX = worldX + facing * headFwd;
        // 雲海が大きく盛り上がる(予告)
        SkyWarnBand.Create(headX - 1.8f, headX + 1.8f, -0.8f, 0.5f, 1.2f);
        float t = 0f;
        while (t < 1.2f && !IsDead)
        {
            t += Time.deltaTime;
            if (Random.value < 0.6f) SkyDrift.Spawn(SkyBossFx.CloudPuff(), new Vector3(worldX + facing * headFwd + Random.Range(-1.8f, 1.8f), GroundY - 0.4f, 0f), Vector2.one * Random.Range(0.8f, 1.6f), new Color(1f, 1f, 1f, 0.75f), new Vector3(0f, 2.5f, 0f), 0.5f, RenderOrder.CombatFx, true);
            yield return null;
        }
        Sfx(SkyBossSfx.Impact(), 1f);
        Shake(0.16f, 0.3f);
        yield return Emerge(0.28f);
        yield return Strike(erupt, 0.3f, 0.12f, 0.05f);
        EndAttack();
        SetPose(Pose.Landing);
        yield return Wait(1.3f);        // 露出中=攻撃チャンス
        yield return Submerge(0.5f);
    }

    IEnumerator CrossSweep()
    {
        bool low = Random.value < 0.5f;
        yield return SwimTo(22f, 14f);
        finVisible = false;
        Vector2 band = low ? LowBand : HighBand;
        SkyWarnBand.Create(PlayerX - 14f, PlayerX + 16f, band.x, band.y, 1.4f);
        // 雲が帯に沿って流れる(身体の通過位置の予兆)
        StartCoroutine(CloudStream(band, 1.4f));
        yield return Wait(1.4f);
        if (IsDead) yield break;
        // 低い横断: 背中だけが回廊をかすめる(ジャンプで回避)。高い横断: 頭上を通過(地上で回避)。
        yOffset = low ? (band.y - bodyHeight) : band.x;
        yOffset += PlayerGroundY - GroundY;
        SetAlpha(1f);
        invulnerable = false;
        SetHurtboxEnabled(true);
        ConfigureHurtbox(new Vector2(0f, low ? bodyHeight - 0.8f : bodyHeight * 0.5f), new Vector2(halfWidth * 1.6f, low ? 1.6f : bodyHeight * 0.7f));
        float bottom = low ? bodyHeight - (band.y - band.x) : 0f;
        float top = low ? bodyHeight : bodyHeight * 0.9f;
        cross.Configure(new Vector2(0f, (bottom + top) * 0.5f), new Vector2(halfWidth * 1.8f, top - bottom));
        Sfx(SkyBossSfx.Whoosh(), 1f);
        Shake(0.1f, 1.2f);
        yield return CrossBody(cross, 22f, -22f, 24f);
        EndAttack();
        SetAlpha(0f);
        invulnerable = true;
        SetHurtboxEnabled(false);
        yOffset = SubmergedY;
        submerged = true;
    }

    IEnumerator CloudStream(Vector2 band, float dur)
    {
        float t = 0f;
        while (t < dur && !IsDead)
        {
            t += Time.deltaTime;
            if (Random.value < 0.7f)
            {
                float y = PlayerGroundY + Random.Range(band.x, band.y);
                SkyDrift.Spawn(SkyBossFx.CloudPuff(), new Vector3(PlayerX + Random.Range(6f, 14f), y, 0f), Vector2.one * Random.Range(0.7f, 1.3f), new Color(0.9f, 0.95f, 1f, 0.6f), new Vector3(-12f, 0f, 0f), 0.9f, RenderOrder.CombatFx, true);
            }
            yield return null;
        }
    }

    IEnumerator CloudBreath()
    {
        facing = -1f;
        yield return SwimTo(10f, 10f);
        finVisible = false;
        Sfx(SkyBossSfx.Roar(), 0.8f);
        yield return Emerge(0.8f);   // 頭部を持ち上げる
        bool low = Random.value < 0.5f;
        Vector2 band = low ? LowBand : HighBand;
        if (!low) band.y = 3.6f;
        float length = Mathf.Max(6f, Gap + 5f);
        breath.Configure(new Vector2(FrontReach + length * 0.5f, (band.x + band.y) * 0.5f - yOffset + (PlayerGroundY - GroundY)), new Vector2(length, band.y - band.x));
        SkyWarnBand.Create(PlayerX - 5f, worldX + facing * FrontReach, band.x, band.y, 1.8f);
        mouthGlow.enabled = true;
        StartCoroutine(FollowGlow(mouthGlow, 0.9f, 0.6f, 0.5f, 3f, 1.8f));
        yield return Telegraph(1.8f);      // 非常に強力なので長い予備動作
        mouthGlow.enabled = false;
        Sfx(SkyBossSfx.Wind(), 1f);
        Sfx(SkyBossSfx.Thunder(), 0.5f);
        yield return Strike(breath, 0.55f, 0.12f);
        yield return Recover(1.2f);        // ブレス後の隙(露出中)
        yield return Submerge(0.6f);
    }
}

// ============ 50,000m 天空魔狼フェンリル ============
// 荒野街道の巨大オオカミの「究極形」。青白い炎を纏い、回廊・雲・一瞬形成される魔力足場を蹴って疾走。
// 神速噛みつき/天空跳躍(着地点表示)/魔力咆哮(衝撃波)/連続疾走(追い越す→振り返る→すれ違い攻撃)。
public class FenrirBoss : SkyBossBase
{
    BossHitbox bite, pass;
    BossTelegraphMarker biteMark, passMark;
    SpriteRenderer mouthGlow;
    int last = -1;
    static readonly Color Flame = new Color(0.6f, 0.85f, 1f, 0.9f);

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Gallop;
        enterSpeed = 16f;
        interruptible = true;
        hitStopOnHit = 0.035f;
        startGap = 7f; minGap = -14f; maxGap = 18f;
        SetBodyTint(new Color(0.88f, 0.95f, 1f));
        Vector2 bc = new Vector2(FrontReach + 0.6f, bodyHeight * 0.42f), bs = new Vector2(2.2f, 1.4f);
        bite = NewHitbox("Bite", bc, bs, BossFx.Fang(), new Color(0.8f, 0.92f, 1f, 0.95f));
        biteMark = NewMarker(bc, bs);
        Vector2 pc2 = new Vector2(FrontReach * 0.3f, 0.62f), ps = new Vector2(halfWidth * 1.4f, 1.24f);
        pass = NewHitbox("Pass", pc2, ps, BossFx.Slash(), new Color(0.7f, 0.9f, 1f, 0.85f));
        passMark = NewMarker(new Vector2(FrontReach + 5f, 0.62f), new Vector2(10f, 1.24f));
        mouthGlow = MakeGlow("MouthGlow", new Color(0.6f, 0.85f, 1f, 0.95f));
        SetAlpha(0f);
        StartCoroutine(Ambient());
    }

    protected override IEnumerator Enter()
    {
        invulnerable = true;
        SetHurtboxEnabled(false);
        SetAlpha(0f);
        // 雲の向こうを青白い光が駆け抜ける → 後方から追い抜き、プレイヤーを跳び越えて前へ
        SkyFlyby.Create(BodyFrames, new Vector2(1.15f, 0.7f), new Vector2(-0.2f, 0.58f), scaleFactor * 0.3f, scaleFactor * 0.5f, 1.1f, new Color(0.55f, 0.75f, 1f, 0.8f), !artFacesLeft);
        yield return Wait(1.1f);
        Sfx(SkyBossSfx.Roar(), 0.7f);
        worldX = PlayerX - 16f;
        yOffset = 0f;
        SetAlpha(1f);
        SetPose(Pose.Move);
        float t = 0f;
        while (Gap < -3f && t < 4f)
        {
            relVelocity = 18f;
            t += Time.deltaTime;
            yield return null;
        }
        yield return Leap(0.7f, 3.5f, 9f);
        ImpactDust(new Vector3(worldX, GroundY, 0f), 8, 1f);
        relVelocity = 0f;
        invulnerable = false;
        SetHurtboxEnabled(true);
        approachDone = true;
    }

    IEnumerator Ambient()
    {
        float flame = 0f;
        while (!IsDead)
        {
            flame -= Time.deltaTime;
            if (flame <= 0f && approachDone)
            {
                flame = 0.05f;
                Vector3 p = CenterWorld + new Vector3(Random.Range(-halfWidth, halfWidth) * 0.8f, Random.Range(-0.3f, 0.45f) * bodyHeight, 0f);
                SkyDrift.Spawn(BossFx.Orb(), p, Vector2.one * Random.Range(0.25f, 0.5f), Flame, new Vector3(-facing * 2f, 1.4f, 0f), 0.45f, RenderOrder.Boss + 1, true);
            }
            AirStep(new Color(0.55f, 0.8f, 1f, 0.9f));
            yield return null;
        }
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            int pick = BossAiUtil.PickNoRepeat(4, ref last);
            if (pick == 0) yield return GodspeedBite();
            else if (pick == 1) yield return SkyLeap();
            else if (pick == 2) yield return RoarWave();
            else yield return RushPass();
        }
    }

    IEnumerator GodspeedBite()
    {
        yield return MoveToGap(4.5f, 8f, 2f);
        mouthGlow.enabled = true;
        StartCoroutine(FollowGlow(mouthGlow, 0.95f, 0.45f, 0.4f, 1.3f, 0.55f));
        yield return Telegraph(0.55f, biteMark);   // 頭を引く → 口元へ魔力
        mouthGlow.enabled = false;
        if (interrupted) { yield return Stagger(0.8f); yield break; }
        Sfx(SkyBossSfx.Whoosh(), 0.7f);
        StartCoroutine(DashMove(0.2f, 18f));      // 瞬間的に距離を詰める
        yield return Strike(bite, 0.22f);
        yield return Recover(0.6f);
        yield return MoveToGap(6f, 8f, 2f);
    }

    IEnumerator SkyLeap()
    {
        yield return Telegraph(0.8f);             // 大きく沈み込む
        float landGap = 2.4f;
        SkyStrike.Create(PlayerX + landGap, 3.2f, 1.3f, 1.0f, 0.25f, Flame, SkyStrike.Look.Burst);
        // 空中の魔力足場を蹴って高く跳ぶ
        StartCoroutine(MidAirSteps(1.0f));
        yield return Leap(1.0f, 5.5f, landGap - Gap);
        ImpactDust(new Vector3(worldX, GroundY, 0f), 12, 1.2f);
        GroundRing(new Vector3(worldX, GroundY, 0f), 3.2f, Flame);
        Shake(0.1f, 0.2f);
        EndAttack();
        SetPose(Pose.Landing);
        yield return Wait(1.0f);                  // 着地の隙
        yield return MoveToGap(6f, 8f, 2f);
    }

    IEnumerator MidAirSteps(float dur)
    {
        float t = 0f, step = 0.18f;
        while (t < dur && !IsDead)
        {
            t += Time.deltaTime;
            step -= Time.deltaTime;
            if (step <= 0f)
            {
                step = 0.2f;
                OneShotSpriteEffect.CreateTweened(SkyBossFx.GroundGlow(), new Vector3(worldX, GroundY + yOffset, 0f), Flame, 0.3f, 1.2f, 2.2f, 0.9f, 0f, default, 0f, RenderOrder.Boss - 1, 0.1f);
            }
            yield return null;
        }
    }

    IEnumerator RoarWave()
    {
        relVelocity = 0f;
        yield return Telegraph(1.0f);             // 停止 → 頭を上げる
        Sfx(SkyBossSfx.Roar(), 1f);
        Shake(0.1f, 0.4f);
        OneShotSpriteEffect.CreateTweened(BossFx.Ring(), BodyPoint(0.9f, 0.6f), Flame, 0.45f, 1f, 4f, 0.9f, 0f, default, 0f, RenderOrder.CombatFx, 0.1f);
        var w = BossProjectile.Create(BossFx.Ring(), new Color(0.65f, 0.85f, 1f, 0.95f), FrontWorld(0.8f, 0.6f), new Vector2(1.5f, 1.2f), new Vector2(facing * 9f, 0f), 2.6f, RenderOrder.Boss + 1);
        w.hugGround = true;
        w.groundOffset = 0.6f;
        PlayAttackPose(0.5f);
        yield return Wait(0.5f);
        yield return Recover(1.1f);
    }

    IEnumerator RushPass()
    {
        // プレイヤーを追い越して前へ → 振り返る → 高速ですれ違い攻撃
        yield return MoveToGap(-9f, 14f, 2.5f);
        yield return MoveToGap(11f, 18f, 2.5f);
        yield return Wait(0.2f);
        yield return Telegraph(0.8f, passMark);
        Sfx(SkyBossSfx.Whoosh(), 0.9f);
        StartCoroutine(DashMove(0.95f, 21f));
        yield return Strike(pass, 0.95f, 0.06f);
        yield return Recover(0.7f);
        yield return MoveToGap(6f, 12f, 2.5f);
    }
}

// ============ 60,000m 天空ゴーレム ============
// 浮島そのものから作られたような古代巨像。周囲の浮遊岩が集まって身体を形成し、コアが点灯して起動。
// 重量感(大股・足音)と浮遊感(身体の岩がわずかに浮き沈み、周囲を岩が周回)を両立。
// 拳(拳を形成→振りかぶる→叩きつけ)/浮遊岩(岩を分離→プレイヤー上空へ→予告→落下)/コア砲撃(チャージ→前方)。
public class SkyGolemBoss : SkyBossBase
{
    BossHitbox fist, beam;
    BossTelegraphMarker fistMark, beamMark;
    SpriteRenderer coreGlow, fistGlow;
    readonly List<SpriteRenderer> orbiters = new List<SpriteRenderer>();
    int last = -1;
    const float FloatAlt = 0.35f;
    static readonly Color Core = new Color(0.55f, 0.95f, 1f, 1f);

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Stride;
        footstepShake = true;
        hitStopOnHit = 0.05f;
        windupMoveFactor = 0.25f;
        startGap = 9f; enterSpeed = 4f;
        Vector2 fc = new Vector2(FrontReach + 1.0f, 0.6f), fs = new Vector2(3.2f, 1.6f);
        fist = NewHitbox("Fist", fc, fs, BossFx.Ring(), new Color(0.9f, 0.85f, 0.75f, 0.95f));
        fistMark = NewMarker(fc, fs);
        Vector2 bc = new Vector2(FrontReach + 6.5f, 0.62f - FloatAlt), bs = new Vector2(13f, 1.25f);
        beam = NewHitbox("CoreBeam", bc, bs, SkyBossFx.Beam(), new Color(0.6f, 0.95f, 1f, 0.95f));
        beamMark = NewMarker(bc, bs);
        coreGlow = MakeGlow("CoreGlow", Core);
        fistGlow = MakeGlow("FistGlow", new Color(1f, 0.9f, 0.6f, 0.9f));
        for (int i = 0; i < 3; i++)
        {
            var go = new GameObject("OrbitRock" + i);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<SpriteRenderer>();
            s.sprite = CaveBossFx.RockChunk();
            s.color = new Color(0.72f, 0.7f, 0.66f, 0f);
            s.sortingOrder = RenderOrder.Boss + 1;
            orbiters.Add(s);
        }
        SetAlpha(0f);
        StartCoroutine(Ambient());
    }

    protected override IEnumerator Enter()
    {
        invulnerable = true;
        SetHurtboxEnabled(false);
        SetAlpha(0f);
        worldX = PlayerX + startGap;
        yOffset = FloatAlt;
        // 周囲の浮遊岩が集まる → 身体形成 → コア点灯
        var rocks = new List<Transform>();
        var starts = new List<Vector3>();
        for (int i = 0; i < 12; i++)
        {
            var go = new GameObject("GatherRock");
            var s = go.AddComponent<SpriteRenderer>();
            s.sprite = CaveBossFx.RockChunk();
            s.color = new Color(0.7f, 0.68f, 0.64f, 1f);
            s.sortingOrder = RenderOrder.Boss + 1;
            float ang = Random.Range(0f, Mathf.PI * 2f);
            Vector3 off = new Vector3(Mathf.Cos(ang), Mathf.Abs(Mathf.Sin(ang)) + 0.2f, 0f) * Random.Range(5f, 9f);
            go.transform.localScale = Vector3.one * Random.Range(0.7f, 1.4f);
            rocks.Add(go.transform);
            starts.Add(off);
        }
        Sfx(SkyBossSfx.Wind(), 0.6f);
        float t = 0f; const float dur = 1.7f;
        while (t < dur && !IsDead)
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / dur);
            worldX = PlayerX + startGap;
            Vector3 center = CenterWorld;
            for (int i = 0; i < rocks.Count; i++)
            {
                float k = Mathf.Clamp01(f * 1.25f - i * 0.02f);
                rocks[i].position = center + starts[i] * (1f - k * k) + new Vector3(0f, Mathf.Sin(Time.time * 3f + i) * 0.2f, 0f);
                rocks[i].localRotation = Quaternion.Euler(0f, 0f, Time.time * 90f + i * 30f);
            }
            SetAlpha(Mathf.InverseLerp(0.55f, 1f, f));
            if (Random.value < 0.08f) Shake(0.05f, 0.1f);
            yield return null;
        }
        foreach (var r in rocks) if (r != null) Destroy(r.gameObject);
        SetAlpha(1f);
        coreGlow.enabled = true;
        SkyBossFx.Flash(BodyPoint(0.4f, 0.6f), Core, 3f, 0.3f);
        Sfx(SkyBossSfx.Impact(), 1f);
        Shake(0.14f, 0.3f);
        invulnerable = false;
        SetHurtboxEnabled(true);
        approachDone = true;
    }

    IEnumerator Ambient()
    {
        while (!IsDead)
        {
            float tt = Time.time;
            if (approachDone && !windingUp) yOffset = FloatAlt + 0.18f * Mathf.Sin(tt * 1.6f);   // 浮遊感
            if (coreGlow.enabled && pose != Pose.Windup)
            {
                coreGlow.transform.position = BodyPoint(0.4f, 0.6f);
                float s = 0.9f + 0.15f * Mathf.Sin(tt * 4f);
                coreGlow.transform.localScale = new Vector3(s, s, 1f);
            }
            for (int i = 0; i < orbiters.Count; i++)
            {
                var o = orbiters[i];
                if (o == null) continue;
                float a = tt * 1.1f + i * 2.1f;
                o.transform.position = CenterWorld + new Vector3(Mathf.Cos(a) * halfWidth * 1.15f, Mathf.Sin(a * 0.8f) * bodyHeight * 0.35f, 0f);
                o.transform.localRotation = Quaternion.Euler(0f, 0f, tt * 60f + i * 50f);
                o.transform.localScale = Vector3.one * 0.7f;
                o.color = new Color(0.72f, 0.7f, 0.66f, approachDone ? 1f : 0f);
            }
            yield return null;
        }
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            int pick = BossAiUtil.PickNoRepeat(3, ref last);
            if (pick == 0) yield return FistSlam();
            else if (pick == 1) yield return FloatingRocks();
            else yield return CoreCannon();
        }
    }

    IEnumerator FistSlam()
    {
        yield return Approach(2.6f, 1.6f, 6f);
        fistGlow.enabled = true;
        StartCoroutine(FollowGlow(fistGlow, 0.85f, 0.75f, 0.5f, 2f, 1.2f));   // 巨大な拳を形成
        yield return Telegraph(1.2f, fistMark);
        fistGlow.enabled = false;
        ImpactDust(FrontWorld(1.0f, 0f), 14, 1.3f);
        GroundRing(FrontWorld(1.0f, 0f), 3.4f, new Color(0.95f, 0.9f, 0.8f, 0.8f));
        Sfx(SkyBossSfx.Impact(), 1f);
        yield return Strike(fist, 0.28f, 0.14f, 0.07f);
        yield return Recover(1.3f);
    }

    IEnumerator FloatingRocks()
    {
        yield return Telegraph(0.8f);   // 身体の岩を分離
        float[] offs = { -2.8f, 0.1f, 2.9f };
        for (int i = offs.Length - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (offs[i], offs[j]) = (offs[j], offs[i]); }
        for (int i = 0; i < offs.Length; i++)
        {
            float warn = 1.3f + i * 0.35f;
            var s = SkyStrike.Create(PlayerX + offs[i], 1.5f, 1.5f, warn, 0.25f, Color.white, SkyStrike.Look.Rock);
            s.incomingHeight = 4.5f;
            StartCoroutine(RockToStrike(s, warn));
        }
        PlayAttackPose(0.6f);
        yield return Wait(1.6f);
        yield return Recover(1.0f);
    }

    // 分離した岩がゴーレムからプレイヤー上空(着弾点の真上)へ移動して待機し、落下の瞬間に消える
    // (落下自体はSkyStrike側の見た目が引き継ぐ)。
    IEnumerator RockToStrike(SkyStrike s, float warn)
    {
        var go = new GameObject("DetachedRock");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = CaveBossFx.RockChunk();
        sr.color = new Color(0.72f, 0.7f, 0.66f, 1f);
        sr.sortingOrder = RenderOrder.Boss + 1;
        go.transform.localScale = Vector3.one * 1.35f;
        Vector3 from = CenterWorld;
        float t = 0f;
        float fallAt = warn * 0.72f;
        while (t < fallAt && s != null)
        {
            t += Time.deltaTime;
            float baseSpeed = TargetBaseSpeed();
            from.x += baseSpeed * Time.deltaTime;
            Vector3 hover = new Vector3(s.WorldX, s.GroundY + 4.5f + Mathf.Sin(Time.time * 6f) * 0.1f, 0f);
            float k = Mathf.Clamp01(t / 0.55f);
            go.transform.position = Vector3.Lerp(from, hover, Mathf.SmoothStep(0f, 1f, k));
            go.transform.localRotation = Quaternion.Euler(0f, 0f, Time.time * 120f);
            // 落下直前は赤く光って予告
            sr.color = Color.Lerp(new Color(0.72f, 0.7f, 0.66f, 1f), new Color(1f, 0.45f, 0.3f, 1f), Mathf.InverseLerp(fallAt * 0.6f, fallAt, t) * (0.6f + 0.4f * Mathf.Sin(Time.time * 30f)));
            yield return null;
        }
        Destroy(go);
    }

    IEnumerator CoreCannon()
    {
        yield return MoveToGap(8f, 2.5f, 3f);
        coreGlow.enabled = true;
        var glowT = coreGlow.transform;
        float t = 0f;
        StartCoroutine(Telegraph(1.5f, beamMark));
        while (t < 1.5f && !IsDead)
        {
            t += Time.deltaTime;
            glowT.position = BodyPoint(0.4f, 0.6f);
            float s = Mathf.Lerp(0.9f, 2.6f, t / 1.5f) * (1f + 0.15f * Mathf.Sin(Time.time * 40f));
            glowT.localScale = new Vector3(s, s, 1f);
            if (Random.value < 0.4f) SkyBossFx.Sparks(glowT.position + (Vector3)Random.insideUnitCircle * 2f, Core, 1, 0.3f);
            yield return null;
        }
        Sfx(SkyBossSfx.Thunder(), 0.7f);
        Sfx(SkyBossSfx.Whoosh(), 1f);
        yield return Strike(beam, 0.5f, 0.12f);
        yield return Recover(1.2f);
    }
}

// ============ 70,000m フェニックス ============
// 炎そのものが生命を持ったような巨大な火の鳥。常時飛行し、炎の軌跡を残す。
// 急降下(上空→炎が強まる→着地点へ急降下)/炎の羽(羽ばたき→少数の羽を前方へ)/炎上突進(全身を炎→横断)。
// 特殊: 初回HPゼロで炎となって消え、炎が集まって復活(HP40%から第二段階)。
public class PhoenixBoss : SkyBossBase
{
    BossHitbox dive, rush;
    int last = -1;
    bool reborn;
    const float HoverAlt = 2.6f;
    static readonly Color Fire = new Color(1f, 0.55f, 0.18f, 1f);

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Wing;
        hitStopOnHit = 0.03f;
        startGap = 8f; minGap = -18f; maxGap = 22f;
        SetBodyTint(new Color(1f, 0.95f, 0.85f));
        dive = NewHitbox("Dive", new Vector2(0f, bodyHeight * 0.4f), new Vector2(halfWidth * 1.1f, bodyHeight * 0.8f), BossFx.Ring(), new Color(1f, 0.6f, 0.2f, 0.9f));
        rush = NewHitbox("Rush", new Vector2(0f, bodyHeight * 0.5f), new Vector2(halfWidth * 1.6f, bodyHeight * 0.7f), BossFx.Slash(), new Color(1f, 0.5f, 0.15f, 0.9f));
        SetAlpha(0f);
        StartCoroutine(Ambient());
    }

    protected override IEnumerator Enter()
    {
        // 遠方に小さな炎 → 高速で接近 → 巨大な翼を広げる
        yield return DistantApproach(1.8f, startGap, HoverAlt, new Vector2(0.97f, 0.9f), 0.12f, new Color(1f, 0.55f, 0.15f, 0.95f));
        SetPose(Pose.Landing);
        Sfx(SkyBossSfx.Flame(), 1f);
        OneShotSpriteEffect.CreateTweened(BossFx.Ring(), CenterWorld, Fire, 0.5f, 1.5f, 6f, 0.9f, 0f, default, 0f, RenderOrder.CombatFx, 0.1f);
        SkyBossFx.Sparks(CenterWorld, Fire, 16, 0.8f);
        yield return Wait(0.6f);
        approachDone = true;
    }

    IEnumerator Ambient()
    {
        float trail = 0f;
        while (!IsDead)
        {
            trail -= Time.deltaTime;
            if (trail <= 0f && approachDone)
            {
                trail = 0.04f;
                // 炎の軌跡(尾・翼の後ろに残る)
                Vector3 p = BodyPoint(-Random.Range(0.3f, 1.0f), Random.Range(0.2f, 0.8f));
                Color c = Color.Lerp(Fire, new Color(1f, 0.85f, 0.3f, 1f), Random.value);
                SkyDrift.Spawn(BossFx.Orb(), p, Vector2.one * Random.Range(0.3f, 0.7f), c, new Vector3(-facing * 1.5f, 0.8f, 0f), 0.5f, RenderOrder.Boss - 1);
            }
            if (approachDone && !windingUp && pose != Pose.Attack && yOffset > 1.5f && yOffset < 3.5f)
            {
                yOffset = Mathf.MoveTowards(yOffset, HoverAlt + 0.35f * Mathf.Sin(Time.time * 1.7f), Time.deltaTime * 2f);
            }
            yield return null;
        }
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            int pick = BossAiUtil.PickNoRepeat(3, ref last);
            if (pick == 1 && Time.time < SkyProjectileGate.NextTime) pick = 0;
            if (pick == 0) yield return DiveAttack();
            else if (pick == 1) yield return FlameFeathers();
            else yield return FlameRush();
            if (reborn) yield return Wait(0.1f); // 第二段階: 隙を少し短く(Recover側で調整)
        }
    }

    float RecoverTime(float normal) => reborn ? normal * 0.75f : normal;

    IEnumerator DiveAttack()
    {
        StartCoroutine(SetAltitude(6.5f, 0.6f));
        yield return MoveToGap(3f, 7f, 1.2f);
        SetBodyTint(new Color(1f, 0.75f, 0.45f));    // 身体の炎が強くなる
        float landGap = Random.Range(-0.5f, 1.0f);
        SkyStrike.Create(PlayerX + landGap, 3f, 2f, 1.1f, 0.2f, Fire, SkyStrike.Look.Burst);
        windingUp = true; facingLocked = true; SetPose(Pose.Windup);
        float t = 0f;
        while (t < 0.85f && !IsDead)
        {
            t += Time.deltaTime;
            windupProgress = t / 0.85f;
            relVelocity = Mathf.Clamp((landGap - Gap) * 3f, -8f, 8f);
            yield return null;
        }
        windingUp = false; windupProgress = 0f; relVelocity = 0f;
        Sfx(SkyBossSfx.Flame(), 1f);
        StartCoroutine(SetAltitude(0.3f, 0.25f));
        yield return Strike(dive, 0.35f, 0.12f, 0.05f);
        SetBodyTint(new Color(1f, 0.95f, 0.85f));
        EndAttack();
        SetPose(Pose.Landing);
        yield return Wait(RecoverTime(0.9f));
        yield return SetAltitude(HoverAlt, 0.6f);
        yield return MoveToGap(8f, 6f, 2f);
    }

    IEnumerator FlameFeathers()
    {
        SkyProjectileGate.NextTime = Time.time + SkyProjectileGate.Interval + 1.5f;
        yield return Telegraph(0.8f);   // 翼を大きく振りかぶる
        Vector3 from = BodyPoint(0.3f, 0.6f);
        Vector2 dir = AimFrom(from);
        int count = reborn ? 5 : 4;
        for (int i = 0; i < count; i++)
        {
            float ang = Mathf.Lerp(-22f, 22f, count > 1 ? i / (float)(count - 1) : 0.5f);
            Vector2 d = Quaternion.Euler(0f, 0f, ang) * dir;
            var p = BossProjectile.Create(SkyBossFx.Feather(), new Color(1f, 0.6f, 0.2f, 1f), from, new Vector2(1.1f, 0.5f), d * 7.5f, 3f, RenderOrder.Boss + 1);
            p.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg + 180f);
        }
        Sfx(SkyBossSfx.Whoosh(), 0.8f);
        PlayAttackPose(0.4f);
        yield return Wait(0.4f);
        yield return Recover(RecoverTime(1.0f));
    }

    IEnumerator FlameRush()
    {
        yield return MoveToGap(16f, 12f, 2f);
        float alt = HighBand.x - bodyHeight * 0.1f;
        yield return SetAltitude(alt, 0.5f);
        SetBodyTint(new Color(1f, 0.6f, 0.3f));      // 全身を炎で包む
        float bottom = alt + bodyHeight * 0.1f, top = alt + bodyHeight * 0.85f;
        SkyWarnBand.Create(PlayerX - 14f, PlayerX + 16f, bottom, top, 1.1f);
        yield return Telegraph(1.1f);
        Sfx(SkyBossSfx.Flame(), 1f);
        Sfx(SkyBossSfx.Whoosh(), 1f);
        yield return CrossBody(rush, 16f, -16f, 24f);
        SetBodyTint(new Color(1f, 0.95f, 0.85f));
        EndAttack();
        yield return SetAltitude(HoverAlt, 0.5f);
        yield return MoveToGap(8f, 12f, 2.5f);
        yield return Recover(RecoverTime(0.6f));
    }

    // 初回のHPゼロ: 炎になって消え、炎が集まって復活する(HP40%から第二段階)。
    protected override bool OnLethalDamage()
    {
        if (reborn) return false;
        reborn = true;
        StopAllCoroutines();
        DisableCombatParts();
        EndAttack();
        relVelocity = 0f;
        StartCoroutine(Rebirth());
        return true;
    }

    IEnumerator Rebirth()
    {
        invulnerable = true;
        SetHurtboxEnabled(false);
        approachDone = false;
        Sfx(SkyBossSfx.Flame(), 1f);
        Shake(0.12f, 0.4f);
        SkyBossFx.Sparks(CenterWorld, Fire, 24, 1.1f);
        // 身体が炎になって消える
        float t = 0f;
        while (t < 0.6f)
        {
            t += Time.deltaTime;
            float f = t / 0.6f;
            SetBodyTint(Color.Lerp(Color.white, new Color(1f, 0.5f, 0.1f), f));
            SetAlpha(1f - f);
            if (Random.value < 0.6f) SkyDrift.Spawn(BossFx.Orb(), CenterWorld + (Vector3)Random.insideUnitCircle * halfWidth, Vector2.one * Random.Range(0.4f, 0.9f), Fire, new Vector3(0f, 2f, 0f), 0.6f, RenderOrder.CombatFx, true);
            yield return null;
        }
        SetAlpha(0f);
        yield return Wait(0.5f);
        // 炎が集まる
        t = 0f;
        while (t < 1.0f)
        {
            t += Time.deltaTime;
            Vector3 c = CenterWorld;
            if (Random.value < 0.8f)
            {
                Vector3 p = c + (Vector3)(Random.insideUnitCircle.normalized * Random.Range(3f, 6f));
                SkyDrift.Spawn(BossFx.Orb(), p, Vector2.one * Random.Range(0.3f, 0.6f), Fire, (c - p) / 0.5f, 0.5f, RenderOrder.CombatFx, true);
            }
            yield return null;
        }
        // 復活
        SetBodyTint(new Color(1f, 0.95f, 0.85f));
        SetAlpha(1f);
        SetPose(Pose.Landing);
        SkyBossFx.Flash(CenterWorld, new Color(1f, 0.8f, 0.4f, 1f), 7f, 0.35f);
        OneShotSpriteEffect.CreateTweened(BossFx.Ring(), CenterWorld, Fire, 0.6f, 2f, 8f, 0.9f, 0f, default, 0f, RenderOrder.CombatFx, 0.1f);
        Sfx(SkyBossSfx.Roar(), 0.8f);
        Sfx(SkyBossSfx.Flame(), 1f);
        Shake(0.15f, 0.5f);
        RestoreHp(Mathf.RoundToInt(maxHp * 0.4f));
        yield return Wait(0.7f);
        invulnerable = false;
        SetHurtboxEnabled(true);
        approachDone = true;
        StartCoroutine(Ambient());
        yield return AI();
    }
}

// ============ 80,000m 天空大蛇 ============
// 雲の中を泳ぐ翼ある神獣の大蛇。荒野街道の巨大蛇/ヒュドラより遥かに長く、神秘的。
// 雲からの噛みつき(頭部が雲へ消える→別の雲が動く→出現して噛みつき)/身体横断(雲の動きで軌道を予告)/
// 雷ブレス(口元発光→長めの溜め)/雷雲生成(咆哮→複数地点に雷雲→落雷、必ず安全地帯を残す)。
public class SkySerpentBoss : SkyBossBase
{
    BossHitbox bite, cross, breath;
    BossTelegraphMarker biteMark;
    SpriteRenderer mouthGlow;
    int last = -1;
    const float HoverAlt = 2.6f;
    static readonly Color Storm = new Color(0.75f, 0.85f, 1f, 1f);

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Slither;
        hitStopOnHit = 0.04f;
        startGap = 8f; minGap = -26f; maxGap = 28f;
        Vector2 bc = new Vector2(FrontReach + 0.5f, bodyHeight * 0.5f), bs = new Vector2(2.4f, 1.6f);
        bite = NewHitbox("Bite", bc, bs, BossFx.Fang(), new Color(1f, 0.95f, 0.8f, 0.95f));
        biteMark = NewMarker(bc, bs);
        cross = NewHitbox("Cross", Vector2.zero, Vector2.one, BossFx.Slash(), new Color(0.85f, 0.9f, 1f, 0.6f));
        breath = NewHitbox("Breath", Vector2.zero, Vector2.one, SkyBossFx.Beam(), new Color(0.75f, 0.9f, 1f, 0.95f));
        mouthGlow = MakeGlow("MouthGlow", new Color(0.7f, 0.9f, 1f, 0.95f));
        SetAlpha(0f);
        StartCoroutine(Ambient());
    }

    protected override IEnumerator Enter()
    {
        // 遠くの雲の間を長い身体がうねりながら近づいてくる
        yield return DistantApproach(2.3f, startGap, HoverAlt, new Vector2(1.0f, 0.85f), 0.18f, new Color(0.5f, 0.6f, 0.82f, 0.6f));
        Sfx(SkyBossSfx.Roar(), 0.8f);
        approachDone = true;
    }

    IEnumerator Ambient()
    {
        float cloud = 0f;
        while (!IsDead)
        {
            cloud -= Time.deltaTime;
            if (cloud <= 0f && approachDone)
            {
                cloud = 0.16f;
                // 身体にまとわりつく雲
                Vector3 p = CenterWorld + new Vector3(Random.Range(-halfWidth, halfWidth), Random.Range(-0.4f, 0.4f) * bodyHeight, 0f);
                SkyDrift.Spawn(SkyBossFx.CloudPuff(), p, Vector2.one * Random.Range(0.7f, 1.4f), new Color(1f, 1f, 1f, 0.45f), new Vector3(-facing * 1f, 0.3f, 0f), 1.0f, RenderOrder.Boss + 1, true);
            }
            yield return null;
        }
    }

    protected override IEnumerator AI()
    {
        while (true)
        {
            int pick = BossAiUtil.PickNoRepeat(4, ref last);
            if (pick == 0) yield return CloudBite();
            else if (pick == 1) yield return BodyCross();
            else if (pick == 2) yield return ThunderBreath();
            else yield return ThunderClouds();
        }
    }

    IEnumerator Vanish(float dur)
    {
        invulnerable = true;
        SetHurtboxEnabled(false);
        SkyBossFx.Sparks(BodyPoint(0.8f, 0.5f), Color.white, 6, 0.8f);
        for (int i = 0; i < 6; i++) SkyDrift.Spawn(SkyBossFx.CloudPuff(), BodyPoint(Random.Range(-1f, 1f), Random.Range(0.2f, 0.8f)), Vector2.one * Random.Range(1.2f, 2.2f), new Color(1f, 1f, 1f, 0.8f), Vector3.zero, 0.9f, RenderOrder.CombatFx, true);
        float t = 0f;
        while (t < dur) { t += Time.deltaTime; SetAlpha(1f - t / dur); yield return null; }
        SetAlpha(0f);
    }

    IEnumerator Appear(float dur)
    {
        float t = 0f;
        while (t < dur) { t += Time.deltaTime; SetAlpha(t / dur); yield return null; }
        SetAlpha(1f);
        invulnerable = false;
        SetHurtboxEnabled(true);
    }

    IEnumerator CloudBite()
    {
        yield return Vanish(0.3f);                 // 頭部が雲へ消える
        bool behind = Random.value < 0.5f;
        float gap = behind ? -5f : 5f;
        float alt = behind ? 0.6f : 1.8f;
        // 別方向の雲が動く(出現地点の予兆)
        float t = 0f;
        while (t < 0.9f && !IsDead)
        {
            t += Time.deltaTime;
            Vector3 spot = new Vector3(PlayerX + gap, GroundY + alt + bodyHeight * 0.5f, 0f);
            if (Random.value < 0.5f)
            {
                Vector3 p = spot + (Vector3)(Random.insideUnitCircle * 1.6f);
                SkyDrift.Spawn(SkyBossFx.CloudPuff(), p, Vector2.one * Random.Range(0.8f, 1.5f), new Color(0.85f, 0.9f, 1f, 0.8f), (spot - p) * 1.5f + new Vector3(0f, 0f, 0f), 0.6f, RenderOrder.CombatFx, true);
            }
            yield return null;
        }
        worldX = PlayerX + gap;
        yOffset = alt;
        facing = gap > 0f ? -1f : 1f;
        yield return Appear(0.2f);
        yield return Telegraph(0.4f, biteMark);
        Sfx(SkyBossSfx.Whoosh(), 0.8f);
        StartCoroutine(DashMove(0.25f, 13f));
        yield return Strike(bite, 0.25f);
        yield return Recover(0.7f);
        StartCoroutine(SetAltitude(HoverAlt, 0.6f));
        yield return MoveToGap(8f, 9f, 2.5f);
    }

    IEnumerator BodyCross()
    {
        bool low = Random.value < 0.5f;
        yield return Vanish(0.35f);
        Vector2 band = low ? LowBand : HighBand;
        SkyWarnBand.Create(PlayerX - 14f, PlayerX + 16f, band.x, band.y, 1.3f);
        // 雲が帯に沿って流れて軌道を知らせる
        float t = 0f;
        while (t < 1.3f && !IsDead)
        {
            t += Time.deltaTime;
            if (Random.value < 0.8f)
            {
                float y = PlayerGroundY + Random.Range(band.x, band.y);
                SkyDrift.Spawn(SkyBossFx.CloudPuff(), new Vector3(PlayerX + Random.Range(4f, 14f), y, 0f), Vector2.one * Random.Range(0.8f, 1.4f), new Color(0.9f, 0.95f, 1f, 0.65f), new Vector3(-14f, 0f, 0f), 0.8f, RenderOrder.CombatFx, true);
            }
            yield return null;
        }
        if (IsDead) yield break;
        yOffset = (low ? band.y - bodyHeight : band.x) + (PlayerGroundY - GroundY);
        if (low) SetVisualSortingOrder(BehindGround);   // 回廊の下をくぐり、背中だけが回廊をかすめる
        float bottom = low ? bodyHeight - (band.y - band.x) : 0f;
        float top = low ? bodyHeight : bodyHeight * 0.95f;
        cross.Configure(new Vector2(0f, (bottom + top) * 0.5f), new Vector2(halfWidth * 1.8f, top - bottom));
        ConfigureHurtbox(new Vector2(0f, (bottom + top) * 0.5f), new Vector2(halfWidth * 1.6f, Mathf.Max(1f, top - bottom)));
        SetAlpha(1f);
        invulnerable = false;
        SetHurtboxEnabled(true);
        Sfx(SkyBossSfx.Whoosh(), 1f);
        yield return CrossBody(cross, 22f, -22f, 23f);
        EndAttack();
        SetVisualSortingOrder(RenderOrder.Boss);
        ConfigureHurtbox(new Vector2(0f, bodyHeight * 0.5f), new Vector2(halfWidth * 2f * hurtWidthRatio, bodyHeight * hurtHeightRatio));
        yield return Vanish(0.2f);
        worldX = PlayerX + 12f;
        yOffset = HoverAlt;
        yield return Appear(0.3f);
        yield return MoveToGap(8f, 8f, 2f);
    }

    IEnumerator ThunderBreath()
    {
        yield return MoveToGap(9f, 6f, 2f);
        bool low = Random.value < 0.5f;
        Vector2 band = low ? LowBand : new Vector2(HighBand.x, 3.6f);
        float length = Mathf.Max(6f, Gap + 5f);
        breath.Configure(new Vector2(FrontReach + length * 0.5f, (band.x + band.y) * 0.5f - yOffset + (PlayerGroundY - GroundY)), new Vector2(length, band.y - band.x));
        SkyWarnBand.Create(PlayerX - 5f, worldX + facing * FrontReach, band.x, band.y, 1.4f);
        mouthGlow.enabled = true;
        StartCoroutine(FollowGlow(mouthGlow, 0.95f, 0.55f, 0.4f, 2.4f, 1.4f));
        yield return Telegraph(1.4f);
        mouthGlow.enabled = false;
        Sfx(SkyBossSfx.Thunder(), 0.8f);
        yield return Strike(breath, 0.5f, 0.1f);
        yield return Recover(1.1f);
    }

    IEnumerator ThunderClouds()
    {
        yield return Telegraph(0.9f);             // 咆哮
        Sfx(SkyBossSfx.Roar(), 1f);
        Shake(0.08f, 0.4f);
        PlayAttackPose(0.5f);
        // 3つの候補地点のうち2つだけに雷雲(残りの1つ+雷雲の間は必ず安全)
        float[] slots = { -3.2f, 0f, 3.2f };
        int safe = Random.Range(0, 3);
        float baseX = PlayerX;
        int n = 0;
        for (int i = 0; i < 3; i++)
        {
            if (i == safe) continue;
            float warn = 1.3f + n * 0.25f;
            var s = SkyStrike.Create(baseX + slots[i], 1.8f, 5.5f, warn, 0.25f, Storm, SkyStrike.Look.Bolt);
            StartCoroutine(StormCloud(s, warn));
            n++;
        }
        yield return Wait(0.8f);
        yield return Recover(1.0f);
    }

    IEnumerator StormCloud(SkyStrike s, float warn)
    {
        var go = new GameObject("StormCloud");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = SkyBossFx.CloudPuff();
        sr.sortingOrder = RenderOrder.CombatFx;
        float t = 0f;
        while (t < warn + 0.5f && s != null)
        {
            t += Time.deltaTime;
            go.transform.position = new Vector3(s.WorldX, s.GroundY + 5.8f, 0f);
            float grow = Mathf.Clamp01(t / 0.4f);
            go.transform.localScale = new Vector3(3.2f * grow, 2.0f * grow, 1f);
            float flick = t > warn * 0.6f && Random.value < 0.15f ? 1f : 0f;
            sr.color = Color.Lerp(new Color(0.35f, 0.38f, 0.5f, 0.9f), new Color(0.85f, 0.9f, 1f, 1f), flick);
            yield return null;
        }
        Destroy(go);
    }
}

// ============ 90,000m 天界の守護者 ============
// 100,000m直前の最終門番。人型の神聖な存在(白・金・青白い光)。天空回廊の最深部を守る者。
// 理不尽な速さではなく「予備動作は見える、しかし攻撃の種類が多い」総合試験:
// 光剣(武器を後方へ→発光→高速斬撃)/天空斬撃(剣を上へ→チャージ→斬撃波、低い波=ジャンプ・高い波=そのまま)/
// 空中攻撃(上空へ→一瞬停止→斜め下へ)/光柱(武器を掲げる→周辺の地面に予告→光柱、安全地帯を必ず残す)。
public class CelestialGuardianBoss : SkyBossBase
{
    BossHitbox sword, dive;
    BossTelegraphMarker swordMark;
    SpriteRenderer swordGlow;
    int last = -1;
    const float HoverAlt = 0.3f;
    static readonly Color Holy = new Color(1f, 0.93f, 0.7f, 1f);

    protected override void OnInit()
    {
        locoStyle = LocoStyle.Cloth;
        interruptible = true;
        hitStopOnHit = 0.035f;
        startGap = 7f; minGap = -8f; maxGap = 16f;
        SetBodyTint(new Color(1f, 0.98f, 0.92f));
        Vector2 sc = new Vector2(FrontReach + 0.7f, bodyHeight * 0.45f), ss = new Vector2(2.6f, 1.8f);
        sword = NewHitbox("LightSword", sc, ss, BossFx.Slash(), new Color(1f, 0.95f, 0.7f, 0.95f));
        swordMark = NewMarker(sc, ss);
        dive = NewHitbox("AerialDive", new Vector2(FrontReach * 0.3f, bodyHeight * 0.4f), new Vector2(2.4f, bodyHeight * 0.9f), BossFx.Slash(), new Color(1f, 0.95f, 0.75f, 0.9f));
        swordGlow = MakeGlow("SwordGlow", new Color(1f, 0.95f, 0.75f, 0.95f));
        SetAlpha(0f);
        StartCoroutine(Ambient());
    }

    protected override IEnumerator Enter()
    {
        // 天から一筋の光 → 光の中から降りてくる
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 top = cam.ViewportToWorldPoint(new Vector3(0.78f, 1.1f, 10f));
            if (pc != null) top.x += PlayerX - pc.transform.position.x; // マルチ: 狙いの相手の画面に
            var go = new GameObject("HeavenLight");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SkyBossFx.Beam();
            sr.sortingOrder = -30;
            sr.color = new Color(1f, 0.95f, 0.75f, 0.55f);
            go.transform.position = new Vector3(top.x, top.y - 7f, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            go.transform.localScale = new Vector3(16f, 2.2f, 1f);
            go.AddComponent<SkyFadeOut>().Init(2.4f, false);
        }
        Sfx(SkyBossSfx.Whoosh(), 0.6f);
        yield return DistantApproach(1.9f, startGap, HoverAlt, new Vector2(0.8f, 1.1f), 0.35f, new Color(1f, 0.95f, 0.8f, 0.75f));
        SkyBossFx.Flash(CenterWorld, Holy, 5f, 0.3f);
        SetPose(Pose.Landing);
        yield return Wait(0.5f);
        approachDone = true;
    }

    IEnumerator Ambient()
    {
        float mote = 0f;
        while (!IsDead)
        {
            if (approachDone && !windingUp && pose != Pose.Attack && yOffset < 1f) yOffset = HoverAlt + 0.15f * Mathf.Sin(Time.time * 2f);
            mote -= Time.deltaTime;
            if (mote <= 0f && approachDone)
            {
                mote = 0.1f;
                Vector3 p = CenterWorld + new Vector3(Random.Range(-halfWidth, halfWidth), Random.Range(-0.5f, 0.5f) * bodyHeight, 0f);
                SkyDrift.Spawn(BossFx.Orb(), p, Vector2.one * Random.Range(0.1f, 0.22f), new Color(1f, 0.95f, 0.75f, 0.9f), new Vector3(0f, 1f, 0f), 1.1f, RenderOrder.Boss + 1, true);
            }
            yield return null;
        }
    }

    float Recovery(float normal) => Hp <= maxHp / 2 ? normal * 0.8f : normal;

    protected override IEnumerator AI()
    {
        while (true)
        {
            int pick = BossAiUtil.PickNoRepeat(4, ref last);
            if (pick == 0) yield return LightSword();
            else if (pick == 1) yield return SkySlash();
            else if (pick == 2) yield return AerialStrike();
            else yield return LightPillars();
        }
    }

    IEnumerator LightSword()
    {
        yield return Approach(1.4f, 3.2f, 5f);
        swordGlow.enabled = true;
        StartCoroutine(FollowGlow(swordGlow, 0.2f, 0.5f, 0.4f, 1.5f, 0.6f));
        yield return Telegraph(0.6f, swordMark);   // 武器を後方へ → 発光
        swordGlow.enabled = false;
        if (interrupted) { yield return Stagger(0.9f); yield break; }
        Sfx(SkyBossSfx.Whoosh(), 0.8f);
        StartCoroutine(DashMove(0.18f, 10f));
        yield return Strike(sword, 0.22f, 0.05f);
        yield return Recover(Recovery(0.75f));
    }

    IEnumerator SkySlash()
    {
        bool high = Random.value < 0.4f;
        swordGlow.enabled = true;
        StartCoroutine(FollowGlow(swordGlow, 0.1f, 1.0f, 0.5f, 2.2f, 0.9f));   // 剣を上へ → 光をチャージ
        yield return Telegraph(0.9f);
        swordGlow.enabled = false;
        if (interrupted) { yield return Stagger(0.9f); yield break; }
        Vector3 from = FrontWorld(0.5f, 0f);
        from.y = PlayerGroundY + (high ? 2.8f : 0.65f);
        var w = BossProjectile.Create(BossFx.Slash(), new Color(1f, 0.93f, 0.65f, 1f), from, new Vector2(1.6f * facing, high ? 1.6f : 1.2f), new Vector2(facing * 10f, 0f), 2.2f, RenderOrder.Boss + 1);
        if (!high) { w.hugGround = true; w.groundOffset = 0.65f; }
        Sfx(SkyBossSfx.Whoosh(), 1f);
        PlayAttackPose(0.35f);
        yield return Wait(0.35f);
        yield return Recover(Recovery(0.9f));
    }

    IEnumerator AerialStrike()
    {
        StartCoroutine(SetAltitude(5f, 0.35f));      // 上空へ高速移動
        yield return MoveToGap(5.5f, 9f, 0.6f);
        float landGap = 0.6f;
        SkyStrike.Create(PlayerX + landGap, 2.4f, 1.8f, 0.8f, 0.2f, Holy, SkyStrike.Look.Burst);
        windingUp = true; facingLocked = true; SetPose(Pose.Windup);
        float t = 0f;
        while (t < 0.5f && !IsDead) { t += Time.deltaTime; windupProgress = t / 0.5f; yield return null; }   // 一瞬停止
        windingUp = false; windupProgress = 0f;
        if (IsDead) yield break;
        Sfx(SkyBossSfx.Whoosh(), 1f);
        float dur = 0.3f;
        StartCoroutine(SetAltitude(HoverAlt, dur));
        StartCoroutine(DashMove(dur, Mathf.Abs(Gap - landGap) / dur));   // 斜め下へ
        yield return Strike(dive, dur, 0.1f, 0.04f);
        EndAttack();
        SetPose(Pose.Landing);
        yield return Wait(Recovery(0.9f));
    }

    IEnumerator LightPillars()
    {
        yield return Telegraph(1.0f);                 // 武器を掲げる
        PlayAttackPose(0.6f);
        float[] slots = { -2.8f, 0f, 2.8f };
        int safe = Random.Range(0, 3);
        float baseX = PlayerX;
        int n = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            if (i == safe) continue;
            SkyStrike.Create(baseX + slots[i], 1.5f, 6f, 1.1f + n * 0.15f, 0.4f, Holy, SkyStrike.Look.Pillar);
            n++;
        }
        yield return Wait(0.7f);
        yield return Recover(Recovery(1.1f));
    }
}
