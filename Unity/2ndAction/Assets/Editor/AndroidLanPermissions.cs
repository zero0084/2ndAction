using System.IO;
using UnityEditor.Android;
using UnityEngine;

// LAN の部屋の自動発見(2026-10-05)に必要な最小限の権限を、生成された Android のマニフェストへ足す。
//  ACCESS_WIFI_STATE           … WifiManager(MulticastLock を作る)
//  CHANGE_WIFI_MULTICAST_STATE … MulticastLock の取得(探している/知らせている間だけ取り、止めたら放す)
// どちらも「通常の権限」(インストール時に付与、実行時の許可ダイアログなし)。INTERNET / ACCESS_NETWORK_STATE は Unity が付ける。
// 位置情報や近くのデバイスの権限は使わない(UDP のブロードキャストなので不要)。
public class AndroidLanPermissions : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 10;

    static readonly string[] Permissions = { "android.permission.ACCESS_WIFI_STATE", "android.permission.CHANGE_WIFI_MULTICAST_STATE" };

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
        if (add.Length == 0) return;
        xml = xml.Insert(insertAt, add.ToString());
        File.WriteAllText(manifest, xml);
        Debug.Log("[LAN] Android permissions added:" + add.ToString().Replace("\n", " "));
    }
}
