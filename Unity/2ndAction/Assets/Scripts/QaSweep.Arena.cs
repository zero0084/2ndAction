#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// 開発用の闘技場(2026-10-04)の自動テスト: -qaArena <dir> [-qaArenaOnly ABCDEFGHI] [-qaArenaShots 1]
//  A 0km/h: 位置は動かず、時間/攻撃/ジャンプ/重力/敵の行動は動く。距離は距離条件のまま、関門なし
//  B 走行: 床/背景が途切れない、速度(基準×補正 / 固定)が正しい
//  C カードの追加/解除/キャラ変更で能力が残らない
//  D 再戦: 同じ条件へ戻る(敵の数/HP/計測/弾)、直前の結果が残る
//  E 停止中は計測/戦闘が進まず、再開後は動く
//  F 倒れてもランは終わらず、すぐ再戦できる / G 無敵: 本来のダメージだけ数える
//  H ボス: 報酬の選択が出ない、距離 99km でも死神が出ない
//  I 退出: ホームへ戻り、保存/設定に試験の結果が混ざらない
public partial class QaSweep
{
    ArenaController Arena => ArenaController.Instance;

    IEnumerator ArenaMode_()
    {
        var snapSave = SaveSystem.Capture();
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        gm = GameManager.Instance;
        string only = Arg("-qaArenaOnly", "ABCDEFGHI");
        var save0 = SaveSystem.CaptureCategory(SaveCategory.Progress);
        int assistPref0 = PlayerPrefs.GetInt("HighSpeedAssistEnabled", -1);
        float best0 = gm.BestDistance;

        if (only.Contains('A')) yield return ArenaStop();
        if (only.Contains('B')) yield return ArenaRun();
        if (only.Contains('C')) yield return ArenaBuild();
        if (only.Contains('D')) yield return ArenaRematch();
        if (only.Contains('E')) yield return ArenaPause();
        if (only.Contains('F') || only.Contains('G')) yield return ArenaDeath();
        if (only.Contains('H')) yield return ArenaBoss();
        if (only.Contains('I')) yield return ArenaExit(save0, assistPref0, best0);

        SaveSystem.Restore(snapSave);
        CardInventory.ReloadFromPrefs(); CardMastery.ReloadFromPrefs();
        RunCheckpoint.Reload();
        L("[arena] test machine save restored");
    }

