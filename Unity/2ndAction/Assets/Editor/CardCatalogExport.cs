using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// カード資料の書き出し(2026-10-30、マスター依頼「現行100カードの一覧化」)。Editor 専用(製品版には入らない)。
// ゲーム側と同じ式(CardVariant.AbilityEffectTextAt / CardRules.Scale / ComboTuning.I / FinalEvolutionTuning.I)で
// 各カードの値を TSV へ書き出す。資料(Docs/CardCatalog_*)は Tools/cards/catalog.py がこの TSV から作る。
//   batchmode: -executeMethod CardCatalogExport.Run -ccOut <dir>
//   メニュー:  Tools/OneMoreMile/Export Card Catalog(Unity/2ndAction/Logs/CardCatalog へ)
public static class CardCatalogExport
{
    [MenuItem("Tools/OneMoreMile/Export Card Catalog")]
    static void Menu() => Export(Path.Combine(Application.dataPath, "../Logs/CardCatalog"));

    public static void Run()
    {
        var a = System.Environment.GetCommandLineArgs();
        string dir = Path.Combine(Application.dataPath, "../Logs/CardCatalog");
        for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-ccOut") dir = a[i + 1];
        Export(dir);
    }

    static string Clean(string s) => (s ?? "").Replace("\t", " ").Replace("\r", "").Replace("\n", " / ");

