#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// エリアルコンボ改修(2026-10-18): -qaAerial <dir>
//   [-aeChars a,b] [-aeSpeeds 18,45,100,150] [-aeStages wasteland_road,natural_cave] [-aeTimings early,normal,late]
//   [-aeSeq U,F,F,F,U,F,F,F,D,F,F,F,D] [-aeNormalEnemy 1] [-aeFps 60]
// 自動前進したまま(実際のプレイと同じ)、前方の敵へフリックの列を入れ、1回ごとの結果を分けて記録する:
//   H = コンボの敵に当たった / Wf = 振ったが遠くて外れた / Wh = 振ったが高さがずれて外れた / W = 振ったが外れた(その他)
//   R = 何も出なかった(入力が受け付けられなかった) / K = 敵を倒した(その時点で撃破を確定)
// あわせて、瞬間移動・天井や地面へのめり込み・滞空の長さを記録する。
//  timing: 入力と入力の間 early 0.2秒 / normal 0.3秒 / late 0.42秒(技の長さ 約0.3〜0.4秒 に対して、早め/ちょうど/遅め)
//  -aeNormalEnemy 1: 普通の HP の敵(途中で倒れる → 撃破と報酬が1回だけか)も試す
// 出力: aerial.tsv(1行=1回)と qa_aerial.txt(まとめ)
public partial class QaSweep
{
    string aeChar;
    string aeDiag = "";
    float aeActualKmh;

    struct AeResult
    {
        public bool launched, slam, killed, spawned, groundSlam;
        public int airHits, rewardDelta, maxAirStage, chain, steps, hits, whiffFar, whiffHeight, whiffOther, rejected, queued, drops;
        public bool kept;
        public int terrainRetries;
        public float maxSep, maxJump, maxCeilOver, maxGroundUnder, maxAirTime;
        public string log;
    }

    static readonly Dictionary<string, float> AeStepWait = new Dictionary<string, float> { { "early", 0.2f }, { "normal", 0.3f }, { "late", 0.42f } };

    IEnumerator AerialMode()
    {
        HookBossLogs();
        Random.InitState(20261018);
        string[] chars = Arg("-aeChars", "swordsman,dual_blade,noble_lady,gunslinger,dragon_lancer,archer,mage,fighter,ninja,miko,vampire,dragonkin").Split(',');
        float[] speeds = Arg("-aeSpeeds", "18,45,100,150").Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        string[] stages = Arg("-aeStages", "wasteland_road,natural_cave").Split(',');
        string[] timings = Arg("-aeTimings", "early,normal,late").Split(',');
        string[] seq = Arg("-aeSeq", "U,F,F,F,U,F,F,F,D,F,F,F,D").Split(',');
        bool normalEnemy = Arg("-aeNormalEnemy", "1") == "1";
        int fps = int.Parse(Arg("-aeFps", "60"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = fps;
        L($"[aerial] fps={fps} seq={string.Join("", seq)} speeds={string.Join("/", speeds)} timings={string.Join("/", timings)}");
        var tsv = new StringBuilder("char\tstage\tkmh\tactualKmh\ttiming\tenemy\tfps\tlaunched\tkept\tdrops\tchain\tsteps\thits\twhiffFar\twhiffHeight\twhiffOther\tqueued\trejected\tkilled\tgroundSlam\trewardDelta\tmaxAirStage\tmaxJump\tceilOver\tgroundUnder\tairTime\tsequence\n");
        var sum = new Dictionary<string, (int n, int launch, int full, int chainSum, int stepSum)>();

        foreach (var ch in chars)
            foreach (var st in stages)
            {
                aeChar = ch;
                yield return AeBegin(ch, st);
                foreach (var kmh in speeds)
                    foreach (var tm in timings)
                    {
                        AeResult r = default;
                        yield return AeTrial(kmh, tm, false, seq, x => r = x);
                        AeRow(tsv, sum, ch, st, kmh, tm, "dummy", fps, r);
                    }
                if (normalEnemy)
                {
                    // 普通の敵: 打ち上げ → 前を何回か(倒れたらそこで撃破)
                    AeResult r = default;
                    yield return AeTrial(45f, "normal", true, new[] { "U", "F", "F", "F", "U", "F", "F" }, x => r = x);
                    AeRow(tsv, sum, ch, st, 45f, "normal", "normal", fps, r);
                    Check(!r.spawned || r.rewardDelta <= 1, $"aerial {ch}/{st}: a normal enemy gives at most one kill reward (got {r.rewardDelta})");
                }
                yield return AeEnd();
                System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "aerial.tsv"), tsv.ToString());
            }

        L($"== aerial summary (dummy enemy, fps {fps}): launched / full (the enemy stayed in the air until the last input and 60%+ of the inputs hit) / average hits of {seq.Length} inputs");
        foreach (var kv in sum)
        {
            var v = kv.Value;
            L($"[aerial] {kv.Key,-24} n={v.n} launched={v.launch} full={v.full} avgChain={(v.n > 0 ? (float)v.chainSum / v.n : 0f):F1}/{(v.n > 0 ? (float)v.stepSum / v.n : 0f):F0}");
        }
        L($"[aerial] flick buffer fired={PlayerController.FlickBufferFired} expired={PlayerController.FlickBufferExpired} relaunch={PlayerController.AerialRelaunches} diveReconnect={PlayerController.DiveReconnects} " +
          $"anchors={EnemyController.ComboAnchorsStarted} endFar={EnemyController.ComboAnchorEndedFar} endTimeout={EnemyController.ComboAnchorEndedTimeout} endLanded={EnemyController.ComboAnchorEndedLanded} " +
          $"downPush={EnemyController.ComboDownPushes} groundSlam={EnemyController.ComboGroundSlams} launchFx={AerialComboFx.Launches} slamFx={AerialComboFx.Slams} airMaxShown={ComboCounterUI.AirMaxShown}");
        Application.targetFrameRate = 60;
    }

    void AeRow(StringBuilder tsv, Dictionary<string, (int n, int launch, int full, int chainSum, int stepSum)> sum, string ch, string st, float kmh, string tm, string enemy, int fps, AeResult r)
    {
        tsv.Append($"{ch}\t{st}\t{kmh:F0}\t{aeActualKmh:F0}\t{tm}\t{enemy}\t{fps}\t{r.launched}\t{r.kept}\t{r.drops}\t{r.chain}\t{r.steps}\t{r.hits}\t{r.whiffFar}\t{r.whiffHeight}\t{r.whiffOther}\t{r.queued}\t{r.rejected}\t{r.killed}\t{r.groundSlam}\t{r.rewardDelta}\t{r.maxAirStage}\t{r.maxJump:F2}\t{r.maxCeilOver:F2}\t{r.maxGroundUnder:F2}\t{r.maxAirTime:F2}\t{r.log}\n");
        L($"[aerial] {ch,-13} {st,-15} {kmh,3:F0}km/h(実{aeActualKmh:F0}) {tm,-6} {enemy,-6} chain={r.chain}/{r.steps} hits={r.hits} far={r.whiffFar} height={r.whiffHeight} other={r.whiffOther} queued={r.queued} rejected={r.rejected} drops={r.drops} kept={r.kept} retry={r.terrainRetries} killed={r.killed} gslam={r.groundSlam} jump={r.maxJump:F2} ceil={r.maxCeilOver:F2} under={r.maxGroundUnder:F2} air={r.maxAirTime:F1}s  {r.log}");
        if (!r.spawned) { L($"[aerial]   (enemy could not be placed)"); return; }
        Check(r.maxJump < 1.2f, $"aerial {ch}/{st}/{kmh}/{tm}: no teleport-like jump of the enemy (max {r.maxJump:F2}m/frame)");
        Check(r.maxCeilOver < 0.15f, $"aerial {ch}/{st}/{kmh}/{tm}: the enemy does not go into the ceiling ({r.maxCeilOver:F2})");
        Check(r.maxGroundUnder < 0.25f, $"aerial {ch}/{st}/{kmh}/{tm}: the enemy does not sink into the ground ({r.maxGroundUnder:F2})");
        if (enemy != "dummy") return;
        string key = $"{ch} {kmh:F0}km/h";
        sum.TryGetValue(key, out var s);
        s.n++; if (r.launched) s.launch++; if (r.kept && r.hits >= r.steps * 0.6f) s.full++; s.chainSum += r.hits; s.stepSum += r.steps;
        sum[key] = s;
    }

    IEnumerator AeBegin(string ch, string stage)
    {
        yield return BeginRun(ch, stage);
        GameManager.BlockExpGain = true;
        SetGod(true);
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
        var tm = TerrainManager.Instance;
        if (tm != null) tm.enemySpawnChance = 0f;
        yield return new WaitForSecondsRealtime(0.3f);
    }

    IEnumerator AeEnd()
    {
        GameManager.BlockExpGain = false;
        PlayerController.DebugRunOnlyScale = 1f;
        SetGod(false);
        yield return EndRun();
    }

    static readonly System.Reflection.FieldInfo AeHp = typeof(EnemyController).GetField("hp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    // 走る速さを目標へ合わせる(数回で合わせる。合わせる前の速さが加速の途中などで低いと倍率が大きくなりすぎていた)
    IEnumerator AeSetSpeed(float kmh)
    {
        for (int it = 0; it < 4; it++)
        {
            float cur = Mathf.Max(0.1f, pc.CurrentAutoRunSpeed);
            PlayerController.DebugRunOnlyScale = Mathf.Clamp(PlayerController.DebugRunOnlyScale * (kmh / 3.6f) / cur, 0.2f, 12f);
            yield return null;
        }
    }

    IEnumerator AeTrial(float kmh, string timing, bool normalHp, string[] seq, System.Action<AeResult> done)
    {
        var r = new AeResult { steps = seq.Length };
        float stepWait = AeStepWait.TryGetValue(timing, out var sw) ? sw : 0.22f;
        // 前の試行の名残(着地/攻撃の終わり/魔法使いの高度)を片付ける
        if (aeChar == "mage") SetPrivate(pc, "mageLevel", 0);
        float w = 0f;
        while ((pc.IsAttacking || !pc.IsGrounded) && w < 3f) { yield return null; w += Time.deltaTime; }
        yield return AeSetSpeed(kmh);
        yield return new WaitForSeconds(0.25f);

        float speed = pc.CurrentAutoRunSpeed;
        aeActualKmh = speed * 3.6f;
        var en = HaSpawn(Mathf.Clamp(speed * 0.5f, 2.5f, 20f), "goblin");
        if (en == null) { done(r); yield break; }
        r.spawned = true;
        if (normalHp) { AeHp.SetValue(en, -1); HaEnsureHp.Invoke(en, null); } // 普通の HP へ戻す
        int killsBefore = gm.EnemyKillCount;
        var log = new StringBuilder();
        int hitsOnTarget = 0;
        EnemyController.LocalHit += OnHit;
        void OnHit(EnemyController e, PlayerAttackKind kind, bool wasLaunched, bool killed)
        {
            if (e != en) return;
            hitsOnTarget++;
            if (kind == PlayerAttackKind.Up && !wasLaunched) r.launched = true;
            if (wasLaunched && kind != PlayerAttackKind.Down && kind != PlayerAttackKind.DownImpact) r.airHits++;
            if (wasLaunched && (kind == PlayerAttackKind.Down || kind == PlayerAttackKind.DownImpact)) r.slam = true;
            if (killed) r.killed = true;
        }
        int slamsBefore = EnemyController.ComboGroundSlams;
        int expiredAtStart = PlayerController.FlickBufferExpired;
        try
        {
            float upReach = AeUpReach();
            // 地上から打ち上げる試験: 敵が届く所へ来た時にプレイヤーが地形で宙に浮いていたら(荒野の段差など)、置き直す(3回まで)
            for (int att = 0; att < 3; att++)
            {
                w = 0f; bool ready = false;
                while (en != null && en.isActiveAndEnabled && w < 4f)
                {
                    if (en.ComboAimPoint.x - pc.transform.position.x <= upReach + pc.CurrentAutoRunSpeed * 0.04f) { ready = pc.IsGrounded; break; } // 速いほど少し手前から(実際のプレイでも早めに入れる)
                    yield return null; w += Time.deltaTime;
                }
                if (ready || att == 2) break;
                r.terrainRetries++;
                if (en != null) Destroy(en.gameObject);
                w = 0f;
                while (!pc.IsGrounded && w < 2f) { yield return null; w += Time.deltaTime; }
                yield return new WaitForSeconds(0.2f);
                en = HaSpawn(Mathf.Clamp(pc.CurrentAutoRunSpeed * 0.5f, 2.5f, 20f), "goblin");
                if (en == null) break;
                if (normalHp) { AeHp.SetValue(en, -1); HaEnsureHp.Invoke(en, null); }
            }
            if (en == null) { done(r); yield break; }
            bool wasAir = false;
            Vector3 prevE = en != null ? en.transform.position : Vector3.zero, prevP = pc.transform.position;
            float airT = 0f;
            bool broken = false;
            for (int si = 0; si < seq.Length; si++)
            {
                if (en == null || !en.isActiveAndEnabled) { log.Append("K "); break; }
                var f = seq[si] == "U" ? PlayerController.FlickDirection.Up : seq[si] == "D" ? PlayerController.FlickDirection.Down : PlayerController.FlickDirection.Forward;
                int swing0 = PlayerAttackInfo.SwingCount, hits0 = hitsOnTarget;
                if (seq[si] == "T") { PlayerController.DebugTapNow = true; yield return null; yield return null; } // 2026-10-23: T = タップ攻撃
                else yield return HaFlick(f);
                float wait = si == 0 ? Mathf.Max(stepWait, 0.18f) : stepWait;
                float t = 0f;
                while (t < wait && en != null && en.isActiveAndEnabled)
                {
                    Vector3 pe = en.transform.position, pp = pc.transform.position;
                    // 瞬間移動 = 敵自身の動きも、プレイヤーとの相対の動きも、どちらも大きい(フレームの時間の跳び/並走を除く)
                    float jump = Mathf.Min((pe - prevE).magnitude, ((pe - prevE) - (pp - prevP)).magnitude);
                    bool playerWarped = (pp - prevP).magnitude > 4f;
                    jump *= 0.0167f / Mathf.Max(0.0167f, Time.deltaTime); // 60fps の1フレームあたりに直す(30fps で1フレームの移動が倍になる分)
                    if (Time.deltaTime > 0f && !playerWarped && jump > r.maxJump)
                    {
                        r.maxJump = jump;
                        if (jump > 1.2f) log.Append($"[JUMP {jump:F2} dt={Time.deltaTime:F3} e=({prevE.x:F1},{prevE.y:F1})->({pe.x:F1},{pe.y:F1}) p=({prevP.x:F1},{prevP.y:F1})->({pp.x:F1},{pp.y:F1}) {en.DebugAirState}] ");
                    }
                    prevE = pe; prevP = pp;
                    if (en.IsLaunchedForCombo) { airT += Time.deltaTime; r.maxAirTime = Mathf.Max(r.maxAirTime, airT); wasAir = true; }
                    else if (wasAir) { r.drops++; wasAir = false; log.Append($"(落 {en.ComboEndReason}) "); } // コンボの敵が着地した(締めの叩きつけを含む)
                    AeTerrainCheck(en, ref r);
                    yield return null; t += Time.deltaTime;
                }
                // この入力の結果
                string res;
                bool dead = en == null || !en.isActiveAndEnabled;
                if (hitsOnTarget > hits0) { res = dead ? "K" : "H"; r.hits++; }
                else if (dead) res = "K";
                else if (PlayerAttackInfo.SwingCount > swing0 || f == PlayerController.FlickDirection.Up && !pc.IsGrounded)
                {
                    Vector2 d = (Vector2)en.ComboAimPoint - (Vector2)pc.transform.position;
                    Vector2 sweet = pc.AerialComboSweetSpot();
                    // 遠い/高さ: そのキャラの当たる所(狙いの位置)からのずれで分ける
                    if (Mathf.Abs(d.x - sweet.x) > 1.6f) { res = $"Wf({d.x:F1})"; r.whiffFar++; }
                    else if (Mathf.Abs(d.y - sweet.y) > 1.3f) { res = $"Wh({d.y:F1})"; r.whiffHeight++; }
                    else { res = $"W({d.x:F1},{d.y:F1}{(en.IsComboAnchored ? "" : ",noanchor")})"; r.whiffOther++; }
                }
                else { res = "q"; r.queued++; } // まだ出ていない(前の技の最中で先行入力として待っている)
                log.Append($"{seq[si]}:{res} ");
                if (!broken && (res == "H" || res == "K" || res == "q")) { if (res != "q") r.chain++; } else broken = true;
                if (dead) { r.killed = true; break; }
            }
            // 着地/落下まで見届ける
            w = 0f;
            while (en != null && en.isActiveAndEnabled && en.IsLaunchedForCombo && w < 4f) { AeTerrainCheck(en, ref r); yield return null; w += Time.deltaTime; }
            yield return new WaitForSeconds(0.4f);
        }
        finally { EnemyController.LocalHit -= OnHit; }
        r.groundSlam = EnemyController.ComboGroundSlams > slamsBefore || r.slam;
        r.rejected = PlayerController.FlickBufferExpired - expiredAtStart; // 入力の不受理 = 覚えていた入力が出ないまま消えた数
        r.rewardDelta = en != null ? en.RewardsGranted : gm.EnemyKillCount - killsBefore; // この敵の報酬の回数(仲間が他の敵を倒した分は数えない)
        r.maxAirStage = en != null ? en.ComboAirHits : 0;
        r.log = log.ToString() + aeDiag; aeDiag = "";
        r.kept = r.launched && (r.drops <= 1 || r.killed); // 最後の入力まで空中に残った(落ちたのは締めの叩きつけだけ)
        if (en != null && en.gameObject.activeSelf) Destroy(en.gameObject);
        done(r);
    }

    void AeTerrainCheck(EnemyController en, ref AeResult r)
    {
        var tm = TerrainManager.Instance;
        if (tm == null || en == null) return;
        var col = HaBodyOf(en);
        if (col == null) return;
        if (!en.IsLaunchedForCombo) return; // 打ち上げ中だけ(地上に立つ敵が低い天井に頭を入れているのは元からの配置)
        float x = col.bounds.center.x;
        float? ceil = tm.GetEffectiveCeilingHeightAt(x);
        if (ceil.HasValue && col.bounds.max.y - ceil.Value > r.maxCeilOver)
        {
            r.maxCeilOver = col.bounds.max.y - ceil.Value;
            if (r.maxCeilOver > 0.15f) aeDiag = $"[CEIL top={col.bounds.max.y:F2} ceil={ceil.Value:F2} x={x:F1} {en.DebugAirState}]";
        }
        float? g = tm.GetHeightAt(en.transform.position.x); // 一番下の地面(上ルート/浮島はこれより上) - これより下へ沈んでいたらめり込み
        if (g.HasValue && g.Value - en.transform.position.y > r.maxGroundUnder)
        {
            r.maxGroundUnder = g.Value - en.transform.position.y;
            if (r.maxGroundUnder > 0.25f) aeDiag = $"[UNDER y={en.transform.position.y:F2} ground={g.Value:F2} x={en.transform.position.x:F1} {en.DebugAirState}]";
        }
    }

    // 地上の上攻撃が届く前方の距離(体の中心まで)。遠距離の上撃ちは斜めに飛ぶので少し先から
    float AeUpReach()
    {
        switch (aeChar)
        {
            case "gunslinger": case "archer": case "mage": case "miko": return 1.6f;
            case "dragon_lancer": return 1.6f;
            case "ninja": case "vampire": return 1.9f; // 前へ進みながら打ち上げる
            default: return 1.25f;
        }
    }

    // ---------------------------------------------------------------- 比較動画
    // -qaAerialVideo <dir> [-avChars a,b] [-avKmh 45] [-avSeq U,F,F,F,U,F,F,F,D,F,F,F,D]: 30fps で毎フレーム書き出す。
    // キャラごとに「検証用の敵(HP が多い)」と「普通の敵」を1回ずつ。画面の左に入力と速さを出す。区切りは video.tsv
    string aeOverlay;
    IEnumerator AerialVideoMode()
    {
        HookBossLogs();
        Random.InitState(20261018);
        string[] chars = Arg("-avChars", "swordsman,dual_blade,noble_lady,gunslinger,dragon_lancer,archer,mage,fighter,ninja,miko,vampire,dragonkin").Split(',');
        float kmh = float.Parse(Arg("-avKmh", "45"), System.Globalization.CultureInfo.InvariantCulture);
        string[] seq = Arg("-avSeq", "U,F,F,F,U,F,F,F,D,F,F,F,D").Split(',');
        demoDir = System.IO.Path.Combine(outDir, "frames");
        System.IO.Directory.CreateDirectory(demoDir);
        var tsv = new StringBuilder("char\tenemy\tfrom\tto\tvx\tvy\tvyMin\tvyMax\tkmh\tresult\n");
        foreach (var ch in chars)
        {
            aeChar = ch;
            yield return AeBegin(ch, "wasteland_road");
            foreach (bool normal in new[] { false, true })
            {
                if (aeChar == "mage") SetPrivate(pc, "mageLevel", 0);
                float w = 0f;
                while ((pc.IsAttacking || !pc.IsGrounded) && w < 3f) { yield return null; w += Time.deltaTime; }
                yield return AeSetSpeed(kmh);
                yield return new WaitForSeconds(0.3f);
                var en = HaSpawn(Mathf.Clamp(pc.CurrentAutoRunSpeed * 0.75f, 3f, 20f), "goblin");
                if (en == null) continue;
                if (normal) { AeHp.SetValue(en, -1); HaEnsureHp.Invoke(en, null); }
                var log = new StringBuilder();
                void OnHit(EnemyController e, PlayerAttackKind kind, bool wasLaunched, bool killed) { if (e == en) log.Append($"{kind}{(wasLaunched ? "(air)" : "")}{(killed ? "!" : "")} "); }
                EnemyController.LocalHit += OnHit;
                int from = demoFrame;
                Time.captureFramerate = 30;
                var vp = new List<Vector3>();
                aeOverlay = $"{(normal ? "普通の敵" : "検証用の敵(HPが多い)")}  {pc.CurrentAutoRunSpeed * 3.6f:F0}km/h\n入力: ";
                var inputs = StartCoroutine(AeVideoInputs(en, seq));
                int n = Mathf.RoundToInt((0.6f + seq.Length * 0.24f + 1.0f) * 30f);
                for (int i = 0; i < n; i++)
                {
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(demoDir, $"f{demoFrame:00000}.png"));
                    demoFrame++;
                    var cam = Camera.main;
                    if (cam != null) vp.Add(cam.WorldToViewportPoint(pc.transform.position + new Vector3(0.8f, 0.9f, 0f)));
                }
                Time.captureFramerate = 0;
                StopCoroutine(inputs);
                aeOverlay = null;
                EnemyController.LocalHit -= OnHit;
                float vx = vp.Count > 0 ? vp.Average(v => v.x) : 0.4f, vy = vp.Count > 0 ? vp.Average(v => v.y) : 0.4f;
                float vyMin = vp.Count > 0 ? vp.Min(v => v.y) : 0.3f, vyMax = vp.Count > 0 ? vp.Max(v => v.y) : 0.6f;
                tsv.Append($"{ch}\t{(normal ? "normal" : "dummy")}\t{from}\t{demoFrame - 1}\t{vx:F3}\t{vy:F3}\t{vyMin:F3}\t{vyMax:F3}\t{kmh:F0}\t{log}\n");
                L($"[aerialVideo] {ch} {(normal ? "normal" : "dummy")} frames {from}-{demoFrame - 1}: {log}");
                if (en != null && en.gameObject.activeSelf) Destroy(en.gameObject);
                System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "video.tsv"), tsv.ToString());
            }
            yield return AeEnd();
        }
        Check(demoFrame > 0, "aerial video: frames written");
    }

    IEnumerator AeVideoInputs(EnemyController en, string[] seq)
    {
        float upReach = AeUpReach(), w = 0f;
        while (en != null && en.isActiveAndEnabled && w < 2f)
        {
            if (en.ComboAimPoint.x - pc.transform.position.x <= upReach) break;
            yield return null; w += Time.deltaTime;
        }
        for (int i = 0; i < seq.Length; i++)
        {
            if (en == null || !en.isActiveAndEnabled) yield break;
            var f = seq[i] == "U" ? PlayerController.FlickDirection.Up : seq[i] == "D" ? PlayerController.FlickDirection.Down : PlayerController.FlickDirection.Forward;
            aeOverlay += seq[i] == "U" ? "↑" : seq[i] == "D" ? "↓" : seq[i] == "T" ? "・" : "→";
            if (seq[i] == "T") { PlayerController.DebugTapNow = true; yield return null; yield return null; }
            else yield return HaFlick(f);
            yield return new WaitForSeconds(i == 0 ? 0.24f : 0.22f);
        }
    }

    GUIStyle aeStyle;
    void OnGUI()
    {
        if (string.IsNullOrEmpty(aeOverlay)) return;
        if (aeStyle == null) aeStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        aeStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.035f);
        var r = new Rect(Screen.width * 0.02f, Screen.height * 0.40f, Screen.width * 0.6f, Screen.height * 0.2f); // 左上の HUD(距離/速さ/MILE)と重ならない高さ
        aeStyle.normal.textColor = Color.black;
        GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), aeOverlay, aeStyle);
        aeStyle.normal.textColor = new Color(1f, 0.95f, 0.6f);
        GUI.Label(r, aeOverlay, aeStyle);
    }
}
#endif
