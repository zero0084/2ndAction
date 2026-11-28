#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 2026-11-28 宣伝動画「スマホでの遊び方」(縦 9:16、マスター依頼)の撮影。 -storeCapture <dir> -scShots pf_launch,pf_combo,pf_land
//  指の操作は本物のタッチと同じ入口(PlayerController.DebugPointer: 置く → 動かす → 離す)で入れる(技を直接呼ばない・自動攻撃/自動回避は切る)。
//  毎フレームの指の位置と押しているか(画面に対する割合、左下が 0,0)を raw/<shot>_touch.tsv に、フリックの結果(出た技)を raw/<shot>_flicks.tsv に残し、
//  編集(Tools/promo_flick/edit.py)はこの記録どおりに白い丸と矢印を重ねる(推測で足さない)。
//  縦の3つの見え方は -pview side|fold|oblique で窓ごとに決め、pf_launch を同じ場所・同じ手順で撮る(見え方だけが違う)
public partial class QaSweep
{
    readonly StringBuilder pfTouch = new StringBuilder();
    readonly StringBuilder pfFlicks = new StringBuilder();
    string PfCharacter => Arg("-pfChar", "dual_blade");

    void PfBeginLog() { pfTouch.Clear(); pfTouch.Append("frame\tdown\tx\ty\n"); pfFlicks.Clear(); pfFlicks.Append("frame\tdir\tresult\tgrounded\n"); }
    void PfSaveLog(string name)
    {
        string raw = System.IO.Path.Combine(outDir, "raw");
        System.IO.File.WriteAllText(System.IO.Path.Combine(raw, name + "_touch.tsv"), pfTouch.ToString());
        System.IO.File.WriteAllText(System.IO.Path.Combine(raw, name + "_flicks.tsv"), pfFlicks.ToString());
        PlayerController.DebugPointer.active = false; PlayerController.DebugPointer.down = false;
    }

    // 指の置き場所: キャラ/敵/HP と重ならない画面の空いた所(縦 = 道の上の空、横 = 右寄りの空)
    Vector2 PfStart(PlayerController.FlickDirection f)
    {
        bool portrait = Screen.height > Screen.width;
        float x = portrait ? 0.56f : 0.66f, y = portrait ? 0.50f : 0.72f; // 横: 道より上の空(0.56 では斬撃/敵に重なった)
        if (f == PlayerController.FlickDirection.Up) y -= 0.05f;
        if (f == PlayerController.FlickDirection.Down) y += 0.05f;
        if (f == PlayerController.FlickDirection.Backward) x += 0.06f;
        return new Vector2(Screen.width * x, Screen.height * y);
    }

    // 1回のフリック: 置く(1コマ)→ 4コマで画面の高さの 11% 動かす → 離す。各コマの指を記録
    IEnumerator PfFlick(PlayerController.FlickDirection f)
    {
        Vector2 dir = f == PlayerController.FlickDirection.Up ? Vector2.up : f == PlayerController.FlickDirection.Down ? Vector2.down : f == PlayerController.FlickDirection.Forward ? Vector2.right : Vector2.left;
        float len = Screen.height * 0.11f;
        Vector2 s = PfStart(f);
        int frame0 = scFrameNow;
        bool g0 = pc.IsGrounded;
        PlayerController.DebugPointer.active = true; PlayerController.DebugPointer.mouse = false;
        for (int i = 0; i <= 5; i++)
        {
            bool down = i <= 4;
            Vector2 p = s + dir * len * Mathf.Clamp01(i / 4f);
            PlayerController.DebugPointer.down = down;
            PlayerController.DebugPointer.pos = p;
            pfTouch.Append($"{scFrameNow}\t{(down ? 1 : 0)}\t{p.x / Screen.width:F4}\t{p.y / Screen.height:F4}\n");
            yield return null;
        }
        StartCoroutine(PfCheck(f, frame0, g0)); // 出た技の確認は並行で(操作の間隔を延ばさない)
    }

    // 出た技(このフリックの後 0.2 秒の間に攻撃が始まったか/跳んだか)。記録だけ(操作には関わらない)
    IEnumerator PfCheck(PlayerController.FlickDirection f, int frame0, bool g0)
    {
        bool attacked = false, jumped = false; float t = 0f;
        while (t < 0.2f) { attacked |= pc.IsAttacking; jumped |= !pc.IsGrounded && g0; t += Time.deltaTime; yield return null; }
        string res = attacked ? "attack" : jumped ? "jump" : "none";
        pfFlicks.Append($"{frame0}\t{f}\t{res}\t{g0}\n");
        L($"[pf] flick {f} frame {frame0} -> {res} (grounded before {g0}, airborne now {!pc.IsGrounded})");
    }

    IEnumerator PfSetup(float distance, bool assistJumps)
    {
        yield return ScBegin(PfCharacter, "wasteland_road", distance, 5, true);
        GameManager.BlockExpGain = true; // 撮影の途中でレベルアップの3択が出ないように
        var hsa = HighSpeedAssist.Instance;
        if (hsa != null) { hsa.SetEnabled(assistJumps); hsa.SetAutoAttack(false); } // 自動攻撃は切る(攻撃は全部手のフリック)
    }

