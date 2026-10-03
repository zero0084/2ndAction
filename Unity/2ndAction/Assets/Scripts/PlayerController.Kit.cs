using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ===== 新4人(2026-09-27) 共通の土台 ===== //
// 6人目 弓使い / 7人目 魔法使い / 8人目 格闘家 / 9人目 忍者。CharacterDefinition.kitが
// Standard以外の間だけ、入力(HandleAttackInput)・ジャンプ時の上攻撃(FireJump)・空中の下
// フリック(Move)がキャラごとのpartial(PlayerController.Archer/Mage/Fighter/Ninja.cs)へ分岐する。
// 既存5人はkit=Standardなので、ここにある処理は全て素通り(何もしない)。
//
// 状態管理は竜騎士(PlayerController.Lancer.cs)と同じ考え方: どの技もBeginKitMove()で「前の技の
// 状態を全部片付けてから」token を得て、終了はEndKitMove(token)に一本化する(try/finally)。
// 割り込まれた古い技はtokenが古いので何も触らずに抜ける。被弾/死亡/復帰はCancelKitMoves()。
public partial class PlayerController
{
    CharacterKit kit = CharacterKit.Standard;
    CharacterDefinition kitDef;
    public CharacterKit Kit => kit;
    bool HasKit => kit != CharacterKit.Standard;

    int kitGeneration;
    bool kitOwnsAttack;
    // 技の最中の前進速度倍率/縦速度の上書き(ダイブキック・急降下・空中で矢を放つ一瞬の滞空など)。
    float kitMoveSlowFactor = 1f;
    // 2026-09-30: 前攻撃/後ろ攻撃で移動の無かったキャラ(弓/魔法/巫女/竜人の尻尾/竜騎士の石突き)の前進・後退量
    public float kitForwardStep = 0.9f;
    // 高速時の構え/詠唱の短縮(2026-09-30): 走る速さが基本の1.5倍を超えたら、その分だけ構え/詠唱を短くする(下限は元の20%)。
    // 以前は高速で走ると弓の引き絞り/魔法の詠唱の間に目の前の敵を追い越してしまい、飛び道具が当たらなかった。
    float KitWindupScale => Mathf.Clamp(1f / Mathf.Max(1f, CurrentAutoRunSpeed / Mathf.Max(0.1f, baseRunSpeed * 1.5f)), 0.2f, 1f);
    public float kitBackStep = 1.3f;
    float? kitVerticalVelocity;
    // 忍者の瞬身/格闘家のカウンター直後のごく短い無敵(被弾処理の入口で弾く。点滅はしない)。
    float kitIFrameTimer;
    public bool IsKitInvincible => kitIFrameTimer > 0f;

    // 見た目: 技ごとのポーズ(PlayerAnimatorが最優先で表示)。framesがnullなら通常の絵。
    public Sprite[] KitPoseFrames { get; private set; }
    public int KitPoseFrame { get; private set; }
    public string KitPoseName { get; private set; }
    // isAttackingを持たない一瞬のポーズ(上攻撃の射撃等)の残り時間。
    float kitPoseTimer;
    readonly Dictionary<string, Sprite[]> kitPoseCache = new Dictionary<string, Sprite[]>();

    // 近接判定(格闘家/忍者)。竜騎士のLanceHitboxと同じく、実行時に1つだけ作って使い回す。
    BoxCollider2D kitHitbox;
    PlayerAttackInfo kitHitInfo;
    public BoxCollider2D KitHitbox => kitHitbox;

    void ApplyKitStats(CharacterDefinition def)
    {
        CancelKitMoves();
        KitOnRunEnd(); // 前のキャラ/前のRunの結界・御札・燃える地面・Blood Gaugeを必ず片付ける
        kit = def.kit;
        kitDef = HasKit ? def : null;
        kitPoseCache.Clear();
        // 体の当たり判定: 竜人だけ大きく、他のキャラは元の大きさ(既存キャラは常に1倍=変化なし)
        ApplyBodyScale(kit == CharacterKit.Dragonkin ? def.dragonkin.bodyScale : 1f);
        if (sr != null) sr.color = Color.white;
        if (!HasKit) return;
        if (def.kitPoses != null)
            foreach (var p in def.kitPoses)
                if (p != null && !string.IsNullOrEmpty(p.name) && p.frames != null && p.frames.Length > 0) kitPoseCache[p.name] = p.frames;
        if (kitHitbox == null)
        {
            var go = new GameObject("KitHitbox");
            go.transform.SetParent(transform, false);
            go.tag = "PlayerAttack";
            kitHitInfo = go.AddComponent<PlayerAttackInfo>();
            kitHitbox = go.AddComponent<BoxCollider2D>();
            kitHitbox.isTrigger = true;
            kitHitbox.enabled = false;
            go.AddComponent<ColliderDebugView>().color = new Color(1f, 0.55f, 0.85f);
        }
        archerLastShotTime = Time.time - 10f; // 開始直後は引き絞り済み
        MageReset();
    }

