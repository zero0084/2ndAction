#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ボスの攻撃の見た目の点検(2026-10-11、実機: 攻撃の絵が無く色付きの四角だけが見える)。
//  -qaAtkFx <dir> [-qaAtkOnly W_Serpent,C_Troll,S_Majin] [-qaAtkSeconds 24] [-qaAtkRes 960x540] [-qaAtkView side|oblique] [-qaAtkStrict 1]
// 闘技場へボスを1体ずつ出し(無敵)、途中で段階を進め・必殺技を出させて、当たる攻撃(判定が有効な間)の絵を毎フレーム調べる。
//  ・仮の絵(BossFx.Block = 単色の四角 / SkyBossFx.Beam = 手続きの帯)が攻撃の本体として出ていたら記録して撮る
//  ・判定が有効なのに見える絵が無い(透明/無効)も記録する
//  -qaAtkStrict 1 の時はどちらも失敗として数える(直した後の確認)。
public partial class QaSweep
{
    struct AtkTarget { public ArenaEnemyKind kind; public int id; public string name; }

    class AtkSeen { public int frames, placeholder, invisible; public string sprites = ""; }

    IEnumerator AtkFxMode()
    {
        var snapSave = SaveSystem.Capture();
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        gm = GameManager.Instance;

        var targets = new List<AtkTarget>();
        foreach (WildBossKind k in System.Enum.GetValues(typeof(WildBossKind))) targets.Add(new AtkTarget { kind = ArenaEnemyKind.WildBoss, id = (int)k, name = "W_" + k });
        foreach (CaveBossKind k in System.Enum.GetValues(typeof(CaveBossKind))) targets.Add(new AtkTarget { kind = ArenaEnemyKind.CaveBoss, id = (int)k, name = "C_" + k });
        foreach (SkyBossKind k in System.Enum.GetValues(typeof(SkyBossKind))) targets.Add(new AtkTarget { kind = ArenaEnemyKind.SkyBoss, id = (int)k, name = "S_" + k });
        string only = Arg("-qaAtkOnly", "");
        if (only != "") { var set = new HashSet<string>(only.Split(',')); targets = targets.Where(t => set.Contains(t.name)).ToList(); }
        float secs = float.Parse(Arg("-qaAtkSeconds", "24"), System.Globalization.CultureInfo.InvariantCulture);
        int maxShots = int.Parse(Arg("-qaAtkShots", "5"));
        bool strict = Arg("-qaAtkStrict", "0") == "1";
        string[] res = Arg("-qaAtkRes", "960x540").Split('x');
        int rw = int.Parse(res[0]), rh = int.Parse(res[1]);
        string view = Arg("-qaAtkView", "side");
        SetRes(rw, rh); yield return WaitRes(rw, rh);
        PortraitRunView.Set(view == "oblique" ? PortraitRunView.Oblique : PortraitRunView.Side);

        Sprite block = BossFx.Block(), beam = SkyBossFx.ProceduralBeam();
        int totalPlaceholder = 0, totalInvisible = 0;
        var summary = new List<string>();
        foreach (var t in targets)
        {
            ArenaConfigReset();
            var c = ArenaMode.Config;
            c.distance = float.Parse(Arg("-qaAtkDist", "30000"), System.Globalization.CultureInfo.InvariantCulture);
            c.invincible = true; c.devAllEnemies = true; c.speedMode = 1;
            c.enemies.Clear();
            c.enemies.Add(new ArenaEnemyEntry { kind = t.kind, bossKind = t.id, count = 1 });
            yield return ArenaLaunch("ATK " + t.name);
            float w = 0f; while (SkyBossAlive() == null && w < 12f) { yield return null; w += Time.deltaTime; }
            var boss = SkyBossAlive();
            if (boss == null) { L($"[{t.name}] boss did not appear"); Check(false, $"{t.name}: the boss appears"); continue; }
            var seen = new Dictionary<string, AtkSeen>();
            int shots = 0; bool phased = false, ult = false; float lastShot = -99f;
            float t0 = Time.time;
            while (Time.time - t0 < secs && boss.DebugAlive)
            {
                float el = Time.time - t0;
                if (!phased && el > secs * 0.35f && boss.PhaseCount > 1) { phased = true; boss.DebugSetPhase(boss.PhaseCount - 1); L($"[{t.name}] phase -> {boss.PhaseCount - 1}"); }
                if (!ult && el > secs * 0.6f) { ult = true; bool ok = boss.DebugForceUltimate(); L($"[{t.name}] force ultimate -> {ok}"); }
                SetPrivate(pc, "hitInvincibleTimer", 1f);
                bool newKind = false;
                foreach (var a in ActiveAttackVisuals())
                {
                    if (!seen.TryGetValue(a.key, out var s)) { s = new AtkSeen(); seen[a.key] = s; newKind = true; }
                    s.frames++;
                    var vis = a.srs.Where(r => r != null && r.enabled && r.gameObject.activeInHierarchy && r.sprite != null && r.color.a > 0.12f && !r.name.StartsWith("Shadow") && !r.name.StartsWith("Warn")).ToList();
                    bool ph = vis.Any(r => (r.sprite == block || r.sprite == beam) && r.color.maxColorComponent > 0.2f);
                    if (ph) { s.placeholder++; if (s.placeholder == 1) newKind = true; }
                    if (vis.Count == 0) s.invisible++;
                    string names = string.Join("+", vis.Select(r => r.sprite == block ? "BLOCK" : r.sprite == beam ? "BEAM" : (string.IsNullOrEmpty(r.sprite.name) ? "proc" : r.sprite.name)).Distinct());
                    if (!s.sprites.Contains(names)) s.sprites += (s.sprites.Length > 0 ? " " : "") + names;
                }
                if ((newKind && shots < maxShots && Time.time - lastShot > 0.25f) || Time.time - lastShot > secs / 3f)
                {
                    lastShot = Time.time;
                    Shot($"{t.name}_{shots:00}");
                    shots++;
                }
                yield return null;
            }
            foreach (var kv in seen)
            {
                L($"[{t.name}] {kv.Key}: frames {kv.Value.frames} placeholder {kv.Value.placeholder} invisible {kv.Value.invisible} sprites [{kv.Value.sprites}]");
                if (kv.Value.placeholder > 0) { totalPlaceholder++; summary.Add($"{t.name} {kv.Key} PLACEHOLDER [{kv.Value.sprites}]"); }
                if (kv.Value.invisible > 2) { totalInvisible++; summary.Add($"{t.name} {kv.Key} INVISIBLE {kv.Value.invisible}f"); }
                if (strict) Check(kv.Value.placeholder == 0, $"{t.name} {kv.Key}: no plain rectangle / procedural band as the attack picture");
                if (strict) Check(kv.Value.invisible <= 2, $"{t.name} {kv.Key}: no invisible active hitbox ({kv.Value.invisible} frames)");
            }
            if (seen.Count == 0) L($"[{t.name}] no damaging attack seen in {secs:F0}s");
            KillSkyBosses();
            yield return new WaitForSeconds(0.5f);
        }
        L("== summary ==");
        foreach (var s in summary) L(s);
        L($"placeholder attacks {totalPlaceholder}, invisible attacks {totalInvisible}");
        PortraitRunView.Set(PortraitRunView.Side);
        SaveSystem.Restore(snapSave);
        CardInventory.ReloadFromPrefs(); CardMastery.ReloadFromPrefs();
        RunCheckpoint.Reload();
    }

    struct AtkVis { public string key; public List<SpriteRenderer> srs; }

    // 判定が有効で、プレイヤーに当たる攻撃と、その見た目
    IEnumerable<AtkVis> ActiveAttackVisuals()
    {
        foreach (var hb in FindObjectsByType<BossHitbox>(FindObjectsSortMode.None))
            if (hb != null && hb.IsActive && hb.damagesPlayer)
                yield return new AtkVis { key = "HB:" + hb.name.Replace("Hitbox_", "") + "@" + OwnerName(hb.transform), srs = hb.GetComponentsInChildren<SpriteRenderer>().ToList() };
        foreach (var th in FindObjectsByType<TrackedHazard>(FindObjectsSortMode.None))
        {
            var col = th != null ? th.GetComponent<BoxCollider2D>() : null;
            if (col != null && col.enabled && th.damages) yield return new AtkVis { key = "TH:" + th.name, srs = th.GetComponentsInChildren<SpriteRenderer>().ToList() };
        }
        foreach (var h in CaveHazard.Live)
            if (h != null && h.IsActive && h.Damaging) yield return new AtkVis { key = "CH:" + h.Kind + "/" + h.Look + "@" + h.Source, srs = h.GetComponentsInChildren<SpriteRenderer>().ToList() };
        foreach (var s in FindObjectsByType<SkyStrike>(FindObjectsSortMode.None))
        {
            var col = s != null ? s.GetComponent<Collider2D>() : null;
            if (col != null && col.enabled) yield return new AtkVis { key = "SS:" + s.name, srs = s.GetComponentsInChildren<SpriteRenderer>().ToList() };
        }
        foreach (var p in FindObjectsByType<BossProjectile>(FindObjectsSortMode.None))
            if (p != null && p.damage && p.isActiveAndEnabled) yield return new AtkVis { key = "BP:" + p.name, srs = p.GetComponentsInChildren<SpriteRenderer>().ToList() };
    }

    static string OwnerName(Transform t)
    {
        var b = t.GetComponentInParent<WildBossBase>();
        if (b != null) return b.GetType().Name;
        var mb = t.GetComponentInParent<IBossBattleDebug>() as MonoBehaviour;
        return mb != null ? mb.GetType().Name : "-";
    }
}
#endif
