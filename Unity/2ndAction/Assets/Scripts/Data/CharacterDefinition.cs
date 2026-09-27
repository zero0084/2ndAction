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
    // お嬢様騎士 二段ジャンプ演出バグ修正(2026-09-13) - 従来ここに
    // doubleJumpFramesが無かったため、PlayerAnimator.doubleJumpFramesが
    // 一度も上書きされず、常に黒剣士の焼き込みアート(=黒剣士の空中上攻撃
    // の絵)のままだった。二段ジャンプした瞬間だけ見た目が黒剣士に変わって
    // 見える、というマスター報告のバグの原因。canUseAirAttack=falseの
    // キャラでは実際の空中攻撃判定は一切発火しない(あくまで見た目だけの
    // 差し替え)。
    public Sprite[] doubleJumpFrames;
    // お嬢様騎士Run読みやすさ改善(2026-09-13) - runFramesのコマ数がPlayer
    // Animator.runFps(黒剣士と共有の単一フィールド)の前提コマ数と異なる
    // 場合に、このキャラだけ再生速度を上書きする。0(未指定)ならPlayer
    // Animatorの素のrunFpsをそのまま使う - 黒剣士の表示には一切影響しない。
    public float runFps;

    // 3人目の主人公追加(2026-09-13、双剣士) - PlayerAnimator.attackFrames
    // Small/Large(黒剣士が元々SceneBuilderで焼き込んでいた1-2段目/3段目
    // 専用アート)と同じ意味の「攻撃コンボの段階別アート」を、他キャラでも
    // 持てるようにする追加フィールド。空(要素数0)のままなら、その段階は
    // 黒剣士のデフォルトアートへフォールバックしない(PlayerAnimator.
    // ApplyCharacterAnimationSet参照 - attackFramesを上書きするキャラは
    // Small/Largeも自分の値かnullかのどちらかになり、黒剣士のSmall/Large
    // が紛れ込むことはない)。
    public Sprite[] attackFramesSmall;
    public Sprite[] attackFramesLarge;

    // 双剣士専用アニメ追加(2026-09-13深夜) - マスター報告「双剣士の下攻撃
    // /下着地が黒剣士と同じになっている」に対応。PlayerAnimator.
    // downAttackFrames/downAttackLandFramesには元々per-character上書き
    // 経路が無く(黒剣士の焼き込みアートを直接参照するのみだった)、
    // canUseDownAttack=trueの他キャラを追加した際に見た目が黒剣士のまま
    // 残ってしまうバグの温床になっていた。doubleJumpFrames/attackFrames
    // Small/Largeと同じ「空なら黒剣士のデフォルトへフォールバック」方式。
    public Sprite[] downAttackFrames;
    public Sprite[] downAttackLandFrames;

    // ===== 被弾リアクション(2026-09-22) ===== //
    // 全キャラ共通の仕組み(PlayerController: Hurt/Recovery)の、キャラ別の上書き。0(未指定)ならPlayerControllerの
    // 既定値(Hurt 0.25秒/無敵0.7秒/Recovery 0.45秒/復帰後無敵1.0秒)を使う。専用のHurt/Recovery絵が用意できたら
    // hurtFrames/recoveryFramesへ設定するだけで差し替わる(空なら既存の絵を使った簡易の姿勢アニメーション)。
    [Header("Hit Reaction (0 = PlayerControllerの既定値)")]
    public float hurtDuration;
    public float hurtInvincibleDuration;
    public float recoveryDuration;
    public float recoveryInvincibleDuration;
    // Hurt中の後方ノックバック倍率(1=既定)。
    public float hurtKnockbackMultiplier = 1f;
    public Sprite[] hurtFrames;
    public Sprite[] recoveryFrames;
    // 専用絵が無い間の簡易アニメーション(既存の絵に姿勢変化を付ける)の見せ方。
    public float hurtLeanDegrees = 12f;      // のけぞり角度
    public float hurtStaggerDistance = 0.12f; // 後方へよろける距離(ワールド単位)
    public float recoveryCrouchDepth = 0.14f; // 復帰時に沈み込む割合

    // ===== RUN開始準備/正常終了演出(2026-09-23) ===== //
    // 開始カウントダウン中の準備ポーズ(StartFrames)と、正常終了時の距離
    // Tier別リアクション(FinishXxxFrames、0-999m/1000-9999m/10000-49999m/
    // 50000m+の4段階)。専用絵が無ければPlayerAnimatorが走りの先頭コマ+
    // 手続き的な姿勢変化(前傾/しゃがみ込み)へフォールバックする。
    [Header("Run Start/Finish (0 = PlayerAnimatorの既定値)")]
    public Sprite[] startFrames;
    public float startFps;
    public float startLeanDegrees = 8f;
    public Sprite[] finishShortFrames;   // Tier0: 0-999m
    public Sprite[] finishMediumFrames;  // Tier1: 1000-9999m
    public Sprite[] finishLongFrames;    // Tier2: 10000-49999m
    public Sprite[] finishExtremeFrames; // Tier3: 50000m+
    public float finishFps;
    public float[] finishTierCrouchDepth = new float[] { 0.03f, 0.08f, 0.16f, 0.30f };

    // ===== 二丁拳銃士(2026-09-23) ===== //
    // 4人目の主人公追加 - 「面で攻撃する剣士」に対して「点で攻撃する遠距離
    // キャラクター」という構造そのものが違う枠。PlayerController側で
    // isRanged==trueの間だけForward/Backward/Up/Downの4攻撃すべてが専用の
    // 弾丸ロジック(DoRangedForwardBackShot/DoRangedUpShot/DoRangedDownShot)
    // へ分岐する - 他3キャラのisRanged==falseの経路(既存のDoAttack/
    // DoUpAttack/DoDiveAttack)は一切変更していない。
    [Header("二丁拳銃士 (Ranged) - 2026-09-23")]
    public bool isRanged;
    // 弾の見た目(細い直線状、進行方向へ自動で回転)。
    public Sprite bulletSprite;
    public float bulletSpeed = 15f;
    public float bulletLifetime = 1.6f;
    // 下攻撃(空中で斜め下へ撃ちながら短時間だけ落下速度を弱める)の挙動。
    // 「Player共通のGravity値そのものは変更しない」よう、Move()側で
    // isHoverShooting中だけvelocityYをこの値へ直接上書きし、Hurt/Death/
    // Respawn/着地のいずれでも必ずEndDiveAttack()経由で解除される。
    public float hoverDuration = 0.22f;
    public float hoverFallSpeed = 0.6f;
    // 上攻撃(ジャンプ+斜め上射撃)専用ポーズ。空(未指定)なら通常のJump/
    // DoubleJump演出のまま(PlayerAnimator.State.UpShot参照) - 黒剣士の
    // ような「上攻撃を持つが専用Stateを持たないキャラ」には一切影響しない
    // 新設の専用Stateなので、既存3キャラのJumpStart/DoubleJumpの意味は
    // 変わらない。
    public Sprite[] upShotFrames;
    // 下攻撃(斜め下射撃)のポーズは、既存のdownAttackFrames(上のダイブ
    // 攻撃用フィールド)をそのまま流用する - このキャラはisDiveAttacking
    // (急降下)を一切使わない(isHoverShootingという完全に別のフラグ)ため、
    // フィールドを二重に持たせず流用で衝突を避ける(PlayerAnimator.Update
    // のdiveAttacking判定を参照)。

    // ===== 竜騎士(2026-09-26) ===== //
    // 5人目の主人公 - 巨大ランスによる前方突進・貫通・高威力。isLancer==true
    // の間だけ4方向の攻撃がPlayerController.Lancer.csの専用処理へ分岐する
    // (他4キャラはfalseのまま=既存のDoAttack/DoUpAttack/DoDiveAttack/射撃に
    // 一切触れない)。数値はInspectorで調整可能(Build Character Databaseは
    // 既存アセットのこれらの値を上書きしない)。
    [Header("竜騎士 (Lancer) - 2026-09-26")]
    public bool isLancer;
    // 前突き: 構え→突き(判定あり+短い突進)→硬直。判定は体の少し前から
    // 始まる細長い帯(懐=体に重なる距離は届かない)。
    public float lanceWindup = 0.12f;
    public float lanceThrustActive = 0.14f;
    public float lanceRecovery = 0.24f;
    public float lanceReach = 2.45f;          // 判定の長さ(world)
    public float lanceReachStart = 0.45f;     // 判定の開始位置(体の中心からの距離)
    public float lanceThickness = 0.42f;     // 判定の太さ
    public float lanceHeight = 0.68f;        // 判定の高さ(足元から)
    public float lanceDashDistance = 0.9f;   // 突き中の前方突進距離
    // 速度連動(将来拡張用の入口): 走行速度倍率が1を超えた分×この係数だけ
    // 突進距離/ノックバックを強める(上限lanceSpeedBonusMax)。0で固定性能。
    public float lanceSpeedBonusPerSpeed = 0.2f;
    public float lanceSpeedBonusMax = 0.6f;
    // 後攻撃(石突き): 前突きより短く・弱く・ノックバック弱め。
    public float lanceBackReach = 0.85f;
    public float lanceBackDamageScale = 0.5f;
    public float lanceBackKnockbackScale = 0.45f;
    // 上攻撃(斜め上への突き上げ、小ジャンプ付き・打ち上げなし)。
    public float lanceUpReach = 1.9f;
    public float lanceUpAngle = 50f;
    public float lanceUpActive = 0.2f;
    // 下攻撃(低い姿勢で前方下への突き)。
    public float lanceDownReach = 1.7f;
    public float lanceDownHeight = 0.22f;
    public float lanceDownActive = 0.16f;
    public Sprite[] lanceBackFrames;
    public Sprite[] lanceDownFrames;

    // 竜騎士 改修(2026-09-26 第2弾) - 前突きの3段コンボ(Quick Thrust → Step Thrust →
    // Dragon Pierce)。配列の[0]〜[2]が1〜3段目。リーチ/突進/威力/KBは上の基準値に掛ける倍率。
    // 3段とも「細長く非常に長い前方判定」のまま、段が進むほど長く・重く・隙が大きい。
    public float[] lanceComboWindup = { 0.07f, 0.1f, 0.16f };
    public float[] lanceComboActive = { 0.11f, 0.13f, 0.18f };
    public float[] lanceComboRecovery = { 0.15f, 0.2f, 0.46f };
    public float[] lanceComboReachScale = { 0.9f, 1.05f, 1.3f };
    public float[] lanceComboDashScale = { 0.6f, 1.1f, 1.8f };
    public float[] lanceComboDamageScale = { 1f, 1.25f, 1.7f };
    public float[] lanceComboKnockbackScale = { 1f, 1.3f, 1.8f };
    public float[] lanceComboHitStop = { 0f, 0.02f, 0.11f };   // 命中時のHitStop(0=敵側の既定値)
    public float[] lanceComboFxThickness = { 0.8f, 1f, 1.6f };  // 槍先の衝撃波の太さ
    public float lanceComboGrace = 0.45f; // 突き終わってからこの秒数以内の入力は次の段へ(過ぎると1段目に戻る)

    // 空中の下攻撃 = 急降下突き(構え→真下へ高速落下→地面へ突き刺して着地→衝撃波)。
    // 地上の下攻撃は従来どおり前方下への突き。
    public float lanceDiveWindup = 0.1f;
    public float lanceDiveSpeed = 22f;
    public float lanceDiveHorizontalScale = 0.1f;  // 落下中の前進速度の倍率(ほぼ真下へ落ちる)
    public float lanceDiveMaxTime = 2.5f;          // 念のための上限(着地しないまま続かない)
    public float lanceDiveImpactRadius = 1.8f;     // 着地の衝撃の横半径
    public float lanceDiveImpactHeight = 1.2f;
    public float lanceDiveImpactDamageScale = 1.5f;
    public float lanceDiveImpactKnockbackScale = 1.6f;
    public float lanceDiveImpactHitStop = 0.1f;
    public float lanceDiveImpactActive = 0.14f;
    public float lanceDiveRecovery = 0.3f;         // 突き刺した後の硬直
    public Sprite[] lanceDiveFrames;               // [0]=構え(穂先を真下へ) [1]=落下 [2]=突き刺して着地
    // 急降下の絵は穂先が足より下にある(下端中央ピボット=穂先の先)。足元をプレイヤー位置に
    // 合わせるため、コマごとに絵を下へずらす量(world)。着地コマは足元が下端なので0。
    public float[] lanceDiveFrameOffsetY = { -0.48f, -0.3f, 0f };

    // 死亡時の専用ポーズ(全キャラ共通の仕組み。空なら従来どおり消えて爆散)。
    public Sprite[] deathFrames;

    // ===== 新4人(2026-09-27): 弓使い/魔法使い/格闘家/忍者 =====
    // kit=Standard(既定)の既存5人は、以下を一切参照しない。
    [Header("新4人 専用キット (2026-09-27)")]
    public CharacterKit kit = CharacterKit.Standard;
    public KitPose[] kitPoses;
    public ArcherKitParams archer = new ArcherKitParams();
    public MageKitParams mage = new MageKitParams();
    public FighterKitParams fighter = new FighterKitParams();
    public NinjaKitParams ninja = new NinjaKitParams();

    public Sprite[] FindKitPose(string poseName)
    {
        if (kitPoses == null) return null;
        foreach (var p in kitPoses)
            if (p != null && p.name == poseName && p.frames != null && p.frames.Length > 0) return p.frames;
        return null;
    }

    // Home画面改善依頼③(2026-09-15) - 「選択中キャラクターの持ち物・装備の
    // 視覚表示」。新しい装備システムではなく、あくまでHome画面で「このキャ
    // ラクターらしさ」を見せるための表示専用データ。武器/防具/象徴的な
    // 小物などを2〜4個想定。iconが未設定の間はGameManager側がlabelの頭文字
    // +placeholderColorの簡易表示へフォールバックする(マスター指示「最終
    // 的な画像素材は後から差し替えられる構造に」に対応 - iconを後から
    // Inspectorで設定するだけで自動的に実画像表示へ切り替わる)。
    // Home画面改善依頼⑦(2026-09-16) - 「3個の装備アイコンを同じ置き方で
    // 並べない」ため、アイテムごとに置き方(GameManager.DrawBelongingsSet
    // 参照)を変えるための分類。表示専用の追加情報であり、既存のicon/label
    // /placeholderColorには一切手を入れていない。
    public enum BelongingKind
    {
        Small,   // 棚の上に置く小物(現状維持に近い置き方)
        Weapon,  // 棚/壁に立てかける、または壁のフックに掛ける
        Shield,  // 壁に掛ける(盾・紋章など)
        Cloth,   // 棚の端やフックから垂らす(マント・羽根・布飾りなど)
    }

    [System.Serializable]
    public struct BelongingItem
    {
        public string label;
        public Texture2D icon;
        public Color placeholderColor;
        public BelongingKind kind;
    }
    [Header("Home画面 持ち物表示 (2026-09-15, 表示専用・装備システムではない)")]
    public BelongingItem[] belongings;
}
