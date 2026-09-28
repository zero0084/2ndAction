#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// 新4人(2026-09-27) - Editor専用の自動確認。結果は NewCharsAutoTest.txt。
// 自然洞窟(天井あり・平坦)で、弓使い→魔法使い→格闘家→忍者の順に:
//  - Character Select相当の選択(NEW RUNは弓使いで開始、以降はキャラ定義を差し替え)と素材の反映
//  - 4方向攻撃それぞれが「そのキャラらしい」挙動をしているか(下の各Test参照)
//  - 被弾/復帰で技の状態が残らない、魔法使いは高度が最低段へ戻る
//  - 既存5人に新4人の処理が混ざらない
public class NewCharsAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("NewCharsTest", 0) != 1) return;
        EditorPrefs.SetInt("NewCharsTest", 0);
        new GameObject("NewCharsTest").AddComponent<NewCharsAutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    int failures;
    void L(string s) { log.AppendLine(s); Debug.Log("[NewCharsTest] " + s); }
    void Check(bool ok, string what) { if (!ok) { failures++; L("  FAIL: " + what); } else L("  ok: " + what); }
    static FieldInfo F(System.Type t, string name) => t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly FieldInfo hpF = F(typeof(EnemyController), "hp");
    static readonly FieldInfo launchedF = F(typeof(EnemyController), "isLaunched");
    static readonly FieldInfo attackingF = F(typeof(PlayerController), "isAttacking");
    static readonly FieldInfo ownsF = F(typeof(PlayerController), "kitOwnsAttack");
    static readonly FieldInfo iframeF = F(typeof(PlayerController), "kitIFrameTimer");

    GameManager gm;
    PlayerController pc;
    PlayerAnimator anim;
    readonly List<GameObject> spawned = new List<GameObject>();

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1.5f);
        gm = GameManager.Instance;
        pc = PlayerController.Instance;
        anim = pc.GetComponentInChildren<PlayerAnimator>();
        gm.SetSelectedCharacter("archer");
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
            // 判定の確認中に穴へ落ちて(落下復帰で)技が中断されると結果がぶれるので、これから生成する地形には穴を作らない
            // (魔法使いの穴越えは確認動画で見せる)。天井の針は魔法使いの天井確認で使うので残す。
            TerrainManager.Instance.pitChanceBase = 0f;
            TerrainManager.Instance.pitChanceRampPer1000m = 0f;
            TerrainManager.Instance.pitChanceMax = 0f;
        }
        typeof(GameManager).GetField("expGainMultiplier", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(gm, 0f);
        StartCoroutine(AutoPickLevelUp());
        yield return new WaitForSeconds(0.3f);
        ClearEnemies();
        // 開始時点で既に生成済みの地形(先の約64m)には穴が残っているので、穴を作らない設定にしてから80m走っておく
        // (穴はジャンプで越える)。以降の確認はすべて穴の無い地形で行われる。
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
        Check(gm.SelectedCharacterId == "archer" && pc.Kit == CharacterKit.Archer, "archer selected via NEW RUN");
        Check(CharacterDatabase.AllCharacters.Count == 12, $"12 characters in database (got {CharacterDatabase.AllCharacters.Count})");

        yield return TestArcher();
        yield return Switch("mage");
        yield return TestMage();
        yield return Switch("fighter");
        yield return TestFighter();
        yield return Switch("ninja");
        yield return TestNinja();
        yield return TestHurtAll();
        yield return TestRegression();

        Application.logMessageReceived -= handler;
        L("");
        L(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        L(anyException ? "SOME EXCEPTIONS LOGGED (see [EXC] lines above)" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../NewCharsAutoTest.txt"), log.ToString());
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
        spawned.Clear();
    }

    EnemyController Spawn(float dx, float dy)
    {
        EnemyDefinition d = EnemyDatabase.FindById("goblin");
        Vector3 p = pc.transform.position;
        float x = p.x + dx;
        float? gy = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(x) : null;
        float y = (gy ?? p.y) + dy;
        var go = GroundFactory.CreateEnemy(null, d.sprite, new Vector2(x, y), d.tint, maxHp: 999, behaviorKind: EnemyBehaviorKind.None);
        spawned.Add(go);
        return go.GetComponent<EnemyController>();
    }

    int Hp(EnemyController e) => e == null ? -9999 : (int)hpF.GetValue(e);
    // hpは初めて被弾するまで-1(未初期化)なので、その間は「無傷」として扱う。
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
        // 実機のフリックと同じく「1回=1フレームだけ」読ませる(HitStop中はPlayerControllerが入力を読まないので、読まれるフレームまで保持)。
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

    // この先aheadの範囲に穴(地面の無い所)が無くなるまで待つ(穴の上だと地面基準の確認ができない)。
    IEnumerator WaitNoPit(float ahead)
    {
        float t = 0f;
        while (t < 10f)
        {
            float x0 = pc.transform.position.x - 1f;
            bool pit = false;
            for (float x = x0; x < x0 + ahead + 1f; x += 0.25f) if (!TerrainManager.Instance.GetHeightAt(x).HasValue) { pit = true; break; }
            // 低い天井の針の下では、地上にいても針に当たって技が中断される(正しい挙動)ので、針の無い所まで待つ。
            if (!pit && SpikesNear(pc.transform.position.x + ahead * 0.5f, ahead * 0.5f + 1.5f).Count > 0) pit = true;
            if (!pit && !pc.IsReacting) yield break;
            // 待っている間に穴へ落ちないよう、穴の手前ではジャンプで越える(魔法使いは浮遊で越えるので不要)
            float lead = 1.1f * Mathf.Max(1f, pc.CurrentAutoRunSpeed / 5f);
            if (pc.Kit != CharacterKit.Mage && pc.IsGrounded && !pc.IsReacting && TerrainManager.Instance.IsNearPit(pc.transform.position.x + lead, 0.4f))
                yield return Flick(PlayerController.FlickDirection.Up);
            yield return null; t += Time.deltaTime;
        }
    }

    float Ground(float x) => TerrainManager.Instance != null ? (TerrainManager.Instance.GetHeightAt(x) ?? pc.transform.position.y) : 0f;

    // ============ 弓使い ============
    IEnumerator TestArcher()
    {
        var def = CharacterDatabase.FindById("archer");
        L($"\n===== archer kit={pc.Kit} poses={(def.kitPoses != null ? def.kitPoses.Length : 0)}");
        // 最大チャージ: 前の射撃から十分待つ → 3体を貫通
        yield return WaitIdle();
        yield return Wait(1.4f);
        int stageBefore = pc.ArcherChargeStage;
        yield return WaitNoPit(7f);
        var a1 = Spawn(3.2f, 0f); var a2 = Spawn(4.4f, 0f); var a3 = Spawn(5.6f, 0f);
        yield return Flick(PlayerController.FlickDirection.Forward);
        bool drawPose = pc.KitPoseName == "draw";
        {
            var es = new[] { a1, a2, a3 }; var prev = new int[3]; float w = 0f;
            while (w < 0.9f)
            {
                for (int i = 0; i < 3; i++)
                {
                    int lo = Lost(es[i]);
                    if (lo != prev[i])
                    {
                        var arrow = GameObject.Find("ArcherArrow_Max");
                        L($"   t={w:F2} enemy{i + 1} lost {prev[i]}->{lo} enemyX={(es[i] != null ? es[i].transform.position.x - pc.transform.position.x : 0f):F2} arrowX={(arrow != null ? arrow.transform.position.x - pc.transform.position.x : -99f):F2} ts={Time.timeScale:F2}");
                        prev[i] = lo;
                    }
                }
                yield return null; w += Time.unscaledDeltaTime;
            }
        }
        L($"[Archer max] stageBefore={stageBefore} lastStage={pc.ArcherLastShotStage} drawPose={drawPose} lost={Lost(a1)},{Lost(a2)},{Lost(a3)}");
        Check(stageBefore == 2 && pc.ArcherLastShotStage == 2, "max charge reached before shot");
        Check(Lost(a1) > 0 && Lost(a2) > 0 && Lost(a3) > 0, "max charge arrow pierces 3 enemies");
        int maxDmg = Lost(a1);
        ClearEnemies();
        // 連射: すぐ次を撃つ → 弱い矢、貫通しない
        yield return WaitIdle();
        yield return Flick(PlayerController.FlickDirection.Forward); // 空撃ちで引き絞りをリセット
        { float w = 0f; while ((Attacking || pc.KitPoseName != null) && w < 1f) { yield return null; w += Time.deltaTime; } }
        yield return WaitNoPit(7f);
        var b1 = Spawn(3.2f, 0f); var b2 = Spawn(4.4f, 0f);
        yield return Flick(PlayerController.FlickDirection.Forward); // 撃ち終わってすぐ次を撃つ
        yield return Wait(0.9f);
        L($"[Archer quick] lastStage={pc.ArcherLastShotStage} lost={Lost(b1)},{Lost(b2)} (max arrow dealt {maxDmg})");
        Check(pc.ArcherLastShotStage == 0, "rapid shot is the weak stage");
        Check(Lost(b1) > 0 && Lost(b2) == 0, "weak arrow stops at the first enemy");
        Check(maxDmg > Lost(b1), "max charge hits harder than a quick shot");
        ClearEnemies();
        // 後ろ
        yield return WaitIdle();
        yield return WaitNoPit(7f);
        var c1 = Spawn(-2.6f, 0f);
        yield return Flick(PlayerController.FlickDirection.Backward);
        yield return Wait(0.7f);
        L($"[Archer back] lost={Lost(c1)}");
        Check(Lost(c1) > 0, "back arrow hits enemy behind");
        ClearEnemies();
        // 上: ジャンプ+斜め上の矢
        yield return WaitIdle();
        yield return WaitNoPit(7f);
        var d1 = Spawn(3.0f, 1.9f);
        yield return Flick(PlayerController.FlickDirection.Up);
        bool upPose = pc.KitPoseName == "upshot";
        yield return Wait(0.8f);
        L($"[Archer up] upPose={upPose} lost={Lost(d1)} launched={(d1 != null && (bool)launchedF.GetValue(d1))}");
        Check(Lost(d1) > 0, "up arrow hits a high enemy");
        ClearEnemies();
        // 空中の下: 1回の滞空で2回まで
        yield return WaitIdle();
        yield return WaitNoPit(8f);
        yield return Flick(PlayerController.FlickDirection.Up);
        yield return Wait(0.12f);
        int shots0 = pc.ArcherShotsFired;
        int downArrows = 0;
        for (int i = 0; i < 3; i++)
        {
            if (pc.IsGrounded) break;
            int s0 = pc.ArcherShotsFired;
            yield return Flick(PlayerController.FlickDirection.Down);
            yield return Wait(0.3f);
            if (pc.ArcherShotsFired > s0) downArrows++;
        }
        L($"[Archer air down] arrows={downArrows} (shots fired {pc.ArcherShotsFired - shots0}) grounded={pc.IsGrounded}");
        Check(downArrows == 2, "air down shot limited to 2 per airtime");
        // 地上の下: 低い矢
        yield return WaitIdle();
        yield return WaitNoPit(7f);
        var e1 = Spawn(3f, 0f);
        yield return Wait(0.2f);
        int lowShots0 = pc.ArcherShotsFired;
        bool groundedAtFlick = pc.IsGrounded; bool attackingAtFlick = Attacking;
        yield return Flick(PlayerController.FlickDirection.Down);
        string arrowInfo = "none";
        { float w = 0f; while (w < 0.7f) { var ar = GameObject.Find("ArcherArrow_Low"); if (ar != null) { var col = e1 != null ? e1.GetComponent<Collider2D>() : null; arrowInfo = $"arrowY={ar.transform.position.y - pc.transform.position.y:F2} enemyCol=[{(col != null ? (col.bounds.min.y - pc.transform.position.y).ToString("F2") + ".." + (col.bounds.max.y - pc.transform.position.y).ToString("F2") : "-")}] dx={(e1 != null ? e1.transform.position.x - ar.transform.position.x : 0f):F2}"; } yield return null; w += Time.deltaTime; } }
        L($"[Archer low] lost={Lost(e1)} shots={pc.ArcherShotsFired - lowShots0} groundedAtFlick={groundedAtFlick} attackingAtFlick={attackingAtFlick} {arrowInfo}");
        Check(Lost(e1) > 0, "ground down = low shot hits");
        ClearEnemies();
    }

    // ============ 魔法使い ============
    IEnumerator TestMage()
    {
        var p = CharacterDatabase.FindById("mage").mage;
        yield return Wait(0.8f);
        yield return WaitNoPit(6f);
        float x = pc.transform.position.x;
        float hover = pc.transform.position.y - Ground(x);
        {
            // 1秒間の浮遊高さの推移(最低/最高)と、その間の被弾/天井
            float hmin = 99f, hmax = -99f, w = 0f; int sp0 = CaveStage.SpikeHitCount; bool reacted = false, blocked = false;
            var surfF = typeof(PlayerController).GetField("mageSurface", BindingFlags.NonPublic | BindingFlags.Instance);
            while (w < 1f)
            {
                float hh = pc.transform.position.y - Ground(pc.transform.position.x);
                hmin = Mathf.Min(hmin, hh); hmax = Mathf.Max(hmax, hh);
                reacted |= pc.IsReacting; blocked |= pc.MageCeilingBlocked;
                yield return null; w += Time.deltaTime;
            }
            L($"[Mage float 1s] hover min={hmin:F2} max={hmax:F2} reacted={reacted} blocked={blocked} spikeHits+={CaveStage.SpikeHitCount - sp0} kit={pc.Kit} y={pc.transform.position.y:F2} ground={Ground(pc.transform.position.x):F2} mageSurface={(float)surfF.GetValue(pc):F2} timeScale={Time.timeScale:F2} grounded={pc.IsGrounded}");
            hover = hmax;
        }
        L($"[Mage float] level={pc.MageAltitudeLevel} hover={hover:F2} (expect ~{p.hoverBase}) ceilingBlocked={pc.MageCeilingBlocked}");
        Check(Mathf.Abs(hover - p.hoverBase) < 0.2f || pc.MageCeilingBlocked, "floats slightly above ground (or pressed down by a low ceiling)");
        // 高度を上げる/下げる
        yield return Flick(PlayerController.FlickDirection.Up);
        yield return Wait(0.35f);
        yield return Flick(PlayerController.FlickDirection.Up);
        yield return Wait(0.9f);
        float yLevel2 = pc.transform.position.y - Ground(pc.transform.position.x);
        float? ceil = TerrainManager.Instance.GetCeilingLimitY(pc.transform.position.x);
        L($"[Mage up x2] level={pc.MageAltitudeLevel} height={yLevel2:F2} ceilingLimit={(ceil.HasValue ? (ceil.Value - Ground(pc.transform.position.x)).ToString("F2") : "none")} blocked={pc.MageCeilingBlocked}");
        Check(pc.MageAltitudeLevel == 2, "two up flicks = altitude level 2");
        Check(yLevel2 > p.hoverBase + p.altitudeStep * 1.5f || pc.MageCeilingBlocked, "actually higher (or held by the ceiling)");
        yield return Flick(PlayerController.FlickDirection.Down);
        yield return Wait(0.9f);
        L($"[Mage down] level={pc.MageAltitudeLevel} height={pc.transform.position.y - Ground(pc.transform.position.x):F2}");
        Check(pc.MageAltitudeLevel == 1, "down flick = altitude level 1");
        // 天井: 最高高度で走り続けても天井(上限)を越えない。天井の針は通常どおり判定される。
        for (int i = 0; i < 3; i++) { yield return Flick(PlayerController.FlickDirection.Up); yield return Wait(0.3f); }
        int spikeBefore = CaveStage.SpikeHitCount;
        float over = 0f; int blockedFrames = 0; float t = 0f;
        var spikeXs = new HashSet<float>();
        while (t < 40f && (CaveStage.SpikeHitCount == spikeBefore || blockedFrames == 0))
        {
            float px = pc.transform.position.x;
            float? lim = TerrainManager.Instance.GetCeilingLimitY(px);
            if (lim.HasValue) over = Mathf.Max(over, pc.transform.position.y - lim.Value);
            if (pc.MageCeilingBlocked) blockedFrames++;
            foreach (float sx in SpikesNear(px, 0.4f)) spikeXs.Add(Mathf.Round(sx * 10f));
            if (pc.IsReacting) { yield return WaitIdle(); for (int i = 0; i < 3; i++) { yield return Flick(PlayerController.FlickDirection.Up); yield return Wait(0.3f); } }
            yield return null; t += Time.deltaTime;
        }
        int spikesPassed = spikeXs.Count;
        L($"[Mage ceiling] maxOverCeiling={over:F3} blockedFrames={blockedFrames} spikesPassed={spikesPassed} spikeHits {spikeBefore}->{CaveStage.SpikeHitCount} t={t:F1}s");
        Check(spikesPassed == 0 || CaveStage.SpikeHitCount > spikeBefore, "flying high under ceiling spikes still gets hit (spikes are not ignored)");
        yield return WaitIdle();
        Check(over <= 0.02f, "never above the cave ceiling limit while flying");
        Check(blockedFrames > 0 || CaveStage.SpikeHitCount > spikeBefore, "the ceiling (height limit or spikes) actually interacted with the high flight");
        // 降りて攻撃
        for (int i = 0; i < 3; i++) { yield return Flick(PlayerController.FlickDirection.Down); yield return Wait(0.3f); }
        yield return Wait(0.6f);
        yield return Wait(0.4f);
        yield return WaitNoPit(7f);
        var f1 = Spawn(3.5f, 0f); var f2 = Spawn(4.2f, 0f);
        string boltState = $"level={pc.MageAltitudeLevel} hover={pc.transform.position.y - Ground(pc.transform.position.x):F2} attacking={Attacking} reacting={pc.IsReacting}";
        yield return Flick(PlayerController.FlickDirection.Forward);
        string boltInfo = "no bolt";
        { float w = 0f; while (w < 0.4f) { var b = GameObject.Find("MageBolt"); if (b != null) { var col = f1 != null ? f1.GetComponent<Collider2D>() : null; boltInfo = $"boltY={b.transform.position.y - Ground(b.transform.position.x):F2} enemyCol=[{(col != null ? (col.bounds.min.y - Ground(col.bounds.center.x)).ToString("F2") + ".." + (col.bounds.max.y - Ground(col.bounds.center.x)).ToString("F2") : "-")}]"; } yield return null; w += Time.deltaTime; } }
        yield return Wait(0.5f);
        L($"[Mage bolt] lost={Lost(f1)},{Lost(f2)} {boltState} {boltInfo}");
        Check(Lost(f1) > 0, "forward bolt hits");
        Check(Lost(f2) > 0, "bolt explosion also hits the enemy next to it");
        ClearEnemies();
        yield return Wait(0.4f);
        yield return WaitNoPit(7f);
        var g1 = Spawn(-2.5f, 0f);
        yield return Flick(PlayerController.FlickDirection.Backward);
        yield return Wait(0.8f);
        L($"[Mage back] lost={Lost(g1)}");
        Check(Lost(g1) > 0, "back bolt hits behind");
        ClearEnemies();
        // 下: 高い所から真下の魔法(地面で爆発)
        yield return Flick(PlayerController.FlickDirection.Up);
        yield return Wait(0.4f);
        yield return Flick(PlayerController.FlickDirection.Up);
        yield return Wait(0.9f);
        yield return WaitNoPit(6f);
        var h1 = Spawn(2.2f, 0f);
        yield return Flick(PlayerController.FlickDirection.Down);
        yield return Wait(0.9f);
        L($"[Mage down magic] level={pc.MageAltitudeLevel} lost={Lost(h1)}");
        Check(Lost(h1) > 0, "downward magic explodes on the ground below");
        ClearEnemies();
        // 被弾で高度が最低段へ戻り、浮遊に復帰する
        gm.DebugSetInvincible(false);
        pc.TakeDamage(source: "test");
        yield return WaitIdle();
        gm.DebugSetInvincible(true);
        yield return Wait(0.6f);
        yield return WaitNoPit(2f);
        yield return Wait(0.5f);
        float h = pc.transform.position.y - Ground(pc.transform.position.x);
        L($"[Mage hurt] level={pc.MageAltitudeLevel} height={h:F2} lives={gm.Lives}");
        Check(pc.MageAltitudeLevel == 0 && Mathf.Abs(h - p.hoverBase) < 0.25f, "after hurt: back to lowest altitude and floating again");
    }

    // 洞窟の天井の針(CaveStage.spikes)のうち、プレイヤーの真上付近(±r)にあるもののx(通り過ぎた針はリストから消えるので、毎フレーム見る)。
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

    // ============ 格闘家 ============
    IEnumerator TestFighter()
    {
        yield return WaitIdle();
        // 最短射程: 少し離れた敵には届かない
        // 判定の前端(体からの距離)を黒剣士の通常攻撃1段目と比べる
        var sb = pc.attackHitbox;
        float sbw = sb is BoxCollider2D sbBox ? sbBox.size.x : 1f;
        float swordReach = sb.transform.localPosition.x + sbw * Mathf.Abs(sb.transform.localScale.x) * 0.5f;
        yield return Flick(PlayerController.FlickDirection.Forward);
        float fighterReach = 0f; { float w = 0f; while (w < 0.4f) { if (pc.KitHitbox.enabled) fighterReach = Mathf.Max(fighterReach, pc.KitHitbox.bounds.max.x - pc.transform.position.x); yield return null; w += Time.deltaTime; } }
        L($"[Fighter range] jab reach={fighterReach:F2} swordsman reach={swordReach:F2}");
        Check(fighterReach > 0.5f && fighterReach < swordReach, "fighter's reach is shorter than the Black Swordsman's");
        yield return WaitIdle();
        yield return Wait(0.5f);
        // 4段コンボ
        yield return WaitNoPit(14f);
        var e = Spawn(1.05f, 0f);
        int maxStage = 0; var lostAt = new int[5]; int last = 0;
        yield return null;
        float t = 0f; float nextFlick = 0f;
        while (t < 2.2f)
        {
            if (t >= nextFlick) { pc.debugInjectFlick = PlayerController.FlickDirection.Forward; nextFlick = t + 0.12f; }
            else pc.debugInjectFlick = null;
            int st = pc.FighterComboStage;
            maxStage = Mathf.Max(maxStage, st);
            int lost = Lost(e);
            if (lost != last && st > 0) { lostAt[st] = Mathf.Max(lostAt[st], lost - last); last = lost; }
            yield return null; t += Time.deltaTime;
        }
        pc.debugInjectFlick = null;
        L($"[Fighter combo] maxStage={maxStage} maxHitByStage=1:{lostAt[1]} 2:{lostAt[2]} 3:{lostAt[3]} 4:{lostAt[4]}");
        Check(maxStage == 4, "4-hit combo reaches the 4th stage");
        Check(lostAt[4] > lostAt[1], "heavy (4th) hits harder than the jab");
        ClearEnemies();
        yield return WaitIdle();
        yield return Wait(0.3f);
        // カウンター
        yield return WaitNoPit(7f);
        var near = Spawn(1.1f, 0f);
        gm.DebugSetInvincible(false);
        int lives = gm.Lives;
        string atFlick = $"attacking={Attacking} grounded={pc.IsGrounded} reacting={pc.IsReacting} pose={pc.KitPoseName ?? "-"}";
        yield return Flick(PlayerController.FlickDirection.Backward);
        yield return Wait(0.2f);
        bool ready = pc.FighterCounterReady;
        L($"   [counter flick] {atFlick}");
        pc.TakeDamage(source: "test-counter");
        yield return Wait(0.6f);
        int livesAfter = gm.Lives;
        gm.DebugSetInvincible(true);
        L($"[Fighter counter] ready={ready} counters={pc.FighterCounterCount} lives {lives}->{livesAfter} nearLost={Lost(near)} hurt={pc.IsHurt}");
        Check(ready && pc.FighterCounterCount == 1, "back step opens a counter window and a hit triggers the counter");
        Check(livesAfter == lives, "countered hit deals no damage");
        Check(Lost(near) > 0, "counter strike hits the nearby enemy");
        ClearEnemies();
        yield return WaitIdle();
        yield return Wait(0.3f);
        // 後ろ(何も来ない) → 肘打ち
        yield return WaitNoPit(7f);
        var behind = Spawn(-1.0f, 0f);
        yield return Flick(PlayerController.FlickDirection.Backward);
        yield return Wait(0.9f);
        L($"[Fighter rear strike] lost={Lost(behind)}");
        Check(Lost(behind) > 0, "rear strike hits an enemy behind");
        ClearEnemies();
        yield return WaitIdle();
        // 上: アッパー(打ち上げ、自分は黒剣士のジャンプより低い)
        yield return WaitNoPit(6f);
        yield return WaitNoPit(6f);
        float y0 = pc.transform.position.y, apex = y0;
        yield return Flick(PlayerController.FlickDirection.Up);
        t = 0f;
        while (t < 1.0f) { apex = Mathf.Max(apex, pc.transform.position.y); yield return null; t += Time.deltaTime; }
        yield return WaitIdle();
        yield return WaitNoPit(4f);
        var up = Spawn(0.75f, 0f);
        yield return Flick(PlayerController.FlickDirection.Up);
        bool launched = false; t = 0f;
        while (t < 1.0f) { if (up != null && (bool)launchedF.GetValue(up)) launched = true; yield return null; t += Time.deltaTime; }
        float normalApex = pc.jumpForce * pc.jumpForce / (2f * pc.gravity);
        L($"[Fighter uppercut] launched={launched} lost={Lost(up)} apex={apex - y0:F2} normalJumpApex={normalApex:F2}");
        Check(launched, "uppercut launches the enemy");
        Check(apex - y0 < normalApex * 0.85f, "uppercut lifts less than a normal jump");
        ClearEnemies();
        yield return WaitIdle();
        // 空中の下: ダイブキック
        yield return WaitNoPit(8f);
        yield return Flick(PlayerController.FlickDirection.Up);
        yield return Wait(0.25f);
        var land = Spawn(2.2f, 0f);
        yield return Flick(PlayerController.FlickDirection.Down);
        t = 0f; while (!pc.IsGrounded && t < 1.5f) { yield return null; t += Time.deltaTime; }
        yield return Wait(0.3f);
        L($"[Fighter dive kick] landedIn={t:F2}s lost={Lost(land)}");
        Check(t < 0.9f, "dive kick drops quickly");
        Check(Lost(land) > 0, "dive kick / landing impact hits");
        ClearEnemies();
        yield return WaitIdle();
        // 地上の下: 足払い
        yield return WaitNoPit(7f);
        var sw = Spawn(1.1f, 0f);
        yield return Flick(PlayerController.FlickDirection.Down);
        yield return Wait(0.5f);
        L($"[Fighter sweep] lost={Lost(sw)}");
        Check(Lost(sw) > 0, "sweep hits");
        ClearEnemies();
    }

    // ============ 忍者 ============
    IEnumerator TestNinja()
    {
        yield return WaitIdle();
        yield return Wait(0.5f);
        // 瞬身: 敵をすり抜けて斬る(無敵はごく短時間)
        gm.DebugSetInvincible(false);
        int lives = gm.Lives;
        yield return WaitNoPit(7f);
        var e = Spawn(1.6f, 0f);
        float ex = e.transform.position.x;
        yield return Flick(PlayerController.FlickDirection.Forward);
        float iframe = (float)iframeF.GetValue(pc);
        yield return Wait(0.5f);
        L($"[Ninja dash] passed={pc.transform.position.x > ex} lost={Lost(e)} lives {lives}->{gm.Lives} iframeAtStart={iframe:F2}");
        Check(pc.transform.position.x > ex, "dash passes through the enemy");
        Check(Lost(e) > 0, "passing slash hits");
        Check(gm.Lives == lives, "no contact damage while passing through (short i-frames)");
        Check(iframe > 0f && iframe <= 0.2f, "i-frames are very short");
        gm.DebugSetInvincible(true);
        ClearEnemies();
        // 連打しても永久無敵にならない
        yield return WaitIdle();
        int grants0 = pc.NinjaIFrameGrants, dashes0 = pc.NinjaDashCount;
        float invTime = 0f, t = 0f, nextFlick = 0f;
        while (t < 4f)
        {
            if (t >= nextFlick) { pc.debugInjectFlick = PlayerController.FlickDirection.Forward; nextFlick = t + 0.1f; } else pc.debugInjectFlick = null;
            if (pc.IsKitInvincible) invTime += Time.deltaTime;
            yield return null; t += Time.deltaTime;
        }
        pc.debugInjectFlick = null;
        L($"[Ninja spam] dashes={pc.NinjaDashCount - dashes0} iframeGrants={pc.NinjaIFrameGrants - grants0} invincibleFraction={invTime / 4f:P0}");
        Check(invTime / 4f < 0.3f, "dash spam does not create near-permanent invincibility");
        Check(pc.NinjaDashCount - dashes0 > pc.NinjaIFrameGrants - grants0, "not every dash grants i-frames");
        yield return WaitIdle();
        // 後ろ: 手裏剣
        yield return WaitNoPit(7f);
        var b = Spawn(-2.4f, 0f);
        yield return Flick(PlayerController.FlickDirection.Backward);
        yield return Wait(0.6f);
        L($"[Ninja shuriken] lost={Lost(b)}");
        Check(Lost(b) > 0, "shuriken hits behind");
        ClearEnemies();
        yield return WaitIdle();
        // 上: 跳躍斬り
        yield return WaitNoPit(7f);
        var u = Spawn(1.6f, 1.3f);
        yield return Flick(PlayerController.FlickDirection.Up);
        yield return Wait(0.5f);
        L($"[Ninja up dash] lost={Lost(u)}");
        Check(Lost(u) > 0, "up dash slash hits a raised enemy");
        ClearEnemies();
        yield return WaitIdle();
        // 空中の下: 急降下斬り
        yield return WaitNoPit(8f);
        yield return Flick(PlayerController.FlickDirection.Up);
        yield return Wait(0.2f);
        var d = Spawn(2.6f, 0f);
        yield return Flick(PlayerController.FlickDirection.Down);
        t = 0f; while (!pc.IsGrounded && t < 1.5f) { yield return null; t += Time.deltaTime; }
        yield return Wait(0.3f);
        L($"[Ninja down dash] landedIn={t:F2}s lost={Lost(d)}");
        Check(t < 0.7f, "down dash drops fast");
        Check(Lost(d) > 0, "down dash / landing slash hits");
        ClearEnemies();
    }

    // ============ 被弾/死亡 ============
    IEnumerator TestHurtAll()
    {
        foreach (string id in new[] { "archer", "mage", "fighter", "ninja" })
        {
            yield return Switch(id);
            // 忍者の前(瞬身)は無敵が付くので、被弾の確認は後ろ(手裏剣)で行う
            yield return Flick(id == "ninja" ? PlayerController.FlickDirection.Backward : PlayerController.FlickDirection.Forward);
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

    // ============ 既存5人に混ざらない ============
    IEnumerator TestRegression()
    {
        foreach (string id in new[] { "swordsman", "dual_blade", "noble_lady", "gunslinger", "dragon_lancer" })
        {
            yield return Switch(id);
            bool kitBox = false, melee = false;
            yield return Flick(PlayerController.FlickDirection.Forward);
            float t = 0f;
            while (t < 0.5f) { if (pc.KitHitbox != null && pc.KitHitbox.enabled) kitBox = true; if (pc.attackHitbox != null && pc.attackHitbox.enabled) melee = true; yield return null; t += Time.deltaTime; }
            L($"[Regression {id}] kit={pc.Kit} kitHitbox={kitBox} swordHitbox={melee} kitPose={(pc.KitPoseFrames != null)}");
            Check(pc.Kit == CharacterKit.Standard && !kitBox && pc.KitPoseFrames == null, id + " unaffected by the new kits");
            yield return WaitIdle();
        }
    }
}

// 同じ矢を二重に数えないための印(テスト専用)。
public class NewCharsTag : MonoBehaviour { }

public static class NewCharsTestMenu
{
    [MenuItem("Tools/OneMoreMile/New Characters Test (batch)")]
    public static void RunBatch()
    {
        EditorPrefs.SetInt("NewCharsTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif
