#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// カード長期育成(Mastery / AWAKENED、2026-10-04)の自動テスト: -qaMastery <dir> [-qaMasteryOnly ABCDEFGHIJKLMN]
//  A Lv8+合成→Lv9  B Lv9+1枚→★1  C 繰り越し  D 一度に大量→複数の★  E ★5=AWAKENED  F AWAKENED後もLv9
//  G 通常性能が変わらない  H 旧セーブ→新(Lv維持/★0)  I 保存→再読込/CONTINUE で一致  J キャラカード/デッキでLv9上限を超えない
//  K ガチャで Lv9 のカードを引いても消えない  L ★5後の余りが失われない  M 総数(MAX/AWAKENED)は動的  N 合成画面/コレクション画面
// テストの前後でセーブ全体を控え/戻す(実セーブを汚さない)。
public partial class QaSweep
{
    string mId, mId2;
    string K(string id, int lv) => CardDataMigration.LegacyToKey(id, lv);
    int Cnt(string key) { var s = CardInventory.FindByKey(key); return s != null ? s.count : 0; }
    // その主能力のカードの所持Lvの最大(合成カード v2|... のキーも含む)
    static int TopLevel(string baseId) { int m = 0; foreach (var s in CardInventory.Stacks) if (s.count > 0 && CardMastery.BaseIdOf(s.cardId) == baseId) m = Mathf.Max(m, s.level); return m; }

