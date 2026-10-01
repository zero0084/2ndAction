#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Reflection;
using UnityEngine;

// ホーム/メニュー/設定/遷移の見た目の確認用撮影(2026-10-01)。 -qaUiShots <dir> [-qaUiTag name]
public partial class QaSweep
{
    IEnumerator UiShotsMode()
    {
        string tag = Arg("-qaUiTag", "ui");
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        yield return new WaitForSecondsRealtime(3.5f); // タイトルのフェード
        Shot($"{tag}_home");
        yield return null; yield return null;
        // 設定(旧: 歯車の列 / 新: 設定パネル)
        var f = typeof(GameManager).GetField("showSettingsPanel", BindingFlags.NonPublic | BindingFlags.Instance);
        if (f != null) { f.SetValue(gm, true); yield return new WaitForSecondsRealtime(0.5f); Shot($"{tag}_home_settings"); yield return null; yield return null; f.SetValue(gm, false); }
        var sp = System.Type.GetType("SettingsPanel");
        if (sp != null)
        {
            sp.GetMethod("OpenStatic", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            yield return new WaitForSecondsRealtime(0.08f); Shot($"{tag}_settings_opening"); yield return null; yield return null;
            yield return new WaitForSecondsRealtime(0.5f); Shot($"{tag}_settings"); yield return null; yield return null;
            sp.GetMethod("CloseStatic", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            yield return new WaitForSecondsRealtime(0.5f);
        }
        var dp = System.Type.GetType("DebugPanel");
        if (dp != null)
        {
            dp.GetMethod("OpenStatic", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            yield return new WaitForSecondsRealtime(0.5f); Shot($"{tag}_debug"); yield return null; yield return null;
            dp.GetMethod("CloseStatic", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            yield return new WaitForSecondsRealtime(0.5f);
        }
        foreach (var name in new[] { "OpenDeckEdit", "OpenCardFusion", "OpenCharacterSelect", "OpenStageSelect" })
        {
            typeof(GameManager).GetMethod(name).Invoke(gm, null);
            yield return new WaitForSecondsRealtime(0.12f); Shot($"{tag}_{name}_mid"); yield return null; yield return null;
            yield return new WaitForSecondsRealtime(0.9f); Shot($"{tag}_{name}"); yield return null; yield return null;
            // 閉じる(各画面のClose)
            CloseOverlay(name);
            yield return new WaitForSecondsRealtime(0.08f); Shot($"{tag}_{name}_closing"); yield return null; yield return null;
            yield return new WaitForSecondsRealtime(0.9f);
        }
        Shot($"{tag}_home_after");
        yield return null; yield return null;
        // ステージ選択 → 出発(扉の光)
        typeof(GameManager).GetMethod("OpenStageSelect").Invoke(gm, null);
        yield return new WaitForSecondsRealtime(1f);
        var ss = FindFirstObjectByType<StageSelectUI>(FindObjectsInactive.Include);
        if (ss != null) ss.GetType().GetMethod("Confirm", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)?.Invoke(ss, null);
        for (int i = 0; i < 6; i++) { yield return new WaitForSecondsRealtime(0.15f); Shot($"{tag}_depart_{i}"); yield return null; yield return null; }
        yield return new WaitForSecondsRealtime(1.2f); Shot($"{tag}_countdown"); yield return null; yield return null;
        yield return new WaitForSecondsRealtime(3f);
        // ポーズ
        var pm = typeof(GameManager).GetField("showPauseMenu", BindingFlags.NonPublic | BindingFlags.Instance);
        if (pm != null) { pm.SetValue(gm, true); TimeControl.Pause(this); yield return new WaitForSecondsRealtime(0.4f); Shot($"{tag}_pause"); yield return null; yield return null; pm.SetValue(gm, false); TimeControl.Resume(this); }
        yield return new WaitForSecondsRealtime(0.5f);
    }

    void CloseOverlay(string openName)
    {
        System.Type t = openName == "OpenDeckEdit" ? typeof(DeckEditUI) : openName == "OpenCardFusion" ? typeof(CardFusionUI) : openName == "OpenCharacterSelect" ? typeof(CharacterSelectUI) : typeof(StageSelectUI);
        var ui = FindFirstObjectByType(t, FindObjectsInactive.Include);
        if (ui == null) return;
        var m = t.GetMethod("Close", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        m?.Invoke(ui, null);
    }
}
#endif
