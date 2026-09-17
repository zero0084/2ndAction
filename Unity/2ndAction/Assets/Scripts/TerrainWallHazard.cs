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
// IsGroundedがtrueの間(歩いて横から突っ切った場合)に加え、Stage01地形
// 挙動修整(2026-09-17), item1 - マスター指摘「穴へ落下時、側壁へ衝突して
// もダメージが発生しない場合がある」への対応で、落下中(VerticalVelocity
// が負)の接触も反応対象に加えた。上ルート/分岐路へジャンプで到達する際は
// 上昇中(VerticalVelocity>=0)なので引き続き誤反応しない - 「合流地点の
// 坂の下を通ってから登る」動きは速度が正のままこの当たり判定を通過する。
// 反応した瞬間にTakeDamage内部のRespawnAtCurrentPositionがプレイヤーを
// 即座に安全地点へスナップし直すため、同じ落下でさらに沈んでfailYの
// 通常落下ダメージ(PlayerController.Move()参照)が追加で発生することは
// ない(スナップ後はもう穴の中にいない)。
public class TerrainWallHazard : MonoBehaviour
{
    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        if (PlayerController.Instance == null) return;
        bool walkedIntoWall = PlayerController.Instance.IsGrounded;
        bool fallingIntoWall = !PlayerController.Instance.IsGrounded && PlayerController.Instance.VerticalVelocity < 0f;
        if (!walkedIntoWall && !fallingIntoWall) return;
        PlayerController.Instance.TakeDamage();
    }
}
