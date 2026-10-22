using System.Collections;
using UnityEngine;

// 2026-10-22 動きの見直し: 空中の被弾(空中のまま / 重い一撃は吹き飛ぶ / 叩き落としは地面へ)・空中で倒れた時の落下・走りのテンポの確認。
// -qaAirHit <dir> [-qaChars swordsman,archer]
public partial class QaSweep
{
    IEnumerator AirHitMode()
    {
        foreach (string ch in Arg("-qaChars", "swordsman,archer").Split(','))
        {
            foreach (string kind in new[] { "normal", "heavy", "slam", "ground", "death" })
            {
                yield return BeginRun(ch, "wasteland_road");
                if (BossManager.Instance != null) BossManager.Instance.enabled = false;
                if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
                if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
                foreach (var s in FindObjectsByType<ObstacleSpawner>(FindObjectsSortMode.None)) s.enabled = false;
                TerrainManager.Instance.pitChanceBase = 0f; TerrainManager.Instance.pitChanceMax = 0f; TerrainManager.Instance.enemySpawnChance = 0f;
                typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
                yield return new WaitForSeconds(1.5f);
                while (!pc.IsGrounded) yield return null;
                if (kind == "death") { stopKeepAlive = true; yield return null; typeof(GameManager).GetProperty("Lives").SetValue(gm, CombatScale.PlayerHit); } // 残り1発(HP を戻す見張りを止める)
                float groundY = pc.transform.position.y;
                if (kind != "ground")
                {
                    typeof(PlayerController).GetField("isGrounded", NP).SetValue(pc, false);
                    typeof(PlayerController).GetField("velocityY", NP).SetValue(pc, 9f);
                    yield return new WaitForSeconds(0.25f);
                }
                float y0 = pc.transform.position.y; int air0 = PlayerController.AirHits;
                int amount = kind == "heavy" ? BossManager.ScaleDamage(CombatScale.PlayerHeavyHit) : CombatScale.PlayerHit;
                int livesBefore = gm.Lives;
                pc.TakeDamage(false, "qa-airhit-" + kind, amount, kind == "slam");
                L($"[airhit] {ch} {kind}: lives {livesBefore} -> {gm.Lives}");
                yield return null;
                float y1 = pc.transform.position.y;
                float peak = y1, tLand = -1f, t = 0f;
                bool hurtSeen = pc.IsHurt;
                while (t < 3f)
                {
                    yield return null; t += Time.deltaTime;
                    peak = Mathf.Max(peak, pc.transform.position.y);
                    hurtSeen |= pc.IsHurt;
                    if (tLand < 0f && pc.transform.position.y <= groundY + 0.05f) tLand = t;
                }
                float h0 = y0 - groundY;
                L($"[airhit] {ch,-10} {kind,-6} height at hit {h0:F2}m, after 1 frame {y1 - groundY:F2}m, peak {peak - groundY:F2}m, landed {(tLand < 0 ? "no" : tLand.ToString("F2") + "s")} hurt={hurtSeen} airHits+{PlayerController.AirHits - air0} gameOver={gm.IsGameOver}");
                if (kind == "ground") { Check(PlayerController.AirHits == air0, $"{ch} ground hit is not an air hit"); Check(hurtSeen, $"{ch} ground hit shows the hurt pose"); }
                else
                {
                    Check(y1 - groundY > h0 - 0.4f, $"{ch} {kind}: no instant warp to the ground on the hit ({h0:F2} -> {y1 - groundY:F2})");
                    Check(tLand >= 0f, $"{ch} {kind}: falls and lands");
                    if (kind != "death") Check(hurtSeen, $"{ch} {kind}: hurt pose in the air");
                    if (kind == "heavy") Check(peak - groundY > h0 + 0.4f, $"{ch} heavy: blown up into the air ({h0:F2} -> peak {peak - groundY:F2})");
                    if (kind == "slam") Check(tLand >= 0f && tLand < 0.45f, $"{ch} slam: forced down quickly ({tLand:F2}s)");
                    if (kind == "normal") Check(tLand > 0.15f, $"{ch} normal: falls naturally (not snapped) {tLand:F2}s");
                    if (kind == "death") Check(gm.IsGameOver, $"{ch} death: game over");
                }
                yield return EndRun();
            }
            // 走りのテンポ: 速いほど上がる(最大2倍)
            yield return BeginRun(ch, "wasteland_road");
            if (BossManager.Instance != null) BossManager.Instance.enabled = false;
            var anim = pc.GetComponentInChildren<PlayerAnimator>();
            foreach (float kmh in new[] { 18f, 50f, 100f, 200f })
            {
                float tt = 0f; while (tt < 2f) { SetKmh(kmh); yield return null; tt += Time.deltaTime; }
                L($"[runtempo] {ch,-10} {kmh,4:F0} km/h: tempo x{(anim != null ? anim.CurrentRunTempo : -1f):F2}");
            }
            yield return EndRun();
        }
    }
}
