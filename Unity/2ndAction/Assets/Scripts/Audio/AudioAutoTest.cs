using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// BGM/SE/環境音の自動確認(2026-09-29)。
//  Player: 起動引数 -audioTest(実際の音声デバイスで再生状態を確かめる)→ AudioAutoTest.txt(実行ファイルの横)
//  Editor: Tools/OneMoreMile/Audio/Audio Test (batch)
// HOME → 荒野(序盤)→ 10,000m(中盤)→ 70,000m(終盤)→ 通常/強敵/特殊ボス → 撃破で道中へ → 100,000m(死神)
// → ポーズ/カード選択中も途切れない → 音量0 → GAME OVER(ジングル)→ HOME → 洞窟/天空 → FINISH(RESULT)
public class AudioAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        bool run = System.Environment.GetCommandLineArgs().Contains("-audioTest");
#if UNITY_EDITOR
        if (EditorPrefs.GetInt("AudioAutoTest", 0) == 1) { EditorPrefs.SetInt("AudioAutoTest", 0); run = true; }
#endif
        if (!run || FindFirstObjectByType<AudioAutoTest>() != null) return;
        Application.runInBackground = true;
        var go = new GameObject("AudioAutoTest");
        DontDestroyOnLoad(go);
        go.AddComponent<AudioAutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    int failures; bool anyException;
    void L(string s) { log.AppendLine(s); Debug.Log("[AudioTest] " + s); }
    void Check(bool ok, string what) { if (!ok) { failures++; L("  FAIL: " + what); } else L("  ok: " + what); }
    static readonly BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    GameManager gm; AudioManager am; BgmDirector dir; AudioLibrary lib;
    RunCheckpoint.Data savedCheckpoint;
    int[] savedLevels;

    IEnumerator Watchdog() { yield return new WaitForSecondsRealtime(900f); L("WATCHDOG"); Finish(); }

    IEnumerator Start()
    {
        StartCoroutine(Watchdog());
        StartCoroutine(AutoPick());
        Application.logMessageReceived += (c, t, type) => { if (type == LogType.Exception) { anyException = true; L("[EXC] " + c + " " + t.Split('\n')[0]); } };
        savedCheckpoint = RunCheckpoint.Load();
        yield return new WaitForSecondsRealtime(2f);
        Grab();
        savedLevels = new[] { am.MasterVolumeLevel, am.BgmVolumeLevel, am.SfxVolumeLevel, am.EnvVolumeLevel };
        am.SetMasterVolumeLevel(4); am.SetBgmVolumeLevel(4); am.SetSfxVolumeLevel(4); am.SetEnvVolumeLevel(4);
        L($"audio: device={AudioSettings.outputSampleRate}Hz driverCaps={AudioSettings.driverCapabilities} lib={(lib != null)}");
        Check(lib != null && lib.stages.Count >= 3, "AudioLibrary loaded (3 stages)");
        foreach (var s in lib.stages) Check(s.early != null && s.middle != null && s.late != null, $"{s.stageId}: early/middle/late all assigned ({s.early?.name}/{s.middle?.name}/{s.late?.name})");
        Check(lib.homeBgm != null && lib.bossNormal != null && lib.bossStrong != null && lib.bossSpecial != null && lib.bossDeath != null && lib.resultJingle != null && lib.gameOverJingle != null, "HOME / boss x3 / death / result / game over assigned");

        // ---- HOME ----
        L("\n[HOME]");
        yield return new WaitForSecondsRealtime(2f);
        Check(dir.Reason == "home" && am.CurrentBgm == lib.homeBgm, $"HOME plays the HOME BGM ({am.CurrentBgm?.name}, reason {dir.Reason})");
        Check(am.CurrentAmbience == lib.homeAmbience && am.CurrentAmbienceLoop != null, $"HOME ambience ({am.CurrentAmbienceLoop?.name})");
        float t0 = am.CurrentBgmTime; yield return new WaitForSecondsRealtime(1.5f);
        Check(am.CurrentBgmTime > t0 + 1f || am.CurrentBgmTime < t0, $"BGM is actually playing (time {t0:F1} -> {am.CurrentBgmTime:F1})");

        // ---- 荒野: 序盤 → 中盤 → 終盤 ----
        yield return BeginRun("wasteland_road");
        var st = lib.FindStage("wasteland_road");
        yield return WaitReason(r => r.StartsWith("stage"), 4f);
        Check(am.CurrentBgm == st.early, $"run start: stage early BGM ({am.CurrentBgm?.name})");
        Check(am.CurrentAmbience == st.ambience && am.CurrentAmbienceLoop == st.ambience.loop, $"stage ambience ({am.CurrentAmbienceLoop?.name})");
        yield return new WaitForSecondsRealtime(2.5f);
        Check(am.PlayingBgmSources == 1, $"only one BGM playing after the fade ({am.PlayingBgmSources})");
        t0 = am.CurrentBgmTime; yield return new WaitForSecondsRealtime(1f);
        Check(am.CurrentBgmTime > t0, $"same BGM keeps playing (no restart while re-requested every frame: {t0:F1} -> {am.CurrentBgmTime:F1})");
        // 走りながら中盤へ
        WarpTo(10020f);
        yield return new WaitForSecondsRealtime(0.5f);
        bool crossfading = am.PlayingBgmSources == 2;
        yield return new WaitForSecondsRealtime(2.5f);
        Check(am.CurrentBgm == st.middle && dir.Reason.EndsWith("Middle"), $"10,000m: middle BGM ({am.CurrentBgm?.name}, {dir.Reason})");
        Check(crossfading && am.PlayingBgmSources == 1, $"switch is a crossfade (both playing during the fade: {crossfading}), then one");
        WarpTo(70020f);
        yield return new WaitForSecondsRealtime(3f);
        Check(am.CurrentBgm == st.late && dir.Reason.EndsWith("Late"), $"70,000m: late BGM ({am.CurrentBgm?.name})");

        // ---- ボス(通常 → 強敵 → 特殊)、撃破で道中へ ----
        yield return Boss(BossBgmTier.Normal, st.late);
        yield return Boss(BossBgmTier.Strong, st.late);
        yield return Boss(BossBgmTier.Special, st.late);

        // ---- ポーズ/カード選択の間も途切れない ----
        L("\n[pause / card choice]");
        var owner = new object();
        var before = am.CurrentBgm; t0 = am.CurrentBgmTime;
        TimeControl.Pause(owner);
        yield return new WaitForSecondsRealtime(1.2f);
        Check(am.CurrentBgm == before && am.CurrentBgmTime > t0 + 0.8f && am.PlayingBgmSources == 1, $"paused: BGM continues, not restarted ({t0:F1} -> {am.CurrentBgmTime:F1})");
        TimeControl.Resume(owner);
        choiceSeen = false;
        typeof(GameManager).GetMethod("TriggerLevelUpChoice", NP).Invoke(gm, null);
        t0 = am.CurrentBgmTime;
        float w = 0f; bool okDuring = true;
        while (!choiceSeen && w < 10f) { if (am.PlayingBgmSources > 1 || am.CurrentBgm != before) okDuring = false; yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        Check(choiceSeen && okDuring && am.CurrentBgm == before && am.CurrentBgmTime > t0, "card choice: BGM continues (no stop, no double play)");

        // ---- 死神(100,000m) ----
        L("\n[Grim Reaper]");
        WarpTo(100010f);
        w = 0f; while (!(BossManager.Instance.DeathSpawned) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(2.5f);
        Check(BossManager.Instance.DeathSpawned && dir.Reason == "death" && am.CurrentBgm == lib.bossDeath, $"100,000m: Grim Reaper BGM ({am.CurrentBgm?.name}, {dir.Reason})");

        // ---- SE(すべてのSeIdが鳴らせる、連打は間引かれる) ----
        L("\n[SE]");
        int missing = 0; var sb = new StringBuilder();
        foreach (SeId id in System.Enum.GetValues(typeof(SeId)))
        {
            var e = lib.FindSe(id);
            bool emptyOnPurpose = id == SeId.CardFusion || id == SeId.FusionSuccess || id == SeId.FusionFail;
            if (emptyOnPurpose) continue;
            if (!am.PlaySe(id)) { missing++; sb.Append(id).Append(' '); }
            yield return new WaitForSecondsRealtime(0.06f);
        }
        Check(missing == 0, $"every SeId plays ({System.Enum.GetValues(typeof(SeId)).Length - 3} ids; missing: {sb})");
        int c0 = am.SePlayCount; for (int i = 0; i < 10; i++) am.PlaySe(SeId.Hit);
        Check(am.SePlayCount - c0 == 1, $"same SE in one frame is thinned out ({am.SePlayCount - c0} of 10 played)");
        foreach (WeaponType wt in System.Enum.GetValues(typeof(WeaponType)))
        {
            var set = lib.FindWeapon(wt);
            Check(set != null && set.weapon == wt && set.normal.Count > 0 && set.strong != null, $"attack SE for {wt} (normal x{set?.normal.Count}, strong {set?.strong?.name})");
        }

        // ---- 音量0 = 完全無音でも問題なく ----
        L("\n[volume 0]");
        am.SetMasterVolumeLevel(0);
        yield return new WaitForSecondsRealtime(0.3f);
        Check(am.CurrentBgmVolume == 0f && am.SfxOutputVolume == 0f && am.EnvOutputVolume == 0f, "master 0: BGM/SE/ENV output are all 0");
        am.PlaySe(SeId.Jump); am.PlayAttack(1); am.PlayAttackHit();
        am.SetMasterVolumeLevel(4); am.SetBgmVolumeLevel(0); am.SetSfxVolumeLevel(0); am.SetEnvVolumeLevel(0);
        yield return new WaitForSecondsRealtime(0.3f);
        Check(am.CurrentBgmVolume == 0f && am.SfxOutputVolume == 0f && am.EnvOutputVolume == 0f, "BGM/SE/ENV each 0: silent");
        am.SetBgmVolumeLevel(2); am.SetSfxVolumeLevel(4); am.SetEnvVolumeLevel(4);
        yield return new WaitForSecondsRealtime(0.3f);
        Check(am.CurrentBgmVolume > 0f && am.SfxOutputVolume > 0f && am.EnvOutputVolume > 0f, "back on: output returns (volume is independent per channel)");
        Check(PlayerPrefs.GetInt("BgmVolumeLevel") == 2 && PlayerPrefs.HasKey("MasterVolumeLevel") && PlayerPrefs.HasKey("EnvVolumeLevel"), "volume levels are saved in PlayerPrefs");
        am.SetBgmVolumeLevel(4);

        // ---- GAME OVER ----
        L("\n[GAME OVER]");
        typeof(GameManager).GetMethod("FinishRun", NP).Invoke(gm, null);
        yield return new WaitForSecondsRealtime(0.6f);
        Check(dir.Reason == "gameover" && am.CurrentJingle == lib.gameOverJingle && am.JinglePlaying, $"GAME OVER jingle ({am.CurrentJingle?.name})");
        Check(am.PlayingBgmSources == 0 || am.CurrentBgmVolume < 0.05f, "the stage BGM stops for the jingle");
        yield return new WaitForSecondsRealtime(1f);
        Check(am.CurrentJingle == lib.gameOverJingle, "the jingle is played once (not restarted every frame)");
        yield return Home();

        // ---- 洞窟/天空: 序盤曲と環境音、FINISH(RESULT) ----
        foreach (string stage in new[] { "natural_cave", "sky_corridor" })
        {
            yield return BeginRun(stage);
            var sa = lib.FindStage(stage);
            yield return WaitReason(r => r.StartsWith("stage"), 4f);
            yield return new WaitForSecondsRealtime(1.5f);
            Check(am.CurrentBgm == sa.early && am.CurrentAmbienceLoop == sa.ambience.loop, $"{stage}: early BGM {am.CurrentBgm?.name} + ambience {am.CurrentAmbienceLoop?.name}");
            if (stage == "sky_corridor")
            {
                gm.Win();
                yield return new WaitForSecondsRealtime(0.6f);
                Check(dir.Reason == "result" && am.CurrentJingle == lib.resultJingle, $"FINISH: RESULT jingle ({am.CurrentJingle?.name})");
            }
            else typeof(GameManager).GetMethod("FinishRun", NP).Invoke(gm, null);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Home();
        }
        Finish();
    }

    // 距離を飛ばす(途中のボスゲートは飛ばした先より後ろへ送る: 飛んだ瞬間にボスが始まって距離が戻されないように)
    void WarpTo(float d)
    {
        var bm = BossManager.Instance;
        if (bm != null) typeof(BossManager).GetField("gateK", NP)?.SetValue(bm, Mathf.FloorToInt(d / 1000f) + 1);
        gm.DebugWarpToDistance(d);
    }

    void Grab() { gm = GameManager.Instance; am = AudioManager.Instance; dir = am != null ? am.GetComponent<BgmDirector>() : null; lib = am != null ? am.Library : null; }

    IEnumerator WaitReason(System.Func<string, bool> ok, float max)
    {
        float w = 0f; while (!ok(dir.Reason) && w < max) { yield return null; w += Time.unscaledDeltaTime; }
    }

    IEnumerator BeginRun(string stage)
    {
        L($"\n===== run on {stage} =====");
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        Grab();
        gm.SetSelectedCharacter("swordsman");
        gm.SetSelectedStage(stage);
        w = 0f; float retry = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 20f)
        {
            if (!gm.HasStarted && retry <= 0f) { typeof(GameManager).GetMethod("StartGame", NP).Invoke(gm, null); retry = 0.5f; }
            yield return null; w += Time.unscaledDeltaTime; retry -= Time.unscaledDeltaTime;
        }
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        typeof(GameManager).GetProperty("Lives").SetValue(gm, 999);
        typeof(GameManager).GetField("expGainMultiplier", NP)?.SetValue(gm, 0f);
        Check(gm.HasStarted, $"run started on {stage}");
    }

    IEnumerator Home()
    {
        var old = gm;
        gm.Retry();
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        Grab();
        Check(!gm.HasStarted && dir.Reason == "home" && am.CurrentBgm == lib.homeBgm, $"back at HOME: HOME BGM ({am.CurrentBgm?.name})");
    }

    // 次のボスの手前へ飛んで、ボス戦 → 撃破 → 道中曲へ戻る
    IEnumerator Boss(BossBgmTier want, AudioClip roadClip)
    {
        var bm = BossManager.Instance;
        // 欲しい系統のゲート(1000m×k: k%10==0 特殊 / k%5==0 強敵 / それ以外 通常)まで飛ぶ
        int k = Mathf.RoundToInt(bm.NextBossDistance / 1000f);
        while (!(want == BossBgmTier.Special ? k % 10 == 0 : want == BossBgmTier.Strong ? (k % 5 == 0 && k % 10 != 0) : (k % 5 != 0))) k++;
        L($"\n[boss {want} at {k * 1000}m]");
        if (k * 1000 > bm.NextBossDistance) { typeof(BossManager).GetField("gateK", NP)?.SetValue(bm, k); }
        gm.DebugWarpToDistance(bm.NextBossDistance - 20f);
        float w = 0f;
        while (string.IsNullOrEmpty(bm.BossMusicKey) && w < 25f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(2.5f);
        Check(dir.Reason.StartsWith("boss:" + want) && am.CurrentBgm == lib.BossBgm(want, bm.BossMusicKey), $"boss fight ({bm.BossMusicKey}): {want} BGM ({am.CurrentBgm?.name}, {dir.Reason})");
        // 倒す
        w = 0f;
        while (!bm.BossDefeatedThisPhase && w < 20f)
        {
            foreach (var b in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (!b.IsDead) b.TakeDamage(99999, b.CenterWorld);
            foreach (var d in FindObjectsByType<DragonController>(FindObjectsSortMode.None)) d.TakeDamage(99999);
            foreach (var m in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) m.TakeDamage(99999);
            yield return new WaitForSecondsRealtime(0.3f); w += 0.3f;
        }
        yield return new WaitForSecondsRealtime(3f);
        Check(bm.BossDefeatedThisPhase || !bm.IsBossPhase, "boss defeated");
        Check(dir.Reason.StartsWith("stage") && am.CurrentBgm == roadClip, $"after the defeat: back to the road BGM for this distance ({am.CurrentBgm?.name})");
        w = 0f; while (bm.IsBossPhase && w < 20f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.5f);
    }

    bool choiceSeen;
    IEnumerator AutoPick()
    {
        while (true)
        {
            var g = GameManager.Instance;
            if (g != null && g.IsRewardSequenceWaitingForSelection)
            {
                choiceSeen = true;
                yield return new WaitForSecondsRealtime(0.8f);
                var seq = FindFirstObjectByType<RewardCardSequence>();
                if (seq != null) { seq.OnCardClicked(0); yield return new WaitForSecondsRealtime(0.2f); seq.OnCardClicked(0); }
                yield return new WaitForSecondsRealtime(0.5f);
                continue;
            }
            yield return null;
        }
    }

    void Finish()
    {
        if (am != null && savedLevels != null) { am.SetMasterVolumeLevel(savedLevels[0]); am.SetBgmVolumeLevel(savedLevels[1]); am.SetSfxVolumeLevel(savedLevels[2]); am.SetEnvVolumeLevel(savedLevels[3]); }
        if (savedCheckpoint != null) { if (savedCheckpoint.active) RunCheckpoint.Save(savedCheckpoint); else RunCheckpoint.Clear(); }
        L(failures == 0 && !anyException ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        L(anyException ? "EXCEPTIONS LOGGED" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../AudioAutoTest.txt"), log.ToString());
#if UNITY_EDITOR
        if (Application.isBatchMode) EditorApplication.Exit(failures == 0 && !anyException ? 0 : 1); else EditorApplication.isPlaying = false;
#else
        Application.Quit(failures == 0 && !anyException ? 0 : 1);
#endif
    }
}

#if UNITY_EDITOR
public static class AudioTestMenu
{
    [MenuItem("Tools/OneMoreMile/Audio/Audio Test (batch)")]
    public static void RunBatch()
    {
        EditorPrefs.SetInt("AudioAutoTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif
