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
}
