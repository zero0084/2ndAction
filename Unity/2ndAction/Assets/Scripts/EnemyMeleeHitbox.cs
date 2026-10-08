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

    // 敵の攻撃が出た瞬間の音(共通)。大きい判定は大型敵の攻撃音。画面外の攻撃は鳴らさない。
    void OnEnable()
    {
        var am = AudioManager.Instance; var cam = Camera.main;
        if (am == null || cam == null) return;
        float half = GameView.HalfWidth(cam), x = transform.position.x, cx = GameView.CenterX(cam);
        if (x < cx - half - 1f || x > cx + half + 1f) return;
        var box = GetComponent<BoxCollider2D>();
        bool big = box != null && box.size.x * Mathf.Abs(transform.lossyScale.x) * box.size.y * Mathf.Abs(transform.lossyScale.y) > 3.5f;
        am.PlaySeAt(big ? SeId.BigEnemyAttack : SeId.EnemyAttack, transform.position); // 2026-10-06: 位置で左右に振る
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player") && PlayerController.Instance != null)
        {
            PlayerController.Instance.TakeDamage(source: "EnemyMelee:" + name);
        }
    }
}
