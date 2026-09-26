#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// カード合成画面の目視確認用ツアー(Editor専用、GUIのEditorで実行)。FusionShots/ へ撮影する。
// 所持カード/デッキ/キャラカード/MILEは退避して撮影後に元へ戻す。
public class FusionVisualTour : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("FusionVisualTour", 0) != 1) return;
        EditorPrefs.SetInt("FusionVisualTour", 0);
        new GameObject("FusionVisualTour").AddComponent<FusionVisualTour>();
    }

    static readonly string[] Keys = { CardInventory.SaveKey, "DeckCardIds", "CharacterCardSlots", "TotalOwnedMile", CardDataMigration.FormatKey, "NewUnconfirmedCardsV1" };
    readonly Dictionary<string, (bool has, string s, int i)> backup = new Dictionary<string, (bool, string, int)>();
    string dir;
    readonly System.Text.StringBuilder log = new System.Text.StringBuilder();

    void Shot(string name)
    {
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png"));
    }

    // 状態を変えてから確実に描画・撮影されるまで待つ(撮影は非同期なので前後に余裕を持たせる)
    IEnumerator ShotAfterSettle(string name)
    {
        yield return new WaitForSecondsRealtime(0.25f);
        Shot(name);
        yield return new WaitForSecondsRealtime(0.35f);
    }

    IEnumerator Start()
    {
        dir = System.IO.Path.Combine(Application.dataPath, "../FusionShots");
        System.IO.Directory.CreateDirectory(dir);
        yield return new WaitForSeconds(1.2f);
        var gm = GameManager.Instance;
        foreach (var k in Keys) backup[k] = (PlayerPrefs.HasKey(k), PlayerPrefs.GetString(k, ""), PlayerPrefs.GetInt(k, 0));

        // ---- 撮影用の所持カード ----
        CardInventory.ResetAll();
        gm.SetDeck(new string[0]);
        var all = CardDatabase.AllCards;
        string A = null, B = null;
        foreach (var c in all) if (A == null && c.effects.Count == 1 && c.effects[0].type == EffectType.AttackPower) A = c.cardId;
        foreach (var c in all) if (B == null && c.effects.Count == 1 && c.effects[0].type == EffectType.MoveSpeed) B = c.cardId;
        for (int i = 0; i < all.Count && i < 26; i++) CardInventory.AddCard(all[i].cardId, 1, 1 + (i % 3));
        var main = new CardVariant { mainId = A, level = 3, rarity = 2 };
        main.AddAbility(A, 3); main.AddAbility(all[5].cardId, 1); main.AddAbility(all[9].cardId, 2);
        var mat = new CardVariant { mainId = B, level = 2, rarity = 1 };
        mat.AddAbility(B, 2); mat.AddAbility(all[12].cardId, 1);
        string mk = main.ToKey(), sk = mat.ToKey();
        CardInventory.AddCard(mk, 3, 1); CardInventory.AddCard(sk, 2, 1);
        CardInventory.AddCard(A, 1, 2);
        gm.AddToDeck(all[1].cardId);

        var ui = gm.cardFusionUI;
        gm.OpenCardFusion();
        yield return new WaitForSecondsRealtime(1.2f);
        StartCoroutine(HideDiagnostics());
        yield return new WaitForSecondsRealtime(0.6f);
        yield return ShotAfterSettle("01_list");
        log.AppendLine($"visibleRows(native)={ui.DebugVisibleRows()} screen={Screen.width}x{Screen.height}");

        ui.DebugSelect(mk, null);
        yield return ShotAfterSettle("02_main_selected");
        ui.DebugSelect(mk, sk);
        yield return ShotAfterSettle("03_cross_preview");
        ui.DebugSelect(A, A);
        yield return ShotAfterSettle("04_same_preview");

        // 異名 両方成功の演出を時間で撮影
        ui.DebugSelect(mk, sk);
        yield return null;
        CardFusionLogic.DebugForcedOutcome = CardFusionLogic.ForcedOutcome.BothSuccess;
        ui.DebugFuse();
        CardFusionLogic.DebugForcedOutcome = null;
        float[] at = { 0.3f, 0.8f, 1.25f, 1.6f, 1.85f, 2.2f, 3.0f };
        float st = Time.realtimeSinceStartup;
        for (int i = 0; i < at.Length; i++)
        {
            while (Time.realtimeSinceStartup - st < at[i]) yield return null;
            Shot($"05_both_{i}");
            yield return null;
        }
        while (!ui.IsShowingResult) yield return null;
        yield return ShotAfterSettle("06_both_result");
        ui.DebugContinue();
        yield return ShotAfterSettle("07_continue");

        // 全失敗
        ui.DebugSelect(A, all[3].cardId);
        yield return null;
        CardFusionLogic.DebugForcedOutcome = CardFusionLogic.ForcedOutcome.BothFail;
        ui.DebugFuse();
        CardFusionLogic.DebugForcedOutcome = null;
        st = Time.realtimeSinceStartup;
        float[] at2 = { 1.3f, 1.9f, 2.5f };
        for (int i = 0; i < at2.Length; i++)
        {
            while (Time.realtimeSinceStartup - st < at2[i]) yield return null;
            Shot($"08_fail_{i}");
            yield return null;
        }
        while (!ui.IsShowingResult) yield return null;
        yield return ShotAfterSettle("09_fail_result");
        ui.DebugBackToList();
        yield return null;

        // 片側成功(素材のみ)
        CardInventory.AddCard(mk, 3, 1); CardInventory.AddCard(sk, 2, 1);
        ui.DebugSelect(mk, sk);
        yield return null;
        CardFusionLogic.DebugForcedOutcome = CardFusionLogic.ForcedOutcome.MaterialOnly;
        ui.DebugFuse();
        CardFusionLogic.DebugForcedOutcome = null;
        ui.DebugSkip();
        while (!ui.IsShowingResult) yield return null;
        yield return ShotAfterSettle("10_material_only_result");
        ui.DebugBackToList();

        // 他の画面比率(ルートを中央へ寄せて16:9/4:3を再現)で一覧の段数を確認
        var rootRect = (RectTransform)ui.root.transform;
        var canvasRect = (RectTransform)rootRect.parent;
        float h = canvasRect.rect.height;
        foreach (var (name, aspect) in new[] { ("16x9", 16f / 9f), ("4x3", 4f / 3f) })
        {
            float w = h * aspect;
            float inset = Mathf.Max(0f, (canvasRect.rect.width - w) / 2f);
            rootRect.offsetMin = new Vector2(inset, 0); rootRect.offsetMax = new Vector2(-inset, 0);
            yield return null;
            ui.Refresh();
            yield return null;
            yield return ShotAfterSettle("11_list_" + name);
            log.AppendLine($"visibleRows({name})={ui.DebugVisibleRows()}");
            ui.DebugSelect(mk, null);
            yield return ShotAfterSettle("12_detail_" + name);
        }
        rootRect.offsetMin = rootRect.offsetMax = Vector2.zero;

        // ---- 後片付け ----
        foreach (var k in Keys)
        {
            var b = backup[k];
            if (!b.has) PlayerPrefs.DeleteKey(k);
            else if (k == "TotalOwnedMile" || k == CardDataMigration.FormatKey) PlayerPrefs.SetInt(k, b.i);
            else PlayerPrefs.SetString(k, b.s);
        }
        PlayerPrefs.Save();
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "tour.txt"), log.ToString());
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "done.txt"), System.DateTime.Now.ToString());
        EditorApplication.isPlaying = false;
    }

    IEnumerator HideDiagnostics()
    {
        var fd = typeof(FreezeDiagnostics).GetField("showOverlay", BindingFlags.NonPublic | BindingFlags.Static);
        var bd = typeof(BossDiagnostics).GetField("showSnapshotOverlay", BindingFlags.NonPublic | BindingFlags.Static);
        while (true) { fd?.SetValue(null, false); bd?.SetValue(null, false); yield return null; }
    }
}

public static class FusionVisualTourMenu
{
    [MenuItem("Tools/OneMoreMile/Fusion Visual Tour")]
    public static void Run()
    {
        EditorPrefs.SetInt("FusionVisualTour", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif
