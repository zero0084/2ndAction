#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

// CARD BALANCE TEST(2026-10-01)の確認。 -qaCardTest <dir> [-qaShots 1]
//  値が累積しないこと(A→B→C→RESET→Aの繰り返し)、テスト中に取ったカードとの掛け合わせ、TEST OFF、
//  実際の動き(ジャンプの高さ/回数、攻撃の時間、判定の大きさ、走る速さ)、カードLv、12キャラの切替、
//  新しいランで値が残らないこと、DEBUG OFFで外れること、パネル上のタッチを操作にしないこと。
public partial class QaSweep
{
    CardBalanceTest ct;

    IEnumerator CardTestMode()
    {
        Application.targetFrameRate = 60;
        bool shots = Arg("-qaShots", "0") == "1";
        yield return BeginRun("swordsman", "wasteland_road");
        TradeQuiet();
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        stopKeepAlive = true; // HPを書き換えない(最大HPの確認のため。無敵で死なない)
        SetDebugMode(true);
        ct = CardBalanceTest.Instance;
        Check(ct != null, "CardBalanceTest exists in a development build");
        if (ct == null) yield break;
        yield return new WaitForSeconds(0.5f);
        var def = CardBalanceTest.CurrentDef();
        float baseSpeed = pc.CardTestBaseRunSpeed * def.groundMobilityMultiplier;
        float baseJump = pc.CardTestBaseJumpForce * def.jumpForceMultiplier;
        L($"[base] {def.characterId} runSpeed={pc.runSpeed:F3} (expect {baseSpeed:F3}) jumpForce={pc.jumpForce:F3} jumps={pc.maxJumps} atk={pc.AttackPower} time={pc.AttackSpeedMultiplier:F2} range={pc.AttackRangeMultiplier:F2} maxHp={gm.maxLives} exp={gm.CardTestExpMultiplier:F2} mile={gm.MileGainMultiplier:F2}");
        Check(Near(pc.runSpeed, baseSpeed) && Near(pc.jumpForce, baseJump), "run starts at the character's base values");

        // ---- 1) 累積しない: A→B→C→RESET→A を5周
        string[] keys = { "speed", "jump", "jumps", "attack", "atktime", "range", "hp", "exp", "mile" };
        for (int cycle = 0; cycle < 5; cycle++)
        {
            foreach (var k in keys) { ct.PressSlot(k, 0); ct.PressSlot(k, 1); ct.PressSlot(k, 2); }
            yield return null;
            ct.ResetToBase();
            yield return null;
            foreach (var k in keys) ct.PressSlot(k, 0);
            yield return null;
        }
        var pS = ct.Get("speed"); var pJ = ct.Get("jump"); var pN = ct.Get("jumps"); var pA = ct.Get("attack");
        var pT = ct.Get("atktime"); var pR = ct.Get("range"); var pH = ct.Get("hp"); var pE = ct.Get("exp"); var pM = ct.Get("mile");
        L($"[cycle x5 then A] runSpeed={pc.runSpeed:F4} expect {baseSpeed * pS.abc[0]:F4} | jump={pc.jumpForce:F4} expect {baseJump * pJ.abc[0]:F4} | jumps={pc.maxJumps} expect {def.jumpCount + pN.abc[0]} | atk={pc.AttackPower} expect {def.attackPower + pA.abc[0]} | time={pc.AttackSpeedMultiplier:F4} expect {def.attackSpeedMultiplier * pT.abc[0]:F4} | range={pc.AttackRangeMultiplier:F4} expect {def.attackRangeMultiplier * pR.abc[0]:F4} | hp={gm.maxLives} expect {Mathf.Min(gm.maxLivesCap, def.baseMaxLives + (int)pH.abc[0])} | exp={gm.CardTestExpMultiplier:F4} mile={gm.MileGainMultiplier:F4}/{gm.BossMileGainMultiplier:F4}");
        Check(Near(pc.runSpeed, baseSpeed * pS.abc[0]), "speed A after 5 cycles of A/B/C/RESET is exactly base x A (no accumulation)");
        Check(Near(pc.jumpForce, baseJump * pJ.abc[0]), "jump power A is exactly base x A");
        Check(pc.maxJumps == def.jumpCount + (int)pN.abc[0], "jump count A is base + A");
        Check(pc.AttackPower == def.attackPower + (int)pA.abc[0], "attack A is base + A");
        Check(Near(pc.AttackSpeedMultiplier, def.attackSpeedMultiplier * pT.abc[0]), "attack time A is base x A");
        Check(Near(pc.AttackRangeMultiplier, def.attackRangeMultiplier * pR.abc[0]), "attack range A is base x A");
        Check(gm.maxLives == Mathf.Min(gm.maxLivesCap, def.baseMaxLives + (int)pH.abc[0]) && gm.Lives == gm.maxLives, "max HP A is base + A (healed)");
        Check(Near(gm.CardTestExpMultiplier, pE.abc[0]) && Near(gm.MileGainMultiplier, pM.abc[0]) && Near(gm.BossMileGainMultiplier, pM.abc[0]), "EXP/MILE A");
        ct.ResetToBase();
        yield return null;
        Check(Near(pc.runSpeed, baseSpeed) && pc.maxJumps == def.jumpCount && pc.AttackPower == def.attackPower && Near(pc.AttackRangeMultiplier, def.attackRangeMultiplier) && gm.maxLives == def.baseMaxLives && Near(gm.CardTestExpMultiplier, 1f), "RESET returns every value to the character base");

        // ---- 2) テスト中に取ったカード / TEST OFF
        var speedUp = CardDatabase.FindById("speed_up");
        var rangeUp = CardDatabase.FindById("attack_range_up");
        ApplyCard(speedUp); ApplyCard(rangeUp);
        yield return null;
        float cardSpeed = pc.runSpeed, cardRange = pc.AttackRangeMultiplier;
        ct.PressSlot("speed", 1); ct.PressSlot("range", 1);
        yield return null;
        Check(Near(pc.runSpeed, cardSpeed * pS.abc[1]), $"test B multiplies on top of the card (run {pc.runSpeed:F3} = {cardSpeed:F3} x {pS.abc[1]})");
        ApplyCard(speedUp); // テスト中にもう1枚
        yield return null;
        ct.PressSlot("speed", 2);
        yield return null;
        float v3step = (1f + CardRules.SoftSpeed(0.06f)) / (1f + CardRules.SoftSpeed(0.03f)); // カード v3: SPEED UP は +3%/Lv の足し算
        Check(Near(pc.runSpeed, cardSpeed * v3step * pS.abc[2]), $"a card picked during the test stays (run {pc.runSpeed:F3} = {cardSpeed:F3} x {v3step:F3} x {pS.abc[2]})");
        ct.TestOff();
        yield return null;
        Check(Near(pc.runSpeed, cardSpeed * v3step) && Near(pc.AttackRangeMultiplier, cardRange, 0.02f), $"TEST OFF keeps the cards and removes only the test (run {pc.runSpeed:F3}, range {pc.AttackRangeMultiplier:F3} vs {cardRange:F3})");
        L($"[info] {ct.Info(pS)}");
        ct.ResetToBase();
        yield return null;
        Check(Near(pc.runSpeed, baseSpeed), "RESET also removes the cards' effect");

        // ---- 3) 実際の動き
        yield return MeasureJump("base");
        float h0 = lastApex;
        ct.PressSlot("jump", 2);
        yield return MeasureJump($"jump C x{pJ.abc[2]}");
        float h1 = lastApex;
        float expectRatio = pJ.abc[2] * pJ.abc[2];
        L($"[jump] apex {h0:F2}m -> {h1:F2}m (ratio {h1 / Mathf.Max(0.01f, h0):F2}, expect ~{expectRatio:F2})");
        Check(h1 / Mathf.Max(0.01f, h0) > expectRatio * 0.85f && h1 / Mathf.Max(0.01f, h0) < expectRatio * 1.15f, "jump power changes the real jump height (height ~ force^2)");
        ct.TurnOff("jump");

        ct.SetValue("jumps", 2);
        yield return WaitReady(30f);
        int used = 0;
        for (int i = 0; i < 6; i++) { yield return Flick(PlayerController.FlickDirection.Up); yield return new WaitForSeconds(0.12f); used = Mathf.Max(used, (int)GetPrivate(pc, "jumpsUsed")); }
        L($"[jumps] maxJumps={pc.maxJumps} jumps actually used in the air={used}");
        Check(used == pc.maxJumps, $"jump count +2 gives {pc.maxJumps} real jumps");
        ct.TurnOff("jumps");
        yield return WaitReady(30f);

        float t0 = 0f, t1 = 0f;
        yield return MeasureAttack(); t0 = lastAttack;
        ct.PressSlot("atktime", 2);
        yield return MeasureAttack(); t1 = lastAttack;
        L($"[attack time] {t0:F3}s -> {t1:F3}s (ratio {t1 / Mathf.Max(0.001f, t0):F2}, expect {pT.abc[2]:F2})");
        Check(Mathf.Abs(t1 / Mathf.Max(0.001f, t0) - pT.abc[2]) < 0.12f, "attack time changes the real attack duration");
        ct.TurnOff("atktime");

        float w0 = 0f, w1 = 0f;
        yield return MeasureHitbox(); w0 = lastWidth;
        ct.PressSlot("range", 2);
        yield return MeasureHitbox(); w1 = lastWidth;
        L($"[range] hitbox scale {w0:F2} -> {w1:F2} (ratio {w1 / Mathf.Max(0.01f, w0):F2}, expect ~{pR.abc[2]:F2})");
        Check(Mathf.Abs(w1 / Mathf.Max(0.01f, w0) - pR.abc[2]) < 0.15f, "attack range changes the real hitbox");
        ct.TurnOff("range");

        SetKmh(100f); yield return new WaitForSeconds(0.3f);
        float v0 = pc.CurrentAutoRunSpeed;
        ct.PressSlot("speed", 1);
        yield return new WaitForSeconds(0.2f);
        float v1 = pc.CurrentAutoRunSpeed;
        L($"[speed] actual {GameManager.SpeedKmh(v0):F0} -> {GameManager.SpeedKmh(v1):F0} km/h | {ct.Info(pS)}");
        Check(Near(v1 / v0, pS.abc[1], 0.02f), "speed B changes the real running speed");
        if (shots) { ct.Open(true); ct.SetValue("jump", 1.2f); yield return new WaitForSeconds(0.3f); Shot("cardtest_control"); yield return null; yield return null; }
        PlayerController.DebugSpeedScale = 1f;
        ct.ResetToBase();

        ct.SetValue("shield", 2);
        Check(pc.ShieldCharges == 2, "shield is set to 2");

        // ---- 4) カードLv
        var ntb = CardDatabase.FindById("no_turning_back");
        ct.ApplyCardLevel(ntb, 9);
        yield return null;
        float ntbExpect = 1f + CardRules.SoftSpeed(0.45f); // カード v3: +5%/Lv の足し算を曲線に通す
        L($"[card] NO TURNING BACK Lv9: run {pc.runSpeed / baseSpeed:F3}x (expect {ntbExpect:F3}) note");
        Check(Near(pc.runSpeed / baseSpeed, ntbExpect, 0.01f), "card Lv9 applies the card's real Lv9 value");
        var da = CardDatabase.FindById("double_attack");
        ct.ApplyCardLevel(da, 9);
        yield return null;
        // カード v3: DOUBLE ATTACK は攻撃速度ではなく「3%/Lv の確率で追加の1撃」
        Check(Near(pc.AttackSpeedMultiplier, def.attackSpeedMultiplier) && Near(GameManager.Instance.Card.Get(EffectType.DoubleAttackChance), 0.27f), $"DOUBLE ATTACK Lv9 = 27% extra attack chance, attack time unchanged (got x{pc.AttackSpeedMultiplier:F3}, chance {GameManager.Instance.Card.Get(EffectType.DoubleAttackChance):F2})");
        var jcu = CardDatabase.FindById("jump_count_up");
        ct.ApplyCardLevel(jcu, 5);
        yield return null;
        Check(pc.maxJumps == def.jumpCount + 5 && Near(pc.runSpeed, baseSpeed), "JUMP COUNT UP Lv5 = base + 5 jumps, other values back to base");
        if (shots) { ct.Open(true); SetPrivate(ct, "tab", CardBalanceTest.Tab.CardChar); yield return new WaitForSeconds(0.3f); Shot("cardtest_cardchar"); yield return null; yield return null; SetPrivate(ct, "tab", CardBalanceTest.Tab.Status); yield return new WaitForSeconds(0.3f); Shot("cardtest_status"); yield return null; yield return null; SetPrivate(ct, "tab", CardBalanceTest.Tab.Control); }
        ct.ResetToBase();

        // ---- 5) 12キャラ切替(同じテスト値のまま)
        ct.PressSlot("speed", 1); ct.PressSlot("jump", 1); ct.PressSlot("atktime", 1); ct.PressSlot("range", 1); ct.PressSlot("jumps", 0);
        foreach (var cd in CharacterDatabase.AllCharacters)
        {
            bool ok = ct.SwitchCharacter(cd.characterId);
            yield return null;
            float bs = pc.CardTestBaseRunSpeed * cd.groundMobilityMultiplier;
            bool vals = Near(pc.runSpeed, bs * pS.abc[1]) && Near(pc.jumpForce, pc.CardTestBaseJumpForce * cd.jumpForceMultiplier * pJ.abc[1])
                && Near(pc.AttackSpeedMultiplier, cd.attackSpeedMultiplier * pT.abc[1]) && Near(pc.AttackRangeMultiplier, cd.attackRangeMultiplier * pR.abc[1])
                && pc.maxJumps == cd.jumpCount + (int)pN.abc[0] && gm.ActiveRunCharacterId == cd.characterId;
            yield return WaitReady(30f, 2f);
            yield return Flick(PlayerController.FlickDirection.Forward);
            yield return new WaitForSeconds(0.5f);
            yield return Flick(PlayerController.FlickDirection.Up);
            yield return new WaitForSeconds(0.6f);
            yield return Flick(PlayerController.FlickDirection.Backward);
            yield return new WaitForSeconds(0.5f);
            L($"[char] {cd.characterId,-14} switched={ok} values={(vals ? "ok" : "NG")} run={pc.runSpeed:F2} jumpF={pc.jumpForce:F2} time={pc.AttackSpeedMultiplier:F2} range={pc.AttackRangeMultiplier:F2} jumps={pc.maxJumps} animSet={pc.GetComponent<PlayerAnimator>() != null}");
            Check(ok && vals, $"{cd.characterId}: switch keeps the test values on the new character's base");
            if (shots && cd.characterId == "dragonkin") { ct.Open(true); yield return new WaitForSeconds(0.2f); Shot("cardtest_dragonkin"); yield return null; yield return null; ct.Open(false); }
        }
        ct.SwitchCharacter("swordsman");
        yield return null;

        // ---- 6) パネル上のタッチ
        ct.Open(true);
        yield return null; yield return null;
        var pr = (Rect)typeof(CardBalanceTest).GetField("panelRect", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ct);
        float sc = Mathf.Clamp(Screen.height / 820f, 0.8f, 2.2f);
        if (pr.width > 0f)
        {
            Vector2 inside = new Vector2(pr.center.x * sc, Screen.height - pr.center.y * sc);
            Vector2 outside = new Vector2(20f, Screen.height * 0.5f);
            Check(CardBalanceTest.BlocksPointer(inside) && !CardBalanceTest.BlocksPointer(outside), "a touch that starts on the panel is not used as a flick; outside it is");
        }
        else Warn("panel rect not measured (OnGUI did not run in this mode)");
        ct.Open(false);

        // ---- 7) DEBUG OFF で外れる
        ct.PressSlot("speed", 2);
        yield return null;
        SetDebugMode(false);
        yield return null; yield return null;
        Check(Near(pc.runSpeed, pc.CardTestBaseRunSpeed * CardBalanceTest.CurrentDef().groundMobilityMultiplier) && !ct.AnyActive, "turning DEBUG off removes the test values");
        SetDebugMode(true);

        // ---- 8) 新しいランへ持ち越さない
        ct.PressSlot("speed", 2); ct.PressSlot("jumps", 2); ct.PressSlot("attack", 2);
        yield return null;
        yield return EndRun();
        yield return BeginRun("fighter", "wasteland_road");
        TradeQuiet();
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        yield return new WaitForSeconds(0.3f);
        var fd = CardBalanceTest.CurrentDef();
        Check(!ct.AnyActive && Near(pc.runSpeed, pc.CardTestBaseRunSpeed * fd.groundMobilityMultiplier) && pc.maxJumps == fd.jumpCount && pc.AttackPower == fd.attackPower, "a new run starts with no test values");
        L($"[new run] {fd.characterId} active={ct.AnyActive} run={pc.runSpeed:F3} jumps={pc.maxJumps} atk={pc.AttackPower} applications={CardBalanceTest.Applications}");
        yield return EndRun();
    }

