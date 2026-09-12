using UnityEngine;

// Stage01 荒野街道 最小実装(2026-09-13) - 石/小木/壁/壊せる木/巨大石に
// 共通のランタイム挙動。EnemyControllerの「PlayerAttackタグ→ダメージ/
// Playerタグ→接触ダメージ」という判定パターンをそのまま踏襲しつつ、
// Enemyが持つ吹き飛ばし/空中コンボ/自動移動などは一切持たない、静止した
// 地形障害物としての最小構成。
//
// 設計判断(マスターへの開示事項) - 「壁/巨大石は無理に突破できない」を
// 物理的に前進を停止させる形では実装していない(このゲームはオート
// ランナーで、プレイヤー自身の水平移動入力が存在しないため、物理的に
// 「立ち止まらせる」新しい移動制御を丸ごと追加する必要がありリスクが
// 大きい)。代わりに、ジャンプで避けなければ確実に被弾する当たり判定の
// 大きさで「無理に通る」感覚を表現し、被弾した瞬間にその個体は退場する
// (同じ相手に連続で削られ続けたり、被弾後もその場に視覚的に残り続けて
// プレイヤーの見た目に重なり続けたりしないようにするため)。
public class ObstacleController : MonoBehaviour
{
    // 壊せる木のみtrue - PlayerAttackとの接触でhpを削り、0以下で破壊される。
    // false(石/小木/壁/巨大石)はPlayerAttackを一切参照せず、Player本体との
    // 接触でのみ反応する(=攻撃では壊せない、避けるかぶつかるかの二択)。
    public bool breakable;
    public int hp = 1;

    bool dying;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (dying) return;

        if (breakable && other.CompareTag("PlayerAttack"))
        {
            int damage = PlayerController.Instance != null ? PlayerController.Instance.EffectiveAttackPower : 1;
            hp -= Mathf.Max(1, damage);
            if (hp <= 0)
            {
                dying = true;
                // Enemyのkill報酬(mileReward/EnemyKillCount)は一切対象外 -
                // これは戦闘対象ではなく地形の一部という扱い(マスター指示
                // 「敵ではなく障害物」の区別どおり)。
                gameObject.SetActive(false);
            }
            return;
        }

        if (other.CompareTag("Player"))
        {
            if (PlayerController.Instance != null) PlayerController.Instance.TakeDamage();
            // 石/小木/壁/巨大石(breakable=false)はここで即座に退場させる -
            // 被弾後もその場に視覚的に残り続けてプレイヤーの見た目に重なり
            // 続けることを避けるため(ObstacleControllerクラスコメント
            // 参照)。壊せる木は上のPlayerAttack分岐でのみ破壊される -
            // Player本体との接触では退場させない(攻撃で破壊する、という
            // マスター指示の役割分担を保つため)。
            if (!breakable)
            {
                dying = true;
                gameObject.SetActive(false);
            }
        }
    }
}
