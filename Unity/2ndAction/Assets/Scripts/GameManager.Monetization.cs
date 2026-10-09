using UnityEngine;

// 広告/広告なしパスのゲーム側の入口(2026-10-10、依頼I)。Steam/PC(Monetization.Mode == None)では何も出さない・何もしない。
//  ・結果画面(脱出/FINISH の成功): 「MILE 2倍」= 通常の持ち帰り MILE(保存済み)と同じ額を、報酬広告の正規の通知(パスは広告なし)で1回だけ追加
//  ・結果画面(ゲームオーバー)から「ホームへ戻る」タップ: 条件が全部そろって広告が用意できている時だけ強制広告(無ければ待たずにホームへ)
//  ・ホームのガチャ: 「広告で1回引く」(1日3回、パスは広告なしで同じ枠)
public partial class GameManager
{
    // ---------------------------------------------------------------- ラン終了の時の控え
    int runsFinishedBeforeThisRun = -1; // このランが終わる前に終わっていたランの数(初回ランの後は強制広告を出さない)
    bool resultAdTried;                 // この結果画面で強制広告を試した(1回だけ)
    bool leavingResult;                 // 強制広告 → ホームへ の途中
    public string LastGameOverAdSkip { get; private set; } = ""; // 出さなかった理由(確認用)
    public int GameOverAdsShown { get; private set; }

    // ラン本番(チュートリアル/練習/闘技場/DEBUG RUN 以外)
    bool CountsForAds => !TutorialMode.Active && !ArenaMode.Active && !DebugRun.WritesBlocked;

    // FinishRun から(成功/ゲームオーバーとも1回)
    void NoteRunFinishedForAds()
    {
        resultAdTried = false; leavingResult = false; doubleBusy = false; doubleNote = "";
        runsFinishedBeforeThisRun = AdRewards.RunsFinished;
        doubleBase = RunMile; // 持ち帰り(カード倍率・リングの MILE を含めた確定額)。掛け直さない
        if (CountsForAds) { AdRewards.NoteRunFinished(); SaveStore.Save(); }
    }

    // マルチ: 全員の勝負が決まった後だけ(観戦中/復活待ちでは出さない)
    static bool MultiplayerSettled => !NetRunLauncher.IsMultiplayerRun || (NetMatch.Instance != null && NetMatch.Instance.RunOver);

    // ---------------------------------------------------------------- ゲームオーバー後の強制広告
    public bool ShouldShowGameOverAd(out string why)
    {
        why = "";
        if (Monetization.Ads == null) why = "no ad service";
        else if (IsWin) why = "success (ads only after game over)";
        else if (resultAdTried) why = "already tried on this result";
        else if (NoAdsPass.Owned) why = "no-ads pass";
        else if (!CountsForAds) why = "tutorial/practice/arena/debug run";
        else if (runsFinishedBeforeThisRun < 1) why = "first run";
        else if (!MultiplayerSettled) why = "multiplayer not settled";
        else if (!AdManager.SessionWarm) why = $"session {Time.realtimeSinceStartup:F0}s < {MonetizationConfig.SessionWarmupSeconds:F0}s";
        else if (!AdManager.CooldownOver && !AdManager.DevIgnoreTiming) why = $"cooldown {AdManager.SecondsSinceLastAd:F0}s < {MonetizationConfig.AdCooldownSeconds:F0}s";
        else if (!Monetization.Ads.IsReady(AdKind.Interstitial)) { Monetization.Ads.Load(AdKind.Interstitial); why = "not loaded (no wait)"; }
        return why.Length == 0;
    }

    // 結果画面のタップ(受け付けた)→ ホームへ。強制広告は条件がそろった時だけ、終わったら(失敗/閉じた/出なかった も)すぐホームへ
    void LeaveResult()
    {
        if (leavingResult) return;
        if (ShouldShowGameOverAd(out string why))
        {
            resultAdTried = true; leavingResult = true;
            DeathLog("Result -> interstitial ad before HOME");
            AdManager.Show(AdKind.Interstitial, "gameover", null, ok =>
            {
                if (ok) GameOverAdsShown++;
                leavingResult = false;
                RetryWithTransition();
            });
            return;
        }
        LastGameOverAdSkip = why;
        if (Monetization.Mode != MonetizationMode.None) Debug.Log($"[Ads] game over ad skipped: {why}");
        RetryWithTransition();
    }

    // ---------------------------------------------------------------- MILE 2倍(成功の結果画面)
    int doubleBase;
    bool doubleBusy;
    string doubleNote = "";
    Rect resultDoubleRect;
    public int DoubleBase => doubleBase;
    public string DoubleNote => doubleNote;

