using System.Collections.Generic;
using UnityEngine;

// 独立した障害物(石/木/壁/大岩)の共通の挙動。
// 2026-09-29: すべての障害物が耐久力を持ち、プレイヤーの正規の攻撃判定(近接/飛び道具)で壊せるようにした。
//  ・耐久力と素材(木/岩/大型)はObstacleBalance(Resources/Obstacles/ObstacleBalance.asset)から。
//  ・同じ攻撃判定の1回の振り(PlayerAttackInfo.SwingId)では1回しかダメージを受けない(判定の重複で減りすぎない)。
//  ・耐久力が0になった瞬間に当たり判定を無効にする(破壊演出の終了を待たずに通過できる)。
//  ・体当たり: 耐久力が残る間は従来どおり(被弾。石/壁/大岩はその場から消え、壊せる木は残る)。
//    同じフレームに攻撃で壊れた場合は被弾させない(接触の処理をフレームの最後まで待つ: LateUpdate)。
//  ・マルチ: HOSTが耐久力/破壊を確定して全員へ共有(NetObstacles)。JOINは自分の攻撃による破壊を先に見せ(予測)、HOSTへ要求する。
//  ・経験値/カード/お金などの報酬は出さない(地形の一部という扱い)。
[DefaultExecutionOrder(100)]
public class ObstacleController : MonoBehaviour
{
    // 以前からの項目(テスト/互換用)。現在はすべてtrue。
    public bool breakable = true;
    public int hp = 1;
    public int maxHp = 1;
    public string kind = "";
    public ObstacleMaterial material = ObstacleMaterial.Rock;
    public bool vanishOnContact = true;

    public bool Broken { get; private set; }
    // マルチ: HOSTが付ける共有ID(0=未共有)。JOINの物はHOSTの写し。
    [System.NonSerialized] public int NetId;
    [System.NonSerialized] public bool NetReplica;
    public int PredictedHp { get; private set; } = -1; // JOIN: 自分の攻撃で減らした見込み(HOSTの確定待ち)

    public static int TotalBroken, TotalHits;
    // 診断/マルチの自動テスト用: 体当たりの被弾 / 自分より前にある障害物を他のプレイヤーが壊した回数
    public static int TotalContactDamage, RemoteBrokenAhead;
    int lastAttacker;
    // 前後比較の自動テスト専用: 改修前の規則(壊せる木だけ耐久2、他は壊れない)を再現する。通常は常にfalse。
    public static bool LegacyRules;
    public static readonly List<ObstacleController> All = new List<ObstacleController>();

    SpriteRenderer visual;
    Vector3 visualBase;
    float shakeUntil, shakeAmount;
    bool pendingContact;
    bool configured;
    public int CrackStage { get; private set; }
    // 自動テスト/診断用
    public int HitsTaken { get; private set; }
    public float BrokenAt { get; private set; } = -1f;
    public bool ContactDamaged { get; private set; }
    public SpriteRenderer Visual => visual;
    // LAST CORRIDOR(2026-09-29): 落ちてくる構造物の演出で、絵だけを上に持ち上げておく量(当たり判定は最初から着地位置)。
    [System.NonSerialized] public float visualLift;

    // 同じ振りでの二重ヒット防止(攻撃判定ごとに最後に当たったSwingId)
    readonly Collider2D[] hitCols = new Collider2D[8];
    readonly int[] hitSwings = new int[8];
    int hitNext;

    void Awake()
    {
        visual = GetComponentInChildren<SpriteRenderer>();
        if (visual != null) visualBase = visual.transform.localPosition;
        if (maxHp < hp) maxHp = Mathf.Max(1, hp);
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // ObstacleSpawnerが置いた直後に呼ぶ(spec名 → 耐久力/素材)
    public void Setup(string kindName)
    {
        kind = kindName;
        var e = ObstacleBalance.Get().Find(kindName);
        if (LegacyRules)
        {
            breakable = kindName == "BreakableTree";
            maxHp = hp = (breakable ? 2 : 1) * CombatScale.K;
            material = breakable ? ObstacleMaterial.Wood : ObstacleMaterial.Rock;
            vanishOnContact = !breakable;
            configured = true;
            name = "Obstacle_" + kindName;
            return;
        }
        breakable = true;
        maxHp = hp = e != null ? Mathf.Max(1, e.durability) : Mathf.Max(1, hp);
        material = e != null ? e.material : ObstacleMaterial.Rock;
        vanishOnContact = e == null || e.vanishOnContact;
        configured = true;
        name = "Obstacle_" + kindName;
    }

    public int Hp => hp;
    public float HpFraction => maxHp > 0 ? Mathf.Clamp01(hp / (float)maxHp) : 0f;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (Broken) return;
        if (other.CompareTag("PlayerAttack")) { ReceiveAttack(other); return; }
        if (other.CompareTag("Player")) pendingContact = true; // 同じフレームに攻撃で壊れたら被弾させない(LateUpdateで判定)
    }

