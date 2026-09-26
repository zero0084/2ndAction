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
        // 弾速の走行補正(2026-09-26) - PlayerController.RunFrameSpeed参照。撃った本人の走行速度を
        // 引き継ぐので、高速走行中でも自分の弾を追い越さない(後ろ撃ちも一定の速さで後方へ飛ぶ)。
        transform.position += (Vector3)((velocity + new Vector2(PlayerController.RunFrameSpeed, 0f)) * Time.deltaTime);
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

        // 弾道の視認性向上(2026-09-24、マスター指示「弾が見づらいので弾道が
        // わかるような線を」)でTrailRendererを追加するにあたり、見た目の
        // スプライトを子オブジェクト"Visual"へ分離した。TrailRendererは
        // 自分の付いたTransformの非一様スケール(下の0.55,0.16)をそのまま
        // メッシュへ適用してしまい、尾が扁平に潰れて見える不具合があった
        // ため、ルート自体は等倍スケールのまま保ち、ストレッチは子だけに
        // 適用する。BoxCollider2Dも(元はこのscaleを利用して"細い直線状の
        // 攻撃判定"を作っていたが)scaleに頼らずcol.sizeで直接同じ大きさを
        // 指定することで、見た目と当たり判定を分離しても効果に変化がない
        // ようにした。
        GameObject visual = new GameObject("Visual");
        visual.transform.SetParent(go.transform, false);
        visual.transform.localScale = new Vector3(0.55f, 0.16f, 1f);

        SpriteRenderer sr = visual.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = new Color(1f, 0.92f, 0.55f);
        sr.sortingOrder = RenderOrder.CombatFx;

        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(0.55f, 0.16f);

        // 弾道の視認性向上(2026-09-24) - 飛翔中ずっと後方へ短い光の尾を
        // 引くTrailRenderer。弾自体の色調(暖色)に合わせ、先端が明るく尾へ
        // 向けて透明化するグラデーションにした。CombatFxより1つ手前の
        // sortingOrderにして、常に弾本体のスプライトの後ろに描かれるように
        // する(先端が霞まないように)。
        TrailRenderer trail = go.AddComponent<TrailRenderer>();
        trail.time = 0.12f;
        trail.minVertexDistance = 0.03f;
        trail.startWidth = 0.14f;
        trail.endWidth = 0.01f;
        trail.material = new Material(Shader.Find("Sprites/Default"));
        trail.sortingLayerID = sr.sortingLayerID;
        trail.sortingOrder = RenderOrder.CombatFx - 1;
        trail.textureMode = LineTextureMode.Stretch;
        trail.numCapVertices = 4;
        Gradient trailGradient = new Gradient();
        trailGradient.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f), new GradientColorKey(new Color(1f, 0.75f, 0.3f), 1f) },
            new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = trailGradient;

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
