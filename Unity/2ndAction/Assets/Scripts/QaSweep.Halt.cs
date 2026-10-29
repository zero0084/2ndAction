#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// ボス戦の停止戦(2026-10-29): -qaHalt <dir> [-hCases wasteland_road:swordsman,…] [-hKmh 45,150] [-hFight 10] [-haltOff 1]
// 実際の関門(1,000m)のボス戦で: 関門 → 止まる(地形・残った雑魚/障害物・能力の速さ) → 戦う(前/後ろの攻撃が当たるか・
// ボスがこちらを向いたまま後ろへ走っていないか) → 撃破 → 報酬 → 走りへ戻る(停止の状態が残らない)を確かめる。
// -haltOff 1 で前の作り(並走するボス戦)と比べる
public partial class QaSweep
{
    IEnumerator HaltMode()
    {
        Application.targetFrameRate = 60;
        HookBossLogs();
        BossHalt.Enabled = Arg("-haltOff", "0") != "1";
        BossManager.NetTestBossHpOverride = QaBossHp;
        string[] cases = Arg("-hCases", "wasteland_road:swordsman,wasteland_road:gunslinger,natural_cave:swordsman,sky_corridor:fighter").Split(',');
        float[] speeds = Arg("-hKmh", "45,150").Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        float fight = float.Parse(Arg("-hFight", "10"), System.Globalization.CultureInfo.InvariantCulture);
        var tsv = new StringBuilder("stage\tchar\tkmh\thalt\tstopSec\tslideM\tplayerSpanM\tpitInArena\tbranch\tonSky\tabilityKmh0\tabilityKmhHalt\tjudged\tbackRunFrames\tbossFrames\tfwdHits\tbackHits\tfwdSent\tbackSent\tcleared\tresumeSec\tdistMoves\trewards\n");
        foreach (var c in cases)
        {
            var sp = c.Split(':');
            foreach (float kmh in speeds)
                yield return HaltCase(sp[0], sp[1], kmh, fight, tsv);
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "halt.tsv"), tsv.ToString());
        BossHalt.Enabled = true;
    }

    bool hVideoOn;
    IEnumerator HaltCapture(float kmh)
    {
        Time.captureFramerate = 30;
        while (hVideoOn)
        {
            var b = WildAlive().FirstOrDefault();
            string st = !Bm.IsBossPhase ? "走り" : b == null ? "関門" : b.Broken ? "BREAK" : "ボス戦";
            aeOverlay = $"{(BossHalt.Enabled ? "新: 止まって戦う" : "前: 並走するボス戦")}  能力の速さ {GameManager.SpeedKmh(pc.AbilityRunSpeed):F0}km/h  実際の前進 {GameManager.SpeedKmh(pc.CurrentAutoRunSpeed):F0}km/h\n予兆の先読み {TelegraphLead.Local:F2}秒  {st}";
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(demoDir, $"f{demoFrame:00000}.png"));
            demoFrame++;
        }
    }

    IEnumerator HaltCase(string stage, string ch, float kmh, float fight, StringBuilder tsv)
    {
        string tag = $"{stage}/{ch}@{kmh:F0}";
        L($"== halt {tag} (halt {(BossHalt.Enabled ? "on" : "off")})");
        yield return BeginRun(ch, stage);
        SetGod(true);
        GameManager.BlockExpGain = true;
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
        yield return new WaitForSeconds(1.0f);
        // 関門の 300m 手前へ(ワープの直後は先の地形をまとめて作るので、関門の戦闘範囲はその後に作られる所へ置く = 実際のランと同じ)
        WarpTo(700f);
        yield return new WaitForSeconds(0.3f);
        SetKmh(kmh);
        yield return new WaitForSeconds(0.5f);
        int clr0 = BossHalt.FieldEnemiesCleared + BossHalt.FieldObstaclesCleared, rw0 = gm.BossRewardChoicesStarted;
        // 関門まで走る
        float w = 0f;
        while (!Bm.IsBossPhase && w < 30f) { yield return null; w += Time.deltaTime; }
        Check(Bm.IsBossPhase, $"halt {tag}: the 1,000m gate starts");
        // -hVideo 1: 関門から走りへ戻るまでを 30fps で書き出す(比較動画用)
        bool video = Arg("-hVideo", "0") == "1";
        if (video)
        {
            demoDir = System.IO.Path.Combine(outDir, "frames"); System.IO.Directory.CreateDirectory(demoDir);
            hVideoOn = true; StartCoroutine(HaltCapture(kmh));
        }
        float ability0 = GameManager.SpeedKmh(pc.AbilityRunSpeed);
        float tGate = Time.time, xGate = pc.transform.position.x, stopSec = -1f;
        w = 0f;
        while (w < 12f && (BossHalt.Enabled ? pc.HaltFactor > 0.001f : false)) { yield return null; w += Time.deltaTime; }
        if (BossHalt.Enabled && pc.HaltFactor <= 0.001f) stopSec = Time.time - tGate;
        float slide = pc.transform.position.x - xGate;
        var tm = TerrainManager.Instance;
        float px = pc.transform.position.x;
        bool pit = tm.FirstPitBetween(px - BossHalt.ArenaBehind, px + BossHalt.ArenaAhead).HasValue;
        bool branch = tm.IsInBranchRoute(px) || tm.IsInBranchRoute(px + BossHalt.ArenaAhead) || tm.IsInBranchRoute(px - BossHalt.ArenaBehind);
        bool sky = pc.QaOnSky;
        yield return WaitBossSpawn();
        float abilityHalt = GameManager.SpeedKmh(pc.AbilityRunSpeed);
        float judged = HighSpeedAssist.Instance != null ? HighSpeedAssist.Instance.JudgedKmh : -1f;
        // 戦う: ボスのいる側へ攻撃を入れる。前/後ろそれぞれ当たった数
        int fwdHits = 0, backHits = 0, fwdSent = 0, backSent = 0, backRun = 0, frames = 0;
        float pMin = px, pMax = px;
        PlayerController.FlickDirection lastDir = PlayerController.FlickDirection.Forward;
        void OnHitW(WildBossBase b, PlayerAttackKind k, bool lethal) { if (lastDir == PlayerController.FlickDirection.Backward) backHits++; else fwdHits++; }
        void OnHitO(Component b, PlayerAttackKind k) { if (lastDir == PlayerController.FlickDirection.Backward) backHits++; else fwdHits++; }
        WildBossBase.QaLocalHit += OnHitW; DragonController.QaLocalHit += OnHitO; MajinController.QaLocalHit += OnHitO;
        var prevX = new Dictionary<WildBossBase, float>();
        float t = 0f, nextIn = 0.3f;
        while (t < fight && Bm.AliveBossCount > 0)
        {
            foreach (var b in WildAlive())
            {
                float bx = b.transform.position.x;
                if (prevX.TryGetValue(b, out float ox) && Time.deltaTime > 0f)
                {
                    float vx = (bx - ox) / Time.deltaTime;
                    frames++;
                    // こちら(左)を向いたまま、右へ(後ろ向きに)走っている
                    if (b.DebugFacing < 0f && vx > 2f && bx > pc.transform.position.x && !b.Broken) backRun++;
                }
                prevX[b] = bx;
            }
            pMin = Mathf.Min(pMin, pc.transform.position.x); pMax = Mathf.Max(pMax, pc.transform.position.x);
            if (t >= nextIn)
            {
                var targets = WildAlive().Select(b => b.CenterWorld.x).Concat(DragonsAlive().Select(d => d.transform.position.x))
                    .Concat(FindObjectsByType<MajinController>(FindObjectsSortMode.None).Where(m => m != null && !m.IsDead).Select(m => m.transform.position.x)).ToList();
                if (targets.Count > 0)
                {
                    float nx = targets.OrderBy(x => Mathf.Abs(x - pc.transform.position.x)).First();
                    lastDir = nx >= pc.transform.position.x ? PlayerController.FlickDirection.Forward : PlayerController.FlickDirection.Backward;
                    if (lastDir == PlayerController.FlickDirection.Backward) backSent++; else fwdSent++;
                    yield return HaFlick(lastDir);
                }
                nextIn = t + 0.35f;
            }
            yield return null; t += Time.deltaTime;
        }
        WildBossBase.QaLocalHit -= OnHitW; DragonController.QaLocalHit -= OnHitO; MajinController.QaLocalHit -= OnHitO;
        int cleared = BossHalt.FieldEnemiesCleared + BossHalt.FieldObstaclesCleared - clr0;
        float d0 = gm.MaxDistance;
        KillAllBosses();
        yield return WaitPhaseEnd();
        float tEnd = Time.time, resume = -1f;
        w = 0f;
        while (w < 5f && pc.HaltFactor < 0.999f) { yield return null; w += Time.deltaTime; }
        if (pc.HaltFactor >= 0.999f) resume = Time.time - tEnd;
        yield return new WaitForSeconds(1.5f);
        bool distMoves = gm.MaxDistance > d0 + 5f;
        hVideoOn = false; Time.captureFramerate = 0; aeOverlay = null;
        int rewards = gm.BossRewardChoicesStarted - rw0;
        L($"[halt] {tag}: stop {stopSec:F2}s slide {slide:F1}m span {pMax - pMin:F1}m pit={pit} branch={branch} sky={sky} ability {ability0:F0}->{abilityHalt:F0}km/h judged {judged:F0} backRun {backRun}/{frames} hits fwd {fwdHits}/{fwdSent} back {backHits}/{backSent} cleared {cleared} resume {resume:F2}s dist {d0:F0}->{gm.MaxDistance:F0} rewards {rewards} | holds={BossHalt.Starts}/{BossHalt.Releases} seek={BossHalt.SeekFrames} '{BossHalt.SeekReason}' flatChunks={TerrainManager.BossArenaFlatChunks} branchPush={TerrainManager.BossArenaBranchPushes} turnAways={WildBossBase.TurnAways} lead={TelegraphLead.Local:F2}s early shown/promoted/cancelled={TelegraphLead.EarlyShown}/{TelegraphLead.EarlyPromoted}/{TelegraphLead.EarlyCancelled}");
        tsv.Append($"{stage}\t{ch}\t{kmh:F0}\t{BossHalt.Enabled}\t{stopSec:F2}\t{slide:F1}\t{pMax - pMin:F1}\t{pit}\t{branch}\t{sky}\t{ability0:F0}\t{abilityHalt:F0}\t{judged:F0}\t{backRun}\t{frames}\t{fwdHits}\t{backHits}\t{fwdSent}\t{backSent}\t{cleared}\t{resume:F2}\t{distMoves}\t{rewards}\n");
        if (BossHalt.Enabled)
        {
            Check(stopSec >= 0f && stopSec < 4f, $"halt {tag}: the run stops at the boss gate ({stopSec:F2}s)");
            Check(!pit && !branch && !sky, $"halt {tag}: the fight area has no pit/branch and the player is on the main road (pit {pit}, branch {branch}, upper {sky})");
            Check(Mathf.Abs(abilityHalt - ability0) < Mathf.Max(3f, ability0 * 0.08f), $"halt {tag}: the speed as an ability stays while stopped ({ability0:F0} -> {abilityHalt:F0} km/h)");
            Check(frames == 0 || backRun < frames * 0.10f, $"halt {tag}: the boss does not run backwards while facing the player ({backRun}/{frames} frames)");
            Check(resume >= 0f && resume < 3f, $"halt {tag}: running resumes after the reward ({resume:F2}s)");
        }
        Check(distMoves, $"halt {tag}: the distance moves again after the fight ({d0:F0} -> {gm.MaxDistance:F0})");
        Check(rewards == 1, $"halt {tag}: one boss reward ({rewards})");
        Check(fwdHits + backHits > 0, $"halt {tag}: attacks reach the boss during the fight (fwd {fwdHits}, back {backHits})");
        Check(!BossHalt.Holding, $"halt {tag}: no stop state is left after the fight");
        SetGod(false);
        GameManager.BlockExpGain = false;
        PlayerController.DebugSpeedScale = 1f;
        yield return EndRun();
    }
}
#endif
