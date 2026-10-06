#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 音の再設計(2026-10-06)の確認: -qaAudio <dir> [-qaAuOnly ABCDEFGHI]
//  A 割り当て(全SeId/道中3段×4ステージ/HOME/ボス5系統/闘技場/風)
//  B 手応えの段(実際の出力の大きさ): 移動 < 通常ヒット < 飛び道具/打ち上げ < 強 < 叩きつけ < FINISH < BOSS FINISH
//  C 釣り合い: 重要なSE > BGM > 環境音、曲ごとの大きさのばらつき
//  D 曲の切り替え: 序盤→中盤→終盤(ゆっくり)、道中→ボス(短く)、ボス→道中(長く)
//  E 同時に鳴る数: 雑魚の多重撃破の中でも BOSS FINISH の音が鳴る
//  F 重要なSEで BGM が一瞬下がって戻る
//  G 全体ミュート/各音量0でも遊べる(無音、ランは続く)
//  H 高速時の風(速さで鳴る/遅いと消える)
//  I 実際の遊びで鳴る(ボスの予兆/撃破/雑魚の撃破/FINISH)
public partial class QaSweep
{
    bool AuCase(char c) { string o = Arg("-qaAuOnly", ""); return o == "" || o.IndexOf(c) >= 0; }
    AudioManager am;
    AudioMeter meter;

    IEnumerator AudioMode()
    {
        yield return BfBeginStage("wasteland_road");
        am = AudioManager.Instance;
        meter = AudioMeter.Ensure();
        Check(am != null && am.Library != null, "AudioManager + AudioLibrary exist");
        Check(meter != null, "the output meter is attached to the AudioListener");
        if (am == null || am.Library == null || meter == null) yield break;
        // 測定のため音量は全部 1(終わったら戻す)
        float m0 = am.MasterVolume, b0 = am.BgmVolume, s0 = am.SfxVolume, e0 = am.EnvVolume; bool mu0 = am.Muted;
        am.SetMuted(false); am.SetMasterVolume(1f, false); am.SetBgmVolume(1f, false); am.SetSfxVolume(1f, false); am.SetEnvVolume(1f, false);
        L($"voices {am.VoiceCount}, sample rate {AudioMeter.SampleRate}");

        if (AuCase('A')) AuLibrary();
        yield return AuSettle();
        if (AuCase('B')) yield return AuSettle();
        if (AuCase('B')) yield return AuLadder();
        if (AuCase('C')) yield return AuSettle();
        if (AuCase('C')) yield return AuBalance();
        if (AuCase('D')) yield return AuSettle();
        if (AuCase('D')) yield return AuTransitions();
        if (AuCase('E')) yield return AuSettle();
        if (AuCase('E')) yield return AuVoices();
        if (AuCase('F')) yield return AuSettle();
        if (AuCase('F')) yield return AuDuck();
        if (AuCase('G')) yield return AuSettle();
        if (AuCase('G')) yield return AuMute();
        if (AuCase('H')) yield return AuSettle();
        if (AuCase('H')) yield return AuWind();
        if (AuCase('I')) yield return AuSettle();
        if (AuCase('I')) yield return AuPlay();

        if (Arg("-qaAuRecord", "0") == "1") { yield return AuSettle(); yield return AuRecord(); }
        BgmDirector.ClearOverride();
        am.SetMuted(mu0); am.SetMasterVolume(m0); am.SetBgmVolume(b0); am.SetSfxVolume(s0); am.SetEnvVolume(e0);
        yield return EndRun();
    }

    // ボス戦/カードの選択/撃破の見た目/止まっている間が終わり、1秒続けて普通に走るまで待つ
    IEnumerator AuSettle()
    {
        var bm = BossManager.Instance;
        float ok = 0f, w = 0f;
        while (ok < 1f && w < 40f)
        {
            yield return null; float dt = Time.unscaledDeltaTime; w += dt;
            bool busy = (bm != null && bm.IsBossPhase) || gm.IsRewardSequenceRunning || BossFinish.AnyRunning || Time.timeScale < 0.5f || gm.IsGameOver;
            ok = busy ? 0f : ok + dt;
        }
        if (ok < 1f) Warn($"settle: still busy after {w:F0}s (boss {bm?.IsBossPhase} reward {gm.IsRewardSequenceRunning} timeScale {Time.timeScale})");
    }

