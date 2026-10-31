#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// カードの絵とレイアウトの見直し(2026-10-31)の確認: -qaCardArt <dir> [-caLangs ja,en,de] [-caDetail id,id,...] [-caChoice id,id,id;id,id,id]
//  テスト用データで全カードを所持した状態にして、実際の画面の大きさで撮る:
//   デッキ編集(所持カードの一覧を上から順に / 詳細 / デッキの枠)・カード合成・ラン中の3択・ラン中のカードの欄(HUD)
public partial class QaSweep
{
    IEnumerator CardArtMode()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();
        var all = CardDatabase.AllCards.Where(c => c != null && !CardVariant.IsVariantKey(c.cardId)).ToList();
        // 全カードを所持(Lv はばらけさせる: 1 / 3 / 9。名前と Lv の重なりを見る)
        int n = 0;
        foreach (var c in all) { CardInventory.AddCard(c.cardId, 1, 1); if ((n++ % 3) != 1) CardInventory.AddCard(c.cardId, n % 3 == 1 ? 9 : 3, 1); } // Lv1 は全部(3択のデッキ用)
        UnlockRules.DevUnlockAll = true;
        L($"[ca] owned {all.Count} cards");
        string[] langs = Arg("-caLangs", "ja").Split(',');
        string[] detail = Arg("-caDetail", "greed,executioner,chain_explosion,attack_up,flame_blade,high_voltage,thunder_strike,the_long_road,long_haul,boss_rush,boss_challenge").Split(',');
        string[] choices = Arg("-caChoice", "attack_up,flame_blade,greed;air_attack_up,sky_master,air_dominion;combo_plus,combo_edge,combo_master").Split(';');
        bool runShots = Arg("-caRun", "1") == "1";
        foreach (var lang in langs)
        {
            Loc.Set(lang, save: false);
            yield return new WaitForSecondsRealtime(0.5f);
            // デッキ編集: 所持カードを上から順に
            gm.OpenDeckEdit();
            yield return new WaitForSecondsRealtime(1.2f);
            var ui = gm.deckEditUI;
            if (ui != null && ui.ownedScrollRect != null)
            {
                int pages = int.Parse(Arg("-caPages", "8"));
                for (int p = 0; p < pages; p++)
                {
                    ui.ownedScrollRect.verticalNormalizedPosition = 1f - p / (float)Mathf.Max(1, pages - 1);
                    yield return new WaitForSecondsRealtime(0.35f);
                    Shot($"{lang}_deck_owned_{p}");
                    yield return Frames(2);
                }
                ui.ownedScrollRect.verticalNormalizedPosition = 1f;
            }
            // 詳細(候補のカード)
            foreach (var id in detail)
            {
                int idx = ui != null ? ui.DebugIndexOfOwned(id) : -1;
                if (idx < 0) { L($"[ca] {id} not in the owned list"); continue; }
                ui.DebugFocusOwned(idx);
                yield return new WaitForSecondsRealtime(0.45f);
                Shot($"{lang}_detail_{id}");
                yield return Frames(2);
            }
            if (ui != null) ui.Close(); // gm.CloseDeckEdit は閉じた後の後始末だけ(画面は ui.Close で閉じる)
            yield return new WaitForSecondsRealtime(1.2f);
            // カード合成
            gm.OpenCardFusion();
            yield return new WaitForSecondsRealtime(1.2f);
            Shot($"{lang}_fusion");
            yield return Frames(2);
            if (gm.cardFusionUI != null) gm.cardFusionUI.Close();
            yield return new WaitForSecondsRealtime(1.2f);
            if (!runShots) continue;
            // ラン中の3択とカードの欄
            var keepDeck = gm.DeckCards.ToList();
            autoPickHold = true;
            yield return BeginRun("swordsman", "wasteland_road");
            stopKeepAlive = true;
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            PlayerController.DebugSpeedScale = 0.3f;
            int k = 0;
            foreach (var set in choices)
            {
                var ids = set.Split(',');
                gm.SetDeck(ids);
                gm.GrantBonusCardChoice();
                float t = 0f;
                while (!gm.IsRewardSequenceWaitingForSelection && t < 8f) { yield return null; t += Time.unscaledDeltaTime; }
                yield return new WaitForSecondsRealtime(0.6f);
                Shot($"{lang}_choice_{k++}");
                yield return Frames(2);
                // 1枚目を選んで閉じる
                var seq = FindFirstObjectByType<RewardCardSequence>();
                autoPickHold = false;
                t = 0f; while (gm.IsRewardSequenceRunning && t < 8f) { yield return null; t += Time.unscaledDeltaTime; }
                autoPickHold = true;
                yield return new WaitForSecondsRealtime(0.8f);
            }
            Shot($"{lang}_run_cards");
            yield return EndRun();
            gm = GameManager.Instance; // ランを終えるとシーンを読み直す(古い参照を使わない)
            autoPickHold = false;
            gm.SetDeck(keepDeck);
        }
        Loc.Set("ja", save: false);
        UnlockRules.DevUnlockAll = false;
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }
}
#endif
