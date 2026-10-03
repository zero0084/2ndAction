#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

// カードバランス v3(2026-10-03)の自動テスト。 -qaCardV3 <dir> [-qaV3Only TDHPSBELCRX] [-qaV3Chars a,b] [-qaV3Secs 12]
//  T: 99枚 × Lv1/5/9 の実測(黒剣士。カードの処理そのままで掛けた後の値)→ cardv3_levels.tsv
//  D: 死にカード監査(99枚 × 12キャラ、Lv1 で何かが変わるか / そのキャラで意味があるか)→ cardv3_dead.tsv
//  H: HP犠牲 / HEART UP の取得順(どちらの順でも罠にならない・下限に届いた後に利点だけ伸びない・払えない時は候補に出ない)
//  P: 追加攻撃 / 属性 / 防御 / 復活の発動(代表キャラで実際に殴る)→ cardv3_procs.tsv
//  S: 速度 100/125/150/175km/h の MOMENTUM / OVERDRIVE / SONIC BLADE と、速度カードの最終km/h → cardv3_speed.tsv
//  B: DPS(硬いダミー)と ボス戦(カードなし/一般/Attack特化/Boss特化)→ cardv3_dps.tsv / cardv3_boss.tsv
//  E: EXP / MILE の進み方(100,000m までの計算。式はゲームの値、撃破の数は仮定)→ cardv3_exp.tsv
//  L: 長距離の雑魚の硬さ(0〜100km の HP / 必要な発数 / 実際の TTK)→ cardv3_ttk.tsv。-qaV3TtkChars / -qaV3TtkEnemies / -qaV3TtkCap
//  C: Challenge 大量 + 追加攻撃 全部 を同時に取って走る(敵の数/フレーム時間/GC/例外/無限連鎖)→ cardv3_perf.tsv
//  R: CONTINUE(PHOENIX の消費・封印・Shield を含むランを中断→再開して同じ状態か)
//  X: 12キャラ × 主要カードの互換(攻撃/範囲/攻撃速度/ジャンプ/コンボ/単発の流れの窓)
public partial class QaSweep
{
    static readonly MethodInfo V3BaseStats = typeof(GameManager).GetMethod("ApplyCharacterBaseStats", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly MethodInfo V3Pick = typeof(GameManager).GetMethod("ApplyUpgradeByCardId", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly MethodInfo V3LivesSet = typeof(GameManager).GetProperty("Lives").GetSetMethod(true);
    static System.Globalization.CultureInfo IC => System.Globalization.CultureInfo.InvariantCulture;

    IEnumerator CardV3Mode()
    {
        var snapSave = SaveSystem.Capture();
        autoPickHold = true;
        string only = Arg("-qaV3Only", "TDHPSBECRX");
        if (only.Contains('T')) yield return V3Table();
        if (only.Contains('D')) yield return V3Dead();
        if (only.Contains('H')) yield return V3Sacrifice();
        if (only.Contains('X')) yield return V3Compat();
        if (only.Contains('P')) yield return V3Procs();
        if (only.Contains('S')) yield return V3Speed();
        if (only.Contains('B')) yield return V3Dps();
        if (only.Contains('E')) yield return V3Exp();
        if (only.Contains('L')) yield return V3Ttk();
        if (only.Contains('W')) yield return V3LongDebug();
        if (only.Contains('C')) yield return V3Perf();
        if (only.Contains('R')) yield return V3Continue();
        GameManager.BlockExpGain = false;
        autoPickHold = false;
        SaveSystem.Restore(snapSave);
        CardInventory.ReloadFromPrefs();
        RunCheckpoint.Reload();
        L("[cardv3] test machine save restored");
    }

    void V3Write(string file, StringBuilder sb) => System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, file), sb.ToString());

    IEnumerator V3Begin(string ch, string stage = "wasteland_road")
    {
        yield return BeginRun(ch, stage);
        GameManager.BlockExpGain = true;
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
        yield return new WaitForSecondsRealtime(0.3f);
    }

    // 穴/障害物/敵/ボスの関門の無い直線(DPS/速度の計測用)
    void V3FlatStretch()
    {
        var tmz = TerrainManager.Instance;
        float lx0 = FloatingOrigin.ToLogical(pc.transform.position.x);
        tmz.SetResumeFlatZone(lx0 - 5f, lx0 + 8000f);
        tmz.enemySpawnChance = 0f;
        var os = FindFirstObjectByType<ObstacleSpawner>(); if (os != null) os.enabled = false;
        foreach (var ob in FindObjectsByType<ObstacleController>(FindObjectsSortMode.None)) Destroy(ob.gameObject);
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
        BossManager.SuppressGates = true;
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
    }

    IEnumerator V3End()
    {
        BossManager.SuppressGates = false;
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = true; // 次のランへ持ち越さない(シーンをまたいで残る)
        GameManager.BlockExpGain = false;
        PlayerController.DebugRunOnlyScale = 1f;
        TimeControl.SetDebugTimeScale(1f);
        yield return EndRun();
    }

    // キャラの基本値へ(カードLv/封印/復活/Shield も外れる)
    void V3Reset()
    {
        var def = CharacterDatabase.FindById(gm.ActiveRunCharacterId);
        V3BaseStats.Invoke(gm, new object[] { def });
        V3LivesSet.Invoke(gm, new object[] { gm.maxLives });
        ElementSystem.ResetCounters(); CardProcs.ResetCounters();
    }

    void V3Apply(string id, int lv)
    {
        var c = CardDatabase.FindBaseById(id);
        if (c == null) { Warn($"unknown card {id}"); return; }
        gm.ApplyCardEffectsStacked(c, lv);
    }

    float KmhAtNaturalCap => GameManager.SpeedKmh(pc.runSpeed * pc.MaxSpeedRatio);

    // 主な値(表/差分の比較用)
    Dictionary<string, float> V3Snap()
    {
        var T = gm.Card;
        var d = new Dictionary<string, float>
        {
            ["atkA"] = pc.CardAttackFactor - 1f,
            ["air"] = T.Get(EffectType.AirPct), ["ground"] = T.Get(EffectType.GroundPct), ["first"] = T.Get(EffectType.FirstPct), ["combo"] = T.Get(EffectType.ComboPct),
            ["fin"] = T.Get(EffectType.FinisherPct), ["boss"] = T.Get(EffectType.BossPct), ["mob"] = T.Get(EffectType.MobPct), ["antiair"] = T.Get(EffectType.AntiAirPct),
            ["down"] = T.Get(EffectType.DownPct), ["fullhp"] = T.Get(EffectType.FullHpPct), ["low50"] = T.Get(EffectType.LowHp50Pct), ["low25"] = T.Get(EffectType.LowHp25Pct),
            ["kmhCap"] = KmhAtNaturalCap, ["jump"] = pc.CardJumpFactor, ["jumps"] = pc.maxJumps, ["mageAlt"] = pc.MageExtraAltitudeLevels,
            ["tempo"] = 1f / Mathf.Max(0.01f, pc.AttackSpeedMultiplier), ["mainTempo"] = 1f / Mathf.Max(0.01f, pc.MainAttackSpeedMultiplier),
            ["range"] = pc.CardRangeFactor, ["projRange"] = PlayerController.ProjectileTravelFactor,
            ["hearts"] = gm.maxLives / (float)CombatScale.HpPerHeart, ["sealed"] = gm.SealedHearts, ["shield"] = pc.ShieldCapacity, ["shieldRe"] = pc.ShieldRechargeSeconds,
            ["exp"] = gm.ExpMultDistance, ["expKillMul"] = gm.ExpMultKill, ["expDist"] = T.Get(EffectType.DistanceExpPct), ["expKill"] = T.Get(EffectType.KillExpPct),
            ["mile"] = gm.MileGainMultiplier, ["bossMile"] = gm.BossMileGainMultiplier, ["treasure"] = T.Get(EffectType.TreasureMilePct), ["distMile"] = T.Get(EffectType.DistanceMilePct),
            ["spawn"] = gm.EnemySpawnRateMultiplier, ["enemyHp"] = gm.EnemyHpMultiplier, ["bossHp"] = gm.BossHpMultiplier, ["action"] = ChallengeSystem.EnemyActionScale, ["elite"] = ChallengeSystem.EliteChance,
            ["steal"] = T.Get(EffectType.LifestealChance), ["steal25"] = T.Get(EffectType.LowHp25LifestealChance), ["heal"] = gm.HealHearts(1), ["dbl"] = T.Get(EffectType.DoubleAttackChance),
            ["phoenix"] = gm.PhoenixCharges, ["phoenixHp"] = T.Get(EffectType.PhoenixLevel),
            ["edge"] = T.Get(EffectType.ComboEdgePct), ["airDom"] = T.Get(EffectType.AirDominionPct), ["momentum"] = T.Get(EffectType.MomentumPct),
            ["overdrive"] = T.Get(EffectType.OverdrivePct), ["odAS"] = T.Get(EffectType.OverdriveAttackSpeedPct), ["lowAS"] = T.Get(EffectType.LowHpAttackSpeedPct), ["sealedPct"] = T.Get(EffectType.SealedHeartPct),
            ["hurtInv"] = T.Get(EffectType.HurtInvincibleSeconds), ["kbReduce"] = T.Get(EffectType.HurtKnockbackReduce),
        };
        // 挙動のカードは Lv(EffectType の合計)をそのまま
        foreach (EffectType t in System.Enum.GetValues(typeof(EffectType)))
            if (CardEffectFormat.IsBehaviorLevel(t) || ElementStats.IsElementEffect(t) || t >= EffectType.BurnPowerPct) d["fx." + t] = T.Get(t);
        return d;
    }

    static string V3Fmt(float v) => Mathf.Approximately(v, Mathf.Round(v)) ? Mathf.RoundToInt(v).ToString() : v.ToString("0.###", IC);

