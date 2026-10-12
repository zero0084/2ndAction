using UnityEngine;

// 魔法使いのボス戦(2026-10-12)。魔法使いはジャンプが無く、上/下フリックは「高度を1段上げる/下げる+斜めの魔法」、
// 前の魔法弾は今の高さから水平に飛ぶ。以前はボス戦でも他のキャラと同じ判断(跳ぶ/二段ジャンプ/跳んで攻撃)を使っていたため、
//  ・浮遊の上下の揺れで「地面にいない」と見なされ、低い攻撃のたびに「二段ジャンプ」(= 1段上昇)を出して昇っていく
//    (ジャンプ回数を使わないので何度でも出る)
//  ・高いボス(ドラゴン等)へ「跳んで攻撃」で上げた高度を、戦いの後も下げる判断がどこにも無い
//  → 低いボス(オオカミ)の頭上 2〜4m に浮いたまま「高さが合わない」で前攻撃を出さず、ボスの噛みつきも届かない。
//    関門で距離が止まって足元の地形も変わらないため、何も起きないまま 480秒の時間切れになっていた(BuildLab 48km/72km)。
// ここでは「今の段」と「ボスに魔法弾が当たる段」を比べて段を合わせてから前攻撃する。避ける時だけ必要な段まで上げ、
// 避け終わったらボスの高さへ戻る。BalanceTuning.AssistMageAltitude で切り替え(比較用)。
public partial class HighSpeedAssist
{
    public int MageBossLevelUps, MageBossLevelDowns, MageBossDodges, MageBossBolts;
    float mageNextLevelChange = -99f; // 段の合わせ直しの間隔(ボスの絵の揺れで上下を繰り返さない)
    float mageHoldUntil = -99f;       // 避けるために上げた段を、攻撃が通り過ぎるまで保つ
    const float MageLevelChangeInterval = 0.3f;

    PlayerController.FlickDirection? DecideBossMage(PlayerController pc, out bool stayLow)
    {
        stayLow = false;
        CollectThreats(pc);
        int lv = pc.MageAltitudeLevel, maxLv = pc.AssistMageMaxLevel;
        float y = pc.transform.position.y;

        // ---- 天井から/高い攻撃: 段を下げる ----
        float ceilSoon = float.MaxValue;
        foreach (var t in threats) if (t.ceiling && t.tc < ceilSoon) ceilSoon = t.tc;
        if (ceilSoon < 0.9f)
        {
            stayLow = true;
            if (lv > 0 && Time.time >= manualAttackUntil)
            {
                MageBossLevelDowns++; BossStayLow++;
                mageHoldUntil = -99f; mageNextLevelChange = Time.time + MageLevelChangeInterval;
                BossPlan = "天井/高い攻撃 → 段を下げる";
                Act("下(段を下げる)", BossPlan);
                return PlayerController.FlickDirection.Down;
            }
        }

        // ---- 低い攻撃: 有効な時間に足元が上にある段まで上げる(上げた段は通り過ぎるまで保つ) ----
        int safeLv = 0; float holdTo = -99f; string what = null;
        foreach (var t in threats)
        {
            if (t.ceiling || t.needY <= 0.05f || t.tc > 0.5f) continue;
            float topAbs = y + t.needY;
            int k = 0;
            while (k < maxLv && pc.AssistMageLevelY(k) < topAbs) k++;
            if (pc.AssistMageLevelY(k) < topAbs) continue; // 最高段でも越えられない
            if (k > safeLv) { safeLv = k; what = t.what; }
            holdTo = Mathf.Max(holdTo, Time.time + t.tc + Mathf.Min(t.td, 1.5f) + 0.1f);
        }
        if (safeLv > lv && !stayLow && Time.time >= manualJumpUntil)
        {
            MageBossDodges++; MageBossLevelUps++; BossDodgeJumps++;
            mageHoldUntil = Mathf.Max(mageHoldUntil, holdTo);
            BossPlan = $"{what} を段を上げて避ける({lv}→{lv + 1})";
            Act("上(段を上げて回避)", BossPlan);
            return PlayerController.FlickDirection.Up;
        }
        int minLv = Time.time < mageHoldUntil ? lv : safeLv; // 避けている間は今の段より下げない

        // ---- 接近と攻撃: ボスに魔法弾が当たる段へ合わせる ----
        bool levelReady = Time.time >= mageNextLevelChange;
        if (!PickBossTarget(pc, out string name, out Bounds bb, out float d))
        {
            // 狙えるボスがいない(無敵/地中/後ろ): 高い所に残らず最低段へ戻っておく
            BossPlan = "攻撃できるボスがいない(無敵/地中/天井)";
            if (lv > minLv && levelReady && Time.time >= manualAttackUntil) return MageLevelDown(lv, "ボスを狙えない → 段を戻す");
            return null;
        }
        float muzzle = pc.AssistForwardMuzzleY;
        float lo = bb.min.y + 0.25f, hi = bb.max.y - 0.25f;
        if (hi < lo) lo = hi = bb.center.y;
        int want = 0; float bestErr = float.MaxValue;
        for (int k = 0; k <= maxLv; k++)
        {
            float m = pc.AssistMageLevelY(k) + muzzle;
            float err = m < lo ? lo - m : (m > hi ? m - hi : 0f);
            if (err < bestErr - 0.01f) { bestErr = err; want = k; } // 同じなら低い段
        }
        want = Mathf.Max(want, minLv);
        if (stayLow) want = Mathf.Min(want, lv);
        if (want < lv && levelReady && Time.time >= manualAttackUntil)
            return MageLevelDown(lv, $"{name} は下 → 段を下げる({lv}→{lv - 1}、下の魔法)");
        if (want > lv && levelReady && Time.time >= manualJumpUntil)
        {
            MageBossLevelUps++; BossAttacks++;
            mageNextLevelChange = Time.time + MageLevelChangeInterval;
            BossPlan = $"{name} は上 → 段を上げる({lv}→{lv + 1}、上の雷撃)";
            Act("上(段を上げる)", BossPlan);
            return PlayerController.FlickDirection.Up;
        }
        if (Time.time < manualAttackUntil) return null;
        if (d > 12f) { BossPlan = $"{name} まで {d:F1}m(届くのを待つ)"; return null; }
        if (!pc.AssistCanStartForwardAttack) return null;
        float muzzleNow = pc.AssistMageLevelY(lv) + muzzle;
        if (bestErr > 0.6f && lv == maxLv && muzzleNow < lo && pc.canUseUpAttack)
        {
            // 最高段でもボスが上: 斜め上の雷撃(段は上がらない)
            BossAttacks++;
            BossPlan = $"{name} は最高段より上 → 上の雷撃";
            Act("上(雷撃)", BossPlan);
            return PlayerController.FlickDirection.Up;
        }
        MageBossBolts++; BossAttacks++;
        BossPlan = $"{name} へ魔法弾(距離{d:F1}m 段{lv})";
        Act("前攻撃(ボス)", BossPlan);
        return PlayerController.FlickDirection.Forward;
    }

    PlayerController.FlickDirection MageLevelDown(int lv, string why)
    {
        MageBossLevelDowns++; BossAttacks++;
        mageNextLevelChange = Time.time + MageLevelChangeInterval;
        BossPlan = why;
        Act("下(段を下げる)", BossPlan);
        return PlayerController.FlickDirection.Down;
    }
}
