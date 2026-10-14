using UnityEngine;

// CPU サポート(2026-10-14): 仲間の攻撃判定(タグ "AllyAttack")に付ける。敵/ボス/障害物は "PlayerAttack" しか見ないので、
// ここで当たりを見つけて「他の参加者の攻撃」の入口(NetApplyRemoteHit 等。マルチで JOIN の攻撃を HOST が処理する所)へ流す。
//  ・ダメージは持ち主(仲間)の攻撃力 × その判定の倍率(PlayerAttackInfo.damageScale)。プレイヤーのカード/連撃/属性の効果は乗らない
//  ・同じ振り(SwingId)で同じ相手へは1回だけ(プレイヤーと同じ仕組み)
//  ・撃破の報酬は今までどおりランに1回(GameManager.RegisterEnemyKill / RegisterBossDefeat。仲間ごとに増えない)
//  ・画面全体を止める演出(HitStop)は出さない(PlayerController.AllyContext の間は HitStop が何もしない)
public class AllyAttackRouter : MonoBehaviour
{
    public PlayerController owner;
    Collider2D col;
    PlayerAttackInfo info;
    readonly System.Collections.Generic.Dictionary<Component, float> recent = new System.Collections.Generic.Dictionary<Component, float>();
    public static int Hits, Kills; // 確認用

    public static void Attach(GameObject go, PlayerController owner)
    {
        var r = go.GetComponent<AllyAttackRouter>();
        if (r == null) r = go.AddComponent<AllyAttackRouter>();
        r.owner = owner;
    }

    void OnTriggerEnter2D(Collider2D other) => Route(other);
    void OnTriggerStay2D(Collider2D other) => Route(other);

    void Route(Collider2D other)
    {
        if (owner == null || !owner.isActiveAndEnabled) return;
        if (col == null) col = GetComponent<Collider2D>();
        if (info == null) info = GetComponent<PlayerAttackInfo>();
        if (col == null || !col.enabled) return;
        // 敵/ボスの攻撃判定は持ち主の子なので素通りする(体にだけ当てる)
        if (other.GetComponent<BossHitbox>() != null || other.GetComponent<EnemyMeleeHitbox>() != null) return;

        var enemy = other.GetComponentInParent<EnemyController>();
        WildBossBase boss = enemy == null ? other.GetComponentInParent<WildBossBase>() : null;
        DragonController dragon = enemy == null && boss == null ? other.GetComponentInParent<DragonController>() : null;
        MajinController majin = enemy == null && boss == null && dragon == null ? other.GetComponentInParent<MajinController>() : null;
        ObstacleController obs = enemy == null && boss == null && dragon == null && majin == null ? other.GetComponentInParent<ObstacleController>() : null;
        Component victim = (Component)enemy ?? (Component)boss ?? (Component)dragon ?? (Component)majin ?? obs;
        if (victim == null) return;
        if (info != null) { if (PlayerAttackInfo.AlreadyHit(col, victim)) return; } // 同じ振りで同じ相手へは1回
        else
        {
            // 振りの番号を持たない判定(念のため): 同じ相手へは 0.35秒に1回
            if (recent.TryGetValue(victim, out float t) && Time.time - t < 0.35f) return;
            recent[victim] = Time.time;
        }

        float scale = info != null ? info.damageScale : 1f;
        int dmg = Mathf.Max(1, Mathf.RoundToInt(owner.EffectiveAttackPower * scale));
        var kind = info != null ? info.kind : PlayerAttackKind.Normal;
        Vector3 at = other.ClosestPoint(transform.position);
        int id = owner.AttackerId;

        PlayerController.AllyContext++; // 効果音/HitStop/演出を控えめに
        try
        {
            if (enemy != null) { bool wasDying = enemy.IsDyingOrDead; enemy.AllyApplyHit(id, dmg, kind, at, info); if (!wasDying && enemy.IsDyingOrDead) Kills++; }
            else if (boss != null) boss.NetApplyRemoteHit(id, dmg, at);
            else if (dragon != null) dragon.NetApplyRemoteHit(id, dmg);
            else if (majin != null) majin.NetApplyRemoteHit(id, dmg);
            else obs.ApplyDamage(dmg, at, id);
            if (enemy != null || boss != null || dragon != null || majin != null) info?.onHit?.Invoke();
        }
        finally { PlayerController.AllyContext--; }
        Hits++;
        if (owner.Ally != null) owner.Ally.NoteHitLanded(victim);
    }
}
