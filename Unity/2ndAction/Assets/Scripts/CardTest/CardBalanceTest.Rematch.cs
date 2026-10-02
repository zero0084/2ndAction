#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

// CARD BALANCE TEST の「ボス再戦」タブ(2026-10-02): 撃破済みプール/直近/抽選の結果の表示と、確認用の操作。
public partial class CardBalanceTest
{
    const int RematchTabRows = 9;

    void DrawRematch(float x, ref float y, float rowH, float gap)
    {
        var bm = BossManager.Instance; var gm = GameManager.Instance;
        if (bm == null || gm == null) return;
        var tn = BossRematchTuning.I;
        GUI.Label(new Rect(x, y, W - 16f, rowH), $"撃破済みプール({bm.DefeatedPool.Count}): {string.Join(", ", bm.DefeatedPool)}", sSmall);
        y += rowH + gap;
        GUI.Label(new Rect(x, y, W - 16f, rowH), $"直近(新しい順): {string.Join(", ", bm.RecentFought)}   除外 {tn.recentExclude}体   再戦 {bm.RematchCount}回", sSmall);
        y += rowH + gap;
        GUI.Label(new Rect(x, y, W - 16f, rowH), "前回の決定: " + bm.LastRematchDecision, sSmall);
        y += rowH + gap;
        float d = gm.MaxDistance;
        var tier = tn.TierAt(d);
        GUI.Label(new Rect(x, y, W - 16f, rowH), $"今 {d:N0}m  次の関門 {bm.NextBossDistance:N0}m  再戦の段階: {tier.label}(HP×{tier.hpMul:0.0}+距離 / 被弾×{tier.damageMul:0.0} / 崩し×{tier.staggerMul:0.0} / 間隔×{tier.cooldownMul:0.00}{(tier.extraPhase ? " / 段階+1" : "")})" +
            (bm.IsBossPhase ? $"  戦闘中: {bm.CurrentEncounterKey}{(bm.CurrentEncounterIsRematch ? "(再戦)" : "")}" : ""), sSmall);
        y += rowH + gap;
        float bx = x;
        if (B(new Rect(bx, y, 150f, rowH), "次の関門を今すぐ")) lastAction = bm.DebugStartNextGateNow() ? "次の関門の手前へ" : "ボス戦中は出せません"; bx += 154f;
        if (B(new Rect(bx, y, 120f, rowH), "全部Unlock")) { bm.DebugUnlockAll(); lastAction = "このステージのボスを全部プールへ"; } bx += 124f;
        if (B(new Rect(bx, y, 110f, rowH), "Pool Reset")) { bm.DebugPoolReset(); lastAction = "プールを空に"; } bx += 114f;
        foreach (int n in new[] { 1, 2 })
        {
            if (B(new Rect(bx, y, 100f, rowH), $"直近{n}体除外", tn.recentExclude == n)) { bm.DebugSetRecentExclude(n); lastAction = $"直近{n}体を除外"; }
            bx += 104f;
        }
        y += rowH + gap;
        bx = x;
        GUI.Label(new Rect(bx, y, 70f, rowH), "距離", sLabel); bx += 70f;
        foreach (float add in new[] { 1000f, 5000f })
        {
            if (B(new Rect(bx, y, 80f, rowH), $"+{add / 1000f:0}km") && !bm.IsBossPhase) gm.DebugWarpToDistance(d + add);
            bx += 84f;
        }
        foreach (float to in new[] { 10000f, 30000f, 50000f, 80000f })
        {
            if (B(new Rect(bx, y, 90f, rowH), $"{to / 1000f:0}km") && !bm.IsBossPhase) gm.DebugWarpToDistance(to - 300f);
            bx += 94f;
        }
        y += rowH + gap;
        GUI.Label(new Rect(x, y, W - 16f, rowH), "1,000m/5,000mの関門: 本来のボスを倒すまでは本来のボス、倒した後は撃破済みから重み付き抽選(直近を除外)。10kmごとの専用ボスは固定", sSmall);
        y += rowH + gap;
    }
}
#endif
