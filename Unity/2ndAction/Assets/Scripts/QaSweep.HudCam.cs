#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// UI 全体の改善(2026-10-31)の確認: -qaHudCam <dir> [-hcPhase shots|set|check] [-hcScenes run,boss,multi]
//  shots: 通常走行 / 単体のボス / 複数のボス を撮影し、地面の割合・キャラとボスの大きさ・ボスが画面内か・ボスの欄の行数を記録。
//         通常走行では HUD の4モード・大きさ・背景・カードの欄(小/大/一時停止)も撮る。
//         -hudLegacy 1 -bossCamOff 1 で同じ場面を改修前の表示で撮る(比較)。
//  set/check: 設定を変えて終わる → 次の起動で同じ値か(再起動後も保持)
public partial class QaSweep
{
    struct HcFrame { public float ground, playerPx, bossPx; public bool bossInView; public int bosses, rows; public float blend, zoom; }

    IEnumerator HudCamMode()
    {
        Application.targetFrameRate = 60;
        string phase = Arg("-hcPhase", "shots");
        bool legacy = HudPrefs.Legacy;
        L($"[hc] phase={phase} legacy={legacy} bossCamOff={CameraFollow.BossFramingDisabled} screen={Screen.width}x{Screen.height}");
        if (phase == "set")
        {
            HudPrefs.SetMode(HudPrefs.Mode.Custom);
            HudPrefs.SetCustom(HudPrefs.Item.Best, false); HudPrefs.SetCustom(HudPrefs.Item.Combo, false);
            HudPrefs.SetScale(2); HudPrefs.SetOpacity(1);
            L($"[hc] set mode={HudPrefs.CurrentMode} items={HudPrefs.CustomItems} scale={HudPrefs.ScaleIndex} opacity={HudPrefs.OpacityIndex}");
            yield break;
        }
        if (phase == "check")
        {
            HudPrefs.Reload();
            L($"[hc] loaded mode={HudPrefs.CurrentMode} items={HudPrefs.CustomItems} scale={HudPrefs.ScaleIndex} opacity={HudPrefs.OpacityIndex}");
            Check(HudPrefs.CurrentMode == HudPrefs.Mode.Custom, "persist: the mode survives a restart");
            Check(!HudPrefs.CustomOn(HudPrefs.Item.Best) && !HudPrefs.CustomOn(HudPrefs.Item.Combo) && HudPrefs.CustomOn(HudPrefs.Item.Hp), "persist: the custom items survive a restart");
            Check(HudPrefs.ScaleIndex == 2 && HudPrefs.OpacityIndex == 1, "persist: size and background survive a restart");
            // 元へ(試験用の保存ファイル)
            HudPrefs.SetMode(HudPrefs.Mode.Standard); HudPrefs.SetCustom(HudPrefs.Item.Best, true); HudPrefs.SetCustom(HudPrefs.Item.Combo, true); HudPrefs.SetScale(1); HudPrefs.SetOpacity(0);
            yield break;
        }

        if (!legacy) { HudPrefs.SetMode(HudPrefs.Mode.Standard); HudPrefs.SetScale(1); HudPrefs.SetOpacity(0); }
        string lang = Arg("-hcLang", ""); // 長い言語/右から左の言語での見え方(保存しない)
        if (lang != "") Loc.Set(lang, save: false);
        var scenes = Arg("-hcScenes", "run,boss,multi").Split(',');
        if (scenes.Contains("run")) yield return HcRun(legacy);
        if (scenes.Contains("boss")) yield return HcBoss(legacy, 1, "boss");
        if (scenes.Contains("multi")) yield return HcBoss(legacy, int.Parse(Arg("-hcMultiKm", "35")), "multi");
        if (!legacy) { HudPrefs.SetMode(HudPrefs.Mode.Standard); HudPrefs.SetScale(1); HudPrefs.SetOpacity(0); }
    }

