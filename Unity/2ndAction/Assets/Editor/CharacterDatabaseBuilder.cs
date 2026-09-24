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

        // キャラクター専用アニメーション差し替え(2026-09-13) - 空文字なら
        // そのStateは黒剣士のSceneBuilder焼き込みアートのまま(マスター
        // 許可の「一時的に既存アニメーション流用」に対応、黒剣士/双剣士は
        // 現時点で全て空のまま)。folderPPUは各フォルダの「そのState開始
        // 直後に最初に見えるフレーム」の実測コンテンツ高さ÷1.13(黒剣士の
        // 基準身長)から算出した値 - SceneBuilder既存の各PlayerXxx_v1
        // フォルダと同じ考え方。
        public string runFramesDir;
        public float runFramesPpu;
        // お嬢様騎士Run読みやすさ改善(2026-09-13) - runFramesDirのコマ数が
        // 黒剣士(PlayerAnimator.runFps基準)と異なる場合の再生速度上書き。
        // 0なら黒剣士のrunFpsをそのまま使う。
        public float runFpsOverride;
        public string jumpStartFramesDir;
        public float jumpStartFramesPpu;
        public string jumpFramesDir;
        public float jumpFramesPpu;
        // お嬢様騎士 二段ジャンプ演出バグ修正(2026-09-13) - 未指定のままだと
        // PlayerAnimator.doubleJumpFramesが黒剣士の焼き込みアート(空中上
        // 攻撃の絵)のままになる(CharacterDefinition.doubleJumpFramesの
        // コメント参照)。
        public string doubleJumpFramesDir;
        public float doubleJumpFramesPpu;
        public string landFramesDir;
        public float landFramesPpu;
        public string attackFramesDir;
        public float attackFramesPpu;
        // 3人目の主人公追加(2026-09-13、双剣士) - 5段コンボを3段階アート
        // (小/中/大)で表現するための追加スロット。空文字ならその段階は
        // 黒剣士のデフォルトへフォールバックする(CharacterDefinition.
        // attackFramesSmall/Largeのコメント参照)。
        public string attackFramesSmallDir;
        public float attackFramesSmallPpu;
        public string attackFramesLargeDir;
        public float attackFramesLargePpu;
        // 双剣士専用アニメ追加(2026-09-13深夜) - 下攻撃/下攻撃着地も他の
        // Stateと同じ「空文字なら黒剣士のデフォルトへフォールバック」方式
        // にした(CharacterDefinition.downAttackFrames/downAttackLandFrames
        // のコメント参照)。
        public string downAttackFramesDir;
        public float downAttackFramesPpu;
        public string downAttackLandFramesDir;
        public float downAttackLandFramesPpu;

        // RUN開始準備/正常終了演出(2026-09-23) - 今回は全キャラ未設定
        // (空文字)のまま=LoadAnimationFolderが空配列を返し、PlayerAnimator
        // 側の手続き的フォールバックが使われる。後でフォルダを用意したら
        // ここへパスを足すだけで、SceneBuilder.Build再実行時に反映される。
        public string startFramesDir;
        public float startFramesPpu;
        public string finishShortFramesDir;
        public float finishShortFramesPpu;
        public string finishMediumFramesDir;
        public float finishMediumFramesPpu;
        public string finishLongFramesDir;
        public float finishLongFramesPpu;
        public string finishExtremeFramesDir;
        public float finishExtremeFramesPpu;

        // 二丁拳銃士追加(2026-09-23) - 遠距離キャラクター専用データ。
        // isRanged=falseの間は以下全て無視される(既存3キャラには一切影響
        // しない)。
        public bool isRanged;
        public string bulletSpritePath;
        public float bulletSpeed;
        public float bulletLifetime;
        public float hoverDuration;
        public float hoverFallSpeed;
        public string upShotFramesDir;
        public float upShotFramesPpu;

        // Home画面改善依頼③(2026-09-15) - 持ち物表示用データ。実画像は
        // まだ用意していないため、labelとplaceholderColorのみを指定する
        // (CharacterDefinition.BelongingItem.iconはnullのまま = GameManager
        // 側が簡易プレースホルダー表示にフォールバックする)。後で専用の
        // アイコン画像を用意したら、このSpecへ画像パスを足すかInspectorで
        // iconを直接差し替えるだけで済む。
        public CharacterDefinition.BelongingItem[] belongings;
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
        // Home画面改善依頼③(2026-09-15) - 「独り道を歩み続ける剣士」という
        // flavorTextに沿った持ち物3点。
        swordsman.belongings = new[]
        {
            new CharacterDefinition.BelongingItem { label = "剣", placeholderColor = new Color(0.75f, 0.78f, 0.85f), icon = LoadIconTexture("Assets/Art/UI/Characters/SwordsmanItems/swordsman_item_0.png"), kind = CharacterDefinition.BelongingKind.Weapon },
            new CharacterDefinition.BelongingItem { label = "外套", placeholderColor = new Color(0.25f, 0.25f, 0.3f), icon = LoadIconTexture("Assets/Art/UI/Characters/SwordsmanItems/swordsman_item_1.png"), kind = CharacterDefinition.BelongingKind.Cloth },
            new CharacterDefinition.BelongingItem { label = "紋章", placeholderColor = new Color(0.6f, 0.5f, 0.25f), icon = LoadIconTexture("Assets/Art/UI/Characters/SwordsmanItems/swordsman_item_2.png"), kind = CharacterDefinition.BelongingKind.Shield },
        };
        // RUN開始準備/正常終了演出 本番素材化(2026-09-24) - ChatGPT生成の
        // 実イラスト(2ポーズ)に差し替え。PPUは他Stateと同じ「frame0の実測
        // コンテンツ高さ÷1.13」方式。
        swordsman.startFramesDir = "Assets/Art/PlayerStart_v1";
        swordsman.startFramesPpu = 668.1f; // 755/1.13
        swordsman.finishShortFramesDir = "Assets/Art/PlayerFinishShort_v1";
        swordsman.finishShortFramesPpu = 641.6f; // 725/1.13
        swordsman.finishMediumFramesDir = "Assets/Art/PlayerFinishMedium_v1";
        swordsman.finishMediumFramesPpu = 682.3f; // 771/1.13
        swordsman.finishLongFramesDir = "Assets/Art/PlayerFinishLong_v1";
        swordsman.finishLongFramesPpu = 585.8f; // 662/1.13
        swordsman.finishExtremeFramesDir = "Assets/Art/PlayerFinishExtreme_v1";
        swordsman.finishExtremeFramesPpu = 642.5f; // 726/1.13

        Spec dualBlade = DefaultBaseline;
        dualBlade.id = "dual_blade";
        dualBlade.displayName = "DUAL BLADE";
        dualBlade.subtitle = "Swift Steel, Endless Motion";
        dualBlade.role = "GROUND COMBO";
        dualBlade.flavorText = "A swift dual-wielder who chains strikes\ntogether without ever slowing down.\nSpeed and momentum are her greatest weapons.";
        dualBlade.portraitPath = $"{PortraitFolder}/dual_blade_portrait.png";
        // マスター指示(2026-09-13、3人目主人公追加)の星評価方向どおり -
        // 「黒剣士より手数・速度寄り」と伝わる表示。LIFEは黒剣士と同等
        // (実際のbaseLives=5、黒剣士の3より多いので星も高め)。
        dualBlade.lifeRating = 4; dualBlade.powerRating = 2; dualBlade.speedRating = 5; dualBlade.comboRating = 5;
        dualBlade.challengeFlag = false;
        dualBlade.sortOrder = 1;
        // Home画面改善依頼③(2026-09-15) - マスター指定の例(双剣/羽根モチーフ
        // /軽装らしい小物)をそのまま採用。
        dualBlade.belongings = new[]
        {
            new CharacterDefinition.BelongingItem { label = "双剣", placeholderColor = new Color(0.55f, 0.75f, 0.85f), icon = LoadIconTexture("Assets/Art/UI/Characters/DualBladeItems/dual_blade_item_0.png"), kind = CharacterDefinition.BelongingKind.Weapon },
            new CharacterDefinition.BelongingItem { label = "羽根", placeholderColor = new Color(0.9f, 0.9f, 0.95f), icon = LoadIconTexture("Assets/Art/UI/Characters/DualBladeItems/dual_blade_item_1.png"), kind = CharacterDefinition.BelongingKind.Cloth },
            new CharacterDefinition.BelongingItem { label = "布飾り", placeholderColor = new Color(0.2f, 0.55f, 0.55f), icon = LoadIconTexture("Assets/Art/UI/Characters/DualBladeItems/dual_blade_item_2.png"), kind = CharacterDefinition.BelongingKind.Small },
        };

        // 3人目のプレイアブル主人公(2026-09-13) - マスター初期案どおりの
        // 実プレイ反映値。「地上コンボ型 - 一発は軽いが手数・速度・機動力
        // で黒剣士を上回り、大きな吹き飛ばしはしない」という差別化方針。
        dualBlade.baseLives = 5;
        dualBlade.baseMaxLives = 5;
        dualBlade.attackPower = 1; // 黒剣士の2より低い(一発の重さより手数で削るキャラ)
        dualBlade.attackComboCount = 5; // 黒剣士の3より多い、5段の高速連撃
        dualBlade.attackSpeedMultiplier = 0.75f; // 小さいほど速い(既存のAddAttackSpeedBonusと同じ方向) - 高速連撃
        dualBlade.attackRangeMultiplier = 0.85f; // 黒剣士よりやや短め(コンパクトな双剣の間合い)
        dualBlade.knockbackPowerMultiplier = 0.6f; // 黒剣士より弱め(大きく吹き飛ばさず地上で刻む設計)
        dualBlade.jumpCount = 2; // 黒剣士と同じ(二段ジャンプ対応)
        dualBlade.jumpForceMultiplier = 1f; // マスター案の「黒剣士と同等」を採用(据え置きが最も安全)
        dualBlade.groundMobilityMultiplier = 1.15f; // 黒剣士より高い地上機動力(速度追従方式のため副作用なし)
        dualBlade.airControlMultiplier = 1f; // 標準
        // 上/空中/下攻撃は黒剣士と同じく全て有効(マスター指示「空中技を
        // 主役にしない」ため性能そのものは黒剣士の初期値のまま、DefaultBaseline
        // 由来のtrue/true/trueを変更しない) - このキャラの個性は通常地上
        // コンボ側の速度/手数/リーチ/ノックバックの差で表現する。

        // 見た目(2026-09-13) - マスター提供の参考イラスト(銀髪ポニーテール
        // /濃紺軽装/ティール布アクセント/双剣)を基準に生成した専用アニメ。
        // お嬢様騎士のPPUは各フォルダごとに異なる値だった(フォルダごとに
        // 元画像の実測コンテンツ高さがバラバラだったため)が、双剣士は
        // 生成/加工パイプライン側で全フォルダの全フレームを同一の目標値
        // (実測コンテンツ高さ306px、= PlayerRun_v1の基準1.125world units)
        // へリサイズ済みのため、Run/JumpStart/JumpAir/Land/Attack(3段階)の
        // 全てで同一PPU=272を使う(=黒剣士のRunと全く同じ基準身長)。
        // Runのみ全コマ共通の接地ライン(LoadRunAnimationFolder)、それ以外は
        // フレームごとの個別Foot Pivot自動検出(LoadAnimationFolder)。
        dualBlade.runFramesDir = "Assets/Art/DualBladeRun_v1";
        dualBlade.runFramesPpu = 272f;
        // Run素材再差し替え(2026-09-13深夜) - マスター確認済みの「頭基準
        // ピボット・2コマ構成」はそのまま維持しつつ、絵そのものをより
        // 「疾走感」のある低い重心・大きな歩幅のダッシュポーズへ差し替え。
        // fps(7)は前回確認済みの値のまま据え置き(今回はコマ数・速度では
        // なく絵柄の変更が主目的のため)。
        dualBlade.runFpsOverride = 7f;
        dualBlade.jumpStartFramesDir = "Assets/Art/DualBladeJumpStart_v1";
        dualBlade.jumpStartFramesPpu = 272f;
        dualBlade.jumpFramesDir = "Assets/Art/DualBladeJumpAir_v1";
        dualBlade.jumpFramesPpu = 272f;
        // 双剣士専用アニメ追加(2026-09-13深夜) - マスター報告「上空中攻撃
        // が黒剣士と同じアニメーションになっている」に対応。双剣士は
        // canUseAirAttack=trueのため、DoubleJump Stateの絵は実際にプレイ
        // 中に見える(お嬢様騎士のような純粋演出用途とは違う)。
        dualBlade.doubleJumpFramesDir = "Assets/Art/DualBladeDoubleJump_v1";
        dualBlade.doubleJumpFramesPpu = 272f;
        dualBlade.landFramesDir = "Assets/Art/DualBladeLand_v1";
        dualBlade.landFramesPpu = 272f;
        // 通常攻撃コンボ(最重要) - 5段の高速連撃を「素早い右手斬り(小)/
        // 返す左手斬り(中)/踏み込み連斬・締めの交差斬り(大)」の3段階アート
        // で表現する(PlayerAnimator.GetAttackFramesの既存3段階フォール
        // バック機構をそのまま利用 - stage<=1→Small、stage>=3→Large、
        // それ以外→Mid)。CharacterDefinition.attackFramesSmall/Largeを
        // 新設して黒剣士以外のキャラでも3段階を持てるようにした。
        dualBlade.attackFramesDir = "Assets/Art/DualBladeAttackMid_v1";
        dualBlade.attackFramesPpu = 272f;
        dualBlade.attackFramesSmallDir = "Assets/Art/DualBladeAttackSmall_v1";
        dualBlade.attackFramesSmallPpu = 272f;
        dualBlade.attackFramesLargeDir = "Assets/Art/DualBladeAttackLarge_v1";
        dualBlade.attackFramesLargePpu = 272f;
        // 双剣士専用アニメ追加(2026-09-13深夜) - マスター報告「下攻撃/
        // 下着地が黒剣士と同じになっている」に対応。双剣士はcanUseDown
        // Attack=trueのため、この2つも実プレイ中に見える。
        dualBlade.downAttackFramesDir = "Assets/Art/DualBladeDownAttack_v1";
        dualBlade.downAttackFramesPpu = 272f;
        dualBlade.downAttackLandFramesDir = "Assets/Art/DualBladeDownAttackLand_v1";
        dualBlade.downAttackLandFramesPpu = 272f;
        // RUN開始準備/正常終了演出 本番素材化(2026-09-24)
        dualBlade.startFramesDir = "Assets/Art/DualBladeStart_v1";
        dualBlade.startFramesPpu = 680.5f; // 769/1.13
        dualBlade.finishShortFramesDir = "Assets/Art/DualBladeFinishShort_v1";
        dualBlade.finishShortFramesPpu = 585.0f; // 661/1.13
        dualBlade.finishMediumFramesDir = "Assets/Art/DualBladeFinishMedium_v1";
        dualBlade.finishMediumFramesPpu = 725.7f; // 820/1.13
        dualBlade.finishLongFramesDir = "Assets/Art/DualBladeFinishLong_v1";
        dualBlade.finishLongFramesPpu = 911.5f; // 1030/1.13
        dualBlade.finishExtremeFramesDir = "Assets/Art/DualBladeFinishExtreme_v1";
        dualBlade.finishExtremeFramesPpu = 597.3f; // 675/1.13

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
        // Home画面改善依頼③(2026-09-15) - マスター指定の例(大剣/王冠モチーフ
        // /青金の紋章)をそのまま採用。
        nobleLady.belongings = new[]
        {
            new CharacterDefinition.BelongingItem { label = "大剣", placeholderColor = new Color(0.8f, 0.8f, 0.85f), icon = LoadIconTexture("Assets/Art/UI/Characters/NobleLadyItems/noble_lady_item_0.png"), kind = CharacterDefinition.BelongingKind.Weapon },
            new CharacterDefinition.BelongingItem { label = "王冠", placeholderColor = new Color(0.95f, 0.85f, 0.35f), icon = LoadIconTexture("Assets/Art/UI/Characters/NobleLadyItems/noble_lady_item_1.png"), kind = CharacterDefinition.BelongingKind.Small },
            new CharacterDefinition.BelongingItem { label = "紋章", placeholderColor = new Color(0.25f, 0.35f, 0.75f), icon = LoadIconTexture("Assets/Art/UI/Characters/NobleLadyItems/noble_lady_item_2.png"), kind = CharacterDefinition.BelongingKind.Shield },
        };

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

        // キャラクター専用アニメーション差し替え(2026-09-13) - ChatGPTで
        // 生成した専用スプライトシート(走り7コマ/ジャンプ4コマを3分割/
        // 通常攻撃4コマ)。PPUは各フォルダの「そのState開始直後に最初に
        // 見えるフレーム」の実測コンテンツ高さ(bottom-up alphaスキャン)
        // ÷1.13(黒剣士PlayerRun_v1と同じ基準身長)で算出した一次値 -
        // 実機で黒剣士と並べて見た際にサイズ差があれば要再調整(マスター
        // への開示事項)。jumpStartFrames/jumpFramesは黒剣士の「上攻撃」
        // ではなく素のジャンプ演出として再定義している(PlayerAnimator.
        // ApplyCharacterAnimationSetのコメント参照) - このキャラは上攻撃
        // 自体を持たない(canUseUpAttack=false)ため意味の衝突は起きない。
        nobleLady.runFramesDir = "Assets/Art/NobleLadyRun_v1";
        nobleLady.runFramesPpu = 272f;
        // お嬢様騎士Run読みやすさ改善(2026-09-13) - 「片足で走っているよう
        // に見える」報告への対応で、左右交互の接地/パッシングポーズが
        // はっきり読める4コマ(接地A/パッシング/接地B/パッシング)へ全面的
        // に描き直した。黒剣士と共有のPlayerAnimator.runFps(10fps)を7コマ
        // にそのまま適用していた頃と同じ1周期の長さ(7/10=0.7秒)を保つよう、
        // 4コマ用に6fps(4/6≈0.67秒)へこのキャラだけ再生速度を落とす。
        nobleLady.runFpsOverride = 6f;
        nobleLady.jumpStartFramesDir = "Assets/Art/NobleLadyJumpStart_v1";
        nobleLady.jumpStartFramesPpu = 249f;
        nobleLady.jumpFramesDir = "Assets/Art/NobleLadyJumpAir_v1";
        nobleLady.jumpFramesPpu = 350f;
        // お嬢様騎士 二段ジャンプ演出バグ修正(2026-09-13) - 剣を掲げる
        // 華麗な専用フラッシュポーズ(実際の空中攻撃ではなく見た目だけの
        // 演出、canUseAirAttack=falseのまま)。PPUはjumpFramesと同じ基準
        // 身長(実測コンテンツ高さ÷1.13、jumpair_00.pngの397px÷350ppu≒
        // 1.134が基準)に揃えて算出(1384px÷1.134≒1220.5)。
        nobleLady.doubleJumpFramesDir = "Assets/Art/NobleLadyDoubleJump_v1";
        nobleLady.doubleJumpFramesPpu = 1220.5f;
        nobleLady.landFramesDir = "Assets/Art/NobleLadyLand_v1";
        nobleLady.landFramesPpu = 282f;
        nobleLady.attackFramesDir = "Assets/Art/NobleLadyAttack_v1";
        nobleLady.attackFramesPpu = 310f;
        // RUN開始準備/正常終了演出 本番素材化(2026-09-24) - マスター指示
        // 「見た目は強そうなのに実際は弱いというギャップ」を踏まえ、超長
        // 距離Finishでは完全に疲れ切って座り込むポーズを他キャラより強調。
        nobleLady.startFramesDir = "Assets/Art/NobleLadyStart_v1";
        nobleLady.startFramesPpu = 761.1f; // 860/1.13
        nobleLady.finishShortFramesDir = "Assets/Art/NobleLadyFinishShort_v1";
        nobleLady.finishShortFramesPpu = 708.0f; // 800/1.13
        nobleLady.finishMediumFramesDir = "Assets/Art/NobleLadyFinishMedium_v1";
        nobleLady.finishMediumFramesPpu = 710.6f; // 803/1.13
        nobleLady.finishLongFramesDir = "Assets/Art/NobleLadyFinishLong_v1";
        nobleLady.finishLongFramesPpu = 718.6f; // 812/1.13
        nobleLady.finishExtremeFramesDir = "Assets/Art/NobleLadyFinishExtreme_v1";
        nobleLady.finishExtremeFramesPpu = 667.3f; // 754/1.13

        // 4人目のプレイアブル主人公(2026-09-23、二丁拳銃士) - 「面で攻撃する
        // 剣士」に対して「点で攻撃する遠距離キャラクター」という構造その
        // ものが違う枠。isRanged=trueがPlayerController側の全く別の攻撃
        // ロジック(DoRangedForwardBackShot/DoRangedUpShot/DoRangedDownShot)
        // へ分岐させるスイッチになる。内部名は正式名称未決定のため仮
        // (GUNSLINGER)。
        Spec gunslinger = DefaultBaseline;
        gunslinger.id = "gunslinger";
        gunslinger.displayName = "GUNSLINGER";
        gunslinger.subtitle = "One Shot, One Distance";
        gunslinger.role = "RANGED";
        gunslinger.flavorText = "Twin pistols, a single sharp line of fire.\nShe wins the fight before it ever\ncloses in - but let an enemy through,\nand she has no answer for it.";
        gunslinger.portraitPath = $"{PortraitFolder}/gunslinger_portrait.png";
        // 表示専用の星評価 - 「遠距離では非常に強いが近距離が苦手」という
        // 武器特性を伝える暫定値(POWERは高いがLIFE低め、近接キャラとは
        // 違う尖り方であることを示す)。
        gunslinger.lifeRating = 2; gunslinger.powerRating = 4; gunslinger.speedRating = 3; gunslinger.comboRating = 3;
        gunslinger.challengeFlag = false;
        gunslinger.sortOrder = 3;
        gunslinger.belongings = new[]
        {
            new CharacterDefinition.BelongingItem { label = "二丁拳銃", placeholderColor = new Color(0.3f, 0.3f, 0.35f), kind = CharacterDefinition.BelongingKind.Weapon },
            new CharacterDefinition.BelongingItem { label = "外套", placeholderColor = new Color(0.2f, 0.22f, 0.28f), kind = CharacterDefinition.BelongingKind.Cloth },
        };

        // 実プレイ反映値 - 「まずは4方向射撃の操作感確認を優先」との指示
        // どおり、極端な調整はせず既存キャラと同程度のレンジに収める
        // (Cooldown/AttackSpeed等はAttackSpeedMultiplier経由で後から調整可能)。
        gunslinger.baseLives = 3;
        gunslinger.baseMaxLives = 5;
        gunslinger.attackPower = 2;
        gunslinger.attackComboCount = 3;
        gunslinger.attackSpeedMultiplier = 1f;
        gunslinger.attackRangeMultiplier = 1f; // 弾自体の射程はbulletLifetime*bulletSpeedで決まる(Hitbox系のこの値はマズルフラッシュVFXにのみ使う)
        gunslinger.knockbackPowerMultiplier = 1f;
        gunslinger.jumpCount = 2;
        gunslinger.jumpForceMultiplier = 1f;
        gunslinger.groundMobilityMultiplier = 1f;
        gunslinger.airControlMultiplier = 1f;
        // 上/空中/下いずれも「専用の射撃」として持つ(通常の近接上/下攻撃
        // ではなくDoRangedUpShot/DoRangedDownShotへ分岐 - PlayerController.
        // ApplyCharacterBaseStats/FireJump/Move参照)。
        gunslinger.canUseUpAttack = true;
        gunslinger.canUseAirAttack = true;
        gunslinger.canUseDownAttack = true;

        gunslinger.isRanged = true;
        gunslinger.bulletSpritePath = "Assets/Art/GunslingerBullet.png";
        gunslinger.bulletSpeed = 15f;
        gunslinger.bulletLifetime = 1.6f; // 15*1.6=24u先まで届く(既存の剣士Hitboxより明確に長い射程)
        gunslinger.hoverDuration = 0.22f;
        gunslinger.hoverFallSpeed = 0.6f;

        // 見た目(2026-09-23、ChatGPT生成の本番素材に差し替え済み) - PPUは
        // 各フォルダの基準身長(そのStateで最初に見えるフレームの実測
        // コンテンツ高さ÷1.13、黒剣士PlayerRun_v1と同じ基準)から算出。
        // ChatGPT生成イラストはポーズごとに実測コンテンツ高さが大きく
        // 異なる(直立/疾走/ジャンプ等でbounding boxの縦横比が変わるため)
        // ので、双剣士/お嬢様騎士と同様フォルダごとに個別のPPUを設定して
        // いる - 実機で黒剣士と並べてサイズ差があれば要再調整(マスターへ
        // の開示事項)。
        // Run簡素化改修(2026-09-24) - 従来2コマ(idle/windup)のみで「ちゃん
        // と走っている」感が出ないとの指摘を受け、中間ストライド4コマを
        // 追加し計6コマへ拡張(run_00〜run_05、双剣士/お嬢様騎士と同規模)。
        // 新規4コマは生成時のキャンバス倍率が既存2コマと異なっていたため、
        // 実測コンテンツ高さを既存の範囲(579〜679px)に収まる630px基準へ
        // 個別にリサイズしてから設置した(フォルダ全体で単一のPPUを使う
        // ConfigureSpriteFolderImportWithSharedHeadPivotの性質上、フレーム
        // ごとの実寸が大きく異なるとキャラクターのサイズがコマごとに
        // ポップして見えるため)。fpsは双剣士(6コマ@7fps)に合わせ6→7fへ。
        gunslinger.runFramesDir = "Assets/Art/GunslingerRun_v1";
        gunslinger.runFramesPpu = 601f; // 679/1.13(既存run_00基準、新規4コマもこの基準へ合わせてリサイズ済み)
        gunslinger.runFpsOverride = 7f;
        gunslinger.jumpStartFramesDir = "Assets/Art/GunslingerJumpStart_v1"; // windup(579px)を流用(実際にはUpShotが優先表示され、まず表示されない安全策)
        gunslinger.jumpStartFramesPpu = 512f; // 579/1.13
        gunslinger.jumpFramesDir = "Assets/Art/GunslingerJumpAir_v1"; // windup(579px)を流用 - UpShotの表示が終わった後の素の滞空ポーズ
        gunslinger.jumpFramesPpu = 512f;
        gunslinger.doubleJumpFramesDir = "Assets/Art/GunslingerDoubleJump_v1"; // windup(579px)を流用(JumpStartと同じ理由で安全策)
        gunslinger.doubleJumpFramesPpu = 512f;
        gunslinger.landFramesDir = "Assets/Art/GunslingerLand_v1"; // 着地ポーズ(545px)
        gunslinger.landFramesPpu = 482f; // 545/1.13
        // Forward/Backward Shot共有(PlayerController.DoRangedForwardBackShot
        // がattackFrames/State.Attackをそのまま使う - Backwardはtransform
        // 反転で自動ミラー)。
        gunslinger.attackFramesDir = "Assets/Art/GunslingerAttack_v1"; // 正面二丁撃ちポーズ(819px)
        gunslinger.attackFramesPpu = 725f; // 819/1.13
        // Up Shot専用(新設のState.UpShot、PlayerController.IsRangedUpShooting
        // がtrueの間だけ表示)。
        gunslinger.upShotFramesDir = "Assets/Art/GunslingerUpShot_v1"; // ジャンプ+斜め上撃ちポーズ(766px)
        gunslinger.upShotFramesPpu = 678f; // 766/1.13
        // Down Shotは既存downAttackFramesフィールドを流用(isDiveAttacking
        // ではなくisHoverShooting中に表示、PlayerAnimator.Update参照)。
        gunslinger.downAttackFramesDir = "Assets/Art/GunslingerDownAttack_v1"; // 空中斜め下撃ちポーズ(656px)
        gunslinger.downAttackFramesPpu = 581f; // 656/1.13
        // RUN開始準備/正常終了演出 本番素材化(2026-09-24)
        gunslinger.startFramesDir = "Assets/Art/GunslingerStart_v1";
        gunslinger.startFramesPpu = 754.0f; // 852/1.13
        gunslinger.finishShortFramesDir = "Assets/Art/GunslingerFinishShort_v1";
        gunslinger.finishShortFramesPpu = 746.9f; // 844/1.13
        gunslinger.finishMediumFramesDir = "Assets/Art/GunslingerFinishMedium_v1";
        gunslinger.finishMediumFramesPpu = 906.2f; // 1024/1.13
        gunslinger.finishLongFramesDir = "Assets/Art/GunslingerFinishLong_v1";
        gunslinger.finishLongFramesPpu = 916.8f; // 1036/1.13
        gunslinger.finishExtremeFramesDir = "Assets/Art/GunslingerFinishExtreme_v1";
        gunslinger.finishExtremeFramesPpu = 721.2f; // 815/1.13

        return new[] { swordsman, dualBlade, nobleLady, gunslinger };
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
            if (existing != null)
            {
                // Home画面改善依頼③(2026-09-15) - 既存アセットの他フィールド
                // (星評価・アニメーション等の手動チューニング値)には一切
                // 触れず、今回新設したbelongingsフィールドだけを後方互換
                // バックフィルする(まだ一度もこのフィールドを持ったことが
                // ない既存アセットが対象、未設定=null/空配列の間だけ)。
                if ((existing.belongings == null || existing.belongings.Length == 0) && spec.belongings != null && spec.belongings.Length > 0)
                {
                    existing.belongings = spec.belongings;
                    EditorUtility.SetDirty(existing);
                }
                // Home画面改善依頼④(2026-09-15) - 前回(③)の時点ではicon
                // (実画像)がまだ用意できておらず、belongings配列自体は
                // 既にexistingへ入っている(=上のbelongings丸ごとバック
                // フィルは発火しない)。今回ChatGPTで実際にアイコン画像を
                // 生成できたので、「配列は既にあるがiconだけがnullのまま」
                // の項目にだけ画像を差し込む、より細かいバックフィルを
                // 追加した。ラベルやplaceholderColor等、既に手動調整された
                // かもしれない値には一切触れない。
                else if (existing.belongings != null && spec.belongings != null)
                {
                    bool changed = false;
                    for (int i = 0; i < existing.belongings.Length && i < spec.belongings.Length; i++)
                    {
                        if (existing.belongings[i].icon == null && spec.belongings[i].icon != null)
                        {
                            existing.belongings[i].icon = spec.belongings[i].icon;
                            changed = true;
                        }
                        // Home画面改善依頼⑦(2026-09-16) - kindは今回新設した
                        // フィールドのため、③④時点で既に生成済みの既存
                        // アセットはまだ持っておらず(deserialize時は既定値
                        // Small=0のまま)、この配列自体が空でないぶん上の
                        // 「配列丸ごとバックフィル」も発火しない。iconと違い
                        // 手動でInspector調整される類の値ではないため、常に
                        // 最新のSpec側の値へ同期する。
                        if (existing.belongings[i].kind != spec.belongings[i].kind)
                        {
                            existing.belongings[i].kind = spec.belongings[i].kind;
                            changed = true;
                        }
                    }
                    if (changed) EditorUtility.SetDirty(existing);
                }
                // 弾スプライト本番素材化(2026-09-23) - bulletSpriteは星評価等と
                // 違い手動でInspector調整される類の値ではない、純粋な素材参照
                // なので、既存アセットでも常に最新のPNG(Assets/Art/
                // GunslingerBullet.png)へ同期し直す(差し替え後にSceneBuilder.
                // Buildを再実行するだけで反映されるようにするため)。
                if (existing.isRanged && !string.IsNullOrEmpty(spec.bulletSpritePath))
                {
                    existing.bulletSprite = LoadBulletSprite(spec.bulletSpritePath);
                    EditorUtility.SetDirty(existing);
                }
                // RUN開始準備/正常終了演出(2026-09-23) - bulletSpriteと同じ
                // 理由(手動チューニング値ではない純粋な素材参照)で、既存
                // アセットでも常に最新のフォルダ内容へ同期し直す。今は全て
                // 空フォルダのため空配列のままだが、後でフォルダに絵を
                // 置いてSceneBuilder.Buildを再実行するだけで反映される。
                existing.startFrames = LoadAnimationFolder(spec.startFramesDir, spec.startFramesPpu);
                existing.finishShortFrames = LoadAnimationFolder(spec.finishShortFramesDir, spec.finishShortFramesPpu);
                existing.finishMediumFrames = LoadAnimationFolder(spec.finishMediumFramesDir, spec.finishMediumFramesPpu);
                existing.finishLongFrames = LoadAnimationFolder(spec.finishLongFramesDir, spec.finishLongFramesPpu);
                existing.finishExtremeFrames = LoadAnimationFolder(spec.finishExtremeFramesDir, spec.finishExtremeFramesPpu);
                EditorUtility.SetDirty(existing);
                continue;
            }

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
            def.belongings = spec.belongings;

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
            def.runFps = spec.runFpsOverride;

            // お嬢様騎士Run表示基準統一(2026-09-13) - Runだけ「フレームごと
            // に個別pivot」ではなく「全コマ共通の接地ライン」を使う専用の
            // 読み込み方式に変更(通常Run中にキャラ全体が上下へガクガク跳ね
            // る不具合の修正、SceneBuilder.ConfigureSpriteFolderImportWith
            // SharedGroundPivotのコメント参照)。Jump/Land/Attackは今回報告
            // が無いため、従来どおりLoadAnimationFolder(個別pivot)のまま。
            def.runFrames = LoadRunAnimationFolder(spec.runFramesDir, spec.runFramesPpu);
            def.jumpStartFrames = LoadAnimationFolder(spec.jumpStartFramesDir, spec.jumpStartFramesPpu);
            def.jumpFrames = LoadAnimationFolder(spec.jumpFramesDir, spec.jumpFramesPpu);
            def.doubleJumpFrames = LoadAnimationFolder(spec.doubleJumpFramesDir, spec.doubleJumpFramesPpu);
            def.landFrames = LoadAnimationFolder(spec.landFramesDir, spec.landFramesPpu);
            def.attackFrames = LoadAnimationFolder(spec.attackFramesDir, spec.attackFramesPpu);
            def.attackFramesSmall = LoadAnimationFolder(spec.attackFramesSmallDir, spec.attackFramesSmallPpu);
            def.attackFramesLarge = LoadAnimationFolder(spec.attackFramesLargeDir, spec.attackFramesLargePpu);
            def.downAttackFrames = LoadAnimationFolder(spec.downAttackFramesDir, spec.downAttackFramesPpu);
            def.downAttackLandFrames = LoadAnimationFolder(spec.downAttackLandFramesDir, spec.downAttackLandFramesPpu);

            def.startFrames = LoadAnimationFolder(spec.startFramesDir, spec.startFramesPpu);
            def.finishShortFrames = LoadAnimationFolder(spec.finishShortFramesDir, spec.finishShortFramesPpu);
            def.finishMediumFrames = LoadAnimationFolder(spec.finishMediumFramesDir, spec.finishMediumFramesPpu);
            def.finishLongFrames = LoadAnimationFolder(spec.finishLongFramesDir, spec.finishLongFramesPpu);
            def.finishExtremeFrames = LoadAnimationFolder(spec.finishExtremeFramesDir, spec.finishExtremeFramesPpu);

            def.isRanged = spec.isRanged;
            if (spec.isRanged && !string.IsNullOrEmpty(spec.bulletSpritePath))
            {
                def.bulletSprite = LoadBulletSprite(spec.bulletSpritePath);
                def.bulletSpeed = spec.bulletSpeed;
                def.bulletLifetime = spec.bulletLifetime;
                def.hoverDuration = spec.hoverDuration;
                def.hoverFallSpeed = spec.hoverFallSpeed;
            }
            def.upShotFrames = LoadAnimationFolder(spec.upShotFramesDir, spec.upShotFramesPpu);

            AssetDatabase.CreateAsset(def, assetPath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        CharacterDatabase.Reset();
    }

    // SceneBuilder.LoadIconTexture(private)と同じ設定 - CardDatabaseBuilder
    // 自身のLoadIconTextureと同じミラーパターン(Texture2Dとして読み込み、
    // 表示側でSprite.Createする方式に合わせる)。
    internal static Texture2D LoadIconTexture(string path)
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

    // キャラクター専用アニメーション差し替え(2026-09-13) - SceneBuilderの
    // PlayerXxx_v1フォルダ群と全く同じ「足元Pivot自動検出+フォルダ単位の
    // 実測PPU」インポート設定を再利用する(SceneBuilder.
    // ConfigureSpriteFolderImportWithFootPivotXY/LoadSpriteSequenceをinternal
    // 化して直接呼び出し、二重実装を避けた)。dirが空文字/未指定なら
    // そのStateは黒剣士のデフォルトアートのまま(空配列を返す -
    // PlayerAnimator.ApplyCharacterAnimationSetのフォールバック参照)。
    //
    // 不具合修正(2026-09-13、隣接コマ混入) - 当初はX=0.5固定のFootPivot
    // (ConfigureSpriteFolderImportWithFootPivot)を使っていたが、これは
    // 「各コマがキャンバス内で水平中央に配置されている」ことが前提。この
    // キャラの各コマは元シートの隣接ポーズが近接/接触していたため、隣接
    // コマへの混入を避けるコマごとに異なる幅でクロップし直した(=もう
    // キャンバス中央に一律配置されていない)結果、X=0.5固定だとコマ間で
    // 見た目の左右位置が微妙にズレる恐れがあった。X,Y両方を自動検出する
    // ConfigureSpriteFolderImportWithFootPivotXY(既存のPlayerUpAttackGround_v1
    // 等と同じ、剣の振り幅でコマごとに実効横幅が変わる素材向けの方式)へ
    // 変更し、見た目の位置ズレを防いでいる。
    static Sprite[] LoadAnimationFolder(string dir, float pixelsPerUnit)
    {
        if (string.IsNullOrEmpty(dir)) return new Sprite[0];
        SceneBuilder.ConfigureSpriteFolderImportWithFootPivotXY(dir, pixelsPerUnit);
        return SceneBuilder.LoadSpriteSequence(dir);
    }

    // 双剣士/お嬢様騎士Run頭基準ピボット化(2026-09-13深夜) - マスター
    // 指摘「頭を中心にアニメーションすることは可能か」に対応し、従来の
    // ConfigureSpriteFolderImportWithSharedGroundPivot(全コマ共通の接地
    // ライン、足元固定)からConfigureSpriteFolderImportWithSharedHeadPivot
    // (全コマ共通の頭頂ライン、頭部固定)へ切り替えた。視線は自然と頭・
    // 顔を追うため、コマ間のわずかな頭身バランスのブレは足元を固定する
    // よりも頭を固定した方が目立ちにくい、というマスターの見立てに対応。
    static Sprite[] LoadRunAnimationFolder(string dir, float pixelsPerUnit)
    {
        if (string.IsNullOrEmpty(dir)) return new Sprite[0];
        SceneBuilder.ConfigureSpriteFolderImportWithSharedHeadPivot(dir, pixelsPerUnit);
        return SceneBuilder.LoadSpriteSequence(dir);
    }

    // 二丁拳銃士追加(2026-09-23) - 弾丸は「回転して進行方向を向くProjectile」
    // のため、キャラクター本体のような足元Pivotではなく中央Pivotで読み込む
    // (PlayerBullet.Create側でtransform.rotationを直接設定するため、Pivot
    // が中心からズレると回転の軸もズレて見た目が破綻する)。
    // 弾スプライト本番素材化(2026-09-23) - 旧プレースホルダー(64x24px)は
    // PPU=96固定で世界サイズ0.667x0.25unitだった。実イラストは解像度が
    // 全く異なる(ChatGPT生成+chroma key抽出)ため、旧来と同じ見た目の
    // 弾サイズ(高さ0.25unit)になるよう、画像の実測高さから逆算する
    // (LoadWildSpriteと同じ考え方)。
    static Sprite LoadBulletSprite(string path)
    {
        const float targetWorldHeight = 0.25f;
        float ppu = 96f;
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null)
        {
            imp.GetSourceTextureWidthAndHeight(out int w, out int h);
            ppu = h / targetWorldHeight;
        }
        return SceneBuilder.ConfigureAndLoadSpriteWithCenterPivot(path, ppu);
    }
}
