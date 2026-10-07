#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// 追加の4件(2026-10-07)の確認: -qaFix4 <dir> [-qaF4Only SRC]
//  S 疾走出発の行き先: そのマップの最高到達距離 >= 行き先(55km → 50kmまで / 59,999m → 60km不可 / 60,000m → 可)、門番の撃破も必要、
//    表示と出発の判定が同じ、保存→読み直しの後も同じ、他のマップの記録を見ない
//  R 転がる石: 平地/坂は地面に沿い、穴の上では落ちる。落ちた後に上の地面へ戻らない。下の足場には上から着地
//  C 天井に張り付くボス: 張り付く途中/最中/降りた後に被弾判定が有効で、体の見た目の中にある。天井の無い所では張り付き技を選ばない。
//    張り付き中に天井を見失ったら降りて通常の行動へ
public partial class QaSweep
{
    bool F4Case(char c) { string o = Arg("-qaF4Only", ""); return o == "" || o.IndexOf(c) >= 0; }

    IEnumerator Fix4Mode()
    {
        if (F4Case('S')) yield return F4Sprint();
        if (F4Case('R')) yield return F4Rock();
        if (F4Case('C')) yield return F4Ceiling();
    }

    // ===================================================================== S
    IEnumerator F4Sprint()
    {
        L("== S: 疾走出発の行き先 ==");
        float w = 0f; while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.5f);
        gm = GameManager.Instance;
        var setBest = typeof(GameManager).GetMethod("SetStageBest", NPI);
        var cacheF = typeof(GameManager).GetField("stageBestCache", NPI);
        string st = "last_corridor", other = "wasteland_road";
        bool dev0 = SprintRecords.DevUnlockAll; SprintRecords.DevUnlockAll = false;
        for (int g = 10; g <= 90; g += 10) SprintRecords.MarkGateCleared(st, g);
        setBest.Invoke(gm, new object[] { other, 99000.0 }); // 他のマップの記録は関係しない
        string Row(double best)
        {
            setBest.Invoke(gm, new object[] { st, best });
            var list = SprintRecords.Destinations(st);
            int maxOk = list.Where(d => d.unlocked).Select(d => d.meters).DefaultIfEmpty(0).Max();
            bool sameAsDepart = list.All(d => SprintRecords.IsUnlocked(st, d.meters, out _) == d.unlocked);
            string why60 = list.FirstOrDefault(d => d.meters == 60000).why;
            L($"[S] best {best:F0}m -> selectable up to {maxOk / 1000}km (60km: {(list.First(d => d.meters == 60000).unlocked ? "OK" : "locked: " + why60)}), display==depart {sameAsDepart}");
            Check(sameAsDepart, $"S: the list and the departure use the same check (best {best:F0})");
            return $"{maxOk}";
        }
        Check(Row(55400) == "50000", "S: best 55.4km -> up to 50km (60km not selectable)");
        Check(Row(59999) == "50000", "S: best 59,999m -> 60km still locked");
        Check(Row(59999.9) == "50000", "S: best 59,999.9m -> 60km still locked (no rounding up)");
        Check(Row(60000) == "60000", "S: best 60,000m -> 60km selectable");
        // 門番の撃破も必要: 30km の門番の記録が無ければ、40km 以降は選べない
        var clearedF = typeof(SprintRecords).GetField("cleared", BindingFlags.NonPublic | BindingFlags.Static);
        var dict = clearedF.GetValue(null) as Dictionary<string, HashSet<int>>;
        dict[st].Remove(30);
        setBest.Invoke(gm, new object[] { st, 75000.0 });
        var l2 = SprintRecords.Destinations(st);
        int max2 = l2.Where(d => d.unlocked).Select(d => d.meters).DefaultIfEmpty(0).Max();
        L($"[S] best 75km without the 30km gate record -> up to {max2 / 1000}km ({l2.First(d => d.meters == 40000).why})");
        Check(max2 == 30000, "S: a missing gate keeps the later destinations locked even when the distance was reached");
        dict[st].Add(30);
        // 保存→読み直し(キャッシュを捨てて保存された値から): 同じ結果
        setBest.Invoke(gm, new object[] { st, 55400.0 });
        SaveStore.Save();
        SprintRecords.Reload();
        (cacheF.GetValue(gm) as System.Collections.IDictionary)?.Clear();
        var l3 = SprintRecords.Destinations(st);
        int max3 = l3.Where(d => d.unlocked).Select(d => d.meters).DefaultIfEmpty(0).Max();
        L($"[S] after reloading the saved values: up to {max3 / 1000}km");
        Check(max3 == 50000, "S: same result after reloading the save");
        // DEBUG の全解放は、そのことが分かる表示
        SprintRecords.DevUnlockAll = true;
        var l4 = SprintRecords.Destinations(st);
        Check(l4.First(d => d.meters == 60000).why == "DEBUG 全解放", "S: with the DEBUG unlock-all, the row says so");
        SprintRecords.DevUnlockAll = dev0;
    }

    // ===================================================================== R
    IEnumerator F4Rock()
    {
        L("== R: 転がる石 ==");
        yield return BeginRun("swordsman", "wasteland_road");
        typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
        BossManager.Instance.enabled = false;
        var tm = TerrainManager.Instance;
        tm.pitChanceBase = 0.6f; tm.pitChanceMax = 0.8f;
        // 穴が生成されるまで走る
        float pitStart = float.NaN, pitEnd = float.NaN, w = 0f;
        while (w < 60f && float.IsNaN(pitEnd))
        {
            yield return new WaitForSeconds(0.5f); w += 0.5f;
            float px = pc.transform.position.x;
            for (float x = px + 12f; x < px + 80f; x += 0.25f)
            {
                if (float.IsNaN(pitStart)) { if (!tm.GetHeightAt(x).HasValue && tm.GetHeightAt(x - 0.5f).HasValue) pitStart = x; }
                else if (tm.GetHeightAt(x).HasValue) { pitEnd = x; break; }
            }
            if (!float.IsNaN(pitStart) && float.IsNaN(pitEnd)) pitStart = float.NaN;
        }
        Check(!float.IsNaN(pitEnd), "R: found a pit ahead to test with");
        if (float.IsNaN(pitEnd)) yield break;
        tm.pitChanceBase = 0f; tm.pitChanceMax = 0f;
        // 穴の手前から、穴へ向かって(走りの座標系では止まっている = 世界に対して走りと同じ速さ)転がす
        foreach (var bounce in new[] { 0f, 1.3f })
        {
            float gy = tm.GetHeightAt(pitStart - 4f) ?? pc.transform.position.y;
            var rock = BossProjectile.Create(CaveBossFx.RockChunk(), Color.white, new Vector3(pitStart - 4f, gy + 0.6f, 0f), new Vector2(1.2f, 1.2f), new Vector2(3f, 0f), 6f, RenderOrder.CombatFx);
            rock.hugGround = true; rock.groundOffset = 0.6f; rock.bounceHeight = bounce; rock.bouncePeriod = 0.6f; rock.damage = false;
            float prevY = rock.transform.position.y, t = 0f, minY = 99999f; bool overPit = false, fell = false, cameBackUp = false, destroyed = false;
            float maxRiseAfterPit = 0f;
            while (t < 5f)
            {
                yield return null; t += Time.deltaTime;
                if (rock == null) { destroyed = true; break; }
                var p = rock.transform.position;
                if (p.x > pitStart + 0.3f && p.x < pitEnd - 0.3f) overPit = true;
                if (overPit && p.y < prevY - 0.05f) fell = true;
                if (fell) { minY = Mathf.Min(minY, p.y); if (p.y > minY + 0.2f) { cameBackUp = true; maxRiseAfterPit = Mathf.Max(maxRiseAfterPit, p.y - minY); } }
                prevY = p.y;
            }
            L($"[R] bounce {bounce}: pit {pitStart:F1}..{pitEnd:F1}, over the pit {overPit}, fell {fell}, came back up {cameBackUp} (rise {maxRiseAfterPit:F2}m), destroyed below the screen {destroyed}");
            Check(overPit && fell, $"R: a rock (bounce {bounce}) falls into the pit");
            Check(!cameBackUp, $"R: a fallen rock (bounce {bounce}) does not come back up to the ground");
            if (rock != null) Object.Destroy(rock.gameObject);
        }
        // 平地/坂: 地面に沿う(落ちない)
        {
            float x0 = pc.transform.position.x + 6f;
            float gy = tm.GetHeightAt(x0) ?? pc.transform.position.y;
            var rock = BossProjectile.Create(CaveBossFx.RockChunk(), Color.white, new Vector3(x0, gy + 0.6f, 0f), new Vector2(1.2f, 1.2f), new Vector2(-2f, 0f), 2.5f, RenderOrder.CombatFx);
            rock.hugGround = true; rock.groundOffset = 0.6f; rock.damage = false;
            float maxOff = 0f, t = 0f;
            while (t < 2f && rock != null)
            {
                yield return null; t += Time.deltaTime;
                if (rock == null) break;
                float? g = tm.GetHeightAt(rock.transform.position.x);
                if (g.HasValue) maxOff = Mathf.Max(maxOff, Mathf.Abs(rock.transform.position.y - 0.6f - g.Value));
            }
            L($"[R] flat ground: max offset from the ground {maxOff:F2}m");
            Check(maxOff < 0.3f, "R: on flat ground / slopes a rock rolls along the ground");
            if (rock != null) Object.Destroy(rock.gameObject);
        }
        yield return EndRun();
    }

    // ===================================================================== C
    IEnumerator F4Ceiling()
    {
        L("== C: 天井に張り付くボス ==");
        // 洞窟: 天井がある → 張り付ける。被弾判定は張り付く途中/最中/降りた後も有効で、見た目の中
        yield return BfBeginStage("natural_cave");
        foreach (var kind in new[] { CaveBossKind.Centipede, CaveBossKind.Bat })
        {
            yield return BfWaitEncounterEnd();
            BossManager.Instance.DebugSpawnBossForTest(1, (int)kind);
            WildBossBase b = null; float w = 0f;
            while (w < 12f) { yield return null; w += Time.unscaledDeltaTime; b = BfFindBoss() as WildBossBase; if (b != null && !b.IsEntering && w > 1.5f) break; }
            if (b == null) { Check(false, $"C: {kind} spawned"); continue; }
            var canUse = typeof(HazardBossBase).GetMethod("CanUseCeiling", NPI);
            // 洞窟でも低い天井の区間では選ばない(正しい)。走りながら数か所で確かめ、どこかで使えれば良い
            bool can = false; string skips = "";
            for (int tries = 0; tries < 8 && !can; tries++)
            {
                can = (bool)canUse.Invoke(b, new object[] { 5f });
                if (!can) { skips += HazardBossBase.LastCeilingSkip + " | "; yield return new WaitForSeconds(1.5f); }
            }
            if (skips != "") L($"[C] {kind}: places where the ceiling move was not chosen: {skips}");
            L($"[C] {kind} in the cave: can use the ceiling {can} {(can ? "" : "(" + HazardBossBase.LastCeilingSkip + ")")}");
            var hurt = typeof(WildBossBase).GetField("hurtCol", NPI).GetValue(b) as BoxCollider2D;
            var inv = typeof(WildBossBase).GetField("invulnerable", NPI);
            var onCeilF = typeof(HazardBossBase).GetField("onCeiling", NPI);
            var routine = kind == CaveBossKind.Centipede ? "CeilingChase" : "CeilingHang";
            var interrupt = typeof(WildBossBase).GetMethod("InterruptAI", NPI);
            var r = (IEnumerator)b.GetType().GetMethod(routine, NPI).Invoke(b, null);
            interrupt.Invoke(b, new object[] { r });
            int climbing = 0, onCeil = 0, after = 0, bad = 0, invFrames = 0, offBody = 0;
            float t = 0f; bool wasOn = false;
            var rends = b.GetComponentsInChildren<SpriteRenderer>();
            while (t < 9f && b != null && !b.IsDead)
            {
                yield return null; t += Time.deltaTime;
                bool on = (bool)onCeilF.GetValue(b);
                if (on) wasOn = true;
                bool roaring = (bool)typeof(WildBossBase).GetField("phaseRoaring", NPI).GetValue(b); // 段階が上がる咆哮の間は意図的に無敵(仕様)
                bool hittable = roaring || (hurt != null && hurt.enabled && !(bool)inv.GetValue(b));
                if (on) { onCeil++; if (!hittable) bad++; if ((bool)inv.GetValue(b)) invFrames++; }
                else if (wasOn) { after++; if (!hittable) bad++; if (after > 60) break; }
                // 被弾判定の中心が、体の絵のどれかの範囲の中にあるか(上下反転の時に判定だけ天井の中に残っていないか)
                var exScale = (Vector2)typeof(WildBossBase).GetField("extraScale", NPI).GetValue(b);
                if (hurt != null && hurt.enabled && Mathf.Abs(exScale.y) > 0.5f) // 上下の反転の途中(体の高さがほぼ0)は除く
                {
                    Vector3 c = hurt.bounds.center;
                    bool inside = rends.Any(sr => { if (sr == null || !sr.enabled || sr.sprite == null) return false; var bb = sr.bounds; bb.Expand(0.6f); return bb.Contains(new Vector3(c.x, c.y, bb.center.z)); });
                    if (!inside) offBody++;
                }
            }
            L($"[C] {kind}: frames on the ceiling {onCeil}, after {after}, not hittable {bad}, invulnerable on the ceiling {invFrames}, hurtbox away from the body {offBody}");
            // 張り付ける天井が無い区間(高すぎて届かない/低すぎる)では技を選ばないのが正しい: その時は注意として記録し、張り付きの判定は見ない
            if (!can) Warn($"C: {kind} found no usable ceiling around the boss here (terrain), see the reasons above");
            if (onCeil > 0)
            {
                Check(bad == 0, $"C: {kind} can be hit while hanging and after dropping ({bad} frames not hittable)");
                Check(offBody <= 3, $"C: {kind} hurtbox follows the body (upside down on the ceiling) ({offBody} frames off; up to 3 frames of one-frame update-order lag while it drops are tolerated)");
            }
            else if (can) Check(false, $"C: {kind} could use the ceiling but never got onto it");
            b.DebugKillWithAttack(BossFinalAttack.Forward);
            yield return BfWaitEncounterEnd();
        }
        yield return EndRun();

        // 荒野(天井が無い): 張り付き技を選ばない。無理に張り付かせても、天井を見失って降りる
        yield return BfBeginStage("wasteland_road");
        BossManager.Instance.DebugSpawnBossForTest(1, (int)CaveBossKind.Centipede);
        WildBossBase c2 = null; float w2 = 0f;
        while (w2 < 12f) { yield return null; w2 += Time.unscaledDeltaTime; c2 = BfFindBoss() as WildBossBase; if (c2 != null && !c2.IsEntering && w2 > 1.5f) break; }
        if (c2 != null)
        {
            bool can2 = (bool)typeof(HazardBossBase).GetMethod("CanUseCeiling", NPI).Invoke(c2, new object[] { 5f });
            int lost0 = HazardBossBase.CeilingLostCount;
            var climb = (IEnumerator)typeof(HazardBossBase).GetMethod("ClimbToCeiling", NPI).Invoke(c2, new object[] { 0.4f });
            typeof(WildBossBase).GetMethod("InterruptAI", NPI).Invoke(c2, new object[] { climb });
            float t = 0f; var onCeilF = typeof(HazardBossBase).GetField("onCeiling", NPI);
            bool back = false;
            while (t < 4f) { yield return null; t += Time.deltaTime; if (HazardBossBase.CeilingLostCount > lost0 && !(bool)onCeilF.GetValue(c2)) { back = true; break; } }
            L($"[C] wasteland (no ceiling): can use the ceiling {can2}, forced climb -> lost the ceiling {HazardBossBase.CeilingLostCount - lost0}, back to the ground {back} after {t:F1}s");
            Check(!can2, "C: no ceiling moves where there is no ceiling");
            Check(back, "C: a boss that loses its ceiling drops and goes back to normal moves");
            c2.DebugKillWithAttack(BossFinalAttack.Forward);
            yield return BfWaitEncounterEnd();
        }
        else Check(false, "C: centipede spawned in the wasteland");
        yield return EndRun();
    }
}
#endif
