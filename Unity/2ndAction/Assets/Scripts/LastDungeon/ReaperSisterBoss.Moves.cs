using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 死神三姉妹の主力の技(2026-10-20、マスター依頼)。仮の絵(手続きの弾/予兆 + 既存のコマ)。正式な絵は後で差し替える。
//  長女(赤黒・地上制圧)  大地断ち … 大鎌を引く(溜め) → 前方への巨大な横薙ぎ(近接) → 斬撃の跡 → 少し遅れて地割れ(地を這う、返せない、跳んで越える)
//                        断罪の扇 … 前方へ扇状の三日月の斬撃弾(大きめ、隙間が見える)。中央付近は返せる、外側は壊せる。2波
//  次女(紫黒・空間支配)  冥府の輪・散華 … 背後に黒い輪 → 遅い紫の玉を弧の形にばらまく(らせん状に向きをずらして3波)。3発に1発は返せる
//                        棺の口 … 空中で溜める → 足元に影と裂け目(予兆) → 地面から闇の顎が噛みつく(2回、返せない、跳んで避ける)
//  三女(青白・急襲/空中) 急降下断罪 … 楽しそうに高く浮く(溜め) → 着地点の印 → 斜め下へ急降下 → 着地の衝撃波(低い、返せない、跳んで越える)
//                        羽刃の雨 … 光を集める → 上から光の刃が雨のように降る(列ごとに予兆の光の線、隙間あり)。2波目の真上の刃は返せる
//  連携(三人同時)        死の舞踏 … 長女の地割れ → 0.9秒後に次女の輪の弾 → さらに0.9秒後に三女の急降下(ReaperFinaleBattle が合図)
//  予兆: どの技も溜めの姿勢(windup / windupB のコマ)+ 発射口の光 / 輪 / 地面の印 / 列の光の線 + 予兆の音(BossTelegraph/Heavy)。
//  技の番号(確認用 DebugForceMove): 0=大地断ち/冥府の輪/急降下  1=断罪の扇/棺の口/羽刃の雨  2=以前の小技
public partial class ReaperSisterBoss
{
    public static readonly string[][] MoveNames =
    {
        new[] { "EarthSever", "FanOfJudgment", "StepAndSweep" },   // 長女
        new[] { "UnderworldRing", "CoffinMaw", "DiveSlash" },      // 次女
        new[] { "JudgmentDive", "FeatherRain", "SkipRush" },       // 三女
    };
    public string LastMove { get; private set; } = "";
    public int MovesStarted { get; private set; }
    public static readonly Dictionary<string, int> MoveCounts = new Dictionary<string, int>();
    public static int CombosRun;

    // 連携技の合図(ReaperFinaleBattle)
    float comboAt = -1f;
    public bool ComboPending => comboAt > 0f;
    public void CueCombo(float at) { if (!IsDead && !Retreating) comboAt = at; }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public int DebugForceMove = -1; // 確認用: 次の技をこの番号にする(一度使うと -1 に戻る)
#endif

    BossHitbox sever, swoop;
    BossTelegraphMarker severMark;
    readonly List<GameObject> temp = new List<GameObject>();

    void InitMoves()
    {
        float h = bodyHeight;
        Color red = new Color(0.85f, 0.12f, 0.12f, 0.95f);
        // 大地断ち: 正面の広い横薙ぎ。地面から 1.1m の低い薙ぎ(基本の跳びの頂点は約 2m。1.1m より上にいる時間は約 0.6秒 = 跳べば越えられる)。
        // 続く地割れも跳んで越える(跳ぶ → 着地 → 跳ぶ のリズム)
        sever = NewHitbox("ReaperSever", new Vector2(halfWidth * 0.5f + 2.7f, 0.55f), new Vector2(5.4f, 1.1f), BossFx.Slash(), red);
        severMark = NewMarker(new Vector2(halfWidth * 0.5f + 2.7f, 0.55f), new Vector2(5.4f, 1.1f));
        // 急降下断罪の体当たり(体の前下)
        swoop = NewHitbox("ReaperSwoop", new Vector2(0.7f, 1.0f), new Vector2(2.0f, 2.0f), BossFx.Slash(), new Color(0.6f, 0.85f, 1f, 0.95f));
    }

