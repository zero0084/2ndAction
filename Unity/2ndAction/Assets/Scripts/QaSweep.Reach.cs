#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// ボス戦の停止戦(2026-10-29): -qaReach <dir> [-rcBosses Wild/Wolf,…|all] [-rcSecs 30] [-rcChar swordsman] [-rcPoke 1]
// 止まって戦う形で、近接のキャラ(既定: 剣士)の攻撃が届く機会があるかをボスごとに測る:
//  届く = 体(被弾の範囲)の手前の端が前後 ReachX m 以内 かつ 高さがジャンプ+上攻撃で届く範囲 かつ 被弾の判定が有効で無敵でない。
//  記録: 届く時間の割合 / 届かない状態が続いた最長の秒数 / 無敵が続いた最長 / 体がプレイヤーより後ろにいた時間 / 背を向けて歩いた回数
//  -rcPoke 1: 届く時は正面/後ろへ攻撃を入れる(ボスの段階が進み、近接に反応する技が出る。HPは大きくして倒さない)
public partial class QaSweep
{
    public const float ReachX = 3.0f, ReachUp = 3.6f;

    IEnumerator ReachMode()
    {
        Application.targetFrameRate = 60;
        string arg = Arg("-rcBosses", "all");
        var keys = new List<string>();
        if (arg == "all")
        {
            foreach (WildBossKind k in System.Enum.GetValues(typeof(WildBossKind))) keys.Add("Wild/" + k);
            foreach (CaveBossKind k in System.Enum.GetValues(typeof(CaveBossKind))) keys.Add("Cave/" + k);
            foreach (SkyBossKind k in System.Enum.GetValues(typeof(SkyBossKind))) keys.Add("Sky/" + k);
        }
        else keys.AddRange(arg.Split(','));
        float secs = float.Parse(Arg("-rcSecs", "30"), System.Globalization.CultureInfo.InvariantCulture);
        string ch = Arg("-rcChar", "swordsman");
        bool poke = Arg("-rcPoke", "1") == "1";
        var tsv = new StringBuilder("boss\tsecs\treachFrac\tmaxUnreachSec\tmaxInvulnSec\tbehindFrac\tfarFrac\thighFrac\thiddenFrac\thits\tturnAways\n");
        foreach (var grp in keys.GroupBy(b => b.Split('/')[0]))
        {
            string stage = grp.Key == "Cave" ? "natural_cave" : grp.Key == "Sky" ? "sky_corridor" : "wasteland_road";
            yield return BeginRun(ch, stage);
            SetGod(true);
            GameManager.BlockExpGain = true;
            if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
            TerrainManager.Instance.pitChanceBase = 0f; TerrainManager.Instance.pitChanceMax = 0f;
            TerrainManager.Instance.DebugSetRouteBranch(false);
            TerrainManager.Instance.enemySpawnChance = 0f;
            if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
            yield return new WaitForSeconds(1.0f);
            foreach (var key in grp)
            {
                if (!BaParse(key, out int fam, out int kind)) continue;
                yield return BfWaitEncounterEnd();
                BossManager.Instance.DebugSpawnBossForTest(fam, kind);
                BaBoss boss = null; float w = 0f;
                while (w < 15f) { yield return null; w += Time.unscaledDeltaTime; boss = BaFind(); if (boss.W != null && !boss.W.IsEntering && w > 1.5f) break; if ((boss.D != null || boss.M != null) && w > 4.5f) break; }
                if (boss == null || !boss.Valid) { L($"[reach] {key}: no boss"); continue; }
                boss.ScaleHp(80f);
                int hits = 0, ta0 = WildBossBase.TurnAways, ci0 = WildBossBase.CloseIns;
                Component bc = boss.C;
                void OnHitW(WildBossBase b, PlayerAttackKind k, bool lethal) { if (b == bc) hits++; }
                void OnHitO(Component b, PlayerAttackKind k) { if (b == bc) hits++; }
                WildBossBase.QaLocalHit += OnHitW; DragonController.QaLocalHit += OnHitO; MajinController.QaLocalHit += OnHitO;
                float t = 0f, reach = 0f, unreach = 0f, maxUnreach = 0f, inv = 0f, maxInv = 0f, behind = 0f, far = 0f, high = 0f, hidden = 0f, nextIn = 0f;
                while (t < secs && boss.Valid && !boss.IsDead)
                {
                    float dt = Time.deltaTime;
                    var hb = boss.Live; float px = pc.transform.position.x, py = pc.transform.position.y;
                    float edge = hb.min.x > px ? hb.min.x - px : hb.max.x < px ? px - hb.max.x : 0f;
                    bool hurtOn = boss.W == null || (boss.W.DebugHurtCol != null && boss.W.DebugHurtCol.enabled);
                    bool invul = boss.W != null && boss.W.DebugInvulnerable;
                    bool vOk = hb.min.y <= py + ReachUp && hb.max.y >= py - 0.5f;
                    bool ok = edge <= ReachX && vOk && hurtOn && !invul && !boss.Broken;
                    if (boss.Broken) ok = true; // BREAK 中は寄せで届く
                    if (ok) { reach += dt; unreach = 0f; } else { unreach += dt; maxUnreach = Mathf.Max(maxUnreach, unreach); }
                    if (invul || !hurtOn) { inv += dt; maxInv = Mathf.Max(maxInv, inv); } else inv = 0f;
                    if (hb.center.x < px - 0.5f) behind += dt;
                    if (edge > ReachX) far += dt;
                    if (!vOk) high += dt;
                    if (!hurtOn || invul) hidden += dt;
                    if (poke && ok && t >= nextIn)
                    {
                        var f = hb.center.x >= px ? PlayerController.FlickDirection.Forward : PlayerController.FlickDirection.Backward;
                        if (hb.min.y > py + 1.6f) f = PlayerController.FlickDirection.Up;
                        yield return HaFlick(f);
                        nextIn = t + 0.45f;
                    }
                    yield return null; t += dt;
                }
                WildBossBase.QaLocalHit -= OnHitW; DragonController.QaLocalHit -= OnHitO; MajinController.QaLocalHit -= OnHitO;
                float T = Mathf.Max(0.01f, t);
                L($"[reach] {key}: {t:F0}s reach {reach / T:P0} maxUnreach {maxUnreach:F1}s maxInvuln {maxInv:F1}s behind {behind / T:P0} far {far / T:P0} high {high / T:P0} hidden {hidden / T:P0} hits {hits} turnAways {WildBossBase.TurnAways - ta0} closeIns {WildBossBase.CloseIns - ci0}");
                tsv.Append($"{key}\t{t:F0}\t{reach / T:F2}\t{maxUnreach:F1}\t{maxInv:F1}\t{behind / T:F2}\t{far / T:F2}\t{high / T:F2}\t{hidden / T:F2}\t{hits}\t{WildBossBase.TurnAways - ta0}\n");
                System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "reach.tsv"), tsv.ToString());
                Check(maxUnreach <= 8f, $"reach {key}: a melee character gets a chance to hit at least every 8s (longest {maxUnreach:F1}s out of reach)");
                Check(reach / T >= 0.25f, $"reach {key}: the boss is within melee reach for at least a quarter of the fight ({reach / T:P0})");
                if (boss.Valid && !boss.IsDead) boss.Kill();
                yield return BfWaitEncounterEnd();
            }
            SetGod(false); GameManager.BlockExpGain = false;
            yield return EndRun();
        }
    }
}
#endif