    IEnumerator HcStart(string stage)
    {
        gm = GameManager.Instance;
        gm.SetSelectedCharacter("swordsman"); gm.SetCharacterCardOwner("swordsman");
        var unlocked = CardDatabase.UnlockedCards.Where(c => c != null && c.icon != null && !CardVariant.IsVariantKey(c.cardId)).ToList();
        for (int s = 0; s < 2 && s < unlocked.Count; s++) { CardInventory.AddCard(unlocked[s].cardId, 1, 1); gm.EquipCharacterCard(s, unlocked[s].cardId, 1); }
        yield return BeginRun("swordsman", stage);
        stopKeepAlive = true; // HP の表示を 990 にしない
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        // デッキから何枚か取った状態(カードの欄の撮影用)
        var apply = typeof(GameManager).GetMethod("ApplyUpgradeByCardId", NP);
        var have = new List<string>(); gm.CollectRunCardIds(have);
        foreach (var id in CardDatabase.AllCards.Where(c => c != null && c.icon != null && !CardVariant.IsVariantKey(c.cardId) && !have.Contains(c.cardId)).Select(c => c.cardId).Take(9)) { apply.Invoke(gm, new object[] { id }); yield return null; }
    }

    HcFrame HcMeasure(string tag)
    {
        var f = new HcFrame { ground = -1f };
        var cam = Camera.main; var cf = CameraFollow.Instance; var tm = TerrainManager.Instance;
        if (cam == null || pc == null) return f;
        float px = pc.transform.position.x;
        float? g = tm != null ? tm.GetHeightAt(px) : null;
        if (g.HasValue) f.ground = cam.WorldToViewportPoint(new Vector3(px, g.Value, 0f)).y;
        var psr = pc.VisualRenderer; // 体の絵(子の効果の輪などは数えない)
        if (psr != null) f.playerPx = (cam.WorldToScreenPoint(new Vector3(px, psr.bounds.max.y, 0f)).y - cam.WorldToScreenPoint(new Vector3(px, psr.bounds.min.y, 0f)).y);
        var bb = new List<Bounds>();
        f.bosses = BossCamFraming.Collect(px, bb);
        f.bossInView = true;
        foreach (var b in bb)
        {
            Vector3 lo = cam.WorldToViewportPoint(b.min), hi = cam.WorldToViewportPoint(b.max);
            f.bossPx = Mathf.Max(f.bossPx, (hi.y - lo.y) * Screen.height);
            if (lo.x < -0.02f || hi.x > 1.02f || hi.y > 1.02f || lo.y < -0.02f) f.bossInView = false;
        }
        f.rows = BossHudPanel.LastRows;
        f.blend = cf != null ? cf.BossBlend : 0f; f.zoom = cf != null ? cf.BossZoomApplied : 1f;
        L($"[hc {tag}] ground={f.ground:F2} playerPx={f.playerPx:F0} bossPx={f.bossPx:F0} bosses={f.bosses} inView={f.bossInView} rows={f.rows} blend={f.blend:F2} zoom={f.zoom:F2} camSize={(cam != null ? cam.orthographicSize : 0f):F2} logicalHalf={GameView.HalfWidth(cam):F1}");
        return f;
    }

    float HcPanelH() => (float)typeof(GameManager).GetProperty("RunPanelHeightV", NP).GetValue(gm);

