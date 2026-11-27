using UnityEngine;

// ボス戦の停止戦(2026-10-29): ボス戦の間は自動前進と道中のスクロールを止め、戦闘範囲の中で戦う(闘技場の 0km/h と同じ形)。
// ゲーム全体の一時停止ではない(時間/攻撃/ジャンプ/重力/ノックバック/敵の行動は通常どおり)。
//  ・止めるのは「実際の前進」だけ。能力としての速さ(カードの効果・HUD の km/h・高速時の補助の判定)は PlayerController.AbilityRunSpeed で、ゼロにしない
//  ・Factor = 実際の前進の倍率(1 = 通常の走り / 0 = 停止)。戦闘の始まりは DecelSeconds で止まり、撃破後の報酬が終わって
//    ボスの遭遇が終わったら(BossManager.HoldsRun が false)AccelSeconds で走りへ戻る
//  ・対象: 距離を止めるボス戦(HoldsRun)。走りながらのボスラッシュ(ラン再開/90km 以降の連続の遭遇)、死神の追跡、
//    エンディングは対象外(Exempt)。開発用の闘技場は自分の速さ(ArenaAutoSpeed)
//  ・この端末のプレイヤーごと(マルチは各端末が自分のプレイヤーだけを止める。戦闘範囲に入った人から止まる: InRange)
public static class BossHalt
{
    public static float DecelSeconds = 0.7f;  // 走り → 停止
    public static float AccelSeconds = 0.9f;  // 停止 → 走り
    public static bool Enabled = true;        // 開発用の比較(-haltOff 1 で前の作り = 並走するボス戦)
    // 2026-11-25(マスター報告「ボス戦でたまに前進しっぱなし/これ以上進めない」): 止まって戦う範囲の作り直し。開発版は -haltArena 0 で前の作り
    //  ① 基準(AnchorX)は「止まり始めた所」ではなく「実際に止まった所」。以前は減速の間の滑り(100km/h で約10m)と、平らな所を探して
    //     走った分まで基準からの距離に入り、戦い始めた時点で踏み込みの上限(22〜30m)の近くにいた = 少し攻撃しただけで前へ出られない
    //  ② ソロ: ボスの体の手前の端が、止まった所から BossFrontMax m より前へ押し出されない。以前はプレイヤーの踏み込みがボスの体に入るたびに
    //     ボスが間合いを保つために前へ押し出され、それを追ってまた踏み込む…で戦いながら前へずれ続け(自動の試験でほぼ毎戦 25m 以上)、上限で止まった
    //  ③ 前にボスがいる間は、踏み込みの上限で止めない(ボスは②で範囲の中にいるので、届かない壁にならない)
    //  ④ 平らな所を探して走るのは、分岐/上の道が理由なら SeekRelaxAfter 秒まで(上の道のボスへは BreakRouteLift で寄る)。穴の時だけ探し続ける
    public static bool ArenaV2 = true;
    public static float BossFrontMax = 20f, SeekRelaxAfter = 2.5f;
    // 2026-11-27(マスター要望): マルチは HOST が決めた「全員が止まる共通の位置」へ、各自が残りの距離に合わせて減速してぴったり止まる。
    //  先に着いた人はそこで止まり(距離も止まる)、後ろの人はボス戦中も走って追いつき、横に1列(LineSpacing 間隔、プレイヤー番号の順)に並んで一緒に戦う。
    //  平らな所を探して走る(SafeToStop/Seek)のはソロだけ(共通の位置は HOST が穴/分岐の無い所に決めている)。開発版は -bossLine 0 で前の作り
    public static bool SharedStop = true;
    public static float LineSpacing = 1.2f;
    public static float BrakeDistance(float speed) => Mathf.Max(1.5f, Mathf.Max(1f, speed) * DecelSeconds * 0.5f); // 時間で止めた時と同じ滑り(等加速度で止まる)
    // この端末のプレイヤーが止まる所(論理X): 共通の位置から、番号の順に後ろへ並ぶ
    static double LineStopLX(double anchor) => anchor - LineSpacing * Mathf.Clamp(NetCombat.LocalPlayerNumber - 1, 0, 7);
    public static double MpStopLX { get; private set; } // 確認用: 最後に使った自分の止まる所
    static bool MpLine => SharedStop && ArenaV2 && NetRunLauncher.IsMultiplayerRun && (NetCombat.Authority || NetCombat.Replica);
    public static bool Stopped { get; private set; } // この遭遇で一度止まりきった(基準が決まった)

