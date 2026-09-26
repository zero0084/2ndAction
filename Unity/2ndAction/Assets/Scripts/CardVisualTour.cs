#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// カードUI(Collection/Deck/詳細パネル/Level Up選択)の目視確認用ツアー(Editor専用、GUIのEditorで実行)。
// CardShots/ へ撮影する。所持カード/デッキ/キャラカード/MILE/NEW状態は退避して撮影後に元へ戻す。
public class CardVisualTour : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("CardVisualTour", 0) != 1) return;
        EditorPrefs.SetInt("CardVisualTour", 0);
        new GameObject("CardVisualTour").AddComponent<CardVisualTour>();
    }

    static readonly string[] Keys = { CardInventory.SaveKey, "DeckCardIds", "CharacterCardSlots", "TotalOwnedMile", CardDataMigration.FormatKey, "NewUnconfirmedCardsV1" };
    readonly Dictionary<string, (bool has, string s, int i)> backup = new Dictionary<string, (bool, string, int)>();
    string dir;
    readonly System.Text.StringBuilder log = new System.Text.StringBuilder();

    // 名前の長さ/カテゴリ/レア度がばらけるように選んだ確認用カード
    static readonly string[] Showcase =
    {
        "speed_up", "greed", "jump_count_up", "jump_power_up", "attack_up", "attack_range_up",
        "treasure_hunter", "berserker", "vampire", "sky_runner", "attack_speed_up", "experience_burst",
    };

    void Shot(string name) => ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png"));

    IEnumerator ShotAfterSettle(string name)
    {
        yield return new WaitForSecondsRealtime(0.3f);
        Shot(name);
        yield return new WaitForSecondsRealtime(0.35f);
    }

    static string StackKey(string baseId)
    {
        foreach (var s in CardInventory.Stacks)
            if (s.cardId == baseId || s.cardId.Contains("|" + baseId + "|")) return s.cardId;
        return baseId;
    }

    IEnumerator Start()
    {
        dir = System.IO.Path.Combine(Application.dataPath, "../CardShots");
        System.IO.Directory.CreateDirectory(dir);
        yield return new WaitForSeconds(1.2f);
        var gm = GameManager.Instance;
        foreach (var k in Keys) backup[k] = (PlayerPrefs.HasKey(k), PlayerPrefs.GetString(k, ""), PlayerPrefs.GetInt(k, 0));
        StartCoroutine(HideDiagnostics());

        // ---- 撮影用の所持カード ----
        CardInventory.ResetAll();
        gm.SetDeck(new string[0]);
        var ids = new List<string>();
        foreach (var id in Showcase) if (CardDatabase.FindById(id) != null) ids.Add(id);
        foreach (var c in CardDatabase.AllCards) if (ids.Count < 30 && !ids.Contains(c.cardId)) ids.Add(c.cardId);
        int[] levels = { 1, 4, 2, 9, 3, 5, 1, 7, 2, 1, 6, 3 };
        for (int i = 0; i < ids.Count; i++) CardInventory.AddCard(ids[i], i < levels.Length ? levels[i] : 1, 1 + i % 2);
        // レア度★1〜★5のフレーム比較用(同じ能力で★だけ違う合成カード)
        for (int r = 1; r <= 5; r++)
        {
            var v = new CardVariant { mainId = "attack_speed_up", level = r, rarity = r };
            v.AddAbility("attack_speed_up", r);
            CardInventory.AddCard(v.ToKey(), r, 1);
        }
        // NEW表示の確認: GREED以外は確認済みにする
        string greedKey = StackKey("greed");
        foreach (var s in new List<CardInventory.Stack>(CardInventory.Stacks)) if (s.cardId != greedKey) CardInventory.ClearNewUnconfirmed(s.cardId);
        gm.AddToDeck(StackKey("attack_up"));
        gm.AddToDeck(StackKey("treasure_hunter"));
        gm.AddToDeck(StackKey("speed_up"));
        gm.AddToDeck(StackKey("jump_count_up"));
        bool eq = gm.EquipCharacterCard(0, StackKey("sky_runner"), 1);
        log.AppendLine($"equip={eq}");

        var ui = gm.deckEditUI;
        gm.OpenDeckEdit();
        yield return new WaitForSecondsRealtime(1.5f);
        log.AppendLine($"screen={Screen.width}x{Screen.height}");
        yield return ShotAfterSettle("01_list");

        foreach (var (key, label) in new[] { ("attack_range_up", "02_attack_range_up"), ("berserker", "03_berserker"), ("greed", "04_greed"), ("treasure_hunter", "05_treasure_hunter") })
        {
            int idx = ui.DebugIndexOfOwned(key);
            log.AppendLine($"{key} idx={idx}");
            ui.DebugFocusOwned(idx);
            ScrollTo(ui, idx);
            yield return ShotAfterSettle(label);
        }

        // ★1〜★5の比較(同じ能力でレア度だけ違う合成カード)
        ScrollTo(ui, ui.DebugIndexOfOwned("attack_speed_up"));
        yield return ShotAfterSettle("06_rarity_lineup");
        ui.ownedScrollRect.verticalNormalizedPosition = 0f;
        yield return ShotAfterSettle("07_list_bottom");
        ui.ownedScrollRect.verticalNormalizedPosition = 1f;

        // 実際のタップ処理(デッキ追加→選択状態、デッキ側タップ→削除)
        var tapOwned = typeof(DeckEditUI).GetMethod("OnOwnedCardTapped", BindingFlags.NonPublic | BindingFlags.Instance);
        var tapDeck = typeof(DeckEditUI).GetMethod("OnDeckSlotTapped", BindingFlags.NonPublic | BindingFlags.Instance);
        int before = gm.DeckCards.Count;
        tapOwned.Invoke(ui, new object[] { ui.DebugIndexOfOwned("vampire") });
        yield return ShotAfterSettle("08_tap_add_vampire");
        int afterAdd = gm.DeckCards.Count;
        tapDeck.Invoke(ui, new object[] { 0 });
        yield return ShotAfterSettle("09_tap_remove_deck0");
        log.AppendLine($"deck before={before} afterAdd={afterAdd} afterRemove={gm.DeckCards.Count}");

        // 裏面: 表面専用のCategory/Lv/Name Plateが残らないこと
        var back = ui.detailPreviewCard;
        if (back != null)
        {
            back.ShowBack();
            yield return ShotAfterSettle("10_preview_back");
            log.AppendLine($"back: levelBadge={back.levelBadge.activeSelf} categoryBadge={back.categoryBadge.activeSelf} namePlate={(back.namePlate != null && back.namePlate.activeSelf)} title={back.titleText.enabled}");
            StartCoroutine(back.FlipToFront(0.6f));
            yield return ShotAfterSettle("11_preview_flipped");
        }

        // 他の画面比率(ルートを中央へ寄せて16:9/4:3を再現)
        var rootRect = (RectTransform)ui.root.transform;
        var canvasRect = (RectTransform)rootRect.parent;
        float h = canvasRect.rect.height;
        foreach (var (name, aspect) in new[] { ("16x9", 16f / 9f), ("4x3", 4f / 3f) })
        {
            float w = h * aspect;
            float inset = Mathf.Max(0f, (canvasRect.rect.width - w) / 2f);
            rootRect.offsetMin = new Vector2(inset, 0); rootRect.offsetMax = new Vector2(-inset, 0);
            yield return null;
            yield return ShotAfterSettle("12_list_" + name);
        }
        rootRect.offsetMin = rootRect.offsetMax = Vector2.zero;

        // ---- Level Up選択(同じカード部品の裏→表のめくり) ----
        ui.root.SetActive(false);
        gm.CloseDeckEdit();
        yield return new WaitForSecondsRealtime(0.5f);
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float t0 = Time.realtimeSinceStartup;
        while (!(gm.HasStarted && !gm.CountdownActive) && Time.realtimeSinceStartup - t0 < 12f) yield return null;
        gm.DebugSetInvincible(true);
        yield return new WaitForSecondsRealtime(1.0f);
        typeof(GameManager).GetMethod("RunLevelUpChoice", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float st = Time.realtimeSinceStartup;
        float[] at = { 0.15f, 0.45f, 0.8f, 1.2f, 1.7f, 2.4f };
        for (int i = 0; i < at.Length; i++)
        {
            while (Time.realtimeSinceStartup - st < at[i]) yield return null;
            Shot($"13_levelup_{i}");
            yield return null;
        }
        float wt = Time.realtimeSinceStartup;
        while (!gm.IsRewardSequenceWaitingForSelection && Time.realtimeSinceStartup - wt < 8f) yield return null;
        yield return ShotAfterSettle("14_levelup_waiting");
        var seq = FindFirstObjectByType<RewardCardSequence>();
        if (seq != null)
        {
            seq.OnCardClicked(1);
            yield return ShotAfterSettle("15_levelup_selected");
            seq.OnCardClicked(1);
            yield return new WaitForSecondsRealtime(1.5f);
        }

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

    // 一覧の指定カードが見える位置までスクロールする
    static void ScrollTo(DeckEditUI ui, int idx)
    {
        if (idx < 0 || idx >= ui.ownedCards.Length) return;
        var sr = ui.ownedScrollRect;
        Canvas.ForceUpdateCanvases();
        RectTransform card = ui.ownedCards[idx].rect;
        float contentH = sr.content.rect.height, viewH = sr.viewport.rect.height;
        float top = -card.anchoredPosition.y - card.rect.height * 0.5f - 30f;
        sr.verticalNormalizedPosition = 1f - Mathf.Clamp01(top / Mathf.Max(1f, contentH - viewH));
    }

    IEnumerator HideDiagnostics()
    {
        var fd = typeof(FreezeDiagnostics).GetField("showOverlay", BindingFlags.NonPublic | BindingFlags.Static);
        var bd = typeof(BossDiagnostics).GetField("showSnapshotOverlay", BindingFlags.NonPublic | BindingFlags.Static);
        while (true) { fd?.SetValue(null, false); bd?.SetValue(null, false); yield return null; }
    }
}

public static class CardVisualTourMenu
{
    [MenuItem("Tools/OneMoreMile/Card Visual Tour")]
    public static void Run()
    {
        EditorPrefs.SetInt("CardVisualTour", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif
