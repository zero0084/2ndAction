#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// カードシステムの正常化(2026-10-03)の確認。 -qaCardFix <dir>
//   A: Lv9上限が能力ごと(キャラカード枠/合成のレア度違い/旧形式の複合ID/取得/表示)
//   B: 実際の現在速度(50/75/100/125/150km/h)と正規化、MOMENTUMの値が変わっていないこと
//   C: 敵出現率カード → EncounterDirector の頻度(間隔/休憩)。重なり/ボスの関門/上限
//   D: 属性の基盤(炎上/冷気/凍結/落雷の連鎖/風刃/出血の回復)。値0なら何も起きない
// テストの前に今のセーブを控え、最後に戻す。
public partial class QaSweep
{
    const BindingFlags AnyInst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    IEnumerator CardFixMode()
    {
        var snapSave = SaveSystem.Capture();
        autoPickHold = true;
        string only = Arg("-qaCardFixOnly", "ABCD");
        if (only.Contains('A')) yield return CapCases();
        if (only.Contains('B')) yield return SpeedCases();
        if (only.Contains('C')) yield return SpawnRateCases();
        if (only.Contains('D')) yield return ElementCases();
        autoPickHold = false;
        SaveSystem.Restore(snapSave);
        CardInventory.ReloadFromPrefs();
        RunCheckpoint.Reload();
        L("[cardfix] test machine save restored");
    }

    // ラン中のカードの状態を、キャラの基準値・カードなしへ戻す(このテストの各ケースの最初)
    void ResetRunCardsForTest()
    {
        // CARD BALANCE TEST の RESET と同じ手順(あちらは DEBUGモードの時だけ動くので、ここで直接)
        var def = CardBalanceTest.CurrentDef();
        gm.CardTestClearCardState();
        pc.CardTestClearCardBonuses();
        if (def != null)
        {
            pc.runSpeed = pc.CardTestBaseRunSpeed * def.groundMobilityMultiplier;
            pc.jumpForce = pc.CardTestBaseJumpForce * def.jumpForceMultiplier;
            pc.maxJumps = Mathf.Max(1, def.jumpCount);
            pc.CardTestSetAttackPower(def.attackPower);
            pc.CardTestSetAttackSpeed(def.attackSpeedMultiplier);
            pc.CardTestSetAttackRange(def.attackRangeMultiplier);
            gm.CardTestSetMaxLives(def.baseMaxLives);
        }
        ((Dictionary<string, int>)typeof(GameManager).GetField("runAbilityStacks", AnyInst).GetValue(gm)).Clear();
        ((System.Collections.IList)typeof(GameManager).GetField("upgradeHistory", AnyInst).GetValue(gm)).Clear();
        var ids = (string[])typeof(GameManager).GetField("characterCardIds", AnyInst).GetValue(gm);
        var lvs = (int[])typeof(GameManager).GetField("characterCardLevels", AnyInst).GetValue(gm);
        for (int i = 0; i < ids.Length; i++) { ids[i] = null; lvs[i] = 1; }
    }

    void SetSlots(params (string id, int lv)[] slots)
    {
        var ids = (string[])typeof(GameManager).GetField("characterCardIds", AnyInst).GetValue(gm);
        var lvs = (int[])typeof(GameManager).GetField("characterCardLevels", AnyInst).GetValue(gm);
        for (int i = 0; i < ids.Length; i++) { ids[i] = i < slots.Length ? slots[i].id : null; lvs[i] = i < slots.Length ? slots[i].lv : 1; }
        typeof(GameManager).GetMethod("ApplyCharacterCardEffects", AnyInst).Invoke(gm, null);
    }

    void Pick(string id) => typeof(GameManager).GetMethod("ApplyUpgradeByCardId", AnyInst).Invoke(gm, new object[] { id });

