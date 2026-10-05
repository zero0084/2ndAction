using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 疾走出発(2026-10-05 試作): 攻略済みの区間を操作不要の演出で駆け抜け、途中のボス報酬ぶんのカードを自動で取り、
// 目的地の関門の少し手前から通常のランへ戻る。ソロのみ(マルチのランでは使えない)。
//
//  ・出発: ホームのステージ選択 →「疾走出発…」→ 行き先(SprintRecords の解放条件)→ 通常の出発と同じ遷移
//          → ApplyGameStart がカウントダウンの代わりに SprintRoutine を始める(CountdownActive のまま = 自分のキャラ/敵/距離は止まっている)
//  ・疾走中: SprintRunner(画面の演出)が距離を進め、関門を通過するたびに SprintGrantRandomCard(ボス報酬の自動取得)、
//            5000m ごとのリングをくぐれたら StartSprintRingChoice(追加の3択)。実際の移動/ボス/敵/障害物/被弾/落下は起きない。
//  ・到着: SprintArrive = 中断再開(CONTINUE)と同じ置き方(距離/次の関門/安全な足場/準備画面→3-2-1→GO)。速さは到着の距離の
//          自然な速さ(+カード)で、疾走の演出の速さは能力に残らない。到着の時点で中断データ(CONTINUE)を1回だけ保存する。
//  ・記録: 飛ばした距離は持ち帰りの MILE(距離ぶん)から除く(SprintSkippedMeters、CONTINUE にも保存)。累計走行距離/距離の EXP は
//          元々「実際に進んだ差分」しか数えないので加算されない。ボスの撃破記録/MILE/EXP/会ったボスの記録も付けない。
public partial class GameManager
{
    public class SprintRequest { public string stageId; public int destination; }
    SprintRequest sprintRequest;
    public bool SprintActive { get; private set; }
    public int SprintDestination { get; private set; }
    public float SprintSkippedMeters { get; private set; }  // 疾走で飛ばした距離(MILE の距離ぶんから除く)
    public int SprintAutoGrants { get; private set; }
    public int SprintAutoSkippedNoCandidate { get; private set; }
    public int SprintRingPicks { get; private set; }
    public bool StageSelectIsOpen => stageSelectOpen;
    public bool PauseMenuOpen => showPauseMenu;

    // ホームのステージ選択から。解放されていなければ false
    public bool DepartSprint(string stageId, int destination)
    {
        if (startTransitioning || HasStarted) return false;
        if (NetSession.IsActive || NetRunLauncher.IsMultiplayerRun) { Debug.Log("[Sprint] multiplayer session - sprint departure is solo only"); return false; }
        if (!SprintRecords.IsUnlocked(stageId, destination, out string why)) { Debug.Log($"[Sprint] {stageId} {destination}m locked ({why})"); return false; }
        sprintRequest = new SprintRequest { stageId = stageId, destination = destination };
        Debug.Log($"[Sprint] depart request {stageId} -> {destination}m");
        DepartFromStageSelect(stageId);
        if (!startTransitioning && !HasStarted) { sprintRequest = null; return false; } // 出発できなかった(遷移中など)
        return true;
    }

    // ApplyGameStart から: 疾走の要求があればカウントダウンの代わりに疾走を始める
    bool TryStartSprintInsteadOfCountdown()
    {
        if (sprintRequest == null) return false;
        var r = sprintRequest;
        sprintRequest = null;
        if (NetRunLauncher.IsMultiplayerRun || r.stageId != activeRunStageId) return false;
        StartCoroutine(SprintRoutine(r));
        return true;
    }

    IEnumerator SprintRoutine(SprintRequest r)
    {
        CountdownActive = true; // 自分のキャラ/敵/障害物/距離を止めたまま(疾走は画面の演出だけ)
        CountdownLabel = "";
        SprintActive = true;
        SprintDestination = r.destination;
        SprintAutoGrants = 0; SprintAutoSkippedNoCandidate = 0; SprintRingPicks = 0;
        while (ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning) yield return null;
        var runner = SprintRunner.Begin(this, r.stageId, r.destination);
        while (runner != null && !runner.Done) yield return null;
        float arrival = runner != null ? runner.ArrivalMeters : Mathf.Max(0f, r.destination - SprintTuning.I.arriveBeforeMeters);
        SprintArrive(arrival, r.destination);
        SprintActive = false;
        if (runner != null) Destroy(runner.gameObject);
    }

    // ---- ボス報酬の自動取得(1回 = 通常のボス報酬の3択の1枚ぶん。選択画面は出さない) ----
    // 候補は通常のボス報酬と同じ: デッキのカードのうち、まだ取れるもの(CanStillPick = Lv9上限/封印/ULTIMATEの条件)。
    // その中から1枚をランダム(通常は3枚提示から1枚を選ぶ → 自動ではランダムに1枚)。候補が尽きていれば何も取らない(通常の
    // ボス報酬で選択画面が出ないのと同じ)。FINAL EVOLUTION はボス報酬に出ないので対象外。
    List<CardDefinition> SprintCandidates()
    {
        var pool = new List<CardDefinition>();
        int maxed = 0;
        foreach (string id in deckCards)
        {
            CardDefinition card = CardDatabase.FindById(id);
            if (card == null) continue;
            if (CanStillPick(card)) pool.Add(card); else maxed++;
        }
        if (pool.Count == 0 && deckCards.Count > 0 && maxed == 0) foreach (var u in CardDatabase.UnlockedCards) if (CanStillPick(u)) pool.Add(u);
        return pool;
    }

    public CardDefinition SprintGrantRandomCard()
    {
        var pool = SprintCandidates();
        if (pool.Count == 0) { SprintAutoSkippedNoCandidate++; Debug.Log("[Sprint] auto grant: no candidate (every deck card is maxed) - nothing granted"); return null; }
        CardDefinition c = pool[Random.Range(0, pool.Count)];
        ApplyRunCardCapped(c, 1, "Sprint auto");
        upgradeHistory.Add(c);
        SprintAutoGrants++;
        Debug.Log($"[Sprint] auto grant #{SprintAutoGrants}: {c.cardName} -> Lv{GetCurrentRunStack(c.cardId)}");
        return c;
    }

    // ---- リング成功の追加の3択(ボス報酬とは別のボーナス)。回復/FINAL EVOLUTION は付けない ----
    public bool SprintChoiceOpen => levelUpPending;
    public bool StartSprintRingChoice()
    {
        if (levelUpPending || rewardCardSequence == null) return false;
        var pool = SprintCandidates();
        if (pool.Count == 0) { Debug.Log("[Sprint] ring bonus: no candidate - nothing to choose"); return false; }
        for (int i = pool.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
        int n = Mathf.Min(3, pool.Count);
        pendingChoices = new CardDefinition[n];
        for (int i = 0; i < n; i++) pendingChoices[i] = pool[i];
        pendingChoiceKind = PendingChoiceKind.LevelUp; // 解決の処理は LEVEL UP と同じ(取得の履歴に足すだけ。チェックポイントは動かさない)
        levelUpPending = true;
        TimeControl.Pause(pendingChoiceTimeOwner);
        var cards = new RewardCardData[n];
        for (int i = 0; i < n; i++) cards[i] = MakeChoiceCardData(pendingChoices[i]);
        SprintRingPicks++;
        StartChoiceSequence(cards, "RING BONUS");
        Debug.Log($"[Sprint] ring bonus choice #{SprintRingPicks} ({n} cards)");
        return true;
    }

    // ---- 到着: 中断再開(CONTINUE)と同じ置き方 ----
    void SprintArrive(float arrival, int destination)
    {
        SprintSkippedMeters = arrival;
        MaxDistance = arrival;
        MaxDistanceExact = arrival;
        HighestReachedDistance = Mathf.Max(HighestReachedDistance, arrival);
        var bm = BossManager.Instance;
        if (bm != null)
        {
            bm.RestoreNextBossDistance(arrival);         // 次の関門 = 目的地の関門(到着地点の先)
            bm.SprintMarkSkippedGates(Mathf.FloorToInt(arrival / 1000f)); // このランの再戦プール(永続の記録ではない)
        }
        safeZoneEndDistance = arrival + safeZoneLength;
        if (PlayerController.Instance != null)
        {
            Vector3 p = PlayerController.Instance.transform.position;
            p.x = arrival - (float)FloatingOrigin.Offset;
            FreezeDiagnostics.NoteIntendedMove("SPRINT arrival");
            PlayerController.Instance.transform.position = p;
        }
        CountdownActive = false;
        SetupResumeFooting(arrival);
        SaveCheckpoint(); // 到着の時点(取得したカード込み)を1回だけ。以後の CONTINUE はここから(自動取得/リングを重ねない)
        BeginResumeGate();
        Debug.Log($"[Sprint] arrived at {arrival:F0}m (destination {destination}m): auto grants {SprintAutoGrants} (no candidate {SprintAutoSkippedNoCandidate}), ring picks {SprintRingPicks}, Lv{Level}, speed {(PlayerController.Instance != null ? PlayerController.Instance.CurrentAutoRunSpeed * KmhPerMps : 0f):F0}km/h, next gate {(bm != null ? bm.NextBossDistance : 0f):F0}m");
    }

    // 持ち帰りの MILE(距離ぶん)の距離: 疾走で飛ばした距離を除く
    float MileDistanceForRun => Mathf.Max(0f, Mathf.Max(MaxDistance, HighestReachedDistance) - SprintSkippedMeters);
}
