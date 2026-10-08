#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ラン中のカード一覧(キャラ固有/デッキの分け方)とHP表示(固定幅・10以上は×N)の撮影と確認(2026-10-01)。 -qaHudShots <dir>
// テスト機の保存データは最初に控えて、最後に元へ戻す。
public partial class QaSweep
{
    IEnumerator HudShotsMode()
    {
        Application.targetFrameRate = 60;
        var snap = SaveSystem.Capture();
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        yield return new WaitForSecondsRealtime(2f);
        // キャラ固有カードを2枚付けて始める(キャラごとの枠なので、走るキャラの枠に付ける。2026-10-08)
        gm.SetSelectedCharacter("swordsman"); gm.SetCharacterCardOwner("swordsman");
        var unlocked = CardDatabase.UnlockedCards.Where(c => c != null && c.icon != null && !CardVariant.IsVariantKey(c.cardId)).ToList();
        for (int s = 0; s < 2 && s < unlocked.Count; s++)
        {
            CardInventory.AddCard(unlocked[s].cardId, 1, 1);
            bool eq = gm.EquipCharacterCard(s, unlocked[s].cardId, 1);
            L($"[hud] equip character card {s}: {unlocked[s].cardId} {eq}");
        }
        yield return BeginRun("swordsman", "wasteland_road");
        stopKeepAlive = true; // HPを99にしない(HP表示を撮るため)
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        autoPickHold = true;
        PlayerController.DebugSpeedScale = 0.3f;
        var hud = FindFirstObjectByType<RunBuildHud>();

        var have = new List<string>(); gm.CollectRunCardIds(have);
        L($"[hud] start: run cards {have.Count} (character {have.Count(gm.IsRunCharacterCard)})");
        Shot("hud_0_start");
        yield return new WaitForSecondsRealtime(0.3f);

        // デッキから何枚か取る(うち1枚はキャラ固有カードと同じ → ★の札に+1)
        var apply = typeof(GameManager).GetMethod("ApplyUpgradeByCardId", NP);
        var pool = CardDatabase.AllCards.Where(c => c != null && c.icon != null && !CardVariant.IsVariantKey(c.cardId)).Select(c => c.cardId).Where(id => !have.Contains(id)).ToList();
        foreach (string id in pool.Take(6)) { apply.Invoke(gm, new object[] { id }); yield return null; }
        string charId = gm.CharacterCardIds.FirstOrDefault(x => !string.IsNullOrEmpty(x));
        if (charId != null) { apply.Invoke(gm, new object[] { charId }); L($"[hud] picked the character card {charId} from the deck too -> picks {gm.GetRunPickCount(charId)}"); }
        yield return new WaitForSecondsRealtime(0.8f);
        var rects = new List<Rect>();
        hud.ComputeCurrentLayout(false, rects, out Rect grid, out float size);
        int nChar = hud.Slots.Count(x => x.character);
        L($"[hud] slots={hud.Slots.Count} character={nChar} grid={grid} size={size:F0} groupGap={hud.LastGroupGap:F0}");
        Check(nChar > 0 && hud.Slots.Take(nChar).All(x => x.character), "character cards come first");
        Check(nChar == hud.Slots.Count || rects.Take(nChar).Max(r => r.yMax) < rects.Skip(nChar).Min(r => r.yMin), "character cards sit on their own row above the deck cards");
        Shot("hud_1_char_and_deck");
        yield return new WaitForSecondsRealtime(0.3f);

        // 選択中(小さく表示)
        gm.GrantBonusCardChoice();
        w = 0f; while (!gm.IsRewardSequenceWaitingForSelection && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.3f);
        Shot("hud_2_during_choice");
        yield return new WaitForSecondsRealtime(0.3f);
        var seq = FindFirstObjectByType<RewardCardSequence>();
        seq.OnCardClicked(0); yield return new WaitForSecondsRealtime(0.4f); seq.OnCardClicked(0);
        w = 0f; while ((gm.LevelUpPending || seq.IsRunning) && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.5f);

        // ---- HP表示: 枠の幅は固定、10以上は ×N
        var livesSetter = typeof(GameManager).GetProperty("Lives").GetSetMethod(true);
        var maxField = typeof(GameManager).GetField("maxLives");
        Rect heartsRef = R("GetHeartsPanelRect");
        int[][] cases = { new[] { 30, 30 }, new[] { 50, 20 }, new[] { 50, 45 }, new[] { 90, 90 }, new[] { 100, 70 }, new[] { 100, 100 }, new[] { 150, 150 }, new[] { 150, 95 }, new[] { 990, 990 } };
        foreach (var c in cases)
        {
            maxField.SetValue(gm, c[0]);
            livesSetter.Invoke(gm, new object[] { c[1] });
            yield return new WaitForSecondsRealtime(0.25f);
            Rect hr = R("GetHeartsPanelRect");
            L($"[hearts] max={c[0]} hp={c[1]} panel={hr}");
            Check(hr == heartsRef, $"HP panel keeps the same size (max {c[0]}, hp {c[1]})");
            Shot($"hearts_max{c[0]}_hp{c[1]}");
            yield return new WaitForSecondsRealtime(0.25f);
        }

        autoPickHold = false;
        PlayerController.DebugSpeedScale = 1f;
        yield return EndRun();
        SaveSystem.Restore(snap);
        L("[hud] test machine save data restored");
    }
}
#endif
