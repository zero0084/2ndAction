#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// メカドラゴン(希少な強敵、2026-10-13)の確認。 -qaMech <dir> [-qaChar swordsman] [-mechGate 60000] [-mechRush 0|1]
//   [-mechSeconds 40] [-mechVideo 0|秒] [-mechKmh 0(自然)] [-mechAssist 1]
// ラスダン(last_corridor)で関門の手前へ跳び、希少枠を強制して戦う(無敵。攻撃は自動操作補助+QA の弾き)。
// 測る物: 地形への埋まり(胴体の下端が地面より下)/ 天井の上 / 飛行のがたつき(画面上の位置の2階差分)/ BREAK 中の流れ /
//         ボスが画面の外にいる時の攻撃(見えない所からの攻撃)/ 処理の重さ(フレーム時間・弾/爆撃の同時数)/ 撃破後の残り物 /
//         ラッシュ: 同時に居たボスの数(メカドラゴンは単独)
public partial class QaSweep
{
    IEnumerator MechMode()
    {
        Application.targetFrameRate = 60;
        string ch = Arg("-qaChar", "swordsman");
        float gate = float.Parse(Arg("-mechGate", "60000"), System.Globalization.CultureInfo.InvariantCulture);
        bool rush = Arg("-mechRush", "0") == "1";
        float seconds = float.Parse(Arg("-mechSeconds", "40"), System.Globalization.CultureInfo.InvariantCulture);
        float video = float.Parse(Arg("-mechVideo", "0"), System.Globalization.CultureInfo.InvariantCulture);
        float kmh = float.Parse(Arg("-mechKmh", "0"), System.Globalization.CultureInfo.InvariantCulture);
        HookBossLogs();
        if (Arg("-mechView", "") != "") PortraitRunView.Set(int.Parse(Arg("-mechView", "0"))); // 縦画面の表示(0 横から / 1 上下2段 / 2 斜め上から)
        yield return BeginRun(ch, "last_corridor");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        if (Arg("-mechAssist", "1") == "1" && HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);
        var bm = Bm;
        int c0 = MechDragonBoss.LaserShots + MechDragonBoss.SweepShots + MechDragonBoss.VulcanBursts + MechDragonBoss.BombsDropped;
        if (rush)
        {
            BossManager.DevForceRareRush = true;
            pc.AddAttackPower(120); // ラッシュの前の相手は早く倒す(メカドラゴンが出たら戻す)
            WarpTo(89600f);
        }
        else
        {
            BossManager.DevForceRare = true;
            WarpTo(gate - 400f);
        }
        // メカドラゴンが出るまで
        MechDragonBoss mech = null;
        float w = 0f, limit = rush ? 420f : 40f;
        bool powerBack = !rush;
        while (w < limit)
        {
            if (kmh > 0f) SetKmh(kmh);
            mech = FindObjectsByType<MechDragonBoss>(FindObjectsSortMode.None).FirstOrDefault(m => m != null && !m.IsDead);
            if (mech != null) break;
            if (rush) FlickNearest();
            w += Time.deltaTime; yield return null;
        }
        Check(mech != null, $"mech spawned ({(rush ? "rush" : $"gate {gate:F0}m")}, waited {w:F0}s)");
        if (mech == null) { yield return EndRun(); yield break; }
        if (!powerBack) { pc.AddAttackPower(-120); powerBack = true; }
        L($"[mech] spawn d={gm.MaxDistance:F0} hp={mech.Hp}/{mech.maxHp} char={ch} rush={rush} res={Screen.width}x{Screen.height}");
        // 動画: 登場の警告から撃破まで(戦う時間は動画の長さ - 7秒。撃破の演出までを含める)
        Coroutine rec = null;
        if (video > 0f) { seconds = Mathf.Min(seconds, video - 7f); rec = StartCoroutine(RecordUltimate($"mech_{ch}{(rush ? "_rush" : "")}_{Screen.width}x{Screen.height}", video)); }
        else Shot("mech_enter");
        w = 0f; while (mech.IsEntering && w < 8f) { yield return null; w += Time.deltaTime; }
        Shot("mech_after_enter");

        var cam = Camera.main;
        var tm = TerrainManager.Instance;
        float t0 = Time.time, lastShot = 0f;
        int frames = 0, embedded = 0, aboveCeil = 0, offscreenAttack = 0, jitterSpikes = 0, maxBosses = 0, maxProj = 0, maxStrikes = 0, maxSprites = 0;
        float maxDt = 0f, sumDt = 0f, breakDrift = 0f, maxBreakDrift = 0f;
        Vector2 prevV = Vector2.zero, prevVel = Vector2.zero, prevAcc = Vector2.zero; int haveV = 0; float minRoom = 99f;
        int shakeSkip = 0; int breakBehind = 0; int clamps0 = MechDragonBoss.CeilingClamps; var jitterBy = new Dictionary<string, int>();
        bool wasBroken = false; float breakStartGap = 0f;
        bool forcedBreak = false, forcedUlt = false; float breakTry = -9f;
        while (Time.time - t0 < seconds && mech != null && !mech.IsDead)
        {
            float t = Time.time - t0;
            if (kmh > 0f) SetKmh(kmh);
            if (!forcedUlt && t > seconds * 0.3f) { forcedUlt = true; mech.DebugForceUltimate(); L($"[mech] force ultimate t={t:F1}"); }
            if (!forcedBreak && t > seconds * 0.6f && !mech.UltimateRunning && !mech.Broken && Time.time - breakTry > 1f) { breakTry = Time.time; int b0 = mech.BreakCount; mech.DebugForceBreak(); if (mech.BreakCount > b0) forcedBreak = true; L($"[mech] force BREAK t={t:F1} ok={mech.BreakCount > b0} roaring={GetPrivateBase(mech, "phaseRoaring")} entering={GetPrivateBase(mech, "entering")} stagger={GetPrivateBase(mech, "stagger")} def={GetPrivateBase(mech, "staggerDefense")} noStagger={BossBattle.DebugNoStagger}"); }
            frames++;
            float dt = Time.unscaledDeltaTime; sumDt += dt; if (frames > 5) maxDt = Mathf.Max(maxDt, dt);
            Bounds bb = mech.AssistBounds;
            float bx = bb.center.x;
            float? g = tm != null ? tm.GetHeightAt(bx) : null;
            float? cl = tm != null ? tm.GetCeilingHeightAt(bx) : null;
            if (g.HasValue && bb.min.y < g.Value - 0.3f && !mech.IsEntering) embedded++;
            float visTop = mech.transform.position.y + mech.bodyHeight * 0.97f * mech.VisualScale; // 見た目の上端(翼の先)
            if (cl.HasValue && visTop > cl.Value + 0.3f) aboveCeil++;
            // がたつき: ボス自身の動き(速さ)が、向きを変えながら続けて大きく変わる(ジグザグ)= 震え。
            // ゲーム内の時間で割った速さで見る(ヒットストップ=時間が止まったフレームと浮動原点の付け替えは飛ばす)。
            // カメラ(画面の揺れ・追従・斜め上の見え方)は含めない = どの表示でも同じ物差し
            Vector3 vp = cam.WorldToViewportPoint(bb.center);
            Vector2 pos = mech.transform.position;
            float gdt = Time.deltaTime;
            if (haveV > 0 && gdt > 0.5f / 60f && (pos - prevV).sqrMagnitude < 400f)
            {
                Vector2 vel = (pos - prevV) / gdt;
                if (haveV >= 2)
                {
                    Vector2 acc = vel - prevVel; // 1フレームでの速さの変化(m/s)
                    bool zig = (acc.x * prevAcc.x < 0f && Mathf.Abs(acc.x) > 6f && Mathf.Abs(prevAcc.x) > 6f) || (acc.y * prevAcc.y < 0f && Mathf.Abs(acc.y) > 6f && Mathf.Abs(prevAcc.y) > 6f);
                    if (zig && !mech.Broken) { jitterSpikes++; string key = mech.DebugAct + (Mathf.Abs(acc.x) > Mathf.Abs(acc.y) ? "/x" : "/y"); jitterBy[key] = jitterBy.TryGetValue(key, out int jn) ? jn + 1 : 1; }
                    prevAcc = acc;
                }
                prevVel = vel; haveV++;
            }
            else if (haveV == 0) haveV = 1;
            prevV = pos;
            if (cl.HasValue && g.HasValue) minRoom = Mathf.Min(minRoom, cl.Value - g.Value);
            // BREAK 中の流れ(プレイヤーから見た位置の変化)
            float gapNow = bb.center.x - pc.transform.position.x;
            if (mech.Broken && !wasBroken) breakStartGap = gapNow;
            if (mech.Broken) { breakDrift = Mathf.Abs(gapNow) - Mathf.Abs(breakStartGap); maxBreakDrift = Mathf.Max(maxBreakDrift, breakDrift); if (gapNow < -3f) breakBehind++; } // 流れ = プレイヤーから離れる向きの変化(届く位置へ寄せるのは正しい動き)
            wasBroken = mech.Broken;
            // 画面の外からの攻撃: ボス本体が画面外で、判定のある攻撃(ヒットボックス)が有効
            bool bossOff = vp.x < -0.05f || vp.x > 1.05f || vp.y < -0.1f || vp.y > 1.1f;
            bool hbActive = mech.GetComponentsInChildren<BossHitbox>().Any(h => h.IsActive && h.damagesPlayer);
            if (bossOff && hbActive) offscreenAttack++;
            int bosses = FindObjectsByType<WildBossBase>(FindObjectsSortMode.None).Count(b => b != null && !b.IsDead);
            maxBosses = Mathf.Max(maxBosses, bosses);
            maxProj = Mathf.Max(maxProj, FindObjectsByType<BossProjectile>(FindObjectsSortMode.None).Length);
            maxStrikes = Mathf.Max(maxStrikes, FindObjectsByType<SkyStrike>(FindObjectsSortMode.None).Length);
            if (frames % 30 == 0) maxSprites = Mathf.Max(maxSprites, mech.GetComponentsInChildren<SpriteRenderer>(true).Count(s => s.enabled && s.gameObject.activeInHierarchy));
            if (rec == null && t - lastShot >= 2f) { lastShot = t; Shot($"mech_t{t:00}"); }
            // QA の弾き: プレイヤーが近づいて攻撃する(自動操作補助と同じ方向)
            FlickNearest();
            yield return null;
        }
        int attacks = MechDragonBoss.LaserShots + MechDragonBoss.SweepShots + MechDragonBoss.VulcanBursts + MechDragonBoss.BombsDropped - c0;
        L($"[mech] {frames} frames  attacks laser={MechDragonBoss.LaserShots} sweep={MechDragonBoss.SweepShots} vulcan={MechDragonBoss.VulcanBursts} bombs={MechDragonBoss.BombsDropped} openings={MechDragonBoss.Openings} phase={mech.Phase} breaks={mech.BreakCount} ults={mech.UltimatesUsed} hp={mech.Hp}/{mech.maxHp}");
        L($"[mech] jitter by act: {string.Join(", ", jitterBy.OrderByDescending(k => k.Value).Select(k => k.Key + "=" + k.Value))}");
        L($"[mech] ceiling: min room {minRoom:F1}m, altitude clamps {MechDragonBoss.CeilingClamps - clamps0}");
        L($"[mech] embedded={embedded} aboveCeiling={aboveCeil} jitterSpikes={jitterSpikes} maxBreakDrift(away)={maxBreakDrift:F2}m breakBehindFrames={breakBehind} offscreenAttackFrames={offscreenAttack}");
        L($"[mech] load: avg {1000f * sumDt / Mathf.Max(1, frames):F1}ms max {1000f * maxDt:F1}ms  max projectiles={maxProj} strikes={maxStrikes} mechSprites={maxSprites} bossesAlive(max)={maxBosses}");
        Check(attacks > 0, $"mech attacks happen ({attacks})");
        Check(embedded <= frames * 0.01f, $"mech body is not embedded in the terrain ({embedded}/{frames} frames)");
        Check(aboveCeil <= frames * 0.01f, $"mech body does not go into the ceiling ({aboveCeil}/{frames} frames)");
        Check(offscreenAttack == 0, $"no damaging attack while the mech is off screen ({offscreenAttack} frames)");
        Check(maxBreakDrift < 3.5f, $"BREAK: the mech does not drift away ({maxBreakDrift:F2}m)");
        Check(jitterSpikes <= frames * 0.02f, $"flight is smooth ({jitterSpikes} spikes / {frames} frames)");
        if (rush) Check(maxBosses <= 1, $"rush: the mech fights alone (max bosses alive with it {maxBosses})");
        // 撃破 → 残り物
        if (mech != null && !mech.IsDead) mech.TakeDamage(Mathf.Max(999999, mech.Hp), mech.CenterWorld);
        Shot("mech_defeat_0");
        yield return new WaitForSeconds(0.8f); Shot("mech_defeat_1");
        yield return new WaitForSeconds(1.6f); Shot("mech_defeat_2");
        yield return new WaitForSeconds(6f);
        int left = FindObjectsByType<MechDragonBoss>(FindObjectsSortMode.None).Count(m => m != null);
        int bombs = FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None).Count(s => s != null && (s.name == "MechBomb" || s.name == "MechReticle"));
        int proj = FindObjectsByType<BossProjectile>(FindObjectsSortMode.None).Length;
        int strikes = FindObjectsByType<SkyStrike>(FindObjectsSortMode.None).Length;
        L($"[mech] after defeat: mech objects={left} bombs/reticles={bombs} projectiles={proj} strikes={strikes}");
        Check(left == 0 && bombs == 0 && proj == 0 && strikes == 0, "nothing of the mech remains after defeat");
        if (rec != null) { float rw = 0f; while (Time.captureFramerate != 0 && rw < video + 5f) { yield return null; rw += Time.unscaledDeltaTime; } }
        Shot("mech_after");
        BossManager.DevForceRare = false; BossManager.DevForceRareRush = false;
        yield return EndRun();
    }

    // 基底クラス(WildBossBase)の private フィールドを読む(確認の記録用)
    static object GetPrivateBase(object o, string field)
    {
        for (var t = o.GetType(); t != null; t = t.BaseType)
        {
            var f = t.GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (f != null) return f.GetValue(o);
        }
        return "?";
    }

    float mechFlickAt;
    void FlickNearest()
    {
        if (Time.time - mechFlickAt < 0.2f) return;
        Vector3? tp = NearestBoss();
        if (!tp.HasValue) return;
        mechFlickAt = Time.time;
        float dx = tp.Value.x - pc.transform.position.x, dy = tp.Value.y - pc.transform.position.y;
        StartCoroutine(Flick(dy > 2.2f ? PlayerController.FlickDirection.Up : dx < -0.5f ? PlayerController.FlickDirection.Backward : PlayerController.FlickDirection.Forward));
    }
}
#endif
