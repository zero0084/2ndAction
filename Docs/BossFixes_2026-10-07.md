# Boss BREAK, arena transition, and other fixes (2026-10-07)

## 1. BREAK: flying bosses drifted behind the screen

**Direct cause (Dragon)**
- While stunned, `StunFollow` only moved the Dragon toward its target at an absolute speed of 14 m/s (= 50 km/h).
- Above 50 km/h it could not keep up with the run, so it slid toward the back (left) of the screen and was left behind.

**Fix: during BREAK, Dragon movement now works in two steps**
1. Each frame it is carried forward by the run speed, which keeps its position on screen.
2. It then moves toward a point the player can reach with melee: 4 m in front of the player, with the bottom of its hurtbox 0.25 m above the ground. The move has a time constant of 0.15 s (0.1 s vertically), so there is no teleport.

The following happen at the same time:
- Attack velocities (charge, dive, fireballs) stop together with their coroutines (`StopAllCoroutines`).
- `ReturnToHome` (used after BREAK and after attacks) now keeps its start point **relative to the player**. Before, at high speed the start point was left behind, so the Dragon jumped backwards once on its way back.

**Majin**
- The descent and the return used the same stale world start point. At 200–320 km/h the Majin drifted 9–16 m behind the player.
- Both now use player-relative positions.
- During BREAK it comes down in 0.35 s, stays at the reachable point until BREAK ends, and only then returns to its usual height.

**Bosses on the WildBossBase base (Wasteland, Cave, Sky)**
- These already moved at the run speed, so they did not drift.
- If the near edge of the hurtbox is more than 3 m from the player, the boss now slides in until that edge is 2 m away (`breakPulling`).
- During BREAK the usual spacing limits (`minGap`/`maxGap`) are relaxed.
- Bosses already within reach do not move. This is the normal case for ground bosses, so they are not pulled toward the player.

**Not changed**
- BREAK during an ultimate (the ultimate is cancelled).
- Death during BREAK hands control over to BOSS FINISH; the Dragon's `state=Dead` stops `StunFollow`.
- Multiplayer stays host-authoritative.

**Tests**
- `-qaBreak` covers flying, hover and ground bosses at 30 / 100 / 200 / 320 km/h; the Dragon during its ultimate, its charge and after a run resume; and a kill during BREAK.
- For each frame it measures distance, screen position, and hurtbox edge and height.

## 2. Entering the arena: the normal dungeon start screen appeared on the way

**Cause:** `ArenaStartRun` → `StartGame` played the normal door-light transition and the 3·2·1·GO countdown. The reload of the home scene was also visible.

**Fix**
- `ArenaStartRun` now calls `ApplyGameStart` directly and skips the countdown (`skipStartCountdown`).
- From the moment the player taps, `ArenaLauncher` covers the screen with "闘技場を準備中…" until the arena has been built. Time is stopped while covered (`coverTimeOwner`).
- Once ready, the cover is removed and the next touch is latched so it does not turn into an attack.
- Measured: home → setup screen 0.73 s; rematch 0.40 s.
- A rematch still reloads the scene (so no state from the previous match carries over), but it is covered, so the start screen is not shown.

## 3. Sprint departure: unreached distances could be selected

**Check**
- The decision uses only that map's best distance and that map's gatekeeper kills. It never used other maps' records or the lifetime distance.
- The list and the actual departure share the same check (`SprintRecords.IsUnlocked`).

**Fix**
- The 0.5 m tolerance was removed. The condition is now `best >= destination`, so 59,999.9 m does not unlock 60 km. The displayed best is rounded down.
- The only path that allows unreached destinations is the DEBUG "sprint destinations: all unlocked" switch. Rows allowed by that switch now say `[DEBUG 全解放]`.

## 4. Terrain-aware boss moves (ceiling cling)

**Before**
- `CaveHazard.CeilingAt` returned ground + 7 m even where there is no ceiling.
- The boss chose ceiling moves wherever it was, and hung on a ceiling that did not exist.

**Fix**
- Ceiling moves (Centipede `CeilingChase` / `CAVE CRAWLER`, Bat `CeilingHang`) are chosen only when a real ceiling with enough space continues over the whole distance the move will cover.
  - That distance is run speed × move duration + 12 m.
  - "Enough space" is 4.2–9.5 m above the ground. A pit under the ceiling does not count against it.
- If no real ceiling is found, the boss keeps using its ground moves.
- If it loses the ceiling while hanging (`OnBattleTick`), it drops smoothly from its current height and goes back to normal moves (`DropAfterLostCeiling`).

**Limits:** No path search was added. Moves through walls and the flight paths of flying bosses are unchanged; for those the existing ceiling clamp is all that applies.

## 5. Ceiling-clinging bosses could not be hit

**Cause (two problems combined)**
1. `ClimbToCeiling` switched on invulnerability and disabled the hurtbox from the start of the climb.
2. On the ceiling the body sprite is flipped upside down to hang below the root. The hurtbox was not flipped, so it stayed above the root, inside the ceiling rock.

**Fix**
- A clinging boss is no longer invulnerable.
- The hurtbox follows the body's vertical flip continuously, including during the climb and the drop.
- Burrowing underground stays invulnerable on purpose.
- Normal enemies have no ceiling-cling behavior; flying enemies are only kept below the ceiling.

## 6. Thrown rocks returned to the ground after falling

**Cause**
- Rocks thrown by bosses (`BossProjectile.hugGround`) snapped to the ground height under their x position every frame. Over a pit they kept their height and floated across, then snapped back up onto the next ground.
- Cave falling rocks (`CaveHazard` `FallRock`) snapped to a ground height of 0 over a pit.

**Fix**
- Rocks follow slopes and small steps (up to 0.9 m up and 1.6 m down).
- Once off the footing they fall under gravity, and land only when they reach the top surface of a lower footing from above.
- A wall in front makes them disappear.
- A rock that has fallen far enough below the screen is removed.

## 7. The game sometimes stopped for a few seconds after a hit

**Cause, confirmed by reproduction**
- `EnemyController.ReactToHit` ran HitStop inside the hit enemy's own coroutine.
- If that enemy died during the stop and was hidden (FINISH calls `SetActive(false)` at the moment of the kill), the coroutine was cut off and `TimeControl.Resume` never ran.
- The game stayed stopped until the watchdog noticed it after 3 s.
- Reproduction with the old code: a 0.1 s HitStop stopped the game for 0.98 s (the watchdog limit had already been lowered to 1 s; it was 3 s before).

**Fix**
- Starting and ending the stop now runs on a persistent runner (`HitStop.Run` / `HitStop.Begin`).
- The watchdog limit was lowered from 3 s to 1 s.
- A lightweight probe (`StallProbe`) logs one line when either of these happens:
  - a frame takes more than 0.25 s
  - HitStop alone keeps the game stopped for more than 0.4 s

  The line includes the last hits (enemy, attack, kill, hits in the same frame) and the active pause reasons.

**Tests:** `-qaHitStall`
- A0: reproduces the old behavior.
- A: the hit enemy disappears during HitStop.
- B: repeated hits on many enemies at once, combos, and multiple simultaneous kills.

## 8. Normal boss battles no longer resume the run when the boss is not defeated

**Change**
- `encounterResumable` is always false. The fight continues until the boss is defeated.
- The fight timer (`EncounterSeconds`) keeps counting but is no longer used to resume.
- While the run is held during the fight:
  - distance does not advance
  - normal enemies do not spawn
  - the next gate is neither evaluated nor queued

**After the kill:** the existing reward and ending run once, then the run continues.

**Last-dungeon boss rush (90 km+):** handled separately (`RushGateK` / its own `RunResumed`), so it is unchanged.

**Bosses stuck out of the fighting area**
- A boss counts as stuck when it stays outside the camera view by a wide margin (`OffArenaWatch`) for 5 s.
- It is then moved back in front of the player at its usual height.
- HP and rewards are unchanged.
- Normal behavior is excluded: overtaking or charging in from off screen (`freeGap`), invulnerable moves, burrowing, the entrance, and the death visual.
