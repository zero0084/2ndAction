#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

// キャラごとのキャラカード枠 / 雲の描画順 / ソニックムーブ(2026-10-02)の確認。 -qaCharCards <dir>
// テスト機の保存は最初に控えて最後に戻す。
public partial class QaSweep
{
    IEnumerator CharCardsMode()
    {
        Application.targetFrameRate = 60;
        var snap = SaveSystem.Capture();
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        yield return new WaitForSecondsRealtime(3.5f);

        // ---- 1) キャラごとの枠: 黒剣士と忍者に別々のカード(同じ1枚を両方に付けられる)
        CardInventory.DebugOwnEveryMissing();
        gm.SetSelectedCharacter("swordsman");
        bool e1 = gm.EquipCharacterCard(0, "attack_up", 1);
        bool e2 = gm.EquipCharacterCard(1, "speed_up", 1);
        gm.SetCharacterCardOwner("ninja");
        bool e3 = gm.EquipCharacterCard(0, "attack_up", 1); // 黒剣士にも付いている1枚を、忍者にも
        bool e4 = gm.EquipCharacterCard(1, "jump_count_up", 1);
        gm.SetCharacterCardOwner(gm.SelectedCharacterId);
        var sw = gm.GetCharacterCardsOf("swordsman"); var nj = gm.GetCharacterCardsOf("ninja");
        L($"[cc] equip sw={e1},{e2} ninja={e3},{e4} | swordsman=[{string.Join(",", sw.ids)}] ninja=[{string.Join(",", nj.ids)}] prefs sw='{PlayerPrefs.GetString(GameManager.CharacterCardSlotsPrefix + "swordsman")}' nj='{PlayerPrefs.GetString(GameManager.CharacterCardSlotsPrefix + "ninja")}'");
        Check(e1 && e2 && e3 && e4 && sw.ids[1] == "speed_up" && nj.ids[1] == "jump_count_up" && sw.ids[0] == "attack_up" && nj.ids[0] == "attack_up",
            "each character keeps its own 3 character-card slots (the same owned card can go on several characters)");
        Check(!PlayerPrefs.HasKey("CharacterCardSlots"), "the old shared slot key is gone (moved to the selected character)");

        // ---- 2) キャラ選択 → カード設定 → デッキ編集(そのキャラ) → 切り替え → 戻る
        gm.OpenCharacterSelect();
        yield return new WaitForSecondsRealtime(1.2f);
        Shot("cc_charselect");
        yield return new WaitForSecondsRealtime(0.2f);
        gm.OpenCharacterCardsFromSelect("ninja");
        yield return new WaitForSecondsRealtime(1.2f);
        var de = FindFirstObjectByType<DeckEditUI>();
        L($"[cc] deck edit header='{(de != null ? de.CharHeaderText : "-")}' owner={gm.CharacterCardOwnerId}");
        Check(de != null && gm.CharacterCardOwnerId == "ninja" && de.CharHeaderText.Contains(CharacterDatabase.FindById("ninja").displayName), "'カード設定' opens deck edit on that character's slots");
        Shot("cc_deckedit_ninja");
        yield return new WaitForSecondsRealtime(0.2f);
        de.DebugCycleCharacter(+1);
        yield return new WaitForSecondsRealtime(0.4f);
        L($"[cc] after ▶: owner={gm.CharacterCardOwnerId} header='{de.CharHeaderText}'");
        Check(gm.CharacterCardOwnerId != "ninja", "▶ switches to the next character's slots");
        Shot("cc_deckedit_next");
        yield return new WaitForSecondsRealtime(0.2f);
        de.Close();
        yield return new WaitForSecondsRealtime(1.4f);
        var cs = FindFirstObjectByType<CharacterSelectUI>(FindObjectsInactive.Include);
        L($"[cc] after close: charSelectActive={(cs != null && cs.gameObject.activeInHierarchy)} owner={gm.CharacterCardOwnerId} selected={gm.SelectedCharacterId}");
        Check(cs != null && cs.gameObject.activeInHierarchy && gm.CharacterCardOwnerId == gm.SelectedCharacterId, "closing deck edit returns to character select (owner back to the selected character)");
        Shot("cc_back_to_select");
        yield return new WaitForSecondsRealtime(0.2f);
        cs.Close();
        yield return new WaitForSecondsRealtime(1.2f);

        // ---- 3) Runは選んだキャラの枠を使う(忍者で出発 → JUMP COUNT UP が付く、黒剣士の SPEED UP は付かない)
        yield return BeginRun("ninja", "wasteland_road");
        int jc = gm.GetCurrentRunStack("jump_count_up"), su = gm.GetCurrentRunStack("speed_up");
        L($"[cc] ninja run: jump_count_up stack={jc} speed_up stack={su} maxJumps={pc.maxJumps}");
        Check(jc == 1 && su == 0, "a run uses the slots of the character it starts with");

        // ---- 4) 雲は地面より奥
        var clouds = FindObjectsByType<ForegroundCloudLayer>(FindObjectsSortMode.None);
        var cloudSr = clouds.SelectMany(c => c.GetComponentsInChildren<SpriteRenderer>(true)).ToList();
        bool cloudsBehind = cloudSr.Count == 0 || cloudSr.All(s => s.sortingOrder < RenderOrder.GroundFill);
        L($"[cc] clouds={cloudSr.Count} orders=[{string.Join(",", cloudSr.Select(s => s.sortingOrder).Distinct())}] ground={RenderOrder.Ground}");
        Check(cloudsBehind, "sky clouds draw behind the ground/road");

        // ---- 5) ソニックムーブ: 低速/中速/高速で撮影
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        var fx = SonicMoveFx.Instance;
        float[] scales = { 1f, 3.2f, 6f };
        foreach (float sc in scales)
        {
            PlayerController.DebugSpeedScale = sc;
            yield return new WaitForSeconds(2.5f);
            int ghosts = fx != null ? fx.GetComponentsInChildren<SpriteRenderer>().Count(s => s.enabled && s.name == "SonicGhost") : 0;
            L($"[cc] sonic: {fx?.Kmh:F0}km/h intensity={fx?.Intensity:F2} ghosts={ghosts}");
            Shot($"cc_sonic_{(int)(fx != null ? fx.Kmh : 0)}kmh");
            yield return null; yield return null;
            if (sc >= 6f) Check(fx != null && fx.Intensity > 0.8f && ghosts >= 3, "at high speed the sonic-move effect is on (afterimages)");
            if (sc <= 1f) Check(fx != null && fx.Intensity < 0.05f, "no sonic-move effect at low speed");
        }
        // 動画用(高速 4秒)
        string vdir = System.IO.Path.Combine(outDir, "video_sonic");
        System.IO.Directory.CreateDirectory(vdir);
        Time.captureFramerate = 30;
        for (int f = 0; f < 120; f++)
        {
            if (f % 25 == 4) pc.debugInjectFlick = f % 50 == 4 ? PlayerController.FlickDirection.Up : PlayerController.FlickDirection.Forward; else pc.debugInjectFlick = null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(vdir, $"f{f:00000}.png"));
        }
        Time.captureFramerate = 0;
        pc.debugInjectFlick = null;
        PlayerController.DebugSpeedScale = 1f;
        yield return EndRun();
        SaveSystem.Restore(snap);
        CardInventory.ReloadFromPrefs();
        L("[cc] test machine save restored");
    }
}
#endif
