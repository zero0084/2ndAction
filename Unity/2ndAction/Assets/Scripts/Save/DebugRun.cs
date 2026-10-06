using System.Collections.Generic;
using UnityEngine;

// 正式記録の対象外のラン(Debug Run、2026-10-02)。開発版のデバッグワープ(ラスダン終盤/エンドロール/ONE MORE MILE?)で使う。
//
// リリース版: IsActive は常に false(開始する Begin が開発版でしかコンパイルされない)。各保存処理の BlocksSave は何もしない。
// 開発版: Debug Run の間は、進行の保存(BEST/MILE/カード/解放/中断中のラン/累計距離/三姉妹の遭遇/ラスダン解放/選択中のステージ)を
//        2重に守る:
//   1) 保存する所の入口で止める(BlocksSave)。メモリ上の値は変わることがあるが、PlayerPrefs へは書かない。
//   2) 開始時に「進行」(SaveCategory.Progress)の全キーを控え(メモリ + ファイル)、終了時に控えと違うキーを元へ戻す。
//      アプリが落ちて終了処理が走らなかった場合も、次の起動(SaveSystem の起動時処理)でファイルから戻す。
//   設定(音量など)と開発用の値(無敵など)は戻さない(Debug Run 中に変えた設定はそのまま残る)。
public static class DebugRun
{
    public static bool IsActive { get; private set; }
    public static string What { get; private set; } = "";
    public static int BlockedWrites { get; private set; }
    public static int LastRestoredKeys { get; private set; } = -1;   // 直前の終了で、控えと違って戻したキーの数(0が正常)
    public static string LastRestoreNote { get; private set; } = "";

    // 進行を書き込まない状態: Debug Run(開発版)/ 闘技場(2026-10-06、リリース版でも。練習なので報酬/記録/所持/解放/累計/CONTINUE を一切書かない)
    public static bool WritesBlocked => IsActive || ArenaMode.Active || ArenaMode.PendingStart;

    // 保存処理の入口で呼ぶ。true なら保存しない。
    public static bool BlocksSave(string what)
    {
        if (!WritesBlocked) return false;
        BlockedWrites++;
        if (BlockedWrites <= 40 || BlockedWrites % 100 == 0) Debug.Log($"[{(IsActive ? "DebugRun" : "Arena")}] save blocked: {what} (#{BlockedWrites})");
        return true;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [System.Serializable] class FileData { public string what; public string time; public List<SaveSystem.Item> items = new List<SaveSystem.Item>(); }
    static List<SaveSystem.Item> snapshot;
    static string FilePath => System.IO.Path.Combine(Application.persistentDataPath, "DebugRunRestore.json");

    public static void Begin(string what)
    {
        if (IsActive) End("begin again");
        snapshot = SaveSystem.CaptureCategory(SaveCategory.Progress);
        try
        {
            var f = new FileData { what = what, time = System.DateTime.Now.ToString("s") };
            f.items.AddRange(snapshot);
            System.IO.File.WriteAllText(FilePath, JsonUtility.ToJson(f));
        }
        catch (System.Exception e) { Debug.LogWarning("[DebugRun] could not write the restore file: " + e.Message); }
        IsActive = true;
        What = what;
        BlockedWrites = 0;
        Debug.Log($"[DebugRun] BEGIN '{what}' (progress keys captured: {snapshot.Count}) - this run is NOT recorded");
    }

    // 戻したキーの数を返す(0 = Debug Run の間に進行の保存が1つも起きなかった)
    public static int End(string why)
    {
        if (!IsActive) return 0;
        string note = "";
        int n = snapshot != null ? SaveSystem.RestoreCategory(snapshot, SaveCategory.Progress, out note) : 0;
        LastRestoredKeys = n;
        LastRestoreNote = n == 0 ? "" : note;
        IsActive = false;
        Debug.Log($"[DebugRun] END '{What}' ({why}) blocked saves={BlockedWrites} restoredKeys={n}{(n > 0 ? " [" + note + "]" : "")}");
        What = "";
        snapshot = null;
        try { if (System.IO.File.Exists(FilePath)) System.IO.File.Delete(FilePath); } catch { }
        return n;
    }

    // 起動時: 前回の Debug Run が終わらずに落ちていたら、控えへ戻す(SaveSystem の起動時処理から)
    public static void RecoverAfterCrash()
    {
        try
        {
            if (!System.IO.File.Exists(FilePath)) return;
            var f = JsonUtility.FromJson<FileData>(System.IO.File.ReadAllText(FilePath));
            if (f != null && f.items != null)
            {
                int n = SaveSystem.RestoreCategory(f.items, SaveCategory.Progress, out string note);
                Debug.LogWarning($"[DebugRun] previous Debug Run '{f.what}' ({f.time}) did not end cleanly - progress restored ({n} keys{(n > 0 ? ": " + note : "")})");
            }
            System.IO.File.Delete(FilePath);
        }
        catch (System.Exception e) { Debug.LogWarning("[DebugRun] crash recovery failed: " + e.Message); }
    }
#endif
}
