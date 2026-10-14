using System.Collections.Generic;
using UnityEngine;

// CPU サポート(2026-10-14): 仲間1人の頭脳と HP。体は本物の PlayerController(仲間モード、PlayerController.Ally.cs)。
//  ・走る速さはプレイヤーの今の速さを基準に、決めた位置(プレイヤーの少し後ろ)へ寄るように少しだけ上下させる(前へ出すぎない)
//  ・ジャンプ/攻撃/回避はプレイヤーの補助と同じ判断(HighSpeedAssist の判断役。地形の先読み・ボスの攻撃の回避・届く時だけ攻撃)
//  ・被弾は自分で調べる(体のタグが "Ally" なので、敵/ボス/罠の「Player に当たったら」には入らない)
//  ・HP 0 で倒れる → CpuParty が一定時間後にプレイヤーの近くの安全な場所で復帰させる(ランは終わらない)
//  ・育成: レベルはプレイヤーのレベルに合わせて、上がるたび/ボスを倒すたびにおすすめ構成の順で1枚取る(止めない)
[DefaultExecutionOrder(-700)]
public class CpuAlly : MonoBehaviour
{
    public PlayerController pc;
    public HighSpeedAssist planner;
    public CharacterDefinition def;
    public CpuBuilds.Build build;
    public int Index;           // 0..2
    public int Pn => Index + 2; // 参加番号(P1 = プレイヤー)
    public int AttackerId => CpuParty.AttackerBase + Pn;
    public string CharacterId => def != null ? def.characterId : "";

    public int Hp { get; private set; }
    public int MaxHp { get; private set; }
    public bool Down { get; private set; }
    public float ReviveAt { get; private set; }
    public float AutoSpeed { get; private set; }
    public readonly Dictionary<string, int> Levels = new Dictionary<string, int>();
    public readonly List<string> Picks = new List<string>(); // 取った順(中断データ用)

    // 確認用
    public int HitsTaken, Downs, Revives, Warps, CardPicks, LandedHits, Falls;
    public string LastWarpReason = "";
    // プレイヤーからの目標の位置。ふだんは後ろ。前の近くに敵/ボスがいる時(Engaged)は、近接は少し前へ出て戦い、遠距離は後ろから撃つ
    public float FollowOffset => Engaged ? (IsMelee ? 0.9f + 0.7f * Index : -1.0f - 0.8f * Index) : -1.4f - 1.3f * Index;
    public bool Engaged { get; private set; }
    bool IsMelee => pc != null && !pc.AssistForwardIsProjectile && !pc.AssistIsMageFlight;
    float engageCheckAt;
    static readonly Collider2D[] near = new Collider2D[16];

    // プレイヤーの前 12m 以内に、生きている敵/ボスがいるか(0.15秒ごと)
    void UpdateEngaged(PlayerController leader)
    {
        if (Time.time < engageCheckAt) return;
        engageCheckAt = Time.time + 0.15f;
        Vector2 c = new Vector2(leader.transform.position.x + 6f, leader.transform.position.y + 1f);
        int n = Physics2D.OverlapBoxNonAlloc(c, new Vector2(13f, 8f), 0f, near);
        bool found = false;
        for (int i = 0; i < n && !found; i++)
        {
            var col = near[i];
            if (col == null) continue;
            var ec = col.GetComponentInParent<EnemyController>();
            if (ec != null) { if (!ec.IsDyingOrDead && ec.bonus == null) found = true; continue; }
            if (col.GetComponentInParent<WildBossBase>() != null || col.GetComponentInParent<DragonController>() != null || col.GetComponentInParent<MajinController>() != null) found = true;
        }
        if (!found && BossBattle.AnyBossFighting)
            foreach (var b in BossBattle.Living)
                if (b != null && b.isActiveAndEnabled && Mathf.Abs(b.transform.position.x - leader.transform.position.x) < 25f) { found = true; break; }
        Engaged = found;
    }

