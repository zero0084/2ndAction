using System.Collections.Generic;
using UnityEngine;

// COMBO 第1段階(2026-10-06)のランの中の状態。定義は ComboTuning、効果の中身は CardProcs.Combo.cs。
//  ・成立は「今のランの能力Lv」(GameManager.GetAbilityRunStack: キャラカード/取得/合成を能力ごとに合算済み、Lv9上限)から毎回計算する。
//    保存しない(CONTINUE ではカードの取り直しの後に計算し直す)。保存するのは一時的な状態(数え/上限の中の数)と、通知を出し終えた COMBO だけ。
//  ・強さは構成カードの Lv(平均と最低の混ぜ方)から: Lv9+Lv1 は平均5でなく、低い方へ寄る。
//  ・FINAL EVOLUTION: 構成カードのどれかが ACTIVE の間だけ ENHANCED(1段階だけ。両方 ACTIVE でも同じ)。FE の倍率は掛けない。
//  ・AWAKENED: 通知の金の飾り/HUD の★だけ(能力値は変えない)。#100 ULTIMATE は構成に使わない。
//  ・マルチでは効果なし(Docs/Multiplayer8.md)。
public class ComboSystem : MonoBehaviour
{
    public static ComboSystem Instance { get; private set; }
    static ComboTuning T => ComboTuning.I;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[ComboSystem]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<ComboSystem>();
    }

    // 成立している COMBO(今のランの能力から)
    public class Active
    {
        public ComboTuning.Combo def;
        public float level;      // 強さの Lv(1〜9、平均と最低の混ぜ方)
        public float factor;     // Lv1 = lv1Factor、Lv9 = 1
        public bool enhanced;    // 構成カードのどれかが FINAL EVOLUTION ACTIVE
        public bool bothEnhanced;
        public bool awakened;    // 構成カードのどれかが AWAKENED(見た目だけ)
        public int counter;      // モジュールごとの数え(空中の命中/風刃の命中/Blood Aegis の数など)
        public int procs;        // 発生した回数(確認用)
    }
    readonly List<Active> active = new List<Active>();
    readonly HashSet<string> announced = new HashSet<string>();
    public IReadOnlyList<Active> ActiveCombos => active;
    public static int Formed, Procs, Recomputes;
    public static bool DebugForceAwakened; // COMBO TEST
    static float recomputeAt;

    public static bool Enabled => !NetRunLauncher.IsMultiplayerRun && !DebugDisabled; // マルチは第1段階では効果なし
    public static bool DebugDisabled; // 自動テスト: COMBO を切った時との比較

    // 新しいラン(GameManager.ResetCardStatsForRun から)
    public void ResetRun()
    {
        active.Clear();
        announced.Clear();
        pendingNotice.Clear();
        noticeUntil = -1f;
        CardProcs.ComboResetRun();
    }

    // ===================================================================== 成立の計算
    // カードが変わった時(RecomputeCardStats)/FINAL EVOLUTION の開始/終了/定期(0.5秒)に呼ぶ。冪等(何度呼んでも同じ)
    public static void Recompute()
    {
        if (Instance == null) return;
        Instance.RecomputeImpl();
    }

    void RecomputeImpl()
    {
        Recomputes++;
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted || !Enabled) { if (active.Count > 0) active.Clear(); return; }
        var keep = new Dictionary<string, Active>();
        foreach (var a in active) keep[a.def.id] = a;
        active.Clear();
        foreach (var c in T.combos)
        {
            if (c == null || !c.enabled || c.abilities == null || c.abilities.Count < 2) continue;
            float sum = 0f, min = 99f; bool ok = true; int n = 0;
            var seen = new HashSet<string>();
            foreach (var id in c.abilities)
            {
                if (string.IsNullOrEmpty(id) || id == UltimateArt.CardId || !seen.Add(id)) { ok = false; break; } // 同じ能力を2回数えない/ULTIMATE は使わない
                int lv = gm.GetAbilityRunStack(id);
                if (lv <= 0) { ok = false; break; }
                sum += lv; min = Mathf.Min(min, lv); n++;
            }
            if (!ok || n < 2) continue;
            if (c.optionalAnyOf != null && c.optionalAnyOf.Count > 0)
            {
                bool any = false;
                foreach (var id in c.optionalAnyOf) if (gm.GetAbilityRunStack(id) > 0) { any = true; break; }
                if (!any) continue;
            }
            float avg = sum / n;
            float level = Mathf.Lerp(avg, min, Mathf.Clamp01(c.minWeight));
            var a = keep.TryGetValue(c.id, out var old) ? old : new Active { def = c };
            a.def = c;
            a.level = level;
            a.factor = Mathf.Lerp(Mathf.Clamp(c.lv1Factor, 0.1f, 1f), 1f, Mathf.InverseLerp(1f, 9f, level));
            int fe = 0; bool aw = DebugForceAwakened;
            foreach (var id in c.abilities) { if (FinalEvolution.IsActive(id)) fe++; if (CardProgression.IsAwakened(id)) aw = true; }
            a.enhanced = fe > 0; a.bothEnhanced = fe >= 2; a.awakened = aw;
            active.Add(a);
            if (old == null && announced.Add(c.id)) { Formed++; Notice(a); Debug.Log($"[Combo] FORMED {c.id} ({string.Join("+", c.abilities)}) Lv{level:0.0} x{a.factor:0.00}"); }
        }
    }

    void Update()
    {
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted || gm.IsGameOver) return;
        if (Time.unscaledTime >= recomputeAt) { recomputeAt = Time.unscaledTime + 0.5f; RecomputeImpl(); }
        CardProcs.ComboTick(this);
    }

    public Active Get(ComboTuning.Module m)
    {
        if (!Enabled) return null;
        foreach (var a in active) if (a.def.module == m) return a;
        return null;
    }
    public Active Get(string id) { foreach (var a in active) if (a.def.id == id) return a; return null; }
    public static bool IsActive(string id) => Instance != null && Instance.Get(id) != null;
    // HUD: このカード(能力)が成立中の COMBO に使われているか
    public static bool UsedInCombo(string abilityId)
    {
        if (Instance == null || abilityId == null) return false;
        foreach (var a in Instance.active) if (a.def.abilities.Contains(abilityId)) return true;
        return false;
    }

    // ===================================================================== 成立の通知(短い。ゲームは止めない)
    readonly Queue<Active> pendingNotice = new Queue<Active>();
    Active noticing; float noticeAt, noticeUntil = -1f;
    void Notice(Active a)
    {
        pendingNotice.Enqueue(a);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.ComboFormed); // 音の再設計(2026-10-06): COMBO の専用の音
    }

    static Texture2D white;
    void OnGUI()
    {
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted || gm.IsGameOver) return;
        if (Time.unscaledTime > noticeUntil)
        {
            if (pendingNotice.Count == 0) return;
            noticing = pendingNotice.Dequeue();
            noticeAt = Time.unscaledTime; noticeUntil = noticeAt + 1.1f;
        }
        if (noticing == null || Event.current.type != EventType.Repaint) return;
        if (white == null) { white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave }; white.SetPixel(0, 0, Color.white); white.Apply(); }
        float t = Time.unscaledTime - noticeAt, k = Mathf.Clamp01(t / 0.12f) * Mathf.Clamp01((noticeUntil - Time.unscaledTime) / 0.25f);
        float s = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 720f);
        float w = 420f * s, h = 76f * s;
        var r = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.30f, w, h);
        Color keep = GUI.color;
        Color col = noticing.def.color;
        bool gold = noticing.awakened;
        GUI.color = new Color(0f, 0f, 0f, 0.6f * k); GUI.DrawTexture(r, white);
        GUI.color = new Color(gold ? 1f : col.r, gold ? 0.85f : col.g, gold ? 0.35f : col.b, 0.9f * k);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, 3f * s), white); GUI.DrawTexture(new Rect(r.x, r.yMax - 3f * s, r.width, 3f * s), white);
        if (t < 0.15f) { GUI.color = new Color(col.r, col.g, col.b, 0.25f * (1f - t / 0.15f)); GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), white); } // 小さな光
        GUI.color = keep;
        var big = UiKit.Label(30f * s, TextAnchor.UpperCenter, true, new Color(1f, 1f, 1f, k));
        var small = UiKit.Label(16f * s, TextAnchor.LowerCenter, false, new Color(0.9f, 0.9f, 1f, k));
        string names = string.Join(" + ", noticing.def.abilities.ConvertAll(id => CardDatabase.FindBaseById(id) != null ? CardDatabase.FindBaseById(id).cardName : id));
        LocGUI.Label(new Rect(r.x, r.y + 4f * s, r.width, 40f * s), (gold ? "★ " : "") + "COMBO!  " + noticing.def.displayName, big);
        LocGUI.Label(new Rect(r.x, r.y, r.width, r.height - 6f * s), names, small);
    }

    // ===================================================================== CONTINUE
    [System.Serializable]
    public class SaveState { public string id; public int counter; }
    [System.Serializable]
    public class SaveData { public List<SaveState> states = new List<SaveState>(); public List<string> announced = new List<string>(); }

    public static SaveData Export()
    {
        var d = new SaveData();
        if (Instance == null) return d;
        foreach (var a in Instance.active) if (a.counter != 0) d.states.Add(new SaveState { id = a.def.id, counter = a.counter });
        d.announced.AddRange(Instance.announced);
        return d;
    }

    // カードの取り直しの後で呼ぶ。成立はいまの能力から計算し直し、一時的な数えだけ戻す(2回読んでも同じ)
    public static void Import(SaveData d)
    {
        if (Instance == null) return;
        Instance.ResetRun();
        if (d != null && d.announced != null) foreach (var id in d.announced) Instance.announced.Add(id); // 通知は出し直さない
        Instance.RecomputeImpl();
        if (d != null && d.states != null)
            foreach (var s in d.states)
            {
                var a = Instance.Get(s.id);
                if (a != null) a.counter = s.counter;
            }
        Debug.Log($"[Combo] restored for CONTINUE: {Instance.active.Count} combo(s) recomputed from the run's abilities, {d?.states?.Count ?? 0} temporary state(s)");
    }

    // 開発用
    public static int ActiveCount => Instance != null ? Instance.active.Count : 0;
}
