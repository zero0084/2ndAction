#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// デッキ 3 段階の難易度の確認(2026-11-05、マスター依頼 ②): -qaTier <dir>
//   [-tiChar swordsman] [-tiStage wasteland_road] [-tiTier A|B|C] [-tiBuild attack|defense|heal|element(C の構成)]
//   [-tiMode idle|human] [-tiStartKm 0] [-tiEndKm 100] [-tiTimeScale 2(時間を速める。物理/オートは同じ)] [-tiRuns 1] [-tiSeed n]
//   [-tiSpeed 100(設定「最高速度」%)] [-tiSpeedScale 1(約900km/h の確認 = 9)]
//  デッキ: A = 今の初期 12 枚(キャラ共通・解放順)すべて Lv1 / B = Lv3〜5 中心の 12 枚(偏らない構成)/ C = Lv7〜9 の 12 枚(構成ごと)。
//   デッキはテスト用データ(-memSave)に作り、ランの中のカードはレベルアップの 3 択から選ぶ(本物の流れ。選び方は乱数 = 普通の人の選び方の幅)。
//  オートは本物の設定のまま。HP は満タンへ戻さない(倒れたら終わり)。
//  idle = オートのまま何も操作しない / human = オート + 予兆に反応する自動の操作(判断型の道中イベント・空中雑魚の急降下/横切り・鎧の敵の打ち上げ)
//  結果: tier.tsv(1行 = 1回の走り)。死亡の理由は 区分(イベント/雑魚/空中/ボス/地形/穴/その他)と、最後の 30 秒の被弾の内訳
public partial class QaSweep
{
    static readonly Dictionary<string, (string id, int lv)[]> TierDecks = new Dictionary<string, (string, int)[]>
    {
        // B: 通常プレイを何度かして育てた中程度(Lv3〜5 中心。極端な最適化はしない)
        { "B", new[] { ("attack_up", 5), ("attack_speed_up", 4), ("heart_up", 4), ("shield", 3), ("air_attack_up", 4), ("attack_range_up", 3),
                       ("jump_count_up", 3), ("mob_killer", 4), ("vampire", 3), ("first_strike", 3), ("speed_up", 3), ("exp_up", 3) } },
        // C: Lv7〜9 + ある程度意図して組んだ 12 枚(構成ごと)
        { "C:attack", new[] { ("attack_up", 9), ("attack_speed_up", 8), ("attack_range_up", 7), ("air_attack_up", 8), ("mob_killer", 8), ("double_attack", 7),
                       ("first_strike", 7), ("combo_plus", 7), ("heart_up", 7), ("shield", 7), ("jump_count_up", 7), ("vampire", 7) } },
        { "C:defense", new[] { ("heart_up", 9), ("shield", 9), ("iron_will", 8), ("perfect_guard", 7), ("attack_up", 8), ("attack_speed_up", 7),
                       ("second_wind", 7), ("vampire", 7), ("air_attack_up", 7), ("jump_count_up", 7), ("attack_range_up", 7), ("last_chance", 7) } },
        { "C:heal", new[] { ("vampire", 9), ("blood_blade", 8), ("second_wind", 8), ("blood_rush", 7), ("heart_up", 8), ("attack_up", 8),
                       ("attack_speed_up", 7), ("shield", 7), ("air_attack_up", 7), ("jump_count_up", 7), ("mob_killer", 7), ("attack_range_up", 7) } },
        { "C:element", new[] { ("flame_blade", 9), ("frost_edge", 8), ("chain_lightning", 8), ("flame_counter", 7), ("blood_rush", 7), ("attack_up", 8),
                       ("attack_speed_up", 7), ("heart_up", 7), ("shield", 7), ("vampire", 7), ("air_attack_up", 7), ("jump_count_up", 7) } },
    };

    class TiHit { public float t, km; public string cat, src; public int hearts; }
    readonly List<TiHit> tiHits = new List<TiHit>();
    int tiShield, tiHealEvents, tiHealHp;

