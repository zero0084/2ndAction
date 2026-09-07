using UnityEngine;

// One "floating platform" art set: a rounded left end-cap, a horizontally
// tiling middle strip, and a rounded right end-cap. Caps are only drawn on
// edges that are actually exposed (next to a pit, or the very start of the
// course) - see TerrainManager's needsLeftCap/needsRightCap - so two
// chunks that are meant to be walked across continuously (e.g. a slope
// leading into the flat ground after it) render as one seamless tiled
// strip instead of every 6m chunk boundary showing a cap-then-cap "gap"
// even where the ground is solid.
[System.Serializable]
public struct PlatformSpriteSet
{
    public Sprite left;
    public Sprite mid;
    public Sprite right;

    public bool IsValid => left != null && mid != null && right != null;
}

public static class GroundFactory
{
    // platformArt (if valid) draws a left-cap + tiled-middle + right-cap
    // compound visual, replacing the old single-sprite-tiled-across-the-
    // whole-segment look. Falls back to the legacy single groundSprite
    // (still tiled across the full length) if platformArt isn't set, and
    // to a plain colored square if neither is set.
    public static GameObject CreateSlopeVisual(Transform parent, Sprite fallbackSprite, Sprite groundSprite, PlatformSpriteSet platformArt, Vector2 a, Vector2 b, float thickness, float visualHeight, float surfaceInset, Color color, bool needsLeftCap = true, bool needsRightCap = true, float leftBleed = 0f, float rightBleed = 0f)
    {
        GameObject go = new GameObject("GroundSegment");
        go.transform.SetParent(parent);
        go.tag = "Ground";

        Vector2 delta = b - a;
        float length = Mathf.Max(delta.magnitude, 0.01f);
        Vector2 dir = delta / length;
        Vector2 down = new Vector2(dir.y, -dir.x);
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

        Vector2 mid = (a + b) * 0.5f;
        // The new platform art is much taller than the old 1-unit-thick
        // slab (it shows a full rock/cloud-wisp underside), so its visual
        // center sits further below the surface line than before - this
        // only affects what's drawn, never GetHeightAt/collision, which
        // are driven purely by chunk.startY/endY.
        //
        // surfaceInset accounts for the art's own grass tufts poking up
        // above the actual flat walkable plane within the texture (they
        // don't start until ~25% of the way down the canvas) - without
        // subtracting it here, the whole visual would sit anchored by its
        // (empty) canvas top edge instead of by where the ground actually
        // is drawn, leaving a visible gap between characters and the
        // platform they're standing on.
        bool usingNewArt = platformArt.IsValid;
        float usedHeight = usingNewArt ? visualHeight : thickness;
        float usedInset = usingNewArt ? surfaceInset : 0f;
        Vector2 center = mid + down * (usedHeight * 0.5f - usedInset);

        go.transform.position = new Vector3(center.x, center.y, 0f);
        go.transform.rotation = Quaternion.Euler(0f, 0f, angle);

        if (usingNewArt)
        {
            BuildThreePieceVisual(go.transform, platformArt, length, visualHeight, needsLeftCap, needsRightCap, leftBleed, rightBleed);
        }
        else if (groundSprite != null)
        {
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = groundSprite;
            sr.color = Color.white;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = new Vector2(length, thickness);
        }
        else
        {
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = fallbackSprite;
            sr.color = color;
            go.transform.localScale = new Vector3(length, thickness, 1f);
        }

        return go;
    }

