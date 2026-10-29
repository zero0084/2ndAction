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

    public static float Factor { get; private set; } = 1f;
    public static bool Halted => Factor < 0.999f;
    public static bool Holding { get; private set; }   // 止める条件が成り立っている(ゆっくり止まっている最中も true)
    public static int Starts { get; private set; }     // 確認用: 止まり始めた回数
    public static int Releases { get; private set; }   // 確認用: 走りへ戻り始めた回数
    public static string LastReason { get; private set; } = "";
    // 戦闘範囲の目安(この端末の場面の X)。止まった所を中心に、ボスが動き回れる幅
    public static float AnchorX { get; private set; }
    public const float ArenaBehind = 9f, ArenaAhead = 22f;

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
#endif
    }

    public static void ResetAll()
    {
        if (!argsRead) ReadArgs();
        NetCombat.ResetBossHold();
        Factor = 1f; easeT = 1f; Holding = false; wasHolding = false; LastReason = ""; tickFrame = -1;
    }

    // 1フレームに1回(この端末のプレイヤーの Update から)
    public static void Tick(PlayerController pc)
    {
        if (tickFrame == Time.frameCount) return;
        tickFrame = Time.frameCount;
        bool want = Wants(pc, out string why);
        if (want && !wasHolding) { Starts++; AnchorX = pc != null ? pc.transform.position.x : 0f; Debug.Log($"[BossHalt] hold ({why}) x={AnchorX:F1}"); fieldCleared = false; }
        // 止まった所(=戦闘範囲)に残った道中の雑魚/障害物を片付ける(報酬なし、煙だけ)。止まりきる少し前に1回
        if (want && !fieldCleared && Factor < 0.35f && pc != null) { fieldCleared = true; ClearField(pc.transform.position.x); }
        if (!want && wasHolding) { Releases++; Debug.Log($"[BossHalt] release ({LastReason} -> {why}) early local shown/promoted/cancelled={TelegraphLead.EarlyShown}/{TelegraphLead.EarlyPromoted}/{TelegraphLead.EarlyCancelled} net sent/recv/late={NetCombat.EarlyTeleSent}/{NetCombat.EarlyTeleReceived}/{NetCombat.EarlyTeleLate} remote shown/expired/cancel={RemoteForesight.Shown}/{RemoteForesight.Expired}/{RemoteForesight.Cancelled} hold sent/recv={NetCombat.BossHoldSent}/{NetCombat.BossHoldReceived}"); }
        if (want) LastReason = why;
        wasHolding = want; Holding = want;
        float dt = Time.deltaTime;
        // 止まる所が安全か(足元と前後に穴が無い・上下ルートの分岐の中でない・上の足場の上でない)。危ない所では止まらずに進み、
        // 平らな所まで来てから止まる(関門の手前の地形は TerrainManager が平地にしているので、通常はすぐ止まる)
        if (want && easeT > 0.0001f && !SafeToStop(pc, out string unsafeWhy)) { SeekingFlat = true; SeekReason = unsafeWhy; easeT = Mathf.MoveTowards(easeT, 1f, dt / Mathf.Max(0.05f, AccelSeconds)); SeekFrames++; }
        else if (want) { SeekingFlat = false; easeT = Mathf.MoveTowards(easeT, 0f, dt / Mathf.Max(0.05f, DecelSeconds)); }
        else { SeekingFlat = false; easeT = Mathf.MoveTowards(easeT, 1f, dt / Mathf.Max(0.05f, AccelSeconds)); }
        Factor = Mathf.SmoothStep(0f, 1f, easeT);
        if (Factor < 0.002f) Factor = 0f;
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
        bool mp = NetRunLauncher.IsMultiplayerRun;
        bool holds = mp && !NetCombat.Authority ? NetCombat.RemoteBossHold : bm.HoldsRun;
        if (!holds) { why = "no boss hold"; holdAnchorTaken = false; return false; }
        if (mp)
        {
            if (NetCombat.Authority && !wasHolding && !holdAnchorTaken) { NetCombat.RefreshHoldAnchor(); holdAnchorTaken = true; } // 遭遇の最初のフレームに基準を決める(1フレーム遅れて前の値を使わない)
            double anchor = NetCombat.Authority ? NetCombat.HoldAnchorLX : NetCombat.RemoteHoldAnchorLX;
            double lx = pc.transform.position.x + FloatingOrigin.Offset;
            if (lx < anchor - ArenaBehind) { why = "behind the fight area"; return false; }
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
