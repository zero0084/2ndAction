using System.Collections.Generic;
using UnityEngine;

// 2026-10-21 確認用: ボスの撃破 → 報酬(MILE/EXP)→ ボスの報酬の選択 を、どのボス(個体)の撃破かとひも付けて記録する(開発版の試験が読む)。
//  ゲームの動きは変えない。BossTag は撃破を登録する直前に呼ぶ側が入れる(名前#個体番号)
public static class RewardLedger
{
    public struct Entry
    {
        public float time; public int frame; public string kind, tag, detail;
        public override string ToString() => $"{time:F2}s f{frame} {kind} [{tag}] {detail}";
    }
    public static string BossTag = "";
    public static readonly List<Entry> Entries = new List<Entry>();
    public static int Trimmed; // 2026-10-28: 古い記録を捨てた件数(試験は「通し番号 = Trimmed + 位置」で読む。捨てると位置がずれ、長い試験で記録が無いように見えていた)

    public static void Note(string kind, string detail)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Entries.Add(new Entry { time = Time.time, frame = Time.frameCount, kind = kind, tag = BossTag, detail = detail });
        if (Entries.Count > 512) { Entries.RemoveRange(0, 256); Trimmed += 256; }
#endif
    }

    public static string Tag(Component c, string name) => c == null ? "?" : $"{name}#{c.GetEntityId()}";
}
