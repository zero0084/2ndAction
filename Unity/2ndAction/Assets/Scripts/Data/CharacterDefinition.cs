using UnityEngine;

// キャラクター選択画面(2026-09-12) - CardDefinition/EnemyDefinitionと同じ
// 「データはScriptableObject、UIコードは共通」という設計を踏襲した、1人の
// 主人公候補ぶんのデータ。今回は表示専用(LIFE/POWER/SPEED/COMBOの星評価も
// 内部戦闘値そのものではなく「このキャラがどういうタイプか」を伝える表示
// 用データ) - 戦闘性能への反映はまだ行わない。4人目・5人目を追加する際は
// このアセットを1つ増やしてCharacterDatabaseBuilderへSpecを足すだけで済み、
// CharacterSelectUI/SceneBuilder側のコードは変更不要(SceneBuilderが
// CharacterDatabase.AllCharactersの件数ぶんだけ動的にカードスロットを
// 生成するため)。
[CreateAssetMenu(fileName = "CharacterDefinition", menuName = "OneMoreMile/Character Definition")]
public class CharacterDefinition : ScriptableObject
{
    public string characterId;
    public string displayName;
    // 参考画像の"The One Who Keeps Moving"のような短い二つ名(任意、空文字
    // なら非表示)。
    public string subtitle;
    // Role Badge文言("BALANCED"/"GROUND COMBO"/"CHALLENGE"等)。
    public string role;
    [TextArea(2, 5)]
    public string flavorText;

    // カード一覧・Character Select中央表示のどちらにも使う。RewardCardData.
    // Icon/CardDefinition.iconと同じ「Texture2Dで持ち、表示側で必要な
    // タイミングにSprite.Createする」方式(RewardCardUI.SetContent参照)。
    // 今回はportrait/mainVisualへ同じ画像を割り当てている(専用の大判
    // ビジュアルはまだ無い) - 将来的に別カットを用意すれば差し替えるだけ。
    public Texture2D portrait;
    public Texture2D mainVisual;

    // 星評価(1-5) - Unity内部の実際の戦闘パラメータではなく、プレイヤーに
    // 「このキャラがどういうタイプなのか」を直感的に伝えるための表示専用
    // データ(マスター指示どおり)。今回はまだ戦闘性能そのものには反映しない。
    [Range(1, 5)] public int lifeRating = 3;
    [Range(1, 5)] public int powerRating = 3;
    [Range(1, 5)] public int speedRating = 3;
    [Range(1, 5)] public int comboRating = 3;

    // 「見た目は強そうだが実は最弱」のお嬢様騎士のような特殊枠に立てる。
    public bool challengeFlag;

    // Resources.LoadAllの読み込み順はファイルシステム依存で不定なため、
    // 表示順を安定させるための明示的なソートキー(CharacterDatabase.Load
    // 参照)。
    public int sortOrder;

    // ===== プレイアブル主人公追加(2026-09-12、お嬢様騎士) ===== //
    // ここから下は表示専用ではなく、実際のゲームプレイに反映される値
    // (GameManager.ApplyCharacterBaseStats/PlayerController.
    // ApplyCharacterBaseStats参照)。マスター指示「黒剣士の値をベタ書き
    // しているだけの構造にはしないでください」に対応 - 黒剣士自身は
    // 全倍率=1.0かつ既存の固定値そのままのSpecを持つことで、この仕組み
    // を通しても性能が一切変化しないことを保証している(既存主人公の
    // 性能保護)。今後4人目・5人目を追加する際もこのアセットへSpecを
    // 1件足すだけで済み、PlayerController/GameManager側のコード変更は
    // 不要。

    [Header("Base Stats (Gameplay - not display-only)")]
    // GameManager.startingLives/maxLivesの代わりにRun開始時に適用される、
    // このキャラクターの初期HP/最大HP。
    public int baseLives = 3;
    public int baseMaxLives = 5;

    // PlayerController.AttackPower(通常は0からAddAttackPowerで積み上げる
    // カードのベース値そのもの)の初期値。
    public int attackPower = 2;
    // PlayerController.maxComboChain - 1にすると「Attack2/Attack3へ一切
    // 接続しない、常に1段止まりの単発攻撃」になる(お嬢様騎士の要件)。
    public int attackComboCount = 3;
    // PlayerController.AttackSpeedMultiplierの初期値(1=通常、大きいほど
    // 遅い - AddAttackSpeedBonusの乗算方向と同じ)。
    public float attackSpeedMultiplier = 1f;
    // PlayerController.AttackRangeMultiplierの初期値(1=通常、小さいほど
    // 攻撃リーチが短い)。
    public float attackRangeMultiplier = 1f;
    // PlayerController.KnockbackPowerMultiplierの初期値(1=通常、小さいほど
    // Enemyへ与えるノックバックが弱い - EnemyController.ApplyGroundKnockback
    // 参照)。
    public float knockbackPowerMultiplier = 1f;

    // PlayerController.maxJumpsの初期値(1にすると二段ジャンプなしの
    // 「1回ジャンプのみ」になる)。
    public int jumpCount = 2;
    // PlayerController.jumpForceの「素の値(baseJumpForce)」への乗算倍率
    // (1=通常)。1未満にする場合、既存ステージの隙間が「完全に詰む高さ」
    // にならないことを実機で必ず確認すること(お嬢様騎士の要件、マスター
    // への開示事項)。
    public float jumpForceMultiplier = 1f;
    // 「地上の機動力」= PlayerController.runSpeed(=このオートランナーの
    // 自動前進速度そのもの)への乗算倍率(1=通常)。ノックバック等は全て
    // PlayerForwardSpeed()を毎フレーム参照する速度追従方式(2026-09-12の
    // 一連の調整)のため、この値を変えても「敵に追いつかれる/引き離され
    // すぎる」といった副作用は起きない設計。
    public float groundMobilityMultiplier = 1f;
    // 「空中制御」= このゲームには左右の空中操作が存在しないため、代替
    // としてPlayerController.gravityへの乗算倍率を採用(1=通常、大きい
    // ほど落下が速く「空中での猶予」が短い) - 実際の設計判断であり確実な
    // 1:1対応ではない点をマスターへ開示。
    public float airControlMultiplier = 1f;

    // 「まずは地上1段攻撃+1回ジャンプのシンプルな弱キャラとして成立させ
    // たい」- falseにすると該当の攻撃トリガー自体が発火しなくなる(数値
    // を弱めるだけでなく技を持たせない、というマスターの理想形どおり)。
    // 通常ジャンプの物理(ジャンプ自体・二段ジャンプの高さ)には一切影響
    // しない。
    public bool canUseUpAttack = true;
    public bool canUseAirAttack = true;
    public bool canUseDownAttack = true;

    // ===== キャラクター専用アニメーション差し替え(2026-09-13) ===== //
    // PlayerAnimator.ApplyCharacterAnimationSetが読む。空(要素数0/null)の
    // ままなら「専用アートがまだ無い」を意味し、黒剣士のSceneBuilder焼き
    // 込みデフォルトのまま変更されない(マスター許可の「一時的に既存
    // アニメーション流用でも構わない」に対応 - 黒剣士・双剣士は現時点で
    // 全て空のままにしてある)。attackFrames以外の上/空中/下攻撃用State
    // (jumpStartFrames/doubleJumpFrames等)は黒剣士では上攻撃の絵を兼ねて
    // いるが、上攻撃を持たないキャラではPlayerAnimator側が「素のジャンプ
    // 演出」として再定義して使う - 詳細はPlayerAnimatorのコメント参照。
    [Header("Dedicated Animation (optional - empty falls back to swordsman art)")]
    public Sprite[] runFrames;
    public Sprite[] jumpStartFrames;
    public Sprite[] jumpFrames;
    public Sprite[] landFrames;
    public Sprite[] attackFrames;
    // お嬢様騎士Run読みやすさ改善(2026-09-13) - runFramesのコマ数がPlayer
    // Animator.runFps(黒剣士と共有の単一フィールド)の前提コマ数と異なる
    // 場合に、このキャラだけ再生速度を上書きする。0(未指定)ならPlayer
    // Animatorの素のrunFpsをそのまま使う - 黒剣士の表示には一切影響しない。
    public float runFps;
}
