#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// 止まって戦うボス戦で、プレイヤーが上ルート(分岐の上の道)に上がった時の確認(2026-11-04)。
//  -qaHaltRoute <dir> [-hrBoss Wild/GoblinRider] [-hrChar swordsman] [-hrKm 31] [-hrSecs 90] [-hrReps 2] [-hrVariants 0,1] [-hrKmh 40]
//  見つかった不具合: 65km/70km のゴブリンライダーで、剣士のオートが 1,000秒以上ダメージ 0 → 関門が終わらない。
//   ボス戦で止まった後、踏み込み/ジャンプで分岐の上の道へ上がると、プレイヤーは前へ進まないので降りられず、
//   ボスは下の道のまま 2〜5m 下にいて近接が届かなかった(BREAK 中だけは上の道へ寄せる作りだった)。
//  1回 = 新しいラン → 生成済みの分岐の手前で関門の代わりにボスを出す → 止まりきったら上の道へ置く(踏み込みで上がったのと同じ状態)
//   → オート(攻撃/回避/ボス戦)で -hrSecs 秒戦う。variant 0 = 前の作り(HaltRouteLiftEnabled=false)/ 1 = 修正後
//  結果は haltroute.tsv(1行 = 1回: 撃破・秒・HP の減り・上の道にいた割合・ボスの寄せの最大)
public partial class QaSweep
{
    IEnumerator HaltRouteMode()
    {
        Application.targetFrameRate = 60;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string key = Arg("-hrBoss", "Wild/GoblinRider"), ch = Arg("-hrChar", "swordsman"), build = Arg("-hrBuild", "recommended");
        float km = float.Parse(Arg("-hrKm", "31"), inv), secs = float.Parse(Arg("-hrSecs", "90"), inv), kmh = float.Parse(Arg("-hrKmh", "40"), inv);
        int reps = int.Parse(Arg("-hrReps", "2")), hpSet = int.Parse(Arg("-hrHp", "2240"));
        float minRise = float.Parse(Arg("-hrMinRise", "3"), inv);
        var variants = Arg("-hrVariants", "0,1").Split(',').Select(s => s.Trim() == "1").ToArray();
        if (!BaParse(key, out int fam, out int kind)) { Check(false, $"haltroute: unknown boss {key}"); yield break; }
        var tsv = new System.Text.StringBuilder("variant\trep\tboss\tplaced\tkilled\tsecs\thp0\thp_end\thp_loss_pct\ton_sky_pct\tmax_lift\thalt_lift_frames\n");
        int killedNew = 0, runsNew = 0;
        foreach (bool fix in variants)
            for (int r = 0; r < reps; r++)
            {
                yield return BeginRun(ch, "wasteland_road");
                SetGod(true);
                AssistOn();
                GameManager.BlockExpGain = true;
                WildBossBase.HaltRouteLiftEnabled = fix;
                var tm = TerrainManager.Instance;
                tm.pitChanceBase = 0f; tm.pitChanceMax = 0f; tm.enemySpawnChance = 0f;
                if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
                WarpTo(km * 1000f); yield return new WaitForSeconds(1.2f);
                if (Bm.NextBossDistance - gm.MaxDistance < 3000f) { WarpTo(Bm.NextBossDistance + 800f); yield return new WaitForSeconds(1.2f); }
                for (float bw = 0f; Bm.IsBossPhase && bw < 120f; bw += Time.deltaTime) yield return null;
                SetupBuild(build, 65f);
                SetKmh(kmh);
                string tag = $"{(fix ? "fix" : "old")}#{r}";

                // 生成済みの分岐が少し先(止まりきる位置より先)に来るまで走る
                float fork = 0f, merge = 0f; bool found = false;
                // 上の道が下の道より minRise m 以上高い所(近接が届かなくなる高さの差。見つかった時は 2〜5m)
                float rise = 0f;
                for (float w = 0f; w < 240f && !found; w += Time.deltaTime)
                {
                    float px = pc.transform.position.x;
                    if (tm.TryGetBranchAfter(px, out fork, out merge, out bool gen) && gen && fork - px > 16f && fork - px < 40f && merge - fork > tm.BranchRampLength * 2f + 25f)
                    {
                        float mid = (fork + merge) * 0.5f;
                        float? s0 = tm.GetSkyHeightAt(mid), g0 = tm.GetHeightAt(mid);
                        rise = s0.HasValue && g0.HasValue ? s0.Value - g0.Value : 0f;
                        if (rise >= minRise) { found = true; break; }
                    }
                    yield return null;
                }
                if (!found) { L($"[haltroute] {tag}: no branch ahead (route={tm.RouteBranchEnabled})"); Check(false, $"haltroute {tag}: branch found"); yield return EndRun(); continue; }

                BaBoss boss = null;
                Bm.DebugSpawnBossForTest(fam, kind);
                SetKmh(kmh);
                for (float w = 0f; w < 15f; w += Time.deltaTime)
                {
                    yield return null;
                    boss = BaFind();
                    if (boss.W != null && !boss.W.IsEntering && BossHalt.Factor < 0.01f) break;
                }
                if (boss == null || boss.W == null) { Check(false, $"haltroute {tag}: boss spawned"); yield return EndRun(); continue; }
                var b = boss.W;

                // 止まった所から上の道へ(踏み込み/ジャンプで上がったのと同じ状態)。上り坂の途中ではなく、上の道が高くなった所(分岐の真ん中)へ
                float ux = (fork + merge) * 0.5f;
                float? sky = tm.GetSkyHeightAt(ux);
                float? low = tm.GetHeightAt(ux);
                bool placed = false;
                for (int k = 0; k < 3 && sky.HasValue && !placed; k++)
                {
                    pc.transform.position = new Vector3(ux, sky.Value + 2.5f, pc.transform.position.z); // 足元の高さ(groundOffset 約1m)より上から落として着地させる
                    // 地面に立ったままだと次のフレームで下の道の高さへ吸い付く(isGrounded の間は今の面に合わせる)→ 空中から落として上の道へ着地させる
                    typeof(PlayerController).GetField("isGrounded", NP)?.SetValue(pc, false);
                    typeof(PlayerController).GetField("velocityY", NP)?.SetValue(pc, 0f);
                    for (float w = 0f; w < 1.5f && !(pc.IsGrounded && pc.QaOnSky); w += Time.deltaTime) yield return null;
                    placed = pc.QaOnSky;
                }
                b.DebugScaleHp(hpSet / (float)Mathf.Max(1, b.maxHp)); // 70km の再戦と同じくらい(距離の HP だと数秒で倒れて比べられない)
                L($"[haltroute] {tag}: halt={BossHalt.Factor:F2} fork={fork:F0} merge={merge:F0} ux={ux:F0} rise={rise:F1} sky={(sky.HasValue ? sky.Value.ToString("F1") : "-")} low={(low.HasValue ? low.Value.ToString("F1") : "-")} placed={placed} px={pc.transform.position.x:F1} py={pc.transform.position.y:F1} boss hp={b.Hp}/{b.maxHp}");
                Check(placed, $"haltroute {tag}: player placed on the upper route");

                int hp0 = b.Hp, lift0 = WildBossBase.HaltRouteLiftFrames;
                float t = 0f, onSky = 0f, maxLift = 0f, logT = 0f;
                while (t < secs && b != null && !b.IsDead)
                {
                    yield return null; t += Time.deltaTime;
                    if (pc.QaOnSky) onSky += Time.deltaTime;
                    maxLift = Mathf.Max(maxLift, b.DebugBreakRouteLift);
                    if ((logT += Time.deltaTime) >= 5f)
                    {
                        logT = 0f;
                        var bb = b.AssistBounds;
                        L($"[haltroute]   {tag} t={t:F0}s hp={b.Hp}/{b.maxHp} act={b.DebugAct} gap={b.DebugGap:F1} lift={b.DebugBreakRouteLift:F1} by={bb.min.y - pc.transform.position.y:F1}..{bb.max.y - pc.transform.position.y:F1} player x={pc.transform.position.x:F1} y={pc.transform.position.y:F1} sky={pc.QaOnSky} halt={BossHalt.Factor:F2} plan='{(HighSpeedAssist.Instance != null ? HighSpeedAssist.Instance.BossPlan : "-")}'");
                    }
                }
                bool killed = b == null || b.IsDead;
                int hpEnd = killed ? 0 : b.Hp;
                float loss = 100f * (hp0 - hpEnd) / Mathf.Max(1, hp0);
                string row = $"{(fix ? 1 : 0)}\t{r}\t{key}\t{placed}\t{killed}\t{t:F1}\t{hp0}\t{hpEnd}\t{loss:F0}\t{100f * onSky / Mathf.Max(0.01f, t):F0}\t{maxLift:F1}\t{WildBossBase.HaltRouteLiftFrames - lift0}";
                tsv.Append(row).Append('\n');
                L($"[haltroute] {row}");
                System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "haltroute.tsv"), tsv.ToString());
                if (fix && placed) { runsNew++; if (killed) killedNew++; }
                if (!killed && boss.Valid) boss.Kill();
                for (float w = 0f; Bm.IsBossPhase && w < 20f; w += Time.deltaTime) yield return null;
                GameManager.BlockExpGain = false;
                PlayerController.DebugSpeedScale = 1f;
                yield return EndRun();
            }
        WildBossBase.HaltRouteLiftEnabled = true;
        if (runsNew > 0) Check(killedNew == runsNew, $"haltroute: fixed build kills the boss from the upper route ({killedNew}/{runsNew})");
        Check(true, "haltroute: done");
    }
}
#endif