    // プレイヤーの攻撃判定が当たった(物理の接触、または移動途中の接触を拾うPlayerAttackSweeperから)。
    public bool ReceiveAttack(Collider2D attack)
    {
        if (Broken || !breakable || attack == null || !gameObject.activeInHierarchy) return false;
        if (maxHp < hp) maxHp = hp; // Setupを通らずに作られた物(テスト等)
        var info = attack.GetComponent<PlayerAttackInfo>();
        int swing = info != null ? info.SwingId : 0;
        for (int i = 0; i < hitCols.Length; i++) if (hitCols[i] == attack && hitSwings[i] == swing) return false; // この振りでは当たり済み
        hitCols[hitNext] = attack; hitSwings[hitNext] = swing; hitNext = (hitNext + 1) % hitCols.Length;

        var pc = PlayerController.Instance;
        int damage = Mathf.Max(1, PlayerAttackInfo.ScaleDamage(attack, pc != null ? pc.EffectiveAttackPower : 1, false));
        Vector3 at = attack.bounds.ClosestPoint(transform.position + Vector3.up * 0.5f);
        TotalHits++;
        HitsTaken++;
        if (NetReplica && NetId != 0)
        {
            // JOIN: HOSTへ要求し、結果は先に見せる(高速で走っていても、壊した障害物に遅れて当たらない)
            int basis = PredictedHp >= 0 ? Mathf.Min(PredictedHp, hp) : hp;
            PredictedHp = Mathf.Max(0, basis - damage);
            NetObstacles.RequestHit(this, damage);
            if (PredictedHp <= 0) Break(at);
            else { HitFx(at); SetCrackStage(StageFor(PredictedHp)); }
            return true;
        }
        ApplyDamage(damage, at, NetObstacles.LocalPlayerNumber);
        return true;
    }

    // HOST/ソロ: ダメージを確定する(マルチなら全員へ知らせる)
    public void ApplyDamage(int damage, Vector3 at, int attacker)
    {
        if (Broken) return;
        lastAttacker = attacker;
        if (maxHp < hp) maxHp = hp;
        hp = Mathf.Max(0, hp - Mathf.Max(1, damage));
        NetObstacles.AuthorityDamaged(this, damage, attacker);
        if (hp <= 0) Break(at);
        else { HitFx(at); SetCrackStage(StageFor(hp)); }
    }

    // JOIN: HOSTから届いた耐久力(確定値)
    public void NetSetHp(int newHp, bool showFx)
    {
        if (Broken) return;
        if (showFx) lastAttacker = -1;
        hp = Mathf.Max(0, newHp);
        if (PredictedHp >= 0 && PredictedHp >= hp) PredictedHp = -1; // 予測が追いついた
        if (showFx) HitFx(transform.position + Vector3.up * 0.5f);
        SetCrackStage(StageFor(PredictedHp >= 0 ? Mathf.Min(PredictedHp, hp) : hp));
    }

    int StageFor(int h) => h >= maxHp ? 0 : h > maxHp * 0.5f ? 1 : 2;

    void SetCrackStage(int s)
    {
        if (s <= CrackStage) return;
        CrackStage = s;
        ObstacleFx.ShowCracks(this, s);
    }

    void HitFx(Vector3 at)
    {
        var bal = ObstacleBalance.Get();
        shakeUntil = Time.time + bal.hitShakeTime * (material == ObstacleMaterial.Heavy ? 0.8f : 1f);
        shakeAmount = bal.hitShakeAmount * (material == ObstacleMaterial.Wood ? 1.2f : material == ObstacleMaterial.Heavy ? 0.6f : 1f);
        ObstacleFx.HitChips(this, at);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Hit, 0.6f);
    }

    // 破壊: 当たり判定/接触ダメージは即座に無効、見た目は演出(ObstacleFx)が引き継ぐ
    public void Break(Vector3 at)
    {
        if (Broken) return;
        Broken = true;
        hp = 0;
        BrokenAt = Time.time;
        pendingContact = false;
        foreach (var c in GetComponentsInChildren<Collider2D>()) c.enabled = false;
        TotalBroken++;
        var lp = PlayerController.Instance;
        if (lastAttacker != 0 && lastAttacker != NetObstacles.LocalPlayerNumber && lp != null && transform.position.x > lp.transform.position.x) RemoteBrokenAhead++;
        NetObstacles.AuthorityBroken(this);
        ObstacleFx.Break(this, at);
    }

    // 演出が見た目を引き継いだ後に消す
    public void FinishBreak() { if (gameObject != null) gameObject.SetActive(false); }

    void LateUpdate()
    {
        if (visual != null)
        {
            if (Time.time < shakeUntil)
            {
                float k = (shakeUntil - Time.time) / Mathf.Max(0.01f, ObstacleBalance.Get().hitShakeTime);
                visual.transform.localPosition = visualBase + new Vector3(Mathf.Sin(Time.time * 90f) * shakeAmount * k, Mathf.Cos(Time.time * 70f + visualLift) * shakeAmount * 0.4f * k + visualLift, 0f);
            }
            else
            {
                Vector3 want = visualBase + new Vector3(0f, visualLift, 0f);
                if (visual.transform.localPosition != want && !Broken) visual.transform.localPosition = want;
            }
        }
        if (!pendingContact) return;
        pendingContact = false;
        if (Broken) return; // 同じフレームに攻撃で壊れた: 被弾しない
        ContactDamaged = true;
        TotalContactDamage++;
        if (PlayerController.Instance != null) PlayerController.Instance.TakeDamage(source: "Obstacle:" + (string.IsNullOrEmpty(kind) ? name : kind));
        if (!configured || vanishOnContact)
        {
            // 石/壁/大岩: 体当たりした個体はその場から消える(従来どおり。見た目が重なり続けない)
            Broken = true;
            foreach (var c in GetComponentsInChildren<Collider2D>()) c.enabled = false;
            gameObject.SetActive(false);
        }
    }
}
