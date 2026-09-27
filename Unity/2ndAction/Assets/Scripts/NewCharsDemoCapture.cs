#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Reflection;
using UnityEngine;

// 新4人(2026-09-27)の確認動画用(開発ビルド専用)。起動引数:
//   -newCharDemo <出力フォルダ> <キャラID> <ステージID> [ceiling]
// 毎フレームをPNGに書き出す(Time.captureFramerate=30で動画の時間を固定、ウィンドウのフォーカスに左右されない)。
// 区間: 通常の移動 → 前 → 後 → 上 → 下。魔法使いは さらに 浮遊/高度上昇/高度下降/穴越え、
// 「ceiling」指定時は洞窟の天井の下を最高高度で飛ぶ場面。
public class NewCharsDemoCapture : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 3; i++)
        {
            if (args[i] != "-newCharDemo") continue;
            var go = new GameObject("NewCharsDemoCapture");
            DontDestroyOnLoad(go);
            var c = go.AddComponent<NewCharsDemoCapture>();
            c.outDir = args[i + 1]; c.charId = args[i + 2]; c.stageId = args[i + 3];
            c.ceilingMode = i + 4 < args.Length && args[i + 4] == "ceiling";
            return;
        }
    }

    string outDir, charId, stageId;
    bool ceilingMode;
    int frame;
    bool capturing;
    PlayerController pc;
    readonly System.Text.StringBuilder times = new System.Text.StringBuilder();
    void Mark(string what) { times.AppendLine($"{what} {frame}"); }

    void LateUpdate()
    {
        if (!capturing) return;
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, $"f_{frame:00000}.png"));
        frame++;
    }

    IEnumerator Start()
    {
        System.IO.Directory.CreateDirectory(outDir);
        yield return new WaitForSecondsRealtime(2f);
        var gm = GameManager.Instance;
        if (charId == "select") { yield return SelectTour(gm); yield break; }
        gm.SetSelectedCharacter(charId);
        gm.SetSelectedStage(stageId);
        typeof(GameManager).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, null);
        pc = PlayerController.Instance;
        var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cf != null) { cf.targetHorizontalHalfWidth = 5.6f; cf.offsetX = 1.8f; cf.highSpeedLookAhead = 0.3f; }
        // Start演出(カウントダウン中の準備ポーズ)から撮る
        Time.captureFramerate = 30;
        capturing = true;
        Mark("start");
        float w = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        var inv = typeof(GameManager).GetProperty("InvincibleMode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        inv?.GetSetMethod(true)?.Invoke(gm, new object[] { true });
        var expF = typeof(GameManager).GetField("expGainMultiplier", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (expF != null) expF.SetValue(gm, 0f);
        foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var s in FindObjectsByType<EnemyWallManager>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var s in FindObjectsByType<EncounterDirector>(FindObjectsSortMode.None)) s.enabled = false;
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        if (TerrainManager.Instance != null) TerrainManager.Instance.enemySpawnChance = 0f;
        // 撮影の途中で穴へ落ちて(落下は無敵でもダメージ)HPが減っていくと不具合に見えるので、魔法使い(穴越えを見せる)以外は
        // これから生成する地形に穴を作らない(撮影専用の設定)。
        if (charId != "mage" && TerrainManager.Instance != null) { TerrainManager.Instance.pitChanceBase = 0f; TerrainManager.Instance.pitChanceRampPer1000m = 0f; TerrainManager.Instance.pitChanceMax = 0f; }
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
        StartCoroutine(AutoPickLevelUp());

        if (ceilingMode) yield return MageCeiling();
        else
        {
            switch (pc.Kit)
            {
                case CharacterKit.Archer: yield return Archer(); break;
                case CharacterKit.Mage: yield return Mage(); break;
                case CharacterKit.Fighter: yield return Fighter(); break;
                case CharacterKit.Ninja: yield return Ninja(); break;
            }
        }
        yield return Wait(0.5f);
        capturing = false;
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "times.txt"), times.ToString());
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "done.txt"), "done");
        yield return new WaitForSecondsRealtime(1f);
        Application.Quit();
    }

    // Character Select: 9人のカードを1枚ずつ中央へスライドさせる(カルーセルのスクロール確認用)。
    IEnumerator SelectTour(GameManager gm)
    {
        var ui = gm.characterSelectUI;
        Time.captureFramerate = 30;
        capturing = true;
        Mark("select");
        gm.OpenCharacterSelect(); // Home(IMGUI)を隠すフラグも立つ正規の入口
        yield return Wait(1.0f);
        var sel = typeof(CharacterSelectUI).GetMethod("SelectIndex", BindingFlags.NonPublic | BindingFlags.Instance);
        var snap = typeof(CharacterSelectUI).GetMethod("BeginSnap", BindingFlags.NonPublic | BindingFlags.Instance);
        int n = CharacterDatabase.AllCharacters.Count;
        for (int i = 0; i < n; i++) { sel.Invoke(ui, new object[] { i }); snap.Invoke(ui, new object[] { i }); yield return Wait(1.1f); }
        for (int i = n - 2; i >= 5; i--) { sel.Invoke(ui, new object[] { i }); snap.Invoke(ui, new object[] { i }); yield return Wait(0.7f); }
        yield return Wait(0.6f);
        capturing = false;
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "times.txt"), times.ToString());
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "done.txt"), "done");
        yield return new WaitForSecondsRealtime(1f);
        Application.Quit();
    }

    // ---------------- 各キャラ ----------------

    IEnumerator RunSection(float seconds)
    {
        Mark("run");
        float rt = 0f, nextJump = 1.8f;
        while (rt < seconds)
        {
            if (NeedJump() || (pc.Kit != CharacterKit.Mage && rt >= nextJump)) { if (rt >= nextJump) nextJump += 2.6f; yield return Flick(PlayerController.FlickDirection.Up); }
            yield return null; rt += Time.deltaTime;
        }
    }

    IEnumerator Archer()
    {
        yield return RunSection(4f);
        // 前: 引き絞り最大 → 3体貫通、続けて連射(弱い矢)
        Mark("forward");
        yield return Safe(8f); yield return Wait(1.3f);
        enemyHp = 30;
        Enemy(3.2f, 0); Enemy(4.3f, 0); Enemy(5.4f, 0);
        yield return Wait(0.2f);
        yield return Flick(PlayerController.FlickDirection.Forward);
        yield return Wait(1.0f);
        Clear(); yield return Safe(8f);
        Enemy(3.0f, 0); Enemy(4.2f, 0);
        for (int i = 0; i < 3; i++) { yield return Flick(PlayerController.FlickDirection.Forward); yield return Wait(0.45f); }
        yield return Wait(0.8f);
        Clear();
        // 後
        Mark("back"); yield return Safe(6f);
        Enemy(-2.4f, 0);
        yield return Flick(PlayerController.FlickDirection.Backward); yield return Wait(0.6f);
        yield return Flick(PlayerController.FlickDirection.Backward); yield return Wait(0.9f);
        Clear();
        // 上
        Mark("up"); yield return Safe(8f);
        Enemy(3.6f, 2.6f);
        yield return Flick(PlayerController.FlickDirection.Up); yield return Wait(1.3f);
        Enemy(3.6f, 2.6f);
        yield return Flick(PlayerController.FlickDirection.Up); yield return Wait(1.3f);
        Clear();
        // 下(空中→地上)
        Mark("down"); yield return Safe(8f);
        yield return Flick(PlayerController.FlickDirection.Up); yield return Wait(0.2f);
        Enemy(3.2f, 0);
        yield return Flick(PlayerController.FlickDirection.Down); yield return Wait(0.35f);
        yield return Flick(PlayerController.FlickDirection.Down); yield return Wait(1.0f);
        yield return Safe(6f);
        Enemy(2.8f, 0);
        yield return Flick(PlayerController.FlickDirection.Down); yield return Wait(1.0f);
        Clear();
    }

    IEnumerator Mage()
    {
        // 浮遊(最低高度)
        Mark("float");
        yield return Wait(3f);
        // 高度上昇 / 下降
        Mark("altitude_up");
        for (int i = 0; i < 3; i++) { yield return Flick(PlayerController.FlickDirection.Up); yield return Wait(0.9f); }
        yield return Wait(0.6f);
        Mark("altitude_down");
        for (int i = 0; i < 3; i++) { yield return Flick(PlayerController.FlickDirection.Down); yield return Wait(0.9f); }
        yield return Wait(0.4f);
        // 穴越え(最低高度のまま次の穴の上を渡る)
        Mark("pit");
        float pw = 0f;
        while (pw < 20f && !PitAhead(6f)) { yield return null; pw += Time.deltaTime; }
        float px0 = pc.transform.position.x;
        while (pw < 26f && pc.transform.position.x < px0 + 12f) { yield return null; pw += Time.deltaTime; }
        Mark("pit_end");
        // 前
        Mark("forward"); yield return Safe(8f);
        enemyHp = 6;
        Enemy(3.2f, 0); Enemy(3.9f, 0); Enemy(6f, 0);
        for (int i = 0; i < 3; i++) { yield return Flick(PlayerController.FlickDirection.Forward); yield return Wait(0.5f); }
        yield return Wait(0.8f); Clear();
        // 後
        Mark("back"); yield return Safe(6f);
        Enemy(-2.2f, 0);
        yield return Flick(PlayerController.FlickDirection.Backward); yield return Wait(0.6f);
        yield return Flick(PlayerController.FlickDirection.Backward); yield return Wait(0.8f);
        Clear();
        // 上(高度上昇+斜め上の雷撃)
        Mark("up"); yield return Safe(8f);
        Enemy(4f, 3.2f); Enemy(5.5f, 3.6f);
        yield return Flick(PlayerController.FlickDirection.Up); yield return Wait(0.8f);
        yield return Flick(PlayerController.FlickDirection.Up); yield return Wait(1.0f);
        Clear();
        // 下(高度下降+真下の魔法)
        Mark("down"); yield return Safe(8f);
        Enemy(2.2f, 0); Enemy(4.5f, 0);
        yield return Flick(PlayerController.FlickDirection.Down); yield return Wait(0.8f);
        yield return Flick(PlayerController.FlickDirection.Down); yield return Wait(1.0f);
        Clear();
    }

    IEnumerator MageCeiling()
    {
        // 洞窟: 次の「天井の針」または「低い天井」の区間が近づいたら最高高度まで上がり、その下を飛ぶ
        // (天井で頭打ちになる/針に当たる様子)。2区間ぶん。
        Mark("ceiling");
        for (int section = 0; section < 2; section++)
        {
            float t = 0f, sx = float.NaN;
            while (t < 40f)
            {
                sx = NextCaveSection(pc.transform.position.x + 6f);
                if (!float.IsNaN(sx) && sx - pc.transform.position.x < 16f) break;
                yield return null; t += Time.deltaTime;
            }
            if (float.IsNaN(sx)) break;
            Mark("section" + section);
            for (int i = 0; i < 3; i++) { yield return Flick(PlayerController.FlickDirection.Up); yield return Wait(0.35f); }
            float w = 0f;
            while (w < 12f && pc.transform.position.x < sx + 14f)
            {
                if (pc.IsReacting) { float rw = 0f; while (pc.IsReacting && rw < 2f) { yield return null; rw += Time.deltaTime; } for (int i = 0; i < 3; i++) { yield return Flick(PlayerController.FlickDirection.Up); yield return Wait(0.3f); } }
                yield return null; w += Time.deltaTime;
            }
            for (int i = 0; i < 3; i++) { yield return Flick(PlayerController.FlickDirection.Down); yield return Wait(0.3f); }
        }
        yield return Wait(1.0f);
    }

    // 洞窟の次の区間(1=天井の針 / 2=低い天井)の開始x(無ければNaN)。
    static float NextCaveSection(float fromX)
    {
        var cave = TerrainManager.Instance != null ? TerrainManager.Instance.cave : null;
        if (cave == null) return float.NaN;
        var list = typeof(CaveStage).GetField("nodes", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(cave) as System.Collections.IList;
        if (list == null) return float.NaN;
        float best = float.NaN;
        foreach (var n in list)
        {
            var t = n.GetType();
            float x = (float)t.GetField("x").GetValue(n);
            int mode = (int)t.GetField("mode").GetValue(n);
            if ((mode == 1 || mode == 2) && x > fromX && (float.IsNaN(best) || x < best)) best = x;
        }
        return best;
    }

    IEnumerator Fighter()
    {
        yield return RunSection(4f);
        Mark("forward"); yield return Safe(6f);
        enemyHp = 40;
        Enemy(1.3f, 0);
        for (int i = 0; i < 6; i++) { yield return Flick(PlayerController.FlickDirection.Forward); yield return Wait(0.13f); }
        yield return Wait(1.0f); Clear();
        yield return Safe(6f);
        Enemy(1.3f, 0);
        for (int i = 0; i < 6; i++) { yield return Flick(PlayerController.FlickDirection.Forward); yield return Wait(0.13f); }
        yield return Wait(1.0f); Clear();
        // 後: バックステップ→肘打ち、続いてカウンター
        Mark("back"); yield return Safe(6f);
        enemyHp = 20;
        Enemy(-0.9f, 0);
        yield return Flick(PlayerController.FlickDirection.Backward); yield return Wait(1.0f);
        Clear(); yield return Safe(6f);
        Enemy(1.2f, 0); Enemy(-0.8f, 0);
        yield return Flick(PlayerController.FlickDirection.Backward); yield return Wait(0.2f);
        pc.TakeDamage(source: "demo-counter"); // 受付中の被弾 → カウンター
        yield return Wait(1.2f); Clear();
        // 上: アッパー
        Mark("up"); yield return Safe(6f);
        Enemy(0.9f, 0);
        yield return Flick(PlayerController.FlickDirection.Up); yield return Wait(1.3f);
        Enemy(0.9f, 0);
        yield return Flick(PlayerController.FlickDirection.Up); yield return Wait(1.3f);
        Clear();
        // 下: 空中ダイブキック → 地上足払い
        Mark("down"); yield return Safe(8f);
        yield return Flick(PlayerController.FlickDirection.Up); yield return Wait(0.3f);
        Enemy(2.4f, 0); Enemy(3.2f, 0);
        yield return Flick(PlayerController.FlickDirection.Down); yield return Wait(1.0f);
        yield return Safe(6f);
        Enemy(1.3f, 0);
        yield return Flick(PlayerController.FlickDirection.Down); yield return Wait(1.0f);
        Clear();
    }

    IEnumerator Ninja()
    {
        yield return RunSection(4f);
        Mark("forward"); yield return Safe(8f);
        enemyHp = 3;
        Enemy(1.8f, 0); Enemy(2.6f, 0);
        yield return Flick(PlayerController.FlickDirection.Forward); yield return Wait(0.7f);
        Enemy(2.0f, 0);
        yield return Flick(PlayerController.FlickDirection.Forward); yield return Wait(0.9f);
        Clear();
        Mark("back"); yield return Safe(6f);
        Enemy(-2.4f, 0);
        yield return Flick(PlayerController.FlickDirection.Backward); yield return Wait(0.5f);
        yield return Flick(PlayerController.FlickDirection.Backward); yield return Wait(0.8f);
        Clear();
        Mark("up"); yield return Safe(8f);
        Enemy(1.8f, 1.3f);
        yield return Flick(PlayerController.FlickDirection.Up); yield return Wait(0.35f);
        Enemy(2.6f, 2.6f);
        yield return Flick(PlayerController.FlickDirection.Up); yield return Wait(1.2f);
        Clear();
        Mark("down"); yield return Safe(8f);
        yield return Flick(PlayerController.FlickDirection.Up); yield return Wait(0.25f);
        Enemy(2.6f, 0); Enemy(3.4f, 0);
        yield return Flick(PlayerController.FlickDirection.Down); yield return Wait(0.9f);
        yield return Safe(6f);
        Enemy(1.4f, 0);
        yield return Flick(PlayerController.FlickDirection.Down); yield return Wait(0.9f);
        Clear();
    }

    // ---------------- 共通 ----------------

    static IEnumerator Wait(float s) { yield return new WaitForSeconds(s); }

    IEnumerator Flick(PlayerController.FlickDirection dir)
    {
        pc.debugInjectFlick = dir;
        do { yield return null; } while (Time.timeScale <= 0f);
        pc.debugInjectFlick = null;
    }

    bool NeedJump()
    {
        var tm = TerrainManager.Instance;
        if (tm == null || pc.Kit == CharacterKit.Mage) return false;
        float lead = 1.1f * Mathf.Max(1f, pc.CurrentAutoRunSpeed / 5f);
        return pc.IsGrounded && tm.IsNearPit(pc.transform.position.x + lead, 0.4f);
    }

    bool PitAhead(float dist)
    {
        var tm = TerrainManager.Instance;
        for (float d = 0.5f; d <= dist; d += 0.25f) if (!tm.GetHeightAt(pc.transform.position.x + d).HasValue) return true;
        return false;
    }

    // この先に穴/低い天井の針が無く、地上(魔法使いは最低高度)にいるところまで待つ(穴はジャンプで越える)。
    IEnumerator Safe(float distance)
    {
        var tm = TerrainManager.Instance;
        float t = 0f;
        while (tm != null && t < 10f)
        {
            bool bad = false;
            for (float d = -1f; d <= distance; d += 0.25f) if (!tm.GetHeightAt(pc.transform.position.x + d).HasValue) { bad = true; break; }
            if (!bad && (pc.IsGrounded || pc.Kit == CharacterKit.Mage) && !pc.IsReacting && !pc.IsAttacking) break;
            if (NeedJump()) yield return Flick(PlayerController.FlickDirection.Up);
            yield return null; t += Time.deltaTime;
        }
    }

    int enemyHp = 3;
    void Enemy(float dx, float dy)
    {
        EnemyDefinition d = EnemyDatabase.FindById("goblin");
        float x = pc.transform.position.x + dx;
        float? gy = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(x) : null;
        float y = (gy ?? pc.transform.position.y) + dy;
        GroundFactory.CreateEnemy(null, d.sprite, new Vector2(x, y), d.tint, maxHp: enemyHp, behaviorKind: EnemyBehaviorKind.None, visualScaleMultiplier: d.visualScaleMultiplier);
    }

    void Clear() { foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject); }

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
}
#endif
