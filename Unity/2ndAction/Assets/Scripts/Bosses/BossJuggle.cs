using System.Collections.Generic;
using UnityEngine;

// ボスの BREAK 中のエリアルコンボ(2026-10-18)。
//  普通の敵(EnemyController.AerialCombo)と同じ考え方: この端末のプレイヤーが手動の攻撃を当て続けている間だけ、
//  ボスの体(被弾判定)の手前の端を「そのキャラの空中攻撃が当たる所」へ寄せる(走行の速さは変えない。相対の補正だけ)。
//   Launch: 小〜中型と飛行のボス = 体ごと打ち上げる(↑で打ち上げ/打ち上げ直し、→で滞空、↓で下へ押して地面で叩きつけ)
//   Kneel : 大型/固定のボス = 動かさない(BREAK の寄せの位置のまま)。膝をついて低くなり、空中の追撃・再上昇・↓の締めが当たる
//  寄せるのは BREAK の間だけ。通常時のボスは今までどおり(打ち上がらない)。
//  ボス側(WildBossBase / DragonController / MajinController)は、毎フレーム Step の結果(中心の移動量)を自分の座標へ反映する。
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

    public static BossJuggle Local { get; private set; }
    // 開発用(比較動画): false = 改修前と同じ(BREAK 中も打ち上げない・怯まない・延びない・予告/立ち直りなし)。ゲーム中は常に true
    public static bool Enabled = true;
    static readonly List<BossJuggle> open = new List<BossJuggle>();

    // 確認用(QA)
    public static int Launches, Relaunches, Reanchors, DownPushes, Slams, KneelHits, KneelSlams, EndedFar, EndedTimeout, EndedLanded, AirLandings;

    public readonly Style style;
    readonly System.Func<Bounds> hurt;
    public readonly string name;
    public bool Open { get; private set; }
    public bool Accepting { get; private set; }  // 回復の予告の後は新しく寄せない(落ちて立ち直る)
    public bool Anchored { get; private set; }
    public bool Airborne { get; private set; }   // 体が地面(休む高さ)より上(打ち上げ/落下中)
    public float VelY { get; private set; }
    public int AirHits { get; private set; }
    public float MaxRise { get; private set; }   // 確認用: 地面からいちばん上がった高さ
    public string EndReason = "";
    float window, total, rise, down, lift;
    bool playerWasAir;

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
    public bool PushingDown => down > 0f || (PlayerController.Instance != null && PlayerController.Instance.IsAerialDiving);

    // 遠距離の技の狙い/届くかの判定に使う点: 体のうち、そのキャラの攻撃が当たる所に一番近い点(端から少し内側)
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

    // ---------------------------------------------------------------- BREAK の始まり/終わり

    public void OpenBreak(bool airborne)
    {
        Open = true; Accepting = Enabled;
        Anchored = false;
        Airborne = style == Style.Launch && airborne;
        VelY = 0f; AirHits = 0; MaxRise = 0f;
        window = total = rise = down = lift = 0f;
        EndReason = "";
        if (!open.Contains(this)) open.Add(this);
    }

    // 回復の予告: これ以降は寄せない(浮いていれば落ちる → 地面で立ち直る)
    public void StopAccepting()
    {
        Accepting = false;
        if (Anchored) EndAnchor("recovering");
    }

    public void CloseBreak()
    {
        Open = false; Accepting = false;
        Anchored = false; Airborne = false;
        VelY = 0f; down = 0f;
        open.Remove(this);
        if (Local == this) Local = null;
    }

    // 雑魚のコンボが始まった時など: この端末のプレイヤーの寄せの相手を手放す
    public static void ReleaseLocal()
    {
        if (Local != null && Local.Anchored) Local.EndAnchor("switched");
        Local = null;
    }

    void BeginAnchor()
    {
        var pc = PlayerController.Instance;
        if (!Anchored) { total = 0f; playerWasAir = pc != null && !pc.IsGrounded; }
        Anchored = true;
        window = Window;
        if (Local != null && Local != this) Local.EndAnchor("switched");
        Local = this;
        EnemyController.ReleaseLocalComboTarget(); // 雑魚を寄せていたら手放す(同時に2体を寄せない)
    }

    void EndAnchor(string reason)
    {
        Anchored = false;
        window = 0f; lift = 0f; down = 0f;
        EndReason = reason;
    }

    // ---------------------------------------------------------------- 命中

    // この端末のプレイヤーの手動の攻撃が当たった(BREAK 中)。戻り値: 叩きつけの締めが入った(Kneel の ↓)
    public bool OnLocalHit(PlayerAttackKind kind)
    {
        if (!Open || !Accepting) return false;
        var pc = PlayerController.Instance;
        if (pc == null) return false;
        bool isDown = kind == PlayerAttackKind.Down || kind == PlayerAttackKind.DownImpact;
        if (style == Style.Kneel)
        {
            // 大型: 位置は動かさない。空中/↑の命中でコンボを続ける(プレイヤーの再上昇/滞空/↓の締めが使える)
            bool airHit = !pc.IsGrounded || kind == PlayerAttackKind.Up;
            if (airHit || Anchored) { BeginAnchor(); AirHits++; KneelHits++; RegisterAir(isDown); }
            if (isDown && Anchored) { KneelSlams++; return true; }
            return false;
        }
        if (kind == PlayerAttackKind.Up)
        {
            bool wasAir = Airborne;
            BeginAnchor();
            down = 0f;
            if (!wasAir) { Airborne = true; rise = RiseTime; VelY = LaunchSpeed; AirHits = 1; Launches++; }
            else { lift = 1.0f; VelY = Mathf.Max(VelY, 5f); AirHits++; Relaunches++; RegisterAir(false); }
            return false;
        }
        if (!Airborne) return false; // 打ち上げる前(地上)の → / ↓ は普通の命中
        if (!Anchored) { BeginAnchor(); Reanchors++; } // 落ちてくる所を空中で捉えた
        window = Window;
        AirHits++;
        if (isDown)
        {
            down = DownPushTime; lift = 0f; DownPushes++;
            if (kind == PlayerAttackKind.DownImpact) VelY = Mathf.Min(VelY, -14f);
        }
        else down = 0f; // ↓の後に → / ↑ が当たった: つなぎ直し
        RegisterAir(isDown);
        return false;
    }

    void RegisterAir(bool final)
    {
        if (ComboCounterUI.Instance != null && AirHits >= 2) ComboCounterUI.Instance.RegisterAir(AirHits, final);
    }

    // ↓の弾/札などから直接(プレイヤー側の締めの技)
    public bool ComboTryDownPush()
    {
        if (!Open || !Anchored) return false;
        if (style == Style.Launch) { down = DownPushTime; lift = 0f; window = Window; DownPushes++; }
        return true;
    }

    // ---------------------------------------------------------------- 毎フレーム

    // Kneel: 寄せの時間を進めるだけ。Launch: 中心の移動量を返す。
    //   center/ext = 被弾判定の中心と半分の大きさ(今)、restCenterY = 地面に立っている時の中心の高さ、
    //   ceilCenterMax = 頭が天井に入らない中心の高さの上限(天井が無ければ +∞)
    //   slammed = ↓で押して地面へ届いた(叩きつけ)、landed = 落ちて地面に着いた
    public Vector2 Step(Vector2 center, Vector2 ext, float restCenterY, float ceilCenterMax, float dt, out bool slammed, out bool landed)
    {
        slammed = false; landed = false;
        var pc = PlayerController.Instance;
        if (!Open) return Vector2.zero;
        if (Anchored)
        {
            window -= dt; total += dt;
            bool pAir = pc != null && !pc.IsGrounded;
            if (pAir) playerWasAir = true;
            if (pc == null) EndAnchor("noPlayer");
            else if (window <= 0f || total > MaxTotal) { EndAnchor("timeout"); EndedTimeout++; }
            else if (playerWasAir && !pAir && style == Style.Launch) { EndAnchor("playerLanded"); EndedLanded++; }
        }
        if (style == Style.Kneel || (!Airborne && !Anchored)) return Vector2.zero;

        float vx = 0f, vy;
        if (Anchored)
        {
            Vector2 sweet = pc.AerialComboSweetSpot();
            lift = Mathf.MoveTowards(lift, 0f, 2.2f * dt);
            if (down > 0f) down -= dt;
            float push = down > 0f ? -DownPushDepth : 0f;
            // 体の手前の端を「当たる所」の少し手前へ。高さは体の中心をそのキャラの攻撃の高さへ
            float nearEdge = Mathf.Clamp(sweet.x - 0.55f, 0.25f, 2.3f);
            Vector2 p = pc.transform.position;
            Vector2 target = new Vector2(p.x + nearEdge + ext.x, p.y + sweet.y + lift + push);
            if (!PushingDown) target.y = Mathf.Max(target.y, restCenterY);
            Vector2 diff = target - center;
            if (diff.magnitude > EngageDistance) { EndAnchor($"far({diff.x:F1},{diff.y:F1})"); EndedFar++; }
            else
            {
                Vector2 corr = Vector2.ClampMagnitude(diff * Gain, MaxCorrection);
                Vector2 pv = pc.MotionVelocity;
                // 走行の分はボス側が既に足している(並走)。プレイヤーの踏み込み/急降下の一瞬の速さ(1フレームで数m)はそのまま追わない
                // (前のフレームの速さなので1フレーム遅れて、ボスだけが跳んで見えた)。差は位置の補正(corr)で滑らかに詰める
                vx = Mathf.Clamp(pv.x - pc.CurrentAutoRunSpeed, -FollowMax, FollowMax) + corr.x;
                if (!pc.IsGrounded) vy = Mathf.Clamp(pv.y, -FollowMax, FollowMax) + corr.y;
                else { VelY -= Gravity * dt; vy = VelY; }    // まだ地上(打ち上げた直後): 普通に上がる
                if (rise > 0f) { rise -= dt; vy = Mathf.Max(vy, RiseMinSpeed); }
                vy = Mathf.Clamp(vy, -24f, 18f);
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
            float pushRate = Mathf.Max(CeilingPushDownPerSec, (pc != null ? pc.CurrentAutoRunSpeed : 0f) * 1.2f);
            ny = Mathf.Max(ceilCenterMax, Mathf.Min(ny, center.y - pushRate * dt));
        }
        if (ny <= restCenterY)
        {
            ny = restCenterY;
            if (Anchored && PushingDown && VelY <= 0f && rise <= 0f)
            {
                slammed = true; Slams++;
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
