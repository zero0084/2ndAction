#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

// 保存 → 終了 → 起動し直して読み込み の確認(2026-10-14)。専用のファイル(-fileSave)だけを使う(通常のセーブに触れない)。
// 以前メモリだけの保存(-memSave)で確かめられなかった「音量 / 闘技場の構成 / CONTINUE」を、本当の再起動で確かめる。
//  -qaPersist <dir> -psPhase set|check -psShared <dir>
//   set   … 音量(全体 0.6 / BGM 0.35)・闘技場の構成・中断再開の慣らし(開発用)を設定し、ランを中断してホームへ(CONTINUE が残る)
//   check … 起動し直した後: 音量が音に反映されている / 闘技場の構成が読み込まれる / CONTINUE で同じ地点・同じ能力値から再開できる
public partial class QaSweep
{
    IEnumerator PersistMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        gm = GameManager.Instance;
        string phase = Arg("-psPhase", "check");
        string shared = Arg("-psShared", outDir);
        System.IO.Directory.CreateDirectory(shared);
        string expectPath = System.IO.Path.Combine(shared, "persist_expect.txt");
        Check(FileSaveStore.Active, "runs on the dedicated save file (-fileSave), not the normal save");
        if (!FileSaveStore.Active) yield break;
        var am = AudioManager.Instance;
        L($"[ps] phase={phase} boot={SaveSystem.LastBoot?.kind} store={FileSaveStore.Folder}");

        if (phase == "set")
        {
            am.SetMasterVolume(0.6f, true);
            am.SetBgmVolume(0.35f, true);
            var cfg = ArenaConfig.Fresh();
            cfg.character = "archer"; cfg.distance = 45000f; cfg.kmh = 140f; cfg.speedMode = 2; cfg.seed = 777; cfg.invincible = true;
            ArenaConfigStore.Save(cfg);
            SaveStore.SetInt(GameManager.ResumeEaseDevKey, 1);
            SaveStore.Save();
            // ランを中断してホームへ(中断データ = CONTINUE)
            yield return BeginRun("dual_blade", "wasteland_road");
            WarpTo(1800f);
            yield return new WaitForSecondsRealtime(1.5f);
            stopKeepAlive = true;
            yield return null;
            // 確認用の HP の補充(最大を超える)を普通の値へ(実際のゲームでは HP は最大を超えない)。少し減らした状態で保存
            typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { Mathf.Max(1, gm.maxLives - 13) });
            typeof(GameManager).GetMethod("SaveCheckpoint", NP).Invoke(gm, null);
            string sig = SigHead(StatSignature());
            var cp0 = RunCheckpoint.Load();
            yield return ResumeGoHome();
            var cp = RunCheckpoint.Load();
            Check(RunCheckpoint.HasActiveRun, "set: the suspended run is saved");
            SaveStore.Save();
            System.IO.File.WriteAllText(expectPath, $"{cp.checkpointDistance.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}\n{sig}\n{JsonUtility.ToJson(cfg)}");
            L($"[ps] set: volume 0.6/0.35, arena archer 45000m 140km/h, checkpoint {cp.checkpointDistance:F0}m ({cp0.checkpointDistance:F0}), saves={FileSaveStore.Saves}");
            yield break;
        }

        var lines = System.IO.File.Exists(expectPath) ? System.IO.File.ReadAllText(expectPath).Split('\n') : new string[0];
        Check(lines.Length >= 3, "check: the expectation from the 'set' launch exists");
        if (lines.Length < 3) yield break;
        float wantDist = float.Parse(lines[0], System.Globalization.CultureInfo.InvariantCulture);
        // 音量: 保存された値と、実際の音の設定(AudioManager)の両方
        Check(Mathf.Abs(SaveStore.GetFloat("MasterVolume", -1f) - 0.6f) < 0.01f && Mathf.Abs(am.MasterVolume - 0.6f) < 0.01f, $"check: master volume restored after the restart ({am.MasterVolume:F2})");
        Check(Mathf.Abs(SaveStore.GetFloat("BgmVolume", -1f) - 0.35f) < 0.01f && Mathf.Abs(am.BgmVolume - 0.35f) < 0.01f, $"check: BGM volume restored after the restart ({am.BgmVolume:F2})");
        // 闘技場: 構成を読み込む(画面を開いた時と同じ読み込み)
        ArenaConfigStore.ResetLoaded(); ArenaConfigStore.Load();
        var c = ArenaMode.Config;
        Check(c != null && c.character == "archer" && Mathf.Abs(c.distance - 45000f) < 1f && Mathf.Abs(c.kmh - 140f) < 0.1f && c.speedMode == 2 && c.seed == 777, $"check: the arena setup is loaded after the restart ({c?.character} {c?.distance:F0}m {c?.kmh:F0}km/h mode {c?.speedMode} seed {c?.seed})");
        Check(SaveStore.GetInt(GameManager.ResumeEaseDevKey, -1) == 1, "check: the dev resume-ease setting is kept");
        // CONTINUE: 同じ地点・同じ能力値から
        Check(RunCheckpoint.HasActiveRun, "check: CONTINUE is available after the restart");
        yield return ResumeContinue("persist");
        Check(gm.HasStarted && Mathf.Abs(gm.MaxDistance - wantDist) < 1.5f, $"check: CONTINUE resumes at the saved point ({gm.MaxDistance:F0} vs {wantDist:F0})");
        string sigNow = SigHead(StatSignature());
        Check(sigNow == lines[1].Trim(), "check: stats/HP/level/cards restored exactly after the restart");
        if (sigNow != lines[1].Trim()) L($"   before : {lines[1]}\n   after  : {sigNow}");
        yield return EndRun();
    }
}
#endif
