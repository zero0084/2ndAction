using UnityEngine;

// エリアルコンボ改修(2026-09-11) - マスターの「攻撃 → 敵がリアクション →
// 浮かせる → 空中追撃 → 下攻撃で叩き落とす」という一連のコンボ対応。
// 全ての攻撃Hitboxは共通のタグ"PlayerAttack"を共有している(FireballController
// の反射判定など、タグだけで十分な既存処理を壊さないため)が、
// EnemyController側で「どの攻撃に当たったか」に応じて反応(小さなノック
// バック/打ち上げ/空中追撃時の滞空補助/叩き落とし)を変えるには、タグだけ
// では情報が足りない。この小さなコンポーネントを各Hitbox GameObjectへ
// 追加し(SceneBuilder.CreatePlayer参照)、EnemyController.OnTriggerEnter2D
// が`other.GetComponent<PlayerAttackInfo>()`で読み取る - 見つからない場合
// はNormal扱い(将来誰かが新しいPlayerAttackタグの何かを追加しても、既存の
// 「小さなノックバック」挙動にフォールバックするだけで安全)。
public enum PlayerAttackKind
{
    Normal,      // 通常攻撃(Forward/Backwardの3段コンボ、地上・空中どちらでも使用可)
    Up,          // 上方向攻撃(ジャンプ/二段ジャンプに連動) - 敵を打ち上げる
    Down,        // 下方向攻撃(空中ダイブ本体) - 浮いている敵を叩き落とす
    DownImpact   // 下方向攻撃の着地衝撃(範囲判定) - 周囲へのおまけヒット
}

public class PlayerAttackInfo : MonoBehaviour
{
    public PlayerAttackKind kind;
}
