#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// 自動テストの起動引数(開発版のみ、2026-10-10)。PC は起動引数そのまま。
// Android は起動引数を渡しにくいので、アプリのフォルダの qa_args.txt(adb push)の中身を足す(読んだら消す = 次の起動では動かない)。
public static class QaArgs
{
    static string[] all;
    public static string[] All { get { if (all == null) Load(); return all; } }
    static void Load()
    {
        var list = new List<string>(Environment.GetCommandLineArgs());
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            string p = Path.Combine(Application.persistentDataPath, "qa_args.txt");
            if (File.Exists(p))
            {
                list.AddRange(File.ReadAllText(p).Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries));
                File.Delete(p);
                Debug.Log("[QaArgs] from qa_args.txt: " + string.Join(" ", list));
            }
        }
        catch (Exception e) { Debug.LogWarning("[QaArgs] " + e.Message); }
#endif
        all = list.ToArray();
    }
    // Android: 相対のフォルダはアプリのフォルダの中へ
    public static string Dir(string d)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Path.IsPathRooted(d)) d = Path.Combine(Application.persistentDataPath, d);
#endif
        try { Directory.CreateDirectory(d); } catch { }
        return d;
    }
}
#endif
