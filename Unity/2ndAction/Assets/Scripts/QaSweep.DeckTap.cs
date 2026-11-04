#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// デッキ編集の空白のタップ(2026-11-04、実機の不具合「空き枠や余白を押すと見えないカードが入る」): -qaDeckTap <dir>(テスト用データ SaveProfile の中だけ)
//  一覧を 上端/途中/末尾 へ動かし、デッキの空き枠・一覧の上下の境目・画面の余白(格子)を押して、デッキが変わらないこと。
//  見えているカードを押すと今までどおり入ること(長押し/ドラッグは -qaDeckV2 で確認)。縦は -screen-width 486 -screen-height 1080 で起動。
//  -deckTapLegacy を付けると直す前の判定(前後比較)
public partial class QaSweep
{
    IEnumerator DeckTapMode()
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
        // 一覧が何画面分にもなるように、いろいろなカードを持たせる
        int added = 0;
        foreach (var c in CardDatabase.UnlockedCards)
        {
            if (c == null || added >= 60) break;
            if (CardInventory.GetTotalCount(c.cardId) < 1) { CardInventory.AddCard(c.cardId, 1, 1); added++; }
        }
        var bases = CardDatabase.UnlockedCards.Where(c => c != null).Select(c => c.cardId).Take(3).ToArray();
        gm.SetDeck(bases); // 3 枚 = 空き枠が 9
        string tag = Screen.height > Screen.width ? "port" : "land";
        L($"[setup] {tag} {Screen.width}x{Screen.height}, owned stacks {CardInventory.Stacks.Count()}, deck {gm.DeckCards.Count} {(DeckEditUI_Legacy ? "(LEGACY tap)" : "")}");
        gm.OpenDeckEdit();
        yield return new WaitForSecondsRealtime(1.4f);
        var ui = gm.deckEditUI;
        string deck0 = string.Join(",", gm.DeckCards);
        int totalHidden = 0, totalAdds = 0, totalTaps = 0;

        foreach (var pos in new[] { ("top", 1f), ("middle", 0.5f), ("end", 0f) })
        {
            ui.DebugSetOwnedScroll(pos.Item2);
            yield return null; yield return null;
            Rect vp = ScreenRectOf(ui.DebugOwnedViewport);
            var pts = new List<(string, Vector2)>();
            for (int i = gm.DeckCards.Count; i < 12; i++) pts.Add(("empty slot " + (i + 1), ui.DebugDeckSlotPos(i)));
            for (float fx = 0.1f; fx < 0.95f; fx += 0.1f)
            {
                pts.Add(("above list", new Vector2(vp.xMin + vp.width * fx, vp.yMax + 3f)));
                pts.Add(("above list +40", new Vector2(vp.xMin + vp.width * fx, vp.yMax + 40f)));
                pts.Add(("below list", new Vector2(vp.xMin + vp.width * fx, vp.yMin - 3f)));
            }
            for (float gx = 0.03f; gx < 1f; gx += 0.06f)
                for (float gy = 0.03f; gy < 1f; gy += 0.06f)
                {
                    var p = new Vector2(Screen.width * gx, Screen.height * gy);
                    if (!vp.Contains(p)) pts.Add(("grid", p));
                }
            int hidden = 0, adds = 0;
            foreach (var (what, p) in pts)
            {
                if (p.x < 0 || p.y < 0 || p.x > Screen.width || p.y > Screen.height) continue;
                bool hid = ui.DebugHiddenOwnedAt(p);
                if (hid) hidden++;
                int n = gm.DeckCards.Count;
                ui.DebugTapAt(p);
                totalTaps++;
                if (gm.DeckCards.Count > n)
                {
                    adds++;
                    L($"[{pos.Item1}] ADDED by tapping {what} at ({p.x:F0},{p.y:F0}) hiddenCard={hid}: {gm.DeckCards.Last()}");
                    gm.SetDeck(deck0.Split(','));
                }
                // ボタン等で画面が変わった(閉じた/確認の窓)時は戻す
                if (ui.OverlayOpen) { ui.HandleBack(); yield return null; }
                if (!ui.root.activeInHierarchy) { gm.OpenDeckEdit(); yield return new WaitForSecondsRealtime(1.2f); ui.DebugSetOwnedScroll(pos.Item2); yield return null; }
                if (gm.DeckCards.Count < n) gm.SetDeck(deck0.Split(','));
            }
            totalHidden += hidden; totalAdds += adds;
            L($"[{pos.Item1}] tapped {pts.Count} points outside the visible list: hidden cards under {hidden}, cards added {adds}");
            Check(adds == 0, $"{tag} {pos.Item1}: tapping empty slots / margins / list edges adds no card ({adds})");
            yield return ShotW($"dt_{tag}_{pos.Item1}");
        }
        L($"[total] taps {totalTaps}, points with a hidden card under the finger {totalHidden}, cards added {totalAdds}");

        // 見えているカードは今までどおり入る(途中まで動かした一覧の、全部見えているカード)
        ui.DebugSetOwnedScroll(0.5f);
        yield return null;
        int vis = Enumerable.Range(0, ui.DebugOwnedShown).FirstOrDefault(i => ui.DebugOwnedFullyVisible(i) && !gm.DeckCards.Contains(OwnedKeyAt(ui, i)));
        int before = gm.DeckCards.Count;
        ui.DebugTapAt(ui.DebugOwnedPos(vis));
        Check(gm.DeckCards.Count == before + 1, $"{tag}: tapping a visible owned card (#{vis}) still adds it ({before} -> {gm.DeckCards.Count})");
        // デッキの埋まった枠を押すと外れる(今までどおり)
        before = gm.DeckCards.Count;
        ui.DebugTapAt(ui.DebugDeckSlotPos(0));
        Check(gm.DeckCards.Count == before - 1, $"{tag}: tapping a filled deck slot still removes it ({before} -> {gm.DeckCards.Count})");
        gm.CloseDeckEdit();
        yield return new WaitForSecondsRealtime(0.8f);
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }

    static bool DeckEditUI_Legacy => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-deckTapLegacy") >= 0;
    static string OwnedKeyAt(DeckEditUI ui, int i) { var st = ui.DebugOwnedStack(i); return st != null ? st.cardId : ""; }
}
#endif
