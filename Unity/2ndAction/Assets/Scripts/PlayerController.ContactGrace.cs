using System.Collections.Generic;
using UnityEngine;

// 攻撃判定の調整と高速時の相打ち対策(2026-09-30)。
//  1) 近接判定の形の調整(MeleeReach)をキャラごとの設定で各判定へ付ける。
//  2) Contact Grace: この端末のプレイヤーの攻撃が敵/ボスに命中したら、その相手との「体の接触ダメージ」だけを
//     contactGraceDuration 秒受けない。プレイヤー単位(このPlayerControllerが持つ)・相手単位の記録で、
//     敵の当たり判定やダメージそのものには触らない(他のプレイヤー/他の敵/敵の攻撃判定/弾/ボスの攻撃/地形は従来どおり)。
//     命中(PlayerAttackInfo.ScaleDamage の命中通知)だけが条件なので、空振りでは発生しない。
//     既存の被弾後の無敵(Hurt/Invincible)とは別物で、そちらの時間や仕様は変えていない。
public partial class PlayerController
{
    // 前後比較の自動テスト専用(false=従来どおり接触した瞬間に被弾)。通常は常にtrue。
    public static bool ContactGraceEnabled = true;
    // 診断(テスト/デバッグ表示用)
    public static int ContactGraceGranted, ContactGraceBlocked, ContactDamageApplied;
    public string LastDamageSource { get; private set; }
    public float LastDamageTime { get; private set; } = -99f;

    float contactGraceDuration = 0.15f;
    public float ContactGraceDuration => contactGraceDuration;
    readonly Dictionary<GameObject, float> contactGraceUntil = new Dictionary<GameObject, float>();
    readonly List<GameObject> contactGracePrune = new List<GameObject>();
    readonly List<MeleeReach> meleeReaches = new List<MeleeReach>();

    void ApplyHitAssist(CharacterDefinition def)
    {
        contactGraceDuration = def != null ? Mathf.Max(0f, def.contactGraceDuration) : 0.15f;
        contactGraceUntil.Clear();
        meleeReaches.Clear();
        // 剣(黒剣士/双剣士/お嬢様騎士)の前・上・下、竜騎士の槍、格闘家/忍者/吸血鬼/竜人などの技の判定。
        // 飛び道具(弾/矢/魔法/御札)は別のオブジェクトなので対象にならない。
        AddMeleeReach(attackHitbox, def);
        AddMeleeReach(upAttackHitbox, def);
        AddMeleeReach(downAttackHitbox, def);
        AddMeleeReach(lanceHitbox, def);
        AddMeleeReach(kitHitbox, def);
    }

    void AddMeleeReach(Collider2D col, CharacterDefinition def)
    {
        if (!(col is BoxCollider2D)) return;
        var r = col.GetComponent<MeleeReach>();
        if (r == null) r = col.gameObject.AddComponent<MeleeReach>();
        r.Setup(this, def);
        meleeReaches.Add(r);
    }

    public IReadOnlyList<MeleeReach> MeleeReaches => meleeReaches;

    static GameObject GraceKey(Component target) => target.gameObject;

    // 攻撃が敵/ボスに命中した(PlayerAttackInfo.ScaleDamage(attack, victim, …)から。障害物では呼ばれない)。
    // コンボの各段が命中するたびに更新する(空振りでは呼ばれない)。
    public void NotifyAttackLanded(Component target, PlayerAttackInfo attack)
    {
        if (target == null || contactGraceDuration <= 0f) return;
        if (attack != null && attack.suppressHitStop) return; // 結界/燃える地面のような細かい多段では付けない(置いておくだけで守られ続けないように)
        contactGraceUntil[GraceKey(target)] = Time.time + contactGraceDuration;
        ContactGraceGranted++;
        if (contactGraceUntil.Count > 64) PruneContactGrace();
    }

    public bool HasContactGrace(Component target)
    {
        if (!ContactGraceEnabled || target == null) return false;
        return contactGraceUntil.TryGetValue(GraceKey(target), out float until) && Time.time < until;
    }

    void PruneContactGrace()
    {
        contactGracePrune.Clear();
        foreach (var kv in contactGraceUntil) if (kv.Value <= Time.time || kv.Key == null) contactGracePrune.Add(kv.Key);
        foreach (var k in contactGracePrune) contactGraceUntil.Remove(k);
    }

    void RecordDamageTaken(string source)
    {
        LastDamageSource = source;
        LastDamageTime = Time.time;
        if (source != null && source.StartsWith("Enemy:")) ContactDamageApplied++;
    }
}

// 敵の体との接触ダメージを、その場ではなくフレームの最後(攻撃判定の掃引=PlayerAttackSweeperの後)に決める。
// 物理の同じ更新で「攻撃判定が敵に入った」と「体が敵に入った」のどちらの通知が先に来るかは決まっていないため、
// その場で決めると、攻撃が同時に当たっていても通知の順番しだいで相打ちになっていた。
//  ・最後の時点で、その敵にこのプレイヤーの攻撃が命中していれば(Contact Grace中)被弾しない。
//  ・猶予が切れた時にまだ体が重なっていて、敵が被弾リアクション中でもなければ、そこで1回だけ被弾する
//    (攻撃を当て続けても、当てていない間まで守られ続けることはない)。
//  ・離れた/敵が倒れた/リアクション中になった接触は捨てる(従来の「リアクション中の接触は無効」と同じ)。
[DefaultExecutionOrder(1000)]
public class ContactDamageResolver : MonoBehaviour
{
    public static ContactDamageResolver Instance { get; private set; }

    struct Pending
    {
        public EnemyController enemy;
        public Collider2D player;
        public bool deferred; // 猶予で1度見送った(以後は体が重なっている間だけ待つ)
    }

    readonly List<Pending> pending = new List<Pending>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[ContactDamageResolver]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<ContactDamageResolver>();
    }

    public static void Queue(EnemyController enemy, Collider2D player)
    {
        if (Instance == null) { enemy.ApplyContactDamage(); return; }
        foreach (var p in Instance.pending) if (p.enemy == enemy) return; // 同じ敵の接触は1つだけ
        Instance.pending.Add(new Pending { enemy = enemy, player = player });
    }

    void LateUpdate()
    {
        if (pending.Count == 0) return;
        var pc = PlayerController.Instance;
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            var p = pending[i];
            if (p.enemy == null || !p.enemy.CanDealContactDamage || pc == null) { pending.RemoveAt(i); continue; }
            if (p.deferred && (p.player == null || !p.enemy.OverlapsBody(p.player))) { pending.RemoveAt(i); continue; } // もう離れた
            if (pc.HasContactGrace(p.enemy))
            {
                if (!p.deferred) { p.deferred = true; pending[i] = p; PlayerController.ContactGraceBlocked++; }
                continue;
            }
            pending.RemoveAt(i);
            p.enemy.ApplyContactDamage();
        }
    }
}
