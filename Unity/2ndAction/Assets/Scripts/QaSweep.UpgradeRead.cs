#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// 前の版の保存を新しい版で読む(2026-11-14、本番前の確認): -qaUpgradeRead <dir>(-memSave を付けない。試験用の製品名の保存だけで使う)
//  前の版のコードが書いた保存(カード/デッキ/プリセット/MILE/記録/解放/パスの控え)を、そのまま読めていることを書き出す。何も書き換えない
public partial class QaSweep
{
    IEnumerator UpgradeReadModeQa()
    {
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(2f);
        gm = GameManager.Instance;
        Check(!MemorySaveStore.Active && !FileSaveStore.Active, "reads the real (sandbox product) save");
        var sb = new System.Text.StringBuilder();
        int stacks = CardInventory.Stacks.Count, cards = CardInventory.Stacks.Sum(s => s.count), maxLv = CardInventory.Stacks.Count == 0 ? 0 : CardInventory.Stacks.Max(s => s.level);
        sb.AppendLine($"MILE {gm.TotalOwnedMile}");
        sb.AppendLine($"cards: {stacks} stacks / {cards} copies / max Lv {maxLv}");
        sb.AppendLine($"deck ({gm.DeckCards.Count}): {string.Join(",", gm.DeckCards)}");
        sb.AppendLine($"preset active {DeckPresets.Active + 1}: {DeckPresets.DisplayName(DeckPresets.Active)}");
        sb.AppendLine($"BEST {gm.BestDistance:F1} / map bests " + string.Join(" ", UnlockRules.NormalMaps.Select(m => m + "=" + UnlockRules.Reach(m).ToString("F0"))));
        sb.AppendLine($"stages unlocked: cave={UnlockRules.OfficialStageUnlocked(UnlockRules.Cave)} sky={UnlockRules.OfficialStageUnlocked(UnlockRules.Sky)} final={ProgressStats.FinalDungeonUnlocked}");
        sb.AppendLine($"no-ads pass cached: {NoAdsPass.Owned}");
        sb.AppendLine($"gacha category: {gm.GachaCategory} / patch notes unread: {PatchNotesUi.HasUnread}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "upgrade_read.txt"), sb.ToString());
        L("[UP] " + sb.ToString().Replace("\n", " | "));
        Check(stacks > 0 && gm.DeckCards.Count > 0, "the older save's cards and deck are read");
        yield return ShotW("upgrade_home");
    }
}
#endif
