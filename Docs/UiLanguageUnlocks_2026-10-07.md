# UI improvements, language support, initial state and unlock conditions (2026-10-07)

## 1. Gacha: pulling repeatedly (`GameManager.Gacha.cs`, commit 10cfcab)

### How a pull works
- **One tap = one pull, done on the spot.** Paying 500 MILE and adding the card both happen in the same frame. The game does not wait for the animation.
- **Taps during the animation or while a card is shown also count, one at a time.** Pulled cards wait in a queue (`gachaRevealQueue`) and are shown one by one.
- **No double charge, no free card, no negative MILE.**
  - Payment and the new card always happen together.
  - A tap you can't afford is refused on the spot and shows "MILE不足" (not enough MILE).
- **Cards are already owned once pulled.** Closing the card view or leaving the screen does not lose them; only the remaining reveal animations are skipped.

### The card view
- **Bigger:** card art, name, ★ rarity and effect text, scaled to screen size.
- **The panel sits to the left of the machine.** The machine stays visible and tappable on top.

| Tap | What happens |
|---|---|
| The gacha machine | Closes the card view and pulls the next card |
| Anywhere else | Closes the card view only (taps never reach the Home buttons behind it) |

- The tap that was held down when a card appeared does not close it. Only a new tap after it appears does.
- Gamepad / keyboard: choose OK/NEXT or the machine.

**Test:** `-qaGacha`, all checks passed.
- 8 taps with MILE for 5 pulls → exactly 5 pulls and 3 refusals.
- MILE spent exactly 5 times; exactly 5 cards added.
- The 5 cards are shown one at a time.
- The tap that showed a card does not close it; a tap outside closes it and is swallowed.
- Tapping the machine pulls the next card.
- Cards not yet shown are still owned after starting a run.

## 2. CONTINUE: automatic restart and 5-second speed-up (`ResumeAccel.cs`, commit 6c9b6a7)

### Restart and speed-up
- The "準備ができたら再開" button and the 3-2-1 countdown were removed.
- Once the restore finishes (screen transition plus a few frames for the camera to catch up), the run **starts on its own**.
- The run speeds up over **5 seconds of game time**, from the normal run start speed to the **speed at the moment of saving**. The target is the speed at save, not the run's top speed.
- Attacks and jumps work during the speed-up.
- While the game is stopped (pause, card choice, HitStop), `Time.deltaTime` is 0, so the 5-second timer stops too.
- The speed-up is only a cap on the normal speed, so the end joins the normal speed with no jump. Measured worst change: 0.45 km/h per frame. Card effects are applied once only, as normal.

### How the speed is saved
- Natural acceleration depends on the raw distance run. Distance covered during boss fights is excluded from the recorded distance.
- Before this change, CONTINUE returned the player to the recorded distance, so the speed dropped.
- Now the checkpoint also saves the **raw distance at the moment of saving** (`speedDistance`) and the speed (`savedSpeedKmh`).
- After restarting, only the natural acceleration uses the difference. Distance, rewards and gates are unaffected.
- If the run is interrupted again during the speed-up, the checkpoint is not overwritten, so the next CONTINUE targets the same speed.
- **Old saves** (from before this change, `speedDistance` = 0): the speed is worked out from the checkpoint distance, as before.
- **Temporary boosts and slowdowns:** CONTINUE has never saved them. They don't carry over and nothing comes back.
- **Safe footing at the restart point:** the existing pit protection is kept. The safe stretch is now measured using the speed at save (the faster, safer value).

**Not changed:** arrival from a sprint departure still uses the ready screen and button. Only CONTINUE was in scope for this change.

**Test:** `-qaResume`, all checks passed.

| Case | Speed (km/h) |
|---|---|
| A, 600 m | 18.0 → 23.1 |
| B, 8 km with 3× SPEED UP | 19.6 → 109.0 |
| C, with ease-in | same as B |
| R, interrupted again during the speed-up | target unchanged |
| O, old save | — |
| D, new run | no speed-up |

Footing on all 4 maps was also checked.

## 3. Deck of 12 (commit ea861a0)
- `GameManager.DeckCapacity = 12`. Editing, saving, loading, departure and in-run lookup all use the same value.
- A new player's starting deck is the first 12 unlocked cards.
- **Edit screen:** the scene has 10 deck slots built in, so the extra slots are copied at runtime. The deck area is 4 columns × 3 rows, scaled to fit (0.66×), so **all 12 cards are visible at once**. Card details appear in the middle panel when a card is tapped.
- **Departing with fewer than 12:** as before, decks from 0 to 12 cards can depart.
- **Migrating old decks:**
  - A 10-card deck loads unchanged (not filled automatically). The first time at Home, the player sees "デッキが12枚になりました … 追加できます", once only.
  - A deck over 12 keeps the first 12. The cards that didn't fit stay owned, and the player is asked to re-edit the deck.
- **Test:** `-qaDeck`, all checks passed. Covers the new deck (12), 12 slots, no 13th card, save/reload, an old 10-card deck plus notice, and a 14-card deck.

## 4. New-player state and unlock conditions (`Save/UnlockRules.cs`, commit ea861a0)

### Maps
| Map | Unlock |
|---|---|
| Wasteland Road | from the start |
| Natural Cave | reach 30,000 m on Wasteland Road |
| Sky Corridor | reach 30,000 m on Natural Cave |
| Arena | reach 30,000 m on Sky Corridor (Home button shows "闘技場 (LOCK)"; tapping it shows the condition) |
| Last dungeon | meet the Reaper on all 3 normal maps **and** 1,000,000 m lifetime distance. **Hidden before unlock:** no slot, no ???, no condition. Even the tutorial doesn't mention it |

### Characters
| Character | Unlock |
|---|---|
| Swordsman | from the start |
| Dual Blade / Gunslinger / Dragon Lancer | Wasteland Road 30k / 50k / 100k |
| Ranger / Sorceress / Brawler | Natural Cave 30k / 50k / 100k |
| Shinobi / Shrine Maiden / Vampire | Sky Corridor 30k / 50k / 100k |
| Dragonkin | unlocked together with the last dungeon (hidden from the list until then) |
| Noble Lady | a real game over at 1,000 m or less on any run map (exactly 1,000 m counts; retiring and the arena don't) |

### Rules
- **Distances count within a single run.**
  - A run continued with CONTINUE is the same run.
  - Unlocks are saved the moment the distance is reached and are never taken back by a later game over.
- **What doesn't count toward unlocks:** the arena, the practice, Debug Runs, and dev-build runs that skipped distance with a warp.
- **Reaper encounter:** recorded per map when the Reaper fight on Wasteland Road, Natural Cave or Sky Corridor starts. Defeating her is not needed.
- **1,000,000 m lifetime distance:** distance actually run on the 3 normal maps, including runs that ended in a game over.
  - **Fix:** CONTINUE used to add the stretch from the checkpoint to the run's furthest point a second time. It now adds only newly covered ground.
  - Last-dungeon distance no longer counts.
  - Sprint-departure distance isn't actually run, so it doesn't count either.
- **Notices:** handled by `NoticeQueue`.
  - During a run: a small banner that doesn't stop the game ("UNLOCKED: …").
  - At Home: one notice at a time, each confirmed with OK, so several unlocks at once don't overlap.
  - Notices not yet confirmed are queued again at startup, so they survive a crash.
- **Last-dungeon reveal:** the first time at Home after it unlocks, 「走り続けたあなたへ。最後の道が開かれました。」 is shown once; the Dragonkin notice follows. The unlock and the "reveal shown" flag are saved separately.
- Locked maps and characters show their condition and progress ("最高 12,345m / 30,000m"). Locked characters can't be selected; the arena lets you try them as "試用" (trial).

### Saved items (progress, kept separately per data set)
`UnlockRulesV1`, `UnlockedStagesV1`, `UnlockedCharsV1`, `UnlockNotifiedV1`, `LastDungeonRevealShown`, `ReaperMapV1_<map>` ×3, `RunReachV1_<map>` ×3.
Dev only: `Dev.UnlockAll` — a DEBUG → test-data page toggle that makes everything selectable without changing the real unlock state.

### Migrating existing saves (this is not a reset)
- Saves with lifetime distance or BEST above 0 were already able to use every map and character, because no unlock system existed.
  - They keep **all maps, the arena and all characters unlocked**, nothing is taken away.
  - No unlock notices are shown.
- The last dungeon follows its existing unlock flag.
  - The "always selectable" switch in dev builds is a debug feature, kept separate from the real unlock state.
- **What can't be restored:** per-map Reaper encounters.
  - Until now only per-sister encounters were recorded, and all three sisters also appear in the last dungeon.
  - So which map a sister was met on can't be told, and **no per-map value is invented**.
- Progress display: the per-map BEST was copied into `RunReachV1` (only values known for certain).
- Lifetime distance is unchanged. Distance already counted can't be separated by map, so the existing total is kept as is.

**Test:** `-qaUnlock`, all checks passed.
- **New data:** only Wasteland Road and Swordsman available; nothing about the last dungeon or Dragonkin appears in the UI.
- **30,000 m threshold:**
  - 29,960 m → passing 30,000 m unlocks the Cave and Dual Blade at that moment.
  - The unlock survives a game over and a reload.
  - The two notices appear one after another.
- **Excluded runs:** runs that used the warp, and Debug Runs, don't unlock anything.
- **Exact boundaries:** 29,999.9 m doesn't unlock; exactly 30,000 m does.
- **Noble Lady:** retiring at 500 m doesn't unlock her, nor does a game over at 1,000.5 m. A real game over under 1,000 m does.
- **Last dungeon:**
  - 1,000,000 m + Reaper on 2 maps → locked.
  - Reaper on 3 maps + 999,999 m → locked.
  - Both conditions met → unlocked, reveal shown once, Dragonkin notice, still unlocked after restart.
- **Reaper:** the actual Reaper appearance on Wasteland Road records Wasteland Road only.
- **Lifetime distance:** after CONTINUE, only the new stretch is added (+90 m vs 90 m newly run).
- **Migration:** everything stays unlocked; last dungeon and Reaper-per-map are not invented; no notices.

**Updated tests:**
- `qaSave` now checks the new last-dungeon rule.
- `qaTutorial` unlocks the Dual Blade before selecting it.
- Other QA modes acknowledge Home notices automatically.

## 5. Language setting and 30 languages (`Scripts/Localization/`)

### Mechanism
- **Languages (30):** ja, en, zh-Hans, zh-Hant, ko, id, ms, vi, th, fil, my, km, lo, hi, bn, ta, fr, de, es, pt-BR, it, pl, uk, ru, tr, cs, ro, ar, fa, sw.
  - The Language setting shows each language in its own script (日本語 / English / العربية …).
- **First launch:** follows the device language.
  - Android reads `java.util.Locale.toLanguageTag`; other platforms read `CultureInfo`.
  - Unsupported languages fall back to English.
- **Choosing a language:** the choice is saved as `Language` and takes priority from then on.
  - It is a setting, so normal data and test data share it.
  - It switches without a restart. Open screens (deck, fusion, character select, map select, settings) update on the spot.
- **Translation tables:** `Resources/Localization/<code>.json`, one per language.
  - Keys are the source text. Japanese is the main source; UI labels that were already in English use that English text as the key.
  - Lookup happens at the draw points (`Loc.Auto`):
    - `UiKit.Button` / `DrawStyledButton` / `LocGUI.Label` (every `GUI.Label` in player-facing files)
    - uGUI `.text =`
    - text baked into the scene (`LocTextBinder` retranslates on scene load and language change)
- **Matching order:**
  1. Exact match.
  2. Line by line.
  3. Templates with inserted values ({0}…; the inserted words themselves, such as card names, are translated too).
  4. Retry without surrounding decoration (« ▶ brackets) and spaces.
- **Missing translations:** fall back to English. Dev builds record them (`LocDebug`), along with "Japanese shown untranslated" and "English labels with no table entry". `-qaLoc` writes a report per language.
- **Long text:** non-wrapping labels shrink to fit their width (down to 60%); wrapping labels shrink to fit their height. uGUI uses bestFit with the original size as the maximum.
- **Arabic and Persian:** `ArabicShaper` builds the joined letter forms (initial, medial, final and isolated, plus the lam-alef ligature). Then each line is reordered right to left, keeping numbers and Latin words in their own direction and mirroring brackets.

### Translation work (the three stages kept separate)
1. **Translations prepared (AI drafts):**
   - 1,062 + 12 + 44 = 1,118 strings, including character flavor texts and extra labels.
   - The groups were translated in parallel following `Tools/loc/TRANSLATE.md` (meaning first for cards, placeholders and tags kept, game terms such as MILE/HP kept, length limits).
   - `merge.py` checks every line automatically: all IDs present, {n} placeholders and tags match, no kana left.
   - Result: **all 30 languages, 1,106 / 1,106 accepted** (ja holds 305 entries for the English keys only).
2. **Display check (done):** `-qaLoc` switches language and screenshots Home, settings, map select, character select, deck, practice, in-run and the results screen. "Japanese shown untranslated" was 0 on the main screens.
3. **Text quality review:** **not done.** Native speakers need to review it, especially:
   - card conditions, effects and drawbacks
   - Burmese, Khmer, Lao, Bengali and Tamil

### Known limits / not done
- **Complex-script shaping:** Unity's legacy text (IMGUI / uGUI Text) does not join or shape Indic, Burmese, Khmer or Lao scripts. Some conjuncts and vowel signs can render broken. Thai appeared readable in the screenshots.
  - Fixing this properly needs TextMeshPro plus HarfBuzz-based font assets, or another text system. That would mean bundling fonts for each language, with the font-size cost discussed below.
- **Fonts:** fonts are not bundled. Glyphs rely on the OS font fallback; Android usually has Noto. On Windows, all 30 languages showed glyphs in the screenshots.
- **Text baked into images:** some images contain English text (character card portrait names, the logo). They were not moved into text. The title "One More Mile" stays as is.
- **Long wrapped right-to-left paragraphs:** the order of wrapped lines can break. The longer texts already use \n line breaks.
- **Not translated (dev-only or by design):**
  - the dev DEBUG panel, test tools and logs
  - credit names
  - gacha stage names (only in Japanese mode)
  - English labels on some boss special-attack banners (translated or kept, per language)
