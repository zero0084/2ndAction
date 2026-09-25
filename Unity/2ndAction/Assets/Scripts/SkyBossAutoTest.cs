#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// 天空回廊ボス追加(2026-09-25) - Editor専用の自動確認(CaveBossAutoTestと同じ流れ)。
// 1) ResolveSkyGate(距離→ボスの表)を1〜100(=1,000m〜100,000m)まで走査し、優先順位
//    (10,000m専用 > 5,000m魔人 > 1,000mドラゴン)・重複なし・100,000mはゲート無し(死神)を確認。
// 2) 11種(ドラゴン/魔人+専用9体)を1体ずつForce Spawnし、登場演出→AIを数秒動かして
//    攻撃(Hitbox/落雷等の予告ハザード/弾)が実際に発生するか、例外が出ないかを確認。撃破→
//    BOSS REWARD→遭遇終了まで実プレイヤーと同じ経路で完了するか確認。フェニックスは復活も確認。
// 3) ドラゴン4体/魔人3体の同時出現。4) 100,000mの死神(既存SpawnDeath)が天空回廊でも出るか。
// 結果は SkyBossAutoTest.txt。
public class SkyBossAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("SkyBossTest", 0) != 1) return;
        EditorPrefs.SetInt("SkyBossTest", 0);
        new GameObject("SkyBossTest").AddComponent<SkyBossAutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    void L(string s) { log.AppendLine(s); Debug.Log("[SkyBossTest] " + s); }

    bool anyException;
    int failures;
    void Check(bool ok, string what) { if (!ok) { failures++; L("[FAIL] " + what); } }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1.5f);
        var gm = GameManager.Instance;
        gm.SetSelectedStage("sky_corridor");
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float t0 = Time.time;
        while (!(gm.HasStarted && !gm.CountdownActive) && Time.time - t0 < 10f) yield return null;
        gm.DebugSetInvincible(true);
        StartCoroutine(KeepAlive(gm));
        StartCoroutine(AutoPickCards(gm));
        L($"stage={gm.ActiveRunStageId} character={gm.SelectedCharacterId}");
        Check(gm.ActiveRunStageId == "sky_corridor", "stage is sky_corridor");
        foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var s in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) s.enabled = false;
        if (TerrainManager.Instance != null) TerrainManager.Instance.enemySpawnChance = 0f;
        yield return new WaitForSeconds(0.5f);

        Application.LogCallback handler = (cond, trace, type) =>
        {
            if (type == LogType.Exception || type == LogType.Error) { anyException = true; L("[EXC] " + cond + "\n" + trace); }
        };
        Application.logMessageReceived += handler;

        var bm = BossManager.Instance;
        var bmType = typeof(BossManager);

        // ---- 1) スケジュール ----
        var resolveSky = bmType.GetMethod("ResolveSkyGate", BindingFlags.NonPublic | BindingFlags.Instance);
        SkyBossKind[] tenKm = { SkyBossKind.Behemoth, SkyBossKind.Titan, SkyBossKind.Jellyfish, SkyBossKind.Leviathan, SkyBossKind.Fenrir, SkyBossKind.SkyGolem, SkyBossKind.Phoenix, SkyBossKind.SkySerpent, SkyBossKind.Guardian };
        L("--- ResolveSkyGate schedule (k=1..100) ---");
        for (int k = 1; k <= 100; k++)
        {
            object[] args = { k, null, null };
            bool ok = (bool)resolveSky.Invoke(bm, args);
            var kind = (SkyBossKind)args[1];
            int count = (int)args[2];
            L(ok ? $"k={k} ({k * 1000}m): kind={kind} count={count}" : $"k={k} ({k * 1000}m): (no gate - Death)");
            if (k == 100) Check(!ok, "100,000m has no regular gate (Death)");
            else if (k % 10 == 0) Check(ok && kind == tenKm[k / 10 - 1] && count == 1, $"k={k} ten-km boss");
            else if (k % 5 == 0) Check(ok && kind == SkyBossKind.Majin && count >= 1 && count <= 3, $"k={k} majin");
            else Check(ok && kind == SkyBossKind.Dragon && count >= 1 && count <= 4, $"k={k} dragon");
        }
        object[] a15 = { 15, null, null }; resolveSky.Invoke(bm, a15);
        Check((int)a15[2] >= 2, "15,000m majin count >= 2");
        object[] a39 = { 39, null, null }; resolveSky.Invoke(bm, a39);
        Check((int)a39[2] == 4, "39,000m dragon count == 4");

        // ---- 2) 11種を1体ずつ ----
        var debugSpawn = bmType.GetMethod("DebugForceSpawnSky", BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (SkyBossKind kind in System.Enum.GetValues(typeof(SkyBossKind)))
        {
            debugSpawn.Invoke(bm, new object[] { kind, 1 });
            float observe = kind == SkyBossKind.Dragon || kind == SkyBossKind.Majin ? 12f : 11f;
            var seen = new HashSet<string>();
            float st = Time.time;
            WildBossBase wb = null;
            bool everVisibleOnScreen = false;
            while (Time.time - st < observe)
            {
                foreach (var hb in FindObjectsByType<BossHitbox>(FindObjectsSortMode.None)) if (hb.IsActive) seen.Add(hb.name);
                foreach (var s in FindObjectsByType<SkyStrike>(FindObjectsSortMode.None)) seen.Add(s.name);
                foreach (var p in FindObjectsByType<BossProjectile>(FindObjectsSortMode.None)) seen.Add("Projectile");
                foreach (var f in FindObjectsByType<FireballController>(FindObjectsSortMode.None)) seen.Add("Fireball");
                foreach (var b in FindObjectsByType<SkyWarnBand>(FindObjectsSortMode.None)) seen.Add("WarnBand");
                if (wb == null) wb = FindFirstObjectByType<WildBossBase>();
                if (wb != null && Camera.main != null)
                {
                    Vector3 vp = Camera.main.WorldToViewportPoint(wb.CenterWorld);
                    if (vp.x > -0.1f && vp.x < 1.1f && vp.y > -0.3f && vp.y < 1.3f) everVisibleOnScreen = true;
                }
                yield return null;
            }

            int dragons = 0, majins = 0;
            foreach (var d in FindObjectsByType<DragonController>(FindObjectsSortMode.None)) if (!d.IsDead) dragons++;
            foreach (var m in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) if (!m.IsDead) majins++;
            L($"[Spawn] {kind}: wild={(wb != null ? wb.name : "-")} dragons={dragons} majins={majins} IsBossPhase={bm.IsBossPhase} onScreen={everVisibleOnScreen} attacks=[{string.Join(",", seen)}]");
            if (kind == SkyBossKind.Dragon) Check(dragons == 1, "dragon spawned");
            else if (kind == SkyBossKind.Majin) Check(majins == 1, "majin spawned");
            else { Check(wb != null, kind + " spawned"); Check(everVisibleOnScreen, kind + " visible on screen"); Check(seen.Count > 0, kind + " attacked within " + observe + "s"); }

            // 撃破
            Diag("before kill " + kind);
            KillAll();
            if (kind == SkyBossKind.Phoenix && wb != null)
            {
                yield return new WaitForSeconds(0.2f);
                Check(!wb.IsDead, "phoenix survives first lethal damage (rebirth)");
                yield return new WaitForSeconds(3.2f);
                L($"[Rebirth] Phoenix: IsDead={wb.IsDead} hp={wb.Hp}/{wb.maxHp} (expect alive, ~40%)");
                Check(!wb.IsDead && wb.Hp > 0 && wb.Hp <= wb.maxHp / 2, "phoenix reborn with partial HP");
                KillAll();
            }
            yield return WaitEncounterEnd(gm, bm, kind.ToString());
        }

        // ---- 3) 複数体 ----
        debugSpawn.Invoke(bm, new object[] { SkyBossKind.Dragon, 4 });
        yield return new WaitForSeconds(8f);
        int dCount = 0; foreach (var d in FindObjectsByType<DragonController>(FindObjectsSortMode.None)) if (!d.IsDead) dCount++;
        L($"[Multi] dragons alive={dCount} (expect 4)");
        Check(dCount == 4, "4 dragons");
        KillAll();
        yield return WaitEncounterEnd(gm, bm, "Dragon x4");

        debugSpawn.Invoke(bm, new object[] { SkyBossKind.Majin, 3 });
        yield return new WaitForSeconds(8f);
        int mCount = 0; foreach (var m in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) if (!m.IsDead) mCount++;
        L($"[Multi] majins alive={mCount} (expect 3)");
        Check(mCount == 3, "3 majins");
        KillAll();
        yield return WaitEncounterEnd(gm, bm, "Majin x3");

        // ---- 3b) 実際の距離ゲート経由(WARNING演出→出現)。1,000m=ドラゴン、10,000m=ベヒーモス ----
        var restore = bmType.GetMethod("RestoreNextBossDistance");
        const float scale = 1f; // Debug Modeの距離短縮は2026-09-26に廃止
        float G(int k) => k * 1000f * scale;
        // 前半のForce Spawn戦闘で溜まった「戦闘中の移動除外」オフセットを0に戻し、ワープ後の距離=ワープ先にする。
        var gmType = typeof(GameManager);
        void ResetOffset()
        {
            gmType.GetField("distanceExclusionOffset", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(gm, 0f);
            gmType.GetField("distanceExclusionOffsetExact", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(gm, 0.0);
        }
        L($"gate scale={scale} (DebugMode={gm.DebugMode})");
        restore.Invoke(bm, new object[] { G(1) - 1000f * scale - 1f });
        ResetOffset();
        gm.DebugWarpToDistance(Mathf.Max(gm.MaxDistance, G(1) - 3f));
        yield return WaitGateSpawn(gm, bm, "Gate 1,000m", () => FindFirstObjectByType<DragonController>() != null);
        KillAll();
        yield return WaitEncounterEnd(gm, bm, "Gate 1,000m");
        restore.Invoke(bm, new object[] { G(9) });
        ResetOffset();
        gm.DebugWarpToDistance(G(10) - 3f);
        yield return WaitGateSpawn(gm, bm, "Gate 10,000m", () => { var w = FindFirstObjectByType<WildBossBase>(); return w != null && w is BehemothBoss; });
        KillAll();
        yield return WaitEncounterEnd(gm, bm, "Gate 10,000m");
        restore.Invoke(bm, new object[] { G(14) });
        ResetOffset();
        gm.DebugWarpToDistance(G(15) - 3f);
        yield return WaitGateSpawn(gm, bm, "Gate 15,000m", () => { int n = 0; foreach (var m in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) if (!m.IsDead) n++; return n >= 2; });
        KillAll();
        yield return WaitEncounterEnd(gm, bm, "Gate 15,000m");

        // ---- 4) 100,000m 死神 ----
        bmType.GetMethod("SpawnDeath", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(bm, null);
        yield return new WaitForSeconds(1.0f);
        var reaper = FindFirstObjectByType<GrimReaperController>();
        L($"[Death] GrimReaper present={reaper != null}");
        Check(reaper != null, "death spawns on sky corridor");

        // ---- 5) 走行継続 ----
        // (MaxDistanceはワープと戦闘中の移動除外が重なるとしばらく据え置きになるため、実際の走行距離で確認)
        float rawBefore = PlayerController.Instance.DistanceFromStart;
        yield return new WaitForSeconds(1.0f);
        float rawAfter = PlayerController.Instance.DistanceFromStart;
        L($"[ContinueRun] run distance {rawBefore:F1} -> {rawAfter:F1} gameOver={gm.IsGameOver}");
        Check(rawAfter > rawBefore + 2f && !gm.IsGameOver, "run keeps going after all encounters");

        // ---- 6) デバッグ速度ボタン(SPD -/+/x1)の倍率が実際の走行速度に効くか ----
        var step = typeof(GameManager).GetMethod("StepDebugSpeed", BindingFlags.NonPublic | BindingFlags.Static);
        float up = (float)step.Invoke(null, new object[] { 1f, +1 });
        float down = (float)step.Invoke(null, new object[] { 1f, -1 });
        var pc = PlayerController.Instance;
        float baseSpeed = pc.CurrentAutoRunSpeed;
        PlayerController.DebugSpeedScale = 3f;
        float fastSpeed = pc.CurrentAutoRunSpeed;
        float x0 = pc.DistanceFromStart;
        yield return new WaitForSeconds(1.0f);
        float moved = pc.DistanceFromStart - x0;
        PlayerController.DebugSpeedScale = 1f;
        L($"[DebugSpeed] step up={up} down={down} base={baseSpeed:F1} x3={fastSpeed:F1} moved1s={moved:F1}");
        Check(up > 1f && down < 1f, "speed step up/down");
        Check(Mathf.Abs(fastSpeed - baseSpeed * 3f) < 0.5f && moved > baseSpeed * 2f, "debug speed x3 applies to actual run speed");

        Application.logMessageReceived -= handler;
        L("");
        L(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        L(anyException ? "SOME EXCEPTIONS LOGGED (see [EXC] lines above)" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../SkyBossAutoTest.txt"), log.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(anyException || failures > 0 ? 1 : 0); else EditorApplication.isPlaying = false;
    }

    // 自動テスト中は回避しないプレイヤーが被弾し続けるため、残機だけを補充してゲームオーバーを防ぐ
    // (報酬カードで残機が再設定されることがあるので毎フレーム確認)。
    IEnumerator KeepAlive(GameManager gm)
    {
        var lives = typeof(GameManager).GetProperty("Lives");
        var setter = lives != null ? lives.GetSetMethod(true) : null;
        while (true)
        {
            if (setter != null && gm.Lives < 50) setter.Invoke(gm, new object[] { 99 });
            yield return null;
        }
    }

    // レベルアップ/ボス報酬のカード選択(timeScale=0で一時停止する)を、実プレイヤーと同じタップ経路で解決する。
    int cardPicks;
    IEnumerator AutoPickCards(GameManager gm)
    {
        while (true)
        {
            if (gm.IsRewardSequenceWaitingForSelection)
            {
                var seq = FindFirstObjectByType<RewardCardSequence>();
                if (seq != null)
                {
                    seq.OnCardClicked(0);
                    yield return new WaitForSecondsRealtime(0.3f);
                    seq.OnCardClicked(0);
                    cardPicks++;
                    yield return new WaitForSecondsRealtime(0.3f);
                    continue;
                }
            }
            yield return null;
        }
    }

    void Diag(string label)
    {
        var gm = GameManager.Instance; var bm = BossManager.Instance; var pc = PlayerController.Instance;
        L($"  [Diag {label}] gameOver={gm.IsGameOver} win={gm.IsWin} started={gm.HasStarted} timeScale={Time.timeScale:F2} playerX={(pc != null ? pc.transform.position.x : 0f):F1} playerY={(pc != null ? pc.transform.position.y : 0f):F1} " +
          $"autoSpeed={(pc != null ? pc.CurrentAutoRunSpeed : 0f):F1} reacting={(pc != null && pc.IsReacting)} finishing={(pc != null && pc.IsFinishing)} dist={gm.MaxDistance:F1} phase={bm.IsBossPhase} " +
          $"alive(w/d/m)={bm.AliveWildCount}/{bm.AliveDragonCount}/{bm.AliveMajinCount} rewardWaiting={gm.IsRewardSequenceWaitingForSelection} lives={gm.Lives}");
    }

    static void KillAll()
    {
        foreach (var w in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (!w.IsDead) w.TakeDamage(99999, w.CenterWorld);
        foreach (var d in FindObjectsByType<DragonController>(FindObjectsSortMode.None)) if (!d.IsDead) d.TakeDamage(99999);
        foreach (var m in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) if (!m.IsDead) m.TakeDamage(99999);
    }

    IEnumerator WaitGateSpawn(GameManager gm, BossManager bm, string label, System.Func<bool> spawned)
    {
        float st = Time.unscaledTime;
        while (!spawned() && Time.unscaledTime - st < 60f) yield return null;
        bool ok = spawned();
        L($"[{label}] spawned={ok} IsBossPhase={bm.IsBossPhase} distance={gm.MaxDistance:F0} (clamped to gate)");
        Check(ok && bm.IsBossPhase, label + " spawned via real distance gate");
        Diag(label);
        yield return new WaitForSeconds(2f);
    }

    IEnumerator WaitEncounterEnd(GameManager gm, BossManager bm, string label)
    {
        float waitStart = Time.unscaledTime;
        int picksBefore = cardPicks;
        while (bm.IsBossPhase && Time.unscaledTime - waitStart < 15f) yield return null;
        bool cardHandled = cardPicks > picksBefore;
        L($"[EncounterEnd] {label}: IsBossPhase={bm.IsBossPhase} (expect False) cardHandled={cardHandled}");
        Diag(label);
        Check(!bm.IsBossPhase, label + " encounter ended");
        // 残った予告/弾を掃除(次のボスの計測に混ざらないように)
        foreach (var s in FindObjectsByType<SkyStrike>(FindObjectsSortMode.None)) Destroy(s.gameObject);
        foreach (var p in FindObjectsByType<BossProjectile>(FindObjectsSortMode.None)) Destroy(p.gameObject);
        foreach (var f in FindObjectsByType<FireballController>(FindObjectsSortMode.None)) Destroy(f.gameObject);
        yield return new WaitForSeconds(0.3f);
    }
}

public static class SkyBossTestMenu
{
    [MenuItem("Tools/OneMoreMile/Sky Boss Test (batch)")]
    public static void RunBatch()
    {
        EditorPrefs.SetInt("SkyBossTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif
