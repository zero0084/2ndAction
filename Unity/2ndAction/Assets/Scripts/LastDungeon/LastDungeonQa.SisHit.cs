#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// 死神三姉妹の命中の切り分け(2026-11-28、マスター依頼⑦)。 -ldQa <dir> -ldQaMode sishit [-ldChar swordsman] [-shView land|side|fold|oblique] [-shSecs 30] [-shOverlay 1]
//  縦横(窓の大きさで決まる)と縦の表示3種を同じ条件で比べる。前半の1人ずつ(長女/次女/三女)と後半の三人同時を、それぞれ shSecs 秒ずつ自然に戦う
//  (HP は大きくして倒さない。時間が来たら退かせる/倒す)。ボットは近いボスへ向けて攻撃し続ける。
//  攻撃1回ごとに、攻撃の判定(タグ PlayerAttack。近接の判定も弾も)と各姉妹の被弾の範囲を毎フレーム比べて、外れた理由を分ける:
//   HIT … 当たった
//   OVERLAP_NOHIT … 判定が重なり、被弾できる状態だったのに当たらなかった(不具合)
//   BLOCKED:<理由> … 重なったが 無敵/被弾の判定が無効/登場中/退き中
//   FAR_X / HIGH / LOW / FAR_XY … 重ならなかった(横の距離 / 姉妹が高い / 低い / 両方)
//   NO_ATTACK … 入力したが攻撃が出なかった(被弾中など)
//  同時に 1 秒ごとに各姉妹の位置(間合い・高さ・画面の中か)を記録し、近接が届く時間の割合(攻撃の機会)を出す。
//  出力: sishit_<view>.tsv(攻撃1回 = 1行) / sishit_<view>_time.tsv / ld_sishit.txt(まとめ) / 画面(判定の枠を描く: -shOverlay 1)
public partial class LastDungeonQa
{
    class ShAtk
    {
        public float t0; public string dir; public bool air; public string phase;
        public ReaperSisterBoss tgt; public string sname = "-", act, move; public float gap, low, edge;
        public bool started, hit, overlapVuln, overlapBlocked; public string blockWhy = "";
        public float minDx = 999f, minDy = 999f; public string hitWho = "";
    }
    readonly List<ShAtk> shOpen = new List<ShAtk>();
    readonly List<ShAtk> shDone = new List<ShAtk>();
    StringBuilder shTime;
    string shView = "land", shPhase = "";
    readonly Dictionary<string, float[]> shOpp = new Dictionary<string, float[]>(); // sister+phase → [秒, 届く秒, 画面内秒]
    readonly List<LineRenderer> shLines = new List<LineRenderer>();
    float shFitMax = 1f;
    double? shLastLx; float shMovedTotal, shSampledT, shHaltedT;

