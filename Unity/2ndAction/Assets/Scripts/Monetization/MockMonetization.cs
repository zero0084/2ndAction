#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using UnityEngine;

// 開発版だけの「模擬」の広告と購入(2026-10-10、依頼I)。製品版には入らない(#if)。
// 画面いっぱいの「TEST AD(模擬)」を出し、DEBUG →「広告/課金(模擬)…」で 在庫なし/読み込み失敗/表示失敗/途中で閉じる/報酬の二重通知/
// 閉じた後に遅れて届く報酬/アプリの中断 を選べる。購入は 成功/支払い待ち(後で完了)/キャンセル/失敗/オフライン/返金 を選べる。
public class MockAdService : MonoBehaviour, IAdService
{
    public enum Behaviour { Normal, NoFill, LoadFail, ShowFail, CloseEarly, DuplicateReward, LateReward }
    public static Behaviour Mode = Behaviour.Normal;
    public static float AdSeconds = 3f;
    public static bool AutoComplete; // 自動テスト: 時間が来たら「閉じる」を押したのと同じ
    static MockAdService inst;
    public static MockAdService Instance
    {
        get
        {
            if (inst == null) { var go = new GameObject("MockAdService"); DontDestroyOnLoad(go); inst = go.AddComponent<MockAdService>(); }
            return inst;
        }
    }
    public string Name => "mock";
    public void Initialize() { }
    readonly bool[] ready = new bool[2];
    public bool IsReady(AdKind k) => ready[(int)k] && Mode != Behaviour.NoFill && Mode != Behaviour.LoadFail;
    public void Load(AdKind k)
    {
        if (ready[(int)k]) return;
        if (Mode == Behaviour.NoFill || Mode == Behaviour.LoadFail) return; // 在庫なし/失敗: 用意できない
        ready[(int)k] = true;
    }
    public bool PrivacyOptionsRequired => false;
    public void ShowPrivacyOptions() { }

    bool showing; AdKind showingKind; float t; Action onReward; Action<AdOutcome, string> onFinished; bool rewardSent;
    public static bool ShowingNow => inst != null && inst.showing;
    public void Show(AdKind k, Action reward, Action<AdOutcome, string> finished)
    {
        if (Mode == Behaviour.ShowFail) { ready[(int)k] = false; finished(AdOutcome.Failed, "mock show failed"); return; }
        if (Mode == Behaviour.NoFill || Mode == Behaviour.LoadFail) ready[(int)k] = false; // 用意していた分も在庫切れ
        if (!ready[(int)k]) { finished(AdOutcome.NotReady, "mock not ready"); return; }
        ready[(int)k] = false;
        showing = true; showingKind = k; t = 0f; onReward = reward; onFinished = finished; rewardSent = false;
    }

    void Update()
    {
        if (!showing) return;
        t += Time.unscaledDeltaTime;
        if (Mode == Behaviour.CloseEarly && t > AdSeconds * 0.4f) { Close(false); return; }
        if (AutoComplete && t >= AdSeconds) Close(true);
    }

    void Close(bool completed)
    {
        if (!showing) return;
        showing = false;
        bool rewardKind = showingKind == AdKind.Rewarded;
        var fin = onFinished; var rw = onReward;
        if (completed && rewardKind && Mode != Behaviour.LateReward) { rw?.Invoke(); if (Mode == Behaviour.DuplicateReward) rw?.Invoke(); }
        fin?.Invoke(AdOutcome.Shown, completed ? "" : "closed early");
        if (completed && rewardKind && Mode == Behaviour.LateReward) StartCoroutine(Late(rw));
    }
    IEnumerator Late(Action rw) { yield return new WaitForSecondsRealtime(1.0f); rw?.Invoke(); rw?.Invoke(); }

