using System.Collections.Generic;
using UnityEngine;

// ランクと距離の予算でボスを複数出す(2026-10-28、マスターの採用済みの方針)。
//  ・30km 未満は今までどおり関門に合う1体(本来の数)。30km 以上は予算 4(〜60km)/5(〜90km)/6(90km〜)。同時は最大3体。
//  ・先に関門に合う主役(今までどおりの決め方: 本来のボス/再戦の抽選/ラスダンの格のプール)を出し、残りの予算で追加する。
//    予算は使い切らなくてよい。コスト: 小(1km格)1 / 中(5km格)2 / 大(10km格)3 / 特別な強敵(メカドラゴン)5。CostOverrides で個別に変えられる。
//  ・追加の候補: 通常マップ = このランで倒したボス(再戦プール)の同じステージの種類。ラスダン = 格のプール(三ステージ混成)。
//    同じボスは重ねない。大型/空中/範囲攻撃などはボスラッシュと同じタグの上限(巨体同士で画面が埋まらない)。
//    竜/魔人(別の仕組みのボス)と特別な強敵は追加に使わない(メカドラゴンはラスダン限定・単独のまま)。
//  ・強さ: 距離による HP/攻撃の補正は今までどおり(追加のボスはその距離の再戦と同じ式。ラスダンはラスダンの倍率)。ランクの追加倍率は掛けない。
//  ・進行: 関門で距離を止め、全員を倒して報酬を選んでから再開(今までの関門と同じ仕組み = 走行中に付いてくる旧仕様には戻さない)。
//    EXP/MILE は各ボスごとに1回(既存の人数分配)。カードの選択は全員を倒した後に1回。
//  ・対象外: ラスダンのボスラッシュ(90km〜)、通常マップの 90〜94km の大群と 95〜99km の3連戦、三姉妹/死神、疾走出発で飛ばした関門、VERSUS(勝敗の決まりの確認待ち)。
//  ・マルチ: HOST だけが決める。各ボスは今までと同じ Spawn*(kind, index) で出す = JOIN には1体ずつ届く(index は重ならない)。
public partial class BossManager
{
    public static bool FormationEnabled = true;
    public static int DevFormationVariant; // 調整案の試算用(開発版の QA だけが変える。0 = 採用済みの決まり)
    public const int FormationMaxBosses = 3;
    public static readonly Dictionary<string, int> FormationCostOverrides = new Dictionary<string, int>(); // 実際の難しさで個別に調整する("Wild/Golem" → 3 など)

    // 確認用
    public static int FormationGates, FormationExtras;
    public static string LastFormation = "";

    public static int FormationBudgetAt(float meters) => meters < 30000f ? 0 : meters < 60000f ? 4 : meters < 90000f ? 5 : 6;

    public int FormationCostOf(string key)
    {
        if (string.IsNullOrEmpty(key)) return 3;
        if (FormationCostOverrides.TryGetValue(key, out int c)) return Mathf.Max(1, c);
        if (key == LastDungeonBossTuning.I.rareKey || key.EndsWith("/MechDragon")) return 5;
        if (!ParseKey(key, out GateFamily f, out int kind)) return 3;
        float first = FirstDistanceOf(f, kind);
        int cost = first <= 1000f ? 1 : first <= 5000f ? 2 : 3;
        // 調整案の試算(開発版の formsim だけが立てる。製品は 0): 2 = 慣れたボスの割引(今の関門の距離の 1/3 より前に初登場した種類はコスト-1)
        if ((DevFormationVariant & 2) != 0 && cost > 1 && first <= gateK * gateIntervalMeters / 3f) cost--;
        return cost;
    }

    bool FormationApplies(float meters)
    {
        if (!FormationEnabled || FormationBudgetAt(meters) <= 0) return false;
        if (RushGateK != 0) return false;
        if (NetRunLauncher.IsMultiplayerRun && NetMatch.Mode == MultiplayerGameMode.Versus) return false;
        return true;
    }

    // 主役の数を同時の上限に収める(30km 以上: 本来の数が4体の狼/ムカデ等 + BOSS RUSH の1体を足しても3体まで)
    int FormationClampCount(int count) => FormationApplies(gateK * gateIntervalMeters) ? Mathf.Clamp(count, 1, FormationMaxBosses) : count;

    // 主役を出した直後に呼ぶ。primaryKey = 主役の種類、primaryCount = 主役の数
    void AddFormationExtras(string primaryKey, int primaryCount)
    {
        float d = gateK * gateIntervalMeters;
        if (!FormationApplies(d)) return;
        var ld = LastDungeonBossTuning.I;
        int budget = FormationBudgetAt(d);
        int left = budget - FormationCostOf(primaryKey) * Mathf.Max(1, primaryCount);
        int bodies = Mathf.Max(1, primaryCount);
        if ((ld.TagsOf(primaryKey) & LastDungeonBossTuning.Tag.Solo) != 0) left = 0; // 特別な強敵は単独
        var group = new List<string> { primaryKey };
        var picks = new List<string>();
        while (left > 0 && bodies < FormationMaxBosses)
        {
            var cands = FormationCandidates(group, left);
            if (cands.Count == 0) break;
            string k = cands[Random.Range(0, cands.Count)]; // HOST/シングルだけが呼ぶ
            group.Add(k); picks.Add(k);
            left -= FormationCostOf(k); bodies++;
        }
        FormationGates++;
        LastFormation = $"{gateK}km budget {budget}: {primaryKey}×{primaryCount}(cost {FormationCostOf(primaryKey) * primaryCount})" + (picks.Count > 0 ? " + " + string.Join(" + ", picks.ConvertAll(p => $"{p}({FormationCostOf(p)})")) : " (追加なし)") + $" = {bodies}体 残り予算 {Mathf.Max(0, left)}";
        Debug.Log("[BossFormation] " + LastFormation);
        if (picks.Count == 0) return;

        // 主役の強さの状態を控えて、追加のボスごとにその距離の強さを入れてから出す(出した後に戻す)
        bool keepRematch = CurrentEncounterIsRematch; float keepMul = CurrentRematchHpMul; string keepTierLabel = CurrentRematchTier; var keepTier = currentTier; float keepDmg = DamageMul;
        int index = Mathf.Max(1, primaryCount);
        foreach (var k in picks)
        {
            if (!ParseKey(k, out GateFamily fam, out int kind)) continue;
            if (IsLastStage) SetLastDungeonStrength(k, d, false);
            else SetFormationStrength(fam, kind, d);
            DamageMul = keepDmg; // プレイヤーへの被弾量は関門で1つ(主役の決まりのまま。追加でまとめて重くしない)
            lastSpawnedWild = null;
            if (fam == GateFamily.Sky && ((SkyBossKind)kind == SkyBossKind.Dragon || (SkyBossKind)kind == SkyBossKind.Majin))
            {
                // 竜/魔人は別の数え方(倒した時に OnDragonDefeated/OnMajinDefeated)。生成もその入口で
                float standoff = dragonStandoffDistance + index * dragonSpacing;
                if ((SkyBossKind)kind == SkyBossKind.Dragon) { aliveDragonsThisEncounter++; SpawnSkyDragon(standoff); }
                else { aliveMajinsThisEncounter++; SpawnSkyMajin(standoff); }
                FormationExtras++; ProgressStats.MarkBossSeen(k); index++;
                continue;
            }
            if (fam == GateFamily.Cave)
            {
                SpawnCaveBoss((CaveBossKind)kind, index);
                if (TerrainManager.Instance != null && TerrainManager.Instance.cave != null && player != null) TerrainManager.Instance.cave.SetBossClearZone(SpawnRef().position.x, 40f);
                if (IsLastStage) encounterHasCave = true;
            }
            else if (fam == GateFamily.Sky) SpawnSkyBoss((SkyBossKind)kind, index);
            else SpawnWild((WildBossKind)kind, index);
            var boss = lastSpawnedWild;
            if (boss != null)
            {
                bossKeys[boss] = k;
                int cost = FormationCostOf(k);
                boss.rewardClassOverride = cost >= 5 ? RunRewards.BossClass.Elite : cost >= 3 ? RunRewards.BossClass.Big : cost == 2 ? RunRewards.BossClass.Mid : RunRewards.BossClass.Small; // EXP は追加のボスの格で
                aliveWildThisEncounter++;
                FormationExtras++;
                ProgressStats.MarkBossSeen(k);
            }
            index++;
        }
        CurrentEncounterIsRematch = keepRematch; CurrentRematchHpMul = keepMul; CurrentRematchTier = keepTierLabel; currentTier = keepTier; DamageMul = keepDmg;
    }

    // 再戦と同じ式(その距離の段階 × 本来の距離からの HP の伸び)
    void SetFormationStrength(GateFamily f, int kind, float d)
    {
        var tn = BossRematchTuning.I;
        currentTier = tn.TierAt(d);
        float distMul = tn.distanceHp ? Mathf.Max(1f, BossHpPlan.MilestoneHpAt(d) / Mathf.Max(1f, BossHpPlan.MilestoneHpAt(FirstDistanceOf(f, kind)))) : 1f;
        CurrentRematchHpMul = distMul * Mathf.Max(0.1f, currentTier.hpMul);
        CurrentRematchTier = currentTier.label;
        CurrentEncounterIsRematch = true;
    }

    List<string> FormationCandidates(List<string> group, int left)
    {
        var ld = LastDungeonBossTuning.I;
        var src = new List<string>();
        if (IsLastStage)
        {
            foreach (var p in new[] { ld.pool1k, ld.pool5k, ld.pool10k }) if (p != null) foreach (var k in p) if (!src.Contains(k)) src.Add(k);
        }
        else foreach (var k in defeatedPool) if (ParseKey(k, out GateFamily f, out _) && f == StageFamily && !src.Contains(k)) src.Add(k);
        var res = new List<string>();
        foreach (var k in src)
        {
            // 調整案の試算 1 = 小ボス(コスト1)だけ同じ種類を2体まで(本来の関門でも群れで出る種類)
            bool dupOk = (DevFormationVariant & 1) != 0 && FormationCostOf(k) == 1 && group.FindAll(x => x == k).Count < 2;
            if ((group.Contains(k) && !dupOk) || !ParseKey(k, out _, out _)) continue;
            if (FormationCostOf(k) > left) continue;
            var t = ld.TagsOf(k);
            // 竜/魔人(別の仕組みのボス)は、天空回廊の小/中ボスとしてだけ追加に使う(天空の他のボスは全部 10km 格 = コスト3 で、
            //  竜/魔人を除くと予算 4/5 では誰も足せなかった。2026-10-28 の試験で 0/36)。特別な強敵は追加にしない
            bool skySmall = IsSkyStage && (k == "Sky/Dragon" || k == "Sky/Majin");
            if ((t & LastDungeonBossTuning.Tag.Solo) != 0 || ((t & LastDungeonBossTuning.Tag.NoRush) != 0 && !skySmall)) continue;
            if (k == ld.rareKey || k.EndsWith("/MechDragon")) continue;
            if (!TagsFit(group, t)) continue;
            res.Add(k);
        }
        return res;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 開発用(2026-10-28、3体が少ない理由の調査): 今のステージで 1〜99km の関門を runs 回、生成なしで抽選だけ通して、
    // 30〜59/60〜89/90〜km の体数の割合・代表的な組み合わせ・3体にならなかった理由を数える。
    // 主役の決め方は本番と同じ関数(Resolve*/Draw/ResolveLastMilestone)、追加は FormationCandidates。プールと直近の並びは終わったら戻す。
    public string DevFormationSim(int runs)
    {
        var keepPool = new List<string>(defeatedPool); var keepRecent = new List<string>(recentFought);
        int keepGateK = gateK, keepRare = rareCountThisRun, keepRareK = rareLastK; string keepKey = CurrentEncounterKey;
        var bands = new[] { (30, 59, "30-59km"), (60, 89, "60-89km"), (90, 99, "90km-") };
        var bodies = new int[3, 4]; var combos = new Dictionary<string, int>[3]; for (int b = 0; b < 3; b++) combos[b] = new Dictionary<string, int>();
        var why = new Dictionary<string, int>[3]; for (int b = 0; b < 3; b++) why[b] = new Dictionary<string, int>();
        var fam = StageFamily;
        try
        {
            for (int run = 0; run < runs; run++)
            {
                defeatedPool.Clear(); recentFought.Clear(); rareCountThisRun = 0; rareLastK = -999;
                for (int k = 1; k <= 99; k++)
                {
                    gateK = k;
                    if (!IsLastStage && (FinaleSkipsGate(k) || FinaleTrioGate(k))) continue; // 通常マップ 91〜99km は大群と3連戦(対象外)
                    if (IsLastStage && RushEnabled && k >= RushFirstK && k <= RushLastK) continue; // ラスダン 90km〜 はボスラッシュ(対象外)
                    string primary; int count;
                    if (IsLastStage)
                    {
                        if (!ResolveLastMilestone(k, out GateFamily lf, out int lk, out _)) continue;
                        primary = Key(lf, lk); count = 1;
                    }
                    else
                    {
                        int kind; count = 1;
                        if (fam == GateFamily.Sky) { if (!ResolveSkyGate(k, out SkyBossKind s, out count)) continue; kind = (int)s; }
                        else if (fam == GateFamily.Cave) { if (!ResolveCaveGate(k, out CaveBossKind c, out count)) continue; kind = (int)c; }
                        else { if (!ResolveGate(k, out WildBossKind w, out count)) continue; kind = (int)w; }
                        string native = Key(fam, kind); primary = native;
                        if (RematchStage && k % 10 != 0 && defeatedPool.Contains(native))
                        {
                            string pick = Draw(k % 5 == 0, out _);
                            if (pick != null && !(pick == native && lastDrawExclude == 0)) { primary = pick; count = 1; }
                        }
                    }
                    recentFought.Remove(primary); recentFought.Insert(0, primary); while (recentFought.Count > 6) recentFought.RemoveAt(recentFought.Count - 1);
                    float d = k * gateIntervalMeters;
                    int bi = k >= 90 ? 2 : k >= 60 ? 1 : k >= 30 ? 0 : -1;
                    if (bi >= 0 && FormationBudgetAt(d) > 0)
                    {
                        count = Mathf.Clamp(count, 1, FormationMaxBosses);
                        int budget = FormationBudgetAt(d), left = budget - FormationCostOf(primary) * count, n = count;
                        var ld = LastDungeonBossTuning.I;
                        if ((ld.TagsOf(primary) & LastDungeonBossTuning.Tag.Solo) != 0) left = 0;
                        var group = new List<string> { primary }; var picks = new List<string>();
                        while (left > 0 && n < FormationMaxBosses)
                        {
                            var cands = FormationCandidates(group, left);
                            if (cands.Count == 0) break;
                            string x = cands[Random.Range(0, cands.Count)];
                            group.Add(x); picks.Add(x); left -= FormationCostOf(x); n++;
                        }
                        bodies[bi, n]++;
                        string combo = (count > 1 ? $"{primary}×{count}" : primary) + (picks.Count > 0 ? " + " + string.Join(" + ", picks) : "");
                        combos[bi].TryGetValue(combo, out int cc); combos[bi][combo] = cc + 1;
                        if (n < 3)
                        {
                            // 3体にならなかった理由: 主役の後の予算で、2体を足せる組み合わせがあったか(タグを無視/守った場合)
                            string r = DevWhyNotThree(primary, count, budget);
                            why[bi].TryGetValue(r, out int wc); why[bi][r] = wc + 1;
                        }
                    }
                    if (!defeatedPool.Contains(primary)) defeatedPool.Add(primary); // 倒した = プールへ(本番の RegisterEncounterDefeated と同じく主役だけ)
                }
            }
        }
        finally
        {
            defeatedPool.Clear(); defeatedPool.AddRange(keepPool); recentFought.Clear(); recentFought.AddRange(keepRecent);
            gateK = keepGateK; rareCountThisRun = keepRare; rareLastK = keepRareK; CurrentEncounterKey = keepKey;
        }
        var sb = new System.Text.StringBuilder();
        for (int b = 0; b < 3; b++)
        {
            int tot = bodies[b, 1] + bodies[b, 2] + bodies[b, 3];
            if (tot == 0) { sb.AppendLine($"[formsim] {bands[b].Item3}: 関門なし(対象外)"); continue; }
            sb.AppendLine($"[formsim] {bands[b].Item3}: gates={tot} 1体 {bodies[b, 1] * 100f / tot:F1}% / 2体 {bodies[b, 2] * 100f / tot:F1}% / 3体 {bodies[b, 3] * 100f / tot:F1}%");
            var top = new List<KeyValuePair<string, int>>(combos[b]); top.Sort((p, q) => q.Value.CompareTo(p.Value));
            for (int i = 0; i < Mathf.Min(8, top.Count); i++) sb.AppendLine($"[formsim]   {top[i].Value * 100f / tot,5:F1}%  {top[i].Key}");
            var ws = new List<KeyValuePair<string, int>>(why[b]); ws.Sort((p, q) => q.Value.CompareTo(p.Value));
            foreach (var w in ws) sb.AppendLine($"[formsim]   3体にならない理由 {w.Value * 100f / tot,5:F1}%  {w.Key}");
        }
        return sb.ToString();
    }

    string DevWhyNotThree(string primary, int count, int budget)
    {
        int left = budget - FormationCostOf(primary) * count, need = 3 - count;
        var ld = LastDungeonBossTuning.I;
        if ((ld.TagsOf(primary) & LastDungeonBossTuning.Tag.Solo) != 0) return "主役が単独の強敵";
        // 候補の全体(タグの上限と NoRush を無視): 同じステージ(通常)/格のプール(ラスダン)の、まだ出ていない種類
        var all = new List<string>();
        if (IsLastStage) { foreach (var p in new[] { ld.pool1k, ld.pool5k, ld.pool10k }) if (p != null) foreach (var k in p) if (!all.Contains(k) && k != primary && ParseKey(k, out _, out _)) all.Add(k); }
        else foreach (var k in defeatedPool) if (!all.Contains(k) && k != primary && ParseKey(k, out GateFamily f, out _) && f == StageFamily) all.Add(k);
        bool CanFill(List<string> pool, int lft, int needN, bool tags, List<string> grp)
        {
            if (needN == 0) return true;
            foreach (var k in pool)
            {
                if (grp.Contains(k) || FormationCostOf(k) > lft) continue;
                if (tags && !FormationCandidates(grp, lft).Contains(k)) continue;
                grp.Add(k); bool ok = CanFill(pool, lft - FormationCostOf(k), needN - 1, tags, grp); grp.RemoveAt(grp.Count - 1);
                if (ok) return true;
            }
            return false;
        }
        var g = new List<string> { primary };
        if (CanFill(all, left, need, true, g)) return "抽選の偏り(3体の組み合わせはあったが、先に選んだ追加で予算が尽きた)";
        if (CanFill(all, left, need, false, g)) return "組み合わせの制限(タグの上限/竜・魔人/NoRush)で3体の組み合わせが無い";
        // 予算: 安い順に need 体を足しても足りない
        var costs = new List<int>(); foreach (var k in all) costs.Add(FormationCostOf(k)); costs.Sort();
        int min = 0; for (int i = 0; i < need && i < costs.Count; i++) min += costs[i];
        if (costs.Count < need) return $"候補の種類が足りない(倒したボスの種類が{all.Count}つ)";
        return $"予算不足(主役 コスト{FormationCostOf(primary)}×{count}、残り{Mathf.Max(0, left)}、安い{need}体でも{min})";
    }
#endif

    // ボスラッシュと同じタグの上限(大型/空中/範囲攻撃/飛び道具/追跡)
    bool TagsFit(List<string> group, LastDungeonBossTuning.Tag t)
    {
        var ld = LastDungeonBossTuning.I;
        int large = 0, air = 0, area = 0, proj = 0, chase = 0;
        foreach (var g in group)
        {
            var bt = ld.TagsOf(g);
            if ((bt & LastDungeonBossTuning.Tag.Large) != 0) large++;
            if ((bt & LastDungeonBossTuning.Tag.Air) != 0) air++;
            if ((bt & LastDungeonBossTuning.Tag.AreaAttack) != 0) area++;
            if ((bt & LastDungeonBossTuning.Tag.Projectile) != 0) proj++;
            if ((bt & LastDungeonBossTuning.Tag.Chaser) != 0) chase++;
        }
        // 巨体は1体まで(通常の関門は止まって戦うので、ラッシュの上限 2 より厳しく)
        if ((t & LastDungeonBossTuning.Tag.Large) != 0 && large + 1 > 1) return false;
        if ((t & LastDungeonBossTuning.Tag.Air) != 0 && air + 1 > ld.maxAir) return false;
        if ((t & LastDungeonBossTuning.Tag.AreaAttack) != 0 && area + 1 > ld.maxAreaAttack) return false;
        if ((t & LastDungeonBossTuning.Tag.Projectile) != 0 && proj + 1 > ld.maxProjectile) return false;
        if ((t & LastDungeonBossTuning.Tag.Chaser) != 0 && chase + 1 > ld.maxChaser) return false;
        return true;
    }
}
