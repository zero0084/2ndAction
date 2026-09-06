using System.Collections.Generic;
using UnityEngine;

public class TerrainManager : MonoBehaviour
{
    public static TerrainManager Instance { get; private set; }

    public Sprite squareSprite;
    public Sprite groundSprite;
    public Sprite skyPathSprite;
    // Game Feel pass - shared across every spawned enemy (see
    // GroundFactory.CreateEnemy's hitSparkSprite/deathCloudSprite params),
    // set once here by SceneBuilder rather than needing each enemy to look
    // them up itself.
    public Sprite enemyHitSparkSprite;
    public Sprite enemyDeathCloudSprite;
    public Sprite enemyGroundShadowSprite;
    // Distance Level Design Ver.1 - Shooter Enemy's projectile. Reuses
    // FireballController (see EnemySpecialBehavior.UpdateShooter) rather
    // than a new projectile class; "簡易Sprite/既存VFX流用で構いません"
    // from the brief - null-safe (Shooter just skips firing if unset).
    public Sprite shooterProjectileSprite;
    // Game Feel pass, section 17 - visual-only ground clutter (see
    // DecorationScatter). Empty by default so a scene built before this
    // pass existed just spawns nothing here, not an error.
    public Sprite[] decorationSprites = new Sprite[0];
    // Legacy single-species fallback - still used whenever enemyPool is
    // empty/unset, or resolves to nothing currently unlocked, so this
    // system degrades to exactly the old single-goblin behavior if the
    // distance-unlock system's data is ever missing.
    public Sprite enemySprite;
    // Distance-unlock system - every EnemyDefinition SceneBuilder found in
    // EnemyDatabase (unlocked or not; unlock status is re-checked fresh at
    // each individual spawn via EnemyDatabase.PickRandomUnlocked, since a
    // new species can unlock mid-run).
    public List<EnemyDefinition> enemyPool = new List<EnemyDefinition>();
    public Transform player;

    // Visual Style Ver.1 floating-platform art (left-cap/mid-tile/right-cap)
    // - used for both the ground path and the sky path if set, taking over
    // from groundSprite/skyPathSprite above (which stay as the fallback if
    // this isn't assigned, so reverting means just clearing this field).
    public PlatformSpriteSet platformArt;
    public float platformVisualHeight = 3.5f;
    // How far below the platform art's canvas-top edge the actual walkable
    // grass line sits (measured from platform_mid.png: fully transparent
    // until row ~155/768, solid grass by row ~190/768) - without this, the
    // sprite's empty top margin gets anchored to the mathematical ground
    // line and everything standing on it (player/enemies) visibly floats
    // above the drawn grass. Only applies when platformArt is set.
    public float platformSurfaceInset = 0.85f;

    [Header("Chunk Sizes")]
    public float flatLength = 6f;
    public float slopeLength = 6f;
    public float slopeHeight = 2f;
    public float pitWidth = 3f;
    public float groundThickness = 1f;

    [Header("Generation")]
    public float generateAheadDistance = 30f;
    public float minEnemySpacing = 14f;
    public float pitChanceBase = 0.2f;
    public float enemySpawnChance = 0.5f;
    public float noEnemyBeforeDistance = 100f;

    [Header("Flying Enemies (dormant - see EnemyMovementType)")]
    // The only enemy art that exists today (the goblin) is drawn and
    // rigged as a ground creature, so it should never randomly spawn as
    // the airborne variant below - hence 0. Left in place, working, and
    // wired through EnemyController.movementType/EnemyAnimator so a future
    // flying enemy species only needs to raise this (or spawn its own
    // Flying-tagged enemy directly) rather than needing new plumbing.
    public float floatingEnemyChance = 0f;
    public float floatingEnemyHeight = 1.8f;
    // Ground-type enemies use the same per-frame foot pivot the player
    // uses, so 0 here means their feet sit exactly on the ground line with
    // no gap - matching the "no floating except when actually airborne"
    // rule the player follows. Flying-type enemies (see
    // floatingEnemyHeight above) are an intentional obstacle variety, not
    // a bug, and are unaffected by this.
    public float groundEnemyHeight = 0f;

    [Header("Difficulty Ramp (starts past a distance threshold)")]
    public float difficultyStartDistance = 1000f;
    public float pitChanceRampPer1000m = 0.08f;
    public float pitChanceMax = 0.55f;
    public float enemyChanceRampPer1000m = 0.15f;
    public float enemyChanceMax = 0.95f;

    [Header("Colors")]
    public Color groundColor = new Color(0.35f, 0.28f, 0.2f);
    public Color enemyColor = new Color(0.9f, 0.15f, 0.15f);

    [Header("Sky Path")]
    // Occasional elevated platforms floating above the main ground path,
    // reachable by jumping - a separate, parallel height-map (see
    // GetSkyHeightAt) rather than another ground ChunkType, since they
    // exist ABOVE the ground rather than replacing it at that X.
    public float skyPathHeightAboveGround = 2.6f;
    public float skyPathSegmentLength = 10f;
    public float skyPathGapMin = 15f;
    public float skyPathGapMax = 30f;
    public float skyPathStartDistance = 60f;

    enum ChunkType { Flat, UpSlope, DownSlope, Pit }

    class RuntimeChunk
    {
        public ChunkType type;
        public float startX, endX, startY, endY;
        public GameObject visual;
        // Distance Level Design Ver.1 - a List instead of a single
        // GameObject, since a Burst Formation (see DistanceTierManager)
        // can now place several enemies at one chunk instead of always
        // exactly one.
        public List<GameObject> enemies = new List<GameObject>();
        // Remembered so a visual can be rebuilt later with the same left
        // treatment (see AddRightCapToPreviousChunk, which retrofits a
        // right cap once a pit turns out to follow this chunk).
        public bool needsLeftCap;
    }

    class SkyChunk
    {
        public float startX, endX, y;
    }

    readonly List<RuntimeChunk> chunks = new List<RuntimeChunk>();
    readonly List<SkyChunk> skyChunks = new List<SkyChunk>();
    float nextStartX;
    float nextStartY;
    float nextSkyStartX;
    ChunkType lastType;
    float lastEnemyX = float.NegativeInfinity;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        nextStartX = 0f;
        nextStartY = 0f;
        nextSkyStartX = skyPathStartDistance;

        // Guaranteed safe runway before any obstacle.
        AddChunk(ChunkType.Flat, flatLength * 2f);

