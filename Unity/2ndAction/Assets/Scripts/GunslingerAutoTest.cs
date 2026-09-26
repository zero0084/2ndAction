#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// 二丁拳銃士追加(2026-09-23) - Editor専用の自動確認。
// 1) Character SelectでGUNSLINGERを選び、NEW RUNで実際に二丁拳銃士として
//    開始できるか。
// 2) Forward/Backward/Up/Downの各フリックで、既存の剣士Hitboxではなく
//    PlayerBullet(細い直線・タグPlayerAttack)が正しい方向へ発射されるか。
// 3) 発射した弾が実際に敵(EnemyController)へダメージを与えるか(既存の
//    OnTriggerEnter2D経由、コード変更なし)。
// 4) Up Shotが敵をLaunchしない(PlayerAttackKind.Normal)ことの確認。
// 5) Down Shot中だけ落下速度が弱まり(velocityY==-hoverFallSpeed)、
//    hoverDuration後に通常落下へ戻ること。「1回の滞空中に1回」制限。
// 6) 既存3キャラ(黒剣士)にisRangedCharacterやPlayerBullet生成が一切
//    起きない(既存の剣士Hitboxが従来どおり有効になる)ことの回帰確認。
// 結果は GunslingerAutoTest.txt。
public class GunslingerAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (EditorPrefs.GetInt("GunslingerTest", 0) != 1) return;
        EditorPrefs.SetInt("GunslingerTest", 0);
        new GameObject("GunslingerTest").AddComponent<GunslingerAutoTest>();
    }

    readonly StringBuilder log = new StringBuilder();
    void L(string s) { log.AppendLine(s); Debug.Log("[GunslingerTest] " + s); }

    static FieldInfo F(System.Type t, string name) => t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1.5f);
        var gm = GameManager.Instance;
        var pc = PlayerController.Instance;

        gm.SetSelectedCharacter("gunslinger");
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        float t0 = Time.time;
        while (!(gm.HasStarted && !gm.CountdownActive) && Time.time - t0 < 10f) yield return null;
        gm.DebugSetInvincible(true); // テスト中はPlayer自身は無敵にする(敵の反撃で本題以外の失敗が混ざらないように)

        L($"stage={gm.ActiveRunStageId} character={gm.SelectedCharacterId} lives={gm.Lives}");
        FieldInfo isRangedField = F(typeof(PlayerController), "isRangedCharacter");
        L($"[Setup] isRangedCharacter={isRangedField.GetValue(pc)} (expect True)");

        foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var s in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) s.enabled = false;
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        if (TerrainManager.Instance != null) TerrainManager.Instance.enemySpawnChance = 0f;
        yield return new WaitForSeconds(0.3f);
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
        yield return null;

        bool anyException = false;
        Application.LogCallback handler = (cond, trace, type) =>
        {
            if (type == LogType.Exception || type == LogType.Error) { anyException = true; L("[EXC] " + cond); }
        };
        Application.logMessageReceived += handler;

        yield return TestShotHeightCoversGroundEnemies(pc);
        yield return TestForwardShot(pc);
        yield return TestBackwardShot(pc);
        yield return TestUpShot(pc);
        yield return TestDownShot(pc);
        yield return TestSwordsmanRegression(pc, gm);

        Application.logMessageReceived -= handler;

        L("");
        L(anyException ? "SOME EXCEPTIONS LOGGED (see [EXC] lines above)" : "NO EXCEPTIONS LOGGED");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../GunslingerAutoTest.txt"), log.ToString());

        if (Application.isBatchMode) EditorApplication.Exit(anyException ? 1 : 0); else EditorApplication.isPlaying = false;
    }

    IEnumerator InjectFlick(PlayerController pc, PlayerController.FlickDirection dir)
    {
        pc.debugInjectFlick = dir;
        yield return null;
        pc.debugInjectFlick = null;
    }

    GameObject SpawnTargetEnemy(PlayerController pc, float offsetX)
    {
        EnemyDefinition def = EnemyDatabase.FindById("goblin");
        if (def == null) return null;
        Vector3 p = pc.transform.position;
        float x = p.x + offsetX;
        float? gy = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(x) : null;
        float y = (gy ?? p.y) + 0.55f;
        GameObject go = GroundFactory.CreateEnemy(null, def.sprite, new Vector2(x, y), def.tint,
            maxHp: 999, behaviorKind: EnemyBehaviorKind.None);
        return go;
    }

    List<PlayerBullet> FindBullets() => new List<PlayerBullet>(FindObjectsByType<PlayerBullet>(FindObjectsSortMode.None));

    // 複数のテストを連続実行すると前のテストの弾(寿命内)がまだ画面に残っている
    // ことがあるため、「フリック前後の集合差分」で今回新しく生成された弾だけを
    // 確実に拾う(FindObjectsByTypeの返却順に依存しない)。
    PlayerBullet NewestBullet(List<PlayerBullet> before, List<PlayerBullet> after)
    {
        foreach (var b in after) if (!before.Contains(b)) return b;
        return null;
    }

    // 攻撃モーション見直し(2026-09-26) - 前方射撃の銃口を新素材に合わせて
    // 0.55→0.71へ上げたため、全地上雑魚敵の当たり判定の高さ範囲に弾の帯
    // (中心y±弾コライダー半分)が入るかを確認する。背の低い敵を撃ち漏らす
    // 回帰のガード。
    IEnumerator TestShotHeightCoversGroundEnemies(PlayerController pc)
    {
        float groundY = pc.transform.position.y;
        float fy = pc.rangedForwardMuzzleOffset.y;
        const float bulletHalf = 0.08f; // PlayerBulletのcol.size.y(0.16)の半分
        float groundEnemyHeight = TerrainManager.Instance != null ? TerrainManager.Instance.groundEnemyHeight : 0f;
        int miss = 0, checkedCount = 0;
        foreach (var def in EnemyDatabase.AllEnemies)
        {
            if (def == null || def.sprite == null || def.movementType == EnemyMovementType.Flying) continue;
            Vector3 p = pc.transform.position + new Vector3(30f, 0f, 0f);
            GameObject go = GroundFactory.CreateEnemy(null, def.sprite, new Vector2(p.x, groundY + groundEnemyHeight), def.tint,
                maxHp: 999, behaviorKind: EnemyBehaviorKind.None, visualScaleMultiplier: def.visualScaleMultiplier);
            var col = go.GetComponent<BoxCollider2D>();
            Physics2D.SyncTransforms();
            float lo = col.bounds.min.y - groundY, hi = col.bounds.max.y - groundY;
            bool hit = fy + bulletHalf >= lo && fy - bulletHalf <= hi;
            checkedCount++;
            if (!hit) miss++;
            L($"[ShotHeight] {def.enemyId}: collider y={lo:F2}..{hi:F2} bullet y={fy:F2} hit={hit}");
            Destroy(go);
        }
        L($"[ShotHeight] checked={checkedCount} miss={miss} (expect miss=0)");
        yield return null;
    }

    IEnumerator TestForwardShot(PlayerController pc)
    {
        GameObject target = SpawnTargetEnemy(pc, 6f);
        FieldInfo hpField = F(typeof(EnemyController), "hp");
        var ec = target.GetComponent<EnemyController>();
        yield return null;

        pc.attackHitbox.enabled = false; // 事前状態を明示
        var before = FindBullets();
        yield return InjectFlick(pc, PlayerController.FlickDirection.Forward);
        PlayerBullet fwdBullet = NewestBullet(before, FindBullets());
        L($"[ForwardShot] bullet spawned={fwdBullet != null} velocity.x>0={(fwdBullet != null && fwdBullet.velocity.x > 0)} meleeHitboxStillOff={!pc.attackHitbox.enabled} (expect True,True,True)");

        // hpは-1のsentinelのまま初期化され、実際に被弾した瞬間だけ
        // EnsureHp()でmaxHp(999)へ確定してからダメージが引かれる - 変化した
        // (もはや-1ではない)ことそのものが「弾が実際に命中した」証拠になる。
        int hpSentinelBefore = (int)hpField.GetValue(ec);
        float t = 0f;
        while (t < 2f && target != null && (int)hpField.GetValue(ec) == hpSentinelBefore) { yield return null; t += Time.deltaTime; }
        int hpAfter = target != null ? (int)hpField.GetValue(ec) : -9999;
        L($"[ForwardShot] target hp sentinel-before={hpSentinelBefore} after={hpAfter} (expect after != {hpSentinelBefore} and < 999 = bullet damaged the enemy)");
        // 不具合修正(このテスト自身) - 被弾直後のEnemyController側HitStop
        // コルーチン(StartCoroutine(HitStop.Freeze(...))、Enemy自身の
        // GameObjectで実行中)が終わる前にDestroy()すると、finallyブロックが
        // 走らずTimeControlのPause owner登録が残ったまま(=Time.timeScaleが
        // 0に固着)になる - HitStopは通常tens of msで終わるため、十分な
        // 実時間バッファを置いてから破棄する。
        yield return new WaitForSecondsRealtime(0.5f);
        if (target != null) Destroy(target);
        yield return new WaitForSecondsRealtime(0.1f);
    }

    IEnumerator TestBackwardShot(PlayerController pc)
    {
        var before = FindBullets();
        FieldInfo isAttackingF = F(typeof(PlayerController), "isAttacking");
        FieldInfo comboBufferedF = F(typeof(PlayerController), "comboBuffered");
        FieldInfo comboWindowOpenF = F(typeof(PlayerController), "comboWindowOpen");
        FieldInfo attackCooldownTimerF = F(typeof(PlayerController), "attackCooldownTimer");
        FieldInfo comboCountF = F(typeof(PlayerController), "comboCount");
        L($"[BackwardShot][diag-before] isAttacking={isAttackingF.GetValue(pc)} comboWindowOpen={comboWindowOpenF.GetValue(pc)} comboBuffered={comboBufferedF.GetValue(pc)} cooldownTimer={attackCooldownTimerF.GetValue(pc)} comboCount={comboCountF.GetValue(pc)} beforeBulletCount={before.Count} timeScale={Time.timeScale:F2}");
        // 直前のForward Shotの命中でHitStop(既存のTimeControl/Time.timeScale=0
        // 短時間停止)がかかっている可能性がある - PlayerController.Update()は
        // 冒頭でTime.timeScale<=0の間まるごとreturnする(=debugInjectFlickすら
        // 読まない)ため、3フレームなど短時間だけ入力を与えると、その全フレーム
        // がHitStop中に収まって一切拾われないことがある。HitStopが明けて実際に
        // 消費される(comboBuffered化またはisAttacking新規trueへ遷移)まで、
        // 実時間ベースで保持し続ける。
        pc.debugInjectFlick = PlayerController.FlickDirection.Backward;
        PlayerBullet backBullet = null;
        float t = 0f;
        while (t < 2f && backBullet == null)
        {
            yield return null; t += Time.unscaledDeltaTime;
            backBullet = NewestBullet(before, FindBullets());
        }
        pc.debugInjectFlick = null;
        L($"[BackwardShot][diag-after] isAttacking={isAttackingF.GetValue(pc)} comboWindowOpen={comboWindowOpenF.GetValue(pc)} comboBuffered={comboBufferedF.GetValue(pc)} cooldownTimer={attackCooldownTimerF.GetValue(pc)} comboCount={comboCountF.GetValue(pc)} waitedRealSec={t:F2}");
        bool mirrored = pc.transform.localScale.x < 0f;
        L($"[BackwardShot] bullet spawned={backBullet != null} velocity.x<0={(backBullet != null && backBullet.velocity.x < 0)} playerMirrored={mirrored} (expect True,True,True)");
        yield return new WaitForSeconds(0.6f); // コンボ/Lungeタイマーが終わるのを待つ
        pc.transform.localScale = Vector3.one;
    }

    IEnumerator TestUpShot(PlayerController pc)
    {
        FieldInfo velField = F(typeof(PlayerController), "velocityY");
        var before = FindBullets();
        yield return InjectFlick(pc, PlayerController.FlickDirection.Up);
        yield return null; // Move()が1回走ってvelocityY=jumpForceが反映されるのを待つ
        float velAfterJump = (float)velField.GetValue(pc);
        PlayerBullet upBullet = NewestBullet(before, FindBullets());
        var kindField = upBullet != null ? upBullet.GetComponent<PlayerAttackInfo>() : null;
        L($"[UpShot] jumped(velocityY>0)={velAfterJump > 0f} bullet spawned={upBullet != null} velocity.y>0={(upBullet != null && upBullet.velocity.y > 0f)} kind={(kindField != null ? kindField.kind.ToString() : "?")} upAttackHitboxUnused={!pc.upAttackHitbox.enabled} (expect True,True,True,Normal,True)");
    }

    IEnumerator TestDownShot(PlayerController pc)
    {
        // Up Shotの直後でまだ空中にいるはず。
        FieldInfo velField = F(typeof(PlayerController), "velocityY");
        FieldInfo hoverFallField = F(typeof(PlayerController), "rangedHoverFallSpeed");
        FieldInfo hoverUsedField = F(typeof(PlayerController), "hoverShotsUsedThisAirtime");
        float hoverFallSpeed = (float)hoverFallField.GetValue(pc);

        // 3連続Down Shot検証(2026-09-24)のため、この先1〜2秒分の滞空時間を
        // 通常のジャンプ高度だけに頼らず確実に確保しておく(本番のジャンプ力
        // /重力チューニングとは無関係な、テストの決定性のためだけの上方
        // テレポート)。
        pc.transform.position += new Vector3(0f, 8f, 0f);

        var beforeDown = FindBullets();
        yield return InjectFlick(pc, PlayerController.FlickDirection.Down);
        yield return null;
        bool hoveringNow = pc.IsRangedHoverShooting;
        float velDuringHover = (float)velField.GetValue(pc);
        PlayerBullet downBullet = NewestBullet(beforeDown, FindBullets());
        L($"[DownShot] hovering={hoveringNow} velocityY≈-hoverFallSpeed={Mathf.Approximately(velDuringHover, -hoverFallSpeed)} bullet spawned={downBullet != null} velocity.y<0={(downBullet != null && downBullet.velocity.y < 0f)} (expect True,True,True,True)");

        // ホバー中に再度フリックしても新しい弾は出ない(1発分のホバーが
        // 終わるまでは次弾を撃てない、という間隔自体は維持)。
        var beforeRetry = FindBullets();
        yield return InjectFlick(pc, PlayerController.FlickDirection.Down);
        PlayerBullet retryBullet = NewestBullet(beforeRetry, FindBullets());
        L($"[DownShot] retry while hovering does not fire again: {retryBullet == null} (expect True)");

        float t = 0f;
        while (t < 1.5f && pc.IsRangedHoverShooting) { yield return null; t += Time.deltaTime; }
        L($"[DownShot] hover ended within 1.5s: {!pc.IsRangedHoverShooting}");

        // 3連続Down Shot(2026-09-24、マスター指示で1回→3回へ拡張) - ホバー
        // 終了後なら同じ滞空中でも2発目・3発目が撃てること、4発目は撃てない
        // ことを確認する。テスト環境の1回のジャンプでは実際の重力次第で
        // 3発分の滞空時間が足りず着地してしまう(=カウントが0へリセット
        // されて本来の上限チェックを検証できない)ことがあるため、各ホバー
        // 終了直後・まだ空中にいる間だけvelocityYを軽く持ち上げて滞空を
        // 延長する(本番のジャンプ力/重力調整とは無関係な、テスト専用の
        // 決定的な検証のための措置)。
        if (!pc.IsGrounded) velField.SetValue(pc, 4f);
        var before2nd = FindBullets();
        yield return InjectFlick(pc, PlayerController.FlickDirection.Down);
        yield return null;
        PlayerBullet secondBullet = NewestBullet(before2nd, FindBullets());
        L($"[DownShot] 2nd shot same airtime: bullet spawned={secondBullet != null} hoverShotsUsedThisAirtime={hoverUsedField.GetValue(pc)} (expect True, 2)");
        float t3 = 0f;
        while (t3 < 1.5f && pc.IsRangedHoverShooting) { yield return null; t3 += Time.deltaTime; }

        if (!pc.IsGrounded) velField.SetValue(pc, 4f);
        var before3rd = FindBullets();
        yield return InjectFlick(pc, PlayerController.FlickDirection.Down);
        yield return null;
        PlayerBullet thirdBullet = NewestBullet(before3rd, FindBullets());
        L($"[DownShot] 3rd shot same airtime: bullet spawned={thirdBullet != null} hoverShotsUsedThisAirtime={hoverUsedField.GetValue(pc)} (expect True, 3)");
        float t4 = 0f;
        while (t4 < 1.5f && pc.IsRangedHoverShooting) { yield return null; t4 += Time.deltaTime; }

        var before4th = FindBullets();
        yield return InjectFlick(pc, PlayerController.FlickDirection.Down);
        yield return null;
        PlayerBullet fourthBullet = NewestBullet(before4th, FindBullets());
        L($"[DownShot] 4th attempt same airtime is blocked: bullet spawned={fourthBullet != null} (expect False)");

        // 着地するまで待ち、着地後に回数がリセットされるか確認。
        float t2 = 0f;
        while (t2 < 6f && !pc.IsGrounded) { yield return null; t2 += Time.deltaTime; }
        L($"[DownShot] landed within 6s: {pc.IsGrounded} hoverShotsUsedThisAirtime reset={(int)hoverUsedField.GetValue(pc) == 0}");
    }

    IEnumerator TestSwordsmanRegression(PlayerController pc, GameManager gm)
    {
        // Runを再スタートせず、既存キャラへの性能反映(ApplyCharacterBaseStats)
        // だけを直接呼び直して、剣士側の経路が一切変わっていないことを
        // 確認する(荒野街道ボス/自然洞窟テストと同じ「直接呼び出しで確認」
        // の考え方)。
        CharacterDefinition swordsman = CharacterDatabase.FindById("swordsman");
        pc.ApplyCharacterBaseStats(swordsman);
        FieldInfo isRangedField = F(typeof(PlayerController), "isRangedCharacter");
        L($"[Regression] swordsman isRangedCharacter={isRangedField.GetValue(pc)} (expect False)");

        int bulletsBefore = FindBullets().Count;
        yield return InjectFlick(pc, PlayerController.FlickDirection.Forward);
        yield return new WaitForSeconds(0.05f);
        int bulletsAfter = FindBullets().Count;
        L($"[Regression] swordsman forward flick spawns no new bullet: {bulletsAfter == bulletsBefore} meleeHitboxEnabled={pc.attackHitbox.enabled} (expect True,True)");
        yield return new WaitForSeconds(0.6f);
    }
}

public static class GunslingerTestMenu
{
    [MenuItem("Tools/OneMoreMile/Gunslinger Test (batch)")]
    public static void RunBatch()
    {
        EditorPrefs.SetInt("GunslingerTest", 1);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
#endif
