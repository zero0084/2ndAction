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

        // プレイアブル主人公追加(2026-09-12、お嬢様騎士) - 実プレイに反映
        // されるベース性能。黒剣士はPlayerControllerの既存デフォルトと
        // 完全一致させ、この仕組みを通しても性能が変化しないことを保証。
        public int baseLives, baseMaxLives;
        public int attackPower, attackComboCount;
        public float attackSpeedMultiplier, attackRangeMultiplier, knockbackPowerMultiplier;
        public int jumpCount;
        public float jumpForceMultiplier, groundMobilityMultiplier, airControlMultiplier;
        public bool canUseUpAttack, canUseAirAttack, canUseDownAttack;
    }

    // 黒剣士=PlayerController/GameManagerの既存デフォルトそのもの(性能
    // 保護のため、全倍率=1.0)。これをコピーしてキャラごとに差分だけ書く。
    static Spec DefaultBaseline => new Spec
    {
        baseLives = 3, baseMaxLives = 5,
        attackPower = 2, attackComboCount = 3,
        attackSpeedMultiplier = 1f, attackRangeMultiplier = 1f, knockbackPowerMultiplier = 1f,
        jumpCount = 2,
        jumpForceMultiplier = 1f, groundMobilityMultiplier = 1f, airControlMultiplier = 1f,
        canUseUpAttack = true, canUseAirAttack = true, canUseDownAttack = true,
    };

    // 初期3人 - マスター提供の参考画像(Character Selectモックアップ)から
    // 切り出したカードイラストをそのままportrait/mainVisualとして使う
    // (専用の大判ビジュアルは今回用意していないため、両方に同じ画像を
    // 割り当てる - CharacterDefinition.mainVisualのコメント参照)。星評価は
    // 参考画像に実際に表示されていた値(黒剣士)をそのまま採用、双剣士/お
    // 嬢様騎士はマスターの説明文("Ground Combo・機動力"/"初期能力が
    // すべて最低クラス")から妥当な暫定値を割り当てた - 戦闘性能への反映は
    // まだ行わないため、実際のゲームプレイには影響しない表示専用の値。
    // プレイアブル主人公追加(2026-09-12、お嬢様騎士) - 各Specは
    // DefaultBaseline(=黒剣士の既存性能そのまま)をコピーしてから差分だけ
    // 上書きする方式に変更。黒剣士自身は一切上書きしない=既存性能の完全
    // 保護、双剣士も今回は戦闘性能そのものへ手を付けない(将来の設計方針
    // はマスターの変更禁止指示どおり据え置き)ため黒剣士と同一のまま。
    static Spec[] Specs()
    {
        Spec swordsman = DefaultBaseline;
        swordsman.id = "swordsman";
        swordsman.displayName = "SWORDSMAN";
        swordsman.subtitle = "The One Who Keeps Moving";
        swordsman.role = "BALANCED";
        swordsman.flavorText = "A lone swordsman who walks his own path.\nNo matter how many times he falls,\nhe rises again - because there is still\na further place to reach.";
        swordsman.portraitPath = $"{PortraitFolder}/swordsman_portrait.png";
        swordsman.lifeRating = 3; swordsman.powerRating = 4; swordsman.speedRating = 3; swordsman.comboRating = 3;
        swordsman.challengeFlag = false;
        swordsman.sortOrder = 0;

        Spec dualBlade = DefaultBaseline;
        dualBlade.id = "dual_blade";
        dualBlade.displayName = "DUAL BLADE";
        dualBlade.subtitle = "Swift Steel, Endless Motion";
        dualBlade.role = "GROUND COMBO";
        dualBlade.flavorText = "A swift dual-wielder who chains strikes\ntogether without ever slowing down.\nSpeed and momentum are her greatest weapons.";
        dualBlade.portraitPath = $"{PortraitFolder}/dual_blade_portrait.png";
        dualBlade.lifeRating = 2; dualBlade.powerRating = 3; dualBlade.speedRating = 5; dualBlade.comboRating = 4;
        dualBlade.challengeFlag = false;
        dualBlade.sortOrder = 1;
        // 戦闘性能の差別化は「双剣士の今後の設計方針」そのもの(今回の
        // 変更禁止対象)のため、意図的にDefaultBaseline(黒剣士と同一)の
        // ままにしてある。

        // お嬢様騎士 - マスター指示「見た目は非常に強そうだが性能はかなり
        // 弱い、ただし入力遅延ではなく性能値のみで表現する」。CHALLENGE
        // HERO等の特別バッジは今回のマスター指示で明示的に廃止
        // (challengeFlag=false、role=通常の名称のみ)。
        Spec nobleLady = DefaultBaseline;
        nobleLady.id = "noble_lady";
        nobleLady.displayName = "NOBLE LADY";
        nobleLady.subtitle = "A Hero Not Yet Awakened";
        // マスターとエステル(相談用ChatGPT)のレビューで「HEAVY=強い/硬い
        // 印象があり、LIFE3・POWER1のこのキャラには誤解を招く」との指摘
        // を反映し、性能タイプを一切示唆しないNOBLE KNIGHTへ変更(2026-09-
        // 13)。星評価を見て初めて弱さに気づく、というギャップ狙いを維持。
        nobleLady.role = "NOBLE KNIGHT";
        nobleLady.flavorText = "Clad in silver and gold, she wields a\ngreatsword said to fell dragons.\nHer legend, however, has yet to catch\nup with her armor.";
        nobleLady.portraitPath = $"{PortraitFolder}/noble_lady_portrait.png";
        // 表示専用の星評価 - マスター提示例(LIFE3/POWER1/SPEED1/COMBO1)
        // に合わせ、他2キャラより明確に見劣りするようにする。
        nobleLady.lifeRating = 3; nobleLady.powerRating = 1; nobleLady.speedRating = 1; nobleLady.comboRating = 1;
        nobleLady.challengeFlag = false;
        nobleLady.sortOrder = 2;

        // ここから実プレイに反映される値(マスター初期案どおり)。
        nobleLady.baseLives = 3;
        nobleLady.baseMaxLives = 3;
        nobleLady.attackPower = 1; // 黒剣士の2より低い、POWER最低
        nobleLady.attackComboCount = 1; // 1段止まり、Attack2/3へ接続しない
        nobleLady.attackSpeedMultiplier = 1.35f; // 大きいほど遅い(既存のAddAttackSpeedBonusと同じ方向)
        nobleLady.attackRangeMultiplier = 0.75f; // リーチ最低
        nobleLady.knockbackPowerMultiplier = 0.5f; // 吹き飛ばし最低
        nobleLady.jumpCount = 1; // 二段ジャンプなし
        // 「完全に詰む高さにはしない」との明示指示のため控えめな
        // 減少幅に留めた - 実機で既存ステージの隙間が飛び越えられるか
        // 必ず確認すること(マスターへの開示事項、Inspectorでも調整可)。
        nobleLady.jumpForceMultiplier = 0.85f;
        nobleLady.groundMobilityMultiplier = 0.95f; // 地上機動力最低(ノックバックは速度追従方式のため副作用なし)
        // 実機確認+エステル(相談用ChatGPT)のレビューを受けて撤回(2026-09-
        // 13) - 重力を強めると「ジャンプが低い上に落下も速く、穴を越え
        // られない」リスクがあり、逆に弱めると滞空時間が伸びて穴越えが
        // 楽になってしまう。「穴を越えるのがギリギリ」は既にjumpForce
        // Multiplier(0.85)+jumpCount(1)だけで十分表現できるため、
        // airControlMultiplierは他キャラと同じ1.0(無変更)に戻した。
        nobleLady.airControlMultiplier = 1f;
        // 「技自体を持たせない/接続しない」というマスターの理想形どおり、
        // 上/空中/下の3攻撃はまず全て未接続にする(今回のスコープ)。
        nobleLady.canUseUpAttack = false;
        nobleLady.canUseAirAttack = false;
        nobleLady.canUseDownAttack = false;

        return new[] { swordsman, dualBlade, nobleLady };
    }

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

            def.baseLives = spec.baseLives;
            def.baseMaxLives = spec.baseMaxLives;
            def.attackPower = spec.attackPower;
            def.attackComboCount = spec.attackComboCount;
            def.attackSpeedMultiplier = spec.attackSpeedMultiplier;
            def.attackRangeMultiplier = spec.attackRangeMultiplier;
            def.knockbackPowerMultiplier = spec.knockbackPowerMultiplier;
            def.jumpCount = spec.jumpCount;
            def.jumpForceMultiplier = spec.jumpForceMultiplier;
            def.groundMobilityMultiplier = spec.groundMobilityMultiplier;
            def.airControlMultiplier = spec.airControlMultiplier;
            def.canUseUpAttack = spec.canUseUpAttack;
            def.canUseAirAttack = spec.canUseAirAttack;
            def.canUseDownAttack = spec.canUseDownAttack;

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
