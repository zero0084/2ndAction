using UnityEngine;

// Watches run distance and triggers a boss checkpoint every bossRepeatInterval
// starting from the very first one, freezing regular enemy spawning and
// spawning however many dragons/majins that checkpoint calls for. The
// counts are derived purely from the checkpoint's index rather than
// tracked incrementally, so the schedule always matches:
//
//   checkpoint 1 (1000m): dragon x1
//   checkpoint 2 (2000m): dragon x2
//   checkpoint 3 (3000m): dragon x3
//   checkpoint 4 (4000m): dragon x4
//   checkpoint 5 (5000m): majin x1 (dragon count wraps back to 0)
//   checkpoint 6 (6000m): majin x1, dragon x1
//   checkpoint 7 (7000m): majin x1, dragon x2
//   checkpoint 9 (9000m): majin x1, dragon x4
//   checkpoint 10 (10000m): majin x2 (dragon count wraps back to 0)
//   checkpoint 11 (11000m): majin x2, dragon x1
//   ...
//
// i.e. majinCount = checkpointIndex / majinCycleLength (integer division),
// dragonCount = checkpointIndex % majinCycleLength.
public partial class BossManager : MonoBehaviour
{
    public static BossManager Instance { get; private set; }

    public Transform player;
    public Sprite squareSprite;
    public Sprite[] dragonIdleFrames;
    public Sprite[] dragonChargeFrames;
    public Sprite[] dragonFireFrames;
    public Sprite[] majinIdleFrames;
    public Sprite[] majinAttackFrames;

    // Boss Defeat Presentation pass - shared between Dragon/Majin (see
    // DragonController/MajinController's own finalHitSparkSprite/
    // bossDeathSmokeSprite fields, which these get copied into at spawn).
    public Sprite bossHitSparkSprite;
    public Sprite bossDeathSmokeSprite;

    // ボス撃破時の飛散パーティクル色(2026-09-10) - マスター指定「ドラゴン=
    // 赤、機械龍=黄、魔人=紫」。各Spawn*でController.defeatBurstColorへ
    // コピーする。雑魚敵の青はEnemyController側の既定値。
    public Color dragonDefeatBurstColor = new Color(1f, 0.3f, 0.18f, 1f);
    public Color mechanicalDragonDefeatBurstColor = new Color(1f, 0.82f, 0.15f, 1f);
    public Color majinDefeatBurstColor = new Color(0.7f, 0.35f, 1f, 1f);
    // No dedicated files yet - PlaySfx is null-safe, so these can stay
    // unassigned without breaking anything (see SceneBuilder).
    public AudioClip bossFinalHitSe;
    public AudioClip bossDefeatSe;

    // Distance Level Design Ver.1, item 7 - Mechanical Dragon. Reuses
    // DragonController entirely (entrance/HP/hit/death Presentation all
    // identical - see DragonController.attacksEnabled) with attacks
    // disabled, per "具体的な攻撃追加は後で設計" / "出現確認用の最低限
    // Placeholder Behaviorでも構いません". null-safe - never spawns if the
    // sprite hasn't been imported yet.
    public Sprite mechanicalDragonSprite;
    public float mechanicalDragonUnlockDistance = 20000f;
    public int mechanicalDragonMaxHp = 300;
    // Distance Level Design Ver.1.1 - best-guess default; flip in the
    // Inspector if it still faces the wrong way once seen in Game View.
    public bool mechanicalDragonDefaultFacingRight = true;
    public bool deathDefaultFacingRight = true;

    // Distance Level Design Ver.1, item 8 - Death/Grim Reaper. Explicitly
    // NOT a fight - no collider, no damage, no chase, no HP - "戦闘仕様・
    // 無敵仕様・追跡速度などは後で設計" per the brief; this is only the
    // "100,000m検知 -> Death出現 -> ゲーム継続" confirmation the brief asks
    // for this pass, nothing more. null-safe - never spawns without art.
    public Sprite deathSprite;
    public float deathSpawnDistance = 100000f;
    public float deathSpawnBehindPlayer = 6f;
    bool deathSpawned;

    public float bossRepeatInterval = 1000f;
    // Every this-many checkpoints, the majin count steps up by one and the
    // dragon count wraps back to 0 (see the table above).
    public int majinCycleLength = 5;

    // Bugfix 2026-09-06 - "Boss→現状より明確に巨大であること". Dragon/
    // MechanicalDragon/Death (GrimReaper) share this single scale field -
    // no separate Visual child exists for any of them (SpriteRenderer/
    // Collider/Rigidbody all live on the same Transform, see SpawnDragon's
    // own comment), so this is the one existing lever for their size and it
    // scales the Collider right along with the Visual, same as it always
    // has - not a new side effect introduced here.
    public float dragonScale = 2.3f;
    public float dragonStandoffDistance = 8f;
    public float dragonSpacing = 4f;
    public float dragonScatterJitter = 2f;
    public int dragonMaxHp = 200;

    // Majin sits further back than the dragons by default (dragonStandoffDistance
    // is its own zero point) so a mixed encounter reads as two distinct
    // depths rather than everything piled on the same line.
    public float majinScale = 2.7f; // Bugfix 2026-09-06 - see dragonScale's own comment
    public float majinStandoffDistance = 16f;
    public float majinSpacing = 6f;
    public float majinScatterJitter = 2.5f;
    // maxHp is set per-encounter as dragonMaxHp * majinHpMultiplier rather
    // than a fixed field, so tuning the dragon's HP keeps the two in sync.
    public float majinHpMultiplier = 6f;

    // Safety net for the scatter: caps the nearest boss's distance so at
    // least one of each type is always visible the moment the encounter
    // starts, regardless of how the random jitter/shuffle played out.
    public float maxGuaranteedVisibleDistance = 16f;

    // ===== 荒野街道ボス追加(2026-09-20) =====
    // 荒野街道の固定スケジュール(距離→ボス)。true(既定)なら従来の
    // 「1000mごとにドラゴン/魔人」の周期ロジックの代わりにこの表で出現する
    // (従来ロジックのコードは残してあり、falseで復帰できる)。
    // (2026-09-26) Debug Modeでの距離短縮(0.2倍)は廃止 - 距離ワープボタンで代替する。
    // 100,000mの死神は従来どおり別系統(SpawnDeath、Gateにはならない)。
    public bool useWildSchedule = true;
    [System.Serializable]
    public class WildBossArt
    {
        public WildBossKind kind;
        public Sprite idle, move, windup, attack;
    }
    public WildBossArt[] wildArt;

    // ===== ボス遭遇スケジュール(1000m周期の階層構造) =====
    // 優先順位: 10,000m単位の専用大型ボス > 5,000m単位のゴブリン・ウルフライダー > 1,000m単位の巨大オオカミ。
    // 10,000m単位は同じ地点で下位のボスを重複出現させない。100,000mは死神(SpawnDeath)なのでゲートを作らない。
    public float gateIntervalMeters = 1000f;
    public int wolfMaxCount = 4;               // 巨大オオカミの最大同時出現数
    public float wolfCountStepMeters = 12000f; // この距離ごとに1体増える
    public int riderMaxCount = 3;              // ウルフライダーの最大同時出現数
    public float riderCountStepMeters = 20000f;
    public float smallBossHpPerKm = 0.03f;     // 距離に応じた雑魚ボスHP倍率(1+km*この値、上限3倍)
    static readonly WildBossKind[] TenKmBosses =
    {
        WildBossKind.Serpent, WildBossKind.Cyclops, WildBossKind.Spider, WildBossKind.Golem, WildBossKind.Griffin,
        WildBossKind.Hydra, WildBossKind.Demon, WildBossKind.Dragon, WildBossKind.BlackKnight,
    };
    int gateK = 1;               // 次に来るゲートは gateK * gateIntervalMeters(m)
    int currentGateK = 1;
    int aliveWildThisEncounter;

    // ===== 自然洞窟ボス追加(2026-09-22) =====
    // 荒野街道(WildBossKind/ResolveGate/SpecFor/SpawnWild)をそのまま流用し、
    // ステージが自然洞窟の間だけこちらの表を使う並行実装。ゲート進行(gateK/
    // currentGateK/aliveWildThisEncounter/100,000mの死神)自体はステージに
    // 依存しない共通の仕組みなのでそのまま共有し、「kが来たときにどのボスを
    // 出すか/どう生成するか」だけをステージで分岐する。
    bool IsCaveStage => GameManager.Instance != null && GameManager.Instance.ActiveRunStageId == "natural_cave";

    [System.Serializable]
    public class CaveBossArt
    {
        public CaveBossKind kind;
        public Sprite idle, move, windup, attack;
    }
    public CaveBossArt[] caveArt;

    public int centipedeMaxCount = 4;               // 巨大ムカデの最大同時出現数
    public float centipedeCountStepMeters = 12000f;
    public int scorpionCaveMaxCount = 3;            // 巨大サソリの最大同時出現数
    public float scorpionCaveCountStepMeters = 20000f;

    static readonly CaveBossKind[] CaveTenKmBosses =
    {
        CaveBossKind.Mole, CaveBossKind.Troll, CaveBossKind.Worm, CaveBossKind.CrystalGolem, CaveBossKind.Bat,
        CaveBossKind.ScorpionKing, CaveBossKind.Basilisk, CaveBossKind.Drake, CaveBossKind.AncientDemon,
    };

    // 荒野街道のResolveGateと全く同じ形(優先順位: 10,000m専用大型 > 5,000m系
    // > 1,000m系。同じkで複数種類が重複することはif/else-ifの構造上起きない)。
    bool ResolveCaveGate(int k, out CaveBossKind kind, out int count)
    {
        float meters = k * gateIntervalMeters;
        count = 1;
        if (k % 10 == 0)
        {
            int idx = (k / 10 - 1) % 10;
            if (idx >= CaveTenKmBosses.Length) { kind = CaveBossKind.Centipede; return false; }
            kind = CaveTenKmBosses[idx];
            return true;
        }
        if (k % 5 == 0)
        {
            kind = CaveBossKind.Scorpion;
            count = Mathf.Clamp(1 + Mathf.FloorToInt(meters / Mathf.Max(1f, scorpionCaveCountStepMeters)), 1, Mathf.Max(1, scorpionCaveMaxCount));
            return true;
        }
        kind = CaveBossKind.Centipede;
        count = Mathf.Clamp(1 + Mathf.FloorToInt(meters / Mathf.Max(1f, centipedeCountStepMeters)), 1, Mathf.Max(1, centipedeMaxCount));
        return true;
    }

    // k番目のゲート(k*1000m)に出すボス。falseなら通常ボスのゲートは無い(100,000mの死神など)。
    bool ResolveGate(int k, out WildBossKind kind, out int count)
    {
        float meters = k * gateIntervalMeters;
        count = 1;
        if (k % 10 == 0)
        {
            int idx = (k / 10 - 1) % 10;
            if (idx >= TenKmBosses.Length) { kind = WildBossKind.Wolf; return false; }
            kind = TenKmBosses[idx];
            return true;
        }
        if (k % 5 == 0)
        {
            kind = WildBossKind.GoblinRider;
            count = Mathf.Clamp(1 + Mathf.FloorToInt(meters / Mathf.Max(1f, riderCountStepMeters)), 1, Mathf.Max(1, riderMaxCount));
            return true;
        }
        kind = WildBossKind.Wolf;
        count = Mathf.Clamp(1 + Mathf.FloorToInt(meters / Mathf.Max(1f, wolfCountStepMeters)), 1, Mathf.Max(1, wolfMaxCount));
        return true;
    }

    // ===== 天空回廊ボス追加(2026-09-25) =====
    // 荒野街道/自然洞窟と同じゲート進行を共有し、天空回廊の間だけこの表を使う。
    //   10,000m単位: 専用大型ボス(ベヒーモス〜天界の守護者) > 5,000m単位: 魔人 > 1,000m単位: ドラゴン
    // ドラゴン/魔人は既存のDragonController/MajinController(天空回廊の元々のボス)をそのまま使う。
    bool IsSkyStage => GameManager.Instance != null && GameManager.Instance.ActiveRunStageId == "sky_corridor";

    [System.Serializable]
    public class SkyBossArt
    {
        public SkyBossKind kind;
        public Sprite idle, move, windup, attack;
    }
    public SkyBossArt[] skyArt;

    public int skyDragonMaxCount = 4;               // ドラゴンの最大同時出現数
    public float skyDragonCountStepMeters = 12000f; // この距離ごとに1体増える
    public int skyMajinMaxCount = 3;                // 魔人の最大同時出現数
    public float skyMajinCountStepMeters = 10000f;  // 15,000m以降で2体、25,000m以降で3体
    public float skyDragonLandingFromMeters = 3000f; // この距離以降のドラゴンは着地噛みつきも使う

    static readonly SkyBossKind[] SkyTenKmBosses =
    {
        SkyBossKind.Behemoth, SkyBossKind.Titan, SkyBossKind.Jellyfish, SkyBossKind.Leviathan, SkyBossKind.Fenrir,
        SkyBossKind.SkyGolem, SkyBossKind.Phoenix, SkyBossKind.SkySerpent, SkyBossKind.Guardian,
    };

    bool ResolveSkyGate(int k, out SkyBossKind kind, out int count)
    {
        float meters = k * gateIntervalMeters;
        count = 1;
        if (k % 10 == 0)
        {
            int idx = (k / 10 - 1) % 10;
            if (idx >= SkyTenKmBosses.Length) { kind = SkyBossKind.Dragon; return false; } // 100,000m = 死神
            kind = SkyTenKmBosses[idx];
            return true;
        }
        if (k % 5 == 0)
        {
            kind = SkyBossKind.Majin;
            count = Mathf.Clamp(1 + Mathf.FloorToInt((meters - 5000f) / Mathf.Max(1f, skyMajinCountStepMeters)), 1, Mathf.Max(1, skyMajinMaxCount));
            return true;
        }
        kind = SkyBossKind.Dragon;
        count = Mathf.Clamp(1 + Mathf.FloorToInt(meters / Mathf.Max(1f, skyDragonCountStepMeters)), 1, Mathf.Max(1, skyDragonMaxCount));
        return true;
    }

    // ===== LAST CORRIDOR(ラストダンジョン候補、2026-09-29) =====
    // 専用ボスは作らず、3ステージの既存ボスを交互に出す(再登場・高い段階のボスも出る)。
    //   1,000m単位: ウルフ(荒野) / 巨大ムカデ(洞窟) / ドラゴン(天空)を順番に
    //   5,000m単位: ゴブリンライダー / 巨大サソリ / 魔人を順番に
    //   10,000m単位: 3ステージの大型ボスから選んだ9体(下の表)。100,000mは死神(正式なラスボス/エンディングは無し)
    // 体数は各ステージの計算式(距離が進むほど増える)をそのまま使う。
    public const string LastStageId = "last_corridor";

    // ===== ラストダンジョン(2026-09-30): ボスラッシュ(90,000〜99,000m)と100,000mの三姉妹戦 =====
    // RushEnabled(LastDungeonFlowがシングルプレイのラストダンジョンだけtrueにする)の間、k=90〜98の関門は
    // 下の表のボスラッシュになり、k=99以降の通常の関門は作らない(99,000〜100,000mは静寂区間)。
    // 1つの関門に複数の家族(荒野/洞窟/天空)のボスを混ぜ、「同時」「時間差で追加」「前のボスの撃破直後に次が登場」を使う。
    // 各関門の撃破後は通常どおりのボス報酬(カード選択)→少し走って次の関門。
    public static bool RushEnabled;
    public static bool SuppressGates;
    public static System.Action FinaleAt100k;
    public const int RushFirstK = 90, RushLastK = 98;
    public enum RushEntry { Now, Delayed, Chain }
    public struct RushBoss
    {
        public int family; public int kind; public RushEntry entry; public float delay;
        public RushBoss(int f, int k, RushEntry e = RushEntry.Now, float d = 0f) { family = f; kind = k; entry = e; delay = d; }
    }
    static RushBoss W(WildBossKind k, RushEntry e = RushEntry.Now, float d = 0f) => new RushBoss(0, (int)k, e, d);
    static RushBoss C(CaveBossKind k, RushEntry e = RushEntry.Now, float d = 0f) => new RushBoss(1, (int)k, e, d);
    static RushBoss S(SkyBossKind k, RushEntry e = RushEntry.Now, float d = 0f) => new RushBoss(2, (int)k, e, d);
    // 90k〜98k。前半は1体ずつ(各ステージの代表)、後半ほど複数/時間差/連続。画面が破綻しないよう同時に出るのは最大2体。
    public static readonly RushBoss[][] RushTable =
    {
        new[] { W(WildBossKind.Serpent), C(CaveBossKind.Mole, RushEntry.Chain) },                                  // 90k 荒野→洞窟の連続
        new[] { S(SkyBossKind.Behemoth) },                                                                           // 91k 天空
        new[] { W(WildBossKind.Cyclops), C(CaveBossKind.Troll, RushEntry.Delayed, 7f) },                             // 92k 巨人2体(時間差)
        new[] { C(CaveBossKind.CrystalGolem), S(SkyBossKind.SkyGolem, RushEntry.Chain) },                           // 93k ゴーレムの連続
        new[] { S(SkyBossKind.Fenrir), W(WildBossKind.Griffin) },                                                    // 94k 2体同時
        new[] { W(WildBossKind.Hydra), C(CaveBossKind.Drake, RushEntry.Chain) },                                     // 95k
        new[] { S(SkyBossKind.SkySerpent), W(WildBossKind.Demon, RushEntry.Delayed, 6f) },                           // 96k 時間差
        new[] { C(CaveBossKind.AncientDemon), S(SkyBossKind.Guardian) },                                             // 97k 2体同時
        new[] { W(WildBossKind.BlackKnight), S(SkyBossKind.Phoenix, RushEntry.Chain), C(CaveBossKind.Basilisk, RushEntry.Chain) }, // 98k 最後の3連戦
    };
    public int RushGateK { get; private set; }         // 今のボスラッシュの関門(0=ボスラッシュではない)
    public int RushSpawnedThisGate { get; private set; }
    public int RushMaxSimultaneous { get; private set; }
    readonly System.Collections.Generic.Queue<RushBoss> rushChain = new System.Collections.Generic.Queue<RushBoss>();
    int rushPending;       // これから出る(時間差/連続)ボスの数(aliveWildに含めて、途中で戦闘が終わらないようにする)
    int rushSlot;
    public static string RushGateLabel(int k)
    {
        if (k < RushFirstK || k > RushLastK) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var b in RushTable[k - RushFirstK])
        {
            if (sb.Length > 0) sb.Append(b.entry == RushEntry.Now ? " + " : b.entry == RushEntry.Delayed ? $" +({b.delay:F0}s) " : " -> ");
            sb.Append(b.family == 0 ? ((WildBossKind)b.kind).ToString() : b.family == 1 ? ((CaveBossKind)b.kind).ToString() : ((SkyBossKind)b.kind).ToString());
        }
        return sb.ToString();
    }

    void StartRushGate(int k)
    {
        var list = RushTable[k - RushFirstK];
        RushGateK = k;
        RushSpawnedThisGate = 0;
        Debug.Log($"[BossRush] Gate {k * 1000}m Start: {RushGateLabel(k)}");
        RushMaxSimultaneous = 0;
        rushChain.Clear();
        rushPending = 0;
        rushSlot = 0;
        aliveWildThisEncounter = list.Length; // 全員(後から出る分も含む)を倒すまで関門は終わらない
        // ボス曲は全関門「特殊」(10,000m級)の系統
        BossMusicTier = BossBgmTier.Special;
        string stage = GameManager.Instance != null ? GameManager.Instance.ActiveRunStageId : "";
        BossMusicKey = $"{stage}/Rush{k}";
        BossDefeatedThisPhase = false;
        foreach (var b in list)
        {
            if (b.entry == RushEntry.Now) SpawnRushBoss(b);
            else if (b.entry == RushEntry.Delayed) { rushPending++; StartCoroutine(RushDelayed(b, k)); }
            else { rushPending++; rushChain.Enqueue(b); }
        }
        if (GameManager.Instance != null) GameManager.Instance.LogBoss("CombatStart");
        Debug.Log($"[Boss][Rush] gate {k * 1000}m: {RushGateLabel(k)}");
    }

    System.Collections.IEnumerator RushDelayed(RushBoss b, int k)
    {
        float t = 0f;
        while (t < b.delay) { if (RushGateK != k || !IsBossPhase) yield break; t += Time.deltaTime; yield return null; }
        if (RushGateK != k || !IsBossPhase) yield break;
        rushPending--;
        SpawnRushBoss(b);
    }

    void SpawnRushBoss(RushBoss b)
    {
        int slot = rushSlot++ % 2; // 同時に並ぶのは最大2体分の間合い
        if (b.family == 1) SpawnCaveBoss((CaveBossKind)b.kind, slot);
        else if (b.family == 2) SpawnSkyBoss((SkyBossKind)b.kind, slot);
        else SpawnWild((WildBossKind)b.kind, slot);
        RushSpawnedThisGate++;
        int alive = 0;
        foreach (var w in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (w != null && !w.IsDead && w.gameObject.activeInHierarchy) alive++;
        RushMaxSimultaneous = Mathf.Max(RushMaxSimultaneous, alive);
        Debug.Log($"[Boss][Rush] spawn {(b.family == 0 ? ((WildBossKind)b.kind).ToString() : b.family == 1 ? ((CaveBossKind)b.kind).ToString() : ((SkyBossKind)b.kind).ToString())} ({b.entry}) alive={alive}");
        Debug.Log($"[BossRush] Boss {RushSpawnedThisGate} Spawn (gate {RushGateK * 1000}m, alive {alive})");
    }

    // 台本のボス戦(100,000mの三姉妹): 通常の関門と同じく距離を止め、雑魚/障害物を止め、ボス曲にする。
    public void BeginScriptedBossPhase(BossBgmTier tier, string musicKey)
    {
        IsBossPhase = true;
        BossMusicTier = tier;
        BossMusicKey = musicKey;
        BossDefeatedThisPhase = false;
        if (GameManager.Instance != null) { GameManager.Instance.ClampMaxDistanceTo(GameManager.Instance.MaxDistance); GameManager.Instance.BeginBossDistanceExclusion(); }
        if (TerrainManager.Instance != null) TerrainManager.Instance.ClearAllEnemies();
    }
    public void MarkScriptedBossDefeated() { BossDefeatedThisPhase = true; }
    public void EndScriptedBossPhase()
    {
        EndBossPhase();
        if (GameManager.Instance != null) GameManager.Instance.EndBossDistanceExclusion();
    }
    bool IsLastStage => GameManager.Instance != null && GameManager.Instance.ActiveRunStageId == LastStageId;
    enum GateFamily { Wild, Cave, Sky }
    struct LastBoss { public GateFamily family; public int kind; public LastBoss(GateFamily f, int k) { family = f; kind = k; } }
    static readonly LastBoss[] LastTenKmBosses =
    {
        new LastBoss(GateFamily.Wild, (int)WildBossKind.Golem),
        new LastBoss(GateFamily.Cave, (int)CaveBossKind.CrystalGolem),
        // 30km(崩壊。床の下が深い断面)は飛ぶフェニックス、60km(奈落。床が細い橋)は下からせり上がるタイタン
        // (タイタンは床の後ろから現れる演出なので、断面の深い区間では体のほとんどが断面に隠れて見えなかった: 2026-09-30)
        new LastBoss(GateFamily.Sky, (int)SkyBossKind.Phoenix),
        new LastBoss(GateFamily.Wild, (int)WildBossKind.Hydra),
        new LastBoss(GateFamily.Cave, (int)CaveBossKind.Basilisk),
        new LastBoss(GateFamily.Sky, (int)SkyBossKind.Titan),
        new LastBoss(GateFamily.Wild, (int)WildBossKind.BlackKnight),
        new LastBoss(GateFamily.Cave, (int)CaveBossKind.AncientDemon),
        new LastBoss(GateFamily.Sky, (int)SkyBossKind.Guardian),
    };

    bool ResolveLastGate(int k, out GateFamily family, out int kind, out int count)
    {
        count = 1; kind = 0;
        if (RushEnabled && k >= RushFirstK)
        {
            family = GateFamily.Wild;
            return k <= RushLastK; // 90〜98k=ボスラッシュ(StartRushGateが出す)、99k以降=関門なし(静寂区間→三姉妹)
        }
        if (k % 10 == 0)
        {
            int idx = k / 10 - 1;
            family = GateFamily.Wild;
            if (idx < 0 || idx >= LastTenKmBosses.Length) return false; // 100,000m = 死神
            family = LastTenKmBosses[idx].family; kind = LastTenKmBosses[idx].kind;
            return true;
        }
        family = (GateFamily)((k % 5 == 0 ? k / 5 - 1 : k - 1) % 3);
        switch (family)
        {
            case GateFamily.Cave: { bool ok = ResolveCaveGate(k, out var c, out count); kind = (int)c; return ok; }
            case GateFamily.Sky: { bool ok = ResolveSkyGate(k, out var s, out count); kind = (int)s; return ok; }
            default: { bool ok = ResolveGate(k, out var w, out count); kind = (int)w; return ok; }
        }
    }

    void SkipEmptyGates()
    {
        int guard = 0;
        if (IsLastStage)
        {
            while (guard++ < 20 && !ResolveLastGate(gateK, out _, out _, out _)) gateK++;
        }
        else if (IsSkyStage)
        {
            while (guard++ < 20 && !ResolveSkyGate(gateK, out _, out _)) gateK++;
        }
        else if (IsCaveStage)
        {
            while (guard++ < 20 && !ResolveCaveGate(gateK, out _, out _)) gateK++;
        }
        else
        {
            while (guard++ < 20 && !ResolveGate(gateK, out _, out _)) gateK++;
        }
    }

    float WildTargetDistance()
    {
        return gateK * gateIntervalMeters;
    }

    float CurrentTargetDistance() => useWildSchedule ? WildTargetDistance() : nextBossDistance;

    public bool IsBossPhase { get; private set; }
    // Bugfix 2026-09-06, item "Boss戦中Distance停止" - called by GameManager
    // exactly where Boss Reward processing actually finishes (SaveCheckpoint
    // time), NOT at the boss's own death - see CheckEncounterComplete's own
    // comment for why IsBossPhase itself no longer flips false there.
    public void EndBossPhase()
    {
        IsBossPhase = false; BossMusicKey = null; BossDefeatedThisPhase = false;
        ResetRematchEncounter();
        if (RunResumed) Debug.Log($"[BossRun] BossPhaseEnd after resumed run (d={(GameManager.Instance != null ? GameManager.Instance.MaxDistance : 0f):F0})");
        RunResumed = false; encounterResumable = false;
        if (pendingChosen) { nextGateNotBefore = Time.time + Mathf.Max(0f, BossBattleTuning.I.pendingSafeDelay); pendingChosen = false; }
    }

    // ===================================================================== //
    // ボス戦の強化(2026-10-01): 時間内に倒せなければラン再開 / ボスが残る間は次のボスを保留
    // ===================================================================== //
    // IsBossPhase … ボスの遭遇が続いている(=次の距離のボスを始めない)。ラン再開後もボスが残る限りtrue。
    // HoldsRun    … ランを止めている(距離が進まない/雑魚/障害物を出さない)。ラン再開でfalseになる。
    // SpawnsHeld  … 雑魚の出現を止めている(ラン再開から zakoResumeDelay 秒は止めたまま)。
    public bool RunResumed { get; private set; }
    public bool HoldsRun => IsBossPhase && !RunResumed;
    public bool SpawnsHeld => IsBossPhase && (!RunResumed || Time.time - resumedAt < BossBattleTuning.I.zakoResumeDelay);
    public float EncounterSeconds => encounterTimer;
    public float ResumeSecondsTotal => resumeSeconds;
    public float ResumeSecondsLeft => Mathf.Max(0f, resumeSeconds - encounterTimer);
    public bool ResumeCountdownVisible => encounterResumable && IsBossPhase && !RunResumed && !BossDefeatedThisPhase && AliveBossCount > 0 && encounterTimer > 0f && ResumeSecondsLeft <= 5f;
    public int ResumeCount { get; private set; }
    public int PendingSkippedCount { get; private set; }
    public string LastGateDecision { get; private set; } = "";
    public int CurrentGateIndex => currentGateK;
    public int NextGateIndex => gateK;
    bool encounterResumable, pendingChosen;
    float encounterTimer, resumeSeconds, resumedAt, nextGateNotBefore;
    int catchUpFloorK;
    string encounterKey = "";

    bool StageUsesBattle => GameManager.Instance != null && System.Array.IndexOf(BossBattleTuning.I.resumeStages, GameManager.Instance.ActiveRunStageId) >= 0;

    void BeginEncounterClock(string key)
    {
        encounterKey = key;
        encounterResumable = StageUsesBattle && RushGateK == 0;
        RunResumed = false;
        encounterTimer = -2.2f; // 登場の演出のぶん(ボスへ集中できる時間は「倒すまでの秒数」に含めない)
        var tn = BossBattleTuning.I;
        var e = tn.For(key);
        int k = currentGateK;
        resumeSeconds = e.resumeSecondsOverride > 0f ? e.resumeSecondsOverride : k % 10 == 0 ? tn.resumeSpecial : k % 5 == 0 ? tn.resumeStrong : tn.resumeNormal;
        Debug.Log($"[BossRun] EncounterStart key={key} gate={k * gateIntervalMeters:F0}m resumeAfter={(encounterResumable ? resumeSeconds.ToString("F0") + "s" : "off")}");
    }

    void TickResume()
    {
        if (!IsBossPhase || !encounterResumable || RunResumed || BossDefeatedThisPhase) return;
        if (AliveBossCount <= 0) return;
        // 撃破の演出中(倒れている最中)は数えない: 倒した直後にラン再開にならないように
        if (!BossBattle.AnyBossFighting && AliveMajinCount <= 0) return;
        encounterTimer += Time.deltaTime;
        if (encounterTimer >= resumeSeconds) ResumeRun();
    }

    public void ResumeRun()
    {
        if (RunResumed || !IsBossPhase) return;
        RunResumed = true;
        resumedAt = Time.time;
        ResumeCount++;
        if (GameManager.Instance != null) GameManager.Instance.ResumeBossDistance();
        Debug.Log($"[BossRun] RunResumed key={encounterKey} after {encounterTimer:F1}s alive={AliveBossCount} d={(GameManager.Instance != null ? GameManager.Instance.MaxDistance : 0f):F0}");
        BossBattleHud.Banner("ラン再開! ボスはまだ追ってくる", new Color(1f, 0.55f, 0.35f), BossBattleTuning.I.resumeBannerSeconds);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossWarning);
    }

    // 遭遇の終わり(撃破)に次の関門を決める。ラン再開中に通過した関門は「1つだけ保留して後で出す」(またはすべて飛ばす)。
    // 同じ関門を二重に出さない/保留を積み上げない: 保留を選んだら、その時点までに通過した関門はすべて済み扱い(catchUpFloorK)。
    void ChooseNextGate()
    {
        int baseNext = currentGateK + 1;
        float d = GameManager.Instance != null ? GameManager.Instance.MaxDistance : 0f;
        int passedMax = Mathf.FloorToInt((d + 0.5f) / Mathf.Max(1f, gateIntervalMeters));
        int from = Mathf.Max(baseNext, catchUpFloorK);
        if (passedMax >= from)
        {
            int best = -1, bestTier = -1;
            for (int k = from; k <= passedMax; k++)
            {
                if (!GateExists(k)) continue;
                int tier = k % 10 == 0 ? 2 : k % 5 == 0 ? 1 : 0;
                if (tier >= bestTier) { bestTier = tier; best = k; }
            }
            catchUpFloorK = passedMax + 1;
            if (best > 0 && BossBattleTuning.I.pendingMode == BossPendingMode.PendingOne)
            {
                PendingSkippedCount += Mathf.Max(0, passedMax - from); // 選ばれなかった関門
                gateK = best;
                pendingChosen = true;
                LastGateDecision = $"pending {best * gateIntervalMeters:F0}m (passed {from * gateIntervalMeters:F0}-{passedMax * gateIntervalMeters:F0}m while the boss was alive)";
            }
            else
            {
                PendingSkippedCount += passedMax - from + 1;
                gateK = passedMax + 1;
                LastGateDecision = $"skipped {from * gateIntervalMeters:F0}-{passedMax * gateIntervalMeters:F0}m";
            }
        }
        else
        {
            gateK = Mathf.Max(baseNext, catchUpFloorK);
            LastGateDecision = $"next {gateK * gateIntervalMeters:F0}m";
        }
        SkipEmptyGates();
        Debug.Log($"[BossRun] NextGate {LastGateDecision} -> gate {gateK * gateIntervalMeters:F0}m (d={d:F0})");
    }

    bool GateExists(int k)
    {
        if (IsLastStage) return ResolveLastGate(k, out _, out _, out _);
        if (IsSkyStage) return ResolveSkyGate(k, out _, out _);
        if (IsCaveStage) return ResolveCaveGate(k, out _, out _);
        return ResolveGate(k, out _, out _);
    }

    // ===== BGM用(2026-09-29): 今のボス戦の曲の系統と、撃破済みか =====
    // 1000mごと=通常 / 5000mごと=強敵 / 10000mごと=特殊。キーは「ステージ/ボスの種類」(個別曲の上書きに使う)。
    public BossBgmTier BossMusicTier { get; private set; }
    public string BossMusicKey { get; private set; }        // 戦闘が始まるまではnull(警告演出の間は道中曲のまま)
    public bool BossDefeatedThisPhase { get; private set; } // 撃破〜報酬選択の間(道中曲へ戻す)
    public bool DeathSpawned => deathSpawned;              // 100,000mの死神(専用曲)
    void SetBossMusic(int k, string kindName)
    {
        BossMusicTier = k % 10 == 0 ? BossBgmTier.Special : k % 5 == 0 ? BossBgmTier.Strong : BossBgmTier.Normal;
        string stage = GameManager.Instance != null ? GameManager.Instance.ActiveRunStageId : "";
        BossMusicKey = $"{stage}/{kindName}";
        BossDefeatedThisPhase = false;
    }
    public int BossesDefeated { get; private set; }
    public float NextBossDistance => CurrentTargetDistance();
    // Bugfix 2026-09-08 (Bug #001 診断フェーズ) - read-only surface for
    // BossDiagnostics' Freeze Snapshot ("AliveBossCount"/"CurrentBossCount").
    public int AliveDragonCount => Mathf.Max(0, aliveDragonsThisEncounter);
    public int AliveMajinCount => Mathf.Max(0, aliveMajinsThisEncounter);
    public int AliveWildCount => Mathf.Max(0, aliveWildThisEncounter);
    public int AliveBossCount => AliveDragonCount + AliveMajinCount + AliveWildCount;

    float nextBossDistance;
    int aliveDragonsThisEncounter;
    int aliveMajinsThisEncounter;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        nextBossDistance = EffectiveStartDistance();
    }

    float EffectiveRepeatInterval()
    {
        return bossRepeatInterval;
    }

    // First encounter is checkpoint 1 (one interval in), matching the
    // original dragon-only pacing the player is used to - not
    // majinCycleLength intervals in, which skipped every encounter before
    // the first majin appeared (checkpoints 1-4 never triggered at all).
    float EffectiveStartDistance()
    {
        return EffectiveRepeatInterval();
    }

    void Update()
    {
        if (GameManager.Instance == null || !GameManager.Instance.HasStarted || GameManager.Instance.IsGameOver) return;
        // マルチプレイPhase 2 - JOIN側はボスを自分で出現させない(HOSTが出現を確定して共有する)。
        if (NetCombat.SuppressLocalBossSpawn) return;

#if UNITY_EDITOR
        // 動作確認用(Editor専用): F1〜F11で荒野街道ボスを即時出現(順序は
        // WildBossKind: Wolf, GoblinRider, Serpent, Cyclops, Spider, Golem,
        // Griffin, Hydra, Demon, Dragon, BlackKnight)。実機/ビルドには含まれない。
        // 自然洞窟ボス追加(2026-09-22) - F1〜F11は荒野街道の11種で既に埋まって
        // いるため、Shift+F1〜F11で自然洞窟ボス(CaveBossKind: Centipede,
        // Scorpion, Mole, Troll, Worm, CrystalGolem, Bat, ScorpionKing,
        // Basilisk, Drake, AncientDemon)を即時出現させる(Shift併用時は荒野
        // 街道側を発火させない)。
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        // 天空回廊ボス追加(2026-09-25) - Ctrl+F1〜F11で天空回廊ボス(SkyBossKind: Dragon, Majin,
        // Behemoth, Titan, Jellyfish, Leviathan, Fenrir, SkyGolem, Phoenix, SkySerpent, Guardian)。
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        for (int k = 0; k < 11; k++)
        {
            if (!Input.GetKeyDown(KeyCode.F1 + k)) continue;
            if (ctrl) DebugForceSpawnSky((SkyBossKind)k);
            else if (shift) DebugForceSpawnCave((CaveBossKind)k);
            else DebugForceSpawn((WildBossKind)k);
        }
        if (Input.GetKeyDown(KeyCode.Backspace)) { foreach (var wb in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) wb.TakeDamage(99999, wb.CenterWorld); }
        if (Input.GetKeyDown(KeyCode.F12)) { GameManager.Instance.DebugSetInvincible(true); }
        if (Input.GetKeyDown(KeyCode.M)) { if (ctrl) DebugForceSpawnSky(SkyBossKind.Dragon, 4); else if (shift) DebugForceSpawnCave(CaveBossKind.Centipede, 4); else DebugForceSpawn(WildBossKind.Wolf, 4); }        // 複数体確認
        if (Input.GetKeyDown(KeyCode.N)) { if (ctrl) DebugForceSpawnSky(SkyBossKind.Majin, 3); else if (shift) DebugForceSpawnCave(CaveBossKind.Scorpion, 3); else DebugForceSpawn(WildBossKind.GoblinRider, 3); } // 複数体確認
        if (Input.GetKeyDown(KeyCode.R)) SpawnDeath();                                 // 死神
#endif

        // Distance Level Design Ver.1, item 8 - "100,000m検知 -> Death出現
        // -> ゲーム継続". Deliberately checked BEFORE the IsBossPhase guard
        // below (and independent of the 1000m checkpoint cycle entirely) -
        // Death is a one-time event that should still fire even if a
        // regular boss encounter happens to be in progress right at
        // 100,000m, and must never block or get blocked by it. Does NOT
        // end/pause the run - "100,000m到達でRunを強制終了しない".
        if (!deathSpawned && GameManager.Instance.MaxDistance >= deathSpawnDistance)
        {
            deathSpawned = true;
            if (GameManager.Instance.DebugMode) Debug.Log($"[Distance] {Mathf.RoundToInt(deathSpawnDistance)} reached");
            // ラストダンジョン(2026-09-30): 追跡の死神ではなく、三姉妹との正式な戦闘(LastDungeonFlow)
            if (FinaleAt100k != null) FinaleAt100k();
            else SpawnDeath();
        }

        TickResume();
        if (IsBossPhase) return;
        if (Time.time < nextGateNotBefore) return; // 保留していたボスは、前のボスの報酬の後に少し空けてから
        if (SuppressGates) return; // ラストダンジョンの静寂区間〜エンディング: 通常のボスの関門を作らない
        // BONUS ZONE(2026-09-29): 区画の最中はボスを始めない(BonusZoneが次のボスの手前で自分から終わる)
        if (BonusZone.Instance != null && BonusZone.Instance.BlocksBoss) return;
        if (player == null || dragonIdleFrames == null || dragonIdleFrames.Length == 0) return;

        float targetDistance = CurrentTargetDistance();
        float gateDistance = GateDistance();
        if (gateDistance >= targetDistance)
        {
            // Reserved immediately (not inside StartBossPhase any more) so
            // this Update() guard (top of the method) stops re-triggering
            // on the very next frame while the Presentation pass is still
            // playing - MaxDistance stays >= nextBossDistance continuously
            // until the encounter actually ends, well after this moment.
            IsBossPhase = true;
            if (GameManager.Instance != null) GameManager.Instance.LogBoss("PhaseStart");

            // Distance Level Design Ver.1.1, item 1 - Boss Gate locks here:
            // snap Distance to EXACTLY this checkpoint (not whatever
            // fractional overshoot triggered this frame) - see
            // GameManager.ReportDistance's own early-return, which is what
            // actually keeps it frozen at this value for the rest of the
            // fight.
            if (GameManager.Instance != null)
            {
                // ボス戦の強化(2026-10-01): 保留していた関門(すでに通り過ぎている)では距離を関門まで戻さない
                float md = GameManager.Instance.MaxDistance;
                if (md >= targetDistance - 0.5f && md - targetDistance < 30f) GameManager.Instance.ClampMaxDistanceTo(targetDistance);
                else if (md < targetDistance) Debug.Log($"[BossRun] Gate {targetDistance:F0}m reached by the front player P{WorldRange.WorldFrontPlayer} (front={gateDistance:F0}m, HOST={md:F0}m) - HOST distance not moved");
                else Debug.Log($"[BossRun] PendingGateStart {targetDistance:F0}m at d={md:F0}");
                // Bugfix 2026-09-06, item "Boss中Distanceの根本修正" - marks
                // the exact moment raw Player movement stops counting
                // toward Distance (see GameManager.BeginBossDistanceExclusion's
                // own comment for why this is needed on top of the
                // IsBossPhase freeze above).
                GameManager.Instance.BeginBossDistanceExclusion();
            }

            // Boss Milestone Presentation pass - wraps the existing
            // StartBossPhase (untouched below) as a callback; falls back to
            // calling it directly if the scene was built before this
            // manager existed, so a boss can never fail to spawn.
            int checkpointIndexForPresentation = Mathf.RoundToInt(targetDistance / EffectiveRepeatInterval());
            bool isFirstEncounter = useWildSchedule ? gateK == 1 : checkpointIndexForPresentation <= 1;
            if (BossMilestonePresentation.Instance != null)
            {
                BossMilestonePresentation.Instance.Play(targetDistance, isFirstEncounter, StartBossPhase);
            }
            else
            {
                StartBossPhase();
            }
        }
    }

    // ===== マルチ Phase 3.1: ボスの関門と登場位置は「走っている全員の最前」(WorldFront)基準 =====
    // HOSTがカード選択/DOWN/後方でも、最前の人が関門に着けばボスが始まる(出すのは今まで通りHOST)。
    // シングル/JOINは今まで通り自分の距離。
    float GateDistance()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) return 0f;
        if (!NetCombat.Authority || !WorldRange.HasAlive) return gm.MaxDistance;
        return Mathf.Max(gm.MaxDistance, WorldRange.WorldFrontDistance);
    }

    // ボスの登場位置の基準: マルチのHOSTは最前で走っている人(カード選択中でない)、それ以外は自分
    Transform SpawnRef()
    {
        if (!NetCombat.Authority || !WorldRange.HasAlive) return player;
        var f = WorldRange.FrontRunning();
        return f.T != null ? f.T : player;
    }

    // ボス戦の開始で雑魚を片付ける: シングルは全部(従来どおり)。マルチは「ボスが出る辺り」(最前の人の画面の少し後ろから先)だけ。
    // 後ろの人がこれから戦うはずの雑魚まで消さない。
    void ClearEnemiesForBoss()
    {
        TerrainManager tm = TerrainManager.Instance;
        if (tm == null) return;
        if (NetCombat.Authority && WorldRange.ActiveCount > 1) tm.ClearEnemiesFrom(SpawnRef().position.x - 30f);
        else tm.ClearAllEnemies();
    }

    void StartBossPhase()
    {
        IsBossPhase = true; // idempotent - already set above when the presentation exists
        SetBossMusic(1, "Legacy"); // 旧スケジュール用の既定(荒野/洞窟/天空の各ゲートはStartWildPhaseで種類ごとに上書き)

        if (useWildSchedule)
        {
            StartWildPhase();
            return;
        }

        int checkpointIndex = Mathf.RoundToInt(nextBossDistance / EffectiveRepeatInterval());
        int majinCount = checkpointIndex / majinCycleLength;
        int dragonCount = checkpointIndex % majinCycleLength;

        aliveMajinsThisEncounter = majinCount;
        aliveDragonsThisEncounter = dragonCount;

        // Auto-run stays on during the fight; each boss tracks the player's
        // base auto-run speed every frame so it holds a constant distance
        // while running, and only an attack lunge/recoil actually changes
        // the gap.
        ClearEnemiesForBoss();

        float[] dragonDistances = BuildScatteredDistances(dragonCount, dragonStandoffDistance, dragonSpacing, dragonScatterJitter);
        for (int i = 0; i < dragonCount; i++)
        {
            SpawnDragon(dragonDistances[i]);
        }

        float[] majinDistances = BuildScatteredDistances(majinCount, majinStandoffDistance, majinSpacing, majinScatterJitter);
        for (int i = 0; i < majinCount; i++)
        {
            SpawnMajin(majinDistances[i]);
        }

        // Distance Level Design Ver.1, item 7 - "20,000m前後 Mechanical
        // Dragon初登場" / "20,000〜99,999m Dragon/魔人/Mechanical Dragonを
        // 距離に応じて使用". Added ON TOP of the existing dragon/majin
        // count formula above (untouched) rather than replacing any of it -
        // every encounter from mechanicalDragonUnlockDistance onward also
        // gets exactly one, in addition to whatever the original formula
        // already produces.
        if (nextBossDistance >= mechanicalDragonUnlockDistance && mechanicalDragonSprite != null)
        {
            aliveDragonsThisEncounter++;
            SpawnMechanicalDragon(dragonStandoffDistance + dragonSpacing * (dragonCount + majinCount + 1));
        }

        if (GameManager.Instance != null) GameManager.Instance.LogBoss("CombatStart");
    }

    // Lays out one evenly-spaced "slot" per boss so they can never end up
    // stacked on the exact same spot, jitters each slot, then shuffles the
    // assignment so the scatter doesn't read as "closest one always spawns
    // first".
    float[] BuildScatteredDistances(int count, float baseStandoff, float spacing, float jitter)
    {
        if (count <= 0) return new float[0];

        float[] distances = new float[count];
        for (int i = 0; i < count; i++)
        {
            float slotCenter = baseStandoff + i * spacing;
            distances[i] = Mathf.Max(2f, slotCenter + Random.Range(-jitter, jitter));
        }

        int nearestIndex = 0;
        for (int i = 1; i < distances.Length; i++)
        {
            if (distances[i] < distances[nearestIndex]) nearestIndex = i;
        }
        if (distances[nearestIndex] > maxGuaranteedVisibleDistance)
        {
            distances[nearestIndex] = baseStandoff;
        }

        for (int i = distances.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (distances[i], distances[j]) = (distances[j], distances[i]);
        }

        return distances;
    }

    // configure: Init直前の追加設定(天空回廊のドラゴン用、2026-09-25追加。既存呼び出しはnull=従来どおり)。
    // マルチプレイPhase 2 - 共有ボスの出現記録(HOST)/パペットの再構築(JOIN)用の入口。
    void SpawnDragon(float standoffDistanceForThisDragon, System.Action<DragonController> configure = null) { NetCombat.BeginBossSpawn(NetCombat.BossMethod.Dragon, 0, 0, standoffDistanceForThisDragon); try { SpawnDragonImpl(standoffDistanceForThisDragon, configure); } finally { NetCombat.EndBossSpawn(); } }

    void SpawnDragonImpl(float standoffDistanceForThisDragon, System.Action<DragonController> configure = null)
    {
        GameObject go = new GameObject("Dragon");
        go.tag = "Boss";
        go.transform.localScale = Vector3.one * dragonScale;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = RenderOrder.Boss;

        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(2.2f, 2.0f);
        var colDebug = go.AddComponent<ColliderDebugView>();
        colDebug.color = new Color(1f, 0.4f, 0f);

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        DragonController dragon = go.AddComponent<DragonController>();
        dragon.idleFrames = dragonIdleFrames;
        dragon.chargeFrames = dragonChargeFrames;
        dragon.fireFrames = dragonFireFrames;
        dragon.squareSprite = squareSprite;
        dragon.maxHp = EffectiveBossMaxHp(dragonMaxHp);
        dragon.standoffDistance = standoffDistanceForThisDragon;
        dragon.finalHitSparkSprite = bossHitSparkSprite;
        dragon.bossDeathSmokeSprite = bossDeathSmokeSprite;
        dragon.finalHitSe = AudioManager.Se(SeId.BossFinalHit, bossFinalHitSe);
        dragon.bossDefeatSe = AudioManager.Se(SeId.BossDefeat, bossDefeatSe);
        dragon.defeatBurstColor = dragonDefeatBurstColor; // 撃破パーティクル=赤

        // Bugfix 2026-09-05, item 6 - "Boss/EnemyがPlayer方向を向かない"
        // covered every OTHER species already (see EnemyFacing.cs's own
        // comment listing what it's applied to), but the real Dragon boss
        // never actually had this component attached at all - unlike
        // Mechanical Dragon below, which already does. Same reasoning as
        // that one: no separate Visual child (SpriteRenderer/Collider share
        // this Transform), col.size has no offset so flipping localScale.x
        // is safe, alwaysFacePlayer=true since the boss's own forward drift
        // isn't a meaningful walking direction. dragonIdleFrames' art
        // (idle_00.png) faces/bites LEFT natively, so defaultFacingRight=
        // false.
        var dragonFacing = go.AddComponent<EnemyFacing>();
        dragonFacing.visual = go.transform;
        dragonFacing.defaultFacingRight = false;
        dragonFacing.alwaysFacePlayer = true;
        dragonFacing.player = player;

        configure?.Invoke(dragon);
        dragon.Init(SpawnRef());
    }

    // Distance Level Design Ver.1 - visually distinct (Mechanical Dragon's
    // own art) but behaviorally a placeholder: idleFrames/chargeFrames/
    // fireFrames are all just a single-element array of the one static
    // sprite provided (DragonController's frame-cycling code handles a
    // length-1 array fine - it just never changes), and attacksEnabled is
    // false, so it appears, can be hit/killed (full Boss Defeat
    // Presentation included), but never attacks.
    // マルチプレイPhase 2 - 共有ボスの出現記録(HOST)/パペットの再構築(JOIN)用の入口。
    void SpawnMechanicalDragon(float standoffDistanceForThisDragon) { NetCombat.BeginBossSpawn(NetCombat.BossMethod.MechDragon, 0, 0, standoffDistanceForThisDragon); try { SpawnMechanicalDragonImpl(standoffDistanceForThisDragon); } finally { NetCombat.EndBossSpawn(); } }

    void SpawnMechanicalDragonImpl(float standoffDistanceForThisDragon)
    {
        GameObject go = new GameObject("MechanicalDragon");
        go.tag = "Boss";
        go.transform.localScale = Vector3.one * dragonScale;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = RenderOrder.Boss;

        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(2.2f, 2.0f);
        var colDebug = go.AddComponent<ColliderDebugView>();
        colDebug.color = new Color(0.2f, 0.6f, 1f);

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        DragonController dragon = go.AddComponent<DragonController>();
        Sprite[] singleFrame = { mechanicalDragonSprite };
        dragon.idleFrames = singleFrame;
        dragon.chargeFrames = singleFrame;
        dragon.fireFrames = singleFrame;
        dragon.squareSprite = squareSprite;
        dragon.maxHp = EffectiveBossMaxHp(mechanicalDragonMaxHp);
        dragon.standoffDistance = standoffDistanceForThisDragon;
        dragon.attacksEnabled = false;
        // Reward/MILE System Ver.1 - "ボスMILE: Mechanical Dragon 200".
        dragon.mileReward = 200;
        dragon.finalHitSparkSprite = bossHitSparkSprite;
        dragon.bossDeathSmokeSprite = bossDeathSmokeSprite;
        dragon.finalHitSe = AudioManager.Se(SeId.BossFinalHit, bossFinalHitSe);
        dragon.bossDefeatSe = AudioManager.Se(SeId.BossDefeat, bossDefeatSe);
        dragon.defeatBurstColor = mechanicalDragonDefeatBurstColor; // 撃破パーティクル=黄

        // Distance Level Design Ver.1.1 - facing fix. No separate Visual
        // child here (DragonController's SpriteRenderer/Collider share this
        // same Transform) - flipping localScale.x's sign is still safe
        // since col.size has no offset (a symmetric box looks identical
        // either way). alwaysFacePlayer=true since a boss's own forward
        // drift (AdvanceTrackedX, matching the player's auto-run pace) is
        // not a meaningful walking direction to face toward.
        var facing = go.AddComponent<EnemyFacing>();
        facing.visual = go.transform;
        facing.defaultFacingRight = mechanicalDragonDefaultFacingRight;
        facing.alwaysFacePlayer = true;
        facing.player = player;

        dragon.Init(SpawnRef());
        if (GameManager.Instance != null && GameManager.Instance.DebugMode) Debug.Log("[Boss] MechanicalDragon Spawn");
    }

    // Distance Level Design Ver.1, item 8 - see GrimReaperController's own
    // comment for exactly what this does/doesn't do. Positioned a fixed
    // distance BEHIND the player (not ahead, not on top of them) so it
    // reads as "something arrived from behind" without ever risking
    // overlapping the player or any terrain/enemy at the moment it spawns.
    // マルチプレイPhase 2 - 共有ボスの出現記録(HOST)/パペットの再構築(JOIN)用の入口。
    // デバッグ/自動テスト用: 100,000m到達と同じ死神の開始(BGMの切り替えも同じフラグ)をその場で起こす。
    public void DebugSpawnReaper() { if (deathSpawned) return; deathSpawned = true; SpawnDeath(); }
    public void DebugMarkDeathSpawned() { deathSpawned = true; } // 開発版のエンドロール/ONE MORE MILE?から始める時: 100,000mの三姉妹戦/死神を出さない

    void SpawnDeath() { NetCombat.BeginBossSpawn(NetCombat.BossMethod.Reaper); try { SpawnDeathImpl(); } finally { NetCombat.EndBossSpawn(); } }

    void SpawnDeathImpl()
    {
        if (player == null) return;
        // 死神三姉妹(2026-09-29): ステージの担当(荒野街道=長女/自然洞窟=次女/天空回廊=三女)を、画面左端の外から出す。
        // マルチのJOINでは同じステージIDで同じ姉妹のパペットが作られる(NetCombat.BossMethod.Reaper)。
        string stage = GameManager.Instance != null ? GameManager.Instance.ActiveRunStageId : "";
        Vector3 pos = player.position + new Vector3(-deathSpawnBehindPlayer * 4f, 0f, 0f);
        ReaperBase.Spawn(stage, player, deathSprite, pos);
        if (GameManager.Instance != null && GameManager.Instance.DebugMode) Debug.Log("[Boss] Death Spawn");
    }

    // マルチプレイPhase 2 - 共有ボスの出現記録(HOST)/パペットの再構築(JOIN)用の入口。
    void SpawnMajin(float standoffDistanceForThisMajin, System.Action<MajinController> configure = null) { NetCombat.BeginBossSpawn(NetCombat.BossMethod.Majin, 0, 0, standoffDistanceForThisMajin); try { SpawnMajinImpl(standoffDistanceForThisMajin, configure); } finally { NetCombat.EndBossSpawn(); } }

    void SpawnMajinImpl(float standoffDistanceForThisMajin, System.Action<MajinController> configure = null)
    {
        GameObject go = new GameObject("Majin");
        go.tag = "Boss";
        go.transform.localScale = Vector3.one * majinScale;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = RenderOrder.Boss;

        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(2.4f, 2.6f);
        var colDebug = go.AddComponent<ColliderDebugView>();
        colDebug.color = new Color(0.6f, 0.1f, 0.7f);

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        MajinController majin = go.AddComponent<MajinController>();
        majin.idleFrames = majinIdleFrames;
        majin.attackFrames = majinAttackFrames;
        majin.squareSprite = squareSprite;
        majin.maxHp = EffectiveBossMaxHp(Mathf.RoundToInt(dragonMaxHp * majinHpMultiplier));
        majin.standoffDistance = standoffDistanceForThisMajin;
        majin.finalHitSparkSprite = bossHitSparkSprite;
        majin.bossDeathSmokeSprite = bossDeathSmokeSprite;
        majin.finalHitSe = AudioManager.Se(SeId.BossFinalHit, bossFinalHitSe);
        majin.bossDefeatSe = AudioManager.Se(SeId.BossDefeat, bossDefeatSe);
        majin.defeatBurstColor = majinDefeatBurstColor; // 撃破パーティクル=紫

        // Bugfix 2026-09-05, item 6 - same reasoning as SpawnDragon's
        // matching block above. majinIdleFrames' art is a roughly
        // front-facing symmetric pose (no strong left/right lean), so
        // defaultFacingRight's exact value barely changes the visual either
        // way here - kept at the class default (true) rather than guessing
        // a lean that isn't really there.
        var majinFacing = go.AddComponent<EnemyFacing>();
        majinFacing.visual = go.transform;
        majinFacing.alwaysFacePlayer = true;
        majinFacing.player = player;

        configure?.Invoke(majin);
        majin.Init(SpawnRef());
    }

    // Only ends the boss phase once every dragon AND every majin spawned
    // this encounter is down. Does NOT end the game - the run just
    // continues.
    public void OnDragonDefeated()
    {
        BossesDefeated++;
        aliveDragonsThisEncounter--;
        CheckEncounterComplete();
    }

    public void OnMajinDefeated()
    {
        BossesDefeated++;
        aliveMajinsThisEncounter--;
        CheckEncounterComplete();
    }

    void CheckEncounterComplete()
    {
        if (aliveDragonsThisEncounter <= 0 && aliveMajinsThisEncounter <= 0 && aliveWildThisEncounter <= 0)
        {
            BossDefeatedThisPhase = true; // BGM: 撃破したら道中曲へ戻す
            RegisterEncounterDefeated(); // 再戦プールへ(2026-10-02)
            // 自然洞窟ボス拡張(2026-09-22) - StartWildPhaseで設定したボス
            // 遭遇区間の戦闘可能スペース保証を、遭遇終了時に必ず解除する。
            if (TerrainManager.Instance != null && TerrainManager.Instance.cave != null)
            {
                TerrainManager.Instance.cave.ClearBossClearZone();
            }

            // Bugfix 2026-09-06, item 2 - "Boss撃破後にゲームが停止する"
            // state-transition trace, stage 1/9.
            if (GameManager.Instance != null) GameManager.Instance.LogBossRewardStage("BossDefeated (CheckEncounterComplete entry)");
            if (GameManager.Instance != null) GameManager.Instance.LogBoss("Defeated");

            // Boss Defeat Presentation pass - captured BEFORE nextBossDistance
            // advances, so the Clear text reports the checkpoint that was
            // just cleared (1000m/2000m/...), not the next one.
            float clearedDistance = CurrentTargetDistance();
            int checkpointIndex = Mathf.RoundToInt(clearedDistance / EffectiveRepeatInterval());
            bool isFirstEncounter = useWildSchedule ? currentGateK == 1 : checkpointIndex <= 1;

            // Bugfix 2026-09-06, item "Boss戦中Distance停止" - IsBossPhase
            // used to flip false right here, at the exact moment the last
            // boss dies - but GameManager.ReportDistance's Boss Gate guard
            // reads this SAME flag, so Distance (and Enemy Wall spawning,
            // and TerrainManager's safe-terrain suppression) all silently
            // resumed the instant the boss died, well before Boss Defeat
            // Presentation even started - let alone before the Boss Reward
            // card choice below actually finished. The brief's own flow is
            // "撃破 -> Defeat演出 -> Reward -> Gameplay復帰 -> Distance再開",
            // so this now stays true straight through both of those and
            // only clears via EndBossPhase(), called from GameManager at
            // the exact point Boss Reward processing itself completes
            // (SaveCheckpoint time - see ApplyUpgradeByCardId/
            // RunBossRewardChoice).
            if (useWildSchedule) ChooseNextGate();
            else nextBossDistance += EffectiveRepeatInterval();

            // Presentation only - fires once the encounter's LAST boss has
            // finished its own individual death presentation (see
            // DragonController/MajinController.FinalHitAndDie, which is
            // what actually calls OnDragonDefeated/OnMajinDefeated above,
            // now at the END of that short sequence instead of instantly).
            // Falls back to nothing (not an error) if the scene was built
            // before this manager existed - IsBossPhase/nextBossDistance
            // above are already fully updated regardless.
            if (BossDefeatPresentation.Instance != null)
            {
                BossDefeatPresentation.Instance.Play(clearedDistance, isFirstEncounter);
            }
            if (GameManager.Instance != null) GameManager.Instance.LogBossRewardStage("BossDefeatPresentation.Play returned (stage 2/9)");

            // Run Continuation/Checkpoint Ver.1, item 6/7 - "Boss撃破 ->
            // Boss Defeat Presentation -> BOSS REWARD". Reuses the exact
            // same Presentation-priority deferral Level Up already has (see
            // GameManager.TriggerBossRewardChoice) - it waits for the
            // BossDefeatPresentation banner just started above to finish
            // before actually pausing/showing cards, so the two never
            // visually fight each other.
            if (GameManager.Instance != null) GameManager.Instance.TriggerBossRewardChoice();
        }
    }

    // Run Continuation/Checkpoint Ver.1, item 7 - called once by
    // GameManager.BeginContinuedRun right after MaxDistance is set to the
    // Checkpoint's distance, so the Boss schedule resumes exactly where it
    // left off instead of re-initializing to EffectiveStartDistance() (which
    // would otherwise immediately misfire a Boss Gate at the player's actual,
    // far-ahead position). checkpointDistance is always itself a boss-clear
    // point (SaveCheckpoint only ever runs right after one), so the next one
    // due is simply one interval further - no need to re-derive
    // majinCount/dragonCount separately; StartBossPhase already re-derives
    // those purely from nextBossDistance/EffectiveRepeatInterval() the next
    // time a Boss Gate actually triggers.
    public void RestoreNextBossDistance(float checkpointDistance)
    {
        IsBossPhase = false;
        RunResumed = false; encounterResumable = false; pendingChosen = false; catchUpFloorK = 0; nextGateNotBefore = 0f; // ボス戦の強化: 再開/ワープで保留を持ち越さない
        nextBossDistance = checkpointDistance + EffectiveRepeatInterval();

        // 荒野街道スケジュール: チェックポイント距離より先の最初のエントリへ。
        gateK = Mathf.Max(1, Mathf.FloorToInt((checkpointDistance + 1f) / Mathf.Max(1f, gateIntervalMeters)) + 1);
        SkipEmptyGates();
    }

    // ===== 荒野街道ボス(WildBossBase系) / 自然洞窟ボス =====
    // ボス戦の強化(2026-10-01)を使うステージ(まず荒野街道)。洞窟/天空はBossBattleTuningのentriesとresumeStagesで広げる。
    public static string[] BattleTunedStages = { "wasteland_road" };
    bool BattleTunedStage => GameManager.Instance != null && System.Array.IndexOf(BattleTunedStages, GameManager.Instance.ActiveRunStageId) >= 0;

    void StartWildPhase()
    {
        SkipEmptyGates();
        currentGateK = gateK;

        aliveDragonsThisEncounter = 0;
        aliveMajinsThisEncounter = 0;
        aliveWildThisEncounter = 0;
        ClearEnemiesForBoss();

        if (IsLastStage)
        {
            ResetRematchEncounter(); CurrentEncounterKey = "";
            if (RushEnabled && gateK >= RushFirstK && gateK <= RushLastK) { StartRushGate(gateK); return; }
            if (!ResolveLastGate(gateK, out GateFamily fam, out int lastKind, out int lastCount)) { IsBossPhase = false; return; }
            if (fam == GateFamily.Sky) StartSkyGate((SkyBossKind)lastKind, lastCount);
            else if (fam == GateFamily.Cave) StartCaveGate((CaveBossKind)lastKind, lastCount);
            else StartWildGate((WildBossKind)lastKind, lastCount);
            return;
        }

        if (IsSkyStage)
        {
            if (!ResolveSkyGate(gateK, out SkyBossKind skyKind, out int skyCount)) { IsBossPhase = false; return; }
            int sk = (int)skyKind; DecideEncounter(GateFamily.Sky, ref sk, ref skyCount); skyKind = (SkyBossKind)sk; // 再戦の抽選(2026-10-02)
            StartSkyGate(skyKind, skyCount);
            return;
        }

        if (IsCaveStage)
        {
            if (!ResolveCaveGate(gateK, out CaveBossKind caveKind, out int caveCount)) { IsBossPhase = false; return; }
            int ck = (int)caveKind; DecideEncounter(GateFamily.Cave, ref ck, ref caveCount); caveKind = (CaveBossKind)ck; // 再戦の抽選(2026-10-02)
            StartCaveGate(caveKind, caveCount);
            return;
        }

        if (!ResolveGate(gateK, out WildBossKind gateKind, out int gateCount)) { IsBossPhase = false; return; }
        int wk = (int)gateKind; DecideEncounter(GateFamily.Wild, ref wk, ref gateCount); gateKind = (WildBossKind)wk; // 再戦の抽選(2026-10-02)
        StartWildGate(gateKind, gateCount);
    }

    void StartSkyGate(SkyBossKind skyKind, int skyCount)
    {
        {
            SetBossMusic(gateK, skyKind.ToString());
            BeginEncounterClock(skyKind.ToString());
            StartSkyEncounter(skyKind, skyCount);
            if (GameManager.Instance != null)
            {
                GameManager.Instance.LogBoss("CombatStart");
                if (GameManager.Instance.DebugMode) Debug.Log($"[Boss] Sky spawn kind={skyKind} count={skyCount} at {WildTargetDistance()}m");
            }
        }
    }

    void StartCaveGate(CaveBossKind caveKind, int caveCount)
    {
        {
            SetBossMusic(gateK, caveKind.ToString());
            BeginEncounterClock(caveKind.ToString());
            aliveWildThisEncounter = caveCount;
            // ボス遭遇区間だけ、最低限の戦闘可能スペース(通常天井相当・針なし)
            // を保証する。マップ全体の生成システムは変更しない(区間限定・
            // このコンポーネントの寿命(CheckEncounterComplete)で必ず解除)。
            if (TerrainManager.Instance != null && TerrainManager.Instance.cave != null && player != null)
            {
                TerrainManager.Instance.cave.SetBossClearZone(SpawnRef().position.x, 40f);
            }
            for (int i = 0; i < caveCount; i++) SpawnCaveBoss(caveKind, i);

            if (GameManager.Instance != null)
            {
                GameManager.Instance.LogBoss("CombatStart");
                if (GameManager.Instance.DebugMode) Debug.Log($"[Boss] Cave spawn kind={caveKind} count={caveCount} at {WildTargetDistance()}m");
            }
        }
    }

    void StartWildGate(WildBossKind gateKind, int gateCount)
    {
        SetBossMusic(gateK, gateKind.ToString());
        BeginEncounterClock(gateKind.ToString());
        var e = new { kind = gateKind, count = gateCount };

        if (e.kind == WildBossKind.Dragon)
        {
            aliveDragonsThisEncounter = e.count;
            for (int i = 0; i < e.count; i++) SpawnWastelandDragon(dragonStandoffDistance + i * dragonSpacing);
        }
        else
        {
            aliveWildThisEncounter = e.count;
            for (int i = 0; i < e.count; i++) SpawnWild(e.kind, i);
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.LogBoss("CombatStart");
            if (GameManager.Instance.DebugMode) Debug.Log($"[Boss] Wild spawn kind={e.kind} count={e.count} at {WildTargetDistance()}m");
        }
    }

    // 種別ごとの既定値(HP/MILE/世界の高さ/入場時の間合い/撃破パーティクル色)。
    struct WildSpec
    {
        public int hp, mile; public float height, gap; public Color burst;
        public WildSpec(int hp, int mile, float height, float gap, Color burst) { this.hp = hp; this.mile = mile; this.height = height; this.gap = gap; this.burst = burst; }
    }

    static WildSpec SpecFor(WildBossKind kind)
    {
        switch (kind)
        {
            case WildBossKind.Wolf: return new WildSpec(240, 30, 2.8f, 9f, new Color(0.7f, 0.7f, 0.8f));
            case WildBossKind.GoblinRider: return new WildSpec(400, 60, 3.6f, 9f, new Color(0.5f, 0.8f, 0.3f));
            case WildBossKind.Serpent: return new WildSpec(550, 90, 3.2f, 9f, new Color(0.4f, 0.9f, 0.4f));
            case WildBossKind.Cyclops: return new WildSpec(800, 120, 6.0f, 10f, new Color(0.9f, 0.6f, 0.3f));
            case WildBossKind.Spider: return new WildSpec(1000, 150, 2.8f, 9f, new Color(0.7f, 0.4f, 0.8f));
            case WildBossKind.Golem: return new WildSpec(1300, 200, 5.5f, 10f, new Color(0.8f, 0.7f, 0.5f));
            case WildBossKind.Griffin: return new WildSpec(1500, 260, 3.4f, 9f, new Color(1f, 0.9f, 0.5f));
            case WildBossKind.Hydra: return new WildSpec(1800, 320, 5.0f, 10f, new Color(0.3f, 1f, 0.6f));
            case WildBossKind.Demon: return new WildSpec(2000, 400, 4.5f, 9f, new Color(0.8f, 0.3f, 1f));
            case WildBossKind.BlackKnight: return new WildSpec(2800, 500, 3.2f, 9f, new Color(0.5f, 0.6f, 1f));
            default: return new WildSpec(300, 50, 3f, 9f, Color.white);
        }
    }

    WildBossArt FindArt(WildBossKind kind)
    {
        if (wildArt == null) return null;
        foreach (var a in wildArt) if (a != null && a.kind == kind) return a;
        return null;
    }

    // マルチプレイPhase 2 - 共有ボスの出現記録(HOST)/パペットの再構築(JOIN)用の入口。
    void SpawnWild(WildBossKind kind, int index) { NetCombat.BeginBossSpawn(NetCombat.BossMethod.Wild, (int)kind, index); try { SpawnWildImpl(kind, index); } finally { NetCombat.EndBossSpawn(); } }

    void SpawnWildImpl(WildBossKind kind, int index)
    {
        WildBossArt art = FindArt(kind);
        WildSpec spec = SpecFor(kind);

        GameObject go = new GameObject("WildBoss_" + kind);
        go.tag = "Boss";
        WildBossBase boss;
        switch (kind)
        {
            case WildBossKind.Wolf: boss = go.AddComponent<WolfBoss>(); break;
            case WildBossKind.GoblinRider: boss = go.AddComponent<GoblinRiderBoss>(); break;
            case WildBossKind.Serpent: boss = go.AddComponent<SerpentBoss>(); break;
            case WildBossKind.Cyclops: boss = go.AddComponent<CyclopsBoss>(); break;
            case WildBossKind.Spider: boss = go.AddComponent<SpiderBoss>(); break;
            case WildBossKind.Golem: boss = go.AddComponent<GolemBoss>(); break;
            case WildBossKind.Griffin: boss = go.AddComponent<GriffinBoss>(); break;
            case WildBossKind.Hydra: boss = go.AddComponent<HydraBoss>(); break;
            case WildBossKind.Demon: boss = go.AddComponent<DemonBoss>(); break;
            default: boss = go.AddComponent<BlackKnightBoss>(); break;
        }

        boss.bossName = kind.ToString();
        float hpScale = RematchHpScaleOr((kind == WildBossKind.Wolf || kind == WildBossKind.GoblinRider) ? Mathf.Min(3f, 1f + currentGateK * smallBossHpPerKm) : 1f);
        boss.maxHp = EffectiveBossMaxHp(Mathf.RoundToInt(spec.hp * hpScale));
        if (BattleTunedStage) boss.ApplyTuning(kind.ToString()); // ボス戦の強化(2026-10-01): 段階/必殺技/崩し
        ApplyRematchTo(boss); // 再戦の強化(2026-10-02)
        boss.slotIndex = index;
        boss.mileReward = spec.mile;
        boss.bodyHeight = spec.height;
        boss.startGap = spec.gap + index * 3.5f; // 複数出現時は少しずつ間隔をずらす
        boss.defeatBurstColor = spec.burst;
        boss.squareSprite = squareSprite;
        boss.hitSparkSprite = bossHitSparkSprite;
        boss.deathSmokeSprite = bossDeathSmokeSprite;
        boss.finalHitSe = AudioManager.Se(SeId.BossFinalHit, bossFinalHitSe);
        boss.defeatSe = AudioManager.Se(SeId.BossDefeat, bossDefeatSe);
        if (art != null)
        {
            boss.idleSprite = art.idle;
            boss.moveSprite = art.move;
            boss.windupSprite = art.windup;
            boss.attackSprite = art.attack;
        }
        // 素材が無い場合でも戦えるよう、単色ブロックで代用(見た目は仮)
        if (boss.idleSprite == null) boss.idleSprite = squareSprite;

        boss.Init(SpawnRef());
    }

    // ===== 自然洞窟ボス: 種別ごとの既定値/生成(SpecFor/FindArt/SpawnWildと同じ形) =====
    struct CaveSpec
    {
        public int hp, mile; public float height, gap; public Color burst;
        public CaveSpec(int hp, int mile, float height, float gap, Color burst) { this.hp = hp; this.mile = mile; this.height = height; this.gap = gap; this.burst = burst; }
    }

    static CaveSpec SpecForCave(CaveBossKind kind)
    {
        switch (kind)
        {
            case CaveBossKind.Centipede: return new CaveSpec(260, 32, 2.6f, 9f, new Color(0.7f, 0.75f, 0.5f));
            case CaveBossKind.Scorpion: return new CaveSpec(420, 62, 3.2f, 9f, new Color(0.6f, 0.9f, 0.5f));
            case CaveBossKind.Mole: return new CaveSpec(550, 95, 3.0f, 9f, new Color(0.6f, 0.45f, 0.3f));
            case CaveBossKind.Troll: return new CaveSpec(850, 125, 5.8f, 10f, new Color(0.85f, 0.6f, 0.3f));
            case CaveBossKind.Worm: return new CaveSpec(1100, 160, 4.2f, 10f, new Color(0.45f, 0.85f, 0.5f));
            case CaveBossKind.CrystalGolem: return new CaveSpec(1400, 210, 5.6f, 10f, new Color(0.55f, 0.85f, 1f));
            case CaveBossKind.Bat: return new CaveSpec(1600, 270, 3.0f, 9f, new Color(0.7f, 0.6f, 0.9f));
            case CaveBossKind.ScorpionKing: return new CaveSpec(1900, 330, 4.6f, 10f, new Color(0.9f, 0.5f, 0.55f));
            case CaveBossKind.Basilisk: return new CaveSpec(2100, 410, 3.4f, 9f, new Color(0.6f, 0.9f, 0.35f));
            case CaveBossKind.Drake: return new CaveSpec(2600, 470, 4.4f, 9f, new Color(1f, 0.55f, 0.25f));
            case CaveBossKind.AncientDemon: return new CaveSpec(2900, 520, 3.6f, 9f, new Color(0.75f, 0.25f, 1f));
            default: return new CaveSpec(300, 50, 3f, 9f, Color.white);
        }
    }

    CaveBossArt FindCaveArt(CaveBossKind kind)
    {
        if (caveArt == null) return null;
        foreach (var a in caveArt) if (a != null && a.kind == kind) return a;
        return null;
    }

    // マルチプレイPhase 2 - 共有ボスの出現記録(HOST)/パペットの再構築(JOIN)用の入口。
    void SpawnCaveBoss(CaveBossKind kind, int index) { NetCombat.BeginBossSpawn(NetCombat.BossMethod.Cave, (int)kind, index); try { SpawnCaveBossImpl(kind, index); } finally { NetCombat.EndBossSpawn(); } }

    void SpawnCaveBossImpl(CaveBossKind kind, int index)
    {
        CaveBossArt art = FindCaveArt(kind);
        CaveSpec spec = SpecForCave(kind);

        GameObject go = new GameObject("CaveBoss_" + kind);
        go.tag = "Boss";
        WildBossBase boss;
        switch (kind)
        {
            case CaveBossKind.Centipede: boss = go.AddComponent<CentipedeBoss>(); break;
            case CaveBossKind.Scorpion: boss = go.AddComponent<ScorpionBoss>(); break;
            case CaveBossKind.Mole: boss = go.AddComponent<MoleBoss>(); break;
            case CaveBossKind.Troll: boss = go.AddComponent<TrollBoss>(); break;
            case CaveBossKind.Worm: boss = go.AddComponent<WormBoss>(); break;
            case CaveBossKind.CrystalGolem: boss = go.AddComponent<CrystalGolemBoss>(); break;
            case CaveBossKind.Bat: boss = go.AddComponent<BatBoss>(); break;
            case CaveBossKind.ScorpionKing: boss = go.AddComponent<ScorpionKingBoss>(); break;
            case CaveBossKind.Basilisk: boss = go.AddComponent<BasiliskBoss>(); break;
            case CaveBossKind.Drake: boss = go.AddComponent<DrakeBoss>(); break;
            default: boss = go.AddComponent<AncientDemonBoss>(); break;
        }

        boss.bossName = kind.ToString();
        // 不具合修正(2026-09-26) - 自然洞窟ボスの素材(実イラスト/手続き的シルエットとも)は
        // 頭が画面右向きに描かれているのに、WildBossBaseの既定(artFacesLeft=true=左向き素材)の
        // ままだったため、常にプレイヤーと逆を向いて表示されていた。
        boss.artFacesLeft = false;
        float hpScale = RematchHpScaleOr((kind == CaveBossKind.Centipede || kind == CaveBossKind.Scorpion) ? Mathf.Min(3f, 1f + currentGateK * smallBossHpPerKm) : 1f);
        boss.maxHp = EffectiveBossMaxHp(Mathf.RoundToInt(spec.hp * hpScale));
        ApplyRematchTo(boss); // 再戦の強化(2026-10-02)
        boss.slotIndex = index;
        boss.mileReward = spec.mile;
        boss.bodyHeight = spec.height;
        boss.startGap = spec.gap + index * 3.5f;
        boss.defeatBurstColor = spec.burst;
        boss.squareSprite = squareSprite;
        boss.hitSparkSprite = bossHitSparkSprite;
        boss.deathSmokeSprite = bossDeathSmokeSprite;
        boss.finalHitSe = AudioManager.Se(SeId.BossFinalHit, bossFinalHitSe);
        boss.defeatSe = AudioManager.Se(SeId.BossDefeat, bossDefeatSe);
        if (art != null)
        {
            boss.idleSprite = art.idle;
            boss.moveSprite = art.move;
            boss.windupSprite = art.windup;
            boss.attackSprite = art.attack;
        }
        // 実イラスト未着手の間は、CaveBossFxの手続き的シルエットを暫定ボディ
        // として使う(荒野街道のsquareSprite代用と同じ位置づけ - 差し替え
        // 前提の仮素材)。
        if (boss.idleSprite == null) boss.idleSprite = CaveBodySilhouette(kind);
        if (boss.windupSprite == null) boss.windupSprite = boss.idleSprite;

        boss.Init(SpawnRef());
    }

    static Sprite CaveBodySilhouette(CaveBossKind kind)
    {
        switch (kind)
        {
            case CaveBossKind.Centipede: return CaveBossFx.Centipede();
            case CaveBossKind.Scorpion: return CaveBossFx.Scorpion();
            case CaveBossKind.Mole: return CaveBossFx.Mole();
            case CaveBossKind.Troll: return CaveBossFx.Troll();
            case CaveBossKind.Worm: return CaveBossFx.Worm();
            case CaveBossKind.CrystalGolem: return CaveBossFx.CrystalGolem();
            case CaveBossKind.Bat: return CaveBossFx.Bat();
            case CaveBossKind.ScorpionKing: return CaveBossFx.ScorpionKing();
            case CaveBossKind.Basilisk: return CaveBossFx.Basilisk();
            case CaveBossKind.Drake: return CaveBossFx.Drake();
            default: return CaveBossFx.AncientDemon();
        }
    }

    // ===== 天空回廊ボス: 遭遇開始/種別ごとの既定値/生成 =====
    void StartSkyEncounter(SkyBossKind kind, int count)
    {
        if (kind == SkyBossKind.Dragon || kind == SkyBossKind.Majin)
        {
            // 既存コントローラーのボス: 遭遇数だけ先に確定し、遠方シルエットの接近演出の後に実体を出す。
            if (kind == SkyBossKind.Dragon) aliveDragonsThisEncounter = count;
            else aliveMajinsThisEncounter = count;
            StartCoroutine(SkyAirborneEntrance(kind, count));
            return;
        }
        aliveWildThisEncounter = count;
        for (int i = 0; i < count; i++) SpawnSkyBoss(kind, i);
    }

    // 遠方(背景レイヤー)を小さなシルエットが近づいてくる → 画面右外から実体が飛来(既存の登場処理)。
    System.Collections.IEnumerator SkyAirborneEntrance(SkyBossKind kind, int count)
    {
        Sprite[] frames = kind == SkyBossKind.Dragon ? dragonIdleFrames : majinIdleFrames;
        float baseScale = kind == SkyBossKind.Dragon ? dragonScale : majinScale;
        for (int i = 0; i < count; i++)
        {
            Vector2 from = new Vector2(0.62f + i * 0.08f, 0.9f - i * 0.05f);
            Vector2 to = new Vector2(1.08f, 0.66f - i * 0.06f);
            // ドラゴン素材は左向き(頭が左)なので、右へ流れる間は反転させない方が「こちらへ向かってくる」に見える
            SkyFlyby.Create(frames, from, to, baseScale * 0.18f, baseScale * 0.6f, 1.3f, new Color(0.4f, 0.47f, 0.66f, 0.8f), false);
        }
        yield return new WaitForSeconds(1.15f);
        if (kind == SkyBossKind.Dragon)
        {
            float[] d = BuildScatteredDistances(count, dragonStandoffDistance, dragonSpacing, dragonScatterJitter);
            for (int i = 0; i < count; i++) SpawnSkyDragon(d[i]);
        }
        else
        {
            float[] d = BuildScatteredDistances(count, majinStandoffDistance, majinSpacing, majinScatterJitter);
            for (int i = 0; i < count; i++) SpawnSkyMajin(d[i]);
        }
    }

    float SkySmallBossHpScale() => Mathf.Min(3f, 1f + currentGateK * smallBossHpPerKm);

    // 1,000m ドラゴン(天空回廊の既存ドラゴン)。火炎弾(反射可能)はそのまま、低空突進を有効化、
    // skyDragonLandingFromMeters以降は着地噛みつきも使う(いずれもDragonControllerの既存機能)。
    // マルチプレイPhase 2 - 共有ボスの出現記録(HOST)/パペットの再構築(JOIN)用の入口。
    void SpawnSkyDragon(float standoff) { NetCombat.BeginBossSpawn(NetCombat.BossMethod.SkyDragon, 0, 0, standoff); try { SpawnSkyDragonImpl(standoff); } finally { NetCombat.EndBossSpawn(); } }

    void SpawnSkyDragonImpl(float standoff)
    {
        SpawnDragon(standoff, dragon =>
        {
            dragon.gameObject.name = "SkyDragon";
            dragon.chargeAttackEnabled = true;
            dragon.landingAttackEnabled = currentGateK * gateIntervalMeters >= skyDragonLandingFromMeters;
            dragon.landingAttackChance = 0.2f;
            dragon.maxHp = EffectiveBossMaxHp(Mathf.RoundToInt(dragonMaxHp * RematchHpScaleOr(SkySmallBossHpScale())));
        });
    }

    // マルチプレイPhase 2 - 共有ボスの出現記録(HOST)/パペットの再構築(JOIN)用の入口。
    void SpawnSkyMajin(float standoff) { NetCombat.BeginBossSpawn(NetCombat.BossMethod.SkyMajin, 0, 0, standoff); try { SpawnSkyMajinImpl(standoff); } finally { NetCombat.EndBossSpawn(); } }

    void SpawnSkyMajinImpl(float standoff)
    {
        SpawnMajin(standoff, majin =>
        {
            majin.gameObject.name = "SkyMajin";
            majin.maxHp = EffectiveBossMaxHp(Mathf.RoundToInt(dragonMaxHp * majinHpMultiplier * RematchHpScaleOr(SkySmallBossHpScale())));
        });
    }

    struct SkySpec
    {
        public int hp, mile; public float height, gap; public Color burst;
        public SkySpec(int hp, int mile, float height, float gap, Color burst) { this.hp = hp; this.mile = mile; this.height = height; this.gap = gap; this.burst = burst; }
    }

    // 神話級・天災級のため、同じ距離帯の荒野街道/自然洞窟ボスよりHP/MILE/体格を一段上げる。
    static SkySpec SpecForSky(SkyBossKind kind)
    {
        switch (kind)
        {
            case SkyBossKind.Behemoth: return new SkySpec(700, 110, 4.8f, 9f, new Color(0.55f, 0.8f, 1f));
            case SkyBossKind.Titan: return new SkySpec(1100, 160, 14f, 9f, new Color(0.8f, 0.85f, 0.95f));
            case SkyBossKind.Jellyfish: return new SkySpec(1300, 200, 4.4f, 8f, new Color(0.6f, 0.95f, 1f));
            case SkyBossKind.Leviathan: return new SkySpec(1600, 250, 4.6f, 7f, new Color(0.7f, 0.85f, 1f));
            case SkyBossKind.Fenrir: return new SkySpec(1800, 310, 3.8f, 7f, new Color(0.6f, 0.85f, 1f));
            case SkyBossKind.SkyGolem: return new SkySpec(2100, 370, 6.2f, 9f, new Color(0.85f, 0.8f, 0.7f));
            case SkyBossKind.Phoenix: return new SkySpec(2200, 430, 4.4f, 8f, new Color(1f, 0.55f, 0.2f));
            case SkyBossKind.SkySerpent: return new SkySpec(2500, 490, 3.4f, 8f, new Color(0.75f, 0.85f, 1f));
            case SkyBossKind.Guardian: return new SkySpec(3200, 580, 3.6f, 7f, new Color(1f, 0.92f, 0.65f));
            default: return new SkySpec(600, 80, 3f, 9f, Color.white);
        }
    }

    SkyBossArt FindSkyArt(SkyBossKind kind)
    {
        if (skyArt == null) return null;
        foreach (var a in skyArt) if (a != null && a.kind == kind) return a;
        return null;
    }

    // マルチプレイPhase 2 - 共有ボスの出現記録(HOST)/パペットの再構築(JOIN)用の入口。
    void SpawnSkyBoss(SkyBossKind kind, int index) { NetCombat.BeginBossSpawn(NetCombat.BossMethod.Sky, (int)kind, index); try { SpawnSkyBossImpl(kind, index); } finally { NetCombat.EndBossSpawn(); } }

    void SpawnSkyBossImpl(SkyBossKind kind, int index)
    {
        SkyBossArt art = FindSkyArt(kind);
        SkySpec spec = SpecForSky(kind);

        GameObject go = new GameObject("SkyBoss_" + kind);
        go.tag = "Boss";
        WildBossBase boss;
        switch (kind)
        {
            case SkyBossKind.Behemoth: boss = go.AddComponent<BehemothBoss>(); break;
            case SkyBossKind.Titan: boss = go.AddComponent<SkyTitanBoss>(); break;
            case SkyBossKind.Jellyfish: boss = go.AddComponent<SkyJellyfishBoss>(); break;
            case SkyBossKind.Leviathan: boss = go.AddComponent<LeviathanBoss>(); break;
            case SkyBossKind.Fenrir: boss = go.AddComponent<FenrirBoss>(); break;
            case SkyBossKind.SkyGolem: boss = go.AddComponent<SkyGolemBoss>(); break;
            case SkyBossKind.Phoenix: boss = go.AddComponent<PhoenixBoss>(); break;
            case SkyBossKind.SkySerpent: boss = go.AddComponent<SkySerpentBoss>(); break;
            default: boss = go.AddComponent<CelestialGuardianBoss>(); break;
        }

        boss.bossName = kind.ToString();
        boss.maxHp = EffectiveBossMaxHp(Mathf.RoundToInt(spec.hp * RematchHpScaleOr(1f)));
        ApplyRematchTo(boss); // 再戦の強化(2026-10-02)
        boss.slotIndex = index;
        boss.mileReward = spec.mile;
        boss.bodyHeight = spec.height;
        boss.startGap = spec.gap + index * 3.5f;
        boss.defeatBurstColor = spec.burst;
        boss.squareSprite = squareSprite;
        boss.hitSparkSprite = bossHitSparkSprite;
        boss.deathSmokeSprite = bossDeathSmokeSprite;
        boss.finalHitSe = AudioManager.Se(SeId.BossFinalHit, bossFinalHitSe);
        boss.defeatSe = AudioManager.Se(SeId.BossDefeat, bossDefeatSe);
        if (art != null)
        {
            boss.idleSprite = art.idle;
            boss.moveSprite = art.move;
            boss.windupSprite = art.windup;
            boss.attackSprite = art.attack;
        }
        // 実イラストが無い場合の暫定ボディ(自然洞窟のCaveBodySilhouetteと同じ位置づけ)
        if (boss.idleSprite == null) boss.idleSprite = SkyBossFx.Placeholder(kind);
        if (boss.windupSprite == null) boss.windupSprite = boss.idleSprite;

        boss.Init(SpawnRef());
    }

    // 80,000m ドラゴン: 既存DragonControllerに突進と着地攻撃を有効化して流用。
    // マルチプレイPhase 2 - 共有ボスの出現記録(HOST)/パペットの再構築(JOIN)用の入口。
    void SpawnWastelandDragon(float standoff) { NetCombat.BeginBossSpawn(NetCombat.BossMethod.WastelandDragon, 0, 0, standoff); try { SpawnWastelandDragonImpl(standoff); } finally { NetCombat.EndBossSpawn(); } }

    void SpawnWastelandDragonImpl(float standoff)
    {
        GameObject go = new GameObject("WildBoss_Dragon");
        go.tag = "Boss";
        go.transform.localScale = Vector3.one * dragonScale;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = RenderOrder.Boss;

        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(2.2f, 2.0f);
        var colDebug = go.AddComponent<ColliderDebugView>();
        colDebug.color = new Color(1f, 0.4f, 0f);

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        DragonController dragon = go.AddComponent<DragonController>();
        dragon.idleFrames = dragonIdleFrames;
        dragon.chargeFrames = dragonChargeFrames;
        dragon.fireFrames = dragonFireFrames;
        dragon.squareSprite = squareSprite;
        dragon.maxHp = EffectiveBossMaxHp(Mathf.RoundToInt(2500 * RematchHpScaleOr(1f)));
        dragon.standoffDistance = standoff;
        dragon.chargeAttackEnabled = true;
        dragon.landingAttackEnabled = true;
        dragon.mileReward = 450;
        if (BattleTunedStage) dragon.EnableWastelandBattle(BossBattleTuning.I.For("Dragon"));
        dragon.finalHitSparkSprite = bossHitSparkSprite;
        dragon.bossDeathSmokeSprite = bossDeathSmokeSprite;
        dragon.finalHitSe = AudioManager.Se(SeId.BossFinalHit, bossFinalHitSe);
        dragon.bossDefeatSe = AudioManager.Se(SeId.BossDefeat, bossDefeatSe);
        dragon.defeatBurstColor = dragonDefeatBurstColor;

        var facing = go.AddComponent<EnemyFacing>();
        facing.visual = go.transform;
        facing.defaultFacingRight = false;
        facing.alwaysFacePlayer = true;
        facing.player = player;

        dragon.Init(SpawnRef());
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 自動テスト用(開発版のみ, 2026-10-02 カードバランス調査): family 0=荒野(Dragon=荒野ドラゴン) 1=洞窟 2=天空
    public void DebugSpawnBossForTest(int family, int kind)
    {
        if (family == 1) DebugForceSpawnCave((CaveBossKind)kind);
        else if (family == 2) DebugForceSpawnSky((SkyBossKind)kind);
        else DebugForceSpawn((WildBossKind)kind);
    }

    void DebugForceSpawn(WildBossKind kind, int count = 1)
    {
        IsBossPhase = true;
        if (TerrainManager.Instance != null) TerrainManager.Instance.ClearAllEnemies();
        foreach (var o in GameObject.FindGameObjectsWithTag("Boss")) Destroy(o);
        aliveDragonsThisEncounter = 0; aliveMajinsThisEncounter = 0; aliveWildThisEncounter = 0;
        if (kind == WildBossKind.Dragon) { aliveDragonsThisEncounter = 1; SpawnWastelandDragon(dragonStandoffDistance); }
        else { aliveWildThisEncounter = count; for (int i = 0; i < count; i++) SpawnWild(kind, i); }
        BeginEncounterClock(kind.ToString());
        Debug.Log("[Boss] DebugForceSpawn " + kind);
    }

    void DebugForceSpawnCave(CaveBossKind kind, int count = 1)
    {
        IsBossPhase = true;
        if (TerrainManager.Instance != null) TerrainManager.Instance.ClearAllEnemies();
        foreach (var o in GameObject.FindGameObjectsWithTag("Boss")) Destroy(o);
        aliveDragonsThisEncounter = 0; aliveMajinsThisEncounter = 0; aliveWildThisEncounter = 0;
        if (TerrainManager.Instance != null && TerrainManager.Instance.cave != null && player != null)
        {
            TerrainManager.Instance.cave.SetBossClearZone(player.position.x, 40f);
        }
        aliveWildThisEncounter = count;
        for (int i = 0; i < count; i++) SpawnCaveBoss(kind, i);
        Debug.Log("[Boss] DebugForceSpawnCave " + kind);
    }

    void DebugForceSpawnSky(SkyBossKind kind, int count = 1)
    {
        IsBossPhase = true;
        if (TerrainManager.Instance != null) TerrainManager.Instance.ClearAllEnemies();
        foreach (var o in GameObject.FindGameObjectsWithTag("Boss")) Destroy(o);
        aliveDragonsThisEncounter = 0; aliveMajinsThisEncounter = 0; aliveWildThisEncounter = 0;
        // 実際のゲートと同じく「戦闘中の移動を距離から除外」を開始しておく(報酬完了時のEndと対になる)。
        if (GameManager.Instance != null) GameManager.Instance.BeginBossDistanceExclusion();
        StartSkyEncounter(kind, count);
        Debug.Log("[Boss] DebugForceSpawnSky " + kind + " x" + count);
    }
#endif

    public void OnWildBossDefeated()
    {
        BossesDefeated++;
        aliveWildThisEncounter--;
        if (RushGateK > 0) Debug.Log($"[BossRush] Boss Defeated (gate {RushGateK * 1000}m, remaining {aliveWildThisEncounter}, queued {rushChain.Count})");
        // ボスラッシュ: 「前のボスの撃破直後に次が登場」。今いるボスが全員倒れたら、連続の次のボスを出す。
        if (RushGateK > 0 && rushChain.Count > 0 && aliveWildThisEncounter - rushPending <= 0)
        {
            rushPending--;
            SpawnRushBoss(rushChain.Dequeue());
            return;
        }
        if (RushGateK > 0 && aliveWildThisEncounter <= 0) RushGateK = 0;
        CheckEncounterComplete();
    }

    // Card Expansion/Gacha Evolution Ver.1 - "Boss Challenge"-family cards
    // (BossHpMultiplier) apply here, on top of whatever base HP the
    // existing Dragon/Majin/Mechanical Dragon formulas already compute.
    int EffectiveBossMaxHp(int baseHp)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (NetTestBossHpOverride > 0) return NetTestBossHpOverride;
#endif
        float multiplier = GameManager.Instance != null ? GameManager.Instance.BossHpMultiplier : 1f;
        // 2026-10-02: ボスHPの再設計案(開発版DEBUGで15/20/25発を選んだ時だけ。既定/製品版は1)
        multiplier *= BossHpPlan.Multiplier(BossHpPlan.CurrentDistance);
        return Mathf.Max(1, Mathf.RoundToInt(baseHp * Mathf.Max(0.01f, multiplier)));
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // マルチプレイPhase 2の自動テスト用(開発ビルドのみ): 荒野街道ボスを即時出現させる。
    public static int NetTestBossHpOverride;
    public void NetTestSpawnWild(WildBossKind kind, int count)
    {
        IsBossPhase = true;
        if (GameManager.Instance != null) GameManager.Instance.BeginBossDistanceExclusion();
        if (TerrainManager.Instance != null) TerrainManager.Instance.ClearAllEnemies();
        currentGateK = gateK;
        aliveWildThisEncounter = count;
        for (int i = 0; i < count; i++) SpawnWild(kind, i);
        BeginEncounterClock(kind.ToString()); // ボス戦の強化: マルチの自動テストでもラン再開を確かめる
    }
#endif

    // マルチプレイPhase 2 - JOIN側: HOSTが出現させたボスと同じ生成処理でパペットを作る
    // (NetCombat.CreatingPuppet中なので、各ボスのInitはAIを始めずに戻る)。
    public void NetSpawnPuppet(NetCombat.BossMethod method, int kind, int index, float standoff)
    {
        switch (method)
        {
            case NetCombat.BossMethod.Wild: SpawnWild((WildBossKind)kind, index); break;
            case NetCombat.BossMethod.Cave: SpawnCaveBoss((CaveBossKind)kind, index); break;
            case NetCombat.BossMethod.Sky: SpawnSkyBoss((SkyBossKind)kind, index); break;
            case NetCombat.BossMethod.Dragon: SpawnDragon(standoff); break;
            case NetCombat.BossMethod.WastelandDragon: SpawnWastelandDragon(standoff); break;
            case NetCombat.BossMethod.SkyDragon: SpawnSkyDragon(standoff); break;
            case NetCombat.BossMethod.Majin: SpawnMajin(standoff); break;
            case NetCombat.BossMethod.SkyMajin: SpawnSkyMajin(standoff); break;
            case NetCombat.BossMethod.MechDragon: SpawnMechanicalDragon(standoff); break;
            case NetCombat.BossMethod.Reaper: SpawnDeath(); break;
        }
    }
}
