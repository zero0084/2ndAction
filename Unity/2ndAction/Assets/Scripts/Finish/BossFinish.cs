using System.Collections.Generic;
using UnityEngine;

// BOSS FINISH SYSTEM(2026-10-06): ボスの撃破演出(見た目だけ)。
//
//  ゲームの処理との分離:
//   ・HP 0 の瞬間に各ボスが死亡を確定する(dead / 攻撃判定・当たり判定を全部無効 / AI・必殺技・崩し・段階の処理が止まる /
//     狙いの対象外 / 報酬(MILE/EXP/ULTIMATE のゲージ)を確定)。ここはそのボスの体を「絵として」動かすだけ。
//   ・遭遇の終了(ボス報酬のカード選択 / 次のボスの抽選・出現 / 撃破の表示)は、撃破の見た目が終わった時(OnDeathVisualFinished)。
//     = 次のボスが倒れている最中のボスと重なって出ない「短い遭遇の終了待ち」(初撃破 2.0 秒 / 再戦 1.1 秒、データで変更可)。
//   ・ラン再開(走りながらのボス戦)の後なら、走りはそのまま(止め直さない)。体はカメラ基準で動かすので走行速度で画面から流れない。
//  時間: Time.deltaTime(HitStop/停止/カード選択の間は止まる)。最後の一撃の HitStop は FinishFx(雑魚と共通: 最大値だけ)。
//  見た目: プロファイル(獣/人型/飛行/ゴーレム/大蛇/巨人)+ 最後の一撃の向き(最初の反応)+ ボス固有の上乗せ(BossDeathFx)。
public interface IBossDeathBody
{
    Transform DeathRoot { get; }
    float DeathBodyHeight { get; }
    BossRig DeathRig { get; }        // 格子のセル(無いボス = null)
    string DeathKey { get; }         // "Wild/Wolf" など(BossDeathTuning の表)
    void SetDeathColor(Color c);
    void OnDeathVisualFinished();    // 見た目が終わった: 非表示 → (HOST)遭遇の終了へ
}

public enum BossFinalAttack : byte { Forward, Back, Up, Aerial, Slam, Projectile, Other }

public struct BossFinishInfo
{
    public BossFinalAttack attack; public sbyte dir; public bool firstKill;
    public ushort Pack() => (ushort)(0x8000 | (byte)attack | ((dir < 0 ? 1 : 0) << 4) | ((firstKill ? 1 : 0) << 5));
    public static bool TryUnpack(ushort v, out BossFinishInfo fi)
    {
        fi = new BossFinishInfo { attack = (BossFinalAttack)(v & 15), dir = (sbyte)(((v >> 4) & 1) == 1 ? -1 : 1), firstKill = ((v >> 5) & 1) == 1 };
        return (v & 0x8000) != 0;
    }
    // 命中した攻撃から(この端末のプレイヤーの攻撃)
    public static BossFinishInfo FromAttack(Collider2D attack, PlayerAttackInfo info, Vector3 bossPos)
    {
        var pc = PlayerController.Instance;
        float dir = pc != null ? Mathf.Sign(bossPos.x - pc.transform.position.x) : 1f;
        if (dir == 0f) dir = 1f;
        var fi = new BossFinishInfo { dir = (sbyte)(dir < 0f ? -1 : 1), attack = BossFinalAttack.Other };
        var kp = attack != null ? attack.GetComponent<KitProjectile>() : null;
        var pb = attack != null ? attack.GetComponent<PlayerBullet>() : null;
        if ((kp != null && Mathf.Abs(kp.velocity.x) > 0.01f) || (pb != null && Mathf.Abs(pb.velocity.x) > 0.01f))
        {
            float vx = kp != null ? kp.velocity.x : pb.velocity.x;
            fi.attack = BossFinalAttack.Projectile; fi.dir = (sbyte)(vx < 0f ? -1 : 1);
            return fi;
        }
        var kind = info != null ? info.kind : PlayerAttackKind.Normal;
        bool air = pc != null && !pc.IsGrounded;
        if (kind == PlayerAttackKind.Up) fi.attack = BossFinalAttack.Up;
        else if (kind == PlayerAttackKind.Down || kind == PlayerAttackKind.DownImpact) fi.attack = BossFinalAttack.Slam;
        else if (air) fi.attack = BossFinalAttack.Aerial;
        else
        {
            float facing = pc != null ? Mathf.Sign(pc.transform.localScale.x) : 1f;
            fi.attack = Mathf.Sign(dir) == facing ? BossFinalAttack.Forward : BossFinalAttack.Back;
        }
        return fi;
    }
    public override string ToString() => $"{attack}/{(dir < 0 ? "L" : "R")}{(firstKill ? "/first" : "/rematch")}";
}

[DefaultExecutionOrder(1000)] // カメラが動いた後に置く(走行中の1フレームのずれを出さない)
public class BossFinish : MonoBehaviour
{
    public static bool Enabled => BossDeathTuning.I.enabled;
    public static int Running { get; private set; }
    public static bool AnyRunning => Running > 0;
    public static int Started { get; private set; }
    public static int Completed { get; private set; }
    public static System.Action<string> QaEvent;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static int DebugFirstKill = -1; // 開発用: -1 = 実際どおり / 0 = 再戦扱い / 1 = 初撃破扱い
#endif

