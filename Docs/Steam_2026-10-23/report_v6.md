# Steam trailer v6 / second trailer "Settings & customization" (2026-10-24)

## 1. First trailer v6 (RESULT heading only)

- **What changed**: only the last 2 cuts (hold to escape → RESULT) were re-shot and swapped in. Every other cut, the captions, the BGM, the logo and the closing line 「決めるのはあなたです。」/ "The choice is yours." are the same as v5.
- **v5 is kept as-is** (the official candidate, nothing overwritten): `trailer/omm_trailer_draft5_{ja,en}_endline_1080p60.mp4`, and in the archive at `SteamCaptureArchive/2026-10-23/final`.
- **Heading**: GAME CLEAR → 脱出成功！ / ESCAPED! (ID 10059, 30 languages). Game logic (success/failure, MILE, records, save) is unchanged — only the displayed text.
- **Finished files**: `trailer/omm_trailer_v6_ja_1080p60.mp4` (68.0s), `trailer/omm_trailer_v6_en_1080p60.mp4` (64.5s)
- **Audio**: ja −14.1 LUFS / true peak −1.2 dBTP, en −14.1 LUFS / −1.1 dBTP (target ≤ −1 dBTP)
- **Edit data**: `trailer/edl_v6_{ja,en}.json` (v5 endline with only the shot and in point of the last 2 cuts changed)
- **Re-shoot**: `-scShots result` (escape only; the fight is on flat ground, and if it runs long the boss is defeated through the normal defeat sequence). ja = cap_r8_ja, en = cap_r9_en.
- **Comparison images**: `compare_v5_v6/result_v5_vs_v6_{ja,en}.png` (left v5, right v6)
- **Estelle's review**: "v6 is more appropriate than v5. It removes the misreading that the whole game was cleared. Recommend adopting it" (v5 stays as a backup). **Final adoption is your call, Master.**

## 2. Second trailer "Settings & customization" (draft 2, 34.4s, ja/en)

| Time | Scene | Caption (ja / en) |
|---|---|---|
| 0.0–1.4 | Running with auto OFF | 操作は、あなたに合わせて。/ Play your way. |
| 1.4–5.0 | Turn on Auto Mode, Auto Attack and Auto Avoid in settings | オートモード・自動攻撃・自動回避 / Auto Mode, Auto Attack, Auto Avoid |
| 5.0–9.0 | After closing, it jumps over pits and attacks automatically | ジャンプも攻撃も、自動で / Jumps and attacks, automated. |
| 9.0–14.4 | Display: screen shake OFF, warnings outline-only, glow 40% → gameplay | 画面揺れ・警告・発光を好みに / Tune screen shake, warnings and glow |
| 14.4–20.1 | Volume: BGM 100 → 25% (held ~1.3s) → 75%, SE 75% (the game's own BGM is heard here) | BGMと効果音は別々に調整 / Set music and SFX separately |
| 20.1–29.9 | Portrait run view: side → split → angled (portrait capture centered, sides blurred) | 縦画面は3つの見え方から / Three views for portrait play |
| 29.9–34.4 | Logo | 自分のペースで、自分の走りを。/ Your pace. Your way. |

- **Finished files**: `trailer_settings/omm_settings_s2_{ja,en}_1080p60.mp4`. Audio: ja −14.3 LUFS / −2.2 dBTP, en −14.1 LUFS / −1.4 dBTP.
- **Draft 1 kept too**: `omm_settings_s1_{ja,en}_1080p60.mp4` (draft before Estelle's review)
- **Changes from Estelle's review** (draft 1 → draft 2):
  - Captions over the settings screen keep the same position (lower left) with a dark band behind them.
  - Closing line changed from 「どこまでも」 because "どこまでも" (endlessly) suggests you can run forever.
  - The BGM 25% state is now audible for at least 1 second.
  - The display scene was shortened slightly.
  - English for the auto-play scene: "Jumps and attacks, automated."
- **Auto Avoid wording**: the in-game note says it can't always avoid everything, so the caption stays at 「ジャンプも攻撃も、自動で」 (Estelle: no exaggeration).
- **Shots**: `QaSweep.StoreCaptureSettings.cs` (`-scShots set_auto,set_display,set_volume,set_portrait`). Settings are changed through the same functions as the settings screen's buttons (HighSpeedAssist / AudioManager / GameSettings / PortraitRunView). set_portrait runs in a 608x1080 window.
- **Edit data**: `trailer_settings/edl_s2_{ja,en}.json`, captions `text/captions_settings.tsv`; `trailer.py` gained `pillar` (portrait capture centered) and `cap_pos: band`.

## 3. Bug found while shooting (fixed)

- The settings screen's "×" was drawn as a single diagonal line away from the button at any screen scale other than 1x (1080p and up, and phones). `GUIUtility.RotateAroundPivot` rotated using the coordinates before GUI.matrix's scaling. It now rotates within the current coordinates (`UiIcons.DrawClose`), which fixes the × on every dialog. Confirmed fixed on the Android emulator (1080x2400) as well.

## 4. Storage

- `C:/GameProject/SteamCaptureArchive/2026-10-24/`
  - `masters/`: every shot the v6 and settings-trailer edit data uses, in H.264 4:4:4 crf10 + wav.
  - `edit/`: EDLs, captions, tools.
  - `final/`: v6 and settings trailer drafts 1 and 2.
- The 10-23 archive (v4/v5 and the masters) is untouched.
- Disk space: partway through, drive C ran out of space (6 MB free). I deleted only 81 copies of old test builds from 10-21 to 10-23 and 4 superseded capture folders in the scratch area. None of these were videos, edit data, backups or materials referenced by an EDL. About 300 GB is free now.