    // 区間の出力を測る(dB)
    IEnumerator AuMeasure(float seconds, System.Action<float, float> done)
    {
        meter.Begin();
        float w = 0f; while (w < seconds) { yield return null; w += Time.unscaledDeltaTime; }
        meter.Collect(out float pk, out float mean, out int n);
        done(pk, mean);
    }

    void AuLibrary()
    {
        L("== A: 割り当て ==");
        var lib = am.Library;
        var missing = new List<string>();
        foreach (SeId id in System.Enum.GetValues(typeof(SeId)))
        {
            var e = lib.FindSe(id);
            if (e == null || e.clips == null || e.clips.Count(c => c != null) == 0) missing.Add(id.ToString());
        }
        L($"[A] SeId {System.Enum.GetValues(typeof(SeId)).Length}, without a clip: {(missing.Count == 0 ? "none" : string.Join(",", missing))}");
        Check(missing.Count == 0, $"A: every SeId has a sound ({missing.Count} missing)");
        foreach (var st in new[] { "sky_corridor", "wasteland_road", "natural_cave", "last_corridor" })
        {
            var s = lib.FindStage(st);
            Check(s != null && s.stageId == st && s.early != null && s.middle != null && s.late != null && s.ambience != null && s.ambience.loop != null, $"A: {st} has early/middle/late BGM + ambient");
            if (s != null) Check(s.early != s.middle && s.middle != s.late, $"A: {st} early/middle/late are different tracks");
        }
        Check(lib.homeBgm != null && lib.homeAmbience != null && lib.homeAmbience.loop != null, "A: HOME BGM + ambient");
        var tiers = new[] { BossBgmTier.Normal, BossBgmTier.Strong, BossBgmTier.Special, BossBgmTier.Death, BossBgmTier.Final }.Select(t => lib.BossBgm(t, "")).ToList();
        Check(tiers.All(c => c != null) && tiers.Distinct().Count() == 5, $"A: 5 boss BGM categories, all different ({tiers.Distinct().Count()})");
        Check(lib.arenaBgm != null && lib.bonusZoneBgm != null && lib.speedWind != null && lib.resultJingle != null && lib.gameOverJingle != null, "A: arena / bonus / wind / result / game over");
        foreach (WeaponType w in System.Enum.GetValues(typeof(WeaponType)))
        {
            var ws = lib.FindWeapon(w);
            Check(ws != null && ws.weapon == w && ws.strong != null && ws.normal.Count(c => c != null) >= 2, $"A: weapon {w} has 2+ swings and a finisher");
        }
    }

