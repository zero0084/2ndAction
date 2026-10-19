using System;
using UnityEngine;

// プライバシーポリシーと問い合わせ先(2026-10-19、Google Play の公開準備)。設定 →「データとプライバシー」から開く。
//  URL は Google サイトで公開しているページ(中身の文章はマスターとエステルが管理する。アプリからは開くだけ)
public static class PrivacyLinks
{
    public const string PolicyUrl = "https://sites.google.com/view/onemoremile-privacy";
    public const string ContactEmail = "tempestars0605@gmail.com";

    public static string LastError { get; private set; } = "";
    public static int Opened { get; private set; } // 確認用

    // 端末の既定のブラウザで開く。開けない時(ブラウザが無い等)は false(画面に URL を出してコピーできるようにする)
    public static bool OpenPolicy()
    {
        LastError = "";
        try
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 自動テスト: 本当にブラウザを開かず、開いたことだけ記録する
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-noOpenUrl") >= 0) { Opened++; Debug.Log("[PRIVACY] open policy (test, not opened): " + PolicyUrl); return true; }
#endif
            Application.OpenURL(PolicyUrl);
            Opened++;
            Debug.Log("[PRIVACY] open policy: " + PolicyUrl);
            return true;
        }
        catch (Exception e)
        {
            LastError = e.GetType().Name;
            Debug.LogWarning("[PRIVACY] could not open the policy page: " + LastError);
            return false;
        }
    }

    public static void Copy(string text)
    {
        try { GUIUtility.systemCopyBuffer = text ?? ""; } catch { }
    }
}
