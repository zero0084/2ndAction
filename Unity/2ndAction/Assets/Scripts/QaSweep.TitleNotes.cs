#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Reflection;
using UnityEngine;

// タイトルのお知らせ/文字の大きさ/更新の案内(2026-11-14)の自動試験: -titleScreen 1 -qaTitleNotes <dir> [-updateMock available]
//  V 文字の大きさの比較(今 / ロゴ拡大 / 案内拡大 / 両方)を撮る(窓の縦横は起動の大きさ)
//  N お知らせ: 未読なら NEW / 開いている間は開始しない / スクロールできる / 閉じる・戻るでタイトルへ / 読んだら NEW が消える(保存)
//  U 更新の案内(-updateMock available): ホームに落ち着いてから1回だけ / ランの途中には出ない / あとで で閉じる / 2回目は出ない
public partial class QaSweep
{
    static readonly FieldInfo PnScroll = typeof(PatchNotesUi).GetField("scroll", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly FieldInfo PnInst = typeof(PatchNotesUi).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);

    IEnumerator TitleNotesModeQa()
    {
        float w = 0f;
        while ((BootSplash.Showing || !TitleScreen.InputReady) && w < 30f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        string tnLang = Arg("-tnLang", "");
        if (tnLang != "") Loc.Set(tnLang, false);
        string tag = (Screen.height > Screen.width ? "port" : "land") + (tnLang != "" ? "_" + tnLang : "");
        Check(TitleScreen.Showing, "title is up");

        // ---- V: 比較の案
        var variants = new (string name, float logo, float prompt, bool band)[] { ("final", 1f, 1f, false) }; // 2026-11-14: 採用(横 D / 縦 C)。比べた案は Docs/Release_2026-11-14
        float l0 = TitleScreen.LogoScale, p0 = TitleScreen.PromptScale; bool b0 = TitleScreen.PromptBand;
        TitleScreen.DevFreezePulse = true; TitleScreen.DevTapText = Arg("-tnTapText", "1") == "1"; // 比べやすく(明るい所で止める / スマホの文)
        foreach (var v in variants)
        {
            TitleScreen.LogoScale = v.logo; TitleScreen.PromptScale = v.prompt; TitleScreen.PromptBand = v.band;
            yield return new WaitForSecondsRealtime(0.4f);
            yield return ShotW($"tn_{tag}_{v.name}");
            Rect lr = TitleScreen.LastLogoRect, pr = TitleScreen.LastPromptRect, nr = TitleScreen.LastNoticeRect;
            bool inside = lr.xMin >= 0 && lr.xMax <= Screen.width && pr.xMin >= 0 && pr.xMax <= Screen.width && pr.yMax <= Screen.height;
            bool apart = !lr.Overlaps(pr) && !lr.Overlaps(nr);
            L($"[V] {v.name}: logo {lr} prompt {pr} notice {nr} inside={inside} apart={apart}");
            Check(inside && apart, $"V {v.name}: logo/prompt inside the screen and not overlapping the notice button");
        }
        TitleScreen.LogoScale = l0; TitleScreen.PromptScale = p0; TitleScreen.PromptBand = b0; TitleScreen.DevFreezePulse = false; TitleScreen.DevTapText = false;
        yield return null;

        // ---- N: お知らせ
        SaveStore.SetInt(PatchNotesUi.ReadKey, 0); SaveStore.Save();
        yield return null;
        Check(PatchNotesUi.HasUnread, "N: unread notes -> NEW on the button");
        yield return ShotW($"tn_{tag}_N_new");
        PatchNotesUi.Open();
        yield return new WaitForSecondsRealtime(0.5f);
        Check(PatchNotesUi.IsOpen && !PatchNotesUi.HasUnread && SaveStore.GetInt(PatchNotesUi.ReadKey, 0) == PatchNotesUi.Latest, "N: opening marks the newest notes as read (saved)");
        TitleScreen.DebugPress = true; // 開いている間の入力
        yield return new WaitForSecondsRealtime(0.6f);
        Check(TitleScreen.Showing && TitleScreen.Starts == 0, "N: an input while the notes are open does not start the game");
        yield return ShotW($"tn_{tag}_N_open");
        var inst = PnInst.GetValue(null) as PatchNotesUi;
        var sc = inst != null ? PnScroll.GetValue(inst) as UiScroll : null;
        if (sc != null)
        {
            Check(sc.CanScroll, "N: the notes are longer than the panel and can scroll");
            sc.y = 99999f;
            yield return null; yield return null;
            yield return ShotW($"tn_{tag}_N_scrolled");
            Check(sc.y > 10f, $"N: scrolled to the end (y {sc.y:F0})");
        }
        typeof(GameManager).GetMethod("DoBack", NP).Invoke(GameManager.Instance, null); // 戻る / Esc
        yield return null;
        Check(!PatchNotesUi.IsOpen && TitleScreen.Showing && !AppExit.Open, "N: Back closes the notes and stays on the title (does not open the exit dialog)");
        yield return new WaitForSecondsRealtime(0.4f);
        Check(!PatchNotesUi.HasUnread, "N: NEW is gone after reading");
        yield return ShotW($"tn_{tag}_N_read");
        PatchNotesUi.Open(); yield return null; PatchNotesUi.Close();
        TitleScreen.DebugPress = true; // 閉じた直後の入力(Android の戻るキー等が同じフレームで届いた時と同じ)
        yield return new WaitForSecondsRealtime(0.3f);
        Check(TitleScreen.Showing && TitleScreen.Starts == 0, "N: closing returns to the title and the closing input does not start the game");
        yield return new WaitForSecondsRealtime(0.3f);

        // ---- U: 更新の案内
        bool mock = System.Array.IndexOf(QaArgs.All, "-updateMock") >= 0;
        TitleScreen.DebugPress = true;
        w = 0f;
        while (TitleScreen.Showing && w < 3f) { yield return null; w += Time.unscaledDeltaTime; }
        Check(!TitleScreen.Showing, "title -> home");
        if (mock)
        {
            w = 0f;
            while (!UpdateCheck.PromptOpen && w < 12f) { yield return null; w += Time.unscaledDeltaTime; }
            Check(UpdateCheck.PromptOpen && UpdateCheck.PromptsShown == 1, $"U: the update prompt appears once the home is calm ({UpdateCheck.State}, {w:F1}s)");
            yield return ShotW($"tn_{tag}_U_prompt");
            Check(UiInputGate.ModalOpen, "U: the prompt blocks the home behind it");
            UpdateCheck.DebugChoose(false);
            yield return new WaitForSecondsRealtime(2.5f);
            Check(!UpdateCheck.PromptOpen && UpdateCheck.PromptsShown == 1, "U: 'later' closes it and it does not come back in this launch");
            gm = GameManager.Instance;
            Check(gm != null && !gm.HasStarted, "U: still at home, playable");
        }
        else L($"[U] no mock: state {UpdateCheck.State} result '{UpdateCheck.LastResult}' (Windows/dev APK: skipped or notplay)");
    }
}
#endif
