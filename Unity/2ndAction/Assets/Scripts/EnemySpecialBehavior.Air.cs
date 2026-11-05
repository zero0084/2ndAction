using UnityEngine;

// ===== 空中雑魚(2026-11-05) ===== //
// 5 つの役(AirRole)の動き。種ごとの数値は AirMinionTuning(id で引く)。状態は天空回廊の敵と同じ skyState を使い回す
// (種類はどちらか一方なので重ならない)。被弾で止められた後は途中の攻撃を再開せず、安全な待機へ戻す(天空回廊と同じ)。
//  A 低空   : 頭の少し上(立っていれば当たらない高さ)を、ゆっくり上下しながら前から寄って来る。T1〜 は予兆のあと短く横へ突っ込む
//  B 高空   : 二段ジャンプの高さを漂う(進む向きは常に左 = 向きがぱたぱた変わらない)。空中のプレイヤーが下に来たら短く降りる
//  C 急降下 : 高い所で止まって光る + 軌道の細い線 → プレイヤーが着く位置へ斜めに(蜘蛛は真下へ)急降下 → 上へ戻る
//  D 横切り : 画面の外で待つ → 通る高さに画面を横切る細い線(予告)→ 右から左へ高速で横切る(体の通り道で当たりを確かめる)
//  E 群れ   : 1 枠を数体に広げて置く(EncounterDirector)。まとまって前から来る。T1〜 は羽音(震え)のあと短く突っ込む / V 字の編隊は横切る
public partial class EnemySpecialBehavior
{
    bool IsAirKind => kind >= EnemyBehaviorKind.AirLow && kind <= EnemyBehaviorKind.AirSwarm;

    AirMinionTuning.Species airSpec;
    float airBaseH, airY, airPhase, airDartFromX, airDartDist, airPrevX = float.NaN;
    bool airYInit, airHitDone, airGroundLane;
    int airDives;
    GameObject airLineGO;
    SpriteRenderer airLineSr, airLineCore;
    readonly SpriteRenderer[] airArrows = new SpriteRenderer[3];
    SpriteRenderer airGlow; // 蜘蛛の目・背中の光(暗い洞窟でも見える。エステル案)
    Collider2D airPlayerCol;
    PlayerController airPlayerColOwner;
    int airForcedLane = -1;          // 横切りの高さの指定(道中イベント): 1 = 地上の高さ / 0 = 跳躍の高さ / -1 = 抽選
    float airForcedDelay = -1f;      // 横切りを始めるまでの待ち(秒、道中イベントで 1 体ずつ)

    // 空中雑魚の危険が来る(予兆, 時刻, 長さ): 横切りの予告の始まり / 急降下の始まり。DecisionEvents が QA の人の操作向けの予兆にする
    public static event System.Action<DecisionEvents.Cue, float, float> AirThreat;

    // 道中イベント「横切りの連続」用: 高さと待ちを決める(Start の前に呼ぶ)
    public void ConfigureCrosser(bool groundLane, float delay)
    {
        airForcedLane = groundLane ? 1 : 0;
        airForcedDelay = Mathf.Max(0f, delay);
    }

    void InitAir()
    {
        skyInitialized = true;
        var ec = GetComponent<EnemyController>();
        string id = ec != null && ec.Definition != null ? ec.Definition.enemyId : null;
        airSpec = AirMinionTuning.I.Find(id) ?? new AirMinionTuning.Species { id = id ?? "", height = new Vector2(2.6f, 2.8f) };
        if (!string.IsNullOrEmpty(id)) gameObject.name = "Air_" + id; // 被弾の記録(Enemy:<名前>)で種が分かるように
        airPhase = Random.Range(0f, 100f);
        skyBob = airPhase;
        skyCooldown = airForcedDelay >= 0f ? airForcedDelay : Random.Range(0.3f, 0.9f);
        float surf = AirSurface(transform.position.x);
        var t = AirMinionTuning.I;
        switch (kind)
        {
            case EnemyBehaviorKind.AirSwarm:
                // 群れは Director が高さをばらして置いた(その高さのまま)。範囲の外なら範囲へ
                airBaseH = Mathf.Clamp(transform.position.y - surf, airSpec.height.x, airSpec.height.y);
                break;
            case EnemyBehaviorKind.AirCrosser:
                airGroundLane = airForcedLane >= 0 ? airForcedLane == 1 : Random.value < t.crosserGroundLaneChance;
                airBaseH = airGroundLane ? t.crosserGroundLaneHeight : t.crosserJumpLaneHeight;
                skyState = SkyState.Approach; // 画面の外で待つ
                break;
            default:
                airBaseH = Random.Range(airSpec.height.x, airSpec.height.y);
                break;
        }
        if (kind == EnemyBehaviorKind.AirLow || kind == EnemyBehaviorKind.AirDiver || kind == EnemyBehaviorKind.AirSwarm)
            skyHitboxGO = SkyMakeHitbox("AirStrikeHitbox", kind == EnemyBehaviorKind.AirSwarm ? new Vector2(0.5f, 0.5f) : kind == EnemyBehaviorKind.AirDiver ? new Vector2(1.0f, 1.0f) : new Vector2(0.9f, 0.8f));
        if (kind == EnemyBehaviorKind.AirHigh) skyHitboxGO = SkyMakeHitbox("AirSwoopHitbox", new Vector2(1.3f, 1.0f));
        SkyMakeMarker();
        airY = AirTargetY(transform.position.x, 0f);
        airYInit = true;
        transform.position = new Vector3(transform.position.x, airY, transform.position.z);
        if (kind == EnemyBehaviorKind.AirDiver && !airSpec.vertical)
        {
            var ef = GetComponent<EnemyFacing>(); // 予兆の間はプレイヤーと一緒に右へ動くので、動く向きではなくプレイヤーの方を向く
            if (ef != null) ef.alwaysFacePlayer = true;
        }
        if (kind == EnemyBehaviorKind.AirDiver && airSpec.vertical)
        {
            AirEnsureLine(new Color(0.75f, 0.88f, 1f, 0.7f)); // 蜘蛛の糸(いつも見える。青白く細く光る)
            // 目と背中の光: 暗い洞窟で本体が見えるよう、体の後ろに淡い赤〜黄の光(予兆の間は強く)
            var g = new GameObject("AirGlow");
            g.transform.SetParent(transform, false);
            g.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            airGlow = g.AddComponent<SpriteRenderer>();
            airGlow.sprite = OneShotSpriteEffect.SoftDotSprite();
            airGlow.sortingOrder = RenderOrder.Enemy - 1;
            g.transform.localScale = Vector3.one * 1.6f / Mathf.Max(0.01f, airGlow.sprite.bounds.size.x);
            airGlow.color = new Color(1f, 0.55f, 0.25f, 0.35f);
        }
    }

    void ResetAirAfterInterrupt()
    {
        if (!skyInitialized) return;
        if (skyHitboxGO != null) skyHitboxGO.SetActive(false);
        if (skyMarkerGO != null) skyMarkerGO.SetActive(false);
        if (airLineGO != null && !(kind == EnemyBehaviorKind.AirDiver && airSpec != null && airSpec.vertical)) airLineGO.SetActive(false);
        if (kind == EnemyBehaviorKind.AirCrosser && skyState == SkyState.Attack) return; // 横切りは止まらずに抜ける
        skyState = kind == EnemyBehaviorKind.AirCrosser ? SkyState.Leave : (kind == EnemyBehaviorKind.AirDiver ? SkyState.Retreat : SkyState.Idle);
        skyCooldown = Mathf.Max(skyCooldown, 0.9f);
        airYInit = false; // 押された後の高さから、なめらかに元の高さへ
    }

    void AirOnDisable()
    {
        if (!IsAirKind) return;
        AirArrows(false, 0f, 0f, 0f, Color.clear, 0f);
        if (skyHitboxGO != null) skyHitboxGO.SetActive(false);
        if (skyMarkerGO != null) skyMarkerGO.SetActive(false);
        if (airLineGO != null && (!gameObject.activeInHierarchy || !(kind == EnemyBehaviorKind.AirDiver && airSpec != null && airSpec.vertical))) airLineGO.SetActive(false);
    }

    EnemyPose AirPose
    {
        get
        {
            switch (skyState)
            {
                case SkyState.Telegraph: return EnemyPose.Telegraph;
                case SkyState.Attack:
                case SkyState.Hold: return EnemyPose.Dive;
                case SkyState.Recover: return EnemyPose.Recover;
                default: return EnemyPose.None;
            }
        }
    }

    // ---- 高さ ----
    // 足場の高さ: 上のルートに置かれた敵は上の道、浮島のあるステージは真下の浮島(EncounterDirector.PlaceAir と同じ考え方)
    float AirSurface(float x)
    {
        var tm = TerrainManager.Instance;
        if (tm == null) return transform.position.y - airBaseH;
        if (surfaceOwner == null) surfaceOwner = GetComponent<EnemyController>();
        float? up = surfaceOwner != null && surfaceOwner.onUpperRoute ? tm.GetSkyHeightAt(x) : null;
        float g = up ?? tm.GetGroundLineAt(x); // 穴の上でも地面の線の高さ(穴の中へ沈まない)
        if (GameManager.Instance != null && GameManager.Instance.ActiveRunStageId == "sky_corridor")
        {
            float? isl = tm.GetSkyHeightAt(x);
            if (isl.HasValue && isl.Value > g && isl.Value < g + 6f) g = isl.Value;
        }
        return g;
    }

    float AirTargetY(float x, float extra)
    {
        float y = AirSurface(x) + airBaseH + extra;
        var tm = TerrainManager.Instance;
        if (tm != null)
        {
            float? c = tm.GetEffectiveCeilingHeightAt(x);
            if (c.HasValue) y = Mathf.Min(y, c.Value - 0.7f);
            float floor = AirSurface(x) + (kind == EnemyBehaviorKind.AirCrosser && airGroundLane ? 0.35f : 1.2f);
            y = Mathf.Max(y, floor);
        }
        return y;
    }

    // 高さをなめらかに寄せる(地面の段差で敵がかくっと上下しない)
    float AirSmoothY(float target, float rate = 5f)
    {
        if (!airYInit) { airYInit = true; airY = transform.position.y; }
        airY = Mathf.Lerp(airY, target, 1f - Mathf.Exp(-rate * EDt));
        return airY;
    }

    // ---- 線(急降下の軌道 / 横切りの予告 / 蜘蛛の糸) ----
    void AirEnsureLine(Color c)
    {
        if (airLineGO == null)
        {
            if (telegraphMarkerSprite == null) return;
            airLineGO = new GameObject("AirLine");
            airLineGO.transform.SetParent(null, false); // 本体の向きの反転・浮遊に引きずられない
            airLineSr = airLineGO.AddComponent<SpriteRenderer>();
            airLineSr.sprite = telegraphMarkerSprite;
            airLineSr.sortingOrder = RenderOrder.CombatFx;
            // 2026-11-05 エステル: 細い線が夕焼けの背景に埋もれる → 外側の色の帯 + 中央の明るい芯(線の長さに合わせて伸びる子)
            var core = new GameObject("Core");
            core.transform.SetParent(airLineGO.transform, false);
            core.transform.localScale = new Vector3(1f, 0.32f, 1f);
            core.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            airLineCore = core.AddComponent<SpriteRenderer>();
            airLineCore.sprite = telegraphMarkerSprite;
            airLineCore.sortingOrder = RenderOrder.CombatFx + 1;
            NetAttackSync.Register(airLineGO, NetAttackSync.AType.Telegraph);
            FloatingOrigin.Shifted += AirLineShift;
        }
        airLineSr.color = c;
        if (airLineCore != null) airLineCore.color = new Color(Mathf.Lerp(c.r, 1f, 0.75f), Mathf.Lerp(c.g, 1f, 0.75f), Mathf.Lerp(c.b, 1f, 0.6f), Mathf.Min(1f, c.a + 0.2f));
        if (!airLineGO.activeSelf) airLineGO.SetActive(true);
    }
    void AirLineShift(float s)
    {
        if (airLineGO != null) airLineGO.transform.position -= new Vector3(s, 0f, 0f);
        foreach (var a in airArrows) if (a != null) a.transform.position -= new Vector3(s, 0f, 0f);
    }

    // 横切りの予告の線の上を、右から左へ流れる小さな矢印(「何かが通る線」と分かる。エステル案)
    void AirArrows(bool on, float x0, float x1, float y, Color c, float t)
    {
        for (int i = 0; i < airArrows.Length; i++)
        {
            if (!on) { if (airArrows[i] != null) airArrows[i].enabled = false; continue; }
            if (airArrows[i] == null)
            {
                var go = new GameObject("AirArrow" + i);
                airArrows[i] = go.AddComponent<SpriteRenderer>();
                airArrows[i].sprite = DecisionEnemy.ChevronSprite();
                airArrows[i].sortingOrder = RenderOrder.CombatFx + 2;
            }
            var ar = airArrows[i];
            ar.enabled = true;
            float u = Mathf.Repeat(t * 0.9f + i / (float)airArrows.Length, 1f); // 右 → 左
            ar.transform.position = new Vector3(Mathf.Lerp(x1, x0, u), y, -0.05f);
            ar.transform.rotation = Quaternion.Euler(0f, 0f, 90f); // 上向きの絵 → 左向き
            ar.transform.localScale = Vector3.one * 0.42f / Mathf.Max(0.01f, ar.sprite.bounds.size.y);
            ar.color = new Color(c.r, c.g, c.b, 0.9f * Mathf.Sin(u * Mathf.PI));
        }
    }

    // 高速では予兆を少し早く(長く)出す: 約 110km/h から伸ばし始め、約 300km/h で +0.25 秒(エステル案。攻撃の速さ・判定は同じ)
    float AirHighSpeedLead() => 0.25f * Mathf.Clamp01((SkyPlayerSpeed() - 30f) / 53f);

