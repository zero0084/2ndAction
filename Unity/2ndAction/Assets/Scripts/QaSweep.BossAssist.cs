#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// 自動操作補助のボス戦対応(2026-10-04)の簡易確認。 -qaBossAssist <dir>
// 同じボス・同じ必殺技を「補助OFF」「補助ON」で1回ずつ見て、補助ONで攻撃/回避が出ること・被弾が減ることを確かめる。
public partial class QaSweep
{
    IEnumerator BossAssistMode()
    {
        Application.targetFrameRate = 60;
        HookBossLogs();
        BossManager.NetTestBossHpOverride = 50000000;
        yield return BeginRun("swordsman", "natural_cave");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        WarpTo(1500f);
        yield return new WaitForSeconds(1f);
        var a = HighSpeedAssist.Instance;
        bool keep = a.assistEnabled;
        foreach (var kind in new[] { CaveBossKind.Troll, CaveBossKind.Mole, CaveBossKind.Scorpion })
        {
            int hitsOff = 0, hitsOn = 0, dmgOff = 0, dmgOn = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                bool on = pass == 1;
                a.assistEnabled = on;
                SetKmh(60f); // 補助の通常の発動速度(100km/h)より遅い: ボス戦だけで働くことも確かめる
                Bm.DebugCaveEncounter(kind, -1);
                yield return WaitBossSpawn(10f);
                var b = WildAlive().FirstOrDefault();
                if (b == null) { Check(false, $"{kind}: spawned"); continue; }
                int hp0 = b.Hp, ph0 = CaveHazard.PlayerHits;
                int att0 = a.BossAttacks, jmp0 = a.BossDodgeJumps + a.BossDoubleJumps, low0 = a.BossStayLow;
                b.DebugSetPhase(2);
                yield return new WaitForSeconds(2.5f);
                b.DebugForceUltimate();
                float w = 0f;
                while (w < 16f && !b.IsDead) { w += Time.deltaTime; yield return null; }
                int hits = CaveHazard.PlayerHits - ph0, dmg = hp0 - b.Hp;
                if (on) { hitsOn = hits; dmgOn = dmg; } else { hitsOff = hits; dmgOff = dmg; }
                L($"[assist {(on ? "ON " : "OFF")}] {kind}: terrain hits={hits} damage dealt={dmg} bossAttacks={a.BossAttacks - att0} dodgeJumps={a.BossDodgeJumps + a.BossDoubleJumps - jmp0} stayLow={a.BossStayLow - low0} plan='{a.BossPlan}'");
                if (on) Shot($"assist_{kind}_on");
                PlayerController.DebugSpeedScale = 1f;
                foreach (var x in WildAlive()) x.TakeDamage(Mathf.Max(99999, x.Hp), x.CenterWorld);
                yield return WaitPhaseEnd(30f);
                yield return new WaitForSeconds(1f);
                if (Bm.NextBossDistance - gm.MaxDistance < 400f) WarpTo(Mathf.Floor(gm.MaxDistance / 1000f) * 1000f + 1500f);
                yield return new WaitForSeconds(0.5f);
            }
            Check(dmgOn > dmgOff, $"{kind}: the assist attacks the boss (damage {dmgOff} -> {dmgOn})");
            Check(hitsOn <= hitsOff, $"{kind}: the assist does not take more terrain hits (hits {hitsOff} -> {hitsOn})");
        }
        a.assistEnabled = keep;
        BossManager.NetTestBossHpOverride = 0;
        yield return EndRun();
    }
}
#endif
