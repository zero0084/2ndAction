#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;

// アプリ容量の調査(2026-10-18): -qaMemProbe <dir> [-mpChars swordsman,noble_lady,...]
// 起動からホームまでの時間、ホームと各キャラのラン中のメモリ(画像の合計など)を mem_probe.tsv に書き、
// 同じ場面を撮る(通常の画面 + プレイヤーへ寄った画面 = 画質の比較用)。最適化の前後で同じ物を比べる。
public partial class QaSweep
{
    readonly StringBuilder mpLog = new StringBuilder("label\trealtime\tprivateMB\tallocMB\treservedMB\tgfxMB\ttextures\ttexMB\ttopTextures\n");

    void MemRow(string label)
    {
        var tex = Resources.FindObjectsOfTypeAll<Texture2D>();
        long texBytes = 0;
        var sizes = tex.Select(t => (t.name, b: UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t))).ToList();
        foreach (var s in sizes) texBytes += s.b;
        string top = string.Join(", ", sizes.OrderByDescending(s => s.b).Take(5).Select(s => $"{s.name}:{s.b / 1048576f:F1}"));
        float priv = 0f;
        try { priv = System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64 / 1048576f; } catch { }
        mpLog.Append($"{label}\t{Time.realtimeSinceStartup:F1}\t{priv:F0}\t{UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576f:F0}\t" +
                     $"{UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576f:F0}\t{UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576f:F0}\t" +
                     $"{tex.Length}\t{texBytes / 1048576f:F0}\t{top}\n");
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "mem_probe.tsv"), mpLog.ToString());
        L($"[mem] {label}: tex {tex.Length} {texBytes / 1048576f:F0}MB, allocated {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576f:F0}MB");
    }

    IEnumerator MemProbeMode()
    {
        L($"[mem] QA start at {Time.realtimeSinceStartup:F1}s since launch");
        yield return new WaitForSecondsRealtime(2f);
        MemRow("home");
        Shot("home");
        yield return new WaitForSecondsRealtime(1f);
        foreach (var ch in Arg("-mpChars", "swordsman,noble_lady,miko,dragonkin,dragon_lancer").Split(','))
        {
            yield return BeginRun(ch, "wasteland_road");
            SetGod(true);
            yield return new WaitForSeconds(3f);
            MemRow("run_" + ch);
            // 同じ瞬間を2通りで撮る: 止めて、通常の画面 → プレイヤーへ寄った画面
            float ts = Time.timeScale; Time.timeScale = 0f;
            yield return new WaitForEndOfFrame();
            Shot("run_" + ch);
            yield return new WaitForSecondsRealtime(1f);
            var cam = Camera.main; var cf = cam != null ? cam.GetComponent<CameraFollow>() : null;
            if (cam != null && pc != null)
            {
                bool cfWas = cf != null && cf.enabled; if (cf != null) cf.enabled = false;
                float size0 = cam.orthographicSize; var pos0 = cam.transform.position;
                cam.orthographicSize = 1.4f;
                cam.transform.position = new Vector3(pc.transform.position.x, pc.transform.position.y + 0.8f, pos0.z);
                yield return new WaitForEndOfFrame();
                Shot("zoom_" + ch);
                yield return new WaitForSecondsRealtime(1f);
                cam.orthographicSize = size0; cam.transform.position = pos0; if (cf != null) cf.enabled = cfWas;
            }
            Time.timeScale = ts;
            yield return EndRun();
        }
        MemRow("end");
        Check(mpLog.Length > 0, "mem probe written");
    }
}
#endif
