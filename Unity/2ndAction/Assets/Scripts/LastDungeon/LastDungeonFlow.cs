using System.Collections;
using UnityEngine;

// ラストダンジョンの一連の流れ(2026-09-30)。LAST CORRIDOR(last_corridor)をシングルプレイで走っている時だけ動く。
//
//   0m ── 最高難度の通常区間(難易度の波: LastCorridorDirector) ── 90,000m
//   ── ボスラッシュ(通常の敵なし、BossManager.RushTable: 90〜98k) ── 99,000m
//   ── 静寂(敵/ボス/大きな障害物/危険なし。曲を消して環境音だけ。99,500/99,800/99,900/99,950/99,990/99,999mの表示) ── 100,000m
//   ── 死神三姉妹戦(ReaperFinaleBattle。RESULTへは行かない) ── 勝利
//   ── そのまま走り続けてエンドロール(CreditsRoad: 巨大文字の道。余韻の速さ) ── THANK YOU FOR PLAYING(壊して通る) ── END
//   ── ONE MORE MILE?(OneMoreMileChoice: 自動前進を止めて立つ。YES/NOを攻撃して選ぶ)
//        YES: 砕ける → 再び走り出す(BEYOND: 平らな一本道が続く試作。距離表示も続く)
//        NO : 崩れる → 少し走る → 減速 → 初めて自分で止まる → 静かに数秒 → 暗転 → ホームへ(正常終了。ゲームオーバーではない)
//
// ボス戦/カード/速度/HP/死亡でラン終了、などは全部既存の仕組みのまま(ラスダンだから別ルール、にはしない)。
// マルチプレイ(と起動引数 -lcLegacyFinale)では従来どおり(100,000mは追跡の死神)。
public class LastDungeonFlow : MonoBehaviour
{
    public enum State { Idle, Run, Rush, Silence, Finale, Credits, Choice, Beyond, Stopping, Done }
    public static LastDungeonFlow Instance { get; private set; }
    public State Current { get; private set; } = State.Idle;
    public bool Enabled { get; private set; }

    [Header("エンドロール")]
    [Tooltip("エンドロールを走る速さ(m/s)。余韻を感じられる速さ")] public float creditsSpeed = 8f;
    [Tooltip("エンドロールの始まり(三姉妹を倒した位置から何m先)")] public float creditsStartAhead = 38f;
    [Header("NOを選んだ時")]
    public float stopRunSpeed = 6f, stopRunSeconds = 1.6f, stopDecelSeconds = 2.6f, stopQuietSeconds = 3.2f, stopFadeSeconds = 1.6f;
    [Header("YESを選んだ時")]
    public float beyondRampSeconds = 3f;

    public CreditsRoad Credits { get; private set; }
    public OneMoreMileChoice Choice { get; private set; }
    public ReaperFinaleBattle Finale { get; private set; }
    public float StateTime => Time.time - stateSince;
    float stateSince;
    LastCorridorDirector lc;
    StaffCreditsData credits;

    // 静寂区間の距離表示
    static readonly float[] Callouts = { 99500f, 99800f, 99900f, 99950f, 99990f, 99999f };
    int calloutIndex;
    float calloutAt = -99f;
    string calloutText = "";
    float fade; // 画面の暗転(NOで止まった後)
    string bannerText = ""; float bannerAt = -99f;
    public int CalloutsShown { get; private set; }

    void Awake()
    {
        Instance = this;
        ResetStatics();
        lc = GetComponent<LastCorridorDirector>();
    }

    void OnDestroy()
    {
        if (Instance == this) { Instance = null; ResetStatics(); }
    }

    static void ResetStatics()
    {
        BossManager.RushEnabled = false;
        BossManager.SuppressGates = false;
        BossManager.FinaleAt100k = null;
        GameManager.EscapeBlocked = false;
        GameManager.BlockExpGain = false;
        PlayerController.ScriptedSpeedCapMps = float.PositiveInfinity;
        CameraFollow.ScriptedOffsetX = 0f;
        BgmDirector.ClearOverride();
        WorldPlatforms.Clear();
    }

