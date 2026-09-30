#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// 攻撃判定の調整と高速時の相打ち対策(2026-09-30)の計測。
//  -qaTrade <dir> [-qaChars a,b] [-qaSpeeds 30,100,200,300] [-qaTaus 0.25,...,0] [-qaTradeModes off,on]
//  1) 規則の確認(黒剣士): 空振りで猶予が出ない / 猶予は命中した敵だけ・約0.15秒で切れる / 猶予中も他の被弾は通る
//  2) 判定の大きさ: 全キャラの前攻撃の判定を、調整なし/ありで30km/hと200km/hで測る
//  3) 相打ちの頻度: 平らな道で雑魚(倒れないようHPを大きくしたゴブリン)へ走って近づき、体と敵の隙間が gap になった
//     瞬間(=体が敵に触れるまでの残り時間がtau秒)に前攻撃を出す。命中と体の接触ダメージの時刻から CLEAN/TRADE(相打ち)/LATE/HURT/WHIFF に分ける。
//     off=改修前(MeleeReach/Contact Graceなし)、on=改修後。
public partial class QaSweep
{
    static readonly string[] TradeChars = { "swordsman", "dual_blade", "noble_lady", "gunslinger", "dragon_lancer", "archer", "mage", "fighter", "ninja", "miko", "vampire", "dragonkin" };

    class TradeRec { public string ch, mode, result; public float kmh, gap; }
    readonly List<TradeRec> tradeRecs = new List<TradeRec>();
    readonly Dictionary<string, float> frontAt30 = new Dictionary<string, float>();

    IEnumerator TradeMode()
    {
        Application.targetFrameRate = 60;
        string[] chars = Arg("-qaChars", string.Join(",", TradeChars)).Split(',');
        float[] speeds = Arg("-qaSpeeds", "30,100,200,300").Split(',').Select(float.Parse).ToArray();
        float[] gaps = Arg("-qaTaus", "0.25,0.2,0.167,0.133,0.117,0.1,0.083,0.067,0.05,0.033,0.017,0").Split(',').Select(float.Parse).ToArray();
        string[] modes = Arg("-qaTradeModes", "off,on").Split(',');

        if (Arg("-qaSkipRules", "0") != "1")
        {
            yield return BeginRun("swordsman", "wasteland_road");
            TradeQuiet();
            yield return GraceRules();
            yield return EndRun();
        }

        foreach (string ch in chars)
        {
            L($"\n===== {ch}");
            yield return BeginRun(ch, "wasteland_road");
            TradeQuiet();
            yield return new WaitForSeconds(0.5f);
            yield return ReachReport(ch);
            foreach (string m in modes)
            {
                SetTradeFeature(m == "on");
                foreach (float kmh in speeds)
                    foreach (float gap in gaps)
                        yield return TradeTrial(ch, kmh, gap, m);
            }
            SetTradeFeature(true);
            PlayerController.DebugSpeedScale = 1f;
            yield return EndRun();
        }
        SetTradeFeature(true);

        // 集計
        string[] kinds = { "CLEAN", "TRADE", "LATE", "HURT", "WHIFF", "OTHER" };
        L("\n===== summary (count per result; trade = attack landed and the same enemy's body hurt the player within 0.25s)");
        foreach (float kmh in speeds)
            foreach (string m in modes)
            {
                var rs = tradeRecs.Where(r => r.kmh == kmh && r.mode == m).ToList();
                L($"{kmh,4:F0}km/h {m,-3}: " + string.Join(" ", kinds.Select(k => $"{k}={rs.Count(r => r.result == k)}")) + $"  (n={rs.Count})");
            }
        L("\n===== per character: TRADE count off -> on (all speeds)");
        foreach (string ch in chars)
        {
            int off = tradeRecs.Count(r => r.ch == ch && r.mode == "off" && r.result == "TRADE");
            int on = tradeRecs.Count(r => r.ch == ch && r.mode == "on" && r.result == "TRADE");
            int hitsOn = tradeRecs.Count(r => r.ch == ch && r.mode == "on" && (r.result == "CLEAN" || r.result == "TRADE" || r.result == "LATE"));
            string bySpeed = string.Join(" ", speeds.Select(k => $"{k:F0}:{tradeRecs.Count(r => r.ch == ch && r.mode == "off" && r.kmh == k && r.result == "TRADE")}->{tradeRecs.Count(r => r.ch == ch && r.mode == "on" && r.kmh == k && r.result == "TRADE")}"));
            L($"{ch,-14} TRADE {off,2} -> {on,2}   [{bySpeed}]   hits(on)={hitsOn}");
            if (modes.Contains("on")) Check(hitsOn > 0, $"{ch}: attacks still land on an enemy");
            if (modes.Contains("off") && modes.Contains("on") && on > off) Warn($"{ch}: more trades after the change ({off} -> {on})");
        }
        if (frontAt30.TryGetValue("fighter", out float ff))
            foreach (var kv in frontAt30)
                if (kv.Key != "fighter" && kv.Key != "noble_lady" && kv.Key != "ninja") Check(ff <= kv.Value + 0.01f, $"fighter keeps the shortest melee reach (fighter {ff:F2}m vs {kv.Key} {kv.Value:F2}m)");
    }

