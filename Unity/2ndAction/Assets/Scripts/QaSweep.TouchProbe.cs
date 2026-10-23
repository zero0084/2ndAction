#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Text;
using UnityEngine;

// 本物の指(Android の adb input swipe / tap など)の結果を記録する(2026-10-23): -qaTouchProbe <dir> [-tpbChar swordsman] [-tpbSeconds 240]
// 黒剣士で荒野を走り(敵なし・無敵・補助 OFF)、届いたフリックの向きと結果・タップ攻撃・技の始まりを touch_probe.txt に書く。
// 準備ができたら ready.txt を書く(adb はそれを待ってから操作する)。画面の大きさと、操作の目安の位置も書く
public partial class QaSweep
{
    IEnumerator TouchProbeMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 20f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(2f);
        gm = GameManager.Instance;
        string ch = Arg("-tpbChar", "swordsman");
        float secs = float.Parse(Arg("-tpbSeconds", "240"), System.Globalization.CultureInfo.InvariantCulture);
        yield return BeginRun(ch, "wasteland_road");
        pc = PlayerController.Instance;
        SetGod(true);
        var tm = TerrainManager.Instance; if (tm != null) tm.enemySpawnChance = 0f;
        if (HighSpeedAssist.Instance != null) { HighSpeedAssist.Instance.SetEnabled(false); HighSpeedAssist.Instance.SetAutoAttack(false); }
        var log = new StringBuilder();
        string path = System.IO.Path.Combine(outDir, "touch_probe.txt");
        void W(string s) { log.AppendLine($"{Time.realtimeSinceStartup:F2}\t{s}"); System.IO.File.WriteAllText(path, log.ToString()); }
        W($"screen {Screen.width}x{Screen.height} orientation {Screen.orientation} char {ch}");
        System.Action<int, PlayerController.FlickDirection, PlayerController.FlickFate> on = (id, d, f) => W($"flick {d} {f}");
        PlayerController.QaFlickFate += on;
        int taps = PlayerController.TapAttacks, moves = pc.DebugMoveStarts;
        bool lastTouch = false;
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "ready.txt"), $"{Screen.width} {Screen.height}");
        float t = 0f;
        while (t < secs)
        {
            bool touching = Input.touchCount > 0;
            if (touching != lastTouch) { W(touching ? $"touch down {Input.GetTouch(0).position}" : "touch up"); lastTouch = touching; }
            if (PlayerController.TapAttacks != taps) { W($"tap attack (total {PlayerController.TapAttacks})"); taps = PlayerController.TapAttacks; }
            int m = pc.DebugMoveStarts; if (m != moves) { W($"move started (grounded {pc.IsGrounded})"); moves = m; }
            yield return null; t += Time.unscaledDeltaTime;
        }
        PlayerController.QaFlickFate -= on;
        W("end");
        SetGod(false);
    }
}
#endif
