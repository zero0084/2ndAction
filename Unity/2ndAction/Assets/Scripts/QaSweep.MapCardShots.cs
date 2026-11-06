#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// マップ専用カード 20 枚の見え方の撮影(2026-11-06、マスターの依頼 29「エステルと実画面確認」): -qaMapCardShots <dir>(-memSave で)
//  ガチャの結果(洞窟★3/天空★4/ラスダン★4/★5)・カード編集(ラスダンの絞り込み・天空・洞窟)・長押しの詳細・ラン中の3択(新カードだけのデッキ)
public partial class QaSweep
{
    static readonly string[] MapCardIds = { "last_bastion", "overflow_ward", "revenge_armor", "break_feast", "iron_heart", "air_combo", "sky_breaker", "dive_boost", "launch_master", "sonic_edge",
        "second_rise", "shield_burst", "blood_armor", "elemental_chaos", "elemental_focus", "break_dominion", "endless_growth", "final_drive", "last_gamble", "one_more_chance" };

    IEnumerator MapCardShotsMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(2f);
        gm = GameManager.Instance;
        bool safe = FileSaveStore.Active || MemorySaveStore.Active;
        Check(safe, "runs on a dedicated save only");
        if (!safe) yield break;
        TutorialProgress.MarkOffered();
        GachaStage.DevAllCardsOpen = true; UnlockRules.DevUnlockAll = true;
        string res = $"{Screen.width}x{Screen.height}";
        Check(CardDatabase.AllCards.Count(c => !CardVariant.IsVariantKey(c.cardId)) == 150, $"150 cards ({CardDatabase.AllCards.Count})");

        // ---- ガチャ
        foreach (var id in new[] { "last_bastion", "sonic_edge", "elemental_chaos", "one_more_chance", "air_combo", "launch_master", "final_drive" }) // 2026-11-06 夜: 絵を差し替えた3枚を追加
        {
            var c = CardDatabase.FindBaseById(id);
            if (c == null) { Check(false, $"card {id} exists"); continue; }
            gm.DebugGachaInject(c);
            float t = 0f; while (!gm.GachaResultOpen && t < 3f) { yield return null; t += Time.unscaledDeltaTime; }
            yield return new WaitForSecondsRealtime(c.rarity >= 4 ? 1.2f : 0.8f);
            Shot($"mc_gacha_{id}_{res}"); yield return null; yield return null;
            gm.DebugGachaClose();
            yield return new WaitForSecondsRealtime(0.4f);
        }

        // ---- カード編集
        foreach (var id in MapCardIds) CardInventory.AddCard(id, 1, 1);
        CardInventory.AddCard("one_more_chance", 9, 1); CardInventory.AddCard("final_drive", 5, 1);
        gm.SetDeck(MapCardIds.Take(12).ToList());
        gm.OpenDeckEdit();
        yield return new WaitForSecondsRealtime(1.5f);
        var ui = gm.deckEditUI;
        Shot($"mc_deck_all_{res}"); yield return null; yield return null;
        foreach (var m in new[] { CardMapCategory.Cave, CardMapCategory.Sky, CardMapCategory.LastDungeon })
        {
            ui.DebugSetMapFilter((int)m); yield return new WaitForSecondsRealtime(0.5f);
            Shot($"mc_deck_{GachaSkinFx.KeyOf(m)}_{res}"); yield return null; yield return null;
        }
        // 長押しの詳細: ラスダンの絞り込みの中の ONE MORE CHANCE と FINAL DRIVE
        foreach (var want in new[] { "one_more_chance", "elemental_chaos", "sonic_edge", "air_combo", "launch_master", "final_drive" }) // 2026-11-06 夜: 絵を差し替えた3枚を追加
        {
            ui.DebugSetMapFilter((int)CardMapCategories.Of(CardDatabase.FindBaseById(want))); yield return new WaitForSecondsRealtime(0.4f);
            int idx = -1; for (int i = 0; i < ui.DebugOwnedShownCount; i++) { var d = ui.DebugShownDef(i); if (d != null && CardMastery.BaseIdOf(d.cardId) == want) { idx = i; break; } }
            Check(idx >= 0, $"{want} shown in its map filter");
            if (idx < 0) continue;
            ui.DebugLongPress(false, false, idx);
            yield return new WaitForSecondsRealtime(0.6f);
            Shot($"mc_detail_{want}_{res}"); yield return null; yield return null;
            ui.CloseBigDetail(); yield return new WaitForSecondsRealtime(0.3f);
        }
        ui.DebugSetMapFilter(-1);
        ui.Close();
        yield return new WaitForSecondsRealtime(1.5f);

        // ---- ラン中の3択(新カードだけのデッキ)
        gm.SetDeck(MapCardIds.Skip(8).Take(12).ToList());
        yield return BeginRun("swordsman", "sky_corridor");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        yield return new WaitForSecondsRealtime(2f);
        for (int k = 0; k < 2; k++)
        {
            autoPickHold = true; // 2026-11-06 夜: 撮る前に自動の選択が押さないように(前の回で動かした自動選択が残っている)
            typeof(GameManager).GetMethod("TriggerLevelUpChoice", NP).Invoke(gm, null);
            yield return WaitTut(() => gm.IsRewardSequenceWaitingForSelection, 6f);
            yield return new WaitForSecondsRealtime(0.8f);
            Shot($"mc_choice{k}_{res}"); yield return null; yield return null;
            autoPickHold = false; // 撮った後は、起動時から動いている自動選択(AutoPickCards)に選ばせる
            yield return WaitTut(() => !gm.IsRewardSequenceRunning, 8f);
            yield return new WaitForSecondsRealtime(0.8f);
        }
        // 2026-11-06 夜: 絵を差し替えた3枚だけの3択(デッキをその3枚にして、もう1回)
        var deckF = typeof(GameManager).GetField("deckCards", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var deckL = (System.Collections.Generic.List<string>)deckF.GetValue(gm);
        var deckKeep = new System.Collections.Generic.List<string>(deckL);
        deckL.Clear(); deckL.AddRange(new[] { "air_combo", "launch_master", "final_drive" });
        autoPickHold = true;
        typeof(GameManager).GetMethod("TriggerLevelUpChoice", NP).Invoke(gm, null);
        yield return WaitTut(() => gm.IsRewardSequenceWaitingForSelection, 6f);
        yield return new WaitForSecondsRealtime(0.8f);
        Shot($"mc_choice_newart_{res}"); yield return null; yield return null;
        autoPickHold = false;
        yield return WaitTut(() => !gm.IsRewardSequenceRunning, 8f);
        yield return new WaitForSecondsRealtime(0.8f);
        deckL.Clear(); deckL.AddRange(deckKeep);
        Shot($"mc_run_{res}"); yield return null; yield return null;
        stopKeepAlive = true;
        L($"[mapcards] shots done ({res})");
    }
}
#endif
