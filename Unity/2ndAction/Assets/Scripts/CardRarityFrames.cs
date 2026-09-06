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

    // Bugfix note (Card UI / Rarity Frame pass) - the ★1 source image
    // supplied for this pass has NO real alpha channel (Format24bppRgb) -
    // its "transparent" interior is a checker pattern baked in as opaque
    // gray pixels, the same class of issue as the earlier Death.jpg
    // problem this project already hit once. Unlike that case, attempting
    // to algorithmically recover transparency here was judged too risky
    // (the checker pixels aren't a clean flat 2-color pattern - they carry
    // soft shading/noise - so a color-threshold mask would just as easily
    // eat into the frame's own silver/gray border art). Rarity 1 has no
    // entry under Resources/CardFrames at all, so GetFrame's null check
    // below always falls back to the caller-supplied default (the
    // project's original CardFrame.png) for it - swap in a real
    // transparent ★1 export under Resources/CardFrames/CardFrameRarity1
    // the moment one exists, nothing else needs to change.
    static void EnsureLoaded()
    {
        if (frames != null) return;
        frames = new Sprite[6];
        for (int r = 2; r <= 5; r++)
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
