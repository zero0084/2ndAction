using UnityEngine;

// Android の Wi-Fi は省電力のためブロードキャスト/マルチキャストの受信を捨てる端末が多い。
// 部屋を探している間/知らせている間だけ WifiManager.MulticastLock を取り、止めたら必ず放す(LanDiscovery.Refresh が呼ぶ)。
// 必要な権限: CHANGE_WIFI_MULTICAST_STATE / ACCESS_WIFI_STATE(どちらも「通常の権限」= 実行時の許可ダイアログは出ない)。
// Android 以外では何もしない。
public static class LanMulticastLock
{
    public static bool Held { get; private set; }
#if UNITY_ANDROID && !UNITY_EDITOR
    static AndroidJavaObject lockObj;
#endif

    public static void Set(bool on)
    {
        if (on == Held) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (on)
            {
                if (lockObj == null)
                {
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    using (var ctx = activity.Call<AndroidJavaObject>("getApplicationContext"))
                    using (var wifi = ctx.Call<AndroidJavaObject>("getSystemService", "wifi"))
                    {
                        lockObj = wifi.Call<AndroidJavaObject>("createMulticastLock", "OneMoreMileLan");
                        lockObj.Call("setReferenceCounted", false);
                    }
                }
                lockObj.Call("acquire");
            }
            else if (lockObj != null && lockObj.Call<bool>("isHeld")) lockObj.Call("release");
            Held = on;
            Debug.Log($"[LAN] MulticastLock {(on ? "acquired" : "released")}");
        }
        catch (System.Exception e)
        {
            Held = false;
            Debug.LogWarning("[LAN] MulticastLock failed: " + e.Message);
        }
#else
        Held = on;
#endif
    }
}
