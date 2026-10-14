#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// 開発版の検証用(2026-10-14): 起動引数 -fileSave <path.json> で、保存をすべて専用のファイルへ読み書きする。
// 通常のセーブ(PlayerPrefs / レジストリ / Android の保存)を読まず・書かず・消さない。-memSave と違ってプロセスを終了しても残るので、
// 「保存 → 終了 → 起動し直して読み込み」や、途中で強制終了した後の復旧を本当の再起動で確かめられる。
// 書き込みは Save() の時だけ(一時ファイルに書き切ってから置き換え)。バックアップ(SaveSystem)も同じフォルダに置く。
public class FileSaveStore : ISaveStore
{
    [Serializable] class Row { public string k; public int t; public string v; }
    [Serializable] class FileRows { public List<Row> rows = new List<Row>(); }

    readonly Dictionary<string, object> map = new Dictionary<string, object>();
    readonly string path;
    public static bool Active { get; private set; }
    public static string Folder { get; private set; } = "";
    public static int Saves { get; private set; }
    // 2026-10-14: Android の実機の確認用。アプリのフォルダの qa_filesave.txt(中身 = 専用のファイルの名前)がある間は、毎回の起動で専用のファイルを使う
    // (qa_args.txt は読んだら消えるので、再起動の確認ができなかった)。消す: adb で消す / DEBUG → テストデータ → 「専用の試験ファイルを終わる」
    public const string StickyFile = "qa_filesave.txt";
    public static bool Sticky { get; private set; }
    public static string FilePath { get; private set; } = "";
    public static string StickyPath => System.IO.Path.Combine(Application.persistentDataPath, StickyFile);
    public static bool EndSticky()
    {
        try { if (System.IO.File.Exists(StickyPath)) System.IO.File.Delete(StickyPath); Debug.Log("[FileSaveStore] sticky test file ended (the next start uses the normal save)"); return true; }
        catch (Exception e) { Debug.LogWarning("[FileSaveStore] could not remove " + StickyFile + ": " + e.Message); return false; }
    }

    FileSaveStore(string p)
    {
        path = p;
        try
        {
            if (System.IO.File.Exists(p))
            {
                var f = JsonUtility.FromJson<FileRows>(System.IO.File.ReadAllText(p));
                if (f != null && f.rows != null)
                    foreach (var r in f.rows)
                    {
                        if (r == null || string.IsNullOrEmpty(r.k)) continue;
                        if (r.t == 0 && int.TryParse(r.v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) map[r.k] = i;
                        else if (r.t == 1 && float.TryParse(r.v, NumberStyles.Float, CultureInfo.InvariantCulture, out float fl)) map[r.k] = fl;
                        else map[r.k] = r.v ?? "";
                    }
            }
        }
        catch (Exception e) { Debug.LogError("[FileSaveStore] could not read " + p + ": " + e.Message); }
    }

    public bool HasKey(string key) => map.ContainsKey(key);
    public int GetInt(string key, int def) => map.TryGetValue(key, out var v) && v is int i ? i : def;
    public float GetFloat(string key, float def) => map.TryGetValue(key, out var v) && v is float f ? f : def;
    public string GetString(string key, string def) => map.TryGetValue(key, out var v) && v is string s ? s : def;
    public void SetInt(string key, int v) => map[key] = v;
    public void SetFloat(string key, float v) => map[key] = v;
    public void SetString(string key, string v) => map[key] = v ?? "";
    public void DeleteKey(string key) => map.Remove(key);
    public void DeleteAll() => map.Clear();

    public void Save()
    {
        var f = new FileRows();
        var keys = new List<string>(map.Keys);
        keys.Sort(StringComparer.Ordinal);
        foreach (var k in keys)
        {
            object o = map[k];
            if (o is int i) f.rows.Add(new Row { k = k, t = 0, v = i.ToString(CultureInfo.InvariantCulture) });
            else if (o is float fl) f.rows.Add(new Row { k = k, t = 1, v = fl.ToString("R", CultureInfo.InvariantCulture) });
            else f.rows.Add(new Row { k = k, t = 2, v = o as string ?? "" });
        }
        string tmp = path + ".tmp";
        System.IO.File.WriteAllText(tmp, JsonUtility.ToJson(f, true));
        if (System.IO.File.Exists(path)) System.IO.File.Replace(tmp, path, null); else System.IO.File.Move(tmp, path);
        Saves++;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Boot()
    {
        var a = Environment.GetCommandLineArgs();
        string p = null;
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-fileSave") p = a[i + 1];
#if UNITY_ANDROID && !UNITY_EDITOR
        // Android は起動引数が渡せないので qa_args.txt(QaArgs)から
        if (p == null) { var q = QaArgs.All; for (int i = 0; i < q.Length - 1; i++) if (q[i] == "-fileSave") p = q[i + 1]; }
        if (p == null)
        {
            try
            {
                if (System.IO.File.Exists(StickyPath))
                {
                    string s = System.IO.File.ReadAllText(StickyPath).Trim();
                    // 名前だけ(アプリのフォルダの中)。空/フォルダの外を指す物は使わない(通常のデータで起動する)
                    if (s.Length > 0 && s.IndexOfAny(new[] { '/', '\\', ':' }) < 0 && !s.Contains("..")) { p = System.IO.Path.Combine("qa_test", s); Sticky = true; }
                    else Debug.LogWarning("[FileSaveStore] " + StickyFile + " must contain a plain file name; ignored");
                }
            }
            catch (Exception e) { Debug.LogWarning("[FileSaveStore] " + e.Message); }
        }
#endif
        if (string.IsNullOrEmpty(p)) return;
        if (!System.IO.Path.IsPathRooted(p)) p = System.IO.Path.Combine(Application.persistentDataPath, p);
        Folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(p));
        System.IO.Directory.CreateDirectory(Folder);
        Platform.Install(save: new FileSaveStore(p));
        Active = true; FilePath = p;
        Debug.Log("[FileSaveStore] saves go to the dedicated test file " + p + " (normal save untouched)");
    }
}
#endif
