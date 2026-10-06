#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

// 全12キャラの攻撃判定の監査(2026-10-03)。 -qaHitAudit <dir>
//   R(reach): 立ち止まった状態(自動前進OFF)で、標準の敵(ゴブリン)を前方 d m に置いて1回だけ攻撃する。
//             当たったか / 攻撃中に体の接触ダメージを受けたか(置いた時に体が重なっていない距離だけ)を d ごとに記録。
//             同時に、出ていた攻撃判定(タグ PlayerAttack)の範囲・発生/終了の時間・踏み込みの距離を記録する。
//   A(aerial): 上攻撃 → ジャンプ → 空中攻撃 → 下攻撃 の流れ(打ち上げ / 空中で当たった数 / 置き去り / 叩きつけ)
//   S(speed):  50/100/125/150km/h で前から来る敵へ攻撃(すり抜け / 接触ダメージ / 飛び道具の出る位置)
//   F(first):  初撃/連撃中/締めの印(AttackSeqTag)が、各キャラの攻撃でどう付くか
//   引数: -qaHaChars a,b  -qaHaOnly RASF  -qaHaTs 2(時間倍率)
// 出力: hitaudit_reach.tsv / hitaudit_geom.tsv / hitaudit_aerial.tsv / hitaudit_speed.tsv / hitaudit_seq.tsv
public partial class QaSweep
{
    const BindingFlags HaInst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly FieldInfo HaEnemyHp = typeof(EnemyController).GetField("hp", HaInst);
    static readonly FieldInfo HaEnemyLaunched = typeof(EnemyController).GetField("isLaunched", HaInst);
    static readonly MethodInfo HaEnsureHp = typeof(EnemyController).GetMethod("EnsureHp", HaInst);

    readonly Dictionary<string, (float reach, float startup)> haForward = new Dictionary<string, (float, float)>();
    struct HaAction { public string name; public PlayerController.FlickDirection flick; public bool air; public bool behind; }
    static readonly HaAction[] HaActions =
    {
        new HaAction { name = "forward", flick = PlayerController.FlickDirection.Forward },
        new HaAction { name = "up", flick = PlayerController.FlickDirection.Up },
        new HaAction { name = "back", flick = PlayerController.FlickDirection.Backward, behind = true },
        new HaAction { name = "airDown", flick = PlayerController.FlickDirection.Down, air = true },
    };

