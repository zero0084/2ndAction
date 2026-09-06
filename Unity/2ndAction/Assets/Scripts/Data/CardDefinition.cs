using System.Collections.Generic;
using UnityEngine;

// One upgrade card's data - everything the level-up draw and the Deck Edit
// screen need. Built/updated as real .asset files under
// Assets/Resources/Cards/ by CardDatabaseBuilder (Tools/OneMoreMile/Build
// Card Database), so adding a new card is "add an entry to that builder
// (or duplicate an existing .asset and tweak it in the Inspector)", never
// "write a new switch case for the new card" - only a genuinely new
// EffectType needs code (GameManager.ApplyCardEffects).
[CreateAssetMenu(menuName = "OneMoreMile/Card Definition")]
public class CardDefinition : ScriptableObject
{
    // Stable identifier, persisted in the player's deck (PlayerPrefs) -
    // never change an existing card's id once players may have it in a
    // saved deck, or that entry will just silently drop out on load.
    public string cardId;
    public string cardName;
    public Texture2D icon;
    [TextArea] public string description;
    public CardCategory category;
    // Display order in the owned-cards grid / results history row - not
    // tied to cardId so cards can be reordered without renaming ids.
    public int sortOrder;
    // How eagerly DeckEditUI.BuildRecommendedDeck picks this card - higher
    // goes in first, ties broken by sortOrder. A simple hand-tuned weight,
    // not a "compute the optimal deck" score (see that method's comment):
    // straightforward stat-up cards sit high, pure-drawback/trade-off cards
    // (SPEED DOWN, BERSERKER, HEAVY ARMOR, GREED) sit low so the one-tap
    // "おすすめ編成" doesn't hand a new player a card with a downside they
    // didn't choose.
    public int recommendPriority = 5;
    // Almost always one entry; BERSERKER/HEAVY ARMOR/GREED use two to pair
    // a buff with its drawback, VAMPIRE uses two to separate its chance
    // from its heal amount.
    public List<CardEffect> effects = new List<CardEffect>();

    // ===== Card Expansion/Gacha Evolution Ver.1 additions ===== //
    // 1-5, matching Card Lv's own 1-5 range but a completely separate
    // concept - "Rarity = 単純な性能差ではありません" (see
    // CardDatabaseBuilder's Specs for the actual per-card assignment and
    // the design intent behind each tier). Purely descriptive/UI (star
    // display) plus a Gacha-pool weight lookup - no code branches on this
    // directly changing gameplay.
    public int rarity = 1;
    // BEST Distance (meters) required for this card to appear in the
    // Gacha's draw pool - deliberately a SEPARATE gate from UnlockManager/
    // UnlockDefinition (the existing Deck/Level-Up eligibility system,
    // left completely untouched) since Gacha eligibility and Deck
    // eligibility are two independent unlock tracks now.
    public float unlockDistance = 0f;
    // Which Gacha Stage's cumulative pool this card first joins (1-5,
    // matching GachaStage's own 1-5) - see GachaStage.IsCardInPool for how
    // this and unlockDistance combine (both must be satisfied).
    public int gachaStage = 1;
    // Data-only for now (see ElementType's own comment) - display/future
    // use, no gameplay hook reads this yet.
    public ElementType element = ElementType.None;

    // "★★★" style string for UI display (Rarity Visual, item 15) -
    // centralized here so every screen (Card Edit/Fusion/Gacha Result/
    // Character Card slots) renders identically without recomputing the
    // loop, and so a future Rarity display tweak is a one-line change.
    public string RarityStars => new string('★', Mathf.Clamp(rarity, 1, 5));
}
