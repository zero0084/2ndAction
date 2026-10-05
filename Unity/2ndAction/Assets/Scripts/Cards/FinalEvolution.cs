using System.Collections.Generic;
using UnityEngine;

// FINAL EVOLUTION(2026-10-04 第1段階: 共通の仕組み + 代表9枚の試作 / 2026-10-05 第2段階: 再使用 + 全カード)。
// 「Lv10」ではなく、Lv9 MAX の能力が一時的に限界突破する仕組み。カードの保存Lv/Mastery/AWAKENED は一切変えない。
//   資格   … そのランで能力Lvが実際に9(キャラカードで開始時から9でも資格あり)。能力(元のカードの cardId)ごとに数える。
//            合成カード/キャラカード/ラン中の取得は能力ごとに合算済み(GameManager.CardCap.cs)なので、二重に発動しない。
//   READY  … 資格を得た地点から FinalEvolutionTuning.readyMeters(既定5,000m)走る(初回だけ)。HUD のカードの枠が金色に脈動する。
//            READY = 「この能力が FINAL EVOLUTION の抽選に参加できる」(残りの回数ではない)。
//   候補   … 次の通常 LEVEL UP の3択のうち最大1枠(残りは通常の候補)。断っても READY のまま(次の LEVEL UP でまた出られる)。
//            ボス報酬/BONUS ZONE/ULTIMATE には混ぜない。複数 READY なら、候補に出た回数が少ないものから(同数はランダム)。
//   ACTIVE … 選ぶと短い演出(カードの光/画面の縁の光/短いヒットストップ/オーラ)→ すぐ再開。時間型(秒)/距離型(m)。
//   終了   … 通常の Lv9 MAX に戻り、すぐ READY へ戻る(再チャージ/Gauge/距離の待ちなし。第2段階で USED を廃止)。
//            同じ能力が ACTIVE の間だけ、その能力は候補に出ない(同じ FINAL EVOLUTION は重ならない)。別の能力同士は同時に ACTIVE になれる。
//            usesPerRun(既定0=制限なし)は将来の特殊カード用。uses = 発動の回数(制限には使わない)。
//   効果   … FinalEvolutionTuning の各カード = 増幅(そのカード自身の効果×amplify)+追加(EffectType)+代表9枚の専用の処理。
//            増幅/追加は GameManager.RecomputeCardStats が毎回作り直す値に乗るだけ(終われば作り直して何も残らない)。
// 効果は既存の計算の「外側」に一時的に掛ける(Card Balance V3 の通常値・EXP の減衰の曲線・ULTIMATE には触らない)。
// ランの中だけの状態(Game Over / ホームへ戻る / 新しいランで消える)。CONTINUE は RunCheckpoint.Data.finalEvolution で戻す。
public class FinalEvolution : MonoBehaviour
{
    public static FinalEvolution Instance { get; private set; }
    public const string ChoicePrefix = "fe|";

    public enum Stage { None, Eligible, Ready, Active, Used }

    [System.Serializable]
    public class SaveState
    {
        public string id;              // 能力(元のカードの cardId)
        public bool eligible;          // 資格(能力Lv9)を得た
        public float eligibleAt;       // 資格を得た距離(m)
        public bool ready, active;
        public int uses;
        public float remaining;        // ACTIVE の残り(秒 / m)
        public int offers;             // LEVEL UP の候補に出た回数(複数 READY の公平さ)
        public bool spent;             // この ACTIVE の間の1回きりの効果を使った(PHOENIX の緊急復活。CONTINUE で戻らない。古い保存は false)
    }

    readonly Dictionary<string, SaveState> states = new Dictionary<string, SaveState>();
    float lastDistance = -1f;
    float activatedAtUnscaled = -99f, endedAtUnscaled = -99f;
    string lastActivated = "", lastEnded = "";
    bool lastWasAwakened;
    int bloodShield;
    bool phoenixToken;
    float rangeApplied = 1f;
    float autoHitTimer;
    readonly Dictionary<Component, float> autoHitOn = new Dictionary<Component, float>();
    readonly Dictionary<Component, float> spreadOn = new Dictionary<Component, float>();

    // 確認用(自動テスト)
    public static int Activations, Ends, EmergencyRevives, BloodShieldBlocks, AutoHits, SpreadBurns, SlashWaves, ContactGuards;
    public static bool DebugForceAwakened;   // FINAL EVOLUTION TEST: AWAKENED の扱いを試す(保存は変えない)
    public static event System.Action<string, bool> Activated; // 専用SE等のフック(能力, AWAKENED)

    static FinalEvolutionTuning T => FinalEvolutionTuning.I;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[FinalEvolution]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<FinalEvolution>();
    }

    // ===================================================================== //
    // 状態
    // ===================================================================== //
    SaveState St(string id)
    {
        if (!states.TryGetValue(id, out var s)) { s = new SaveState { id = id }; states[id] = s; }
        return s;
    }

    public static Stage StageOf(string abilityId)
    {
        if (Instance == null || abilityId == null || !Instance.states.TryGetValue(abilityId, out var s)) return Stage.None;
        if (s.active) return Stage.Active;
        if (s.ready) return Stage.Ready;
        if (T.usesPerRun > 0 && s.uses >= T.usesPerRun) return Stage.Used; // 通常は制限なし(将来の特殊カード用)
        return s.eligible ? Stage.Eligible : Stage.None;
    }
    public static bool IsActive(string abilityId) => StageOf(abilityId) == Stage.Active;
    public static float Remaining(string abilityId) => Instance != null && Instance.states.TryGetValue(abilityId, out var s) ? s.remaining : 0f;
    public static float EligibleAt(string abilityId) => Instance != null && Instance.states.TryGetValue(abilityId, out var s) && s.eligible ? s.eligibleAt : -1f;
    public static int Uses(string abilityId) => Instance != null && Instance.states.TryGetValue(abilityId, out var s) ? s.uses : 0;
    public static bool Supported(string abilityId) => T.For(abilityId) != null;
    static float P(string id) { var e = T.For(id); return e != null ? e.power : 1f; }
    static float P2(string id) { var e = T.For(id); return e != null ? e.power2 : 0f; }
    public static bool IsAwakenedFor(string id) => DebugForceAwakened || CardProgression.IsAwakened(id);
    public static bool AnyActive { get { if (Instance == null) return false; foreach (var s in Instance.states.Values) if (s.active) return true; return false; } }

    // 新しいラン(GameManager.ResetCardStatsForRun から)
    public void ResetRun()
    {
        if (AnyActiveRaw()) foreach (var s in states.Values) if (s.active) RemoveEffects(s.id);
        states.Clear();
        lastDistance = -1f;
        bloodShield = 0; phoenixToken = false;
        autoHitOn.Clear(); spreadOn.Clear();
        lastActivated = lastEnded = "";
        SetRangeFactor(1f);
        CameraFollow.FinalEvolutionZoom = 1f;
        SonicMoveFx.ForcedIntensity = 0f;
    }
    bool AnyActiveRaw() { foreach (var s in states.Values) if (s.active) return true; return false; }

    // ===================================================================== //
    // 毎フレーム: 資格 → READY、ACTIVE の残り
    // ===================================================================== //
    void Update()
    {
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted) { lastDistance = -1f; return; }
        if (gm.IsGameOver) return;
        float d = gm.MaxDistance;
        float dd = lastDistance >= 0f ? Mathf.Max(0f, d - lastDistance) : 0f;
        if (dd > 400f) dd = 0f; // ワープ(開発用)の飛び
        lastDistance = d;
        int limit = T.usesPerRun; // 0 = 制限なし
        foreach (var e in T.entries)
        {
            if (e == null || string.IsNullOrEmpty(e.abilityId)) continue;
            var s = St(e.abilityId);
            if (s.active)
            {
                s.remaining -= e.kind == FinalEvolutionTuning.Kind.Time ? Time.deltaTime : dd;
                if (s.remaining <= 0f) End(e.abilityId);
                continue;
            }
            if ((limit > 0 && s.uses >= limit) || s.ready) continue;
            if (!s.eligible)
            {
                if (gm.GetAbilityRunStack(e.abilityId) >= GameManager.MaxRunCardLevel)
                {
                    s.eligible = true;
                    s.eligibleAt = d;
                    Debug.Log($"[FinalEvo] {e.abilityId} eligible at {d:F0}m (Lv9; READY at {d + T.readyMeters:F0}m)");
                }
                continue;
            }
            if (d - s.eligibleAt >= T.readyMeters)
            {
                s.ready = true;
                Debug.Log($"[FinalEvo] {e.abilityId} READY at {d:F0}m");
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Milestone);
            }
        }
        if (IsActive("speed_up")) SpeedAutoHits();
    }

    void LateUpdate() { UpdateAura(); }

    // ===================================================================== //
    // LEVEL UP の候補
    // ===================================================================== //
    public static bool OffersAllowed => !(T.disableInMultiplayer && NetRunLauncher.IsMultiplayerRun);

    // READY の中から1つ(候補に出た回数が少ないものから、同数はランダム)。無ければ null
    public static string PickCandidate()
    {
        if (Instance == null || !OffersAllowed) return null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // FINAL EVOLUTION TEST: 選んだ能力を候補にする(DEBUG RUN の中だけ。READY で ACTIVE でない時だけ)
        if (DebugForceCandidate != null && DebugRun.IsActive && Instance.states.TryGetValue(DebugForceCandidate, out var fs) && fs.ready && !fs.active && T.For(fs.id) != null) { fs.offers++; return fs.id; }
#endif
        var list = new List<SaveState>();
        int best = int.MaxValue;
        foreach (var s in Instance.states.Values)
        {
            if (!s.ready || s.active || T.For(s.id) == null) continue;
            if (s.offers < best) { best = s.offers; list.Clear(); }
            if (s.offers == best) list.Add(s);
        }
        if (list.Count == 0) return null;
        var pick = list[Random.Range(0, list.Count)];
        pick.offers++;
        return pick.id;
    }

    public static bool IsChoiceId(string cardId) => cardId != null && cardId.StartsWith(ChoicePrefix);
    public static string AbilityOfChoice(string cardId) => IsChoiceId(cardId) ? cardId.Substring(ChoicePrefix.Length) : null;

    static readonly Dictionary<string, CardDefinition> choiceCards = new Dictionary<string, CardDefinition>();
    // 3択の器に入れる仮のカード(cardId = "fe|<能力>"。通常のカードの適用の経路には流さない)
    public static CardDefinition ChoiceCard(string abilityId)
    {
        if (choiceCards.TryGetValue(abilityId, out var c) && c != null) return c;
        var src = CardDatabase.FindBaseById(abilityId);
        c = ScriptableObject.CreateInstance<CardDefinition>();
        c.hideFlags = HideFlags.DontSave;
        c.cardId = ChoicePrefix + abilityId;
        c.cardName = src != null ? src.cardName : abilityId;
        c.icon = src != null ? src.icon : null;
        c.rarity = 5;
        c.category = src != null ? src.category : default;
        var e = T.For(abilityId);
        c.description = e != null ? e.description : "";
        choiceCards[abilityId] = c;
        return c;
    }

    public static RewardCardData ChoiceCardData(string abilityId)
    {
        var c = ChoiceCard(abilityId);
        var e = T.For(abilityId);
        float mul = IsAwakenedFor(abilityId) ? 1f + T.awakenedDurationBonus : 1f;
        string dur = e == null ? "" : e.kind == FinalEvolutionTuning.Kind.Time ? $"{e.durationSeconds * mul:0.#}秒間" : $"次の{e.durationMeters * mul:#,0}m";
        return new RewardCardData
        {
            CardId = c.cardId,
            Icon = c.icon,
            Title = c.cardName,
            Description = (e != null ? e.title + "\n" : "") + c.description,
            Rarity = 5,
            LevelLine = "FINAL EVOLUTION",
            Category = c.category,
            ValueLine = dur,
            FinalEvolution = true,
            Awakened = IsAwakenedFor(abilityId),
        };
    }

    // ===================================================================== //
    // 発動 / 終了
    // ===================================================================== //
    public static bool Activate(string abilityId)
    {
        if (Instance == null || abilityId == null) return false;
        var e = T.For(abilityId);
        var s = Instance.St(abilityId);
        if (e == null || s.active || !s.ready) { Debug.LogWarning($"[FinalEvo] activate refused {abilityId} (ready={s.ready} active={s.active})"); return false; }
        bool awake = IsAwakenedFor(abilityId);
        float mul = awake ? 1f + T.awakenedDurationBonus : 1f;
        s.ready = false; s.active = true; s.uses++; s.spent = false;
        s.remaining = (e.kind == FinalEvolutionTuning.Kind.Time ? e.durationSeconds : e.durationMeters) * mul;
        Instance.ApplyEffects(abilityId);
        RecomputeStats(); // 増幅/追加を能力値へ
        Activations++;
        Instance.activatedAtUnscaled = Time.unscaledTime;
        Instance.lastActivated = abilityId;
        Instance.lastWasAwakened = awake;
        Debug.Log($"[FinalEvo] ACTIVATE {abilityId} ({e.kind} {s.remaining:F0}{(e.kind == FinalEvolutionTuning.Kind.Time ? "s" : "m")}) awakened={awake} uses={s.uses}");
        BossBattleHud.Banner("FINAL EVOLUTION  " + (CardDatabase.FindBaseById(abilityId)?.cardName ?? abilityId), awake ? new Color(1f, 0.9f, 0.45f) : new Color(1f, 0.72f, 0.3f), 1.5f);
        if (GameManager.Instance != null) GameManager.Instance.StartCoroutine(HitStop.Freeze(0.08f));
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.LevelUp);
        Activated?.Invoke(abilityId, awake);
        return true;
    }

    void End(string abilityId)
    {
        var s = St(abilityId);
        if (!s.active) return;
        s.active = false; s.remaining = 0f; s.spent = false;
        // 第2段階: すぐ READY へ戻る(再チャージなし)。将来の特殊カードで回数を決めた時だけ、使い切ったら戻らない
        s.ready = T.usesPerRun <= 0 || s.uses < T.usesPerRun;
        RemoveEffects(abilityId);
        RecomputeStats(); // 増幅/追加を外して作り直す(何も残らない)
        Ends++;
        endedAtUnscaled = Time.unscaledTime;
        lastEnded = abilityId;
        Debug.Log($"[FinalEvo] END {abilityId} -> back to Lv9 MAX, {(s.ready ? "READY again" : "no more uses")} (activations {s.uses})");
        var pc = PlayerController.Instance;
        if (pc != null && IsAwakenedFor(abilityId))
            OneShotSpriteEffect.CreateTweened(OneShotSpriteEffect.SoftDotSprite(), pc.transform.position + Vector3.up * 0.9f, new Color(1f, 0.9f, 0.5f, 0.9f), 0.45f, 0.6f, 3.4f, -1f, 0f); // AWAKENED: 終わりの光
    }

    static void RecomputeStats()
    {
        var gm = GameManager.Instance;
        if (gm != null && gm.HasStarted) gm.RecomputeCardStats();
    }

    // ---- 増幅 / 追加(GameManager.RecomputeCardStats から) ----
    // そのカード自身の効果の倍率(ACTIVE の間だけ。1 = 増幅しない)
    public static float Amplify(string abilityId)
    {
        if (Instance == null || abilityId == null || !Instance.states.TryGetValue(abilityId, out var s) || !s.active) return 1f;
        var e = T.For(abilityId);
        return e != null && e.amplify > 0f ? e.amplify : 1f;
    }
    // ACTIVE の能力の「追加」の合計(種類ごと)
    public static float BonusOf(EffectType t)
    {
        if (Instance == null) return 0f;
        float sum = 0f;
        foreach (var s in Instance.states.Values)
        {
            if (!s.active) continue;
            var e = T.For(s.id);
            if (e == null || e.bonuses == null) continue;
            foreach (var b in e.bonuses) if (b != null && b.type == t) sum += b.value;
        }
        return sum;
    }
    // 最大HP(ハート)以外の追加を能力値の合計へ
    public static void AddBonuses(CardTotals totals)
    {
        if (Instance == null || totals == null) return;
        foreach (var s in Instance.states.Values)
        {
            if (!s.active) continue;
            var e = T.For(s.id);
            if (e == null || e.bonuses == null) continue;
            foreach (var b in e.bonuses) if (b != null && b.type != EffectType.MaxHpHearts && b.type != EffectType.SacrificeHearts) totals.Add(b.type, b.value);
        }
    }
    public static int ActiveCount { get { if (Instance == null) return 0; int n = 0; foreach (var s in Instance.states.Values) if (s.active) n++; return n; } }

    void ApplyEffects(string id)
    {
        switch (id)
        {
            case "attack_range_up": SetRangeFactor(P(id)); break;
            case "speed_up": CameraFollow.FinalEvolutionZoom = 1.08f; SonicMoveFx.ForcedIntensity = 1f; break;
            case "phoenix": phoenixToken = true; break;
            case "vampire": bloodShield = 0; break;
        }
    }

    void RemoveEffects(string id)
    {
        switch (id)
        {
            case "attack_range_up": SetRangeFactor(1f); break;
            case "speed_up": CameraFollow.FinalEvolutionZoom = 1f; SonicMoveFx.ForcedIntensity = 0f; autoHitOn.Clear(); break;
            case "phoenix": phoenixToken = false; break;             // 使わなかった専用の復活は消える
            case "vampire": bloodShield = 0; break;                  // 余った Blood Shield は残さない(通常の Shield にもしない)
            case "flame_blade": spreadOn.Clear(); break;             // 延焼の間隔の記録も残さない(再使用で溜まらない)
        }
    }

    void SetRangeFactor(float f)
    {
        var pc = PlayerController.Instance;
        if (pc != null) pc.ApplyFinalEvolutionRange(f / Mathf.Max(0.01f, rangeApplied));
        rangeApplied = f;
    }

    // ===================================================================== //
    // 各能力の効果(ゲーム側の1か所から読む)
    // ===================================================================== //
    // ATTACK UP: 最終ダメージの倍率(攻撃の枠 AttackPct は変えない)
    public static float AttackMul => IsActive("attack_up") ? P("attack_up") : 1f;

    // SPEED UP: 実速度の倍率。速くしすぎない(speedCapKmh まで。元からそれより速ければ上げない)
    public static float SpeedFactor(float baseMps)
    {
        if (!IsActive("speed_up") || baseMps <= 0.01f) return 1f;
        float cap = T.speedCapKmh / GameManager.KmhPerMps;
        float target = Mathf.Min(baseMps * P("speed_up"), Mathf.Max(baseMps, cap));
        return target / baseMps;
    }

    // EXP UP: EXP の枠へ足す(曲線の前)
    public static float ExpBucketBonus => IsActive("exp_up") ? P("exp_up") : 0f;
    // GREED: MILE の倍率 / 受けるダメージの倍率
    public static float MileMul => IsActive("greed") ? P("greed") : 1f;
    public static float DamageTakenMul => IsActive("greed") ? Mathf.Max(1f, P2("greed")) : 1f;
    // VAMPIRE: 吸収の確率の追加
    public static float LifestealChanceAdd => IsActive("vampire") ? P("vampire") : 0f;
    public static int BloodShield => Instance != null ? Instance.bloodShield : 0;
    // FLAME BLADE / THUNDER STRIKE
    public static float BurnChanceAdd => IsActive("flame_blade") ? P("flame_blade") : 0f;
    public static float BurnDpsMul => IsActive("flame_blade") ? Mathf.Max(1f, P2("flame_blade")) : 1f;
    public static float LightningChanceAdd => IsActive("thunder_strike") ? P("thunder_strike") : 0f;
    public static int LightningChainsAdd => IsActive("thunder_strike") ? Mathf.RoundToInt(P2("thunder_strike")) : 0;
    public static bool PhoenixTokenReady => Instance != null && Instance.phoenixToken && IsActive("phoenix");

    // VAMPIRE: 満タンで溢れた回復を Blood Shield(上限あり)へ
    public static void OnOverheal()
    {
        if (Instance == null || !IsActive("vampire")) return;
        int cap = Mathf.Max(0, Mathf.RoundToInt(P2("vampire")));
        if (Instance.bloodShield < cap) Instance.bloodShield++;
    }

    // 被弾の入口(PlayerController.TakeDamage)。true = この被弾は受けない
    public static bool InterceptDamage(string source, ref int amount)
    {
        if (Instance == null || !AnyActive) return false;
        string src = source ?? "";
        // SPEED UP: 接触(敵の体)と障害物から守る
        if (IsActive("speed_up") && (src.StartsWith("Enemy:") || src.StartsWith("Obstacle:"))) { ContactGuards++; return true; }
        // VAMPIRE: Blood Shield が1回分を受ける
        if (IsActive("vampire") && Instance.bloodShield > 0)
        {
            Instance.bloodShield--;
            BloodShieldBlocks++;
            var pc = PlayerController.Instance;
            if (pc != null) OneShotSpriteEffect.CreateTweened(OneShotSpriteEffect.SoftDotSprite(), pc.transform.position + Vector3.up * 0.9f, new Color(0.9f, 0.1f, 0.2f, 0.9f), 0.3f, 0.6f, 2f, -1f, 0f);
            return true;
        }
        // GREED: 受けるダメージが増える(リスク)
        float m = DamageTakenMul;
        if (m > 1.001f) amount = Mathf.Max(1, Mathf.CeilToInt(amount * m));
        return false;
    }

    // PHOENIX: 間に1回だけ、致死の被弾から専用の緊急復活(通常の Charge には触らない)
    public static bool TryEmergencyRevive(GameManager gm)
    {
        if (!PhoenixTokenReady || gm == null) return false;
        Instance.phoenixToken = false;
        Instance.St("phoenix").spent = true;
        EmergencyRevives++;
        Debug.Log("[FinalEvo] PHOENIX emergency revive (normal PHOENIX charge untouched)");
        BossBattleHud.Banner("FINAL EVOLUTION: REBIRTH", new Color(1f, 0.6f, 0.2f), 1.4f);
        if (PlayerController.Instance != null) CardProcs.PhoenixBurst(PlayerController.Instance.transform.position);
        return true;
    }
    public static float EmergencyReviveHpFraction => Mathf.Clamp(P("phoenix"), 0.1f, 1f);

    // FLAME BLADE: 炎上させた相手の周りへ小さく延焼(延焼からはさらに延焼しない/同じ相手は1秒に1回/proc の枠を使う)
    public static void OnBurnApplied(Component victim, float dps, float duration)
    {
        if (Instance == null || !IsActive("flame_blade") || victim == null || spreading) return;
        float now = Time.time;
        if (Instance.spreadOn.TryGetValue(victim, out float t) && now - t < 1f) return;
        if (Instance.spreadOn.Count > 64) Instance.spreadOn.Clear();
        Instance.spreadOn[victim] = now;
        spreading = true;
        try
        {
            spreadBuf.Clear();
            var col = victim.GetComponentInChildren<Collider2D>();
            Vector3 at = col != null ? col.bounds.center : victim.transform.position;
            CardProcs.CollectTargets(at, 2.6f, spreadBuf);
            int n = 0;
            foreach (var o in spreadBuf)
            {
                if (o == victim || n >= 2 || !CardProcs.TakeProcBudget()) continue;
                if (!ElementSystem.IsAlive(o)) continue;
                ElementStatus.For(o).AddBurn(dps * 0.6f, duration * 0.6f);
                SpreadBurns++; n++;
                if (CardProcs.TakeFxBudget()) OneShotSpriteEffect.CreateTweened(OneShotSpriteEffect.SoftDotSprite(), at, new Color(1f, 0.4f, 0.1f, 0.8f), 0.25f, 0.4f, 1.6f, -1f, 0f);
            }
        }
        finally { spreading = false; }
    }
    static bool spreading;
    static readonly List<Component> spreadBuf = new List<Component>();

    // ATTACK RANGE UP: 命中時に先端から短い斬撃波(斬撃波自身からは出さない/0.3秒に1回/proc の枠)
    public static void OnPlayerHit(Component victim, PlayerAttackInfo info)
    {
        if (!IsActive("attack_range_up") || (info != null && info.elementProc)) return;
        var pc = PlayerController.Instance;
        if (pc == null || Time.time - lastSlash < 0.3f || !CardProcs.TakeProcBudget()) return;
        lastSlash = Time.time;
        float dir = pc.FacingSign;
        const float speed = 24f, reach = 7f;
        float scale = Mathf.Clamp(P2("attack_range_up"), 0.05f, 1f);
        Vector3 pos = pc.transform.position + new Vector3(dir * 1.4f, 0.9f, 0f);
        var proj = KitProjectile.Create(KitArt.WhiteSprite(), pos, new Vector2(dir * speed, 0f), reach / speed,
            new Vector2(1.1f, 0.22f), new Vector2(1.2f, 0.8f), new Color(0.75f, 0.95f, 1f, 0.9f), PlayerAttackKind.Normal,
            scale, 0.25f, 0f, new Color(0.7f, 0.95f, 1f, 0.5f));
        proj.name = "FinalEvoSlashWave";
        proj.pierce = 2;
        var pi = proj.GetComponent<PlayerAttackInfo>();
        if (pi != null) { pi.elementProc = true; pi.seqTag = AttackSeqTag.None; pi.seqMoveId = 0; }
        SlashWaves++;
    }
    static float lastSlash = -9f;

    // SPEED UP: 接敵した敵へ自動の小攻撃(静かなダメージ。1体0.5秒に1回、1回に3体まで)
    void SpeedAutoHits()
    {
        autoHitTimer -= Time.deltaTime;
        if (autoHitTimer > 0f) return;
        autoHitTimer = 0.12f;
        var pc = PlayerController.Instance;
        if (pc == null || (NetRunLauncher.IsMultiplayerRun && !NetCombat.Authority)) return;
        spreadBuf.Clear();
        Vector3 c = pc.transform.position + new Vector3(pc.FacingSign * 1.1f, 0.9f, 0f);
        CardProcs.CollectTargets(c, new Vector2(2.4f, 2.2f), spreadBuf);
        int n = 0; float now = Time.time;
        int dmg = Mathf.Max(1, Mathf.RoundToInt(pc.EffectiveAttackPower * Mathf.Clamp(P2("speed_up"), 0.05f, 1f)));
        foreach (var o in spreadBuf)
        {
            if (n >= 3 || !ElementSystem.IsAlive(o)) continue;
            if (autoHitOn.TryGetValue(o, out float t) && now - t < 0.5f) continue;
            if (autoHitOn.Count > 64) autoHitOn.Clear();
            autoHitOn[o] = now;
            ElementSystem.DealQuiet(o, dmg, ElementType.None);
            AutoHits++; n++;
        }
    }

    // ===================================================================== //
    // 見た目(仮): オーラ / 画面の縁の光 / カードの光
    // ===================================================================== //
    SpriteRenderer aura;
    void UpdateAura()
    {
        var pc = PlayerController.Instance;
        string act = null;
        foreach (var s in states.Values) if (s.active) { act = s.id; break; }
        if (act == null || pc == null) { if (aura != null) aura.enabled = false; return; }
        if (aura == null)
        {
            var go = new GameObject("FinalEvoAura");
            go.transform.SetParent(transform, false);
            aura = go.AddComponent<SpriteRenderer>();
            aura.sprite = OneShotSpriteEffect.SoftDotSprite();
            aura.sortingOrder = RenderOrder.Player - 1;
        }
        var e = T.For(act);
        bool awake = IsAwakenedFor(act);
        Color col = e != null ? e.aura : Color.white;
        if (awake) col = Color.Lerp(col, new Color(1f, 0.85f, 0.35f), 0.45f); // AWAKENED: 金のオーラ
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (awake ? 7f : 5f));
        col.a = (awake ? 0.62f : 0.5f) + 0.18f * pulse;
        aura.color = col;
        aura.enabled = true;
        aura.transform.position = pc.transform.position + new Vector3(0f, 0.9f, 0.1f);
        Vector2 b = aura.sprite.bounds.size;
        float size = (awake ? 3.4f : 3.0f) + 0.3f * pulse;
        aura.transform.localScale = new Vector3(size / Mathf.Max(0.01f, b.x), size * 1.15f / Mathf.Max(0.01f, b.y), 1f);
    }

    static Texture2D white;
    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        float t = Time.unscaledTime - activatedAtUnscaled;
        if (t < 0f || t > 0.7f) return;
        if (white == null) { white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave }; white.SetPixel(0, 0, Color.white); white.Apply(); }
        var e = T.For(lastActivated);
        Color c = e != null ? e.aura : new Color(1f, 0.7f, 0.3f);
        if (lastWasAwakened) c = Color.Lerp(c, new Color(1f, 0.88f, 0.4f), 0.5f);
        float a = (1f - t / 0.7f);
        float w = Screen.width, h = Screen.height, th = Mathf.Min(w, h) * 0.05f;
        for (int i = 0; i < 6; i++)
        {
            float k = th * (1f - i / 6f);
            GUI.color = new Color(c.r, c.g, c.b, a * 0.16f);
            GUI.DrawTexture(new Rect(0, 0, w, k), white); GUI.DrawTexture(new Rect(0, h - k, w, k), white);
            GUI.DrawTexture(new Rect(0, 0, k, h), white); GUI.DrawTexture(new Rect(w - k, 0, k, h), white);
        }
        GUI.color = Color.white;
    }

    public static float ActivatedAgo => Instance != null ? Time.unscaledTime - Instance.activatedAtUnscaled : 99f;
    public static string LastActivated => Instance != null ? Instance.lastActivated : "";

    // ===================================================================== //
    // CONTINUE
    // ===================================================================== //
    public static List<SaveState> Export()
    {
        var list = new List<SaveState>();
        if (Instance == null) return list;
        foreach (var s in Instance.states.Values)
            if (s.eligible || s.uses > 0 || s.ready || s.active)
                list.Add(new SaveState { id = s.id, eligible = s.eligible, eligibleAt = s.eligibleAt, ready = s.ready, active = s.active, uses = s.uses, remaining = s.remaining, offers = s.offers, spent = s.spent });
        return list;
    }

    public static void Import(List<SaveState> list)
    {
        if (Instance == null) return;
        Instance.ResetRun();
        if (list == null) return;
        foreach (var s in list)
        {
            if (s == null || string.IsNullOrEmpty(s.id)) continue;
            // 第1段階の保存(USED = 使い終わって READY でない)は、再使用の仕様では READY に戻す(資格と初回の距離は済んでいる)
            bool readyNow = s.ready || (!s.active && s.eligible && s.uses > 0 && (T.usesPerRun <= 0 || s.uses < T.usesPerRun));
            Instance.states[s.id] = new SaveState { id = s.id, eligible = s.eligible, eligibleAt = s.eligibleAt, ready = readyNow && !s.active, active = s.active, uses = s.uses, remaining = s.remaining, offers = s.offers, spent = s.active && s.spent };
            if (s.active)
            {
                Instance.ApplyEffects(s.id); // ResetRun の後なので二重にはならない
                if (s.id == "phoenix" && s.spent) Instance.phoenixToken = false; // 使った緊急復活は CONTINUE で戻らない
            }
        }
        RecomputeStats(); // ACTIVE の増幅/追加を能力値へ(CONTINUE のカードの取り直しの後)
        Debug.Log($"[FinalEvo] restored {list.Count} state(s) for CONTINUE");
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // FINAL EVOLUTION TEST / 自動テスト用
    public static string DebugForceCandidate; // FINAL EVOLUTION TEST: この能力を候補にする(null = 通常の公平な抽選)
    public static void DebugSetEligibleAt(string id, float d) { if (Instance != null) { var s = Instance.St(id); s.eligible = true; s.eligibleAt = d; } }
    public static void DebugMakeReady(string id) { if (Instance == null) return; var s = Instance.St(id); if (!s.eligible && GameManager.Instance != null) { s.eligible = true; s.eligibleAt = GameManager.Instance.MaxDistance - T.readyMeters; } s.ready = true; }
    public static void DebugEnd(string id) { if (Instance != null) Instance.End(id); }
    public static int DebugListenerCount => Activated == null ? 0 : Activated.GetInvocationList().Length;
    public static int DebugTrackedTargets => Instance == null ? 0 : Instance.autoHitOn.Count + Instance.spreadOn.Count;
    public static void DebugSetRemaining(string id, float r) { if (Instance != null) Instance.St(id).remaining = r; }
#endif
}
