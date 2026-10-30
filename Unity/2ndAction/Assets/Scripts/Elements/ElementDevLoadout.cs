using UnityEngine;

// 属性の確認用(2026-10-30、開発版だけ): ランの開始時に属性のカードを持たせる。既存の試験(-qaMapFinale / -ldQa / -netAuto …)を
// 属性ありで走らせて、表示の負荷・例外を確かめるため。製品版では何もしない。
//   -elemLoadout all9            … 5属性の系統のカードを全部 Lv9
//   -elemLoadout fire9,ice1e     … 炎の系統を Lv9 / 氷は入口のカード(FROST EDGE)だけ Lv1(末尾 e = 入口だけ)
public static class ElementDevLoadout
{
    public struct Family { public string key; public ElementType type; public string[] entry, family; }
    public static readonly Family[] Families =
    {
        new Family { key = "fire", type = ElementType.Fire, entry = new[] { "flame_blade" }, family = new[] { "flame_blade", "burning_soul", "inferno", "flame_counter" } },
        new Family { key = "ice", type = ElementType.Ice, entry = new[] { "frost_edge" }, family = new[] { "frost_edge", "ice_prison", "absolute_zero" } },
        new Family { key = "thunder", type = ElementType.Thunder, entry = new[] { "thunder_strike" }, family = new[] { "thunder_strike", "chain_lightning", "high_voltage", "thunder_lord" } },
        new Family { key = "wind", type = ElementType.Wind, entry = new[] { "wind_cutter" }, family = new[] { "wind_cutter", "gale", "tornado" } },
        // 血の「入口」は命中で効く出血(BLOOD BLADE)。VAMPIRE は撃破で判定する吸収
        // 2026-10-31 カード拡張: 新しい 3 枚だけ(…n)/ 既存+新しい全部(…a)
        new Family { key = "firen", type = ElementType.Fire, entry = new[] { "scorch" }, family = new[] { "scorch", "fire_whirl", "combustion" } },
        new Family { key = "icen", type = ElementType.Ice, entry = new[] { "permafrost" }, family = new[] { "permafrost", "glacier_guard", "shatter_strike" } },
        new Family { key = "thundern", type = ElementType.Thunder, entry = new[] { "conductor" }, family = new[] { "conductor", "volt_surge", "judgment_bolt" } },
        new Family { key = "windn", type = ElementType.Wind, entry = new[] { "slipstream" }, family = new[] { "slipstream", "updraft", "tempest" } },
        new Family { key = "firea", type = ElementType.Fire, entry = new[] { "flame_blade" }, family = new[] { "flame_blade", "burning_soul", "inferno", "flame_counter", "scorch", "fire_whirl", "combustion" } },
        new Family { key = "icea", type = ElementType.Ice, entry = new[] { "frost_edge" }, family = new[] { "frost_edge", "ice_prison", "absolute_zero", "permafrost", "glacier_guard", "shatter_strike" } },
        new Family { key = "thundera", type = ElementType.Thunder, entry = new[] { "thunder_strike" }, family = new[] { "thunder_strike", "chain_lightning", "high_voltage", "thunder_lord", "conductor", "volt_surge", "judgment_bolt" } },
        new Family { key = "winda", type = ElementType.Wind, entry = new[] { "wind_cutter" }, family = new[] { "wind_cutter", "gale", "tornado", "slipstream", "updraft", "tempest" } },
        new Family { key = "blood", type = ElementType.Blood, entry = new[] { "blood_blade" }, family = new[] { "vampire", "predator", "overheal", "blood_rush", "blood_blade", "phoenix" } },
    };

    public static string LastApplied = "";

    // spec を今のランのカードへ(ゲームのカード適用そのまま。能力ごとの Lv9 上限も通る)
    public static int ApplySpec(GameManager gm, string spec)
    {
        int n = 0;
        if (gm == null || string.IsNullOrEmpty(spec)) return 0;
        foreach (var raw in spec.Split(','))
        {
            string item = raw.Trim().ToLowerInvariant();
            if (item.Length < 2) continue;
            bool entryOnly = item.EndsWith("e") && char.IsDigit(item[item.Length - 2]); // 「fire9e」= 入口だけ(「fire」「ice」の e ではない)
            if (entryOnly) item = item.Substring(0, item.Length - 1);
            int digits = 0; while (digits < item.Length && char.IsDigit(item[item.Length - 1 - digits])) digits++;
            int lv = digits > 0 ? int.Parse(item.Substring(item.Length - digits)) : 9;
            string key = item.Substring(0, item.Length - digits);
            foreach (var f in Families)
            {
                // "all" = 既存の 5 系統(2026-10-30 の試験と同じ)/ "allx" = 新しいカードも含めた全部(2026-10-31)
                bool expansion = f.key.EndsWith("n") || f.key.EndsWith("a");
                if (key == "all" ? expansion : key == "allx" ? !(f.key.EndsWith("a") || f.key == "blood") : key != f.key) continue;
                foreach (var id in entryOnly ? f.entry : f.family)
                {
                    var c = CardDatabase.FindBaseById(id);
                    if (c == null) continue;
                    gm.ApplyCardEffectsStacked(c, Mathf.Clamp(lv, 1, 9));
                    n++;
                }
            }
        }
        LastApplied = spec;
        Debug.Log($"[ElementDevLoadout] {spec}: {n} cards");
        return n;
    }

    // ランの開始時(GameManager.ApplyCharacterCardEffects の後)
    public static void ApplyAtRunStart(GameManager gm)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!Debug.isDebugBuild) return;
        var a = QaArgs.All;
        for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-elemLoadout") ApplySpec(gm, a[i + 1]);
        for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-elemPerfLog" && a[i + 1] == "1") { var _ = ElementVfx.I; } // 属性なしの比較でも fps の記録を出す
#endif
    }
}
