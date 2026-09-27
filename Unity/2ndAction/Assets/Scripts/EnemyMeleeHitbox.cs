using UnityEngine;

// 敵AI行動Tier試験実装(2026-09-16) - T1/T2の近接攻撃「Attack」フェーズ中
// だけ有効になる、独立した子GameObjectの当たり判定。EnemyController本体の
// BoxCollider2D(常時有効、Player接触ダメージ用)とは別物 - こちらは
// EnemySpecialBehaviorが状態遷移に合わせてSetActiveをON/OFFするだけの
// 単純な「一定時間だけ有効なHitbox」。
//
// 既存のFireballController/EnemyController/DragonControllerが揃って使って
// いる「other.CompareTag("Player")ならPlayerController.Instance.
// TakeDamage()を呼ぶ」という同じ1行のパターンをそのまま踏襲している -
// Player側に新しい受け口は一切必要ない。
public class EnemyMeleeHitbox : MonoBehaviour
{
    // マルチプレイPhase 2.5: 敵の近接判定(持ち主の敵からの相対位置、有効/無効)をJOINにも出す。
    void Awake() { NetAttackSync.Register(gameObject, NetAttackSync.AType.EnemyMelee); }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player") && PlayerController.Instance != null)
        {
            PlayerController.Instance.TakeDamage(source: "EnemyMelee:" + name);
        }
    }
}
