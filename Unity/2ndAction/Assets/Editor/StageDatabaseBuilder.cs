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
        },
        new Spec
        {
            id = "underground_ruins",
            displayName = "地下遺跡",
            enemyText = "敵: スケルトン / コウモリ",
            featureText = "障害物: 段差・落下床・トゲ・動く足場",
            routeText = "ルート: 未開放",
            unlocked = false,
            sortOrder = 1,
        },
        new Spec
        {
            id = "sky_corridor",
            displayName = "天空回廊",
            enemyText = "敵: フライヤー / 浮遊兵",
            featureText = "障害物: 強風・移動足場・崩れる床",
            routeText = "ルート: 未開放",
            unlocked = false,
            sortOrder = 2,
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
            if (existing != null) continue; // 既存のハンドチューニング値は一切上書きしない

            var def = ScriptableObject.CreateInstance<StageDefinition>();
            def.stageId = spec.id;
            def.displayName = spec.displayName;
            def.thumbnail = null; // サムネ未用意 - StageSelectUI/DrawStageHotspotは単色パネル+テキストへフォールバックする
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
}