    // ===================================================================== A
    IEnumerator CapCases()
    {
        yield return BeginRun("swordsman", "wasteland_road");
        TimeControl.Pause(this);
        var atkCard = CardDatabase.FindBaseById("attack_up");
        float per = atkCard.effects.First(e => e.type == EffectType.AttackPct).value; // v3: 攻撃力は割合(1Lvごとに+5%)
        var spdCard = CardDatabase.FindBaseById("speed_up");

        // A1: 3枠に ATTACK UP x9 の合成(レア度だけ違う3つのキー)。以前は27回分 → 9回分で止まる
        ResetRunCardsForTest();
        float atk0 = pc.CardAttackFactor;
        SetSlots(("v2|attack_up|9|1|attack_up*9", 9), ("v2|attack_up|9|2|attack_up*9", 9), ("v2|attack_up|9|3|attack_up*9", 9));
        float a1 = pc.CardAttackFactor;
        Check(Mathf.Abs(a1 - (atk0 + per * 9)) < 0.001f, $"A1: three fused ATTACK UP x9 in the character slots give Lv9 only ({atk0} -> {a1}, expected +{per * 9})");
        Check(gm.GetCurrentRunStack("attack_up") == 9 && !gm.CanStillPick(atkCard), $"A1: ATTACK UP is Lv{gm.GetCurrentRunStack("attack_up")} and no longer offered");
        int disc1 = gm.CardCapDiscardedStacks;
        Check(disc1 == 18, $"A1: 18 stacks were not applied ({disc1})");
        // 取得してもこれ以上は効かない(素のID/合成のどちらでも)
        Pick("attack_up"); Pick("v2|attack_up|5|4|attack_up*5");
        Check(Mathf.Approximately(pc.CardAttackFactor, a1), $"A1: picking more ATTACK UP (plain / fused) changes nothing ({pc.CardAttackFactor:F3})");

        // A2: 素のLv9 + 能力の混ざった合成。上限に届いた能力だけ止まり、ほかの能力は効く
        ResetRunCardsForTest();
        float spd0 = pc.runSpeed;
        SetSlots(("attack_up", 9), ("v2|attack_up|9|3|attack_up*5/speed_up*4", 9));
        Check(Mathf.Abs(pc.CardAttackFactor - (atk0 + per * 9)) < 0.001f, $"A2: plain Lv9 + fused (attack x5, speed x4): attack stays at Lv9 ({pc.CardAttackFactor:F3})");
        Check(gm.GetAbilityRunStack("speed_up") == 4 && pc.runSpeed > spd0 * 1.1f, $"A2: the other ability in the fused card still applies (speed Lv{gm.GetAbilityRunStack("speed_up")}, runSpeed {spd0:0.00}->{pc.runSpeed:0.00})");
        // SPEED UP は残り5まで取れる → 6回目からは効かない
        int picked = 0; float sBefore = pc.runSpeed;
        while (gm.CanStillPick(spdCard) && picked < 20) { Pick("speed_up"); picked++; }
        Check(picked == 5 && gm.GetAbilityRunStack("speed_up") == 9, $"A2: SPEED UP offered until Lv9 ({picked} picks, Lv{gm.GetAbilityRunStack("speed_up")})");
        float s9 = pc.runSpeed; Pick("speed_up");
        Check(Mathf.Approximately(pc.runSpeed, s9), "A2: a pick at Lv9 (any route) adds nothing");

        // A3: 同じ素のIDを2枠(Lv9+Lv9)。合計で9
        ResetRunCardsForTest();
        SetSlots(("attack_up", 9), ("attack_up", 9));
        Check(Mathf.Abs(pc.CardAttackFactor - (atk0 + per * 9)) < 0.001f && gm.GetCurrentRunStack("attack_up") == 9, $"A3: the same card in two slots (Lv9+Lv9) gives Lv9 ({pc.CardAttackFactor:F3})");

        // A4: 旧形式の複合ID("a+b")も能力に分解して数える
        var ab = GameManager.AbilitiesOf("attack_up+speed_up");
        Check(ab.Count == 2 && ab.Any(a => a.id == "attack_up") && ab.Any(a => a.id == "speed_up"), $"A4: an old compound id splits into its abilities ({string.Join(",", ab.Select(a => a.id + "*" + a.stacks))})");
        ResetRunCardsForTest();
        SetSlots(("attack_up", 8));
        Pick("attack_up+speed_up");
        Check(gm.GetCurrentRunStack("attack_up") == 9 && gm.GetAbilityRunStack("speed_up") == 1, "A4: compound pick adds 1 to each ability within the cap");

        // A5: 表示(候補の Lv.N -> Lv.N+1)は主能力で、9で止まる
        ResetRunCardsForTest();
        SetSlots(("attack_up", 7));
        var data = (RewardCardData)typeof(GameManager).GetMethod("MakeChoiceCardData", AnyInst).Invoke(gm, new object[] { atkCard });
        Check(data.LevelLine != null && data.LevelLine.Contains("Lv.7 -> Lv.8"), $"A5: choice label for Lv7 ATTACK UP = '{data.LevelLine}'");
        L($"[cardfix] A: cap per ability OK (discarded stacks in A1: {disc1})");
        ResetRunCardsForTest();
        TimeControl.Resume(this);
        yield return EndRun();
    }

    // ===================================================================== B
    IEnumerator SpeedCases()
    {
        yield return BeginRun("swordsman", "wasteland_road");
        TimeControl.Pause(this);
        ResetRunCardsForTest();
        float baseKmh = GameManager.SpeedKmh(pc.runSpeed);
        // 自然加速だけで 50 / 75 / 100km/h になる距離へ(倍率 = 目標 ÷ 基準)
        foreach (float target in new[] { 50f, 75f, 100f })
        {
            float m = target / baseKmh;
            float d = pc.speedUpStartDistance + 100f * Mathf.Log(m) / Mathf.Log(1f + pc.speedUpPer100m);
            if (target >= 100f) d += 200f; // 上限の手前で止まらないよう少し先
            WarpTo(d);
            yield return null;
            float kmh = pc.CurrentRunKmh;
            Check(Mathf.Abs(kmh - Mathf.Min(target, 100f)) < 0.6f, $"B: natural speed at {d:F0}m = {kmh:0.0} km/h (expected {target})");
            L($"[cardfix] B: {target} km/h -> CurrentRunKmh {kmh:0.0}, factor(50..150) {pc.SpeedFactor01(50f, 150f):0.00}, momentum term distance {pc.MomentumSpeedTermByDistance:0.00} / current {pc.MomentumSpeedTermByCurrentSpeed:0.00}");
        }
        // 速度カードで 125 / 150km/h(自然加速の上限100km/hの上に掛かる)
        var c25 = ScriptableObject.CreateInstance<CardDefinition>(); c25.cardId = "qa_speed25"; c25.effects = new List<CardEffect> { new CardEffect { type = EffectType.MoveSpeed, value = 0.25f } };
        var c20 = ScriptableObject.CreateInstance<CardDefinition>(); c20.cardId = "qa_speed20"; c20.effects = new List<CardEffect> { new CardEffect { type = EffectType.MoveSpeed, value = 0.2f } };
        gm.ApplyCardEffectsStacked(c25, 1);
        Check(Mathf.Abs(pc.CurrentRunKmh - 125f) < 0.6f, $"B: card speed +25% on the 100 km/h cap = {pc.CurrentRunKmh:0.0} km/h (HUD value)");
        float f125 = pc.SpeedFactor01(50f, 150f);
        gm.ApplyCardEffectsStacked(c20, 1);
        Check(Mathf.Abs(pc.CurrentRunKmh - 150f) < 0.6f, $"B: then +20% = {pc.CurrentRunKmh:0.0} km/h");
        Check(Mathf.Abs(f125 - 0.75f) < 0.01f && Mathf.Abs(pc.SpeedFactor01(50f, 150f) - 1f) < 0.001f, $"B: normalized factor 50..150 km/h: 125 -> {f125:0.00}, 150 -> {pc.SpeedFactor01(50f, 150f):0.00}");
        // v3: MOMENTUM は実際の速さ(100km/h 以下は0、150km/h で最大)
        var mom = CardDatabase.FindBaseById("momentum");
        gm.ApplyCardEffectsStacked(mom, 1);
        float mf = PlayerController.MomentumFactor(pc.CurrentRunKmh);
        Check(Mathf.Abs(mf - 1f) < 0.02f && PlayerController.MomentumFactor(100f) == 0f, $"B: MOMENTUM uses the current speed (150 km/h -> factor {mf:0.00}, 100 km/h -> 0)");
        ResetRunCardsForTest();
        TimeControl.Resume(this);
        yield return EndRun();
    }

    // ===================================================================== C
    IEnumerator SpawnRateCases()
    {
        foreach (string stage in Arg("-qaCardFixStages", "natural_cave,wasteland_road").Split(','))
            yield return SpawnRateStage(stage);
    }

    IEnumerator SpawnRateStage(string stage)
    {
        var results = new List<(string name, float mult, float freq, int enc, int rest, int enemies, float meters, float minGap)>();
        int[] seeds = { 11, 22 };
        foreach (var (name, n) in new[] { ("x1", 0), ("MORE ENEMIES Lv5", 5), ("PANDEMONIUM Lv9 (extreme)", -9) })
        foreach (int seed in seeds)
        {
            // same seeds for every setting: only the spawn frequency differs (terrain + encounter choices)
            if (EncounterDirector.Instance != null) typeof(EncounterDirector).GetField("fixedSeed", AnyInst).SetValue(EncounterDirector.Instance, seed);
            Random.InitState(seed);
            yield return BeginRun("swordsman", stage);
            GameManager.BlockExpGain = true; // keep level-up choices from stopping the run (this test picks no cards)
            WarpTo(5200f);                   // natural cap (100 km/h): the high speed assist clears obstacles/pits
            yield return new WaitForSecondsRealtime(0.5f);
            ResetRunCardsForTest();
            if (n > 0) gm.ApplyCardEffectsStacked(CardDatabase.FindBaseById("more_enemies"), n);
            if (n < 0) gm.ApplyCardEffectsStacked(CardDatabase.FindBaseById("pandemonium"), -n);
            var dir = EncounterDirector.Instance;
            int idx0 = dir.Recent.Count;
            int sp0 = dir.SpawnedEnemies;
            float d0 = gm.MaxDistance;
            TimeControl.SetDebugTimeScale(3f);
            float t = 0f;
            while (t < 90f && gm.MaxDistance < 9900f && !gm.IsGameOver) { yield return null; t += Time.unscaledDeltaTime; }
            TimeControl.SetDebugTimeScale(1f);
            GameManager.BlockExpGain = false;
            // route pairs (upper/lower at a terrain fork) are placed by the terrain, not by spacing, and their two routes overlap in X:
            // count/check only the main-route encounters (the part spawn rate controls)
            var recs = dir.Recent.Skip(idx0).Where(r => string.IsNullOrEmpty(r.upper) && string.IsNullOrEmpty(r.lower) && (r.terrain == null || !r.terrain.Contains("branch"))).ToList();
            int enc = recs.Count(r => r.intensity != EncounterIntensity.Rest), rest = recs.Count(r => r.intensity == EncounterIntensity.Rest);
            float minGap = float.MaxValue;
            var fights = recs.Where(r => r.intensity != EncounterIntensity.Rest).OrderBy(r => r.anchorLogical).ToList();
            for (int i = 1; i < fights.Count; i++) minGap = Mathf.Min(minGap, (float)(fights[i].anchorLogical - fights[i - 1].endLogical));
            int k = results.FindIndex(r => r.name == name);
            if (k < 0) results.Add((name, gm.EnemySpawnRateMultiplier, dir.SpawnFrequency, enc, rest, dir.SpawnedEnemies - sp0, gm.MaxDistance - d0, minGap));
            else { var o = results[k]; results[k] = (name, o.mult, o.freq, o.enc + enc, o.rest + rest, o.enemies + dir.SpawnedEnemies - sp0, o.meters + gm.MaxDistance - d0, Mathf.Min(o.minGap, minGap)); }
            yield return EndRun();
        }
        if (EncounterDirector.Instance != null) typeof(EncounterDirector).GetField("fixedSeed", AnyInst).SetValue(EncounterDirector.Instance, int.MinValue);
        foreach (var r in results)
            L($"[cardfix] C {stage}: {r.name,-26} spawnRate x{r.mult:0.00} -> frequency x{r.freq:0.00}: {r.meters:F0}m, encounters {r.enc} (rest {r.rest}), enemies {r.enemies}, main encounters/km {r.enc / Mathf.Max(0.001f, r.meters / 1000f):0.0}, all enemies/km {r.enemies / Mathf.Max(0.001f, r.meters / 1000f):0.0}, min gap {(r.minGap == float.MaxValue ? 0 : r.minGap):F1}m");
        var x1 = results[0]; var more = results[1]; var ext = results[2];
        float k1 = x1.enc / Mathf.Max(1f, x1.meters), k2 = more.enc / Mathf.Max(1f, more.meters), k3 = ext.enc / Mathf.Max(1f, ext.meters);
        Check(Mathf.Approximately(x1.freq, 1f), $"C {stage}: no spawn-rate card = frequency x1 (unchanged)");
        Check(x1.meters > 3000f && more.meters > 3000f && ext.meters > 3000f, $"C {stage}: each run covered enough distance ({x1.meters:F0}/{more.meters:F0}/{ext.meters:F0}m)");
        // 荒野街道は Encounter の多くが上下ルートの分岐区間(地形の分岐の位置で決まる)なので、出現率の効きが小さい(既知の制約、報告済み)
        bool branchStage = stage == "wasteland_road";
        if (branchStage && !(k2 > k1 * 1.2f)) Warn($"C {stage}: spawn rate has a small effect here (route-pair sections are fixed by terrain forks): main encounters {k1 * 1000f:0.0} -> {k2 * 1000f:0.0} per km, all enemies {x1.enemies / Mathf.Max(1f, x1.meters) * 1000f:0.0} -> {more.enemies / Mathf.Max(1f, more.meters) * 1000f:0.0} per km");
        else Check(more.freq >= 1.5f - 1e-3f && k2 > k1 * 1.2f, $"C {stage}: MORE ENEMIES Lv5 (x{more.mult:0.00}) makes main-route encounters more frequent ({k1 * 1000f:0.0} -> {k2 * 1000f:0.0} per km)");
        Check(Mathf.Abs(ext.freq - 2f) < 0.001f && k3 < k1 * 3f, $"C {stage}: an extreme value is capped at frequency x2 (x{ext.mult:0.0} -> x{ext.freq:0.0}, {k3 * 1000f:0.0} main encounters per km)");
        Check(results.All(r => r.minGap > 0f), $"C {stage}: main-route encounters never overlap (min gap {results.Min(r => r.minGap):F1}m)");
    }

