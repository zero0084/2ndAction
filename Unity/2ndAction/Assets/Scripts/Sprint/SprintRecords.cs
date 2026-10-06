using System.Collections.Generic;
using UnityEngine;

// 疾走出発の解放条件(2026-10-05 試作)。
//  ・行き先 D(10,000m 刻み、上限 SprintTuning.maxDestination)を選べるのは
//      (1) そのマップで D に到達したことがある(マップ別 BEST ≥ D)
//      (2) D より手前の 10,000m ごとの門番(10k, 20k, … D-10k)を、そのマップで「実際に倒した」記録がすべてある
//    = 未突破の門番を飛び越えない。D の門番は到着後に戦う(倒している必要はない)。
//  ・「実際に倒した」記録は新しく作った(以前は永続の撃破記録が無かった): ソロの通常ランで 10,000m 刻みの関門の
//    遭遇を倒した時に記録する(BossManager.CheckEncounterComplete)。疾走で飛ばした関門・DEBUG RUN・闘技場・マルチは記録しない。
//    保存キー SprintGatesV1 = "wasteland_road:10,20;natural_cave:10" のような文字列(Progress)。
//  ・このため、以前のセーブでは記録が空 = 疾走出発は 10,000m の門番を倒し直すまで使えない(開発版は DEBUG の全解放で確認できる)。
public static class SprintRecords
{
    static Dictionary<string, HashSet<int>> cleared;

    static Dictionary<string, HashSet<int>> Cleared
    {
        get
        {
            if (cleared != null) return cleared;
            cleared = new Dictionary<string, HashSet<int>>();
            foreach (var part in SaveStore.GetString(SaveKeys.SprintGates, "").Split(';'))
            {
                int c = part.IndexOf(':');
                if (c <= 0) continue;
                var set = new HashSet<int>();
                foreach (var k in part.Substring(c + 1).Split(',')) if (int.TryParse(k, out int v) && v > 0) set.Add(v);
                cleared[part.Substring(0, c)] = set;
            }
            return cleared;
        }
    }

    public static void Reload() { cleared = null; }

    public static bool IsGateCleared(string stageId, int gateK) => !string.IsNullOrEmpty(stageId) && Cleared.TryGetValue(stageId, out var s) && s.Contains(gateK);

    // 10,000m 刻みの関門の門番を実際に倒した(ソロの通常ラン)
    public static void MarkGateCleared(string stageId, int gateK)
    {
        if (string.IsNullOrEmpty(stageId) || gateK <= 0 || gateK % 10 != 0) return;
        if (IsGateCleared(stageId, gateK)) return;
        if (DebugRun.BlocksSave("SprintGate_" + stageId + "_" + gateK)) return;
        if (!Cleared.TryGetValue(stageId, out var s)) { s = new HashSet<int>(); Cleared[stageId] = s; }
        s.Add(gateK);
        Save();
        Debug.Log($"[Sprint] gate cleared record: {stageId} {gateK * 1000}m");
    }

    static void Save()
    {
        var parts = new List<string>();
        foreach (var kv in Cleared)
        {
            var ks = new List<int>(kv.Value); ks.Sort();
            parts.Add(kv.Key + ":" + string.Join(",", ks));
        }
        SaveStore.SetString(SaveKeys.SprintGates, string.Join(";", parts));
        SaveStore.Save();
    }

    // 開発版: 記録が無くても全部の行き先を選べる(DEBUG パネル)
    public static bool DevUnlockAll
    {
        get => Debug.isDebugBuild && SaveStore.GetInt(SaveKeys.DevSprintUnlockAll, 0) != 0;
        set { if (Debug.isDebugBuild) { SaveStore.SetInt(SaveKeys.DevSprintUnlockAll, value ? 1 : 0); SaveStore.Save(); } }
    }

    public struct Destination { public int meters; public bool unlocked; public string why; }

    public static List<Destination> Destinations(string stageId)
    {
        var list = new List<Destination>();
        var t = SprintTuning.I;
        float best = GameManager.Instance != null ? (float)GameManager.Instance.GetStageBest(stageId) : 0f;
        for (int d = t.stepMeters; d <= t.maxDestination; d += t.stepMeters)
        {
            bool ok = IsUnlocked(stageId, d, out string why, best);
            list.Add(new Destination { meters = d, unlocked = ok, why = why });
        }
        return list;
    }

    public static bool IsUnlocked(string stageId, int destination, out string why) =>
        IsUnlocked(stageId, destination, out why, GameManager.Instance != null ? (float)GameManager.Instance.GetStageBest(stageId) : 0f);

    static bool IsUnlocked(string stageId, int destination, out string why, float best)
    {
        why = "";
        var t = SprintTuning.I;
        if (destination <= 0 || destination % t.stepMeters != 0 || destination > t.maxDestination) { why = "対象外の距離"; return false; }
        if (DevUnlockAll) { why = "DEBUG 全解放"; return true; }
        if (best + 0.5f < destination) { why = $"{destination / 1000}km に未到達"; return false; }
        for (int g = 10; g * 1000 < destination; g += 10)
            if (!IsGateCleared(stageId, g)) { why = $"{g}km の門番が未撃破"; return false; }
        return true;
    }
}
