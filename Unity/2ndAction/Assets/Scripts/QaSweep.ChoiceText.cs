#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// 3択(LEVEL UP / BOSS REWARD)のカード名と説明の表示の確認(2026-11-06、実機: 縦画面の FINAL EVOLUTION 3枚で2・3段目の名前/説明が空)。
//  -qaChoiceText <dir> [-ctRounds 8](段階ごとの回数) [-ctDist 12000](走った距離) [-ctFontStress 1](文字の絵の作り直しの負荷)
//                [-ctNoRepair 1](表示の自動修復を切り、直す前の不具合を数える) [-ctShots 2](各段階の最初の何回を撮るか)
//  横(1920x1080)と縦(608x1080)は窓の大きさ(-screen-width/-screen-height)で決める。
//  段階: N 通常のカードだけ / M 通常2+FINAL EVOLUTION 1 / F FINAL EVOLUTION 3枚(デッキのカードを全部 Lv9。実機と同じ FLAME BLADE / JUDGMENT BOLT / CONDUCTOR)
//  回し方(順に): 0 閉じ終わってから次 / 1 決定の直後に次を起こす(閉じる演出の途中) / 2 LEVEL UP と BOSS REWARD を同時(片方は後回し→続けて出る) / 3 全候補を順に選び直してから決定
//  毎回: 各枠の名前/説明が空でなく候補と一致(RewardCardSequence.ValidateChoiceDisplay)、画面の画素で文字が描かれているか、選んだ候補が実際に取得されたか
public partial class QaSweep
{
    int ctOpened, ctPixelFail, ctStateFail, ctApplyFail, ctRebuilds, ctMismatchLogs;
    float ctMinFrac = 1f;
    string ctTag = "";
    int ctShotsLeft;
    static readonly string[] CtFeCards = { "flame_blade", "judgment_bolt", "conductor" };

    RewardCardSequence CtSeq => gm != null ? gm.rewardCardSequence : null;

    IEnumerator ChoiceTextMode()
    {
        Application.targetFrameRate = 60;
        autoPickHold = true;
        int rounds = int.Parse(Arg("-ctRounds", "8"));
        float dist = float.Parse(Arg("-ctDist", "12000"));
        bool stress = Arg("-ctFontStress", "1") == "1";
        RewardCardSequence.QaNoRepair = Arg("-ctNoRepair", "0") == "1";
        int shotsPer = int.Parse(Arg("-ctShots", "2"));
        ctTag = $"{Screen.width}x{Screen.height}";
        Font.textureRebuilt += f => ctRebuilds++;
        Application.logMessageReceived += (c, tr, type) => { if (c.StartsWith("[ChoiceText] display mismatch")) { ctMismatchLogs++; if (ctMismatchLogs <= 20) L("[ct] product check: " + c.Substring(0, Mathf.Min(900, c.Length))); } };
        L($"[ct] screen {ctTag} portrait={PortraitRunView.IsPortraitScreen} rounds/phase={rounds} dist={dist} fontStress={stress} noRepair={RewardCardSequence.QaNoRepair} lang={Loc.Current}");

        yield return V3Begin("swordsman");
        FeInvincible(true);
        BossManager.SuppressGates = true;
        WarpTo(dist); RunLedger.DevClearDebug();
        yield return new WaitForSecondsRealtime(1.5f);
        var deck = (GetPrivate(gm, "deckCards") as List<string>) ?? new List<string>();
        L($"[ct] deck ({deck.Count}): {string.Join(",", deck)}  d={gm.MaxDistance:F0}");

        // N: 通常のカードだけ
        ctShotsLeft = shotsPer;
        yield return CtPhase("N", rounds, stress);

        // M: FINAL EVOLUTION を READY にする(デッキの外の3枚を Lv9 → 資格 → READY)。デッキの通常カードはまだ選べる → 通常2+FE1
        foreach (var id in CtFeCards) yield return FeLv9(id);
        yield return null;
        foreach (var id in CtFeCards) FeReadyNow(id);
        yield return null;
        L($"[ct] FE ready: {string.Join(",", CtFeCards.Select(id => id + ":" + FinalEvolution.StageOf(id)))} candidates={FinalEvolution.CandidateCount}");
        ctShotsLeft = shotsPer;
        yield return CtPhase("M", rounds, stress);

        // F: デッキのカードを全部 Lv9 → 通常の候補が無い → FINAL EVOLUTION だけで3枚(実機と同じ形)
        foreach (var id in deck.Distinct())
        {
            var c = CardDatabase.FindById(id);
            for (int k = 0; k < 12 && c != null && gm.CanStillPick(c); k++) V3Pick.Invoke(gm, new object[] { id });
        }
        yield return null; yield return null;
        foreach (var s in FinalEvolution.Export()) if (s.eligible && !CtFeCards.Contains(s.id)) FinalEvolution.DebugSetEligibleAt(s.id, gm.MaxDistance + 1e6f); // 実機と同じ3枚に絞る(他の Lv9 はまだ READY にしない)
        foreach (var id in CtFeCards) FeReadyNow(id);
        yield return null;
        L($"[ct] deck all Lv9; FE candidates={FinalEvolution.CandidateCount}");
        ctShotsLeft = shotsPer;
        yield return CtPhase("F", rounds, stress);

        L($"[ct] SUMMARY screen={ctTag} choicesOpened={ctOpened} stateFail={ctStateFail} pixelFail={ctPixelFail} applyFail={ctApplyFail} minTextPixelFrac={ctMinFrac:P2} fontRebuilds={ctRebuilds} productMismatchLogs={ctMismatchLogs} productRepairs={RewardCardSequence.ChoiceTextRepairs}");
        Check(ctOpened >= rounds * 3, $"choices opened {ctOpened} >= {rounds * 3}");
        Check(RewardCardSequence.ChoiceTextRepairs == 0 || !RewardCardSequence.QaNoRepair, "no-repair run");
        if (RewardCardSequence.ChoiceTextRepairs > 0) Warn($"product display check repaired {RewardCardSequence.ChoiceTextRepairs} time(s): {RewardCardSequence.LastChoiceTextProblem}");
        RewardCardSequence.QaNoRepair = false;
        autoPickHold = false;
        yield return V3End();
    }

    IEnumerator CtPhase(string phase, int rounds, bool stress)
    {
        for (int r = 0; r < rounds; r++)
        {
            int variant = r % 4;
            bool boss = (r / 2) % 2 == 1;
            string label = $"{phase}{r:00}_v{variant}_{(boss ? "boss" : "lvup")}";
            if (!(gm.IsRewardSequenceRunning || gm.DebugChoiceOpen))
            {
                CtTrigger(boss);
                if (variant == 2) CtTrigger(!boss); // 同時: 片方は後回し → 閉じた後に続けて出る
            }
            else if (variant == 2) CtTrigger(!boss); // 前の回の続き(決定直後に起こした3択)が開いている: もう1つを後回しで重ねる
            if (stress) { yield return new WaitForSecondsRealtime(0.15f); CtFontStress(); } // めくる途中で文字の絵を作り直させる
            yield return CtWaitOpen(10f);
            if (!gm.IsRewardSequenceWaitingForSelection) { Check(false, $"{label}: choice did not open (running={gm.IsRewardSequenceRunning} pending={gm.DebugChoiceOpen} step={RewardCardSequence.DebugStep})"); yield return CtDrain(); continue; }
            yield return CtHandleOpen(label, r, variant, stress);
            if (variant == 1)
            {
                // 決定の直後(閉じる演出の途中)に次を起こす
                float w = 0f;
                while (gm.DebugChoiceOpen && w < 6f) { yield return null; w += Time.unscaledDeltaTime; }
                CtTrigger(!boss);
                continue; // 次の回はこの3択を扱う(開いていれば触発しない)
            }
            if (variant == 2)
            {
                // 後回しになった方が続けて出る
                yield return CtWaitResolved();
                yield return CtWaitOpen(10f);
                if (gm.IsRewardSequenceWaitingForSelection) yield return CtHandleOpen(label + "_2nd", r + 1, 0, stress);
                else Check(false, $"{label}: the deferred second choice did not open");
            }
            yield return CtWaitResolved();
            yield return CtDrain();
        }
        yield return CtDrain();
    }

    void CtTrigger(bool boss)
    {
        if (boss) gm.TriggerBossRewardChoice(); else gm.DebugTriggerLevelUp();
    }

    IEnumerator CtWaitOpen(float timeout)
    {
        float w = 0f;
        while (!(gm.IsRewardSequenceWaitingForSelection && CtSeq != null && CtSeq.QaCardCount > 0) && w < timeout) { yield return null; w += Time.unscaledDeltaTime; }
    }

    IEnumerator CtWaitResolved()
    {
        float w = 0f;
        while ((gm.IsRewardSequenceWaitingForSelection || gm.DebugChoiceOpen) && w < 8f) { yield return null; w += Time.unscaledDeltaTime; }
    }

    // 残った3択(後回しの分など)を全部片付ける(通常の自動選択と同じ)
    IEnumerator CtDrain()
    {
        float idle = 0f, w = 0f;
        while (idle < 1.2f && w < 30f)
        {
            if (gm.IsRewardSequenceWaitingForSelection) { idle = 0f; yield return CtHandleOpen("drain", 0, 0, false); }
            else if (gm.IsRewardSequenceRunning || gm.DebugChoiceOpen || gm.BossRewardDeferredPending) idle = 0f;
            else idle += Time.unscaledDeltaTime;
            yield return null; w += Time.unscaledDeltaTime;
        }
    }

    IEnumerator CtHandleOpen(string label, int r, int variant, bool stress)
    {
        ctOpened++;
        var seq = CtSeq;
        int n = seq.QaCardCount;
        var ids = gm.DebugPendingChoiceIds;
        L($"[ct] {label} open #{seq.ChoiceSerial} n={n} portrait={seq.QaPortrait} ids=[{string.Join(",", Enumerable.Range(0, n).Select(i => seq.QaData(i).CardId))}] pending=[{string.Join(",", ids)}]");
        Check(ids.Length == n, $"{label}: candidates {ids.Length} == shown {n}");
        for (int i = 0; i < n && i < ids.Length; i++) Check(ids[i] == seq.QaData(i).CardId, $"{label}: slot {i} shows the candidate ({seq.QaData(i).CardId} vs {ids[i]})");
        bool shoot = ctShotsLeft > 0 || label.StartsWith("F");
        if (ctShotsLeft > 0) ctShotsLeft--;
        yield return CtVerify(label + "_open", shoot);
        if (stress)
        {
            CtFontStress(); // 選択待ちの間に文字の絵が作り直された(IMGUI 等の別の文字が新しい字を求めた)場合
            yield return CtVerify(label + "_stress", false);
            // アプリが裏から戻った(Android の文字の絵の作り直し対策の経路)
            seq.SendMessage("OnApplicationPause", false, SendMessageOptions.DontRequireReceiver);
            seq.SendMessage("OnApplicationFocus", true, SendMessageOptions.DontRequireReceiver);
            yield return CtVerify(label + "_resume", false);
        }
        if (variant == 3)
        {
            for (int i = 0; i < n; i++)
            {
                seq.OnCardClicked(i);
                yield return new WaitForSecondsRealtime(0.25f);
                if (seq.QaHighlighted != i) { yield return new WaitForSecondsRealtime(0.1f); }
                yield return CtVerify($"{label}_hl{i}", shoot && i == n - 1);
            }
        }
        // 決定(選んだ枠の候補が実際に取得されるか)
        int idx = n > 0 ? r % n : 0;
        var want = seq.QaData(idx);
        string ability = FinalEvolution.AbilityOfChoice(want.CardId);
        int before = ability != null ? FinalEvolution.Uses(ability) : gm.GetRunPickCount(want.CardId);
        if (seq.QaHighlighted != idx) { seq.OnCardClicked(idx); yield return new WaitForSecondsRealtime(0.25f); if (variant != 3) yield return CtVerify($"{label}_sel{idx}", false); }
        seq.OnCardClicked(idx);
        // 決定の確定まで(実時間で測る。文字の絵の作り直しで1フレームが長いと、2回目のタップが二重発火よけ(0.2秒)に吸われることがある → 選び直す)
        float t0 = Time.realtimeSinceStartup;
        while (gm.DebugChoiceOpen && Time.realtimeSinceStartup - t0 < 12f)
        {
            yield return new WaitForSecondsRealtime(0.4f);
            if (seq.IsWaitingForSelection && gm.DebugChoiceOpen) { L($"[ct] {label}: confirm tap not taken yet (hl={seq.QaHighlighted}) -> tap again"); if (seq.QaHighlighted != idx) { seq.OnCardClicked(idx); yield return new WaitForSecondsRealtime(0.3f); } seq.OnCardClicked(idx); }
        }
        int after = ability != null ? FinalEvolution.Uses(ability) : gm.GetRunPickCount(want.CardId);
        bool ok = after == before + 1 || (ability == null && !gm.CanStillPick(CardDatabase.FindById(want.CardId)) && after >= before);
        if (!ok) ctApplyFail++;
        Check(ok, $"{label}: picked slot {idx} '{want.Title}' ({want.CardId}) was applied ({before}->{after})");
    }

