using UnityEngine;

// カードVisual最終調整依頼(2026-09-18), item2/3 - カード左上のCategory
// Iconを保持する。CardRarityFrames.cs(★1-5のRarity Frame)と全く同じ
// 「Resources.Loadでランタイム遅延読み込み+一度だけキャッシュ」パターン
// を踏襲した - Editor専用のSceneBuilder.Build()からstaticフィールドへ
// 直接書き込んでも実機/Play Modeには反映されない(過去のRarity Frame不具合
// と同じ原因)ため、Assets/Resources/CardCategoryIcons/配下に置き、実行時
// にここから読む。
public static class CardCategoryIcons
{
    static bool loaded;
    static Sprite movement;
    static Sprite attack;
    static Sprite defense;
    static Sprite growth;
    static Sprite heal;
    static Sprite special;
    static Sprite risk;

    static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        movement = Resources.Load<Sprite>("CardCategoryIcons/Icon_Movement");
        attack = Resources.Load<Sprite>("CardCategoryIcons/Icon_Attack");
        defense = Resources.Load<Sprite>("CardCategoryIcons/Icon_Defense");
        growth = Resources.Load<Sprite>("CardCategoryIcons/Icon_Growth");
        heal = Resources.Load<Sprite>("CardCategoryIcons/Icon_Heal");
        special = Resources.Load<Sprite>("CardCategoryIcons/Icon_Special");
        risk = Resources.Load<Sprite>("CardCategoryIcons/Icon_Risk");
    }

    // 未生成の間(素材が無いカテゴリ)はnullを返す - RewardCardUI側は
    // Image.sprite=nullを許容し、その場合はCategoryBadge自体を隠す
    // (空の菱形だけが残らないようにする)。
    public static Sprite GetIcon(CardCategory category)
    {
        EnsureLoaded();
        switch (category)
        {
            case CardCategory.Movement: return movement;
            case CardCategory.Attack: return attack;
            case CardCategory.Defense: return defense;
            case CardCategory.Growth: return growth;
            case CardCategory.Heal: return heal;
            case CardCategory.Special: return special;
            case CardCategory.Risk: return risk;
            default: return null;
        }
    }
}