    public static float Factor { get; private set; } = 1f;
    public static bool Halted => Factor < 0.999f;
    public static bool Holding { get; private set; }   // 止める条件が成り立っている(ゆっくり止まっている最中も true)
    public static int Starts { get; private set; }     // 確認用: 止まり始めた回数
    public static int Releases { get; private set; }   // 確認用: 走りへ戻り始めた回数
    public static string LastReason { get; private set; } = "";
    // 戦闘範囲の目安(この端末の場面の X)。止まった所を中心に、ボスが動き回れる幅
    public static float AnchorX { get; private set; }
    public const float ArenaBehind = 9f, ArenaAhead = 22f;
    public static double AnchorLX { get; private set; } // 2026-11-07: 止まった所の論理X(浮動原点がずれても使える)

    // 2026-11-07(双剣士の調査): 止まって戦う間、攻撃の踏み込み(ボス戦では ×1.4〜2.6 に伸びる)で止まった所から前へ出すぎない。
    // ボスはプレイヤーとの間合いを保って下がるので、手数の多いキャラ(双剣士 = 振りが 0.75倍の時間)ほど踏み込みを重ねて前へずれ続け、
    // 1戦で 20〜30m(黒剣士/ルカは 14m 前後)、長引くと平らにしてある所(関門の先 55m まで、TerrainManager.BossArenaAfter)を越えて
    // 戦いの最中に穴/段差の壁に当たっていた(双剣士だけ、ボス戦の地形の被弾 1ラン平均 1〜2ハート)。
    // 止まった所から LungeAheadSoft m 先で踏み込みを弱め始め、LungeAheadMax m 先で前への踏み込みを 0 にする(後ろへの踏み込み/ノックバックはそのまま)
    public static float LungeAheadSoft = 22f, LungeAheadMax = 30f;
    public static double AheadOfAnchor(float x) => Holding ? x + FloatingOrigin.Offset - AnchorLX : 0.0; // 2026-11-07 第2: 止まった所からの前の距離(止めていない時は 0)
    public static float ForwardLungeLimit(float playerX)
    {
        if (!BalanceTuning.HaltLungeLimit || !Holding) return 1f;
        // ③ 前にボスの体がある間は止めない(ボスは ClampBossForward で範囲の中)
        if (ArenaV2 && Stopped && !NetRunLauncher.IsMultiplayerRun) { float f = BossBattle.NearestBossFrontAheadPublic(playerX); if (f != float.MaxValue && f > 0.5f) return 1f; }
        double ahead = playerX + FloatingOrigin.Offset - AnchorLX;
        if (ahead <= LungeAheadSoft) return 1f;
        return Mathf.Clamp01((float)((LungeAheadMax - ahead) / Mathf.Max(0.1f, LungeAheadMax - LungeAheadSoft)));
    }

    // その場で止める条件(外の仕組みが「止めない」を決める時に使う)
    public static System.Func<string> Exempt; // null 以外を返したら止めない(理由)

    static int tickFrame = -1;
    static float easeT = 1f; // 0..1 の進み(Factor は SmoothStep で)
    static bool wasHolding;

    static bool argsRead;
    static void ReadArgs()
    {
        argsRead = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-haltOff" && a[i + 1] == "1") Enabled = false; // 開発用の比較: 前の作り(並走するボス戦)
        for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-haltArena" && a[i + 1] == "0") ArenaV2 = false; // 2026-11-25 の作り直しの前と比べる
        for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-bossLine" && a[i + 1] == "0") SharedStop = false; // 2026-11-27 の共通の止まる位置の前と比べる
#endif
    }

    public static void ResetAll()
    {
        if (!argsRead) ReadArgs();
        NetCombat.ResetBossHold();
        if (Starts > 0) Debug.Log($"[HaltWatch] summary holds={Starts} " + BossHaltWatch.Summary());
        BossHaltWatch.ResetCounters();
        Factor = 1f; easeT = 1f; Holding = false; wasHolding = false; LastReason = ""; tickFrame = -1; Stopped = false; seekSince = -1f; relaxedThisHold = false;
    }

