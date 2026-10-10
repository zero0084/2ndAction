#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// 疾走出発(2026-10-05 試作)の確認。 -qaSprint <dir> [-qaSprintOnly ABCDEFG]
//  A 解放条件(到達/門番の撃破記録/上限90km/ラスダン)                   B 30km へ(リング全部逃す): 自動取得の回数=飛ばす関門の数、
//    到着地点/次の関門/速さ/足場/Lv/距離 EXP なし/中断データ1回/MILE から除く/撃破記録を付けない → 30km のボスへ正常につながる
//  C 20km へ(リング全部くぐる): 追加の3択がリングの数だけ(成功時だけ)、選択中は進まない
//  D Lv9 上限: デッキ2枚 → 上限で候補が尽きたら取らない(9を超えない)       E アプリ中断(裏へ)の間は進まない
//  F 通常の出発は疾走しない(カウントダウン→0mから)                      G 演出の所要時間(目的地別)
//  (2026-10-06)H 90km・デッキ10枚・リング全部: 候補が尽きた原因の確認(全 Lv9 か)、尽きた後のリングは MILE
//  I 最初から全 Lv9: リングは全部 MILE(選択なし・止まらない)、開始で案内、FINISH で持ち帰る
//  J 途中で全 Lv9(デッキ1枚・20km): カード → 案内1回 → MILE、GAME OVER で失う
//  K 12キャラ: 表示 80%・リングの中心=胴体、リング成功、通常のランへの補間
public partial class QaSweep
{
    bool SprintCase(char c) { string o = Arg("-qaSprintOnly", ""); return o == "" || o.IndexOf(c) >= 0; }

    float sprintWorstFrame;
    IEnumerator TrackSprintWorstFrame(float secs)
    {
        float t = 0f;
        while (t < secs) { yield return null; t += Time.unscaledDeltaTime; if (Time.frameCount - lastShotFrame > 2) sprintWorstFrame = Mathf.Max(sprintWorstFrame, Time.unscaledDeltaTime); } // 撮影の直後は数えない
    }

