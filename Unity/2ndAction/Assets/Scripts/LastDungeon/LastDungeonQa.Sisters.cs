#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 死神三姉妹の技の確認(2026-10-20)。 -ldQa <dir> -ldQaMode sisters [-ldChar swordsman]
//  A … 弾き返しの基本: 返せる弾を前/上/空中/下の攻撃で返す → ボスへ当たる / 壊せる弾が砕ける / 返せない弾は攻撃で消えない / 多段で二重に返らない
//  B … 各姉妹の2つの技を2回ずつ: 出た弾の種類・予兆・判定の位置(正面)・ボットの迎撃(壊す/返す)・被弾・FPS・画面
//  C … 三人同時: 連携技「死の舞踏」の時間差(長女 → 次女 → 三女)、FPS
public partial class LastDungeonQa
{
    IEnumerator SistersMode()
    {
        string ch = Arg("-ldChar", "swordsman");
        ReaperFinaleBattle.DebugHpOverride = 99999;
        ReaperFinaleBattle.ComboFirstDelay = 3f; ReaperFinaleBattle.ComboInterval = 12f;
        yield return BeginRun(ch);
        var flow = LastDungeonFlow.Instance;
        gm.DebugWarpToDistance(99960f);
        warpedTo = 99960f;
        L($"[sisters] char={ch} warp to 99,960m");
        float w = 0f;
        while ((flow == null || flow.Current != LastDungeonFlow.State.Finale || ReaperFinaleBattle.Instance == null) && w < 40f) { yield return null; w += Time.deltaTime; if (flow == null) flow = LastDungeonFlow.Instance; }
        var fin = ReaperFinaleBattle.Instance;
        Check(fin != null, "the finale started");
        if (fin == null) yield break;
        // 1人目(長女)が出るのを待つ
        w = 0f; while (fin.Active.Count == 0 && w < 10f) { yield return null; w += Time.deltaTime; }
        var eldest = fin.Active.FirstOrDefault();
        w = 0f; while (eldest != null && eldest.DebugAct == "" && w < 6f) { yield return null; w += Time.deltaTime; }
        yield return new WaitForSeconds(2.5f);

        // ---------------------------------------------------------------- A 弾き返しの基本
        ReaperSisterBoss.DebugHold = true;
        yield return new WaitForSeconds(1.5f);
        ReaperBullet.ClearAll();
        yield return Lineup(eldest);
        yield return ReflectProbe(eldest, "forward", PlayerController.FlickDirection.Forward, false, new Vector2(4.5f, 1.0f), new Vector2(-9f, 0f), 1.9f);
        yield return ReflectProbe(eldest, "up", PlayerController.FlickDirection.Up, false, new Vector2(0.6f, 6.5f), new Vector2(0f, -9f), 3.2f);
        yield return ReflectProbe(eldest, "air forward", PlayerController.FlickDirection.Forward, true, new Vector2(5.0f, 2.6f), new Vector2(-9f, 0f), 2.0f);
        yield return ReflectProbe(eldest, "down", PlayerController.FlickDirection.Down, true, new Vector2(1.2f, 0.5f), new Vector2(-4f, 0f), 2.4f);
        // 壊せる弾
        int br0 = ReaperBullet.BrokenCount;
        yield return KindProbe(eldest, ReaperBulletKind.Breakable);
        Check(ReaperBullet.BrokenCount > br0, $"A: a breakable bullet is destroyed by a forward attack ({ReaperBullet.BrokenCount - br0})");
        // 返せない弾: 攻撃しても消えない
        yield return KindProbe(eldest, ReaperBulletKind.Hazard);
        // 多段: 1発の返せる弾を連続で攻撃しても返るのは1回
        int r0 = ReaperBullet.ReflectedCount;
        var one = SpawnAtPlayer(eldest, ReaperBulletKind.Reflectable, new Vector2(2.6f, 1.0f), new Vector2(-6f, 0f));
        for (int i = 0; i < 4; i++) { yield return Flick(PlayerController.FlickDirection.Forward); yield return new WaitForSeconds(0.12f); }
        yield return new WaitForSeconds(0.8f);
        Check(ReaperBullet.ReflectedCount - r0 <= 1, $"A: repeated hits reflect a bullet only once ({ReaperBullet.ReflectedCount - r0})");
        ReaperBullet.ClearAll();
        ReaperSisterBoss.DebugHold = false;

        // ---------------------------------------------------------------- B 各姉妹の技(技そのものを見るため、この間は崩し/怯みを止める)
        BossBattle.DebugNoStagger = true;
        StartCoroutine(SpikeWatch());
        for (int si = 0; si < 3; si++)
        {
            var b = fin.Active.FirstOrDefault(x => x != null && !x.Retreating);
            w = 0f; while (b == null && w < 12f) { yield return null; w += Time.deltaTime; b = fin.Active.FirstOrDefault(x => x != null && !x.Retreating); }
            if (b == null) { Check(false, $"B: sister #{si} appeared"); break; }
            L($"[sisters] B {b.sister} (solo)");
            yield return new WaitForSeconds(1.5f);
            for (int rep = 0; rep < 2; rep++)
                for (int mv = 0; mv < 2; mv++)
                    yield return ObserveMove(b, mv, $"{b.sister}_{ReaperSisterBoss.MoveNames[(int)b.sister][mv]}_{rep}", rep == 0);
            // 退かせる(次の姉妹へ)
            b.TakeDamage(9999999, b.CenterWorld);
            w = 0f; while (b != null && b.gameObject.activeInHierarchy && w < 6f) { yield return null; w += Time.deltaTime; }
        }

        BossBattle.DebugNoStagger = false;
        // ---------------------------------------------------------------- C 三人同時と連携技
        w = 0f; while (fin.Current != ReaperFinaleBattle.Stage.Group && w < 20f) { yield return null; w += Time.deltaTime; }
        w = 0f; while (fin.Active.Count(x => x != null && !x.IsDead) < 3 && w < 10f) { yield return null; w += Time.deltaTime; }
        L($"[sisters] C group: {fin.Active.Count} sisters");
        int combo0 = ReaperSisterBoss.CombosRun;
        var parts = new List<(float t, string who)>();
        var seen = new HashSet<ReaperSisterBoss>();
        float t0 = Time.time; bool shot = false, shotCombo = false;
        fpsSum = 0f; fpsN = 0; fpsMin = 999f;
        int bullets0 = ReaperBullet.Spawned, maxLive = 0;
        while (Time.time - t0 < 34f && !gm.IsGameOver)
        {
            foreach (var s in fin.Active)
            {
                if (s == null) continue;
                if (s.LastMove.StartsWith("DanseMacabre") && !seen.Contains(s) && ReaperSisterBoss.CombosRun > combo0) { seen.Add(s); parts.Add((Time.time, s.sister.ToString())); }
            }
            if (!shot && Time.time - t0 > 6f && ReaperBullet.Live.Count >= 4) { shot = true; Shot("sisters_group_bullets"); }
            if (!shotCombo && parts.Count >= 2) { shotCombo = true; Shot("sisters_danse_macabre"); }
            maxLive = Mathf.Max(maxLive, ReaperBullet.Live.Count);
            BotIntercept();
            if (Time.frameCount - lastShotFrame > 3) SampleFps();
            yield return null;
        }
        parts.Sort((a, b) => a.t.CompareTo(b.t));
        L($"[sisters] C combo parts: {string.Join(", ", parts.Select(p => $"{p.who}@{p.t - t0:F1}s"))} cued {fin.CombosCued}; bullets {ReaperBullet.Spawned - bullets0}, most alive at once {maxLive} | {PerfLine()}");
        bool order = parts.Count >= 3 && parts[0].who == "Eldest" && parts[1].who == "Second" && parts[2].who == "Youngest";
        bool spaced = parts.Count >= 3 && parts[1].t - parts[0].t > 0.5f && parts[2].t - parts[1].t > 0.5f;
        Check(fin.CombosCued >= 1 && order && spaced, "C: Danse Macabre runs as Eldest -> Second -> Youngest with time gaps");
        Check(maxLive <= 45, $"C: not a flood of bullets with all three (most alive at once {maxLive})");
        L($"[sisters] totals: spawned {ReaperBullet.Spawned} ({string.Join(", ", ReaperBullet.SpawnedByKind.Select(kv => kv.Key + " " + kv.Value))}), broken {ReaperBullet.BrokenCount}, reflected {ReaperBullet.ReflectedCount}, reflected hits on bosses {ReaperBullet.ReflectHits}, player hits by bullets {ReaperBullet.PlayerHits}, bites {ReaperBite.Spawned} (player hits {ReaperBite.PlayerHits}), player hits total {hits}");
        L($"[sisters] moves: {string.Join(", ", ReaperSisterBoss.MoveCounts.Select(kv => kv.Key + " " + kv.Value))}");
        L($"[sisters] hits by reason: {string.Join(", ", hitWhy.Select(kv => kv.Key + " " + kv.Value))}; bot jumps over the low sweep {BotSeverJumps}");
        foreach (var s in fin.Active.ToArray()) if (s != null && !s.IsDead) s.TakeDamage(9999999, s.CenterWorld);
        keepAlive = false;
    }

