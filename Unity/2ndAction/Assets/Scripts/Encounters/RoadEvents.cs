using System.Collections.Generic;
using UnityEngine;

// 道中イベント(2026-11-04、マスターの依頼「5km ごとのボス以外にも、走っている途中で何かが起きる」)。調整値は RoadEventTuning。
//  ・通常マップの 20〜90km を 5km の区間に分け、区間ごとに1回(sectionChance)、次の中から1つを選んで起こす:
//      敵ラッシュ(数十秒だけ間隔を詰めて休憩なし)/ 飛行編隊(飛ぶ敵の組を多く)/ 強敵の混成(HP の多い敵が混ざる)/
//      追跡(速い敵が多い)/ 空中ボスの乱入(BossManager.AirRaid.cs。走ったまま戦う)
//  ・背景の旅(LandmarkJourney)とつなぐ: 章の後半(目的地の城/結晶/神殿へ近づく 5〜10km)は「関門の手前の敵ラッシュ」「強敵」を多く、
//    敵ラッシュは目的地の目の前(章の 8.2〜9.0km)に置く。章の前半(内部を抜けた後)は飛行編隊/追跡/乱入を多く。
//    天空回廊は飛行編隊と乱入、自然洞窟は強敵(地底の大物)を多く。その帯に居ない種類の敵のイベントは選ばない。
//  ・Encounter の中身は今までの Formation のまま(1か所に重ねない)。間隔/休憩/激しさ/Formation と敵の選び方の重みだけを変える
//    (EncounterDirector が PaceMul/Surge/NoRest/FormationMul/EnemyMul を読む)。同時に攻撃できる雑魚の上限(ZakoAttackLimiter)はそのまま効く。
//  ・始まりの知らせは小さく: WARNING の帯(訳済みの文「WARNING  {0} が迫る!」)+ 音 + 空の影/砂ぼこり/地響き。
//  ・ボス戦/BONUS ZONE/門をくぐる間/カウントダウン中は始めない(少し後へ送る。区間の終わりを過ぎたらその区間は無し)。
//  ・マルチ: 出現を決める HOST だけが持つ(JOIN は何もしない)。乱入はソロ/CPU サポートだけ(マルチは今は対象外)。
public enum RoadEventKind { None, Rush, Flying, Elite, Chase, AirRaid }

public class RoadEvents : MonoBehaviour
{
    public static RoadEvents Instance { get; private set; }
    public static RoadEventKind Active { get; private set; }
    public static string ActiveLabel { get; private set; } = "";
    static float activeUntil;
    // 確認用(QA)
    public static int Started;
    public static readonly int[] StartedByKind = new int[6];
    public static readonly List<string> History = new List<string>(); // 「km 種類 label」
    public static int Postponed, Skipped, RaidFallbacks;

