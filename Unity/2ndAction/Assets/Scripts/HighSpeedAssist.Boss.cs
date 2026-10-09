using System.Collections.Generic;
using UnityEngine;

// 自動操作補助のボス戦対応(2026-10-04)。補助がONなら、ボスと戦っている間は速さに関係なく働く(以前はボスに無反応だった)。
//  ・回避: 予告(洞窟の地形攻撃 CaveHazard / 迫ってくるボス弾・衝撃波 / 有効になったボス本体の判定)を見て
//          赤=低い → 跳ぶ(有効な時間に体が上にあるよう踏み切りを合わせる) / 金=背が高い → 二段ジャンプ /
//          紫=天井から・高い → 地面にいる(跳ばない。空中なら下攻撃で早く降りる)。
//  ・接近と攻撃: このゲームに横移動は無いので、ボス戦中に大きく伸びる攻撃の踏み込み(BossBattle.LungeScale)で間合いを詰める。
//          届く距離なら前攻撃、頭上の高さのボスは跳んでから前攻撃、空中でボスの真上付近なら下攻撃(崩しが大きい)。
//          無敵/地中/天井にいる間(判定が無い間)は攻撃しない。
//  ・手動入力が常に優先(既存の補助と同じ)。回避の判断が攻撃より先。
public partial class HighSpeedAssist
{
    [Header("ボス戦(2026-10-04)")]
    [Tooltip("ボスと戦っている間は、速さに関係なく補助を働かせる")]
    public bool bossAssist = true;
    [Tooltip("前攻撃でボスへ踏み込む最大の距離(m)。ボス戦中は攻撃の踏み込みが伸びる")]
    public float bossApproachReach = 7f;
    [Tooltip("回避の踏み切りに使う余裕(秒)")]
    public float bossJumpMargin = 0.04f;

    public int BossDodgeJumps, BossDoubleJumps, BossStayLow, BossAttacks, BossDownAttacks, BossTerrainJumps;
    public string BossPlan { get; private set; } = "";

    bool BossFightNear(PlayerController pc)
    {
        if (!bossAssist || !BossBattle.AnyBossFighting) return false;
        float px = pc.transform.position.x;
        foreach (var b in BossBattle.Living)
        {
            if (b == null || !b.isActiveAndEnabled) continue;
            if (Mathf.Abs(b.transform.position.x - px) < 30f) return true;
        }
        return false;
    }

    struct Threat { public float tc, td, needY; public bool ceiling; public string what; }
    readonly List<Threat> threats = new List<Threat>();

    // 単発ジャンプで高さh以上にいる時間帯 [t1, t2](踏み切りから)。届かなければ false
    bool JumpWindow(PlayerController pc, float h, out float t1, out float t2)
    {
        float v0 = pc.AssistJumpForce, g = Mathf.Max(1f, pc.AssistGravity);
        float disc = v0 * v0 - 2f * g * h;
        if (disc <= 0f) { t1 = t2 = 0f; return false; }
        float r = Mathf.Sqrt(disc);
        t1 = (v0 - r) / g; t2 = (v0 + r) / g;
        return true;
    }

