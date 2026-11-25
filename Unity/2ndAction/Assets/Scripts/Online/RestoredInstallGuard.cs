using System;
using System.IO;
using Unity.Services.Authentication;
using UnityEngine;

// Android の自動バックアップ/機種変更の転送からの復元(2026-11-25 セキュリティ監査、マスター依頼)。
//  ・セーブ(PlayerPrefs)はバックアップに残す(機種変更でセーブが戻る)。UGS の匿名サインインの情報(セッション)も同じ PlayerPrefs に
//    入っているので、そのままでは別の端末で同じ UGS アカウントとして入れてしまう。
//  ・この端末へのインストールの印を「バックアップされない場所」(Android の no_backup)と PlayerPrefs の両方に書く。
//    PlayerPrefs の印はあるのに no_backup の印が無い/違う = バックアップから戻した(別の端末/入れ直し)→ サインインの前に UGS のセッションを消す
//    (新しい匿名のプレイヤーになる。セーブはそのまま)。
//  ・今の版へ更新した既存の端末(PlayerPrefs の印が無い)は、初回として印を書くだけ(セッションは残す = 今の UGS アカウントのまま)。
//  ・何度実行しても同じ結果(印が合っていれば何もしない)。Android 以外は何もしない。
public static class RestoredInstallGuard
{
    const string PrefKey = "Device.InstallIdV1";
    const string FileName = "omm_install_id";
    public static string LastResult { get; private set; } = "";

    public static void BeforeSignIn()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            string dir = NoBackupDir();
            if (string.IsNullOrEmpty(dir)) { LastResult = "no no_backup dir"; return; }
            string path = Path.Combine(dir, FileName);
            string local = File.Exists(path) ? File.ReadAllText(path).Trim() : "";
            string saved = PlayerPrefs.GetString(PrefKey, "");
            if (!string.IsNullOrEmpty(saved) && saved == local) { LastResult = "same install"; return; }
            if (!string.IsNullOrEmpty(saved) && saved != local)
            {
                // バックアップから戻したセーブ(この端末のインストールではない)
                if (AuthenticationService.Instance.SessionTokenExists && !AuthenticationService.Instance.IsSignedIn)
                {
                    AuthenticationService.Instance.ClearSessionToken();
                    LastResult = "restored from a backup: online sign-in reset (the save is kept)";
                }
                else LastResult = "restored from a backup (no session to reset)";
                Debug.Log("[Online] " + LastResult);
            }
            else LastResult = "first run of this version on this install";
            string id = string.IsNullOrEmpty(local) ? Guid.NewGuid().ToString("N") : local;
            if (local != id) File.WriteAllText(path, id);
            PlayerPrefs.SetString(PrefKey, id);
            PlayerPrefs.Save();
        }
        catch (Exception e) { LastResult = "error " + e.GetType().Name; Debug.LogWarning("[Online] install check failed: " + e.GetType().Name); }
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    static string NoBackupDir()
    {
        using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var f = act.Call<AndroidJavaObject>("getNoBackupFilesDir"))
            return f.Call<string>("getAbsolutePath");
    }
#endif
}
