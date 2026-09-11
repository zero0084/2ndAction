using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Orchestrates the card-draw presentation for a level-up choice:
// LEVEL UP announcement -> deck appears -> 3 cards fade/scale in -> flip
// face-up (each with a small Gold Edge Glow flash) -> briefly showcased
// (no tap yet - "今回引いたのはこの3枚" per the brief) -> cards shrink away
// and a horizontal 3-row "choice panel" fades in (icon + title + detailed
// description + key value per row, Vampire Survivors-style) -> player taps
// a ROW (not a card) -> the picked row glows and grows, the other two
// vanish, its icon gets a brief highlight -> the caller's onApply callback
// actually applies the upgrade (GameManager.ApplyUpgradeByCardId, passed in
// untouched) -> everything closes.
//
// レベルアップ選択UI改修(2026-09-11) - マスター提供の参考画像「レベル
// アップ演出案 - カードからアイコンへ(ヴァンサバ風選択UI)」に基づく2段階
// 化。「カードから引いた感(デッキ感)」はカード演出のまま残し、実際の
// 選択・比較はLevelUpChoiceRowUI 3行の横長UIへ切り替える - 「デッキ・
// カード=引く楽しさ」「横長選択UI=情報の読みやすさ・選びやすさ」という
// 役割分担(マスターの指示どおり)。
//
// This class only ever receives already-decided data (RewardCardData) and
// hands back the chosen card's id (a plain string) through onApply - it has
// no opinion on what the 3 choices are, how they were picked, or what
// picking one does (deck/probability/effect/EXP logic all live in
// GameManager/CardDatabase, untouched by this file, and RewardCardData
// itself is unchanged except for the new optional ValueLine field). Level
// Up Presentation pass (2026-09-02): everything below is the Presentation
// layer for that same, unchanged contract - existing StartSequence
// (cardData, onApply) signature and GameManager's call site are both
// untouched.
//
// Runs entirely on unscaled time/WaitForSecondsRealtime since it's active
// while Time.timeScale == 0.
public class RewardCardSequence : MonoBehaviour
{
    [Header("Root")]
    public CanvasGroup rootGroup;
    public Image dimImage;

    [Header("Level Up Announcement")]
    // Both optional - a scene built before this pass added them just skips
    // the announcement entirely (see PlayLevelUpAnnouncement) rather than
    // throwing a null reference.
    public CanvasGroup levelUpTextGroup;
    public RectTransform levelUpTextRect;
    // Run Continuation/Checkpoint Ver.1 - the announcement text itself
    // ("LEVEL UP" vs "BOSS REWARD") is now set per-call (see StartSequence's
    // announcementText param) instead of a scene-build-time constant, so
    // this same sequence/Canvas can present both without duplicating any of
    // the deck/card/select machinery below.
    public Text levelUpText;

    [Header("Deck")]
    public RectTransform deckRoot;
    public Image[] deckStackImages;

    [Header("Cards")]
    public RewardCardUI[] cards = new RewardCardUI[3];
    public Vector2[] cardSlotPositions = new Vector2[3];

    // レベルアップ選択UI改修(2026-09-11) - マスター提供の参考画像どおり、
    // 「カードを引く演出」(上のCards)→「横長の詳細選択UI」(この
    // ChoicePanel)という2段階に変更。カードは3枚を短く見せる"デッキ感"の
    // 演出専用になり(タップ不可)、実際の選択はこちらのLevelUpChoiceRowUI
    // 3行で行う。既存のカード抽選/効果適用ロジック(GameManager側)・
    // RewardCardData自体は完全に不変 - 見せ方(Presentation)だけの追加。
    [Header("Choice Panel (横長3択、Vampire Survivors風)")]
    public CanvasGroup choicePanelGroup;
    public Text choiceHeaderText;
    public Text choiceSubText;
    public LevelUpChoiceRowUI[] rows = new LevelUpChoiceRowUI[3];

    [Header("Glow")]
    public Image glowImage;

    // No dedicated SE yet - PlaySfx is null-safe, so every field below can
    // stay unassigned without breaking anything.
    [Header("Audio (optional)")]
    public AudioClip levelUpSe;
    public AudioClip deckAppearSe;
    public AudioClip drawSe;
    public AudioClip flipSe;
    public AudioClip selectSe;
    public AudioClip confirmSe;

    // ===== Level Up Presentation pass - Inspector-tunable timing =====
    // "最初は気持ちいいが5回目から邪魔になる演出にはしないでください" - every
    // duration below exists so pace can be re-tuned after watching real
    // playtime footage without touching any code.

    [Header("Timing - Level Up Announcement")]
    // Brief pre-beat before the EXP bar flash/LEVEL UP text - the gameplay
    // is already fully paused by the time this runs (GameManager sets
    // Time.timeScale=0 before calling StartSequence), so this alone reads
    // as the "軽いHit Stop的な停止" the brief asks for; a separate
    // HitStop.Freeze would be redundant at timeScale already 0.
    //
    // Level Up UI微調整(2026-09-11) - 「もっさり感」の最大の原因がここ
    // だった: 旧実装はPlayLevelUpAnnouncement全体(pre-pause+pop+hold+
    // fade、合計約0.48秒)を`yield return`で待ってから初めてデッキ/カード
    // 演出を開始していた - "LEVEL UP"の文字が完全に消えるまで何も起きない
    // 「ただ見ているだけ」の時間になっていた。RunSequenceBody側を
    // `StartCoroutine`(fire-and-forget)へ変更し、デッキ出現と"LEVEL UP"
    // ポップを同時進行にした(このコルーチン自体の長さ・演出内容は不変 -
    // 「演出そのものは削除せず」の指示どおり)。
    public float preAnnouncementPause = 0.05f;
    public float levelUpPopDuration = 0.35f;

    [Header("Timing - Background Overlay")]
    [Range(0f, 1f)] public float backgroundOverlayAlpha = 0.6f;
    public float backgroundFadeDuration = 0.15f;

    // Level Up UI微調整(2026-09-11) - 「LEVEL UP発生から選択可能になる
    // まで、可能なら全体約1秒前後を目標」。旧デフォルト(約3.1秒)から、
    // 「演出そのものは削除せず、各待機時間を短縮する」方針でおよそ半分
    // 以下(約1.3秒)へ短縮。カードのドロー/フリップは「各カードの移動が
    // 終わるのを待ってから次を始める」直列処理から、「短い間隔で次々に
    // 開始し、複数枚が同時に動いている」並行処理へ変更(下のDraw Cards
    // Start/Flip Startループ参照) - "0.1〜0.15秒間隔でテンポよく出現"を
    // 実現しつつ、各カード自体の移動時間(cardDrawDuration)は見た目の
    // 滑らかさのためあえて短くしすぎていない(間隔より長くても、次のカード
    // と重なって動くだけで全体の待ち時間は増えない)。
    [Header("Timing - Wait (target ~1.0-1.5s to Reveal complete)")]
    public float rootFadeDuration = 0.08f;
    public float deckShowPause = 0.06f;
    public float cardDrawDuration = 0.18f;
    // Stagger between each card starting its own appear (fly + scale-in +
    // fade-in) - Left -> Center -> Right。マスター指示の「0.1〜0.15秒
    // 間隔」に合わせた値。
    public float cardAppearInterval = 0.12f;
    public float postDrawPause = 0.05f;
    public float cardFlipDuration = 0.18f;
    public float cardRevealInterval = 0.05f;
    public float revealFlashDuration = 0.16f;
    public float flipFinishPause = 0.05f;

    // レベルアップ選択UI改修(2026-09-11) - 旧: 表になったカード自身に
    // タップ待ちのIdle Pulseをかけていたが、カードはもう選択UIではなく
    // 「引いた3枚を短く見せるだけ」の演出になったため、この明滅は横長行
    // (LevelUpChoiceRowUI)側の縁色にのみ意味を持つ。フィールド自体は
    // カード表示中には使わなくなったが、行が「選択可能」であることを示す
    // 視覚的な手がかりは持たせず(参考画像どおり縁は常時金色で静止、タップ
    // で初めて発光する設計)、このセクションは削除。

    [Header("Timing - Card Showcase (「今回引いたのはこの3枚」を短く見せる)")]
    // マスター指示:「3枚が表になった状態は長時間表示する必要はない」
    // 「テンポを優先」「3枚が揃った後の待機は約0.3〜0.5秒程度」。
    public float cardShowcaseHold = 0.32f;

    [Header("Timing - Card -> Choice Row Transition")]
    public float cardExitDuration = 0.1f;
    public float choicePanelFadeInDuration = 0.1f;
    public float rowAppearInterval = 0.05f;
    public float rowAppearDuration = 0.12f;

    [Header("Timing - Row Selection Payoff (target ~0.4-0.6s total)")]
    // Step 6 - immediate in-place "picked" feedback(行の拡大+縁の発光)。
    public float selectedRowScale = 1.04f;
    public float selectedRowFlashDuration = 0.22f;
    // Step 7 - 他の2候補が消える。
    public float unselectedRowFadeDuration = 0.16f;
    // Step 8 - 選択したアイコンを短時間強調表示するGlow(既存PlayGlowを
    // 行の位置で再利用)からApply Upgradeまでの間。
    public float glowDuration = 0.24f;
    public float choicePanelCloseDuration = 0.16f;
    public float rootCloseDuration = 0.18f;

    [Header("Debug")]
    // Item 16 - the [LevelUpPresentation] milestone logs; separate from
    // the always-on DebugStep/LogStep diagnostic below (which already
    // existed and stays on regardless of this flag).
    public bool debugLogEnabled = true;

    bool running;
    // Bugfix 2026-09-06 - exposed read-only so GameManager's state-transition
    // logging (LogBossRewardStage) can report this sequence's own status
    // alongside Time.timeScale/IsBossPhase/levelUpPending while diagnosing
    // "Boss撃破後にゲームが停止する".
    public bool IsRunning => running;
    public bool IsWaitingForSelection => waitingForSelection;
    int selectedIndex = -1;
    bool waitingForSelection;

    // Diagnostic only: the name of whatever step RunSequence is currently
    // on, shown on-screen (see GameManager.OnGUI) and logged, so a freeze
    // can be pinned to an exact step without needing adb/logcat access.
    public static string DebugStep = "";

    static void LogStep(string step)
    {
        DebugStep = step;
        Debug.Log("RewardCardSequence: " + step);
    }

    void LogPresentation(string message)
    {
        if (debugLogEnabled) Debug.Log(message);
    }