    IEnumerator ArenaLaunch(string tag)
    {
        EndgameDebug.LaunchArena("qa " + tag);
        yield return new WaitForSecondsRealtime(1f);
        float w = 0f;
        while ((EndgameDebug.Instance.Launching || Arena == null || !ArenaMode.BattleRunning) && w < 40f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance; pc = PlayerController.Instance;
        yield return new WaitForSeconds(0.3f);
        Check(Arena != null && ArenaMode.Active && DebugRun.IsActive, $"[{tag}] the arena starts as a DEBUG RUN");
    }

    void ArenaConfigReset()
    {
        ArenaMode.Config = new ArenaConfig();
        ArenaMode.Previous = null;
    }

    IEnumerator ArenaShot(string name)
    {
        if (Arg("-qaArenaShots", "0") != "1") yield break;
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, name + ".png"));
        yield return new WaitForSecondsRealtime(0.4f);
    }

    // ===================================================================== A
    IEnumerator ArenaStop()
    {
        L("== A: 0km/h ==");
        ArenaConfigReset();
        var c = ArenaMode.Config;
        c.speedMode = 0; c.kmh = 0f; c.distance = 30000f;
        c.enemies.Add(new ArenaEnemyEntry { kind = ArenaEnemyKind.Enemy, enemyId = "rusher_runner", tier = 2, count = 1, ahead = 14f }); // 突進してくる敵(0km/h でも敵が動くことの確認)
        yield return ArenaLaunch("A");
        float x0 = pc.transform.position.x, y0 = pc.transform.position.y, t0 = Time.time;
        var gob = FindObjectsByType<EnemyController>(FindObjectsSortMode.None).FirstOrDefault(e => !e.ArenaDummy);
        float gx0 = gob != null ? gob.transform.position.x : 0f;
        yield return new WaitForSeconds(2f);
        Check(Mathf.Abs(pc.transform.position.x - x0) < 0.5f && Time.time - t0 > 1.9f, $"A: 0km/h: the player does not auto-run but time goes on (dx {pc.transform.position.x - x0:F2}, t {Time.time - t0:F1}s)");
        Check(Mathf.Abs(gm.MaxDistance - 30000f) < 0.5f && !Bm.IsBossPhase, $"A: distance stays at the distance condition ({gm.MaxDistance:F0}), no boss gate");
        // 攻撃: 標的へ前フリック
        long d0 = ArenaMode.Current.dealt;
        for (int i = 0; i < 6; i++) { StartCoroutine(Flick(PlayerController.FlickDirection.Forward)); yield return new WaitForSeconds(0.35f); }
        Check(ArenaMode.Current.dealt > d0 && ArenaMode.Current.hits > 0, $"A: attacks hit the dummy and are measured (dealt {ArenaMode.Current.dealt}, hits {ArenaMode.Current.hits})");
        // ジャンプ/重力
        float yMax = pc.transform.position.y;
        StartCoroutine(Flick(PlayerController.FlickDirection.Up));
        float w = 0f; while (w < 1.2f) { yMax = Mathf.Max(yMax, pc.transform.position.y); yield return null; w += Time.deltaTime; }
        yield return new WaitForSeconds(1f);
        Check(yMax > y0 + 0.8f && Mathf.Abs(pc.transform.position.y - y0) < 0.3f && pc.IsGrounded, $"A: jump goes up ({yMax - y0:F2}m) and gravity brings the player back");
        bool goblinActed = gob == null || gob.IsDying || Mathf.Abs(gob.transform.position.x - gx0) > 0.05f || ArenaMode.Current.wouldHits > 0;
        Check(goblinActed, $"A: the rusher moves/acts at 0km/h (moved {(gob != null ? gob.transform.position.x - gx0 : 0f):F2}m, hits on player {ArenaMode.Current.wouldHits})");
        yield return ArenaShot("arena_stop");
    }

    // ===================================================================== B
    IEnumerator ArenaRun()
    {
        L("== B: 走行 ==");
        ArenaConfigReset();
        var c = ArenaMode.Config;
        c.speedMode = 1; c.kmh = 100f; c.enemies.Clear();
        yield return ArenaLaunch("B100");
        float factor = pc.ArenaSpeedFactor;
        float x0 = pc.transform.position.x, t0 = Time.time;
        var tm = TerrainManager.Instance; int gaps = 0; float yMin = 99f, yMax = -99f; int floorMiss = 0;
        var stage = FindFirstObjectByType<ArenaStage>();
        float w = 0f;
        while (w < 6f)
        {
            yield return null; w += Time.deltaTime;
            float px = pc.transform.position.x;
            float? g = tm.GetHeightAt(px), g2 = tm.GetHeightAt(px + 25f);
            if (!g.HasValue || !g2.HasValue) gaps++;
            yMin = Mathf.Min(yMin, pc.transform.position.y); yMax = Mathf.Max(yMax, pc.transform.position.y);
            var floorT = stage != null ? stage.transform.Find("Floor") : null;
            if (floorT == null || Mathf.Abs(floorT.position.x - Camera.main.transform.position.x) > 6f) floorMiss++;
        }
        float v = (pc.transform.position.x - x0) / (Time.time - t0);
        float want = 100f / GameManager.KmhPerMps * factor;
        L($"[B] base 100km/h x factor {factor:F2}: measured {v * GameManager.KmhPerMps:F1} km/h (want {want * GameManager.KmhPerMps:F1}), gaps {gaps}, y {yMin:F2}..{yMax:F2}, floorMiss {floorMiss}");
        Check(Mathf.Abs(v - want) < want * 0.04f, "B: base speed x character/card factor is applied");
        Check(gaps == 0 && yMax - yMin < 0.05f && floorMiss == 0, "B: the floor never ends while running (flat ground, floor visual follows the camera)");
        yield return ArenaShot("arena_run100");
        // 実効速度固定 300km/h + SPEED UP Lv9(固定なので補正は入らない) / 基準速度 + SPEED UP は速くなる
        c.speedMode = 2; c.kmh = 300f; c.build.Add(new ArenaBuildEntry { key = "speed_up", times = 9 });
        yield return ArenaLaunch("B300fixed");
        x0 = pc.transform.position.x; t0 = Time.time;
        yield return new WaitForSeconds(3f);
        v = (pc.transform.position.x - x0) / (Time.time - t0);
        Check(Mathf.Abs(v * GameManager.KmhPerMps - 300f) < 8f, $"B: fixed 300km/h ignores SPEED UP Lv9 ({v * GameManager.KmhPerMps:F1} km/h, factor {pc.ArenaSpeedFactor:F2})");
        c.speedMode = 1; c.kmh = 60f;
        yield return ArenaLaunch("B60base");
        float f2 = pc.ArenaSpeedFactor;
        x0 = pc.transform.position.x; t0 = Time.time;
        yield return new WaitForSeconds(3f);
        v = (pc.transform.position.x - x0) / (Time.time - t0);
        Check(f2 > 1.05f && Mathf.Abs(v * GameManager.KmhPerMps - 60f * f2) < 4f, $"B: base 60km/h with SPEED UP Lv9 runs faster ({v * GameManager.KmhPerMps:F1} km/h, factor {f2:F2})");
    }

    // ===================================================================== C
    IEnumerator ArenaBuild()
    {
        L("== C: ビルドの追加/解除/キャラ変更 ==");
        ArenaConfigReset();
        var c = ArenaMode.Config;
        c.enemies.Clear();
        yield return ArenaLaunch("C0");
        string Sig() => $"atk={pc.EffectiveAttackPower} as={pc.AttackSpeedMultiplier:F3} hp={gm.maxLives} jump={pc.jumpForce:F2}x{pc.maxJumps} run={pc.runSpeed:F3} range={pc.CardRangeFactor:F3}";
        string base0 = Sig();
        c.build.Add(new ArenaBuildEntry { key = "attack_up", times = 9 }); c.build.Add(new ArenaBuildEntry { key = "heart_up", times = 5 }); c.build.Add(new ArenaBuildEntry { key = "jump_count_up", times = 3 });
        yield return ArenaLaunch("C1");
        string with1 = Sig();
        c.build.Clear();
        yield return ArenaLaunch("C2");
        string base1 = Sig();
        c.character = "noble_lady"; // HP30 / 攻撃10 / ジャンプ1回(黒剣士と基本値が違う)
        yield return ArenaLaunch("C3");
        string fighter = Sig();
        c.character = "swordsman";
        c.build.Add(new ArenaBuildEntry { key = "attack_up", times = 9 }); c.build.Add(new ArenaBuildEntry { key = "heart_up", times = 5 }); c.build.Add(new ArenaBuildEntry { key = "jump_count_up", times = 3 });
        yield return ArenaLaunch("C4");
        string with2 = Sig();
        L($"[C] base {base0} | build {with1} | cleared {base1} | fighter {fighter} | build again {with2}");
        Check(with1 != base0, "C: the build changes the stats");
        Check(base1 == base0, "C: clearing the build returns exactly to the base stats (nothing left over)");
        Check(fighter != base0, "C: changing the character changes the stats");
        Check(with2 == with1, "C: the same build gives the same stats again (no stacking)");
        // 能力ごとの Lv9 上限(14回足しても9)
        c.build.Clear(); c.build.Add(new ArenaBuildEntry { key = "attack_up", times = 9 }); c.build.Add(new ArenaBuildEntry { key = CardDataMigration.LegacyToKey("attack_up", 5), times = 1, owned = true });
        yield return ArenaLaunch("C5");
        Check(gm.GetAbilityRunStack("attack_up") == 9, $"C: the Lv9 ability cap holds in the arena (attack_up Lv{gm.GetAbilityRunStack("attack_up")})");
    }

    // ===================================================================== D
    IEnumerator ArenaRematch()
    {
        L("== D: 再戦 ==");
        ArenaConfigReset();
        var c = ArenaMode.Config;
        c.enemies.Clear();
        c.enemies.Add(new ArenaEnemyEntry { kind = ArenaEnemyKind.Enemy, enemyId = "shooter_archer", tier = 2, count = 3, ahead = 10f, spacing = 3f });
        yield return ArenaLaunch("D1");
        int n1 = FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Count(e => !e.IsDying);
        for (int i = 0; i < 8; i++) { StartCoroutine(Flick(PlayerController.FlickDirection.Forward)); yield return new WaitForSeconds(0.3f); }
        yield return new WaitForSeconds(2f);
        var r1 = ArenaMode.Current;
        int hpBefore = gm.Lives;
        Arena.Rematch("qa");
        yield return new WaitForSecondsRealtime(1f);
        float w = 0f; while ((EndgameDebug.Instance.Launching || Arena == null || !ArenaMode.BattleRunning) && w < 40f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance; pc = PlayerController.Instance;
        int n2 = FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Count(e => !e.IsDying);
        int projectiles = FindObjectsByType<Rigidbody2D>(FindObjectsSortMode.None).Count(rb => rb != null && rb.GetComponent<EnemyController>() == null && rb.GetComponent<PlayerController>() == null && rb.gameObject.activeInHierarchy && rb.name.ToLower().Contains("proj"));
        L($"[D] before: enemies {n1}, dealt {r1.dealt}, hp {hpBefore}; after rematch: enemies {n2}, hp {gm.Lives}/{gm.maxLives}, metrics dealt {ArenaMode.Current.dealt} t {ArenaMode.Current.time:F2}, projectiles {projectiles}, previous dealt {ArenaMode.Previous?.dealt}");
        Check(n2 == 3 && gm.Lives == gm.maxLives && ArenaMode.Current.dealt == 0 && ArenaMode.Current.time < 1f, "D: rematch restores the same enemies, full HP and fresh metrics");
        Check(ArenaMode.Previous != null && ArenaMode.Previous.dealt == r1.dealt, "D: the previous result is kept for comparison");
        Check(projectiles == 0, "D: no projectiles remain after the rematch (scene rebuilt)");
    }

    // ===================================================================== E
    IEnumerator ArenaPause()
    {
        L("== E: 停止 ==");
        ArenaConfigReset();
        var c = ArenaMode.Config;
        c.speedMode = 1; c.kmh = 60f;
        c.enemies.Add(new ArenaEnemyEntry { kind = ArenaEnemyKind.Enemy, enemyId = "goblin", tier = 2, count = 2, ahead = 30f });
        yield return ArenaLaunch("E");
        yield return new WaitForSeconds(0.5f);
        Arena.SetPanel(true);
        yield return new WaitForSecondsRealtime(0.2f);
        float t0 = ArenaMode.Current.time, x0 = pc.transform.position.x;
        yield return new WaitForSecondsRealtime(1.5f);
        Check(Mathf.Approximately(ArenaMode.Current.time, t0) && Mathf.Abs(pc.transform.position.x - x0) < 0.01f && Time.timeScale == 0f, $"E: while the settings panel is open nothing moves and time is not measured (t {t0:F2} -> {ArenaMode.Current.time:F2})");
        yield return ArenaShot("arena_panel");
        Arena.SetPanel(false);
        yield return new WaitForSeconds(1f);
        Check(ArenaMode.Current.time > t0 + 0.8f && pc.transform.position.x > x0 + 5f && Time.timeScale > 0f, "E: after closing, the battle and measurement continue");
    }

    // ===================================================================== F / G
    IEnumerator ArenaDeath()
    {
        L("== F/G: 倒れる / 無敵 ==");
        ArenaConfigReset();
        var c = ArenaMode.Config;
        c.invincible = true;
        yield return ArenaLaunch("G");
        int hp0 = gm.Lives;
        stopKeepAlive = true;
        for (int i = 0; i < 3; i++) { SetPrivate(pc, "hitInvincibleTimer", 0f); pc.TakeDamage(false, "QaArena"); yield return new WaitForSeconds(0.1f); }
        Check(gm.Lives == hp0 && ArenaMode.Current.taken == 0 && ArenaMode.Current.wouldTake > 0, $"G: invincible: HP unchanged, the would-be damage is recorded ({ArenaMode.Current.wouldTake})");
        Arena.ToggleInvincible();
        for (int i = 0; i < 40 && !ArenaMode.Current.defeated; i++) { SetPrivate(pc, "hitInvincibleTimer", 0f); gm.TryDamagePlayer(false, "QaArena", CombatScale.PlayerHeavyHit); yield return new WaitForSecondsRealtime(0.05f); }
        yield return new WaitForSecondsRealtime(0.3f);
        Check(ArenaMode.Current.defeated && !gm.IsGameOver && gm.HasStarted && ArenaMode.Current.taken > 0, $"F: defeat ends the trial without a game over (taken {ArenaMode.Current.taken})");
        Check(Arena.PanelOpen, "F: the result panel opens after the defeat");
        yield return ArenaShot("arena_defeat");
        Arena.Rematch("qa after defeat");
        yield return new WaitForSecondsRealtime(1f);
        float w = 0f; while ((EndgameDebug.Instance.Launching || Arena == null || !ArenaMode.BattleRunning) && w < 40f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance; pc = PlayerController.Instance;
        Check(ArenaMode.Active && !ArenaMode.Current.defeated && gm.Lives == gm.maxLives && Time.timeScale > 0f, "F: rematch right after the defeat works");
        stopKeepAlive = false;
    }

    // ===================================================================== H
    IEnumerator ArenaBoss()
    {
        L("== H: ボス ==");
        ArenaConfigReset();
        var c = ArenaMode.Config;
        c.distance = 99000f; c.invincible = true;
        c.enemies.Clear(); c.enemies.Add(new ArenaEnemyEntry { kind = ArenaEnemyKind.WildBoss, bossKind = (int)WildBossKind.Wolf, count = 1 });
        yield return ArenaLaunch("H");
        float w = 0f; while (Bm.AliveBossCount <= 0 && w < 10f) { yield return null; w += Time.deltaTime; }
        yield return new WaitForSeconds(2.5f);
        var boss = WildAlive().FirstOrDefault();
        Check(boss != null, "H: the boss appears in the arena");
        yield return ArenaShot("arena_boss");
        if (boss != null) boss.TakeDamage(boss.Hp + 10, boss.CenterWorld);
        w = 0f; while ((Bm.IsBossPhase || gm.IsRewardSequenceRunning) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSeconds(1f);
        Check(!Bm.IsBossPhase && !gm.IsRewardSequenceRunning && !gm.UltimateChoiceOpen, $"H: no boss reward card choice in the arena (phase {Bm.IsBossPhase})");
        Check(!Bm.DeathSpawned && gm.MaxDistance >= 99000f, "H: at the 99km distance condition the reaper does not appear");
        Check(ArenaMode.Current.kills >= 1 && ArenaMode.Current.clearTime >= 0f, $"H: the boss kill is measured (clear {ArenaMode.Current.clearTime:F2}s)");
    }

    // ===================================================================== I
    IEnumerator ArenaExit(System.Collections.Generic.List<SaveSystem.Item> save0, int assistPref0, float best0)
    {
        L("== I: 退出 ==");
        ArenaConfigReset();
        ArenaMode.Config.assistMode = 2; ArenaMode.Config.speedMode = 1; ArenaMode.Config.kmh = 150f; ArenaMode.Config.build.Add(new ArenaBuildEntry { key = "attack_up", times = 9 });
        yield return ArenaLaunch("I");
        yield return new WaitForSeconds(1f);
        var old = gm;
        Arena.ExitHome();
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(2.5f);
        gm = GameManager.Instance;
        var save1 = SaveSystem.CaptureCategory(SaveCategory.Progress);
        string diff = "";
        foreach (var a in save0) { var b = save1.FirstOrDefault(x => x.k == a.k); if (b == null || b.v != a.v) diff += a.k + " "; }
        foreach (var b in save1) if (!save0.Any(x => x.k == b.k)) diff += "+" + b.k + " ";
        L($"[I] home: arena={ArenaMode.Active} debugRun={DebugRun.IsActive} encounter={(EncounterDirector.Instance != null ? EncounterDirector.Instance.enabled : (bool?)null)} assistPref {assistPref0} -> {PlayerPrefs.GetInt("HighSpeedAssistEnabled", -1)} best {best0:F0} -> {gm.BestDistance:F0} progress diff [{diff}]");
        Check(!ArenaMode.Active && !DebugRun.IsActive && !gm.HasStarted, "I: exit returns home and ends the DEBUG RUN");
        Check(diff.Length == 0 && Mathf.Approximately(best0, gm.BestDistance), "I: no progress save changed (BEST / MILE / cards / unlocks / lifetime distance / CONTINUE / selected character)");
        Check(PlayerPrefs.GetInt("HighSpeedAssistEnabled", -1) == assistPref0 && (EncounterDirector.Instance == null || EncounterDirector.Instance.enabled) && !ArenaMode.Invincible, "I: the normal settings are untouched (assist / encounters / invincibility)");
        // 通常のランは通常どおり(自然加速・速度の指定なし)
        yield return BeginRun("swordsman", "wasteland_road");
        yield return new WaitForSeconds(2f);
        Check(!ArenaMode.Active && pc.CurrentAutoRunSpeed > 0f && gm.MaxDistance > 5f && gm.HasStarted, $"I: a normal run afterwards runs normally (speed {pc.CurrentAutoRunSpeed * GameManager.KmhPerMps:F1} km/h, d {gm.MaxDistance:F0})");
        yield return EndRun();
    }
}
#endif