    IEnumerator SistersHitMode()
    {
        string ch = Arg("-ldChar", "swordsman");
        shView = Arg("-shView", Screen.height > Screen.width ? "side" : "land");
        float secs = float.Parse(Arg("-shSecs", "30"), System.Globalization.CultureInfo.InvariantCulture);
        bool overlay = Arg("-shOverlay", "1") != "0";
        if (Screen.height > Screen.width)
            PortraitRunView.Set(shView == "fold" ? PortraitRunView.Fold : shView == "oblique" ? PortraitRunView.Oblique : PortraitRunView.Side);
        bool kill = Arg("-shKill", "0") != "0"; // 強化の前後の比較: HP はそのまま、倒すまで戦う(各段階の上限 shKillMax 秒)
        float killMax = float.Parse(Arg("-shKillMax", "240"), System.Globalization.CultureInfo.InvariantCulture);
        ReaperFinaleBattle.DebugHpOverride = kill ? 0 : 999999;
        yield return BeginRun(ch);
        FreezeDiagnostics.EventTap += ShDamageTap;
        if (HighSpeedAssist.Instance != null && Arg("-shAssist", "0") == "0") HighSpeedAssist.Instance.SetEnabled(false);
        L($"[sishit] char={ch} view={shView} screen={Screen.width}x{Screen.height} portraitSide={PortraitRunView.UseSidePortrait} fold={FoldView.Active} oblique={PortraitRunView.UseOblique} secs={secs}");
        shTime = new StringBuilder("t\tphase\tsister\tgap\tlow\thigh\tedge\treach\tact\tmove\tvpx\tvpy\tonScreen\tinvuln\ttargetable\n");
        var flow = LastDungeonFlow.Instance;
        gm.DebugWarpToDistance(float.Parse(Arg("-shWarp", "99960"), System.Globalization.CultureInfo.InvariantCulture)); // -shWarp 99700 = 三姉妹の手前を走ってから(ワープの瞬間に作る地形を戦場にしない)
        warpedTo = 99960f;
        float w = 0f;
        while ((flow == null || flow.Current != LastDungeonFlow.State.Finale || ReaperFinaleBattle.Instance == null) && w < 40f) { yield return null; w += Time.deltaTime; if (flow == null) flow = LastDungeonFlow.Instance; }
        var fin = ReaperFinaleBattle.Instance;
        Check(fin != null, "the finale started");
        if (fin == null) yield break;
        WildBossBase.QaLocalHit += ShOnHit;
        StartCoroutine(ShWatch(overlay));
        // ---- 前半: 1人ずつ
        for (int si = 0; si < 3; si++)
        {
            ReaperSisterBoss b = null;
            w = 0f; while (b == null && w < 15f) { yield return null; w += Time.deltaTime; b = fin.Active.FirstOrDefault(x => x != null && !x.Retreating); }
            if (b == null) { Check(false, $"solo sister #{si} appeared"); break; }
            shPhase = "solo";
            L($"[sishit] solo {b.sister}");
            float t = 0f; int shots = 0;
            ShStageBegin($"solo {b.sister}", b);
            while ((kill ? t < killMax : t < secs) && b != null && !b.Retreating)
            {
                ShBot();
                if ((!kill || shots < 4) && t >= shots * (secs / 4f) + 1.5f) { Shot($"{shView}_solo_{b.sister}_{shots}"); shots++; }
                yield return null; t += Time.deltaTime;
            }
            ShStageEnd(b != null && b.Retreating, t);
            yield return ShDrain();
            if (b != null && !b.Retreating) b.TakeDamage(99999999, b.CenterWorld);
            w = 0f; while (b != null && b.gameObject.activeInHierarchy && w < 6f) { yield return null; w += Time.deltaTime; }
        }
        // ---- 後半: 三人同時
        w = 0f; while (fin.Current != ReaperFinaleBattle.Stage.Group && w < 20f) { yield return null; w += Time.deltaTime; }
        w = 0f; while (fin.Active.Count(x => x != null && !x.IsDead) < 3 && w < 10f) { yield return null; w += Time.deltaTime; }
        shPhase = "group";
        L($"[sishit] group {fin.Active.Count}");
        {
            float t = 0f; int shots = 0;
            ShStageBegin("group", fin.Active.FirstOrDefault());
            while (kill ? (t < killMax * 2f && fin.GroupDefeated < 3) : t < secs * 1.5f)
            {
                ShBot();
                if ((!kill || shots < 4) && t >= shots * (secs / 4f) + 1.5f) { Shot($"{shView}_group_{shots}"); shots++; }
                yield return null; t += Time.deltaTime;
            }
            ShStageEnd(fin.GroupDefeated >= 3, t);
        }
        yield return ShDrain();
        shPhase = "";
        foreach (var b in fin.Active.ToArray()) if (b != null && !b.IsDead) b.TakeDamage(99999999, b.CenterWorld);
        w = 0f; while (fin.Current != ReaperFinaleBattle.Stage.Done && w < 15f) { yield return null; w += Time.deltaTime; }
        Check(fin == null || fin.Current == ReaperFinaleBattle.Stage.Done, "the finale finished");
        L($"[sishit] whole finale: player moved {shMovedTotal:F1}m forward while sisters were up, halted {100f * shHaltedT / Mathf.Max(0.01f, shSampledT):F0}% of the time, BossHalt starts {BossHalt.Starts} releases {BossHalt.Releases}, last reason '{BossHalt.LastReason}'");
        L($"[sishit] damage reasons: {string.Join(", ", hitWhy.Select(kv => kv.Key + "=" + kv.Value))}");
        if (ReaperSisterBoss.HaltFight) Check(shHaltedT >= shSampledT * 0.9f, $"the sisters fight is fought standing (auto-run stopped {100f * shHaltedT / Mathf.Max(0.01f, shSampledT):F0}% of the fight)");
        string after = Arg("-shAfter", "");
        if (after != "" && flow != null) { L($"[sishit] credits and ONE MORE MILE? -> {after}"); yield return CreditsAndChoice(flow, after == "yes"); }
        WildBossBase.QaLocalHit -= ShOnHit;
        FreezeDiagnostics.EventTap -= ShDamageTap;
        ShReport();
    }

    // 強化の前後の比較: 段階ごとの時間/被ダメージ/命中
    string shStage; int shStageDmg, shStageHits, shStageHitCount, shStageAtk; float shStageT0; string shStageHp;
    readonly List<string> shStages = new List<string>();
    void ShDamageTap(string m)
    {
        if (shStage == null || !m.StartsWith("[Damage] Hit reason=")) return;
        int i = m.IndexOf(" amount="); if (i < 0) return;
        int j = m.IndexOf(' ', i + 8);
        if (int.TryParse(m.Substring(i + 8, (j > 0 ? j : m.Length) - i - 8), out int a)) { shStageDmg += a; shStageHitCount++; }
    }
    void ShStageBegin(string name, ReaperSisterBoss b)
    {
        shStage = name; shStageDmg = shStageHitCount = 0; shStageT0 = Time.time;
        shStageHits = shDone.Count(a => a.hit) + shOpen.Count(a => a.hit); shStageAtk = shDone.Count + shOpen.Count;
        shStageHp = string.Join("/", ReaperSisterBoss.All.Where(x => x != null && !x.IsDead && x.isActiveAndEnabled).Select(x => $"{x.sister}:{x.maxHp}"));
        // 戦闘範囲の地面(穴の上で戦わないか): 後ろ 20m 〜 前 150m の地面の無い所と、そこで「平地にする」決まりが真か
        var tmS = TerrainManager.Instance;
        if (tmS != null && pc != null)
        {
            var sbp = new StringBuilder(); float px0 = pc.transform.position.x; float? inPit = null;
            for (float dx = -20f; dx <= 150f; dx += 0.5f)
            {
                float x = px0 + dx; bool pit = !tmS.GetHeightAt(x).HasValue;
                if (pit && inPit == null) inPit = x;
                if (!pit && inPit != null) { float lx0 = FloatingOrigin.ToLogical(inPit.Value); sbp.Append($" [{lx0:F1}..{FloatingOrigin.ToLogical(x):F1} forceFlat={(tmS.forceFlatAt != null ? tmS.forceFlatAt(lx0).ToString() : "null")}]"); inPit = null; }
            }
            L($"[sishit] ground {name}: player lx {FloatingOrigin.ToLogical(px0):F1}, pits:{(sbp.Length == 0 ? " none" : sbp.ToString())}");
        }
        L($"[sishit] stage {name} begin hp {shStageHp} dmgMul(boss)={BossManager.DamageMul:F2} sisterDmg x{ReaperSisterBoss.FinaleDamageMul:F2} hpMul x{ReaperSisterBoss.FinaleHpMul:F2} bossHpCard x{(gm != null ? gm.BossHpMultiplier : 1f):F2}");
    }
    void ShStageEnd(bool cleared, float t)
    {
        if (shStage == null) return;
        int hits = shDone.Count(a => a.hit) + shOpen.Count(a => a.hit) - shStageHits, atk = shDone.Count + shOpen.Count - shStageAtk;
        string line = $"{shView}	{shStage}	{(cleared ? "cleared" : "TIMEOUT")}	{t:F1}	{shStageHp}	{shStageHitCount}	{shStageDmg}	{atk}	{hits}";
        shStages.Add(line);
        L($"[sishit] stage {shStage} {(cleared ? "cleared" : "TIMEOUT")} in {t:F1}s | damage taken {shStageHitCount} hits / {shStageDmg} | attacks {atk} hit {hits}");
        shStage = null;
    }

    // 振りごと(PlayerAttackInfo.SwingId)の確認: 攻撃の判定が被弾できる姉妹の被弾の範囲に 物理の1歩(FixedUpdate)以上 重なったのに、その振りが当たらなかったか
    class ShSwing { public int id; public ReaperSisterBoss s; public string sister, col, phase, act, move; public int frames, touch; public float first, last, depth; public bool hit; public bool air; public string diag = ""; }
    static readonly System.Reflection.FieldInfo ShHurtCol = typeof(WildBossBase).GetField("hurtCol", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    readonly Dictionary<(int, ReaperSisterBoss), ShSwing> shSwOpen = new Dictionary<(int, ReaperSisterBoss), ShSwing>();
    readonly List<ShSwing> shSwDone = new List<ShSwing>();
    void ShSwingStep()
    {
        foreach (var a in PlayerAttackInfo.Active.ToArray())
        {
            if (a == null) continue;
            var c = a.col != null ? a.col : a.GetComponent<Collider2D>();
            if (c == null || !c.enabled || !c.gameObject.activeInHierarchy || !c.CompareTag("PlayerAttack")) continue;
            var cb = c.bounds;
            foreach (var s in ReaperSisterBoss.All)
            {
                if (s == null || s.IsDead || !s.isActiveAndEnabled || s.Retreating || !s.AssistTargetable) continue;
                var hb = s.LiveHurtBounds;
                float ix = Mathf.Min(cb.max.x, hb.max.x) - Mathf.Max(cb.min.x, hb.min.x), iy = Mathf.Min(cb.max.y, hb.max.y) - Mathf.Max(cb.min.y, hb.min.y);
                if (ix < 0.05f || iy < 0.05f) continue;
                var key = (a.SwingId, s);
                if (!shSwOpen.TryGetValue(key, out var sw))
                    shSwOpen[key] = sw = new ShSwing { id = a.SwingId, s = s, sister = s.sister.ToString(), col = c.name, phase = shPhase, act = s.DebugAct, move = s.LastMove, first = Time.time, air = pc != null && !pc.IsGrounded };
                sw.frames++; sw.last = Time.time; sw.depth = Mathf.Max(sw.depth, Mathf.Min(ix, iy));
                var hc = ShHurtCol != null ? ShHurtCol.GetValue(s) as Collider2D : null;
                bool touching = hc != null && Physics2D.IsTouching(c, hc);
                if (touching) sw.touch++;
                if (sw.frames == 1)
                    sw.diag = $"t={Time.time:F2} col={c.name} layer={LayerMask.LayerToName(c.gameObject.layer)}/{(hc != null ? LayerMask.LayerToName(hc.gameObject.layer) : "-")} " +
                              $"ignore={(hc != null && Physics2D.GetIgnoreLayerCollision(c.gameObject.layer, hc.gameObject.layer))} trig={c.isTrigger} rb={(c.attachedRigidbody != null ? c.attachedRigidbody.bodyType.ToString() + (c.attachedRigidbody.simulated ? "" : "(off)") : "none")} " +
                              $"hurtRb={(hc != null && hc.attachedRigidbody != null ? hc.attachedRigidbody.simulated.ToString() : "-")} pcAttacking={(pc != null && pc.IsAttacking)} grounded={(pc != null && pc.IsGrounded)} ts={Time.timeScale:F2} " +
                              $"inv={s.DebugInvulnerable} broken={s.Broken} act={s.DebugAct} owner={(c.transform.root != null ? c.transform.root.name : "-")} recentHitOfSwing={PlayerAttackInfo.LastHitSwingId}";
            }
        }
        var close = new List<(int, ReaperSisterBoss)>();
        foreach (var kv in shSwOpen)
        {
            var sw = kv.Value;
            if (!sw.hit && sw.s != null && PlayerAttackInfo.WasHit(sw.id, sw.s)) sw.hit = true;
            if (Time.time - sw.last > 0.4f) close.Add(kv.Key);
        }
        foreach (var k in close) { shSwDone.Add(shSwOpen[k]); shSwOpen.Remove(k); }
    }

    float shLastFlick;
    void ShBot()
    {
        if (pc == null || Time.time - shLastFlick < 0.18f) return;
        ReaperSisterBoss best = null; float bd = float.MaxValue;
        foreach (var s in ReaperSisterBoss.All) if (s != null && !s.IsDead && s.isActiveAndEnabled && !s.Retreating) { float e = ShEdge(s); if (e < bd) { bd = e; best = s; } }
        if (best == null) return;
        shLastFlick = Time.time;
        var hb = best.LiveHurtBounds; Vector3 p = pc.transform.position;
        float cx = hb.center.x - p.x;
        PlayerController.FlickDirection f;
        if (hb.min.y > p.y + 2.4f) f = PlayerController.FlickDirection.Up;
        else if (!pc.IsGrounded && hb.max.y < p.y + 0.4f) f = PlayerController.FlickDirection.Down;
        else f = cx < -0.3f ? PlayerController.FlickDirection.Backward : PlayerController.FlickDirection.Forward;
        bool wasAttacking = pc.IsAttacking;
        var a = new ShAtk { t0 = Time.time, dir = f.ToString(), air = !pc.IsGrounded, phase = shPhase, tgt = best, sname = best.sister.ToString(), act = best.DebugAct, move = best.LastMove, gap = hb.center.x - p.x, low = hb.min.y - p.y, edge = bd };
        shOpen.Add(a);
        StartCoroutine(ShFlick(f, a, wasAttacking));
    }

    IEnumerator ShFlick(PlayerController.FlickDirection f, ShAtk a, bool wasAttacking)
    {
        pc.debugInjectFlick = f;
        yield return null; yield return null;
        pc.debugInjectFlick = null;
        a.started = pc.IsAttacking || ShAnyAttackCollider();
    }

    float ShEdge(WildBossBase s)
    {
        var lb = s.LiveHurtBounds; float px = pc.transform.position.x;
        return lb.min.x > px ? lb.min.x - px : lb.max.x < px ? px - lb.max.x : 0f;
    }

    readonly List<Collider2D> shCols = new List<Collider2D>();
    void ShGatherAttackColliders()
    {
        shCols.Clear();
        foreach (var c in FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
            if (c != null && c.enabled && c.gameObject.activeInHierarchy && c.CompareTag("PlayerAttack")) shCols.Add(c);
    }
    bool ShAnyAttackCollider() { ShGatherAttackColliders(); return shCols.Count > 0; }

    void ShOnHit(WildBossBase b, PlayerAttackKind kind, bool killed)
    {
        var s = b as ReaperSisterBoss;
        if (s == null) return;
        // 一番古い、まだ当たっていない開いた攻撃に付ける(同じ攻撃の多段は最初の1回)
        foreach (var a in shOpen) if (!a.hit) { a.hit = true; a.hitWho = s.sister.ToString(); a.started = true; return; }
        if (shOpen.Count > 0) shOpen[shOpen.Count - 1].hitWho += "+" + s.sister;
    }

    IEnumerator ShWatch(bool overlay)
    {
        float nextSample = 0f;
        while (true)
        {
            yield return new WaitForFixedUpdate();
            if (pc == null) continue;
            if (shOpen.Count > 0)
            {
                ShGatherAttackColliders();
                foreach (var a in shOpen)
                {
                    if (a.tgt == null) continue;
                    var hb = a.tgt.LiveHurtBounds;
                    foreach (var c in shCols)
                    {
                        var cb = c.bounds;
                        float dx = Mathf.Max(0f, Mathf.Max(cb.min.x - hb.max.x, hb.min.x - cb.max.x));
                        float dy = cb.min.y > hb.max.y ? cb.min.y - hb.max.y : hb.min.y > cb.max.y ? -(hb.min.y - cb.max.y) : 0f; // 正 = 攻撃が上(姉妹が低い)、負 = 姉妹が高い
                        if (dx + Mathf.Abs(dy) < a.minDx + Mathf.Abs(a.minDy)) { a.minDx = dx; a.minDy = dy; }
                        if (dx <= 0f && dy == 0f)
                        {
                            string why = ShBlockWhy(a.tgt);
                            if (why == "") a.overlapVuln = true; else { a.overlapBlocked = true; a.blockWhy = why; }
                        }
                    }
                }
                for (int i = shOpen.Count - 1; i >= 0; i--)
                    if (Time.time - shOpen[i].t0 > 0.75f) { shDone.Add(shOpen[i]); shOpen.RemoveAt(i); }
            }
            ShSwingStep();
            if (shPhase != "" && pc != null)
            {
                double lx = pc.transform.position.x + FloatingOrigin.Offset;
                if (shLastLx.HasValue && lx > shLastLx.Value && lx - shLastLx.Value < 5.0) shMovedTotal += (float)(lx - shLastLx.Value);
                shLastLx = lx;
                shSampledT += Time.fixedDeltaTime; if (BossHalt.Halted) shHaltedT += Time.fixedDeltaTime;
            }
            else shLastLx = null;
            if (shPhase != "" && Time.time >= nextSample)
            {
                nextSample = Time.time + 0.25f;
                foreach (var s in ReaperSisterBoss.All) if (s != null && !s.IsDead && s.isActiveAndEnabled) ShSample(s, 0.25f);
            }
            if (overlay) ShDrawOverlay();
        }
    }

    string ShBlockWhy(ReaperSisterBoss s)
    {
        if (s.IsDead) return "dead";
        if (s.Retreating) return "retreating";
        if (s.DebugInvulnerable) return "invulnerable";
        if (!s.AssistTargetable) return "hurtboxOff/entering";
        return "";
    }

    void ShSample(ReaperSisterBoss s, float dt)
    {
        var hb = s.LiveHurtBounds; Vector3 p = pc.transform.position;
        float edge = ShEdge(s);
        bool targetable = s.AssistTargetable;
        bool reach = targetable && edge <= WildBossBase.MeleeEdge && hb.min.y <= p.y + WildBossBase.MeleeUp;
        var cam = Camera.main;
        Vector3 vp = cam != null ? cam.WorldToViewportPoint(hb.center) : Vector3.zero;
        bool on = vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
        string key = shPhase + "\t" + s.sister;
        if (CameraFollow.Instance != null) shFitMax = Mathf.Max(shFitMax, CameraFollow.Instance.PortraitFitZoom);
        if (!shOpp.TryGetValue(key, out var o)) shOpp[key] = o = new float[3];
        o[0] += dt; if (reach) o[1] += dt; if (on) o[2] += dt;
        shTime.Append($"{Time.time:F2}\t{shPhase}\t{s.sister}\t{hb.center.x - p.x:F2}\t{hb.min.y - p.y:F2}\t{hb.max.y - p.y:F2}\t{edge:F2}\t{(reach ? 1 : 0)}\t{s.DebugAct}\t{s.LastMove}\t{vp.x:F2}\t{vp.y:F2}\t{(on ? 1 : 0)}\t{(s.DebugInvulnerable ? 1 : 0)}\t{(targetable ? 1 : 0)}\n");
    }

    IEnumerator ShDrain()
    {
        float w = 0f; while (shOpen.Count > 0 && w < 1.5f) { yield return null; w += Time.deltaTime; }
    }

    static string ShResult(ShAtk a)
    {
        if (a.hit) return "HIT";
        if (a.overlapVuln) return "OVERLAP_NOHIT";
        if (a.overlapBlocked) return "BLOCKED:" + a.blockWhy;
        if (!a.started) return "NO_ATTACK";
        if (a.minDx > 999f - 1f) return "NO_COLLIDER";
        bool fx = a.minDx > 0.01f, fy = Mathf.Abs(a.minDy) > 0.01f;
        if (fx && fy) return "FAR_XY";
        if (fx) return "FAR_X";
        return a.minDy < 0f ? "HIGH" : "LOW";
    }

    void ShReport()
    {
        var sb = new StringBuilder("view\tphase\tsister\tdir\tair\tresult\thitWho\tact\tmove\tgap\tlow\tedge\tminDx\tminDy\n");
        foreach (var a in shDone)
            sb.Append($"{shView}\t{a.phase}\t{a.sname}\t{a.dir}\t{(a.air ? 1 : 0)}\t{ShResult(a)}\t{a.hitWho}\t{a.act}\t{a.move}\t{a.gap:F2}\t{a.low:F2}\t{a.edge:F2}\t{(a.minDx > 998f ? -1f : a.minDx):F2}\t{(a.minDx > 998f ? 0f : a.minDy):F2}\n");
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, $"sishit_{shView}.tsv"), sb.ToString());
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, $"sishit_{shView}_time.tsv"), shTime.ToString());
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, $"sishit_{shView}_stages.tsv"), "view\tstage\tresult\tsec\thp\tdamageHits\tdamage\tattacks\thits\n" + string.Join("\n", shStages) + "\n");
        L($"[sishit] portrait camera fit max x{shFitMax:F2} reachFix={ReaperSisterBoss.ReachFix}");
        L("[sishit] summary (attacks: result counts)");
        foreach (var g in shDone.GroupBy(a => a.phase + " " + a.sname).OrderBy(g => g.Key))
        {
            int n = g.Count(), h = g.Count(a => a.hit);
            string parts = string.Join(" ", g.GroupBy(ShResult).OrderByDescending(x => x.Count()).Select(x => $"{x.Key}={x.Count()}"));
            L($"  {g.Key}: attacks {n} hit {h} ({(n > 0 ? 100f * h / n : 0f):F0}%) | {parts}");
        }
        foreach (var kv in shOpp.OrderBy(k => k.Key))
            L($"  time {kv.Key.Replace('\t', ' ')}: {kv.Value[0]:F1}s, melee reach {100f * kv.Value[1] / Mathf.Max(0.01f, kv.Value[0]):F0}%, on main camera {100f * kv.Value[2] / Mathf.Max(0.01f, kv.Value[0]):F0}%");
        foreach (var kv in shSwOpen) shSwDone.Add(kv.Value);
        var sw = new StringBuilder("view\tphase\tsister\tswing\tcollider\tframes\tdepth\thit\tair\tact\tmove\n");
        foreach (var x in shSwDone) sw.Append($"{shView}\t{x.phase}\t{x.sister}\t{x.id}\t{x.col}\t{x.frames}\t{x.depth:F2}\t{(x.hit ? 1 : 0)}\t{(x.air ? 1 : 0)}\t{x.act}\t{x.move}\n");
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, $"sishit_{shView}_swings.tsv"), sw.ToString());
        L("[sishit] swings that overlapped a hittable sister (1+ physics steps): hit / missed");
        foreach (var g in shSwDone.GroupBy(x => x.phase + " " + x.sister).OrderBy(g => g.Key))
            L($"  {g.Key}: swings {g.Count()} hit {g.Count(x => x.hit)} missed {g.Count(x => !x.hit)} (missed with 2+ steps {g.Count(x => !x.hit && x.frames >= 2)}) cols {string.Join(",", g.Where(x => !x.hit).GroupBy(x => x.col).Select(y => y.Key + "=" + y.Count()))}");
        foreach (var x in shSwDone.Where(x => !x.hit && x.frames >= 2)) L($"  MISSED swing {x.id} {x.phase} {x.sister} frames={x.frames} touching={x.touch} depth={x.depth:F2} | {x.diag}");
        int bug = shSwDone.Count(x => !x.hit && x.frames >= 2);
        Check(bug == 0, $"no swing overlapped a hittable sister for 2+ physics steps without hitting ({bug})");
    }

    // 判定の枠(緑 = 姉妹の被弾の範囲、黄 = プレイヤーの攻撃の判定)を画面に描く
    void ShDrawOverlay()
    {
        int k = 0;
        foreach (var s in ReaperSisterBoss.All)
            if (s != null && !s.IsDead && s.isActiveAndEnabled) ShRect(k++, s.LiveHurtBounds, s.AssistTargetable ? new Color(0.2f, 1f, 0.3f, 1f) : new Color(0.6f, 0.6f, 0.6f, 1f));
        ShGatherAttackColliders();
        foreach (var c in shCols) { if (k > 14) break; ShRect(k++, c.bounds, new Color(1f, 0.9f, 0.1f, 1f)); }
        for (int i = k; i < shLines.Count; i++) shLines[i].enabled = false;
    }

    void ShRect(int i, Bounds b, Color c)
    {
        while (shLines.Count <= i)
        {
            var go = new GameObject("ShOverlay" + shLines.Count);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.positionCount = 5; lr.useWorldSpace = true; lr.widthMultiplier = 0.08f; lr.sortingOrder = 32000;
            shLines.Add(lr);
        }
        var l = shLines[i];
        l.enabled = true; l.startColor = l.endColor = c;
        l.SetPosition(0, new Vector3(b.min.x, b.min.y, -0.1f)); l.SetPosition(1, new Vector3(b.max.x, b.min.y, -0.1f));
        l.SetPosition(2, new Vector3(b.max.x, b.max.y, -0.1f)); l.SetPosition(3, new Vector3(b.min.x, b.max.y, -0.1f));
        l.SetPosition(4, new Vector3(b.min.x, b.min.y, -0.1f));
    }
}
#endif