    void LogMarks(string label)
    {
        var parts = new List<string>();
        foreach (var m in ReaperFrameFollower.Live)
        {
            if (m == null) continue;
            var r = m.GetComponent<SpriteRenderer>();
            Vector3 d = m.transform.position - pc.transform.position;
            parts.Add($"{m.name} d=({d.x:F1},{d.y:F1}) a={(r != null ? r.color.a : -1f):F2} order={(r != null ? r.sortingOrder : 0)} scale={m.transform.localScale.x:F1}x{m.transform.localScale.y:F1} spr={(r != null && r.sprite != null ? r.sprite.rect.width.ToString() : "null")}");
        }
        L($"[sisters] marks at {label}: {(parts.Count == 0 ? "(none)" : string.Join(" | ", parts))}");
    }

    // 重いフレーム(50ms 超)が出た時に、何が起きていたかを残す
    IEnumerator SpikeWatch()
    {
        int n = 0;
        while (n < 40)
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0.05f && Time.frameCount - lastShotFrame > 3) // 画面の保存(撮影)の直後の重いフレームは除く
            {
                n++;
                var s = ReaperSisterBoss.All.FirstOrDefault(x => x != null && !x.IsDead);
                L($"[sisters] spike {dt * 1000f:F0}ms t={Time.time:F1}: {(s != null ? s.sister + " " + s.LastMove + " " + s.DebugAct : "-")} live bullets {ReaperBullet.Live.Count}, spawned {ReaperBullet.Spawned}, reflected {ReaperBullet.ReflectedCount}, broken {ReaperBullet.BrokenCount}, ts {Time.timeScale:F2}");
            }
            yield return null;
        }
    }

    // 見比べ用: 3姉妹 × 3種類を止めて並べて撮る(行 = 長女/次女/三女、列 = 壊せる/返せる/返せない)
    IEnumerator Lineup(ReaperSisterBoss owner)
    {
        var made = new List<ReaperBullet>();
        var shapes = new[] { ReaperBulletShape.Crescent, ReaperBulletShape.Orb, ReaperBulletShape.Blade };
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
            {
                var kind = (ReaperBulletKind)c;
                Vector3 p = pc.transform.position + new Vector3(4.5f + c * 3.2f, 7.5f - r * 2.6f, 0f);
                var b = ReaperBullet.Spawn(owner, kind, shapes[r], p, new Vector2(1.1f, 1.0f), Vector2.zero, 3f);
                b.sister = (ReaperSister)r;
                b.SendMessage("Recolor", SendMessageOptions.DontRequireReceiver);
                made.Add(b);
            }
        yield return new WaitForSeconds(0.4f);
        Shot("lineup_kinds_by_sister");
        yield return null; yield return null;
        foreach (var b in made) if (b != null) Destroy(b.gameObject);
        yield return new WaitForSeconds(0.3f);
    }

    // ---------------------------------------------------------------- A の道具
    ReaperBullet SpawnAtPlayer(ReaperSisterBoss owner, ReaperBulletKind kind, Vector2 offset, Vector2 vel)
    {
        Vector3 p = pc.transform.position + new Vector3(offset.x, offset.y, 0f);
        var shape = kind == ReaperBulletKind.Hazard ? ReaperBulletShape.Spike : ReaperBulletShape.Orb;
        return ReaperBullet.Spawn(owner, kind, shape, p, new Vector2(1.0f, 1.0f), vel, 4f);
    }

    IEnumerator ReflectProbe(ReaperSisterBoss owner, string label, PlayerController.FlickDirection f, bool air, Vector2 offset, Vector2 vel, float triggerDist)
    {
        float w = 0f;
        while ((pc.IsAttacking || !pc.IsGrounded) && w < 3f) { yield return null; w += Time.deltaTime; }
        yield return new WaitForSeconds(0.3f);
        int r0 = ReaperBullet.ReflectedCount, h0 = ReaperBullet.ReflectHits, ph0 = ReaperBullet.PlayerHits;
        int hp0 = owner != null ? owner.Hp : 0;
        if (air) { yield return Flick(PlayerController.FlickDirection.Up); yield return new WaitForSeconds(0.28f); }
        var b = SpawnAtPlayer(owner, ReaperBulletKind.Reflectable, offset, vel);
        bool fired = false;
        float t = 0f;
        while (t < 2.5f && b != null && !b.reflected)
        {
            t += Time.deltaTime;
            float d = Vector2.Distance(b.transform.position, pc.transform.position + new Vector3(0f, 0.9f, 0f));
            if (!fired && d < triggerDist) { fired = true; StartCoroutine(Flick(f)); }
            yield return null;
        }
        bool reflected = ReaperBullet.ReflectedCount > r0;
        float t2 = 0f;
        while (reflected && t2 < 2.5f && ReaperBullet.ReflectHits == h0) { t2 += Time.deltaTime; yield return null; }
        int dmg = owner != null ? hp0 - owner.Hp : 0;
        L($"[sisters] A {label}: reflected={reflected} bossHit={ReaperBullet.ReflectHits > h0} bossDamage={dmg} stagger={(owner != null ? owner.StaggerFraction : 0f):F2} playerHit={ReaperBullet.PlayerHits > ph0}");
        Check(reflected && ReaperBullet.ReflectHits > h0 && dmg > 0, $"A: a reflectable bullet is sent back by the {label} attack and hits the sister");
        if (b != null) Destroy(b.gameObject);
        yield return new WaitForSeconds(0.6f);
    }

    IEnumerator KindProbe(ReaperSisterBoss owner, ReaperBulletKind kind)
    {
        float w = 0f;
        while ((pc.IsAttacking || !pc.IsGrounded) && w < 3f) { yield return null; w += Time.deltaTime; }
        yield return new WaitForSeconds(0.3f);
        int hz0 = ReaperBullet.HazardIgnored, br0 = ReaperBullet.BrokenCount, rf0 = ReaperBullet.ReflectedCount;
        var b = SpawnAtPlayer(owner, kind, new Vector2(4.5f, 1.0f), new Vector2(-8f, 0f));
        bool fired = false, aliveAfterAttack = false;
        float t = 0f, firedAt = -1f;
        while (t < 2f && b != null)
        {
            t += Time.deltaTime;
            float d = Vector2.Distance(b.transform.position, pc.transform.position + new Vector3(0f, 0.9f, 0f));
            if (!fired && d < 1.9f) { fired = true; firedAt = t; StartCoroutine(Flick(PlayerController.FlickDirection.Forward)); }
            if (fired && t - firedAt > 0.12f && t - firedAt < 0.2f && b != null) aliveAfterAttack = true;
            yield return null;
        }
        if (kind == ReaperBulletKind.Hazard)
        {
            L($"[sisters] A hazard: touched by the attack {ReaperBullet.HazardIgnored - hz0}, broken {ReaperBullet.BrokenCount - br0}, reflected {ReaperBullet.ReflectedCount - rf0}");
            Check(ReaperBullet.HazardIgnored > hz0 && ReaperBullet.BrokenCount == br0 && ReaperBullet.ReflectedCount == rf0, "A: a hazard (unreflectable) bullet is not removed or reflected by an attack");
        }
        if (b != null) Destroy(b.gameObject);
        yield return new WaitForSeconds(0.6f);
    }

    // ---------------------------------------------------------------- B の道具
    IEnumerator ObserveMove(ReaperSisterBoss b, int mv, string label, bool shots)
    {
        int s0 = ReaperBullet.Spawned, br0 = ReaperBullet.BrokenCount, rf0 = ReaperBullet.ReflectedCount, rh0 = ReaperBullet.ReflectHits, ph0 = ReaperBullet.PlayerHits, bite0 = ReaperBite.Spawned, hits0 = hits;
        // 今の技が終わって待っている所で、次の技を指定する(自分で次の技を始めないように)
        ReaperSisterBoss.DebugHold = true;
        float wh = 0f; while (b != null && !b.DebugHolding && wh < 12f) { BotIntercept(); yield return null; wh += Time.deltaTime; }
        wh = 0f; while (ReaperBullet.Live.Count > 0 && wh < 4f) { BotIntercept(); yield return null; wh += Time.deltaTime; }
        var kinds0 = new Dictionary<ReaperBulletKind, int>(ReaperBullet.SpawnedByKind);
        s0 = ReaperBullet.Spawned; br0 = ReaperBullet.BrokenCount; rf0 = ReaperBullet.ReflectedCount; rh0 = ReaperBullet.ReflectHits; ph0 = ReaperBullet.PlayerHits; bite0 = ReaperBite.Spawned; hits0 = hits;
        int started0 = b.MovesStarted;
        b.DebugForceMove = mv;
        ReaperSisterBoss.DebugHold = false;
        float w = 0f;
        while (b != null && b.MovesStarted == started0 && w < 12f) { BotIntercept(); yield return null; w += Time.deltaTime; }
        string name = b != null ? b.LastMove : "?";
        bool sawTelegraph = false, frontOk = true, meleeSeen = false, shotBullets = false, shotWindup = false;
        float feetMin = 99f, teleT0 = 0f;
        float t = 0f; fpsSum = 0f; fpsN = 0; fpsMin = 999f;
        while (b != null && b.MovesStarted == started0 + 1 && t < 9f)
        {
            t += Time.deltaTime;
            if (b.DebugAct == "Telegraph")
            {
                if (!sawTelegraph) teleT0 = t;
                sawTelegraph = true;
                // 予兆が始まってから 0.5秒(印が濃くなった所)で撮る
                if (shots && !shotWindup && t - teleT0 > 0.5f) { shotWindup = true; Shot(label + "_windup"); LogMarks(label); }
            }
            foreach (var hn in new[] { "sever", "swoop" })
                if (b.DebugHitboxActive(hn))
                {
                    float? g = TerrainManager.Instance.GetHeightAt(pc.transform.position.x);
                    var pcol = pc.GetComponent<Collider2D>();
                    if (g.HasValue && pcol != null) feetMin = Mathf.Min(feetMin, pcol.bounds.min.y - g.Value);
                    meleeSeen = true;
                    float hx = b.DebugHitboxCenter(hn).x, bx = b.transform.position.x;
                    if (!(hx < bx + 0.3f)) frontOk = false; // 正面(プレイヤー側 = 左)にあるか
                }
            if (shots && !shotBullets && ReaperBullet.Live.Count >= 3) { shotBullets = true; StartCoroutine(ShotLater(label + "_bullets", 0.25f)); }
            BotIntercept();
            if (Time.frameCount - lastShotFrame > 3) SampleFps();
            yield return null;
            if (b != null && b.DebugAct == "Wait" && t > 1.5f && b.LastMove == name && ReaperBullet.Live.Count == 0) break;
        }
        yield return new WaitForSeconds(0.8f);
        var kinds = ReaperBullet.SpawnedByKind.Select(kv => $"{kv.Key} {kv.Value - (kinds0.TryGetValue(kv.Key, out int k0) ? k0 : 0)}").Where(x => !x.EndsWith(" 0"));
        L($"[sisters] B {label}: move={name} telegraph={sawTelegraph} melee={(meleeSeen ? (frontOk ? "front" : "BEHIND") + $" (player feet min {feetMin:F1}m above ground while active)" : "-")} bullets {ReaperBullet.Spawned - s0} [{string.Join(", ", kinds)}] bites {ReaperBite.Spawned - bite0} | bot: broke {ReaperBullet.BrokenCount - br0}, reflected {ReaperBullet.ReflectedCount - rf0}, reflected hits {ReaperBullet.ReflectHits - rh0} | player hit {hits - hits0} (bullets {ReaperBullet.PlayerHits - ph0}) | {PerfLine()}");
        Check(sawTelegraph, $"B {label}: the move shows a telegraph first");
        if (meleeSeen) Check(frontOk, $"B {label}: the melee hit area is in front of the sister");
        bool needsBullets = name != "EarthSever" || true;
        Check(ReaperBullet.Spawned - s0 > 0 || ReaperBite.Spawned - bite0 > 0, $"B {label}: the move produced its attack (bullets/bites)");
    }

    // ボット: 近づいた壊せる/返せる弾を、向きに合わせた攻撃で迎え撃つ(返せない弾は跳んで避ける)。
    //  大地断ちは予兆の終わりに跳ぶ(低い横薙ぎは跳べば越えられることの確認)
    float lastIntercept;
    readonly Dictionary<ReaperSisterBoss, float> teleStart = new Dictionary<ReaperSisterBoss, float>();
    readonly HashSet<ReaperSisterBoss> jumpedThisTele = new HashSet<ReaperSisterBoss>();
    public static int BotSeverJumps;
    void BotIntercept()
    {
        foreach (var s in ReaperSisterBoss.All)
        {
            if (s == null || s.IsDead) continue;
            bool tele = s.DebugAct == "Telegraph";
            if (tele && !teleStart.ContainsKey(s)) { teleStart[s] = Time.time; jumpedThisTele.Remove(s); }
            if (!tele && s.DebugAct != "Strike") teleStart.Remove(s);
            if (tele && s.LastMove == "EarthSever" && !jumpedThisTele.Contains(s) && teleStart.TryGetValue(s, out float ts0) && Time.time - ts0 > 0.78f && pc.IsGrounded)
            { jumpedThisTele.Add(s); BotSeverJumps++; lastIntercept = Time.time; StartCoroutine(Flick(PlayerController.FlickDirection.Up)); return; }
        }
        if (Time.time - lastIntercept < 0.22f) return;
        Vector3 me = pc.transform.position + new Vector3(0f, 0.9f, 0f);
        ReaperBullet near = null; float bd = 99f;
        foreach (var b in ReaperBullet.Live)
        {
            if (b == null || b.reflected) continue;
            float d = Vector2.Distance(b.transform.position, me);
            if (d < bd) { bd = d; near = b; }
        }
        if (near == null || bd > (near.kind == ReaperBulletKind.Hazard ? 3.4f : 2.6f)) return;
        lastIntercept = Time.time;
        Vector3 rel = near.transform.position - me;
        PlayerController.FlickDirection f;
        if (near.kind == ReaperBulletKind.Hazard) { if (pc.IsGrounded && rel.y < 1.0f && bd < 3.4f) f = PlayerController.FlickDirection.Up; else return; }
        else if (rel.y > 1.4f) f = PlayerController.FlickDirection.Up;
        else if (!pc.IsGrounded && rel.y < -0.6f) f = PlayerController.FlickDirection.Down;
        else f = rel.x < -0.4f ? PlayerController.FlickDirection.Backward : PlayerController.FlickDirection.Forward;
        StartCoroutine(Flick(f));
    }
}
#endif