        while (nextStartX < generateAheadDistance)
        {
            GenerateNext();
        }
        while (nextSkyStartX < generateAheadDistance)
        {
            GenerateNextSkyChunk();
        }
    }

    void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;
        if (player == null) return;

        while (nextStartX < player.position.x + generateAheadDistance)
        {
            GenerateNext();
        }
        while (nextSkyStartX < player.position.x + generateAheadDistance)
        {
            GenerateNextSkyChunk();
        }

        // Old chunks are intentionally never destroyed: getting hit sends the
        // player back to the very start of the run, so the ground from x=0
        // onward has to stay intact and walkable the whole game, not just
        // whatever's near the player's current position.
    }

    // Clears any currently-alive regular enemies (used when the boss fight
    // starts, so the arena isn't cluttered with leftover ground enemies).
    public void ClearAllEnemies()
    {
        foreach (RuntimeChunk c in chunks)
        {
            for (int i = 0; i < c.enemies.Count; i++)
            {
                if (c.enemies[i] != null) Destroy(c.enemies[i]);
            }
            c.enemies.Clear();
        }
    }

    // If x lands inside a pit (no ground), returns the solid ground just
    // behind the pit's start edge instead - used to respawn the player on
    // solid footing after a fall, rather than back inside empty space.
    public float FindSafeRespawnX(float x)
    {
        foreach (RuntimeChunk c in chunks)
        {
            if (c.type == ChunkType.Pit && x >= c.startX && x <= c.endX)
            {
                return c.startX - 0.5f;
            }
        }
        return x;
    }

    // Elevated platform floating above the ground at x, if any - checked
    // alongside GetHeightAt (never instead of it) so the player can land on
    // whichever surface is actually beneath them.
    public float? GetSkyHeightAt(float x)
    {
        for (int i = 0; i < skyChunks.Count; i++)
        {
            SkyChunk c = skyChunks[i];
            if (x >= c.startX && x <= c.endX) return c.y;
        }
        return null;
    }

    void GenerateNextSkyChunk()
    {
        float startX = nextSkyStartX;
        float endX = startX + skyPathSegmentLength;
        float groundY = GetHeightAt((startX + endX) * 0.5f) ?? nextStartY;
        float y = groundY + skyPathHeightAboveGround;

        // Uses the cloud sprite instead of the regular ground texture, since
        // these platforms float in the sky rather than sitting on the
        // ground path.
        //
        // Game Feel refinement pass, section 15 - a new Cloud Platform.png
        // was provided as a visual-only candidate for this, but
        // CreateSlopeVisual's single-sprite fallback path (the only way to
        // actually use skyPathSprite instead of platformArt here) uses
        // SpriteDrawMode.Tiled sized from groundThickness/thickness - a
        // convention built for the old small repeating ground tile, not
        // this richer single "island" composition, and would need real
        // visual verification (Simple/stretch vs re-tuned Tiled sizing) to
        // get right rather than risk a distorted or duplicated-looking sky
        // path with no way to check it here. Left on platformArt (the same
        // art the ground path uses) per "無理に全箇所へ使用する必要はあり
        // ません" - Cloud Platform.png is imported and available
        // (LoadTiledSprite in Build()) for whenever this gets picked back
        // up with a way to actually see the result.
        GroundFactory.CreateSlopeVisual(transform, squareSprite, skyPathSprite, platformArt,
            new Vector2(startX, y), new Vector2(endX, y), groundThickness, platformVisualHeight, platformSurfaceInset, groundColor);

        skyChunks.Add(new SkyChunk { startX = startX, endX = endX, y = y });
        nextSkyStartX = endX + Random.Range(skyPathGapMin, skyPathGapMax);
    }

    public float? GetHeightAt(float x)
    {
        for (int i = 0; i < chunks.Count; i++)
        {
            RuntimeChunk c = chunks[i];
            if (x >= c.startX && x <= c.endX)
            {
                if (c.type == ChunkType.Pit) return null;
                float span = c.endX - c.startX;
                float t = span > 0.0001f ? (x - c.startX) / span : 0f;
                return Mathf.Lerp(c.startY, c.endY, t);
            }
        }

        return chunks.Count > 0 ? chunks[chunks.Count - 1].endY : (float?)0f;
    }

    // Visual-only slope angle (degrees) of whichever ground chunk sits
    // under x, matching the exact rotation GroundFactory already gives the
    // ground art itself, so a grounded character tilted by this angle
    // visually sits flush with the slope instead of only touching it at
    // one point. 0 for flat ground, a pit, or when x isn't over any chunk.
    public float GetSlopeAngleAt(float x)
    {
        for (int i = 0; i < chunks.Count; i++)
        {
            RuntimeChunk c = chunks[i];
            if (x >= c.startX && x <= c.endX)
            {
                if (c.type == ChunkType.Pit) return 0f;
                return Mathf.Atan2(c.endY - c.startY, c.endX - c.startX) * Mathf.Rad2Deg;
            }
        }

        return 0f;
    }

    void GenerateNext()
    {
        ChunkType type = PickNextType();
        float length = type switch
        {
            ChunkType.Flat => flatLength,
            ChunkType.UpSlope => slopeLength,
            ChunkType.DownSlope => slopeLength,
            ChunkType.Pit => pitWidth,
            _ => flatLength
        };
        AddChunk(type, length);
    }

    // Distance Level Design Ver.1.1, item 2 - a Formation that needs more
    // guaranteed-flat width than the chunk it started on RESERVES it here
    // (see RequestFlatRun/SpawnFormation) instead of hoping the terrain
    // generator happens to continue flat on its own - this is the actual
    // fix for "Formationが意図した形になっていない": every member is now
    // placed at an exact offset from one anchor, safely, because the whole
    // span is forced flat before any of it is used.
    int forcedFlatChunksRemaining;

    public void RequestFlatRun(int additionalFlatChunks)
    {
        forcedFlatChunksRemaining = Mathf.Max(forcedFlatChunksRemaining, additionalFlatChunks);
    }

    ChunkType PickNextType()
    {
        if (forcedFlatChunksRemaining > 0)
        {
            forcedFlatChunksRemaining--;
            return ChunkType.Flat;
        }

        // A pit is always followed by a flat landing zone, and only ever
        // opens up out of flat ground so its width (and thus jumpability) is predictable.
        if (lastType == ChunkType.Pit)
        {
            return ChunkType.Flat;
        }

        if (lastType != ChunkType.Flat)
        {
            return ChunkType.Flat;
        }

        // Gently bias slope choice back toward the baseline elevation so the
        // course doesn't drift far above/below the camera over a long run.
        float bias = Mathf.Clamp(-nextStartY / 10f, -0.15f, 0.15f);
        float pitChance = GetPitChance();
        float upChance = Mathf.Clamp(0.3f + bias, 0.05f, 0.55f);
        float downChance = Mathf.Clamp(0.3f - bias, 0.05f, 0.55f);

        float roll = Random.value;
        if (roll < pitChance) return ChunkType.Pit;
        roll -= pitChance;
        if (roll < upChance) return ChunkType.UpSlope;
        roll -= upChance;
        if (roll < downChance) return ChunkType.DownSlope;
        return ChunkType.Flat;
    }

    void AddChunk(ChunkType type, float length)
    {
        float startX = nextStartX;
        float startY = nextStartY;
        float endX = startX + length;
        float endY = startY;

        if (type == ChunkType.UpSlope) endY = startY + slopeHeight;
        else if (type == ChunkType.DownSlope) endY = startY - slopeHeight;

        var chunk = new RuntimeChunk { type = type, startX = startX, endX = endX, startY = startY, endY = endY };

        if (type == ChunkType.Pit && chunks.Count > 0)
        {
            // A pit only ever opens up right after a Flat chunk (see
            // PickNextType), and that chunk's right edge was drawn assuming
            // it might keep running into more solid ground (no cap, so its
            // tile could meet the next chunk seamlessly) - since nothing
            // continues it now, retrofit a proper right end-cap onto that
            // now-exposed edge instead of leaving a bare vertical cut.
            // This is the one case that can't be decided proactively (see
            // GroundFactory's class comment - whether a Pit is coming isn't
            // known until it's actually rolled), so it's patched reactively
            // here instead.
            AddRightCapToPreviousChunk();
        }

        if (type != ChunkType.Pit)
        {
            // Only draw a left end-cap where the ground is actually
            // exposed (right after a pit, or the very first chunk of the
            // course) - otherwise this chunk continues directly from the
            // previous one, so a cap here would draw a rounded edge (and
            // visual gap) over ground that's solid underfoot. The right
            // side never gets a proactive cap (see GroundFactory's class
            // comment); if a pit turns out to follow, AddRightCapToPreviousChunk
            // above retrofits one once that's known.
            bool needsLeftCap = chunks.Count == 0 || lastType == ChunkType.Pit;
            chunk.needsLeftCap = needsLeftCap;

            // A continuing joint (no cap) between two chunks whose ground
            // angle differs - i.e. a flat<->slope transition, the only kind
            // that occurs (see PickNextType, a slope is always bordered by
            // Flat on both sides) - leaves a wedge-shaped gap on the outer
            // side of the bend, since each chunk's own thickness is drawn
            // perpendicular to its own angle rather than world-vertical.
            // Bleeding this chunk's near (left) edge back over that joint
            // by the fixed amount needed to fully cover the wedge (derived
            // once below from the constant slope angle) closes it. 0 for a
            // flat-to-flat joint (angle difference 0) or any capped edge.
            float leftBleed = 0f;
            if (!needsLeftCap && platformArt.IsValid && (type == ChunkType.Flat) != (lastType == ChunkType.Flat))
            {
                float theta = Mathf.Atan2(slopeHeight, slopeLength);
                float outerDepth = platformVisualHeight - platformSurfaceInset;
                leftBleed = outerDepth * Mathf.Tan(theta);
            }

            chunk.visual = GroundFactory.CreateSlopeVisual(transform, squareSprite, groundSprite, platformArt,
                new Vector2(startX, startY), new Vector2(endX, endY), groundThickness, platformVisualHeight, platformSurfaceInset, groundColor,
                needsLeftCap, needsRightCap: false, leftBleed: leftBleed);

            // Game Feel pass, section 17 - visual-only clutter along this
            // chunk's top surface, purely to break up "the ground repeats
            // itself" (see DecorationScatter's own comment). Pits obviously
            // have no surface to decorate; everything else (Flat/Slope)
            // qualifies, independent of the enemy-spawn check below.
            if (type != ChunkType.Pit)
            {
                DecorationScatter.ScatterAlongChunk(chunk.visual.transform, decorationSprites, new Vector2(startX, startY), new Vector2(endX, endY));
            }

            bool bossActive = BossManager.Instance != null && BossManager.Instance.IsBossPhase;
            // Run Continuation/Checkpoint Ver.1, item 14 - no new Formation
            // spawns for a short distance right after a CONTINUE (see
            // GameManager.IsInSafeZone) - each chunk generated during that
            // window just stays plain/empty ground instead, so there's no
            // spawn backlog to "catch up on" once the zone ends.
            bool inSafeZone = GameManager.Instance != null && GameManager.Instance.IsInSafeZone;
            bool safeForEnemy = !bossActive && !inSafeZone && type == ChunkType.Flat && lastType != ChunkType.Pit && startX >= noEnemyBeforeDistance;
            if (safeForEnemy)
            {
                bool spacingOk = startX - lastEnemyX > minEnemySpacing;
                // Distance Level Design Ver.1.1, item 2 - DistanceTierManager
                // returns a pre-defined Formation's SpawnPoints as WORLD-UNIT
                // offsets from a single anchor (this chunk's own LEFT edge,
                // startX - never the previous chunk, which isn't guaranteed
                // flat). Every offset is authored >=0 (grows rightward only -
                // see SceneBuilder.BuildFormations), so reserving enough
                // ADDITIONAL flat chunks ahead (RequestFlatRun) to cover the
                // widest member is all that's needed to guarantee the whole
                // shape lands on real, flat, already-decided ground - no
                // member can ever land in a Gap or on terrain not yet
                // generated.
                if (DistanceTierManager.Instance != null
                    && DistanceTierManager.Instance.TryStartFormation(spacingOk, GetEnemyChance(), out List<EnemySpawnRequest> requests, out float maxXOffsetNeeded))
                {
                    float rightMostNeededX = startX + maxXOffsetNeeded;
                    int additionalChunks = Mathf.Max(0, Mathf.CeilToInt((rightMostNeededX - endX) / Mathf.Max(1f, flatLength)));
                    if (additionalChunks > 0) RequestFlatRun(additionalChunks);

                    SpawnFormation(chunk, requests, startX, startY);
                    lastEnemyX = startX;
                }
            }
        }

        chunks.Add(chunk);
        nextStartX = endX;
        nextStartY = endY;
        lastType = type;
    }

    // Distance Level Design Ver.1.1 - places every member of one Formation
    // instance at anchorX+xOffset (world units, never chunk-relative any
    // more), replacing Ver.1's per-chunk single spawn. anchorY is the flat
    // ground height at the anchor - safe to reuse for every member's ground
    // level since the whole reserved span is guaranteed Flat (see
    // RequestFlatRun), so Flat chunks keep startY==endY throughout it.
    void SpawnFormation(RuntimeChunk chunk, List<EnemySpawnRequest> requests, float anchorX, float anchorY)
    {
        Camera cam = Camera.main;
        float? screenTopY = cam != null ? cam.transform.position.y + cam.orthographicSize - (DistanceTierManager.Instance != null ? DistanceTierManager.Instance.screenTopMargin : 0.6f) : (float?)null;

        foreach (EnemySpawnRequest req in requests)
        {
            float ex = anchorX + req.xOffset;
            float ey = anchorY + req.yOffset;

            // Spawn Validation (item 2) - "画面上下端の外に出ない": a member
            // whose absolute Y would exceed the camera's own top edge is
            // skipped outright rather than spawned off-screen.
            if (screenTopY.HasValue && ey > screenTopY.Value) continue;

            SpawnFormationMember(chunk, req, ex, ey);
        }
    }

    void SpawnFormationMember(RuntimeChunk chunk, EnemySpawnRequest req, float ex, float ey)
    {
        EnemyDefinition enemyDef = EnemyDatabase.PickRandomUnlockedOfCategory(enemyPool, req.category);
        // Category had nothing available (species not added to enemyPool
        // yet, or its art hasn't been dropped in) - the ORIGINAL
        // category-agnostic pick is still a reasonable fallback so a
        // Formation slot never spawns literally nothing.
        if (enemyDef == null) enemyDef = EnemyDatabase.PickRandomUnlocked(enemyPool);

        Sprite eSprite = enemyDef != null ? enemyDef.sprite : (enemySprite != null ? enemySprite : squareSprite);
        Color eColor = enemyDef != null ? enemyDef.tint : (enemySprite != null ? Color.white : enemyColor);

        bool airborne = req.yOffset > 0.01f || (enemyDef != null && enemyDef.movementType == EnemyMovementType.Flying);
        EnemyMovementType movementType = airborne ? EnemyMovementType.Flying : EnemyMovementType.Ground;
        float heightOffset = airborne ? 0f : groundEnemyHeight; // yOffset (already folded into ey by the caller) IS the airborne height for a Formation member - groundEnemyHeight only applies to true ground placements

        int maxHp = DistanceTierManager.Instance != null && enemyDef != null ? DistanceTierManager.Instance.EnemyHpFor(enemyDef.hpMultiplier) : 1;
        EnemyBehaviorKind behaviorKind = enemyDef != null ? enemyDef.behaviorKind : EnemyBehaviorKind.None;
        bool bigKnockback = enemyDef != null && enemyDef.bigKnockbackOnHit;
        bool enableVisualFacing = enemyDef != null && enemyDef.enableVisualFacing;
        bool defaultFacingRight = enemyDef == null || enemyDef.defaultFacingRight;
        Sprite[] runFrames = enemyDef != null ? enemyDef.runFrames : null;

        int mileReward = enemyDef != null ? enemyDef.mileReward : 1;
        float visualScaleMultiplier = enemyDef != null ? enemyDef.visualScaleMultiplier : 1f;
        GameObject enemyGO = GroundFactory.CreateEnemy(transform, eSprite, new Vector2(ex, ey + heightOffset), eColor, enemyHitSparkSprite, enemyDeathCloudSprite, movementType, enemyGroundShadowSprite, maxHp, behaviorKind, bigKnockback, shooterProjectileSprite, enableVisualFacing, defaultFacingRight, runFrames, mileReward, visualScaleMultiplier);
        enemyGO.GetComponent<EnemyController>().movementType = movementType;
        if (GameManager.Instance != null && GameManager.Instance.DebugMode)
        {
            Debug.Log($"[Enemy] Spawn {(enemyDef != null ? enemyDef.displayName : "Normal")} HP={maxHp}");
        }

        // A Formation member placed at ground level (yOffset<=0) sits
        // static save for whatever EnemySpecialBehavior gives it, so it
        // still gets the one-time flat-ground tilt (0 degrees - the whole
        // reserved span is guaranteed Flat, never a slope). Airborne
        // members stay level.
        if (movementType == EnemyMovementType.Ground)
        {
            enemyGO.transform.rotation = Quaternion.identity;
        }

        chunk.enemies.Add(enemyGO);
    }

    // Rebuilds the most recently added chunk's visual with a right cap it
    // didn't originally get (see the Pit branch in AddChunk above). Only
    // the visual is touched - startX/endX/startY/endY (and therefore
    // GetHeightAt/GetSlopeAngleAt/collision-free grounding) are completely
    // unchanged, and there's no enemy/collider dependency on this GameObject
    // to preserve, so a plain destroy-and-recreate is safe here.
    void AddRightCapToPreviousChunk()
    {
        RuntimeChunk prev = chunks[chunks.Count - 1];
        if (prev.type == ChunkType.Pit || prev.visual == null || !platformArt.IsValid) return;

        Destroy(prev.visual);
        prev.visual = GroundFactory.CreateSlopeVisual(transform, squareSprite, groundSprite, platformArt,
            new Vector2(prev.startX, prev.startY), new Vector2(prev.endX, prev.endY), groundThickness, platformVisualHeight, platformSurfaceInset, groundColor,
            prev.needsLeftCap, needsRightCap: true);
    }

    // How far the chunk currently being placed sits past the difficulty
    // start line, in units of 1000m. Used to ramp pit/enemy odds gently.
    float GetDifficultyProgress()
    {
        return Mathf.Max(0f, nextStartX - difficultyStartDistance) / 1000f;
    }

    float GetPitChance()
    {
        return Mathf.Min(pitChanceBase + GetDifficultyProgress() * pitChanceRampPer1000m, pitChanceMax);
    }

    float GetEnemyChance()
    {
        return Mathf.Min(enemySpawnChance + GetDifficultyProgress() * enemyChanceRampPer1000m, enemyChanceMax);
    }
}
