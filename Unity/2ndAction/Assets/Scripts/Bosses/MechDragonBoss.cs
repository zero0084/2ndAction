using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ============ 希少な強敵 機械竜(メカドラゴン)— ラスダンだけ(2026-10-13) ============
// 「遭遇したら運が悪かったと思うほど強い」。HP/被弾は全ボスの上位(SpecForSky / BossBattleTuning の MechDragon)。
// 近代兵器の機械竜: レーザー(照準 → 発射の直前に狙いを固定 → 照射 → 冷却の隙)/ バルカン(砲身の回転 → 連射。射撃の高さは固定、
// 弾幕の切れ目で避ける)/ 空爆(上空 → 着弾地点の予告 → 投下 → 爆発。必ず足場のある安全な地点を1つ残す)。
// 第2段階: 高い帯のなぎ払い(紫 = 地面にいる)とバルカンの帯の切り替え。第3段階: なぎ払いが2回・空爆の地点が増える。
// 必殺技 OVERKILL PROTOCOL: なぎ払い → 空爆 → 固定照準のレーザー2〜3連 → 冷却で長く降りてくる(長い反撃の機会)。
// 守ること: 強攻撃の後は低く降りて隙を作る(近接が届く)/ 予兆の無い大ダメージは無い / 1回の攻撃のダメージは1回(BossHitbox)。
// 絵: Resources/Bosses/MechDragon/<姿勢>.png(全コマ同じキャンバス・足元の中央が基準)。無い姿勢は idle で代用。
public partial class MechDragonBoss : SkyBossBase
{
    static readonly string[] PoseNames = { "idle", "move", "laser_charge", "laser_fire", "laser_cool", "vulcan_prep", "vulcan_fire", "vulcan_end", "bomb_prep", "bomb_drop", "hurt", "break", "recover", "defeat", "defeat2" };
    static Dictionary<string, Sprite> art;
    static Sprite bombArt, bulletArt, reticleArt, muzzleArt, blastArt;
    static readonly Color Laser = new Color(0.55f, 0.85f, 1f, 1f);
    static readonly Color Muzzle = new Color(1f, 0.85f, 0.35f, 1f);
    static readonly Color Alarm = new Color(1f, 0.25f, 0.2f, 1f);
    const float HoverAlt = 0.9f;      // 普段の高さ(近接が届く。飛び続けて攻撃できない状態にしない)
    const float LowAlt = 0.15f;       // 隙の高さ(地面の近くまで降りてくる)

    BossHitbox beam, sweep;
    SpriteRenderer mouthGlow, gunGlow;
    string mpose;                      // 今の技の姿勢(null = 通常)
    int last = -1;
    readonly List<GameObject> spawnedFx = new List<GameObject>(); // 照準など(撃破/中断で消す)

    public static int LaserShots, SweepShots, VulcanBursts, BombsDropped, Openings; // 確認用

    // ---- 絵(BossManager.SpawnSkyBossImpl が Init の前に呼ぶ) ----
    public static void LoadArt(WildBossBase boss)
    {
        if (art == null)
        {
            art = new Dictionary<string, Sprite>();
            foreach (var n in PoseNames) { var s = Resources.Load<Sprite>("Bosses/MechDragon/" + n); if (s != null) art[n] = s; }
            bombArt = Resources.Load<Sprite>("Bosses/MechDragon/fx_bomb");
            bulletArt = Resources.Load<Sprite>("Bosses/MechDragon/fx_bullet");
            reticleArt = Resources.Load<Sprite>("Bosses/MechDragon/fx_reticle");
            blastArt = Resources.Load<Sprite>("Bosses/MechDragon/fx_explosion");
            muzzleArt = Resources.Load<Sprite>("Effects/muzzleflash");
        }
        if (art.TryGetValue("idle", out var idle)) boss.idleSprite = idle;
        if (art.TryGetValue("move", out var mv)) boss.moveSprite = mv;
        if (art.TryGetValue("laser_charge", out var w)) boss.windupSprite = w;   // マルチの相手側(姿勢だけ同期)はこの4枚で見せる
        if (art.TryGetValue("laser_fire", out var a)) boss.attackSprite = a;
        boss.artFacesLeft = true;      // 頭が画面左向きに描かれている
        boss.hurtWidthRatio = 0.42f;   // 翼を広げた絵なので、被弾範囲は胴体(翼は含めない)
        boss.hurtHeightRatio = 0.8f;
    }