    IEnumerator NextMove(int n)
    {
        int pick = (n % 5 == 0) ? 2 : (n % 2 == 1 ? 0 : 1); // 主力の2つを交互、5回に1回は以前の小技
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (DebugForceMove >= 0) { pick = DebugForceMove; DebugForceMove = -1; }
#endif
        yield return RunMove(pick);
    }

    IEnumerator RunMove(int pick)
    {
        int si = (int)sister;
        LastMove = MoveNames[si][Mathf.Clamp(pick, 0, 2)];
        MovesStarted++;
        MoveCounts[LastMove] = (MoveCounts.TryGetValue(LastMove, out int c) ? c : 0) + 1;
        Debug.Log($"[ReaperBoss] {sister} move {LastMove}");
        switch (sister)
        {
            case ReaperSister.Eldest:
                if (pick == 0) yield return EarthSever();
                else if (pick == 1) yield return FanOfJudgment();
                else yield return StepAndSweep();
                break;
            case ReaperSister.Second:
                if (pick == 0) yield return UnderworldRing(aggression > 1.1f ? 2 : 3);
                else if (pick == 1) yield return CoffinMaw();
                else yield return DiveSlash();
                break;
            default:
                if (pick == 0) yield return JudgmentDive();
                else if (pick == 1) yield return FeatherRain(aggression > 1.1f ? 1 : 2);
                else yield return SkipRush();
                break;
        }
        ClearTemp();
    }

    // ================================================================ 長女
    IEnumerator EarthSever()
    {
        animVariant = 0;
        yield return MoveToGap(4.6f, 4.5f, 2.5f);
        // 地割れの通り道の予兆(正面から手前へ、地面の帯)
        float len = 11f;
        var path = FrameMark("SeverPath", BossFx.WarnZone(), new Color(1f, 0.15f, 0.12f, 0f),
            new Vector3(worldX + facing * (FrontReach + len * 0.5f), GroundY + 0.45f, 0f), new Vector2(len, 0.9f), 1.6f, true, 0.45f);
        path.onTick = f => SetMarkAlpha(path, f < 0.62f ? Mathf.Lerp(0.25f, 0.6f, f / 0.62f) * Blink(f) : Mathf.Lerp(0.6f, 0f, (f - 0.62f) / 0.38f));
        yield return Telegraph(0.95f, severMark);
        yield return Strike(sever, 0.3f, 0.14f, 0.05f);
        // 斬撃の跡(地面に赤黒い三日月) → 少し遅れて地割れ
        var scar = FrameMark("SeverScar", ReaperFx.Fill(ReaperBulletShape.Crescent), new Color(0.35f, 0.02f, 0.05f, 0.9f),
            new Vector3(worldX + facing * (FrontReach + 2.2f), GroundY + 0.08f, 0f), new Vector2(3.6f, 0.5f), 1.2f, true, 0.08f);
        scar.onTick = f => SetMarkAlpha(scar, 0.9f * (1f - f));
        yield return Hold(0.32f);
        Shake(0.08f, 0.15f);
        FireFissure(7.5f);
        yield return Recover(0.9f);
    }

    void FireFissure(float speed)
    {
        Vector3 from = new Vector3(worldX + facing * (FrontReach + 0.6f), GroundY, 0f);
        var b = ReaperBullet.Spawn(this, ReaperBulletKind.Hazard, ReaperBulletShape.Fissure, from, new Vector2(1.5f, 0.75f), new Vector2(facing * speed, 0f), 2.2f);
        b.hugGround = true; b.groundOffset = 0f;
        ImpactDust(from, 6, 0.9f);
    }

