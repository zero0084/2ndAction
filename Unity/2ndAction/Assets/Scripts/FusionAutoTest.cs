#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// カード合成改修(2026-09-26) - Editor専用の自動確認。結果は FusionAutoTest.txt。
// 実行前に所持カード/デッキ/キャラカード/MILEの保存データを退避し、終了時に必ず元へ戻す。
public class FusionAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("FusionTest", 0) != 1) return;
        EditorPrefs.SetInt("FusionTest", 0);
        new GameObject("FusionTest").AddComponent<FusionAutoTest>();
    }

    static readonly string[] BaseKeys = { CardInventory.SaveKey, "DeckCardIds", "CharacterCardSlots", "TotalOwnedMile", CardDataMigration.FormatKey, "NewUnconfirmedCardsV1", CardMastery.SaveKey };
    // キャラごとのキャラカード枠(CharacterCardSlots.<characterId>、2026-10-02)も退避する(ClearAll/装備で書き換えるため)
    readonly List<string> Keys = new List<string>();
    readonly Dictionary<string, (bool has, string s, int i)> backup = new Dictionary<string, (bool, string, int)>();
    readonly StringBuilder log = new StringBuilder();
    int failures;
    void L(string s) { log.AppendLine(s); Debug.Log("[FusionTest] " + s); }
    void Check(bool ok, string what) { if (!ok) { failures++; L("  FAIL: " + what); } else L("  ok: " + what); }

    GameManager gm;
    string A, B, C, D;               // 実在するカードID
    readonly List<string> extra = new List<string>();

    void Backup()
    {
        Keys.Clear();
        Keys.AddRange(BaseKeys);
        Keys.AddRange(SaveKeys.CharacterCardKeys());
        foreach (var k in Keys)
        {
            bool has = PlayerPrefs.HasKey(k);
            backup[k] = (has, PlayerPrefs.GetString(k, ""), PlayerPrefs.GetInt(k, 0));
        }
    }

    void Restore()
    {
        foreach (var k in Keys)
        {
            var b = backup[k];
            if (!b.has) PlayerPrefs.DeleteKey(k);
            else if (k == "TotalOwnedMile" || k == CardDataMigration.FormatKey) PlayerPrefs.SetInt(k, b.i);
            else PlayerPrefs.SetString(k, b.s);
        }
        PlayerPrefs.Save();
        ReloadAll();
    }

    // 「再起動」相当: 所持カード/デッキ/キャラカード/MILEを保存データから読み直す。
    void ReloadAll()
    {
        CardInventory.ReloadFromPrefs();
        var t = typeof(GameManager);
        t.GetMethod("LoadDeck", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        t.GetMethod("LoadMile", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        t.GetMethod("LoadCharacterCards", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
    }

    void ClearAll()
    {
        CardInventory.ResetAll();
        gm.SetDeck(new string[0]);
        for (int i = 0; i < GameManager.CharacterCardSlotCount; i++) gm.EquipCharacterCard(i, null, 1);
    }

    string Key(string main, int level, int rarity, params (string id, int stacks)[] abilities)
    {
        var v = new CardVariant { mainId = main, level = level, rarity = rarity };
        foreach (var a in abilities) v.AddAbility(a.id, a.stacks);
        return v.ToKey();
    }

    void Give(string key, int count)
    {
        CardVariant v = CardVariant.Parse(key);
        CardInventory.AddCard(key, v != null ? v.level : 1, count);
    }

    int Count(string key) { var s = CardInventory.FindByKey(key); return s != null ? s.count : 0; }
    int R(string id) => CardDatabase.FindBaseById(id).rarity;
    // v3: 攻撃のカードの合計(カードLvから毎回計算)
    float Atk() => gm.Card.Get(EffectType.AttackPct);

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1.0f);
        gm = GameManager.Instance;
        Backup();
        bool anyException = false;
        Application.LogCallback handler = (cond, trace, type) =>
        {
            if (type == LogType.Exception || type == LogType.Error) { anyException = true; L("[EXC] " + cond + "\n" + trace); }
        };
        Application.logMessageReceived += handler;
        CardFusionLogic.DebugForcedOutcome = null;
        try
        {
            // 攻撃力を持つカードをAに(効果適用の確認用)
            var all = CardDatabase.AllCards;
            foreach (var c in all) if (A == null && c.effects.Count == 1 && (c.effects[0].type == EffectType.AttackPower || c.effects[0].type == EffectType.AttackPct)) A = c.cardId; // v3: AttackPct
            foreach (var c in all) if (c.cardId != A && c.effects.Count == 1 && (c.effects[0].type == EffectType.MoveSpeed || c.effects[0].type == EffectType.SpeedPct)) { B = c.cardId; break; } // v3: SpeedPct
            foreach (var c in all) if (c.cardId != A && c.cardId != B && C == null) C = c.cardId;
            foreach (var c in all) if (c.cardId != A && c.cardId != B && c.cardId != C && D == null) D = c.cardId;
            foreach (var c in all) if (c.cardId != A && c.cardId != B && c.cardId != C && c.cardId != D && extra.Count < 10) extra.Add(c.cardId);
            L($"cards A={A}({R(A)}) B={B}({R(B)}) C={C} D={D}");

            TestMigration();
            TestSameName();
            TestSameStackAndCaps();
            TestCrossOutcomes();
            TestRefund();
            TestAbilityCap();
            TestInUse();
            TestEffects();
            TestRunChoice();
            TestProbability();
        }
        catch (System.Exception e) { failures++; L("[EXC-TEST] " + e); }

        yield return TestUi();

        CardFusionLogic.DebugForcedOutcome = null;
        Application.logMessageReceived -= handler;
        Restore();
        L($"[Restore] stacks={CardInventory.Stacks.Count} deck={gm.DeckCards.Count} mile={gm.TotalOwnedMile}");
        L("");
        L(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        L(anyException ? "SOME EXCEPTIONS LOGGED" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../FusionAutoTest.txt"), log.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(anyException || failures > 0 ? 1 : 0); else EditorApplication.isPlaying = false;
    }

    // ---- 旧データの移行 ----
    void TestMigration()
    {
        L("== Migration ==");
        string legacy = JsonUtility.ToJson(new CardInventory.SaveWrapper
        {
            stacks = new List<CardInventory.Stack>
            {
                new CardInventory.Stack { cardId = A, level = 1, count = 2 },
                new CardInventory.Stack { cardId = A, level = 3, count = 1 },
                new CardInventory.Stack { cardId = A + "+" + B, level = 1, count = 1 },
                new CardInventory.Stack { cardId = C, level = 2, count = 1 },
            }
        });
        PlayerPrefs.SetString(CardInventory.SaveKey, legacy);
        PlayerPrefs.SetString("DeckCardIds", $"{A},{A},{A},{A}+{B}");
        // 旧形式の頃はキャラカード枠が全キャラ共通(CharacterCardSlots)。2026-10-02以降は起動時に
        // 選択中のキャラの枠(CharacterCardSlots.<id>)へ移す。その時点でキャラごとの枠はまだ無い前提なので消しておく
        string ownerKey = GameManager.CharacterCardSlotsPrefix + gm.SelectedCharacterId;
        PlayerPrefs.DeleteKey(ownerKey);
        PlayerPrefs.SetString("CharacterCardSlots", $"{C}:2,,");
        PlayerPrefs.SetInt(CardDataMigration.FormatKey, 0);
        PlayerPrefs.Save();
        // 起動時と同じ順: CardDataMigration → (ReloadAll内の LoadCharacterCards で)共通枠をキャラの枠へ移す
        CardDataMigration.RunIfNeeded();
        ReloadAll();
        string a3 = Key(A, 3, R(A), (A, 3));
        string ab = CardDataMigration.LegacyToKey(A + "+" + B, 1);
        string c2 = Key(C, 2, R(C), (C, 2));
        CardVariant vab = CardVariant.Parse(ab);
        L($"  report: {CardDataMigration.LastReport}");
        Check(Count(A) == 2 && Count(a3) == 1 && Count(ab) == 1 && Count(c2) == 1, "所持カードが枚数そのままで新形式へ移行");
        Check(vab != null && vab.mainId == A && vab.StacksOf(A) == 1 && vab.StacksOf(B) == 1 && vab.AbilityCount == 2, "旧複合カード(主+副)の副能力がサブ能力として残る");
        var deck = gm.DeckCards;
        Check(deck.Count == 4 && deck[0] == A && deck[1] == A && deck[2] == a3 && deck[3] == ab, $"デッキは低いLvから順に割り当てて移行(4枚とも残る) [{string.Join(",", deck)}]");
        Check(gm.CharacterCardOwnerId == gm.SelectedCharacterId && gm.CharacterCardIds[0] == c2 && gm.GetCharacterCardLevel(0) == 2,
            $"キャラクターカードもLvと能力を保ったまま移行(選択中のキャラ {gm.SelectedCharacterId} の枠へ) [{PlayerPrefs.GetString(ownerKey)}]");
        Check(!PlayerPrefs.HasKey("CharacterCardSlots") && PlayerPrefs.GetString(ownerKey).StartsWith(c2 + ":2"), "旧共通枠は消え、キャラごとの枠に新形式で保存");
        Check(PlayerPrefs.GetInt(CardDataMigration.FormatKey) == CardDataMigration.CurrentFormat, "移行済みフラグ");
        string before = PlayerPrefs.GetString(CardInventory.SaveKey);
        CardDataMigration.RunIfNeeded();
        Check(PlayerPrefs.GetString(CardInventory.SaveKey) == before, "2回目は何もしない");
    }

    // ---- 同名 ----
    void TestSameName()
    {
        L("== SameName ==");
        ClearAll();
        string a3 = Key(A, 3, R(A), (A, 3), (C, 1));
        string a2 = Key(A, 2, R(A), (A, 2), (D, 2));
        Give(a3, 1); Give(a2, 1);
        var r = CardFusionLogic.Execute(a3, a2, out string err);
        Check(r != null && r.kind == CardFusionLogic.Kind.SameName, "同名の別個体同士を合成できる " + err);
        Check(r != null && r.result.level == 5, "同名Lv.3+Lv.2 → Lv.5");
        Check(r != null && r.result.StacksOf(A) == 5 && r.result.StacksOf(C) == 1 && r.result.StacksOf(D) == 2 && r.result.AbilityCount == 3, "両方の主能力・サブ能力を継承し、同じ能力は合算");
        ReloadAll();
        Check(Count(a3) == 0 && Count(a2) == 0 && Count(r.resultKey) == 1, "再起動後: 素材2枚が消え完成品1枚が残る");
        CardVariant back = CardVariant.Parse(r.resultKey);
        Check(back != null && back.level == 5 && back.StacksOf(A) == 5, "再起動後もLvと能力が維持");
    }

    // ---- 同一スタックの2枚/上限 ----
    void TestSameStackAndCaps()
    {
        L("== SameStack / Level cap ==");
        ClearAll();
        Give(A, 1);
        Check(CardFusionLogic.BlockReason(A, A) != null, "1枚しか無いカードを両方の枠に指定できない: " + CardFusionLogic.BlockReason(A, A));
        Give(A, 1);
        var r = CardFusionLogic.Execute(A, A, out string err);
        Check(r != null && r.result.level == 2 && Count(A) == 0, "同じ性能の2枚(別個体)から合成できる " + err);

        ClearAll();
        string a5 = Key(A, 5, R(A), (A, 5)), a4 = Key(A, 4, R(A), (A, 4)), a5b = Key(A, 5, R(A), (A, 5));
        Give(a5, 2); Give(a4, 1);
        int mileBefore = gm.TotalOwnedMile;
        // カード長期育成(2026-10-04): 同名で合計 Lv.10 は、Lv.9 MAX になり超えた1は Mastery へ(以前は合成不可だった。何も捨てない)
        int m0 = CardMastery.MasteryLevel(A) * 100 + CardMastery.MasteryProgress(A);
        var over = CardFusionLogic.Execute(a5, a5b, out string e2);
        Check(over != null && over.result.level == 9 && over.masteryGain == 1 && Count(a5) == 0 && gm.TotalOwnedMile == mileBefore
            && CardMastery.MasteryLevel(A) * 100 + CardMastery.MasteryProgress(A) > m0, "同名で合計Lv.10 → Lv.9 MAX + Mastery +1(余りを捨てない) " + e2);
        Give(a5, 1);
        var ok = CardFusionLogic.Execute(a5, a4, out string e3);
        Check(ok != null && ok.result.level == 9, "合計Lv.9は合成できる " + e3);
        Check(CardVariant.Parse(ok.resultKey).level == CardVariant.MaxLevel, "Lv.9(上限)");
        // 違うカード同士で合計が Lv.9 を超える組み合わせは今まで通り合成不可・何も消費しない
        string b5 = Key(B, 5, R(B), (B, 5));
        Give(b5, 1); Give(a5, 1);
        var cross = CardFusionLogic.Execute(a5, b5, out string e4);
        Check(cross == null && Count(a5) == 1 && Count(b5) == 1, "異名で合計Lv.10は合成不可・何も消費しない: " + e4);
    }

    // ---- 異名4通り ----
    void TestCrossOutcomes()
    {
        L("== Cross outcomes ==");
        // 2026-09-28改訂: 片側だけ成功した時は成功側のLvだけ(メインLv.3+素材Lv.2 → 両方5/メインのみ3/素材のみ2)
        string main = Key(A, 3, R(A), (A, 3), (C, 1));
        string mat = Key(B, 2, R(B), (B, 2), (D, 2));
        {
            string pv = CardFusionLogic.Preview(main, mat);
            Check(pv.Contains("両側成功: Lv.3 + Lv.2 → <b>Lv.5</b>") && pv.Contains("メインのみ成功: <b>Lv.3</b>") && pv.Contains("素材のみ成功: <b>Lv.2</b>") && pv.Contains($"{R(A) * 3 * 50 + R(B) * 2 * 50} MILE"),
                "合成前の予告: 両側成功Lv.5/メインのみLv.3/素材のみLv.2と還元額を表示");
        }
        foreach (CardFusionLogic.ForcedOutcome f in System.Enum.GetValues(typeof(CardFusionLogic.ForcedOutcome)))
        {
            ClearAll();
            Give(main, 1); Give(mat, 1);
            int mile0 = gm.TotalOwnedMile;
            CardFusionLogic.DebugForcedOutcome = f;
            var r = CardFusionLogic.Execute(main, mat, out string err);
            CardFusionLogic.DebugForcedOutcome = null;
            ReloadAll();
            if (r == null) { Check(false, $"{f}: 実行できない {err}"); continue; }
            CardVariant saved = r.resultKey != null ? CardVariant.Parse(r.resultKey) : null;
            switch (f)
            {
                case CardFusionLogic.ForcedOutcome.BothSuccess:
                    Check(saved != null && saved.mainId == A && saved.level == 5 && saved.StacksOf(A) == 3 && saved.StacksOf(C) == 1 && saved.StacksOf(B) == 2 && saved.StacksOf(D) == 2 && saved.AbilityCount == 4, "両方成功: 両側の能力一式・Lv.5");
                    Check(saved != null && saved.rarity == Mathf.Min(5, Mathf.Max(R(A), R(B)) + 1), "両方成功: レア度=高い方+1");
                    break;
                case CardFusionLogic.ForcedOutcome.MainOnly:
                    Check(saved != null && saved.mainId == A && saved.level == 3 && saved.AbilityCount == 2 && saved.StacksOf(A) == 3 && saved.StacksOf(C) == 1 && saved.StacksOf(B) == 0 && saved.StacksOf(D) == 0, "メインのみ: メイン側の能力一式と強化量だけ(素材側が混入しない)・Lv.3(素材のLvを加算しない)");
                    Check(r.result.level == 3, "メインのみ: リザルトの完成Lvも3(保存データと一致)");
                    break;
                case CardFusionLogic.ForcedOutcome.MaterialOnly:
                    Check(saved != null && saved.mainId == B && saved.level == 2 && saved.AbilityCount == 2 && saved.StacksOf(B) == 2 && saved.StacksOf(D) == 2 && saved.StacksOf(A) == 0 && saved.StacksOf(C) == 0 && saved.Main.id == B, "素材のみ: 素材側の能力一式と強化量だけ・主能力も素材側・Lv.2(メインのLvを加算しない)");
                    Check(r.result.level == 2, "素材のみ: リザルトの完成Lvも2(保存データと一致)");
                    Check(CardDatabase.FindById(r.resultKey).cardName == CardDatabase.FindBaseById(B).cardName, "素材のみ: 名前も素材側の主能力に合わせる");
                    break;
                case CardFusionLogic.ForcedOutcome.BothFail:
                    Check(r.resultKey == null && CardInventory.Stacks.Count == 0, "両方失敗: 完成カードなし・2枚とも消費");
                    Check(gm.TotalOwnedMile == mile0 + r.refundMile && r.refundMile == R(A) * 3 * 50 + R(B) * 2 * 50, $"両方失敗: MILE還元 {r.refundMile}(保存後も一致)");
                    break;
            }
            // メインのみ成功の完成品はメインと同じ性能(同じキー)になるので、「所持の合計が完成品1枚だけ」で確認する
            int total = 0; foreach (var st in CardInventory.Stacks) total += st.count;
            if (saved != null) Check(Count(r.resultKey) == 1 && total == 1 && gm.TotalOwnedMile == mile0, $"{f}: 保存データでも2枚消費・完成品1枚だけが残る・MILE変化なし (所持{total}枚)");
        }

        // 異名でも入力2枚の合計がLv.10以上なら、片側成功の可能性があっても合成不可(何も消費しない)
        ClearAll();
        string m5 = Key(A, 5, R(A), (A, 5)), s5 = Key(B, 5, R(B), (B, 5));
        Give(m5, 1); Give(s5, 1);
        int mileB = gm.TotalOwnedMile;
        var blocked = CardFusionLogic.Execute(m5, s5, out string be);
        Check(blocked == null && Count(m5) == 1 && Count(s5) == 1 && gm.TotalOwnedMile == mileB, "異名Lv.5+Lv.5(合計10)は片側成功でも合成不可・消費なし: " + be);
    }

    void TestRefund()
    {
        L("== Refund formula ==");
        var a = new CardVariant { mainId = A, level = 3, rarity = 2 }; a.AddAbility(A, 3);
        var b = new CardVariant { mainId = B, level = 2, rarity = 1 }; b.AddAbility(B, 2);
        Check(CardFusionLogic.RefundFor(a, b) == 400, "レア度2・Lv.3 + レア度1・Lv.2 → 400 MILE");
    }

    void TestAbilityCap()
    {
        L("== Ability cap ==");
        ClearAll();
        var m = new CardVariant { mainId = A, level = 1, rarity = R(A) }; m.AddAbility(A, 1);
        for (int i = 0; i < 4; i++) m.AddAbility(extra[i], 1);
        var s = new CardVariant { mainId = B, level = 1, rarity = R(B) }; s.AddAbility(B, 1);
        for (int i = 4; i < 7; i++) s.AddAbility(extra[i], 1);
        string mk = m.ToKey(), sk = s.ToKey();
        Give(mk, 1); Give(sk, 1);
        CardFusionLogic.DebugForcedOutcome = CardFusionLogic.ForcedOutcome.BothSuccess;
        var r = CardFusionLogic.Execute(mk, sk, out string err);
        CardFusionLogic.DebugForcedOutcome = null;
        ReloadAll();
        Check(r != null && CardVariant.Parse(r.resultKey).AbilityCount == 9, "サブ能力を持つカード同士で9能力まで保持(保存後も) " + err);
        // 10種類になる組み合わせは合成不可
        var m2 = m.Clone(); m2.AddAbility(extra[7], 1);
        string m2k = m2.ToKey();
        Give(m2k, 1); Give(sk, 1);
        string block = CardFusionLogic.BlockReason(m2k, sk);
        Check(block != null && block.Contains("能力"), "10種類になる場合は合成不可: " + block);
    }

    void TestInUse()
    {
        L("== In use ==");
        ClearAll();
        string k = Key(A, 2, R(A), (A, 2));
        Give(k, 1); Give(B, 1);
        gm.AddToDeck(k);
        string reason = CardFusionLogic.LockReason(k);
        Check(reason != null && reason.Contains("デッキ"), "デッキで使用中のカードは理由付きで選べない: " + reason);
        Check(CardFusionLogic.Execute(k, B, out _) == null && Count(k) == 1, "使用中カードは合成で消費されない");
        gm.SetDeck(new string[0]);
        gm.EquipCharacterCard(0, k, 2);
        reason = CardFusionLogic.LockReason(k);
        Check(reason != null && reason.Contains("キャラクター"), "キャラカード装備中も理由付きで選べない: " + reason);
        gm.EquipCharacterCard(0, null, 1);
        Check(CardFusionLogic.LockReason(B) == null, "使用中でないカードには理由を出さない");
    }

    // ---- 実際の効果適用 ----
    void TestEffects()
    {
        L("== Effects ==");
        var pc = PlayerController.Instance;
        // カードバランス v3(2026-10-03 以降): 攻撃のカードは AttackPct(カードLvから毎回計算する合計 GameManager.Card)。
        // 2026-10-04 に v3 の値へ合わせた(以前は固定値の AttackPower を見ていたので、v3 以降このテストは動いていなかった)
        float per = CardDatabase.FindBaseById(A).effects[0].value;
        string k = Key(A, 5, R(A), (A, 3), (B, 2));
        CardDefinition def = CardDatabase.FindById(k);
        int expectedEffects = CardDatabase.FindBaseById(A).effects.Count * 3 + CardDatabase.FindBaseById(B).effects.Count * 2;
        Check(def != null && def.effects.Count == expectedEffects, $"合成カードの効果=各能力の効果×強化量({def?.effects.Count}/{expectedEffects})。合成Lv(5)は掛けない");
        float ap0 = Atk();
        gm.ApplyCardEffectsStacked(def, 1);
        Check(Mathf.Abs(Atk() - ap0 - per * 3) < 1e-4f, $"取得時: 主能力が強化量ぶん適用(攻撃 +{Atk() - ap0:0.###}、期待 +{per * 3:0.###})");
        Check(def.description.Contains(CardDatabase.FindBaseById(B).cardName), "説明文(デッキ編集/ゲーム中の選出)に全サブ能力が出る");
        // キャラカード装備: 合成カードは1回だけ適用(合成Lvで掛けない)
        ClearAll();
        Give(k, 1);
        gm.EquipCharacterCard(0, k, 5);
        typeof(GameManager).GetMethod("ResetCardStatsForRun", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, new object[] { CharacterDatabase.FindById(gm.SelectedCharacterId) });
        ap0 = Atk();
        typeof(GameManager).GetMethod("ApplyCharacterCardEffects", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        Check(Mathf.Abs(Atk() - ap0 - per * 3) < 1e-4f, $"キャラカード: 合成Lvと強化量の二重適用なし(攻撃 +{Atk() - ap0:0.###}、期待 +{per * 3:0.###})");
        gm.EquipCharacterCard(0, null, 1);
    }

    // デッキに入れた合成カードが、ゲーム中の選出説明で全能力を見せ、取得時に強化量どおり適用されるか
    void TestRunChoice()
    {
        L("== Deck / run pick ==");
        ClearAll();
        var pc = PlayerController.Instance;
        float per = CardDatabase.FindBaseById(A).effects[0].value;
        string k = Key(A, 4, R(A), (A, 3), (D, 1));
        Give(k, 1);
        Check(gm.AddToDeck(k) && gm.DeckCards.Count == 1 && gm.DeckCards[0] == k, "合成カードをデッキへ入れられる");
        Check(!gm.AddToDeck(k), "所持枚数を超えてデッキへ入らない");
        ReloadAll();
        Check(gm.DeckCards.Count == 1 && gm.DeckCards[0] == k, "再起動後もデッキの合成カードが残る");
        CardDefinition def = CardDatabase.FindById(k);
        var t = typeof(GameManager);
        var data = (RewardCardData)t.GetMethod("MakeChoiceCardData", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, new object[] { def });
        Check(data.LevelLine.Contains("合成Lv.4") && data.Description.Contains(CardDatabase.FindBaseById(D).cardName), $"選出カードの説明に合成Lvと全サブ能力 [{data.LevelLine}]");
        t.GetMethod("ResetCardStatsForRun", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, new object[] { CharacterDatabase.FindById(gm.SelectedCharacterId) });
        float ap0 = Atk();
        int hist0 = gm.UpgradeCount;
        t.GetMethod("ApplyUpgradeByCardId", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, new object[] { k });
        Check(Mathf.Abs(Atk() - ap0 - per * 3) < 1e-4f && gm.UpgradeCount == hist0 + 1, $"Run中に取得: 主能力が強化量×3で1回だけ適用(攻撃 +{Atk() - ap0:0.###})");
        gm.SetDeck(new string[0]);
    }

    void TestProbability()
    {
        L("== Probability (通常の確率) ==");
        ClearAll();
        int n = 400;
        Give(A, n); Give(B, n);
        int mainOk = 0, matOk = 0;
        for (int i = 0; i < n; i++)
        {
            var r = CardFusionLogic.Execute(A, B, out _);
            if (r == null) break;
            if (r.mainInherited) mainOk++;
            if (r.materialInherited) matOk++;
            // 完成品は次の試行の邪魔にならないよう捨てる
            if (r.resultKey != null) CardInventory.RemoveCard(r.resultKey, r.result.level, 1);
        }
        float pm = mainOk / (float)n, pmat = matOk / (float)n;
        L($"  main {pm:P1} material {pmat:P1} (n={n})");
        Check(pm > 0.42f && pm < 0.58f && pmat > 0.18f && pmat < 0.32f, "メイン50%・素材25%(独立)に近い");
    }

    // ---- 画面 ----
    IEnumerator TestUi()
    {
        L("== UI ==");
        ClearAll();
        foreach (var c in CardDatabase.AllCards) Give(c.cardId, 1);
        string a2 = Key(A, 2, R(A), (A, 2));
        Give(A, 1); Give(a2, 1);
        var ui = gm.cardFusionUI;
        gm.OpenCardFusion();
        float t = 0f;
        while (t < 3f && (ui == null || !ui.root.activeInHierarchy)) { t += Time.unscaledDeltaTime; yield return null; }
        yield return new WaitForSecondsRealtime(0.8f);
        int rows = ui.DebugVisibleRows();
        Check(rows >= 2, $"一覧が2段以上完全に見える(見えている段 {rows})");

        // 同じ性能2枚(Aは所持2)を両枠へ
        ui.DebugSelect(null, null);
        ui.DebugSetActiveSlot(0);
        ui.DebugTapCell(A);
        ui.DebugTapCell(A);
        Check(ui.MainKey == A && ui.MaterialKey == A, "所持2枚の同一性能カードから別々の2枚を選べる");
        ui.DebugSelect(null, null);
        ui.DebugSetActiveSlot(0);
        ui.DebugTapCell(B);
        ui.DebugTapCell(B);
        Check(ui.MainKey == B && ui.MaterialKey == null && ui.DebugStatusText.Length > 0, "1枚しかないカードは2枠目に入らず理由を表示: " + ui.DebugStatusText);

        // 異名(両方成功固定)を画面から実行 → 演出中の連打・スキップ → リザルト → 続けて合成
        ui.DebugSelect(a2, C);
        Check(ui.DebugDetailText.Contains("異名合成") && ui.DebugDetailText.Contains("50%") && ui.DebugDetailText.Contains("25%") && ui.DebugDetailText.Contains("消費"), "合成前: 異名の区別・確率・消費の説明");
        int stacksBefore = CardInventory.Stacks.Count;
        CardFusionLogic.DebugForcedOutcome = CardFusionLogic.ForcedOutcome.BothSuccess;
        ui.DebugFuse(); ui.DebugFuse(); ui.DebugFuse();
        CardFusionLogic.DebugForcedOutcome = null;
        yield return new WaitForSecondsRealtime(0.3f);
        ui.DebugFuse();
        ui.DebugSkip();
        // batchmodeではフレームのdeltaTimeと実時間がずれるので、実時間で待つ(全失敗の経路と同じ)。
        float wall1 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - wall1 < 12f && !ui.IsShowingResult) yield return null;
        var res = CardFusionLogic.LastResult;
        Check(ui.IsShowingResult && res != null && res.kind == CardFusionLogic.Kind.CrossBoth, "スキップしてもリザルトが確認できる");
        Check(Count(a2) == 0 && Count(C) == 0 && Count(res.resultKey) == 1, "連打しても1回分だけ消費・完成品1枚");
        Check(ui.DebugResultText.Contains("NEW") && ui.DebugResultText.Contains("継承成功"), "リザルトに継承成否と新しい能力(NEW)");
        ui.DebugContinue();
        yield return null;
        Check(ui.MainKey == res.resultKey && ui.MaterialKey == null, "続けて合成: 完成カードがメイン、素材枠は空");

        // 2026-09-28改訂: 異名メインLv.3+素材Lv.2 → メインのみ成功はLv.3(画面の予告・リザルト・保存・続けて合成が一致)
        string m3 = Key(A, 3, R(A), (A, 3), (C, 1)), s2 = Key(D, 2, R(D), (D, 2), (B, 1));
        Give(m3, 1); Give(s2, 1);
        ui.DebugSelect(m3, s2);
        string detail = ui.DebugDetailText;
        Check(detail.Contains("両側成功: Lv.3 + Lv.2 → <b>Lv.5</b>") && detail.Contains("メインのみ成功: <b>Lv.3</b>") && detail.Contains("素材のみ成功: <b>Lv.2</b>") && detail.Contains("MILE"),
            "画面の予告: 両側成功Lv.5/メインのみLv.3/素材のみLv.2/全失敗の還元額");
        CardFusionLogic.DebugForcedOutcome = CardFusionLogic.ForcedOutcome.MainOnly;
        ui.DebugFuse(); ui.DebugFuse();
        CardFusionLogic.DebugForcedOutcome = null;
        yield return new WaitForSecondsRealtime(0.3f);
        ui.DebugFuse();
        ui.DebugSkip();
        float wall2 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - wall2 < 12f && !ui.IsShowingResult) yield return null;
        var mo = CardFusionLogic.LastResult;
        CardVariant moSaved = mo != null && mo.resultKey != null ? CardVariant.Parse(mo.resultKey) : null;
        Check(ui.IsShowingResult && mo != null && mo.kind == CardFusionLogic.Kind.CrossMainOnly && moSaved != null && moSaved.level == 3
              && moSaved.StacksOf(D) == 0 && moSaved.StacksOf(B) == 0 && moSaved.StacksOf(A) == 3 && moSaved.StacksOf(C) == 1,
            "画面から実行: メインのみ成功はLv.3・メイン側の能力と強化量だけ");
        Check(ui.DebugResultText.Contains("合成Lv.<b>3</b>") && ui.DebugResultText.Contains("メイン側のLvのみ") && ui.DebugResultText.Contains("継承失敗"),
            "リザルト: 完成Lv.3と内訳・継承成否が保存データと一致: " + ui.DebugResultText.Replace('\n', '|'));
        Check(Count(s2) == 0 && Count(mo.resultKey) == 1, "連打・スキップしても2枚を1回だけ消費し、完成品は1枚");
        ui.DebugContinue();
        yield return null;
        Check(ui.MainKey == mo.resultKey && CardVariant.Parse(ui.MainKey).level == 3, "続けて合成: 修正後のLv.3のカードがメインに入る");

        // 全失敗の画面
        ui.DebugSelect(A, B);
        CardFusionLogic.DebugForcedOutcome = CardFusionLogic.ForcedOutcome.BothFail;
        int mile0 = gm.TotalOwnedMile;
        ui.DebugFuse();
        CardFusionLogic.DebugForcedOutcome = null;
        // batchmodeではフレームのdeltaTimeと実時間がずれるので、実時間で待つ。
        float wallStart = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - wallStart < 12f && !ui.IsShowingResult) yield return null;
        t = Time.realtimeSinceStartup - wallStart;
        L($"  fail-path: waited {t:F2}s presenting={ui.IsPresenting} result={ui.IsShowingResult} text={ui.DebugResultText.Replace('\n', '|')}");
        Check(ui.IsShowingResult && ui.DebugResultText.Contains("還元") && gm.TotalOwnedMile > mile0, $"全失敗: 演出後に還元MILEと更新後MILEを表示 (+{gm.TotalOwnedMile - mile0})");
        ui.DebugBackToList();
        ui.Close();
        yield return new WaitForSecondsRealtime(0.8f);
    }
}

public static class FusionTestMenu
{
    [MenuItem("Tools/OneMoreMile/Fusion Test (batch)")]
    public static void RunBatch()
    {
        EditorPrefs.SetInt("FusionTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif
