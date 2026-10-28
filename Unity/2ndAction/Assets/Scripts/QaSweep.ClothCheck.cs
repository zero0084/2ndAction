#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

// 布表現の見直しの実ゲーム確認(2026-10-25): -qaClothCheck <dir> [-ccChar swordsman] [-ccFast 3] [-ccOnly sf|card]
//  -ccOnly card: キャラのカード(portrait)が出る所を撮る(ホームの額縁 / キャラ選択の選択中・非選択 / 闘技場のキャラ欄)
//  -ccOnly sf: 出発(カウントダウン→GO→走り)と、距離4段階の本物の脱出(減速→到着のポーズ→暗転→結果)だけを撮る
//  キャラ選択の立ち絵を撮影 → 荒野で 普通の速さの走り / 高速の走り / 走り→攻撃・ジャンプ攻撃・下攻撃・被弾 → 暗い洞窟の走りと攻撃 を、
//  30fps で毎フレーム書き出す(<dir>/frames/f00000.png、区切りは <dir>/demo_ranges.txt)。
//  プレイヤーの絵の名前・状態・画面の位置を毎フレーム <dir>/cloth_vis.txt に書く(揺れ・7→1 のつながりの測定用)
public partial class QaSweep
{
    IEnumerator ClothRecord(string label, float seconds, System.Func<int, IEnumerator> each = null)
    {
        int n = Mathf.RoundToInt(seconds * 30f);
        L($"[cloth] {label}: frames {demoFrame}-{demoFrame + n - 1}");
        System.IO.File.AppendAllText(System.IO.Path.Combine(outDir, "demo_ranges.txt"), $"{label}\t{demoFrame}\t{demoFrame + n - 1}\n");
        for (int i = 0; i < n; i++)
        {
            var job = each != null ? each(i) : null;
            if (job != null) StartCoroutine(job);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(demoDir, $"f{demoFrame:00000}.png"));
            var p = PlayerController.Instance;
            if (p != null && Camera.main != null)
            {
                var sr = p.GetComponentInChildren<SpriteRenderer>();
                Vector3 sp = Camera.main.WorldToScreenPoint(p.transform.position);
                string spr = sr != null && sr.sprite != null ? sr.sprite.name : "-";
                string tex = sr != null && sr.sprite != null ? sr.sprite.texture.name : "-";
                Bounds b = sr != null ? sr.bounds : default;
                Vector3 bt = Camera.main.WorldToScreenPoint(new Vector3(b.center.x, b.max.y, 0f));
                Vector3 bb = Camera.main.WorldToScreenPoint(new Vector3(b.center.x, b.min.y, 0f));
                System.IO.File.AppendAllText(System.IO.Path.Combine(outDir, "cloth_vis.txt"),
                    $"{demoFrame}\t{label}\t{spr}\t{tex}\t{sp.x:F1}\t{Screen.height - sp.y:F1}\t{Screen.height - bt.y:F1}\t{Screen.height - bb.y:F1}\t{p.IsGrounded}\t{Time.realtimeSinceStartup:F3}\n");
            }
            demoFrame++;
        }
    }

    IEnumerator ClothSetupRun(string ch, string stage)
    {
        yield return BeginRun(ch, stage);
        SetGod(true);
        if (BossManager.Instance != null) BossManager.Instance.enabled = false;
        WarpTo(260f);
        yield return new WaitForSeconds(1.5f);
    }

    IEnumerator ClothInject(PlayerController.FlickDirection d) { if (pc != null) pc.debugInjectOnce = d; yield break; }

    IEnumerator ClothHurt()
    {
        SetGod(false);
        typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { 150 });
        SetPrivate(pc, "hitInvincibleTimer", 0f);
        pc.TakeDamage(false, "cloth-hurt");
        yield return null;
        SetGod(true);
    }

    // 出発: ランを始めてからカウントダウンの間も撮る(BeginRun はカウントダウンが終わるまで待つため別に書く)
    IEnumerator ClothBeginRecordStart(string ch, string stage)
    {
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        gm.SetSelectedCharacter(ch);
        gm.SetSelectedStage(stage);
        // カウントダウンは実時間(WaitForSecondsRealtime)、出発のコマはゲームの時間で進む。撮影の時間を固定すると実時間だけが先に進み
        // 2コマ目が出る前に GO になるので、ここだけ固定せずに撮る(1コマごとの実時刻は cloth_vis.txt の最後の列。動画はそれで組む)
        Time.captureFramerate = 0;
        typeof(GameManager).GetMethod("StartGame", NP).Invoke(gm, null);
        yield return ClothRecord("start", 5f); // 3・2・1・GO の約3秒 + 走り出し
        w = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 20f) { yield return null; w += Time.unscaledDeltaTime; }
        pc = PlayerController.Instance;
        stopKeepAlive = false;
        StartCoroutine(KeepAlive());
    }

    // 本物の脱出(長押しの完了)。到着のポーズに入ったら長押しをやめる
    IEnumerator ClothEscape()
    {
        SetPrivate(gm, "escapeUnlocked", true);
        SetPrivate(pc, "escapeHoldTimer", pc.escapeHoldDuration - 0.02f);
        PlayerController.QaEscapeHold = true;
        float w = 0f;
        while (!pc.IsFinishing && w < 3f) { yield return null; w += Time.unscaledDeltaTime; }
        PlayerController.QaEscapeHold = false;
        L($"[cloth] escape: finishing={pc.IsFinishing} tier={pc.FinishTierIndex} d={gm.MaxDistance:F0}");
    }

    // 出発と、距離4段階(0=1km未満 / 1=10km未満 / 2=50km未満 / 3=それ以上)の到着 → 結果の画面
    IEnumerator ClothStartFinish(string ch)
    {
        float[] dists = { 500f, 5000f, 20000f, 60000f };
        for (int k = 0; k < dists.Length; k++)
        {
            if (k == 0) yield return ClothBeginRecordStart(ch, "wasteland_road");
            else yield return BeginRun(ch, "wasteland_road");
            SetGod(true);
            if (BossManager.Instance != null) BossManager.Instance.enabled = false;
            WarpTo(dists[k]);
            RunLedger.DevClearDebug();
            yield return new WaitForSeconds(1.5f);
            RunLedger.DevClearDebug();
            Time.captureFramerate = 30;
            yield return ClothRecord($"finish_t{k}", 5.5f, i => i == 15 ? ClothEscape() : null);
            Time.captureFramerate = 0;
            PlayerController.QaEscapeHold = false;
            // 2026-10-28: 1回だけ win=False になった件の切り分け(確定したか / 終わったか / 理由 / 結果を何回決めたか / 記録)
            var lc = gm.LastCommit;
            L($"[cloth] finish tier {k} state: over={gm.IsGameOver} win={gm.IsWin} reason={gm.DeathReason} escapeConfirmed={gm.EscapeConfirmed} finishing={(pc != null && pc.IsFinishing)} FinishRun x{gm.FinishRunCalls} commit={(lc != null && lc.committed)} d={gm.MaxDistance:F0}");
            Check(gm.IsGameOver && gm.IsWin, $"cloth: finish tier {k} reached the result (win={gm.IsWin})");
            yield return EndRun();
        }
    }

    IEnumerator ClothCardShots(string ch)
    {
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        gm.SetSelectedCharacter(ch);
        yield return new WaitForSecondsRealtime(3.5f); // タイトルのフェード
        Shot($"card_home_{ch}");
        yield return null; yield return null;
        // キャラ選択: 選択中 / となりを選んだ時(このキャラのカードは非選択)
        gm.OpenCharacterSelect();
        yield return new WaitForSecondsRealtime(1.5f);
        var ui = FindFirstObjectByType<CharacterSelectUI>();
        var all = CharacterDatabase.AllCharacters;
        int idx = -1;
        for (int i = 0; i < all.Count; i++) if (all[i].characterId == ch) idx = i;
        if (ui != null && idx >= 0)
        {
            ui.DebugSelectAndCenter(idx);
            yield return new WaitForSecondsRealtime(1.2f); Shot($"card_select_{ch}_selected"); yield return null; yield return null;
            ui.DebugSelectAndCenter(idx + 1 < all.Count ? idx + 1 : idx - 1);
            yield return new WaitForSecondsRealtime(1.2f); Shot($"card_select_{ch}_unselected"); yield return null; yield return null;
            ui.DebugSelectAndCenter(idx);
            yield return new WaitForSecondsRealtime(0.8f);
            ui.Close();
            yield return new WaitForSecondsRealtime(1.2f);
        }
        // 闘技場のキャラ欄(読み直しの後に設定の画面が開く)
        ArenaMode.Config.character = ch;
        ArenaLauncher.Launch("qa cloth card", true);
        w = 0f;
        while (!(ArenaMode.Active && ArenaController.Instance != null && ArenaController.Instance.PanelOpen) && w < 30f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        var ac = ArenaController.Instance;
        Check(ac != null && ac.PanelOpen, "cloth card: the arena setup opened");
        if (ac != null)
        {
            ac.Tab = 0;
            ArenaMode.Config.character = ch;
            yield return new WaitForSecondsRealtime(0.6f); Shot($"card_arena_{ch}_selected"); yield return null; yield return null;
            string other = all[idx + 1 < all.Count ? idx + 1 : idx - 1].characterId;
            ArenaMode.Config.character = other;
            yield return new WaitForSecondsRealtime(0.6f); Shot($"card_arena_{ch}_unselected"); yield return null; yield return null;
            ArenaMode.Config.character = ch;
        }
    }

    IEnumerator ClothCheckMode()
    {
        Application.targetFrameRate = 60;
        string ch = Arg("-ccChar", "swordsman");
        float fast = float.Parse(Arg("-ccFast", "3"), System.Globalization.CultureInfo.InvariantCulture);
        demoDir = System.IO.Path.Combine(outDir, "frames");
        System.IO.Directory.CreateDirectory(demoDir);
        if (Arg("-ccOnly", "") == "card") { yield return ClothCardShots(ch); yield break; }
        if (Arg("-ccOnly", "") == "sf")
        {
            yield return ClothStartFinish(ch);
            L($"[cloth] total {demoFrame} frames");
            Check(demoFrame > 600, "cloth: frames written");
            yield break;
        }

        // 0) キャラ選択の立ち絵
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        yield return new WaitForSecondsRealtime(1.5f);
        gm.OpenCharacterSelect();
        yield return new WaitForSecondsRealtime(1.5f);
        var ui = FindFirstObjectByType<CharacterSelectUI>();
        if (ui != null)
        {
            var all = CharacterDatabase.AllCharacters;
            for (int i = 0; i < all.Count; i++) if (all[i].characterId == ch) { ui.DebugSelectAndCenter(i); break; }
            yield return new WaitForSecondsRealtime(1.2f);
            var img = ui.mainVisualImage;
            L($"[cloth] select: {(img != null && img.sprite != null ? img.sprite.texture.name + " " + img.sprite.texture.width + "x" + img.sprite.texture.height : "none")}");
            Shot($"charselect_{ch}");
            yield return new WaitForSecondsRealtime(0.5f);
            ui.Close(); // キャラ選択を閉じる(GameManager.CloseCharacterSelect は印を戻すだけで画面は閉じない)
            yield return new WaitForSecondsRealtime(1.5f);
            L($"[cloth] select closed: {!ui.isActiveAndEnabled || !ui.gameObject.activeInHierarchy}");
        }

        // 1) 荒野: 普通 / 高速 / 状態の切り替え
        yield return ClothSetupRun(ch, "wasteland_road");
        Time.captureFramerate = 30;
        PlayerController.DebugSpeedScale = 1f;
        yield return ClothRecord("run_normal", 4f);
        PlayerController.DebugSpeedScale = fast;
        yield return new WaitForSeconds(1.2f); // 走りのテンポがなじむまで(0.4秒の時定数)
        yield return ClothRecord("run_fast", 4f);
        PlayerController.DebugSpeedScale = 1f;
        yield return new WaitForSeconds(1.2f);
        var F = PlayerController.FlickDirection.Forward; var U = PlayerController.FlickDirection.Up; var D = PlayerController.FlickDirection.Down;
        yield return ClothRecord("transitions", 9f, i =>
        {
            // 0.6s 前攻撃 / 1.8s 前攻撃 / 3.0s ジャンプ攻撃 / 3.6s 空中で下攻撃 / 5.4s 被弾 / 7.0s ジャンプ攻撃
            if (i == 18 || i == 54) return ClothInject(F);
            if (i == 90 || i == 210) return ClothInject(U);
            if (i == 108) return ClothInject(D);
            if (i == 162) return ClothHurt();
            return null;
        });
        // 2026-10-25: 3段のコンボ(1段目 attacksmall / 2段目 attack / 3段目 attacklarge)。前攻撃を続けて入れる
        yield return ClothRecord("combo", 5f, i => (i % 75 == 6 || i % 75 == 18 || i % 75 == 30) ? ClothInject(F) : null);
        // 空中の上攻撃(ジャンプ中にもう一度↑)と、下攻撃 → 着地の突き刺し
        yield return ClothRecord("air", 6f, i => (i == 6 || i == 22 || i == 90 || i == 106) ? ClothInject(U) : (i == 40 || i == 124) ? ClothInject(D) : null);
        // 倒れる(ゲームオーバー)。最後に撮る。倒れる絵は結果の画面が出る前に流れるので、とどめの直後から撮る
        SetGod(false);
        stopKeepAlive = true;
        var livesP = typeof(GameManager).GetProperty("Lives").GetSetMethod(true);
        yield return ClothRecord("death", 3f, i =>
        {
            if (i % 6 == 0 && !gm.IsGameOver)
            {
                livesP.Invoke(gm, new object[] { 1 });
                SetPrivate(pc, "hitInvincibleTimer", 0f);
                pc.TakeDamage(false, "cloth-death");
            }
            return null;
        });
        yield return EndRun();

        // 2) 暗い洞窟: 走り + 攻撃
        yield return ClothSetupRun(ch, "natural_cave");
        Time.captureFramerate = 30;
        yield return ClothRecord("cave_run", 5f, i => i == 90 ? ClothInject(F) : i == 120 ? ClothInject(U) : null);
        PlayerController.DebugSpeedScale = fast;
        yield return new WaitForSeconds(1.2f);
        yield return ClothRecord("cave_fast", 3f);
        Time.captureFramerate = 0;
        PlayerController.DebugSpeedScale = 1f;
        yield return EndRun();
        L($"[cloth] total {demoFrame} frames");
        Check(demoFrame > 600, "cloth: frames written");
    }
}
#endif
