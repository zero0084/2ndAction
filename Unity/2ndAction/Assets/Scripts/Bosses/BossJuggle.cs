using System.Collections.Generic;
using UnityEngine;

// ボスの BREAK 中のエリアルコンボ(2026-10-18)。
//  普通の敵(EnemyController.AerialCombo)と同じ考え方: プレイヤーが手動の攻撃を当て続けている間だけ、
//  ボスの体(被弾判定)の手前の端を「そのキャラの空中攻撃が当たる所」へ寄せる(走行の速さは変えない。相対の補正だけ)。
//   Launch: 小〜中型と飛行のボス = 体ごと打ち上げる(↑で打ち上げ/打ち上げ直し、→で滞空、↓で下へ押して地面で叩きつけ)
//   Kneel : 大型/固定のボス = 動かさない(BREAK の寄せの位置のまま)。膝をついて低くなり、空中の追撃・再上昇・↓の締めが当たる
//  寄せるのは BREAK の間だけ。通常時のボスは今までどおり(打ち上がらない)。
//  ボス側(WildBossBase / DragonController / MajinController)は、毎フレーム Step の結果(中心の移動量)を自分の座標へ反映する。
// 2026-10-21 コンボのつながり改修:
//  ・基準のプレイヤー(Owner): 最初に有効な打ち上げ(↑)を当てた人。HOST がこの端末のプレイヤー(0)か、協力の相手(プレイヤー番号)を
//    基準に寄せる。ほかの人の命中は滞空を延ばすだけで基準は替えない(命中のたびに左右へ引っ張られない)。
//    基準の人が離れた/倒れた/切断した/当てなくなった時は、直前まで当てていた人へ引き継ぐ(いなければ寄せを終える)。
//  ・締めの合図(Finishing): 回復の予告(残り0.8秒)の後は打ち上げ直しをしない(今までどおり)が、寄せはやめずに少しずつ下げる。
//    → の追撃と ↓ の締めはそのまま当たる(予告の瞬間に落ちて届かなくならない)。BREAK の終わりで寄せを終える。
//  ・JOIN の端末: HOST から届く状態(Replica)で、空中の狙い/再上昇の判定だけに使う(位置は HOST が決める)。
public interface IAirComboTarget
{
    bool IsComboAnchored { get; }
    Vector3 ComboAimPoint { get; }
    bool ComboTryDownPush();
}

// プレイヤー側の参照口: 今のコンボの相手(雑魚 or BREAK 中のボス)
public static class AirComboTargets
{
    public static IAirComboTarget Local
    {
        get
        {
            var b = BossJuggle.Local;
            if (b != null && b.OwnerGone) b = null;
            if (b != null && b.IsComboAnchored) return b;
            var e = EnemyController.LocalComboTarget;
            if (e != null) return e;
            return b;
        }
    }

    // 空中で次につなげられる相手(届く所)。寄せている最中のボスを先に、なければ雑魚、なければ BREAK 中のボス
    public static IAirComboTarget FindCandidate(PlayerController pc, float reachX, float reachY)
    {
        var b = BossJuggle.FindCandidate(pc, reachX, reachY);
        if (b != null && b.IsComboAnchored) return b;
        var e = EnemyController.FindComboCandidate(pc, reachX, reachY);
        if (e != null) return e;
        return b;
    }
}

// 2026-10-21 コンボのつながり改修の比較用(開発版の試験/比較動画だけ): true = 改修前(APK 338 まで)の動き
public static class ComboRevision
{
    public static bool Legacy;
    // 2026-10-21 追加調査の分(竜騎士の締めの急降下の前進・↓の叩き落とし・低い天井・膝つきへの地上の↓・天空ドラゴンの立ち直り)だけを切る。確認用(比較の動画/試験)
    public static bool FollowupOff;
    public static bool Followup => !Legacy && !FollowupOff;
}

public class BossJuggle : IAirComboTarget
{
    public enum Style { Launch, Kneel }

    public const float Window = 0.9f;          // 手動の攻撃が当たってから寄せ続ける時間(秒。雑魚と同じ)
    public const float MaxTotal = 12f;         // 安全装置(BREAK の長さで先に終わる)
    public const float EngageDistance = 7f;    // これより大きくずれたら寄せをやめる(体が大きいので雑魚の 4.5m より広い)
    public const float Gain = 9f;
    public const float MaxCorrection = 10f;    // 補正の速さの上限(m/秒。プレイヤーの動きに足す分)
    public const float Gravity = 32f;
    public const float LaunchSpeed = 12f, RiseTime = 0.16f, RiseMinSpeed = 6f;
    public const float DownPushTime = 0.4f, DownPushDepth = 1.8f;
    public const float CeilingPushDownPerSec = 20f;
    public const float FollowMax = 18f; // プレイヤーの動き(走行を除く)を追う速さの上限(m/秒)
    public const float FinishSinkPerSec = 1.6f; // 締めの合図の後、寄せの高さを下げる速さ(m/秒)
    public const float HandoffRecent = 0.5f;    // 引き継げる相手 = この秒数以内に当てた人
    public const float JoinViewLag = 0.066f;    // JOIN の端末がボスを描く時刻の遅れ(NGO の ServerTime の見積もり、2026-09-28 の計測)
    public const float LandGrace = 0.25f;       // 基準の人が着地してから寄せを終えるまでの猶予(高速の段差で一瞬浮いて着いただけで終わらない。跳び直せば続く)

    public const int OwnerNone = -1, OwnerLocal = 0; // Owner: -1 = なし / 0 = この端末のプレイヤー / 1以上 = 協力の相手のプレイヤー番号(HOST)

    public static BossJuggle Local { get; private set; }
    // 開発用(比較動画): false = 改修前と同じ(BREAK 中も打ち上げない・怯まない・延びない・予告/立ち直りなし)。ゲーム中は常に true
    public static bool Enabled = true;
    static readonly List<BossJuggle> open = new List<BossJuggle>();

    // 確認用(QA)
    public static int Launches, Relaunches, Reanchors, DownPushes, Slams, KneelHits, KneelSlams, EndedFar, EndedTimeout, EndedLanded, AirLandings;
    public static int RemoteLaunches, RemoteHits, Handoffs, OwnerKeptOnOtherHit, FinishHits, FinishSlams;

    public readonly Style style;
    readonly System.Func<Bounds> hurt;
    public readonly string name;
    public bool Open { get; private set; }
    public bool Accepting { get; private set; }  // 回復の予告の後も → / ↓ は受ける(打ち上げ直しだけしない)
    public bool Finishing { get; private set; }  // 回復の予告の後(締めの合図)
    public bool Anchored { get; private set; }
    public bool Airborne { get; private set; }   // 体が地面(休む高さ)より上(打ち上げ/落下中)
    public int Owner { get; private set; } = OwnerNone;
    public float VelY { get; private set; }
    public int AirHits { get; private set; }
    public float MaxRise { get; private set; }   // 確認用: 地面からいちばん上がった高さ
    public string EndReason = "";
    float window, total, rise, down, lift, sink, landedFor;
    // 2026-10-21 追加調査: 締めの合図の後の ↓ は、ボスを地面まで叩き落とす(叩きつけ = 締め)。以前は ↓ でもボスは「プレイヤーの高さ - 1.8m」までしか
    //  下がらず、↓ が弾/魔法のキャラ(二丁拳銃士/魔法使い等。撃った後も空中に残る)は ↓ が当たっても地面へ届かないまま BREAK が終わっていた
    float finishPush;
    public const float FinishSlamSpeed = 14f, FinishPushMax = 0.6f, FinishSlamReach = 0.35f; // 叩き落とす速さの下限(m/秒)/ 長さの上限(秒)/ 地面へ届くまでの目安(秒)
    public static int FinishPushes, GroundDownFinishes;
    // 2026-10-21 追加調査: 天井でボスの体の中心が止まる高さ(+∞ = 天井なし)と体の高さの半分。プレイヤーが天井の下で上がりすぎて、
    //  天井につかえたボス(体が大きい)の上を空振りしないよう、プレイヤー側で上がる高さを抑えるのに使う(PlayerController.ComboCeilingCap)
    public float CeilCap { get; private set; } = float.PositiveInfinity;
    public float HalfHeight { get; private set; }
    bool playerWasAir;
    readonly Dictionary<int, float> lastHitAt = new Dictionary<int, float>(); // 参加者ごとの最後の命中(引き継ぎの相手を選ぶ)

    // JOIN の端末: HOST から届いた状態だけを持つ(寄せの計算はしない)
    public bool IsReplica { get; private set; }
    bool replicaMine; // 基準がこの端末のプレイヤー

    // BREAK 中に寄る相手: 狙っている相手が CPU の仲間なら、この端末のプレイヤー(BREAK はプレイヤーへのご褒美。協力の相手が人なら今までどおり狙いの相手)
    public static Transform BreakFocus(Transform target)
    {
        var me = PlayerController.Instance;
        if (target == null) return me != null ? me.transform : null;
        var p = target.GetComponent<PlayerController>();
        return p != null && p.IsAlly && me != null ? me.transform : target;
    }

    // 被弾判定の今の範囲(物理の更新を待たずに Transform から計算する)。Collider2D.bounds は物理の更新の時にしか動かないので、
    // 高速(150km/h で 1フレーム 0.7m)では 1〜2フレーム前の位置のまま残り、寄せの計算が数mずれていた
    public static Bounds LiveBounds(BoxCollider2D box)
    {
        if (box == null) return new Bounds(Vector3.zero, Vector3.one);
        Transform t = box.transform;
        Vector3 c = t.TransformPoint(box.offset);
        Vector3 s = t.lossyScale;
        return new Bounds(new Vector3(c.x, c.y, 0f), new Vector3(Mathf.Abs(box.size.x * s.x), Mathf.Abs(box.size.y * s.y), 1f));
    }

    readonly Object owner;
    public BossJuggle(Style style, System.Func<Bounds> hurtBounds, string name, Object owner)
    {
        this.style = style;
        hurt = hurtBounds;
        this.name = name;
        this.owner = owner;
    }
    // 持ち主のボスが消えた(撃破の後に片付けられた等)。消えたボスの寄せの情報は使わない
    public bool OwnerGone => owner == null;

    public bool IsComboAnchored => Open && Anchored && !OwnerGone;
    public bool AnchoredToLocal => IsComboAnchored && (IsReplica ? replicaMine : Owner == OwnerLocal);
    public bool PushingDown
    {
        get
        {
            if (down > 0f) return true;
            var a = AnchorOf(Owner);
            return a.ok && a.diving;
        }
    }

    // 遠距離の技の狙い/届くかの判定に使う点(この端末のプレイヤーから見て): 体のうち、そのキャラの攻撃が当たる所に一番近い点(端から少し内側)
    public Vector3 ComboAimPoint
    {
        get
        {
            if (OwnerGone) return Vector3.zero;
            Bounds b = hurt();
            Vector3 c = b.center;
            var pc = PlayerController.Instance;
            if (pc == null) return c;
            Vector2 s = pc.AerialComboSweetSpot();
            Vector3 want = pc.transform.position + new Vector3(s.x, s.y, 0f);
            float ix = Mathf.Max(0f, b.extents.x - 0.35f), iy = Mathf.Max(0f, b.extents.y - 0.35f);
            return new Vector3(Mathf.Clamp(want.x, c.x - ix, c.x + ix), Mathf.Clamp(want.y, c.y - iy, c.y + iy), 0f);
        }
    }

    // 体の上端(被弾判定の今の範囲。膝つきの大型とのコンボで、プレイヤーが上がりすぎない高さに使う)
    public float TopY => OwnerGone ? 0f : hurt().max.y;

    // ---------------------------------------------------------------- 基準のプレイヤー

    public struct Anchor
    {
        public bool ok, grounded, diving;
        public Vector2 pos, vel, sweet;
        public float run;
    }