    static string V3Diff(Dictionary<string, float> a, Dictionary<string, float> b)
    {
        var parts = new List<string>();
        foreach (var kv in b) if (!a.TryGetValue(kv.Key, out float x) || Mathf.Abs(x - kv.Value) > 1e-4f) parts.Add($"{kv.Key}={V3Fmt(kv.Value)}");
        return string.Join(" ", parts);
    }

    // ===================================================================== T
    IEnumerator V3Table()
    {
        L("== T: 99枚 × Lv1/5/9(黒剣士、カードの処理そのまま) ==");
        yield return V3Begin("swordsman");
        var cards = CardDatabase.AllCards.OrderBy(c => c.sortOrder).ToList();
        Check(cards.Count == 99, $"normal cards = 99 ({cards.Count})");
        Check(CardDatabase.FindBaseById("ultimate") != null && CardDatabase.FindBaseById("ultimate").cardName == "ALMIGHTY", "old #79 ULTIMATE is shown as ALMIGHTY (cardId kept)");
        Check(cards.All(c => c.cardName != "ULTIMATE"), "no card is named ULTIMATE (reserved for #100)");
        V3Reset();
        var baseSnap = V3Snap();
        var sb = new StringBuilder("no\tid\tname\tLv1\tLv5\tLv9\n");
        int idx = 0;
        foreach (var c in cards)
        {
            idx++;
            var row = new List<string> { idx.ToString(), c.cardId, c.cardName };
            foreach (int lv in new[] { 1, 5, 9 })
            {
                V3Reset();
                V3Apply(c.cardId, lv);
                yield return null;
                row.Add(V3Diff(baseSnap, V3Snap()));
            }
            sb.AppendLine(string.Join("\t", row));
        }
        V3Write("cardv3_levels.tsv", sb);
        L($"[T] wrote cardv3_levels.tsv ({cards.Count} cards)");

        // 目安の値(仕様の基準)
        V3Reset(); V3Apply("speed_up", 9); float su9 = KmhAtNaturalCap;
        V3Reset(); V3Apply("attack_up", 9); float au9 = pc.CardAttackFactor;
        V3Reset(); V3Apply("attack_speed_up", 9); float as9 = 1f / pc.AttackSpeedMultiplier;
        V3Reset(); V3Apply("attack_range_up", 9); float ar9 = pc.CardRangeFactor;
        V3Reset(); V3Apply("jump_power_up", 9); float jp9 = pc.CardJumpFactor;
        V3Reset(); V3Apply("heart_up", 9); float hu9 = gm.maxLives;
        L($"[T] SPEED UP Lv9 {su9:F1}km/h (spec 127) / ATTACK UP Lv9 x{au9:F2} (1.45) / ATTACK SPEED UP Lv9 tempo x{as9:F2} (1.27) / RANGE Lv9 x{ar9:F2} (1.27) / JUMP Lv9 x{jp9:F2} (1.27) / HEART UP Lv9 maxHP {hu9} (50+90)");
        Check(Mathf.Abs(su9 - 127f) < 1.5f, "SPEED UP Lv9 = +27% (127 km/h at the natural cap)");
        Check(Mathf.Abs(au9 - 1.45f) < 0.01f, "ATTACK UP Lv9 = +45%");
        Check(Mathf.Abs(as9 - 1.27f) < 0.01f, "ATTACK SPEED UP Lv9 = +27% attack speed");
        Check(Mathf.Abs(ar9 - 1.27f) < 0.01f && Mathf.Abs(jp9 - 1.27f) < 0.01f, "ATTACK RANGE UP / JUMP POWER UP Lv9 = +27%");
        Check(Mathf.RoundToInt(hu9) == 140, "HEART UP Lv9 = +9 hearts");
        V3Reset(); V3Apply("heart_up", 9); V3Apply("fortress", 9); V3Apply("the_long_road", 9);
        Check(gm.maxLives == CardRules.MaxHeartsCap * CombatScale.HpPerHeart, $"max HP cap = 20 hearts ({gm.maxLives})");
        V3Reset(); V3Apply("shield", 1); int s1 = pc.ShieldCapacity; V3Reset(); V3Apply("shield", 4); int s4 = pc.ShieldCapacity; V3Reset(); V3Apply("shield", 9); int s9 = pc.ShieldCapacity;
        Check(s1 == 1 && s4 == 2 && s9 == 3, $"SHIELD Lv1-3=1 / Lv4-6=2 / Lv7-9=3 ({s1},{s4},{s9})");
        V3Reset(); V3Apply("exp_converter", 9); V3Apply("exp_converter", 9);
        Check(gm.ExpMultDistance >= 0.25f - 1e-4f && gm.ExpMultKill >= 0.25f - 1e-4f && !float.IsNaN(gm.ExpMultDistance), $"EXP CONVERTER never makes EXP <= 0 (x{gm.ExpMultDistance:F2} with Lv18)");
        // 速度の極端
        V3Reset(); foreach (var id in new[] { "speed_up", "greed", "no_turning_back", "close_call", "ultimate", "overdrive" }) V3Apply(id, 9);
        float sMax = KmhAtNaturalCap;
        V3Reset(); V3Apply("speed_down", 9); float sDown = KmhAtNaturalCap;
        L($"[T] all speed cards Lv9 {sMax:F0} km/h (no 200+), SPEED DOWN Lv9 {sDown:F0} km/h");
        Check(sMax < 180f, "all speed cards Lv9 stay well under 200 km/h");
        Check(sDown >= 55f, "SPEED DOWN Lv9 stays controllable (>= 55 km/h at the natural cap)");
        yield return V3End();
    }

    // ===================================================================== D
    // そのキャラで意味がない効果(それしか無いカードは「死にカード」)
    static bool V3Applicable(EffectType t, CharacterDefinition def)
    {
        bool mage = def.kit == CharacterKit.Mage;
        switch (t)
        {
            case EffectType.SkyMasterLevel: return !mage; // 空中ジャンプを取り戻す(魔法使いはジャンプしない)
            case EffectType.DownPct: return def.canUseDownAttack;
        }
        return true;
    }

    IEnumerator V3Dead()
    {
        L("== D: 死にカード監査(99枚 × 12キャラ、Lv1) ==");
        var sb = new StringBuilder("id\tname\tchar\tchanged\tdead\tpartial\n");
        int dead = 0, partial = 0;
        var deadList = new List<string>();
        foreach (var def in CharacterDatabase.AllCharacters)
        {
            yield return V3Begin(def.characterId);
            V3Reset();
            var b = V3Snap();
            foreach (var c in CardDatabase.AllCards.OrderBy(x => x.sortOrder))
            {
                V3Reset();
                V3Apply(c.cardId, 1);
                var s = V3Snap();
                string diff = V3Diff(b, s);
                var types = c.effects.Select(e => e.type).Distinct().ToList();
                var na = types.Where(t => !V3Applicable(t, def)).ToList();
                bool isDead = diff.Length == 0 || na.Count == types.Count;
                if (isDead) { dead++; deadList.Add($"{c.cardName}@{def.characterId}"); }
                if (!isDead && na.Count > 0) partial++;
                sb.AppendLine($"{c.cardId}\t{c.cardName}\t{def.characterId}\t{diff}\t{(isDead ? "DEAD" : "")}\t{string.Join(",", na)}");
            }
            yield return V3End();
        }
        V3Write("cardv3_dead.tsv", sb);
        L($"[D] dead (card x char) = {dead} {string.Join(", ", deadList)} / partial (some effect not usable by that char) = {partial}");
        Check(dead == 0, "no card does nothing for any character at Lv1");
    }

    // ===================================================================== H
    IEnumerator V3Sacrifice()
    {
        L("== H: HP犠牲 / HEART UP の取得順 ==");
        yield return V3Begin("swordsman");
        stopKeepAlive = true;
        int per = CombatScale.HpPerHeart;
        string[] risk = { "berserker", "glass_cannon", "heart_breaker", "deaths_contract" };
        // 1) HEART UP Lv9 → 犠牲4枚 Lv9(実際の取得と同じ経路: 候補に出ない時は取らない)
        V3Reset();
        for (int i = 0; i < 9; i++) V3Pick.Invoke(gm, new object[] { "heart_up" });
        int skipped = 0;
        foreach (var id in risk) for (int i = 0; i < 9; i++) { if (gm.CanStillPick(CardDatabase.FindBaseById(id))) V3Pick.Invoke(gm, new object[] { id }); else skipped++; }
        L($"[H1] HEART UP 9 then risk x4: maxHP {gm.maxLives} (before seal {gm.CardMaxLivesBeforeSeal}) sealed {gm.SealedHearts} heartUpLv {gm.GetCurrentRunStack("heart_up")} atkA x{pc.CardAttackFactor:F2} skippedPicks {skipped} levels {string.Join(",", risk.Select(r => r + ":" + gm.GetCurrentRunStack(r) + "/paid" + gm.SacrificePaidOf(r)))}");
        Check(gm.GetCurrentRunStack("heart_up") == 9, "HEART UP stays Lv9 after sacrifices (growth history is kept)");
        Check(gm.maxLives >= CardRules.MinHearts * per, "max HP never below 1 heart");
        float atk1 = pc.CardAttackFactor;
        // 下限に届いた後: 犠牲カードの残りLvは候補に出ず、取っても攻撃は伸びない
        bool anyPickable = risk.Any(id => gm.GetCurrentRunStack(id) < 9 && gm.CanStillPick(CardDatabase.FindBaseById(id)));
        L($"[H1] at floor: any risk card still offered = {anyPickable}");
        Check(!anyPickable || gm.SacrificeAvailableHearts() > 0, "an unpayable sacrifice level is not offered");
        // 2) 逆の順: 犠牲 → HEART UP(HEART UP は普通に効き、払えなかった分は余裕ができた時に払われる)
        V3Reset();
        foreach (var id in risk) for (int i = 0; i < 9; i++) if (gm.CanStillPick(CardDatabase.FindBaseById(id))) V3Pick.Invoke(gm, new object[] { id });
        int maxA = gm.maxLives; float atkA = pc.CardAttackFactor; int sealedA = gm.SealedHearts;
        for (int i = 0; i < 9; i++) V3Pick.Invoke(gm, new object[] { "heart_up" });
        L($"[H2] risk first: maxHP {maxA} sealed {sealedA} atk x{atkA:F2} → then HEART UP 9: maxHP {gm.maxLives} sealed {gm.SealedHearts} atk x{pc.CardAttackFactor:F2} heartUpLv {gm.GetCurrentRunStack("heart_up")}");
        Check(gm.GetCurrentRunStack("heart_up") == 9, "HEART UP can still be taken to Lv9 after the sacrifices (no order trap)");
        Check(gm.maxLives >= maxA, "HEART UP after sacrifices raises (or keeps) the usable max HP");
        // 3) キャラカード(開始時)の犠牲が払えない時: 効く Lv は払えた分まで、HP が増えたら残りを払う
        V3Reset();
        V3Apply("deaths_contract", 9); // お嬢様騎士でなくても: 5ハート要求 / 黒剣士は4まで
        int effPaid = gm.SacrificePaidOf("deaths_contract");
        float atkDebt = pc.CardAttackFactor;
        V3Apply("heart_up", 3);
        L($"[H3] DEATH'S CONTRACT Lv9 at start: paid {effPaid} atk x{atkDebt:F2} → +HEART UP 3: paid {gm.SacrificePaidOf("deaths_contract")} atk x{pc.CardAttackFactor:F2} maxHP {gm.maxLives}");
        Check(gm.SacrificePaidOf("deaths_contract") >= effPaid && pc.CardAttackFactor >= atkDebt, "unpaid sacrifice is paid later when HP grows (benefit catches up, never drops)");
        // 4) Lives は封印で最大を超えない
        Check(gm.Lives <= gm.maxLives, "current HP never exceeds the sealed max");
        stopKeepAlive = false;
        yield return V3End();
    }

    // ===================================================================== X
    IEnumerator V3Compat()
    {
        L("== X: 12キャラ × 主要カード ==");
        var sb = new StringBuilder("char\tatk9\trange9\tprojRange9\ttempo9\tmainTempo9\tjump9\tjumps9\tmageAlt9\tseqWindowBase\tseqWindowFast\tseqWindowSlow\n");
        foreach (var def in CharacterDatabase.AllCharacters)
        {
            yield return V3Begin(def.characterId);
            V3Reset(); float w0 = pc.SequenceResetWindow;
            V3Apply("attack_up", 9); float atk = pc.CardAttackFactor;
            V3Reset(); V3Apply("attack_range_up", 9); float rng = pc.CardRangeFactor, prj = PlayerController.ProjectileTravelFactor;
            V3Reset(); V3Apply("attack_speed_up", 9); V3Apply("rapid_edge", 9); float tempo = 1f / pc.AttackSpeedMultiplier * (pc.AttackSpeedMultiplier / Mathf.Max(0.01f, def.attackSpeedMultiplier)) ; float main = def.attackSpeedMultiplier / pc.MainAttackSpeedMultiplier; float wFast = pc.SequenceResetWindow;
            V3Reset(); V3Apply("giant_slayer", 9); float wSlow = pc.SequenceResetWindow;
            V3Reset(); V3Apply("jump_power_up", 9); V3Apply("jump_count_up", 9); float jf = pc.CardJumpFactor; int jumps = pc.maxJumps; int alt = pc.MageExtraAltitudeLevels;
            sb.AppendLine($"{def.characterId}\t{atk:F2}\t{rng:F2}\t{prj:F2}\t{def.attackSpeedMultiplier / Mathf.Max(0.01f, pc.AttackSpeedMultiplier):F2}\t{main:F2}\t{jf:F2}\t{jumps}\t{alt}\t{w0:F2}\t{wFast:F2}\t{wSlow:F2}");
            Check(Mathf.Abs(atk - 1.45f) < 0.01f, $"{def.characterId}: ATTACK UP Lv9 = +45%");
            Check(Mathf.Abs(rng - 1.27f) < 0.01f && Mathf.Abs(prj - 1.27f) < 0.01f, $"{def.characterId}: RANGE Lv9 = +27% (melee box and projectile travel)");
            Check(jumps == def.jumpCount + 9, $"{def.characterId}: JUMP COUNT UP Lv9 = +9 ({jumps})");
            if (def.kit == CharacterKit.Mage) Check(alt == 3, $"mage: JUMP COUNT UP Lv9 = +3 altitude levels ({alt})");
            Check(wFast >= CardRules.SingleAttackSequenceReset * CardRules.SequenceResetMinScale - 1e-3f && wSlow > w0, $"{def.characterId}: single-attack sequence window follows attack speed (base {w0:F2}s, fast {wFast:F2}s, slow {wSlow:F2}s)");
            yield return V3End();
        }
        V3Write("cardv3_compat.tsv", sb);
    }

    // ===================================================================== P
    struct V3ProcCase { public string name; public string[] cards; public string counter; public bool air, kill, ranged; }

    static int V3Counter(string name)
    {
        switch (name)
        {
            case "shockwave": return CardProcs.Shockwaves;
            case "pierce": return CardProcs.PierceHits + KitProjectile.PierceThrough;
            case "aerial": return CardProcs.AerialSlashes;
            case "combomaster": return CardProcs.ComboMasterHits;
            case "groundbreaker": return CardProcs.GroundBreakers;
            case "sonic": return CardProcs.SonicSlashes;
            case "chainexplosion": return CardProcs.ChainExplosions;
            case "double": return CardProcs.DoubleAttacks;
            case "inferno": return CardProcs.Infernos;
            case "tornado": return CardProcs.Tornados;
            case "burn": return ElementSystem.BurnProcs;
            case "chill": return ElementSystem.ChillProcs;
            case "freeze": return ElementSystem.FreezeProcs;
            case "shatter": return ElementSystem.Shatters;
            case "lightning": return ElementSystem.LightningProcs;
            case "chain": return ElementSystem.LightningHits - ElementSystem.LightningProcs;
            case "wind": return ElementSystem.WindBlades;
            case "bleed": return ElementSystem.BleedProcs;
            case "lifesteal": return GameManager.LifestealProcs;
        }
        return 0;
    }

    static readonly V3ProcCase[] V3ProcCases =
    {
        new V3ProcCase { name = "SHOCKWAVE", cards = new[] { "shockwave" }, counter = "shockwave" },
        new V3ProcCase { name = "PIERCING BLADE", cards = new[] { "piercing_blade" }, counter = "pierce" },
        new V3ProcCase { name = "AERIAL BLADE", cards = new[] { "aerial_blade" }, counter = "aerial", air = true },
        new V3ProcCase { name = "COMBO MASTER", cards = new[] { "combo_master" }, counter = "combomaster" },
        new V3ProcCase { name = "GROUND BREAKER", cards = new[] { "ground_breaker" }, counter = "groundbreaker" },
        new V3ProcCase { name = "SONIC BLADE", cards = new[] { "sonic_blade" }, counter = "sonic" },
        new V3ProcCase { name = "DOUBLE ATTACK", cards = new[] { "double_attack" }, counter = "double" },
        new V3ProcCase { name = "CHAIN EXPLOSION", cards = new[] { "chain_explosion" }, counter = "chainexplosion", kill = true },
        new V3ProcCase { name = "FLAME BLADE", cards = new[] { "flame_blade" }, counter = "burn" },
        new V3ProcCase { name = "INFERNO(+FLAME)", cards = new[] { "flame_blade", "inferno" }, counter = "inferno", kill = true },
        new V3ProcCase { name = "INFERNO(単体)", cards = new[] { "inferno" }, counter = "burn" },
        new V3ProcCase { name = "BURNING SOUL(単体)", cards = new[] { "burning_soul" }, counter = "burn" },
        new V3ProcCase { name = "FROST EDGE", cards = new[] { "frost_edge" }, counter = "chill" },
        new V3ProcCase { name = "ICE PRISON(+FROST)", cards = new[] { "frost_edge", "ice_prison" }, counter = "freeze" },
        new V3ProcCase { name = "ABSOLUTE ZERO(+FROST)", cards = new[] { "frost_edge", "ice_prison", "absolute_zero" }, counter = "shatter" },
        new V3ProcCase { name = "ICE PRISON(単体)", cards = new[] { "ice_prison" }, counter = "chill" },
        new V3ProcCase { name = "ABSOLUTE ZERO(単体)", cards = new[] { "absolute_zero" }, counter = "chill" },
        new V3ProcCase { name = "THUNDER STRIKE", cards = new[] { "thunder_strike" }, counter = "lightning" },
        new V3ProcCase { name = "HIGH VOLTAGE(単体)", cards = new[] { "high_voltage" }, counter = "lightning" },
        new V3ProcCase { name = "CHAIN LIGHTNING(+THUNDER)", cards = new[] { "thunder_strike", "chain_lightning" }, counter = "chain" },
        new V3ProcCase { name = "CHAIN LIGHTNING(単体)", cards = new[] { "chain_lightning" }, counter = "lightning" },
        new V3ProcCase { name = "THUNDER LORD(単体)", cards = new[] { "thunder_lord" }, counter = "lightning" },
        new V3ProcCase { name = "WIND CUTTER", cards = new[] { "wind_cutter" }, counter = "wind" },
        new V3ProcCase { name = "GALE(単体)", cards = new[] { "gale" }, counter = "wind" },
        new V3ProcCase { name = "TORNADO(+WIND)", cards = new[] { "wind_cutter", "tornado" }, counter = "tornado" },
        new V3ProcCase { name = "TORNADO(単体)", cards = new[] { "tornado" }, counter = "wind" },
        new V3ProcCase { name = "BLOOD BLADE", cards = new[] { "blood_blade" }, counter = "bleed" },
        new V3ProcCase { name = "VAMPIRE", cards = new[] { "vampire", "predator" }, counter = "lifesteal", kill = true },
    };

