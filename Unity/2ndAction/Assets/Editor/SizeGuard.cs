using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 容量の見張り(2026-10-25 v1.0 容量監査)。ビルドのたびに自動で動く(止めはしない。警告だけ)。
//  ビルドの前: Assets/Art と Assets/Resources の画像を調べて、容量が増える設定を警告する(Builds/SizeGuard_<機種>.txt)
//   ・幅か高さが4の倍数でない → Windows では無圧縮(1画素 4バイト)になる。Tools/art/pad4.py で透明な画素を足してそろえる
//     (背景のつなぎ目/繰り返しの絵は足すとすき間が出るので例外。今の例外は KnownRaw に載せた物)
//   ・Android / Windows の上書きが無い → TextureImportRules の範囲の外に置いていないか
//   ・元の大きさが 4096 を超える
//  ビルドの後: 出力の大きさを Builds/SizeHistory.tsv に足し、同じ機種・同じ種類(開発版/製品版)の前回より 5% 以上増えたら警告する
//  メニュー: Tools/2ndAction/Size Guard: Scan Textures(バッチ: -executeMethod SizeGuard.Run)
public class SizeGuard : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    public int callbackOrder => 200;
    const string HistoryPath = "Builds/SizeHistory.tsv";
    const float GrowthWarn = 0.05f;

    // 4の倍数でなくてよい(わざと Windows で無圧縮/RGB24 のままにしている)所: 背景・地面・つながる絵・繰り返しの絵
    static readonly string[] KnownRaw =
    {
        "Assets/Art/Background/", "Assets/Art/LastCorridor/", "Assets/Art/VisualStyleV1/Ground/", "Assets/Art/Ground/", "Assets/Art/Cave/",
        "Assets/Art/Decoration/", "Assets/Art/Obstacles_v1/", "Assets/Resources/Scenery/", "Assets/Resources/Arena/", "Assets/Art/Brand/",
    };

    public void OnPreprocessBuild(BuildReport report)
    {
        var issues = Scan(out int scanned);
        string path = $"Builds/SizeGuard_{report.summary.platform}.txt";
        Directory.CreateDirectory("Builds");
        File.WriteAllText(path, Format(issues, scanned));
        int windowsRaw = issues.Count(i => i.kind == "windows-raw");
        if (issues.Count > 0) Debug.LogWarning($"[SizeGuard] {issues.Count} texture warnings ({windowsRaw} would be uncompressed on Windows) - see {path}");
        else Debug.Log($"[SizeGuard] textures OK ({scanned} checked)");
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        var s = report.summary;
        if (s.result != BuildResult.Succeeded && s.result != BuildResult.Unknown) return;
        long size = OutputSize(s.outputPath);
        if (size <= 0) return;
        bool dev = (s.options & BuildOptions.Development) != 0;
        string kind = $"{s.platform}{(dev ? "-dev" : "")}{(s.outputPath.EndsWith(".aab") ? "-aab" : "")}";
        long prev = 0;
        if (File.Exists(HistoryPath))
            foreach (var line in File.ReadAllLines(HistoryPath).Skip(1))
            {
                var c = line.Split('\t');
                if (c.Length >= 4 && c[1] == kind && long.TryParse(c[3], out long v)) prev = v;
            }
        if (!File.Exists(HistoryPath)) File.WriteAllText(HistoryPath, "date\tkind\tversion\tbytes\tdelta\toutput\n");
        long delta = prev > 0 ? size - prev : 0;
        File.AppendAllText(HistoryPath, $"{System.DateTime.Now:yyyy-MM-dd HH:mm}\t{kind}\t{PlayerSettings.bundleVersion} ({PlayerSettings.Android.bundleVersionCode})\t{size}\t{delta}\t{s.outputPath}\n");
        if (prev > 0 && size > prev * (1f + GrowthWarn))
            Debug.LogWarning($"[SizeGuard] {kind} grew {(size - prev) / 1048576f:F1}MB ({(size - (double)prev) / prev * 100:F1}%) since the last build: {prev:N0} -> {size:N0} bytes");
        else Debug.Log($"[SizeGuard] {kind} {size:N0} bytes (delta {delta:N0})");
    }

    static long OutputSize(string output)
    {
        try
        {
            if (File.Exists(output)) return new FileInfo(output).Length;
            string dir = Directory.Exists(output) ? output : Path.GetDirectoryName(output);
            if (!Directory.Exists(dir)) return 0;
            return new DirectoryInfo(dir).GetFiles("*", SearchOption.AllDirectories).Where(f => !f.FullName.Contains("DoNotShip")).Sum(f => f.Length);
        }
        catch { return 0; }
    }

    public struct Issue { public string kind, path, note; }

    public static List<Issue> Scan(out int scanned)
    {
        var list = new List<Issue>();
        var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art", "Assets/Resources" }).Select(AssetDatabase.GUIDToAssetPath).Distinct().ToList();
        scanned = 0;
        foreach (var p in paths)
        {
            if (!(AssetImporter.GetAtPath(p) is TextureImporter ti)) continue;
            scanned++;
            ti.GetSourceTextureWidthAndHeight(out int w, out int h);
            if (w > 4096 || h > 4096) list.Add(new Issue { kind = "huge", path = p, note = $"{w}x{h}" });
            if ((w % 4 != 0 || h % 4 != 0) && !KnownRaw.Any(p.StartsWith) && ti.wrapMode != TextureWrapMode.Repeat)
                list.Add(new Issue { kind = "windows-raw", path = p, note = $"{w}x{h} (pad4.py で4の倍数へ)" });
            if (TextureImportRules.Applies(p) && !ti.GetPlatformTextureSettings("Android").overridden)
                list.Add(new Issue { kind = "android-no-override", path = p, note = "" });
            if (TextureImportRules.StandaloneApplies(p) && !TextureImportRules.StandaloneOk(ti, p))
                list.Add(new Issue { kind = "windows-setting", path = p, note = "Apply Texture Import Rules を実行" });
        }
        return list;
    }

    static string Format(List<Issue> issues, int scanned)
    {
        var sb = new StringBuilder($"# SizeGuard {System.DateTime.Now:yyyy-MM-dd HH:mm}  checked {scanned}  warnings {issues.Count}\n");
        foreach (var g in issues.GroupBy(i => i.kind)) sb.AppendLine($"# {g.Key}: {g.Count()}");
        sb.AppendLine("kind\tpath\tnote");
        foreach (var i in issues) sb.AppendLine($"{i.kind}\t{i.path}\t{i.note}");
        return sb.ToString();
    }

    [MenuItem("Tools/2ndAction/Size Guard: Scan Textures")]
    public static void Run()
    {
        var issues = Scan(out int scanned);
        Directory.CreateDirectory("Builds");
        File.WriteAllText("Builds/SizeGuard_scan.txt", Format(issues, scanned));
        Debug.Log($"[SizeGuard] scan: {issues.Count} warnings in {scanned} textures -> Builds/SizeGuard_scan.txt");
    }
}