    // who: 0 = この端末のプレイヤー / 1以上 = 協力の相手(HOST の端末で、分身の位置と最後に届いた状態から)
    public static Anchor AnchorOf(int who)
    {
        var a = new Anchor();
        if (who == OwnerNone) return a;
        if (who == OwnerLocal)
        {
            var pc = PlayerController.Instance;
            if (pc == null || pc.NetIsDowned) return a;
            a.ok = true; a.pos = pc.transform.position; a.vel = pc.MotionVelocity; a.grounded = pc.IsGrounded;
            a.sweet = pc.AerialComboSweetSpot(); a.run = pc.CurrentAutoRunSpeed; a.diving = pc.IsAerialDiving;
            return a;
        }
        foreach (NetPlayer p in NetPlayer.All)
        {
            if (p == null || p.IsOwner || p.PlayerNumber != who) continue;
            var av = p.Avatar;
            if (av == null || !av.HasRecentPosition) return a;
            var rec = NetMatch.Get(who);
            if (rec != null && rec.State != NetMatch.PState.Alive) return a; // DOWN/脱落/退出
            a.ok = true; a.vel = av.ReportedVelocity; a.grounded = av.Grounded;
            // 分身は少し前の位置を見せている(再生の遅れ)。JOIN の画面では「今の自分」に対してボスが見えるので、遅れの分だけ先の位置を基準にする
            // 横は JOIN がボスを見る時刻の遅れ(サーバー時刻の見積もりの遅れ 約0.066秒)の分も先へ。縦は再生の遅れだけ(跳ぶ弧を行き過ぎない)
            float lag = Mathf.Clamp(p.PlaybackLag, 0f, 0.25f);
            a.pos = (Vector2)av.transform.position + new Vector2(a.vel.x * (lag + JoinViewLag), a.vel.y * lag);
            a.diving = !a.grounded && a.vel.y < -12f; // 急降下(落ちる速さで見分ける)
            a.sweet = PlayerController.SweetSpotFor(av.Def, a.diving); a.run = p.RemoteRunSpeed;
            return a;
        }
        return a;
    }

    // ---------------------------------------------------------------- BREAK の始まり/終わり

    public void OpenBreak(bool airborne)
    {
        IsReplica = false;
        Open = true; Accepting = Enabled; Finishing = false;
        Anchored = false; Owner = OwnerNone;
        Airborne = style == Style.Launch && airborne;
        VelY = 0f; AirHits = 0; MaxRise = 0f;
        window = total = rise = down = lift = sink = finishPush = 0f;
        CeilCap = float.PositiveInfinity;
        EndReason = "";
        lastHitAt.Clear();
        if (!open.Contains(this)) open.Add(this);
    }

    // 回復の予告(締めの合図): 打ち上げ直しはしない。寄せは続け、少しずつ下げる(→ / ↓ は届く)
    public void BeginFinish()
    {
        if (!Open) return;
        if (ComboRevision.Legacy) { Accepting = false; if (Anchored) EndAnchor("recovering"); return; } // 改修前: 予告で寄せをやめて落ちる
        Finishing = true;
        lift = 0f;
    }
    // 互換(以前の呼び方)
    public void StopAccepting() => BeginFinish();

    // BREAK の時間が尽きた: 寄せを終える(浮いていれば落ちて、地面で立ち直る)
    public void EndCombo(string reason)
    {
        if (Anchored && finishPush > 0f) return; // 締めの叩き落としの途中: 地面に着く(叩きつけ)まで続ける(最大 FinishPushMax 秒)
        if (Anchored) EndAnchor(reason);
        Accepting = false;
    }

    public void CloseBreak()
    {
        Open = false; Accepting = false; Finishing = false;
        Anchored = false; Airborne = false; Owner = OwnerNone;
        VelY = 0f; down = 0f; finishPush = 0f;
        open.Remove(this);
        if (Local == this) Local = null;
    }

    // 雑魚のコンボが始まった時など: この端末のプレイヤーの寄せの相手を手放す
    public static void ReleaseLocal()
    {
        if (Local != null && Local.Anchored && Local.Owner == OwnerLocal && !Local.IsReplica) Local.EndAnchor("switched");
        Local = null;
    }

    void BeginAnchor(int who)
    {
        if (!Anchored) { total = 0f; var a = AnchorOf(who); playerWasAir = a.ok && !a.grounded; }
        else if (Owner != who) { Handoffs++; var a = AnchorOf(who); playerWasAir = a.ok && !a.grounded; }
        Anchored = true;
        Owner = who;
        window = Window;
        landedFor = 0f;
        if (who == OwnerLocal) TakeLocal();
    }

    void TakeLocal()
    {
        if (Local != null && Local != this && Local.Owner == OwnerLocal && !Local.IsReplica) Local.EndAnchor("switched");
        Local = this;
        EnemyController.ReleaseLocalComboTarget(); // 雑魚を寄せていたら手放す(同時に2体を寄せない)
    }

    void EndAnchor(string reason)
    {
        Anchored = false;
        Owner = OwnerNone;
        window = 0f; lift = 0f; down = 0f; finishPush = 0f;
        EndReason = reason;
    }

    // ---------------------------------------------------------------- 命中

    // この端末のプレイヤーの手動の攻撃が当たった(BREAK 中)。戻り値: 叩きつけの締めが入った(Kneel の ↓)
    public bool OnLocalHit(PlayerAttackKind kind) => OnHit(kind, OwnerLocal);

    // who: 当てた人(0 = この端末 / 1以上 = 協力の相手のプレイヤー番号。HOST だけが呼ぶ)
    public bool OnHit(PlayerAttackKind kind, int who)
    {
        if (!Open || !Accepting || IsReplica) return false;
        var a = AnchorOf(who);
        if (!a.ok) return false;
        if (who != OwnerLocal) RemoteHits++;
        if (Finishing) FinishHits++;
        lastHitAt[who] = Time.time;
        bool isDown = kind == PlayerAttackKind.Down || kind == PlayerAttackKind.DownImpact;
        bool byOther = Anchored && Owner != who; // ほかの人が基準のコンボへの追撃: 基準は替えない
        if (who == OwnerLocal) TakeLocal();
        if (style == Style.Kneel)
        {
            // 大型: 位置は動かさない。空中/↑の命中でコンボを続ける(プレイヤーの再上昇/滞空/↓の締めが使える)
            bool airHit = !a.grounded || kind == PlayerAttackKind.Up;
            if (byOther) { OwnerKeptOnOtherHit++; window = Mathf.Max(window, Window * 0.6f); AirHits++; KneelHits++; RegisterAir(isDown, who); }
            else if (airHit || Anchored) { BeginAnchor(who); AirHits++; KneelHits++; RegisterAir(isDown, who); }
            // 2026-10-21 追加調査: 締めの合図の後は、着地して地上の ↓ の技(忍者の滑り込み等)を当てても締めになる。膝をついた大型は低いので
            //  コンボの途中で着地しやすく、以前は地上の ↓ が「普通の命中」になって締めが入らなかった(↓ の入力から 0.6秒以内の命中だけ)
            bool downFinish = isDown || (Finishing && ComboRevision.Followup && who == OwnerLocal && PlayerController.Instance != null
                                         && Time.time - PlayerController.Instance.LastDownMoveAt < 0.6f);
            if (downFinish && Anchored) { KneelSlams++; if (Finishing) FinishSlams++; if (!isDown) GroundDownFinishes++; return true; }
            return false;
        }
        if (kind == PlayerAttackKind.Up && !Finishing)
        {
            bool wasAir = Airborne;
            if (byOther)
            {
                // 基準の人が寄せている最中の、ほかの人の↑: 滞空を延ばすだけ(持ち上げは基準の人の↑だけ。上下に引っ張り合わない)
                OwnerKeptOnOtherHit++; window = Mathf.Max(window, Window * 0.6f); AirHits++; RegisterAir(false, who);
                return false;
            }
            BeginAnchor(who);
            down = 0f;
            if (!wasAir) { Airborne = true; rise = RiseTime; VelY = LaunchSpeed; AirHits = 1; Launches++; if (who != OwnerLocal) RemoteLaunches++; }
            else { lift = 1.0f; VelY = Mathf.Max(VelY, 5f); AirHits++; Relaunches++; RegisterAir(false, who); }
            return false;
        }
        if (!Airborne) return false; // 打ち上げる前(地上)の → / ↓ は普通の命中(締めの合図の後の↑も)
        if (!Anchored) { BeginAnchor(who); Reanchors++; } // 落ちてくる所を空中で捉えた
        else if (byOther) { OwnerKeptOnOtherHit++; window = Mathf.Max(window, Window * 0.6f); }
        else window = Window;
        AirHits++;
        if (isDown)
        {
            down = DownPushTime; lift = 0f; DownPushes++;
            if (kind == PlayerAttackKind.DownImpact) VelY = Mathf.Min(VelY, -14f);
            if (Finishing) BeginFinishPush(); // 協力: 基準でない人の ↓ でも締めになる(縦に下ろすだけで、横の寄せは基準の人のまま)
        }
        else down = 0f; // ↓の後に → / ↑ が当たった: つなぎ直し
        RegisterAir(isDown, who);
        return false;
    }

