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
//  ・証明書・Bundle ID・署名には触れない。
//  ・App Store に出す版を上げる時は MarketingVersion をここで変える(例 1.0.0 → 1.0.1)。
// 2026-11-15 夜(マスター依頼: TestFlight 用の再ビルドは Build 5): iOS のビルド番号(CFBundleVersion)もここで決める。
//  ・ProjectSettings.asset はリポジトリへ入れない決まり(Android の versionCode 等の作業中の値が入るため)なので、
//    Unity Cloud がリポジトリから取った時に確実にこの番号になるよう、Version と同じくビルドの直前に BuildNumber へ差し替えて、後で戻す。
//  ・TestFlight / App Store Connect へ次に上げる時は、BuildNumber を 1 つ上げてコミットする(同じ Version で同じ番号は上げられない)
public class IosVersion : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    public const string MarketingVersion = "1.0.0";
    public const string BuildNumber = "7"; // 2026-11-22 マスター依頼: iOS の広告(テスト広告)を入れた TestFlight の版は Build 7(Version 1.0.0 のまま)
    // Apple の決まり: 1〜3 個の 0 以上の整数を点でつないだ物
    static readonly Regex Valid = new Regex(@"^\d+(\.\d+){0,2}$");
    public static bool IsValid(string v) => !string.IsNullOrEmpty(v) && Valid.IsMatch(v);

    public int callbackOrder => -1000; // ほかのビルド前の処理より先に

    const string SavedKey = "IosVersion.SavedBundleVersion"; // 戻す値(エディターの一時的な控え。プロジェクトには残らない)
    const string SavedBuildKey = "IosVersion.SavedBuildNumber";

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.iOS) return;
        if (!IsValid(MarketingVersion)) throw new BuildFailedException($"IosVersion.MarketingVersion '{MarketingVersion}' is not a valid iOS version (digits and up to 2 dots)");
        if (!IsValid(BuildNumber)) throw new BuildFailedException($"IosVersion.BuildNumber '{BuildNumber}' is not a valid iOS build number (digits and up to 2 dots)");
        string was = PlayerSettings.bundleVersion;
        if (was == MarketingVersion) SessionState.EraseString(SavedKey);
        else { SessionState.SetString(SavedKey, was ?? ""); PlayerSettings.bundleVersion = MarketingVersion; }
        string wasBuild = PlayerSettings.iOS.buildNumber;
        if (wasBuild == BuildNumber) SessionState.EraseString(SavedBuildKey);
        else { SessionState.SetString(SavedBuildKey, wasBuild ?? ""); PlayerSettings.iOS.buildNumber = BuildNumber; }
        Debug.Log($"[IosVersion] iOS version '{was}' -> '{MarketingVersion}', build '{wasBuild}' -> '{BuildNumber}' for this build (restored after the build)");
        // 2026-11-22: この iOS ビルドの広告(テスト/本番)を記録に残す(Unity Cloud のログでも分かるように)。本番は OMM_ADS_PROD を iOS の定義に足した時だけ
        Debug.Log($"[IosAds] adsProfile={MonetizationConfig.AdsProfile} units={MonetizationConfig.UnitsLabel} rewarded={MonetizationConfig.RewardedUnit} interstitial={MonetizationConfig.InterstitialUnit}");
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
        if (saved != null)
        {
            SessionState.EraseString(SavedKey);
            if (PlayerSettings.bundleVersion == MarketingVersion) PlayerSettings.bundleVersion = saved;
            Debug.Log($"[IosVersion] restored bundleVersion '{saved}' ({why})");
        }
        string savedBuild = SessionState.GetString(SavedBuildKey, null);
        if (savedBuild != null)
        {
            SessionState.EraseString(SavedBuildKey);
            if (PlayerSettings.iOS.buildNumber == BuildNumber) PlayerSettings.iOS.buildNumber = savedBuild;
            Debug.Log($"[IosVersion] restored iOS buildNumber '{savedBuild}' ({why})");
        }
    }
}
