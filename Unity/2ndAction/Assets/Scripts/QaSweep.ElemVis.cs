#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// 属性の視認性の改修(2026-10-30)の確認: -qaElemVis <dir> [-evPart vis,boss,multi,dummy,real,perf] [-evElems fire,ice,…] [-evVideo 1] [-elemVfx old]
//  vis   … 各属性 「属性なし / 入口(Lv9)/ 上位まで(系統を全部 Lv9)」を同じ並びの雑魚に当てて撮る(-evVideo 1 で 30fps の連番も)
//  boss  … 大きい/飛ぶ/黒い/明るいボスに5属性ぜんぶ(複数属性の見え方 J と K)
//  dummy … 100回の命中あたりの発動回数・追加ダメージ(入口 Lv1 / 入口 Lv9 / 系統 Lv9。倒れないダミー)
//  real  … 実戦(荒野街道 20km〜、補助で自動に戦う 90秒): 属性なし と 各属性の系統 Lv9 の 撃破数・属性のダメージの割合・属性で倒した数
//  perf  … 雑魚 40体 + 5属性 Lv9 で fps / 粒の数 / マークの数 / 使ったメモリ
public partial class QaSweep
{
    static ElementDevLoadout.Family EvFam(string key) => ElementDevLoadout.Families.First(f => f.key == key);
    static readonly Dictionary<string, string> EvJa = new Dictionary<string, string> { { "fire", "炎" }, { "ice", "氷" }, { "thunder", "雷" }, { "wind", "風" }, { "blood", "血" }, { "all", "5属性" }, { "none", "なし" } };
    readonly StringBuilder evTsv = new StringBuilder();
    readonly List<EnemyController> evMobs = new List<EnemyController>();
    int evBossKills, evBreaks; float evGoalSec; bool evScaled; // builds の測定(EvRow へ)
    int evMobHp = 160; // 数発で倒れる(撃破で起きる INFERNO / VAMPIRE も見える)

    IEnumerator ElemVisMode()
    {
        Application.targetFrameRate = 60;
        var parts = Arg("-evPart", "vis,boss,dummy,real,perf").Split(',');
        var elems = Arg("-evElems", "fire,ice,thunder,wind,blood").Split(',');
        bool video = Arg("-evVideo", "0") == "1";
        L($"[ev] mode={(ElementVfx.Legacy ? "OLD" : "NEW")} parts={string.Join(",", parts)}");
        aeOverlayY = 0.86f; // 画面の下(敵の頭上のマークと重ならない)
        evTsv.Append("part\telement\tcase\tsecs\thits\tprocs\tper100\tstatus\telemDamage\tdirect\tsharePct\tkills\telemKills\tchains\tfreezes\tshatters\ttornados\tinfernos\tlifesteal\tbleedHeal\tpeakParticles\tpeakAuras\tpeakMarked\tsfx\tsfxThinned\tdropped\tfpsAvg\tfpsMin\tmemMB\tforced\tfrozenSec\tchilledSec\ticeStagger\thealHp\thitHeal\ttornadoPulls\twindAirHits\tbossKills\tbreaks\tgoalSec\tatkShare\texpansion\n");
        if (video) { demoDir = System.IO.Path.Combine(outDir, "frames"); System.IO.Directory.CreateDirectory(demoDir); demoFrame = 0; }
        var vtsv = new StringBuilder("element\tcase\tfrom\tto\n");

        if (parts.Contains("vis"))
            foreach (var el in elems)
                foreach (var cs in new[] { "none", "entry", "family" })
                {
                    int f0 = demoFrame;
                    yield return EvVisCase(el, cs, video);
                    if (video) vtsv.Append($"{el}\t{cs}\t{f0}\t{demoFrame - 1}\n");
                }
        if (video) System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "video.tsv"), vtsv.ToString());
        if (parts.Contains("boss"))
            foreach (var key in Arg("-evBosses", "Wild/Cyclops,Wild/BlackKnight,Cave/CrystalGolem,Sky/Phoenix,Wild/Griffin").Split(','))
                yield return EvBossCase(key);
        if (parts.Contains("dummy"))
            foreach (var el in elems)
                foreach (var cs in new[] { "entry1", "entry9", "family9" })
                    yield return EvDummyCase(el, cs);
        if (parts.Contains("real"))
            foreach (var el in new[] { "none" }.Concat(elems))
                yield return EvRealCase(el);
        if (parts.Contains("perf")) yield return EvPerfCase();
        // 2026-10-30 属性20枚の再設計: ビルドの比較(-evBuilds fire,ice,thunder,wind,blood,normal,mixed / -evScen zako100,boss,rush,bossrush)
        if (parts.Contains("builds"))
            foreach (var b in Arg("-evBuilds", "fire,ice,thunder,wind,blood,normal,mixed").Split(','))
                foreach (var sc in Arg("-evScen", "zako100,boss,rush,bossrush").Split(','))
                    yield return EvBuildCase(b, sc);
        if (parts.Contains("cards"))
            foreach (var id in ElementDevLoadout.Families.SelectMany(f => f.family).Distinct().Where(x => Arg("-evCards", "") == "" || Arg("-evCards", "").Split(',').Contains(x)))
                foreach (int lv in new[] { 1, 9 })
                    yield return EvCardCase(id, lv);
        if (parts.Contains("rush")) { yield return EvRealCase("none", 90500f, "rush"); yield return EvRealCase("all", 90500f, "rush"); } // 90〜95km の雑魚の大群(属性なし/5属性 Lv9)
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "elem.tsv"), evTsv.ToString());
    }

    // ---------------------------------------------------------------- 共通
    IEnumerator EvBegin(string stage = "wasteland_road")
    {
        yield return BeginRun("swordsman", stage);
        SetGod(true);
        GameManager.BlockExpGain = true;
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
        var tm = TerrainManager.Instance;
        tm.enemySpawnChance = 0f; tm.pitChanceBase = 0f; tm.pitChanceMax = 0f;
        tm.DebugSetRouteBranch(false);
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
        yield return new WaitForSecondsRealtime(0.5f);
        tm.RequestFlatRun(12);
        WarpTo(1200f); // 穴なし・分かれ道なし・平地の予約で作られた所
        yield return new WaitForSecondsRealtime(1.2f);
        // 段差のない 12m を前で探して、そこへ立つ(雑魚と同じ高さで当てる)
        float px = pc.transform.position.x;
        for (float x = px + 2f; x < px + 160f; x += 0.5f)
        {
            float? h0 = tm.GetHeightAt(x); bool ok = h0.HasValue;
            for (float d = 0.5f; ok && d <= 12f; d += 0.5f) { float? h = tm.GetHeightAt(x + d); ok = h.HasValue && Mathf.Abs(h.Value - h0.Value) < 0.15f; }
            if (ok) { PlayerController.DebugRunOnlyScale = 0f; HaPlace(new Vector3(x, 0f, 0f)); L($"[ev] flat ground at {x - px:F1}m ahead"); break; }
        }
        yield return new WaitForSecondsRealtime(0.4f);
        PlayerController.DebugRunOnlyScale = 0f; // その場で(止まった画面で見比べる)
        yield return new WaitForSecondsRealtime(0.6f);
        ElementVfx.PeakParticles = 0; ElementAura.PeakAuras = 0; ElementAura.PeakMarked = 0;
    }
    IEnumerator EvEnd()
    {
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = true;
        PlayerController.DebugRunOnlyScale = 1f;
        foreach (var m in evMobs) if (m != null) Destroy(m.gameObject);
        evMobs.Clear();
        SetGod(false); GameManager.BlockExpGain = false;
        yield return EndRun();
        yield return ReloadHome();
    }
    void EvApply(string el, string cs)
    {
        if (el == "none" || cs == "none") return;
        if (el == "all") { ElementDevLoadout.ApplySpec(gm, "all9"); return; }
        string spec = cs == "entry" || cs == "entry9" ? el + "9e" : cs == "entry1" ? el + "1e" : el + "9";
        ElementDevLoadout.ApplySpec(gm, spec);
    }
    // 雑魚を前に並べる(倒れたら同じ所に補充)
    void EvKeepMobs(int n, float x0, float gap, int hp)
    {
        evMobs.RemoveAll(m => m == null || !ElementSystem.IsAlive(m));
        for (int i = evMobs.Count; i < n; i++)
        {
            var en = HaSpawn(x0 + gap * i + Random.Range(-0.2f, 0.2f));
            if (en == null) continue;
            HaEnemyHp.SetValue(en, hp);
            evMobs.Add(en);
        }
    }
    void EvRow(string part, string el, string cs, float secs, int kills, float fpsAvg = 0f, float fpsMin = 0f, ElementType? typeOverride = null)
    {
        var T = ElementTelemetry.Procs; var f = typeOverride ?? (el == "none" || el == "all" ? ElementType.None : EvFam(el).type); int i = (int)f;
        long total = ElementTelemetry.DirectDamage + ElementTelemetry.OtherDamage; for (int k = 0; k < ElementTelemetry.N; k++) total += ElementTelemetry.Damage[k];
        long ed = f == ElementType.None ? total - ElementTelemetry.DirectDamage - ElementTelemetry.OtherDamage : ElementTelemetry.Damage[i];
        long ek = f == ElementType.None ? ElementTelemetry.Kills.Sum() : ElementTelemetry.Kills[i];
        long pr = f == ElementType.None ? T.Sum() : T[i];
        long stc = f == ElementType.None ? ElementTelemetry.Status.Sum() : ElementTelemetry.Status[i];
        float per100 = ElementTelemetry.Hits > 0 ? pr * 100f / ElementTelemetry.Hits : 0f;
        float share = total > 0 ? ed * 100f / total : 0f;
        float mem = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f);
        evTsv.Append($"{part}\t{el}\t{cs}\t{secs:F0}\t{ElementTelemetry.Hits}\t{pr}\t{per100:F1}\t{stc}\t{ed}\t{ElementTelemetry.DirectDamage}\t{share:F1}\t{kills}\t{ek}\t{ElementTelemetry.Chains}\t{ElementTelemetry.Freezes}\t{ElementTelemetry.Shatters}\t{ElementTelemetry.Tornados}\t{ElementTelemetry.Infernos}\t{ElementTelemetry.LifestealHeals}\t{ElementTelemetry.BleedHealHp}\t{ElementVfx.PeakParticles}\t{ElementAura.PeakAuras}\t{ElementAura.PeakMarked}\t{ElementVfx.SfxPlayed}\t{ElementVfx.SfxThinned}\t{ElementVfx.Dropped}\t{fpsAvg:F1}\t{fpsMin:F1}\t{mem:F0}\t{ElementSystem.ForcedStrikes}\t{ElementTelemetry.FrozenSeconds:F1}\t{ElementTelemetry.ChilledSeconds:F1}\t{ElementTelemetry.IceStagger:F1}\t{ElementTelemetry.HealHp}\t{ElementTelemetry.HitHealHp}\t{CardProcs.TornadoPulls}\t{ElementSystem.WindAirBonusHits}\t{evBossKills}\t{evBreaks}\t{evGoalSec:F1}\t{ElementSystem.ElementAttackShareNow:F2}\t{ElementSystem.ExpansionReport()}\n");
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "elem.tsv"), evTsv.ToString());
        L($"[ev] {part} {el} {cs}: hits {ElementTelemetry.Hits} procs {pr} ({per100:F1}/100) status {stc} dmg {ed} ({share:F1}%) kills {kills} elemKills {ek} | particles peak {ElementVfx.PeakParticles} auras {ElementAura.PeakAuras} marks {ElementAura.PeakMarked} sfx {ElementVfx.SfxPlayed}/{ElementVfx.SfxThinned} dropped {ElementVfx.Dropped}{(fpsAvg > 0 ? $" fps {fpsAvg:F0}/{fpsMin:F0}" : "")} | {ElementSystem.ExpansionReport()}");
    }

    // ---------------------------------------------------------------- vis(撮影)
    IEnumerator EvVisCase(string el, string cs, bool video)
    {
        yield return EvBegin();
        EvApply(el, cs);
        ElementTelemetry.Reset();
        string lbl = cs == "none" ? "属性なし" : cs == "entry" ? $"入口のカード Lv9({string.Join("/", EvFam(el).entry.Select(id => CardDatabase.FindBaseById(id).cardName))})" : "系統を全部 Lv9(上位カードまで)";
        aeOverlay = $"{(ElementVfx.Legacy ? "旧" : "新")}  {EvJa[el]}  {lbl}";
        if (video) Time.captureFramerate = 30;
        float t = 0f, nextAtk = 0f; int shot = 0; float secs = float.Parse(Arg("-evSecs", "4.5"), System.Globalization.CultureInfo.InvariantCulture);
        while (t < secs)
        {
            EvKeepMobs(4, 3.2f, 1.5f, evMobHp);
            if (t >= nextAtk) { StartCoroutine(HaFlick(PlayerController.FlickDirection.Forward)); nextAtk = t + 0.33f; }
            if (shot < 3 && t >= 1.2f + 1.2f * shot) { Shot($"ev_{el}_{cs}_{shot}"); shot++; }
            if (video) { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(demoDir, $"f{demoFrame:00000}.png")); demoFrame++; }
            else yield return null;
            t += video ? 1f / 30f : Time.deltaTime;
        }
        // 状態が切れる所(エステルの依頼): 倒れない2体に1.5秒当てて、攻撃を止めて6.5秒(炎上は最大 5.25秒+重ね、出血は4秒。自然に消えるまで)
        if (cs != "none")
        {
            foreach (var m in evMobs) if (m != null) Destroy(m.gameObject);
            evMobs.Clear();
            EvKeepMobs(2, 3.2f, 1.5f, 1000000);
            aeOverlay = $"{(ElementVfx.Legacy ? "旧" : "新")}  {EvJa[el]}  {lbl}  → 攻撃を止めて状態が切れるまで";
            float t2 = 0f; nextAtk = 0f;
            while (t2 < 8f)
            {
                if (t2 < 1.5f && t2 >= nextAtk) { StartCoroutine(HaFlick(PlayerController.FlickDirection.Forward)); nextAtk = t2 + 0.33f; }
                if (t2 > 7.6f && shot < 4) { Shot($"ev_{el}_{cs}_end"); shot = 4; }
                if (video) { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(demoDir, $"f{demoFrame:00000}.png")); demoFrame++; }
                else yield return null;
                t2 += video ? 1f / 30f : Time.deltaTime;
            }
            var stEnd = evMobs.Where(m => m != null).Select(m => m.GetComponent<ElementStatus>()).Where(x => x != null).ToList();
            bool anyLeft = stEnd.Any(x => x.Burning || x.Chilled || x.Frozen || x.Bleeding || x.Charged);
            Check(!anyLeft, $"vis {el} {cs}: the states wear off after the attacks stop");
        }
        Time.captureFramerate = 0; aeOverlay = null;
        EvRow("vis", el, cs, secs, 0);
        Check(cs == "none" || ElementTelemetry.Procs[(int)EvFam(el).type] > 0, $"vis {el} {cs}: the element triggered ({ElementTelemetry.Procs[(int)EvFam(el).type]})");
        yield return EvEnd();
    }

    // ---------------------------------------------------------------- boss(複数属性・大きさ・色)
    IEnumerator EvBossCase(string key)
    {
        yield return EvBegin(key.StartsWith("Sky") ? "sky_corridor" : key.StartsWith("Cave") ? "natural_cave" : "wasteland_road");
        evScaled = false;
        if (!BaParse(key, out int fam, out int kind)) { L($"[ev] unknown boss {key}"); yield return EvEnd(); yield break; }
        TerrainManager.Instance.DebugSetRouteBranch(false);
        EvApply("all", "family");
        PlayerController.DebugRunOnlyScale = 1f;
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true); // ボスは補助で戦う(止まって戦う形)
        Bm.DebugSpawnBossForTest(fam, kind);
        BaBoss boss = null; float w = 0f;
        while (w < 15f) { yield return null; w += Time.unscaledDeltaTime; boss = BaFind(); if (boss.Valid && w > 1f) { if (!evScaled) { boss.ScaleHp(60f); evScaled = true; } if (boss.W == null || !boss.W.IsEntering) break; } } // 倍率は1回だけ
        if (boss == null || !boss.Valid) { Check(false, $"boss {key}: spawned"); yield return EvEnd(); yield break; }
        ElementTelemetry.Reset();
        aeOverlay = $"{(ElementVfx.Legacy ? "旧" : "新")}  ボス {key}  5属性の系統 Lv9(複数属性)";
        float t = 0f; int shot = 0, maxStates = 0;
        bool video = Arg("-evVideo", "0") == "1"; int bf0 = demoFrame;
        if (video) Time.captureFramerate = 30;
        while (t < 12f && boss.Valid && !boss.IsDead)
        {
            if (video) { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(demoDir, $"f{demoFrame:00000}.png")); demoFrame++; }
            var hb = boss.Live; float px = pc.transform.position.x;
            var f = hb.center.x >= px ? PlayerController.FlickDirection.Forward : PlayerController.FlickDirection.Backward;
            if (hb.min.y > pc.transform.position.y + 1.6f) f = PlayerController.FlickDirection.Up;
            var st = boss.C.GetComponent<ElementStatus>();
            if (st != null)
            {
                int states = (st.Burning ? 1 : 0) + (st.Chilled || st.Frozen ? 1 : 0) + (st.Charged ? 1 : 0) + (st.Bleeding ? 1 : 0) + (st.Windswept ? 1 : 0);
                maxStates = Mathf.Max(maxStates, states);
                if (shot < 3 && states >= 2 && t > 2f + 2.5f * shot) { Shot($"ev_boss_{key.Replace('/', '_')}_{shot}"); shot++; }
            }
            if (!video) yield return null;
            t += video ? 1f / 30f : Time.deltaTime;
        }
        if (video) { Time.captureFramerate = 0; System.IO.File.AppendAllText(System.IO.Path.Combine(outDir, "video_boss.tsv"), $"{key}\t{bf0}\t{demoFrame - 1}\n"); }
        if (shot == 0) Shot($"ev_boss_{key.Replace('/', '_')}_x");
        aeOverlay = null;
        EvRow("boss", "all", key, t, 0);
        L($"[ev] boss {key}: states at once max {maxStates}, shots {shot}");
        Check(ElementTelemetry.Procs.Sum() > 0, $"boss {key}: elements triggered on the boss ({ElementTelemetry.Procs.Sum()})");
        if (boss.Valid && !boss.IsDead) boss.Kill();
        yield return BfWaitEncounterEnd();
        yield return EvEnd();
    }

    // ---------------------------------------------------------------- dummy(100回の命中あたり)
    IEnumerator EvDummyCase(string el, string cs)
    {
        yield return EvBegin();
        EvApply(el, cs);
        EvKeepMobs(3, 2.2f, 1.1f, 1000000);
        ElementTelemetry.Reset(); ElementVfx.SfxPlayed = 0; ElementVfx.SfxThinned = 0;
        float t = 0f, nextAtk = 0f;
        while (ElementTelemetry.Hits < 100 && t < 60f)
        {
            if (t >= nextAtk) { StartCoroutine(HaFlick(PlayerController.FlickDirection.Forward)); nextAtk = t + 0.33f; }
            yield return null; t += Time.deltaTime;
        }
        long hits = ElementTelemetry.Hits;
        yield return new WaitForSeconds(4f); // 継続ダメージ(炎上/出血)が終わるまで
        ElementTelemetry.Hits = hits;
        EvRow("dummy", el, cs, t, 0);
        yield return EvEnd();
    }

    // ---------------------------------------------------------------- real(実戦)
    IEnumerator EvRealCase(string el, float warp = 22000f, string tag = "family9")
    {
        yield return BeginRun("swordsman", "wasteland_road");
        SetGod(true);
        GameManager.BlockExpGain = true; // カードの選択で構成を変えない
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = true; // 前のケースで止めた出現を戻す(シーンをまたいで残る)
        WarpTo(warp); // 22km(21km の関門とボーナスの後)/ rush = 90.5km(雑魚の大群)
        yield return new WaitForSecondsRealtime(2f);
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);
        EvApply(el, "family");
        ElementTelemetry.Reset(); ElementVfx.PeakParticles = 0; ElementAura.PeakAuras = 0; ElementAura.PeakMarked = 0;
        long k0 = ElementTelemetry.EnemyDeaths;
        float secs = float.Parse(Arg("-evRealSecs", "90"), System.Globalization.CultureInfo.InvariantCulture), t = 0f;
        var fps = new List<float>();
        float nextAtk = 0f;
        while (t < secs && gm.HasStarted && !gm.IsGameOver)
        {
            // 補助に加えて前へ攻撃を出し続ける(攻撃し続けるプレイヤー。属性の判定の機会を揃える)
            if (t >= nextAtk && !gm.IsRewardSequenceRunning) { StartCoroutine(HaFlick(PlayerController.FlickDirection.Forward)); nextAtk = t + 0.35f; }
            yield return null; t += Time.deltaTime; fps.Add(1f / Mathf.Max(1e-4f, Time.unscaledDeltaTime));
        }
        EvRow(tag == "rush" ? "rush" : "real", el, tag, t, (int)(ElementTelemetry.EnemyDeaths - k0), fps.Count > 0 ? fps.Average() : 0f, fps.Count > 0 ? fps.OrderBy(x => x).ElementAt(fps.Count / 100) : 0f);
        SetGod(false); GameManager.BlockExpGain = false;
        yield return EndRun();
        yield return ReloadHome();
    }

    // ---------------------------------------------------------------- builds(2026-10-30): 7つのビルド × 4つの場面
    //  属性ビルド = その属性の系統を全部 Lv9 + ATTACK UP Lv9 + ATTACK SPEED UP Lv9(どのビルドにも入る普通の強化)
    //  normal = 属性なしの通常攻撃特化(ATTACK UP / ATTACK SPEED UP / FIRST STRIKE / COMBO PLUS / GROUND FIGHTER / DOUBLE ATTACK を Lv9)
    //  mixed  = 炎2枚(FLAME BLADE / BURNING SOUL)+ 雷2枚(THUNDER STRIKE / CHAIN LIGHTNING)を Lv9 + ATTACK UP Lv9 + ATTACK SPEED UP Lv9
    void EvApplyBuild(string b)
    {
        void C(string id, int lv) { var c = CardDatabase.FindBaseById(id); if (c != null) gm.ApplyCardEffectsStacked(c, lv); }
        if (b == "normal") { foreach (var id in new[] { "attack_up", "attack_speed_up", "first_strike", "combo_plus", "ground_fighter", "double_attack" }) C(id, 9); return; }
        if (b == "mixed") { foreach (var id in new[] { "flame_blade", "burning_soul", "thunder_strike", "chain_lightning" }) C(id, 9); }
        // 2026-10-31 カード拡張: 炎×風(FIRE WHIRL で風刃が炎を運ぶ)/ 氷×雷(CONDUCTOR で冷えた敵への落雷)の 7 枚
        else if (b == "firewind") { foreach (var id in new[] { "flame_blade", "burning_soul", "fire_whirl", "combustion", "wind_cutter", "gale", "tornado" }) C(id, 9); }
        else if (b == "icethunder") { foreach (var id in new[] { "frost_edge", "ice_prison", "shatter_strike", "thunder_strike", "chain_lightning", "conductor", "judgment_bolt" }) C(id, 9); }
        else if (b == "bloodlow") ElementDevLoadout.ApplySpec(gm, "blood9");
        else if (b != "none") ElementDevLoadout.ApplySpec(gm, b + "9");
        C("attack_up", 9); C("attack_speed_up", 9); // 2026-10-30: 普通の強化はどのビルドにも Lv9(実際のランの終盤に近い攻撃力)
    }
    IEnumerator EvBuildCase(string b, string sc)
    {
        evBossKills = 0; evBreaks = 0; evGoalSec = 0f; evScaled = false;
        // 2026-10-30: 雑魚100体とボス1体は闘技場で(平らな床・0km/h・同じ条件。通常のマップの地形でぶれていた)。90〜99km は本物のマップのまま
        if (sc == "zako100")
        {
            yield return EvArenaBegin(false);
            EvApplyBuild(b);
            ElementTelemetry.Reset(); ElementVfx.PeakParticles = 0;
            float t = 0f, nextAtk = 0f;
            while (ElementTelemetry.EnemyDeaths < 100 && t < 180f)
            {
                EvKeepMobs(5, 3.0f, 1.0f, 900); EvPinHp(b);
                if (t >= nextAtk) { StartCoroutine(HaFlick(PlayerController.FlickDirection.Forward)); nextAtk = t + 0.33f; }
                yield return null; t += Time.deltaTime;
            }
            evGoalSec = t;
            EvRow("build_" + sc, b, sc, t, (int)ElementTelemetry.EnemyDeaths, 0f, 0f, BuildType(b));
            yield return EvArenaEnd();
            yield break;
        }
        if (sc == "boss")
        {
            yield return EvArenaBegin(true);
            EvApplyBuild(b);
            BaBoss boss = null; float w = 0f;
            while (w < 15f) { yield return null; w += Time.unscaledDeltaTime; boss = BaFind(); if (boss.Valid && w > 1f) { if (!evScaled) { boss.ScaleHp(8f); evScaled = true; } if (boss.W == null || !boss.W.IsEntering) break; } } // 倍率は1回だけ(毎フレーム掛けると桁あふれで倒れていた)
            ElementTelemetry.Reset();
            float t = 0f;
            while (t < 150f && boss != null && boss.Valid && !boss.IsDead) { EvPinHp(b); yield return null; t += Time.deltaTime; }
            evGoalSec = t; evBreaks = boss != null && boss.W != null ? boss.W.BreakCount : 0; evBossKills = boss != null && boss.IsDead ? 1 : 0;
            EvRow("build_" + sc, b, sc, t, 0, 0f, 0f, BuildType(b));
            if (boss != null && boss.Valid && !boss.IsDead) boss.Kill();
            yield return new WaitForSecondsRealtime(1f);
            yield return EvArenaEnd();
            yield break;
        }
        // rush = 90.5km から90秒(雑魚の大群)/ bossrush = 94.9km から150秒(95km の3連戦)
        yield return BeginRun("swordsman", "wasteland_road");
        SetGod(true);
        GameManager.BlockExpGain = true;
        if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = true;
        WarpTo(sc == "rush" ? 90500f : 94900f);
        yield return new WaitForSecondsRealtime(2f);
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(true);
        EvApplyBuild(b);
        ElementTelemetry.Reset();
        int bk0 = gm.BossKillCount;
        float secs = sc == "rush" ? 90f : 150f, tt = 0f, na = 0f;
        while (tt < secs && gm.HasStarted && !gm.IsGameOver)
        {
            if (tt >= na && !gm.IsRewardSequenceRunning) { StartCoroutine(HaFlick(PlayerController.FlickDirection.Forward)); na = tt + 0.35f; }
            EvPinHp(b);
            yield return null; tt += Time.deltaTime;
        }
        evBossKills = gm.BossKillCount - bk0; evGoalSec = tt;
        EvRow("build_" + sc, b, sc, tt, (int)ElementTelemetry.EnemyDeaths, 0f, 0f, BuildType(b));
        SetGod(false); GameManager.BlockExpGain = false;
        yield return EndRun();
        yield return ReloadHome();
    }
    // 闘技場(ホームの「闘技場」と同じ入口)。黒剣士・0km/h・無敵・距離 50km の敵の強さ。ボスは サイクロプス1体 + 操作アシスト常時
    IEnumerator EvArenaBegin(bool boss)
    {
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        ArenaConfigReset();
        var c = ArenaMode.Config;
        c.character = "swordsman"; c.speedMode = 0; c.kmh = 0f; c.distance = 50000f; c.invincible = true; c.devAllEnemies = true;
        c.assistMode = boss ? 2 : 0;
        c.enemies.Clear();
        if (boss) c.enemies.Add(new ArenaEnemyEntry { kind = ArenaEnemyKind.WildBoss, bossKind = (int)WildBossKind.Cyclops, count = 1, ahead = 8f });
        yield return ArenaLaunch("ev " + (boss ? "boss" : "zako"));
        evMobs.Clear();
        ElementVfx.PeakParticles = 0; ElementAura.PeakAuras = 0; ElementAura.PeakMarked = 0;
    }
    IEnumerator EvArenaEnd()
    {
        foreach (var m in evMobs) if (m != null) Destroy(m.gameObject);
        evMobs.Clear();
        var old = gm;
        if (Arena != null) Arena.ExitHome();
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance == old || GameManager.Instance.HasStarted || ArenaMode.Active) && w < 20f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        gm = GameManager.Instance; pc = PlayerController.Instance;
    }
    // bloodlow = HP をハート25%に保つ(無敵のまま。回復しても毎フレーム戻す)。BLOOD RUSH / 出血の低HPボーナスの確認
    void EvPinHp(string b) { if (b == "bloodlow" && gm != null) typeof(GameManager).GetProperty("Lives").SetValue(gm, Mathf.Max(1, Mathf.RoundToInt(gm.maxLives * 0.25f))); } // DebugSetLives は Editor だけ
    static ElementType BuildType(string b) => b == "bloodlow" ? ElementType.Blood : b.StartsWith("fire") ? ElementType.Fire : b.StartsWith("ice") ? ElementType.Ice : b.StartsWith("thunder") ? ElementType.Thunder : b.StartsWith("wind") ? ElementType.Wind : b == "blood" ? ElementType.Blood : ElementType.None;

    // ---------------------------------------------------------------- cards(20枚を1枚ずつ: Lv1 / Lv9、100回の命中。雑魚は HP 400 で時々倒れる = 撃破で起きる効果も入る)
    IEnumerator EvCardCase(string id, int lv)
    {
        yield return EvBegin();
        var c = CardDatabase.FindBaseById(id);
        gm.ApplyCardEffectsStacked(c, lv);
        ElementTelemetry.Reset(); ElementVfx.SfxPlayed = 0; ElementVfx.SfxThinned = 0;
        float t = 0f, nextAtk = 0f;
        while (ElementTelemetry.Hits < 100 && t < 60f)
        {
            EvKeepMobs(3, 3.0f, 1.2f, 400);
            if (t >= nextAtk) { StartCoroutine(HaFlick(PlayerController.FlickDirection.Forward)); nextAtk = t + 0.33f; }
            yield return null; t += Time.deltaTime;
        }
        long hits = ElementTelemetry.Hits;
        yield return new WaitForSeconds(4f);
        ElementTelemetry.Hits = hits;
        EvRow("card", id, "lv" + lv, t, (int)ElementTelemetry.EnemyDeaths, 0f, 0f, c.element);
        yield return EvEnd();
    }

    // ---------------------------------------------------------------- perf(雑魚 40体 + 5属性)
    IEnumerator EvPerfCase()
    {
        yield return EvBegin();
        EvApply("all", "family");
        ElementTelemetry.Reset(); ElementVfx.SfxPlayed = 0; ElementVfx.SfxThinned = 0; ElementVfx.Dropped = 0;
        long mem0 = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
        var fps = new List<float>();
        float t = 0f, nextAtk = 0f; int shot = 0;
        aeOverlay = $"{(ElementVfx.Legacy ? "旧" : "新")}  雑魚40体 + 5属性の系統 Lv9";
        while (t < 20f)
        {
            EvKeepMobs(40, 1.8f, 0.45f, 400);
            if (t >= nextAtk) { StartCoroutine(HaFlick(PlayerController.FlickDirection.Forward)); nextAtk = t + 0.3f; }
            if (shot < 2 && t > 6f + 6f * shot) { Shot($"ev_perf_{shot}"); shot++; }
            yield return null; t += Time.deltaTime; if (t > 2f) fps.Add(1f / Mathf.Max(1e-4f, Time.unscaledDeltaTime));
        }
        aeOverlay = null;
        float avg = fps.Average(), p1 = fps.OrderBy(x => x).ElementAt(fps.Count / 100);
        long mem1 = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
        EvRow("perf", "all", "40mobs", t, (int)ElementTelemetry.EnemyDeaths, avg, p1);
        L($"[ev] perf: fps avg {avg:F1} 1%low {p1:F1} particles peak {ElementVfx.PeakParticles}/{ElementVfx.MaxParticles} auras {ElementAura.PeakAuras} marks {ElementAura.PeakMarked} dropped {ElementVfx.Dropped} sfx {ElementVfx.SfxPlayed} thinned {ElementVfx.SfxThinned} mem {(mem1 - mem0) / 1048576f:+0.0;-0.0}MB");
        Check(ElementVfx.PeakParticles <= ElementVfx.MaxParticles && ElementAura.PeakMarked <= ElementAura.MaxMarkedPerFrame, "perf: particles and marks stay under their caps");
        yield return EvEnd();
    }
}
#endif
