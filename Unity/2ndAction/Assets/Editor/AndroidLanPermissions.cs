using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.Android;
using UnityEngine;

// LAN の部屋の自動発見(2026-10-05)に必要な最小限の権限を、生成された Android のマニフェストへ足す。
//  ACCESS_WIFI_STATE           … WifiManager(MulticastLock を作る)
//  CHANGE_WIFI_MULTICAST_STATE … MulticastLock の取得(探している/知らせている間だけ取り、止めたら放す)
// どちらも「通常の権限」(インストール時に付与、実行時の許可ダイアログなし)。INTERNET / ACCESS_NETWORK_STATE は Unity が付ける。
// 位置情報や近くのデバイスの権限は使わない(UDP のブロードキャストなので不要)。
// 2026-11-21(マスター承認): Wi-Fi の権限があると Google Play は android.hardware.wifi を「必須」と推定し、Wi-Fi の無い端末
//  (Play Games on PC / Android XR 等)へ配信しない(v458 の公開の概要で警告)。Wi-Fi は必須ではない: ONLINE・広告・課金はインターネットだけ。
//  Wi-Fi の無い端末では MulticastLock の作成が失敗するが、LanMulticastLock が捕まえて記録するだけで、部屋を探す処理は続く。
//  → <uses-feature android:name="android.hardware.wifi" android:required="false" /> を明示する(明示すると権限からの推定より優先される)。
//  確認: Tools/ads/verify_closedtest.ps1 が AAB/APK の最終マニフェストで Wi-Fi が必須でないことを確かめる
public class AndroidLanPermissions : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 10;

    static readonly string[] Permissions = { "android.permission.ACCESS_WIFI_STATE", "android.permission.CHANGE_WIFI_MULTICAST_STATE" };
    const string WifiFeature = "android.hardware.wifi";

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
        if (!File.Exists(manifest)) { Debug.LogWarning("[LAN] AndroidManifest not found: " + manifest); return; }
        string xml = File.ReadAllText(manifest);
        int head = xml.IndexOf("<manifest", System.StringComparison.Ordinal);
        int insertAt = head >= 0 ? xml.IndexOf('>', head) + 1 : -1;
        if (insertAt <= 0) { Debug.LogWarning("[LAN] AndroidManifest has no <manifest> tag"); return; }
        var add = new System.Text.StringBuilder();
        foreach (var p in Permissions)
            if (!xml.Contains("\"" + p + "\"")) add.Append($"\n  <uses-permission android:name=\"{p}\" />");
        // 2026-11-21: Wi-Fi は「必須ではない」と明示する(既に wifi の uses-feature があれば触らない。必須のまま書かれていたら警告)
        if (!xml.Contains("\"" + WifiFeature + "\"")) add.Append($"\n  <uses-feature android:name=\"{WifiFeature}\" android:required=\"false\" />");
        else if (!Regex.IsMatch(xml, "<uses-feature[^>]*\"" + Regex.Escape(WifiFeature) + "\"[^>]*android:required=\"false\""))
            Debug.LogWarning("[LAN] AndroidManifest already declares " + WifiFeature + " without required=\"false\" - Google Play will treat Wi-Fi as required");
        if (add.Length == 0) return;
        xml = xml.Insert(insertAt, add.ToString());
        File.WriteAllText(manifest, xml);
        Debug.Log("[LAN] Android permissions/features added:" + add.ToString().Replace("\n", " "));
    }
}