    IEnumerator FanOfJudgment()
    {
        animVariant = 1;
        yield return MoveToGap(9f, 7f, 2.5f);
        MuzzleGlow(new Vector2(halfWidth * 0.5f + 0.5f, bodyHeight * 0.52f), 0.85f, 1.8f);
        yield return Telegraph(0.85f);
        yield return KeepDistance(6f);
        var B = ReaperBulletKind.Breakable; var R = ReaperBulletKind.Reflectable;
        FireFan(new[] { -24f, -12f, 0f, 12f, 24f }, new[] { B, B, R, B, B });
        if (aggression <= 1.1f)
        {
            yield return Hold(0.55f);
            FireFan(new[] { -18f, -6f, 6f, 18f }, new[] { B, R, R, B });
        }
        yield return Recover(0.9f);
    }

    void FireFan(float[] offs, ReaperBulletKind[] kinds)
    {
        PlayAttackPose(0.3f);
        Shake(0.06f, 0.12f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySeAt(SeId.BossAttack, CenterWorld);
        Vector3 from = FrontWorld(0.4f, bodyHeight * 0.5f);
        Vector2 aim = AimFrom(from);
        for (int i = 0; i < offs.Length; i++)
        {
            Vector2 d = Quaternion.Euler(0f, 0f, offs[i]) * aim;
            ReaperBullet.Spawn(this, kinds[i], ReaperBulletShape.Crescent, from, new Vector2(1.6f, 1.15f), d * 8.5f, 4.5f);
        }
    }

    // ================================================================ 次女
    IEnumerator UnderworldRing(int waves)
    {
        animVariant = 0;
        yield return MoveToGap(10f, 7f, 2.5f);
        // 背後の黒い輪(魔法陣): 溜めの間に広がって回る
        var ring = TempChild("UnderworldRing", BossFx.Ring(), new Color(0.35f, 0.08f, 0.5f, 0f), new Vector2(-0.5f, bodyHeight * 0.55f), RenderOrder.CombatFx - 1);
        var inner = TempChild("UnderworldRingInner", BossFx.Ring(), new Color(0.05f, 0.0f, 0.08f, 0f), new Vector2(-0.5f, bodyHeight * 0.55f), RenderOrder.CombatFx - 2);
        StartCoroutine(SpinRing(ring, inner, 0.9f + waves * 0.5f + 0.7f));
        yield return Telegraph(0.9f);
        yield return KeepDistance(6.5f);
        for (int w = 0; w < waves && !IsDead; w++)
        {
            FireArc(9, 150f, w * 7f, w);
            yield return Hold(0.5f);
        }
        yield return Recover(0.8f);
    }

    void FireArc(int n, float span, float phase, int wave)
    {
        PlayAttackPose(0.25f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySeAt(SeId.EnemyShot, CenterWorld);
        Vector3 from = CenterWorld + new Vector3(facing * 0.4f, bodyHeight * 0.05f, 0f);
        Vector2 aim = AimFrom(from);
        for (int i = 0; i < n; i++)
        {
            float a = -span * 0.5f + span * i / (n - 1) + phase;
            Vector2 d = Quaternion.Euler(0f, 0f, a) * aim;
            var k = (i + wave) % 3 == 1 ? ReaperBulletKind.Reflectable : ReaperBulletKind.Breakable;
            ReaperBullet.Spawn(this, k, ReaperBulletShape.Orb, from, new Vector2(1.0f, 1.0f), d * 4.4f, 6.5f);
        }
    }

    IEnumerator SpinRing(SpriteRenderer outer, SpriteRenderer inner, float life)
    {
        float t = 0f;
        while (t < life && outer != null)
        {
            t += Time.deltaTime;
            float grow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.7f));
            float fade = Mathf.Clamp01((life - t) / 0.35f);
            float sc = bodyHeight * (0.4f + 0.8f * grow);
            outer.transform.localScale = new Vector3(sc, sc, 1f);
            outer.transform.localRotation = Quaternion.Euler(0f, 0f, t * 120f);
            outer.color = new Color(0.62f, 0.25f, 0.9f, 0.85f * grow * fade);
            if (inner != null)
            {
                inner.transform.localScale = new Vector3(sc * 0.75f, sc * 0.75f, 1f);
                inner.transform.localRotation = Quaternion.Euler(0f, 0f, -t * 200f);
                inner.color = new Color(0.1f, 0.02f, 0.16f, 0.9f * grow * fade);
            }
            yield return null;
        }
    }

