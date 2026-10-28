using System.Collections.Generic;

// 2026-10-28: 技ごとの1枚絵(Resources/BossPose/)が実際に表示されたかを数える(開発版の QA が読む。製品でも数えるだけで何もしない)
public static class BossPoseStats
{
    public static readonly Dictionary<string, int> Shown = new Dictionary<string, int>();
    public static bool DevDisabled; // 開発版の比較用(-noBossPose 1): 技ごとの1枚絵を使わない = 10-28 より前の表示
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void DevArgs() { var a = QaArgs.All; for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-noBossPose" && a[i + 1] == "1") DevDisabled = true; for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-poseTrace" && a[i + 1] == "1") DevTrace = true; }
#endif
    public static string LastNew = "";
    public static int NewCount;
    // 開発版の確認(-poseTrace 1): 表示している技の絵が変わるたびに、UTC の時刻付きで1行(HOST と JOIN のログを突き合わせる)
    public static bool DevTrace;
    static readonly Dictionary<string, string> traceLast = new Dictionary<string, string>();
    public static void Trace(string who, string art)
    {
        if (!DevTrace) return;
        if (traceLast.TryGetValue(who, out var l) && l == art) return;
        traceLast[who] = art;
        UnityEngine.Debug.Log($"[PoseTrace] {System.DateTime.UtcNow:HH:mm:ss.fff} {who} {art}");
    }
    public static void Note(string key)
    {
        if (Shown.TryGetValue(key, out int n)) { Shown[key] = n + 1; return; }
        Shown[key] = 1; LastNew = key; NewCount++;
    }
}
