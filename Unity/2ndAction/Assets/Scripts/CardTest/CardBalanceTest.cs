#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// CARD BALANCE TEST(2026-10-01) - カードが操作感へ与える主要パラメータを、ラン中にその場で変えて比べるための
// 開発ビルド/Editor専用パネル(このファイルはリリースビルドではコンパイルされない)。
//
// 開き方: DEBUG(タイトルの右列)をON → ラン中に左側のデバッグ列に出る「CARD TEST」ボタン。
//
// 値の持ち方(累積しない仕組み):
//  ・プレイヤー/GameManagerの値(runSpeed・jumpForce 等)は「キャラ基準 × カード」の値を持っている。テストはその上に
//    1枚の層として掛ける。今掛かっている層(applied)を覚えておき、値を変える時は「今の値 ÷ 前の層 × 新しい層」
//    (足し算の項目は「− 前の層 + 新しい層」)で掛け替える。A→B→C→…と何度切り替えても層は常に1枚。
//  ・RESET: カードの効果を外し、テストの層も外して、そのキャラ本来の基準値(CharacterDefinition)へ直接書き戻す。
//  ・TEST OFF: テストの層だけを外す(ラン中に取ったカードの効果は残る)。
//  ・テスト中にカードを取った場合、カードは今までどおりその値に掛かる(実効値の表示に出る)。
// 通常プレイへ影響しない仕組み:
//  ・DEBUGがOFF/リリースビルドでは何も表示せず何もしない(DEBUGをOFFにした瞬間にテストの層を外す)。
//  ・新しいラン(シーンの読み直し/Run開始)を検出したら、テストの値はすべて捨てる(次のランへ持ち越さない)。
//  ・A/B/Cの候補値だけはPlayerPrefsに保存される(値の編集結果。ゲームの性能には関係しない)。
public partial class CardBalanceTest : MonoBehaviour
{
    public static CardBalanceTest Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[CardBalanceTest]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<CardBalanceTest>();
    }

    public enum Kind { Mul, Add, Set }
    public enum Tab { Control, Status, CardChar, Boss, Rematch }

    public class Param
    {
        public string key, label;
        public Kind kind;
        public float[] defaults;    // A/B/Cの初期値
        public float[] abc;         // A/B/C(編集可能、PlayerPrefsに保存)
        public float step, min, max;
        public Tab tab;
        public float value;         // テストで設定した値(Mul=倍率 / Add=基準からの加算 / Set=そのままの値)
        public bool active;         // テスト値を掛けているか
        public int slot = -1;       // 直前に押したA/B/C(表示用)
        public float applied;       // 今プレイヤーに掛かっている層(Mul=倍率, Add=実際に足した量)。Setは使わない
        public float Neutral => kind == Kind.Mul ? 1f : 0f;
        public float Desired => active ? value : Neutral;
    }

    public readonly List<Param> Params = new List<Param>();
    Param pSpeed, pJump, pJumps, pAttack, pAtkTime, pRange, pHp, pShield, pExp, pMile;

    public bool IsOpen { get; private set; }
    public bool Paused { get; private set; }
    public bool AnyActive => Params.Any(p => p.active);
    bool editSlots;
    Tab tab = Tab.Control;
    int charIndex = -1;
    int cardIndex;
    string cardNote = "";
    string lastAction = "";

    // どのランに対して層を掛けているか(新しいランを検出したら捨てる)
    PlayerController boundPc;
    GameManager boundGm;
    bool boundStarted;

    // テスト/自動確認用
    public static int Applications;

    void Awake()
    {
        P(ref pSpeed, "speed", "移動速度", Kind.Mul, new[] { 1.2f, 1.3f, 1.4f }, 0.05f, 0.2f, 8f, Tab.Control);
        P(ref pJump, "jump", "ジャンプ力", Kind.Mul, new[] { 1.1f, 1.2f, 1.3f }, 0.05f, 0.2f, 4f, Tab.Control);
        P(ref pJumps, "jumps", "ジャンプ回数", Kind.Add, new[] { 1f, 2f, 3f }, 1f, -3f, 9f, Tab.Control);
        P(ref pAtkTime, "atktime", "攻撃時間", Kind.Mul, new[] { 0.9f, 0.8f, 0.7f }, 0.05f, 0.1f, 4f, Tab.Control);
        P(ref pRange, "range", "攻撃範囲", Kind.Mul, new[] { 1.2f, 1.4f, 1.6f }, 0.1f, 0.3f, 6f, Tab.Control);
        // 2026-10-02: 攻撃力/HPは10倍スケール(+1 = 旧+0.1)
        P(ref pAttack, "attack", "攻撃力", Kind.Add, new[] { 10f, 30f, 50f }, 5f, -200f, 20000f, Tab.Control);
        P(ref pHp, "hp", "最大HP", Kind.Add, new[] { 10f, 20f, 40f }, 5f, -300f, 300f, Tab.Status);
        P(ref pShield, "shield", "Shield", Kind.Set, new[] { 1f, 2f, 3f }, 1f, 0f, 20f, Tab.Status);
        P(ref pExp, "exp", "EXP倍率", Kind.Mul, new[] { 1.2f, 1.5f, 2f }, 0.1f, 0f, 8f, Tab.Status);
        P(ref pMile, "mile", "MILE倍率", Kind.Mul, new[] { 1.2f, 1.5f, 2f }, 0.1f, 0f, 8f, Tab.Status);
    }

    void P(ref Param field, string key, string label, Kind kind, float[] abc, float step, float min, float max, Tab t)
    {
        field = new Param { key = key, label = label, kind = kind, defaults = abc, abc = (float[])abc.Clone(), step = step, min = min, max = max, tab = t };
        field.value = field.Neutral;
        field.applied = field.Neutral;
        for (int i = 0; i < 3; i++)
        {
            try { field.abc[i] = PlayerPrefs.GetFloat($"CardTest.{key}.{i}", abc[i]); } catch { }
        }
        Params.Add(field);
    }

    static bool Allowed()
    {
        var gm = GameManager.Instance;
        return gm != null && gm.DebugMode && Debug.isDebugBuild;
    }

    static bool InRun()
    {
        var gm = GameManager.Instance;
        return Allowed() && PlayerController.Instance != null && gm.HasStarted && !gm.IsGameOver;
    }

    public static CharacterDefinition CurrentDef()
    {
        var gm = GameManager.Instance;
        return gm != null ? CharacterDatabase.FindById(gm.ActiveRunCharacterId) : null;
    }

    public void Toggle() { IsOpen = !IsOpen; if (!IsOpen) SetPaused(false); }
    public void Open(bool on) { IsOpen = on; if (!on) SetPaused(false); }

    void SetPaused(bool on)
    {
        if (on == Paused) return;
        Paused = on;
        if (on) TimeControl.Pause(this); else TimeControl.Resume(this);
    }

    void Update()
    {
        if (testBoss != null) TrackTestBoss(); // ボス試験(2026-10-02)
        var gm = GameManager.Instance;
        var pc = PlayerController.Instance;
        // 新しいラン(シーン読み直し/Run開始/別のプレイヤー)を検出: テストの値は持ち越さない
        bool started = gm != null && gm.HasStarted;
        if (pc != boundPc || gm != boundGm || started != boundStarted)
        {
            Forget();
            boundPc = pc; boundGm = gm; boundStarted = started;
        }
        if (!Allowed())
        {
            if (AnyApplied() && InRunIgnoringDebug()) RemoveTestLayer();
            foreach (var p in Params) { p.active = false; p.slot = -1; }
            IsOpen = false;
            SetPaused(false);
            return;
        }
        if (Paused && !InRun()) SetPaused(false);
        if (InRun()) ApplyDesired();
    }

    static bool InRunIgnoringDebug()
    {
        var gm = GameManager.Instance;
        return PlayerController.Instance != null && gm != null && gm.HasStarted && !gm.IsGameOver;
    }

    bool AnyApplied() => Params.Any(p => p.kind != Kind.Set && Mathf.Abs(p.applied - p.Neutral) > 1e-5f);

    // 新しいランでは値が作り直されているので、層を掛けていないことにする(値は書かない)
    void Forget()
    {
        foreach (var p in Params) { p.applied = p.Neutral; p.active = false; p.slot = -1; p.value = p.Neutral; }
        SetPaused(false);
        cardNote = "";
        charIndex = -1;
    }

    // ------------------------------------------------------------------ 値の読み書き
    float Read(Param p)
    {
        var pc = PlayerController.Instance; var gm = GameManager.Instance;
        if (p == pSpeed) return pc.runSpeed;
        if (p == pJump) return pc.jumpForce;
        if (p == pJumps) return pc.maxJumps;
        if (p == pAttack) return pc.AttackPower;
        if (p == pAtkTime) return pc.AttackSpeedMultiplier;
        if (p == pRange) return pc.AttackRangeMultiplier;
        if (p == pHp) return gm.maxLives;
        if (p == pShield) return pc.ShieldCharges;
        if (p == pExp) return gm.CardTestExpMultiplier;
        if (p == pMile) return gm.MileGainMultiplier;
        return 0f;
    }

    void Write(Param p, float v, float ratioForMile = 1f)
    {
        var pc = PlayerController.Instance; var gm = GameManager.Instance;
        if (p == pSpeed) pc.runSpeed = Mathf.Max(0.01f, v);
        else if (p == pJump) pc.jumpForce = Mathf.Max(0.01f, v);
        else if (p == pJumps) pc.maxJumps = Mathf.Max(1, Mathf.RoundToInt(v));
        else if (p == pAttack) pc.CardTestSetAttackPower(Mathf.RoundToInt(v));
        else if (p == pAtkTime) pc.CardTestSetAttackSpeed(v);
        else if (p == pRange) pc.CardTestSetAttackRange(v);
        else if (p == pHp) gm.CardTestSetMaxLives(Mathf.RoundToInt(v));
        else if (p == pShield) pc.CardTestSetShield(Mathf.RoundToInt(v));
        else if (p == pExp) gm.CardTestExpMultiplier = Mathf.Max(0f, v);
        else if (p == pMile) gm.CardTestSetMileMultipliers(Mathf.Max(0f, v), Mathf.Max(0f, gm.BossMileGainMultiplier * ratioForMile));
    }

    // テストの層を掛けていない値(=キャラ基準×カード)
    public float NoTest(Param p)
    {
        float cur = Read(p);
        if (p.kind == Kind.Mul) return Mathf.Abs(p.applied) > 1e-6f ? cur / p.applied : cur;
        if (p.kind == Kind.Add) return cur - p.applied;
        return cur;
    }

    void ApplyDesired()
    {
        foreach (var p in Params)
        {
            if (p.kind == Kind.Set) continue; // Shieldはボタンを押した時だけ(使うと減る値なので毎フレーム戻さない)
            float want = p.Desired;
            if (Mathf.Abs(want - p.applied) < 1e-6f) continue;
            float noTest = NoTest(p);
            if (p.kind == Kind.Mul)
            {
                float old = p.applied;
                Write(p, noTest * want, Mathf.Abs(old) > 1e-6f ? want / old : 1f);
                float now = Read(p);
                p.applied = Mathf.Abs(noTest) > 1e-6f ? now / noTest : want; // 下限で切られた時は実際に掛かった倍率
            }
            else
            {
                Write(p, noTest + want);
                p.applied = Read(p) - noTest; // 上限/下限で切られた時は実際に足した量
            }
            Applications++;
        }
    }

    void RemoveTestLayer()
    {
        foreach (var p in Params) { p.active = false; p.slot = -1; }
        if (InRunIgnoringDebug()) ApplyDesiredIgnoringDebug();
    }

    void ApplyDesiredIgnoringDebug() => ApplyDesired();

    // ------------------------------------------------------------------ 操作(ボタン/テストから)
    public Param Get(string key) => Params.FirstOrDefault(p => p.key == key);

    public void SetValue(string key, float v, int slot = -1)
    {
        var p = Get(key); if (p == null) return;
        p.value = Mathf.Clamp(v, p.min, p.max);
        p.active = true;
        p.slot = slot;
        if (p.kind == Kind.Set && InRun()) Write(p, p.value);
        if (InRun()) ApplyDesired();
        lastAction = $"{p.label} = {Fmt(p, p.value)}";
    }

    public void PressSlot(string key, int slot)
    {
        var p = Get(key); if (p == null) return;
        if (editSlots)
        {
            p.abc[slot] = p.active ? p.value : p.Neutral;
            PlayerPrefs.SetFloat($"CardTest.{p.key}.{slot}", p.abc[slot]);
            PlayerPrefs.Save();
            lastAction = $"{p.label} の {(char)('A' + slot)} に {Fmt(p, p.abc[slot])} を登録";
            return;
        }
        SetValue(key, p.abc[slot], slot);
    }

    public void Step(string key, int dir)
    {
        var p = Get(key); if (p == null) return;
        float from = p.active ? p.value : p.Neutral;
        SetValue(key, Mathf.Round((from + dir * p.step) / p.step) * p.step);
    }

    public void TurnOff(string key)
    {
        var p = Get(key); if (p == null) return;
        p.active = false; p.slot = -1;
        if (InRun()) ApplyDesired();
    }

    // テストの層だけ外す(カードの効果は残る)
    public void TestOff()
    {
        foreach (var p in Params) { p.active = false; p.slot = -1; }
        if (InRun()) ApplyDesired();
        cardNote = "";
        lastAction = "TEST OFF(カードの効果は残る)";
    }

    // カード補正・テスト補正を外し、そのキャラ本来の基準値へ
    public void ResetToBase()
    {
        foreach (var p in Params) { p.active = false; p.slot = -1; p.value = p.Neutral; p.applied = p.Neutral; }
        cardNote = "";
        var def = CurrentDef();
        var pc = PlayerController.Instance; var gm = GameManager.Instance;
        if (!InRun() || def == null) return;
        gm.CardTestClearCardState();
        pc.CardTestClearCardBonuses();
        pc.runSpeed = pc.CardTestBaseRunSpeed * def.groundMobilityMultiplier;
        pc.jumpForce = pc.CardTestBaseJumpForce * def.jumpForceMultiplier;
        pc.maxJumps = Mathf.Max(1, def.jumpCount);
        pc.CardTestSetAttackPower(def.attackPower);
        pc.CardTestSetAttackSpeed(def.attackSpeedMultiplier);
        pc.CardTestSetAttackRange(def.attackRangeMultiplier);
        gm.CardTestSetMaxLives(def.baseMaxLives);
        lastAction = $"RESET: {def.displayName} の基準値へ(カード/テストの補正なし)";
    }

    // 実際のカードをLv(=重ねた枚数)で掛けた時の値を、テストの値として入れる(先にRESETして基準から)
    public void ApplyCardLevel(CardDefinition card, int lv)
    {
        var def = CurrentDef();
        if (card == null || def == null) return;
        ResetToBase();
        var unsupported = new List<string>();
        float speed = 1f, jump = 1f, atkTime = 1f, exp = 1f, mile = 1f;
        float rangeAdd = 0f; int jumps = 0, attack = 0, hp = 0, shield = 0;
        bool uSpeed = false, uJump = false, uJumps = false, uAttack = false, uAtk = false, uRange = false, uHp = false, uShield = false, uExp = false, uMile = false;
        foreach (var e in card.effects)
        {
            float v = e.value;
            switch (e.type)
            {
                case EffectType.MoveSpeed: speed *= Mathf.Pow(1f + v, lv); uSpeed = true; break;
                case EffectType.JumpPower: jump *= Mathf.Pow(1f + v, lv); uJump = true; break;
                case EffectType.JumpCount: jumps += Mathf.RoundToInt(v) * lv; uJumps = true; break;
                case EffectType.AttackPower: attack += Mathf.RoundToInt(v) * lv; uAttack = true; break;
                case EffectType.MaxHp: hp += Mathf.RoundToInt(v) * lv; uHp = true; break;
                case EffectType.AttackRange: rangeAdd += v * lv; uRange = true; break;
                case EffectType.AttackSpeed: atkTime *= Mathf.Pow(1f - v, lv); uAtk = true; break;
                case EffectType.Shield: shield += Mathf.RoundToInt(v) * lv; uShield = true; break;
                case EffectType.ExpGain: exp += v * lv; uExp = true; break;
                case EffectType.MileGainMultiplier: mile += v * lv; uMile = true; break;
                default: unsupported.Add($"{e.type} {(v >= 0 ? "+" : "")}{v * lv:0.##}"); break;
            }
        }
        // カードの実装と同じ上限/下限: 範囲は足し算(下限0.1)、攻撃時間は合計で下限0.25
        if (uSpeed) SetValue("speed", speed);
        if (uJump) SetValue("jump", jump);
        if (uJumps) SetValue("jumps", jumps);
        if (uAttack) SetValue("attack", attack);
        if (uAtk) SetValue("atktime", Mathf.Max(0.25f, def.attackSpeedMultiplier * atkTime) / def.attackSpeedMultiplier);
        if (uRange) SetValue("range", Mathf.Max(0.1f, def.attackRangeMultiplier + rangeAdd) / def.attackRangeMultiplier);
        if (uHp) SetValue("hp", hp);
        if (uShield) SetValue("shield", shield);
        if (uExp) SetValue("exp", exp);
        if (uMile) SetValue("mile", mile);
        cardNote = $"{card.cardName} Lv{lv}" + (unsupported.Count > 0 ? $"  このパネル対象外の効果: {string.Join(", ", unsupported)}" : "");
        lastAction = $"カード {card.cardName} Lv{lv} を基準値に適用";
    }

    public bool SwitchCharacter(string id)
    {
        var gm = GameManager.Instance;
        if (!InRun() || gm == null) return false;
        // 新しいキャラの基準値で作り直すので、層は掛かっていない状態に戻してから(値はその後で掛け直す)
        foreach (var p in Params) p.applied = p.Neutral;
        if (!gm.CardTestSwitchCharacter(id)) return false;
        foreach (var p in Params) if (p.kind == Kind.Set && p.active) Write(p, p.value);
        ApplyDesired();
        lastAction = $"キャラ切替: {id}(カードの効果は外れ、テスト値はそのまま掛け直し)";
        return true;
    }

    // ------------------------------------------------------------------ 表示
    static string Fmt(Param p, float v)
    {
        if (p.kind == Kind.Mul) return $"×{v:0.00}";
        if (p.kind == Kind.Add) return (v >= 0 ? "+" : "") + v.ToString("0");
        return v.ToString("0");
    }

    public string Info(Param p)
    {
        var pc = PlayerController.Instance; var gm = GameManager.Instance; var def = CurrentDef();
        if (pc == null || gm == null || def == null) return "";
        float noTest = NoTest(p);
        if (p == pSpeed)
        {
            float charBase = pc.CardTestBaseRunSpeed * def.groundMobilityMultiplier;
            float nat = pc.CardTestNaturalMultiplier;
            float natKmh = GameManager.SpeedKmh(charBase * nat);
            float actual = GameManager.SpeedKmh(pc.CurrentAutoRunSpeed);
            string dbg = Mathf.Abs(PlayerController.DebugSpeedScale * PlayerController.DebugRunOnlyScale - 1f) > 0.001f ? $" ×DEBUG{PlayerController.DebugSpeedScale * PlayerController.DebugRunOnlyScale:0.##}" : "";
            return $"自然{natKmh:F0}km/h ×{noTest / charBase:0.00} ×{p.applied:0.00}{dbg} → {actual:F0}km/h";
        }
        if (p == pJump)
        {
            float b = pc.CardTestBaseJumpForce * def.jumpForceMultiplier;
            float h = pc.jumpForce * pc.jumpForce / (2f * Mathf.Max(0.01f, pc.CardTestGravity));
            string mage = def.kit == CharacterKit.Mage ? " ※魔法使いは浮遊で不使用" : "";
            return $"{b:F1} ×{noTest / b:0.00} ×{p.applied:0.00} → {pc.jumpForce:F1}(高さ{h:F2}m){mage}";
        }
        if (p == pJumps) return $"{def.jumpCount} {Sgn(noTest - def.jumpCount)} {Sgn(p.applied)} → {pc.maxJumps}回";
        if (p == pAttack) return $"{def.attackPower} {Sgn(noTest - def.attackPower)} {Sgn(p.applied)} → {pc.AttackPower}(条件込み今{pc.EffectiveAttackPower})";
        if (p == pAtkTime) return $"×{def.attackSpeedMultiplier:0.00} ×{noTest / def.attackSpeedMultiplier:0.00} ×{p.applied:0.00} → ×{pc.AttackSpeedMultiplier:0.00}(小=速い)";
        if (p == pRange) return $"×{def.attackRangeMultiplier:0.00} ×{noTest / def.attackRangeMultiplier:0.00} ×{p.applied:0.00} → ×{pc.AttackRangeMultiplier:0.00}";
        if (p == pHp) return $"{def.baseMaxLives} {Sgn(noTest - def.baseMaxLives)} {Sgn(p.applied)} → 最大{gm.maxLives}(上限{gm.maxLivesCap}) 今{gm.Lives}";
        if (p == pShield) return $"今{pc.ShieldCharges}回(押した時に設定、被弾で減る)";
        if (p == pExp) return $"1.00 ×{noTest:0.00} ×{p.applied:0.00} → ×{gm.CardTestExpMultiplier:0.00}";
        if (p == pMile) return $"1.00 ×{noTest:0.00} ×{p.applied:0.00} → 雑魚×{gm.MileGainMultiplier:0.00} ボス×{gm.BossMileGainMultiplier:0.00}";
        return "";
    }

    static string Sgn(float v) => (v >= 0 ? "+" : "") + Mathf.RoundToInt(v);

    // ------------------------------------------------------------------ IMGUI
    const float W = 840f;
    bool atTop; // パネルの位置(既定=画面の下側。敵が来る右側中央をふさがない)
    float S => Mathf.Clamp(Screen.height / 820f, 0.8f, 2.2f); // 画面の大きさに合わせた拡大率(スマホでも押せる大きさ)
    Rect panelRect;
    GUIStyle sLabel, sSmall, sBtn, sBtnOn, sTitle;

    // この矩形の中で始まったタッチはプレイヤーの操作(フリック)にしない
    public static bool BlocksPointer(Vector2 screenPos)
    {
        var i = Instance;
        if (i == null || !i.IsOpen || !Allowed()) return false;
        Vector2 gui = new Vector2(screenPos.x, Screen.height - screenPos.y) / i.S;
        return i.panelRect.Contains(gui);
    }

    void Styles()
    {
        if (sLabel != null) return;
        sLabel = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft, wordWrap = false };
        sLabel.normal.textColor = Color.white;
        sSmall = new GUIStyle(sLabel) { fontSize = 11 };
        sSmall.normal.textColor = new Color(0.75f, 1f, 0.8f);
        sTitle = new GUIStyle(sLabel) { fontSize = 15, fontStyle = FontStyle.Bold };
        sTitle.normal.textColor = new Color(1f, 0.85f, 0.35f);
        sBtn = new GUIStyle(GUI.skin.button) { fontSize = 12 };
        sBtnOn = new GUIStyle(sBtn) { fontStyle = FontStyle.Bold };
        sBtnOn.normal.textColor = sBtnOn.hover.textColor = new Color(1f, 0.85f, 0.3f);
    }

    bool B(Rect r, string t, bool on = false) => GUI.Button(r, t, on ? sBtnOn : sBtn);

    void OnGUI()
    {
        if (!Allowed() || !InRunIgnoringDebug()) return;
        Styles();
        Matrix4x4 old = GUI.matrix;
        float s = S;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float sw = Screen.width / s, sh = Screen.height / s;
        GUI.depth = -10;

        if (!IsOpen)
        {
            // 閉じている間もテスト値が掛かっていることが分かるように
            if (AnyActive)
            {
                var r = new Rect(sw - 250f, sh - 34f, 240f, 26f);
                UiBackdrop.Draw(r, 0.6f);
                GUI.Label(new Rect(r.x + 8f, r.y, r.width, r.height), "CARD TEST 適用中(DEBUGのCARD TESTで開く)", sSmall);
            }
            panelRect = default;
            GUI.matrix = old;
            return;
        }

        float rowH = 30f, gap = 3f;
        int rows = tab == Tab.CardChar ? 7 : tab == Tab.Boss ? BossTabRows : tab == Tab.Rematch ? RematchTabRows : Params.Count(p => p.tab == tab) + 4;
        float h = rows * (rowH + gap) + 12f;
        panelRect = new Rect(sw - W - 10f, atTop ? 96f : Mathf.Max(96f, sh - h - 88f), W, h);
        UiBackdrop.Draw(panelRect, 0.82f);
        float x = panelRect.x + 8f, y = panelRect.y + 6f;

        // 見出し
        GUI.Label(new Rect(x, y, 200f, rowH), "CARD BALANCE TEST", sTitle);
        float bx = x + 200f;
        if (B(new Rect(bx, y, 70f, rowH), Paused ? "再開" : "一時停止", Paused)) SetPaused(!Paused); bx += 74f;
        if (B(new Rect(bx, y, 92f, rowH), "RESET(基準)")) ResetToBase(); bx += 96f;
        if (B(new Rect(bx, y, 82f, rowH), "TEST OFF")) TestOff(); bx += 86f;
        if (B(new Rect(bx, y, 108f, rowH), editSlots ? "A/B/C登録中" : "A/B/C編集", editSlots)) editSlots = !editSlots; bx += 112f;
        if (B(new Rect(bx, y, 64f, rowH), atTop ? "位置↓" : "位置↑")) atTop = !atTop; bx += 68f;
        if (B(new Rect(panelRect.xMax - 44f, y, 36f, rowH), "×")) Open(false);
        y += rowH + gap;

        // タブ
        string[] tabs = { "操作系", "ステータス", "カードLv / キャラ", "ボス試験", "ボス再戦" };
        for (int i = 0; i < 5; i++) if (B(new Rect(x + i * 164f, y, 160f, rowH - 4f), tabs[i], (int)tab == i)) tab = (Tab)i;
        y += rowH + gap;

        if (tab == Tab.CardChar) DrawCardChar(x, ref y, rowH, gap);
        else if (tab == Tab.Boss) DrawBossTest(x, ref y, rowH, gap);
        else if (tab == Tab.Rematch) DrawRematch(x, ref y, rowH, gap);
        else
        {
            foreach (var p in Params)
            {
                if (p.tab != tab) continue;
                DrawRow(p, x, y, rowH);
                y += rowH + gap;
            }
        }

        string help = editSlots ? "A/B/C編集中: 今の値を押したA/B/Cへ登録(保存)。もう一度押して終了"
                                : "A/B/C=切替 −+=微調整 OFF=その項目だけ外す / RESET=カードもテストも外した基準値 / TEST OFF=テストだけ外す";
        GUI.Label(new Rect(x, y, W - 16f, rowH * 0.5f + 4f), string.IsNullOrEmpty(lastAction) ? "" : "直前: " + lastAction, sSmall);
        GUI.Label(new Rect(x, y + rowH * 0.5f + 2f, W - 16f, rowH * 0.5f + 4f), help, sSmall);
        GUI.matrix = old;
    }

    void DrawRow(Param p, float x, float y, float rowH)
    {
        GUI.Label(new Rect(x, y, 92f, rowH), p.label, sLabel);
        float cx = x + 92f;
        if (B(new Rect(cx, y, 32f, rowH), "−")) Step(p.key, -1); cx += 34f;
        GUI.Label(new Rect(cx, y, 58f, rowH), p.active ? Fmt(p, p.value) : "—", p.active ? sTitle : sLabel); cx += 58f;
        if (B(new Rect(cx, y, 32f, rowH), "+")) Step(p.key, +1); cx += 36f;
        for (int i = 0; i < 3; i++)
        {
            string t = $"{(char)('A' + i)} {Fmt(p, p.abc[i])}";
            if (B(new Rect(cx, y, 66f, rowH), t, p.active && p.slot == i)) PressSlot(p.key, i);
            cx += 68f;
        }
        if (B(new Rect(cx, y, 40f, rowH), "OFF", !p.active)) TurnOff(p.key); cx += 44f;
        GUI.Label(new Rect(cx, y, panelRect.xMax - cx - 6f, rowH), Info(p), sSmall);
    }

    void DrawCardChar(float x, ref float y, float rowH, float gap)
    {
        // キャラ切替
        var chars = CharacterDatabase.AllCharacters;
        var def = CurrentDef();
        if (charIndex < 0 && def != null) for (int i = 0; i < chars.Count; i++) if (chars[i] == def) charIndex = i;
        charIndex = Mathf.Clamp(charIndex, 0, Mathf.Max(0, chars.Count - 1));
        GUI.Label(new Rect(x, y, 92f, rowH), "キャラ", sLabel);
        if (B(new Rect(x + 92f, y, 32f, rowH), "◀")) charIndex = (charIndex + chars.Count - 1) % chars.Count;
        GUI.Label(new Rect(x + 128f, y, 170f, rowH), chars.Count > 0 ? chars[charIndex].displayName : "-", sTitle);
        if (B(new Rect(x + 300f, y, 32f, rowH), "▶")) charIndex = (charIndex + 1) % chars.Count;
        if (B(new Rect(x + 338f, y, 110f, rowH), "このキャラへ切替") && chars.Count > 0) SwitchCharacter(chars[charIndex].characterId);
        GUI.Label(new Rect(x + 456f, y, W - 470f, rowH), def != null ? $"今: {def.displayName}(移動×{def.groundMobilityMultiplier:0.##} 跳×{def.jumpForceMultiplier:0.##} 攻{def.attackPower} 範囲×{def.attackRangeMultiplier:0.##} 時間×{def.attackSpeedMultiplier:0.##} 跳{def.jumpCount} コンボ{def.attackComboCount} HP{def.baseMaxLives})" : "", sSmall);
        y += rowH + gap;
        GUI.Label(new Rect(x, y, W - 16f, rowH), "切替はラン中にその場で行う(カードの効果は外れ、テストで掛けている値は新しいキャラの基準へ掛け直す)", sSmall);
        y += rowH + gap;

        // カードLv
        var cards = CardDatabase.AllCards.OrderBy(c => c.sortOrder).ToList();
        if (cards.Count > 0)
        {
            cardIndex = Mathf.Clamp(cardIndex, 0, cards.Count - 1);
            var c = cards[cardIndex];
            GUI.Label(new Rect(x, y, 92f, rowH), "カード", sLabel);
            if (B(new Rect(x + 92f, y, 32f, rowH), "◀")) cardIndex = (cardIndex + cards.Count - 1) % cards.Count;
            GUI.Label(new Rect(x + 128f, y, 170f, rowH), c.cardName, sTitle);
            if (B(new Rect(x + 300f, y, 32f, rowH), "▶")) cardIndex = (cardIndex + 1) % cards.Count;
            float cx = x + 338f;
            foreach (int lv in new[] { 1, 5, 9 })
            {
                if (B(new Rect(cx, y, 60f, rowH), $"Lv{lv}")) ApplyCardLevel(c, lv);
                cx += 64f;
            }
            y += rowH + gap;
            GUI.Label(new Rect(x, y, W - 16f, rowH), "1回分: " + string.Join(", ", c.effects.Select(e => $"{e.type} {(e.value >= 0 ? "+" : "")}{e.value:0.##}")), sSmall);
            y += rowH + gap;
            GUI.Label(new Rect(x, y, W - 16f, rowH), string.IsNullOrEmpty(cardNote) ? "Lvを押すと RESET してから、そのカードをLv枚重ねた値を「操作系/ステータス」へ入れる" : "適用中: " + cardNote, sSmall);
            y += rowH + gap;
        }
    }
}
#endif
