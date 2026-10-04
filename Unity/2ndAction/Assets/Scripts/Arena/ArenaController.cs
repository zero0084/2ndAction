#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 開発用の闘技場の本体(2026-10-04、開発版のみ)。EndgameDebug.RunLaunchArena がランの開始後に作る。
//  ・Setup: 通常の出現/障害物/関門/BONUS/雲を止め、地面を平地だけにして、プレイヤーを新しい平地へ置き、見た目(ArenaStage)を作る。
//           距離条件 → キャラの基準値 + 試験のビルド(通常の取得の処理)→ 速度/操作アシスト/無敵 → 乱数のシード → 敵を出す → 計測を始める
//  ・戦闘中: 小さな計測の表示と左のボタン列(設定/再戦/結果/全回復/無敵/全削除/退出)だけ。設定を開くと試験を止める(時間/計測が進まない)
//  ・再戦/適用して再戦: シーンを読み直して同じ設定で作り直す(敵/弾/設置攻撃/演出/状態/クールダウンが残らない)。直前の結果は残して比べる
//  ・退出: ホームへ(シーンの読み直しで DEBUG RUN が終わり、保存は開始前へ戻る。ResetAll で試験の設定を戻す)
public class ArenaController : MonoBehaviour
{
    public static ArenaController Instance { get; private set; }
    static readonly object pauseOwner = new object();

    readonly List<EnemyController> spawned = new List<EnemyController>();
    readonly List<(EnemyController en, float offset)> dummies = new List<(EnemyController, float)>();
    int bossesSpawned;
    float groundY;
    string buildNote = "", status = "";
    bool panelOpen;
    int tab;
    float bannerT;

    void Awake() { Instance = this; }
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        TimeControl.Resume(pauseOwner);
        ArenaMode.BlockRects.Clear();
        ArenaMode.Defeated -= OnDefeated;
    }

    // シーンの読み直し/退出の時(EndgameDebug.SafeReset): 試験の設定を全部戻す
    public static void ResetAll()
    {
        ArenaMode.End();
        ArenaMode.BlockRects.Clear();
        TimeControl.Resume(pauseOwner);
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.ArenaRestore();
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = true; // シーンをまたいで残る
    }

    // ===================================================================== 準備
    public IEnumerator Setup()
    {
        var gm = GameManager.Instance; var pc = PlayerController.Instance; var tm = TerrainManager.Instance; var cfg = ArenaMode.Config;
        ArenaMode.Defeated += OnDefeated;
        // 通常のランの仕組みを止める(闘技場ではランダムな敵/障害物/関門/BONUS を出さない)
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
        foreach (var o in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) o.enabled = false;
        foreach (var o in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) o.enabled = false;
        foreach (var o in FindObjectsByType<ForegroundCloudLayer>(FindObjectsSortMode.None)) o.gameObject.SetActive(false);
        if (BonusZone.Instance != null) BonusZone.Instance.enabled = false;
        ClearEnemiesNow();
        foreach (var o in FindObjectsByType<ObstacleController>(FindObjectsSortMode.None)) if (o != null) Destroy(o.gameObject);
        // 地面: これから作る所はすべて平地。プレイヤーをその先へ置く
        tm.ConfigureArena();
        float x = tm.NextGenerateX + 30f;
        tm.GenerateNow(x + 160f);
        pc.ArenaPlaceAt(x);
        yield return null;
        groundY = tm.GetHeightAt(pc.transform.position.x) ?? (pc.transform.position.y - pc.groundOffset);
        ArenaStage.Create(groundY);
        // 距離条件 → ビルド(キャラの基準値から毎回作り直す)
        gm.ArenaSetDistance(cfg.distance);
        buildNote = gm.ArenaApplyBuild(cfg.build);
        pc.IsStandingIdle = cfg.speedMode == 0 || cfg.kmh <= 0.01f;
        ApplyAssist();
        ArenaMode.Invincible = cfg.invincible;
        if (cfg.seedFixed) Random.InitState(cfg.seed);
        yield return null;
        SpawnAll();
        ArenaMode.Current = new ArenaResult { label = Label(), spawned = spawned.Count + bossesSpawned };
        ArenaMode.BattleRunning = true;
        status = $"開始: {Label()}";
        Debug.Log($"[Arena] READY {Label()} build=[{buildNote}] ground={groundY:F2} enemies={spawned.Count}+bosses {bossesSpawned} hp={gm.Lives}/{gm.maxLives} atk={pc.EffectiveAttackPower} speed={pc.CurrentAutoRunSpeed * GameManager.KmhPerMps:F1}km/h");
    }

    static string Label()
    {
        var c = ArenaMode.Config;
        var def = CharacterDatabase.FindById(c.character);
        string sp = c.speedMode == 0 || c.kmh <= 0.01f ? "0km/h" : $"{c.kmh:0}km/h{(c.speedMode == 2 ? "固定" : "")}";
        return $"{(def != null ? def.displayName : c.character)} / カード{c.build.Count} / {sp} / {c.distance:0}m";
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
        var cfg = ArenaMode.Config;
        foreach (var e in cfg.enemies)
        {
            if (e == null) continue;
            switch (e.kind)
            {
                case ArenaEnemyKind.Enemy:
                case ArenaEnemyKind.Dummy:
                    for (int i = 0; i < Mathf.Clamp(e.count, 1, 20); i++) SpawnEnemy(e, e.ahead + i * e.spacing);
                    break;
                default:
                    if (BossManager.Instance != null)
                    {
                        int family = e.kind == ArenaEnemyKind.CaveBoss ? 2 : e.kind == ArenaEnemyKind.SkyBoss ? 3 : 1;
                        BossManager.Instance.ArenaSpawnBoss(family, e.bossKind, e.count);
                        bossesSpawned += Mathf.Clamp(e.count, 1, 4);
                    }
                    break;
            }
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

    void ClearEnemiesNow()
    {
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (e != null) Destroy(e.gameObject);
        if (TerrainManager.Instance != null) TerrainManager.Instance.ClearAllEnemies();
        if (BossManager.Instance != null) BossManager.Instance.ArenaClearBosses();
        spawned.Clear(); dummies.Clear(); bossesSpawned = 0;
    }

    int AliveEnemies => spawned.Count(e => e != null && !e.IsDying) + (BossManager.Instance != null ? BossManager.Instance.AliveBossCount : 0);

    // ===================================================================== 毎フレーム
    void Update()
    {
        if (!ArenaMode.Active) return;
        var r = ArenaMode.Current;
        if (ArenaMode.BattleRunning && !panelOpen)
        {
            r.time += Time.deltaTime; // 止めている間(設定を開く/停止メニュー)は進まない
            if (r.spawned > 0 && r.clearTime < 0f && AliveEnemies == 0 && r.time > 0.5f)
            {
                r.clearTime = r.time; r.ended = true; ArenaMode.BattleRunning = false; bannerT = 4f;
                status = $"全滅 {r.clearTime:F2}秒";
                Debug.Log($"[Arena] CLEAR in {r.clearTime:F2}s dealt={r.dealt} hits={r.hits} taken={r.taken} would={r.wouldTake}");
            }
        }
        if (bannerT > 0f) bannerT -= Time.unscaledDeltaTime;
    }

    void LateUpdate()
    {
        // 動かない標的: 走っている時はプレイヤーとの間合いを保つ(止まっている時はその場)
        var pc = PlayerController.Instance;
        if (pc == null || dummies.Count == 0 || pc.CurrentAutoRunSpeed <= 0.01f) return;
        foreach (var (en, off) in dummies)
            if (en != null) en.transform.position = new Vector3(pc.transform.position.x + off, en.transform.position.y, en.transform.position.z);
    }

    void OnDefeated()
    {
        var r = ArenaMode.Current;
        r.ended = true; ArenaMode.BattleRunning = false;
        status = $"倒れました({r.time:F1}秒)。「同条件で再戦」ですぐやり直せます";
        Debug.Log($"[Arena] DEFEATED at {r.time:F2}s ({r.defeatReason})");
        SetPanel(true); tab = 4;
    }

    // ===================================================================== 操作
    public void Rematch(string why)
    {
        var r = ArenaMode.Current;
        if (r != null && (r.time > 0.1f || r.dealt > 0)) ArenaMode.Previous = r;
        SetPanel(false);
        EndgameDebug.LaunchArena(why);
    }
    public void EndTrial()
    {
        var r = ArenaMode.Current;
        r.ended = true; ArenaMode.BattleRunning = false;
        status = "試験を終えました(結果)";
        SetPanel(true); tab = 4;
    }
    public void RefillHp() { if (GameManager.Instance != null) GameManager.Instance.ArenaRefillLives(); status = "HP全回復"; }
    public void ToggleInvincible() { ArenaMode.Config.invincible = ArenaMode.Invincible = !ArenaMode.Invincible; status = $"無敵 {(ArenaMode.Invincible ? "ON" : "OFF")}"; }
    public void ClearEnemies() { ClearEnemiesNow(); status = "敵を全削除"; }
    public void ExitHome()
    {
        SetPanel(false);
        var gm = GameManager.Instance;
        status = "ホームへ";
        if (gm != null) gm.ReturnToHome();
    }
    public void SetPanel(bool on)
    {
        if (panelOpen == on) return;
        panelOpen = on;
        if (on) TimeControl.Pause(pauseOwner);
        else { TimeControl.Resume(pauseOwner); UiInputGate.LatchUntilRelease(); }
    }
    public bool PanelOpen => panelOpen;

    // ===================================================================== 表示
    static float S => Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 1f, 2.6f);
    Vector2 listScroll, buildScroll, ownedScroll, enemyScroll, enemyListScroll;
    string search = "";
    int cardFilter, enemyCat;
    string enemyPickId = "goblin"; int enemyPickBoss; int pickCount = 1; float pickAhead = 8f, pickSpacing = 2.5f; int pickTier = 1;
    string kmhText = "", distText = "", seedText = "";

    void OnGUI()
    {
        if (!ArenaMode.Active) return;
        GUI.depth = -2000;
        float s = S;
        var keep = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float W = Screen.width / s, H = Screen.height / s;
        ArenaMode.BlockRects.Clear();
        if (panelOpen) DrawPanel(W, H);
        else DrawHud(W, H, s);
        GUI.matrix = keep;
    }

    void DrawHud(float W, float H, float s)
    {
        var r = ArenaMode.Current;
        // 計測(上の中央、Lv/EXP の下)
        float top = (Screen.height - (Screen.safeArea.y + Screen.safeArea.height) + 28f + 54f + 10f) / s;
        string line = $"闘技場  与 {r.dealt:N0}  命中 {r.hits}  被 {r.taken}(本来 {r.wouldTake})  撃破 {r.kills}/{r.spawned}  {r.time:F1}s" + (r.clearTime >= 0f ? $"  全滅 {r.clearTime:F2}s" : "") + (r.defeated ? "  倒れた" : "") + (ArenaMode.Invincible ? "  [無敵]" : "");
        var st = UiKit.Label(14f, TextAnchor.MiddleCenter, true, new Color(1f, 0.92f, 0.7f));
        var size = st.CalcSize(new GUIContent(line));
        var lr = new Rect(W * 0.5f - size.x * 0.5f - 10f, top, size.x + 20f, 26f);
        UiKit.Fill(lr, new Color(0.05f, 0.03f, 0.08f, 0.72f));
        GUI.Label(lr, line, st);
        // 左のボタン列
        string[] labels = { "≡ 設定", "同条件で再戦", r.ended ? "結果" : "試験を終える", "HP全回復", ArenaMode.Invincible ? "無敵 ON" : "無敵 OFF", "敵を全削除", "ホームへ退出" };
        float bw = 128f, bh = 44f, gap = 6f, x0 = Screen.safeArea.x / s + 10f, y0 = Mathf.Max(top + 34f, H * 0.3f);
        for (int i = 0; i < labels.Length; i++)
        {
            var br = new Rect(x0, y0 + i * (bh + gap), bw, bh);
            ArenaMode.BlockRects.Add(new Rect(br.x * s, br.y * s, br.width * s, br.height * s));
            if (UiKit.Button(br, labels[i], 14f, i == 1 || (i == 4 && ArenaMode.Invincible), false))
            {
                switch (i)
                {
                    case 0: SetPanel(true); break;
                    case 1: Rematch("hud"); break;
                    case 2: if (r.ended) { SetPanel(true); tab = 4; } else EndTrial(); break;
                    case 3: RefillHp(); break;
                    case 4: ToggleInvincible(); break;
                    case 5: ClearEnemies(); break;
                    case 6: ExitHome(); break;
                }
            }
        }
        if (bannerT > 0f && r.clearTime >= 0f)
        {
            var bs = UiKit.Label(30f, TextAnchor.MiddleCenter, true, new Color(1f, 0.9f, 0.4f));
            GUI.Label(new Rect(0f, H * 0.36f, W, 50f), $"全滅  {r.clearTime:F2}秒", bs);
        }
        if (!string.IsNullOrEmpty(status)) GUI.Label(new Rect(x0, y0 + labels.Length * (bh + gap), 420f, 22f), status, UiKit.Label(12f, TextAnchor.MiddleLeft, false, new Color(0.85f, 0.9f, 1f)));
    }

    static readonly string[] Tabs = { "キャラ", "カード", "敵", "速度/操作", "計測" };

    void DrawPanel(float W, float H)
    {
        var p = new Rect(12f, 12f, W - 24f, H - 24f);
        UiKit.Fill(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, 0.45f));
        OrnateUi.DrawPanel(p, 0.96f);
        GUI.Label(new Rect(p.x + 16f, p.y + 8f, 260f, 34f), "闘技場(試験中は停止)", UiKit.Label(20f, TextAnchor.MiddleLeft, true, new Color(1f, 0.75f, 0.45f)));
        float tw = 118f;
        for (int i = 0; i < Tabs.Length; i++)
            if (UiKit.Button(new Rect(p.x + 290f + i * (tw + 6f), p.y + 8f, tw, 34f), Tabs[i], 15f, tab == i, false)) tab = i;
        var c = new Rect(p.x + 16f, p.y + 50f, p.width - 32f, p.height - 112f);
        switch (tab)
        {
            case 0: DrawCharTab(c); break;
            case 1: DrawCardTab(c); break;
            case 2: DrawEnemyTab(c); break;
            case 3: DrawSpeedTab(c); break;
            default: DrawResultTab(c); break;
        }
        float by = p.yMax - 54f, bw = (p.width - 32f - 24f) / 4f;
        if (UiKit.Button(new Rect(p.x + 16f, by, bw, 44f), "適用して再戦", 17f, true, false)) Rematch("apply");
        if (UiKit.Button(new Rect(p.x + 16f + (bw + 8f), by, bw, 44f), "閉じて再開", 17f, false, false)) SetPanel(false);
        var r = ArenaMode.Current;
        if (UiKit.Button(new Rect(p.x + 16f + 2f * (bw + 8f), by, bw, 44f), r.ended ? "(試験は終了済み)" : "試験を終える(結果)", 15f, false, false, !r.ended)) EndTrial();
        if (UiKit.Button(new Rect(p.x + 16f + 3f * (bw + 8f), by, bw, 44f), "ホームへ退出", 17f, false, false)) ExitHome();
        GUI.Label(new Rect(p.x + 16f, by - 22f, p.width - 32f, 20f), "変更は「適用して再戦」で反映(シーンを読み直して同じ条件で作り直す)。無敵/HP/操作アシストはすぐ反映。保存データは変わりません(DEBUG RUN)", UiKit.Label(11f, TextAnchor.MiddleLeft, false, new Color(0.8f, 0.8f, 0.88f)));
    }

    // ---- キャラ
    void DrawCharTab(Rect c)
    {
        var cfg = ArenaMode.Config;
        GUI.Label(new Rect(c.x, c.y, c.width, 22f), "全プレイアブルキャラ(未所持/未解放でも選べる)。固有の能力/攻撃は通常どおり", UiKit.Label(13f, TextAnchor.MiddleLeft, false, new Color(0.85f, 0.85f, 0.9f)));
        var chars = CharacterDatabase.AllCharacters;
        float bw = (c.width - 18f) / 4f;
        for (int i = 0; i < chars.Count; i++)
        {
            var d = chars[i];
            var br = new Rect(c.x + (i % 4) * (bw + 6f), c.y + 28f + (i / 4) * 50f, bw, 44f);
            if (UiKit.Button(br, d.displayName, 15f, cfg.character == d.characterId, false)) cfg.character = d.characterId;
        }
        float y = c.y + 28f + Mathf.CeilToInt(chars.Count / 4f) * 50f + 10f;
        DrawStats(new Rect(c.x, y, c.width, c.yMax - y));
    }

    void DrawStats(Rect r)
    {
        var gm = GameManager.Instance; var pc = PlayerController.Instance;
        if (gm == null || pc == null) return;
        var cfg = ArenaMode.Config;
        var lab = UiKit.Label(13f, TextAnchor.UpperLeft, false, new Color(1f, 0.92f, 0.72f));
        float baseKmh = cfg.speedMode == 0 ? 0f : cfg.kmh;
        string text =
            $"今の試験: {CharacterDatabase.FindById(gm.ActiveRunCharacterId)?.displayName}  HP {gm.Lives}/{gm.maxLives}(封印 {gm.SealedHearts})  攻撃 {pc.EffectiveAttackPower}(カード ×{pc.CardAttackFactor:F2})  攻撃速度 ×{1f / Mathf.Max(0.01f, pc.AttackSpeedMultiplier):F2}  範囲 ×{pc.CardRangeFactor:F2}  Shield {pc.ShieldCapacity}\n" +
            $"速度: 基準 {baseKmh:0} km/h  → 実効 {pc.CurrentAutoRunSpeed * GameManager.KmhPerMps:0.0} km/h  (キャラ×カードの補正 ×{pc.ArenaSpeedFactor:F2}{(cfg.speedMode == 2 ? "、固定なので移動には入れない" : "")})  ジャンプ力 {pc.jumpForce:F1}  回数 {pc.maxJumps}\n" +
            $"距離条件 {gm.MaxDistance:0}m(雑魚の基本HP {(DistanceTierManager.Instance != null ? DistanceTierManager.Instance.EnemyHpFor(1f) : 0)})  ビルド: {buildNote}";
        GUI.Label(r, text, lab);
    }

    // ---- カード
    static readonly string[] CardFilters = { "すべて", "移動", "攻撃", "防御", "成長", "回復", "特殊", "リスク" };
    void DrawCardTab(Rect c)
    {
        var cfg = ArenaMode.Config;
        float lw = c.width * 0.5f - 8f;
        // 検索 + 分類
        GUI.Label(new Rect(c.x, c.y, 60f, 30f), "検索", UiKit.Label(14f));
        search = GUI.TextField(new Rect(c.x + 50f, c.y, lw - 50f, 30f), search ?? "");
        float fw = (lw - 7f * 4f) / 8f;
        for (int i = 0; i < CardFilters.Length; i++)
            if (UiKit.Button(new Rect(c.x + i * (fw + 4f), c.y + 36f, fw, 30f), CardFilters[i], 11f, cardFilter == i, false)) cardFilter = i;
        var cards = CardDatabase.AllCards.Where(d => d != null && !CardVariant.IsVariantKey(d.cardId)
            && (cardFilter == 0 || (int)d.category == cardFilter - 1)
            && (string.IsNullOrEmpty(search) || d.cardName.ToLowerInvariant().Contains(search.ToLowerInvariant()) || d.cardId.Contains(search.ToLowerInvariant()))).OrderBy(d => d.sortOrder).ToList();
        var listR = new Rect(c.x, c.y + 72f, lw, c.height * 0.55f - 72f);
        float rowH = 38f;
        listScroll = BeginList(listR, listScroll, cards.Count * rowH);
        for (int i = 0; i < cards.Count; i++)
        {
            var d = cards[i];
            var rr = new Rect(0, i * rowH, lw - 20f, rowH - 4f);
            UiKit.Fill(rr, new Color(0.08f, 0.09f, 0.16f, 0.8f));
            GUI.Label(new Rect(rr.x + 8f, rr.y, rr.width - 90f, rr.height), $"{d.cardName}  <size=11>{d.RarityStars}</size>", UiKit.Label(13f));
            if (UiKit.Button(new Rect(rr.xMax - 80f, rr.y + 2f, 76f, rr.height - 4f), "+ 追加", 13f, false, false)) AddCard(d.cardId, false);
        }
        EndList();
        // 所持カードのコピー(合成カードの能力一式を試す)
        var ownR = new Rect(c.x, listR.yMax + 26f, lw, c.yMax - listR.yMax - 26f);
        GUI.Label(new Rect(c.x, listR.yMax + 4f, lw, 20f), "所持カードからコピー(合成Lv/能力一式はそのまま。1回の取得で全能力)", UiKit.Label(12f, TextAnchor.MiddleLeft, true, new Color(0.8f, 0.9f, 1f)));
        var owned = CardInventory.Stacks.Where(st => st.count > 0 && (st.level > 1 || CardVariant.IsVariantKey(st.cardId))).ToList();
        ownedScroll = BeginList(ownR, ownedScroll, owned.Count * rowH);
        for (int i = 0; i < owned.Count; i++)
        {
            var st = owned[i];
            var v = CardVariant.Parse(st.cardId); var d = CardDatabase.FindById(st.cardId);
            var rr = new Rect(0, i * rowH, lw - 20f, rowH - 4f);
            UiKit.Fill(rr, new Color(0.06f, 0.1f, 0.14f, 0.8f));
            GUI.Label(new Rect(rr.x + 8f, rr.y, rr.width - 90f, rr.height), $"{d?.cardName}  合成Lv{st.level}  能力{(v != null ? v.AbilityCount : 1)}種", UiKit.Label(12f));
            if (UiKit.Button(new Rect(rr.xMax - 80f, rr.y + 2f, 76f, rr.height - 4f), "コピー", 13f, false, false)) AddCard(st.cardId, true);
        }
        EndList();
        // ビルド
        float rx = c.x + lw + 16f, rw = c.width - lw - 16f;
        GUI.Label(new Rect(rx, c.y, rw - 100f, 30f), $"試験のビルド({cfg.build.Count})  ラン中の取得Lv(能力ごとに最大9)", UiKit.Label(14f, TextAnchor.MiddleLeft, true, new Color(1f, 0.85f, 0.5f)));
        if (UiKit.Button(new Rect(rx + rw - 96f, c.y, 96f, 30f), "全解除", 13f, false, false)) cfg.build.Clear();
        var bR = new Rect(rx, c.y + 36f, rw, c.height * 0.6f - 36f);
        buildScroll = BeginList(bR, buildScroll, cfg.build.Count * rowH);
        for (int i = 0; i < cfg.build.Count; i++)
        {
            var e = cfg.build[i];
            var d = CardDatabase.FindById(e.key); var v = CardVariant.Parse(e.key);
            var rr = new Rect(0, i * rowH, rw - 20f, rowH - 4f);
            UiKit.Fill(rr, new Color(0.12f, 0.09f, 0.05f, 0.85f));
            string name = d != null ? d.cardName : e.key;
            string lv = e.owned ? $"所持コピー 合成Lv{(v != null ? v.level : 1)} 能力{(v != null ? v.AbilityCount : 1)}種 ×{e.times}回" : $"取得Lv {e.times}";
            GUI.Label(new Rect(rr.x + 8f, rr.y, rr.width - 150f, rr.height), $"{name}  <size=11>{lv}</size>", UiKit.Label(13f));
            if (UiKit.Button(new Rect(rr.xMax - 144f, rr.y + 2f, 44f, rr.height - 4f), "-", 15f, false, false)) { e.times--; if (e.times <= 0) { cfg.build.RemoveAt(i); break; } }
            if (UiKit.Button(new Rect(rr.xMax - 96f, rr.y + 2f, 44f, rr.height - 4f), "+", 15f, false, false)) e.times = Mathf.Min(GameManager.MaxRunCardLevel, e.times + 1);
            if (UiKit.Button(new Rect(rr.xMax - 48f, rr.y + 2f, 44f, rr.height - 4f), "×", 15f, false, false)) { cfg.build.RemoveAt(i); break; }
        }
        EndList();
        DrawStats(new Rect(rx, bR.yMax + 6f, rw, c.yMax - bR.yMax - 6f));
    }

    void AddCard(string key, bool owned)
    {
        var cfg = ArenaMode.Config;
        var e = cfg.build.FirstOrDefault(x => x.key == key);
        if (e != null) e.times = Mathf.Min(GameManager.MaxRunCardLevel, e.times + 1);
        else cfg.build.Add(new ArenaBuildEntry { key = key, times = 1, owned = owned });
    }

    // ---- 敵
    static readonly string[] EnemyCats = { "雑魚", "荒野ボス", "洞窟ボス", "天空ボス", "動かない標的" };
    void DrawEnemyTab(Rect c)
    {
        var cfg = ArenaMode.Config;
        float lw = c.width * 0.5f - 8f;
        float cw = (lw - 16f) / 5f;
        for (int i = 0; i < EnemyCats.Length; i++)
            if (UiKit.Button(new Rect(c.x + i * (cw + 4f), c.y, cw, 32f), EnemyCats[i], 12f, enemyCat == i, false)) enemyCat = i;
        var names = new List<(string label, string id, int kind)>();
        if (enemyCat == 0) foreach (var d in EnemyDatabase.AllEnemies.OrderBy(d => d.enemyId)) names.Add((string.IsNullOrEmpty(d.displayName) ? d.enemyId : d.displayName, d.enemyId, 0));
        else if (enemyCat == 1) foreach (var n in System.Enum.GetNames(typeof(WildBossKind))) names.Add((n, n, (int)System.Enum.Parse(typeof(WildBossKind), n)));
        else if (enemyCat == 2) foreach (var n in System.Enum.GetNames(typeof(CaveBossKind))) names.Add((n, n, (int)System.Enum.Parse(typeof(CaveBossKind), n)));
        else if (enemyCat == 3) foreach (var n in System.Enum.GetNames(typeof(SkyBossKind))) names.Add((n, n, (int)System.Enum.Parse(typeof(SkyBossKind), n)));
        float rowH = 36f;
        var listR = new Rect(c.x, c.y + 38f, lw, c.height - 38f - 132f);
        enemyListScroll = BeginList(listR, enemyListScroll, Mathf.Max(1, names.Count) * rowH);
        if (enemyCat == 4) GUI.Label(new Rect(8f, 0f, lw - 20f, 60f), "攻撃の確認用。動かず攻撃もしない(接触ダメージなし・HP 99,999,999)。走っている時はプレイヤーとの間合いを保つ", UiKit.Label(12f, TextAnchor.UpperLeft));
        for (int i = 0; i < names.Count; i++)
        {
            var n = names[i];
            bool sel = enemyCat == 0 ? enemyPickId == n.id : enemyPickBoss == n.kind;
            if (UiKit.Button(new Rect(0, i * rowH, lw - 20f, rowH - 4f), n.label, 13f, sel, false)) { if (enemyCat == 0) enemyPickId = n.id; else enemyPickBoss = n.kind; }
        }
        EndList();
        // 数 / 前方 / 間隔 / Tier
        float y = listR.yMax + 6f;
        y = Stepper(c.x, y, lw, "数", pickCount.ToString(), () => pickCount = Mathf.Max(1, pickCount - 1), () => pickCount = Mathf.Min(enemyCat >= 1 && enemyCat <= 3 ? 4 : 20, pickCount + 1));
        if (enemyCat == 0 || enemyCat == 4)
        {
            y = Stepper(c.x, y, lw, "前方(m)", pickAhead.ToString("0.#"), () => pickAhead = Mathf.Max(1.5f, pickAhead - 1f), () => pickAhead = Mathf.Min(60f, pickAhead + 1f));
            y = Stepper(c.x, y, lw, "間隔(m)", pickSpacing.ToString("0.#"), () => pickSpacing = Mathf.Max(0.5f, pickSpacing - 0.5f), () => pickSpacing = Mathf.Min(20f, pickSpacing + 0.5f));
        }
        else GUI.Label(new Rect(c.x, y, lw, 40f), "ボスの位置はボス自身の登場(前方から走り込み/間合い)で決まる", UiKit.Label(11f, TextAnchor.UpperLeft));
        if (enemyCat == 0)
        {
            float tw = (lw - 60f - 16f) / 5f;
            GUI.Label(new Rect(c.x + lw * 0.5f + 8f, listR.yMax + 6f, 50f, 30f), "Tier", UiKit.Label(13f));
            for (int t = 0; t < 5; t++) if (UiKit.Button(new Rect(c.x + lw * 0.5f + 50f + t * 34f, listR.yMax + 6f, 30f, 30f), "T" + t, 11f, pickTier == t, false)) pickTier = t;
        }
        if (UiKit.Button(new Rect(c.x + lw * 0.5f + 8f, c.yMax - 40f, lw * 0.5f - 8f, 38f), "構成に追加", 15f, true, false))
        {
            var e = new ArenaEnemyEntry { kind = (ArenaEnemyKind)Mathf.Clamp(enemyCat, 0, 4), enemyId = enemyPickId, bossKind = enemyPickBoss, count = pickCount, ahead = pickAhead, spacing = pickSpacing, tier = pickTier };
            if (enemyCat == 4) e.kind = ArenaEnemyKind.Dummy;
            cfg.enemies.Add(e);
        }
        // 今の構成 + 距離条件
        float rx = c.x + lw + 16f, rw = c.width - lw - 16f;
        GUI.Label(new Rect(rx, c.y, rw - 100f, 30f), $"敵の構成({cfg.enemies.Count})", UiKit.Label(14f, TextAnchor.MiddleLeft, true, new Color(1f, 0.85f, 0.5f)));
        if (UiKit.Button(new Rect(rx + rw - 96f, c.y, 96f, 30f), "構成を空に", 12f, false, false)) cfg.enemies.Clear();
        var eR = new Rect(rx, c.y + 36f, rw, c.height - 36f - 120f);
        enemyScroll = BeginList(eR, enemyScroll, cfg.enemies.Count * rowH);
        for (int i = 0; i < cfg.enemies.Count; i++)
        {
            var e = cfg.enemies[i];
            string nm = e.kind == ArenaEnemyKind.Enemy ? e.enemyId + $" T{e.tier}" : e.kind == ArenaEnemyKind.Dummy ? "動かない標的" : e.kind == ArenaEnemyKind.WildBoss ? ((WildBossKind)e.bossKind).ToString() : e.kind == ArenaEnemyKind.CaveBoss ? ((CaveBossKind)e.bossKind).ToString() : ((SkyBossKind)e.bossKind).ToString();
            var rr = new Rect(0, i * rowH, rw - 20f, rowH - 4f);
            UiKit.Fill(rr, new Color(0.12f, 0.06f, 0.06f, 0.85f));
            string pos = e.kind == ArenaEnemyKind.Enemy || e.kind == ArenaEnemyKind.Dummy ? $" 前方{e.ahead:0.#}m 間隔{e.spacing:0.#}m" : " (ボス)";
            GUI.Label(new Rect(8f, rr.y, rr.width - 60f, rr.height), $"{nm} ×{e.count}{pos}", UiKit.Label(13f));
            if (UiKit.Button(new Rect(rr.xMax - 48f, rr.y + 2f, 44f, rr.height - 4f), "×", 15f, false, false)) { cfg.enemies.RemoveAt(i); break; }
        }
        EndList();
        float dy = eR.yMax + 8f;
        GUI.Label(new Rect(rx, dy, rw, 22f), $"距離条件(敵の強さ。距離のイベントは起きない): {cfg.distance:0} m", UiKit.Label(13f, TextAnchor.MiddleLeft, true));
        float[] dp = { 0f, 1000f, 5000f, 10000f, 30000f, 50000f, 99000f };
        float dw = (rw - 6f * 4f) / 7f;
        for (int i = 0; i < dp.Length; i++) if (UiKit.Button(new Rect(rx + i * (dw + 4f), dy + 26f, dw, 32f), dp[i] >= 1000f ? $"{dp[i] / 1000f:0}km" : "0", 12f, Mathf.Approximately(cfg.distance, dp[i]), false)) cfg.distance = dp[i];
        distText = GUI.TextField(new Rect(rx, dy + 64f, rw * 0.5f, 30f), string.IsNullOrEmpty(distText) ? cfg.distance.ToString("0") : distText);
        if (UiKit.Button(new Rect(rx + rw * 0.5f + 8f, dy + 64f, rw * 0.5f - 8f, 30f), "数値で指定", 12f, false, false) && float.TryParse(distText, out float dv)) { cfg.distance = Mathf.Clamp(dv, 0f, 99999f); distText = ""; }
    }

    float Stepper(float x, float y, float w, string label, string value, System.Action minus, System.Action plus)
    {
        float half = w * 0.5f - 8f;
        GUI.Label(new Rect(x, y, 80f, 30f), label, UiKit.Label(13f));
        if (UiKit.Button(new Rect(x + 80f, y, 40f, 30f), "-", 15f, false, false)) minus();
        GUI.Label(new Rect(x + 122f, y, half - 166f, 30f), value, UiKit.Label(14f, TextAnchor.MiddleCenter, true));
        if (UiKit.Button(new Rect(x + half - 42f, y, 40f, 30f), "+", 15f, false, false)) plus();
        return y + 34f;
    }

    // ---- 速度 / 操作
    void DrawSpeedTab(Rect c)
    {
        var cfg = ArenaMode.Config; var pc = PlayerController.Instance;
        float x = c.x, y = c.y, w = c.width;
        GUI.Label(new Rect(x, y, w, 22f), "速度(自然加速は止める。0km/h は自動前進だけを止め、時間/攻撃/ジャンプ/重力/敵は通常どおり)", UiKit.Label(13f, TextAnchor.MiddleLeft, true, new Color(1f, 0.85f, 0.5f)));
        y += 26f;
        string[] modes = { "停止", "基準速度(カード/キャラの補正あり)", "実効速度固定(補正は移動に入れない)" };
        float mw = (w - 16f) / 3f;
        for (int i = 0; i < 3; i++) if (UiKit.Button(new Rect(x + i * (mw + 8f), y, mw, 36f), modes[i], 13f, cfg.speedMode == i, false)) cfg.speedMode = i;
        y += 42f;
        float[] presets = { 0f, 30f, 60f, 100f, 150f, 200f, 300f };
        float pw = (w - 6f * 6f) / 7f;
        for (int i = 0; i < presets.Length; i++) if (UiKit.Button(new Rect(x + i * (pw + 6f), y, pw, 36f), $"{presets[i]:0}km/h", 13f, Mathf.Approximately(cfg.kmh, presets[i]), false)) { cfg.kmh = presets[i]; if (presets[i] > 0f && cfg.speedMode == 0) cfg.speedMode = 1; }
        y += 42f;
        kmhText = GUI.TextField(new Rect(x, y, 160f, 32f), string.IsNullOrEmpty(kmhText) ? cfg.kmh.ToString("0") : kmhText);
        if (UiKit.Button(new Rect(x + 168f, y, 140f, 32f), "数値で指定", 13f, false, false) && float.TryParse(kmhText, out float kv)) { cfg.kmh = Mathf.Clamp(kv, 0f, 600f); kmhText = ""; }
        if (pc != null) GUI.Label(new Rect(x + 320f, y, w - 320f, 32f), $"今: 基準 {(cfg.speedMode == 0 ? 0f : cfg.kmh):0} km/h → 実効 {pc.CurrentAutoRunSpeed * GameManager.KmhPerMps:0.0} km/h(補正 ×{pc.ArenaSpeedFactor:F2})。変更は「適用して再戦」で", UiKit.Label(12f));
        y += 40f;
        // 操作アシスト(試験の中だけ。通常の設定は変えない)
        GUI.Label(new Rect(x, y, w, 22f), "操作アシスト(高速時の自動補助。闘技場の中だけ・保存しない)", UiKit.Label(13f, TextAnchor.MiddleLeft, true, new Color(1f, 0.85f, 0.5f)));
        y += 26f;
        string[] am = { "OFF", "高速時のみ", "常時" };
        for (int i = 0; i < 3; i++) if (UiKit.Button(new Rect(x + i * (130f + 8f), y, 130f, 34f), am[i], 14f, cfg.assistMode == i, false)) { cfg.assistMode = i; ApplyAssist(); }
        float ex = x + 3f * 138f + 16f;
        GUI.Label(new Rect(ex, y, 120f, 34f), $"開始 {cfg.assistEngageKmh:0}km/h", UiKit.Label(13f));
        if (UiKit.Button(new Rect(ex + 120f, y, 40f, 34f), "-", 15f, false, false)) { cfg.assistEngageKmh = Mathf.Max(HighSpeedAssist.MinEngageKmh, cfg.assistEngageKmh - 5f); ApplyAssist(); }
        if (UiKit.Button(new Rect(ex + 164f, y, 40f, 34f), "+", 15f, false, false)) { cfg.assistEngageKmh = Mathf.Min(HighSpeedAssist.MaxEngageKmh, cfg.assistEngageKmh + 5f); ApplyAssist(); }
        y += 40f;
        if (UiKit.Button(new Rect(x, y, 260f, 34f), $"障害物を壊して通る: {(cfg.assistBreakObstacles ? "ON" : "OFF")}", 13f, cfg.assistBreakObstacles, false)) { cfg.assistBreakObstacles = !cfg.assistBreakObstacles; ApplyAssist(); }
        if (UiKit.Button(new Rect(x + 268f, y, 300f, 34f), $"着地前の二段ジャンプ: {(cfg.assistEarlyDoubleJump ? "ON" : "OFF")}", 13f, cfg.assistEarlyDoubleJump, false)) { cfg.assistEarlyDoubleJump = !cfg.assistEarlyDoubleJump; ApplyAssist(); }
        y += 44f;
        if (UiKit.Button(new Rect(x, y, 200f, 36f), $"無敵: {(ArenaMode.Invincible ? "ON" : "OFF")}", 15f, ArenaMode.Invincible, false)) ToggleInvincible();
        if (UiKit.Button(new Rect(x + 208f, y, 160f, 36f), "HP全回復", 15f, false, false)) RefillHp();
        if (UiKit.Button(new Rect(x + 376f, y, 160f, 36f), "敵を全削除", 15f, false, false)) ClearEnemies();
        y += 44f;
        if (UiKit.Button(new Rect(x, y, 200f, 34f), $"乱数シード固定: {(cfg.seedFixed ? "ON" : "OFF")}", 13f, cfg.seedFixed, false)) cfg.seedFixed = !cfg.seedFixed;
        seedText = GUI.TextField(new Rect(x + 208f, y, 140f, 34f), string.IsNullOrEmpty(seedText) ? cfg.seed.ToString() : seedText);
        if (UiKit.Button(new Rect(x + 356f, y, 100f, 34f), "指定", 13f, false, false) && int.TryParse(seedText, out int sv)) { cfg.seed = sv; seedText = ""; }
        if (UiKit.Button(new Rect(x + 464f, y, 140f, 34f), "新しいシード", 13f, false, false)) { cfg.seed = Random.Range(1, 999999); seedText = ""; }
    }

    // ---- 計測
    void DrawResultTab(Rect c)
    {
        var cur = ArenaMode.Current; var prev = ArenaMode.Previous;
        GUI.Label(new Rect(c.x, c.y, c.width, 24f), $"今回: {cur.label}{(prev != null ? "    直前: " + prev.label : "")}", UiKit.Label(13f, TextAnchor.MiddleLeft, true, new Color(1f, 0.85f, 0.5f)));
        string[] rows = { "与ダメージ", "命中数", "DPS(与ダメージ/戦闘時間)", "被ダメージ(実際のHP減少)", "本来の被ダメージ(無敵/シールド含む)", "本来の被弾回数", "撃破数", "戦闘時間(停止中を除く)", "全滅までの時間", "結果" };
        System.Func<ArenaResult, string>[] vals =
        {
            r => r.dealt.ToString("N0"), r => r.hits.ToString(), r => (r.time > 0.01f ? r.dealt / r.time : 0f).ToString("N1"),
            r => r.taken.ToString(), r => r.wouldTake.ToString(), r => r.wouldHits.ToString(), r => $"{r.kills} / {r.spawned}",
            r => $"{r.time:F2}s", r => r.clearTime >= 0f ? $"{r.clearTime:F2}s" : "—",
            r => r.defeated ? "倒れた" : r.clearTime >= 0f ? "全滅" : r.ended ? "手動で終了" : "試験中",
        };
        float y = c.y + 30f, col1 = c.x, col2 = c.x + c.width * 0.42f, col3 = c.x + c.width * 0.66f;
        var head = UiKit.Label(13f, TextAnchor.MiddleLeft, true, new Color(0.8f, 0.9f, 1f));
        GUI.Label(new Rect(col2, y, 200f, 24f), "今回", head);
        GUI.Label(new Rect(col3, y, 200f, 24f), "直前", head);
        y += 26f;
        var lab = UiKit.Label(14f, TextAnchor.MiddleLeft, false, new Color(1f, 0.95f, 0.85f));
        for (int i = 0; i < rows.Length; i++)
        {
            GUI.Label(new Rect(col1, y, col2 - col1, 26f), rows[i], lab);
            GUI.Label(new Rect(col2, y, col3 - col2, 26f), vals[i](cur), lab);
            GUI.Label(new Rect(col3, y, c.xMax - col3, 26f), prev != null ? vals[i](prev) : "—", lab);
            y += 28f;
        }
        GUI.Label(new Rect(c.x, y + 6f, c.width, 40f), "時間はゲームの時間(設定を開いている間/停止中は進まない)。命中数は敵/ボスへのダメージの回数(継続ダメージ/追加攻撃も含む)。「同条件で再戦」で今回の結果が「直前」になる", UiKit.Label(11f, TextAnchor.UpperLeft, false, new Color(0.8f, 0.8f, 0.88f)));
    }

    // ===================================================================== 指で動かせる一覧(IMGUI のスクロールは指で引けないので、ドラッグで動かす)
    Rect listRect; Vector2 listPos; float listContentH;
    bool dragging; Vector2 dragStart; float dragStartScroll; bool dragMoved;
    static Vector2 activeListScrollRef;

    Vector2 BeginList(Rect r, Vector2 scroll, float contentH)
    {
        listRect = r; listContentH = contentH;
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
        return GUI.BeginScrollView(r, scroll, new Rect(0, 0, r.width - 20f, Mathf.Max(contentH, r.height)));
    }
    void EndList() { GUI.EndScrollView(); }
}
#endif
