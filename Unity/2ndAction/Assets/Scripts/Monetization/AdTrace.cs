using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// 広告を出した/出さなかった理由の控え(2026-11-14、マスター: 「成功の後に強制広告が出た」報告の原因は未確定。再発した時に追えるように)。
//  ・結果画面からホームへ戻る時の判断(結果の種類・モード・強制広告を出したか/出さなかった理由)と、広告の表示の開始/終わりを1行ずつ。
//  ・端末の中のファイル(ad_trace.txt、最後の 30 行)だけ。セーブデータではない(消えても困らない。送信もしない)。
//  ・問い合わせのメールの下書きの最後に直近の数行を付ける(本人が内容を見てから送る。個人を特定する情報は入れない)。
public static class AdTrace
{
    const int Max = 30;
    static List<string> lines;
    static string path;
    public static int Written { get; private set; } // 確認用

    static void Load()
    {
        if (lines != null) return;
        lines = new List<string>();
        try
        {
            path = Path.Combine(Application.persistentDataPath, "ad_trace.txt");
            if (File.Exists(path)) lines.AddRange(File.ReadAllLines(path));
            if (lines.Count > Max) lines.RemoveRange(0, lines.Count - Max);
        }
        catch (Exception) { /* 読めなくてもゲームには影響させない */ }
    }

    public static void Note(string s)
    {
        Load();
        string line = $"{DateTime.Now:MM-dd HH:mm:ss} v{AppInfo.VersionCode} {s}";
        lines.Add(line);
        if (lines.Count > Max) lines.RemoveRange(0, lines.Count - Max);
        Debug.Log("[AdTrace] " + s);
        try { if (path != null) { File.WriteAllLines(path, lines); Written++; } } catch (Exception) { }
    }

    public static IReadOnlyList<string> Lines { get { Load(); return lines; } }

    // 直近 n 行(無ければ "")
    public static string Tail(int n)
    {
        Load();
        if (lines.Count == 0) return "";
        int from = Mathf.Max(0, lines.Count - n);
        return string.Join("\n", lines.GetRange(from, lines.Count - from));
    }
}
