using System.IO;
using UnityEditor;
using UnityEngine;

// キャラクター選択画面(2026-09-12) - Assets/Resources/Characters/ に
// CharacterDefinitionアセットを(初回のみ)生成する。EnemyDatabaseBuilder/
// CardDatabaseBuilderと同じ「既存アセットは絶対に上書きしない」方針 -
// 星評価等をInspectorで手動調整した後にSceneBuilder.Buildを再実行しても、
// その調整値が消えない。
public static class CharacterDatabaseBuilder
{
    const string CharactersFolder = "Assets/Resources/Characters";
    const string PortraitFolder = "Assets/Art/UI/Characters";

    struct Spec
    {
        public string id;
        public string displayName;
        public string subtitle;
        public string role;
        public string flavorText;
        public string portraitPath;
        public int lifeRating, powerRating, speedRating, comboRating;
        public bool challengeFlag;
        public int sortOrder;
    }

    // 初期3人 - マスター提供の参考画像(Character Selectモックアップ)から
    // 切り出したカードイラストをそのままportrait/mainVisualとして使う
    // (専用の大判ビジュアルは今回用意していないため、両方に同じ画像を
    // 割り当てる - CharacterDefinition.mainVisualのコメント参照)。星評価は
    // 参考画像に実際に表示されていた値(黒剣士)をそのまま採用、双剣士/お
    // 嬢様騎士はマスターの説明文("Ground Combo・機動力"/"初期能力が
    // すべて最低クラス")から妥当な暫定値を割り当てた - 戦闘性能への反映は
    // まだ行わないため、実際のゲームプレイには影響しない表示専用の値。
    static Spec[] Specs() => new[]
    {
        new Spec
        {
            id = "swordsman",
            displayName = "SWORDSMAN",
            subtitle = "The One Who Keeps Moving",
            role = "BALANCED",
            flavorText = "A lone swordsman who walks his own path.\nNo matter how many times he falls,\nhe rises again - because there is still\na further place to reach.",
            portraitPath = $"{PortraitFolder}/swordsman_portrait.png",
            lifeRating = 3, powerRating = 4, speedRating = 3, comboRating = 3,
            challengeFlag = false,
            sortOrder = 0,
        },
        new Spec
        {
            id = "dual_blade",
            displayName = "DUAL BLADE",
            subtitle = "Swift Steel, Endless Motion",
            role = "GROUND COMBO",
            flavorText = "A swift dual-wielder who chains strikes\ntogether without ever slowing down.\nSpeed and momentum are her greatest weapons.",
            portraitPath = $"{PortraitFolder}/dual_blade_portrait.png",
            lifeRating = 2, powerRating = 3, speedRating = 5, comboRating = 4,
            challengeFlag = false,
            sortOrder = 1,
        },
        new Spec
        {
            id = "noble_lady",
            displayName = "NOBLE LADY",
            subtitle = "A Hero Not Yet Awakened",
            role = "CHALLENGE",
            flavorText = "She looks every bit the hero -\nbut her true strength has yet to awaken.\nFor those who seek a real challenge.",
            portraitPath = $"{PortraitFolder}/noble_lady_portrait.png",
            lifeRating = 1, powerRating = 1, speedRating = 1, comboRating = 1,
            challengeFlag = true,
            sortOrder = 2,
        },
    };

    [MenuItem("Tools/OneMoreMile/Build Character Database")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(CharactersFolder))
        {
            Directory.CreateDirectory(CharactersFolder);
            AssetDatabase.Refresh();
        }

        foreach (Spec spec in Specs())
        {
            string assetPath = $"{CharactersFolder}/{spec.id}.asset";
            CharacterDefinition existing = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(assetPath);
            if (existing != null) continue; // 既存のハンドチューニング値は一切上書きしない

            var def = ScriptableObject.CreateInstance<CharacterDefinition>();
            def.characterId = spec.id;
            def.displayName = spec.displayName;
            def.subtitle = spec.subtitle;
            def.role = spec.role;
            def.flavorText = spec.flavorText;
            Texture2D portrait = LoadIconTexture(spec.portraitPath);
            def.portrait = portrait;
            def.mainVisual = portrait;
            def.lifeRating = spec.lifeRating;
            def.powerRating = spec.powerRating;
            def.speedRating = spec.speedRating;
            def.comboRating = spec.comboRating;
            def.challengeFlag = spec.challengeFlag;
            def.sortOrder = spec.sortOrder;

            AssetDatabase.CreateAsset(def, assetPath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        CharacterDatabase.Reset();
    }

    // SceneBuilder.LoadIconTexture(private)と同じ設定 - CardDatabaseBuilder
    // 自身のLoadIconTextureと同じミラーパターン(Texture2Dとして読み込み、
    // 表示側でSprite.Createする方式に合わせる)。
    static Texture2D LoadIconTexture(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