    static bool Near(float a, float b, float rel = 0.001f) => Mathf.Abs(a - b) <= Mathf.Max(1e-4f, Mathf.Abs(b) * rel);

    void SetDebugMode(bool on)
    {
        var p = typeof(GameManager).GetProperty("DebugMode");
        p.SetValue(gm, on);
    }

    void ApplyCard(CardDefinition c)
    {
        typeof(GameManager).GetMethod("ApplyCardEffects", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, new object[] { c });
    }

    float lastApex, lastAttack, lastWidth;

    IEnumerator MeasureJump(string label)
    {
        yield return WaitReady(30f);
        float y0 = pc.transform.position.y, top = y0;
        yield return Flick(PlayerController.FlickDirection.Up);
        for (float t = 0f; t < 2.5f; t += Time.deltaTime)
        {
            HoldKmh(30f);
            top = Mathf.Max(top, pc.transform.position.y);
            if (t > 0.3f && pc.IsGrounded) break;
            yield return null;
        }
        lastApex = top - y0;
        L($"[jump {label}] apex {lastApex:F2}m jumpForce={pc.jumpForce:F2}");
    }

    IEnumerator MeasureAttack()
    {
        yield return WaitReady(30f);
        yield return new WaitForSeconds(0.4f);
        yield return Flick(PlayerController.FlickDirection.Forward);
        float t = 0f; bool seen = false;
        while (t < 3f)
        {
            HoldKmh(30f);
            if (pc.IsAttacking) seen = true; else if (seen) break;
            yield return null; t += Time.deltaTime;
        }
        lastAttack = t;
        yield return new WaitForSeconds(0.6f); // 次の段の受付が切れるまで
    }

    IEnumerator MeasureHitbox()
    {
        yield return WaitReady(30f);
        yield return new WaitForSeconds(0.4f);
        yield return Flick(PlayerController.FlickDirection.Forward);
        lastWidth = 0f;
        // 攻撃範囲倍率が掛かるのは判定のTransformの大きさ(1段目どうしで比べる。形の追加調整MeleeReachはColliderのsize側なので含めない)
        for (float t = 0f; t < 0.6f && lastWidth <= 0f; t += Time.deltaTime)
        {
            var c = pc.attackHitbox as BoxCollider2D;
            if (c != null && c.enabled) { yield return null; lastWidth = c.transform.localScale.x; L($"   [hitbox] stage={pc.CurrentAttackStage} localScale.x={lastWidth:F3} range={pc.AttackRangeMultiplier:F2}"); }
            yield return null;
        }
        yield return new WaitForSeconds(0.6f);
    }
}
#endif
