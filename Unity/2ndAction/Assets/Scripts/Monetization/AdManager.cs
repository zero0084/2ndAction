using System;
using System.Collections.Generic;
using UnityEngine;

// 広告を出す共通の流れ(2026-10-10、依頼I)。
//  ・間隔: 前回の広告(強制/報酬とも)が終わってから 5分(実時間)。強制広告は起動から 5分経つまで出さない。時間はゲームの速さに関係ない実時間
//    (Time.realtimeSinceStartup と、再起動をまたぐための終了時刻の UTC の両方で見る)
//  ・広告の間: 音を止める(AudioListener.pause。設定の音量/消音は変えない)、画面の入力を止める(UiInputGate.AdShowing)。
//    終わったら戻し、押しっぱなしの指は離すまで無視(LatchUntilRelease)
//  ・報酬: SDK の正規の報酬通知だけで渡す(閉じただけでは渡さない)。1回の表示で報酬は1回だけ(通知が重なっても/遅れても)。
//    報酬を「受け取った」記録を保存してから渡す(AdRewards が鍵ごとに付与済みを持つ)ので、途中でアプリが落ちても次の起動で回収できる
//  ・SDK の通知は Unity の主スレッド以外から来ることがある → いったん貯めて Update で処理する
public class AdManager : MonoBehaviour
{
    static AdManager inst;
    public static void Ensure()
    {
        if (inst != null) return;
        var go = new GameObject("AdManager");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<AdManager>();
        go.AddComponent<MonetizationUi>();
    }

    public static bool Showing { get; private set; }
    public static float LastAdEndRealtime { get; private set; } = -1f;
    public const string LastAdEndUtcKey = "AdLastEndUtcV1";
    public static int ShowsStarted { get; private set; }
    public static int RewardsDelivered { get; private set; }
    public static int DuplicateRewardsIgnored { get; private set; }
    public static string LastNote { get; private set; } = "";

    // 主スレッドへ渡す箱
    static readonly List<Action> queue = new List<Action>();
    public static void Post(Action a) { lock (queue) queue.Add(a); }

    void Update()
    {
        Action[] run = null;
        lock (queue) { if (queue.Count > 0) { run = queue.ToArray(); queue.Clear(); } }
        if (run != null) foreach (var a in run) { try { a(); } catch (Exception e) { Debug.LogException(e); } }
        // 読み込み: 強制/報酬とも1本ずつ用意しておく(在庫が無くても待たせない)
        if (Monetization.Ads != null && !Showing && Time.frameCount % 120 == 0)
        {
            if (!Monetization.Ads.IsReady(AdKind.Interstitial) && !NoAdsPass.Owned) Monetization.Ads.Load(AdKind.Interstitial);
            if (!Monetization.Ads.IsReady(AdKind.Rewarded)) Monetization.Ads.Load(AdKind.Rewarded);
        }
    }

    // 復帰した時: 広告なしパスの状態を確かめる(繋がらなければ控えのまま)。広告の表示中の中断は SDK が続きを出す
    float lastResumeCheck = -999f;
    void OnApplicationPause(bool paused)
    {
        if (paused || Showing) return;
        if (Time.realtimeSinceStartup - lastResumeCheck < 60f) return;
        lastResumeCheck = Time.realtimeSinceStartup;
        if (Monetization.Store != null && Monetization.Store.Ready) NoAdsPass.Check("resume");
    }

    // ---- 間隔
    public static float SecondsSinceLastAd
    {
        get
        {
            float a = LastAdEndRealtime >= 0f ? Time.realtimeSinceStartup - LastAdEndRealtime : float.MaxValue;
            // 再起動をまたいだ分(端末の時計。戻した時は「間隔が足りない」側に倒す)
            string s = SaveStore.GetString(LastAdEndUtcKey, "");
            if (long.TryParse(s, out long ticks))
            {
                double d = (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds;
                if (d < 0) d = 0;
                a = Mathf.Min(a, (float)d);
            }
            return a;
        }
    }
    public static bool CooldownOver => SecondsSinceLastAd >= MonetizationConfig.AdCooldownSeconds;
    public static bool SessionWarm => Time.realtimeSinceStartup + DevSessionAgeBonus >= MonetizationConfig.SessionWarmupSeconds || DevIgnoreTiming;
    public static float DevSessionAgeBonus;
    public static void DevForgetLastAd() { LastAdEndRealtime = -1f; SaveStore.DeleteKey(LastAdEndUtcKey); } // 自動テスト: 起動からの時間を足して扱う(間隔だけを確かめる)
    public static bool DevIgnoreTiming; // 開発版の確認用(DEBUG の模擬のページ)

    // 報酬広告は自分で選んで見る物なので、強制広告の間隔(5分)では止めない(見た時刻は強制広告の間隔に数える)
    public static bool CanShowRewardedNow(out string why)
    {
        why = "";
        if (Monetization.Ads == null) { why = "広告を利用できません"; return false; }
        if (Showing) { why = "広告の表示中です"; return false; }
        if (!Monetization.Ads.IsReady(AdKind.Rewarded)) { Monetization.Ads.Load(AdKind.Rewarded); why = "広告を準備しています。少し待ってからお試しください"; return false; }
        return true;
    }

    // ---- 表示
    // onReward は正規の報酬通知の時だけ・1回だけ。onDone は必ず最後に1回(報酬の有無に関係なく)
    public static bool Show(AdKind kind, string tag, Action onReward, Action<bool> onDone)
    {
        Ensure();
        if (Monetization.Ads == null || Showing) { onDone?.Invoke(false); return false; }
        if (!Monetization.Ads.IsReady(kind)) { Monetization.Ads.Load(kind); LastNote = $"{tag}: not ready"; onDone?.Invoke(false); return false; }
        Showing = true; ShowsStarted++;
        bool rewarded = false, finished = false, prevPause = AudioListener.pause;
        AudioListener.pause = true;
        LastNote = $"{tag}: showing";
        Debug.Log($"[Ads] show {kind} ({tag})");
        void Finish(AdOutcome o, string msg)
        {
            if (finished) return;
            finished = true;
            Showing = false;
            AudioListener.pause = prevPause;
            if (o == AdOutcome.Shown)
            {
                LastAdEndRealtime = Time.realtimeSinceStartup;
                SaveStore.SetString(LastAdEndUtcKey, DateTime.UtcNow.Ticks.ToString()); SaveStore.Save();
            }
            UiInputGate.LatchUntilRelease();
            if (PlayerController.Instance != null) PlayerController.Instance.ClearPointerState();
            LastNote = $"{tag}: {o}{(string.IsNullOrEmpty(msg) ? "" : " " + msg)}{(rewarded ? " +reward" : "")}";
            Debug.Log($"[Ads] finished {kind} ({tag}) {o} {msg} rewarded={rewarded}");
            onDone?.Invoke(o == AdOutcome.Shown);
            Monetization.Ads.Load(kind); // 次の分
        }
        Monetization.Ads.Show(kind,
            () => Post(() =>
            {
                if (rewarded) { DuplicateRewardsIgnored++; Debug.Log($"[Ads] duplicate reward ignored ({tag})"); return; }
                rewarded = true; RewardsDelivered++;
                onReward?.Invoke(); // 呼ぶ側(AdRewards)が鍵で付与済みを記録する
            }),
            (o, msg) => Post(() => Finish(o, msg)));
        return true;
    }
}
