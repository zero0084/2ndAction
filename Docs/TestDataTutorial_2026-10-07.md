# Test data and first-time tutorial (2026-10-07)

## 1. Test data (dev builds only)

### Where it lives
- Open it from DEBUG on the Home screen: the **テストデータ…** button at the top of page 1. It opens a page with these buttons:
  - **新規テストデータで開始** — confirmation: "Empty the test data and start in the same state as a new user. The normal data is not deleted."
  - **テストデータをリセット…** — confirmation: "Deletes only the test data. The normal data is not deleted."
  - **通常データへ戻る**
  - **前回のテストデータの続きで遊ぶ**
- You can switch only on the Home screen. During a run, in the arena, during the practice or in multiplayer, the page says "切り替えはホームでだけできます。ホームへ戻ってから開いてください".
- While test data is in use, a small red **TEST DATA** label is always shown at the top center of the screen.
- Release builds have none of this:
  - `SaveProfile.IsTest` is always false.
  - The DEBUG page and the label are compiled out.
  - No keys are remapped.

### How it works
- `SaveStore` (the game's single entry point for saving) remaps keys.
  - While test data is in use, progress keys are read and written as `"test:" + key`. The normal data's keys are never touched.
  - The profile flag `SaveProfile.Active` is not remapped. That is why the game still starts in the test data after the app is killed.
- Keys written while in the test data are listed in `SaveProfile.TestKeys`. Reset deletes those keys plus the `test:` version of every registered key, and nothing else.
- Backups go to `SaveBackups_test/`, and the Debug Run restore file to `DebugRunRestore_test.json`. Both are separate from the normal data's files.
- Switching works in this order:
  1. Flush pending writes of the current data (`ProgressStats.Flush`, `SaveStore.Save`).
  2. Change the flag.
  3. Run the save boot for the new data. Empty test data gets the new-user initial state (`DefaultSave`).
  4. Reload the static caches: cards, Mastery, NEW marks, CONTINUE, lifetime distance, met bosses, sprint.
  5. Reload the scene.

  Autosaves therefore never write into the other data.

### What is kept separate and what is shared

| Separate (test data only) | Shared by both |
|---|---|
| MILE, owned cards, NEW marks, Mastery, deck | Volume and mute |
| Characters, selected character and stage, character cards | Screen shake, glow, orientation |
| Distance unlocks, per-map BEST, overall BEST and time | High-speed assist settings |
| Lifetime distance | Multiplayer settings (last IP, port, mode, room name) |
| Three Reaper Sisters, last-dungeon unlock, met bosses, sprint gates | Arena settings |
| CONTINUE (suspended run) | Dev values: invincible, DEBUG mode, boss HP plan, resume ease-in, CARD TEST panel |
| Tutorial progress | |
| Dev unlocks: **all cards open / last dungeon always selectable / sprint all unlocked** (default OFF, so no automatic unlock-all) | |

### Initial state of the test data
Same as a real new user (`DefaultSave.WriteNewProgress`):
- MILE 0, BEST 0, lifetime 0
- No CONTINUE
- Stage: Wasteland Road
- Character: first in the list (Black Swordsman)
- Deck: the first 10 cards unlocked at distance 0
- Three Sisters not met; last dungeon locked and not selectable
- The first-time practice offer appears

## 2. First-time tutorial

### Flow
1. **First time pressing the door with new data**
   - A dialog appears: 「はじめての方へ」 with **操作を練習する** / **そのまま始める**.
   - It is shown only for data that has not run yet (lifetime distance under 1m), has no CONTINUE and is not in multiplayer. It appears once only.
2. **Practice** (`TutorialLauncher` / `TutorialRun`)
   - A cover saying 「準備中」 hides the setup. The scene is reloaded and a flat practice area is built:
     - no pits, steps, obstacles or random enemies
     - 11 km/h
     - no damage
     - distance does not advance
     - no EXP, bosses or escape
   - It uses the Black Swordsman at base strength. The selected character, deck and character cards are neither used nor written.
   - Each explanation is shown one at a time in large text and pauses the game. Practice resumes with **やってみる**.
     - The finger that pressed the button does not count as input until it is lifted (the input gate plus `ClearPointerState`).
     - The panel is placed on the side away from the character (above or below). If it does not fit, it shrinks along with its text.
   - Steps:
     1. Forward/back attacks: 「キャラは自動で走ります。前後にフリックすると、その方向へ攻撃します」. Practice targets on both sides.
     2. Up = jump, then double jump.
     3. Down attack in the air (over solid floor).
     4. Launch, then a follow-up attack in the air. A target that can be launched is provided. **スキップ** is available.
     5. Pick one of 3 harmless cards (ATTACK UP / HEART UP / SPEED UP). The effect applies only in the practice; nothing is added to owned cards.
     6. End: 「まずは1,000mのボスを目指そう！」
        - From the first door: **出発する** opens Stage Select, the normal departure for a new run.
        - From Settings: **ホームへ戻る**.
        - **もう一度練習する** is available in both cases.
   - **練習をやめる** (top right, below HP) leaves at any time. Leaving counts as "done", so the offer does not come back.
   - There is no time limit. If the player is stuck for 12 s, a hint is added.
   - Input hints follow the last device used: touch = flick, keyboard = keys, gamepad = buttons.
3. **Settings → 「遊び方」**
   - **操作を練習する** can be pressed any number of times, from Home only. Afterwards the player returns to Home. During a run or multiplayer the button is disabled with a note.
4. **Writes during the practice**
   - Nothing is written:
     - rewards, MILE, BEST, lifetime distance
     - unlocks, CONTINUE, selected character
   - Every save entry point is blocked: `TutorialMode.Active` was added to `DebugRun.WritesBlocked`.
   - A CONTINUE that existed before the practice stays as it was.
   - Only the tutorial progress flags are saved.
5. **First-time guidance**
   - **Escape guidance**: shown when the first boss reward ends and escape unlocks (`EscapeAvailable`).
     - It waits until nothing else is going on: no reward, level-up, boss fight, pause, resume screen, transition, Finish, or airborne player. Then it pauses the game and shows:
       > (画面/H キー/BACK ボタン)を3秒長押しすると脱出して、このランで集めたMILEを持ち帰れます。
       > 倒れてしまうと、このランのMILEは失われます。(BEST距離の記録は残ります)
       > このまま走り続けてもOK。脱出するかどうかは自由です。
     - Escape is not forced. The finger that presses **わかった** is not counted as an escape hold.
     - If the app is killed or the player dies before **わかった** is pressed, the guide is shown again the next time it applies.
   - **MILE guidance**: after the first escape (Win), shown once on the Home screen.
     > MILEは、ホームのガチャで使えます(1回 500 MILE)。引いたカードはデッキに入れて、次のランを強くできます。
6. **Results screen after dying (FAILED)**
   - Before this change it showed "+N MILE", which looked like MILE had been gained.
   - It now shows:
     - in red: 「失ったもの: このランのMILE N」
     - in green: 「残るもの: BEST距離の記録・累計走行距離 / 持っているカード・MILE(WALLET n)」
   - The CLEAR (escape) screen is unchanged.

### Where progress is stored
These are registered as progress keys in `SaveKeys`, so they are kept separately per data:
- `Tutorial.V1`, `Tutorial.Offered`, `Tutorial.PracticeDone`, `Tutorial.EscapeGuide`, `Tutorial.MileGuide`

**Existing users**
- On first launch of this version, data with lifetime distance or BEST above 0 has every first-time guide marked as "shown". Existing players see no offers.
- This is not a schema change: save format v3 is unchanged and nothing is reset.

**Multiplayer**
- No practice, door offer, escape guidance or MILE guidance.

## 3. Tests (`-qaTutorial`, Windows dev build, all checks passed)

**P: test data**
- New-user state: MILE 0, lifetime 0, BEST 0, no CONTINUE, no unlock-all, the offer appears.
- MILE and lifetime distance are written only to `test:` keys.
- Volume is shared.
- After an app kill (the flag is read again), the game is still in the test data.
- Reset deletes only the test data.
- After going back to the normal data, the normal values come back and the test data is kept.
- Reset from the normal data deletes only `test:` keys.

**T: practice**
- Offer → practice.
- Input while an explanation is open does nothing. Pauses are confirmed.
- 11.2 km/h; no damage; distance stays 0.
- All 6 steps achieved; launch + follow-up succeeded on the first try.
- The card takes effect; it is not added to owned cards.
- Progress data is identical before and after.
- The first-run flow ends at Stage Select. The selected character and deck are unchanged.
- From Settings: practice → quit → Home, and the CONTINUE is kept.

**G: guidance**
- The escape guide appears once, pauses the game, and the run continues after **わかった**.
- First escape → MILE guide appears once at Home and does not appear again.

**R: death**
- The run MILE is not added to the wallet.
- Results-screen screenshot checked.

**END**
- Every raw value of the normal data is identical before and after the test. No test keys are left behind.

## 4. Not verified on Android
- Touch flicks, long presses and the input carry-over check on a real device. The tests used keyboard and gamepad-equivalent input injection.
- Text size and panel placement around the notch / safe area on phone screens, and how the guide panel avoids the character.
- The PlayerPrefs XML on Android: that `test:` keys survive an app kill and the TEST DATA label stays shown.
- Pausing the practice explanation via app pause (Home button) and resuming.
