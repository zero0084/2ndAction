# Translations and display checks (2026-10-24)

## 1. Translations applied (Estelle's review → 30 languages)

| ID | Content | State |
|---|---|---|
| 10010–10011 | 2nd and 3rd lines of the "Return to Home" confirmation in the pause menu | Applied to 30 languages |
| 10012–10047 | Settings "Data & Privacy", the data-deletion screen and its confirmation, the LOCAL encryption message | Applied to 30 languages (unreviewed since 10-19 → done) |
| 10056–10058 | License screen (row name / button / note) | English is Master's approved wording; other languages checked for meaning and display width |
| 10059 | RESULT success heading 「脱出成功！」/ "ESCAPED!" | Applied to 30 languages |
| 10060–10062 | Loading-failure frame (Japanese-only since 10-11, found during this check) | Applied to 30 languages |

- The quoted button name in 10026 (the ad-free pass line) now matches each language's existing "Restore Purchases" button (ID 8133) (`Tools/loc/est23/fix10026.py`, log in `fix10026.log`).
- `{0}` (distance / URL / email) is kept in every language, and no translation has a line break inside it (checked by merge_add).
- Estelle's raw text and the collection scripts: `Tools/loc/est23/`
- **LocPendingReview.Pending is empty**, so ReleaseBuildGuard (the guard on the Android product build) no longer stops on the **translation** condition.
  - It is still stopped by the production ad-unit IDs, the production app ID and the purchase-receipt verification (GooglePlayTangle). These are your settings to enter, Master, and I haven't touched them.

## 2. Display check in 30 languages (Windows, 1920x1080, `-qaLocUi`, dedicated save)

`sheets/`: one image per screen with 30 languages side by side (left to right, top to bottom: ja, en, ar, bn, cs, de, es, fa, fil, fr, hi, id, it, km, ko, lo, ms, my, pl, pt-BR, ro, ru, sw, ta, th, tr, uk, vi, zh-Hans, zh-Hant).

| Screen | Result |
|---|---|
| RESULT (success) | Heading fits on one line in all 30 languages (TextFit overflow 0) |
| Settings: Data & Privacy | Fits in all 30 languages |
| Data-deletion screen / confirmation (mocked, no network) | Fits in all 30 languages; "can't be undone" and "cards, MILE, progress and the ad-free pass are not deleted" are shown |
| License (first page / scrolled to the end) | Opens and reaches the last line of the text in all 30 languages |
| Loading-failure frame | **Burmese (my) "Loading is taking longer than expected" was clipped on line 2 → fixed** (a wrapping heading now shrinks until it fits the frame; `LoadingScreen.FitLabel`). Re-checked with my/ta/km/ja/en; after the fix it's `sheets/loadfail_slow_my_after_fix.png` |

- On the first check, the Thai shot was hidden behind the "loading is taking too long" frame (a load over 25s because an APK build was running at the same time). On the re-check without the load, Thai displayed normally.

## 3. License screen on Android

- **Tested on the Android emulator (AVD omm_test, 1080x2400, landscape) with APK 360 (development build).** No real device was available this time; on-device testing is still open.
- Steps: launch with a dedicated save (`-fileSave emu_lic/save.json`) → Settings → scroll the settings screen with real touch swipes (`adb input touchscreen swipe`) → License "View" → swipe the text up to the end → Android back key.
- Results (`android_license/`):
  - Opens.
  - Reaches the end of the text ("OTHER DEALINGS IN THE SOFTWARE.", which matches the last line of the original), and further swipes don't move it.
  - The back key returns to Settings.
  - The × is drawn in the right place too (after the fix).
- The development build has the same License screen as the product build. The Android product build can't be made right now because of the guard in §1.

## 4. License screen on the Windows product build

- Checked on 10-23 with the Windows product build: it opens, scrolls to the end with the mouse wheel, and Esc closes it. The save was restored exactly afterwards.
- Re-check after these translations: see §6.

## 5. Online

- Not tested this time. As before, only **sign-in and room search** count as checked; actual co-op and versus play are **unverified**.