    void HcPause(bool on)
    {
        typeof(GameManager).GetField("showPauseMenu", NP).SetValue(gm, on);
        var owner = typeof(GameManager).GetField("pauseMenuTimeOwner", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        if (on) TimeControl.Pause(owner); else TimeControl.Resume(owner);
    }

    IEnumerator HcRun(bool legacy)
    {
        yield return HcStart("wasteland_road");
        WarpTo(2600f);
        float w = 0f;
        while (w < 4f) { SetKmh(120f); yield return null; w += Time.deltaTime; }
        var hud = RunBuildHud.Instance;
        HcMeasure("run");
        Shot("run_standard");
        yield return new WaitForSecondsRealtime(0.4f);
        if (legacy) { yield return EndRun(); yield break; }

        Check(hud != null && hud.IsVisible && !hud.Expanded, "standard: the card list is shown small");
        float smallS = hud.LastSlotSize;
        hud.DebugSetExpanded(true); yield return null; yield return null;
        Check(hud.Expanded && (hud.LastSlotSize > smallS * 1.15f || GameManager.HudStacked), $"standard: tapping the list shows it large (slot {smallS:F0} -> {hud.LastSlotSize:F0}px)");
        Shot("run_standard_cards_large");
        yield return new WaitForSecondsRealtime(0.4f);
        hud.DebugSetExpanded(false);

        HudPrefs.SetMode(HudPrefs.Mode.Simple); yield return new WaitForSecondsRealtime(0.4f);
        Check(!HudPrefs.Show(HudPrefs.Item.Best) && HudPrefs.Show(HudPrefs.Item.Hp) && HudPrefs.Show(HudPrefs.Item.Speed) && HudPrefs.Show(HudPrefs.Item.Distance), "simple: HP / distance / speed only");
        Check(!hud.IsVisible, "simple: the card list is hidden");
        Shot("run_simple");
        yield return new WaitForSecondsRealtime(0.4f);

        HudPrefs.SetMode(HudPrefs.Mode.Hidden); yield return new WaitForSecondsRealtime(0.4f);
        Check(HcPanelH() <= 0f && !hud.IsVisible, "hidden: no run panel and no card list");
        Shot("run_hidden");
        yield return new WaitForSecondsRealtime(0.4f);
        // 非表示でも一時停止は押せる/開くと設定とカードの一覧(全部・大きく)
        Rect pr = (Rect)typeof(GameManager).GetMethod("GetPauseButtonRect", NP).Invoke(gm, null);
        Check(pr.width >= 40f, $"hidden: the pause button keeps its size ({pr.width:F0}px)");
        HcPause(true); yield return new WaitForSecondsRealtime(0.4f);
        Check(hud.IsVisible && hud.Expanded, "hidden + pause: the full card list is shown");
        Shot("run_hidden_pause");
        yield return new WaitForSecondsRealtime(0.4f);
        HcPause(false); yield return new WaitForSecondsRealtime(0.3f);
        Check(!hud.IsVisible, "hidden: the list hides again after closing the pause menu");

        HudPrefs.SetMode(HudPrefs.Mode.Custom);
        foreach (var it in new[] { HudPrefs.Item.Best, HudPrefs.Item.LevelExp, HudPrefs.Item.CharCards, HudPrefs.Item.TakeHome }) HudPrefs.SetCustom(it, false);
        yield return new WaitForSecondsRealtime(0.4f);
        float h1 = HcPanelH();
        Shot("run_custom");
        yield return new WaitForSecondsRealtime(0.4f);
        // 設定の画面(一時停止 → 設定): 画面情報の表示の欄(カスタムの項目)
        HcPause(true); SettingsPanel.OpenStatic(); yield return new WaitForSecondsRealtime(0.8f);
        Check(SettingsPanel.IsOpen, "settings: opens from the pause menu during a run");
        Shot("settings_hud_custom");
        yield return new WaitForSecondsRealtime(0.4f);
        SettingsPanel.CloseStatic(); yield return new WaitForSecondsRealtime(0.6f); HcPause(false); yield return new WaitForSecondsRealtime(0.3f);
        foreach (var it in new[] { HudPrefs.Item.Best, HudPrefs.Item.LevelExp, HudPrefs.Item.CharCards, HudPrefs.Item.TakeHome }) HudPrefs.SetCustom(it, true);
        yield return new WaitForSecondsRealtime(0.3f);
        Check(HcPanelH() > h1 && HudPrefs.Show(HudPrefs.Item.LevelExp), $"custom: turning items back on shows them again (panel {h1:F0} -> {HcPanelH():F0})");

        HudPrefs.SetMode(HudPrefs.Mode.Standard);
        HudPrefs.SetScale(0); yield return new WaitForSecondsRealtime(0.4f); Shot("run_size_small"); yield return new WaitForSecondsRealtime(0.4f);
        HudPrefs.SetScale(2); yield return new WaitForSecondsRealtime(0.4f); Shot("run_size_large"); yield return new WaitForSecondsRealtime(0.4f);
        Rect pr2 = (Rect)typeof(GameManager).GetMethod("GetPauseButtonRect", NP).Invoke(gm, null);
        Check(Mathf.Abs(pr2.width - pr.width) < 0.5f, "size: the pause button does not change size");
        HudPrefs.SetScale(1); HudPrefs.SetOpacity(2); yield return new WaitForSecondsRealtime(0.4f); Shot("run_background_min"); yield return new WaitForSecondsRealtime(0.4f);
        HudPrefs.SetOpacity(0);
        yield return EndRun();
    }

    IEnumerator HcBoss(bool legacy, int km, string tag)
    {
        yield return HcStart("wasteland_road");
        var bm = BossManager.Instance; bm.enabled = true;
        if (km > 1) bm.SprintMarkSkippedGates(km - 1); // 複数のボス: そこまでの関門を倒して来た状態(再戦のプール)
        typeof(GameManager).GetField("distanceExclusionOffset", NP)?.SetValue(gm, 0f);
        typeof(GameManager).GetField("distanceExclusionOffsetExact", NP)?.SetValue(gm, 0.0);
        WarpTo(km * 1000f - 400f);
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false; // 雑魚なし(ボスの見え方だけ)
        float w = 0f;
        while (!(bm.IsBossPhase && bm.AliveBossCount > 0) && w < 60f) { SetKmh(120f); yield return null; w += Time.deltaTime; }
        Check(bm.IsBossPhase, $"{tag}: reached the boss gate");
        // 構図が落ち着くまで(登場の演出 + 寄り)
        w = 0f; while (w < 5f) { yield return null; w += Time.deltaTime; }
        var frames = new List<HcFrame>();
        for (int i = 0; i < 4; i++)
        {
            frames.Add(HcMeasure($"{tag}{i}"));
            Shot($"{tag}_{i}");
            w = 0f; while (w < 1.5f) { yield return null; w += Time.deltaTime; }
        }
        var f = frames.Where(x => x.bosses > 0).ToList();
        if (f.Count == 0) { Check(false, $"{tag}: no boss in range to measure"); yield return EndRun(); yield break; }
        float ground = f.Average(x => x.ground), ppx = f.Average(x => x.playerPx);
        L($"[hc {tag}] avg ground={ground:F2} playerPx={ppx:F0} bossPx={f.Average(x => x.bossPx):F0} bosses={f.Max(x => x.bosses)} inView={f.Count(x => x.bossInView)}/{f.Count}");
        if (!legacy && !GameManager.HudStacked)
        {
            Check(ground > 0.12f && ground < 0.36f, $"{tag}: the ground is in the bottom 25-35% ({ground:F2})");
            Check(f.All(x => x.bossInView), $"{tag}: every engaged boss stays on screen");
            Check(f.All(x => x.rows == x.bosses || x.rows >= BossHudPanel.MaxRows), $"{tag}: one boss panel row per boss ({string.Join(",", f.Select(x => x.rows + "/" + x.bosses))})");
            if (tag == "multi") Check(f.Max(x => x.bosses) >= 2, $"multi: two or more bosses at {km}km");
        }
        // BREAK の帯(BREAK させた時の表示)
        if (!legacy)
        {
            var dbg = FindObjectsByType<WildBossBase>(FindObjectsSortMode.None).FirstOrDefault(b => !b.IsDead);
            if (dbg != null) { dbg.DebugBreak(); w = 0f; while (w < 0.6f) { yield return null; w += Time.deltaTime; } HcMeasure($"{tag}_break"); Shot($"{tag}_break"); yield return new WaitForSecondsRealtime(0.4f); }
        }
        yield return EndRun();
    }
}
#endif
