using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ===================================================================== //
// 通常マップの専用ラスボス 3体(2026-11-06、マスターの依頼。設計 = Docs/FinalBosses_2026-11-06/design.md)
//  98km の「そのマップのラスボス」の枠だけで出る(BossManager.FinalBoss.cs / FinaleSet.cs)。どれも走りながら戦う。
//  今まで覚えた操作(前攻撃・打ち上げ・空中攻撃・急降下・ジャンプ・上下の安全帯・BREAK)の最終試験。新しい操作は求めない。
//  難しさは HP ではなく組み合わせ(第2段階で強攻撃、第3段階で技を続けて出す)。
//  予兆は今までの決まり(低い=赤で跳ぶ / 高い=紫で地面にいる / 背が高い=金で二段ジャンプ)を保ったまま、ボスごとの色を混ぜる:
//    荒野王 = 赤橙の帯 + 砂煙 / 深淵の主 = 淡い青の帯 + 点滅する結晶 / 天帝 = 青紫の縁取り。
//  攻撃は全部 走行の座標系で流れる部品(CaveHazard / TrackedHazard の予告 / ボスの判定)なので、300〜900km/h でも見え方は同じ。
//  必殺技は BeginUltimate(StartUlt)を通る = 全体で同時に1体だけ。BREAK/立ち直りは共通の仕組み(膝つき)。撃破は BOSS FINISH(BossDeathTuning)。
//  絵: Resources/BossAnim/<garmud|noctaris|seraphiel>/(1024x1024、体は真ん中、左向き)。体の見えている範囲で大きさを合わせる(FitCanvasArt)。
//    技の番号(animVariant)とコマの名前: 0 = windup_/attack_ / 1 = windupB_/attackB_ / 2 = windupC_/attackC_ / 3 = windupD_/attackD_ / 4 = windupE_/attackE_
//    必殺技 = ult_、被弾 = hurt_、BREAK = break_/breakloop_、立ち直り = rise_、撃破 = defeat_、戻り = recover_(技ごと recoverB_ 等)。無いコマは待機の絵
// ===================================================================== //

// 確認用(QA): ラスボスが使った技の回数
public static class FinalBossStats
{
    public static readonly Dictionary<string, int> Moves = new Dictionary<string, int>();
    public static void Note(WildBossBase b, string move)
    {
        if (b == null) return;
        string k = b.bossName + "/" + move;
        Moves[k] = Moves.TryGetValue(k, out int n) ? n + 1 : 1;
        if (Debug.isDebugBuild) Debug.Log($"[FinalBoss] {b.bossName} {move} phase={b.Phase} hp={b.Hp}/{b.maxHp}");
    }

    // 走行と一緒に流れる予告の帯(WildBossBase.LaneWarn と同じ)。ラスボスの体(Boss - 1)より手前に描く
    public static void Lane(float worldX, float width, float yLow, float yHigh, float dur, Color tint)
    {
        var h = TrackedHazard.CreateLifted(worldX, width, Mathf.Max(0.2f, yHigh - yLow), yLow, dur, 0f, Color.clear);
        h.warnTint = tint;
        var sr = h.GetComponent<SpriteRenderer>();
        if (sr != null) sr.sortingOrder = RenderOrder.Boss;
    }

    // BREAK 中の弱点の光(角の付け根 / 胸の核 / 光輪のひび)。名前は "...Glow" にしない(技の光を消す共通の処理の対象外)
    public static SpriteRenderer WeakPoint(Transform parent, Color c)
    {
        var g = new GameObject("WeakPoint");
        g.transform.SetParent(parent, false);
        var s = g.AddComponent<SpriteRenderer>();
        s.sprite = BossFx.Orb();
        s.color = c;
        s.sortingOrder = RenderOrder.Boss + 1;
        s.enabled = false;
        return s;
    }

    public static void ShowWeakPoint(SpriteRenderer s, bool on, Vector3 at, float size, Color c)
    {
        if (s == null) return;
        s.enabled = on;
        if (!on) return;
        s.transform.position = at;
        float k = size * (1f + 0.18f * Mathf.Sin(Time.time * 9f));
        Vector3 ls = s.transform.parent != null ? s.transform.parent.lossyScale : Vector3.one;
        s.transform.localScale = new Vector3(k / Mathf.Max(0.01f, Mathf.Abs(ls.x)), k / Mathf.Max(0.01f, Mathf.Abs(ls.y)), 1f);
        c.a = 0.65f + 0.3f * Mathf.Sin(Time.time * 9f);
        s.color = c;
    }
}

// ===================================================================== //
// 荒野街道 98km 荒野王 ガルムード(The Wasteland King)
//  獅子+雄牛の巨大な四足の獣王。背に崩れた砦の城壁と二本の軍旗。街道を塞ぐ「最後の門」として前方を走り続ける。
//  通常   角の振り上げ      : 正面の低い帯 → 跳ぶ(windup_/attack_)
//  移動   突進              : 間合いを取り、前脚で地面を掻く(砂煙 + 地面の帯)→ 目の前まで突っ込む → 跳んで越える。
//                              第2段階から二度目の突進は背が高い(金)= 二段ジャンプ(windupB_/attackB_)
//  上下   城壁の瓦礫を投げる : 帯の高さで判断(低い=跳ぶ / 高い=地面にいる)。2〜3個を交互に(windupC_/attackC_)
//  強攻撃 大地割り          : 前脚を高く上げて叩きつけ → 地面の衝撃波 2列(低い=跳ぶ×2)→ 高い砂嵐(地面にいる)(windupD_/attackD_)
//  必殺技 荒野の終焉        : 咆哮 → 亡霊の騎兵隊が低 → 高 → 低(再戦の強化で +高)で駆け抜ける → 膝をつく大きな隙(ult_)
//  BREAK  前脚が崩れて伏せる。角の付け根(弱点)が光る(膝つき = 打ち上げ・空中攻撃が届く)
//  BREAK 明け 立ち上がりの咆哮: 小さな砂の衝撃波(跳ぶ)
// ===================================================================== //
public class GarmudBoss : HazardBossBase
{
    public static readonly Color Theme = new Color(1f, 0.42f, 0.12f, 1f);   // 予兆の色(赤橙)
    static readonly Color SandColor = new Color(0.86f, 0.72f, 0.5f, 1f);
    BossHitbox horn, charge, chargeTall, slam;
    BossTelegraphMarker hornMark, slamMark;
    SpriteRenderer weakPoint;
    float riseAnswered = -99f;
    static Sprite ghostRider; static bool ghostLoaded;

