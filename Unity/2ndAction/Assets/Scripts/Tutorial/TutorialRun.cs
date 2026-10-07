using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 操作の練習(2026-10-07)。TutorialLauncher が平地の練習区画を用意してから置く。
//  1 前後の攻撃  2 ジャンプ→二段ジャンプ  3 空中で下攻撃(しっかりした地面の上)  4 打ち上げ→空中で追撃(スキップ可)
//  5 カードを選ぶ(害の無い3枚。効果はこの練習の中だけ)  6 おわり「まずは1,000mのボスを目指そう!」
//  ・説明は1つずつ、大きな文字で。説明の間はゲームを止め、「やってみる」で再開(押した指は離すまで操作にしない)。
//  ・説明の板はキャラに重ならない側(上か下)に出す。操作の書き方は最後に使った機器(タッチ/キー/パッド)に合わせる。
//  ・時間制限なし。ダメージなし。報酬/記録は書かない。いつでも「練習をやめる」で抜けられる。
public class TutorialRun : MonoBehaviour
{
    public const string CharacterId = "swordsman";
    public static TutorialRun Instance { get; private set; }
    public static bool PanelOpen => Instance != null && Instance.phase == Phase.Explain;

    public enum Step { Attack = 1, Jump, Dive, Launch, Card, End }
    enum Phase { Explain, Practice, Done }
    public Step CurrentStep { get; private set; } = Step.Attack;
    Phase phase = Phase.Explain;
    float doneTimer, practiceTime;
    readonly object pauseOwner = new object();
    bool paused;

    // 達成の状況
    public bool AttackForwardDone, AttackBackDone, JumpDone, DoubleJumpDone, DiveStarted, DiveDone, LaunchDone, FollowUpDone, CardChosen;
    public string ChosenCard { get; private set; } = "";
    public static readonly string[] PracticeCards = { "attack_up", "heart_up", "speed_up" };
    public int Hits { get; private set; }

    readonly List<(EnemyController en, float off)> pinned = new List<(EnemyController, float)>();
    EnemyController launchTarget;
    float groundY;
    bool ready;

    // ---------------------------------------------------------------- 用意
    public IEnumerator Setup()
    {
        Instance = this;
        var gm = GameManager.Instance; var pc = PlayerController.Instance; var tm = TerrainManager.Instance;
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
        foreach (var o in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) o.enabled = false;
        foreach (var o in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) o.enabled = false;
        if (BonusZone.Instance != null) BonusZone.Instance.enabled = false;
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (e != null) Destroy(e.gameObject);
        if (tm != null) tm.ClearAllEnemies();
        foreach (var o in FindObjectsByType<ObstacleController>(FindObjectsSortMode.None)) if (o != null) Destroy(o.gameObject);
        if (tm != null)
        {
            tm.ConfigureArena(); // 平地(穴/段差/障害物/敵なし)
            float x = tm.NextGenerateX + 30f;
            tm.GenerateNow(x + 400f);
            pc.ArenaPlaceAt(x);
        }
        yield return null;
        groundY = tm != null ? (tm.GetHeightAt(pc.transform.position.x) ?? (pc.transform.position.y - pc.groundOffset)) : 0f;
        gm.ArenaApplyBuild(new List<ArenaBuildEntry>()); // 黒剣士の基準の強さ(キャラカード/デッキは使わない)
        pc.IsStandingIdle = false;
        PlayerController.AttackStarted += OnAttack;
        pc.JumpStarted += OnJump;
        pc.DoubleJumped += OnDoubleJump;
        pc.DiveAttackLanded += OnDiveLanded;
        EnemyController.LocalHit += OnHit;
        // 覆いの下で少し走らせて、カメラを新しい位置へ追いつかせてから最初の説明で止める
        TutorialLauncher.ReleaseCoverTime();
        float settle = 0f;
        while (settle < 0.8f) { yield return null; settle += Time.unscaledDeltaTime; }
        ready = true;
        EnterStep(Step.Attack);
        Debug.Log($"[Tutorial] READY ground={groundY:F2} speed={pc.CurrentAutoRunSpeed * GameManager.KmhPerMps:F1}km/h hp={gm.Lives}/{gm.maxLives}");
    }

    public static void Teardown()
    {
        if (Instance == null) return;
        Instance.Unhook();
        Instance.SetPaused(false);
        Destroy(Instance.gameObject);
        Instance = null;
    }

    void OnDestroy() { Unhook(); if (Instance == this) Instance = null; }

    void Unhook()
    {
        PlayerController.AttackStarted -= OnAttack;
        EnemyController.LocalHit -= OnHit;
        var pc = PlayerController.Instance;
        if (pc != null) { pc.JumpStarted -= OnJump; pc.DoubleJumped -= OnDoubleJump; pc.DiveAttackLanded -= OnDiveLanded; }
    }

    // ---------------------------------------------------------------- 入力の記録
    void OnAttack(PlayerController.AttackDirection d)
    {
        if (phase != Phase.Practice || CurrentStep != Step.Attack) return;
        if (d == PlayerController.AttackDirection.Forward) AttackForwardDone = true; else AttackBackDone = true;
    }
    void OnJump() { if (phase == Phase.Practice && CurrentStep == Step.Jump) JumpDone = true; }
    void OnDoubleJump() { if (phase == Phase.Practice && CurrentStep == Step.Jump && JumpDone) DoubleJumpDone = true; }
    void OnDiveLanded() { if (phase == Phase.Practice && CurrentStep == Step.Dive) DiveDone = true; }
    void OnHit(EnemyController en, PlayerAttackKind kind, bool wasLaunched, bool killed)
    {
        Hits++;
        if (phase != Phase.Practice || CurrentStep != Step.Launch || en == null || en != launchTarget) return;
        if (kind == PlayerAttackKind.Up && !wasLaunched) LaunchDone = true;
        var pc = PlayerController.Instance;
        if (wasLaunched && LaunchDone && pc != null && !pc.IsGrounded) FollowUpDone = true;
    }

    // ---------------------------------------------------------------- 段階
    void EnterStep(Step s)
    {
        CurrentStep = s;
        phase = Phase.Explain;
        practiceTime = 0f;
        ClearTargets();
        SetPaused(true);
        Debug.Log($"[Tutorial] step {(int)s} {s}: explain");
    }

    public void BeginPractice()
    {
        if (phase != Phase.Explain) return;
        if (CurrentStep == Step.End) return;
        phase = Phase.Practice;
        ReleaseInput();
        SetPaused(false);
        SpawnTargetsFor(CurrentStep);
        if (CurrentStep == Step.Card) OfferCards();
        Debug.Log($"[Tutorial] step {(int)CurrentStep} {CurrentStep}: practice");
    }

    public void SkipStep()
    {
        Debug.Log($"[Tutorial] step {(int)CurrentStep} {CurrentStep}: skipped");
        ReleaseInput();
        Next();
    }

    void Next()
    {
        if (CurrentStep >= Step.End) return;
        EnterStep(CurrentStep + 1);
        if (CurrentStep == Step.End) TutorialProgress.MarkPracticeDone();
    }

    bool Achieved()
    {
        switch (CurrentStep)
        {
            case Step.Attack: return AttackForwardDone && AttackBackDone;
            case Step.Jump: return DoubleJumpDone;
            case Step.Dive: return DiveDone;
            case Step.Launch: return FollowUpDone;
            case Step.Card: return CardChosen;
        }
        return false;
    }

