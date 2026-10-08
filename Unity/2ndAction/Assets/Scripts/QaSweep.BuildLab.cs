#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// ビルド検証(2026-10-08、依頼F): 1つのキャラ×ステージ×ビルド×育成×シードを、実際のゲームの自動補助だけで走らせて記録する。
//  検証側がするのは 出発 / カード選択(ビルドの優先順位で、実際に出た候補から)/ 記録 だけ。攻撃・回避・無敵・HP の補充・ワープはしない。
//  保存は -memSave(MemorySaveStore)必須: 通常のセーブを読まず書かない。開発用の解放(全キャラ/全マップ/全カード)はそのメモリの中だけ。
//  起動: -memSave -qaBuildLab <outDir> -blBuilds <builds.json> -blBuild <name> -blStage <id> -blGrowth 0|9 -blSeed N -encounterSeed N
//        [-blDepart normal|sprint] [-blSprintDest 50000] [-blRing none|all] [-blEngage 100] [-blBossLimit 240] [-blReaperLimit 300]
//        [-blMaxMin 240] [-blFast 1] [-blCommit hash] [-blTrial id]
//  出力: <outDir>/result.json(1試行=1行の JSON)/ events.tsv(取得・ボス・被弾・死亡の時系列)
public partial class QaSweep
{
    [System.Serializable] public class BlBuild { public string name, character, type, note; public string[] deck, charCards, priority; public bool takeFinalEvolution = true; }
    [System.Serializable] public class BlBuildFile { public BlBuild[] builds; }

    [System.Serializable] public class BlPick { public float dist, t; public string kind; public string[] offered; public string picked, why; public int level; }
    [System.Serializable] public class BlBoss { public float dist, t0, seconds; public string names; public bool defeated, timedOut; public float hpLeftFrac; public int hitsTaken; }
    [System.Serializable] public class BlHit { public float dist, t; public string reason, near; public int dmg, after; public bool boss; }
    [System.Serializable]
    public class BlResult
    {
        public string trial, commit, platform, build, buildType, character, stage, depart, ringPolicy;
        public int seed, growth, deckSize;
        public float engageKmh, bossLimit;
        public bool autoInBoss = true, autoAttack = true, autoAvoid = true;
        public string[] deck, charCards;
        public string outcome;          // success / dead / boss_timeout / stuck / time_limit / setup_error
        public bool success;
        public float maxDistance, gameSeconds, realSeconds, successGameSeconds = -1f;
        public float reaperSurvivedMeters = -1f, reaperSurvivedSeconds = -1f; public string reaperEnd = "";
        public int level, bossesDefeated, hitsTaken, shieldBlocks;
        public float assistFirstActiveDist = -1f, maxKmh;
        public string cause = "", causeDetail = "";
        public string deathSnapshot = "";
        public string finalAbilities = "";
        public BlPick[] picks; public BlBoss[] bosses; public BlHit[] lastHits;
        public string notes = "";
        public float avgFps; public int slowFrames; // 実行の負荷の確認(並列で 60fps を保てたか。slowFrames = 1/30 秒より長いフレーム)
    }

    BlResult bl;
    readonly List<BlPick> blPicks = new List<BlPick>();
    readonly List<BlBoss> blBosses = new List<BlBoss>();
    readonly List<BlHit> blHits = new List<BlHit>();
    readonly StringBuilder blEvents = new StringBuilder("t\tdist\tkind\tdetail\n");
    float blT, blReal0, blRunReal0; int blFrames;
    BlBoss blCurBoss;
    string blSnapshot = "";

    void BlEvent(string kind, string detail) => blEvents.Append($"{blT:F1}\t{(gm != null ? gm.MaxDistance : 0f):F0}\t{kind}\t{detail}\n");

