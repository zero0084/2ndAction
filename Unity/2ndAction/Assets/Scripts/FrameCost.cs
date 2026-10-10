using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

// 重いフレームの切り分け用(2026-10-10、全体点検)。`using (FrameCost.Scope("名前")) { ... }` で囲んだ処理の時間を足していき、
// StallProbe が重いフレームを見つけた時に「そのフレームで時間を食った処理」の上位をログへ出す。普段はログを出さない(足し算だけ)。
public static class FrameCost
{
    static readonly Dictionary<string, long> ticks = new Dictionary<string, long>();
    static readonly List<KeyValuePair<string, long>> sortBuf = new List<KeyValuePair<string, long>>();

    public struct Section : System.IDisposable
    {
        readonly string name; readonly long start;
        public Section(string n) { name = n; start = Stopwatch.GetTimestamp(); }
        public void Dispose()
        {
            long d = Stopwatch.GetTimestamp() - start;
            ticks[name] = ticks.TryGetValue(name, out long t) ? t + d : d;
        }
    }

    public static Section Scope(string name) => new Section(name);

    // フレームの区切り(StallProbe がフレームの最初に呼ぶ)。前のフレームの合計を控えて数え直す。文字列は作らない(毎フレーム呼ぶため)
    static readonly Dictionary<string, long> last = new Dictionary<string, long>();
    public static double LastMaxMs { get; private set; }
    public static void EndFrame()
    {
        last.Clear();
        long max = 0;
        foreach (var kv in ticks) { last[kv.Key] = kv.Value; if (kv.Value > max) max = kv.Value; }
        // 一度出た名前は 0 で残す(Dictionary の作り直しをしない = 毎フレームの割り当てなし)
        sortBuf.Clear();
        foreach (var kv in ticks) sortBuf.Add(kv);
        foreach (var kv in sortBuf) ticks[kv.Key] = 0;
        LastMaxMs = max * 1000.0 / Stopwatch.Frequency;
    }

    // 前のフレームで時間を食った処理の上位(ログを出す時だけ呼ぶ)
    public static string LastTop(int count, float minMs)
    {
        sortBuf.Clear();
        foreach (var kv in last) if (kv.Value > 0) sortBuf.Add(kv);
        sortBuf.Sort((x, y) => y.Value.CompareTo(x.Value));
        var sb = new StringBuilder();
        double toMs = 1000.0 / Stopwatch.Frequency;
        for (int i = 0; i < sortBuf.Count && i < count; i++)
        {
            double ms = sortBuf[i].Value * toMs;
            if (ms < minMs) break;
            sb.Append(sortBuf[i].Key).Append('=').Append(ms.ToString("F0")).Append("ms ");
        }
        return sb.Length > 0 ? sb.ToString().TrimEnd() : "(none measured)";
    }

    public static void Reset() => ticks.Clear();
}
