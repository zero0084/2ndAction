#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

// ガチャのレア度の確率(2026-11-14 マスター決定: 無いレア度の分はその分類で引ける一番低いレア度へ)の試験と確率表: -qaGachaOdds <dir>
//  T 5分類 × ガチャLv(BEST の距離)× マップでの到達距離 の組み合わせごとに、対象のカードを本物のカードの表と解禁の判定から作り、
//    変更前(451: 対象のレア度の間で重みを配り直す)と変更後の確率表を出す(gacha_odds_table.md)。成立しない条件(GachaOddsIssue)も出す
//  S 表 = 抽選: 組み合わせごとに、レア度の乱数を 0..1 に等間隔で 20000 回並べて本物の抽選(DrawFromGachaPool)へ通し、
//    出たレア度の回数が表 × 20000 と ±1 以内で一致すること(乱数に頼らない確かめ方)。出たカードが対象の分類/対象のカードであること
//  R 今のデータ(テスト用データで全部解禁)で、画面の抽選の入口(GachaPullOnce)と同じ対象・表になっていること + 固定の種で 5000 回の参考値
public partial class QaSweep
{
    static readonly MethodInfo GoDraw3 = typeof(GameManager).GetMethod("DrawFromGachaPool", BindingFlags.NonPublic | BindingFlags.Instance, null,
        new[] { typeof(List<CardDefinition>), typeof(float), typeof(float) }, null);
    static readonly MethodInfo GoPoolFor = typeof(GameManager).GetMethod("BuildGachaPoolFor", BindingFlags.NonPublic | BindingFlags.Instance);

    static readonly CardMapCategory[] GoCats = { CardMapCategory.Common, CardMapCategory.Wilderness, CardMapCategory.Cave, CardMapCategory.Sky, CardMapCategory.LastDungeon };
    static string GoName(CardMapCategory k) => k switch { CardMapCategory.Common => "共通", CardMapCategory.Wilderness => "荒野", CardMapCategory.Cave => "洞窟", CardMapCategory.Sky => "天空", _ => "ラスダン" };

    // 変更前(APK 451): 対象にあるレア度の間で重みを配り直す
    static float[] GoOldOdds(IList<CardDefinition> pool, float[] w)
    {
        var o = new float[5]; var present = new bool[5];
        foreach (var c in pool) present[GameManager.GachaRarityOf(c) - 1] = true;
        float t = 0f; for (int i = 0; i < 5; i++) if (present[i]) t += w[i];
        for (int i = 0; i < 5; i++) o[i] = present[i] && t > 0f ? w[i] / t : 0f;
        return o;
    }
    static string GoPct(float[] o) => string.Join(" / ", o.Select(x => x <= 0f ? "–" : (x * 100f).ToString("0.##") + "%"));

