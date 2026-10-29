#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

// 翻訳の仕上げの確認(2026-10-29)
//  -qaLocSwitch <dir> [-lsPhase 1|2] [-lsRecord <file>]
//     合成カード(前から持っていた物 = 保存の形のまま入れたキー / 新しく作った物)の説明が、言語の切り替え・画面の開き直し・
//     開いたままの切り替え・アプリの再起動(phase 2)で今の言語になるか。カードの能力/Lv/所持数が変わらないか
//  -qaLocFallback <dir> [-lfLangs de,ar]
//     組み立てた文の部品の訳が無い時に実際に何が出るか(空欄・数値の欠けが無いか)
//  -qaRtlWrap <dir> [-rwLangs ar,fa]
//     右から左の言語の折り返し: 主な画面を開き、uGUI の文字が自動で折り返していないか(= 行に分けてから並べ替えた)を数える + 撮る
public partial class QaSweep
{
    static readonly Regex JpAny = new Regex("[\u3040-\u30fa\u30fc-\u30ff\u4e00-\u9fff]");
    static bool HasJp(string s, string code) => code != "ja" && !string.IsNullOrEmpty(s) && (code.StartsWith("zh") ? Regex.IsMatch(s, "[\u3040-\u30fa\u30fc-\u30ff]") : JpAny.IsMatch(s));

    // 前から持っていた合成カード: 保存の形(OwnedCardsV1 の cardId = 能力一式のキー)はそのまま。説明は保存されない
    static readonly string[] LegacyVariantKeys =
    {
        "v2|attack_up|2|2|attack_up*1/speed_down*1",
        "v2|speed_up|3|3|speed_up*2/heart_up*1",
    };

