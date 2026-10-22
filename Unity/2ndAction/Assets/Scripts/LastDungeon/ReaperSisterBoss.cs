using System.Collections;
using UnityEngine;

// ラストダンジョン 100,000m: 死神三姉妹との正式な戦闘(2026-09-30)。
// これまでプレイヤーを追ってきた三姉妹(ReaperSisterData: 絵/歩き・浮遊・スキップのテンポ)を、既存のボス戦の仕組み
// (WildBossBase: HP/被弾/予告/攻撃判定/撃破演出/プレイヤーと並走する間合い)に載せたもの。
//  長女(Eldest) … ゆっくり歩く。大鎌の横薙ぎ / 地を這う斬撃(跳んで越える) / 距離を詰める踏み込み
//  次女(Second) … 低空に浮かぶ。上空から急降下して斬る / 魂の弾(3発、上下にずれる)
//  三女(Youngest) … 楽しそうにスキップ。跳びかかり(着地に衝撃) / 前後の往復突進 / 回転斬り
// 前半(ソロ)はHPが0になると倒れず「退く」(RetreatOnLethal)。後半(三人同時)は通常どおり撃破演出→DefeatOverride。
// 2026-10-20(マスター依頼: 「避けるだけ」ではなく「攻めながら突破する」三姉妹): 各姉妹に主力の技を2つ(ReaperSisterBoss.Moves.cs)。
//  飛び道具は ReaperBullet(壊せる/返せる/返せない)。返した弾が当たるとダメージ + 崩し(怯み/BREAK、調整は BossBattleTuning "ReaperSister")。
//  三人同時の時は連携技「死の舞踏」(ReaperFinaleBattle が時間差で合図する)。以前の技は合間の小技として残す。
// 移動のコマ送りは走行速度と無関係の一定テンポ(ReaperAnimatorと同じ考え方)。
public partial class ReaperSisterBoss : WildBossBase
{
    public ReaperSisterData data;
    public ReaperSister sister;
    public bool retreatOnLethal;              // 前半: HP0で倒れずに退く
    public System.Action<ReaperSisterBoss> Retreated;
    public bool Retreating { get; private set; }
    public float aggression = 1f;             // 後半は少し間隔を詰める(三人同時の分、個々は控えめ)