    bool ShouldRun(GameManager gm)
    {
        if (gm == null || !gm.HasStarted || gm.ActiveRunStageId != LastCorridorDirector.StageId) return false;
        if (NetRunLauncher.IsMultiplayerRun) return false;
        if (lc != null && !lc.hardMode) return false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-lcLegacyFinale") >= 0) return false;
#endif
        return true;
    }

    void SetState(State s)
    {
        if (Current == s) return;
        State from = Current;
        Current = s;
        stateSince = Time.time;
        var gm = GameManager.Instance;
        Debug.Log($"[LastDungeon] state -> {s} at {(gm != null ? gm.MaxDistance : 0f):F0}m");
        // 状態遷移ログ(2026-10-02): フリーズ/二重遷移の時に、最後にどこまで来たかを追えるように(GameManager側の状態も一緒に)
        string tag = s switch
        {
            State.Run => "[FinalDungeon] Enter",
            State.Rush => "[BossRush] Start",
            State.Silence => "[SilentSection] Start",
            State.Finale => "[ReaperBoss] Start",
            State.Credits => "[Ending] Credits Start",
            State.Choice => "[Ending] Credits Finished -> [EndingChoice] Start",
            State.Beyond => "[EndingChoice] YES Selected",
            State.Stopping => "[EndingChoice] NO Selected",
            State.Done => "[Ending] Finished (Home)",
            _ => "[FinalDungeon] " + s,
        };
        Debug.Log($"{tag} ({from} -> {s}) d={(gm != null ? gm.MaxDistance : 0f):F0}{(Debug.isDebugBuild && gm != null ? " | GM " + gm.DebugStateLine() : "")}");
    }

    void Update()
    {
        var gm = GameManager.Instance;
        var pc = PlayerController.Instance;
        if (gm == null || pc == null) return;
        if (!Enabled)
        {
            if (!ShouldRun(gm)) return;
            Enabled = true;
            BossManager.RushEnabled = true;
            BossManager.FinaleAt100k = OnReach100k;
            credits = StaffCreditsData.LoadOrDefault();
            Preload();
            SetState(State.Run);
        }
        if (gm.IsGameOver) return;
        float d = gm.MaxDistance;
        var bm = BossManager.Instance;
        float rushFrom = lc != null ? lc.rushFrom : 90000f, silenceFrom = lc != null ? lc.silenceFrom : 99000f;

        switch (Current)
        {
            case State.Run:
                if (d >= rushFrom - 1f) SetState(State.Rush);
                break;
            case State.Rush:
                if (d >= silenceFrom && (bm == null || !bm.IsBossPhase)) BeginSilence();
                break;
            case State.Silence:
                UpdateSilence(d);
                break;
            case State.Credits:
                UpdateCredits(pc);
                break;
            case State.Choice:
                UpdateChoice(pc);
                break;
            case State.Beyond:
                UpdateBeyond(pc);
                break;
        }
    }

    // 三姉妹/巨大文字の絵を走り始めに読み込んでおく(初めて出る瞬間の読み込みで処理落ちしないように)
    static readonly System.Collections.Generic.List<Object> preloaded = new System.Collections.Generic.List<Object>();
    void Preload()
    {
        preloaded.Clear();
        foreach (var s in new[] { "Eldest", "Second", "Youngest" })
        {
            var d = Resources.Load<ReaperSisterData>("Reapers/" + s + "Data");
            if (d != null) preloaded.Add(d);
        }
        foreach (var sp in Resources.LoadAll<Sprite>("LastDungeon/Glyphs")) preloaded.Add(sp);
        var slab = Resources.Load<Sprite>("LastDungeon/Fx/slab");
        if (slab != null) preloaded.Add(slab);
    }

    // ===================================================================== //
    // 99,000〜100,000m: 静寂
    // ===================================================================== //
    void BeginSilence()
    {
        SetState(State.Silence);
        BossManager.SuppressGates = true;
        GameManager.EscapeBlocked = true;
        BgmDirector.OverrideActive = true;
        BgmDirector.OverrideClip = null;      // 曲を消す(ゆっくり)
        BgmDirector.OverrideFadeSeconds = 5f;
        BgmDirector.OverrideAmbience = null;  // ステージの環境音(風/空気)だけが残る
        BgmDirector.OverrideReason = "silence";
        calloutIndex = 0;
        while (calloutIndex < Callouts.Length && GameManager.Instance.MaxDistance >= Callouts[calloutIndex]) calloutIndex++;
    }

    void UpdateSilence(float d)
    {
        if (calloutIndex < Callouts.Length && d >= Callouts[calloutIndex])
        {
            calloutText = $"{Callouts[calloutIndex]:N0} m";
            calloutAt = Time.time;
            calloutIndex++;
            CalloutsShown++;
            Debug.Log($"[LastDungeon] callout {calloutText}");
        }
    }

    // BossManager: 100,000mに着いた(追跡の死神の代わりに三姉妹戦)
    void OnReach100k()
    {
        if (Current >= State.Finale) return; // 三姉妹戦/エンドロール以降に二重に始めない(デバッグでエンドロールから始めた時も)
        SetState(State.Finale);
        BgmDirector.ClearOverride(); // ボス戦の曲(死神の曲)はBossManagerの台本のボス戦が決める
        calloutText = "100,000 m"; calloutAt = Time.time;
        var pc = PlayerController.Instance;
        Finale = ReaperFinaleBattle.Begin(pc != null ? pc.transform : null);
        Finale.Finished = BeginCredits;
    }

    // ===================================================================== //
    // エンドロール
    // ===================================================================== //
    void BeginCredits()
    {
        SetState(State.Credits);
        var pc = PlayerController.Instance;
        StartCoroutine(EaseSpeedCap(float.PositiveInfinity, creditsSpeed, 3f));
        float startX = pc.transform.position.x + creditsStartAhead;
        Credits = CreditsRoad.Build(startX, credits);
        // 背景: 荒野→洞窟→天空と、これまで走ってきた場所を順に通り、最後は夜明けの色へ
        if (lc != null)
        {
            lc.propsEndLX = (float)FloatingOrigin.ToLogical(startX - 10f);
            lc.fillDepthOverride = 48f; // 床が奈落の上の細い橋から、しっかりした地面へ(どこかへ辿り着いた感じ)
        }
        var am = AudioManager.Instance;
        var lib = am != null ? am.Library : null;
        BgmDirector.OverrideActive = true;
        BgmDirector.OverrideClip = lib != null && lib.homeBgm != null ? lib.homeBgm : null;
        BgmDirector.OverrideFadeSeconds = 3f;
        BgmDirector.OverrideAmbience = lib != null ? lib.homeAmbience : null;
        BgmDirector.OverrideReason = "credits";
        Banner("");
    }

    Sprite ThemeBg(string stageId)
    {
        var tm = TerrainManager.Instance;
        if (tm == null) return null;
        foreach (var t in tm.stageThemes) if (t.stageId == stageId) return t.backgroundSprite;
        return tm.DefaultBackgroundSprite; // 天空回廊はテーマ表を持たず、シーンの既定の背景(空)を使う
    }

