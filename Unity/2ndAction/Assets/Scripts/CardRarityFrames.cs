using UnityEngine;

// Card UI / Rarity Frame pass - holds the 5 Rarity-specific Frame Sprites
// (★1-★5). RewardCardUI reads from here at SetContent() time so every
// screen that already reuses RewardCardUI (Level Up/Boss Reward/
// Collection/Character Card/Deck/Fusion/Gacha Result) gets Rarity-correct
// frames automatically, with no per-screen wiring beyond what
// CreateRewardCard already does.
//
// Bugfix 2026-09-06, item 3 - "Rarity Frameが反映されていない". Root cause:
// the first version of this class populated its Sprite[] from
// SceneBuilder.Build() (an EDITOR-ONLY batchmode process), but a plain
// static C# field is never serialized anywhere - writing to it from the
// Editor process has zero effect on the actual game process (Play mode or
// a real device build), which starts with a completely fresh, empty
// array every time. Every card, everywhere, was silently always falling
// back to the old CardFrame.png - exactly the symptom reported. Fixed by
// loading lazily at RUNTIME via Resources.Load instead (the same pattern
// CardDatabase.Load() already uses for CardDefinition assets), which is
// why the 4 usable frame PNGs live under Assets/Resources/CardFrames/ -
// Resources.Load can only ever find assets physically inside a folder
// literally named "Resources".
public static class CardRarityFrames
{
    // Index 0 unused - index N holds Rarity N's frame (1-5). null until
    // first accessed (EnsureLoaded), then cached for the rest of the
    // process - same lifecycle as CardDatabase.cachedCards.
    static Sprite[] frames;

    // Bugfix note (Card UI / Rarity Frame pass) - the ★1 source image THEN
    // supplied had NO real alpha channel (Format24bppRgb) - its
    // "transparent" interior was a checker pattern baked in as opaque gray
    // pixels, the same class of issue as the earlier Death.jpg problem this
    // project already hit once, and too risky to algorithmically recover.
    // Card UI改修(2026-09-08) - a proper transparent ★1 export now exists
    // (CardFrameRarity1.png, verified via PowerShell/System.Drawing to have
    // real alpha: A=0 at the corners/center, A≈253 on the border art), so
    // Rarity 1 finally has its own entry here like every other tier.
    static void EnsureLoaded()
    {
        if (frames != null) return;
        frames = new Sprite[6];
        for (int r = 1; r <= 5; r++)
        {
            frames[r] = Resources.Load<Sprite>($"CardFrames/CardFrameRarity{r}");
        }
    }

    public static Sprite GetFrame(int rarity, Sprite fallback)
    {
        EnsureLoaded();
        int r = Mathf.Clamp(rarity <= 0 ? 1 : rarity, 1, 5);
        Sprite s = frames[r];
        return s != null ? s : fallback;
    }
}
