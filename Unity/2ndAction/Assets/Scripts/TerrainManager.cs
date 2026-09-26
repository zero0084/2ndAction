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
    // エリアルコンボ改修(2026-09-11), item 8 - 下攻撃で叩き落とされた敵が
    // 地面へ到達した瞬間の衝撃VFX。「既存素材が使用できる場合はそれを
    // 利用」の指示どおり、プレイヤー自身の下攻撃着地と全く同じ
    // ImpactBurstBlue.pngをSceneBuilderが共有で流し込む(EnemyController.
    // SlamImpactRoutine/HitAndDie(viaSlam)参照)。全種族共通の1枚なので、
    // GroundFactory.CreateEnemyの引数を増やすのではなく、既存の
    // enemyHitSparkSprite等と同じ「TerrainManagerが持つ共有アセット」に
    // した。
    public Sprite enemyGroundImpactSprite;
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

    // 敵AI行動Tier試験実装(2026-09-16) - 「T0→T1→T2を順番に遭遇できる
    // デバッグ配置」。enemyPool([0]=goblin_t0,[1]=goblin_t1,[2]=goblin_t2、
    // SceneBuilderが割り当てる)には含めない別アセット - 通常のランダム
    // 抽選(EnemyDatabase.PickRandomUnlockedOfCategory)には一切乗らない
    // (Resources/Enemies/ではなく別フォルダに置いてあるため)ので、
    // Formation自体・敵種類のバリエーションは今までどおり無改造。
    // GameManager.DebugMode(実機のDEBUGトグルでON/OFFできる、Editor専用
    // ではない)がONの間だけ、ラン開始から近い距離のNormal category枠を
    // 順番に横取りしてT0/T1/T2を1体ずつ強制スポーンする。
    public EnemyDefinition[] debugTierTestEnemies;
    // 1体あたりの割り当て距離幅(m)。noEnemyBeforeDistanceより後ろから
    // 開始し、T0はそこから0〜この幅、T1は1〜2倍、T2は2〜3倍の区間で
    // 最初に現れたNormal枠を横取りする。既定値を控えめにしてT0〜T2の3体が
    // 最初のBoss戦(1,000m)より十分手前で出揃うようにしてある。
    public float debugTierTestSpanMeters = 30f;

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

    // 荒野街道 地面埋め修整(2026-09-13深夜) - マスター報告「下ルートの下側
    // に見えている空白部分を、地面で埋める」への対応。null(未指定)のまま
    // なら何も描かれず、既存の見た目(天空回廊含む他ステージ)は完全に
    // 無改造。GroundFactory.CreateGroundFillVisual参照 - Pit区間は
    // AddChunk/RebuildAllChunkVisuals側の分岐でそもそも呼び出さないため、
    // 「穴の部分だけ道が途切れて見える」要件は自動的に満たされる。
    public Sprite groundFillSprite;
    // カメラのorthographicSize(CameraFollow.targetHorizontalHalfWidth/
    // aspect)が示す「地面ラインの下に実際に見えている世界単位数」の
    // 実測値(標準的な横長比率で約9.3)に余裕を持たせた既定値。
    // 基礎品質修整(2026-09-14) - 初期値30は実機確認の結果、穴(Pit)の
    // 側壁が過剰に深く見え、穴の存在感を誇張しすぎていた(「穴とわかる」
    // より「描画が壊れている」ように見えるリスク)ため、カメラに実際に
    // 映る範囲を十分覆いつつ側壁の圧迫感を抑えた14まで縮小した。
    public float groundFillDepth = 14f;
    public float groundFillOverlap = 0.05f;
    // 路面と地中断面の接続見た目修正(2026-09-15) - マスター報告「路面の下に
    // 空色の帯が見える」の根本原因。GroundFillの開始位置は、以前は表面
    // スラブの「見た目上のキャンバス矩形の底辺」(platformVisualHeight-
    // platformSurfaceInset)から逆算していたが、実際のプラットフォーム
    // アート(platform_wasteland_mid.png)の岩肌イラストはそのキャンバス
    // 矩形の底辺よりずっと手前(実測、キャンバス高さの約78%)で終わって
    // おり、そこから下は透明なマージンだった(岩の下面のギザギザした
    // 輪郭を表現するための余白で、キャンバス全域には描かれていない)。
    // GroundFillの開始位置をキャンバスの「見た目上の底辺」に合わせて
    // いた結果、「岩肌が実際に見えている部分の終わり」から「Fillの開始
    // 位置」までの間、両方とも透明な帯ができ、背景の空色がそのまま透けて
    // 見えていた。この値は、表面ラインから実測した「岩肌が実際に見えて
    // いる部分の底辺」までのワールド単位オフセットで、SceneBuilder側で
    // ステージごとの実測値に差し替える(TerrainThemeSet.groundFillTop
    // Offset参照) - platformSurfaceInsetと同じくplatformArtとセットで
    // 差し替わる必要がある値。
    public float groundFillTopOffset = 2.634f;
    // Stage01次段階調整(2026-09-16), item5 - マスター報告「下側を地面で
    // 埋めたことで、画面下部の岩断面が大きく占有し窮屈に見える」への対応。
    // groundFillDepth(見せる高さ)自体はワイドな画面比率でのカメラ可視
    // 範囲をぎりぎりカバーする値として既に一度実測調整済みで、これ以上
    // 縮めると再びアスペクト比によっては空色の帯が覗くリスクがある
    // (このフィールドのすぐ上のコメント参照)。そのため「高さ」ではなく
    // 「濃さ」側で圧迫感を弱める - このColorをGroundFillのSpriteRenderer
    // へ乗算適用する(Color.white=無変更)。alpha<=0のままなら常にColor.
    // whiteとして扱う(backgroundTintと同じ「未指定」判定パターン)。
    public Color groundFillTint = Color.white;

    [Header("Chunk Sizes")]
    public float flatLength = 6f;
    // 道のなめらか化(2026-09-10) - マスター報告「道の角が少し出ている箇所が
    // いくつかある。もう少しなめらかに」。slope1本あたりの角度 θ = atan2(
    // slopeHeight, slopeLength) を浅くすると、flat↔slopeの継ぎ目でできる
    // くさび状のはみ出し(leftBleedで覆っている量、tan(θ)に比例)が小さくな
    // り、かつ坂そのものの折れ角も緩くなって道全体がなめらかに見える。
    // 6/2(≒18.4°)→ 8.5/1.5(≒10.0°)で tan(θ) は約53%に低下。
    public float slopeLength = 8.5f;
    public float slopeHeight = 1.5f;
    // Bugfix 2026-09-08, item4 - see AddChunk's leftBleed comment.
    // 道のなめらか化(2026-09-10) - 上記で必要bleed量自体が減ったので、
    // 素材の縁ギザギザ吸収用の安全マージンも 1.4→1.2 へ控えめに(過大な
    // bleedは逆に継ぎ目の角が下側へリップ状にはみ出して見える一因)。
    public float cornerBleedSafetyMargin = 1.2f;
    public float pitWidth = 3f;
    public float groundThickness = 1f;

    [Header("Generation")]
    // Stage01次段階調整(2026-09-16) - ObstacleSpawner/EnemyWallManager/
    // UpperRouteEnemySpawnerはいずれも「MaxDistance(=このRunで到達した
    // 最大到達距離、TakeDamageのノックバックで下がってもMaxDistance自体は
    // 減らない)+spawnAheadDistance(28)」を配置先Xとして使う。ノックバック
    // 直後などplayer.position.xがMaxDistanceより後ろへ下がっている間、
    // このTerrainManager側のチャンク生成は現在のplayer.position.x基準
    // (+generateAheadDistance)でしか先読みしないため、以前の30ではその
    // 配置先Xがまだチャンク未生成の領域に入り込む余地が実質2程度しか無く、
    // 危うい状態だった(未生成領域をGetHeightAtへ問い合わせると、実際に
    // 後で生成される地形とは無関係な「末尾チャンクの高さ」へ静かにフォー
    // ルバックしてしまう - 見た目上「地形として成立していない」配置の
    // 一因になり得る)。各スポナーのspawnAheadDistance(28)に対して確実な
    // 余裕を持たせるため45へ引き上げた。IsGenerated(x)も参照。
    public float generateAheadDistance = 45f;
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
    // 雑魚敵配置修正(2026-09-10) - 「Flyingは空中どこでもランダムに配置」。
    // Formation側のyOffsetパターンではなく、この範囲(地面からの高さ)の中
    // でSpawnFormationMemberが毎回ランダムに選ぶ。上限は画面上端(screenTopY)
    // でも別途クランプされるので、実際にはこれと画面サイズの小さい方になる。
    public float flyingSpawnMinHeight = 1.2f;
    public float flyingSpawnMaxHeight = 4f;

    [Header("Difficulty Ramp (starts past a distance threshold)")]
    public float difficultyStartDistance = 1000f;
    public float pitChanceRampPer1000m = 0.08f;
    public float pitChanceMax = 0.55f;
    public float enemyChanceRampPer1000m = 0.15f;
    public float enemyChanceMax = 0.95f;

    [Header("Colors")]
    public Color groundColor = new Color(0.35f, 0.28f, 0.2f);
    public Color enemyColor = new Color(0.9f, 0.15f, 0.15f);

    // ステージ別ビジュアル差し替え(2026-09-13) - 「荒野街道(地上の草地/土)」
    // と「天空回廊(既存の岩+雲の浮遊足場)」を見た目でも区別できるように
    // する仕組み。stageIdが一致するTerrainThemeSetが無ければ何もしない
    // (=現状の見た目のまま)ので、天空回廊はこの配列に何もエントリを
    // 足さない限り常に既存の見た目のまま - 「いままでの天空マップ」を
    // 無改造で温存する、という要件を安全側で満たす。
    [System.Serializable]
    public struct TerrainThemeSet
    {
        public string stageId;
        public PlatformSpriteSet platformArt;
        public Sprite groundSprite;
        public Color groundColor;
        // 任意 - 指定時のみWorldTimeCycleの昼背景を差し替える(夜背景/
        // 昼夜遷移そのものには一切触れない、今回のスコープを最小限に
        // 保つため)。
        public Sprite backgroundSprite;
        // 基礎品質修整(2026-09-14) - マスター報告「背景の情報量が強く、
        // Player/Enemy/Object/Terrainが背景に埋もれる」への対応。背景
        // スプライトへ乗算するティント色。既定Color.white(=無変更)なら
        // 他フィールドと同じ「未指定なら無変更」ルールに従う - 新規アート
        // 生成やBlur等のシェーダー変更を伴わない、最も安全な視認性向上策
        // として採用(コントラスト/彩度を落とし暗部を持ち上げる効果を、
        // 単純な乗算ティントで近似する)。
        public Color backgroundTint;
        // Stage01完成版要求仕様書「街道らしさ」対応(2026-09-13) - 指定時
        // のみ道端の散策物(DecorationScatter)を差し替える。天空回廊用の
        // 花/岩/廃墟看板(既存decorationSprites)はこの配列を空のまま
        // にすれば一切変更されない - platformArt/groundColor/
        // backgroundSpriteと全く同じ「未指定なら無変更」ルール。
        public Sprite[] decorationSprites;
        // 荒野街道 地面埋め修整(2026-09-13深夜) - 指定時のみ地面の断面埋め
        // テクスチャを差し替える。未指定(null)のままなら他フィールドと
        // 同じ「無変更」ルール。
        public Sprite groundFillSprite;
        // 接地ズレ修整(2026-09-15) - マスター報告「Player/Enemy/Obstacleが
        // 地面から浮いて見える」の根本原因調査で発覚: platformSurfaceInset
        // はplatformArt(見た目のテクスチャ)と対になる値のはずなのに、
        // これまでApplyStageThemeがplatformArtだけを差し替え、
        // platformSurfaceInsetは天空回廊の元アート(platform_mid.png)から
        // 測定した値のまま据え置かれていた。荒野街道のテクスチャ
        // (platform_wasteland_mid.png)は透明マージンの比率が全く違う
        // ため、この値を使い続けると「数式上の地面ライン」と「見た目の
        // 地面ライン」がズレ、荒野街道でだけPlayer/Enemy/Obstacle全てが
        // 浮いて見えていた。0以下(未指定)なら他フィールドと同じ「無
        // 変更」ルールに従う。
        public float platformSurfaceInset;
        // 路面と地中断面の接続見た目修正(2026-09-15) - platformSurfaceInset
        // と同じ理由・同じ「platformArtとセットで差し替える」ルール。表面
        // ラインから実測した「岩肌が実際に見えている部分の底辺」までの
        // ワールド単位オフセット(TerrainManager.groundFillTopOffset参照)。
        // 0以下(未指定)なら他フィールドと同じ「無変更」ルールに従う。
        public float groundFillTopOffset;
        // Stage01次段階調整(2026-09-16), item5 - GroundFillへ乗算するティ
        // ント。backgroundTintと同じ「alpha>0のときだけ指定されたとみなす」
        // ルール(既定のColor構造体はalpha=0)。
        public Color groundFillTint;
        // ルート構造再調整(2026-09-13) - trueの場合のみ、後述のRoute Branch
        // システム(上下ルートの分岐→並走→合流)を使う。falseのまま(=未
        // 指定、天空回廊など)なら、既存の「短い浮遊足場がランダムに点在
        // する」Sky Path生成ロジックが完全に無改造で動き続ける - 生成
        // コード自体は共通クラス(SkyChunk/GetSkyHeightAt等)を再利用する
        // ため、既存の「他ステージ本実装禁止」要件を満たすには生成の
        // *中身*をこのフラグで完全に分岐させる必要があった。
        public bool enableRouteBranch;
        // 分岐/合流地点に置く目印(道標)。null なら何も置かない。
        public Sprite branchMarkerSprite;
        // 自然洞窟(2026-09-21) - trueのステージだけ天井/針/たいまつ/暗さ(CaveStage)を
        // 有効化する。既存ステージはfalse(未指定)のまま無改造。
        public bool enableCave;
    }
    public TerrainThemeSet[] stageThemes = new TerrainThemeSet[0];
    // SceneBuilderが既存のday backgroundのSpriteRendererをそのまま渡す
    // (WorldTimeCycle.dayLayerと同一のコンポーネント参照)。
    public SpriteRenderer backgroundRenderer;
    // 自然洞窟(2026-09-21) - 洞窟固有の天井/針/暗さ。SceneBuilderが割り当てる
    // (未割り当てなら洞窟機能は何もしない)。
    public CaveStage cave;

    // ApplyStageThemeが差し替える値の「初期状態」。テーマの無いステージ(天空回廊等)を
    // 別テーマのRunの後に選んでも、前のテーマの見た目/設定が残らないよう復元するために使う。
    bool themeDefaultsCaptured;
    bool themeDirty;
    PlatformSpriteSet defPlatformArt;
    float defSurfaceInset, defFillTopOffset;
    Sprite defGroundSprite, defBackgroundSprite, defGroundFillSprite, defBranchMarker;
    Color defGroundColor, defBackgroundColor, defFillTint;
    Sprite[] defDecorations;
    bool defRouteBranch;

    void CaptureThemeDefaults()
    {
        themeDefaultsCaptured = true;
        defPlatformArt = platformArt; defSurfaceInset = platformSurfaceInset; defFillTopOffset = groundFillTopOffset;
        defGroundSprite = groundSprite; defGroundColor = groundColor;
        defBackgroundSprite = backgroundRenderer != null ? backgroundRenderer.sprite : null;
        defBackgroundColor = backgroundRenderer != null ? backgroundRenderer.color : Color.white;
        defDecorations = decorationSprites; defGroundFillSprite = groundFillSprite; defFillTint = groundFillTint;
        defRouteBranch = routeBranchEnabled; defBranchMarker = branchMarkerSprite;
    }

    void RestoreThemeDefaults()
    {
        platformArt = defPlatformArt; platformSurfaceInset = defSurfaceInset; groundFillTopOffset = defFillTopOffset;
        groundSprite = defGroundSprite; groundColor = defGroundColor;
        if (backgroundRenderer != null) { backgroundRenderer.sprite = defBackgroundSprite; backgroundRenderer.color = defBackgroundColor; }
        decorationSprites = defDecorations; groundFillSprite = defGroundFillSprite; groundFillTint = defFillTint;
        routeBranchEnabled = defRouteBranch; branchMarkerSprite = defBranchMarker;
    }

    // GameManager.StartGame/BeginContinuedRunの両方から、Run開始時(かつ
    // Playerがワープする前 - BeginContinuedRunのコメント参照)に一度だけ
    // 呼ばれる。マッチする場合のみgroundSprite/platformArt/groundColor/
    // 背景を差し替え、既に生成済みのチャンク(Start()が起動直後に必ず
    // 作る最初の数十m分の"滑走路" - Home画面はこれを覆い隠しているため
    // 見えないが、NEW RUN開始と同時に見えてしまう)の見た目もその場で
    // 描き直す。マッチしない(=未知のstageId、あるいは天空回廊のように
    // エントリ自体が無い)場合は何も変更しない - 安全側のデフォルト動作。
    public void ApplyStageTheme(string stageId)
    {
        if (!themeDefaultsCaptured) CaptureThemeDefaults();
        if (cave != null) cave.SetActive(false);
        TerrainThemeSet? match = null;
        foreach (TerrainThemeSet t in stageThemes)
        {
            if (t.stageId == stageId) { match = t; break; }
        }
        if (!match.HasValue)
        {
            if (themeDirty)
            {
                RestoreThemeDefaults();
                themeDirty = false;
                RebuildAllChunkVisuals();
            }
            return;
        }
        themeDirty = true;

        TerrainThemeSet theme = match.Value;
        platformArt = theme.platformArt;
        // 接地ズレ修整(2026-09-15) - platformArtと必ずセットで差し替える。
        // これを忘れるとテーマ切り替え後もplatformSurfaceInsetだけ前の
        // テーマ(=前のテクスチャの透明マージン測定値)のまま残ってしまう。
        if (theme.platformSurfaceInset > 0f) platformSurfaceInset = theme.platformSurfaceInset;
        // 路面と地中断面の接続見た目修正(2026-09-15) - platformSurfaceInset
        // と同じくplatformArtとセットで差し替える。
        if (theme.groundFillTopOffset > 0f) groundFillTopOffset = theme.groundFillTopOffset;
        if (theme.groundSprite != null) groundSprite = theme.groundSprite;
        groundColor = theme.groundColor;
        if (backgroundRenderer != null && theme.backgroundSprite != null) backgroundRenderer.sprite = theme.backgroundSprite;
        // backgroundTintのColor構造体としての既定値はCol(0,0,0,0)(未指定)
        // なので、alpha>0を「実際に指定された」判定に使う(Color.white等の
        // 「変更なし」を意味する値ではなく、フィールド自体が触られたか
        // どうかを見分けるため)。
        if (backgroundRenderer != null && theme.backgroundTint.a > 0f) backgroundRenderer.color = theme.backgroundTint;
        if (theme.decorationSprites != null && (theme.decorationSprites.Length > 0 || theme.enableCave)) decorationSprites = theme.decorationSprites; // 洞窟は空配列=道端の装飾を出さない
        if (theme.groundFillSprite != null) groundFillSprite = theme.groundFillSprite;
        if (theme.groundFillTint.a > 0f) groundFillTint = theme.groundFillTint;
        routeBranchEnabled = theme.enableRouteBranch;
        branchMarkerSprite = theme.branchMarkerSprite;

        // 洞窟は先に有効化する(地面の断面の深さ等を、再構築より前に切り替えるため)。
        if (cave != null && theme.enableCave) cave.SetActive(true);
        RebuildAllChunkVisuals();
    }

    // AddRightCapToPreviousChunk(既存)と全く同じ「Destroy→GroundFactory.
    // CreateSlopeVisualで再構築」パターンを、全チャンクへ一括適用したもの。
    // leftBleedは(既存のAddRightCapToPreviousChunk同様)厳密には再計算せず
    // 0扱いにする簡略化 - 元々このリトロフィット経路でも同じ簡略化がされ
    // ており、見た目への影響は継ぎ目にごくわずかな隙間が出得る程度の
    // 既存の許容範囲内(このメソッド固有の新しい問題ではない)。
    void RebuildAllChunkVisuals()
    {
        for (int i = 0; i < chunks.Count; i++)
        {
            if (chunks[i].type == ChunkType.Pit || chunks[i].visual == null) continue;
            RebuildChunkVisual(i);
        }

        // Stage01完成版要求仕様書バグ修正(2026-09-13) - Sky Path(空中足場)
        // もここで作り直す。修正前は`chunks`(地上)だけが対象で、Run開始前
        // (Start()時点、まだApplyStageThemeが呼ばれる前)に既に生成済みの
        // Sky PathがデフォルトのplatformArt(岩+雲の天空回廊アート)のまま
        // 取り残され、荒野街道を選んでも最初の区間だけ「岩+雲の浮遊足場」
        // が混ざって見えてしまっていた。
        for (int i = 0; i < skyChunks.Count; i++)
        {
            SkyChunk sc = skyChunks[i];
            if (sc.visual == null) continue;

            Destroy(sc.visual);
            sc.visual = GroundFactory.CreateSlopeVisual(transform, squareSprite, skyPathSprite, platformArt,
                new Vector2(sc.startX, sc.startY), new Vector2(sc.endX, sc.endY), groundThickness, platformVisualHeight, platformSurfaceInset, groundColor);
        }
    }

    // Stage01次段階調整(2026-09-16) - RebuildAllChunkVisuals本体から、
    // 「chunks[i]1個ぶんのVisual/Fill/Decorationを、現在のフィールド値
    // (テーマ切り替え後の色/アート等)とc.type/近隣チャンクの型に基づいて
    // 破棄→再構築する」処理をそのまま抜き出したもの。EnsureSolidGroundAt
    // (Pit→Flat変換後の再描画)からも呼べるようにするための単純なリファ
    // クタで、ロジック自体は一切変えていない。
    // 地面断面(GroundFill)の作り直し。穴に面した端(左=needsLeftCap、右=次が穴)は、表面スラブの丸いキャップより
    // 断面が四角く飛び出さないよう内側へ縮め、上端をスラブの裏へ伸ばして岩の下面の隙間から背景が見えないようにする。
    public float pitEdgeFillInset = 0.28f;
    public float pitEdgeFillTopExtra = 0.35f;
    void RebuildFill(int i, bool nextIsPit)
    {
        RuntimeChunk c = chunks[i];
        if (c.fillVisual != null) Destroy(c.fillVisual);
        c.fillVisual = null;
        if (groundFillSprite == null || c.type == ChunkType.Pit) return;
        float bleed = 0f;
        if (!c.needsLeftCap && platformArt.IsValid && i > 0 && (c.type == ChunkType.Flat) != (chunks[i - 1].type == ChunkType.Flat))
        {
            float theta = Mathf.Atan2(slopeHeight, slopeLength);
            float fillOuterDepth = groundFillTopOffset - groundFillOverlap + groundFillDepth;
            bleed = fillOuterDepth * Mathf.Tan(theta) * cornerBleedSafetyMargin;
        }
        float li = c.needsLeftCap ? pitEdgeFillInset : 0f;
        float ri = nextIsPit ? pitEdgeFillInset : 0f;
        float topExtra = (c.needsLeftCap || nextIsPit) ? pitEdgeFillTopExtra : 0f;
        c.fillVisual = GroundFactory.CreateGroundFillVisual(transform, groundFillSprite,
            new Vector2(c.startX, c.startY), new Vector2(c.endX, c.endY),
            groundFillTopOffset, groundFillDepth, groundFillOverlap, RenderOrder.GroundFill, bleed, groundFillTint, li, ri, topExtra);
    }

    void RebuildChunkVisual(int i)
    {
        RuntimeChunk c = chunks[i];

        // 基礎品質修整 続報(2026-09-14) - マスター報告「地面の描画と道の
        // 描画のズレ」の実機スクリーンショット確認で発見: このリトロ
        // フィット経路(Run開始前の"滑走路"区間 - ApplyStageThemeが
        // 呼ばれる前にStart()が生成した最初の数十m分)は、AddChunkと
        // 違いleftBleed(flat<->slope継ぎ目の楔形隙間を覆う量)を一切
        // 計算していなかった - スラブ・Fillのどちらも隙間が残ったまま
        // だった。AddChunkと全く同じ式で、直前のチャンク(i-1)との
        // 継ぎ目についてbleed量を計算し直す。
        bool needsRightCap = i + 1 < chunks.Count && chunks[i + 1].type == ChunkType.Pit;
        float rebuildLeftBleed = 0f;
        if (!c.needsLeftCap && platformArt.IsValid && i > 0 && (c.type == ChunkType.Flat) != (chunks[i - 1].type == ChunkType.Flat))
        {
            float theta = Mathf.Atan2(slopeHeight, slopeLength);
            float outerDepth = platformVisualHeight - platformSurfaceInset;
            rebuildLeftBleed = outerDepth * Mathf.Tan(theta) * cornerBleedSafetyMargin;
        }

        if (c.visual != null) Destroy(c.visual);
        c.visual = GroundFactory.CreateSlopeVisual(transform, squareSprite, groundSprite, platformArt,
            new Vector2(c.startX, c.startY), new Vector2(c.endX, c.endY), groundThickness, platformVisualHeight, platformSurfaceInset, groundColor,
            c.needsLeftCap, needsRightCap: needsRightCap, leftBleed: rebuildLeftBleed, addWallCollider: routeBranchEnabled);

        // 荒野街道 地面埋め修整(2026-09-13深夜) - テーマ切り替え時も
        // 断面帯を作り直す(差し替え前のテーマの帯が残り続けたり、
        // 逆に新テーマにgroundFillSpriteが無いのに前のテーマの帯が
        // 残ったりしないように、毎回いったん破棄してから要否を見る)。
        RebuildFill(i, i + 1 < chunks.Count && chunks[i + 1].type == ChunkType.Pit);

        if (decorationSprites != null && decorationSprites.Length > 0)
        {
            DecorationScatter.ScatterAlongChunk(c.visual.transform, decorationSprites, new Vector2(c.startX, c.startY), new Vector2(c.endX, c.endY));
        }
    }

    // Stage01次段階調整(2026-09-16), item1/3/4 - GenerateNextBranchが
    // forkX/mergeXの実際の地面高さを読む前に呼ぶ。両地点はブランチ登録
    // (branchRanges.Add)によりその時点で既にIsInBranchRoute==trueとなって
    // おりPitChanceの危険度ブーストを受けるため、運悪くforkX/mergeXちょう
    // どがPitチャンクとして生成されてしまうことがあり得る - その場合、
    // ①GetHeightAt(forkX/mergeX)がnullを返し、ランプの起点/終点が
    // 「??」フォールバック(直近生成チャンクの高さ、forkX/mergeXの実際の
    // 高さとは無関係)を使って誤った高さに置かれる、②道標(branchMarker)
    // が穴の上に浮いて見える、③合流地点が実は穴で「合流したのに即転落」
    // になる、という3つの見た目/配置矛盾が同時に起こり得た。xを含む
    // チャンクがPitなら、その場でFlatへ変換し(そのチャンク自身と、Cap
    // フラグが変わる両隣を再描画)、以降のGetHeightAt(x)が必ず有効な高さ
    // を返すようにする。
    void EnsureSolidGroundAt(float x)
    {
        int idx = -1;
        for (int i = chunks.Count - 1; i >= 0; i--)
        {
            if (x >= chunks[i].startX && x <= chunks[i].endX) { idx = i; break; }
            if (chunks[i].endX < x) break; // これより前のチャンクはさらに手前 - 見つからない
        }
        if (idx < 0 || chunks[idx].type != ChunkType.Pit) return;

        RuntimeChunk pit = chunks[idx];
        pit.type = ChunkType.Flat;
        pit.endY = pit.startY;
        // Pitチャンク自身はAddChunkの見た目分岐を素通りしていたため左
        // キャップを持ったことが無い - 直前が無い(先頭)か直前もPit(通常
        // 起こらないが念のため)の場合のみ、今回新たに必要になる。
        pit.needsLeftCap = idx == 0 || chunks[idx - 1].type == ChunkType.Pit;

        if (idx > 0) RebuildChunkVisual(idx - 1);
        RebuildChunkVisual(idx);
        if (idx + 1 < chunks.Count)
        {
            // 直後のチャンクはlastType==Pitだった時点でneedsLeftCap=trueを
            // 持って生成済み - もう穴の縁ではなくなったので、そのフラグを
            // 落としてから描き直す。
            chunks[idx + 1].needsLeftCap = false;
            RebuildChunkVisual(idx + 1);
        }
    }

    // Stage01次段階調整(2026-09-16), item2/3 - 穴(Pit)の前後marginメート
    // ル以内にxが入っているか。xそのものが穴の中の場合も含む。Obstacle
    // Spawner/EnemyWallManagerが「穴の直前・直後には大型障害物/敵を置き
    // すぎない/即被弾させない」を実装するために使う。xの近く(生成済み
    // チャンクの末尾寄り)から逆順に見て、margin分以上手前まで外れたら
    // 打ち切るので、Runが長時間続いてchunksが大きくなっても軽量。
    public bool IsNearPit(float x, float margin)
    {
        for (int i = chunks.Count - 1; i >= 0; i--)
        {
            RuntimeChunk c = chunks[i];
            if (c.endX < x - margin) break;
            if (c.type == ChunkType.Pit && x >= c.startX - margin && x <= c.endX + margin) return true;
        }
        return false;
    }

    // Stage01次段階調整(2026-09-16) - xまで実際に地面チャンクが生成済み
    // か。ObstacleSpawner等の「spawnAheadDistanceぶん先」を狙う配置系が、
    // GetHeightAtへの問い合わせ時点でまだ生成されていない領域(=末尾
    // チャンクの高さへ静かにフォールバックしてしまう領域)を狙っていない
    // かを確認するために使う - generateAheadDistanceの引き上げと合わせた
    // 二重の安全策(このコメント群のgenerateAheadDistance解説参照)。
    public bool IsGenerated(float x) => x <= nextStartX;
    public float GeneratedEndX => nextStartX;

    // 穴(Pit)を無視した「地面ライン」の高さ。洞窟の天井は穴の上でも連続した高さに
    // したいので、GetHeightAt(穴でnull)ではなくこちらを使う。未生成なら末尾の高さ。
    public float GetGroundLineAt(float x)
    {
        int lo = 0, hi = chunks.Count - 1;
        if (hi < 0) return 0f;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (chunks[mid].endX < x) lo = mid + 1; else hi = mid;
        }
        RuntimeChunk c = chunks[lo];
        float span = c.endX - c.startX;
        float t = span > 0.0001f ? Mathf.Clamp01((x - c.startX) / span) : 0f;
        return Mathf.Lerp(c.startY, c.endY, t);
    }

    // xが上下ルート分岐(登録済み/次に予定されている分岐)からmargin以内か。
    // 次の分岐の範囲は乱数に依存しない(GenerateNextBranch参照)ので未生成でも先読みできる。
    public bool IsBranchNear(float x, float margin)
    {
        if (!routeBranchEnabled) return false;
        foreach (BranchRange r in branchRanges)
        {
            if (x >= r.forkX - margin && x <= r.mergeX + margin) return true;
        }
        float plannedMerge = nextBranchX + branchRampLength * 2f + branchLength;
        return x >= nextBranchX - margin && x <= plannedMerge + margin;
    }

    // 自然洞窟(2026-09-21) - 天井の高さ/針との接触。洞窟でなければ常にnull/false。
    public float? GetCeilingHeightAt(float x) => cave != null ? cave.GetCeilingHeightAt(x) : null;
    // プレイヤー足元Yがこれを超えると頭が天井にめり込む、という上限(洞窟でなければnull)。
    public float? GetCeilingLimitY(float x)
    {
        if (cave == null) return null;
        float? c = cave.GetCeilingHeightAt(x);
        return c.HasValue ? c.Value - cave.playerHeadHeight : (float?)null;
    }
    // 針の先端まで含めた「通行できる天井の高さ」(針が無ければ通常の天井と同じ)。障害物配置などが
    // 通路の空間を判断するのに使う。洞窟でなければnull。
    public float? GetEffectiveCeilingHeightAt(float x) => cave != null ? cave.GetEffectiveCeilingHeightAt(x) : null;
    // 「乗って走れる一番高い床」(下ルートの地面ライン と 上ルートの床 の高い方)。
    public float GetFloorTopAt(float x)
    {
        float g = GetGroundLineAt(x);
        float? s = GetSkyHeightAt(x);
        return s.HasValue ? Mathf.Max(g, s.Value) : g;
    }
    public bool IsCeilingSpikeHit(float x, float feetY) => cave != null && cave.IsSpikeHit(x, feetY + cave.playerHeadHeight);

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
    // Bugfix 2026-09-08, item3 - 「空中に斜めの道とかも生成されるように
    // して」。従来はSkyChunkが常にy一定(完全水平)だったのを、UpSlope/
    // DownSlopeと同じ考え方でたまに傾斜させる。skyPathMinClearanceAbove
    // Groundは、下り斜面が自分の着地点直下の地面に近づきすぎない/めり込
    // まないようにするための下限クランプ。
    public float skyPathSlopeChance = 0.35f;
    public float skyPathSlopeHeight = 1.5f;
    public float skyPathMinClearanceAboveGround = 1.6f;

    [Header("Route Branch (荒野街道 上/下ルート分岐 - ApplyStageThemeのenableRouteBranchで有効化)")]
    // Stage01「荒野街道」ルート構造再調整(2026-09-13) - マスター提供の参考
    // 画像を仕様図として扱った再実装。従来のSky Pathは「短い浮遊足場が
    // ランダムに点在する」構造だったが、今回の要求は「上ルート/下ルート
    // が分岐→一定区間並走→合流する、それぞれ独立した"道"」。SkyChunk/
    // GetSkyHeightAt/onSky(PlayerController)等の既存の仕組み(地上と空中
    // の2つの高さを毎フレーム両方チェックし、着地した方に追従する)は
    // 完全に再利用し、変えたのは「生成される区間の形」だけ - 点在する
    // 短い足場ではなく、ランプアップ→(ほぼ平坦な)並走区間→ランプダウン
    // という一続きの長い区間を生成する。routeBranchEnabled=falseの間は
    // 既存のGenerateNextSkyChunkが完全に無改造のまま動き続ける(天空回廊
    // 用)。
    public float branchStartDistance = 100f;
    public float branchMinInterval = 90f;
    public float branchMaxInterval = 150f;
    // Stage01仕上げ調整(2026-09-13深夜) - マスター指摘「上ルートが独立
    // した道としては弱い/分岐・合流が曖昧」に対応。branchLengthを45→70
    // (上ルートを単に走れる距離として1.5倍以上に延長、「上ルートを選んで
    // 進んでいる」実感を強める)、branchRampLength/branchHeightAboveGround
    // を6→9/2.4→3.0(坂そのものを長く・高低差もはっきりさせ、「ここで
    // 道が上下に分かれる/戻る」と視覚的に伝わる上り坂/下り坂にする)。
    public float branchLength = 70f;
    public float branchRampLength = 9f;
    public float branchSegmentLength = 9f;
    public float branchHeightAboveGround = 3f;
    // 上ルートは「比較的平坦で走りやすい」という要求のため、Sky Pathより
    // 起伏を穏やかにしてある(発生確率・高さともに控えめ)。
    public float branchSlopeChance = 0.25f;
    public float branchSlopeHeight = 1f;
    public float branchMinClearanceAboveGround = 1.6f;
    // 下ルート(danger)側の危険度ブースト - 分岐区間中に地上(chunks)側で
    // 生成される穴/敵の確率へ掛ける倍率。GetPitChance/GetEnemyChance側で
    // 参照する(危険度の上限=pitChanceMax/enemyChanceMaxは既存のまま、
    // 理不尽な値までは上げない)。
    // Stage01仕上げ調整(2026-09-13深夜) - マスター指摘「下ルートの危険が
    // 敵密度の高さに寄りすぎている」に対応し、敵ブーストを1.3→1.15へ
    // 弱めた(穴側のブーストは据え置き=穴は敵とは質の異なる危険要素の
    // ままにする)。敵を減らした分の「忙しさ」はObstacleSpawner.
    // dangerHeavyWeightMultiplier(壁/巨大石/壊せる木の重み)を1.8→2.4へ
    // 引き上げて補う - 「敵の壁」ではなく「敵+障害物+穴の複合」で危険度
    // を表現する狙い。
    public float branchDangerPitMultiplier = 1.6f;
    public float branchDangerEnemyMultiplier = 1.15f;
    bool routeBranchEnabled;
    Sprite branchMarkerSprite;

    enum ChunkType { Flat, UpSlope, DownSlope, Pit }

    class RuntimeChunk
    {
        public ChunkType type;
        public float startX, endX, startY, endY;
        public GameObject visual;
        // 荒野街道 地面埋め修整(2026-09-13深夜) - visualとは別に保持し、
        // RebuildAllChunkVisualsで一緒に破棄・再生成できるようにする。
        public GameObject fillVisual;
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
        public float startX, endX, startY, endY;
        // Stage01完成版要求仕様書バグ修正(2026-09-13) - 以前はここに参照を
        // 保持していなかったため、RunStart時点で既に生成済みのSky Path
        // (Start()がApplyStageTheme呼び出しより前に走るため、常にデフォルト
        // のplatformArt=岩+雲の天空回廊アートで生成される)が、荒野街道を
        // 選んでも二度と描き直されず「岩+雲の浮遊足場」のまま残ってしまう
        // バグがあった(RebuildAllChunkVisualsは`chunks`だけを対象にしてお
        // り`skyChunks`には触れていなかった)。visualを保持することで
        // RebuildAllChunkVisualsからSky Pathも作り直せるようにした。
        public GameObject visual;
    }

    // ルート構造再調整(2026-09-13) - 1つの分岐(フォーク)から合流(マージ)
    // までのXレンジを覚えておくためだけの軽量レコード。IsInBranchRoute
    // (地上=下ルート側の危険度ブースト判定)とObstacleSpawner/
    // UpperRouteEnemySpawner(上ルート側の軽い配置判定)の両方から使う。
    class BranchRange
    {
        public float forkX, mergeX;
    }

    readonly List<RuntimeChunk> chunks = new List<RuntimeChunk>();
    readonly List<SkyChunk> skyChunks = new List<SkyChunk>();
    readonly List<BranchRange> branchRanges = new List<BranchRange>();
    float nextStartX;
    float nextStartY;
    float nextSkyStartX;
    float nextBranchX;
    ChunkType lastType;
    float lastEnemyX = float.NegativeInfinity;

    void Awake()
    {
        Instance = this;
    }

    void OnEnable() { FloatingOrigin.Shifted += OnOriginShifted; }
    void OnDisable() { FloatingOrigin.Shifted -= OnOriginShifted; }

    // 高速走行対応(2026-09-22) - Floating Origin。シーン全体を-sだけ戻したので、
    // 地形が持つX座標のデータ(チャンク範囲/次の生成位置/分岐範囲)も同じだけ戻す。
    // 論理距離が必要な箇所(難易度/敵の出現開始距離)は FloatingOrigin.Offset を足して評価する。
    // あわせて、プレイヤーよりずっと後ろのチャンク(見た目/敵)を破棄する。以前は全チャンクを
    // 永久に保持していたため、長距離走行で GameObject が数千個に膨らんでいた。
    public float chunkKeepBehindDistance = 300f;
    void OnOriginShifted(float s)
    {
        for (int i = 0; i < chunks.Count; i++) { chunks[i].startX -= s; chunks[i].endX -= s; }
        for (int i = 0; i < skyChunks.Count; i++) { skyChunks[i].startX -= s; skyChunks[i].endX -= s; }
        for (int i = 0; i < branchRanges.Count; i++) { branchRanges[i].forkX -= s; branchRanges[i].mergeX -= s; }
        nextStartX -= s;
        nextSkyStartX -= s;
        nextBranchX -= s;
        if (!float.IsNegativeInfinity(lastEnemyX)) lastEnemyX -= s;

        float px = player != null ? player.position.x : 0f;
        // マルチプレイPhase 2 - HOSTは共有の敵が乗っている地形を、最後尾のプレイヤーが通過するまで残す
        // (前のプレイヤーの都合で後ろのプレイヤーの敵/足場を消さない)。シングル/JOINでは従来どおり。
        px = NetCombat.RearmostPlayerX(px);
        float cutoff = px - chunkKeepBehindDistance;
        int removeChunks = 0;
        while (removeChunks < chunks.Count - 2 && chunks[removeChunks].endX < cutoff) removeChunks++;
        for (int i = 0; i < removeChunks; i++)
        {
            RuntimeChunk c = chunks[i];
            if (c.visual != null) Destroy(c.visual);
            if (c.fillVisual != null) Destroy(c.fillVisual);
            for (int e = 0; e < c.enemies.Count; e++) if (c.enemies[e] != null) Destroy(c.enemies[e]);
        }
        if (removeChunks > 0) chunks.RemoveRange(0, removeChunks);
        for (int i = skyChunks.Count - 1; i >= 0; i--)
        {
            if (skyChunks[i].endX < cutoff)
            {
                if (skyChunks[i].visual != null) Destroy(skyChunks[i].visual);
                skyChunks.RemoveAt(i);
            }
        }
        for (int i = branchRanges.Count - 1; i >= 0; i--)
            if (branchRanges[i].mergeX < cutoff) branchRanges.RemoveAt(i);
    }

    void Start()
    {
        nextStartX = 0f;
        nextStartY = 0f;
        nextSkyStartX = skyPathStartDistance;
        nextBranchX = branchStartDistance;

        // Guaranteed safe runway before any obstacle.
        AddChunk(ChunkType.Flat, flatLength * 2f);

        while (nextStartX < generateAheadDistance)
        {
            GenerateNext();
        }
        // ルート構造再調整(2026-09-13) - branchStartDistance/skyPathStart
        // Distanceは共にgenerateAheadDistanceより十分大きい既定値なので、
        // Run開始直後のこの初回バッチではどちらの分岐も実際には発火しない
        // (=ApplyStageThemeがrouteBranchEnabledを設定し終えるまでの間に
        // 誤った方の生成ロジックが動いてしまう心配がない)。
        if (routeBranchEnabled)
        {
            while (nextBranchX < generateAheadDistance) GenerateNextBranch();
        }
        else
        {
            while (nextSkyStartX < generateAheadDistance) GenerateNextSkyChunk();
        }
    }

    void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;
        if (player == null) return;
        if (WorldRng.IsDeterministic) { UpdateDeterministic(); return; }

        while (nextStartX < player.position.x + generateAheadDistance)
        {
            GenerateNext();
        }
        if (routeBranchEnabled)
        {
            while (nextBranchX < player.position.x + generateAheadDistance) GenerateNextBranch();
        }
        else
        {
            while (nextSkyStartX < player.position.x + generateAheadDistance) GenerateNextSkyChunk();
        }

        // Old chunks are intentionally never destroyed: getting hit sends the
        // player back to the very start of the run, so the ground from x=0
        // onward has to stay intact and walkable the whole game, not just
        // whatever's near the player's current position.
    }

    // マルチプレイ対応Phase 1(2026-09-25) - 通常のUpdateは分岐/空中足場を「プレイヤーの
    // 位置+先読み距離」を基準に生成するため、同じ乱数でも端末ごとに「どこまで地面を
    // 生成済みか」のタイミングがずれ、合流直後の平地予約(RequestFlatRun)や空中足場の
    // 高さ(未生成地点の高さのフォールバック)が変わって地形が食い違う。マルチプレイRun中は
    // 分岐/空中足場を「地面の生成位置(nextStartX)がその地点に達した瞬間」に生成する
    // (地面チャンクの列の中の決まった位置で必ず生成される)ことで全端末の地形を一致させる。
    // ステージのテーマ(分岐の有無)が確定する前に生成しないよう、Run開始までは待つ。
    void UpdateDeterministic()
    {
        if (GameManager.Instance != null && !GameManager.Instance.HasStarted) return;
        // マルチプレイPhase 2 - HOSTは先頭のプレイヤーの前方まで生成する(共有の敵をその先に出すため)。
        float target = NetCombat.ForemostPlayerX(player.position.x) + generateAheadDistance;
        int guard = 0;
        while (guard++ < 10000)
        {
            if (routeBranchEnabled)
            {
                if (nextBranchX <= nextStartX) { GenerateNextBranch(); continue; }
            }
            else if (nextSkyStartX + skyPathSegmentLength <= nextStartX)
            {
                GenerateNextSkyChunk();
                continue;
            }
            if (nextStartX >= target) break;
            GenerateNext();
        }
    }

    // デバッグ/検証用: 生成済みの地面の並び(種類・論理X・高さ)を要約した値。
    // 同じWorldSeedで走っている2端末でこの値が一致すれば、地形が一致している。
    public string DebugTerrainSignature(float minLogicalX, float maxLogicalX)
    {
        unchecked
        {
            int h = 17, count = 0;
            foreach (RuntimeChunk c in chunks)
            {
                float lx = FloatingOrigin.ToLogical(c.startX);
                if (lx < minLogicalX) continue;
                if (lx > maxLogicalX) break;
                h = h * 31 + (int)c.type;
                h = h * 31 + Mathf.RoundToInt(lx * 10f);
                h = h * 31 + Mathf.RoundToInt(c.startY * 100f);
                h = h * 31 + Mathf.RoundToInt(c.endY * 100f);
                count++;
            }
            foreach (SkyChunk s in skyChunks)
            {
                float lx = FloatingOrigin.ToLogical(s.startX);
                if (lx < minLogicalX || lx > maxLogicalX) continue;
                h = h * 31 + Mathf.RoundToInt(lx * 10f);
                h = h * 31 + Mathf.RoundToInt(s.startY * 100f);
                h = h * 31 + Mathf.RoundToInt(s.endY * 100f);
                count++;
            }
            return $"{count}:{h:X8}";
        }
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
    // Stage01仕上げ前調整(2026-09-15) - マスター指摘「復帰直後に同じ穴へ
    // 再落下しない」への対応。0.5fだと自動前進速度(通常5〜6 units/秒)で
    // ノックバック分を差し引いてもコンマ数秒で穴の縁へ戻ってしまい、
    // 再落下を避ける実質的な反応時間がほぼ無かった(TakeDamageのisFallは
    // hitInvincibleTimerによる連続ヒット防止を素通りするため、無敵時間の
    // 長さでは救えない - 純粋に距離の問題)。pitReactionBufferぶん手前まで
    // 下げ、目視して助走してから穴へ向き直えるだけの間合いを確保する。
    public float pitReactionBuffer = 2.5f;
    public float FindSafeRespawnX(float x)
    {
        foreach (RuntimeChunk c in chunks)
        {
            if (c.type == ChunkType.Pit && x >= c.startX && x <= c.endX)
            {
                return c.startX - pitReactionBuffer;
            }
        }
        return x;
    }

    // デバッグ/自動テスト用: 地形の継ぎ目(種類が変わる位置/穴の縁/上ルートの起点・終点)の一覧。
    public System.Collections.Generic.List<KeyValuePair<float, string>> DebugListJoints()
    {
        var list = new System.Collections.Generic.List<KeyValuePair<float, string>>();
        for (int i = 1; i < chunks.Count; i++)
        {
            if (chunks[i].type != chunks[i - 1].type)
                list.Add(new KeyValuePair<float, string>(chunks[i].startX, chunks[i - 1].type + "->" + chunks[i].type));
        }
        foreach (SkyChunk s in skyChunks) list.Add(new KeyValuePair<float, string>(s.startX, "sky-start"));
        foreach (BranchRange r in branchRanges) { list.Add(new KeyValuePair<float, string>(r.forkX, "fork")); list.Add(new KeyValuePair<float, string>(r.mergeX, "merge")); }
        list.Sort((a, b) => a.Key.CompareTo(b.Key));
        return list;
    }

    // デバッグ/自動テスト用: xがどの地形チャンクか・穴/分岐との関係を文字列で返す。
    public string DebugDescribeAt(float x)
    {
        string chunk = "none";
        for (int i = 0; i < chunks.Count; i++)
        {
            RuntimeChunk c = chunks[i];
            if (x >= c.startX && x <= c.endX)
            {
                string prev = i > 0 ? chunks[i - 1].type.ToString() : "-";
                string next = i + 1 < chunks.Count ? chunks[i + 1].type.ToString() : "-";
                chunk = c.type + "[" + prev + "<" + "|>" + next + "] leftCap=" + c.needsLeftCap + " x=" + c.startX.ToString("F1") + ".." + c.endX.ToString("F1");
                break;
            }
        }
        return chunk + " nearPit(3)=" + IsNearPit(x, 3f) + " inBranch=" + IsInBranchRoute(x) + " sky=" + (GetSkyHeightAt(x).HasValue ? "y" : "n");
    }

    // Elevated platform floating above the ground at x, if any - checked
    // alongside GetHeightAt (never instead of it) so the player can land on
    // whichever surface is actually beneath them.
    public float? GetSkyHeightAt(float x)
    {
        for (int i = 0; i < skyChunks.Count; i++)
        {
            SkyChunk c = skyChunks[i];
            if (x >= c.startX && x <= c.endX)
            {
                float span = c.endX - c.startX;
                float t = span > 0.0001f ? (x - c.startX) / span : 0f;
                return Mathf.Lerp(c.startY, c.endY, t);
            }
        }
        return null;
    }

    // ルート構造再調整(2026-09-13) - xが現在生成済みのいずれかの分岐区間
    // (フォーク〜マージ)の内側にあるか。ObstacleSpawner/EnemyWallManager
    // 相当の各スポナーが「下ルート側の危険度を上げる」「上ルート側にだけ
    // 軽い配置をする」を判断するのに使う。routeBranchEnabled=false(天空
    // 回廊等)の間はbranchRangesが常に空なので、常にfalseを返す。
    public bool IsInBranchRoute(float x)
    {
        foreach (BranchRange r in branchRanges)
        {
            if (x >= r.forkX && x <= r.mergeX) return true;
        }
        return false;
    }

    // Stage01オブジェクト配置整理依頼(2026-09-17), item3 - 「分岐/合流
    // 地点はルート選択区間として、大型障害物を置かない」に対応する公開
    // アクセサ。branchRanges自体はprivateのまま保つ(書き込みは許可しない)
    // - ObstacleSpawnerが「xはどこかのfork/mergeからmargin以内か」だけを
    // 真偽値で問い合わせられるようにした。
    public bool IsNearBranchEdge(float x, float margin)
    {
        foreach (BranchRange r in branchRanges)
        {
            if (Mathf.Abs(x - r.forkX) <= margin) return true;
            if (Mathf.Abs(x - r.mergeX) <= margin) return true;
        }
        return false;
    }

    void GenerateNextSkyChunk()
    {
        float startX = nextSkyStartX;
        float endX = startX + skyPathSegmentLength;
        float groundYStart = GetHeightAt(startX) ?? nextStartY;
        float groundYEnd = GetHeightAt(endX) ?? groundYStart;
        float startY = groundYStart + skyPathHeightAboveGround;

        // Bugfix 2026-09-08, item3 - occasionally slope this segment up or
        // down across its length instead of always staying perfectly flat,
        // same spirit as the ground path's UpSlope/DownSlope chunks.
        float endY = startY;
        if (WorldRng.Sky.Value < skyPathSlopeChance)
        {
            float delta = WorldRng.Sky.Value < 0.5f ? skyPathSlopeHeight : -skyPathSlopeHeight;
            endY = startY + delta;
            float minEndY = groundYEnd + skyPathMinClearanceAboveGround;
            if (endY < minEndY) endY = minEndY;
        }

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
        GameObject skyVisual = GroundFactory.CreateSlopeVisual(transform, squareSprite, skyPathSprite, platformArt,
            new Vector2(startX, startY), new Vector2(endX, endY), groundThickness, platformVisualHeight, platformSurfaceInset, groundColor);

        skyChunks.Add(new SkyChunk { startX = startX, endX = endX, startY = startY, endY = endY, visual = skyVisual });
        nextSkyStartX = endX + WorldRng.Sky.Range(skyPathGapMin, skyPathGapMax);
    }

    // ルート構造再調整(2026-09-13) - マスター提供の参考画像を仕様図として
    // 実装した「分岐→並走→合流」区間の生成。routeBranchEnabled=trueの
    // 間、GenerateNextSkyChunkの代わりにこちらが呼ばれる。
    //
    // 生成する3パーツ(すべてskyChunksへ積む=既存のGetSkyHeightAt/onSky/
    // RebuildAllChunkVisuals等の仕組みをそのまま再利用できる):
    //   1) ランプアップ: forkXの地上高さから、branchHeightAboveGroundぶん
    //      登る1コマ。プレイヤー目線では「ここから上ルートへの坂」に見える。
    //   2) 並走区間: branchSegmentLength刻みで複数コマ、ゆるい起伏
    //      (branchSlopeChance)を持たせつつ、常に地上よりbranchMinClearance
    //      AboveGround以上高い位置を保つ - 上ルートが下ルートの穴/斜面に
    //      潰されないようにするための下限クランプ。
    //   3) ランプダウン: mergeXの実際の地上高さまで戻し、合流させる。
    //
    // 分岐区間の合計X長は乱数(高さのブレ)に一切依存しない完全な定数
    // (branchRampLength*2 + branchLength)なので、forkX/mergeXは生成前
    // から正確に分かる - これを利用して、地上(chunks)側の生成がこの区間
    // へ追いつく前にbranchRangesへ範囲を登録してから地上生成を強制的に
    // 進める(下のwhileループ)。そうしないと、この区間で生成される地上
    // チャンクがGetPitChance/GetEnemyChanceの危険度ブースト(IsInBranch
    // Route判定)を受け損ねてしまう。
    void GenerateNextBranch()
    {
        float forkX = nextBranchX;
        float mergeX = forkX + branchRampLength * 2f + branchLength;
        branchRanges.Add(new BranchRange { forkX = forkX, mergeX = mergeX });

        while (nextStartX < mergeX) GenerateNext();

        // Stage01次段階調整(2026-09-16), item1/3/4 - forkX/mergeXちょうど
        // がPitとして生成されてしまっていないかをここで確定させる(上の
        // whileループで地上チャンクは既にmergeXまで生成済み)。EnsureSolid
        // GroundAt自体のコメント参照 - これを怠ると、直後のGetHeightAt
        // (forkX)/(mergeX)がnullを返し「??」フォールバック(実際のforkX/
        // mergeXの高さとは無関係な値)を使ってランプの起点/終点や道標が
        // 誤った高さ・穴の上に置かれ得た。
        EnsureSolidGroundAt(forkX);
        EnsureSolidGroundAt(mergeX);
        // item4 - 「合流地点に降りた直後、安全に接地できる」ため、merge
        // 直後の1チャンクを強制的にFlatで予約しておく(Formationが使う
        // RequestFlatRunと全く同じ仕組み)。まだ生成されていない先の
        // チャンクに対する予約なので、実際に消費されるのはこの先の
        // GenerateNext呼び出し時点。
        RequestFlatRun(1);

        float groundYAtFork = GetHeightAt(forkX) ?? nextStartY;

        float x = forkX;
        float y = groundYAtFork;

        // 1) ランプアップ - 左端は地上と地続き(露出していない=キャップ不要)
        // ではなく、ここが上ルートの本当の起点なので左キャップを付ける。
        float rampUpEndX = x + branchRampLength;
        float rampUpEndY = groundYAtFork + branchHeightAboveGround;
        // 2026-09-22 - ランプ(上ルートの起点/終点)の壁ハザードは撤去。ランプの真下は下ルートの地面が続いており、
        // 歩いて通るだけの下ルートのプレイヤーが「壁」に触れてダメージを受けていた(TerrainDamageAutoTestで確認)。
        // 崖面Collider追加(2026-09-15) - ランプアップの起点(forkX)は下ルート
        // (danger)がすぐ脇を通る、まさにマスター指摘「上下ルート間の崖面」
        // の実例。このメソッドはrouteBranchEnabled=trueの間しか呼ばれない
        // ので、常時trueで問題ない。
        GameObject rampUpVisual = GroundFactory.CreateSlopeVisual(transform, squareSprite, skyPathSprite, platformArt,
            new Vector2(x, y), new Vector2(rampUpEndX, rampUpEndY), groundThickness, platformVisualHeight, platformSurfaceInset, groundColor,
            needsLeftCap: true, needsRightCap: false, addWallCollider: false);
        skyChunks.Add(new SkyChunk { startX = x, endX = rampUpEndX, startY = y, endY = rampUpEndY, visual = rampUpVisual });
        CreateBranchUndersideFill(new Vector2(x, y), new Vector2(rampUpEndX, rampUpEndY));
        // Stage01仕上げ調整(2026-09-13深夜) - マスター指摘「浮遊足場感が
        // 残る」への軽い緩和策として、分岐区間だけ既定(0.35)より密に
        // 装飾を撒き、道自体の存在感/賑やかさを上げる(崖面のような専用
        // 埋め合わせ visualは別途新規アセットが要るため今回は見送り、
        // マスターへ別途報告)。
        if (decorationSprites != null && decorationSprites.Length > 0)
            DecorationScatter.ScatterAlongChunk(rampUpVisual.transform, decorationSprites, new Vector2(x, y), new Vector2(rampUpEndX, rampUpEndY), spawnChance: 0.55f);
        float prevAng = SegAngleDeg(new Vector2(forkX, groundYAtFork), new Vector2(rampUpEndX, rampUpEndY));
        x = rampUpEndX; y = rampUpEndY;

        // 2) 並走区間 - ランプダウン分の余地(branchRampLength)を残して
        // 複数コマ生成する。
        float parallelEndX = forkX + branchRampLength + branchLength;
        while (x < parallelEndX)
        {
            float segEndX = Mathf.Min(x + branchSegmentLength, parallelEndX);
            float segEndY = y;
            if (WorldRng.Branch.Value < branchSlopeChance)
            {
                float delta = WorldRng.Branch.Value < 0.5f ? branchSlopeHeight : -branchSlopeHeight;
                segEndY = y + delta;
            }
            float groundYAtSegEnd = GetHeightAt(segEndX) ?? groundYAtFork;
            float minY = groundYAtSegEnd + branchMinClearanceAboveGround;
            if (segEndY < minY) segEndY = minY;

            float segAng = SegAngleDeg(new Vector2(x, y), new Vector2(segEndX, segEndY));
            float segBleed = platformArt.IsValid ? SkyJoinBleed(prevAng, segAng, platformVisualHeight - platformSurfaceInset) : 0f;
            GameObject segVisual = GroundFactory.CreateSlopeVisual(transform, squareSprite, skyPathSprite, platformArt,
                new Vector2(x, y), new Vector2(segEndX, segEndY), groundThickness, platformVisualHeight, platformSurfaceInset, groundColor,
                needsLeftCap: false, needsRightCap: false, leftBleed: segBleed);
            skyChunks.Add(new SkyChunk { startX = x, endX = segEndX, startY = y, endY = segEndY, visual = segVisual });
            CreateBranchUndersideFill(new Vector2(x, y), new Vector2(segEndX, segEndY), SkyJoinBleed(prevAng, segAng, groundFillTopOffset - groundFillOverlap + 4f));
            prevAng = segAng;
            if (decorationSprites != null && decorationSprites.Length > 0)
                DecorationScatter.ScatterAlongChunk(segVisual.transform, decorationSprites, new Vector2(x, y), new Vector2(segEndX, segEndY), spawnChance: 0.55f);

            x = segEndX; y = segEndY;
        }

        // 3) ランプダウン - mergeXの実際の地上高さへ戻す。右端は合流して
        // 道が終わる=露出しているので右キャップを付ける。
        float groundYAtMerge = GetHeightAt(mergeX) ?? y;
        float downAng = SegAngleDeg(new Vector2(x, y), new Vector2(mergeX, groundYAtMerge));
        float downBleed = platformArt.IsValid ? SkyJoinBleed(prevAng, downAng, platformVisualHeight - platformSurfaceInset) : 0f;
        GameObject rampDownVisual = GroundFactory.CreateSlopeVisual(transform, squareSprite, skyPathSprite, platformArt,
            new Vector2(x, y), new Vector2(mergeX, groundYAtMerge), groundThickness, platformVisualHeight, platformSurfaceInset, groundColor,
            needsLeftCap: false, needsRightCap: true, leftBleed: downBleed, addWallCollider: false);
        skyChunks.Add(new SkyChunk { startX = x, endX = mergeX, startY = y, endY = groundYAtMerge, visual = rampDownVisual });
        CreateBranchUndersideFill(new Vector2(x, y), new Vector2(mergeX, groundYAtMerge), SkyJoinBleed(prevAng, downAng, groundFillTopOffset - groundFillOverlap + 4f));
        // Stage01仕上げ調整(2026-09-13深夜) - ランプダウンにはこれまで
        // 装飾が撒かれていなかった(ランプアップ/並走区間のみ)。合流地点
        // にも同じ賑やかさを持たせ、「戻ってきた」感を統一する。
        if (decorationSprites != null && decorationSprites.Length > 0)
            DecorationScatter.ScatterAlongChunk(rampDownVisual.transform, decorationSprites, new Vector2(x, y), new Vector2(mergeX, groundYAtMerge), spawnChance: 0.55f);

        // 分岐/合流地点そのものが「ここでルートが分かれる/戻る」と視覚的
        // に分かるよう、道標を1本ずつ地上側に直接置く(DecorationScatter
        // のようなランダム配置ではなく、狙った位置への確定配置)。
        if (branchMarkerSprite != null)
        {
            PlaceBranchMarker(forkX, groundYAtFork);
            PlaceBranchMarker(mergeX, groundYAtMerge);
        }

        nextBranchX = mergeX + WorldRng.Branch.Range(branchMinInterval, branchMaxInterval);
    }

    // Stage01地形挙動修整(2026-09-17), item3 - マスター指摘「上下ルート間の
    // 大きな空洞が橋のように見える」。上ルート(sky route)のスラブは元々
    // 下に何も敷いていない浮遊足場だったため、下ルートの地面との間が素通し
    // の空洞になっていた。既存のGroundFill(下ルート用、常にgroundFillDepth
    // 固定の深さで敷く)とは違い、こちらは実際の下ルート地面の高さ
    // (GetHeightAt)までの「本当の隙間」ぶんだけ動的に深さを計算し、断面
    // テクスチャがちょうど下ルートの地面に届くようにする - 深すぎて下
    // ルートの走行スペースにめり込む/浅すぎて隙間が残る、のどちらも避ける
    // ため。GetHeightAtが取れない(区間内がPit等)場合はbranchHeightAbove
    // Groundを既定の隙間とみなして安全側にフォールバックする。
    // 上ルートの継ぎ目(ランプ<->並走区間、並走区間どうし)で、角度の違う矩形が重ならずに残る楔形の隙間を
    // 覆うための食い込み量。deltaDegは隣り合う2区間の角度差、depthはその素材の下方向の厚み。
    float SkyJoinBleed(float prevAngleDeg, float angleDeg, float depth)
    {
        float d = Mathf.Abs(Mathf.DeltaAngle(prevAngleDeg, angleDeg)) * Mathf.Deg2Rad;
        if (d < 0.005f) return 0f;
        return Mathf.Min(depth * Mathf.Tan(Mathf.Min(d, 1.2f)) * cornerBleedSafetyMargin, 6f);
    }
    static float SegAngleDeg(Vector2 a, Vector2 b) => Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;

    void CreateBranchUndersideFill(Vector2 a, Vector2 b, float leftBleed = 0f)
    {
        if (groundFillSprite == null) return;

        float groundYAtA = GetHeightAt(a.x) ?? (a.y - branchHeightAboveGround);
        float groundYAtB = GetHeightAt(b.x) ?? (b.y - branchHeightAboveGround);
        float gapA = a.y - groundYAtA;
        float gapB = b.y - groundYAtB;
        float avgGap = (gapA + gapB) * 0.5f;

        // スラブ自身の可視部分(groundFillTopOffset-overlap)より下だけを
        // Fillが担当するため、その分を差し引く。わずかな安全マージン
        // (0.2f)を足し、隙間ぴったりでヘアラインの隙間が残らないようにする。
        float fillDepth = Mathf.Max(0.5f, avgGap - (groundFillTopOffset - groundFillOverlap) + 0.2f);

        GroundFactory.CreateGroundFillVisual(transform, groundFillSprite, a, b,
            groundFillTopOffset, fillDepth, groundFillOverlap, RenderOrder.GroundFill, leftBleed, groundFillTint);
    }

    void PlaceBranchMarker(float x, float y)
    {
        GameObject markerGO = new GameObject("RouteMarker");
        markerGO.transform.SetParent(transform);
        markerGO.transform.position = new Vector3(x, y, 0f);
        var sr = markerGO.AddComponent<SpriteRenderer>();
        sr.sprite = branchMarkerSprite;
        sr.sortingOrder = RenderOrder.Ground;
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

        // Bugfix 2026-09-09 - 「道が下のデッドラインを超えて生成される場合
        // がある」。上のbiasはあくまで「下り坂を選ぶ確率」を弱めるだけの
        // ソフトな調整で、どれだけ下がっていても最低15%はDownSlopeが選ば
        // れうる(downChanceのクランプ下限0.05はPitChance込みなので実質
        // もう少し高い) - 長時間プレイで下り坂が連続すれば理論上どこまで
        // でも下がりうるハードな上限が存在しなかった。ここでMinGroundY
        // (PlayerController.failYに安全マージンを足した値)を下回る位置
        // までは絶対に下らせないよう、DownSlopeの選択肢自体を確率から
        // 除外する(0%にしてFlatへ振り替え)ハードな床を追加。
        if (nextStartY - slopeHeight < MinGroundY)
        {
            downChance = 0f;
        }

        float roll = WorldRng.Terrain.Value;
        if (roll < pitChance) return ChunkType.Pit;
        roll -= pitChance;
        if (roll < upChance) return ChunkType.UpSlope;
        roll -= upChance;
        if (roll < downChance) return ChunkType.DownSlope;
        return ChunkType.Flat;
    }

    // Bugfix 2026-09-09 - 地形が実際に生成してよい最低の高さ(これを下回る
    // 生成は行わない)。PlayerController.failY(死亡ライン)に安全マージン
    // を足した値 - PlayerController.Instanceがまだ存在しない(ビルド順の
    // 都合等)場合のフォールバックはPlayerController.failYの既定値と同じ
    // -8fを使う。
    public float terrainFloorMarginAboveDeadline = 1.5f;
    float MinGroundY => (PlayerController.Instance != null ? PlayerController.Instance.failY : -8f) + terrainFloorMarginAboveDeadline;

    void AddChunk(ChunkType type, float length)
    {
        float startX = nextStartX;
        float startY = nextStartY;
        float endX = startX + length;
        float endY = startY;

        if (type == ChunkType.UpSlope) endY = startY + slopeHeight;
        else if (type == ChunkType.DownSlope) endY = Mathf.Max(startY - slopeHeight, MinGroundY); // Bugfix 2026-09-09 - 保険のハードクランプ(PickNextType側で確率自体は既に0にしているが、念のため二重に保証)

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
                // Bugfix 2026-09-08, item4 - 「道の角が少し浮いたり、離れた
                // りする」報告。上のtan(theta)は幾何学的には既に必要量を満
                // たしているはずだが、実際の素材(岩/雲の下面)は完全な矩形
                // ではなく縁がギザギザ/丸みを帯びているため、テクスチャの
                // 不透明部分がクアッドの端まで届いておらず、ジオメトリ上は
                // 覆えていてもアルファの隙間として稀に見えてしまう -
                // cornerBleedSafetyMarginで少し多めに食い込ませ、素材の縁の
                // 不整形分を吸収する。
                leftBleed = outerDepth * Mathf.Tan(theta) * cornerBleedSafetyMargin;
            }

            // 崖面Collider追加(2026-09-15) - routeBranchEnabled(=荒野街道の
            // 間だけtrue)をそのままaddWallColliderへ渡すことで、天空回廊
            // (routeBranchEnabled常にfalse)には一切影響を与えない。
            chunk.visual = GroundFactory.CreateSlopeVisual(transform, squareSprite, groundSprite, platformArt,
                new Vector2(startX, startY), new Vector2(endX, endY), groundThickness, platformVisualHeight, platformSurfaceInset, groundColor,
                needsLeftCap, needsRightCap: false, leftBleed: leftBleed, addWallCollider: routeBranchEnabled);

            // 基礎品質修整 続報(2026-09-14) - マスター報告「道の曲がりで
            // 穴が気になる」の実機動画を確認したところ、実際にプレイヤーが
            // 落ちられる穴ではなく、flat<->slope継ぎ目でスラブの真下の
            // 断面帯(GroundFill)にだけ楔形の隙間が見えていた - 上のleft
            // Bleedはスラブ自身の薄い厚み(platformVisualHeight基準)向けに
            // 計算された値で、遥かに深いGroundFill(groundFillDepth基準)の
            // 外周まではカバーできていなかった(楔の幅は深さに比例して
            // 広がるため、スラブでは見えない量でもFillの下端では顕著に
            // なる)。同じtheta/cornerBleedSafetyMarginを使い、Fill自身の
            // 深さ基準でleftBleedを別途計算し直す。
            float fillLeftBleed = 0f;
            if (!needsLeftCap && platformArt.IsValid && (type == ChunkType.Flat) != (lastType == ChunkType.Flat))
            {
                float theta = Mathf.Atan2(slopeHeight, slopeLength);
                float fillOuterDepth = groundFillTopOffset - groundFillOverlap + groundFillDepth;
                fillLeftBleed = fillOuterDepth * Mathf.Tan(theta) * cornerBleedSafetyMargin;
            }

            // 荒野街道 地面埋め修整(2026-09-13深夜) - Pit以外の全チャンクの
            // 表面スラブの真下に、断面テクスチャの帯を敷く(このif自体が
            // ChunkType.Pitでは通らないため、穴は自動的に埋まらず可視の
            // ギャップとして残る)。
            if (groundFillSprite != null)
            {
                chunk.fillVisual = GroundFactory.CreateGroundFillVisual(transform, groundFillSprite,
                    new Vector2(startX, startY), new Vector2(endX, endY),
                    groundFillTopOffset, groundFillDepth, groundFillOverlap, RenderOrder.GroundFill, fillLeftBleed, groundFillTint,
                    needsLeftCap ? pitEdgeFillInset : 0f, 0f, needsLeftCap ? pitEdgeFillTopExtra : 0f);
            }

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
            bool safeForEnemy = !bossActive && !inSafeZone && type == ChunkType.Flat && lastType != ChunkType.Pit && FloatingOrigin.ToLogical(startX) >= noEnemyBeforeDistance;
            if (WorldRng.IsDeterministic)
            {
                ReserveFormationDeterministic(chunk, type, startX, startY, endX, !bossActive && !inSafeZone);
            }
            else if (safeForEnemy)
            {
                // 高速走行の視認性補正 - 速くなったぶん敵どうしの最低距離も広げる(反応時間を保つ)。
                float speedRatio = PlayerController.Instance != null ? Mathf.Max(1f, PlayerController.Instance.SpeedRatio) : 1f;
                bool spacingOk = startX - lastEnemyX > minEnemySpacing * speedRatio;
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
    // マルチプレイ対応Phase 1(2026-09-25) - 編成(Formation)は地形に「平地の予約」
    // (RequestFlatRun)を入れるため、シングルプレイと同じく「その時点のプレイヤー速度/
    // ボス戦中か/安全地帯か」で決めると、端末ごとに地形そのものがずれてしまう。
    // マルチプレイRun中だけは、全端末で一致する入力(チャンクの論理X・WorldSeed)のみで
    // 予約の有無と幅を決め、実際に敵を出すかどうか(ボス戦中等)はこの端末のローカル状況で
    // 判断する(敵自体はPhase 1ではネット同期しない)。
    void ReserveFormationDeterministic(RuntimeChunk chunk, ChunkType type, float startX, float startY, float endX, bool localSpawnAllowed)
    {
        float logicalX = FloatingOrigin.ToLogical(startX);
        if (type != ChunkType.Flat || lastType == ChunkType.Pit || logicalX < noEnemyBeforeDistance) return;
        if (DistanceTierManager.Instance == null) return;

        PlayerController pc = PlayerController.Instance;
        float speedRatio = 1f;
        if (pc != null && logicalX > pc.speedUpStartDistance)
            speedRatio = Mathf.Min(1f + (logicalX - pc.speedUpStartDistance) / 100f * pc.speedUpPer100m, pc.maxSpeedMultiplier);
        bool spacingOk = startX - lastEnemyX > minEnemySpacing * Mathf.Max(1f, speedRatio);

        if (!DistanceTierManager.Instance.TryStartFormationWorld(spacingOk, GetEnemyChance(), logicalX, out List<EnemySpawnRequest> requests, out float maxXOffsetNeeded))
            return;

        float rightMostNeededX = startX + maxXOffsetNeeded;
        int additionalChunks = Mathf.Max(0, Mathf.CeilToInt((rightMostNeededX - endX) / Mathf.Max(1f, flatLength)));
        if (additionalChunks > 0) RequestFlatRun(additionalChunks);
        lastEnemyX = startX;
        if (localSpawnAllowed) SpawnFormation(chunk, requests, startX, startY);
    }

    void SpawnFormation(RuntimeChunk chunk, List<EnemySpawnRequest> requests, float anchorX, float anchorY)
    {
        Camera cam = Camera.main;
        float? screenTopY = cam != null ? cam.transform.position.y + cam.orthographicSize - (DistanceTierManager.Instance != null ? DistanceTierManager.Instance.screenTopMargin : 0.6f) : (float?)null;

        foreach (EnemySpawnRequest req in requests)
        {
            float ex = anchorX + req.xOffset;
            SpawnFormationMember(chunk, req, ex, anchorY, screenTopY);
        }
    }

    // 雑魚敵配置修正(2026-09-10) - 「雑魚敵の配置を、Flying以外は地上の道の
    // 上にいるように」「Flyingは空中どこでもランダムに配置」。以前はFormation
    // が指定するyOffset(SpawnPointごとの高さパターン、VerticalLine/
    // DiagonalUp/GroundAir等で使用)がそのまま実際の配置高さになり、かつ
    // 「yOffset>0なら見た目もFlying扱いにする」実装だったため、その位置に
    // たまたま選ばれたNon-Flying種(Runner等、地上専用の走行アニメーション
    // しか持たない種族)までもが宙に浮いて見える不具合があった。実際の配置
    // 高さは種族自身のmovementTypeだけで決め、Formationのyoffsetは(役割/
    // カテゴリの決定にのみ使われ)高さの計算には一切使わないよう変更 -
    // Flying種はflyingSpawnMinHeight〜flyingSpawnMaxHeight(画面上端でも別
    // 途クランプ)の範囲でランダムな高度に、それ以外は必ずanchorY(実際に
    // 生成された地面の高さ)に接地させる。
    // 敵AI行動Tier試験実装(2026-09-16) - ラン距離を3つの帯(noEnemyBefore
    // Distance起点、debugTierTestSpanMetersごと)に分け、それぞれの帯で
    // 最初に来たNormal category枠を1回だけT0/T1/T2で横取りする。
    // TerrainManager自体に明示的な「ラン開始」コールバックが無いため、
    // 距離がほぼ0(=新しいランが始まった)まで戻ったことをリセットの合図
    // として使う。
    bool[] debugTierTestSpawnedFlags = new bool[3];

    EnemyDefinition TryGetDebugTierTestEnemy(EnemyCategory category)
    {
        if (category != EnemyCategory.Normal) return null;
        if (GameManager.Instance == null || !GameManager.Instance.DebugMode) return null;
        if (debugTierTestEnemies == null || debugTierTestEnemies.Length < 3) return null;

        float dist = GameManager.Instance.MaxDistance;
        if (dist < 1f)
        {
            for (int i = 0; i < debugTierTestSpawnedFlags.Length; i++) debugTierTestSpawnedFlags[i] = false;
        }

        for (int i = 0; i < 3; i++)
        {
            float windowStart = noEnemyBeforeDistance + debugTierTestSpanMeters * i;
            float windowEnd = noEnemyBeforeDistance + debugTierTestSpanMeters * (i + 1);
            if (!debugTierTestSpawnedFlags[i] && dist >= windowStart && dist < windowEnd)
            {
                debugTierTestSpawnedFlags[i] = true;
                return debugTierTestEnemies[i];
            }
        }
        return null;
    }

    void SpawnFormationMember(RuntimeChunk chunk, EnemySpawnRequest req, float ex, float anchorY, float? screenTopY)
    {
        // 敵AI行動Tier試験実装(2026-09-16) - DebugMode中だけ、通常のランダム
        // 抽選より優先してT0→T1→T2を順番に強制スポーンする(実機のDEBUG
        // トグルで比較テストできるようにする「テストしやすい構造」)。
        // Normal以外のcategory(Flying等)には絶対に割り込まない - 「Flying
        // 系以外の敵は地上に存在する」という前提を壊さないため。
        EnemyDefinition enemyDef = TryGetDebugTierTestEnemy(req.category);
        if (enemyDef == null) enemyDef = EnemyDatabase.PickRandomUnlockedOfCategory(enemyPool, req.category);
        // Category had nothing available (species not added to enemyPool
        // yet, or its art hasn't been dropped in) - the ORIGINAL
        // category-agnostic pick is still a reasonable fallback so a
        // Formation slot never spawns literally nothing.
        if (enemyDef == null) enemyDef = EnemyDatabase.PickRandomUnlocked(enemyPool);

        bool airborne = enemyDef != null && enemyDef.movementType == EnemyMovementType.Flying;

        float ey;
        if (airborne)
        {
            float minY = anchorY + flyingSpawnMinHeight;
            float maxY = anchorY + flyingSpawnMaxHeight;
            if (screenTopY.HasValue) maxY = Mathf.Min(maxY, screenTopY.Value);
            if (maxY < minY) maxY = minY;
            ey = Random.Range(minY, maxY);
        }
        else
        {
            ey = anchorY;
        }

        // Spawn Validation (item 2) - "画面上下端の外に出ない": a member
        // whose absolute Y would exceed the camera's own top edge is skipped
        // outright rather than spawned off-screen (ground-level members
        // never realistically hit this, but kept as a safety net).
        if (screenTopY.HasValue && ey > screenTopY.Value) return;

        Sprite eSprite = enemyDef != null ? enemyDef.sprite : (enemySprite != null ? enemySprite : squareSprite);
        Color eColor = enemyDef != null ? enemyDef.tint : (enemySprite != null ? Color.white : enemyColor);

        EnemyMovementType movementType = airborne ? EnemyMovementType.Flying : EnemyMovementType.Ground;
        float heightOffset = airborne ? 0f : groundEnemyHeight;

        int maxHp = DistanceTierManager.Instance != null && enemyDef != null ? DistanceTierManager.Instance.EnemyHpFor(enemyDef.hpMultiplier) : 1;
        EnemyBehaviorKind behaviorKind = enemyDef != null ? enemyDef.behaviorKind : EnemyBehaviorKind.None;
        bool bigKnockback = enemyDef != null && enemyDef.bigKnockbackOnHit;
        bool enableVisualFacing = enemyDef != null && enemyDef.enableVisualFacing;
        bool defaultFacingRight = enemyDef == null || enemyDef.defaultFacingRight;
        Sprite[] runFrames = enemyDef != null ? enemyDef.runFrames : null;

        int mileReward = enemyDef != null ? enemyDef.mileReward : 1;
        float visualScaleMultiplier = enemyDef != null ? enemyDef.visualScaleMultiplier : 1f;
        EnemyAiTier aiTier = enemyDef != null ? enemyDef.aiTier : EnemyAiTier.T0;
        GameObject enemyGO = GroundFactory.CreateEnemy(transform, eSprite, new Vector2(ex, ey + heightOffset), eColor, enemyHitSparkSprite, enemyDeathCloudSprite, movementType, enemyGroundShadowSprite, maxHp, behaviorKind, bigKnockback, shooterProjectileSprite, enableVisualFacing, defaultFacingRight, runFrames, mileReward, visualScaleMultiplier, aiTier, squareSprite);
        enemyGO.GetComponent<EnemyController>().movementType = movementType;
        GroundFactory.ApplyAttackSprite(enemyGO, enemyDef);
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
            prev.needsLeftCap, needsRightCap: true, addWallCollider: routeBranchEnabled);
        // 断面も、右端(穴の縁)がスラブの丸いキャップより飛び出さないよう作り直す。
        RebuildFill(chunks.Count - 1, true);
    }

    // How far the chunk currently being placed sits past the difficulty
    // start line, in units of 1000m. Used to ramp pit/enemy odds gently.
    float GetDifficultyProgress()
    {
        return Mathf.Max(0f, FloatingOrigin.ToLogical(nextStartX) - difficultyStartDistance) / 1000f;
    }

    float GetPitChance()
    {
        float chance = Mathf.Min(pitChanceBase + GetDifficultyProgress() * pitChanceRampPer1000m, pitChanceMax);
        // ルート構造再調整(2026-09-13) - 今生成中の地上チャンク(nextStartX,
        // AddChunk呼び出し時点でまだ加算前の値=このチャンクの開始X)が
        // 分岐区間の内側なら、下ルート(Danger)の危険度を上げる。上限は
        // 既存のpitChanceMaxのまま(理不尽な値までは上げない)。
        if (IsInBranchRoute(nextStartX)) chance = Mathf.Min(chance * branchDangerPitMultiplier, pitChanceMax);
        return chance;
    }

    float GetEnemyChance()
    {
        float chance = Mathf.Min(enemySpawnChance + GetDifficultyProgress() * enemyChanceRampPer1000m, enemyChanceMax);
        if (IsInBranchRoute(nextStartX)) chance = Mathf.Min(chance * branchDangerEnemyMultiplier, enemyChanceMax);
        return chance;
    }
}
