using System.Collections.Generic;
using UnityEngine;

// BONUS ZONE(2026-09-29) - 全ステージ共通のボーナス区画。
//
// 流れ: ボス撃破(ボス報酬の選択まで終わった)→ 安全区間 → 確率でBONUS ZONE → 「BONUS ZONE!」(走行は止めない)
//       → 報酬Enemyのwaveを流す(EncounterDirectorがBonus用のFormationを置く)→ 時間/距離で終了
//       → 「BONUS END」+ 残った報酬Enemyは逃げる/消える → BONUS RESULT(1〜2秒)→ 安全距離 → 通常Encounterへ戻る
// ・BONUS中(開始〜安全距離まで)は通常Encounter/壁/障害物/上ルートの敵/ボスの開始を止める(SuppressesNormalSpawns)。
// ・報酬は既存のRun Progressionへ: MILE=仮取得MILE(GameManager.AddRunBonusMile)、EXP=通常のEXP(GrantBonusExp)、
//   Card=既存の3枚Card Choiceを順番待ちへ1件追加(GrantBonusCardChoice)。GAME OVERなら仮取得MILEは通常どおり失う。
// ・マルチ: 報酬の同期は未対応のため自然発生させない(profile.allowInMultiplayer)。報酬は必ずRewardMile/Exp/Cardを通すので、
//   後から「報酬を受ける端末へ送る」形にできる。
// GameManagerと同じObjectに付く(シーン再読込=次Run/Homeで作り直される)。
public class BonusZone : MonoBehaviour
{
    public static BonusZone Instance { get; private set; }
    public enum Phase { Idle, Intro, Active, Ending, PostRest }

    public BonusZoneProfile Profile { get; private set; }
    public Phase State { get; private set; } = Phase.Idle;
    public BonusEncounterType Current { get; private set; }
    public bool IsJackpot { get; private set; }
    public int BonusMile { get; private set; }
    public float BonusExp { get; private set; }
    public int BonusCards { get; private set; }
    public float Elapsed { get; private set; }
    public float StartDistance { get; private set; }
    public float EndDistance { get; private set; }
    public int ZonesStarted { get; private set; }
    public int FairiesThisZone { get; private set; }
    public float RemainingSeconds => Profile == null ? 0f : Mathf.Max(0f, Profile.durationSeconds - Elapsed);
    public float RemainingDistance => Profile == null || GameManager.Instance == null ? 0f : Mathf.Max(0f, StartDistance + Profile.durationDistance - GameManager.Instance.MaxDistance);

    // 通常の敵の出現/ボスの開始を止めるか(Director・壁・障害物・上ルートの敵・BossManagerが見る)
    public static bool SuppressesNormalSpawns => Instance != null && Instance.State != Phase.Idle;
    // ボスの開始を待たせるか(区画の本体の間だけ。終了の表示/安全距離の間はボスを優先する)
    public bool BlocksBoss => State == Phase.Intro || State == Phase.Active;
    // Bonus用のwaveを出してよいか(開始〜終了まで)
    public bool SpawningAllowed => State == Phase.Intro || State == Phase.Active;

    readonly List<BonusEnemy> enemies = new List<BonusEnemy>();
    int waveIndex;
    float phaseTime;
    bool wasBossPhase;
    float pendingAtDistance = -1f;
    static System.Random rng = new System.Random();

    void Awake()
    {
        Instance = this;
        Profile = BonusZoneProfile.Load();
    }
    void OnDestroy() { if (Instance == this) Instance = null; RestoreBgm(); }

    // ===================================================================== //
    // 開始/終了
    // ===================================================================== //
    void Update()
    {
        var gm = GameManager.Instance;
        if (gm == null || Profile == null) return;
        if (!gm.HasStarted) return;
        if (gm.IsGameOver) { if (State != Phase.Idle) Abort(); return; }

        // ボス区間の終わり(ボス報酬の選択まで済んだ)を見て、安全区間の後に抽選する
        var bm = BossManager.Instance;
        bool boss = bm != null && bm.IsBossPhase;
        if (wasBossPhase && !boss && State == Phase.Idle) pendingAtDistance = gm.MaxDistance + Profile.delayAfterBoss;
        wasBossPhase = boss;
        if (pendingAtDistance >= 0f && State == Phase.Idle && gm.MaxDistance >= pendingAtDistance)
        {
            pendingAtDistance = -1f;
            if (CanStartNaturally(gm) && Rand() < Profile.chanceAfterBoss) Begin(null, false);
        }

        float dt = Time.deltaTime; // Level Up選択/ポーズ中は止まる(残り時間が減らない)
        phaseTime += dt;
        switch (State)
        {
            case Phase.Intro:
                Elapsed += dt;
                if (phaseTime >= Profile.introSeconds) { State = Phase.Active; phaseTime = 0f; }
                break;
            case Phase.Active:
                Elapsed += dt;
                if (Elapsed >= Profile.durationSeconds || gm.MaxDistance >= StartDistance + Profile.durationDistance) End();
                break;
            case Phase.Ending:
                if (phaseTime >= 0.6f + Profile.resultSeconds) { State = Phase.PostRest; phaseTime = 0f; }
                break;
            case Phase.PostRest:
                // 残りの報酬Enemyが居なくなり、安全距離を走ったら通常へ
                // (逃げ遅れた報酬Enemyが居ても、少し経てば片付けて通常へ戻す)
                if (phaseTime > 6f) foreach (var e in enemies) if (e != null && e.isActiveAndEnabled) e.gameObject.SetActive(false);
                if (gm.MaxDistance >= EndDistance + Profile.postRestDistance && ActiveEnemyCount() == 0)
                {
                    State = Phase.Idle;
                    Debug.Log($"[BONUS] back to normal encounters at {gm.MaxDistance:F0}m");
                }
                break;
        }
        // 次のボスが近づいたら区画を先に終える(ボスと重ねない。Debugで強制した時など)
        if ((State == Phase.Intro || State == Phase.Active) && bm != null && bm.enabled && bm.NextBossDistance > 0f)
        {
            float toBoss = bm.NextBossDistance - gm.MaxDistance;
            if (toBoss >= 0f && toBoss < 60f) End();
        }
        // 開始直後(0.6秒)は、同じフレームに置かれた/分岐の中身として後から出た通常の敵も片付ける
        if (State == Phase.Intro && phaseTime < 0.6f) ClearNormalEnemiesAhead();
        if (State == Phase.Intro || State == Phase.Active) UpdateBgm(true);
    }

    // 自動テスト(batchmode)では自然発生させない(既存のテストの前提=ボス後に通常の敵/障害物が続く、を変えない)。
    // BONUS ZONEのテストだけがtrueにする。実機/Editorでの通常プレイは常に自然発生する。
    public static bool AllowNaturalInBatch;

    bool CanStartNaturally(GameManager gm)
    {
        if (Application.isBatchMode && !AllowNaturalInBatch) return false;
        if (NetMatch.Active && !Profile.allowInMultiplayer) return false;
        var bm = BossManager.Instance;
        if (bm != null && bm.NextBossDistance > 0f && bm.NextBossDistance - gm.MaxDistance < Profile.minDistanceToNextBoss) return false;
        var pc = PlayerController.Instance;
        return pc != null && !pc.IsFinishing;
    }

    // Debug/テスト用: 種類を指定して今すぐ始める(nullなら抽選)。
    public bool Force(string typeId = null)
    {
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted || gm.IsGameOver || Profile == null) return false;
        if (NetMatch.Active && !Profile.allowInMultiplayer) return false; // マルチは報酬の同期が未対応(Debugでも始めない)
        if (State != Phase.Idle) Abort();
        return Begin(typeId, true);
    }

    bool Begin(string typeId, bool forced)
    {
        var gm = GameManager.Instance;
        BonusEncounterType t = !string.IsNullOrEmpty(typeId) ? Profile.FindType(typeId) : RollType();
        if (t == null || t.waves.Count == 0) return false;
        Current = t;
        IsJackpot = t.id == Profile.jackpotTypeId;
        State = Phase.Intro; phaseTime = 0f; Elapsed = 0f; waveIndex = 0;
        BonusMile = 0; BonusExp = 0f; BonusCards = 0; FairiesThisZone = 0;
        StartDistance = gm.MaxDistance;
        ZonesStarted++;
        ClearNormalEnemiesAhead();
        introFlash = 1f;
        PlayChime(IsJackpot);
        Debug.Log($"[BONUS] start {t.id}{(IsJackpot ? " (JACKPOT)" : "")}{(forced ? " (forced)" : "")} at {StartDistance:F0}m");
        return true;
    }

    // 種類の抽選: 低確率でJACKPOT、それ以外は重み付き(テストでは開始せずに抽選だけ確かめる)
    public BonusEncounterType RollType()
    {
        BonusEncounterType t = null;
        if (Rand() < Profile.jackpotChance) t = Profile.FindType(Profile.jackpotTypeId);
        return t ?? PickType();
    }

    BonusEncounterType PickType()
    {
        float total = 0f;
        foreach (var t in Profile.types) if (t != null && t.id != Profile.jackpotTypeId && t.weight > 0f) total += t.weight;
        if (total <= 0f) return null;
        float r = Rand() * total;
        foreach (var t in Profile.types)
        {
            if (t == null || t.id == Profile.jackpotTypeId || t.weight <= 0f) continue;
            if (r < t.weight) return t;
            r -= t.weight;
        }
        return null;
    }

    public void End()
    {
        if (State != Phase.Intro && State != Phase.Active) return;
        var gm = GameManager.Instance;
        State = Phase.Ending; phaseTime = 0f;
        EndDistance = gm != null ? gm.MaxDistance : 0f;
        foreach (var e in enemies) if (e != null && e.isActiveAndEnabled) e.Leave();
        RestoreBgm();
        PlayEndSe();
        Debug.Log($"[BONUS] end {Current?.id}: MILE +{BonusMile} EXP +{BonusExp:F0} CARD +{BonusCards} ({Elapsed:F1}s, {EndDistance - StartDistance:F0}m)");
    }

    // Run終了/Game Over: 何も残さず止める
    void Abort()
    {
        foreach (var e in enemies) if (e != null) e.Leave();
        State = Phase.Idle; Current = null;
        RestoreBgm();
    }

    // 開始の瞬間、前方で待っている通常の敵を片付ける(BONUS中に通常の敵に襲われない)。
    // 目の前(6m以内)の敵だけは残す(急に消えると不自然、すぐ通り過ぎる)。
    void ClearNormalEnemiesAhead()
    {
        var pc = PlayerController.Instance;
        if (pc == null) return;
        float px = pc.transform.position.x;
        int n = 0;
        foreach (var ec in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
        {
            if (ec == null || ec.bonus != null || !ec.gameObject.activeInHierarchy) continue;
            if (ec.transform.position.x > px + 6f) { ec.gameObject.SetActive(false); n++; }
        }
        if (n > 0) Debug.Log($"[BONUS] cleared {n} normal enemies waiting ahead");
    }

    // ===================================================================== //
    // EncounterDirectorから: 次に置くFormation
    // ===================================================================== //
    public EncounterFormation NextFormation(out Vector2 gap)
    {
        gap = Current != null ? Current.waveGap : new Vector2(8f, 12f);
        if (Current == null || Current.waves.Count == 0) return null;
        string id = Current.waves[waveIndex % Current.waves.Count];
        waveIndex++;
        return Profile.FindFormation(id);
    }

    public void RegisterEnemy(BonusEnemy e) { if (e != null && !enemies.Contains(e)) enemies.Add(e); }
    public void UnregisterEnemy(BonusEnemy e) { enemies.Remove(e); }
    public int ActiveEnemyCount() { int n = 0; foreach (var e in enemies) if (e != null && e.isActiveAndEnabled) n++; return n; }
    public IReadOnlyList<BonusEnemy> Enemies => enemies;

    public bool TryReserveFairy()
    {
        if (Profile == null || FairiesThisZone >= Profile.maxFairiesPerZone || !SpawningAllowed) return false;
        FairiesThisZone++;
        return true;
    }

    // ===================================================================== //
    // 報酬(既存のRun Progressionへ)
    // ===================================================================== //
    public int RewardMile(int amount, Vector3 at, BonusEnemy src, bool kill = false)
    {
        var gm = GameManager.Instance;
        if (gm == null || amount <= 0) return 0;
        float mul = Current != null ? Current.mileMultiplier : 1f;
        int got = gm.AddRunBonusMile(Mathf.Max(1, Mathf.RoundToInt(amount * mul)));
        if (got <= 0) return 0;
        BonusMile += got;
        Popup(at, $"+{got} MILE", new Color(1f, 0.86f, 0.3f), src, false, got, kill);
        PlayCoin();
        return got;
    }

    public float RewardExp(float amount, Vector3 at, BonusEnemy src)
    {
        var gm = GameManager.Instance;
        if (gm == null || amount <= 0f) return 0f;
        float mul = Current != null ? Current.expMultiplier : 1f;
        float got = gm.GrantBonusExp(amount * mul);
        if (got <= 0f) return 0f;
        BonusExp += got;
        Popup(at, $"+{Mathf.RoundToInt(got)} EXP", new Color(0.55f, 1f, 0.6f), src, false, Mathf.RoundToInt(got));
        PlayExp();
        return got;
    }

    public void RewardCard(Vector3 at, BonusEnemy src)
    {
        var gm = GameManager.Instance;
        if (gm == null || Profile == null) return;
        for (int i = 0; i < Mathf.Max(1, Profile.fairyCardChoices); i++) gm.GrantBonusCardChoice();
        BonusCards += Mathf.Max(1, Profile.fairyCardChoices);
        Popup(at, "CARD CHOICE!", new Color(1f, 0.6f, 1f), src, true);
        cardFlash = 1f;
        PlayChime(false);
    }

    // ===================================================================== //
    // 表示(IMGUI、既存HUDと同じScreen Space)
    // ===================================================================== //
    class Pop { public Vector3 world; public string text; public Color color; public float t; public BonusEnemy src; public int amount; public bool big; public string unit; }
    readonly List<Pop> pops = new List<Pop>();
    const int MaxPops = 8;
    const float PopLife = 0.9f, MergeWindow = 0.45f;
    float introFlash, cardFlash;

    // 同じ敵から短い間に続いた「+N MILE」は1つにまとめる(Multi Hitで数字が何十個も重ならない)
    public void Popup(Vector3 world, string text, Color color, BonusEnemy src, bool big, int amount = 0, bool kill = false)
    {
        string unit = text.EndsWith(" MILE") ? "MILE" : text.EndsWith(" EXP") ? "EXP" : null;
        if (unit != null && src != null && !kill)
        {
            foreach (var p in pops)
                if (p.src == src && p.unit == unit && p.t < MergeWindow)
                {
                    p.amount += amount; p.text = $"+{p.amount} {unit}"; p.t = 0.05f; p.world = world; return;
                }
        }
        if (pops.Count >= MaxPops) pops.RemoveAt(0);
        pops.Add(new Pop { world = world, text = text, color = color, t = 0f, src = src, amount = amount, big = big || kill, unit = unit });
    }

    static Texture2D white;
    static Texture2D White() { if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); white.hideFlags = HideFlags.HideAndDontSave; } return white; }
    static Texture2D edgeTex;
    static Texture2D EdgeTex()
    {
        if (edgeTex == null)
        {
            edgeTex = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int i = 0; i < 64; i++) edgeTex.SetPixel(i, 0, new Color(1f, 1f, 1f, Mathf.Pow(1f - i / 63f, 2f)));
            edgeTex.Apply();
        }
        return edgeTex;
    }

    void OnGUI()
    {
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted || gm.IsOverlayOpen) return;
        float dtu = Time.unscaledDeltaTime;
        if (!gm.IsGameOver && State != Phase.Idle && Current != null)
        {
            DrawEdgeGlow();
            if (State == Phase.Intro) DrawIntroBanner();
            if (State == Phase.Intro || State == Phase.Active) DrawStatusStrip();
            if (State == Phase.Ending) DrawResult();
        }
        DrawPops(dtu);
        if (gm.DebugMode && !gm.IsGameOver) DrawDebug(gm);
        introFlash = Mathf.Max(0f, introFlash - dtu * 1.5f);
        cardFlash = Mathf.Max(0f, cardFlash - dtu * 1.8f);
    }

    Color ThemeColor(float t)
    {
        if (IsJackpot) return Color.Lerp(new Color(1f, 0.85f, 0.3f), Color.HSVToRGB(Mathf.Repeat(t * 0.6f, 1f), 0.6f, 1f), 0.45f); // JACKPOTは金に虹色が巡る
        return Current != null ? Current.color : new Color(1f, 0.84f, 0.3f);
    }

    void DrawEdgeGlow()
    {
        // 画面の左右の端に金色の光(開始直後は強く、区画の間は薄く脈打つ)。中央は空ける。
        float t = Time.unscaledTime;
        float a = State == Phase.Intro ? 0.55f : State == Phase.Active ? 0.16f + 0.06f * Mathf.Sin(t * 3f) : 0.1f;
        a = Mathf.Max(a, introFlash * 0.7f) + cardFlash * 0.4f;
        Color c = cardFlash > 0.01f ? Color.Lerp(ThemeColor(t), new Color(1f, 0.6f, 1f), cardFlash) : ThemeColor(t);
        c.a = a;
        Color prev = GUI.color; GUI.color = c;
        float w = Screen.width * (IsJackpot ? 0.12f : 0.08f);
        GUI.DrawTexture(new Rect(0f, 0f, w, Screen.height), EdgeTex());
        GUIUtility.RotateAroundPivot(180f, new Vector2(Screen.width - w * 0.5f, Screen.height * 0.5f));
        GUI.DrawTexture(new Rect(Screen.width - w, 0f, w, Screen.height), EdgeTex());
        GUI.matrix = Matrix4x4.identity;
        // 開始時の金色の粒(画面の端から上へ)
        if (State == Phase.Intro || introFlash > 0f)
        {
            int n = IsJackpot ? 28 : 16;
            for (int i = 0; i < n; i++)
            {
                float seed = i * 12.9898f;
                float side = (i % 2 == 0) ? Mathf.Repeat(seed * 0.37f, 1f) * w * 1.4f : Screen.width - Mathf.Repeat(seed * 0.53f, 1f) * w * 1.4f;
                float y = Screen.height * (1f - Mathf.Repeat(phaseTime * (0.35f + Mathf.Repeat(seed, 0.3f)) + Mathf.Repeat(seed * 0.11f, 1f), 1f));
                float s = 3f + Mathf.Repeat(seed * 7.7f, 4f);
                Color pc = ThemeColor(t + i * 0.05f); pc.a = 0.85f;
                GUI.color = pc;
                GUI.DrawTexture(new Rect(side, y, s, s), White());
            }
        }
        GUI.color = prev;
    }

    GUIStyle bannerStyle, subStyle, stripStyle, popStyle;
    void EnsureStyles()
    {
        if (bannerStyle != null) return;
        bannerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        subStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        stripStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        popStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };
    }

    static void Outlined(Rect r, string text, GUIStyle st, Color c, float o = 2f)
    {
        Color keep = st.normal.textColor;
        st.normal.textColor = new Color(0f, 0f, 0f, 0.85f * c.a);
        GUI.Label(new Rect(r.x - o, r.y, r.width, r.height), text, st); GUI.Label(new Rect(r.x + o, r.y, r.width, r.height), text, st);
        GUI.Label(new Rect(r.x, r.y - o, r.width, r.height), text, st); GUI.Label(new Rect(r.x, r.y + o, r.width, r.height), text, st);
        st.normal.textColor = c;
        GUI.Label(r, text, st);
        st.normal.textColor = keep;
    }

    void DrawIntroBanner()
    {
        EnsureStyles();
        float k = Mathf.Clamp01(phaseTime / Mathf.Max(0.1f, Profile.introSeconds));
        float pop = k < 0.18f ? Mathf.Lerp(1.5f, 1f, k / 0.18f) : 1f;
        float alpha = k > 0.8f ? Mathf.Clamp01((1f - k) / 0.2f) : 1f;
        float h = Screen.height;
        bannerStyle.fontSize = Mathf.RoundToInt(h * (IsJackpot ? 0.11f : 0.09f) * pop);
        subStyle.fontSize = Mathf.RoundToInt(h * 0.042f);
        Color c = ThemeColor(Time.unscaledTime); c.a = alpha;
        float y = h * 0.24f;
        Outlined(new Rect(0f, y, Screen.width, h * 0.13f), IsJackpot ? "JACKPOT!!" : "BONUS ZONE!", bannerStyle, c, 3f);
        Color w = new Color(1f, 1f, 1f, alpha);
        Outlined(new Rect(0f, y + h * 0.12f, Screen.width, h * 0.06f), (IsJackpot ? "BONUS ZONE  " : "") + Current.displayName, subStyle, w);
    }

    // 区画の間: 画面上の中央(Lv/EXPの下)に小さく「種類・残り・稼ぎ」
    void DrawStatusStrip()
    {
        EnsureStyles();
        float h = Screen.height;
        stripStyle.fontSize = Mathf.Max(12, Mathf.RoundToInt(h * 0.024f));
        Rect safe = Screen.safeArea;
        float top = Screen.height - (safe.y + safe.height) + 28f + 54f + 8f;
        string txt = $"{(IsJackpot ? "JACKPOT" : "BONUS")}  {Current.displayName}   {RemainingSeconds:0}s   MILE +{BonusMile}   EXP +{Mathf.RoundToInt(BonusExp)}{(BonusCards > 0 ? $"   CARD +{BonusCards}" : "")}";
        Vector2 sz = stripStyle.CalcSize(new GUIContent(txt));
        var r = new Rect((Screen.width - sz.x) * 0.5f - 12f, top, sz.x + 24f, sz.y + 6f);
        UiBackdrop.Draw(r, 0.5f);
        Outlined(r, txt, stripStyle, ThemeColor(Time.unscaledTime), 1f);
    }

    void DrawResult()
    {
        EnsureStyles();
        float h = Screen.height;
        float a = phaseTime < 0.15f ? phaseTime / 0.15f : Mathf.Clamp01((0.6f + Profile.resultSeconds - phaseTime) / 0.3f);
        if (phaseTime < 0.6f)
        {
            bannerStyle.fontSize = Mathf.RoundToInt(h * 0.075f);
            Outlined(new Rect(0f, h * 0.29f, Screen.width, h * 0.12f), "BONUS END", bannerStyle, new Color(1f, 1f, 1f, a), 3f);
            return;
        }
        subStyle.fontSize = Mathf.RoundToInt(h * 0.034f);
        stripStyle.fontSize = Mathf.RoundToInt(h * 0.04f);
        // コンボ数(画面上の中央 0.2〜0.28)と重ならないよう、その下に出す
        var panel = new Rect(Screen.width * 0.5f - h * 0.36f, h * 0.295f, h * 0.72f, h * 0.17f);
        Color prev = GUI.color; GUI.color = new Color(1f, 1f, 1f, a);
        UiBackdrop.Draw(panel, 0.75f);
        GUI.color = prev;
        Color c = ThemeColor(Time.unscaledTime); c.a = a;
        Outlined(new Rect(panel.x, panel.y + panel.height * 0.06f, panel.width, panel.height * 0.4f), IsJackpot ? "JACKPOT RESULT" : "BONUS RESULT", subStyle, c, 2f);
        string line = $"MILE +{BonusMile}    EXP +{Mathf.RoundToInt(BonusExp)}    CARD +{BonusCards}";
        Outlined(new Rect(panel.x, panel.y + panel.height * 0.46f, panel.width, panel.height * 0.46f), line, stripStyle, new Color(1f, 1f, 1f, a), 2f);
    }

    void DrawPops(float dtu)
    {
        if (pops.Count == 0) return;
        EnsureStyles();
        Camera cam = Camera.main;
        for (int i = pops.Count - 1; i >= 0; i--)
        {
            var p = pops[i];
            p.t += Time.timeScale > 0f ? dtu : 0f;
            if (p.t >= PopLife || cam == null) { pops.RemoveAt(i); continue; }
            Vector3 sp = cam.WorldToScreenPoint(p.world + Vector3.up * (0.9f + p.t * 1.1f));
            if (sp.z < 0f) continue;
            float a = p.t < 0.1f ? p.t / 0.1f : Mathf.Clamp01((PopLife - p.t) / 0.3f);
            popStyle.fontSize = Mathf.RoundToInt(Screen.height * (p.big ? 0.046f : 0.034f));
            Color c = p.color; c.a = a;
            Outlined(new Rect(sp.x - 120f, Screen.height - sp.y - 20f, 240f, 40f), p.text, popStyle, c, 1.5f);
        }
    }

    bool debugOpen;
    void DrawDebug(GameManager gm)
    {
        EnsureStyles();
        var btn = new GUIStyle(GUI.skin.button) { fontSize = 13 };
        Rect safe = Screen.safeArea;
        float x = Screen.width * 0.30f, y = Screen.height - safe.y - 28f - 36f;
        if (GUI.Button(new Rect(x, y, 96f, 34f), debugOpen ? "BONUS ▼" : "BONUS ▲", btn)) debugOpen = !debugOpen;
        var info = new GUIStyle(GUI.skin.label) { fontSize = 12 };
        info.normal.textColor = new Color(1f, 0.9f, 0.5f);
        string st = State == Phase.Idle ? $"BONUS idle (zones {ZonesStarted}){(pendingAtDistance >= 0f ? $" roll at {pendingAtDistance:F0}m" : "")}"
            : $"BONUS {State} {Current?.id} {RemainingSeconds:F1}s/{RemainingDistance:F0}m MILE+{BonusMile} EXP+{BonusExp:F0} CARD+{BonusCards} enemies={ActiveEnemyCount()}";
        GUI.Label(new Rect(x + 102f, y + 8f, 560f, 22f), st, info);
        if (!debugOpen || Profile == null) return;
        float by = y - 38f;
        if (GUI.Button(new Rect(x, by, 96f, 32f), "RANDOM", btn)) Force();
        if (GUI.Button(new Rect(x + 100f, by, 96f, 32f), "END", btn)) End();
        by -= 36f;
        int col = 0;
        foreach (var t in Profile.types)
        {
            if (t == null) continue;
            if (GUI.Button(new Rect(x + col * 124f, by, 120f, 32f), t.id.ToUpperInvariant(), btn)) Force(t.id);
            if (++col >= 4) { col = 0; by -= 36f; }
        }
    }

    // ===================================================================== //
    // 音(専用の短いSE。BGMは区画の間だけ少し速く)
    // ===================================================================== //
    static AudioClip chime, jackpotChime, coin, expSe, endSe;
    float coinCooldownUntil;
    bool bgmRaised;

    void UpdateBgm(bool on)
    {
        if (on && !bgmRaised && AudioManager.Instance != null) { AudioManager.Instance.SetBgmPitch(IsJackpot ? 1.1f : 1.06f); bgmRaised = true; }
    }
    void RestoreBgm()
    {
        if (bgmRaised && AudioManager.Instance != null) AudioManager.Instance.SetBgmPitch(1f);
        bgmRaised = false;
    }

    static AudioClip Tone(string name, float[] freqs, float noteLen, float decay, float vol)
    {
        int rate = 44100;
        int n = Mathf.CeilToInt(rate * (noteLen * freqs.Length + decay));
        var data = new float[n];
        for (int k = 0; k < freqs.Length; k++)
        {
            int start = Mathf.RoundToInt(k * noteLen * rate);
            for (int i = start; i < n; i++)
            {
                float t = (i - start) / (float)rate;
                float env = Mathf.Exp(-t / decay) * Mathf.Clamp01(t * 200f);
                data[i] += vol * env * (Mathf.Sin(2f * Mathf.PI * freqs[k] * t) + 0.35f * Mathf.Sin(4f * Mathf.PI * freqs[k] * t));
            }
        }
        var clip = AudioClip.Create(name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    void PlayChime(bool jackpot)
    {
        if (AudioManager.Instance == null) return;
        if (jackpot) { if (jackpotChime == null) jackpotChime = Tone("bonus_jackpot", new[] { 784f, 988f, 1175f, 1568f, 1976f, 2349f }, 0.07f, 0.35f, 0.22f); AudioManager.Instance.PlaySfx(jackpotChime); }
        else { if (chime == null) chime = Tone("bonus_chime", new[] { 1047f, 1319f, 1568f, 2093f }, 0.075f, 0.3f, 0.25f); AudioManager.Instance.PlaySfx(chime); }
    }
    void PlayCoin()
    {
        if (AudioManager.Instance == null || Time.unscaledTime < coinCooldownUntil) return; // 連打でもうるさくしない
        coinCooldownUntil = Time.unscaledTime + 0.08f;
        if (coin == null) coin = Tone("bonus_coin", new[] { 1760f, 2637f }, 0.045f, 0.09f, 0.2f);
        AudioManager.Instance.PlaySfxVolume(coin, 0.8f);
    }
    void PlayExp()
    {
        if (AudioManager.Instance == null || Time.unscaledTime < coinCooldownUntil) return;
        coinCooldownUntil = Time.unscaledTime + 0.08f;
        if (expSe == null) expSe = Tone("bonus_exp", new[] { 880f, 1320f }, 0.05f, 0.12f, 0.18f);
        AudioManager.Instance.PlaySfxVolume(expSe, 0.8f);
    }
    void PlayEndSe()
    {
        if (AudioManager.Instance == null) return;
        if (endSe == null) endSe = Tone("bonus_end", new[] { 1568f, 1175f, 1319f }, 0.09f, 0.3f, 0.2f);
        AudioManager.Instance.PlaySfx(endSe);
    }

    // カード妖精の光などに使う柔らかい円
    static Sprite softCircle;
    public static Sprite SoftCircleSprite()
    {
        if (softCircle != null) return softCircle;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d)));
        }
        tex.Apply();
        softCircle = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n / 1.2f);
        softCircle.hideFlags = HideFlags.HideAndDontSave;
        return softCircle;
    }

    static float Rand() => (float)rng.NextDouble();
}