    public void Init(CharacterDefinition d, int index, CpuBuilds.Build b)
    {
        def = d; Index = index; build = b;
        Levels.Clear(); Picks.Clear();
        // ラン開始から効くキャラカード(プレイヤーがそのカードを持っている Lv。持っていなければ Lv1)
        if (build != null) foreach (var id in build.charCards) AddLevels(id, Mathf.Max(1, AllyCardStats.OwnedLevel(id)), false);
        Recompute(true);
    }

    // ---- 育成 ---- //
    int AddLevels(string id, int n, bool asPick)
    {
        if (string.IsNullOrEmpty(id) || n <= 0) return 0;
        Levels.TryGetValue(id, out int cur);
        int add = Mathf.Min(n, CardInventory.MaxCardLevel - cur);
        if (add <= 0) return 0;
        Levels[id] = cur + add;
        if (asPick) { Picks.Add(id); CardPicks++; }
        return add;
    }

    // レベルアップ/ボス撃破: おすすめの順で、まだ Lv9 に届いていないカードを1枚取る(止めない)
    public void PickNext()
    {
        if (build == null) return;
        foreach (var id in build.priority)
        {
            Levels.TryGetValue(id, out int cur);
            if (cur >= CardInventory.MaxCardLevel) continue;
            if (CardDatabase.FindBaseById(id) == null) continue;
            if (AddLevels(id, Mathf.Max(1, AllyCardStats.OwnedLevel(id)), true) > 0) { Recompute(false); return; }
        }
    }

    // 中断データから: 取った順に取り直す(同じ結果になる)
    public void RestorePicks(IList<string> picks)
    {
        if (picks == null) return;
        foreach (var id in picks) AddLevels(id, Mathf.Max(1, AllyCardStats.OwnedLevel(id)), true);
        Recompute(true);
    }

    void Recompute(bool fill)
    {
        if (pc == null) return;
        var totals = AllyCardStats.Compute(Levels, def, out int max);
        pc.ApplyCardTotals(totals);
        int before = MaxHp;
        MaxHp = max;
        if (fill || before <= 0) Hp = MaxHp;
        else if (MaxHp > before) Hp = Mathf.Min(MaxHp, Hp + (MaxHp - before)); // 増えた分だけ回復
        else Hp = Mathf.Min(Hp, MaxHp);
    }

    // ---- HP ---- //
    public bool ApplyDamage(bool isFall, int amount, string source)
    {
        if (Down) return false;
        if (!isFall && pc != null && pc.TryConsumeShield()) return false; // SHIELD のカード
        int dmg = Mathf.Max(1, amount);
        if (dmg > CombatScale.PlayerHit && Hp >= MaxHp && dmg >= Hp) dmg = Mathf.Max(CombatScale.PlayerHit, Hp - CombatScale.PlayerHit); // 満タンから1発では倒れない(プレイヤーと同じ)
        Hp = Mathf.Max(0, Hp - dmg);
        HitsTaken++;
        if (isFall) Falls++;
        if (Hp <= 0) CpuParty.NotifyDown(this);
        return true;
    }

    public void Heal(int amount) { if (!Down) Hp = Mathf.Min(MaxHp, Hp + Mathf.Max(0, amount)); }

    public void SetDown(float reviveAt) { Down = true; ReviveAt = reviveAt; Downs++; if (pc != null) pc.AllyDown(); }
    public void SetRevived(int hp) { Down = false; Hp = Mathf.Clamp(hp, 1, MaxHp); Revives++; if (pc != null) pc.AllyRevive(); }
    public void SetHp(int hp) { Hp = Mathf.Clamp(hp, 0, MaxHp); }

    public void NoteHitLanded(Component victim) { LandedHits++; }

    // ---- 判断(PlayerController.Update の入力の代わり) ---- //
    public PlayerController.FlickDirection? Decide()
    {
        if (pc == null || planner == null || Down) return null;
        return planner.Decide(pc);
    }