    void OnGUI()
    {
        if (!showing) return;
        GUI.depth = -3000;
        UiKit.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.05f, 0.05f, 0.08f, 1f));
        var st = new GUIStyle(UiKit.Label(Mathf.Clamp(Screen.height * 0.05f, 24f, 60f), TextAnchor.MiddleCenter, true, Color.white));
        GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, Screen.height * 0.12f), showingKind == AdKind.Rewarded ? "TEST AD (模擬) 報酬広告" : "TEST AD (模擬) インタースティシャル", st);
        float left = Mathf.Max(0f, AdSeconds - t);
        var st2 = new GUIStyle(UiKit.Label(Mathf.Clamp(Screen.height * 0.03f, 16f, 34f), TextAnchor.MiddleCenter, false, new Color(0.8f, 0.85f, 1f)));
        GUI.Label(new Rect(0, Screen.height * 0.45f, Screen.width, 60f), left > 0f ? $"{left:F1} 秒" : "報酬の条件を満たしました", st2);
        Rect close = new Rect(Screen.width - 180f, 30f, 150f, 56f);
        if (GUI.Button(close, left > 0f && showingKind == AdKind.Rewarded ? "途中で閉じる" : "閉じる")) Close(left <= 0f || showingKind == AdKind.Interstitial);
        // 模擬: 広告の間にアプリが中断した(ホーム画面へ出た)のと同じ扱い
        Rect pause = new Rect(30f, 30f, 260f, 56f);
        if (GUI.Button(pause, "アプリ中断の模擬")) { foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)) { mb.SendMessage("OnApplicationPause", true, SendMessageOptions.DontRequireReceiver); mb.SendMessage("OnApplicationPause", false, SendMessageOptions.DontRequireReceiver); } }
    }
}

public class MockStoreService : IStoreService
{
    public enum Behaviour { Success, PendingThenComplete, Cancel, Fail, Offline }
    public static Behaviour Mode = Behaviour.Success;
    public static bool Refunded; // 返金/取消の模擬(次の確認で「持っていない」)
    static MockStoreService inst;
    public static MockStoreService Instance => inst ?? (inst = new MockStoreService());
    public string Name => "mock";
    public bool Ready { get; private set; }
    public event Action<PurchaseResult> LatePurchase;
    public const string OwnedKey = "Dev.MockStoreOwnedV1"; // 模擬の「ストアのアカウント」(本物とは別)

    public void Initialize(Action<bool> done) { Ready = Mode != Behaviour.Offline; done(Ready); }
    public string LocalizedPrice(string id) => Ready ? "¥(模擬) 000" : null;

    public void Purchase(string id, Action<PurchaseResult> done)
    {
        switch (Mode)
        {
            case Behaviour.Offline: done(new PurchaseResult { status = PurchaseStatus.NotReady, productId = id, message = "offline (mock)" }); return;
            case Behaviour.Cancel: done(new PurchaseResult { status = PurchaseStatus.Cancelled, productId = id }); return;
            case Behaviour.Fail: done(new PurchaseResult { status = PurchaseStatus.Failed, productId = id, message = "mock failure" }); return;
            case Behaviour.PendingThenComplete:
                done(new PurchaseResult { status = PurchaseStatus.Pending, productId = id });
                MockAdService.Instance.StartCoroutine(CompleteLater(id));
                return;
        }
        if (SaveStore.GetInt(OwnedKey, 0) == 1 && !Refunded) { done(new PurchaseResult { status = PurchaseStatus.AlreadyOwned, productId = id, token = "mock-token" }); return; }
        SaveStore.SetInt(OwnedKey, 1); SaveStore.Save(); Refunded = false;
        done(new PurchaseResult { status = PurchaseStatus.Purchased, productId = id, token = "mock-token" });
    }

    IEnumerator CompleteLater(string id)
    {
        yield return new WaitForSecondsRealtime(4f); // 支払いが後で完了した
        SaveStore.SetInt(OwnedKey, 1); SaveStore.Save(); Refunded = false;
        LatePurchase?.Invoke(new PurchaseResult { status = PurchaseStatus.Purchased, productId = id, token = "mock-token" });
    }

    public void Restore(string id, Action<RestoreResult> done)
    {
        if (Mode == Behaviour.Offline) { done(new RestoreResult { ok = false, message = "offline (mock)" }); return; }
        bool owned = SaveStore.GetInt(OwnedKey, 0) == 1 && !Refunded;
        done(new RestoreResult { ok = true, owned = owned, token = owned ? "mock-token" : "" });
    }

    public static void DevClearAccount() { SaveStore.SetInt(OwnedKey, 0); SaveStore.Save(); Refunded = false; }
}
#endif