    IEnumerator GachaOddsModeQa()
    {
        float wait = 0f;
        while (GameManager.Instance == null && wait < 10f) { yield return null; wait += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        float[] W = gm.gachaRarityWeights;
        L($"weights {string.Join("/", W)}");
        Check(W.Length == 5 && Mathf.Abs(W.Sum() - 100f) < 0.001f, "weights are 5 values that sum to 100");

        // ---- T: 組み合わせごとの対象と表
        var md = new StringBuilder();
        md.AppendLine("# ガチャのレア度の確率表(自動生成: QaSweep -qaGachaOdds)");
        md.AppendLine();
        md.AppendLine($"重み ★1〜★5 = {string.Join(" / ", W)}。変更前 = APK 451(対象にあるレア度の間で重みを配り直す)。変更後 = 無いレア度の分を対象の一番低いレア度へ。");
        md.AppendLine("ガチャLv = BEST の距離(Lv1 0m / Lv2 5,000m / Lv3 20,000m / Lv4 50,000m / Lv5 100,000m)。到達 = その分類のマップでの最高到達距離(共通は無関係、ラスダンは解放済みのとき)。");
        md.AppendLine("対象が同じレア度の組になる条件はまとめています。「–」は出ない。");
        md.AppendLine();
        float[] bests = { 0f, 5000f, 20000f, 50000f, 100000f };
        float[] reaches = { 0f, 999f, 1000f, 5000f, 20000f, 30000f, 50000f, 100000f };
        var all = CardDatabase.AllCards.Where(c => c != null).ToList();
        int rows = 0, issues = 0;
        var checkedSets = new Dictionary<string, List<CardDefinition>>();
        foreach (var k in GoCats)
        {
            md.AppendLine($"## {GoName(k)}");
            md.AppendLine();
            md.AppendLine("| ガチャLv(BEST) | マップでの到達 | 対象の枚数(★1/★2/★3/★4/★5) | 変更前 ★1/★2/★3/★4/★5 | 変更後 ★1/★2/★3/★4/★5 | 備考 |");
            md.AppendLine("|---|---|---|---|---|---|");
            string prevKey = null;
            foreach (float best in bests)
            {
                int stage = GachaStage.StageForDistance(best);
                IEnumerable<float> rs = (k == CardMapCategory.Common || k == CardMapCategory.LastDungeon) ? new[] { best } : reaches.Where(r => r <= best || r == 0f).Append(best).Distinct();
                foreach (float reach in rs.OrderBy(x => x))
                {
                    var pool = all.Where(c => CardMapCategories.Of(c) == k && GachaStage.IsCardEligibleByDistance(c, best, stage)
                                              && (k == CardMapCategory.Common || k == CardMapCategory.LastDungeon || reach >= CardMapUnlock.RequiredMeters(c) - 0.01f)).ToList();
                    var cnt = new int[5]; foreach (var c in pool) cnt[GameManager.GachaRarityOf(c) - 1]++;
                    string key = string.Join(",", cnt);
                    if (key == prevKey) continue; // 同じ対象(到達の違いで変わらない)
                    prevKey = key;
                    var oldO = pool.Count > 0 ? GoOldOdds(pool, W) : new float[5];
                    var newO = GameManager.GachaRarityOdds(pool, W);
                    string issue = GameManager.GachaOddsIssue(pool, W);
                    if (issue != "") issues++;
                    string reachTxt = k == CardMapCategory.Common ? "—" : k == CardMapCategory.LastDungeon ? "解放済み" : reach.ToString("N0") + "m 以上";
                    string note = pool.Count == 0 ? "対象なし(引けない)" : issue;
                    md.AppendLine($"| Lv{stage}({best:N0}m) | {reachTxt} | {pool.Count}({string.Join("/", cnt)}) | {(pool.Count > 0 ? GoPct(oldO) : "—")} | {(pool.Count > 0 ? GoPct(newO) : "—")} | {note} |");
                    L($"[T] {k} best {best:N0} reach {reach:N0}: {pool.Count} ({key}) old {GoPct(oldO)} new {GoPct(newO)} {issue}");
                    rows++;
                    if (pool.Count > 0) checkedSets[$"{k} best{best} reach{reach}"] = pool;
                    // 期待: 対象にある ★2 以上で一番低いレア度でない物は重みどおり、★4/★5 は対象にある限り 4% / 1%
                    if (pool.Count > 0)
                    {
                        int lowest = System.Array.FindIndex(cnt, x => x > 0);
                        bool ok = Mathf.Abs(newO.Sum() - 1f) < 1e-5f;
                        for (int i = 0; i < 5; i++)
                        {
                            if (cnt[i] == 0) ok &= newO[i] == 0f;
                            else if (i != lowest) ok &= Mathf.Abs(newO[i] - W[i] / 100f) < 1e-6f;
                        }
                        Check(ok, $"T: {k} best {best:N0} reach {reach:N0}: absent rarities 0, others at their weight, the lowest takes the rest ({GoPct(newO)})");
                    }
                }
            }
            md.AppendLine();
        }
        Check(issues == 0, $"T: no combination where ★4/★5 is the lowest rarity of the pool ({issues})");
        md.AppendLine(issues == 0 ? "成立しない条件: なし(どの組み合わせでも一番低いレア度は ★1〜★3。★4/★5 は対象にある限り 4% / 1%)。" : $"成立しない条件: {issues} 件(上の備考)。");
        md.AppendLine();

        // ---- S: 表 = 抽選(等間隔の乱数で本物の抽選を通す)
        L("== S: odds table == actual draw ==");
        const int N = 20000;
        int sOk = 0;
        foreach (var kv in checkedSets)
        {
            var pool = kv.Value; var odds = GameManager.GachaRarityOdds(pool, W);
            var got = new int[5]; int wrong = 0;
            var k = CardMapCategories.Of(pool[0]);
            var ids = new HashSet<string>(pool.Select(c => c.cardId));
            for (int i = 0; i < N; i++)
            {
                var c = (CardDefinition)GoDraw3.Invoke(gm, new object[] { pool, (i + 0.5f) / N, ((i * 7919) % N + 0.5f) / N });
                if (c == null || !ids.Contains(c.cardId) || CardMapCategories.Of(c) != k) { wrong++; continue; }
                got[GameManager.GachaRarityOf(c) - 1]++;
            }
            bool match = wrong == 0;
            for (int r = 0; r < 5; r++) match &= Mathf.Abs(got[r] - odds[r] * N) <= 1.01f;
            if (match) sOk++;
            else Check(false, $"S: {kv.Key}: draw counts {string.Join("/", got)} vs table {string.Join("/", odds.Select(o => (o * N).ToString("0.#")))} (wrong {wrong})");
        }
        Check(sOk == checkedSets.Count, $"S: for all {checkedSets.Count} combinations the actual draw matches the table within ±1 of {N} (evenly spaced rolls) and only gives cards of the pool");

        // ---- R: 今のデータ(全部解禁)の対象 = 画面の抽選の対象、固定の種での参考値
        L("== R: live pools ==");
        gm.DebugSetBestDistance(100500f);
        foreach (var st in UnlockRules.NormalMaps) { GameManager.WriteStageBest(st, 100500); UnlockRules.CommitRun(st, 100500, null); }
        ProgressStats.DevSetFinalDungeonUnlocked(true);
        UnlockRules.QaOverride = false;
        if (UnlockRules.DevUnlockAll) UnlockRules.DevUnlockAll = false;
        yield return ReloadHome();
        md.AppendLine("## 今のデータでの確認(テスト用データ・全部解禁・固定の種で 5000 回の参考値)");
        md.AppendLine();
        md.AppendLine("| 分類 | 対象の枚数 | 表 ★1/★2/★3/★4/★5 | 5000 回の結果 |");
        md.AppendLine("|---|---|---|---|");
        foreach (var k in GoCats)
        {
            var pool = (List<CardDefinition>)GoPoolFor.Invoke(gm, new object[] { k });
            var odds = gm.GachaOddsFor(k);
            string expectKey = $"{k} best100000 reach100000";
            var exp = all.Where(c => CardMapCategories.Of(c) == k).ToList();
            Check(pool.Count == exp.Count, $"R: {k} live pool = all {exp.Count} cards of the category when everything is unlocked ({pool.Count})");
            Check(GameManager.GachaRarityOdds(exp, W).SequenceEqual(odds), $"R: {k} live odds = table ({GoPct(odds)})");
            Random.InitState(20261114 + (int)k);
            var got = new int[5];
            for (int i = 0; i < 5000; i++) { var c = (CardDefinition)GoDraw3.Invoke(gm, new object[] { pool, Random.value, Random.value }); got[GameManager.GachaRarityOf(c) - 1]++; }
            md.AppendLine($"| {GoName(k)} | {pool.Count} | {GoPct(odds)} | {string.Join(" / ", got)} |");
            L($"[R] {k}: pool {pool.Count} odds {GoPct(odds)} seeded 5000 -> {string.Join("/", got)}");
            yield return null;
        }
        md.AppendLine();
        md.AppendLine($"組み合わせ {rows} 行。試験の結果は qa_log を参照。");
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "gacha_odds_table.md"), md.ToString());
        L($"wrote gacha_odds_table.md ({rows} rows)");
    }
}
#endif