    void RegisterAir(bool final, int who)
    {
        if (who != OwnerLocal) return; // AIR の段の表示はこの端末のプレイヤーの命中だけ
        if (ComboCounterUI.Instance != null && AirHits >= 2) ComboCounterUI.Instance.RegisterAir(AirHits, final);
    }

    void BeginFinishPush()
    {
        if (!ComboRevision.Followup || !Anchored || finishPush > 0f) return;
        finishPush = FinishPushMax; FinishPushes++;
    }

    // ↓の弾/札などから直接(プレイヤー側の締めの技)
    public bool ComboTryDownPush()
    {
        if (!Open || !Anchored) return false;
        if (IsReplica) return true; // JOIN: 押す処理は HOST(命中の報告)で
        if (style == Style.Launch) { down = DownPushTime; lift = 0f; window = Window; DownPushes++; if (Finishing) BeginFinishPush(); }
        return true;
    }

    // ---------------------------------------------------------------- 毎フレーム

    // Kneel: 寄せの時間を進めるだけ。Launch: 中心の移動量を返す。
    //   center/ext = 被弾判定の中心と半分の大きさ(今)、restCenterY = 地面に立っている時の中心の高さ、
    //   ceilCenterMax = 頭が天井に入らない中心の高さの上限(天井が無ければ +∞)、baseRun = ボスが今並走している速さ(ボス側が足している分)
    //   slammed = ↓で押して地面へ届いた(叩きつけ)、landed = 落ちて地面に着いた
    public Vector2 Step(Vector2 center, Vector2 ext, float restCenterY, float ceilCenterMax, float dt, out bool slammed, out bool landed, float baseRun = float.NaN)
    {
        slammed = false; landed = false;
        if (!Open || IsReplica) return Vector2.zero;
        CeilCap = ceilCenterMax; HalfHeight = ext.y;
        Anchor a = AnchorOf(Owner);
        if (Anchored)
        {
            window -= dt; total += dt;
            if (Finishing) sink += FinishSinkPerSec * dt;
            if (finishPush > 0f) { finishPush -= dt; down = Mathf.Max(down, 0.05f); }
            if (a.ok && !a.grounded) playerWasAir = true;
            if (!a.ok) { if (!TryHandoff("ownerGone")) EndAnchor("ownerGone"); }
            else if (window <= 0f || total > MaxTotal) { if (!TryHandoff("timeout")) { EndAnchor("timeout"); EndedTimeout++; } }
            else if (playerWasAir && a.grounded && style == Style.Launch)
            {
                landedFor += dt;
                if (landedFor > (ComboRevision.Legacy ? 0f : LandGrace) && !TryHandoff("playerLanded")) { EndAnchor("playerLanded"); EndedLanded++; }
            }
            if (!a.ok || !a.grounded) landedFor = 0f;
            a = AnchorOf(Owner);
        }
        if (style == Style.Kneel || (!Airborne && !Anchored)) return Vector2.zero;

        float vx = 0f, vy;
        if (Anchored && a.ok)
        {
            Vector2 sweet = a.sweet;
            lift = Mathf.MoveTowards(lift, 0f, 2.2f * dt);
            if (down > 0f) down -= dt;
            float push = down > 0f ? -DownPushDepth : 0f;
            // 体の手前の端を「当たる所」の少し手前へ。高さは体の中心をそのキャラの攻撃の高さへ(締めの合図の後は少しずつ下げる)
            float nearEdge = Mathf.Clamp(sweet.x - 0.55f, 0.25f, 2.3f);
            Vector2 p = a.pos;
            Vector2 target = new Vector2(p.x + nearEdge + ext.x, p.y + sweet.y + lift + push - sink);
            if (!PushingDown) target.y = Mathf.Max(target.y, restCenterY);
            Vector2 diff = target - center;
            if ((finishPush > 0f ? Mathf.Abs(diff.x) : diff.magnitude) > EngageDistance) { if (!TryHandoff("far")) { EndAnchor($"far({diff.x:F1},{diff.y:F1})"); EndedFar++; } }
            else
            {
                Vector2 corr = Vector2.ClampMagnitude(diff * Gain, MaxCorrection);
                Vector2 pv = a.vel;
                // 走行の分はボス側が既に足している(並走)。プレイヤーの踏み込み/急降下の一瞬の速さ(1フレームで数m)はそのまま追わない
                // (前のフレームの速さなので1フレーム遅れて、ボスだけが跳んで見えた)。差は位置の補正(corr)で滑らかに詰める
                // 2026-10-21: 引く走行の速さは「ボスが実際に足している速さ」(協力では狙いの相手と基準の人の速さが違うことがある)
                float run = float.IsNaN(baseRun) ? a.run : baseRun;
                vx = Mathf.Clamp(pv.x - run, -FollowMax, FollowMax) + corr.x;
                if (!a.grounded) vy = Mathf.Clamp(pv.y, -FollowMax, FollowMax) + corr.y;
                else { VelY -= Gravity * dt; vy = VelY; }    // まだ地上(打ち上げた直後): 普通に上がる
                if (rise > 0f) { rise -= dt; vy = Mathf.Max(vy, RiseMinSpeed); }
                vy = Mathf.Clamp(vy, -24f, 18f);
                // 締めの叩き落とし: プレイヤーの高さに関係なく地面へ。高い所(飛ぶボス/高く上がったコンボ)からでも約0.35秒で届く速さ(毎秒 14〜32m)
                if (finishPush > 0f) vy = Mathf.Min(vy, -Mathf.Clamp((center.y - restCenterY) / FinishSlamReach, FinishSlamSpeed, 32f));
                VelY = vy;
            }
        }
        if (!Anchored)
        {
            VelY -= Gravity * dt;
            vy = Mathf.Max(VelY, -26f);
            VelY = vy;
        }
        else vy = VelY;

        float ny = center.y + vy * dt;
        // 天井: 頭が天井の少し下で止まる(下がってくる天井には毎秒 20m まで押し下げられる。地面より下へは押さない)
        if (ny > ceilCenterMax)
        {
            if (VelY > 0f) VelY = 0f;
            // 下がってくる天井に沿って下がる速さ: 毎秒 20m、または走行の速さの1.2倍(高速で天井の坂を走る時に頭が入らない)
            float pushRate = Mathf.Max(CeilingPushDownPerSec, (a.ok ? a.run : 0f) * 1.2f);
            ny = Mathf.Max(ceilCenterMax, Mathf.Min(ny, center.y - pushRate * dt));
        }
        if (ny <= restCenterY)
        {
            ny = restCenterY;
            if (Anchored && PushingDown && VelY <= 0f && rise <= 0f)
            {
                slammed = true; Slams++;
                if (Finishing) FinishSlams++;
                EndAnchor("slam");
                Airborne = false; VelY = 0f;
            }
            else if (!Anchored)
            {
                landed = true; AirLandings++;
                Airborne = false; VelY = 0f;
            }
            else if (VelY < 0f) VelY = 0f; // 寄せている間はプレイヤーが低くても地面に沿うだけ(着地はコンボの終わりで)
        }
        else Airborne = true;
        MaxRise = Mathf.Max(MaxRise, ny - restCenterY);
        return new Vector2(vx * dt, ny - center.y);
    }