    public static readonly System.Collections.Generic.List<ReaperSisterBoss> All = new System.Collections.Generic.List<ReaperSisterBoss>();
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
        b.mileReward = RunRewards.BossMile(800, 1f); // 2026-10-15: 基本 × 1.5(最後の相手)
        b.bodyHeight = data != null ? Mathf.Max(3.8f, data.heightWorld * 1.75f) : 4.2f; // 追ってきた時より大きく(最後に真正面から向き合う相手)
        b.slotIndex = slot;
        b.slotSpacing = 4.6f; // 2026-10-20: 三人同時で体が大きく重なっていた(3.6m)。正式な絵は横幅が広いので少し離す
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
        b.ApplyTuning("ReaperSister"); // 2026-10-20: 崩し(怯み/BREAK)。段階/必殺技は使わない
        b.Init(player);
        return b;
    }

    protected override void OnInit()
    {
        // 2026-10-19 見た目の完成度向上: 姿勢ごとのコマ(Resources/BossAnim/reaper_eldest|second|youngest/、ppu.txt=512、右向き)。
        // 置いた姿勢だけ使い、無い姿勢は今までどおり(移動=moveFrames、溜め/攻撃=待機の絵)
        animKey = "reaper_" + sister.ToString().ToLowerInvariant();
        if (data != null && data.moveFrames != null && data.moveFrames.Length > 0)
        {
            loopFrames = MatchIdleScale(data.moveFrames, idleSprite);
            loopFps = data.motion == ReaperMotion.Skip ? Mathf.Max(1f, data.skipRate * data.moveFrames.Length / 2f) : Mathf.Max(1f, data.moveFps);
            loopTimeOffset = (int)sister * 0.37f;
        }
        baseHover = sister == ReaperSister.Second ? 1.1f : 0f;
        // 2026-10-20: 溜めの点滅は弱く・姉妹の色で(赤い影のように見えていた)。絵の溜めの姿勢を見せる
        windupTintScale = 0.35f;
        artPoseProceduralScale = 0.3f; // 溜め/攻撃/戻りの絵がある時は、絵の姿勢を主に(手続きの反り/踏み込みは弱く)
        windupTint = ReaperFx.SisterGlow(sister);
        yOffset = baseHover;
        tint = sister == ReaperSister.Eldest ? new Color(0.92f, 0.88f, 1f) : sister == ReaperSister.Second ? new Color(0.86f, 0.92f, 1f) : new Color(1f, 0.9f, 0.96f);
        SetBodyTint(tint);
        Color purple = new Color(0.8f, 0.45f, 0.95f, 0.95f);
        // 2026-10-20: 判定の位置は「正面が +x」(BossHitbox.Place)。以前は負の値で体の後ろに出ていた(大鎌/急降下が正面のプレイヤーに届かなかった)
        float h = bodyHeight;
        scythe = NewHitbox("ReaperScythe", new Vector2(halfWidth * 0.5f + 1.5f, h * 0.45f), new Vector2(3.2f, h * 0.8f), BossFx.Slash(), purple);
        scytheMark = NewMarker(new Vector2(halfWidth * 0.5f + 1.5f, h * 0.45f), new Vector2(3.2f, h * 0.8f));
        dive = NewHitbox("ReaperDive", new Vector2(1.2f, 0.9f), new Vector2(2.6f, 2.0f), BossFx.Slash(), new Color(0.5f, 0.8f, 1f, 0.95f));
        diveMark = NewMarker(new Vector2(1.2f, 0.9f), new Vector2(2.6f, 2.0f));
        slam = NewHitbox("ReaperSlam", new Vector2(0f, 0.5f), new Vector2(4.2f, 1.1f), BossFx.Ring(), new Color(1f, 0.6f, 0.9f, 0.95f));
        slamMark = NewMarker(new Vector2(0f, 0.5f), new Vector2(4.2f, 1.1f));
        spin = NewHitbox("ReaperSpin", new Vector2(0f, h * 0.45f), new Vector2(3.6f, h * 0.9f), BossFx.Slash(), new Color(1f, 0.5f, 0.85f, 0.95f));
        spinMark = NewMarker(new Vector2(0f, h * 0.45f), new Vector2(3.6f, h * 0.9f));
        InitMoves();
        supportsInterrupt = true; // 2026-10-20: 怯み/BREAK で技を止めてよい(OnInterrupted で浮き/予兆を戻す)
        if (!All.Contains(this)) All.Add(this);
        StartCoroutine(MotionLoop());
    }

    void OnDestroy() { All.Remove(this); }

    // 2026-10-20: 移動のコマ(624×667 など)と待機の絵(777×907 など)は同じ PPU 512 だが描かれた大きさが違い、溜め/攻撃(待機の絵)に
    // 入った瞬間に体が最大 36% 大きくなっていた。体の高さ(判定の bodyHeight)は待機の絵で決めているので、移動のコマを待機の絵の高さへそろえる
    // (追跡モードの ReaperAnimator は元のコマのまま)。新しい姿勢の絵(Resources/BossAnim/reaper_*)も待機の絵と同じ縮尺で入れる
    static Sprite[] MatchIdleScale(Sprite[] frames, Sprite idle)
    {
        if (idle == null || frames == null) return frames;
        var o = new Sprite[frames.Length];
        float target = idle.bounds.size.y;
        for (int i = 0; i < frames.Length; i++)
        {
            var f = frames[i];
            if (f == null) { o[i] = null; continue; }
            float k = target / Mathf.Max(0.01f, f.bounds.size.y);
            if (Mathf.Abs(k - 1f) < 0.02f) { o[i] = f; continue; }
            Vector2 pivot = new Vector2(f.pivot.x / f.rect.width, f.pivot.y / f.rect.height);
            o[i] = Sprite.Create(f.texture, f.rect, pivot, f.pixelsPerUnit / k, 0, SpriteMeshType.FullRect);
            o[i].name = f.name;
        }
        return o;
    }

    // 撃破の倒れ方は姉妹ごと(BossDeathTuning "Reaper/Eldest" など)。断末魔の声は "Reaper" のまま
    public override string DeathProfileKey => "Reaper/" + sister;

    // BREAK の絵がまだ無い間は、BREAK/立ち直りの間に移動のコマ(歩く足)を回さず、待機の絵で崩れて見せる
    protected override Sprite PoseOverride()
    {
        var a = Anim;
        if (Retreating && a != null && a.hurt != null) return a.hurt[0]; // 前半の HP0: 崩れた姿勢のまま薄れて奥へ退く
        bool hasBreakArt = a != null && (a.brk != null || a.breakLoop != null || a.rise != null);
        if (!hasBreakArt && (Broken || BreakRecovering) && pose != Pose.Fly) return idleSprite;
        return null;
    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static bool DebugHold;
    public bool DebugHolding { get; private set; }
    public void DebugSetAlpha(float a) => SetAlpha(a); // 撮影用: 他の姉妹を薄くして一人の動きを見やすく
    public Vector3 DebugHitboxCenter(string which) { var hb = which == "sever" ? sever : which == "swoop" ? swoop : which == "scythe" ? scythe : null; return hb != null ? hb.transform.position : Vector3.zero; }
    public bool DebugHitboxActive(string which) { var hb = which == "sever" ? sever : which == "swoop" ? swoop : which == "scythe" ? scythe : null; return hb != null && hb.IsActive; }
#endif

    // 移動のテンポ(浮遊の上下/スキップの跳ね/歩きの揺れ)を、攻撃中以外は常に付ける
    IEnumerator MotionLoop()
    {
        float resumeT = 0f;
        while (!IsDead)
        {
            bobT += Time.deltaTime;
            // 2026-10-22: 三女のスキップのコマ(loopFrames)を、跳ねと同じ時計で回す(着地のコマと実際の着地がずれていた)
            if (sister == ReaperSister.Youngest && loopFrames != null) loopTimeOffset = bobT - Time.time;
            if (bob && !Retreating && !Broken && DebugAct != "Flinch")
            {
                float target;
                switch (sister)
                {
                    case ReaperSister.Second:
                        target = baseHover + Mathf.Sin(bobT * 0.55f * Mathf.PI * 2f) * 0.18f;
                        break;
                    case ReaperSister.Youngest:
                    {
                        float rate = data != null ? data.skipRate : 2.2f;
                        float hops = bobT * rate; float ph = hops - Mathf.Floor(hops);
                        bool floaty = ((int)hops) % 4 == 3;
                        target = (floaty ? Mathf.Sin(ph * Mathf.PI) * 0.7f : 4f * ph * (1f - ph) * 0.32f);
                        break;
                    }
                    default:
                        target = Mathf.Abs(Mathf.Sin(bobT * 2.5f * Mathf.PI * 0.5f)) * 0.05f;
                        break;
                }
                // 2026-10-22: 攻撃などの後に揺れへ戻る時は、今の高さから寄せる(以前は揺れの位相の高さへ1フレームで飛んでいた)
                resumeT += Time.deltaTime;
                yOffset = resumeT < 0.35f ? Mathf.Lerp(yOffset, target, 1f - Mathf.Exp(-Time.deltaTime / 0.07f)) : target;
            }
            else resumeT = 0f;
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (DebugHold) { DebugHolding = true; relVelocity = 0f; yield return null; continue; } // 確認用: 技を出さずに待つ
            DebugHolding = false;
#endif
            // 三人同時の連携技(ReaperFinaleBattle の合図): 今の技を終えてから、合図の時刻に自分の番を出す
            if (comboAt > 0f) { yield return ComboTurn(); continue; }
            n++;
            yield return NextMove(n);
            yield return Wait(Mathf.Lerp(1.4f, 0.9f, Mathf.Clamp01(aggression - 1f)) * (slotIndex > 0 ? 1.2f : 1f));
        }
    }

    // ---- 長女 ----
    IEnumerator ScytheSweep(float windup)
    {
        animVariant = 0; // コマ: 1つ目の技(windup/attack)
        yield return MoveToGap(3.4f, 3.5f, 3f);
        yield return Telegraph(windup, scytheMark);
        yield return Strike(scythe, 0.26f, 0.08f);
        yield return Recover(0.7f);
    }

    IEnumerator StepAndSweep()
    {
        animVariant = 0; // コマ: 1つ目の技(windup/attack)
        yield return MoveToGap(6.5f, 4f, 2.5f);
        yield return Telegraph(0.6f, scytheMark);
        yield return DashMove(0.25f, 12f); // 一歩で間合いを詰める
        yield return Strike(scythe, 0.24f, 0.1f);
        yield return Recover(0.8f);
    }

    // 地を這う斬撃: 地面に沿って進む紫の刃。跳んで越える(上攻撃/空中でも越えられる高さ)
    IEnumerator ReapingWave()
    {
        animVariant = 1; // コマ: 2つ目の技(windupB/attackB)
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
        animVariant = 0; // コマ: 1つ目の技(windup/attack)
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
        animVariant = 1; // コマ: 2つ目の技(windupB/attackB)
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
        animVariant = 0; // コマ: 1つ目の技(windup/attack)
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
        animVariant = 0; // コマ: 1つ目の技(windup/attack)
        yield return MoveToGap(8f, 4f, 2f);
        yield return Telegraph(0.5f, spinMark);
        yield return DashMove(0.35f, 16f);
        yield return Strike(spin, 0.22f, 0.08f);
        yield return Retreat(8f, 0.45f);
        yield return Recover(0.6f);
    }

    IEnumerator SpinSlash()
    {
        animVariant = 1; // コマ: 2つ目の技(windupB/attackB)
        yield return MoveToGap(3.2f, 4.5f, 2.5f);
        yield return Telegraph(0.55f, spinMark);
        yield return Strike(spin, 0.3f, 0.08f);
        yield return Recover(0.7f);
    }

    // ---- HP0の扱い ----
    protected override bool OnLethalDamage()
    {
        if (!retreatOnLethal) return false;
        if (!Retreating)
        {
            Retreating = true;
            StopAllCoroutines();   // 2026-10-20: BREAK/技の途中でも、退く動きだけにする
            ResetCombatState();
            StartCoroutine(RetreatRoutine());
        }
        return true;
    }

    // 怯み/BREAK で技が止められた: 浮き/予兆/一時的な絵を戻し、移動の揺れを動かし直す
    protected override void OnInterrupted()
    {
        bob = true;
        ClearTemp();
        comboAt = -1f;
        if (!Retreating && !IsDead) StartCoroutine(MotionLoop());
    }

    IEnumerator RetreatRoutine()
    {
        Retreating = true;
        ClearTemp();
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
