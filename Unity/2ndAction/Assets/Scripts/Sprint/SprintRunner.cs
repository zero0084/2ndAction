using System.Collections.Generic;
using UnityEngine;

// 疾走出発の画面の演出(2026-10-05 試作、2026-10-06 見た目/リング報酬の改善)。実際のキャラは出発地点で止まったまま(GameManager.CountdownActive)で、
// ここが全画面の演出(流れる景色/走るキャラ/距離/自動取得の表示/リング)を描き、決まった秒数で到着する。
//  ・時間は実時間。カードの選択中/一時停止中/アプリが裏にある間は進まない(到着が割り込まない)。
//  ・関門(1,000m ごと)を通過するたびに、ボス報酬ぶんを1枚自動で取得(GameManager.SprintGrantRandomCard)。目的地の関門は通過しない。
//    候補が無い時は何も取らない(MILE にもしない)。右側に積み重ねず、状態の1行にまとめる。
//  ・リング: 通過する 5000m ごとに1つ。早めに予告し(景色の速さと無関係の秒数)、上下フリック(またはキー/パッド/画面の▲▼)で
//    高さ(上/中/下)を合わせる。くぐる瞬間の前後に判定の幅を持たせる(判定は段と時間。キャラの表示の大きさとは無関係)。
//    成功 → 弾ける演出 → 報酬は1回だけ:
//      カードの候補がある   … 「BONUS CARD」→ 追加の3択(選んだら再開)
//      デッキの対象カードがすべてラン中 Lv9 … 「+N MILE」を自動で受け取り、止まらずに続ける(SprintTuning.ringMileReward)
//      それ以外の理由で空   … 報酬なし(全 Lv9 と混同しない。原因はログ)
//    逃しても何も減らない。何も操作しなくても到着する。
//  ・到着: 通常のランへ戻る時は、疾走のキャラを実際のキャラの位置/大きさへ短く寄せながら画面を薄くする(BeginOutro)。
public class SprintRunner : MonoBehaviour
{
    public static SprintRunner Instance { get; private set; }
    // 確認用(自動テスト): リングの扱い(-1 = 人の操作 / 0 = 全部逃す / 1 = 全部くぐる / 2 = 交互)と時間の倍率
    public static int QaRingPolicy = -1;
    public static float QaTimeScale = 1f;

    public bool Done { get; private set; }
    public bool OutroDone { get; private set; }
    public float ArrivalMeters { get; private set; }
    public float DistanceNow { get; private set; }
    public int Destination { get; private set; }
    public float TotalSeconds { get; private set; }
    public float Elapsed => t;
    public int RingsTotal => rings.Count;
    public int RingsSucceeded { get; private set; }
    public int RingsMissed { get; private set; }
    public int RingsNoCandidate { get; private set; }
    public int RingCardRewards { get; private set; }
    public int RingMileRewards { get; private set; }
    public int GatesPassed { get; private set; }
    public int GatesTotal { get; private set; }
    public int AutoSkipped { get; private set; }
    public int FeedRowsNoCandidate { get; private set; }   // 「取れるカードなし」を右側に積んだ行の数(確認用: 0 のはず)
    public bool AllMaxedAtStart { get; private set; }
    public bool AllMaxedNotified { get; private set; }
    public int AllMaxedNotices { get; private set; }
    public int Lane => lane;
    public float PausedSeconds { get; private set; }
    public float CharHeightPx { get; private set; }          // 確認用: 疾走のキャラの表示の高さ
    public Rect LastCharRect { get; private set; }
    public Rect LastRingRect { get; private set; }
    public int OutroFrames { get; private set; }             // 確認用: 到着の補間を描いたフレーム数
    public float OutroProgress => outro ? Mathf.Clamp01(outroT / Mathf.Max(0.05f, SprintTuning.I.outroSeconds)) : 0f;
    public float NextRingLead { get { foreach (var r in rings) if (!r.resolved) return r.time - t; return -1f; } } // 確認用

    GameManager gm;
    string stageId;
    float t;
    int lane = 1, lanes = 3;
    float visLane = 1f;
    int nextGateK = 1, lastGateK;
    bool waitingChoice, choiceOpened;
    int closedFrames, haltFrames = 99;
    float choiceWaitSince;
    float pendingChoiceAt = -1f;
    bool allMaxed;
    string statusLine = "";
    float introStart = -1f;
    string notice = ""; float noticeUntil;
    bool outro; float outroT; // 到着の補間の経過(1フレーム最大 0.05 秒: 到着の処理の重い1フレームで飛ばさない)
    Rect outroFrom;

    class Ring { public float meters, time; public int lane; public bool matched, resolved, success; }
    readonly List<Ring> rings = new List<Ring>();
    class Feed { public CardDefinition card; public int lv; public float at; public string text; }
    readonly List<Feed> feed = new List<Feed>();
    class Burst { public float at; public int lane; public string text; public Color col; public float[] ang, spd; }
    readonly List<Burst> bursts = new List<Burst>();
    readonly List<Vector2> trail = new List<Vector2>(); // (実時間, 足元の段の値)
    string banner = ""; float bannerUntil; Color bannerColor = Color.white;

    Texture2D bg, ringTex, glowTex, white;
    Sprite[] runFrames;
    readonly float[] streakY = new float[26], streakX = new float[26], streakLen = new float[26];

    public static SprintRunner Begin(GameManager gm, string stageId, int destination)
    {
        var go = new GameObject("SprintRunner");
        var r = go.AddComponent<SprintRunner>();
        r.Setup(gm, stageId, destination);
        return r;
    }

    void Setup(GameManager g, string sid, int destination)
    {
        Instance = this;
        gm = g; stageId = sid; Destination = destination;
        var tn = SprintTuning.I;
        lanes = Mathf.Clamp(tn.lanes, 2, 3);
        lane = lanes == 3 ? 1 : 0;
        visLane = lane;
        ArrivalMeters = Mathf.Max(0f, destination - tn.arriveBeforeMeters);
        GatesTotal = Mathf.FloorToInt(ArrivalMeters / 1000f);
        lastGateK = GatesTotal;
        TotalSeconds = tn.SecondsFor(destination);
        // リング同士の間隔が短すぎる時は全体を遅くする(景色の速さに操作の猶予を合わせない)
        int ringCount = 0;
        for (float m = tn.ringEveryMeters; m < ArrivalMeters - 1f; m += tn.ringEveryMeters) ringCount++;
        if (ringCount > 0)
        {
            float gap = TotalSeconds * tn.ringEveryMeters / Mathf.Max(1f, ArrivalMeters);
            if (gap < tn.ringMinGapSeconds) TotalSeconds *= tn.ringMinGapSeconds / Mathf.Max(0.1f, gap);
            // 最初のリングの予告が出発直後にかからないように
            float first = TotalSeconds * tn.ringEveryMeters / Mathf.Max(1f, ArrivalMeters);
            if (first < tn.ringTelegraphSeconds + 0.8f) TotalSeconds *= (tn.ringTelegraphSeconds + 0.8f) / Mathf.Max(0.1f, first);
        }
        for (float m = tn.ringEveryMeters; m < ArrivalMeters - 1f; m += tn.ringEveryMeters)
            rings.Add(new Ring { meters = m, time = TotalSeconds * m / ArrivalMeters, lane = Random.Range(0, lanes) });
        // 見た目
        var st = StageDatabase.FindById(stageId);
        bg = st != null ? st.thumbnail : null;
        // 走りのコマ: アニメーターが実際に使っている物(キャラ定義に無いキャラは既定のコマを使う = 黒剣士など)
        var pa = PlayerController.Instance != null ? PlayerController.Instance.GetComponentInChildren<PlayerAnimator>() : null;
        var def = CharacterDatabase.FindById(gm.ActiveRunCharacterId);
        runFrames = pa != null && pa.runFrames != null && pa.runFrames.Length > 0 ? pa.runFrames : def != null ? def.runFrames : null;
        white = Texture2D.whiteTexture;
        ringTex = MakeRing(128, 0.78f);
        glowTex = MakeGlow(64);
        for (int i = 0; i < streakY.Length; i++) { streakY[i] = Random.value; streakX[i] = Random.value; streakLen[i] = Random.Range(0.08f, 0.3f); }
        // 最初から全 Lv9 なら、開始の説明で「リング報酬は MILE」と案内する
        AllMaxedAtStart = allMaxed = gm.SprintAllMaxed;
        if (allMaxed) { AllMaxedNotified = true; AllMaxedNotices++; }
        Debug.Log($"[Sprint] start {stageId} -> {destination}m: arrival {ArrivalMeters:F0}m, {TotalSeconds:F1}s, gates to grant {GatesTotal}, rings {rings.Count}, charScale {tn.charScale:F2}, pool [{gm.SprintLastPoolDetail}]{(allMaxed ? " ALL MAXED at start -> ring reward = MILE" : "")}");
    }

    static Texture2D MakeRing(int size, float inner)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Clamp01((1f - d) * 18f) * Mathf.Clamp01((d - inner) * 18f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return tex;
    }

    // 柔らかい光の玉(中心が明るく外へ消える)
    static Texture2D MakeGlow(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Clamp01(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c);
                float a = (1f - d) * (1f - d);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return tex;
    }

    bool Halted => gm == null || gm.SprintChoiceOpen || gm.IsRewardSequenceRunning || gm.PauseMenuOpen || !Application.isFocused && Application.isMobilePlatform;

    void OnApplicationPause(bool paused) { appPaused = paused; }
    bool appPaused;

    void Update()
    {
        if (outro)
        {
            outroT += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (outroT >= Mathf.Max(0.05f, SprintTuning.I.outroSeconds)) OutroDone = true;
            return;
        }
        if (Done || gm == null) return;
        var tn = SprintTuning.I;
        ReadInput();
        visLane = Mathf.Lerp(visLane, lane, 1f - Mathf.Exp(-Mathf.Max(1f, tn.laneFollow) * Time.unscaledDeltaTime));
        if (waitingChoice)
        {
            // リングの3択: 開いてから閉じるまで止める
            if (!choiceOpened && Time.unscaledTime - choiceWaitSince > 0.1f) choiceOpened = true;
            if (gm.SprintChoiceOpen || gm.IsRewardSequenceRunning) { closedFrames = 0; return; }
            // 選択の状態が1フレームだけ閉じて見えることがある(開き直し)ので、閉じたまま数フレーム続いてから再開する
            if (++closedFrames < 4) return;
            waitingChoice = false;
            Banner("疾走再開!", new Color(0.6f, 0.95f, 1f), 1.0f);
            CheckAllMaxed(); // 選んだカードで全 Lv9 になったら、以降は MILE と案内
        }
        if (Halted || appPaused) { PausedSeconds += Time.unscaledDeltaTime; haltFrames = 0; return; }
        if (++haltFrames < 3 && t > 0f) return; // 止まっていた直後の揺れでは進めない
        if (introStart < 0f) introStart = Time.unscaledTime; // 説明は実際に動き始めた時から
        float dt = Time.unscaledDeltaTime * Mathf.Max(0.01f, QaTimeScale);
        t = Mathf.Min(TotalSeconds, t + dt);
        DistanceNow = ArrivalMeters * t / Mathf.Max(0.01f, TotalSeconds);
        trail.Add(new Vector2(Time.unscaledTime, visLane));
        while (trail.Count > 0 && Time.unscaledTime - trail[0].x > 0.6f) trail.RemoveAt(0);

        // 関門の通過 → ボス報酬ぶんの自動取得(目的地の関門は通過しない)
        while (nextGateK <= lastGateK && DistanceNow >= nextGateK * 1000f)
        {
            var c = gm.SprintGrantRandomCard();
            GatesPassed++;
            if (c != null) feed.Add(new Feed { card = c, lv = gm.GetCurrentRunStack(c.cardId), at = Time.unscaledTime });
            else AutoSkipped++; // 候補なし: 右側へ積まず、状態の1行にまとめる
            nextGateK++;
            CheckAllMaxed();
        }

        // 成功したリングのカードの3択(弾ける演出を少し見せてから開く)
        if (pendingChoiceAt > 0f && Time.unscaledTime >= pendingChoiceAt)
        {
            pendingChoiceAt = -1f;
            if (gm.StartSprintRingChoice()) { RingCardRewards++; waitingChoice = true; choiceOpened = false; choiceWaitSince = Time.unscaledTime; }
            else
            {
                // 演出の間の自動取得で候補が尽きた: 全 Lv9 になったのなら MILE(どちらか1回だけ)
                CheckAllMaxed();
                if (allMaxed) { int m = gm.SprintGrantRingMile(); RingMileRewards++; SetLastBurstText($"+{m} MILE", MileColor); if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.MileGet); }
                else { RingsNoCandidate++; Banner("RING!  今は取れるカードがありません", new Color(1f, 0.8f, 0.5f), 1.4f); }
            }
        }

        // リング
        foreach (var r in rings)
        {
            if (r.resolved) continue;
            if (QaRingPolicy >= 0 && t >= r.time - tn.ringTelegraphSeconds * 0.5f)
            {
                int idx = rings.IndexOf(r);
                bool want = QaRingPolicy == 1 || (QaRingPolicy == 2 && idx % 2 == 0);
                lane = want ? r.lane : (r.lane + 1) % lanes;
            }
            float w = tn.ringWindowSeconds;
            if (t >= r.time - w && t <= r.time + w && lane == r.lane) r.matched = true;
            if (t > r.time + w)
            {
                r.resolved = true;
                if (r.matched) RingSuccess(r);
                else
                {
                    RingsMissed++;
                    Banner("リングを逃した", new Color(0.8f, 0.85f, 0.95f), 0.9f);
                    Debug.Log($"[Sprint] ring {r.meters:F0}m: missed (ring lane {r.lane}, player lane {lane})");
                }
                break; // 1フレームに1つ
            }
        }

