using UnityEngine;

// BONUS ZONE(2026-09-29) - 報酬Enemyの報酬の計算。EnemyController(この端末のPlayerの攻撃が当たった/倒した)から呼ばれ、
// 報酬はBonusZone経由でGameManagerの既存のRun Progression(仮取得MILE/EXP/Card Choice)へ渡す。
// 動き(逃げる/居座る/跳ねる)はEnemySpecialBehavior.Bonus.cs。ここでは逃走の指示(Leave)だけ持つ。
public class BonusEnemy : MonoBehaviour
{
    public BonusEnemyKind kind;
    EnemyController ec;
    public int MileGiven { get; private set; }
    public int RewardHits { get; private set; }
    public int Hits { get; private set; }
    public bool Leaving { get; private set; }   // BONUS終了/上限到達で逃げている(もう報酬は出ない)
    public bool Capped { get; private set; }    // ミミックの報酬上限に届いた
    public float SpawnTime { get; private set; }
    public float LastHitTime { get; private set; } = -100f;

    public void Init(BonusEnemyKind k, EnemyController controller)
    {
        kind = k; ec = controller; SpawnTime = Time.time;
        if (ec != null) ec.bonus = this;
        var zone = BonusZone.Instance;
        if (zone != null) zone.RegisterEnemy(this);
        // ミミックはPlayerの前に居座る間も、動く向きではなくPlayerの方を向く
        var facing = GetComponent<EnemyFacing>();
        if (facing != null && k == BonusEnemyKind.Mimic) facing.alwaysFacePlayer = true;
    }

    public void Leave() { Leaving = true; }

    BonusZoneProfile P => BonusZone.Instance != null ? BonusZone.Instance.Profile : null;

    // この端末のPlayerの攻撃が当たった(倒した一撃も含む)
    public void OnLocalHit(PlayerAttackKind attack, bool wasLaunched, bool killed, Vector3 point)
    {
        Hits++;
        LastHitTime = Time.time;
        var zone = BonusZone.Instance; var p = P;
        if (zone == null || p == null || Leaving) return;
        switch (kind)
        {
            case BonusEnemyKind.TreasureGoblin:
                Give(zone, p.goblinHitMile, point);
                break;
            case BonusEnemyKind.Mimic:
                if (Capped) return;
                float mul = 1f;
                bool aerial = PlayerController.Instance != null && !PlayerController.Instance.IsGrounded;
                if (attack == PlayerAttackKind.Down || attack == PlayerAttackKind.DownImpact) mul = p.mimicSlamMultiplier;
                else if (attack == PlayerAttackKind.Up) mul = p.mimicLaunchMultiplier;
                else if (aerial || wasLaunched) mul = p.mimicAerialMultiplier;
                int amount = Mathf.Max(1, Mathf.RoundToInt(p.mimicHitMile * mul));
                amount = Mathf.Min(amount, Mathf.Max(0, p.mimicMaxMile - MileGiven)); // 1体から出せる上限
                if (amount > 0) { RewardHits++; Give(zone, amount, point); }
                if (MileGiven >= p.mimicMaxMile || RewardHits >= p.mimicMaxRewardHits)
                {
                    Capped = true; // 上限: これ以上は出さず、逃げ出す(無限稼ぎの防止)
                    zone.Popup(point, "EMPTY!", new Color(0.8f, 0.85f, 1f), this, true);
                }
                break;
        }
    }

    void Give(BonusZone zone, int baseAmount, Vector3 point)
    {
        int got = zone.RewardMile(baseAmount, point, this);
        MileGiven += Mathf.Max(0, baseAmount);
        if (got <= 0) return;
    }

    // 倒した(落下死も含む)。報酬は倒した本人の端末で出す(マルチは未対応: 自然発生させない)
    public void OnKilled(bool fallDeath)
    {
        var zone = BonusZone.Instance; var p = P;
        if (zone == null || p == null) return;
        Vector3 pos = transform.position + Vector3.up * 0.8f;
        switch (kind)
        {
            case BonusEnemyKind.TreasureGoblin: if (!Leaving) zone.RewardMile(p.goblinKillMile, pos, this, true); break;
            case BonusEnemyKind.Mimic: if (!Leaving) zone.RewardMile(p.mimicKillMile, pos, this, true); break;
            case BonusEnemyKind.GoldenSlime: zone.RewardExp(p.goldenSlimeExp, pos, this); break;
            case BonusEnemyKind.CardFairy: zone.RewardCard(pos, this); break;
        }
    }

    void OnDestroy() { if (BonusZone.Instance != null) BonusZone.Instance.UnregisterEnemy(this); }
}
