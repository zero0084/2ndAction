using UnityEngine;

// 二丁拳銃士(2026-09-23) - Player自身が放つ弾丸。FireballController(敵Owned、
// Playerへダメージを与える側)と同じ「直線移動+Trigger Collider+一定時間で
// 自動消滅」の構成を、逆向き(Enemy/Boss/Dragon/Majinへダメージを与える側)
// に使う。ダメージ計算自体はこのスクリプトの外(EnemyController/WildBossBase
// /DragonController/MajinControllerの各既存OnTriggerEnter2D、タグ
// "PlayerAttack"+PlayerAttackInfoを読む既存の仕組み)がそのまま処理するため、
// ここでは持たない - 敵側のHit Stop/Hit VFX/コンボカウンター/被ダメージ
// リアクションが変更なしでそのまま動く。
[RequireComponent(typeof(BoxCollider2D))]
public class PlayerBullet : MonoBehaviour
{
    public Vector2 velocity;
    public float lifetime = 1.6f;

    float age;
    bool hasHit;

    void Update()
    {
        transform.position += (Vector3)(velocity * Time.deltaTime);
        age += Time.deltaTime;
        if (age > lifetime) Destroy(gameObject);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (hasHit) return;
        // ダメージ自体は相手側の既存OnTriggerEnter2D(タグ"PlayerAttack"を
        // 読む)へ任せ、弾はここで消える(貫通させない - "細い射線=点で攻撃
        // する"という武器特性どおり、1発で複数の敵を巻き込まない)。
        if (other.GetComponent<EnemyController>() != null ||
            other.GetComponent<WildBossBase>() != null ||
            other.GetComponent<DragonController>() != null ||
            other.GetComponent<MajinController>() != null)
        {
            hasHit = true;
            Destroy(gameObject);
        }
    }

    public static GameObject Create(Sprite sprite, Vector3 position, Vector2 velocity, float lifetime)
    {
        GameObject go = new GameObject("PlayerBullet");
        go.tag = "PlayerAttack";
        go.transform.position = position;
        float angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
        go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        // 「細い直線状の攻撃判定」- 巨大なProjectile Colliderにはしない。
        go.transform.localScale = new Vector3(0.55f, 0.16f, 1f);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = new Color(1f, 0.92f, 0.55f);
        sr.sortingOrder = RenderOrder.CombatFx;

        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;

        // 不具合修正(2026-09-23) - EnemyControllerにはRigidbody2Dが無い
        // (地面判定/移動はTransform直操作)。既存の剣士Hitboxは常にPlayer
        // (Rigidbody2D持ち)の子として動くため気付かれなかったが、この弾は
        // Player本体から独立したルートGameObjectのため、どちらの側にも
        // Rigidbody2Dが無いとUnity 2D物理がOnTriggerEnter2Dそのものを発火
        // しない(自動テストで実際に命中しないことを確認して発覚)。
        // Kinematicにして重力/押し出しの影響は受けない。
        Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        go.AddComponent<PlayerAttackInfo>().kind = PlayerAttackKind.Normal;

        PlayerBullet b = go.AddComponent<PlayerBullet>();
        b.velocity = velocity;
        b.lifetime = lifetime;
        return go;
    }
}
