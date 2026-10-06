# BOSS FINISH SYSTEM (2026-10-06)

When a boss's HP reaches 0, the boss dies immediately (logic). The finish visual then plays separately (presentation). When the visual ends, the encounter ends (boss reward choice / next boss).

## Order of events

1. **HP 0, same frame (HOST/solo)**
   - Decide the finish info: final attack type, direction, first kill or rematch. This is the `BossFinishInfo`, packed into `BossFinishCode`.
   - Set `dead` and stop AI, attacks, ULTIMATE, stagger and phase. Disable every Collider2D and hide all hitboxes and warning markers.
   - Confirm the reward (`RegisterDefeatReward`: MILE / EXP / ULTIMATE gauge) once, at this moment.
   - If no other boss is alive, clear the boss attacks still on screen (`BossFinish.ClearBossHazards`):
     - BossProjectile / TrackedHazard / SkyStrike / SkyWarnBand / SkyFlyby / SkyDrift / CaveHazard
     - boss fireballs (`FireballController.bossOwned`); minion bullets and reflected fireballs are left alone
2. **Death visual (`BossFinish`, a component added to the boss root)**
   - Final Hit: HitStop 0.13s (merged with `RequestStop` taking the max), flash, small shake (capped), zoom 0.94 for 0.22s, slow-mo 0.16s ×0.45. Slow-mo runs only when `TimeControl.PresentationBusy` is not active.
   - The boss's reaction to the final attack (Forward / Back / Up / Aerial / Slam / Projectile / Other) leads into the profile's motion.
   - Motion is camera-relative, so the boss stays on screen even at 300 km/h.
3. **Visual ends**
   - Hide the boss → `OnDeathVisualFinished` → end the encounter:
     - `OnWildBossDefeated` / `DefeatOverride`
     - `OnDragonDefeated` / `OnMajinDefeated`
   - Level-up and the boss reward choice wait until the visual ends: `IsBossPresentationActive` includes `BossFinish.AnyRunning`.

- **Phoenix rebirth / reaper retreat:** handled earlier in `OnLethalDamage`, so they don't enter FINISH. Only the final death does.
- **Run resume:** the run does not stop during the visual (the run speed is not reduced). It only waits during the HitStop and slow-mo.
- **GameOver:** `GameOverCleanup` → `BossFinish.StopAll()` (zoom back to 1, slow-mo released, encounter not advanced).

## Length (`Resources/Finish/BossDeathTuning`, falls back to defaults when absent)

| | Length |
|---|---|
| First kill | 2.0s (`firstKillSeconds`) |
| Rematch | 1.1s (`rematchSeconds`) |

A kill counts as a rematch when any of these is true:
- `RematchTierApplied >= 0`
- `CurrentEncounterIsRematch`
- a last-dungeon rush (`CurrentBossEncounter.rush`)

## Profiles (`BossDeathProfile`)

| Profile | Motion | Bosses |
|---|---|---|
| BEAST | Knocked flying → lands (1–2 bounces depending on mass) → rolls and slides → stops on its side → dissolves into light | Wolf, GoblinRider, Spider, Scorpion, Mole, ScorpionKing, Basilisk, Drake, Behemoth (lightning), Fenrir |
| HUMANOID | Brief freeze → kneels / slumps forward → still → dissolves into rising light (no explosion) | Demon, BlackKnight, AncientDemon, Guardian (halo), Majin, Reaper |
| FLYING | Loses its posture → loses altitude → crashes diagonally → bursts on the ground → wreck slides and topples → smoke and light | Griffin, Dragon, Bat, Jellyfish, Phoenix (turns to fire, rises into the sky) |
| GOLEM | Cracks → parts fall off → cells scatter (`BossRig` per cell) | Golem, CrystalGolem (crystal), SkyGolem |
| SERPENT | Collapses in waves from head to tail → sinks | Serpent, Hydra, Centipede, Worm, Leviathan (sinks into the clouds), SkySerpent (storm) |
| GIANT | Staggers → falls to its knees → large ground impact → sinks | Cyclops, Troll, Titan (sinks) |

Each entry (`BossEntry`) has `deathMass` / `deathKnockback` / `deathFallSpeed` / `specialFx`.
- Heavier mass means it is blown away less and bounces less.

## Multiplayer

- The HOST decides `BossFinishInfo.Pack()` = `0x8000 | attack | dir<<4 | firstKill<<5` and appends it to the boss OpDeath.
- JOIN receives it in `NetCombat.HandleDeath` → `NetBossFinishCode` / `NetBossFinishLocal` → `NetPuppetDie` plays the same visual.
- The reward and the encounter end are HOST-only.
- The impact effects (HitStop / zoom) are strong only for the player who landed the hit; other players get a weaker version.

## Developer tools

- **DEBUG → BOSS FINISH (page 10):** choose the family (Wasteland / Cave / Sky), the boss and the attack. The kill-type button treats the kill as actual / rematch / first kill. "出して倒す" (spawn and kill) spawns the boss and kills it. There is also an ON/OFF switch.
- **Automated test:** `OneMoreMile.exe -qaBossFinish <dir> [-qaBfOnly ABC..] [-qaBfShots 1] [-qaBfAll 1]`
  - A–U: profiles × attacks, ULTIMATE / stagger / phase, before and after resume, pending boss, rematch / first kill, 100 / 200 / 300 km/h
  - X: GameOver during the visual
  - Y: Leviathan
  - Z: after Retry
  - P: Phoenix
  - `-qaBfAll`: one kill of every Wasteland / Cave / Sky boss
- **What each kill checks:**
  - reward in the same frame
  - colliders off
  - FINISH started
  - profile / attack / first-kill-or-rematch
  - hazards cleared
  - visual length (between 0.8×D and D+0.9s)
  - on screen (viewport)
  - run speed not reduced
  - encounter ended

## Load

- Effects use the `FinishFx` pool (shared with Enemy FINISH; SpriteRenderer ×700, nothing allocated at runtime). Particles are reduced to fit the budget.
- One `BossFinish` component per boss, removed when finished.