    IEnumerator MasteryMode()
    {
        var snapSave = SaveSystem.Capture();
        float w = 0f;
        while ((GameManager.Instance == null) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        yield return new WaitForSecondsRealtime(1f);
        string only = Arg("-qaMasteryOnly", "ABCDEFGHIJKLMN");
        // 新しい状態から(所持カード/Mastery を空に)。デッキ/キャラカードで使われていないカードを選ぶ
        PlayerPrefs.DeleteKey(CardInventory.SaveKey); PlayerPrefs.DeleteKey(CardMastery.SaveKey);
        CardInventory.ReloadFromPrefs(); CardMastery.ReloadFromPrefs();
        var free = CardDatabase.AllCards.Where(c => !CardVariant.IsVariantKey(c.cardId) && !gm.IsCardInUse(c.cardId)).Select(c => c.cardId).ToList();
        mId = free.Contains("attack_up") ? "attack_up" : free[0];
        mId2 = free.First(x => x != mId);
        L($"[mastery] test cards: {mId}, {mId2}; need = {string.Join("/", MasteryTuning.I.need)} (total {MasteryTuning.TotalToAwaken})");

        if (only.Contains('A') || only.Contains('B') || only.Contains('C') || only.Contains('D') || only.Contains('E') || only.Contains('F') || only.Contains('L')) MasteryFusionChain(only);
        if (only.Contains('M')) MasteryCounts();
        if (only.Contains('H')) MasteryMigration();
        if (only.Contains('I')) yield return MasterySaveLoad();
        if (only.Contains('K')) MasteryGacha();
        if (only.Contains('N')) yield return MasteryUi();
        if (only.Contains('G')) yield return MasteryStats();
        if (only.Contains('J')) yield return MasteryCap();

        SaveSystem.Restore(snapSave);
        CardInventory.ReloadFromPrefs(); CardMastery.ReloadFromPrefs();
        RunCheckpoint.Reload();
        L("[mastery] test machine save restored");
    }

    void MasteryFusionChain(string only)
    {
        L("== A-F/L: 合成の流れ ==");
        string id = mId, k8 = K(id, 8), k9 = K(id, 9);
        // A: Lv8 + Lv1 → Lv9 MAX
        CardInventory.AddCard(k8, 8, 1); CardInventory.AddCard(id, 1, 1);
        var r = CardFusionLogic.Execute(k8, id, out string err);
        Check(r != null && r.kind == CardFusionLogic.Kind.SameName && r.result.level == 9 && r.masteryGain == 0 && Cnt(k9) == 1, $"A: Lv8 + Lv1 -> Lv9 MAX ({(r != null ? r.resultKey : err)})");
        Check(CardMastery.IsMaxReached(id) && CardMastery.MasteryLevel(id) == 0 && !CardMastery.IsAwakened(id), "A: Lv9 MAX recorded, Mastery ★0");
        // B: Lv9 + 1枚 → ★1(メインは残り、素材だけ消える)
        CardInventory.AddCard(id, 1, 1);
        Check(CardFusionLogic.BlockReason(k9, id) == null, $"B: Lv9 MAX main + same card can be fused ({CardFusionLogic.BlockReason(k9, id)})");
        r = CardFusionLogic.Execute(k9, id, out err);
        Check(r != null && r.kind == CardFusionLogic.Kind.Mastery && r.masteryGain == 1, $"B: kind Mastery +1 ({r?.kind} {err})");
        Check(Cnt(k9) == 1 && Cnt(id) == 0, $"B: the Lv9 main stays, the material is consumed (Lv9 x{Cnt(k9)}, Lv1 x{Cnt(id)})");
        Check(CardMastery.MasteryLevel(id) == 1 && CardMastery.MasteryProgress(id) == 0, $"B: ★1 0/2 (★{CardMastery.MasteryLevel(id)} {CardMastery.MasteryProgress(id)})");
        // C: 繰り越し(★1 0/2 に +3 → ★2 1/3)
        CardInventory.AddCard(K(id, 3), 3, 1);
        r = CardFusionLogic.Execute(k9, K(id, 3), out err);
        Check(r != null && CardMastery.MasteryLevel(id) == 2 && CardMastery.MasteryProgress(id) == 1, $"C: +3 at ★1 0/2 -> ★2 1/3, the extra 1 carried over (★{CardMastery.MasteryLevel(id)} {CardMastery.MasteryProgress(id)}) {err}");
        // D: 一度に大量(Lv9 素材 = +9: ★2 1/3 → ★4 3/5)
        CardInventory.AddCard(k9, 9, 1);
        r = CardFusionLogic.Execute(k9, k9, out err);
        Check(r != null && r.masteryGain == 9 && r.mastery.StarsGained == 2 && CardMastery.MasteryLevel(id) == 4 && CardMastery.MasteryProgress(id) == 3, $"D: +9 crosses two stars: ★2 1/3 -> ★4 3/5 (★{CardMastery.MasteryLevel(id)} {CardMastery.MasteryProgress(id)}) {err}");
        Check(Cnt(k9) == 1, $"D: one Lv9 copy used as material, the main remains (Lv9 x{Cnt(k9)})");
        // E: ★5 = AWAKENED(+2)
        CardInventory.AddCard(K(id, 2), 2, 1);
        r = CardFusionLogic.Execute(k9, K(id, 2), out err);
        Check(r != null && r.mastery.awakenedNow && CardMastery.IsAwakened(id) && CardMastery.MasteryLevel(id) == 5 && CardMastery.Overflow(id) == 0, $"E: ★5 reached = AWAKENED (overflow {CardMastery.Overflow(id)}) {err}");
        // F: AWAKENED 後もカードは Lv9 のまま
        CardVariant v9 = CardVariant.Parse(k9);
        Check(Cnt(k9) == 1 && TopLevel(id) == 9 && v9.level == 9 && CardInventory.Stacks.All(s => s.level <= 9), "F: the card is still Lv9 MAX (no Lv10+)");
        // L: ★5 の後の余り(合成はカードを消費しない / 直接の加算と Lv9 超えの合成は保管へ)
        CardInventory.AddCard(id, 1, 2);
        string block = CardFusionLogic.BlockReason(k9, id);
        var none = CardFusionLogic.Execute(k9, id, out err);
        Check(block != null && none == null && Cnt(id) == 2, $"L: fusing into an AWAKENED card is blocked and consumes nothing (Lv1 x{Cnt(id)}: {block})");
        var g = CardMastery.AddProgress(id, 4, "qa");
        Check(g.overflowAdded == 4 && CardMastery.Overflow(id) == 4 && CardMastery.MasteryLevel(id) == 5, $"L: progress after ★5 is kept as overflow ({CardMastery.Overflow(id)})");
        CardInventory.AddCard(K(id, 5), 5, 1); CardInventory.AddCard(K(id, 6), 6, 1);
        r = CardFusionLogic.Execute(K(id, 5), K(id, 6), out err);
        Check(r != null && r.result.level == 9 && CardMastery.Overflow(id) == 6, $"L: Lv5 + Lv6 on an AWAKENED card -> Lv9 and the extra 2 kept (overflow {CardMastery.Overflow(id)}) {err}");
        // 余り繰り越しの合成(2枚目のカード): Lv5 + Lv5 → Lv9 + Mastery 1(以前は合成不可で消えなかったが、今は捨てずに進む)
        string id2 = mId2, k5 = K(id2, 5);
        CardInventory.AddCard(k5, 5, 2);
        r = CardFusionLogic.Execute(k5, k5, out err);
        var res = r != null ? r.result : null;
        Check(r != null && res.level == 9 && res.Main.stacks == 9 && r.masteryGain == 1 && CardMastery.MasteryLevel(id2) == 1 && Cnt(K(id2, 9)) == 1 && Cnt(k5) == 0,
            $"C: Lv5 + Lv5 -> Lv9 MAX (main x{res?.Main.stacks}) and the extra 1 goes to Mastery ★{CardMastery.MasteryLevel(id2)} {err}");
        // 違うカード同士で合計が9を超える組み合わせは今まで通り合成不可
        CardInventory.AddCard(K(id2, 6), 6, 1);
        Check(CardFusionLogic.BlockReason(k9, K(id2, 6)) != null, "cross-name fusion over Lv9 stays blocked (unchanged)");
        // 合成で消費しないメイン: デッキ/キャラカードで使用中の Lv9 でも、素材が空いていれば Mastery の合成ができる
        L($"[A-F] {id}: ★{CardMastery.MasteryLevel(id)} AWAKENED={CardMastery.IsAwakened(id)} overflow={CardMastery.Overflow(id)}; {id2}: ★{CardMastery.MasteryLevel(id2)} {CardMastery.MasteryProgress(id2)}");
    }

    void MasteryCounts()
    {
        L("== M: 総数 ==");
        int total = CardMastery.TotalCards, db = CardDatabase.AllCards.Count(c => !CardVariant.IsVariantKey(c.cardId));
        L($"[M] MAX {CardMastery.MaxCount}/{total}  AWAKENED {CardMastery.AwakenedCount}/{total}  allAwakened={CardMastery.AllAwakened}");
        Check(total == db && total == CardDatabase.AllCards.Count, $"M: the total comes from the card database ({total}, includes #100 ULTIMATE)");
        Check(Resources.Load<MasteryTuning>("Mastery/MasteryTuning") != null, "M: the tuning asset (Resources/Mastery/MasteryTuning) loads");
        Check(CardMastery.AwakenedCount >= 1 && CardMastery.MaxCount >= 2 && !CardMastery.AllAwakened && CardMastery.ReachedAwakenedMilestone(1) && !CardMastery.ReachedAwakenedMilestone(10), "M: counts / milestones");
    }

    void MasteryMigration()
    {
        L("== H: 旧セーブ → 新しい版 ==");
        var keep = SaveSystem.Capture();
        // 旧APK相当: Mastery のキーが無く、schema 2。Lv9 と Lv3 のカードを持っている
        string inv = "{\"stacks\":[{\"cardId\":\"" + K("speed_up", 9) + "\",\"level\":9,\"count\":1},{\"cardId\":\"" + K("heart_up", 3) + "\",\"level\":3,\"count\":2},{\"cardId\":\"attack_up\",\"level\":1,\"count\":4}]}";
        PlayerPrefs.SetString(CardInventory.SaveKey, inv);
        PlayerPrefs.DeleteKey(CardMastery.SaveKey);
        PlayerPrefs.SetInt(SaveKeys.SchemaVersion, 2);
        var r = SaveSystem.Boot(0);
        CardInventory.ReloadFromPrefs(); CardMastery.ReloadFromPrefs();
        string invAfter = PlayerPrefs.GetString(CardInventory.SaveKey, "");
        L($"[H] boot: {r.report}");
        Check(r.schemaAfter == SaveSystem.CurrentSchemaVersion && !r.migrationFailed, $"H: migrated 2 -> {SaveSystem.CurrentSchemaVersion}");
        Check(TopLevel("speed_up") == 9 && Cnt(K("heart_up", 3)) == 2 && Cnt("attack_up") == 4, "H: card levels/counts kept (Lv9 stays Lv9)");
        Check(CardMastery.IsMaxReached("speed_up") && CardMastery.MasteryLevel("speed_up") == 0 && !CardMastery.IsAwakened("speed_up"), "H: an existing Lv9 is recorded as MAX and starts at ★0 (no guessed Mastery)");
        Check(!CardMastery.IsMaxReached("heart_up") && CardMastery.MasteryLevel("heart_up") == 0, "H: a Lv3 card is not MAX, ★0");
        // 新しい版で2回目の起動: 何も変わらない
        string m1 = PlayerPrefs.GetString(CardMastery.SaveKey, "");
        r = SaveSystem.Boot(0);
        Check(PlayerPrefs.GetString(CardMastery.SaveKey, "") == m1 && PlayerPrefs.GetString(CardInventory.SaveKey, "") == invAfter, "H: a second boot changes nothing");
        // 壊れた Mastery の JSON は修復される(所持カードは無事)
        PlayerPrefs.SetString(CardMastery.SaveKey, "{broken");
        r = SaveSystem.Boot(0);
        Check(r.repairs.Any(x => x.Contains(CardMastery.SaveKey)) && PlayerPrefs.GetString(CardInventory.SaveKey, "") == invAfter, $"H: a corrupt mastery save is repaired, cards untouched ({string.Join(" / ", r.repairs)})");
        SaveSystem.Restore(keep);
        CardInventory.ReloadFromPrefs(); CardMastery.ReloadFromPrefs();
    }

    IEnumerator MasterySaveLoad()
    {
        L("== I: 保存 → 再起動 → 読込 / CONTINUE ==");
        string a = CardMastery.ExportJson();
        CardMastery.ReloadFromPrefs(); CardInventory.ReloadFromPrefs();
        string b = CardMastery.ExportJson();
        Check(a == b && a.Contains(mId), "I: mastery save -> restart (reload from storage) -> identical");
        Check(CardMastery.RoundTripEquals(out string det), "I: JSON round trip " + det);
        // CONTINUE をはさんでも同じ
        yield return BeginRun("swordsman", "wasteland_road");
        yield return new WaitForSecondsRealtime(1.5f);
        typeof(GameManager).GetMethod("SaveCheckpoint", NP).Invoke(gm, null);
        var old = gm;
        gm.ReturnToHome();
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f);
        gm = GameManager.Instance;
        w = 0f; float retry = 0f;
        while (gm.ResumeGate != GameManager.ResumeGatePhase.Waiting && w < 12f)
        {
            if (!gm.HasStarted && retry <= 0f) { gm.ContinueActiveRun(); retry = 0.5f; }
            yield return null; w += Time.unscaledDeltaTime; retry -= Time.unscaledDeltaTime;
        }
        Check(CardMastery.ExportJson() == a && CardMastery.IsAwakened(mId), "I: mastery unchanged across CONTINUE");
        gm.RequestResumeFromGate();
        yield return new WaitForSecondsRealtime(1f);
        yield return EndRun();
        gm = GameManager.Instance;
    }

    void MasteryGacha()
    {
        L("== K: ガチャ ==");
        // 引ける全カードを Lv9 で持たせる → 何が出ても「Lv9 MAX のカードの重複」
        CardInventory.DebugOwnEveryAtLevel(9, 1);
        CardMastery.SyncMaxFromInventory();
        gm.AddMile(100000);
        string before = CardMastery.ExportJson();
        int totalBefore = CardInventory.Stacks.Sum(s => s.count);
        typeof(GameManager).GetMethod("OnGachaMachineTapped", NP).Invoke(gm, null);
        // 2026-10-10: ガチャ連続(10/07)から、引いたカードは見せる順番待ちの列(gachaRevealQueue)の最後に入る
        var queue = (List<CardDefinition>)typeof(GameManager).GetField("gachaRevealQueue", NP).GetValue(gm);
        var drawn = queue != null && queue.Count > 0 ? queue[queue.Count - 1] : (CardDefinition)typeof(GameManager).GetField("gachaResultCard", NP)?.GetValue(gm);
        int totalAfter = CardInventory.Stacks.Sum(s => s.count);
        L($"[K] drew {drawn?.cardId}: owned {totalBefore} -> {totalAfter}, Lv1 copies {Cnt(drawn?.cardId)}, MAX={CardMastery.IsMaxReached(drawn?.cardId)}");
        Check(drawn != null && totalAfter == totalBefore + 1 && Cnt(drawn.cardId) >= 1, "K: the drawn duplicate of a Lv9 MAX card is kept as a card (not lost)");
        Check(CardMastery.ExportJson() == before, "K: not consumed into Mastery automatically (the player uses it in Fusion)");
        // 合成で Mastery の素材にできる(デッキ/キャラカードで唯一の1枚を使っている時は「使用中」で止まるのが正しい)
        string kb = drawn != null ? CardFusionLogic.BlockReason(K(drawn.cardId, 9), drawn.cardId) : "none";
        Check(drawn != null && CardFusionLogic.IsMasteryFusion(CardVariant.Parse(K(drawn.cardId, 9)), CardVariant.Parse(drawn.cardId)) && (kb == null || kb.Contains("使用中") || CardMastery.IsAwakened(drawn.cardId)), $"K: it can be used for Mastery in Fusion ({kb ?? "ready"})");
        typeof(GameManager).GetField("gachaResultOpen", NP)?.SetValue(gm, false);
    }

