using System.Collections;
using UnityEngine;

// 死神三姉妹の共通部分(2026-09-29)。旧GrimReaperController(1体の死神)の置き換え。
// 構造: ReaperBase ├ ReaperEldest(長女/荒野街道/歩く) ├ ReaperSecond(次女/自然洞窟/低空浮遊) └ ReaperYoungest(三女/天空回廊/スキップ)
// それぞれ独立したPrefab(Resources/Reapers/ReaperEldest.prefab 等)。今回は100,000m以降の「追跡モード」だけを持つ。
// 将来のラストダンジョン(三人同時の通常ボス)は、このクラスにモードを足して各サブクラスへ戦闘AIを書く想定
// (UpdateCombatの入口だけ用意してある)。
//
// 追跡(HOST/シングルのみ。JOINのパペットはNetCombatが位置を配り、このコンポーネントは止まる):
//  ・死神の位置は自分の論理X(FloatingOriginの影響を受けない)で持ち、毎フレーム自分の速さで進める。プレイヤーの位置に貼り付けない。
//  ・自分の速さ = プレイヤーの実際の速さを「遅れて」なぞった速さ(加速には遅く、減速には速く追従)
//                + じわじわ詰める速さ(登場からの時間で少しずつ増える)
//                + 再追跡(画面左端より後ろへ下がって一定時間たつと、落ち着く位置までの残り距離に比例した速さで戻る)
//    → 急加速すると一時的に引き離せる(画面外へ出ることもある)が、数秒すると滑らかに画面左へ戻ってきて、また詰めてくる。
//  ・距離の基準は画面(カメラ): 「プレイヤーから画面左端までの距離」に対する割合で、落ち着く位置/引き離された判定を決める。
//  ・瞬間移動はしない(ワープ/原点移動は同じ量だけずらす、標的の切り替えは今の位置のまま追い直す)。
//  ・歩き/浮遊/スキップのアニメーションは速さと無関係(ReaperAnimator)。
//  ・捕捉: 距離がcaptureGap以下 → 予告 → 大鎌(既存のBossHitbox)。シングルは一撃でRun終了(既存のGameOver処理)。
//    マルチは既存どおり大鎌の被弾(HOSTのHP表/JOINの被弾申告)で、CO-OPのダウン/VERSUSの脱落は既存の仕組みに任せる。
public abstract class ReaperBase : MonoBehaviour
{
    public ReaperSisterData data;
    public enum Phase { Waiting, Appear, Chase, Captured }

    // ---- 状態(デバッグ/自動テスト用) ----
    public Phase CurrentPhase { get; private set; } = Phase.Waiting;
    public float Gap { get; private set; }               // プレイヤーとの距離(m)
    public float LeftEdgeGap { get; private set; }       // プレイヤーから画面左端までの距離(m)
    public float FollowSpeed { get; private set; }       // 追従している速さ(m/s)
    public float ReaperSpeed { get; private set; }       // 死神自身の速さ(m/s)
    public float PlayerSpeed { get; private set; }       // 測ったプレイヤーの速さ(m/s)
    public float Reacquire { get; private set; }         // 再追跡の強さ(0..1)
    public bool Stunned { get; private set; }            // プレイヤーが被弾で止まっている
    public bool OffScreen => Gap > LeftEdgeGap + 0.6f;
    public int Strikes { get; private set; }
    public int Reappears { get; private set; }            // 標的が後ろへ戻って現れ直した回数
    public float SinceSpawn { get; private set; }
    public static ReaperBase Active { get; private set; }
    // 自動テスト用: trueの間は捕捉の一撃でRunを終えない(追跡の挙動だけを測る)。通常は常にfalse。
    public static bool DebugNoReap;

    protected Transform player;
    protected Transform visual;
    protected ReaperAnimator anim;
    BossHitbox scythe;
    BossTelegraphMarker scytheMark;
    double reaperX, lastPlayerX;
    bool haveLast;
    float escapedTimer, groundY, rootY, nextStrike;
    bool groundKnown, striking;

    public void NetSetTarget(Transform t)
    {
        if (t == null || t == player) return;
        player = t;
        haveLast = false; // 今の位置のまま、新しい標的を追い直す(ワープしない)
    }

    // ===================================================================== //
    // 出現
    // ===================================================================== //
    public static string PrefabNameFor(string stageId) =>
        stageId == "natural_cave" ? "ReaperSecond" : stageId == "sky_corridor" ? "ReaperYoungest" : "ReaperEldest";

    // 担当ステージの姉妹を出す(Prefab: Resources/Reapers/<名前>)。Prefabが無ければ既存の死神の絵で仮に組む。
    public static ReaperBase Spawn(string stageId, Transform player, Sprite fallbackSprite, Vector3 nearPos)
    {
        string name = PrefabNameFor(stageId);
        GameObject go;
        var prefab = Resources.Load<GameObject>("Reapers/" + name);
        if (prefab != null) go = Instantiate(prefab, nearPos, Quaternion.identity);
        else
        {
            go = new GameObject(name);
            go.transform.position = nearPos;
            switch (name)
            {
                case "ReaperSecond": go.AddComponent<ReaperSecond>(); break;
                case "ReaperYoungest": go.AddComponent<ReaperYoungest>(); break;
                default: go.AddComponent<ReaperEldest>(); break;
            }
        }
        go.name = name;
        var r = go.GetComponent<ReaperBase>();
        if (r.data == null) r.data = Resources.Load<ReaperSisterData>("Reapers/" + name + "Data");
        if (r.data == null) r.data = ScriptableObject.CreateInstance<ReaperSisterData>();
        r.player = player != null ? player : (PlayerController.Instance != null ? PlayerController.Instance.transform : null);
        r.Build(fallbackSprite);
        Active = r;
        // マルチ: HOSTでは共有ボスとして登録(JOINのパペットはNetCombat側でこのAIを止める)。
        NetCombat.OnBossInit(r);
        return r;
    }

    void Build(Sprite fallbackSprite)
    {
        visual = transform.Find("Visual");
        if (visual == null)
        {
            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
        }
        anim = GetComponent<ReaperAnimator>();
        if (anim == null) anim = gameObject.AddComponent<ReaperAnimator>();
        anim.Setup(data, visual, fallbackSprite, MotionOf());

        var ch = data.chase;
        Vector2 sc = new Vector2(ch.captureGap * 0.85f, 1.0f), ss = new Vector2(2.8f, 2.6f);
        scythe = BossHitbox.Create(transform, BossFx.Slash(), new Color(0.8f, 0.45f, 0.95f, 0.95f), "ReaperScythe", RenderOrder.Boss + 1);
        scythe.Configure(sc, ss);
        // シングルは一撃で終わり(下のReap)。大鎌の判定そのものは見た目だけ(二重に被弾させない)。
        if (!NetRunLauncher.IsMultiplayerRun && ch.captureLethalSolo) scythe.damagesPlayer = false;
        scytheMark = BossTelegraphMarker.Create(transform, RenderOrder.Boss - 1);
        scytheMark.Configure(sc, ss);
    }

    protected abstract ReaperMotion MotionOf();

    // 足元の高さ(浮遊の高さは種類ごと。上下のゆれ/跳ねる動きはReaperAnimatorが見た目だけに付ける)
    protected virtual float BaseHeight => 0f;

    // 将来の戦闘AI用(今回は使わない)。
    protected virtual void UpdateCombat(float dt) { }

    void OnEnable() { FloatingOrigin.Warped += OnWarped; }
    void OnDisable() { FloatingOrigin.Warped -= OnWarped; }
    void OnDestroy() { if (Active == this) Active = null; }
    void OnWarped(float d) { reaperX += d; lastPlayerX += d; }

    // ===================================================================== //
    // 追跡
    // ===================================================================== //
    void Update()
    {
        if (player == null || data == null) return;
        var gm = GameManager.Instance;
        if (gm != null && (!gm.HasStarted || gm.IsGameOver)) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        var ch = data.chase;
        SinceSpawn += dt;

        double px = player.position.x + FloatingOrigin.Offset;
        LeftEdgeGap = MeasureLeftEdgeGap();
        if (!haveLast)
        {
            // 初回/標的の切り替え: まだ位置が無ければ画面左端の外側に置く。標的を替えた時は今の位置のまま。
            if (CurrentPhase == Phase.Waiting)
            {
                reaperX = px - (LeftEdgeGap + ch.appearOffscreenMargin);
                FollowSpeed = PlayerController.Instance != null ? PlayerController.Instance.CurrentAutoRunSpeed : 0f;
            }
            lastPlayerX = px;
            haveLast = true;
        }
        // 標的が安全地点への復帰などで大きく後ろへ戻った: 死神を一緒に引き戻す(=瞬間移動)代わりに、
        // いったん姿を消して画面左端の外から現れ直す(出現の流れをもう一度)。
        if (CurrentPhase != Phase.Waiting && px - lastPlayerX < -ch.reappearBackDistance && reaperX > px - LeftEdgeGap)
        {
            reaperX = px - (LeftEdgeGap + ch.appearOffscreenMargin);
            CurrentPhase = Phase.Appear;
            escapedTimer = 0f;
            Reappears++;
            anim.Reappear();
            FreezeDiagnostics.LogEvent($"[Reaper] {data.sister} target moved back {px - lastPlayerX:F1}m -> reappear from the left");
        }
        float actual = Mathf.Clamp((float)((px - lastPlayerX) / dt), 0f, 600f); // 実際の移動(ノックバック/復帰で後ろへ戻る分は0)
        lastPlayerX = px;
        // 追う速さの基準: 自分の端末のプレイヤーなら「巡航の走行速度」(被弾の停止/ノックバック/復帰の移動を含まない)。
        // 他の端末のプレイヤーは実際の移動から測る(急に遅くなったら被弾で止まったとみなす)。
        var localPc = PlayerController.Instance;
        bool local = localPc != null && player == localPc.transform;
        float cruise = local ? localPc.CurrentAutoRunSpeed : actual;
        // 被弾で止まっている間: 追従の速さはそのまま保ち(止まった分だけ遅れて再加速で置いていかれる、を防ぐ)、
        // 死神は「プレイヤーの実際の速さ+stunCloseSpeed」で詰める(高速でも被弾1回で一気に捕まらない)。
        Stunned = local ? localPc.IsReacting : (FollowSpeed > 3f && actual < FollowSpeed * 0.35f);
        PlayerSpeed = Stunned ? actual : cruise;
        if (!Stunned)
        {
            float tau = cruise > FollowSpeed ? ch.accelFollowTime : ch.decelFollowTime;
            FollowSpeed += (cruise - FollowSpeed) * (1f - Mathf.Exp(-dt / Mathf.Max(0.01f, tau)));
        }

        Gap = (float)(px - reaperX);
        float target = LeftEdgeGap * ch.targetScreenFraction;
        float escapeLine = LeftEdgeGap * ch.escapeScreenFraction;
        float approach = 0f, catchup = 0f;

        switch (CurrentPhase)
        {
            case Phase.Waiting:
                // 既存の死神の開始(BGM/警告)の後、少し間を置いてから姿を見せ始める
                approach = 0f;
                if (SinceSpawn >= ch.appearDelay) { CurrentPhase = Phase.Appear; anim.BeginAppear(); ReaperAppearFx.Play(); }
                break;
            case Phase.Appear:
                // 画面左端の外から、ゆっくり落ち着く位置まで(遠く離れていれば、まず画面左端の外まで再追跡と同じ要領で追いつく)
                approach = ch.appearApproachSpeed + Mathf.Min(ch.maxCatchupSpeed, ch.reacquireStrength * Mathf.Max(0f, Gap - (LeftEdgeGap + ch.appearOffscreenMargin)));
                if (Gap <= target) CurrentPhase = Phase.Chase;
                break;
            default:
                approach = ch.closeSpeed * (1f + ch.closeSpeedGrowthPerMinute * SinceSpawn / 60f);
                if (Gap > escapeLine) escapedTimer += dt;
                else escapedTimer = Mathf.Max(0f, escapedTimer - dt * 2f);
                float r = Mathf.Clamp01((escapedTimer - ch.reacquireDelay) / Mathf.Max(0.01f, ch.reacquireRampTime));
                if (Gap > ch.hardLeashDistance) r = 1f;
                Reacquire = r;
                catchup = Mathf.Min(ch.maxCatchupSpeed, r * ch.reacquireStrength * Mathf.Max(0f, Gap - target));
                break;
        }

        ReaperSpeed = Stunned && CurrentPhase != Phase.Waiting ? actual + ch.stunCloseSpeed : FollowSpeed + approach + catchup;
        double xBeforeMove = reaperX;
        reaperX += ReaperSpeed * dt;
        // プレイヤーを追い越さない(捕捉の距離で止まる)。ノックバックでプレイヤーが少し下がった時は、一気に引き戻さず
        // ゆっくり下がる(1フレームで後ろへ跳ぶ=瞬間移動に見せない。その間は捕捉の距離の内側でも捕捉扱い)。
        double minX = px - ch.captureGap;
        if (reaperX > minX) reaperX = System.Math.Min(reaperX, System.Math.Max(minX, xBeforeMove - 20.0 * dt));
        Gap = (float)(px - reaperX);
        if (CurrentPhase == Phase.Chase || CurrentPhase == Phase.Captured)
            CurrentPhase = Gap <= ch.captureGap + 0.05f ? Phase.Captured : Phase.Chase;

        // 高さ: 足元の地面(穴の上では直前の地面の高さのまま歩く/浮く)をなめらかに追う
        float x = (float)(reaperX - FloatingOrigin.Offset);
        float? h = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(x) : null;
        if (h.HasValue) { groundY = h.Value; if (!groundKnown) { rootY = groundY; groundKnown = true; } }
        else if (!groundKnown) { groundY = player.position.y; rootY = groundY; groundKnown = true; }
        rootY = Mathf.Lerp(rootY, groundY + BaseHeight, 1f - Mathf.Exp(-dt * 10f));
        transform.position = new Vector3(x, rootY, 0f);

        if (CurrentPhase == Phase.Captured && !striking && Time.time >= nextStrike) StartCoroutine(CaptureStrike());
        UpdateCombat(dt);
    }

    // プレイヤーから画面左端までの距離(このカメラが追っているのが標的なら実測、別のプレイヤーなら既定の視野から)
    float MeasureLeftEdgeGap()
    {
        Camera cam = Camera.main;
        var local = PlayerController.Instance;
        if (cam != null && cam.orthographic && local != null && player == local.transform)
        {
            float half = cam.orthographicSize * cam.aspect;
            return Mathf.Max(3f, player.position.x - (cam.transform.position.x - half));
        }
        var cf = cam != null ? cam.GetComponent<CameraFollow>() : null;
        return cf != null ? Mathf.Max(3f, cf.targetHorizontalHalfWidth - cf.offsetX) : 14f;
    }

    // ===================================================================== //
    // 捕捉
    // ===================================================================== //
    IEnumerator CaptureStrike()
    {
        striking = true;
        var ch = data.chase;
        scytheMark.Show(1f);
        anim.SetStrikeWindup(true);
        float w = 0f;
        while (w < ch.captureWindup)
        {
            w += Time.deltaTime;
            scytheMark.SetProgress(w / ch.captureWindup);
            yield return null;
        }
        scytheMark.Hide();
        anim.SetStrikeWindup(false);
        Strikes++;
        bool reached = Gap <= ch.captureGap + 0.6f;
        FreezeDiagnostics.LogEvent($"[Reaper] {data.sister} strike gap={Gap:F2} reached={reached}");
        if (reached && !DebugNoReap && !NetRunLauncher.IsMultiplayerRun && ch.captureLethalSolo && player == (PlayerController.Instance != null ? PlayerController.Instance.transform : null))
            GameManager.Instance.ReapPlayer("Reaper:" + data.sister);
        yield return scythe.Strike(1f, 0.28f);
        nextStrike = Time.time + ch.captureRetry;
        striking = false;
    }
}
