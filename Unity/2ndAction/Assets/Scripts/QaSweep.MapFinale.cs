#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 通常マップの終盤(2026-10-24、BossManager.MapFinale.cs)と FINAL EVOLUTION の 60秒の確認。
//   -qaMapFinale <dir> [-mfStages wasteland_road,natural_cave,sky_corridor] [-mfChar swordsman] [-mfBuildLv 5]
//                      [-mfFight real|kill] [-mfFightCap 150] [-mfParts FLOW,FE,LD]
//  FLOW … 89,850m から: A 90km のボス(本来のボス)→ 撃破 → 雑魚の大群 / B 90〜95km の密度・攻撃の上限・EXP/レベルアップ /
//         C 95km で雑魚が止まり3連戦 / D 3体目の撃破 → 距離の再開 / E 96〜99km も同じ / F 99km の後 100km へ / G 死神
//         real = 補助の自動攻撃で戦う(無敵。-mfFightCap 秒で倒れなければ試験が倒す)。kill = 試験がすぐ倒す(流れだけ)
//  FE   … H 通常の3択に入る / I 60秒 / J 一時停止・カード選択の間は減らない / K 取り直しで 60秒へ戻る / 通常のカードが無い時は FE で3枚
//  LD   … L ラスダンの 90km はこれまでどおり(走りながらのラッシュ。3連戦/雑魚の大群にならない)
public partial class QaSweep
{
    string MfChar => Arg("-mfChar", "swordsman");
    static readonly Dictionary<string, string> MfNative90 = new Dictionary<string, string>
    { { "wasteland_road", "Wild/BlackKnight" }, { "natural_cave", "Cave/AncientDemon" }, { "sky_corridor", "Sky/Guardian" } };

    IEnumerator MapFinaleMode()
    {
        HookBossLogs();
        string parts = Arg("-mfParts", "FLOW,FE,LD");
        if (parts.Contains("FLOW")) foreach (var st in Arg("-mfStages", "wasteland_road,natural_cave,sky_corridor").Split(',')) yield return MfFlow(st);
        if (parts.Contains("FE")) yield return MfFinalEvo();
        if (parts.Contains("LD")) yield return MfLastDungeon();
        L("");
        foreach (var line in BossManager.TrioLog) L("[trio] " + line);
    }

    int MfAliveBosses() => WildAlive().Count(b => !(b is ReaperSisterBoss)) + DragonsAlive().Count + FindObjectsByType<MajinController>(FindObjectsSortMode.None).Count(m => m != null && !m.IsDead);

    // 1体のボスを倒すまで(real: 補助の攻撃で。上限を超えたら試験が倒す)。戻り値 = 自力で倒した秒(上限で倒した時は -1)
    IEnumerator MfFight(System.Action<float> done)
    {
        bool real = Arg("-mfFight", "real") == "real";
        float cap = float.Parse(Arg("-mfFightCap", "150"), System.Globalization.CultureInfo.InvariantCulture);
        float w = 0f;
        while (MfAliveBosses() == 0 && w < 20f) { yield return null; w += Time.deltaTime; }
        float t0 = Time.time; int k0 = gm.BossKillCount;
        while (gm.BossKillCount == k0 && MfAliveBosses() > 0)
        {
            if (!real || Time.time - t0 > cap) { KillAllBosses(); yield return new WaitForSeconds(0.3f); done(real ? -1f : 0f); yield break; }
            yield return null;
        }
        done(Time.time - t0);
    }

    IEnumerator MfFlow(string st)
    {
        L($"== map finale flow: {st} ({MfChar}, mix Lv{Arg("-mfBuildLv", "5")}, fight {Arg("-mfFight", "real")}) ==");
        yield return BeginRun(MfChar, st);
        SetGod(true);
        if (HighSpeedAssist.Instance != null) { HighSpeedAssist.Instance.SetEnabled(true); HighSpeedAssist.Instance.SetAutoAttack(true); HighSpeedAssist.Instance.SetAutoInBoss(true); }
        int lv = int.Parse(Arg("-mfBuildLv", "5"));
        foreach (var id in CardBuildPresets.Find("mix")) { var cd = CardDatabase.FindBaseById(id); if (cd != null) gm.ApplyCardEffectsStacked(cd, lv); }
        WarpTo(89850f);
        typeof(GameManager).GetMethod("ApplySprintArrivalLevel", NP).Invoke(gm, new object[] { 89850f });
        ZakoAttackLimiter.ResetStats();
        int picks0 = cardPicks;
        // ---- A: 90km は本来のボス(通常の関門) ----
        float w = 0f;
        while (!(Bm.IsBossPhase && MfAliveBosses() > 0) && w < 40f) { yield return null; w += Time.unscaledDeltaTime; }
        string key90 = Bm.CurrentEncounterKey;
        Check(Bm.IsBossPhase && Bm.CurrentGateIndex == 90 && Bm.CurrentTrio == null && !Bm.MapRushActive, $"A {st}: the 90km gate is a normal boss gate (gate {Bm.CurrentGateIndex}, trio {(Bm.CurrentTrio != null)}, old rush {Bm.MapRushActive})");
        Check(key90 == MfNative90[st], $"A {st}: the 90km boss is the map's own 90km boss ({key90})");
        float fs = 0f; yield return MfFight(x => fs = x);
        L($"[A] {st} 90km {key90}: {(fs < 0 ? "not down by the cap (the test finished it)" : $"down in {fs:F0}s")}");
        w = 0f; while (Bm.IsBossPhase && w < 40f) { yield return null; w += Time.unscaledDeltaTime; }
        w = 0f; while (!BossManager.ZakoRushNow && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
        Check(BossManager.ZakoRushNow && Bm.NextGateIndex == BossManager.FinaleTrioFirstK, $"A {st}: after the 90km boss the zako rush starts (rush {BossManager.ZakoRushNow}, next gate {Bm.NextGateIndex}km, d={gm.MaxDistance:F0})");
        // ---- B: 90〜95km の雑魚の大群 ----
        int lv90 = gm.Level, kills90 = gm.EnemyKillCount, picks90 = cardPicks; float exp90 = gm.TotalExpEarned, t90 = Time.time;
        var waveAlive = new float[5]; var waveSamples = new int[5]; var waveKills = new int[5]; var waveMax = new int[5]; var waveHeld = new int[5];
        int plans0 = EncounterDirector.RushPlans, members0 = EncounterDirector.RushMembers, lastKills = gm.EnemyKillCount, bossDuring = 0;
        float sampleAt = 0f;
        while (gm.MaxDistance < 95000f && Time.time - t90 < 400f)
        {
            int wave = BossManager.ZakoRushWave(gm.MaxDistance);
            if (Time.time >= sampleAt && !gm.IsRewardSequenceRunning)
            {
                sampleAt = Time.time + 0.5f;
                float camX = Camera.main != null ? Camera.main.transform.position.x : pc.transform.position.x;
                float half = Camera.main != null ? Camera.main.orthographicSize * Camera.main.aspect : 12f;
                int onScreen = FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Count(e => e != null && e.isActiveAndEnabled && !e.IsDying && Mathf.Abs(e.transform.position.x - camX) <= half + 1f);
                waveAlive[wave] += onScreen; waveSamples[wave]++; waveMax[wave] = Mathf.Max(waveMax[wave], onScreen);
                waveHeld[wave] = Mathf.Max(waveHeld[wave], ZakoAttackLimiter.Held);
                if (Bm.IsBossPhase) bossDuring++;
            }
            if (gm.EnemyKillCount != lastKills) { waveKills[wave] += gm.EnemyKillCount - lastKills; lastKills = gm.EnemyKillCount; }
            yield return null;
        }
        float tRush = Time.time - t90;
        int lv95 = gm.Level, picksRush = cardPicks - picks90;
        string ws = string.Join(" | ", Enumerable.Range(0, 5).Select(i => $"{90 + i}-{91 + i}km on-screen avg {(waveSamples[i] > 0 ? waveAlive[i] / waveSamples[i] : 0):F1} max {waveMax[i]} kills {waveKills[i]} attackers≤{waveHeld[i]}"));
        L($"[B] {st} zako rush {tRush:F0}s: {ws}");
        L($"[B] {st} kills {gm.EnemyKillCount - kills90}, EXP +{gm.TotalExpEarned - exp90:F0}, Lv {lv90}->{lv95} (+{lv95 - lv90}), card choices {picksRush}, plans {EncounterDirector.RushPlans - plans0}, enemies placed {EncounterDirector.RushMembers - members0}, attack slots granted {ZakoAttackLimiter.Granted} denied {ZakoAttackLimiter.Denied} max held {ZakoAttackLimiter.MaxHeld}/{ZakoAttackLimiter.MaxAttackers}");
        float avg0 = waveSamples[0] > 0 ? waveAlive[0] / waveSamples[0] : 0f, avg4 = waveSamples[4] > 0 ? waveAlive[4] / waveSamples[4] : 0f;
        Check(EncounterDirector.RushPlans - plans0 > 20, $"B {st}: the zako rush plans many encounters ({EncounterDirector.RushPlans - plans0})");
        Check(avg4 >= 2f && avg4 >= avg0, $"B {st}: the density rises to the end (90-91km avg {avg0:F1} -> 94-95km avg {avg4:F1})");
        Check(ZakoAttackLimiter.MaxHeld <= ZakoAttackLimiter.MaxAttackers, $"B {st}: simultaneous attackers stay within the cap ({ZakoAttackLimiter.MaxHeld}/{ZakoAttackLimiter.MaxAttackers})");
        Check(bossDuring == 0, $"B {st}: no boss gate during 91-94km ({bossDuring} samples in a boss phase)");
        Check(lv95 > lv90, $"B {st}: the player levels up in the zako rush (Lv {lv90}->{lv95})");
        // ---- C〜E: 95〜99km の3連戦 ----
        int picks95 = cardPicks, lvT0 = gm.Level;
        for (int k = BossManager.FinaleTrioFirstK; k <= BossManager.FinaleTrioLastK; k++)
        {
            w = 0f; while (!(Bm.IsBossPhase && Bm.CurrentTrio != null && Bm.CurrentTrio.k == k) && w < 60f) { yield return null; w += Time.unscaledDeltaTime; }
            var trio = Bm.CurrentTrio;
            Check(trio != null && trio.k == k && trio.plan.Count == 3, $"C {st}: {k}km is a 3-boss gate ({(trio != null ? Bm.LastTrioPlan : "none")})");
            if (trio == null) break;
            if (k == BossManager.FinaleTrioFirstK)
            {
                int plans95 = EncounterDirector.RushPlans; yield return new WaitForSeconds(3f);
                int zako = FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Count(e => e != null && e.isActiveAndEnabled && !e.IsDying && e.GetComponent<BonusEnemy>() == null);
                Check(EncounterDirector.RushPlans == plans95 && zako <= 2, $"C {st}: no zako after 95km (new plans {EncounterDirector.RushPlans - plans95}, zako alive {zako})");
            }
            float dGate = gm.MaxDistance;
            int maxTogether = 0; var gaps = new List<float>(); var fights = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                w = 0f; while (MfAliveBosses() == 0 && w < 30f) { yield return null; w += Time.unscaledDeltaTime; }
                maxTogether = Mathf.Max(maxTogether, MfAliveBosses());
                string who = Bm.CurrentEncounterKey;
                float f = 0f; yield return MfFight(x => f = x);
                fights.Add($"{who.Substring(who.IndexOf('/') + 1)} {(f < 0 ? "cap" : f.ToString("0") + "s")}");
                if (i < 2)
                {
                    float tDown = Time.time; float rt = Time.realtimeSinceStartup; int p0 = cardPicks;
                    w = 0f; while (MfAliveBosses() == 0 && w < 40f) { maxTogether = Mathf.Max(maxTogether, MfAliveBosses()); yield return null; w += Time.unscaledDeltaTime; }
                    gaps.Add(Time.time - tDown);
                    if (cardPicks > p0) L($"[C] {st} {k}km: {cardPicks - p0} level-up choice(s) between boss {i + 1} and {i + 2}");
                    Check(Bm.IsBossPhase && Bm.CurrentTrio == trio, $"C {st}: {k}km stays one gate between bosses {i + 1} and {i + 2}");
                }
                maxTogether = Mathf.Max(maxTogether, MfAliveBosses());
            }
            Check(maxTogether <= 1, $"C {st}: {k}km bosses come one at a time (max together {maxTogether})");
            Check(gm.MaxDistance <= dGate + 1f, $"C {st}: the distance stays at the gate during the 3 bosses ({dGate:F0} -> {gm.MaxDistance:F0}m)");
            Check(gaps.All(g => g >= 0.8f && g <= 8f), $"C {st}: {k}km the next boss comes after a short gap (game s: {string.Join(", ", gaps.Select(g => g.ToString("0.0")))})");
            L($"[C] {st} {k}km: {string.Join(" > ", fights)} | gaps {string.Join("/", gaps.Select(g => g.ToString("0.0")))}s");
            // D: 3体目 → 報酬 → 距離の再開
            w = 0f; while (Bm.IsBossPhase && w < 40f) { yield return null; w += Time.unscaledDeltaTime; }
            float dEnd = gm.MaxDistance;
            w = 0f; while (gm.MaxDistance < dEnd + 20f && w < 10f) { yield return null; w += Time.deltaTime; }
            Check(!Bm.IsBossPhase && gm.MaxDistance > dEnd + 10f, $"D {st}: after the 3rd boss of {k}km the distance resumes ({dEnd:F0} -> {gm.MaxDistance:F0}m)");
        }
        int lv99 = gm.Level, picksTrio = cardPicks - picks95;
        L($"[E] {st} 95-99km: Lv {lvT0}->{lv99} (+{lv99 - lvT0}), card choices {picksTrio} (incl. 5 boss rewards), bosses spawned {Bm.TrioBossesSpawned}");
        // ---- F/G: 100km の死神 ----
        w = 0f; while (!Bm.DeathSpawned && w < 90f) { yield return null; w += Time.unscaledDeltaTime; }
        Check(Bm.DeathSpawned && !Bm.IsBossPhase, $"F/G {st}: the run reaches 100km and the reaper chase starts ({gm.MaxDistance:F0}m, reaper {Bm.DeathSpawned})");
        L($"[G] {st} total 90-100km: Lv {lv90}->{gm.Level}, card choices {cardPicks - picks0}, FE active {FinalEvolution.ActiveCount}, run time {Time.time - t90:F0}s");
        SetGod(false);
        yield return EndRun();
    }

    // ---- FINAL EVOLUTION: 通常の3択 / 60秒 / 止まっている間は減らない / 取り直しで 60秒 ----
    IEnumerator MfWaitChoice(float timeout = 10f)
    {
        float w = 0f;
        while (!(gm.IsRewardSequenceWaitingForSelection && gm.DebugChoiceOpen) && w < timeout) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.2f);
    }
    IEnumerator MfPick(int idx)
    {
        var seq = FindFirstObjectByType<RewardCardSequence>();
        if (seq == null) yield break;
        seq.OnCardClicked(idx); yield return new WaitForSecondsRealtime(0.25f); seq.OnCardClicked(idx);
        float w = 0f; while (gm.DebugChoiceOpen && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.3f);
    }

    IEnumerator MfFinalEvo()
    {
        L("== final evolution 60s ==");
        yield return BeginRun(MfChar, "wasteland_road");
        SetGod(true);
        autoPickHold = true;
        DebugRun.Begin("qa map finale FE");
        const string id = "attack_up";
        gm.FinalEvoTestPrepare(id);
        FinalEvolution.DebugMakeReady(id);
        yield return new WaitForSeconds(0.5f);
        // H: 通常の3択(3枚、FE は1枠)
        gm.DebugTriggerLevelUp();
        yield return MfWaitChoice();
        var ids = gm.DebugPendingChoiceIds;
        int fe = System.Array.FindIndex(ids, x => FinalEvolution.IsChoiceId(x));
        Check(ids.Length == 3 && fe >= 0 && ids.Count(FinalEvolution.IsChoiceId) == 1, $"H: FINAL EVOLUTION is one of the normal 3 cards ({string.Join(", ", ids)})");
        yield return MfPick(Mathf.Max(0, fe));
        // I: 60秒
        float r0 = FinalEvolution.Remaining(id);
        Check(FinalEvolution.IsActive(id) && Mathf.Abs(r0 - 60f) < 1f, $"I: active for 60s ({FinalEvolution.IsActive(id)}, {r0:F1}s)");
        yield return new WaitForSeconds(5f);
        float r1 = FinalEvolution.Remaining(id);
        Check(r0 - r1 > 4f && r0 - r1 < 6.5f, $"I: the timer counts run time ({r0:F1} -> {r1:F1}s after 5s)");
        // J: 一時停止 / カード選択の間は減らない
        var owner = new object();
        TimeControl.Pause(owner);
        yield return new WaitForSecondsRealtime(3f);
        float r2 = FinalEvolution.Remaining(id);
        TimeControl.Resume(owner);
        Check(Mathf.Abs(r2 - r1) < 0.3f, $"J: paused - the timer stops ({r1:F1} -> {r2:F1}s over 3s real)");
        string normalPick = null;
        gm.DebugTriggerLevelUp();
        yield return MfWaitChoice();
        float r3 = FinalEvolution.Remaining(id);
        yield return new WaitForSecondsRealtime(3f);
        float r4 = FinalEvolution.Remaining(id);
        Check(Mathf.Abs(r4 - r3) < 0.3f, $"J: card choice open - the timer stops ({r3:F1} -> {r4:F1}s over 3s real)");
        ids = gm.DebugPendingChoiceIds;
        int nIdx = System.Array.FindIndex(ids, x => !FinalEvolution.IsChoiceId(x));
        normalPick = nIdx >= 0 ? ids[nIdx] : null;
        int feIdx2 = System.Array.FindIndex(ids, x => FinalEvolution.IsChoiceId(x));
        Check(ids.Length == 3, $"J: the next level-up is again 3 cards ({string.Join(", ", ids)})");
        yield return MfPick(Mathf.Max(0, nIdx));
        // K: 取り直し → 残り 60秒へ(足さない)
        yield return new WaitForSeconds(4f);
        float r5 = FinalEvolution.Remaining(id);
        int ref0 = FinalEvolution.Refreshes;
        gm.DebugTriggerLevelUp();
        yield return MfWaitChoice();
        ids = gm.DebugPendingChoiceIds;
        int feIdx = System.Array.FindIndex(ids, x => x == FinalEvolution.ChoicePrefix + id);
        Check(feIdx >= 0, $"K: the active FINAL EVOLUTION is offered again ({string.Join(", ", ids)})");
        if (feIdx >= 0) yield return MfPick(feIdx); else yield return MfPick(0);
        float r6 = FinalEvolution.Remaining(id);
        Check(FinalEvolution.Refreshes == ref0 + 1 && Mathf.Abs(r6 - 60f) < 1f && FinalEvolution.IsActive(id), $"K: re-acquired - the timer goes back to 60s, not added ({r5:F1} -> {r6:F1}s)");
        // HUD の表示(撮影)
        yield return new WaitForSeconds(0.5f);
        int drawn0 = GameManager.FeHudDrawn;
        yield return new WaitForSecondsRealtime(0.3f);
        Check(GameManager.FeHudDrawn > drawn0, $"I: the remaining time is shown on the HUD (drawn {GameManager.FeHudDrawn - drawn0}, skip '{GameManager.FeHudSkip}', active {FinalEvolution.ActiveCount}, choice {gm.DebugChoiceOpen})");
        Shot("fe_hud");
        yield return null; yield return null; // 撮影は描画の終わり(撮った直後に次の操作をしない)
        // 通常のカードが無い時: FE で3枚
        foreach (var d in gm.DeckCards.ToList()) gm.FinalEvoTestPrepare(d);
        var ready = new List<string>();
        foreach (var d in gm.DeckCards) if (FinalEvolution.Supported(d) && d != id && ready.Count < 3) { FinalEvolution.DebugMakeReady(d); ready.Add(d); }
        gm.DebugTriggerLevelUp();
        yield return MfWaitChoice();
        ids = gm.DebugPendingChoiceIds;
        Check(ids.Length == 3 && ids.All(FinalEvolution.IsChoiceId), $"H: with no normal card left, the choice is still 3 cards of FINAL EVOLUTION ({string.Join(", ", ids)})");
        Shot("fe_three");
        yield return null; yield return null; // 撮影は描画の終わり(撮った直後に次の操作をしない)
        yield return MfPick(0);
        // 60秒で終わる(早送りで確かめる)
        FinalEvolution.DebugSetRemaining(id, 1.5f);
        yield return new WaitForSeconds(2.5f);
        Check(!FinalEvolution.IsActive(id) && FinalEvolution.StageOf(id) == FinalEvolution.Stage.Ready, $"I: it ends when the time is up and becomes READY again ({FinalEvolution.StageOf(id)})");
        autoPickHold = false;
        SetGod(false);
        yield return EndRun();
    }

    // ---- L: ラスダンの 90km はこれまでどおり ----
    IEnumerator MfLastDungeon()
    {
        L("== last dungeon unchanged ==");
        yield return BeginRun(MfChar, BossManager.LastStageId);
        SetGod(true);
        WarpTo(89900f);
        float w = 0f; while (!Bm.RushActive && w < 40f) { yield return null; w += Time.unscaledDeltaTime; }
        Check(Bm.RushActive && Bm.CurrentTrio == null && !BossManager.ZakoRushNow, $"L: the last dungeon 90km is still the running boss rush (rush {Bm.RushActive}, trio {(Bm.CurrentTrio != null)}, zako rush {BossManager.ZakoRushNow})");
        SetGod(false);
        yield return EndRun();
    }
}
#endif
