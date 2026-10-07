using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 闘技場(2026-10-06 正式版。ホームから入れる「キャラやカードを自由に試せる練習場」)の本体。ArenaLauncher がランの開始後に作る。
//  ・Setup: 通常の出現/障害物/関門/BONUS/雲を止め、地面を平地にして、プレイヤーを平地へ置き、見た目(ArenaStage)を作る。
//           距離条件 → キャラの基準値 + 試用のビルド(通常の取得の処理)→ 速度/操作アシスト/無敵 → 敵を出す → 計測
//  ・準備画面(タブ): キャラ → カード → 相手 → 詳細 → 結果。開くと戦闘/計測は止まる(TimeControl)。「開始」/「同条件で再戦」は
//    シーンを読み直して同じ設定で作り直す(敵/弾/設置攻撃/演出/状態/クールダウンが残らない)。直前の結果は残して比べる。
//  ・戦闘中は小さな表示(与ダメージ/被ダメージ/時間)と左のボタン(設定/再戦/全削除/ホーム)だけ。倒れても GameOver にしない。
//  ・未所持のキャラ/カードは「試用」(所持/解放は変えない)。未遭遇のボスは名前を伏せて選べない。開発版だけの項目は「開発版」の欄へ分ける。
public class ArenaController : MonoBehaviour
{
    readonly UiScroll flavorScroll = new UiScroll(); // 2026-10-08
    public static ArenaController Instance { get; private set; }
    static readonly object pauseOwner = new object();

    readonly List<EnemyController> spawned = new List<EnemyController>();
    readonly List<(EnemyController en, float offset)> dummies = new List<(EnemyController, float)>();
    int bossesSpawned;
    float groundY;
    string buildNote = "", status = "";
    bool panelOpen;
    int tab;
    float bannerT, clearPanelAt = -1f;
    string setupJson = ""; // 作った時の設定(変更があれば「開始」で作り直す)
    bool ready;            // Setup が終わるまでは計測/勝敗の判定をしない