    // Lays out left-cap/middle/right-cap side by side spanning exactly
    // `length`. Either cap is entirely omitted (0 width) when that side
    // isn't exposed, so the middle tile just runs edge-to-edge and meets
    // the neighboring chunk's own middle tile with no gap. If both
    // requested caps together would be wider than the segment, they're
    // shrunk proportionally instead of overlapping, so even a short
    // segment still looks reasonable.
    //
    // leftBleed/rightBleed (only meaningful on a side with no cap) push the
    // mid tile's edge out past the segment's true geometric boundary on
    // that side, into the neighboring segment's space. Two straight slabs
    // rotated to different angles (e.g. flat meeting a slope) only touch at
    // a single point along their shared top edge - everywhere else they
    // form a wedge-shaped gap on the outside of the bend, since each slab's
    // own thickness is measured perpendicular to ITS OWN angle, not the
    // world vertical. Bleeding the tile back past the boundary on one side
    // covers that wedge; see TerrainManager.AddChunk for how the bleed
    // amount is derived from the fixed slope angle. Harmless where there's
    // no angle difference (flat-to-flat), since bleed is 0 there and the
    // tile pattern repeats seamlessly regardless of length.
    static void BuildThreePieceVisual(Transform parent, PlatformSpriteSet art, float length, float visualHeight, bool needsLeftCap, bool needsRightCap, float leftBleed = 0f, float rightBleed = 0f)
    {
        float leftCapWidth = needsLeftCap ? visualHeight * (art.left.rect.width / art.left.rect.height) : 0f;
        float rightCapWidth = needsRightCap ? visualHeight * (art.right.rect.width / art.right.rect.height) : 0f;

        float totalCapWidth = leftCapWidth + rightCapWidth;
        float maxCapWidth = length * 0.9f;
        if (totalCapWidth > maxCapWidth && totalCapWidth > 0f)
        {
            float scale = maxCapWidth / totalCapWidth;
            leftCapWidth *= scale;
            rightCapWidth *= scale;
        }

        float leftX = -length / 2f + leftCapWidth / 2f;
        float rightX = length / 2f - rightCapWidth / 2f;

        float midLeftEdge = -length / 2f + leftCapWidth - (needsLeftCap ? 0f : leftBleed);
        float midRightEdge = length / 2f - rightCapWidth + (needsRightCap ? 0f : rightBleed);
        float midWidth = Mathf.Max(0.01f, midRightEdge - midLeftEdge);
        float midX = (midLeftEdge + midRightEdge) / 2f;

        if (needsLeftCap) CreatePieceChild(parent, "Left", art.left, leftCapWidth, visualHeight, leftX, tiled: false);
        CreatePieceChild(parent, "Mid", art.mid, midWidth, visualHeight, midX, tiled: true);
        if (needsRightCap) CreatePieceChild(parent, "Right", art.right, rightCapWidth, visualHeight, rightX, tiled: false);
    }

    static void CreatePieceChild(Transform parent, string name, Sprite sprite, float width, float height, float localX, bool tiled)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(localX, 0f, 0f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = Color.white;

        if (tiled)
        {
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = new Vector2(width, height);
        }
        else
        {
            // End caps aren't tiled - just scaled so their fixed art hits
            // exactly width x height regardless of the sprite's own pixel
            // dimensions/PPU.
            float nativeWidth = sprite.rect.width / sprite.pixelsPerUnit;
            float nativeHeight = sprite.rect.height / sprite.pixelsPerUnit;
            go.transform.localScale = new Vector3(width / nativeWidth, height / nativeHeight, 1f);
        }
    }

    // Root/Visual split (see the class-level convention this and
    // CreatePlayer both follow): Root is the gameplay-authoritative
    // transform - position (foot/ground line), rotation (slope tilt, set
    // by the caller right after this returns), and the BoxCollider2D all
    // live here, untouched by anything purely cosmetic. Visual is the one
    // and only child holding the SpriteRenderer/outline - EnemyAnimator's
    // idle squash/sway apply to Visual, never to Root, so idle "breathing"
    // can never subtly resize/rotate the hit detection collider.
    // Distance Level Design Ver.1 - maxHp/behaviorKind/bigKnockbackOnHit/
    // projectileSprite all default to the ORIGINAL single-hit, no-special-
    // Behavior goblin (maxHp 1, None, false, null), so any existing caller
    // that doesn't pass them keeps spawning the exact same enemy as before.
    // Distance Level Design Ver.1.1 - enableVisualFacing/defaultFacingRight
    // default to false/true (the "don't add EnemyFacing at all" state), so
    // any existing caller that doesn't pass them keeps spawning exactly the
    // same enemy as before - only TerrainManager's Formation spawn path
    // passes real values, sourced from EnemyDefinition.
    public static GameObject CreateEnemy(Transform parent, Sprite sprite, Vector2 position, Color color, Sprite hitSparkSprite = null, Sprite deathCloudSprite = null, EnemyMovementType movementType = EnemyMovementType.Ground, Sprite groundShadowSprite = null, int maxHp = 1, EnemyBehaviorKind behaviorKind = EnemyBehaviorKind.None, bool bigKnockbackOnHit = false, Sprite projectileSprite = null, bool enableVisualFacing = false, bool defaultFacingRight = true, Sprite[] runFrames = null, int mileReward = 1, float visualScaleMultiplier = 1f)
    {
        GameObject go = new GameObject("Enemy");
        go.transform.SetParent(parent);
        go.transform.position = new Vector3(position.x, position.y, 0f);
        go.tag = "Enemy";

        GameObject visualGO = new GameObject("Visual");
        visualGO.transform.SetParent(go.transform, false);
        // Enemy Visual Size Unification pass - set BEFORE any other
        // component's Start() runs this frame, so EnemyAnimator's own
        // captured "baseVisualScale" (used for idle squash/sway) and
        // EnemyFacing's sign-flip (which preserves whatever magnitude is
        // already here, see its own comment) both already account for
        // this correctly. Collider sizing just below is intentionally
        // UNAFFECTED - it still reads raw sprite.bounds, per the brief's
        // "見た目だけを揃える修正" (Visual-only).
        visualGO.transform.localScale = Vector3.one * Mathf.Max(0.01f, visualScaleMultiplier);

        var sr = visualGO.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = RenderOrder.Enemy;
        visualGO.AddComponent<SpriteOutline>();

        // Vertical Mode Prototype (2026-09-08) - see Billboard's own comment.
        // No-ops entirely while PortraitCameraRig isn't active (Landscape
        // build, or the scene was built before this prototype existed) -
        // PortraitCameraRig.Instance is only ever non-null once SceneBuilder
        // has actually created that camera, which happens well before any
        // enemy is ever spawned at runtime.
        if (PortraitCameraRig.Instance != null)
        {
            var enemyBillboard = visualGO.AddComponent<Billboard>();
            enemyBillboard.targetCamera = PortraitCameraRig.Instance.cam;
        }

        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        // Sprite.bounds already accounts for the sprite's own custom foot
        // pivot, in world units - this reproduces exactly what Unity's
        // "auto-fit a new BoxCollider2D to the SpriteRenderer already on
        // this GameObject" behavior used to do here, which no longer
        // triggers automatically now that the SpriteRenderer lives one
        // level down on Visual instead of directly on this Root.
        if (sprite != null)
        {
            col.size = sprite.bounds.size;
            col.offset = sprite.bounds.center;
        }
        var colDebug = go.AddComponent<ColliderDebugView>();
        colDebug.color = Color.red;

        var enemyController = go.AddComponent<EnemyController>();
        enemyController.hitSparkSprite = hitSparkSprite;
        enemyController.deathCloudSprite = deathCloudSprite;
        enemyController.maxHp = Mathf.Max(1, maxHp);
        enemyController.mileReward = Mathf.Max(0, mileReward);
        if (bigKnockbackOnHit)
        {
            // Heavy Enemy - "画面端まで吹っ飛ばすような感じ" via the same
            // KnockbackRoutine every enemy already has, just much larger -
            // see EnemyController.NonLethalHit.
            enemyController.hitKnockbackDistance = 8f;
            enemyController.hitKnockbackDuration = 0.35f;
        }
        var animator = go.AddComponent<EnemyAnimator>();
        animator.visual = visualGO.transform;
        animator.runFrames = runFrames;

        // Distance Level Design Ver.1 - Irregular/Shooter/Chaser/Rusher
        // only (behaviorKind None, the original goblin, never gets this
        // component at all - see EnemySpecialBehavior's own class comment).
        if (behaviorKind != EnemyBehaviorKind.None)
        {
            var special = go.AddComponent<EnemySpecialBehavior>();
            special.kind = behaviorKind;
            special.projectileSprite = projectileSprite;
        }

        // Distance Level Design Ver.1.1 - facing fix, opt-in per species
        // (see EnemyDefinition.enableVisualFacing's own comment for why the
        // original goblin never gets this).
        if (enableVisualFacing)
        {
            var facing = go.AddComponent<EnemyFacing>();
            facing.visual = visualGO.transform;
            facing.defaultFacingRight = defaultFacingRight;
        }

        // Game Feel refinement pass, section 4 - "Playerおよび地上Enemyの
        // 足元に配置" - Ground enemies sit static on one chunk for their
        // whole lifetime (unlike the player, which needs to track height/
        // slope every frame - see PlayerDustEffects.UpdateContactShadow),
        // so a plain child sprite positioned once at the foot line is
        // enough; no per-frame terrain tracking needed. Flying enemies
        // don't get one - there's no ground directly under them to cast on.
        if (movementType == EnemyMovementType.Ground && groundShadowSprite != null)
        {
            GameObject shadowGO = new GameObject("GroundShadow");
            shadowGO.transform.SetParent(go.transform, false);
            var shadowSr = shadowGO.AddComponent<SpriteRenderer>();
            shadowSr.sprite = groundShadowSprite;
            // Visibility Pass - alpha bumped to match PlayerDustEffects'
            // contact shadow (was 0.32); size deliberately untouched, same
            // reasoning as the player's shadow.
            shadowSr.color = new Color(0f, 0f, 0f, 0.42f);
            shadowSr.sortingOrder = RenderOrder.EnvironmentFx;
            // Scaled relative to the enemy's own sprite width so it reads
            // as "this enemy's shadow", not a fixed size unrelated to how
            // big the enemy actually is.
            float shadowWidth = sprite != null ? sprite.bounds.size.x * 0.8f : 0.6f;
            float shadowScale = groundShadowSprite.bounds.size.x > 0.001f ? shadowWidth / groundShadowSprite.bounds.size.x : 1f;
            shadowGO.transform.localScale = Vector3.one * shadowScale;
            shadowGO.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        }

        return go;
    }
}
