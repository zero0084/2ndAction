#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// カード(10倍化・Lv9上限の後)がキャラへ正しく反映され、ボスと戦いになっているかの確認(2026-10-02)。 -qaCardBoss <dir> [-qaCbChars a,b] [-qaCbPlan 20]
//  1) キャラごとに13枚(デッキ10+キャラカード3相当)を Lv3/7/9 まで取り、能力値がカードの値どおりに増えているか
//  2) 10km(Lv3)・40km(Lv7)・90km(Lv9)の距離で、その距離の節目ボスと実際に戦う(ボットが殴る)。
//     倒すまでの時間/当てた回数/1発の平均と最大/受けたダメージ/段階/必殺技/BREAK を記録する
//  3) -qaCbPlan 20 を付けると、ボスHPの再設計案(20発)でも同じ戦闘をする(比較用)
public partial class QaSweep
{
    static readonly string[] CbDeck = { "attack_up", "thunder_strike", "flame_blade", "high_voltage", "ground_breaker", "heavy_impact", "boss_killer", "hunter", "momentum", "overdrive" };
    static readonly string[] CbChar = { "combo_master", "iron_will", "ultimate" };
    static readonly (float d, int lv, WildBossKind kind)[] CbFights = { (10000f, 3, WildBossKind.Serpent), (40000f, 7, WildBossKind.Golem), (90000f, 9, WildBossKind.BlackKnight) };

    IEnumerator CardBossMode()
    {
        Application.targetFrameRate = 60;
        var chars = Arg("-qaCbChars", string.Join(",", CharacterDatabase.AllCharacters.Select(c => c.characterId))).Split(',');
        int plan = int.Parse(Arg("-qaCbPlan", "0"));
        // ---- 0) 全カード開放(開発版のボタンと同じ処理。テスト機の保存は最後に戻す)
        {
            var snap = SaveSystem.Capture();
            bool wasOpen = GachaStage.DevAllCardsOpen;
            GachaStage.DevAllCardsOpen = false;
            int before = CardDatabase.UnlockedCards.Count;
            GachaStage.DevAllCardsOpen = true;
            int after = CardDatabase.UnlockedCards.Count;
            int added = CardInventory.DebugOwnEveryMissing();
            bool allOwned = CardDatabase.AllCards.All(c => CardInventory.GetTotalCount(c.cardId) > 0);
            int again = CardInventory.DebugOwnEveryMissing();
            int lv9 = CardInventory.DebugOwnEveryAtLevel(9, 3);
            bool allLv9 = CardDatabase.AllCards.All(c => CardInventory.DebugCountAtLevel(c.cardId, 9) >= 3);
            int lv9Again = CardInventory.DebugOwnEveryAtLevel(9, 3);
            allLv9 &= lv9Again == 0;
            L($"[unlock] unlocked {before} -> {after}/{CardDatabase.AllCards.Count}, owned missing +{added}, all owned={allOwned}, second press +{again}, Lv9x3 added {lv9} kinds, all Lv9x3={allLv9}");
            Check(after == CardDatabase.AllCards.Count && allOwned && again == 0 && allLv9, "debug: unlock-all opens and gives every card (Lv1, and Lv9 x3)");
            GachaStage.DevAllCardsOpen = wasOpen;
            SaveSystem.Restore(snap);
            CardInventory.ReloadFromPrefs();
        }
        var table = new List<string>();
        foreach (int hits in plan > 0 ? new[] { 0, plan } : new[] { 0 })
        {
            BossHpPlan.Hits = hits;
            var list = hits > 0 ? chars.Where(c => c == "swordsman" || c == "archer" || c == "fighter" || c == "miko").ToArray() : chars;
            foreach (string ch in list) yield return CardBossChar(ch, hits, table);
        }
        BossHpPlan.Hits = 0;
        BossManager.SuppressGates = false;
        L("");
        L("== まとめ(ボスHP: 現行/案, 距離, Lv, ボス, 結果) ==");
        foreach (var t in table) L(t);
    }

    IEnumerator CardBossChar(string ch, int planHits, List<string> table)
    {
        yield return BeginRun(ch, "wasteland_road");
        stopKeepAlive = true; // HPは自分で見る(満HPの条件を正しく)
        var bm = BossManager.Instance; bm.enabled = true;
        BossManager.SuppressGates = true;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        var NPS = BindingFlags.NonPublic | BindingFlags.Instance;
        var deck = typeof(GameManager).GetField("deckCards", NPS).GetValue(gm) as List<string>;
        deck.Clear(); deck.AddRange(CbDeck);
        var pick = typeof(GameManager).GetMethod("ApplyUpgradeByCardId", NPS);
        var def = CharacterDatabase.FindById(ch);
        // 被弾の記録
        int dmgTaken = 0, hitsTaken = 0;
        System.Action<string> tap = m =>
        {
            if (!m.StartsWith("[Damage] Hit reason=")) return;
            int i = m.IndexOf("amount="); if (i < 0) return;
            int j = m.IndexOf(' ', i); int.TryParse(m.Substring(i + 7, (j < 0 ? m.Length : j) - i - 7), out int a);
            dmgTaken += a; hitsTaken++;
        };
        FreezeDiagnostics.EventTap += tap;
        string all = CbDeck.Concat(CbChar).Aggregate((a, b) => a + "," + b);
        int lvNow = 0;
        foreach (var f in CbFights)
        {
            // ---- 1) カードを取って、値が反映されているか
            int add = f.lv - lvNow;
            foreach (string id in CbDeck.Concat(CbChar)) for (int i = 0; i < add; i++) pick.Invoke(gm, new object[] { id });
            lvNow = f.lv;
            var sum = new Dictionary<EffectType, float>();
            foreach (string id in CbDeck.Concat(CbChar))
            {
                int st = gm.GetCurrentRunStack(id);
                foreach (var e in CardDatabase.FindById(id).effects) sum[e.type] = (sum.TryGetValue(e.type, out float v) ? v : 0f) + e.value * st;
            }
            float S(EffectType t) => sum.TryGetValue(t, out float v) ? v : 0f;
            bool stacksOk = CbDeck.Concat(CbChar).All(id => gm.GetCurrentRunStack(id) >= f.lv && gm.GetCurrentRunStack(id) <= GameManager.MaxRunCardLevel);
            bool valsOk = pc.AttackPower == def.attackPower + Mathf.RoundToInt(S(EffectType.AttackPower))
                && pc.GroundAttackPowerBonus == Mathf.RoundToInt(S(EffectType.GroundAttackPower))
                && pc.BossDamageBonus == Mathf.RoundToInt(S(EffectType.BossDamageBonus))
                && pc.MomentumBonus == Mathf.RoundToInt(S(EffectType.MomentumBonus))
                && pc.ComboFinalStageBonus == Mathf.RoundToInt(S(EffectType.ComboFinalStageBonus))
                && pc.FullHpAttackBonus == Mathf.RoundToInt(S(EffectType.FullHpAttackBonus))
                && gm.maxLives == Mathf.Clamp(def.baseMaxLives + Mathf.RoundToInt(S(EffectType.MaxHp)), 1, gm.maxLivesCap);
            L($"[cb:{ch}] Lv{f.lv}: atk {pc.AttackPower} (base {def.attackPower}+{S(EffectType.AttackPower)}) gnd +{pc.GroundAttackPowerBonus} boss +{pc.BossDamageBonus} mom {pc.MomentumBonus} fin +{pc.ComboFinalStageBonus} fullHp +{pc.FullHpAttackBonus} maxHp {gm.maxLives} speed {GameManager.SpeedKmh(pc.runSpeed):F0}km/h(基本) stacks ok={stacksOk}");
            Check(stacksOk && valsOk, $"{ch} Lv{f.lv}: card values are applied to the character exactly");

            // ---- 2) その距離で節目ボスと戦う
            gm.DebugWarpToDistance(f.d - 60f);
            yield return new WaitForSeconds(1.2f);
            foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
            var livesSet = typeof(GameManager).GetProperty("Lives").GetSetMethod(true);
            livesSet.Invoke(gm, new object[] { gm.maxLives });
            int estOne = pc.BossHitEstimate;
            bm.NetTestSpawnWild(f.kind, 1);
            WildBossBase boss = null; float w = 0f;
            while (boss == null && w < 5f) { yield return null; w += Time.unscaledDeltaTime; boss = FindObjectsByType<WildBossBase>(FindObjectsSortMode.None).FirstOrDefault(b => !b.IsDead); }
            if (boss == null) { Check(false, $"{ch}: boss {f.kind} spawned"); continue; }
            int maxHp = boss.maxHp, last = boss.Hp, hits = 0, maxPhase = 1, maxHit = 0; long dealt = 0;
            dmgTaken = 0; hitsTaken = 0;
            float t0 = Time.time, lastFlick = 0f;
            var dmgs = new List<int>();
            while (!boss.IsDead && Time.time - t0 < 75f)
            {
                yield return null;
                if (boss.Hp < last) { int dd = last - boss.Hp; hits++; dealt += dd; dmgs.Add(dd); maxHit = Mathf.Max(maxHit, dd); last = boss.Hp; }
                maxPhase = Mathf.Max(maxPhase, boss.Phase);
                if (gm.Lives < gm.maxLives / 3) livesSet.Invoke(gm, new object[] { gm.maxLives }); // 倒れないように(受けた量は数える)
                if (Time.time - lastFlick > 0.15f)
                {
                    lastFlick = Time.time;
                    float dx = boss.CenterWorld.x - pc.transform.position.x, dy = boss.CenterWorld.y - pc.transform.position.y;
                    StartCoroutine(Flick(dy > 2.2f ? PlayerController.FlickDirection.Up : dx < -0.5f ? PlayerController.FlickDirection.Backward : PlayerController.FlickDirection.Forward));
                }
            }
            if (boss.IsDead && boss.Hp < last) { hits++; dealt += last; dmgs.Add(last); }
            float secs = Time.time - t0;
            bool killed = boss.IsDead;
            int avg = hits > 0 ? (int)(dealt / hits) : 0;
            string res = $"{(planHits > 0 ? $"案{planHits}発" : "現行")} {ch,-13} {f.d / 1000f,3:0}km Lv{f.lv} {f.kind,-11} HP{maxHp,7:N0} | {(killed ? "撃破" : "未撃破")} {secs,5:F1}s {hits,3}発 平均{avg,6:N0} 最大{maxHit,6:N0}(見積り{estOne:N0}) 被弾{hitsTaken}回/{dmgTaken}HP(最大HP{gm.maxLives}) 段階{maxPhase}/{boss.PhaseCount} 必殺{boss.UltimatesUsed} BREAK{boss.BreakCount}";
            L("[cb] " + res);
            table.Add(res);
            if (!killed) { boss.TakeDamage(99999, boss.CenterWorld); }
            w = 0f; while (bm.IsBossPhase && w < 20f) { yield return null; w += Time.unscaledDeltaTime; }
            yield return new WaitForSeconds(0.5f);
        }
        FreezeDiagnostics.EventTap -= tap;
        BossManager.SuppressGates = false;
        yield return EndRun();
    }
}
#endif
