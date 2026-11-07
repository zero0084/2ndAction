using System.Collections.Generic;
using UnityEngine;

// チャレンジ達成の短い通知(2026-11-07)。「CHALLENGE COMPLETE」+チャレンジ名+バッヂ+報酬。
//  ・ゲームを止めない(Time.timeScale には触らない)。入力も取らない(描くだけ)。時間は実時間(一時停止中でも消える)
//  ・同時に複数達成したら順番に1つずつ(1件 3秒)。出る瞬間に短い SE
//  ・場所: 画面の上の中央(ラン中は HUD の下。お知らせの帯 NoticeQueue.Toast と重ならないよう、その下の段)
public class ChallengeToast : MonoBehaviour
{
    public static ChallengeToast Instance { get; private set; }
    public const float Seconds = 3f;
    static readonly Queue<ChallengeDef> queue = new Queue<ChallengeDef>();
    ChallengeDef now; float t; bool shownInRun;
    public static int ShownCount { get; private set; }
    public static string NowId => Instance != null && Instance.now != null ? Instance.now.id : "";
    public static int Pending => queue.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[ChallengeToast]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<ChallengeToast>();
        // エステル(11-07): ラン中の通知がホームへ戻った後も薄く残って見えた → 画面が切り替わったら今の1件はすぐ消す(0.3秒で薄れる)。
        //  まだ出ていない分(queue)は捨てずに、新しい画面で順番に出す
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => { if (Instance != null && Instance.now != null) Instance.t = Mathf.Min(Instance.t, 0.3f); };
    }

    public static void Show(ChallengeDef d) { if (d != null) queue.Enqueue(d); }
    public static void ClearAll() { queue.Clear(); if (Instance != null) Instance.now = null; }

    void Update()
    {
        var g = GameManager.Instance;
        bool inRun = g != null && g.HasStarted;
        if (now != null && inRun != shownInRun) t = Mathf.Min(t, 0.3f); // ラン⇔ホームが切り替わったら今の1件はすぐ薄れて消える(シーンを読み直さない遷移の分)
        if (now != null) { t -= Time.unscaledDeltaTime; if (t <= 0f) now = null; }
        if (now == null && queue.Count > 0)
        {
            now = queue.Dequeue(); t = Seconds; ShownCount++; shownInRun = inRun;
            var am = AudioManager.Instance;
            if (am != null) am.PlaySe(SeId.MasteryUp); // 短いチャイム
        }
    }

    void OnGUI()
    {
        if (now == null) return;
        GUI.depth = -2050; // 設定(-2000)より手前。ボタンは無いので入力は取らない
        float ts = UiScalePref.Menu(Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 0.8f, 2.6f));
        float a = Mathf.Clamp01(t / 0.3f) * Mathf.Clamp01((Seconds - t) / 0.2f);
        float slide = (1f - Mathf.Clamp01((Seconds - t) / 0.2f)) * -12f * ts;
        float w = Mathf.Min(560f * ts, Screen.width - 16f), h = 76f * ts;
        float y = 150f * ts;
        var gm = GameManager.Instance;
        if (gm != null && gm.HasStarted && !gm.IsGameOver && !TextFit.Legacy) y = Mathf.Max(y, GameManager.NoticeTopPx + 54f * ts); // HUD とお知らせの帯の下
        if (gm != null && gm.IsGameOver && gm.LastResultPanelRect.height > 0f)
        {
            Rect rp = gm.LastResultPanelRect; // 結果画面: 板の下(入らなければ上)
            y = rp.yMax + 8f + h <= Screen.height - 4f ? rp.yMax + 8f : Mathf.Max(4f, rp.y - h - 8f);
        }
        var r = new Rect(Screen.width * 0.5f - w * 0.5f, y + slide, w, h);
        Color keep = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, a);
        UiBackdrop.Draw(r, 0.86f);
        Color cc = ChallengeDefs.CategoryColor(now.cat);
        UiKit.Fill(new Rect(r.x, r.y, 5f * ts, r.height), new Color(cc.r, cc.g, cc.b, 0.95f));
        float icon = h - 12f * ts;
        BadgeArt.Draw(new Rect(r.x + 12f * ts, r.y + 6f * ts, icon, icon), now, true);
        float tx = r.x + 20f * ts + icon, tw = r.xMax - tx - 10f * ts;
        string reward = Loc.F(LocProfile.Reward, now.mile);
        var rs = UiKit.Label(16f * ts, TextAnchor.UpperRight, false, new Color(1f, 0.85f, 0.45f));
        float rw = Mathf.Min(tw * 0.42f, rs.CalcSize(new GUIContent(reward)).x + 4f);
        LocGUI.Label(new Rect(tx, r.y + 6f * ts, tw - rw - 6f * ts, 22f * ts), LocProfile.ChallengeComplete, UiKit.Label(15f * ts, TextAnchor.UpperLeft, true, new Color(1f, 0.86f, 0.4f)));
        GUI.Label(new Rect(r.xMax - 10f * ts - rw, r.y + 6f * ts, rw, 22f * ts), reward, rs);
        LocGUI.Label(new Rect(tx, r.y + 28f * ts, tw, 40f * ts), now.name, UiKit.Label(24f * ts, TextAnchor.MiddleLeft, true, Color.white)); // 名前は英字(全言語そのまま)。長ければ LocGUI が縮める
        GUI.color = keep;
    }
}
