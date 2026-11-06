using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// マップ分類による解禁(2026-11-06、マスターの依頼 4/5)。分類(CardMapCategory)は「そのマップで解禁する場所」。
//  ・共通のカード: 今までどおり(BEST の距離 ≥ unlockDistance かつ ガチャの段階 ≥ gachaStage。GachaStage.IsCardEligibleByDistance)
//  ・マップのカード: 今までの条件に加えて「そのマップで、成功したラン(帰還)の到達距離 ≥ max(unlockDistance, 入口の距離)」
//      入口の距離: 荒野街道 0m(最初のマップなので今までと同じ)/ 自然洞窟・天空回廊 1,000m(最初のボスの関門)
//      到達距離は UnlockRules.Reach(マップ別、成功したランだけ確定。既存のデータはマップ別BESTから作ってある)
//  ・ラストダンジョンのカード: 今までの条件に加えて、ラストダンジョンが解放されていること(ProgressStats.FinalDungeonUnlocked)
//  ・一度解禁したカードは、どのマップでも通常どおりガチャ/デッキで使える(マップ専用にはしない)。抽選の重みはレア度のまま(隠れて下げない)
//  ・既存のデータ: この仕組みの前に使えていた(今までの条件を満たしていた)カードと、所持しているカードは解禁済みのまま(取り上げない)。
//    起動時に一度だけ GrandfatherKey へ書く(SaveSystem.Boot → EnsureInitialized)。新しいデータは空(最初から分類どおり)
//  ・初期デッキ(DefaultSave.StartingDeck)は今までの条件で作る(分類で変わらない)。初期デッキのカードは所持しているので使えるが、
//    マップのカードなら、ガチャで重ねて引けるのはそのマップで解禁してから
public static class CardMapUnlock
{
    public const string InitKey = "CardMapUnlockV1";             // 仕組みを入れた印(int 1)
    public const string GrandfatherKey = "CardMapGrandfatherV1"; // 前から使えていたので解禁済みのままにするカード(カンマ区切り)
    public const float CaveSkyEntryMeters = 1000f;

    static HashSet<string> grand;
    static HashSet<string> Grand
    {
        get
        {
            if (grand != null) return grand;
            grand = new HashSet<string>();
            foreach (var p in SaveStore.GetString(GrandfatherKey, "").Split(',')) if (!string.IsNullOrEmpty(p)) grand.Add(p.Trim());
            return grand;
        }
    }
    public static void Reload() { grand = null; }
    public static bool IsGrandfathered(string cardId) => !string.IsNullOrEmpty(cardId) && Grand.Contains(cardId);
    public static bool QaDisable; // 確認用(今までの条件だけで比べる)

    // 解禁に要るマップの到達距離(Common は 0、ラスダンは -1 = 距離ではなく解放)
    // カードごとの到達距離(分類の入口より奥で解禁したいカード)。2026-11-06 マスター決定: VAMPIRE は洞窟の中盤〜後半
    public static readonly Dictionary<string, float> ReachOverride = new Dictionary<string, float> { { "vampire", 30000f } };
    public static float RequiredMeters(CardDefinition c)
    {
        var k = CardMapCategories.Of(c);
        if (c != null && k != CardMapCategory.Common && k != CardMapCategory.LastDungeon && ReachOverride.TryGetValue(c.cardId, out float ov)) return Mathf.Max(ov, c.unlockDistance);
        switch (k)
        {
            case CardMapCategory.Wilderness: return Mathf.Max(0f, c.unlockDistance);
            case CardMapCategory.Cave: case CardMapCategory.Sky: return Mathf.Max(CaveSkyEntryMeters, c.unlockDistance);
            case CardMapCategory.LastDungeon: return -1f;
            default: return 0f;
        }
    }

    // マップの条件だけ(今までの距離/段階の条件は GachaStage 側)
    public static bool MapGateOpen(CardDefinition c)
    {
        if (c == null || QaDisable) return true;
        var k = CardMapCategories.Of(c);
        if (k == CardMapCategory.Common) return true;
        if (IsGrandfathered(c.cardId)) return true;
        if (k == CardMapCategory.LastDungeon) return ProgressStats.FinalDungeonUnlocked;
        string stage = CardMapCategories.StageIdOf(k);
        return UnlockRules.Reach(stage) >= RequiredMeters(c) - 0.01;
    }

    // 画面/資料の「解禁の条件」(日本語の元の文。表示は Loc.Auto)。共通のカードは今までの条件の文
    public static string ConditionSrc(CardDefinition c)
    {
        if (c == null) return "";
        var k = CardMapCategories.Of(c);
        if (k == CardMapCategory.LastDungeon) return LocCardCommon.UnlockLastDungeon;
        if (k == CardMapCategory.Common) return "";
        float m = RequiredMeters(c);
        string stage = CardMapCategories.StageIdOf(k);
        return Loc.Src(LocCardCommon.UnlockOnMap, UnlockRules.StageName(stage), m.ToString("N0", CultureInfo.InvariantCulture));
    }

    // 解禁済みか(ガチャの候補/デッキの解放の一覧と同じ判定)。UI はこれを読む
    public static bool IsUnlocked(CardDefinition c)
    {
        if (c == null) return false;
        var gm = GameManager.Instance;
        float best = gm != null ? gm.BestDistance : SaveStore.GetFloat("BestDistance", 0f);
        return GachaStage.IsCardEligible(c, best, GachaStage.StageForDistance(best));
    }

    // 起動時(SaveSystem.Boot)。一度だけ: 遊んだ形跡のあるデータは、今までの条件で使えたカード + 所持カードを解禁済みのままにする
    public static string EnsureInitialized()
    {
        if (SaveStore.HasKey(InitKey)) return "";
        SaveStore.SetInt(InitKey, 1);
        bool played = ProgressStats.ReadDouble(SaveKeys.LifetimeDistance) > 0.0 || SaveStore.GetFloat("BestDistance", 0f) > 0f;
        var ids = new List<string>();
        if (played)
        {
            float best = SaveStore.GetFloat("BestDistance", 0f);
            int stage = GachaStage.StageForDistance(best);
            foreach (var c in CardDatabase.AllCards)
            {
                if (c == null || CardMapCategories.Of(c) == CardMapCategory.Common) continue;
                if (GachaStage.IsCardEligibleByDistance(c, best, stage) || OwnedInSave(c.cardId)) ids.Add(c.cardId);
            }
        }
        SaveStore.SetString(GrandfatherKey, string.Join(",", ids));
        Reload();
        return played ? $"card map unlock: existing player -> kept {ids.Count} map cards unlocked; " : "card map unlock: new data -> map cards unlock on their maps; ";
    }

    // 所持カード(保存の JSON に cardId が入っているか。v2 キーは主カードの ID も入る)
    static string ownedJson;
    static bool OwnedInSave(string cardId)
    {
        if (ownedJson == null) ownedJson = SaveStore.GetString("OwnedCardsV1", "");
        return !string.IsNullOrEmpty(ownedJson) && (ownedJson.Contains("\"" + cardId + "\"") || ownedJson.Contains("|" + cardId + "|"));
    }
}
