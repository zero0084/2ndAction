using System.Collections.Generic;
using UnityEngine;

// 疾走出発の画面の演出(2026-10-05 試作)。実際のキャラは出発地点で止まったまま(GameManager.CountdownActive)で、
// ここが全画面の演出(流れる景色/走るキャラ/距離/自動取得の表示/リング)を描き、決まった秒数で到着する。
//  ・時間は実時間。カードの選択中/一時停止中/アプリが裏にある間は進まない(到着が割り込まない)。
//  ・関門(1,000m ごと)を通過するたびに、ボス報酬ぶんを1枚自動で取得(GameManager.SprintGrantRandomCard)。目的地の関門は通過しない。
//  ・リング: 通過する 5000m ごとに1つ。早めに予告し(景色の速さと無関係の秒数)、上下フリック(またはキー/画面の▲▼)で
//    高さ(上/中/下)を合わせる。くぐる瞬間の前後に判定の幅を持たせる。成功 → 疾走を止めて追加の3択 → 選んだら再開。
//    逃しても何も減らない(追加のボーナスを逃すだけ)。何も操作しなくても到着する。
public class SprintRunner : MonoBehaviour
{
    public static SprintRunner Instance { get; private set; }
    // 確認用(自動テスト): リングの扱い(-1 = 人の操作 / 0 = 全部逃す / 1 = 全部くぐる / 2 = 交互)と時間の倍率
    public static int QaRingPolicy = -1;
    public static float QaTimeScale = 1f;

    public bool Done { get; private set; }
    public float ArrivalMeters { get; private set; }
    public float DistanceNow { get; private set; }
    public int Destination { get; private set; }
    public float TotalSeconds { get; private set; }
    public float Elapsed => t;
    public int RingsTotal => rings.Count;
    public int RingsSucceeded { get; private set; }
    public int RingsMissed { get; private set; }
    public int RingsNoCandidate { get; private set; }
    public int GatesPassed { get; private set; }
    public int GatesTotal { get; private set; }
    public int Lane => lane;
    public float PausedSeconds { get; private set; }
    public float NextRingLead { get { foreach (var r in rings) if (!r.resolved) return r.time - t; return -1f; } } // 確認用

    GameManager gm;
    string stageId;
    float t;
    int lane = 1, lanes = 3;
    int nextGateK = 1, lastGateK;
    bool waitingChoice, choiceOpened;
    int closedFrames, haltFrames = 99;
    float choiceWaitSince;

    class Ring { public float meters, time; public int lane; public bool matched, resolved, success; }
    readonly List<Ring> rings = new List<Ring>();
    class Feed { public CardDefinition card; public int lv; public float at; public string text; }
    readonly List<Feed> feed = new List<Feed>();
    string banner = ""; float bannerUntil; Color bannerColor = Color.white;

    Texture2D bg, ringTex, white;
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
        for (int i = 0; i < streakY.Length; i++) { streakY[i] = Random.value; streakX[i] = Random.value; streakLen[i] = Random.Range(0.08f, 0.3f); }
        Debug.Log($"[Sprint] start {stageId} -> {destination}m: arrival {ArrivalMeters:F0}m, {TotalSeconds:F1}s, gates to grant {GatesTotal}, rings {rings.Count}");
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

    bool Halted => gm == null || gm.SprintChoiceOpen || gm.IsRewardSequenceRunning || gm.PauseMenuOpen || !Application.isFocused && Application.isMobilePlatform;

    void OnApplicationPause(bool paused) { appPaused = paused; }
    bool appPaused;