    // 基準の人が当てなくなった/離れた: 直前まで当てていたほかの人(届く所で空中にいる)へ引き継ぐ
    bool TryHandoff(string why)
    {
        int best = OwnerNone; float bestT = Time.time - HandoffRecent;
        foreach (var kv in lastHitAt)
        {
            if (kv.Key == Owner || kv.Value < bestT) continue;
            var a = AnchorOf(kv.Key);
            if (!a.ok || (style == Style.Launch && a.grounded)) continue;
            best = kv.Key; bestT = kv.Value;
        }
        if (best == OwnerNone) return false;
        BeginAnchor(best);
        window = Window * 0.6f;
        EndReason = $"handoff({why})";
        return true;
    }

    // ---------------------------------------------------------------- 協力: HOST → JOIN の状態

    // HOST: 送る1バイト(下位4ビット = 基準の人のプレイヤー番号、16 = 受付中、32 = 締めの合図、64 = 浮いている、128 = 膝つき)
    public byte NetPack()
    {
        if (!Open) return 0;
        int pn = !Anchored ? 0 : Owner == OwnerLocal ? NetCombat.LocalPlayerNumber : Owner;
        byte b = (byte)(Mathf.Clamp(pn, 0, 15));
        if (Accepting) b |= 16;
        if (Finishing) b |= 32;
        if (Airborne) b |= 64;
        if (style == Style.Kneel) b |= 128;
        return b;
    }

    // JOIN: 届いた状態を写す(寄せの計算はしない)
    public void NetApplyReplica(byte b)
    {
        IsReplica = true;
        bool openNow = (b & (16 | 32)) != 0;
        if (openNow && !Open) { Open = true; AirHits = 0; if (!open.Contains(this)) open.Add(this); }
        if (!openNow && Open) { CloseBreak(); IsReplica = true; return; }
        Accepting = (b & 16) != 0;
        Finishing = (b & 32) != 0;
        Airborne = (b & 64) != 0;
        int pn = b & 15;
        Anchored = pn != 0;
        replicaMine = pn != 0 && pn == NetCombat.LocalPlayerNumber;
        Owner = pn == 0 ? OwnerNone : pn;
        if (replicaMine) Local = this;
    }

    // JOIN: この端末のプレイヤーが当てた(空中の補助の相手として覚える)
    public void NoteReplicaLocalHit()
    {
        if (!IsReplica || !Open) return;
        AirHits++;
        Local = this;
    }

    // ---------------------------------------------------------------- 候補

    public static BossJuggle FindCandidate(PlayerController pc, float reachX, float reachY)
    {
        if (pc == null) return null;
        Vector2 p = pc.transform.position;
        BossJuggle best = null; float bestD = float.MaxValue;
        for (int i = open.Count - 1; i >= 0; i--)
        {
            var j = open[i];
            if (j == null || !j.Open || j.OwnerGone) { if (j != null && j.OwnerGone && Local == j) Local = null; open.RemoveAt(i); continue; }
            if (!j.Accepting) continue;
            Vector2 d = (Vector2)j.ComboAimPoint - p;
            if (d.x <= -0.8f || d.x >= reachX + 0.4f || Mathf.Abs(d.y - 0.6f) >= reachY) continue;
            float dd = d.sqrMagnitude + (j.Anchored && Local == j ? -100f : 0f);
            if (dd < bestD) { bestD = dd; best = j; }
        }
        return best;
    }
}