        if (t >= TotalSeconds && !waitingChoice && pendingChoiceAt < 0f)
        {
            Done = true;
            Debug.Log($"[Sprint] run done: {GatesPassed} gates granted (no candidate {AutoSkipped}), rings {RingsSucceeded}/{rings.Count} (missed {RingsMissed}; card {RingCardRewards}, MILE {RingMileRewards}, none {RingsNoCandidate}), paused {PausedSeconds:F1}s");
        }
    }

    static readonly Color CardColor = new Color(1f, 0.88f, 0.35f), MileColor = new Color(0.55f, 1f, 0.7f);

    // リング成功: 報酬はカードか MILE のどちらか1回だけ
    void RingSuccess(Ring r)
    {
        r.success = true;
        RingsSucceeded++;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.RingBurst); // 音の再設計(2026-10-06): リング成功
        var st = gm.SprintPoolDiagnose(out string why);
        var b = AddBurst(r.lane);
        if (st == GameManager.SprintPoolState.HasCandidates)
        {
            b.text = "BONUS CARD"; b.col = CardColor;
            pendingChoiceAt = Time.unscaledTime + Mathf.Max(0f, SprintTuning.I.ringChoiceDelay);
        }
        else if (st == GameManager.SprintPoolState.AllMaxed)
        {
            int m = gm.SprintGrantRingMile();
            RingMileRewards++;
            b.text = $"+{m} MILE"; b.col = MileColor;
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.MileGet);
            CheckAllMaxed();
        }
        else
        {
            RingsNoCandidate++;
            b.text = "RING!"; b.col = new Color(1f, 0.8f, 0.5f);
            Banner("今は取れるカードがありません", new Color(1f, 0.8f, 0.5f), 1.4f);
        }
        Debug.Log($"[Sprint] ring {r.meters:F0}m: SUCCESS (lane {r.lane}) -> {b.text} [{st}: {why}]");
    }

    Burst AddBurst(int ringLane)
    {
        var b = new Burst { at = Time.unscaledTime, lane = ringLane, ang = new float[16], spd = new float[16] };
        for (int i = 0; i < b.ang.Length; i++) { b.ang[i] = (i + Random.value * 0.6f) / b.ang.Length * Mathf.PI * 2f; b.spd[i] = Random.Range(0.75f, 1.25f); }
        bursts.Add(b);
        return b;
    }
    void SetLastBurstText(string s, Color c) { if (bursts.Count > 0) { bursts[bursts.Count - 1].text = s; bursts[bursts.Count - 1].col = c; } }

    // デッキの対象カードがすべて Lv9 になった時に一度だけ案内する(最初から全 Lv9 なら開始の説明で)
    void CheckAllMaxed()
    {
        bool now = gm.SprintAllMaxed;
        if (now && !allMaxed && !AllMaxedNotified)
        {
            AllMaxedNotified = true; AllMaxedNotices++;
            notice = "デッキ内のカードがすべてLv9になりました。\n以降のリング報酬はMILEです";
            noticeUntil = Time.unscaledTime + 3.2f;
            Debug.Log($"[Sprint] all deck cards reached run Lv{GameManager.MaxRunCardLevel} at {DistanceNow:F0}m - ring rewards are MILE from now on [{gm.SprintLastPoolDetail}]");
        }
        allMaxed = now;
    }

    // 到着: 通常のランへ戻る補間を始める(GameManager.SprintRoutine から。到着の処理の後)
    public void BeginOutro()
    {
        outro = true; outroT = 0f;
        outroFrom = LastCharRect;
    }

    // 上下フリック(画面のどこでも)/ 画面右下の▲▼ / キー(↑↓ W S)/ パッド
    Vector2 downPos; bool down;
    void ReadInput()
    {
        if (QaRingPolicy >= 0) return;
        // キー(↑↓ W S)/ パッド(十字キー・スティック・A=上 / B=下)。GameInput(2026-10-06)
        if (!PadNav.MenuActive && (GameInput.Down(GameAction.NavUp) || GameInput.Down(GameAction.Jump))) MoveLane(1);
        if (!PadNav.MenuActive && (GameInput.Down(GameAction.NavDown) || GameInput.Down(GameAction.AttackDown))) MoveLane(-1);
        Vector2 p = Input.mousePosition;
        if (Input.touchCount > 0) p = Input.GetTouch(0).position;
        bool pressed = Input.GetMouseButton(0) || Input.touchCount > 0;
        if (pressed && !down) { down = true; downPos = p; }
        else if (!pressed && down)
        {
            down = false;
            float dy = p.y - downPos.y, min = Screen.height * 0.06f;
            if (Mathf.Abs(dy) > min && Mathf.Abs(dy) > Mathf.Abs(p.x - downPos.x)) MoveLane(dy > 0 ? 1 : -1);
            else
            {
                // タップ: 画面右下の▲▼
                Rect up = UpButton(), dn = DownButton();
                Vector2 gp = new Vector2(p.x, Screen.height - p.y);
                if (up.Contains(gp)) MoveLane(1); else if (dn.Contains(gp)) MoveLane(-1);
            }
        }
    }
    static Rect UpButton() { float s = Screen.height / 1080f; return new Rect(Screen.width - 200f * s, Screen.height - 420f * s, 160f * s, 150f * s); }
    static Rect DownButton() { float s = Screen.height / 1080f; return new Rect(Screen.width - 200f * s, Screen.height - 250f * s, 160f * s, 150f * s); }

    void MoveLane(int d) { if (waitingChoice) return; lane = Mathf.Clamp(lane + d, 0, lanes - 1); }

    void Banner(string s, Color c, float sec) { banner = s; bannerColor = c; bannerUntil = Time.unscaledTime + sec; }

    // 実際の操作に合わせた案内(最後に使った機器)
    static string ControlHint()
    {
        switch (GameInput.LastDevice)
        {
            case InputDeviceKind.Gamepad: return "十字キー / スティックの上下(A = 上 / B = 下)";
            case InputDeviceKind.Keyboard: return "↑↓ キー / W・S";
            default: return "上下にフリック / 右下の ▲▼";
        }
    }

    // ===================================================================== //
    void OnGUI()
    {
        if (gm == null) return;
        if (Done && !outro) return;
        if (!outro && (waitingChoice || gm.SprintChoiceOpen)) return; // カードの3択(と取得中のカード一覧)を見せる
        GUI.depth = -900;
        var tn = SprintTuning.I;
        float W = Screen.width, H = Screen.height, s = H / 1080f;
        float speedPhase = t * 2.4f;
        float op = 1f; // 画面全体の濃さ(到着の補間で薄くなる)
        float op01 = 0f;
        if (outro)
        {
            op01 = OutroProgress;
            op = 1f - Mathf.SmoothStep(0f, 1f, op01);
        }
        Color keep = GUI.color;

        // 背景: ステージの絵を横に流す(暗め)
        GUI.color = new Color(1f, 1f, 1f, op);
        UiKit.Fill(new Rect(0, 0, W, H), new Color(0.05f, 0.06f, 0.1f, 1f));
        if (bg != null)
        {
            float aspect = (float)bg.width / Mathf.Max(1, bg.height);
            float bw = H * aspect;
            float off = (speedPhase * 0.9f) % 1f;
            GUI.color = new Color(0.75f, 0.78f, 0.85f, op);
            for (int i = -1; i < Mathf.CeilToInt(W / bw) + 2; i++)
                GUI.DrawTextureWithTexCoords(new Rect(i * bw - off * bw, 0, bw + 1f, H), bg, new Rect(0, 0, 1, 1));
            GUI.color = new Color(1f, 1f, 1f, op);
        }
        // 流れの線
        for (int i = 0; i < streakY.Length; i++)
        {
            float x = ((streakX[i] - t * (1.6f + streakLen[i] * 6f)) % 1f + 1f) % 1f;
            UiKit.Fill(new Rect(x * W, streakY[i] * H * 0.75f, streakLen[i] * W, 2f * s), new Color(1f, 1f, 1f, 0.25f));
        }
        // 地面の帯
        float groundY = H * 0.78f;
        UiKit.Fill(new Rect(0, groundY, W, H - groundY), new Color(0.08f, 0.07f, 0.06f, 0.92f));
        for (int i = 0; i < 14; i++)
        {
            float x = ((i / 14f - speedPhase * 1.7f) % 1f + 1f) % 1f;
            UiKit.Fill(new Rect(x * W, groundY + 4f * s, 90f * s, 6f * s), new Color(1f, 0.85f, 0.45f, 0.35f));
        }

        // 高さの段(上/中/下)
        float laneH = 150f * s, baseY = groundY + 8f * s; // 一番下の段 = 地面の帯に足が乗る
        float LaneY(float l) => baseY - l * laneH;
        float charX = W * 0.28f;
        float th = 260f * s * Mathf.Clamp(tn.charScale, 0.3f, 1.5f); // 疾走中のキャラの高さ(従来 260)
        CharHeightPx = th;
        float RingCenterY(int l) => LaneY(l) - th * tn.torsoFrac;  // リングの中心 = その段にいる時のキャラの胴体の中心

        if (!outro)
        {
            // リング(予告: 右端から近づく)
            foreach (var r in rings)
            {
                if (r.resolved && t > r.time + 0.6f) continue;
                if (r.success) continue; // 成功したリングは弾ける演出(下)で描く
                float lead = r.time - t;
                if (lead > tn.ringTelegraphSeconds || lead < -0.6f) continue;
                float k = Mathf.Clamp01(1f - lead / tn.ringTelegraphSeconds);
                float rx = Mathf.Lerp(W + 40f * s, charX, k);
                float rs = Mathf.Lerp(120f, 220f, k) * s;
                Color rc = r.resolved ? new Color(0.6f, 0.6f, 0.7f, 0.6f) : (lane == r.lane ? new Color(0.5f, 1f, 0.6f, 1f) : new Color(1f, 0.75f, 0.3f, 1f));
                GUI.color = rc;
                var rr = new Rect(rx - rs * 0.32f, RingCenterY(r.lane) - rs * 0.5f, rs * 0.64f, rs);
                GUI.DrawTexture(rr, ringTex);
                if (!r.resolved && lead < 0.3f) LastRingRect = rr;
                GUI.color = Color.white;
                if (!r.resolved)
                {
                    // 予告の矢印(右端): どの高さか
                    UiKit.Fill(new Rect(W - 26f * s, RingCenterY(r.lane) - 40f * s, 14f * s, 80f * s), new Color(rc.r, rc.g, rc.b, 0.85f));
                    GUI.Label(new Rect(W - 420f * s, RingCenterY(r.lane) - 70f * s, 380f * s, 40f * s), $"RING {(r.lane == 2 ? "▲上" : r.lane == 1 ? "■中" : "▼下")}  {Mathf.Max(0f, lead):F1}", UiKit.Label(30f * s, TextAnchor.MiddleRight, true, rc));
                }
            }

            // 足元の光の軌跡(疾走専用: 空中の段でも光の上を走っているように)
            float feetNow = LaneY(visLane);
            for (int i = 0; i < trail.Count; i++)
            {
                float age = Time.unscaledTime - trail[i].x;
                float k = 1f - age / 0.6f;
                if (k <= 0f) continue;
                float x = charX - age * W * 0.75f;
                float y = LaneY(trail[i].y);
                GUI.color = new Color(0.6f, 0.95f, 1f, 0.22f * k);
                GUI.DrawTexture(new Rect(x - 26f * s, y - 7f * s, 52f * s, 14f * s), glowTex);
            }
            GUI.color = new Color(1f, 0.95f, 0.75f, visLane > 0.15f ? 0.55f : 0.3f); // 足元の光の足場(地面の段では控えめ)
            GUI.DrawTexture(new Rect(charX - th * 0.36f, feetNow - 9f * s, th * 0.72f, 18f * s), glowTex);
            GUI.color = Color.white;
        }

        // 走るキャラ(到着の補間: 実際のキャラの位置/大きさへ寄せる)
        if (runFrames != null && runFrames.Length > 0)
        {
            var sp = runFrames[(int)(t * 14f) % runFrames.Length];
            if (sp != null)
            {
                Rect tr = sp.textureRect;
                float tw = th * tr.width / Mathf.Max(1f, tr.height);
                Rect cr = new Rect(charX - tw * 0.5f, LaneY(visLane) - th, tw, th);
                if (!outro) LastCharRect = cr;
                else
                {
                    float ez = Mathf.SmoothStep(0f, 1f, op01);
                    cr = outroFrom;
                    if (PlayerScreenRect(out Rect target))
                    {
                        // 足元の中心と高さを合わせる(横幅は絵の比率のまま)
                        float h2 = Mathf.Lerp(outroFrom.height, target.height, ez);
                        float w2 = h2 * tr.width / Mathf.Max(1f, tr.height);
                        float cx = Mathf.Lerp(outroFrom.center.x, target.center.x, ez);
                        float by = Mathf.Lerp(outroFrom.yMax, target.yMax, ez);
                        cr = new Rect(cx - w2 * 0.5f, by - h2, w2, h2);
                    }
                }
                Rect uv = new Rect(tr.x / sp.texture.width, tr.y / sp.texture.height, tr.width / sp.texture.width, tr.height / sp.texture.height);
                GUI.color = new Color(1f, 1f, 1f, outro ? Mathf.Clamp01((1f - op01) * 4f) : 1f); // 最後に実際のキャラへ重なって消える
                GUI.DrawTextureWithTexCoords(cr, sp.texture, uv);
                GUI.color = Color.white;
            }
        }
        if (outro) { if (Event.current.type == EventType.Repaint) OutroFrames++; GUI.color = keep; return; }

        // 成功の演出: リングが弾ける光 + 短い粒子 + 「BONUS CARD」/「+N MILE」(次のリング/キャラを隠さない大きさ)
        for (int i = bursts.Count - 1; i >= 0; i--)
        {
            var b = bursts[i];
            float age = Time.unscaledTime - b.at, dur = Mathf.Max(0.3f, tn.ringBurstSeconds);
            if (age > dur) { bursts.RemoveAt(i); continue; }
            float p = age / dur;
            float cx = charX, cy = RingCenterY(b.lane);
            float rs = 220f * s * (1f + 0.55f * Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, p * 1.6f)));
            float ra = Mathf.Clamp01(1f - p * 1.5f);
            GUI.color = new Color(b.col.r, b.col.g, b.col.b, ra);
            GUI.DrawTexture(new Rect(cx - rs * 0.32f, cy - rs * 0.5f, rs * 0.64f, rs), ringTex);
            GUI.color = new Color(1f, 0.97f, 0.85f, Mathf.Clamp01(0.7f - p * 2f));
            float fl = 150f * s;
            GUI.DrawTexture(new Rect(cx - fl * 0.5f, cy - fl * 0.5f, fl, fl), glowTex);
            for (int k = 0; k < b.ang.Length; k++)
            {
                float d = (40f + 230f * b.spd[k] * Mathf.Sqrt(p)) * s;
                float px = cx + Mathf.Cos(b.ang[k]) * d * 0.7f, py = cy + Mathf.Sin(b.ang[k]) * d;
                float ps = 18f * s * (1f - p);
                GUI.color = new Color(b.col.r, b.col.g, b.col.b, Mathf.Clamp01(1f - p) * 0.9f);
                GUI.DrawTexture(new Rect(px - ps * 0.5f, py - ps * 0.5f, ps, ps), glowTex);
            }
            if (!string.IsNullOrEmpty(b.text))
            {
                float ta = Mathf.Clamp01((1f - p) * 3f);
                float ty = cy - 130f * s - 40f * s * p;
                var tr2 = new Rect(cx - 260f * s, ty, 520f * s, 56f * s);
                GUI.color = new Color(1f, 1f, 1f, ta);
                GUI.Label(new Rect(tr2.x + 2f * s, tr2.y + 2f * s, tr2.width, tr2.height), b.text, UiKit.Label(40f * s, TextAnchor.MiddleCenter, true, new Color(0f, 0f, 0f, 0.75f * ta)));
                GUI.Label(tr2, b.text, UiKit.Label(40f * s, TextAnchor.MiddleCenter, true, new Color(b.col.r, b.col.g, b.col.b, ta)));
            }
            GUI.color = Color.white;
        }

        // 上部: 行き先と距離 / 進み具合(関門とリングの印)
        GUI.Label(new Rect(40f * s, 24f * s, W, 50f * s), $"疾走出発  →  {Destination:N0}m の関門の手前へ", UiKit.Label(34f * s, TextAnchor.MiddleLeft, true, new Color(1f, 0.88f, 0.5f)));
        GUI.Label(new Rect(40f * s, 70f * s, W, 90f * s), $"{DistanceNow:N0} m", UiKit.Label(72f * s, TextAnchor.MiddleLeft, true, Color.white));
        Rect bar = new Rect(40f * s, 170f * s, W - 80f * s, 14f * s);
        UiKit.Fill(bar, new Color(1f, 1f, 1f, 0.15f));
        UiKit.Fill(new Rect(bar.x, bar.y, bar.width * DistanceNow / Mathf.Max(1f, ArrivalMeters), bar.height), new Color(1f, 0.8f, 0.35f, 0.9f));
        for (int k = 1; k <= GatesTotal; k++) UiKit.Fill(new Rect(bar.x + bar.width * k * 1000f / ArrivalMeters - 1f, bar.y - 4f * s, 2f, bar.height + 8f * s), new Color(1f, 1f, 1f, 0.35f));
        foreach (var r in rings)
        {
            GUI.color = r.success ? new Color(1f, 0.9f, 0.3f) : r.resolved ? new Color(0.6f, 0.6f, 0.7f) : new Color(0.5f, 0.9f, 1f);
            GUI.DrawTexture(new Rect(bar.x + bar.width * r.meters / ArrivalMeters - 12f * s, bar.y - 12f * s, 24f * s, 36f * s), ringTex);
        }
        GUI.color = Color.white;
        int ringMile = gm.RunRingMile;
        GUI.Label(new Rect(40f * s, 196f * s, W, 36f * s), $"ボス報酬の自動取得 {GatesPassed}/{GatesTotal}   リング {RingsSucceeded}/{rings.Count}{(ringMile > 0 ? $"   リングの MILE +{ringMile}" : "")}", UiKit.Label(24f * s, TextAnchor.MiddleLeft, false, new Color(0.85f, 0.9f, 1f)));

        // 右: 取得したカード(アイコン/名前/更新後のLv)。候補が無い時は積み重ねずに状態の1行
        float fy = 250f * s;
        if (allMaxed || AutoSkipped > 0)
        {
            Rect row = new Rect(W - 520f * s, fy, 480f * s, 64f * s);
            UiKit.Fill(row, allMaxed ? new Color(0.12f, 0.1f, 0.03f, 0.85f) : new Color(0.08f, 0.08f, 0.12f, 0.85f));
            statusLine = allMaxed
                ? $"デッキ全カード Lv{GameManager.MaxRunCardLevel} MAX\nリング報酬は +{tn.ringMileReward} MILE"
                : $"今は取れるカードがありません(×{AutoSkipped})";
            var stl = UiKit.Label(22f * s, TextAnchor.MiddleLeft, true, allMaxed ? new Color(1f, 0.88f, 0.45f) : new Color(0.85f, 0.85f, 0.9f));
            stl.wordWrap = true;
            GUI.Label(new Rect(row.x + 16f * s, row.y, row.width - 24f * s, row.height), statusLine, stl);
            fy += 70f * s;
        }
        int shown = 0;
        for (int i = feed.Count - 1; i >= 0 && shown < 5; i--)
        {
            var f = feed[i];
            float age = Time.unscaledTime - f.at;
            if (age > tn.grantFeedSeconds) break;
            float a = Mathf.Clamp01((tn.grantFeedSeconds - age) / 0.6f);
            Rect row = new Rect(W - 520f * s, fy, 480f * s, 64f * s);
            UiKit.Fill(row, new Color(0.06f, 0.08f, 0.16f, 0.82f * a));
            if (f.card != null && f.card.icon != null) { GUI.color = new Color(1, 1, 1, a); GUI.DrawTexture(new Rect(row.x + 8f * s, row.y + 6f * s, 52f * s, 52f * s), f.card.icon, ScaleMode.ScaleToFit); GUI.color = Color.white; }
            string txt = f.card != null ? $"+ {f.card.cardName}  Lv.{f.lv}{(f.lv >= GameManager.MaxRunCardLevel ? " MAX" : "")}" : f.text;
            if (f.card == null) FeedRowsNoCandidate++;
            GUI.Label(new Rect(row.x + 70f * s, row.y, row.width - 76f * s, row.height), txt, UiKit.Label(26f * s, TextAnchor.MiddleLeft, true, new Color(1f, 0.95f, 0.8f, a)));
            fy += 70f * s; shown++;
        }

        // 右下: 上下の操作
        Rect up = UpButton(), dn = DownButton();
        UiKit.Fill(up, new Color(0.1f, 0.14f, 0.28f, 0.7f)); UiKit.Fill(dn, new Color(0.1f, 0.14f, 0.28f, 0.7f));
        GUI.Label(up, "▲", UiKit.Label(64f * s, TextAnchor.MiddleCenter, true, Color.white));
        GUI.Label(dn, "▼", UiKit.Label(64f * s, TextAnchor.MiddleCenter, true, Color.white));
        if (introStart < 0f || Time.unscaledTime - introStart > tn.introSeconds)
            GUI.Label(new Rect(40f * s, H - 70f * s, W - 300f * s, 50f * s), $"{ControlHint()} で高さを合わせてリングをくぐる(逃しても減るものはありません)", UiKit.Label(24f * s, TextAnchor.MiddleLeft, false, new Color(0.85f, 0.9f, 1f)));

        // 開始の説明(読みやすい大きさで短く)
        if (introStart >= 0f)
        {
            float age = Time.unscaledTime - introStart, dur = Mathf.Max(0.5f, tn.introSeconds);
            if (age < dur)
            {
                float a = Mathf.Clamp01(age / 0.25f) * Mathf.Clamp01((dur - age) / 0.4f);
                string l1 = AllMaxedAtStart ? "上下で移動 / リング通過で +MILE" : "上下で移動 / リング通過で追加カード";
                string l2 = ControlHint();
                string l3 = AllMaxedAtStart ? "デッキ内のカードがすべてLv9です。リング報酬はMILEです" : "";
                Rect pr = new Rect(W * 0.45f - 560f * s, groundY + 22f * s, 1120f * s, (l3.Length > 0 ? 190f : 140f) * s); // 地面の帯(キャラ/リングの通り道を隠さない)
                UiKit.Fill(pr, new Color(0.03f, 0.04f, 0.09f, 0.78f * a));
                GUI.Label(new Rect(pr.x, pr.y + 12f * s, pr.width, 64f * s), l1, UiKit.Label(48f * s, TextAnchor.MiddleCenter, true, new Color(1f, 0.92f, 0.6f, a)));
                GUI.Label(new Rect(pr.x, pr.y + 80f * s, pr.width, 44f * s), l2, UiKit.Label(32f * s, TextAnchor.MiddleCenter, true, new Color(0.85f, 0.95f, 1f, a)));
                if (l3.Length > 0) GUI.Label(new Rect(pr.x, pr.y + 130f * s, pr.width, 44f * s), l3, UiKit.Label(28f * s, TextAnchor.MiddleCenter, true, new Color(0.6f, 1f, 0.75f, a)));
            }
        }
        // 途中で全 Lv9 になった時の案内(一度だけ)
        if (!string.IsNullOrEmpty(notice) && Time.unscaledTime < noticeUntil)
        {
            float a = Mathf.Clamp01((noticeUntil - Time.unscaledTime) / 0.4f);
            Rect pr = new Rect(W * 0.45f - 520f * s, groundY + 30f * s, 1040f * s, 120f * s);
            UiKit.Fill(pr, new Color(0.08f, 0.06f, 0.01f, 0.8f * a));
            var nl = UiKit.Label(36f * s, TextAnchor.MiddleCenter, true, new Color(1f, 0.9f, 0.5f, a)); nl.wordWrap = true;
            GUI.Label(pr, notice, nl);
        }

        if (!string.IsNullOrEmpty(banner) && Time.unscaledTime < bannerUntil)
            GUI.Label(new Rect(0, H * 0.26f, W, 70f * s), banner, UiKit.Label(40f * s, TextAnchor.MiddleCenter, true, bannerColor));
        GUI.color = keep;

        // このオーバーレイの下(止まっているゲーム画面/HUD)へ押下を通さない
        var e = Event.current;
        if (e.type == EventType.MouseDown || e.type == EventType.MouseUp) e.Use();
    }

    // 実際のキャラの画面上の矩形(GUI 座標)。到着の補間の行き先
    bool PlayerScreenRect(out Rect r)
    {
        r = default;
        var pc = PlayerController.Instance; var cam = Camera.main;
        if (pc == null || cam == null) return false;
        bool any = false; Bounds b = default;
        foreach (var sr in pc.GetComponentsInChildren<SpriteRenderer>())
        {
            if (sr == null || !sr.enabled || !sr.gameObject.activeInHierarchy || sr.sprite == null) continue;
            if (!any) { b = sr.bounds; any = true; } else b.Encapsulate(sr.bounds);
        }
        if (!any) return false;
        Vector3 a = cam.WorldToScreenPoint(b.min), c = cam.WorldToScreenPoint(b.max);
        r = Rect.MinMaxRect(Mathf.Min(a.x, c.x), Screen.height - Mathf.Max(a.y, c.y), Mathf.Max(a.x, c.x), Screen.height - Mathf.Min(a.y, c.y));
        return r.width > 1f && r.height > 1f;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }
}
