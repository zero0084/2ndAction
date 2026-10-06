#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// #100 ULTIMATE(2026-10-04)の自動テスト: -qaUltimate <dir> [-qaUltOnly ALGSEBRT]
//  A: 12キャラ × 発動 → 画面内の敵 → 前進 → 通常へ戻る → BUFF の終わり(荒野街道 Lv5)
//  L: Lv1/5/9 の前進/BUFF/ダメージ(重い敵/精鋭が低Lvで残るか)
//  G: Gauge(距離だけ/高速の上限/撃破/ボス/発動中は溜まらない/二重発動しない/Lvの溜まりやすさ)
//  S: 3ステージ × 何度も発動(穴/段差/天井/分岐の上でも落ちない/潜らない/止まらない)
//  E: 端の場面(ボス関門の手前/1000mの境目/BONUS ZONE/100km の手前/停止中/穴の直前)
//  B: ボス戦(アリーナ: ダメージの上限/戦闘が続く/距離が進まない)
//  R: CONTINUE(Gauge と Lv が戻る)
//  T: ULTIMATE TEST(DEBUG RUN: BEST / 選択中のキャラ / 中断データが変わらない)
public partial class QaSweep
{
    UltimateArt Ua => UltimateArt.Instance;
    readonly List<float> ultFrameMs = new List<float>();

    IEnumerator UltimateMode()
    {
        var snapSave = SaveSystem.Capture();
        autoPickHold = true;
        string only = Arg("-qaUltOnly", "ALGSEBRT");
        if (only.Contains('A')) yield return UltAll();
        if (only.Contains('L')) yield return UltLevels();
        if (only.Contains('G')) yield return UltGauge();
        if (only.Contains('S')) yield return UltStages();
        if (only.Contains('E')) yield return UltEdges();
        if (only.Contains('B')) yield return UltBoss();
        if (only.Contains('R')) yield return UltContinue();
        if (only.Contains('T')) yield return UltDebugMenu();
        GameManager.BlockExpGain = false;
        autoPickHold = false;
        SaveSystem.Restore(snapSave);
        CardInventory.ReloadFromPrefs();
        RunCheckpoint.Reload();
        L("[ult] test machine save restored");
    }

    // ===================================================================== 共通
    IEnumerator UltBegin(string ch, string stage, int lv)
    {
        yield return V3Begin(ch, stage);
        V3Reset();
        V3Apply(UltimateArt.CardId, lv);
    }

    void UltWarp(float d)
    {
        gm.DebugWarpToDistance(d);
        gm.DebugResetDistanceExclusion();
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (e != null) Destroy(e.gameObject);
        foreach (var o in FindObjectsByType<ObstacleController>(FindObjectsSortMode.None)) if (o != null) Destroy(o.gameObject);
        if (TerrainManager.Instance != null) TerrainManager.Instance.ClearAllEnemies();
    }

    // その距離の普通の HP の敵を、プレイヤーの前 dx m の地面に出す
    EnemyController UltSpawn(float dx, string id = "goblin")
    {
        var tm = TerrainManager.Instance;
        var def = EnemyDatabase.FindById(id);
        if (tm == null || def == null) return null;
        float x = pc.transform.position.x + dx;
        tm.GenerateNow(x + 20f);
        for (int k = 0; k < 30 && !tm.GetHeightAt(x).HasValue; k++) x += 0.5f;
        float? gy = tm.GetHeightAt(x);
        if (!gy.HasValue) return null;
        var go = tm.SpawnEncounterEnemy(def, new Vector2(x, gy.Value), EnemyAiTier.T0, def.behaviorKind);
        return go != null ? go.GetComponent<EnemyController>() : null;
    }