    void SetTradeFeature(bool on)
    {
        MeleeReach.Enabled = on;
        PlayerController.ContactGraceEnabled = on;
    }

    void TradeQuiet()
    {
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
        foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
        TerrainManager.Instance.pitChanceBase = 0f; TerrainManager.Instance.pitChanceMax = 0f; TerrainManager.Instance.enemySpawnChance = 0f;
        SetPrivate(gm, "expGainMultiplier", 0f); // レベルアップのカード選択でキャラの性能が変わらないように
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false); // 被弾を数えるので無敵は切る(KeepAliveがHPを戻す)
        ClearShield();
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
        foreach (var o in FindObjectsByType<ObstacleController>(FindObjectsSortMode.None)) Destroy(o.gameObject);
    }

    void HoldKmh(float kmh) => SetKmh(kmh);
    static Bounds BoxWorldBounds(BoxCollider2D c)
    {
        var t = c.transform; Vector2 h = c.size * 0.5f;
        var b = new Bounds(t.TransformPoint(c.offset), Vector3.zero);
        foreach (var d in new[] { new Vector2(-h.x, -h.y), new Vector2(h.x, -h.y), new Vector2(h.x, h.y), new Vector2(-h.x, h.y) }) b.Encapsulate(t.TransformPoint(c.offset + d));
        return b;
    }
    void ClearShield() { var p = typeof(PlayerController).GetProperty("ShieldCharges"); if (p != null) p.SetValue(pc, 0); }

    IEnumerator WaitReady(float kmh, float timeout = 6f)
    {
        float w = 0f;
        while (w < timeout && (pc.IsReacting || pc.IsHitInvincible || !pc.IsGrounded || pc.IsAttacking)) { HoldKmh(kmh); yield return null; w += Time.deltaTime; }
        ClearShield();
    }

    EnemyController SpawnGoblinAt(float x, string name, int hp)
    {
        var def = EnemyDatabase.FindById("goblin");
        float? gy = TerrainManager.Instance.GetHeightAt(x);
        var go = TerrainManager.Instance.SpawnEncounterEnemy(def, new Vector2(x, gy ?? pc.transform.position.y), EnemyAiTier.T0, def.behaviorKind);
        if (go == null) return null;
        go.name = name;
        var ec = go.GetComponent<EnemyController>();
        if (ec != null && hp > 0) SetPrivate(ec, "hp", hp);
        return ec;
    }

    // ---------------------------------------------------------------- 1) 規則
    IEnumerator GraceRules()
    {
        L("\n===== contact grace rules (swordsman, 20km/h)");
        const float kmh = 20f;
        SetTradeFeature(true);
        yield return WaitReady(kmh);

        // 空振り: 猶予は出ない
        int g0 = PlayerController.ContactGraceGranted;
        yield return Flick(PlayerController.FlickDirection.Forward);
        for (float w = 0f; w < 0.6f; w += Time.deltaTime) { HoldKmh(kmh); yield return null; }
        Check(PlayerController.ContactGraceGranted == g0, $"a whiffed attack grants no contact grace (granted {g0} -> {PlayerController.ContactGraceGranted})");
        L($"[rule] whiff: grace granted {PlayerController.ContactGraceGranted - g0} time(s)");

        // 命中した敵Aの体: 猶予中は被弾しない → 約0.15秒で切れ、重なったままなら1回被弾する
        yield return WaitReady(kmh);
        var a = SpawnGoblinAt(pc.transform.position.x + 0.2f, "QA_EnemyA", 9999);
        pc.NotifyAttackLanded(a, null);
        float t0 = Time.time, lastDmg = pc.LastDamageTime, hurtAt = -1f; string src = null;
        int blocked0 = PlayerController.ContactGraceBlocked;
        while (Time.time - t0 < 0.8f && a != null)
        {
            HoldKmh(kmh);
            a.transform.position = new Vector3(pc.transform.position.x + 0.2f, a.transform.position.y, 0f);
            if (pc.LastDamageTime != lastDmg) { hurtAt = Time.time - t0; src = pc.LastDamageSource; break; }
            yield return null;
        }
        L($"[rule] enemy A hit by the player, body kept overlapping: blocked={PlayerController.ContactGraceBlocked - blocked0} hurt at {(hurtAt < 0 ? "never" : hurtAt.ToString("F3") + "s")} src={src}");
        Check(PlayerController.ContactGraceBlocked > blocked0, "contact with the enemy that was just hit is held back during the grace");
        Check(hurtAt >= 0.12f && hurtAt <= 0.3f && src != null && src.Contains("QA_EnemyA"), $"the grace ends after ~{pc.ContactGraceDuration:F2}s and the still-overlapping body hurts once (hurt at {hurtAt:F3}s)");
        if (a != null) Destroy(a.gameObject);

        // 猶予は命中した敵だけ: Aに命中、Bの体には普通に当たる
        yield return WaitReady(kmh);
        a = SpawnGoblinAt(pc.transform.position.x + 6f, "QA_EnemyA", 9999);
        var b = SpawnGoblinAt(pc.transform.position.x + 0.2f, "QA_EnemyB", 9999);
        pc.NotifyAttackLanded(a, null);
        t0 = Time.time; lastDmg = pc.LastDamageTime; hurtAt = -1f; src = null;
        while (Time.time - t0 < 0.5f && b != null)
        {
            HoldKmh(kmh);
            b.transform.position = new Vector3(pc.transform.position.x + 0.2f, b.transform.position.y, 0f);
            if (a != null) a.transform.position = new Vector3(pc.transform.position.x + 6f, a.transform.position.y, 0f);
            if (pc.LastDamageTime != lastDmg) { hurtAt = Time.time - t0; src = pc.LastDamageSource; break; }
            yield return null;
        }
        L($"[rule] grace on A, B touches: hurt at {(hurtAt < 0 ? "never" : hurtAt.ToString("F3") + "s")} src={src}");
        Check(hurtAt >= 0f && hurtAt < 0.1f && src != null && src.Contains("QA_EnemyB"), "another enemy's body still hurts while the grace is on the hit enemy");
        if (a != null) Destroy(a.gameObject);
        if (b != null) Destroy(b.gameObject);

        // 猶予中でも地形/障害物/敵の攻撃は通る(被弾の入口は猶予を見ない)
        yield return WaitReady(kmh);
        a = SpawnGoblinAt(pc.transform.position.x + 6f, "QA_EnemyA", 9999);
        pc.NotifyAttackLanded(a, null);
        lastDmg = pc.LastDamageTime;
        pc.TakeDamage(source: "Obstacle:QA");
        Check(pc.LastDamageTime != lastDmg && pc.LastDamageSource == "Obstacle:QA", "hazard damage is not blocked by the contact grace");
        yield return null;
        yield return WaitReady(kmh);
        pc.NotifyAttackLanded(a, null);
        lastDmg = pc.LastDamageTime;
        pc.TakeDamage(source: "Fireball:QA");
        Check(pc.LastDamageTime != lastDmg, "enemy projectile damage is not blocked by the contact grace");
        if (a != null) Destroy(a.gameObject);

        // 改修前(猶予なし)の比較: 同じ状況で即座に被弾する
        yield return WaitReady(kmh);
        SetTradeFeature(false);
        a = SpawnGoblinAt(pc.transform.position.x + 0.2f, "QA_EnemyA", 9999);
        pc.NotifyAttackLanded(a, null);
        t0 = Time.time; lastDmg = pc.LastDamageTime; hurtAt = -1f;
        while (Time.time - t0 < 0.5f && a != null)
        {
            HoldKmh(kmh);
            a.transform.position = new Vector3(pc.transform.position.x + 0.2f, a.transform.position.y, 0f);
            if (pc.LastDamageTime != lastDmg) { hurtAt = Time.time - t0; break; }
            yield return null;
        }
        L($"[rule] grace OFF: hurt at {(hurtAt < 0 ? "never" : hurtAt.ToString("F3") + "s")}");
        Check(hurtAt >= 0f && hurtAt < 0.1f, "with the grace switched off the same contact hurts immediately (old behaviour)");
        if (a != null) Destroy(a.gameObject);
        SetTradeFeature(true);
        yield return WaitReady(kmh);
    }

    // ---------------------------------------------------------------- 2) 判定の大きさ
    IEnumerator ReachReport(string ch)
    {
        foreach (float kmh in new[] { 30f, 200f })
            foreach (bool on in new[] { false, true })
            {
                SetTradeFeature(on);
                yield return WaitReady(kmh);
                for (float w = 0f; w < 0.3f; w += Time.deltaTime) { HoldKmh(kmh); yield return null; }
                yield return Flick(PlayerController.FlickDirection.Forward);
                MeleeReach seen = null; Bounds bb = default;
                for (float w = 0f; w < 0.8f && seen == null; w += Time.deltaTime)
                {
                    HoldKmh(kmh);
                    foreach (var r in pc.MeleeReaches)
                    {
                        var c = r.GetComponent<BoxCollider2D>();
                        if (c != null && c.enabled && r.gameObject.activeInHierarchy) { seen = r; bb = BoxWorldBounds(c); break; }
                    }
                    yield return null;
                }
                if (seen == null) { L($"[reach] {ch} {kmh:F0}km/h {(on ? "on " : "off")}: no melee hitbox (projectile/area attack)"); continue; }
                // 当たり判定(物理)の位置は体の当たり判定と同じ剛体に付いているので、体の前端を基準に測る(見た目のTransformとは高速時に数フレームずれる)
                var bodyB = BoxWorldBounds(pc.GetComponent<BoxCollider2D>()); // Transformから計算(物理の姿勢の更新待ちで高速時にずれるbounds値は使わない)
                float px = bodyB.max.x;
                float front = bb.max.x - px;
                L($"[reach] {ch} {kmh:F0}km/h {(on ? "on " : "off")}: {seen.name} x(from body front)=[{bb.min.x - px:F2}..{front:F2}] y(from feet)=[{bb.min.y - bodyB.min.y:F2}..{bb.max.y - bodyB.min.y:F2}] size {seen.LastBaseSize.x:F2}x{seen.LastBaseSize.y:F2} -> {seen.LastSize.x:F2}x{seen.LastSize.y:F2} speedAssist={seen.LastSpeedAssist:F2}m (max {seen.speedAssistMax:F2})");
                if (on)
                {
                    Check(seen.LastSpeedAssist <= seen.speedAssistMax + 1e-4f, $"{ch}: the high speed assist stays under its maximum");
                    if (kmh < 100f) Check(seen.LastSpeedAssist == 0f, $"{ch}: no high speed assist below 100km/h");
                    if (kmh < 100f) frontAt30[ch] = front;
                    if (ch == "dragon_lancer") Check(Mathf.Abs(seen.LastSize.y - seen.LastBaseSize.y) < 1e-4f, "dragon_lancer: the thrust keeps its thickness");
                }
            }
        SetTradeFeature(true);
    }

    // ---------------------------------------------------------------- 3) 相打ちの頻度
    IEnumerator TradeTrial(string ch, float kmh, float gap, string mode)
    {
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
        yield return WaitReady(kmh);
        var tm = TerrainManager.Instance;
        float mps = kmh / GameManager.KmhPerMps;
        float ahead = 4f + mps * 0.3f;
        { float w = 0f; while (w < 6f && !FlatAhead(tm, pc.transform.position.x - 1f, ahead + 4f)) { HoldKmh(kmh); yield return null; w += Time.deltaTime; } }
        yield return WaitReady(kmh);
        var ec = SpawnGoblinAt(pc.transform.position.x + ahead, "QA_Trade", 9999);
        if (ec == null) { Check(false, "goblin spawned"); yield break; }
        var ecol = ec.GetComponent<Collider2D>();
        var body = pc.GetComponent<BoxCollider2D>();
        int hp0 = ec.NetHp;
        float lastDmg = pc.LastDamageTime;
        bool fired = false; float tFire = -1f, tHit = -1f, tDmg = -1f, gapAtFire = 0f; string src = null;
        float t = 0f;
        var trace = new StringBuilder();
        while (t < 5f && ec != null)
        {
            HoldKmh(kmh);
            float g = ecol.bounds.min.x - body.bounds.max.x;
            if (fired && trace.Length < 6000)
            {
                string boxes = "";
                foreach (var r in pc.MeleeReaches) { var c = r.GetComponent<BoxCollider2D>(); if (c != null && c.enabled) boxes += $" {r.name}[{c.bounds.min.x - ecol.bounds.max.x:F2}..{c.bounds.max.x - ecol.bounds.min.x:F2}]"; }
                trace.Append($"     t={t - tFire:+0.000} gap={g:F2} enemyX-playerX={ec.transform.position.x - pc.transform.position.x:F2} canContact={(ec.CanDealContactDamage ? 1 : 0)} grace={(pc.HasContactGrace(ec) ? 1 : 0)} hp={ec.NetHp} atk={(pc.IsAttacking ? 1 : 0)} react={(pc.IsReacting ? 1 : 0)} boxes(rel. enemy):{boxes}\n");
            }
            if (!fired && g <= gap * mps) { fired = true; tFire = t; gapAtFire = g; StartCoroutine(Flick(PlayerController.FlickDirection.Forward)); }
            if (tHit < 0f && ec.NetHp < hp0) tHit = t;
            if (tDmg < 0f && pc.LastDamageTime != lastDmg) { tDmg = t; src = pc.LastDamageSource; }
            if (fired && t - tFire > 0.8f) break;
            if (!fired && g < -3f) break;
            yield return null; t += Time.deltaTime;
        }
        bool hit = tHit >= 0f;
        bool enemyDmg = tDmg >= 0f && src != null && src.StartsWith("Enemy:");
        string result;
        if (tDmg >= 0f && !enemyDmg) result = "OTHER";
        else if (hit && enemyDmg && tDmg - tHit <= 0.25f && tDmg - tHit >= -0.1f) result = "TRADE";
        else if (hit && enemyDmg && tDmg > tHit) result = "LATE";
        else if (enemyDmg) result = "HURT";
        else if (hit) result = "CLEAN";
        else result = "WHIFF";
        tradeRecs.Add(new TradeRec { ch = ch, mode = mode, kmh = kmh, gap = gap, result = result });
        L($"[trade] {ch,-13} {mode,-3} {kmh,4:F0}km/h tau={gap:F3}s (gap at fire {gapAtFire:F2}m) -> {result,-5} hit={(hit ? (tHit - tFire).ToString("+0.000") : "-")} hurt={(tDmg >= 0f ? (tDmg - tFire).ToString("+0.000") : "-")} {(src ?? "")}");
        string tr = Arg("-qaTrace", "");
        if (tr != "" && (tr == "all" || tr.Split('/').Contains(result))) L(trace.ToString().TrimEnd());
        if (ec != null) Destroy(ec.gameObject);
    }
}
#endif
