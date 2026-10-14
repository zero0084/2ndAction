#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// SE の改修(2026-10-14): ボス遭遇の重低音 / ジャンプ / ボスの声。専用の保存(-fileSave / -memSave)で行う。
//  -qaBossVoice <dir> [-bvPhase all|static|encounter|kinds|controls]
//   static    … 割り当て(ジャンプ/二段/遭遇の音が新しいファイル、声 21 個に音がある、全ボスの名前が表にある)
//   encounter … 本当の遭遇(荒野 1km のオオカミ): 重低音 → 登場の地鳴り → 咆哮 の順と間、ボス戦の BGM、撃破の断末魔、5回続けても重ならない・溜まらない
//   kinds     … 全ボス(荒野/洞窟/天空 33体 + 追跡の死神)を出して、種類どおりの咆哮と断末魔が鳴るか
//   controls  … ポーズ/ゲームオーバーで止まる、消音で鳴らない、SE の音量に従う、ジャンプの連続(同時に鳴る数)、FPS
public partial class QaSweep
{
    IEnumerator BossVoiceMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        gm = GameManager.Instance;
        bool safe = FileSaveStore.Active || MemorySaveStore.Active;
        Check(safe, "runs on a dedicated save only");
        if (!safe) yield break;
        Application.targetFrameRate = 60;
        string phase = Arg("-bvPhase", "all");
        if (phase == "all" || phase == "static") BvStatic();
        if (phase == "all" || phase == "encounter") yield return BvEncounter();
        if (phase == "all" || phase == "kinds") yield return BvKinds();
        if (phase == "all" || phase == "controls") yield return BvControls();
    }

    AudioManager Am => AudioManager.Instance;
    int SeN(SeId id) => Am.SeCounts.TryGetValue(id, out int n) ? n : 0;
    static IEnumerable<SeId> VoiceIds() { for (int i = (int)SeId.BossRoarBeast; i <= (int)SeId.BossDeathMech; i++) yield return (SeId)i; }
    bool AnyVoicePlaying => VoiceIds().Any(id => Am.IsSePlaying(id));

    void BvStatic()
    {
        var lib = Am.Library;
        string Clip(SeId id) { var e = lib != null ? lib.FindSe(id) : null; return e != null && e.clips.Count > 0 && e.clips[0] != null ? e.clips[0].name : "(none)"; }
        L($"[bv] Jump={Clip(SeId.Jump)} DoubleJump={Clip(SeId.DoubleJump)} BossWarning={Clip(SeId.BossWarning)} BossAppear={Clip(SeId.BossAppear)} vol {lib.FindSe(SeId.BossAppear).volume}");
        Check(Clip(SeId.Jump) == "mv_jump_v3" && Clip(SeId.DoubleJump) == "mv_double_jump_v3", "static: jump / double jump use the new light swishes");
        Check(Clip(SeId.BossWarning) == "boss_impact_v3", "static: the boss encounter uses the deep impact (no chime)");
        foreach (var id in VoiceIds()) Check(Clip(id).EndsWith("_v3"), $"static: {id} has a sound ({Clip(id)})");
        var keys = new List<string>();
        foreach (WildBossKind k in System.Enum.GetValues(typeof(WildBossKind))) keys.Add(k == WildBossKind.Dragon ? "Dragon" : "Wild/" + k);
        foreach (CaveBossKind k in System.Enum.GetValues(typeof(CaveBossKind))) keys.Add("Cave/" + k);
        foreach (SkyBossKind k in System.Enum.GetValues(typeof(SkyBossKind))) keys.Add(k == SkyBossKind.Dragon ? "Dragon" : k == SkyBossKind.Majin ? "Majin" : "Sky/" + k);
        keys.Add("Reaper");
        var missing = keys.Distinct().Where(k => !BossVoice.Table.ContainsKey(k)).ToList();
        Check(missing.Count == 0, $"static: every boss has a voice in the table ({keys.Distinct().Count()} names, missing: {string.Join(",", missing)})");
        foreach (var k in keys.Distinct()) if (BossVoice.Table.TryGetValue(k, out var p)) L($"[bv]   {k,-20} -> {p.kind,-8} pitch {p.pitch:F2} vol {p.vol:F2}");
    }

    // 本当の遭遇(1km のオオカミ)。重低音 → 地鳴り → 咆哮 の順、間、BGM
    IEnumerator BvEncounter()
    {
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        for (int rep = 0; rep < 5; rep++)
        {
            float gate = 1000f * (rep + 1);
            int w0n = SeN(SeId.BossWarning), a0n = SeN(SeId.BossAppear), r0n = SeN(SeId.BossRoarBeast), d0n = SeN(SeId.BossDeathBeast);
            float tW = -1f, tA = -1f, tR = -1f, tBgm = -1f;
            WarpTo(gate - 70f);
            float w = 0f;
            while (w < 25f && (tR < 0f || tBgm < 0f))
            {
                float now = Time.unscaledTime;
                if (tW < 0f && SeN(SeId.BossWarning) > w0n) tW = now;
                if (tA < 0f && SeN(SeId.BossAppear) > a0n) tA = now;
                if (tR < 0f && BossVoice.Played > 0 && SeN(SeId.BossRoarBeast) > r0n) tR = now;
                var bgm = Am.GetComponent<BgmDirector>();
                if (tBgm < 0f && bgm != null && bgm.Reason.StartsWith("boss")) tBgm = now;
                yield return null; w += Time.unscaledDeltaTime;
            }
            string bossNames = string.Join(",", WildAlive().Select(b => b.bossName));
            L($"[bv] #{rep + 1} gate {gate:F0} ({bossNames}): boom at 0, appear rumble +{tA - tW:F2}s, roar +{tR - tW:F2}s, boss BGM +{tBgm - tW:F2}s, warnings {SeN(SeId.BossWarning) - w0n}, roars {SeN(SeId.BossRoarBeast) - r0n}");
            Check(tW > 0f && tR > 0f, $"#{rep + 1}: the boom and the roar both play");
            Check(tR - tW >= BossVoice.AfterBoomSeconds - 0.05f, $"#{rep + 1}: the roar waits for the boom ({tR - tW:F2}s >= {BossVoice.AfterBoomSeconds}s)");
            Check(tA < 0f || tA >= tW, $"#{rep + 1}: the appear rumble comes after the boom");
            Check(SeN(SeId.BossWarning) - w0n == 1, $"#{rep + 1}: one boom per encounter ({SeN(SeId.BossWarning) - w0n})");
            int bosses = WildAlive().Count();
            Check(SeN(SeId.BossRoarBeast) - r0n <= Mathf.Max(1, bosses), $"#{rep + 1}: at most one roar per boss ({SeN(SeId.BossRoarBeast) - r0n} for {bosses})");
            Check(tBgm > 0f, $"#{rep + 1}: the boss BGM starts");
            yield return new WaitForSecondsRealtime(0.8f);
            KillAllBosses();
            w = 0f; while (SeN(SeId.BossDeathBeast) == d0n && w < 4f) { yield return null; w += Time.unscaledDeltaTime; }
            Check(SeN(SeId.BossDeathBeast) > d0n, $"#{rep + 1}: the death cry plays on defeat");
            yield return WaitPhaseEnd();
            yield return new WaitForSecondsRealtime(3.0f);
            Check(!AnyVoicePlaying, $"#{rep + 1}: no boss voice keeps playing after the defeat");
            L($"[bv] #{rep + 1} voices: active {Am.ActiveVoices}/{Am.VoiceCount}, peak {Am.PeakVoices}, dropped {Am.DroppedVoices}");
        }
        Check(Am.VoiceCount <= 32, $"encounter: the SE voices do not pile up ({Am.VoiceCount})");
        yield return EndRun();
    }

    // 全ボスを出して、種類どおりの咆哮と断末魔
    IEnumerator BvKinds()
    {
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        WarpTo(300f);
        yield return new WaitForSecondsRealtime(1f);
        var bm = BossManager.Instance;
        var list = new List<(int fam, int kind, string key)>();
        foreach (WildBossKind k in System.Enum.GetValues(typeof(WildBossKind))) list.Add((0, (int)k, k == WildBossKind.Dragon ? "Dragon" : "Wild/" + k));
        foreach (CaveBossKind k in System.Enum.GetValues(typeof(CaveBossKind))) list.Add((1, (int)k, "Cave/" + k));
        foreach (SkyBossKind k in System.Enum.GetValues(typeof(SkyBossKind))) list.Add((2, (int)k, k == SkyBossKind.Dragon ? "Dragon" : k == SkyBossKind.Majin ? "Majin" : "Sky/" + k));
        string only = Arg("-bvOnly", "");
        int ok = 0;
        foreach (var e in list)
        {
            if (only != "" && !only.Split(',').Contains(e.key)) continue;
            BossVoice.Table.TryGetValue(e.key, out var p);
            SeId roar = BossVoice.IdFor(p.kind, BossVoice.Moment.Roar), death = BossVoice.IdFor(p.kind, BossVoice.Moment.Death);
            int r0 = SeN(roar), d0 = SeN(death);
            string key0 = BossVoice.LastKey;
            bm.DebugSpawnBossForTest(e.fam, e.kind);
            float w = 0f;
            while (SeN(roar) == r0 && w < 12f) { yield return null; w += Time.unscaledDeltaTime; }
            bool roared = SeN(roar) > r0 && BossVoice.LastKey == e.key;
            float pitch = 0f; // 鳴っている声の高さ(ボスごとの違い)
            foreach (var src in Am.GetComponents<AudioSource>()) if (src.isPlaying && src.clip != null && src.clip.name.StartsWith("bv_")) pitch = src.pitch;
            yield return new WaitForSecondsRealtime(0.6f);
            // フェニックスは一度だけ蘇る(仕様)→ 断末魔が鳴るまで倒し直す(最大3回)
            w = 0f;
            for (int k = 0; k < 3 && SeN(death) == d0; k++)
            {
                KillAllBosses();
                float wk = 0f; while (SeN(death) == d0 && wk < 5f) { yield return null; wk += Time.unscaledDeltaTime; }
                w += wk;
            }
            bool died = SeN(death) > d0;
            L($"[bv] {e.key,-20} {p.kind,-8} roar {(roared ? "ok" : "MISSING")} (after {w:F1}s, pitch {pitch:F2}) death {(died ? "ok" : "MISSING")}");
            Check(roared, $"kinds: {e.key} roars as {p.kind} when it appears (last '{BossVoice.LastKey}')");
            Check(died, $"kinds: {e.key} has a death cry ({death})");
            if (roared && died) ok++;
            yield return WaitPhaseEnd(15f);
            yield return new WaitForSecondsRealtime(1.0f);
        }
        L($"[bv] kinds: {ok}/{list.Count} bosses roar and cry");
        // 追跡の死神(100,000m)
        int rr = SeN(SeId.BossRoarReaper);
        bm.DebugSpawnReaper();
        float wr = 0f; while (SeN(SeId.BossRoarReaper) == rr && wr < 6f) { yield return null; wr += Time.unscaledDeltaTime; }
        Check(SeN(SeId.BossRoarReaper) > rr, "kinds: the chasing reaper has its eerie voice after the boom");
        L($"[bv] reaper chase voice after {wr:F1}s");
        yield return EndRun();
    }

    IEnumerator BvControls()
    {
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        yield return new WaitForSecondsRealtime(1f);
        // ポーズで止まる
        BossVoice.Play("Wild/Golem", BossVoice.Moment.Roar);
        yield return new WaitForSecondsRealtime(0.3f);
        Check(Am.IsSePlaying(SeId.BossRoarGolem), "controls: a roar is playing");
        typeof(GameManager).GetField("showPauseMenu", NP).SetValue(gm, true);
        yield return null; yield return null;
        Check(!AnyVoicePlaying, "controls: pausing stops the boss voices");
        Check(!BossVoice.Play("Wild/Golem", BossVoice.Moment.Growl), "controls: no new boss voice while paused");
        typeof(GameManager).GetField("showPauseMenu", NP).SetValue(gm, false);
        yield return null;
        // 消音
        Am.SetMuted(true);
        BossVoice.Play("Dragon", BossVoice.Moment.Roar);
        yield return null;
        Check(!Am.IsSePlaying(SeId.BossRoarDragon), "controls: muted -> no boss voice");
        Am.PlayJump(); yield return null;
        Check(!Am.IsSePlaying(SeId.Jump), "controls: muted -> no jump sound");
        Am.SetMuted(false);
        yield return new WaitForSecondsRealtime(0.3f);
        // SE の音量に従う
        float keep = Am.SfxVolume;
        Am.SetSfxVolume(1f, false); Am.StopAllSe(); BossVoice.Play("Cave/Worm", BossVoice.Moment.Growl); yield return null;
        float v1 = Am.LoudestVoiceVolume;
        Am.StopAllSe(); yield return new WaitForSecondsRealtime(1.1f);
        Am.SetSfxVolume(0.5f, false); BossVoice.Play("Cave/Scorpion", BossVoice.Moment.Growl); yield return null;
        float v05 = Am.LoudestVoiceVolume;
        L($"[bv] SE volume 1.0 -> {v1:F3}, 0.5 -> {v05:F3}");
        Check(v1 > 0f && Mathf.Abs(v05 / v1 - 0.5f) < 0.12f, $"controls: the boss voice follows the SE volume ({v05 / Mathf.Max(0.0001f, v1):F2})");
        Am.SetSfxVolume(keep, false); Am.StopAllSe();
        // ジャンプの連続: 同時に鳴る数の上限(2)を超えない
        int maxJ = 0;
        for (int i = 0; i < 12; i++)
        {
            if (i % 2 == 0) Am.PlayJump(); else Am.PlayDoubleJump();
            yield return new WaitForSecondsRealtime(0.04f);
            int n = Am.GetComponents<AudioSource>().Count(s => s.isPlaying && s.clip != null && (s.clip.name == "mv_jump_v3" || s.clip.name == "mv_double_jump_v3"));
            maxJ = Mathf.Max(maxJ, n);
        }
        L($"[bv] rapid jumps: at most {maxJ} jump sounds at once");
        Check(maxJ <= 4, $"controls: rapid jumps do not stack ({maxJ})");
        // 本当のジャンプの処理(PlayerController.FireJump)で、通常 → 二段 を 8回続ける
        var fire = typeof(PlayerController).GetMethod("FireJump", NP);
        int j1 = SeN(SeId.Jump), j2 = SeN(SeId.DoubleJump), maxReal = 0;
        for (int i = 0; i < 8; i++)
        {
            float wg = 0f; while (!pc.IsGrounded && wg < 3f) { yield return null; wg += Time.unscaledDeltaTime; }
            fire.Invoke(pc, null);
            yield return new WaitForSecondsRealtime(0.18f);
            fire.Invoke(pc, null);
            yield return new WaitForSecondsRealtime(0.05f);
            maxReal = Mathf.Max(maxReal, Am.GetComponents<AudioSource>().Count(x => x.isPlaying && x.clip != null && x.clip.name.StartsWith("mv_") && x.clip.name.Contains("jump_v3")));
        }
        L($"[bv] real jumps: Jump x{SeN(SeId.Jump) - j1}, DoubleJump x{SeN(SeId.DoubleJump) - j2}, at most {maxReal} jump sounds at once");
        Check(SeN(SeId.Jump) - j1 >= 7 && SeN(SeId.DoubleJump) - j2 >= 7, "controls: the real jump code plays the jump and the double jump sounds");
        // FPS: ボスが居る時/居ない時
        float t = 0f; int frames = 0;
        while (t < 3f) { yield return null; t += Time.unscaledDeltaTime; frames++; }
        float fpsNoBoss = frames / t;
        BossManager.Instance.DebugSpawnBossForTest(2, (int)SkyBossKind.Leviathan);
        yield return new WaitForSecondsRealtime(2.5f);
        t = 0f; frames = 0;
        while (t < 3f) { yield return null; t += Time.unscaledDeltaTime; frames++; }
        float fpsBoss = frames / t;
        L($"[bv] fps: no boss {fpsNoBoss:F1}, boss + voices {fpsBoss:F1}");
        Check(fpsBoss > fpsNoBoss * 0.8f, $"controls: no big frame drop with a boss and its voice ({fpsNoBoss:F0} -> {fpsBoss:F0})");
        KillAllBosses();
        yield return new WaitForSecondsRealtime(1f);
        // ゲームオーバーで止まる
        BossVoice.Play("Sky/Behemoth", BossVoice.Moment.Roar);
        yield return new WaitForSecondsRealtime(0.2f);
        typeof(GameManager).GetProperty("IsGameOver").SetValue(gm, true);
        yield return null; yield return null;
        Check(!AnyVoicePlaying, "controls: game over stops the boss voices");
        typeof(GameManager).GetProperty("IsGameOver").SetValue(gm, false);
        yield return EndRun();
    }
}
#endif
