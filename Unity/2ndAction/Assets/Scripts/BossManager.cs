using UnityEngine;

// Watches run distance and triggers a boss checkpoint every bossRepeatInterval
// starting from the very first one, freezing regular enemy spawning and
// spawning however many dragons/majins that checkpoint calls for. The
// counts are derived purely from the checkpoint's index rather than
// tracked incrementally, so the schedule always matches:
//
//   checkpoint 1 (1000m): dragon x1
//   checkpoint 2 (2000m): dragon x2
//   checkpoint 3 (3000m): dragon x3
//   checkpoint 4 (4000m): dragon x4
//   checkpoint 5 (5000m): majin x1 (dragon count wraps back to 0)
//   checkpoint 6 (6000m): majin x1, dragon x1
//   checkpoint 7 (7000m): majin x1, dragon x2
//   checkpoint 9 (9000m): majin x1, dragon x4
//   checkpoint 10 (10000m): majin x2 (dragon count wraps back to 0)
//   checkpoint 11 (11000m): majin x2, dragon x1
//   ...
//
// i.e. majinCount = checkpointIndex / majinCycleLength (integer division),
// dragonCount = checkpointIndex % majinCycleLength.
public class BossManager : MonoBehaviour
{
    public static BossManager Instance { get; private set; }

    public Transform player;
    public Sprite squareSprite;
    public Sprite[] dragonIdleFrames;
    public Sprite[] dragonChargeFrames;
    public Sprite[] dragonFireFrames;
    public Sprite[] majinIdleFrames;
    public Sprite[] majinAttackFrames;

    // Boss Defeat Presentation pass - shared between Dragon/Majin (see
    // DragonController/MajinController's own finalHitSparkSprite/
    // bossDeathSmokeSprite fields, which these get copied into at spawn).
    public Sprite bossHitSparkSprite;
    public Sprite bossDeathSmokeSprite;
    // No dedicated files yet - PlaySfx is null-safe, so these can stay
    // unassigned without breaking anything (see SceneBuilder).
    public AudioClip bossFinalHitSe;
    public AudioClip bossDefeatSe;

    // Distance Level Design Ver.1, item 7 - Mechanical Dragon. Reuses
    // DragonController entirely (entrance/HP/hit/death Presentation all
    // identical - see DragonController.attacksEnabled) with attacks
    // disabled, per "具体的な攻撃追加は後で設計" / "出現確認用の最低限
    // Placeholder Behaviorでも構いません". null-safe - never spawns if the
    // sprite hasn't been imported yet.
    public Sprite mechanicalDragonSprite;
    public float mechanicalDragonUnlockDistance = 20000f;
    public int mechanicalDragonMaxHp = 30;
    // Distance Level Design Ver.1.1 - best-guess default; flip in the
    // Inspector if it still faces the wrong way once seen in Game View.
    public bool mechanicalDragonDefaultFacingRight = true;
    public bool deathDefaultFacingRight = true;

    // Distance Level Design Ver.1, item 8 - Death/Grim Reaper. Explicitly
    // NOT a fight - no collider, no damage, no chase, no HP - "戦闘仕様・
    // 無敵仕様・追跡速度などは後で設計" per the brief; this is only the
    // "100,000m検知 -> Death出現 -> ゲーム継続" confirmation the brief asks
    // for this pass, nothing more. null-safe - never spawns without art.
    public Sprite deathSprite;
    public float deathSpawnDistance = 100000f;
    public float deathSpawnBehindPlayer = 6f;
    bool deathSpawned;

    public float bossRepeatInterval = 1000f;
    public float bossRepeatIntervalDebug = 200f;
    // Every this-many checkpoints, the majin count steps up by one and the
    // dragon count wraps back to 0 (see the table above).
    public int majinCycleLength = 5;

    // Bugfix 2026-09-06 - "Boss→現状より明確に巨大であること". Dragon/
    // MechanicalDragon/Death (GrimReaper) share this single scale field -
    // no separate Visual child exists for any of them (SpriteRenderer/
    // Collider/Rigidbody all live on the same Transform, see SpawnDragon's
    // own comment), so this is the one existing lever for their size and it
    // scales the Collider right along with the Visual, same as it always
    // has - not a new side effect introduced here.
    public float dragonScale = 2.3f;
    public float dragonStandoffDistance = 8f;
    public float dragonSpacing = 4f;
    public float dragonScatterJitter = 2f;
    public int dragonMaxHp = 20;

    // Majin sits further back than the dragons by default (dragonStandoffDistance
    // is its own zero point) so a mixed encounter reads as two distinct
    // depths rather than everything piled on the same line.
    public float majinScale = 2.7f; // Bugfix 2026-09-06 - see dragonScale's own comment
    public float majinStandoffDistance = 16f;
    public float majinSpacing = 6f;
    public float majinScatterJitter = 2.5f;
    // maxHp is set per-encounter as dragonMaxHp * majinHpMultiplier rather
    // than a fixed field, so tuning the dragon's HP keeps the two in sync.
    public float majinHpMultiplier = 6f;

    // Safety net for the scatter: caps the nearest boss's distance so at
    // least one of each type is always visible the moment the encounter
    // starts, regardless of how the random jitter/shuffle played out.
    public float maxGuaranteedVisibleDistance = 16f;

    public bool IsBossPhase { get; private set; }
    // Bugfix 2026-09-06, item "Boss戦中Distance停止" - called by GameManager
    // exactly where Boss Reward processing actually finishes (SaveCheckpoint
    // time), NOT at the boss's own death - see CheckEncounterComplete's own
    // comment for why IsBossPhase itself no longer flips false there.
    public void EndBossPhase() => IsBossPhase = false;
    public int BossesDefeated { get; private set; }
    public float NextBossDistance => nextBossDistance;
    // Bugfix 2026-09-08 (Bug #001 診断フェーズ) - read-only surface for
    // BossDiagnostics' Freeze Snapshot ("AliveBossCount"/"CurrentBossCount").
    public int AliveDragonCount => Mathf.Max(0, aliveDragonsThisEncounter);
    public int AliveMajinCount => Mathf.Max(0, aliveMajinsThisEncounter);
    public int AliveBossCount => AliveDragonCount + AliveMajinCount;

    float nextBossDistance;
    int aliveDragonsThisEncounter;
    int aliveMajinsThisEncounter;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        nextBossDistance = EffectiveStartDistance();
    }

    float EffectiveRepeatInterval()
    {
        return GameManager.Instance != null && GameManager.Instance.DebugMode ? bossRepeatIntervalDebug : bossRepeatInterval;
    }

    // First encounter is checkpoint 1 (one interval in), matching the
    // original dragon-only pacing the player is used to - not
    // majinCycleLength intervals in, which skipped every encounter before
    // the first majin appeared (checkpoints 1-4 never triggered at all).
    float EffectiveStartDistance()
    {
        return EffectiveRepeatInterval();
    }

    void Update()
    {
        if (GameManager.Instance == null || !GameManager.Instance.HasStarted || GameManager.Instance.IsGameOver) return;

        // Distance Level Design Ver.1, item 8 - "100,000m検知 -> Death出現
        // -> ゲーム継続". Deliberately checked BEFORE the IsBossPhase guard
        // below (and independent of the 1000m checkpoint cycle entirely) -
        // Death is a one-time event that should still fire even if a
        // regular boss encounter happens to be in progress right at
        // 100,000m, and must never block or get blocked by it. Does NOT
        // end/pause the run - "100,000m到達でRunを強制終了しない".
        if (!deathSpawned && GameManager.Instance.MaxDistance >= deathSpawnDistance)
        {
            deathSpawned = true;
            if (GameManager.Instance.DebugMode) Debug.Log($"[Distance] {Mathf.RoundToInt(deathSpawnDistance)} reached");
            SpawnDeath();
        }

        if (IsBossPhase) return;
        if (player == null || dragonIdleFrames == null || dragonIdleFrames.Length == 0) return;

        if (GameManager.Instance.MaxDistance >= nextBossDistance)
        {
            // Reserved immediately (not inside StartBossPhase any more) so
            // this Update() guard (top of the method) stops re-triggering
            // on the very next frame while the Presentation pass is still
            // playing - MaxDistance stays >= nextBossDistance continuously
            // until the encounter actually ends, well after this moment.
            IsBossPhase = true;
            if (GameManager.Instance != null) GameManager.Instance.LogBoss("PhaseStart");

            // Distance Level Design Ver.1.1, item 1 - Boss Gate locks here:
            // snap Distance to EXACTLY this checkpoint (not whatever
            // fractional overshoot triggered this frame) - see
            // GameManager.ReportDistance's own early-return, which is what
            // actually keeps it frozen at this value for the rest of the
            // fight.
            if (GameManager.Instance != null)
            {
                GameManager.Instance.ClampMaxDistanceTo(nextBossDistance);
                // Bugfix 2026-09-06, item "Boss中Distanceの根本修正" - marks
                // the exact moment raw Player movement stops counting
                // toward Distance (see GameManager.BeginBossDistanceExclusion's
                // own comment for why this is needed on top of the
                // IsBossPhase freeze above).
                GameManager.Instance.BeginBossDistanceExclusion();
            }

            // Boss Milestone Presentation pass - wraps the existing
            // StartBossPhase (untouched below) as a callback; falls back to
            // calling it directly if the scene was built before this
            // manager existed, so a boss can never fail to spawn.
            int checkpointIndexForPresentation = Mathf.RoundToInt(nextBossDistance / EffectiveRepeatInterval());
            bool isFirstEncounter = checkpointIndexForPresentation <= 1;
            if (BossMilestonePresentation.Instance != null)
            {
                BossMilestonePresentation.Instance.Play(nextBossDistance, isFirstEncounter, StartBossPhase);
            }
            else
            {
                StartBossPhase();
            }
        }
    }

    void StartBossPhase()
    {
        IsBossPhase = true; // idempotent - already set above when the presentation exists

        int checkpointIndex = Mathf.RoundToInt(nextBossDistance / EffectiveRepeatInterval());
        int majinCount = checkpointIndex / majinCycleLength;
        int dragonCount = checkpointIndex % majinCycleLength;

        aliveMajinsThisEncounter = majinCount;
        aliveDragonsThisEncounter = dragonCount;

        // Auto-run stays on during the fight; each boss tracks the player's
        // base auto-run speed every frame so it holds a constant distance
        // while running, and only an attack lunge/recoil actually changes
        // the gap.
        if (TerrainManager.Instance != null) TerrainManager.Instance.ClearAllEnemies();

        float[] dragonDistances = BuildScatteredDistances(dragonCount, dragonStandoffDistance, dragonSpacing, dragonScatterJitter);
        for (int i = 0; i < dragonCount; i++)
        {
            SpawnDragon(dragonDistances[i]);
        }

        float[] majinDistances = BuildScatteredDistances(majinCount, majinStandoffDistance, majinSpacing, majinScatterJitter);
        for (int i = 0; i < majinCount; i++)
        {
            SpawnMajin(majinDistances[i]);
        }

        // Distance Level Design Ver.1, item 7 - "20,000m前後 Mechanical
        // Dragon初登場" / "20,000〜99,999m Dragon/魔人/Mechanical Dragonを
        // 距離に応じて使用". Added ON TOP of the existing dragon/majin
        // count formula above (untouched) rather than replacing any of it -
        // every encounter from mechanicalDragonUnlockDistance onward also
        // gets exactly one, in addition to whatever the original formula
        // already produces.
        if (nextBossDistance >= mechanicalDragonUnlockDistance && mechanicalDragonSprite != null)
        {
            aliveDragonsThisEncounter++;
            SpawnMechanicalDragon(dragonStandoffDistance + dragonSpacing * (dragonCount + majinCount + 1));
        }

        if (GameManager.Instance != null) GameManager.Instance.LogBoss("CombatStart");
    }

    // Lays out one evenly-spaced "slot" per boss so they can never end up
    // stacked on the exact same spot, jitters each slot, then shuffles the
    // assignment so the scatter doesn't read as "closest one always spawns
    // first".
    float[] BuildScatteredDistances(int count, float baseStandoff, float spacing, float jitter)
    {
        if (count <= 0) return new float[0];

        float[] distances = new float[count];
        for (int i = 0; i < count; i++)
        {
            float slotCenter = baseStandoff + i * spacing;
            distances[i] = Mathf.Max(2f, slotCenter + Random.Range(-jitter, jitter));
        }

        int nearestIndex = 0;
        for (int i = 1; i < distances.Length; i++)
        {
            if (distances[i] < distances[nearestIndex]) nearestIndex = i;
        }
        if (distances[nearestIndex] > maxGuaranteedVisibleDistance)
        {
            distances[nearestIndex] = baseStandoff;
        }

        for (int i = distances.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (distances[i], distances[j]) = (distances[j], distances[i]);
        }

        return distances;
    }

    void SpawnDragon(float standoffDistanceForThisDragon)
    {
        GameObject go = new GameObject("Dragon");
        go.tag = "Boss";
        go.transform.localScale = Vector3.one * dragonScale;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = RenderOrder.Boss;

        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(2.2f, 2.0f);
        var colDebug = go.AddComponent<ColliderDebugView>();
        colDebug.color = new Color(1f, 0.4f, 0f);

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        DragonController dragon = go.AddComponent<DragonController>();
        dragon.idleFrames = dragonIdleFrames;
        dragon.chargeFrames = dragonChargeFrames;
        dragon.fireFrames = dragonFireFrames;
        dragon.squareSprite = squareSprite;
        dragon.maxHp = EffectiveBossMaxHp(dragonMaxHp);
        dragon.standoffDistance = standoffDistanceForThisDragon;
        dragon.finalHitSparkSprite = bossHitSparkSprite;
        dragon.bossDeathSmokeSprite = bossDeathSmokeSprite;
        dragon.finalHitSe = bossFinalHitSe;
        dragon.bossDefeatSe = bossDefeatSe;

        // Bugfix 2026-09-05, item 6 - "Boss/EnemyがPlayer方向を向かない"
        // covered every OTHER species already (see EnemyFacing.cs's own
        // comment listing what it's applied to), but the real Dragon boss
        // never actually had this component attached at all - unlike
        // Mechanical Dragon below, which already does. Same reasoning as
        // that one: no separate Visual child (SpriteRenderer/Collider share
        // this Transform), col.size has no offset so flipping localScale.x
        // is safe, alwaysFacePlayer=true since the boss's own forward drift
        // isn't a meaningful walking direction. dragonIdleFrames' art
        // (idle_00.png) faces/bites LEFT natively, so defaultFacingRight=
        // false.
        var dragonFacing = go.AddComponent<EnemyFacing>();
        dragonFacing.visual = go.transform;
        dragonFacing.defaultFacingRight = false;
        dragonFacing.alwaysFacePlayer = true;
        dragonFacing.player = player;

        dragon.Init(player);
    }

    // Distance Level Design Ver.1 - visually distinct (Mechanical Dragon's
    // own art) but behaviorally a placeholder: idleFrames/chargeFrames/
    // fireFrames are all just a single-element array of the one static
    // sprite provided (DragonController's frame-cycling code handles a
    // length-1 array fine - it just never changes), and attacksEnabled is
    // false, so it appears, can be hit/killed (full Boss Defeat
    // Presentation included), but never attacks.
    void SpawnMechanicalDragon(float standoffDistanceForThisDragon)
    {
        GameObject go = new GameObject("MechanicalDragon");
        go.tag = "Boss";
        go.transform.localScale = Vector3.one * dragonScale;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = RenderOrder.Boss;

        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(2.2f, 2.0f);
        var colDebug = go.AddComponent<ColliderDebugView>();
        colDebug.color = new Color(0.2f, 0.6f, 1f);

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        DragonController dragon = go.AddComponent<DragonController>();
        Sprite[] singleFrame = { mechanicalDragonSprite };
        dragon.idleFrames = singleFrame;
        dragon.chargeFrames = singleFrame;
        dragon.fireFrames = singleFrame;
        dragon.squareSprite = squareSprite;
        dragon.maxHp = EffectiveBossMaxHp(mechanicalDragonMaxHp);
        dragon.standoffDistance = standoffDistanceForThisDragon;
        dragon.attacksEnabled = false;
        // Reward/MILE System Ver.1 - "ボスMILE: Mechanical Dragon 200".
        dragon.mileReward = 200;
        dragon.finalHitSparkSprite = bossHitSparkSprite;
        dragon.bossDeathSmokeSprite = bossDeathSmokeSprite;
        dragon.finalHitSe = bossFinalHitSe;
        dragon.bossDefeatSe = bossDefeatSe;

        // Distance Level Design Ver.1.1 - facing fix. No separate Visual
        // child here (DragonController's SpriteRenderer/Collider share this
        // same Transform) - flipping localScale.x's sign is still safe
        // since col.size has no offset (a symmetric box looks identical
        // either way). alwaysFacePlayer=true since a boss's own forward
        // drift (AdvanceTrackedX, matching the player's auto-run pace) is
        // not a meaningful walking direction to face toward.
        var facing = go.AddComponent<EnemyFacing>();
        facing.visual = go.transform;
        facing.defaultFacingRight = mechanicalDragonDefaultFacingRight;
        facing.alwaysFacePlayer = true;
        facing.player = player;

        dragon.Init(player);
        if (GameManager.Instance != null && GameManager.Instance.DebugMode) Debug.Log("[Boss] MechanicalDragon Spawn");
    }

    // Distance Level Design Ver.1, item 8 - see GrimReaperController's own
    // comment for exactly what this does/doesn't do. Positioned a fixed
    // distance BEHIND the player (not ahead, not on top of them) so it
    // reads as "something arrived from behind" without ever risking
    // overlapping the player or any terrain/enemy at the moment it spawns.
    void SpawnDeath()
    {
        if (deathSprite == null || player == null) return;
        Vector3 pos = player.position + new Vector3(-deathSpawnBehindPlayer, 1f, 0f);
        GrimReaperController.Create(deathSprite, pos, dragonScale, deathDefaultFacingRight, player);
        if (GameManager.Instance != null && GameManager.Instance.DebugMode) Debug.Log("[Boss] Death Spawn");
    }

    void SpawnMajin(float standoffDistanceForThisMajin)
    {
        GameObject go = new GameObject("Majin");
        go.tag = "Boss";
        go.transform.localScale = Vector3.one * majinScale;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = RenderOrder.Boss;

        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(2.4f, 2.6f);
        var colDebug = go.AddComponent<ColliderDebugView>();
        colDebug.color = new Color(0.6f, 0.1f, 0.7f);

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        MajinController majin = go.AddComponent<MajinController>();
        majin.idleFrames = majinIdleFrames;
        majin.attackFrames = majinAttackFrames;
        majin.squareSprite = squareSprite;
        majin.maxHp = EffectiveBossMaxHp(Mathf.RoundToInt(dragonMaxHp * majinHpMultiplier));
        majin.standoffDistance = standoffDistanceForThisMajin;
        majin.finalHitSparkSprite = bossHitSparkSprite;
        majin.bossDeathSmokeSprite = bossDeathSmokeSprite;
        majin.finalHitSe = bossFinalHitSe;
        majin.bossDefeatSe = bossDefeatSe;

        // Bugfix 2026-09-05, item 6 - same reasoning as SpawnDragon's
        // matching block above. majinIdleFrames' art is a roughly
        // front-facing symmetric pose (no strong left/right lean), so
        // defaultFacingRight's exact value barely changes the visual either
        // way here - kept at the class default (true) rather than guessing
        // a lean that isn't really there.
        var majinFacing = go.AddComponent<EnemyFacing>();
        majinFacing.visual = go.transform;
        majinFacing.alwaysFacePlayer = true;
        majinFacing.player = player;

        majin.Init(player);
    }

    // Only ends the boss phase once every dragon AND every majin spawned
    // this encounter is down. Does NOT end the game - the run just
    // continues.
    public void OnDragonDefeated()
    {
        BossesDefeated++;
        aliveDragonsThisEncounter--;
        CheckEncounterComplete();
    }

    public void OnMajinDefeated()
    {
        BossesDefeated++;
        aliveMajinsThisEncounter--;
        CheckEncounterComplete();
    }

    void CheckEncounterComplete()
    {
        if (aliveDragonsThisEncounter <= 0 && aliveMajinsThisEncounter <= 0)
        {
            // Bugfix 2026-09-06, item 2 - "Boss撃破後にゲームが停止する"
            // state-transition trace, stage 1/9.
            if (GameManager.Instance != null) GameManager.Instance.LogBossRewardStage("BossDefeated (CheckEncounterComplete entry)");
            if (GameManager.Instance != null) GameManager.Instance.LogBoss("Defeated");

            // Boss Defeat Presentation pass - captured BEFORE nextBossDistance
            // advances, so the Clear text reports the checkpoint that was
            // just cleared (1000m/2000m/...), not the next one.
            float clearedDistance = nextBossDistance;
            int checkpointIndex = Mathf.RoundToInt(nextBossDistance / EffectiveRepeatInterval());
            bool isFirstEncounter = checkpointIndex <= 1;

            // Bugfix 2026-09-06, item "Boss戦中Distance停止" - IsBossPhase
            // used to flip false right here, at the exact moment the last
            // boss dies - but GameManager.ReportDistance's Boss Gate guard
            // reads this SAME flag, so Distance (and Enemy Wall spawning,
            // and TerrainManager's safe-terrain suppression) all silently
            // resumed the instant the boss died, well before Boss Defeat
            // Presentation even started - let alone before the Boss Reward
            // card choice below actually finished. The brief's own flow is
            // "撃破 -> Defeat演出 -> Reward -> Gameplay復帰 -> Distance再開",
            // so this now stays true straight through both of those and
            // only clears via EndBossPhase(), called from GameManager at
            // the exact point Boss Reward processing itself completes
            // (SaveCheckpoint time - see ApplyUpgradeByCardId/
            // RunBossRewardChoice).
            nextBossDistance += EffectiveRepeatInterval();

            // Presentation only - fires once the encounter's LAST boss has
            // finished its own individual death presentation (see
            // DragonController/MajinController.FinalHitAndDie, which is
            // what actually calls OnDragonDefeated/OnMajinDefeated above,
            // now at the END of that short sequence instead of instantly).
            // Falls back to nothing (not an error) if the scene was built
            // before this manager existed - IsBossPhase/nextBossDistance
            // above are already fully updated regardless.
            if (BossDefeatPresentation.Instance != null)
            {
                BossDefeatPresentation.Instance.Play(clearedDistance, isFirstEncounter);
            }
            if (GameManager.Instance != null) GameManager.Instance.LogBossRewardStage("BossDefeatPresentation.Play returned (stage 2/9)");

            // Run Continuation/Checkpoint Ver.1, item 6/7 - "Boss撃破 ->
            // Boss Defeat Presentation -> BOSS REWARD". Reuses the exact
            // same Presentation-priority deferral Level Up already has (see
            // GameManager.TriggerBossRewardChoice) - it waits for the
            // BossDefeatPresentation banner just started above to finish
            // before actually pausing/showing cards, so the two never
            // visually fight each other.
            if (GameManager.Instance != null) GameManager.Instance.TriggerBossRewardChoice();
        }
    }

    // Run Continuation/Checkpoint Ver.1, item 7 - called once by
    // GameManager.BeginContinuedRun right after MaxDistance is set to the
    // Checkpoint's distance, so the Boss schedule resumes exactly where it
    // left off instead of re-initializing to EffectiveStartDistance() (which
    // would otherwise immediately misfire a Boss Gate at the player's actual,
    // far-ahead position). checkpointDistance is always itself a boss-clear
    // point (SaveCheckpoint only ever runs right after one), so the next one
    // due is simply one interval further - no need to re-derive
    // majinCount/dragonCount separately; StartBossPhase already re-derives
    // those purely from nextBossDistance/EffectiveRepeatInterval() the next
    // time a Boss Gate actually triggers.
    public void RestoreNextBossDistance(float checkpointDistance)
    {
        IsBossPhase = false;
        nextBossDistance = checkpointDistance + EffectiveRepeatInterval();
    }

    // Card Expansion/Gacha Evolution Ver.1 - "Boss Challenge"-family cards
    // (BossHpMultiplier) apply here, on top of whatever base HP the
    // existing Dragon/Majin/Mechanical Dragon formulas already compute.
    int EffectiveBossMaxHp(int baseHp)
    {
        float multiplier = GameManager.Instance != null ? GameManager.Instance.BossHpMultiplier : 1f;
        return Mathf.Max(1, Mathf.RoundToInt(baseHp * Mathf.Max(0.01f, multiplier)));
    }
}
