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
