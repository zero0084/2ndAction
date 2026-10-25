#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

// 布表現の見直しの実ゲーム確認(2026-10-25): -qaClothCheck <dir> [-ccChar swordsman] [-ccFast 3]
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
                    $"{demoFrame}\t{label}\t{spr}\t{tex}\t{sp.x:F1}\t{Screen.height - sp.y:F1}\t{Screen.height - bt.y:F1}\t{Screen.height - bb.y:F1}\t{p.IsGrounded}\n");
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

    IEnumerator ClothCheckMode()
    {
        Application.targetFrameRate = 60;
        string ch = Arg("-ccChar", "swordsman");
        float fast = float.Parse(Arg("-ccFast", "3"), System.Globalization.CultureInfo.InvariantCulture);
        demoDir = System.IO.Path.Combine(outDir, "frames");
        System.IO.Directory.CreateDirectory(demoDir);

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