    IEnumerator HitAuditMode()
    {
        var snapSave = SaveSystem.Capture();
        autoPickHold = true;
        string[] chars = Arg("-qaHaChars", string.Join(",", CharacterDatabase.AllCharacters.Select(c => c.characterId))).Split(',');
        string only = Arg("-qaHaOnly", "RASF");
        float ts = float.Parse(Arg("-qaHaTs", "2"), System.Globalization.CultureInfo.InvariantCulture);
        var reach = new StringBuilder("char\taction\td\tcontactDist\thit\tdamaged\tfirstActive\tlastActive\tlunge\n");
        var geom = new StringBuilder("char\taction\tminFwd\tmaxFwd\tminUp\tmaxUp\tfirstActive\tlastActive\tlunge\tprojectiles\tprojSpeed\tprojLife\tprojSize\tprojPierce\tplayerHalfW\tplayerTop\n");
        var aerial = new StringBuilder("char\tlaunched\tairHits\tslam\tmaxSeparation\tsequence\n");
        var speed = new StringBuilder("char\tkmh\tenemies\thit\tpassedNoHit\tdamaged\tprojBehind\n");
        var seq = new StringBuilder("char\tsequence\n");
        foreach (string ch in chars)
        {
            if (only.Contains('R')) yield return HaReach(ch, ts, reach, geom);
            if (only.Contains('A')) yield return HaAerial(ch, ts, aerial);
            if (only.Contains('S')) yield return HaSpeed(ch, speed);
            if (only.Contains('F')) yield return HaSeq(ch, seq);
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "hitaudit_reach.tsv"), reach.ToString());
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "hitaudit_geom.tsv"), geom.ToString());
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "hitaudit_aerial.tsv"), aerial.ToString());
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "hitaudit_speed.tsv"), speed.ToString());
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "hitaudit_seq.tsv"), seq.ToString());
        }
        TimeControl.SetDebugTimeScale(1f);
        PlayerController.DebugRunOnlyScale = 1f;
        autoPickHold = false;
        SaveSystem.Restore(snapSave);
        CardInventory.ReloadFromPrefs();
        RunCheckpoint.Reload();
        L("[hitaudit] test machine save restored");
    }

    // ---------------------------------------------------------------- 共通
    IEnumerator HaBegin(string ch, string stage = "natural_cave")
    {
        yield return BeginRun(ch, stage); // 一本道(上下ルートの分岐が無い)
        GameManager.BlockExpGain = true;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false); // the dev invincible setting would hide contact damage
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
        var tm = TerrainManager.Instance;
        if (tm != null) tm.enemySpawnChance = 0f; // 普通の敵は出さない(このテストの敵だけ)
        yield return new WaitForSecondsRealtime(0.3f);
    }

    IEnumerator HaEnd()
    {
        GameManager.BlockExpGain = false;
        PlayerController.DebugRunOnlyScale = 1f;
        TimeControl.SetDebugTimeScale(1f);
        yield return EndRun();
    }

    // 体の中心がプレイヤーの前方 d m の地面に立つ敵(HPは大きく、攻撃しない)
    EnemyController HaSpawn(float d, string id = "goblin")
    {
        // 狙った位置へ直接出す(DebugSpawnEnemy は画面の右端の地面を探すので、穴/障害物があると失敗していた)。
        // 狙った位置が穴なら、地面のある所まで前へずらす。
        var tm = TerrainManager.Instance;
        var def = EnemyDatabase.FindById(id);
        if (tm == null || def == null) return null;
        float wantX = pc.transform.position.x + d * HaFacing();
        for (int k = 0; k < 20 && !tm.GetHeightAt(wantX).HasValue; k++) wantX += 0.5f * HaFacing();
        float baseY = tm.GetHeightAt(wantX) ?? pc.transform.position.y;
        var go = tm.SpawnEncounterEnemy(def, new Vector2(wantX, baseY), EnemyAiTier.T0, def.behaviorKind);
        if (go == null) return null;
        d = (wantX - pc.transform.position.x) * HaFacing();
        var en = go.GetComponent<EnemyController>();
        if (en == null) { Destroy(go); return null; }
        HaEnsureHp.Invoke(en, null);
        HaEnemyHp.SetValue(en, 100000);
        var sb = go.GetComponent<EnemySpecialBehavior>();
        if (sb != null) sb.enabled = false;
        float x = pc.transform.position.x + d * HaFacing();
        float gy = TerrainManager.Instance.GetHeightAt(x) ?? pc.transform.position.y;
        var col = en.GetComponentInChildren<Collider2D>();
        float lift = col != null ? go.transform.position.y - col.bounds.min.y : 0f;
        go.transform.position = new Vector3(x - (col != null ? col.bounds.center.x - go.transform.position.x : 0f), gy + lift, go.transform.position.z);
        return en;
    }

    static Collider2D HaBodyOf(EnemyController en)
    {
        if (en == null) return null;
        foreach (var c in en.GetComponents<Collider2D>()) if (c.enabled) return c;
        foreach (var c in en.GetComponentsInChildren<Collider2D>()) if (c.enabled && c.GetComponent<EnemyMeleeHitbox>() == null) return c;
        return null;
    }

    float HaFacing() => pc.transform.localScale.x < 0f ? -1f : 1f;
    int HaHp(EnemyController en) => en != null ? (int)HaEnemyHp.GetValue(en) : 0;
    Collider2D HaBody() => pc.GetComponent<Collider2D>();

    // 今出ている、このプレイヤーの攻撃判定(近接の箱/飛び道具/爆発/ゾーン)
    readonly List<Collider2D> haCols = new List<Collider2D>();
    void HaActiveAttackCols()
    {
        haCols.Clear();
        foreach (var info in PlayerAttackInfo.Active)
        {
            if (info == null || !info.isActiveAndEnabled) continue;
            var c = info.col != null ? info.col : info.GetComponent<Collider2D>();
            if (c != null && c.enabled) haCols.Add(c);
        }
    }

    IEnumerator HaWaitIdle(float max = 3f)
    {
        float w = 0f;
        while ((pc.IsAttacking || pc.IsReacting || pc.IsHitInvincible || !pc.IsGrounded) && w < max) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSeconds(0.25f);
    }

    IEnumerator HaFlick(PlayerController.FlickDirection f)
    {
        pc.debugInjectFlick = f;
        yield return null; yield return null;
        if (pc != null) pc.debugInjectFlick = null;
    }

    void HaPlace(Vector3 pos)
    {
        FreezeDiagnostics.NoteIntendedMove("hit audit reset");
        float gy = TerrainManager.Instance.GetHeightAt(pos.x) ?? pos.y;
        pc.transform.position = new Vector3(pos.x, gy + pc.groundOffset, pos.z);
    }

    // ---------------------------------------------------------------- R
    IEnumerator HaReach(string ch, float ts, StringBuilder reach, StringBuilder geom)
    {
        yield return HaBegin(ch);
        pc.autoRunEnabled = false;
        TimeControl.SetDebugTimeScale(ts);
        yield return new WaitForSeconds(0.3f);
        Vector3 home = pc.transform.position;
        var body = HaBody();
        float playerHalfW = body.bounds.extents.x;
        float playerTop = body.bounds.max.y - pc.transform.position.y;
        // 敵の体の半分の幅
        var probe = HaSpawn(6f);
        yield return null;
        float enemyHalfW = probe != null ? probe.GetComponentInChildren<Collider2D>().bounds.extents.x : 0.4f;
        if (probe != null) Destroy(probe.gameObject);
        float contactDist = playerHalfW + enemyHalfW;
        var summary = new List<string>();
        foreach (var act in HaActions)
        {
            float maxHit = -1f, minSafeHit = -1f; int damagedTrials = 0, safeTrials = 0;
            float gMinF = float.MaxValue, gMaxF = float.MinValue, gMinU = float.MaxValue, gMaxU = float.MinValue, gFirst = -1f, gLast = -1f, gLunge = 0f;
            int projCount = 0; float projSpeed = 0f, projLife = 0f; Vector2 projSize = Vector2.zero; int projPierce = 0;
            for (float d = 0f; d <= 4.01f; d += 0.2f)
            {
                yield return HaWaitIdle();
                HaPlace(home);
                yield return null;
                float signed = act.behind ? -d : d;
                var en = HaSpawn(signed);
                yield return new WaitForSeconds(0.08f);
                if (en == null) continue;
                int hp0 = HaHp(en);
                float dmgT0 = pc.LastDamageTime;
                if (act.air) { yield return HaFlick(PlayerController.FlickDirection.Up); yield return new WaitForSeconds(0.28f); }
                float t0 = Time.time; Vector3 p0 = pc.transform.position;
                yield return HaFlick(act.flick);
                float first = -1f, last = -1f;
                var seenProj = new HashSet<Collider2D>();
                while (Time.time - t0 < 1.1f)
                {
                    HaActiveAttackCols();
                    if (haCols.Count > 0)
                    {
                        if (first < 0f) first = Time.time - t0;
                        last = Time.time - t0;
                        if (d > 3.9f) // 敵が届かない所の試行で、判定の形を集める
                        {
                            float fx = HaFacing();
                            foreach (var c in haCols)
                            {
                                var b = c.bounds;
                                bool proj = c.GetComponent<KitProjectile>() != null || c.GetComponent<PlayerBullet>() != null;
                                if (proj)
                                {
                                    if (seenProj.Add(c))
                                    {
                                        projCount++;
                                        var kp = c.GetComponent<KitProjectile>();
                                        if (kp != null) { projSpeed = Mathf.Max(projSpeed, kp.velocity.magnitude); projLife = Mathf.Max(projLife, kp.lifetime); projPierce = Mathf.Max(projPierce, kp.pierce); }
                                        var rb = c.GetComponent<Rigidbody2D>(); if (rb != null) projSpeed = Mathf.Max(projSpeed, rb.linearVelocity.magnitude);
                                        projSize = Vector2.Max(projSize, (Vector2)b.size);
                                    }
                                    continue; // 飛び道具は別に記録(形の集計に入れない)
                                }
                                float a = (b.min.x - home.x) * fx, z = (b.max.x - home.x) * fx;
                                gMinF = Mathf.Min(gMinF, Mathf.Min(a, z)); gMaxF = Mathf.Max(gMaxF, Mathf.Max(a, z));
                                gMinU = Mathf.Min(gMinU, b.min.y - home.y); gMaxU = Mathf.Max(gMaxU, b.max.y - home.y);
                            }
                        }
                    }
                    yield return null;
                }
                bool hit = HaHp(en) < hp0;
                bool damaged = pc.LastDamageTime > dmgT0 && pc.LastDamageSource != null && pc.LastDamageSource.StartsWith("Enemy:");
                float lunge = (pc.transform.position.x - p0.x) * HaFacing();
                if (d > 3.9f) { gFirst = first; gLast = last; gLunge = lunge; }
                bool overlapAtStart = d < contactDist;
                if (hit) maxHit = d;
                if (hit && !overlapAtStart && minSafeHit < 0f) minSafeHit = d;
                if (!overlapAtStart && hit) { safeTrials++; if (damaged) damagedTrials++; }
                reach.Append($"{ch}\t{act.name}\t{signed:F1}\t{contactDist:F2}\t{(hit ? 1 : 0)}\t{(damaged && !overlapAtStart ? 1 : 0)}\t{first:F2}\t{last:F2}\t{lunge:F2}\n");
                if (en != null) Destroy(en.gameObject);
                yield return null;
            }
            geom.Append($"{ch}\t{act.name}\t{gMinF:F2}\t{gMaxF:F2}\t{gMinU:F2}\t{gMaxU:F2}\t{gFirst:F2}\t{gLast:F2}\t{gLunge:F2}\t{projCount}\t{projSpeed:F1}\t{projLife:F2}\t{projSize.x:F2}x{projSize.y:F2}\t{projPierce}\t{playerHalfW:F2}\t{playerTop:F2}\n");
            if (act.name == "forward") haForward[ch] = (maxHit, Mathf.Max(0f, gFirst));
            summary.Add($"{act.name}: hit≤{maxHit:F1}m (body contact {contactDist:F2}m) contact-damage {damagedTrials}/{safeTrials}");
        }
        L($"[hitaudit] R {ch,-13} " + string.Join(" | ", summary));
        pc.autoRunEnabled = true;
        yield return HaEnd();
    }

    // ---------------------------------------------------------------- A
    IEnumerator HaAerial(string ch, float ts, StringBuilder aerial)
    {
        yield return HaBegin(ch);
        pc.autoRunEnabled = false;
        TimeControl.SetDebugTimeScale(1f);
        yield return new WaitForSeconds(0.3f);
        var en = HaSpawn(1.0f);
        yield return new WaitForSeconds(0.1f);
        if (en == null) { L($"[hitaudit] A {ch}: test enemy could not be placed - skipped"); pc.autoRunEnabled = true; yield return HaEnd(); yield break; }
        int hp = HaHp(en), airHits = 0, groundHits = 0; bool launched = false, slam = false; float maxSep = 0f;
        var log = new StringBuilder();
        var steps = new (PlayerController.FlickDirection f, float wait, string label)[]
        {
            (PlayerController.FlickDirection.Up, 0.22f, "up"), (PlayerController.FlickDirection.Up, 0.18f, "jump"),
            (PlayerController.FlickDirection.Forward, 0.26f, "air1"), (PlayerController.FlickDirection.Forward, 0.26f, "air2"),
            (PlayerController.FlickDirection.Down, 0.9f, "down"),
        };
        foreach (var s in steps)
        {
            if (en == null) break;
            yield return HaFlick(s.f);
            float t = 0f;
            while (t < s.wait && en != null)
            {
                int h = HaHp(en);
                bool enAir = (bool)HaEnemyLaunched.GetValue(en);
                if (enAir) launched = true;
                if (h < hp)
                {
                    if (!pc.IsGrounded && enAir) airHits++; else groundHits++;
                    if (s.label == "down" && enAir) slam = true;
                    log.Append($"{s.label}:hit({(enAir ? "air" : "gnd")}) ");
                    hp = h;
                }
                if (!pc.IsGrounded && enAir) maxSep = Mathf.Max(maxSep, Vector2.Distance(pc.transform.position, en.transform.position));
                yield return null; t += Time.deltaTime;
            }
        }
        aerial.Append($"{ch}\t{launched}\t{airHits}\t{slam}\t{maxSep:F2}\t{log}\n");
        L($"[hitaudit] A {ch,-13} launched={launched} airHits={airHits} groundHits={groundHits} slam={slam} maxSeparation={maxSep:F1}m  {log}");
        if (en != null) Destroy(en.gameObject);
        pc.autoRunEnabled = true;
        yield return HaEnd();
    }

    // ---------------------------------------------------------------- S
    IEnumerator HaSpeed(string ch, StringBuilder speed)
    {
        foreach (float kmh in new[] { 50f, 100f, 125f, 150f })
        {
            // High speed test: wasteland (no cave ceiling spikes), no pits ahead (the CONTINUE safe stretch), no obstacles.
            // Earlier runs fell into pits / hit obstacles with the auto jump assist off, which cancelled the attack and looked like a miss.
            yield return HaBegin(ch, "wasteland_road");
            {
                var tmz = TerrainManager.Instance;
                float lx0 = FloatingOrigin.ToLogical(pc.transform.position.x);
                tmz.SetResumeFlatZone(lx0 - 5f, lx0 + 4000f);
                var os = FindFirstObjectByType<ObstacleSpawner>(); if (os != null) os.enabled = false;
                foreach (var ob in FindObjectsByType<ObstacleController>(FindObjectsSortMode.None)) Destroy(ob.gameObject);
            }
            stopKeepAlive = true; // count contact damage (HP is restored below)
            float baseKmh = Mathf.Max(1f, pc.CurrentRunKmh / Mathf.Max(0.01f, PlayerController.DebugRunOnlyScale));
            PlayerController.DebugRunOnlyScale = kmh / baseKmh;
            yield return new WaitForSeconds(0.4f);
            int enemies = 0, hits = 0, firstHits = 0, passed = 0, damaged = 0, projBehind = 0; string missWhy = "";
            for (int i = 0; i < 6; i++)
            {
                { float wi = 0f; while ((pc.IsAttacking || pc.IsReacting) && wi < 3f) { yield return null; wi += Time.deltaTime; } }
                var en = HaSpawn(26f);
                if (en == null) break;
                HaEnemyHp.SetValue(en, 1); // 1発で倒れる(後ろに残らない)
                enemies++;
                float dmgT0 = pc.LastDamageTime;
                bool hit = false;
                float t = 0f;
                // one press per enemy, timed like a player: when the enemy is within (static reach + distance covered during the startup)
                (float reach, float startup) fr = haForward.TryGetValue(ch, out var r0) && r0.reach > 0f ? r0 : (2.5f, 0.1f);
                float trigger = Mathf.Max(0.8f, fr.reach * 0.85f) + pc.CurrentAutoRunSpeed * (fr.startup + 0.03f);
                bool pressed = false; int pressFrames = 0; float pressDx = -99f, enemyLift = -99f; bool attackSeen = false, overlapSeen = false, secondPress = false; string trace = "";
                while (t < 3f)
                {
                    if (en == null || !en.gameObject.activeInHierarchy || en.IsDyingOrReplica) { hit = true; break; }
                    float dx = (en.transform.position.x - pc.transform.position.x) * HaFacing();
                    if (dx < -2.5f) break; // passed
                    if (pressed)
                    {
                        HaActiveAttackCols();
                        if (haCols.Count > 0) attackSeen = true;
                        var ecol = HaBodyOf(en);
                        float px = pc.transform.position.x, fx = HaFacing();
                        float hbMin = 99f, hbMax = -99f;
                        foreach (var c in haCols)
                        {
                            if (ecol != null && c.bounds.Intersects(ecol.bounds)) overlapSeen = true;
                            hbMin = Mathf.Min(hbMin, (c.bounds.min.x - px) * fx); hbMax = Mathf.Max(hbMax, (c.bounds.max.x - px) * fx);
                        }
                        if (trace.Length < 900)
                            trace += ecol != null ? $"{dx:F1}|e{(ecol.bounds.min.x - px) * fx:F1}..{(ecol.bounds.max.x - px) * fx:F1}|h{(haCols.Count > 0 ? $"{hbMin:F1}..{hbMax:F1}" : "-")} " : $"{dx:F1}|noBody ";
                    }
                    if (pressed && pressFrames <= 0 && !secondPress && !pc.IsAttacking && dx > 0.8f) { secondPress = true; pressFrames = 2; } // a player presses again if the first one missed
                    if (!pressed && dx < trigger && !pc.IsAttacking)
                    {
                        pressed = true; pressFrames = 2; pressDx = dx;
                        float? g = TerrainManager.Instance.GetHeightAt(en.transform.position.x);
                        var ec = en.GetComponentInChildren<Collider2D>();
                        enemyLift = g.HasValue && ec != null ? ec.bounds.min.y - g.Value : -99f;
                    }
                    pc.debugInjectFlick = pressFrames-- > 0 ? PlayerController.FlickDirection.Forward : (PlayerController.FlickDirection?)null;
                    // 飛び道具がプレイヤーより後ろに出ていないか
                    foreach (var kp in FindObjectsByType<KitProjectile>(FindObjectsSortMode.None))
                        if ((kp.transform.position.x - pc.transform.position.x) * HaFacing() < -1.0f && kp.velocity.x * HaFacing() > 0f) { projBehind++; break; }
                    yield return null; t += Time.deltaTime;
                }
                pc.debugInjectFlick = null;
                if (hit && !secondPress) firstHits++;
                if (hit) hits++; else { passed++; missWhy += $"[miss: pressDx {pressDx:F1} trigger {trigger:F1} enemyLift {enemyLift:F2} attackOut {attackSeen} overlap {overlapSeen} trace {trace}] "; }
                if (pc.LastDamageTime > dmgT0 && pc.LastDamageSource != null && pc.LastDamageSource.StartsWith("Enemy:")) damaged++;
                typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { gm.maxLives });
                if (en != null) Destroy(en.gameObject);
                yield return new WaitForSeconds(0.3f);
            }
            speed.Append($"{ch}\t{kmh}\t{enemies}\t{hits}\t{passed}\t{damaged}\t{projBehind}\t{missWhy}\n");
            L($"[hitaudit] S {ch,-13} {kmh,3}km/h (actual {pc.CurrentRunKmh:0}) enemies {enemies} hit {hits} (first press {firstHits}) passed {passed} contact-damage {damaged} projectile-behind {projBehind} {missWhy}");
            stopKeepAlive = false; StartCoroutine(KeepAlive());
            yield return HaEnd();
        }
    }

    // ---------------------------------------------------------------- F
    IEnumerator HaSeq(string ch, StringBuilder seq)
    {
        yield return HaBegin(ch);
        pc.autoRunEnabled = false;
        yield return new WaitForSeconds(0.3f);
        // 印ごとに区別できる大きさのボーナスを付けて、敵へ与えたダメージから読み取る
        var setFirst = typeof(PlayerController).GetProperty("FirstHitBonus").GetSetMethod(true);
        var setFinal = typeof(PlayerController).GetProperty("ComboFinalStageBonus").GetSetMethod(true);
        setFirst.Invoke(pc, new object[] { 1000 });
        setFinal.Invoke(pc, new object[] { 100000 });
        var en = HaSpawn(1.4f);
        yield return new WaitForSeconds(0.1f);
        if (en == null) { L($"[hitaudit] F {ch}: test enemy could not be placed - skipped"); pc.autoRunEnabled = true; yield return HaEnd(); yield break; }
        HaEnemyHp.SetValue(en, 10000000);
        var log = new StringBuilder();
        int hp = HaHp(en);
        // 前方の攻撃を連打(連撃/連続で撃つ)→ 少し待つ → 上攻撃 → 前方1回
        var steps = new List<(PlayerController.FlickDirection f, float wait, string label)>();
        for (int i = 0; i < 6; i++) steps.Add((PlayerController.FlickDirection.Forward, 0.18f, "F" + (i + 1)));
        steps.Add((PlayerController.FlickDirection.Forward, 1.6f, "wait"));
        steps.Add((PlayerController.FlickDirection.Up, 0.6f, "Up"));
        steps.Add((PlayerController.FlickDirection.Forward, 0.8f, "F"));
        foreach (var s in steps)
        {
            if (s.label != "wait") yield return HaFlick(s.f);
            float t = 0f;
            while (t < s.wait && en != null)
            {
                int h = HaHp(en);
                if (h < hp)
                {
                    int dmg = hp - h; hp = h;
                    // the move's damage scale (0.4-3x) multiplies the bonus too: Finisher 100000 -> 40000+, First 1000 -> 400..3000
                    string tag = dmg >= 40000 ? "FIN" : dmg >= 400 ? "FIRST" : "-";
                    log.Append($"{s.label}:{tag} ");
                }
                // 敵を手前に戻す(吹き飛んで届かなくならないように)
                if (en != null && Mathf.Abs(en.transform.position.x - (pc.transform.position.x + 1.4f * HaFacing())) > 0.6f && !(bool)HaEnemyLaunched.GetValue(en))
                    en.transform.position = new Vector3(pc.transform.position.x + 1.4f * HaFacing(), en.transform.position.y, en.transform.position.z);
                yield return null; t += Time.deltaTime;
            }
        }
        setFirst.Invoke(pc, new object[] { 0 }); setFinal.Invoke(pc, new object[] { 0 });
        seq.Append($"{ch}\t{log}\n");
        L($"[hitaudit] F {ch,-13} {log}");
        if (en != null) Destroy(en.gameObject);
        pc.autoRunEnabled = true;
        yield return HaEnd();
    }
}
#endif
