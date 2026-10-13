#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// 2026-10-13 第2弾の表示の確認(撮影つき): -qaFix13b <dir>(テスト用データの中で行う)
//  C3 合成カードの詳細(能力ごとの Lv と実際の値) / 動きの効果の具体的な文
//  A12 通常の強化でちょうど Lv.9 → Lv.9 MAX / C4 合成画面の情報行が切れない
public partial class QaSweep
{
    IEnumerator Fix13bModeQa()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();
        string res = $"{Screen.width}x{Screen.height}";

        // ---- C3: 合成カードの詳細
        L("== C3: fused card detail ==");
        var v = new CardVariant { mainId = "sonic_blade", level = 9, rarity = 3 };
        v.AddAbility("sonic_blade", 4); v.AddAbility("attack_up", 3); v.AddAbility("shield", 2);
        string key = v.ToKey();
        CardInventory.AddCard(key, 9, 1);
        CardInventory.AddCard("shockwave", 1, 1);
        string desc = v.Describe();
        L("[C3] " + desc.Replace("\n", " / "));
        Check(!desc.Contains("×強化量") && !desc.Contains("効果Lv"), "C3: the fused description has no uniform '×強化量' / '効果Lv'");
        gm.OpenDeckEdit();
        yield return new WaitForSecondsRealtime(1.3f);
        var deck = gm.deckEditUI;
        int idx = deck.DebugIndexOfOwned(key);
        Check(idx >= 0, $"C3: the fused card is in the collection ({idx})");
        if (idx >= 0) { deck.DebugFocusOwned(idx); yield return new WaitForSecondsRealtime(0.4f); Shot($"c3_fused_detail_{res}"); yield return null; yield return null; }
        int idx2 = deck.DebugIndexOfOwned("shockwave");
        if (idx2 >= 0) { deck.DebugFocusOwned(idx2); yield return new WaitForSecondsRealtime(0.4f); Shot($"c3_behavior_detail_{res}"); yield return null; yield return null; }
        gm.CloseDeckEdit();
        yield return new WaitForSecondsRealtime(1f);

        // ---- A12 / C4: 合成でちょうど Lv.9
        L("== A12/C4: fusion to exactly Lv.9 ==");
        string id = "long_blade";
        string k4 = CardDataMigration.LegacyToKey(id, 4), k5 = CardDataMigration.LegacyToKey(id, 5);
        CardInventory.AddCard(k4, 4, 1); CardInventory.AddCard(k5, 5, 1);
        gm.OpenCardFusion();
        yield return new WaitForSecondsRealtime(0.8f);
        var ui = FindFirstObjectByType<CardFusionUI>(FindObjectsInactive.Include);
        Check(ui != null, "A12: fusion screen found");
        if (ui != null)
        {
            ui.DebugSelect(k5, null);
            yield return new WaitForSecondsRealtime(0.4f);
            Shot($"c4_fusion_info_{res}"); yield return null; yield return null;
            ui.DebugSelect(k5, k4);
            yield return new WaitForSecondsRealtime(0.4f);
            string detail = (typeof(CardFusionUI).GetField("detailText", NP).GetValue(ui) as UnityEngine.UI.Text)?.text ?? "";
            L("[A12] preview: " + detail.Replace("\n", " / "));
            Check(detail.Contains("MAX"), "A12: the preview of Lv5 + Lv4 says MAX");
            Shot($"a12_fusion_preview_{res}"); yield return null; yield return null;
            ui.DebugFuse();
            // 結果の画面が出るまで演出を飛ばす(段ごとに飛ばす印が戻るので、出るまで押し続ける)
            var phaseF = typeof(CardFusionUI).GetField("phase", NP);
            float wr = 0f;
            while (wr < 8f && phaseF != null && phaseF.GetValue(ui).ToString() != "Result") { ui.DebugSkip(); yield return null; wr += Time.unscaledDeltaTime; }
            yield return new WaitForSecondsRealtime(0.6f);
            Shot($"a12_fusion_result_{res}"); yield return null; yield return null;
            ui.DebugContinue();
            yield return new WaitForSecondsRealtime(0.5f);
            Check(CardInventory.GetTotalCount(CardDataMigration.LegacyToKey(id, 9)) >= 1, "A12: the fused card is Lv.9");
            ui.Close(); gm.CloseCardFusion();
            yield return new WaitForSecondsRealtime(1f);
        }
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }
}
#endif