    void Update()
    {
        if (Done || gm == null) return;
        ReadInput();
        if (waitingChoice)
        {
            // リングの3択: 開いてから閉じるまで止める
            if (!choiceOpened && Time.unscaledTime - choiceWaitSince > 0.1f) choiceOpened = true;
            if (gm.SprintChoiceOpen || gm.IsRewardSequenceRunning) { closedFrames = 0; return; }
            // 選択の状態が1フレームだけ閉じて見えることがある(開き直し)ので、閉じたまま数フレーム続いてから再開する
            if (++closedFrames < 4) return;
            waitingChoice = false;
            Banner("疾走再開!", new Color(0.6f, 0.95f, 1f), 1.0f);
        }
        if (Halted || appPaused) { PausedSeconds += Time.unscaledDeltaTime; haltFrames = 0; return; }
        if (++haltFrames < 3 && t > 0f) return; // 止まっていた直後の揺れでは進めない
        float dt = Time.unscaledDeltaTime * Mathf.Max(0.01f, QaTimeScale);
        t = Mathf.Min(TotalSeconds, t + dt);
        DistanceNow = ArrivalMeters * t / Mathf.Max(0.01f, TotalSeconds);

        // 関門の通過 → ボス報酬ぶんの自動取得(目的地の関門は通過しない)
        while (nextGateK <= lastGateK && DistanceNow >= nextGateK * 1000f)
        {
            var c = gm.SprintGrantRandomCard();
            GatesPassed++;
            feed.Add(new Feed { card = c, lv = c != null ? gm.GetCurrentRunStack(c.cardId) : 0, at = Time.unscaledTime, text = c != null ? null : $"{nextGateK}km 報酬: 取れるカードなし" });
            nextGateK++;
        }

        // リング
        var tn = SprintTuning.I;
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
                if (r.matched)
                {
                    r.success = true;
                    RingsSucceeded++;
                    Banner("RING!  追加のカードを1枚選べます", new Color(1f, 0.9f, 0.4f), 1.2f);
                    if (gm.StartSprintRingChoice()) { waitingChoice = true; choiceOpened = false; choiceWaitSince = Time.unscaledTime; }
                    else { RingsNoCandidate++; Banner("RING! (取れるカードがありません)", new Color(1f, 0.8f, 0.5f), 1.4f); }
                    Debug.Log($"[Sprint] ring {r.meters:F0}m: SUCCESS (lane {r.lane})");
                }
                else
                {
                    RingsMissed++;
                    Banner("リングを逃した", new Color(0.8f, 0.85f, 0.95f), 0.9f);
                    Debug.Log($"[Sprint] ring {r.meters:F0}m: missed (ring lane {r.lane}, player lane {lane})");
                }
                break; // 1フレームに1つ
            }
        }

        if (t >= TotalSeconds && !waitingChoice)
        {
            Done = true;
            Debug.Log($"[Sprint] run done: {GatesPassed} gates granted, rings {RingsSucceeded}/{rings.Count} (missed {RingsMissed}), paused {PausedSeconds:F1}s");
        }
    }

    // 上下フリック(画面のどこでも)/ 画面右下の▲▼ / キー(↑↓ W S)
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
                float s = Screen.height / 1080f;
                Rect up = new Rect(Screen.width - 200f * s, Screen.height - 420f * s, 160f * s, 150f * s);
                Rect dn = new Rect(Screen.width - 200f * s, Screen.height - 250f * s, 160f * s, 150f * s);
                Vector2 gp = new Vector2(p.x, Screen.height - p.y);
                if (up.Contains(gp)) MoveLane(1); else if (dn.Contains(gp)) MoveLane(-1);
            }
        }
    }

    void MoveLane(int d) { if (waitingChoice) return; lane = Mathf.Clamp(lane + d, 0, lanes - 1); }

    void Banner(string s, Color c, float sec) { banner = s; bannerColor = c; bannerUntil = Time.unscaledTime + sec; }

    // ===================================================================== //
    void OnGUI()
    {
        if (Done || gm == null) return;
        if (waitingChoice || gm.SprintChoiceOpen) return; // カードの3択(と取得中のカード一覧)を見せる
        GUI.depth = -900;
        float W = Screen.width, H = Screen.height, s = H / 1080f;
        float speedPhase = t * 2.4f;

        // 背景: ステージの絵を横に流す(暗め)
        GUI.color = Color.white;
        UiKit.Fill(new Rect(0, 0, W, H), new Color(0.05f, 0.06f, 0.1f, 1f));
        if (bg != null)
        {
            float aspect = (float)bg.width / Mathf.Max(1, bg.height);
            float bw = H * aspect;
            float off = (speedPhase * 0.9f) % 1f;
            GUI.color = new Color(0.75f, 0.78f, 0.85f, 1f);
            for (int i = -1; i < Mathf.CeilToInt(W / bw) + 2; i++)
                GUI.DrawTextureWithTexCoords(new Rect(i * bw - off * bw, 0, bw + 1f, H), bg, new Rect(0, 0, 1, 1));
            GUI.color = Color.white;
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
        float LaneY(int l) => baseY - l * laneH;
        float charX = W * 0.28f;

        // リング(予告: 右端から近づく)
        var tn = SprintTuning.I;
        foreach (var r in rings)
        {
            if (r.resolved && Time.unscaledTime > 0f && t > r.time + 0.6f) continue;
            float lead = r.time - t;
            if (lead > tn.ringTelegraphSeconds || lead < -0.6f) continue;
            float k = Mathf.Clamp01(1f - lead / tn.ringTelegraphSeconds);
            float rx = Mathf.Lerp(W + 40f * s, charX, k);
            float rs = Mathf.Lerp(120f, 220f, k) * s;
            Color rc = r.success ? new Color(1f, 0.9f, 0.3f, 1f) : (r.resolved ? new Color(0.6f, 0.6f, 0.7f, 0.6f) : (lane == r.lane ? new Color(0.5f, 1f, 0.6f, 1f) : new Color(1f, 0.75f, 0.3f, 1f)));
            GUI.color = rc;
            GUI.DrawTexture(new Rect(rx - rs * 0.32f, LaneY(r.lane) - rs * 0.5f - 40f * s, rs * 0.64f, rs), ringTex);
            GUI.color = Color.white;
            if (!r.resolved)
            {
                // 予告の矢印(右端): どの高さか
                UiKit.Fill(new Rect(W - 26f * s, LaneY(r.lane) - 80f * s, 14f * s, 80f * s), new Color(rc.r, rc.g, rc.b, 0.85f));
                GUI.Label(new Rect(W - 420f * s, LaneY(r.lane) - 110f * s, 380f * s, 40f * s), $"RING {(r.lane == 2 ? "▲上" : r.lane == 1 ? "■中" : "▼下")}  {Mathf.Max(0f, lead):F1}", UiKit.Label(30f * s, TextAnchor.MiddleRight, true, rc));
            }
        }

        // 走るキャラ
        if (runFrames != null && runFrames.Length > 0)
        {
            var sp = runFrames[(int)(t * 14f) % runFrames.Length];
            if (sp != null)
            {
                Rect tr = sp.textureRect;
                float th = 260f * s, tw = th * tr.width / Mathf.Max(1f, tr.height);
                Rect uv = new Rect(tr.x / sp.texture.width, tr.y / sp.texture.height, tr.width / sp.texture.width, tr.height / sp.texture.height);
                GUI.DrawTextureWithTexCoords(new Rect(charX - tw * 0.5f, LaneY(lane) - th, tw, th), sp.texture, uv);
            }
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
        GUI.Label(new Rect(40f * s, 196f * s, W, 36f * s), $"ボス報酬の自動取得 {GatesPassed}/{GatesTotal}   リング {RingsSucceeded}/{rings.Count}", UiKit.Label(24f * s, TextAnchor.MiddleLeft, false, new Color(0.85f, 0.9f, 1f)));

        // 右: 取得したカード(アイコン/名前/更新後のLv)
        float fy = 250f * s;
        int shown = 0;
        for (int i = feed.Count - 1; i >= 0 && shown < 6; i--)
        {
            var f = feed[i];
            float age = Time.unscaledTime - f.at;
            if (age > tn.grantFeedSeconds) break;
            float a = Mathf.Clamp01((tn.grantFeedSeconds - age) / 0.6f);
            Rect row = new Rect(W - 520f * s, fy, 480f * s, 64f * s);
            UiKit.Fill(row, new Color(0.06f, 0.08f, 0.16f, 0.82f * a));
            if (f.card != null && f.card.icon != null) { GUI.color = new Color(1, 1, 1, a); GUI.DrawTexture(new Rect(row.x + 8f * s, row.y + 6f * s, 52f * s, 52f * s), f.card.icon, ScaleMode.ScaleToFit); GUI.color = Color.white; }
            string txt = f.card != null ? $"+ {f.card.cardName}  Lv.{f.lv}{(f.lv >= GameManager.MaxRunCardLevel ? " MAX" : "")}" : f.text;
            GUI.Label(new Rect(row.x + 70f * s, row.y, row.width - 76f * s, row.height), txt, UiKit.Label(26f * s, TextAnchor.MiddleLeft, true, new Color(1f, 0.95f, 0.8f, a)));
            fy += 70f * s; shown++;
        }

        // 右下: 上下の操作
        Rect up = new Rect(W - 200f * s, H - 420f * s, 160f * s, 150f * s), dn = new Rect(W - 200f * s, H - 250f * s, 160f * s, 150f * s);
        UiKit.Fill(up, new Color(0.1f, 0.14f, 0.28f, 0.7f)); UiKit.Fill(dn, new Color(0.1f, 0.14f, 0.28f, 0.7f));
        GUI.Label(up, "▲", UiKit.Label(64f * s, TextAnchor.MiddleCenter, true, Color.white));
        GUI.Label(dn, "▼", UiKit.Label(64f * s, TextAnchor.MiddleCenter, true, Color.white));
        GUI.Label(new Rect(40f * s, H - 70f * s, W, 50f * s), "上下フリック / ▲▼ で高さを合わせてリングをくぐる(逃しても減るものはありません)", UiKit.Label(24f * s, TextAnchor.MiddleLeft, false, new Color(0.85f, 0.9f, 1f)));

        if (!string.IsNullOrEmpty(banner) && Time.unscaledTime < bannerUntil)
            GUI.Label(new Rect(0, H * 0.32f, W, 80f * s), banner, UiKit.Label(46f * s, TextAnchor.MiddleCenter, true, bannerColor));

        // このオーバーレイの下(止まっているゲーム画面/HUD)へ押下を通さない
        var e = Event.current;
        if (e.type == EventType.MouseDown || e.type == EventType.MouseUp) e.Use();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }
}
