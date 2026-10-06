# Audio Redesign (BGM / SE / Ambient), 2026-10-06

The game grew a lot after the first audio pass (2026-09-29): aerial combos, Launch / Slam, enemy FINISH, BOSS FINISH, 12 characters, many enemies and bosses, the arena, cards / COMBO / ULTIMATE / FINAL EVOLUTION, sprint departure, and multiplayer. The audio was re-checked against the current game and redesigned.

## What the survey found

- **The loudness ladder had collapsed.**
  - Two causes:
    - Unity imported every mono clip with **Normalize = ON**, which pushes each file's peak to 0 dB. This erased the loudness differences between files.
    - The old SE pack (`Audio/SE/*.wav`) is about 8–10 dB quieter than the placeholders.
  - Measured result: a normal hit, a strong hit and the boss final hit were all about the same loudness.
  - Meanwhile the warning sound (square wave) and the countdown were loud.
- **Sounds shared too widely.**
  - COMBO reused Decide.
  - ULTIMATE reused Decide / BossWarning / BossHit.
  - Boss BREAK reused BossFinalHit.
  - FINAL EVOLUTION reused Milestone / LevelUp.
  - Ninja shared the dual-blade sounds, the miko shared the mage's, the dragonkin shared the fighter's, and the dragon lancer and vampire shared Special.
- **Never played:** CardGet. The library slots for card fusion were empty (procedural sounds were used instead).
- **No sound at all:** boss and enemy telegraphs, Launch success, FINISH, BOSS FINISH (only the shared strong hit), the arena, multiplayer, high speed, sprint rings, and the sonic boom.
- **SE playback:** everything went through `PlayOneShot` on one AudioSource. There were no priorities and no cap on simultaneous sounds, so mass kills could bury the important sounds.
- **BGM loudness:** the real tracks were -17.2 dB (TitleBgm) and -19.3 dB (GameplayBgm); the placeholders ranged from -11 to -13.6 dB.

## Structure (what changed)

- **Asset generation:** `Tools/audio/gen_audio_v2.py`
  - Adds new SE, wind, map ambient, the final-boss BGM and the arena BGM.
  - Writes **loudness-matched copies** (`*_n.wav`) of every SE, BGM and ambient file, one target per category. The originals are left untouched (they are the source material).
  - SE are written 5.2 dB lower so the loud sounds (FINISH / BOSS FINISH) keep peak headroom; the game raises SE volume to match.
  - BGM where one instrument's pitch sticks out (a single band at 1–8 kHz that is 10× the median and over 2.5% of the energy) gets only that band lowered by 6 dB. This applied to Sky Corridor early (around 1.5 kHz).
- **`AudioLibraryBuilder`:** import **Normalize = OFF**, then a full rebuild of the library (`Tools/OneMoreMile/Audio/Rebuild Audio Library (overwrite)`).
- **`AudioManager`**
  - **SE voices:** 20 AudioSources. Each `SeEntry` has a **priority** (Low / Normal / High / Critical) and a **max simultaneous count**.
    - When the same SE exceeds its max, the oldest copy is replaced.
    - When no voice is free, the oldest voice of the same or lower priority is stopped.
    - With more than 5 overlapping Normal sounds, those are turned down slightly.
  - **Critical SE** briefly lower the BGM to ×0.55 and ambient to about ×0.78 (fast down, back in about 0.5 s).
  - **`PlaySeAt(id, worldPos)`:** pans left and right by screen position. Off-screen sounds below High priority are not played.
  - **Per-track volume correction (`musicGains`)**, plus **high-speed wind** (silent below 70 km/h, peaks at 300 km/h; pitch 0.85 → 1.25; follows the environment volume).
  - Base volumes: BGM 0.36, SE 1.0, Env 0.40.
- **`BgmDirector`**
  - Fade length depends on the kind of switch:

    | Switch | Fade |
    |---|---|
    | Stage → boss | 0.7 s |
    | Boss → stage | 2.4 s |
    | Early → middle → late | 3.5 s |
    | HOME ↔ run | 1.2 s |

  - Boss music categories: Normal / Strong / Special / Death (reaper) / Final (last-dungeon rush).
  - The arena plays the arena BGM.
  - Logs `[BGM] reason -> clip fade` whenever the track changes.
- **No AudioMixer was added.** Everything still runs on AudioSources, the four volume settings and the existing mute.

## BGM playback conditions

| Situation | Track |
|---|---|
| HOME | TitleBgm (existing, gain 1.15) + morning breeze and birds |
| Stage 0–9,999 m / 10,000–69,999 m / 70,000 m+ | Per stage: early / middle / late (crossfade 3.5 s) |
| Wasteland Road early | GameplayBgm (existing, gain 1.45) |
| Boss gate every 1,000 m | Normal (5,000 m = Strong, 10,000 m = Special) |
| Last-dungeon boss rush (90–98 km) | **Final** (new, `bgm_boss_final`) |
| Reaper at 100,000 m / the sisters' final battle | Death |
| BONUS ZONE | bonus (pitch up during the zone) |
| Arena | **Arena** (new, `bgm_arena`) |
| RESULT / GAME OVER | Jingles (gain 0.6) |
| Last-dungeon ending | Existing override (silence / credits / choice) |

## Main SE conditions (loudness ladder: normal hit < strong hit < Slam < FINISH < BOSS FINISH)

| Event | SeId | Priority |
|---|---|---|
| Jump / double jump / land | Jump / DoubleJump / Land (old sounds, normalized) | Low |
| Landing from a dive (dragon lancer / fighter / ninja / every dive) | **LandHeavy** | Normal |
| Swing, per weapon family (2 alternating + finisher) | 11 families, see below | Normal |
| Normal hit | **Hit** (3 variations) | Normal |
| Projectile hit | **HitProjectile** | Normal |
| Up attack / Launch hit | **HitLaunch** | High |
| Lethal hit (normal kill) | StrongHit ×0.8 + EnemyDefeat | High |
| Slam landing (enemy hits the ground) | **SlamImpact** | High |
| Enemy FINISH (heavy / overkill) | **FinishHit + FinishBurst** | High |
| BOSS FINISH | **BossFinishHit** (Critical) → **BossCollapse** (crash / slam / collapse) → **BossDissolve** | Critical / High |
| Enemy telegraph (5 kinds: dive, rush, melee, hop, worm) | **EnemyTelegraph** (on screen only) | Normal |
| Enemy shot | **EnemyShot** | Low |
| Boss telegraph (`WildBossBase.Telegraph`, covers every Wasteland / Cave / Sky boss) | **BossTelegraph** / **BossTelegraphHeavy** (wind-up ≥ 0.8 s) | High / Critical |
| Dragon / Majin charge | **BossCharge** / BossTelegraph | High |
| Boss BREAK / phase change / ultimate | **BossBreak / BossPhase / BossUltimate** | Critical |
| Card appears / flips / is selected / is obtained / is rare (★4+ or FE / AWAKENED) | **CardAppear / CardFlip** / CardSelect / **CardGet** / **CardRare** | — |
| COMBO formed | **ComboFormed** | High |
| FINAL EVOLUTION READY / activate | **FinalEvolutionReady / Activate** | High / Critical |
| ULTIMATE gauge full / activate / impact | **UltimateReady / Activate / Impact** | High / Critical |
| Fusion charge / success / fail | CardFusion / FusionSuccess / FusionFail (library) | — |
| Mastery up / Lv.9 MAX / AWAKENED | **MasteryUp / MaxLevel** | High |
| Sprint ring success / +MILE | **RingBurst / MileGet** | — |
| Entering sonic speed | **SonicBoom** (once until the speed drops again) | High |
| Pause / resume | **Pause / Resume** | High |
| Settings open / close / mute | **UiOpen / UiClose / UiToggle** | — |
| Multiplayer: next screen / back / create a room / join / leave / down / revive | Decide / Cancel / **NetJoin / NetLeave / NetDown / NetRevive** | — |
| Arena start / clear / defeat | **ArenaStart / ArenaWin / ArenaLose** | Critical / High |

**Weapon families.** Ninja, miko, dragonkin, dragon lancer and vampire now have their own sounds; before, they borrowed another character's.

| Family | Characters |
|---|---|
| Sword | Black Swordsman, Noble Lady Knight |
| DualBlade | Dual Blade |
| Ninja | Ninja |
| Gun | Gunslinger |
| Bow | Archer |
| Magic | Mage |
| Spirit (bell + ofuda paper) | Miko |
| Strike | Fighter |
| Claw (tearing) | Dragonkin |
| Lance (thrust + metallic ring) | Dragon Lancer |
| Blood (dark swing + wingbeat) | Vampire |

## Testing

- **`-qaAudio <dir> [-qaAuOnly ABCDEFGHI] [-qaAuRecord 1]`** (Windows development build)
  - Measures the **actual output** (`AudioMeter`, `OnAudioFilterRead` on the AudioListener).
  - A: assignments
  - B: loudness ladder
  - C: balance — every BGM, ambient alone, important SE vs BGM
  - D: distance bands and boss crossfades
  - E: simultaneous voices
  - F: BGM dip under important SE
  - G: mute / each volume at 0
  - H: wind
  - I: sounds play in actual gameplay
  - R: about 50 s of play recorded to `gameplay_audio.wav`
- **Every SE in any test:** add `-auLog` to log each SeId as `[SE] Id`. BGM changes are always logged as `[BGM]`.
- **Existing tests:** `-audioTest` (the existing full sweep HOME → stage → boss → reaper → GAME OVER → HOME → other stages → RESULT). Only its wait was changed, to match the slower phase crossfade.

## Unused files (not deleted)

These are not referenced from the library, so they are not included in builds:
- `Audio/SE/01_attack_hit`, `03_enemy_defeat`, `AttackSe3`, `LandSe`
- `Placeholder/SE/atk_sword_strong`
- the original `Placeholder/*` files (they are the source for the `_n` copies)

`CardConfirmSe` / `CardDeckAppearSe` / `CardDrawSe` / `CardFlipSe` remain only as scene fallbacks for `RewardCardSequence` (used when the library has no entry).
