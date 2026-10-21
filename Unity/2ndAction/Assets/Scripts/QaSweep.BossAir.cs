#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// ボスの怯み / BREAK 中のエリアルコンボ(2026-10-18): -qaBossAir <dir>
//   [-baChars swordsman,gunslinger,noble_lady] [-baBosses Wild/Wolf,Sky/Titan,Sky/Dragon,…] [-baSpeeds 45,150] [-baSeq U,F,F,F,U,F,F,F,D,F,F,F,D]
//   [-baKill 1] [-baNormal 1] [-baFps 60]
// 1回 = 1体のボス × 1人 × 1つの速さ:
//   A 通常: 前攻撃を入れ続けて、崩しの溜まり・怯みの回数・スーパーアーマー(怯まなかった回数)を数える。
//     あわせて崩しを段階的に足して(DebugAddStagger)、怯みが「行動の合間」だけで起きることを確かめる
//   B BREAK: BREAK させてから入力の列を入れ、1回ごとに 当たった(H + 種類 u/n/d/i)/外れた(W)/出なかった(q) を記録(^ = 浮いている、* = 寄せている)。
//     打ち上げ・寄せ・空中の段・叩きつけ・BREAK の長さ(延長込み)・回復の予告・立ち直り・BREAK 後の最初の攻撃までの時間・
//     画面の中にいるか・天井/地面へのめり込み・瞬間移動 を測る
//   C 撃破: BREAK 中に打ち上げてから HP を少なくして当て、HP 0 で撃破が確定する(BOSS FINISH が始まり、報酬が1回)
// 出力: bossair.tsv(1行=1回)と qa_bossair.txt
public partial class QaSweep
{
    struct BaResult
    {
        public int hitsA, flinch, armored, probeFlinch, probeArmored, probeCross;
        public string probeActs;
        public bool broke, launched, warned, recovered, killed, finishOk, rewardOnce;
        public int hits, whiff, queued, airHitsMax, slams, kneelHits, kneelSlams, downPushes;
        public float breakSec, breakExtra, firstAttack, maxRise, maxJump, ceilOver, groundUnder, vpMinX, vpMaxX, vpMinY, vpMaxY, minEdge, maxEdge;
        public string style, log;
    }

    // 荒野/洞窟/天空の共通のボス(WildBossBase)と、専用のドラゴン/魔人を同じように扱う
    class BaBoss
    {
        public WildBossBase W; public DragonController D; public MajinController M;
        public Component C => W != null ? (Component)W : D != null ? (Component)D : M;
        public bool Valid => W != null || D != null || M != null;
        public bool IsDead => W != null ? W.IsDead : D != null ? D.IsDead : M == null || M.IsDead;
        public bool Broken => W != null ? W.Broken : D != null ? D.Broken : M != null && M.Broken;
        public bool Recovering => W != null ? W.BreakRecovering : D != null ? D.BreakRecovering : M != null && M.BreakRecovering;
        public bool Warned => W != null ? W.BreakWarned : D != null ? D.BreakWarned : M != null && M.BreakWarned;
        public BossJuggle Juggle => W != null ? W.Juggle : D != null ? D.Juggle : M != null ? M.Juggle : null;
        public Bounds Live
        {
            get
            {
                if (W != null) return W.LiveHurtBounds;
                var box = C != null ? C.GetComponent<BoxCollider2D>() : null;
                return box != null ? BossJuggle.LiveBounds(box) : new Bounds(C != null ? C.transform.position : Vector3.zero, Vector3.one);
            }
        }
        public float Top => W != null ? W.transform.position.y + W.bodyHeight : Live.max.y;
        public float Bottom => W != null ? W.transform.position.y : Live.min.y;
        public string Style => W != null ? W.DebugBreakStyle.ToString() : "Launch";
        public string Act => W != null ? W.DebugAct : D != null ? D.DebugStateName : "-";
        public int Flinch => W != null ? W.FlinchCount : D != null ? D.FlinchCount : M != null ? M.FlinchCount : 0;
        public float StaggerFraction => W != null ? W.StaggerFraction : D != null ? D.StaggerFraction : M != null ? M.StaggerFraction : 0f;
        public float StaggerMax => W != null ? W.DebugStaggerMax : D != null ? D.DebugStaggerMax : M != null ? M.DebugStaggerMax : 0f;
        public void AddStagger(float v) { if (W != null) W.DebugAddStagger(v); else if (D != null) D.DebugAddStagger(v); else if (M != null) M.DebugAddStaggerQa(v); }
        public float LastBreak => W != null ? W.LastBreakSeconds : D != null ? D.LastBreakSeconds : M != null ? M.LastBreakSeconds : 0f;
        public float LastExtra => W != null ? W.LastBreakExtra : D != null ? D.LastBreakExtra : M != null ? M.LastBreakExtra : 0f;
        public float FirstAttack => W != null ? W.FirstAttackAfterBreak : D != null ? D.AttackDelayAfterBreak : M != null ? M.AttackDelayAfterBreak : -1f;
        public float BreakMax => W != null ? W.DebugBreakMax : D != null ? D.DebugBreakMax : M != null ? M.DebugBreakMax : 0f;
        public void ResetResist() { if (W != null) W.DebugResetResist(); else if (D != null) D.DebugResetResist(); else if (M != null) M.DebugResetResist(); }
        public void ForceBreak() { if (W != null) W.DebugForceBreak(); else if (D != null) D.DebugForceBreak(); else if (M != null) M.DebugForceBreak(); }
        public void ScaleHp(float m) { if (W != null) W.DebugScaleHp(m); else if (D != null) D.DebugScaleHp(m); else if (M != null) M.DebugScaleHp(m); }
        public void SetHp(int hp) { if (W != null) W.DebugSetHp(hp); else if (D != null) D.DebugSetHp(hp); else if (M != null) M.DebugSetHp(hp); }
        public void Kill() { if (W != null) W.DebugKillWithAttack(BossFinalAttack.Forward); else if (D != null) D.DebugKillWithAttack(BossFinalAttack.Forward); else if (M != null) M.DebugKillWithAttack(BossFinalAttack.Forward); }
        public bool Calm => !Broken && !Recovering && Act != "Flinch" && !(W != null && W.UltimateRunning);
    }

    IEnumerator BossAirMode()
    {
        HookBossLogs();
        Random.InitState(20261018);
        string[] chars = Arg("-baChars", "swordsman,gunslinger,noble_lady").Split(',');
        string[] bosses = Arg("-baBosses", "Wild/Wolf,Wild/BlackKnight,Wild/Griffin,Wild/Cyclops,Wild/Golem,Cave/Scorpion,Cave/Bat,Cave/Troll,Cave/AncientDemon,Sky/Dragon,Sky/Majin,Sky/Fenrir,Sky/Phoenix,Sky/Titan,Sky/Leviathan,Sky/MechDragon").Split(',');
        float[] speeds = Arg("-baSpeeds", "45,150").Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        string[] seq = Arg("-baSeq", "U,F,F,F,U,F,F,F,D,F,F,F,D").Split(',');
        bool kill = Arg("-baKill", "1") == "1", normal = Arg("-baNormal", "1") == "1";
        int fps = int.Parse(Arg("-baFps", "60"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = fps;
        var tsv = new StringBuilder("char\tboss\tstyle\tkmh\tfps\thitsA\tflinch\tarmored\tprobeCross\tprobeFlinch\tprobeArmored\tprobeActs\tbroke\tlaunched\thits\twhiff\tqueued\tairMax\tslams\tkneelHits\tkneelSlams\tdownPush\tbreakSec\textra\twarned\trecovered\tfirstAttack\tmaxRise\tmaxJump\tceilOver\tgroundUnder\tvpX\tvpY\tedge\tkilled\tfinish\trewardOnce\tlog\n");
        foreach (var ch in chars)
        {
            var byStage = bosses.GroupBy(b => b.Split('/')[0]);
            foreach (var grp in byStage)
            {
                string stage = grp.Key == "Cave" ? "natural_cave" : grp.Key == "Sky" ? "sky_corridor" : "wasteland_road";
                yield return BeginRun(ch, stage);
                SetGod(true);
                GameManager.BlockExpGain = true;
                if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
                TerrainManager.Instance.pitChanceBase = 0f; TerrainManager.Instance.pitChanceMax = 0f;
                TerrainManager.Instance.enemySpawnChance = 0f;
                if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
                if (BossManager.Instance != null) BossManager.Instance.enabled = true;
                yield return new WaitForSeconds(1.2f);
                foreach (var bk in grp)
                    foreach (var kmh in speeds)
                    {
                        BaResult r = default;
                        yield return BaCase(ch, bk, kmh, seq, kill, normal, x => r = x);
                        tsv.Append($"{ch}\t{bk}\t{r.style}\t{kmh:F0}\t{fps}\t{r.hitsA}\t{r.flinch}\t{r.armored}\t{r.probeCross}\t{r.probeFlinch}\t{r.probeArmored}\t{r.probeActs}\t{r.broke}\t{r.launched}\t{r.hits}\t{r.whiff}\t{r.queued}\t{r.airHitsMax}\t{r.slams}\t{r.kneelHits}\t{r.kneelSlams}\t{r.downPushes}\t{r.breakSec:F2}\t{r.breakExtra:F2}\t{r.warned}\t{r.recovered}\t{r.firstAttack:F2}\t{r.maxRise:F2}\t{r.maxJump:F2}\t{r.ceilOver:F2}\t{r.groundUnder:F2}\t{r.vpMinX:F2}..{r.vpMaxX:F2}\t{r.vpMinY:F2}..{r.vpMaxY:F2}\t{r.minEdge:F1}..{r.maxEdge:F1}\t{r.killed}\t{r.finishOk}\t{r.rewardOnce}\t{r.log}\n");
                        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "bossair.tsv"), tsv.ToString());
                    }
                GameManager.BlockExpGain = false;
                PlayerController.DebugSpeedScale = 1f;
                SetGod(false);
                yield return EndRun();
            }
        }
        L($"[bossair] totals: launches={BossJuggle.Launches} relaunch={BossJuggle.Relaunches} reanchor={BossJuggle.Reanchors} downPush={BossJuggle.DownPushes} slams={BossJuggle.Slams} kneelHits={BossJuggle.KneelHits} kneelSlams={BossJuggle.KneelSlams} " +
          $"endFar={BossJuggle.EndedFar} endTimeout={BossJuggle.EndedTimeout} endLanded={BossJuggle.EndedLanded} airLand={BossJuggle.AirLandings} flinches={WildBossBase.FlinchesTotal} armored={WildBossBase.FlinchArmoredTotal} relaunchInput={PlayerController.AerialRelaunches} diveReconnect={PlayerController.DiveReconnects}");
        Application.targetFrameRate = 60;
    }

    static bool BaParse(string key, out int fam, out int kind)
    {
        fam = 0; kind = 0;
        var p = key.Split('/');
        if (p.Length != 2) return false;
        try
        {
            if (p[0] == "Wild") { fam = 0; kind = (int)System.Enum.Parse(typeof(WildBossKind), p[1]); return true; }
            if (p[0] == "Cave") { fam = 1; kind = (int)System.Enum.Parse(typeof(CaveBossKind), p[1]); return true; }
            if (p[0] == "Sky") { fam = 2; kind = (int)System.Enum.Parse(typeof(SkyBossKind), p[1]); return true; }
        }
        catch { }
        return false;
    }

    BaBoss BaFind()
    {
        // 撃破の見た目の途中のボス(前の試行)は除く
        var w = FindObjectsByType<WildBossBase>(FindObjectsSortMode.None).FirstOrDefault(b => b != null && !b.IsDead && b.isActiveAndEnabled);
        if (w != null) return new BaBoss { W = w };
        var d = FindObjectsByType<DragonController>(FindObjectsSortMode.None).FirstOrDefault(b => b != null && !b.IsDead && b.isActiveAndEnabled);
        if (d != null) return new BaBoss { D = d };
        var m = FindObjectsByType<MajinController>(FindObjectsSortMode.None).FirstOrDefault(b => b != null && !b.IsDead && b.isActiveAndEnabled);
        return new BaBoss { M = m };
    }

