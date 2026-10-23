#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;

// Steam のストア素材の撮影(2026-10-23): -storeCapture <dir> [-scShots a,b,...] [-scLang ja|en] [-scFfmpeg <path>]
// 撮影用ビルド(BuildStoreCapture: 開発版の透かし無し)で、実際のゲームの場面を作って 60fps で書き出す。
//  ・場面づくり: 距離ワープ / 敵やボスを目の前に置く / レベルアップ(実際の3択から選ぶ)/ 補助(製品にもある自動のジャンプ/攻撃)
//  ・映像: 毎フレームの画面を ffmpeg へ渡して raw/<shot>.mkv(劣化なし、RGB)。音: AudioRenderer で同じ時間の流れ → raw/<shot>.wav
//    音は SE/環境音だけ(-scBgm 1 で BGM/ジングルも)。紹介動画では場面が切り替わっても途切れないよう、ゲームの BGM を1本通しで重ねるため
//  ・静止画: stills/<shot>_<n>.png(撮りたい瞬間に印を付けた所)
//  ・撮影の間は開発用の表示(build の番号/DEV BUILD/TEST FILE)を出さない(StoreCapture.Clean)
// 撮る場面は SteamShots(下)。どれも製品版で普通に遊んで起きる場面(ボスは関門と同じ出し方、カードは実際の選択)
public partial class QaSweep
{
    string scFfmpeg;
    bool scRecStop, scRecBusy;
    string scStillName; // 次に書き出すフレームを静止画にも
    bool scGuideHold;   // 脱出の説明を自動で閉じない(撮る間)
    bool scMuteBgm = true;
    static readonly System.Reflection.FieldInfo ScBgmField = typeof(AudioManager).GetField("bgm", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    static readonly System.Reflection.FieldInfo ScJingleField = typeof(AudioManager).GetField("jingleSource", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    // BGM/ジングルの音を止める(音量の設定は変えない。AudioSource.mute だけ)
    void ScApplyBgmMute()
    {
        var am = AudioManager.Instance;
        if (am == null) return;
        if (ScBgmField?.GetValue(am) is AudioSource[] arr) foreach (var a in arr) if (a != null) a.mute = scMuteBgm;
        if (ScJingleField?.GetValue(am) is AudioSource j && j != null) j.mute = scMuteBgm;
    }
    int scStillNo;
    readonly List<string> scIndex = new List<string>();

    IEnumerator StoreCaptureMode()
    {
        StoreCapture.Clean = true;
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        gm = GameManager.Instance;
        bool safe = FileSaveStore.Active || MemorySaveStore.Active;
        Check(safe, "runs on a dedicated save only");
        if (!safe) yield break;
        scFfmpeg = Arg("-scFfmpeg", "ffmpeg");
        scMuteBgm = Arg("-scBgm", "0") != "1";
        string lang = Arg("-scLang", "ja");
        if (Loc.Current != lang) Loc.Set(lang, false);
        L($"[capture] screen {Screen.width}x{Screen.height} lang={Loc.Current} debugBuild={Debug.isDebugBuild} devGate={DevGate.On} audio {AudioSettings.outputSampleRate}Hz {AudioSettings.speakerMode}");
        Check(Screen.width == 1920 && Screen.height == 1080, $"the screen is 1920x1080 (got {Screen.width}x{Screen.height})");
        ScPrepareSave();
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(outDir, "raw"));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(outDir, "stills"));
        string[] shots = Arg("-scShots", "run_wasteland,aerial,cards,cave,sky,gunslinger,crowd,boss").Split(',');
        foreach (var s in shots)
        {
            L($"== shot {s} ==");
            IEnumerator body = s switch
            {
                "run_wasteland" => ScRunShot(s, "swordsman", "wasteland_road", 24000f, 7, 14f),
                "run_day" => ScRunShot(s, "swordsman", "wasteland_road", 6150f, 6, 14f),
                "dual" => ScRunShot(s, "dual_blade", "wasteland_road", 38150f, 8, 12f),
                "boss_hydra" => ScBossShot(s, "ninja", "wasteland_road", 0, (int)WildBossKind.Hydra, 30150f),
                "cave" => ScRunShot(s, "ninja", "natural_cave", 18000f, 7, 14f),
                "sky" => ScRunShot(s, "mage", "sky_corridor", 18000f, 7, 14f),
                "gunslinger" => ScRunShot(s, "gunslinger", "wasteland_road", 52000f, 8, 12f),
                "noble" => ScRunShot(s, "noble_lady", "natural_cave", 33000f, 8, 12f),
                "miko" => ScRunShot(s, "miko", "sky_corridor", 41000f, 8, 12f),
                "aerial" => ScAerialShot(s, "swordsman"),
                "aerial_dual" => ScAerialShot(s, "dual_blade"),
                "crowd" => ScCrowdShot(s, "swordsman"),
                "cards" => ScCardsShot(s),
                "boss" => ScBossShot(s, "swordsman", "wasteland_road", 0, (int)WildBossKind.Cyclops, 12150f),
                "boss_cave" => ScBossShot(s, "dual_blade", "natural_cave", 1, (int)CaveBossKind.CrystalGolem, 16150f),
                "boss_sky" => ScBossShot(s, "archer", "sky_corridor", 2, (int)SkyBossKind.Behemoth, 16150f, false), // 天空は足場が細く、脱出の溜め(補助のジャンプが止まる)で落ちる
                "start" => ScStartShot(s, "swordsman"),
                _ => null,
            };
            if (body == null) { L($"[capture] unknown shot {s}"); continue; }
            yield return body;
            if (scRecBusy) yield return ScRecStop();
            System.IO.File.WriteAllLines(System.IO.Path.Combine(outDir, "index.tsv"), scIndex);
        }
        StoreCapture.Clean = false;
    }

    // ---------------------------------------------------------------- 録画
    void ScRecStart(string name) { scRecStop = false; scRecBusy = true; StartCoroutine(ScRecLoop(name)); }
    IEnumerator ScRecStop() { scRecStop = true; while (scRecBusy) yield return null; }
    IEnumerator ScRecord(string name, float seconds) { ScRecStart(name); yield return new WaitForSeconds(seconds); yield return ScRecStop(); }
    void ScStill(string tag) { scStillName = tag; }

    static int ScChannels(AudioSpeakerMode m) => m switch
    {
        AudioSpeakerMode.Mono => 1, AudioSpeakerMode.Quad => 4, AudioSpeakerMode.Surround => 5,
        AudioSpeakerMode.Mode5point1 => 6, AudioSpeakerMode.Mode7point1 => 8, _ => 2,
    };

    IEnumerator ScRecLoop(string name)
    {
        string raw = System.IO.Path.Combine(outDir, "raw");
        Time.captureFramerate = 60;
        int ch = ScChannels(AudioSettings.speakerMode), rate = AudioSettings.outputSampleRate;
        var pcm = new List<short>(rate * ch * 30);
        AudioRenderer.Start();
        System.Diagnostics.Process ff = null;
        System.IO.Stream ffIn = null;
        byte[] buf = null;
        int frames = 0;
        float t0 = Time.time;
        while (!scRecStop)
        {
            ScApplyBgmMute();
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (ff == null)
            {
                string pix = tex.format == TextureFormat.RGB24 ? "rgb24" : tex.format == TextureFormat.ARGB32 ? "argb" : tex.format == TextureFormat.BGRA32 ? "bgra" : "rgba";
                var psi = new System.Diagnostics.ProcessStartInfo(scFfmpeg,
                    $"-y -loglevel error -f rawvideo -pix_fmt {pix} -s {tex.width}x{tex.height} -r 60 -i - -vf vflip -c:v libx264rgb -preset ultrafast -qp 0 \"{System.IO.Path.Combine(raw, name + ".mkv")}\"")
                { UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true };
                ff = System.Diagnostics.Process.Start(psi);
                ffIn = ff.StandardInput.BaseStream;
                L($"[capture] {name}: {tex.width}x{tex.height} {tex.format} -> {pix}");
            }
            var data = tex.GetRawTextureData<byte>();
            if (buf == null || buf.Length != data.Length) buf = new byte[data.Length];
            data.CopyTo(buf);
            ffIn.Write(buf, 0, buf.Length);
            if (scStillName != null)
            {
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, "stills", $"{name}_{scStillNo++:00}_{scStillName}.png"), tex.EncodeToPNG());
                L($"[capture] still {name} {scStillName} @frame {frames}");
                scStillName = null;
            }
            Destroy(tex);
            int n = AudioRenderer.GetSampleCountForCaptureFrame() * ch;
            using (var ab = new NativeArray<float>(n, Allocator.Temp))
            {
                AudioRenderer.Render(ab);
                for (int i = 0; i < n; i++) pcm.Add((short)Mathf.Clamp(Mathf.RoundToInt(ab[i] * 32767f), -32768, 32767));
            }
            frames++;
        }
        AudioRenderer.Stop();
        Time.captureFramerate = 0;
        if (ffIn != null) { ffIn.Flush(); ffIn.Close(); }
        if (ff != null) { ff.WaitForExit(120000); }
        ScWriteWav(System.IO.Path.Combine(raw, name + ".wav"), pcm, ch, rate);
        float secs = frames / 60f;
        L($"[capture] {name}: {frames} frames ({secs:F1}s, game time {Time.time - t0:F1}s), audio {pcm.Count / (float)ch / rate:F1}s");
        Check(frames > 0, $"{name}: frames written");
        scIndex.Add($"{name}\t{frames}\t{secs:F2}");
        scRecBusy = false;
    }

    static void ScWriteWav(string path, List<short> pcm, int ch, int rate)
    {
        using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Create))
        using (var bw = new System.IO.BinaryWriter(fs))
        {
            int bytes = pcm.Count * 2;
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); bw.Write(36 + bytes); bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt ")); bw.Write(16); bw.Write((short)1); bw.Write((short)ch); bw.Write(rate); bw.Write(rate * ch * 2); bw.Write((short)(ch * 2)); bw.Write((short)16);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data")); bw.Write(bytes);
            foreach (var s in pcm) bw.Write(s);
        }
    }

    // ---------------------------------------------------------------- 場面づくり
    // 撮影用の保存(新規)を、遊び込んだ人の状態へ: 全体の BEST 40km 台、ガチャで集めたカードのデッキ(炎/雷/氷/風など)。
    // レベルアップの3択はデッキから出るので、新規の初期デッキ(基本のカードだけ)では製品の中盤の見た目にならない
    static readonly string[] ScDeck = { "flame_blade", "thunder_strike", "frost_edge", "wind_cutter", "chain_lightning", "inferno", "tornado", "double_attack", "attack_speed_up", "attack_up", "heart_up", "vampire" };
    void ScPrepareSave()
    {
        gm.DebugSetBestDistance(41872.6f);
        var ids = new List<string>();
        foreach (var id in ScDeck) if (CardDatabase.FindById(id) != null) { CardInventory.AddCard(id, 1, 1); ids.Add(id); }
        gm.SetDeck(ids);
        L($"[capture] save: BEST {gm.BestDistance:F0}m, deck {string.Join(",", gm.DeckCards)}");
    }

    // レベルアップ/報酬の3択から、見た目の分かりやすいカード(属性/攻撃)を選ぶ。無ければ左
    static readonly string[] ScPickPrefer = { "inferno", "tornado", "chain_lightning", "flame_blade", "thunder_strike", "frost_edge", "wind_cutter", "double_attack", "attack_speed_up", "attack_up" };
    static readonly System.Reflection.FieldInfo ScCurrentCards = typeof(RewardCardSequence).GetField("currentCardData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    int ScPickIndex(RewardCardSequence seq)
    {
        if (seq == null || !(ScCurrentCards?.GetValue(seq) is RewardCardData[] cards) || cards.Length == 0) return 0;
        foreach (var want in ScPickPrefer)
            for (int i = 0; i < cards.Length; i++) if (cards[i].CardId == want) return i;
        return 0;
    }

    // 撮影区間の地形: 論理 X の範囲だけ、平地の一本道(穴/坂/上下の分かれ道/空中足場なし。闘技場の地形と同じ設定を区間だけ)
    static readonly System.Reflection.FieldInfo ScRouteBranch = typeof(TerrainManager).GetField("routeBranchEnabled", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    float ScFlatStage(float from, float length)
    {
        var tm = TerrainManager.Instance;
        float lx = FloatingOrigin.ToLogical(pc.transform.position.x);
        float a = lx + from, b = lx + from + length;
        if (tm != null)
        {
            tm.forceFlatAt = x => x >= a - 40f && x <= b;
            tm.skyAllowedAt = x => x < a - 60f || x > b;
            ScRouteBranch?.SetValue(tm, false);
            tm.SetResumeFlatZone(a - 40f, b);
        }
        return a;
    }
    // 新しいランを始め、ワープ → レベル(実際の3択から選ぶ)→ 補助 ON(製品の設定と同じ自動のジャンプ/攻撃)
    IEnumerator ScBegin(string ch, string stage, float distance, int level, bool assist = true)
    {
        // 新しい保存なので BEST が 0 m のまま → 遊び込んだ人の記録らしい値(撮る距離より先、100km 未満)
        GameManager.WriteStageBest(stage, System.Math.Round(Mathf.Clamp(distance * 1.37f + 4127.4f, 24000f, 97000f), 2));
        yield return BeginRun(ch, stage);
        stopKeepAlive = true; // HP を 990 にする試験用の保ち方は使わない(HUD の HP が不自然になる)
        SetGod(true);         // 撮影中に倒れない(被弾の見た目は出る)
        var hsa = HighSpeedAssist.Instance;
        if (hsa != null) { hsa.SetEnabled(assist); hsa.SetAutoAttack(assist); }
        if (distance > 0f) { gm.DebugWarpToDistance(distance); yield return new WaitForSeconds(0.6f); }
        var gain = typeof(GameManager).GetMethod("GainExp", NP);
        for (int k = 0; k < 40 && gm.Level < level; k++)
        {
            gain.Invoke(gm, new object[] { gm.ExpToNext + 1f });
            float w = 0f;
            while ((gm.IsLocalChoiceOpen || gm.IsRewardSequenceWaitingForSelection) && w < 8f) { yield return null; w += Time.unscaledDeltaTime; }
            yield return new WaitForSecondsRealtime(0.4f);
        }
        L($"[capture] {ch} {stage} at {gm.MaxDistance:F0}m Lv{gm.Level} speed {pc.CurrentAutoRunSpeed * 3.6f:F0}km/h");
    }

    IEnumerator ScEnd()
    {
        if (gm != null) SetGod(false);
        yield return EndRun();
    }

    // 待つ: 録画中は動画の時間(1/60 秒のフレーム)で数える。一時停止(カードの選択/説明)の間も進む。
    // 録画していない時は実時間(WaitForSeconds は一時停止の間ずっと止まるため使わない)
    IEnumerator ScWait(float seconds)
    {
        if (scRecBusy) { int n = Mathf.RoundToInt(seconds * 60f); for (int i = 0; i < n; i++) yield return null; }
        else yield return new WaitForSecondsRealtime(seconds);
    }

    // 少し先を平らな地面にする(穴/坂を作らない区間。CONTINUE の足場と同じ仕組み)。区間の始まり(論理 X)を返す
    float ScFlatAhead(float from, float length)
    {
        var tm = TerrainManager.Instance;
        float lx = FloatingOrigin.ToLogical(pc.transform.position.x);
        if (tm != null) tm.SetResumeFlatZone(lx + from, lx + from + length);
        return lx + from;
    }

    IEnumerator ScWaitReach(float logicalX, float max = 30f)
    {
        float w = 0f;
        while (FloatingOrigin.ToLogical(pc.transform.position.x) < logicalX && w < max) { yield return null; w += Time.unscaledDeltaTime; }
    }

    // 走りながら戦う(補助 ON の実際のプレイ)
    IEnumerator ScRunShot(string name, string ch, string stage, float distance, int level, float seconds)
    {
        yield return ScBegin(ch, stage, distance, level);
        yield return new WaitForSeconds(3f); // ワープ直後の生成が落ち着くまで
        ScRecStart(name);
        for (float t = 1f; t <= seconds; t += 1f)
        {
            yield return ScWait(1f); ScStill($"t{t:00}");
        }
        yield return ScRecStop();
        yield return ScEnd();
    }

    // 打ち上げ → 空中コンボ(手で入れる操作。補助は切る)。2km 付近(45km/h 前後、カメラが寄っている)の平らな所で
    IEnumerator ScAerialShot(string name, string ch)
    {
        yield return ScBegin(ch, "wasteland_road", 1900f, 5, true); // 区間までは補助 ON(穴を越える)
        var tm = TerrainManager.Instance;
        if (tm != null) tm.enemySpawnChance = 0f;
        float zone = ScFlatStage(260f, 260f);
        yield return ScWaitReach(zone + 10f);
        if (HighSpeedAssist.Instance != null) { HighSpeedAssist.Instance.SetEnabled(false); HighSpeedAssist.Instance.SetAutoAttack(false); }
        yield return HaWaitIdle();
        float d = Mathf.Clamp(pc.CurrentAutoRunSpeed * 0.9f, 6f, 14f);
        var en = HaSpawn(d, "goblin_elite");
        var en2 = HaSpawn(d + 3.0f, "goblin");
        var en3 = HaSpawn(d + 5.5f, "goblin");
        foreach (var e in new[] { en, en2, en3 }) if (e != null) { AeHp.SetValue(e, -1); HaEnsureHp.Invoke(e, null); }
        ScRecStart(name);
        float upReach = AeUpReach(), w = 0f;
        while (en != null && en.isActiveAndEnabled && en.ComboAimPoint.x - pc.transform.position.x > upReach + 1.5f && w < 3f) { yield return null; w += Time.deltaTime; }
        string[] seq = { "F", "U", "F", "F", "F", "U", "F", "F", "D", "F", "F" };
        for (int i = 0; i < seq.Length; i++)
        {
            var f = seq[i] == "U" ? PlayerController.FlickDirection.Up : seq[i] == "D" ? PlayerController.FlickDirection.Down : PlayerController.FlickDirection.Forward;
            yield return HaFlick(f);
            yield return ScWait(i == 1 || i == 5 ? 0.3f : 0.26f);
            if (i == 3 || i == 4 || i == 6 || i == 7) ScStill($"air{i}");
        }
        yield return ScWait(1.4f);
        yield return ScRecStop();
        if (tm != null) tm.enemySpawnChance = 1f;
        yield return ScEnd();
    }

    // まとめて吹き飛ばす: 平らな所で敵の群れへ(補助 ON、レベルアップで取った追加攻撃つき)
    IEnumerator ScCrowdShot(string name, string ch)
    {
        yield return ScBegin(ch, "wasteland_road", 2500f, 9);
        float zone = ScFlatStage(260f, 300f);
        yield return ScWaitReach(zone + 10f);
        float ahead = Mathf.Clamp(pc.CurrentAutoRunSpeed * 1.2f, 10f, 22f);
        for (int i = 0; i < 7; i++)
        {
            var e = HaSpawn(ahead + i * 1.2f, i % 3 == 0 ? "goblin_elite" : "goblin");
            if (e != null) { AeHp.SetValue(e, -1); HaEnsureHp.Invoke(e, null); var sb = e.GetComponent<EnemySpecialBehavior>(); if (sb != null) sb.enabled = true; }
        }
        ScRecStart(name);
        for (int i = 0; i < 8; i++) { yield return ScWait(0.35f); if (i >= 2) ScStill($"c{i}"); }
        yield return ScWait(2.2f);
        yield return ScRecStop();
        yield return ScEnd();
    }

    // レベルアップの3択(実際の選択画面)。選んで閉じるまで
    IEnumerator ScCardsShot(string name)
    {
        yield return ScBegin("swordsman", "wasteland_road", 14000f, 4);
        yield return new WaitForSeconds(2f);
        autoPickHold = true;
        ScRecStart(name);
        yield return ScWait(1.5f);
        typeof(GameManager).GetMethod("GainExp", NP).Invoke(gm, new object[] { gm.ExpToNext + 1f });
        float w = 0f;
        while (!gm.IsRewardSequenceWaitingForSelection && w < 6f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return ScWait(1.0f);
        ScStill("choice");
        var seq = FindFirstObjectByType<RewardCardSequence>();
        int pick = ScPickIndex(seq);
        L($"[capture] cards: pick #{pick}");
        yield return ScWait(1.0f);
        if (seq != null) seq.OnCardClicked(pick);
        yield return ScWait(0.7f);
        ScStill("picked");
        yield return ScWait(0.6f);
        if (seq != null) seq.OnCardClicked(pick);
        yield return ScWait(2.5f);
        yield return ScRecStop();
        autoPickHold = false;
        yield return ScEnd();
    }

    // ボス戦: 関門と同じ出し方(BossManager の関門の生成)。撃破 → 報酬 → 脱出の説明 → 長押しで脱出 → RESULT
    // 関門は 1km ごと。次の関門の前に脱出まで終えるよう、関門の直後から始める
    IEnumerator ScBossShot(string name, string ch, string stage, int family, int kind, float distance, bool escape = true)
    {
        yield return ScBegin(ch, stage, distance, 10);
        yield return new WaitForSeconds(1.5f);
        var bm = FindFirstObjectByType<BossManager>();
        if (bm == null) { L("[capture] no BossManager"); yield return ScEnd(); yield break; }
        bm.DebugSpawnBossForTest(family, kind);
        yield return null;
        autoPickHold = true;
        scGuideHold = true;
        ScRecStart(name + "_fight");
        float t = 0f;
        int stills = 0;
        while (t < 120f)
        {
            yield return ScWait(0.5f); t += 0.5f;
            if (t >= 2f && stills < 12 && Mathf.Repeat(t, 2f) < 0.25f) { ScStill($"t{t:00}"); stills++; }
            bool alive = GameObject.FindGameObjectsWithTag("Boss").Any(o => o.activeInHierarchy);
            if (!alive || gm.IsRewardSequenceWaitingForSelection) break;
        }
        L($"[capture] {name}: boss fight {t:F1}s, boss alive={GameObject.FindGameObjectsWithTag("Boss").Any(o => o.activeInHierarchy)} at {gm.MaxDistance:F0}m");
        // 撃破の演出 → 報酬の選択
        float w = 0f;
        while (!gm.IsRewardSequenceWaitingForSelection && w < 12f) { yield return ScWait(0.1f); w += 0.1f; }
        yield return ScWait(1.2f);
        ScStill("reward");
        var seq = FindFirstObjectByType<RewardCardSequence>();
        int pick = ScPickIndex(seq);
        if (seq != null) { seq.OnCardClicked(pick); yield return ScWait(0.6f); seq.OnCardClicked(pick); }
        yield return ScWait(0.6f);
        autoPickHold = false; // この後のレベルアップ(撃破の EXP)は自動で選ぶ
        // 初めての脱出の説明(製品でも初めての1回だけ出る)
        w = 0f;
        while (!FirstRunGuide.Open && w < 6f) { yield return ScWait(0.1f); w += 0.1f; }
        if (FirstRunGuide.Open)
        {
            yield return ScWait(1.2f);
            ScStill("escape_guide");
            yield return ScWait(1.8f);
            FirstRunGuideDebug.Answer(0);
        }
        scGuideHold = false;
        yield return ScRecStop();
        if (!escape) { yield return ScEnd(); yield break; }
        // 平らな所で長押し → 脱出 → RESULT(脱出の溜めの間は補助のジャンプが止まるので、穴の無い所で)
        float zone = ScFlatAhead(90f, 220f);
        yield return ScWaitReach(zone);
        L($"[capture] escape from {gm.MaxDistance:F0}m (available={gm.EscapeAvailable})");
        ScRecStart(name + "_escape");
        yield return ScWait(0.8f);
        for (int f = 0; f < 60 * 4 && !gm.IsGameOver; f++)
        {
            GameInput.Inject(GameAction.Hold);
            if (f == 60 * 2) ScStill("escape_hold");
            yield return null;
        }
        w = 0f;
        while (!gm.IsGameOver && w < 8f) { yield return ScWait(0.1f); w += 0.1f; }
        L($"[capture] escape: game over={gm.IsGameOver}");
        yield return ScWait(2.5f);
        ScStill("result");
        yield return ScWait(1.0f);
        yield return ScRecStop();
        yield return ScEnd();
    }

    // 出発(カウントダウン → 走り出し)
    IEnumerator ScStartShot(string name, string ch)
    {
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        gm.SetSelectedCharacter(ch);
        gm.SetSelectedStage("wasteland_road");
        ScRecStart(name);
        typeof(GameManager).GetMethod("StartGame", NP).Invoke(gm, null);
        yield return new WaitForSeconds(7f);
        yield return ScRecStop();
        pc = PlayerController.Instance;
        yield return EndRun();
    }
}
#endif
