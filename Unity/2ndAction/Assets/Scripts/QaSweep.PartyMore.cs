#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

// CPU サポートの確認の続き(2026-10-14)。
//  -qaPartyResume <dir> -cpuAllies a,b   … 中断 → CONTINUE で編成/取ったカード/HP/倒れている状態が引き継がれる。記録の人数も
//  -qaPartySprint <dir> -cpuAllies a,b   … 疾走出発の到着の後、仲間がプレイヤーの近くにいる
//  -qaPartyRanking <dir>                  … サポートの記録は別の表。サポートの表がまだ無いサーバーでも、ソロの送信待ちは送れる
public partial class QaSweep
{
    IEnumerator PartyResumeMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        RunCheckpoint.Clear();
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        float w = 0f; while (gm.CountdownActive && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        SetKmh(100f);
        Check(CpuParty.Size == CpuParty.Request.Count && CpuParty.Size >= 2, $"spawned {CpuParty.Size} allies");
        Check(RunLedger.Current != null && RunLedger.Current.partySize == CpuParty.Size + 1 && RunLedger.Current.supportIds == string.Join(",", CpuParty.Allies.Select(a => a.CharacterId)),
            $"the run record has the support ({RunLedger.Current?.partySize} / {RunLedger.Current?.supportIds})");
        yield return new WaitForSeconds(25f);
        // P2 を倒す / P3 は HP を減らす
        var a2 = CpuParty.Allies[0]; var a3 = CpuParty.Allies[1];
        for (int k = 0; k < 20 && !a2.Down; k++) { a2.SetHp(1); typeof(PlayerController).GetField("hitInvincibleTimer", NP).SetValue(a2.pc, 0f); a2.pc.TakeDamage(false, "qa"); if (!a2.Down) yield return new WaitForSeconds(0.3f); }
        a3.SetHp(Mathf.Max(1, a3.MaxHp / 2 + 3));
        yield return new WaitForSeconds(1.5f);
        var before = CpuParty.Export();
        string snap = string.Join(" | ", before.Select(m => $"{m.characterId} picks[{string.Join(",", m.picks)}] hp={m.hp} down={m.down} reviveIn={m.reviveIn:F1}"));
        L($"[partyResume] before suspend: {snap}");
        Check(before[0].down && before[0].reviveIn > 0f, "P2 is down before the suspend");
        float d0 = gm.MaxDistance;
        stopKeepAlive = true;
        gm.ReturnToHome();
        w = 0f; while ((GameManager.Instance == null || GameManager.Instance == gm || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        yield return new WaitForSecondsRealtime(1f);
        Check(RunCheckpoint.HasActiveRun && RunCheckpoint.Load().party.Count == before.Count, $"the suspend data has the party ({RunCheckpoint.Load().party.Count})");
        CpuParty.Request.Clear(); // 出発の画面の選択が変わっても、CONTINUE は中断した時の仲間
        yield return ResumeContinue("party");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        var after = CpuParty.Export();
        L($"[partyResume] after continue: " + string.Join(" | ", after.Select(m => $"{m.characterId} picks[{string.Join(",", m.picks)}] hp={m.hp} down={m.down} reviveIn={m.reviveIn:F1}")));
        Check(after.Count == before.Count && after.Select(m => m.characterId).SequenceEqual(before.Select(m => m.characterId)), "same allies after CONTINUE");
        Check(after.Zip(before, (x, y) => x.picks.SequenceEqual(y.picks)).All(b => b), "the allies' cards (picks) are the same");
        Check(after[1].hp == before[1].hp && !after[1].down, $"P3 HP is kept ({after[1].hp} / {before[1].hp})");
        Check(after[0].down, "P2 is still down after CONTINUE (no free revive)");
        Check(RunLedger.Current != null && RunLedger.Current.partySize == before.Count + 1, $"the run record keeps the party size ({RunLedger.Current?.partySize})");
        float wait = 0f; var p2 = CpuParty.Allies[0];
        while (p2.Down && wait < CpuParty.ReviveSeconds + 5f) { yield return null; wait += Time.deltaTime; }
        Check(!p2.Down && Mathf.Abs(p2.pc.transform.position.x - PlayerController.Instance.transform.position.x) < 12f, $"P2 revives near the player after CONTINUE ({wait:F1}s)");
        Shot("party_after_continue");
        yield return null; yield return null;
        yield return EndRun();
    }

    IEnumerator PartySprintMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        gm = GameManager.Instance;
        RunCheckpoint.Clear();
        SprintRecords.DevUnlockAll = true;
        string stage = "wasteland_road";
        gm.SetSelectedCharacter("swordsman"); gm.SetSelectedStage(stage);
        SprintRunner.QaRingPolicy = 1; SprintRunner.QaTimeScale = 3f;
        bool ok = gm.DepartSprint(stage, 10000);
        Check(ok, "sprint departure accepted");
        float w = 0f;
        while (SprintRunner.Instance == null && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        bool hiddenDuring = false;
        w = 0f;
        while ((gm.SprintActive || gm.CountdownActive || !gm.HasStarted) && w < 240f)
        {
            if (gm.SprintActive && CpuParty.Allies.Count > 0 && CpuParty.Allies.All(a => !a.gameObject.activeSelf)) hiddenDuring = true;
            yield return null; w += Time.unscaledDeltaTime;
        }
        SprintRunner.QaRingPolicy = -1; SprintRunner.QaTimeScale = 1f;
        pc = PlayerController.Instance;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        yield return new WaitForSeconds(1.5f);
        L($"[partySprint] arrived d={gm.MaxDistance:F0} allies: " + string.Join(" | ", CpuParty.Allies.Select(a => $"P{a.Pn} active={a.gameObject.activeSelf} dx={a.pc.transform.position.x - pc.transform.position.x:F1} dy={a.pc.transform.position.y - pc.transform.position.y:F1}")));
        Check(CpuParty.Size == CpuParty.Request.Count && CpuParty.Size > 0, "allies exist after the sprint");
        Check(hiddenDuring, "allies are hidden during the sprint presentation");
        Check(CpuParty.Allies.All(a => a.gameObject.activeSelf && Mathf.Abs(a.pc.transform.position.x - pc.transform.position.x) < 12f), "allies are next to the player after the arrival");
        Shot("party_after_sprint");
        yield return null; yield return null;
        yield return new WaitForSeconds(10f);
        Check(CpuParty.Allies.All(a => a.LandedHits >= 0 && a.gameObject.activeSelf), "allies keep running after the arrival");
        yield return EndRun();
    }

    IEnumerator PartyRankingMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        string mock = System.IO.Path.Combine(outDir, "lb_mock");
        var me = new MockBackend(mock, "player-me");
        Leaderboard.Backend = me;
        Leaderboard.DevReset(); Leaderboard.Backend = me;
        SaveStore.SetInt(Leaderboard.JoinedKey, 1);
        long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Leaderboard.Entry E(string id, int m, int party, string allies) => new Leaderboard.Entry
        {
            runId = id, stageId = "wasteland_road", characterId = "swordsman", meters = m, playSeconds = m / 25f + 10f, startedUtc = now - (long)(m / 25f) - 60, committedUtc = now,
            partySize = party, allies = allies,
        };
        string Id() => System.Guid.NewGuid().ToString("N");
        var solo = E(Id(), 3000, 1, ""); var sup = E(Id(), 5000, 3, "archer,mage");
        // 確定の流れ(OnSuccessCommitted)と同じ: 自己ベスト → 送信待ち
        var runSolo = new RunLedger.Run { runId = solo.runId, stageId = solo.stageId, characterId = "swordsman", maxReached = 3000, playSeconds = solo.playSeconds, startedUtc = solo.startedUtc, officialStage = true };
        var runSup = new RunLedger.Run { runId = sup.runId, stageId = sup.stageId, characterId = "swordsman", maxReached = 5000, playSeconds = sup.playSeconds, startedUtc = sup.startedUtc, officialStage = true, partySize = 3, supportIds = "archer,mage" };
        Check(Leaderboard.IsEligible(runSup, out string why) , $"a support run is eligible for the support board ({why})");
        var runDevAlly = new RunLedger.Run { runId = Id(), stageId = "wasteland_road", characterId = "swordsman", maxReached = 100, playSeconds = 30, startedUtc = now - 40, officialStage = true, partySize = 2, supportIds = "archer", officialParty = false };
        Check(!Leaderboard.IsEligible(runDevAlly, out string why2), $"an ally that is not officially unlocked makes the run ineligible ({why2})");
        Leaderboard.OnSuccessCommitted(runSup);
        Leaderboard.OnSuccessCommitted(runSolo);
        Check(Leaderboard.PendingCountFor("wasteland_road", true) == 1 && Leaderboard.PendingCountFor("wasteland_road", false) == 1, "a longer support run does not remove the solo pending post (separate boards)");
        Check(Leaderboard.VerifiedBest("wasteland_road", false)?.meters == 3000 && Leaderboard.VerifiedBest("wasteland_road", true)?.meters == 5000, "verified bests are kept per board");
        // サポートの表がまだ無いサーバー: サポートは残り、ソロは送れる
        MockBackend.SupportBoardsMissing = true;
        Task t = Leaderboard.FlushAsync("qa");
        float w = 0f; while (!t.IsCompleted && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
        Check(Leaderboard.PendingCountFor("wasteland_road", false) == 0 && Leaderboard.PendingCountFor("wasteland_road", true) == 1, "support board missing: the solo post is sent, the support post stays pending");
        MockBackend.SupportBoardsMissing = false;
        t = Leaderboard.FlushAsync("qa2");
        // (順位の画面の自動の再送が先に走っている時は、こちらの呼び出しはすぐ戻る。送信待ちが無くなるまで待つ)
        w = 0f; while ((!t.IsCompleted || Leaderboard.Sending || Leaderboard.PendingCount > 0) && w < 8f) { yield return null; w += Time.unscaledDeltaTime; if (t.IsCompleted && !Leaderboard.Sending && Leaderboard.PendingCount > 0 && w > 1f) t = Leaderboard.FlushAsync("qa3"); }
        Check(Leaderboard.PendingCount == 0, "after the support board exists, the pending support post is sent automatically");
        Leaderboard.Page ps = null, pu = null;
        var lt = me.Load("wasteland_road", false, 50); w = 0f; while (!lt.IsCompleted && w < 5f) { yield return null; w += Time.unscaledDeltaTime; } ps = lt.Result;
        lt = me.Load("wasteland_road", true, 50); w = 0f; while (!lt.IsCompleted && w < 5f) { yield return null; w += Time.unscaledDeltaTime; } pu = lt.Result;
        L($"[partyRanking] solo board: {string.Join(",", ps.top.Select(r => r.meters))} / support board: {string.Join(",", pu.top.Select(r => r.meters + "(" + r.allies + ")"))}");
        Check(ps.top.Count == 1 && ps.top[0].meters == 3000 && pu.top.Count == 1 && pu.top[0].meters == 5000 && pu.top[0].allies == "archer,mage", "the support record is only on the support board");
        // 検査: 仲間の数が合わない / 一覧にないキャラ は拒否
        Check(LeaderboardRules.Validate(E(Id(), 100, 3, "archer"), 0, null, now) == "party mismatch", "validation: party size and allies must match");
        Check(LeaderboardRules.Validate(E(Id(), 100, 2, "nobody"), 0, null, now) == "bad ally", "validation: unknown ally");
        Check(LeaderboardRules.Validate(E(Id(), 100, 1, ""), 0, null, now) == "", "validation: an old/solo entry is still fine");
        Leaderboard.DevReset();
        yield break;
    }
    // 識別用オーラ/名前の見た目(2026-10-14)。 -qaPartyShots <dir> -cpuAllies a,b,c [-qaPartyViews 0,1,2](縦の画面の見せ方)
    IEnumerator PartyShotsMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        string views = Arg("-qaPartyViews", "0");
        foreach (var vs in views.Split(','))
        {
            int view = int.Parse(vs);
            PortraitRunView.Set(view);
            GameSettings.SetAuraIntensity(0.6f, false);
            yield return BeginRun(Arg("-qaPartyChar", "swordsman"), Arg("-qaPartyStage", "wasteland_road"));
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            float w = 0f; while (gm.CountdownActive && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
            SetKmh(60f);
            yield return new WaitForSeconds(6f);
            Check(ParticipantAura.All.Count == CpuParty.Size + 1, $"view{view}: an aura on the player and each ally ({ParticipantAura.All.Count})");
            Shot($"aura_view{view}_run"); yield return null; yield return null;
            yield return new WaitForSeconds(2.5f);
            Shot($"aura_view{view}_run2"); yield return null; yield return null;
            GameSettings.SetAuraIntensity(0f, false);
            yield return new WaitForSeconds(0.3f);
            Shot($"aura_view{view}_intensity0"); yield return null; yield return null;
            GameSettings.SetAuraIntensity(0.6f, false);
            // ボスの予兆(赤い警告)がオーラ/名前に隠れないか
            BossManager.Instance.DebugSpawnBossForTest(0, 1);
            yield return new WaitForSeconds(5f);
            Shot($"aura_view{view}_boss"); yield return null; yield return null;
            yield return new WaitForSeconds(3f);
            Shot($"aura_view{view}_boss2"); yield return null; yield return null;
            yield return EndRun();
        }
    }
}
#endif