    IEnumerator AuLadder()
    {
        L("== B: 手応えの段 ==");
        am.SetBgmVolume(0f, false); am.SetEnvVolume(0f, false);
        yield return new WaitForSecondsRealtime(1.8f);
        var ids = new[] { SeId.Jump, SeId.Land, SeId.Hit, SeId.HitProjectile, SeId.HitLaunch, SeId.StrongHit, SeId.SlamImpact, SeId.FinishHit, SeId.BossFinishHit,
                          SeId.Decide, SeId.CardGet, SeId.LevelUp, SeId.EnemyTelegraph, SeId.BossTelegraph, SeId.BossTelegraphHeavy, SeId.BossWarning, SeId.UltimateActivate, SeId.PlayerDamage };
        var peak = new Dictionary<SeId, float>();
        foreach (var id in ids)
        {
            am.StopAllSe();
            yield return new WaitForSecondsRealtime(0.15f);
            float pk = -120f;
            AudioManager.QaSolo = true; AudioManager.QaSoloId = id; // ゲーム中の他の SE(着地/攻撃など)を混ぜない
            meter.Begin();
            am.PlaySe(id);
            float w = 0f; while (w < 0.9f) { yield return null; w += Time.unscaledDeltaTime; }
            meter.Collect(out pk, out _, out _);
            AudioManager.QaSolo = false;
            peak[id] = pk;
        }
        L("[B] " + string.Join("  ", ids.Select(i => $"{i} {peak[i]:F1}")));
        var ladder = new[] { SeId.Hit, SeId.StrongHit, SeId.SlamImpact, SeId.FinishHit, SeId.BossFinishHit };
        for (int i = 1; i < ladder.Length; i++)
            Check(peak[ladder[i]] > peak[ladder[i - 1]] + 0.5f, $"B: {ladder[i]} ({peak[ladder[i]]:F1}dB) is louder than {ladder[i - 1]} ({peak[ladder[i - 1]]:F1}dB)");
        Check(peak[SeId.Jump] < peak[SeId.Hit] && peak[SeId.Land] < peak[SeId.Hit], "B: movement (jump/land) is quieter than a normal hit");
        Check(peak[SeId.HitLaunch] > peak[SeId.Hit] && peak[SeId.HitLaunch] < peak[SeId.FinishHit], "B: launch hit sits between a normal hit and FINISH");
        Check(peak[SeId.Decide] < peak[SeId.StrongHit], "B: UI is quieter than the strong combat SE");
        Check(peak[SeId.BossTelegraph] > peak[SeId.Hit], $"B: a boss telegraph is clearly audible over a hit ({peak[SeId.BossTelegraph]:F1} > {peak[SeId.Hit]:F1})");
        Check(peak[SeId.EnemyTelegraph] > peak[SeId.Jump], "B: an enemy telegraph is audible");
        auPeak = peak;
        am.SetBgmVolume(1f, false); am.SetEnvVolume(1f, false);
        yield return new WaitForSecondsRealtime(1.8f);
    }
    Dictionary<SeId, float> auPeak;

    IEnumerator AuBalance()
    {
        L("== C: 釣り合い ==");
        var lib = am.Library;
        // 各曲を同じ条件で(環境音0)
        am.SetEnvVolume(0f, false);
        var tracks = new List<(string, AudioClip)>();
        foreach (var st in lib.stages) { tracks.Add((st.stageId + "/early", st.early)); tracks.Add((st.stageId + "/middle", st.middle)); tracks.Add((st.stageId + "/late", st.late)); }
        tracks.Add(("home", lib.homeBgm)); tracks.Add(("boss normal", lib.bossNormal)); tracks.Add(("boss strong", lib.bossStrong)); tracks.Add(("boss special", lib.bossSpecial));
        tracks.Add(("reaper", lib.bossDeath)); tracks.Add(("final", lib.bossFinal)); tracks.Add(("arena", lib.arenaBgm)); tracks.Add(("bonus", lib.bonusZoneBgm));
        var means = new List<float>(); var sb = new System.Text.StringBuilder();
        float bgmPeakMax = -120f;
        foreach (var (name, clip) in tracks)
        {
            if (clip == null) continue;
            BgmDirector.OverrideActive = true; BgmDirector.OverrideClip = clip; BgmDirector.OverrideFadeSeconds = 0.01f; BgmDirector.OverrideReason = "qa " + name;
            yield return new WaitForSecondsRealtime(0.5f);
            float pk = 0f, mean = 0f;
            yield return AuMeasure(2.5f, (p, m) => { pk = p; mean = m; });
            means.Add(mean); bgmPeakMax = Mathf.Max(bgmPeakMax, pk);
            sb.Append($"{name} {mean:F1}/{pk:F1}  ");
        }
        L("[C] BGM mean/peak dB: " + sb);
        float spread = means.Count > 0 ? means.Max() - means.Min() : 99f;
        float bgmMean = means.Count > 0 ? means.Average() : -120f;
        Check(spread <= 5f, $"C: BGM loudness is even across tracks (spread {spread:F1}dB <= 5)");
        // 環境音だけ
        BgmDirector.OverrideActive = true; BgmDirector.OverrideClip = null; BgmDirector.OverrideFadeSeconds = 0.01f;
        am.SetEnvVolume(1f, false);
        yield return new WaitForSecondsRealtime(1.6f);
        float envMean = 0f, envPk = 0f;
        yield return AuMeasure(4f, (p, m) => { envPk = p; envMean = m; });
        L($"[C] ambient alone: mean {envMean:F1} peak {envPk:F1}; BGM average mean {bgmMean:F1} max peak {bgmPeakMax:F1}");
        Check(envMean < bgmMean - 3f, $"C: ambient ({envMean:F1}dB) sits under the BGM ({bgmMean:F1}dB)");
        if (auPeak != null)
        {
            Check(auPeak[SeId.BossFinishHit] > bgmPeakMax + 2f, $"C: BOSS FINISH ({auPeak[SeId.BossFinishHit]:F1}) is above the loudest BGM peak ({bgmPeakMax:F1})");
            Check(auPeak[SeId.FinishHit] > bgmMean + 6f, $"C: FINISH ({auPeak[SeId.FinishHit]:F1}) stands out of the BGM ({bgmMean:F1})");
            Check(auPeak[SeId.Hit] > bgmMean + 2f, $"C: a normal hit ({auPeak[SeId.Hit]:F1}) is heard over the BGM ({bgmMean:F1})");
        }
        BgmDirector.ClearOverride();
        yield return new WaitForSecondsRealtime(1.5f);
    }