    static void Export(string dir)
    {
        Directory.CreateDirectory(dir);
        CardDatabase.Reset();
        var cards = CardDatabase.AllCards;
        var combos = ComboTuning.I.combos.Where(c => c != null).ToList();
        var fe = FinalEvolutionTuning.I;
        var reqSac = typeof(GameManager).GetMethod("RequiredSacrifice", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var sb = new StringBuilder();
        sb.Append("no\tid\tname\trarity\tcategory\telement\tsortOrder\trecommendPriority\tunlockDistance\tgachaStage\tmaxLv\tdescription\tlv1\tlv9\teffects\t" +
                  "elementEffects\tlv1Element\tlv9Element\tcombos\tfeTitle\tfeDesc\tfeModule\tfeKind\tfeDuration\tfeAmplify\tfeBonuses\tsacrificeLv1\tsacrificeLv9\tbehaviorLevel\n");
        int no = 0;
        foreach (var c in cards)
        {
            no++;
            string eff = string.Join(" ; ", c.effects.Select(e => $"{e.type}:{e.value.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{e.scaling}"));
            string elemEff = string.Join(",", c.effects.Where(e => ElementStats.IsElementEffect(e.type) || IsElementLevel(e.type)).Select(e => e.type.ToString()).Distinct());
            string inCombos = string.Join(",", combos.Where(k => k.abilities != null && k.abilities.Contains(c.cardId)).Select(k => k.displayName));
            var f = c.cardId == UltimateArt.CardId ? null : fe.For(c.cardId);
            string feDur = f == null ? "" : f.kind == FinalEvolutionTuning.Kind.Time ? $"{f.durationSeconds:0.#}s" : $"{f.durationMeters:0}m";
            string feB = f == null ? "" : string.Join(",", f.bonuses.Select(b => $"{b.type}:{b.value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
            int s1 = 0, s9 = 0;
            if (reqSac != null) { s1 = (int)reqSac.Invoke(null, new object[] { c, 1 }); s9 = (int)reqSac.Invoke(null, new object[] { c, 9 }); }
            bool behavior = c.effects.Any(e => CardEffectFormat.IsBehaviorLevel(e.type));
            sb.Append(no).Append('\t').Append(c.cardId).Append('\t').Append(Clean(c.cardName)).Append('\t').Append(c.rarity).Append('\t').Append(c.category)
              .Append('\t').Append(c.element).Append('\t').Append(c.sortOrder).Append('\t').Append(c.recommendPriority).Append('\t').Append(c.unlockDistance)
              .Append('\t').Append(c.gachaStage).Append('\t').Append(CardVariant.MaxLevel).Append('\t').Append(Clean(c.description))
              .Append('\t').Append(Clean(CardVariant.AbilityEffectTextAt(c.cardId, 1))).Append('\t').Append(Clean(CardVariant.AbilityEffectTextAt(c.cardId, 9)))
              .Append('\t').Append(eff).Append('\t').Append(elemEff).Append('\t').Append(ElementAlone(c, 1)).Append('\t').Append(ElementAlone(c, 9))
              .Append('\t').Append(inCombos).Append('\t').Append(Clean(f?.title)).Append('\t').Append(Clean(f?.description)).Append('\t').Append(f != null ? f.module.ToString() : "")
              .Append('\t').Append(f != null ? f.kind.ToString() : "").Append('\t').Append(feDur).Append('\t').Append(f != null ? f.amplify.ToString("0.##") : "")
              .Append('\t').Append(feB).Append('\t').Append(s1).Append('\t').Append(s9).Append('\t').Append(behavior ? 1 : 0).Append('\n');
        }
        File.WriteAllText(Path.Combine(dir, "cards.tsv"), sb.ToString(), new UTF8Encoding(false));

        var cb = new StringBuilder("id\tname\tmodule\tabilities\tdescription\tenhanced\tpower\tradius\tcooldown\tchance\tcount\tlv1Factor\trisk\tenabled\n");
        foreach (var k in combos)
            cb.Append(k.id).Append('\t').Append(k.displayName).Append('\t').Append(k.module).Append('\t').Append(string.Join("+", k.abilities)).Append('\t').Append(Clean(k.description))
              .Append('\t').Append(Clean(k.enhancedDescription)).Append('\t').Append(k.power).Append('\t').Append(k.radius).Append('\t').Append(k.cooldown).Append('\t').Append(k.procChance)
              .Append('\t').Append(k.count).Append('\t').Append(k.lv1Factor).Append('\t').Append(k.risk ? 1 : 0).Append('\t').Append(k.enabled ? 1 : 0).Append('\n');
        File.WriteAllText(Path.Combine(dir, "combos.tsv"), cb.ToString(), new UTF8Encoding(false));


        float[] w = { 50f, 30f, 15f, 4f, 1f };
        var gs = new StringBuilder("key\tvalue\n");
        gs.Append("gachaRarityWeights\t").Append(string.Join(",", w)).Append('\n');
        gs.Append("gachaThresholds\t").Append(string.Join(",", GachaStage.Thresholds)).Append('\n');
        gs.Append("deckCapacity\t").Append(GameManager.DeckCapacity).Append('\n');
        gs.Append("feReadyMeters\t").Append(fe.readyMeters).Append('\n');
        File.WriteAllText(Path.Combine(dir, "settings.tsv"), gs.ToString(), new UTF8Encoding(false));
        Debug.Log($"[CardCatalog] {cards.Count} cards, {combos.Count} combos -> {dir}");
    }

    static bool IsElementLevel(EffectType t) => t == EffectType.InfernoLevel || t == EffectType.AbsoluteZeroLevel || t == EffectType.TornadoLevel || t == EffectType.BloodBladeLevel || t == EffectType.FlameCounterLevel;

    // そのカード1枚だけ(Lv)で、各属性の発動の条件(ElementStats.AnyActive と同じ組み合わせ)が満たされるか
    static string ElementAlone(CardDefinition c, int lv)
    {
        var v = new Dictionary<EffectType, float>();
        foreach (var e in c.effects) v[e.type] = (v.TryGetValue(e.type, out float x) ? x : 0f) + CardRules.Scale(e.scaling, e.value, lv);
        float G(EffectType t) => v.TryGetValue(t, out float x) ? x : 0f;
        var on = new List<string>();
        if (G(EffectType.BurnChance) > 0f && G(EffectType.BurnPower) > 0f) on.Add("炎上");
        if (G(EffectType.ChillChance) > 0f && G(EffectType.ChillSlow) > 0f) on.Add("冷気");
        if (G(EffectType.LightningChance) > 0f && G(EffectType.LightningPower) > 0f) on.Add("落雷");
        if (G(EffectType.WindBladeChance) > 0f && G(EffectType.WindBladePower) > 0f) on.Add("風刃");
        if (G(EffectType.WindPierce) > 0f) on.Add("風の貫通");
        if (G(EffectType.BleedChance) > 0f && G(EffectType.BleedPower) > 0f) on.Add("出血");
        return string.Join(",", on);
    }
}