    IEnumerator CoffinMaw()
    {
        animVariant = 1;
        bob = false;
        yield return SetAltitude(3.2f, 0.5f);
        yield return MoveToGap(7.5f, 4f, 2f);
        StartCoroutine(MawAt(1.05f));
        yield return Telegraph(0.75f);
        PlayAttackPose(0.4f);
        yield return Hold(0.65f);
        StartCoroutine(MawAt(0.9f)); // 2回目(1回目を跳んで避けた着地の頃)
        yield return Hold(1.3f);
        yield return SetAltitude(baseHover, 0.5f);
        bob = true;
        yield return Recover(0.6f);
    }

    // プレイヤーの足元(走りの座標系で固定)に影と裂け目 → 地面から闇の顎が噛みつく
    IEnumerator MawAt(float warn)
    {
        if (player == null) yield break;
        float px = player.position.x;
        float? gh = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(px) : (float?)null;
        float gy = gh ?? player.position.y;
        var shadow = FrameMark("MawShadow", ReaperFx.Disc(), new Color(0.18f, 0.0f, 0.28f, 0f), new Vector3(px, gy + 0.2f, 0f), new Vector2(3.8f, 1.0f), warn + 0.45f, true, 0.2f);
        var rift = FrameMark("MawRift", ReaperFx.Rim(ReaperBulletShape.Orb), new Color(0.9f, 0.45f, 1f, 0f), new Vector3(px, gy + 0.22f, 0f), new Vector2(3.5f, 0.8f), warn + 0.45f, true, 0.22f);
        float wf = warn / (warn + 0.45f);
        shadow.onTick = f => SetMarkAlpha(shadow, f < wf ? Mathf.Lerp(0.2f, 0.8f, f / wf) : 0.8f * (1f - (f - wf) / (1f - wf)));
        rift.onTick = f => SetMarkAlpha(rift, f < wf ? Mathf.Lerp(0.15f, 0.9f, f / wf) * Blink(f) : 0f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySeAt(SeId.BossTelegraphHeavy, new Vector3(px, gy, 0f));
        yield return Hold(warn);
        if (IsDead || Retreating) yield break;
        ReaperBite.Spawn(this, shadow.transform.position, 2.6f, 1.15f, 0.32f); // 当たりは地面から 1.15m(顎の絵はもっと高く伸びる)
        Shake(0.09f, 0.15f);
    }

    // ================================================================ 三女
    IEnumerator JudgmentDive()
    {
        animVariant = 0;
        bob = false;
        yield return MoveToGap(7.2f, 5f, 2f);
        // 着地点の印(プレイヤーの少し前、走りの座標系で固定)
        float land = 4.2f; // 着地はプレイヤーの前(体当たりは当たらない距離)。来るのは着地の衝撃波(跳ぶ)
        float lx = player != null ? player.position.x + land : worldX;
        float? gh = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(lx) : (float?)null;
        var mark = FrameMark("DiveMark", ReaperFx.Disc(), new Color(0.75f, 0.92f, 1f, 0f), new Vector3(lx, (gh ?? GroundY) + 0.06f, 0f), new Vector2(3.6f, 0.9f), 1.3f, true, 0.06f);
        mark.onTick = f => SetMarkAlpha(mark, f < 0.6f ? Mathf.Lerp(0.35f, 0.9f, f / 0.6f) * Blink(f) : 0.9f * (1f - (f - 0.6f) / 0.4f));
        StartCoroutine(SetAltitude(5.6f, 0.55f)); // 楽しそうに高く浮く(溜め)
        StartCoroutine(Giggle(0.75f));
        yield return Telegraph(0.75f);
        // 急降下: 0.3秒で斜め下へ、着地点まで
        float dur = 0.3f;
        StartCoroutine(SetAltitude(0.05f, dur));
        StartCoroutine(DiveTo(land, dur)); // 降りている間もプレイヤー(攻撃の踏み込みで動く)の前の着地点へ合わせ続ける
        yield return Strike(swoop, dur + 0.06f, 0.0f, 0.0f);
        Debug.Log($"[ReaperBoss] {sister} dive landed at gap {Gap:F1}m (target {land:F1}m)");
        // 着地の衝撃波(低い、返せない)
        Shake(0.12f, 0.2f);
        ImpactDust(new Vector3(worldX, GroundY + 0.1f, 0f), 9, 1.1f);
        GroundRing(new Vector3(worldX, GroundY, 0f), 4.5f, new Color(0.75f, 0.92f, 1f, 0.9f));
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySeAt(SeId.SlamImpact, CenterWorld);
        var w1 = ReaperBullet.Spawn(this, ReaperBulletKind.Hazard, ReaperBulletShape.Fissure, new Vector3(worldX + facing * 0.8f, GroundY, 0f), new Vector2(0.8f, 0.42f), new Vector2(facing * 10f, 0f), 1.1f);
        w1.hugGround = true; w1.groundOffset = 0f;
        var w2 = ReaperBullet.Spawn(this, ReaperBulletKind.Hazard, ReaperBulletShape.Fissure, new Vector3(worldX - facing * 0.8f, GroundY, 0f), new Vector2(0.8f, 0.42f), new Vector2(-facing * 10f, 0f), 0.8f);
        w2.hugGround = true; w2.groundOffset = 0f;
        yield return Recover(0.45f);
        yield return Leap(0.5f, 2.2f, 4.5f); // 跳ねて間合いへ戻る(着地の隙は 0.45秒 = 反撃の機会)
        bob = true;
    }

    IEnumerator DiveTo(float land, float dur)
    {
        float t = 0f;
        while (t < dur && !IsDead)
        {
            t += Time.deltaTime;
            float remain = Gap - land; // 三人同時でも、着地はプレイヤーの前(並びの位置 slotOffset は使わない)
            relVelocity = facing * Mathf.Max(0f, remain) / Mathf.Max(0.04f, dur - t + Time.deltaTime) / Mathf.Max(0.1f, MoveScale); // 移動の倍率(ラスダン)で行き過ぎない
            yield return null;
        }
        relVelocity = 0f;
    }

    IEnumerator Giggle(float dur)
    {
        float t = 0f;
        while (t < dur && !IsDead)
        {
            t += Time.deltaTime;
            extraScale = new Vector2(1f + 0.05f * Mathf.Sin(t * 30f), 1f - 0.05f * Mathf.Sin(t * 30f));
            yield return null;
        }
        extraScale = Vector2.one;
    }

    IEnumerator FeatherRain(int waves)
    {
        animVariant = 1;
        bob = false;
        yield return MoveToGap(9f, 7f, 2f);
        StartCoroutine(SetAltitude(2.4f, 0.5f));
        MuzzleGlow(new Vector2(0f, bodyHeight * 0.85f), 0.9f, 2.2f);
        StartCoroutine(GatherLight(0.9f));
        yield return Telegraph(0.9f);
        PlayAttackPose(0.5f);
        var B = ReaperBulletKind.Breakable; var R = ReaperBulletKind.Reflectable;
        // 1波目: プレイヤーの真上(0)は空ける。2波目: 1m ずらして、真上の刃は返せる(上攻撃で返す/壊して道を作る)
        StartCoroutine(RainWave(new[] { -4f, -2f, 2f, 4f, 6f, 8f }, new[] { B, B, B, B, B, B }));
        if (waves > 1)
        {
            yield return Hold(0.7f);
            StartCoroutine(RainWave(new[] { -3f, -1f, 0f, 1f, 3f, 5f, 7f }, new[] { B, B, R, B, B, R, B }));
        }
        yield return Hold(1.4f);
        yield return SetAltitude(0f, 0.4f);
        bob = true;
        yield return Recover(0.7f);
    }

    IEnumerator RainWave(float[] offs, ReaperBulletKind[] kinds)
    {
        if (player == null) yield break;
        var cam = Camera.main;
        float top = cam != null ? cam.transform.position.y + cam.orthographicSize + 1.2f : player.position.y + 9f;
        float px = player.position.x;
        // 列ごとの光の線(0.5秒前)
        foreach (float o in offs)
        {
            float x = px + o;
            float? g = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(x) : (float?)null;
            float gy = g ?? player.position.y;
            float h = top - gy;
            var line = FrameMark("RainLine", BossFx.Block(), new Color(0.75f, 0.92f, 1f, 0f), new Vector3(x, gy + h * 0.5f, 0f), new Vector2(0.4f, h), 0.55f, false, 0f);
            line.onTick = f => SetMarkAlpha(line, 0.55f * Mathf.Sin(f * Mathf.PI) * Blink(f));
        }
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySeAt(SeId.BossTelegraph, CenterWorld);
        yield return Hold(0.5f);
        if (IsDead || Retreating) yield break;
        // 走った分だけ列も流れているので、今のプレイヤーの位置からの同じ距離に落とす(走りの座標系で固定)
        float drift = player.position.x - px;
        for (int i = 0; i < offs.Length; i++)
            ReaperBullet.Spawn(this, kinds[i], ReaperBulletShape.Blade, new Vector3(px + drift + offs[i], top, 0f), new Vector2(0.6f, 1.55f), new Vector2(0f, -11f), 2.6f);
    }

    IEnumerator GatherLight(float dur)
    {
        float t = 0f, next = 0f;
        while (t < dur && !IsDead)
        {
            t += Time.deltaTime;
            if (t >= next)
            {
                next = t + 0.06f;
                Vector2 off = Random.insideUnitCircle.normalized * Random.Range(1.6f, 2.8f);
                var g = TempChild("Gather", OneShotSpriteEffect.SoftDotSprite(), new Color(0.85f, 0.95f, 1f, 0.9f), new Vector2(off.x, bodyHeight * 0.85f + off.y), RenderOrder.CombatFx + 1);
                g.transform.localScale = Vector3.one * 0.35f;
                StartCoroutine(Converge(g, new Vector3(0f, bodyHeight * 0.85f, 0f), 0.3f));
            }
            yield return null;
        }
    }

    IEnumerator Converge(SpriteRenderer g, Vector3 to, float dur)
    {
        if (g == null) yield break;
        Vector3 from = g.transform.localPosition;
        float t = 0f;
        while (t < dur && g != null)
        {
            t += Time.deltaTime;
            g.transform.localPosition = Vector3.Lerp(from, to, t / dur);
            yield return null;
        }
        if (g != null) { temp.Remove(g.gameObject); Destroy(g.gameObject); }
    }

    // ================================================================ 連携技「死の舞踏」(三人同時の時、ReaperFinaleBattle が合図)
    IEnumerator ComboTurn()
    {
        // 合図の時刻まで間合いで待つ(歩き/浮遊の揺れはそのまま)
        while (Time.time < comboAt && !IsDead && !Retreating) { relVelocity = 0f; yield return null; }
        comboAt = -1f;
        if (IsDead || Retreating) yield break;
        CombosRun++;
        LastMove = "DanseMacabre_" + sister;
        MoveCounts[LastMove] = (MoveCounts.TryGetValue(LastMove, out int c) ? c : 0) + 1;
        Debug.Log($"[ReaperBoss] {sister} combo part (Danse Macabre)");
        BossAttackGate.NextTime = Time.time; // 合図どおりの時間差で出す(複数体の予備動作のずらしを待たない)
        StartCoroutine(ComboFlash());
        switch (sister)
        {
            case ReaperSister.Eldest:
                // 足場を制限: 地割れ(跳ぶ)
                animVariant = 0;
                yield return Telegraph(0.55f);
                PlayAttackPose(0.35f);
                FireFissure(8f);
                yield return Recover(0.6f);
                break;
            case ReaperSister.Second:
                animVariant = 0;
                yield return Telegraph(0.55f);
                FireArc(9, 140f, 0f, 1);
                yield return Recover(0.6f);
                break;
            default:
                yield return JudgmentDive();
                break;
        }
        ClearTemp();
        yield return Wait(0.6f);
    }

    IEnumerator ComboFlash()
    {
        Color glow = ReaperFx.SisterGlow(sister);
        OneShotSpriteEffect.CreateTweened(BossFx.Ring(), CenterWorld, glow, 0.35f, bodyHeight * 0.3f, bodyHeight * 1.2f, 0.9f, 0f, default, 0f, RenderOrder.CombatFx, 0.1f);
        float t = 0f;
        while (t < 0.5f && !IsDead)
        {
            t += Time.deltaTime;
            SetBodyTint(Color.Lerp(tint, glow, Mathf.PingPong(t * 6f, 1f)));
            yield return null;
        }
        SetBodyTint(tint);
    }

    // ================================================================ 返された弾
    public void TakeReflectedHit(ReaperBullet b)
    {
        int atk = PlayerController.Instance != null ? PlayerController.Instance.EffectiveBossAttackPower : 2 * CombatScale.K;
        // 返した弾は普通の攻撃の 1.5 倍 + 崩し(staggerReflect)。返されたことが分かるように光る
        TakeReflectedHit(Mathf.Max(1, Mathf.RoundToInt(atk * 1.5f)), 1f, b != null ? b.transform.position : CenterWorld);
        OneShotSpriteEffect.CreateTweened(BossFx.Ring(), CenterWorld, new Color(1f, 0.95f, 0.7f, 0.9f), 0.28f, bodyHeight * 0.25f, bodyHeight * 0.95f, 0.9f, 0f, default, 0f, RenderOrder.CombatFx, 0.05f);
    }

    // 飛び道具の直前: プレイヤーが攻撃の踏み込みで近づいていたら、軽く跳び退いて間合いを取る(目の前で撃たない)
    IEnumerator KeepDistance(float min)
    {
        float t = 0f;
        while (Gap - slotOffset < min && t < 0.45f && !IsDead)
        {
            t += Time.deltaTime;
            relVelocity = -facing * 16f;
            yield return null;
        }
        relVelocity = 0f;
    }

    // ================================================================ 小さな道具
    IEnumerator Hold(float seconds)
    {
        float t = 0f;
        while (t < seconds && !IsDead) { t += Time.deltaTime; yield return null; }
    }

    static float Blink(float f) => 0.65f + 0.35f * Mathf.Sin(f * 40f);

    ReaperFrameFollower FrameMark(string name, Sprite s, Color c, Vector3 pos, Vector2 scale, float life, bool snapGround, float groundOffset)
    {
        var m = ReaperFrameFollower.Make("Reaper" + name, s, c, pos, scale, life, RenderOrder.CombatFx + 1, snapGround, groundOffset); // 地面の絵より手前(以前は奥で、影/帯が見えなかった)
        temp.Add(m.gameObject);
        return m;
    }

    static void SetMarkAlpha(ReaperFrameFollower m, float a)
    {
        if (m == null) return;
        var r = m.GetComponent<SpriteRenderer>();
        if (r == null) return;
        var c = r.color; c.a = Mathf.Clamp01(a); r.color = c;
    }

    SpriteRenderer TempChild(string name, Sprite s, Color c, Vector2 local, int order)
    {
        var g = new GameObject("Reaper" + name);
        g.transform.SetParent(transform, false);
        g.transform.localPosition = new Vector3(local.x * Mathf.Sign(facing), local.y, 0f); // 判定と同じ「正面が +x」
        var r = g.AddComponent<SpriteRenderer>();
        r.sprite = s; r.color = c; r.sortingOrder = order;
        temp.Add(g);
        return r;
    }

    // 発射口の光(溜めの間に大きく明るくなる)
    void MuzzleGlow(Vector2 local, float dur, float maxScale)
    {
        var g = TempChild("Muzzle", OneShotSpriteEffect.SoftDotSprite(), ReaperFx.SisterGlow(sister), local, RenderOrder.CombatFx + 1);
        StartCoroutine(GrowGlow(g, dur, maxScale));
    }

    IEnumerator GrowGlow(SpriteRenderer g, float dur, float maxScale)
    {
        float t = 0f;
        Color c = g != null ? g.color : Color.white;
        while (t < dur + 0.25f && g != null)
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / dur);
            float s = Mathf.Lerp(0.2f, maxScale, f) * (1f + 0.12f * Mathf.Sin(t * 30f));
            g.transform.localScale = new Vector3(s, s, 1f);
            c.a = t <= dur ? Mathf.Lerp(0.3f, 0.95f, f) : Mathf.Lerp(0.95f, 0f, (t - dur) / 0.25f);
            g.color = c;
            yield return null;
        }
        if (g != null) { temp.Remove(g.gameObject); Destroy(g.gameObject); }
    }

    void ClearTemp()
    {
        foreach (var g in temp) if (g != null) Destroy(g);
        temp.Clear();
        extraScale = Vector2.one;
    }
}

