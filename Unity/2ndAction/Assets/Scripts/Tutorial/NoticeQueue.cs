using System.Collections.Generic;
using UnityEngine;

// ホームで出すお知らせの順番待ち(2026-10-07)。解放の通知などが同時に起きても重ねず、1つずつ「OK」で確認する。
// ラン中に起きた物はホームへ戻ってから出す(進行中のランを遮らない)。各お知らせは表示済みの印を呼び出し側が持つ。
public class NoticeQueue : MonoBehaviour
{
    public struct Notice { public string title, body, id; public System.Action onShown; public bool special; }
    public static NoticeQueue Instance { get; private set; }
    static readonly List<Notice> queue = new List<Notice>();
    static readonly HashSet<string> queuedIds = new HashSet<string>();
    Notice? current;
    float idle;
    public static bool Open => Instance != null && Instance.current.HasValue;
    public static bool QaAutoAcknowledge; // 自動テスト: お知らせを確かめないテストでは出たらすぐ閉じる
    public static int Pending => queue.Count;
    public static string CurrentId => Instance != null && Instance.current.HasValue ? Instance.current.Value.id : "";
    public static int Shown { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[NoticeQueue]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<NoticeQueue>();
    }

    // id が同じ物は1回だけ並べる。onShown は「OK」を押した時(表示済みの印を保存する)
    public static void Enqueue(string id, string title, string body, System.Action onShown, bool special = false)
    {
        if (string.IsNullOrEmpty(id) || queuedIds.Contains(id)) return;
        if (Instance != null && Instance.current.HasValue && Instance.current.Value.id == id) return;
        queuedIds.Add(id);
        queue.Add(new Notice { id = id, title = title, body = body, onShown = onShown, special = special });
    }

    // ラン中の小さな帯(止めない・操作を妨げない)。同時に複数あれば順番に3秒ずつ。詳しいお知らせはホームで
    static readonly Queue<string> toasts = new Queue<string>();
    string toastNow; float toastT;
    public static int ToastsShown { get; private set; }
    public static void Toast(string text) { if (!string.IsNullOrEmpty(text)) toasts.Enqueue(text); }

    // データ(通常/テスト用)を切り替えた時: 前のデータのお知らせを持ち越さない
    public static void ClearAll() { queue.Clear(); queuedIds.Clear(); toasts.Clear(); if (Instance != null) { Instance.current = null; Instance.toastNow = null; } }

    public static void DebugAnswer()
    {
        if (Instance == null || !Instance.current.HasValue) return;
        Instance.Acknowledge();
    }

    void Acknowledge()
    {
        var n = current.Value;
        current = null;
        queuedIds.Remove(n.id);
        try { n.onShown?.Invoke(); } catch (System.Exception e) { Debug.LogWarning("[Notice] " + e.Message); }
        UiInputGate.LatchUntilRelease();
        idle = 0f;
        Debug.Log($"[Notice] OK {n.id}");
    }

    void Update()
    {
        if (toastNow != null) { toastT -= Time.unscaledDeltaTime; if (toastT <= 0f) toastNow = null; }
        if (toastNow == null && toasts.Count > 0) { toastNow = toasts.Dequeue(); toastT = 3f; ToastsShown++; }
        if (current.HasValue || queue.Count == 0) { idle = 0f; return; }
        var gm = GameManager.Instance;
        bool homeCalm = gm != null && !gm.HasStarted && !gm.IsOverlayOpen && !gm.HomeModalOpen && !NetSession.IsActive
            && !SettingsPanel.IsVisible && !UiInputGate.ModalOpen && !TutorialLauncher.Covering && !ArenaLauncher.Covering && !TutorialMode.Active
            && !(ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning);
        idle = homeCalm ? idle + Time.unscaledDeltaTime : 0f;
        if (idle < 0.7f) return;
        current = queue[0];
        queue.RemoveAt(0);
        Shown++;
        if (QaAutoAcknowledge) { Debug.Log($"[Notice] auto-acknowledged for QA: {current.Value.id}"); Acknowledge(); return; }
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(current.Value.special ? SeId.MaxLevel : SeId.UiOpen);
        Debug.Log($"[Notice] show {current.Value.id}");
    }

