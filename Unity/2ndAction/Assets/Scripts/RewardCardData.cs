using UnityEngine;

// What one reward card needs to show - built by GameManager.MakeCardData
// from a CardDefinition, handed to RewardCardSequence/DeckEditUI. Purely a
// data carrier, no logic of its own. CardId (not the CardDefinition object
// itself) is what round-trips back through RewardCardSequence's onApply
// callback, since that's also what the deck/PlayerPrefs persistence uses.
public struct RewardCardData
{
    public string CardId;
    public Texture2D Icon;
    public string Title;
    public string Description;

    // Card UI / Rarity Frame pass - Rarity (1-5) picks which frame Sprite
    // RewardCardUI shows (see CardRarityFrames) and how many ★ RarityText
    // renders; 0 means "no card" and is treated as Rarity 1 for frame
    // purposes so an unset value never looks broken.
    public int Rarity;
    // Pre-formatted by whichever caller builds this data (same pattern as
    // Title/Description already use) - e.g. "Lv.2 -> Lv.3" for a Level Up/
    // Boss Reward choice, "Lv.3 x2" for Collection/Fusion, "Lv.3" for
    // Character Card/Gacha Result. Empty/null hides the Level row entirely
    // (RewardCardUI.ShowEmpty/ShowBack, and slots like a Deck card whose
    // owned Lv isn't meaningful here).
    public string LevelLine;
    // Item 5 - "EQUIPPED" badge, shown only where the caller actually knows
    // equip state (Character Card slots / Collection); false everywhere
    // else rather than re-deriving it from CardId at display time.
    public bool ShowEquippedBadge;

    // Card UI改修(2026-09-08) - 所持枚数を「×N」として常に固定位置に表示
    // するための専用フィールド。以前はLevelLine文字列に"Lv.3 x2"のように
    // 埋め込んでいたが、新デザインではLv(右上固定)とCount(タイトル帯右端
    // 固定)を別々の場所に表示する仕様のため分離した。0以下は「表示しな
    // い」(単発所持、または所持枚数の概念がない画面向け)。
    public int Count;

    // レベルアップ選択UI改修(2026-09-11) - 横長3択UI(LevelUpChoiceRowUI)
    // の右端に出す「主要な強化数値」(例: "+10%"/"+3")。CardEffectFormat.
    // FormatPrimaryValueで呼び出し側(GameManager.MakeChoiceCardData)が
    // 事前に文字列化する(Title/Descriptionと同じ「呼び出し側が整形済み
    // 文字列を渡す」方針)。空文字なら数値欄を表示しない(表示できない/
    // 複数効果すぎて代表値が出せないカード向け)。
    public string ValueLine;

    // カードVisual最終調整依頼(2026-09-18), item3 - 左上のCategory Icon用。
    // CardDefinition.categoryをそのまま渡す(表示可否はRewardCardUI側で
    // CardIdの有無等ではなくisFrontだけを見て判断するため、値自体は常に
    // 渡してよい)。
    public CardCategory Category;
    // item1 - 「実際に初めて取得したが、まだプレイヤーが確認していない」
    // カードにのみtrue。CardInventory.IsNewUnconfirmedを呼び出し側
    // (GameManager.MakeOwnedCardData)が確認して渡す - Level Up/Boss
    // Reward候補(MakeChoiceCardData)では意図的に渡さない(常にfalseの
    // まま)。
    public bool ShowNewBadge;
}