    IEnumerator BaCase(string ch, string key, float kmh, string[] seq, bool kill, bool normal, System.Action<BaResult> done)
    {
        var r = new BaResult { vpMinX = 9f, vpMaxX = -9f, vpMinY = 9f, vpMaxY = -9f, minEdge = 99f, maxEdge = -99f, firstAttack = -1f };
        string tag = $"{ch} {key} @{kmh:F0}";
        L($"== bossair {tag} ==");
        if (!BaParse(key, out int fam, out int kind)) { L($"[bossair] unknown boss {key}"); done(r); yield break; }
        yield return BfWaitEncounterEnd();
        var bm = BossManager.Instance;
        bm.DebugSpawnBossForTest(fam, kind);
        SetKmh(kmh);
        BaBoss boss = null; float w = 0f; bool scaled = false;
        while (w < 15f)
        {
            yield return null; w += Time.unscaledDeltaTime;
            boss = BaFind();
            if (boss.Valid && !scaled) { boss.ScaleHp(40f); scaled = true; } // 見つけたらすぐ(登場を待つ間に CPU の仲間が倒さない)
            if (boss.W != null && !boss.W.IsEntering && w > 1.5f) break;
            if ((boss.D != null || boss.M != null) && w > 4.5f) break;
        }
        Check(boss != null && boss.Valid, $"bossair {tag}: boss spawned");
        if (boss == null || !boss.Valid) { done(r); yield break; }
        Component bc = boss.C; // HP は見つけた時に 40倍(長いコンボの途中で倒れない。撃破は C で別に確かめる)
        SetKmh(kmh);
        r.style = boss.Style;
        var log = new StringBuilder();
        int hitCount = 0;
        var kinds = new StringBuilder();
        void Note(Component b, PlayerAttackKind k) { if (b != bc) return; hitCount++; kinds.Append(k == PlayerAttackKind.Up ? "u" : k == PlayerAttackKind.Down ? "d" : k == PlayerAttackKind.DownImpact ? "i" : "n"); }
        void OnHitW(WildBossBase b, PlayerAttackKind k, bool lethal) => Note(b, k);
        void OnHitO(Component b, PlayerAttackKind k) => Note(b, k);
        WildBossBase.QaLocalHit += OnHitW;
        DragonController.QaLocalHit += OnHitO;
        MajinController.QaLocalHit += OnHitO;
        try
        {
            yield return new WaitForSeconds(0.6f);
            // ---- A: 通常時(前攻撃を入れ続ける) ----
            if (normal)
            {
                int f0 = boss.Flinch, a0 = WildBossBase.FlinchArmoredTotal, h0 = hitCount;
                float t = 0f;
                int n = 0;
                while (t < 6f && !boss.Broken && !boss.IsDead)
                {
                    yield return HaFlick(n % 5 == 4 ? PlayerController.FlickDirection.Up : PlayerController.FlickDirection.Forward);
                    n++;
                    float s = 0f; while (s < 0.3f) { yield return null; s += Time.deltaTime; }
                    t += 0.3f + 2f / 60f;
                }
                r.hitsA = hitCount - h0; r.flinch = boss.Flinch - f0; r.armored = WildBossBase.FlinchArmoredTotal - a0;
                // 崩しを段階的に足す: ゲージの目盛り(40%/75%)を越えた時の行動と、怯んだか
                yield return BaWaitCalm(boss);
                var acts = new StringBuilder();
                int pf0 = boss.Flinch, pa0 = WildBossBase.FlinchArmoredTotal;
                boss.ResetResist();
                float max = boss.StaggerMax;
                // 2つの目盛り(40% / 75%)を1回ずつ越える。怯みの間隔(2.5秒)が空いてから
                foreach (float mark in new[] { 0.45f, 0.8f })
                {
                    float s0 = 0f; while (s0 < 2.6f && !boss.Broken && !boss.IsDead) { yield return null; s0 += Time.deltaTime; }
                    if (boss.Broken || boss.IsDead) break;
                    float before = boss.StaggerFraction;
                    string act = boss.Act;
                    int fl0 = boss.Flinch;
                    if (before < mark) boss.AddStagger((mark - before) * max + 0.01f);
                    float after = boss.StaggerFraction;
                    if ((before < 0.4f && after >= 0.4f) || (before < 0.75f && after >= 0.75f)) { r.probeCross++; acts.Append($"{act}{(boss.Flinch > fl0 ? "→F" : "")}/"); }
                }
                r.probeFlinch = boss.Flinch - pf0; r.probeArmored = WildBossBase.FlinchArmoredTotal - pa0; r.probeActs = acts.ToString();
                if (boss.Broken) L($"[bossair] {tag} broke during the normal phase");
            }
            // 前の BREAK/怯みの名残が終わるまで待つ
            yield return BaWaitCalm(boss);

            // ---- B: BREAK → 入力の列 ----
            int kills0 = gm.BossKillCount;
            int led0 = RewardLedger.Entries.Count; string bossTag = RewardLedger.Tag(bc, boss.W != null ? boss.W.bossName : bc.name);
            int l0 = BossJuggle.Launches + BossJuggle.Relaunches, sl0 = BossJuggle.Slams, kn0 = BossJuggle.KneelHits, dp0 = BossJuggle.DownPushes, ks0 = BossJuggle.KneelSlams;
            w = 0f; while ((!pc.IsGrounded || pc.IsAttacking) && w < 2f) { yield return null; w += Time.deltaTime; }
            boss.ForceBreak();
            yield return null;
            yield return new WaitForSecondsRealtime(0.2f);
            r.broke = boss.Broken;
            Check(r.broke, $"bossair {tag}: BREAK started");
            if (!r.broke) { done(r); yield break; }
            // 届く所へ寄ってくるのを待つ
            float near = r.style == "Launch" ? 1.25f : 2.6f;
            w = 0f;
            while (w < 2f && BaEdge(boss) > near) { yield return null; w += Time.deltaTime; }
            var cam = Camera.main;
            Vector3 prevB = boss.Live.center, prevP = pc.transform.position;
            System.Action<float> sample = dt =>
            {
                var bb = boss.Live;
                Vector3 pb = bb.center, pp = pc.transform.position;
                float jump = Mathf.Min((pb - prevB).magnitude, ((pb - prevB) - (pp - prevP)).magnitude) * 0.0167f / Mathf.Max(0.0167f, dt);
                var jj = boss.Juggle;
                if ((pp - prevP).magnitude < 4f && dt > 0f && jump > r.maxJump) { r.maxJump = jump; if (jump > 1.5f) log.Append($"[JUMP {jump:F2} dt={dt:F3} b=({prevB.x:F1},{prevB.y:F1})->({pb.x:F1},{pb.y:F1}) p=({prevP.x:F1},{prevP.y:F1})->({pp.x:F1},{pp.y:F1}) air={(jj != null && jj.Airborne)} anc={(jj != null && jj.Anchored)} act={boss.Act} split={(boss.W != null ? boss.W.DebugMoveSplit.ToString("F2") : "-")} rel={(boss.W != null ? boss.W.DebugRelVelocity : 0f):F1}] "); }
                prevB = pb; prevP = pp;
                var tm = TerrainManager.Instance;
                float? ceil = tm != null ? tm.GetEffectiveCeilingHeightAt(pb.x) : null;
                if (jj != null && jj.Airborne && ceil.HasValue) r.ceilOver = Mathf.Max(r.ceilOver, boss.Top - ceil.Value);
                float? g = tm != null ? tm.GetSupportHeightAt(bc.transform.position.x) : (float?)null;
                if (g.HasValue && r.style == "Launch")
                {
                    float un = g.Value - boss.Bottom;
                    // 2026-10-21: めり込んだ瞬間の様子(どの状態・どの床の段で起きたか)
                    if (un > 0.25f && un > r.groundUnder + 0.1f) log.Append($"[UNDER {un:F2} t={Time.time:F2} x={bc.transform.position.x:F1} gRoot={g.Value:F2} gBody={(tm.GetSupportHeightAt(bb.center.x)):F2} bottom={boss.Bottom:F2} air={(jj != null && jj.Airborne)} anc={(jj != null && jj.Anchored)} broken={boss.Broken} act={boss.Act}] ");
                    r.groundUnder = Mathf.Max(r.groundUnder, un);
                }
                if (cam != null && boss.Broken)
                {
                    Vector3 vp = cam.WorldToViewportPoint(pb);
                    r.vpMinX = Mathf.Min(r.vpMinX, vp.x); r.vpMaxX = Mathf.Max(r.vpMaxX, vp.x);
                    r.vpMinY = Mathf.Min(r.vpMinY, vp.y); r.vpMaxY = Mathf.Max(r.vpMaxY, vp.y);
                    float e = BaEdge(boss);
                    r.minEdge = Mathf.Min(r.minEdge, e); r.maxEdge = Mathf.Max(r.maxEdge, e);
                }
                if (jj != null) { r.airHitsMax = Mathf.Max(r.airHitsMax, jj.AirHits); r.maxRise = Mathf.Max(r.maxRise, jj.MaxRise); }
                if (boss.Warned) r.warned = true;
            };
            for (int si = 0; si < seq.Length && boss.Broken && !boss.IsDead; si++)
            {
                var f = seq[si] == "U" ? PlayerController.FlickDirection.Up : seq[si] == "D" ? PlayerController.FlickDirection.Down : PlayerController.FlickDirection.Forward;
                int sw0 = PlayerAttackInfo.SwingCount, h0 = hitCount;
                kinds.Length = 0;
                yield return HaFlick(f);
                float s = 0f;
                while (s < 0.3f) { sample(Time.deltaTime); yield return null; s += Time.deltaTime; }
                string res;
                if (hitCount > h0) { res = "H" + kinds; r.hits++; }
                else if (PlayerAttackInfo.SwingCount > sw0) { res = $"W({BaEdge(boss):F1})"; r.whiff++; }
                else { res = "q"; r.queued++; }
                var j = boss.Juggle;
                log.Append($"{seq[si]}:{res}{(j != null && j.Airborne ? "^" : "")}{(j != null && j.Anchored ? "*" : "")} ");
            }
            // BREAK の終わりまで見届ける
            w = 0f;
            while (boss.Broken && w < 8f && !boss.IsDead) { sample(Time.deltaTime); yield return null; w += Time.deltaTime; }
            r.breakSec = boss.LastBreak; r.breakExtra = boss.LastExtra;
            w = 0f; while (boss.Recovering && w < 2f) { yield return null; w += Time.deltaTime; }
            r.recovered = !boss.Broken && !boss.Recovering;
            w = 0f; while (boss.FirstAttack < 0f && w < 5f && !boss.IsDead) { yield return null; w += Time.deltaTime; }
            r.firstAttack = boss.FirstAttack;
            r.launched = BossJuggle.Launches + BossJuggle.Relaunches > l0; // 飛行のボスは落ちる途中で打ち上げると「打ち上げ直し」に数える
            r.slams = BossJuggle.Slams - sl0; r.kneelHits = BossJuggle.KneelHits - kn0; r.downPushes = BossJuggle.DownPushes - dp0; r.kneelSlams = BossJuggle.KneelSlams - ks0;

            // ---- C: BREAK 中の撃破 ----
            if (kill && !boss.IsDead)
            {
                // フェニックスの復活(初登場の1回)は仕様。ここでは HP 0 で撃破が確定するかを見るので、復活しないようにする
                var btq = BossBattleTuning.I; bool rebirthKeep = btq.phoenixRebirthOnFirst; float rebirthChanceKeep = btq.phoenixRebirthRematchChance;
                btq.phoenixRebirthOnFirst = false; btq.phoenixRebirthRematchChance = 0f;
                try {
                yield return BaWaitCalm(boss);
                w = 0f; while ((!pc.IsGrounded || pc.IsAttacking) && w < 2f) { yield return null; w += Time.deltaTime; }
                boss.ForceBreak();
                yield return null;
                yield return new WaitForSecondsRealtime(0.2f);
                w = 0f; while (w < 2f && BaEdge(boss) > near) { yield return null; w += Time.deltaTime; }
                int st0 = BossFinish.Started;
                yield return HaFlick(PlayerController.FlickDirection.Up);
                float s = 0f; while (s < 0.32f) { yield return null; s += Time.deltaTime; }
                boss.SetHp(1);
                int hk0 = hitCount;
                for (int i = 0; i < 10 && !boss.IsDead; i++)
                {
                    yield return HaFlick(i % 4 == 3 ? PlayerController.FlickDirection.Up : PlayerController.FlickDirection.Forward);
                    s = 0f; while (s < 0.3f && !boss.IsDead) { yield return null; s += Time.deltaTime; }
                    if (!boss.IsDead) log.Append($"c{i}:e{BaEdge(boss):F1},dy{boss.Live.center.y - pc.transform.position.y:F1},{(boss.Broken ? "B" : "-")}{(pc.IsGrounded ? "g" : "a")}{(pc.IsAttacking ? "A" : "")}{(boss.W != null && boss.W.DebugInvulnerable ? "I" : "")}{(boss.W != null && boss.W.DebugHurtCol != null && !boss.W.DebugHurtCol.enabled ? "X" : "")} ");
                }
                yield return new WaitForSeconds(0.6f); // 飛んでいる矢/弾/結界が当たるまで
                r.killed = boss.IsDead;
                if (!r.killed) log.Append($"[C: no kill, hits after hp1={hitCount - hk0} broken={boss.Broken}] ");
                r.finishOk = BossFinish.Started == st0 + 1;
                yield return BfWaitEncounterEnd();
                // 2026-10-21: 報酬1回は「このボス(個体)」で数える。以前は撃破数の合計で比べていたため、150km/h で関門を越えて
                //  報酬の直後に出た別のボス(関門の再戦のオオカミ等)を CPU の仲間が倒すと +2 になり、失敗に見えていた(二重取りではない)。
                //  このボスの撃破の登録が1回・その後(次の撃破まで)にボスの報酬の選択が1回・MILE が入っている、を見る
                // 別のボスが出て倒された時は、そのボスの報酬の選択が出るまで待つ(取りこぼしの確認のため。後回しの安全装置 12秒 + 余裕で最大30秒)
                for (float wt = 0f; wt < 30f && gm.BossRewardChoicesStarted < gm.BossKillCount; wt += Time.unscaledDeltaTime) yield return null;
                var led = new StringBuilder();
                int myDefeats = 0, myChoices = 0, others = 0, myMile = 0, allChoices = 0; bool afterMine = false;
                for (int li = Mathf.Min(led0, RewardLedger.Entries.Count); li < RewardLedger.Entries.Count; li++)
                {
                    var e = RewardLedger.Entries[li];
                    if (!e.kind.StartsWith("Boss:")) led.Append(e).Append(" | ");
                    if (e.kind == "BossDefeat")
                    {
                        if (e.tag == bossTag) { myDefeats++; afterMine = true; var m = System.Text.RegularExpressions.Regex.Match(e.detail, @"mile\+(\d+)"); if (m.Success) myMile += int.Parse(m.Groups[1].Value); }
                        else { others++; afterMine = false; }
                    }
                    if (e.kind == "RewardChoice") { allChoices++; if (afterMine) myChoices++; }
                }
                // このボスの撃破の登録は1回・MILE が入った、かつ、この Run のボスの撃破の数と報酬の選択の数が同じ(取りこぼし・二重が無い)。
                // 後回しの選択はまとめて後に出るので、どの選択がどの撃破の分かは順番では決めない(数で見る)
                r.rewardOnce = myDefeats == 1 && myMile > 0 && gm.BossRewardChoicesStarted == gm.BossKillCount;
                L($"[bossair] {tag} ledger target={bossTag} myDefeats={myDefeats} myChoices={myChoices} myMile={myMile} otherBossDefeats={others} allChoices={allChoices} runKills={gm.BossKillCount} runChoices={gm.BossRewardChoicesStarted} queuedExtra={GameManager.BossRewardsQueuedExtra} killsDelta={gm.BossKillCount - kills0}: {led}");
                } finally { btq.phoenixRebirthOnFirst = rebirthKeep; btq.phoenixRebirthRematchChance = rebirthChanceKeep; }
            }
        }
        finally
        {
            WildBossBase.QaLocalHit -= OnHitW;
            DragonController.QaLocalHit -= OnHitO;
            MajinController.QaLocalHit -= OnHitO;
        }
        r.log = log.ToString();
        L($"[bossair] {tag} style={r.style} A: hits={r.hitsA} flinch={r.flinch} armored={r.armored} probe cross={r.probeCross} flinch={r.probeFlinch} armored={r.probeArmored} acts={r.probeActs} | " +
          $"B: launched={r.launched} hits={r.hits}/{seq.Length} whiff={r.whiff} q={r.queued} air={r.airHitsMax} slam={r.slams} kneel={r.kneelHits}/{r.kneelSlams} down={r.downPushes} break={r.breakSec:F2}s(+{r.breakExtra:F2}) warned={r.warned} recovered={r.recovered} firstAtk={r.firstAttack:F2}s " +
          $"rise={r.maxRise:F2} jump={r.maxJump:F2} ceil={r.ceilOver:F2} under={r.groundUnder:F2} vp=({r.vpMinX:F2}..{r.vpMaxX:F2},{r.vpMinY:F2}..{r.vpMaxY:F2}) edge={r.minEdge:F1}..{r.maxEdge:F1} | C: killed={r.killed} finish={r.finishOk} reward1={r.rewardOnce}  {r.log}");
        float bmax = boss.Valid ? boss.BreakMax : 99f;
        Check(r.breakSec > 1f && r.breakSec < bmax + 1.6f, $"bossair {tag}: BREAK is bounded ({r.breakSec:F2}s, max {bmax:F2}s + landing)");
        Check(r.warned, $"bossair {tag}: the recovery warning is shown before BREAK ends");
        Check(r.recovered, $"bossair {tag}: the boss recovers after BREAK");
        Check(r.firstAttack < 0f || r.firstAttack >= WildBossBase.BreakRecoverSeconds + 0.2f, $"bossair {tag}: no instant counterattack after BREAK (first attack {r.firstAttack:F2}s)");
        Check(r.maxJump < 1.5f, $"bossair {tag}: no teleport-like jump during BREAK ({r.maxJump:F2}m/frame)");
        Check(r.ceilOver < 0.2f, $"bossair {tag}: a launched boss does not go into the ceiling ({r.ceilOver:F2})");
        Check(r.groundUnder < 0.25f, $"bossair {tag}: the boss does not sink into the ground ({r.groundUnder:F2})");
        Check(r.vpMinX > 0.2f && r.vpMaxX < 1.05f, $"bossair {tag}: stays on screen in front during BREAK (x {r.vpMinX:F2}..{r.vpMaxX:F2})");
        if (r.style == "Launch") Check(r.launched, $"bossair {tag}: ↑ launches the boss during BREAK");
        if (kill) Check(r.killed && r.finishOk && r.rewardOnce, $"bossair {tag}: a kill at HP 0 during the air combo is confirmed once (killed {r.killed}, finish {r.finishOk}, reward once {r.rewardOnce})");
        if (normal && r.probeCross > 0) Check(r.probeFlinch + r.probeArmored >= 1, $"bossair {tag}: crossing the gauge marks flinches or shows armor");
        if (boss.Valid && !boss.IsDead) boss.Kill();
        yield return BfWaitEncounterEnd();
        done(r);
    }