// 棺の口の噛みつき(地面から闇の顎が上下に閉じる)。走りの座標系で固定、返せない。1回の噛みつきで当たるのは1回だけ
public class ReaperBite : MonoBehaviour
{
    public static int Spawned, PlayerHits;
    float age, active;
    bool hit;
    SpriteRenderer upper, lower;
    BoxCollider2D col;
    Vector2 size;

    public static ReaperBite Spawn(ReaperSisterBoss owner, Vector3 groundPos, float width, float height, float active)
    {
        var go = new GameObject("ReaperBite");
        go.transform.position = groundPos;
        var b = go.AddComponent<ReaperBite>();
        b.size = new Vector2(width, height);
        b.active = active;
        var rb = go.AddComponent<Rigidbody2D>(); rb.bodyType = RigidbodyType2D.Kinematic; rb.gravityScale = 0f;
        b.col = go.AddComponent<BoxCollider2D>(); b.col.isTrigger = true;
        b.col.size = b.size; b.col.offset = new Vector2(0f, height * 0.5f);
        b.upper = b.Jaw("Upper", true); b.lower = b.Jaw("Lower", false);
        var f = go.AddComponent<ReaperFrameFollower>(); f.life = active + 0.35f; f.snapGround = true; f.groundOffset = 0f;
        Spawned++;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySeAt(SeId.BossAttack, groundPos);
        return b;
    }