    IEnumerator AuTransitions()
    {
        L("== D: 曲の切り替え ==");
        var lib = am.Library; var dir = am.GetComponent<BgmDirector>(); var bm = BossManager.Instance;
        bm.enabled = false; // 関門のボスを出さない(距離の段だけを見る)
        foreach (var (dist, want) in new[] { (9500f, "Early"), (10200f, "Middle"), (70300f, "Late") })
        {
            gm.DebugWarpToDistance(dist);
            float w = 0f; while (w < 4f && !(dir.Reason.EndsWith(want) && am.CurrentBgm == lib.StageBgm(gm.ActiveRunStageId, gm.MaxDistance))) { yield return null; w += Time.unscaledDeltaTime; }
            L($"[D] {dist}m -> {dir.Reason} fade {dir.LastFadeSeconds:F1}s clip {(am.CurrentBgm != null ? am.CurrentBgm.name : "-")}");
            Check(dir.Reason.EndsWith(want), $"D: {dist}m plays the {want} BGM ({dir.Reason})");
            if (want != "Early") Check(Mathf.Approximately(dir.LastFadeSeconds, lib.phaseFade), $"D: {want} crossfades slowly ({dir.LastFadeSeconds:F1}s = {lib.phaseFade})");
            yield return new WaitForSecondsRealtime(lib.phaseFade + 0.3f);
            Check(am.PlayingBgmSources == 1, $"D: only 1 BGM after the crossfade ({am.PlayingBgmSources})");
        }
        bm.enabled = true;
        // 本物の関門(1000mごと)で: 曲の分類(BossMusicKey)は関門が決める
        gm.DebugWarpToDistance(Mathf.Ceil((gm.MaxDistance + 400f) / 1000f) * 1000f - 40f);
        float t = 0f; while (t < 25f && !dir.Reason.StartsWith("boss")) { yield return null; t += Time.unscaledDeltaTime; }
        L($"[D] boss -> {dir.Reason} fade {dir.LastFadeSeconds:F1}s after {t:F1}s");
        Check(dir.Reason.StartsWith("boss"), "D: the boss BGM starts");
        Check(Mathf.Approximately(dir.LastFadeSeconds, lib.bossInFade), $"D: into the boss BGM quickly ({dir.LastFadeSeconds:F1}s = {lib.bossInFade})");
        // 関門のボス(複数体/ドラゴンもあり得る)を全部倒す
        t = 0f;
        while (t < 20f && bm.IsBossPhase && !bm.BossDefeatedThisPhase)
        {
            foreach (var b in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (b != null && !b.IsDead && b.isActiveAndEnabled && !b.IsEntering) b.DebugKillWithAttack(BossFinalAttack.Forward);
            foreach (var d in FindObjectsByType<DragonController>(FindObjectsSortMode.None)) if (d != null && d.isActiveAndEnabled) d.DebugKillWithAttack(BossFinalAttack.Forward);
            yield return new WaitForSecondsRealtime(0.3f); t += 0.3f;
        }
        t = 0f; while (t < 12f && !dir.Reason.StartsWith("stage")) { yield return null; t += Time.unscaledDeltaTime; }
        L($"[D] after the boss -> {dir.Reason} fade {dir.LastFadeSeconds:F1}s after {t:F1}s");
        Check(dir.Reason.StartsWith("stage"), "D: back to the stage BGM after the boss");
        Check(Mathf.Approximately(dir.LastFadeSeconds, lib.bossOutFade), $"D: back from the boss slowly ({dir.LastFadeSeconds:F1}s = {lib.bossOutFade})");
        yield return BfWaitEncounterEnd();
    }

    IEnumerator AuVoices()
    {
        L("== E: 同時に鳴る数 ==");
        am.ResetSeStats();
        // 雑魚の多重撃破相当: 1フレームに 40 回
        for (int i = 0; i < 40; i++) { am.PlaySe(i % 3 == 0 ? SeId.EnemyDefeat : i % 3 == 1 ? SeId.Hit : SeId.FinishBurst); }
        // (同じSEの最小間隔があるので、実際には数回ずつ。間隔を無視して声を埋める)
        var clip = AudioManager.LibraryClip(SeId.EnemyDefeat);
        for (int i = 0; i < 40; i++) am.PlaySfx(clip);
        yield return null;
        int active = am.ActiveVoices;
        am.PlaySe(SeId.BossFinishHit);
        yield return null;
        bool playing = am.IsSePlaying(SeId.BossFinishHit);
        L($"[E] voices {am.VoiceCount}: active {active} peak {am.PeakVoices} stolen {am.StolenVoices} dropped {am.DroppedVoices}; BOSS FINISH playing {playing}");
        Check(am.PeakVoices <= am.VoiceCount, "E: the voice count is capped");
        Check(playing, "E: BOSS FINISH plays even when every voice is busy (it takes a lower-priority voice)");
        yield return new WaitForSecondsRealtime(2.5f);
    }

    IEnumerator AuDuck()
    {
        L("== F: 重要なSEで BGM を一瞬下げる ==");
        yield return new WaitForSecondsRealtime(0.5f);
        float before = am.CurrentBgmVolume;
        am.PlaySe(SeId.BossFinishHit);
        yield return new WaitForSecondsRealtime(0.15f);
        float during = am.CurrentBgmVolume, duck = am.SeDuckNow;
        yield return new WaitForSecondsRealtime(2.2f);
        float after = am.CurrentBgmVolume;
        L($"[F] BGM volume {before:F3} -> {during:F3} (duck {duck:F2}) -> {after:F3}");
        Check(duck < 0.75f && during < before * 0.8f, "F: the BGM dips under an important SE");
        Check(am.SeDuckNow > 0.97f && after > before * 0.9f, "F: the BGM comes back");
    }

    IEnumerator AuMute()
    {
        L("== G: ミュート/音量0 ==");
        float d0 = gm.MaxDistance;
        am.SetMuted(true);
        yield return new WaitForSecondsRealtime(0.3f);
        float pk = 0f;
        am.PlaySe(SeId.BossFinishHit, 1f); am.PlaySe(SeId.Hit);
        yield return AuMeasure(1.2f, (p, m) => pk = p);
        L($"[G] muted: output peak {pk:F1}dB, run {d0:F0} -> {gm.MaxDistance:F0}m");
        Check(pk < -70f, $"G: full mute is silent ({pk:F1}dB)");
        Check(gm.MaxDistance > d0 + 1f && !gm.IsGameOver, "G: the run goes on while muted");
        am.SetMuted(false);
        foreach (var ch in new[] { "master", "bgm", "se", "env" })
        {
            am.SetMasterVolume(ch == "master" ? 0f : 1f, false); am.SetBgmVolume(ch == "bgm" ? 0f : 1f, false); am.SetSfxVolume(ch == "se" ? 0f : 1f, false); am.SetEnvVolume(ch == "env" ? 0f : 1f, false);
            yield return new WaitForSecondsRealtime(0.2f);
            am.PlaySe(SeId.Hit);
            yield return null;
            bool ok = ch == "master" ? am.CurrentBgmVolume == 0f && am.SfxOutputVolume == 0f && am.EnvOutputVolume == 0f
                    : ch == "bgm" ? am.CurrentBgmVolume == 0f && am.SfxOutputVolume > 0f
                    : ch == "se" ? am.SfxOutputVolume == 0f && am.LoudestVoiceVolume == 0f && am.CurrentBgmVolume > 0f
                    : am.EnvOutputVolume == 0f && am.CurrentBgmVolume > 0f;
            Check(ok, $"G: {ch} = 0 silences only that part (bgm {am.CurrentBgmVolume:F3} se {am.SfxOutputVolume:F3} env {am.EnvOutputVolume:F3} loudest voice {am.LoudestVoiceVolume:F3})");
        }
        am.SetMasterVolume(1f, false); am.SetBgmVolume(1f, false); am.SetSfxVolume(1f, false); am.SetEnvVolume(1f, false);
        yield return new WaitForSecondsRealtime(0.5f);
    }

    IEnumerator AuWind()
    {
        L("== H: 高速時の風 ==");
        SetKmh(300f);
        // (途中でレベルアップの選択が開くと止まって風が消えるので、区間の最大で見る)
        float hi = 0f, hiKmh = 0f, ww = 0f;
        while (ww < 3f) { yield return null; ww += Time.unscaledDeltaTime; if (am.WindLevel > hi) { hi = am.WindLevel; hiKmh = pc.CurrentRunKmh; } }
        var lib = am.Library;
        float k = Mathf.InverseLerp(lib.windFromKmh, lib.windFullKmh, hiKmh), want = lib.windMaxVolume * k * k * (3f - 2f * k);
        SetKmh(25f);
        yield return new WaitForSecondsRealtime(2.5f);
        float lo = am.WindLevel;
        PlayerController.DebugSpeedScale = 1f;
        L($"[H] wind at {hiKmh:F0}km/h {hi:F2} (expected {want:F2}), at 25km/h {lo:F2}");
        Check(hiKmh > 150f && hi > 0.15f && Mathf.Abs(hi - want) < 0.08f, "H: the wind is heard at high speed and follows the speed");
        Check(lo < 0.05f, "H: no wind at low speed");
    }

    IEnumerator AuPlay()
    {
        L("== I: 実際の遊びで鳴る ==");
        am.ResetSeStats();
        float sumDt = 0f; int frames = 0; float maxDt = 0f;
        // 雑魚: 普通の撃破と FINISH(強い撃破)
        var list = FinishDebug.Spawn("goblin", 6, 5f, 1.3f);
        yield return null;
        FinishDebug.Kill(list.Take(3).ToList(), PlayerAttackKind.Normal, false, false, false);
        FinishDebug.Kill(list.Skip(3).ToList(), PlayerAttackKind.Normal, true, false, false);
        yield return new WaitForSecondsRealtime(1.5f);
        // ボス: 予兆が聞こえる → 撃破
        var bm = BossManager.Instance; bm.enabled = true;
        bm.DebugSpawnBossForTest(0, (int)WildBossKind.BlackKnight);
        float t = 0f;
        while (t < 16f && Count(SeId.BossTelegraph) + Count(SeId.BossTelegraphHeavy) == 0) { yield return null; float dt = Time.unscaledDeltaTime; t += dt; sumDt += dt; frames++; maxDt = Mathf.Max(maxDt, dt); }
        var boss = FindObjectsByType<WildBossBase>(FindObjectsSortMode.None).FirstOrDefault(b => b != null && !b.IsDead);
        if (boss != null) boss.DebugKillWithAttack(BossFinalAttack.Slam);
        t = 0f; while (t < 3f) { yield return null; float dt = Time.unscaledDeltaTime; t += dt; sumDt += dt; frames++; maxDt = Mathf.Max(maxDt, dt); }
        string counts = string.Join(", ", am.SeCounts.OrderBy(k => k.Key.ToString()).Select(k => $"{k.Key} {k.Value}"));
        L($"[I] SE played: {counts}");
        L($"[I] frame avg {(frames > 0 ? sumDt / frames * 1000f : 0f):F1}ms max {maxDt * 1000f:F1}ms, voices peak {am.PeakVoices}/{am.VoiceCount} stolen {am.StolenVoices} dropped {am.DroppedVoices} offscreen skipped {am.OffscreenSkipped}");
        Check(Count(SeId.EnemyDefeat) >= 1 && Count(SeId.FinishHit) >= 1 && Count(SeId.FinishBurst) >= 1, "I: enemy defeat + FINISH sounds play");
        Check(Count(SeId.BossTelegraph) + Count(SeId.BossTelegraphHeavy) >= 1, "I: a boss telegraph is heard before its attack");
        Check(Count(SeId.BossFinishHit) == 1 && Count(SeId.BossDissolve) >= 1, "I: BOSS FINISH plays its hit and the dissolve once");
        yield return BfWaitEncounterEnd();
    }
    int Count(SeId id) => am.SeCounts.TryGetValue(id, out int n) ? n : 0;

    // 録音(-qaAuRecord 1): 実際の遊びに近い流れを約70秒 wav に残す(マスターが聞いて確かめる用)
    //  雑魚と走る(140km/h、補助の自動攻撃)→ 雑魚の多重撃破/FINISH → ボスの関門(警告/ボス曲/予兆)→ BOSS FINISH → 報酬 → 300km/h(風/音速)
    IEnumerator AuRecord()
    {
        L("== R: 録音 ==");
        var bm = BossManager.Instance;
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = true;
        bm.enabled = false;
        meter.StartRecording();
        SetKmh(140f);
        yield return new WaitForSecondsRealtime(14f);
        PlayerController.DebugSpeedScale = 1f;
        var list = FinishDebug.Spawn("goblin", 5, 5f, 1.2f);
        yield return null;
        FinishDebug.Kill(list.Take(2).ToList(), PlayerAttackKind.Normal, false, false, false);
        yield return new WaitForSecondsRealtime(0.6f);
        FinishDebug.Kill(list.Skip(2).ToList(), PlayerAttackKind.Down, true, false, false);
        yield return new WaitForSecondsRealtime(2.5f);
        bm.enabled = true;
        gm.DebugWarpToDistance(Mathf.Ceil((gm.MaxDistance + 400f) / 1000f) * 1000f - 40f);
        float t = 0f; while (t < 25f && !(bm.IsBossPhase && FindObjectsByType<WildBossBase>(FindObjectsSortMode.None).Any(b => b != null && !b.IsDead && !b.IsEntering))) { yield return null; t += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(9f); // ボス曲と予兆を聞く
        t = 0f;
        while (t < 15f && bm.IsBossPhase && !bm.BossDefeatedThisPhase)
        {
            foreach (var b in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (b != null && !b.IsDead && b.isActiveAndEnabled && !b.IsEntering) b.DebugKillWithAttack(BossFinalAttack.Slam);
            foreach (var d in FindObjectsByType<DragonController>(FindObjectsSortMode.None)) if (d != null && d.isActiveAndEnabled) d.DebugKillWithAttack(BossFinalAttack.Slam);
            yield return new WaitForSecondsRealtime(0.4f); t += 0.4f;
        }
        yield return new WaitForSecondsRealtime(8f); // 撃破の見た目 → 報酬(自動で選ぶ)→ 道中の曲へ戻る
        bm.enabled = false;
        SetKmh(300f);
        yield return new WaitForSecondsRealtime(8f);
        PlayerController.DebugSpeedScale = 1f;
        yield return new WaitForSecondsRealtime(2f);
        string path = System.IO.Path.Combine(outDir, "gameplay_audio.wav");
        float sec = meter.StopRecording(path);
        L($"[R] recorded {sec:F1}s -> {path}");
        Check(sec > 30f, $"R: recorded the gameplay audio ({sec:F1}s)");
    }
}
#endif
