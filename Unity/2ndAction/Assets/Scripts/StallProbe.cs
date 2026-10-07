using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 攻撃が当たった時の「数秒止まる」の切り分け用の軽い計測(2026-10-07)。
//  ・重い処理による止まり: 1フレームの実時間が 0.25 秒を超えた
//  ・ゲーム内時間の止まり: timeScale が 0 のまま 0.4 秒を超えた(HitStop だけが理由の時。ポーズ/カード選択は除く)
//  のどちらかが起きたら、その直前の命中(敵/攻撃の種類/撃破か/同じフレームの命中数)と、止めている理由をログへ1行出す。
//  通常時は命中の記録を数件持つだけ(ログは出さない)。
public class StallProbe : MonoBehaviour
{
    struct Hit { public float t; public int frame; public string enemy; public string kind; public bool killed; }
    static readonly Queue<Hit> hits = new Queue<Hit>();
    static int hitsThisFrame, killsThisFrame, frameOfCount = -1;
    public static int Stalls { get; private set; }      // 確認用: 計測した止まりの回数
    public static float LongestStopSeconds { get; private set; }
    public static string LastStall { get; private set; } = "";
    public static void ResetLongest() { LongestStopSeconds = 0f; }
    static float quietUntil;
    void OnEnable() { UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => quietUntil = Time.realtimeSinceStartup + 1.5f; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<StallProbe>() != null) return;
        var go = new GameObject("[StallProbe]");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideInHierarchy;
        go.AddComponent<StallProbe>();
    }

    public static void NoteHit(string enemy, string kind, bool killed)
    {
        if (frameOfCount != Time.frameCount) { frameOfCount = Time.frameCount; hitsThisFrame = 0; killsThisFrame = 0; }
        hitsThisFrame++; if (killed) killsThisFrame++;
        hits.Enqueue(new Hit { t = Time.realtimeSinceStartup, frame = Time.frameCount, enemy = enemy, kind = kind, killed = killed });
        while (hits.Count > 8) hits.Dequeue();
    }

    public static string LastHitsText()
    {
        var sb = new StringBuilder("hits[");
        foreach (var h in hits) sb.Append($"{h.enemy}/{h.kind}{(h.killed ? "/KILL" : "")}@{Time.realtimeSinceStartup - h.t:F2}s ");
        sb.Append($"] sameFrame {hitsThisFrame} kills {killsThisFrame}");
        return sb.ToString();
    }

    float stopSince = -1f; bool stopLogged;
    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (dt > 0.25f && Time.frameCount > 30 && Time.realtimeSinceStartup > quietUntil) // シーンの読み直し直後は数えない
            Report($"heavy frame {dt:F2}s");
        // ゲーム内時間の停止(HitStop だけが理由の時)
        bool hitStopOnly = Time.timeScale <= 0f && HitStop.IsActive && TimeControl.ActiveReasonCount <= HitStop.ActiveCount;
        if (hitStopOnly)
        {
            if (stopSince < 0f) { stopSince = Time.realtimeSinceStartup; stopLogged = false; }
            float s = Time.realtimeSinceStartup - stopSince;
            LongestStopSeconds = Mathf.Max(LongestStopSeconds, s);
            if (!stopLogged && s > 0.4f) { stopLogged = true; Report($"time stopped by HitStop for {s:F2}s"); }
        }
        else stopSince = -1f;
    }

    static void Report(string what)
    {
        Stalls++;
        LastStall = $"{what} | hitstops {HitStop.ActiveCount} reasons {TimeControl.ActiveReasonCount} [{TimeControl.DescribeActiveReasons()}] | {LastHitsText()}";
        Debug.LogWarning("[Stall] " + LastStall);
        FreezeDiagnostics.LogEvent("[Stall] " + LastStall);
    }
}
