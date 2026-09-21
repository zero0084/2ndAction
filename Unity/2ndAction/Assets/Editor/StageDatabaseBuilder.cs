using System.IO;
using UnityEditor;
using UnityEngine;

// ステージ選択導線追加(2026-09-12) - Assets/Resources/Stages/ に
// StageDefinitionアセットを(初回のみ)生成する。CharacterDatabaseBuilderと
// 同じ「既存アセットは絶対に上書きしない」方針。
public static class StageDatabaseBuilder
{
    const string StagesFolder = "Assets/Resources/Stages";

    struct Spec
    {
        public string id;
        public string displayName;
        public string enemyText;
        public string featureText;
        public string routeText;
        public bool unlocked;
        public int sortOrder;
        // Stage01完成版要求仕様書対応(2026-09-13) - 未指定(null/空文字)なら
        // 従来どおりStageSelectUIが単色パネル+テキストへフォールバックする
        // (CharacterDatabaseBuilder.LoadIconTextureをinternal化して再利用)。
        public string thumbnailPath;
    }

    // 初期3ステージ - 実際に遊べるのは荒野街道のみ(現状のTerrainManagerは
    // 単一の連続手続き生成トラックのため、複数ステージの実体分岐はまだ
    // 無い、StageDefinition.csのコメント参照)。地下遺跡/天空回廊は
    // マスター指示どおり「未開放表示のダミー」として先に並べておく。
    static Spec[] Specs() => new[]
    {
        new Spec
        {
            id = "wasteland_road",
            displayName = "荒野街道",
            enemyText = "敵: ゴブリン / 鳥",
            featureText = "障害物: 石・小木・壁・壊せる木・巨大石・穴",
            routeText = "ルート: 上 Easy / 下 Danger",
            unlocked = true,
            sortOrder = 0,
            thumbnailPath = "Assets/Art/Background/WastelandThumbnail.png",
        },
        new Spec
        {
            id = "natural_cave",
            displayName = "自然洞窟",
            enemyText = "敵: ゴブリン / 鳥",
            featureText = "特徴: 岩の天井・天井の針・暗がりとたいまつ",
            routeText = "ルート: 上 Easy / 下 Danger",
            unlocked = true,
            sortOrder = 1,
            thumbnailPath = "Assets/Art/Background/CaveThumbnail.png",
        },
        new Spec
        {
            // マスター指示(2026-09-13)「いままでの天空マップを天空回廊
            // として選択できるように」- これまでこのゲームがずっと使って
            // きた既存コンテンツ(岩+雲の浮遊足場アート、ゴブリン/フライ
            // ング等の全Enemy進行、上空の道)をそのまま指す。TerrainManager.
            // stageThemesにこのIDのエントリを追加していない限り、荒野街道
            // 追加前と見た目・中身は完全に無改造のまま(ApplyStageThemeの
            // コメント参照)。
            id = "sky_corridor",
            displayName = "天空回廊",
            enemyText = "敵: ゴブリン系 + 距離進行で徐々に解禁",
            featureText = "特徴: 岩と雲の浮遊足場、上空の道",
            routeText = "ルート: 進むほど危険度が上昇",
            unlocked = true,
            sortOrder = 2,
            // Home画面改善依頼③(2026-09-15) - 新規画像生成はせず、既存の
            // 天空回廊デフォルト背景をそのままプレビューに流用する(マスター
            // 指示「新しい画像をUnity側で生成する必要はありません」に対応)。
            thumbnailPath = "Assets/Art/Background/background.png",
        },
    };

    [MenuItem("Tools/OneMoreMile/Build Stage Database")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(StagesFolder))
        {
            Directory.CreateDirectory(StagesFolder);
            AssetDatabase.Refresh();
        }

        foreach (Spec spec in Specs())
        {
            string assetPath = $"{StagesFolder}/{spec.id}.asset";
            StageDefinition existing = AssetDatabase.LoadAssetAtPath<StageDefinition>(assetPath);
            if (existing != null)
            {
                // Home画面改善依頼③(2026-09-15) - 既存アセットの他フィールド
                // には触れず、thumbnailが未設定(null)の場合だけバックフィル
                // する(荒野街道は既に設定済みなのでこの分岐には来ない)。
                if (existing.thumbnail == null && !string.IsNullOrEmpty(spec.thumbnailPath))
                {
                    existing.thumbnail = LoadThumbnailTexture(spec.thumbnailPath);
                    EditorUtility.SetDirty(existing);
                }
                continue;
            }

            var def = ScriptableObject.CreateInstance<StageDefinition>();
            def.stageId = spec.id;
            def.displayName = spec.displayName;
            def.thumbnail = !string.IsNullOrEmpty(spec.thumbnailPath) ? LoadThumbnailTexture(spec.thumbnailPath) : null;
            def.enemyText = spec.enemyText;
            def.featureText = spec.featureText;
            def.routeText = spec.routeText;
            def.unlocked = spec.unlocked;
            def.sortOrder = spec.sortOrder;

            AssetDatabase.CreateAsset(def, assetPath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        StageDatabase.Reset();
    }

    // Home画面改善依頼③(2026-09-15) - background.png等、既に他の用途
    // (Sprite等)でインポート設定済みの画像を「壊さず」再利用するための
    // 専用ローダー。CharacterDatabaseBuilder.LoadIconTextureはtextureType
    // をDefaultへ強制上書きするため、既にSprite型でインポート済みの画像
    // (例: 天空回廊のbackground.png、SceneBuilder.Buildで実際のゲーム背景
    // としてSprite参照されている)に使うと、そちらの参照がnullになって
    // 天空回廊の背景が消える regression を起こす(実機ビルドで実際に発生を
    // 確認し、このヘルパーへ差し替えて修正した)。単純にTexture2Dとして
    // 読み込むだけで、インポート設定には一切触れない。
    static Texture2D LoadThumbnailTexture(string path)
    {
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