    IBossDeathBody body;
    Transform root;
    BossRig rig;
    BossDeathTuning.BossEntry entry;
    BossFinishInfo info;
    float h, D, t;
    Vector2 oc, v;          // 体の中心(カメラ基準)と速度
    float rot, rotVel, sx = 1f, sy = 1f, alpha = 1f;
    Vector3 baseScale;
    bool pivotBase;         // true = 足元を軸に回す(倒れる)/ false = 中心を軸に回す(転がる)
    Color tint = Color.white;
    int bounces; bool landed, impactDone, burstDone, explodeDone; float crashP, smokeTimer;
    float realStart; bool zoomOn, slowOn, finished;
    float fxTimer;
    float[] cellRank, cellRnd; Vector2[] cellVel; float[] cellRot; bool[] cellLoose;
    static object zoomOwner, slowOwner;

    public BossFinishInfo Info => info;
    public BossDeathProfile Profile => entry.profile;
    public float Duration => D;
    public float Elapsed => t;

    // 死亡を確定した直後に呼ぶ(各ボス)。localImpact = この端末のプレイヤーの一撃(HitStop/寄り/スローを掛ける)
    public static BossFinish Begin(IBossDeathBody body, BossFinishInfo fi, Vector3 hitPos, bool localImpact)
    {
        if (body == null || body.DeathRoot == null) return null;
        var f = body.DeathRoot.gameObject.AddComponent<BossFinish>();
        f.Setup(body, fi, hitPos, localImpact);
        return f;
    }

