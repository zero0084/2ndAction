using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

// Windows の会社名の変更(DefaultCompany → MIXGame、2026-11-25 マスター依頼)で、保存先が変わる物を新しい場所へ写す。
//  ・PlayerPrefs(進行・設定・中断データ CONTINUE・購入/広告の記録・ランキングの送信待ち・UGS のサインイン など全部)は
//    HKCU\Software\<会社名>\<製品名> のレジストリ → 値を「そのまま」(種類も中身も)写す。
//  ・ファイル(バックアップ SaveBackups・確定の控え・診断のログ)は %USERPROFILE%\AppData\LocalLow\<会社名>\<製品名> → 写す。
//  ・写すだけ(古い場所は消さない = 控えとして残る)。新しい場所に既にある値/ファイルは上書きしない。
//    最後に新しい場所へ「写し終えた」の印を書き、2回目からは何もしない(何度起動しても増えない・消えない)。
//  ・一番最初(SubsystemRegistration = セーブの読み込みより前)に動く。Windows の製品だけ(エディター・他の機種は何もしない)。
public static class WindowsSaveMigration
{
    public const string OldCompany = "DefaultCompany";
    const string MarkerValue = "OMM_MigratedFromDefaultCompany";
    public static string LastResult { get; private set; } = "";

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Run()
    {
        try { LastResult = Migrate(Application.companyName, Application.productName, Application.persistentDataPath); }
        catch (Exception e) { LastResult = "error " + e.GetType().Name; }
        Debug.Log("[SaveMigration] " + LastResult);
    }
#endif

    public static string Migrate(string company, string product, string newDataPath)
    {
        if (string.IsNullOrEmpty(company) || company == OldCompany) return "company name unchanged";
        string oldPath = $@"Software\{OldCompany}\{product}", newPath = $@"Software\{company}\{product}";
        if (RegCreateKeyExW(HKCU, newPath, 0, null, 0, KEY_READ | KEY_WRITE, IntPtr.Zero, out IntPtr nk, out _) != 0) return "cannot open the new registry key";
        try
        {
            if (RegQueryValueExW(nk, MarkerValue, IntPtr.Zero, out _, null, ref Zero) == 0) return "already migrated";
            int regCopied = 0, regKept = 0, files = 0;
            if (RegOpenKeyExW(HKCU, oldPath, 0, KEY_READ, out IntPtr ok) == 0)
            {
                try { CopyValues(ok, nk, ref regCopied, ref regKept); }
                finally { RegCloseKey(ok); }
            }
            // ファイル: LocalLow\DefaultCompany\<製品名> → 今の persistentDataPath
            try
            {
                var newDir = new DirectoryInfo(newDataPath);
                if (newDir.Parent != null && newDir.Parent.Parent != null)
                {
                    string oldDir = Path.Combine(newDir.Parent.Parent.FullName, OldCompany, product);
                    if (Directory.Exists(oldDir)) files = CopyDir(oldDir, newDataPath);
                }
            }
            catch (Exception) { /* ファイルは控えのみ。レジストリの移行は続ける */ }
            byte[] one = BitConverter.GetBytes(1);
            RegSetValueExW(nk, MarkerValue, 0, REG_DWORD, one, one.Length);
            return $"migrated from {OldCompany}: registry values copied {regCopied} (kept existing {regKept}), files copied {files}";
        }
        finally { RegCloseKey(nk); }
    }

    static void CopyValues(IntPtr from, IntPtr to, ref int copied, ref int kept)
    {
        for (int i = 0; ; i++)
        {
            int nameLen = 16384, dataLen = 0;
            var name = new StringBuilder(nameLen);
            int r = RegEnumValueW(from, i, name, ref nameLen, IntPtr.Zero, out int type, null, ref dataLen);
            if (r == ERROR_NO_MORE_ITEMS) break;
            if (r != 0 && r != ERROR_MORE_DATA) continue;
            byte[] data = new byte[Math.Max(1, dataLen)];
            nameLen = 16384; name = new StringBuilder(nameLen);
            if (RegEnumValueW(from, i, name, ref nameLen, IntPtr.Zero, out type, data, ref dataLen) != 0) continue;
            string n = name.ToString();
            int probe = 0;
            if (RegQueryValueExW(to, n, IntPtr.Zero, out _, null, ref probe) == 0) { kept++; continue; } // 新しい場所に既にある = 上書きしない
            if (RegSetValueExW(to, n, 0, type, data, dataLen) == 0) copied++;
        }
    }

    static int CopyDir(string from, string to)
    {
        int n = 0;
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from))
        {
            string dst = Path.Combine(to, Path.GetFileName(f));
            if (File.Exists(dst)) continue;
            File.Copy(f, dst); n++;
        }
        foreach (var d in Directory.GetDirectories(from)) n += CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
        return n;
    }

    // ---- Windows の API(このプロジェクトの .NET の版には Microsoft.Win32.Registry が無い) ----
    static readonly IntPtr HKCU = new IntPtr(unchecked((int)0x80000001));
    const int KEY_READ = 0x20019, KEY_WRITE = 0x20006, REG_DWORD = 4, ERROR_NO_MORE_ITEMS = 259, ERROR_MORE_DATA = 234;
    static int Zero = 0;
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int RegOpenKeyExW(IntPtr hKey, string subKey, int options, int samDesired, out IntPtr result);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int RegCreateKeyExW(IntPtr hKey, string subKey, int reserved, string cls, int options, int samDesired, IntPtr sec, out IntPtr result, out int disposition);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int RegEnumValueW(IntPtr hKey, int index, StringBuilder name, ref int nameLen, IntPtr reserved, out int type, byte[] data, ref int dataLen);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int RegQueryValueExW(IntPtr hKey, string name, IntPtr reserved, out int type, byte[] data, ref int dataLen);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int RegSetValueExW(IntPtr hKey, string name, int reserved, int type, byte[] data, int dataLen);
    [DllImport("advapi32.dll")] static extern int RegCloseKey(IntPtr hKey);
}