    void UpdateCredits(PlayerController pc)
    {
        if (Credits == null) return;
        double px = FloatingOrigin.ToLogical(pc.transform.position.x);
        // 背景のクロスフェード(エンドロールの長さに合わせて4つの景色を通る)
        if (lc != null)
        {
            float span = Mathf.Max(1f, (float)(Credits.ChoiceX - Credits.StartX));
            float u = Mathf.Clamp01((float)(px - Credits.StartX + 30.0) / span) * 3f;
            Sprite[] seq = { ThemeBg("wasteland_road"), ThemeBg("natural_cave"), ThemeBg("sky_corridor"), ThemeBg("sky_corridor") };
            int i = Mathf.Min(2, Mathf.FloorToInt(u));
            float f = Mathf.Clamp01((u - i) * 1.4f - 0.2f);
            if (seq[i] != null)
            {
                lc.bgOverride = true;
                lc.bgOverrideFrom = seq[i];
                lc.bgOverrideTo = seq[i + 1] != null ? seq[i + 1] : seq[i];
                lc.bgOverrideBlend = i >= 2 ? 0f : f;
                // 最後(天空)は夜明けのような少し温かい色へ
                lc.bgOverrideTint = Color.Lerp(new Color(0.85f, 0.85f, 0.92f), new Color(1f, 0.93f, 0.85f), Mathf.Clamp01(u - 2f));
            }
        }
        if (px >= Credits.ChoiceX) BeginChoice(pc);
    }

    // ===================================================================== //
    // ONE MORE MILE?
    // ===================================================================== //
    void BeginChoice(PlayerController pc)
    {
        SetState(State.Choice);
        StartCoroutine(StopForChoice(pc));
    }

    IEnumerator StopForChoice(PlayerController pc)
    {
        yield return EaseSpeedCap(creditsSpeed, 0f, 1.1f);
        pc.autoRunEnabled = false;
        PlayerController.ScriptedSpeedCapMps = float.PositiveInfinity;
        pc.IsStandingIdle = true;
        BgmDirector.OverrideClip = null; // 静かな場所: 曲は止め、環境音だけ
        BgmDirector.OverrideFadeSeconds = 4f;
        BgmDirector.OverrideReason = "choice";
        StartCoroutine(EaseCamera(0f, -(Camera.main != null && Camera.main.GetComponent<CameraFollow>() != null ? Camera.main.GetComponent<CameraFollow>().offsetX : 6f), 1.4f));
        yield return new WaitForSeconds(0.5f);
        float gy = TerrainManager.Instance != null ? (TerrainManager.Instance.GetHeightAt(pc.transform.position.x) ?? pc.transform.position.y) : pc.transform.position.y;
        Choice = OneMoreMileChoice.Build(pc.transform.position.x, gy, credits);
        Choice.Chosen = OnChosen;
    }

    void UpdateChoice(PlayerController pc) { }

    void OnChosen(bool yes)
    {
        if (yes) StartCoroutine(GoBeyond());
        else StartCoroutine(StopByChoice());
    }

    // YES: 走り続ける
    IEnumerator GoBeyond()
    {
        SetState(State.Beyond);
        yield return new WaitForSeconds(1.4f);
        var pc = PlayerController.Instance;
        pc.IsStandingIdle = false;
        pc.autoRunEnabled = true;
        GameManager.EscapeBlocked = false; // 長押しの帰還でいつでも終われる
        StartCoroutine(EaseCamera(CameraFollow.ScriptedOffsetX, 0f, 1.6f));
        var lib = AudioManager.Instance != null ? AudioManager.Instance.Library : null;
        var sky = lib != null ? lib.FindStage("sky_corridor") : null;
        BgmDirector.OverrideClip = sky != null && sky.early != null ? sky.early : (AudioManager.Instance != null ? AudioManager.Instance.gameplayBgm : null);
        BgmDirector.OverrideFadeSeconds = 2f;
        BgmDirector.OverrideAmbience = null;
        BgmDirector.OverrideReason = "beyond";
        Banner("BEYOND");
        // 速度は0から走り出して、元の自然な速さへ戻る
        float natural = pc.runSpeed * pc.NaturalMultiplierAt(pc.DistanceFromStart);
        yield return EaseSpeedCap(0.5f, Mathf.Max(8f, natural), beyondRampSeconds);
        PlayerController.ScriptedSpeedCapMps = float.PositiveInfinity;
    }

    void UpdateBeyond(PlayerController pc) { }

    // NO: 少し走る → 減速 → 止まる → 静かに → 暗転 → ホーム
    IEnumerator StopByChoice()
    {
        SetState(State.Stopping);
        yield return new WaitForSeconds(1.2f);
        var pc = PlayerController.Instance;
        pc.IsStandingIdle = false;
        pc.autoRunEnabled = true;
        StartCoroutine(EaseCamera(CameraFollow.ScriptedOffsetX, 0f, 2.5f));
        yield return EaseSpeedCap(0.3f, stopRunSpeed, 0.6f);
        yield return new WaitForSeconds(stopRunSeconds);
        yield return EaseSpeedCap(stopRunSpeed, 0f, stopDecelSeconds, easeOut: true);
        pc.autoRunEnabled = false;
        PlayerController.ScriptedSpeedCapMps = float.PositiveInfinity;
        StoppedAtDistance = GameManager.Instance.MaxDistance;
        Debug.Log($"[LastDungeon] stopped by choice at {StoppedAtDistance:F1}m");
        pc.PlayVoluntaryStopPose(1.8f);
        yield return new WaitForSeconds(stopQuietSeconds);
        float t = 0f;
        while (t < stopFadeSeconds) { t += Time.unscaledDeltaTime; fade = Mathf.Clamp01(t / stopFadeSeconds); yield return null; }
        SetState(State.Done);
        GameManager.Instance.FinishByChoice(); // 正常終了(MILE確定/BEST更新) → Resultを出さずにホームへ
    }
    public float StoppedAtDistance { get; private set; } = -1f;

    // ===================================================================== //
    // 共通
    // ===================================================================== //
    IEnumerator EaseSpeedCap(float from, float to, float seconds, bool easeOut = false)
    {
        var pc = PlayerController.Instance;
        if (float.IsPositiveInfinity(from)) from = pc != null ? pc.CurrentAutoRunSpeed : to;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / Mathf.Max(0.01f, seconds));
            if (easeOut) k = 1f - (1f - k) * (1f - k);
            PlayerController.ScriptedSpeedCapMps = Mathf.Max(0f, Mathf.Lerp(from, to, k));
            yield return null;
        }
        PlayerController.ScriptedSpeedCapMps = Mathf.Max(0f, to);
    }

    IEnumerator EaseCamera(float from, float to, float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            CameraFollow.ScriptedOffsetX = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds)));
            yield return null;
        }
        CameraFollow.ScriptedOffsetX = to;
    }

    void Banner(string t) { bannerText = t; bannerAt = Time.time; }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // ===================================================================== //
    // 開発版: 三姉妹撃破の直後と同じ状態から始める(EndgameDebug)。静寂区間(99,000m〜、穴/障害物/敵なし)にいる時に呼ぶこと。
    // ===================================================================== //
    void DebugPostVictoryState()
    {
        BossManager.SuppressGates = true;      // 静寂区間と同じ: これ以上ボスの関門を作らない
        GameManager.EscapeBlocked = true;
        GameManager.BlockExpGain = true;       // 三姉妹の勝利と同じ: レベルアップ等を出さない
        if (GameManager.Instance != null) GameManager.Instance.DropPendingChoicesForFinale();
        if (BossManager.Instance != null) BossManager.Instance.DebugMarkDeathSpawned(); // 100,000mで三姉妹戦を始めない
    }

    public void DebugBeginCredits()
    {
        if (!Enabled || Current >= State.Credits) return;
        Debug.Log("[Ending] DEBUG: start from the credits (sisters treated as defeated)");
        DebugPostVictoryState();
        BeginCredits();
    }

    public void DebugBeginChoice()
    {
        if (!Enabled || Current >= State.Choice) return;
        Debug.Log("[EndingChoice] DEBUG: start from ONE MORE MILE? (sisters and credits treated as done)");
        DebugPostVictoryState();
        StartCoroutine(DebugChoiceRoutine());
    }

    IEnumerator DebugChoiceRoutine()
    {
        var pc = PlayerController.Instance;
        yield return EaseSpeedCap(float.PositiveInfinity, creditsSpeed, 1.2f);
        BgmDirector.OverrideActive = true;
        BgmDirector.OverrideClip = null;
        BgmDirector.OverrideFadeSeconds = 1f;
        BgmDirector.OverrideAmbience = null;
        BgmDirector.OverrideReason = "choice";
        BeginChoice(pc);
    }
