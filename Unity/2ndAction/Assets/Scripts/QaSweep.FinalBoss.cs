#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 通常マップの専用ラスボス(2026-11-06、Bosses/FinalBosses.cs)の確認。
//   -qaFinalBoss <dir> [-fbStages wasteland_road,natural_cave,sky_corridor] [-fbChar swordsman] [-fbBuildLv 5]
//                      [-fbCap 240(ラスボスと戦う上限の秒)] [-fbTimeScale 2] [-fbKmh 0(0 = 自然の速さ)] [-fbForce 1(出なかった BREAK/必殺技を試験が起こす)]
//  流れ: 94,850m へ跳ぶ → 95km の組のラッシュ(組のボスは試験がすぐ倒す)→ 98km のラスボス(自動操作補助で本当に戦う。被弾はするが HP は減らさない)
//        → 撃破 → 報酬 → 99〜100km → 死神。各ステージで: 出現 / 使った技 / BREAK と立ち直り / 必殺技 / 撃破 / 戦いの秒 / 被弾 / 画面に対する大きさ / 例外
public partial class QaSweep
{
    static bool IsFinalBossObj(WildBossBase b) => b is GarmudBoss || b is NoctarisBoss || b is SeraphielBoss;

    IEnumerator FinalBossMode()
    {
        HookBossLogs();
        foreach (var st in Arg("-fbStages", "wasteland_road,natural_cave,sky_corridor").Split(',')) yield return FbFlow(st);
        L("");
        foreach (var kv in FinalBossStats.Moves.OrderBy(k => k.Key)) L($"[moves] {kv.Key} x{kv.Value}");
    }

    void FbKillSetBosses()
    {
        foreach (var b in WildAlive()) if (!IsFinalBossObj(b) && !(b is ReaperSisterBoss)) b.TakeDamage(Mathf.Max(99999, b.Hp), b.CenterWorld);
        foreach (var d in DragonsAlive()) d.TakeDamage(Mathf.Max(99999, d.Hp));
        foreach (var m in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) if (m != null && !m.IsDead) m.TakeDamage(Mathf.Max(99999, m.Hp));
    }

    IEnumerator FbFlow(string st)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string ch = Arg("-fbChar", "swordsman");
        float cap = float.Parse(Arg("-fbCap", "240"), inv), ts = float.Parse(Arg("-fbTimeScale", "2"), inv), kmh = float.Parse(Arg("-fbKmh", "0"), inv);
        bool force = Arg("-fbForce", "1") == "1";
        string want = BossManager.FinalBossKeyFor(st);
        L($"== final boss: {st} -> {want} ({ch}, mix Lv{Arg("-fbBuildLv", "5")}, cap {cap:F0}s, time x{ts}) ==");
        yield return BeginRun(ch, st);
        SetGod(false);
        if (HighSpeedAssist.Instance != null) { HighSpeedAssist.Instance.SetEnabled(true); HighSpeedAssist.Instance.SetAutoAttack(true); HighSpeedAssist.Instance.SetAutoInBoss(true); }
        int lv = int.Parse(Arg("-fbBuildLv", "5"));
        foreach (var id in CardBuildPresets.Find("mix")) { var cd = CardDatabase.FindBaseById(id); if (cd != null) gm.ApplyCardEffectsStacked(cd, lv); }
        Application.targetFrameRate = Mathf.RoundToInt(60f * Mathf.Max(1f, ts)); QualitySettings.vSyncCount = 0;
        TimeControl.SetDebugTimeScale(ts);
        WarpTo(94850f);
        typeof(GameManager).GetMethod("ApplySprintArrivalLevel", NP).Invoke(gm, new object[] { 94850f });
        var moves0 = new Dictionary<string, int>(FinalBossStats.Moves);
        int exc0 = excCount;