    IEnumerator SprintMode()
    {
        Application.targetFrameRate = 60;
        FreezeDiagnostics.EventTap += m => { if (m.StartsWith("[Damage] Hit reason=")) FreezeDiagnosticsHits++; };
        // ---- A: 解放条件(セーブの値を直接置いて、元へは戻す: テストの後でレジストリごと戻す)
        if (SprintCase('A'))
        {
            float w0 = 0f;
            while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
            gm = GameManager.Instance;
            SprintRecords.DevUnlockAll = false;
            var setBest = typeof(GameManager).GetMethod("SetStageBest", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            PlayerPrefs.DeleteKey(SaveKeys.SprintGates); SprintRecords.Reload();
            setBest.Invoke(gm, new object[] { "wasteland_road", 0.0 });
            int n0 = SprintRecords.Destinations("wasteland_road").Count(d => d.unlocked);
            setBest.Invoke(gm, new object[] { "wasteland_road", 35000.0 });
            int nReachOnly = SprintRecords.Destinations("wasteland_road").Count(d => d.unlocked);
            SprintRecords.MarkGateCleared("wasteland_road", 10); SprintRecords.MarkGateCleared("wasteland_road", 20);
            var d1 = SprintRecords.Destinations("wasteland_road");
            setBest.Invoke(gm, new object[] { "natural_cave", 55000.0 });
            SprintRecords.MarkGateCleared("natural_cave", 10); SprintRecords.MarkGateCleared("natural_cave", 30);
            var d2 = SprintRecords.Destinations("natural_cave");
            setBest.Invoke(gm, new object[] { "last_corridor", 100000.0 });
            for (int g = 10; g <= 90; g += 10) SprintRecords.MarkGateCleared("last_corridor", g);
            var d3 = SprintRecords.Destinations("last_corridor");
            string Fmt(List<SprintRecords.Destination> l) => string.Join(" ", l.Select(d => $"{d.meters / 1000}{(d.unlocked ? "o" : "x")}"));
            L($"[A] none={n0} reachOnly35k={nReachOnly} | wasteland(best35k,10/20 cleared): {Fmt(d1)} | cave(best55k,10/30): {Fmt(d2)} [{d2.First(d => !d.unlocked).why}] | lastCorridor(all): {Fmt(d3)}");
            Check(n0 == 0, "A: nothing unlocked without records");
            Check(nReachOnly == 1 && SprintRecords.IsUnlocked("wasteland_road", 10000, out _), "A: reaching 35km alone only unlocks 10km (no 10k gatekeeper to skip)");
            Check(d1.Where(d => d.unlocked).Select(d => d.meters).SequenceEqual(new[] { 10000, 20000, 30000 }), "A: 10/20km gatekeepers cleared + reached 35km -> 10/20/30km (40km not reached)");
            Check(d2.Where(d => d.unlocked).Select(d => d.meters).SequenceEqual(new[] { 10000, 20000 }), "A: the uncleared 20km gatekeeper blocks 30km+ (no jumping over it)");
            Check(d3.Max(d => d.meters) == 90000 && d3.All(d => d.unlocked), "A: destinations stop at 90km (no jumping over the reapers / last dungeon finale)");
            PlayerPrefs.DeleteKey(SaveKeys.SprintGates); SprintRecords.Reload();
        }

        SprintRecords.DevUnlockAll = true;
        if (SprintCase('B')) yield return SprintRunCase("B", "wasteland_road", 30000, 0, true);
        if (SprintCase('C')) yield return SprintRunCase("C", "natural_cave", 20000, 1, false);
        if (SprintCase('D')) yield return SprintRunCase("D", "sky_corridor", 30000, 0, false, deckSize: 2);
        if (SprintCase('E')) yield return SprintRunCase("E", "wasteland_road", 10000, 0, false, appPause: true);
        if (SprintCase('F'))
        {
            yield return BeginRun("swordsman", "wasteland_road");
            Check(!gm.SprintActive && gm.MaxDistance < 50f && SprintRunner.Instance == null, $"F: a normal departure does not sprint (d={gm.MaxDistance:F0})");
            yield return EndRun();
        }
        if (SprintCase('H')) yield return SprintRunCase("H", "wasteland_road", 90000, 1, false, deckSize: 10); // 2026-10-10: 既定のデッキは12枚になった。説明どおり10枚で試す
        if (SprintCase('I')) yield return SprintRunCase("I", "wasteland_road", 20000, 1, false, deckSize: 2, preMax: true, endWith: "win");
        if (SprintCase('J')) yield return SprintRunCase("J", "natural_cave", 20000, 1, false, deckSize: 1, endWith: "gameover");
        if (SprintCase('K'))
        {
            foreach (var def in CharacterDatabase.AllCharacters)
                yield return SprintRunCase("K_" + def.characterId, "wasteland_road", 10000, 1, false, character: def.characterId, quick: true);
        }
        if (SprintCase('G'))
        {
            var t = SprintTuning.I;
            L("[G] durations (without ring choices): " + string.Join(" ", Enumerable.Range(1, 9).Select(i => $"{i * 10}km={t.SecondsFor(i * 10000):F0}s")));
        }
        SprintRecords.DevUnlockAll = false;
    }

    IEnumerator WaitHome()
    {
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        yield return new WaitForSecondsRealtime(0.5f);
    }

    IEnumerator SprintRunCase(string tag, string stage, int dest, int ringPolicy, bool followBoss, int deckSize = 0, bool appPause = false,
        bool preMax = false, string endWith = null, string character = "swordsman", bool quick = false)
    {
        yield return WaitHome();
        gm.SetSelectedCharacter(character);
        GameManager.QaSprintPreMax = preMax;
        var deckF = typeof(GameManager).GetField("deckCards", BindingFlags.Instance | BindingFlags.NonPublic);
        var deck = (List<string>)deckF.GetValue(gm);
        var deckBackup = new List<string>(deck);
        if (deckSize > 0) { var keep = deck.Take(deckSize).ToList(); deck.Clear(); deck.AddRange(keep); }
        int deckCount = deck.Count;
        SprintRunner.QaRingPolicy = ringPolicy;
        SprintRunner.QaTimeScale = 3f;
        bool cleared10Before = SprintRecords.IsGateCleared(stage, 10);
        double lifeBefore = ProgressStats.LifetimeDistance;
        int levelUps = 0, ringChoicesSeen = 0;
        pc = PlayerController.Instance;
        bool ok = gm.DepartSprint(stage, dest);
        Check(ok, $"{tag}: sprint departure accepted ({stage} {dest}m)");
        if (!ok) { deck.Clear(); deck.AddRange(deckBackup); yield break; }
        float w = 0f;
        while (SprintRunner.Instance == null && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        var r = SprintRunner.Instance;
        Check(r != null && gm.CountdownActive && gm.SprintActive, $"{tag}: sprint overlay starts, the run itself stays frozen");
        if (r == null) { deck.Clear(); deck.AddRange(deckBackup); yield break; }
        Shot($"sprint_{tag}_running");
        float d0 = gm.MaxDistance;
        int choiceFrames = 0, choiceAdvance = 0, enemies = 0, bossFrames = 0;
        float lastT = r.Elapsed, frozenWhilePaused = -1f;
        bool pausedOnce = false, ringShot = false, burstShot = false, introShot = false;
        float ringAlignErr = -1f, charH = -1f;
        GameManager.QaSprintPreMax = false; // (出発の時だけ)
        w = 0f;
        while (!r.Done && w < 240f)
        {
            if (gm.SprintChoiceOpen)
            {
                choiceFrames++;
                if (choiceFrames > 1 && r.Elapsed > lastT + 0.001f) { choiceAdvance++; L($"[{tag}] advanced while choosing: dt={r.Elapsed - lastT:F3} frame={choiceFrames} seqRunning={gm.IsRewardSequenceRunning} waiting={FindFirstObjectByType<RewardCardSequence>()?.IsWaitingForSelection}"); } // 開いたフレーム自体(その前に進んだ分)は数えない
                if (choiceFrames == 30) { ringChoicesSeen++; Shot($"sprint_{tag}_ring_choice"); }
                var seq = FindFirstObjectByType<RewardCardSequence>();
                if (seq != null && seq.IsWaitingForSelection && choiceFrames > 40) { seq.OnCardClicked(0); yield return new WaitForSecondsRealtime(0.25f); seq.OnCardClicked(0); choiceFrames = 0; }
            }
            if (r.NextRingLead > 0f && r.NextRingLead < 0.25f && r.GatesPassed >= 3 && (!ringShot || ringAlignErr > 0.05f))
            {
                if (!ringShot) { ringShot = true; Shot($"sprint_{tag}_ring_coming"); }
                // リングの中心とキャラの胴体の中心(表示の 80%)。2026-10-10: キャラが段を移っている途中は別の段の高さになるので、
                // リングが来るまでの間でいちばん合った値をとる(ボットはリングの段へ移ってから通る)
                var cr = r.LastCharRect; var rr = r.LastRingRect;
                float torso = cr.yMax - cr.height * SprintTuning.I.torsoFrac;
                float e = Mathf.Abs(rr.center.y - torso) / Mathf.Max(1f, cr.height);
                ringAlignErr = ringAlignErr < 0f ? e : Mathf.Min(ringAlignErr, e);
                charH = r.CharHeightPx / (Screen.height / 1080f);
            }
            if (!burstShot && r.RingsSucceeded > 0 && !gm.SprintChoiceOpen) { burstShot = true; yield return new WaitForSecondsRealtime(0.12f); Shot($"sprint_{tag}_ring_burst"); }
            if (!introShot && r.Elapsed > 0.3f) { introShot = true; Shot($"sprint_{tag}_intro"); if (Arg("-qaSprintVideo", "0") == "1") StartCoroutine(RecordUltimate($"sprint_{tag}", 3f)); } // 走りのコマの大きさの確認用(2026-10-09)
            if (!gm.SprintChoiceOpen) choiceFrames = 0; // 閉じたら数え直す(次の選択が開いた最初のフレームを数えない)
            if (appPause && !pausedOnce && r.Elapsed > r.TotalSeconds * 0.4f)
            {
                pausedOnce = true;
                r.SendMessage("OnApplicationPause", true);
                float t0 = r.Elapsed; yield return new WaitForSecondsRealtime(1.5f); frozenWhilePaused = r.Elapsed - t0;
                r.SendMessage("OnApplicationPause", false);
            }
            enemies = Mathf.Max(enemies, FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Count(e => e.isActiveAndEnabled));
            if (BossManager.Instance != null && BossManager.Instance.IsBossPhase) bossFrames++;
            lastT = r.Elapsed;
            yield return null; w += Time.unscaledDeltaTime;
        }
        int grants = gm.SprintAutoGrants, noCand = gm.SprintAutoSkippedNoCandidate;
        sprintWorstFrame = 0f; StartCoroutine(TrackSprintWorstFrame(4f)); // 到着の瞬間からの一番長いフレーム(2026-10-10)
        // 到着の補間(疾走のキャラ → 実際のキャラ)の途中を撮る
        { float wo = 0f; while (r.OutroProgress < 0.45f && SprintRunner.Instance == r && wo < 3f) { yield return null; wo += Time.unscaledDeltaTime; } }
        if (!quick || tag == "K_swordsman") Shot($"sprint_{tag}_outro");
        { float wo = 0f; while (SprintRunner.Instance != null && wo < 3f) { yield return null; wo += Time.unscaledDeltaTime; } }
        Check(r.OutroFrames >= 5 && SprintRunner.Instance == null, $"{tag}: a short blend back to the normal run is drawn, then the overlay is gone ({r.OutroFrames} frames)");
        // リング報酬: 成功1回につきカードか MILE のどちらか1回だけ
        L($"[{tag}] ring rewards: success {r.RingsSucceeded} = card {r.RingCardRewards} + MILE {r.RingMileRewards} + none {r.RingsNoCandidate}; ring MILE {gm.RunRingMile} (x{SprintTuning.I.ringMileReward}); allMaxed start={r.AllMaxedAtStart} notices={r.AllMaxedNotices}; feed 'no card' rows {r.FeedRowsNoCandidate}; pool [{gm.SprintLastPoolDetail}]; charH {charH:F0}/1080 ringAlign {ringAlignErr:F3}");
        Check(r.RingCardRewards + r.RingMileRewards + r.RingsNoCandidate == r.RingsSucceeded, $"{tag}: each passed ring gives exactly one reward (card or MILE)");
        Check(r.RingCardRewards == gm.SprintRingPicks && gm.RunRingMile == r.RingMileRewards * SprintTuning.I.ringMileReward && gm.SprintRingMileRewards == r.RingMileRewards, $"{tag}: reward counters agree (picks {gm.SprintRingPicks}, MILE {gm.RunRingMile})");
        Check(r.FeedRowsNoCandidate == 0, $"{tag}: 'no card' is not stacked on the right ({r.FeedRowsNoCandidate} rows)");
        Check(r.RingsNoCandidate == 0, $"{tag}: no ring ended without a reward (the empty pool was always the all-Lv9 case)");
        if (charH > 0f && ringPolicy == 1) Check(Mathf.Abs(charH - 260f * SprintTuning.I.charScale) < 1f && ringAlignErr >= 0f && ringAlignErr < 0.05f, $"{tag}: sprint character at {SprintTuning.I.charScale:P0} ({charH:F0}px/1080) and the ring is centred on the torso (err {ringAlignErr:F3} of height)");
        Check(r.AllMaxedNotices <= 1, $"{tag}: the all-Lv9 notice is shown at most once ({r.AllMaxedNotices})");
        int expectGates = Mathf.FloorToInt((dest - SprintTuning.I.arriveBeforeMeters) / 1000f);
        L($"[{tag}] {stage} -> {dest}m: grants {grants} (+no candidate {noCand}) / expected {expectGates}, rings {r.RingsSucceeded}/{r.RingsTotal} missed {r.RingsMissed} choices {gm.SprintRingPicks}, sprint {r.Elapsed:F1}s (x{SprintRunner.QaTimeScale} = {r.Elapsed / SprintRunner.QaTimeScale:F1}s real) paused {r.PausedSeconds:F1}s, enemies {enemies}, boss frames {bossFrames}, deck {deckCount}");
        Check(grants + noCand == expectGates, $"{tag}: auto grants = skipped boss gates ({grants}+{noCand} = {expectGates}; the destination's boss is not pre-granted)");
        Check(enemies == 0 && bossFrames == 0, $"{tag}: no enemies / boss fights during the sprint");
        int expectRings = Mathf.FloorToInt((dest - SprintTuning.I.arriveBeforeMeters - 1f) / SprintTuning.I.ringEveryMeters);
        if (ringPolicy == 1) Check(r.RingsSucceeded == expectRings && gm.SprintRingPicks + gm.SprintRingMileRewards == expectRings, $"{tag}: every ring passed -> one reward each (choices {gm.SprintRingPicks} + MILE {gm.SprintRingMileRewards} / {expectRings})");
        if (ringPolicy == 0) Check(r.RingsSucceeded == 0 && gm.SprintRingPicks == 0 && r.RingsMissed == expectRings, $"{tag}: all rings missed -> no extra choice, still arrives");
        if (ringPolicy == 1) Check(choiceAdvance == 0, $"{tag}: the sprint does not advance while a card choice is open ({choiceAdvance} frames)");
        if (appPause) Check(frozenWhilePaused >= 0f && frozenWhilePaused < 0.05f, $"{tag}: the sprint does not advance while the app is in the background ({frozenWhilePaused:F2}s)");
        if (deckSize > 0)
        {
            var hist = (List<CardDefinition>)typeof(GameManager).GetField("upgradeHistory", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(gm);
            int maxLv = hist.Select(c => gm.GetCurrentRunStack(c.cardId)).DefaultIfEmpty(0).Max();
            Check(maxLv <= GameManager.MaxRunCardLevel && noCand > 0, $"{tag}: with a {deckSize}-card deck the cards stop at Lv{GameManager.MaxRunCardLevel} (max {maxLv}) and the rest grant nothing ({noCand})");
        }

        // 到着(2026-10-10: ボタン/3-2-1 なしで自動で走り出す。到着から走り出すまでの時間と、いちばん長いフレームを測る)
        w = 0f; float dAtGo = -1f;
        while (gm.ResumeGate != GameManager.ResumeGatePhase.None || SprintRunner.Instance != null || !gm.HasStarted)
        {
            yield return null; w += Time.unscaledDeltaTime;
            if (w > 10f) break;
        }
        float worstFrame = sprintWorstFrame;
        dAtGo = gm.MaxDistance;
        float goDelay = w;
        float arrival = dest - SprintTuning.I.arriveBeforeMeters;
        var bm = BossManager.Instance;
        float kmh = pc.CurrentAutoRunSpeed * GameManager.KmhPerMps;
        float natural = pc.NaturalMultiplierAt(arrival);
        var data = RunCheckpoint.Load();
        L($"[{tag}] arrival: d={gm.MaxDistance:F0} (exp {arrival:F0}) gate={bm.NextBossDistance:F0} gateK={bm.NextGateIndex} speed={kmh:F0}km/h naturalMul={natural:F2} Lv={gm.Level} exp={gm.Exp:F0} skipped={gm.SprintSkippedMeters:F0} mileDist={typeof(GameManager).GetProperty("MileDistanceForRun", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(gm)} checkpoint={data.checkpointDistance:F0} history={data.upgradeHistoryCardIds.Count} lifetime +{ProgressStats.LifetimeDistance - lifeBefore:F0} fixedPits={gm.LastResumeFixedPits} safeAhead={gm.LastResumeSafeAheadMeters:F0}m");
        Shot($"sprint_{tag}_arrival");
        L($"[{tag}] go: resumed {goDelay:F2}s after the overlay ended its run, worst frame {worstFrame * 1000f:F0}ms, d at go {dAtGo:F0}");
        Check(Mathf.Abs(dAtGo - arrival) < 3f && Mathf.Abs(bm.NextBossDistance - dest) < 1f, $"{tag}: arrives {SprintTuning.I.arriveBeforeMeters:F0}m before the {dest}m gate, next gate = {dest}m (d at go {dAtGo:F0})");
        Check(gm.ResumeGate == GameManager.ResumeGatePhase.None && goDelay < 1.5f, $"{tag}: runs on by itself after the arrival (no button / 3-2-1; {goDelay:F2}s)");
        Check(worstFrame < 0.25f, $"{tag}: no long stall at the arrival (worst frame {worstFrame * 1000f:F0}ms)");
        Check(gm.Level == 1, $"{tag}: skipped distance gives no distance EXP / level-ups (Lv{gm.Level})");
        Check(Mathf.Abs(data.checkpointDistance - arrival) < 1f && data.upgradeHistoryCardIds.Count >= grants && Mathf.Abs(data.sprintSkippedMeters - arrival) < 1f, $"{tag}: CONTINUE data saved once at the arrival (history {data.upgradeHistoryCardIds.Count})");
        Check(data.runRingMile == gm.RunRingMile, $"{tag}: the ring MILE is in the CONTINUE data once ({data.runRingMile} / {gm.RunRingMile})");
        if (tag == "H")
            Check(noCand > 0 && r.RingMileRewards > 0 && r.AllMaxedNotices == 1 && !r.AllMaxedAtStart, $"H: with a 10-card deck to 90km the cards run out because every deck card reaches run Lv9 ({gm.SprintLastPoolDetail}); later rings give MILE");
        if (tag == "I") Check(r.AllMaxedAtStart && r.RingMileRewards == r.RingsSucceeded && gm.SprintRingPicks == 0 && r.RingsSucceeded > 0 && r.PausedSeconds < 0.5f, $"I: all Lv9 from the start -> every ring is MILE, no card choice, no stop (paused {r.PausedSeconds:F1}s)");
        if (tag == "J") Check(r.RingCardRewards >= 1 && r.RingMileRewards >= 1 && r.AllMaxedNotices == 1 && !r.AllMaxedAtStart, $"J: card first, then one notice, then MILE (card {r.RingCardRewards}, MILE {r.RingMileRewards})");
        if (quick)
        {
            Check(r.RingsSucceeded == r.RingsTotal && r.RingsTotal > 0, $"{tag}: the ring is passed with the {character} sprint sprite");
            SprintRunner.QaRingPolicy = -1; SprintRunner.QaTimeScale = 1f;
            deck.Clear(); deck.AddRange(deckBackup);
            yield return EndRun();
            yield break;
        }
        Check(ProgressStats.LifetimeDistance - lifeBefore < 1.0, $"{tag}: lifetime distance not credited for the sprint");
        Check(SprintRecords.IsGateCleared(stage, 10) == cleared10Before, $"{tag}: no gatekeeper record from skipped gates");
        Check(kmh > 1f && !gm.SprintActive, $"{tag}: normal run speed at arrival ({kmh:F0}km/h)");

        // 走る → 目的地の関門のボス
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        int hits0 = HitCountQa();
        bool falling = false; float minY = 999f; w = 0f;
        // 再開地点の安全な足場は「到着時の速さで約2秒」(中断再開と同じ)。その範囲で落ちない/当たらないことを見る
        while (w < 1.8f) { minY = Mathf.Min(minY, pc.transform.position.y); if (pc.transform.position.y < -6f) falling = true; yield return null; w += Time.deltaTime; }
        Check(!falling && HitCountQa() == hits0, $"{tag}: safe start after GO (no fall, no hit; minY {minY:F1})");
        if (followBoss)
        {
            w = 0f;
            while (!(bm.IsBossPhase && bm.AliveBossCount > 0) && w < 40f) { yield return null; w += Time.deltaTime; }
            L($"[{tag}] boss at the destination: {bm.CurrentEncounterKey} {bm.LastRematchDecision} alive={bm.AliveBossCount} d={gm.MaxDistance:F0}");
            Check(bm.IsBossPhase && Mathf.Abs(gm.MaxDistance - dest) < 2f, $"{tag}: the {dest}m gate boss appears normally ({bm.CurrentEncounterKey})");
            Shot($"sprint_{tag}_boss");
            float mileDist = (float)typeof(GameManager).GetProperty("MileDistanceForRun", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(gm);
            Check(mileDist < 400f, $"{tag}: the distance MILE only counts what was actually run ({mileDist:F0}m)");
        }
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        SprintRunner.QaRingPolicy = -1; SprintRunner.QaTimeScale = 1f;
        // MILE の持ち帰り: FINISH(脱出と同じ Win)で RunMile ごと財布へ / GAME OVER で失う(既存のルールのまま)
        if (endWith != null)
        {
            int wallet0 = gm.TotalOwnedMile, runMile = gm.RunMile, ringMile = gm.RunRingMile;
            if (endWith == "win") gm.Win();
            else
            {
                stopKeepAlive = true;
                for (int i = 0; i < 60 && !gm.IsGameOver; i++) { SetPrivate(pc, "hitInvincibleTimer", 0f); gm.TryDamagePlayer(false, "qa-sprint", CombatScale.PlayerHeavyHit); yield return new WaitForSecondsRealtime(0.05f); }
            }
            yield return new WaitForSecondsRealtime(0.5f);
            int gained = gm.TotalOwnedMile - wallet0;
            L($"[{tag}] end by {endWith}: wallet +{gained} (run MILE {runMile} incl. ring {ringMile}, at end {gm.RunMile})");
            if (endWith == "win") Check(ringMile > 0 && gained == gm.RunMile && gm.RunMile >= ringMile, $"{tag}: FINISH banks the run MILE including the ring MILE (+{gained})");
            else Check(gm.IsGameOver && gained == 0 && ringMile > 0, $"{tag}: GAME OVER loses the ring MILE with the rest of the run MILE (wallet +{gained}, ring MILE was {ringMile})");
        }
        deck.Clear(); deck.AddRange(deckBackup);
        yield return EndRun();
    }

    int HitCountQa() => FreezeDiagnosticsHits;
    static int FreezeDiagnosticsHits;
}
#endif