    // 発動して終わるまで待つ(フレーム時間/天井/地面も見る)
    IEnumerator UltFire(string tag, System.Action<UltimateArt.Record> after = null, bool expectOk = true)
    {
        float wr = 0f;
        while ((pc.IsReacting || gm.UltimateChoiceOpen || gm.IsBossPresentationActivePublic) && wr < 8f) { yield return null; wr += Time.unscaledDeltaTime; }
        Ua.DebugSetGauge(100f);
        int lives0 = gm.Lives;
        bool ok = Ua.TryActivate("qa " + tag);
        if (expectOk) Check(ok, $"[{tag}] activates (gauge 100%) {(ok ? "" : "blocked: " + Ua.LastBlockReason)}");
        if (!ok) yield break;
        Check(Ua.Gauge == 0f, $"[{tag}] activation consumes the gauge");
        Check(!Ua.TryActivate("qa double"), $"[{tag}] a second press while active does nothing");
        ultFrameMs.Clear();
        float w = 0f; int belowGround = 0, aboveCeiling = 0;
        var tm = TerrainManager.Instance;
        bool shots = Arg("-qaUltShots", "0") == "1";
        float[] shotAt = { 0.3f, 0.62f, 1.4f, 2.2f, 2.95f };
        int shotIdx = 0;
        while (Ua.Active && w < 10f)
        {
            yield return null;
            w += Time.unscaledDeltaTime;
            if (shots && shotIdx < shotAt.Length && w >= shotAt[shotIdx])
            {
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, $"shot_{tag.Replace(':', '_').Replace('#', '_')}_{shotIdx}.png"));
                shotIdx++;
            }
            ultFrameMs.Add(Time.unscaledDeltaTime * 1000f);
            float x = pc.transform.position.x, y = pc.transform.position.y;
            float? g = tm.GetHeightAt(x);
            if (g.HasValue && y < g.Value + pc.groundOffset - 0.05f) belowGround++;
            float? c = tm.GetCeilingLimitY(x);
            if (c.HasValue && g.HasValue && c.Value > g.Value + pc.groundOffset + 0.1f && y > c.Value + 0.05f) aboveCeiling++;
        }
        var r = Ua.Last;
        Check(!Ua.Active, $"[{tag}] ends (took {w:F1}s)");
        Check(belowGround == 0, $"[{tag}] never inside the ground during the move ({belowGround} frames)");
        Check(aboveCeiling == 0, $"[{tag}] never above the cave ceiling ({aboveCeiling} frames)");
        Check(r != null && r.landedOnGround, $"[{tag}] ends standing on ground (y {r?.landY:F2} ground {r?.landGroundY:F2})");
        Check(r != null && r.livesAfter >= r.livesBefore && gm.Lives >= lives0, $"[{tag}] no damage during the ULTIMATE ({lives0} -> {gm.Lives})");
        Check(Ua.BuffActive, $"[{tag}] BUFF starts after the ULTIMATE");
        float avg = ultFrameMs.Count > 0 ? ultFrameMs.Average() : 0f, max = ultFrameMs.Count > 0 ? ultFrameMs.Max() : 0f;
        if (r != null) L($"[{tag}] {r.character} Lv{r.level} {(r.arena ? "ARENA" : $"advance {r.d1 - r.d0:F1}m (plan {r.plannedAdvance:F0}, limited {r.limitedAdvance:F0}{(string.IsNullOrEmpty(r.limitReason) ? "" : " " + r.limitReason)})")} t={r.seconds:F2}s pulses={r.pulses} events={r.damageEvents} hit/killed={r.mobsHit}/{r.mobsKilled} boss={r.bossDamage} ({r.bossFractionMax * 100f:F1}%) buff={r.buffSeconds:F1}s frames avg {avg:F1}ms max {max:F1}ms{(r.aborted ? " CUT SHORT " + r.abortReason : "")}");
        after?.Invoke(r);
        // 終わった直後に止まっていない(壁に引っかかっていない)
        float x0 = pc.transform.position.x;
        yield return new WaitForSeconds(0.6f);
        Check(pc.transform.position.x > x0 + 1f || gm.IsGameOver || (Bm != null && Bm.HoldsRun), $"[{tag}] keeps running after the ULTIMATE (+{pc.transform.position.x - x0:F1}m in 0.6s)");
    }

    // ===================================================================== A
    IEnumerator UltAll()
    {
        L("== A: 12キャラ × 発動 → 画面内の敵 → 前進 → 通常へ → BUFF の終わり(荒野街道 Lv5) ==");
        var sb = new StringBuilder("char\tadvance\tseconds\tmobsHit\tkilled\tdamageEvents\tmobDamage\tbuff\tavgMs\tmaxMs\n");
        foreach (var ch in EndgameDebug.UltCharacters)
        {
            yield return UltBegin(ch, "wasteland_road", 5);
            UltWarp(3150f);
            yield return new WaitForSeconds(1.2f);
            int atk0 = pc.EffectiveAttackPower;
            float tempo0 = pc.AttackSpeedMultiplier;
            var near = new List<EnemyController>();
            foreach (float dx in new[] { 5f, 8f, 11f, 14f }) { var e = UltSpawn(dx); if (e != null) near.Add(e); }
            var far = new List<EnemyController>();
            foreach (float dx in new[] { 55f, 95f }) { var e = UltSpawn(dx); if (e != null) far.Add(e); }
            yield return null;
            int total = UltimateArt.MobDamageTotal(5);
            int goblinHp = near.Count > 0 ? near[0].maxHp : 0;
            UltimateArt.Record rec = null;
            yield return UltFire(ch, r => rec = r);
            if (rec == null) { yield return V3End(); continue; }
            float adv = rec.d1 - rec.d0;
            Check(adv > 135f && adv < 168f, $"[A:{ch}] Lv5 advances about 150m ({adv:F1})");
            Check(near.All(e => e == null || e.IsDying), $"[A:{ch}] the {near.Count} goblins on screen are defeated (goblin HP {goblinHp}, ULTIMATE {total})");
            Check(far.Count(e => e == null || e.IsDying) >= 1, $"[A:{ch}] enemies that come into view during the move are hit too ({far.Count(e => e == null || e.IsDying)}/{far.Count})");
            Check(rec.damageEvents <= rec.mobsHit * 2 + 4, $"[A:{ch}] few gameplay damage events ({rec.damageEvents} for {rec.mobsHit} enemies)");
            Check(Mathf.Abs(Ua.BuffRemaining - 8f) < 0.9f, $"[A:{ch}] BUFF about 8s at Lv5 ({Ua.BuffRemaining:F1}s)");
            int atk1 = pc.EffectiveAttackPower;
            Check(atk1 > atk0 && atk1 <= Mathf.CeilToInt(atk0 * 1.2f) + 1, $"[A:{ch}] BUFF raises attack modestly ({atk0} -> {atk1})");
            Check(pc.AttackSpeedMultiplier < tempo0, $"[A:{ch}] BUFF raises attack speed ({tempo0:F3} -> {pc.AttackSpeedMultiplier:F3})");
            float avg = ultFrameMs.Count > 0 ? ultFrameMs.Average() : 0f, max = ultFrameMs.Count > 0 ? ultFrameMs.Max() : 0f;
            sb.AppendLine($"{ch}\t{adv:F1}\t{rec.seconds:F2}\t{rec.mobsHit}\t{rec.mobsKilled}\t{rec.damageEvents}\t{rec.mobDamage}\t{rec.buffSeconds:F1}\t{avg:F1}\t{max:F1}");
            // BUFF の終わりまで(守りも残らない)
            float w = 0f;
            while (Ua.BuffActive && w < 14f) { yield return null; w += Time.deltaTime; }
            Check(!Ua.BuffActive && !UltimateArt.ProtectsFromHit, $"[A:{ch}] BUFF and protection end ({w:F1}s)");
            Check(pc.EffectiveAttackPower <= atk0 + 1, $"[A:{ch}] attack returns after the BUFF ({pc.EffectiveAttackPower} vs {atk0})");
            yield return V3End();
        }
        V3Write("ult_chars.tsv", sb);
    }

    // ===================================================================== L
    IEnumerator UltLevels()
    {
        L("== L: Lv1/5/9 の前進 / BUFF / ダメージ ==");
        var sb = new StringBuilder("lv\tadvance\tbuff\tmobTotal\tenemyHp1\tratio\togreSurvived\teliteSurvived\n");
        foreach (int lv in new[] { 1, 5, 9 })
        {
            yield return UltBegin("swordsman", "wasteland_road", lv);
            UltWarp(5150f);
            yield return new WaitForSeconds(1.2f);
            int hp1 = DistanceTierManager.Instance.EnemyHpFor(1f);
            int total = UltimateArt.MobDamageTotal(lv);
            var ogre = UltSpawn(9f, "heavy_ogre");
            var elite = UltSpawn(13f, "goblin");
            if (elite != null) ChallengeSystem.MakeElite(elite, 3f);
            var gob = UltSpawn(6f, "goblin");
            yield return null;
            UltimateArt.Record rec = null;
            yield return UltFire($"L{lv}", r => rec = r);
            if (rec == null) { yield return V3End(); continue; }
            float adv = rec.d1 - rec.d0, want = lv == 1 ? 100f : lv == 5 ? 150f : 200f, wantBuff = lv == 1 ? 5f : lv == 5 ? 8f : 12f;
            Check(Mathf.Abs(adv - want) < 15f, $"[L{lv}] advance about {want:F0}m ({adv:F1})");
            Check(Mathf.Abs(rec.buffSeconds - wantBuff) < 0.05f, $"[L{lv}] BUFF {wantBuff:F0}s ({rec.buffSeconds:F1})");
            float ratio = total / (float)hp1;
            Check(gob == null || gob.IsDying, $"[L{lv}] a normal goblin is defeated");
            bool ogreAlive = ogre != null && !ogre.IsDying, eliteAlive = elite != null && !elite.IsDying;
            if (lv == 1) Check(ogreAlive, $"[L{lv}] the heavy ogre (HP x3) can survive at Lv1");
            if (lv == 9) Check(!ogreAlive, $"[L{lv}] the heavy ogre falls at Lv9");
            L($"[L{lv}] mob total {total} = HP(x1) {hp1} x {ratio:F2}; ogre {(ogreAlive ? "survived" : "defeated")}, elite(x3) {(eliteAlive ? "survived" : "defeated")}");
            sb.AppendLine($"{lv}\t{adv:F1}\t{rec.buffSeconds:F1}\t{total}\t{hp1}\t{ratio:F2}\t{ogreAlive}\t{eliteAlive}");
            yield return V3End();
        }
        // カードの攻撃力: 攻撃特化 Lv9 でも上限(×1.8)まで
        yield return UltBegin("swordsman", "wasteland_road", 9);
        int plain = UltimateArt.MobDamageTotal(9);
        foreach (var id in CardBuildPresets.Find("attack")) V3Apply(id, 9);
        int built = UltimateArt.MobDamageTotal(9);
        L($"[L] build factor: attack x{pc.CardAttackFactor:F2} -> ULTIMATE x{UltimateArt.BuildFactor:F2} ({plain} -> {built})");
        Check(UltimateArt.BuildFactor <= UltimateTuning.I.buildFactorMax + 1e-3f && built <= Mathf.CeilToInt(plain * UltimateTuning.I.buildFactorMax) + 1, "an attack build raises the ULTIMATE only up to the cap (no x6 runaway)");
        yield return V3End();
        V3Write("ult_levels.tsv", sb);
    }

    // ===================================================================== G
    IEnumerator UltGauge()
    {
        L("== G: Gauge ==");
        var T = UltimateTuning.I;
        // 距離だけ(敵なし)・高速: 上限 %/秒 で頭打ち
        yield return UltBegin("swordsman", "wasteland_road", 1);
        UltWarp(3150f);
        V3FlatStretch();
        yield return new WaitForSeconds(0.5f);
        Ua.DebugSetGauge(0f);
        PlayerController.DebugRunOnlyScale = 4f;
        float g0 = Ua.Gauge, d0 = gm.MaxDistance, t0 = Time.time;
        yield return new WaitForSeconds(10f);
        float secs = Time.time - t0, meters = gm.MaxDistance - d0, gain = Ua.Gauge - g0;
        PlayerController.DebugRunOnlyScale = 1f;
        L($"[G] fast distance only: {meters:F0}m in {secs:F1}s ({GameManager.SpeedKmh(meters / secs):F0} km/h) -> +{gain:F2}% (per-meter would be {meters * T.gaugePerMeter:F1}%)");
        Check(gain > 0f && gain <= T.gaugeDistanceMaxPerSecond * secs * 1.05f + 0.6f, "distance gain is capped per second at high speed (no distance-only spam)");
        // 普通の速さ: 1m ごと
        Ua.DebugSetGauge(0f);
        d0 = gm.MaxDistance; t0 = Time.time;
        yield return new WaitForSeconds(4f);
        meters = gm.MaxDistance - d0; gain = Ua.Gauge;
        L($"[G] normal speed: {meters:F0}m in {Time.time - t0:F1}s -> +{gain:F2}%");
        Check(gain > 0f && gain <= meters * T.gaugePerMeter + 0.05f, "distance fills the gauge at the per-meter rate (or the per-second cap)");
        // 撃破
        Ua.DebugSetGauge(0f);
        GameManager.BlockExpGain = false;
        float before = Ua.Gauge;
        for (int i = 0; i < 5; i++) gm.RegisterEnemyKill(1, 1f);
        gm.RegisterEnemyKill(1, 2f); // 精鋭
        float killGain = Ua.Gauge - before;
        GameManager.BlockExpGain = true;
        L($"[G] 5 kills + 1 elite -> +{killGain:F2}%");
        Check(Mathf.Abs(killGain - (5f * T.gaugePerKill + T.gaugePerEliteKill)) < 0.2f, "kills fill the gauge (normal / elite)");
        // 発動中は溜まらない
        Ua.DebugSetGauge(100f);
        Check(Ua.TryActivate("qa gauge"), "activates");
        for (int i = 0; i < 5; i++) gm.RegisterEnemyKill(1, 1f);
        Check(Ua.Gauge == 0f, $"no gauge while the ULTIMATE is running ({Ua.Gauge:F2})");
        float w = 0f; while (Ua.Active && w < 8f) { yield return null; w += Time.deltaTime; }
        // BUFF 中は半分
        Ua.DebugSetGauge(0f);
        gm.RegisterEnemyKill(1, 1f);
        Check(Mathf.Abs(Ua.Gauge - T.gaugePerKill * T.gaugeDuringBuff) < 0.05f, $"gauge fills at x{T.gaugeDuringBuff} during the BUFF ({Ua.Gauge:F2})");
        Ua.DebugEndBuff();
        // Lv9 は溜まりやすい
        V3Reset(); V3Apply(UltimateArt.CardId, 9);
        Ua.DebugSetGauge(0f);
        gm.RegisterEnemyKill(1, 1f);
        Check(Mathf.Abs(Ua.Gauge - T.gaugePerKill * (1f + 8f * T.gaugeLevelBonus)) < 0.05f, $"Lv9 fills faster (+{8f * T.gaugeLevelBonus * 100f:F0}%: {Ua.Gauge:F2})");
        // カードなし: ボタンも出ない/溜まらない
        V3Reset();
        gm.RegisterEnemyKill(1, 1f);
        Check(!UltimateArt.HasCard && !Ua.ButtonVisible && !Ua.TryActivate("qa nocard"), "without the card there is no button and no activation");
        yield return V3End();
        // ボスへのダメージ
        yield return UltBegin("swordsman", "wasteland_road", 1);
        UltWarp(1955f);
        w = 0f; while ((Bm.AliveBossCount <= 0 || !BossBattle.AnyBossFighting) && w < 30f) { yield return null; w += Time.deltaTime; }
        var boss = WildAlive().FirstOrDefault();
        if (boss == null) Warn("[G] no boss appeared for the boss gauge check");
        else
        {
            Ua.DebugSetGauge(0f);
            int dmg = Mathf.Max(1, boss.maxHp / 20);
            boss.TakeDamage(dmg, boss.CenterWorld);
            float bg = Ua.Gauge;
            L($"[G] boss hit {dmg} of {boss.maxHp} -> +{bg:F2}%");
            Check(bg > 0f && bg <= T.gaugeBossHitMax + 0.01f, "boss damage fills the gauge (capped per hit)");
        }
        yield return V3End();
    }

    // ===================================================================== S
    IEnumerator UltStages()
    {
        L("== S: 3ステージ × 何度も発動(穴/段差/天井/分岐) ==");
        var sb = new StringBuilder("stage\tn\td0\tadvance\tpitsCrossed\tlimit\tseconds\n");
        foreach (var stage in new[] { "wasteland_road", "natural_cave", "sky_corridor" })
        {
            int reps = int.Parse(Arg("-qaUltReps", "6"));
            yield return UltBegin(stage == "sky_corridor" ? "mage" : stage == "natural_cave" ? "ninja" : "dragon_lancer", stage, 9);
            BossManager.SuppressGates = true; // 関門のボス戦を挟まずに何度も(関門の扱いは E/B で確かめる)
            UltWarp(1250f);
            yield return new WaitForSeconds(1.5f);
            int pitsTotal = 0;
            for (int i = 0; i < reps; i++)
            {
                var tm = TerrainManager.Instance;
                float x0 = pc.transform.position.x;
                tm.GenerateNow(x0 + 260f);
                int pits = 0;
                for (float s = 0f; s < 220f; s += 2f) { float? p = tm.FirstPitBetween(x0 + s, x0 + s + 2f); if (p.HasValue && p.Value >= x0 + s) pits++; }
                float dBefore = gm.MaxDistance;
                UltimateArt.Record rec = null;
                yield return UltFire($"S:{stage}#{i}", r => rec = r);
                if (rec != null)
                {
                    pitsTotal += pits;
                    sb.AppendLine($"{stage}\t{i}\t{rec.d0:F0}\t{rec.d1 - rec.d0:F1}\t{pits}\t{rec.limitReason}\t{rec.seconds:F2}");
                    Check(rec.d1 - rec.d0 > 150f, $"[S:{stage}#{i}] advanced ({rec.d1 - rec.d0:F0}m, pits ahead {pits})");
                }
                Ua.DebugEndBuff();
                yield return new WaitForSeconds(2.5f + i % 3);
            }
            L($"[S:{stage}] {reps} activations, pit chunks in the paths: {pitsTotal}, fall hits {CaveStage.SpikeHitCount} spikes");
            if (stage != "sky_corridor" && pitsTotal == 0) Warn($"[S:{stage}] no pit was crossed (terrain luck)");
            yield return V3End();
        }
        V3Write("ult_stages.tsv", sb);
    }

    // ===================================================================== E
    IEnumerator UltEdges()
    {
        L("== E: 端の場面 ==");
        // (1) 次のボス関門の手前: 関門を飛ばさず、手前で止まる。関門はその後ふつうに始まる
        yield return UltBegin("swordsman", "wasteland_road", 9);
        UltWarp(3880f);
        yield return new WaitForSeconds(0.8f);
        UltimateArt.Record rec = null;
        yield return UltFire("E:gate", r => rec = r);
        if (rec != null)
        {
            Check(rec.limitReason.Contains("boss gate") && rec.d1 < 4000f, $"[E:gate] stops before the 4000m boss gate ({rec.d1:F0}m, {rec.limitReason})");
            Check(!rec.aborted, "[E:gate] was not cut short by the boss phase");
        }
        float w = 0f; while (!Bm.IsBossPhase && w < 20f) { yield return null; w += Time.deltaTime; }
        Check(Bm.IsBossPhase && Mathf.Abs(gm.MaxDistance - 4000f) < 1f, $"[E:gate] the 4000m boss gate starts normally afterwards (d={gm.MaxDistance:F0})");
        yield return V3End();

        // (2) 1000m の境目(関門なし): 距離は一度だけ数える(累計距離 = 進んだ距離)
        yield return UltBegin("archer", "wasteland_road", 9);
        BossManager.SuppressGates = true;
        UltWarp(2880f);
        yield return new WaitForSeconds(0.8f);
        double run0 = ProgressStats.LifetimeDistance; float d0 = gm.MaxDistance;
        yield return UltFire("E:1000m", r => rec = r);
        float runDelta = (float)(ProgressStats.LifetimeDistance - run0), dDelta = gm.MaxDistance - d0;
        Check(rec != null && rec.d0 < 3000f && rec.d1 > 3000f, $"[E:1000m] crosses 3000m continuously ({rec?.d0:F0} -> {rec?.d1:F0})");
        Check(Mathf.Abs(runDelta - dDelta) < 1.5f, $"[E:1000m] distance counted once (run total +{runDelta:F1} vs distance +{dDelta:F1})");
        yield return V3End();

        // (3) BONUS ZONE: 区画の間は使えない / ボスの後の抽選の手前で止まる
        yield return UltBegin("miko", "wasteland_road", 9);
        BossManager.SuppressGates = true;
        UltWarp(5150f);
        yield return new WaitForSeconds(0.8f);
        var bz = BonusZone.Instance;
        if (bz == null) Warn("[E:bonus] no BonusZone");
        else
        {
            typeof(BonusZone).GetField("pendingAtDistance", NP).SetValue(bz, gm.MaxDistance + 90f);
            float pend = bz.PendingStartDistance;
            yield return UltFire("E:bonusPending", r => rec = r);
            Check(rec != null && rec.d1 < pend && rec.limitReason.Contains("BONUS"), $"[E:bonus] stops before the pending BONUS ZONE start {pend:F0} ({rec?.d1:F0}, {rec?.limitReason})");
            Ua.DebugEndBuff();
            w = 0f; while (bz.State == BonusZone.Phase.Idle && w < 6f) { yield return null; w += Time.deltaTime; }
            if (bz.State == BonusZone.Phase.Idle) bz.Force();
            yield return new WaitForSeconds(0.3f);
            Ua.DebugSetGauge(100f);
            bool act = Ua.TryActivate("qa during bonus");
            Check(!act && Ua.LastBlockReason == "BONUS中", $"[E:bonus] cannot activate during the BONUS ZONE ({Ua.LastBlockReason}, state {bz.State})");
            bz.End();
        }
        yield return V3End();

        // (4) 100km の手前(三姉妹の地点を飛ばさない)
        yield return UltBegin("vampire", "wasteland_road", 9);
        BossManager.SuppressGates = true;
        UltWarp(99860f);
        yield return new WaitForSeconds(0.8f);
        yield return UltFire("E:100km", r => rec = r);
        Check(rec != null && rec.d1 < 100000f && rec.limitReason.Contains("100km"), $"[E:100km] stops before 100,000m ({rec?.d1:F0}, {rec?.limitReason})");
        yield return V3End();

        // (5) 停止中/選択中は使えない
        yield return UltBegin("fighter", "wasteland_road", 5);
        UltWarp(3150f);
        yield return new WaitForSeconds(0.5f);
        var owner = new object();
        TimeControl.Pause(owner);
        yield return null;
        Ua.DebugSetGauge(100f);
        Check(!Ua.TryActivate("qa paused"), $"cannot activate while paused ({Ua.LastBlockReason})");
        TimeControl.Resume(owner);
        yield return null;
        // (6) 穴の直前で発動しても落ちない
        var tm = TerrainManager.Instance;
        float? pit = null;
        for (int tries = 0; tries < 40 && !pit.HasValue; tries++)
        {
            tm.GenerateNow(pc.transform.position.x + 80f);
            pit = tm.FirstPitBetween(pc.transform.position.x + 4f, pc.transform.position.x + 60f);
            if (!pit.HasValue) yield return new WaitForSeconds(0.5f);
        }
        if (!pit.HasValue) Warn("[E:pit] no pit found ahead");
        else
        {
            w = 0f; while (pc.transform.position.x < pit.Value - 2.5f && w < 8f) { yield return null; w += Time.deltaTime; }
            yield return UltFire("E:pitEdge", r => rec = r);
            L($"[E:pitEdge] activated {pit.Value - (rec != null ? rec.x0 : 0f):F1}m before a pit");
        }
        yield return V3End();
    }

    // ===================================================================== B
    IEnumerator UltBoss()
    {
        L("== B: ボス戦(アリーナ) ==");
        var sb = new StringBuilder("stage\tchar\tlv\tboss\tmaxHp\tdealt\tfraction\talive\tbossPhase\tdistanceMoved\n");
        foreach (var (stage, ch, lv) in new[] { ("wasteland_road", "swordsman", 9), ("wasteland_road", "gunslinger", 1), ("natural_cave", "noble_lady", 9), ("sky_corridor", "dragonkin", 9) })
        {
            yield return UltBegin(ch, stage, lv);
            UltWarp(1500f);
            yield return new WaitForSeconds(0.3f);
            float gateAt = Bm.NextBossDistance;
            UltWarp(gateAt - 45f);
            float w = 0f;
            while ((Bm.AliveBossCount <= 0 || !BossBattle.AnyBossFighting) && w < 35f) { yield return null; w += Time.deltaTime; }
            yield return new WaitForSeconds(0.6f);
            var boss = WildAlive().OrderBy(b => Mathf.Abs(b.transform.position.x - pc.transform.position.x)).FirstOrDefault();
            if (boss == null)
            {
                // 天空回廊の関門のドラゴン(旧 DragonController): 前進なし・その場で撃つ + ボスの上限
                var dr = DragonsAlive().FirstOrDefault();
                if (dr == null) { Warn($"[B:{stage}] no boss to test"); yield return V3End(); continue; }
                int dh0 = dr.Hp; float dd0 = gm.MaxDistance;
                UltimateArt.Record drec = null;
                yield return UltFire($"B:{stage}:{ch}:dragon", r => drec = r);
                float dfrac = (dh0 - dr.Hp) / (float)dr.maxHp;
                Check(drec != null && !drec.arena && drec.d1 - drec.d0 < 1f, $"[B:{stage}] legacy dragon: in place (no advance during the boss fight)");
                Check(dfrac > 0.04f && dfrac <= UltimateTuning.I.bossDamageCap + 0.03f && !dr.IsDead, $"[B:{stage}] legacy dragon: capped damage {dfrac * 100f:F1}%, alive {!dr.IsDead}");
                Check(Bm.IsBossPhase, $"[B:{stage}] the fight continues");
                sb.AppendLine($"{stage}\t{ch}\t{lv}\tDragon(legacy)\t{dr.maxHp}\t{dh0 - dr.Hp}\t{dfrac:F3}\t{!dr.IsDead}\t{Bm.IsBossPhase}\t{gm.MaxDistance - dd0:F1}");
                yield return V3End(); continue;
            }
            int hp0 = boss.Hp;
            float d0 = gm.MaxDistance;
            UltimateArt.Record rec = null;
            yield return UltFire($"B:{stage}:{ch}", r => rec = r);
            if (rec == null) { yield return V3End(); continue; }
            float frac = (hp0 - boss.Hp) / (float)boss.maxHp;
            Check(rec.arena, $"[B:{stage}] uses the arena movement during the boss fight");
            Check(frac > 0.04f && frac <= UltimateTuning.I.bossDamageCap + 0.03f, $"[B:{stage}] big boss damage but capped ({frac * 100f:F1}% of max HP, cap {UltimateTuning.I.bossDamageCap * 100f:F0}%)");
            Check(!boss.IsDead && Bm.IsBossPhase && Bm.AliveBossCount > 0, $"[B:{stage}] the fight continues (boss HP {boss.Hp}/{boss.maxHp})");
            if (Bm.HoldsRun) Check(Mathf.Abs(gm.MaxDistance - d0) < 1f, $"[B:{stage}] distance does not advance while the boss holds the run ({d0:F0} -> {gm.MaxDistance:F0})");
            float gap = boss.transform.position.x - pc.transform.position.x;
            Check(gap > 0f, $"[B:{stage}] the player returned to the front side of the boss (gap {gap:F1}m)");
            // 続けて殴れる(戦闘が止まっていない)
            int hpA = boss.Hp;
            for (int i = 0; i < 25 && !boss.IsDead; i++) { StartCoroutine(Flick(PlayerController.FlickDirection.Forward)); yield return new WaitForSeconds(0.2f); }
            Check(boss.IsDead || boss.Hp < hpA || !BossBattle.AnyBossFighting, $"[B:{stage}] normal attacks keep working after the ULTIMATE (HP {hpA} -> {boss.Hp})");
            sb.AppendLine($"{stage}\t{ch}\t{lv}\t{boss.bossName}\t{boss.maxHp}\t{hp0 - boss.Hp}\t{frac:F3}\t{!boss.IsDead}\t{Bm.IsBossPhase}\t{gm.MaxDistance - d0:F1}");
            yield return V3End();
        }
        V3Write("ult_boss.tsv", sb);
    }

    // ===================================================================== R
    IEnumerator UltContinue()
    {
        L("== R: CONTINUE ==");
        yield return V3Begin("dual_blade", "wasteland_road");
        V3Reset();
        stopKeepAlive = true;
        UltWarp(3150f);
        yield return new WaitForSeconds(1f);
        foreach (var id in new[] { UltimateArt.CardId, UltimateArt.CardId, UltimateArt.CardId, "attack_up" }) V3Pick.Invoke(gm, new object[] { id });
        Ua.DebugSetGauge(63f);
        int lv = UltimateArt.Level;
        typeof(GameManager).GetMethod("SaveCheckpoint", NP).Invoke(gm, null);
        var data = RunCheckpoint.Load();
        Check(data != null && Mathf.Abs(data.ultimateGauge - 63f) < 0.5f, $"the checkpoint stores the gauge ({data?.ultimateGauge:F1})");
        var old = gm;
        gm.ReturnToHome();
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f);
        gm = GameManager.Instance;
        w = 0f; float retry = 0f;
        while (gm.ResumeGate != GameManager.ResumeGatePhase.Waiting && w < 12f)
        {
            if (!gm.HasStarted && retry <= 0f) { gm.ContinueActiveRun(); retry = 0.5f; }
            yield return null; w += Time.unscaledDeltaTime; retry -= Time.unscaledDeltaTime;
        }
        pc = PlayerController.Instance;
        L($"[R] after CONTINUE: Lv{UltimateArt.Level} (was {lv}) gauge {Ua.Gauge:F1}");
        Check(UltimateArt.Level == lv && lv >= 3 && Mathf.Abs(Ua.Gauge - 63f) < 0.5f, "CONTINUE restores the ULTIMATE level and gauge");
        gm.RequestResumeFromGate();
        yield return new WaitForSecondsRealtime(4f);
        Ua.DebugSetGauge(100f);
        yield return UltFire("R:afterContinue");
        stopKeepAlive = false;
        yield return V3End();
    }

    // ===================================================================== T
    IEnumerator UltDebugMenu()
    {
        L("== T: ULTIMATE TEST(DEBUG RUN) ==");
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        float best0 = gm.BestDistance;
        string sel0 = gm.SelectedCharacterId;
        bool hadRun0 = RunCheckpoint.HasActiveRun;
        string chosen = sel0 == "ninja" ? "vampire" : "ninja";
        EndgameDebug.LaunchUltimate(chosen, 9, "natural_cave", false);
        yield return new WaitForSecondsRealtime(1f);
        w = 0f;
        while ((EndgameDebug.Instance.Launching || GameManager.Instance == null || !GameManager.Instance.HasStarted) && w < 40f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance; pc = PlayerController.Instance;
        yield return new WaitForSeconds(0.5f);
        L($"[T] launched: debugRun={DebugRun.IsActive} '{DebugRun.What}' char={gm.ActiveRunCharacterId} Lv{UltimateArt.Level} gauge={Ua.Gauge:F0} d={gm.MaxDistance:F0}");
        Check(DebugRun.IsActive && gm.ActiveRunCharacterId == chosen && UltimateArt.Level == 9 && Ua.Gauge >= 100f, "ULTIMATE TEST starts a DEBUG RUN with the chosen character, Lv9 and a full gauge");
        var order = (List<string>)typeof(GameManager).GetField("cardOrder", NP).GetValue(gm);
        Check(gm.CardLevel(UltimateArt.CardId) == 9 && order.Count == 1, $"only the ULTIMATE card is applied ({string.Join(",", order)})");
        yield return UltFire("T:menu");
        var old = gm;
        gm.Retry();
        w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(2.5f);
        gm = GameManager.Instance;
        RunCheckpoint.Reload();
        bool hadRun1 = RunCheckpoint.HasActiveRun;
        L($"[T] back home: debugRun={DebugRun.IsActive} restoredKeys={DebugRun.LastRestoredKeys} ({DebugRun.LastRestoreNote}) best {best0:F0} -> {gm.BestDistance:F0} selected {sel0} -> {gm.SelectedCharacterId} activeRun {hadRun0} -> {hadRun1}");
        Check(!DebugRun.IsActive && Mathf.Approximately(best0, gm.BestDistance) && gm.SelectedCharacterId == sel0 && hadRun0 == hadRun1, "ULTIMATE TEST leaves BEST / selected character / active run unchanged");
    }
}
#endif
