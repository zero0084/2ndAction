using UnityEngine;

// 終盤の硬さの調整(2026-10-11、攻略の再検証)。調整の単位ごとに切り替えられるようにまとめる(比べてから採用するため)。
//  ・LateEnemyHp: 雑魚の HP の距離による伸びを、LateHpFrom より先は LateHpEvery ごとに +1段(それまでは hpIncreaseDistance ごと)にする。
//    序盤(LateHpFrom まで)の雑魚は変わらない。ボスの HP は別(BossHpPlan/再戦)。
//  ・AttackUpHighLevel: ATTACK UP の高い Lv の伸び。Lv1〜3 は今までどおり(+5%/Lv)、Lv4 から伸びを大きくする(AttackUpLevelFactor)。
//  ・BossFrontReturn: ボスがプレイヤーの後ろに張り付いたままにならない(下の説明)。
//  ・AssistFlyingBosses: 自動の補助がドラゴン/魔人も攻撃する。
// 開発版は起動引数 -balVariant base|hp|atk|boss|assist|combo|jump の組み合わせ(例 hp,atk,boss,assist,combo,jump = 全部)
// + お嬢様騎士の候補 nawake|narmor|nheavy|nreach / 格闘家の候補 fjumpで切り替えられる(base = 調整前)。保存はしない。
public static class BalanceTuning
{
    public static bool LateEnemyHp = true;    // 2026-10-11 採用(比較の結果: 終盤の普通の雑魚の必要な命中数 約-35%)
    public static float LateHpFrom = 20000f;   // ここまでは今までどおり(hpIncreaseDistance = 2,000m ごとに +1段)
    public static float LateHpEvery = 4000f;   // ここから先は 4,000m ごとに +1段

    public static bool AttackUpHighLevel = true; // 2026-10-11 採用(Lv9 の攻撃力 +45% → +80%)
    // 値(0.05)に掛ける数。Lv1〜3 は今までどおり(1,2,3)、Lv9 で 16(= +80%)
    static readonly float[] AttackUpLevelTable = { 0f, 1f, 2f, 3f, 4.6f, 6.2f, 7.8f, 10.5f, 13.2f, 16f };
    public static float AttackUpLevelFactor(int lv) => AttackUpLevelTable[Mathf.Clamp(lv, 0, AttackUpLevelTable.Length - 1)];

    // ボスの位置取り(2026-10-11): 荒野/洞窟/天空のボスがプレイヤーの後ろ(前攻撃が届かない所)に張り付いたままになる /
    // ドラゴン・魔人の間合いが踏み込み等で少しずつ後ろへずれていく → 前へ戻す
    public static bool BossFrontReturn = true;
    public static float BossBehindSeconds = 3f;   // 後ろにこれだけ続いたら前へ回り込む
    public static float BossReturnSpeed = 16f;    // 回り込む速さ(m/秒、走る速さに足す)
    public static float FlyerDriftDeadZone = 2f;  // ドラゴン/魔人: 本来の間合いからのずれがこれを超えた分を
    public static float FlyerDriftRate = 0.6f;    //   1秒あたりこの割合で戻す(一瞬の踏み込みにはすぐ付いていかない)

    // ===== にらみ合いの修正(2026-10-16、マスターの依頼) =====
    // 再戦の最終強化(特殊攻撃の間隔 4.2秒)では、特殊攻撃そのものの長さ(古代の魔王の床と天井の魔法 4.3秒 等)が間隔より長く、
    // 終わった時には次の特殊攻撃がもう使える → 寄って攻撃する普通の技が一度も選ばれず、ボスが登場の位置(9m 前)から動かなかった。
    // BossSpecialBreather: 特殊攻撃を続けて使う時は、間に普通の技を必ず1回はさむ(間隔そのものは変えない)
    public static bool BossSpecialBreather = true;
    // AssistApproachLunge: 自動の補助(近接)は、届く距離(7m)のすぐ外で待つボスにも、踏み込みの大きい前攻撃で寄る
    // (以前は「届くのを待つ」だけで、ボスが寄ってこないと何分も攻撃しなかった)。手動の連打と同じ寄り方
    public static bool AssistApproachLunge = true;
    public static float AssistApproachWait = 1.5f;   // 届かないままこれだけ続いたら寄る
    public static float AssistApproachMax = 14f;     // これより遠いボスには寄らない(画面の外の相手は追わない)

    // 自動の補助がドラゴン/魔人(専用の作りのボス)も攻撃する(2026-10-11)。以前は WildBossBase 系だけを狙っていた
    public static bool AssistFlyingBosses = true;
    // 自動の補助が格闘家の連撃を続けて押す(猶予/予約の間も押す)(2026-10-11)
    public static bool AssistFighterCombo = true;
    // 自動の補助のジャンプの計算を、キャラの実際の跳び方(技のジャンプ)に合わせる(2026-10-11)
    public static bool AssistKitJumpModel = true;

    // お嬢様騎士の強化の候補(2026-10-11、PlayerController.Noble.cs)。比べてから採用する
    // 2026-10-11 採用(覚醒+鎧): 平地の比較(90km)でボスの撃破時間 ゴーレム 67→45秒 / グリフォン 152→90 / トロル 67→32(被弾 16→3回)、
    // 終盤の雑魚の取り逃し 11→3体。序盤(20km まで)の攻撃力と間合いは今までどおり
    public static bool NobleAwaken = true;         // 覚醒: 走った距離で攻撃力と大剣の間合いが伸びる
    public static float NobleAwakenFrom = 20000f, NobleAwakenTo = 80000f, NobleAwakenMax = 0.6f; // From まで1倍 → To で 1+Max 倍
    public static float NobleAwakenRange = 1.27f;  // 間合い: To で基礎(0.75)の 1.27倍 ≒ 0.95(グリフォンなど少し上のボスに届く)
    public static bool NobleArmor = true;          // 鎧: 振っている間の被弾で攻撃が止まらない + 受けるダメージ減
    public static float NobleArmorDamage = 0.75f;
    public static bool NobleHeavy = false;         // 大剣(比較用): 距離に関係なく攻撃力
    public static float NobleHeavyMul = 1.4f;
    // 格闘家の跳ぶ高さの候補(2026-10-11): ジャンプは昇拳(通常の 0.9倍、空中は さらに 0.95倍)。1 = 通常のジャンプと同じ高さ
    // 2026-10-11 採用: 80km 4km の地形の被害 段差/壁 5〜6 → 1回、落下 1〜6 → 0〜2回(補助の計算を合わせても低いジャンプでは越えられない段差が残った)
    public static bool FighterFullJump = true;
    public static bool NobleReach = false;         // 間合い(比較用)
    public static float NobleReachValue = 0.95f;   // 今は 0.75

    // ===== 格闘家/お嬢様の追加強化(2026-10-12)。-charTune base で全部切った状態(比較用)、-charTune fr,fk,ff,fa,ng,nm,nj で個別 =====
    // 格闘家: 近い間合いで連撃を当て切る強さ
    public static bool FighterReach = true;      // fr: 拳/蹴りの前方の届き ×1.2・上下の厚み +0.25m(小さな位置ずれで空振りしない)
    public static float FighterReachMul = 1.2f, FighterThickAdd = 0.25f;
    public static bool FighterKeepClose = true;  // fk: 1〜3段目の吹き飛ばしを弱める(連撃の途中で届かない所へ押し出さない)
    public static float FighterMidKnockMul = 0.45f;
    public static bool FighterFinisher = true;   // ff: 4段目(締め)の威力 2.2 → 3.0倍
    public static float FighterFinisherDamage = 3.0f;
    public static bool FighterAtk = false;       // fa: 基礎攻撃力(比較用。部分試験で足りなければ採用)
    public static float FighterAtkMul = 1.15f;
    // お嬢様: 重い大剣の一撃 + 手動で状況に対応する攻撃手段
    public static bool NobleGreatsword = true;   // ng: 大剣の見た目に合う間合い(基礎 0.75 → 1.0)と一撃の重さ(×1.6)
    public static float NobleGreatswordRange = 1.0f, NobleGreatswordDamage = 1.6f; // 威力: 部分試験(80km オーガ 5.75→4.50秒)で 1.2→1.6
    // nj: ジャンプ。お嬢様は 1回・高さ ×0.85 で、高速の穴/段差を越え切れず落下が格闘家の約2倍だった(補助の失敗理由の1位は
    // 「空中でジャンプ回数を使い切っている」)。部分試験(天空回廊 60km から 14km、地形の被害 落下+段差): 今 10/17回(敵なし/あり)、
    // 高さ 1.0 で 9/11回、2回(高さ 0.85 のまま)で 4/2回 → 重さの表現の高さは据え置き、回数だけ他のキャラと同じ 2回
    public static bool NobleJump = true;
    public static float NobleJumpForce = 0.85f; public static int NobleJumpCount = 2;
    // キャラの基本のジャンプ回数(カードの前)。キャラのデータの値を直接読まず、ここを通す(お嬢様の調整が効くように)
    public static int JumpCountFor(CharacterDefinition def) => def == null ? 1 : Mathf.Max(1, def.characterId == "noble_lady" && NobleJump ? NobleJumpCount : def.jumpCount);
    public static bool NobleMoves = true;        // nm: 空中の前/後フリック=大剣の薙ぎ払い / 空中の下フリック=振り下ろしながら急降下+着地の衝撃波

    // 魔法使いのボス戦(2026-10-12)。開発版は -mageFix base で両方切った状態(比較用)、-mageFix assist,safety で個別
    // assist: 自動の補助がボスに魔法弾が当たる段へ高度を合わせる(ジャンプ前提の判断を使わない、HighSpeedAssist.Mage.cs)
    // safety: ボス戦で高い段に浮いたまま攻撃しない時間が続いたら1段ずつ降りる(PlayerController.Mage.cs)
    public static bool AssistMageAltitude = true;
    public static bool MageHoverSafety = true;

    // ===== 双剣士/ルカの序盤(2026-11-07、初期デッキで 10km の関門まで届かない件。キャラ側の調整 + 自動の補助の予測の抜け) =====
    // 10km の大蛇そのもの(HP/攻撃)は変えない。開発版は -charFix base で全部切った状態(比較用)、-charFix lj,lp,lo,lb,la,jd,ds,hl で個別
    // --- ルカ ---
    // lj: 自動の補助の跳びの予測を実際の跳び方に合わせる。①ジャンプで前へ跳ぶ技の前進(ルカの飛びつき = 0.16秒で 1.4m)
    //     ②空中から始める計画(二段ジャンプの候補)は空中のジャンプの強さで(ルカは空中 ×0.9。以前は地上の強さで計算していた)
    //     予測と実際の着地のずれ(平地の地形試験 6km): 平均 1.44m → 0.28m
    public static bool AssistJumpLunge = true;
    // lp: ルカの猫パンチ(1・2段目)の判定の出ている時間 0.07 → 0.15秒・戻り 0.09 → 0.01秒、飛びかかり(3段目)は判定 0.14 → 0.34秒・戻り 0.3 → 0.1秒
    //     (1回の長さは同じ = 火力は同じ)。判定が短く、約 110km/h では間隔 3〜5m で並ぶ雑魚の2体目が戻りの間に体へ届き、
    //     一度も殴られないまま体当たりになっていた(接触した時の雑魚の HP は満タン)
    public static bool CatPawActive = true;
    public static float CatPawActiveAdd = 0.08f, CatPounceActiveAdd = 0.2f;
    // lo: ルカの猫パンチ(1・2段目)の威力 0.75 → 1.0(序盤の小型の雑魚を1発で)、飛びかかり 1.7 → 1.3(3段の合計はほぼ同じ = ボスへの火力は同じ)
    public static bool CatPawOneShot = true;
    public static float CatPawDamage = 1.0f, CatPounceDamage = 1.3f;
    // lb: ルカのボス戦の猫パンチの踏み込み 0.25 → 0.7m(PlayerController.CatComboLunge)
    public static bool CatBossLunge = true;
    public static float CatBossLungeDistance = 0.7f;
    // --- 双剣士 ---
    // ds: 双剣士の連撃はボスの崩し(BREAK のゲージ)が溜まりやすい(1発ごとの崩し ×1.5)。手数の多さを強みにする(個性)。
    //     双剣士は1発が軽く(攻撃力 黒剣士の半分・振りは 0.75倍の時間)、ボスへの火力が黒剣士の約 0.7倍で、10km の大蛇の戦いが長く受けるハートも多かった。
    //     BREAK の間はボスが攻撃せず、ダメージも 1.35倍(ボス側の値は変えない。この端末のプレイヤーの攻撃だけ)
    public static bool DualBladeStagger = true;
    public static float DualBladeStaggerMul = 1.5f;
    public static float CharBossStaggerScale(PlayerController pc) => pc != null ? CharStaggerScaleFor(pc.CharId) : 1f;
    // マルチ(2026-11-07): HOST が JOIN の攻撃を受け取った時も、その参加者のキャラで同じ倍率を掛ける(NetCombat → NetApplyRemoteHit)。
    //  JOIN 側は自分で崩しを足さない(パペットは HOST へ申告するだけ)= 二重にならない。最終の BREAK の値は HOST が決めて同期する
    public static float CharStaggerScaleFor(string charId) => DualBladeStagger && charId == "dual_blade" ? DualBladeStaggerMul : 1f;
    // --- 全キャラ(自動の補助/止まって戦うボス戦。手数の多い・速いキャラほど効く) ---
    // la: 自動の補助が空中で前攻撃してよいかの予測に、攻撃の踏み込みを入れる(以前は当たった時の滞空だけ)
    public static bool AssistAirAttackLunge = true;
    // jd: 自動の補助の予測に JUMP COUNT UP の空中ジャンプの減衰を入れる(以前は満額で予測。技のジャンプのキャラは対象外)
    public static bool AssistJumpDecay = true;
    // hl: 止まって戦うボス戦で、止まった所から 22〜30m より前へ攻撃の踏み込みで出ない(BossHalt.ForwardLungeLimit)
    public static bool HaltLungeLimit = true;

    // ===== 忍者/ペンタトゥットゥルー星人の 5km の関門(2026-11-07 第2、初期デッキで 5km のゴブリンライダーで毎回倒れる件) =====
    // ゴブリンライダー(HP/攻撃/AI)は変えない。開発版は -charFix2 base で全部切った状態(比較用)、-charFix2 ns,cs,co,bv,fb,ob,nj,nh,nb,hj で個別
    // 調査(tier_btrace、0.05秒ごとのボス戦の様子): 黒剣士は小さな踏み込みを重ねてボスの体の中に入り(半分以上の時間)、棍棒(顔の前 -0.8〜+1.8m)の
    //  外側にいるので 1戦の被弾 0。忍者は瞬身(3m × ボス戦の伸び 1.4〜2.6)でボスを通り抜けて背中側へ出る → ボスが振り向いて棍棒 /
    //  通り抜けを重ねて止まった所から 20m 以上前へ出て、止まって戦う間の踏み込みの上限(hl)で瞬身が動かなくなり空振り。
    //  ペンタは前の1・2段目に踏み込みが無く(0m)、ボスが寄ってくる顔の前 1.7m(棍棒の届く所)で殴り合い、棍棒 → 下がって魔法弾 の繰り返しで 2ハートずつ失っていた
    // ns: 忍者のボス戦の瞬身は、前にいるボスの体の中心で止まる(すり抜けない。斬撃・無敵・踏み込みの速さは同じ。雑魚は今までどおりすり抜け)
    public static bool NinjaBossStop = true;
    // cs: ペンタののびーるパンチ(1・2段目)はボス戦だけ少し踏み込む(0 → 0.9m。ボス戦の伸びが掛かる)。腕の届く長さ・威力は同じ。部分試験(4→5.6km 各3回、-charCloudStep): 0.6m 失うハート 2.7 / 0.9m 1.7 / 1.2m 1.7(棍棒 2.0→1.0→1.3回)
    public static bool CloudBossStep = true;
    public static float CloudBossStepDistance = 0.9f;
    // co: ペンタののびーるパンチ(1・2段目)の威力 0.85 → 1.0(序盤の小型の雑魚を1発で)、ぽかぽか(3段目)1.6 → 1.3(3段の合計は同じ)。ルカの lo と同じ考え方
    public static bool CloudPawOneShot = true;
    public static float CloudPawDamage = 1.0f, CloudPokaDamage = 1.3f;
    // --- 全キャラ(自動の補助の予測の抜け) ---
    // bv: 有効になったボス本体の判定(騎乗突撃 18m/秒 等)の迫る速さを実際の動きから測る(以前は 12m/秒 と決め打ちで、速い突進は跳ぶのが遅れて当たっていた)
    public static bool AssistBodySpeed = true;
    // fb: ボス戦で迫ってくる火球(FireballController。ゴブリンライダーの魔法弾など)を回避の対象に入れる(以前は BossProjectile だけを見ていて、火球は素通り)
    public static bool AssistFireball = true;
    // ob: 補助自身の入力が技の最中に先行入力として覚えられて後で出る間、補助の判断を止めない(以前は手動の操作と同じ扱いで最大 0.9秒止まり、
    //     その間は回避の跳躍も出なかった)。補助の判断が「跳ぶ」ならそちらを優先(PlayerController.ApplyHighSpeedAssist)
    public static bool AssistOwnBuffer = true;
    // nj: 自動の補助の跳びの予測に、忍者の跳躍斬りの前への跳び込み(0.18秒で 1.6m)を入れる(ルカの lj と同じ。以前は忍者だけ入っていなかった)
    public static bool AssistNinjaJumpLunge = true;
    // nh: 止まって戦うボス戦の踏み込みの上限(hl)で、忍者の瞬身がボスへ届かなくならない。瞬身がボスの体の中心で止まる時(ns)は、
    //     その止まる所が止まった所から LungeAheadMax + NinjaHaltExtra m 以内なら踏み込みを弱めない(忍者は瞬身そのものが間合いで、
    //     踏み込みが 0 になると斬撃が体の前 0.65m までしか届かず、8km のオオカミの戦いで1発も当たらないまま 4 回噛まれていた)
    public static bool NinjaHaltReach = true;
    public static float NinjaHaltExtra = 10f;
    // nb: 自動の補助がボス戦で後ろのボスへ後ろ攻撃する距離を、忍者は手裏剣の届く距離(最大 8m)に(以前は近接と同じ 3.2m)
    public static bool AssistNinjaBack = true;
    // hj: 止まって戦うボス戦の回避の跳躍の安全確認(BossJumpSafe)を、実際の前進(止まっている = ほぼ 0)と、着地までの長さの先読みで行う。
    //     以前は走る速さで予測し先読みが約 0.4秒しかなく、回避の跳躍の大半が「着地が先読み範囲外」で取りやめ(全キャラ)
    public static bool AssistHaltJump = true;
    // 人が操作しても自動の補助の強さを落とさない(2026-11-07、HighSpeedAssist.Strength)。開発版 -assistGraded 1 で以前(操作の後 4 秒は 100〜130km/h で段階的)
    public static bool AssistKeepStrength = true;

    public static string Describe => $"lateEnemyHp={(LateEnemyHp ? $"on(from {LateHpFrom:F0}m every {LateHpEvery:F0}m)" : "off")} attackUpHighLevel={(AttackUpHighLevel ? "on" : "off")} bossFrontReturn={(BossFrontReturn ? "on" : "off")} assistFlyingBosses={(AssistFlyingBosses ? "on" : "off")} assistFighterCombo={(AssistFighterCombo ? "on" : "off")} assistKitJumpModel={(AssistKitJumpModel ? "on" : "off")} noble={(NobleAwaken ? "awaken " : "")}{(NobleArmor ? "armor " : "")}{(NobleHeavy ? "heavy " : "")}{(NobleReach ? "reach" : "")} fighterFullJump={(FighterFullJump ? "on" : "off")}";

    // 雑魚の HP の距離の段(DistanceTierManager.CurrentEnemyHp)
    public static int HpSteps(float distance, float increaseDistance)
    {
        float inc = Mathf.Max(1f, increaseDistance);
        if (!LateEnemyHp || distance <= LateHpFrom) return Mathf.FloorToInt(distance / inc);
        return Mathf.FloorToInt(LateHpFrom / inc) + Mathf.FloorToInt((distance - LateHpFrom) / Mathf.Max(1f, LateHpEvery));
    }

    public static string DescribeChar => $"fighterReach={FighterReach} keepClose={FighterKeepClose} finisher={FighterFinisher} fighterAtk={FighterAtk} nobleGreatsword={NobleGreatsword} nobleMoves={NobleMoves} nobleJump={(NobleJump ? NobleJumpForce + "x" + NobleJumpCount : "off")}";

    static bool applied;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ApplyArgs()
    {
        if (applied) return;
        applied = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var a = QaArgs.All;
        for (int i = 0; i < a.Length - 1; i++)
        {
            if (a[i] != "-balVariant") continue;
            string v = a[i + 1];
            if (v == "base") { LateEnemyHp = false; AttackUpHighLevel = false; BossFrontReturn = false; AssistFlyingBosses = false; AssistFighterCombo = false; AssistKitJumpModel = false; NobleAwaken = false; NobleArmor = false; NobleHeavy = false; NobleReach = false; FighterFullJump = false; }
            else
            {
                LateEnemyHp = v.Contains("hp");
                AttackUpHighLevel = v.Contains("atk");
                BossFrontReturn = v.Contains("boss");
                AssistFlyingBosses = v.Contains("assist");
                AssistFighterCombo = v.Contains("combo");
                AssistKitJumpModel = v.Contains("jump");
                NobleAwaken = v.Contains("nawake");
                NobleArmor = v.Contains("narmor");
                NobleHeavy = v.Contains("nheavy");
                NobleReach = v.Contains("nreach");
                FighterFullJump = v.Contains("fjump");
            }
            Debug.Log("[BalanceTuning] variant " + v + ": " + Describe);
        }
        for (int i = 0; i < a.Length - 1; i++)
        {
            if (a[i] != "-charTune") continue;
            string v = a[i + 1];
            if (v == "base") { FighterReach = false; FighterKeepClose = false; FighterFinisher = false; FighterAtk = false; NobleGreatsword = false; NobleMoves = false; NobleJump = false; }
            else
            {
                FighterReach = v.Contains("fr"); FighterKeepClose = v.Contains("fk"); FighterFinisher = v.Contains("ff"); FighterAtk = v.Contains("fa");
                NobleGreatsword = v.Contains("ng"); NobleMoves = v.Contains("nm"); NobleJump = v.Contains("nj");
            }
            Debug.Log("[BalanceTuning] charTune " + v + ": " + DescribeChar);
        }
        for (int i = 0; i < a.Length - 1; i++)
        {
            if (a[i] == "-ldRushChoices") { BossManager.LdRushChoices = a[i + 1] != "0"; Debug.Log($"[BalanceTuning] ldRushChoices={BossManager.LdRushChoices}"); continue; } // 比較用(2026-10-16)
            if (a[i] != "-standoffFix") continue; // 比較用: base = 修正前 / breather / lunge / breather,lunge = 両方
            string v = a[i + 1];
            BossSpecialBreather = v.Contains("breather"); AssistApproachLunge = v.Contains("lunge");
            Debug.Log($"[BalanceTuning] standoffFix {v}: specialBreather={BossSpecialBreather} approachLunge={AssistApproachLunge}");
        }
        for (int i = 0; i < a.Length - 1; i++)
        {
            if (a[i] != "-charFix") continue; // 2026-11-07 双剣士/ルカ: base = 調整前 / lj,lp,lo,lb,ds,la,jd,hl を個別に
            string v = a[i + 1];
            AssistJumpLunge = v.Contains("lj"); CatPawActive = v.Contains("lp"); CatPawOneShot = v.Contains("lo"); AssistAirAttackLunge = v.Contains("la"); AssistJumpDecay = v.Contains("jd"); DualBladeStagger = v.Contains("ds"); CatBossLunge = v.Contains("lb"); HaltLungeLimit = v.Contains("hl");
            Debug.Log($"[BalanceTuning] charFix {v}: assistJumpLunge={AssistJumpLunge} catPawActive={CatPawActive} catPawOneShot={CatPawOneShot} airAttackLunge={AssistAirAttackLunge} jumpDecay={AssistJumpDecay} dualBladeStagger={DualBladeStagger} catBossLunge={CatBossLunge} haltLungeLimit={HaltLungeLimit}");
        }
        for (int i = 0; i < a.Length - 1; i++)
        {
            if (a[i] != "-charFix2") continue; // 2026-11-07 第2 忍者/ペンタ: base = 調整前 / ns,cs,co,bv,fb,ob,nj,nh,nb,hj を個別に
            string v = a[i + 1];
            NinjaBossStop = v.Contains("ns"); CloudBossStep = v.Contains("cs"); AssistBodySpeed = v.Contains("bv"); AssistFireball = v.Contains("fb"); AssistOwnBuffer = v.Contains("ob"); AssistNinjaJumpLunge = v.Contains("nj"); NinjaHaltReach = v.Contains("nh"); AssistNinjaBack = v.Contains("nb"); AssistHaltJump = v.Contains("hj"); CloudPawOneShot = v.Contains("co");
            Debug.Log($"[BalanceTuning] charFix2 {v}: ninjaBossStop={NinjaBossStop} cloudBossStep={CloudBossStep} assistBodySpeed={AssistBodySpeed} assistFireball={AssistFireball} assistOwnBuffer={AssistOwnBuffer} ninjaJumpLunge={AssistNinjaJumpLunge} ninjaHaltReach={NinjaHaltReach} ninjaBack={AssistNinjaBack} haltJump={AssistHaltJump} cloudPawOneShot={CloudPawOneShot}");
        }
        for (int i = 0; i < a.Length - 1; i++)
        {
            if (a[i] == "-assistGraded") { AssistKeepStrength = a[i + 1] == "0"; Debug.Log($"[BalanceTuning] assistKeepStrength={AssistKeepStrength}"); continue; }
            if (a[i] != "-mageFix") continue;
            string v = a[i + 1];
            AssistMageAltitude = v.Contains("assist"); MageHoverSafety = v.Contains("safety");
            Debug.Log($"[BalanceTuning] mageFix {v}: assistMageAltitude={AssistMageAltitude} mageHoverSafety={MageHoverSafety}");
        }
        // 数値の比較用(部分試験): -charNobleDmg 1.5 / -charFighterAtk 1.15 / -charFighterFin 3.0
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        for (int i = 0; i < a.Length - 1; i++)
        {
            if (a[i] == "-charNobleDmg") NobleGreatswordDamage = float.Parse(a[i + 1], inv);
            else if (a[i] == "-charFighterAtk") { FighterAtk = true; FighterAtkMul = float.Parse(a[i + 1], inv); }
            else if (a[i] == "-charFighterFin") FighterFinisherDamage = float.Parse(a[i + 1], inv);
            else if (a[i] == "-charNobleJump") { NobleJump = true; NobleJumpForce = float.Parse(a[i + 1], inv); }
            else if (a[i] == "-charNobleJumps") { NobleJump = true; NobleJumpCount = int.Parse(a[i + 1]); }
            else if (a[i] == "-charCloudStep") { CloudBossStepDistance = float.Parse(a[i + 1], inv); Debug.Log($"[BalanceTuning] cloudBossStep={CloudBossStepDistance}"); } // 2026-11-07 第2: ペンタのボス戦の踏み込み(比較用)
            else continue;
            Debug.Log($"[BalanceTuning] {a[i]} {a[i + 1]}: nobleDmg={NobleGreatswordDamage} fighterAtk={(FighterAtk ? FighterAtkMul : 1f)} fighterFin={FighterFinisherDamage}");
        }
#endif
    }
}