    static string TiCategory(string reason)
    {
        string k = reason.StartsWith("HPZero:") ? reason.Substring(7) : reason;
        if (k.StartsWith("DeathY")) return "pit";
        if (k.StartsWith("TerrainWall") || k.StartsWith("Obstacle") || k.StartsWith("CeilingSpike")) return "terrain";
        if (k.Contains("LaneSweep")) return "event";
        if (k.StartsWith("Enemy:Air_") || k.Contains("AirStrikeHitbox") || k.Contains("AirSwoopHitbox")) return "air";
        bool bossPhase = BossManager.Instance != null && BossManager.Instance.IsBossPhase;
        if (k.StartsWith("Boss") || k.StartsWith("WildBoss") || k.StartsWith("CaveBoss") || k.StartsWith("Dragon") || k.StartsWith("SkyStrike") || k.StartsWith("Majin") || k.StartsWith("Mech")) return "boss";
        if (k.StartsWith("Reaper")) return "reaper";
        bool ev = DecisionEvents.Running != RoadEventKind.None || RoadEvents.Active != RoadEventKind.None;
        if (k.StartsWith("Enemy") || k.StartsWith("Fireball")) return bossPhase && k.StartsWith("Fireball") ? "boss" : ev ? "event" : "zako";
        return "other";
    }

    List<string> TierDeckKeys(string tier, string build)
    {
        var keys = new List<string>();
        // A: 新規のプレイヤーの初期デッキ(解放の距離 0 のカードを並び順に 12 枚。テスト用データは走行距離があり vampire 等が解放済みのため、名前で固定)
        if (tier == "A") { keys.AddRange(new[] { "speed_up", "speed_down", "attack_up", "jump_power_up", "jump_count_up", "heart_up", "attack_range_up", "attack_speed_up", "air_attack_up", "shield", "exp_up", "more_enemies" }); foreach (var id in keys) CardInventory.AddCard(id, 1, 1); return keys; }
        string k = tier == "B" ? "B" : "C:" + build;
        if (!TierDecks.TryGetValue(k, out var cards)) cards = TierDecks["C:attack"];
        foreach (var (id, lv) in cards)
        {
            if (CardDatabase.FindBaseById(id) == null) { L($"[tier] missing card {id}"); continue; }
            string key = lv > 1 ? CardDataMigration.LegacyToKey(id, lv) : id;
            CardInventory.AddCard(key, lv, 1);
            keys.Add(key);
        }
        return keys;
    }

