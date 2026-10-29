#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// フリックの復旧の確認(2026-10-23、実機で上下左右のフリックが出なくなった不具合): -qaTouchFlick <dir>
//   [-tfChars swordsman,...] [-tfAuto 0,1] [-tfModes ground,air] [-tfSpeeds slow,fast]
// 技を直接呼ばず、画面の指と同じ経路(PlayerController.UpdatePointerInput の 指を置く → 動かす → 離す)を通す。
// 1つの操作ごとに、同じ向きを従来の入力(debugInjectOnce = 指の判定を通らない)で入れた時と比べる:
//   ・その向きの入力が届いた(QaFlickFate)、技の始まりの数、結果(すぐ出た/予約/無視)が同じ
//   ・フリックの指を離してもタップ攻撃が出ない / 上フリックの前にタップ攻撃が出ない
//   ・短く押して離した時だけタップ攻撃(前攻撃・踏み込みなし)が出る / 長押しでは出ない
public partial class QaSweep
{
    struct GestureResult
    {
        public List<string> fates; public int moves, taps, swings; public bool leftGround;
        public string groundAtFate; public int mageBefore, mageAfter; // 2026-10-29: 入力が届いたフレームの接地(G/A)と魔法使いの段
        public string Summary => $"[{string.Join(" ", fates)}] moves {moves} swings {swings} taps {taps}{(leftGround ? " jumped" : "")} at={groundAtFate}{(mageBefore >= 0 ? $" mage {mageBefore}->{mageAfter}" : "")}";
    }

    IEnumerator TouchFlickMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        gm = GameManager.Instance;
        string[] chars = Arg("-tfChars", "swordsman,dual_blade,noble_lady,gunslinger,dragon_lancer,archer,mage,fighter,ninja,miko,vampire,dragonkin,luka,pentatuttulu").Split(',');
        string[] autos = Arg("-tfAuto", "0,1").Split(',');
        string[] modes = Arg("-tfModes", "ground,air").Split(',');
        string[] speeds = Arg("-tfSpeeds", "slow,fast").Split(',');
        bool expKeep = GameManager.BlockExpGain; GameManager.BlockExpGain = true; // 2026-10-29: 途中のレベルアップの停止で指の操作が読まれなかった(試験側)
        L($"[touch] screen {Screen.width}x{Screen.height} slop {Mathf.Max(12f, Mathf.Min(Screen.width, Screen.height) * PlayerController.TapSlopFraction):F0}px");
        var tsv = new StringBuilder("char\tauto\tmode\tgesture\tspeed\ttouch\tinject\tok\n");
        int cases = 0, okCases = 0;
        foreach (var auto in autos)
        {
            bool autoOn = auto == "1";
            foreach (var ch in chars)
            {
                yield return BeginRun(ch, "wasteland_road");
                pc = PlayerController.Instance;
                SetGod(true);
                var tm = TerrainManager.Instance;
                if (tm != null) tm.enemySpawnChance = 0f;
                var hsa = HighSpeedAssist.Instance;
                if (hsa != null) { hsa.SetEnabled(autoOn); hsa.SetAutoAttack(autoOn); }
                yield return new WaitForSeconds(1.0f);
                foreach (var mode in modes)
                {
                    bool air = mode == "air";
                    foreach (var dir in new[] { PlayerController.FlickDirection.Up, PlayerController.FlickDirection.Forward, PlayerController.FlickDirection.Backward, PlayerController.FlickDirection.Down })
                        foreach (var sp in speeds)
                        {
                            GestureResult t = default, r = default;
                            yield return TfRun(air, () => TfSwipe(dir, sp == "fast"), x => t = x);
                            yield return TfRun(air, () => TfInject(dir), x => r = x);
                            string want = dir.ToString();
                            bool reached = t.fates.Exists(f => f.StartsWith(want + ":"));
                            bool sameFate = TfFirst(t, want) == TfFirst(r, want);
                            bool noTap = t.taps == 0;
                            bool moved = (t.moves > 0) == (r.moves > 0);
                            // 地上の下フリックは、届いたフレームに空中なら急降下・地上なら出ない作り。届くフレームは指の速さで1〜7フレーム違い、
                            // 地面の起伏/補助のジャンプで接地が変わる → 接地が違った時だけは「地形の違い」として別に数える(届いた・結果は同じ)
                            if (!moved && t.groundAtFate != r.groundAtFate) { moved = true; tfTerrainOnly++; L($"[touch] {ch} auto={auto} {mode} {dir} {sp}: moves differ only because grounded differed at the input frame (touch {t.groundAtFate} / inject {r.groundAtFate})"); }
                            bool ok = reached && sameFate && noTap && moved;
                            cases++; if (ok) okCases++;
                            string tag = $"{ch} auto={auto} {mode} {dir} {sp}";
                            L($"[touch] {tag}: touch {t.Summary} | inject {r.Summary}{(ok ? "" : "  <-- NG")}");
                            tsv.Append($"{ch}\t{auto}\t{mode}\t{dir}\t{sp}\t{t.Summary}\t{r.Summary}\t{(ok ? 1 : 0)}\n");
                            Check(reached, $"{tag}: the swipe reaches the game as a {dir} flick");
                            Check(sameFate, $"{tag}: the swipe does the same as the direct input ({TfFirst(t, want)} vs {TfFirst(r, want)})");
                            Check(noTap, $"{tag}: releasing a flick does not add a tap attack");
                            Check(moved, $"{tag}: a move starts like the direct input ({t.moves} vs {r.moves})");
                            if (dir == PlayerController.FlickDirection.Up) Check(!t.fates.Exists(f => f.StartsWith("Forward:")) || r.fates.Exists(f => f.StartsWith("Forward:")), $"{tag}: no forward attack before the up flick");
                        }
                    // タップ: 短く押して離す → タップ攻撃 1 回(前攻撃)。長押し → 出ない
                    GestureResult tp = default, lp = default;
                    yield return TfRun(air, () => TfHold(0.05f), x => tp = x);
                    yield return TfRun(air, () => TfHold(0.45f), x => lp = x);
                    string tg = $"{ch} auto={auto} {mode}";
                    L($"[touch] {tg} tap: {tp.Summary} | long press: {lp.Summary} | reject '{PlayerController.LastTapReject}'");
                    tsv.Append($"{ch}\t{auto}\t{mode}\ttap\t-\t{tp.Summary}\t-\t{(tp.taps == 1 ? 1 : 0)}\n");
                    tsv.Append($"{ch}\t{auto}\t{mode}\tlongpress\t-\t{lp.Summary}\t-\t{(lp.taps == 0 ? 1 : 0)}\n");
                    Check(tp.taps == 1, $"{tg}: a short tap gives one tap attack ({tp.taps})");
                    Check(tp.fates.Exists(f => f.StartsWith("Forward:")), $"{tg}: the tap attack is a forward attack");
                    Check(lp.taps == 0, $"{tg}: a long press gives no tap attack ({lp.taps})");
                    cases += 2; if (tp.taps == 1) okCases++; if (lp.taps == 0) okCases++;
                }
                PlayerController.DebugPointer.active = false;
                if (tm != null) tm.enemySpawnChance = 1f;
                SetGod(false);
                yield return EndRun();
                System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "touch_flick.tsv"), tsv.ToString());
            }
        }
        if (HighSpeedAssist.Instance != null) { HighSpeedAssist.Instance.SetEnabled(true); HighSpeedAssist.Instance.SetAutoAttack(true); }
        GameManager.BlockExpGain = expKeep;
        L($"[touch] cases ok {okCases}/{cases} (terrain-only differences {tfTerrainOnly})");
    }

    static string TfFirst(GestureResult g, string dir)
    {
        var f = g.fates.Find(x => x.StartsWith(dir + ":"));
        return f == null ? "none" : f.Substring(dir.Length + 1);
    }

    // 地上/空中の状態にしてから操作を1回、その後 0.7 秒の結果を集める
    int tfTerrainOnly;
    IEnumerator TfRun(bool air, System.Func<IEnumerator> gesture, System.Action<GestureResult> done)
    {
        float g = 0f;
        while (Time.timeScale <= 0f && !TimeControl.PausedOnlyByHitStop && g < 10f) { yield return null; g += Time.unscaledDeltaTime; } // 停止の間は始めない
        pc.DebugSetMageLevel(0); // 2026-10-29: 魔法使いの段を毎回そろえる(前の操作の段が残り、上限/下限で結果が変わっていた)
        g = 0f;
        while ((!pc.IsGrounded || pc.QaInputBusy || pc.IsReacting || pc.IsAttacking) && g < 4f) { yield return null; g += Time.deltaTime; }
        yield return new WaitForSeconds(0.35f);
        if (air)
        {
            for (int k = 0; k < 3 && pc.IsGrounded; k++)
            {
                pc.debugInjectOnce = PlayerController.FlickDirection.Up; // 空中にする準備は従来の入力で(確かめる操作とは別)
                float a = 0f; while (pc.IsGrounded && a < 0.4f) { yield return null; a += Time.deltaTime; }
            }
            g = 0f; while (pc.QaInputBusy && !pc.IsGrounded && g < 1f) { yield return null; g += Time.deltaTime; }
            yield return new WaitForSeconds(0.05f);
        }
        var res = new GestureResult { fates = new List<string>() };
        System.Action<int, PlayerController.FlickDirection, PlayerController.FlickFate> on = (id, d, f) => { res.fates.Add($"{d}:{f}"); if (res.groundAtFate == null) res.groundAtFate = pc.IsGrounded ? "G" : "A"; };
        res.mageBefore = pc.Kit == CharacterKit.Mage ? pc.MageAltitudeLevel : -1;
        PlayerController.QaFlickFate += on;
        int m0 = pc.DebugMoveStarts, t0 = PlayerController.TapAttacks, s0 = PlayerAttackInfo.SwingCount;
        bool wasGround = pc.IsGrounded, left = false;
        tfSampling = true;
        var sampler = StartCoroutine(TfSample()); // 技の始まりは毎フレーム読まないと数え落とす(指を動かしている間も)
        yield return gesture();
        for (float t = 0f; t < 0.7f; t += Time.deltaTime) { if (wasGround && !pc.IsGrounded) left = true; yield return null; }
        tfSampling = false; StopCoroutine(sampler);
        PlayerController.QaFlickFate -= on;
        res.mageAfter = pc.Kit == CharacterKit.Mage ? pc.MageAltitudeLevel : -1;
        res.moves = pc.DebugMoveStarts - m0; res.taps = PlayerController.TapAttacks - t0; res.swings = PlayerAttackInfo.SwingCount - s0; res.leftGround = left;
        done(res);
    }

    bool tfSampling;
    IEnumerator TfSample() { while (tfSampling) { int _ = pc.DebugMoveStarts; yield return null; } }

    Vector2 TfStart => new Vector2(Screen.width * 0.5f, Screen.height * 0.45f);

    // 指を置く → 1フレームごとに動かす(slow 9px / fast 38px)→ 90px 動いたら離す
    IEnumerator TfSwipe(PlayerController.FlickDirection dir, bool fast)
    {
        Vector2 v = dir == PlayerController.FlickDirection.Up ? Vector2.up : dir == PlayerController.FlickDirection.Down ? Vector2.down
            : dir == PlayerController.FlickDirection.Forward ? Vector2.right : Vector2.left;
        // 向きは画面の向き(右 = 前、ClassifyFlickDirection)。キャラが一瞬振り向いていても変えない
        var p = PlayerController.DebugPointer.pos = TfStart;
        PlayerController.DebugPointer.prevDown = false; // 前の操作の離した指が読まれずに残っていても、置いた瞬間として始める
        PlayerController.DebugPointer.active = true; PlayerController.DebugPointer.down = true;
        yield return null;
        float step = fast ? 38f : 9f;
        for (float d = 0f; d < 90f; d += step) { p += v * step; PlayerController.DebugPointer.pos = p; yield return null; }
        PlayerController.DebugPointer.down = false;
        yield return null;
        yield return null;
        PlayerController.DebugPointer.active = false;
    }

    // 動かさずに sec 秒押して離す
    IEnumerator TfHold(float sec)
    {
        PlayerController.DebugPointer.pos = TfStart;
        PlayerController.DebugPointer.prevDown = false;
        PlayerController.DebugPointer.active = true; PlayerController.DebugPointer.down = true;
        yield return null;
        float t = 0f; while (t < sec) { yield return null; t += Time.unscaledDeltaTime; }
        PlayerController.DebugPointer.down = false;
        yield return null;
        yield return null;
        PlayerController.DebugPointer.active = false;
    }

    IEnumerator TfInject(PlayerController.FlickDirection dir)
    {
        pc.debugInjectOnce = dir;
        yield return null;
        yield return null;
    }
}
#endif
