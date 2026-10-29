#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// 組み立てる文(数値・名前を差し込む表示)の訳の確認(2026-10-29、訳の抜けの修正): -qaLocDyn <dir> [-qaLocDynLangs en,de,ar,ta] [-qaLocDynShots 1]
//  言語ごとに、合成(予告4種・結果5種)/ デッキの合成カードの詳細 / 全カードの能力の行(Lv1・5・9・上限超え)/ 合成カードの説明 /
//  疾走の行き先と走っている時 / リザルトの解放の行 / お知らせ / 解放の知らせ / 練習の説明 を出し、日本語が残った文を全部書き出す。
//  画面は撮る(縦横・右から左の確認は撮った画面を目で見る)。訳の計算・ゲームの動きは変えない
public partial class QaSweep
{
    IEnumerator LocDynMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();
        UnlockRules.DevUnlockAll = true; SprintRecords.DevUnlockAll = true;
        gm.AddMile(5000);
        string keep = Loc.Current;
        bool shots = Arg("-qaLocDynShots", "1") == "1";
        string tag = $"{Screen.width}x{Screen.height}";
        var summary = new StringBuilder();
        foreach (var code in Arg("-qaLocDynLangs", "en").Split(','))
        {
            Loc.Set(code, save: false);
            yield return ReloadHome();
            LocDebug.Reset();
            int probes = 0;
            var leftList = new List<string>();
            void Probe(string where, string src)
            {
                if (string.IsNullOrEmpty(src)) return;
                probes++;
                string r = Loc.Auto(src);
                // \u300c\u30fb\u300d(U+30FB)\u306f\u8a33\u3067\u3082\u4f7f\u3046\u8a18\u53f7\u306a\u306e\u3067\u65e5\u672c\u8a9e\u306b\u6570\u3048\u306a\u3044
                bool left = code != "ja" && (code.StartsWith("zh") ? System.Text.RegularExpressions.Regex.IsMatch(r, "[\u3040-\u30fa\u30fc-\u30ff]") : System.Text.RegularExpressions.Regex.IsMatch(r, "[\u3040-\u30fa\u30fc-\u30ff\u4e00-\u9fff]"));
                if (left)
                {
                    // 複数行は残った行だけを書く(どの行の型が足りないか分かるように)
                    var sl = src.Split('\n'); var rl = r.Split('\n');
                    if (sl.Length == rl.Length && sl.Length > 1) { for (int i = 0; i < sl.Length; i++) if (System.Text.RegularExpressions.Regex.IsMatch(rl[i], "[\u3040-\u30fa\u30fc-\u30ff\u4e00-\u9fff]")) leftList.Add($"{where}#{i}\t{sl[i]}\t{rl[i]}"); }
                    else leftList.Add($"{where}\t{src.Replace("\n", "\\n")}\t{r.Replace("\n", "\\n")}");
                }
            }
            // 画面に出ている文(訳した後の物)をそのまま調べる(見出しなど、元の文を渡せない所)
            void Shown(string where, string shown)
            {
                if (string.IsNullOrEmpty(shown)) return;
                probes++;
                bool left = code != "ja" && System.Text.RegularExpressions.Regex.IsMatch(shown, code.StartsWith("zh") ? "[぀-ヺー-ヿ]" : "[぀-ヺー-ヿ一-鿿]");
                if (left) leftList.Add($"{where}\t(shown)\t{shown.Replace("\n", "\\n")}");
            }
            void S(string n) { if (shots) Shot($"locdyn_{code}_{tag}_{n}"); }

            // ---- 全カードの能力の行・説明(画面を開かずに、表示に渡す文と同じものを訳す)
            foreach (var c in CardDatabase.AllCards)
            {
                if (c == null) continue;
                Probe("card.description " + c.cardId, c.description);
                foreach (int lv in new[] { 1, 5, 9, 12 }) Probe($"AbilityLine {c.cardId} Lv{lv}", CardVariant.AbilityLine(c.cardId, lv));
                if (c.effects != null) foreach (var e in c.effects) Probe("EffectPart " + c.cardId, CardVariant.EffectPart(e));
            }
            yield return null;

            // ---- 合成: 予告(同名/同名で Lv9 超え/MASTERY/異名)と結果(同名/両方成功/メインのみ/素材のみ/失敗/MASTERY)
            var ids = CardDatabase.UnlockedCards.Select(c => c.cardId).ToList();
            string A = ids.FirstOrDefault(i => i == "attack_up") ?? ids[0];
            string B = ids.FirstOrDefault(i => i != A && i == "speed_down") ?? ids.First(i => i != A);
            CardInventory.AddCard(A, 1, 8); CardInventory.AddCard(B, 1, 6); CardInventory.AddCard(A, 9, 3); CardInventory.AddCard(A, 6, 2);
            gm.OpenCardFusion(); yield return new WaitForSecondsRealtime(1.3f); yield return WaitNoTransition();
            var fu = FindFirstObjectByType<CardFusionUI>();
            if (fu != null)
            {
                string Key(string id, int lv) => CardInventory.Stacks.Where(s => s.count > 0 && CardVariant.Parse(s.cardId) != null && CardVariant.Parse(s.cardId).mainId == id && CardVariant.Parse(s.cardId).level == lv).Select(s => s.cardId).FirstOrDefault() ?? (lv == 1 ? id : null);
                string a1 = Key(A, 1), b1 = Key(B, 1), a9 = Key(A, 9), a6 = Key(A, 6);
                var pre = new List<(string n, string m, string t)> { ("same", a1, a1), ("cross", a1, b1) };
                if (a6 != null) pre.Add(("same_over9", a6, a6));
                if (a9 != null) pre.Add(("mastery", a9, a9));
                foreach (var p in pre)
                {
                    if (p.m == null || p.t == null) continue;
                    fu.DebugSelect(p.m, p.t); yield return new WaitForSecondsRealtime(0.4f);
                    Probe("fusion.preview " + p.n, CardFusionLogic.Preview(p.m, p.t));
                    Probe("fusion.detail " + p.n, fu.DebugDetailText);
                    S("fusion_preview_" + p.n); yield return null;
                }
                var res = new List<(string n, string m, string t, CardFusionLogic.ForcedOutcome? f)>
                {
                    ("same", a1, a1, null), ("both", a1, b1, CardFusionLogic.ForcedOutcome.BothSuccess), ("mainonly", a1, b1, CardFusionLogic.ForcedOutcome.MainOnly),
                    ("matonly", a1, b1, CardFusionLogic.ForcedOutcome.MaterialOnly), ("fail", a1, b1, CardFusionLogic.ForcedOutcome.BothFail),
                };
                if (a9 != null) res.Add(("mastery", a9, a9, null));
                foreach (var r in res)
                {
                    fu.DebugSelect(r.m, r.t); yield return new WaitForSecondsRealtime(0.3f);
                    CardFusionLogic.DebugForcedOutcome = r.f; fu.DebugFuse(); CardFusionLogic.DebugForcedOutcome = null;
                    yield return new WaitForSecondsRealtime(0.4f);
                    Shown("fusion.caption " + r.n, (typeof(CardFusionUI).GetField("fxCaption", NP)?.GetValue(fu) as UnityEngine.UI.Text)?.text);
                    fu.DebugSkip();
                    yield return WaitTut(() => fu.DebugInResult, 8f); yield return new WaitForSecondsRealtime(0.5f);
                    Probe("fusion.result " + r.n, fu.DebugResultText);
                    Shown("fusion.title " + r.n, (typeof(CardFusionUI).GetField("resultTitle", NP)?.GetValue(fu) as UnityEngine.UI.Text)?.text);
                    S("fusion_result_" + r.n); yield return null;
                    fu.DebugBackToList(); yield return new WaitForSecondsRealtime(0.5f);
                    // 作った合成カードを次の組で使えるように、所持を足し直す
                    CardInventory.AddCard(A, 1, 2); CardInventory.AddCard(B, 1, 2);
                    a1 = Key(A, 1); b1 = Key(B, 1);
                }
                fu.SendMessage("Close", SendMessageOptions.DontRequireReceiver);
                yield return new WaitForSecondsRealtime(1.3f);
            }
            // 合成カードの説明(作り置きの文)
            foreach (var s in CardInventory.Stacks.Where(s => CardVariant.IsVariantKey(s.cardId)).Take(12)) Probe("variant.description " + s.cardId, CardDatabase.FindById(s.cardId)?.description);

            // ---- デッキ: 合成カードの詳細
            yield return ReloadHome();
            gm.OpenDeckEdit(); yield return new WaitForSecondsRealtime(1.3f); yield return WaitNoTransition();
            var deck = FindFirstObjectByType<DeckEditUI>();
            if (deck != null)
            {
                var stacks = typeof(DeckEditUI).GetField("displayedStacks", NP)?.GetValue(deck) as List<CardInventory.Stack>;
                int vi = stacks != null ? stacks.FindIndex(s => CardVariant.IsVariantKey(s.cardId)) : -1;
                if (vi >= 0) { deck.DebugFocusOwned(vi); yield return new WaitForSecondsRealtime(0.5f); S("deck_variant_detail"); yield return null; }
                deck.Close(); yield return new WaitForSecondsRealtime(1.3f);
            }

            // ---- 疾走: 行き先 / 走っている時
            gm.OpenStageSelect(); yield return new WaitForSecondsRealtime(1.3f); yield return WaitNoTransition();
            SprintDeparturePanel.DebugOpen(true); yield return new WaitForSecondsRealtime(0.6f); S("sprint_panel"); yield return null;
            SprintDeparturePanel.DebugOpen(false); yield return new WaitForSecondsRealtime(0.3f);
            foreach (var d in SprintRecords.Destinations("wasteland_road")) Probe("sprint.reason", d.why);
            FindFirstObjectByType<StageSelectUI>()?.Close(); yield return new WaitForSecondsRealtime(1.3f);
            yield return ReloadHome();
            gm.SetSelectedCharacter("swordsman");
            gm.DepartSprint("wasteland_road", 20000);
            // 解放の知らせ(ラン中だけ出る)も、走っている画面で一緒に撮る
            var ud = UnlockDatabase.AllUnlocks.FirstOrDefault();
            yield return new WaitForSecondsRealtime(2f);
            if (ud != null) { gm.DebugShowUnlockToast(ud); Probe("unlock.toast", Loc.Src("解放条件達成: {0}", ud.displayName)); }
            yield return new WaitForSecondsRealtime(1f); S("sprint_run"); yield return null;
            yield return WaitTut(() => gm.HasStarted && SprintRunner.Instance == null, 40f);
            yield return new WaitForSecondsRealtime(1f);

            // ---- リザルトの解放の行(表示に使う一覧をそのまま作る)
            {
                var cr = new RunLedger.CommitResult { committed = true };
                cr.unlockedStages.Add("natural_cave"); cr.unlockedChars.Add("dual_blade");
                foreach (var u in UnlockDatabase.AllUnlocks.Take(3)) if (u != null) cr.unlockedOther.Add(u.displayName);
                var lcProp = typeof(GameManager).GetProperty("LastCommit");
                object keepLc = lcProp.GetValue(gm);
                lcProp.GetSetMethod(true).Invoke(gm, new object[] { cr });
                var lines = typeof(GameManager).GetMethod("ResultUpdateLines", NP).Invoke(gm, new object[] { true }) as List<string>;
                if (lines != null) foreach (var l in lines) Probe("result.update", l);
                lcProp.GetSetMethod(true).Invoke(gm, new object[] { keepLc });
            }
            yield return EndRun();

            // ---- お知らせ(デッキ上限)/ 解放の知らせ
            yield return ReloadHome();
            NoticeQueue.QaAutoAcknowledge = false;
            SaveStore.SetInt(DeckCapacityNotice.Key, 0);
            // 2026-10-29: テスト用データのデッキが満杯(12枚)だとお知らせが出ないので、一時的に減らして出す(後で戻す)
            var deckField = typeof(GameManager).GetField("deckCards", NP);
            var deckList = deckField?.GetValue(gm) as List<string>;
            var deckKeep = deckList != null ? new List<string>(deckList) : null;
            if (deckList != null && deckList.Count >= GameManager.DeckCapacity) deckList.RemoveRange(8, deckList.Count - 8);
            DeckCapacityNotice.Check(gm, true);
            Probe("notice.overflow", $"デッキの上限({GameManager.DeckCapacity}枚)を超えていた 3 枚をデッキから外しました。\nカードは所持一覧に残っています。デッキ編集で入れ替えてください。");
            Probe("notice.deck12",$"デッキに入れられるカードが {GameManager.DeckCapacity} 枚になりました(今 {gm.DeckCards.Count} 枚)。\nデッキ編集で追加できます。");
            yield return WaitTut(() => NoticeQueue.Open, 4f); yield return new WaitForSecondsRealtime(0.5f);
            Check(NoticeQueue.Open, $"locdyn {code}: the deck notice is on screen");
            S("notice_deck"); yield return null;
            NoticeQueue.ClearAll(); yield return new WaitForSecondsRealtime(0.3f);
            if (deckList != null && deckKeep != null) { deckList.Clear(); deckList.AddRange(deckKeep); }
            SaveStore.SetInt(DeckCapacityNotice.Key, 1);

            // ---- 練習(最初の説明)
            TutorialLauncher.Launch(false, "qa locdyn");
            yield return WaitTut(() => TutorialRun.Instance != null && !TutorialLauncher.Covering && TutorialRun.PanelOpen, 25f);
            yield return new WaitForSecondsRealtime(0.8f); S("tutorial"); yield return null;
            TutorialRun.Instance?.Quit("qa locdyn");
            yield return WaitTut(() => GameManager.Instance != null && !TutorialMode.Active && !TutorialLauncher.Covering, 12f);
            gm = GameManager.Instance;

            // ---- 闘技場の結果の「今回の条件」「試用ビルド」(結果に残る文)
            {
                var cfg = ArenaMode.Config; var keepBuild = new List<ArenaBuildEntry>(cfg.build); int keepMode = cfg.speedMode; float keepKmh = cfg.kmh;
                cfg.build.Clear(); cfg.build.Add(new ArenaBuildEntry { key = A, times = 3 }); cfg.build.Add(new ArenaBuildEntry { key = A, times = 1, owned = true });
                cfg.speedMode = 2; cfg.kmh = 60f;
                var lab = typeof(ArenaController).GetMethod("Label", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                var bs = typeof(ArenaController).GetMethod("BuildSummary", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (lab != null) Probe("arena.label", "今回の条件: " + (string)lab.Invoke(null, null));
                if (bs != null) Probe("arena.build", "試用ビルド: " + (string)bs.Invoke(null, null));
                cfg.build.Clear(); if (bs != null) Probe("arena.build.none", "試用ビルド: " + (string)bs.Invoke(null, null));
                cfg.build.AddRange(keepBuild); cfg.speedMode = keepMode; cfg.kmh = keepKmh;
            }

            // ---- 書き出し: 調べた文で日本語が残った物 + 画面に出た時に記録された物(LocDebug)
            var all = new HashSet<string>(leftList);
            foreach (var s in LocDebug.UntranslatedShown) all.Add("shown\t" + s.Replace("\n", "\\n") + "\t");
            foreach (var s in LocDebug.JapaneseShown) all.Add("shown(partial)\t" + s.Replace("\n", "\\n") + "\t");
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, $"locdyn_{code}.tsv"), string.Join("\n", all.OrderBy(x => x)));
            L($"[locdyn] {code}: probes {probes}, Japanese left in probes {leftList.Count}, shown untranslated {LocDebug.UntranslatedShown.Count}, partially translated {LocDebug.JapaneseShown.Count}");
            summary.Append($"{code}:{leftList.Count}+{LocDebug.UntranslatedShown.Count}+{LocDebug.JapaneseShown.Count} ");
            if (code != "ja") Check(leftList.Count == 0 && LocDebug.UntranslatedShown.Count == 0 && LocDebug.JapaneseShown.Count == 0, $"locdyn {code}: no Japanese left in the built texts ({leftList.Count} probes, {LocDebug.UntranslatedShown.Count} shown, {LocDebug.JapaneseShown.Count} partial)");
        }
        L($"[locdyn] summary {summary}");
        Loc.Set(keep, save: false);
        UnlockRules.DevUnlockAll = false; SprintRecords.DevUnlockAll = false;
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }
}
#endif