    IEnumerator BuildLabMode()
    {
        autoPickHold = true; stopKeepAlive = true;
        bl = new BlResult
        {
            trial = Arg("-blTrial", ""), commit = Arg("-blCommit", ""), platform = $"{Platform.Name} {SystemInfo.processorType} x{SystemInfo.processorCount} / Unity {Application.unityVersion} / build {Application.version}",
            stage = Arg("-blStage", "wasteland_road"), growth = int.Parse(Arg("-blGrowth", "0")), seed = int.Parse(Arg("-blSeed", "1")),
            depart = Arg("-blDepart", "normal"), ringPolicy = Arg("-blRing", "none"),
            engageKmh = float.Parse(Arg("-blEngage", "100"), System.Globalization.CultureInfo.InvariantCulture),
            bossLimit = float.Parse(Arg("-blBossLimit", "240"), System.Globalization.CultureInfo.InvariantCulture),
        };
        float reaperLimit = float.Parse(Arg("-blReaperLimit", "300"), System.Globalization.CultureInfo.InvariantCulture);
        float maxMin = float.Parse(Arg("-blMaxMin", "240"), System.Globalization.CultureInfo.InvariantCulture);
        int sprintDest = int.Parse(Arg("-blSprintDest", "50000"));
        bool fast = Arg("-blFast", "0") == "1"; // 既定は通常速度(固定刻みの早送りは実時間で進む処理とずれるため使わない)
        blReal0 = Time.realtimeSinceStartup;

        // ---- ビルド
        BlBuild b = null;
        try
        {
            var file = JsonUtility.FromJson<BlBuildFile>(System.IO.File.ReadAllText(Arg("-blBuilds", "")));
            string want = Arg("-blBuild", "");
            b = file.builds.FirstOrDefault(x => x.name == want);
        }
        catch (System.Exception e) { bl.notes = "build file: " + e.Message; }
        if (b == null || !MemorySaveStore.Active) { bl.outcome = "setup_error"; bl.notes += MemorySaveStore.Active ? " build not found" : " -memSave is required"; BlWrite(); yield break; }
        bl.build = b.name; bl.buildType = b.type; bl.character = b.character;

        // ---- 検証用のデータ(メモリの中だけ)
        float w = 0f;
        while (GameManager.Instance == null && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        UnlockRules.DevUnlockAll = true; GachaStage.DevAllCardsOpen = true; SprintRecords.DevUnlockAll = true;
        SaveStore.SetInt(SaveKeys.DevFinalDungeonAlwaysOpen, 1);
        TutorialProgress.MarkOffered(); TutorialProgress.MarkEscapeGuideShown();
        SaveStore.SetInt("HighSpeedAssistEnabled", 1);
        SaveStore.SetFloat("HighSpeedAssistEngageKmh", bl.engageKmh);
        SaveStore.SetInt(HighSpeedAssist.BossPrefKey, 1); SaveStore.SetInt(HighSpeedAssist.AttackPrefKey, 1); SaveStore.SetInt(HighSpeedAssist.AvoidPrefKey, 1);
        int lv = bl.growth >= 9 ? 9 : 1;
        string Key(string id) => string.IsNullOrEmpty(id) ? "" : lv > 1 ? CardDataMigration.LegacyToKey(id, lv) : id;
        var deckKeys = (b.deck ?? new string[0]).Select(Key).ToList();
        foreach (var id in (b.deck ?? new string[0]).Concat(b.charCards ?? new string[0]).Where(x => !string.IsNullOrEmpty(x)).Distinct())
            CardInventory.AddCard(id, lv, 16);
        SaveStore.Save();

        // ---- 乱数: 地形(WorldRng)を固定してからシーンを読み直す(地形はシーンの最初に作られる)。出現は -encounterSeed、他は Random.InitState
        WorldRng.BeginDeterministic(bl.seed);
        yield return ReloadHome();
        gm.SetSelectedCharacter(b.character);
        gm.SetCharacterCardOwner(b.character);
        var cc = b.charCards ?? new string[0];
        var ccSet = new List<string>();
        for (int s = 0; s < GameManager.CharacterCardSlotCount; s++)
        {
            string id = s < cc.Length ? cc[s] : "";
            bool ok = string.IsNullOrEmpty(id) ? gm.EquipCharacterCard(s, null, 1) : gm.EquipCharacterCard(s, Key(id), lv);
            ccSet.Add(string.IsNullOrEmpty(id) ? "" : (ok ? Key(id) : "FAILED:" + id));
        }
        gm.SetDeck(deckKeys);
        bl.deck = gm.DeckCards.ToArray(); bl.deckSize = bl.deck.Length; bl.charCards = ccSet.ToArray();
        if (bl.deckSize != deckKeys.Count) bl.notes += $" deck set {bl.deckSize}/{deckKeys.Count}";
        gm.SetSelectedStage(bl.stage);
        var hsa = HighSpeedAssist.Instance;
        if (hsa != null) { hsa.SetEngageKmh(bl.engageKmh); hsa.SetEnabled(true); hsa.SetAutoInBoss(true); hsa.SetAutoAttack(true); hsa.SetAutoAvoid(true); }
        bl.engageKmh = hsa != null ? hsa.EngageSettingKmh : bl.engageKmh;
        Random.InitState(bl.seed);
        GameManager.QaDamageEvent = (reason, dmg, before, after) =>
        {
            var h = new BlHit { dist = gm != null ? gm.MaxDistance : 0f, t = blT, reason = reason, dmg = dmg, after = after, boss = BossManager.Instance != null && BossManager.Instance.IsBossPhase, near = BlNear(4f) };
            blHits.Add(h); if (blHits.Count > 60) blHits.RemoveAt(0);
            bl.hitsTaken++; if (blCurBoss != null) blCurBoss.hitsTaken++;
            BlEvent("hit", $"{reason} -{dmg} -> {after} near[{h.near}]");
        };

        // ---- 出発(通常 / 疾走)
        if (bl.depart == "sprint")
        {
            SprintRunner.QaRingPolicy = bl.ringPolicy == "all" ? 1 : -1; // none = 入力なし(下の段のリングだけ自然に通る)
            bool okDep = gm.DepartSprint(bl.stage, sprintDest);
            bl.depart = $"sprint:{sprintDest}";
            if (!okDep) { bl.outcome = "setup_error"; bl.notes += " DepartSprint refused"; BlWrite(); yield break; }
        }
        else typeof(GameManager).GetMethod("StartGame", NP).Invoke(gm, null);
        w = 0f;
        while (!(gm.HasStarted) && w < 20f) { yield return null; w += Time.unscaledDeltaTime; }
        pc = PlayerController.Instance;
        if (fast) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; Time.captureDeltaTime = 1f / 60f; }
        else { QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60; } // スマホと同じ 60fps の通常速度(画面の更新間隔に左右されない)
        blRunReal0 = Time.realtimeSinceStartup; blFrames = 0;
        BlEvent("start", $"{b.name} {bl.character} {bl.stage} growth{bl.growth} seed{bl.seed} {bl.depart} engage{bl.engageKmh}");

        // ---- 走る
        bool lastDungeon = bl.stage == BossManager.LastStageId;
        float lastProgressT = 0f, lastProgressD = 0f, snapAt = 0f, progressAt = 0f;
        bool wasBoss = false;
        while (true)
        {
            yield return null;
            if (gm == null) { gm = GameManager.Instance; if (gm == null) continue; }
            if (gm.HasStarted && !gm.IsGameOver) blT += Time.deltaTime;
            blFrames++; if (Time.unscaledDeltaTime > 1f / 30f) bl.slowFrames++;
            float d = gm.MaxDistance;
            var pcNow = PlayerController.Instance;
            if (pcNow != null) bl.maxKmh = Mathf.Max(bl.maxKmh, SpeedKmhOf(pcNow.CurrentAutoRunSpeed));
            if (hsa == null) hsa = HighSpeedAssist.Instance;
            if (bl.assistFirstActiveDist < 0f && hsa != null && pcNow != null && SpeedKmhOf(pcNow.CurrentAutoRunSpeed) >= bl.engageKmh) bl.assistFirstActiveDist = d;

            // 案内(初回の脱出の説明など)は「わかった」で閉じる(プレイヤーが閉じるのと同じ)
            if (FirstRunGuide.Open) FirstRunGuideDebug.Answer(0);
            // 再開の停止画面(疾走の到着/CONTINUE)は、プレイヤーがタップするのと同じ入口で始める
            if (gm.ResumeGate == GameManager.ResumeGatePhase.Waiting) { if (gm.RequestResumeFromGate()) BlEvent("resume_gate", "tap"); }
            // 経過の控え(長い試行の様子を外から見る)
            if (blT >= progressAt) { progressAt = blT + 30f; System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "progress.txt"), $"{blT / 60f:F1}min {d:F0}m {(Time.realtimeSinceStartup - blReal0) / 60f:F1}min-real {blSnapshot}"); }
            // カード選択(レベルアップ/ボス報酬/リング)
            if (gm.IsRewardSequenceWaitingForSelection) { yield return BlPickCard(b); continue; }

