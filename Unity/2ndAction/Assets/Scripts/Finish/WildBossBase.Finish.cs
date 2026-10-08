using System.Collections;
using UnityEngine;

// BOSS FINISH SYSTEM(2026-10-06)の荒野/洞窟/天空ボス(WildBossBase 系)の側。
//  HP 0: 死亡確定(dead / 攻撃判定・当たり判定を無効 / AI・必殺技・崩し・段階が止まる)→ 報酬(MILE/EXP/ゲージ)をその場で確定 →
//  ボスの攻撃の残りを片付け(遭遇の最後のボスなら)→ 撃破の見た目(BossFinish)→ 見た目が終わったら非表示 → 遭遇の終了(ボス報酬の選択/次のボス)。
//  HP 0 で別の段階へ移るボス(フェニックスの復活 / 死神の退却)は OnLethalDamage で先に分かれるので、最後の撃破だけがここへ来る。
public abstract partial class WildBossBase : IBossDeathBody
{
    [System.NonSerialized] public ushort BossFinishCode;     // HOST: 決めた撃破の情報(マルチの OpDeath で送る)
    [System.NonSerialized] public ushort NetBossFinishCode;  // JOIN: HOST から届いた撃破の情報
    [System.NonSerialized] public bool NetBossFinishLocal;   // JOIN: 自分のプレイヤーが倒した
    BossFinishInfo pendingFinal; bool pendingFinalSet;
    bool rewardRegistered, encounterNotified;
    public BossFinishInfo LastFinishInfo { get; private set; }

    // ---- IBossDeathBody ----
    public Transform DeathRoot => transform;
    public float DeathBodyHeight => bodyHeight;
    public BossRig DeathRig => rig;
    public string DeathKey => this is ReaperSisterBoss ? "Reaper" : (this is CaveBossBase ? "Cave/" : this is SkyBossBase ? "Sky/" : "Wild/") + bossName;
    public void SetDeathColor(Color c) => SetVisualColor(c);
    public void OnDeathVisualFinished()
    {
        if (hpBar != null) { Destroy(hpBar.gameObject); hpBar = null; }
        gameObject.SetActive(false);
        NotifyDefeatEncounter();
        // 2026-10-08(メモリの修正): 非表示のまま残すと、ボス戦のたびに部品(約100個/体)が溜まり続けていた。少し待ってから消す
        //  (遭遇の終了/報酬の選択はここまでで済んでいる。参照している所は null を見て飛ばす)
        Destroy(gameObject, DefeatedBodyLifetime);
    }
    public const float DefeatedBodyLifetime = 10f;

    // 命中した攻撃(この端末のプレイヤー)の向き/種類を、致死になった時のために控える
    void NoteFinalAttack(Collider2D other, PlayerAttackInfo info)
    {
        pendingFinal = BossFinishInfo.FromAttack(other, info, transform.position);
        pendingFinalSet = true;
    }

    BossFinishInfo DecideFinishInfo()
    {
        BossFinishInfo fi;
        if (pendingFinalSet && netAttacker <= 0) fi = pendingFinal;
        else
        {
            // ULTIMATE / 属性 / 相手のプレイヤー / 跳ね返した火球: 向きはプレイヤーから離れる向き
            float d = player != null ? Mathf.Sign(transform.position.x - player.position.x) : 1f;
            fi = new BossFinishInfo { attack = BossFinalAttack.Other, dir = (sbyte)(d < 0f ? -1 : 1) };
        }
        pendingFinalSet = false;
        var bm = BossManager.Instance;
        bool rematch = RematchTierApplied >= 0 || (bm != null && bm.CurrentEncounterIsRematch) || (bm != null && bm.CurrentBossEncounter != null && bm.CurrentBossEncounter.rush);
        fi.firstKill = !rematch;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (BossFinish.DebugFirstKill >= 0) fi.firstKill = BossFinish.DebugFirstKill == 1;
#endif
        return fi;
    }

    // 撃破の見た目の前の片付け: 姿勢/色/変形を通常へ、当たり判定を全部無効、HP バーを消す
    void PrepareDeathVisual()
    {
        SetAlpha(1f); SetBodyTint(Color.white); extraScale = Vector2.one;
        try { OnInterrupted(); } catch (System.Exception e) { Debug.LogException(e); } // 洞窟: 天井/地中/暗さを戻す
        if (visual != null)
        {
            float artSign = artFacesLeft ? (facing < 0f ? 1f : -1f) : (facing < 0f ? -1f : 1f);
            visual.localScale = new Vector3(scaleFactor * artSign, scaleFactor, 1f);
            visual.localRotation = Quaternion.identity;
            visual.localPosition = Vector3.zero;
        }
        DisableAllHitboxes(); // 攻撃判定と予兆の表示を消す
        if (rig != null) { rig.ResetCells(); rig.SetColor(Color.white); }
        foreach (var c in GetComponentsInChildren<Collider2D>(true)) if (c != null) c.enabled = false;
        if (hpBar != null && isActiveAndEnabled) StartCoroutine(hpBar.FadeOutRoutine(0.25f));
    }

    // 報酬(MILE/EXP/ULTIMATE のゲージ)を確定。死亡の瞬間に1回
    void RegisterDefeatReward()
    {
        if (rewardRegistered) return;
        rewardRegistered = true;
        if (NetPuppet) return;
        if (!NetCombat.RouteBossDefeatReward(NetId) && GameManager.Instance != null) GameManager.Instance.RegisterBossDefeat(mileReward);
    }

    // 遭遇の終了(ボス報酬の選択 / 次のボス / ラスダンのラッシュ / 死神の終幕)。撃破の見た目が終わった時に1回
    void NotifyDefeatEncounter()
    {
        if (encounterNotified) return;
        encounterNotified = true;
        defeatRegistered = true; // (従来の RegisterDefeatOnce を通さない)
        if (NetPuppet) return;
        if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return; // ランが終わっている
        if (DefeatOverride != null) { DefeatOverride(this); return; }
        if (BossManager.Instance != null) BossManager.Instance.OnWildBossDefeated(this);
    }

    // TakeDamage の撃破の分岐から(HOST/ソロ)
    void BeginBossFinish(Vector3 hitPos)
    {
        var fi = LastFinishInfo;
        PrepareDeathVisual();
        RegisterDefeatReward();
        BossFinish.ClearBossHazards();
        BossFinish.Begin(this, fi, hitPos, localImpact: netAttacker <= 0);
    }

    // (消された時に遭遇を進めることはしない: 片付け/次のボスの出現と同じフレームで数がずれるため。見た目は必ず時間で終わる)
    void OnDestroyFinish() { }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 開発用(BOSS FINISH TEST / 自動テスト): 指定の攻撃で倒す(通常の撃破と同じ処理)
    public bool IsInvulnerableForQa => invulnerable;
    public bool DebugKillWithAttack(BossFinalAttack attack, float dirX = 0f)
    {
        if (dead || NetPuppet) return false;
        float d = dirX != 0f ? dirX : (player != null ? Mathf.Sign(transform.position.x - player.position.x) : 1f);
        pendingFinal = new BossFinishInfo { attack = attack, dir = (sbyte)(d < 0f ? -1 : 1) };
        pendingFinalSet = true;
        netAttacker = 0;
        invulnerable = false;
        TakeDamage(Hp + 999999, CenterWorld);
        return dead;
    }
#endif
}