    protected override void OnInit()
    {
        HazardBattleSetup();
        animKey = "garmud";
        // 絵(1024): 足元 y=863 / 上端(軍旗の先) y=219 / 左(角) x=155 / 右(尾) x=893
        FitCanvasArt(0.156f, 0.786f, 0.151f, 0.873f);
        locoStyle = LocoStyle.Stride;
        footstepShake = true;
        enterSpeed = 12f;
        artPoseProceduralScale = 0.35f;
        windupTint = Theme; windupTintScale = 0.6f;
        hitStopOnHit = 0.03f;
        startGap = halfWidth + 5.5f; minGap = halfWidth * 0.85f; maxGap = halfWidth + 12f;
        // 被弾: 獣の体と背の砦(軍旗の先と尾は外す)
        ConfigureHurtbox(new Vector2(0f, bodyHeight * 0.4f), new Vector2(halfWidth * 2f * 0.8f, bodyHeight * 0.78f));
        float r = FrontReach;
        Color fx = new Color(1f, 0.55f, 0.25f, 0.95f);
        Vector2 hc = new Vector2(r + 1.0f, 0.6f), hs = new Vector2(3.0f, 1.15f);
        horn = NewHitbox("HornUpswing", hc, hs, BossFx.Slash(), fx);
        hornMark = NewMarker(hc, hs);
        charge = NewHitbox("Charge", new Vector2(r * 0.55f, 0.55f), new Vector2(r * 0.9f + 1.2f, 1.1f), BossFx.Slash(), fx);
        chargeTall = NewHitbox("ChargeTall", new Vector2(r * 0.55f, 1.1f), new Vector2(r * 0.9f + 1.2f, 2.2f), BossFx.Slash(), new Color(1f, 0.8f, 0.3f, 0.95f));
        Vector2 sc = new Vector2(r + 1.2f, 0.45f), ss = new Vector2(3.4f, 0.9f);
        slam = NewHitbox("GroundSlam", sc, ss, BossFx.Ring(), fx);
        slamMark = NewMarker(sc, ss);
        hornMark.SetTheme(Theme, 0.45f); slamMark.SetTheme(Theme, 0.45f);
        weakPoint = FinalBossStats.WeakPoint(transform, new Color(1f, 0.85f, 0.4f, 0.9f));
        SetVisualSortingOrder(RenderOrder.Boss - 1); // 大きな体がプレイヤー/予告を隠さないように、体は一つ奥へ
    }

    CaveHazard Th(CaveHazard h)
    {
        if (h == null) return null;
        h.SetWarnTheme(Theme, 0.35f);
        ImpactDust(new Vector3(h.transform.position.x, h.transform.position.y + 0.1f, 0f), 4, 0.7f); // 砂煙(予兆の場所)
        return h;
    }
    static Color Mix(Color lane) => Color.Lerp(lane, Theme, 0.35f);
    void Lane(float relX, float width, float yLow, float yHigh, float dur, Color tint) => FinalBossStats.Lane(PlayerX + relX, width, yLow, yHigh, dur, tint);
    void Note(string move) { DebugAct = move; FinalBossStats.Note(this, move); }
    // 1回目の必殺技: 第2段階に入って 1.5秒たてば撃つ(段階が続けて上がると firstUltimateDelay の待ちが毎回やり直しになり、一度も撃たずに倒れることがあった)
    bool FirstUltDue => UltimatesUsed == 0 && Phase >= 2 && !BossBattle.UltimateActive && Time.time - phaseUnlockedAt >= 1.5f;

