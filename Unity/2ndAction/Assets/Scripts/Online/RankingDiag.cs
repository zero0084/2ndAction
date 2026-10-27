using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

// ランキングの診断(2026-10-27、APK 387 の実機で「脱出しても記録がありません」の切り分け。開発版だけ)。
//  脱出の確定 / 送信 / 読み込み の結果を、アプリのフォルダの ranking_diag.txt に残す(起動し直しても読める、最新 16 行)。
//  ランキングの画面(開発版)が、今の状態と一緒に表示する。PlayerId は先頭6文字だけ。秘密・トークンは扱わない。
//  製品版では呼び出しごと消える(Conditional)
public static class RankingDiag
{
    const int Keep = 16;
    static List<string> lines;
    public static string LastCommit = "", LastSubmit = "", LastLoad = "";

    static string PathOf => Path.Combine(UnityEngine.Application.persistentDataPath, "ranking_diag.txt");

    public static List<string> Lines
    {
        get
        {
            if (lines != null) return lines;
            lines = new List<string>();
            try { if (File.Exists(PathOf)) lines.AddRange(File.ReadAllLines(PathOf)); } catch { }
            foreach (var l in lines)
            {
                if (l.Contains(" COMMIT ")) LastCommit = l;
                if (l.Contains(" SEND ")) LastSubmit = l;
                if (l.Contains(" LOAD ")) LastLoad = l;
            }
            return lines;
        }
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Note(string kind, string text)
    {
        var l = Lines;
        string line = $"{DateTime.Now:MM-dd HH:mm:ss} {kind} {text}";
        l.Add(line);
        while (l.Count > Keep) l.RemoveAt(0);
        if (kind == "COMMIT") LastCommit = line;
        if (kind == "SEND") LastSubmit = line;
        if (kind == "LOAD") LastLoad = line;
        try { File.WriteAllLines(PathOf, l); } catch { }
        UnityEngine.Debug.Log("[RankingDiag] " + line);
    }
}
