using UnityEngine;

// BOSS FINISH(2026-10-06): ドラゴン(荒野/天空)・魔人。HP 0 で報酬を確定し、見た目が終わったら遭遇の終了へ(WildBossBase と同じ考え方)。
public partial class DragonController
{
    [System.NonSerialized] public ushort BossFinishCode, NetBossFinishCode;
    [System.NonSerialized] public bool NetBossFinishLocal;
    BossFinishInfo pendingFinal; bool pendingFinalSet, finishRewarded, finishNotified;
    public bool IsDeadForFinish => state == State.Dead;

    void NoteFinalAttack(Collider2D other, PlayerAttackInfo info) { pendingFinal = BossFinishInfo.FromAttack(other, info, transform.position); pendingFinalSet = true; }

    void BeginBossFinish(bool puppet)
    {
        BossFinishInfo fi;
        if (puppet) { if (!BossFinishInfo.TryUnpack(NetBossFinishCode, out fi)) fi = BossDeathAdapter.Decide(default, false, 1, transform); }
        else fi = BossDeathAdapter.Decide(pendingFinal, pendingFinalSet, netAttacker, transform);
        pendingFinalSet = false;
        BossDeathAdapter.Prepare(this, new[] { flashOverlay, hitFlashOverlay }, hpBar);
        if (!puppet && !finishRewarded)
        {
            finishRewarded = true; bossDefeatRegistered = true;
            if (!NetCombat.RouteBossDefeatReward(NetId) && GameManager.Instance != null) GameManager.Instance.RegisterBossDefeat(mileReward);
        }
        BossFinish.ClearBossHazards();
        float h = sr != null ? sr.bounds.size.y : 3f;
        var body = new BossDeathAdapter(transform, h, new[] { sr }, "Dragon", () =>
        {
            if (hpBar != null) { Destroy(hpBar.gameObject); hpBar = null; }
            gameObject.SetActive(false);
            if (puppet || finishNotified) return;
            finishNotified = true;
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;
            if (BossManager.Instance != null) BossManager.Instance.OnDragonDefeated();
        });
        BossFinish.Begin(body, fi, transform.position + new Vector3(0f, h * 0.5f, 0f), puppet ? NetBossFinishLocal : netAttacker <= 0);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public bool DebugKillWithAttack(BossFinalAttack attack, float dirX = 0f)
    {
        if (state == State.Dead || NetPuppet) return false;
        var pc = PlayerController.Instance;
        float d = dirX != 0f ? dirX : (pc != null ? Mathf.Sign(transform.position.x - pc.transform.position.x) : 1f);
        pendingFinal = new BossFinishInfo { attack = attack, dir = (sbyte)(d < 0f ? -1 : 1) }; pendingFinalSet = true;
        netAttacker = 0;
        TakeDamage(Hp + 999999);
        return state == State.Dead;
    }
#endif
}

public partial class MajinController
{
    [System.NonSerialized] public ushort BossFinishCode, NetBossFinishCode;
    [System.NonSerialized] public bool NetBossFinishLocal;
    BossFinishInfo pendingFinal; bool pendingFinalSet, finishRewarded, finishNotified;
    public bool IsDeadForFinish => state == State.Dead;

    void NoteFinalAttack(Collider2D other, PlayerAttackInfo info) { pendingFinal = BossFinishInfo.FromAttack(other, info, transform.position); pendingFinalSet = true; }

    void BeginBossFinish(bool puppet)
    {
        BossFinishInfo fi;
        if (puppet) { if (!BossFinishInfo.TryUnpack(NetBossFinishCode, out fi)) fi = BossDeathAdapter.Decide(default, false, 1, transform); }
        else fi = BossDeathAdapter.Decide(pendingFinal, pendingFinalSet, netAttacker, transform);
        pendingFinalSet = false;
        BossDeathAdapter.Prepare(this, new[] { flashOverlay, hitFlashOverlay }, hpBar);
        if (!puppet && !finishRewarded)
        {
            finishRewarded = true; bossDefeatRegistered = true;
            if (!NetCombat.RouteBossDefeatReward(NetId) && GameManager.Instance != null) GameManager.Instance.RegisterBossDefeat(mileReward);
        }
        BossFinish.ClearBossHazards();
        float h = sr != null ? sr.bounds.size.y : 3f;
        var body = new BossDeathAdapter(transform, h, new[] { sr }, "Majin", () =>
        {
            if (hpBar != null) { Destroy(hpBar.gameObject); hpBar = null; }
            gameObject.SetActive(false);
            if (puppet || finishNotified) return;
            finishNotified = true;
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;
            if (BossManager.Instance != null) BossManager.Instance.OnMajinDefeated();
        });
        BossFinish.Begin(body, fi, transform.position + new Vector3(0f, h * 0.5f, 0f), puppet ? NetBossFinishLocal : netAttacker <= 0);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public bool DebugKillWithAttack(BossFinalAttack attack, float dirX = 0f)
    {
        if (state == State.Dead || NetPuppet) return false;
        var pc = PlayerController.Instance;
        float d = dirX != 0f ? dirX : (pc != null ? Mathf.Sign(transform.position.x - pc.transform.position.x) : 1f);
        pendingFinal = new BossFinishInfo { attack = attack, dir = (sbyte)(d < 0f ? -1 : 1) }; pendingFinalSet = true;
        netAttacker = 0;
        TakeDamage(Hp + 999999);
        return state == State.Dead;
    }
#endif
}
