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
        return first <= 1000f ? 1 : first <= 5000f ? 2 : 3;
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
            if (group.Contains(k) || !ParseKey(k, out _, out _)) continue;
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
