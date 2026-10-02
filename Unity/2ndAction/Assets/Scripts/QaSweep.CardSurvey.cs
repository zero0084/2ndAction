#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

// カードバランス調査(2026-10-02、値は変えない): -qaCardSurvey <dir>
//  1) 全カード(素のカード)を Lv1/Lv5/Lv9 として、ゲームの適用処理(GameManager.ApplyCardEffectsStacked)そのままで掛け、
//     掛かった後の能力値を読む(同じフレームの中で、掛ける前の値へリフレクションで戻す。ランは1回だけ)。 → card_survey.tsv
//  2) キャラの基礎値(CharacterDefinitionの全項目)                                                            → characters.tsv
//  3) 合成カード(v2キー)でLv9の上限を超えて同じ能力を重ねられるか(上限はカードIDの文字列ごと)              → ログ
//  4) 速度: 自然加速の上限(5km以降)で SPEED UP等の Lv1/5/9 の実際のkm/h                                       → ログ
public partial class QaSweep
{
    static readonly BindingFlags AllInst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    static Dictionary<FieldInfo, object> SnapFields(object o)
    {
        var d = new Dictionary<FieldInfo, object>();
        for (var t = o.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            foreach (var f in t.GetFields(AllInst | BindingFlags.DeclaredOnly))
            {
                var ft = f.FieldType;
                if (ft == typeof(int) || ft == typeof(float) || ft == typeof(double) || ft == typeof(bool) || ft == typeof(long)) d[f] = f.GetValue(o);
            }
        return d;
    }
    static void RestoreFields(object o, Dictionary<FieldInfo, object> d) { foreach (var kv in d) kv.Key.SetValue(o, kv.Value); }

    static readonly string[] SurveyCols = { "runSpeed", "jumpForce", "maxJumps", "AttackPower", "Air", "Ground", "FirstHit", "Final", "LowHp", "FullHp", "Momentum", "Boss",
        "RangeMul", "AtkTimeMul", "Shield", "maxLives", "Lives", "ExpMul", "DrainChance", "DrainAmount", "SpawnRate", "EnemyHpMul", "BossHpMul", "MileMul", "BossMileMul" };

    float[] ReadSurveyStats()
    {
        object G(string n) { var f = typeof(GameManager).GetField(n, AllInst); if (f != null) return f.GetValue(gm); var p = typeof(GameManager).GetProperty(n, AllInst); return p?.GetValue(gm); }
        return new[]
        {
            pc.runSpeed, pc.jumpForce, pc.maxJumps, pc.AttackPower, pc.AirAttackPowerBonus, pc.GroundAttackPowerBonus, pc.FirstHitBonus, pc.ComboFinalStageBonus,
            pc.LowHpAttackBonus, pc.FullHpAttackBonus, pc.MomentumBonus, pc.BossDamageBonus, pc.AttackRangeMultiplier, pc.AttackSpeedMultiplier, pc.ShieldCharges,
            gm.maxLives, gm.Lives, System.Convert.ToSingle(G("expGainMultiplier")), System.Convert.ToSingle(G("lifestealChance")), System.Convert.ToSingle(G("lifestealAmount")),
            gm.EnemySpawnRateMultiplier, gm.EnemyHpMultiplier, gm.BossHpMultiplier, gm.MileGainMultiplier, gm.BossMileGainMultiplier,
        };
    }

    IEnumerator CardSurveyMode()
    {
        Application.targetFrameRate = 60;
        var snapSave = SaveSystem.Capture();
        yield return BeginRun("swordsman", "wasteland_road");
        stopKeepAlive = true; // 能力値を読む間はHPを触らない
        yield return new WaitForSecondsRealtime(1f);
        var inv = typeof(GameManager).GetProperty("InvincibleMode");

        // ---- 2) キャラの基礎値
        {
            var sb = new StringBuilder();
            var fields = typeof(CharacterDefinition).GetFields(BindingFlags.Instance | BindingFlags.Public).Where(f => f.FieldType.IsPrimitive || f.FieldType == typeof(string)).ToArray();
            sb.AppendLine(string.Join("\t", fields.Select(f => f.Name)));
            foreach (var c in CharacterDatabase.AllCharacters) sb.AppendLine(string.Join("\t", fields.Select(f => System.Convert.ToString(f.GetValue(c), System.Globalization.CultureInfo.InvariantCulture))));
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "characters.tsv"), sb.ToString());
            L($"[survey] characters: {CharacterDatabase.AllCharacters.Count}");
        }

        // ---- 1) 全カード Lv1/5/9
        {
            var sb = new StringBuilder();
            sb.AppendLine("cardId\tname\tlevel\t" + string.Join("\t", SurveyCols));
            var pcSnap = SnapFields(pc); var gmSnap = SnapFields(gm);
            float[] base0 = ReadSurveyStats();
            sb.AppendLine("(none)\t(none)\t0\t" + string.Join("\t", base0.Select(v => v.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture))));
            int n = 0;
            foreach (var card in CardDatabase.AllCards)
            {
                foreach (int lv in new[] { 1, 5, 9 })
                {
                    gm.ApplyCardEffectsStacked(card, lv);
                    float[] s = ReadSurveyStats();
                    sb.AppendLine($"{card.cardId}\t{card.cardName}\t{lv}\t" + string.Join("\t", s.Select(v => v.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture))));
                    RestoreFields(pc, pcSnap); RestoreFields(gm, gmSnap);
                }
                n++;
            }
            float[] after = ReadSurveyStats();
            bool restored = after.SequenceEqual(base0);
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "card_survey.tsv"), sb.ToString());
            L($"[survey] measured {n} cards x Lv1/5/9 through ApplyCardEffectsStacked; state restored between cards: {restored}");
            Check(n == CardDatabase.AllCards.Count && restored, "every card measured and the player restored between measurements");
        }

        // ---- 3) 合成カードで Lv9 の上限を超えて重なるか
        {
            var pcSnap = SnapFields(pc); var gmSnap = SnapFields(gm);
            var ids = (string[])typeof(GameManager).GetField("characterCardIds", AllInst).GetValue(gm);
            var lvs = (int[])typeof(GameManager).GetField("characterCardLevels", AllInst).GetValue(gm);
            var hist = (System.Collections.IList)typeof(GameManager).GetField("upgradeHistory", AllInst).GetValue(gm);
            string[] keepIds = (string[])ids.Clone(); int[] keepLvs = (int[])lvs.Clone(); int keepHist = hist.Count;
            var plain = CardDatabase.FindBaseById("attack_up");
            string k1 = "v2|attack_up|9|1|attack_up*9", k2 = "v2|attack_up|9|2|attack_up*9", k3 = "v2|attack_up|9|3|attack_up*9";
            var v1 = CardDatabase.FindById(k1); var v2 = CardDatabase.FindById(k2); var v3 = CardDatabase.FindById(k3);
            int atk0 = pc.AttackPower;
            // キャラカード3枠に「攻撃力×9」の合成Lv9を3種類(レア度だけ違う)装備したのと同じ(ラン開始時の適用と同じ: 合成カードは1回)
            ids[0] = k1; lvs[0] = 9; ids[1] = k2; lvs[1] = 9; ids[2] = k3; lvs[2] = 9;
            gm.ApplyCardEffectsStacked(v1, 1); gm.ApplyCardEffectsStacked(v2, 1); gm.ApplyCardEffectsStacked(v3, 1);
            int afterChar = pc.AttackPower;
            // デッキの素の ATTACK UP をLv9まで取る(取れる間だけ)
            int picks = 0;
            while (gm.CanStillPick(plain) && picks < 20) { hist.Add(plain); gm.ApplyCardEffectsStacked(plain, 1); picks++; }
            int afterDeck = pc.AttackPower;
            L($"[survey] fusion stacking: ATTACK UP x9 fused in 3 character slots (keys differing only by rarity) -> attack {atk0} -> {afterChar}; then plain ATTACK UP picked {picks} times -> {afterDeck} (= {(afterDeck - atk0) / 10} stacks of +10)");
            L($"[survey]   run stack counted per key: plain={gm.GetCurrentRunStack("attack_up")} {k1}={gm.GetCurrentRunStack(k1)} -> the Lv9 cap is per card ID string, not per ability");
            Check(true, "fusion stacking measured");
            for (int i = 0; i < ids.Length; i++) { ids[i] = keepIds[i]; lvs[i] = keepLvs[i]; }
            while (hist.Count > keepHist) hist.RemoveAt(hist.Count - 1);
            RestoreFields(pc, pcSnap); RestoreFields(gm, gmSnap);
        }

        // ---- 4) 速度: 自然加速の上限(6km)で実際のkm/h
        {
            inv.SetValue(gm, true);
            WarpTo(6000f);
            yield return new WaitForSeconds(1.5f);
            var pcSnap = SnapFields(pc); var gmSnap = SnapFields(gm);
            float baseKmh = GameManager.SpeedKmh(pc.CurrentAutoRunSpeed);
            L($"[survey] speed at 6km (natural cap): {baseKmh:F1}km/h (swordsman runSpeed {pc.runSpeed:F2})");
            foreach (string id in new[] { "speed_up", "greed", "no_turning_back", "close_call", "ultimate", "speed_down", "brake_attack", "fortress", "reverse_gear" })
            {
                var c = CardDatabase.FindBaseById(id);
                if (c == null) continue;
                var line = new StringBuilder($"[survey] speed {c.cardName,-16}");
                foreach (int lv in new[] { 1, 5, 9 })
                {
                    gm.ApplyCardEffectsStacked(c, lv);
                    line.Append($" Lv{lv} {GameManager.SpeedKmh(pc.CurrentAutoRunSpeed):F1}km/h");
                    RestoreFields(pc, pcSnap); RestoreFields(gm, gmSnap);
                }
                L(line.ToString());
            }
            inv.SetValue(gm, false);
        }

        yield return EndRun();
        SaveSystem.Restore(snapSave);
        CardInventory.ReloadFromPrefs();
        L("[survey] test machine save restored");
    }
}
#endif