    IEnumerator V3Procs()
    {
        L("== P: 追加攻撃 / 属性 / 防御 / 復活 ==");
        var chars = Arg("-qaV3Chars", "swordsman,gunslinger,mage,fighter").Split(',');
        float secs = float.Parse(Arg("-qaV3Secs", "8"), IC);
        var sb = new StringBuilder("char\tcase\tcards\tcounter\tcount\tdealt\n");
        var savedRoll = ElementSystem.Roll;
        foreach (string ch in chars)
        {
            yield return V3Begin(ch, "natural_cave");
            var tm = TerrainManager.Instance; if (tm != null) tm.enemySpawnChance = 0f;
            var failed = new List<V3ProcCase>();
            foreach (var pcase in V3ProcCases)
            {
                int got = 0;
                yield return V3ProcCaseRun(ch, pcase, secs, sb, g => got = g);
                if (got == 0) failed.Add(pcase);
            }
            // ---- 防御 / 復活(被弾を直接起こす)
            yield return V3Defense(ch, sb);
            yield return V3End();
            // 確率の低いケース(単体の小さな確率)/途中で止まったケースは、新しいランで長めにやり直す
            if (failed.Count > 0)
            {
                yield return V3Begin(ch, "natural_cave");
                var tm2 = TerrainManager.Instance; if (tm2 != null) tm2.enemySpawnChance = 0f;
                foreach (var pcase in failed)
                {
                    int got = 0;
                    yield return V3ProcCaseRun(ch, pcase, Mathf.Max(20f, secs * 2f), sb, g => got = g);
                    L($"[P:{ch}] retry {pcase.name}: +{got}");
                    Check(got > 0, $"{ch}: {pcase.name} works ({pcase.counter}) [retry]");
                }
                yield return V3End();
            }
        }
        ElementSystem.Roll = savedRoll;
        V3Write("cardv3_procs.tsv", sb);
    }

    IEnumerator V3ProcCaseRun(string ch, V3ProcCase pcase, float secs, StringBuilder sb, System.Action<int> result)
    {
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
        yield return null;
        V3Reset();
        foreach (var id in pcase.cards) V3Apply(id, 9);
        // 単体のケース(上位カードの小さな確率)は、確率の判定を必ず成功にして「仕組みが動くか」を見る
        var rollSave = ElementSystem.Roll;
        if (pcase.name.Contains("単体")) ElementSystem.Roll = () => 0.001f;
        int before = V3Counter(pcase.counter);
        long dmgBefore = ElementSystem.ElementDamage;
        var targets = new List<EnemyController>();
        for (int i = 0; i < 4; i++)
        {
            var en = HaSpawn(1.4f + i * 0.9f);
            if (en == null) continue;
            if (pcase.kill) HaEnemyHp.SetValue(en, 30); else HaEnemyHp.SetValue(en, 100000);
            targets.Add(en);
        }
        float t0 = Time.time, lastFlick = 0f;
        pc.autoRunEnabled = false; // 連撃の3段目まで同じ相手に届くように(敵も元の位置へ戻す)
        var slots = targets.Select(t => t != null ? t.transform.position.x - pc.transform.position.x : 0f).ToList();
        while (Time.time - t0 < secs && V3Counter(pcase.counter) == before)
        {
            yield return null;
            for (int i = 0; i < targets.Count && i < slots.Count; i++)
            {
                var t = targets[i];
                // 倒れていない敵が穴へ落ちた/消えた時は出し直す(このテストの敵は倒す想定のケース以外は倒れない)
                if (!pcase.kill && (t == null || t.IsDying)) { var en = HaSpawn(slots[i]); if (en != null) { HaEnemyHp.SetValue(en, 100000); targets[i] = en; } continue; }
                if (t == null || t.IsDying || (bool)HaEnemyLaunched.GetValue(t)) continue;
                float want = pc.transform.position.x + slots[i];
                var tmg = TerrainManager.Instance;
                if (Mathf.Abs(t.transform.position.x - want) > 0.5f && tmg != null && tmg.GetHeightAt(want).HasValue) t.transform.position = new Vector3(want, t.transform.position.y, t.transform.position.z);
            }
            if (pcase.kill && targets.All(t => t == null || t.IsDying))
            {
                targets.Clear();
                for (int i = 0; i < 4; i++) { var en = HaSpawn(1.4f + i * 0.9f); if (en != null) { HaEnemyHp.SetValue(en, 30); targets.Add(en); } }
            }
            if (Time.time - lastFlick > 0.16f)
            {
                lastFlick = Time.time;
                if (pcase.air && pc.IsGrounded) StartCoroutine(Flick(PlayerController.FlickDirection.Up));
                else StartCoroutine(Flick(PlayerController.FlickDirection.Forward));
            }
        }
        pc.autoRunEnabled = true;
        ElementSystem.Roll = rollSave;
        int got = V3Counter(pcase.counter) - before;
        if (got == 0)
        {
            var alive = targets.Where(t => t != null && !t.IsDying).ToList();
            L($"[P:{ch}] DIAG {pcase.name}: pos=({pc.transform.position.x:F1},{pc.transform.position.y:F1}) grounded={pc.IsGrounded} reacting={pc.IsReacting} timeScale={Time.timeScale:F2} reasons={TimeControl.DescribeActiveReasons()} lives={gm.Lives} alive={alive.Count} dx=[{string.Join(",", alive.Select(t => (t.transform.position.x - pc.transform.position.x).ToString("F1")))}] attacking={GetPrivate(pc, "isAttacking")} combo={pc.CurrentAttackStage} hitsTotal={ComboCounterUI.Instance?.ComboCount}");
            Shot($"v3proc_fail_{ch}_{pcase.counter}");
        }
        sb.AppendLine($"{ch}\t{pcase.name}\t{string.Join("+", pcase.cards)}\t{pcase.counter}\t{got}\t{ElementSystem.ElementDamage - dmgBefore}");
        L($"[P:{ch}] {pcase.name}: {pcase.counter} +{got} ({Time.time - t0:F1}s)");
        result(got);
        V3Write("cardv3_procs.tsv", sb);
    }

    IEnumerator V3Defense(string ch, StringBuilder sb)
    {
        V3FlatStretch(); // 道中の敵(Encounter)/障害物/ボスの関門で、確かめたい被弾以外のダメージを受けないように
        stopKeepAlive = true;
        int per = CombatScale.HpPerHeart;
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
        System.Func<GameManager.DamageResult> hit = () => gm.TryDamagePlayer(true, "QaV3", CombatScale.PlayerHit);
        // COUNTER / FLAME COUNTER / PERFECT GUARD(Shield を持っていなくても1つ)
        foreach (var id in new[] { "counter", "flame_counter", "perfect_guard" })
        {
            V3Reset();
            foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
            yield return null;
            var en = HaSpawn(1.5f); if (en != null) HaEnemyHp.SetValue(en, 100000);
            V3Apply(id, 5);
            int shields = pc.ShieldCharges;
            int c0 = CardProcs.Counters + CardProcs.FlameCounters;
            int lives = gm.Lives;
            var r = hit();
            bool inv = gm.CardInvincibleActive;
            int procs = CardProcs.Counters + CardProcs.FlameCounters - c0;
            L($"[P:{ch}] {id} Lv5: shield {shields} → blocked={r} lives {lives}->{gm.Lives} counterProcs {procs} invincible {inv}");
            sb.AppendLine($"{ch}\t{id}\t{id}\tblock\t{(r == GameManager.DamageResult.Ignored ? 1 : 0)}\t{procs}");
            Check(shields >= 1 && r == GameManager.DamageResult.Ignored && gm.Lives == lives, $"{ch}: {id} gives a Shield even without SHIELD and it blocks");
            if (id == "perfect_guard") Check(inv, $"{ch}: PERFECT GUARD gives a short invincibility on a block");
            else Check(procs > 0, $"{ch}: {id} counter fires on a block");
            yield return new WaitForSeconds(0.2f);
        }
        // SHIELD の回復(反撃のテストの敵は片付ける。触れて Shield を使わないように)
        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
        yield return null;
        SetPrivate(pc, "hitInvincibleTimer", 0f);
        SetPrivate(pc, "mageLevel", 0); // 魔法使い: 前のテストの上攻撃で上がった高度のまま洞窟の天井の針に触れないように
        yield return new WaitForSeconds(0.5f);
        {
            V3Reset();
            CardRules.ShieldRechargeBaseSeconds = 2f; CardRules.ShieldRechargeMinSeconds = 0.5f;
            V3Apply("shield", 1);
            hit();
            int afterHit = pc.ShieldCharges;
            yield return new WaitForSeconds(2.2f);
            L($"[P:{ch}] SHIELD Lv1: after block {afterHit}, after recharge {pc.ShieldCharges} (cap {pc.ShieldCapacity}, recharge {pc.ShieldRechargeSeconds:F1}s, lives {gm.Lives}, y {pc.transform.position.y:F1}, last damage '{pc.LastDamageSource}' {Time.time - pc.LastDamageTime:F1}s ago)");
            Check(afterHit == 0 && pc.ShieldCharges == 1, $"{ch}: SHIELD recharges over time");
            CardRules.ShieldRechargeBaseSeconds = 24f; CardRules.ShieldRechargeMinSeconds = 8f;
        }
        // OVERHEAL: 満タンで回復 → Shield
        {
            V3Reset(); V3Apply("overheal", 1);
            int s0 = pc.ShieldCharges;
            gm.CardHeal(per);
            L($"[P:{ch}] OVERHEAL: full-HP heal → shield {s0}->{pc.ShieldCharges}");
            Check(pc.ShieldCharges == s0 + 1, $"{ch}: OVERHEAL turns an overflowing heal into a Shield (even without VAMPIRE)");
        }
        // SECOND WIND
        {
            V3Reset(); V3Apply("second_wind", 5);
            V3LivesSet.Invoke(gm, new object[] { gm.maxLives * 4 / 10 });
            int l0 = gm.Lives;
            hit();
            int l1 = gm.Lives;
            V3LivesSet.Invoke(gm, new object[] { gm.maxLives * 4 / 10 });
            yield return new WaitForSeconds(1.0f);
            hit();
            int l2 = gm.Lives;
            L($"[P:{ch}] SECOND WIND Lv5: {l0} -hit-> {l1} (healed), again -> {l2} (cooldown {gm.SecondWindReadyDistance:F0}m)");
            Check(l1 > l0 - per && l2 == gm.maxLives * 4 / 10 - per, $"{ch}: SECOND WIND heals once, then waits for its distance cooldown");
        }
        // LAST CHANCE
        {
            V3Reset(); V3Apply("last_chance", 1);
            V3LivesSet.Invoke(gm, new object[] { per * 2 });
            hit();
            bool inv = gm.CardInvincibleActive;
            var r2 = hit();
            yield return new WaitForSeconds(CardRules.LastChanceBaseSeconds + 0.3f);
            bool rearmedWhileLow = gm.LastChanceArmed;
            V3LivesSet.Invoke(gm, new object[] { per * 3 });
            float saveRearm = CardRules.LastChanceRearmSeconds; CardRules.LastChanceRearmSeconds = 0.5f;
            yield return new WaitForSeconds(0.7f);
            bool rearmed = gm.LastChanceArmed;
            CardRules.LastChanceRearmSeconds = saveRearm;
            L($"[P:{ch}] LAST CHANCE: at 1 heart invincible={inv}, next hit {r2}, re-armed while still at 1 heart={rearmedWhileLow}, after healing+time={rearmed}");
            Check(inv && r2 == GameManager.DamageResult.Ignored && !rearmedWhileLow && rearmed, $"{ch}: LAST CHANCE gives one invincible window and no loop at 1 heart");
        }
        // PHOENIX
        {
            V3Reset(); V3Apply("phoenix", 5);
            int charges = gm.PhoenixCharges;
            V3LivesSet.Invoke(gm, new object[] { per });
            var r = gm.TryDamagePlayer(true, "QaV3", CombatScale.PlayerHeavyHit);
            int livesAfter = gm.Lives; int lvAfter = gm.CardLevel("phoenix");
            bool offered = gm.CanStillPick(CardDatabase.FindBaseById("phoenix"));
            V3Pick.Invoke(gm, new object[] { "phoenix" });
            L($"[P:{ch}] PHOENIX Lv5: charge {charges} → lethal hit {r}, revived HP {livesAfter} (3 hearts), level after {lvAfter}, offered again {offered}, re-taken charge {gm.PhoenixCharges} Lv{gm.CardLevel("phoenix")}");
            Check(charges == 1 && r == GameManager.DamageResult.Hit && !gm.IsGameOver && livesAfter == 3 * per, $"{ch}: PHOENIX Lv5 cancels the death with 3 hearts");
            Check(lvAfter == 0 && offered && gm.PhoenixCharges == 1 && gm.CardLevel("phoenix") == 1, $"{ch}: PHOENIX is consumed, offered again, re-taken from Lv1 with a new charge");
        }
        V3LivesSet.Invoke(gm, new object[] { gm.maxLives });
        stopKeepAlive = false;
        StartCoroutine(KeepAlive());
    }

    // ===================================================================== S
    IEnumerator V3Speed()
    {
        L("== S: 速度 ==");
        var sb = new StringBuilder("case\tkmh\tvalue\tnote\n");
        yield return V3Begin("swordsman", "natural_cave");
        // 速度カードの最終km/h(自然上限100km/hで)
        foreach (var (name, cards) in new (string, string[])[]
        {
            ("SPEED UP Lv1", new[] { "speed_up:1" }), ("SPEED UP Lv5", new[] { "speed_up:5" }), ("SPEED UP Lv9", new[] { "speed_up:9" }),
            ("GREED Lv9", new[] { "greed:9" }), ("NO TURNING BACK Lv9", new[] { "no_turning_back:9" }), ("ALMIGHTY Lv9", new[] { "ultimate:9" }), ("CLOSE CALL Lv9", new[] { "close_call:9" }),
            ("SPEED UP 9 + GREED 9", new[] { "speed_up:9", "greed:9" }), ("SPEED UP 9 + GREED 9 + NTB 9", new[] { "speed_up:9", "greed:9", "no_turning_back:9" }),
            ("Speed特化 Lv5", CardBuildPresets.Find("speed").Select(x => x + ":5").ToArray()), ("Speed特化 Lv9", CardBuildPresets.Find("speed").Select(x => x + ":9").ToArray()),
            ("SPEED DOWN Lv9", new[] { "speed_down:9" }), ("FORTRESS Lv9", new[] { "fortress:9" }), ("REVERSE GEAR Lv9", new[] { "reverse_gear:9" }),
        })
        {
            V3Reset();
            foreach (var e in cards) { var kv = e.Split(':'); V3Apply(kv[0], int.Parse(kv[1])); }
            sb.AppendLine($"{name}\t{KmhAtNaturalCap:F1}\t\tat natural 100km/h");
            L($"[S] {name}: {KmhAtNaturalCap:F1} km/h");
        }
        var tm = TerrainManager.Instance; if (tm != null) tm.enemySpawnChance = 0f;
        // MOMENTUM / OVERDRIVE / SONIC BLADE を実際の速さで(荒野、自然加速が上限に届く距離、穴/障害物/敵なしの直線)
        yield return V3End();
        yield return V3Begin("swordsman", "wasteland_road");
        WarpTo(4500f);
        yield return new WaitForSeconds(1.0f);
        {
            var tmz = TerrainManager.Instance;
            float lx0 = FloatingOrigin.ToLogical(pc.transform.position.x);
            tmz.SetResumeFlatZone(lx0 - 5f, lx0 + 8000f);
            var os = FindFirstObjectByType<ObstacleSpawner>(); if (os != null) os.enabled = false;
            foreach (var ob in FindObjectsByType<ObstacleController>(FindObjectsSortMode.None)) Destroy(ob.gameObject);
            if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
            if (BossManager.Instance != null) BossManager.SuppressGates = true;
            foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
        }
        foreach (float kmh in new[] { 100f, 125f, 150f, 175f })
        {
            V3Reset();
            V3Apply("momentum", 9); V3Apply("overdrive", 9); V3Apply("sonic_blade", 9);
            float baseKmh = Mathf.Max(1f, pc.CurrentRunKmh / Mathf.Max(0.01f, PlayerController.DebugRunOnlyScale));
            PlayerController.DebugRunOnlyScale = kmh / baseKmh;
            yield return new WaitForSeconds(0.2f);
            float mom = PlayerController.MomentumFactor(pc.CurrentRunKmh) * gm.Card.Get(EffectType.MomentumPct);
            float t0 = Time.time; float activatedAt = -1f;
            int sonic0 = CardProcs.SonicSlashes; float reachSum = 0f; int reachN = 0; float lastFlick = 0f;
            while (Time.time - t0 < 4f)
            {
                yield return null;
                if (activatedAt < 0f && pc.OverdriveActive) activatedAt = Time.time - t0;
                if (Time.time - lastFlick > 0.5f)
                {
                    lastFlick = Time.time;
                    StartCoroutine(Flick(PlayerController.FlickDirection.Forward));
                    yield return null; yield return null;
                    foreach (var kp in FindObjectsByType<KitProjectile>(FindObjectsSortMode.None))
                        if (kp.name == "CardSonicSlash") { reachSum += kp.lifetime * Mathf.Abs(kp.velocity.x); reachN++; }
                }
            }
            float atkMul = CardRules.CondMultiplier(pc.CardStateCondition());
            string line = $"{pc.CurrentRunKmh:F0}km/h: MOMENTUM +{mom * 100f:F0}% / OVERDRIVE {(activatedAt >= 0f ? $"on at {activatedAt:F1}s" : "off")} / attack x{atkMul:F2} (state) / SONIC {CardProcs.SonicSlashes - sonic0} slashes reach {(reachN > 0 ? reachSum / reachN : 0f):F1}m";
            L("[S] " + line);
            sb.AppendLine($"MOMENTUM9+OVERDRIVE9+SONIC9\t{pc.CurrentRunKmh:F0}\t{atkMul:F2}\t{line}");
            if (kmh < CardRules.OverdriveThresholdKmh) Check(activatedAt < 0f, $"OVERDRIVE does not start at {kmh} km/h");
            else Check(activatedAt >= 0f, $"OVERDRIVE starts at {kmh} km/h");
            if (kmh <= 100f) Check(mom < 0.001f, "MOMENTUM gives nothing at <=100 km/h");
        }
        // 解除
        PlayerController.DebugRunOnlyScale = 1f * 0.8f;
        yield return new WaitForSeconds(1.5f);
        Check(!pc.OverdriveActive, "OVERDRIVE ends when the speed drops");
        PlayerController.DebugRunOnlyScale = 1f;
        BossManager.SuppressGates = false;
        V3Write("cardv3_speed.tsv", sb);
        yield return V3End();
    }

