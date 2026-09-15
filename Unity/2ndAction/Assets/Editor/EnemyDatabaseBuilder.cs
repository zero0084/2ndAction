using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Creates (once) the EnemyDefinition assets under Assets/Resources/Enemies/
// - mirrors CardDatabaseBuilder/UnlockDatabaseBuilder, including "never
// overwrite an existing asset" so hand-tuned fields (tint, movementType,
// and now category/hpMultiplier/etc.) survive a rebuild.
//
// Must run AFTER SceneBuilder has already configured every species' sprite
// import settings (foot pivot / PPU, Root-Visual sizing) - it just
// references those already-configured Sprites by path. Distance Level
// Design Ver.1 - the 6 new species (Flying/Irregular/Shooter/Heavy/Chaser/
// Rusher) added below self-load their own sprite by path the same way the
// goblin already did, so a scene rebuild before the corresponding PNG
// exists on disk just leaves that species' `sprite` null (AssetDatabase.
// LoadAssetAtPath is null-safe) rather than failing the whole build -
// EnemyDatabase/TerrainManager already skip a species with no sprite (see
// PickRandomUnlocked's caller-side fallback).
public static class EnemyDatabaseBuilder
{
    const string EnemiesFolder = "Assets/Resources/Enemies";
    const string GoblinSpritePath = "Assets/Art/Enemy/enemy_v1.png";

    // Distance Level Design Ver.1 - new species art, imported by
    // SceneBuilder (foot-pivot PPU, alpha settings) before this runs.
    // Stage01完成版要求仕様書「鳥」対応(2026-09-13) - flying_wyvern(現状
    // 実際にゲーム中へ出現する唯一の飛行種)の見た目を、ドラゴン風で強敵
    // に見えすぎていた元のFlyingEnemy.pngから、自然な鷹のイラストへ
    // 差し替え。元の画像は削除せず温存(将来天空回廊が本実装される際の
    // 強敵系飛行種として再利用できるようにするため)。
    const string FlyingSpritePath = "Assets/Art/Enemy/WastelandBird.png";
    const string IrregularSpritePath = "Assets/Art/Enemy/IrregularEnemy.png";
    const string ShooterSpritePath = "Assets/Art/Enemy/ShooterEnemy.png";
    const string HeavySpritePath = "Assets/Art/Enemy/HeavyEnemy.png";
    const string RunnerSpritePath = "Assets/Art/Enemy/RunnerEnemy.png";
    // Runner Enemy Run Animation - 5 frames, imported by SceneBuilder
    // (ConfigureSpriteFolderImportWithFootPivotUniformSize) before this runs.
    const string RunnerRunFramesFolder = "Assets/Art/RunnerRun";
    // 敵アニメーション追加(2026-09-15) - Goblin用の走行5コマ(ChatGPTでenemy_v1.
    // pngを参照画像として生成、SceneBuilderが同じUniformSize方式でインポート
    // 済み)。goblin_eliteは同じ素材+色ティントのみで見た目を差別化している
    // 既存の仕組み(spritePath=null、tintだけ紫)なので、走行アニメーションも
    // そのまま共用できる。
    const string GoblinRunFramesFolder = "Assets/Art/GoblinRun";

    struct Spec
    {
        public string id;
        public string displayName;
        public string spritePath;
        public Color tint;
        public EnemyMovementType movementType;
        public EnemyCategory category;
        public EnemyBehaviorKind behaviorKind;
        public float hpMultiplier;
        public bool bigKnockbackOnHit;
        // Distance Level Design Ver.1.1 - defaults to false/true (C#
        // struct default), matching goblin/goblin_elite's "already correct,
        // don't touch" art - only the 6 new species below set
        // enableVisualFacing = true explicitly.
        public bool enableVisualFacing;
        public bool defaultFacingRight;
        // 敵アニメーション追加(2026-09-15) - 元々useRunnerRunFrames(bool、
        // Runner専用)だったものを汎用化。空文字なら従来どおりrunFrames=
        // null(=EnemyAnimatorの手続き的idleのみ)、指定時はそのフォルダから
        // LoadRunFrames()で読み込む。Runner種はRunnerRunFramesFolder、
        // Goblin/EliteGoblinはGoblinRunFramesFolderを指定する。
        public string runFramesDir;
        // Reward/MILE System Ver.1 - per-species MILE value (see
        // EnemyDefinition.mileReward's own comment). Defaults to 1 (C#
        // struct default) so any Spec below that doesn't set this
        // explicitly still matches "Normal種は1".
        public int mileReward;
        // Enemy Visual Size Unification pass - see EnemyDefinition.
        // visualScaleMultiplier's own comment. 0 (C# struct default) means
        // "not set" and resolves to 1 in Build() below, same pattern as
        // mileReward above.
        public float visualScaleMultiplier;
    }

    // Runner Enemy Run Animation - loads the already-configured frame
    // Sprites from the given folder, sorted by filename (e.g. runner_run_0
    // .. runner_run_4), same "load whatever's there, sorted" pattern
    // SceneBuilder's own LoadSpriteSequence uses for Dragon/Majin/Player.
    // Returns an empty array (never null) if the folder/frames aren't
    // there yet, so a species referencing this just falls back to its
    // single static `sprite` - EnemyAnimator already treats an empty
    // runFrames array as "no run animation".
    // 敵アニメーション追加(2026-09-15) - 元々RunnerRunFramesFolder固定
    // だったものをフォルダ引数化(GoblinRunFramesFolder等、他種でも再利用
    // するため)。
    static Sprite[] LoadRunFrames(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return new Sprite[0];
        string[] files = Directory.GetFiles(folder, "*.png");
        System.Array.Sort(files);
        var list = new List<Sprite>();
        foreach (string f in files)
        {
            Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(f.Replace('\\', '/'));
            if (s != null) list.Add(s);
        }
        return list.ToArray();
    }

    [MenuItem("Tools/OneMoreMile/Build Enemy Database")]
    public static void Build()
    {
        Sprite goblinSprite = AssetDatabase.LoadAssetAtPath<Sprite>(GoblinSpritePath);
        Build(goblinSprite);
    }

    public static void Build(Sprite goblinSprite)
    {
        if (!AssetDatabase.IsValidFolder(EnemiesFolder))
        {
            Directory.CreateDirectory(EnemiesFolder);
            AssetDatabase.Refresh();
        }

        foreach (Spec spec in Specs(goblinSprite))
        {
            string assetPath = $"{EnemiesFolder}/{spec.id}.asset";
            EnemyDefinition existing = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(assetPath);
            if (existing != null)
            {
                // Bugfix 2026-09-05, item 6 - enableVisualFacing/
                // defaultFacingRight predate this asset for goblin/
                // goblin_elite specifically (created before the field
                // existed), so they're still sitting at EnemyDefinition's
                // own bare class defaults (false/true) rather than any
                // hand-tuned value - same one-time backfill reasoning
                // CardDatabaseBuilder already uses for its own brand-new
                // fields. A value that's already anything else (a
                // deliberate Inspector edit made after this rebuild first
                // ran) is left completely alone.
                if (!existing.enableVisualFacing && existing.defaultFacingRight && spec.enableVisualFacing)
                {
                    existing.enableVisualFacing = spec.enableVisualFacing;
                    existing.defaultFacingRight = spec.defaultFacingRight;
                    EditorUtility.SetDirty(existing);
                }
                // 敵アニメーション追加(2026-09-15) - runFramesも同じ「まだ
                // 一度もこのフィールドを持ったことがない既存アセット(空配列
                // /null)だけバックフィルする」パターン。既にコマが設定されて
                // いる場合(Runner種、または手動でInspectorから外した場合)は
                // 一切触れない。
                if ((existing.runFrames == null || existing.runFrames.Length == 0) && !string.IsNullOrEmpty(spec.runFramesDir))
                {
                    Sprite[] frames = LoadRunFrames(spec.runFramesDir);
                    if (frames.Length > 0)
                    {
                        existing.runFrames = frames;
                        EditorUtility.SetDirty(existing);
                    }
                }
                continue; // never overwrite anything else, see class comment
            }

            var def = ScriptableObject.CreateInstance<EnemyDefinition>();
            def.enemyId = spec.id;
            def.displayName = spec.displayName;
            def.sprite = !string.IsNullOrEmpty(spec.spritePath) ? AssetDatabase.LoadAssetAtPath<Sprite>(spec.spritePath) : goblinSprite;
            def.tint = spec.tint;
            def.movementType = spec.movementType;
            def.category = spec.category;
            def.behaviorKind = spec.behaviorKind;
            def.hpMultiplier = spec.hpMultiplier;
            def.bigKnockbackOnHit = spec.bigKnockbackOnHit;
            def.enableVisualFacing = spec.enableVisualFacing;
            def.defaultFacingRight = spec.defaultFacingRight;
            def.runFrames = !string.IsNullOrEmpty(spec.runFramesDir) ? LoadRunFrames(spec.runFramesDir) : null;
            // Reward/MILE System Ver.1 - Spec.mileReward defaults to 0 (C#
            // struct default) when a Spec below doesn't set it explicitly;
            // treat that as "1" (Normal's value) rather than an accidental
            // 0-MILE species.
            def.mileReward = spec.mileReward > 0 ? spec.mileReward : 1;
            // Enemy Visual Size Unification pass - same "0 = not set"
            // convention as mileReward above.
            def.visualScaleMultiplier = spec.visualScaleMultiplier > 0f ? spec.visualScaleMultiplier : 1f;

            AssetDatabase.CreateAsset(def, assetPath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EnemyDatabase.Reset();
    }

    static IEnumerable<Spec> Specs(Sprite goblinSprite)
    {
        // Always available (no matching UnlockDefinition) - the two enemies
        // that already existed before this system, unchanged, now tagged
        // Normal for DistanceTierManager's AvailableEnemyTypes filtering.
        yield return new Spec
        {
            id = "goblin",
            displayName = "GOBLIN",
            spritePath = null, // uses goblinSprite directly, same as before this pass
            tint = Color.white,
            movementType = EnemyMovementType.Ground,
            category = EnemyCategory.Normal,
            behaviorKind = EnemyBehaviorKind.None,
            hpMultiplier = 1f,
            bigKnockbackOnHit = false,
            // Bugfix 2026-09-05, item 6 - "Normal EnemyがPlayer方向を向か
            // ない" was this species specifically: it was left disabled on
            // the (incorrect, per this bugfix) assumption that its art was
            // "already correct" and needed no facing logic at all. The
            // dagger-wielding art (enemy_v1.png) clearly faces/lunges LEFT,
            // so defaultFacingRight=false - matching chaser_runner/
            // rusher_runner's same reasoning.
            enableVisualFacing = true,
            defaultFacingRight = false,
            // Bugfix 2026-09-06 (再調整) - "雑魚EnemyのVisual SizeをPlayer
            // に対して統一" - the earlier Enemy Visual Size Unification pass
            // only ever calibrated every OTHER species relative to Goblin's
            // own (unadjusted, multiplier=1.0) size - it never checked
            // whether Goblin itself actually matched the Player's height, so
            // every species inherited that same gap. Re-measured directly
            // against PlayerRun_v1's own alpha-cropped content height (avg
            // ~1.18 world units across its 5 run frames, PPU 186): Goblin's
            // raw art measures ~1.24 world units (PPU 1053). First pass
            // targeted ~110% of Player (1.05); still read as too small once
            // seen in motion, so re-targeted to ~120% (1.14).
            visualScaleMultiplier = 1.14f,
            // 敵アニメーション追加(2026-09-15) - マスター報告「各敵キャラの
            // アニメーションを追加してほしい」への対応。ChatGPTへenemy_v1.png
            // を参照画像として渡し、同じキャラクター・同じ画風で左向きの
            // 走行5コマを生成(SceneBuilder.ConfigureSpriteFolderImportWith
            // FootPivotUniformSizeでインポート、Runnerと同じ「コマ個別PPU」
            // 方式のため伸び縮みのズレは発生しない)。
            runFramesDir = GoblinRunFramesFolder
        };

        yield return new Spec
        {
            id = "goblin_elite",
            displayName = "ELITE GOBLIN",
            spritePath = null,
            tint = new Color(0.75f, 0.55f, 1f), // violet, distinct from the base red-brown goblin
            movementType = EnemyMovementType.Ground,
            category = EnemyCategory.Normal,
            behaviorKind = EnemyBehaviorKind.None,
            hpMultiplier = 1f,
            bigKnockbackOnHit = false,
            // Same art/reasoning as goblin above (goblin_elite is the same
            // sprite, just re-tinted).
            enableVisualFacing = true,
            defaultFacingRight = false,
            visualScaleMultiplier = 1.14f,
            // 敵アニメーション追加(2026-09-15) - goblin_eliteはgoblinと全く
            // 同じ素材(spritePath=null)を紫ティントで差別化しているだけの
            // 種なので、走行アニメーションも同じGoblinRunFramesFolderを共用
            // する(tintはSpriteRenderer.colorへ適用され、表示中のどのコマ
            // にも独立して効くため、コマ切り替えとティントは干渉しない)。
            runFramesDir = GoblinRunFramesFolder
        };

        // ===== Distance Level Design Ver.1 - 6 new species ===== //
        yield return new Spec
        {
            id = "flying_wyvern",
            displayName = "BIRD",
            spritePath = FlyingSpritePath,
            tint = Color.white,
            movementType = EnemyMovementType.Flying,
            category = EnemyCategory.Flying,
            behaviorKind = EnemyBehaviorKind.Flying,
            hpMultiplier = 1f,
            bigKnockbackOnHit = false,
            enableVisualFacing = true,
            defaultFacingRight = true,
            mileReward = 2,
            // Stage01完成版要求仕様書「鳥」対応(2026-09-13) - 新しい鷹アート
            // (WastelandBird.png、PPU 1117.6で読み込み済み)は素の状態で
            // 既に約0.85 world units - マスター指示「もう少し小型で自然な
            // 鳥系素材へ」に沿って、旧ドラゴン風アート(実効高さ約1.24、
            // Playerの約105%)よりはっきり小さく(Playerの約72%)、かつ
            // ゴブリンより小柄な「小型の障害物的な敵」として読める大きさに
            // 調整。追加の拡大縮小は不要なため1fのまま。
            visualScaleMultiplier = 1f
        };

        yield return new Spec
        {
            id = "irregular_imp",
            displayName = "IRREGULAR",
            spritePath = IrregularSpritePath,
            tint = Color.white,
            movementType = EnemyMovementType.Ground,
            category = EnemyCategory.Irregular,
            behaviorKind = EnemyBehaviorKind.Irregular,
            hpMultiplier = 1f,
            bigKnockbackOnHit = false,
            enableVisualFacing = true,
            defaultFacingRight = true,
            mileReward = 2,
            // Bugfix 2026-09-06 (再調整) - re-measured against Player
            // directly: Irregular's raw art (a squat kobold-style pose)
            // measures only ~0.84 world units tall vs Player's ~1.18. First
            // pass targeted ~110% (1.55); re-targeted to ~120% (1.69),
            // Largest multiplier of the batch since the source art itself
            // is genuinely the shortest/most compact of the six.
            visualScaleMultiplier = 1.69f
        };

        yield return new Spec
        {
            id = "shooter_archer",
            displayName = "SHOOTER",
            spritePath = ShooterSpritePath,
            tint = Color.white,
            movementType = EnemyMovementType.Ground,
            category = EnemyCategory.Shooter,
            behaviorKind = EnemyBehaviorKind.Shooter,
            hpMultiplier = 1f,
            bigKnockbackOnHit = false,
            enableVisualFacing = true,
            defaultFacingRight = true,
            mileReward = 3,
            // Bugfix 2026-09-06 (再調整) - re-measured against Player
            // directly: Shooter's raw art measures ~1.19 world units tall
            // vs Player's ~1.18 (already ~100%). First pass targeted ~110%
            // (1.09); re-targeted to ~120% (1.19).
            visualScaleMultiplier = 1.19f
        };

        yield return new Spec
        {
            id = "heavy_ogre",
            displayName = "HEAVY",
            spritePath = HeavySpritePath,
            tint = Color.white,
            movementType = EnemyMovementType.Ground,
            category = EnemyCategory.Heavy,
            behaviorKind = EnemyBehaviorKind.Heavy,
            hpMultiplier = 3f, // "この基本HPへ追加倍率" - test value, Inspector-tunable after the fact
            bigKnockbackOnHit = true,
            enableVisualFacing = true,
            defaultFacingRight = true,
            mileReward = 5,
            // Bugfix 2026-09-06 (再調整) - re-measured against Player
            // directly: Heavy's raw art already measures ~1.75 world units
            // tall, ~148% of Player's ~1.18 - already inside the brief's
            // own "Heavy->130-150%" band. First pass trimmed to 0.95
            // (->~140%); nudged back up slightly to 0.98 (->~145%) so Heavy
            // reads unambiguously larger even next to the also-enlarged
            // Normal/Shooter/Irregular/Chaser/Rusher above.
            visualScaleMultiplier = 0.98f
        };

        yield return new Spec
        {
            id = "chaser_runner",
            displayName = "CHASER",
            spritePath = RunnerSpritePath,
            tint = Color.white,
            movementType = EnemyMovementType.Ground,
            category = EnemyCategory.Chaser,
            behaviorKind = EnemyBehaviorKind.Chaser,
            hpMultiplier = 1f,
            bigKnockbackOnHit = false,
            enableVisualFacing = true,
            // Bugfix 2026-09-08 - 「雑魚敵Runnerの向きが逆」報告を受けて
            // RunnerEnemy.png(idle)とAssets/Art/RunnerRun/runner_run_0.png
            // (走行1コマ目)を実際に目視確認した結果、両方とも頭/口先が
            // 画像の右側にある=素材はRIGHT向きだと判明。旧コメントの
            // 「明確に左向き」という前提が誤りだった(このコメントを書いた
            // 時点で実際の画像を再確認していなかったと思われる)。
            defaultFacingRight = true,
            runFramesDir = RunnerRunFramesFolder,
            mileReward = 3,
            // Bugfix 2026-09-06 (再調整, root cause found) - the previous
            // pass's own comment flagged "run frames weren't independently
            // re-measured" as a real gap, and that's exactly where the "まだ
            // 小さい/個体差が出る" report traced to: the run-cycle frames
            // (Assets/Art/RunnerRun/) were imported at PPU 724 (each frame's
            // raw CANVAS height) on the assumption canvas height ≈ character
            // height - wrong here, since each 434x724 frame is a narrow
            // slice of a wide horizontal strip and the actual alpha content
            // only fills ~41-53% of that canvas. At PPU 724 the run
            // animation rendered at roughly HALF the world size of
            // RunnerEnemy.png's own static portrait, and fluctuated
            // noticeably frame-to-frame (the "individual差" symptom) - most
            // of the time Chaser/Rusher are moving, i.e. showing THIS art,
            // not the portrait. Fixed at the import site instead of here
            // (SceneBuilder's RunnerRun import now uses PPU 374, solved so
            // the run frames' average natural size matches the portrait's
            // own - see that call's own comment for the math), so ONE
            // multiplier now sizes both states consistently: ~120% of
            // Player on average (ranging ~103-132% across the 5 frames),
            // matching the portrait's own ~120% target too.
            visualScaleMultiplier = 1.52f
        };

        yield return new Spec
        {
            id = "rusher_runner",
            displayName = "RUSHER",
            spritePath = RunnerSpritePath, // "Runner Enemy素材を共用可能" - same art as Chaser, different Behavior
            tint = new Color(1f, 0.85f, 0.85f), // faint red tint so Rusher reads as distinct from Chaser despite sharing art
            movementType = EnemyMovementType.Ground,
            category = EnemyCategory.Rusher,
            behaviorKind = EnemyBehaviorKind.Rusher,
            hpMultiplier = 1f,
            bigKnockbackOnHit = false,
            enableVisualFacing = true,
            defaultFacingRight = true, // Bugfix 2026-09-08 - see chaser_runner's matching comment
            runFramesDir = RunnerRunFramesFolder,
            mileReward = 3,
            visualScaleMultiplier = 1.52f // same shared Runner art - see chaser_runner's matching comment
        };
    }
}