            // ボス
            var bm = BossManager.Instance;
            bool boss = bm != null && bm.IsBossPhase;
            if (boss && !wasBoss) { blCurBoss = new BlBoss { dist = d, t0 = blT }; blBosses.Add(blCurBoss); BlEvent("boss_start", ""); }
            if (boss && blCurBoss != null)
            {
                var alive = BlBossesAlive();
                if (alive.Count > 0)
                {
                    string names = string.Join("+", alive.Select(x => x.name).Distinct());
                    if (string.IsNullOrEmpty(blCurBoss.names) || (blCurBoss.names.Length < 120 && !blCurBoss.names.Contains(names))) blCurBoss.names = string.IsNullOrEmpty(blCurBoss.names) ? names : blCurBoss.names + "|" + names;
                    blCurBoss.hpLeftFrac = alive.Average(x => x.frac);
                }
                if (blT - blCurBoss.t0 > bl.bossLimit)
                {
                    blCurBoss.seconds = blT - blCurBoss.t0; blCurBoss.timedOut = true;
                    bl.outcome = "boss_timeout"; bl.cause = "制限時間内に撃破できず";
                    bl.causeDetail = $"{blCurBoss.names} @{blCurBoss.dist:F0}m HP残り{blCurBoss.hpLeftFrac:P0} {bl.bossLimit:F0}s";
                    BlEvent("boss_timeout", bl.causeDetail);
                    break;
                }
            }
            if (!boss && wasBoss && blCurBoss != null) { blCurBoss.seconds = blT - blCurBoss.t0; blCurBoss.defeated = true; BlEvent("boss_end", $"{blCurBoss.names} {blCurBoss.seconds:F1}s"); }
            wasBoss = boss;

            // 状態の控え(倒れた直前を残す)
            if (blT >= snapAt) { snapAt = blT + 0.5f; blSnapshot = BlSnapshot(); }

            // 成功
            if (!bl.success)
            {
                bool done = lastDungeon
                    ? (LastDungeonFlow.Instance != null && LastDungeonFlow.Instance.Current >= LastDungeonFlow.State.Credits)
                    : d >= 100000f;
                if (done) { bl.success = true; bl.successGameSeconds = blT; BlEvent("success", lastDungeon ? "final battle cleared" : "100,000m"); if (lastDungeon) { bl.outcome = "success"; break; } }
            }
            else if (!lastDungeon && blT - bl.successGameSeconds > reaperLimit) { bl.reaperEnd = "time_limit"; bl.outcome = "success"; break; }

            // 終わり
            if (gm.IsGameOver)
            {
                bl.outcome = bl.success ? "success" : "dead";
                if (bl.success) bl.reaperEnd = "captured_or_dead:" + gm.DeathReason;
                else BlClassifyDeath();
                BlEvent("dead", gm.DeathReason);
                break;
            }
            // 進まない(ボス戦/選択/停止ではないのに距離が止まっている)
            bool holds = boss || gm.IsRewardSequenceRunning || gm.LevelUpPending || (Time.timeScale < 0.01f && !gm.ResumeGateActive);
            if (d > lastProgressD + 1f || holds) { lastProgressD = d; lastProgressT = blT; }
            if (blT - lastProgressT > 90f) { bl.outcome = "stuck"; bl.cause = "不具合の疑い"; bl.causeDetail = $"距離が90秒進まない @{d:F0}m"; BlEvent("stuck", bl.causeDetail); break; }
            if (blT > maxMin * 60f) { bl.outcome = "time_limit"; bl.cause = "検証の時間上限"; bl.causeDetail = $"{maxMin}分"; break; }
        }
        if (bl.success && !lastDungeon) { bl.reaperSurvivedMeters = gm.MaxDistance - 100000f; bl.reaperSurvivedSeconds = blT - bl.successGameSeconds; }
        Time.captureDeltaTime = 0f;
        GameManager.QaDamageEvent = null;
        BlWrite();
    }

    static float SpeedKmhOf(float mps) => mps * 3.6f;

    // ---- カード選択: 出た候補から、ビルドの優先順位の一番上(FINAL EVOLUTION はビルドの方針どおり)。順位に無い物しか無ければ1枚目
    IEnumerator BlPickCard(BlBuild b)
    {
        yield return null; yield return null;
        var seq = FindFirstObjectByType<RewardCardSequence>();
        if (seq == null || !gm.IsRewardSequenceWaitingForSelection) yield break;
        var data = typeof(RewardCardSequence).GetField("currentCardData", NP)?.GetValue(seq) as RewardCardData[];
        int count = data != null ? data.Length : 0;
        var offered = new string[count];
        for (int i = 0; i < count; i++) offered[i] = data[i].CardId ?? "";
        int best = 0, bestRank = int.MaxValue; string why = "fallback-first";
        var pr = b.priority != null && b.priority.Length > 0 ? b.priority : b.deck;
        for (int i = 0; i < count; i++)
        {
            string id = offered[i];
            int rank;
            if (id.StartsWith(FinalEvolution.ChoicePrefix)) rank = b.takeFinalEvolution ? -1 : int.MaxValue - 1;
            else
            {
                string baseId = CardVariant.IsVariantKey(id) ? CardVariant.Parse(id)?.mainId : id;
                rank = System.Array.IndexOf(pr, baseId);
                if (rank < 0) rank = 10000 + i;
            }
            if (rank < bestRank) { bestRank = rank; best = i; why = rank == -1 ? "final-evolution" : rank >= 10000 ? "not-in-priority" : $"priority#{rank}"; }
        }
        string kind = gm.SprintActive ? "ring" : (BossManager.Instance != null && BossManager.Instance.IsBossPhase) ? "boss" : "levelup";
        string picked = count > 0 ? offered[best] : "";
        var p = new BlPick { dist = gm.MaxDistance, t = blT, kind = kind, offered = offered, picked = picked, why = why, level = gm.Level };
        blPicks.Add(p);
        BlEvent("pick", $"{kind} [{string.Join(",", offered.Select(BlShort))}] -> {BlShort(picked)} ({why})");
        if (count == 0) yield break;
        seq.OnCardClicked(best);
        for (int i = 0; i < 6; i++) yield return null;
        seq.OnCardClicked(best);
        for (int i = 0; i < 6; i++) yield return null;
    }
    static string BlShort(string id) => string.IsNullOrEmpty(id) ? "-" : CardVariant.IsVariantKey(id) ? (CardVariant.Parse(id)?.mainId ?? id) + "*" : id;

    struct BlBossInfo { public string name; public float frac; public Vector3 pos; }
    List<BlBossInfo> BlBossesAlive()
    {
        var list = new List<BlBossInfo>();
        foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (!(mb is IBossBattleDebug dbg) || !mb.isActiveAndEnabled || !dbg.DebugAlive) continue;
            float hp = BlNum(mb, "Hp"), max = BlNum(mb, "maxHp");
            if (max <= 0f) max = BlNum(mb, "MaxHp");
            list.Add(new BlBossInfo { name = dbg.DebugName, frac = max > 0f ? Mathf.Clamp01(hp / max) : -1f, pos = mb.transform.position });
        }
        return list;
    }
    static float BlNum(object o, string name)
    {
        var t = o.GetType();
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.FlattenHierarchy;
        for (var tt = t; tt != null; tt = tt.BaseType)
        {
            var pinfo = tt.GetProperty(name, F); if (pinfo != null && pinfo.GetIndexParameters().Length == 0) { try { return System.Convert.ToSingle(pinfo.GetValue(o)); } catch { } }
            var f = tt.GetField(name, F); if (f != null) { try { return System.Convert.ToSingle(f.GetValue(o)); } catch { } }
        }
        return -1f;
    }

    // 近くにある物(被弾の原因の手がかり): 敵/障害物/ボス(距離 r 以内)
    string BlNear(float r)
    {
        var p = PlayerController.Instance; if (p == null) return "";
        Vector3 pp = p.transform.position;
        var sb = new List<string>();
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
            if (e.isActiveAndEnabled && Vector2.Distance(e.transform.position, pp) < r) sb.Add($"enemy:{e.name.Replace("(Clone)", "")}@{e.transform.position.x - pp.x:F1}");
        foreach (var o in FindObjectsByType<ObstacleController>(FindObjectsSortMode.None))
            if (o.isActiveAndEnabled && Vector2.Distance(o.transform.position, pp) < r) sb.Add($"obstacle:{o.name.Replace("(Clone)", "")}@{o.transform.position.x - pp.x:F1}");
        foreach (var bb in BlBossesAlive())
            if (Mathf.Abs(bb.pos.x - pp.x) < r * 4f) sb.Add($"boss:{bb.name}@{bb.pos.x - pp.x:F1}");
        return string.Join(";", sb.Take(8));
    }

    string BlSnapshot()
    {
        var p = PlayerController.Instance;
        var tm = FindFirstObjectByType<TerrainManager>();
        string terr = "";
        if (p != null && tm != null)
        {
            float x = p.transform.position.x;
            tm.TryGetGroundFast(x, out float gy, out bool pitHere);
            terr = $"grounded={p.IsGrounded} y={p.transform.position.y:F1} groundY={gy:F1} pitHere={pitHere} pitNear={tm.IsNearPit(x + 3f, 3f)}";
        }
        var kmh = p != null ? SpeedKmhOf(p.CurrentAutoRunSpeed) : 0f;
        var hsa = HighSpeedAssist.Instance;
        return $"HP {gm.Lives}/{gm.MaxLives} Lv{gm.Level} {gm.MaxDistance:F0}m {kmh:F0}km/h assist={(hsa != null ? hsa.CurrentStatus.ToString() : "-")} boss={(BossManager.Instance != null && BossManager.Instance.IsBossPhase)} {terr} near[{BlNear(6f)}] abilities[{BlAbilities()}]";
    }

    string BlAbilities()
    {
        var ids = new HashSet<string>();
        foreach (var k in gm.DeckCards) ids.Add(CardVariant.IsVariantKey(k) ? CardVariant.Parse(k)?.mainId : k);
        foreach (var k in gm.CharacterCardIds) if (!string.IsNullOrEmpty(k)) ids.Add(CardVariant.IsVariantKey(k) ? CardVariant.Parse(k)?.mainId : k);
        return string.Join(",", ids.Where(x => x != null).Select(x => $"{x}:{gm.GetAbilityRunStack(x)}").Where(s => !s.EndsWith(":0")));
    }

    // 敗因の分類(手がかりから。判断できない時は 未確定)
    void BlClassifyDeath()
    {
        var last = blHits.Count > 0 ? blHits[blHits.Count - 1] : null;
        string reason = gm.DeathReason ?? "";
        bool fall = reason.StartsWith("DeathY");
        bool assistOff = bl.assistFirstActiveDist < 0f || (last != null && last.dist < bl.assistFirstActiveDist);
        bool inBoss = last != null && last.boss;
        string near = last != null ? last.near : "";
        if (fall) { bl.cause = assistOff ? "補助が未作動(開始速度に届く前)の落下" : "穴・落下"; }
        else if (reason.Contains("TerrainWall") || reason.Contains("Wall")) bl.cause = assistOff ? "補助が未作動(開始速度に届く前)の地形の段差(壁)" : "地形の段差(壁)";
        else if (inBoss) bl.cause = blCurBoss != null && blCurBoss.hpLeftFrac > 0.98f ? "攻撃が届かない(ボスがほぼ無傷)" : "耐久不足(ボス戦)";
        else if (reason.Contains("Reaper") || near.Contains("Reaper")) bl.cause = "死神";
        else if (near.Contains("obstacle:")) bl.cause = assistOff ? "補助が未作動(開始速度に届く前)の障害物" : "障害物";
        else if (near.Contains("enemy:")) bl.cause = assistOff ? "補助が未作動(開始速度に届く前)の雑魚" : "耐久不足(雑魚)";
        else bl.cause = "未確定";
        bl.causeDetail = $"{reason} last[{(last != null ? last.reason + " near " + last.near : "-")}]";
        bl.deathSnapshot = blSnapshot;
    }

    void BlWrite()
    {
        if (gm != null)
        {
            bl.maxDistance = gm.MaxDistance; bl.level = gm.Level;
            bl.bossesDefeated = BossManager.Instance != null ? BossManager.Instance.BossesDefeated : 0;
            bl.finalAbilities = BlAbilities();
            if (string.IsNullOrEmpty(bl.deathSnapshot)) bl.deathSnapshot = blSnapshot;
        }
        bl.gameSeconds = blT; bl.realSeconds = Time.realtimeSinceStartup - blReal0;
        bl.avgFps = blFrames / Mathf.Max(1f, Time.realtimeSinceStartup - blRunReal0);
        bl.picks = blPicks.ToArray(); bl.bosses = blBosses.ToArray(); bl.lastHits = blHits.Skip(Mathf.Max(0, blHits.Count - 12)).ToArray();
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "result.json"), JsonUtility.ToJson(bl));
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "events.tsv"), blEvents.ToString());
        L($"[buildlab] {bl.build} {bl.stage} g{bl.growth} seed{bl.seed} {bl.depart}: {bl.outcome} {bl.maxDistance:F0}m {bl.gameSeconds / 60f:F1}min game / {bl.realSeconds / 60f:F1}min real cause={bl.cause} {bl.causeDetail}");
    }
}
#endif