    string CurrentRunId => RunLedger.Current != null ? RunLedger.Current.runId : "";
    public bool DoubleOffered => Monetization.Mode != MonetizationMode.None && IsGameOver && IsWin && !QuietFinish && CountsForAds && doubleBase > 0 && !string.IsNullOrEmpty(CurrentRunId) && MultiplayerSettled;
    public bool DoubleClaimed => AdRewards.IsDoubled(CurrentRunId);

    float ResultMonetizationExtraHeight() => DoubleOffered ? 96f : 0f;

    // ResultTapThisFrame から: 結果画面のボタンの上のタップは「ホームへ」にしない
    bool PointerOnResultButton()
    {
        if (!DoubleOffered || resultDoubleRect.width <= 0f) return false;
        Vector2 p = Input.mousePosition;
        for (int i = 0; i < Input.touchCount; i++) if (Input.GetTouch(i).phase == TouchPhase.Began) { p = Input.GetTouch(i).position; break; }
        return resultDoubleRect.Contains(new Vector2(p.x, Screen.height - p.y));
    }

    void DrawResultMonetization(Rect panelRect)
    {
        if (!DoubleOffered) { resultDoubleRect = Rect.zero; return; }
        float pad = 24f;
        Rect area = new Rect(panelRect.x + pad, panelRect.yMax - 40f - 92f, panelRect.width - pad * 2f, 88f);
        Rect btn = new Rect(area.x, area.y, area.width, 50f);
        resultDoubleRect = btn;
        var note = new GUIStyle(UiKit.Label(15f, TextAnchor.MiddleCenter, false, new Color(0.85f, 0.88f, 0.95f))) { wordWrap = true };
        string sum = Loc.F("通常{0}MILE＋追加{1}MILE＝合計{2}MILE", doubleBase, doubleBase, doubleBase * 2);
        if (DoubleClaimed)
        {
            UiKit.Button(btn, Loc.T("MILE 2倍 受け取り済み"), 18f, true, false, false);
            GUI.Label(new Rect(area.x, btn.yMax + 4f, area.width, 30f), sum, note);
            return;
        }
        bool pass = NoAdsPass.Owned;
        string why = "";
        bool can = pass || AdManager.CanShowRewardedNow(out why);
        string label = pass ? Loc.T("広告なしパス: MILE 2倍を受け取る") : Loc.T("広告を見て MILE 2倍");
        bool enabled = !doubleBusy && !AdManager.Showing && !leavingResult;
        if (UiKit.Button(btn, label, 18f, true, false, enabled && can) && enabled)
        {
            if (can) StartDoubleClaim(pass);
            else doubleNote = why;
        }
        string line = !string.IsNullOrEmpty(doubleNote) ? Loc.Auto(doubleNote) : !can && !string.IsNullOrEmpty(why) ? Loc.Auto(why) : sum;
        GUI.Label(new Rect(area.x, btn.yMax + 4f, area.width, 32f), line, note);
    }

    void StartDoubleClaim(bool pass)
    {
        if (doubleBusy || AdManager.Showing || leavingResult) return; // 連打
        string runId = CurrentRunId; int amount = doubleBase;
        if (pass)
        {
            if (AdRewards.ClaimDouble(runId, amount, "pass")) { doubleNote = ""; PlayCoinSe(); }
            return;
        }
        doubleBusy = true; doubleNote = "";
        bool claimed = false;
        AdManager.Show(AdKind.Rewarded, "double", () =>
        {
            // 正規の報酬の通知(1回の表示で1回だけ。ランの ID ごとにも1回だけ)
            if (AdRewards.ClaimDouble(runId, amount, "ad")) { claimed = true; PlayCoinSe(); }
        }, ok =>
        {
            doubleBusy = false;
            if (!claimed && !AdRewards.IsDoubled(runId)) doubleNote = ok ? "最後まで見なかったため、追加の MILE は受け取れませんでした" : "広告を表示できませんでした。もう一度お試しください";
        });
    }

