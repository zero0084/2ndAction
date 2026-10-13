#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// ガチャの連続操作(2026-10-07)の自動テスト: -qaGacha <dir>(テスト用データの中で行う)
//  A 連打: 所持金の範囲で1回ずつ。支払い回数 = 獲得枚数、マイナスにならない、足りない回は断る
//  B 確認: 1枚ずつ順番に出る。確認中の本体タップ = 閉じて次の1回。開いた瞬間の指では閉じない。本体以外への押下は吸い取る
//  C 画面移動: 見せる前にランへ出ても、獲得済みのカードは所持のまま
public partial class QaSweep
{
    IEnumerator GachaModeQa()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();
        var tap = typeof(GameManager).GetMethod("OnGachaMachineTapped", NP);
        var input = typeof(GameManager).GetMethod("GachaPopupInput", NP);

        // ---- A
        L("== A: rapid taps ==");
        gm.AddMile(GameManager.GachaCostMile * 5 + 100);
        int mile0 = gm.TotalOwnedMile, owned0 = TotalOwned();
        for (int i = 0; i < 8; i++) tap.Invoke(gm, null); // 同じフレームで8回(演出の最中も含む)
        L($"[A] MILE {mile0} -> {gm.TotalOwnedMile}, owned {owned0} -> {TotalOwned()}, pulls {gm.GachaPulls}, refused {gm.GachaRefusedNoMile}, queued {gm.GachaPendingReveals}");
        Check(gm.GachaPulls == 5 && gm.GachaRefusedNoMile == 3, "A: 8 taps with MILE for 5 -> exactly 5 pulls, 3 refused");
        Check(gm.TotalOwnedMile == mile0 - GameManager.GachaCostMile * 5 && gm.TotalOwnedMile >= 0, "A: MILE spent exactly 5 times, never negative");
        Check(TotalOwned() == owned0 + 5, "A: exactly 5 cards gained (one per paid pull)");
        Check(SaveStore.GetInt("TotalOwnedMile", -1) == gm.TotalOwnedMile, "A: the spend is saved immediately");

        // ---- B
        L("== B: reveal / close ==");
        yield return WaitTut(() => gm.GachaResultOpen, 3f);
        Check(gm.GachaResultOpen && gm.GachaPendingReveals == 4, $"B: the first card is shown, 4 waiting ({gm.GachaPendingReveals})");
        yield return new WaitForSecondsRealtime(0.3f);
        Shot("gacha_result");
        // 開いた瞬間の指(開く前から押していた)で閉じない: 開いた直後の押下→離し
        typeof(GameManager).GetField("gachaResultOpenedAt", NP).SetValue(gm, Time.unscaledTime);
        string first = gm.GachaResultCardId;
        SendEvent(input, EventType.MouseDown, new Vector2(40f, 40f));
        SendEvent(input, EventType.MouseUp, new Vector2(40f, 40f));
        Check(gm.GachaResultOpen && gm.GachaResultCardId == first, "B: the tap that was down when the card appeared does not close it");
        // 新しいタップ(本体以外)で閉じる。背後へは通さない
        yield return new WaitForSecondsRealtime(0.2f);
        var ev = SendEvent(input, EventType.MouseDown, new Vector2(40f, 40f));
        Check(ev.type == EventType.Used, "B: a press outside the gacha machine is swallowed (does not reach the home buttons)");
        SendEvent(input, EventType.MouseUp, new Vector2(40f, 40f));
        Check(!gm.GachaResultOpen, "B: tapping anywhere closes the card");
        int shown = 1;
        for (int i = 0; i < 10 && (gm.GachaPendingReveals > 0 || gm.GachaResultOpen); i++)
        {
            yield return WaitTut(() => gm.GachaResultOpen, 2f);
            if (!gm.GachaResultOpen) break;
            shown++;
            typeof(GameManager).GetMethod("CloseGachaResult", NP).Invoke(gm, null);
            yield return null;
        }
        Check(shown == 5, $"B: all 5 cards were shown one by one ({shown})");
        // 確認中に本体をタップ = 閉じて次の1回
        gm.AddMile(GameManager.GachaCostMile * 2);
        tap.Invoke(gm, null);
        yield return WaitTut(() => gm.GachaResultOpen, 3f);
        int pulls = gm.GachaPulls;
        tap.Invoke(gm, null);
        Check(!gm.GachaResultOpen && gm.GachaPulls == pulls + 1, "B: tapping the machine while a card is shown closes it and draws the next");
        yield return WaitTut(() => gm.GachaResultOpen, 3f);
        // 本体の上の押下は吸い取らない(本体のボタンへ届く)
        var mr = (Rect)typeof(GameManager).GetField("lastGachaMachineRect", NP).GetValue(gm);
        var ev2 = SendEvent(input, EventType.MouseDown, mr.center);
        Check(ev2.type == EventType.MouseDown && mr.width > 0f, "B: a press on the machine passes through to the machine button");
        Check(mr.width > 0f, $"B: machine rect known ({mr})");
        typeof(GameManager).GetMethod("CloseGachaResult", NP).Invoke(gm, null);

