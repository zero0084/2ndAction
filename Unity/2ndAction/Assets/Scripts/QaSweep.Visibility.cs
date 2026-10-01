#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 全ステージのPlayerの見やすさ(2026-10-01)。 -qaVisShots <dir> [-qaVisChars a,b] [-qaVisVideo 0|1]
// ステージ×時間帯×キャラ(暗い色/明るい色/中間)×速度(低速/高速)で、補助なし(before)/あり(after)を撮る。
// 各写真の名前と、その時のPlayerの画面上の位置を記録する(後で切り出して並べるため)。
// 高速では跳ぶ/攻撃も入れる。最後に代表的な場面を数秒ずつ毎フレーム書き出す(動画用)。
public partial class QaSweep
{
    IEnumerator VisShotsMode()
    {
        Application.targetFrameRate = 60;
        string[] chars = Arg("-qaVisChars", "swordsman,vampire,ninja,noble_lady,miko,fighter").Split(',');
        var stages = new List<(string stage, int[] segs)>
        {
            ("wasteland_road", new[] { 0, 1, 2, 3 }),
            ("natural_cave", new[] { -1 }),
            ("sky_corridor", new[] { -1 }),
            ("last_corridor", new[] { -1 }),
        };
        string[] segName = { "day", "evening", "night", "dawn" };
        var fpsLog = new List<string>();
        foreach (var (stage, segs) in stages)
            foreach (string ch in chars)
            {
                yield return BeginRun(ch, stage);
                PrepSceneryRun();
                yield return new WaitForSeconds(1.2f);
                foreach (int seg in segs)
                {
                    string tname = seg >= 0 ? segName[seg] : "-";
                    if (seg >= 0) { SceneryCycle.DebugPreviewDistance = SceneryCycle.DistanceForSegment(seg); yield return new WaitForSeconds(2.2f); }
                    for (int speed = 0; speed < 2; speed++)
                    {
                        PlayerController.DebugSpeedScale = speed == 0 ? 1f : 4.5f;
                        yield return new WaitForSeconds(speed == 0 ? 0.6f : 1.6f);
                        if (speed == 1) { pc.debugInjectFlick = PlayerController.FlickDirection.Up; yield return null; yield return null; pc.debugInjectFlick = null; yield return new WaitForSeconds(0.18f); }
                        string sp = speed == 0 ? "low" : "high";
                        float kmh = GameManager.SpeedKmh(pc.CurrentAutoRunSpeed);
                        for (int on = 0; on < 2; on++)
                        {
                            ReadabilityDirector.OutlineEnabled = on == 1;
                            ReadabilityDirector.VeilEnabled = on == 1;
                            Time.timeScale = 0f; // 前後で同じ瞬間を撮る
                            yield return new WaitForEndOfFrame();
                            Vector3 s = Camera.main.WorldToScreenPoint(pc.transform.position + Vector3.up * 1.0f);
                            string name = $"vis_{stage}_{tname}_{ch}_{sp}_{(on == 1 ? "after" : "before")}";
                            L($"[shot] {shotNo:0000}_{name} px={s.x:F0} py={Screen.height - s.y:F0} kmh={kmh:F0} luma={(ReadabilityDirector.Instance != null ? ReadabilityDirector.Instance.Luma : -1f):F2}");
                            Shot(name);
                            yield return null; yield return null;
                        }
                        Time.timeScale = 1f;
                        ReadabilityDirector.OutlineEnabled = ReadabilityDirector.VeilEnabled = true;
                    }
                }
                // 負荷の目安(この端末のフレーム時間、補助あり/なし各2秒)
                if (ch == chars[0])
                {
                    PlayerController.DebugSpeedScale = 4.5f;
                    float[] ms = new float[2];
                    for (int on = 0; on < 2; on++)
                    {
                        ReadabilityDirector.OutlineEnabled = ReadabilityDirector.VeilEnabled = on == 1;
                        Application.targetFrameRate = 1000; QualitySettings.vSyncCount = 0;
                        yield return new WaitForSecondsRealtime(0.5f);
                        int n = 0; float t = 0f;
                        while (t < 2f) { yield return null; t += Time.unscaledDeltaTime; n++; }
                        ms[on] = t / n * 1000f;
                    }
                    Application.targetFrameRate = 60;
                    ReadabilityDirector.OutlineEnabled = ReadabilityDirector.VeilEnabled = true;
                    fpsLog.Add($"{stage}: frame {ms[0]:F2}ms without -> {ms[1]:F2}ms with ({1000f / ms[0]:F0} -> {1000f / ms[1]:F0} fps, uncapped)");
                }
                SceneryCycle.DebugPreviewDistance = null;
                PlayerController.DebugSpeedScale = 1f;
                yield return EndRun();
            }
        foreach (var f in fpsLog) L("[fps] " + f);

        if (Arg("-qaVisVideo", "1") == "1")
        {
            var vids = new (string stage, string ch, int seg)[] { ("wasteland_road", "vampire", 2), ("natural_cave", "swordsman", -1), ("sky_corridor", "miko", -1), ("last_corridor", "noble_lady", -1), ("wasteland_road", "noble_lady", 0) };
            foreach (var v in vids)
            {
                yield return BeginRun(v.ch, v.stage);
                PrepSceneryRun();
                if (v.seg >= 0) SceneryCycle.DebugPreviewDistance = SceneryCycle.DistanceForSegment(v.seg);
                PlayerController.DebugSpeedScale = 4.5f;
                yield return new WaitForSeconds(2.5f);
                string vdir = System.IO.Path.Combine(outDir, $"video_{v.stage}_{v.ch}");
                System.IO.Directory.CreateDirectory(vdir);
                Time.captureFramerate = 30;
                for (int f = 0; f < 150; f++)
                {
                    if (f % 22 == 5) pc.debugInjectFlick = f % 44 == 5 ? PlayerController.FlickDirection.Up : PlayerController.FlickDirection.Forward;
                    else pc.debugInjectFlick = null;
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(vdir, $"f{f:00000}.png"));
                }
                pc.debugInjectFlick = null;
                Time.captureFramerate = 0;
                SceneryCycle.DebugPreviewDistance = null;
                PlayerController.DebugSpeedScale = 1f;
                L($"[video] {v.stage}/{v.ch}: 150 frames");
                yield return EndRun();
            }
        }
    }
}
#endif
