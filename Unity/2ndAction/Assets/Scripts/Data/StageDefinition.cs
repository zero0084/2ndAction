using UnityEngine;

// ステージ選択導線追加(2026-09-12) - CharacterDefinitionと同じ「データは
// ScriptableObject、UIコードは共通」という設計を踏襲した、1ステージぶんの
// データ。マスター指示どおり「ヴァンサバ系のように、サムネ・名前・特徴
// だけの簡易表示」を狙った、あえて最小限の項目構成 - 長文の説明や複雑な
// メタデータは持たせない。
//
// 今回はナビゲーション(選ぶ→保存→Homeへ戻る→出発)の実装が主目的で、
// 実際のTerrain/Enemy生成を複数ステージ分岐させる仕組みはまだ無い
// (TerrainManagerは単一の連続手続き生成トラックのまま) - unlockedな
// ステージは当面「荒野街道」1つだけなので、選択してもゲームプレイ自体は
// 現状の1本道のまま(=荒野街道の中身そのもの)。4人目・5人目ならぬ2つ目・
// 3つ目のステージを実際に遊べるようにする際は、このアセットを1つ増やし
// つつTerrainManager側に「ステージごとの生成データ切り替え」を別途実装
// する必要がある(今回のスコープ外、マスターの明示的な「今回は導線追加に
// 留める」指示どおり)。
[CreateAssetMenu(fileName = "StageDefinition", menuName = "OneMoreMile/Stage Definition")]
public class StageDefinition : ScriptableObject
{
    public string stageId;
    public string displayName;

    // サムネイル画像(任意) - 用意できない間はnullのままでよく、
    // StageSelectUI/GameManager.DrawStageHotspotはどちらもnullを許容し
    // 単色パネル+テキストのみの表示にフォールバックする(マスター指示
    // 「難しければステージ名のみでも可」に対応)。
    public Texture2D thumbnail;

    // カード上に並べる短い特徴テキスト、各1〜2行程度を想定(長文説明は
    // 意図的に持たせない)。
    [TextArea(1, 2)] public string enemyText;
    [TextArea(1, 2)] public string featureText;
    [TextArea(1, 2)] public string routeText;

    // 地下遺跡/天空回廊のような未開放ステージ - trueでも今回は選択・出発
    // 不可(StageSelectUI側でグレーアウト+Lock表示、タップしても選択され
    // ない)。将来、実際の解禁条件(距離到達等)を追加する余地として
    // boolのまま残してある。
    public bool unlocked = true;

    // Resources.LoadAllの読み込み順はファイルシステム依存で不定なため、
    // 表示順を安定させるための明示的なソートキー(CharacterDatabase.Load
    // と同じ理由)。
    public int sortOrder;
}