        // ---- 95〜98km: 組のボスは試験がすぐ倒す。ラスボスが出るまで ----
        WildBossBase fb = null;
        float w = 0f, killAt = 0f;
        int setKills = 0;
        while (w < 600f)
        {
            if (kmh > 0f) SetKmh(kmh);
            fb = WildAlive().FirstOrDefault(IsFinalBossObj);
            if (fb != null) break;
            if (Time.time >= killAt && Bm.IsBossPhase) { killAt = Time.time + 0.4f; int n0 = gm.BossKillCount; FbKillSetBosses(); setKills += gm.BossKillCount - n0; }
            if (gm.IsGameOver) break;
            yield return null; w += Time.unscaledDeltaTime;
        }
        Check(fb != null, $"{st}: the final boss spawned (waited {w:F0}s real, d={gm.MaxDistance:F0}m, set rush {Bm.SetRushActive})");
        if (fb == null) { TimeControl.SetDebugTimeScale(1f); yield return EndRun(); yield break; }
        string key = fb.DeathKey;
        Check(key == want, $"{st}: the final boss is {want} ({key})");
        L($"[spawn] {st} {key} at {gm.MaxDistance:F0}m hp={fb.Hp}/{fb.maxHp} phase={fb.Phase}/{fb.PhaseCount} bodyHeight={fb.bodyHeight:F1} halfWidth={fb.HalfWidth:F1} rematchTier={fb.DebugRT} set kills before={setKills}");
        w = 0f; while (fb != null && fb.IsEntering && w < 10f) { yield return null; w += Time.deltaTime; }
        var cam = Camera.main;
        if (cam != null && fb != null)
        {
            Bounds hb = fb.AssistBounds;
            float screenH = cam.orthographicSize * 2f;
            L($"[size] {key}: visible body {fb.bodyHeight:F1}m = {fb.bodyHeight / screenH * 100f:F0}% of the screen height ({screenH:F1}m, aspect {cam.aspect:F2}); hurtbox {hb.size.x:F1}x{hb.size.y:F1}m bottom {hb.min.y - pc.transform.position.y:+0.0;-0.0}m from the player's feet; gap {fb.DebugGap:F1}m");
        }
        Shot($"fb_{st}_enter");

