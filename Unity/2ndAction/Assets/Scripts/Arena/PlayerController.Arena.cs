using UnityEngine;

// 開発用の闘技場(2026-10-04): 自動前進の速さの指定と、試験の始めの置き直し
public partial class PlayerController
{
    // 闘技場の「敵を全削除」: 被弾ののけぞり/硬直/被弾後の無敵を戻す(残った攻撃の続きで動けない状態を残さない)
    public void ArenaClearHurtState()
    {
        hitInvincibleTimer = 0f;
        reactionTimer = 0f; Reaction = ReactionKind.None;
        var sr2 = GetComponentInChildren<SpriteRenderer>(); if (sr2 != null && !hasDied) sr2.enabled = true;
    }

    // 闘技場の自動前進の速さ(m/s)。闘技場でなければ null(従来の計算)
    //  0km/h … 0(自動前進だけを止める。時間/攻撃/ジャンプ/重力/ノックバック/敵の行動は通常どおり)
    //  基準速度 … 指定の速さ × (このキャラ×カードの走る速さ / 共通の基本速度)。自然加速と自然上限は使わない
    //  実効速度固定 … 指定の速さそのもの(キャラ/カードの補正は移動に入れない)
    float? ArenaAutoSpeed()
    {
        if (TutorialMode.Active) return TutorialMode.SpeedMps; // 操作の練習(2026-10-07): ゆっくり一定
        if (!ArenaMode.Active) return null;
        var c = ArenaMode.Config;
        if (c.speedMode == 0 || c.kmh <= 0.01f) return 0f;
        float mps = c.kmh / GameManager.KmhPerMps;
        return c.speedMode == 2 ? mps : mps * ArenaSpeedFactor;
    }
    // キャラ×カードの速さの補正(共通の基本速度に対する倍率)
    public float ArenaSpeedFactor => runSpeed / CommonBaseRunSpeed;

    // 新しい平地の上へ置く(試験の始め)
    public void ArenaPlaceAt(float x)
    {
        FreezeDiagnostics.NoteIntendedMove("ARENA place");
        transform.position = new Vector3(x, transform.position.y, 0f);
        velocityY = 0f; knockbackTimer = 0f; lungeVelocityX = 0f;
        PlaceOnGroundForResume();
    }
}
