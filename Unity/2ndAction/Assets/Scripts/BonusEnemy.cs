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
    // PERFECT判定(2026-10-01): 画面に現れた時刻(<0=まだ)/倒された
    public float FirstSeenTime { get; private set; } = -1f;
    public bool Seen => FirstSeenTime >= 0f;
    public bool Killed { get; private set; }
    // 倒されて死亡演出の最中(撃破の報酬はこの後で出る)。PERFECTの判定では撃破として数える
    public bool Dying => !Killed && ec != null && ec.IsDying;
    int comboHits; // 続けて報酬が出たHit数(連続取得SEの高さ)

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

    void Update()
    {
        if (Seen) return;
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 v = cam.WorldToViewportPoint(transform.position);
        if (v.z > 0f && v.x > 0.02f && v.x < 0.98f && v.y > -0.05f && v.y < 1.05f)
        {
            FirstSeenTime = Time.time;
            if (BonusZone.Instance != null) BonusZone.Instance.NotifySeen(this);
        }
    }

    BonusZoneProfile P => BonusZone.Instance != null ? BonusZone.Instance.Profile : null;

    // この端末のPlayerの攻撃が当たった(倒した一撃も含む)
    public void OnLocalHit(PlayerAttackKind attack, bool wasLaunched, bool killed, Vector3 point)
    {
        if (Time.time - LastHitTime > 0.6f) comboHits = 0;
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
        comboHits++;
        int got = zone.RewardMile(baseAmount, point, this, false, comboHits);
        MileGiven += Mathf.Max(0, baseAmount);
        if (got <= 0) return;
        if (kind == BonusEnemyKind.Mimic) zone.AddMimicMile(got);
    }

    // 倒した(落下死も含む)。報酬は倒した本人の端末で出す(マルチは未対応: 自然発生させない)
    public void OnKilled(bool fallDeath)
    {
        var zone = BonusZone.Instance; var p = P;
        if (zone == null || p == null) return;
        Vector3 pos = transform.position + Vector3.up * 0.8f;
        if (Killed) return;
        Killed = true;
        zone.NotifyKilled(this);
        switch (kind)
        {
            case BonusEnemyKind.TreasureGoblin: if (!Leaving) { zone.RewardMile(p.goblinKillMile, pos, this, true); zone.KillBurst(pos, new Color(1f, 0.85f, 0.25f), true); } break;
            case BonusEnemyKind.Mimic: if (!Leaving) { zone.RewardMile(p.mimicKillMile, pos, this, true); zone.KillBurst(pos, new Color(1f, 0.85f, 0.25f), true); } break;
            case BonusEnemyKind.GoldenSlime:
                {
                    var gm = GameManager.Instance;
                    float exp = p.goldenSlimeExp + p.goldenSlimeExpPerLevel * (gm != null ? gm.ExpToNext : 0f);
                    zone.RewardExp(exp, pos, this, true);
                    zone.KillBurst(pos, new Color(0.5f, 1f, 0.55f), false);
                }
                break;
            case BonusEnemyKind.CardFairy: zone.RewardCard(pos, this); break;
        }
    }

    void OnDestroy() { if (BonusZone.Instance != null) BonusZone.Instance.UnregisterEnemy(this); }
    void OnDisable() { if (!Killed && BonusZone.Instance != null) BonusZone.Instance.NotifyGone(this); }
}