    bool RiseDue => BreakEndedAt > riseAnswered && Time.time - BreakEndedAt < 2.5f;

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (RiseDue) { yield return RiseRoar(); continue; }
            if (UltimateReady(2) || FirstUltDue) { yield return WastelandEnd(); continue; }
            if (SpecialReady(SpecialPhase)) { MarkSpecial(); yield return EarthSplitter(); continue; }
            int pick = BossAiUtil.PickNoRepeat(3, ref lastPick);
            if (pick == 0) yield return HornUpswing();
            else if (pick == 1) yield return Charge();
            else yield return RubbleThrow();
            // 第3段階: 間をあけずに次の技へ(組み合わせで難しくする。予兆の長さは変えない)
            if (Enraged && Random.value < 0.45f) { if (pick == 2) yield return HornUpswing(); else yield return RubbleThrow(); }
        }
    }

    // 通常: 角の振り上げ(正面の低い帯 → 跳ぶ)
    IEnumerator HornUpswing()
    {
        Note("HornUpswing");
        animVariant = 0;
        yield return Melee(horn, hornMark, 1.2f, 4.5f, 0.6f, 0.25f, 0.8f, 0.1f);
    }

    // 移動: 突進。前脚で地面を掻く(砂煙 + 地面の帯)→ 目の前まで突っ込む(跳んで越える)。第2段階から2回目(背が高い = 二段)
    IEnumerator Charge()
    {
        Note("Charge");
        animVariant = 1;
        yield return MoveToGap(halfWidth + 12f, 12f, 1.4f);
        int n = Phase >= 2 ? 2 : 1;
        for (int i = 0; i < n && !IsDead; i++)
        {
            bool tall = i == 1;
            var hb = tall ? chargeTall : charge;
            hb.damageAmount = HitDmg;
            float front = Mathf.Max(2f, FrontDist);
            Lane(front * 0.5f - 0.5f, front + 1f, 0f, tall ? 2.2f : 1.1f, 1.0f + 0.35f, Mix(tall ? TallLaneColor : LowLaneColor));
            StartCoroutine(SandPaw(0.95f));
            yield return Telegraph(0.95f);
            const float sp = 20f;
            float dur = Mathf.Clamp((FrontDist + 0.5f) / sp, 0.15f, 0.9f);
            Shake(0.1f, 0.2f);
            StartCoroutine(DashMove(dur, sp));
            yield return Strike(hb, dur, 0.12f);
            relVelocity = 0f;
            if (i + 1 < n)
            {
                EndAttack();
                yield return Retreat(10f, 0.9f); // 下がって構え直す(2回目は背が高い)
            }
        }
        yield return Recover(1.0f * RecoverMul);
    }

    IEnumerator SandPaw(float seconds)
    {
        float t = 0f, puff = 0f;
        while (t < seconds && !IsDead)
        {
            t += Time.deltaTime; puff -= Time.deltaTime;
            if (puff <= 0f) { puff = 0.12f; ImpactDust(new Vector3(worldX + facing * FrontReach * 0.7f, GroundY + 0.1f, 0f), 3, 0.8f); }
            yield return null;
        }
    }

    // 上下の判断: 城壁の瓦礫を投げる(低い = 跳ぶ / 高い = 地面にいる)。高さの変わり目は 1.05秒あける
    IEnumerator RubbleThrow()
    {
        Note("RubbleThrow");
        animVariant = 2;
        int n = Phase >= 2 ? 3 : 2;
        var seq = new bool[n];
        seq[0] = Random.value < 0.5f;
        for (int i = 1; i < n; i++) seq[i] = Random.value < 0.6f ? !seq[i - 1] : seq[i - 1];
        if (n >= 2 && System.Array.TrueForAll(seq, x => x == seq[0])) seq[n - 1] = !seq[0]; // 低い/高いを必ず両方
        yield return Telegraph(0.7f); // 背の瓦礫を持ち上げる
        for (int i = 0; i < n && !IsDead; i++)
        {
            bool high = seq[i];
            float y0 = high ? 2.1f : 0f, y1 = high ? 3.4f : 1.1f;
            float start = Mathf.Max(6f, Gap), speed = 11f;
            Lane(1.5f, 6f, y0, y1, start / speed, Mix(high ? HighLaneColor : LowLaneColor)); // 軌道の帯
            PlayAttackPose(0.35f);
            var w = Th(WaveFrom(start, 1.4f, y0, y1, speed, CaveLook.Rock, false, 0.75f));
            if (w != null) w.SetTint(SandColor);
            bool change = i + 1 < n && seq[i + 1] != high;
            yield return Wait(change ? 1.05f : 0.75f);
        }
        yield return Recover(0.8f * RecoverMul);
    }

    // 強攻撃: 大地割り。叩きつけ → 地面の衝撃波 2列(跳ぶ×2)→ 高い砂嵐(地面にいる)
    IEnumerator EarthSplitter()
    {
        Note("EarthSplitter");
        animVariant = 3;
        slam.damageAmount = HitDmg;
        Foresee(slamMark);
        yield return Approach(2.2f, 4.5f, 3f);
        yield return Telegraph(1.0f, slamMark); // 前脚を高く上げる
        Shake(0.2f, 0.3f);
        ImpactDust(new Vector3(worldX + facing * FrontReach, GroundY, 0f), 14, 1.3f);
        GroundRing(new Vector3(worldX + facing * FrontReach, GroundY, 0f), 6f, Theme);
        yield return Strike(slam, 0.22f, 0.22f, 0.06f);
        Th(ShockFromFront(1.0f, 12f, CaveLook.Dirt));
        yield return Wait(0.85f);
        PlayAttackPose(0.3f);
        Th(ShockFromFront(1.0f, 12f, CaveLook.Dirt));
        yield return Wait(0.8f);
        // 続けて高い砂嵐(地面にいる)
        float start = OffscreenAheadGap(), speed = 14f;
        Lane(1.5f, 8f, 2.1f, 4.2f, start / speed, Mix(HighLaneColor));
        var s = Th(WaveFrom(start, 3.2f, 2.1f, 4.2f, speed, CaveLook.Wind, false, 0.8f));
        if (s != null) s.SetTint(new Color(SandColor.r, SandColor.g, SandColor.b, 0.85f));
        yield return Recover(1.1f * RecoverMul);
    }

    // 必殺技: 荒野の終焉。咆哮 → 亡霊の騎兵隊が上下交互の帯で駆け抜ける → 膝をつく大きな隙
    IEnumerator WastelandEnd()
    {
        if (!StartUlt("荒野の終焉", Theme)) yield break;
        Note("Ultimate");
        UltBeat("start 荒野の終焉");
        animVariant = 0; // コマ: ult_(無ければ待機)
        yield return MoveToGap(halfWidth + 8f, 10f, 1.0f);
        Glow(1.1f, Theme);
        StartCoroutine(SandPaw(1.1f));
        yield return Telegraph(1.1f); // 咆哮
        Shake(0.25f, 0.5f);
        BossVoice.Growl(this, DeathKey);
        var seq = Upgraded ? new[] { false, true, false, true } : new[] { false, true, false };
        for (int i = 0; i < seq.Length && !IsDead; i++)
        {
            bool high = seq[i];
            float y0 = high ? 2.1f : 0f, y1 = high ? 4.0f : 1.15f;
            float start = OffscreenAheadGap() + 2f, speed = 16f;
            UltBeat(high ? "cavalry high" : "cavalry low");
            Lane(1.5f, 8f, y0, y1, start / speed, Mix(high ? HighLaneColor : LowLaneColor));
            PlayAttackPose(0.4f);
            var w = Th(WaveFrom(start, 3.4f, y0, y1, speed, CaveLook.Wind, true, 0.7f));
            if (w != null)
            {
                var gr = GhostRider();
                if (gr != null) { w.SetBodySprite(gr); w.KeepBodyAspect(); }
                w.SetTint(new Color(1f, 0.72f, 0.5f, 0.62f)); // 亡霊(半透明の橙)
            }
            yield return Wait(1.2f);
        }
        yield return Wait(1.3f);
        UltBeat("recovery");
        yield return CaveRecovery(4.2f, "王が膝をついた! 攻撃のチャンス!", new Vector2(1.05f, 0.88f), CaveLook.Dirt, new Vector2(0f, 0.8f), 2.6f, 1.3f);
    }

    static Sprite GhostRider()
    {
        if (!ghostLoaded) { ghostLoaded = true; ghostRider = Resources.Load<Sprite>("BossAnim/goblin_rider/move_00"); }
        return ghostRider;
    }

    // BREAK 明け: 立ち上がりの咆哮(小さな砂の衝撃波 = 跳ぶ)
    IEnumerator RiseRoar()
    {
        riseAnswered = BreakEndedAt;
        Note("RiseRoar");
        animVariant = 3;
        yield return Telegraph(0.5f);
        Shake(0.12f, 0.25f);
        BossVoice.Growl(this, DeathKey);
        ImpactDust(new Vector3(worldX, GroundY, 0f), 12, 1.2f);
        Th(ShockFromFront(0.8f, 10f, CaveLook.Dirt));
        PlayAttackPose(0.3f);
        yield return Recover(0.6f);
    }

    protected override void OnBattleTick(float dt)
    {
        base.OnBattleTick(dt);
        // BREAK 中: 角の付け根(弱点)が光る
        FinalBossStats.ShowWeakPoint(weakPoint, Broken && !IsDead, new Vector3(worldX + facing * halfWidth * 0.62f, GroundY + yOffset + bodyHeight * 0.42f, 0f), 1.3f, new Color(1f, 0.85f, 0.4f));
    }

    protected override void OnInterrupted()
    {
        base.OnInterrupted();
        if (weakPoint != null && IsDead) weakPoint.enabled = false;
    }
}

// ===================================================================== //
// 自然洞窟 98km 深淵の主 ノクタリス(Abyss Sovereign)
//  目の無い、低く重い古代の巨体。背びれと冠の結晶が光る。四本の短い脚で這い、地面の下を並走して浮上して攻撃する。
//  通常   背びれの結晶     : 地面から列で突き出る(低い=跳ぶ)。第2段階から2列(windup_/attack_)
//  移動   結晶のつらら     : 潜って並走しながら、天井から結晶を落とす(影と光 → その場所では跳ばない)(windup_ → 潜る dive_)
//  上下   尾の薙ぎ / 噛みつき: 尾 = 高い帯(地面にいる)(windupB_/attackB_)/ 頭で下を噛む = 低い(跳ぶ)(windupC_/attackC_)。第3段階は両方続けて
//  強攻撃 深淵の大顎       : 潜る → 地面に光る輪 → 真下から飛び出して噛む(跳ぶ。第2段階から背が高い = 二段)(windupD_/attackD_、emerge_)
//  必殺技 深淵の潮         : 暗くなる → 結晶の光の帯が低 → 高 → 低 → 闇の波(光る線で予告)→ 核がむき出しの大きな隙(ult_)
//  BREAK  地上に全身を出してのけぞり、胸の大結晶(核)がむき出し
//  BREAK 明け 結晶を飛び散らせて潜る(低い = 跳ぶ)
// ===================================================================== //
public class NoctarisBoss : CaveBossBase
{
    public static readonly Color Theme = new Color(0.55f, 0.86f, 1f, 1f);   // 予兆の色(淡い青)
    BossHitbox bite;
    BossTelegraphMarker biteMark;
    SpriteRenderer core;
    float riseAnswered = -99f;

    protected override void CaveInit()
    {
        animKey = "noctaris";
        artFacesLeft = true; // 絵は左向き(洞窟のボスの既定 = 右向きを上書き)
        // 絵(1024、2026-11-06 の縮尺の直し後): 足元 y=746 / 上端(背びれの結晶) y=266 / 左(顎) x=130 / 右(尾) x=893
        FitCanvasArt(0.2705f, 0.740f, 0.127f, 0.873f);
        locoStyle = LocoStyle.Crawl;
        footstepShake = true;
        moveTempoScale = 0.8f;
        enterSpeed = 10f;
        artPoseProceduralScale = 0.35f;
        windupTint = Theme; windupTintScale = 0.6f;
        hitStopOnHit = 0.03f;
        startGap = halfWidth + 5f; minGap = halfWidth * 0.85f; maxGap = halfWidth + 11f;
        ConfigureHurtbox(new Vector2(0f, bodyHeight * 0.4f), new Vector2(halfWidth * 2f * 0.84f, bodyHeight * 0.78f));
        float r = FrontReach;
        Vector2 bc = new Vector2(r + 0.8f, 0.6f), bs = new Vector2(2.8f, 1.15f);
        bite = NewHitbox("Bite", bc, bs, BossFx.Fang(), new Color(0.6f, 0.85f, 1f, 0.95f));
        biteMark = NewMarker(bc, bs);
        biteMark.SetTheme(Theme, 0.45f);
        core = FinalBossStats.WeakPoint(transform, Theme);
        SetVisualSortingOrder(RenderOrder.Boss - 1); // 大きな体がプレイヤー/予告を隠さないように、体は一つ奥へ
    }

    CaveHazard Th(CaveHazard h)
    {
        if (h == null) return null;
        h.SetWarnTheme(Theme, 0.4f);
        // 点滅する結晶(予兆の場所)
        OneShotSpriteEffect.CreateScatterBurst(CaveBossFx.CrystalShard(), h.transform.position + new Vector3(0f, 0.3f, 0f), Theme, 3, 0.5f, 0.15f, 0.3f, 1.2f, 1f, RenderOrder.CombatFx);
        return h;
    }
    static Color Mix(Color lane) => Color.Lerp(lane, Theme, 0.4f);
    void Lane(float relX, float width, float yLow, float yHigh, float dur, Color tint) => FinalBossStats.Lane(PlayerX + relX, width, yLow, yHigh, dur, tint);
    void Note(string move) { DebugAct = move; FinalBossStats.Note(this, move); }
    // 1回目の必殺技: 第2段階に入って 1.5秒たてば撃つ(段階が続けて上がると firstUltimateDelay の待ちが毎回やり直しになり、一度も撃たずに倒れることがあった)
    bool FirstUltDue => UltimatesUsed == 0 && Phase >= 2 && !BossBattle.UltimateActive && Time.time - phaseUnlockedAt >= 1.5f;
    bool RiseDue => BreakEndedAt > riseAnswered && Time.time - BreakEndedAt < 2.5f;

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (RiseDue) { yield return RiseScatter(); continue; }
            if (UltimateReady(2) || FirstUltDue) { yield return AbyssTide(); continue; }
            if (SpecialReady(SpecialPhase)) { MarkSpecial(); yield return AbyssJaw(); continue; }
            int pick = BossAiUtil.PickNoRepeat(3, ref lastPick);
            if (pick == 0) yield return CrystalSpines();
            else if (pick == 1) yield return IcicleRain();
            else yield return TailOrBite();
            // 地面の下を並走して位置を変える(浮上して次の技)
            if (!IsDead && Random.value < 0.3f) yield return TunnelRun();
        }
    }

    // 通常: 背びれの結晶が地面から列で突き出る(跳ぶ)
    IEnumerator CrystalSpines()
    {
        Note("CrystalSpines");
        animVariant = 0;
        Glow(0.75f, Theme);
        yield return Telegraph(0.75f);
        PlayAttackPose(0.3f);
        Th(FloorAt(0f, 2.4f, 1.1f, 0.8f, 0.3f, CaveLook.Crystal));
        Th(FloorAt(3.4f, 2.0f, 1.0f, 0.8f, 0.3f, CaveLook.Crystal));  // 列の続き(前方)
        Th(FloorAt(6.6f, 2.0f, 0.9f, 0.8f, 0.3f, CaveLook.Crystal));
        if (Phase >= 2)
        {
            yield return Wait(1.0f);
            PlayAttackPose(0.3f);
            Th(FloorAt(0f, 2.4f, 1.1f, 0.75f, 0.3f, CaveLook.Crystal));
            Th(FloorAt(-3.4f, 2.0f, 1.0f, 0.75f, 0.3f, CaveLook.Crystal));
        }
        yield return Wait(0.7f);
        yield return Recover(0.8f * RecoverMul);
    }

    // 移動: 潜って並走しながら、天井から結晶のつららを落とす(落ちる場所では跳ばない)
    IEnumerator IcicleRain()
    {
        Note("IcicleRain");
        animVariant = 0;
        yield return Telegraph(0.5f);
        yield return Burrow(0.45f);
        StartCoroutine(TunnelTo(halfWidth + 3f, 10f, 2.0f));
        float[] rel = Phase >= 2 ? new[] { 0f, 2.8f, -2.6f, 0f } : new[] { 0f, 2.8f, 0f };
        for (int i = 0; i < rel.Length; i++) Th(CeilAt(rel[i], 2.2f, 0.9f + i * 0.45f, 0.4f, CaveLook.Crystal, false, 2.2f));
        yield return Wait(0.9f + rel.Length * 0.45f + 0.4f);
        yield return Surface(0.35f);
        yield return Recover(0.7f * RecoverMul);
    }

    // 上下の判断: 尾の薙ぎ(高い帯 = 地面にいる)/ 頭で下を噛む(低い = 跳ぶ)。第3段階は両方続けて
    IEnumerator TailOrBite()
    {
        bool tail = Random.value < 0.5f;
        int steps = Enraged ? 2 : 1;
        for (int s = 0; s < steps && !IsDead; s++, tail = !tail)
        {
            if (tail)
            {
                Note("TailSweep");
                animVariant = 1;
                Th(BandAt(1.0f, 7f, 2.1f, 3.8f, 0.85f, 0.3f, CaveLook.Water));
                yield return Telegraph(0.85f);
                PlayAttackPose(0.3f);
                yield return Wait(0.4f);
                EndAttack();
            }
            else
            {
                Note("Bite");
                animVariant = 2;
                bite.damageAmount = HitDmg;
                Foresee(biteMark);
                yield return Approach(1.2f, 5f, 2.5f);
                yield return Telegraph(0.55f, biteMark);
                yield return Strike(bite, 0.22f, 0.08f);
                EndAttack();
            }
        }
        yield return Recover(0.85f * RecoverMul);
    }

    // 強攻撃: 深淵の大顎。潜る → 地面に光る輪 → 真下から飛び出して噛む(跳ぶ / 第2段階から背が高い = 二段)
    IEnumerator AbyssJaw()
    {
        Note("AbyssJaw");
        animVariant = 3;
        yield return Telegraph(0.45f);
        yield return Burrow(0.5f);
        yield return TunnelTo(minGap, 12f, 1.5f); // 頭がプレイヤーの足元に来る所(体はプレイヤーの前)
        bool tall = Phase >= 2;
        const float warn = 1.0f;
        Th(TellAt(0f, warn, CaveTellStyle.Circle, CaveLook.Crystal));
        Th(FloorAt(0f, 3.0f, tall ? 2.2f : 1.2f, warn, 0.35f, CaveLook.Crystal, false));
        float t = 0f;
        while (t < warn - 0.12f && !IsDead) { t += Time.deltaTime; relVelocity = 0f; yield return null; }
        extraScale = new Vector2(1.08f, 1.08f);
        emergeArtUntil = Time.time + 0.75f;
        yield return Surface(0.2f);
        PlayAttackPose(0.35f);
        Shake(0.16f, 0.25f);
        yield return Wait(0.35f);
        extraScale = Vector2.one;
        // 飛び出した後、前へ倒れ込んで四つ足に戻る(land_ があれば)
        if (Anim != null && Anim.land != null) { exhaustArtFrames = Anim.land; exhaustArt = true; SetPose(Pose.Idle); yield return Wait(0.45f); exhaustArt = false; exhaustArtFrames = null; }
        yield return MoveToGap(startGap, 8f, 1.2f);
        yield return Recover(1.0f * RecoverMul);
    }

    // 必殺技: 深淵の潮。暗くなる → 結晶の光の帯が上下交互 → 闇の波 → 核がむき出しの大きな隙
    IEnumerator AbyssTide()
    {
        if (!StartUlt("深淵の潮", Theme)) yield break;
        Note("Ultimate");
        UltBeat("start 深淵の潮");
        animVariant = 0; // コマ: ult_(無ければ待機)
        yield return MoveToGap(startGap + 1f, 8f, 1.0f);
        CaveDarkness.Set(0.4f);
        Glow(1.1f, Theme);
        yield return Telegraph(1.0f);
        var seq = Upgraded ? new[] { false, true, false, true } : new[] { false, true, false };
        for (int i = 0; i < seq.Length && !IsDead; i++)
        {
            bool high = seq[i];
            UltBeat(high ? "band high" : "band low");
            PlayAttackPose(0.3f);
            Th(BandAt(0.8f, 8f, high ? 2.1f : 0f, high ? 3.8f : 1.1f, 0.85f, 0.35f, CaveLook.Water, true));
            yield return Wait(1.25f);
        }
        // 闇の波(光る線で予告。低い = 跳ぶ)
        UltBeat("dark wave");
        float start = OffscreenAheadGap(), speed = 13f;
        Lane(1.5f, 8f, 0f, 1.2f, start / speed, Mix(LowLaneColor));
        var w = Th(WaveFrom(start, 3.0f, 0f, 1.15f, speed, CaveLook.Water, true, 0.75f));
        if (w != null) w.SetTint(new Color(0.25f, 0.35f, 0.75f, 0.9f));
        yield return Wait(start / speed + 0.5f);
        CaveDarkness.Set(0f);
        UltBeat("recovery");
        yield return CaveRecovery(4.2f, "核がむき出しだ! 攻撃のチャンス!", new Vector2(1.05f, 0.9f), CaveLook.Crystal, new Vector2(0.1f, 0.6f), 2.6f, 1.3f);
    }

    // 地面の下を並走して位置を変える
    IEnumerator TunnelRun()
    {
        Note("TunnelRun");
        yield return Burrow(0.4f);
        yield return TunnelTo(halfWidth + Random.Range(3f, 8f), 9f, 1.4f);
        yield return Surface(0.35f);
    }

    // BREAK 明け: 結晶を飛び散らせて潜る(低い = 跳ぶ)
    IEnumerator RiseScatter()
    {
        riseAnswered = BreakEndedAt;
        Note("RiseScatter");
        animVariant = 0;
        yield return Telegraph(0.5f);
        OneShotSpriteEffect.CreateScatterBurst(CaveBossFx.CrystalShard(), CenterWorld, Theme, 10, 0.6f, 0.2f, 0.45f, 5f, 1.4f, RenderOrder.CombatFx);
        Th(ShockFromFront(0.9f, 10f, CaveLook.Crystal));
        PlayAttackPose(0.3f);
        yield return Wait(0.4f);
        yield return TunnelRun();
    }

    protected override void OnBattleTick(float dt)
    {
        base.OnBattleTick(dt);
        // BREAK 中: 胸の大結晶(核)がむき出しで光る
        FinalBossStats.ShowWeakPoint(core, Broken && !IsDead, new Vector3(worldX + facing * halfWidth * 0.45f, GroundY + yOffset + bodyHeight * 0.35f, 0f), 1.4f, Theme);
    }

    protected override void OnInterrupted()
    {
        base.OnInterrupted();
        if (core != null && IsDead) core.enabled = false;
    }
}