    // 1フレームに1回(この端末のプレイヤーの Update から)
    public static void Tick(PlayerController pc)
    {
        if (tickFrame == Time.frameCount) return;
        tickFrame = Time.frameCount;
        bool want = Wants(pc, out string why);
        if (want && !wasHolding) { Starts++; AnchorX = pc != null ? pc.transform.position.x : 0f; AnchorLX = AnchorX + FloatingOrigin.Offset; HoldStartLX = AnchorLX; Stopped = false; seekSince = -1f; relaxedThisHold = false; Debug.Log($"[BossHalt] hold ({why}) x={AnchorX:F1}"); fieldCleared = false; }
        if (!want) Stopped = false;
        // 止まった所(=戦闘範囲)に残った道中の雑魚/障害物を片付ける(報酬なし、煙だけ)。止まりきる少し前に1回
        if (want && !fieldCleared && Factor < 0.35f && pc != null) { fieldCleared = true; ClearField(pc.transform.position.x); }
        if (!want && wasHolding) { Releases++; Debug.Log($"[BossHalt] release ({LastReason} -> {why}) early local shown/promoted/cancelled={TelegraphLead.EarlyShown}/{TelegraphLead.EarlyPromoted}/{TelegraphLead.EarlyCancelled} net sent/recv/late={NetCombat.EarlyTeleSent}/{NetCombat.EarlyTeleReceived}/{NetCombat.EarlyTeleLate} remote shown/expired/cancel={RemoteForesight.Shown}/{RemoteForesight.Expired}/{RemoteForesight.Cancelled} hold sent/recv={NetCombat.BossHoldSent}/{NetCombat.BossHoldReceived}"); }
        if (want) LastReason = why;
        wasHolding = want; Holding = want;
        float dt = Time.deltaTime;
        // マルチ: 共通の位置(から自分の並び)まで、残りの距離に合わせて減速して止まる(等加速度: 速さ ∝ √残り)
        if (want && MpLine && pc != null)
        {
            double stop = LineStopLX(NetCombat.Authority ? NetCombat.HoldAnchorLX : NetCombat.RemoteHoldAnchorLX);
            MpStopLX = stop;
            double rem = stop - (pc.transform.position.x + FloatingOrigin.Offset);
            float brake = BrakeDistance(pc.AbilityRunSpeed);
            float f = rem <= 0.05 ? 0f : Mathf.Clamp01(Mathf.Sqrt((float)(rem / brake)));
            // 走りへ戻っている途中(減速の後にまた走り出す等)で急に速くならない: 上げる時は AccelSeconds で
            Factor = f < Factor ? f : Mathf.MoveTowards(Factor, f, dt / Mathf.Max(0.05f, AccelSeconds));
            if (Factor < 0.002f) Factor = 0f;
            easeT = Factor; SeekingFlat = false;
            if (!Stopped)
            {
                AnchorX = pc.transform.position.x; AnchorLX = AnchorX + FloatingOrigin.Offset;
                if (Factor <= 0f) { Stopped = true; Debug.Log($"[BossHalt] stopped x={AnchorX:F1} slide={AnchorLX - HoldStartLX:F1}m line={AnchorLX:F1}/{stop:F1} (shared {(NetCombat.Authority ? NetCombat.HoldAnchorLX : NetCombat.RemoteHoldAnchorLX):F1})"); }
            }
            BossHaltWatch.Tick(pc, want, why);
            return;
        }
        // 止まる所が安全か(足元と前後に穴が無い・上下ルートの分岐の中でない・上の足場の上でない)。危ない所では止まらずに進み、
        // 平らな所まで来てから止まる(関門の手前の地形は TerrainManager が平地にしているので、通常はすぐ止まる)
        bool unsafeHere = want && easeT > 0.0001f && !SafeToStop(pc, out string unsafeWhy) && !SeekRelaxed(unsafeWhy);
        if (unsafeHere) { SeekingFlat = true; SeekReason = unsafeWhyLast; if (seekSince < 0f) seekSince = Time.time; easeT = Mathf.MoveTowards(easeT, 1f, dt / Mathf.Max(0.05f, AccelSeconds)); SeekFrames++; }
        else if (want) { SeekingFlat = false; easeT = Mathf.MoveTowards(easeT, 0f, dt / Mathf.Max(0.05f, DecelSeconds)); }
        else { SeekingFlat = false; seekSince = -1f; easeT = Mathf.MoveTowards(easeT, 1f, dt / Mathf.Max(0.05f, AccelSeconds)); }
        Factor = Mathf.SmoothStep(0f, 1f, easeT);
        if (Factor < 0.002f) Factor = 0f;
        // ① 止まりきるまでは基準をプレイヤーに合わせ続ける(止まった所 = 基準)
        if (want && ArenaV2 && !Stopped && pc != null)
        {
            AnchorX = pc.transform.position.x; AnchorLX = AnchorX + FloatingOrigin.Offset;
            if (Factor <= 0f) { Stopped = true; Debug.Log($"[BossHalt] stopped x={AnchorX:F1} slide={AnchorLX - HoldStartLX:F1}m"); }
        }
        BossHaltWatch.Tick(pc, want, why); // 2026-11-25: 記録だけ(前進しっぱなし/進めないの調査)
    }
    static bool fieldCleared, holdAnchorTaken;
    public static int FieldEnemiesCleared, FieldObstaclesCleared; // 確認用
    // 戦闘範囲(プレイヤーの後ろ ArenaBehind 〜 前 ArenaAhead+8m)の雑魚と障害物。ボスは消さない。マルチは敵/障害物を全員で共有しているので触らない
    static void ClearField(float x)
    {
        if (NetRunLauncher.IsMultiplayerRun) return;
        float a = x - ArenaBehind, b = x + ArenaAhead + 8f;
        foreach (var e in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
        {
            if (e == null || !e.isActiveAndEnabled || e.GetComponent<BonusEnemy>() != null) continue;
            float ex = e.transform.position.x;
            if (ex < a || ex > b) continue;
            Puff(e.transform.position + Vector3.up * 0.6f);
            e.gameObject.SetActive(false); Object.Destroy(e.gameObject);
            FieldEnemiesCleared++;
        }
        for (int i = ObstacleController.All.Count - 1; i >= 0; i--)
        {
            var o = ObstacleController.All[i];
            if (o == null || o.Broken || !o.isActiveAndEnabled) continue;
            float ox = o.transform.position.x;
            if (ox < a || ox > b) continue;
            o.Break(o.transform.position + Vector3.up * 0.5f);
            FieldObstaclesCleared++;
        }
        if (FieldEnemiesCleared + FieldObstaclesCleared > 0) Debug.Log($"[BossHalt] field cleared x={x:F1}: enemies {FieldEnemiesCleared}, obstacles {FieldObstaclesCleared} (total)");
    }
    static void Puff(Vector3 at)
    {
        OneShotSpriteEffect.CreateTweened(OneShotSpriteEffect.SoftDotSprite(), at, new Color(0.85f, 0.85f, 0.9f, 0.9f), 0.45f, 0.6f, 2.2f, 0.9f, 0f, new Vector3(0f, 0.6f, 0f), 0f, RenderOrder.CombatFx, 0.1f);
    }

    public static double HoldStartLX { get; private set; } // 止まり始めた所(確認用。基準は止まりきった所)
    static float seekSince = -1f;
    static bool relaxedThisHold;
    static string unsafeWhyLast = "";
    // ④ 分岐/上の道が理由で探し続けて SeekRelaxAfter 秒たったら、その場で止まる(穴の上では止まらない)
    static bool SeekRelaxed(string why)
    {
        unsafeWhyLast = why;
        if (!ArenaV2 || why == "pit") return false;
        if (relaxedThisHold) return true; // 一度あきらめたら、この遭遇の間は分岐/上の道では探し直さない(減速の途中で毎フレーム見直して、また走り出していた)
        if (seekSince < 0f || Time.time - seekSince < SeekRelaxAfter) return false;
        SeekRelaxes++; relaxedThisHold = true;
        Debug.Log($"[BossHalt] stop without a flat place after {Time.time - seekSince:F1}s ({why})");
        return true;
    }
    public static int SeekRelaxes, BossFrontClamps; // 確認用

    // ② ソロの止まって戦うボス戦: ボスの体の手前の端を、止まった所から BossFrontMax m より前へ出さない(前へ動いた分だけ戻す。
    //  すでに範囲の外から入って来る/技でわざと離れる/BREAK/飛び去る などは呼ぶ側で除く)
    public static float ClampBossForward(float worldX, float worldXBefore, float halfWidth)
    {
        if (!ArenaV2 || !Holding || !Stopped || NetRunLauncher.IsMultiplayerRun) return worldX;
        float edge = (float)(AnchorLX - FloatingOrigin.Offset) + BossFrontMax + halfWidth;
        if (worldX <= edge || worldX <= worldXBefore) return worldX;
        BossFrontClamps++;
        return Mathf.Max(edge, worldXBefore);
    }

    public static bool SeekingFlat { get; private set; }
    public static string SeekReason { get; private set; } = "";
    public static int SeekFrames { get; private set; }

    // 止まるまでに進む分(減速の間)を見込んで、止まる辺りの前後に穴/分岐が無いか
    static bool SafeToStop(PlayerController pc, out string why)
    {
        why = "";
        var tm = TerrainManager.Instance;
        if (tm == null || pc == null) return true;
        float x = pc.transform.position.x;
        float v = pc.AbilityRunSpeed * Factor;
        float slide = v * DecelSeconds * 0.5f * easeT; // ここから止まるまでに進む距離の目安
        float a = x - 3f, b = x + slide + 8f; // 立つ所の前後(戦闘範囲の全部ではない。マルチは地形を平らにできないので、遠い穴は残る)
        if (pc.QaOnSky) { why = "on the upper route"; return false; }
        if (tm.FirstPitBetween(a, b).HasValue) { why = "pit"; return false; }
        if (tm.IsInBranchRoute(x + slide) || tm.IsInBranchRoute(b)) { why = "route branch"; return false; }
        return true;
    }

    static bool Wants(PlayerController pc, out string why)
    {
        why = "";
        if (!Enabled) { why = "off"; return false; }
        var gm = GameManager.Instance; var bm = BossManager.Instance;
        if (pc == null || gm == null || bm == null || !gm.HasStarted || gm.IsGameOver) { why = "no run"; return false; }
        if (ArenaMode.Active || TutorialMode.Active) { why = "arena"; return false; }
        // マルチ: JOIN は HOST から届いた「止まる遭遇の最中か」で決める。戦闘範囲(最前の人が関門に着いた所)より後ろの人は、着くまで走る
        // 2026-11-27: HOST との接続が切れた参加者(以後はソロと同じ)は、自分の端末のボス戦で止まる(残った HOST の合図で止まり続けない)
        bool mp = NetRunLauncher.IsMultiplayerRun && (NetCombat.Authority || NetCombat.Replica);
        bool holds = mp && !NetCombat.Authority ? NetCombat.RemoteBossHold : bm.HoldsRun;
        if (!holds) { why = "no boss hold"; holdAnchorTaken = false; return false; }
        if (mp)
        {
            if (NetCombat.Authority && !wasHolding && !holdAnchorTaken) { NetCombat.RefreshHoldAnchor(); holdAnchorTaken = true; } // 遭遇の最初のフレームに基準を決める(1フレーム遅れて前の値を使わない)
            double anchor = NetCombat.Authority ? NetCombat.HoldAnchorLX : NetCombat.RemoteHoldAnchorLX;
            double lx = pc.transform.position.x + FloatingOrigin.Offset;
            // 共通の位置へ並ぶ時: 自分の止まる所の手前(止まりきるのに要る距離 + ArenaBehind)までは普通に走る
            double behind = MpLine ? LineStopLX(anchor) - BrakeDistance(pc.AbilityRunSpeed) - ArenaBehind : anchor - ArenaBehind;
            if (lx < behind) { why = "behind the fight area"; return false; }
        }
        if (gm.EscapeConfirmed) { why = "escape"; return false; }  // 脱出の確定後は脱出の演出(自分の減速)に任せる
        var fin = ReaperFinaleBattle.Instance;
        if (fin != null && fin.Current != ReaperFinaleBattle.Stage.Done) { why = "sisters finale"; return false; } // 三姉妹との最終戦は並走のまま(攻めて突破)
        var ex = Exempt != null ? Exempt() : null;
        if (!string.IsNullOrEmpty(ex)) { why = ex; return false; }
        why = bm.BossMusicKey ?? "boss";
        return true;
    }
}
