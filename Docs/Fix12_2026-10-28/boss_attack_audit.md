# Boss attack body-animation audit (read-only, 2026-10-28)

Scripts root: `C:/GameProject/2ndAction/Unity/2ndAction/Assets/Scripts` (paths below are relative to it).

Legend:
- **OK**: the body changes to a windup frame or image, then an attack frame or image, and the hit lines up with it.
- **OK\***: the same, but the sky bosses only have still images (idle, windup and attack). Recover, Fly and Landing all fall back to the idle image.
- **PARTIAL**: only part of the sequence plays, or the frames don't match the move, or the body holds one still frame for a long hazard sequence.
- **MISSING**: the body stays in idle, move or the held windup frame while the effects, projectiles or hazards do the attack.

## 0. How the systems work (needed to read the tables)

| System | Where | Notes |
|---|---|---|
| WildBossBase pose to sprite | `WildBossBase.cs:506-602` | `PickAnimSprite` uses BossAnimSet frames (`Resources/BossAnim/<animKey>/`). If there are none, `PickSprite` uses the still `idleSprite/windupSprite/attackSprite/moveSprite`. In that fallback Fly uses moveSprite or idle, Landing uses idle, and Recover uses attackSprite. |
| Telegraph | `WildBossBase.cs:909-945` | Sets `Pose.Windup` and fills `windupProgress`. At the end it **resets windupProgress to 0 but leaves the pose at Windup**. If no Strike or PlayAttackPose follows, the boss holds windup frame 0 (or the still windup image), and the lean and tint are reset too. |
| Strike / PlayAttackPose | `WildBossBase.cs:947-970` | Sets `Pose.Attack` on the same frame the hitbox or projectile starts, so timing lines up. The last attack frame stays up until the next SetPose. |
| Recover | `WildBossBase.cs:1014-1025` | Shows `RecoverFor(animVariant)`. If there is none, it holds the last attack frame for 0.15-0.28 s. The sky bosses have no Anim, so they go straight to Idle. |
| `RecoverFor(v)` | `Bosses/BossAnimSet.cs` (`RecoverFor`) | `(v==1 ? recoverB : null) ?? recover`. **Every variant other than 1 falls back to the move-0 `recover_` frame.** |
| Ultimate frames | `WildBossBase.cs:510-517` | While `UltimateRunning && animVariant==0`, **Move and Fly also show `ult_` frames**: `ult[1]`, or a loop if there are 3 or more. |
| Dragon / Majin | `DragonController.cs:532-551`, `MajinController.cs:351-372` | These are separate controllers that loop a frame set (`idleFrames/chargeFrames/fireFrames`, and Majin's `idleFrames/attackFrames`). The sets are baked into the scene by `Editor/SceneBuilder.cs:584-596` from `Assets/Art/DragonIdle(8) / DragonCharge(7) / DragonFire(8) / MajinIdle(24) / MajinAttack(8)`. There is no windup, recover, landing or bite art and no pose system. |
| Sky bosses (9) | `BossManager.cs:1466-1512`, `Editor/SceneBuilder.cs:652-662, 4575-4590` | Each has only `Assets/Art/SkyBoss/<name>_{idle,windup,attack}.png`. There is **no move image and no frame animation** for any of them. |
| MechDragon | `Bosses/MechDragonBoss.cs:33-83` | `PoseOverride` shows one image per move phase (`Resources/Bosses/MechDragon/*`, 15 poses, all present). |

Art inventory for `Resources/BossAnim` (frame counts per prefix were checked):
- wolf: idle none, move 6, windup 3, attack 4, ult 2. It has no B/C variants and no recover.
- goblin_rider: windup 2, attack 3, recover 1, windupB 2, attackB 1, ult 3.
- serpent: windup/attack 2/2, plus B/C, dive 2, emerge 1, recover 1.
- cyclops: windup/attack 2/2, plus B/C, ult 2, exhaust 1, recover 1.
- spider: windup/attack plus B/C(2)/D, recover 1.
- golem: windup/attack 2/2, plus B/C/D, exhaust 1, recover 1.
- griffin: A-D, land 1, exhaust 1.
- hydra: A-D, ult 2, exhaust 1.
- demon: A-C, ult 2, exhaust 1, recover 1.
- black_knight: A-E, fly 1, ult 2, exhaust 1, recover 1.
- centipede: A-D (windupC is missing, so it falls back to windup), recover 1.
- scorpion: A-D (windupC missing), exhaust 1, recover 1.
- mole: A-B, dive 2, emerge 1, exhaust 1, recover 1.
- troll: A-D, exhaust 1, recover 1.
- worm: A-B, dive 2, emerge 1, pieces, recover 1.
- crystal_golem: A-D, ult 2, exhaust 1, recoverB 1.
- bat: A-C, land 2, exhaust 1.
- scorpion_king: A-E, exhaust 1.
- basilisk: A-E, exhaust 1.
- drake: A-E, dive 1, emerge 1, exhaust 1, recover 1.
- ancient_demon: A-C, ult 2, exhaust 1.
- reaper_* (3 sisters): A-B (2 each), recover and recoverB.
- **dragon, majin, mech_dragon and the 9 sky bosses have no BossAnim folder.**

---

## 1. Dragons (highest priority)

### 1a. DragonController: sky dragon (1 km, `SpawnSkyDragonImpl`, `BossManager.cs:1392-1410`) and wasteland dragon (80 km, `BossManager.cs:1541-1549`)
Both use the same code. BossManager sets `chargeAttackEnabled = true` and `landingAttackEnabled = true` for both.

| Attack | Windup | Attack | Recover | Status | Notes |
|---|---|---|---|---|---|
| Telegraph before Fire/Charge (`TelegraphAndAttack`, `DragonController.cs:577-600`) | idleFrames plus a white flash overlay blinking every 0.3 s for **3.0 s** (`telegraphDuration`, line 55) | n/a | n/a | **MISSING (windup)** | The only telegraph is the flash overlay. The body keeps flapping in idle. |
| Fire breath, 5 patterns (`FireAttack`, `:657-690`) | fireFrames start 0.5 s before the first shot (`fireWindupDuration`, line 71) | fireFrames loop at 8 fps (`animFps`) | fireFrames for 0.6 s (`fireRecoverDuration`), then idleFrames | **PARTIAL** | All 8 fire frames have the flame baked in. Flame shows before any fireball spawns and after the last one. There is no inhale windup and no mouth-closing recover. The loop is not synced to each shot (Triple, Quint, Fan, DoubleFan). |
| Charge / dive (`ChargeAttack`, `:605-623`; the contact hit is `OnTriggerEnter` while `State.Charging`, `:790`) | idle plus flash (the telegraph above) | chargeFrames loop during the dive and the rise | idleFrames during `ReturnToHome` | **PARTIAL** | chargeFrames are an upright rearing and roaring pose (charge_00-06). They don't show a dive or swoop, but the body does change. |
| Landing attack: descend, land slam, bite, rise (`LandingAttack`, `:389-459`) | idle plus `BlinkFlash(1.3)` | idleFrames during the descent and the `landHitbox.Strike` (`:424`). For the bite, idle plus `BlinkFlash(0.8)`, then `biteHitbox.Strike` (`:444-446`) | idleFrames during the grounded hold and the rise | **MISSING** | There is **no `SetFrames` call anywhere in the routine**. The flying-idle loop plays while the dragon sits on the ground, lands and bites. |
| Ultimate "DRAGON FIRE CHARGE / 煉獄の巨大火球" (`GiantFireball`, `:990-1063`) | 0.6 s rise in idle, then **fireFrames for the 1.5 s orb charge** (`:1022`) | fireFrames when the giant fireball spawns (`:1043`) | **fireFrames stay on for 2.2 s, then for all of `LowRecovery(2.8 s)`** (sky only, `:1067-1090`), and only switch to idle at `:1061` | **PARTIAL (wrong frames)** | The dragon breathes flame while charging an orb. During the low-altitude "反撃のチャンス!" recovery it still loops the fire-breath animation with a blue tint. |
| Phase change (`CheckBattlePhase`, `:908-928`) | n/a | n/a | n/a | n/a | Ring effect and shake only. No roar pose. |
| BREAK / stun (`AddStagger` to `StunRoutine`, `:949`) | n/a | n/a | n/a | n/a | idleFrames (flying) while it sits stunned near the ground. |

### 1b. MajinController (sky, `MajinController.cs` and `MajinController.Battle.cs`)

| Attack | Windup | Attack | Recover | Status | Notes |
|---|---|---|---|---|---|
| Telegraph (`TelegraphAndAttack`, `MajinController.cs:386-405`) | idleFrames plus flash for 3.0 s | n/a | n/a | **MISSING (windup)** | Same pattern as the dragon. |
| Fireball line or ring (`FireAttack`, `:424-475`) | attackFrames start 0.5 s before the shots | attackFrames loop (arms-spread roar, 8 frames at 10 fps) | idleFrames plus a dark `recoveryTint` | **PARTIAL** | The windup and the attack are the same loop. Recover is the idle loop with a tint. |
| Claw swoop, phase 1+ (`ClawSwoop`, `Battle.cs:151-177`) | attackFrames during a 0.45 s glide plus a purple flash | attackFrames while the `CaveHazard.Band "Majin:claw"` slash hits (0.7 s warn) | attackFrames with a dark tint for the 1.4 s "counter window" | **PARTIAL (no claw motion)** | The only motion is the generic arms-spread loop. No pose swings a claw. Recovery also shows the attack loop. |
| Teleport circles, phase 2 (`TeleportCircles`, `:179-203`) | attackFrames | attackFrames during the low and high circles | idleFrames | PARTIAL | A casting loop that isn't synced to the two circles. |
| Ultimate SKY MAGIC ASSAULT (`SkyMagicAssault`, `:205-270`) | attackFrames | attackFrames during the circles, shots, low and high bands, and the final **body dash** (`CaveHazard.Wave "Majin:dash"`) | `DescendExposed`: idle plus tint | PARTIAL | The final dash shows the arms-spread loop sliding across the screen. |

### 1c. MechDragonBoss (`Bosses/MechDragonBoss.cs`)
| Attack | Windup | Attack | Recover | Status |
|---|---|---|---|---|
| LaserShot (`:224-276`) | `laser_charge` | `laser_fire` | `laser_cool` (Cooldown) | OK (one image per phase) |
| HighSweep (`:323-372`) | `laser_charge` | `laser_fire` | idle | OK |
| Vulcan (`:374-431`) | `vulcan_prep` | `vulcan_fire` | `vulcan_end` | OK |
| AirRaid (`:446-476`) | `bomb_prep` | `bomb_drop` (kept through Recover) | `bomb_drop` | OK |
| Barrage / OVERKILL PROTOCOL (`:561-586`) | reuses the above | reuses the above | `laser_cool` plus Steam | OK |

---

## 2. Sky bosses (`SkyBosses.cs`, `SkyBossesBattle.cs`; three still images each, recover = idle)

| Boss | Attack | Windup | Attack | Recover | Status | Notes |
|---|---|---|---|---|---|---|
| Behemoth | ChargeAttack `SkyBosses.cs:285-297` | windup | attack (Strike) | idle | OK\* | |
| | ThunderCall `:299-312` | windup | attack (0.6 s) | idle | OK\* | The pose ends about 0.5-1.2 s before the bolts land (warn 1.1-1.8 s). |
| | ShockStomp `:314-327` | windup | attack | idle | OK\* | |
| | ElectricTrail (special) `SkyBossesBattle.cs:39-57` | windup | attack (Swoop) | idle | OK\* | |
| | THUNDER APOCALYPSE `:59-107` | windup | one PlayAttackPose (`:74`) at the bolts | idle | **PARTIAL** | The ground waves (front and back) and the air band (`:77-90`) play while the attack image is held. The final charge is OK. |
| Titan | KneelAndSlam `SkyBosses.cs:423-451` | windup | attack 0.9 s | **idle during the kneel hold** | PARTIAL | The "fist planted, low stance" counter window shows the idle image. |
| | FistSlam `:453-461` / Gust `:463-475` / ThunderSpear `:516-530` | windup | attack | idle | OK\* | |
| | SpearRain `SkyBossesBattle.cs:119-137` | windup | attack at the spears | idle | PARTIAL | The energy wave (`:134-136`) has no pose. |
| | JUDGMENT OF TITAN `:140-180` | windup | one attack (`:149`) | idle | **PARTIAL** | One pose covers the shockwave, floor cracks, spear rain and the **giant hand sweep** (`:166-170`, about 6 s). The hand sweep has no pose change. |
| Jellyfish | TentacleLash `SkyBosses.cs:631-637` / ElectricShock `:639-650` / LightningOrbs `:652-670` | windup | attack | idle | OK\* | |
| | TentacleField (special) `SkyBossesBattle.cs:204-219` | windup (held) | **none** | idle | **MISSING** | `Telegraph(0.8)` is followed by 3 timed tentacle Floor/Band hazards with no PlayAttackPose. |
| | HEAVENLY PULSE `:221-258` | windup (held about 8 s) | **none** | idle | **MISSING** | Low and high waves, the orb spread (`:244-247`) and the full-circle pulse all happen without an attack pose. |
| Leviathan | BurstUp `SkyBosses.cs:850-874` / CrossSweep `:876-906` / CloudBreath `:923-947` | windup or Emerge | attack (Strike or CrossBody) | Landing (idle) | OK\* | |
| | DeepApproach / LEVIATHAN CROSSING `SkyBossesBattle.cs:271-335` | | | | OK\* / n/a | It is submerged and invisible during the waves. The final breath has PlayAttackPose (`:325`). |
| Fenrir | GodspeedBite / RoarWave / RushPass `SkyBosses.cs:1037-1117` | windup | attack | idle | OK\* | |
| | SkyLeap `:1052-1067` | windup | **idle during Leap and landing** (Fly falls back to idle, no move image) | Landing (idle) | PARTIAL | The landing SkyStrike hits while the body is idle. |
| | SkyRun (special) `SkyBossesBattle.cs:352-376` | windup **held through the 1.4 s run** | **none**: Leap, then the landing bite (StrikeAt) in idle | Landing | **MISSING** | |
| | FENRIR SKY HUNT `:378-436` | windup | attack (Swoop ×2, giant bite Strike) | Landing | OK\* | |
| SkyGolem | FistSlam / FloatingRocks / CoreCannon `SkyBosses.cs:1251-1331` | windup | attack | idle | OK\* | |
| | Disassemble (special) `SkyBossesBattle.cs:471-486` | windup (held, alpha 0.6) | **none** | idle | **MISSING** | Two rock waves and a falling rock happen with no attack pose. |
| | SKY FORTRESS `:488-533` | (faded and invulnerable during the waves) | fist StrikeAt and wave after a Telegraph with **no attack pose** (`:511-518`). The beam Strike is OK | idle | PARTIAL | |
| Phoenix | DiveAttack / FlameFeathers / FlameRush / FlameTrail | windup | attack | Landing or idle | OK\* | |
| | PHOENIX REBIRTH `SkyBossesBattle.cs:552-582` | windup | one attack (`:564`) | idle | PARTIAL | The attack image is held through the pillars and wave (about 6-8 s). The FlameRush that follows is OK. |
| SkySerpent | CloudBite / BodyCross / ThunderBreath / ThunderClouds / StormSurge | windup | attack | idle | OK\* | |
| | HEAVEN SERPENT STORM `:620-664` | | the roar on Appear (`:630-633`) has no pose. The final breath is OK (`:658`) | | PARTIAL | It is vanished for most of the sequence. |
| Guardian | LightSword / SkySlash / AerialStrike / LightPillars / GuardianCombo `SkyBosses.cs:1862-1932` | windup | attack | Landing or idle | OK\* | |
| | HEAVEN'S JUDGMENT `SkyBossesBattle.cs:694-733` | windup (**held about 11 s**) | **none** | idle | **MISSING** | There is no PlayAttackPose at all. Pillars, two waves, the floor, and the final **sword wave** under the swordGlow (`:722-727`) all play on the held windup image. |

---

## 3. Wasteland bosses (`WildBosses.cs`, BossAnim)

| Boss | Attack | Windup | Attack | Recover | Status | Notes |
|---|---|---|---|---|---|---|
| Wolf | Bite `:72-88` | windup_ ×3 | attack_ ×4 | holds the last attack frame | OK | |
| | Ultimate 疾風の追い越し突進 (`OvertakeCharge`, `:90-142`) | ult_00 (Telegraph) | ult_01 (Strike) | break_ (Exhausted) | **PARTIAL** | Because of `UltimateRunning`, the Move pose shows **the static ult_01 frame** while the wolf runs backwards and leaps over the player (`:94-113`, up to about 5 s). The run cycle stops. |
| GoblinRider | Club `:196-200` / CastMagic `:225-247` / RideCharge (ult_ ×3 loop) `:207-223` | yes | yes | recover_ | OK | After CastMagic, `Recover` shows the **club** `recover_` frame (the RecoverFor fallback). |
| Serpent | Bite / Charge (B) / TailSweep (C) / BurrowAttack (dive_, emerge_) `:318-391` | yes | yes | recover_ | OK | Charge and Tail reuse the bite recover_. |
| | Ultimate 大地を裂く大蛇 (`EarthRender`, `:393-448`) | dive_ | emerge_ for 1 s, then the bite attack frame is held | Exhausted | PARTIAL | The head, body and tail that cross the lane are generic Fang/Orb/Slash `LaneWave` effects. There are no ult frames. |
| Cyclops | Slam / Stomp / Sweep / DoubleStomp `:490-532` | yes | yes | recover_ | OK | |
| | Ultimate 天崩しの大棍棒 (`SkyfallClub`, `:543-581`) | ult_00 | ult_01, held by `holdSlamArt` | exhaust_ | **PARTIAL (bug)** | `BeginUltimate` runs before `MoveToGap(10,4,2.2)` (`:545-547`), so the cyclops **walks for up to 2.2 s showing ult_01 (club smashed into rubble)**. |
| Spider | Bite / Leg / JumpAttack / WebShot / DeathDance `:655-783` | yes | yes (SpiderArt air, land, skitter) | recover_ | OK | |
| Golem | 4 normal moves / BoulderToss `:875-927` | yes | yes | recover_ | OK | |
| | Ultimate 大地の怒り (`EarthWrath`, `:930-961`) | windupD | attackD (Strike), then **EndAttack → Idle** | exhaust_ | **PARTIAL** | The 7 rock pillars (`:941-946`) and the **giant boulder throw** (`:948-951`) play in idle or move. The boulder throw has no throw pose. attackB (the throw) exists and could be reused. |
| Griffin | Claw / Charge / DiveSequence (D plus land_) / FeatherStorm / TripleSwoop `:1029-1157` | yes | yes | exhaust_ or land_ | OK | |
| Hydra | Volley / VenomSpit / TriHeadAssault (C, D, ult_) `:1216-1336` | yes | yes | exhaust_ | OK | |
| Demon | Claw / MagicVolley / GroundMagic / PillarMarch / InfernalCircle `:1375-1484` | yes | yes | recover_ or exhaust_ | OK | |
| BlackKnight | Slash, double, dash, up, air (A-E, fly_), MimicAerialCombo, DarkTripleRush (ult_) `:1526-1643` | yes | yes | recover_ or exhaust_ | OK | |

---

## 4. Cave bosses (`CaveBosses.cs`, BossAnim)

| Boss | Attack | Windup | Attack | Recover | Status | Notes |
|---|---|---|---|---|---|---|
| Centipede | Bite / Body slam / Rush `:60-86` | yes | yes | recover_ | OK | |
| | CeilingChase (special) `:88-110` | none | rocks are dropped (`RockAt`) while the move frames crawl upside down. The drop shock (`ShockFromFront`) uses only the Landing squash | recover_ | PARTIAL | |
| | CAVE CRAWLER `:112-159` | | ceiling run and drop shock have no pose. The low rush (C) and tall rush (windupD, then Swoop attackD) are OK | exhaust (CaveRecovery) | PARTIAL | |
| Scorpion | Claw / Sting / Rush / PoisonBolt / PoisonRain `:219-271` | yes | yes | recover_ | OK | |
| | SCORPION DEATH ZONE `:273-300` | | ② the overhead sting (`CeilAt`, `:285-289`) happens while the **claw** attack frame (variant 0) is held | exhaust_ | PARTIAL | It should switch to variant 1 (the sting). |
| Mole | Claw / Rush / BurrowEmerge / FakeBurrow / UNDERGROUND HUNT `:327-391` | yes | yes (dive_, emerge_) | recover_ or exhaust_ | OK | |
| Troll | Slam / Sweep / Stomp / CeilingStrike `:424-472` | yes | yes | recover_ | OK | |
| | CAVE COLLAPSE `:474-508` | windupD | one PlayAttackPose (`:482`), then **attackD is held for about 7 s** through 3 collapse/crack waves and the rocks | exhaust_ | PARTIAL | The final mega slam is OK. |
| Worm | 3 normal moves / CrossBody / EARTH BREAKER `:542-631` | yes | yes (emerge overrides) | recover_ or exhaust_ | OK | The crossing body is drawn as hazard pieces using worm art (`piece_*`). |
| CrystalGolem | Punch / Slam / CrystalBolt / CrystalField `:675-734` | yes | yes | recoverB or last frame | OK | |
| | CRYSTAL PRISON `:736-774` | ult_00 | ②③ the floor and ceiling crystals (`:747-756`) **hold ult_00 (arms crossed, windup)**. ult_01 (the cage) only appears at ⑤ | exhaust_ | PARTIAL | The opening `MoveToGap` (`:739`) can show ult_01 while walking if the previous move left animVariant at 0. |
| Bat | ClawPass / DiveAttack / SonicAttack / CeilingHang (land_) / ECHO HUNT `:838-955` | yes | yes | exhaust_ | OK | It is offscreen during the ECHO HUNT rings. |
| ScorpionKing | Claw / Sting / Bolt / SpikeRow / MultiPoison / TailCombo `:995-1065` | yes | yes | exhaust | OK | |
| | KING'S EXECUTION `:1067-1099` | | ② spikes and overhead stingers hold attackD. **③ the poison rain (`FakeShot`, `:1087-1089`) has no PlayAttackPose and follows a MoveToGap, so it plays in idle or move** | exhaust_ | PARTIAL (③ is MISSING) | MultiPoison (`:1045`) already does it correctly with `PlayAttackPose` per shot. |
| Basilisk | Bite / Tail / Breath / Gaze `:1152-1191` | yes | yes | exhaust | OK | |
| | PETRIFYING GAZE `:1193-1223` | | ② the gaze wave and ③ the stone pillars hold the gaze attack frame | exhaust_ | PARTIAL | |
| Drake | Bite / Claw / Breath / Rush / UndergroundBurst / TailCeiling `:1258-1310` | yes | yes | recover_ | OK | |
| | CAVERN DRAGON RAGE `:1312-1343` | ① the "roar" is the breath windup (windupC) | **no attack or roar pose**. windupC is held through ② the rocks and ③ the floor crack (about 2.7 s) | exhaust_ | PARTIAL | ④ to ⑥ are OK. |
| AncientDemon | Claw / MagicShots / GroundCircle / Teleport / ABYSSAL CAVERN `:1379-1484` | yes | yes (ult_) | exhaust | OK | |
| | FloorCeilingMagic (special) `:1460-1469` | windupC (held) | **none** | idle | **MISSING** | The floor magic, ceiling magic and the second floor (when enraged) all play with no PlayAttackPose. |

---

## 5. Reaper sisters (lower priority; `LastDungeon/ReaperSisterBoss*.cs`)
Most moves are OK, since every move goes through Telegraph and then Strike or PlayAttackPose, with recover and recoverB frames.
- **PARTIAL**: `ComboTurn` for the Second sister (`ReaperSisterBoss.Moves.cs:425-428`). It goes `Telegraph(0.55)`, then `FireArc`, with no PlayAttackPose, straight to `Recover`. The body goes from windup to recover and skips the attack frame.

---

## 6. Problems that affect many bosses
1. **Telegraph leaves the boss in Windup** (`WildBossBase.cs:927-944`). Any hazard sequence after a Telegraph that has no Strike or PlayAttackPose freezes on windup frame 0 or the still windup image. This causes the sky MISSING items (Jellyfish ×2, Fenrir SkyRun, SkyGolem Disassemble, Guardian ultimate) and the AncientDemon and Drake items.
2. **The ultimate frames take over the Move pose** (`WildBossBase.cs:510-517`). This affects Wolf (OvertakeCharge), Cyclops (SkyfallClub walk) and possibly CrystalGolem (CrystalPrison). Hydra, Demon and AncientDemon already avoid it by setting animVariant before moving.
3. **`RecoverFor` falls back to the move-0 `recover_`** (`Bosses/BossAnimSet.cs`). After moves B, C and D, the boss briefly (0.25-0.6 s) shows another move's recover frame. This affects goblin_rider, serpent, cyclops, spider, golem, troll, mole, scorpion, centipede, worm, drake, demon and black_knight.
4. **The sky bosses have only 3 still images.** Fly, Landing, Recover and Move all show idle.
5. **Dragon and Majin** have no pose system and only 3 or 2 frame sets, which are baked into the scene. New frames need either a scene rebuild or Resources loading like `MechDragonBoss.LoadArt`.

---

## 7. Counts
| Family | OK | PARTIAL | MISSING |
|---|---|---|---|
| Dragon (sky and wasteland, same code) | 0 | 3 (Fire, Charge, GiantFireball) | 1 (LandingAttack), plus the 3 s telegraph has no windup pose |
| Majin | 0 | 4 | 0 (but the telegraph has no windup pose, and Claw has no claw motion) |
| MechDragon | 5 | 0 | 0 |
| Sky, 9 bosses | 36 (OK\*) | 8 | 5 |
| Wasteland, 10 bosses | 42 | 4 | 0 |
| Cave, 11 bosses | 51 | 8 | 1 (plus the KING'S EXECUTION ③ segment) |
| Reaper sisters | most | 1 | 0 |
| **Total** | | **28** | **7** |

---

## 8. Prioritized fix list

1. **Dragon LandingAttack: MISSING** (`DragonController.cs:389-459`).
   - Now: reuse **DragonCharge** (charge_00-06, an upright rearing stance that is already scaled to the same world size, PPU 227 vs 175 for 1168 vs 900 px). Use it for the grounded hold. Use charge_00-03 (head pulled back) as the bite windup and charge_04-06 (mouth opening) as the bite strike.
   - New frames needed:
     - a descend-and-land frame (wings flared, legs out)
     - a landing impact frame
     - a real bite (head lunging forward)
     - a takeoff frame
   - Load them from Resources the way `MechDragonBoss.LoadArt` does, to avoid rebuilding the scene.
2. **Dragon telegraph, 3 s of idle plus flash** (`:577-600`). Switch to chargeFrames (rear back) or new inhale frames during the telegraph, and keep the flash as an extra cue.
3. **Dragon Fire** (`:657-690`). The fire frames have the flame drawn into every frame.
   - New frames needed:
     - an inhale or windup frame with no flame (the head and mouth glow can stay)
     - a mouth-closing recover frame
   - Show a fire frame only on each shot, or loop fire only while shots are spawning.
4. **Dragon GiantFireball and LowRecovery: wrong frames** (`:1022`, `:1043-1062`, `:1067-1090`).
   - Change the code: call `SetFrames(idleFrames)` (or a future exhaust frame) at the release or before `LowRecovery`, so the counter window doesn't show fire breathing.
   - Use chargeFrames for the orb charge instead of fireFrames.
   - Ideally add new frames: an orb-charge pose (mouth wide, no breath) and an exhausted low-hover pose.
5. **Dragon ChargeAttack** (`:605-623`). The rearing pose is used for the dive. A dive or swoop frame would be new art.
6. **Sky MISSING items** (code-only fixes that reuse the existing attack image): add `PlayAttackPose` on each hazard wave.
   - Jellyfish TentacleField (`SkyBossesBattle.cs:210-216`) and HEAVENLY PULSE (`:232-249`)
   - Fenrir SkyRun: `PlayAttackPose` at the landing bite (`:369-373`)
   - SkyGolem Disassemble (`:476-482`)
   - Guardian HEAVEN'S JUDGMENT: pillars (`:702`), the waves, and especially the final sword wave (`:726`)
7. **Majin Claw and Telegraph**.
   - Claw is MISSING in practice. MajinAttack only has an arms-spread roar loop.
   - New frames needed: a claw swing (raise → slash → follow-through) and a windup (gathering magic).
   - Change the code: show idleFrames, not the attack loop, during the 1.4 s claw recovery.
8. **Golem EarthWrath** (`WildBosses.cs:940-951`). Set `animVariant = 1` and call `PlayAttackPose` at the boulder throw (attackB = the throw already exists). Call `PlayAttackPose` with variant 2 (ground pound) at the start of the pillars instead of idling.
9. **Cyclops SkyfallClub walk** (`WildBosses.cs:545-547`) and **Wolf OvertakeCharge** (`:94-113`). Change the code: set a non-zero animVariant while moving, the way Hydra does at `:1254` and Demon at `:1429`, or block the ult override for Move in `PickAnimSprite` (`WildBossBase.cs:514-516`). The wolf could also keep its normal `move_` run cycle during the retreat and the jump-over.
10. **Cave ultimate segments**:
    - ScorpionKing ③: add `animVariant=2; PlayAttackPose(0.15f)` per `FakeShot` (`CaveBosses.cs:1088`), copying MultiPoison.
    - Scorpion DeathZone ②: `animVariant=1; PlayAttackPose` at `CeilAt` (`:286`).
    - CrystalGolem Prison ②③: `PlayAttackPose` after the Telegraph at `:747`, so it shows ult_01 (the cage).
    - AncientDemon FloorCeilingMagic: `PlayAttackPose(0.25f)` on each Floor or Ceil (`:1463-1466`).
    - Drake DragonRage ①: the roar needs an attack pose. Reuse attackC (breath) or new roar art.
    - Troll CaveCollapse: re-trigger `PlayAttackPose` (variant 3) on each collapse wave.
11. **Make Recover follow the move** (`BossAnimSet.RecoverFor`). Either fall back to holding the last attack frame for variants ≥ 2 (and for 1 when recoverB is missing), or add recoverB/C art. A code-only option: return `null` when `v != 0` and no matching recover frame exists.
12. **Sky ultimates that hold one image for 6-11 s** (Titan Judgment, Behemoth Apocalypse, Phoenix Rebirth) are PARTIAL. Re-call `PlayAttackPose` for each wave. Real motion would need new frames: all 9 sky bosses would need move, recover and per-move windup/attack frames, since none have a BossAnim folder.
