#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// 出発前に選んだデッキがランに使われるか(2026-11-04、実機の不具合): -qaDeckDepart <dir>(テスト用データ SaveProfile の中だけ)
//  デッキ1 = 目印 A だけ、デッキ2 = 目印 B だけ。前回このマップをデッキ1で走った記録を作ってから、デッキ2を選んで出発する。
//  「出発の直前に選んだ番号と中身」「ランが使うカード(deckCards と実際の3択)」「RUN BUILD HUD の取得カード」を照合する。
//  通常出発 → 疾走出発 → デッキ1へ戻して通常出発 の3回。
public partial class QaSweep
{
    const string DdpA = "attack_up", DdpB = "speed_up";

    IEnumerator DeckDepartMode()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        gm = GameManager.Instance;
        UnlockRules.QaOverride = true;
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        gm = GameManager.Instance;
        TutorialProgress.MarkOffered();
        SaveStore.SetInt(DeckCapacityNotice.Key, 1);
        SaveStore.Save();
        foreach (var id in new[] { DdpA, DdpB }) if (CardInventory.GetTotalCount(id) < 2) CardInventory.AddCard(id, 1, 2);
        string owned0 = DdOwned();

        // デッキ1 = A、デッキ2 = B(目印。ほかのカードは入れない)
        if (DeckPresets.Active != 0) gm.ActivatePreset(0);
        gm.SetDeck(new[] { DdpA });
        gm.ActivatePreset(1);
        gm.SetDeck(new[] { DdpB });
        gm.ActivatePreset(0);
        // 前回このマップをデッキ1で走った(ランの開始で書かれる記録と同じ)
        DeckPresets.Remember("solo", "wasteland_road");
        L($"[setup] p1 {string.Join(",", DeckPresets.Get(0).cards)} p2 {string.Join(",", DeckPresets.Get(1).cards)} remembered(wasteland) {DeckPresets.Remembered("solo", "wasteland_road") + 1}");

        yield return DdpRun("normal", 1, DdpB, false);
        yield return DdpRun("sprint", 1, DdpB, true);
        yield return DdpRun("back-to-1", 0, DdpA, false);
        yield return DdpRun("again-2", 1, DdpB, false);

        Check(DdOwned() == owned0, "owned cards are unchanged by switching and departing");
        Check(DeckPresets.Get(0).cards.SequenceEqual(new[] { DdpA }) && DeckPresets.Get(1).cards.SequenceEqual(new[] { DdpB }), "the contents of decks 1 and 2 are unchanged");
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }

    // choose = 出発の直前にホームで選ぶプリセット(デッキ編集の◀▶と同じ ActivatePreset)
    IEnumerator DdpRun(string tag, int choose, string marker, bool sprint)
    {
        gm = GameManager.Instance;
        if (DeckPresets.Active != choose) gm.ActivatePreset(choose);
        int chosen = DeckPresets.Active;
        string chosenCards = string.Join(",", gm.DeckCards);
        L($"[{tag}] chosen before departure: deck {chosen + 1} = {chosenCards}");
        gm.SetSelectedCharacter("swordsman");
        bool ok;
        if (sprint)
        {
            SprintRecords.DevUnlockAll = true;
            SprintRunner.QaRingPolicy = 1; SprintRunner.QaTimeScale = 6f;
            ok = gm.DepartSprint("wasteland_road", 10000);
        }
        else
        {
            gm.DepartFromStageSelect("wasteland_road");
            ok = true;
        }
        float t = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive && !gm.SprintActive) && t < 40f) { yield return null; t += Time.unscaledDeltaTime; }
        if (sprint) { SprintRunner.QaTimeScale = 1f; SprintRecords.DevUnlockAll = false; }
        Check(ok && gm.HasStarted, $"{tag}: the run started");
        pc = PlayerController.Instance;
        stopKeepAlive = false; StartCoroutine(KeepAlive());
        SetGod(true);
        string runCards = string.Join(",", gm.DeckCards);
        L($"[{tag}] in the run: deck {DeckPresets.Active + 1} = {runCards} (distance {gm.MaxDistance:F0}m)");
        Check(DeckPresets.Active == chosen, $"{tag}: the run uses the deck chosen before departure (chosen {chosen + 1}, used {DeckPresets.Active + 1})");
        Check(runCards == chosenCards, $"{tag}: the run's cards are the chosen deck's cards ({chosenCards} -> {runCards})");

        // 実際の3択: 目印の1枚だけ(デッキが1枚なので、3択の中身はその1枚)
        yield return DdpLevelUp();
        var offered = DdpOffered();
        L($"[{tag}] level-up offered: {string.Join(",", offered)}");
        // 疾走出発は道中の自動の付与で目印が Lv9 に届いていることがある(その時は3択が出ない = 正しい)
        bool maxed = gm.GetRunCardLevel(marker) >= 9;
        Check((offered.Count > 0 || (sprint && maxed)) && offered.All(id => GameManager.DeckKindOf(id) == marker), $"{tag}: the card choice offers only the chosen deck's marker {marker} ({string.Join(",", offered)}{(maxed ? ", marker already Lv9" : "")})");
        // 選んで HUD(RUN BUILD)に出るカード
        DdpPickFirst();
        yield return new WaitForSecondsRealtime(1.2f);
        var acquired = new List<string>(); gm.CollectRunCardIds(acquired);
        L($"[{tag}] RUN BUILD acquired: {string.Join(",", acquired)}");
        Check(acquired.Any(id => GameManager.DeckKindOf(id) == marker) && !acquired.Any(id => GameManager.DeckKindOf(id) == (marker == DdpA ? DdpB : DdpA)), $"{tag}: the HUD shows the chosen deck's card");
        Shot($"ddp_{tag}_hud");
        SetGod(false);
        yield return EndRun();
        yield return ReloadHome();
        gm = GameManager.Instance;
        Check(DeckPresets.Active == chosen && DeckPresets.Remembered("solo", "wasteland_road") == chosen, $"{tag}: back home the chosen deck is still in use and remembered ({DeckPresets.Active + 1})");
    }

    IEnumerator DdpLevelUp()
    {
        var gain = typeof(GameManager).GetMethod("GainExp", BindingFlags.NonPublic | BindingFlags.Instance);
        gain.Invoke(gm, new object[] { gm.ExpToNext + 1f });
        float w = 0f;
        while (!gm.IsLocalChoiceOpen && w < 8f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.6f);
    }

    List<string> DdpOffered()
    {
        var f = typeof(GameManager).GetField("pendingChoices", BindingFlags.NonPublic | BindingFlags.Instance);
        var arr = f != null ? f.GetValue(gm) as CardDefinition[] : null;
        return arr == null ? new List<string>() : arr.Where(c => c != null).Select(c => c.cardId).ToList();
    }

    void DdpPickFirst()
    {
        var offered = DdpOffered();
        if (offered.Count == 0) return;
        typeof(GameManager).GetMethod("ApplyUpgradeByCardId", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, new object[] { offered[0] });
    }
}
#endif