    SpriteRenderer Jaw(string name, bool top)
    {
        var g = new GameObject(name);
        g.transform.SetParent(transform, false);
        var r = g.AddComponent<SpriteRenderer>();
        r.sprite = BossFx.Fang();
        r.color = new Color(0.16f, 0.02f, 0.22f, 1f);
        r.sortingOrder = RenderOrder.CombatFx + 2;
        g.transform.localScale = new Vector3(size.x, size.y * 0.85f * (top ? -1f : 1f), 1f); // 絵は当たりより大きめ(読みやすさ)
        return r;
    }

    void Update()
    {
        age += Time.deltaTime;
        // 顎が地面から伸びて閉じる(0〜0.12秒)→ 開いて沈む
        float close = Mathf.Clamp01(age / 0.12f);
        float sink = Mathf.Clamp01((age - active) / 0.3f);
        float h = size.y * (0.3f + 0.7f * close) * (1f - sink);
        if (upper != null) upper.transform.localPosition = new Vector3(0f, Mathf.Lerp(size.y * 1.1f, size.y * 0.72f, close) * (1f - sink), 0f);
        if (lower != null) lower.transform.localPosition = new Vector3(0f, size.y * 0.25f * (1f - sink), 0f);
        var c = new Color(0.16f, 0.02f, 0.22f, 1f - sink);
        if (upper != null) upper.color = c;
        if (lower != null) lower.color = c;
        if (col != null) col.enabled = age <= active;
        if (h < 0f) h = 0f;
    }

    void OnTriggerStay2D(Collider2D other)
    {
        if (hit || col == null || !col.enabled || !other.CompareTag("Player") || PlayerController.Instance == null) return;
        hit = true;
        PlayerHits++;
        PlayerController.Instance.TakeDamage(source: "ReaperBite", amount: BossManager.ScaleDamage(CombatScale.PlayerHit));
    }
}
