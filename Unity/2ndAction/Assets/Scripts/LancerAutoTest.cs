#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// 竜騎士追加(2026-09-26) - Editor専用の自動確認。結果は LancerAutoTest.txt。
// 1) DRAGON LANCERを選んでNEW RUNでき、isLancer/専用素材が反映されているか
// 2) 前突き: 細長い判定が前方の複数の敵をまとめて貫き(3体とも被弾)、体に重なる距離(懐)には判定が無い
// 3) 後ろ攻撃: 背後の敵に当たり、前突きよりダメージ・ノックバックが弱い
// 4) 上攻撃: ジャンプしつつ斜め上の敵に当たり、打ち上げ(Launch)はしない
// 5) 下攻撃: 地上で低い位置の敵に当たる(急降下しない)
// 6) 被弾で専用Hurt絵、Start/Finish素材が揃っている
// 7) 既存キャラ(黒剣士/拳銃士)に竜騎士の処理が混ざらない
public class LancerAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("LancerTest", 0) != 1) return;
        EditorPrefs.SetInt("LancerTest", 0);
        new GameObject("LancerTest").AddComponent<LancerAutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    int failures;
    void L(string s) { log.AppendLine(s); Debug.Log("[LancerTest] " + s); }
    void Check(bool ok, string what) { if (!ok) { failures++; L("  FAIL: " + what); } }
    static FieldInfo F(System.Type t, string name) => t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly FieldInfo hpF = F(typeof(EnemyController), "hp");
    static readonly FieldInfo launchedF = F(typeof(EnemyController), "isLaunched");

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1.5f);
        var gm = GameManager.Instance;
        var pc = PlayerController.Instance;
        gm.SetSelectedCharacter("dragon_lancer");
        gm.SetSelectedStage("natural_cave"); // 荒野街道は穴で落下復帰が挟まり判定がぶれるため、平坦な洞窟で確認する
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float t0 = Time.time;
        while (!(gm.HasStarted && !gm.CountdownActive) && Time.time - t0 < 10f) yield return null;
        gm.DebugSetInvincible(true);

        bool anyException = false;
        Application.LogCallback handler = (cond, trace, type) =>
        {
            if (type == LogType.Exception || type == LogType.Error) { anyException = true; L("[EXC] " + cond); }
        };
        Application.logMessageReceived += handler;

        foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var s in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) s.enabled = false;
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        if (TerrainManager.Instance != null) TerrainManager.Instance.enemySpawnChance = 0f;
        // 自然洞窟が一本道化(2026-09-27のEncounter改修)で穴を含むようになったため、確認中の落下復帰で技が中断され
        // 結果がぶれる。テスト中にこれから生成する地形には穴を作らない(テストの環境だけの設定、ゲーム本体は変えない)。
        if (TerrainManager.Instance != null) { TerrainManager.Instance.pitChanceBase = 0f; TerrainManager.Instance.pitChanceRampPer1000m = 0f; TerrainManager.Instance.pitChanceMax = 0f; }
        yield return new WaitForSeconds(0.3f);
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
        yield return null;

        var def = CharacterDatabase.FindById("dragon_lancer");
        var anim = pc.GetComponentInChildren<PlayerAnimator>();
        L($"stage={gm.ActiveRunStageId} character={gm.SelectedCharacterId} isLancer={pc.IsLancer} attackPower={pc.AttackPower} knockback={pc.KnockbackPowerMultiplier:F2}");
        Check(gm.SelectedCharacterId == "dragon_lancer" && pc.IsLancer, "lancer selected");
        int n(Sprite[] a) => a == null ? 0 : a.Length;
        L($"[Art] run={n(def.runFrames)} thrust={n(def.attackFrames)} back={n(def.lanceBackFrames)} down={n(def.lanceDownFrames)} up={n(def.upShotFrames)} jump={n(def.jumpFrames)} land={n(def.landFrames)} hurt={n(def.hurtFrames)} death={n(def.deathFrames)} start={n(def.startFrames)} finish={n(def.finishShortFrames)}/{n(def.finishMediumFrames)}/{n(def.finishLongFrames)}/{n(def.finishExtremeFrames)} portrait={(def.portrait != null)} mainVisual={(def.mainVisual != null)}");
        Check(n(def.runFrames) >= 4 && n(def.attackFrames) == 2 && n(def.lanceBackFrames) > 0 && n(def.lanceDownFrames) > 0 && n(def.upShotFrames) > 0, "attack/run art");
        Check(n(def.hurtFrames) > 0 && n(def.deathFrames) > 0 && n(def.startFrames) == 4 && n(def.finishExtremeFrames) == 2, "reaction/start/finish art");

        StartCoroutine(AutoPickLevelUp());
        // 距離EXPのレベルアップで攻撃速度アップ等のカードが自動で選ばれると、各技の時間が変わって計測がぶれるので止める
        typeof(GameManager).GetField("expGainMultiplier", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(gm, 0f);
        yield return TestForwardPierce(pc, anim);
        yield return TestBackward(pc, anim);
        yield return TestUp(pc);
        yield return TestDown(pc, anim);
        yield return TestHurt(pc, anim, gm);
        yield return TestCombo(pc);
        yield return TestDive(pc, anim);
        yield return TestUpThenHorizontal(pc, gm);
        yield return TestRegression(pc, def);
        yield return TestDeath(pc, anim, gm);

        Application.logMessageReceived -= handler;
        L("");
        L(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        L(anyException ? "SOME EXCEPTIONS LOGGED (see [EXC] lines above)" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../LancerAutoTest.txt"), log.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(anyException || failures > 0 ? 1 : 0); else EditorApplication.isPlaying = false;
    }

    EnemyController Spawn(PlayerController pc, float dx, float dy)
    {
        EnemyDefinition d = EnemyDatabase.FindById("goblin");
        Vector3 p = pc.transform.position;
        float x = p.x + dx;
        float? gy = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(x) : null;
        float y = (gy ?? p.y) + dy;
        var go = GroundFactory.CreateEnemy(null, d.sprite, new Vector2(x, y), d.tint, maxHp: 999, behaviorKind: EnemyBehaviorKind.None);
        return go.GetComponent<EnemyController>();
    }

    int Hp(EnemyController e) => e == null ? -9999 : (int)hpF.GetValue(e);
    float hitboxFeetY;

    // 走行距離のEXPでレベルアップしてもカード選択で止まらないよう自動で選ぶ(時間が止まると判定がずれる)。
    IEnumerator AutoPickLevelUp()
    {
        while (true)
        {
            var gm = GameManager.Instance;
            if (gm != null && gm.IsRewardSequenceWaitingForSelection)
            {
                var seq = FindFirstObjectByType<RewardCardSequence>();
                if (seq != null) { seq.OnCardClicked(0); yield return new WaitForSecondsRealtime(0.15f); seq.OnCardClicked(0); }
                yield return new WaitForSecondsRealtime(0.3f);
                continue;
            }
            yield return null;
        }
    }

    IEnumerator Flick(PlayerController pc, PlayerController.FlickDirection dir)
    {
        pc.debugInjectFlick = dir;
        float t = 0f;
        // HitStop(timeScale=0)中はPlayerControllerが入力を読まないので、読まれるまで保持する。
        while (t < 1f) { yield return null; t += Time.unscaledDeltaTime; if (Time.timeScale > 0f) break; }
        yield return null;
        pc.debugInjectFlick = null;
    }

    IEnumerator WaitIdle(PlayerController pc)
    {
        float t = 0f;
        while ((pc.IsAttacking || pc.LanceMove != PlayerController.LanceMoveKind.None || !pc.IsGrounded) && t < 3f) { yield return null; t += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.6f);
    }

    void Cleanup(params EnemyController[] es) { foreach (var e in es) if (e != null) Destroy(e.gameObject); }

    IEnumerator TestForwardPierce(PlayerController pc, PlayerAnimator anim)
    {
        yield return WaitIdle(pc);
        // 前方3体(突進ぶん少し先に置く) - 1回の突きで全員に当たれば「貫通」。
        var a = Spawn(pc, 1.4f, 0f); var b = Spawn(pc, 2.2f, 0f); var c = Spawn(pc, 3.0f, 0f);
        yield return null;
        yield return Flick(pc, PlayerController.FlickDirection.Forward);
        bool sawWindup = pc.LanceMove == PlayerController.LanceMoveKind.Forward && pc.LanceFrameIndex == 0;
        float t = 0f; Bounds hb = default; bool sawThrustFrame = false; float relMin = 0f, relMax = 0f;
        while (t < 1f)
        {
            if (pc.LanceHitbox != null && pc.LanceHitbox.enabled) { hb = pc.LanceHitbox.bounds; float cx = pc.LanceHitbox.transform.localPosition.x; relMin = cx - pc.LanceHitbox.transform.localScale.x * 0.5f; relMax = cx + pc.LanceHitbox.transform.localScale.x * 0.5f; }
            if (pc.LanceFrameIndex == 1 && anim != null && anim.CurrentState == PlayerAnimator.State.Attack) sawThrustFrame = true;
            yield return null; t += Time.unscaledDeltaTime;
        }
        float px = pc.transform.position.x;
        L($"[Forward] windup={sawWindup} thrustPose={sawThrustFrame} hitbox local x={relMin:F2}..{relMax:F2} h={hb.size.y:F2}");
        L($"[Forward] hp a={Hp(a)} b={Hp(b)} c={Hp(c)} (expect all < 999 = pierced)");
        int dmgFwd = 999 - Hp(a);
        Check(sawWindup && sawThrustFrame, "forward windup→thrust pose");
        Check(Hp(a) < 999 && Hp(b) < 999 && Hp(c) < 999 && Hp(a) > 0, "forward pierces 3 enemies");
        Check(hb.size.x > 2f && hb.size.y < 0.6f, "long thin hitbox");
        forwardDamage = dmgFwd;
        yield return new WaitForSecondsRealtime(0.4f);
        Cleanup(a, b, c);

        // 懐: 判定の開始位置(体の少し前)より内側には判定が無いこと。
        yield return WaitIdle(pc);
        yield return Flick(pc, PlayerController.FlickDirection.Forward);
        t = 0f; float minRel = 99f;
        while (t < 0.6f)
        {
            // 物理のboundsは突進中に1フレーム遅れるので、ローカル座標(体の中心からの距離)で測る。
            if (pc.LanceHitbox != null && pc.LanceHitbox.enabled) minRel = Mathf.Min(minRel, pc.LanceHitbox.transform.localPosition.x - pc.LanceHitbox.transform.localScale.x * 0.5f);
            yield return null; t += Time.unscaledDeltaTime;
        }
        L($"[Close] hitbox starts {minRel:F2} ahead of body (expect >= 0.3: 懐は空く)");
        Check(minRel >= 0.3f && minRel < 90f, "close range gap");
    }
    int forwardDamage;

    IEnumerator TestBackward(PlayerController pc, PlayerAnimator anim)
    {
        yield return WaitIdle(pc);
        // 構えの間も走り続けるので、その分だけ体の近くに置く(石突きの届く距離で当たるか)。
        var e = Spawn(pc, -0.2f, 0f);
        yield return Flick(pc, PlayerController.FlickDirection.Backward);
        bool backPose = false; float t = 0f; float kb = 0f; float scaleX = pc.transform.localScale.x;
        while (t < 0.8f)
        {
            if (pc.LanceMove == PlayerController.LanceMoveKind.Backward) { kb = Mathf.Max(kb, pc.KnockbackPowerMultiplier); if (anim != null && anim.VisualRenderer.sprite != null && anim.VisualRenderer.sprite.name.StartsWith("back")) backPose = true; }
            yield return null; t += Time.unscaledDeltaTime;
        }
        int dmg = 999 - Hp(e);
        L($"[Backward] hp={Hp(e)} dmg={dmg} forwardDmg={forwardDamage} knockbackMul={kb:F2} backPose={backPose} facingRight={scaleX > 0}");
        Check(Hp(e) < 999, "backward hits behind");
        Check(dmg < forwardDamage, "backward weaker than forward");
        Check(kb > 0f && kb < 1f, "backward knockback weaker");
        Check(backPose, "backward pose shown");
        yield return new WaitForSecondsRealtime(0.4f);
        Cleanup(e);
    }

    IEnumerator TestUp(PlayerController pc)
    {
        yield return WaitIdle(pc);
        var e = Spawn(pc, 1.6f, 1.5f);
        yield return null;
        float y0 = pc.transform.position.y;
        yield return Flick(pc, PlayerController.FlickDirection.Up);
        float t = 0f; float maxY = y0; bool launched = false; bool upPose = false;
        while (t < 0.8f)
        {
            maxY = Mathf.Max(maxY, pc.transform.position.y);
            if (e != null && (bool)launchedF.GetValue(e)) launched = true;
            if (pc.IsRangedUpShooting) upPose = true;
            yield return null; t += Time.unscaledDeltaTime;
        }
        L($"[Up] rose={maxY - y0:F2} hp={Hp(e)} launched={launched} upPose={upPose}");
        Check(maxY - y0 > 0.3f, "up jumps");
        Check(Hp(e) < 999, "up thrust hits diagonal-up enemy");
        Check(!launched, "up thrust does not launch");
        yield return new WaitForSecondsRealtime(0.3f);
        Cleanup(e);
    }

    IEnumerator TestDown(PlayerController pc, PlayerAnimator anim)
    {
        yield return WaitIdle(pc);
        var e = Spawn(pc, 1.3f, 0f);
        yield return null;
        yield return Flick(pc, PlayerController.FlickDirection.Down);
        float t = 0f; bool downMove = false; bool dove = false; Bounds hb = default; bool downPose = false;
        while (t < 0.7f)
        {
            if (pc.LanceMove == PlayerController.LanceMoveKind.Down) downMove = true;
            if (pc.IsDiveAttacking) dove = true;
            if (pc.LanceHitbox != null && pc.LanceHitbox.enabled) { Physics2D.SyncTransforms(); hb = pc.LanceHitbox.bounds; hitboxFeetY = pc.transform.position.y; }
            if (anim != null && anim.VisualRenderer.sprite != null && anim.VisualRenderer.sprite.name.StartsWith("down")) downPose = true;
            yield return null; t += Time.unscaledDeltaTime;
        }
        float gy = hitboxFeetY;
        L($"[Down] move={downMove} dove={dove} downPose={downPose} hitbox y={hb.min.y - gy:F2}..{hb.max.y - gy:F2} hp={Hp(e)}");
        Check(downMove && !dove, "ground down thrust (no dive)");
        Check(hb.max.y - gy < 0.7f, "down hitbox is low");
        Check(Hp(e) < 999, "down thrust hits low enemy");
        Check(downPose, "down pose shown");
        yield return new WaitForSecondsRealtime(0.3f);
        Cleanup(e);
    }

    // ===== 2026-09-26 第2弾: 3段突き / 急降下突き / 上攻撃→横攻撃の停止バグ ===== //

    IEnumerator TestCombo(PlayerController pc)
    {
        yield return WaitIdle(pc);
        var enemies = new List<EnemyController>();
        for (int i = 0; i < 6; i++) enemies.Add(Spawn(pc, 1.6f + i * 0.9f, 0f));
        yield return null;
        var stageSeen = new List<int>(); var reachByStage = new float[4]; var dmgByStage = new float[4]; var kbByStage = new float[4];
        float stage3End = -1f, idleAfter3 = -1f, t = 0f;
        int lastStage = 0;
        // 突きの最中に次の入力を送り続ける(連打)
        float nextFlick = 0f;
        while (t < 2.6f)
        {
            if (t >= nextFlick && stageSeen.Count < 3) { pc.debugInjectFlick = PlayerController.FlickDirection.Forward; nextFlick = t + 0.12f; }
            else pc.debugInjectFlick = null;
            int st = pc.LanceComboStage;
            if (st > 0 && st != lastStage) { stageSeen.Add(st); lastStage = st; }
            if (st > 0 && pc.LanceHitbox != null && pc.LanceHitbox.enabled)
            {
                reachByStage[st] = Mathf.Max(reachByStage[st], pc.LanceHitbox.transform.localScale.x);
                dmgByStage[st] = Mathf.Max(dmgByStage[st], pc.EffectiveAttackPower);
                kbByStage[st] = Mathf.Max(kbByStage[st], pc.KnockbackPowerMultiplier);
            }
            if (st == 3 && pc.LanceHitbox != null && !pc.LanceHitbox.enabled && pc.LanceFrameIndex == 1 && stage3End < 0f) stage3End = t;
            if (stage3End >= 0f && idleAfter3 < 0f && !pc.IsAttacking) idleAfter3 = t;
            yield return null; t += Time.unscaledDeltaTime;
        }
        pc.debugInjectFlick = null;
        L($"[Combo] stages={string.Join(">", stageSeen)} reach1..3={reachByStage[1]:F2}/{reachByStage[2]:F2}/{reachByStage[3]:F2} dmg={dmgByStage[1]}/{dmgByStage[2]}/{dmgByStage[3]} kb={kbByStage[1]:F2}/{kbByStage[2]:F2}/{kbByStage[3]:F2} recovery3={(idleAfter3 - stage3End):F2}s");
        Check(stageSeen.Count >= 3 && stageSeen[0] == 1 && stageSeen[1] == 2 && stageSeen[2] == 3, "combo 1>2>3");
        Check(reachByStage[1] < reachByStage[2] && reachByStage[2] < reachByStage[3], "reach grows per stage");
        Check(dmgByStage[3] >= dmgByStage[2] && dmgByStage[2] >= dmgByStage[1] && dmgByStage[3] > dmgByStage[1], "damage grows per stage");
        Check(kbByStage[1] < kbByStage[2] && kbByStage[2] < kbByStage[3], "knockback grows per stage");
        Check(reachByStage[3] > 2.5f, "stage3 long thin reach");
        Check(idleAfter3 - stage3End > 0.35f, "stage3 has bigger recovery");
        // 入力が途切れたら1段目へ戻る
        yield return WaitIdle(pc);
        yield return Flick(pc, PlayerController.FlickDirection.Forward);
        yield return null;
        int again = pc.LanceComboStage;
        L($"[Combo] after pause next stage={again} (expect 1)");
        Check(again == 1, "combo resets after pause");
        yield return WaitIdle(pc);
        Cleanup(enemies.ToArray());
    }

    // 前方に穴が無い区間まで待つ(落下復帰が挟まると着地の確認ができないため)
    IEnumerator WaitNoPitAhead(PlayerController pc, float distance)
    {
        var tm = TerrainManager.Instance;
        float t = 0f;
        while (tm != null && t < 20f)
        {
            bool pit = false;
            for (float d = -1f; d <= distance; d += 0.5f) if (tm.IsNearPit(pc.transform.position.x + d, 0.3f)) { pit = true; break; }
            if (!pit && pc.IsGrounded) break;
            yield return null; t += Time.unscaledDeltaTime;
        }
    }

    IEnumerator TestDive(PlayerController pc, PlayerAnimator anim)
    {
        yield return WaitIdle(pc);
        yield return WaitNoPitAhead(pc, 6f);
        // 上昇のピーク付近まで待つ(洞窟の天井に当たって早く着地した場合は、空中にいる状態になるまでやり直す)
        float t = 0f;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            yield return Flick(pc, PlayerController.FlickDirection.Up);
            t = 0f;
            while (t < 0.3f) { yield return null; t += Time.unscaledDeltaTime; }
            if (!pc.IsGrounded && pc.transform.position.y > 0.5f + (TerrainManager.Instance != null ? (TerrainManager.Instance.GetHeightAt(pc.transform.position.x) ?? 0f) : 0f)) break;
            yield return WaitIdle(pc);
        }
        float airY = pc.transform.position.y;
        // 着地点の周り(前後)に敵を置く
        var near = new[] { Spawn(pc, 0.9f, 0f), Spawn(pc, -0.6f, 0f), Spawn(pc, 1.6f, 0f) };
        yield return null;
        yield return Flick(pc, PlayerController.FlickDirection.Down);
        float x0 = pc.transform.position.x, y0 = pc.transform.position.y;
        bool dive = false, landed = false, sawHitStop = false, sawImpactPose = false; float fallX = 0f, fallY = 0f, maxVy = 0f; t = 0f;
        float prevY = y0; float landT = -1f, runT = -1f;
        while (t < 3f)
        {
            if (pc.LanceMove == PlayerController.LanceMoveKind.Dive) dive = true;
            if (pc.IsLanceDiving && pc.LanceFrameIndex == 1) { fallX = pc.transform.position.x - x0; fallY = y0 - pc.transform.position.y; if (Time.deltaTime > 0f) maxVy = Mathf.Max(maxVy, (prevY - pc.transform.position.y) / Time.deltaTime); }
            if (dive && pc.LanceFrameIndex == 2) { landed = true; sawImpactPose = true; if (landT < 0f) landT = t; }
            if (HitStop.IsActive && landed) sawHitStop = true;
            if (landed && runT < 0f && !pc.IsAttacking && pc.IsGrounded && pc.LanceMove == PlayerController.LanceMoveKind.None) runT = t;
            prevY = pc.transform.position.y;
            yield return null; t += Time.unscaledDeltaTime;
        }
        int hit = 0; foreach (var e in near) if (Hp(e) < 999) hit++;
        L($"[Dive] dive={dive} fall dx={fallX:F2} dy={fallY:F2} (vertical ratio {(fallY > 0.01f ? fallX / fallY : 99):F2}) maxFallSpeed={maxVy:F1} landed={landed} impactPose={sawImpactPose} hitStop={sawHitStop} enemiesHit={hit}/3 backToRun={(runT - landT):F2}s airY={airY:F2}");
        Check(dive && landed, "air down = dive + landing");
        Check(fallY > 0.3f && Mathf.Abs(fallX) < fallY * 0.5f, "dive falls almost straight down");
        Check(maxVy > 12f, "dive is fast");
        Check(sawHitStop, "landing hitstop");
        Check(hit >= 2, "impact damages nearby enemies");
        Check(runT > 0f && runT - landT < 1.2f, "returns to run after dive");
        yield return WaitIdle(pc);
        Cleanup(near);
    }

    // 攻撃の後、走れる/攻撃できる状態へ戻っているか(永久停止していないか)。
    IEnumerator CheckRecovered(PlayerController pc, string label)
    {
        float t = 0f;
        var trace = new StringBuilder();
        string last = "";
        while ((pc.IsAttacking || pc.LanceMove != PlayerController.LanceMoveKind.None || !pc.IsGrounded || pc.IsReacting) && t < 3f)
        {
            string now = $"{pc.LanceMove}/{pc.LanceComboStage}/{pc.LanceFrameIndex}/{(pc.IsGrounded ? "G" : "A")}/ts{Time.timeScale:F0}";
            if (now != last) { trace.Append($" {t:F2}:{now}"); last = now; }
            yield return null; t += Time.deltaTime; // レベルアップ等の一時停止中は数えない
        }
        bool idle = !pc.IsAttacking && pc.LanceMove == PlayerController.LanceMoveKind.None && pc.IsGrounded;
        float waited = t;
        if (!idle || t > 1.5f) L($"  [Recover] {label} trace:{trace}");
        if (!idle) L($"  [Recover] {label} still busy: attacking={pc.IsAttacking} lance={pc.LanceMove} grounded={pc.IsGrounded} reacting={pc.IsReacting} y={pc.transform.position.y:F2}");
        yield return new WaitForSecondsRealtime(0.55f);
        float x0 = pc.transform.position.x;
        yield return new WaitForSeconds(0.4f);
        float moved = pc.transform.position.x - x0;
        yield return Flick(pc, PlayerController.FlickDirection.Forward);
        bool canAttack = false; t = 0f;
        while (t < 0.4f) { if (pc.IsAttacking) canAttack = true; yield return null; t += Time.unscaledDeltaTime; }
        L($"[Recover] {label}: idle={idle} ({waited:F2}s) runMoved={moved:F2} canAttackAgain={canAttack}");
        Check(idle && moved > 0.5f && canAttack, label + " recovers (run + attack)");
        yield return WaitIdle(pc);
    }

    IEnumerator Sequence(PlayerController pc, params (PlayerController.FlickDirection dir, float wait)[] steps)
    {
        foreach (var s in steps)
        {
            yield return Flick(pc, s.dir);
            float t = 0f; while (t < s.wait) { yield return null; t += Time.unscaledDeltaTime; }
        }
    }

    IEnumerator TestUpThenHorizontal(PlayerController pc, GameManager gm)
    {
        var U = PlayerController.FlickDirection.Up; var F = PlayerController.FlickDirection.Forward;
        var B = PlayerController.FlickDirection.Backward; var D = PlayerController.FlickDirection.Down;
        yield return WaitIdle(pc);
        yield return WaitNoPitAhead(pc, 8f);
        yield return Sequence(pc, (U, 0.08f), (F, 0f));
        yield return CheckRecovered(pc, "1 Up>Forward>Land>Run");
        yield return Sequence(pc, (U, 0.08f), (B, 0f));
        yield return CheckRecovered(pc, "2 Up>Back>Land>Run");
        yield return Sequence(pc, (U, 0.05f), (F, 0.1f), (F, 0.1f), (F, 0f));
        yield return CheckRecovered(pc, "3 Up>Forward>Forward>Forward");
        yield return Sequence(pc, (U, 0.05f), (F, 0.05f), (U, 0.05f), (F, 0f));
        yield return CheckRecovered(pc, "3b Up>F>Up(2nd jump)>F");
        yield return Sequence(pc, (F, 0.2f), (U, 0f));
        yield return CheckRecovered(pc, "3c Forward>Up during recovery (old freeze)");
        yield return Sequence(pc, (D, 0.08f), (U, 0f));
        yield return CheckRecovered(pc, "3d GroundDown>Up");
        yield return Sequence(pc, (U, 0.12f), (D, 0f));
        yield return CheckRecovered(pc, "4 Up>Dive>Land");
        // 5: 攻撃中の被弾から復帰
        gm.DebugSetInvincible(false);
        yield return Sequence(pc, (U, 0.05f), (F, 0.05f));
        pc.TakeDamage(source: "LancerTest");
        gm.DebugSetInvincible(true);
        yield return CheckRecovered(pc, "5 Hurt during Up/Forward");
        gm.DebugSetInvincible(false);
        yield return Sequence(pc, (U, 0.1f), (D, 0.1f));
        pc.TakeDamage(source: "LancerTest");
        gm.DebugSetInvincible(true);
        yield return CheckRecovered(pc, "5b Hurt during Dive");
    }

    IEnumerator TestHurt(PlayerController pc, PlayerAnimator anim, GameManager gm)
    {
        yield return WaitIdle(pc);
        gm.DebugSetInvincible(false);
        // レベルアップの一時停止中などは被弾しないので、通常時に当たるまで数回試す
        for (int attempt = 0; attempt < 5 && !pc.IsHurt; attempt++)
        {
            float w = 0f;
            while ((Time.timeScale == 0f || gm.IsRewardSequenceWaitingForSelection) && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
            pc.TakeDamage(source: "LancerTest");
            yield return null; yield return null;
            if (!pc.IsHurt) yield return new WaitForSecondsRealtime(0.8f);
        }
        bool hurt = pc.IsHurt;
        string spr = anim != null && anim.VisualRenderer.sprite != null ? anim.VisualRenderer.sprite.name : "?";
        L($"[Hurt] IsHurt={hurt} state={(anim != null ? anim.CurrentState.ToString() : "?")} sprite={spr}");
        Check(hurt && spr.StartsWith("hurt"), "hurt pose");
        gm.DebugSetInvincible(true);
        yield return new WaitForSecondsRealtime(1.5f);
    }

    // 最後の被弾で力尽きる: 消えて爆散する代わりに専用の死亡ポーズを見せる。
    IEnumerator TestDeath(PlayerController pc, PlayerAnimator anim, GameManager gm)
    {
        yield return WaitIdle(pc);
        gm.DebugSetInvincible(false); // これはLivesを999に戻すので、先に呼んでから残り1にする
        gm.DebugSetLives(1);
        // 直前の被弾テストの無敵時間が残っていると当たらないので、倒れるまで数回試す。
        float t = 0f;
        while (t < 6f && !gm.IsGameOver)
        {
            if (pc.ShieldCharges > 0) { pc.TryConsumeShield(); }
            pc.TakeDamage(source: "LancerTest");
            yield return new WaitForSecondsRealtime(0.2f); t += 0.2f;
        }
        t = 0f;
        while (t < 1.5f && !pc.IsDeadPosing) { yield return null; t += Time.unscaledDeltaTime; }
        yield return null; yield return null;
        string spr = anim != null && anim.VisualRenderer.sprite != null ? anim.VisualRenderer.sprite.name : "?";
        bool visible = anim != null && anim.VisualRenderer.enabled;
        L($"[Death] gameOver={gm.IsGameOver} deadPosing={pc.IsDeadPosing} visible={visible} sprite={spr}");
        Check(pc.IsDeadPosing && visible && spr.StartsWith("death"), "death pose");
    }

    IEnumerator TestRegression(PlayerController pc, CharacterDefinition lancer)
    {
        yield return WaitIdle(pc);
        foreach (string id in new[] { "swordsman", "gunslinger" })
        {
            pc.ApplyCharacterBaseStats(CharacterDatabase.FindById(id));
            L($"[Regression] {id}: isLancer={pc.IsLancer} knockback={pc.KnockbackPowerMultiplier:F2} lanceHitboxEnabled={(pc.LanceHitbox != null && pc.LanceHitbox.enabled)}");
            Check(!pc.IsLancer, id + " is not lancer");
            if (id == "swordsman")
            {
                yield return Flick(pc, PlayerController.FlickDirection.Forward);
                bool melee = false, lance = false; float mt = 0f;
                while (mt < 0.5f) { if (pc.attackHitbox.enabled) melee = true; if (pc.LanceHitbox != null && pc.LanceHitbox.enabled) lance = true; yield return null; mt += Time.deltaTime; }
                L($"[Regression] swordsman forward: meleeHitbox={melee} lanceHitbox={lance} (expect True,False)");
                Check(melee && !lance, "swordsman uses its own hitbox");
                Check(Mathf.Approximately(pc.KnockbackPowerMultiplier, 1f), "swordsman knockback unchanged");
                yield return new WaitForSeconds(0.8f);
            }
        }
        pc.ApplyCharacterBaseStats(lancer);
    }
}

public static class LancerTestMenu
{
    [MenuItem("Tools/OneMoreMile/Lancer Test (batch)")]
    public static void RunBatch()
    {
        EditorPrefs.SetInt("LancerTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif
