#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

// 開発版の検証用(2026-10-08、依頼F): 起動引数 -memSave で、保存をすべてこのプロセスのメモリの中だけにする。
// 通常のセーブ(PlayerPrefs / レジストリ)を読まず・書かず・消さないので、検証で進行を作っても通常の記録と混ざらない。
// プロセスごとに別なので、検証を並列に走らせても互いに干渉しない。終了すると全部消える。
public class MemorySaveStore : ISaveStore
{
    readonly Dictionary<string, object> map = new Dictionary<string, object>();
    public static bool Active { get; private set; }

    public bool HasKey(string key) => map.ContainsKey(key);
    public int GetInt(string key, int def) => map.TryGetValue(key, out var v) && v is int i ? i : def;
    public float GetFloat(string key, float def) => map.TryGetValue(key, out var v) && v is float f ? f : def;
    public string GetString(string key, string def) => map.TryGetValue(key, out var v) && v is string s ? s : def;
    public void SetInt(string key, int v) => map[key] = v;
    public void SetFloat(string key, float v) => map[key] = v;
    public void SetString(string key, string v) => map[key] = v ?? "";
    public void DeleteKey(string key) => map.Remove(key);
    public void DeleteAll() => map.Clear();
    public void Save() { }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Boot()
    {
        foreach (var a in System.Environment.GetCommandLineArgs())
            if (a == "-memSave")
            {
                Platform.Install(save: new MemorySaveStore());
                Active = true;
                Debug.Log("[MemorySaveStore] saves are kept in memory only (normal save untouched)");
                return;
            }
    }
}
#endif
