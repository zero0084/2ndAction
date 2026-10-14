using System.Collections.Generic;
using UnityEngine;

// CPU サポート(2026-10-14): オフラインの一人プレイで、CPU の仲間(最大3人)と一緒に走る。
//  ・出発の前に選んだ仲間(Request、0人 = 従来のソロ)を、ランの開始(GameManager.ApplyGameStart)で作る。マルチのランでは作らない
//  ・仲間の体はプレイヤーの複製(PlayerController の仲間モード)。頭脳と HP は CpuAlly
//  ・倒れた仲間: 少しして姿を消し、ReviveSeconds 後にプレイヤーの近くの安全な場所で復帰(HP は ReviveHpFraction)。救助の操作は要らない
//  ・離れすぎた/引っかかった/落ちた仲間: プレイヤーの近くの安全な場所へ戻す(穴・穴の縁・天井の針・障害物/敵/ボスの攻撃の中を避ける)
//  ・プレイヤーが倒れたら従来どおりランの終わり(仲間はその場で止まる)
// 調整値(初期値): ReviveSeconds=10秒 / ReviveHpFraction=0.5 / 姿を消すまで 1.2秒 / 後ろ 25m・前 9m で呼び戻し / 2.5秒進まなければ引っかかり
public class CpuParty : MonoBehaviour
{
    public const int AttackerBase = 100;
    public const int MaxAllies = 3;
    public static float ReviveSeconds = 10f;
    public static float ReviveHpFraction = 0.5f;
    public static float HideAfterDownSeconds = 1.2f;
    public static float WarpBehind = 25f, WarpAhead = 9f, StuckSeconds = 2.5f; // 後ろは被弾ののけぞりで離れても、まず走って追いつく(呼び戻しは最後の手段)

    public static CpuParty Instance { get; private set; }
    public static readonly List<CpuAlly> Allies = new List<CpuAlly>();
    // 次のランの仲間(出発の前に選ぶ。キャラの ID、0〜3人)。シーンを読み直しても残る
    public static readonly List<string> Request = new List<string>();
    public static bool Active => Allies.Count > 0;
    public static int Size => Allies.Count;
    public static int TotalWarps, TotalRevives; // 確認用
    // プレイヤーが実際に進んでいる速さ(m/s、なめらかにした値)。仲間の走る速さの基準(プレイヤーがボス戦の演出/のけぞり等で
    // 止まった時は仲間も止まる。「本来の走る速さ」を基準にすると、止まったプレイヤーを追い越していた)。時間で平均するので fps に左右されない
    public static float LeaderSpeed { get; private set; }
    double leaderLastDist = double.NaN;

    void TrackLeaderSpeed(PlayerController leader)
    {
        float dt = Time.deltaTime;
        double d = leader.DistanceExact;
        if (dt <= 0f) return;
        if (!double.IsNaN(leaderLastDist))
        {
            float v = (float)((d - leaderLastDist) / dt);
            if (v > -80f && v < 200f) LeaderSpeed = Mathf.Lerp(LeaderSpeed, Mathf.Max(0f, v), 1f - Mathf.Exp(-dt / 0.12f)); // 疾走/ワープの跳びは捨てる
        }
        leaderLastDist = d;
    }

    readonly Dictionary<CpuAlly, float> downSince = new Dictionary<CpuAlly, float>();
    readonly HashSet<CpuAlly> sprintHidden = new HashSet<CpuAlly>();
    readonly Dictionary<CpuAlly, (float x, float t)> progress = new Dictionary<CpuAlly, (float, float)>();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 開発用: -cpuAllies archer,mage(出発の画面を通さずに仲間を付ける)/ -cpuRevive 秒
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void DevArgs()
    {
        var a = QaArgs.All;
        for (int i = 0; i < a.Length - 1; i++)
        {
            if (a[i] == "-cpuAllies") { Request.Clear(); foreach (var id in a[i + 1].Split(',')) if (!string.IsNullOrEmpty(id) && Request.Count < MaxAllies) Request.Add(id.Trim()); }
            if (a[i] == "-cpuRevive" && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float s)) ReviveSeconds = Mathf.Max(0.5f, s);
        }
    }
#endif

    // ---- ランの開始/終わり ---- //
    public static void BeginRun()
    {
        Clear();
        if (NetRunLauncher.IsMultiplayerRun || NetSession.IsActive) return; // 人間のマルチには付けない(今回の対象外)
        if (Request.Count == 0) return;
        var ids = new List<string>();
        foreach (var id in Request) if (CharacterDatabase.FindById(id) != null && ids.Count < MaxAllies) ids.Add(id);
        Spawn(ids);
    }

    public static void Spawn(IList<string> ids)
    {
        EnsureInstance();
        for (int i = 0; i < ids.Count && i < MaxAllies; i++)
        {
            var a = SpawnOne(ids[i], i);
            if (a != null) Allies.Add(a);
        }
        MarkLedger();
        ParticipantAura.AttachLocalPlayer();
        Debug.Log($"[CpuParty] spawned {Allies.Count} ally(s): {string.Join(",", ids)}");
    }

    // 記録: サポートの利用と人数(ソロのランキングに混ぜないため)
    static void MarkLedger()
    {
        var run = RunLedger.Current;
        if (run == null) return;
        var ids = new List<string>();
        bool official = true;
        foreach (var a in Allies) if (a != null) { ids.Add(a.CharacterId); if (!UnlockRules.OfficialCharacterUnlocked(a.CharacterId)) official = false; }
        run.partySize = 1 + ids.Count;
        run.supportIds = string.Join(",", ids);
        run.officialParty = official;
    }

    // ---- 中断データ ---- //
    [System.Serializable]
    public class MemberState
    {
        public string characterId = "";
        public List<string> picks = new List<string>();
        public int hp;
        public bool down;
        public float reviveIn; // 倒れている時: 復帰までの残り秒
    }

    public static List<MemberState> Export()
    {
        var list = new List<MemberState>();
        foreach (var a in Allies)
        {
            if (a == null) continue;
            list.Add(new MemberState
            {
                characterId = a.CharacterId, picks = new List<string>(a.Picks), hp = a.Hp, down = a.Down,
                reviveIn = a.Down ? Mathf.Max(0f, a.ReviveAt - Time.time) : 0f,
            });
        }
        return list;
    }

    // CONTINUE: 中断した時の仲間を、同じカード/HP/倒れている状態で(プレイヤーを足場へ置いた後に呼ぶ)
    public static void RestoreFromCheckpoint(List<MemberState> party)
    {
        Clear();
        if (party == null || party.Count == 0 || NetRunLauncher.IsMultiplayerRun || NetSession.IsActive) return;
        EnsureInstance();
        for (int i = 0; i < party.Count && i < MaxAllies; i++)
        {
            var m = party[i];
            var a = SpawnOne(m.characterId, i);
            if (a == null) continue;
            Allies.Add(a);
            a.RestorePicks(m.picks);
            if (m.down)
            {
                a.SetHp(0);
                a.SetDown(Time.time + Mathf.Max(0.5f, m.reviveIn));
                Instance.downSince[a] = Time.time - HideAfterDownSeconds; // 再開の時点で姿は消えている
                a.gameObject.SetActive(false);
            }
            else a.SetHp(m.hp > 0 ? m.hp : a.MaxHp);
        }
        ParticipantAura.AttachLocalPlayer();
        // 記録の人数は中断前の台帳のまま(RunLedger.Resume)。台帳が古い(人数なし)時だけ付け直す
        if (RunLedger.Current != null && RunLedger.Current.partySize <= 1 && Allies.Count > 0) MarkLedger();
        Debug.Log($"[CpuParty] restored {Allies.Count} ally(s) from the suspend data");
    }

    public static void Clear()
    {
        foreach (var a in Allies) if (a != null) Destroy(a.gameObject);
        Allies.Clear();
        if (Instance != null) { Instance.downSince.Clear(); Instance.progress.Clear(); }
    }

    static void EnsureInstance()
    {
        if (Instance != null) return;
        var go = new GameObject("[CpuParty]");
        Instance = go.AddComponent<CpuParty>();
    }

    void OnDestroy() { if (Instance == this) { Instance = null; Allies.Clear(); } }

    // ラン中に作られた子(技の判定/演出/脱出の輪/縁取りなど)は複製しない。仲間のキャラ用に作り直す
    static readonly HashSet<string> RuntimeChildren = new HashSet<string>
    {
        "ReadabilityRim", "KitHitbox", "LanceHitbox", "LanceThrustFx", "LanceDiveTrail", "BloodAura", "EscapeRing", "MuzzleFlash",
        "ArcherChargeGlow", "FinalEvoAura", "UltimateFxLabel", "AuraGlow", "AuraRing",
    };
    // Awake で作り直される子(複製した古い方が残ると、プレイヤーの絵のまま止まった影になる): 同じ名前の兄弟の前半(複製した方)を消す
    static readonly HashSet<string> AwakeChildren = new HashSet<string> { "Outline", "BrightenOverlay", "ContactShadow" };

    static void RemoveClonedDuplicates(GameObject go)
    {
        foreach (var parent in go.GetComponentsInChildren<Transform>(true))
        {
            if (parent == null) continue; // 前の親で消した子
            var groups = new Dictionary<string, List<Transform>>();
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (!AwakeChildren.Contains(c.name)) continue;
                if (!groups.TryGetValue(c.name, out var l)) groups[c.name] = l = new List<Transform>();
                l.Add(c);
            }
            foreach (var l in groups.Values)
                if (l.Count >= 2) for (int i = 0; i < l.Count / 2; i++) DestroyImmediate(l[i].gameObject);
        }
    }

    static CpuAlly SpawnOne(string id, int index)
    {
        var leader = PlayerController.Instance;
        var def = CharacterDatabase.FindById(id);
        if (leader == null || def == null) return null;
        Vector3 pos = leader.transform.position + new Vector3(-1.4f - 1.3f * index, 0f, 0f);
        GameObject go;
        PlayerController.SpawningAlly = true;
        try { go = Instantiate(leader.gameObject, pos, leader.transform.rotation); }
        finally { PlayerController.SpawningAlly = false; }
        go.name = $"CpuAlly{index + 1}_{id}";
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            if (t != null && t != go.transform && RuntimeChildren.Contains(t.name)) DestroyImmediate(t.gameObject);
        RemoveClonedDuplicates(go);
        var clonedAura = go.GetComponent<ParticipantAura>();
        if (clonedAura != null) DestroyImmediate(clonedAura);
        var pc = go.GetComponent<PlayerController>();
        var ally = go.AddComponent<CpuAlly>();
        ally.pc = pc;
        pc.BindAlly(ally);
        HighSpeedAssist.CreatingPlanner = true;
        try { ally.planner = go.AddComponent<HighSpeedAssist>(); }
        finally { HighSpeedAssist.CreatingPlanner = false; }
        pc.ApplyCharacterBaseStats(def);
        var anim = go.GetComponent<PlayerAnimator>();
        if (anim != null) anim.ApplyCharacterAnimationSet(def);
        pc.RetagAllyHitboxes();
        pc.AllyMatchStart(leader);
        ally.Init(def, index, CpuBuilds.For(id));
        pc.AllyWarpTo(pos.x, leader.AllyOnSky);
        // 自分より奥に描く(重なった時にプレイヤーが手前)
        if (pc.VisualRenderer != null) { var vp = pc.VisualRenderer.transform.localPosition; pc.VisualRenderer.transform.localPosition = new Vector3(vp.x, vp.y, vp.z + 0.05f * (index + 1)); }
        var aura = ParticipantAura.Attach(go, pc.VisualRenderer, ally.Pn, false, true, Loc.T(def.displayName));
        if (aura != null) aura.Hidden = () => ally.Down;
        return ally;
    }

    // ---- 育成(プレイヤーのレベルアップ/ボス撃破に合わせて、止めずに1枚ずつ) ---- //
    public static void OnLeaderLevelUp() { foreach (var a in Allies) if (a != null) a.PickNext(); }
    public static void OnBossDefeated() { foreach (var a in Allies) if (a != null) a.PickNext(); }

    // ---- 攻撃の持ち主の位置(敵のノックバックの向き) ---- //
    public static bool TryGetAttacker(int attacker, out Vector3 pos, out float speed)
    {
        pos = Vector3.zero; speed = 0f;
        if (attacker <= AttackerBase) return false;
        foreach (var a in Allies)
        {
            if (a == null || a.AttackerId != attacker || a.pc == null) continue;
            pos = a.pc.transform.position; speed = a.AutoSpeed;
            return true;
        }
        return false;
    }

    public static CpuAlly FindByPn(int pn) { foreach (var a in Allies) if (a != null && a.Pn == pn) return a; return null; }

    // ---- 倒れる/復帰/呼び戻し ---- //
    public static void NotifyDown(CpuAlly a)
    {
        if (a == null || a.Down) return;
        a.SetDown(Time.time + ReviveSeconds);
        if (Instance != null) Instance.downSince[a] = Time.time;
        Debug.Log($"[CpuParty] P{a.Pn} {a.CharacterId} DOWN - revive in {ReviveSeconds:F0}s");
    }

    void Update()
    {
        var leader = PlayerController.Instance;
        var gm = GameManager.Instance;
        if (leader == null || gm == null || Allies.Count == 0) return;
        TrackLeaderSpeed(leader);
        bool running = gm.HasStarted && !gm.IsGameOver && !gm.CountdownActive && !gm.ResumeGateActive && Time.timeScale > 0f;
        // 疾走出発の演出の間: 仲間は姿を消し、到着してから近くへ置く(高速で動くプレイヤーへ毎フレーム呼び戻さない)
        if (gm.SprintActive)
        {
            foreach (var a in Allies) if (a != null && !a.Down && a.gameObject.activeSelf) { a.gameObject.SetActive(false); sprintHidden.Add(a); }
            return;
        }
        if (sprintHidden.Count > 0 && running)
        {
            foreach (var a in sprintHidden) if (a != null && !a.Down) { a.gameObject.SetActive(true); Warp(a, leader, "sprint arrival"); }
            sprintHidden.Clear();
        }
        foreach (var a in Allies)
        {
            if (a == null) continue;
            if (a.Down)
            {
                if (a.gameObject.activeSelf && downSince.TryGetValue(a, out float t0) && Time.time - t0 > HideAfterDownSeconds) a.gameObject.SetActive(false);
                if (running && Time.time >= a.ReviveAt) Revive(a, leader);
                continue;
            }
            if (!running || !a.gameObject.activeSelf) { progress.Remove(a); continue; }
            float dx = a.pc.transform.position.x - (leader.transform.position.x + a.FollowOffset);
            if (dx < -WarpBehind) { Warp(a, leader, "behind"); continue; }
            if (dx > WarpAhead) { Warp(a, leader, "ahead"); continue; }
            if (a.pc.transform.position.y < leader.transform.position.y - 12f) { Warp(a, leader, "fell"); continue; }
            // 引っかかり: プレイヤーが走っているのに一定時間ほとんど進んでいない
            if (!progress.TryGetValue(a, out var p) || a.pc.transform.position.x > p.x + 0.5f) progress[a] = (a.pc.transform.position.x, Time.time);
            else if (Time.time - p.t > StuckSeconds && leader.CurrentAutoRunSpeed > 1f) { Warp(a, leader, "stuck"); progress.Remove(a); }
        }
    }

    void Revive(CpuAlly a, PlayerController leader)
    {
        float x = FindSafeX(leader, a);
        a.gameObject.SetActive(true);
        a.pc.AllyWarpTo(x, leader.AllyOnSky && SkyAt(x, leader).HasValue);
        a.SetRevived(Mathf.Max(1, Mathf.RoundToInt(a.MaxHp * ReviveHpFraction)));
        downSince.Remove(a); progress.Remove(a);
        TotalRevives++;
        Debug.Log($"[CpuParty] P{a.Pn} {a.CharacterId} REVIVED at x={x:F1} (player x={leader.transform.position.x:F1}) hp={a.Hp}/{a.MaxHp}");
    }

    void Warp(CpuAlly a, PlayerController leader, string why)
    {
        float x = FindSafeX(leader, a);
        if (!a.pc.AllyWarpTo(x, leader.AllyOnSky && SkyAt(x, leader).HasValue)) return;
        a.Warps++; a.LastWarpReason = why; TotalWarps++;
        Debug.Log($"[CpuParty] P{a.Pn} {a.CharacterId} warp ({why}) to x={x:F1}");
    }

    static float? SkyAt(float x, PlayerController leader)
    {
        var tm = TerrainManager.Instance;
        return tm != null ? tm.GetSkyHeightAt(x, leader.transform.position.y + 3f) : null;
    }

    // プレイヤーの近く(後ろ寄り)で、穴/穴の縁/天井/障害物/敵・ボスの攻撃の中でない所
    static readonly Collider2D[] probe = new Collider2D[16];
    public static float FindSafeX(PlayerController leader, CpuAlly a)
    {
        var tm = TerrainManager.Instance;
        float lx = leader.transform.position.x;
        if (tm == null) return lx;
        float[] offsets = { a.FollowOffset, a.FollowOffset - 1f, a.FollowOffset + 1f, -1f, -3f, -5f, -7f, -9f, -0.5f, 0f };
        foreach (float off in offsets)
        {
            float x = lx + off;
            if (x > tm.GeneratedEndX - 2f) continue;
            float gy;
            if (leader.AllyOnSky) { float? s = SkyAt(x, leader); if (!s.HasValue) continue; gy = s.Value; }
            else
            {
                if (!tm.TryGetGroundFast(x, out float h, out bool pit) || pit) continue;
                gy = h;
            }
            if (tm.IsNearPit(x, 1.2f)) continue;
            if (tm.HasCave)
            {
                float? lim = tm.GetCeilingLimitY(x);
                if (lim.HasValue && lim.Value < gy) continue;
                if (tm.IsCeilingSpikeHit(x, gy)) continue;
            }
            int n = Physics2D.OverlapBoxNonAlloc(new Vector2(x, gy + 0.7f), new Vector2(1.6f, 1.4f), 0f, probe);
            bool bad = false;
            for (int i = 0; i < n && !bad; i++)
            {
                var c = probe[i];
                if (c == null) continue;
                if (c.GetComponent<BossHitbox>() is BossHitbox bh && bh.IsActive) bad = true;
                else if (c.GetComponent<BossProjectile>() != null || c.GetComponent<EnemyMeleeHitbox>() != null || c.GetComponent<FireballController>() != null) bad = true;
                else if (c.GetComponent<CaveHazard>() is CaveHazard ch && ch.Damaging) bad = true;
                else if (c.GetComponent<SkyStrike>() is SkyStrike ss && ss.Damaging) bad = true;
                else if (c.GetComponentInParent<ObstacleController>() is ObstacleController oc && !oc.Broken) bad = true;
                else if (c.GetComponentInParent<EnemyController>() is EnemyController ec && !ec.IsDyingOrDead) bad = true;
                else if (c.GetComponentInParent<WildBossBase>() != null || c.GetComponentInParent<DragonController>() != null || c.GetComponentInParent<MajinController>() != null) bad = true;
            }
            if (bad) continue;
            return x;
        }
        return lx; // 見つからない: プレイヤーと同じ位置(プレイヤーが立てている所)
    }
}