    void Update()
    {
        if (!ready) return;
        var pc = PlayerController.Instance;
        if (CurrentStep == Step.Dive && phase == Phase.Practice && pc != null && pc.IsDiveAttacking) DiveStarted = true;
        if (phase == Phase.Practice)
        {
            practiceTime += Time.unscaledDeltaTime;
            if (Achieved())
            {
                phase = Phase.Done; doneTimer = 0f;
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.UiToggle);
                Debug.Log($"[Tutorial] step {(int)CurrentStep} {CurrentStep}: achieved after {practiceTime:F1}s");
            }
        }
        else if (phase == Phase.Done)
        {
            doneTimer += Time.unscaledDeltaTime;
            // 着地を待ってから次の説明へ(空中で止めない)
            if (doneTimer > 0.9f && (pc == null || pc.IsGrounded || doneTimer > 2.5f)) Next();
        }
        // 止めている間に何かが時間を戻しても、説明の間は止めたまま
        if (phase == Phase.Explain && !paused) SetPaused(true);
    }

    void LateUpdate()
    {
        var pc = PlayerController.Instance;
        if (pc == null) return;
        foreach (var (en, off) in pinned)
            if (en != null && en.isActiveAndEnabled) en.transform.position = new Vector3(pc.transform.position.x + off, en.transform.position.y, en.transform.position.z);
    }

    // ---------------------------------------------------------------- 標的
    void SpawnTargetsFor(Step s)
    {
        ClearTargets();
        if (s == Step.Attack) { Spawn(2.2f, false); Spawn(-2.2f, false); }
        else if (s == Step.Launch) launchTarget = Spawn(1.7f, true);
    }

    EnemyController Spawn(float offset, bool launchable)
    {
        var tm = TerrainManager.Instance; var pc = PlayerController.Instance;
        var def = EnemyDatabase.FindById("goblin");
        if (tm == null || pc == null || def == null) return null;
        float x = pc.transform.position.x + offset;
        float y = tm.GetHeightAt(x) ?? groundY;
        var go = tm.SpawnEncounterEnemy(def, new Vector2(x, y), EnemyAiTier.T0, EnemyBehaviorKind.None);
        var en = go != null ? go.GetComponent<EnemyController>() : null;
        if (en == null) return null;
        en.ArenaDummy = true;                 // 体当たり/攻撃なし
        en.hitKnockbackEnabled = launchable;  // 打ち上げの練習の相手だけ吹き飛ぶ
        en.maxHp = 99999999; en.ResetHpToMax();
        var sb = go.GetComponent<EnemySpecialBehavior>(); if (sb != null) sb.enabled = false;
        pinned.Add((en, offset));
        return en;
    }

    void ClearTargets()
    {
        foreach (var (en, _) in pinned) if (en != null) Destroy(en.gameObject);
        pinned.Clear();
        launchTarget = null;
    }

    // ---------------------------------------------------------------- カード
    void OfferCards()
    {
        var gm = GameManager.Instance;
        if (gm == null || gm.rewardCardSequence == null) { CardChosen = true; return; }
        var list = new List<RewardCardData>();
        foreach (var id in PracticeCards) { var c = CardDatabase.FindById(id); if (c != null) list.Add(gm.MakeCardData(c)); }
        if (list.Count == 0) { CardChosen = true; return; }
        SetPaused(true);
        bool ok = gm.rewardCardSequence.StartSequence(list.ToArray(), id =>
        {
            gm.TutorialApplyCard(id); // このランの中だけ(所持/デッキには書かない)
            ChosenCard = id;
            CardChosen = true;
            SetPaused(false);
            ReleaseInput();
        }, "PRACTICE");
        if (!ok) { SetPaused(false); CardChosen = true; }
    }

    // ---------------------------------------------------------------- 止める/入力
    void SetPaused(bool on)
    {
        if (on == paused) return;
        paused = on;
        if (on) TimeControl.Pause(pauseOwner); else TimeControl.Resume(pauseOwner);
    }

    void ReleaseInput()
    {
        UiInputGate.LatchUntilRelease(); // ボタンを押した指は離すまで操作にしない
        if (PlayerController.Instance != null) PlayerController.Instance.ClearPointerState();
    }

    public void Quit(string why)
    {
        TutorialProgress.MarkPracticeDone(); // スキップも「済み」(初回の勧めを繰り返さない)
        TutorialLauncher.Exit(TutorialMode.FirstRunFlow, why);
    }

    public void Retry() { TutorialLauncher.Relaunch("retry"); }

    // ---------------------------------------------------------------- 表示
    static string Key(string touch, string keys, string pad)
    {
        switch (GameInput.LastDevice)
        {
            case InputDeviceKind.Keyboard: return keys;
            case InputDeviceKind.Gamepad: return pad;
            default: return touch;
        }
    }
    static string FwdIn => Key("右(前)へフリック", "→ / D / Z キー", "X ボタン(右スティック →)");
    static string BackIn => Key("左(後ろ)へフリック", "← / A / X キー", "Y ボタン(右スティック ←)");
    static string UpIn => Key("上へフリック", "↑ / W / Space キー", "A ボタン(右スティック ↑)");
    static string DownIn => Key("下へフリック", "↓ / S キー", "B ボタン(右スティック ↓)");

    string Title()
    {
        switch (CurrentStep)
        {
            case Step.Attack: return "攻撃";
            case Step.Jump: return "ジャンプ";
            case Step.Dive: return "下攻撃";
            case Step.Launch: return "打ち上げ → 空中追撃";
            case Step.Card: return "カードを選ぶ";
            default: return "練習おわり";
        }
    }

    string Explain()
    {
        switch (CurrentStep)
        {
            case Step.Attack: return $"キャラは自動で走ります。\n前後にフリックすると、その方向へ攻撃します。\n\n前: {FwdIn}\n後ろ: {BackIn}";
            case Step.Jump: return $"上へフリックするとジャンプします。\n空中でもう一度上へフリックすると、二段ジャンプです。\n\nジャンプ: {UpIn}";
            case Step.Dive: return $"ジャンプ中に下へフリックすると、真下へ急降下して攻撃します。\n足元がしっかりした地面の上で使いましょう。\n\n下攻撃: {DownIn}";
            case Step.Launch: return "敵の近くでジャンプすると、上への攻撃で敵を打ち上げます。\n浮いた敵には、空中で攻撃を当てて追撃できます。\n\n(難しければスキップできます)";
            case Step.Card: return "走っているとレベルが上がり、3枚のカードから1枚を選べます。\n選んだ強化は、そのランの間だけ有効です。\n\nここでは練習なので、効果はこの練習の中だけです。";
            default: return "これで基本の操作はおしまいです。\n\nまずは1,000mのボスを目指そう!";
        }
    }

    string Hint()
    {
        string ok(bool b) => b ? "OK" : "--";
        switch (CurrentStep)
        {
            case Step.Attack: return $"前へ攻撃 [{ok(AttackForwardDone)}]   後ろへ攻撃 [{ok(AttackBackDone)}]";
            case Step.Jump: return $"ジャンプ [{ok(JumpDone)}]   二段ジャンプ [{ok(DoubleJumpDone)}]" + (practiceTime > 12f && JumpDone && !DoubleJumpDone ? "\nジャンプ中にもう一度、上へ" : "");
            case Step.Dive: return $"ジャンプしてから下へ [{ok(DiveDone)}]" + (practiceTime > 12f && !DiveStarted ? "\nまず上でジャンプ → 空中で下へ" : "");
            case Step.Launch: return $"打ち上げる [{ok(LaunchDone)}]   空中で追撃 [{ok(FollowUpDone)}]" + (practiceTime > 12f && !LaunchDone ? "\n敵のすぐ手前でジャンプ" : practiceTime > 12f && !FollowUpDone ? "\n浮いた敵へ、ジャンプ中に前へ攻撃" : "");
            case Step.Card: return "カードを1枚選んでください";
        }
        return "";
    }

    GUIStyle titleSt, bodySt, hintSt;
    void OnGUI()
    {
        if (!ready || TutorialLauncher.Covering) return;
        var gm = GameManager.Instance;
        if (gm != null && gm.IsGameOver) return;
        GUI.depth = -1500;
        float s = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 0.8f, 2.6f);
        if (titleSt == null)
        {
            titleSt = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
            bodySt = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            hintSt = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
        }
        hintSt.fontSize = Mathf.RoundToInt(22 * s); hintSt.normal.textColor = Color.white;

        // いつでも抜けられる(右上のHPの下)。練習中はHUDのボタンと同じ層(パッド/キーの操作をメニューに取られない)
        var quitR = new Rect(Screen.width - 210 * s, 92 * s, 196 * s, 44 * s);
        int hudLayer = PadNav.BeginLayer(phase == Phase.Explain ? 40 : PadNav.HudLayer);
        bool quit = CurrentStep != Step.End && UiKit.Button(quitR, "練習をやめる", 18 * s, false, false);
        PadNav.EndLayer(hudLayer);
        if (quit) { Quit("quit button"); return; }

        if (phase == Phase.Explain)
        {
            int pl = PadNav.BeginLayer(50);
            // キャラに重ならない側(上/下)へ。入りきらなければ低くして文字も小さく
            float pw = Mathf.Min(Screen.width - 32 * s, 900 * s);
            var p = GuidePlacement.Place(pw, Mathf.Min(Screen.height * 0.62f, 380 * s), 14 * s, 8 * s, out float k);
            titleSt.fontSize = Mathf.RoundToInt(34 * s * k); titleSt.normal.textColor = new Color(1f, 0.86f, 0.45f);
            bodySt.fontSize = Mathf.RoundToInt(25 * s * k); bodySt.normal.textColor = Color.white;
            OrnateUi.DrawPanel(p, 0.95f);
            LocGUI.Label(new Rect(p.x + 20 * s, p.y + 10 * s * k, p.width - 40 * s, 46 * s * k), (CurrentStep == Step.End ? Loc.T(Title()) : $"{(int)CurrentStep} / 5  {Loc.T(Title())}"), titleSt);
            LocGUI.Label(new Rect(p.x + 28 * s, p.y + 56 * s * k, p.width - 56 * s, p.height - 130 * s * k), Explain(), bodySt);
            float bw = 260 * s * k, bh = 58 * s * k, by = p.yMax - bh - 14 * s * k;
            if (CurrentStep == Step.End)
            {
                bool first = TutorialMode.FirstRunFlow;
                if (UiKit.Button(new Rect(p.center.x - bw - 10 * s, by, bw, bh), first ? "出発する" : "ホームへ戻る", 24 * s, true)) TutorialLauncher.Exit(first, "finished");
                if (UiKit.Button(new Rect(p.center.x + 10 * s, by, bw, bh), "もう一度練習する", 22 * s, false)) Retry();
            }
            else if (CurrentStep == Step.Launch)
            {
                if (UiKit.Button(new Rect(p.center.x - bw - 10 * s, by, bw, bh), "やってみる", 24 * s, true)) BeginPractice();
                if (UiKit.Button(new Rect(p.center.x + 10 * s, by, bw, bh), "スキップ", 22 * s, false)) SkipStep();
            }
            else if (UiKit.Button(new Rect(p.center.x - bw * 0.5f, by, bw, bh), CurrentStep == Step.Card ? "選んでみる" : "やってみる", 24 * s, true)) BeginPractice();
            PadNav.EndLayer(pl);
            // 説明の間は背後へ通さない
            if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp) Event.current.Use();
        }
        else
        {
            // 練習中: 小さな帯(キャラに重ならない側)
            string h = phase == Phase.Done ? "OK!" : Hint();
            if (string.IsNullOrEmpty(h) || (CurrentStep == Step.Card && gm != null && gm.rewardCardSequence != null && gm.rewardCardSequence.IsRunning)) return;
            float bw = Mathf.Min(Screen.width - 32 * s, 760 * s), bh = (h.Contains("\n") ? 92 : 58) * s;
            // キャラから遠い側の端(上はHUDの下)
            bool up = true;
            if (GuidePlacement.PlayerBand(out float ptop, out float pbot)) up = ptop > Screen.height - pbot;
            var r = new Rect((Screen.width - bw) * 0.5f, up ? 92 * s : Screen.height - bh - 16 * s, bw, bh);
            UiBackdrop.Draw(r, 0.7f);
            hintSt.normal.textColor = phase == Phase.Done ? new Color(0.6f, 1f, 0.6f) : Color.white;
            LocGUI.Label(r, h, hintSt);
            if (CurrentStep == Step.Launch && phase == Phase.Practice)
            {
                var sk = new Rect(r.xMax - 150 * s, r.yMax + 6 * s, 150 * s, 42 * s);
                int sl = PadNav.BeginLayer(PadNav.HudLayer);
                bool skip = UiKit.Button(sk, "スキップ", 18 * s, false, false);
                PadNav.EndLayer(sl);
                if (skip) SkipStep();
            }
        }
    }
}