    // ===================================================================== D
    IEnumerator ElementCases()
    {
        yield return BeginRun("swordsman", "wasteland_road");
        WarpTo(3000f);
        yield return new WaitForSecondsRealtime(1.0f);
        ResetRunCardsForTest();
        ElementSystem.ResetCounters();
        var dir = EncounterDirector.Instance;
        var oldRoll = ElementSystem.Roll;

        // D0: 値0なら何も起きない
        var e0 = dir.DebugSpawnEnemy(dir != null ? "goblin" : "", EnemyAiTier.T0);
        yield return null;
        var en0 = e0 != null ? e0.GetComponent<EnemyController>() : null;
        if (en0 != null) ElementSystem.OnPlayerHit(en0, null, 100);
        Check(ElementSystem.BurnProcs + ElementSystem.ChillProcs + ElementSystem.LightningProcs + ElementSystem.WindBlades + ElementSystem.BleedProcs == 0 && (en0 == null || en0.GetComponent<ElementStatus>() == null),
            "D0: with no element values nothing happens (existing cards have none yet)");

        ElementSystem.Roll = () => 0f; // 確率は必ず成功
        TimeControl.Pause(this); // 敵/時間を止めて、状態だけ確かめる(継続ダメージは最後に時間を流して確かめる)
        var enemies = new List<EnemyController>();
        foreach (var id in new[] { "goblin", "goblin", "goblin", "goblin" })
        {
            var go = dir.DebugSpawnEnemy(id, EnemyAiTier.T2);
            if (go != null) enemies.Add(go.GetComponent<EnemyController>());
        }
        // 近くに並べる(連鎖の範囲内)
        for (int i = 0; i < enemies.Count; i++) { var p = enemies[i].transform.position; p.x = pc.transform.position.x + 6f + i * 1.6f; enemies[i].transform.position = p; }
        Check(enemies.Count == 4, $"D: test enemies spawned ({enemies.Count})");
        if (enemies.Count < 4) { ElementSystem.Roll = oldRoll; TimeControl.Resume(this); yield return EndRun(); yield break; }
        var elems = gm.Elements;

        // 氷: Chill → 行動の時間が遅くなる、3回で凍結(雑魚は0)
        CardBalanceTest.ToggleElementTest(gm, 1, true);
        var a = enemies[0];
        ElementSystem.OnPlayerHit(a, null, 100);
        var st = a.GetComponent<ElementStatus>();
        Check(st != null && st.Chilled && Mathf.Abs(st.TimeScale - 0.5f) < 0.01f, $"D-ice: chill slows the enemy's actions (time x{(st != null ? st.TimeScale : -1):0.00})");
        ElementSystem.OnPlayerHit(a, null, 100); ElementSystem.OnPlayerHit(a, null, 100);
        Check(st.Frozen && st.TimeScale == 0f && ElementSystem.FreezeProcs == 1, $"D-ice: 3 chills freeze the enemy (time x{st.TimeScale}, freezes {ElementSystem.FreezeProcs})");
        CardBalanceTest.ToggleElementTest(gm, 1, false);

        // 雷: 当たった敵 + 近くの3体へ連鎖
        CardBalanceTest.ToggleElementTest(gm, 2, true);
        foreach (var en in enemies) typeof(EnemyController).GetMethod("EnsureHp", AnyInst).Invoke(en, null);
        var hp0 = enemies.Select(en => (int)typeof(EnemyController).GetField("hp", AnyInst).GetValue(en)).ToArray();
        ElementSystem.OnPlayerHit(enemies[0], null, 100);
        var hp1 = enemies.Select(en => en != null ? (int)typeof(EnemyController).GetField("hp", AnyInst).GetValue(en) : -999).ToArray();
        int damaged = 0; for (int i = 0; i < enemies.Count; i++) if (hp1[i] < hp0[i]) damaged++;
        Check(ElementSystem.LightningProcs == 1 && ElementSystem.LightningHits == 4 && damaged == 4, $"D-thunder: one strike chains to 3 nearby enemies (hits {ElementSystem.LightningHits}, damaged {damaged}/4, hp {string.Join(",", hp0)} -> {string.Join(",", hp1)})");
        CardBalanceTest.ToggleElementTest(gm, 2, false);

        // 風: 前方へ貫通の風刃(風刃自身からは出ない)
        CardBalanceTest.ToggleElementTest(gm, 3, true);
        int w0 = ElementSystem.WindBlades;
        ElementSystem.OnPlayerHit(enemies[1], null, 100);
        var blade = Object.FindObjectsByType<KitProjectile>(FindObjectsSortMode.None).FirstOrDefault(k => k.name == "ElementWindBlade");
        Check(ElementSystem.WindBlades == w0 + 1 && blade != null, "D-wind: a wind blade is fired forward");
        if (blade != null) ElementSystem.OnPlayerHit(enemies[1], blade.GetComponent<PlayerAttackInfo>(), 100);
        Check(ElementSystem.WindBlades == w0 + 1, "D-wind: a wind blade's own hit does not fire another blade");
        if (blade != null) Object.Destroy(blade.gameObject); // この後の確認の敵を巻き込まない
        CardBalanceTest.ToggleElementTest(gm, 3, false);

        // ボス: 冷気は止めずに弱い減速、凍結でも0.5倍
        BossManager.Instance.DebugSpawnBossForTest(0, (int)WildBossKind.Golem);
        yield return null;
        var boss = Object.FindObjectsByType<WildBossBase>(FindObjectsSortMode.None).FirstOrDefault(b => !b.IsDead);
        if (boss != null)
        {
            CardBalanceTest.ToggleElementTest(gm, 1, true);
            ElementSystem.OnPlayerHit(boss, null, 100);
            float s1 = ElementStatus.AiTimeScaleOf(boss.gameObject);
            ElementSystem.OnPlayerHit(boss, null, 100); ElementSystem.OnPlayerHit(boss, null, 100);
            float s2 = ElementStatus.AiTimeScaleOf(boss.gameObject);
            Check(s1 > 0.8f && s1 < 1f && Mathf.Abs(s2 - ElementStatus.BossFreezeScale) < 0.01f, $"D-ice boss: chill is weak (x{s1:0.00}) and freeze becomes a slow, never a stop (x{s2:0.00})");
            CardBalanceTest.ToggleElementTest(gm, 1, false);
            int bhp = boss.Hp;
            boss.TakeElementDamage(50, boss.CenterWorld);
            Check(boss.Hp == bhp - 50, $"D-boss: element damage lowers boss HP quietly ({bhp} -> {boss.Hp})");
        }
        else Warn("D: boss not spawned");

        // 炎 / 血: 時間を流して継続ダメージ・回復を確かめる
        TimeControl.Resume(this);
        stopKeepAlive = true;
        yield return null;
        typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { Mathf.Max(1, gm.maxLives / 2) });
        int lives0 = gm.Lives;
        CardBalanceTest.ToggleElementTest(gm, 0, true);
        CardBalanceTest.ToggleElementTest(gm, 4, true);
        var fresh = dir.DebugSpawnEnemy("goblin", EnemyAiTier.T0);
        var b = fresh != null ? fresh.GetComponent<EnemyController>() : null;
        Check(b != null, "D-fire/blood: test enemy spawned");
        if (b != null)
        {
            var bp = b.transform.position; bp.x = pc.transform.position.x + 70f; b.transform.position = bp; // プレイヤーが2秒で届かない所
            typeof(EnemyController).GetField("hp", AnyInst).SetValue(b, 100000);
            long dmg0 = ElementSystem.ElementDamage;
            ElementSystem.OnPlayerHit(b, null, 200);
            var bs = b.GetComponent<ElementStatus>();
            Check(bs != null && bs.Burning && bs.Bleeding, "D-fire/blood: burn and bleed applied");
            yield return new WaitForSeconds(2.1f);
            long dot = ElementSystem.ElementDamage - dmg0;
            Check(dot > 0, $"D-fire/blood: damage over time ticks ({dot} in ~2s; burn 200x0.3/s + bleed 200x0.25x(1+missing)/s)");
            Check(ElementSystem.BleedHealed > 0, $"D-blood: bleed heals the player ({ElementSystem.BleedHealed} healed, HP {lives0} -> {gm.Lives})");
        }
        CardBalanceTest.ToggleElementTest(gm, 0, false);
        CardBalanceTest.ToggleElementTest(gm, 4, false);
        L($"[cardfix] D: procs burn {ElementSystem.BurnProcs} chill {ElementSystem.ChillProcs} freeze {ElementSystem.FreezeProcs} lightning {ElementSystem.LightningProcs}/{ElementSystem.LightningHits} wind {ElementSystem.WindBlades} bleed {ElementSystem.BleedProcs} dmg {ElementSystem.ElementDamage} healed {ElementSystem.BleedHealed}");
        ElementSystem.Roll = oldRoll;
        stopKeepAlive = false;
        StartCoroutine(KeepAlive());
        yield return EndRun();
    }
}
#endif
