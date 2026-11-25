using System.Collections.Generic;
using UnityEngine;

// マルチの受信値の検証(2026-11-25 セキュリティ監査)。HOST が正式な値を決める方針はそのまま、受け取る値の範囲・有限・回数だけを見る。
// 不正な値は捨てる(送った人は切断しない = 正常な通信の揺れで誤って切らない)。捨てた数は Rejects(確認用)。ログは種類ごとに数秒に1行
public static class NetSecurity
{
    public const int MaxHp = (99 + 20) * CombatScale.HpPerHeart; // ハート99個(CardRules.MaxHeartsCap)+余裕
    public const float MaxRunSpeedMps = 90f;     // 324 km/h(自然上限100km/h・補助/設定の上乗せより十分上)
    public const double DistanceSlack = 60.0;    // 距離の増え方の余裕(m)
    public const double EscapeSlack = 200.0;     // 脱出の申告: HOST が知っている距離 + これ以上は認めない
    public const int MaxHitsPerSecond = 90;      // 1人の攻撃の要求(多段/属性の追加攻撃を含めた実測より十分上)
    public const float MaxVacuum = 25f, MaxVacuumSeconds = 2f; // 吸い寄せ(竜巻)の速さ/秒数
    public const float MaxMileBase = 50000f, MaxExpMul = 20f;  // HOST から届く撃破の報酬の元(ボスは基本×1.5×強さ。異常値と NaN だけ止める)
    public const int MaxBounty = 3000;                          // WANTED の賞金(Lv255 で 2570)
    public const float MaxVersusBonus = 2f;                     // VERSUS の順位/REAPER のボーナス(実際の上限 0.45)

    public static int Rejects { get; private set; }
    static readonly Dictionary<string, float> lastLog = new Dictionary<string, float>();
    public static void Reject(string what, string why)
    {
        Rejects++;
        float now = Time.realtimeSinceStartup;
        if (lastLog.TryGetValue(what, out float t) && now - t < 3f) return;
        lastLog[what] = now;
        Debug.LogWarning($"[NET][SEC] rejected {what}: {why} (total {Rejects})");
    }

    public static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    public static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    public static float ClampFinite(float v, float min, float max) => Finite(v) ? Mathf.Clamp(v, min, max) : min;

    static readonly HashSet<string> stages = new HashSet<string> { "wasteland_road", "natural_cave", "sky_corridor", "last_corridor", "test_wasteland", "test_wasteland_pit" };
    public static bool KnownStage(string id) => !string.IsNullOrEmpty(id) && stages.Contains(id);

    // 送った人ごとの1秒の回数
    struct Window { public float start; public int count; }
    static readonly Dictionary<(string, ulong), Window> windows = new Dictionary<(string, ulong), Window>();
    public static bool RateOk(string kind, ulong sender, int perSecond)
    {
        float now = Time.realtimeSinceStartup;
        var key = (kind, sender);
        windows.TryGetValue(key, out Window w);
        if (now - w.start >= 1f) { w.start = now; w.count = 0; }
        w.count++;
        windows[key] = w;
        if (w.count > perSecond) { if (w.count == perSecond + 1) Reject(kind, $"rate limit {perSecond}/s from client {sender}"); else Rejects++; return false; }
        return true;
    }
}
