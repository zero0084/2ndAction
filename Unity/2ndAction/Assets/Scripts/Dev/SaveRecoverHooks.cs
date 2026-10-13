#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

// セーブの消失からの復旧の確認(2026-10-13、開発版のみ)。セーブの起動時処理(SaveSystem.Boot)より前に、テスト用データだけを壊す。
//  -srLoseAtStart        … テスト用データの保存キー(test:)を全部消す(バックアップのファイルは残す)= 端末の保存データだけが消えた状態
//  -srCorrupt lastgood|prev|both … テスト用データのバックアップを途中で切れた状態にする(先頭 40% だけ残す)
// 通常のデータでは何もしない(SaveProfile がテスト用データの時だけ)。
public static class SaveRecoverHooks
{
    public static string Done = "";
    public static readonly List<string> DamagedHashes = new List<string>(); // 壊したファイルの中身(壊した後)の SHA-1。救済用に残っているかを確かめる

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    static void PreBoot()
    {
        var a = QaArgs.All;
        bool lose = false; string corrupt = null;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == "-srLoseAtStart") lose = true;
            if (a[i] == "-srCorrupt" && i + 1 < a.Length) corrupt = a[i + 1];
        }
        if (!lose && corrupt == null) return;
        if (!SaveProfile.IsTest) { Debug.LogWarning("[SaveRecoverHooks] ignored: only for the test data"); Done = "ignored (normal data)"; return; }
        var log = new List<string>();
        if (corrupt != null)
        {
            string dir = System.IO.Path.Combine(Application.persistentDataPath, SaveProfile.BackupSubdir(true));
            var names = corrupt == "both" ? new[] { "lastgood", "lastgood_prev" } : corrupt == "prev" ? new[] { "lastgood_prev" } : new[] { "lastgood" };
            foreach (var n in names)
            {
                string p = System.IO.Path.Combine(dir, n + ".json");
                if (!System.IO.File.Exists(p)) { log.Add($"{n}: missing"); continue; }
                byte[] all = System.IO.File.ReadAllBytes(p);
                byte[] cut = new byte[all.Length * 2 / 5];
                System.Array.Copy(all, cut, cut.Length);
                System.IO.File.WriteAllBytes(p, cut);
                DamagedHashes.Add(Sha1(cut));
                log.Add($"{n}: cut {all.Length}->{cut.Length} bytes");
            }
        }
        if (lose)
        {
            int n = SaveProfile.DeleteTestData(true);
            log.Add($"deleted {n} test keys (backup files kept)");
        }
        Done = string.Join("; ", log);
        Debug.Log("[SaveRecoverHooks] " + Done);
    }

    public static string Sha1(byte[] b)
    {
        using (var h = System.Security.Cryptography.SHA1.Create()) return System.BitConverter.ToString(h.ComputeHash(b)).Replace("-", "");
    }
}
#endif
