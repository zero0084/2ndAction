using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// アプリ容量の調査(2026-10-18): Build Report と画像の取り込み設定を TSV に書き出す。
//   Unity.exe -batchmode -executeMethod SizeAudit.Run -sizeReport <buildreportのパス> -sizeOut <出力先>
// 書き出す物:
//   report_files.tsv   … ビルドの出力ファイル(役割・大きさ)。summary.totalSize の中身の確認用
//   report_assets.tsv  … 詰め込まれた素材ごとの大きさ(元の素材・型・出力先)
//   textures.tsv       … ビルドに入った画像の取り込み設定と Android / Windows(Standalone)の形式
public static class SizeAudit
{
    static string Arg(string name, string def)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return def;
    }

    public static void Run()
    {
        string src = Arg("-sizeReport", "Library/LastBuild.buildreport");
        string outDir = Arg("-sizeOut", "Builds/SizeAudit");
        Directory.CreateDirectory(outDir);
        // Build Report は Assets の下に置かないと読めない → 一時的に写して読んだら消す
        const string tmp = "Assets/__SizeAuditTmp.buildreport";
        File.Copy(src, tmp, true);
        AssetDatabase.ImportAsset(tmp, ImportAssetOptions.ForceSynchronousImport);
        var report = AssetDatabase.LoadAllAssetsAtPath(tmp).OfType<BuildReport>().FirstOrDefault();
        try
        {
            if (report == null) { Debug.LogError("SizeAudit: could not load " + src); return; }
            Dump(report, outDir);
        }
        finally
        {
            AssetDatabase.DeleteAsset(tmp);
        }
    }

    // 圧縮の見た目の比較: -astcFiles "a.png;b.png" -sizeOut <dir>
    // 元の絵を ASTC 4x4/5x5/6x6 で圧縮 → CPU で戻して PNG に書き、元との差(PSNR、不透明部分だけ)を出す
    public static void AstcCompare()
    {
        string outDir = Arg("-sizeOut", "Builds/SizeAudit/astc");
        Directory.CreateDirectory(outDir);
        var formats = Arg("-cmpFormats", "ASTC_4x4,ASTC_5x5,ASTC_6x6").Split(',').Select(s => (TextureFormat)System.Enum.Parse(typeof(TextureFormat), s)).ToArray(); // 2026-10-25: -cmpFormats BC7,DXT5 で Windows の形式も
        var sb = new StringBuilder("file\tformat\tbytes\tpsnr\n");
        foreach (var path in Arg("-astcFiles", "").Split(';').Where(s => s.Length > 0))
        {
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            src.LoadImage(File.ReadAllBytes(path));
            var orig = src.GetPixels32();
            string name = Path.GetFileName(Path.GetDirectoryName(path)) + "_" + Path.GetFileNameWithoutExtension(path);
            foreach (var f in formats)
            {
                var t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
                t.SetPixels32(orig); t.Apply();
                EditorUtility.CompressTexture(t, f, TextureCompressionQuality.Best);
                long bytes = t.GetRawTextureData().LongLength;
                var dec = t.GetPixels32();
                double se = 0; long cnt = 0;
                for (int i = 0; i < orig.Length; i++)
                {
                    if (orig[i].a < 16) continue;
                    int dr = orig[i].r - dec[i].r, dg = orig[i].g - dec[i].g, db = orig[i].b - dec[i].b;
                    se += dr * dr + dg * dg + db * db; cnt += 3;
                }
                double psnr = cnt > 0 && se > 0 ? 10 * System.Math.Log10(255.0 * 255.0 / (se / cnt)) : 99;
                var o = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
                o.SetPixels32(dec); o.Apply();
                File.WriteAllBytes(Path.Combine(outDir, $"{name}_{f}.png"), o.EncodeToPNG());
                sb.AppendLine($"{path}\t{f}\t{bytes}\t{psnr:F2}");
                Object.DestroyImmediate(t); Object.DestroyImmediate(o);
            }
            Object.DestroyImmediate(src);
        }
        File.WriteAllText(Path.Combine(outDir, "astc_compare.tsv"), sb.ToString());
        Debug.Log("SizeAudit: astc compare written to " + outDir);
    }

    static void Dump(BuildReport report, string outDir)
    {
        var s = report.summary;
        var sb = new StringBuilder();
        sb.AppendLine($"# platform={s.platform} result={s.result} totalSize={s.totalSize} output={s.outputPath} options={s.options}");
        sb.AppendLine("path\trole\tsize");
        foreach (var f in report.GetFiles()) sb.AppendLine($"{f.path}\t{f.role}\t{f.size}");
        File.WriteAllText(Path.Combine(outDir, "report_files.tsv"), sb.ToString());

        var texSizes = new Dictionary<string, long>();
        var assetTypes = new Dictionary<string, string>();
        sb.Clear();
        sb.AppendLine("source\ttype\tpackedSize\tfile");
        foreach (var pa in report.packedAssets)
        {
            foreach (var c in pa.contents)
            {
                string type = c.type != null ? c.type.Name : "?";
                sb.AppendLine($"{c.sourceAssetPath}\t{type}\t{c.packedSize}\t{pa.shortPath}");
                if (type == "Texture2D" && !string.IsNullOrEmpty(c.sourceAssetPath))
                {
                    texSizes.TryGetValue(c.sourceAssetPath, out long v);
                    texSizes[c.sourceAssetPath] = v + (long)c.packedSize;
                }
            }
        }
        File.WriteAllText(Path.Combine(outDir, "report_assets.tsv"), sb.ToString());

        // 画像の取り込み設定(ビルドに入った画像だけ)
        var getStorage = System.Type.GetType("UnityEditor.TextureUtil, UnityEditor")?.GetMethod("GetStorageMemorySizeLong",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
        sb.Clear();
        sb.AppendLine("path\tpackedAndroid\tsrcW\tsrcH\timpW\timpH\tfileBytes\ttextureType\tspriteMode\tppu\tdefaultMax\tcompression\tcrunched\tandroidOverride\tandroidFormat\tandroidMax\tfinalFormat\tmipmap\treadable\talphaSource\tnpot\tfilter\tsprites\tpcOverride\tpcFormat\tpcMax\tpcFinalFormat");
        foreach (var kv in texSizes.OrderByDescending(k => k.Value))
        {
            string p = kv.Key;
            var ti = AssetImporter.GetAtPath(p) as TextureImporter;
            if (ti == null) { sb.AppendLine($"{p}\t{kv.Value}\t\t\t\t\t\t(not TextureImporter)"); continue; }
            ti.GetSourceTextureWidthAndHeight(out int sw, out int sh);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            var android = ti.GetPlatformTextureSettings("Android");
            var auto = ti.GetAutomaticFormat("Android");
            var pc = ti.GetPlatformTextureSettings("Standalone"); // 2026-10-25: Windows の形式も
            var pcAuto = ti.GetAutomaticFormat("Standalone");
            long fileBytes = File.Exists(p) ? new FileInfo(p).Length : 0;
            int spriteCount = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Sprite>().Count();
            sb.AppendLine(string.Join("\t", new object[] {
                p, kv.Value, sw, sh, tex != null ? tex.width : 0, tex != null ? tex.height : 0, fileBytes,
                ti.textureType, ti.spriteImportMode, ti.spritePixelsPerUnit, ti.maxTextureSize, ti.textureCompression, ti.crunchedCompression,
                android.overridden, android.format, android.maxTextureSize, android.overridden ? android.format.ToString() : auto.ToString(),
                ti.mipmapEnabled, ti.isReadable, ti.alphaSource, ti.npotScale, ti.filterMode, spriteCount,
                pc.overridden, pc.format, pc.maxTextureSize, pc.overridden ? pc.format.ToString() : pcAuto.ToString() }));
        }
        File.WriteAllText(Path.Combine(outDir, "textures.tsv"), sb.ToString());
        Debug.Log($"SizeAudit: wrote {outDir} (files={report.GetFiles().Length} packedAssets={report.packedAssets.Length} textures={texSizes.Count})");
    }
}