    static Sprite Art(string n) => n != null && art != null && art.TryGetValue(n, out var s) ? s : null;
    // 2026-10-19: 撃破の絵(defeat → defeat2)。以前は撃破の後に姿勢の切り替えが止まるため、用意していた絵が出ていなかった
    public override Sprite[] DeathFrames
    {
        get
        {
            Sprite a = Art("defeat"), b = Art("defeat2");
            if (a == null && b == null) return base.DeathFrames;
            if (a == null) return new[] { b };
            if (b == null) return new[] { a };
            return new[] { a, b };
        }
    }

    float deadAt = -1f, recoverUntil;
    bool wasBroken;

    protected override Sprite PoseOverride()
    {
        if (IsDead)
        {
            // 撃破: 爆発しながら崩れる → 煙を上げて落ちる
            if (deadAt < 0f) deadAt = Time.time;
            return (Time.time - deadAt > 0.7f ? Art("defeat2") : null) ?? Art("defeat") ?? Art("break");
        }
        if (Broken) { wasBroken = true; return Art("break"); }
        if (wasBroken) { wasBroken = false; recoverUntil = Time.time + 0.8f; }   // BREAK 明け: 体勢を立て直す絵を少しだけ
        if (Time.time < recoverUntil && mpose == null) return Art("recover");
        if (mpose != null) return Art(mpose);
        if (HitFlashTime > 0.05f) return Art("hurt");
        return null;
    }

    void SetMech(string p) { mpose = p; if (p == null) holdGap = float.NaN; }

    // ラスダンの低い天井: 体(翼の先まで)が天井へ入らない高さに抑える(2026-10-13: 天井の石に翼が埋まっていた)。
    // 毎フレーム、位置を決める直前(ApplyTransform の前)に掛かる。床より下へは下げない
    public static int CeilingClamps; // 確認用
    float ceilScale = 1f;
    public float VisualScale => ceilScale;
    // 体の上の位置(BodyPoint と同じ。低い天井で小さく見せている時は、その大きさに合わせる: 口/銃口/爆弾倉と攻撃の出る所を合わせる)
    Vector3 MP(float fwd, float h) => new Vector3(worldX + facing * halfWidth * fwd * ceilScale, GroundY + yOffset + bodyHeight * h * ceilScale, 0f);
    protected override void OnBattleTick(float dt)
    {
        base.OnBattleTick(dt);
        if (IsDead) return;
        var tm = TerrainManager.Instance;
        // 体の幅の範囲で一番低い天井(少し先も見る: 低い区間へ入る前に縮み始める)
        float? c = null;
        if (tm != null)
            for (int i = -2; i <= 3; i++)
            {
                float? ci = tm.GetEffectiveCeilingHeightAt(worldX + halfWidth * 0.5f * i);
                if (ci.HasValue && (!c.HasValue || ci.Value < c.Value)) c = ci;
            }
        float need = bodyHeight * 0.97f;
        float target = 1f;
        if (c.HasValue)
        {
            float room = c.Value - GroundY - 0.2f;
            if (room < need) target = Mathf.Clamp(room / need, 0.7f, 1f); // 体より低い天井(約4.4mの所もある): 少し小さく見せて入れる
        }
        ceilScale = Mathf.MoveTowards(ceilScale, target, (target < ceilScale ? 2.5f : 0.8f) * dt);
        extraScale = new Vector2(ceilScale, ceilScale);
        if (!c.HasValue) return;
        float maxAlt = c.Value - GroundY - need * ceilScale - 0.2f;
        if (yOffset > maxAlt) { yOffset = Mathf.Max(0f, maxAlt); CeilingClamps++; }
    }

    // 撃つ間の間合い: 構え/発射の間もプレイヤーの前(holdGap)に留まる(止まっていると走るプレイヤーに追いつかれ、
    // 頭上や後ろから撃つことになる。2026-10-13 の確認で判明)。NaN = 保たない(接近/冷却/BREAK)
    float holdGap = float.NaN, holdVel;

    // 飛んでいるので、足もとの床の細かい段差(階段・継ぎ目)に1フレームで合わせない: 床の高さをなめらかに追う(上下の震えの対策)
    float smoothGround; int smoothGroundFrame = -1; bool smoothGroundInit;
    protected override float TerrainGround(float x)
    {
        float g = base.TerrainGround(x);
        if (!approachDone || IsDead) { smoothGround = g; smoothGroundInit = true; return g; }
        if (!smoothGroundInit) { smoothGround = g; smoothGroundInit = true; }
        if (smoothGroundFrame != Time.frameCount)
        {
            smoothGroundFrame = Time.frameCount;
            smoothGround = Mathf.Lerp(smoothGround, g, 1f - Mathf.Exp(-5f * Time.deltaTime));
            if (smoothGround < g) smoothGround = g; // 上り(段差を上がる)はすぐ合わせる(遅れると体が床に埋まった。2026-10-13)。なめらかにするのは下りだけ
            if (Mathf.Abs(g - smoothGround) > 4f) smoothGround = g; // 大きな高低差(崖の上下)は追いつく
        }
        return smoothGround;
    }

    // 確認用の記録(開発版のみ)
    static void MLog(string m) { if (Debug.isDebugBuild) Debug.Log("[Mech] " + m); }

    protected override void OnInit()
    {
        HazardBattleSetup();
        restAltitude = HoverAlt;
        locoStyle = LocoStyle.Cloth;
        hitStopOnHit = 0.03f;
        startGap = 8f; minGap = -6f; maxGap = 15f;
        // 被弾範囲: 胴体(翼は含めない)。高さの中央を胴体に
        ConfigureHurtbox(new Vector2(0f, bodyHeight * 0.52f), new Vector2(Mathf.Max(1.6f, halfWidth * 2f * hurtWidthRatio), bodyHeight * 0.78f));
        beam = NewHitbox("Laser", Vector2.zero, new Vector2(1f, 0.7f), SkyBossFx.Beam(), new Color(0.75f, 0.92f, 1f, 1f));
        sweep = NewHitbox("LaserSweep", Vector2.zero, new Vector2(1f, 0.55f), SkyBossFx.Beam(), new Color(0.8f, 0.6f, 1f, 1f));
        mouthGlow = MakeGlow("MouthGlow", new Color(0.6f, 0.9f, 1f, 0.95f));
        gunGlow = MakeGlow("GunGlow", new Color(1f, 0.85f, 0.4f, 0.95f));
        SetAlpha(0f);
        StartCoroutine(Ambient());
    }

    // ---- 登場: 警告(危険な相手だと伝える)→ 遠くから飛来 ----
    protected override IEnumerator Enter()
    {
        BossBattleHud.Banner("WARNING!  メカドラゴン接近!", Alarm, 2.6f);
        CaveDarkness.Set(0.25f);
        for (int i = 0; i < 3; i++)
        {
            SkyBossFx.Flash(new Vector3(PlayerX + 4f, PlayerGroundY + 3f, 0f), Alarm, 9f, 0.22f);
            Sfx(SkyBossSfx.Thunder(), 0.5f);
            yield return Wait(0.45f);
        }
        Sfx(SkyBossSfx.Roar(), 0.9f);
        yield return DistantApproach(1.8f, startGap, HoverAlt, new Vector2(0.95f, 0.85f), 0.4f, new Color(0.35f, 0.5f, 0.75f, 0.7f));
        CaveDarkness.Set(0f);
        SkyBossFx.Flash(CenterWorld, Laser, 6f, 0.25f);
        Shake(0.12f, 0.35f);
        approachDone = true;
    }

    IEnumerator Ambient()
    {
        float spark = 0f;
        while (!IsDead)
        {
            if (approachDone && !windingUp && pose != Pose.Attack && !Broken && mpose == null && float.IsNaN(holdGap)) yOffset = Mathf.Lerp(yOffset, restAltitude + 0.18f * Mathf.Sin(Time.time * 2.2f), 0.1f);
            spark -= Time.deltaTime;
            // 間合いを保つ速さはなめらかに変える(プレイヤーの踏み込みの細かい前後に毎フレーム付いていくと、機体が前後に震えた)
            float holdTarget = !float.IsNaN(holdGap) && approachDone && !Broken && !IsDead ? Mathf.Clamp((holdGap - Gap) * 2.5f, -8f, 8f) : 0f;
            holdVel = Mathf.Lerp(holdVel, holdTarget, 1f - Mathf.Exp(-6f * Time.deltaTime));
            if (Mathf.Abs(holdVel) > 0.01f && !Broken && !IsDead) worldX += holdVel * Time.deltaTime;
            if (spark <= 0f && approachDone)
            {
                spark = 0.12f;
                Vector3 p = MP(Random.Range(-0.6f, 0.4f), Random.Range(0.3f, 0.8f));
                SkyDrift.Spawn(BossFx.Orb(), p, Vector2.one * Random.Range(0.08f, 0.16f), new Color(0.5f, 0.85f, 1f, 0.9f), new Vector3(1.5f, -0.5f, 0f), 0.5f, RenderOrder.Boss + 1, true);
            }
            yield return null;
        }
        ClearFx();
    }

