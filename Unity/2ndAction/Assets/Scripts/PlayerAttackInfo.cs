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

    // 新4人(2026-09-27) - 攻撃(判定/弾)ごとの倍率。弓のチャージ矢や格闘家の4段目のように
    // 「その判定だけ強い」攻撃のため、命中した瞬間のプレイヤーの状態ではなく判定自身に持たせる
    // (飛んでいる矢は、撃った後にプレイヤーが別の技を出しても撃った時の威力のまま)。
    // 既定値(1/1/0)のままなら従来と完全に同じ(既存5人の判定はすべて既定値)。
    public float damageScale = 1f;
    public float knockbackScale = 1f;
    public float hitStop;

    // 敵/ボス/障害物がダメージを読む箇所から呼ぶ。倍率1なら値をそのまま返す。
    public static int ScaleDamage(Collider2D attack, int damage)
    {
        if (attack == null) return damage;
        var info = attack.GetComponent<PlayerAttackInfo>();
        if (info == null || Mathf.Approximately(info.damageScale, 1f)) return damage;
        return Mathf.Max(1, Mathf.RoundToInt(damage * info.damageScale));
    }
}
