using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Multiplayer.Tools.NetworkSimulator.Runtime;
#endif

// 開発用: 通信に人工の遅延を足す(2026-10-07、ONLINE の遅延テスト)。Multiplayer Tools の Network Simulator を NetworkManager に付ける。
//  起動引数 -netLatency <ms> [-netJitter <ms>] [-netLoss <%>]。この端末が送る通信を遅らせる(往復はおよそ +ms)。
//  Network Simulator は開発版(DEBUG)と Editor でだけ働く(製品版では何もしない)。接続の開始前に付ける必要がある。
public static class NetLatencySim
{
    public static int DelayMs { get; private set; } = -1;
    public static int JitterMs { get; private set; }
    public static int LossPercent { get; private set; }
    public static bool Enabled => DelayMs > 0 || LossPercent > 0;

    static void ReadArgs()
    {
        if (DelayMs >= 0) return;
        DelayMs = 0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
        {
            if (a[i] == "-netLatency" && int.TryParse(a[i + 1], out int d)) DelayMs = Mathf.Max(0, d);
            if (a[i] == "-netJitter" && int.TryParse(a[i + 1], out int j)) JitterMs = Mathf.Max(0, j);
            if (a[i] == "-netLoss" && int.TryParse(a[i + 1], out int l)) LossPercent = Mathf.Clamp(l, 0, 100);
        }
#endif
    }

    public static void Set(int delayMs, int jitterMs = 0, int lossPercent = 0) { DelayMs = delayMs; JitterMs = jitterMs; LossPercent = lossPercent; }

    public static void Apply(GameObject networkManagerObject)
    {
        ReadArgs();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (networkManagerObject == null) return;
        var sim = networkManagerObject.GetComponent<NetworkSimulator>();
        if (!Enabled) { if (sim != null) sim.ConnectionPreset = NetworkSimulatorPreset.Create("none"); return; }
        if (sim == null) sim = networkManagerObject.AddComponent<NetworkSimulator>();
        sim.ConnectionPreset = NetworkSimulatorPreset.Create($"lag{DelayMs}", "dev latency test", DelayMs, JitterMs, 0, LossPercent);
        Debug.Log($"[NET] Latency simulation delay={DelayMs}ms jitter={JitterMs}ms loss={LossPercent}%");
#endif
    }
}