    IEnumerator LocSwitchMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        gm = GameManager.Instance;
        string phase = Arg("-lsPhase", "1");
        string recPath = Arg("-lsRecord", System.IO.Path.Combine(outDir, "locswitch_record.txt"));
        if (phase == "1")
        {
            string langKeep = SaveStore.GetString(Loc.PrefKey, "");
            SaveProfile.Switch(true, true);
            yield return ReloadHome();
            TutorialProgress.MarkOffered();
            // 前から持っていた物(保存の形のキーを入れる)+ 新しく作る物(合成の処理で)
            foreach (var k in LegacyVariantKeys) CardInventory.AddCard(k, 1, 1);
            CardInventory.AddCard("jump_power_up", 1, 3); CardInventory.AddCard("attack_up", 1, 3);
            CardFusionLogic.DebugForcedOutcome = CardFusionLogic.ForcedOutcome.BothSuccess;
            var fr = CardFusionLogic.Execute("jump_power_up", "attack_up", out string ferr); // 前から持っていた物と違う組み合わせ
            CardFusionLogic.DebugForcedOutcome = null;
            string newKey = fr != null ? fr.resultKey : null;
            L($"[locswitch] new fused card: {newKey ?? "(failed " + ferr + ")"}");
            var keys = LegacyVariantKeys.Where(k => CardDatabase.FindById(k) != null).ToList();
            if (!string.IsNullOrEmpty(newKey)) keys.Add(newKey);
            Check(keys.Count == 3, $"locswitch: 3 fused cards to check ({keys.Count})");
            string before = InvSnapshot();
            string saved = SaveStore.GetString("OwnedCardsV1", "");
            Check(!saved.Contains("【") && !saved.Contains("[Main]") && !saved.Contains("description"), "locswitch: the saved card data holds only ids/levels/counts (no description text)");
            L($"[locswitch] saved OwnedCardsV1 (head): {(saved.Length > 220 ? saved.Substring(0, 220) + "…" : saved)}");

            var texts = new Dictionary<string, string>(); // lang|key -> 詳細の文
            string[] langs = Arg("-lsLangs", "ja,en,ar,de,ta").Split(',');
            for (int li = 0; li < langs.Length; li++)
            {
                string code = langs[li];
                Loc.Set(code, save: false);
                yield return ReloadHome();
                foreach (var key in keys)
                {
                    string a = null, b = null;
                    yield return DeckDetail(key, x => a = x, $"ls_{code}_{ShortKey(key)}_open");
                    yield return DeckDetail(key, x => b = x, null); // 閉じて開き直す
                    texts[code + "|" + key] = a;
                    Check(!string.IsNullOrEmpty(a), $"locswitch {code} {ShortKey(key)}: the detail is shown");
                    Check(!HasJp(a, code), $"locswitch {code} {ShortKey(key)}: no Japanese in the detail ({Clip(a)})");
                    Check(a == b, $"locswitch {code} {ShortKey(key)}: same text after closing and reopening");
                    if (li > 0) Check(a != texts[langs[0] + "|" + key], $"locswitch {code} {ShortKey(key)}: the detail changed from {langs[0]}");
                }
                // 合成の画面の詳細(選んだ時の説明)
                gm.OpenCardFusion(); yield return new WaitForSecondsRealtime(1.3f); yield return WaitNoTransition();
                var fu = FindFirstObjectByType<CardFusionUI>();
                if (fu != null)
                {
                    fu.DebugSelect(keys[0], keys[keys.Count - 1]); yield return new WaitForSecondsRealtime(0.4f);
                    string fd = fu.DebugDetailText;
                    Check(!HasJp(fd, code), $"locswitch {code}: no Japanese in the fusion detail ({Clip(fd)})");
                    Shot($"ls_{code}_fusion_detail"); yield return null;
                    fu.SendMessage("Close", SendMessageOptions.DontRequireReceiver); yield return new WaitForSecondsRealtime(1.3f);
                }
            }
            // 開いたまま言語を切り替える(設定の画面と同じ Loc.Set): 詳細が書き直される
            {
                Loc.Set("en", save: false); yield return ReloadHome();
                string key = keys[keys.Count - 1];
                gm.OpenDeckEdit(); yield return new WaitForSecondsRealtime(1.3f); yield return WaitNoTransition();
                var deck = FindFirstObjectByType<DeckEditUI>();
                if (deck != null && FocusStack(deck, key))
                {
                    yield return new WaitForSecondsRealtime(0.4f);
                    string en = deck.DebugDetailText;
                    Loc.Set("de", save: false); yield return new WaitForSecondsRealtime(0.5f);
                    string live = deck.DebugDetailText;
                    Shot("ls_live_switch_de"); yield return null;
                    Check(live != en && !HasJp(live, "de"), $"locswitch: switching language with the deck open rewrites the detail ({Clip(live)})");
                    Check(live == texts["de|" + key], "locswitch: the rewritten detail equals the detail opened fresh in German");
                    Loc.Set("ar", save: false); yield return new WaitForSecondsRealtime(0.5f);
                    string liveAr = deck.DebugDetailText;
                    Check(!HasJp(liveAr, "ar") && liveAr != live, "locswitch: switching to Arabic with the deck open rewrites the detail");
                    deck.Close(); yield return new WaitForSecondsRealtime(1.3f);
                }
                else Check(false, "locswitch: the fused card is in the owned list");
            }
            Check(InvSnapshot() == before, "locswitch: abilities/levels/counts are unchanged by the language changes");
            // 再起動の確認の準備: ドイツ語を選んだ状態(保存)にして記録を書く
            Loc.Set("de", save: true);
            yield return ReloadHome();
            var rec = new StringBuilder();
            rec.AppendLine("langKeep\t" + langKeep);
            rec.AppendLine("inventory\t" + before);
            foreach (var key in keys) rec.AppendLine("de\t" + key + "\t" + texts["de|" + key].Replace("\n", "\\n"));
            System.IO.File.WriteAllText(recPath, rec.ToString());
            L($"[locswitch] phase 1 done, record {recPath}");
            SaveStore.Save();
            yield break; // テスト用データのまま終わる(phase 2 が同じデータで起動する)
        }
        else
        {
            var lines = System.IO.File.ReadAllLines(recPath);
            string langKeep = lines.First(l => l.StartsWith("langKeep\t")).Substring(9);
            string inv = lines.First(l => l.StartsWith("inventory\t")).Substring(10);
            Check(Loc.Current == "de", $"locswitch restart: the chosen language is kept ({Loc.Current})");
            Check(SaveProfile.IsTest, "locswitch restart: started on the test data");
            yield return ReloadHome();
            Check(InvSnapshot() == inv, "locswitch restart: abilities/levels/counts are the same after the restart");
            foreach (var l in lines.Where(l => l.StartsWith("de\t")))
            {
                var p = l.Split('\t');
                string key = p[1], want = p[2].Replace("\\n", "\n");
                string got = null;
                yield return DeckDetail(key, x => got = x, $"ls_restart_de_{ShortKey(key)}");
                Check(got == want, $"locswitch restart {ShortKey(key)}: the German detail is the same as before the restart ({Clip(got)})");
                Check(!HasJp(got, "de"), $"locswitch restart {ShortKey(key)}: no Japanese");
            }
            // 後片付け: 言語を元に戻し、テスト用データを消して通常のデータへ
            if (string.IsNullOrEmpty(langKeep)) SaveStore.DeleteKey(Loc.PrefKey); else SaveStore.SetString(Loc.PrefKey, langKeep);
            Loc.Set(string.IsNullOrEmpty(langKeep) ? Loc.DetectDevice() : langKeep, save: !string.IsNullOrEmpty(langKeep));
            SaveProfile.Switch(false, true);
            yield return ReloadHome();
            L("[locswitch] phase 2 done");
        }
    }

    static string ShortKey(string k) => k.StartsWith("v2|") ? k.Substring(3).Replace("|", "_").Replace("*", "x").Replace("/", "+") : k;
    static string Clip(string s) => s == null ? "null" : (s.Length > 120 ? s.Substring(0, 120) + "…" : s).Replace("\n", " / ");
    static string InvSnapshot() => string.Join(";", CardInventory.Stacks.Where(s => s.count > 0).OrderBy(s => s.cardId).ThenBy(s => s.level).Select(s => $"{s.cardId}:{s.level}x{s.count}"));

    bool FocusStack(DeckEditUI deck, string key)
    {
        var stacks = typeof(DeckEditUI).GetField("displayedStacks", NP)?.GetValue(deck) as List<CardInventory.Stack>;
        int vi = stacks != null ? stacks.FindIndex(s => s.cardId == key) : -1;
        if (vi < 0) return false;
        deck.DebugFocusOwned(vi);
        return true;
    }

    IEnumerator DeckDetail(string key, System.Action<string> got, string shot)
    {
        gm.OpenDeckEdit(); yield return new WaitForSecondsRealtime(1.3f); yield return WaitNoTransition();
        var deck = FindFirstObjectByType<DeckEditUI>();
        string t = null;
        if (deck != null && FocusStack(deck, key)) { yield return new WaitForSecondsRealtime(0.4f); t = deck.DebugDetailText; if (shot != null) { Shot(shot); yield return null; } }
        got(t);
        if (deck != null) { deck.Close(); yield return new WaitForSecondsRealtime(1.3f); }
    }

    // ---------------------------------------------------------------- 部品の訳が無い時
    IEnumerator LocFallbackMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        string keepLang = Loc.Current;
        var tsv = new StringBuilder("lang\tcase\tsource\tshown\tnumbers kept\tjapanese left\n");
        // (名前, 消す訳のキー(無ければ null), 確かめる文)
        var cases = new List<(string name, string drop, string src)>
        {
            ("part missing here: inherit word", "継承成功", "メイン側(ATTACK UP Lv.1): <color=#ffd76a>継承成功</color>"),
            ("part missing here: ability label", "攻撃力", "攻撃力 +5%"),
            ("part missing here: ability label in a list", "移動速度", "攻撃力 +10% / 移動速度 -4%(固定)"),
            ("template missing here: 【主】", "【主】{0}", "【主】ATTACK UP Lv2  (攻撃力 +10%)"),
            ("template missing here: rarity", "レア度 <color=#ffd76a>{0}</color>", "合成Lv.<b>2</b>    レア度 <color=#ffd76a>★★</color>"),
            ("part with no translation anywhere", null, "メイン側(ATTACK UP Lv.1): <color=#ffd76a>未登録の結果</color>"),
            ("ability line with an unknown label", null, "ATTACK UP Lv3  (未登録の能力 +12%)"),
            ("list with one unknown part", null, "攻撃力 +10% / 未登録の効果 +3m"),
        };
        foreach (var code in Arg("-lfLangs", "de,ar,en").Split(','))
        {
            foreach (var c in cases)
            {
                Loc.Set(code, save: false);
                if (c.drop != null) Loc.DebugDropFromCurrent(c.drop);
                string r = Loc.Auto(c.src);
                var nums = Regex.Matches(c.src, @"\d+(?:[.,]\d+)?").Cast<Match>().Select(m => m.Value).ToList();
                bool numsKept = nums.All(n => r.Contains(n));
                bool jp = HasJp(r, code);
                tsv.Append($"{code}\t{c.name}\t{c.src}\t{r.Replace("\n", "\\n")}\t{numsKept}\t{jp}\n");
                L($"[fallback] {code} {c.name}: '{c.src}' -> '{r}'{(jp ? "  (Japanese shown)" : "")}");
                Check(!string.IsNullOrEmpty(r) && r.Trim().Length > 0, $"fallback {code} {c.name}: not blank");
                Check(numsKept, $"fallback {code} {c.name}: all numbers of the source are shown ({string.Join(",", nums)})");
            }
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "loc_fallback.tsv"), tsv.ToString());
        Loc.Set(keepLang, save: false);
        yield return null;
    }

    // ---------------------------------------------------------------- 右から左の折り返し
    static readonly Regex ArabicForms = new Regex("[\uFB50-\uFDFF\uFE70-\uFEFF]");
    int rtlAutoWrapped, rtlChecked;
    // uGUI: 右から左の文で、文に入れた改行の数より多く折り返している Text(= 自動の折り返しが残って行の順番が入れ替わる)を数える
    void RtlAudit(string tag)
    {
        if (!Loc.IsRtl) return;
        foreach (var t in FindObjectsByType<Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (t.horizontalOverflow != HorizontalWrapMode.Wrap || string.IsNullOrEmpty(t.text) || !ArabicForms.IsMatch(t.text)) continue;
            int explicitLines = t.text.Split('\n').Length;
            var gen = t.cachedTextGenerator;
            if (gen == null || gen.lineCount == 0) continue;
            rtlChecked++;
            if (gen.lineCount > explicitLines)
            {
                rtlAutoWrapped++;
                L($"[rtl] {tag}: auto-wrapped {gen.lineCount} lines (text has {explicitLines}) at {PathOf(t.transform)} '{Clip(t.text)}'");
            }
        }
    }
    static string PathOf(Transform t) { var s = t.name; for (var p = t.parent; p != null && s.Length < 120; p = p.parent) s = p.name + "/" + s; return s; }

    IEnumerator RtlWrapMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        gm = GameManager.Instance;
        string keepLang = Loc.Current;
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();
        UnlockRules.DevUnlockAll = true; SprintRecords.DevUnlockAll = true;
        gm.AddMile(5000);
        foreach (var k in LegacyVariantKeys) CardInventory.AddCard(k, 1, 1);
        string tag = $"{Screen.width}x{Screen.height}";
        Loc.DebugNoRtlWrap = Arg("-rwOff", "0") == "1"; // 前の作りで撮る(比べる画像)
        if (Loc.DebugNoRtlWrap) tag += "_off";
        foreach (var code in Arg("-rwLangs", "ar,fa").Split(','))
        {
            Loc.Set(code, save: false);
            yield return ReloadHome();
            rtlAutoWrapped = 0; rtlChecked = 0; int imgui0 = LocGUI.RtlFixCount, ugui0 = RtlTextWrap.FixedCount;
            void S(string n) { RtlAudit(n); Shot($"rtl_{code}_{tag}_{n}"); }
            // デッキの詳細(uGUI、合成カードの能力の行・色のタグ・数字・括弧)
            gm.OpenDeckEdit(); yield return new WaitForSecondsRealtime(1.3f); yield return WaitNoTransition();
            var deck = FindFirstObjectByType<DeckEditUI>();
            if (deck != null)
            {
                FocusStack(deck, LegacyVariantKeys[0]); yield return new WaitForSecondsRealtime(0.5f); S("deck_variant"); yield return null;
                var stacks = typeof(DeckEditUI).GetField("displayedStacks", NP)?.GetValue(deck) as List<CardInventory.Stack>;
                int gi = stacks != null ? stacks.FindIndex(s => s.cardId == "glass_cannon") : -1;
                if (gi < 0) { CardInventory.AddCard("glass_cannon", 1, 1); deck.Close(); yield return new WaitForSecondsRealtime(1.3f); gm.OpenDeckEdit(); yield return new WaitForSecondsRealtime(1.3f); deck = FindFirstObjectByType<DeckEditUI>(); FocusStack(deck, "glass_cannon"); }
                else deck.DebugFocusOwned(gi);
                yield return new WaitForSecondsRealtime(0.5f); S("deck_long_desc"); yield return null;
                deck.Close(); yield return new WaitForSecondsRealtime(1.3f);
            }
            // 合成の結果(uGUI、タグの多い複数行)
            gm.OpenCardFusion(); yield return new WaitForSecondsRealtime(1.3f); yield return WaitNoTransition();
            var fu = FindFirstObjectByType<CardFusionUI>();
            if (fu != null)
            {
                CardInventory.AddCard("attack_up", 1, 2); CardInventory.AddCard("speed_down", 1, 2);
                fu.DebugSelect("attack_up", "speed_down"); yield return new WaitForSecondsRealtime(0.4f); S("fusion_preview"); yield return null;
                CardFusionLogic.DebugForcedOutcome = CardFusionLogic.ForcedOutcome.BothSuccess; fu.DebugFuse(); CardFusionLogic.DebugForcedOutcome = null;
                yield return new WaitForSecondsRealtime(0.4f); fu.DebugSkip();
                yield return WaitTut(() => fu.DebugInResult, 8f); yield return new WaitForSecondsRealtime(0.5f); S("fusion_result"); yield return null;
                fu.DebugBackToList(); yield return new WaitForSecondsRealtime(0.5f);
                fu.SendMessage("Close", SendMessageOptions.DontRequireReceiver); yield return new WaitForSecondsRealtime(1.3f);
            }
            // お知らせ(IMGUI、数字と括弧の入った2行)
            NoticeQueue.QaAutoAcknowledge = false;
            NoticeQueue.Enqueue("qa_rtl", "デッキ上限が12枚になりました", $"デッキに入れられるカードが {GameManager.DeckCapacity} 枚になりました(今 8 枚)。\nデッキ編集で追加できます。", null);
            yield return WaitTut(() => NoticeQueue.Open, 4f); yield return new WaitForSecondsRealtime(0.5f); S("notice"); yield return null;
            NoticeQueue.ClearAll(); yield return new WaitForSecondsRealtime(0.3f);
            // 疾走の行き先(IMGUI、長い説明のスクロール)
            gm.OpenStageSelect(); yield return new WaitForSecondsRealtime(1.3f); yield return WaitNoTransition();
            SprintDeparturePanel.DebugOpen(true); yield return new WaitForSecondsRealtime(0.6f); S("sprint_panel"); yield return null;
            SprintDeparturePanel.DebugOpen(false); yield return new WaitForSecondsRealtime(0.3f);
            FindFirstObjectByType<StageSelectUI>()?.Close(); yield return new WaitForSecondsRealtime(1.3f);
            // 練習の説明(IMGUI)
            TutorialLauncher.Launch(false, "qa rtl");
            yield return WaitTut(() => TutorialRun.Instance != null && !TutorialLauncher.Covering && TutorialRun.PanelOpen, 25f);
            yield return new WaitForSecondsRealtime(0.8f); S("tutorial"); yield return null;
            TutorialRun.Instance?.Quit("qa rtl");
            yield return WaitTut(() => GameManager.Instance != null && !TutorialMode.Active && !TutorialLauncher.Covering, 12f);
            gm = GameManager.Instance;
            // ラン中のカード選択(uGUI、カードの説明)
            yield return BeginRun("swordsman", "wasteland_road");
            yield return new WaitForSecondsRealtime(1f);
            typeof(GameManager).GetMethod("TriggerLevelUpChoice", NP).Invoke(gm, null); // レベルアップの選択を出す
            yield return WaitTut(() => gm.IsRewardSequenceWaitingForSelection, 6f); yield return new WaitForSecondsRealtime(0.8f); S("card_choice"); yield return null;
            autoPickHold = false; StartCoroutine(AutoPickCards());
            yield return WaitTut(() => !gm.IsRewardSequenceRunning, 8f);
            yield return EndRun();
            yield return ReloadHome();
            L($"[rtl] {code} {tag}: uGUI texts checked {rtlChecked}, auto-wrapped (line order broken) {rtlAutoWrapped}; re-wrapped by the fix: IMGUI {LocGUI.RtlFixCount - imgui0}, uGUI {RtlTextWrap.FixedCount - ugui0}");
            Check(rtlAutoWrapped == 0, $"rtl {code} {tag}: no uGUI text wraps on its own ({rtlAutoWrapped})");
        }
        Loc.Set(keepLang, save: false);
        UnlockRules.DevUnlockAll = false; SprintRecords.DevUnlockAll = false;
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }
}
#endif
