#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 黒剣士とタイタン(天空)の攻撃の届き方の切り分け(2026-10-15 第3弾)。 -qaRewards <dir> -rwPhase titan [-rwChar swordsman] [-rwTitanSec 60]
//  ボスの HP は大きく固定し(倒れないように)、与えたダメージ/当たった回数/被弾範囲とプレイヤーの位置の関係を記録する。
//   A auto   … カード無し + 補助(自動攻撃)だけ
//   B jump   … カード無し + 補助なし、手動のジャンプ攻撃(上 → 空中で前)を繰り返す
//   C up     … カード無し + 補助なし、手動の上攻撃だけを繰り返す
//   D build  … 普通の育成済み(mix の Lv5)+ 補助(自動攻撃)
//   E rush   … ラスダンのボスラッシュの強さ(LastDungeon の倍率)で A と同じ
public partial class QaSweep
{
    IEnumerator RwTitan()
    {
        string ch = RwChar;
        float secs = float.Parse(Arg("-rwTitanSec", "60"));
        var all = new (string tag, bool assist, string manual, string preset)[] { ("A auto", true, null, null), ("B jump", false, "jump", null), ("C up", false, "up", null), ("D build", true, null, "mix"), ("F far", true, null, null), ("G ld", true, null, null) };
        string only = Arg("-rwTitanCases", "ABCDE");
        foreach (var c in all)
        {
            if (only.IndexOf(c.tag[0]) < 0) continue;
            BossManager.NetTestBossHpOverride = 10000000;
            yield return BeginRun(ch, c.tag[0] == 'G' ? "last_corridor" : "sky_corridor"); // G: ラスダン(ラッシュの外の 31km)
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            GameManager.BlockExpGain = true;
            if (c.preset != null) foreach (var id in CardBuildPresets.Find(c.preset)) { var cd = CardDatabase.FindBaseById(id); if (cd != null) gm.ApplyCardEffectsStacked(cd, 5); }
            if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(c.assist);
            WarpTo(c.tag[0] == 'F' ? 85300f : c.tag[0] == 'G' ? 31300f : 3300f); // F: 天空の 85km(距離/浮動原点の影響の切り分け)
            yield return new WaitForSecondsRealtime(1f);
            Bm.DebugSpawnBossForTest(2, (int)SkyBossKind.Titan);
            yield return WaitBossSpawn(30f);
            var titan = FindObjectsByType<SkyTitanBoss>(FindObjectsSortMode.None).FirstOrDefault(b => b != null && !b.IsDead);
            if (titan == null) { Check(false, $"{c.tag}: the Titan appears"); yield return EndRun(); continue; }
            float wEnter = 0f; while (titan.IsEntering && wEnter < 10f) { yield return null; wEnter += Time.unscaledDeltaTime; }
            int hp0 = titan.Hp, lastHp = titan.Hp, hits = 0, kneels0 = titan.KneelCount, reachableFrames = 0, frames = 0, kneelFrames = 0, kneelReachFrames = 0;
            float t0 = Time.time, nextManual = 0f, dxMin = 99f, dyMin = 99f, dyMax = -99f, firstHit = -1f;
            int phase = 0;
            var hc = titan.DebugHurtCol;
            var pcol = pc.GetComponent<Collider2D>();
            while (Time.time - t0 < secs)
            {
                if (gm.Lives < gm.maxLives) typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { gm.maxLives });
                if (titan.Hp < lastHp) { hits++; if (firstHit < 0f) firstHit = Time.time - t0; }
                lastHp = titan.Hp;
                frames++;
                if (hc != null && hc.enabled && pcol != null)
                {
                    var hb = hc.bounds; var pb = pcol.bounds;
                    float dx = hb.min.x - pb.max.x;     // 被弾範囲の手前の端まで(前方、m)
                    float dyB = hb.min.y - pb.max.y;    // 被弾範囲の下の端 − 頭の高さ(正 = 頭より上)
                    dxMin = Mathf.Min(dxMin, dx); dyMin = Mathf.Min(dyMin, dyB); dyMax = Mathf.Max(dyMax, dyB);
                    if (dx < 2.5f && dyB < 1.0f) reachableFrames++; // 地上の前攻撃が届きそうな位置(前 2.5m 以内、下の端が頭 +1m より下)
                    if (titan.IsKneeling) { kneelFrames++; if (dx < 2.5f && dyB < 1.0f) kneelReachFrames++; }
                }
                if (c.manual != null && Time.time >= nextManual)
                {
                    if (c.manual == "up") { StartCoroutine(Flick(PlayerController.FlickDirection.Up)); nextManual = Time.time + 0.45f; }
                    else if (phase == 0) { StartCoroutine(Flick(PlayerController.FlickDirection.Up)); phase = 1; nextManual = Time.time + 0.22f; }
                    else { StartCoroutine(Flick(PlayerController.FlickDirection.Forward)); phase = 0; nextManual = Time.time + 0.6f; }
                }
                yield return null;
            }
            int dealt = hp0 - titan.Hp;
            L($"[titan] {ch} {c.tag}: dealt {dealt} in {secs:F0}s ({dealt / secs:F0}/s), hits {hits}, first hit {firstHit:F1}s, kneels {titan.KneelCount - kneels0} (kneel frames {kneelFrames}, reachable while kneeling {kneelReachFrames}), reachable frames {reachableFrames}/{frames}, front gap min {dxMin:F1}m, hurtbox bottom above head {dyMin:F1}..{dyMax:F1}m");
            Check(true, "logged");
            BossManager.NetTestBossHpOverride = 0;
            GameManager.BlockExpGain = false;
            if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);
            yield return EndRun();
        }
        if (only.IndexOf('E') >= 0) yield return RwTitanInRush(ch, secs);
    }

    // E: ラスダンのボスラッシュの中のタイタン(走りながら / 99km の足止め)。補助の自動攻撃だけ、カード無し
    IEnumerator RwTitanInRush(string ch, float secs)
    {
        BossManager.NetTestBossHpOverride = 10000000;
        yield return BeginRun(ch, "last_corridor");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        GameManager.BlockExpGain = true;
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);
        WarpTo(89900f);
        float w = 0f;
        while (!(Bm.IsBossPhase && Bm.CurrentBossEncounter != null && Bm.CurrentBossEncounter.continuous) && w < 30f) { yield return null; w += Time.unscaledDeltaTime; }
        var enc = Bm.CurrentBossEncounter;
        if (enc == null) { Check(false, "E: the rush starts"); yield return EndRun(); yield break; }
        Bm.DebugSetRushPlan(new List<string> { enc.plan[0], "Sky/Titan", "Sky/Titan", "Sky/Titan", "Sky/Titan", "Sky/Titan", "Sky/Titan", "Sky/Titan", "Sky/Titan" });
        // 1体目(予定の最初のボス)を片付けて、2体目(タイタン)を待つ
        w = 0f; while (Bm.AliveBossCount < 1 && w < 20f) { yield return null; w += Time.unscaledDeltaTime; }
        foreach (var b in WildAlive()) b.TakeDamage(99999999, b.CenterWorld);
        SkyTitanBoss titan = null; w = 0f;
        while (titan == null && w < 90f) { titan = FindObjectsByType<SkyTitanBoss>(FindObjectsSortMode.None).FirstOrDefault(b => b != null && !b.IsDead && !b.IsEntering); yield return null; w += Time.unscaledDeltaTime; }
        if (titan == null) { Check(false, "E: the Titan appears in the rush"); yield return EndRun(); yield break; }
        yield return RwTitanMeasure(ch, "E1 rush running", titan, secs);
        // 99km の足止めまで速く進める(タイタンは生かしたまま)
        PlayerController.DebugSpeedScale = 3f;
        w = 0f; while (!(enc.held99 && Bm.HoldsRun) && w < 240f) { yield return null; w += Time.unscaledDeltaTime; }
        PlayerController.DebugSpeedScale = 1f;
        L($"[titan] E: hold reached={enc.held99} at {gm.MaxDistance:F0}m, alive {Bm.AliveBossCount}, queue {enc.Queued}");
        titan = FindObjectsByType<SkyTitanBoss>(FindObjectsSortMode.None).FirstOrDefault(b => b != null && !b.IsDead);
        if (titan != null) yield return RwTitanMeasure(ch, "E2 rush 99km hold", titan, secs);
        BossManager.NetTestBossHpOverride = 0;
        GameManager.BlockExpGain = false;
        yield return EndRun();
    }

    IEnumerator RwTitanMeasure(string ch, string tag, SkyTitanBoss titan, float secs)
    {
        int hp0 = titan.Hp, lastHp = titan.Hp, hits = 0, kneels0 = titan.KneelCount, reach = 0, frames = 0, kneelFrames = 0, kneelReach = 0, attacksTried = 0;
        float t0 = Time.time, dxMin = 99f, dxMax = -99f, dyMin = 99f, dyMax = -99f, firstHit = -1f;
        var hc = titan.DebugHurtCol; var pcol = pc.GetComponent<Collider2D>();
        bool wasAttacking = false;
        var fInv = typeof(WildBossBase).GetField("invulnerable", RwNP); var fApp = typeof(SkyBossBase).GetField("approachDone", RwNP);
        int targetable = 0, invul = 0, appDone = 0; var acts = new Dictionary<string, int>();
        while (Time.time - t0 < secs && titan != null && !titan.IsDead)
        {
            if (titan.AssistTargetable) targetable++;
            string act = titan.DebugAct ?? "?"; acts[act] = acts.TryGetValue(act, out int an) ? an + 1 : 1;
            if (fInv != null && (bool)fInv.GetValue(titan)) invul++;
            if (fApp != null && (bool)fApp.GetValue(titan)) appDone++;
            if (titan.Hp < lastHp) { hits++; if (firstHit < 0f) firstHit = Time.time - t0; }
            lastHp = titan.Hp; frames++;
            bool att = pc.IsAttacking; if (att && !wasAttacking) attacksTried++; wasAttacking = att;
            if (hc != null && hc.enabled && pcol != null)
            {
                var hb = hc.bounds; var pb = pcol.bounds;
                float dx = hb.min.x - pb.max.x, dyB = hb.min.y - pb.max.y;
                dxMin = Mathf.Min(dxMin, dx); dxMax = Mathf.Max(dxMax, dx); dyMin = Mathf.Min(dyMin, dyB); dyMax = Mathf.Max(dyMax, dyB);
                if (dx < 2.5f && dyB < 1.0f) reach++;
                if (titan.IsKneeling) { kneelFrames++; if (dx < 2.5f && dyB < 1.0f) kneelReach++; }
            }
            yield return null;
        }
        L($"[titan] {ch} {tag}: dealt {hp0 - (titan != null ? titan.Hp : 0)} in {secs:F0}s, hits {hits}, first hit {firstHit:F1}s, attacks started {attacksTried}, kneels {(titan != null ? titan.KneelCount - kneels0 : -1)} (kneel frames {kneelFrames}, reachable while kneeling {kneelReach}), reachable frames {reach}/{frames}, front gap {dxMin:F1}..{dxMax:F1}m, hurtbox bottom above head {dyMin:F1}..{dyMax:F1}m | targetable {targetable}, invulnerable {invul}, approach done {appDone} of {frames} frames, acts [{string.Join(", ", acts.OrderByDescending(x => x.Value).Select(x => x.Key + " " + x.Value))}], alpha {(titan != null ? titan.GetComponentInChildren<SpriteRenderer>()?.color.a ?? -1f : -1f):F2}");
    }
}
#endif
