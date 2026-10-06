using System.Collections.Generic;
using UnityEngine;

// ボスの再戦プール(2026-10-02)。
//  ・そのラン・そのステージで「撃破した」ボスを登録する(距離に着いただけでは登録しない。死神は対象外)。
//  ・1,000m/5,000mの関門: その関門の本来のボス(荒野=狼/ウルフライダー、洞窟=ムカデ/サソリ、天空=ドラゴン/魔人)を
//    一度倒すまでは本来のボス(初登場)。倒した後は、プールから重み付きで抽選する(直近N体は外す。候補が無い時だけ緩める)。
//  ・10,000mごとの専用ボスは固定(抽選しない)。ラストダンジョン(三ステージ混成)は従来どおり。
//  ・抽選は関門の戦闘が始まる時(保留していた関門も、始まる時に抽選する)。HOSTだけが抽選し、JOINには種類が届く
//    (JOINはボスの関門の処理をしない: NetCombat.SuppressLocalBossSpawn)。
//  ・再戦のボスは今の距離に合わせて強くする(BossRematchTuning: HP/被弾量/崩し/間隔/段階)。
public partial class BossManager
{
    readonly List<string> defeatedPool = new List<string>();   // "Wild/Serpent" 等(撃破した順)
    readonly List<string> recentFought = new List<string>();   // 直近に戦った(新しい順)
    public IReadOnlyList<string> DefeatedPool => defeatedPool;
    public IReadOnlyList<string> RecentFought => recentFought;
    public string CurrentEncounterKey { get; private set; } = "";
    public bool CurrentEncounterIsRematch { get; private set; }
    public string CurrentRematchTier { get; private set; } = "";
    public float CurrentRematchHpMul { get; private set; } = 1f;
    public string LastRematchDecision { get; private set; } = "";
    public int RematchCount { get; private set; }
    public static float DamageMul { get; private set; } = 1f; // 再戦ボスのPlayerへの被弾量(戦闘中だけ)
    BossRematchTuning.Tier currentTier;

    // 被弾の量(ボス由来の攻撃が呼ぶ)。再戦中は段階の倍率を掛ける。
    public static int ScaleDamage(int amount) => DamageMul > 1.001f ? Mathf.Max(1, Mathf.RoundToInt(amount * DamageMul)) : amount;

    static string Key(GateFamily f, int kind) =>
        f == GateFamily.Cave ? "Cave/" + (CaveBossKind)kind : f == GateFamily.Sky ? "Sky/" + (SkyBossKind)kind : "Wild/" + (WildBossKind)kind;

    static bool ParseKey(string key, out GateFamily f, out int kind)
    {
        f = GateFamily.Wild; kind = 0;
        if (string.IsNullOrEmpty(key)) return false;
        int i = key.IndexOf('/');
        if (i <= 0) return false;
        string fam = key.Substring(0, i), name = key.Substring(i + 1);
        if (fam == "Cave" && System.Enum.TryParse(name, out CaveBossKind c)) { f = GateFamily.Cave; kind = (int)c; return true; }
        if (fam == "Sky" && System.Enum.TryParse(name, out SkyBossKind s)) { f = GateFamily.Sky; kind = (int)s; return true; }
        if (fam == "Wild" && System.Enum.TryParse(name, out WildBossKind w)) { f = GateFamily.Wild; kind = (int)w; return true; }
        return false;
    }

    GateFamily StageFamily => IsSkyStage ? GateFamily.Sky : IsCaveStage ? GateFamily.Cave : GateFamily.Wild;
    bool RematchStage => !IsLastStage && GameManager.Instance != null &&
        (GameManager.Instance.ActiveRunStageId == "wasteland_road" || IsCaveStage || IsSkyStage);

    // その種類が本来初めて出てくる距離(m)。再戦のHPの基準に使う
    float FirstDistanceOf(GateFamily f, int kind)
    {
        if (f == GateFamily.Wild)
        {
            var k = (WildBossKind)kind;
            if (k == WildBossKind.Wolf) return 1000f;
            if (k == WildBossKind.GoblinRider) return 5000f;
            int i = System.Array.IndexOf(TenKmBosses, k); return i >= 0 ? (i + 1) * 10000f : 10000f;
        }
        if (f == GateFamily.Cave)
        {
            var k = (CaveBossKind)kind;
            if (k == CaveBossKind.Centipede) return 1000f;
            if (k == CaveBossKind.Scorpion) return 5000f;
            int i = System.Array.IndexOf(CaveTenKmBosses, k); return i >= 0 ? (i + 1) * 10000f : 10000f;
        }
        {
            var k = (SkyBossKind)kind;
            if (k == SkyBossKind.Dragon) return 1000f;
            if (k == SkyBossKind.Majin) return 5000f;
            int i = System.Array.IndexOf(SkyTenKmBosses, k); return i >= 0 ? (i + 1) * 10000f : 10000f;
        }
    }