// ===================================================================== //
// 天空回廊 98km 天帝 セラフィエル(Sky Emperor)
//  六枚の翼と三重の光輪、三叉の光の錫杖。前方の空に浮かび、後退しながら攻撃する。
//  通常   光の羽根の連射   : 低い列(金 = 跳ぶ)/ 高い列(青紫 = 地面にいる)を交互に 3〜5本(windup_/attack_)
//  移動   光の槍           : 前方の地面に光の円 → 槍が刺さったまま迫ってくる(跳んで越える)。2〜3本(windupB_/attackB_)
//  上下   翼の薙ぎ払い     : 上の帯(地面にいる)/ 下の帯(跳ぶ)(どちらも windupC_/attackC_/recoverC_)。第3段階は両方続けて
//  強攻撃 裁きの光線       : 高さを示す細い線 → 太い光線が横切る(低い = 跳ぶ / 高い = 地面にいる)を2本(windupD_/attackD_/recoverD_)
//  必殺技 天の裁き         : 光輪が広がる → 上下交互の光の帯 3本(再戦の強化で4本)→ 最後に全体の閃光(地面が安全。光で示す)→ 降りてくる大きな隙(ult_)
//  BREAK  翼が畳まれ光輪にひび、跳べる高さまで降りてくる(膝つき)
//  BREAK 明け 翼を広げる風(弱い押し戻し = 少し遅くなるだけ。ダメージなし)
// ===================================================================== //
public class SeraphielBoss : SkyBossBase
{
    public static readonly Color Theme = new Color(0.56f, 0.44f, 1f, 1f);   // 予兆の縁取り(青紫)
    static readonly Color Gold = new Color(1f, 0.9f, 0.55f, 1f);
    const float HoverAlt = 1.0f;
    float HurtHalf => halfWidth * 0.42f; // 被弾の範囲(真ん中の体)の半幅(ConfigureHurtbox の幅の半分)
    SpriteRenderer haloCrack;
    float riseAnswered = -99f;

    protected override void OnInit()
    {
        HazardBattleSetup();
        animKey = "seraphiel";
        // 絵(1024、浮いているので体の中心で合わせた連番): 衣の裾 y=897 / 光輪の上 y=85 / 翼の先 x=118〜907
        FitCanvasArt(0.123f, 0.917f, 0.115f, 0.887f);
        frontReachRatio = 0.4f; // 「正面」は翼の先ではなく体(錫杖)
        restAltitude = HoverAlt;
        yOffset = HoverAlt;
        locoStyle = LocoStyle.Cloth;
        suppressLocoDust = true;
        artPoseProceduralScale = 0.35f;
        windupTint = Theme; windupTintScale = 0.6f;
        hitStopOnHit = 0.03f;
        // 間合いは翼ではなく体(被弾の範囲)で決める。普段は体の手前の端がプレイヤーの 4m 先、攻撃の後は近くへ来る(Opening)
        startGap = HurtHalf + 4f; minGap = HurtHalf + 0.6f; maxGap = HurtHalf + 9f;
        // 被弾: 真ん中の体(翼の先は外す)。衣の裾から光輪の下まで
        ConfigureHurtbox(new Vector2(0f, bodyHeight * 0.45f), new Vector2(halfWidth * 2f * 0.42f, bodyHeight * 0.84f));
        SetBodyTint(new Color(1f, 0.99f, 0.95f));
        haloCrack = FinalBossStats.WeakPoint(transform, Gold);
        SetVisualSortingOrder(RenderOrder.Boss - 1); // 大きな体がプレイヤー/予告を隠さないように、体は一つ奥へ
        SetAlpha(0f); // 登場は空の遠くから(DistantApproach)
    }