    void Awake() { Instance = this; }
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        TimeControl.Resume(pauseOwner);
        ArenaMode.BlockRects.Clear();
        ArenaMode.Defeated -= OnDefeated;
    }

    // 闘技場から出た/シーンの読み直し: 試験の設定を全部戻す(通常のランへ持ち出さない)
    public static void ResetAll()
    {
        ArenaMode.End();
        ArenaMode.BlockRects.Clear();
        TimeControl.Resume(pauseOwner);
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.ArenaRestore();
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = true; // シーンをまたいで残る
    }

    // ===================================================================== 準備
    public IEnumerator Setup(bool openSetup = false)
    {
        var gm = GameManager.Instance; var pc = PlayerController.Instance; var tm = TerrainManager.Instance; var cfg = ArenaMode.Config;
        ArenaMode.Defeated += OnDefeated;
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
        foreach (var o in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) o.enabled = false;
        foreach (var o in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) o.enabled = false;
        foreach (var o in FindObjectsByType<ForegroundCloudLayer>(FindObjectsSortMode.None)) o.gameObject.SetActive(false);
        if (BonusZone.Instance != null) BonusZone.Instance.enabled = false;
        ClearEnemiesNow();
        foreach (var o in FindObjectsByType<ObstacleController>(FindObjectsSortMode.None)) if (o != null) Destroy(o.gameObject);
        tm.ConfigureArena();
        float x = tm.NextGenerateX + 30f;
        tm.GenerateNow(x + 160f);
        pc.ArenaPlaceAt(x);
        yield return null;
        groundY = tm.GetHeightAt(pc.transform.position.x) ?? (pc.transform.position.y - pc.groundOffset);
        ArenaStage.Create(groundY);
        gm.ArenaSetDistance(cfg.distance);
        buildNote = gm.ArenaApplyBuild(cfg.build);
        pc.IsStandingIdle = cfg.speedMode == 0 || cfg.kmh <= 0.01f;
        ApplyAssist();
        ArenaMode.Invincible = cfg.invincible;
        if (cfg.seedFixed) Random.InitState(cfg.seed);
        yield return null;
        SpawnAll();
        ArenaMode.Current = new ArenaResult { label = Label(), spawned = spawned.Count + bossesSpawned, build = BuildSummary() };
        ArenaMode.BattleRunning = true;
        ready = true;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.ArenaStart); // 音の再設計(2026-10-06): 開始の銅鑼
        setupJson = JsonUtility.ToJson(cfg);
        status = "";
        if (openSetup) { tab = 0; SetPanel(true); }
        Debug.Log($"[Arena] READY {Label()} build=[{buildNote}] ground={groundY:F2} enemies={spawned.Count}+bosses {bossesSpawned} hp={gm.Lives}/{gm.maxLives} atk={pc.EffectiveAttackPower} speed={pc.CurrentAutoRunSpeed * GameManager.KmhPerMps:F1}km/h setup={openSetup}");
    }

    static string Label()
    {
        var c = ArenaMode.Config;
        var def = CharacterDatabase.FindById(c.character);
        string sp = c.speedMode == 0 || c.kmh <= 0.01f ? "0km/h" : $"{c.kmh:0}km/h{(c.speedMode == 2 ? "固定" : "")}";
        return $"{(def != null ? def.displayName : c.character)} / カード{c.build.Count}枚 / {sp} / {c.distance / 1000f:0.#}km地点相当";
    }

    static string BuildSummary()
    {
        var c = ArenaMode.Config;
        if (c.build.Count == 0) return "カードなし";
        return string.Join("、", c.build.Select(b => { var d = CardDatabase.FindById(b.key); return (d != null ? d.cardName : b.key) + (b.owned ? "(所持のコピー)" : $" Lv{b.times}"); }));
    }

    void ApplyAssist()
    {
        var c = ArenaMode.Config;
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.ArenaApply(c.assistMode, c.assistEngageKmh, c.assistBreakObstacles, c.assistEarlyDoubleJump);
    }

    // ===================================================================== 敵
    void SpawnAll()
    {
        spawned.Clear(); dummies.Clear(); bossesSpawned = 0;
        // ボスを先に出す(ボスの出現は周りの雑魚を片付けるので、雑魚は後から)。ボスは1種類まで(同時に複数の系統の関門は持てない)
        var boss = ArenaMode.Config.enemies.FirstOrDefault(ArenaConfig.IsBoss);
        if (boss != null && BossManager.Instance != null)
        {
            int family = boss.kind == ArenaEnemyKind.CaveBoss ? 2 : boss.kind == ArenaEnemyKind.SkyBoss ? 3 : 1;
            BossManager.Instance.ArenaSpawnBoss(family, boss.bossKind, boss.count);
            bossesSpawned += Mathf.Clamp(boss.count, 1, 4);
        }
        foreach (var e in ArenaMode.Config.enemies)
        {
            if (e == null || ArenaConfig.IsBoss(e)) continue;
            for (int i = 0; i < Mathf.Clamp(e.count, 1, 20); i++) SpawnEnemy(e, e.ahead + i * e.spacing);
        }
    }

    void SpawnEnemy(ArenaEnemyEntry e, float ahead)
    {
        var tm = TerrainManager.Instance; var pc = PlayerController.Instance;
        bool dummy = e.kind == ArenaEnemyKind.Dummy;
        var def = EnemyDatabase.FindById(dummy ? "goblin" : e.enemyId);
        if (tm == null || pc == null || def == null) return;
        float x = pc.transform.position.x + Mathf.Max(1.5f, ahead);
        float y = groundY + (def.movementType == EnemyMovementType.Flying ? 2.6f : 0f);
        var tier = dummy ? EnemyAiTier.T0 : (EnemyAiTier)Mathf.Clamp(e.tier, 0, 4);
        var go = tm.SpawnEncounterEnemy(def, new Vector2(x, y), tier, dummy ? EnemyBehaviorKind.None : def.behaviorKind);
        var en = go != null ? go.GetComponent<EnemyController>() : null;
        if (en == null) return;
        if (dummy)
        {
            en.ArenaDummy = true;
            en.hitKnockbackEnabled = false;
            en.maxHp = 99999999; en.ResetHpToMax();
            var sb = go.GetComponent<EnemySpecialBehavior>(); if (sb != null) sb.enabled = false;
            dummies.Add((en, x - pc.transform.position.x));
        }
        else spawned.Add(en);
    }

    // 敵を全部消す: 本体 + 敵/ボス由来の飛び道具/設置攻撃/予告/落石/予約された攻撃(ボスの体ごと消すので、ボスのコルーチンも止まる)
    public static int LastClearedObjects;
    void ClearEnemiesNow()
    {
        int n = 0;
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (e != null) { Destroy(e.gameObject); n++; }
        if (TerrainManager.Instance != null) TerrainManager.Instance.ClearAllEnemies();
        if (BossManager.Instance != null) BossManager.Instance.ArenaClearBosses();
        n += DestroyAll<BossProjectile>() + DestroyAll<BossHitbox>() + DestroyAll<BossTelegraphMarker>() + DestroyAll<FireballController>()
           + DestroyAll<CeilingFallRock>() + DestroyAll<FallingDebris>() + DestroyAll<SkyStrike>() + DestroyAll<SkyWarnBand>() + DestroyAll<SkyFlyby>() + DestroyAll<SkyDrift>();
        n += CaveHazard.LiveCount;
        CaveHazard.ClearAll();
        var pc = PlayerController.Instance;
        if (pc != null) pc.ArenaClearHurtState(); // プレイヤーの被弾の状態(のけぞり/硬直)も戻す
        spawned.Clear(); dummies.Clear(); bossesSpawned = 0;
        LastClearedObjects = n;
    }
    static int DestroyAll<T>() where T : Component
    {
        int n = 0;
        foreach (var c in FindObjectsByType<T>(FindObjectsSortMode.None)) if (c != null) { Destroy(c.gameObject); n++; }
        return n;
    }

    int AliveEnemies => spawned.Count(e => e != null && !e.IsDying) + (BossManager.Instance != null ? BossManager.Instance.AliveBossCount : 0);

    // ===================================================================== 毎フレーム
    void Update()
    {
        if (!ArenaMode.Active || !ready) return;
        // ゲームパッド: LB/RB でタブ
        if (panelOpen && PadNav.MenuActive)
        {
            if (GameInput.Down(GameAction.TabPrev)) tab = (tab + Tabs.Length - 1) % Tabs.Length;
            else if (GameInput.Down(GameAction.TabNext)) tab = (tab + 1) % Tabs.Length;
        }
        var r = ArenaMode.Current;
        if (ArenaMode.BattleRunning && !panelOpen)
        {
            r.time += Time.deltaTime; // 止めている間(準備画面/停止メニュー)は進まない
            if (r.spawned > 0 && !r.clearedByHand && r.clearTime < 0f && AliveEnemies == 0 && r.time > 0.5f)
            {
                r.clearTime = r.time; r.ended = true; ArenaMode.BattleRunning = false; bannerT = 3f;
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.ArenaWin);
                clearPanelAt = Time.unscaledTime + 1.6f; // 少し見せてから結果
                Debug.Log($"[Arena] CLEAR in {r.clearTime:F2}s dealt={r.dealt} hits={r.hits} taken={r.taken} would={r.wouldTake}");
            }
        }
        if (bannerT > 0f) bannerT -= Time.unscaledDeltaTime;
        if (clearPanelAt > 0f && Time.unscaledTime >= clearPanelAt) { clearPanelAt = -1f; tab = 4; SetPanel(true); }
    }

    void LateUpdate()
    {
        var pc = PlayerController.Instance;
        if (pc == null || dummies.Count == 0 || pc.CurrentAutoRunSpeed <= 0.01f) return;
        foreach (var (en, off) in dummies)
            if (en != null) en.transform.position = new Vector3(pc.transform.position.x + off, en.transform.position.y, en.transform.position.z);
    }

    void OnDefeated()
    {
        var r = ArenaMode.Current;
        r.ended = true; ArenaMode.BattleRunning = false;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.ArenaLose);
        status = "倒れました。「同条件で再戦」ですぐやり直せます";
        Debug.Log($"[Arena] DEFEATED at {r.time:F2}s ({r.defeatReason})");
        tab = 4; SetPanel(true);
    }

    // ===================================================================== 操作
    public void Rematch(string why)
    {
        var r = ArenaMode.Current;
        if (r != null && (r.time > 0.1f || r.dealt > 0)) ArenaMode.Previous = r;
        SetPanel(false);
        ArenaLauncher.Launch(why, false);
    }
    // 「開始」: 準備画面で何か変えていれば作り直す。変えていなければそのまま始める
    public void StartOrRematch()
    {
        if (JsonUtility.ToJson(ArenaMode.Config) != setupJson || ArenaMode.Current.ended) Rematch("start");
        else SetPanel(false);
    }
    public void RefillHp() { if (GameManager.Instance != null) GameManager.Instance.ArenaRefillLives(); status = "HPを全回復しました"; }
    public void ToggleInvincible() { ArenaMode.Config.invincible = ArenaMode.Invincible = !ArenaMode.Invincible; setupJson = JsonUtility.ToJson(ArenaMode.Config); }
    public void ClearEnemies() { ClearEnemiesNow(); if (ArenaMode.Current != null) ArenaMode.Current.clearedByHand = true; status = "敵と敵の攻撃を全部消しました(撃破の時間には数えません)"; }
    public void ExitHome() { SetPanel(false); ArenaLauncher.Exit(); }
    public void SetPanel(bool on)
    {
        if (panelOpen == on) return;
        panelOpen = on;
        if (on) TimeControl.Pause(pauseOwner);
        else { TimeControl.Resume(pauseOwner); UiInputGate.LatchUntilRelease(); }
    }
    public bool PanelOpen => panelOpen;
    public int Tab { get => tab; set => tab = Mathf.Clamp(value, 0, Tabs.Length - 1); }

    // ===================================================================== 表示(IMGUI、短辺720を基準に拡大。スマホ横画面で押しやすい大きさ)
    static float S => Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 0.75f, 2.6f);
    Vector2 listScroll, buildScroll, enemyScroll, enemyListScroll, charScroll;
    string search = "";
    int cardFilter, enemyCat;
    string enemyPickId = ""; int enemyPickBoss = -1; int pickCount = 1; int pickTier = 1;
    static readonly Color Gold = new Color(1f, 0.85f, 0.5f), Soft = new Color(0.85f, 0.88f, 0.95f), Dim = new Color(0.65f, 0.68f, 0.78f), Trial = new Color(0.55f, 0.9f, 1f);

    void OnGUI()
    {
        if (!ArenaMode.Active) return;
        GUI.depth = -2000;
        float s = S;
        var keep = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float W = Screen.width / s, H = Screen.height / s;
        ArenaMode.BlockRects.Clear();
        int padLayer = PadNav.BeginLayer(panelOpen ? 3 : PadNav.HudLayer); // 戦闘中の左のボタンは HUD(パッドでは START で設定)
        if (panelOpen) DrawPanel(W, H, s);
        else DrawHud(W, H, s);
        PadNav.EndLayer(padLayer);
        GUI.matrix = keep;
    }

    Rect SafeRect(float W, float H, float s)
    {
        var sa = Screen.safeArea;
        float l = sa.x / s, t = (Screen.height - sa.yMax) / s, r = (Screen.width - sa.xMax) / s, b = sa.y / s;
        return new Rect(l + 10f, t + 10f, W - l - r - 20f, H - t - b - 20f);
    }

    void DrawHud(float W, float H, float s)
    {
        var r = ArenaMode.Current;
        var safe = SafeRect(W, H, s);
        // 小さな計測(上の中央): 与ダメージ / 被ダメージ / 時間
        string line = $"与ダメージ {r.dealt:N0}    被ダメージ {r.taken}    {r.time:F1}秒" + (ArenaMode.Invincible ? "    無敵" : "");
        var st = UiKit.Label(16f, TextAnchor.MiddleCenter, true, new Color(1f, 0.94f, 0.75f));
        var size = st.CalcSize(new GUIContent(line));
        float top = safe.y + 92f;
        var lr = new Rect(W * 0.5f - size.x * 0.5f - 12f, top, size.x + 24f, 30f);
        UiKit.Fill(lr, new Color(0.05f, 0.03f, 0.08f, 0.66f));
        LocGUI.Label(lr, line, st);
        // 左のボタン(大きめ)
        string[] labels = { "≡ 設定", "同条件で再戦", "敵を全削除", "ホームへ" };
        float bw = 150f, bh = 52f, gap = 8f, x0 = safe.x, y0 = Mathf.Max(top + 40f, H * 0.32f);
        for (int i = 0; i < labels.Length; i++)
        {
            var br = new Rect(x0, y0 + i * (bh + gap), bw, bh);
            ArenaMode.BlockRects.Add(new Rect(br.x * s, br.y * s, br.width * s, br.height * s));
            if (UiKit.Button(br, labels[i], 17f, i == 1, false))
            {
                switch (i)
                {
                    case 0: tab = 0; SetPanel(true); break;
                    case 1: Rematch("hud"); break;
                    case 2: ClearEnemies(); break;
                    case 3: ExitHome(); break;
                }
            }
        }
        if (bannerT > 0f && r.clearTime >= 0f)
        {
            var bs = UiKit.Label(34f, TextAnchor.MiddleCenter, true, new Color(1f, 0.9f, 0.4f));
            LocGUI.Label(new Rect(0f, H * 0.36f, W, 54f), $"全滅  {r.clearTime:F2}秒", bs);
        }
        if (!string.IsNullOrEmpty(status)) LocGUI.Label(new Rect(x0, y0 + labels.Length * (bh + gap), 460f, 26f), status, UiKit.Label(14f, TextAnchor.MiddleLeft, false, Soft));
    }

    static readonly string[] Tabs = { "キャラ", "カード", "相手", "詳細", "結果" };

    void DrawPanel(float W, float H, float s)
    {
        var safe = SafeRect(W, H, s);
        UiKit.Fill(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, 0.5f));
        var p = safe;
        OrnateUi.DrawPanel(p, 0.96f);
        LocGUI.Label(new Rect(p.x + 18f, p.y + 8f, 240f, 34f), "闘技場", UiKit.Label(24f, TextAnchor.MiddleLeft, true, new Color(1f, 0.75f, 0.45f)));
        LocGUI.Label(new Rect(p.x + 18f, p.y + 40f, 260f, 20f), "キャラやカードを自由に試せる練習場", UiKit.Label(12f, TextAnchor.MiddleLeft, false, Dim));
        float tabX = p.x + 280f, tw = Mathf.Min(120f, (p.width - 280f - 330f) / Tabs.Length - 6f);
        for (int i = 0; i < Tabs.Length; i++)
            if (UiKit.Button(new Rect(tabX + i * (tw + 6f), p.y + 12f, tw, 44f), Tabs[i], 17f, tab == i, false)) tab = i;
        // 右上: 開始 / ホーム(どのタブでも同じ場所)
        var cur = ArenaMode.Current;
        bool changed = JsonUtility.ToJson(ArenaMode.Config) != setupJson;
        string startLabel = cur.ended ? "同条件で再戦" : changed ? "この設定で開始" : "開始";
        if (UiKit.Button(new Rect(p.xMax - 330f, p.y + 10f, 200f, 50f), startLabel, 19f, true, false)) StartOrRematch();
        if (UiKit.Button(new Rect(p.xMax - 122f, p.y + 10f, 110f, 50f), "ホームへ", 17f, false, false)) ExitHome();
        var c = new Rect(p.x + 18f, p.y + 70f, p.width - 36f, p.height - 84f);
        switch (tab)
        {
            case 0: DrawCharTab(c); break;
            case 1: DrawCardTab(c); break;
            case 2: DrawEnemyTab(c); break;
            case 3: DrawDetailTab(c); break;
            default: DrawResultTab(c); break;
        }
    }

    // ---- キャラ(画像/名前/特徴。未所持は「試用」)
    void DrawCharTab(Rect c)
    {
        var cfg = ArenaMode.Config;
        var chars = new System.Collections.Generic.List<CharacterDefinition>();
        foreach (var cd in CharacterDatabase.AllCharacters) if (UnlockRules.IsCharacterVisible(cd.characterId)) chars.Add(cd); // 竜人は解放前は出さない(2026-10-07)
        float gw = c.width * 0.56f;
        int cols = 4; float cw = (gw - 20f - (cols - 1) * 8f) / cols, ch = 112f;
        var listR = new Rect(c.x, c.y, gw, c.height);
        charScroll = BeginList(listR, charScroll, Mathf.CeilToInt(chars.Count / (float)cols) * (ch + 8f));
        for (int i = 0; i < chars.Count; i++)
        {
            var d = chars[i];
            var r = new Rect((i % cols) * (cw + 8f), (i / cols) * (ch + 8f), cw, ch);
            bool sel = cfg.character == d.characterId;
            UiKit.Fill(r, sel ? new Color(0.35f, 0.24f, 0.08f, 0.95f) : new Color(0.08f, 0.09f, 0.16f, 0.9f));
            if (d.portrait != null) GUI.DrawTexture(new Rect(r.x + 4f, r.y + 4f, r.width - 8f, r.height - 30f), d.portrait, ScaleMode.ScaleToFit);
            LocGUI.Label(new Rect(r.x, r.yMax - 26f, r.width, 24f), d.displayName, UiKit.Label(14f, TextAnchor.MiddleCenter, true, sel ? Gold : Color.white));
            if (!CharacterOwned(d)) LocGUI.Label(new Rect(r.x + 4f, r.y + 2f, r.width - 8f, 18f), "試用", UiKit.Label(12f, TextAnchor.UpperRight, true, Trial));
            if (PadNav.Button(r) | GUI.Button(r, GUIContent.none, GUIStyle.none)) cfg.character = d.characterId;
        }
        EndList();
        var sd = CharacterDatabase.FindById(cfg.character);
        float rx = c.x + gw + 16f, rw = c.width - gw - 16f;
        if (sd == null) return;
        if (sd.mainVisual != null || sd.portrait != null) GUI.DrawTexture(new Rect(rx, c.y, rw, c.height * 0.5f), sd.mainVisual != null ? sd.mainVisual : sd.portrait, ScaleMode.ScaleToFit);
        float y = c.y + c.height * 0.5f + 6f;
        LocGUI.Label(new Rect(rx, y, rw, 32f), sd.displayName + (CharacterOwned(sd) ? "" : "  (試用)"), UiKit.Label(22f, TextAnchor.MiddleLeft, true, Gold));
        y += 32f;
        if (!string.IsNullOrEmpty(sd.subtitle)) { LocGUI.Label(new Rect(rx, y, rw, 22f), sd.subtitle, UiKit.Label(14f, TextAnchor.MiddleLeft, false, Soft)); y += 22f; }
        if (!string.IsNullOrEmpty(sd.role)) { LocGUI.Label(new Rect(rx, y, rw, 22f), sd.role, UiKit.Label(13f, TextAnchor.MiddleLeft, true, Trial)); y += 24f; }
        var desc = UiKit.Label(14f, TextAnchor.UpperLeft, false, Soft); desc.wordWrap = true;
        flavorScroll.Text(new Rect(rx, y, rw, c.yMax - y), sd.flavorText ?? "", desc, sd.characterId); // 2026-10-08: 長い説明はスクロール
    }
    static bool CharacterOwned(CharacterDefinition d) => d != null && UnlockRules.IsCharacterUnlocked(d.characterId); // 2026-10-07: 未解放のキャラは闘技場では「試用」

    // ---- カード(画像/名前/効果/試用Lv。検索と絞り込み。デッキ/キャラカードのコピー)
    static readonly string[] CardFilters = { "すべて", "移動", "攻撃", "防御", "成長", "回復", "特殊", "リスク" };
    void DrawCardTab(Rect c)
    {
        var cfg = ArenaMode.Config;
        float lw = c.width * 0.54f;
        LocGUI.Label(new Rect(c.x, c.y, 50f, 34f), "検索", UiKit.Label(15f));
        search = GUI.TextField(new Rect(c.x + 50f, c.y + 2f, lw - 50f, 32f), search ?? "");
        float fw = (lw - 7f * 4f) / 8f;
        for (int i = 0; i < CardFilters.Length; i++)
            if (UiKit.Button(new Rect(c.x + i * (fw + 4f), c.y + 40f, fw, 34f), CardFilters[i], 13f, cardFilter == i, false)) cardFilter = i;
        string q = (search ?? "").Trim().ToLowerInvariant();
        var cards = CardDatabase.AllCards.Where(d => d != null && !CardVariant.IsVariantKey(d.cardId)
            && (cardFilter == 0 || (int)d.category == cardFilter - 1)
            && (q.Length == 0 || d.cardName.ToLowerInvariant().Contains(q) || (d.description ?? "").ToLowerInvariant().Contains(q))).OrderBy(d => d.sortOrder).ToList();
        var listR = new Rect(c.x, c.y + 80f, lw, c.height - 80f - 46f);
        float rowH = 58f;
        listScroll = BeginList(listR, listScroll, cards.Count * rowH);
        for (int i = 0; i < cards.Count; i++)
        {
            var d = cards[i];
            var rr = new Rect(0, i * rowH, lw - 20f, rowH - 4f);
            UiKit.Fill(rr, new Color(0.08f, 0.09f, 0.16f, 0.85f));
            if (d.icon != null) GUI.DrawTexture(new Rect(rr.x + 4f, rr.y + 4f, rr.height - 8f, rr.height - 8f), d.icon, ScaleMode.ScaleToFit);
            float tx = rr.x + rr.height + 4f;
            bool owned = CardInventory.GetTotalCount(d.cardId) > 0;
            LocGUI.Label(new Rect(tx, rr.y + 2f, rr.width - tx - 90f, 22f), d.cardName + (owned ? "" : "  <color=#8ce6ff>試用</color>"), UiKit.Label(15f, TextAnchor.MiddleLeft, true));
            var dl = UiKit.Label(11f, TextAnchor.UpperLeft, false, Dim); dl.wordWrap = true; dl.clipping = TextClipping.Clip;
            LocGUI.Label(new Rect(tx, rr.y + 24f, rr.width - tx - 90f, rr.height - 26f), d.description ?? "", dl);
            if (UiKit.Button(new Rect(rr.xMax - 82f, rr.y + 8f, 78f, rr.height - 16f), "+ 追加", 15f, false, false)) AddCard(d.cardId, false, 1);
        }
        EndList();
        float cby = listR.yMax + 6f, cbw = (lw - 8f) / 2f;
        if (UiKit.Button(new Rect(c.x, cby, cbw, 38f), "今のデッキをコピー", 14f, false, false)) CopyDeck();
        if (UiKit.Button(new Rect(c.x + cbw + 8f, cby, cbw, 38f), "キャラカードをコピー", 14f, false, false)) CopyCharacterCards();
        float rx = c.x + lw + 14f, rw = c.width - lw - 14f;
        LocGUI.Label(new Rect(rx, c.y, rw - 100f, 32f), $"試用のビルド({cfg.build.Count}枚)", UiKit.Label(17f, TextAnchor.MiddleLeft, true, Gold));
        if (UiKit.Button(new Rect(rx + rw - 96f, c.y, 96f, 32f), "全部外す", 14f, false, false)) cfg.build.Clear();
        LocGUI.Label(new Rect(rx, c.y + 32f, rw, 20f), "Lv = このランで取った回数(能力ごとに最大9)。所持・デッキは変わりません", UiKit.Label(11f, TextAnchor.MiddleLeft, false, Dim));
        var bR = new Rect(rx, c.y + 56f, rw, c.height - 56f);
        float bRow = 48f;
        buildScroll = BeginList(bR, buildScroll, Mathf.Max(1, cfg.build.Count) * bRow);
        if (cfg.build.Count == 0) LocGUI.Label(new Rect(8f, 8f, rw - 30f, 60f), "左の一覧の「+ 追加」でカードを入れます。カードなしでも試せます", UiKit.Label(13f, TextAnchor.UpperLeft, false, Dim));
        for (int i = 0; i < cfg.build.Count; i++)
        {
            var e = cfg.build[i];
            var d = CardDatabase.FindById(e.key); var v = CardVariant.Parse(e.key);
            var rr = new Rect(0, i * bRow, rw - 20f, bRow - 4f);
            UiKit.Fill(rr, new Color(0.14f, 0.1f, 0.05f, 0.9f));
            if (d != null && d.icon != null) GUI.DrawTexture(new Rect(rr.x + 3f, rr.y + 3f, rr.height - 6f, rr.height - 6f), d.icon, ScaleMode.ScaleToFit);
            string name = d != null ? d.cardName : e.key;
            string lv = e.owned ? $"所持のコピー(合成Lv{(v != null ? v.level : 1)}・能力{(v != null ? v.AbilityCount : 1)}種)" : $"Lv {e.times}";
            LocGUI.Label(new Rect(rr.x + rr.height + 2f, rr.y, rr.width - rr.height - 150f, rr.height), $"{name}\n<size=11>{lv}</size>", UiKit.Label(13f));
            if (!e.owned)
            {
                if (UiKit.Button(new Rect(rr.xMax - 144f, rr.y + 4f, 44f, rr.height - 8f), "-", 18f, false, false)) { e.times--; if (e.times <= 0) { cfg.build.RemoveAt(i); break; } }
                if (UiKit.Button(new Rect(rr.xMax - 96f, rr.y + 4f, 44f, rr.height - 8f), "+", 18f, false, false)) e.times = Mathf.Min(GameManager.MaxRunCardLevel, e.times + 1);
            }
            if (UiKit.Button(new Rect(rr.xMax - 48f, rr.y + 4f, 44f, rr.height - 8f), "×", 18f, false, false)) { cfg.build.RemoveAt(i); break; }
        }
        EndList();
    }

    void AddCard(string key, bool owned, int lv)
    {
        var cfg = ArenaMode.Config;
        var e = cfg.build.FirstOrDefault(x => x.key == key && x.owned == owned);
        if (e != null) e.times = Mathf.Min(GameManager.MaxRunCardLevel, e.times + (owned ? 0 : 1));
        else cfg.build.Add(new ArenaBuildEntry { key = key, times = Mathf.Clamp(lv, 1, GameManager.MaxRunCardLevel), owned = owned });
    }
    // デッキのカード: 1回目の取得の状態(合成カードは能力一式の所持のコピー)
    void CopyDeck()
    {
        var gm = GameManager.Instance; if (gm == null) return;
        int n = 0;
        foreach (var id in gm.DeckCards) if (!string.IsNullOrEmpty(id) && CardDatabase.FindById(id) != null) { AddCard(id, CardVariant.IsVariantKey(id), 1); n++; }
        status = $"デッキの{n}枚を試用のビルドへコピーしました(デッキは変わりません)";
    }
    // キャラカード: 開始時の Lv のまま
    void CopyCharacterCards()
    {
        var gm = GameManager.Instance; if (gm == null) return;
        int n = 0;
        for (int i = 0; i < GameManager.CharacterCardSlotCount; i++)
        {
            string id = gm.CharacterCardIds[i];
            if (string.IsNullOrEmpty(id) || CardDatabase.FindById(id) == null) continue;
            AddCard(id, CardVariant.IsVariantKey(id), gm.GetCharacterCardLevel(i)); n++;
        }
        status = $"キャラカードの{n}枚をコピーしました(開始時のLv)";
    }

    // ---- 相手(練習標的/遭遇済みの雑魚/遭遇済みのボス。数/強さ/○km地点相当)
    static readonly string[] EnemyCats = { "練習標的", "雑魚", "荒野のボス", "洞窟のボス", "天空のボス" };
    void DrawEnemyTab(Rect c)
    {
        var cfg = ArenaMode.Config;
        float lw = c.width * 0.5f - 8f;
        float cw = (lw - 16f) / 5f;
        for (int i = 0; i < EnemyCats.Length; i++)
            if (UiKit.Button(new Rect(c.x + i * (cw + 4f), c.y, cw, 40f), EnemyCats[i], 14f, enemyCat == i, false)) enemyCat = i;
        float rowH = 46f;
        var listR = new Rect(c.x, c.y + 46f, lw, c.height - 46f - 124f);
        var items = ArenaCatalog.Items(enemyCat, cfg.devAllEnemies);
        enemyListScroll = BeginList(listR, enemyListScroll, Mathf.Max(2, items.Count) * rowH);
        if (enemyCat == 0) LocGUI.Label(new Rect(8f, 4f, lw - 30f, 80f), "動かない標的: 攻撃もしない(当たってもダメージなし)・倒れない。攻撃の確認に。走っている時はプレイヤーとの間合いを保ちます", UiKit.Label(14f, TextAnchor.UpperLeft, false, Soft));
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var r = new Rect(0, i * rowH, lw - 20f, rowH - 4f);
            bool sel = enemyCat == 1 ? enemyPickId == it.id : enemyPickBoss == it.kind;
            UiKit.Fill(r, sel ? new Color(0.35f, 0.18f, 0.08f, 0.95f) : new Color(0.1f, 0.07f, 0.09f, 0.9f));
            if (it.sprite != null && it.known) DrawSprite(new Rect(r.x + 4f, r.y + 2f, r.height - 4f, r.height - 4f), it.sprite);
            LocGUI.Label(new Rect(r.x + r.height + 6f, r.y, r.width - r.height - 10f, r.height), it.label, UiKit.Label(15f, TextAnchor.MiddleLeft, it.known, it.known ? Color.white : Dim));
            if (it.known && (PadNav.Button(r) | GUI.Button(r, GUIContent.none, GUIStyle.none))) { if (enemyCat == 1) enemyPickId = it.id; else enemyPickBoss = it.kind; }
        }
        EndList();
        float y = listR.yMax + 6f;
        bool bossCat = enemyCat >= 2;
        y = Stepper(c.x, y, lw, "数", pickCount.ToString(), () => pickCount = Mathf.Max(1, pickCount - 1), () => pickCount = Mathf.Min(bossCat ? 4 : enemyCat == 0 ? 6 : 12, pickCount + 1));
        if (enemyCat == 1) y = Stepper(c.x, y, lw, "動きの強さ", "段階 " + (pickTier + 1), () => pickTier = Mathf.Max(0, pickTier - 1), () => pickTier = Mathf.Min(4, pickTier + 1));
        if (bossCat) { var nt = UiKit.Label(11f, TextAnchor.UpperLeft, false, Dim); nt.wordWrap = true; LocGUI.Label(new Rect(c.x, y, lw * 0.55f - 8f, 60f), enemyCat == 2 ? "ボスは自分の登場の動きで前から現れます" : "洞窟/天空のボスの地形を使う技(穴/天井/落石/空の帯など)は、平らな闘技場では本編と出方が違います", nt); }
        bool canAdd = enemyCat == 0 || (enemyCat == 1 && !string.IsNullOrEmpty(enemyPickId)) || (bossCat && enemyPickBoss >= 0 && ArenaCatalog.Items(enemyCat, cfg.devAllEnemies).Any(i => i.kind == enemyPickBoss && i.known));
        if (bossCat) LocGUI.Label(new Rect(c.x, c.yMax - 70f, lw * 0.55f - 8f, 20f), "ボスは1種類まで(雑魚と一緒に出せます)", UiKit.Label(11f, TextAnchor.UpperLeft, false, Dim));
        if (UiKit.Button(new Rect(c.x + lw * 0.55f, c.yMax - 46f, lw * 0.45f, 44f), "相手に加える", 16f, true, false, canAdd) && canAdd)
        {
            var e = new ArenaEnemyEntry { count = pickCount, tier = pickTier, ahead = 6f, spacing = 2.5f };
            if (enemyCat == 0) e.kind = ArenaEnemyKind.Dummy;
            else if (enemyCat == 1) { e.kind = ArenaEnemyKind.Enemy; e.enemyId = enemyPickId; }
            else
            {
                e.kind = enemyCat == 2 ? ArenaEnemyKind.WildBoss : enemyCat == 3 ? ArenaEnemyKind.CaveBoss : ArenaEnemyKind.SkyBoss; e.bossKind = enemyPickBoss;
                if (cfg.enemies.RemoveAll(ArenaConfig.IsBoss) > 0) status = "ボスは1種類まで(前のボスと入れ替えました)";
            }
            cfg.enemies.Add(e);
        }
        float rx = c.x + lw + 16f, rw = c.width - lw - 16f;
        LocGUI.Label(new Rect(rx, c.y, rw - 110f, 34f), $"今の相手({cfg.enemies.Count})", UiKit.Label(17f, TextAnchor.MiddleLeft, true, Gold));
        if (UiKit.Button(new Rect(rx + rw - 104f, c.y, 104f, 34f), "全部外す", 14f, false, false)) cfg.enemies.Clear();
        var eR = new Rect(rx, c.y + 40f, rw, c.height - 40f - 112f);
        enemyScroll = BeginList(eR, enemyScroll, Mathf.Max(1, cfg.enemies.Count) * rowH);
        if (cfg.enemies.Count == 0) LocGUI.Label(new Rect(8f, 6f, rw - 30f, 50f), "相手がいません(このまま始めると敵なしで動きだけ試せます)", UiKit.Label(13f, TextAnchor.UpperLeft, false, Dim));
        for (int i = 0; i < cfg.enemies.Count; i++)
        {
            var e = cfg.enemies[i];
            var rr = new Rect(0, i * rowH, rw - 20f, rowH - 4f);
            UiKit.Fill(rr, new Color(0.14f, 0.06f, 0.06f, 0.9f));
            LocGUI.Label(new Rect(8f, rr.y, rr.width - 60f, rr.height), $"{ArenaCatalog.NameOf(e)}  ×{e.count}{(e.kind == ArenaEnemyKind.Enemy ? $"  段階{e.tier + 1}" : "")}", UiKit.Label(15f));
            if (UiKit.Button(new Rect(rr.xMax - 48f, rr.y + 4f, 44f, rr.height - 8f), "×", 18f, false, false)) { cfg.enemies.RemoveAt(i); break; }
        }
        EndList();
        float dy = eR.yMax + 8f;
        LocGUI.Label(new Rect(rx, dy, rw, 24f), $"敵の強さ: {cfg.distance / 1000f:0.#}km地点相当(HP/攻撃が本編のその距離と同じ)", UiKit.Label(14f, TextAnchor.MiddleLeft, true, Soft));
        float[] dp = { 1000f, 5000f, 10000f, 30000f, 50000f, 70000f, 90000f };
        float dw = (rw - 6f * 4f) / 7f;
        for (int i = 0; i < dp.Length; i++) if (UiKit.Button(new Rect(rx + i * (dw + 4f), dy + 30f, dw, 42f), $"{dp[i] / 1000f:0}km", 14f, Mathf.Approximately(cfg.distance, dp[i]), false)) cfg.distance = dp[i];
    }

    static void DrawSprite(Rect r, Sprite sp)
    {
        if (sp == null || sp.texture == null) return;
        var tr = sp.textureRect; var tex = sp.texture;
        var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
        float aspect = tr.width / Mathf.Max(1f, tr.height);
        var fit = aspect > r.width / r.height ? new Rect(r.x, r.center.y - r.width / aspect * 0.5f, r.width, r.width / aspect) : new Rect(r.center.x - r.height * aspect * 0.5f, r.y, r.height * aspect, r.height);
        GUI.DrawTextureWithTexCoords(fit, tex, uv);
    }

    float Stepper(float x, float y, float w, string label, string value, System.Action minus, System.Action plus)
    {
        LocGUI.Label(new Rect(x, y, 110f, 38f), label, UiKit.Label(15f));
        if (UiKit.Button(new Rect(x + 110f, y, 48f, 38f), "-", 20f, false, false)) minus();
        LocGUI.Label(new Rect(x + 162f, y, 110f, 38f), value, UiKit.Label(16f, TextAnchor.MiddleCenter, true));
        if (UiKit.Button(new Rect(x + 276f, y, 48f, 38f), "+", 20f, false, false)) plus();
        return y + 42f;
    }

    // ---- 詳細(速度/無敵/操作アシスト/未対応の効果。開発版の項目は分ける)
    void DrawDetailTab(Rect c)
    {
        var cfg = ArenaMode.Config; var pc = PlayerController.Instance;
        float x = c.x, y = c.y, w = c.width;
        var head = UiKit.Label(16f, TextAnchor.MiddleLeft, true, Gold);
        var note = UiKit.Label(12f, TextAnchor.UpperLeft, false, Dim); note.wordWrap = true;
        LocGUI.Label(new Rect(x, y, w, 26f), "速度", head); y += 28f;
        string[] modes = { "止まる(0km/h)", "走る(基準速度+補正)", "走る(速度を固定)" };
        float mw = (w - 16f) / 3f;
        for (int i = 0; i < 3; i++) if (UiKit.Button(new Rect(x + i * (mw + 8f), y, mw, 42f), modes[i], 15f, cfg.speedMode == i, false)) { cfg.speedMode = i; if (i > 0 && cfg.kmh <= 0.01f) cfg.kmh = 60f; }
        y += 46f;
        LocGUI.Label(new Rect(x, y, w, 36f), cfg.speedMode == 0 ? "自動の前進だけが止まります(攻撃/ジャンプ/重力/のけぞり/敵の動きは本編どおり)" :
            cfg.speedMode == 1 ? "基準の速度に、キャラ/カードの速さの補正(SPEED UP など)を掛けた速さで走ります" : "補正を掛けず、この速さのまま走ります(速さのカードの違いを除いて比べる時に)", note);
        y += 36f;
        if (cfg.speedMode > 0)
        {
            float[] presets = { 30f, 60f, 100f, 130f, 160f };
            float pw = (w - 4f * 6f) / 5f;
            for (int i = 0; i < presets.Length; i++) if (UiKit.Button(new Rect(x + i * (pw + 6f), y, pw, 40f), $"{presets[i]:0}km/h", 15f, Mathf.Approximately(cfg.kmh, presets[i]), false)) cfg.kmh = presets[i];
            y += 44f;
            if (pc != null) LocGUI.Label(new Rect(x, y, w, 22f), $"今: 基準 {cfg.kmh:0}km/h → 実際 {pc.CurrentAutoRunSpeed * GameManager.KmhPerMps:0}km/h(補正 ×{pc.ArenaSpeedFactor:F2})", UiKit.Label(13f, TextAnchor.MiddleLeft, false, Soft));
            y += 24f;
        }
        y += 6f;
        LocGUI.Label(new Rect(x, y, w, 26f), "無敵 / HP", head); y += 28f;
        if (UiKit.Button(new Rect(x, y, 220f, 42f), $"無敵: {(ArenaMode.Invincible ? "ON" : "OFF")}", 16f, ArenaMode.Invincible, false)) ToggleInvincible();
        if (UiKit.Button(new Rect(x + 228f, y, 180f, 42f), "HPを全回復", 16f, false, false)) RefillHp();
        LocGUI.Label(new Rect(x + 418f, y, w - 418f, 42f), "無敵の時も「本来受けたダメージ」は結果に出ます", note);
        y += 50f;
        LocGUI.Label(new Rect(x, y, w, 26f), "操作アシスト(設定の「高速時の自動補助」と同じ。闘技場の中だけ・設定は変わりません)", head); y += 28f;
        string[] am = Debug.isDebugBuild ? new[] { "OFF", "ON", "常時(開発版)" } : new[] { "OFF", "ON" };
        for (int i = 0; i < am.Length; i++) if (UiKit.Button(new Rect(x + i * 148f, y, 140f, 42f), am[i], 15f, cfg.assistMode == i, false)) { cfg.assistMode = i; ApplyAssist(); }
        float ex = x + am.Length * 148f + 12f;
        LocGUI.Label(new Rect(ex, y, 150f, 42f), $"働く速さ {cfg.assistEngageKmh:0}km/h~", UiKit.Label(14f));
        if (UiKit.Button(new Rect(ex + 152f, y, 46f, 42f), "-", 20f, false, false)) { cfg.assistEngageKmh = Mathf.Max(HighSpeedAssist.MinEngageKmh, cfg.assistEngageKmh - 5f); ApplyAssist(); }
        if (UiKit.Button(new Rect(ex + 202f, y, 46f, 42f), "+", 20f, false, false)) { cfg.assistEngageKmh = Mathf.Min(HighSpeedAssist.MaxEngageKmh, cfg.assistEngageKmh + 5f); ApplyAssist(); }
        y += 50f;
        var un = UiKit.Label(12f, TextAnchor.UpperLeft, false, new Color(1f, 0.75f, 0.6f)); un.wordWrap = true;
        LocGUI.Label(new Rect(x, y, w, 40f), "闘技場では未対応: FINAL EVOLUTION / AWAKENED / Mastery / ULTIMATE のゲージ / レベルアップ / ボス報酬 / BONUS ZONE(カードは通常の Lv の効果と COMBO だけ。報酬・記録・所持は一切変わりません)", un);
        y += 44f;
        if (Debug.isDebugBuild)
        {
            LocGUI.Label(new Rect(x, y, w, 22f), "開発版だけ", UiKit.Label(13f, TextAnchor.MiddleLeft, true, new Color(1f, 0.55f, 0.5f)));
            y += 24f;
            if (UiKit.Button(new Rect(x, y, 240f, 36f), $"未遭遇の敵も選ぶ: {(cfg.devAllEnemies ? "ON" : "OFF")}", 13f, cfg.devAllEnemies, false)) cfg.devAllEnemies = !cfg.devAllEnemies;
            if (UiKit.Button(new Rect(x + 248f, y, 240f, 36f), $"乱数を固定: {(cfg.seedFixed ? "ON" : "OFF")}", 13f, cfg.seedFixed, false)) cfg.seedFixed = !cfg.seedFixed;
            if (UiKit.Button(new Rect(x + 496f, y, 220f, 36f), $"障害物を壊す補助: {(cfg.assistBreakObstacles ? "ON" : "OFF")}", 13f, cfg.assistBreakObstacles, false)) { cfg.assistBreakObstacles = !cfg.assistBreakObstacles; ApplyAssist(); }
        }
    }

    // ---- 結果(今回と前回)
    void DrawResultTab(Rect c)
    {
        var cur = ArenaMode.Current; var prev = ArenaMode.Previous;
        var head = UiKit.Label(15f, TextAnchor.MiddleLeft, true, new Color(0.8f, 0.9f, 1f));
        string outcome = cur.defeated ? "倒れた" : cur.clearTime >= 0f ? $"全滅 {cur.clearTime:F2}秒" : cur.ended ? "終了" : "戦闘中";
        LocGUI.Label(new Rect(c.x, c.y, c.width, 30f), $"今回: {outcome}", UiKit.Label(20f, TextAnchor.MiddleLeft, true, Gold));
        if (!string.IsNullOrEmpty(status)) LocGUI.Label(new Rect(c.x + 260f, c.y, c.width - 260f, 30f), status, UiKit.Label(14f, TextAnchor.MiddleLeft, false, Soft));
        string[] rows = { "与ダメージ", "命中数", "撃破数", "撃破までの時間", "被ダメージ(HPが減った分)", "本来の被ダメージ(無敵/Shield も含む)", "戦闘時間" };
        System.Func<ArenaResult, string>[] vals =
        {
            r => r.dealt.ToString("N0"), r => r.hits.ToString(), r => $"{r.kills} / {r.spawned}",
            r => r.clearTime >= 0f ? $"{r.clearTime:F2}秒" : "—",
            r => r.taken.ToString(), r => $"{r.wouldTake}({r.wouldHits}回)", r => $"{r.time:F2}秒",
        };
        float y = c.y + 36f, col1 = c.x, col2 = c.x + c.width * 0.42f, col3 = c.x + c.width * 0.68f;
        LocGUI.Label(new Rect(col2, y, 200f, 26f), "今回", head);
        LocGUI.Label(new Rect(col3, y, 200f, 26f), "前回", head);
        y += 28f;
        var lab = UiKit.Label(16f, TextAnchor.MiddleLeft, false, new Color(1f, 0.95f, 0.85f));
        for (int i = 0; i < rows.Length; i++)
        {
            if (i % 2 == 0) UiKit.Fill(new Rect(col1, y, c.width, 30f), new Color(1f, 1f, 1f, 0.04f));
            LocGUI.Label(new Rect(col1 + 6f, y, col2 - col1, 30f), rows[i], lab);
            LocGUI.Label(new Rect(col2, y, col3 - col2, 30f), vals[i](cur), lab);
            LocGUI.Label(new Rect(col3, y, c.xMax - col3, 30f), prev != null ? vals[i](prev) : "—", lab);
            y += 32f;
        }
        var bl = UiKit.Label(13f, TextAnchor.UpperLeft, false, Soft); bl.wordWrap = true;
        LocGUI.Label(new Rect(c.x, y + 4f, c.width, 44f), $"今回の条件: {cur.label}\n試用のビルド: {cur.build}", bl);
        if (prev != null) LocGUI.Label(new Rect(c.x, y + 50f, c.width, 40f), $"前回の条件: {prev.label}\n前回のビルド: {prev.build}", UiKit.Label(12f, TextAnchor.UpperLeft, false, Dim));
        float by = c.yMax - 56f, bw = (c.width - 16f) / 3f;
        if (UiKit.Button(new Rect(c.x, by, bw, 52f), "同条件で再戦", 19f, true, false)) Rematch("result");
        if (UiKit.Button(new Rect(c.x + bw + 8f, by, bw, 52f), "設定を変更", 18f, false, false)) tab = 0;
        if (UiKit.Button(new Rect(c.x + 2f * (bw + 8f), by, bw, 52f), "ホームへ", 18f, false, false)) ExitHome();
    }

    // ===================================================================== 指で動かせる一覧(IMGUI のスクロールは指で引けないので、ドラッグで動かす)
    bool dragging; Vector2 dragStart; float dragStartScroll; bool dragMoved;
    static Vector2 activeListScrollRef;

    Vector2 BeginList(Rect r, Vector2 scroll, float contentH)
    {
        var e = Event.current;
        if (e.type == EventType.MouseDown && r.Contains(e.mousePosition)) { dragging = true; dragStart = e.mousePosition; dragStartScroll = scroll.y; dragMoved = false; activeListScrollRef = r.position; }
        if (dragging && activeListScrollRef == r.position)
        {
            if (e.type == EventType.MouseDrag)
            {
                float dy = e.mousePosition.y - dragStart.y;
                if (Mathf.Abs(dy) > 8f) dragMoved = true;
                if (dragMoved) { scroll.y = Mathf.Clamp(dragStartScroll - dy, 0f, Mathf.Max(0f, contentH - r.height)); e.Use(); }
            }
            else if (e.type == EventType.MouseUp) { dragging = false; if (dragMoved) e.Use(); }
        }
        UiKit.Fill(r, new Color(0.02f, 0.02f, 0.05f, 0.6f));
        // ゲームパッド: 一覧の端から先へ進もうとした時/右スティックで、見えている範囲を動かす
        if (e.type == EventType.Layout)
        {
            int req = PadNav.ScrollRequestFor(PadNav.ToScreen(r));
            if (req != 0) scroll.y = Mathf.Clamp(scroll.y + req * r.height * 0.5f, 0f, Mathf.Max(0f, contentH - r.height));
        }
        PadNav.PushClip(r);
        return GUI.BeginScrollView(r, scroll, new Rect(0, 0, r.width - 20f, Mathf.Max(contentH, r.height)));
    }
    void EndList() { GUI.EndScrollView(); PadNav.PopClip(); }
}