    IEnumerator TierMode()
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string stage = Arg("-tiStage", "wasteland_road"), ch = Arg("-tiChar", "swordsman"), tier = Arg("-tiTier", "A"), build = Arg("-tiBuild", "attack"), modeArg = Arg("-tiMode", "idle");
        float startKm = float.Parse(Arg("-tiStartKm", "4"), inv), endKm = float.Parse(Arg("-tiEndKm", "100"), inv), ts = float.Parse(Arg("-tiTimeScale", "2"), inv);
        int runs = int.Parse(Arg("-tiRuns", "1"));
        var rng = new System.Random(int.Parse(Arg("-tiSeed", System.Environment.TickCount.ToString())));
        GameSettings.SetMaxSpeedPercent(int.Parse(Arg("-tiSpeed", "100")));
        float speedScale = float.Parse(Arg("-tiSpeedScale", "1"), inv);
        // -tiVid 52,60,68 = その km を越えた時から -tiVidLen 秒(既定 6)、15fps の連番を撮る(画面ありの時だけ。エステルに見せる用)
        var vidKms = Arg("-tiVid", "").Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries).Select(x => float.Parse(x, inv)).ToList();
        float vidLen = float.Parse(Arg("-tiVidLen", "6"), inv);
        Application.targetFrameRate = Mathf.RoundToInt(60f * Mathf.Max(1f, ts)); QualitySettings.vSyncCount = 0; // 速めても 1 フレームの時間は 60fps と同じくらいに
        string path = System.IO.Path.Combine(outDir, "tier.tsv");
        if (!System.IO.File.Exists(path))
            System.IO.File.WriteAllText(path, "char\tstage\ttier\tbuild\tmode\tspeed\tstart_km\tend_km\tdied\tdeath_cat\tdeath_src\tlast30s\tsecs\tlv\tmaxHp\thp_min_pct\thits\thearts\tby_cat\tshield_blocks\theals\theal_hp\tzako_kills\tzako_kill_s\tbosses\tboss_s\tboss_wins\tdecision_ev\tair_seen\tmile\tdeck\trun_cards\tby10km\tauto_on_pct\tfps\tgame_fps\n");
        GameManager.QaDamageEvent = OnTiDamage;
        GameManager.QaShieldEvent = r => tiShield++;
        for (int run = 0; run < runs; run++)
        {
            // デッキ(ランの前に。BeginRun は今のデッキでランを始める)
            gm = GameManager.Instance;
            float w0 = 0f; while ((gm == null || gm.HasStarted) && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; gm = GameManager.Instance; }
            var deck = TierDeckKeys(tier, build);
            gm.SetDeck(deck);
            L($"[tier] run {run} char={ch} stage={stage} tier={tier} build={build} mode={modeArg} deck={string.Join(",", gm.DeckCards)}");
            yield return BeginRun(ch, stage);
            stopKeepAlive = true; yield return null;
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
            var a = HighSpeedAssist.Instance;
            // オートは 0km/h から全部の補助(本物の既定は 100km/h から = 序盤は人が操作する前提。完全放置の測定なので最初から任せる。-qaDiff と同じ)
            if (a != null) { a.SetEnabled(true); a.SetAutoAttack(true); a.SetAutoAvoid(true); a.SetAutoInBoss(true); a.engageKmh = 0f; a.releaseKmh = -1f; a.fullAssistKmh = 1f; }
            if (startKm > 0f)
            {
                // 0〜startKm は人が操作する区間(オートは約 100km/h から)。そこまでのカードはデッキから乱数で(1km あたり約 2 回のレベルアップ、デッキのカードの Lv ぶん)
                int picks = Mathf.RoundToInt(startKm * 2f);
                for (int i = 0; i < picks; i++)
                {
                    string key = deck[rng.Next(deck.Count)];
                    var v = CardVariant.Parse(key); var c = CardDatabase.FindBaseById(CardMastery.BaseIdOf(key));
                    if (c != null) { int add = Mathf.Min(v != null ? v.level : 1, GameManager.MaxRunCardLevel - gm.GetRunCardLevel(c.cardId)); if (add > 0) gm.DebugApplyRunCard(c, add); }
                }
                WarpTo(startKm * 1000f); yield return new WaitForSeconds(1f);
                typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { gm.MaxLives });
            }
            PlayerController.DebugSpeedScale = speedScale;
            TimeControl.SetDebugTimeScale(ts);
            tiHits.Clear(); tiShield = 0; tiHealEvents = 0; tiHealHp = 0;
            int ev0 = DecisionEvents.Started;
            var seen = new HashSet<EnemyController>(); var firstSeen = new Dictionary<EnemyController, float>(); var killS = new List<float>(); int airSeen = 0;
            System.Action<EnemyController, PlayerAttackKind, bool, bool> onHit = (e, k, air, killed) => { if (killed && e != null && e.Definition != null && e.Definition.hpMultiplier < 5f && firstSeen.TryGetValue(e, out float f0)) killS.Add(Time.time - f0); };
            EnemyController.LocalHit += onHit;
            float t = 0f, sample = 0f, hpMin = 1f, runSecs50 = 0f; int lastLives = gm.Lives; bool realAssist = false; float hpMin95 = 1f, t95 = -1f, t99 = -1f; float vidUntil = -1f, vidNext = 0f; int vidN = 0; string vidTag = ""; int autoFrames = 0, autoOn = 0, frames = 0; float realT = 0f;
            int bosses = 0, bossWins = 0; float bossStart = -1f; var bossSecs = new List<float>(); bool wasBoss = false;
            while (!gm.IsGameOver && gm.MaxDistance < endKm * 1000f && t < 7200f)
            {
                yield return null; t += Time.deltaTime; sample -= Time.unscaledDeltaTime; frames++; realT += Time.unscaledDeltaTime;
                if (gm.IsRewardSequenceWaitingForSelection && !arPicking) StartCoroutine(TiPick(rng));
                if (vidKms.Count > 0 && gm.MaxDistance >= vidKms[0] * 1000f && vidUntil < 0f) { vidUntil = Time.unscaledTime + vidLen; vidTag = $"vid_{tier}_{vidKms[0]:F0}km"; vidKms.RemoveAt(0); vidN = 0; }
                if (vidUntil > 0f)
                {
                    if (Time.unscaledTime > vidUntil) vidUntil = -1f;
                    else if (Time.unscaledTime >= vidNext)
                    {
                        vidNext = Time.unscaledTime + 1f / 15f;
                        string vd = System.IO.Path.Combine(outDir, vidTag);
                        System.IO.Directory.CreateDirectory(vd);
                        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(vd, $"{vidN++:000}.png"));
                    }
                }
                if (FirstRunGuide.Open) FirstRunGuideDebug.Answer(FirstRunGuide.Showing == FirstRunGuide.Kind.DoorPrompt ? 1 : 0); // 初回の案内(時間が止まる)は閉じる
                if (modeArg == "human") HumanResponder(a);
                // 4km からは本物のオートの設定(開始の速さ 100km/h、最高速度がそれより低い時はその 97%)。それまでは人が操作する区間なので 0km/h から任せる
                if (!realAssist && gm.MaxDistance >= Mathf.Max(4000f, startKm * 1000f) && a != null) { realAssist = true; a.ReloadPrefs(); a.SetEnabled(true); }
                if (gm.Lives > lastLives) { tiHealEvents++; tiHealHp += gm.Lives - lastLives; }
                lastLives = gm.Lives;
                hpMin = Mathf.Min(hpMin, gm.Lives / (float)Mathf.Max(1, gm.MaxLives));
                if (gm.MaxDistance >= 95000f) { hpMin95 = Mathf.Min(hpMin95, gm.Lives / (float)Mathf.Max(1, gm.MaxLives)); if (t95 < 0f) t95 = Time.time; }
                if (gm.MaxDistance >= 99000f && t99 < 0f) t99 = Time.time;
                bool boss = BossManager.Instance != null && BossManager.Instance.IsBossPhase;
                if (boss && !wasBoss) { bosses++; bossStart = t; }
                if (!boss && wasBoss && bossStart >= 0f) { bossSecs.Add(t - bossStart); if (!gm.IsGameOver) bossWins++; }
                wasBoss = boss;
                if (!boss && gm.MaxDistance >= 50000f) runSecs50 += Time.unscaledDeltaTime;
                if (realAssist && !boss && a != null) { autoFrames++; if (a.Engaged) autoOn++; } // 4km からのボス戦以外で、オートが働いていた割合 // 50km からのボス戦以外の実時間(道中イベントの頻度)
                if (sample <= 0f)
                {
                    sample = 0.25f;
                    var cam = Camera.main;
                    foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
                    {
                        if (e == null || e.IsDying || firstSeen.ContainsKey(e)) continue;
                        if (cam != null) { var v = cam.WorldToViewportPoint(e.transform.position); if (v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f) continue; }
                        firstSeen[e] = Time.time;
                        if (e.Definition != null && e.Definition.behaviorKind >= EnemyBehaviorKind.AirLow && e.Definition.behaviorKind <= EnemyBehaviorKind.AirSwarm) airSeen++;
                    }
                }
            }
            EnemyController.LocalHit -= onHit;
            TimeControl.SetDebugTimeScale(1f);
            bool died = gm.IsGameOver;
            float endD = gm.MaxDistance / 1000f;
            var fatal = died && tiHits.Count > 0 ? tiHits[tiHits.Count - 1] : null;
            // 2026-11-06: 終盤 95〜100km(3体同時の組 → ラスボス → 静かな道)のまとめ
            {
                var bmF = BossManager.Instance;
                var h95 = tiHits.Where(h => h.km >= 95f).ToList();
                string fin = bmF == null ? "-" : $"sets={bmF.SetsStarted} spawned={bmF.SetBossesSpawned} maxAlive={bmF.MaxSetAlive} dropped={bmF.SetLeftoversDropped} hold98={bmF.SetHolds98} " +
                    $"t95-99={(t95 > 0f && t99 > 0f ? (t99 - t95) : -1f):F0}s finalUp={(bmF.FinalSpawnedAt > 0f && bmF.SetStartedAt > 0f ? bmF.FinalSpawnedAt - bmF.SetStartedAt : -1f):F0}s heavyDeferred={BossAttackGate.HeavyDeferred} heavyPeak={BossAttackGate.HeavyPeak} setHeals={bmF.SetKillHeals}";
                Debug.Log($"[QA] [finale] {ch} {stage} {tier} {modeArg} died={died} end={endD:F2} hpMin95={hpMin95 * 100f:F0}% hits95={h95.Count} hearts95={h95.Sum(h => h.hearts)} by95=[{string.Join(" ", h95.GroupBy(h => h.cat).Select(g => g.Key + ":" + g.Sum(x => x.hearts)))}] {fin}");
            }
            string last30 = string.Join(" ", tiHits.Where(h => fatal != null && fatal.t - h.t <= 30f).GroupBy(h => h.cat).Select(g => $"{g.Key}:{g.Sum(x => x.hearts)}"));
            string byCat = string.Join(" ", tiHits.GroupBy(h => h.cat).OrderByDescending(g => g.Sum(x => x.hearts)).Select(g => $"{g.Key}:{g.Sum(x => x.hearts)}"));
            string runCards = string.Join(" ", deck.Select(k => CardMastery.BaseIdOf(k)).Distinct().Select(id => $"{id}:{gm.GetRunCardLevel(id)}"));
            killS.Sort();
            // 10km ごとの被弾(区分の頭文字: e=イベント z=雑魚 a=空中 b=ボス t=地形 p=穴 o=その他)
            string by10 = string.Join("|", tiHits.GroupBy(h => Mathf.FloorToInt(h.km / 10f) * 10).OrderBy(g => g.Key)
                .Select(g => g.Key + ":" + string.Join("", g.GroupBy(h => h.cat).Select(c => c.Key.Substring(0, 1) + c.Sum(x => x.hearts)))));
            string row = $"{ch}\t{stage}\t{tier}\t{(tier == "C" ? build : "-")}\t{modeArg}\t{GameSettings.MaxSpeedPercent}{(speedScale > 1.01f ? "x" + speedScale.ToString(inv) : "")}\t{startKm:F1}\t{endD:F2}\t{(died ? 1 : 0)}\t{(fatal != null ? fatal.cat : "-")}\t{(fatal != null ? fatal.src : "-")}\t{last30}\t{t:F0}\t{gm.Level}\t{gm.MaxLives / CombatScale.HpPerHeart}\t{hpMin * 100f:F0}\t{tiHits.Count}\t{tiHits.Sum(h => h.hearts)}\t{byCat}\t{tiShield}\t{tiHealEvents}\t{tiHealHp / (float)CombatScale.HpPerHeart:F1}\t{killS.Count}\t{(killS.Count > 0 ? killS.Average() : 0f):F2}\t{bosses}\t{(bossSecs.Count > 0 ? bossSecs.Average() : 0f):F1}\t{bossWins}\t{DecisionEvents.Started - ev0}({(runSecs50 > 1f ? (DecisionEvents.Started - ev0) / (runSecs50 * ts / 60f) : 0f):F2}/min)\t{airSeen}\t{gm.TakeHomeMile}\t{string.Join(",", deck.Select(k => CardMastery.BaseIdOf(k)))}\t{runCards}\t{by10}\t{(autoFrames > 0 ? autoOn * 100f / autoFrames : 0f):F0}\t{(realT > 0f ? frames / realT : 0f):F0}\t{(realT > 0f ? frames / realT / ts : 0f):F0}";
            System.IO.File.AppendAllText(path, row + "\n");
            L($"[tier] {row}");
            yield return EndRun();
        }
        GameManager.QaDamageEvent = null; GameManager.QaShieldEvent = null;
        Check(true, "tier: done");
    }

    IEnumerator TiPick(System.Random rng)
    {
        arPicking = true;
        var seq = FindFirstObjectByType<RewardCardSequence>();
        if (seq != null)
        {
            int n = Mathf.Max(1, gm.DebugPendingChoiceIds != null ? gm.DebugPendingChoiceIds.Length : 3);
            int idx = rng.Next(n); // 普通の人の選び方の幅(いつも最適を選ぶわけではない)
            seq.OnCardClicked(idx);
            yield return new WaitForSecondsRealtime(0.2f);
            seq.OnCardClicked(idx);
            float w = 0f;
            while (gm.IsRewardSequenceWaitingForSelection && w < 6f) { yield return null; w += Time.unscaledDeltaTime; }
        }
        arPicking = false;
    }

    void OnTiDamage(string reason, int dmg, int before, int after)
    {
        int hearts = Mathf.RoundToInt(dmg / (float)CombatScale.HpPerHeart);
        string k = reason.StartsWith("HPZero:") ? reason.Substring(7) : reason;
        tiHits.Add(new TiHit { t = Time.time, km = gm != null ? gm.MaxDistance / 1000f : 0f, cat = TiCategory(reason), src = k, hearts = hearts });
    }
}
#endif
