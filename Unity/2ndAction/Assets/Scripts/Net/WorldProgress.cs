using System.Collections.Generic;
using UnityEngine;

// マルチ 安定化(2026-10-11): 共有世界の進行距離と、距離のイベントを一意に起こすための記録。
//
//  FrontHigh … マルチの HOST: 「自分の距離」と「活動中のプレイヤーの最前(WorldFrontDistance、HOST の物差し)」の大きい方の、
//               このランでの最高値。先頭が脱落/DOWN/切断して最前が後ろへ下がっても、下げない(イベントを巻き戻さない)。
//               JOIN: HOST から表(NetMatch)で受け取った値。シングル: 自分の距離そのまま(従来どおり)。
//  TryTrigger(id) … HOST(またはシングル)で、そのイベントを初めて起こす時だけ true(先頭が入れ替わっても二重に起こさない)。
//               一度起きたイベントは、ランが終わるまで「起きた」のまま(進行方向のみ)。
// LOCAL / ONLINE で同じ。
public static class WorldProgress
{
    static double frontHigh, fromHost;
    static GameManager owner;
    static readonly HashSet<string> triggered = new HashSet<string>();
    public static IReadOnlyCollection<string> Triggered => triggered;

    static void EnsureRun()
    {
        var gm = GameManager.Instance;
        if (gm == owner) return;
        owner = gm; frontHigh = 0; fromHost = 0; triggered.Clear(); // 新しいラン(シーンの読み直し)
    }

    public static double FrontHigh
    {
        get
        {
            EnsureRun();
            var gm = GameManager.Instance;
            if (gm == null) return 0;
            if (!NetRunLauncher.IsMultiplayerRun || !NetSession.IsActive) return gm.MaxDistanceExact; // シングル: 従来どおり自分の距離
            if (NetCombat.Replica) return fromHost > 0 ? fromHost : gm.MaxDistanceExact; // JOIN: HOST の値(まだ届いていなければ自分の距離)
            double d = gm.MaxDistanceExact;
            if (WorldRange.HasAlive) d = System.Math.Max(d, WorldRange.WorldFrontDistance);
            if (d > frontHigh) frontHigh = d;
            return frontHigh;
        }
    }

    // JOIN: HOST の表から
    public static void SetFromHost(double v) { EnsureRun(); if (v > fromHost) fromHost = v; }
    public static void NoteFromHost(string id) { EnsureRun(); if (triggered.Add(id)) Debug.Log($"[WorldEvent] {id} (from HOST)"); }

    // HOST/シングル: このイベントを今起こしてよいか(初めての時だけ true)
    public static bool TryTrigger(string id)
    {
        EnsureRun();
        if (NetCombat.Replica) return false; // JOIN は世界のイベントを決めない
        if (!triggered.Add(id)) return false;
        Debug.Log($"[WorldEvent] {id} triggered at front {FrontHigh:F0}m (front player P{WorldRange.WorldFrontPlayer})");
        return true;
    }

    public static bool IsTriggered(string id) { EnsureRun(); return triggered.Contains(id); }
}
