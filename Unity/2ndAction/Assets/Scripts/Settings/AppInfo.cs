using System;
using UnityEngine;

// ゲームの情報(2026-10-26 公開前の案内の整備): バージョンの表示・問い合わせのメール・著作権の表記。
//  ・バージョンはビルドの情報から取る(手で書かない): バージョン名 = Application.version(ビルドの時に BuildAndroid が日時で付ける)、
//    Android は端末に入っている版番号(versionCode)も PackageManager から読む
//  ・問い合わせ先は PrivacyLinks.ContactEmail(マスター確認済み: ゲーム開発用の共通アドレス、公開可)
//  ・メールの件名は「[OneMoreMile] お問い合わせ」(マスター指定)。本文には、バージョンと利用環境を書く欄(分かる所は自動で入れる)
public static class AppInfo
{
    public const string Copyright = "© 2026 MIXGame";
    public const string ContactSubject = "[OneMoreMile] お問い合わせ";

    static string versionCode;
    // Android の版番号(読めない環境・Windows は空)
    public static string VersionCode
    {
        get
        {
            if (versionCode != null) return versionCode;
            versionCode = "";
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var pm = activity.Call<AndroidJavaObject>("getPackageManager"))
                using (var info = pm.Call<AndroidJavaObject>("getPackageInfo", activity.Call<string>("getPackageName"), 0))
                    versionCode = info.Get<int>("versionCode").ToString();
            }
            catch (Exception e) { Debug.LogWarning("[AppInfo] versionCode: " + e.GetType().Name); }
#endif
            return versionCode;
        }
    }

    // 画面に出すバージョン: 「2026-10-26 1933 (381)」(版番号が無い環境は日時だけ)
    public static string VersionLabel => string.IsNullOrEmpty(VersionCode) ? Application.version : $"{Application.version} ({VersionCode})";

    // 利用環境(問い合わせの本文の下書き用。端末の名前と OS)
    public static string Environment => $"{SystemInfo.deviceModel} / {SystemInfo.operatingSystem}";

    // 問い合わせの本文の下書き。日本語と英語を並べる(どの言語の人にも書く所が分かるように)
    public static string ContactBody =>
        "ゲームのバージョン / Game version: " + VersionLabel + "\n" +
        "利用環境 / Device & OS: " + Environment + "\n" +
        "(違っていたら直してください / Please correct if wrong)\n\n" +
        "お問い合わせ内容 / Your message:\n";

    public static string LastError { get; private set; } = "";
    public static int MailOpened { get; private set; } // 確認用

    // メールアプリを開く(件名と本文の下書きつき)。開けない時は false(画面にアドレスを出してコピーできるようにする)
    public static bool OpenContactMail()
    {
        LastError = "";
        string url = "mailto:" + PrivacyLinks.ContactEmail + "?subject=" + Uri.EscapeDataString(ContactSubject) + "&body=" + Uri.EscapeDataString(ContactBody);
        try
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 自動テスト: 本当にメールアプリを開かず、開いたことだけ記録する
            if (Array.IndexOf(System.Environment.GetCommandLineArgs(), "-noOpenUrl") >= 0 || Array.IndexOf(QaArgs.All, "-noOpenUrl") >= 0) { MailOpened++; Debug.Log("[AppInfo] open contact mail (test, not opened): " + url); return true; }
#endif
            Application.OpenURL(url);
            MailOpened++;
            Debug.Log("[AppInfo] open contact mail");
            return true;
        }
        catch (Exception e)
        {
            LastError = e.GetType().Name;
            Debug.LogWarning("[AppInfo] could not open the mail app: " + LastError);
            return false;
        }
    }
}
