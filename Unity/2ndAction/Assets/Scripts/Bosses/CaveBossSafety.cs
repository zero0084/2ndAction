using System.Collections.Generic;
using UnityEngine;

// 自然洞窟ボス強化(2026-10-04) - 「洞窟そのものが攻撃してくる」攻撃を、回避不能にしないための調停役。
//  ・床から(跳ぶ)と天井から(地面にいる)の攻撃が、プレイヤーの位置で同時に有効にならないよう、
//    後から来た方の発動を少し遅らせる(予告が長くなるだけ。予告なしの攻撃は作らない)。
//  ・天井からの攻撃の最中に穴を跳ばなければならない状況を作らない(穴が近い時は天井攻撃を出さない)。
//  ・洞窟ボスと戦っている間は新しい穴を作らない(シングルのみ。マルチは地形を両端末で同じに作るため触らない)/必殺技の間は新しい雑魚を出さない。
//  ・洞窟ボスが生きている間は、天井を通常の高さ・天井の針なしに保つ(シングルのみ。CaveStageのボス区間を追従させる)。
// 判定の重なりはCaveHazardが毎フレーム数え、自動テスト(-qaCaveBoss)で0件を確かめる。
public static class CaveBossSafety
{
    public const string Stage = "natural_cave";
    public const float Margin = 0.45f;      // 床と天井の有効時間の最小の間隔(秒)
    public const float ColumnHalf = 0.9f;   // プレイヤーの「列」とみなす半幅

    struct Win { public bool ceiling; public float start, end; }
    static readonly List<Win> wins = new List<Win>();

    public static int Delayed, PitSkips;   // 統計(自動テスト用)

    public static bool CaveStageActive => GameManager.Instance != null && GameManager.Instance.ActiveRunStageId == Stage;
    // 天空回廊(2026-10-05)も同じ調停を使う(落下死のある足場。必殺技の間だけ新しい穴を作らない)
    public const string SkyStage = "sky_corridor";
    public static bool SkyStageActive => GameManager.Instance != null && GameManager.Instance.ActiveRunStageId == SkyStage;
    static bool Multi => NetRunLauncher.IsMultiplayerRun;

    // 洞窟ボスと戦っている間(ラン再開後も含む)と必殺技の間は、新しい穴/坂を作らない(シングル)。
    // 天井の攻撃(地面にいる)と穴(跳ぶ)が重なる詰みを地形の側でも作らない。
    public static bool ForceFlatTerrain => !Multi && ((CaveStageActive && (BossBattle.UltimateActive || CaveBossFighting)) || (SkyStageActive && BossBattle.UltimateActive));
    static bool CaveBossFighting => BossManager.Instance != null && BossManager.Instance.IsBossPhase && BossManager.Instance.AliveWildCount > 0;
    public static bool HoldZako => (CaveStageActive || SkyStageActive) && BossBattle.UltimateActive && !NetCombat.Replica;

    // プレイヤーの列を覆う攻撃の有効時間を予約する。戻り値=発動を遅らせる秒数(0なら予定どおり)。
    // activeIn: 今から何秒後に有効になるか / activeDur: 有効な長さ
    // 流れてくる物(落石/毒だまり/柱/横切る体)は、プレイヤーの踏み込み攻撃などで届く時刻がずれるので前後に余裕を持たせる
    public static float ReserveDrifting(bool ceiling, float activeIn, float activeDur)
    {
        const float pad = 0.4f;
        float d = Reserve(ceiling, Mathf.Max(0f, activeIn - pad), activeDur + pad * 2f);
        return d;
    }

    public static float Reserve(bool ceiling, float activeIn, float activeDur)
    {
        float now = Time.time;
        for (int i = wins.Count - 1; i >= 0; i--) if (wins[i].end < now - 1f) wins.RemoveAt(i);
        float delay = 0f;
        for (int pass = 0; pass < 4; pass++)
        {
            float s = now + activeIn + delay, e = s + activeDur;
            bool moved = false;
            foreach (var w in wins)
            {
                if (w.ceiling == ceiling) continue;
                if (s < w.end + Margin && e > w.start - Margin) { delay = w.end + Margin - (now + activeIn); moved = true; }
            }
            if (!moved) break;
        }
        if (delay > 0.001f) Delayed++;
        wins.Add(new Win { ceiling = ceiling, start = now + activeIn + delay, end = now + activeIn + delay + activeDur });
        return Mathf.Max(0f, delay);
    }

    public static void ResetAll() { wins.Clear(); }

    // from〜to秒後(天井の攻撃が有効な間)にプレイヤーが通る範囲に穴があるか(それ以前の穴は今のうちに跳べる)
    public static bool PitDuring(float from, float to)
    {
        var tm = TerrainManager.Instance; var pc = PlayerController.Instance;
        if (tm == null || pc == null) return false;
        float v = PlayerController.RunFrameSpeed, px = pc.transform.position.x;
        float x0 = px + v * from - 2f, x1 = px + v * to + 2.5f;
        for (float x = x0; x <= x1; x += 1.2f) if (tm.IsNearPit(x, 0.8f)) return true;
        return false;
    }

    // プレイヤーの前方(今からseconds秒の間に通る範囲)に穴があるか
    public static bool PitAhead(float seconds)
    {
        var tm = TerrainManager.Instance; var pc = PlayerController.Instance;
        if (tm == null || pc == null) return false;
        float x0 = pc.transform.position.x - 1.5f;
        float x1 = pc.transform.position.x + Mathf.Max(4f, PlayerController.RunFrameSpeed * seconds + 3f);
        for (float x = x0; x <= x1; x += 1.5f) if (tm.IsNearPit(x, 0.8f)) return true;
        return false;
    }
}
