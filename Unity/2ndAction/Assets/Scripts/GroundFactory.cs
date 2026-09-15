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
    public static GameObject CreateSlopeVisual(Transform parent, Sprite fallbackSprite, Sprite groundSprite, PlatformSpriteSet platformArt, Vector2 a, Vector2 b, float thickness, float visualHeight, float surfaceInset, Color color, bool needsLeftCap = true, bool needsRightCap = true, float leftBleed = 0f, float rightBleed = 0f, bool addWallCollider = false)
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
            // 崖面Collider追加(2026-09-15) - addWallColliderがtrueの場合のみ、
            // 露出しているキャップ(needsLeftCap/needsRightCap)の位置に
            // 「歩行面ラインより下だけ」を覆う当たり判定を追加する。既定
            // falseなので、この引数を渡さない全ての既存呼び出し(天空回廊
            // のGenerateNextSkyChunk等)は完全に無改造のまま。
            BuildThreePieceVisual(go.transform, platformArt, length, visualHeight, usedInset, needsLeftCap, needsRightCap, leftBleed, rightBleed, addWallCollider);
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
    static void BuildThreePieceVisual(Transform parent, PlatformSpriteSet art, float length, float visualHeight, float surfaceInset, bool needsLeftCap, bool needsRightCap, float leftBleed = 0f, float rightBleed = 0f, bool addWallCollider = false)
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

        if (needsLeftCap)
        {
            CreatePieceChild(parent, "Left", art.left, leftCapWidth, visualHeight, leftX, tiled: false);
            if (addWallCollider) CreateWallHazard(parent, leftCapWidth, visualHeight, surfaceInset, leftX);
        }
        CreatePieceChild(parent, "Mid", art.mid, midWidth, visualHeight, midX, tiled: true);
        if (needsRightCap)
        {
            CreatePieceChild(parent, "Right", art.right, rightCapWidth, visualHeight, rightX, tiled: false);
            if (addWallCollider) CreateWallHazard(parent, rightCapWidth, visualHeight, surfaceInset, rightX);
        }
    }

    // 崖面Collider追加(2026-09-15) - マスター報告「見た目上は壁なのに物理的
    // には通過できる箇所がある」への対応。露出したキャップ(Pitの縁/上下
    // ルート分岐の坂の起点・合流点)にだけ、歩行面ライン(surfaceInset)より
    // 下の「崖の側面・地面の断面」に相当する範囲だけを覆うTriggerを追加
    // する。キャップ本体(CreatePieceChild)とは別の子オブジェクトにする
    // ことで、見た目側のスケール変換(width/nativeWidth比)を気にせず
    // GroundSegment自身のローカル座標(スケール1)でそのまま計算できる。
    // 歩行面ラインちょうどではなく少しだけ下(buffer分)から始めているのは、
    // 普通に接地して歩いているだけの状態を「壁に当たった」と誤反応しない
    // ようにするための緩衝。当たり判定の大きさで表現する既存のObstacle
    // Controllerと同じ思想(GroundFactory.CreateObstacle参照) - 新しい
    // 壁専用の物理ブロックは追加しない。実際の反応(ダメージ/復帰)は
    // TerrainWallHazard.cs、PlayerController.TakeDamage()を経由する既存
    // ロジックをそのまま再利用する。
    static void CreateWallHazard(Transform parent, float capWidth, float visualHeight, float surfaceInset, float localX)
    {
        const float buffer = 0.15f;
        float topLocalY = (visualHeight * 0.5f - surfaceInset) - buffer;
        float bottomLocalY = -visualHeight * 0.5f;
        float colliderHeight = Mathf.Max(0.05f, topLocalY - bottomLocalY);
        float centerLocalY = (topLocalY + bottomLocalY) * 0.5f;

        GameObject go = new GameObject("WallHazard");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(localX, centerLocalY, 0f);

        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        // 見た目の幅より少し内側(90%)に収め、「Colliderが壁から大きく
        // はみ出さない」というマスター指示を満たす。
        col.size = new Vector2(capWidth * 0.9f, colliderHeight);

        go.AddComponent<TerrainWallHazard>();
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

    // 荒野街道 地面埋め修整(2026-09-13深夜) - マスター報告「下ルートの下側
    // に見えている空白部分を、地面で埋める」への対応。CameraFollowの
    // orthographicSize計算(targetHorizontalHalfWidth/aspect)から、通常の
    // 画面比率でも地面ラインの下に約9世界単位、ワイドな比率ではさらに
    // 大きく見えてしまう一方、既存のplatformVisualHeight(3.5)はそのごく
    // 一部しか覆わない - これが「浮遊足場感」の実測できる原因だった。
    // このメソッドは、CreateSlopeVisualが描く表面スラブの底辺から真下へ
    // fillDepth分だけ、同じ角度で新しい岩/土断面テクスチャ(縦タイリング
    // 前提でChatGPT生成済み)を敷き詰める - Pit区間はTerrainManager側で
    // そもそも呼び出されないため、「穴の部分だけ道が途切れて見える」と
    // いう要件は自然に満たされる(このメソッド自体は常に不透明な帯を
    // 描くので、呼び出し側の分岐だけが穴の可視性を担保する)。
    // overlapは表面スラブの底辺とこの帯の上端の間にヘアラインの隙間が
    // 出ないための保険的な食い込み量(CreateSlopeVisualのleftBleedと同じ
    // 発想) - スラブ自体が完全な矩形でキャップも無いため、通常は0に近い
    // 値で十分。
    // 基礎品質修整 続報(2026-09-14) - マスター報告「道の曲がりで穴が気に
    // なる」の実機動画確認で発見: flat<->slope継ぎ目で、このFillだけに
    // 楔形の隙間が見えていた(スラブ自身は無事)。原因はCreateSlopeVisual
    // のleftBleedと全く同じ「隣接する2つの回転した矩形が、角度の違う
    // 継ぎ目で完全には重ならない」現象だが、Fillはスラブよりずっと深い
    // (groundFillDepth)ため、同じ角度でも楔の幅が比例して大きくなり、
    // スラブ用に計算したleftBleedでは足りていなかった。leftBleedは
    // このメソッド自身の深さ基準で呼び出し側が計算し直して渡す想定
    // (TerrainManager.AddChunk参照)。実装はCreateSlopeVisualのキャップ
    // 付き構成と違い単一の矩形なので、GameObject自体をdir方向へ
    // -leftBleed/2だけずらしつつ幅をleftBleedぶん広げることで、右端は
    // 元の位置のまま左端だけ隣接チャンク側へ食い込ませている。
    // 路面と地中断面の接続見た目修正(2026-09-15) - マスター報告「路面の
    // 下に空色の帯が見える、道路パーツが浮いて別の断面ブロックが下に
    // 置かれているように見える」の根本原因を特定・修正。以前はこの帯の
    // 開始位置を「表面スラブの見た目上のキャンバス矩形の底辺」
    // (surfaceVisualHeight-surfaceInset)から逆算していたが、実際の
    // platform_wasteland_mid.png自身の岩肌イラストは、そのキャンバス
    // 矩形の底辺よりずっと手前(実測約78%の高さ)で終わっており、そこから
    // 下・矩形の本当の底辺までは透明マージンだった(素材が岩の下面の
    // ギザギザした輪郭を表現するため、キャンバスいっぱいには描かれて
    // いない)。従来の計算はこの透明マージン分(約0.76ワールド単位)だけ
    // Fillの開始位置を実際の岩肌より深く配置してしまっており、「岩肌の
    // 見えている部分の終わり」から「Fillの開始位置」までの間が両方とも
    // 透明で、背景の空色がそのまま見えてしまっていた。呼び出し側
    // (TerrainManager)で、このキャンバス矩形底辺ではなく実測した「岩肌が
    // 実際に見えている部分の底辺」を基準にしたオフセット
    // (slabContentBottomOffset)を渡すよう変更し、この透明ギャップそのもの
    // を無くした。
    public static GameObject CreateGroundFillVisual(Transform parent, Sprite fillSprite, Vector2 a, Vector2 b, float slabContentBottomOffset, float fillDepth, float overlap, int sortingOrder, float leftBleed = 0f)
    {
        GameObject go = new GameObject("GroundFill");
        go.transform.SetParent(parent);
        go.tag = "Ground";

        Vector2 delta = b - a;
        float length = Mathf.Max(delta.magnitude, 0.01f);
        Vector2 dir = delta / length;
        Vector2 down = new Vector2(dir.y, -dir.x);
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

        Vector2 mid = (a + b) * 0.5f;
        float centerOffset = slabContentBottomOffset - overlap + fillDepth * 0.5f;
        Vector2 center = mid + down * centerOffset - dir * (leftBleed * 0.5f);

        go.transform.position = new Vector3(center.x, center.y, 0f);
        go.transform.rotation = Quaternion.Euler(0f, 0f, angle);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = fillSprite;
        sr.color = Color.white;
        sr.drawMode = SpriteDrawMode.Tiled;
        sr.size = new Vector2(length + leftBleed, fillDepth);
        sr.sortingOrder = sortingOrder;

        return go;
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

    // Stage01 荒野街道 完成版素材(2026-09-13) - 石/小木/壁/壊せる木/巨大石
    // それぞれ専用に生成・content-awareクロップ・foot pivot設定済みの
    // 実スプライト(objSprite)を、Enemyの見た目サイズ統一(CreateEnemyの
    // visualScaleMultiplier)と同じ考え方で「そのスプライト自身のアスペクト
    // 比を保ったまま、目標の高さ(targetHeight world units)に収まる
    // よう均一スケール」して表示する。objSpriteがnull(専用アート未生成の
    // 状況向けフォールバック - ObstacleSpawnerのデフォルトspecs参照)の
    // 場合のみ、以前どおりsquareSpriteをtargetHeight基準の四角へ
    // 引き伸ばして表示する。`position`は地面と接する足元の座標。
    // 基礎品質修整(2026-09-14) - groundAngle(度)はTerrainManager.
    // GetSlopeAngleAtから渡される、その設置X位置における地面の傾き。
    // 以前は常に0(世界基準で直立)固定だったため、坂の上に置かれた障害物
    // が足元の1点だけで接地し、見た目には浮いている/斜めに食い込んでいる
    // ように見えていた - マスター報告「地面の傾斜に合わせて自然に配置」
    // への対応。既定0fなので、傾き情報を渡さない既存の呼び出し元(現状
    // 無し)があっても従来どおり直立のまま。
    public static GameObject CreateObstacle(Transform parent, Sprite squareSprite, Sprite objSprite, Vector2 position, float targetHeight, Color color, bool breakable, int hp, float groundAngle = 0f)
    {
        GameObject go = new GameObject("Obstacle");
        go.transform.SetParent(parent);
        go.transform.position = new Vector3(position.x, position.y, 0f);
        go.transform.rotation = Quaternion.Euler(0f, 0f, groundAngle);
        go.tag = "Obstacle";

        GameObject visualGO = new GameObject("Visual");
        visualGO.transform.SetParent(go.transform, false);
        var sr = visualGO.AddComponent<SpriteRenderer>();
        sr.sortingOrder = RenderOrder.Enemy;

        if (objSprite != null)
        {
            // objSpriteはSceneBuilderがConfigureAndLoadSpriteWithFootPivotで
            // 読み込み済み(pivot=足元)なので、visualGOをローカル原点に
            // 置いたままsprite自身のpivotで接地させられる - CreateEnemyと
            // 全く同じパターン。
            sr.sprite = objSprite;
            sr.color = Color.white;
            float scale = objSprite.bounds.size.y > 0.001f ? targetHeight / objSprite.bounds.size.y : 1f;
            visualGO.transform.localScale = Vector3.one * scale;
            // 接地ズレ修整(2026-09-15) - 以前ここにあった-0.06fの見た目のみ
            // 下げるその場しのぎの補正は撤去した。「浮いて見える」の真因は
            // 個々のオブジェクトではなく、地面Visual自体の基準ズレ
            // (TerrainManager.TerrainThemeSet.platformSurfaceInsetが荒野
            // 街道のテクスチャに対して測定し直されていなかったこと)と判明
            // したため、そちらを直接修正した(TerrainManager.ApplyStageTheme
            // /SceneBuilder.BuildTerrainThemes参照)。地面基準そのものが
            // 合った以上、Obstacleを個別にさらに沈める理由は無い - マスター
            // 指示「個別オブジェクトの調整は最後に、必要なものだけ」に従い、
            // 地面基準を合わせた上で実機確認してから要否を判断する。

            // 当たり判定基礎品質修整(2026-09-14) - マスター報告「見た目で
            // 判断した範囲と実際に当たる範囲がズレる」への対応。矩形の
            // BoxCollider2Dは丸い石・先細りの木といった非矩形シルエットの
            // 四隅で見た目より大きく張り出してしまう(「避けたつもりなのに
            // 当たる」の主因)。objSpriteはコンテンツを詰めてトリムした上で
            // Mesh Type=Tight(既定)でインポートされているため、Unityが
            // 自動生成する物理シェイプ(GetPhysicsShapeCount/GetPhysicsShape)
            // をそのままPolygonCollider2Dへ渡すだけで、実シルエットに沿った
            // 判定になる。GetPhysicsShapeが取得できない(形状データ無し)
            // 場合のみ、従来のバウンディングボックス矩形へ安全にフォール
            // バックする。
            int shapeCount = objSprite.GetPhysicsShapeCount();
            if (shapeCount > 0)
            {
                var poly = go.AddComponent<PolygonCollider2D>();
                poly.pathCount = shapeCount;
                var pointBuffer = new System.Collections.Generic.List<Vector2>();
                for (int i = 0; i < shapeCount; i++)
                {
                    pointBuffer.Clear();
                    objSprite.GetPhysicsShape(i, pointBuffer);
                    var scaledPoints = new Vector2[pointBuffer.Count];
                    for (int p = 0; p < pointBuffer.Count; p++) scaledPoints[p] = pointBuffer[p] * scale;
                    poly.SetPath(i, scaledPoints);
                }
                poly.isTrigger = true;
            }
            else
            {
                var col = go.AddComponent<BoxCollider2D>();
                col.isTrigger = true;
                col.size = objSprite.bounds.size * scale;
                col.offset = (Vector2)objSprite.bounds.center * scale;
            }
        }
        else
        {
            float width = targetHeight * 0.7f;
            visualGO.transform.localPosition = new Vector3(0f, targetHeight * 0.5f, 0f);
            visualGO.transform.localScale = new Vector3(width, targetHeight, 1f);
            sr.sprite = squareSprite;
            sr.color = color;

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(width, targetHeight);
            col.offset = new Vector2(0f, targetHeight * 0.5f);
        }

        var colDebug = go.AddComponent<ColliderDebugView>();
        colDebug.color = Color.yellow;

        var obstacle = go.AddComponent<ObstacleController>();
        obstacle.breakable = breakable;
        obstacle.hp = Mathf.Max(1, hp);

        return go;
    }
}
