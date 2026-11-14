using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// iOS の版(CFBundleShortVersionString)の決め方(2026-11-15、マスター依頼: Unity Build Automation の初回 iOS ビルドが
//  「iOS Version has not been set up correctly ... '2026-11-06 2229' contains invalid characters」で止まった)。
//  ・PlayerSettings.bundleVersion は Android の versionName / Windows の版と共通の1つの値。Android は日付の版(BuildAndroid が毎回書く)を使う。
//  ・iOS は App Store の形(数字と点だけ、3つまで)が要る → iOS をビルドする時だけ、ビルドの直前に MarketingVersion へ差し替える。
//    ProjectSettings に保存された値(Android/Windows が使う)は書き換えない: ビルドの後に元へ戻す(クラウドビルドでも手元でも同じ)。
//  ・ビルド番号(CFBundleVersion = PlayerSettings.iOS.buildNumber)・証明書・Bundle ID・署名には触れない。
//  ・App Store に出す版を上げる時は MarketingVersion をここで変える(例 1.0.0 → 1.0.1)。
public class IosVersion : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    public const string MarketingVersion = "1.0.0";
    // Apple の決まり: 1〜3 個の 0 以上の整数を点でつないだ物
    static readonly Regex Valid = new Regex(@"^\d+(\.\d+){0,2}$");
    public static bool IsValid(string v) => !string.IsNullOrEmpty(v) && Valid.IsMatch(v);

    public int callbackOrder => -1000; // ほかのビルド前の処理より先に

    const string SavedKey = "IosVersion.SavedBundleVersion"; // 戻す値(エディターの一時的な控え。プロジェクトには残らない)

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.iOS) return;
        if (!IsValid(MarketingVersion)) throw new BuildFailedException($"IosVersion.MarketingVersion '{MarketingVersion}' is not a valid iOS version (digits and up to 2 dots)");
        string was = PlayerSettings.bundleVersion;
        if (was == MarketingVersion) { SessionState.EraseString(SavedKey); Debug.Log($"[IosVersion] iOS version = {MarketingVersion} (already set) buildNumber={PlayerSettings.iOS.buildNumber}"); return; }
        SessionState.SetString(SavedKey, was ?? "");
        PlayerSettings.bundleVersion = MarketingVersion;
        Debug.Log($"[IosVersion] iOS version '{was}' -> '{MarketingVersion}' for this build (restored after the build). buildNumber={PlayerSettings.iOS.buildNumber}");
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.iOS) return;
        Restore("postprocess");
    }

    // ビルドが途中で失敗して OnPostprocessBuild が呼ばれなかった時も、次にエディターが落ち着いた時に戻す
    [InitializeOnLoadMethod]
    static void RestoreAfterReload() => EditorApplication.delayCall += () => Restore("editor reload");

    static void Restore(string why)
    {
        string saved = SessionState.GetString(SavedKey, null);
        if (saved == null) return;
        SessionState.EraseString(SavedKey);
        if (PlayerSettings.bundleVersion == MarketingVersion) PlayerSettings.bundleVersion = saved;
        Debug.Log($"[IosVersion] restored bundleVersion '{saved}' ({why})");
    }
}
