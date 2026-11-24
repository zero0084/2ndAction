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

// 協力(CO-OP)の画面の距離 = チーム走行距離(2026-11-24、マスター依頼: ボスの後で端末ごとに距離の表示が違った)。
//  決め方: HOST が決める WorldProgress.FrontHigh(活動中のプレイヤーの最前の、HOST の物差しでの距離。このランで下がらない)。
//   ボスの関門・距離のイベントと同じ値。各プレイヤーの位置は動かさない(表示だけ)。距離の MILE / EXP / 記録は今までどおり各自の距離。
//  HOST: 毎フレームの FrontHigh。JOIN: 表(約1秒ごと)で届く値を、直前2回の届いた値の速さで先へ伸ばして滑らかにする(先へ伸ばすのは最大 1.25 秒分)。
//  どちらも表示は下げない(届いた値が表示より小さい時は、追いつくまで表示を止める = 巻き戻らない)。
//  対戦(VERSUS)とシングルは使わない(各自の距離のまま)
public static class TeamDistance
{
    static GameManager owner;
    static double shown, lastRecv, prevRecv, speed;
    static float lastRecvAt = -1f, prevRecvAt = -1f;
    public static int Received { get; private set; } // 確認用

    public static bool Active => MpRun.On && NetSession.IsActive && NetMatch.Active && NetMatch.Mode == MultiplayerGameMode.Coop;

    static void EnsureRun()
    {
        var gm = GameManager.Instance;
        if (gm == owner) return;
        owner = gm; shown = 0; lastRecv = prevRecv = 0; speed = 0; lastRecvAt = prevRecvAt = -1f; Received = 0;
    }

    // JOIN: HOST の表から(WorldProgress.SetFromHost と同じ値)
    public static void NoteFromHost(double v)
    {
        EnsureRun();
        float now = Time.realtimeSinceStartup;
        if (lastRecvAt >= 0f && now - lastRecvAt >= 0.2f) speed = System.Math.Max(0.0, (v - lastRecv) / (now - lastRecvAt)); // 近すぎる2回からは速さを出さない
        prevRecv = lastRecv; prevRecvAt = lastRecvAt;
        lastRecv = System.Math.Max(lastRecv, v); lastRecvAt = now; Received++;
    }

    // 画面に出すチーム走行距離(協力のマルチ中だけ意味がある)
    public static double Display
    {
        get
        {
            EnsureRun();
            double target;
            if (!NetCombat.Replica) target = WorldProgress.FrontHigh; // HOST
            else if (lastRecvAt < 0f) target = 0; // まだ届いていない(出発の直後だけ)
            else target = lastRecv + speed * Mathf.Min(Time.realtimeSinceStartup - lastRecvAt, 1.25f);
            if (target > shown) shown = target;
            return shown;
        }
    }
}

// マルチの止まり方の切り替え(2026-11-24)。On = 新しい決まり(ボスを倒したら足止めを解く・HOST だけ時間を止めない・距離を HOST だけ止めない・
//  一時停止のメニューは本人だけ止まる・協力はチーム走行距離)。Legacy は開発版の比較だけ(-netLegacyStops 1 = 変更前の動き)。製品版は常に新しい決まり
public static class MpRun
{
    public static bool Legacy { get; private set; }
    public static bool On => NetRunLauncher.IsMultiplayerRun && !Legacy;
    public static bool PauseOnlyMe => NetMatch.Active && !Legacy;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-netLegacyStops" && a[i + 1] == "1") { Legacy = true; Debug.Log("[MpRun] legacy stop rules (dev comparison)"); }
    }
#endif
}
