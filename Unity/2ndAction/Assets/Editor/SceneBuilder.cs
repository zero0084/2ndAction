using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class SceneBuilder
{
    const string SpritePath = "Assets/Art/square.png";
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string OrnateFramePath = "Assets/Art/UI/OrnateFrame.png";

    [MenuItem("Tools/2ndAction/Build Prototype Scene")]
    public static void Build()
    {
        ConfigureMobilePlayerSettings();

        EnsureTag("Ground");
        EnsureTag("Enemy");
        EnsureTag("PlayerAttack");
        EnsureTag("Boss");

        Sprite squareSprite = EnsureSquareSprite();

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Camera
        GameObject camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        Camera cam = camGO.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 10.4f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.75f, 0.85f, 0.97f);
        cam.transform.position = new Vector3(4f, 1f, -10f);
        camGO.AddComponent<AudioListener>();
        CameraFollow follow = camGO.AddComponent<CameraFollow>();
        follow.offsetX = 6f;

        // Background (fixed backdrop that always fills the camera view)
        // Distance Level Design Ver.1 - dayBackgroundSr hoisted to method
        // scope (was local to this if-block) so WorldTimeCycle setup further
        // down can wire it in as dayLayer without a second lookup.
        Sprite backgroundSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Background/background.png");
        SpriteRenderer dayBackgroundSr = null;
        if (backgroundSprite != null)
        {
            GameObject bgGO = new GameObject("Background");
            dayBackgroundSr = bgGO.AddComponent<SpriteRenderer>();
            dayBackgroundSr.sprite = backgroundSprite;
            dayBackgroundSr.sortingOrder = -100;
            var bgFollower = bgGO.AddComponent<BackgroundFollower>();
            bgFollower.cam = cam;
        }

        // Card database - built/refreshed first so every CardDefinition
        // asset (icons included) exists under Resources/Cards before
        // anything below reads through CardDatabase.AllCards.
        CardDatabaseBuilder.Build();

        // Card UI / Rarity Frame pass - one-time Editor-side import
        // configuration for the Rarity frame PNGs under Resources/
        // CardFrames/ (see its own comment - CardRarityFrames itself loads
        // the actual Sprites lazily at runtime via Resources.Load, not from
        // anything this method caches).
        LoadCardRarityFrames();

        // Distance-unlock system - the UnlockDefinition assets themselves
        // (EnemyDatabaseBuilder runs later, once the goblin sprite import
        // is configured - see the terrain setup block below).
        UnlockDatabaseBuilder.Build();

        // GameManager
        GameObject gmGO = new GameObject("GameManager");
        GameManager gameManager = gmGO.AddComponent<GameManager>();
        // Home Room UI reconstruction pass - new logo ("ONE MORE MILE / To
        // the Next Me"), real alpha (confirmed via pixel inspection), no
        // extra processing needed.
        gameManager.titleLogo = LoadIconTexture("Assets/Art/UI/TitleLogoV2.png");
        // TOP screen is now "Home Room" (the player's own room, returned to
        // after a Run) instead of the previous floating-continents scene -
        // see GameManager.OnGUI's title-screen block for the room's tap
        // targets (door/bed/book/desk gacha machine). Still a single static
        // painted scene, drawn fully static (no scroll/parallax) exactly
        // like its predecessor.
        gameManager.topBackground = LoadIconTexture("Assets/Art/UI/TopBackgroundHomeRoom.png");
        // Imported ONCE as a Sprite (ForegroundCloudLayer needs that for
        // in-game SpriteRenderer use - see Build() below) - gameManager's
        // own Texture2D field is then just a cheap AssetDatabase lookup of
        // the same already-imported asset, not a second reimport with
        // conflicting settings (same pattern as OrnateFrame's two textures).
        Sprite topCloudSprite = LoadTiledSprite("Assets/Art/UI/TopCloud.png", 300f);
        // Home Room pass - the drifting-cloud layer was designed for the
        // old floating-continents sky backdrop; a fully enclosed room has
        // no sky to drift clouds across, so GameManager.OnGUI's title
        // block no longer calls DrawScrollingCloud. topCloud itself is left
        // wired (still used in-game by ForegroundCloudLayer above, unrelated
        // to the TOP screen) rather than removed.
        gameManager.topCloud = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/UI/TopCloud.png");

        // Game Feel refinement pass, section 14 - the in-game foreground
        // cloud layer (see ForegroundCloudLayer's own comment), now its own
        // dedicated Foreground Cloud.png rather than reusing the TOP
        // screen's cloud art. PPU chosen so the source image's own width
        // maps to ~4.4 world units - ForegroundCloudLayer's own
        // scaleMin/scaleMax (0.35-0.65) then brings the actual on-screen
        // size down from that to something small per "小さく短く控えめに".
        GameObject cloudLayerGO = new GameObject("ForegroundCloudLayer");
        ForegroundCloudLayer cloudLayer = cloudLayerGO.AddComponent<ForegroundCloudLayer>();
        cloudLayer.cam = cam;
        cloudLayer.cloudSprite = LoadTiledSprite("Assets/Art/Effects/ForegroundCloudNew.png", 350f);
        // The large OrnateFrame.png (uGUI's Deck Edit screen, via
        // CreateOrnatePanel) and the small OrnateFrameSmall.png (IMGUI's
        // OrnateUi, TOP screen) are two SEPARATE texture files - see
        // OrnateUi.FrameTexture's comment for why one texture can't serve
        // both button-sized and panel-sized elements.
        LoadOrnateFrameSprite();
        gameManager.ornateFrame = LoadIconTexture("Assets/Art/UI/OrnateFrameSmall.png");

        // OneMoreMile Presentation pass - shared screen transition (see
        // ScreenTransitionManager's own class comment for the design/why).
        // No sprite/scene asset to load here - it draws entirely from
        // procedural 1x1 textures via OnGUI, same as UiBackdrop.
        GameObject transitionGO = new GameObject("ScreenTransitionManager");
        transitionGO.AddComponent<ScreenTransitionManager>();

        // エリアルコンボ改修(2026-09-11), item 7 - 「3 HIT/4 HIT...のような
        // 簡単なコンボ表示」。既存HUDと同じOnGUIの単純なテキスト表示
        // (ComboCounterUI.cs参照)、専用のuGUI Canvasは不要。
        GameObject comboCounterGO = new GameObject("ComboCounterUI");
        comboCounterGO.AddComponent<ComboCounterUI>();

        // Audio (AudioManager generates its own placeholder tones at runtime,
        // since procedural AudioClips can't be saved into the scene file).
        GameObject audioGO = new GameObject("AudioManager");
        AudioManager audioManager = audioGO.AddComponent<AudioManager>();
        ConfigureMusicImport("Assets/Audio/TitleBgm.wav");
        ConfigureMusicImport("Assets/Audio/GameplayBgm.wav");
        audioManager.titleBgm = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/TitleBgm.wav");
        audioManager.gameplayBgm = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/GameplayBgm.wav");

        ConfigureSfxImport("Assets/Audio/SE/JumpSe.wav");
        ConfigureSfxImport("Assets/Audio/SE/DoubleJumpSe.wav");
        ConfigureSfxImport("Assets/Audio/SE/AttackSe1.wav");
        ConfigureSfxImport("Assets/Audio/SE/AttackSe2.wav");
        ConfigureSfxImport("Assets/Audio/SE/AttackSe3.wav");
        ConfigureSfxImport("Assets/Audio/SE/LandSe.wav");
        audioManager.jumpSe = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/JumpSe.wav");
        audioManager.doubleJumpSe = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/DoubleJumpSe.wav");
        audioManager.landSe = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/LandSe.wav");
        audioManager.attackSe1 = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/AttackSe1.wav");
        audioManager.attackSe2 = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/AttackSe2.wav");
        audioManager.attackSe3 = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/AttackSe3.wav");

        // Game Feel pass - OneMoreMile_SE_Subtle_Pack (see AudioManager's
        // own field comments for the volume/role each of these plays).
        ConfigureSfxImport("Assets/Audio/SE/01_attack_hit.wav");
        ConfigureSfxImport("Assets/Audio/SE/02_player_damage.wav");
        ConfigureSfxImport("Assets/Audio/SE/03_enemy_defeat.wav");
        ConfigureSfxImport("Assets/Audio/SE/04_player_death.wav");
        ConfigureSfxImport("Assets/Audio/SE/05_landing.wav");
        audioManager.attackHitSe = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/01_attack_hit.wav");
        audioManager.playerDamageSe = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/02_player_damage.wav");
        audioManager.enemyDefeatSe = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/03_enemy_defeat.wav");
        audioManager.playerDeathSe = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/04_player_death.wav");
        audioManager.landingSe = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/05_landing.wav");

        ConfigureSfxImport("Assets/Audio/SE/CardDeckAppearSe.wav");
        ConfigureSfxImport("Assets/Audio/SE/CardDrawSe.wav");
        ConfigureSfxImport("Assets/Audio/SE/CardFlipSe.wav");
        ConfigureSfxImport("Assets/Audio/SE/CardSelectSe.wav");
        ConfigureSfxImport("Assets/Audio/SE/CardConfirmSe.wav");

        gameManager.rewardCardSequence = BuildRewardCardCanvas();
        gameManager.deckEditUI = BuildDeckEditCanvas();
        gameManager.cardFusionUI = BuildCardFusionCanvas();
        // キャラクター選択画面(2026-09-12) - CharacterDatabaseBuilder.Build
        // をここで呼ぶ(EnemyDatabaseBuilder.Buildと同じ扱い) - 4人目・5人目
        // を追加する際もCharacterDefinitionアセットを1つ足すだけで、この
        // SceneBuilder.Buildを再実行すれば自動的にCharacter Select画面へ
        // 反映される(BuildCharacterSelectCanvas側がCharacterDatabase.
        // AllCharactersの件数ぶん動的にカードスロットを生成するため)。
        CharacterDatabaseBuilder.Build();
        gameManager.characterSelectUI = BuildCharacterSelectCanvas();
        StageDatabaseBuilder.Build();
        gameManager.stageSelectUI = BuildStageSelectCanvas();
        // Home Room UI reconstruction pass, item 4 - the Gacha machine is
        // now a prop drawn directly onto the TOP room (see GameManager.
        // OnGUI's title-screen block), not a Sprite inside a Canvas -
        // plain Texture2D import (LoadIconTexture, same as topBackground/
        // titleLogo) since GUI.DrawTexture takes a Texture2D, not a Sprite.
        gameManager.gachaMachineTexture = LoadIconTexture("Assets/Art/UI/GachaMachine.png");
        // Home画面 / Stage Select改善依頼(2026-09-16), item2 - Character
        // 肖像画を「壁に飾られた額縁」に見せるためのフレーム。ChatGPTで
        // 生成した、中央が完全透明(実アルファ)のPNG。
        gameManager.portraitFrameTexture = LoadIconTexture("Assets/Art/UI/PortraitFrame.png");
        // Ver.1 finishing pass, item 8 - reuses the already-imported Card
        // Select SE (see ConfigureSfxImport("...CardSelectSe.wav") above)
        // for the room hotspots' tap feedback.
        gameManager.roomTapSe = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/CardSelectSe.wav");

        // Player
        GameObject player = CreatePlayer(squareSprite);
        // Y=0 matches groundOffset=0 (see PlayerController) - Move() will
        // recompute this from GetHeightAt on the very first frame anyway,
        // but starting it at the same value avoids a one-frame pop.
        player.transform.position = new Vector3(1f, 0f, 0f);

        follow.target = player.transform;

        // Vertical Mode Prototype (2026-09-08) - the second, Portrait-mode
        // camera (see PortraitCameraRig's own comment for the full "斜め上
        // 視点" reasoning). Tagged MainCamera same as the Landscape camera
        // above - Camera.main resolves to whichever tagged camera is
        // currently ENABLED, so every existing Camera.main call site
        // elsewhere in the project (CameraFollow.Shake, the Escape charge
        // gauge, etc.) automatically follows whichever mode is active with
        // zero changes needed there. Disabled by default (and its
        // AudioListener with it) - the Landscape build's behavior is
        // unchanged until ViewModeToggle actually switches modes.
        GameObject portraitCamGO = new GameObject("Portrait Camera");
        portraitCamGO.tag = "MainCamera";
        Camera portraitCam = portraitCamGO.AddComponent<Camera>();
        portraitCam.orthographic = false;
        portraitCam.fieldOfView = 62f;
        portraitCam.nearClipPlane = 1f;
        portraitCam.farClipPlane = 100f;
        portraitCam.clearFlags = CameraClearFlags.SolidColor;
        portraitCam.backgroundColor = cam.backgroundColor;
        portraitCam.enabled = false;
        var portraitListener = portraitCamGO.AddComponent<AudioListener>();
        portraitListener.enabled = false;
        PortraitCameraRig portraitRig = portraitCamGO.AddComponent<PortraitCameraRig>();
        portraitRig.target = player.transform;
        portraitRig.cam = portraitCam;

        // Billboard (see its own comment) on the Player's Visual child only
        // - Root carries the Collider2D/Rigidbody2D and must never be
        // rotated in 3D. No-ops entirely while portraitCam is disabled, so
        // this has zero effect on the Landscape build.
        Transform playerVisual = player.transform.Find("Visual");
        if (playerVisual != null)
        {
            var playerBillboard = playerVisual.gameObject.AddComponent<Billboard>();
            playerBillboard.targetCamera = portraitCam;
        }

        // One-key (V) Landscape/Portrait switch for Editor comparison - see
        // ViewModeToggle's own comment. Lives on the same GameObject as the
        // Landscape camera purely so it's easy to find in the Hierarchy;
        // has no functional dependency on that placement.
        var viewModeToggle = camGO.AddComponent<ViewModeToggle>();
        viewModeToggle.landscapeCam = cam;
        viewModeToggle.portraitRig = portraitRig;

        // Terrain (infinite chunk-based course generator)
        GameObject terrainGO = new GameObject("TerrainManager");
        TerrainManager terrain = terrainGO.AddComponent<TerrainManager>();
        terrain.squareSprite = squareSprite;
        // Legacy single-tile cloud texture - kept assigned as the fallback
        // groundSprite/skyPathSprite (GroundFactory only falls back to
        // these if platformArt below isn't valid), so clearing
        // terrain.platformArt reverts to this old look instantly.
        Sprite cloudSprite = LoadTiledSprite("Assets/Art/Ground/CloudPlatform.png", 1024f);
        terrain.groundSprite = cloudSprite;
        terrain.skyPathSprite = cloudSprite;
        // Game Feel refinement pass - imported (and available on
        // TerrainManager, unused for now) but not yet wired into the sky
        // path's actual visual - see TerrainManager.AddSkyChunk's own
        // comment on why this needs real Game View verification first.
        LoadTiledSprite("Assets/Art/Ground/CloudPlatformNew.png", 1024f);

        // Visual Style Ver.1 floating-platform art (left-cap/mid-tile/
        // right-cap, all three sharing one PPU so their world-space
        // heights agree with platformVisualHeight and each other's aspect
        // ratios stay correct). Used for both the ground path and the sky
        // path. 768 is the source crop's pixel height (all three pieces
        // share it, since CropPlatform.ps1 cropped them from the same
        // source image without changing height) - update this if the
        // platform art is ever re-cropped at a different resolution.
        const float PlatformSourcePixelHeight = 768f;
        float platformPpu = PlatformSourcePixelHeight / terrain.platformVisualHeight;
        terrain.platformArt = new PlatformSpriteSet
        {
            left = LoadTiledSprite("Assets/Art/VisualStyleV1/Ground/platform_left.png", platformPpu),
            mid = LoadTiledSprite("Assets/Art/VisualStyleV1/Ground/platform_mid.png", platformPpu),
            right = LoadTiledSprite("Assets/Art/VisualStyleV1/Ground/platform_right.png", platformPpu)
        };
        // Measured directly from platform_mid.png: rows 0-155/768 are fully
        // transparent canvas margin, and grass coverage doesn't read as
        // solid ground until row ~190/768 - i.e. the actual walkable
        // surface sits ~190px below the canvas top. 190/768 * platformVisualHeight
        // = the world-unit inset GroundFactory needs to subtract so the
        // drawn grass line - not the empty canvas edge - lines up with the
        // math ground line characters actually stand on.
        terrain.platformSurfaceInset = 190f / PlatformSourcePixelHeight * terrain.platformVisualHeight;

        // Visual Style Ver.1 grunt enemy (goblin) - enemy.png (the old
        // flying-creature sprite) stays untouched on disk for an easy
        // revert; PPU chosen to match its old on-screen size (measured:
        // old sprite's visible silhouette is ~1665px tall at 1329 px/unit
        // -> ~1.25 world units; new sprite's silhouette is ~1308px tall
        // within a 1320px padded canvas, so 1320/1.25 =~ 1053 px/unit
        // reproduces that same in-game size).
        terrain.enemySprite = ConfigureAndLoadSpriteWithFootPivot("Assets/Art/Enemy/enemy_v1.png", 1053f);
        terrain.player = player.transform;
        // ステージ別ビジュアル差し替え(2026-09-13) - 天空回廊(既存の見た目
        // そのまま)はstageThemesに何も追加しない。荒野街道の専用アートが
        // 用意でき次第、ここへTerrainThemeSetを1件追加するだけで済む。
        terrain.backgroundRenderer = dayBackgroundSr;
        terrain.stageThemes = BuildTerrainThemes();
        // Game Feel refinement pass - OneMoreMile_GameFeel pack, individual
        // PNGs (see AGENTS/PR notes) - imported at a consistent ~1-world-
        // unit BASE size each (PPU == the source file's own pixel width),
        // so every per-effect Scale multiplier already living in code
        // (EnemyController.hitParticleScale/deathCloudScale,
        // PlayerDustEffects.dustScale, etc.) is what actually determines
        // the small final on-screen size - not two separate, easy-to-
        // double-count shrink factors fighting each other.
        terrain.enemyHitSparkSprite = LoadTiledSprite("Assets/Art/Effects/HitSpark.png", 1536f);
        terrain.enemyDeathCloudSprite = LoadTiledSprite("Assets/Art/Effects/EnemyDeathSmoke.png", 1536f);
        terrain.enemyGroundShadowSprite = LoadTiledSprite("Assets/Art/Effects/GroundShadow.png", 1672f);
        // エリアルコンボ改修(2026-09-11), item 8 - プレイヤー自身の下攻撃
        // 着地(CreatePlayer内のdownAttackLandSlashVisual)と全く同じ素材/
        // Pivotを共有(「既存素材が使用できる場合はそれを利用」)。
        terrain.enemyGroundImpactSprite = LoadTiledSpriteWithPivot("Assets/Art/Effects/ImpactBurstBlue.png", 545f, new Vector2(0.5f, 0.05f));

        // Game Feel refinement pass, section 13 - bottom-content-pivoted
        // (same approach as every foot-pivoted character sprite - see
        // ConfigureAndLoadSpriteWithFootPivot) so DecorationScatter can
        // just place each one directly on the surface Y with no per-item
        // offset math ("素材下端が地面に接するように" - never floating,
        // never buried). Per-item PPU (not the same ~1-unit base the
        // effects above use) since these are meant to read as different
        // physical sizes relative to each other and the player - Rock/
        // RuinsSign deliberately smaller than their source canvas implies
        // ("Playerと比較して巨大にしすぎない"), DecorationScatter's own
        // scaleMin/scaleMax then adds a further +/-  random variance on
        // top of whichever of these base sizes gets picked each spawn.
        terrain.decorationSprites = new[]
        {
            ConfigureAndLoadSpriteWithFootPivot("Assets/Art/Decoration/DecorGrass.png", 3072f),
            ConfigureAndLoadSpriteWithFootPivot("Assets/Art/Decoration/DecorFlowers.png", 3413f),
            ConfigureAndLoadSpriteWithFootPivot("Assets/Art/Decoration/DecorRock.png", 1920f),
            ConfigureAndLoadSpriteWithFootPivot("Assets/Art/Decoration/DecorRuinsSign.png", 1536f),
        };

        // Distance Level Design Ver.1 - the 5 new enemy species' art.
        // Same ConfigureAndLoadSpriteWithFootPivot every character sprite
        // in this project already goes through (Texture Type=Sprite,
        // Alpha Source=Input Texture Alpha, alphaIsTransparency=ON, foot
        // pivot) - "過去にGameFeel素材で発生した黒/白い矩形背景...再発させ
        // ない" from the brief is what that shared helper already exists to
        // guarantee. PPU chosen per-species (each source canvas is
        // 1536x1024, generously padded - NOT used as the world size
        // directly) so each reads at a sensible size relative to the
        // Player's own 1-unit collider and the existing goblin (~1.25
        // units tall) - Heavy noticeably bigger, Irregular noticeably
        // smaller, matching their brief descriptions ("巨大な重装オーガ" /
        // "小型の獣人"). Actual on-screen sizing is a first pass - expect
        // to retune these PPU values after seeing them in Game View.
        // EnemyDatabaseBuilder below self-loads each of these by path once
        // this import config is applied - the returned Sprite references
        // aren't otherwise needed here.
        // Stage01完成版要求仕様書「鳥」対応(2026-09-13) - 元のFlyingEnemy.png
        // (ドラゴン風で強敵に見えすぎる)は温存しつつ(将来天空回廊が本実装
        // される際に強敵系の飛行敵として再利用できるよう削除しない)、
        // 現状で実際にゲーム中に出現する唯一の飛行種(flying_wyvern、
        // wasteland_road専用)にはこちらの新しい鷹アートを使う。
        ConfigureAndLoadSpriteWithFootPivot("Assets/Art/Enemy/WastelandBird.png", 1117.6f);
        ConfigureAndLoadSpriteWithFootPivot("Assets/Art/Enemy/FlyingEnemy.png", 730f);
        ConfigureAndLoadSpriteWithFootPivot("Assets/Art/Enemy/IrregularEnemy.png", 1140f);
        ConfigureAndLoadSpriteWithFootPivot("Assets/Art/Enemy/ShooterEnemy.png", 850f);
        ConfigureAndLoadSpriteWithFootPivot("Assets/Art/Enemy/HeavyEnemy.png", 570f);
        ConfigureAndLoadSpriteWithFootPivot("Assets/Art/Enemy/RunnerEnemy.png", 1020f);

        // Runner Enemy Run Animation - 5 individually-sliced frames (see
        // the scratchpad slicing step this pass added; Assets/Art/RunnerRun/
        // runner_run_0..4.png), each with its own foot pivot via the SAME
        // per-frame-lowest-pixel technique the Player/Dragon/Majin
        // animations already use - "足元位置を揃えて、走行中にガタつかない
        // ように". EnemyDatabaseBuilder below self-loads these by
        // sorted-filename order once this import config is applied.
        //
        // Bugfix 2026-09-06 - "Enemyサイズがまだ小さい/個体差が出る"
        // (Chaser/Rusher specifically). The PPU here used to be 724 (each
        // frame's own raw canvas HEIGHT), on the assumption "canvas height
        // ≈ character height" - wrong for this specific sprite sheet: each
        // 434x724 frame is a narrow slice of a wide horizontal running
        // strip, so the character's actual alpha content only fills
        // ~41-53% of that canvas (PowerShell/LockBits measured 298-383px
        // of real content per frame, not 724). At PPU 724 the run frames
        // rendered at barely HALF the world size of RunnerEnemy.png's own
        // static portrait (measured separately at PPU 1020) - since
        // Chaser/Rusher show these run frames essentially the whole time
        // they're moving, and the SAME visualScaleMultiplier below has to
        // look right on BOTH the portrait (idle/stagger states) and these
        // run frames (moving), the two needed to share one natural PPU
        // baseline. This pass's own follow-up bug (see below) is exactly
        // the residual (~103-132%) unevenness a single shared PPU couldn't
        // fully remove.
        //
        // 敵Runnerアニメーションのズレ修正(2026-09-15) - マスター報告
        // 「Runnerのアニメーションのズレ」の根本原因が上記コメントにまさに
        // 記録されていた「374という1つの共有PPUでは平均値しか合わせられず、
        // 実測103〜132%の個体差(コマごとの伸び縮み)が残る」という既知の
        // 限界そのものだった。ConfigureSpriteFolderImportWithFootPivotUniform
        // Size(新設、SceneBuilder.cs内の同関数のコメント参照)へ切り替え、
        // 共有PPUではなく「コマ個別のPPU」をそのコマ自身のアルファコンテン
        // ツ高さから逆算する方式にした。targetWorldHeight=RunnerEnemy.png
        // 自身の実測ワールド高さ(954px÷PPU1020=0.9353)を渡すことで、5コマ
        // 全てがポートレートと寸分違わず同じワールド高さで描画されるように
        // なり、伸び縮み(ズレ)が原理的に解消される。
        ConfigureSpriteFolderImportWithFootPivotUniformSize("Assets/Art/RunnerRun", 954f / 1020f);

        // 敵アニメーション追加(2026-09-15) - マスター報告「各敵キャラの
        // アニメーションを追加してほしい」への対応、第1弾(Goblin)。ChatGPT
        // にenemy_v1.pngを参照画像として渡し、同じキャラクター・同じ画風の
        // 左向き走行5コマを生成(黒背景、しきい値透過処理済み)。Runnerの
        // ズレ修正で新設したConfigureSpriteFolderImportWithFootPivotUniform
        // Sizeをそのまま使い、targetWorldHeight=enemy_v1.png自身の実測ワー
        // ルド高さ(1308px÷PPU1053=1.2422)を渡すことで、最初から伸び縮み
        // (Runnerで発見したのと同じ種類のズレ)が起きない状態で導入する。
        ConfigureSpriteFolderImportWithFootPivotUniformSize("Assets/Art/GoblinRun", 1308f / 1053f);

        // 敵アニメーション追加(2026-09-15) - 第2弾(Shooter)。ShooterEnemy.png
        // 自身の実測ワールド高さ(1009px÷PPU850=1.1871)をtargetWorldHeightに
        // 渡す。
        ConfigureSpriteFolderImportWithFootPivotUniformSize("Assets/Art/ShooterRun", 1009f / 850f);

        // 敵アニメーション追加(2026-09-15) - 第3弾(Heavy)。HeavyEnemy.png
        // 自身の実測ワールド高さ(996px÷PPU570=1.7474)をtargetWorldHeightに
        // 渡す。
        ConfigureSpriteFolderImportWithFootPivotUniformSize("Assets/Art/HeavyRun", 996f / 570f);

        // 敵アニメーション追加(2026-09-15) - 第4弾(Irregular)。IrregularEnemy.
        // png自身の実測ワールド高さ(956px÷PPU1140=0.8386)をtargetWorld
        // Heightに渡す。
        ConfigureSpriteFolderImportWithFootPivotUniformSize("Assets/Art/IrregularRun", 956f / 1140f);

        // 敵アニメーション追加(2026-09-15) - 第5弾(Flying/Bird)、最後の1種。
        // 他4種と違い「走行」ではなく「羽ばたき」5コマ(WastelandBird.pngを
        // 参照画像にChatGPTで生成、黒背景・しきい値透過処理済み)。ここだけ
        // 足元Pivot系の関数を使わない - 翼を広げたコマと畳んだコマとでは
        // シルエットの縦幅が本来大きく異なる(実測297-425px、約43%差)ため、
        // Runner/Goblin等と同じ「コマ個別PPUで揃える」処理をすると本来の
        // 翼の広がりごと胴体まで拡大縮小されてしまい逆効果。さらに「最下点
        // 基準Pivot」も、翼を下げたコマでは翼先が最下点になってしまい
        // 胴体が上下にジャンプして見える(実測、コンテンツ中心が308〜505px
        // まで変動)。Flying種はGetHeightAt基準の接地もそもそも無く
        // (EnemyAnimator.Update、isFlyingの独自bobのみ)、必要なのは「胴体の
        // 位置がコマ間で一定であること」だけなので、既存のConfigureSprite
        // FolderImport(Pivot指定なし=Unity既定のCenter Pivot、PPUは全コマ
        // 共有のWastelandBird.png自身の値)をそのまま使う - 生成時に「胸位置
        // をコマ間で揃える」よう指示済みなので、固定Center Pivotで胴体が
        // ブレずに揃う。
        ConfigureSpriteFolderImport("Assets/Art/WastelandBirdFlap", 1117.6f);

        // Distance-unlock system - enemy species database, built now that
        // the goblin sprite's import (foot pivot/PPU) is configured, since
        // EnemyDatabaseBuilder just references that already-set-up Sprite
        // rather than reconfiguring it. terrain.enemyPool below is every
        // species that exists (locked or not) - which of them actually
        // spawns is re-checked per-spawn against UnlockManager, not here.
        // The 5 new species above are imported first for the same reason -
        // EnemyDatabaseBuilder self-loads them by path (see its own class
        // comment) rather than taking them as parameters.
        EnemyDatabaseBuilder.Build(terrain.enemySprite);
        terrain.enemyPool = new List<EnemyDefinition>(EnemyDatabase.AllEnemies);

        // 敵AI行動Tier試験実装(2026-09-16) - T0/T1/T2比較用の3体
        // (goblin_t0/t1/t2)は通常のenemyPoolには含めない別Resourcesフォル
        // ダに作られる(EnemyDatabaseBuilder.BuildTierTestEnemies自身の
        // コメント参照)。TerrainManager.debugTierTestEnemiesへ直接割り当て、
        // GameManager.DebugModeがONの間だけ走行開始直後にT0→T1→T2の順で
        // 強制スポーンされる。
        terrain.debugTierTestEnemies = EnemyDatabaseBuilder.BuildTierTestEnemies(terrain.enemySprite);

        // Distance Level Design Ver.1 - Shooter Enemy's projectile visual;
        // "簡易Sprite/既存VFX流用で構いません" from the brief, so this
        // originally just reused the already-imported Hit Spark art rather
        // than needing a dedicated arrow/bolt asset.
        //
        // 敵の攻撃VFX修正(2026-09-15) - マスター報告「敵の攻撃時の炎などの
        // アニメーションがうまく反映されていない」を調査。HitSpark.png は
        // 静止した被弾の閃光(放射状にギザギザ/羽根状に広がる形状)用の絵で、
        // FireballController の常時回転+脈動スケール(DragonController/
        // MajinController の火球と全く同じ手続き的アニメーション、詳細は
        // FireballController.cs参照)と組み合わせると、飛翔する一塊の弾に
        // 見えるべきところが回転する放射状の閃光になってしまい、明らかに
        // 「炎の塊」としては破綻して見えていた。ボス(ドラゴン/魔人)の火球は
        // 同じFireballControllerでsquareSprite(単色四角、色は暖色に着色)を
        // 使っており、こちらは正しく「回転+脈動+燃えかすの尾」で炎の塊らし
        // く見えている(既に実績のある組み合わせ) - Shooter敵もこれに合わ
        // せ、専用の炎/矢アートが用意できるまでの間はsquareSpriteへ統一する。
        terrain.shooterProjectileSprite = squareSprite;

        // Boss (watches distance, spawns dragon/majin encounters starting
        // at 1000m)
        GameObject bossGO = new GameObject("BossManager");
        BossManager boss = bossGO.AddComponent<BossManager>();
        boss.player = player.transform;
        boss.squareSprite = squareSprite;
        boss.dragonIdleFrames = LoadSpriteSequence("Assets/Art/DragonIdle");
        boss.dragonChargeFrames = LoadSpriteSequence("Assets/Art/DragonCharge");
        boss.dragonFireFrames = LoadSpriteSequence("Assets/Art/DragonFire");

        // Majin frames are freshly extracted GIF frames each with their own
        // native resolution (420x630 idle, 720x720 attack) - normalize both
        // to the same in-world character height so switching from idle to
        // attack doesn't visibly pop in scale.
        const float majinWorldHeight = 3.45f;
        ConfigureSpriteFolderImport("Assets/Art/MajinIdle", 630f / majinWorldHeight);
        ConfigureSpriteFolderImport("Assets/Art/MajinAttack", 720f / majinWorldHeight);
        boss.majinIdleFrames = LoadSpriteSequence("Assets/Art/MajinIdle");
        boss.majinAttackFrames = LoadSpriteSequence("Assets/Art/MajinAttack");

        // Boss Defeat Presentation pass - reuses the same already-imported
        // Hit Spark / Enemy Death Smoke art regular enemies use (cheap
        // AssetDatabase lookup, same pattern as gameManager.topCloud) -
        // per-boss scale fields (finalHitSparkScale/bossDeathSmokeScale)
        // are what actually make these read bigger than a regular enemy's
        // own hit/death, not a separate texture.
        boss.bossHitSparkSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Effects/HitSpark.png");
        boss.bossDeathSmokeSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Effects/EnemyDeathSmoke.png");

        // Distance Level Design Ver.1 - Mechanical Dragon (item 7). Same
        // "PPU chosen for a base world height, dragonScale multiplies on
        // top" convention as the real Dragon/Majin art.
        boss.mechanicalDragonSprite = ConfigureAndLoadSpriteWithFootPivot("Assets/Art/Boss/MechanicalDragon.png", 1152f / 3.2f);

        // Distance Level Design Ver.1 - Death/Grim Reaper (item 8).
        // INTENTIONALLY NOT WIRED - the provided file (Death.jpg) has the
        // transparency checkerboard baked in as opaque pixel content
        // rather than a real alpha channel (it's a .jpg - no alpha
        // channel is even possible), so importing it as-is would reproduce
        // exactly the "黒/白い矩形背景" bug the brief explicitly warns
        // against, just with a checkerboard pattern instead of a solid
        // color. boss.deathSprite stays null (SpawnDeath already no-ops
        // without one - see BossManager) until a real transparent PNG is
        // provided; see the chat response for the actual ask back to the
        // user.
        // No dedicated files yet (LoadAssetAtPath safely returns null if
        // missing, and PlaySfx is null-safe too) - drop these 2 .wav files
        // in later to wire them up with no code change.
        boss.bossFinalHitSe = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/BossFinalHitSe.wav");
        boss.bossDefeatSe = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/BossDefeatSe.wav");

        // Boss Milestone Presentation pass - the short "1000m到達->Boss登
        // 場" beat BossManager.Update() hands off to instead of spawning
        // the encounter immediately (see BossManager's own comment). Drawn
        // entirely from a procedural 1x1 texture via OnGUI, same as
        // ScreenTransitionManager - no sprite/scene asset needed here.
        GameObject bossPresentationGO = new GameObject("BossMilestonePresentation");
        BossMilestonePresentation bossPresentation = bossPresentationGO.AddComponent<BossMilestonePresentation>();
        // No dedicated files yet (LoadAssetAtPath safely returns null if
        // missing, and PlaySfx is null-safe too) - drop these 3 .wav files
        // in later to wire them up with no code change.
        bossPresentation.milestoneSe = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/MilestoneSe.wav");
        bossPresentation.bossWarningSe = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/BossWarningSe.wav");
        bossPresentation.bossAppearSe = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/BossAppearSe.wav");

        // Boss Defeat Presentation pass - the "Boss撃破 -> GAME CLEAR" beat
        // (see BossManager.CheckEncounterComplete, which calls into this).
        GameObject bossDefeatGO = new GameObject("BossDefeatPresentation");
        BossDefeatPresentation bossDefeatPresentation = bossDefeatGO.AddComponent<BossDefeatPresentation>();
        bossDefeatPresentation.milestoneClearSe = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/MilestoneClearSe.wav");

        // Distance Level Design Ver.1/1.1 - the Tier table (Enemy Category
        // availability) and Formation table (pre-defined SpawnPoint
        // patterns, own minDistance/maxDistance each) - both are public
        // Inspector arrays on the component afterward, this is just the
        // starting data. Tiers are cumulative (a later one only ADDS
        // categories); Formations each carry their own valid distance
        // range now instead of being nested under a Tier.
        GameObject tierGO = new GameObject("DistanceTierManager");
        DistanceTierManager tierManager = tierGO.AddComponent<DistanceTierManager>();
        tierManager.tiers = BuildDistanceTiers();
        tierManager.formations = BuildFormations();

        // Distance Level Design Ver.1, item 9 - World Time Cycle. The
        // EXISTING "Background" GameObject (bgGO, created near the top of
        // this method) is dayLayer, untouched; nightLayer is a fresh
        // second BackgroundFollower-driven layer (same cover-scale/camera-
        // follow code, just a different sprite/sortingOrder/alpha), only
        // created if the Night art actually imported successfully.
        GameObject timeGO = new GameObject("WorldTimeCycle");
        WorldTimeCycle timeCycle = timeGO.AddComponent<WorldTimeCycle>();
        timeCycle.dayLayer = dayBackgroundSr;
        Sprite nightSprite = ConfigureAndLoadSpriteWithCenterPivot("Assets/Art/Background/NightFloatingIsland.png", 941f);
        if (nightSprite != null)
        {
            GameObject nightGO = new GameObject("NightBackground");
            var nightSr = nightGO.AddComponent<SpriteRenderer>();
            nightSr.sprite = nightSprite;
            nightSr.sortingOrder = -99; // one above the Day background's -100, still well behind gameplay
            var nightFollower = nightGO.AddComponent<BackgroundFollower>();
            nightFollower.cam = cam;
            timeCycle.nightLayer = nightSr;
        }

        // Enemy wall (every 500m, a vertical column of grunt enemies -
        // skipped whenever that milestone coincides with a boss encounter)
        GameObject wallGO = new GameObject("EnemyWallManager");
        EnemyWallManager wallManager = wallGO.AddComponent<EnemyWallManager>();
        wallManager.player = player.transform;
        wallManager.squareSprite = squareSprite;
        wallManager.enemySprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Enemy/enemy_v1.png");
        wallManager.enemyPool = new List<EnemyDefinition>(EnemyDatabase.AllEnemies);

        // Stage01 荒野街道 最小実装(2026-09-13) - 石/小木/壁/壊せる木/
        // 巨大石を一定間隔で配置する。EnemyWallManagerと同じ「player直下に
        // 生成しplayer.position.xを起点に前方の距離だけ管理する」配置。
        GameObject obstacleGO = new GameObject("ObstacleSpawner");
        ObstacleSpawner obstacleSpawner = obstacleGO.AddComponent<ObstacleSpawner>();
        obstacleSpawner.player = player.transform;
        obstacleSpawner.squareSprite = squareSprite;

        // Stage01 荒野街道 完成版素材(2026-09-13) - 「プレースホルダー/
        // 単色四角は残さないでください」への対応。5種類それぞれ専用に
        // ChatGPTで生成・content-awareクロップ済みの実スプライトを
        // ConfigureAndLoadSpriteWithFootPivotで(足元pivotで)読み込み、
        // ObstacleSpawnerのデフォルトspecs(色付き四角フォールバック)を
        // 実アート版で上書きする。PPUはtargetHeight(ゴブリン実効高さ
        // ~1.41 world unitsを基準にした完成版要求仕様書の相対サイズ:
        // 石70%/小木90%/壁150%/壊せる木100%/巨大石200%+)から
        // 各画像の実クロップ高さ(px)を割って算出した。
        Sprite obstacleRockSprite = ConfigureAndLoadSpriteWithFootPivot("Assets/Art/Obstacles_v1/obstacle_rock.png", 138f);
        Sprite obstacleSmallTreeSprite = ConfigureAndLoadSpriteWithFootPivot("Assets/Art/Obstacles_v1/obstacle_smalltree.png", 196.8f);
        Sprite obstacleBreakableTreeSprite = ConfigureAndLoadSpriteWithFootPivot("Assets/Art/Obstacles_v1/obstacle_breakabletree.png", 235f);
        Sprite obstacleWallSprite = ConfigureAndLoadSpriteWithFootPivot("Assets/Art/Obstacles_v1/obstacle_wall.png", 186.7f);
        Sprite obstacleGiantRockSprite = ConfigureAndLoadSpriteWithFootPivot("Assets/Art/Obstacles_v1/obstacle_giantrock.png", 181.4f);

        obstacleSpawner.specs = new[]
        {
            new ObstacleSpawner.ObstacleSpec { name = "Rock", sprite = obstacleRockSprite, targetHeight = 1.0f, color = Color.white, breakable = false, hp = 1, weight = 30f },
            new ObstacleSpawner.ObstacleSpec { name = "SmallTree", sprite = obstacleSmallTreeSprite, targetHeight = 1.25f, color = Color.white, breakable = false, hp = 1, weight = 25f },
            new ObstacleSpawner.ObstacleSpec { name = "BreakableTree", sprite = obstacleBreakableTreeSprite, targetHeight = 1.4f, color = Color.white, breakable = true, hp = 2, weight = 20f },
            new ObstacleSpawner.ObstacleSpec { name = "Wall", sprite = obstacleWallSprite, targetHeight = 2.1f, color = Color.white, breakable = false, hp = 1, weight = 15f },
            new ObstacleSpawner.ObstacleSpec { name = "GiantRock", sprite = obstacleGiantRockSprite, targetHeight = 2.8f, color = Color.white, breakable = false, hp = 1, weight = 10f },
        };

        // ルート構造再調整(2026-09-13) - 上ルート(Easy)専用の軽い敵配置。
        // EnemyWallManagerと同じ敵プール(EnemyDatabase.AllEnemies=ゴブリン
        // /鳥)をそのまま再利用する - 新種族や新しいFormationは追加しない。
        GameObject upperEnemyGO = new GameObject("UpperRouteEnemySpawner");
        UpperRouteEnemySpawner upperEnemySpawner = upperEnemyGO.AddComponent<UpperRouteEnemySpawner>();
        upperEnemySpawner.player = player.transform;
        upperEnemySpawner.squareSprite = squareSprite;
        upperEnemySpawner.enemyPool = new List<EnemyDefinition>(EnemyDatabase.AllEnemies);

        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true)
        };

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("SceneBuilder: prototype scene built at " + ScenePath);
    }

    // Builds the whole card-draw presentation (Canvas, EventSystem, dim
    // background, deck stack, 3 cards, glow) for the level-up reward
    // sequence - see RewardCardSequence for the coroutine that drives it.
    // Starts fully hidden (RewardCardRoot inactive); GameManager activates
    // it by calling StartSequence when a level-up actually happens.
    static RewardCardSequence BuildRewardCardCanvas()
    {
        GameObject canvasGO = new GameObject("RewardCardCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // above everything else Unity renders (OnGUI still draws on top of this)

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<StandaloneInputModule>();
        }

        Sprite cardBackSprite = LoadTiledSprite("Assets/Art/UI/CardBack.png", 100f);
        Sprite cardFrameSprite = LoadTiledSprite("Assets/Art/UI/CardFrame.png", 100f);
        // Card UI改修(2026-09-08) - 新共通素材(カード下地/タイトル帯)。
        Sprite cardBaseSprite = LoadTiledSprite("Assets/Art/UI/CardFrames/CardBase.png", 100f);
        Sprite cardTitleBandSprite = LoadTiledSprite("Assets/Art/UI/CardFrames/CardTitlePlate.png", 100f);
        Sprite glowSprite = CreateRadialGlowSprite();

        GameObject rootGO = new GameObject("RewardCardRoot");
        rootGO.transform.SetParent(canvasGO.transform, false);
        RectTransform rootRect = rootGO.AddComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        CanvasGroup rootGroup = rootGO.AddComponent<CanvasGroup>();
        rootGroup.alpha = 0f;
        RewardCardSequence sequence = rootGO.AddComponent<RewardCardSequence>();
        sequence.rootGroup = rootGroup;
        rootGO.SetActive(false);

        // Level Up Presentation pass - "LEVEL UP" pop text. A SIBLING of
        // rootGO (parented directly to canvasGO), not a child of it -
        // rootGroup.alpha starts at 0 and CanvasGroup alpha multiplies down
        // the hierarchy, so a child of rootGO couldn't be shown before the
        // card UI itself starts fading in. Its own CanvasGroup lets
        // RewardCardSequence show/hide it completely independently (see
        // PlayLevelUpAnnouncement).
        GameObject levelUpTextGO = new GameObject("LevelUpText");
        levelUpTextGO.transform.SetParent(canvasGO.transform, false);
        RectTransform levelUpTextRect = levelUpTextGO.AddComponent<RectTransform>();
        levelUpTextRect.anchorMin = levelUpTextRect.anchorMax = new Vector2(0.5f, 0.5f);
        levelUpTextRect.pivot = new Vector2(0.5f, 0.5f);
        levelUpTextRect.sizeDelta = new Vector2(700f, 120f);
        levelUpTextRect.anchoredPosition = new Vector2(0f, 60f);
        CanvasGroup levelUpTextGroup = levelUpTextGO.AddComponent<CanvasGroup>();
        levelUpTextGroup.alpha = 0f;
        levelUpTextGroup.blocksRaycasts = false;
        levelUpTextGO.SetActive(false);
        Text levelUpText = levelUpTextGO.AddComponent<Text>();
        ConfigureCardText(levelUpText, 64, FontStyle.Bold, new Color(1f, 0.85f, 0.4f));
        levelUpText.text = "LEVEL UP";
        sequence.levelUpTextGroup = levelUpTextGroup;
        sequence.levelUpTextRect = levelUpTextRect;
        // Run Continuation/Checkpoint Ver.1 - RewardCardSequence now
        // overwrites this text per-call ("LEVEL UP" vs "BOSS REWARD" - see
        // StartSequence's announcementText param), so it needs the actual
        // Text component wired, not just the group/rect.
        sequence.levelUpText = levelUpText;

        // Dim background - a plain full-screen black Image (not IMGUI,
        // unlike the pause dim used elsewhere) so it sits correctly behind
        // the cards in this Canvas instead of always drawing on top the way
        // OnGUI would.
        GameObject dimGO = new GameObject("Dim");
        dimGO.transform.SetParent(rootGO.transform, false);
        RectTransform dimRect = dimGO.AddComponent<RectTransform>();
        StretchFull(dimRect);
        Image dimImage = dimGO.AddComponent<Image>();
        dimImage.color = new Color(0f, 0f, 0f, 0.75f);
        dimImage.raycastTarget = false;
        sequence.dimImage = dimImage;

        // Deck: a small stack of card-back copies near the bottom center,
        // purely decorative - never shrinks as cards are drawn.
        GameObject deckRootGO = new GameObject("DeckRoot");
        deckRootGO.transform.SetParent(rootGO.transform, false);
        RectTransform deckRect = deckRootGO.AddComponent<RectTransform>();
        deckRect.anchorMin = deckRect.anchorMax = new Vector2(0.5f, 0.5f);
        deckRect.pivot = new Vector2(0.5f, 0.5f);
        deckRect.sizeDelta = Vector2.zero;
        deckRect.anchoredPosition = new Vector2(0f, -420f);
        sequence.deckRoot = deckRect;

        const float cardWidth = 260f; // Card UI改修 - spec's "報酬選択: 260x390"
        float cardHeight = cardWidth * CardAspect;

        var deckImages = new Image[3];
        for (int i = 0; i < 3; i++)
        {
            GameObject stackGO = new GameObject("DeckCard" + i);
            stackGO.transform.SetParent(deckRootGO.transform, false);
            RectTransform stackRect = stackGO.AddComponent<RectTransform>();
            stackRect.anchorMin = stackRect.anchorMax = new Vector2(0.5f, 0.5f);
            stackRect.pivot = new Vector2(0.5f, 0.5f);
            stackRect.sizeDelta = new Vector2(cardWidth * 0.75f, cardHeight * 0.75f);
            // Slight offset per layer so the stack reads as several cards,
            // not one.
            stackRect.anchoredPosition = new Vector2(i * 4f, i * -3f);
            Image stackImage = stackGO.AddComponent<Image>();
            stackImage.sprite = cardBackSprite;
            stackImage.raycastTarget = false;
            stackImage.enabled = false;
            deckImages[i] = stackImage;
        }
        sequence.deckStackImages = deckImages;

        // The 3 drawn cards, and the slots they fly out to.
        sequence.cardSlotPositions = new[]
        {
            new Vector2(-340f, 40f),
            new Vector2(0f, 40f),
            new Vector2(340f, 40f)
        };
        var cardComponents = new RewardCardUI[3];
        for (int i = 0; i < 3; i++)
        {
            // カード選択UI再設計(2026-09-12第3弾) - 「引いた3枚のカードその
            // ものを最後まで選択UIとして使う」との明示的な依頼により、この
            // カード自身をタップ可能にする(旧: onClick=null、横長行UIへの
            // 切り替え後にだけタップ可能にしていた)。
            cardComponents[i] = CreateRewardCard(rootGO.transform, i, cardWidth, cardHeight, cardBackSprite, cardFrameSprite, sequence.OnCardClicked, cardBaseSprite, cardTitleBandSprite);
        }
        sequence.cards = cardComponents;

        // カード選択UI再設計(2026-09-12第3弾) - マスター指示「前回の横長
        // 3段リスト形式は今回は使用せず、カード3枚＋共通の詳細説明エリア
        // という構成で」。旧ChoicePanel(横長3行UIとその見出し)は完全に
        // 廃止し、代わりにカードの下に1つだけの「詳細説明エリア」を置く -
        // タップされたカードのTitle/Lv/効果説明をここに書き換える方式
        // (RewardCardSequence.UpdateDetailPanel参照)、カードごとに別々の
        // パネルは出さない。
        GameObject detailPanelGO = new GameObject("DetailPanel");
        detailPanelGO.transform.SetParent(rootGO.transform, false);
        RectTransform detailPanelRect = detailPanelGO.AddComponent<RectTransform>();
        detailPanelRect.anchorMin = detailPanelRect.anchorMax = new Vector2(0.5f, 0.5f);
        detailPanelRect.pivot = new Vector2(0.5f, 0.5f);
        const float detailPanelWidth = 1200f;
        const float detailPanelHeight = 220f;
        detailPanelRect.sizeDelta = new Vector2(detailPanelWidth, detailPanelHeight);
        // カードは(-340/0/340, 40)、半分の高さ195なので下端はy=-155 -
        // その少し下に余白を空けて配置する。
        detailPanelRect.anchoredPosition = new Vector2(0f, -300f);
        CanvasGroup detailPanelGroup = detailPanelGO.AddComponent<CanvasGroup>();
        detailPanelGroup.alpha = 0f;
        detailPanelGO.SetActive(false);
        sequence.detailPanelGroup = detailPanelGroup;

        Sprite panelSprite = RoundedPanelSprite();
        const float detailBorderPx = 5f;
        GameObject detailEdgeGO = new GameObject("Edge");
        detailEdgeGO.transform.SetParent(detailPanelGO.transform, false);
        StretchFull(detailEdgeGO.AddComponent<RectTransform>());
        Image detailEdgeImage = detailEdgeGO.AddComponent<Image>();
        detailEdgeImage.sprite = panelSprite;
        detailEdgeImage.type = Image.Type.Sliced;
        detailEdgeImage.color = new Color(0.83f, 0.68f, 0.32f, 1f);
        detailEdgeImage.raycastTarget = false;

        GameObject detailBgGO = new GameObject("Background");
        detailBgGO.transform.SetParent(detailPanelGO.transform, false);
        RectTransform detailBgRect = detailBgGO.AddComponent<RectTransform>();
        detailBgRect.anchorMin = Vector2.zero;
        detailBgRect.anchorMax = Vector2.one;
        detailBgRect.offsetMin = new Vector2(detailBorderPx, detailBorderPx);
        detailBgRect.offsetMax = new Vector2(-detailBorderPx, -detailBorderPx);
        Image detailBgImage = detailBgGO.AddComponent<Image>();
        detailBgImage.sprite = panelSprite;
        detailBgImage.type = Image.Type.Sliced;
        detailBgImage.color = new Color(0.06f, 0.08f, 0.17f, 0.92f);
        detailBgImage.raycastTarget = false;

        GameObject detailTitleGO = new GameObject("Title");
        detailTitleGO.transform.SetParent(detailPanelGO.transform, false);
        RectTransform detailTitleRect = detailTitleGO.AddComponent<RectTransform>();
        detailTitleRect.anchorMin = new Vector2(0f, 1f);
        detailTitleRect.anchorMax = new Vector2(1f, 1f);
        detailTitleRect.pivot = new Vector2(0.5f, 1f);
        detailTitleRect.sizeDelta = new Vector2(-64f, 56f);
        detailTitleRect.anchoredPosition = new Vector2(0f, -18f);
        Text detailTitleText = detailTitleGO.AddComponent<Text>();
        ConfigureCardText(detailTitleText, 40, FontStyle.Bold, new Color(1f, 0.85f, 0.4f));
        detailTitleText.resizeTextForBestFit = true;
        detailTitleText.resizeTextMinSize = 16;
        detailTitleText.resizeTextMaxSize = 40;
        sequence.detailTitleText = detailTitleText;

        GameObject detailLevelGO = new GameObject("LevelLine");
        detailLevelGO.transform.SetParent(detailPanelGO.transform, false);
        RectTransform detailLevelRect = detailLevelGO.AddComponent<RectTransform>();
        detailLevelRect.anchorMin = new Vector2(1f, 1f);
        detailLevelRect.anchorMax = new Vector2(1f, 1f);
        detailLevelRect.pivot = new Vector2(1f, 1f);
        detailLevelRect.sizeDelta = new Vector2(280f, 40f);
        detailLevelRect.anchoredPosition = new Vector2(-32f, -20f);
        Text detailLevelText = detailLevelGO.AddComponent<Text>();
        ConfigureCardText(detailLevelText, 24, FontStyle.Bold, new Color(1f, 0.92f, 0.7f));
        detailLevelText.alignment = TextAnchor.MiddleRight;
        sequence.detailLevelText = detailLevelText;

        GameObject detailDescGO = new GameObject("Description");
        detailDescGO.transform.SetParent(detailPanelGO.transform, false);
        RectTransform detailDescRect = detailDescGO.AddComponent<RectTransform>();
        detailDescRect.anchorMin = new Vector2(0f, 0f);
        detailDescRect.anchorMax = new Vector2(1f, 1f);
        detailDescRect.offsetMin = new Vector2(32f, 44f);
        detailDescRect.offsetMax = new Vector2(-32f, -66f);
        Text detailDescriptionText = detailDescGO.AddComponent<Text>();
        ConfigureCardText(detailDescriptionText, 26, FontStyle.Normal, new Color(0.9f, 0.92f, 0.97f));
        detailDescriptionText.alignment = TextAnchor.UpperLeft;
        detailDescriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
        detailDescriptionText.verticalOverflow = VerticalWrapMode.Overflow;
        sequence.detailDescriptionText = detailDescriptionText;

        GameObject detailHintGO = new GameObject("Hint");
        detailHintGO.transform.SetParent(detailPanelGO.transform, false);
        RectTransform detailHintRect = detailHintGO.AddComponent<RectTransform>();
        detailHintRect.anchorMin = new Vector2(0f, 0f);
        detailHintRect.anchorMax = new Vector2(1f, 0f);
        detailHintRect.pivot = new Vector2(0.5f, 0f);
        detailHintRect.sizeDelta = new Vector2(-64f, 34f);
        detailHintRect.anchoredPosition = new Vector2(0f, 14f);
        Text detailHintText = detailHintGO.AddComponent<Text>();
        ConfigureCardText(detailHintText, 20, FontStyle.Italic, new Color(0.65f, 0.9f, 1f));
        detailHintText.alignment = TextAnchor.LowerRight;
        sequence.detailHintText = detailHintText;

        // Glow burst behind whichever card gets confirmed.
        GameObject glowGO = new GameObject("Glow");
        glowGO.transform.SetParent(rootGO.transform, false);
        RectTransform glowRect = glowGO.AddComponent<RectTransform>();
        glowRect.anchorMin = glowRect.anchorMax = new Vector2(0.5f, 0.5f);
        glowRect.pivot = new Vector2(0.5f, 0.5f);
        glowRect.sizeDelta = new Vector2(cardWidth * 2.2f, cardWidth * 2.2f);
        Image glowImage = glowGO.AddComponent<Image>();
        glowImage.sprite = glowSprite;
        glowImage.raycastTarget = false;
        glowImage.color = new Color(1f, 0.82f, 0.35f, 0f);
        glowGO.SetActive(false);
        sequence.glowImage = glowImage;

        // Level Up Presentation pass - no dedicated file yet (LoadAssetAtPath
        // safely returns null if missing, and PlaySfx is null-safe too);
        // drop Assets/Audio/SE/LevelUpSe.wav in later to wire it up with no
        // code change.
        sequence.levelUpSe = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/LevelUpSe.wav");
        sequence.deckAppearSe = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/CardDeckAppearSe.wav");
        sequence.drawSe = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/CardDrawSe.wav");
        sequence.flipSe = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/CardFlipSe.wav");
        sequence.selectSe = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/CardSelectSe.wav");
        sequence.confirmSe = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SE/CardConfirmSe.wav");

        return sequence;
    }

    // Deck Edit screen: a left panel of every CardDatabase.AllCards entry
    // (all owned, since there's no unlock/collection system yet) and a
    // right panel of the DeckCapacity slots currently filled by the deck -
    // both scrolling grids (GridLayoutGroup inside a ScrollRect) built from
    // the same card visuals as the reward-card sequence (CreateRewardCard),
    // so growing the database later needs no layout changes here. Tapping
    // an owned card adds it; tapping a filled deck slot removes it -
    // DeckEditUI hit-tests these taps itself (see its class comment for
    // why it doesn't use Button.onClick here), so this method passes null
    // for CreateRewardCard's onClick and doesn't wire the back button's
    // onClick either.
    // Starts hidden; GameManager.OpenDeckEdit() activates it.
    static DeckEditUI BuildDeckEditCanvas()
    {
        GameObject canvasGO = new GameObject("DeckEditCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90; // below RewardCardCanvas (100), above default rendering

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<StandaloneInputModule>();
        }

        Sprite cardBackSprite = LoadTiledSprite("Assets/Art/UI/CardBack.png", 100f);
        Sprite cardFrameSprite = LoadTiledSprite("Assets/Art/UI/CardFrame.png", 100f);
        Sprite cardBaseSprite = LoadTiledSprite("Assets/Art/UI/CardFrames/CardBase.png", 100f);
        Sprite cardTitleBandSprite = LoadTiledSprite("Assets/Art/UI/CardFrames/CardTitlePlate.png", 100f);

        GameObject rootGO = new GameObject("DeckEditRoot");
        rootGO.transform.SetParent(canvasGO.transform, false);
        RectTransform rootRect = rootGO.AddComponent<RectTransform>();
        StretchFull(rootRect);

        DeckEditUI deckEdit = rootGO.AddComponent<DeckEditUI>();
        deckEdit.root = rootGO;
        deckEdit.rootGroup = rootGO.AddComponent<CanvasGroup>();

        // Opaque backdrop - not just a dim overlay like the reward-card
        // sequence's, since this screen sits over the TOP screen's own
        // IMGUI which GameManager separately stops drawing while this is
        // open, but should still look like a distinct full screen.
        GameObject bgGO = new GameObject("Backdrop");
        bgGO.transform.SetParent(rootGO.transform, false);
        RectTransform bgRect = bgGO.AddComponent<RectTransform>();
        StretchFull(bgRect);
        Image bgImage = bgGO.AddComponent<Image>();
        bgImage.color = new Color(0.05f, 0.06f, 0.12f, 0.96f);

        // Card UI改修(2026-09-08) - spec's統一基準サイズ「コレクション一
        // 覧/デッキ一覧: 180x270」に合わせてcardWidthを130->180へ(2:3固定
        // - CardAspect参照)。180幅では4列だとGridLayoutGroupの列間隔込み
        // で収まらない(4*180+3*22=786 > innerWidth 610)ため3列へ減らした
        // - スクロールは既にDeckEditUI自身が手動ドライブしているため
        // (Update参照)、列数を減らしても画面に収まらない項目はスクロー
        // ルで見える。
        const float cardWidth = 180f;
        float cardHeight = cardWidth * CardAspect;
        const int columns = 3;

        // Three columns side by side - COLLECTION (left) -> selected-card
        // detail (center) -> DECK (right) - matching the reference mockup's
        // left-to-right reading order. Each has its own ornate-framed panel
        // (via BuildDeckPanel/CreateOrnatePanel), so their borders alone
        // separate the three regions without needing extra divider lines.
        const float sideCenterX = 590f;
        // 700, not 640 - with DeckPanelContentPad's wider content margin
        // (45px/side, up from the old 30px, for the border-overflow fix -
        // see CreateOrnatePanel/OrnatePanelPixelsPerUnit), a 640-wide panel
        // no longer leaves quite enough innerWidth for 4 columns of
        // cardWidth-wide cards plus GridLayoutGroup's own spacing (needs
        // 4*130 + 3*22 = 586px) without clipping the 4th column - card size
        // itself is unchanged (per the "don't change it" brief), only the
        // panel grew to comfortably fit it again.
        const float sidePanelWidth = 700f;
        const float detailPanelWidth = 440f;

        // BuildDeckPanel creates ONE shared header+scroll+grid - every card
        // is parented under that single Content transform, not a fresh
        // panel per card (an earlier version of this method accidentally
        // called BuildDeckPanel inside the loop, creating 15 fully
        // separate overlapping panels that stacked on top of each other
        // and hid all but one card).
        ScrollRect ownedScroll = BuildDeckPanel(rootGO.transform, "COLLECTION", -sideCenterX, sidePanelWidth, cardWidth, cardHeight, columns);
        deckEdit.ownedScrollRect = ownedScroll;
        Transform ownedPanelContent = ownedScroll.content;

        // Category filter row - COLLECTION only (see BuildCategoryFilterTabs).
        BuildCategoryFilterTabs(rootGO.transform, -sideCenterX, sidePanelWidth - DeckPanelContentPad * 2f, out var filterRects, out var filterBackgrounds, out var filterLabels);
        deckEdit.filterButtonRects = filterRects;
        deckEdit.filterButtonBackgrounds = filterBackgrounds;
        deckEdit.filterButtonLabels = filterLabels;
        // Home Room UI reconstruction pass, item 6 - COLLECTION is now one
        // slot per OWNED (cardId, level) stack (see DeckEditUI.Refresh),
        // not one slot per CardDefinition - a fixed pool sized generously
        // (16 cards x up to MaxCardLevel(5)) rather than CardDatabase.
        // AllCards.Count, same convention CardFusionUI's owned list uses.
        const int ownedPoolSize = 48;
        var ownedCards = new RewardCardUI[ownedPoolSize];
        for (int i = 0; i < ownedPoolSize; i++)
        {
            ownedCards[i] = CreateRewardCard(ownedPanelContent, i, cardWidth, cardHeight, cardBackSprite, cardFrameSprite, null, cardBaseSprite, cardTitleBandSprite);
        }
        deckEdit.ownedCards = ownedCards;

        ScrollRect deckScroll = BuildDeckPanel(rootGO.transform, "DECK", sideCenterX, sidePanelWidth, cardWidth, cardHeight, columns);
        deckEdit.deckScrollRect = deckScroll;
        Transform deckPanelContent = deckScroll.content;
        var deckSlotCards = new RewardCardUI[GameManager.DeckCapacity];
        for (int i = 0; i < GameManager.DeckCapacity; i++)
        {
            deckSlotCards[i] = CreateRewardCard(deckPanelContent, i, cardWidth, cardHeight, cardBackSprite, cardFrameSprite, null, cardBaseSprite, cardTitleBandSprite);
        }
        deckEdit.deckSlotCards = deckSlotCards;

        // "おすすめ編成" / "全て外す" - DECK panel only (see
        // BuildDeckActionButtons).
        BuildDeckActionButtons(rootGO.transform, sideCenterX, sidePanelWidth - DeckPanelContentPad * 2f, out var recommendRect, out var clearRect);
        deckEdit.recommendButtonRect = recommendRect;
        deckEdit.clearButtonRect = clearRect;

        // "DECK 10 / 10" / "COLLECTION 15 / 15" - English chrome labels
        // matching the game's other UI text (START/DECK/BEST/DISTANCE are
        // all already English), each sitting under the panel it counts.
        Text collectionCountText = CreateDeckCountText(rootGO.transform, -sideCenterX);
        deckEdit.collectionCountText = collectionCountText;

        Text countText = CreateDeckCountText(rootGO.transform, sideCenterX);
        deckEdit.countText = countText;

        // Center detail column - card icon, name, category, and effect
        // description for whichever card was most recently tapped in
        // either side panel, at a size that's actually comfortable to
        // read (the grid cards themselves are only 130px wide). Same
        // height as the side panels so all three read as one aligned row.
        RectTransform detailPanelRect = CreateOrnatePanel(rootGO.transform, "DetailPanel");
        detailPanelRect.anchorMin = detailPanelRect.anchorMax = new Vector2(0.5f, 1f);
        detailPanelRect.pivot = new Vector2(0.5f, 1f);
        detailPanelRect.sizeDelta = new Vector2(detailPanelWidth, DeckPanelHeight);
        detailPanelRect.anchoredPosition = new Vector2(0f, -100f);

        // Same top/bottom content margin every other panel uses
        // (DeckPanelContentPad/DeckPanelContentBottom - see BuildDeckPanel)
        // so this panel's own ornate border never overlaps the icon/text
        // either. Top-to-bottom info flow: card preview -> name/category ->
        // effect description, per the reference layout.
        const float iconSize = 200f;
        const float nameHeight = 40f;
        const float categoryHeight = 26f;
        const float sideMargin = DeckPanelContentPad;

        GameObject detailIconGO = new GameObject("DetailIcon");
        detailIconGO.transform.SetParent(detailPanelRect, false);
        RectTransform detailIconRect = detailIconGO.AddComponent<RectTransform>();
        detailIconRect.anchorMin = detailIconRect.anchorMax = new Vector2(0.5f, 1f);
        detailIconRect.pivot = new Vector2(0.5f, 1f);
        detailIconRect.sizeDelta = new Vector2(iconSize, iconSize);
        detailIconRect.anchoredPosition = new Vector2(0f, DeckPanelHeaderY);
        Image detailIcon = detailIconGO.AddComponent<Image>();
        detailIcon.preserveAspect = true;
        detailIcon.raycastTarget = false;
        detailIcon.enabled = false; // hidden until a card is actually selected
        deckEdit.detailIcon = detailIcon;

        float nameY = DeckPanelHeaderY - iconSize - 20f;
        GameObject detailNameGO = new GameObject("DetailName");
        detailNameGO.transform.SetParent(detailPanelRect, false);
        RectTransform detailNameRect = detailNameGO.AddComponent<RectTransform>();
        detailNameRect.anchorMin = detailNameRect.anchorMax = new Vector2(0.5f, 1f);
        detailNameRect.pivot = new Vector2(0.5f, 1f);
        detailNameRect.sizeDelta = new Vector2(detailPanelWidth - sideMargin * 2f, nameHeight);
        detailNameRect.anchoredPosition = new Vector2(0f, nameY);
        Text detailName = detailNameGO.AddComponent<Text>();
        ConfigureCardText(detailName, 26, FontStyle.Bold, new Color(1f, 0.85f, 0.4f));
        deckEdit.detailName = detailName;

        float categoryY = nameY - nameHeight - 4f;
        GameObject detailCategoryGO = new GameObject("DetailCategory");
        detailCategoryGO.transform.SetParent(detailPanelRect, false);
        RectTransform detailCategoryRect = detailCategoryGO.AddComponent<RectTransform>();
        detailCategoryRect.anchorMin = detailCategoryRect.anchorMax = new Vector2(0.5f, 1f);
        detailCategoryRect.pivot = new Vector2(0.5f, 1f);
        detailCategoryRect.sizeDelta = new Vector2(detailPanelWidth - sideMargin * 2f, categoryHeight);
        detailCategoryRect.anchoredPosition = new Vector2(0f, categoryY);
        Text detailCategory = detailCategoryGO.AddComponent<Text>();
        ConfigureCardText(detailCategory, 16, FontStyle.Normal, new Color(0.85f, 0.85f, 0.92f, 0.85f));
        deckEdit.detailCategory = detailCategory;

        // Fills the remaining space down to the panel's own safe bottom
        // margin (DeckPanelContentBottom) instead of a fixed height, so it
        // never runs under the panel's bottom border regardless of how
        // tall the icon/name/category block above ends up. Home Room UI
        // reconstruction pass, item 9 - reserves a CONVERT button's height
        // at the very bottom of that space (convertReserve), only ever
        // shown/active for a COLLECTION-originated selection (see
        // DeckEditUI.RefreshConvertButton).
        const float convertReserve = 66f;
        float descY = categoryY - categoryHeight - 20f;
        float descHeight = descY - DeckPanelContentBottom - convertReserve;
        GameObject detailDescGO = new GameObject("DetailDescription");
        detailDescGO.transform.SetParent(detailPanelRect, false);
        RectTransform detailDescRect = detailDescGO.AddComponent<RectTransform>();
        detailDescRect.anchorMin = new Vector2(0f, 1f);
        detailDescRect.anchorMax = new Vector2(1f, 1f);
        detailDescRect.pivot = new Vector2(0.5f, 1f);
        detailDescRect.offsetMin = new Vector2(sideMargin, 0f);
        detailDescRect.offsetMax = new Vector2(-sideMargin, 0f);
        detailDescRect.sizeDelta = new Vector2(0f, descHeight);
        detailDescRect.anchoredPosition = new Vector2(0f, descY);
        Text detailDesc = detailDescGO.AddComponent<Text>();
        ConfigureCardText(detailDesc, 20, FontStyle.Normal, Color.white);
        detailDesc.alignment = TextAnchor.UpperCenter;
        deckEdit.detailText = detailDesc;
        deckEdit.detailPlaceholder = "カードをタップして\n詳細を確認";
        detailDesc.text = deckEdit.detailPlaceholder;

        // Item 9 - CONVERT button, right at the panel's own bottom margin.
        // Starts inactive (RefreshConvertButton toggles it) - no card is
        // selected yet on a fresh Open().
        GameObject convertGO = new GameObject("ConvertButton");
        convertGO.transform.SetParent(detailPanelRect, false);
        RectTransform convertRect = convertGO.AddComponent<RectTransform>();
        convertRect.anchorMin = new Vector2(0f, 1f);
        convertRect.anchorMax = new Vector2(1f, 1f);
        convertRect.pivot = new Vector2(0.5f, 1f);
        convertRect.offsetMin = new Vector2(sideMargin, 0f);
        convertRect.offsetMax = new Vector2(-sideMargin, 0f);
        convertRect.sizeDelta = new Vector2(0f, convertReserve - 10f);
        convertRect.anchoredPosition = new Vector2(0f, DeckPanelContentBottom + convertReserve - 10f);
        Image convertBg = convertGO.AddComponent<Image>();
        convertBg.color = new Color(0.55f, 0.42f, 0.14f, 0.9f); // same warm gold as "おすすめ編成" - a constructive action
        convertBg.raycastTarget = false;
        GameObject convertLabelGO = new GameObject("Label");
        convertLabelGO.transform.SetParent(convertGO.transform, false);
        StretchFull(convertLabelGO.AddComponent<RectTransform>());
        Text convertLabel = convertLabelGO.AddComponent<Text>();
        ConfigureCardText(convertLabel, 18, FontStyle.Bold, new Color(1f, 0.93f, 0.75f));
        deckEdit.convertButtonRect = convertRect;
        deckEdit.convertButtonLabel = convertLabel;
        convertGO.SetActive(false);

        // borderScale 2 - at 180x70 this panel is much smaller than
        // COLLECTION/DECK/SELECTED CARD (600-900 units), where the
        // baseline ~31-unit border would eat nearly half its height.
        RectTransform backRect = CreateOrnatePanel(rootGO.transform, "BackButton", borderScale: 2f);
        backRect.anchorMin = backRect.anchorMax = new Vector2(0f, 1f);
        backRect.pivot = new Vector2(0f, 1f);
        backRect.sizeDelta = new Vector2(180f, 70f);
        backRect.anchoredPosition = new Vector2(30f, -30f);
        GameObject backGO = backRect.gameObject;
        // Not wired via Button.onClick - DeckEditUI hit-tests this rect
        // itself (see its class comment). Button/Image stay purely for the
        // visible box.
        backGO.AddComponent<Button>().targetGraphic = backGO.GetComponent<Image>();
        deckEdit.backButtonRect = backRect;

        GameObject backLabelGO = new GameObject("Label");
        backLabelGO.transform.SetParent(backGO.transform, false);
        StretchFull(backLabelGO.AddComponent<RectTransform>());
        Text backLabel = backLabelGO.AddComponent<Text>();
        ConfigureCardText(backLabel, 26, FontStyle.Bold, Color.white);
        backLabel.text = "戻る";

        // Item 7 - Character Card slots (max 3), a small row tucked above
        // the DECK panel (clear of the back button, top-left) since the
        // three main panels already claim y=-100 downward.
        //
        // Card UI改修(2026-09-08), item 9-1 - 「CHARACTER CARDSの小さい装
        // 備枠が見切れやすい」の根本原因: 旧charSlotSize(84)はCreateReward
        // Cardへ幅・高さ両方に渡されており、実質「正方形」の枠にfitさせて
        // いた。frameImageはpreserveAspect=trueで実際は2:3の縦長フレーム
        // 画像を正方形の枠内にletterboxする形になり、上下(またはleft/
        // right)に大きな余白ができてカード自体が実際より小さく・窮屈に見
        // える(「見切れて」いるように感じる)原因になっていた。spec通り
        // 2:3固定(CardAspect)の縦長スロットに修正 - 幅は72(旧84よりやや
        // 狭いが、高さが108に伸びる分、正方形時とほぼ同じ「面積」感)。
        const float charSlotWidth = 72f;
        float charSlotHeight = charSlotWidth * CardAspect;
        const float charSlotGap = 14f;
        float charRowWidth = GameManager.CharacterCardSlotCount * charSlotWidth + (GameManager.CharacterCardSlotCount - 1) * charSlotGap;
        float charStartX = sideCenterX - charRowWidth / 2f + charSlotWidth / 2f;
        const float charSlotY = -58f;

        GameObject charHeaderGO = new GameObject("CharacterCardsHeader");
        charHeaderGO.transform.SetParent(rootGO.transform, false);
        RectTransform charHeaderRect = charHeaderGO.AddComponent<RectTransform>();
        charHeaderRect.anchorMin = charHeaderRect.anchorMax = new Vector2(0.5f, 1f);
        charHeaderRect.pivot = new Vector2(0.5f, 1f);
        charHeaderRect.sizeDelta = new Vector2(400f, 26f);
        charHeaderRect.anchoredPosition = new Vector2(sideCenterX, -8f);
        Text charHeaderText = charHeaderGO.AddComponent<Text>();
        ConfigureCardText(charHeaderText, 16, FontStyle.Bold, new Color(1f, 0.85f, 0.4f));
        charHeaderText.text = "CHARACTER CARDS";

        var characterSlots = new RewardCardUI[GameManager.CharacterCardSlotCount];
        for (int i = 0; i < GameManager.CharacterCardSlotCount; i++)
        {
            RewardCardUI slot = CreateRewardCard(rootGO.transform, 1000 + i, charSlotWidth, charSlotHeight, cardBackSprite, cardFrameSprite, null, cardBaseSprite, cardTitleBandSprite);
            slot.rect.anchorMin = slot.rect.anchorMax = new Vector2(0.5f, 1f);
            slot.rect.pivot = new Vector2(0.5f, 1f);
            slot.rect.anchoredPosition = new Vector2(charStartX + i * (charSlotWidth + charSlotGap), charSlotY);
            characterSlots[i] = slot;
        }
        deckEdit.characterSlotCards = characterSlots;

        // Item 9 - shared "magic circle" glow for Convert (see
        // DeckEditUI.PlayConvertGlow), centered on screen, hidden by
        // default. Reuses the same tintable ring CardFusionUI uses.
        Sprite magicCircleSprite = LoadTiledSprite("Assets/Art/Effects/DoubleJumpRing.png", 1672f);
        GameObject circleGO = new GameObject("MagicCircle");
        circleGO.transform.SetParent(rootGO.transform, false);
        RectTransform circleRect = circleGO.AddComponent<RectTransform>();
        circleRect.anchorMin = circleRect.anchorMax = new Vector2(0.5f, 0.5f);
        circleRect.pivot = new Vector2(0.5f, 0.5f);
        circleRect.sizeDelta = new Vector2(500f, 500f);
        circleRect.anchoredPosition = Vector2.zero;
        Image circleImage = circleGO.AddComponent<Image>();
        circleImage.sprite = magicCircleSprite;
        circleImage.preserveAspect = true;
        circleImage.raycastTarget = false;
        circleGO.SetActive(false);
        deckEdit.magicCircleImage = circleImage;

        // Built last so it's the last sibling under rootGO.transform and
        // renders above every panel/card already built above (uGUI draws
        // by sibling order) - starts hidden (see BuildConfirmDialog).
        deckEdit.confirmDialog = BuildConfirmDialog(rootGO.transform);

        rootGO.SetActive(false);
        return deckEdit;
    }

    // Home Room UI reconstruction pass - the new independent Card Fusion
    // screen (see CardFusionUI's own class comment). Same overlay-Canvas
    // pattern as BuildDeckEditCanvas (raw-touch, no Button.onClick), but a
    // simpler layout: MAIN/SUB slots + FUSE button + one owned-cards list -
    // deliberately does NOT reuse BuildDeckPanel's own Y-position constants
    // (DeckPanelHeaderY etc.), since those are tightly coupled to
    // DeckEditUI's specific two-side-panel layout.
    static CardFusionUI BuildCardFusionCanvas()
    {
        GameObject canvasGO = new GameObject("CardFusionCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90; // same layer as DeckEditCanvas - never open simultaneously (see GameManager.AnyOverlayOpen)

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<StandaloneInputModule>();
        }

        Sprite cardBackSprite = LoadTiledSprite("Assets/Art/UI/CardBack.png", 100f);
        Sprite cardFrameSprite = LoadTiledSprite("Assets/Art/UI/CardFrame.png", 100f);
        Sprite cardBaseSprite = LoadTiledSprite("Assets/Art/UI/CardFrames/CardBase.png", 100f);
        Sprite cardTitleBandSprite = LoadTiledSprite("Assets/Art/UI/CardFrames/CardTitlePlate.png", 100f);
        // Reuses the existing Double Jump Ring effect sprite (Game Feel
        // pass) as a stand-in "magic circle" - see CardFusionUI.
        // magicCircleImage's own comment for why (no dedicated magic-
        // circle art was cut from the reference storyboards).
        Sprite magicCircleSprite = LoadTiledSprite("Assets/Art/Effects/DoubleJumpRing.png", 1672f);

        GameObject rootGO = new GameObject("CardFusionRoot");
        rootGO.transform.SetParent(canvasGO.transform, false);
        RectTransform rootRect = rootGO.AddComponent<RectTransform>();
        StretchFull(rootRect);

        CardFusionUI menu = rootGO.AddComponent<CardFusionUI>();
        menu.root = rootGO;
        menu.rootGroup = rootGO.AddComponent<CanvasGroup>();

        GameObject bgGO = new GameObject("Backdrop");
        bgGO.transform.SetParent(rootGO.transform, false);
        RectTransform bgRect = bgGO.AddComponent<RectTransform>();
        StretchFull(bgRect);
        Image bgImage = bgGO.AddComponent<Image>();
        bgImage.color = new Color(0.05f, 0.06f, 0.12f, 0.96f);

        // ===== MAIN / SUB slots (item 10) ===== //
        // Card UI改修(2026-09-08) - 旧slotSize(220の正方形)をspec通り2:3
        // 固定(CardAspect)へ - Character Card slotと同じ「正方形へletterbox
        // されて小さく見える」バグ(item 9-1と同根)を持っていたため。幅は
        // Collection/Deckグリッドと同じ180に揃え、高さはCardAspectで270。
        const float slotWidth = 180f;
        float slotHeight = slotWidth * CardAspect;
        const float slotGap = 140f; // leaves room for the magic circle between them
        const float slotY = -160f;
        RewardCardUI mainSlot = CreateRewardCard(rootGO.transform, 1, slotWidth, slotHeight, cardBackSprite, cardFrameSprite, null, cardBaseSprite, cardTitleBandSprite);
        mainSlot.rect.anchorMin = mainSlot.rect.anchorMax = new Vector2(0.5f, 1f);
        mainSlot.rect.pivot = new Vector2(0.5f, 1f);
        mainSlot.rect.anchoredPosition = new Vector2(-(slotWidth + slotGap) / 2f, slotY);
        menu.mainSlotCard = mainSlot;

        RewardCardUI subSlot = CreateRewardCard(rootGO.transform, 2, slotWidth, slotHeight, cardBackSprite, cardFrameSprite, null, cardBaseSprite, cardTitleBandSprite);
        subSlot.rect.anchorMin = subSlot.rect.anchorMax = new Vector2(0.5f, 1f);
        subSlot.rect.pivot = new Vector2(0.5f, 1f);
        subSlot.rect.anchoredPosition = new Vector2((slotWidth + slotGap) / 2f, slotY);
        menu.subSlotCard = subSlot;

        GameObject mainLabelGO = new GameObject("MainLabel");
        mainLabelGO.transform.SetParent(rootGO.transform, false);
        RectTransform mainLabelRect = mainLabelGO.AddComponent<RectTransform>();
        mainLabelRect.anchorMin = mainLabelRect.anchorMax = new Vector2(0.5f, 1f);
        mainLabelRect.pivot = new Vector2(0.5f, 1f);
        mainLabelRect.sizeDelta = new Vector2(slotWidth, 30f);
        mainLabelRect.anchoredPosition = new Vector2(-(slotWidth + slotGap) / 2f, slotY - slotHeight - 8f);
        Text mainLabel = mainLabelGO.AddComponent<Text>();
        ConfigureCardText(mainLabel, 18, FontStyle.Bold, new Color(1f, 0.85f, 0.4f));
        mainLabel.text = "MAIN CARD";

        GameObject subLabelGO = new GameObject("SubLabel");
        subLabelGO.transform.SetParent(rootGO.transform, false);
        RectTransform subLabelRect = subLabelGO.AddComponent<RectTransform>();
        subLabelRect.anchorMin = subLabelRect.anchorMax = new Vector2(0.5f, 1f);
        subLabelRect.pivot = new Vector2(0.5f, 1f);
        subLabelRect.sizeDelta = new Vector2(slotWidth, 30f);
        subLabelRect.anchoredPosition = new Vector2((slotWidth + slotGap) / 2f, slotY - slotHeight - 8f);
        Text subLabel = subLabelGO.AddComponent<Text>();
        ConfigureCardText(subLabel, 18, FontStyle.Bold, new Color(0.85f, 0.85f, 0.92f, 0.85f));
        subLabel.text = "SUB / MATERIAL CARD";

        // ===== Magic circle - centered between MAIN and SUB ===== //
        GameObject circleGO = new GameObject("MagicCircle");
        circleGO.transform.SetParent(rootGO.transform, false);
        RectTransform circleRect = circleGO.AddComponent<RectTransform>();
        circleRect.anchorMin = circleRect.anchorMax = new Vector2(0.5f, 1f);
        circleRect.pivot = new Vector2(0.5f, 1f);
        circleRect.sizeDelta = new Vector2(180f, 180f);
        circleRect.anchoredPosition = new Vector2(0f, slotY - slotHeight / 2f + 90f);
        Image circleImage = circleGO.AddComponent<Image>();
        circleImage.sprite = magicCircleSprite;
        circleImage.preserveAspect = true;
        circleImage.raycastTarget = false;
        circleGO.SetActive(false);
        menu.magicCircleImage = circleImage;

        // ===== FUSE button ===== //
        RectTransform fuseRect = CreateOrnatePanel(rootGO.transform, "FuseButton", borderScale: 2f);
        fuseRect.anchorMin = fuseRect.anchorMax = new Vector2(0.5f, 1f);
        fuseRect.pivot = new Vector2(0.5f, 1f);
        fuseRect.sizeDelta = new Vector2(320f, 64f);
        fuseRect.anchoredPosition = new Vector2(0f, slotY - slotHeight - 60f);
        menu.fuseButtonRect = fuseRect;
        GameObject fuseLabelGO = new GameObject("Label");
        fuseLabelGO.transform.SetParent(fuseRect, false);
        StretchFull(fuseLabelGO.AddComponent<RectTransform>());
        Text fuseLabel = fuseLabelGO.AddComponent<Text>();
        ConfigureCardText(fuseLabel, 22, FontStyle.Bold, Color.white);
        fuseLabel.text = "SELECT MAIN / SUB";
        menu.fuseButtonLabel = fuseLabel;

        // ===== Status text ===== //
        float statusY = slotY - slotHeight - 140f;
        GameObject statusGO = new GameObject("StatusText");
        statusGO.transform.SetParent(rootGO.transform, false);
        RectTransform statusRect = statusGO.AddComponent<RectTransform>();
        statusRect.anchorMin = statusRect.anchorMax = new Vector2(0.5f, 1f);
        statusRect.pivot = new Vector2(0.5f, 1f);
        statusRect.sizeDelta = new Vector2(1700f, 34f);
        statusRect.anchoredPosition = new Vector2(0f, statusY);
        Text statusText = statusGO.AddComponent<Text>();
        ConfigureCardText(statusText, 18, FontStyle.Normal, new Color(0.9f, 0.92f, 1f, 0.85f));
        menu.statusText = statusText;

        // ===== Owned cards list (item 11 - shows EVERY owned stack, never
        // hides a locked one) ===== //
        const float gridPanelWidth = 1750f;
        float gridTopY = statusY - 46f;
        const float gridBottomMargin = 40f;
        const int gridColumns = 7;
        // Card UI改修(2026-09-08) - Collection/Deckグリッドと同じ180x270
        // (spec統一基準サイズ)に揃えた。7列 * 180 + 6*18(spacing) = 1368,
        // gridPanelWidth(1750) - gridPad*2(45*2=90) = 1660以内に収まる。
        const float gridCardWidth = 180f;
        float gridCardHeight = gridCardWidth * CardAspect;

        RectTransform gridPanelRect = CreateOrnatePanel(rootGO.transform, "OwnedCardsPanel");
        float gridPanelHeight = 1080f + gridTopY - gridBottomMargin; // from gridTopY down to gridBottomMargin above the bottom edge
        gridPanelRect.anchorMin = gridPanelRect.anchorMax = new Vector2(0.5f, 1f);
        gridPanelRect.pivot = new Vector2(0.5f, 1f);
        gridPanelRect.sizeDelta = new Vector2(gridPanelWidth, gridPanelHeight);
        gridPanelRect.anchoredPosition = new Vector2(0f, gridTopY);

        const float gridPad = 45f;
        GameObject countGO = new GameObject("OwnedCountText");
        countGO.transform.SetParent(rootGO.transform, false);
        RectTransform countRect = countGO.AddComponent<RectTransform>();
        countRect.anchorMin = countRect.anchorMax = new Vector2(0.5f, 1f);
        countRect.pivot = new Vector2(0.5f, 1f);
        countRect.sizeDelta = new Vector2(600f, 34f);
        countRect.anchoredPosition = new Vector2(0f, gridTopY - gridPad);
        Text ownedCountText = countGO.AddComponent<Text>();
        ConfigureCardText(ownedCountText, 20, FontStyle.Bold, new Color(1f, 0.85f, 0.4f));
        menu.ownedCountText = ownedCountText;

        GameObject scrollGO = new GameObject("OwnedScroll");
        scrollGO.transform.SetParent(rootGO.transform, false);
        RectTransform scrollRect = scrollGO.AddComponent<RectTransform>();
        scrollRect.anchorMin = scrollRect.anchorMax = new Vector2(0.5f, 1f);
        scrollRect.pivot = new Vector2(0.5f, 1f);
        scrollRect.sizeDelta = new Vector2(gridPanelWidth - gridPad * 2f, gridPanelHeight - gridPad - 40f - gridPad);
        scrollRect.anchoredPosition = new Vector2(0f, gridTopY - gridPad - 40f);
        ScrollRect scroll = scrollGO.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scrollGO.AddComponent<RectMask2D>();

        GameObject contentGO = new GameObject("Content");
        contentGO.transform.SetParent(scrollGO.transform, false);
        RectTransform contentRect = contentGO.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.sizeDelta = Vector2.zero;
        scroll.content = contentRect;
        scroll.viewport = scrollRect;

        GridLayoutGroup grid = contentGO.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(gridCardWidth, gridCardHeight);
        grid.spacing = new Vector2(18f, 18f);
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = gridColumns;
        ContentSizeFitter fitter = contentGO.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.verticalNormalizedPosition = 1f;
        menu.ownedScrollRect = scroll;

        // Pre-built pool of owned-card slots - see CardFusionUI.Refresh for
        // how many can actually be shown at once (deactivates the rest).
        // 48 comfortably covers every (cardId, level) combination realistic
        // for this Ver.1's 16 cards x MaxCardLevel(5) without being wasteful.
        const int ownedPoolSize = 48;
        var ownedCards = new RewardCardUI[ownedPoolSize];
        for (int i = 0; i < ownedPoolSize; i++)
        {
            ownedCards[i] = CreateRewardCard(contentGO.transform, 2000 + i, gridCardWidth, gridCardHeight, cardBackSprite, cardFrameSprite, null, cardBaseSprite, cardTitleBandSprite);
        }
        menu.ownedCards = ownedCards;

        // ===== Reveal card (Fusion success) - centered, large, hidden by
        // default, built after everything else so it renders on top =====
        // Card UI改修(2026-09-08) - spec's「詳細表示: 360x540」に合わせた
        // (2:3固定)。
        RewardCardUI revealCard = CreateRewardCard(rootGO.transform, 9000, 360f, 360f * CardAspect, cardBackSprite, cardFrameSprite, null, cardBaseSprite, cardTitleBandSprite);
        revealCard.rect.anchorMin = revealCard.rect.anchorMax = new Vector2(0.5f, 0.5f);
        revealCard.rect.pivot = new Vector2(0.5f, 0.5f);
        revealCard.rect.anchoredPosition = Vector2.zero;
        revealCard.gameObject.SetActive(false);
        menu.revealCard = revealCard;

        // ===== Back button ===== //
        RectTransform backRect = CreateOrnatePanel(rootGO.transform, "BackButton", borderScale: 2f);
        backRect.anchorMin = backRect.anchorMax = new Vector2(0f, 1f);
        backRect.pivot = new Vector2(0f, 1f);
        backRect.sizeDelta = new Vector2(180f, 70f);
        backRect.anchoredPosition = new Vector2(30f, -30f);
        GameObject backGO = backRect.gameObject;
        backGO.AddComponent<Button>().targetGraphic = backGO.GetComponent<Image>();
        menu.backButtonRect = backRect;
        GameObject backLabelGO = new GameObject("Label");
        backLabelGO.transform.SetParent(backGO.transform, false);
        StretchFull(backLabelGO.AddComponent<RectTransform>());
        Text backLabel = backLabelGO.AddComponent<Text>();
        ConfigureCardText(backLabel, 26, FontStyle.Bold, Color.white);
        backLabel.text = "戻る";

        rootGO.SetActive(false);
        return menu;
    }

    // キャラクター選択画面(2026-09-12) - Home画面左上の新規ホットスポット
    // (GameManager.DrawCharacterHotspot)から開く全画面uGUI。DeckEditUI/
    // CardFusionUIと同じ「Button.onClick/EventSystemに頼らず自前でタップ
    // 位置を照合する」方式(CharacterSelectUI.HandleTap参照)。左のカード
    // 一覧はCharacterDatabase.AllCharactersの件数ぶんここで動的に生成する
    // ため、4人目・5人目を追加した後も本メソッド自体は変更不要。
    static CharacterSelectUI BuildCharacterSelectCanvas()
    {
        GameObject canvasGO = new GameObject("CharacterSelectCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90; // DeckEdit/CardFusionと同じ帯 - 同時に開くことはない(GameManager.AnyOverlayOpen)

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<StandaloneInputModule>();
        }

        GameObject rootGO = new GameObject("CharacterSelectRoot");
        rootGO.transform.SetParent(canvasGO.transform, false);
        RectTransform rootRect = rootGO.AddComponent<RectTransform>();
        StretchFull(rootRect);
        CanvasGroup rootGroup = rootGO.AddComponent<CanvasGroup>();

        CharacterSelectUI ui = rootGO.AddComponent<CharacterSelectUI>();
        ui.root = rootRect;
        ui.rootGroup = rootGroup;

        // 背景 - DeckEditUI/CardFusionUIの"Backdrop"と同じ単色塗り(濃紺)に
        // 統一した。当初はHome部屋背景(TopBackgroundHomeRoom.png)を暗め
        // に転用する案も検討したが、その画像は既にGameManager.topBackground
        // (LoadIconTexture、Texture2D/Default設定)としてHome画面のOnGUI
        // 描画に使われており、ここで別の設定(LoadTiledSprite、Sprite/
        // Repeat設定)で読み込み直すと同じアセットのインポート設定を
        // 上書きしてしまい、Home画面側の見た目に意図しない影響が出る恐れ
        // があった(「既存Home全体のレイアウトを大改造しない」という明示
        //的な制約に抵触するリスク) - 新規アートを増やさずに済み、かつ
        // 既存の他画面と統一感もあるこの単色塗りを採用した。
        GameObject bgGO = new GameObject("Background");
        bgGO.transform.SetParent(rootGO.transform, false);
        RectTransform bgRect = bgGO.AddComponent<RectTransform>();
        StretchFull(bgRect);
        Image bgImage = bgGO.AddComponent<Image>();
        bgImage.color = new Color(0.04f, 0.05f, 0.1f, 0.97f);
        bgImage.raycastTarget = false;

        // ヘッダー
        GameObject headerGO = new GameObject("HeaderTitle");
        headerGO.transform.SetParent(rootGO.transform, false);
        RectTransform headerRect = headerGO.AddComponent<RectTransform>();
        headerRect.anchorMin = headerRect.anchorMax = new Vector2(0f, 1f);
        headerRect.pivot = new Vector2(0f, 1f);
        headerRect.sizeDelta = new Vector2(760f, 64f);
        headerRect.anchoredPosition = new Vector2(56f, -36f);
        Text headerText = headerGO.AddComponent<Text>();
        ConfigureCardText(headerText, 44, FontStyle.Bold, new Color(1f, 0.85f, 0.4f));
        headerText.alignment = TextAnchor.MiddleLeft;
        headerText.text = "CHARACTER SELECT";

        // BACK(左下)
        RectTransform backRect = CreateOrnatePanel(rootGO.transform, "BackButton", borderScale: 2f);
        backRect.anchorMin = backRect.anchorMax = new Vector2(0f, 0f);
        backRect.pivot = new Vector2(0f, 0f);
        backRect.sizeDelta = new Vector2(220f, 76f);
        backRect.anchoredPosition = new Vector2(56f, 40f);
        GameObject backLabelGO = new GameObject("Label");
        backLabelGO.transform.SetParent(backRect, false);
        StretchFull(backLabelGO.AddComponent<RectTransform>());
        Text backLabel = backLabelGO.AddComponent<Text>();
        ConfigureCardText(backLabel, 28, FontStyle.Bold, new Color(0.9f, 0.92f, 0.97f));
        backLabel.text = "« BACK";
        ui.backButtonRect = backRect;

        // SELECT(右下)
        RectTransform selectRect = CreateOrnatePanel(rootGO.transform, "SelectButton", borderScale: 2f);
        selectRect.anchorMin = selectRect.anchorMax = new Vector2(1f, 0f);
        selectRect.pivot = new Vector2(1f, 0f);
        selectRect.sizeDelta = new Vector2(280f, 76f);
        selectRect.anchoredPosition = new Vector2(-56f, 40f);
        GameObject selectLabelGO = new GameObject("Label");
        selectLabelGO.transform.SetParent(selectRect, false);
        StretchFull(selectLabelGO.AddComponent<RectTransform>());
        Text selectLabel = selectLabelGO.AddComponent<Text>();
        ConfigureCardText(selectLabel, 30, FontStyle.Bold, new Color(1f, 0.9f, 0.5f));
        selectLabel.text = "SELECT";
        ui.selectButtonRect = selectRect;

        // 左: キャラクター一覧 - CharacterDatabase.AllCharactersの件数ぶん
        // 動的に生成(将来キャラクターが増えてもここは変更不要)。
        var allCharacters = CharacterDatabase.AllCharacters;
        const float cardWidth = 210f;
        const float cardHeight = cardWidth * 1.85f; // 参考画像のカード比率に近い縦長
        const float cardSpacing = 24f;

        var cardSlotRects = new RectTransform[allCharacters.Count];
        var cardGlowImages = new Image[allCharacters.Count];

        for (int i = 0; i < allCharacters.Count; i++)
        {
            CharacterDefinition def = allCharacters[i];

            GameObject slotGO = new GameObject("CharacterSlot_" + def.characterId);
            slotGO.transform.SetParent(rootGO.transform, false);
            RectTransform slotRect = slotGO.AddComponent<RectTransform>();
            slotRect.anchorMin = slotRect.anchorMax = new Vector2(0f, 0.5f);
            slotRect.pivot = new Vector2(0f, 0.5f);
            slotRect.sizeDelta = new Vector2(cardWidth, cardHeight);
            slotRect.anchoredPosition = new Vector2(56f + i * (cardWidth + cardSpacing), 60f);

            // 選択中の縁の発光 - 金枠+シアン寄りの淡い外周(マスター指示の
            // 「金枠・シアン発光・Selection marker」)。ポートレート画像
            // より一回り大きい丸角パネルとして背後に重ね、選択中のスロット
            // だけSetActive(true)にする(EquippedBadgeと同じ「表示状態だけ
            // 切り替える」パターン)。
            GameObject glowGO = new GameObject("SelectionGlow");
            glowGO.transform.SetParent(slotGO.transform, false);
            RectTransform glowRect = glowGO.AddComponent<RectTransform>();
            glowRect.anchorMin = Vector2.zero;
            glowRect.anchorMax = Vector2.one;
            glowRect.offsetMin = new Vector2(-10f, -10f);
            glowRect.offsetMax = new Vector2(10f, 10f);
            Image glowImage = glowGO.AddComponent<Image>();
            glowImage.sprite = RoundedPanelSprite();
            glowImage.type = Image.Type.Sliced;
            glowImage.color = new Color(1f, 0.85f, 0.4f, 0.95f);
            glowImage.raycastTarget = false;
            glowGO.SetActive(false);

            GameObject portraitGO = new GameObject("Portrait");
            portraitGO.transform.SetParent(slotGO.transform, false);
            RectTransform portraitRect = portraitGO.AddComponent<RectTransform>();
            StretchFull(portraitRect);
            Image portraitImage = portraitGO.AddComponent<Image>();
            portraitImage.sprite = ToUiSprite(def.portrait);
            portraitImage.preserveAspect = true;

            // 見た目のButton(タップ判定自体はCharacterSelectUI.HandleTapが
            // 自前で行う、DeckEditUI等と同じ方針) - targetGraphicがあると
            // ポインタの状態変化を素直に受け付けられる。
            Button slotButton = slotGO.AddComponent<Button>();
            slotButton.targetGraphic = portraitImage;
            slotButton.transition = Selectable.Transition.None;

            cardSlotRects[i] = slotRect;
            cardGlowImages[i] = glowImage;
        }
        ui.cardSlotRects = cardSlotRects;
        ui.cardGlowImages = cardGlowImages;

        // 中央: 選択中キャラクターの大きなビジュアル。
        GameObject mainVisualGO = new GameObject("MainVisual");
        mainVisualGO.transform.SetParent(rootGO.transform, false);
        RectTransform mainVisualRect = mainVisualGO.AddComponent<RectTransform>();
        mainVisualRect.anchorMin = mainVisualRect.anchorMax = new Vector2(0.5f, 0.46f);
        mainVisualRect.pivot = new Vector2(0.5f, 0.5f);
        mainVisualRect.sizeDelta = new Vector2(560f, 880f);
        mainVisualRect.anchoredPosition = new Vector2(60f, 0f);
        CanvasGroup mainVisualGroup = mainVisualGO.AddComponent<CanvasGroup>();
        Image mainVisualImage = mainVisualGO.AddComponent<Image>();
        mainVisualImage.preserveAspect = true;
        mainVisualImage.raycastTarget = false;
        ui.mainVisualImage = mainVisualImage;
        ui.mainVisualGroup = mainVisualGroup;

        // 右: 情報パネル(役割/説明/星評価)。
        RectTransform infoRect = CreateOrnatePanel(rootGO.transform, "InfoPanel");
        infoRect.anchorMin = infoRect.anchorMax = new Vector2(1f, 0.5f);
        infoRect.pivot = new Vector2(1f, 0.5f);
        infoRect.sizeDelta = new Vector2(560f, 780f);
        infoRect.anchoredPosition = new Vector2(-64f, 30f);

        GameObject infoTitleGO = new GameObject("Title");
        infoTitleGO.transform.SetParent(infoRect, false);
        RectTransform infoTitleRect = infoTitleGO.AddComponent<RectTransform>();
        infoTitleRect.anchorMin = new Vector2(0f, 1f);
        infoTitleRect.anchorMax = new Vector2(1f, 1f);
        infoTitleRect.pivot = new Vector2(0.5f, 1f);
        infoTitleRect.sizeDelta = new Vector2(-80f, 52f);
        infoTitleRect.anchoredPosition = new Vector2(0f, -46f);
        Text infoTitleText = infoTitleGO.AddComponent<Text>();
        ConfigureCardText(infoTitleText, 38, FontStyle.Bold, new Color(1f, 0.85f, 0.4f));
        ui.titleText = infoTitleText;

        GameObject subtitleGO = new GameObject("Subtitle");
        subtitleGO.transform.SetParent(infoRect, false);
        RectTransform subtitleRect = subtitleGO.AddComponent<RectTransform>();
        subtitleRect.anchorMin = new Vector2(0f, 1f);
        subtitleRect.anchorMax = new Vector2(1f, 1f);
        subtitleRect.pivot = new Vector2(0.5f, 1f);
        subtitleRect.sizeDelta = new Vector2(-80f, 32f);
        subtitleRect.anchoredPosition = new Vector2(0f, -96f);
        Text subtitleText = subtitleGO.AddComponent<Text>();
        ConfigureCardText(subtitleText, 20, FontStyle.Italic, new Color(0.75f, 0.9f, 1f));
        ui.subtitleText = subtitleText;

        // Role Badge - 「見た目は強そうだが最弱」等の特殊枠は赤系(マスター
        // 指示どおり)、通常は紺系。RefreshDetailが色/文言を書き換える。
        GameObject roleBadgeGO = new GameObject("RoleBadge");
        roleBadgeGO.transform.SetParent(infoRect, false);
        RectTransform roleBadgeRect = roleBadgeGO.AddComponent<RectTransform>();
        roleBadgeRect.anchorMin = new Vector2(0.5f, 1f);
        roleBadgeRect.anchorMax = new Vector2(0.5f, 1f);
        roleBadgeRect.pivot = new Vector2(0.5f, 1f);
        roleBadgeRect.sizeDelta = new Vector2(320f, 46f);
        roleBadgeRect.anchoredPosition = new Vector2(0f, -140f);
        Image roleBadgeBg = roleBadgeGO.AddComponent<Image>();
        roleBadgeBg.sprite = RoundedPanelSprite();
        roleBadgeBg.type = Image.Type.Sliced;
        GameObject roleBadgeLabelGO = new GameObject("Label");
        roleBadgeLabelGO.transform.SetParent(roleBadgeGO.transform, false);
        StretchFull(roleBadgeLabelGO.AddComponent<RectTransform>());
        Text roleBadgeText = roleBadgeLabelGO.AddComponent<Text>();
        ConfigureCardText(roleBadgeText, 22, FontStyle.Bold, new Color(1f, 0.92f, 0.7f));
        ui.roleBadgeText = roleBadgeText;
        ui.roleBadgeBg = roleBadgeBg;

        // 「CHALLENGE HERO」の小さな追加バッジ(役割バッジと重ねて強調 -
        // マスター指示の「見た目は強そうだが実は最弱、であることが分かる
        // ように」)。
        GameObject challengeBadgeGO = new GameObject("ChallengeBadge");
        challengeBadgeGO.transform.SetParent(infoRect, false);
        RectTransform challengeBadgeRect = challengeBadgeGO.AddComponent<RectTransform>();
        challengeBadgeRect.anchorMin = new Vector2(0.5f, 1f);
        challengeBadgeRect.anchorMax = new Vector2(0.5f, 1f);
        challengeBadgeRect.pivot = new Vector2(0.5f, 1f);
        challengeBadgeRect.sizeDelta = new Vector2(320f, 24f);
        challengeBadgeRect.anchoredPosition = new Vector2(0f, -188f);
        Text challengeBadgeText = challengeBadgeGO.AddComponent<Text>();
        ConfigureCardText(challengeBadgeText, 15, FontStyle.Italic, new Color(1f, 0.55f, 0.5f));
        challengeBadgeText.text = "Strong in appearance. Weak in truth.";
        challengeBadgeGO.SetActive(false);
        ui.challengeBadge = challengeBadgeGO;

        // 説明文。
        GameObject flavorGO = new GameObject("FlavorText");
        flavorGO.transform.SetParent(infoRect, false);
        RectTransform flavorRect = flavorGO.AddComponent<RectTransform>();
        flavorRect.anchorMin = new Vector2(0f, 1f);
        flavorRect.anchorMax = new Vector2(1f, 1f);
        flavorRect.pivot = new Vector2(0.5f, 1f);
        flavorRect.sizeDelta = new Vector2(-80f, 170f);
        flavorRect.anchoredPosition = new Vector2(0f, -230f);
        Text flavorText = flavorGO.AddComponent<Text>();
        ConfigureCardText(flavorText, 22, FontStyle.Normal, new Color(0.92f, 0.93f, 0.97f));
        flavorText.alignment = TextAnchor.UpperLeft;
        ui.flavorText = flavorText;

        // 星評価4行(LIFE/POWER/SPEED/COMBO) - 内部戦闘値ではなく表示専用
        // (マスター指示どおり、CharacterDefinitionの説明コメント参照)。
        string[] statLabels = { "LIFE", "POWER", "SPEED", "COMBO" };
        Text[] statTexts = new Text[statLabels.Length];
        float statStartY = -420f;
        float statRowHeight = 56f;
        for (int s = 0; s < statLabels.Length; s++)
        {
            GameObject rowGO = new GameObject("Stat_" + statLabels[s]);
            rowGO.transform.SetParent(infoRect, false);
            RectTransform rowRect = rowGO.AddComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.sizeDelta = new Vector2(-80f, statRowHeight);
            rowRect.anchoredPosition = new Vector2(0f, statStartY - s * statRowHeight);

            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(rowGO.transform, false);
            RectTransform labelRect = labelGO.AddComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(0.4f, 1f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            Text labelText = labelGO.AddComponent<Text>();
            ConfigureCardText(labelText, 22, FontStyle.Bold, new Color(0.85f, 0.88f, 0.95f));
            labelText.alignment = TextAnchor.MiddleLeft;
            labelText.text = statLabels[s];

            GameObject starsGO = new GameObject("Stars");
            starsGO.transform.SetParent(rowGO.transform, false);
            RectTransform starsRect = starsGO.AddComponent<RectTransform>();
            starsRect.anchorMin = new Vector2(0.4f, 0f);
            starsRect.anchorMax = new Vector2(1f, 1f);
            starsRect.offsetMin = Vector2.zero;
            starsRect.offsetMax = Vector2.zero;
            Text starsText = starsGO.AddComponent<Text>();
            ConfigureCardText(starsText, 26, FontStyle.Bold, new Color(1f, 0.85f, 0.4f));
            starsText.alignment = TextAnchor.MiddleRight;
            statTexts[s] = starsText;
        }
        ui.lifeStarsText = statTexts[0];
        ui.powerStarsText = statTexts[1];
        ui.speedStarsText = statTexts[2];
        ui.comboStarsText = statTexts[3];

        rootGO.SetActive(false);
        return ui;
    }

    // ステージ選択導線追加(2026-09-12) - CharacterSelectと違い中央の大きな
    // メインビジュアル/右側の詳細情報パネルは持たない、マスター指示
    // 「ヴァンサバ系のように、サムネ・名前・特徴だけの簡易表示」どおりの
    // より単純な構成 - 各カード自体に名前・特徴テキストを直接焼き込む。
    static StageSelectUI BuildStageSelectCanvas()
    {
        GameObject canvasGO = new GameObject("StageSelectCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90; // DeckEdit/CardFusion/CharacterSelectと同じ帯 - 同時に開くことはない(GameManager.AnyOverlayOpen)

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<StandaloneInputModule>();
        }

        GameObject rootGO = new GameObject("StageSelectRoot");
        rootGO.transform.SetParent(canvasGO.transform, false);
        RectTransform rootRect = rootGO.AddComponent<RectTransform>();
        StretchFull(rootRect);
        CanvasGroup rootGroup = rootGO.AddComponent<CanvasGroup>();

        StageSelectUI ui = rootGO.AddComponent<StageSelectUI>();
        ui.root = rootRect;
        ui.rootGroup = rootGroup;

        // 背景 - CharacterSelect/DeckEdit/CardFusionと同じ単色塗り(濃紺)。
        // 既存アセットの使い回しによるインポート設定汚染リスク(過去に
        // BuildCharacterSelectCanvasで自己発見・修正済みの副作用)を避ける
        // ため、ここでも新規/共有アセットは一切読み込まない。
        GameObject bgGO = new GameObject("Background");
        bgGO.transform.SetParent(rootGO.transform, false);
        RectTransform bgRect = bgGO.AddComponent<RectTransform>();
        StretchFull(bgRect);
        Image bgImage = bgGO.AddComponent<Image>();
        bgImage.color = new Color(0.04f, 0.05f, 0.1f, 0.97f);
        bgImage.raycastTarget = false;

        GameObject headerGO = new GameObject("HeaderTitle");
        headerGO.transform.SetParent(rootGO.transform, false);
        RectTransform headerRect = headerGO.AddComponent<RectTransform>();
        headerRect.anchorMin = headerRect.anchorMax = new Vector2(0.5f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.sizeDelta = new Vector2(760f, 64f);
        headerRect.anchoredPosition = new Vector2(0f, -36f);
        Text headerText = headerGO.AddComponent<Text>();
        ConfigureCardText(headerText, 44, FontStyle.Bold, new Color(1f, 0.85f, 0.4f));
        headerText.alignment = TextAnchor.MiddleCenter;
        headerText.text = "STAGE SELECT";

        // BACK(左下) - 選択を確定せずHomeへ戻る(StageSelectUI.Close参照)。
        RectTransform backRect = CreateOrnatePanel(rootGO.transform, "BackButton", borderScale: 2f);
        backRect.anchorMin = backRect.anchorMax = new Vector2(0f, 0f);
        backRect.pivot = new Vector2(0f, 0f);
        backRect.sizeDelta = new Vector2(220f, 76f);
        backRect.anchoredPosition = new Vector2(56f, 40f);
        GameObject backLabelGO = new GameObject("Label");
        backLabelGO.transform.SetParent(backRect, false);
        StretchFull(backLabelGO.AddComponent<RectTransform>());
        Text backLabel = backLabelGO.AddComponent<Text>();
        ConfigureCardText(backLabel, 28, FontStyle.Bold, new Color(0.9f, 0.92f, 0.97f));
        backLabel.text = "« BACK";
        ui.backButtonRect = backRect;

        // 出発(中央下) - 参考画像どおり中央配置。選択を確定してHomeへ戻る
        // だけで、Run開始そのものはHome側の既存Doorホットスポット
        // (OnDoorTapped)が行う(StageSelectUI.Confirmのコメント参照)。
        RectTransform departRect = CreateOrnatePanel(rootGO.transform, "DepartButton", borderScale: 2f);
        departRect.anchorMin = departRect.anchorMax = new Vector2(0.5f, 0f);
        departRect.pivot = new Vector2(0.5f, 0f);
        departRect.sizeDelta = new Vector2(280f, 76f);
        departRect.anchoredPosition = new Vector2(0f, 40f);
        GameObject departLabelGO = new GameObject("Label");
        departLabelGO.transform.SetParent(departRect, false);
        StretchFull(departLabelGO.AddComponent<RectTransform>());
        Text departLabel = departLabelGO.AddComponent<Text>();
        ConfigureCardText(departLabel, 30, FontStyle.Bold, new Color(1f, 0.9f, 0.5f));
        departLabel.text = "出発";
        ui.departButtonRect = departRect;

        // ステージカード一覧 - StageDatabase.AllStagesの件数ぶん動的に生成
        // (将来ステージが増えてもここは変更不要)。
        var allStages = StageDatabase.AllStages;
        const float cardWidth = 380f;
        const float cardHeight = 560f;
        const float cardSpacing = 40f;
        float totalWidth = allStages.Count * cardWidth + Mathf.Max(0, allStages.Count - 1) * cardSpacing;
        float startX = -totalWidth / 2f;

        var cardSlotRects = new RectTransform[allStages.Count];
        var cardGlowImages = new Image[allStages.Count];
        var cardUnlocked = new bool[allStages.Count];

        for (int i = 0; i < allStages.Count; i++)
        {
            StageDefinition def = allStages[i];

            RectTransform cardRect = CreateOrnatePanel(rootGO.transform, "StageCard_" + def.stageId);
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(cardWidth, cardHeight);
            cardRect.anchoredPosition = new Vector2(startX + cardWidth / 2f + i * (cardWidth + cardSpacing), 20f);

            // 選択中の縁の発光 - CharacterSelectUIのSelectionGlowと同じ
            // 「一回り大きい丸角パネルを背後に重ね、選択中だけ表示する」
            // 方式。CreateOrnatePanelは既にFill/Frameの2子を持つため、
            // SetSiblingIndex(0)でその手前(=描画上は最背面)へ回し、Fill/
            // Frameの外周からわずかにはみ出すリングとして見せる。
            GameObject glowGO = new GameObject("SelectionGlow");
            glowGO.transform.SetParent(cardRect, false);
            RectTransform glowRect = glowGO.AddComponent<RectTransform>();
            glowRect.anchorMin = Vector2.zero;
            glowRect.anchorMax = Vector2.one;
            glowRect.offsetMin = new Vector2(-12f, -12f);
            glowRect.offsetMax = new Vector2(12f, 12f);
            Image glowImage = glowGO.AddComponent<Image>();
            glowImage.sprite = RoundedPanelSprite();
            glowImage.type = Image.Type.Sliced;
            glowImage.color = new Color(1f, 0.85f, 0.4f, 0.95f);
            glowImage.raycastTarget = false;
            glowGO.transform.SetSiblingIndex(0);
            glowGO.SetActive(false);

            // サムネ領域 - 専用画像が無い間は単色パネルへフォールバック
            // (StageDefinition.thumbnailのコメント、マスター指示「難しけれ
            // ばステージ名のみでも可」に対応)。
            GameObject thumbGO = new GameObject("Thumbnail");
            thumbGO.transform.SetParent(cardRect, false);
            RectTransform thumbRect = thumbGO.AddComponent<RectTransform>();
            thumbRect.anchorMin = new Vector2(0f, 1f);
            thumbRect.anchorMax = new Vector2(1f, 1f);
            thumbRect.pivot = new Vector2(0.5f, 1f);
            thumbRect.sizeDelta = new Vector2(-40f, 220f);
            thumbRect.anchoredPosition = new Vector2(0f, -30f);
            Image thumbImage = thumbGO.AddComponent<Image>();
            if (def.thumbnail != null)
            {
                thumbImage.sprite = ToUiSprite(def.thumbnail);
                thumbImage.preserveAspect = true;
            }
            else
            {
                thumbImage.sprite = RoundedPanelSprite();
                thumbImage.type = Image.Type.Sliced;
                thumbImage.color = def.unlocked ? new Color(0.16f, 0.22f, 0.35f, 1f) : new Color(0.12f, 0.12f, 0.14f, 1f);
            }
            thumbImage.raycastTarget = false;

            GameObject nameGO = new GameObject("Name");
            nameGO.transform.SetParent(cardRect, false);
            RectTransform nameRect = nameGO.AddComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0.5f, 1f);
            nameRect.sizeDelta = new Vector2(-40f, 48f);
            nameRect.anchoredPosition = new Vector2(0f, -266f);
            Text nameText = nameGO.AddComponent<Text>();
            ConfigureCardText(nameText, 30, FontStyle.Bold, def.unlocked ? new Color(1f, 0.9f, 0.6f) : new Color(0.55f, 0.55f, 0.58f));
            nameText.alignment = TextAnchor.MiddleCenter;
            nameText.text = def.displayName;

            // 特徴テキスト3行(敵/障害物/ルート) - マスター指示「サムネ・
            // 名前・特徴だけの簡易表示」どおり最小限、長文説明は入れない。
            GameObject featGO = new GameObject("FeatureText");
            featGO.transform.SetParent(cardRect, false);
            RectTransform featRect = featGO.AddComponent<RectTransform>();
            featRect.anchorMin = new Vector2(0f, 1f);
            featRect.anchorMax = new Vector2(1f, 1f);
            featRect.pivot = new Vector2(0.5f, 1f);
            featRect.sizeDelta = new Vector2(-40f, 190f);
            featRect.anchoredPosition = new Vector2(0f, -320f);
            Text featText = featGO.AddComponent<Text>();
            ConfigureCardText(featText, 18, FontStyle.Normal, def.unlocked ? new Color(0.88f, 0.9f, 0.95f) : new Color(0.5f, 0.5f, 0.53f));
            featText.alignment = TextAnchor.UpperLeft;
            featText.text = $"{def.enemyText}\n{def.featureText}\n{def.routeText}";

            // Lockアイコン代替 - 専用アート未用意のためテキスト表示(マス
            // ター指示「Lock icon」の簡易代用、未開放が伝われば機能面は
            // 十分)。
            GameObject lockGO = new GameObject("LockLabel");
            lockGO.transform.SetParent(cardRect, false);
            RectTransform lockRect = lockGO.AddComponent<RectTransform>();
            lockRect.anchorMin = new Vector2(0f, 1f);
            lockRect.anchorMax = new Vector2(1f, 1f);
            lockRect.pivot = new Vector2(0.5f, 1f);
            lockRect.sizeDelta = new Vector2(-40f, 100f);
            lockRect.anchoredPosition = new Vector2(0f, -110f);
            Text lockText = lockGO.AddComponent<Text>();
            ConfigureCardText(lockText, 40, FontStyle.Bold, new Color(0.8f, 0.8f, 0.82f, 0.9f));
            lockText.alignment = TextAnchor.MiddleCenter;
            lockText.text = "LOCKED";
            lockGO.SetActive(!def.unlocked);

            cardSlotRects[i] = cardRect;
            cardGlowImages[i] = glowImage;
            cardUnlocked[i] = def.unlocked;
        }
        ui.cardSlotRects = cardSlotRects;
        ui.cardGlowImages = cardGlowImages;
        ui.cardUnlocked = cardUnlocked;

        rootGO.SetActive(false);
        return ui;
    }

    // CharacterDefinition.portrait/mainVisualはTexture2D(RewardCardData.
    // Iconと同じ「表示側でSprite.Createする」方式 - CharacterDefinitionの
    // 説明コメント参照)なので、Editor側でuGUI Imageへ割り当てる際も同じ
    // 変換をここで行う。
    static Sprite ToUiSprite(Texture2D tex)
    {
        if (tex == null) return null;
        return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
    }

    // "LABEL n / m" count readout sitting under one Deck Edit panel -
    // shared by both COLLECTION (left, centerX=-500) and DECK (right,
    // centerX=500). DeckEditUI.Refresh() sets the actual text each time.
    // DeckPanelCountY (below) sits directly below BOTH panels' scroll/grid -
    // the same Y works for COLLECTION and DECK even though only DECK also
    // reserves an action-button row above this, because that row's height
    // is already folded into DeckPanelActionY/DeckPanelCountY's derivation.
    static Text CreateDeckCountText(Transform parent, float centerX)
    {
        GameObject countGO = new GameObject("CountText");
        countGO.transform.SetParent(parent, false);
        RectTransform countRect = countGO.AddComponent<RectTransform>();
        countRect.anchorMin = new Vector2(0.5f, 1f);
        countRect.anchorMax = new Vector2(0.5f, 1f);
        countRect.pivot = new Vector2(0.5f, 1f);
        countRect.sizeDelta = new Vector2(400f, DeckPanelCountHeight);
        countRect.anchoredPosition = new Vector2(centerX, DeckPanelCountY);
        Text countText = countGO.AddComponent<Text>();
        ConfigureCardText(countText, 22, FontStyle.Bold, new Color(1f, 0.85f, 0.4f));
        return countText;
    }

    // Builds one Deck Edit panel: a header label plus a scrolling
    // GridLayoutGroup grid beneath it, centered at `centerX` (so -500/+500
    // gives a left/right pair within the 1920-wide reference canvas).
    // Returns the ScrollRect itself (its .content is where the caller
    // parents cards under - CreateRewardCard's own placeholder size/
    // position gets overridden by GridLayoutGroup on the next layout pass
    // regardless; its .viewport is what DeckEditUI's manual drag-scroll
    // hit-tests against and scrolls, see DeckEditUI.Update).
    // Shared layout constants for the COLLECTION/DECK panels - a panel is
    // "Frame" (CreateOrnatePanel's decorative border) plus a ContentRoot
    // laid out entirely within DeckPanelContentPad of every edge, so
    // nothing (header text, filter tabs, cards) ever sits under the
    // frame's painted border. DeckPanelContentPad is comfortably larger
    // than the ~31-unit on-screen border OrnatePanelPixelsPerUnit produces
    // (see CreateOrnatePanel), not just equal to it - deliberate breathing
    // room, not a bare minimum.
    const float DeckPanelHeight = 870f;
    const float DeckPanelContentPad = 45f;
    const float DeckPanelHeaderHeight = 50f;
    const float DeckPanelFilterHeight = 34f;
    const float DeckPanelGap = 12f;
    const float DeckPanelActionHeight = 50f;
    const float DeckPanelCountHeight = 36f;

    // Derived Y positions (top-pivoted, relative to the same origin the
    // panel itself uses, -100) - every other panel-content Y in this file
    // is one of these so header/filter/action/count row heights can change
    // without hand-recomputing anything downstream.
    const float DeckPanelHeaderY = -100f - DeckPanelContentPad;
    const float DeckPanelFilterY = DeckPanelHeaderY - DeckPanelHeaderHeight - DeckPanelGap;
    const float DeckPanelContentBottom = -100f - DeckPanelHeight + DeckPanelContentPad;
    const float DeckPanelCountY = DeckPanelContentBottom + DeckPanelCountHeight;
    const float DeckPanelActionY = DeckPanelCountY + DeckPanelGap + DeckPanelActionHeight;

    static ScrollRect BuildDeckPanel(Transform parent, string headerText, float centerX, float panelWidth, float cardWidth, float cardHeight, int columns)
    {
        float innerWidth = panelWidth - DeckPanelContentPad * 2f;

        // Navy+gold region panel behind the header+grid, sized to enclose
        // both with some margin - gives COLLECTION/DECK each a clearly
        // bounded area instead of just floating text and cards over the
        // plain screen backdrop. Created first so it renders behind
        // everything else built below (uGUI draws in sibling order).
        RectTransform panelRect = CreateOrnatePanel(parent, "Panel_" + headerText);
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.sizeDelta = new Vector2(panelWidth, DeckPanelHeight);
        panelRect.anchoredPosition = new Vector2(centerX, -100f);

        // Y positions here start well below the back button (which occupies
        // roughly y=-30..-100 in the top-left corner) even for the left
        // panel, whose header would otherwise sit directly behind it, AND
        // below the panel's own top border (DeckPanelContentPad). A fixed
        // gap is always left between the header and the scroll grid (even
        // on the DECK panel, which has no filter tabs of its own) so both
        // panels' card grids start at the same Y and read as one aligned
        // row - see BuildCategoryFilterTabs, called separately by
        // BuildDeckEditCanvas only for the COLLECTION panel.
        GameObject headerGO = new GameObject("Header_" + headerText);
        headerGO.transform.SetParent(parent, false);
        RectTransform headerRect = headerGO.AddComponent<RectTransform>();
        headerRect.anchorMin = headerRect.anchorMax = new Vector2(0.5f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.sizeDelta = new Vector2(innerWidth, DeckPanelHeaderHeight);
        headerRect.anchoredPosition = new Vector2(centerX, DeckPanelHeaderY);
        Text headerLabel = headerGO.AddComponent<Text>();
        ConfigureCardText(headerLabel, 26, FontStyle.Bold, new Color(1f, 0.85f, 0.4f));
        headerLabel.text = headerText;

        // Always reserves the filter row's height below the header (even
        // for DECK, which has no filter tabs of its own - see above) so
        // the scroll/grid always starts at the same Y regardless.
        float scrollY = DeckPanelFilterY - DeckPanelFilterHeight - DeckPanelGap;
        // DECK additionally reserves its action-button row (see
        // BuildDeckActionButtons) above the count text row (see
        // CreateDeckCountText) that both panels reserve below the scroll -
        // the scroll itself just fills whatever's left down to whichever
        // of those actually starts first, so bumping either height never
        // needs a matching hand-recomputed scroll height here.
        float reservedTop = headerText == "DECK" ? DeckPanelActionY : DeckPanelCountY;
        float scrollHeight = scrollY - (reservedTop + DeckPanelGap);

        GameObject scrollGO = new GameObject("Scroll_" + headerText);
        scrollGO.transform.SetParent(parent, false);
        RectTransform scrollRect = scrollGO.AddComponent<RectTransform>();
        scrollRect.anchorMin = scrollRect.anchorMax = new Vector2(0.5f, 1f);
        scrollRect.pivot = new Vector2(0.5f, 1f);
        scrollRect.sizeDelta = new Vector2(innerWidth, scrollHeight);
        scrollRect.anchoredPosition = new Vector2(centerX, scrollY);
        ScrollRect scroll = scrollGO.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scrollGO.AddComponent<RectMask2D>();

        GameObject contentGO = new GameObject("Content");
        contentGO.transform.SetParent(scrollGO.transform, false);
        RectTransform contentRect = contentGO.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.sizeDelta = Vector2.zero;
        scroll.content = contentRect;
        scroll.viewport = scrollRect;

        GridLayoutGroup grid = contentGO.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(cardWidth, cardHeight);
        grid.spacing = new Vector2(22f, 20f);
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        ContentSizeFitter fitter = contentGO.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Card grids must always open scrolled to the top (never mid-scroll
        // from a stale layout pass) - forced again defensively in
        // DeckEditUI.Open() every time the screen is (re)opened, but also
        // set here so it's correct even before that ever runs once.
        scroll.verticalNormalizedPosition = 1f;

        return scroll;
    }

    // ALL/ATTACK/DEFENSE/SUPPORT/SPECIAL - the category filter row under
    // COLLECTION's header (see the reference mockup's "empty DECK screen"
    // sheet). DeckEditUI maps these 5 buckets onto the existing
    // CardCategory enum itself (see DeckEditUI.MatchesFilter) rather than
    // this method knowing anything about card data - it just builds the
    // buttons and hands their Rects/backgrounds/labels back for DeckEditUI
    // to hit-test (raw-touch, same as every other tap on this screen) and
    // highlight. Only called for the COLLECTION panel; DECK has no filter
    // row of its own.
    static readonly string[] CategoryFilterNames = { "ALL", "ATTACK", "DEFENSE", "SUPPORT", "SPECIAL" };

    static void BuildCategoryFilterTabs(Transform parent, float centerX, float innerWidth, out RectTransform[] rects, out Image[] backgrounds, out Text[] labels)
    {
        int count = CategoryFilterNames.Length;
        rects = new RectTransform[count];
        backgrounds = new Image[count];
        labels = new Text[count];

        const float spacing = 6f;
        float tabWidth = (innerWidth - spacing * (count - 1)) / count;

        for (int i = 0; i < count; i++)
        {
            GameObject tabGO = new GameObject("Filter_" + CategoryFilterNames[i]);
            tabGO.transform.SetParent(parent, false);
            RectTransform tabRect = tabGO.AddComponent<RectTransform>();
            tabRect.anchorMin = tabRect.anchorMax = new Vector2(0.5f, 1f);
            tabRect.pivot = new Vector2(0f, 1f);
            tabRect.sizeDelta = new Vector2(tabWidth, DeckPanelFilterHeight);
            tabRect.anchoredPosition = new Vector2(centerX - innerWidth / 2f + i * (tabWidth + spacing), DeckPanelFilterY);

            Image bg = tabGO.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.09f, 0.16f, 0.85f);
            bg.raycastTarget = false;

            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(tabGO.transform, false);
            StretchFull(labelGO.AddComponent<RectTransform>());
            Text label = labelGO.AddComponent<Text>();
            ConfigureCardText(label, 14, FontStyle.Bold, Color.white);
            label.text = CategoryFilterNames[i];

            rects[i] = tabRect;
            backgrounds[i] = bg;
            labels[i] = label;
        }
    }

    // "おすすめ編成" / "全て外す" - side by side under the DECK panel's card
    // grid (see the reference mockup), sitting in the gap between the
    // scroll region's bottom and the "DECK n / 10" count text below it.
    // Raw-touch hit-tested by DeckEditUI like every other tap on this
    // screen, not Button.onClick - flat colored buttons (not the ornate
    // frame) since they're secondary actions, not hero CTAs.
    static void BuildDeckActionButtons(Transform parent, float centerX, float innerWidth, out RectTransform recommendRect, out RectTransform clearRect)
    {
        const float y = DeckPanelActionY;
        const float height = DeckPanelActionHeight;
        const float gap = 12f;
        float buttonWidth = (innerWidth - gap) / 2f;

        GameObject recommendGO = new GameObject("RecommendButton");
        recommendGO.transform.SetParent(parent, false);
        recommendRect = recommendGO.AddComponent<RectTransform>();
        recommendRect.anchorMin = recommendRect.anchorMax = new Vector2(0.5f, 1f);
        recommendRect.pivot = new Vector2(0f, 1f);
        recommendRect.sizeDelta = new Vector2(buttonWidth, height);
        recommendRect.anchoredPosition = new Vector2(centerX - innerWidth / 2f, y);
        Image recommendBg = recommendGO.AddComponent<Image>();
        recommendBg.color = new Color(0.55f, 0.42f, 0.14f, 0.9f); // warm gold - a positive/constructive action
        recommendBg.raycastTarget = false;
        GameObject recommendLabelGO = new GameObject("Label");
        recommendLabelGO.transform.SetParent(recommendGO.transform, false);
        StretchFull(recommendLabelGO.AddComponent<RectTransform>());
        Text recommendLabel = recommendLabelGO.AddComponent<Text>();
        ConfigureCardText(recommendLabel, 16, FontStyle.Bold, new Color(1f, 0.93f, 0.75f));
        recommendLabel.text = "おすすめ編成";

        GameObject clearGO = new GameObject("ClearAllButton");
        clearGO.transform.SetParent(parent, false);
        clearRect = clearGO.AddComponent<RectTransform>();
        clearRect.anchorMin = clearRect.anchorMax = new Vector2(0.5f, 1f);
        clearRect.pivot = new Vector2(0f, 1f);
        clearRect.sizeDelta = new Vector2(buttonWidth, height);
        clearRect.anchoredPosition = new Vector2(centerX - innerWidth / 2f + buttonWidth + gap, y);
        Image clearBg = clearGO.AddComponent<Image>();
        clearBg.color = new Color(0.42f, 0.12f, 0.12f, 0.9f); // muted red - a destructive action
        clearBg.raycastTarget = false;
        GameObject clearLabelGO = new GameObject("Label");
        clearLabelGO.transform.SetParent(clearGO.transform, false);
        StretchFull(clearLabelGO.AddComponent<RectTransform>());
        Text clearLabel = clearLabelGO.AddComponent<Text>();
        ConfigureCardText(clearLabel, 16, FontStyle.Bold, Color.white);
        clearLabel.text = "全て外す";
    }

    // A small reusable yes/no confirm modal (see ConfirmDialogUI) - a
    // screen-covering dim backdrop plus one centered ornate panel with a
    // message and two buttons. Built once per Deck Edit canvas, parented
    // last under rootGO.transform so it renders above every panel/card
    // already built (uGUI draws by sibling order) - starts hidden.
    static ConfirmDialogUI BuildConfirmDialog(Transform parent)
    {
        GameObject rootGO = new GameObject("ConfirmDialog");
        rootGO.transform.SetParent(parent, false);
        RectTransform rootRect = rootGO.AddComponent<RectTransform>();
        StretchFull(rootRect);
        ConfirmDialogUI dialog = rootGO.AddComponent<ConfirmDialogUI>();
        dialog.root = rootGO;

        GameObject dimGO = new GameObject("Dim");
        dimGO.transform.SetParent(rootGO.transform, false);
        RectTransform dimRect = dimGO.AddComponent<RectTransform>();
        StretchFull(dimRect);
        Image dim = dimGO.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.6f);
        dim.raycastTarget = false;

        RectTransform panelRect = CreateOrnatePanel(rootGO.transform, "Panel");
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(560f, 280f);
        panelRect.anchoredPosition = Vector2.zero;

        GameObject messageGO = new GameObject("Message");
        messageGO.transform.SetParent(panelRect, false);
        RectTransform messageRect = messageGO.AddComponent<RectTransform>();
        messageRect.anchorMin = new Vector2(0f, 1f);
        messageRect.anchorMax = new Vector2(1f, 1f);
        messageRect.pivot = new Vector2(0.5f, 1f);
        messageRect.offsetMin = new Vector2(30f, 0f);
        messageRect.offsetMax = new Vector2(-30f, 0f);
        messageRect.sizeDelta = new Vector2(0f, 140f);
        messageRect.anchoredPosition = new Vector2(0f, -40f);
        Text messageText = messageGO.AddComponent<Text>();
        ConfigureCardText(messageText, 22, FontStyle.Normal, Color.white);
        dialog.messageText = messageText;

        const float buttonWidth = 220f;
        const float buttonHeight = 64f;
        const float buttonGap = 20f;

        GameObject yesGO = new GameObject("Yes");
        yesGO.transform.SetParent(panelRect, false);
        RectTransform yesRect = yesGO.AddComponent<RectTransform>();
        yesRect.anchorMin = yesRect.anchorMax = new Vector2(0.5f, 0f);
        yesRect.pivot = new Vector2(1f, 0f);
        yesRect.sizeDelta = new Vector2(buttonWidth, buttonHeight);
        yesRect.anchoredPosition = new Vector2(-buttonGap / 2f, 30f);
        Image yesBg = yesGO.AddComponent<Image>();
        yesBg.color = new Color(0.55f, 0.42f, 0.14f, 0.95f);
        yesBg.raycastTarget = false;
        GameObject yesLabelGO = new GameObject("Label");
        yesLabelGO.transform.SetParent(yesGO.transform, false);
        StretchFull(yesLabelGO.AddComponent<RectTransform>());
        Text yesLabel = yesLabelGO.AddComponent<Text>();
        ConfigureCardText(yesLabel, 20, FontStyle.Bold, new Color(1f, 0.93f, 0.75f));
        yesLabel.text = "はい";
        dialog.yesRect = yesRect;

        GameObject noGO = new GameObject("No");
        noGO.transform.SetParent(panelRect, false);
        RectTransform noRect = noGO.AddComponent<RectTransform>();
        noRect.anchorMin = noRect.anchorMax = new Vector2(0.5f, 0f);
        noRect.pivot = new Vector2(0f, 0f);
        noRect.sizeDelta = new Vector2(buttonWidth, buttonHeight);
        noRect.anchoredPosition = new Vector2(buttonGap / 2f, 30f);
        Image noBg = noGO.AddComponent<Image>();
        noBg.color = new Color(0.14f, 0.16f, 0.24f, 0.95f);
        noBg.raycastTarget = false;
        GameObject noLabelGO = new GameObject("Label");
        noLabelGO.transform.SetParent(noGO.transform, false);
        StretchFull(noLabelGO.AddComponent<RectTransform>());
        Text noLabel = noLabelGO.AddComponent<Text>();
        ConfigureCardText(noLabel, 20, FontStyle.Bold, Color.white);
        noLabel.text = "いいえ";
        dialog.noRect = noRect;

        rootGO.SetActive(false);
        return dialog;
    }

    // One reward card: back image, base art, icon, frame, title band, title,
    // level, count, (description/rarity - detail mode only), and a Button
    // covering the whole card for tap-to-select - the same structure every
    // time, only the content (set later via SetContent) differs, standing
    // in for a shared Prefab in a project where every object is built by
    // code. Card UI改修(2026-09-08) - overloaded to also accept the 2 new
    // common art pieces (baseSprite/titleBandSprite); every existing call
    // site is updated to pass them (see each BuildXxxCanvas method).
    static RewardCardUI CreateRewardCard(Transform parent, int index, float width, float height, Sprite backSprite, Sprite frameSprite, System.Action<int> onClick, Sprite baseSprite = null, Sprite titleBandSprite = null)
    {
        GameObject cardGO = new GameObject("RewardCard" + index);
        cardGO.transform.SetParent(parent, false);
        RectTransform rect = cardGO.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);

        CanvasGroup group = cardGO.AddComponent<CanvasGroup>();

        RewardCardUI card = cardGO.AddComponent<RewardCardUI>();
        card.rect = rect;
        card.canvasGroup = group;

        GameObject backGO = new GameObject("Back");
        backGO.transform.SetParent(cardGO.transform, false);
        StretchFull(backGO.AddComponent<RectTransform>());
        Image backImage = backGO.AddComponent<Image>();
        backImage.sprite = backSprite;
        backImage.raycastTarget = false;
        card.backImage = backImage;

        // Card UI改修(2026-09-08) - 新レイアウトの土台となる「カード下地」
        // (深い青の共通背景アート、全Rarity共通) - Backのすぐ上、Frameより
        // 下に配置。表向き時は常時表示、裏向き(Back)時は非表示。
        GameObject baseGO = new GameObject("Base");
        baseGO.transform.SetParent(cardGO.transform, false);
        StretchFull(baseGO.AddComponent<RectTransform>());
        Image baseImageComp = baseGO.AddComponent<Image>();
        baseImageComp.sprite = baseSprite;
        baseImageComp.raycastTarget = false;
        card.baseImage = baseImageComp;

        // カードUIデザイン提案(2026-09-09)反映 - 「01 イラスト重視: カード
        // の約65-70%をイラスト領域に」に合わせ、Icon/IconBackdropを大幅に
        // 拡大(旧: 高さ約41% → 新: 約67%)。タイトル帯を画面下端近くまで
        // 押し下げ、Lv表示はコンパクトなバッジ化(下記LevelBadge参照)、
        // カード表面の常時表示は「イラスト/タイトル/Lv」のみ(「04 情報を
        // 絞る」)という提案どおり。
        Color cardPanelColor = new Color(0.04f, 0.05f, 0.12f, 1f);
        GameObject iconBackdropGO = new GameObject("IconBackdrop");
        iconBackdropGO.transform.SetParent(cardGO.transform, false);
        RectTransform iconBackdropRect = iconBackdropGO.AddComponent<RectTransform>();
        iconBackdropRect.anchorMin = new Vector2(0.06f, 0.205f);
        iconBackdropRect.anchorMax = new Vector2(0.94f, 0.875f);
        iconBackdropRect.offsetMin = Vector2.zero;
        iconBackdropRect.offsetMax = Vector2.zero;
        Image iconBackdropImage = iconBackdropGO.AddComponent<Image>();
        iconBackdropImage.color = new Color(cardPanelColor.r, cardPanelColor.g, cardPanelColor.b, 0.55f);
        iconBackdropImage.raycastTarget = false;
        card.iconBackdrop = iconBackdropImage;

        // カードUIデザイン提案「不足パーツ - イラストマスク」- イラストを
        // 美しく見せる専用マスクフレーム画像はまだない(新規アート生成が
        // 必要、今回のパスの対象外)ため、代わりにIconBackdropの角丸/縁で
        // 簡易的に代替している(将来的に専用マスク画像が用意でき次第、
        // ここへ差し込む形にできる)。
        GameObject iconGO = new GameObject("Icon");
        iconGO.transform.SetParent(cardGO.transform, false);
        RectTransform iconRect = iconGO.AddComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0.08f, 0.22f);
        iconRect.anchorMax = new Vector2(0.92f, 0.86f);
        iconRect.offsetMin = Vector2.zero;
        iconRect.offsetMax = Vector2.zero;
        Image iconImage = iconGO.AddComponent<Image>();
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        card.iconImage = iconImage;

        // Card UI改修(2026-09-08) - Frameはこの位置(Icon/IconBackdropの
        // "後"、つまり描画順で"上")に移動。以前はBackの直後(Iconより下)
        // に置かれていたが、フレームの縁飾りが常にIcon/下地より手前に来る
        // よう仕様の描画順(1.下地 2.イラスト 3.フレーム 4.タイトル帯...)
        // に合わせた。フレーム自体は中央が透過(枠のみ不透明)なので、以前
        // の順序でも見た目上の破綻はなかったが、こちらがより正しい/安全。
        GameObject frameGO = new GameObject("Frame");
        frameGO.transform.SetParent(cardGO.transform, false);
        StretchFull(frameGO.AddComponent<RectTransform>());
        Image frameImage = frameGO.AddComponent<Image>();
        frameImage.sprite = frameSprite;
        frameImage.raycastTarget = false;
        // Card UI / Rarity Frame pass, item 8 - the 5 Rarity frame images
        // are NOT all the same aspect ratio (and this card's own
        // RectTransform size must never change per-Rarity), so the frame
        // Image fits within the card bounds instead of stretching to fill
        // it - keeps every Rarity's art undistorted regardless of which
        // differently-proportioned frame Sprite ends up swapped in here at
        // SetContent() time.
        frameImage.preserveAspect = true;
        card.frameImage = frameImage;
        card.defaultFrameSprite = frameSprite;

        // Card UI改修(2026-09-08) - 新共通素材「タイトル帯」。旧レイアウ
        // トのTitle領域をこのプレート画像で置き換え、その上にTitleText/
        // CountTextを重ねる(タイトル帯右端に所持枚数)。
        // カードUIデザイン提案(2026-09-09)反映 - 「02 タイトル帯」を画面
        // 下端近くまで押し下げ、上のイラスト領域を最大化。
        GameObject titleBandGO = new GameObject("TitleBand");
        titleBandGO.transform.SetParent(cardGO.transform, false);
        RectTransform titleBandRect = titleBandGO.AddComponent<RectTransform>();
        titleBandRect.anchorMin = new Vector2(0.03f, 0.025f);
        titleBandRect.anchorMax = new Vector2(0.97f, 0.195f);
        titleBandRect.offsetMin = Vector2.zero;
        titleBandRect.offsetMax = Vector2.zero;
        Image titleBandImageComp = titleBandGO.AddComponent<Image>();
        titleBandImageComp.sprite = titleBandSprite;
        titleBandImageComp.raycastTarget = false;
        // preserveAspect=false (stretch to fill) - the supplied banner art's
        // own native aspect (~2:1) is narrower than the width this slot
        // needs to span on a 2:3 card, so a preserveAspect fit would
        // letterbox it down to roughly half the card's width instead of
        // reading as a full-width title plate. A disclosed simplification
        // for this pass (mild horizontal stretch on the ornamental gems) -
        // a future pass could either 9-slice this art (fixed-size end caps,
        // stretchy middle) or source a wider-proportioned banner instead.
        titleBandImageComp.preserveAspect = false;
        card.titleBandImage = titleBandImageComp;

        // カードUIデザイン提案(2026-09-09)反映 - Descriptionはもうカード
        // 自身の専用スペースを持たず(タイトル帯が下端へ移動したため空き
        // がない)、showDetails時のみイラスト領域の下寄りにオーバーレイ
        // 表示する(Iconより後ろに置いているので描画順でIconの上に重なる)。
        // Collection/Deck(showDetails:false)では常に非表示のままなので、
        // イラストが隠れることはない。
        GameObject textBackdropGO = new GameObject("TextBackdrop");
        textBackdropGO.transform.SetParent(cardGO.transform, false);
        RectTransform textBackdropRect = textBackdropGO.AddComponent<RectTransform>();
        textBackdropRect.anchorMin = new Vector2(0.09f, 0.225f);
        textBackdropRect.anchorMax = new Vector2(0.91f, 0.40f);
        textBackdropRect.offsetMin = Vector2.zero;
        textBackdropRect.offsetMax = Vector2.zero;
        Image textBackdropImage = textBackdropGO.AddComponent<Image>();
        textBackdropImage.color = new Color(cardPanelColor.r, cardPanelColor.g, cardPanelColor.b, 0.85f);
        textBackdropImage.raycastTarget = false;
        card.textBackdrop = textBackdropImage;

        // Item 5 - イラスト領域の上寄りを横断するリボン状(小さいサイズで
        // も"EQUIPPED"が収まるよう全幅を使う、という既存方針は維持)。
        GameObject equippedGO = new GameObject("EquippedBadge");
        equippedGO.transform.SetParent(cardGO.transform, false);
        RectTransform equippedRect = equippedGO.AddComponent<RectTransform>();
        // LevelBadge(右上、常時表示)と重ならないよう、右端は0.66手前まで
        // (LevelBadgeの左端)に収める。
        equippedRect.anchorMin = new Vector2(0.10f, 0.795f);
        equippedRect.anchorMax = new Vector2(0.62f, 0.855f);
        equippedRect.offsetMin = Vector2.zero;
        equippedRect.offsetMax = Vector2.zero;
        Image equippedBg = equippedGO.AddComponent<Image>();
        equippedBg.color = new Color(0.55f, 0.42f, 0.14f, 0.95f);
        equippedBg.raycastTarget = false;
        GameObject equippedLabelGO = new GameObject("Label");
        equippedLabelGO.transform.SetParent(equippedGO.transform, false);
        StretchFull(equippedLabelGO.AddComponent<RectTransform>());
        Text equippedLabel = equippedLabelGO.AddComponent<Text>();
        ConfigureCardText(equippedLabel, Mathf.Max(8, DescFontSizeFor(width) - 2), FontStyle.Bold, new Color(1f, 0.93f, 0.75f));
        // Best Fit - a full-width strip is still only ~30px tall on the
        // smallest (Character Card) slots, so the text must be free to
        // shrink below its nominal size rather than clip ("EQUIPP" before
        // this bugfix).
        equippedLabel.resizeTextForBestFit = true;
        equippedLabel.resizeTextMinSize = 6;
        equippedLabel.resizeTextMaxSize = Mathf.Max(8, DescFontSizeFor(width) - 2);
        equippedLabel.text = "EQUIPPED";
        card.equippedBadge = equippedGO;
        equippedGO.SetActive(false);

        // Title text sits over the LEFT/CENTER portion of TitleBand -
        // CountText (below) claims the band's own right edge, so Title
        // never overlaps it.
        GameObject titleGO = new GameObject("Title");
        titleGO.transform.SetParent(cardGO.transform, false);
        RectTransform titleRect = titleGO.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.09f, 0.04f);
        titleRect.anchorMax = new Vector2(0.76f, 0.175f);
        titleRect.offsetMin = Vector2.zero;
        titleRect.offsetMax = Vector2.zero;
        Text titleText = titleGO.AddComponent<Text>();
        ConfigureCardText(titleText, TitleFontSizeFor(width), FontStyle.Bold, new Color(1f, 0.85f, 0.4f));
        titleText.resizeTextForBestFit = true;
        titleText.resizeTextMinSize = 8;
        titleText.resizeTextMaxSize = TitleFontSizeFor(width);
        card.titleText = titleText;

        // Card UI改修(2026-09-08) - 所持枚数「×N」、タイトル帯の右端に固
        // 定(所持枚数表示の位置を固定したい、という要望どおり)。
        GameObject countGO = new GameObject("Count");
        countGO.transform.SetParent(cardGO.transform, false);
        RectTransform countRect = countGO.AddComponent<RectTransform>();
        countRect.anchorMin = new Vector2(0.775f, 0.04f);
        countRect.anchorMax = new Vector2(0.95f, 0.175f);
        countRect.offsetMin = Vector2.zero;
        countRect.offsetMax = Vector2.zero;
        Text countTextComp = countGO.AddComponent<Text>();
        ConfigureCardText(countTextComp, Mathf.Max(8, DescFontSizeFor(width) - 1), FontStyle.Bold, new Color(0.75f, 0.9f, 1f));
        countTextComp.alignment = TextAnchor.MiddleRight;
        countTextComp.resizeTextForBestFit = true;
        countTextComp.resizeTextMinSize = 6;
        countTextComp.resizeTextMaxSize = Mathf.Max(8, DescFontSizeFor(width) - 1);
        card.countText = countTextComp;

        // カードUIデザイン提案(2026-09-09)反映 - TextBackdrop(上で移動
        // 済み)に合わせてイラスト下寄りのオーバーレイ位置へ。
        GameObject descGO = new GameObject("Description");
        descGO.transform.SetParent(cardGO.transform, false);
        RectTransform descRect = descGO.AddComponent<RectTransform>();
        descRect.anchorMin = new Vector2(0.11f, 0.24f);
        descRect.anchorMax = new Vector2(0.89f, 0.385f);
        descRect.offsetMin = Vector2.zero;
        descRect.offsetMax = Vector2.zero;
        Text descText = descGO.AddComponent<Text>();
        ConfigureCardText(descText, DescFontSizeFor(width), FontStyle.Normal, Color.white);
        descText.resizeTextForBestFit = true;
        descText.resizeTextMinSize = 6;
        descText.resizeTextMaxSize = DescFontSizeFor(width);
        card.descriptionText = descText;

        // カード選択UI再設計(2026-09-12第3弾) - 「カード本体には長い説明文
        // を詰め込まず、イラスト/タイトル/主要効果の短い表記のみ」。
        // IconBackdropの暗い下地(0.205〜0.875)の下端に重ねる形で、
        // TitleBand(0.025〜0.195)のすぐ上に短い1行(例: "HP +20%")を常時
        // 表示する。levelText/countTextと同じく、showDetailsの値に関係なく
        // データ(ValueLine)の有無だけで表示可否が決まる独立行(RewardCardUI.
        // ApplyFaceVisibility参照)。
        GameObject valueLineGO = new GameObject("ValueLine");
        valueLineGO.transform.SetParent(cardGO.transform, false);
        RectTransform valueLineRect = valueLineGO.AddComponent<RectTransform>();
        valueLineRect.anchorMin = new Vector2(0.09f, 0.205f);
        valueLineRect.anchorMax = new Vector2(0.91f, 0.30f);
        valueLineRect.offsetMin = Vector2.zero;
        valueLineRect.offsetMax = Vector2.zero;
        Text valueLineText = valueLineGO.AddComponent<Text>();
        ConfigureCardText(valueLineText, Mathf.Max(10, DescFontSizeFor(width) + 2), FontStyle.Bold, new Color(0.65f, 0.9f, 1f));
        valueLineText.resizeTextForBestFit = true;
        valueLineText.resizeTextMinSize = 8;
        valueLineText.resizeTextMaxSize = Mathf.Max(10, DescFontSizeFor(width) + 2);
        card.valueLineText = valueLineText;

        // Card UI / Rarity Frame pass, item 2 - Rarity (top-left, shown only
        // in showDetails mode now)。イラスト領域の上に直接乗る形になった
        // (カードUIデザイン提案でイラストが拡大されたため)- Levelと違い
        // 常時表示ではなくshowDetails限定のままなので、常設のバッジ背景は
        // 付けていない。
        GameObject rarityGO = new GameObject("Rarity");
        rarityGO.transform.SetParent(cardGO.transform, false);
        RectTransform rarityRect = rarityGO.AddComponent<RectTransform>();
        rarityRect.anchorMin = new Vector2(0.08f, 0.895f);
        rarityRect.anchorMax = new Vector2(0.42f, 0.965f);
        rarityRect.offsetMin = Vector2.zero;
        rarityRect.offsetMax = Vector2.zero;
        Text rarityText = rarityGO.AddComponent<Text>();
        ConfigureCardText(rarityText, Mathf.Max(9, DescFontSizeFor(width)), FontStyle.Bold, new Color(1f, 0.85f, 0.4f));
        rarityText.alignment = TextAnchor.MiddleLeft;
        rarityText.resizeTextForBestFit = true;
        rarityText.resizeTextMinSize = 6;
        rarityText.resizeTextMaxSize = Mathf.Max(9, DescFontSizeFor(width));
        card.rarityText = rarityText;

        // カードUIデザイン提案「03 Lv表示: 右上にコンパクトで上品なレベル
        // バッジを配置」「不足パーツ - レベルバッジ(汎用)」への対応。
        // 専用のバッジ画像はまだない(新規アート生成が必要、今回のパスの
        // 対象外)ため、既存のUI Spriteを45°回転させた菱形(ダイヤ)背景で
        // 簡易的に代替した - 他のUI(カードフレームの縁飾り等)と同系統の
        // 菱形モチーフなので、見た目の統一感は保てる。LevelBadge(親、無
        // 回転)の中にDiamond(回転)とLabel(無回転、テキストが斜めになら
        // ないようDiamondの子ではなく親の子として並列に置く)を分ける構成。
        GameObject levelGO = new GameObject("LevelBadge");
        levelGO.transform.SetParent(cardGO.transform, false);
        RectTransform levelRect = levelGO.AddComponent<RectTransform>();
        levelRect.anchorMin = new Vector2(0.66f, 0.785f);
        levelRect.anchorMax = new Vector2(0.945f, 0.975f);
        levelRect.offsetMin = Vector2.zero;
        levelRect.offsetMax = Vector2.zero;
        // 上記anchorMin/Maxは、カード比率(2:3固定、CardAspect参照)込みで
        // 実ピクセル換算するとほぼ正方形になるよう調整済み(幅0.285*width
        // ≈高さ0.19*height=0.19*1.5*width=0.285*width) - 回転させる菱形が
        // 縦にはみ出さないようにするための計算。

        GameObject levelDiamondGO = new GameObject("Diamond");
        levelDiamondGO.transform.SetParent(levelGO.transform, false);
        RectTransform levelDiamondRect = levelDiamondGO.AddComponent<RectTransform>();
        levelDiamondRect.anchorMin = new Vector2(0.5f, 0.5f);
        levelDiamondRect.anchorMax = new Vector2(0.5f, 0.5f);
        levelDiamondRect.pivot = new Vector2(0.5f, 0.5f);
        // 正方形を45°回転 - 親の矩形いっぱいに菱形が収まるよう、対角線
        // (=sqrt(2)倍)で計算した一辺の長さにする。
        levelDiamondRect.sizeDelta = new Vector2(width * 0.19f, width * 0.19f);
        levelDiamondRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Image levelDiamondImage = levelDiamondGO.AddComponent<Image>();
        levelDiamondImage.color = new Color(0.06f, 0.08f, 0.16f, 0.92f);
        levelDiamondImage.raycastTarget = false;
        GameObject levelDiamondBorderGO = new GameObject("Border");
        levelDiamondBorderGO.transform.SetParent(levelDiamondGO.transform, false);
        RectTransform levelDiamondBorderRect = levelDiamondBorderGO.AddComponent<RectTransform>();
        levelDiamondBorderRect.anchorMin = Vector2.zero;
        levelDiamondBorderRect.anchorMax = Vector2.one;
        levelDiamondBorderRect.offsetMin = new Vector2(-3f, -3f);
        levelDiamondBorderRect.offsetMax = new Vector2(3f, 3f);
        Image levelDiamondBorderImage = levelDiamondBorderGO.AddComponent<Image>();
        levelDiamondBorderImage.color = new Color(0.83f, 0.68f, 0.32f, 0.95f);
        levelDiamondBorderImage.raycastTarget = false;
        levelDiamondBorderGO.transform.SetAsFirstSibling(); // 縁取り(金)を内側の紺より後ろへ

        GameObject levelLabelGO = new GameObject("Label");
        levelLabelGO.transform.SetParent(levelGO.transform, false);
        StretchFull(levelLabelGO.AddComponent<RectTransform>());
        Text levelText = levelLabelGO.AddComponent<Text>();
        ConfigureCardText(levelText, Mathf.Max(8, DescFontSizeFor(width) - 1), FontStyle.Bold, new Color(1f, 0.92f, 0.7f));
        levelText.alignment = TextAnchor.MiddleCenter;
        levelText.resizeTextForBestFit = true;
        levelText.resizeTextMinSize = 6;
        levelText.resizeTextMaxSize = Mathf.Max(8, DescFontSizeFor(width) - 1);
        card.levelText = levelText;

        // Invisible full-card button purely for tap-to-select - its own
        // Image target graphic is the frame (already drawn above), not a
        // separate visible box.
        Button button = cardGO.AddComponent<Button>();
        button.targetGraphic = frameImage;
        button.transition = Selectable.Transition.None; // no built-in tint/flash on hover or press
        int capturedIndex = index;
        // onClick may be null (DeckEditUI's cards) - that screen hit-tests
        // taps itself instead, see DeckEditUI's class comment for why.
        if (onClick != null) button.onClick.AddListener(() => onClick(capturedIndex));
        card.button = button;

        card.ShowBack();
        card.SetInteractable(false);
        return card;
    }

    // カード選択UI再設計(2026-09-12第3弾) - 「前回の横長3段リスト形式は
    // 今回は使用せず」との明示的な指示によりCreateLevelUpChoiceRow(横長
    // 1行ぶんの選択肢)は廃止し、DetailPanel(BuildRewardCardCanvas内、
    // RoundedPanelSpriteを共有)へ置き換えた。LevelUpChoiceRowUI.cs自体も
    // 削除済み(他に参照箇所なし)。

    // DetailPanel(BuildRewardCardCanvas)が使う、丸角パネル用の1枚の
    // 9-sliceスプライト(白塗り、実際の色はImage.colorで着色 - Edge/
    // Backgroundの2枚で共有する)。CreateRadialGlowSprite等と同じ「手続き
    // 的にテクスチャを生成する」パターンを踏襲、新規アート不要。
    static Sprite roundedPanelSpriteCache;
    static Sprite RoundedPanelSprite()
    {
        if (roundedPanelSpriteCache != null) return roundedPanelSpriteCache;

        const int size = 128;
        const int radius = 22;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool inside = true;
                if (x < radius && y < radius) inside = Vector2.Distance(new Vector2(x, y), new Vector2(radius, radius)) <= radius;
                else if (x >= size - radius && y < radius) inside = Vector2.Distance(new Vector2(x, y), new Vector2(size - radius, radius)) <= radius;
                else if (x < radius && y >= size - radius) inside = Vector2.Distance(new Vector2(x, y), new Vector2(radius, size - radius)) <= radius;
                else if (x >= size - radius && y >= size - radius) inside = Vector2.Distance(new Vector2(x, y), new Vector2(size - radius, size - radius)) <= radius;
                pixels[y * size + x] = inside ? Color.white : new Color(1f, 1f, 1f, 0f);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();

        roundedPanelSpriteCache = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        return roundedPanelSpriteCache;
    }

    // Title/description font sizes scale with card width instead of being
    // fixed - they were hardcoded to the reward-card sequence's 260-wide
    // cards' sizes (30/22) regardless of actual card width, so the Deck
    // Edit screen's much smaller 130-wide cards ended up with severely
    // oversized text that the title/description boxes clipped down to
    // nothing readable. 260f is the reward-card sequence's own cardWidth,
    // kept as the scale's reference point so those cards render identically
    // to before.
    const float ReferenceCardWidth = 260f;

    static int TitleFontSizeFor(float width) => Mathf.Max(10, Mathf.RoundToInt(width * (30f / ReferenceCardWidth)));
    static int DescFontSizeFor(float width) => Mathf.Max(9, Mathf.RoundToInt(width * (22f / ReferenceCardWidth)));

    // Card UI改修(2026-09-08) - 「全カードの基準サイズを統一(512x768、縦
    // 長2:3)」。従来はカードのRectTransform自体の縦横比がcardFrameSprite
    // (Rarity 1のフォールバック用に読み込んでいた古いCardFrame.png)の
    // 実ピクセル比にそのまま連動していた(cardHeight = cardWidth *
    // (frameSprite.rect.height / width))ため、フォールバック画像を差し替
    // えるたびにカード全体の比率が意図せず変わりうる脆い設計だった。今回
    // 全画面で512x768=2:3に統一するにあたり、Spriteの実ピクセル比からは
    // 完全に切り離した固定定数に変更。
    const float CardAspect = 1.5f; // 768 / 512

    static void ConfigureCardText(Text text, int fontSize, FontStyle style, Color color)
    {
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
    }

    static void StretchFull(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    // Visual Style Ver.1 panel: a navy semi-transparent fill with a thin
    // gold edge, for uGUI screens (Deck Edit) - the uGUI equivalent of
    // UiBackdrop's IMGUI treatment used everywhere else, built from two
    // stacked Images (gold behind, an inset navy fill on top) since uGUI
    // has no built-in bordered-box primitive. Returns the outer
    // RectTransform for the caller to position/size.
    static RectTransform CreateNavyGoldPanel(Transform parent, string name, float borderThickness = 2.5f)
    {
        GameObject goldGO = new GameObject(name);
        goldGO.transform.SetParent(parent, false);
        RectTransform goldRect = goldGO.AddComponent<RectTransform>();
        Image gold = goldGO.AddComponent<Image>();
        gold.color = new Color(0.83f, 0.68f, 0.32f, 0.9f);
        gold.raycastTarget = false;

        GameObject fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(goldGO.transform, false);
        RectTransform fillRect = fillGO.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(borderThickness, borderThickness);
        fillRect.offsetMax = new Vector2(-borderThickness, -borderThickness);
        Image fill = fillGO.AddComponent<Image>();
        fill.color = new Color(0.06f, 0.08f, 0.17f, 0.85f);
        fill.raycastTarget = false;

        return goldRect;
    }

    // Imports Assets/Art/UI/OrnateFrame.png (generated to match CardFrame.png/
    // CardBack.png's art direction) as a Sliced Sprite - a decorative navy+
    // gold+blue-accent ring with a fully transparent interior, authored at
    // 1536x1024 with a uniform 280px border.
    //
    // spritePixelsPerUnit is the critical setting here: left at its default
    // (100), a 280px border renders as a ~280-CANVAS-UNIT on-screen border
    // (uGUI's Sliced Image maps sprite.border through sprite.pixelsPerUnit
    // 1:1 against the Canvas's own referencePixelsPerUnit, which also
    // defaults to 100) - on a 640-900 unit wide panel that's most of the
    // panel, which is exactly the "frame invading the content" bug this
    // fixes. OrnatePanelPixelsPerUnit instead maps that same 280px border
    // down to a reasonable ~31 on-screen units, while every pixel of the
    // authored corner/edge art is still there (see
    // Image.pixelsPerUnitMultiplier on individual CreateOrnatePanel calls
    // for further-scaled-down instances like the small BackButton).
    const float OrnatePanelBorderPx = 280f;
    const float OrnatePanelPixelsPerUnit = 900f;

    static Sprite ornateFrameSpriteCache;

    static Sprite LoadOrnateFrameSprite()
    {
        if (ornateFrameSpriteCache != null) return ornateFrameSpriteCache;

        AssetDatabase.ImportAsset(OrnateFramePath, ImportAssetOptions.ForceUpdate);
        TextureImporter importer = AssetImporter.GetAtPath(OrnateFramePath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            // Game Feel refinement pass - explicit, not left to Unity's own
            // auto-detection: the user reported effect/decoration sprites
            // rendering as solid black/white rectangles in Game View, and
            // this is the one alpha-related import setting this file never
            // set explicitly anywhere. FromInput = "use the PNG's own alpha
            // channel" (as opposed to None/FromGrayScale).
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.spritePixelsPerUnit = OrnatePanelPixelsPerUnit;
            importer.spriteBorder = new Vector4(OrnatePanelBorderPx, OrnatePanelBorderPx, OrnatePanelBorderPx, OrnatePanelBorderPx);
            importer.SaveAndReimport();
        }
        ornateFrameSpriteCache = AssetDatabase.LoadAssetAtPath<Sprite>(OrnateFramePath);
        return ornateFrameSpriteCache;
    }

    // Visual Style Ver.2's uGUI panel: the same navy-fill-plus-ornate-frame
    // treatment as OrnateUi.DrawPanel (IMGUI, TOP screen), built here as a
    // Sliced Image so it 9-slice-stretches correctly at any RectTransform
    // size. Used for every panel/card-shaped element on the Deck Edit
    // screen (COLLECTION/DECK/SELECTED CARD panels, empty card/deck-slot
    // placeholders) instead of CreateNavyGoldPanel's plain thin-edge
    // treatment, which stays in place only as a fallback for anything not
    // yet migrated. A genuinely reusable primitive (Panel), not a TOP/DECK-
    // only one-off - later screens (RESULT/GAME OVER/AREA UNLOCK/SETTING)
    // can call this exact same method. Returns the outer RectTransform for
    // the caller to position/size, same convention as CreateNavyGoldPanel.
    //
    // borderScale further divides the on-screen border beyond
    // OrnatePanelPixelsPerUnit's own baseline (~31 units) via uGUI's
    // Image.pixelsPerUnitMultiplier, for panels much smaller than
    // COLLECTION/DECK/SELECTED CARD (e.g. BackButton, ~180x70) where even
    // that baseline border would eat too much of the available space -
    // 1 = baseline, 2 = half as thick, etc.
    static RectTransform CreateOrnatePanel(Transform parent, string name, float borderScale = 1f)
    {
        Sprite frame = LoadOrnateFrameSprite();

        GameObject rootGO = new GameObject(name);
        rootGO.transform.SetParent(parent, false);
        RectTransform rootRect = rootGO.AddComponent<RectTransform>();

        GameObject fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(rootGO.transform, false);
        RectTransform fillRect = fillGO.AddComponent<RectTransform>();
        // Inset slightly further than the frame's own painted edge so the
        // navy fill never peeks past the ornate border's outer gold line.
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(6f, 6f);
        fillRect.offsetMax = new Vector2(-6f, -6f);
        Image fill = fillGO.AddComponent<Image>();
        // Darker/more opaque than CreateNavyGoldPanel's fill (per the "keep
        // the world barely visible, panel interior dark enough that cards
        // stay readable" brief) - the ornate frame drawn on top is already
        // fairly opaque itself, so this mostly matters for the panel
        // interior the frame's own border doesn't cover.
        fill.color = new Color(0.03f, 0.035f, 0.07f, 0.92f);
        fill.raycastTarget = false;

        GameObject frameGO = new GameObject("Frame");
        frameGO.transform.SetParent(rootGO.transform, false);
        RectTransform frameRect = frameGO.AddComponent<RectTransform>();
        StretchFull(frameRect);
        Image frameImage = frameGO.AddComponent<Image>();
        frameImage.sprite = frame;
        frameImage.type = Image.Type.Sliced;
        frameImage.pixelsPerUnitMultiplier = borderScale;
        frameImage.raycastTarget = false;

        return rootRect;
    }

    // A soft white radial falloff, generated once - tinted/scaled/faded at
    // runtime for the confirm glow instead of needing a hand-authored glow
    // asset.
    static Sprite CreateRadialGlowSprite()
    {
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float maxDist = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center) / maxDist;
                float alpha = Mathf.Clamp01(1f - dist);
                alpha = alpha * alpha; // softer falloff toward the edge
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();

        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
    }

    // For sprites drawn tiled in the game world (via SpriteRenderer.drawMode
    // = Tiled, e.g. GroundFactory.CreateSlopeVisual) - pixelsPerUnit is set
    // from the source texture's own height so it renders at a sensible
    // world-space scale (one texture-height tall per world unit) instead of
    // whatever a generic default would give a texture this size.
    // Bugfix 2026-09-06, item 3 - "Rarity Frameが反映されていない". This used
    // to also cache the loaded Sprites into CardRarityFrames.frames, but
    // that's a plain static C# field with no Unity serialization - writing
    // to it from THIS Editor-only batchmode process had zero effect on the
    // actual game process (Play mode or a device build starts with a fresh,
    // empty array every time), so every card was silently always falling
    // back to the old CardFrame.png. CardRarityFrames now loads its own
    // Sprites lazily at runtime via Resources.Load (see its own comment) -
    // this method's only remaining job is configuring each PNG's import
    // settings once (Editor-only work that genuinely does need to run
    // here), which is why the 5 frames (★1-★5) live under Assets/Resources/
    // CardFrames/ - Resources.Load can only ever find assets physically
    // inside a folder literally named "Resources".
    //
    // Card UI改修(2026-09-08) - ★1は新しく供給された素材(元は
    // Assets/Art/UI/CardFrames/CardFrameRarity1_raw.pngとして置かれていた
    // が、本当にアルファチャンネルを持たない(Format24bppRgb)ことをPowerShell/
    // System.Drawingで確認済みだった)を、今回マスターから新規に供給された
    // ☆1.png(PowerShell/System.Drawingでコーナー/中央A=0・枠部分A≈253を
    // 実際に確認済み、正しい透過を持つ)に差し替え、Resources/CardFrames/
    // CardFrameRarity1.pngとして配置 - これで長年空いていた★1の穴が埋まり、
    // CardRarityFrames.GetFrame(1, ...)がようやく実際のRarity 1専用フレー
    // ムを返せるようになった。
    static void LoadCardRarityFrames()
    {
        ConfigureCardFrameImport("Assets/Resources/CardFrames/CardFrameRarity1.png");
        ConfigureCardFrameImport("Assets/Resources/CardFrames/CardFrameRarity2.png");
        ConfigureCardFrameImport("Assets/Resources/CardFrames/CardFrameRarity3.png");
        ConfigureCardFrameImport("Assets/Resources/CardFrames/CardFrameRarity4.png");
        ConfigureCardFrameImport("Assets/Resources/CardFrames/CardFrameRarity5.png");
    }

    // Item 7 - plain single-sprite UI import (Sprite (2D and UI), alpha
    // enabled, Full Rect mesh - a Full Rect mesh is what lets
    // Image.preserveAspect letterbox correctly instead of assuming a tight
    // alpha-cropped mesh). Not tiled/9-sliced (Clamp, not Repeat) - every
    // Rarity frame here is shown as one whole image via
    // RewardCardUI.frameImage, never sliced. A path that doesn't exist on
    // disk is a no-op (Resources.Load simply returns null for it later,
    // same "missing asset degrades gracefully" pattern as
    // EnemyDatabaseBuilder's per-species sprite paths).
    static void ConfigureCardFrameImport(string path)
    {
        if (!System.IO.File.Exists(path)) return;
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.wrapMode = TextureWrapMode.Clamp;

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);

            importer.SaveAndReimport();
        }
    }

    static Sprite LoadTiledSprite(string path, float pixelsPerUnit)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.alphaIsTransparency = true;
            // Game Feel refinement pass - explicit, not left to Unity's own
            // auto-detection: the user reported effect/decoration sprites
            // rendering as solid black/white rectangles in Game View, and
            // this is the one alpha-related import setting this file never
            // set explicitly anywhere. FromInput = "use the PNG's own alpha
            // channel" (as opposed to None/FromGrayScale).
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.wrapMode = TextureWrapMode.Repeat;
            // Stage01基礎品質修整(2026-09-14) - このヘルパーで読み込む全ての
            // スプライトはSpriteRenderer.drawMode=Tiledで使われる(地面の
            // 断面埋め/プラットフォーム中央タイル/天空回廊の道等)。デフォルト
            // のMesh Type(Tight、アルファ形状に沿った凹凸メッシュ)のままだと
            // Unity自身が実機コンソールで警告する「Sprite Tiling might not
            // appear correctly because the Sprite used is not generated with
            // Full Rect」の状態になり、タイル境界で隙間ができる - マスター
            // 報告「地面断面同士の縦の隙間」の実機再現で確認した実際の原因。
            ApplySpriteMeshTypeFullRect(importer);
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static void ApplySpriteMeshTypeFullRect(TextureImporter importer)
    {
        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
    }

    // 不具合修正(2026-09-10) - LoadTiledSpriteの派生版。既存のVFX単発画像
    // (SlashArcBlue等)は全てCenter Pivot前提で、位置はコード側のtransform
    // 調整で合わせていたが、地面衝撃VFX(ImpactBurstBlue)は「爆発の根本=
    // 地面接地点」を基準にしたいため、Custom Pivotを直接指定できるように
    // した(ApplyCustomPivotを既存のPlayerアニメーション用と共通で再利用)。
    static Sprite LoadTiledSpriteWithPivot(string path, float pixelsPerUnit, Vector2 pivot)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.alphaIsTransparency = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.wrapMode = TextureWrapMode.Repeat;
            ApplyCustomPivot(importer, pivot);
            ApplySpriteMeshTypeFullRect(importer);
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // Level-up choice icons are plain UI images (not sprites drawn in the
    // game world), so import as a regular Texture2D for GUI.DrawTexture /
    // GUI.Button to use directly.
    static Texture2D LoadIconTexture(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            // Game Feel refinement pass - explicit, not left to Unity's own
            // auto-detection: the user reported effect/decoration sprites
            // rendering as solid black/white rectangles in Game View, and
            // this is the one alpha-related import setting this file never
            // set explicitly anywhere. FromInput = "use the PNG's own alpha
            // channel" (as opposed to None/FromGrayScale).
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Full-length music tracks import as huge uncompressed WAVs by default -
    // switch to compressed/streaming so the APK doesn't balloon and the
    // whole track isn't decompressed into memory at once.
    static void ConfigureMusicImport(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null) return;

        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.Streaming;
        settings.compressionFormat = AudioCompressionFormat.Vorbis;
        settings.quality = 0.7f;
        importer.defaultSampleSettings = settings;
        importer.SaveAndReimport();
    }

    // Short one-shot SFX: decompress fully into memory up front (no
    // streaming latency) rather than the music tracks' streaming setup.
    static void ConfigureSfxImport(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null) return;

        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        importer.defaultSampleSettings = settings;
        importer.SaveAndReimport();
    }

    static void ConfigureMobilePlayerSettings()
    {
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortrait = true;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

        // Was still "2ndAction" (the working/codename) - this is what shows
        // as the installed app's label under the home-screen icon.
        PlayerSettings.productName = "One More Mile";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.zero0084.action2nd");
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.zero0084.action2nd");
        PlayerSettings.iOS.targetOSVersionString = "13.0";
        PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;

        ConfigureAppIcon();
    }

    // Replaces the default Unity icon shown on the device home screen -
    // the same source texture is assigned to every required Android icon
    // slot; Unity's build step scales it down as needed for each. Also sets
    // up the Adaptive Icon (Android 8+) background/foreground layers -
    // without those, a launcher that expects an adaptive icon synthesizes
    // its own by shrinking the single legacy icon onto a white circle,
    // which is the "small icon floating in a white circle" look.
    static void ConfigureAppIcon()
    {
        Texture2D icon = LoadIconTexture("Assets/Art/UI/AppIcon.png");
        if (icon == null) return;

        int[] sizes = PlayerSettings.GetIconSizesForTargetGroup(BuildTargetGroup.Android);
        var icons = new Texture2D[sizes.Length];
        for (int i = 0; i < icons.Length; i++) icons[i] = icon;
        PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Android, icons);

        int[] unknownSizes = PlayerSettings.GetIconSizesForTargetGroup(BuildTargetGroup.Unknown);
        if (unknownSizes.Length > 0)
        {
            var unknownIcons = new Texture2D[unknownSizes.Length];
            for (int i = 0; i < unknownIcons.Length; i++) unknownIcons[i] = icon;
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, unknownIcons);
        }

        Texture2D transparentForeground = LoadIconTexture("Assets/Art/UI/AppIconForeground.png");
        if (transparentForeground != null)
        {
            ConfigureAdaptiveIcon(icon, transparentForeground);
        }
    }

    // Background carries the full artwork edge-to-edge (it's the layer the
    // OS mask - circle, squircle, rounded square, whatever the device uses
    // - crops against, which is exactly what we want so no white shows
    // outside the mask); foreground is fully transparent since there's no
    // separate cutout element meant to sit above it. Also fills the "Round"
    // icon slot some launchers read directly, with the same full artwork.
    static void ConfigureAdaptiveIcon(Texture2D background, Texture2D foreground)
    {
        NamedBuildTarget target = NamedBuildTarget.Android;

        PlatformIcon[] adaptiveIcons = PlayerSettings.GetPlatformIcons(target, AndroidPlatformIconKind.Adaptive);
        foreach (PlatformIcon slot in adaptiveIcons)
        {
            slot.SetTextures(new[] { background, foreground });
        }
        PlayerSettings.SetPlatformIcons(target, AndroidPlatformIconKind.Adaptive, adaptiveIcons);

        PlatformIcon[] roundIcons = PlayerSettings.GetPlatformIcons(target, AndroidPlatformIconKind.Round);
        foreach (PlatformIcon slot in roundIcons)
        {
            slot.SetTextures(new[] { background });
        }
        PlayerSettings.SetPlatformIcons(target, AndroidPlatformIconKind.Round, roundIcons);
    }

    // Root/Visual split (see the same convention on GroundFactory.
    // CreateEnemy): Root is the gameplay-authoritative transform - position
    // (foot/ground line, groundOffset=0), rotation (slope tilt), and scale
    // (facing flip) that PlayerController/TerrainManager/BodyCollider/
    // AttackHitbox all key off. Visual is the one child that actually
    // renders the sprite, so a future per-asset visual nudge only ever
    // needs Visual's own localPosition - never Root, never the collider.
    static GameObject CreatePlayer(Sprite sprite)
    {
        GameObject go = new GameObject("Player");
        go.tag = "Player";

        GameObject visualGO = new GameObject("Visual");
        visualGO.transform.SetParent(go.transform, false);

        var sr = visualGO.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        // The source art's actual pixel colors are now pre-brightened
        // (PlayerAnimGenerator.BrightenBaseSprites) instead of relying on
        // transparency to fake a lighter look - half-transparent black over
        // a similarly-dark background still reads as "a dark shadow", so
        // near-opaque here lets the genuinely lighter colors show clearly.
        sr.color = new Color(1f, 1f, 1f, 0.95f);
        sr.sortingOrder = RenderOrder.Player;
        visualGO.AddComponent<SpriteOutline>();

        // Documents where the ground-contact sample point is (Root's own
        // X/Y - see PlayerController.Move()/TerrainManager.GetHeightAt).
        // Not read by any code itself - grounding here is math-driven, not
        // a physics raycast - but gives every Ground-type character the
        // same named node in its hierarchy for anyone looking for it.
        GameObject groundCheckGO = new GameObject("GroundCheck");
        groundCheckGO.transform.SetParent(go.transform, false);

        var col = go.AddComponent<BoxCollider2D>();
        col.size = Vector2.one;
        // Offset up by 0.5 to compensate for the root transform now sitting
        // at the sprite's FOOT (groundOffset=0 - see PlayerController) instead
        // of at body-center like it did when this collider size/position was
        // originally tuned. This puts the collider at exactly the same
        // world-space span it always had (root.y .. root.y+1, back when
        // root.y was ground+0.5) - i.e. restores prior hit-detection
        // behavior unchanged, rather than leaving it centered on the feet.
        col.offset = new Vector2(0f, 0.5f);
        var colDebug = go.AddComponent<ColliderDebugView>();
        colDebug.color = new Color(0.2f, 0.6f, 1f);

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        // Added early, before PlayerAnimator/PlayerDustEffects, on purpose:
        // a component added later that [RequireComponent]s PlayerController
        // would otherwise auto-create one right then, and this explicit
        // AddComponent would add a SECOND, independent instance on top of
        // it - both running Update()/Move() every frame and silently
        // doubling the player's effective movement speed. (Exactly this
        // happened once - PlayerDustEffects used to declare that attribute.)
        var pc = go.AddComponent<PlayerController>();
        // Game Feel pass - a soft round particle (the same procedural dot
        // Polish Pass 1's hit-spark/run-dust already use) instead of the
        // flat square placeholder, for a less "ハリボテ" death burst. No new
        // asset needed - see OneShotSpriteEffect.SoftDotSprite's own comment.
        pc.explosionParticleSprite = OneShotSpriteEffect.SoftDotSprite();
        // Run Continuation/Checkpoint Ver.1, item 4 - reuses the existing
        // Double Jump Ring effect sprite as the escape-charge magic circle
        // (see PlayerController.escapeRingSprite's own comment) - same
        // already-imported asset PlayerDustEffects.doubleJumpRingSprite
        // uses, loaded again here (idempotent, same pattern already used
        // for CardBack.png/CardFrame.png across multiple Canvases).
        pc.escapeRingSprite = LoadTiledSprite("Assets/Art/Effects/DoubleJumpRing.png", 1672f);

        // ===== Common rendering rule: sizing =====
        // Root's transform.localScale must stay (1,1,1) - the only scale
        // ever applied to it is ApplyAttackDirection's facing flip
        // ((-1,1,1) / (1,1,1)), which is a gameplay-direction flag, not a
        // sizing knob. All actual on-screen sizing comes from each
        // sprite's own pixelsPerUnit, chosen so its measured content-
        // pixel-height maps to the SAME target world height as every
        // other Ground-type character (~1.13 units - see below). Adding a
        // new Player/Ground-Enemy sprite folder later: measure its
        // representative frame's opaque-pixel bounding-box height (the
        // same bottom-up alpha scan ComputeLowestContentPivotY already
        // does for the foot pivot), then set pixelsPerUnit =
        // thatHeightPx / 1.13. Don't touch Root's scale to compensate for
        // a mis-sized asset - fix the PPU instead.
        //
        // Visual Style Ver.1 - AI-generated replacement art in *_v1 folders,
        // pointed at instead of the original PlayerRun/PlayerJump/etc.
        // folders (left untouched on disk for an easy revert: just swap
        // these 8 paths back). Each folder's own AI-generated sheet drew
        // the character at a slightly different scale (the "same character
        // scale" instruction in the generation prompt wasn't perfectly
        // consistent across separately-generated sheets), so a single
        // shared pixelsPerUnit made the player visibly change size between
        // e.g. running and airborne. Each folder gets its own
        // pixelsPerUnit instead, individually measured so the character's
        // on-screen height matches across all of them (target: the old
        // art's ~1.13-world-unit-tall silhouette, i.e. each value here is
        // that folder's measured content-pixel-height / 1.13). Each sprite
        // also gets a per-frame foot/lowest-point pivot (see
        // ConfigureSpriteFolderImportWithFootPivot) instead of dead-center,
        // so the player doesn't appear to float above or sink into
        // platforms depending on which animation is playing.
        ConfigureSpriteFolderImportWithFootPivot("Assets/Art/PlayerRun_v1", 186f);
        ConfigureSpriteFolderImportWithFootPivot("Assets/Art/PlayerJump_v1", 167f);
        // 品質改善 Bug #002(2026-09-09), item 3/6 - 「Player Attack Animation
        // 中にCharacter Sizeが変わる」の再調査。実測(頭頂〜足先のアルファ
        // 境界、bottom-up alpha scan)したところ、この3コマの高さは268/301/
        // 312pxとコマごとにばらつきがあり(振りの姿勢差、自然な範囲)、旧
        // PPU(237)はそのどれとも噛み合わない値だった(frame0が基準の
        // +32%という大きな乖離)。基準フレームを1枚選ぶのではなく、3コマ
        // の最大/最小の中間(290px)を基準に据えることで、最大でも約±8%の
        // 乖離に収める(1枚に厳密に合わせると他のコマがより大きくズレる
        // ため、3コマ全体でのバランスを優先)。
        ConfigureSpriteFolderImportWithFootPivot("Assets/Art/PlayerAttack_v1", 257f);
        // AttackSmall (combo stage 1) was originally calibrated (250) against
        // a mid-swing frame, but this clip's very FIRST frame - the one the
        // player actually sees the instant a stage-1 attack starts, right
        // after Run - draws the character in a compact windup pose that's
        // measurably shorter (234px) than every other folder's starting
        // pose, so at 250 it rendered ~17% smaller than normal the moment
        // the swing began (measured via a bottom-up alpha scan, same method
        // as the foot pivot). Lowered so THAT frame matches the ~1.13-unit
        // baseline (234/1.13); later frames in the swing grow larger as the
        // sword extends, same as the other attack folders already do.
        ConfigureSpriteFolderImportWithFootPivot("Assets/Art/PlayerAttackSmall_v1", 207f);
        // 品質改善 Bug #002、item 3/6 - PlayerAttack_v1と同じ理由・同じ方式
        // (3コマ204/227/230pxの中間217pxを基準)。旧180だと全コマが基準
        // より26-28%大きく描画されていた。
        ConfigureSpriteFolderImportWithFootPivot("Assets/Art/PlayerAttackLarge_v1", 192f);
        // 不具合修正(2026-09-10) - 「地上着地時のキャラサイズが一時的に
        // 大きくなる」。品質改善Bug #002では land_00/land_01(241px/205px)
        // の中間223pxを基準(197)にしていたが、これは「Landステート開始直
        // 後にプレイヤーが最初に見るフレーム」であるland_00自身がRun/Jump
        // より約8%大きく描画される結果になっており、まさにこの実機報告の
        // 症状そのものだった。他の攻撃アニメ群と同じ「State開始直後に最初
        // に見えるフレームを基準にする」原則どおり、land_00(241px)を基準
        // に戻す(land_01は着地から走行へ戻る一瞬の中間コマで、about -15%
        // 小さく見えるトレードオフはあるが、「大きくなる」よりは目立ちに
        // くいと判断)。
        ConfigureSpriteFolderImportWithFootPivot("Assets/Art/PlayerLand_v1", 213f);

        // 上下攻撃アニメーション差し替え(2026-09-08) - マスターから供給
        // された専用手描きアニメーション3種(地上上攻撃5枚/空中上攻撃5枚/
        // 下降攻撃3枚)に差し替え。旧実装(既存のJumpStart/AttackSmall/
        // DoubleJump素材を「たまたま流用」していたもの、および
        // PlayerJump_v1/jump_01.pngを回転加工しただけの下降攻撃3枚)を全て
        // 置き換える - PlayerAnimator/PlayerController側のState機械(Jump
        // Start/DoubleJump/DownAttack)自体は無改造のまま(ブリーフの「既存
        // のGround判定が利用できる場合は新しい判定システムを作らない」指示
        // どおり - 地上上攻撃はJumpStarted、空中上攻撃はDoubleJumpedという
        // 既存イベントがそのまま「Grounded/Airborne」の判別を兼ねている)。
        //
        // Pivot: 供給されたシートは剣の振り幅に応じて各コマの実効横幅が
        // 変わり、キャラクター本体が水平方向に中央固定されていない(既存
        // のConfigureSpriteFolderImportWithFootPivotが前提とするX=0.5固定
        // が使えない) - マスターの依頼書自身が名指しで警告していた「画像
        // サイズ基準で中央揃えするとガクガクする」症状を避けるため、地上
        // 版は自動足元検出(X,Y両方)、空中版と下降攻撃は目視で選んだ胴体/
        // 剣先基準点を個別に指定している(下記ConfigureSpriteFolderImport
        // WithFootPivotXY/WithManualPivots参照)。
        //
        // PPU: 各フォルダのPPUは、そのState開始直後にプレイヤーが最初に
        // 見るフレーム(=直前のState、Run/Jumpと同じ高さで違和感なく繋がる
        // べきフレーム)のアルファ内容の高さを1.13ワールド単位の基準に
        // 合わせて算出(既存のPlayerAttackSmall_v1が「Stage1開始直後に見
        // える最初のフレーム」を基準にPPUを再調整した、という前例と同じ
        // 考え方)。剣が伸びきる中盤コマではその分やや大きく見える(=既存
        // の攻撃アニメ群も同様に許容している、振りの勢いによる自然な変化)。
        ConfigureSpriteFolderImportWithFootPivotXY("Assets/Art/PlayerUpAttackGround_v1", 249f);
        Vector2[] upAttackAirPivots =
        {
            new Vector2(0.564f, 0.351f), // upattackair_00 - 振りかぶり開始
            new Vector2(0.530f, 0.406f),
            new Vector2(0.426f, 0.523f), // upattackair_02 - 頭上へ最大に振り抜いた瞬間
            new Vector2(0.449f, 0.406f),
            new Vector2(0.576f, 0.351f), // upattackair_04 - 空中姿勢へ復帰
        };
        ConfigureSpriteFolderImportWithManualPivots("Assets/Art/PlayerUpAttackAir_v1", 321f, upAttackAirPivots);

        // 下降攻撃アート差し替え(2026-09-09) - マスターから直接供給された
        // 2枚(急降下ダイブ姿勢/着地衝撃姿勢)へ総入れ替え。振りかぶりコマ
        // は供給されなかったため、PlayerDownAttack_v1の2枚(downattack_00/
        // 01)には同じダイブ姿勢を複製配置 - 元々downattack_01(ダイブ)は
        // 急降下中ずっと保持され続けるコマなので、開始直後から同じ絵が
        // 続くだけで見た目上の不整合はない。Pivotは頭部・胴体のアルファ
        // 加重重心を実測(centroid.ps1)、PPUは頭頂〜足先の本体のみ(剣・
        // マント除く)を目視実測して算出(既存踏襲)。
        Vector2[] downAttackPivots =
        {
            new Vector2(0.568f, 0.582f), // downattack_00/01 - 胴体重心実測(共通、供給素材が1枚のため複製)
            new Vector2(0.568f, 0.582f),
        };
        // downattack_00の本体のみの高さ(剣・マント除く、足先(頭上)〜頭頂
        // (体下端))を実測 約738px -> 738/1.13 ≈ 653
        ConfigureSpriteFolderImportWithManualPivots("Assets/Art/PlayerDownAttack_v1", 653f, downAttackPivots);
        Vector2[] downAttackLandPivots = { new Vector2(0.546f, 0.172f) }; // downattackland_00 - 剣先が地面に刺さる衝撃点を目視で指定
        // 不具合修正(2026-09-10) - 「下攻撃の着地時の画像がまだ少し大きい」。
        // 前回の実測(385px)は頭頂位置を少し低く見誤っており、髪の生え際
        // 込みで再計測すると頭頂〜足先(衝撃エフェクト・岩の破片除く)は
        // 約400px -> 400/1.13 ≈ 354 だった(前回のPPU341だと約4%大きく
        // 描画されていた)。
        ConfigureSpriteFolderImportWithManualPivots("Assets/Art/PlayerDownAttackLand_v1", 354f, downAttackLandPivots);

        Sprite[] runFrames = LoadSpriteSequence("Assets/Art/PlayerRun_v1");
        Sprite[] jumpFrames = LoadSpriteSequence("Assets/Art/PlayerJump_v1");
        Sprite[] attackFrames = LoadSpriteSequence("Assets/Art/PlayerAttack_v1");
        Sprite[] attackFramesSmall = LoadSpriteSequence("Assets/Art/PlayerAttackSmall_v1");
        Sprite[] attackFramesLarge = LoadSpriteSequence("Assets/Art/PlayerAttackLarge_v1");
        Sprite[] upAttackGroundFrames = LoadSpriteSequence("Assets/Art/PlayerUpAttackGround_v1");
        Sprite[] upAttackAirFrames = LoadSpriteSequence("Assets/Art/PlayerUpAttackAir_v1");
        Sprite[] landFrames = LoadSpriteSequence("Assets/Art/PlayerLand_v1");
        Sprite[] downAttackFrames = LoadSpriteSequence("Assets/Art/PlayerDownAttack_v1");
        Sprite[] downAttackLandFrames = LoadSpriteSequence("Assets/Art/PlayerDownAttackLand_v1");

        if (runFrames.Length > 0)
        {
            var animator = go.AddComponent<PlayerAnimator>();
            animator.runFrames = runFrames;
            animator.jumpFrames = jumpFrames;
            animator.attackFrames = attackFrames;
            animator.attackFramesSmall = attackFramesSmall;
            animator.attackFramesLarge = attackFramesLarge;
            // 上下攻撃アニメーション差し替え(2026-09-08) - jumpStartFrames/
            // doubleJumpFramesという既存フィールド名自体は変更していない
            // (タップジャンプ廃止以降、ジャンプは常に上攻撃を伴うため、
            // 「JumpStart State = 地上上攻撃」「DoubleJump State = 空中上
            // 攻撃」という対応そのものは既に成立している - フィールドの
            // 中身だけを専用アートへ差し替えた)。
            animator.jumpStartFrames = upAttackGroundFrames;
            animator.doubleJumpFrames = upAttackAirFrames;
            animator.landFrames = landFrames;
            animator.downAttackFrames = downAttackFrames;
            animator.downAttackLandFrames = downAttackLandFrames;
            // 5枚を、Hitbox有効時間(upAttackActiveTime=0.28s、PlayerController
            // 参照)とほぼ同じ長さで再生しきるfps - 見た目の振りとHitboxの
            // タイミングが大きくズレないようにする。
            animator.jumpStartFps = 18f;
            animator.doubleJumpFps = 18f;
            sr.sprite = runFrames[0];
        }

        var dustFx = go.AddComponent<PlayerDustEffects>();
        // Legacy fallbacks only (see PlayerDustEffects' own field comments) -
        // kept wired so an already-built scene never goes silently blank,
        // but jumpPuffSprite/landingPuffSprite below take priority.
        dustFx.jumpStartDustFrames = LoadSpriteSequence("Assets/Art/JumpDust");
        dustFx.doubleJumpDustSprite = FirstSprite("Assets/Art/DoubleJumpDust");
        dustFx.ascensionSmokeFrames = LoadSpriteSequence("Assets/Art/AscensionSmoke");
        // Game Feel refinement pass - OneMoreMile_GameFeel pack, same
        // ~1-unit-base-then-code-multiplier convention as the enemy effects
        // above (see that block's comment).
        dustFx.jumpPuffSprite = LoadTiledSprite("Assets/Art/Effects/JumpPuff.png", 1536f);
        dustFx.landingPuffSprite = LoadTiledSprite("Assets/Art/Effects/LandingPuff.png", 1536f);
        dustFx.doubleJumpRingSprite = LoadTiledSprite("Assets/Art/Effects/DoubleJumpRing.png", 1672f);
        dustFx.runDustSprite = LoadTiledSprite("Assets/Art/Effects/RunDust.png", 1536f);
        dustFx.grassDustSprite = LoadTiledSprite("Assets/Art/Effects/GrassDust.png", 1536f);
        dustFx.groundShadowSprite = LoadTiledSprite("Assets/Art/Effects/GroundShadow.png", 1672f);

        // Attack hitbox (child) - scaled to 2x the original reach/size, with
        // its near edge kept roughly where the old (smaller) box started.
        // Y=0.5 for the same reason as the body collider's offset above -
        // restores this child's original world-space height (it used to sit
        // at root.y+0 when root.y was ground+0.5; root.y is now the foot
        // line, so it needs the +0.5 back explicitly) instead of drifting
        // down to foot height along with the (intentionally) lowered root.
        GameObject hitbox = new GameObject("AttackHitbox");
        hitbox.transform.SetParent(go.transform);
        hitbox.transform.localPosition = new Vector3(1.0f, 0.5f, 0f);
        hitbox.transform.localScale = new Vector3(1.4f, 1.8f, 1f);
        hitbox.tag = "PlayerAttack";
        // エリアルコンボ改修(2026-09-11) - EnemyControllerが「どの攻撃に
        // 当たったか」を判定できるよう、各Hitboxへ種別タグを付与
        // (PlayerAttackInfo.cs参照)。
        hitbox.AddComponent<PlayerAttackInfo>().kind = PlayerAttackKind.Normal;

        var hitboxCol = hitbox.AddComponent<BoxCollider2D>();
        hitboxCol.isTrigger = true;
        var hitboxDebug = hitbox.AddComponent<ColliderDebugView>();
        hitboxDebug.color = new Color(1f, 0.9f, 0.1f);

        // 攻撃エフェクト全面調整(2026-09-08)/品質改善 Bug #002(2026-09-09)
        // - 「巨大な紫剣エフェクト」(AttackSlashFx流用)から、剣の軌跡に
        // 沿った控えめな青白いVFXへ差し替え。新素材は既に「三日月が右上
        // へ向けて自然に振り上がる」形状で供給されているため、旧構成が
        // 必要としていた80°回転(汎用の斜め剣画像を無理やり上向きに見せる
        // ための回転)はもう不要 - 回転0のまま、位置とScaleだけをInspector
        // から調整する運用にした(上/空中/下降攻撃と共通、ここで一度だけ
        // ロードして使い回す)。
        // 不具合修正(2026-09-09、下降攻撃アート差し替えと同時) - 通常攻撃
        // と同じ原因(素材が小さすぎる+位置がHitboxとズレている)が上/下降
        // 攻撃にも残っていたため、ChatGPTで新規に太く大きい専用VFXを生成
        // (SlashUpBlue.png=斬り上げ用クレセント、DiveTrailBlue.png=急降下
        // トレイル本体を差し替え)。PPUはそれぞれの対応Hitbox実寸に揃うよ
        // う算出(SlashUpBlue: 1478px÷1.8u≒821、DiveTrailBlue: 1651px÷
        // 2.2u≒751 - トレイルはHitbox本体よりやや大きめの2.2uを基準にし
        // ている、急降下中ずっと表示され続ける演出上の効果のため)。
        Sprite diveTrailVfx = LoadTiledSprite("Assets/Art/Effects/DiveTrailBlue.png", 751f);
        Sprite slashUpVfx = LoadTiledSprite("Assets/Art/Effects/SlashUpBlue.png", 821f);

        // 派手なアニメーション化(2026-09-10) - マスターの「通常攻撃と上攻撃
        // のエフェクトをもっと派手なアニメーションにしたい」という指示で、
        // ChatGPTで「細い先行線→太いピーク+バースト→二次衝撃波→残像→
        // 消えかけ」の5コマシートを新規生成し、5枚を等幅スライス+共通キャン
        // バス中央寄せで書き出したもの(scratchpad/attackframes/split_center_
        // frames.ps1)。従来のPlaySingle(1枚絵をScale/Alphaで手続き的に演
        // 出)ではなく、AttackSlashVisual.PlayFramesでframes配列を実コマ送り
        // 再生する。PPUは旧1枚絵VFX(SlashArcBlue=500想定897px÷1.8u、
        // SlashUpBlue=821想定1478px÷1.8u)のクレセント実寸(約435px)が
        // ほぼ同じ世界サイズになるよう算出(435px÷約1.78u≒245前後)。
        ConfigureSpriteFolderImport("Assets/Art/Effects/SlashArcBlueFrames", 250f);
        ConfigureSpriteFolderImport("Assets/Art/Effects/SlashUpBlueFrames", 245f);
        Sprite[] slashArcFrames = LoadSpriteSequence("Assets/Art/Effects/SlashArcBlueFrames");
        Sprite[] slashUpFrames = LoadSpriteSequence("Assets/Art/Effects/SlashUpBlueFrames");

        // 不具合修正(2026-09-09) - 「攻撃エフェクトが表示されていない」。
        // マスター提供の実機動画+新設のAttackVfxCapture(Editor専用デバッグ
        // ツール、Tools/2ndAction/Capture Attack VFX)による直接検証で判明
        // した実際の原因は「描画されていない」のではなく「描画はされて
        // いるが小さすぎる上にキャラクター/剣から離れた位置に浮いて見え、
        // 実機の明るい空背景に溶け込んでほぼ視認できない」だった。通常
        // 攻撃向けにChatGPTで新規生成した、太くはっきりした専用VFX
        // (SlashArcBlue.png、旧SlashCrescentBlueより大幅に大きく明るい)
        // へ差し替える(上/空中/下降攻撃は旧クレセントのまま、今回は通常
        // 攻撃のみに影響を絞る)。PPUはHitbox本体のサイズ(hitboxBaseScale
        // ≒1.4x1.8)とほぼ揃うように算出(897px÷1.8u≒500)。
        Sprite slashArcVfx = LoadTiledSprite("Assets/Art/Effects/SlashArcBlue.png", 500f);

        // Slash FX (separate from the invisible hitbox) - a short one-shot
        // sword-swing effect showing the attack's reach, sized per combo
        // stage. 不具合修正(2026-09-09) - 「攻撃範囲がちゃんと見えるよう
        // に」。位置をAttackHitboxの基準位置(hitboxBaseLocalPos)と完全に
        // 一致させ(以前は(0.3,0.5)という別の固定値で、Hitboxの実際の位置
        // (1.0,0.5)とズレていた)、PlayerController.DoAttack側でも同じ
        // hitboxBaseLocalPosを使って毎回位置を合わせ直すことで、Hitboxと
        // VFXが常に同じ場所に表示されるようにする。
        GameObject slashGO = new GameObject("AttackSlash");
        slashGO.transform.SetParent(go.transform);
        slashGO.transform.localPosition = hitbox.transform.localPosition;
        // 派手なアニメーション化(2026-09-10) - 新しい5コマシートは素材自体が
        // 「左下→右上」の斜めクレセントとして描かれているため、旧1枚絵向け
        // の-35°補正は不要(かけると逆に傾く)。回転0のまま位置/Scaleだけ
        // 合わせる。
        slashGO.transform.localRotation = Quaternion.identity;
        var slashVisual = slashGO.AddComponent<AttackSlashVisual>();
        slashVisual.singleSprite = slashArcVfx;   // PlaySingleフォールバック用に残す
        slashVisual.frames = slashArcFrames;      // PlayFrames(実コマ送り)で使う主役
        slashVisual.fps = 17f;                    // 5コマ÷17fps≒0.29秒(旧singleDuration相当)
        slashVisual.singleDuration = 0.28f;
        slashVisual.opacity = 0.92f;

        pc.attackHitbox = hitboxCol;
        pc.attackSlashVisual = slashVisual;

        // Operation System Ver.2 (2026-09-06), item 2 - "上フリック=ジャンプ
        // 攻撃"用の独立したHitbox+Slash FX。既存のAttackHitbox/AttackSlash
        // とは別オブジェクト(Forward/Backwardの3段コンボ系統には一切触れ
        // ないよう分離、詳細はPlayerController.DoUpAttackのコメント参照)。
        // 不具合修正(2026-09-10) - 「空中上攻撃時、攻撃範囲がプレイヤーキ
        // ャラから離れたところから開始している」。デバッグ表示(Collider
        // DebugView)で確認したところ、旧位置(Y=1.7、高さ1.4→Y範囲
        // [1.0,2.4])はキャラクター本体の高さ(約1.13)と一切重ならず、頭上
        // にぽっかり浮いた判定になっていた - 通常攻撃のHitbox(Y=0.5、高さ
        // 1.8→Y範囲[-0.4,1.4]、キャラクター全身を包含)と同じ考え方に揃え、
        // Y=0.9・高さ2.0(Y範囲[-0.1,1.9])へ変更 - 下端がキャラクター本体
        // (足元付近)と重なりつつ、上端は従来同様頭上高くまで届く。
        //
        // 実機フィードバック(2026-09-12第5弾) - 「主人公の前方～斜め前上
        // 方向への攻撃判定が狭く、上攻撃を出しても敵に届かず相打ちになる」。
        // X方向の半径を0.8→1.2、中心を0.3→0.5前方へ移動(X範囲[-0.7,1.7]、
        // 旧[-0.5,1.1]) - 「真上だけの縦長判定」ではなく前方～斜め前上まで
        // まとめてカバーする扇形に近い範囲を狙う(VFXの見た目から極端に
        // はみ出さない程度の拡張に留めた)。Y方向は変更なし。
        GameObject upHitbox = new GameObject("UpAttackHitbox");
        upHitbox.transform.SetParent(go.transform);
        upHitbox.transform.localPosition = new Vector3(0.5f, 0.9f, 0f);
        upHitbox.transform.localScale = new Vector3(2.4f, 2.0f, 1f);
        upHitbox.tag = "PlayerAttack";
        upHitbox.AddComponent<PlayerAttackInfo>().kind = PlayerAttackKind.Up;

        var upHitboxCol = upHitbox.AddComponent<BoxCollider2D>();
        upHitboxCol.isTrigger = true;
        var upHitboxDebug = upHitbox.AddComponent<ColliderDebugView>();
        upHitboxDebug.color = new Color(0.6f, 0.9f, 1f);

        // 不具合修正(2026-09-09) - 通常攻撃と同じ理由で、位置をUpAttack
        // Hitboxの基準位置と完全に一致させる(以前は(0.35,1.5)という別の
        // 固定値で、Hitboxの実際の位置(0.3,1.7)とズレていた)。
        GameObject upSlashGO = new GameObject("UpAttackSlash");
        upSlashGO.transform.SetParent(go.transform);
        upSlashGO.transform.localPosition = upHitbox.transform.localPosition;
        var upSlashVisual = upSlashGO.AddComponent<AttackSlashVisual>();
        upSlashVisual.singleSprite = slashUpVfx;   // PlaySingleフォールバック用に残す
        upSlashVisual.frames = slashUpFrames;      // PlayFrames(実コマ送り)で使う主役
        upSlashVisual.fps = 17f;                   // 5コマ÷17fps≒0.29秒
        // Item「重要：エフェクトサイズ」- 「巨大なエフェクトを画面いっぱ
        // いに表示する必要はない」「キャラクターの剣の軌跡＋少し外側」程
        // 度。singleDuration/opacityもここでInspector調整可能。
        upSlashVisual.singleDuration = 0.28f;
        upSlashVisual.opacity = 0.92f;
        pc.upAttackHitbox = upHitboxCol;
        pc.upAttackSlashVisual = upSlashVisual;

        // 実機フィードバック(2026-09-12第5弾) - 「上攻撃で主人公の真上
        // 付近のEnemyも拾い直せるように」。ダメージ判定(UpAttackHitbox)
        // とは別の、Pickup/Vacuum専用のマーカー範囲。"PlayerAttack"タグは
        // 付けない(EnemyController.OnTriggerEnter2Dの通常ダメージ判定には
        // 一切関与させない、あくまでPlayerController.TriggerUpAttackVacuum
        // がPhysics2D.OverlapBoxAllで.boundsだけを読み取る手動判定用) -
        // 主人公の真上を中心に、少し前後までカバーする範囲(item 9「吸い
        // 込み範囲は広げすぎない、剣の斬り上げに巻き込まれても違和感のない
        // 範囲に限定」に沿って、まずは控えめなサイズから)。
        GameObject vacuumGO = new GameObject("UpAttackVacuumArea");
        vacuumGO.transform.SetParent(go.transform);
        vacuumGO.transform.localPosition = new Vector3(0.2f, 2.1f, 0f);
        vacuumGO.transform.localScale = new Vector3(2.2f, 2.2f, 1f);
        var vacuumCol = vacuumGO.AddComponent<BoxCollider2D>();
        vacuumCol.isTrigger = true;
        vacuumCol.enabled = false; // DoUpAttack中のみ一時的に有効化(他のHitboxと同じ慣習、デバッグ表示用)
        var vacuumDebug = vacuumGO.AddComponent<ColliderDebugView>();
        vacuumDebug.color = new Color(0.9f, 0.6f, 1f);
        pc.upAttackVacuumHitbox = vacuumCol;

        // 方向攻撃システム Ver.2(2026-09-07)、項目3 - "空中で↓フリック=
        // 下降攻撃"用の独立したHitbox+Slash FX。UpAttackHitbox/UpAttackSlash
        // と全く同じ構造(別オブジェクト、Forward/Backwardコンボには一切
        // 触れない)。プレイヤーの真下〜やや前方下をカバーする位置・サイ
        // ズにして、下降中の敵を攻撃できるようにする(「剣先だけではなく
        // 真下付近にも多少余裕のある判定」との指示どおり、UpAttackHitbox
        // と同程度の余裕を持たせたサイズ)。
        GameObject downHitbox = new GameObject("DownAttackHitbox");
        downHitbox.transform.SetParent(go.transform);
        downHitbox.transform.localPosition = new Vector3(0.15f, -0.4f, 0f);
        downHitbox.transform.localScale = new Vector3(1.5f, 1.3f, 1f);
        downHitbox.tag = "PlayerAttack";
        downHitbox.AddComponent<PlayerAttackInfo>().kind = PlayerAttackKind.Down;

        var downHitboxCol = downHitbox.AddComponent<BoxCollider2D>();
        downHitboxCol.isTrigger = true;
        var downHitboxDebug = downHitbox.AddComponent<ColliderDebugView>();
        downHitboxDebug.color = new Color(1f, 0.5f, 0.2f);

        // 攻撃エフェクト全面調整(2026-09-08)/不具合修正(2026-09-09) - 縦
        // 方向の太いトレイルVFX。DownAttackHitboxの基準位置から
        // PlayerController.downSlashUpwardOffset分だけ上にずらした位置に
        // 配置し(トレイルが衝撃点付近から上へ伸びているように見せる)、
        // ShowSustained/HideSustainedで急降下中ずっと表示し続ける(着地の
        // 瞬間にHideSustained - PlayerController.EndDiveAttack参照)。新
        // 素材は既に縦向きなので回転は不要。
        GameObject downSlashGO = new GameObject("DownAttackSlash");
        downSlashGO.transform.SetParent(go.transform);
        downSlashGO.transform.localPosition = downHitbox.transform.localPosition + new Vector3(0f, pc.downSlashUpwardOffset, 0f);
        var downSlashVisual = downSlashGO.AddComponent<AttackSlashVisual>();
        downSlashVisual.singleSprite = diveTrailVfx;
        downSlashVisual.opacity = 0.85f;

        pc.downAttackHitbox = downHitboxCol;
        pc.downAttackSlashVisual = downSlashVisual;

        // 不具合修正(2026-09-10) - 「下攻撃の着地時に衝撃はエフェクトを
        // 追加し、それにも攻撃判定が入るように」。DownAttackHitbox(ダイブ
        // 中のみ有効)とは別の独立したHitbox+VFX - 着地の瞬間だけ短時間
        // (PlayerController.diveImpactHitboxDuration)有効になり、地面沿い
        // に左右へ広い判定(ダイブ本体より横に広く、縦は低い)で周囲の敵を
        // まとめて巻き込む。VFXはChatGPTで新規生成した地面衝撃バースト
        // (ImpactBurstBlue.png、他のエネルギーエフェクトと同じ配色で統一)
        // - Pivotを爆発の根本(接地点)に指定し、地面にめり込まず自然に接地
        // して見えるようにする(横長1634x795px、PPUはHitbox幅3.0uに揃うよ
        // う算出: 1634px÷3.0u≒545)。
        Sprite impactBurstVfx = LoadTiledSpriteWithPivot("Assets/Art/Effects/ImpactBurstBlue.png", 545f, new Vector2(0.5f, 0.05f));

        GameObject downLandHitbox = new GameObject("DownAttackLandHitbox");
        downLandHitbox.transform.SetParent(go.transform);
        downLandHitbox.transform.localPosition = new Vector3(0f, 0.1f, 0f);
        downLandHitbox.transform.localScale = new Vector3(3.0f, 1.0f, 1f);
        downLandHitbox.tag = "PlayerAttack";
        downLandHitbox.AddComponent<PlayerAttackInfo>().kind = PlayerAttackKind.DownImpact;

        var downLandHitboxCol = downLandHitbox.AddComponent<BoxCollider2D>();
        downLandHitboxCol.isTrigger = true;
        downLandHitboxCol.enabled = false;
        var downLandHitboxDebug = downLandHitbox.AddComponent<ColliderDebugView>();
        downLandHitboxDebug.color = new Color(1f, 0.85f, 0.2f);

        GameObject downLandSlashGO = new GameObject("DownAttackLandSlash");
        downLandSlashGO.transform.SetParent(go.transform);
        downLandSlashGO.transform.localPosition = new Vector3(0f, 0f, 0f);
        var downLandSlashVisual = downLandSlashGO.AddComponent<AttackSlashVisual>();
        downLandSlashVisual.singleSprite = impactBurstVfx;
        downLandSlashVisual.singleDuration = 0.3f;
        downLandSlashVisual.singleStartScaleFraction = 0.5f;
        downLandSlashVisual.opacity = 0.9f;

        pc.downAttackLandHitbox = downLandHitboxCol;
        pc.downAttackLandSlashVisual = downLandSlashVisual;

        return go;
    }

    static Sprite FirstSprite(string dir)
    {
        Sprite[] frames = LoadSpriteSequence(dir);
        return frames.Length > 0 ? frames[0] : null;
    }

    // Explicitly configures every PNG in a folder as a single Sprite with a
    // given pixels-per-unit before LoadSpriteSequence reads it - needed for
    // any frame folder that wasn't already produced by a tool that set this
    // itself (the existing Dragon*/Player* folders were), otherwise a fresh
    // import can default to a non-Sprite texture type.
    static void ConfigureSpriteFolderImport(string dir, float pixelsPerUnit)
    {
        if (!Directory.Exists(dir)) return;
        foreach (string f in Directory.GetFiles(dir, "*.png"))
        {
            string assetPath = f.Replace('\\', '/');
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) continue;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.alphaIsTransparency = true;
            // Game Feel refinement pass - explicit, not left to Unity's own
            // auto-detection: the user reported effect/decoration sprites
            // rendering as solid black/white rectangles in Game View, and
            // this is the one alpha-related import setting this file never
            // set explicitly anywhere. FromInput = "use the PNG's own alpha
            // channel" (as opposed to None/FromGrayScale).
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }
    }

    // Like ConfigureSpriteFolderImport, but also gives each sprite its own
    // custom pivot instead of the default dead-center one - the Visual
    // Style Ver.1 player frames were each cropped to their own sheet's
    // full canvas height for baseline consistency WITHIN a sheet (see
    // CropSheet.ps1), so different sheets (run/jump/attack/...) ended up
    // with different amounts of empty headroom above the character. A
    // fixed center pivot then put PlayerController's transform.position -
    // which is what actually gets pinned to the ground/jump-physics height
    // - at a different point relative to the character in every state,
    // making the player appear to float above or sink into the platform
    // depending on which animation was playing. Anchoring each frame's
    // pivot to its own LOWEST non-transparent pixel instead means that
    // point (feet when grounded, whatever's lowest mid-swing/mid-air
    // otherwise) is what tracks transform.position, which is exactly what
    // a 2D character sprite's anchor should represent.
    // internal(privateではない) - プレイアブル主人公アニメーション差し替え
    // (2026-09-13)でCharacterDatabaseBuilder.csからも同じ足元Pivot自動検出
    // ロジックを再利用するため。
    internal static void ConfigureSpriteFolderImportWithFootPivot(string dir, float pixelsPerUnit)
    {
        if (!Directory.Exists(dir)) return;
        foreach (string f in Directory.GetFiles(dir, "*.png"))
        {
            string assetPath = f.Replace('\\', '/');
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) continue;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.alphaIsTransparency = true;
            // Game Feel refinement pass - explicit, not left to Unity's own
            // auto-detection: the user reported effect/decoration sprites
            // rendering as solid black/white rectangles in Game View, and
            // this is the one alpha-related import setting this file never
            // set explicitly anywhere. FromInput = "use the PNG's own alpha
            // channel" (as opposed to None/FromGrayScale).
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.filterMode = FilterMode.Bilinear;
            ApplyCustomPivot(importer, ComputeLowestContentPivotY(f));
            importer.SaveAndReimport();
        }
    }

    // Configures a single sprite file the same way (foot/lowest-point
    // pivot) - for one-off sprites like the enemy that aren't part of a
    // whole animation folder.
    // Distance Level Design Ver.1, item 3 - starting Tier data: Enemy
    // Category availability only as of Ver.1.1 (Formation availability
    // moved to each FormationData's own minDistance/maxDistance - see
    // BuildFormations below). Cumulative: each tier includes everything
    // the previous one already had, only adding new categories, per
    // "20,000mまでに基本Enemy Typeが一通り解禁される". The last tier's
    // endDistance (100,000m) is also what DistanceTierManager.CurrentTier
    // falls back to indefinitely past that point, so nothing needs a tier
    // past it.
    // ステージ別ビジュアル差し替え(2026-09-13) - 荒野街道専用の地上アート
    // (草地/土、ChatGPT生成→黒背景をしきい値透過処理→均等3分割)。天空回廊
    // はエントリを追加しない=既存の岩+雲の浮遊足場アートのまま、という
    // 設計(TerrainManager.ApplyStageThemeのコメント参照)。
    static TerrainManager.TerrainThemeSet[] BuildTerrainThemes()
    {
        // 既存のplatform_left/mid/right.pngと同じ考え方 - ソース画像の実
        // ピクセル高さをterrain.platformVisualHeight(3.5)へ割り当てる
        // PPUを算出し、既存の岩+雲テーマと物理的な見た目の高さを揃える。
        const float WastelandSourcePixelHeight = 724f;
        float wastelandPpu = WastelandSourcePixelHeight / 3.5f;
        var wastelandPlatformArt = new PlatformSpriteSet
        {
            left = LoadTiledSprite("Assets/Art/VisualStyleV1/Ground/platform_wasteland_left.png", wastelandPpu),
            mid = LoadTiledSprite("Assets/Art/VisualStyleV1/Ground/platform_wasteland_mid.png", wastelandPpu),
            right = LoadTiledSprite("Assets/Art/VisualStyleV1/Ground/platform_wasteland_right.png", wastelandPpu)
        };
        // BackgroundFollowerは常に画面を覆うようスケールし直す(cover方式)
        // ため、PPUの実際の値はアスペクト比にしか影響しない - 他の背景と
        // 同じ簡便な値でよい。
        Sprite wastelandBackground = ConfigureAndLoadSpriteWithCenterPivot("Assets/Art/Background/WastelandBackground.png", 1000f);

        // Stage01基礎見た目修整依頼(2026-09-13深夜) - マスター報告「下ルー
        // トの下側に見えている空白部分を、地面で埋める」への対応。既存の
        // platform_wasteland_mid.pngの岩下面バンドは光源が上部に偏ってお
        // り縦タイリングすると縞模様の継ぎ目が出るため流用せず、ChatGPTで
        // 均一光源・縦シームレス前提の新規岩/土断面テクスチャを生成した。
        // 同じwastelandPpuを使うことで、既存の岩下面バンドと粒感のスケー
        // ルを揃えている。
        Sprite wastelandGroundFill = LoadTiledSprite("Assets/Art/VisualStyleV1/Ground/groundfill_wasteland.png", wastelandPpu);

        // Stage01完成版要求仕様書「街道らしさ」対応(2026-09-13) - 天空回廊
        // 用の花/岩/廃墟看板(既存decorationSprites、DecorRuinsSign.png等
        // PPU 1536-3413=世界高さ約0.53-0.67)とは別に、道標/柵/木箱・樽/
        // 壊れた荷車の4種を用意し、荒野街道選択時だけDecorationScatterへ
        // 渡す(TerrainManager.TerrainThemeSet.decorationSprites参照)。
        // PPUは各画像の実クロップ高さ(px)から目標world heightへ逆算 -
        // 既存の廃墟看板より一回り大きめ(0.55〜0.85)にして、単なる草花
        // クラッターより「街道の生活感」がひと目でわかる存在感を出した。
        Sprite wastelandDecorSignpost = ConfigureAndLoadSpriteWithFootPivot("Assets/Art/Decoration/Wasteland_v1/decor_signpost.png", 524.7f);
        Sprite wastelandDecorFence = ConfigureAndLoadSpriteWithFootPivot("Assets/Art/Decoration/Wasteland_v1/decor_fence.png", 516.4f);
        Sprite wastelandDecorCrateBarrel = ConfigureAndLoadSpriteWithFootPivot("Assets/Art/Decoration/Wasteland_v1/decor_cratebarrel.png", 525f);
        Sprite wastelandDecorCart = ConfigureAndLoadSpriteWithFootPivot("Assets/Art/Decoration/Wasteland_v1/decor_cart.png", 550f);

        return new[]
        {
            new TerrainManager.TerrainThemeSet
            {
                stageId = "wasteland_road",
                platformArt = wastelandPlatformArt,
                // 接地ズレ修整(2026-09-15) - マスター報告「Player/Enemy/
                // Obstacleが地面から浮いて見える」の根本原因。platform_mid.png
                // (天空回廊)用に測定したplatformSurfaceInset(190/768)を
                // 荒野街道でも使い続けていたのが原因 - platform_wasteland_
                // mid.pngはキャンバス内の透明マージン比率が全く違う。
                // PowerShellでplatform_wasteland_mid.png(724x724)をアルファ
                // チャンネル走査し、天空回廊の測定基準(「完全に不透明になる
                // 最初の行」=platform_mid.pngでは190/768)と同じ基準を適用
                // した結果、荒野街道では行239で完全に不透明になる
                // (それ以前は岩肌のギザギザで徐々に不透明度が上がる遷移帯)。
                // TerrainManager.ApplyStageThemeがこの値をplatformArtと
                // セットで差し替える。
                platformSurfaceInset = 239f / WastelandSourcePixelHeight * 3.5f,
                // 路面/地中断面の接続見た目修整(2026-09-15) - platform_wasteland_mid.pngの
                // アルファチャンネルを行単位で走査すると、岩の不透明部分は上端(行239)
                // から始まり、下端は行555(不透明度98.1%)~行582(0%)にかけて
                // ギザギザに透明フェードしていく(岩の裂け目の縁取り表現)。
                // GroundFillの上端をキャンバス矩形の下端(旧実装)ではなく、
                // この岩の不透明部分がほぼ途切れる行558(不透明度約87%、安全
                // マージンを見て50%地点(行567)より早め)に合わせることで、
                // スラブとFillの間に空色の隙間が生じないようにする。
                groundFillTopOffset = (558f - 239f) / WastelandSourcePixelHeight * 3.5f,
                groundSprite = null, // platformArtが有効な間は未使用(フォールバック専用)
                groundColor = Color.white, // platformArt使用中は各ピースがColor.white固定で描画されるため実質未参照
                backgroundSprite = wastelandBackground,
                // 基礎品質修整(2026-09-14) - マスター報告「背景の情報量が
                // 強く、Player/Enemy/Objectが埋もれる」への対応。新規アート
                // 生成やシェーダーでのBlur/彩度調整はせず、既存背景への
                // 乗算ティントのみで明度・コントラストを控えめに落とす
                // (約15-20%減、若干寒色寄り) - 「消す」のではなく前景を
                // 相対的に目立たせるための最小限の調整。
                backgroundTint = new Color(0.8f, 0.82f, 0.85f, 1f),
                decorationSprites = new[] { wastelandDecorSignpost, wastelandDecorFence, wastelandDecorCrateBarrel, wastelandDecorCart },
                groundFillSprite = wastelandGroundFill,
                // Stage01次段階調整(2026-09-16), item5 - マスター報告「下側を
                // 地面で埋めたことで、画面下部の岩断面が大きく占有し窮屈に
                // 見える」への対応。groundFillDepth(見せる高さ)はワイドな
                // 画面比率でのカメラ可視範囲をぎりぎりカバーする実測値なので
                // そのまま維持し、代わりにこの帯へ乗算するティントで濃さ/
                // コントラストだけを約25-30%控えめにする(「再び空色の帯を
                // 出さない」ため高さ側には触れない、という制約に対応)。
                groundFillTint = new Color(0.72f, 0.7f, 0.68f, 1f),
                // ルート構造再調整(2026-09-13) - マスター提供の参考画像を
                // 仕様図として、上ルート/下ルートが分岐→並走→合流する
                // Route Branchシステムを荒野街道だけで有効化する。天空回廊
                // はこのフラグ自体を持たない(既定false)ので無改造のまま。
                enableRouteBranch = true,
                branchMarkerSprite = wastelandDecorSignpost,
            }
        };
    }

    static DistanceTier[] BuildDistanceTiers()
    {
        // Stage01 荒野街道 最小実装(2026-09-13) - マスター指示「敵はゴブリン
        // /鳥のみ」に対応するため、0-1000mのTutorial帯にもFlying(=鳥の
        // 代役、EnemyDatabaseBuilder.Specs参照)を追加した。Irregular等は
        // 引き続き1000m以降まで解禁しない(1stステージは敵種を増やしすぎ
        // ない、という明示指示どおり)。
        var tutorial = new[] { EnemyCategory.Normal, EnemyCategory.Flying };
        var tier1Types = new[] { EnemyCategory.Normal, EnemyCategory.Flying, EnemyCategory.Irregular };
        var tier2Types = new[] { EnemyCategory.Normal, EnemyCategory.Flying, EnemyCategory.Irregular, EnemyCategory.Shooter, EnemyCategory.Heavy, EnemyCategory.Chaser };
        var tier3Types = new[] { EnemyCategory.Normal, EnemyCategory.Flying, EnemyCategory.Irregular, EnemyCategory.Shooter, EnemyCategory.Heavy, EnemyCategory.Chaser, EnemyCategory.Rusher };

        return new[]
        {
            new DistanceTier { tierName = "0-1000m Tutorial", startDistance = 0f, endDistance = 1000f, availableEnemyTypes = tutorial },
            new DistanceTier { tierName = "1000-5000m", startDistance = 1000f, endDistance = 5000f, availableEnemyTypes = tier1Types },
            new DistanceTier { tierName = "5000-10000m", startDistance = 5000f, endDistance = 10000f, availableEnemyTypes = tier2Types },
            new DistanceTier { tierName = "10000-20000m", startDistance = 10000f, endDistance = 20000f, availableEnemyTypes = tier3Types },
            new DistanceTier { tierName = "20000-100000m", startDistance = 20000f, endDistance = 100000f, availableEnemyTypes = tier3Types }
        };
    }

    // Distance Level Design Ver.1.1, item 2 - the actual pre-defined
    // Formation shapes, as real World-Unit SpawnPoint offsets (this is the
    // core fix for "Formationが意図した形になっていない" - see
    // DistanceTierManager's own class comment). Every xOffset is >=0 by
    // convention (each Formation only ever grows RIGHTWARD from its own
    // anchor - TerrainManager.RequestFlatRun only ever reserves forward,
    // never backward into unverified terrain). Spacing between points is
    // chosen generously (>=1.1 units, comfortably more than any enemy's own
    // ~1 unit collider width) so Enemy同士 never overlap.
    static FormationData[] BuildFormations()
    {
        FormationSpawnPoint P(float x, float y, EnemyRole role) => new FormationSpawnPoint { xOffset = x, yOffset = y, role = role };

        // Horizontal Line - "5～10体程度...Playerが攻撃しながら連続撃破し
        // やすい程度" - 7 points, 1.5 unit spacing (comfortably chainable
        // with the player's own attack range/combo timing).
        var horizontalLine = new FormationData
        {
            formationId = "horizontal_line",
            formationType = EnemyFormationType.HorizontalLine,
            weight = 2.5f,
            minDistance = 0f, // item 2 - "1000mまでに最低一度は必ず出現" - available from distance 0 so the very first eligible chunk can already roll it
            maxDistance = 999999f,
            spawnPoints = new[]
            {
                P(0f, 0f, EnemyRole.Any), P(1.5f, 0f, EnemyRole.Any), P(3f, 0f, EnemyRole.Any), P(4.5f, 0f, EnemyRole.Any),
                P(6f, 0f, EnemyRole.Any), P(7.5f, 0f, EnemyRole.Any), P(9f, 0f, EnemyRole.Any)
            }
        };

        // Vertical Line - "高さ方向へ...通常Jump/Double Jumpで攻略可能な範
        // 囲、画面上端へはみ出さない" - 4 points stacked at a modest 0.9
        // unit step (screenTopMargin/DistanceTierManager still clamps any
        // that would exceed the camera regardless of this authored height).
        var verticalLine = new FormationData
        {
            formationId = "vertical_line",
            formationType = EnemyFormationType.VerticalLine,
            weight = 1f,
            minDistance = 1000f,
            maxDistance = 999999f,
            spawnPoints = new[] { P(0f, 0f, EnemyRole.Any), P(0f, 0.9f, EnemyRole.Any), P(0f, 1.8f, EnemyRole.Any), P(0f, 2.7f, EnemyRole.Any) }
        };

        // Diagonal Up - "XとYを段階的に増加、Jumpしながら斬っていく流れ".
        var diagonalUp = new FormationData
        {
            formationId = "diagonal_up",
            formationType = EnemyFormationType.DiagonalUp,
            weight = 1.2f,
            minDistance = 1000f,
            maxDistance = 999999f,
            spawnPoints = new[] { P(0f, 0f, EnemyRole.Any), P(1.6f, 0.8f, EnemyRole.Any), P(3.2f, 1.6f, EnemyRole.Any), P(4.8f, 2.4f, EnemyRole.Any), P(6.4f, 3.2f, EnemyRole.Any) }
        };

        // Cluster - "多少密集しているが完全にCollider同士が重ならない" - a
        // tight but non-overlapping diamond, 1.1-1.3 unit spacing.
        var cluster = new FormationData
        {
            formationId = "cluster",
            formationType = EnemyFormationType.Cluster,
            weight = 1.2f,
            minDistance = 1000f,
            maxDistance = 999999f,
            spawnPoints = new[] { P(0f, 0f, EnemyRole.Any), P(1.2f, 0f, EnemyRole.Any), P(0.6f, 0f, EnemyRole.Any), P(1.8f, 0f, EnemyRole.Any) }
        };

        // Ground + Air - "別Y座標に明確に配置、空中Enemyが地面へ埋まらない"
        // - 2 ground (y=0) + 2 air (y=1.8, well above flyingMinHeight).
        // Stage01 荒野街道 最小実装(2026-09-13) - minDistanceを5000f→0fへ
        // 変更し、0m(荒野街道)から利用可能にした。EnemyRole.Anyは意図的に
        // Flyingを含まない(DistanceTierManager.ResolveRole既定分岐 -
        // GroundLikeCategoriesにFlyingが無い)ため、「ゴブリンと鳥をたまに
        // 混ぜる」を実現する唯一の既存手段がこのGroundAir(Normal+Flyingを
        // 明示的な別ロールとして両方持つ)formationだった - 新規Formation
        // コードを足さず、既存の仕組みをそのまま早期解禁するだけで済んだ。
        var groundAir = new FormationData
        {
            formationId = "ground_air",
            formationType = EnemyFormationType.GroundAir,
            weight = 1.3f,
            minDistance = 0f,
            maxDistance = 999999f,
            spawnPoints = new[] { P(0f, 0f, EnemyRole.Normal), P(3f, 0f, EnemyRole.Normal), P(1.5f, 1.8f, EnemyRole.Flying), P(4.5f, 1.8f, EnemyRole.Flying) }
        };

        // Frontline + Shooter - "Normal/Heavyが前衛、Shooterは前衛の少し後
        // 方...画面外から一方的に撃つ配置は禁止" - Shooter sits only 2.5
        // units behind the frontline (well within its own shooterRange), so
        // it's always on-screen alongside the melee it's protecting.
        var frontlineShooter = new FormationData
        {
            formationId = "frontline_shooter",
            formationType = EnemyFormationType.FrontlineShooter,
            weight = 1.4f,
            minDistance = 5000f,
            maxDistance = 999999f,
            spawnPoints = new[] { P(0f, 0f, EnemyRole.Frontline), P(1.6f, 0f, EnemyRole.Frontline), P(3.2f, 0f, EnemyRole.Shooter) }
        };

        // Heavy + Normal - "Heavyを壁として使い、その周囲にNormalを配置".
        var heavyNormal = new FormationData
        {
            formationId = "heavy_normal",
            formationType = EnemyFormationType.HeavyNormal,
            weight = 1.4f,
            minDistance = 5000f,
            maxDistance = 999999f,
            spawnPoints = new[] { P(1.5f, 0f, EnemyRole.Heavy), P(0f, 0f, EnemyRole.Normal), P(3f, 0f, EnemyRole.Normal) }
        };

        // Rush - a tight burst of Chaser/Rusher-role members; the actual
        // "rush" character comes entirely from THEIR OWN Behavior
        // (EnemySpecialBehavior.UpdateRusher's telegraph->dash), not from
        // the spawn shape itself, so this is just a small tight cluster.
        var rush = new FormationData
        {
            formationId = "rush",
            formationType = EnemyFormationType.Rush,
            weight = 1.5f,
            minDistance = 10000f,
            maxDistance = 999999f,
            spawnPoints = new[] { P(0f, 0f, EnemyRole.Any), P(1.3f, 0f, EnemyRole.Any), P(2.6f, 0f, EnemyRole.Any) }
        };

        var single = new FormationData
        {
            formationId = "single",
            formationType = EnemyFormationType.Single,
            weight = 2f,
            minDistance = 0f,
            maxDistance = 999999f,
            spawnPoints = new[] { P(0f, 0f, EnemyRole.Any) }
        };

        var smallGroup = new FormationData
        {
            formationId = "small_group",
            formationType = EnemyFormationType.SmallGroup,
            weight = 2f,
            minDistance = 0f,
            maxDistance = 999999f,
            spawnPoints = new[] { P(0f, 0f, EnemyRole.Any), P(1.4f, 0f, EnemyRole.Any) }
        };

        return new[] { single, smallGroup, horizontalLine, verticalLine, cluster, diagonalUp, groundAir, frontlineShooter, heavyNormal, rush };
    }

    static Sprite ConfigureAndLoadSpriteWithFootPivot(string path, float pixelsPerUnit)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.alphaIsTransparency = true;
            // Game Feel refinement pass - explicit, not left to Unity's own
            // auto-detection: the user reported effect/decoration sprites
            // rendering as solid black/white rectangles in Game View, and
            // this is the one alpha-related import setting this file never
            // set explicitly anywhere. FromInput = "use the PNG's own alpha
            // channel" (as opposed to None/FromGrayScale).
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.filterMode = FilterMode.Bilinear;
            ApplyCustomPivot(importer, ComputeLowestContentPivotY(path));
            // 基礎品質修整 続報(2026-09-14) - マスター報告「オブジェクトが
            // 地面から浮いて見える」の実機動画確認で発見: 障害物(特に
            // 双剣士の攻撃と違い矩形でない、木の柵のような穴の多い複雑な
            // シルエット)が接地点の少し上に浮いて描画されていた。原因は
            // 既定のMesh Type=Tightにある - Unityのポリゴン簡略化
            // (Tessellation)が、ピボット計算(ComputeLowestContentPivotY、
            // 生のアルファ値を直接スキャン)が捉えた最下端の細い突起(柵の
            // 脚等)をメッシュ生成時に削ってしまうことがあり、その場合
            // 「ピボットの位置」と「実際に描画されるメッシュの最下端」が
            // 一致しなくなる - ピボット基準では正しく接地しているのに、
            // 見た目のメッシュはそこまで届かず浮いて見える。Full Rectに
            // 切り替えると単純な矩形+テクスチャのアルファそのものを描画
            // するため、簡略化による誤差が原理的に発生しない。
            ApplySpriteMeshTypeFullRect(importer);
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // 基礎品質修整 続報(2026-09-14) - マスター報告「背景画像が画面の上半分
    // にしか見えない」の実機動画確認で発見: WastelandBackground.png/
    // NightFloatingIsland.pngが、地上オブジェクト用のConfigureAndLoad
    // SpriteWithFootPivot(接地点=画像下端付近にピボットを置く)で読み込ま
    // れていた。BackgroundFollowerは「スプライトの中心をカメラ位置に合わ
    // せ、cover方式で拡大縮小する」設計のため、ピボットが下端寄りだと
    // 背景全体がカメラより大きく上へずれてしまい、画面下半分が覆われずに
    // 背景の外側(透明/クリアカラー)が見えてしまっていた - 元から無改造
    // だった既定の"background.png"(AssetDatabase.LoadAssetAtPathで素の
    // まま読み込み=Unity既定のCenter pivotのまま)には無かった問題。
    // 背景用に、ピボットをCenterのまま維持する専用ローダーを用意した。
    static Sprite ConfigureAndLoadSpriteWithCenterPivot(string path, float pixelsPerUnit)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.alphaIsTransparency = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.filterMode = FilterMode.Bilinear;
            ApplyCustomPivot(importer, new Vector2(0.5f, 0.5f));
            ApplySpriteMeshTypeFullRect(importer);
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // TextureImporter doesn't expose spriteAlignment directly - it has to
    // go through TextureImporterSettings.
    static void ApplyCustomPivot(TextureImporter importer, float pivotY)
    {
        ApplyCustomPivot(importer, new Vector2(0.5f, pivotY));
    }

    // 上下攻撃アニメーション差し替え(2026-09-08) - X も明示指定できる版。
    // 既存のApplyCustomPivot(pivotYのみ)はX=0.5固定 - 供給元シートが
    // "キャラクターが各コマで水平方向に完全に中央揃えされている"前提に依
    // 存しており、その前提が崩れる(このパスの上/下攻撃シートは剣の振り
    // 幅に応じて各コマの実効幅が変わり、キャラクター本体の水平位置が0.5
    // からズレる)と再生中に本体がガクガク横移動して見える - マスターの
    // 依頼書自身が名指しで警告していた症状そのもの。
    static void ApplyCustomPivot(TextureImporter importer, Vector2 pivot)
    {
        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = pivot;
        importer.SetTextureSettings(settings);
    }

    // Reads the raw PNG bytes directly (not through the AssetDatabase, so
    // no readable-texture import setting is needed) and returns the
    // normalized Y (0 = bottom, 1 = top, matching Unity's own pivot
    // convention) of the lowest row containing any non-transparent pixel.
    static float ComputeLowestContentPivotY(string filePath)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(bytes);

        int w = tex.width, h = tex.height;
        Color32[] pixels = tex.GetPixels32();
        int lowestY = -1;
        for (int y = 0; y < h && lowestY < 0; y++)
        {
            int rowStart = y * w;
            for (int x = 0; x < w; x += 2)
            {
                if (pixels[rowStart + x].a > 15) { lowestY = y; break; }
            }
        }
        Object.DestroyImmediate(tex);

        return lowestY < 0 ? 0.5f : Mathf.Clamp01((float)lowestY / h);
    }

    // 上下攻撃アニメーション差し替え(2026-09-08) - 足元PivotのX,Y両方版。
    // 「最下段の非透明行」だけでなく、その最下段付近(下から高さの4%、
    // 最低6px)の非透明ピクセルのX平均も求め、その帯における実際の足の
    // 水平位置をXピボットに使う - ComputeLowestContentPivotYと違い、剣の
    // 振り幅でシート内キャラクターの水平占有位置が変わる新素材向け。
    static Vector2 ComputeLowestContentPivotXY(string filePath)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(bytes);

        int w = tex.width, h = tex.height;
        Color32[] pixels = tex.GetPixels32();
        int lowestY = -1;
        for (int y = 0; y < h && lowestY < 0; y++)
        {
            int rowStart = y * w;
            for (int x = 0; x < w; x++)
            {
                if (pixels[rowStart + x].a > 15) { lowestY = y; break; }
            }
        }
        if (lowestY < 0) { Object.DestroyImmediate(tex); return new Vector2(0.5f, 0.5f); }

        // Bugfix (this pass) - GetPixels32 is bottom-up (y=0 at the bottom
        // edge), so lowestY (found scanning UPWARD from y=0) is already the
        // bottom-most content row - the band must extend from there toward
        // LARGER y (up into the body) to sample "the bottom N rows of actual
        // content", not smaller y (which runs off the bottom edge into the
        // empty margin below the foot and picked up only a stray sliver of
        // whichever single pixel column happened to sit exactly at lowestY -
        // the original version of this method had this backwards, producing
        // wildly-off pivotX values caught by cross-checking against an
        // independent top-down reference scan before shipping).
        int bandHeight = Mathf.Max(6, Mathf.RoundToInt(h * 0.04f));
        int yEnd = Mathf.Min(h - 1, lowestY + bandHeight - 1);
        double sumX = 0.0;
        int count = 0;
        for (int y = lowestY; y <= yEnd; y++)
        {
            int rowStart = y * w;
            for (int x = 0; x < w; x++)
            {
                if (pixels[rowStart + x].a > 15) { sumX += x; count++; }
            }
        }
        Object.DestroyImmediate(tex);

        float pivotX = count > 0 ? Mathf.Clamp01((float)(sumX / count) / w) : 0.5f;
        float pivotY = Mathf.Clamp01((float)lowestY / h);
        return new Vector2(pivotX, pivotY);
    }

    // 上下攻撃アニメーション差し替え(2026-09-08) - 足元(接地/自動計算)で
    // はなく、胴体・剣先など「目視で選んだ基準点」を各ファイルへ個別に割
    // り当てる版。Directory.GetFiles+Sortの並び(LoadSpriteSequenceが読む
    // 並びと同じ)にpivots配列を対応させる - 空中上攻撃(胴体基準)や下降
    // 攻撃の3コマ(胴体/接地点基準)のように、自動検出可能な「最下段」基準
    // が意味を持たないケース向け。
    static void ConfigureSpriteFolderImportWithManualPivots(string dir, float pixelsPerUnit, Vector2[] pivots)
    {
        if (!Directory.Exists(dir)) return;
        string[] files = Directory.GetFiles(dir, "*.png");
        System.Array.Sort(files);
        for (int i = 0; i < files.Length; i++)
        {
            string assetPath = files[i].Replace('\\', '/');
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) continue;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.alphaIsTransparency = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.filterMode = FilterMode.Bilinear;
            Vector2 pivot = i < pivots.Length ? pivots[i] : new Vector2(0.5f, 0.5f);
            ApplyCustomPivot(importer, pivot);
            importer.SaveAndReimport();
        }
    }

    // 上下攻撃アニメーション差し替え(2026-09-08) - ConfigureSpriteFolder
    // ImportWithFootPivotのX,Y自動版(ComputeLowestContentPivotXY使用)。
    // 地上上攻撃(足が地面に接地したまま振るモーション)のように、自動の
    // 足元検出がそのまま正しい基準になる場合に使う。
    // internal(privateではない) - プレイアブル主人公アニメーション差し替え
    // (2026-09-13)でCharacterDatabaseBuilder.csからも再利用するため。
    internal static void ConfigureSpriteFolderImportWithFootPivotXY(string dir, float pixelsPerUnit)
    {
        if (!Directory.Exists(dir)) return;
        foreach (string f in Directory.GetFiles(dir, "*.png"))
        {
            string assetPath = f.Replace('\\', '/');
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) continue;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.alphaIsTransparency = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.filterMode = FilterMode.Bilinear;
            ApplyCustomPivot(importer, ComputeLowestContentPivotXY(f));
            importer.SaveAndReimport();
        }
    }

    // 敵Runnerアニメーションのズレ修正(2026-09-15) - マスター報告「Runner
    // のアニメーションのズレ」の原因調査。RunnerRunの5コマは横長のスプライ
    // トシートから個別に切り出されたもので、実際のアルファコンテンツの縦
    // 幅がコマごとに298〜382px(約28%の差)とバラバラだった。従来の
    // ConfigureSpriteFolderImportWithFootPivot/XYはフォルダ全体で単一の
    // pixelsPerUnitを共有するため(EnemyDatabaseBuilder.chaser_runnerの
    // コメントに記録済みの既知の限界、実測103〜132%の個体差)、足元の接地
    // 位置こそ揃っていても、コマが切り替わるたびにキャラクター全体の背丈
    // (頭の高さ)が伸び縮みして見えていた - これが「アニメーションのズレ」
    // の正体。
    // 対処: フォルダ共有ではなく「コマ個別」のpixelsPerUnitを、そのコマ
    // 自身のアルファコンテンツ実測高さから逆算する(targetWorldHeightは
    // 全コマ共通の出力先ワールド高さ)。これにより全コマが常に同じワール
    // ド高さで描画されるようになり、伸び縮みが原理的に無くなる。足元
    // Pivot(X,Y自動検出)は既存のConfigureSpriteFolderImportWithFootPivotXY
    // と同じロジックをそのまま使うため、接地感には影響しない。
    internal static void ConfigureSpriteFolderImportWithFootPivotUniformSize(string dir, float targetWorldHeight)
    {
        if (!Directory.Exists(dir)) return;
        foreach (string f in Directory.GetFiles(dir, "*.png"))
        {
            string assetPath = f.Replace('\\', '/');
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) continue;

            int contentHeightPx = MeasureContentHeightPixels(f);
            float ppu = targetWorldHeight > 0.001f && contentHeightPx > 0 ? contentHeightPx / targetWorldHeight : 1000f;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = ppu;
            importer.alphaIsTransparency = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.filterMode = FilterMode.Bilinear;
            ApplyCustomPivot(importer, ComputeLowestContentPivotXY(f));
            importer.SaveAndReimport();
        }
    }

    // ConfigureSpriteFolderImportWithFootPivotUniformSize専用ヘルパー -
    // 非透明ピクセルが存在する最上段〜最下段の行数(=そのコマ自身の実際の
    // キャラクター高さ、px)を返す。ComputeLowestContentPivotY/XYと同じ
    // アルファ走査方式だが、最下段だけでなく最上段も求める点が異なる。
    static int MeasureContentHeightPixels(string filePath)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(bytes);

        int w = tex.width, h = tex.height;
        Color32[] pixels = tex.GetPixels32();
        int lowestY = -1, highestY = -1;
        for (int y = 0; y < h; y++)
        {
            int rowStart = y * w;
            bool rowHasContent = false;
            for (int x = 0; x < w; x++)
            {
                if (pixels[rowStart + x].a > 15) { rowHasContent = true; break; }
            }
            if (rowHasContent)
            {
                if (lowestY < 0) lowestY = y;
                highestY = y;
            }
        }
        Object.DestroyImmediate(tex);

        return lowestY < 0 ? h : (highestY - lowestY + 1);
    }

    // お嬢様騎士Run表示基準統一(2026-09-13) - 通常Run中にキャラ全体が
    // フレームごとに上下へガクガク跳ねて見える不具合の修正。原因:
    // ConfigureSpriteFolderImportWithFootPivotXY(上記)はフレームごとに
    // 「そのフレーム自身の最下段コンテンツ行」を個別にpivotY化していた -
    // NobleLadyRun_v1の各フレームはfind_best_cuts.ps1で横方向のみ切り出し
    // たもの(全フレーム高さ724pxが共通=同じ座標系を共有)なので、脚が
    // 地面に着いているコマと、歩幅の合間で脚が浮いているコマとで「その
    // コマ自身の最下点」の絵の中での高さが本来かなり異なる。それを毎回
    // そのコマ自身の最下点に合わせて接地させてしまうと、脚が浮いている
    // コマだけキャラ全体が不自然に持ち上がって見える(=今回の症状)。
    // 正しくは「実際に足が最も深く地面へ接地しているコマ」1つを基準に、
    // 全コマ共通の接地ラインを1本だけ使うこと - 脚が浮いているコマは、
    // その分だけ足が地面から離れて描かれて当然良い(それが本来の走行の
    // 弾みそのもの)。Xは従来どおりコマごとの最下段付近の重心を使う(歩幅
    // による自然な左右のブレは今回問題視されていない)。
    // 対象はお嬢様騎士Runのみ - ConfigureSpriteFolderImportWithFootPivotXY
    // 自体(PlayerUpAttackGround_v1等の既存利用箇所)には一切触れないため、
    // 黒剣士や他のState(Jump/Land/Attack)の表示には影響しない。
    internal static void ConfigureSpriteFolderImportWithSharedGroundPivot(string dir, float pixelsPerUnit)
    {
        if (!Directory.Exists(dir)) return;
        string[] files = Directory.GetFiles(dir, "*.png");
        System.Array.Sort(files);
        if (files.Length == 0) return;

        // お嬢様騎士Runモーション読みやすさ改善(2026-09-13) - 4コマ目の
        // 追加生成で、フレームごとにソース画像の解像度が異なる(ChatGPTの
        // 別セッションで生成した新規コマを、既存コマと同じキャラクター
        // 表示サイズになるよう別途リサイズしてから追加した)ケースが
        // 出てきたため、「下から何割」という比率(=キャンバス高さが全コマ
        // 共通という前提)ではなく、「下から何ピクセル」という絶対値を
        // 共有してからフレームごとの高さで割ってpivotYへ変換するよう修正。
        // キャンバス高さが全コマ共通だった場合(従来のケース)は数学的に
        // 同じ結果になる。
        int sharedGroundPixels = int.MaxValue;
        var heightByFile = new System.Collections.Generic.Dictionary<string, int>();
        foreach (string f in files)
        {
            int lowestY = LowestContentRowPixels(f, out int height);
            heightByFile[f] = height;
            if (lowestY >= 0 && lowestY < sharedGroundPixels) sharedGroundPixels = lowestY;
        }
        if (sharedGroundPixels == int.MaxValue) sharedGroundPixels = 0;

        foreach (string f in files)
        {
            string assetPath = f.Replace('\\', '/');
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) continue;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.alphaIsTransparency = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.filterMode = FilterMode.Bilinear;
            float pivotX = ComputeLowestContentPivotXY(f).x;
            int height = heightByFile[f];
            float pivotY = height > 0 ? Mathf.Clamp01((float)sharedGroundPixels / height) : 0.5f;
            ApplyCustomPivot(importer, new Vector2(pivotX, pivotY));
            importer.SaveAndReimport();
        }
    }

    // ConfigureSpriteFolderImportWithSharedGroundPivot専用のヘルパー -
    // ComputeLowestContentPivotXYと同じアルファ走査だが、Xは使わず「下から
    // 何ピクセルの位置に最初の非透明ピクセルがあるか」(と画像の高さ)を返す。
    static int LowestContentRowPixels(string filePath, out int height)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(bytes);

        int w = tex.width, h = tex.height;
        height = h;
        Color32[] pixels = tex.GetPixels32();
        int lowestY = -1;
        for (int y = 0; y < h && lowestY < 0; y++)
        {
            int rowStart = y * w;
            for (int x = 0; x < w; x++)
            {
                if (pixels[rowStart + x].a > 15) { lowestY = y; break; }
            }
        }
        Object.DestroyImmediate(tex);

        return lowestY;
    }

    // 双剣士/お嬢様騎士Run頭基準ピボット化(2026-09-13深夜) - マスター
    // 指摘「画像がブレる理由がわかってきた、頭を中心にアニメーションする
    // ことは可能か」に対応。
    //
    // 原理: 足元基準(ConfigureSpriteFolderImportWithSharedGroundPivot)は
    // 「実際に足が最も深く接地しているコマ」を基準に全コマ共通の接地
    // ラインを1本使うことで、脚が浮いているコマでもキャラ全体が不自然に
    // 持ち上がらないようにしていた。これは「足の接地位置さえ揃えれば
    // 良い」という前提では正しいが、AI生成素材のようにコマごとの頭身
    // バランスが微妙に揺れる場合、足元を固定した結果として頭部側にその
    // 揺れがそのまま「ブレ」として現れる(視線は自然と顔・頭を追うため、
    // この部分のブレが最も目につきやすい)。
    //
    // 重要な注意(単純な上下反転では済まない理由) - Root(PlayerControllerの
    // 乗る本体)は常にスプライトの"足元"(pivotYが0に近い値)に来る設計
    // (CreatePlayerのコメント「Root sitting at the sprite's FOOT」参照)。
    // Jump/Attack等の他StateはすべてこのFoot Pivot前提のまま(このメソッド
    // はRunのみに使う想定)なので、もしpivotYを単純に「頭頂基準」(1に近い
    // 値)にしてしまうと、Rootの位置は変わらないままスプライトの表示だけ
    // 「頭がRoot位置(=地面の高さ)に来る」形になり、キャラが地面に頭まで
    // 埋まって見える大穴になる上、Run⇔Jump/Attackの状態切り替えの瞬間に
    // キャラの表示位置が体1つぶんガクッと飛ぶ(ここは絶対に避けたい)。
    //
    // 正しい実装: pivotYの値そのものは他Stateと同じ「0に近い、足元寄り」
    // の範囲に保ったまま、その足元基準点を「実際のそのコマの最下段ピクセル」
    // ではなく「頭の位置から逆算した仮想の接地ライン」に置き換える。
    // 具体的には、①ConfigureSpriteFolderImportWithSharedGroundPivotと
    // 同じ基準コマ(足が最も深く接地しているコマ)の「頭頂〜接地点の
    // ピクセル距離」を基準身長(standingSpanPixels)として求め、②各コマの
    // 頭頂位置からstandingSpanPixelsぶん下がった位置を「このコマの仮想
    // 接地ピクセル」として使う。これにより見た目の位置レンジは従来の
    // 足元基準と同じ(pivotYはごく小さい値のまま)でありながら、実際に
    // 揃うのは頭の高さになる - 揺れ(ブレ)が足元側(=通常の走行の弾みと
    // して自然に見える)へ移る。
    internal static void ConfigureSpriteFolderImportWithSharedHeadPivot(string dir, float pixelsPerUnit)
    {
        if (!Directory.Exists(dir)) return;
        string[] files = Directory.GetFiles(dir, "*.png");
        System.Array.Sort(files);
        if (files.Length == 0) return;

        int referenceLowestY = int.MaxValue;
        string referenceFile = null;
        var lowestYByFile = new System.Collections.Generic.Dictionary<string, int>();
        var highestYByFile = new System.Collections.Generic.Dictionary<string, int>();
        var heightByFile = new System.Collections.Generic.Dictionary<string, int>();
        foreach (string f in files)
        {
            int lowestY = LowestContentRowPixels(f, out int height);
            int highestY = HighestContentRowPixels(f);
            lowestYByFile[f] = lowestY;
            highestYByFile[f] = highestY;
            heightByFile[f] = height;
            if (lowestY >= 0 && lowestY < referenceLowestY) { referenceLowestY = lowestY; referenceFile = f; }
        }
        // 基準コマ(足が最も深く接地しているコマ)自身の頭頂〜接地点の
        // ピクセル距離を「基準身長」とする - 全コマ共通のPPU/スケールで
        // 生成済みのフォルダである前提のため、この絶対ピクセル値がそのまま
        // 他のコマにも通用する。
        int standingSpanPixels = 0;
        if (referenceFile != null) standingSpanPixels = Mathf.Max(0, highestYByFile[referenceFile] - lowestYByFile[referenceFile]);

        foreach (string f in files)
        {
            string assetPath = f.Replace('\\', '/');
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) continue;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.alphaIsTransparency = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.filterMode = FilterMode.Bilinear;
            // Run頭基準ピボット改善(2026-09-13深夜、続き) - マスター報告
            // 「まだ頭が前後に動いている」への対応。前回パスではYのみ頭
            // 基準化し、X(左右)は従来どおり足元付近の重心のままにしていた
            // が、走行ポーズは左右の脚が大きく開くため、足元basisの重心は
            // コマごとに大きく左右へブレる(=キャラ全体が前後(画面左右)に
            // 揺れて見える、まさに今回の報告内容)。頭部(上端付近の帯)は
            // 脚ほど開かないため、Xも頭基準の重心に切り替える。
            float pivotX = ComputeHighestContentPivotX(f);
            int height = heightByFile[f];
            // このコマの頭頂位置から基準身長ぶん下げた「仮想接地ピクセル」
            // をpivotYへ変換する(ConfigureSpriteFolderImportWithShared
            // GroundPivotと同じ「絶対ピクセル÷このコマの高さ」の式)。
            int virtualGroundPixels = highestYByFile[f] - standingSpanPixels;
            float pivotY = height > 0 ? Mathf.Clamp01((float)virtualGroundPixels / height) : 0.5f;
            ApplyCustomPivot(importer, new Vector2(pivotX, pivotY));
            importer.SaveAndReimport();
        }
    }

    // ConfigureSpriteFolderImportWithSharedHeadPivot専用のヘルパー -
    // ComputeLowestContentPivotXYのX計算部分の上下反転版。「上端付近の帯
    // (頭部)の非透明ピクセルの重心X」を返す。ComputeLowestContentPivotXY
    // と同じ「見つけた端の行からband幅ぶん内側(=下)へ向かってサンプル
    // する」考え方を上下反転して適用。
    static float ComputeHighestContentPivotX(string filePath)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(bytes);

        int w = tex.width, h = tex.height;
        Color32[] pixels = tex.GetPixels32();
        int highestY = -1;
        for (int y = h - 1; y >= 0 && highestY < 0; y--)
        {
            int rowStart = y * w;
            for (int x = 0; x < w; x++)
            {
                if (pixels[rowStart + x].a > 15) { highestY = y; break; }
            }
        }
        if (highestY < 0) { Object.DestroyImmediate(tex); return 0.5f; }

        int bandHeight = Mathf.Max(6, Mathf.RoundToInt(h * 0.04f));
        int yStart = Mathf.Max(0, highestY - bandHeight + 1);
        double sumX = 0.0;
        int count = 0;
        for (int y = yStart; y <= highestY; y++)
        {
            int rowStart = y * w;
            for (int x = 0; x < w; x++)
            {
                if (pixels[rowStart + x].a > 15) { sumX += x; count++; }
            }
        }
        Object.DestroyImmediate(tex);

        return count > 0 ? Mathf.Clamp01((float)(sumX / count) / w) : 0.5f;
    }

    // ConfigureSpriteFolderImportWithSharedHeadPivot専用のヘルパー -
    // LowestContentRowPixelsの上下反転版。「下から何ピクセルの位置に
    // 最後の(=最も上にある)非透明ピクセル行(頭頂/髪やマントの最高点)が
    // あるか」を返す(heightはLowestContentRowPixels側で取得済みのため
    // ここでは返さない)。
    static int HighestContentRowPixels(string filePath)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(bytes);

        int w = tex.width, h = tex.height;
        Color32[] pixels = tex.GetPixels32();
        int highestY = -1;
        for (int y = h - 1; y >= 0 && highestY < 0; y--)
        {
            int rowStart = y * w;
            for (int x = 0; x < w; x++)
            {
                if (pixels[rowStart + x].a > 15) { highestY = y; break; }
            }
        }
        Object.DestroyImmediate(tex);

        return highestY;
    }

    // internal - 上のConfigureSpriteFolderImportWithFootPivotと同じ理由。
    internal static Sprite[] LoadSpriteSequence(string dir)
    {
        if (!Directory.Exists(dir)) return new Sprite[0];

        string[] files = Directory.GetFiles(dir, "*.png");
        System.Array.Sort(files);

        var list = new List<Sprite>();
        foreach (string f in files)
        {
            string assetPath = f.Replace('\\', '/');
            Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (s != null) list.Add(s);
        }
        return list.ToArray();
    }

    static Sprite EnsureSquareSprite()
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        if (existing != null) return existing;

        string dir = Path.GetDirectoryName(SpritePath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();

        File.WriteAllBytes(SpritePath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(SpritePath, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(SpritePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = size;
        importer.filterMode = FilterMode.Point;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
    }

    static void EnsureTag(string tag)
    {
        SerializedObject tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty tagsProp = tagManager.FindProperty("tags");

        for (int i = 0; i < tagsProp.arraySize; i++)
        {
            if (tagsProp.GetArrayElementAtIndex(i).stringValue == tag) return;
        }

        tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
        tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tag;
        tagManager.ApplyModifiedProperties();
    }
}