        // ---- ラスボスと戦う(自動操作補助)----
        float t0 = Time.time;
        int breaks0 = fb.BreakCount, hits = 0, overlap = 0, frames = 0, ults0 = fb.UltimatesUsed;
        float lastHit = pc.LastDamageTime;
        var hitSrc = new Dictionary<string, int>();
        bool forcedUlt = false, forcedBreak = false, sawBroken = false, sawRecover = false, shotBreak = false, shotUlt = false;
        float minGapSeen = 99f;
        while (fb != null && !fb.IsDead && Time.time - t0 < cap && !gm.IsGameOver)
        {
            float t = Time.time - t0;
            if (kmh > 0f) SetKmh(kmh);
            frames++;
            if (pc.LastDamageTime != lastHit) { lastHit = pc.LastDamageTime; hits++; string s = pc.LastDamageSource ?? "?"; hitSrc[s] = hitSrc.TryGetValue(s, out int c) ? c + 1 : 1; }
            Bounds hb = fb.AssistBounds;
            if (hb.Contains(new Vector3(pc.transform.position.x, hb.center.y, hb.center.z))) overlap++;
            minGapSeen = Mathf.Min(minGapSeen, fb.DebugGap);
            if (fb.Broken) { sawBroken = true; if (!shotBreak) { shotBreak = true; Shot($"fb_{st}_break"); } }
            if (fb.BreakRecovering) sawRecover = true;
            if (fb.UltimateRunning && !shotUlt && fb.DebugAct != "Exhausted") { shotUlt = true; Shot($"fb_{st}_ult"); }
            if (force && !forcedUlt && t > cap * 0.3f && fb.UltimatesUsed == ults0 && !fb.UltimateRunning && !fb.Broken) { forcedUlt = true; fb.DebugForceUltimate(); L($"[force] {key} ultimate at t={t:F0}s (none so far)"); }
            if (force && !forcedBreak && t > cap * 0.45f && fb.BreakCount == breaks0 && !fb.UltimateRunning && !fb.Broken) { forcedBreak = true; fb.DebugForceBreak(); L($"[force] {key} BREAK at t={t:F0}s (none so far)"); }
            yield return null;
        }
        float fight = Time.time - t0;
        bool killedByTest = false;
        if (fb != null && !fb.IsDead) { killedByTest = true; L($"[cap] {key} not down after {fight:F0}s (hp {fb.Hp}/{fb.maxHp}, phase {fb.Phase}) - the test finishes it"); fb.TakeDamage(Mathf.Max(99999, fb.Hp), fb.CenterWorld); }
        var used = FinalBossStats.Moves.Where(kv => kv.Key.StartsWith(fb.bossName + "/")).Select(kv => (kv.Key.Substring(kv.Key.IndexOf('/') + 1), kv.Value - (moves0.TryGetValue(kv.Key, out int m0) ? m0 : 0))).Where(x => x.Item2 > 0).ToList();
        L($"[fight] {st} {key}: {(killedByTest ? "cap" : $"down in {fight:F0}s (game time)")}, phase reached {fb.Phase}, BREAK {fb.BreakCount - breaks0} (broken seen {sawBroken}, recovery seen {sawRecover}), ultimates {fb.UltimatesUsed - ults0}, specials {fb.SpecialsUsed}");
        L($"[fight] {st} moves: {string.Join(", ", used.Select(x => $"{x.Item1} x{x.Item2}"))}");
        L($"[fight] {st} player hits {hits}: {string.Join(", ", hitSrc.Select(kv => $"{kv.Key} x{kv.Value}"))}; body overlapped the player {overlap * 100f / Mathf.Max(1, frames):F1}% of frames; min gap {minGapSeen:F1}m");
        string[] expect = fb is GarmudBoss ? new[] { "HornUpswing", "Charge", "RubbleThrow", "EarthSplitter", "Ultimate" }
                        : fb is NoctarisBoss ? new[] { "CrystalSpines", "IcicleRain", "TailSweep", "Bite", "AbyssJaw", "Ultimate" }
                        : new[] { "FeatherVolley", "LightSpears", "WingSweepHigh", "WingSweepLow", "JudgmentBeam", "Ultimate" };
        var missing = expect.Where(e => !used.Any(u => u.Item1 == e)).ToList();
        if (missing.Count > 0) Warn($"{st}: moves not seen in this fight: {string.Join(", ", missing)}");
        Check(sawBroken, $"{st}: BREAK happened");
        Check(!sawBroken || sawRecover || fb.IsDead, $"{st}: BREAK recovery ran");

        // ---- 撃破 → 報酬 → 99〜100km → 死神 ----
        w = 0f; while (Bm.IsBossPhase && w < 60f) { if (Time.time >= killAt) { killAt = Time.time + 0.4f; FbKillSetBosses(); } yield return null; w += Time.unscaledDeltaTime; }
        Check(!Bm.IsBossPhase, $"{st}: the encounter completed after the final boss ({gm.MaxDistance:F0}m)");
        float d0 = gm.MaxDistance;
        w = 0f; while (!Bm.DeathSpawned && w < 240f && !gm.IsGameOver) { yield return null; w += Time.unscaledDeltaTime; }
        Check(Bm.DeathSpawned, $"{st}: the run continued from {d0:F0}m to 100km and the reaper came ({gm.MaxDistance:F0}m)");
        Check(excCount == exc0, $"{st}: no exceptions during the final boss flow ({excCount - exc0})");
        L($"[end] {st}: {d0:F0}m -> {gm.MaxDistance:F0}m, reaper {Bm.DeathSpawned}, Lv {gm.Level}, exceptions {excCount - exc0}");
        TimeControl.SetDebugTimeScale(1f);
        Application.targetFrameRate = 60;
        yield return EndRun();
    }
}
#endif
