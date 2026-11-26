using UnityEngine;

// 初回チュートリアルの完了報酬(2026-11-26 依頼⑧): ガチャ10回分 = 5,000 MILE(固定。MILE 倍率は掛けない、ログイン特典とは別)。
//  ・同じセーブデータで1回だけ。状態は1つのキー(RewardKey)に持ち、MILE の加算と「受け取った」の記録を1回の保存で書く
//    (再起動/画面の開き直しで二重にならない)。
//  ・練習を最後まで終えた時(TutorialRun の End)に「受け取り待ち」にする。練習中は保存を止めている(DebugRun.WritesBlocked)ので、
//    MILE を加えるのは練習を出た後のホーム/ステージ選択(DailyLoginBonus.AtHome と同じ条件)。スキップは完了ではない(付与しない。後で最後までやれば付与)。
//  ・2回目以降の練習(再プレイ)では付与しない(受け取り済み)。
//  ・既存プレイヤー: この仕組みを入れた後の最初の起動で、練習が済み(PracticeDone)のデータは「既存プレイヤーの受け取り待ち」にして、
//    ホームで1回だけ付与する(既存プレイヤー向けのお知らせ)。新しいデータは 0 から(完了した時に受け取る)。
public class TutorialClearReward : MonoBehaviour
{
    public const string RewardKey = "Tutorial.ClearReward"; // 0=まだ / 1=完了した・受け取り待ち / 2=受け取り済み / 3=既存プレイヤー・受け取り待ち
    public const int Amount = 5000;                          // ガチャ10回分(1回 500 MILE)
    public const int StateNone = 0, StatePending = 1, StateClaimed = 2, StateLegacyPending = 3;

    // ID 11114〜11117(2026-11-26): 訳が入るまでは、その言語では文字なしの「+5,000 MILE」(英字の用語と数字だけ)
    public const string ToastText = "チュートリアル完了！ {0} MILE獲得";                                       // ID 11114
    public const string LegacyTitle = "チュートリアル完了報酬";                                                // ID 11115
    public const string LegacyBody = "すでにチュートリアルを完了しているため、報酬として {0} MILE をお届けしました。ガチャ10回分です。"; // ID 11116
    public const string LegacyToast = "チュートリアル完了報酬：{0} MILE獲得";                                   // ID 11117(お知らせが出せない時)

    public static int State => SaveStore.GetInt(RewardKey, StateNone);
    public static bool Claimed => State == StateClaimed;
    public static string AmountText => Amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    public static int GrantsThisSession { get; private set; } // 確認用
    public static string LastResult { get; private set; } = "";

    // セーブの起動時処理から(SaveSystem.Boot)。この仕組みより前のデータで練習が済みなら、既存プレイヤーの受け取り待ちにする
    public static string EnsureInitialized()
    {
        if (SaveStore.HasKey(RewardKey)) return "";
        bool existing = TutorialProgress.PracticeDone;
        SaveStore.SetInt(RewardKey, existing ? StateLegacyPending : StateNone);
        return existing ? "tutorial reward: existing player -> 5,000 MILE at home; " : "";
    }

    // 練習を最後まで終えた時(TutorialRun)。まだ受け取っていなければ受け取り待ちにする(MILE は練習を出た後)
    public static void OnTutorialCompleted()
    {
        int s = State;
        if (s == StateClaimed || s == StatePending) { LastResult = $"completed again (state {s}) - nothing"; return; }
        SaveStore.SetInt(RewardKey, StatePending); // 既存プレイヤーの受け取り待ち(3)も、完了したので通常の受け取り待ち(1)へ(どちらも1回だけ)
        SaveStore.Save();
        LastResult = "completed -> pending";
        Debug.Log("[TutorialReward] tutorial completed -> 5,000 MILE pending (granted when back at home/stage select)");
    }

    // 受け取り待ちなら加える(ホーム/ステージ選択で、保存できる時だけ)。加えたら true
    public static bool TryGrant(string via)
    {
        int s = State;
        if (s != StatePending && s != StateLegacyPending) return false;
        if (DebugRun.WritesBlocked || DebugRun.BlocksSave("TutorialReward")) { LastResult = "save blocked - later"; return false; }
        var gm = GameManager.Instance;
        if (gm == null) { LastResult = "no game manager - later"; return false; }
        gm.AddMileWithoutFlush(Amount);                 // 財布へ(MILE 倍率なし。ランの獲得・記録には入れない)
        SaveStore.SetInt(RewardKey, StateClaimed);
        SaveStore.Save();                                // MILE と「受け取った」を1回の保存で
        GrantsThisSession++;
        bool legacy = s == StateLegacyPending;
        LastResult = $"granted ({(legacy ? "existing player" : "completed")}) via {via}";
        Debug.Log($"[TutorialReward] +{Amount} MILE ({(legacy ? "existing player" : "tutorial completed")}, via {via}) wallet={gm.TotalOwnedMile}");
        if (legacy && Loc.Ready(LegacyTitle) && Loc.Ready(LegacyBody))
            NoticeQueue.Enqueue("tutorial_clear_reward", Loc.T(LegacyTitle), Loc.F(LegacyBody, AmountText), null);
        else if (legacy) NoticeQueue.Toast(Loc.Ready(LegacyToast) ? Loc.F(LegacyToast, AmountText) : "+" + AmountText + " MILE");
        else NoticeQueue.Toast(Loc.Ready(ToastText) ? Loc.F(ToastText, AmountText) : "+" + AmountText + " MILE");
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Coin);
        return true;
    }

    public static TutorialClearReward Instance { get; private set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 自動試験(-qa…/-netAuto…)では、ホームでの自動の付与を止める(MILE の額を比べる既存の試験を乱さない)。この報酬の試験は -tutorialRewardOn
        var a = QaArgs.All;
        foreach (var x in a) if ((x.StartsWith("-qa") || x.StartsWith("-netAuto")) && System.Array.IndexOf(a, "-tutorialRewardOn") < 0) { devQuiet = true; break; }
#endif
        var go = new GameObject("[TutorialClearReward]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<TutorialClearReward>();
    }

    static bool devQuiet;
    float pollT;
    void Update()
    {
        pollT -= Time.unscaledDeltaTime;
        if (pollT > 0f) return;
        pollT = 0.5f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (devQuiet) return;
#endif
        int s = State;
        if (s != StatePending && s != StateLegacyPending) return;
        if (!DailyLoginBonus.AtHome || NetSession.IsActive) return;
        TryGrant("home");
    }
}
