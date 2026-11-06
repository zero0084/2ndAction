using System.Collections.Generic;
using UnityEngine;

// マップ専用カード 20 枚(2026-11-06。Docs/Cards_2026-11-06/map_cards_v2.md)の数値と、いくつかの処理の入口。
//  数値は資料の Lv1 / Lv5 / Lv9 を直線でつなぐ(Lv1→5 と Lv5→9 をそれぞれ等分。At3)。上限・間隔は資料どおり。
//  状態は 2 か所: プレイヤーの強化/一時シールド/空中の数え = PlayerController.MapCards、ランの状態(血の盾/1ランに1回/撃破数/間隔)= GameManager.MapCards。
//  表示は CardBehaviorText、文は LocCardMap、効果の表は Editor/CardBalanceV3(MapTable)。
//  シナジーの上限(無限にしない): 一時シールドは合計 3 枚・時間で消える / 血の盾は最大 3 ハート・10 秒で 1 つ減る /
//   回復は終盤の回復の倍率(HealScaled)を通す / IRON HEART は上限 +50% / SHIELD BURST は 3 秒以上の間隔 /
//   ELEMENTAL CHAOS と ELEMENTAL FOCUS は高い方だけ / ENDLESS GROWTH は上限 +40%
public static class MapCardRules
{
    // ---- Lv と直線の補間
    public static int Lv(EffectType t)
    {
        var gm = GameManager.Instance;
        return gm != null ? Mathf.Clamp(Mathf.RoundToInt(gm.Card.Get(t)), 0, 9) : 0;
    }
    // Lv1 = a / Lv5 = b / Lv9 = c(間は直線)
    public static float At3(int lv, float a, float b, float c)
    {
        lv = Mathf.Clamp(lv, 1, 9);
        return lv <= 5 ? a + (b - a) * (lv - 1) / 4f : b + (c - b) * (lv - 5) / 4f;
    }
    // 枚数/回数(1 / 2 / 3 の段)は切り捨て(Lv1〜4 = 1、Lv5〜8 = 2、Lv9 = 3)
    public static int Steps3(int lv, int a, int b, int c) => Mathf.FloorToInt(At3(lv, a, b, c) + 1e-4f);

    // ================================================================ 自然洞窟
    // LAST BASTION: HP30% 以下になった瞬間に一時シールド(8秒)。再発動までの秒
    public const float LastBastionHpFraction = 0.30f, LastBastionShieldSeconds = 8f;
    public static int LastBastionShields(int lv) => Steps3(lv, 1, 2, 3);
    public static float LastBastionCooldown(int lv) => At3(lv, 90f, 75f, 60f);
    // OVERFLOW WARD: 最大HPを超えた回復の割合 → 一時シールド(ハート1つ分 = 1枚)。最大数・20秒で消える・変換は3秒に1回
    public const float OverflowShieldSeconds = 20f, OverflowConvertInterval = 3f;
    public static float OverflowPct(int lv) => At3(lv, 0.30f, 0.50f, 0.70f);
    public static int OverflowMax(int lv) => Steps3(lv, 1, 2, 3);
    // 一時シールドの合計の上限(LAST BASTION / OVERFLOW WARD で共通。通常の Shield とは別に数える)
    public const int TempShieldTotalCap = 3;
    // REVENGE ARMOR: ハート2つ以上の被弾の後の秒・攻撃。再発動まで 8 秒
    public const float RevengeCooldown = 8f;
    public static float RevengeSeconds(int lv) => At3(lv, 3f, 4f, 5f);
    public static float RevengeAttack(int lv) => At3(lv, 0.20f, 0.30f, 0.40f);
    // BREAK FEAST: BREAK させるとハート回復。20 秒に1回
    public const float BreakFeastCooldown = 20f;
    public static int BreakFeastHearts(int lv) => Steps3(lv, 1, 2, 3);
    // IRON HEART: 5ハートを超えた最大HP 1ハートにつき 回復量・Shield の回復の速さ。上限あり
    public const int IronHeartBaseHearts = 5;
    public static float IronHeartPerHeart(int lv) => At3(lv, 0.02f, 0.03f, 0.04f);
    public static float IronHeartCap(int lv) => At3(lv, 0.30f, 0.40f, 0.50f);
    public static float IronHeartBonus(int lv, int maxLives)
    {
        if (lv <= 0) return 0f;
        int over = Mathf.Max(0, maxLives / CombatScale.HpPerHeart - IronHeartBaseHearts);
        return Mathf.Min(IronHeartCap(lv), over * IronHeartPerHeart(lv));
    }

    // ================================================================ 天空回廊
    // AIR COMBO: 着地せずに N 回当てると 3 秒 攻撃
    public const float AirComboSeconds = 3f;
    public static int AirComboHits(int lv) => Mathf.CeilToInt(At3(lv, 5f, 4f, 3f) - 1e-4f);
    public static float AirComboAttack(int lv) => At3(lv, 0.10f, 0.18f, 0.26f);
    // SKY BREAKER: 空中の攻撃の BREAK の溜まり
    public static float SkyBreaker(int lv) => At3(lv, 0.15f, 0.35f, 0.55f);
    // DIVE BOOST: 急降下で着地した後 2.5 秒
    public const float DiveBoostSeconds = 2.5f;
    public static float DiveSpeed(int lv) => At3(lv, 0.10f, 0.18f, 0.26f);
    public static float DiveAttack(int lv) => At3(lv, 0.15f, 0.27f, 0.39f);
    // LAUNCH MASTER: 打ち上げた敵への空中の攻撃・落下をゆっくり(重力を下げる。入力の受付/攻撃の判定の時刻は変えない)
    public static float LaunchAttack(int lv) => At3(lv, 0.12f, 0.28f, 0.44f);
    public static float LaunchSlow(int lv) => At3(lv, 0.10f, 0.20f, 0.30f);
    // 打ち上げた敵(雑魚 EnemyController / ボスの BossJuggle)の落ちる時の重力の倍率
    public static float LaunchFallMul { get { int lv = Lv(EffectType.LaunchMasterLevel); return lv > 0 ? 1f - LaunchSlow(lv) : 1f; } }
    // SONIC EDGE: 130km/h 以上(PlayerController.CurrentRunKmh)
    public const float SonicEdgeKmh = 130f;
    public static float SonicAttack(int lv) => At3(lv, 0.15f, 0.31f, 0.47f);
    public static float SonicBreak(int lv) => At3(lv, 0.10f, 0.26f, 0.42f);

    // ================================================================ ラストダンジョン
    // SECOND RISE: 1ランに1回、HP25% 以下で最大HPの割合を回復 + 8 秒 攻撃・移動速度
    public const float SecondRiseHpFraction = 0.25f, SecondRiseSeconds = 8f;
    public static float SecondRiseHeal(int lv) => At3(lv, 0.30f, 0.45f, 0.60f);
    public static float SecondRiseBuff(int lv) => At3(lv, 0.20f, 0.30f, 0.40f);
    // SHIELD BURST: Shield(一時シールド含む)が壊れると半径 3m に攻撃力の倍率・ボスの BREAK。間隔
    public const float ShieldBurstRadius = 3f;
    public static float ShieldBurstPower(int lv) => At3(lv, 3f, 5f, 7f);
    public static float ShieldBurstBreak(int lv) => At3(lv, 0.05f, 0.10f, 0.15f);
    public static float ShieldBurstInterval(int lv) => At3(lv, 6f, 4.5f, 3f);
    // BLOOD ARMOR: 吸収の超過の割合 → 血の盾(ハート単位)。最大・10 秒で1つ減る。血の盾がある間 出血の確率
    public const float BloodShieldDecaySeconds = 10f;
    public static float BloodArmorPct(int lv) => At3(lv, 0.50f, 0.75f, 1.00f);
    public static int BloodArmorMax(int lv) => Steps3(lv, 1, 2, 3);
    public static float BloodArmorBleed(int lv) => At3(lv, 0.10f, 0.20f, 0.30f);
    // ELEMENTAL CHAOS: デッキの属性の種類(2〜5)で属性の確率・威力全体(割合)
    public static float ChaosBonus(int lv, int kinds)
    {
        if (lv <= 0 || kinds < 2) return 0f;
        switch (Mathf.Min(kinds, 5))
        {
            case 2: return At3(lv, 0.06f, 0.08f, 0.10f);
            case 3: return At3(lv, 0.12f, 0.16f, 0.20f);
            case 4: return At3(lv, 0.20f, 0.26f, 0.32f);
            default: return At3(lv, 0.30f, 0.40f, 0.50f);
        }
    }
    // ELEMENTAL FOCUS: デッキの属性が1種類なら、その属性の威力(割合)・確率(足す)。2種類なら半分、3種類以上は無し
    public static float FocusPower(int lv, int kinds) => lv <= 0 || kinds <= 0 || kinds >= 3 ? 0f : At3(lv, 0.30f, 0.50f, 0.70f) * (kinds == 1 ? 1f : 0.5f);
    public static float FocusChance(int lv, int kinds) => lv <= 0 || kinds <= 0 || kinds >= 3 ? 0f : At3(lv, 0.10f, 0.20f, 0.30f) * (kinds == 1 ? 1f : 0.5f);
    // BREAK DOMINION: BREAK 中の敵へ
    public static float DominionAttack(int lv) => At3(lv, 0.15f, 0.31f, 0.47f);
    public static float DominionElement(int lv) => At3(lv, 0.10f, 0.26f, 0.42f);
    public static float DominionReward(int lv) => At3(lv, 0.10f, 0.26f, 0.42f);
    // ENDLESS GROWTH: 撃破ごと(そのランだけ)・上限
    public static float GrowthPerKill(int lv) => At3(lv, 0.002f, 0.003f, 0.004f);
    public static float GrowthCap(int lv) => At3(lv, 0.15f, 0.27f, 0.40f);
    // FINAL DRIVE: 80km 以降
    public const float FinalDriveFromMeters = 80000f;
    public static float FinalSpeed(int lv) => At3(lv, 0.10f, 0.15f, 0.20f);
    public static float FinalAttack(int lv) => At3(lv, 0.30f, 0.50f, 0.70f);
    public static float FinalBreak(int lv) => At3(lv, 0.20f, 0.40f, 0.60f);
    // LAST GAMBLE(危険の側。Lv で変わらない。報酬は既存の MILE/EXP の種類、密度は EnemySpawnRate を表で持つ)
    public const float GambleExtraHeartChance = 0.30f, GambleHeavyChance = 0.10f, GambleGapMul = 0.70f, GambleDensity = 0.15f;
    // ONE MORE CHANCE: 1ランに1回、倒れるダメージでハート1つ残る(1.5 秒無敵)→ 10 秒の強化
    public const float OneMoreInvincible = 1.5f, OneMoreSeconds = 10f;
    public static float OneMoreAttack(int lv) => At3(lv, 0.40f, 0.70f, 1.00f);
    public static float OneMoreSpeed(int lv) => At3(lv, 0.15f, 0.22f, 0.30f);
    public static float OneMoreBreak(int lv) => At3(lv, 0.30f, 0.55f, 0.80f);

    // ---- 確認用の数え(開発版の QA が読む)
    public static int TempShieldsGiven, TempShieldsUsed, OverflowConversions, RevengeTriggers, RevengeReduced, BreakFeastHeals, AirComboTriggers,
        DiveBoosts, SecondRises, ShieldBursts, BloodShieldGained, BloodShieldUsed, GambleExtraHearts, OneMoreChances, BossBreaksSeen, DominionKills;

    // ================================================================ BREAK(ボス)の溜まり
    // 各ボスの AddStagger から(WildBossBase / DragonController / MajinController)。プレイヤーの今の状態で倍率
    public static float BreakGainMul
    {
        get
        {
            var pc = PlayerController.Instance;
            return pc != null ? 1f + Mathf.Max(0f, pc.MapBreakBonus) : 1f;
        }
    }
    // BREAK した瞬間(各ボスの BreakCount++ の所)→ BREAK FEAST
    public static void OnBossBreak()
    {
        BossBreaksSeen++;
        var gm = GameManager.Instance;
        if (gm != null) gm.MapOnEnemyBreak();
    }

    // BREAK 中か(ボスの本体/当たり判定)。JOIN のパペットは HOST の BREAK を BossJuggle(写し)が開いているかで見る
    public static bool IsBroken(Component c)
    {
        if (c is BossHurtbox h) c = h.owner;
        switch (c)
        {
            case WildBossBase b: return b.Broken || (b.NetPuppet && b.Juggle != null && b.Juggle.Open);
            case DragonController d: return d.Broken || (d.NetPuppet && d.Juggle != null && d.Juggle.Open);
            case MajinController m: return m.Broken || (m.NetPuppet && m.Juggle != null && m.Juggle.Open);
        }
        return false;
    }

    // JOIN のパペット(毎フレーム): HOST の BREAK が始まった(写しの BossJuggle が開いた)瞬間 → この端末の BREAK FEAST。
    //  HOST の端末では本物のボスの AddStagger が OnBossBreak を呼ぶ(パペットは呼ばない)ので、二重には数えない
    public static int PuppetBreaksSeen; // 確認用
    public static void PuppetBreakTick(ref bool wasOpen, BossJuggle j)
    {
        bool open = j != null && j.Open;
        if (open && !wasOpen) { PuppetBreaksSeen++; OnBossBreak(); }
        wasOpen = open;
    }

    // ================================================================ マルチ
    // HP を決める端末(ソロ/HOST)の被弾の入口が要るカードのうち、JOIN では成り立たないもの。マルチのランでは候補に出さず、持っていても効かない
    //  ONE MORE CHANCE: 倒れる判定は HOST(協力は DOWN/復活、対戦は脱落)。LAST GAMBLE: 危険(敵の攻撃 +1 ハート/高火力敵/判断イベント/敵の密度)が
    //  HOST の世界と HOST の被弾にしか掛からず、JOIN は報酬だけになる。#100 ULTIMATE(マルチ未対応)と同じ扱い
    public static bool SoloOnly(string cardId) => cardId == "one_more_chance" || cardId == "last_gamble";
    public static bool Offerable(CardDefinition c) => c == null || !SoloOnly(c.cardId) || !NetMatch.Active;
    public static bool Multiplayer => NetRunLauncher.IsMultiplayerRun;
    // 打ち上げた敵か(雑魚の Launched / ボスの BREAK の打ち上げで浮いている)
    public static bool IsLaunched(Component c)
    {
        if (c is BossHurtbox h) c = h.owner;
        switch (c)
        {
            case EnemyController e: return e.AerialState == EnemyController.EnemyAerialState.Launched;
            case WildBossBase b: return b.Juggle != null && b.Juggle.Open && b.Juggle.Airborne;
            case DragonController d: return d.Juggle != null && d.Juggle.Open && d.Juggle.Airborne;
            case MajinController m: return m.Juggle != null && m.Juggle.Open && m.Juggle.Airborne;
        }
        return false;
    }

    // BREAK DOMINION: BREAK 中の敵へ当てた時刻(撃破の報酬で「BREAK 中に倒した」を判定する。とどめの一撃が BREAK 中の命中)
    public static float LastBrokenHitAt = -99f;
    public static void NoteHit(Component victim) { if (IsBroken(victim)) LastBrokenHitAt = Time.time; }
    // ボス撃破の MILE/EXP の倍率(RegisterBossDefeat)。1回使ったら消す
    public static float TakeBrokenKillMul()
    {
        int lv = Lv(EffectType.BreakDominionLevel);
        bool broken = Time.time - LastBrokenHitAt < 1.0f;
        LastBrokenHitAt = -99f;
        if (lv <= 0 || !broken) return 1f;
        DominionKills++;
        return 1f + DominionReward(lv);
    }
    // 属性/カードの追加攻撃のダメージ(ElementSystem.DealQuiet)。BREAK 中の敵へ属性の威力
    public static int ElementDamageOnBroken(Component victim, int dmg, ElementType el)
    {
        if (el == ElementType.None || dmg <= 0) return dmg;
        int lv = Lv(EffectType.BreakDominionLevel);
        if (lv <= 0 || !IsBroken(victim)) return dmg;
        return Mathf.Max(1, Mathf.RoundToInt(dmg * (1f + DominionElement(lv))));
    }

    // ================================================================ 属性(ELEMENTAL CHAOS / ELEMENTAL FOCUS)
    // デッキの属性カードの種類(炎/氷/雷/風/血)。デッキはランの間は変わらないので、0.5 秒ごとに数え直すだけ
    static float deckCountAt = -99f;
    static int deckKinds; static int deckMask;
    public static int DeckElementKinds { get { Refresh(); return deckKinds; } }
    public static int DeckElementMask { get { Refresh(); return deckMask; } }
    public static void InvalidateDeck() { deckCountAt = -99f; }
    static void Refresh()
    {
        if (Time.unscaledTime - deckCountAt < 0.5f && deckCountAt > 0f) return;
        deckCountAt = Time.unscaledTime;
        deckMask = 0;
        var gm = GameManager.Instance;
        if (gm != null)
            foreach (var key in gm.DeckCards)
            {
                var c = CardDatabase.FindBaseById(GameManager.DeckKindOf(key));
                if (c != null && c.element != ElementType.None) deckMask |= 1 << (int)c.element;
            }
        int n = 0; for (int m = deckMask; m != 0; m >>= 1) n += m & 1;
        deckKinds = n;
    }
    // 属性ごとの威力・確率(高い方だけ: CHAOS と FOCUS の威力を比べて大きい方を使う)
    static void ElementBonus(ElementType el, out float powerMul, out float chanceMul, out float chanceAdd)
    {
        powerMul = 1f; chanceMul = 1f; chanceAdd = 0f;
        int chaos = Lv(EffectType.ElementalChaosLevel), focus = Lv(EffectType.ElementalFocusLevel);
        if (chaos <= 0 && focus <= 0) return;
        int kinds = DeckElementKinds;
        bool inDeck = (DeckElementMask & (1 << (int)el)) != 0;
        float cb = ChaosBonus(chaos, kinds);
        float fp = inDeck ? FocusPower(focus, kinds) : 0f, fc = inDeck ? FocusChance(focus, kinds) : 0f;
        if (fp > cb) { powerMul = 1f + fp; chanceAdd = fc; }
        else if (cb > 0f) { powerMul = 1f + cb; chanceMul = 1f + cb; }
    }
    public static float ElementChance(ElementType el, float baseChance)
    {
        if (baseChance <= 0f) return baseChance; // 確率の無い属性は発動させない(足すのは持っている属性だけ)
        ElementBonus(el, out _, out float mul, out float add);
        return baseChance * mul + add;
    }
    public static float ElementPower(ElementType el)
    {
        ElementBonus(el, out float p, out _, out _);
        return p;
    }

    // ================================================================ LAST GAMBLE(危険の側)
    public static bool GambleOn => Lv(EffectType.LastGambleLevel) > 0 && !Multiplayer; // マルチでは効かない(SoloOnly)
    public static float GambleHeavyBonus => GambleOn ? GambleHeavyChance : 0f;   // 高火力敵(CardRewards.HeavyChanceBonus に足す)
    public static float GambleGapMulNow => GambleOn ? GambleGapMul : 1f;         // 判断イベントの間隔(CardRewards.DecisionGapMul に掛ける)
    // 敵の攻撃(雑魚/ボス/弾/死神)だけ。落下・障害物・壁・天井の棘(事故)は変えない
    public static bool IsEnemyAttackReason(string reason)
    {
        if (string.IsNullOrEmpty(reason)) return false;
        string r = reason.StartsWith("HPZero:") ? reason.Substring(7) : reason;
        if (r.StartsWith("DeathY") || r.StartsWith("Fall") || r.StartsWith("Obstacle") || r.StartsWith("TerrainWall") || r.StartsWith("CeilingSpike") || r.StartsWith("Accident")) return false;
        return r.StartsWith("Enemy") || r.StartsWith("Boss") || r.StartsWith("WildBoss") || r.StartsWith("CaveBoss") || r.StartsWith("Dragon")
            || r.StartsWith("SkyStrike") || r.StartsWith("Majin") || r.StartsWith("Mech") || r.StartsWith("Fireball") || r.StartsWith("Net") || r.StartsWith("Reaper");
    }
    public static int GambleExtra(int dmg, string reason)
    {
        if (!GambleOn || !IsEnemyAttackReason(reason)) return dmg;
        if (Random.value >= GambleExtraHeartChance) return dmg;
        GambleExtraHearts++;
        return dmg + CombatScale.HpPerHeart;
    }

    // ---- 確認用: 数えを 0 へ
    public static void ResetCounters()
    {
        TempShieldsGiven = TempShieldsUsed = OverflowConversions = RevengeTriggers = RevengeReduced = BreakFeastHeals = AirComboTriggers = 0;
        DiveBoosts = SecondRises = ShieldBursts = BloodShieldGained = BloodShieldUsed = GambleExtraHearts = OneMoreChances = BossBreaksSeen = DominionKills = 0;
    }
}