    // ===================================================================== B
    IEnumerator V3Dps()
    {
        L("== B: DPS / ボス ==");
        var chars = Arg("-qaV3DpsChars", "swordsman,dual_blade,noble_lady,dragon_lancer,fighter,gunslinger,archer,mage").Split(',');
        float secs = float.Parse(Arg("-qaV3Secs", "10"), IC);
        var builds = new (string name, string preset, int lv)[] { ("none", null, 0), ("mix5", "mix", 5), ("mix9", "mix", 9), ("attack9", "attack", 9), ("extreme9", "extreme", 9), ("boss9", "boss", 9), ("fire9", "fire", 9), ("lightning9", "lightning", 9), ("wind9", "wind", 9), ("speed9", "speed", 9) };
        var sb = new StringBuilder("char\tbuild\tdps\tratio\thitMul\tnote\n");
        foreach (string ch in chars)
        {
            yield return V3Begin(ch, "wasteland_road");
            V3FlatStretch();
            stopKeepAlive = true;
            var tm = TerrainManager.Instance; if (tm != null) tm.enemySpawnChance = 0f;
            float baseDps = 0f;
            foreach (var b in builds)
            {
                foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
                yield return null;
                V3Reset();
                if (b.preset != null) foreach (var id in CardBuildPresets.Find(b.preset)) V3Apply(id, b.lv);
                V3LivesSet.Invoke(gm, new object[] { gm.maxLives });
                float hitMul = pc.EffectiveAttackPower / (float)Mathf.Max(1, pc.AttackPower);
                var dummy = HaSpawn(1.6f);
                if (dummy == null) continue;
                HaEnemyHp.SetValue(dummy, 10000000);
                // その場で殴り続ける(自動前進を止め、ダミーは同じ間合いへ戻す)。HP の減りを毎フレーム足す(属性/追加攻撃のダメージも含む)
                pc.autoRunEnabled = false;
                float slot = dummy.transform.position.x - pc.transform.position.x;
                int prevHp = (int)HaEnemyHp.GetValue(dummy);
                long dealtL = 0;
                float t0 = Time.time, lastFlick = 0f;
                while (Time.time - t0 < secs)
                {
                    yield return null;
                    if (gm.Lives < gm.maxLives) V3LivesSet.Invoke(gm, new object[] { gm.maxLives });
                    SetPrivate(pc, "hitInvincibleTimer", 0f);
                    if (Time.time - lastFlick > 0.12f) { lastFlick = Time.time; StartCoroutine(Flick(PlayerController.FlickDirection.Forward)); }
                    if (dummy == null || dummy.IsDying)
                    {
                        dummy = HaSpawn(slot);
                        if (dummy != null) { HaEnemyHp.SetValue(dummy, 10000000); prevHp = 10000000; }
                        continue;
                    }
                    int hpNow = (int)HaEnemyHp.GetValue(dummy);
                    if (hpNow < prevHp) dealtL += prevHp - hpNow;
                    prevHp = hpNow;
                    float want = pc.transform.position.x + slot;
                    var tmz = TerrainManager.Instance;
                    if (!(bool)HaEnemyLaunched.GetValue(dummy) && Mathf.Abs(dummy.transform.position.x - want) > 0.5f && tmz != null && tmz.GetHeightAt(want).HasValue)
                        dummy.transform.position = new Vector3(want, dummy.transform.position.y, dummy.transform.position.z);
                }
                pc.autoRunEnabled = true;
                float dps = dealtL / secs;
                if (dps <= 0f) { L($"[B:{ch}] DIAG {b.name}: dummy={(dummy != null)} pos=({pc.transform.position.x:F1},{pc.transform.position.y:F1}) grounded={pc.IsGrounded}"); Shot($"v3dps_zero_{ch}_{b.name}"); }
                if (b.name == "none") baseDps = Mathf.Max(1f, dps);
                string note = b.preset != null ? $"{b.preset} Lv{b.lv}" : "no cards";
                sb.AppendLine($"{ch}\t{b.name}\t{dps:F1}\t{dps / Mathf.Max(1f, baseDps):F2}\t{hitMul:F2}\t{note}");
                L($"[B:{ch}] {b.name,-10} dps {dps,7:F1}  x{dps / Mathf.Max(1f, baseDps):F2}  (1 hit x{hitMul:F2})");
                V3Write("cardv3_dps.tsv", sb);
            }
            stopKeepAlive = false;
            yield return V3End();
        }
        // 1発の倍率(条件ごと、黒剣士)
        yield return V3Begin("swordsman", "natural_cave");
        var hm = new StringBuilder("build\tground\tair\tfirst\tfinisher\tboss\tlowHp25\tbestMoment\n");
        foreach (var b in builds)
        {
            V3Reset();
            if (b.preset != null) foreach (var id in CardBuildPresets.Find(b.preset)) V3Apply(id, b.lv);
            float A = pc.CardAttackFactor;
            System.Func<EffectType, float> T = t => gm.Card.Get(t);
            float gr = A * CardRules.CondMultiplier(T(EffectType.GroundPct) + (gm.Lives >= gm.maxLives ? T(EffectType.FullHpPct) : 0f));
            float ai = A * CardRules.CondMultiplier(T(EffectType.AirPct));
            float fi = A * CardRules.CondMultiplier(T(EffectType.GroundPct) + T(EffectType.FirstPct));
            float fn = A * CardRules.CondMultiplier(T(EffectType.GroundPct) + T(EffectType.FinisherPct));
            float bo = A * CardRules.CondMultiplier(T(EffectType.GroundPct) + T(EffectType.BossPct));
            float lo = A * CardRules.CondMultiplier(T(EffectType.GroundPct) + T(EffectType.LowHp50Pct) + T(EffectType.LowHp25Pct) + gm.SealedHearts * T(EffectType.SealedHeartPct));
            float best = A * CardRules.CondMultiplier(Mathf.Max(T(EffectType.GroundPct), T(EffectType.AirPct)) + Mathf.Max(T(EffectType.FirstPct), T(EffectType.FinisherPct)) + T(EffectType.BossPct) + T(EffectType.LowHp50Pct) + T(EffectType.LowHp25Pct) + T(EffectType.MomentumPct) + T(EffectType.OverdrivePct) + gm.SealedHearts * T(EffectType.SealedHeartPct));
            hm.AppendLine($"{b.name}\t{gr:F2}\t{ai:F2}\t{fi:F2}\t{fn:F2}\t{bo:F2}\t{lo:F2}\t{best:F2}");
            L($"[B] hit multipliers {b.name,-10}: ground x{gr:F2} air x{ai:F2} first x{fi:F2} finisher x{fn:F2} boss x{bo:F2} lowHP x{lo:F2} best moment x{best:F2}");
        }
        V3Write("cardv3_hitmul.tsv", hm);
        yield return V3End();
        if (Arg("-qaV3NoBoss", "0") != "1") yield return V3Boss();
    }

    IEnumerator V3Boss()
    {
        var fights = new (float d, WildBossKind kind)[] { (1000f, WildBossKind.Wolf), (10000f, WildBossKind.Serpent) };
        var builds = new (string name, string preset, int lv)[] { ("none", null, 0), ("mix5", "mix", 5), ("attack9", "attack", 9), ("boss9", "boss", 9) };
        var sb = new StringBuilder("char\tboss\tbuild\tkilled\tsecs\thits\tavg\n");
        foreach (string ch in Arg("-qaV3BossChars", "swordsman,archer,noble_lady").Split(','))
        {
            foreach (var f in fights)
            foreach (var b in builds)
            {
                yield return V3Begin(ch);
                var bm = BossManager.Instance; bm.enabled = true;
                BossManager.SuppressGates = true;
                V3Reset();
                if (b.preset != null) foreach (var id in CardBuildPresets.Find(b.preset)) V3Apply(id, b.lv);
                WarpTo(f.d - 60f);
                yield return new WaitForSeconds(1.2f);
                foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
                bm.NetTestSpawnWild(f.kind, 1);
                WildBossBase boss = null; float w = 0f;
                while (boss == null && w < 5f) { yield return null; w += Time.unscaledDeltaTime; boss = FindObjectsByType<WildBossBase>(FindObjectsSortMode.None).FirstOrDefault(x => !x.IsDead); }
                if (boss == null) { Warn($"boss {f.kind} not spawned"); yield return V3End(); continue; }
                int last = boss.Hp, hits = 0; long dealt = 0;
                float t0 = Time.time, lastFlick = 0f;
                while (!boss.IsDead && Time.time - t0 < 90f)
                {
                    yield return null;
                    if (boss.Hp < last) { hits++; dealt += last - boss.Hp; last = boss.Hp; }
                    if (Time.time - lastFlick > 0.15f)
                    {
                        lastFlick = Time.time;
                        float dx = boss.CenterWorld.x - pc.transform.position.x, dy = boss.CenterWorld.y - pc.transform.position.y;
                        StartCoroutine(Flick(dy > 2.2f ? PlayerController.FlickDirection.Up : dx < -0.5f ? PlayerController.FlickDirection.Backward : PlayerController.FlickDirection.Forward));
                    }
                }
                float s = Time.time - t0;
                string res = $"{ch}\t{f.kind}@{f.d / 1000f:0}km\t{b.name}\t{(boss.IsDead ? "yes" : "no")}\t{s:F1}\t{hits}\t{(hits > 0 ? dealt / hits : 0)}";
                sb.AppendLine(res);
                L($"[B:boss] {res.Replace('\t', ' ')}");
                V3Write("cardv3_boss.tsv", sb);
                BossManager.SuppressGates = false;
                yield return V3End();
            }
        }
    }

    // ===================================================================== E
    // EXP(2026-10-04): 1つの枠 + 曲線。100,000m までの到達Lvを、今の式(ゲームの値)と変更前の式(掛け合わせ、曲線なし)で比べる。
    // 撃破の数は仮定(-qaV3KillsPerKm、既定 20体/km)。取得は決めた順のカードを Lv9 まで(それ以外は EXP に関係ないカード)。
    static readonly int[] V3ExpMarks = { 10000, 30000, 50000, 70000, 100000 };

    IEnumerator V3Exp()
    {
        L("== E: EXP / MILE(100,000mまで。式はゲームの値、撃破の数は仮定) ==");
        yield return V3Begin("swordsman");
        float killsPerKm = float.Parse(Arg("-qaV3KillsPerKm", "20"), IC);
        // EXP を持つカード(最新の99枚から抽出)
        var expCards = CardDatabase.AllCards.Where(c => c.effects.Any(e => e.type == EffectType.ExpGain || e.type == EffectType.DistanceExpPct || e.type == EffectType.KillExpPct)).ToList();
        L($"[E] EXP cards ({expCards.Count}): " + string.Join(", ", expCards.Select(c => $"{c.cardName}[{string.Join("+", c.effects.Where(e => e.type == EffectType.ExpGain || e.type == EffectType.DistanceExpPct || e.type == EffectType.KillExpPct).Select(e => $"{e.type}{e.value * 100f:+0;-0}%"))}]")));
        string[] full = { "exp_up", "level_break", "the_long_road", "experience_burst", "pathfinder", "long_haul", "one_more_mile" };
        var configs = new (string name, string[] order, int cap)[]
        {
            ("EXPなし", new string[0], 9),
            ("EXP UP Lv1", new[] { "exp_up" }, 1),
            ("EXP UP Lv5", new[] { "exp_up" }, 5),
            ("EXP UP Lv9", new[] { "exp_up" }, 9),
            ("EXP 2枚(UP+LEVEL BREAK)", new[] { "exp_up", "level_break" }, 9),
            ("EXP 3枚(+THE LONG ROAD)", new[] { "exp_up", "level_break", "the_long_road" }, 9),
            ("EXP 全特化(7枚)", full, 9),
            ("MILE特化", new[] { "executioner", "treasure_hunter", "one_more_mile", "mob_killer", "elite_enemies", "wanted", "exp_converter" }, 9),
            ("Challenge大量", CardBuildPresets.Find("challenge"), 9),
        };
        var sb = new StringBuilder("config\tformula\t10km\t30km\t50km\t70km\t100km\tmultAt100km\tmile10km\tmile50km\tmile100km\n");
        var newAt = new Dictionary<string, int[]>();
        foreach (var cfg in configs)
        {
            foreach (bool oldFormula in new[] { false, true })
            {
                V3Reset();
                int level = 1; float exp = 0f, toNext = gm.expBaseForLevel2;
                var cardLv = new Dictionary<string, int>();
                float mileEnemy = 0f, mileBoss = 0f;
                var marks = new List<int>(); var miles = new List<float>();
                float multEnd = 1f;
                for (int m = 100; m <= 100000; m += 100)
                {
                    float g = gm.Card.Get(EffectType.ExpGain), d = gm.Card.Get(EffectType.DistanceExpPct), k = gm.Card.Get(EffectType.KillExpPct);
                    float md = oldFormula ? Mathf.Max(0.25f, 1f + g) * (1f + d) : gm.ExpMultDistance;
                    float mk = oldFormula ? Mathf.Max(0.25f, 1f + g) * (1f + k) : gm.ExpMultKill;
                    multEnd = md;
                    exp += 100f * gm.expPerMeter * md + killsPerKm * 0.1f * gm.enemyKillExp * mk + (m % 1000 == 0 ? gm.bossKillExp * mk : 0f);
                    mileEnemy += killsPerKm * 0.1f * gm.MileGainMultiplier;
                    if (m % 1000 == 0) mileBoss += 50f * gm.BossMileGainMultiplier;
                    while (exp >= toNext)
                    {
                        exp -= toNext; level++;
                        toNext = gm.expBaseForLevel2 + gm.expGrowthPerLevel * (level - 1);
                        foreach (var id in cfg.order)
                        {
                            cardLv.TryGetValue(id, out int lvNow);
                            if (lvNow >= cfg.cap) continue;
                            cardLv[id] = lvNow + 1;
                            V3Apply(id, 1);
                            break;
                        }
                    }
                    if (System.Array.IndexOf(V3ExpMarks, m) >= 0) { marks.Add(level); miles.Add(m / 100f * Mathf.Max(0f, 1f + gm.Card.Get(EffectType.DistanceMilePct)) + mileEnemy + mileBoss); }
                }
                string f = oldFormula ? "変更前" : "今";
                sb.AppendLine($"{cfg.name}\t{f}\t{string.Join("\t", marks)}\t{multEnd:F2}\t{miles[0]:F0}\t{miles[2]:F0}\t{miles[4]:F0}");
                L($"[E] {cfg.name,-24} {f,-3} Lv at 10/30/50/70/100km: {string.Join("/", marks)}  distance EXP x{multEnd:F2} at 100km  MILE 10/50/100km: {miles[0]:F0}/{miles[2]:F0}/{miles[4]:F0}");
                if (!oldFormula) newAt[cfg.name] = marks.ToArray();
            }
        }
        V3Write("cardv3_exp.tsv", sb);
        int[] none = newAt["EXPなし"], up9 = newAt["EXP UP Lv9"], all = newAt["EXP 全特化(7枚)"];
        Check(up9[4] >= 100 && up9[4] <= 116, $"EXP UP Lv9 alone stays about the same (100km Lv{up9[4]}, target 100-115)");
        Check(all[0] < 60, $"EXP full build no longer finishes the deck by 10km (10km Lv{all[0]}, was 111)");
        Check(all[4] >= 140 && all[4] <= 210 && all[4] > up9[4] + 20, $"EXP full build is still clearly faster (100km Lv{all[4]} vs EXP UP Lv9 {up9[4]} vs none {none[4]})");
        // 取得順を入れ替えても、最終の EXP 倍率は同じ
        var orders = new[] { new[] { "exp_up", "level_break", "long_haul", "exp_converter", "pathfinder", "experience_burst" }, new[] { "experience_burst", "long_haul", "pathfinder", "exp_up", "exp_converter", "level_break" } };
        var res = new List<(float, float)>();
        foreach (var o in orders) { V3Reset(); foreach (var id in o) V3Pick.Invoke(gm, new object[] { id }); foreach (var id in o) for (int i = 0; i < 4; i++) V3Pick.Invoke(gm, new object[] { id }); res.Add((gm.ExpMultDistance, gm.ExpMultKill)); }
        L($"[E] order A: distance x{res[0].Item1:F4} kill x{res[0].Item2:F4} / order B: distance x{res[1].Item1:F4} kill x{res[1].Item2:F4}");
        Check(Mathf.Approximately(res[0].Item1, res[1].Item1) && Mathf.Approximately(res[0].Item2, res[1].Item2), "EXP multiplier does not depend on the order cards were taken");
        // EXP CONVERTER と組み合わせても 0以下 / NaN にならない
        V3Reset(); V3Apply("exp_converter", 9); V3Apply("exp_converter", 9); V3Apply("pathfinder", 1);
        Check(gm.ExpMultDistance >= 0.25f && gm.ExpMultKill >= 0.25f && !float.IsNaN(gm.ExpMultDistance) && !float.IsNaN(gm.ExpMultKill), $"EXP CONVERTER + others: floor x0.25 kept (x{gm.ExpMultDistance:F2} / x{gm.ExpMultKill:F2})");
        yield return V3End();
    }