    protected override IEnumerator Enter()
    {
        Sfx(SkyBossSfx.Whoosh(), 0.6f);
        yield return DistantApproach(1.8f, startGap, HoverAlt, new Vector2(0.8f, 1.1f), 0.35f, new Color(0.95f, 0.92f, 1f, 0.75f));
        SkyBossFx.Flash(CenterWorld, Gold, 6f, 0.3f);
        SetPose(Pose.Landing);
        yield return Wait(0.4f);
        approachDone = true;
    }

    CaveHazard Th(CaveHazard h)
    {
        if (h != null) h.SetWarnTheme(Theme, 0.3f, true); // 青紫の縁取り
        return h;
    }
    static Color Mix(Color lane) => Color.Lerp(lane, Theme, 0.3f);
    void Lane(float relX, float width, float yLow, float yHigh, float dur, Color tint) => FinalBossStats.Lane(PlayerX + relX, width, yLow, yHigh, dur, tint);
    void Note(string move) { DebugAct = move; FinalBossStats.Note(this, move); }
    // 1回目の必殺技: 第2段階に入って 1.5秒たてば撃つ(段階が続けて上がると firstUltimateDelay の待ちが毎回やり直しになり、一度も撃たずに倒れることがあった)
    bool FirstUltDue => UltimatesUsed == 0 && Phase >= 2 && !BossBattle.UltimateActive && Time.time - phaseUnlockedAt >= 1.5f;
    bool RiseDue => BreakEndedAt > riseAnswered && Time.time - BreakEndedAt < 2.5f;

    protected override IEnumerator AI()
    {
        while (true)
        {
            if (RiseDue) { yield return WingGust(); continue; }
            if (UltimateReady(2) || FirstUltDue) { yield return HeavenJudgment(); continue; }
            if (SpecialReady(SpecialPhase)) { MarkSpecial(); yield return JudgmentBeam(); continue; }
            int pick = BossAiUtil.PickNoRepeat(3, ref lastPick);
            if (pick == 0) yield return FeatherVolley();
            else if (pick == 1) yield return LightSpears();
            else yield return WingSweep();
            if (Enraged && Random.value < 0.4f) { if (pick == 0) yield return WingSweep(); else yield return FeatherVolley(); }
        }
    }

    // 通常: 光の羽根の連射。低い列(金)/ 高い列(青紫)を交互に。撃ちながら後ろへ下がる
    IEnumerator FeatherVolley()
    {
        Note("FeatherVolley");
        animVariant = 0;
        int n = Phase >= 3 ? 5 : Phase >= 2 ? 4 : 3;
        yield return Telegraph(0.6f); // 翼を広げる
        StartCoroutine(Retreat(1.6f, n * 0.9f));
        bool high = Random.value < 0.5f;
        for (int i = 0; i < n && !IsDead; i++)
        {
            float y0 = high ? 2.15f : 0.05f, y1 = high ? 3.3f : 1.05f;
            float start = Mathf.Max(6f, Gap - 1f), speed = 13f;
            Lane(1.2f, 5f, y0, y1, start / speed, Mix(high ? HighLaneColor : LowLaneColor));
            PlayAttackPose(0.25f);
            var w = Th(WaveFrom(start, 1.3f, y0, y1, speed, high ? CaveLook.Wind : CaveLook.Holy, false, 0.7f));
            if (w != null) { w.SetBodySprite(SkyBossFx.Feather()); w.KeepBodyAspect(); w.SetTint(high ? new Color(0.7f, 0.6f, 1f, 1f) : Gold); }
            high = !high;
            yield return Wait(1.0f); // 高さが変わるので 1秒あける
        }
        relVelocity = 0f;
        yield return Opening(0.8f * RecoverMul);
    }

    // 移動: 光の槍。前方の地面に光の円 → 槍が刺さったまま迫ってくる(跳んで越える)
    IEnumerator LightSpears()
    {
        Note("LightSpears");
        animVariant = 1;
        yield return Telegraph(0.7f); // 錫杖を掲げる
        PlayAttackPose(0.4f);
        int n = Phase >= 2 ? 3 : 2;
        const float drift = 5.5f, warn = 0.7f;
        float last = 0f;
        for (int i = 0; i < n; i++)
        {
            float rel = 5.5f + i * 5f;
            float pass = (rel + 1.2f) / drift; // プレイヤーを通り過ぎるまで
            var c = Th(TellAt(rel, pass, CaveTellStyle.Circle, CaveLook.Holy, true));
            if (c != null) c.SetDrift(drift);
            var s = Th(FloorAt(rel, 1.0f, 1.15f, warn, Mathf.Max(0.3f, pass - warn), CaveLook.Holy));
            if (s != null) { s.SetDrift(drift); s.SetBodySprite(SkyBossFx.Spear()); }
            last = pass;
        }
        yield return Wait(last + 0.2f);
        yield return Opening(0.7f * RecoverMul);
    }

    // 上下の判断: 翼の薙ぎ払い(上の帯 = 地面にいる / 下の帯 = 跳ぶ)。第3段階は両方続けて
    IEnumerator WingSweep()
    {
        bool high = Random.value < 0.5f;
        int steps = Enraged ? 2 : 1;
        for (int s = 0; s < steps && !IsDead; s++, high = !high)
        {
            Note(high ? "WingSweepHigh" : "WingSweepLow");
            animVariant = 2; // コマ: 六枚の翼を引く → 横に薙ぐ(上も下も同じ絵。高さは帯の色で)
            Th(BandAt(0.6f, 7.5f, high ? 2.1f : 0f, high ? 4.2f : 1.15f, 0.85f, 0.3f, CaveLook.Wind));
            yield return Telegraph(0.85f);
            PlayAttackPose(0.3f);
            for (int i = 0; i < 4; i++) SkyDrift.Spawn(SkyBossFx.CloudPuff(), FrontWorld(-0.5f, high ? 3f : 0.6f), Vector2.one * Random.Range(0.8f, 1.4f), new Color(1f, 1f, 1f, 0.7f), new Vector3(-9f, Random.Range(-1f, 1f), 0f), 0.6f, RenderOrder.CombatFx, true);
            yield return Wait(0.45f);
            EndAttack();
        }
        yield return Opening(0.8f * RecoverMul);
    }

