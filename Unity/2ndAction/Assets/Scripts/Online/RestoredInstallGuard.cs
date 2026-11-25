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
//  ・印を入れる前の版のバックアップ(印がどちらにも無い)から戻した時: 入れたばかりで一度も更新されていないのにサインインの情報があれば、同じく消す(2026-11-25 第4回)。
//    旧版からの普通の更新は「初めて入れた時刻 < 最後の更新の時刻」なので消さない。時刻が読めない時も消さない(アカウントを残す側へ倒す)。
//  ・何度実行しても同じ結果(印が合っていれば何もしない)。Android 以外は何もしない。
public static class RestoredInstallGuard
{
    const string PrefKey = "Device.InstallIdV1";
    const string FileName = "omm_install_id";
    public static string LastResult { get; private set; } = "";
    public static bool RestoredFromBackup { get; private set; }   // この起動でバックアップからの復元を見つけた
    public static long FirstInstallMs { get; private set; }  // 確認用
    public static long LastUpdateMs { get; private set; }

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
                // 前のアカウントのランキングの状態(参加/送信待ち/受付付きの記録/送信済み)は持ち込まない。
                //  新しい匿名のプレイヤーでは受付が使えず、参加(コードネームの公開の同意)もやり直してもらう。ローカルの BEST・進行・購入はそのまま
                Leaderboard.ForgetAfterDeletion();
                RestoredFromBackup = true;
                Debug.Log("[Online] " + LastResult);
            }
            else if (FreshInstallNotUpdated() && AuthenticationService.Instance.SessionTokenExists && !AuthenticationService.Instance.IsSignedIn)
            {
                // 2026-11-25 第4回: 印を入れる前の版のバックアップから戻した(印がどちらにも無いが、入れたばかりで一度も更新されていないのにサインインの情報がある)
                //  = 入れ直し/新しい端末への復元。旧版からの普通の更新(初めて入れた時刻 < 最後の更新の時刻)はここへ来ない = アカウントのまま
                AuthenticationService.Instance.ClearSessionToken();
                Leaderboard.ForgetAfterDeletion();
                RestoredFromBackup = true;
                LastResult = "restored from an older backup: online sign-in reset (the save is kept)";
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
    // 入れたばかりで一度も更新されていない(初めて入れた時刻 = 最後の更新の時刻)。分からない時は false(= アカウントを残す側)
    static bool FreshInstallNotUpdated()
    {
        try
        {
            using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var pm = act.Call<AndroidJavaObject>("getPackageManager"))
            using (var info = pm.Call<AndroidJavaObject>("getPackageInfo", act.Call<string>("getPackageName"), 0))
            {
                long first = info.Get<long>("firstInstallTime"), last = info.Get<long>("lastUpdateTime");
                FirstInstallMs = first; LastUpdateMs = last;
                return first > 0 && Math.Abs(last - first) < 2000;
            }
        }
        catch (Exception) { return false; }
    }

    static string NoBackupDir()
    {
        using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var f = act.Call<AndroidJavaObject>("getNoBackupFilesDir"))
            return f.Call<string>("getAbsolutePath");
    }
#endif
}
