#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

// タップ攻撃(2026-10-23 復活)の確認: -qaTap <dir> [-tpChars ...] [-tpAuto 0,1](専用の保存 -fileSave で)。
//  全12人 × 地上/空中 × オート OFF/ON で、タップ1回 = 前攻撃1回(振りが出る)・前への踏み込みが無い(右フリックとの比較)・
//  二重に出ない、連打で右フリックの連打より速く振れない、を確かめる。
//  実際の指の判定(短い/動かさない/フリックの指/長押し/UI の指)は PlayerController.UpdatePointerInput。ここは DebugTapNow で「タップとして確定した」所から先を見る
public partial class QaSweep
{
    IEnumerator TapMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        gm = GameManager.Instance;
        bool safe = FileSaveStore.Active || MemorySaveStore.Active;
        Check(safe, "runs on a dedicated save only");
        if (!safe) yield break;
        string[] chars = Arg("-tpChars", "swordsman,dual_blade,noble_lady,gunslinger,dragon_lancer,archer,mage,fighter,ninja,miko,vampire,dragonkin").Split(',');
        string[] autos = Arg("-tpAuto", "0,1").Split(',');
        foreach (var auto in autos)
        {
            bool autoOn = auto == "1";
            var hsa = HighSpeedAssist.Instance;
            if (hsa != null) { hsa.SetEnabled(autoOn); hsa.SetAutoAttack(autoOn); }
            foreach (var ch in chars)
            {
                yield return BeginRun(ch, "wasteland_road");
                pc = PlayerController.Instance;
                yield return new WaitForSeconds(1.0f);
                // 地上: タップ / 右フリック
                yield return TapMeasure(ch, autoOn, true, false);
                yield return new WaitForSeconds(0.9f);
                yield return TapMeasure(ch, autoOn, true, true);
                yield return new WaitForSeconds(0.9f);
                // 空中: ↑で跳んでから(上りの途中で)タップ / 右フリック
                yield return TapMeasure(ch, autoOn, false, false);
                yield return new WaitForSeconds(1.2f);
                yield return TapMeasure(ch, autoOn, false, true);
                yield return new WaitForSeconds(1.2f);
                // 連打: 1.2 秒に 30 回タップ → 振りの数は 右フリック 30 回と同じ以下(攻撃の速さ/硬直を超えない)
                int tapSw = 0, flickSw = 0;
                yield return Mash(true, r => tapSw = r);
                yield return new WaitForSeconds(1.0f);
                yield return Mash(false, r => flickSw = r);
                L($"[tap] {ch} auto={(autoOn ? 1 : 0)} mash: tap swings {tapSw} / flick swings {flickSw}");
                if (tapSw >= 0 && flickSw >= 0) Check(tapSw <= flickSw + 1, $"{ch} auto={(autoOn ? 1 : 0)}: mashing taps does not attack faster than mashing flicks ({tapSw} vs {flickSw})");
                else L($"[tap] {ch}: mash compare skipped (hit during mashing)");
                if (tapSw >= 0) Check(tapSw >= 1, $"{ch} auto={(autoOn ? 1 : 0)}: mashing taps attacks");
                yield return EndRun();
            }
        }
        if (HighSpeedAssist.Instance != null) { HighSpeedAssist.Instance.SetEnabled(true); HighSpeedAssist.Instance.SetAutoAttack(true); }
    }

    IEnumerator TapMeasure(string ch, bool autoOn, bool ground, bool flick)
    {
        // 前の技が終わって、地上で動ける状態から
        float g = 0f;
        while ((!pc.IsGrounded || pc.QaInputBusy || pc.IsReacting) && g < 3f) { yield return null; g += Time.deltaTime; } // 被弾の反応の間は入力を受け付けない(仕様)ので待つ
        yield return new WaitForSeconds(0.15f);
        if (!ground)
        {
            // ↑で跳ぶ(跳べるまで最大3回)。上りの途中で入力
            for (int k = 0; k < 3 && pc.IsGrounded; k++)
            {
                yield return HaFlick(PlayerController.FlickDirection.Up);
                float a = 0f;
                while (pc.IsGrounded && a < 0.4f) { yield return null; a += Time.deltaTime; }
            }
            yield return new WaitForSeconds(0.1f);
            g = 0f;
            while (pc.QaInputBusy && !pc.IsGrounded && g < 1f) { yield return null; g += Time.deltaTime; } // 上攻撃の最中なら終わるまで
        }
        int sw0 = PlayerAttackInfo.SwingCount, taps0 = PlayerController.TapAttacks;
        float step0 = pc.ScreenStepOffset, maxStep = 0f, firstSwingAt = -1f, t = 0f;
        int swingsIn015 = 0;
        var fates = new System.Text.StringBuilder();
        System.Action<int, PlayerController.FlickDirection, PlayerController.FlickFate> onFate = (id, d, f) => fates.Append($"{d}:{f} ");
        PlayerController.QaFlickFate += onFate;
        string state0 = $"react={pc.IsReacting} busy={pc.QaInputBusy} grounded={pc.IsGrounded}";
        if (flick) { pc.debugInjectOnce = PlayerController.FlickDirection.Forward; yield return null; } // 本物の1回のフリックと同じ入力
        else { PlayerController.DebugTapNow = true; yield return null; }
        bool airAtInput = !pc.IsGrounded;
        while (t < 0.6f)
        {
            int sw = PlayerAttackInfo.SwingCount - sw0;
            if (sw <= 1) maxStep = Mathf.Max(maxStep, pc.ScreenStepOffset - step0); // 入力した技(1回目の振り)の間だけ。後に続く補助の攻撃は含めない
            if (sw > 0 && firstSwingAt < 0f) firstSwingAt = t;
            if (firstSwingAt >= 0f && t - firstSwingAt <= 0.15f) swingsIn015 = sw;
            yield return null; t += Time.deltaTime;
        }
        PlayerController.QaFlickFate -= onFate;
        int swings = PlayerAttackInfo.SwingCount - sw0;
        string kind = flick ? "flick" : "tap";
        L($"[tap]   fates: {fates} | before: {state0} assistAttacks={PlayerController.AssistAttackCount}");
        L($"[tap] {ch} auto={(autoOn ? 1 : 0)} {(ground ? "ground" : "air")} {kind}: swings {swings} (first {firstSwingAt:F2}s, in0.15 {swingsIn015}) step +{maxStep:F2}m air={airAtInput} tapAttacks+{PlayerController.TapAttacks - taps0} reject='{PlayerController.LastTapReject}'");
        string tag = $"{ch} auto={(autoOn ? 1 : 0)} {(ground ? "ground" : "air")} {kind}";
        Check(swings >= 1, $"{tag}: attacks");
        if (!flick)
        {
            Check(maxStep < 0.12f, $"{tag}: no forward step (screen step +{maxStep:F2}m)");
            Check(swingsIn015 <= 1 || ch == "gunslinger" || ch == "dual_blade" || ch == "archer", $"{tag}: one tap = one attack, not doubled ({swingsIn015} swings in 0.15s)");
            if (!ground) Check(airAtInput, $"{tag}: the tap was made in the air");
        }
    }

    IEnumerator Mash(bool tap, System.Action<int> result)
    {
        float g = 0f;
        while ((!pc.IsGrounded || pc.QaInputBusy) && g < 3f) { yield return null; g += Time.deltaTime; }
        while (pc.IsReacting && g < 4f) { yield return null; g += Time.deltaTime; }
        int sw0 = PlayerAttackInfo.SwingCount;
        bool hurt = false;
        for (int i = 0; i < 30; i++)
        {
            if (tap) PlayerController.DebugTapNow = true; else pc.debugInjectOnce = PlayerController.FlickDirection.Forward;
            yield return new WaitForSeconds(0.04f);
            hurt |= pc.IsReacting;
        }
        yield return new WaitForSeconds(0.6f);
        result(hurt ? -1 : PlayerAttackInfo.SwingCount - sw0); // 途中で被弾した回は比べない(-1)
    }

    // 代表的な動画(-qaTapVideo <dir> [-tvChars ...] [-tvKmh 18]): 闘技場(平らな床)で、敵の前で
    //  タップ×3(その場で前攻撃)→ 右フリック(踏み込みあり)→ ↑ 打ち上げ → タップ×3(空中の追撃)→ ↑ 再上昇 → タップ×2 → ↓ 締め
    // を 30fps で毎フレーム書き出す(frames/f00000.png …、video.tsv に区間)。入力は画面の左に表示
    IEnumerator TapVideoMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f); // ゲームの起動(ホーム)が終わってから闘技場へ
        gm = GameManager.Instance;
        string[] chars = Arg("-tvChars", "swordsman,gunslinger,ninja,mage").Split(',');
        float kmh = float.Parse(Arg("-tvKmh", "18"), System.Globalization.CultureInfo.InvariantCulture);
        demoDir = System.IO.Path.Combine(outDir, "frames");
        System.IO.Directory.CreateDirectory(demoDir);
        var tsv = new System.Text.StringBuilder("char\tfrom\tto\n");
        string[] seq = { "T", "T", "T", "F", "U", "T", "T", "T", "U", "T", "T", "D" };
        foreach (var ch in chars)
        {
            ArenaConfigReset();
            var c = ArenaMode.Config;
            c.character = ch; c.speedMode = 1; c.kmh = kmh; c.assistMode = 0; c.invincible = true;
            c.enemies = new System.Collections.Generic.List<ArenaEnemyEntry> { new ArenaEnemyEntry { kind = ArenaEnemyKind.Enemy, enemyId = "goblin", tier = 0, count = 3, ahead = 6f, spacing = 6f } };
            yield return ArenaLaunch("tapvideo " + ch);
            if (!ArenaMode.BattleRunning) { L($"[tapvideo] {ch}: arena did not start, skipped"); continue; }
            yield return new WaitForSeconds(0.6f);
            int from = demoFrame;
            Time.captureFramerate = 30;
            aeOverlay = $"{ch}  タップ攻撃\n入力: ";
            var inputs = StartCoroutine(TapVideoInputs(seq));
            int n = Mathf.RoundToInt((0.5f + seq.Length * 0.32f + 1.2f) * 30f);
            for (int i = 0; i < n; i++)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(demoDir, $"f{demoFrame:00000}.png"));
                demoFrame++;
            }
            Time.captureFramerate = 0;
            StopCoroutine(inputs);
            aeOverlay = null;
            tsv.Append($"{ch}\t{from}\t{demoFrame - 1}\n");
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "video.tsv"), tsv.ToString());
            L($"[tapvideo] {ch} frames {from}-{demoFrame - 1}");
        }
        Check(demoFrame > 0, "tap video: frames written");
    }

    IEnumerator TapVideoInputs(string[] seq)
    {
        yield return new WaitForSeconds(0.5f);
        foreach (var k in seq)
        {
            aeOverlay += k == "T" ? "タップ " : k == "F" ? "→ " : k == "U" ? "↑ " : "↓ ";
            if (k == "T") PlayerController.DebugTapNow = true;
            else pc.debugInjectOnce = k == "F" ? PlayerController.FlickDirection.Forward : k == "U" ? PlayerController.FlickDirection.Up : PlayerController.FlickDirection.Down;
            yield return new WaitForSeconds(k == "U" ? 0.36f : 0.3f);
        }
    }
}
#endif
