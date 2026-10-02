#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// 追加3人(巫女/吸血鬼/竜人、2026-09-28) - Editor専用の自動確認。結果は NewChars2AutoTest.txt。
// 自然洞窟(穴なし)で、巫女→吸血鬼→竜人の順に:
//  - 4方向攻撃と固有要素(御札のSeal/結界、Blood Gauge/Rush、滑空/ブレス/燃える地面)が仕様どおり動く
//  - 永久回復・永久滑空にならない、結界/燃える地面は同時に1つ
//  - 被弾/復帰/キャラ切り替えで、結界・御札・ゲージ・Rush・滑空・体の大きさが残らない
//  - 既存9人に新3人の処理が混ざらない
public class NewChars2AutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("NewChars2Test", 0) != 1) return;
        EditorPrefs.SetInt("NewChars2Test", 0);
        new GameObject("NewChars2Test").AddComponent<NewChars2AutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    int failures;
    void L(string s) { log.AppendLine(s); Debug.Log("[NewChars2Test] " + s); }
    void Check(bool ok, string what) { if (!ok) { failures++; L("  FAIL: " + what); } else L("  ok: " + what); }
    static FieldInfo F(System.Type t, string name) => t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly FieldInfo hpF = F(typeof(EnemyController), "hp");
    static readonly FieldInfo launchedF = F(typeof(EnemyController), "isLaunched");
    static readonly FieldInfo attackingF = F(typeof(PlayerController), "isAttacking");
    static readonly FieldInfo ownsF = F(typeof(PlayerController), "kitOwnsAttack");
    static readonly MethodInfo respawnM = typeof(PlayerController).GetMethod("OnKitRespawn", BindingFlags.NonPublic | BindingFlags.Instance);

    GameManager gm;
    PlayerController pc;
    PlayerAnimator anim;
    Vector2 baseBodySize;

    // テストのコルーチンが例外で止まっても終了できるように(結果を書き出して失敗扱いで抜ける)
    IEnumerator Watchdog()
    {
        yield return new WaitForSecondsRealtime(420f);
        L("WATCHDOG: test did not finish in time (a coroutine probably threw) - " + failures + " failure(s) so far");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../NewChars2AutoTest.txt"), log.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(1); else EditorApplication.isPlaying = false;
    }

    IEnumerator Start()
    {
        StartCoroutine(Watchdog());
        yield return new WaitForSeconds(1.5f);
        gm = GameManager.Instance;
        pc = PlayerController.Instance;
        anim = pc.GetComponentInChildren<PlayerAnimator>();
        gm.SetSelectedCharacter("miko");
        gm.SetSelectedStage("natural_cave");
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float t0 = Time.time;
        while (!(gm.HasStarted && !gm.CountdownActive) && Time.time - t0 < 10f) yield return null;
        gm.DebugSetInvincible(true);

        bool anyException = false;
        Application.LogCallback handler = (cond, trace, type) =>
        {
            if (type == LogType.Exception || type == LogType.Error) { anyException = true; L("[EXC] " + cond + "\n" + trace); }
        };
        Application.logMessageReceived += handler;

        foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var s in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var s in FindObjectsByType<EncounterDirector>(FindObjectsSortMode.None)) s.enabled = false;
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        if (TerrainManager.Instance != null)
        {
            TerrainManager.Instance.enemySpawnChance = 0f;
            TerrainManager.Instance.pitChanceBase = 0f;
            TerrainManager.Instance.pitChanceRampPer1000m = 0f;
            TerrainManager.Instance.pitChanceMax = 0f;
        }
        typeof(GameManager).GetField("expGainMultiplier", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(gm, 0f);
        StartCoroutine(AutoPickLevelUp());
        yield return new WaitForSeconds(0.3f);
        ClearEnemies();
        {
            float wx0 = pc.transform.position.x, wt = 0f;
            while (pc.transform.position.x < wx0 + 80f && wt < 30f)
            {
                float lead = 1.1f * Mathf.Max(1f, pc.CurrentAutoRunSpeed / 5f);
                if (pc.IsGrounded && !pc.IsReacting && TerrainManager.Instance.IsNearPit(pc.transform.position.x + lead, 0.4f)) yield return Flick(PlayerController.FlickDirection.Up);
                yield return null; wt += Time.deltaTime;
            }
            yield return WaitIdle();
        }

        L($"[Select] selected={gm.SelectedCharacterId} activeRun={gm.ActiveRunCharacterId} kit={pc.Kit} lives={gm.Lives}/{gm.maxLives}");
        Check(gm.SelectedCharacterId == "miko" && pc.Kit == CharacterKit.Miko, "miko selected via NEW RUN");
        Check(CharacterDatabase.AllCharacters.Count == 12, $"12 characters in database (got {CharacterDatabase.AllCharacters.Count})");
        var order = new List<string>();
        foreach (var c in CharacterDatabase.AllCharacters) order.Add(c.characterId);
        L("   order: " + string.Join(",", order));
        Check(order.Count == 12 && order[9] == "miko" && order[10] == "vampire" && order[11] == "dragonkin", "new three are 10th/11th/12th");

        yield return Switch("swordsman");
        baseBodySize = pc.GetComponent<BoxCollider2D>().size;
        yield return Switch("miko");
        yield return TestMiko();
        yield return Switch("vampire");
        yield return TestVampire();
        yield return Switch("dragonkin");
        yield return TestDragonkin();
        yield return TestHurtAll();
        yield return TestRegression();

        Application.logMessageReceived -= handler;
        L("");
        L(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        L(anyException ? "SOME EXCEPTIONS LOGGED (see [EXC] lines above)" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../NewChars2AutoTest.txt"), log.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(anyException || failures > 0 ? 1 : 0); else EditorApplication.isPlaying = false;
    }

    // ============ 共通 ============

    IEnumerator Switch(string id)
    {
        yield return WaitIdle();
        ClearEnemies();
        var def = CharacterDatabase.FindById(id);
        pc.ApplyCharacterBaseStats(def);
        anim.ApplyCharacterAnimationSet(def);
        yield return new WaitForSeconds(0.6f);
        int n(Sprite[] a) => a == null ? 0 : a.Length;
        string poses = def.kitPoses == null ? "-" : string.Join(",", System.Array.ConvertAll(def.kitPoses, p => p.name + ":" + n(p.frames)));
        L($"\n===== {id} kit={pc.Kit} run={n(def.runFrames)} jump={n(def.jumpFrames)} land={n(def.landFrames)} hurt={n(def.hurtFrames)} death={n(def.deathFrames)} start={n(def.startFrames)} finish={n(def.finishShortFrames)}/{n(def.finishMediumFrames)}/{n(def.finishLongFrames)}/{n(def.finishExtremeFrames)} poses=[{poses}] portrait={def.portrait != null} main={def.mainVisual != null}");
    }

    void ClearEnemies()
    {
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
    }

    EnemyController Spawn(float dx, float dy, EnemyBehaviorKind kind = EnemyBehaviorKind.None)
    {
        EnemyDefinition d = EnemyDatabase.FindById("goblin");
        Vector3 p = pc.transform.position;
        float x = p.x + dx;
        float? gy = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(x) : null;
        float y = (gy ?? p.y) + dy;
        var go = GroundFactory.CreateEnemy(null, d.sprite, new Vector2(x, y), d.tint, maxHp: 999, behaviorKind: kind);
        return go.GetComponent<EnemyController>();
    }

    int Hp(EnemyController e) => e == null ? -9999 : (int)hpF.GetValue(e);
    int Lost(EnemyController e) { int h = Hp(e); return h < 0 ? 0 : 999 - h; }

    IEnumerator AutoPickLevelUp()
    {
        while (true)
        {
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

    IEnumerator Flick(PlayerController.FlickDirection dir)
    {
        pc.debugInjectFlick = dir;
        float t = 0f;
        do { yield return null; t += Time.unscaledDeltaTime; } while (Time.timeScale <= 0f && t < 1f);
        pc.debugInjectFlick = null;
    }

    bool Attacking => (bool)attackingF.GetValue(pc);

    IEnumerator WaitIdle()
    {
        float t = 0f;
        while ((Attacking || !pc.IsGrounded || pc.IsReacting) && t < 3f) { yield return null; t += Time.unscaledDeltaTime; }
        yield return new WaitForSeconds(0.1f);
    }

    IEnumerator Wait(float s) { yield return new WaitForSeconds(s); }

    IEnumerator WaitNoPit(float ahead)
    {
        float t = 0f;
        while (t < 10f)
        {
            float x0 = pc.transform.position.x - 1f;
            bool pit = false;
            for (float x = x0; x < x0 + ahead + 1f; x += 0.25f) if (!TerrainManager.Instance.GetHeightAt(x).HasValue) { pit = true; break; }
            if (!pit && SpikesNear(pc.transform.position.x + ahead * 0.5f, ahead * 0.5f + 1.5f).Count > 0) pit = true;
            if (!pit && !pc.IsReacting) yield break;
            float lead = 1.1f * Mathf.Max(1f, pc.CurrentAutoRunSpeed / 5f);
            if (pc.IsGrounded && !pc.IsReacting && TerrainManager.Instance.IsNearPit(pc.transform.position.x + lead, 0.4f))
                yield return Flick(PlayerController.FlickDirection.Up);
            yield return null; t += Time.deltaTime;
        }
    }

    List<float> SpikesNear(float px, float r)
    {
        var res = new List<float>();
        var cave = TerrainManager.Instance != null ? TerrainManager.Instance.cave : null;
        if (cave == null) return res;
        var list = typeof(CaveStage).GetField("spikes", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(cave) as System.Collections.IList;
        if (list == null) return res;
        foreach (var sp in list)
        {
            float x = (float)sp.GetType().GetField("x").GetValue(sp);
            if (Mathf.Abs(x - px) <= r) res.Add(x);
        }
        return res;
    }

    float JumpApex() => pc.jumpForce * pc.jumpForce / (2f * pc.gravity);

    // ============ 巫女 ============
    IEnumerator TestMiko()
    {
        var p = CharacterDatabase.FindById("miko").miko;
        yield return WaitIdle();
        // 前: 御札が貼り付き、時間差で浄化爆発
        yield return WaitNoPit(7f);
        var a = Spawn(3.2f, 0f);
        int det0 = KitSealMark.Detonations;
        yield return Flick(PlayerController.FlickDirection.Forward);
        bool throwPose = false; { float w = 0f; while (w < 0.25f) { throwPose |= pc.KitPoseName == "throw"; yield return null; w += Time.deltaTime; } }
        yield return Wait(0.35f);
        int marks = KitSealMark.ActiveCount; int lostHit = Lost(a);
        yield return Wait(p.markDelay + 0.2f);
        int lostAfter = Lost(a);
        L($"[Miko ofuda] throwPose={throwPose} marksAfterHit={marks} lost hit={lostHit} afterBurst={lostAfter} detonations+={KitSealMark.Detonations - det0}");
        Check(lostHit > 0 && marks == 1, "ofuda hits and sticks a seal on the enemy");
        Check(KitSealMark.Detonations - det0 == 1 && lostAfter > lostHit, "seal bursts after a delay");
        Check(KitSealMark.ActiveCount == 0, "seal is gone after bursting");
        ClearEnemies();
        // 2枚目: 貼られた敵へ当てると即爆発(時間差を待たない)
        yield return WaitIdle();
        yield return WaitNoPit(7f);
        var b = Spawn(3.4f, 0f);
        yield return Flick(PlayerController.FlickDirection.Forward);
        yield return Wait(p.ofudaCooldown + 0.05f);
        int det1 = KitSealMark.Detonations; int lb = Lost(b);
        float tFirst = Time.time;
        yield return Flick(PlayerController.FlickDirection.Forward);
        float w2 = 0f; while (KitSealMark.Detonations == det1 && w2 < 0.8f) { yield return null; w2 += Time.deltaTime; }
        yield return Wait(0.15f);
        L($"[Miko detonate] detonations+={KitSealMark.Detonations - det1} after {w2:F2}s (markDelay {p.markDelay}) lost {lb}->{Lost(b)}");
        Check(KitSealMark.Detonations - det1 == 1 && w2 < p.markDelay - 0.3f, "second ofuda on a sealed enemy detonates immediately");
        Check(Lost(b) - lb >= 2, "immediate detonation deals a big hit");
        ClearEnemies();
        yield return Wait(p.markDelay);
        // 後: 式神
        yield return WaitIdle();
        yield return WaitNoPit(7f);
        var c = Spawn(-2.6f, 0f);
        yield return Flick(PlayerController.FlickDirection.Backward);
        bool shiki = false; { float w = 0f; while (w < 0.9f) { shiki |= GameObject.Find("MikoShikigami") != null; yield return null; w += Time.deltaTime; } }
        L($"[Miko shikigami] seen={shiki} lost={Lost(c)}");
        Check(shiki && Lost(c) > 0, "shikigami flies backward and hits the enemy behind");
        ClearEnemies();
        // 上: 御札を扇状に3枚
        yield return WaitIdle();
        yield return WaitNoPit(7f);
        var d = Spawn(2.4f, 2.4f);
        int thrown0 = pc.MikoOfudaThrown;
        yield return Flick(PlayerController.FlickDirection.Up);
        yield return Wait(0.8f);
        L($"[Miko up fan] ofuda={pc.MikoOfudaThrown - thrown0} lost={Lost(d)}");
        Check(pc.MikoOfudaThrown - thrown0 == p.upAngles.Length, "up = fan of ofuda");
        Check(Lost(d) > 0, "fan hits an enemy in the air");
        ClearEnemies();
        // 下: 結界(敵を遅くし、少しずつ削り、ノックバックさせない)
        yield return WaitIdle();
        yield return WaitNoPit(8f);
        var still = Spawn(p.barrierForward, 0f);
        float stillX = still.transform.position.x;
        int slowed0 = KitZone.SlowedFrames;
        yield return Flick(PlayerController.FlickDirection.Down);
        bool placePose = false; { float w = 0f; while (w < 0.4f) { placePose |= pc.KitPoseName == "place"; yield return null; w += Time.deltaTime; } }
        var zone = KitZone.Find(KitZone.Kind.Barrier);
        // 結界の中へ走り込んでくる敵(Chaser)
        var mover = zone != null ? Spawn(zone.transform.position.x + p.barrierWidth * 0.5f + 2f - pc.transform.position.x, 0f, EnemyBehaviorKind.Chaser) : null;
        float maxShift = 0f; int lostStill0 = Lost(still);
        { float w = 0f; while (w < 1.6f) { if (still != null) maxShift = Mathf.Max(maxShift, Mathf.Abs(still.transform.position.x - stillX)); yield return null; w += Time.deltaTime; } }
        L($"[Miko barrier] placePose={placePose} zone={(zone != null)} ticks={(zone != null ? zone.TicksDone : 0)} stillLost={Lost(still)} maxKnockShift={maxShift:F3} slowedFrames+={KitZone.SlowedFrames - slowed0} moverLost={Lost(mover)}");
        Check(zone != null, "down places a barrier on the ground ahead");
        Check(zone != null && zone.TicksDone >= 3 && Lost(still) >= 2, "barrier deals damage repeatedly");
        Check(maxShift < 0.1f, "barrier ticks do not knock the enemy away");
        Check(KitZone.SlowedFrames - slowed0 > 0, "enemies moving inside the barrier are slowed");
        // 置き直すと古い方は消える(同時に1つ)
        yield return WaitIdle();
        yield return Wait(p.barrierCooldown);
        var z1 = KitZone.Find(KitZone.Kind.Barrier);
        yield return Flick(PlayerController.FlickDirection.Down);
        yield return Wait(0.4f);
        var z2 = KitZone.Find(KitZone.Kind.Barrier);
        L($"[Miko barrier x2] count={KitZone.ActiveCount(KitZone.Kind.Barrier)} replaced={(z1 != null && z2 != null && z1 != z2) || z1 == null}");
        Check(KitZone.ActiveCount(KitZone.Kind.Barrier) == 1, "only one barrier at a time");
        // 時間で消える
        yield return Wait(p.barrierDuration + 0.3f);
        Check(KitZone.ActiveCount(KitZone.Kind.Barrier) == 0, "barrier expires after its duration");
        ClearEnemies();
        // 復帰/キャラ切り替えで結界・御札が残らない
        yield return WaitIdle();
        yield return WaitNoPit(8f);
        var e = Spawn(3f, 0f);
        yield return Flick(PlayerController.FlickDirection.Forward);
        yield return Wait(0.35f);
        yield return WaitIdle();
        yield return Wait(0.2f);
        yield return Flick(PlayerController.FlickDirection.Down);
        yield return Wait(0.4f);
        int zb = KitZone.ActiveCount(KitZone.Kind.Barrier), mb = KitSealMark.ActiveCount;
        respawnM.Invoke(pc, null);
        yield return null;
        L($"[Miko respawn cleanup] before barrier={zb} seals={mb} after barrier={KitZone.ActiveCount(KitZone.Kind.Barrier)} seals={KitSealMark.ActiveCount}");
        Check(zb == 1 && KitZone.ActiveCount(KitZone.Kind.Barrier) == 0 && KitSealMark.ActiveCount == 0, "respawn clears barrier and seals");
        ClearEnemies();
    }

    // ============ 吸血鬼 ============
    IEnumerator TestVampire()
    {
        var p = CharacterDatabase.FindById("vampire").vampire;
        yield return WaitIdle();
        Check(pc.BloodGauge == 0f && !pc.BloodRush, "gauge starts empty");
        // 前: 3段コンボ(当てるとゲージが増える)
        yield return WaitNoPit(14f);
        var e = Spawn(1.2f, 0f);
        int maxStage = 0; float t = 0f, next = 0f;
        var lostBy = new int[4]; int last = 0;
        while (t < 1.6f)
        {
            if (t >= next) { pc.debugInjectFlick = PlayerController.FlickDirection.Forward; next = t + 0.1f; } else pc.debugInjectFlick = null;
            int st = pc.VampireComboStage; maxStage = Mathf.Max(maxStage, st);
            int lo = Lost(e); if (lo != last && st > 0) { lostBy[st] = Mathf.Max(lostBy[st], lo - last); last = lo; }
            yield return null; t += Time.deltaTime;
        }
        pc.debugInjectFlick = null;
        float g1 = pc.BloodGauge;
        L($"[Vampire combo] maxStage={maxStage} hitByStage 1:{lostBy[1]} 2:{lostBy[2]} 3:{lostBy[3]} gauge={g1:F0}");
        Check(maxStage == 3, "3-hit claw combo reaches the blood slash");
        Check(g1 > 0f, "hitting fills the Blood gauge");
        Check(lostBy[3] >= lostBy[1], "blood slash (3rd) is at least as strong as a claw");
        ClearEnemies();
        // 当てない時間が続くと減る
        yield return WaitIdle();
        float gBefore = pc.BloodGauge;
        yield return Wait(p.decayDelay + 1.5f);
        L($"[Vampire decay] {gBefore:F0} -> {pc.BloodGauge:F0}");
        Check(gBefore > 0f && pc.BloodGauge < gBefore, "gauge decays while not hitting");
        // Blood Rush まで殴り続ける(同時に、命中回復が上限付きであることも見る)
        gm.DebugSetInvincible(false);
        pc.TakeDamage(source: "test-lower-life");
        yield return WaitIdle();
        gm.DebugSetInvincible(true);
        int lives0 = gm.Lives, heals0 = pc.VampireHealsGiven, rush0 = pc.BloodRushCount;
        yield return WaitNoPit(40f);
        e = Spawn(1.2f, 0f);
        float rushAt = -1f; t = 0f; next = 0f;
        float runBefore = pc.VampireRunMultiplier, runRush = 0f;
        float atkSumN = 0f, atkSumR = 0f; int atkN = 0, atkR = 0, prevStage = 0; float stageStart = 0f;
        while (t < 14f)
        {
            if (t >= next) { pc.debugInjectFlick = PlayerController.FlickDirection.Forward; next = t + 0.08f; } else pc.debugInjectFlick = null;
            // 3段目の吹き飛ばしで間合いの外へ出た敵は、殴り続けられる位置へ置き直す(Rushまで当て続けるための準備)
            if (e == null || Hp(e) <= 10 || Mathf.Abs(e.transform.position.x - pc.transform.position.x - 1.2f) > 0.9f) { ClearEnemies(); e = Spawn(1.2f, 0f); }
            if (pc.BloodRush && rushAt < 0f) rushAt = t;
            if (pc.BloodRush) runRush = Mathf.Max(runRush, pc.VampireRunMultiplier);
            int st = pc.VampireComboStage;
            if (st != prevStage && st > 0) { float dur = t - stageStart; stageStart = t; if (prevStage > 0) { if (pc.BloodRush) { atkSumR += dur; atkR++; } else if (pc.BloodGauge < p.tierThreshold) { atkSumN += dur; atkN++; } } }
            prevStage = st;
            if (rushAt >= 0f && t > rushAt + p.rushDuration + 0.5f) break;
            yield return null; t += Time.deltaTime;
        }
        pc.debugInjectFlick = null;
        int heals = pc.VampireHealsGiven - heals0;
        L($"[Vampire rush] rushAt={rushAt:F1}s rushes+={pc.BloodRushCount - rush0} run {runBefore:F2}->{runRush:F2} avgStage normal={(atkN > 0 ? atkSumN / atkN : 0):F3}({atkN}) rush={(atkR > 0 ? atkSumR / atkR : 0):F3}({atkR}) heals={heals} lives {lives0}->{gm.Lives}/{gm.maxLives} gaugeAfter={pc.BloodGauge:F0}");
        Check(rushAt >= 0f && pc.BloodRushCount - rush0 >= 1, "gauge reaches 100 and Blood Rush starts");
        Check(runRush > runBefore * 1.05f, "Blood Rush speeds up running");
        Check(atkR > 0 && atkN > 0 && atkSumR / atkR < atkSumN / atkN, "Blood Rush speeds up attacks");
        Check(heals <= 1, "healing during Rush is capped (no endless regeneration)");
        Check(!pc.BloodRush || t >= 14f, "Blood Rush ends after its duration");
        Check(pc.VampireRunMultiplier == 1f, "run speed back to normal after Rush");
        ClearEnemies();
        // 後: バックステップ+コウモリ
        yield return WaitIdle();
        yield return WaitNoPit(7f);
        var b = Spawn(-2.6f, 0f);
        yield return Flick(PlayerController.FlickDirection.Backward);
        bool bats = false; { float w = 0f; while (w < 0.8f) { bats |= GameObject.Find("VampireBat") != null; yield return null; w += Time.deltaTime; } }
        L($"[Vampire bats] seen={bats} lost={Lost(b)}");
        Check(bats && Lost(b) > 0, "bat swarm flies backward and hits");
        ClearEnemies();
        // 上: 霧/コウモリ化して斜め上へ(経路に判定、忍者の瞬身と違い透けて上へ流れる)
        yield return WaitIdle();
        yield return WaitNoPit(7f);
        var u = Spawn(1.3f, 1.2f);
        float y0 = pc.transform.position.y, x0 = pc.transform.position.x, apex = y0; float minAlpha = 1f;
        var sr = anim.VisualRenderer;
        yield return Flick(PlayerController.FlickDirection.Up);
        t = 0f; while (t < 0.6f) { apex = Mathf.Max(apex, pc.transform.position.y); if (sr != null) minAlpha = Mathf.Min(minAlpha, sr.color.a); yield return null; t += Time.deltaTime; }
        L($"[Vampire mist] rise={apex - y0:F2} lost={Lost(u)} minAlpha={minAlpha:F2}");
        Check(apex - y0 > 1f, "mist moves the vampire up");
        Check(Lost(u) > 0, "mist path damages the enemy");
        Check(minAlpha < 0.6f, "vampire turns translucent (mist) while moving");
        yield return WaitIdle();
        Check(sr == null || sr.color.a > 0.99f, "no leftover translucency after mist");
        ClearEnemies();
        // 下(空中): 急降下吸血 → ゲージ大幅増加
        yield return WaitIdle();
        yield return Wait(p.decayDelay + 0.5f);
        yield return WaitNoPit(8f);
        yield return Flick(PlayerController.FlickDirection.Up);
        yield return Wait(0.3f);
        var dv = Spawn(2.0f, 0f);
        float gd0 = pc.BloodGauge;
        yield return Flick(PlayerController.FlickDirection.Down);
        t = 0f; while (!pc.IsGrounded && t < 1.5f) { yield return null; t += Time.deltaTime; }
        yield return Wait(0.2f);
        L($"[Vampire dive] landedIn={t:F2}s lost={Lost(dv)} gauge {gd0:F0}->{pc.BloodGauge:F0} rush={pc.BloodRush}");
        Check(Lost(dv) > 0, "dive bite hits");
        Check(pc.BloodGauge - gd0 >= p.gainDiveHit * 0.9f || pc.BloodRush, "dive bite gives a large Blood gain");
        ClearEnemies();
        // 地上の下
        yield return WaitIdle();
        yield return WaitNoPit(7f);
        var lw = Spawn(1.1f, 0f);
        yield return Flick(PlayerController.FlickDirection.Down);
        yield return Wait(0.5f);
        L($"[Vampire low] lost={Lost(lw)}");
        Check(Lost(lw) > 0, "ground down = low blood sweep hits");
        ClearEnemies();
        // 復帰でRushが残らない/キャラ切り替えでゲージが残らない
        respawnM.Invoke(pc, null);
        yield return null;
        L($"[Vampire respawn] gauge={pc.BloodGauge:F0} rush={pc.BloodRush}");
        Check(!pc.BloodRush && pc.BloodGauge <= 30f, "respawn clears Rush and caps the gauge");
    }

    // ============ 竜人 ============
    IEnumerator TestDragonkin()
    {
        var p = CharacterDatabase.FindById("dragonkin").dragonkin;
        yield return WaitIdle();
        Vector2 size = pc.GetComponent<BoxCollider2D>().size;
        L($"[Dragon body] size={size} base={baseBodySize}");
        Check(size.y > baseBodySize.y * 1.05f, "dragonkin has a bigger hurtbox");
        // 前: 爪3段(3段目が重い)
        yield return WaitNoPit(14f);
        var e = Spawn(1.3f, 0f);
        int maxStage = 0; float t = 0f, next = 0f; var lostBy = new int[4]; int last = 0;
        float kbX = 0f;
        while (t < 2.2f)
        {
            if (t >= next) { pc.debugInjectFlick = PlayerController.FlickDirection.Forward; next = t + 0.12f; } else pc.debugInjectFlick = null;
            int st = pc.DragonComboStage; maxStage = Mathf.Max(maxStage, st);
            int lo = Lost(e); if (lo != last && st > 0) { lostBy[st] = Mathf.Max(lostBy[st], lo - last); last = lo; }
            if (e != null) kbX = Mathf.Max(kbX, e.transform.position.x - pc.transform.position.x);
            yield return null; t += Time.deltaTime;
        }
        pc.debugInjectFlick = null;
        L($"[Dragon claw] maxStage={maxStage} hitByStage 1:{lostBy[1]} 2:{lostBy[2]} 3:{lostBy[3]} maxEnemyDistance={kbX:F2}");
        Check(maxStage == 3, "3-hit claw combo");
        Check(lostBy[3] > lostBy[1], "3rd claw hits harder");
        ClearEnemies();
        // 後: 尾(広い範囲)
        yield return WaitIdle();
        yield return WaitNoPit(7f);
        var tb = Spawn(-2.2f, 0f);
        yield return Flick(PlayerController.FlickDirection.Backward);
        bool tailPose = false; { float w = 0f; while (w < 0.6f) { tailPose |= pc.KitPoseName == "tail"; yield return null; w += Time.deltaTime; } }
        L($"[Dragon tail] pose={tailPose} lost={Lost(tb)} launched={(tb != null && (bool)launchedF.GetValue(tb))}");
        Check(Lost(tb) > 0, "tail sweep hits an enemy well behind");
        ClearEnemies();
        // 上: 羽ばたき(通常より高い)+アッパー、落ち際に1回だけ滑空
        yield return WaitIdle();
        yield return WaitNoPit(9f);
        var up = Spawn(0.9f, 0f);
        float y0 = pc.transform.position.y, apex = y0;
        int glide0 = pc.DragonGlideCount;
        float glideTime = 0f, minVy = 0f, prevY = y0;
        yield return Flick(PlayerController.FlickDirection.Up);
        t = 0f;
        while (t < 3f && !(pc.IsGrounded && t > 0.3f))
        {
            float y = pc.transform.position.y;
            apex = Mathf.Max(apex, y);
            if (pc.IsDragonGliding) { glideTime += Time.deltaTime; if (Time.deltaTime > 0f) minVy = Mathf.Min(minVy, (y - prevY) / Time.deltaTime); }
            prevY = y;
            yield return null; t += Time.deltaTime;
        }
        L($"[Dragon flap] rise={apex - y0:F2} normalApex={JumpApex():F2} hit={Lost(up)} glides={pc.DragonGlideCount - glide0} glideTime={glideTime:F2} glideMinVy={minVy:F2}");
        Check(apex - y0 > JumpApex() * 1.15f, "wing flap jumps higher than a normal jump");
        Check(Lost(up) > 0, "flap upper slash hits");
        Check(pc.DragonGlideCount - glide0 == 1 && glideTime > 0.2f && glideTime <= p.glideTime + 0.05f, "one short glide per flap (no endless flight)");
        Check(minVy >= -p.glideFallSpeed - 0.3f, "glide slows the fall");
        Check(!pc.IsDragonGliding, "no glide after landing");
        ClearEnemies();
        // 被弾で滑空が切れる
        yield return WaitIdle();
        yield return WaitNoPit(9f);
        yield return Flick(PlayerController.FlickDirection.Up);
        t = 0f; while (!pc.IsDragonGliding && t < 2f) { yield return null; t += Time.deltaTime; }
        bool gliding = pc.IsDragonGliding;
        gm.DebugSetInvincible(false);
        pc.TakeDamage(source: "test-glide-hurt");
        yield return null;
        bool afterHurt = pc.IsDragonGliding;
        yield return WaitIdle();
        gm.DebugSetInvincible(true);
        L($"[Dragon glide hurt] glidingBefore={gliding} glidingAfterHit={afterHurt}");
        Check(gliding && !afterHurt, "getting hit ends the glide");
        // 下(空中): ブレス → 燃える地面(同時に1つ)
        yield return Wait(0.5f);
        yield return WaitNoPit(10f);
        yield return Flick(PlayerController.FlickDirection.Up);
        yield return Wait(0.3f);
        var fe = Spawn(3.0f, 0f);
        int burns0 = pc.DragonBurnsCreated;
        yield return Flick(PlayerController.FlickDirection.Down);
        bool breathPose = false; int maxFire = 0; KitZone fire = null;
        { float w = 0f; while (w < 1.6f) { breathPose |= pc.KitPoseName == "breath"; maxFire = Mathf.Max(maxFire, KitZone.ActiveCount(KitZone.Kind.Fire)); fire = fire ?? KitZone.Find(KitZone.Kind.Fire); yield return null; w += Time.deltaTime; } }
        L($"[Dragon breath] pose={breathPose} burns+={pc.DragonBurnsCreated - burns0} maxFireZones={maxFire} lost={Lost(fe)} fireTicks={(fire != null ? fire.TicksDone : -1)}");
        Check(pc.DragonBurnsCreated - burns0 >= 1, "breath sets the ground on fire");
        Check(maxFire == 1, "only one burning ground at a time");
        Check(Lost(fe) > 0, "breath / burning ground damages the enemy");
        yield return Wait(p.burnDuration + 0.3f);
        Check(KitZone.ActiveCount(KitZone.Kind.Fire) == 0, "burning ground expires");
        ClearEnemies();
        // 地上の下: 低いブレス
        yield return WaitIdle();
        yield return WaitNoPit(7f);
        var ge = Spawn(2.2f, 0f);
        yield return Flick(PlayerController.FlickDirection.Down);
        yield return Wait(0.9f);
        L($"[Dragon ground breath] lost={Lost(ge)}");
        Check(Lost(ge) > 0, "ground breath hits an enemy ahead");
        ClearEnemies();
        yield return WaitIdle();
    }

    // ============ 被弾 ============
    IEnumerator TestHurtAll()
    {
        foreach (string id in new[] { "miko", "vampire", "dragonkin" })
        {
            yield return Switch(id);
            yield return Flick(PlayerController.FlickDirection.Forward);
            yield return Wait(0.05f);
            gm.DebugSetInvincible(false);
            pc.TakeDamage(source: "test-hurt");
            bool hurt = pc.IsHurt;
            bool attacking = Attacking;
            string spr = anim.VisualRenderer.sprite != null ? anim.VisualRenderer.sprite.name : "null";
            yield return WaitIdle();
            gm.DebugSetInvincible(true);
            yield return Wait(0.3f);
            L($"[Hurt {id}] hurt={hurt} attackingAfterHit={attacking} sprite={spr} poseAfter={pc.KitPoseName ?? "-"} owns={(bool)ownsF.GetValue(pc)}");
            Check(hurt && !attacking, id + ": hit cancels the move and plays Hurt");
            Check(!(bool)ownsF.GetValue(pc) && pc.KitPoseName == null, id + ": no leftover move state after Hurt");
        }
    }

    // ============ 既存9人に混ざらない ============
    IEnumerator TestRegression()
    {
        // 直前に巫女の結界・竜人の炎・吸血鬼のRushを出した状態から切り替える
        yield return Switch("dragonkin");
        yield return WaitNoPit(8f);
        yield return Flick(PlayerController.FlickDirection.Down);
        yield return Wait(0.6f);
        var expect = new Dictionary<string, CharacterKit>
        {
            { "swordsman", CharacterKit.Standard }, { "dual_blade", CharacterKit.Standard }, { "noble_lady", CharacterKit.Standard },
            { "gunslinger", CharacterKit.Standard }, { "dragon_lancer", CharacterKit.Standard },
            { "archer", CharacterKit.Archer }, { "mage", CharacterKit.Mage }, { "fighter", CharacterKit.Fighter }, { "ninja", CharacterKit.Ninja },
        };
        foreach (var kv in expect)
        {
            yield return Switch(kv.Key);
            Vector2 size = pc.GetComponent<BoxCollider2D>().size;
            yield return Flick(PlayerController.FlickDirection.Forward);
            yield return Wait(0.5f);
            string pose = pc.KitPoseName ?? "-";
            bool newPose = pose == "throw" || pose == "claw1" || pose == "claw2" || pose == "claw3" || pose == "bloodslash" || pose == "breath" || pose == "glide";
            L($"[Regression {kv.Key}] kit={pc.Kit} body={size} barrier={KitZone.ActiveCount(KitZone.Kind.Barrier)} fire={KitZone.ActiveCount(KitZone.Kind.Fire)} seals={KitSealMark.ActiveCount} gauge={pc.BloodGauge:F0} rush={pc.BloodRush} glide={pc.IsDragonGliding} pose={pose}");
            Check(pc.Kit == kv.Value, kv.Key + " keeps its own kit");
            Check(Mathf.Abs(size.y - baseBodySize.y) < 0.001f && Mathf.Abs(size.x - baseBodySize.x) < 0.001f, kv.Key + " has the normal hurtbox");
            Check(KitZone.ActiveCount(KitZone.Kind.Barrier) == 0 && KitZone.ActiveCount(KitZone.Kind.Fire) == 0 && KitSealMark.ActiveCount == 0, kv.Key + ": no leftover barrier/fire/seal");
            Check(pc.BloodGauge == 0f && !pc.BloodRush && !pc.IsDragonGliding && !newPose, kv.Key + ": no Blood/glide/new-kit pose");
            yield return WaitIdle();
        }
    }
}

public static class NewChars2TestMenu
{
    [MenuItem("Tools/OneMoreMile/New Characters 2 Test (batch)")]
    public static void RunBatch()
    {
        EditorPrefs.SetInt("NewChars2Test", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif
