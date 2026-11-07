#if UNITY_EDITOR || DEVELOPMENT_BUILD
// 開発版(Editor / Development Build)だけ(NetAutoTest 本体と同じ)
using UnityEngine;

// 2026-11-07 双剣士の崩し ×1.5 のマルチの確認: -netAutoStaggerTest <回数> [-netAutoChar dual_blade] -netAutoBossAt 20 -netAutoBossHp 400000
//  HOST / JOIN とも、ボスが出て 4 秒後から同じ並び(地上の前攻撃 / 空中の前攻撃 / 地上の上攻撃 / 空中の下攻撃)で
//  決まった回数だけボスへ当てる(ダメージ 1、本物の当たり判定の代わりに当たった時と同じ経路)。HOST は自分の攻撃として、JOIN は HOST への申告として。
//  HOST の WildBossBase.QaStaggerBy に攻撃した人ごとの崩しの合計が入る → STG_SUMMARY(同じキャラ・同じ回数なら一致するはず)。
//  JOIN は自分で崩しを足さない(パペット)ので、二重なら HOST の合計が増える。最後に両方の端末でボスの崩しの割合(StaggerFraction)も出す
public partial class NetAutoTest
{
    int stgCount, stgLagMs; string stgChar = ""; bool stgCharSet; int stgDone; float stgNext = -1f, stgSummaryAt = -1f; bool stgSummarized;
    static readonly PlayerAttackKind[] StgKinds = { PlayerAttackKind.Normal, PlayerAttackKind.Normal, PlayerAttackKind.Up, PlayerAttackKind.Down };
    static readonly bool[] StgAir = { false, true, false, true };

    bool ParseStaggerArg(string a, string next)
    {
        if (a == "-netAutoStaggerTest") { int.TryParse(next, out stgCount); return true; }
        if (a == "-netAutoChar") { stgChar = next; return true; }
        if (a == "-netAutoStgLag") { int.TryParse(next, out stgLagMs); return true; } // JOIN の申告を ms 遅らせる(+0〜同じ量のゆらぎ。遅れて届いても合計が同じか)
        return false;
    }

    // Run が始まる前に、この端末のキャラを決める(未解放でも試験なので選ぶ。保存はしない = -memSave)
    void StaggerPreTick(GameManager gm)
    {
        // GameManager の起動処理が保存の値で選び直すので、Run が始まるまで毎フレーム合わせる
        if (stgCharSet || string.IsNullOrEmpty(stgChar) || gm == null) return;
        if (gm.HasStarted) { stgCharSet = true; L($"STG char role={role} selected={gm.SelectedCharacterId} run={gm.ActiveRunCharacterId}"); return; }
        if (gm.SelectedCharacterId != stgChar) SetPrivateProperty(gm, "SelectedCharacterId", stgChar);
    }

    static WildBossBase StgBoss()
    {
        foreach (var b in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (b != null && b.isActiveAndEnabled) return b;
        return null;
    }

    void StaggerTick(GameManager gm)
    {
        if (stgCount <= 0 || bossAt <= 0f) return;
        var boss = StgBoss();
        if (stgNext < 0f)
        {
            if (boss == null || runTime < bossAt + 4f) return;
            stgNext = runTime + (role == "JOIN" ? 0.3f : 0f);
            var pc = PlayerController.Instance;
            L($"STG start role={role} me=P{NetCombat.LocalPlayerNumber} char={(pc != null ? pc.CharId : "?")} boss={boss.name} puppet={boss.NetPuppet} netId={boss.NetId}");
        }
        if (stgDone < stgCount && runTime >= stgNext && boss != null)
        {
            int i = stgDone % StgKinds.Length;
            if (role == "HOST") boss.QaScriptedLocalHit(StgKinds[i], StgAir[i]);
            else if (boss.NetPuppet)
            {
                if (stgLagMs > 0) StartCoroutine(StgLate(boss, StgKinds[i], StgAir[i], (stgLagMs + Random.Range(0f, stgLagMs)) / 1000f));
                else NetCombat.RequestHit(boss.NetId, 1, StgKinds[i], boss.CenterWorld, true, StgAir[i]);
            }
            stgDone++;
            stgNext = runTime + 0.6f;
            if (stgDone == stgCount) stgSummaryAt = runTime + 3f + stgLagMs * 2f / 1000f;
        }
        if (stgSummaryAt > 0f && runTime >= stgSummaryAt && !stgSummarized)
        {
            stgSummarized = true;
            StaggerSummary(boss);
        }
    }

    System.Collections.IEnumerator StgLate(WildBossBase boss, PlayerAttackKind kind, bool air, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        if (boss != null) NetCombat.RequestHit(boss.NetId, 1, kind, boss.CenterWorld, true, air);
    }

    void StaggerSummary(WildBossBase boss)
    {
        var t = BossBattleTuning.I;
        float unit = 0f;
        for (int k = 0; k < stgCount; k++) { int i = k % StgKinds.Length; unit += t.StaggerFor(StgKinds[i], StgAir[i]); }
        var pc = PlayerController.Instance;
        string frac = boss == null ? "-" : role == "HOST" ? $"{((IBossBattleDebug)boss).StaggerFraction:F4} broken={boss.Broken}" : $"{boss.NetStaggerFraction:F4} broken={boss.NetBrokenShown}(HOST から届いた値)";
        if (role == "HOST")
        {
            string per = "";
            for (int p = 0; p < WildBossBase.QaStaggerBy.Length; p++) if (WildBossBase.QaStaggerBy[p] > 0f) per += $" by{(p == 0 ? "HOST" : "P" + p)}={WildBossBase.QaStaggerBy[p]:F3}";
            string chars = "";
            foreach (var np in NetPlayer.All) if (np != null) chars += $" P{np.PlayerNumber}={np.CharacterId.Value}";
            L($"STG_SUMMARY role=HOST hostChar={(pc != null ? pc.CharId : "?")} hits={stgDone} baseSum={unit:F3} x1.5={unit * 1.5f:F3}{per} remoteCharStagger={NetCombat.StatRemoteCharStagger} players=[{chars.Trim()}] fraction={frac}");
        }
        else
        {
            var nc = NetCombat.Instance;
            L($"STG_SUMMARY role=JOIN me=P{NetCombat.LocalPlayerNumber} char={(pc != null ? pc.CharId : "?")} sent={stgDone} hitRequestsSent={(nc != null ? nc.StatHitRequestsSent : -1)} localStaggerAdded={WildBossBase.QaStaggerBy[0]:F3} fraction={frac}");
        }
    }
}
#endif