    void CollectThreats(PlayerController pc)
    {
        threats.Clear();
        float px = pc.transform.position.x;
        float pad = halfW + 0.35f;
        // ---- 洞窟の地形攻撃 ----
        foreach (var h in CaveHazard.Live)
        {
            if (h == null || !h.Damaging) continue;
            float rel = h.transform.position.x - px;
            float half = h.Width * 0.45f;
            var t = new Threat { ceiling = h.CeilingType, needY = h.CeilingType ? 0f : h.TopY + 0.15f, what = h.Kind.ToString() };
            float drift = h.Drift;
            switch (h.Kind)
            {
                case CaveHazardKind.Floor:
                case CaveHazardKind.Ceiling:
                case CaveHazardKind.Band:
                    if (Mathf.Abs(rel) > half + pad) continue;
                    t.tc = Mathf.Max(0f, h.WarnLeft);
                    t.td = h.ActiveLeft;
                    break;
                case CaveHazardKind.FallRock:
                    if (!h.Landed) continue; // 落ちてくる岩は前方に落ちる(着地して転がってくる方を避ける)
                    if (rel - half - pad < 0f) { if (rel + half + pad < 0f) continue; t.tc = 0f; } else t.tc = (rel - half - pad) / Mathf.Max(0.5f, drift);
                    t.td = (half * 2f + pad * 2f) / Mathf.Max(0.5f, drift);
                    break;
                default: // Wave / Pool / Pillar: 流れてくる
                    if (Mathf.Abs(drift) < 0.1f) continue;
                    float d = drift > 0f ? rel - half - pad : -rel - half - pad; // 前から(drift>0)/後ろから(drift<0)
                    if (d < -(half * 2f + pad * 2f)) continue; // 通り過ぎた
                    t.tc = Mathf.Max(0f, d / Mathf.Abs(drift));
                    t.tc = Mathf.Max(t.tc, h.WarnLeft);
                    t.td = (half * 2f + pad * 2f) / Mathf.Abs(drift);
                    break;
            }
            if (t.tc > 1.6f) continue;
            threats.Add(t);
        }
        // ---- 迫ってくるボス弾/衝撃波(地面すれすれ = 跳ぶ) ----
        foreach (var bp in FindObjectsByType<BossProjectile>(FindObjectsSortMode.None))
        {
            if (bp == null || !bp.damage) continue;
            var c = bp.GetComponent<Collider2D>();
            if (c == null) continue;
            Bounds b = c.bounds;
            float vx = bp.velocity.x; // 走行の座標系での速さ
            float rel = b.center.x - px;
            if (rel * vx >= 0f) continue; // 離れていく
            float gy = pc.transform.position.y;
            if (b.min.y > gy + 1.9f) { if (Mathf.Abs(rel) < b.extents.x + pad + Mathf.Abs(vx) * 0.6f) threats.Add(new Threat { ceiling = true, tc = Mathf.Max(0f, (Mathf.Abs(rel) - b.extents.x - pad) / Mathf.Abs(vx)), td = (b.size.x + pad * 2f) / Mathf.Abs(vx), what = "highShot" }); continue; }
            float dd = Mathf.Abs(rel) - b.extents.x - pad;
            threats.Add(new Threat { tc = Mathf.Max(0f, dd / Mathf.Max(0.5f, Mathf.Abs(vx))), td = (b.size.x + pad * 2f) / Mathf.Max(0.5f, Mathf.Abs(vx)), needY = b.max.y - gy + 0.15f, what = "shot" });
        }
        // ---- 有効になったボス本体の判定(突進/薙ぎ払い): 体の近くで低い物は跳ぶ ----
        foreach (var lb in BossBattle.Living)
        {
            if (!(lb is WildBossBase w) || w.IsDead || !w.isActiveAndEnabled) continue;
            foreach (var hb in w.GetComponentsInChildren<BossHitbox>())
            {
                if (hb == null || !hb.IsActive || !hb.damagesPlayer) continue;
                var c = hb.GetComponent<Collider2D>();
                if (c == null) continue;
                Bounds b = c.bounds;
                float gy = pc.transform.position.y;
                float dd = Mathf.Max(0f, Mathf.Abs(b.center.x - px) - b.extents.x - pad);
                if (dd > 3.5f) continue;
                if (b.min.y > gy + 1.9f) { threats.Add(new Threat { ceiling = true, tc = 0f, td = 0.3f, what = "bodyHigh" }); continue; }
                threats.Add(new Threat { tc = dd / 12f, td = 0.4f, needY = b.max.y - gy + 0.15f, what = "body" });
            }
        }
    }

    // 戻り値: 入力 / stayLow: このフレームは跳ばない(天井から・高い攻撃が来る)
    PlayerController.FlickDirection? DecideBoss(PlayerController pc, float frameDt, out bool stayLow)
    {
        stayLow = false;
        CollectThreats(pc);
        bool grounded = pc.AssistJumpsUsed == 0 && Mathf.Abs(pc.AssistVelocityY) < 0.01f;
        // ---- 天井から/高い攻撃: 地面にいる ----
        float ceilSoon = float.MaxValue;
        foreach (var t in threats) if (t.ceiling && t.tc < ceilSoon) ceilSoon = t.tc;
        if (ceilSoon < 0.9f)
        {
            stayLow = true;
            if (!grounded && pc.canUseDownAttack && pc.AssistVelocityY < 4f && !pc.AssistIsDiveOrHover && Time.time >= manualJumpUntil)
            {
                BossDownAttacks++; BossStayLow++;
                BossPlan = "天井/高い攻撃 → 下攻撃で降りる";
                Act("下攻撃(降りる)", BossPlan);
                return PlayerController.FlickDirection.Down;
            }
        }
        // ---- 低い/背が高い攻撃: 有効な時間に体が上にあるよう跳ぶ ----
        Threat? next = null;
        foreach (var t in threats) if (!t.ceiling && t.needY > 0.05f && (next == null || t.tc < next.Value.tc)) next = t;
        if (next.HasValue && Time.time >= manualJumpUntil)
        {
            var t = next.Value;
            bool tall = t.needY > 1.9f;
            float h = Mathf.Min(t.needY, 1.9f);
            if (grounded && !stayLow)
            {
                if (JumpWindow(pc, h, out float t1, out float t2))
                {
                    // 有効な時間の始まりに高さhへ届いている踏み切り(長い/背が高い物は二段で延ばす)
                    float takeoffIn = t.tc - t1 - bossJumpMargin;
                    if (takeoffIn <= frameDt * 1.5f && t.tc + Mathf.Min(t.td, tall ? 9f : t2 - t1) > 0f)
                    {
                        BossDodgeJumps++;
                        BossPlan = $"{t.what}(高さ{t.needY:F1}) を跳んで避ける{(tall ? "(二段)" : "")}";
                        Act("ジャンプ(回避)", BossPlan);
                        return PlayerController.FlickDirection.Up;
                    }
                    BossPlan = $"{t.what} まで {t.tc:F2}秒 → {takeoffIn:F2}秒後に跳ぶ";
                }
            }
            else if (!grounded && pc.AssistJumpsUsed < pc.AssistMaxJumps && !stayLow)
            {
                // 空中: 背が高い物 / 着地の頃に有効な物は二段ジャンプで越える
                bool falling = pc.AssistVelocityY < 1.5f;
                bool lowNow = pc.transform.position.y - GroundYUnder(pc) < t.needY;
                if (falling && (tall || lowNow) && t.tc < 0.35f)
                {
                    BossDoubleJumps++;
                    BossPlan = $"{t.what} を二段ジャンプで越える";
                    Act("二段ジャンプ(回避)", BossPlan);
                    return PlayerController.FlickDirection.Up;
                }
            }
            if (t.tc < 0.6f) return null; // 回避の準備中は攻撃を出さない(踏み切りが遅れないように)
        }
        // ---- 接近と攻撃 ----
        return BossAttack(pc, grounded, stayLow);
    }

    float GroundYUnder(PlayerController pc)
    {
        var tm = TerrainManager.Instance;
        float x = pc.transform.position.x;
        return tm != null ? (tm.GetHeightAt(x) ?? pc.transform.position.y) : pc.transform.position.y;
    }

    PlayerController.FlickDirection? BossAttack(PlayerController pc, bool grounded, bool stayLow)
    {
        if (Time.time < manualAttackUntil) return null;
        WildBossBase best = null; float bestD = float.MaxValue;
        Vector3 p = pc.transform.position;
        foreach (var lb in BossBattle.Living)
        {
            if (!(lb is WildBossBase w) || !w.AssistTargetable) continue;
            Bounds b = w.AssistBounds;
            float d = b.min.x > p.x ? b.min.x - p.x : (b.max.x < p.x ? p.x - b.max.x : 0f);
            if (b.center.x < p.x - 1.5f) continue; // 後ろのボスは狙わない(前攻撃が届かない)
            if (d < bestD) { bestD = d; best = w; }
        }
        if (best == null) { BossPlan = "攻撃できるボスがいない(無敵/地中/天井)"; return null; }
        Bounds bb = best.AssistBounds;
        float reach = pc.AssistForwardIsProjectile ? 12f : Mathf.Max(pc.AssistForwardReach, bossApproachReach);
        if (bestD > reach) { BossPlan = $"{best.bossName} まで {bestD:F1}m(届くのを待つ)"; return null; }
        float feet = p.y, top = p.y + bodyH;
        // 空中で真上付近: 下攻撃(地面への衝撃で崩しが大きい)
        if (!grounded && pc.canUseDownAttack && bestD < 1.2f && bb.max.y < feet + 0.3f && !pc.AssistIsDiveOrHover)
        {
            BossDownAttacks++; BossAttacks++;
            BossPlan = $"{best.bossName} の真上 → 下攻撃";
            Act("下攻撃(ボス)", BossPlan);
            return PlayerController.FlickDirection.Down;
        }
        // 頭上のボス: 跳んでから攻撃(天井から何か来る時は跳ばない)
        if (grounded && !stayLow && bb.min.y > top + 0.4f && bestD < 3f && Time.time >= manualJumpUntil)
        {
            BossPlan = $"{best.bossName} は高い所 → 跳んで攻撃";
            Act("ジャンプ(ボスへ)", BossPlan);
            return PlayerController.FlickDirection.Up;
        }
        if (!pc.AssistCanStartForwardAttack) return null;
        if (bb.max.y < feet - 0.5f || bb.min.y > top + 1.2f) return null; // 高さが合わない
        BossAttacks++;
        BossPlan = $"{best.bossName} へ前攻撃(距離{bestD:F1}m)";
        Act("前攻撃(ボス)", BossPlan);
        return PlayerController.FlickDirection.Forward;
    }
}