        // ---- C
        L("== C: leave before seeing ==");
        gm.AddMile(GameManager.GachaCostMile * 3);
        int o1 = TotalOwned(), m1 = gm.TotalOwnedMile;
        tap.Invoke(gm, null); tap.Invoke(gm, null); tap.Invoke(gm, null);
        yield return BeginRun("swordsman", "wasteland_road");
        Check(TotalOwned() == o1 + 3 && SaveStore.GetInt("TotalOwnedMile", -1) == m1 - GameManager.GachaCostMile * 3, "C: cards drawn but not yet shown are owned after leaving home");
        Check(!gm.GachaResultOpen && gm.GachaPendingReveals == 0, "C: nothing left to show inside the run");
        yield return EndRun();
        yield return ReloadHome();
        Check(TotalOwned() == o1 + 3, "C: still owned after reload");
        // ---- D NEW の判定(2026-10-13): 引いた時点で同名(通常/合成済み)を持っていないか。続けて引いた同じカードは最初の1枚だけ NEW
        L("== D: NEW / DUPLICATE ==");
        var poolD = (List<CardDefinition>)typeof(GameManager).GetMethod("BuildGachaPool", NP).Invoke(gm, null);
        var fresh = poolD.Where(c => c != null && CardInventory.GetTotalCountByName(c.cardId) == 0).Take(2).ToList();
        Check(fresh.Count == 2, $"D: two cards not owned yet exist in the pool ({fresh.Count})");
        if (fresh.Count == 2)
        {
            if (gm.GachaResultOpen) typeof(GameManager).GetMethod("CloseGachaResult", NP).Invoke(gm, null);
            while (gm.GachaPendingReveals > 0 || gm.GachaResultOpen) { if (gm.GachaResultOpen) typeof(GameManager).GetMethod("CloseGachaResult", NP).Invoke(gm, null); yield return null; }
            gm.AddMile(GameManager.GachaCostMile * 3);
            GameManager.DevForceGachaCard = fresh[0].cardId;
            tap.Invoke(gm, null); tap.Invoke(gm, null); // 同じカードを続けて2回(見せる前に2枚目)
            yield return WaitTut(() => gm.GachaResultOpen, 3f);
            bool firstNew = gm.GachaResultIsNew;
            yield return new WaitForSecondsRealtime(0.8f); Shot("gacha_D_1st_new"); yield return null; yield return null;
            typeof(GameManager).GetMethod("CloseGachaResult", NP).Invoke(gm, null);
            yield return WaitTut(() => gm.GachaResultOpen, 3f);
            bool secondNew = gm.GachaResultIsNew;
            yield return new WaitForSecondsRealtime(0.8f); Shot("gacha_D_2nd_duplicate"); yield return null; yield return null;
            Check(firstNew && !secondNew, $"D: the same new card twice in a row -> first NEW, second DUPLICATE ({firstNew}/{secondNew})");
            typeof(GameManager).GetMethod("CloseGachaResult", NP).Invoke(gm, null);
            yield return null;
            // 合成済み(v2 キー)だけを持っている同名のカード → DUPLICATE
            var v = new CardVariant { mainId = fresh[1].cardId, level = 3, rarity = Mathf.Max(1, fresh[1].rarity) };
            v.AddAbility(fresh[1].cardId, 3);
            CardInventory.AddCard(v.ToKey(), 1, 1);
            Check(CardInventory.GetTotalCount(fresh[1].cardId) == 0 && CardInventory.GetTotalCountByName(fresh[1].cardId) == 1, "D: the fused copy is counted by name only");
            GameManager.DevForceGachaCard = fresh[1].cardId;
            tap.Invoke(gm, null);
            yield return WaitTut(() => gm.GachaResultOpen, 3f);
            Check(!gm.GachaResultIsNew, "D: owning only a fused card of the same name -> DUPLICATE");
            yield return new WaitForSecondsRealtime(0.8f); Shot("gacha_D_duplicate_fused"); yield return null; yield return null;
            typeof(GameManager).GetMethod("CloseGachaResult", NP).Invoke(gm, null);
            GameManager.DevForceGachaCard = null;
        }

        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }

    static int TotalOwned() { int n = 0; foreach (var s in CardInventory.Stacks) n += s.count; return n; }

    Event SendEvent(MethodInfo m, EventType t, Vector2 guiPos)
    {
        var e = new Event { type = t, mousePosition = guiPos, button = 0 };
        Event.current = e;
        m.Invoke(gm, null);
        return e;
    }
}
#endif