    // 関門の種類を決める。本来の予定(Resolve*)の結果を受け取り、再戦にするならkind/countを書き換える。
    void DecideEncounter(GateFamily family, ref int kind, ref int count)
    {
        CurrentEncounterIsRematch = false; CurrentRematchTier = ""; CurrentRematchHpMul = 1f; DamageMul = 1f; currentTier = null;
        int k = gateK;
        string native = Key(family, kind);
        if (!RematchStage || k % 10 == 0 || RushGateK != 0)
        {
            LastRematchDecision = k % 10 == 0 ? $"{k}km: 専用ボス {native}(固定)" : $"{k}km: {native}";
            BeginEncounterKey(native, false);
            return;
        }
        if (!defeatedPool.Contains(native))
        {
            LastRematchDecision = $"{k}km: 本来のボス {native}(まだ倒していない=初登場)";
            BeginEncounterKey(native, false);
            return;
        }
        bool fiveK = k % 5 == 0;
        string pick = Draw(fiveK, out string why);
        // プールがまだ小さく、直前の除外を外してやっと本来のボスしか残らなかった時は、従来どおりの本来のボス(再戦扱いにしない)
        if (pick == native && lastDrawExclude == 0) { why += " → 本来のボスのまま"; pick = null; }
        if (pick == null || !ParseKey(pick, out GateFamily pf, out int pk) || pf != family)
        {
            LastRematchDecision = $"{k}km: 候補なし → 本来のボス {native}({why})";
            BeginEncounterKey(native, false);
            return;
        }
        kind = pk;
        count = 1; // 再戦は1体(距離に合わせて強い1体)
        float d = k * gateIntervalMeters;
        var tn = BossRematchTuning.I;
        currentTier = tn.TierAt(d);
        float distMul = tn.distanceHp ? Mathf.Max(1f, BossHpPlan.MilestoneHpAt(d) / Mathf.Max(1f, BossHpPlan.MilestoneHpAt(FirstDistanceOf(pf, pk)))) : 1f;
        CurrentRematchHpMul = distMul * Mathf.Max(0.1f, currentTier.hpMul);
        CurrentRematchTier = currentTier.label;
        DamageMul = Mathf.Max(0.1f, currentTier.damageMul);
        CurrentEncounterIsRematch = true;
        RematchCount++;
        LastRematchDecision = $"{k}km: 再戦 {pick}({currentTier.label} HP×{CurrentRematchHpMul:0.0} 被弾×{DamageMul:0.0}、{why})";
        BeginEncounterKey(pick, true);
    }

    void BeginEncounterKey(string key, bool rematch)
    {
        CurrentEncounterKey = key;
        recentFought.Remove(key);
        recentFought.Insert(0, key);
        ProgressStats.MarkBossSeen(key); // 会ったボス(ラスダンの抽選で製品版が優先する)
        while (recentFought.Count > 6) recentFought.RemoveAt(recentFought.Count - 1);
        Debug.Log($"[BossPool] {LastRematchDecision} pool=[{string.Join(", ", defeatedPool)}]");
    }

    int lastDrawExclude = -1;
    // 重み付き抽選(HOST/シングルだけが呼ぶ)
    string Draw(bool fiveK, out string why)
    {
        var tn = BossRematchTuning.I;
        // 今回の関門で直近に入る前の並び(BeginEncounterKeyはまだ呼んでいない)。
        // 除外はプールに2体以上の候補が残る範囲まで(小さいプールで「狼→騎兵→蛇→狼…」の決まった順番にならないように)。
        int startEx = Mathf.Min(Mathf.Max(0, tn.recentExclude), Mathf.Max(1, defeatedPool.Count - 2));
        lastDrawExclude = -1;
        for (int exclude = startEx; exclude >= 0; exclude--)
        {
            var skip = new HashSet<string>();
            for (int i = 0; i < Mathf.Min(exclude, recentFought.Count); i++) skip.Add(recentFought[i]);
            float total = 0f;
            var cands = new List<(string key, float w)>();
            foreach (var key in defeatedPool)
            {
                if (skip.Contains(key)) continue;
                float w = tn.WeightFor(key, fiveK);
                if (w <= 0f) continue;
                cands.Add((key, w)); total += w;
            }
            if (cands.Count == 0) continue;
            lastDrawExclude = exclude;
            float r = Random.value * total;
            foreach (var c in cands) { r -= c.w; if (r <= 0f) { why = $"候補{cands.Count}体/直近{exclude}体除外"; return c.key; } }
            why = $"候補{cands.Count}体/直近{exclude}体除外"; return cands[cands.Count - 1].key;
        }
        why = "プールが空";
        return null;
    }

    // 戦闘が終わった(関門のボスを全部倒した): プールへ登録
    void RegisterEncounterDefeated()
    {
        if (string.IsNullOrEmpty(CurrentEncounterKey) || !RematchStage) { ResetRematchEncounter(); return; }
        if (!defeatedPool.Contains(CurrentEncounterKey))
        {
            defeatedPool.Add(CurrentEncounterKey);
            Debug.Log($"[BossPool] + {CurrentEncounterKey} (pool {defeatedPool.Count}: {string.Join(", ", defeatedPool)})");
        }
        ResetRematchEncounter();
    }

    void ResetRematchEncounter() { DamageMul = 1f; currentTier = null; CurrentEncounterIsRematch = false; }

    // 再戦の強化をボスへ(段階/必殺技の間隔/崩し。荒野街道のように戦闘の調整値を持つボスだけ)
    void ApplyRematchTo(WildBossBase boss)
    {
        TrackSpawned(boss); // 遭遇へ登録 + ラスダンの攻撃間隔/移動速度(2026-10-05)
        if (boss == null || !CurrentEncounterIsRematch || currentTier == null) return;
        boss.ApplyRematch(currentTier);
    }
    float RematchHpScaleOr(float normalScale) => CurrentEncounterIsRematch ? CurrentRematchHpMul : normalScale;

    // 疾走出発(2026-10-05): 飛ばした関門(1〜lastK)の本来のボスを、このランの再戦プールへ(通常に走った時と同じ状態にする)。
    // このランの中だけの記録(永続の撃破記録/会ったボスの記録には付けない)。ラスダンは対象外(節目はプールの抽選)。
    public void SprintMarkSkippedGates(int lastK)
    {
        if (IsLastStage || !RematchStage) return;
        int added = 0;
        for (int k = 1; k <= lastK; k++)
        {
            string key = null;
            if (IsSkyStage) { if (ResolveSkyGate(k, out var s, out _)) key = Key(GateFamily.Sky, (int)s); }
            else if (IsCaveStage) { if (ResolveCaveGate(k, out var c, out _)) key = Key(GateFamily.Cave, (int)c); }
            else if (ResolveGate(k, out var w, out _)) key = Key(GateFamily.Wild, (int)w);
            if (key != null && !defeatedPool.Contains(key)) { defeatedPool.Add(key); added++; }
        }
        Debug.Log($"[Sprint] rematch pool +{added} from skipped gates 1-{lastK} ({defeatedPool.Count})");
    }

    // ---- 中断中のラン(CONTINUE) ----
    public void ExportPool(RunCheckpoint.Data d)
    {
        d.bossPoolVersion = 1;
        d.defeatedBosses = new List<string>(defeatedPool);
        d.recentBosses = new List<string>(recentFought);
    }
    public void ImportPool(RunCheckpoint.Data d, float checkpointDistance)
    {
        defeatedPool.Clear(); recentFought.Clear();
        if (d != null && d.bossPoolVersion >= 1)
        {
            if (d.defeatedBosses != null) foreach (var k in d.defeatedBosses) if (ParseKey(k, out _, out _) && !defeatedPool.Contains(k)) defeatedPool.Add(k);
            if (d.recentBosses != null) foreach (var k in d.recentBosses) if (ParseKey(k, out _, out _)) recentFought.Add(k);
            return;
        }
        // この仕組みより前の中断データ: 通過した関門の本来のボスを倒した扱いにする(再開してすぐ全部「初登場」に戻らないように)
        if (!RematchStage) return;
        var fam = StageFamily;
        for (int k = 1; k * gateIntervalMeters <= checkpointDistance; k++)
        {
            int kind = 0, count = 0; bool ok;
            if (fam == GateFamily.Sky) { ok = ResolveSkyGate(k, out var s, out count); kind = (int)s; }
            else if (fam == GateFamily.Cave) { ok = ResolveCaveGate(k, out var c, out count); kind = (int)c; }
            else { ok = ResolveGate(k, out var w, out count); kind = (int)w; }
            string key = Key(fam, kind);
            if (ok && !defeatedPool.Contains(key)) defeatedPool.Add(key);
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // ---- 開発用 ----
    public void DebugPoolReset() { defeatedPool.Clear(); recentFought.Clear(); LastRematchDecision = "pool reset"; }
    public void DebugUnlockAll()
    {
        var fam = StageFamily;
        if (fam == GateFamily.Sky) foreach (SkyBossKind s in System.Enum.GetValues(typeof(SkyBossKind))) { string k = Key(fam, (int)s); if (!defeatedPool.Contains(k)) defeatedPool.Add(k); }
        else if (fam == GateFamily.Cave) foreach (CaveBossKind c in System.Enum.GetValues(typeof(CaveBossKind))) { string k = Key(fam, (int)c); if (!defeatedPool.Contains(k)) defeatedPool.Add(k); }
        else foreach (WildBossKind w in System.Enum.GetValues(typeof(WildBossKind))) { string k = Key(fam, (int)w); if (!defeatedPool.Contains(k)) defeatedPool.Add(k); }
        LastRematchDecision = "unlock all";
    }
    public void DebugUnlock(string key) { if (ParseKey(key, out _, out _) && !defeatedPool.Contains(key)) defeatedPool.Add(key); }
    public void DebugSetRecentExclude(int n) { BossRematchTuning.I.recentExclude = Mathf.Clamp(n, 0, 5); }
    // 次の関門をすぐ始める(その手前へワープ)
    public bool DebugStartNextGateNow()
    {
        if (IsBossPhase || GameManager.Instance == null) return false;
        nextGateNotBefore = 0f;
        float target = CurrentTargetDistance();
        if (GameManager.Instance.MaxDistance < target - 30f) GameManager.Instance.DebugWarpToDistance(target - 25f);
        return true;
    }
#endif
}