    IEnumerator MasteryUi()
    {
        L("== N: 合成画面 / コレクション ==");
        string id = mId2, k9 = K(id, 9);
        CardInventory.AddCard(id, 1, 3);
        gm.OpenCardFusion();
        yield return new WaitForSecondsRealtime(0.8f);
        var ui = FindFirstObjectByType<CardFusionUI>(FindObjectsInactive.Include);
        Check(ui != null, "N: fusion screen found");
        if (ui != null)
        {
            ui.DebugSelect(k9, null);
            yield return null;
            string detail = (typeof(CardFusionUI).GetField("detailText", NP).GetValue(ui) as UnityEngine.UI.Text)?.text ?? "";
            Check(detail.Contains("MASTERY"), "N: selecting a Lv9 MAX card shows MASTERY instead of 'cannot fuse'");
            ui.DebugSelect(k9, id);
            yield return null;
            detail = (typeof(CardFusionUI).GetField("detailText", NP).GetValue(ui) as UnityEngine.UI.Text)?.text ?? "";
            string label = (typeof(CardFusionUI).GetField("fuseLabel", NP).GetValue(ui) as UnityEngine.UI.Text)?.text ?? "";
            Check(detail.Contains("+1 Mastery") && label == "合成する", $"N: the preview shows +1 Mastery before fusing ({label})");
            if (Arg("-qaMasteryShots", "0") == "1") { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, "mastery_fusion_preview.png")); yield return new WaitForSecondsRealtime(0.5f); }
            int lv0 = CardMastery.MasteryLevel(id), p0 = CardMastery.MasteryProgress(id);
            ui.DebugFuse();
            yield return new WaitForSecondsRealtime(0.3f);
            ui.DebugSkip();
            yield return new WaitForSecondsRealtime(1.2f);
            if (Arg("-qaMasteryShots", "0") == "1") { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, "mastery_fusion_result.png")); yield return new WaitForSecondsRealtime(0.5f); }
            ui.DebugContinue();
            yield return new WaitForSecondsRealtime(0.5f);
            Check(CardMastery.MasteryLevel(id) * 100 + CardMastery.MasteryProgress(id) > lv0 * 100 + p0 && Cnt(k9) == 1, $"N: fused through the screen: ★{lv0} {p0} -> ★{CardMastery.MasteryLevel(id)} {CardMastery.MasteryProgress(id)}");
            // AWAKENED のカードをメインに(★5 の表示と、合成しても消費しないこと)
            ui.DebugSelect(K(mId, 9), mId);
            yield return new WaitForSecondsRealtime(0.4f);
            if (Arg("-qaMasteryShots", "0") == "1") { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, "mastery_fusion_awakened.png")); yield return new WaitForSecondsRealtime(0.5f); }
            ui.Close(); gm.CloseCardFusion();
            yield return new WaitForSecondsRealtime(1.5f);
        }
        gm.OpenDeckEdit();
        yield return new WaitForSecondsRealtime(1.5f);
        var deck = FindFirstObjectByType<DeckEditUI>(FindObjectsInactive.Include);
        string ct = deck != null && deck.collectionCountText != null ? deck.collectionCountText.text : "";
        Check(ct.Contains("MAX") && ct.Contains("AWAKENED") && ct.Contains("/" + CardMastery.TotalCards), $"N: the collection header shows MAX / AWAKENED totals ({ct})");
        var awakenedCard = deck != null ? deck.ownedCards.FirstOrDefault(c => c != null && c.gameObject.activeInHierarchy && c.Data.Awakened) : null;
        Check(awakenedCard != null, "N: an AWAKENED card is drawn with the AWAKENED visual in the collection");
        if (Arg("-qaMasteryShots", "0") == "1") { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, "mastery_collection.png")); yield return new WaitForSecondsRealtime(0.5f); }
        gm.CloseDeckEdit();
        yield return new WaitForSecondsRealtime(0.6f);
    }

    IEnumerator MasteryStats()
    {
        L("== G: AWAKENED でも通常の性能は同じ ==");
        string id = mId, k9 = K(id, 9);
        yield return V3Begin("swordsman");
        var def = CardDatabase.FindById(k9);
        string snap(Dictionary<string, float> d) => string.Join(" ", d.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value.ToString("0.####")));
        V3Reset();
        gm.ApplyCardEffectsStacked(def, 1);
        string awake = snap(V3Snap());
        bool wasAwake = CardMastery.IsAwakened(id);
        string keep = CardMastery.ExportJson();
        CardMastery.DebugResetCard(id);
        V3Reset();
        gm.ApplyCardEffectsStacked(def, 1);
        string plain = snap(V3Snap());
        PlayerPrefs.SetString(CardMastery.SaveKey, keep); CardMastery.ReloadFromPrefs();
        Check(wasAwake && awake == plain, $"G: a Lv9 card gives exactly the same stats whether AWAKENED or ★0 (atk x{pc.CardAttackFactor:F3})");
        yield return V3End();
    }

    IEnumerator MasteryCap()
    {
        L("== J: キャラカード/デッキ と Lv9 上限 ==");
        string id = mId, k9 = K(id, 9);
        CardInventory.AddCard(k9, 9, 2);
        float w0 = 0f; while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        gm.SetSelectedCharacter("swordsman");
        bool eq = gm.EquipCharacterCard(0, k9, 9);
        L($"[J] equip {k9} -> {eq} {gm.LastEquipMessage}");
        yield return BeginRun("swordsman", "wasteland_road");
        int lvStart = gm.GetAbilityRunStack(id);
        V3Pick.Invoke(gm, new object[] { k9 });
        V3Pick.Invoke(gm, new object[] { id });
        int lvAfter = gm.GetAbilityRunStack(id);
        L($"[J] AWAKENED={CardMastery.IsAwakened(id)} run ability Lv at start {lvStart}, after picking Lv9 + Lv1 again {lvAfter}; FE eligible={CardProgression.FinalEvolutionEligible(id)} maxAtStart={CardProgression.MaxAtRunStart(id)} awakenedBonus={CardProgression.FinalEvolutionAwakenedBonus(id)}");
        Check(eq && lvStart == 9 && lvAfter == 9, "J: an AWAKENED Lv9 character card is Lv9 in the run and never goes past the Lv9 cap (Mastery is not an ability stack)");
        Check(CardProgression.MaxAtRunStart(id) && CardProgression.FinalEvolutionEligible(id) && CardProgression.FinalEvolutionAwakenedBonus(id), "J: Final Evolution hook: Lv9 from the start (character card) is eligible too (2026-10-04 FINAL EVOLUTION); AWAKENED bonus is readable");
        yield return EndRun();
        gm = GameManager.Instance;
        gm.EquipCharacterCard(0, "", 0);
        // キャラカードなし: ランの中で Lv9 に届いたら資格
        yield return BeginRun("swordsman", "wasteland_road");
        for (int i = 0; i < 9; i++) V3Pick.Invoke(gm, new object[] { id });
        Check(gm.GetAbilityRunStack(id) == 9 && CardProgression.ReachedMaxInRun(id) && CardProgression.FinalEvolutionEligible(id), $"J: reaching Lv9 during the run makes it Final Evolution eligible (Lv{gm.GetAbilityRunStack(id)})");
        yield return EndRun();
        gm = GameManager.Instance;
    }
}
#endif