    void PlaySfx(AudioClip clip)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx(clip);
    }

    // Bugfix 2026-09-06 - last-resort recovery for GameManager's outer
    // stuck-choice watchdog (see UpdatePendingChoiceWatchdog): if
    // RunSequence's coroutine crashed or hung partway through (before ever
    // reaching onApply), this resets every flag/visual this class owns so
    // a LATER, fresh StartSequence call can't be refused by the stale
    // `running` guard, and hides whatever was left on screen. Does NOT
    // call onApply - GameManager's watchdog already resolves the pending
    // choice itself (Checkpoint/EndBossPhase/UnlockEscape) without
    // actually applying a card, exactly like a 0-card pool already does.
    public void ForceReset()
    {
        StopAllCoroutines();
        running = false;
        waitingForSelection = false;
        selectedIndex = -1;
        gameObject.SetActive(false);
    }

    public void StartSequence(RewardCardData[] cardData, System.Action<string> onApply, string announcementText = "LEVEL UP")
    {
        if (running) return; // a level-up choice is already pending; ignore a re-trigger

        // A coroutine cannot be started on an inactive GameObject - Unity
        // just fails to schedule it (logging an error, not throwing), so
        // RunSequence would never run a single line, including the
        // gameObject.SetActive(true) that used to be its first real step.
        // This root GameObject starts inactive (see SceneBuilder), so it
        // must be activated here, before StartCoroutine, not inside the
        // coroutine itself. (Item 13's exact warning - kept unchanged from
        // before this pass.)
        gameObject.SetActive(true);
        StartCoroutine(RunSequence(cardData, onApply, announcementText));
    }

    IEnumerator RunSequence(RewardCardData[] cardData, System.Action<string> onApply, string announcementText)
    {
        LogStep("LevelUp Start");
        running = true;
        selectedIndex = -1;

        // Item 13 safety net - GameManager guarantees at least 1 entry
        // whenever StartSequence is actually called (pool.Count==0 returns
        // before ever reaching here), so this is provably unreachable in
        // practice, but the Presentation layer must never be the reason a
        // paused run gets stuck if that contract is ever violated by a
        // future change on the GameManager side.
        int cardCount = Mathf.Min(cards.Length, cardData != null ? cardData.Length : 0);
        if (cardCount <= 0)
        {
            Debug.LogWarning("RewardCardSequence: StartSequence called with no card data - resolving immediately instead of stalling.");
            running = false;
            gameObject.SetActive(false);
            onApply?.Invoke(null);
            yield break;
        }

        LogPresentation("[LevelUpPresentation] Started");

        // Bugfix 2026-09-07 (Bug #001, root cause) - everything from here to
        // the end of the method used to run with NO exception/early-exit
        // protection at all: `running` (and this GameObject staying active)
        // were only ever cleared at the very bottom, after every yield above
        // it had already completed successfully. If ANYTHING threw partway
        // through - a null card icon, a malformed RewardCardData, anything
        // inside onApply()/GameManager.ApplyUpgradeByCardId - `running`
        // would stay stuck true forever (blocking StartSequence's own
        // re-entry guard) AND, since onApply is what actually clears
        // GameManager.levelUpPending/restores Time.timeScale, THAT would
        // stay stuck too - exactly the report's "同じ意味のFlagが複数存在
        // している場合、片方だけ解除されてGameplay停止" concern, except here
        // it's worse: neither flag clears at all without reaching onApply.
        // Wrapped in try/finally so this Presentation always releases its
        // own state no matter what happens above, INCLUDING inside onApply
        // itself (GameManager.ApplyUpgradeByCardId, which now has its own
        // matching try/finally protecting levelUpPending/Time.timeScale -
        // see its own comment). GameManager's 30s pendingChoiceStuckTimer
        // watchdog remains the outermost net regardless, in case both of
        // these somehow still aren't enough.
        try
        {
            yield return RunSequenceBody(cardData, onApply, announcementText, cardCount);
        }
        finally
        {
            running = false;
            waitingForSelection = false;
            if (gameObject != null) gameObject.SetActive(false);
        }
    }

    IEnumerator RunSequenceBody(RewardCardData[] cardData, System.Action<string> onApply, string announcementText, int cardCount)
    {
        rootGroup.alpha = 0f;
        dimImage.color = new Color(dimImage.color.r, dimImage.color.g, dimImage.color.b, 0f);
        glowImage.gameObject.SetActive(false);
        for (int i = 0; i < cards.Length; i++)
        {
            cards[i].StopIdlePulse();
            cards[i].gameObject.SetActive(false);
        }
        // レベルアップ選択UI改修(2026-09-11) - 横長選択パネルも毎回同じ
        // 「非表示・不透明度0」からスタートさせる(カード側と同じ理由 -
        // 前回の実行の見た目が次回に持ち越らないように)。
        if (choicePanelGroup != null)
        {
            choicePanelGroup.alpha = 0f;
            choicePanelGroup.gameObject.SetActive(false);
        }
        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i] == null) continue;
            rows[i].SetInteractable(false);
            rows[i].gameObject.SetActive(false);
        }
        if (levelUpTextGroup != null) levelUpTextGroup.alpha = 0f;
        LogStep("Pause Complete");

        // ===== Level Up announcement - EXP bar flash + "LEVEL UP"/"BOSS
        // REWARD" pop =====
        // Level Up UI微調整(2026-09-11) - 「もっさり感」の主因だった
        // ブロッキングyieldを撤廃。ポップ演出自体(pre-pause+pop+hold+
        // fade)は完全に維持したまま、fire-and-forgetにしてデッキ/カード
        // 演出と同時進行させる - "LEVEL UP"の文字が画面中央でポップして
        // いる間に、下ではもうデッキが出てカードが飛び出し始める。
        StartCoroutine(PlayLevelUpAnnouncement(announcementText));

        // ===== Background dim + card UI fade in (run together) =====
        StartCoroutine(FadeDim(backgroundOverlayAlpha, backgroundFadeDuration));
        yield return FadeRoot(1f, rootFadeDuration);
        LogStep("Dim Fade Complete");

        PlaySfx(deckAppearSe);
        SetDeckVisible(true);
        LogStep("Deck Show Start");
        yield return new WaitForSecondsRealtime(deckShowPause);
        LogStep("Deck Show Complete");

        // ===== Card Back appear: each card fades/scales in at its own
        // slot, staggered Left -> Center -> Right (cardAppearInterval),
        // riding the same fly-from-deck motion this project already had -
        // "Card Back出現" from the brief layered onto the existing motion
        // rather than replacing it outright. =====
        //
        // Level Up UI微調整(2026-09-11) - 「3枚のカードは約0.1〜0.15秒間隔
        // でテンポよく出現」。以前はcard.MoveToを`yield return`していた
        // ため、1枚の移動(cardDrawDuration)が完全に終わってから次の1枚が
        // 動き出す直列処理になっており、3枚で"間隔+移動時間"を3回分足した
        // 長さがそのままかかっていた。MoveToもFadeTo/ScaleToと同じ
        // fire-and-forgetにし、ループ自体はcardAppearIntervalぶんだけ待って
        // 次のカードを動かし始める並行処理へ変更 - 複数枚が同時に空中を
        // 飛んでいる状態になる(見た目はテンポアップするだけで、各カード
        // 自体の飛び方(移動時間・イージング)は一切変えていない)。
        LogStep("Draw Cards Start");
        for (int i = 0; i < cardCount; i++)
        {
            var card = cards[i];
            card.gameObject.SetActive(true);
            card.ShowBack();
            card.rect.anchoredPosition = deckRoot.anchoredPosition;
            card.rect.localScale = Vector3.one * 0.8f;
            card.canvasGroup.alpha = 0f;
            PlaySfx(drawSe);
            LogStep("Draw Card " + i);
            StartCoroutine(card.FadeTo(1f, cardDrawDuration));
            StartCoroutine(card.ScaleTo(1f, cardDrawDuration));
            StartCoroutine(card.MoveTo(cardSlotPositions[i], cardDrawDuration));
            if (i < cardCount - 1) yield return new WaitForSecondsRealtime(cardAppearInterval);
        }
        // 最後のカードがまだ飛んでいる途中でも次の演出(Flip)へ進んでしまわ
        // ないよう、ループの合計待機時間(cardAppearInterval×(N-1))を差し
        // 引いた残り分だけ追加で待つ - cardDrawDurationがcardAppearInterval
        // より短い/同程度なら実質待たない(既にループの間隔待ちだけで十分)。
        float remainingDrawTime = cardDrawDuration - cardAppearInterval * (cardCount - 1);
        if (remainingDrawTime > 0f) yield return new WaitForSecondsRealtime(remainingDrawTime);
        LogStep("Draw Cards Complete");
        LogPresentation("[LevelUpPresentation] Cards spawned");

        yield return new WaitForSecondsRealtime(postDrawPause);
        SetDeckVisible(false);

        // ===== Reveal: flip each face-up, staggered, each with a brief
        // Gold Edge Glow flash right as its content appears - "何が出た？
        // という小さな期待感". =====
        LogStep("Flip Start");
        for (int i = 0; i < cardCount; i++)
        {
            // Card UI改修(2026-09-08) - showDetails:true。Level Up/Boss
            // Reward選択はこの3枚を見て即決めるため、効果文/★を隠す新デ
            // フォルトのシンプル表示ではなく、従来どおり常時表示のまま。
            // DeckEditUIと違いこの画面には別途詳細パネルがないため、情報
            // を失わせないための意図的な措置(RewardCardUI.showDetailsの
            // コメント参照)。
            cards[i].SetContent(cardData[i], showDetails: true);
            PlaySfx(flipSe);
            LogStep("Flip Card " + i);
            StartCoroutine(cards[i].FlipToFront(cardFlipDuration));
            StartCoroutine(cards[i].FlashFrame(revealFlashDuration));
            if (i < cardCount - 1) yield return new WaitForSecondsRealtime(cardRevealInterval);
        }
        yield return new WaitForSecondsRealtime(flipFinishPause);
        LogStep("Flip Complete");
        LogPresentation("[LevelUpPresentation] Reveal complete");

        // ===== カードの見せ場はここまで - 「3枚が表になった状態は長時間
        // 表示する必要はありません」「今回引いたのはこの3枚、と認識できる
        // 程度の短い演出にして、テンポを優先してください」。カードはまだ
        // タップ不可のまま(SetInteractableを一度も呼んでいない = 常に
        // 非インタラクティブ)、ただ短く見せるだけ。 =====
        yield return new WaitForSecondsRealtime(cardShowcaseHold);
        LogStep("Card Showcase Complete");
        LogPresentation("[LevelUpPresentation] Card showcase complete");

        // ===== 「カードから横長選択UIへ切り替え」 - カードを縮めて消し、
        // 入れ替わりに横長3択パネル(LevelUpChoiceRowUI×3)をフェード/
        // スケールインさせる。RewardCardData自体(cardData)はカード表示の
        // ときと完全に同じものをそのまま行UIへ渡すだけ - 抽選/効果適用
        // ロジックには一切触れていない。 =====
        LogStep("Card->Row Transition Start");
        for (int i = 0; i < cardCount; i++)
        {
            StartCoroutine(cards[i].FadeTo(0f, cardExitDuration));
            StartCoroutine(cards[i].ScaleTo(0.85f, cardExitDuration));
        }
        yield return new WaitForSecondsRealtime(cardExitDuration);
        for (int i = 0; i < cards.Length; i++) cards[i].gameObject.SetActive(false);

        if (choiceHeaderText != null) choiceHeaderText.text = announcementText;
        for (int i = 0; i < cardCount; i++)
        {
            rows[i].gameObject.SetActive(true);
            rows[i].SetContent(cardData[i]);
            rows[i].rect.localScale = Vector3.one * 0.92f;
            rows[i].canvasGroup.alpha = 0f;
        }
        if (choicePanelGroup != null)
        {
            choicePanelGroup.gameObject.SetActive(true);
            yield return FadeCanvasGroup(choicePanelGroup, 1f, choicePanelFadeInDuration);
        }
        for (int i = 0; i < cardCount; i++)
        {
            StartCoroutine(rows[i].FadeTo(1f, rowAppearDuration));
            StartCoroutine(rows[i].ScaleTo(1f, rowAppearDuration));
            if (i < cardCount - 1) yield return new WaitForSecondsRealtime(rowAppearInterval);
        }
        yield return new WaitForSecondsRealtime(rowAppearDuration);
        LogStep("Card->Row Transition Complete");
        LogPresentation("[LevelUpPresentation] Choice rows shown");

        // Input Lock (item 14) - Rows shown -> Input Enable, exactly at
        // this line; nothing before this point can register a tap
        // (SetInteractable(true) is the only thing that flips
        // button.interactable/canvasGroup.blocksRaycasts on).
        for (int i = 0; i < cardCount; i++) rows[i].SetInteractable(true);

        // Belt-and-suspenders: if a tap somehow never gets recognized by
        // either input path above, this guarantees the run can never be
        // stuck paused forever - it just auto-picks the first row after a
        // long wait instead.
        LogStep("Waiting For Selection");
        waitingForSelection = true;
        float waitStart = Time.unscaledTime;
        const float selectionTimeoutSeconds = 20f;
        while (selectedIndex < 0)
        {
            if (Time.unscaledTime - waitStart > selectionTimeoutSeconds)
            {
                selectedIndex = 0;
                break;
            }
            yield return null;
        }
        waitingForSelection = false;
        LogStep("Selected " + selectedIndex);

        // Input Lock (item 14) - selection resolved -> Input Disable
        // immediately, before any animation, so a double-tap can't select a
        // second row or re-enter this block (OnRowClicked's own
        // selectedIndex>=0 guard already blocks it too - belt-and-suspenders
        // against Card Effect double-apply / coroutine double-run).
        PlaySfx(selectSe);
        for (int i = 0; i < cardCount; i++) rows[i].SetInteractable(false);

        var winner = rows[selectedIndex];
        LogPresentation($"[LevelUpPresentation] Card selected: {winner.Data.CardId}");

        // ===== 選択後の演出 - マスター指示どおり短く、素早くゲーム再開へ
        // 戻れるように。「選択した横長UIが発光→他の2候補が消える→選択した
        // 能力アイコンを短時間強調表示→能力取得処理→LEVEL UP画面を閉じる
        // →ゲーム再開」の順。 =====

        // Step 6 - 選択した行がその場で発光+軽く拡大 - "これを選んだ、
        // というFeedbackを明確に".
        LogStep("Select Feedback Start");
        StartCoroutine(winner.FlashEdge(selectedRowFlashDuration + 0.1f));
        yield return winner.ScaleTo(selectedRowScale, selectedRowFlashDuration);
        winner.SetSelectedVisual(true);
        LogStep("Select Feedback Complete");

        // Step 7 - 他の2候補が消える。
        for (int i = 0; i < cardCount; i++)
        {
            if (i == selectedIndex) continue;
            StartCoroutine(rows[i].FadeTo(0f, unselectedRowFadeDuration));
        }
        yield return new WaitForSecondsRealtime(unselectedRowFadeDuration);

        // Step 8 - 選択した能力アイコンを短時間強調表示(既存のGold Glow
        // バーストを、カードの中心ではなく選択された行の位置で再利用)。
        LogStep("Confirm Glow Start");
        PlaySfx(confirmSe);
        StartCoroutine(PlayGlow(winner.rect.anchoredPosition));
        yield return new WaitForSecondsRealtime(glowDuration * 0.6f);
        LogStep("Confirm Glow Complete");

        LogStep("Apply Upgrade");
        onApply(winner.Data.CardId);
        LogPresentation("[LevelUpPresentation] Gameplay resumed");
        LogStep("Apply Upgrade Complete");

        if (choicePanelGroup != null) yield return FadeCanvasGroup(choicePanelGroup, 0f, choicePanelCloseDuration);
        yield return FadeRoot(0f, rootCloseDuration);
        // gameObject.SetActive(false)/running=false are now handled by the
        // outer RunSequence's finally block (see its own comment) so they
        // still happen even if something above this point threw.
        LogStep("Sequence Complete");
        LogPresentation("[LevelUpPresentation] Presentation finished");
    }

    // "EXP Barが一瞬発光 -> LEVEL UP表示" - GameManager owns the actual EXP
    // bar (its own IMGUI HUD), so this only ever calls the one public
    // trigger method it exposes (FlashExpBar) rather than reaching into its
    // fields; the LEVEL UP text itself lives in this Canvas so its
    // pop/fade can be driven directly. Gracefully skipped (not an error) if
    // levelUpTextGroup/Rect weren't wired - a scene built before this pass.
    IEnumerator PlayLevelUpAnnouncement(string announcementText)
    {
        yield return new WaitForSecondsRealtime(preAnnouncementPause);

        // Run Continuation/Checkpoint Ver.1 - only a normal Level Up flashes
        // the EXP bar/plays the Level Up SE (Boss Reward doesn't touch EXP
        // at all, so flashing that bar for it would be misleading).
        bool isLevelUp = announcementText == "LEVEL UP";
        if (isLevelUp)
        {
            if (GameManager.Instance != null) GameManager.Instance.FlashExpBar();
            PlaySfx(levelUpSe);
        }
        if (levelUpText != null) levelUpText.text = announcementText;

        if (levelUpTextGroup == null || levelUpTextRect == null) yield break;

        levelUpTextGroup.gameObject.SetActive(true);
        levelUpTextGroup.alpha = 0f;
        levelUpTextRect.localScale = Vector3.one * 0.8f;

        // Pop: Scale 0.8 -> 1.05 -> 1.0, Alpha 0 -> 1, over the first ~55%
        // of levelUpPopDuration; hold, then fade over the remainder.
        float popDuration = levelUpPopDuration * 0.55f;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, popDuration);
            float f = Mathf.Clamp01(t);
            float scale = f < 0.7f ? Mathf.Lerp(0.8f, 1.05f, f / 0.7f) : Mathf.Lerp(1.05f, 1f, (f - 0.7f) / 0.3f);
            levelUpTextRect.localScale = Vector3.one * scale;
            levelUpTextGroup.alpha = Mathf.Min(1f, f / 0.6f);
            yield return null;
        }

        float remaining = Mathf.Max(0f, levelUpPopDuration - popDuration);
        float hold = remaining * 0.35f;
        float fade = remaining - hold;
        if (hold > 0f) yield return new WaitForSecondsRealtime(hold);

        t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, fade);
            levelUpTextGroup.alpha = Mathf.Lerp(1f, 0f, Mathf.Clamp01(t));
            yield return null;
        }
        levelUpTextGroup.gameObject.SetActive(false);
    }

    void SetDeckVisible(bool visible)
    {
        foreach (Image img in deckStackImages) img.enabled = visible;
    }

    IEnumerator FadeRoot(float target, float duration)
    {
        float start = rootGroup.alpha;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, duration);
            rootGroup.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(t));
            yield return null;
        }
    }

    // レベルアップ選択UI改修(2026-09-11) - FadeRoot/FadeDimと同じ形の汎用
    // 版。choicePanelGroup(横長3択パネル全体)のフェードイン/アウトに使う -
    // 専用のFadeChoicePanelを別途書く代わりに、任意のCanvasGroupを受け取
    // れるようにして重複を避けた。
    IEnumerator FadeCanvasGroup(CanvasGroup group, float target, float duration)
    {
        float start = group.alpha;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, duration);
            group.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(t));
            yield return null;
        }
    }

    // Background Dark Overlay (item 2) - separate from FadeRoot above:
    // rootGroup.alpha covers the whole card-UI subtree (deck/cards/glow)
    // fading together, while dimImage's OWN alpha is animated here
    // independently so BackgroundOverlayAlpha/BackgroundFadeDuration can be
    // tuned without affecting how fast the cards themselves fade in.
    IEnumerator FadeDim(float target, float duration)
    {
        Color c = dimImage.color;
        float start = c.a;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, duration);
            c.a = Mathf.Lerp(start, target, Mathf.Clamp01(t));
            dimImage.color = c;
            yield return null;
        }
    }

    IEnumerator PlayGlow(Vector2 atPosition)
    {
        glowImage.rectTransform.anchoredPosition = atPosition;
        glowImage.gameObject.SetActive(true);
        glowImage.color = new Color(1f, 0.82f, 0.35f, 0f);
        glowImage.rectTransform.localScale = Vector3.one * 0.6f;

        float t = 0f;
        float growDuration = glowDuration * 0.4f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, growDuration);
            float f = Mathf.Clamp01(t);
            glowImage.color = new Color(1f, 0.82f, 0.35f, Mathf.Lerp(0f, 0.9f, f));
            glowImage.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.3f, f);
            yield return null;
        }

        t = 0f;
        float fadeDuration = glowDuration * 0.6f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, fadeDuration);
            float f = Mathf.Clamp01(t);
            glowImage.color = new Color(1f, 0.82f, 0.35f, Mathf.Lerp(0.9f, 0f, f));
            glowImage.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.3f, 1.6f, f);
            yield return null;
        }

        glowImage.gameObject.SetActive(false);
    }

    // レベルアップ選択UI改修(2026-09-11) - 選択はもうカードではなく横長行
    // (LevelUpChoiceRowUI)に対して行う。SceneBuilderが各行のButton.
    // onClickへこれを配線する - "normal" path via UGUI's EventSystem/
    // GraphicRaycaster.
    public void OnRowClicked(int index)
    {
        if (selectedIndex >= 0) return; // already picked; ignore further taps
        selectedIndex = index;
    }

    // Backup path that doesn't depend on the EventSystem/Button pipeline at
    // all - hit-tests raw touch/mouse-down position directly against each
    // row's RectTransform ("行全体をタップ可能に" - the row's own rect IS
    // the full tap area, no separate hit-box needed), the same way the rest
    // of this project (which has no other uGUI anywhere) already handles
    // taps. Whichever path notices the tap first wins; this exists purely
    // so a level-up can never get stuck waiting on a tap that the
    // EventSystem, for whatever reason, didn't deliver.
    void Update()
    {
        if (!waitingForSelection || selectedIndex >= 0) return;

        if (!TouchInputUtil.TryGetTapPosition(out Vector2 screenPos)) return;

        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i] == null || !rows[i].gameObject.activeInHierarchy) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(rows[i].rect, screenPos, null))
            {
                OnRowClicked(i);
                return;
            }
        }
    }

}
