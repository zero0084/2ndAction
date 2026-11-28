#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

// 条件を作る補助試験(2026-11-28、マスター依頼 ③④)。通常の通し試験とは別に記録する(ワープ/HP の指定/強制の致命傷を使う)。
//  -netAutoCondWarpAt t:距離     … t 秒目に指定の距離へ飛ぶ(GameManager.DebugWarpToDistance。全員同じ値で並べる)
//  -netAutoCondHpAt t:hp[,t:hp]  … t 秒目に自分の HP を決める(JOIN は HOST へ要求 = HOST の表の値)
//  -netAutoCondKillAt t          … t 秒目に、死神ではない致命傷(HP をハート1つにしてから被弾 "AutoTestKill")
//  -netAutoCondCards t:id:lv[,…] … t 秒目にカードを持たせる(PHOENIX など)
//  -netAutoCondFeAt t:id         … t 秒目に最終進化を発動して長く続ける(PHOENIX の緊急復活の確認)
//  記録: COND 行(状態の変化・HP・PHOENIX の残り・最終進化・死神の段階)を 1 秒ごと + 変化した時
public partial class NetAutoTest
{
    float condWarpAt = -1f, condWarpTo; bool condWarpDone;
    readonly System.Collections.Generic.List<Vector2> condHp = new System.Collections.Generic.List<Vector2>();
    float condKillAt = -1f; bool condKillStarted, condKillDone; float condKillTimer;
    float condCardsAt = -1f; string condCards; bool condCardsDone;
    float condAllCardsAt = -1f; int condAllCardsLv = 9; bool condAllCardsDone;
    float condFeAt = -1f; string condFeId; bool condFeDone;
    bool condOn, condNoRevive; float condLogNext; string condLastState = "";
    float condSpeedAt = -1f, condSpeed = 1f; bool condSpeedDone;
    int condSisterHp; string condLdChoice = ""; string condLdLast = ""; float condLdAtkNext; int condLdAtks;
    int condHpKeep = -1; float condHpKeepFrom, condKeepNext; bool condPhoenixKeep, condFeKeep; int condPhoenixReapplied, condFeReactivated;

    bool ParseCondArg(string a, string next)
    {
        switch (a)
        {
            case "-netAutoCondWarpAt": { var p = next.Split(':'); if (p.Length == 2) { float.TryParse(p[0], out condWarpAt); float.TryParse(p[1], out condWarpTo); } condOn = true; return true; }
            case "-netAutoCondHpAt": foreach (var e in next.Split(',')) { var p = e.Split(':'); if (p.Length == 2 && float.TryParse(p[0], out float t) && float.TryParse(p[1], out float hp)) condHp.Add(new Vector2(t, hp)); } condOn = true; return true;
            case "-netAutoCondKillAt": float.TryParse(next, out condKillAt); condOn = true; return true;
            case "-netAutoCondAllCards": { var p = next.Split(':'); if (p.Length == 2) { float.TryParse(p[0], out condAllCardsAt); int.TryParse(p[1], out condAllCardsLv); } condOn = true; return true; } // 2026-11-28 依頼⑦(マスター): t 秒目に全カード(#100 ULTIMATE を除く)を Lv で持つ
            case "-netAutoCondCards": { int i = next.IndexOf(':'); if (i > 0) { float.TryParse(next.Substring(0, i), out condCardsAt); condCards = next.Substring(i + 1); } condOn = true; return true; }
            case "-netAutoCondNoRevive": condNoRevive = next != "0"; condOn = true; return true;
            case "-netAutoCondHpKeep": { var p = next.Split(':'); if (p.Length == 2) { float.TryParse(p[0], out condHpKeepFrom); int.TryParse(p[1], out condHpKeep); } condOn = true; return true; } // t:hp … t 秒目から、生きている間は HP をこの値に保つ(上回ったら戻す)
            case "-netAutoCondSpeedAt": { var p = next.Split(':'); if (p.Length == 2) { float.TryParse(p[0], out condSpeedAt); float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out condSpeed); } condOn = true; return true; } // t:倍率 … 遅く走って死神に狙われる
            case "-netAutoCondSisterHp": int.TryParse(next, out condSisterHp); condOn = true; return true; // HOST: 三姉妹の HP(短くする)
            case "-netAutoCondLdChoice": condLdChoice = next.ToLowerInvariant(); condOn = true; return true; // HOST: ONE MORE MILE? の答え(yes/no)
            case "-netAutoCondPhoenixKeep": condPhoenixKeep = next != "0"; condOn = true; return true; // PHOENIX が無くなったら持ち直す(死神の時に必ず持っている)
            case "-netAutoCondFeKeep": condFeKeep = next != "0"; condOn = true; return true;       // 最終進化 PHOENIX の緊急復活が使われたら発動し直す
            case "-netAutoCondFeAt": { var p = next.Split(':'); if (p.Length == 2) { float.TryParse(p[0], out condFeAt); condFeId = p[1]; } condOn = true; return true; }
        }
        return false;
    }

    void CondTick(GameManager gm)
    {
        if (!condOn || gm == null || !gm.HasStarted) return;
        var pc = PlayerController.Instance;
        if (pc == null) return;
        float t = runTime;
        var me = NetMatch.Get(NetCombat.LocalPlayerNumber);
        bool alive = me == null || me.State == NetMatch.PState.Alive;
        if (condWarpAt >= 0f && !condWarpDone && t >= condWarpAt && !gm.IsGameOver) { condWarpDone = true; gm.DebugWarpToDistance(condWarpTo); L($"COND warp to {condWarpTo:F0} at t={t:F1}"); }
        if (condSpeedAt >= 0f && !condSpeedDone && t >= condSpeedAt) { condSpeedDone = true; PlayerController.DebugSpeedScale = condSpeed; L($"COND speed scale {condSpeed:F2} at t={t:F1}"); }
        if (condCardsAt >= 0f && !condCardsDone && t >= condCardsAt && !gm.IsGameOver)
        {
            condCardsDone = true;
            foreach (var e in condCards.Split(','))
            {
                var kv = e.Split(':');
                var c = CardDatabase.FindBaseById(kv[0]);
                if (c != null) gm.ApplyCardEffectsStacked(c, kv.Length > 1 && int.TryParse(kv[1], out int lv) ? lv : 1);
                L($"COND card {kv[0]} -> {(c != null ? "applied" : "NOT FOUND")} phoenixCharges={gm.PhoenixCharges}");
            }
        }
        if (condAllCardsAt >= 0f && !condAllCardsDone && t >= condAllCardsAt && !gm.IsGameOver)
        {
            condAllCardsDone = true;
            int n = 0, fail = 0;
            foreach (var c in CardDatabase.AllCards)
            {
                if (c == null || CardVariant.IsVariantKey(c.cardId) || c.cardId == "character_ultimate") continue; // #100 ULTIMATE はマルチ未対応
                try { gm.ApplyCardEffectsStacked(c, condAllCardsLv); n++; } catch (System.Exception ex) { fail++; L($"COND all cards: {c.cardId} failed {ex.GetType().Name}"); }
            }
            L($"COND all cards Lv{condAllCardsLv}: applied {n} (failed {fail}) bossHpMul={gm.BossHpMultiplier:F2} lives={gm.Lives}/{gm.MaxLives} atk={(PlayerController.Instance != null ? PlayerController.Instance.EffectiveBossAttackPower : 0)}");
        }
        if (condFeAt >= 0f && !condFeDone && t >= condFeAt && !gm.IsGameOver && alive)
        {
            condFeDone = true;
            FinalEvolution.DebugMakeReady(condFeId);
            bool ok = FinalEvolution.Activate(condFeId);
            if (ok) FinalEvolution.DebugSetRemaining(condFeId, 9999f);
            L($"COND final evolution {condFeId} activated={ok} phoenixToken={FinalEvolution.PhoenixTokenReady}");
        }
        if (Time.unscaledTime >= condKeepNext && alive && !gm.IsGameOver)
        {
            condKeepNext = Time.unscaledTime + 1f;
            int hpNow = NetCombat.Replica ? (me != null ? me.Hp : gm.Lives) : gm.Lives;
            if (condHpKeep > 0 && t >= condHpKeepFrom && hpNow > condHpKeep) SetOwnHp(gm, condHpKeep);
            if (condPhoenixKeep && condCardsDone && gm.PhoenixCharges <= 0) { var c = CardDatabase.FindBaseById("phoenix"); if (c != null) { gm.ApplyCardEffectsStacked(c, 1); condPhoenixReapplied++; L($"COND phoenix re-applied #{condPhoenixReapplied} charges={gm.PhoenixCharges}"); } }
            if (condFeKeep && condFeDone && !FinalEvolution.PhoenixTokenReady)
            {
                FinalEvolution.DebugEnd(condFeId); FinalEvolution.DebugMakeReady(condFeId);
                bool ok = FinalEvolution.Activate(condFeId); if (ok) FinalEvolution.DebugSetRemaining(condFeId, 9999f);
                condFeReactivated++; L($"COND final evolution {condFeId} re-activated #{condFeReactivated} ok={ok} token={FinalEvolution.PhoenixTokenReady}");
            }
        }
        for (int i = condHp.Count - 1; i >= 0; i--)
            if (t >= condHp[i].x && alive && !gm.IsGameOver) { SetOwnHp(gm, (int)condHp[i].y); L($"COND hp set to {(int)condHp[i].y} at t={t:F1}"); condHp.RemoveAt(i); }
        if (condKillAt >= 0f && !condKillDone && t >= condKillAt && !gm.IsGameOver && (condKillStarted || alive))
        {
            if (!condKillStarted) { condKillStarted = true; SetOwnHp(gm, CombatScale.PlayerHit); condKillTimer = 0f; L($"COND KILL (not the reaper) start t={t:F1} dist={pc.DistanceExact:F1}"); }
            condKillTimer += Time.unscaledDeltaTime;
            int hpNow = NetCombat.Replica ? (me != null ? me.Hp : 990) : gm.Lives;
            if (hpNow <= CombatScale.PlayerHit && condKillTimer > 0.3f && alive) pc.TakeDamage(isFall: false, source: "AutoTestKill");
            if (!alive || gm.IsGameOver || condKillTimer > 8f) { condKillDone = true; L($"COND KILL done state={(me != null ? me.State.ToString() : "-")} hp={gm.Lives} phoenixRevives={GameManager.PhoenixRevives}"); }
        }
        if (condSisterHp > 0) ReaperFinaleBattle.DebugHpOverride = condSisterHp;
        var ld = LastDungeonFlow.Instance;
        if (ld != null)
        {
            string s = $"{ld.Current}/{(ld.Finale != null ? ld.Finale.Current.ToString() : "-")}/{(ld.Choice != null ? (ld.Choice.Decided ? (ld.Choice.ChoseYes ? "YES" : "NO") : (ld.Choice.Risen ? "risen" : "rising")) : "-")}";
            if (s != condLdLast) { condLdLast = s; L($"COND LD t={t:F1} flow={s} d={gm.MaxDistance:F0} front={WorldProgress.FrontHigh:F0} mile={gm.RunMile} beyond={gm.LdBeyond} clear={NetMatch.LdLocalOutcome} rank={NetMatch.LdClearRank}/{NetMatch.LdClearPlayers}"); }
            if (NetCombat.Authority && condLdChoice != "" && ld.Choice != null && ld.Choice.Risen && !ld.Choice.Decided) { L($"COND LD HOST chooses {condLdChoice}"); ld.Choice.ForceDecide(condLdChoice == "yes"); }
            // エンドロールの壁(THANK YOU FOR PLAYING / END)と YES/NO の石は人と同じく攻撃して壊す(仮想のパッドの前攻撃。bot は文字を敵と見ないため)
            if ((ld.Current == LastDungeonFlow.State.Credits || ld.Current == LastDungeonFlow.State.Choice) && Time.unscaledTime >= condLdAtkNext && !gm.IsGameOver)
            {
                condLdAtkNext = Time.unscaledTime + 0.35f;
                NatPress(GameAction.AttackForward);
                condLdAtks++;
                if (condLdAtks % 40 == 1) L($"COND LD attack press #{condLdAtks} (credits wall / choice stones) x={FloatingOrigin.ToLogical(pc.transform.position.x):F0} lockedHitsIgnored={ChoiceWord.LockedHitsIgnored} hits={ChoiceWord.TotalHits}");
            }
        }
        string st = $"{(me != null ? me.State.ToString() : "-")}/{(pc.NetIsDowned ? "downed" : "up")}/{(gm.IsGameOver ? (gm.IsWin ? "WIN" : "OVER") : "run")}";
        if (st != condLastState || Time.unscaledTime >= condLogNext)
        {
            condLogNext = Time.unscaledTime + 1f;
            var rp = ReaperBase.Active;
            L($"COND t={t:F1} state={st}{(st != condLastState ? " (changed)" : "")} hp={gm.Lives}/{gm.MaxLives} tableHp={(me != null ? me.Hp : -1)} phoenix={gm.PhoenixCharges}/{GameManager.PhoenixRevives} fe={(FinalEvolution.IsActive("phoenix") ? "phoenix" : "-")}/{FinalEvolution.EmergencyRevives} reaper={(rp != null ? rp.CurrentPhase.ToString() : "none")} reaperElim={NetMatch.ReaperEliminations} dist={pc.DistanceExact:F0} rank='{NetMatch.ComputeRank().Text}' clear={(NetMatch.Instance != null && NetMatch.Instance.HostClearDeclared)}/{NetMatch.HostClearLocalOutcome}");
            condLastState = st;
        }
    }
}
#endif