    void Update()
    {
        var leader = PlayerController.Instance;
        if (pc == null || leader == null) { AutoSpeed = 0f; return; }
        // 走る速さ: プレイヤーの今の速さ × 位置の補正(目標より後ろなら少し速く、前なら少し遅く)
        // 基準: プレイヤーが実際に進んでいる速さ(止まったら止まる)。走り出しの直後など測れていない時は本来の速さ
        // (攻撃の踏み込みの一瞬の速さは拾わない: 本来の走る速さを上限にする)
        float baseSpeed = CpuParty.LeaderSpeed > 0.5f ? Mathf.Min(CpuParty.LeaderSpeed, leader.CurrentAutoRunSpeed) : leader.CurrentAutoRunSpeed;
        UpdateEngaged(leader);
        float target = leader.transform.position.x + FollowOffset;
        float dx = pc.transform.position.x - target;
        // 目標より後ろなら速く(大きく遅れた時はより速く追いつく)、前なら遅く
        float k = Mathf.Clamp(1f - dx * 0.12f, 0.55f, dx < -6f ? 1.9f : 1.45f);
        if (CpuParty.LeaderSpeed <= 0.5f && dx > 0f) k = 0f; // プレイヤーが止まっていて、目標より前: 止まる
        float ahead = pc.transform.position.x - leader.transform.position.x;
        if (ahead > (Engaged ? 4f : 3f)) k = Mathf.Min(k, 0.6f); // 前へ出すぎない(先に進んでイベントを起こさない)
        AutoSpeed = Mathf.Max(0f, baseSpeed * k);
        if (!Down && pc.isActiveAndEnabled) ProbeDamage();
    }

    // ---- 被弾の判定(敵/ボス/罠の判定と体が重なっているか) ---- //
    static readonly Collider2D[] hits = new Collider2D[24];
    void ProbeDamage()
    {
        var body = pc.GetComponent<BoxCollider2D>();
        if (body == null || !body.enabled) return;
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted || gm.IsGameOver || gm.CountdownActive || Time.timeScale <= 0f) return;
        Bounds b = body.bounds;
        int n = Physics2D.OverlapBoxNonAlloc(b.center, b.size * 0.9f, 0f, hits);
        for (int i = 0; i < n; i++)
        {
            var c = hits[i];
            if (c == null || c.transform.IsChildOf(pc.transform)) continue;
            string src = HazardOf(c);
            if (src == null) continue;
            pc.TakeDamage(false, src); // 無敵/のけぞり中は PlayerController 側で無視される
            if (Down) return;
            break;
        }
    }

    static string HazardOf(Collider2D c)
    {
        var bh = c.GetComponent<BossHitbox>();
        if (bh != null) return bh.IsActive && bh.damagesPlayer ? "BossCombatPart" : null;
        if (c.GetComponent<BossProjectile>() != null) return "BossProjectile";
        if (c.GetComponent<EnemyMeleeHitbox>() != null) return "EnemyMelee";
        if (c.GetComponent<FireballController>() != null) return "Fireball";
        var ch = c.GetComponent<CaveHazard>();
        if (ch != null) return ch.Damaging ? "CaveHazard" : null;
        var ss = c.GetComponent<SkyStrike>();
        if (ss != null) return ss.Damaging ? "SkyStrike" : null;
        if (c.GetComponent<TerrainWallHazard>() != null) return "TerrainWall";
        var oc = c.GetComponentInParent<ObstacleController>();
        if (oc != null) return !oc.Broken && oc.isActiveAndEnabled ? "Obstacle" : null;
        var ec = c.GetComponentInParent<EnemyController>();
        if (ec != null) return ec.CanDealContactDamage && c.gameObject == ec.gameObject ? "Enemy" : null; // 体(本体の判定)だけ
        return null;
    }
}
