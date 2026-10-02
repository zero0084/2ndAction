using UnityEngine;
// Card Expansion/Gacha Evolution Ver.1 - BEST-Distance-driven Gacha growth
// (item 10/11). Stage thresholds are centralized here so GameManager
// (stage computation), the Home Room Gacha display, and the draw-pool
// filtering logic all agree on the exact same breakpoints. Deliberately a
// pure function of BestDistance (no separate Stage save field - see the
// brief's own "Gacha StageはBEST Distanceから再計算可能な構造でも構いま
// せん", taken literally here).
public static class GachaStage
{
    public const int StageCount = 5;
    // BEST Distance (meters) required to REACH each stage - index 0 is
    // Stage 1 (always available, "0m〜").
    public static readonly float[] Thresholds = { 0f, 5000f, 20000f, 50000f, 100000f };
    public static readonly string[] StageNames = { "OLD", "REPAIRED", "MECHANICAL", "MAGICAL", "DEATH-TOUCHED" };

    // 1-based stage number for a given BEST Distance.
    public static int StageForDistance(float bestDistance)
    {
        int stage = 1;
        for (int i = 1; i < Thresholds.Length; i++)
        {
            if (bestDistance >= Thresholds[i]) stage = i + 1;
        }
        return stage;
    }

    // The BEST Distance needed to reach the stage just above `currentStage`,
    // or -1 once already at the max stage.
    public static float NextEvolutionDistance(int currentStage)
    {
        if (currentStage >= StageCount) return -1f;
        return Thresholds[currentStage];
    }

    // Item 11 - cumulative pool: a card is eligible once ITS OWN
    // unlockDistance/gachaStage are both satisfied - never removed from
    // the pool once unlocked, regardless of how far the stage grows past
    // it ("旧Cardを排出Poolから削除しないでください").
    public static bool IsCardEligible(CardDefinition card, float bestDistance, int currentStage)
    {
        if (DevAllCardsOpen) return true; // 開発版: 全カード開放(2026-10-02)
        return bestDistance >= card.unlockDistance && currentStage >= card.gachaStage;
    }

    // 開発版だけ: 距離/ガチャ段階に関係なく全カードを「解放済み」にする(ガチャの候補・解放済みカードの一覧)。製品版では常にfalse
    public const string DevAllCardsOpenKey = "Dev.AllCardsOpen";
    public static bool DevAllCardsOpen
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return Debug.isDebugBuild && PlayerPrefs.GetInt(DevAllCardsOpenKey, 0) == 1;
#else
            return false;
#endif
        }
        set
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            PlayerPrefs.SetInt(DevAllCardsOpenKey, value ? 1 : 0); PlayerPrefs.Save();
#endif
        }
    }
}
