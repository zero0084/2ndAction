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

    public int BossDodgeJumps, BossDoubleJumps, BossStayLow, BossAttacks, BossDownAttacks, BossTerrainJumps, BossTerrainOverStayLow, BossShotJumps, BossApproachLunges;
    float outOfReachSince = -1f; // 2026-10-16: 届く距離の外にボスが居続けた時刻
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
        float g = Mathf.Max(1f, pc.AssistGravity);
        float v0 = pc.AssistJumpForce - 0.5f * g * PlayerController.GravityRefStep; // 2026-10-13 第3弾: 実際の軌道(PlayerController.Move)と同じ
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
        // ---- 空からの着弾(SkyStrike: メカドラゴンの空爆など。2026-10-13 まで補助が知らず、立ったまま当たっていた) ----
        // 走行と一緒に流れるので、プレイヤーとの位置の差は変わらない。自分の列に来る物だけ、有効な時間に爆発の上にいるよう跳ぶ
        foreach (var s in FindObjectsByType<SkyStrike>(FindObjectsSortMode.None))
        {
            if (s == null || !s.Damaging) continue;
            float rel = s.WorldX - px;
            if (Mathf.Abs(rel) > s.Width * 0.5f + pad) continue;
            if (s.WarnLeft > 1.6f) continue;
            threats.Add(new Threat { tc = s.WarnLeft, td = s.ActiveLeft, needY = s.Height + 0.15f, what = "strike" });
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

    // 急降下(下攻撃)の着地点に足場があるか(2026-10-12)。急降下は残りの滞空/空中ジャンプを捨てて真下へ落ちるので、
    // 穴の上で選ぶとそのまま落ちる(お嬢様の天空回廊のクラゲ戦で 1戦あたりの落下 0.6 → 2.6回)。
    // 今の位置から、落ち切るまでの前進ぶん(0.45秒)+ 0.6m 先まで、どこかが穴/足場なしなら急降下しない(穴を跳び越えてから使う)。
    public static int BossDiveSkippedOverPit { get; private set; } // 確認用
    bool DiveLandsSafely(PlayerController pc)
    {
        var tm = TerrainManager.Instance;
        if (tm == null) return true;
        Vector3 p = pc.transform.position;
        float ahead = Mathf.Max(0.5f, pc.AssistHorizontalSpeed) * 0.45f + 0.6f;
        for (int i = 0; i <= 6; i++)
        {
            float x = p.x + ahead * i / 6f;
            float? sky = tm.GetSkyHeightFast(x);
            if (sky.HasValue && sky.Value <= p.y + 0.1f) continue; // 上ルートの足場
            if (tm.TryGetGroundFast(x, out float gh, out bool pit) && !pit && gh <= p.y + 0.1f) continue;
            BossDiveSkippedOverPit++;
            return false;
        }
        return true;
    }

    // 戻り値: 入力 / stayLow: このフレームは跳ばない(天井から・高い攻撃が来る)
    PlayerController.FlickDirection? DecideBoss(PlayerController pc, float frameDt, out bool stayLow)
    {
        // 魔法使いはジャンプが無く上/下が「高度の上げ下げ」なので、専用の判断を使う(2026-10-12、HighSpeedAssist.Mage.cs)
        if (pc.AssistIsMageFlight && BalanceTuning.AssistMageAltitude) return DecideBossMage(pc, out stayLow);
        stayLow = false;
        CollectThreats(pc);
        bool grounded = pc.AssistJumpsUsed == 0 && Mathf.Abs(pc.AssistVelocityY) < 0.01f;
        // ---- 天井から/高い攻撃: 地面にいる ----
        float ceilSoon = float.MaxValue;
        foreach (var t in threats) if (t.ceiling && t.tc < ceilSoon) ceilSoon = t.tc;
        if (ceilSoon < 0.9f)
        {
            stayLow = true;
            if (!grounded && pc.canUseDownAttack && pc.AssistVelocityY < 4f && !pc.AssistIsDiveOrHover && Time.time >= manualJumpUntil && DiveLandsSafely(pc))
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
                    // 2026-10-13 第3弾: いちばん近いフレームで跳ぶ(以前は 1.5 フレーム前 = fps が低いほど早く跳んでいた)
                    if (takeoffIn <= frameDt * 0.5f && t.tc + Mathf.Min(t.td, tall ? 9f : t2 - t1) > 0f && BossJumpSafe(pc, "ジャンプ(回避)"))
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
                if (falling && (tall || lowNow) && t.tc < 0.35f && BossJumpSafe(pc, "二段ジャンプ(回避)"))
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
        if (!PickBossTarget(pc, out string bestName, out Bounds bb, out float bestD)) { BossPlan = "攻撃できるボスがいない(無敵/地中/天井)"; return null; }
        Vector3 p = pc.transform.position;
        float reach = pc.AssistForwardIsProjectile ? 12f : Mathf.Max(pc.AssistForwardReach, bossApproachReach);
        if (bestD > reach)
        {
            // 2026-10-16(にらみ合いの修正): 届く距離のすぐ外でボスが待っている時は、踏み込みの大きい前攻撃で寄る(手動の連打と同じ)。
            // 前攻撃の踏み込みはボス戦の間、遠いほど大きく伸びる(BossBattle.LungeScale)。飛び道具のキャラは対象外(今までどおり)
            if (BalanceTuning.AssistApproachLunge && !pc.AssistForwardIsProjectile && bestD <= BalanceTuning.AssistApproachMax && !stayLow && !pc.IsAttacking)
            {
                if (outOfReachSince < 0f) outOfReachSince = Time.time;
                if (Time.time - outOfReachSince >= BalanceTuning.AssistApproachWait)
                {
                    outOfReachSince = Time.time; BossApproachLunges++;
                    BossPlan = $"{bestName} まで {bestD:F1}m → 踏み込みで寄る";
                    Act("前攻撃(寄る)", BossPlan);
                    return PlayerController.FlickDirection.Forward;
                }
            }
            BossPlan = $"{bestName} まで {bestD:F1}m(届くのを待つ)"; return null;
        }
        outOfReachSince = -1f;
        float feet = p.y, top = p.y + bodyH;
        // 空中で真上付近: 下攻撃(地面への衝撃で崩しが大きい)
        if (!grounded && pc.canUseDownAttack && bestD < 1.2f && bb.max.y < feet + 0.3f && !pc.AssistIsDiveOrHover && DiveLandsSafely(pc))
        {
            BossDownAttacks++; BossAttacks++;
            BossPlan = $"{bestName} の真上 → 下攻撃";
            Act("下攻撃(ボス)", BossPlan);
            return PlayerController.FlickDirection.Down;
        }
        // 頭上のボス: 跳んでから攻撃(天井から何か来る時は跳ばない)
        if (grounded && !stayLow && bb.min.y > top + 0.4f && bestD < 3f && Time.time >= manualJumpUntil && BossJumpSafe(pc, "ジャンプ(ボスへ)"))
        {
            BossPlan = $"{bestName} は高い所 → 跳んで攻撃";
            Act("ジャンプ(ボスへ)", BossPlan);
            return PlayerController.FlickDirection.Up;
        }
        // 2026-10-13 第3弾(全キャラのラスダン通しで発見): 飛び道具が浮いているボスの下を素通りしていた。弓の矢は足元から 0.62m を水平に飛び、
        // 魔人は地面から 0.8m 以上浮くので、地上から撃ち続けても一度も当たらなかった(闘技場で 150秒・300回撃って 0)。
        // 弾の高さ(最後に撃った弾で覚える)がボスの判定の下端より下なら、着地が安全な時に跳び、届く高さで撃つ
        if (pc.AssistForwardIsProjectile && !pc.AssistIsMageFlight && pc.ShotYOffset > 0f)
        {
            float shotY = feet + pc.ShotYOffset; // CPU サポート: 撃った本人の弾の高さ
            if (shotY < bb.min.y + 0.1f)
            {
                if (grounded && !stayLow && Time.time >= manualJumpUntil && BossJumpSafe(pc, "ジャンプ(撃ち上げ)"))
                {
                    BossShotJumps++;
                    BossPlan = $"{bestName} は弾より高い(弾 {shotY - feet:F1}m / 下端 {bb.min.y - feet:F1}m)→ 跳んで撃つ";
                    Act("ジャンプ(撃ち上げ)", BossPlan);
                    return PlayerController.FlickDirection.Up;
                }
                BossPlan = $"{bestName} は弾より高い → 届く高さまで待つ";
                return null;
            }
        }
        if (!pc.AssistCanStartForwardAttack) return null;
        if (bb.max.y < feet - 0.5f || bb.min.y > top + 1.2f) return null; // 高さが合わない
        BossAttacks++;
        BossPlan = $"{bestName} へ前攻撃(距離{bestD:F1}m)";
        Act("前攻撃(ボス)", BossPlan);
        return PlayerController.FlickDirection.Forward;
    }

    // 狙うボス(前にいて判定のある、いちばん近いもの)。いなければ false
    bool PickBossTarget(PlayerController pc, out string bestName, out Bounds bb, out float bestD)
    {
        bestName = null; bb = default; bestD = float.MaxValue;
        Vector3 p = pc.transform.position;
        foreach (var lb in BossBattle.Living)
        {
            Bounds b; string name;
            if (lb is WildBossBase w) { if (!w.AssistTargetable) continue; b = w.AssistBounds; name = w.bossName; }
            // 2026-10-11(攻略の再検証): ドラゴン/魔人(専用の作りのボス)も狙う。以前は WildBossBase だけを狙っていたため、
            // 天空回廊/ラスダンのドラゴン・魔人の戦いでは自動の攻撃がほとんど出ず、480秒かけても倒しきれなかった
            else if (BalanceTuning.AssistFlyingBosses && lb != null && lb.isActiveAndEnabled && lb is IBossBattleDebug dbg && dbg.DebugAlive)
            {
                var c = lb.GetComponent<Collider2D>();
                if (c == null || !c.enabled) continue;
                b = c.bounds; name = dbg.DebugName;
            }
            else continue;
            float d = b.min.x > p.x ? b.min.x - p.x : (b.max.x < p.x ? p.x - b.max.x : 0f);
            if (b.center.x < p.x - 1.5f) continue; // 後ろのボスは狙わない(前攻撃が届かない)
            if (d < bestD) { bestD = d; bestName = name; bb = b; }
        }
        return bestName != null;
    }
}