    // 打ち上げ → 空中で前フリック → 下フリックで急降下(縦の冒頭/縦の3つの見え方で同じ手順)
    IEnumerator PfLaunchRoutine(int airHits)
    {
        float d = Mathf.Clamp(pc.CurrentAutoRunSpeed * 0.9f, 7f, 14f);
        var en = HaSpawn(d, "goblin_elite");
        var en2 = HaSpawn(d + 3.2f, "goblin");
        foreach (var e in new[] { en, en2 }) if (e != null) { AeHp.SetValue(e, -1); HaEnsureHp.Invoke(e, null); }
        yield return TutWaitAhead(en, AeUpReach() + 1.2f, 4f);
        // 間隔は操作説明ショート(gp_tutorial)と同じ: 上 → 0.32秒 → 前 0.27秒ごと → 0.3秒 → 下(指の動き 6コマ = 0.1秒を含む)
        yield return PfFlick(PlayerController.FlickDirection.Up);
        yield return ScWait(0.22f);
        for (int i = 0; i < airHits; i++) { yield return PfFlick(PlayerController.FlickDirection.Forward); yield return ScWait(0.17f); }
        yield return ScWait(0.2f);
        yield return PfFlick(PlayerController.FlickDirection.Down);
    }

    IEnumerator PfLaunchShot(string name)
    {
        yield return PfSetup(1150f, true);
        var tm = TerrainManager.Instance;
        if (tm != null) tm.enemySpawnChance = 0f;
        float zone = ScFlatStage(150f, 500f); // 2km の関門(ボス)の手前で撮る
        yield return ScWaitReach(zone + 10f);
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
        yield return HaWaitIdle();
        PfBeginLog();
        ScRecStart(name);
        yield return ScWait(1.0f);
        yield return PfLaunchRoutine(4);
        yield return ScWait(1.0f);
        // 地上の敵へ前フリック(1発で倒れる)
        var e1 = HaSpawn(Mathf.Clamp(pc.CurrentAutoRunSpeed * 0.9f, 7f, 14f), "goblin");
        if (e1 != null) AeHp.SetValue(e1, 1);
        yield return TutWaitAhead(e1, 2.2f, 4f);
        yield return PfFlick(PlayerController.FlickDirection.Forward);
        yield return ScWait(1.4f);
        ScStill("launch_end");
        yield return ScRecStop();
        PfSaveLog(name);
        GameManager.BlockExpGain = false;
        if (tm != null) tm.enemySpawnChance = 1f;
        yield return ScEnd();
    }

    // 長いコンボ: 前の敵 → 打ち上げ → 空中で前フリックを続ける → 急降下 → 着地して残りを前/後ろフリック
    IEnumerator PfComboShot(string name)
    {
        yield return PfSetup(1150f, true);
        var tm = TerrainManager.Instance;
        if (tm != null) tm.enemySpawnChance = 0f;
        float zone = ScFlatStage(150f, 600f);
        yield return ScWaitReach(zone + 10f);
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
        yield return HaWaitIdle();
        PfBeginLog();
        ScRecStart(name);
        yield return ScWait(0.8f);
        var e0 = HaSpawn(Mathf.Clamp(pc.CurrentAutoRunSpeed * 0.9f, 7f, 14f), "goblin");
        if (e0 != null) AeHp.SetValue(e0, 1);
        yield return TutWaitAhead(e0, 2.2f, 4f);
        yield return PfFlick(PlayerController.FlickDirection.Forward);
        yield return ScWait(0.5f);
        yield return PfLaunchRoutine(4); // 空中の前フリックは 4回(5回目からは着地していた)
        yield return ScWait(0.35f);
        var e2 = HaSpawn(-1.6f, "goblin");
        if (e2 != null) AeHp.SetValue(e2, 1);
        yield return ScWait(0.12f);
        yield return PfFlick(PlayerController.FlickDirection.Backward);
        yield return ScWait(0.5f);
        yield return PfLaunchRoutine(4);
        yield return ScWait(0.5f);
        for (int k = 0; k < 2; k++) // 地上の敵を前フリックで続けて
        {
            var g = HaSpawn(Mathf.Clamp(pc.CurrentAutoRunSpeed * 0.9f, 7f, 14f), "goblin");
            if (g != null) AeHp.SetValue(g, 1);
            yield return TutWaitAhead(g, 2.2f, 4f);
            yield return PfFlick(PlayerController.FlickDirection.Forward);
            yield return ScWait(0.3f);
        }
        // 締め: もう一度 打ち上げ → 空中で前 → 急降下(気持ちよく決める所)
        yield return PfLaunchRoutine(4);
        yield return ScWait(4.0f); // 締めの後、ロゴへ移る間も映像が続く(止め絵にしない)
        ScStill("combo_end");
        yield return ScRecStop();
        PfSaveLog(name);
        GameManager.BlockExpGain = false;
        if (tm != null) tm.enemySpawnChance = 1f;
        yield return ScEnd();
    }

    // 横画面: 速い区間を走りながら、近づく敵へフリック(穴を越える補助のジャンプだけ使う。攻撃は全部手のフリック)
    IEnumerator PfLandShot(string name)
    {
        yield return PfSetup(6150f, true);
        var tm = TerrainManager.Instance;
        if (tm != null) tm.enemySpawnChance = 0f;
        float zone = ScFlatStage(300f, 900f);
        yield return ScWaitReach(zone + 10f);
        yield return HaWaitIdle();
        PfBeginLog();
        ScRecStart(name);
        yield return ScWait(0.6f);
        float lead = Mathf.Clamp(pc.CurrentAutoRunSpeed * 0.9f, 10f, 24f);
        for (int k = 0; k < 3; k++)
        {
            var e = HaSpawn(lead, k == 1 ? "goblin_elite" : "goblin");
            if (e != null) AeHp.SetValue(e, 1);
            yield return TutWaitAhead(e, 2.4f, 4f);
            yield return PfFlick(PlayerController.FlickDirection.Forward);
            yield return ScWait(0.5f);
        }
        yield return PfLaunchRoutine(4);
        yield return ScWait(1.2f);
        ScStill("land_end");
        yield return ScRecStop();
        PfSaveLog(name);
        GameManager.BlockExpGain = false;
        if (tm != null) tm.enemySpawnChance = 1f;
        yield return ScEnd();
    }
}
#endif