    // ===================== 技の開始/終了 ===================== //

    int BeginKitMove(string pose, bool ownsAttack)
    {
        ResetKitState();
        int token = ++kitGeneration;
        kitOwnsAttack = ownsAttack;
        if (ownsAttack)
        {
            isAttacking = true;
            comboWindowOpen = false;
            comboBuffered = false;
        }
        SetKitPose(pose, 0);
        return token;
    }

    void EndKitMove(int token)
    {
        if (token != kitGeneration) return;
        ResetKitState();
    }

    bool KitAlive(int gen, int token) => gen == attackGeneration && token == kitGeneration;

    void ResetKitState()
    {
        if (kitOwnsAttack)
        {
            isAttacking = false;
            comboWindowOpen = false;
            kitOwnsAttack = false;
        }
        if (kitHitbox != null) kitHitbox.enabled = false;
        lungeVelocityX = 0f;
        kitVerticalVelocity = null;
        kitMoveSlowFactor = 1f;
        KitPoseFrames = null;
        KitPoseName = null;
        KitPoseFrame = 0;
        kitPoseTimer = 0f;
        if (HasKit) transform.localScale = Vector3.one;
        if (kitHitInfo != null) { kitHitInfo.onHit = null; kitHitInfo.suppressHitStop = false; kitHitInfo.suppressKnockback = false; }
        fighterCounterTimer = 0f;
        kitDiveActive = false;
        kitDiveLanded = false;
    }

    // 被弾/死亡/復帰/キャラ切り替え: 実行中の技を全て打ち切る。
    void CancelKitMoves()
    {
        kitGeneration++;
        ResetKitState();
        fighterNextStage = 1;
        fighterGraceTimer = 0f;
        archerLastShotTime = Time.time; // 被弾で引き絞りはやり直し
        if (archerChargeGlow != null) archerChargeGlow.enabled = false;
        // 10〜12人目: コンボ段/滑空は被弾でも必ず解除(重力の変更を残さない)
        vampireComboStage = 0; vampireNextStage = 1; vampireGraceTimer = 0f;
        DragonReset();
        if (sr != null && kit == CharacterKit.Vampire) sr.color = Color.white;
    }

    // Run終了(正常終了/死亡)/キャラ切り替え: 置いた物とゲージを全て片付ける。
    void KitOnRunEnd()
    {
        MikoCleanup();
        DragonCleanup();
        VampireReset(true);
        if (bloodBarBg != null) { bloodBarBg.enabled = false; bloodBarFill.enabled = false; bloodAura.enabled = false; }
    }

    // 復帰(被弾/落下)で安全地点へ戻った直後。
    void OnKitRespawn()
    {
        CancelKitMoves();
        archerDownShotsUsed = 0;
        MageReset();
        // 巫女の結界/御札・竜人の燃える地面は復帰で消す。吸血鬼はRushを解除しゲージを30までに抑える。
        MikoCleanup();
        DragonCleanup();
        VampireReset(false);
    }

    // ===================== ポーズ ===================== //

    Sprite[] FindPose(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return kitPoseCache.TryGetValue(name, out var f) ? f : null;
    }

    void SetKitPose(string name, int frame)
    {
        KitPoseName = name;
        KitPoseFrames = FindPose(name);
        KitPoseFrame = frame;
    }

    // isAttackingを持たない一瞬のポーズ(ジャンプしながらの射撃など)。技の実行中は上書きしない。
    void FlashKitPose(string name, float seconds)
    {
        if (kitOwnsAttack) return;
        SetKitPose(name, 0);
        kitPoseTimer = seconds;
    }

    // ===================== 毎フレーム/着地/被弾の入口 ===================== //

    void KitUpdate()
    {
        if (!HasKit) return;
        float dt = Time.deltaTime;
        if (kitIFrameTimer > 0f) kitIFrameTimer -= dt;
        if (fighterCounterTimer > 0f) fighterCounterTimer -= dt;
        if (fighterGraceTimer > 0f)
        {
            fighterGraceTimer -= dt;
            if (fighterGraceTimer <= 0f && !isAttacking) fighterNextStage = 1;
        }
        if (kitPoseTimer > 0f)
        {
            kitPoseTimer -= dt;
            if (kitPoseTimer <= 0f && !kitOwnsAttack) { KitPoseFrames = null; KitPoseName = null; }
        }
        // 安全装置: 技が実行中でないのにisAttackingだけ残っていたら解除(竜騎士と同じ)。
        if (isAttacking && !kitOwnsAttack)
        {
            isAttacking = false;
            comboWindowOpen = false;
            comboBuffered = false;
            lungeVelocityX = 0f;
        }
        if (kit == CharacterKit.Archer) ArcherUpdate();
        else if (kit == CharacterKit.Vampire) VampireUpdate();
        else if (kit == CharacterKit.Dragonkin) DragonUpdate();
    }

    // 着地の瞬間(Move()の着地処理から)。
    void OnKitLanded()
    {
        if (!HasKit) return;
        archerDownShotsUsed = 0;
        if (kitDiveActive) { kitDiveLanded = true; return; }
        if (!kitOwnsAttack)
        {
            kitVerticalVelocity = null;
            lungeVelocityX = 0f;
        }
    }

    // TakeDamageの入口(落下以外)。trueならこの被弾は無かったことにする(忍者の瞬身の無敵/格闘家のカウンター)。
    bool kitHazardDamage; // 天井の針など地形のダメージ中(無敵/カウンターの対象外)
    bool KitInterceptDamage(string source)
    {
        if (!HasKit || kitHazardDamage) return false;
        if (kitIFrameTimer > 0f) return true;
        if (kit == CharacterKit.Fighter && fighterCounterTimer > 0f)
        {
            TriggerFighterCounter(source);
            return true;
        }
        return false;
    }

    // ===================== 入力 ===================== //

    void HandleKitInput()
    {
        if (requestedFlick == null) return;
        FlickDirection f = requestedFlick.Value;
        switch (kit)
        {
            case CharacterKit.Archer: HandleArcherInput(f); break;
            case CharacterKit.Mage: HandleMageInput(f); break;
            case CharacterKit.Fighter: HandleFighterInput(f); break;
            case CharacterKit.Ninja: HandleNinjaInput(f); break;
            case CharacterKit.Miko: HandleMikoInput(f); break;
            case CharacterKit.Vampire: HandleVampireInput(f); break;
            case CharacterKit.Dragonkin: HandleDragonInput(f); break;
        }
    }

    // FireJump(ジャンプが実際に出た瞬間)から。上攻撃=キャラごとの技。
    void OnKitJump(bool airborne)
    {
        switch (kit)
        {
            case CharacterKit.Archer: ArcherUpShot(); break;
            case CharacterKit.Fighter: StartCoroutine(FighterUppercut(airborne)); break;
            case CharacterKit.Ninja: StartCoroutine(NinjaUpDash()); break;
            case CharacterKit.Miko: MikoUpFan(); break;
            case CharacterKit.Vampire: StartCoroutine(VampireMist()); break;
            case CharacterKit.Dragonkin: StartCoroutine(DragonFlap(airborne)); break;
        }
    }

    // ===================== 近接判定 ===================== //

    float KitFacing => transform.localScale.x < 0f ? -1f : 1f;
    public float FacingSign => KitFacing; // 属性(風刃)の向き(2026-10-03)

    // ローカル座標(右向き基準、後ろ向きの技はルートの反転で自動的に左右反転)の中心・大きさ・角度で判定を出す。
    // いったん無効化してから有効化し直すので、前の段で重なっていた敵にも改めて当たる。
    void ArmKitBox(Vector2 center, Vector2 size, float deg, PlayerAttackKind kind, float damageScale, float knockbackScale, float hitStop)
    {
        if (kitHitbox == null) return;
        kitHitbox.enabled = false;
        var t = kitHitbox.transform;
        t.localPosition = new Vector3(center.x, center.y, 0f);
        t.localRotation = Quaternion.Euler(0f, 0f, deg);
        t.localScale = new Vector3(size.x, size.y, 1f);
        kitHitbox.size = Vector2.one;
        kitHitbox.offset = Vector2.zero;
        kitHitInfo.kind = kind;
        kitHitInfo.damageScale = damageScale;
        kitHitInfo.knockbackScale = knockbackScale;
        kitHitInfo.hitStop = hitStop;
        kitHitInfo.fixedReach = false;
        kitHitbox.enabled = true;
        kitHitInfo.Rearm();
    }

    void DisarmKitBox(int token)
    {
        if (token != kitGeneration) return;
        if (kitHitbox != null) kitHitbox.enabled = false;
    }

    // ===================== 急降下(格闘家のダイブキック/忍者の急降下斬り 共通) ===================== //

    bool kitDiveActive;
    bool kitDiveLanded;

    // ===================== 演出 ===================== //

    void KitShake(float mag, float dur)
    {
        var cam = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cam != null) cam.Shake(mag, dur);
    }

    // プレイヤー基準のローカル位置(右向き基準)をワールドへ。
    Vector3 KitWorld(Vector2 local) => transform.position + new Vector3(local.x * KitFacing, local.y, 0f);

    // 正常終了(Finish)の減速中: 魔法使いは高く飛んでいても浮遊の最低高度まで降りる。他キャラは何もしない。
    void KitFinishTick(float dt)
    {
        if (kit != CharacterKit.Mage) return;
        MageFinishTick(dt);
    }
}
