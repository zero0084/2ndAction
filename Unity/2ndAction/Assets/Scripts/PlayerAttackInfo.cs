using UnityEngine;

// エリアルコンボ改修(2026-09-11) - マスターの「攻撃 → 敵がリアクション →
// 浮かせる → 空中追撃 → 下攻撃で叩き落とす」という一連のコンボ対応。
// 全ての攻撃Hitboxは共通のタグ"PlayerAttack"を共有している(FireballController
// の反射判定など、タグだけで十分な既存処理を壊さないため)が、
// EnemyController側で「どの攻撃に当たったか」に応じて反応(小さなノック
// バック/打ち上げ/空中追撃時の滞空補助/叩き落とし)を変えるには、タグだけ
// では情報が足りない。この小さなコンポーネントを各Hitbox GameObjectへ
// 追加し(SceneBuilder.CreatePlayer参照)、EnemyController.OnTriggerEnter2D
// が`other.GetComponent<PlayerAttackInfo>()`で読み取る - 見つからない場合
// はNormal扱い(将来誰かが新しいPlayerAttackタグの何かを追加しても、既存の
// 「小さなノックバック」挙動にフォールバックするだけで安全)。
public enum PlayerAttackKind
{
    Normal,      // 通常攻撃(Forward/Backwardの3段コンボ、地上・空中どちらでも使用可)
    Up,          // 上方向攻撃(ジャンプ/二段ジャンプに連動) - 敵を打ち上げる
    Down,        // 下方向攻撃(空中ダイブ本体) - 浮いている敵を叩き落とす
    DownImpact   // 下方向攻撃の着地衝撃(範囲判定) - 周囲へのおまけヒット
}

public class PlayerAttackInfo : MonoBehaviour
{
    public PlayerAttackKind kind;

    // 新4人(2026-09-27) - 攻撃(判定/弾)ごとの倍率。弓のチャージ矢や格闘家の4段目のように
    // 「その判定だけ強い」攻撃のため、命中した瞬間のプレイヤーの状態ではなく判定自身に持たせる
    // (飛んでいる矢は、撃った後にプレイヤーが別の技を出しても撃った時の威力のまま)。
    // 既定値(1/1/0)のままなら従来と完全に同じ(既存5人の判定はすべて既定値)。
    public float damageScale = 1f;
    public float knockbackScale = 1f;
    public float hitStop;

    // 10〜12人目(2026-09-28) - 結界/燃える地面のような「細かく何度も当たる」判定用。trueなら命中しても
    // 全体のHitStop(一瞬の停止)や敵の地上ノックバックを起こさない。既定false=従来どおり。
    public bool suppressHitStop;
    public bool suppressKnockback;
    // 敵/ボスに命中した瞬間に呼ばれる(吸血鬼のBlood Gauge等)。障害物では呼ばない。既定null=何もしない。
    public System.Action onHit;
    // 2026-09-30: trueなら MeleeReach(キャラ別の判定調整/高速補正)を掛けない(吸血鬼の血のSlashのように「今の間合いのまま」にしたい技)。
    // 技の判定を出すたびに ArmKitBox が false へ戻す。
    [System.NonSerialized] public bool fixedReach;
    // 属性(2026-10-03): 属性の効果が出した攻撃(風刃)。これ自身からは風刃を出さない(連鎖で増え続けない)
    [System.NonSerialized] public bool elementProc;
    // First / Combo / Finisher (2026-10-03, PlayerController.AttackSeq.cs): tag and press id given when the attack was made
    [System.NonSerialized] public AttackSeqTag seqTag;
    [System.NonSerialized] public int seqMoveId;

    // 障害物の耐久力/高速時のすり抜け対策(2026-09-29)。
    // SwingId: この判定の「1回の振り(発射)」の番号。同じ振りでは敵/障害物へ1回しか当たらない(判定の重複で二重に減らない)。
    // 判定が有効になるたび(PlayerAttackSweeperが有効化の瞬間を検出)、または技の開始時(ArmKitBox等)に新しい番号になる。
    // 飛び道具は1発ごとに別のGameObjectなので、生成時の番号のまま。
    static int swingCounter;
    public int SwingId { get; private set; }
    public void NewSwing() { SwingId = ++swingCounter; }
    // 判定を別の位置/大きさで出し直した(前の位置からの掃引はしない)
    public void Rearm() { NewSwing(); wasEnabled = false; }
    public static void RearmOf(Component c) { if (c == null) return; var i = c.GetComponent<PlayerAttackInfo>(); if (i != null) i.Rearm(); }
    public static readonly System.Collections.Generic.List<PlayerAttackInfo> Active = new System.Collections.Generic.List<PlayerAttackInfo>();
    [System.NonSerialized] public Collider2D col;
    [System.NonSerialized] public bool wasEnabled;
    [System.NonSerialized] public Bounds prevBounds;

    void Awake() { col = GetComponent<Collider2D>(); NewSwing(); }
    void OnEnable() { if (!Active.Contains(this)) Active.Add(this); wasEnabled = false; }
    void OnDisable() { Active.Remove(this); wasEnabled = false; }

    // Hit target history (2026-10-03): the same attack instance (SwingId: one swing / one projectile / one blast / one zone tick)
    // damages the same boss only once. Normal enemies already had this (EnemyController.AlreadyHitBySwing); bosses / the dragon /
    // the Majin did not, so a projectile that re-entered (or was also reported by the high speed sweep) could hit twice.
    // A blast is a separate attack instance, so "projectile + explosion" two-stage moves still hit twice on purpose.
    static readonly System.Collections.Generic.Dictionary<(Component, int), float> recentHits = new System.Collections.Generic.Dictionary<(Component, int), float>();
    public static int DuplicateHitsBlocked;
    public static bool AlreadyHit(Collider2D attack, Component victim)
    {
        if (attack == null || victim == null) return false;
        var info = attack.GetComponent<PlayerAttackInfo>();
        if (info == null) return false;
        var key = (victim, info.SwingId);
        float now = Time.time;
        if (recentHits.TryGetValue(key, out float t) && now - t < 2f) { DuplicateHitsBlocked++; return true; }
        if (recentHits.Count > 256)
        {
            var old = new System.Collections.Generic.List<(Component, int)>();
            foreach (var kv in recentHits) if (now - kv.Value > 2f || kv.Key.Item1 == null) old.Add(kv.Key);
            foreach (var k in old) recentHits.Remove(k);
        }
        recentHits[key] = now;
        return false;
    }

    // 敵/ボス/障害物がダメージを読む箇所から呼ぶ。倍率1なら値をそのまま返す。
    public static int ScaleDamage(Collider2D attack, int damage) => ScaleDamage(attack, damage, true);

    // 敵/ボスが命中を受け付けた時(victim=受けた側)。命中の演出(AttackFlair)を出してからダメージを返す。
    public static int ScaleDamage(Collider2D attack, Component victim, int damage)
    {
        if (attack != null && victim != null) NotifyFlair(attack, victim);
        // 高速時の相打ち対策(2026-09-30): 命中した相手との体の接触ダメージだけを短時間受けない(PlayerController.ContactGrace.cs)。
        // この端末のPlayerAttack判定はすべてこの端末のプレイヤーのもの(他のプレイヤーの攻撃は判定を持たない見た目だけ)。
        if (attack != null && victim != null && PlayerController.Instance != null)
            PlayerController.Instance.NotifyAttackLanded(victim, attack.GetComponent<PlayerAttackInfo>());
        // First / Finisher bonus: from the tag the attack got when it was made (once per press per enemy), before the move's damage scale
        var seqInfo = attack != null ? attack.GetComponent<PlayerAttackInfo>() : null;
        if (seqInfo != null && seqInfo.seqTag != AttackSeqTag.None && victim != null && PlayerController.Instance != null)
            damage += PlayerController.Instance.ConsumeSeqBonus(seqInfo.seqTag, seqInfo.seqMoveId, victim);
        int result = ScaleDamage(attack, damage, true);
        // 属性(2026-10-03): 敵/ボスへの命中はすべてここを通る。カードで得た属性の効果(炎上/冷気/落雷/風刃/出血)を判定する
        if (victim != null) ElementSystem.OnPlayerHit(victim, attack != null ? attack.GetComponent<PlayerAttackInfo>() : null, result);
        return result;
    }

    static void NotifyFlair(Collider2D attack, Component victim)
    {
        var info = attack.GetComponent<PlayerAttackInfo>();
        if (info != null && info.suppressHitStop) return; // 結界/燃える地面のような細かい多段は派手にしない
        Vector3 ac = attack.bounds.center;
        var vc = victim.GetComponentInChildren<Collider2D>();
        Vector3 pos = vc != null ? (Vector3)vc.bounds.ClosestPoint(ac) : victim.transform.position + Vector3.up * 0.8f;
        pos = Vector3.Lerp(pos, vc != null ? vc.bounds.center : pos, 0.25f);
        var pc = PlayerController.Instance;
        float dirX = pc != null ? Mathf.Sign(pos.x - pc.transform.position.x + 0.001f) : 1f;
        bool boss = victim is WildBossBase || victim is DragonController || victim is MajinController;
        bool strong = boss || (info != null && (info.damageScale >= 1.4f || info.hitStop >= 0.06f || info.kind == PlayerAttackKind.Down || info.kind == PlayerAttackKind.DownImpact));
        AttackFlair.Hit(pos, dirX, strong);
    }

    public static int ScaleDamage(Collider2D attack, int damage, bool notifyHit)
    {
        if (attack == null) return damage;
        var info = attack.GetComponent<PlayerAttackInfo>();
        if (info == null) return damage;
        if (notifyHit) info.onHit?.Invoke();
        if (Mathf.Approximately(info.damageScale, 1f)) return damage;
        return Mathf.Max(1, Mathf.RoundToInt(damage * info.damageScale));
    }
}
