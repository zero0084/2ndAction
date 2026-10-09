using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

// 広告ガチャの日付(日本時間 0時区切り)に使う時刻(2026-10-10、依頼I)。
//  ・通信できる時: HTTPS の応答の Date ヘッダー(Google の接続確認の URL。端末の時計とは無関係)で「サーバーの時刻 − 端末の時刻」の差を取り、
//    以後は端末の経過時間から計算する(端末の時計を変えても日付は変わらない)
//  ・取れない時: 端末の時計。ただし「今までに見た一番新しい時刻」より前へ戻った時は進めない(時計を戻して回数を復活させない)。
//    時計を先へ進めることはオフラインでは防げない(限界: 本格的には UGS Cloud Code などサーバー側で回数を持つ必要がある)
//  ・Steam 版では使わない(広告ガチャが無い)
public class TrustedClock : MonoBehaviour
{
    static TrustedClock inst;
    public const string LastSeenKey = "TrustedClockLastSeenV1"; // 見た一番新しい UTC(ticks)
    const string Url = "https://connectivitycheck.gstatic.com/generate_204";

    public static bool HasServerTime { get; private set; }
    static double serverMinusLocalSec;      // サーバーの時刻 − 端末の時刻(秒、表示用)
    static DateTime serverAtSync;           // 取れた時のサーバーの時刻
    static float syncedAtRealtime;          // その時の経過時間(端末の時計を変えても進み方は変わらない)
    public static string LastSyncNote { get; private set; } = "";
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static double DevShiftHours;     // 開発版: 日付の切り替えを試す
#endif

    public static void Ensure()
    {
        if (inst != null) return;
        var go = new GameObject("TrustedClock");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<TrustedClock>();
        inst.StartCoroutine(inst.Sync());
    }

    public static void Resync() { Ensure(); inst.StartCoroutine(inst.Sync()); }

    void OnApplicationPause(bool paused) { if (!paused) StartCoroutine(Sync()); }

    IEnumerator Sync()
    {
        using (var req = UnityWebRequest.Head(Url))
        {
            req.timeout = 8;
            yield return req.SendWebRequest();
            string date = req.GetResponseHeader("Date");
            if (req.result == UnityWebRequest.Result.Success && !string.IsNullOrEmpty(date) && DateTime.TryParse(date, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime server))
            {
                serverMinusLocalSec = (server - DateTime.UtcNow).TotalSeconds;
                serverAtSync = server;
                syncedAtRealtime = Time.realtimeSinceStartup;
                HasServerTime = true;
                LastSyncNote = $"server time ok (device clock {(serverMinusLocalSec >= 0 ? "-" : "+")}{Math.Abs(serverMinusLocalSec):F0}s)";
            }
            else LastSyncNote = $"server time unavailable ({req.result} {req.error})";
            Debug.Log($"[TrustedClock] {LastSyncNote}");
        }
        NoteSeen(NowUtc);
    }

    // 今の UTC(サーバーの時刻が取れていればそれ基準)
    public static DateTime NowUtc
    {
        get
        {
            DateTime t = HasServerTime ? serverAtSync.AddSeconds(Time.realtimeSinceStartup - syncedAtRealtime) : DateTime.UtcNow;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            t = t.AddHours(DevShiftHours);
#endif
            // 時計が戻った: 見た一番新しい時刻より前にはしない
            if (long.TryParse(SaveStore.GetString(LastSeenKey, ""), out long ticks))
            {
                var seen = new DateTime(ticks, DateTimeKind.Utc);
                if (t < seen) t = seen;
            }
            return t;
        }
    }

    static void NoteSeen(DateTime t)
    {
        if (long.TryParse(SaveStore.GetString(LastSeenKey, ""), out long ticks) && ticks >= t.Ticks) return;
        SaveStore.SetString(LastSeenKey, t.Ticks.ToString());
        SaveStore.Save();
    }

    // 日本時間の日付("2026-10-10")と、次の切り替えまでの時間
    public static string DayKey
    {
        get
        {
            var j = NowUtc.AddHours(MonetizationConfig.DayResetUtcOffsetHours);
            return j.ToString("yyyy-MM-dd");
        }
    }
    public static TimeSpan UntilNextDay
    {
        get
        {
            var j = NowUtc.AddHours(MonetizationConfig.DayResetUtcOffsetHours);
            return j.Date.AddDays(1) - j;
        }
    }

    // 回数を使う時に呼ぶ(使った時刻を「見た時刻」として残す = 戻しても復活しない)
    public static void NoteUse() => NoteSeen(NowUtc);
}