    // a → b の細い線
    void AirLine(Vector3 a, Vector3 b, float width)
    {
        if (airLineGO == null) return;
        Vector3 d = b - a;
        float len = d.magnitude;
        Vector2 spr = telegraphMarkerSprite.bounds.size;
        airLineGO.transform.position = (a + b) * 0.5f;
        airLineGO.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        airLineGO.transform.localScale = new Vector3(len / Mathf.Max(0.001f, spr.x), width / Mathf.Max(0.001f, spr.y), 1f);
    }

    void AirDestroyLine()
    {
        if (airLineGO != null) { FloatingOrigin.Shifted -= AirLineShift; Destroy(airLineGO); airLineGO = null; }
        for (int i = 0; i < airArrows.Length; i++) if (airArrows[i] != null) { Destroy(airArrows[i].gameObject); airArrows[i] = null; }
    }

    // ---- 体の通り道の当たり(速く横切る敵が物理の判定をすり抜けないように) ----
    void AirSweptContact(float xa, float xb)
    {
        if (airHitDone) return;
        var pc = PlayerController.Instance;
        var ec = enemyCtl;
        if (pc == null || ec == null || !ec.CanDealContactDamage) { if (pc != null && Mathf.Abs(transform.position.x - pc.transform.position.x) < 1.5f) AirLog($"swept: cannot ({(ec == null ? "no ec" : "ec off")})"); return; }
        if (airPlayerColOwner != pc)
        {
            airPlayerColOwner = pc; airPlayerCol = null;
            foreach (var c in pc.GetComponents<Collider2D>()) if (c != null && !c.isTrigger) { airPlayerCol = c; break; }
            if (airPlayerCol == null) airPlayerCol = pc.GetComponent<Collider2D>();
        }
        var body = GetComponent<BoxCollider2D>();
        if (airPlayerCol == null || !airPlayerCol.enabled || body == null) return;
        var pb = EnemyController.FreshBounds(airPlayerCol);
        Vector2 off = body.offset * (Vector2)transform.lossyScale;
        Vector2 half = body.size * 0.5f * new Vector2(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
        float cy = transform.position.y + off.y;
        float minX = Mathf.Min(xa, xb) + off.x - half.x, maxX = Mathf.Max(xa, xb) + off.x + half.x;
        if (maxX < pb.min.x || minX > pb.max.x || cy + half.y < pb.min.y || cy - half.y > pb.max.y)
        {
            if (!(maxX < pb.min.x || minX > pb.max.x)) AirLog($"swept: x overlap but y miss body {cy - half.y:F2}..{cy + half.y:F2} player {pb.min.y:F2}..{pb.max.y:F2}");
            return;
        }
        airHitDone = true;
        AirLog("swept HIT");
        // その場で当てる(猶予の仕組み = ContactDamageResolver はフレームの最後にもう一度重なりを見るが、高速の横切りはその時には通り過ぎていて当たらなかった)
        ec.ApplyContactDamage();
        AirHitsDealt++;
    }

    public static int AirHitsDealt; // 確認用: 横切りの通り道の当たり
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    static int airLogArg = -1;
    static bool AirLogOn { get { if (airLogArg < 0) airLogArg = System.Array.IndexOf(QaArgs.All, "-airLog") >= 0 ? 1 : 0; return airLogArg == 1; } }
    void AirLog(string s) { if (AirLogOn) Debug.Log($"[AIR] {name} {s} x={transform.position.x:F1} y={transform.position.y:F2} px={(player != null ? player.position.x : 0f):F1} py={(player != null ? player.position.y : 0f):F2}"); }
#else
    void AirLog(string s) { }
#endif

    // ===================================================================== //
    // 毎フレーム
    // ===================================================================== //
    void UpdateAir()
    {
        if (!skyInitialized) return;
        skyCooldown -= EDt;
        switch (kind)
        {
            case EnemyBehaviorKind.AirLow: UpdateAirLow(); break;
            case EnemyBehaviorKind.AirHigh: UpdateAirHigh(); break;
            case EnemyBehaviorKind.AirDiver: UpdateAirDiver(); break;
            case EnemyBehaviorKind.AirCrosser: UpdateAirCrosser(); break;
            case EnemyBehaviorKind.AirSwarm: UpdateAirSwarm(); break;
        }
    }

    float AirBob(float amp, float speed) => Mathf.Sin((Time.time + airPhase) * speed) * amp;

    // 短く横へ突っ込む(低空 / 群れ)。戻り値 = 攻撃の途中か
    bool AirDartCycle(float dartSpeed, float recover, float cooldownMin, float cooldownMax)
    {
        switch (skyState)
        {
            case SkyState.Telegraph:
            {
                skyTimer -= EDt;
                // 予兆: 少し上がって止まる(群れは小刻みに震える = 羽音)
                float shake = kind == EnemyBehaviorKind.AirSwarm ? Mathf.Sin(Time.time * 55f + airPhase) * 0.05f : 0f;
                float y = AirSmoothY(AirTargetY(transform.position.x, 0.2f), 8f);
                transform.position = new Vector3(transform.position.x + shake, y, transform.position.z);
                SkyMarker(kind != EnemyBehaviorKind.AirSwarm, 1f - skyTimer / Mathf.Max(0.05f, skyStateTotal));
                if (skyTimer <= 0f)
                {
                    SkyMarker(false);
                    skyState = SkyState.Attack;
                    airDartFromX = transform.position.x;
                    airDartDist = kind == EnemyBehaviorKind.AirSwarm ? 2.6f : 2.4f;
                    SkyHitbox(true);
                }
                return true;
            }
            case SkyState.Attack:
            {
                float x = transform.position.x - dartSpeed * EDt;
                float y = AirSmoothY(AirTargetY(x, 0f), 6f);
                transform.position = new Vector3(x, y, transform.position.z);
                if (airDartFromX - x >= airDartDist) { SkyHitbox(false); skyState = SkyState.Recover; skyTimer = recover; }
                return true;
            }
            case SkyState.Recover:
                skyTimer -= EDt;
                transform.position = new Vector3(transform.position.x, AirSmoothY(AirTargetY(transform.position.x, 0f)), transform.position.z);
                if (skyTimer <= 0f) { skyState = SkyState.Idle; skyCooldown = Random.Range(cooldownMin, cooldownMax); }
                return true;
        }
        return false;
    }

    // ---- A 低空 ----
    void UpdateAirLow()
    {
        if (AirDartCycle(8f, 0.45f, 2.5f, 4f)) return;
        float x = transform.position.x - airSpec.speed * EDt;
        float y = AirSmoothY(AirTargetY(x, AirBob(0.22f, 2.2f)));
        transform.position = new Vector3(x, y, transform.position.z);
        float dx = SkyDx;
        if (aiTier >= EnemyAiTier.T1 && skyCooldown <= 0f && dx > 1.2f && dx <= SkyLead(airSpec.telegraph + 0.3f, 4.5f) && SkyOnScreen() && AttackSlot(airSpec.telegraph + 1f))
            StartAirTelegraph(airSpec.telegraph);
    }

    void StartAirTelegraph(float duration)
    {
        skyState = SkyState.Telegraph;
        skyTimer = skyStateTotal = duration;
        skyFacing = -1f;
    }

    // ---- B 高空 ----
    void UpdateAirHigh()
    {
        switch (skyState)
        {
            case SkyState.Telegraph:
                skyTimer -= EDt;
                SkyMarker(true, 1f - skyTimer / Mathf.Max(0.05f, skyStateTotal));
                transform.position = new Vector3(transform.position.x, AirSmoothY(AirTargetY(transform.position.x, 0.25f), 8f), transform.position.z);
                if (skyTimer <= 0f)
                {
                    SkyMarker(false);
                    skyState = SkyState.Attack;
                    skyDiveFrom = transform.position;
                    float floor = AirSurface(transform.position.x) + 2.3f; // 立っている頭より上で止まる(空中だけを狙う)
                    skyDiveTo = new Vector3(transform.position.x - 1.2f, Mathf.Max(floor, transform.position.y - 1.6f), transform.position.z);
                    SkyHitbox(true);
                }
                return;
            case SkyState.Attack:
                transform.position = Vector3.MoveTowards(transform.position, skyDiveTo, 6.5f * EDt);
                airY = transform.position.y;
                if ((transform.position - skyDiveTo).sqrMagnitude < 0.01f) { skyState = SkyState.Hold; skyTimer = 0.15f; }
                return;
            case SkyState.Hold:
                skyTimer -= EDt;
                if (skyTimer <= 0f) { SkyHitbox(false); skyState = SkyState.Recover; skyTimer = 0.5f; }
                return;
            case SkyState.Recover:
                skyTimer -= EDt;
                if (skyTimer <= 0f) { skyState = SkyState.Idle; skyCooldown = Random.Range(2.5f, 4f); }
                return;
        }
        // 漂う: 左へゆっくり + 前後の揺れ(揺れの速さ < 左へ進む速さ → 向きが変わらない)+ 上下の揺れ
        float sway = Mathf.Cos((Time.time + airPhase) * 0.9f) * 0.45f * 0.9f; // 揺れの速度
        float x = transform.position.x - (airSpec.speed + sway * 0.6f) * EDt;
        float y = AirSmoothY(AirTargetY(x, AirBob(0.35f, 0.9f)), 3f);
        transform.position = new Vector3(x, y, transform.position.z);
        // 空中のプレイヤーが下(前後 2.5m 以内)に来たら短く降りる
        if (aiTier >= EnemyAiTier.T1 && skyCooldown <= 0f && player != null)
        {
            float dx = SkyDx;
            float lead = SkyPlayerSpeed() * (airSpec.telegraph + 0.2f);
            bool airborne = PlayerController.Instance != null && !PlayerController.Instance.IsGrounded;
            if (airborne && dx > -0.5f && dx < lead + 2.5f && player.position.y < transform.position.y - 0.4f && SkyOnScreen() && AttackSlot(airSpec.telegraph + 1f))
                StartAirTelegraph(airSpec.telegraph);
        }
    }

    // ---- C 急降下 ----
    void UpdateAirDiver()
    {
        bool vertical = airSpec.vertical;
        if (vertical && airLineGO != null)
        {
            // 蜘蛛の糸: 天井(無ければ画面の上)から体まで
            float? c = TerrainManager.Instance != null ? TerrainManager.Instance.GetEffectiveCeilingHeightAt(transform.position.x) : null;
            float top = c ?? (transform.position.y + 6f);
            bool tele = skyState == SkyState.Telegraph;
            AirLine(new Vector3(transform.position.x, top, 0f), transform.position + Vector3.up * 0.25f, tele ? 0.07f : 0.045f);
            if (airLineSr != null) airLineSr.color = new Color(0.75f, 0.88f, 1f, tele ? 0.95f : 0.7f);
            if (airGlow != null) airGlow.color = new Color(1f, tele ? 0.35f : 0.55f, 0.25f, tele ? 0.6f + 0.25f * Mathf.Sin(Time.time * 18f) : 0.35f);
        }
        switch (skyState)
        {
            case SkyState.Idle:
            {
                float x = vertical ? transform.position.x : transform.position.x + Mathf.Sin((Time.time + airPhase) * 0.5f) * 0.4f * EDt;
                float y = AirSmoothY(AirTargetY(x, AirBob(vertical ? 0.08f : 0.25f, vertical ? 1.6f : 1.3f)), 3f);
                transform.position = new Vector3(x, y, transform.position.z);
                float dx = SkyDx;
                float dropTime = vertical ? Mathf.Max(1f, airBaseH - 1f) / Mathf.Max(1f, airSpec.speed) : AirDiveSeconds;
                if (aiTier >= EnemyAiTier.T1 && airDives < AirMinionTuning.I.diverMaxDives && skyCooldown <= 0f && dx > 1.0f
                    && dx <= SkyLead(airSpec.telegraph + AirHighSpeedLead() + dropTime, vertical ? 3.5f : 6f) && SkyOnScreen() && AttackSlot(airSpec.telegraph + 1.4f))
                {
                    StartAirTelegraph(airSpec.telegraph + AirHighSpeedLead());
                    if (!vertical) AirEnsureLine(new Color(1f, 0.22f, 0.15f, 0.85f));
                }
                return;
            }
            case SkyState.Telegraph:
            {
                skyTimer -= EDt;
                float prog = 1f - skyTimer / Mathf.Max(0.05f, skyStateTotal);
                SkyMarker(true, prog);
                // 止まる(少しだけ上へ身構える。蜘蛛は小刻みに揺れる)
                float wig = vertical ? Mathf.Sin(Time.time * 40f) * 0.04f : 0f;
                float tx0 = transform.position.x + wig;
                // 斜めの急降下: 予兆の間はプレイヤーの少し前に居続ける(画面に入ってから予兆を始めると、高速では急降下が後ろへ外れた)
                if (!vertical && player != null) tx0 = Mathf.Max(tx0, player.position.x + SkyPlayerSpeed() * AirDiveSeconds + 1.5f);
                transform.position = new Vector3(tx0, AirSmoothY(AirTargetY(tx0, vertical ? 0f : 0.3f), 8f), transform.position.z);
                Vector3 target = AirDiveTarget(vertical);
                if (!vertical) AirLine(transform.position, target, Mathf.Lerp(0.07f, 0.12f, prog));
                if (skyTimer <= 0f)
                {
                    SkyMarker(false);
                    if (!vertical && airLineGO != null) airLineGO.SetActive(false);
                    skyDiveFrom = transform.position;
                    skyDiveTo = target;
                    airDiveTotal = airDiveLeft = vertical ? Vector3.Distance(transform.position, target) / Mathf.Max(1f, airSpec.speed) : AirDiveSeconds;
                    skyState = SkyState.Attack;
                    airDives++;
                    SkyHitbox(true);
                    AirLog($"dive start to=({target.x:F1},{target.y:F2})");
                    // 地上のプレイヤーへの急降下 = 着く直前に跳べば下を抜ける / 空中なら急降下で地上へ
                    if (AirThreat != null && player != null)
                    {
                        float arrive = Vector3.Distance(transform.position, target) / Mathf.Max(1f, airSpec.speed);
                        bool grounded = PlayerController.Instance == null || PlayerController.Instance.IsGrounded;
                        AirThreat(grounded ? DecisionEvents.Cue.JumpOver : DecisionEvents.Cue.StayLow, Time.time + arrive, vertical ? 0.45f : 0.2f);
                    }
                }
                return;
            }
            case SkyState.Attack:
                airDiveLeft -= EDt;
                if (!vertical && player != null)
                {
                    // 決まった秒数(AirDiveSeconds)で、着く時刻のプレイヤーの位置へ。横だけ寄せ続ける(速さで決めると斜めの距離のぶん遅れ、後ろへ外れていた)。
                    //  高さは予兆の時に決めた低さのまま = 着く直前に跳べば上を抜けられる / 空中なら急降下で下を抜けられる
                    float want = player.position.x + SkyPlayerSpeed() * Mathf.Max(0f, airDiveLeft);
                    skyDiveTo.x = Mathf.Clamp(want, skyDiveFrom.x - 9f, skyDiveFrom.x + 4f);
                    // 高さ: 地上のプレイヤーなら、着く所の本当の地面 + 0.55m(坂で足元が下がっても体の真ん中へ)。空中のプレイヤーは予兆の時の高さのまま
                    var pcI = PlayerController.Instance;
                    if (pcI != null && pcI.IsGrounded && TerrainManager.Instance != null)
                    {
                        float? gh = TerrainManager.Instance.GetHeightAt(skyDiveTo.x);
                        if (gh.HasValue && gh.Value > player.position.y - 3f && gh.Value < player.position.y + 3f) skyDiveTo.y = gh.Value + 0.55f;
                    }
                }
                float stepSpeed = vertical ? airSpec.speed : Vector3.Distance(transform.position, skyDiveTo) / Mathf.Max(0.02f, airDiveLeft);
                transform.position = Vector3.MoveTowards(transform.position, skyDiveTo, stepSpeed * EDt);
                airY = transform.position.y;
                if ((transform.position - skyDiveTo).sqrMagnitude < 0.01f) { skyState = SkyState.Hold; skyTimer = vertical ? 0.35f : 0.16f; AirLog("dive hold"); }
                return;
            case SkyState.Hold:
                skyTimer -= EDt;
                if (skyTimer <= 0f) { SkyHitbox(false); skyState = SkyState.Recover; skyTimer = vertical ? 0.3f : 0.35f; }
                return;
            case SkyState.Recover:
                skyTimer -= EDt;
                if (skyTimer <= 0f) skyState = SkyState.Retreat;
                return;
            case SkyState.Retreat:
            {
                float ty = AirTargetY(transform.position.x, 0f);
                Vector3 p = transform.position;
                p.y = Mathf.MoveTowards(p.y, ty, (vertical ? 3.5f : 5f) * EDt);
                transform.position = p;
                airY = p.y;
                if (Mathf.Abs(p.y - ty) < 0.08f) { skyState = SkyState.Idle; skyCooldown = Random.Range(2f, 3f); }
                return;
            }
        }
    }

    // 急降下の着く所: 斜め = プレイヤーが着く位置(横は最大 4.5m)、真下 = 自分の真下の頭の高さ
    // 斜めの急降下の秒数(予兆の線を見てから跳ぶ時間は予兆の側で取る)
    const float AirDiveSeconds = 0.42f;
    float airDiveLeft, airDiveTotal;

    Vector3 AirDiveTarget(bool vertical)
    {
        float surf = AirSurface(transform.position.x);
        if (vertical) return new Vector3(transform.position.x, surf + 1.05f, transform.position.z);
        float ty = Mathf.Max(surf + 0.9f, player.position.y + 0.5f);
        float px = player.position.x + SkyPlayerSpeed() * (Mathf.Max(0f, skyTimer) + AirDiveSeconds);
        float tx = Mathf.Clamp(px, transform.position.x - 9f, transform.position.x + 4f);
        // 高さはプレイヤーの足元 + 0.55m(体の真ん中)。地面の線(AirSurface)は坂で実際の足元とずれ、体の上を抜けていた
        float aimY = player.position.y + 0.55f;
        return new Vector3(tx, Mathf.Max(AirSurface(tx) + 0.35f, aimY), transform.position.z);
    }

    // ---- D 横切り ----
    void UpdateAirCrosser()
    {
        Camera cam = Camera.main;
        float half = cam != null ? GameView.HalfWidth(cam) : 9f;
        float cx = cam != null ? GameView.CenterX(cam) : player.position.x;
        switch (skyState)
        {
            case SkyState.Approach:
            {
                // 画面の右の外で待つ(画面の中へ入って来ないよう、右の縁の少し外へ付いて行く)
                float hold = cx + half + 1.5f;
                float x = Mathf.Max(transform.position.x, hold);
                if (transform.position.x > hold + 0.01f) x = transform.position.x; // まだ遠い: その場(画面が近づくのを待つ)
                float ty = AirTargetY(x, AirBob(0.05f, 3f));
                transform.position = new Vector3(x, AirSmoothY(ty, 6f), transform.position.z);
                float warn = AirMinionTuning.I.crosserWarnSeconds + AirHighSpeedLead();
                // 画面の縁から warn 秒で着く距離より近ければ予告を始める
                if (transform.position.x - (cx + half) <= Mathf.Max(3f, SkyPlayerSpeed() * 0.3f) && skyCooldown <= 0f)
                {
                    skyState = SkyState.Telegraph;
                    skyTimer = skyStateTotal = warn;
                    if (AirThreat != null && player != null)
                    {
                        float rel = airSpec.speed + SkyPlayerSpeed();
                        float arrive = warn + Mathf.Max(0f, cx + half + 1.5f - player.position.x) / Mathf.Max(1f, rel);
                        AirThreat(airGroundLane ? DecisionEvents.Cue.JumpOver : DecisionEvents.Cue.StayLow, Time.time + arrive, 1.6f / Mathf.Max(1f, rel) + 0.1f);
                    }
                    AirEnsureLine(airGroundLane ? new Color(1f, 0.6f, 0.1f, 0.85f) : new Color(1f, 0.22f, 0.22f, 0.85f));
                }
                return;
            }
            case SkyState.Telegraph:
            {
                skyTimer -= EDt;
                float hold = cx + half + 1.5f;
                float y = AirSmoothY(AirTargetY(hold, 0f), 6f);
                transform.position = new Vector3(hold, y, transform.position.z);
                float prog = 1f - skyTimer / Mathf.Max(0.05f, skyStateTotal);
                // 画面を横切る細い線(通る高さ)。最後に太く
                AirLine(new Vector3(cx - half, y, 0f), new Vector3(cx + half, y, 0f), Mathf.Lerp(0.09f, 0.16f, prog * prog));
                if (airLineSr != null) { var c = airLineSr.color; c.a = 0.6f + 0.35f * Mathf.PingPong(prog * 4f, 1f); airLineSr.color = c; }
                AirArrows(true, cx - half, cx + half, y, airLineSr != null ? airLineSr.color : Color.red, prog * skyStateTotal);
                if (skyTimer <= 0f)
                {
                    if (airLineGO != null) airLineGO.SetActive(false);
                    AirArrows(false, 0f, 0f, 0f, Color.clear, 0f);
                    skyState = SkyState.Attack;
                    airPrevX = transform.position.x;
                    airHitDone = false;
                    AirLog($"cross start lane={(airGroundLane ? "ground" : "jump")}");
                }
                return;
            }
            case SkyState.Attack:
            case SkyState.Leave:
            {
                float x = transform.position.x - airSpec.speed * EDt;
                float y = AirSmoothY(AirTargetY(x, 0f), 18f); // 地面の起伏にすぐ合わせる(遅いと坂で体の上/下を抜けた)
                transform.position = new Vector3(x, y, transform.position.z);
                if (skyState == SkyState.Attack && !float.IsNaN(airPrevX)) AirSweptContact(airPrevX, x);
                airPrevX = x;
                if (x < cx - half - 4f) gameObject.SetActive(false); // 画面の左の外へ抜けたら消す
                return;
            }
        }
    }

    // ---- E 群れ ----
    void UpdateAirSwarm()
    {
        if (airSpec.vFormation)
        {
            // V 字の編隊: 速く左へ横切る(突っ込みなし)。画面の左の外へ抜けたら消す
            float x = transform.position.x - airSpec.speed * EDt;
            float y = AirSmoothY(AirTargetY(x, AirBob(0.12f, 3f)), 4f);
            transform.position = new Vector3(x, y, transform.position.z);
            Camera cam = Camera.main;
            if (cam != null && x < GameView.CenterX(cam) - GameView.HalfWidth(cam) - 4f) gameObject.SetActive(false);
            return;
        }
        if (AirDartCycle(7f, 0.35f, 3f, 5f)) return;
        float nx = transform.position.x - airSpec.speed * EDt + Mathf.Sin((Time.time + airPhase) * 4f) * 0.25f * EDt;
        float ny = AirSmoothY(AirTargetY(nx, AirBob(0.18f, 3.2f)), 5f);
        transform.position = new Vector3(nx, ny, transform.position.z);
        float dx = SkyDx;
        if (aiTier >= EnemyAiTier.T1 && skyCooldown <= 0f && dx > 1.5f && dx <= SkyLead(airSpec.telegraph + 0.35f, 5f) && SkyOnScreen())
            StartAirTelegraph(airSpec.telegraph);
    }
}