    // 体の手前の端までの距離(プレイヤーの x から)
    float BaEdge(BaBoss b)
    {
        var bb = b.Live;
        float px = pc.transform.position.x;
        return px < bb.min.x ? bb.min.x - px : px > bb.max.x ? px - bb.max.x : 0f;
    }

    IEnumerator BaWaitCalm(BaBoss b)
    {
        float w = 0f;
        while (!b.Calm && w < 10f && !b.IsDead) { yield return null; w += Time.deltaTime; }
        yield return new WaitForSeconds(0.3f);
    }
    // ---------------------------------------------------------------- 比較動画
    // -qaBossAirVideo <dir> [-bvPairs swordsman:Wild/BlackKnight,…] [-bvKmh 45] [-bvSeq U,F,F,F,U,F,F,F,D,F,F,F,D]
    // 1組(キャラ:ボス)ごとに「改修前(BossJuggle.Enabled=false)」と「改修後」を同じ手順で 30fps 毎フレーム書き出す。区切りは video.tsv
    IEnumerator BossAirVideoMode()
    {
        HookBossLogs();
        Random.InitState(20261018);
        string[] pairs = Arg("-bvPairs", "swordsman:Wild/BlackKnight,gunslinger:Wild/Griffin,noble_lady:Wild/Cyclops,dragon_lancer:Cave/Troll,fighter:Sky/Dragon,ninja:Sky/Titan").Split(',');
        float kmh = float.Parse(Arg("-bvKmh", "45"), System.Globalization.CultureInfo.InvariantCulture);
        string[] seq = Arg("-bvSeq", "U,F,F,F,U,F,F,F,D,F,F,F,D").Split(',');
        // 2026-10-21: -bvRev 1 = 「改修前」を APK 338 の動き(ComboRevision.Legacy)にする(既定は 2026-10-18 の改修前 = BossJuggle.Enabled=false)
        bool rev = Arg("-bvRev", "0") == "1";
        bvGap = float.Parse(Arg("-bvGap", "0.3"), System.Globalization.CultureInfo.InvariantCulture);
        bvOnce = rev;
        // 2026-10-21 追加調査: -bvFinishAtWarn 1 = ↑→→→ をくり返し、締めの合図が出たら 0.15秒後に ↓(締めの試験と同じ入れ方)。
        //  -bvOnlyNew 1 = 今の版の動きだけ撮る(別の版の exe で撮ったものと並べる時)
        bool finAtWarn = Arg("-bvFinishAtWarn", "0") == "1", onlyNew = Arg("-bvOnlyNew", "0") == "1";
        bool followup = Arg("-bvFollowup", "0") == "1"; // 「改修前」= APK 339 の動き(今回の追加調査の分だけを切る)
        if (finAtWarn) bvOnce = true;
        demoDir = System.IO.Path.Combine(outDir, "frames");
        System.IO.Directory.CreateDirectory(demoDir);
        var tsv = new StringBuilder("char\tboss\tlegacy\tfrom\tto\tlog\n");
        foreach (var pr in pairs)
        {
            var sp = pr.Split(':');
            if (sp.Length != 2 || !BaParse(sp[1], out int fam, out int kind)) continue;
            string ch = sp[0], key = sp[1];
            string stage = fam == 1 ? "natural_cave" : fam == 2 ? "sky_corridor" : "wasteland_road";
            yield return BeginRun(ch, stage);
            SetGod(true);
            GameManager.BlockExpGain = true;
            if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
            TerrainManager.Instance.pitChanceBase = 0f; TerrainManager.Instance.pitChanceMax = 0f;
            TerrainManager.Instance.enemySpawnChance = 0f;
            if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
            if (BossManager.Instance != null) BossManager.Instance.enabled = true;
            yield return new WaitForSeconds(1.2f);
            foreach (bool legacy in onlyNew ? new[] { false } : new[] { true, false })
            {
                if (followup) { BossJuggle.Enabled = true; ComboRevision.Legacy = false; ComboRevision.FollowupOff = legacy; }
                else if (rev) { BossJuggle.Enabled = true; ComboRevision.Legacy = legacy; } else BossJuggle.Enabled = !legacy;
                yield return BfWaitEncounterEnd();
                BossManager.Instance.DebugSpawnBossForTest(fam, kind);
                SetKmh(kmh);
                BaBoss boss = null; float w = 0f;
                while (w < 15f)
                {
                    yield return null; w += Time.unscaledDeltaTime;
                    boss = BaFind();
                    if (boss.W != null && !boss.W.IsEntering && w > 1.5f) break;
                    if ((boss.D != null || boss.M != null) && w > 4.5f) break;
                }
                if (boss == null || !boss.Valid) continue;
                boss.ScaleHp(40f);
                SetKmh(kmh);
                yield return new WaitForSeconds(0.8f);
                yield return BaWaitCalm(boss);
                w = 0f; while ((!pc.IsGrounded || pc.IsAttacking) && w < 2f) { yield return null; w += Time.deltaTime; }
                boss.ForceBreak();
                yield return new WaitForSecondsRealtime(0.2f);
                float near = legacy && !rev ? 3.2f : (boss.Style == "Launch" ? 1.25f : 2.6f);
                w = 0f; while (w < 2f && BaEdge(boss) > near) { yield return null; w += Time.deltaTime; }
                var log = new StringBuilder();
                Component bc = boss.C;
                void OnW(WildBossBase b, PlayerAttackKind k, bool lethal) { if (b == bc) log.Append(k == PlayerAttackKind.Up ? "u" : k == PlayerAttackKind.Down ? "d" : k == PlayerAttackKind.DownImpact ? "i" : "n"); }
                void OnO(Component b, PlayerAttackKind k) { if (b == bc) log.Append(k == PlayerAttackKind.Up ? "u" : k == PlayerAttackKind.Down ? "d" : k == PlayerAttackKind.DownImpact ? "i" : "n"); }
                WildBossBase.QaLocalHit += OnW; DragonController.QaLocalHit += OnO; MajinController.QaLocalHit += OnO;
                int from = demoFrame;
                int bvSlams0 = BossJuggle.Slams + BossJuggle.KneelSlams;
                Time.captureFramerate = 30;
                string head = $"{(legacy ? (followup ? "改修前(APK339)" : rev ? "改修前(APK338)" : "改修前") : "改修後")}  {pc.CurrentAutoRunSpeed * 3.6f:F0}km/h\n入力: ";
                string arrows = "";
                bvArrows = "";
                var inputs = StartCoroutine(finAtWarn ? BvInputsFinish(boss) : BvInputs(seq));
                int n = Mathf.RoundToInt((finAtWarn ? 0.3f + 6.5f : 0.3f + seq.Length * bvGap + 2.4f) * 30f);
                for (int i = 0; i < n; i++)
                {
                    arrows = bvArrows;
                    var j = boss.Juggle;
                    string st = boss.IsDead ? "撃破" : boss.Broken ? (boss.Warned ? ((rev && !legacy) || followup ? "BREAK(締めの合図 ↓)" : "BREAK(回復の予告)") : "BREAK") + (j != null && j.Airborne ? " 打ち上げ中" : "") + (j != null && j.IsComboAnchored && j.AirHits >= 2 ? $" AIR {j.AirHits}" : "")
                        : boss.Recovering ? "立ち直り" : "通常";
                    if (BossJuggle.Slams + BossJuggle.KneelSlams > bvSlams0) st += "  締めが入った";
                    aeOverlay = head + arrows + "\n" + st;
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(demoDir, $"f{demoFrame:00000}.png"));
                    demoFrame++;
                }
                Time.captureFramerate = 0;
                StopCoroutine(inputs);
                aeOverlay = null;
                WildBossBase.QaLocalHit -= OnW; DragonController.QaLocalHit -= OnO; MajinController.QaLocalHit -= OnO;
                tsv.Append($"{ch}\t{key}\t{(legacy ? 1 : 0)}\t{from}\t{demoFrame - 1}\t{log}\n");
                L($"[bossAirVideo] {ch} {key} {(legacy ? "before" : "after")} frames {from}-{demoFrame - 1}: {log}");
                System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "video.tsv"), tsv.ToString());
                if (boss.Valid && !boss.IsDead) boss.Kill();
                yield return BfWaitEncounterEnd();
            }
            BossJuggle.Enabled = true; ComboRevision.Legacy = false; ComboRevision.FollowupOff = false;
            GameManager.BlockExpGain = false;
            SetGod(false);
            PlayerController.DebugSpeedScale = 1f;
            yield return EndRun();
        }
        BossJuggle.Enabled = true;
        Check(demoFrame > 0, "boss air video: frames written");
    }

    string bvArrows = "";
    float bvGap = 0.3f; bool bvOnce;
    // ↑→→→ をくり返して、締めの合図(回復の予告)が出たら 0.15秒後に ↓ を1回
    IEnumerator BvInputsFinish(BaBoss boss)
    {
        string[] loop = { "U", "F", "F", "F" };
        for (int i = 0; i < 24 && boss.Valid && !boss.IsDead && boss.Broken && !boss.Warned; i++)
        {
            string k = loop[i % 4];
            bvArrows += k == "U" ? "↑" : "→";
            pc.debugInjectOnce = k == "U" ? PlayerController.FlickDirection.Up : PlayerController.FlickDirection.Forward;
            float t = 0f; while (t < bvGap && !boss.Warned) { yield return null; t += Time.deltaTime; }
        }
        if (!boss.Valid || !boss.Warned) yield break;
        yield return new WaitForSeconds(0.15f);
        bvArrows += " ↓";
        pc.debugInjectOnce = PlayerController.FlickDirection.Down;
    }

    IEnumerator BvInputs(string[] seq)
    {
        for (int i = 0; i < seq.Length; i++)
        {
            var f = seq[i] == "U" ? PlayerController.FlickDirection.Up : seq[i] == "D" ? PlayerController.FlickDirection.Down : PlayerController.FlickDirection.Forward;
            bvArrows += seq[i] == "U" ? "↑" : seq[i] == "D" ? "↓" : "→";
            if (bvOnce) { pc.debugInjectOnce = f; yield return new WaitForSeconds(bvGap); continue; } // 本物の1回のフリック(2026-10-21)
            yield return HaFlick(f);
            yield return new WaitForSeconds(bvGap - 2f / 30f);
        }
    }
}
#endif