    void Setup(IBossDeathBody b, BossFinishInfo fi, Vector3 hitPos, bool localImpact)
    {
        var tn = BossDeathTuning.I;
        body = b; root = b.DeathRoot; rig = b.DeathRig; info = fi;
        entry = tn.For(b.DeathKey);
        h = Mathf.Max(0.5f, b.DeathBodyHeight);
        D = Mathf.Max(0.4f, fi.firstKill ? tn.firstKillSeconds : tn.rematchSeconds);
        baseScale = root.localScale;
        var cam = Camera.main;
        Vector2 camP = cam != null ? (Vector2)cam.transform.position : Vector2.zero;
        oc = (Vector2)root.position + new Vector2(0f, h * 0.5f) - camP;
        pivotBase = entry.profile == BossDeathProfile.Humanoid || entry.profile == BossDeathProfile.Golem || entry.profile == BossDeathProfile.Giant;
        Running++; Started++;
        realStart = Time.unscaledTime;
        // 最後の一撃: 白い光 + 星形 + 輪(画面に対して)、HitStop(最大値だけ)、揺れ(上限)、一瞬の寄り
        if (localImpact)
        {
            FinishFx.StopFor(tn.finalHitStop);
            FinishFx.ShakeCapped(tn.finalShake);
            if (zoomOwner == null) { zoomOwner = this; zoomOn = true; CameraFollow.BossFinishZoom = tn.finalZoom; }
        }
        Vector3 center = root.position + new Vector3(0f, h * 0.5f, 0f);
        Vector3 hp = hitPos == Vector3.zero ? center : hitPos;
        FinishFx.Flash(hp, h * 1.1f, new Color(1f, 1f, 1f, 0.95f), 0.22f);
        FinishFx.Star(hp, h * 0.8f, new Color(1f, 0.97f, 0.85f, 1f), 0.3f);
        // 音(2026-10-06): 一番重い一撃(Critical: BGM を一瞬下げる)。倒した端末でなくても鳴らす(少し小さく)
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossFinishHit, localImpact ? 1f : 0.75f);
        FinishFx.Ring(center, h * 1.6f, new Color(1f, 1f, 1f, 0.75f), 0.32f);
        // 最初の反応(最後の一撃の向き/種類)。重いほど小さい
        float k = entry.deathKnockback * tn.reactionScale / Mathf.Max(0.3f, entry.deathMass);
        float d = info.dir < 0 ? -1f : 1f;
        switch (info.attack)
        {
            case BossFinalAttack.Up: v = new Vector2(d * k * 0.25f, k * 0.9f); rotVel = -d * 50f; break;
            case BossFinalAttack.Aerial: v = new Vector2(d * k * 0.9f, -k * 0.4f); rotVel = -d * 70f; break;
            case BossFinalAttack.Slam: v = new Vector2(d * k * 0.15f, -k * 0.9f); sy = 0.85f; break;
            default: v = new Vector2(d * k, k * 0.35f); rotVel = -d * 80f / Mathf.Max(1f, entry.deathMass); break;
        }
        // 格子のセル(崩れる/列ごとに沈む/光になる)
        if (rig != null)
        {
            int n = rig.CellCount;
            cellRank = new float[n]; cellRnd = new float[n]; cellVel = new Vector2[n]; cellRot = new float[n]; cellLoose = new bool[n];
            cellAlpha = new float[n]; for (int i = 0; i < n; i++) cellAlpha[i] = 1f;
            // 頭(プレイヤーの側)からの順: 0 = 尾 … 1 = 頭
            float minX = float.MaxValue, maxX = float.MinValue;
            for (int i = 0; i < n; i++) { float x = rig.Cell(i).transform.position.x; minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); }
            for (int i = 0; i < n; i++)
            {
                float x = rig.Cell(i).transform.position.x;
                float u = Mathf.InverseLerp(minX, maxX, x);
                cellRank[i] = d > 0f ? 1f - u : u; // 押された向きの反対 = プレイヤーの側 = 頭
                cellRnd[i] = Random.value;
            }
        }
        QaEvent?.Invoke($"begin {b.DeathKey} {entry.profile} {info}");
        Debug.Log($"[BossFinish] {b.DeathKey} {entry.profile} fx={entry.specialFx} {info} D={D:F1}s mass={entry.deathMass}");
    }

    void Update()
    {
        // 実時間の短い演出(寄り/スロー)。HitStop の後で
        var tn = BossDeathTuning.I;
        float rt = Time.unscaledTime - realStart;
        if (zoomOn && rt > tn.finalHitStop + tn.finalZoomSeconds) EndZoom();
        if (!slowOn && slowOwner == null && zoomOwner == this && tn.slowMotionSeconds > 0f && rt > tn.finalHitStop && rt < tn.finalHitStop + 0.05f && !TimeControl.PresentationBusy)
        {
            slowOwner = this; slowOn = true;
            TimeControl.BeginPresentationDrive(this);
            TimeControl.SetPresentationScale(this, tn.slowMotionScale);
        }
        if (slowOn && rt > tn.finalHitStop + tn.slowMotionSeconds) EndSlow();
    }

    void EndZoom() { if (!zoomOn) return; zoomOn = false; if (zoomOwner == (object)this) { zoomOwner = null; CameraFollow.BossFinishZoom = 1f; } }
    void EndSlow() { if (!slowOn) return; slowOn = false; TimeControl.EndPresentationDrive(this); if (slowOwner == (object)this) slowOwner = null; }

    void LateUpdate()
    {
        if (finished) return;
        var cam = Camera.main;
        if (cam == null) return;
        float dt = Time.deltaTime;
        if (dt > 0f)
        {
            t += dt;
            Step(dt, cam);
        }
        Apply(cam);
        if (t >= D) Finish();
    }

    float GroundRel(Camera cam)
    {
        Vector2 camP = cam.transform.position;
        float bottom = -cam.orthographicSize + 0.35f;
        var tm = TerrainManager.Instance;
        float? g = tm != null ? tm.GetHeightAt(camP.x + oc.x) : null;
        if (!g.HasValue) return bottom;
        return Mathf.Max(bottom, g.Value - camP.y);
    }

    Vector3 W(Camera cam, Vector2 rel) => new Vector3(cam.transform.position.x + rel.x, cam.transform.position.y + rel.y, 0f);
    float P => t / D;
    float Dir => info.dir < 0 ? -1f : 1f;

    void Step(float dt, Camera cam)
    {
        var tn = BossDeathTuning.I;
        float g = tn.gravity;
        float p = P;
        float ground = GroundRel(cam);
        Vector2 c = oc;
        switch (entry.profile)
        {
            case BossDeathProfile.Beast:
            {
                // 吹っ飛ぶ/崩れる → 着地 → 1〜2回転がる → 滑る → 止まる → 光になって消える
                v.y -= g * dt;
                oc += v * dt;
                rot += rotVel * dt;
                float bottom = oc.y - h * 0.5f * sy;
                if (bottom <= ground && v.y <= 0f)
                {
                    oc.y = ground + h * 0.5f * sy;
                    int maxB = entry.deathMass < 1.8f ? 2 : 1;
                    if (bounces < maxB && Mathf.Abs(v.y) > 3f)
                    {
                        bounces++;
                        v.y = Mathf.Max(2f, -v.y * 0.32f); v.x *= 0.62f;
                        Dust(cam, new Vector2(oc.x, ground), h * 0.8f);
                        FinishFx.ShakeCapped(0.05f);
                    }
                    else { v.y = 0f; landed = true; }
                }
                if (landed)
                {
                    v.x *= Mathf.Exp(-3.2f * dt);
                    // 転がる(滑る速さに合わせて)→ 止まる時は横倒し
                    if (Mathf.Abs(v.x) > 0.8f) rotVel = -v.x / (h * 0.5f) * Mathf.Rad2Deg * 0.55f;
                    else
                    {
                        float target = Mathf.Round(rot / 360f) * 360f - Dir * 70f;
                        rot = Mathf.Lerp(rot, target, 1f - Mathf.Exp(-6f * dt)); rotVel = 0f;
                        oc.y = Mathf.Lerp(oc.y, ground + h * 0.32f, 1f - Mathf.Exp(-6f * dt));
                    }
                }
                else rotVel = Mathf.Lerp(rotVel, -Dir * 160f / Mathf.Max(1f, entry.deathMass), 1f - Mathf.Exp(-2f * dt));
                Dissolve(cam, p, 0.72f, new Color(1f, 0.95f, 0.8f));
                if (!burstDone && p > 0.93f) { burstDone = true; FinishFx.BurstAt(W(cam, oc), h * 0.35f, new Color(1f, 0.9f, 0.6f), 16, true); }
                break;
            }
            case BossDeathProfile.Humanoid:
            {
                // 一瞬止まる(衝撃だけ残る)→ 膝をつく/前屈 → 短い静止 → 光の粒になって消える(爆発しない)
                if (p < 0.18f)
                {
                    v.x *= Mathf.Exp(-6f * dt);
                    if (info.attack == BossFinalAttack.Up) { v.y -= g * dt; oc.y = Mathf.Max(ground + h * 0.5f, oc.y + v.y * dt); } else v.y = 0f;
                    oc.x += v.x * dt;
                    rot = Mathf.Lerp(rot, -Dir * 12f, 1f - Mathf.Exp(-14f * dt));
                }
                else if (p < 0.36f)
                {
                    if (!impactDone) { impactDone = true; FinishFx.Flash(W(cam, oc + new Vector2(0, h * 0.1f)), h * 0.8f, new Color(1f, 1f, 1f, 0.35f), 0.45f); Halo(cam, false); }
                    oc.y = Mathf.Lerp(oc.y, ground + h * 0.5f * sy, 1f - Mathf.Exp(-10f * dt));
                }
                else if (p < 0.62f)
                {
                    float k = Mathf.SmoothStep(0f, 1f, (p - 0.36f) / 0.26f);
                    sy = Mathf.Lerp(1f, 0.78f, k);
                    rot = Mathf.Lerp(-Dir * 12f, Dir * 16f, k); // 前へ崩れる(プレイヤーの側へ)
                    oc.y = ground + h * 0.5f * sy;
                    if (!explodeDone && k > 0.95f) { explodeDone = true; Dust(cam, new Vector2(oc.x, ground), h * 0.6f); Halo(cam, true); }
                }
                Dissolve(cam, p, 0.72f, new Color(1f, 0.92f, 0.6f), rising: true);
                break;
            }
            case BossDeathProfile.Flying:
            {
                if (entry.specialFx == BossDeathFx.PhoenixFire)
                {
                    // 炎へ変わる → 炎の羽根が舞う → 空へ消える
                    v = Vector2.Lerp(v, new Vector2(0f, 3f), 1f - Mathf.Exp(-2f * dt));
                    oc += v * dt;
                    tint = Color.Lerp(Color.white, new Color(1f, 0.55f, 0.15f), Mathf.Clamp01(p * 2f));
                    fxTimer -= dt;
                    if (fxTimer <= 0f)
                    {
                        fxTimer = 0.035f;
                        Vector3 w = W(cam, oc + new Vector2(Random.Range(-0.4f, 0.4f) * h, Random.Range(-0.4f, 0.3f) * h));
                        FinishFx.Spark(w, new Vector2(Random.Range(-1f, 1f), Random.Range(2f, 5f)), h * 0.18f, Random.Range(0.4f, 0.7f), new Color(1f, Random.Range(0.4f, 0.7f), 0.1f, 0.9f), 1.5f, -3f);
                        if (Random.value < 0.4f) FinishFx.Debris(w, new Vector2(Random.Range(-2f, 2f), Random.Range(1f, 3f)), h * 0.06f, 0.9f, new Color(1f, 0.6f, 0.2f), -1f);
                    }
                    alpha = 1f - Mathf.SmoothStep(0f, 1f, (p - 0.45f) / 0.5f);
                    if (!burstDone && p > 0.9f) { burstDone = true; FinishFx.BurstAt(W(cam, oc), h * 0.4f, new Color(1f, 0.6f, 0.2f), 18, true); }
                    break;
                }
                // 翼/浮遊の姿勢を崩す → 高度を失う → 斜め下へ墜落 → 地面/画面の下で大きく弾ける
                if (!impactDone)
                {
                    v.x = Mathf.Lerp(v.x, Dir * entry.deathKnockback * 0.5f / Mathf.Sqrt(entry.deathMass), 1f - Mathf.Exp(-2f * dt));
                    v.y -= entry.deathFallSpeed * 1.6f * dt;
                    oc += v * dt;
                    rot = Mathf.Lerp(rot, Dir * 35f + Mathf.Sin(t * 25f) * 6f, 1f - Mathf.Exp(-5f * dt));
                    fxTimer -= dt;
                    if (fxTimer <= 0f) { fxTimer = 0.06f; FinishFx.Spark(W(cam, oc), -v * 0.15f, h * 0.15f, 0.35f, new Color(1f, 0.9f, 0.7f, 0.6f)); }
                    if (oc.y - h * 0.3f <= ground || p > 0.86f)
                    {
                        impactDone = true;
                        oc.y = Mathf.Max(oc.y, ground + h * 0.3f);
                        Vector3 w = W(cam, new Vector2(oc.x, Mathf.Max(ground, oc.y - h * 0.3f)));
                        FinishFx.Flash(w, h * 2f, new Color(1f, 0.95f, 0.8f, 0.9f), 0.25f);
                        FinishFx.Ring(w, h * 2.6f, new Color(1f, 0.9f, 0.6f, 0.8f), 0.35f, 0.35f);
                        FinishFx.BurstAt(w, h * 0.45f, entry.specialFx == BossDeathFx.Storm ? new Color(0.7f, 0.8f, 1f) : new Color(1f, 0.85f, 0.5f), 22, true);
                        Dust(cam, new Vector2(oc.x, ground), h * 1.2f);
                        FinishFx.ShakeCapped(0.12f);
                        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossCollapse);
                        crashP = p;
                    }
                }
                else
                {
                    // 残骸: 地面を少し滑って横倒し → 煙/光の粒になって消える(全体の長さは D のまま)
                    v.x *= Mathf.Exp(-4f * dt);
                    oc.x += v.x * dt;
                    oc.y = Mathf.Lerp(oc.y, ground + h * 0.3f, 1f - Mathf.Exp(-10f * dt));
                    rot = Mathf.Lerp(rot, Dir * 80f, 1f - Mathf.Exp(-6f * dt));
                    smokeTimer -= dt;
                    if (smokeTimer <= 0f)
                    {
                        smokeTimer = 0.09f;
                        FinishFx.Spark(W(cam, oc + new Vector2(Random.Range(-0.3f, 0.3f) * h, 0f)), new Vector2(Random.Range(-0.4f, 0.4f), Random.Range(1f, 2.2f)), h * 0.14f, 0.7f,
                            entry.specialFx == BossDeathFx.Storm ? new Color(0.75f, 0.82f, 1f, 0.5f) : new Color(0.85f, 0.8f, 0.72f, 0.45f), 1.2f, -0.5f);
                    }
                    Dissolve(cam, p, Mathf.Min(0.9f, Mathf.Max(0.62f, crashP + 0.12f)), new Color(1f, 0.9f, 0.7f));
                }
                break;
            }
            case BossDeathProfile.Golem:
            {
                // 亀裂 → コアの発光 → 破片が漏れる → 一部が崩れる → 大きな光の爆発 → 残りが崩壊
                v.x *= Mathf.Exp(-8f * dt); oc.x += v.x * dt;
                oc.y = ground + h * 0.5f;
                Color core = entry.specialFx == BossDeathFx.Crystal ? new Color(0.5f, 0.95f, 1f) : new Color(1f, 0.6f, 0.25f);
                if (p < 0.6f)
                {
                    float q = p / 0.6f;
                    oc.x += Mathf.Sin(t * 70f) * 0.02f * h * q; // 震え
                    fxTimer -= dt;
                    if (fxTimer <= 0f)
                    {
                        fxTimer = Mathf.Lerp(0.12f, 0.04f, q);
                        Vector3 cw = rig != null ? rig.Cell(Random.Range(0, rig.CellCount)).transform.position + new Vector3(0.3f, 0.3f, 0f) * (h / 6f) : W(cam, oc + Random.insideUnitCircle * h * 0.35f);
                        FinishFx.Flash(cw, h * Random.Range(0.12f, 0.22f), new Color(core.r, core.g, core.b, 0.9f), Mathf.Max(0.2f, (0.6f - p) * D));
                        FinishFx.Debris(cw, new Vector2(Random.Range(-2f, 2f), Random.Range(0f, 3f)), h * 0.04f, 0.6f, entry.specialFx == BossDeathFx.Crystal ? new Color(0.6f, 0.95f, 1f) : new Color(0.55f, 0.48f, 0.4f));
                    }
                    if (Mathf.FloorToInt(q * 6f) != Mathf.FloorToInt((q - dt / (0.6f * D)) * 6f))
                        FinishFx.Flash(W(cam, oc), h * Mathf.Lerp(0.4f, 1.2f, q), new Color(core.r, core.g, core.b, 0.7f), 0.25f); // コアが強く光っていく
                    tint = Color.Lerp(Color.white, Color.Lerp(Color.white, core, 0.4f), q);
                    if (rig != null && q > 0.55f) LooseCells(cam, 0.3f, dt);
                }
                else if (!explodeDone)
                {
                    explodeDone = true;
                    Vector3 w = W(cam, oc);
                    FinishFx.Flash(w, h * 2.4f, new Color(1f, 0.98f, 0.9f, 1f), 0.3f);
                    FinishFx.Ring(w, h * 2.8f, new Color(core.r, core.g, core.b, 0.8f), 0.4f);
                    FinishFx.BurstAt(w, h * 0.5f, core, 28, true);
                    for (int i = 0; i < 14; i++) FinishFx.Debris(w, Random.insideUnitCircle.normalized * Random.Range(4f, 10f) + Vector2.up * 4f, h * Random.Range(0.05f, 0.1f), Random.Range(0.6f, 1f), entry.specialFx == BossDeathFx.Crystal ? new Color(0.65f, 1f, 1f) : new Color(0.5f, 0.45f, 0.38f));
                    if (entry.specialFx == BossDeathFx.Crystal) for (int i = 0; i < 8; i++) FinishFx.Twinkle(w + (Vector3)(Random.insideUnitCircle * h * 0.6f), h * 0.3f, new Color(0.8f, 1f, 1f), 0.6f);
                    FinishFx.ShakeCapped(0.14f);
                    if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossCollapse);
                    if (rig != null) LooseCells(cam, 1f, dt, explode: true);
                }
                if (rig != null) StepCells(dt);
                if (p > 0.6f) alpha = rig != null ? 1f : Mathf.Clamp01(1f - (p - 0.6f) / 0.3f);
                break;
            }
            case BossDeathProfile.Serpent:
            {
                // 頭がのけぞる → 力が抜ける → 尾から順に胴体が落ちる/沈む → 最後に頭 → 大きな衝撃
                v *= Mathf.Exp(-5f * dt); oc += v * dt;
                rot = p < 0.2f ? Mathf.Lerp(rot, -Dir * 14f, 1f - Mathf.Exp(-12f * dt)) : Mathf.Lerp(rot, 0f, 1f - Mathf.Exp(-3f * dt));
                if (rig != null)
                {
                    float vs = Mathf.Max(0.01f, Mathf.Abs(rig.Cell(0).transform.lossyScale.y));
                    for (int i = 0; i < rig.CellCount; i++)
                    {
                        float ts = 0.2f + cellRank[i] * 0.55f;          // 尾 0.2 → 頭 0.75
                        float tau = Mathf.Max(0f, (p - ts) * D);
                        float drop = entry.deathFallSpeed * 0.9f * tau * tau * 1.4f;
                        float a = Mathf.Clamp01(1f - drop / (h * 1.1f));
                        rig.SetCell(i, new Vector2(0f, -drop / vs), 0f, new Color(tint.r, tint.g, tint.b, a * alpha));
                        if (entry.specialFx == BossDeathFx.CloudSink && tau > 0f && tau < dt * 1.5f && i % 3 == 0)
                            FinishFx.Cloud(rig.Cell(i).transform.position, new Vector2(Random.Range(-1f, 1f), 0.6f), h * 0.7f, 1.2f, new Color(1f, 1f, 1f, 0.75f));
                    }
                }
                else { oc.y -= entry.deathFallSpeed * Mathf.Max(0f, p - 0.3f) * dt; alpha = Mathf.Clamp01(1f - (p - 0.5f) / 0.4f); }
                if (entry.specialFx == BossDeathFx.Storm)
                {
                    fxTimer -= dt;
                    if (fxTimer <= 0f && p < 0.8f) { fxTimer = 0.08f; FinishFx.Cloud(W(cam, oc + new Vector2(Random.Range(-1f, 1f) * h, Random.Range(0f, 0.6f) * h)), new Vector2(0f, -0.5f), h * 0.6f, 1f, new Color(0.45f, 0.5f, 0.6f, 0.55f)); }
                }
                if (!impactDone && p > 0.82f)
                {
                    impactDone = true;
                    Vector3 head = W(cam, new Vector2(oc.x - Dir * h * 0.6f, ground));
                    FinishFx.Flash(head, h * 1.6f, new Color(1f, 0.95f, 0.85f, 0.9f), 0.25f);
                    FinishFx.Ring(head, h * 2.4f, new Color(1f, 0.9f, 0.7f, 0.8f), 0.35f, 0.32f);
                    FinishFx.BurstAt(head, h * 0.35f, new Color(1f, 0.85f, 0.55f), 16, false);
                    Dust(cam, new Vector2(oc.x - Dir * h * 0.6f, ground), h);
                    FinishFx.ShakeCapped(0.1f);
                }
                if (p > 0.9f) alpha = Mathf.Clamp01((1f - p) / 0.1f);
                break;
            }
            case BossDeathProfile.Giant:
            {
                // 大きく揺れる → よろめく → 膝をつく → 前か後ろへ崩れる(Titan は雲海へ沈む)→ 巨大な衝撃
                bool sink = entry.specialFx == BossDeathFx.Sink;
                v *= Mathf.Exp(-6f * dt); oc.x += v.x * dt;
                if (p < 0.3f)
                {
                    float q = p / 0.3f;
                    rot = Mathf.Sin(t * 9f) * 7f * (1f - q) - Dir * 6f * q;
                    if (Mathf.FloorToInt(q * 3f) != Mathf.FloorToInt((q - dt / (0.3f * D)) * 3f)) { oc.x += Dir * h * 0.05f; Dust(cam, new Vector2(oc.x, ground), h * 0.4f); FinishFx.ShakeCapped(0.05f); }
                    if (!sink) oc.y = ground + h * 0.5f * sy;
                }
                else if (p < 0.5f)
                {
                    float q = (p - 0.3f) / 0.2f;
                    sy = Mathf.Lerp(1f, 0.86f, q);
                    rot = Mathf.Lerp(rot, -Dir * 8f, 1f - Mathf.Exp(-8f * dt));
                    if (!sink) oc.y = ground + h * 0.5f * sy;
                    if (!explodeDone && q > 0.5f) { explodeDone = true; Dust(cam, new Vector2(oc.x - Dir * h * 0.3f, ground), h * 0.6f); }
                }
                else if (p < 0.82f)
                {
                    float q = (p - 0.5f) / 0.32f;
                    if (sink)
                    {
                        rot = Mathf.Lerp(-Dir * 8f, -Dir * 18f, q);
                        oc.y -= entry.deathFallSpeed * q * dt * 1.5f;
                        fxTimer -= dt;
                        if (fxTimer <= 0f) { fxTimer = 0.06f; FinishFx.Cloud(W(cam, new Vector2(oc.x + Random.Range(-0.6f, 0.6f) * h * 0.6f, ground)), new Vector2(Random.Range(-1.5f, 1.5f), 0.8f), h * 0.35f, 1.1f, new Color(1f, 1f, 1f, 0.7f)); }
                    }
                    else rot = Mathf.Lerp(-Dir * 8f, -Dir * 82f, q * q); // 加速しながら倒れる(押された向きへ)
                }
                else if (!impactDone)
                {
                    impactDone = true;
                    Vector3 w = W(cam, new Vector2(oc.x + (sink ? 0f : Dir * h * 0.7f), ground));
                    FinishFx.Flash(w, h * 1.4f, new Color(1f, 0.95f, 0.85f, 0.85f), 0.3f);
                    FinishFx.Ring(w, h * 3f, new Color(1f, 0.88f, 0.65f, 0.85f), 0.4f, 0.28f);
                    Dust(cam, new Vector2(w.x - cam.transform.position.x, ground), h * 1.8f);
                    for (int i = 0; i < 10; i++) FinishFx.Debris(w, new Vector2(Random.Range(-8f, 8f), Random.Range(3f, 9f)), h * 0.05f, 0.7f, new Color(0.55f, 0.47f, 0.38f));
                    FinishFx.ShakeCapped(0.16f);
                    if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossCollapse);
                }
                if (p > 0.84f) alpha = Mathf.Clamp01((1f - p) / 0.16f);
                break;
            }
        }
        // ボス固有の上乗せ(プロファイル共通の上に)
        if (entry.specialFx == BossDeathFx.Lightning && p > 0.55f && p < 0.85f)
        {
            fxTimer -= dt;
            if (fxTimer <= 0f)
            {
                fxTimer = 0.07f;
                Vector3 w = W(cam, oc + new Vector2(Random.Range(-0.5f, 0.5f) * h, 0f));
                FinishFx.Pillar(w + new Vector3(0f, h * 2f, 0f), h * 0.12f, h * 5f, new Color(0.7f, 0.85f, 1f, 0.9f), 0.18f);
                FinishFx.Flash(w, h * 0.5f, new Color(0.8f, 0.9f, 1f, 0.8f), 0.15f);
            }
        }
    }

    // 体が光の粒になって消える(セルごとに少しずつ / セルが無ければ全体)
    bool dissolveSe;
    void Dissolve(Camera cam, float p, float from, Color sparkColor, bool rising = false)
    {
        if (p < from) return;
        if (!dissolveSe) { dissolveSe = true; if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossDissolve); }
        float q = Mathf.Clamp01((p - from) / (1f - from));
        tint = Color.Lerp(Color.white, new Color(1.2f, 1.15f, 1f), q);
        if (rig != null)
        {
            for (int i = 0; i < rig.CellCount; i++)
            {
                float th = rising ? (1f - rig.CellUv(i).y) * 0.6f + cellRnd[i] * 0.4f : cellRnd[i];
                float a = Mathf.Clamp01((th + 0.15f - q) * 4f);
                var cl = rig.Cell(i);
                if (a < 0.98f && a > 0.02f && Random.value < 0.08f)
                    FinishFx.Spark(cl.transform.position, new Vector2(Random.Range(-0.6f, 0.6f), Random.Range(1.5f, 3.5f)), h * 0.07f, Random.Range(0.4f, 0.8f), sparkColor, 1f, -1f, true);
                cellAlpha[i] = a;
            }
        }
        else
        {
            alpha = 1f - q;
            fxTimer -= Time.deltaTime;
            if (fxTimer <= 0f) { fxTimer = 0.03f; FinishFx.Spark(W(cam, oc + Random.insideUnitCircle * h * 0.4f), new Vector2(0f, Random.Range(1.5f, 3f)), h * 0.08f, 0.6f, sparkColor, 1f, -1f, true); }
        }
    }
    float[] cellAlpha;

    // 天界の守護者: 光輪(砕ける/静かに光へ)
    void Halo(Camera cam, bool shatter)
    {
        if (entry.specialFx != BossDeathFx.Halo) return;
        Vector3 head = W(cam, oc + new Vector2(0f, h * 0.55f));
        if (!shatter) FinishFx.Ring(head, h * 0.7f, new Color(1f, 0.9f, 0.5f, 0.9f), 0.5f, 0.35f);
        else for (int i = 0; i < 10; i++) FinishFx.Debris(head, new Vector2(Random.Range(-4f, 4f), Random.Range(1f, 5f)), h * 0.04f, 0.8f, new Color(1f, 0.88f, 0.45f), 12f);
    }

    void Dust(Camera cam, Vector2 rel, float size)
    {
        Vector3 w = W(cam, rel);
        FinishFx.Ring(w, size * 1.6f, new Color(0.85f, 0.75f, 0.6f, 0.6f), 0.35f, 0.3f);
        for (int i = 0; i < 6; i++) FinishFx.Cloud(w + new Vector3(Random.Range(-0.4f, 0.4f) * size, 0.1f, 0f), new Vector2(Random.Range(-3f, 3f), Random.Range(0.5f, 1.5f)), size * 0.35f, 0.6f, new Color(0.8f, 0.72f, 0.6f, 0.55f));
    }

    // ゴーレム: セルが外れて崩れる
    void LooseCells(Camera cam, float fraction, float dt, bool explode = false)
    {
        Vector2 center = (Vector2)W(cam, oc);
        for (int i = 0; i < rig.CellCount; i++)
        {
            if (cellLoose[i] || cellRnd[i] > fraction) continue;
            cellLoose[i] = true;
            Vector2 cp = rig.Cell(i).transform.position;
            Vector2 outv = (cp - center).normalized;
            cellVel[i] = explode ? outv * Random.Range(4f, 9f) + Vector2.up * Random.Range(2f, 6f) : new Vector2(outv.x * 1.5f, Random.Range(0f, 1.5f));
            cellRot[i] = 0f;
        }
    }
    Vector2[] cellOff;
    void StepCells(float dt)
    {
        if (cellOff == null) cellOff = new Vector2[rig.CellCount];
        float vs = Mathf.Max(0.01f, Mathf.Abs(rig.Cell(0).transform.lossyScale.y));
        float vx = Mathf.Max(0.01f, Mathf.Abs(rig.Cell(0).transform.lossyScale.x));
        float g = BossDeathTuning.I.gravity;
        for (int i = 0; i < rig.CellCount; i++)
        {
            if (!cellLoose[i]) continue;
            cellVel[i].y -= g * dt;
            cellOff[i] += cellVel[i] * dt;
            cellRot[i] += 360f * dt * (cellRnd[i] - 0.5f) * 2f;
            float a = Mathf.Clamp01(1f - cellOff[i].magnitude / (h * 1.4f));
            cellAlpha[i] = a;
            float sgn = Mathf.Sign(rig.Cell(i).transform.lossyScale.x);
            rig.SetCell(i, new Vector2(cellOff[i].x / vx * sgn, cellOff[i].y / vs), cellRot[i], Color.white);
        }
    }

    void Apply(Camera cam)
    {
        Vector2 camP = cam.transform.position;
        root.localScale = new Vector3(baseScale.x * sx, baseScale.y * sy, baseScale.z);
        root.rotation = Quaternion.Euler(0f, 0f, rot);
        Vector2 basePos;
        if (pivotBase) basePos = camP + oc - new Vector2(0f, h * 0.5f * sy);
        else basePos = camP + oc + (Vector2)(Quaternion.Euler(0f, 0f, rot) * new Vector3(0f, -h * 0.5f * sy, 0f));
        root.position = new Vector3(basePos.x, basePos.y, root.position.z);
        Color c = new Color(tint.r, tint.g, tint.b, alpha);
        if (rig != null)
        {
            // セルごとの濃さ(光になる/崩れる)× 全体の濃さ。大蛇は列ごとの沈み込みの中で塗る
            if (entry.profile != BossDeathProfile.Serpent)
                for (int i = 0; i < rig.CellCount; i++) rig.Cell(i).color = new Color(tint.r, tint.g, tint.b, Mathf.Min(cellAlpha[i], alpha));
        }
        else body.SetDeathColor(c);
    }

    void Finish()
    {
        if (finished) return;
        finished = true;
        EndZoom(); EndSlow();
        Running = Mathf.Max(0, Running - 1); Completed++;
        QaEvent?.Invoke($"done {body.DeathKey}");
        try { body.OnDeathVisualFinished(); } catch (System.Exception e) { Debug.LogException(e); }
        Destroy(this);
    }

    void OnDisable()
    {
        // 途中で消えた(シーンの読み直し/片付け)時も、寄り/スロー/数え上げを残さない
        if (!finished) { finished = true; EndZoom(); EndSlow(); Running = Mathf.Max(0, Running - 1); }
    }

    // GAME OVER / ランの終わり: 撃破の見た目を止めて消す(遭遇の終了の処理はしない: ランは終わっている。報酬は確定済み)
    public static void StopAll()
    {
        foreach (var f in FindObjectsByType<BossFinish>(FindObjectsSortMode.None))
        {
            if (f == null || f.finished) continue;
            f.finished = true; f.EndZoom(); f.EndSlow(); Running = Mathf.Max(0, Running - 1);
            if (f.root != null) f.root.gameObject.SetActive(false);
            Destroy(f);
        }
        CameraFollow.BossFinishZoom = 1f;
        zoomOwner = null; slowOwner = null;
    }

    // ボスの攻撃の残り(弾/設置/予告/落石/空の帯)を消す。生き残っているボスが居なければ(=遭遇の最後のボス)
    public static void ClearBossHazards()
    {
        foreach (var b in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (b != null && !b.IsDead && b.isActiveAndEnabled) return;
        foreach (var b in FindObjectsByType<DragonController>(FindObjectsSortMode.None)) if (b != null && b.isActiveAndEnabled && !b.IsDeadForFinish) return;
        foreach (var b in FindObjectsByType<MajinController>(FindObjectsSortMode.None)) if (b != null && b.isActiveAndEnabled && !b.IsDeadForFinish) return;
        int n = 0;
        n += DestroyAll<BossProjectile>() + DestroyAll<TrackedHazard>() + DestroyAll<SkyStrike>() + DestroyAll<SkyWarnBand>() + DestroyAll<SkyFlyby>() + DestroyAll<SkyDrift>();
        // ボスの火球だけ(雑魚の弾/跳ね返した火球は残す)
        foreach (var f in FindObjectsByType<FireballController>(FindObjectsSortMode.None)) if (f != null && f.bossOwned && !f.reflected) { Destroy(f.gameObject); n++; }
        n += CaveHazard.LiveCount;
        CaveHazard.ClearAll();
        LastHazardsCleared = n;
        if (n > 0) Debug.Log($"[BossFinish] cleared {n} boss hazards (last boss of the encounter down)");
    }
    public static int LastHazardsCleared { get; private set; }
    static int DestroyAll<T>() where T : Component
    {
        int n = 0;
        foreach (var c in FindObjectsByType<T>(FindObjectsSortMode.None)) if (c != null) { Destroy(c.gameObject); n++; }
        return n;
    }
}