    // ===================================================================== W
    // Android 用の長距離の確認(DEBUGパネル → 長距離の確認): DEBUG RUN で 30km へ、Attack特化を付けて。保存を汚さないこと
    IEnumerator V3LongDebug()
    {
        L("== W: 長距離の確認(DEBUG RUN) ==");
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        float best0 = gm.BestDistance;
        EndgameDebug.LaunchLong("wasteland_road", 30000f, EndgameDebug.LongBuild.Attack, EndgameDebug.Profile.Sturdy);
        yield return new WaitForSecondsRealtime(1f);
        w = 0f;
        while ((EndgameDebug.Instance.Launching || GameManager.Instance == null || !GameManager.Instance.HasStarted) && w < 40f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance; pc = PlayerController.Instance;
        yield return new WaitForSeconds(2f);
        int hp1 = DistanceTierManager.Instance != null ? DistanceTierManager.Instance.EnemyHpFor(1f) : 0;
        L($"[W] launched: debugRun={DebugRun.IsActive} '{DebugRun.What}' d={gm.MaxDistance:F0} atk x{pc.CardAttackFactor:F2} enemyHp(x1)={hp1} status='{EndgameDebug.Instance.Status}'");
        Check(DebugRun.IsActive && gm.MaxDistance >= 30000f && gm.MaxDistance < 31000f, "long-distance check starts a DEBUG RUN at 30km");
        Check(pc.CardAttackFactor > 2f && hp1 >= 200, "the chosen build is applied and the enemies have the 30km HP");
        yield return new WaitForSeconds(8f);
        int enemies = FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Length;
        L($"[W] after 8s: d={gm.MaxDistance:F0} enemies={enemies} blockedSaves={DebugRun.BlockedWrites}");
        var old = gm;
        gm.Retry();
        w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        gm = GameManager.Instance;
        L($"[W] back home: debugRun={DebugRun.IsActive} restoredKeys={DebugRun.LastRestoredKeys} best {best0:F0} -> {gm.BestDistance:F0}");
        Check(!DebugRun.IsActive && DebugRun.LastRestoredKeys == 0 && Mathf.Approximately(best0, gm.BestDistance), "the DEBUG RUN ended without changing BEST / progress");
    }

    // ===================================================================== L
    // 長距離の雑魚の硬さ(2026-10-04、雑魚HPは変えていない): 距離ごとの HP、地上の1発で必要な数、実際に殴って倒すまでの時間(TTK)。
    // TTK は硬いまま同じ位置に並べた3体(AI なし)を殴り続け、3体とも倒れるまでの秒数 ÷ 3。攻撃速度/追加の1撃/属性(継続/範囲)も入る。
    IEnumerator V3Ttk()
    {
        L("== L: 長距離の雑魚の硬さ(HP / 必要な発数 / TTK) ==");
        float[] dists = { 0f, 10000f, 30000f, 50000f, 70000f, 100000f };
        string[] enemyIds = Arg("-qaV3TtkEnemies", "goblin,heavy_ogre").Split(',');
        var builds = new (string name, string preset, int lv)[] { ("カードなし", null, 0), ("一般 Lv9", "mix", 9), ("Attack特化 Lv9", "attack", 9), ("炎特化 Lv9", "fire", 9), ("雷特化 Lv9", "lightning", 9), ("極端Risk Lv9", "extreme", 9) };
        float cap = float.Parse(Arg("-qaV3TtkCap", "20"), IC);
        var sb = new StringBuilder("char\tenemy\tdistance\thp\tbuild\thit\thitsNeeded\tttkPerEnemy\tkilled\n");
        foreach (string ch in Arg("-qaV3TtkChars", "swordsman").Split(','))
        {
            yield return V3Begin(ch, "wasteland_road");
            V3FlatStretch();
            var dtm = DistanceTierManager.Instance;
            foreach (var b in builds)
            {
                V3Reset();
                if (b.preset != null) foreach (var id in CardBuildPresets.Find(b.preset)) V3Apply(id, b.lv);
                foreach (string eid in enemyIds)
                {
                    var def = EnemyDatabase.FindById(eid);
                    if (def == null) { Warn($"unknown enemy {eid}"); continue; }
                    foreach (float d in dists)
                    {
                        int hp = Mathf.Max(1, Mathf.RoundToInt((1 + dtm.baseHpBonus + Mathf.FloorToInt(d / Mathf.Max(1f, dtm.hpIncreaseDistance))) * CombatScale.K * def.hpMultiplier));
                        foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) Destroy(e.gameObject);
                        yield return null;
                        V3LivesSet.Invoke(gm, new object[] { gm.maxLives });
                        int hit = pc.EffectiveAttackPower;
                        int need = Mathf.CeilToInt(hp / (float)Mathf.Max(1, hit));
                        var targets = new List<EnemyController>();
                        var slots = new List<float>();
                        for (int i = 0; i < 3; i++)
                        {
                            var en = HaSpawn(1.4f + i * 1.0f, eid);
                            if (en == null) continue;
                            en.maxHp = hp; HaEnemyHp.SetValue(en, hp);
                            targets.Add(en); slots.Add(en.transform.position.x - pc.transform.position.x);
                        }
                        pc.autoRunEnabled = false;
                        float t0 = Time.time, lastFlick = 0f;
                        while (Time.time - t0 < cap && targets.Any(t => t != null && !t.IsDying))
                        {
                            yield return null;
                            if (gm.Lives < gm.maxLives) V3LivesSet.Invoke(gm, new object[] { gm.maxLives });
                            SetPrivate(pc, "hitInvincibleTimer", 0f);
                            if (Time.time - lastFlick > 0.12f) { lastFlick = Time.time; StartCoroutine(Flick(PlayerController.FlickDirection.Forward)); }
                            for (int i = 0; i < targets.Count; i++)
                            {
                                var t = targets[i];
                                if (t == null || t.IsDying || (bool)HaEnemyLaunched.GetValue(t)) continue;
                                float want = pc.transform.position.x + slots[i];
                                var tmz = TerrainManager.Instance;
                                if (Mathf.Abs(t.transform.position.x - want) > 0.5f && tmz != null && tmz.GetHeightAt(want).HasValue) t.transform.position = new Vector3(want, t.transform.position.y, t.transform.position.z);
                            }
                        }
                        pc.autoRunEnabled = true;
                        int killed = targets.Count(t => t == null || t.IsDying);
                        float secs = Time.time - t0;
                        float per = killed > 0 ? secs / killed : float.PositiveInfinity;
                        sb.AppendLine($"{ch}\t{eid}\t{d / 1000f:0}km\t{hp}\t{b.name}\t{hit}\t{need}\t{(killed > 0 ? per.ToString("F2", IC) : ">" + cap.ToString("F0", IC))}\t{killed}/{targets.Count}");
                        L($"[L:{ch}] {eid,-10} {d / 1000f,3:0}km HP{hp,5} {b.name,-14} 1発{hit,4} 必要{need,3}発  TTK {(killed > 0 ? per.ToString("F2") + "s" : ">" + cap + "s")} ({killed}/{targets.Count})");
                        V3Write("cardv3_ttk.tsv", sb);
                    }
                }
            }
            yield return V3End();
        }
    }

    // ===================================================================== C
    IEnumerator V3Perf()
    {
        L("== C: Challenge 大量 + 追加攻撃 全部 ==");
        var sb = new StringBuilder("case\tsecs\tmaxLiving\tavgMs\tmaxMs\tp99Ms\tgc\tprocs\tprocDrops\tfxDrops\telites\twanted\texceptions\n");
        foreach (var (name, presets, lv) in new (string, string[], int)[] { ("なし", new string[0], 0), ("Challenge+追加攻撃 Lv9", new[] { "challenge", "procs" }, 9) })
        {
            yield return V3Begin("swordsman");
            if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);
            BossManager.SuppressGates = true; // ボスの関門(1kmごと)で雑魚が止まらないように
            WarpTo(6400f);
            yield return new WaitForSeconds(1.0f);
            V3Reset();
            foreach (var p in presets) foreach (var id in CardBuildPresets.Find(p)) V3Apply(id, lv);
            ChallengeSystem.ResetCounters();
            int exc0 = exceptions.Count;
            int gc0 = System.GC.CollectionCount(0);
            var frames = new List<float>();
            int maxLiving = 0;
            float t0 = Time.time, lastFlick = 0f;
            float secs = float.Parse(Arg("-qaV3PerfSecs", "60"), IC);
            while (Time.time - t0 < secs)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000f);
                maxLiving = Mathf.Max(maxLiving, ChallengeSystem.LivingEnemies);
                if (Time.time - lastFlick > 0.15f) { lastFlick = Time.time; StartCoroutine(Flick(PlayerController.FlickDirection.Forward)); }
            }
            frames.Sort();
            float avg = frames.Count > 0 ? frames.Average() : 0f, max = frames.Count > 0 ? frames[frames.Count - 1] : 0f, p99 = frames.Count > 0 ? frames[Mathf.Min(frames.Count - 1, (int)(frames.Count * 0.99f))] : 0f;
            int procs = CardProcs.Shockwaves + CardProcs.ChainExplosions + CardProcs.Infernos + CardProcs.Tornados + CardProcs.DoubleAttacks + ElementSystem.LightningHits;
            string line = $"{name}\t{secs:F0}\t{maxLiving}\t{avg:F1}\t{max:F1}\t{p99:F1}\t{System.GC.CollectionCount(0) - gc0}\t{procs}\t{CardProcs.ProcBudgetDrops}\t{CardProcs.FxBudgetDrops}\t{ChallengeSystem.EliteSpawned}\t{ChallengeSystem.WantedSpawned}\t{exceptions.Count - exc0}";
            sb.AppendLine(line);
            L($"[C] {line.Replace('\t', ' ')}  spawnHeldByCap {ChallengeSystem.SpawnsHeldByCap}");
            Check(exceptions.Count == exc0, $"{name}: no exceptions while running with every proc and challenge card");
            Check(maxLiving <= CardRules.MaxLivingEnemiesForSpawn + 16, $"{name}: living enemies stay near the safety cap ({maxLiving})");
            yield return V3End();
        }
        V3Write("cardv3_perf.tsv", sb);
    }

    // ===================================================================== R
    IEnumerator V3Continue()
    {
        L("== R: CONTINUE(カードの状態) ==");
        yield return V3Begin("swordsman");
        stopKeepAlive = true;
        WarpTo(3000f);
        yield return new WaitForSecondsRealtime(1.0f);
        var hist = (List<CardDefinition>)typeof(GameManager).GetField("upgradeHistory", NP).GetValue(gm);
        foreach (var id in new[] { "heart_up", "heart_up", "berserker", "berserker", "berserker", "glass_cannon", "shield", "shield", "phoenix", "phoenix", "attack_up", "speed_up", "flame_blade" })
            V3Pick.Invoke(gm, new object[] { id });
        // PHOENIX を使って取り直す
        int per = CombatScale.HpPerHeart;
        pc.CardTestSetShield(0); // Shield で防がれないように
        SetPrivate(pc, "hitInvincibleTimer", 0f);
        V3LivesSet.Invoke(gm, new object[] { per });
        var pr = gm.TryDamagePlayer(true, "QaV3", CombatScale.PlayerHeavyHit);
        L($"[R] lethal hit -> {pr}, phoenix used {gm.PhoenixConsumedCount}");
        Check(gm.PhoenixConsumedCount == 1 && !gm.IsGameOver, "PHOENIX revived before the CONTINUE check");
        V3Pick.Invoke(gm, new object[] { "phoenix" });
        V3LivesSet.Invoke(gm, new object[] { Mathf.Max(per, gm.maxLives - per) });
        typeof(GameManager).GetMethod("SaveCheckpoint", NP).Invoke(gm, null);
        string Sig() => $"lives={gm.Lives}/{gm.maxLives} sealed={gm.SealedHearts} phoenix={gm.PhoenixCharges}/Lv{gm.CardLevel("phoenix")}/used{gm.PhoenixConsumedCount} shieldCap={pc.ShieldCapacity} atk={pc.CardAttackFactor:F3} kmh={KmhAtNaturalCap:F1} burn={gm.Elements.BurnChance:F2}";
        string before = Sig();
        L($"[R] before home: {before}");
        var old = gm;
        gm.ReturnToHome();
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f);
        gm = GameManager.Instance;
        w = 0f; float retry = 0f;
        while (gm.ResumeGate != GameManager.ResumeGatePhase.Waiting && w < 12f)
        {
            if (!gm.HasStarted && retry <= 0f) { gm.ContinueActiveRun(); retry = 0.5f; }
            yield return null; w += Time.unscaledDeltaTime; retry -= Time.unscaledDeltaTime;
        }
        pc = PlayerController.Instance;
        string after = Sig();
        L($"[R] after CONTINUE: {after}");
        Check(before == after, "CONTINUE restores sealed HP / PHOENIX use and re-take / Shield / card values exactly");
        gm.RequestResumeFromGate();
        yield return new WaitForSecondsRealtime(4f);
        stopKeepAlive = false;
        yield return V3End();
    }
}
#endif