    class Plan { public int section; public RoadEventKind kind; public float at, hi; public bool done; public string why = ""; }
    readonly Dictionary<int, Plan> plans = new Dictionary<int, Plan>();
    float lastRaidD = -99999f;
    GameManager boundGm; bool boundStarted;
    System.Random rng = new System.Random();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[RoadEvents]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<RoadEvents>();
    }

    static RoadEventTuning T => RoadEventTuning.I;
    float SectionM => Mathf.Max(500f, T.sectionKm * 1000f);

    void ResetRun()
    {
        plans.Clear(); lastRaidD = -99999f;
        Active = RoadEventKind.None; ActiveLabel = ""; activeUntil = 0f;
        rng = new System.Random(System.Environment.TickCount);
    }

    void Update()
    {
        var gm = GameManager.Instance;
        if (gm != boundGm) { boundGm = gm; boundStarted = false; }
        if (gm == null || !gm.HasStarted) { if (boundStarted) { boundStarted = false; ResetRun(); } return; }
        if (!boundStarted) { boundStarted = true; ResetRun(); }
        if (gm.IsGameOver) { End("game over"); return; }
        if (!Usable(gm)) { if (Active != RoadEventKind.None) End("not usable"); return; }

        float d = gm.MaxDistance;
        var bm = BossManager.Instance;
        // 終わり: 時間(乱入はボスが終わった時 = OnRaidEnded)。ボス戦が始まったら道中のイベントも閉じる
        if (Active != RoadEventKind.None && Active != RoadEventKind.AirRaid)
        {
            if (Time.time >= activeUntil) End("time");
            else if (bm != null && bm.IsBossPhase && !bm.AirRaidActive) End("boss gate");
        }
        if (Active == RoadEventKind.AirRaid && (bm == null || !bm.AirRaidActive)) End("raid over");

        if (d < T.fromKm * 1000f || d >= T.untilKm * 1000f) return;
        int s = Mathf.FloorToInt(d / SectionM);
        if (!plans.TryGetValue(s, out var p)) { p = MakePlan(s, d); plans[s] = p; }
        if (p.done || p.kind == RoadEventKind.None || d < p.at || Active != RoadEventKind.None) return;
        if (d > p.hi) { p.done = true; Skipped++; Debug.Log($"[RoadEvent] section {s * SectionM / 1000f:F0}km: {p.kind} skipped (passed the window: {p.why})"); return; }
        if (Blocked(gm, bm, out string why)) { p.at = d + 80f; p.why = why; Postponed++; return; }
        p.done = true;
        Begin(p, d);
    }

    static bool Usable(GameManager gm)
    {
        if (!RoadEventTuning.On || !DifficultyTuning.Active) return false; // 通常マップ(ラスダン/闘技場/練習は DifficultyTuning.Active が false)
        if (NetCombat.SuppressLocalEnemySpawn) return false; // JOIN: 出現は HOST が決める
        return true;
    }

    static bool Blocked(GameManager gm, BossManager bm, out string why)
    {
        why = "";
        if (bm != null && bm.IsBossPhase) { why = "boss"; return true; }
        if (BonusZone.Instance != null && BonusZone.Instance.State != BonusZone.Phase.Idle) { why = "bonus zone"; return true; }
        if (LandmarkJourney.GateVisible) { why = "gate"; return true; }
        if (gm.CountdownActive || gm.ResumeGateActive) { why = "countdown"; return true; }
        return false;
    }

    // ---- 区間の予定 ----
    Plan MakePlan(int s, float d)
    {
        var p = new Plan { section = s };
        float sec0 = s * SectionM;
        bool afterBigGate = Mathf.Abs(Mathf.Repeat(sec0, 10000f)) < 1f; // 10km の関門(門 → 内部)の後の区間
        bool approach = !afterBigGate;                                   // 章の後半: 目的地へ近づく
        float lo = sec0 + (afterBigGate ? T.startAfterBigGateKm : T.startAfterKm) * 1000f;
        float hi = sec0 + SectionM - T.endBeforeKm * 1000f;
        lo = Mathf.Max(lo, d + 60f); // 途中から始まった(CONTINUE/ワープ)時はこれから先だけ
        p.hi = hi;
        if (hi - lo < 150f || R01() >= T.sectionChance) { p.kind = RoadEventKind.None; p.done = true; Debug.Log($"[RoadEvent] section {sec0 / 1000f:F0}km: none ({(hi - lo < 150f ? "no room" : "chance")})"); return p; }

        string stage = GameManager.Instance != null ? GameManager.Instance.ActiveRunStageId : "";
        bool sky = stage == "sky_corridor", cave = stage == "natural_cave";
        float wRush = T.rushWeight * (approach ? T.approachRushMul : 1f);
        float wFly = HasEnemy(lo, IsFlyer) ? T.flyingWeight * (approach ? 1f : T.openFlyingMul) * (sky ? T.skyFlyingMul : 1f) : 0f;
        float wElite = HasEnemy(lo, IsElite) ? T.eliteWeight * (approach ? T.approachEliteMul : 1f) * (cave ? T.caveEliteMul : 1f) : 0f;
        float wChase = HasEnemy(lo, IsChaser) ? T.chaseWeight * (approach ? 1f : T.openChaseMul) : 0f;
        float wRaid = RaidPlannable(lo) ? T.airRaidWeight * (approach ? 1f : T.openRaidMul) * (sky ? T.skyRaidMul : 1f) : 0f;
        float[] w = { 0f, wRush, wFly, wElite, wChase, wRaid };
        float sum = 0f; foreach (var x in w) sum += x;
        float r = R01() * sum;
        p.kind = RoadEventKind.Rush;
        for (int i = 1; i < w.Length; i++) { if (r < w[i]) { p.kind = (RoadEventKind)i; break; } r -= w[i]; }
        // 敵ラッシュは目的地(城門/結晶/神殿)の目の前に。それ以外は区間の中のどこか
        if (p.kind == RoadEventKind.Rush && approach)
        {
            float chap0 = Mathf.Floor(sec0 / 10000f) * 10000f;
            float a = chap0 + T.approachRushAtKm.x * 1000f, b = chap0 + T.approachRushAtKm.y * 1000f;
            p.at = Mathf.Clamp(Mathf.Lerp(a, b, R01()), lo, hi);
        }
        else p.at = Mathf.Lerp(lo, hi - 200f, R01());
        if (p.kind == RoadEventKind.AirRaid) p.at = Mathf.Min(p.at, hi - T.raidMinGateGapKm * 1000f + T.endBeforeKm * 1000f); // 関門まで戦える距離を残す
        p.at = Mathf.Max(p.at, lo);
        Debug.Log($"[RoadEvent] section {sec0 / 1000f:F0}km{(approach ? " (approach)" : "")}: {p.kind} at {p.at:F0}m (window {lo:F0}-{hi:F0}m, w rush {wRush:F1} fly {wFly:F1} elite {wElite:F1} chase {wChase:F1} raid {wRaid:F1})");
        return p;
    }

    bool RaidPlannable(float at)
    {
        if (RoadEventTuning.RaidDisabledByArg || NetRunLauncher.IsMultiplayerRun) return false;
        if (at < T.raidUnlockKm * 1000f || at - lastRaidD < T.raidCooldownKm * 1000f) return false;
        var gm = GameManager.Instance;
        var keys = gm != null ? T.RaidKeys(gm.ActiveRunStageId) : null;
        return keys != null && keys.Count > 0;
    }

    float R01() => (float)rng.NextDouble();

    // ---- 始まり / 終わり ----
    void Begin(Plan p, float d)
    {
        var kind = p.kind;
        if (kind == RoadEventKind.AirRaid)
        {
            var bm = BossManager.Instance;
            string why = "no boss manager";
            if (bm != null && d - lastRaidD >= T.raidCooldownKm * 1000f && bm.CanStartAirRaid(d, out why) && bm.StartAirRaid())
            {
                lastRaidD = d;
                Mark(kind, d, "AIR RAID " + BossManager.LastRaidKey);
                return;
            }
            // 乱入できない(関門が近い/BONUS など): 同じ空の事件として飛行編隊へ
            RaidFallbacks++;
            Debug.Log($"[RoadEvent] air raid at {d:F0}m not possible ({why}) -> flying squad");
            kind = HasEnemy(d, IsFlyer) ? RoadEventKind.Flying : RoadEventKind.Rush;
        }
        float secs = kind == RoadEventKind.Rush ? T.rushSeconds : kind == RoadEventKind.Flying ? T.flyingSeconds : kind == RoadEventKind.Elite ? T.eliteSeconds : T.chaseSeconds;
        activeUntil = Time.time + Mathf.Max(3f, secs);
        var sample = SampleEnemy(d, kind);
        string name = kind == RoadEventKind.Rush ? "HORDE" : sample != null && !string.IsNullOrEmpty(sample.displayName) ? sample.displayName.ToUpperInvariant() : kind.ToString().ToUpperInvariant();
        Mark(kind, d, name);
        Cue(kind, sample, name);
    }

    void Mark(RoadEventKind kind, float d, string label)
    {
        Active = kind; ActiveLabel = label;
        Started++; StartedByKind[(int)kind]++;
        History.Add($"{d / 1000f:F1}\t{kind}\t{label}");
        Debug.Log($"[RoadEvent] START {kind} '{label}' at {d:F0}m{(kind != RoadEventKind.AirRaid ? $" for {activeUntil - Time.time:F0}s" : "")}");
    }

    void End(string why)
    {
        if (Active == RoadEventKind.None) return;
        Debug.Log($"[RoadEvent] END {Active} ({why}) at {(GameManager.Instance != null ? GameManager.Instance.MaxDistance : 0f):F0}m");
        Active = RoadEventKind.None; ActiveLabel = "";
    }

    public static void OnRaidEnded(bool defeated) { if (Instance != null && Active == RoadEventKind.AirRaid) Instance.End(defeated ? "raid boss defeated" : "raid boss left"); }

    // 開発用: 今すぐ起こす(QA)
    public static bool DebugStart(RoadEventKind kind)
    {
        if (Instance == null || GameManager.Instance == null) return false;
        Instance.End("debug");
        float d = GameManager.Instance.MaxDistance;
        var p = new Plan { section = -1, kind = kind, at = d, hi = d + 5000f, done = true };
        if (kind == RoadEventKind.AirRaid) Instance.lastRaidD = -99999f;
        Instance.Begin(p, d);
        return Active != RoadEventKind.None;
    }

    // ---- 知らせ(小さく: 帯 + 音 + 空の影/砂ぼこり/地響き) ----
    void Cue(RoadEventKind kind, EnemyDefinition sample, string name)
    {
        Color c = kind == RoadEventKind.Rush ? new Color(1f, 0.6f, 0.3f) : kind == RoadEventKind.Flying ? new Color(0.6f, 0.85f, 1f)
            : kind == RoadEventKind.Elite ? new Color(1f, 0.45f, 0.4f) : new Color(1f, 0.85f, 0.35f);
        BossBattleHud.Banner($"WARNING  {name} が迫る!", c, 1.8f);
        var am = AudioManager.Instance;
        var cam = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        switch (kind)
        {
            case RoadEventKind.Rush:
                if (am != null) am.PlaySe(SeId.EnemyTelegraph, 0.9f);
                SkyBossSfx.Play(SkyBossSfx.Impact(), 0.35f);
                if (cam != null) cam.Shake(0.05f, 0.5f);
                Dust(true, new Color(0.62f, 0.5f, 0.36f, 0.7f));
                break;
            case RoadEventKind.Flying: // 羽音と影の編隊で(画面は揺らさない)
                SkyBossSfx.Play(SkyBossSfx.Wind(), 0.6f);
                SkyBossSfx.Play(SkyBossSfx.Whoosh(), 0.45f);
                if (sample != null) SquadShadow(sample.sprite);
                break;
            case RoadEventKind.Elite:
                if (am != null) am.PlaySe(SeId.LandHeavy, 0.9f);
                if (cam != null) cam.Shake(0.09f, 0.25f);
                Instance.Invoke(nameof(SecondThud), 0.45f);
                break;
            case RoadEventKind.Chase:
                SkyBossSfx.Play(SkyBossSfx.Whoosh(), 0.6f);
                Dust(false, new Color(0.62f, 0.5f, 0.36f, 0.7f));
                break;
        }
    }

    void SecondThud()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.LandHeavy, 0.8f);
        var cam = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cam != null) cam.Shake(0.07f, 0.2f);
    }

    // 画面の端(前 = 右 / 後ろ = 左)の地面から砂ぼこり(敵の群れが来る)
    static void Dust(bool ahead, Color col)
    {
        var cam = Camera.main; var pc = PlayerController.Instance;
        if (cam == null || pc == null) return;
        float x = ahead ? GameView.Right(cam) - 0.6f : GameView.Left(cam) + 0.6f;
        for (int i = 0; i < 3; i++)
            OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), new Vector3(x, pc.transform.position.y + 0.3f + i * 0.5f, 0f), col, 8, 1.1f, 0.5f, 1.3f, 2.2f, 2.5f, RenderOrder.CombatFx - 2);
    }

    // ---- 上空を横切る影(判定なし。暗い単色のシルエット。2026-11-04 エステルの案) ----
    static readonly Color ShadowTint = new Color(0.06f, 0.07f, 0.11f, 0.72f);
    static float ScaleFor(Sprite sprite, float heightOfScreen)
    {
        var cam = Camera.main;
        return cam == null || sprite == null ? 1f : cam.orthographicSize * 2f * Mathf.Clamp(heightOfScreen, 0.02f, 0.8f) / Mathf.Max(0.01f, sprite.bounds.size.y);
    }

    // 飛行編隊: 4つの影をひとつの編隊に(浅い V 字。先頭 1.5 倍・残り 1.2 倍。右(前)→左(後ろ)へ約 1.3 秒。WARNING の帯より上 = 画面の上から 15〜25%)
    public static void SquadShadow(Sprite sprite, float baseHeightOfScreen = 0.06f, float seconds = 1.3f)
    {
        if (sprite == null || Camera.main == null) return;
        float s = ScaleFor(sprite, baseHeightOfScreen);
        var frames = new[] { sprite };
        // (先頭からの横のずれ, 縦のずれ, 大きさ)
        var slots = new[] { (0f, 0f, 1.5f), (0.063f, 0.035f, 1.2f), (0.063f, -0.035f, 1.2f), (0.126f, 0.07f, 1.2f) }; // 間隔を約1割詰めた(エステル)
        foreach (var (dx, dy, k) in slots)
            SkyFlyby.Create(frames, new Vector2(1.12f + dx, 0.82f + dy), new Vector2(-0.18f + dx, 0.8f + dy), s * k * 0.9f, s * k, seconds, ShadowTint, false, solid: true);
    }

    // 空中ボスの乱入の予兆: 0 秒 風/羽音 + 大きな影が後ろ(左)から頭上を越えて前(右)へ約 1 秒 → 0.5 秒 WARNING の帯(1.2 秒)+ 短い唸り声 → 1.6 秒 本体が前から飛来
    public static void RaidTelegraph(Sprite sprite, bool artFacesRight, string key, string name, float heightOfScreen = 0.45f)
    {
        if (sprite != null && Camera.main != null)
            SkyFlyby.Create(new[] { sprite }, new Vector2(-0.3f, 0.7f), new Vector2(1.3f, 0.74f), ScaleFor(sprite, heightOfScreen) * 0.95f, ScaleFor(sprite, heightOfScreen) * 1.05f, 1.0f, ShadowTint, !artFacesRight, solid: true);
        SkyBossSfx.Play(SkyBossSfx.Wind(), 0.7f);
        SkyBossSfx.Play(SkyBossSfx.Whoosh(), 0.5f);
        if (Instance != null) Instance.StartCoroutine(Instance.RaidWarnLater(key, name, 0.5f));
    }
    System.Collections.IEnumerator RaidWarnLater(string key, string name, float wait)
    {
        yield return new WaitForSeconds(wait);
        BossBattleHud.Banner($"WARNING  {name} が迫る!", new Color(1f, 0.55f, 0.3f), 1.2f);
        BossVoice.Growl(null, key);
    }

    // ---- EncounterDirector が読む値 ----
    static RoadEventKind DirKind => Active == RoadEventKind.AirRaid ? RoadEventKind.None : Active;
    public static float PaceMul => DirKind == RoadEventKind.Rush ? T.rushPace : DirKind == RoadEventKind.Flying ? T.flyingPace : DirKind == RoadEventKind.Chase ? T.chasePace : 1f;
    public static float Surge => DirKind == RoadEventKind.Rush ? T.rushSurge : -1f;
    public static bool NoRest => DirKind != RoadEventKind.None;
    public static float HardMul => DirKind == RoadEventKind.Elite ? T.eliteHardMul : 1f;
    public static float FormationMul(EncounterFormation f)
    {
        if (DirKind != RoadEventKind.Flying || f == null) return 1f;
        int air = 0;
        foreach (var s in f.slots) if (s != null && (s.kind == EncounterSlotKind.AirLow || s.kind == EncounterSlotKind.AirMiddle || s.kind == EncounterSlotKind.AirHigh)) air++;
        return air > 0 ? T.flyingAirFormationMul : T.flyingGroundOnlyMul;
    }
    public static float EnemyMul(EnemyDefinition d)
    {
        if (d == null) return 1f;
        switch (DirKind)
        {
            case RoadEventKind.Flying: return IsFlyer(d) ? T.flyingEnemyMul : 1f;
            case RoadEventKind.Elite: return IsElite(d) ? T.eliteEnemyMul : 1f;
            case RoadEventKind.Chase: return IsChaser(d) ? T.chaseEnemyMul : 1f;
            default: return 1f;
        }
    }

    static bool IsFlyer(EnemyDefinition d) => d.movementType == EnemyMovementType.Flying;
    static bool IsElite(EnemyDefinition d) => d.hpMultiplier >= T.eliteHpMultiplier && d.hpMultiplier < 5f; // BONUS の宝の敵(5 以上)は除く
    static bool IsChaser(EnemyDefinition d) => d.behaviorKind == EnemyBehaviorKind.Chaser || d.behaviorKind == EnemyBehaviorKind.Rusher;

    // その距離の帯にその種類の敵が居るか / 知らせの名前に使う1体
    static bool HasEnemy(float d, System.Func<EnemyDefinition, bool> pred) => FindEnemy(d, pred) != null;
    static EnemyDefinition FindEnemy(float d, System.Func<EnemyDefinition, bool> pred)
    {
        var dir = EncounterDirector.Instance;
        var band = dir != null && dir.Profile != null ? dir.Profile.BandFor(d) : null;
        if (band == null) return null;
        EnemyDefinition best = null; float bw = 0f;
        foreach (var e in band.enemies)
        {
            if (e == null || e.weight <= 0f) continue;
            var def = EnemyDatabase.FindById(e.enemyId);
            if (def != null && pred(def) && e.weight > bw) { best = def; bw = e.weight; }
        }
        return best;
    }
    static EnemyDefinition SampleEnemy(float d, RoadEventKind k) =>
        k == RoadEventKind.Flying ? FindEnemy(d, IsFlyer) : k == RoadEventKind.Elite ? FindEnemy(d, IsElite) : k == RoadEventKind.Chase ? FindEnemy(d, IsChaser) : null;

    public static string Describe() => Active == RoadEventKind.None ? "-" : $"{Active} {ActiveLabel}";
}
