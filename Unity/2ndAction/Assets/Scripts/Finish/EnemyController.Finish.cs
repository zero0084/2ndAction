using UnityEngine;

// Enemy FINISH System(2026-10-06)の敵本体の側。
//  HP 0 の瞬間(FinishDeath): 死亡確定(dying)→ 見た目の分身を FinishFx へ渡す → 本体を非表示(SetActive(false): 当たり判定/AI/攻撃/
//  接触ダメージ/狙いの対象/敵の数から外れる)→ 報酬を確定(RegisterKillReward)。BONUS ZONE の数え方のため「非表示 → 報酬」の順は従来どおり。
//  どの攻撃で倒したか(向き/種類)は、命中した瞬間の攻撃判定(PlayerAttackInfo)とプレイヤーの状態から決める(攻撃判定は二重に持たない)。
public partial class EnemyController
{
    [System.NonSerialized] public EnemyDefinition Definition; // 出現させた定義(GroundFactory が入れる。重さ/空中の判定に使う)
    FinishInfo lastFinish; bool lastFinishSet;
    [System.NonSerialized] public ushort NetFinishCode;   // HOST: 決めた FINISH / JOIN: HOST から届いた FINISH
    [System.NonSerialized] public bool NetFinishLocal;    // JOIN: 自分のプレイヤーが倒した(HitStop を掛ける)
    public static bool FinishEnabled => FinishTuning.I.enabled;
    public FinishInfo LastFinish => lastFinish; // 確認用

    float FinishMass()
    {
        var t = FinishTuning.I; var d = Definition;
        float m = t.massDefault;
        if (d != null)
        {
            if (d.finishMass > 0f) m = d.finishMass;
            else
            {
                if (d.category == EnemyCategory.Heavy) m = t.massHeavyCategory;
                if (d.movementType == EnemyMovementType.Flying) m = Mathf.Min(m, t.massFlying);
                m *= Mathf.Max(0.6f, d.visualScaleMultiplier);
            }
            m *= 1f + Mathf.Clamp01(d.finishKnockbackResistance) * 3f;
        }
        if (IsElite) m *= 1.2f;
        return m;
    }

    bool IsFlyingKind => Definition != null && Definition.movementType == EnemyMovementType.Flying;

    // 命中した攻撃から FINISH を決める(この端末のプレイヤーの攻撃)
    void DecideFinishLocal(Collider2D attack, PlayerAttackInfo info, PlayerAttackKind kind, int damage, int hpBefore)
    {
        float dirX = AwayDirFromPlayer();
        // 飛び道具は飛んでいた向き
        var kp = attack != null ? attack.GetComponent<KitProjectile>() : null;
        if (kp != null && Mathf.Abs(kp.velocity.x) > 0.01f) dirX = Mathf.Sign(kp.velocity.x);
        var pb = attack != null ? attack.GetComponent<PlayerBullet>() : null;
        if (pb != null && Mathf.Abs(pb.velocity.x) > 0.01f) dirX = Mathf.Sign(pb.velocity.x);
        var pc = PlayerController.Instance;
        bool aerial = pc != null && !pc.IsGrounded;
        SetFinish(BuildFinish(kind, info, dirX, aerial, damage, hpBefore));
    }

    // JOIN のプレイヤーの攻撃(HOST)/ 属性の継続ダメージ
    void DecideFinishRemote(PlayerAttackKind kind, int damage, int hpBefore, bool noStop)
    {
        var fi = BuildFinish(kind, null, AwayDirFromPlayer(), false, damage, hpBefore);
        fi.noStop |= noStop;
        SetFinish(fi);
    }

    FinishInfo BuildFinish(PlayerAttackKind kind, PlayerAttackInfo info, float dirX, bool aerial, int damage, int hpBefore)
    {
        var t = FinishTuning.I;
        var fi = new FinishInfo { dir = (sbyte)(dirX < 0f ? -1 : 1), mass = FinishMass(), flying = IsFlyingKind };
        fi.shape = kind == PlayerAttackKind.Up ? FinishShape.Up
                 : (kind == PlayerAttackKind.Down || kind == PlayerAttackKind.DownImpact) ? FinishShape.Slam
                 : aerial ? FinishShape.Aerial : FinishShape.Side;
        bool heavy = IsElite || kind == PlayerAttackKind.DownImpact || (kind == PlayerAttackKind.Down && isLaunched)
                     || (info != null && (info.knockbackScale >= t.heavyKnockbackScale || info.hitStop >= t.heavyAttackHitStop || info.seqTag == AttackSeqTag.Finisher));
        bool over = damage >= t.overkillThreshold * Mathf.Max(1, hpBefore);
        fi.type = over ? FinishType.Overkill : heavy ? FinishType.Heavy : FinishType.Normal;
        return fi;
    }

    void SetFinish(FinishInfo fi)
    {
        lastFinish = fi; lastFinishSet = true;
        NetFinishCode = fi.Pack();
    }

    // HP 0: 死亡確定 → 見た目 → 非表示 → 報酬(すべてこのフレームの中で)
    void FinishDeath(Vector3 contactPoint)
    {
        dying = true;
        DisableMotionComponents();
        var fi = lastFinishSet ? lastFinish : new FinishInfo { type = FinishType.Normal, shape = FinishShape.Side, dir = (sbyte)(AwayDirFromPlayer() < 0f ? -1 : 1), mass = FinishMass(), flying = IsFlyingKind };
        lastFinishSet = false;
        if (!hitStopEnabled || hitNoStop) fi.noStop = true;
        lastFinish = fi;
        PlayFinishVisual(fi, contactPoint);
        if (AudioManager.Instance != null)
        {
            if (fi.type != FinishType.Normal || fi.shape == FinishShape.Slam) AudioManager.Instance.PlayStrongHit(); else AudioManager.Instance.PlayAttackHit();
            AudioManager.Instance.PlayEnemyDefeat();
        }
        FinishDeaths++;
        gameObject.SetActive(false);
        RegisterKillReward(fallDeath: false);
    }
    public static int FinishDeaths { get; private set; } // 確認用

    void PlayFinishVisual(FinishInfo fi, Vector3 contactPoint)
    {
        Sprite spr = poseDeath != null ? poseDeath : (sr != null ? sr.sprite : null);
        if (sr == null || spr == null)
        {
            ExplosionEffect.CreateForDefeat(transform.position, deathBurstColor, 1f, sortingOrder: RenderOrder.CombatFx);
            return;
        }
        Color bodyColor = sr.color;
        if (bodyColor == hitFlashColor || NetLocalFlashActive) bodyColor = Color.white;
        bodyColor.a = 1f;
        FinishFx.Play(sr.transform.position, spr, sr.flipX, sr.transform.lossyScale, bodyColor, deathBurstColor, contactPoint, fi);
    }

    // JOIN: HOST が確定した撃破の見た目(同じ FINISH を自分の端末で再生して消す。報酬/死亡の処理はしない)
    bool NetPlayFinishAndRemove()
    {
        if (!FinishEnabled || NetFinishCode == 0 || !isActiveAndEnabled) return false;
        var fi = FinishInfo.Unpack(NetFinishCode);
        if (!NetFinishLocal) fi.noStop = true; // 他の人が倒した敵では自分の画面を止めない
        PlayFinishVisual(fi, transform.position);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayEnemyDefeat();
        Destroy(gameObject);
        return true;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 開発用(DEBUG の FINISH TEST / 自動テスト): 指定の FINISH で倒す(通常の撃破と同じ処理: 死亡確定/報酬/マルチの通知)
    public bool DebugKillWithFinish(PlayerAttackKind kind, bool heavy, bool overkill, bool aerial, float dirX = 0f)
    {
        if (dying || NetReplica || !isActiveAndEnabled) return false;
        EnsureHp();
        int hpBefore = Mathf.Max(1, hp);
        int damage = overkill ? hpBefore * 10 : hpBefore;
        hp = 0;
        netReactionAttacker = 0;
        float dx = dirX != 0f ? dirX : AwayDirFromPlayer();
        var fi = BuildFinish(kind, null, dx, aerial, damage, hpBefore);
        if (heavy && fi.type == FinishType.Normal) fi.type = FinishType.Heavy;
        SetFinish(fi);
        NetCombat.AuthorityDamaged(NetId, 0, damage, 0, (byte)kind, transform.position, true);
        hitNoStop = false; hitNoKnockback = false;
        if (FinishEnabled) FinishDeath(transform.position);
        else ProcessHit(kind, transform.position, true);
        return true;
    }
#endif
}
