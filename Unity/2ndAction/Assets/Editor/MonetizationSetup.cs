using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// 広告/課金の SDK の設定(2026-10-10、依頼I)。
//  ・AdMob のアプリ ID: マスターが本番のアプリ ID を決めるまでは Google のテスト用アプリ ID(ca-app-pub-3940256099942544~3347511713)
//  ・Android の依存(play-services-ads 等)を External Dependency Manager で解決する
// batch: -executeMethod MonetizationSetup.Apply
public static class MonetizationSetup
{
    public const string TestAndroidAppId = "ca-app-pub-3940256099942544~3347511713"; // Google のサンプル(テスト用)
    // 本番のアプリ ID(2026-10-26 マスターが AdMob で登録: OneMoreMile / Android)。テスト広告の版もこの ID + Google のテスト用広告ユニットで動かす
    public const string ProdAndroidAppId = "ca-app-pub-2863190967263764~2717237567";
    // iOS(2026-11-15 マスター指示): iOS 用の AdMob アプリをまだ作っていないので、Google 公式の iOS テスト用アプリ ID を
    //  GoogleMobileAdsSettings(Assets/Google Mobile Ads/Settings の iOS 欄)に入れている。iOS の広告機能は有効にしていない(OMM_ADMOB は Android だけ)。→ 2026-11-22 に iOS の広告を有効化し、本番のアプリ ID(下の ProdIosAppId)へ切り替えた。
    //  ※ 正式公開の前に、マスターの iOS 用 AdMob アプリ ID へ切り替えること(ProductionConfigAudit が NG で知らせる)
    public const string TestIosAppId = "ca-app-pub-3940256099942544~1458002511";
    // 2026-11-22(マスターが AdMob で iOS のアプリを作成: OneMoreMile / com.zero0084.action2nd)。iOS の広告を有効にした(テスト広告の版もこの ID + Google の iOS 用テスト広告ユニット)
    public const string ProdIosAppId = "ca-app-pub-2863190967263764~1910097904";

    [MenuItem("Tools/OneMoreMile/Monetization: Apply SDK Settings")]
    public static void Apply() => ApplyAndReport();

    // 戻り値: アプリ ID を書き換えたか(書き換えた同じ起動の中のビルドには古い ID が入ることがある → BuildClosedTest は作り直しを求める)
    public static bool ApplyAndReport()
    {
        bool changed = false;
        var t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("GoogleMobileAds.Editor.GoogleMobileAdsSettings")).FirstOrDefault(x => x != null);
        if (t == null) { Debug.LogError("[MonetizationSetup] Google Mobile Ads plugin not found"); return false; }
        var inst = (ScriptableObject)t.GetMethod("LoadInstance", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Invoke(null, null);
        var prop = t.GetProperty("GoogleMobileAdsAndroidAppId");
        string want = string.IsNullOrEmpty(ProdAndroidAppId) ? TestAndroidAppId : ProdAndroidAppId;
        if ((string)prop.GetValue(inst) != want)
        {
            prop.SetValue(inst, want); changed = true;
            EditorUtility.SetDirty(inst);
            AssetDatabase.SaveAssets();
        }
        Debug.Log($"[MonetizationSetup] AdMob Android app id = {prop.GetValue(inst)}");
        // 2026-11-22: iOS の欄も本番のアプリ ID にそろえる(Info.plist の GADApplicationIdentifier。Android のビルドには影響しない = changed に数えない)
        var iosProp = t.GetProperty("GoogleMobileAdsIOSAppId");
        if (iosProp != null && (string)iosProp.GetValue(inst) != ProdIosAppId)
        {
            iosProp.SetValue(inst, ProdIosAppId);
            EditorUtility.SetDirty(inst);
            AssetDatabase.SaveAssets();
        }
        if (iosProp != null) Debug.Log($"[MonetizationSetup] AdMob iOS app id = {iosProp.GetValue(inst)}");
        ResolveAndroid();
        return changed;
    }

    public static void ResolveAndroid()
    {
        var r = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("GooglePlayServices.PlayServicesResolver")).FirstOrDefault(x => x != null);
        if (r == null) { Debug.LogWarning("[MonetizationSetup] External Dependency Manager not found"); return; }
        var m = r.GetMethod("ResolveSync", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(bool) }, null);
        if (m == null) { Debug.LogWarning("[MonetizationSetup] ResolveSync not found"); return; }
        object ok = m.Invoke(null, new object[] { true });
        Debug.Log($"[MonetizationSetup] Android dependencies resolved: {ok}");
    }
}
