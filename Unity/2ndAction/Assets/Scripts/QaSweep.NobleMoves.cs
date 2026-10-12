#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// お嬢様の空中の技(薙ぎ払い/振り下ろし急降下, 2026-10-12)の確認。闘技場の「動かない標的」を置き、入力は実際の操作と同じフリックで入れる。
//   -qaNobleMoves <dir> [-nmDist 80000] [-nmCards none|range|speed|power|jump|all] [-qaNmShots 1]
// 各場面: 地上の一振り(基準) / 空中の前フリック(地面の標的・浮いた標的) / 空中の後フリック / 空中の下フリック(真下・少し前・衝撃波だけ)
// 記録: 1回の技で標的が受けた「ダメージの回数と合計」(同じ当たりが二重に入っていないか)、技の後にポーズ/当たりが残っていないか、着地後に動けるまでの時間。
public partial class QaSweep
{
    IEnumerator NobleMovesMode()
    {
        var snapSave = SaveSystem.Capture();
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        gm = GameManager.Instance;
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60;
        float dist = float.Parse(Arg("-nmDist", "80000"), System.Globalization.CultureInfo.InvariantCulture);
        L($"[noble moves] dist={dist:F0} {BalanceTuning.DescribeChar}");
        foreach (var cards in Arg("-nmCards", "none,all").Split(','))
        {
            // (名前, 標的の前方距離, 標的の高さ, 入力の順番) 入力: J=ジャンプ, F=前, B=後, D=下, 数字=待ち(1/100秒)
            var scenes = new (string name, float ahead, float lift, string seq)[]
            {
                ("ground_forward", 1.4f, 0f, "F"),
                ("air_forward_low", 1.4f, 0f, "J 30 F"),
                ("air_forward_near", 0.9f, 0f, "J 22 F"),
                ("air_forward_far", 2.0f, 0f, "J 30 F"),
                ("air_backward", -1.2f, 0f, "J 30 B"),
                ("dive_onto", 0.35f, 0f, "J 32 D"),
                ("dive_shock_only", 1.25f, 0f, "J 32 D"),
                ("dive_miss_far", 2.6f, 0f, "J 32 D"),
            };
            foreach (var sc in scenes)
            {
                ArenaConfigReset();
                var c = ArenaMode.Config;
                c.character = "noble_lady"; c.distance = dist; c.invincible = true; c.assistMode = 0; c.speedMode = 0; c.kmh = 0f;
                c.seedFixed = true; c.seed = 7101;
                c.build.Clear();
                if (cards == "range" || cards == "all") c.build.Add(new ArenaBuildEntry { key = "attack_range_up", times = 9 });
                if (cards == "speed" || cards == "all") c.build.Add(new ArenaBuildEntry { key = "attack_speed_up", times = 9 });
                if (cards == "power" || cards == "all") c.build.Add(new ArenaBuildEntry { key = "attack_up", times = 9 });
                if (cards == "jump" || cards == "all") c.build.Add(new ArenaBuildEntry { key = "jump_power_up", times = 9 });
                c.enemies.Clear();
                c.enemies.Add(new ArenaEnemyEntry { kind = ArenaEnemyKind.Dummy, count = 1, ahead = Mathf.Max(1.5f, Mathf.Abs(sc.ahead)) });
                yield return ArenaLaunch($"NM {cards} {sc.name}");
                pc = PlayerController.Instance;
                yield return new WaitForSeconds(0.4f);
                var dummy = FindObjectsByType<EnemyController>(FindObjectsSortMode.None).FirstOrDefault(e => e != null && e.ArenaDummy);
                if (dummy == null || pc == null) { L($"[nm {cards} {sc.name}] dummy missing"); continue; }
                // 標的の位置を決める(闘技場の配置は前方 1.5m 以上なので、ここで置き直す)
                Vector3 dp = dummy.transform.position;
                dp.x = pc.transform.position.x + sc.ahead;
                dummy.transform.position = dp;
                var rb = dummy.GetComponent<Rigidbody2D>(); if (rb != null) { rb.position = dp; rb.linearVelocity = Vector2.zero; }
                yield return new WaitForSeconds(0.3f);
                int hp0 = dummy.CurrentHpForUltimate, last = hp0;
                var drops = new List<int>();
                int air0 = PlayerController.NobleAirHits, dv0 = PlayerController.NobleDiveHits, sh0 = PlayerController.NobleShockHits;
                float t0 = Time.time, landAt = -1f, freeAt = -1f;
                bool wasAir = false; int shot = 0; float nextShot = 0f;
                string poses = "";
                var seq = new Queue<string>(sc.seq.Split(' '));
                float waitUntil = 0f;
                bool shots = Arg("-qaNmShots", "1") == "1" && cards == "none";
                while (Time.time - t0 < 2.4f)
                {
                    if (seq.Count > 0 && Time.time >= waitUntil)
                    {
                        string s = seq.Dequeue();
                        if (int.TryParse(s, out int cs)) waitUntil = Time.time + cs / 100f;
                        else
                        {
                            pc.debugInjectFlick = s == "J" ? PlayerController.FlickDirection.Up : s == "F" ? PlayerController.FlickDirection.Forward : s == "B" ? PlayerController.FlickDirection.Backward : PlayerController.FlickDirection.Down;
                            yield return null;
                            pc.debugInjectFlick = null;
                            continue;
                        }
                    }
                    int hp = dummy.CurrentHpForUltimate;
                    if (hp < last) drops.Add(last - hp);
                    last = hp;
                    if (!pc.IsGrounded) wasAir = true;
                    else if (wasAir && landAt < 0f) landAt = Time.time - t0;
                    if (landAt >= 0f && freeAt < 0f && !pc.IsAttacking && !pc.IsDiveAttacking && pc.KitPoseFrames == null) freeAt = Time.time - t0;
                    string ps = pc.KitPoseName != null ? $"{pc.KitPoseName}{pc.KitPoseFrame}" : "-";
                    if (!poses.EndsWith(ps)) poses += (poses.Length > 0 ? ">" : "") + ps;
                    if (shots && Time.time - t0 >= nextShot && Time.time - t0 < 1.3f && seq.Count == 0)
                    {
                        Shot($"nm_{sc.name}_{shot++:00}_{ps}");
                        nextShot = Time.time - t0 + 0.06f;
                    }
                    yield return null;
                }
                bool poseCleared = pc.KitPoseFrames == null;
                bool hitboxOff = pc.attackHitbox == null || !pc.attackHitbox.enabled;
                string line = $"{cards} {sc.name} ahead={sc.ahead:F2} hits={drops.Count} dmg=[{string.Join(",", drops)}] total={drops.Sum()} airHits={PlayerController.NobleAirHits - air0} diveHits={PlayerController.NobleDiveHits - dv0} shockHits={PlayerController.NobleShockHits - sh0} atk={pc.AttackPower} land={landAt:F2} free={freeAt:F2} lockout={(freeAt >= 0f && landAt >= 0f ? freeAt - landAt : -1f):F2} poses={poses} poseCleared={poseCleared} hitboxOff={hitboxOff}";
                L("[nm] " + line);
                Check(poseCleared && hitboxOff, $"[nm {cards} {sc.name}] pose/hitbox cleared after the move");
                Check(drops.Count <= 2, $"[nm {cards} {sc.name}] at most 2 damage events per move (dive + shock)");
            }
        }
        SaveSystem.Restore(snapSave);
        CardInventory.ReloadFromPrefs(); CardMastery.ReloadFromPrefs();
        RunCheckpoint.Reload();
    }
}
#endif