#endif

    // ===================================================================== //
    // 画面の文字(静寂の距離表示/三姉妹の名前/BEYOND/暗転)
    // ===================================================================== //
    GUIStyle big, mid;
    void OnGUI()
    {
        if (!Enabled) return;
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted) return;
        if (big == null)
        {
            big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            mid = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        }
        float sh = Screen.height;
        big.fontSize = Mathf.RoundToInt(sh * 0.085f);
        mid.fontSize = Mathf.RoundToInt(sh * 0.05f);

        // 静寂: 100,000mへ近づくほど画面の縁が暗くなる
        if (Current == State.Silence && !gm.IsGameOver)
        {
            float k = Mathf.InverseLerp(99000f, 100000f, gm.MaxDistance);
            DrawVignette(0.08f + 0.3f * k);
        }
        // 距離の表示(大きく、ゆっくり現れて消える)
        float ca = CalloutAlpha(calloutAt, 2.6f);
        if (ca > 0f) DrawShadowed(new Rect(0f, sh * 0.34f, Screen.width, sh * 0.14f), calloutText, big, new Color(1f, 0.95f, 0.85f, ca));
        // 三姉妹の名前
        if (Finale != null && !string.IsNullOrEmpty(Finale.Banner))
        {
            float ba = CalloutAlpha(Finale.BannerTime, 2.4f);
            if (ba > 0f) DrawShadowed(new Rect(0f, sh * 0.2f, Screen.width, sh * 0.1f), Finale.Banner, mid, new Color(0.85f, 0.7f, 1f, ba));
        }
        if (!string.IsNullOrEmpty(bannerText))
        {
            float ba = CalloutAlpha(bannerAt, 3.2f);
            if (ba > 0f) DrawShadowed(new Rect(0f, sh * 0.26f, Screen.width, sh * 0.14f), bannerText, big, new Color(1f, 0.92f, 0.6f, ba));
        }
        if (fade > 0f)
        {
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, fade);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, sh), Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }

    static float CalloutAlpha(float at, float dur)
    {
        float t = Time.time - at;
        if (t < 0f || t > dur) return 0f;
        return Mathf.Clamp01(t / 0.5f) * Mathf.Clamp01((dur - t) / 0.8f);
    }

    static void DrawShadowed(Rect r, string text, GUIStyle st, Color c)
    {
        var prev = st.normal.textColor;
        st.normal.textColor = new Color(0f, 0f, 0f, c.a * 0.7f);
        GUI.Label(new Rect(r.x + 3f, r.y + 3f, r.width, r.height), text, st);
        st.normal.textColor = c;
        GUI.Label(r, text, st);
        st.normal.textColor = prev;
    }

    static Texture2D vignette;
    static void DrawVignette(float a)
    {
        if (vignette == null)
        {
            const int n = 64;
            vignette = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx * 0.8f + dy * dy);
                    vignette.SetPixel(x, y, new Color(0f, 0f, 0f, Mathf.SmoothStep(0.25f, 1.25f, r)));
                }
            vignette.Apply();
        }
        var prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, a);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), vignette);
        GUI.color = prev;
    }
}
