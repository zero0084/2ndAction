#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// BREAK 中のボスの位置(2026-10-07): 飛行ボスが BREAK 中に画面の後ろへ流されないこと。
//  -qaBreak <dir> [-qaBrOnly 名前,…]
//  各ボスを速さ別(30/100/200/320km/h)に出し、BREAK させて、BREAK の間の「プレイヤーとの距離」「画面上の位置」「地面からの高さ」を毎フレーム測る。
//  BREAK の終わりの1.5秒は、1フレームでの大きな飛び(瞬間移動)が無いことを見る。
//  ドラゴンは 必殺技中 / 突進中 / ラン再開の後 にも BREAK させる。地上のボスは「BREAK でプレイヤーへ吸い寄せられない」ことを見る。
public partial class QaSweep
{
    struct BrCase { public string name; public int family; public int kind; public bool flying; public float[] speeds; public string extra; }

    IEnumerator BreakMode()
    {
        var cases = new List<BrCase>();
        float[] all = { 30f, 100f, 200f, 320f }, two = { 100f, 320f }, ground = { 30f, 320f };
        void Add(string n, int fam, int k, bool fly, float[] sp, string extra = null) => cases.Add(new BrCase { name = n, family = fam, kind = k, flying = fly, speeds = sp, extra = extra });
        Add("Wild/Dragon", 0, (int)WildBossKind.Dragon, true, all);
        Add("Wild/Dragon ultimate", 0, (int)WildBossKind.Dragon, true, new[] { 200f }, "ultimate");
        Add("Wild/Dragon charge", 0, (int)WildBossKind.Dragon, true, new[] { 200f }, "charge");
        Add("Wild/Dragon resumed", 0, (int)WildBossKind.Dragon, true, new[] { 150f }, "resume");
        Add("Wild/Dragon kill", 0, (int)WildBossKind.Dragon, true, new[] { 200f }, "kill");
        Add("Sky/Guardian kill", 2, (int)SkyBossKind.Guardian, true, new[] { 200f }, "kill");
        Add("Wild/Griffin", 0, (int)WildBossKind.Griffin, true, two);
        Add("Cave/Bat", 1, (int)CaveBossKind.Bat, true, two);
        Add("Sky/Dragon", 2, (int)SkyBossKind.Dragon, true, all);
        Add("Sky/Majin", 2, (int)SkyBossKind.Majin, true, all);
        Add("Sky/Majin ultimate", 2, (int)SkyBossKind.Majin, true, new[] { 200f }, "ultimate");
        Add("Sky/Jellyfish", 2, (int)SkyBossKind.Jellyfish, true, two);
        Add("Sky/Leviathan", 2, (int)SkyBossKind.Leviathan, true, two);
        Add("Sky/Fenrir", 2, (int)SkyBossKind.Fenrir, true, two);
        Add("Sky/Phoenix", 2, (int)SkyBossKind.Phoenix, true, two);
        Add("Sky/SkySerpent", 2, (int)SkyBossKind.SkySerpent, true, two);
        Add("Sky/Guardian", 2, (int)SkyBossKind.Guardian, true, two);
        Add("Wild/Wolf", 0, (int)WildBossKind.Wolf, false, ground);
        Add("Wild/BlackKnight", 0, (int)WildBossKind.BlackKnight, false, ground);
        Add("Cave/Troll", 1, (int)CaveBossKind.Troll, false, ground);
        Add("Sky/Titan", 2, (int)SkyBossKind.Titan, false, ground);
        string only = Arg("-qaBrOnly", "");
        if (only != "") cases = cases.Where(c => only.Split(',').Any(o => c.name.Contains(o))).ToList();

        // 自動操作補助は切る(ボス戦では補助がボスへ近づいて攻撃するので、BREAK 中の距離の変化がボスの動きなのか分からなくなる)
        var assist = HighSpeedAssist.Instance; bool assistWas = assist != null && assist.assistEnabled;
        if (assist != null) assist.SetEnabled(false);
        string stageNow = null;
        foreach (var c in cases)
        {
            string stage = c.family == 1 ? "natural_cave" : c.family == 2 ? "sky_corridor" : "wasteland_road";
            foreach (float kmh in c.speeds)
            {
                if (stage != stageNow) { if (stageNow != null) yield return EndRun(); yield return BfBeginStage(stage); stageNow = stage; }
                yield return BreakCase(c, kmh);
            }
        }
        if (assist != null) assist.SetEnabled(assistWas);
        DragonController.BreakDiag = false;
        PlayerController.DebugSpeedScale = 1f;
        yield return EndRun();
    }

    IEnumerator BreakCase(BrCase c, float kmh)
    {
        string tag = $"{c.name} @{kmh:F0}";
        L($"== {tag} ==");
        yield return BfWaitEncounterEnd();
        var bm = BossManager.Instance;
        bm.DebugSpawnBossForTest(c.family, c.kind);
        SetKmh(kmh);
        Object boss = null; float w = 0f;
        while (w < 15f)
        {
            yield return null; w += Time.unscaledDeltaTime;
            boss = BfFindBoss();
            if (boss is WildBossBase wb && !wb.IsEntering && w > 1.5f) break;
            if ((boss is DragonController || boss is MajinController) && w > 4.5f) break;
        }
        var dbg = boss as IBossBattleDebug;
        Check(dbg != null, $"{tag}: boss spawned");
        if (dbg == null) yield break;
        SetKmh(kmh); // 出現の間に速さが変わっていることがあるので、もう一度
        yield return new WaitForSeconds(0.5f);

        // 状況を作る
        if (c.extra == "ultimate")
        {
            dbg.DebugForceUltimate();
            float u = 0f; while (u < 4f && !dbg.UltimateRunning) { yield return null; u += Time.deltaTime; }
            yield return new WaitForSeconds(0.7f);
            Check(dbg.UltimateRunning, $"{tag}: the ultimate is running when BREAK hits");
        }
        else if (c.extra == "charge" && boss is DragonController dr0)
        {
            var stF = typeof(DragonController).GetField("state", NP);
            float u = 0f; while (u < 14f && stF.GetValue(dr0).ToString() != "Charging") { yield return null; u += Time.deltaTime; }
            L($"[{tag}] waited {u:F1}s for the charge ({stF.GetValue(dr0)})");
        }
        else if (c.extra == "resume") { bm.ResumeRun(); yield return new WaitForSeconds(1f); Check(bm.RunResumed, $"{tag}: the run has resumed"); }

        float kmhNow = pc.CurrentRunKmh;
        Transform bt = (boss as Component).transform;
        // プレイヤーの攻撃が当たる判定(被弾判定)で測る: WildBossBase は hurtCol、ドラゴン/魔人は本体の BoxCollider2D
        var hurtF = typeof(WildBossBase).GetField("hurtCol", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Collider2D Body() => boss is WildBossBase wbh ? hurtF.GetValue(wbh) as Collider2D : (boss as Component).GetComponent<BoxCollider2D>();
        float gap0 = bt.position.x - pc.transform.position.x;
        dbg.DebugForceBreak();
        yield return null;
        Check(dbg.Broken, $"{tag}: BREAK started");
        if (c.extra == "kill")
        {
            // BREAK 中に撃破: 死亡が確定したら BREAK の位置の制御は終わり、BOSS FINISH へ渡す
            yield return new WaitForSeconds(1f);
            int st0 = BossFinish.Started;
            if (boss is WildBossBase wk) wk.DebugKillWithAttack(BossFinalAttack.Forward); else if (boss is DragonController dk) dk.DebugKillWithAttack(BossFinalAttack.Forward);
            yield return null;
            Vector3 p0 = bt.position; float px0 = pc.transform.position.x;
            yield return new WaitForSeconds(0.5f);
            bool fin = BossFinish.Started == st0 + 1;
            L($"[{tag}] killed during BREAK: finish started {fin}, alive {dbg.DebugAlive}");
            Check(fin && !dbg.DebugAlive, $"{tag}: a kill during BREAK hands over to BOSS FINISH");
            PlayerController.DebugSpeedScale = 1f;
            yield return BfWaitEncounterEnd();
            yield break;
        }
        float dur = 0f, minGap = 999f, maxGap = -999f, maxAbove = -99f, minVp = 9f, maxVp = -9f, maxEdge = -99f;
        bool verbose = Arg("-qaBrVerbose", "0") == "1";
        var yOffF = typeof(WildBossBase).GetField("yOffset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        float nextV = 0f, lastPx = pc.transform.position.x, stumbleUntil = -1f; int stumbleFrames = 0;
        float tEnd = -1f, maxJump = 0f, lastGap = gap0;
        var cam = Camera.main;
        float t = 0f;
        while (t < 12f)
        {
            yield return null;
            float dt = Time.deltaTime; t += dt;
            if (!(boss as Component) || !dbg.DebugAlive) break;
            float gap = bt.position.x - pc.transform.position.x;
            // プレイヤーが地形の段差でつまずいて走行の速さどおりに進めなかったフレームは除く(ボスは走行の速さで並走するので、距離が一時的に開く)
            float pv = dt > 0f ? (pc.transform.position.x - lastPx) / dt : pc.CurrentAutoRunSpeed;
            lastPx = pc.transform.position.x;
            bool stumble = dt > 0f && Mathf.Abs(pv - pc.CurrentAutoRunSpeed) > 4f;
            if (stumble) stumbleUntil = t + 0.6f; // 追いつくまでの間も除く
            stumble = t < stumbleUntil;
            if (stumble && dbg.Broken) stumbleFrames++;
            if (dbg.Broken)
            {
                dur = t;
                if (t > 1.0f && !stumble) // 寄せの間(最初の1秒: 遠くからでも約0.8秒で届く位置へ)は除く
                {
                    var col = Body();
                    float bottom = col != null ? col.bounds.min.y : bt.position.y;
                    float? g = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(bt.position.x) : null;
                    float above = g.HasValue ? bottom - g.Value : -99f; // 穴の上(地面が無い所)の高さは数えない
                    minGap = Mathf.Min(minGap, gap); maxGap = Mathf.Max(maxGap, gap); maxAbove = Mathf.Max(maxAbove, above);
                    // 体の手前の端までの距離(大きいボスは中心が遠くても端は近い)
                    float pxNow = pc.transform.position.x;
                    float edgeD = col != null ? (pxNow < col.bounds.min.x ? col.bounds.min.x - pxNow : pxNow > col.bounds.max.x ? pxNow - col.bounds.max.x : 0f) : Mathf.Abs(gap);
                    maxEdge = Mathf.Max(maxEdge, edgeD);
                    float vx = cam.WorldToViewportPoint(bt.position).x;
                    minVp = Mathf.Min(minVp, vx); maxVp = Mathf.Max(maxVp, vx);
                }
            }
            else
            {
                if (tEnd < 0f) tEnd = t;
                if (dt > 0f) maxJump = Mathf.Max(maxJump, Mathf.Abs(gap - lastGap) - pc.CurrentAutoRunSpeed * dt); // 1フレーム分の走行(更新順のずれ)は除く
                if (t - tEnd > 1.5f) break;
            }
            if (verbose && t >= nextV)
            {
                nextV = t + 0.1f;
                var hc = Body();
                var rvF = typeof(WildBossBase).GetField("relVelocity", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                L($"[{tag}] t={t:F2} broken={dbg.Broken} gap={gap:F2} y={bt.position.y:F2} yOff={(boss is WildBossBase ? yOffF.GetValue(boss) : "-")} rel={(boss is WildBossBase ? rvF.GetValue(boss) : "-")} hurt={(hc != null ? $"{hc.enabled} c={hc.bounds.center.x - bt.position.x:F2} ext={hc.bounds.extents.x:F2}" : "-")} kmh={pc.CurrentRunKmh:F0} pv={pv:F1} auto={pc.CurrentAutoRunSpeed:F1} grounded={pc.IsGrounded} atk={pc.IsAttacking} px={pc.transform.position.x:F1}");
            }
            lastGap = gap;
        }
        L($"[{tag}] run {kmhNow:F0}km/h, gap before {gap0:F1}m, BREAK {dur:F1}s: gap {minGap:F1}..{maxGap:F1}m, screen x {minVp:F2}..{maxVp:F2}, body bottom above ground max {maxAbove:F1}m, after BREAK max jump/frame {maxJump:F2}m, player stumble frames {stumbleFrames}");
        Check(dur > 1f, $"{tag}: BREAK lasts ({dur:F1}s)");
        Check(minVp > 0.3f && maxVp < 1.0f, $"{tag}: stays on screen in front of the player during BREAK (screen x {minVp:F2}..{maxVp:F2})");
        Check(minGap > -1.5f, $"{tag}: does not drift behind the player during BREAK (min gap {minGap:F1}m)");
        if (c.flying) Check(maxEdge < 3.5f && maxAbove < 2.6f, $"{tag}: a flying boss comes down within reach during BREAK (body edge max {maxEdge:F1}m away, bottom above ground {maxAbove:F1}m)");
        else
        {
            // 地上のボス: その場で止まる(BREAK の間に動き回らない)、プレイヤーへ吸い寄せられない(重ならない)。
            // 届かない距離(体の手前の端まで3m超)にいた時だけ、届く位置まで滑らかに寄る
            float allow = 1.5f + kmhNow / 3.6f / 60f * 1.5f; // 1フレーム分の走行(更新順のずれ)は許す
            Check(maxGap - minGap < allow, $"{tag}: a ground boss holds still during BREAK (gap {minGap:F1}..{maxGap:F1}, allow {allow:F1})");
            Check(minGap > 2f, $"{tag}: a ground boss is not pulled onto the player (min gap {minGap:F1}m)");
        }
        Check(maxJump < 1.5f, $"{tag}: no warp when BREAK ends (max jump {maxJump:F2}m/frame)");
        // 片付け
        if (dbg.DebugAlive)
        {
            if (boss is WildBossBase wb2) wb2.DebugKillWithAttack(BossFinalAttack.Forward);
            else if (boss is DragonController d2) d2.DebugKillWithAttack(BossFinalAttack.Forward);
            else if (boss is MajinController m2) m2.DebugKillWithAttack(BossFinalAttack.Forward);
        }
        PlayerController.DebugSpeedScale = 1f;
        yield return BfWaitEncounterEnd();
    }
}
#endif