    GUIStyle titleSt, bodySt;
    void OnGUI()
    {
        if (toastNow != null)
        {
            float ts = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 0.8f, 2.6f);
            float a = Mathf.Clamp01(toastT / 0.4f) * Mathf.Clamp01((3f - toastT) / 0.25f);
            var r = new Rect(Screen.width * 0.5f - 260 * ts, 96 * ts, 520 * ts, 44 * ts);
            Color keep = GUI.color; GUI.color = new Color(1f, 1f, 1f, a);
            UiBackdrop.Draw(r, 0.75f);
            GUI.Label(r, toastNow, UiKit.Label(20 * ts, TextAnchor.MiddleCenter, true, new Color(1f, 0.88f, 0.5f)));
            GUI.color = keep;
        }
        if (!current.HasValue) return;
        var n = current.Value;
        GUI.depth = -1850;
        float s = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 0.8f, 2.6f);
        if (titleSt == null)
        {
            titleSt = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
            bodySt = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
        }
        titleSt.fontSize = Mathf.RoundToInt(30 * s); titleSt.normal.textColor = n.special ? new Color(1f, 0.92f, 0.7f) : new Color(1f, 0.86f, 0.45f);
        bodySt.fontSize = Mathf.RoundToInt(22 * s); bodySt.normal.textColor = Color.white;
        UiKit.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, n.special ? 0.75f : 0.55f));
        float pw = Mathf.Min(Screen.width - 32 * s, 820 * s), ph = Mathf.Min(Screen.height * 0.7f, 330 * s);
        var p = new Rect((Screen.width - pw) * 0.5f, (Screen.height - ph) * 0.5f, pw, ph);
        if (n.special)
        {
            float glow = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.2f);
            UiKit.Fill(new Rect(p.x - 10 * s, p.y - 10 * s, p.width + 20 * s, p.height + 20 * s), new Color(1f, 0.85f, 0.5f, 0.15f + 0.15f * glow));
        }
        OrnateUi.DrawPanel(p, 0.95f);
        GUI.Label(new Rect(p.x + 20 * s, p.y + 14 * s, p.width - 40 * s, 46 * s), n.title, titleSt);
        GUI.Label(new Rect(p.x + 28 * s, p.y + 62 * s, p.width - 56 * s, p.height - 140 * s), n.body, bodySt);
        if (queue.Count > 0) GUI.Label(new Rect(p.x, p.yMax - 96 * s, p.width, 22 * s), $"({queue.Count + 1}件のお知らせ)", UiKit.Label(15 * s, TextAnchor.MiddleCenter, false, new Color(1f, 1f, 1f, 0.6f)));
        int pl = PadNav.BeginLayer(70);
        if (UiKit.Button(new Rect(p.center.x - 130 * s, p.yMax - 70 * s, 260 * s, 54 * s), "OK", 24 * s, true)) Acknowledge();
        PadNav.EndLayer(pl);
        if (Event.current != null && (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp)) Event.current.Use();
    }
}

// デッキ枠12枚の案内(2026-10-07)。既存のデッキがあって12枚未満の時だけ1回。上限を超えて外した時はその旨も
public static class DeckCapacityNotice
{
    public const string Key = "DeckCapacity12Notice";
    public static void Check(GameManager gm, bool hadSavedDeck)
    {
        if (gm == null || DebugRun.WritesBlocked) return;
        if (gm.DeckOverflowDropped > 0)
        {
            NoticeQueue.Enqueue("deck_overflow", "デッキを整理しました",
                $"デッキの上限({GameManager.DeckCapacity}枚)を超えていた {gm.DeckOverflowDropped} 枚をデッキから外しました。\nカードは所持一覧に残っています。デッキ編集で入れ替えてください。", null);
            SaveStoreSaveDeckNow(gm);
        }
        if (!hadSavedDeck || SaveStore.GetInt(Key, 0) != 0) return;
        if (gm.DeckCards.Count >= GameManager.DeckCapacity) { SaveStore.SetInt(Key, 1); return; }
        NoticeQueue.Enqueue("deck12", "デッキが12枚になりました",
            $"デッキに入れられるカードが {GameManager.DeckCapacity} 枚になりました(今 {gm.DeckCards.Count} 枚)。\nデッキ編集で追加できます。", () => { SaveStore.SetInt(Key, 1); SaveStore.Save(); });
    }
    static void SaveStoreSaveDeckNow(GameManager gm) { gm.SaveDeckNow(); }
}
