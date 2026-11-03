#if UNITY_EDITOR || DEVELOPMENT_BUILD || OMM_RELEASE_PROBE
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// 初回プレイから帰還までの自動確認(2026-11-03、ct422 を基準にした確認の依頼)。ReleaseSpawnProbe と同じく、製品版と同じ中身の
// 確認用ビルド(OMM_RELEASE_PROBE、製品名「One More Mile Test」で本物の保存と別)でも動く。起動を3回に分けて、アプリの強制終了と再起動を挟む。
//   起動引数: -runProbe <出力フォルダ> -runProbePhase 1|2|3   → <出力フォルダ>/run_probe_<phase>.txt(PASS/FAIL、最後に DONE)
//   1: 新しい保存で荒野街道を通常の出発 → 1,000m までの雑魚 → 1,000m のボスを倒す → 報酬(チェックポイント)→ BONUS ZONE(抽選の当たりだけ固定)→
//      被弾 → ホームへ戻る → キャラカードを変える → CONTINUE(HP/最大HP/カード/能力の照合)→ もう一度被弾 → 裏へ回す(OnApplicationPause)→
//      「KILL」と書いて待つ(外から強制終了)
//   2: 起動 → キャラカードを外す → CONTINUE(強制終了の前の値と照合)→ 脱出 → 結果が脱出成功・持ち帰り MILE が1回だけ所持へ入る → ホームでも増えない
//   3: 起動 → MILE/最高距離が残っていて、中断中のランは無い(もう一度入らない)
// 移動の間は HP を満タンに保ち、自動操作補助を 0km/h から効かせる(被弾の確認の間は保たない)。ボスへのダメージは攻撃の命中と同じ入口(TakeDamage)を小分けに当てる
public class ReleaseRunProbe : MonoBehaviour
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    string dir, outPath;
    int phase, fails;
    readonly List<string> lines = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var a = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(a, "-runProbe");
        if (i < 0 || i + 1 >= a.Length) return;
        Application.runInBackground = true;
        var go = new GameObject("[ReleaseRunProbe]");
        DontDestroyOnLoad(go);
        var p = go.AddComponent<ReleaseRunProbe>();
        int pi = System.Array.IndexOf(a, "-runProbePhase");
        p.phase = pi >= 0 && pi + 1 < a.Length ? int.Parse(a[pi + 1]) : 1;
        p.dir = a[i + 1];
        GameManager.ProbeLegacyContinue = System.Array.IndexOf(a, "-probeLegacy") >= 0;
        System.IO.Directory.CreateDirectory(p.dir);
        p.outPath = System.IO.Path.Combine(p.dir, $"run_probe_{p.phase}.txt");
        p.StartCoroutine(p.AnswerGuides());
        p.StartCoroutine(p.Run());
    }

    void L(string s) { lines.Add(s); Debug.Log("[RunProbe] " + s); System.IO.File.WriteAllLines(outPath + ".part", lines); }
    void Check(bool ok, string what) { if (!ok) fails++; L((ok ? "[PASS] " : "[FAIL] ") + what); }
    IEnumerator WaitFor(System.Func<bool> cond, float sec) { float t = 0f; while (!cond() && t < sec) { yield return null; t += Time.unscaledDeltaTime; } }

    static GameManager Gm => GameManager.Instance;
    static PlayerController Pc => PlayerController.Instance;
    // 移動の間は HP を満タンに保つ(無敵の設定は使わない: 使うとデバッグ機能の使用としてランの記録(最高距離など)が付かず、記録の確認ができない)
    static bool guard;
    static void SetInvincible(bool on) => guard = on;
    void Update()
    {
        var g = Gm;
        if (guard && g != null && g.HasStarted && !g.IsGameOver && g.Lives < g.maxLives) typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(g, new object[] { g.maxLives });
    }

    // 初回の案内(脱出の説明など、時間が止まる)と、レベルアップ/ボス報酬の3択(左のカード)は、押したのと同じにする
    public int Picks;
    IEnumerator AnswerGuides()
    {
        while (true)
        {
            if (FirstRunGuide.Open && FirstRunGuide.Instance != null)
            {
                L($"  guide answered: {FirstRunGuide.Showing}");
                typeof(FirstRunGuide).GetMethod("DebugAnswer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Invoke(FirstRunGuide.Instance, new object[] { 0 });
            }
            var g = Gm;
            if (g != null && g.IsRewardSequenceWaitingForSelection)
            {
                var seq = FindFirstObjectByType<RewardCardSequence>();
                if (seq != null)
                {
                    seq.OnCardClicked(0);
                    yield return new WaitForSecondsRealtime(0.25f);
                    seq.OnCardClicked(0);
                    Picks++;
                    yield return new WaitForSecondsRealtime(0.3f);
                    continue;
                }
            }
            yield return null;
        }
    }

    IEnumerator Home()
    {
        yield return WaitFor(() => Gm != null && !Gm.HasStarted && TutorialLauncher.Instance != null && !TutorialLauncher.Instance.Launching && !TutorialMode.Active && !TutorialMode.PendingStart, 60f);
        yield return new WaitForSecondsRealtime(1.5f);
    }

    // 今の状態(HP/最大HP/取得カード/キャラカード/能力)
    static List<string> History() => ((List<CardDefinition>)typeof(GameManager).GetField("upgradeHistory", NP).GetValue(Gm)).Select(c => c.cardId).ToList();
    static string CharCards()
    {
        var ids = (string[])typeof(GameManager).GetField("characterCardIds", NP).GetValue(Gm);
        var lv = (int[])typeof(GameManager).GetField("characterCardLevels", NP).GetValue(Gm);
        return string.Join(",", ids.Select((id, k) => string.IsNullOrEmpty(id) ? "-" : $"{id}:{lv[k]}"));
    }
    static string Abilities() => $"atk={Pc.EffectiveAttackPower} jumps={Pc.maxJumps} shield={Pc.ShieldCapacity} sealed={Gm.SealedHearts} lv={Gm.Level}";
    struct Snap { public int hp, max; public string cards, abil; public override string ToString() => $"HP {hp}/{max} cards=[{cards}] {abil}"; }
    static Snap Now() => new Snap { hp = Gm.Lives, max = Gm.maxLives, cards = string.Join(",", History()), abil = Abilities() };
    static Snap FromCp(RunCheckpoint.Data d) => new Snap { hp = d.lives, max = d.maxLives, cards = string.Join(",", d.upgradeHistoryCardIds), abil = "-" };

    // 被弾(無敵を切って、敵の攻撃と同じ入口で1発)。被弾後の無敵の間は次が入らないので待つ
    IEnumerator Hit(string why)
    {
        SetInvincible(false);
        int before = Gm.Lives;
        float t = 0f;
        while (Gm.Lives == before && t < 6f)
        {
            Gm.TryDamagePlayer(false, "RunProbe:" + why);
            yield return new WaitForSecondsRealtime(0.2f); t += 0.2f;
        }
        L($"  hit ({why}): HP {before} -> {Gm.Lives}/{Gm.maxLives}");
    }

    IEnumerator Continue()
    {
        yield return Home();
        Check(RunCheckpoint.HasActiveRun && Gm.HomeShowsContinueChoice, "home offers CONTINUE");
        Gm.ContinueActiveRun();
        yield return WaitFor(() => Gm.HasStarted, 20f);
        yield return null; yield return null;
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);
        NoPits();
    }
    // 無敵でも穴に落ちるとゲームオーバー(落下は無敵を通す)。この確認は穴が対象ではないので、ここから先の生成だけ穴を出さない
    // 先に生成済みの穴もあるので、自動操作補助を 0km/h から効かせる(production の最終試験の ProdVerifyPilot と同じ値)
    static void NoPits()
    {
        var tm = TerrainManager.Instance; if (tm != null) { tm.pitChanceBase = 0f; tm.pitChanceMax = 0f; }
        var a = HighSpeedAssist.Instance; if (a == null) return;
        a.engageKmh = 0f; a.releaseKmh = -1f; a.fullAssistKmh = 1f;
    }

    static List<MonoBehaviour> AliveBosses()
    {
        var r = new List<MonoBehaviour>();
        r.AddRange(FindObjectsByType<WildBossBase>(FindObjectsSortMode.None).Where(b => b != null && !b.IsDead && b.isActiveAndEnabled));
        r.AddRange(FindObjectsByType<DragonController>(FindObjectsSortMode.None).Where(b => b != null && !b.IsDead));
        r.AddRange(FindObjectsByType<MajinController>(FindObjectsSortMode.None).Where(b => b != null && !b.IsDead));
        return r;
    }
    static void HitBoss(MonoBehaviour b)
    {
        if (b is WildBossBase w) w.TakeDamage(Mathf.Max(1, w.maxHp / 12), w.CenterWorld);
        else if (b is DragonController d) d.TakeDamage(Mathf.Max(1, d.maxHp / 12));
        else if (b is MajinController m) m.TakeDamage(Mathf.Max(1, m.maxHp / 12));
    }

    string StateFile => System.IO.Path.Combine(dir, "state.txt");
    void SaveState(Dictionary<string, string> kv) => System.IO.File.WriteAllLines(StateFile, kv.Select(e => e.Key + "=" + e.Value));
    Dictionary<string, string> LoadState() => System.IO.File.Exists(StateFile) ? System.IO.File.ReadAllLines(StateFile).Where(s => s.Contains("=")).ToDictionary(s => s.Substring(0, s.IndexOf('=')), s => s.Substring(s.IndexOf('=') + 1)) : new Dictionary<string, string>();

    IEnumerator Run()
    {
        L($"start phase={phase} legacyContinue={GameManager.ProbeLegacyContinue} debugBuild={Debug.isDebugBuild} devGate={DevGate.On} product={Application.productName} version={Application.version} platform={Application.platform}");
        yield return Home();
        if (phase == 1) yield return Phase1();
        else if (phase == 2) yield return Phase2();
        else yield return Phase3();
        L($"failures={fails}");
        L("DONE");
        System.IO.File.WriteAllLines(outPath, lines);
        if (phase == 1) { L("KILL"); System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "kill_me.txt"), "now"); yield break; } // 外から強制終了される
        yield return new WaitForSecondsRealtime(1f);
        Application.Quit();
    }

    IEnumerator Phase1()
    {
        var gm = Gm;
        Check(gm.TotalOwnedMile == 0 && gm.BestDistance < 1f && !RunCheckpoint.HasActiveRun, $"fresh save (MILE {gm.TotalOwnedMile}, best {gm.BestDistance:F0}m, suspended {RunCheckpoint.HasActiveRun})");
        int mile0 = gm.TotalOwnedMile;

        // 1. 通常の出発(ステージ選択の「出発」と同じ StartGame)
        gm.SetSelectedCharacter("swordsman");
        gm.SetSelectedStage("wasteland_road");
        float w = 0f, retry = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 25f)
        {
            if (!gm.HasStarted && retry <= 0f) { typeof(GameManager).GetMethod("StartGame", NP).Invoke(gm, null); retry = 0.6f; }
            yield return null; w += Time.unscaledDeltaTime; retry -= Time.unscaledDeltaTime;
        }
        SetInvincible(true);
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);
        NoPits();
        float firstZakoAt = -1f; int maxZako = 0;
        var bz = BonusZone.Instance;
        while (gm.MaxDistance < 990f && !gm.IsGameOver)
        {
            if (firstZakoAt < 0f && EncounterDirector.Instance != null && EncounterDirector.Instance.SpawnedEnemies > 0 && FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Any(e => e.isActiveAndEnabled && e.GetComponent<BonusEnemy>() == null))
                firstZakoAt = gm.MaxDistance;
            maxZako = Mathf.Max(maxZako, EncounterDirector.Instance != null ? EncounterDirector.Instance.SpawnedEnemies : 0);
            yield return null;
        }
        L($"run to 990m: first zako at {firstZakoAt:F0}m, zako placed {maxZako}, level {gm.Level}, picks {Picks}, HP {gm.Lives}/{gm.maxLives}");
        Check(firstZakoAt >= 0f && firstZakoAt < 1000f, $"normal enemies appear before 1,000m (first at {firstZakoAt:F0}m, {maxZako} placed)");

        // 2. 1,000m のボス(攻撃の命中と同じ入口で小分けに当てる)→ 報酬の3択 → チェックポイント
        var bm = BossManager.Instance;
        yield return WaitFor(() => bm.IsBossPhase && AliveBosses().Count > 0, 60f);
        string bossNames = string.Join(",", AliveBosses().Select(b => b.GetType().Name));
        Check(bm.IsBossPhase && AliveBosses().Count > 0, $"the first boss appears at {gm.MaxDistance:F0}m ({bossNames})");
        // BONUS ZONE の当たり/外れの乱数だけ当たりに固定(ボスの後の抽選。条件の判定は本物)
        var rngF = typeof(BonusZone).GetField("rng", BindingFlags.NonPublic | BindingFlags.Static);
        int seed = 0; while (new System.Random(seed).NextDouble() >= 0.4) seed++;
        rngF.SetValue(null, new System.Random(seed));
        float bt = 0f;
        while (AliveBosses().Count > 0 && bt < 120f)
        {
            foreach (var b in AliveBosses()) HitBoss(b);
            yield return new WaitForSecondsRealtime(0.35f); bt += 0.35f;
        }
        int picks0 = Picks;
        yield return WaitFor(() => gm.EscapeAvailable && !bm.IsBossPhase && Picks > picks0, 60f);
        var cp = RunCheckpoint.Load();
        var atCp = Now(); // チェックポイント(ボス報酬の直後)の能力。再開はここへそろう(この後の獲得は取り消し)
        L($"boss defeated in {bt:F1}s: bossKills {gm.BossKillCount}, escape {gm.EscapeAvailable}, checkpoint {cp.checkpointDistance:F0}m runAtCheckpoint {cp.runAtCheckpoint} cp {FromCp(cp)}");
        Check(gm.BossKillCount >= 1 && gm.EscapeAvailable && cp.active && cp.checkpointDistance >= 990f, "the boss is defeated, escape opens and the checkpoint moves to the boss");

        // 3. BONUS ZONE: ボスの後の抽選
        L($"bonus: roll at {bz.PendingStartDistance:F0}m (now {gm.MaxDistance:F0}m, state {bz.State})");
        yield return WaitFor(() => bz.State != BonusZone.Phase.Idle, 40f);
        int bonusSeen = 0; float zt = 0f;
        while (bz.State != BonusZone.Phase.Idle && zt < 60f) { bonusSeen = Mathf.Max(bonusSeen, FindObjectsByType<BonusEnemy>(FindObjectsSortMode.None).Count(e => e != null && e.isActiveAndEnabled)); yield return null; zt += Time.unscaledDeltaTime; }
        L($"bonus zone: started {bz.ZonesStarted} type {bz.Current?.id} enemies {bonusSeen} MILE +{bz.BonusMile} ({zt:F1}s, now {gm.MaxDistance:F0}m)");
        Check(bz.ZonesStarted > 0 && bonusSeen > 0, $"bonus zone enemies appear after the boss ({bonusSeen})");

        // 4. 被弾 → ホームへ戻る(ポーズの「ホームへ戻る」と同じ)→ キャラカードを変える → CONTINUE
        yield return Hit("1"); yield return new WaitForSecondsRealtime(1.5f); yield return Hit("2");
        var atInterrupt = Now();
        cp = RunCheckpoint.Load();
        L($"interrupt (return home) at {gm.MaxDistance:F0}m: {atInterrupt} | saved checkpoint {FromCp(cp)} at {cp.checkpointDistance:F0}m");
        gm.ReturnToHome();
        yield return WaitFor(() => GameManager.Instance != gm, 20f);
        gm = Gm;
        yield return Home();
        CardInventory.AddCard("heart_up", 1, 3); // 専用のテスト用データだけ(製品名が別)。デッキが1枚ぶん押さえるので多めに
        bool eq = gm.EquipCharacterCard(0, "heart_up", 1);
        L($"at home: equipped heart_up Lv1 as a character card = {eq} ({CharCards()}) {gm.LastEquipMessage}");
        yield return Continue();
        var afterCont = Now();
        L($"after CONTINUE at {gm.MaxDistance:F0}m: {afterCont}");
        // 期待: カード/能力/最大HP はチェックポイントの時点(その後の獲得は取り消し = 画面の説明どおり)、HP は中断の時点(最大HPで切る)
        int wantHp = Mathf.Min(atInterrupt.hp, cp.maxLives);
        Check(afterCont.hp == wantHp, $"A CONTINUE keeps the HP of the interruption (interrupted {atInterrupt.hp}/{atInterrupt.max}, checkpoint {cp.lives}/{cp.maxLives} -> {afterCont.hp}, want {wantHp})");
        Check(afterCont.max == cp.maxLives, $"A CONTINUE uses the checkpoint's max HP even if character cards changed at home ({cp.maxLives} -> {afterCont.max})");
        Check(afterCont.cards == atCp.cards && afterCont.abil == atCp.abil, $"A CONTINUE restores the checkpoint's cards and abilities ([{atCp.cards}] {atCp.abil} -> [{afterCont.cards}] {afterCont.abil})");

        // 5. もう一度被弾 → アプリを裏へ(Android のホームボタン/電話と同じ OnApplicationPause)→ 強制終了を待つ
        yield return new WaitForSecondsRealtime(4f);
        yield return Hit("3");
        var beforeKill = Now();
        gm.SendMessage("OnApplicationPause", true);
        SaveState(new Dictionary<string, string> { ["hp"] = beforeKill.hp.ToString(), ["max"] = beforeKill.max.ToString(), ["cards"] = beforeKill.cards, ["abil"] = beforeKill.abil, ["mile0"] = mile0.ToString(), ["d"] = gm.MaxDistance.ToString("F0") });
        L($"app backgrounded at {gm.MaxDistance:F0}m: {beforeKill} -> waiting for the forced kill");
    }

    IEnumerator Phase2()
    {
        var st = LoadState();
        var gm = Gm;
        Check(st.Count > 0, "phase 1 values found");
        var cp = RunCheckpoint.Load();
        L($"after restart: suspended {cp.active} checkpoint {cp.checkpointDistance:F0}m {FromCp(cp)} interruptLives {cp.interruptLives} MILE {gm.TotalOwnedMile}");
        bool eq = gm.EquipCharacterCard(0, null, 1);
        L($"at home: removed the character card = {eq} ({CharCards()})");
        yield return Continue();
        var s = Now();
        L($"after CONTINUE (after the kill) at {gm.MaxDistance:F0}m: {s}");
        Check(s.hp.ToString() == st["hp"], $"B CONTINUE after a forced kill keeps the HP ({st["hp"]} -> {s.hp})");
        Check(s.max.ToString() == st["max"], $"B CONTINUE after a forced kill keeps the max HP ({st["max"]} -> {s.max})");
        Check(s.cards == st["cards"] && s.abil == st["abil"], $"B CONTINUE after a forced kill keeps the cards and abilities ([{st["cards"]}] {st["abil"]} -> [{s.cards}] {s.abil})");
        SetInvincible(true);

        // 脱出(長押しの完了と同じ: TryConfirmEscape → 演出)
        yield return WaitFor(() => gm.EscapeAvailable && Pc != null, 20f);
        yield return new WaitForSecondsRealtime(3f);
        int mileBefore = gm.TotalOwnedMile;
        bool ok = gm.TryConfirmEscape("hold");
        if (ok) Pc.StartCoroutine((IEnumerator)typeof(PlayerController).GetMethod("DoFinishSequence", NP).Invoke(Pc, null));
        yield return WaitFor(() => gm.IsGameOver, 40f);
        yield return new WaitForSecondsRealtime(3f);
        int runMile = gm.RunMile, mileAfter = gm.TotalOwnedMile;
        L($"escape: confirmed {ok} over {gm.IsGameOver} win {gm.IsWin} reason {gm.DeathReason} d {gm.MaxDistance:F0}m RESULT MILE {runMile} (take-home) owned {mileBefore} -> {mileAfter} best {gm.BestDistance:F0}m suspended {RunCheckpoint.HasActiveRun}");
        Check(ok && gm.IsGameOver && gm.IsWin, "the result is an escape success");
        Check(runMile > 0 && mileAfter - mileBefore == runMile, $"the take-home MILE is added once ({mileBefore} + {runMile} = {mileAfter})");
        Check(!RunCheckpoint.HasActiveRun, "the suspended run is cleared after the escape");
        Check(gm.BestDistance >= 1000f, $"the escape is recorded (best {gm.BestDistance:F0}m)");
        // ホームへ(結果の「ホームへ」と同じ読み直し)。戻っても増えない
        gm.Retry();
        yield return WaitFor(() => GameManager.Instance != gm, 20f);
        yield return Home();
        Check(Gm.TotalOwnedMile == mileAfter, $"returning home does not add it again ({Gm.TotalOwnedMile})");
        st["mileAfter"] = mileAfter.ToString(); st["best"] = gm.BestDistance.ToString("F0"); st["runMile"] = runMile.ToString();
        SaveState(st);
        SaveStore.Save();
    }

    IEnumerator Phase3()
    {
        var st = LoadState();
        var gm = Gm;
        yield return new WaitForSecondsRealtime(2f);
        L($"after restart: MILE {gm.TotalOwnedMile} best {gm.BestDistance:F0}m suspended {RunCheckpoint.HasActiveRun}");
        Check(st.ContainsKey("mileAfter") && gm.TotalOwnedMile.ToString() == st["mileAfter"], $"the MILE stays after the restart ({(st.ContainsKey("mileAfter") ? st["mileAfter"] : "?")} -> {gm.TotalOwnedMile})");
        Check(st.ContainsKey("mile0") && gm.TotalOwnedMile - int.Parse(st["mile0"]) == int.Parse(st.ContainsKey("runMile") ? st["runMile"] : "-1"), "the MILE grew by the result's take-home MILE exactly once over the whole flow");
        Check(st.ContainsKey("best") && Mathf.Abs(gm.BestDistance - float.Parse(st["best"])) < 1f, $"the best distance stays ({gm.BestDistance:F0}m)");
        Check(!RunCheckpoint.HasActiveRun && !gm.HomeShowsContinueChoice, "no CONTINUE is offered after the escape");
    }
}
#endif
