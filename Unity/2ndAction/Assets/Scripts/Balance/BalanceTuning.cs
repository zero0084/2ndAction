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
            if (a[i] != "-standoffFix") continue; // 比較用: base = 修正前 / breather / lunge / breather,lunge = 両方
            string v = a[i + 1];
            BossSpecialBreather = v.Contains("breather"); AssistApproachLunge = v.Contains("lunge");
            Debug.Log($"[BalanceTuning] standoffFix {v}: specialBreather={BossSpecialBreather} approachLunge={AssistApproachLunge}");
        }
        for (int i = 0; i < a.Length - 1; i++)
        {
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
            else continue;
            Debug.Log($"[BalanceTuning] {a[i]} {a[i + 1]}: nobleDmg={NobleGreatswordDamage} fighterAtk={(FighterAtk ? FighterAtkMul : 1f)} fighterFin={FighterFinisherDamage}");
        }
#endif
    }
}