    // 画面の状態 + 画素
    IEnumerator CtVerify(string label, bool shot)
    {
        yield return null; yield return null; // 製品側の確かめ(次のフレーム)と描画が済むまで
        yield return new WaitForEndOfFrame();
        var seq = CtSeq;
        if (seq == null || !seq.IsWaitingForSelection) yield break;
        int n = seq.QaCardCount;
        var sb = new StringBuilder();
        int probs = seq.ValidateChoiceDisplay(false, sb);
        if (probs > 0) { ctStateFail++; Check(false, $"{label}: display state{sb} :: {seq.DescribeChoiceSlots()}"); }
        // 文字が描かれているか(明るい文字の画素の割合。背景は暗い紺)
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        var fr = new List<string>();
        try
        {
            if (seq.QaPortrait)
            {
                for (int i = 0; i < n; i++)
                {
                    var vp = seq.QaPortraitViewport(i);
                    float f = vp != null ? BrightFrac(tex, ScreenRectOf(vp)) : 0f;
                    fr.Add($"{i}:{f:P1}");
                    ctMinFrac = Mathf.Min(ctMinFrac, f);
                    if (f < 0.004f) { ctPixelFail++; Check(false, $"{label}: slot {i} ({seq.QaData(i).CardId}) right-side text not drawn (bright {f:P2}) :: {seq.DescribeChoiceSlots()}"); shot = true; }
                }
            }
            else
            {
                // 横: カード下の説明の欄(選んでいなければ「カードを選んでください」)
                var dt = seq.detailDescriptionText;
                var sc = dt != null ? dt.GetComponentInParent<UguiScrollText>(true) : null;
                RectTransform area = sc != null && sc.Viewport != null ? sc.Viewport : dt != null ? dt.rectTransform : null;
                float f = area != null ? BrightFrac(tex, ScreenRectOf(area)) : 0f;
                fr.Add($"detail:{f:P1}");
                ctMinFrac = Mathf.Min(ctMinFrac, f);
                if (f < 0.003f) { ctPixelFail++; Check(false, $"{label}: detail text not drawn (bright {f:P2}) :: {seq.DescribeChoiceSlots()}"); shot = true; }
                if (seq.QaHighlighted >= 0 && seq.detailTitleText != null)
                {
                    float ft = BrightFrac(tex, ScreenRectOf(seq.detailTitleText.rectTransform));
                    fr.Add($"title:{ft:P1}");
                    if (ft < 0.003f) { ctPixelFail++; Check(false, $"{label}: detail title not drawn (bright {ft:P2})"); shot = true; }
                }
                // カード面の名前
                for (int i = 0; i < n; i++)
                {
                    var c = seq.QaCards[i];
                    Check(c.titleText.enabled || c.TitleTooSmall, $"{label}: card {i} face title visible");
                }
            }
        }
        finally { Destroy(tex); }
        L($"[ct] {label} ok={probs == 0} text-pixels {string.Join(" ", fr)}");
        if (shot) Shot($"{ctTag}_{label}");
    }

    // 画面上の範囲は QaSweep.Layout.cs の ScreenRectOf(左下原点の画素)
    static float BrightFrac(Texture2D tex, Rect r)
    {
        int x0 = Mathf.Clamp(Mathf.FloorToInt(r.xMin), 0, tex.width - 1), x1 = Mathf.Clamp(Mathf.CeilToInt(r.xMax), 0, tex.width);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(r.yMin), 0, tex.height - 1), y1 = Mathf.Clamp(Mathf.CeilToInt(r.yMax), 0, tex.height);
        int w = x1 - x0, h = y1 - y0;
        if (w <= 0 || h <= 0) return 0f;
        var px = tex.GetPixels32();
        int bright = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                var p = px[y * tex.width + x];
                if (Mathf.Max(p.r, Mathf.Max(p.g, p.b)) > 175) bright++;
            }
        return bright / (float)(w * h);
    }

    // 文字の絵(フォントの貼り付け先)に新しい字を大量に求めて、作り直し/拡張を起こす(長いランで HUD 等が色々な大きさの字を使った状態の再現)
    void CtFontStress()
    {
        var seq = CtSeq;
        Font f = null;
        if (seq != null) { var t = seq.QaPortraitDesc(0); f = t != null ? t.font : seq.detailDescriptionText != null ? seq.detailDescriptionText.font : null; }
        if (f == null) f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var sb = new StringBuilder();
        for (int i = 0; i < 250; i++) sb.Append((char)Random.Range(0x4E00, 0x9FA0));
        string s = sb.ToString();
        int before = ctRebuilds;
        foreach (int size in new[] { 17, 27, 31, 36, 47 }) f.RequestCharactersInTexture(s, size, FontStyle.Normal);
        f.RequestCharactersInTexture(s, Random.Range(18, 60), FontStyle.Bold);
        if (ctRebuilds == before) L($"[ct] font stress did not rebuild the atlas (tex {(f.material != null && f.material.mainTexture != null ? f.material.mainTexture.width + "x" + f.material.mainTexture.height : "?")})");
    }
}
#endif