    // 強攻撃: 裁きの光線。高さを示す細い線 → 太い光線が横切る(低い = 跳ぶ / 高い = 地面にいる)を 2本
    IEnumerator JudgmentBeam()
    {
        Note("JudgmentBeam");
        animVariant = 3; // コマ: 錫杖を前へ構えて細い線 → 太い光線
        yield return SetAltitude(HoverAlt + 0.8f, 0.3f);
        bool low = Random.value < 0.5f;
        for (int i = 0; i < 2 && !IsDead; i++, low = !low)
        {
            float y0 = low ? 0f : 2.1f, y1 = low ? 1.15f : 3.7f, mid = (y0 + y1) * 0.5f;
            Lane(2f, 18f, mid - 0.1f, mid + 0.1f, 0.55f, Mix(low ? LowLaneColor : HighLaneColor)); // 高さを示す細い線
            yield return Wait(0.45f);
            Th(BandAt(1.5f, 16f, y0, y1, 0.75f, 0.45f, CaveLook.Holy));
            yield return Telegraph(0.75f);
            PlayAttackPose(0.45f);
            Shake(0.12f, 0.25f);
            yield return Wait(0.6f);
            EndAttack();
        }
        yield return SetAltitude(HoverAlt, 0.35f);
        yield return Opening(1.0f * RecoverMul);
    }

    // 必殺技: 天の裁き。光輪が広がる → 上下交互の光の帯 → 最後に全体の閃光(地面が安全)→ 降りてくる大きな隙
    IEnumerator HeavenJudgment()
    {
        if (!StartUlt("天の裁き", Gold)) yield break;
        Note("Ultimate");
        UltBeat("start 天の裁き");
        animVariant = 0; // コマ: ult_(無ければ待機)
        yield return MoveToGap(startGap + 1f, 8f, 1.0f);
        yield return SetAltitude(HoverAlt + 1.2f, 0.4f);
        Th(TellAt(0f, 1.2f, CaveTellStyle.Circle, CaveLook.Holy, true, true)); // 光輪が広がる
        SkyBossFx.Flash(CenterWorld, Gold, 7f, 0.35f);
        Glow(1.2f, Gold);
        yield return Telegraph(1.1f);
        var seq = Upgraded ? new[] { false, true, false, true } : new[] { false, true, false };
        for (int i = 0; i < seq.Length && !IsDead; i++)
        {
            bool high = seq[i];
            UltBeat(high ? "band high" : "band low");
            PlayAttackPose(0.35f);
            Th(BandAt(0.8f, 12f, high ? 2.1f : 0f, high ? 4.2f : 1.15f, 0.9f, 0.35f, CaveLook.Holy, true));
            yield return Wait(1.3f);
        }
        // 最後に全体の閃光: 地面(安全な高さ)を光で示す → 上は全部光る(地面にいる)
        UltBeat("final flash");
        BossBattleHud.Banner("地面へ!", Gold, 1.0f);
        Lane(1f, 16f, 0f, 0.3f, 1.2f, new Color(0.75f, 1f, 0.8f, 1f)); // 安全な高さ(白緑の光)
        Th(BandAt(1f, 16f, 1.9f, 7f, 1.0f, 0.45f, CaveLook.Holy, true));
        PlayAttackPose(1.2f);
        yield return Wait(1.6f);
        SkyBossFx.Flash(CenterWorld, Color.white, 9f, 0.3f);
        UltBeat("recovery");
        yield return CaveRecovery(4.5f, "光輪が砕けた! 大チャンス!", new Vector2(1f, 0.92f), CaveLook.Holy, new Vector2(0f, 0.85f), 2.6f, 1.3f, 0.2f);
    }

    // BREAK 明け: 翼を広げる風(弱い押し戻し。ダメージなし、少し遅くなるだけ)
    IEnumerator WingGust()
    {
        riseAnswered = BreakEndedAt;
        Note("WingGust");
        animVariant = 2;
        yield return Telegraph(0.45f);
        Th(BandAt(0.5f, 6f, 0f, 2.6f, 0.4f, 0.4f, CaveLook.Wind, false, 0, 0.75f, 0.5f));
        PlayAttackPose(0.35f);
        for (int i = 0; i < 6; i++) SkyDrift.Spawn(SkyBossFx.CloudPuff(), FrontWorld(-0.5f, Random.Range(0.3f, 2.4f)), Vector2.one * Random.Range(0.8f, 1.5f), new Color(1f, 1f, 1f, 0.75f), new Vector3(-11f, Random.Range(-1f, 1f), 0f), 0.7f, RenderOrder.CombatFx, true);
        yield return Wait(0.5f);
        yield return Recover(0.5f);
    }

    // 攻撃の後の隙: 体の手前の端がプレイヤーの 1.2m 先まで降りてくる(後退しながら撃つ → 寄ってきて隙、の繰り返し)
    IEnumerator Opening(float seconds)
    {
        DebugAct = "Opening";
        EndAttack();
        if (Mathf.Abs(yOffset - HoverAlt) > 0.05f) StartCoroutine(SetAltitude(HoverAlt, 0.3f));
        yield return MoveToGap(HurtHalf + 1.2f, 10f, 0.9f);
        yield return Recover(seconds);
    }

    protected override void OnBattleTick(float dt)
    {
        base.OnBattleTick(dt);
        // BREAK 中: 光輪のひびが光る(頭の上)
        FinalBossStats.ShowWeakPoint(haloCrack, Broken && !IsDead, new Vector3(worldX + facing * halfWidth * 0.05f, GroundY + yOffset + bodyHeight * 0.86f, 0f), 1.6f, Gold);
    }

    protected override void SkyRestore()
    {
        if (haloCrack != null && IsDead) haloCrack.enabled = false;
    }
}
