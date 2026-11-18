using System;
using System.Collections.Generic;
using UnityEngine;

// ログイン特典(2026-11-18、マスター依頼): 「Windows 版」または「広告なしパスを持っている」プレイヤーに、毎日 5,000 MILE。
//  ・その日はじめてホームに入った時に、所持 MILE へ加える(ホームにいる間に日付が変わった時も、その日の分を1回)
//  ・広告なしパスを購入した時/「購入を復元」等で持っていると確かめた時は、その場で今日の分を(まだなら)加える
//  ・両方の条件を満たしても1日 5,000 MILE(受け取りは日付ごとに1回)。MILE 2倍の特典は掛けない(固定額)。来なかった日の分は繰り越さない
//  ・日付は広告ガチャと同じ TrustedClock.DayKey(日本時間 0時で切り替え。世界共通)。Windows 版はサーバーの時刻を取らない
//    (端末の時計。ただし一度見た時刻より前へは戻さない = 時計を戻して2回受け取れない。先へ進める操作はオフラインでは防げない)
//  ・保存: 所持 MILE と受け取った日(進行のデータ LoginBonusV1)を同じ1回の保存で書く。もう1つ、受け取った日を設定のデータ
//    (LoginBonusLastDayV1。セーブの初期化/はじめからでも残る)にも同じ保存で書き、同じ日に初期化して2回受け取ることを防ぐ
//  ・無料のモバイル版(パスなし)・iOS では何もしない
public class DailyLoginBonus : MonoBehaviour
{
    public const int Amount = 5000;
    public const string Key = "LoginBonusV1";              // 進行: 受け取った日/回数/合計/直近の記録
    public const string SharedDayKey = "LoginBonusLastDayV1"; // 設定: 最後に受け取った日(初期化でも消えない)
    // 文言(2026-11-18、エステルの訳を30言語へ): {0} = 額(3桁区切り)
    public const string ToastText = "本日のログイン特典：{0} MILE"; // ID 11060
    public const string PassBenefitText = "4. ログイン特典: 毎日、初めてホームに入った時に {0} MILE を受け取れます(購入当日から対象。受け取らなかった日の分は繰り越されません)"; // ID 11061
    public static string AmountText => Amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    [Serializable] class Data { public string lastDay = ""; public int claims; public long total; public List<string> log = new List<string>(); }
    static Data d;
    static Data D
    {
        get
        {
            if (d != null) return d;
            string raw = SaveStore.GetString(Key, "");
            try { d = string.IsNullOrEmpty(raw) ? new Data() : JsonUtility.FromJson<Data>(raw) ?? new Data(); } catch { d = new Data(); }
            return d;
        }
    }
    public static void Reload() { d = null; }

    public static int Claims => D.claims;
    public static long Total => D.total;
    public static string LastDay => D.lastDay;
    public static IReadOnlyList<string> Log => D.log;
    public static int GrantsThisSession { get; private set; }   // 確認用
    public static string LastResult { get; private set; } = ""; // 確認用: 最後に判断した理由

    // ---- 対象か
    public static bool IsWindowsEdition
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (devAs == "mobile") return false;
            if (devAs == "windows") return true;
#endif
            return Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;
        }
    }
    public static bool PassOwned => NoAdsPass.Owned;
    public static bool Eligible => IsWindowsEdition || PassOwned;
    public static string EligibleBy => IsWindowsEdition && PassOwned ? "windows+pass" : IsWindowsEdition ? "windows" : PassOwned ? "pass" : "none";

    public static string Today => TrustedClock.DayKey;
    public static bool ClaimedOn(string day) => !string.IsNullOrEmpty(day) && (D.lastDay == day || SaveStore.GetString(SharedDayKey, "") == day);
    public static bool ClaimedToday => ClaimedOn(Today);

    // 今日の分を加える(対象で、まだ受け取っていない時だけ)。加えたら true
    public static bool TryGrant(string via)
    {
        if (!Eligible) { LastResult = "not eligible (" + via + ")"; return false; }
        string day = Today;
        if (string.IsNullOrEmpty(day)) { LastResult = "no day"; return false; }
        if (ClaimedOn(day)) { LastResult = $"already claimed {day} ({via})"; return false; }
        if (DebugRun.WritesBlocked || DebugRun.BlocksSave("LoginBonus")) { LastResult = "save blocked (debug run) - later"; return false; }
        var gm = GameManager.Instance;
        if (gm == null) { LastResult = "no game manager - later"; return false; }
        D.lastDay = day;
        D.claims++;
        D.total += Amount;
        D.log.Add($"{day} +{Amount} by {EligibleBy} via {via}");
        if (D.log.Count > 20) D.log.RemoveRange(0, D.log.Count - 20);
        gm.AddMileWithoutFlush(Amount);                    // 財布へ(MILE 2倍は掛けない。ランの獲得・記録には入れない)
        SaveStore.SetString(Key, JsonUtility.ToJson(D));
        SaveStore.SetString(SharedDayKey, day);
        SaveStore.Save();                                   // MILE と受け取った日を1回の保存で
        TrustedClock.NoteUse();                             // 受け取った時刻を「見た時刻」に(時計を戻しても同じ日をもう一度にしない)
        GrantsThisSession++;
        LastResult = $"granted {day} by {EligibleBy} via {via}";
        Debug.Log($"[LoginBonus] +{Amount} MILE on {day} (eligible by {EligibleBy}, via {via}) wallet={gm.TotalOwnedMile} claims={D.claims}");
        NoticeQueue.Toast(Loc.F(ToastText, AmountText));
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Coin);
        return true;
    }

    // 広告なしパスを購入した/持っていると確かめた時(NoAdsPass から)
    public static void OnPassConfirmed(string via)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (devQuiet) { LastResult = "quiet (qa) - pass " + via; return; }
#endif
        TryGrant("pass " + via);
    }

    // ---- ホームに入った時(毎秒の見回り)
    public static DailyLoginBonus Instance { get; private set; }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    static bool devQuiet;
    static string devAs = ""; // 開発版の試験: -loginBonusAs mobile|windows(Windows の exe で無料のモバイル版の扱いを試す)
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var a = QaArgs.All;
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-loginBonusAs") devAs = a[i + 1];
        // 自動試験(-qa…/-netAuto…)では、ホームでの自動の付与を止める(MILE の額を比べる既存の試験を乱さない)。ログイン特典の試験は -loginBonusOn
        foreach (var x in a) if ((x.StartsWith("-qa") || x.StartsWith("-netAuto")) && System.Array.IndexOf(a, "-loginBonusOn") < 0) { devQuiet = true; break; }
#endif
        var go = new GameObject("[DailyLoginBonus]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<DailyLoginBonus>();
    }

    float pollT;
    public static bool AtHome
    {
        get
        {
            var gm = GameManager.Instance;
            return gm != null && !gm.HasStarted && !TitleScreen.Showing && !TutorialMode.Active && !ArenaMode.Active
                && !TutorialLauncher.Covering && !ArenaLauncher.Covering
                && !(ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning);
        }
    }

    void Update()
    {
        pollT -= Time.unscaledDeltaTime;
        if (pollT > 0f) return;
        pollT = 1f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (devQuiet) return;
#endif
        if (!Eligible || !AtHome || ClaimedToday) return;
        TryGrant("home");
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static string DevSummary => $"eligible={Eligible} by {EligibleBy} today={Today} claimedToday={ClaimedToday} last={D.lastDay} shared={SaveStore.GetString(SharedDayKey, "")} claims={D.claims} total={D.total} session={GrantsThisSession} result='{LastResult}'";
#endif
}