// 闘技場の相手の一覧(遭遇の正規データから: ボス = ProgressStats の会ったボス / 雑魚 = 距離で解放済み かつ そのステージを走ったことがある)
public static class ArenaCatalog
{
    public struct Item { public string label, id; public int kind; public bool known; public Sprite sprite; }
    public const string Unknown = "？？？(まだ会っていない)";

    public static List<Item> Items(int cat, bool devAll)
    {
        var list = new List<Item>();
        bool all = devAll && Debug.isDebugBuild;
        if (cat == 1)
        {
            foreach (var d in EnemyDatabase.AllEnemies)
            {
                if (d == null || d.bonusKind != BonusEnemyKind.None) continue;
                bool k = all || EnemySeen(d);
                list.Add(new Item { label = k ? (string.IsNullOrEmpty(d.displayName) ? d.enemyId : d.displayName) : Unknown, id = d.enemyId, known = k, sprite = k ? d.sprite : null });
            }
            list.Sort((a, b) => b.known.CompareTo(a.known));
        }
        else if (cat >= 2)
        {
            var t = cat == 2 ? typeof(WildBossKind) : cat == 3 ? typeof(CaveBossKind) : typeof(SkyBossKind);
            string fam = cat == 2 ? "Wild" : cat == 3 ? "Cave" : "Sky";
            foreach (var n in System.Enum.GetNames(t))
            {
                string key = fam + "/" + n;
                bool k = all || ProgressStats.HasSeenBoss(key);
                list.Add(new Item { label = k ? BossName(n) : Unknown, id = k ? key : "", kind = (int)System.Enum.Parse(t, n), known = k }); // まだ会っていないボスは名前も出さない
            }
        }
        return list;
    }

    public static bool EnemySeen(EnemyDefinition d)
    {
        if (d == null) return false;
        if (!UnlockManager.IsTargetUnlocked(UnlockType.Enemy, d.enemyId)) return false;
        var gm = GameManager.Instance;
        if (d.stageIds == null || d.stageIds.Length == 0 || gm == null) return true;
        foreach (var s in d.stageIds) if (!string.IsNullOrEmpty(s) && gm.GetStageBest(s) > 1.0) return true;
        return false;
    }

    // "BlackKnight" → "BLACK KNIGHT"
    public static string BossName(string enumName)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < enumName.Length; i++) { if (i > 0 && char.IsUpper(enumName[i]) && !char.IsUpper(enumName[i - 1])) sb.Append(' '); sb.Append(char.ToUpperInvariant(enumName[i])); }
        return sb.ToString();
    }

    // 構成に入っている相手が今のセーブで選べるか(まだ会っていない敵/ボスは不可。練習標的はいつでも)
    public static bool EntryKnown(ArenaEnemyEntry e, bool devAll)
    {
        if (e == null) return false;
        if (devAll && Debug.isDebugBuild) return true;
        switch (e.kind)
        {
            case ArenaEnemyKind.Dummy: return true;
            case ArenaEnemyKind.Enemy: return EnemySeen(EnemyDatabase.FindById(e.enemyId));
            case ArenaEnemyKind.WildBoss: return ProgressStats.HasSeenBoss("Wild/" + (WildBossKind)e.bossKind);
            case ArenaEnemyKind.CaveBoss: return ProgressStats.HasSeenBoss("Cave/" + (CaveBossKind)e.bossKind);
            default: return ProgressStats.HasSeenBoss("Sky/" + (SkyBossKind)e.bossKind);
        }
    }

    public static string NameOf(ArenaEnemyEntry e)
    {
        switch (e.kind)
        {
            case ArenaEnemyKind.Dummy: return "動かない標的";
            case ArenaEnemyKind.Enemy: { var d = EnemyDatabase.FindById(e.enemyId); return d != null && !string.IsNullOrEmpty(d.displayName) ? d.displayName : e.enemyId; }
            case ArenaEnemyKind.WildBoss: return BossName(((WildBossKind)e.bossKind).ToString());
            case ArenaEnemyKind.CaveBoss: return BossName(((CaveBossKind)e.bossKind).ToString());
            default: return BossName(((SkyBossKind)e.bossKind).ToString());
        }
    }
}