    void PlayCoinSe() { if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Coin); }

    // ---------------------------------------------------------------- 広告ガチャ(ホーム)
    public int AdGachaPulls { get; private set; }
    string adGachaNote = ""; float adGachaNoteUntil;
    bool adGachaBusy;
    public bool AdGachaAvailable => Monetization.Mode != MonetizationMode.None && !HasStarted;

    // ガチャの抽選(MILE のガチャと同じ抽選/同じ重複の扱い。支払いだけ無い)
    CardDefinition DrawGachaCardForAd()
    {
        var pool = BuildGachaPool();
        return pool.Count == 0 ? null : DrawFromGachaPool(pool);
    }

    void RevealAdGachaCard(CardDefinition card)
    {
        if (card == null) return;
        AdGachaPulls++;
        gachaRevealQueue.Add(card);
        if (!gachaResultOpen && gachaMachineShakeTimer <= 0f) gachaMachineShakeTimer = gachaMachineShakeDuration;
        deskHotspotFlashTimer = roomHotspotFlashDuration;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Gacha);
    }

    public void OnAdGachaTapped()
    {
        if (!AdGachaAvailable || adGachaBusy || AdManager.Showing) return;
        string day = TrustedClock.DayKey;
        if (AdRewards.GachaRemaining(day) <= 0) { SetAdGachaNote(Loc.F("本日の回数は終わりました(あと{0}で回復)", FormatUntilNextDay())); return; }
        if (NoAdsPass.Owned)
        {
            TrustedClock.NoteUse();
            RevealAdGachaCard(AdRewards.ClaimGachaPull(day, "pass", DrawGachaCardForAd));
            return;
        }
        if (!AdManager.CanShowRewardedNow(out string why)) { SetAdGachaNote(Loc.Auto(why)); return; }
        adGachaBusy = true;
        bool got = false;
        AdManager.Show(AdKind.Rewarded, "gacha", () =>
        {
            // 正規の報酬の通知の時に、抽選 + カード + 回数 を同じ保存で確定(再起動で引き直せない)
            string d = TrustedClock.DayKey;
            TrustedClock.NoteUse();
            var card = AdRewards.ClaimGachaPull(d, "ad", DrawGachaCardForAd);
            if (card != null) { got = true; RevealAdGachaCard(card); }
        }, ok =>
        {
            adGachaBusy = false;
            if (!got) SetAdGachaNote(ok ? Loc.T("最後まで見なかったため、引けませんでした(回数は減っていません)") : Loc.T("広告を表示できませんでした(回数は減っていません)"));
        });
    }

    void SetAdGachaNote(string s) { adGachaNote = s; adGachaNoteUntil = Time.unscaledTime + 3.5f; }

    static string FormatUntilNextDay()
    {
        var t = TrustedClock.UntilNextDay;
        int h = Mathf.Max(0, (int)t.TotalHours), m = Mathf.Max(0, t.Minutes);
        return h > 0 ? Loc.F("{0}時間{1}分", h, m) : Loc.F("{0}分", Mathf.Max(1, m));
    }

    // ガチャ機の近くの小さなボタン(横画面: 機械の下 / 縦画面: 施設のボタンの上)
    void DrawAdGachaButton(Rect anchor, bool interactable, float fade)
    {
        if (!AdGachaAvailable || fade < 0.99f) return;
        string day = TrustedClock.DayKey;
        int left = AdRewards.GachaRemaining(day);
        bool pass = NoAdsPass.Owned;
        float s = GachaUiScale;
        float w = Mathf.Max(anchor.width, 190f * s), h = 54f * s;
        Rect r = HomePortrait
            ? new Rect(anchor.xMax - w, anchor.y - h - 8f, w, h)
            : new Rect(anchor.center.x - w / 2f, anchor.yMax + 6f, w, h);
        r.x = Mathf.Clamp(r.x, SafeLeft() + 8f, Screen.width - SafeRight() - 8f - r.width);
        string title = pass ? Loc.T("無料で1回引く(パス)") : Loc.T("広告で1回引く");
        string sub = left > 0 ? Loc.F("本日あと{0}回", left) : Loc.F("次の回復まで {0}", FormatUntilNextDay());
        bool ok = interactable && left > 0 && !adGachaBusy && !AdManager.Showing;
        Rect top = new Rect(r.x, r.y, r.width, h * 0.62f);
        if (UiKit.Button(r, "", 16f * s, false, false, ok) && interactable) OnAdGachaTapped();
        var st = UiKit.Label(15f * s, TextAnchor.MiddleCenter, true, ok ? new Color(1f, 0.9f, 0.6f) : new Color(0.7f, 0.72f, 0.78f));
        GUI.Label(top, title, st);
        GUI.Label(new Rect(r.x, r.y + h * 0.5f, r.width, h * 0.5f), sub, UiKit.Label(12f * s, TextAnchor.MiddleCenter, false, new Color(0.85f, 0.88f, 0.95f)));
        if (!string.IsNullOrEmpty(adGachaNote) && Time.unscaledTime < adGachaNoteUntil)
        {
            var ns = new GUIStyle(UiKit.Label(13f * s, TextAnchor.MiddleCenter, false, new Color(1f, 0.85f, 0.6f))) { wordWrap = true };
            float nw = Mathf.Max(r.width, 300f * s);
            Rect nr = new Rect(Mathf.Clamp(r.center.x - nw / 2f, 8f, Screen.width - nw - 8f), HomePortrait ? r.y - 52f * s : r.yMax + 4f, nw, 48f * s);
            UiBackdrop.Draw(nr, 0.8f);
            GUI.Label(nr, adGachaNote, ns);
        }
    }
}
