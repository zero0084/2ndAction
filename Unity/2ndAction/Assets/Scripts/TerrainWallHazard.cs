using UnityEngine;

// 崖面Collider追加(2026-09-15) - マスター報告「見た目上は壁なのに物理的には
// 通過できる箇所がある」への対応。GroundFactory.CreateWallHazardが穴の縁/
// 上下ルート分岐の坂の起点・合流点にだけ付ける、歩行面ラインより下の
// 「崖の側面」用トリガー。
//
// このゲームはオートランナーで水平移動入力が存在しないため、物理的に前進を
// 止める壁は実装しない(ObstacleController参照、同じ設計判断)。代わりに
// 既存のPlayerController.TakeDamage()をそのまま呼ぶだけ - ダメージ・
// ノックバック・無敵時間に加え、TakeDamage内部のRespawnAtCurrentPosition
// が呼び出し時点のX位置における実際の地面の高さへ即座にスナップし直す
// ため、「壁にめり込んだ状態」が自動的に解消される(=マスター指示の
// 「壁に当たった際の既存ダメージ処理、少し手前への復帰...など既存ロジック
// と整合を取ってください」にそのまま合致する)。
//
// IsGroundedがtrueの間だけ反応させているのが唯一の判断ポイント: 上ルート/
// 分岐路へジャンプで到達する際、上昇中のプレイヤーは必ずこの当たり判定の
// 縦方向の範囲を通過する(合流地点の坂の下を通ってから登る、等)が、その間
// は常にisGrounded=falseなので絶対に誤反応しない。「壁として見える場所を
// 歩いて(=接地した状態で)横から突っ切ろうとした」場合にのみ反応する。
public class TerrainWallHazard : MonoBehaviour
{
    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        if (PlayerController.Instance == null || !PlayerController.Instance.IsGrounded) return;
        PlayerController.Instance.TakeDamage();
    }
}
