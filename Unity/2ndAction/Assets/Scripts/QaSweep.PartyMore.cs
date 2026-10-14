#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

// CPU サポートの確認の続き(2026-10-14)。
//  -qaPartyResume <dir> -cpuAllies a,b   … 中断 → CONTINUE で編成/取ったカード/HP/倒れている状態が引き継がれる。記録の人数も
//  -qaPartySprint <dir> -cpuAllies a,b   … 疾走出発の到着の後、仲間がプレイヤーの近くにいる
//  -qaPartyRanking <dir>                  … ランキングはソロだけ: サポートのランは対象外、前の版のサポートの送信待ちは取り除く(ソロへ振り替えない)
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

    // 2026-10-14(仕様変更): ランキングはソロだけ。サポートありのランは投稿しない / 前の版のサポートの送信待ち・自己ベストは起動時に取り除く(ソロへ振り替えない)
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
        // ① 前の版が残した物: サポートの送信待ち/自己ベスト(+ ソロの送信待ち/自己ベスト)
        var oldSup = E(Id(), 5000, 3, "archer,mage"); var oldSolo = E(Id(), 2000, 1, "");
        var oldSup2 = E(Id(), 4000, 2, "ninja"); oldSup2.stageId = "natural_cave";
        string J(params Leaderboard.Entry[] es) => "{\"items\":[" + string.Join(",", es.Select(x => JsonUtility.ToJson(x))) + "]}";
        SaveStore.SetString(Leaderboard.PendingKey, J(oldSup, oldSolo, oldSup2));
        SaveStore.SetString(Leaderboard.VerifiedKey, J(oldSup, oldSolo));
        int purged = Leaderboard.PurgeSupportEntries();
        Check(purged == 3, $"old support records are removed from the queue and the verified bests ({purged})");
        Check(Leaderboard.PendingCount == 1 && Leaderboard.PendingCountFor("wasteland_road") == 1 && Leaderboard.PendingCountFor("natural_cave") == 0, "the solo pending post is kept, nothing moved to the solo board");
        Check(Leaderboard.VerifiedBest("wasteland_road")?.meters == 2000, "the solo verified best is kept (the support 5000m is not moved to solo)");
        Check(Leaderboard.PurgeSupportEntries() == 0, "a second start-up removes nothing more");
        // ② 資格: 一度でもサポートを使ったランは対象外(ソロは対象)
        var runSolo = new RunLedger.Run { runId = Id(), stageId = "wasteland_road", characterId = "swordsman", maxReached = 3000, playSeconds = 3000 / 25f + 10f, startedUtc = now - 200, officialStage = true };
        var runSup = new RunLedger.Run { runId = Id(), stageId = "wasteland_road", characterId = "swordsman", maxReached = 9000, playSeconds = 9000 / 25f + 10f, startedUtc = now - 500, officialStage = true, partySize = 3, supportIds = "archer,mage" };
        var runSupGone = new RunLedger.Run { runId = Id(), stageId = "wasteland_road", characterId = "swordsman", maxReached = 9000, playSeconds = 400, startedUtc = now - 500, officialStage = true, partySize = 1, supportIds = "archer" };
        Check(Leaderboard.IsEligible(runSolo, out string w1), $"a solo run is eligible ({w1})");
        Check(!Leaderboard.IsEligible(runSup, out string w2) && w2.Contains("CPU support"), $"a CPU support run is not eligible ({w2})");
        Check(!Leaderboard.IsEligible(runSupGone, out string w3), $"a run that used support once is not eligible even with the size back to 1 ({w3})");
        Leaderboard.OnSuccessCommitted(runSolo);
        Check(Leaderboard.PendingCount == 1 && Leaderboard.VerifiedBest("wasteland_road")?.meters == 3000, "a longer solo run replaces the shorter solo pending post");
        // ③ サーバーの検査(サーバーの omm_submit_run.js と同じ): ソロだけ
        Check(LeaderboardRules.Validate(E(Id(), 100, 3, "archer,mage"), 0, null, now) == "solo only", "validation: a support entry is rejected (solo only)");
        Check(LeaderboardRules.Validate(E(Id(), 100, 1, "archer"), 0, null, now) == "solo only", "validation: allies with size 1 are rejected");
        Check(LeaderboardRules.Validate(E(Id(), 100, 1, ""), 0, null, now) == "", "validation: a solo entry is fine");
        Task t = Leaderboard.FlushAsync("qa");
        float w = 0f; while ((!t.IsCompleted || Leaderboard.Sending || Leaderboard.PendingCount > 0) && w < 8f) { yield return null; w += Time.unscaledDeltaTime; if (t.IsCompleted && !Leaderboard.Sending && Leaderboard.PendingCount > 0 && w > 1f) t = Leaderboard.FlushAsync("qa2"); }
        Check(Leaderboard.PendingCount == 0 && Leaderboard.LastSendError == "", "the solo post is sent, nothing is left as 'pending' or 'failed'");
        var lt = me.Load("wasteland_road", 50); w = 0f; while (!lt.IsCompleted && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
        var ps = lt.Result;
        L($"[partyRanking] solo board: {string.Join(",", ps.top.Select(r => r.meters))}");
        Check(ps.top.Count == 1 && ps.top[0].meters == 3000, "the board has only the solo record");
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
        bool firstView = true;
        foreach (var vs in views.Split(','))
        {
            int view = int.Parse(vs);
            PortraitRunView.Set(view);
            if (firstView)
            {
                // 出発の画面: CPU サポートのボタンと仲間の一覧
                firstView = false;
                var g0 = GameManager.Instance;
                g0.OpenStageSelect();
                yield return new WaitForSecondsRealtime(1.5f);
                Shot("support_button"); yield return null; yield return null;
                SupportDeparturePanel.DebugOpen(true);
                yield return new WaitForSecondsRealtime(0.5f);
                Shot("support_panel"); yield return null; yield return null;
                SupportDeparturePanel.DebugOpen(false);
                // 本物の「出発」で出る(画面の閉じ方も普段どおり)
                g0.SetSelectedCharacter(Arg("-qaPartyChar", "swordsman"));
                g0.DepartFromStageSelect(Arg("-qaPartyStage", "wasteland_road"));
                float wc = 0f;
                while (!(GameManager.Instance != null && GameManager.Instance.HasStarted && !GameManager.Instance.CountdownActive) && wc < 20f) { yield return null; wc += Time.unscaledDeltaTime; }
                gm = GameManager.Instance; pc = PlayerController.Instance;
                stopKeepAlive = false; StartCoroutine(KeepAlive());
                Check(!gm.IsOverlayOpen && CpuParty.Size == CpuParty.Request.Count, $"departed from the stage select with the allies ({CpuParty.Size}), the stage select is closed");
            }
            else
            {
                GameSettings.SetAuraIntensity(0.6f, false);
                yield return BeginRun(Arg("-qaPartyChar", "swordsman"), Arg("-qaPartyStage", "wasteland_road"));
            }
            GameSettings.SetAuraIntensity(0.6f, false);
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