    float Rec(float s) => s * RecoverMul * (Hp <= maxHp / 2 ? 0.85f : 1f);

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (UltimateReady(2)) { yield return OverkillProtocol(); continue; }
            if (SpecialReady(SpecialPhase)) { MarkSpecial(); yield return (Phase >= 2 ? Barrage() : AirRaid()); continue; }
            int n = Phase >= 2 ? 4 : 3;
            int pick = BossAiUtil.PickNoRepeat(n, ref last);
            if (pick == 0) yield return LaserShot(1);
            else if (pick == 1) yield return Vulcan();
            else if (pick == 2) yield return AirRaid();
            else yield return HighSweep(Enraged ? 2 : 1);
        }
    }

    // ===================================================================== レーザー(固定照準)
    // 照準が狙いを追う(青)→ 発射の0.4秒前に固定(赤、音)→ 固定した方向へ照射 → 冷却(低く降りて隙)
    IEnumerator LaserShot(int shots, bool ult = false)
    {
        holdGap = 9f;
        yield return SetAltitude(HoverAlt + 1.4f, 0.35f);
        yield return MoveToGap(9f, 9f, 1.0f);
        for (int s = 0; s < shots && !IsDead; s++)
        {
            SetMech("laser_charge");
            mouthGlow.enabled = true;
            StartCoroutine(FollowGlow(mouthGlow, 0.47f, 0.68f, 0.3f, 1.4f, 0.8f));
            var ret = MakeReticle();
            float charge = (Enraged ? 0.95f : 1.15f) * (ult ? 0.9f : 1f), lockAt = charge - 0.4f;
            Vector3 aim = Vector3.zero; bool locked = false;
            windingUp = true; facingLocked = true; SetPose(WildBossBase.Pose.Windup);
            float t = 0f;
            while (t < charge && !IsDead && !interrupted)
            {
                t += Time.deltaTime; windupProgress = t / charge;
                if (!locked)
                {
                    aim = new Vector3(PlayerX, (player != null ? player.position.y : PlayerGroundY) + 0.75f, 0f);
                    if (t >= lockAt) { locked = true; Sfx(SkyBossSfx.Impact(), 0.5f); }
                }
                if (ret != null) { ret.transform.position = aim; ret.color = reticleArt != null ? (locked ? Color.white : new Color(1f, 1f, 1f, 0.55f)) : (locked ? new Color(1f, 0.25f, 0.2f, 0.95f) : new Color(0.5f, 0.85f, 1f, 0.8f)); ret.transform.localScale = Vector3.one * (locked ? 1.0f : 1.3f - 0.3f * windupProgress) * (reticleArt != null ? 1.3f / Mathf.Max(0.01f, reticleArt.bounds.size.x) : 1.4f); }
                yield return null;
            }
            windingUp = false; windupProgress = 0f;
            mouthGlow.enabled = false;
            if (ret != null) Destroy(ret.gameObject);
            if (IsDead) yield break;
            if (interrupted) { SetMech(null); yield return Stagger(0.9f); yield break; }
            // 照射(固定した方向へ。地面で止まる)
            SetMech("laser_fire");
            SetPose(WildBossBase.Pose.Attack);
            LaserShots++;
            Sfx(SkyBossSfx.Thunder(), 0.9f);
            Vector3 from = MP(0.6f, 0.68f);
            Vector3 dir = (aim - from).normalized;
            float len = 30f;
            float gy = PlayerGroundY;
            if (dir.y < -0.05f) len = Mathf.Min(len, (from.y - gy) / -dir.y + 0.4f); // 地面に当たる所まで
            Vector3 to = from + dir * len;
            beam.damageAmount = ult ? Dmg(true) : HitDmg;
            yield return FireBeam(beam, from, to, 0.7f, 0.45f);
            OneShotSpriteEffect.CreateTweened(BossFx.Ring(), to, Laser, 0.35f, 0.4f, 2.4f, sortingOrder: RenderOrder.CombatFx);
            SkyBossFx.Sparks(to, Laser, 10, 0.7f);
            if (s + 1 < shots) yield return Wait(0.35f);
        }
        // 冷却: 低く降りて近づく(近接が届く隙)
        if (!ult) yield return Cooldown(1.4f);
        SetMech(null); EndAttack();
    }

    // 照射: 判定(BossHitbox)を口の位置からの線に合わせる(1回の照射でダメージは最大1回)
    IEnumerator FireBeam(BossHitbox hb, Vector3 from, Vector3 to, float thick, float dur)
    {
        Vector3 d = to - from;
        float len = d.magnitude;
        hb.Configure(Vector2.zero, new Vector2(len, thick));
        StartCoroutine(hb.Strike(facing, dur, false));
        float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        float t = 0f;
        while (t < dur && !IsDead)
        {
            hb.transform.position = (from + to) * 0.5f;
            hb.transform.rotation = Quaternion.Euler(0f, 0f, ang);
            t += Time.deltaTime;
            yield return null;
        }
        hb.Deactivate();
        hb.transform.localRotation = Quaternion.identity;
    }

    IEnumerator Cooldown(float seconds)
    {
        SetMech("laser_cool");
        holdGap = float.NaN;
        Openings++;
        StartCoroutine(Steam(seconds));
        StartCoroutine(SetAltitude(LowAlt, 0.35f));
        yield return Approach(2.6f, 6f, 1.0f);
        yield return Recover(Rec(seconds));
        yield return SetAltitude(HoverAlt, 0.4f);
    }

    IEnumerator Steam(float seconds)
    {
        float t = 0f;
        while (t < seconds && !IsDead)
        {
            Vector3 p = MP(Random.Range(0.3f, 0.7f), Random.Range(0.6f, 0.85f));
            SkyDrift.Spawn(BossFx.Orb(), p, Vector2.one * Random.Range(0.25f, 0.45f), new Color(0.92f, 0.95f, 1f, 0.55f), new Vector3(Random.Range(-0.4f, 0.4f), 1.6f, 0f), 0.7f, RenderOrder.Boss + 1, true);
            t += 0.08f;
            yield return new WaitForSeconds(0.08f);
        }
    }

    // ===================================================================== 高い帯のなぎ払い(第2段階〜)
    // 紫の帯(= 地面にいれば当たらない)を予告 → 口からの光線が上から下へ帯の中だけを払う。跳ぶと当たる
    IEnumerator HighSweep(int times)
    {
        MLog($"HighSweep x{times} ult={UltimateRunning}");
        holdGap = 10f;
        yield return SetAltitude(HoverAlt + 2.2f, 0.35f);
        MLog($"sweep pre gap={Gap:F1}");
        yield return MoveToGap(10f, 9f, 1.0f);
        MLog($"sweep moved gap={Gap:F1} alt={yOffset:F1}");
        for (int i = 0; i < times && !IsDead; i++)
        {
            SetMech("laser_charge");
            mouthGlow.enabled = true;
            StartCoroutine(FollowGlow(mouthGlow, 0.47f, 0.68f, 0.3f, 1.6f, 0.9f));
            LaneWarn(-2f, 18f, HighBand.x, HighBand.y, 1.0f, HighLaneColor);
            yield return Telegraph(1.0f);
            MLog($"sweep telegraphed gap={Gap:F1}");
            mouthGlow.enabled = false;
            if (IsDead) yield break;
            if (interrupted) { SetMech(null); yield return Stagger(0.9f); yield break; }
            SetMech("laser_fire");
            SweepShots++;
            Sfx(SkyBossSfx.Thunder(), 0.9f);
            Vector3 from = MP(0.6f, 0.68f);
            float gy = PlayerGroundY;
            float dur = 0.8f;
            sweep.damageAmount = HitDmg;
            sweep.Configure(Vector2.zero, new Vector2(1f, 0.5f));
            float t = 0f;
            bool started = false;
            while (t < dur && !IsDead)
            {
                float h = Mathf.Lerp(HighBand.y - 0.2f, HighBand.x + 0.25f, t / dur); // 帯の上から下へ(帯の下端より下へは来ない)
                Vector3 to = new Vector3(PlayerX - 6f, gy + h, 0f);
                Vector3 mid = (from + to) * 0.5f; Vector3 d = to - from;
                if (!started) { sweep.Configure(Vector2.zero, new Vector2(d.magnitude, 0.5f)); StartCoroutine(sweep.Strike(facing, dur, false)); started = true; }
                sweep.transform.position = mid;
                sweep.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                if (t == 0f) MLog($"sweep fire from={from} to={to} playerX={PlayerX:F1} active={sweep.IsActive}");
                t += Time.deltaTime;
                yield return null;
            }
            sweep.Deactivate(); sweep.transform.localRotation = Quaternion.identity;
            yield return Wait(0.3f);
        }
        yield return Cooldown(1.2f);
        SetMech(null); EndAttack();
    }

    // ===================================================================== バルカン
    // 両肩の砲身が回る(予告)→ 帯の予告(赤 = 低い → 跳ぶ / 紫 = 高い → 地面にいる)→ その高さへ連射(向きは固定、追わない)
    // 1回の連射ごとに切れ目(0.45秒)。第1段階は同じ帯、第2段階からは帯が切り替わる
    IEnumerator Vulcan()
    {
        holdGap = 8f;
        yield return SetAltitude(HoverAlt + 0.6f, 0.3f);
        yield return MoveToGap(8f, 9f, 1.0f);
        SetMech("vulcan_prep");
        gunGlow.enabled = true;
        StartCoroutine(FollowGlow(gunGlow, 0.6f, 0.68f, 0.2f, 0.9f, 0.6f));
        yield return Telegraph(0.85f);
        gunGlow.enabled = false;
        if (IsDead) yield break;
        if (interrupted) { SetMech(null); yield return Stagger(0.9f); yield break; }
        int bursts = Enraged ? 4 : 3;
        bool low = Random.value < 0.5f;
        for (int b = 0; b < bursts && !IsDead; b++)
        {
            if (Phase >= 2 && b > 0) low = !low;
            Vector2 band = low ? new Vector2(0.25f, 1.1f) : new Vector2(2.0f, 3.0f);
            LaneWarn(-1f, 16f, band.x, band.y, 0.45f, low ? LowLaneColor : HighLaneColor);
            SetMech("vulcan_prep");
            yield return Wait(0.45f);
            if (IsDead) yield break;
            SetMech("vulcan_fire");
            VulcanBursts++;
            float gy = PlayerGroundY;
            for (int i = 0; i < 6 && !IsDead; i++)
            {
                Vector3 m = MP(0.72f, 0.68f);
                float yy = gy + Mathf.Lerp(band.x, band.y, (i % 3) / 2f);
                Vector3 from = new Vector3(m.x, Mathf.Lerp(m.y, yy, 0.85f), 0f);
                BossProjectile pj;
                if (bulletArt != null)
                {
                    // 絵は弾頭が左・後ろへ光の尾。長さ 1.1m、当たり判定は弾頭側の 7割(尾の先は当たらない)
                    Vector2 bs = bulletArt.bounds.size;
                    float k = 1.1f / Mathf.Max(0.01f, bs.x);
                    pj = BossProjectile.Create(bulletArt, Color.white, from, new Vector2(k, k), new Vector2(facing * 16f, (yy - from.y) * 3f), 2.4f, RenderOrder.Boss + 1);
                    var sr = pj.GetComponent<SpriteRenderer>(); sr.flipX = facing > 0f;
                    var c = pj.GetComponent<BoxCollider2D>();
                    c.size = new Vector2(bs.x * 0.7f, bs.y * 0.6f);
                    c.offset = new Vector2(bs.x * 0.15f * facing, 0f);
                }
                else pj = BossProjectile.Create(BossFx.Orb(), Muzzle, from, new Vector2(0.45f, 0.2f), new Vector2(facing * 16f, (yy - from.y) * 3f), 2.4f, RenderOrder.Boss + 1);
                pj.damageAmount = HitDmg;
                pj.hugGround = false;
                OneShotSpriteEffect.CreateTweened(muzzleArt != null ? muzzleArt : BossFx.Orb(), m, Muzzle, 0.06f, 0.5f, 1.0f, sortingOrder: RenderOrder.CombatFx);
                if (i % 2 == 0) Sfx(SkyBossSfx.Impact(), 0.25f);
                Shake(0.03f, 0.05f);
                yield return Wait(0.07f);
            }
            SetMech("vulcan_end");
            yield return Wait(0.45f); // 弾幕の切れ目
        }
        SetMech("vulcan_end");
        StartCoroutine(Smoke(0.9f));
        yield return Recover(Rec(0.9f));
        SetMech(null); EndAttack();
    }

    IEnumerator Smoke(float seconds)
    {
        float t = 0f;
        while (t < seconds && !IsDead)
        {
            SkyDrift.Spawn(BossFx.Orb(), MP(0.6f, 0.7f), Vector2.one * Random.Range(0.2f, 0.35f), new Color(0.55f, 0.55f, 0.6f, 0.6f), new Vector3(0.6f, 1.2f, 0f), 0.8f, RenderOrder.Boss + 1, true);
            t += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }
    }

    // ===================================================================== 空爆
    // 上空へ → 爆弾倉が開く(予告)→ 着弾地点の予告(地面の光)→ 爆弾が落ちて爆発。必ず足場のある安全な地点を1つ残す
    IEnumerator AirRaid()
    {
        SetMech("bomb_prep");
        StartCoroutine(SetAltitude(HoverAlt + 3.2f, 0.5f));
        holdGap = 2.5f;
        yield return MoveToGap(2.5f, 10f, 1.2f);
        yield return Telegraph(0.7f);
        if (IsDead) yield break;
        if (interrupted) { SetMech(null); yield return Stagger(0.9f); yield break; }
        SetMech("bomb_drop");
        float[] slots = Enraged ? new[] { -6f, -3f, 0f, 3f, 6f } : new[] { -3.2f, 0f, 3.2f };
        int safe = PickSafeSlot(slots);
        float baseX = PlayerX;
        int n = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            if (i == safe) continue;
            float x = baseX + slots[i];
            var st = SkyStrike.Create(x, 2.4f, 2.4f, 1.0f + n * 0.14f, 0.3f, Muzzle, SkyStrike.Look.Burst);
            st.incomingHeight = Mathf.Max(3f, yOffset + bodyHeight * 0.4f);
            float slotRel = slots[i];
            st.onFire = p => { Blast(p); Shake(0.08f, 0.15f); Sfx(SkyBossSfx.Impact(), 0.7f); LogBlast(p, slotRel, slots[safe]); };
            StartCoroutine(DropBomb(x, 1.0f + n * 0.14f));
            BombsDropped++;
            n++;
        }
        yield return Wait(1.5f);
        StartCoroutine(SetAltitude(HoverAlt, 0.5f));
        yield return Recover(Rec(0.8f));
        SetMech(null); EndAttack();
    }

    void Blast(Vector3 p)
    {
        if (blastArt != null)
        {
            float k = 2.6f / Mathf.Max(0.01f, blastArt.bounds.size.x);
            OneShotSpriteEffect.CreateTweened(blastArt, p + Vector3.up * 1.0f, Color.white, 0.5f, k * 0.6f, k, 1f, 0f, new Vector3(0f, 0.6f, 0f), Random.Range(-12f, 12f), RenderOrder.CombatFx, 0.35f);
            ExplosionEffect.Create(BossFx.Orb(), p + Vector3.up * 0.6f, new Color(1f, 0.6f, 0.2f), 6, 0.4f, 0.7f, 0.9f, RenderOrder.CombatFx);
        }
        else ExplosionEffect.Create(BossFx.Orb(), p + Vector3.up * 0.6f, new Color(1f, 0.6f, 0.2f), 14, 0.6f, 0.9f, 1.2f, RenderOrder.CombatFx);
    }

    // 確認用: 爆発の瞬間のプレイヤーの位置(爆発の柱の中か / 跳んで上を越えたか / 天井の高さ / 足もとが穴か)
    public static int BlastsOverPlayer, BlastsJumpedOver;
    void LogBlast(Vector3 p, float slotRel, float safeRel)
    {
        if (player == null) return;
        float px = player.position.x, dx = px - p.x;
        if (Mathf.Abs(dx) > 1.2f + 0.4f) return; // 柱(幅2.4m)の外
        BlastsOverPlayer++;
        var tm = TerrainManager.Instance;
        float? g = tm != null ? tm.GetHeightAt(px) : null;
        float? c = tm != null ? tm.GetEffectiveCeilingHeightAt(px) : null;
        float dy = g.HasValue ? player.position.y - g.Value : -99f;
        if (dy > 2.4f) BlastsJumpedOver++;
        MLog($"blast over player: slot {slotRel:+0.0;-0.0} safe {safeRel:+0.0;-0.0} dx={dx:F1} playerY-ground={(g.HasValue ? dy.ToString("F1") : "PIT")} room={(c.HasValue && g.HasValue ? (c.Value - g.Value).ToString("F1") : "-")}");
    }

    // 安全な地点: 穴の上ではない(足場がある)ものからランダム。全部穴なら真ん中
    // 安全な地点: 穴の上ではない(足場がある)ものから。走り続けるので横へは大きく動けない(1秒で数 m は無理)。
    // 避け方は「安全な列に居る」か「跳んで爆発(高さ 2.4m)の上を越える」。2026-10-13: 天井が低くて跳んで越えられない所
    // (天井まで 5.2m 未満。天井の針もここ)では、今いる列を安全にする(避けようの無い組み合わせを作らない)
    public static int SafeSlotForcedHere; // 確認用
    int PickSafeSlot(float[] slots)
    {
        var tm = TerrainManager.Instance;
        var ok = new List<int>();
        int here = 0; float best = float.MaxValue;
        for (int i = 0; i < slots.Length; i++)
        {
            if (Mathf.Abs(slots[i]) < best) { best = Mathf.Abs(slots[i]); here = i; }
            bool ground = true;
            if (tm != null && tm.TryGetGroundFast(PlayerX + slots[i], out _, out bool pit)) ground = !pit;
            if (ground) ok.Add(i);
        }
        if (tm != null && ok.Contains(here))
        {
            float? c = tm.GetEffectiveCeilingHeightAt(PlayerX);
            float? g = tm.GetHeightAt(PlayerX);
            if (c.HasValue && g.HasValue && c.Value - g.Value < 5.2f) { SafeSlotForcedHere++; MLog($"air raid: low ceiling over the player ({c.Value - g.Value:F1}m) -> the player's column is the safe one"); return here; }
        }
        if (ok.Count == 0) return slots.Length / 2;
        return ok[Random.Range(0, ok.Count)];
    }

    // 爆弾の落下(見た目。判定は SkyStrike): 爆弾倉から着弾地点へ、走行と一緒に流れながら落ちる
    IEnumerator DropBomb(float worldX, float fall)
    {
        var go = new GameObject("MechBomb");
        go.transform.SetParent(transform, true); // ボスと一緒に消える(撃破/片付けで残らない)
        spawnedFx.Add(go);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = bombArt != null ? bombArt : BossFx.Orb();
        sr.color = bombArt != null ? Color.white : new Color(0.15f, 0.15f, 0.2f, 1f);
        sr.sortingOrder = RenderOrder.Boss + 1;
        go.transform.localScale = Vector3.one * (bombArt != null ? 0.9f / Mathf.Max(0.01f, bombArt.bounds.size.x) : 0.5f);
        sr.flipX = facing > 0f;
        Vector3 start = MP(0.18f, 0.22f);
        float relStart = start.x - PlayerX, relEnd = worldX - PlayerX;
        float gy = PlayerGroundY;
        float t = 0f;
        while (t < fall && !IsDead)
        {
            float k = t / fall;
            go.transform.position = new Vector3(PlayerX + Mathf.Lerp(relStart, relEnd, k), Mathf.Lerp(start.y, gy + 0.3f, k * k), 0f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, -80f * facing, k));
            t += Time.deltaTime;
            yield return null;
        }
        spawnedFx.Remove(go);
        Destroy(go);
    }

    // ===================================================================== 特殊攻撃 / 必殺技
    IEnumerator Barrage()
    {
        yield return Vulcan();
        if (!IsDead) yield return AirRaid();
    }

    IEnumerator OverkillProtocol()
    {
        if (!StartUlt("OVERKILL PROTOCOL", Alarm)) yield break;
        CaveDarkness.Set(0.3f);
        yield return HighSweep(1);
        if (IsDead) yield break;
        yield return AirRaid();
        if (IsDead) yield break;
        yield return LaserShot(Upgraded || Enraged ? 3 : 2, true);
        CaveDarkness.Set(0f);
        // 隙: 冷却で長く降りてくる(長い反撃の機会)
        SetMech("laser_cool");
        StartCoroutine(Steam(4.2f));
        yield return SetAltitude(LowAlt, 0.4f);
        yield return MoveToGap(2.8f, 9f, 1.2f);
        Openings++;
        yield return CaveRecovery(4.2f, "冷却中! 攻撃のチャンス!", new Vector2(1.05f, 0.9f), CaveLook.Lightning, new Vector2(0.4f, 0.75f), 2.6f, 1.3f, LowAlt);
        SetMech(null);
        yield return SetAltitude(HoverAlt, 0.5f);
    }

    // ---- 照準(見た目だけ。判定は無い) ----
    SpriteRenderer MakeReticle()
    {
        var go = new GameObject("MechReticle");
        go.transform.SetParent(transform, true); // ボスと一緒に消える
        spawnedFx.Add(go);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = reticleArt != null ? reticleArt : BossFx.Ring();
        sr.sortingOrder = RenderOrder.CombatFx + 2;
        return sr;
    }

    void ClearFx()
    {
        holdGap = float.NaN;
        foreach (var g in spawnedFx) if (g != null) Destroy(g);
        spawnedFx.Clear();
        if (mouthGlow != null) mouthGlow.enabled = false;
        if (gunGlow != null) gunGlow.enabled = false;
        if (beam != null) beam.Deactivate();
        if (sweep != null) sweep.Deactivate();
    }

    protected override void SkyRestore()
    {
        ClearFx();
        mpose = null;
        CaveDarkness.Set(0f);
        StartCoroutine(Ambient());
    }

}
