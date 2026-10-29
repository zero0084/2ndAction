#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// BREAK 中に攻撃が出なくなる件の調査(2026-10-29): -qaBreakTrace <dir> [-btChars gunslinger,swordsman] [-btBosses Wild/Wolf,Wild/Griffin,Cave/Scorpion]
//   [-btReps 3] [-btKmh 45] [-btStart wait|now] [-btSeq U,F,F,F,U,F,F,F,D,F,F,F,D]
// 入力は画面の指と同じ経路(指を置く → 動かす → 離す、DebugPointer)で入れる。BREAK の始まりから終わりまで、毎フレーム同じ時間軸で書き出す:
//   指の状態 / 入力の行方(受け付け・予約・無視…)/ 技の始まり / 攻撃中・待ち時間・入力の受付不可 / 被弾の反応 / 時間の停止とその理由 / 画面の板
//   / BREAK の経過・残り / ボスとの距離・寄せの最中か / ボスの状態 / 当たった回数
// 1つの入力ごとに「受け取っていない / 受け取ったが技が出ない / 技は出たが当たっていない / 当たった」に分ける(breaktrace_inputs.tsv)。
// -btStart wait: BossAir と同じく、ボスが届く所(Launch 1.25m)へ寄るまで最大2秒待ってから入れる。now: BREAK の直後から入れる(人の操作に近い)
public partial class QaSweep
{
    IEnumerator BreakTraceMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        gm = GameManager.Instance;
        string[] chars = Arg("-btChars", "gunslinger,swordsman").Split(',');
        string[] bosses = Arg("-btBosses", "Wild/Wolf,Wild/Griffin,Cave/Scorpion").Split(',');
        int reps = int.Parse(Arg("-btReps", "3"));
        float kmh = float.Parse(Arg("-btKmh", "45"), System.Globalization.CultureInfo.InvariantCulture);
        string[] starts = Arg("-btStart", "wait,now").Split(',');
        string[] seq = Arg("-btSeq", "U,F,F,F,U,F,F,F,D,F,F,F,D").Split(',');
        var frames = new StringBuilder(BtFramesHeader);
        var inputs = new StringBuilder("case\tidx\tdir\tsentAt\tfate\tmoveStarted\tswing\thit\tclass\tstateAtSend\n");
        var summary = new StringBuilder("case\tchar\tboss\tstart\trep\tinputsSent\tnotReceived\treceivedNoMove\tmoveNoHit\thit\tbreakSec\tfirstInputAt\treachAt\tstopped\n");
        bool expKeep = GameManager.BlockExpGain;
        foreach (var ch in chars)
        {
            foreach (var grp in bosses.GroupBy(b => b.Split('/')[0]))
            {
                string stage = grp.Key == "Cave" ? "natural_cave" : grp.Key == "Sky" ? "sky_corridor" : "wasteland_road";
                yield return BeginRun(ch, stage);
                SetGod(true);
                GameManager.BlockExpGain = true;
                if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
                TerrainManager.Instance.pitChanceBase = 0f; TerrainManager.Instance.pitChanceMax = 0f;
                TerrainManager.Instance.DebugSetRouteBranch(false); // 2026-10-29: 上ルートに乗るとボスの上を弾が抜け、BREAK 中の撃破の確認が地形で落ちていた
                TerrainManager.Instance.enemySpawnChance = 0f;
                if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
                if (BossManager.Instance != null) BossManager.Instance.enabled = true;
                yield return new WaitForSeconds(1.2f);
                foreach (var bk in grp)
                    foreach (var st in starts)
                        for (int rep = 0; rep < reps; rep++)
                        {
                            string cs = $"{ch}|{bk}|{st}|{rep}";
                            yield return BtCase(cs, ch, bk, kmh, st == "now", seq, frames, inputs, summary);
                            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "breaktrace_frames.tsv"), frames.ToString());
                            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "breaktrace_inputs.tsv"), inputs.ToString());
                            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "breaktrace_summary.tsv"), summary.ToString());
                        }
                PlayerController.DebugPointer.active = false;
                PlayerController.DebugSpeedScale = 1f;
                SetGod(false);
                yield return EndRun();
            }
        }
        GameManager.BlockExpGain = expKeep;
    }

    // ---- BossAir の中でも同じ記録を取る(-baTrace 1)。止まった回は BossAir の手順(通常時の攻撃と崩しの後の BREAK)で出たため
    public class BtRec { public bool on = true; public string evt = "", ptr = "-"; public int hits; public float t0; public int moves0, sw0; public float reachAt = -1f;
        public Dictionary<PlayerBullet, Vector3> shots = new Dictionary<PlayerBullet, Vector3>(); public Dictionary<PlayerBullet, int> shotNo = new Dictionary<PlayerBullet, int>(); public int shotCount; public string inv = ""; }
    // 2026-10-29: 「技は出たが当たっていない」の理由を見るため、弾ごとに出た所・向き・消えた所を当たり判定(体)からの位置で書く
    static string BtRel(Vector3 p, Bounds hb) => $"({p.x - hb.min.x:F2},{p.y - hb.min.y:F2})";
    void BtShots(BaBoss boss, BtRec st)
    {
        var hb = boss.Live; var seen = new HashSet<PlayerBullet>();
        foreach (var b in FindObjectsByType<PlayerBullet>(FindObjectsSortMode.None))
        {
            var id = b; seen.Add(id); Vector3 p = b.transform.position;
            if (!st.shots.ContainsKey(id))
            {
                var rb = b.GetComponent<Rigidbody2D>(); Vector2 v = rb != null ? rb.linearVelocity : Vector2.zero;
                st.shotNo[id] = ++st.shotCount;
                st.evt += $"SHOT#{st.shotCount}(at{BtRel(p, hb)} v=({v.x:F0},{v.y:F0}) hurt=({hb.size.x:F1}x{hb.size.y:F1})) ";
            }
            st.shots[id] = p;
        }
        foreach (var k in st.shots.Keys.Where(k => !seen.Contains(k)).ToList()) { st.evt += $"SHOTEND#{st.shotNo[k]}(last{BtRel(st.shots[k], hb)}) "; st.shots.Remove(k); st.shotNo.Remove(k); }
        var W = boss.W;
        string inv = W == null ? "" : $"{(W.DebugInvulnerable ? "inv" : "")}{(W.DebugHurtCol != null && !W.DebugHurtCol.enabled ? "hurtOff" : "")}";
        if (inv != st.inv) { st.evt += $"BOSS[{(inv == "" ? "hittable" : inv)}] "; st.inv = inv; }
    }
    IEnumerator BtRecord(string cs, BaBoss boss, BtRec st, StringBuilder frames, float near)
    {
        var eof = new WaitForEndOfFrame();
        while (st.on)
        {
            yield return eof;
            var W = boss.W;
            if (boss.Valid && !boss.IsDead) BtShots(boss, st);
            float edge = W != null && !W.IsDead ? BaEdge(boss) : -1f;
            if (st.reachAt < 0f && edge >= 0f && edge <= near) st.reachAt = Time.time - st.t0;
            var jj = boss.Juggle;
            var cam = Camera.main; string hv = "-", sv = "-", pv = "-";
            if (cam != null && W != null)
            {
                var hb = boss.Live; hv = $"{cam.WorldToViewportPoint(hb.min).x:F2}..{cam.WorldToViewportPoint(hb.max).x:F2}";
                var sr = W.GetComponentsInChildren<SpriteRenderer>().Where(x => x.enabled && x.sprite != null).OrderByDescending(x => x.bounds.size.x * x.bounds.size.y).FirstOrDefault();
                if (sr != null) sv = $"{cam.WorldToViewportPoint(sr.bounds.min).x:F2}..{cam.WorldToViewportPoint(sr.bounds.max).x:F2}";
                pv = $"{cam.WorldToViewportPoint(pc.transform.position).x:F2}";
            }
            frames.Append($"{cs}\t{Time.time - st.t0:F3}\t{Time.frameCount}\t{(W != null ? W.BreakElapsed : 0f):F2}\t{(W != null ? W.DebugBreakRemaining : 0f):F2}\t{boss.Broken}\t{edge:F2}\t{(W != null && W.DebugBreakPulling)}\t{(W != null ? W.DebugRelVelocity : 0f):F1}\t{boss.Act}\t{(jj == null ? "-" : jj.Airborne ? "air" : jj.Anchored ? "anc" : "-")}\t{st.ptr}\t{pc.IsGrounded}\t{pc.IsAttacking}\t{pc.QaAttackCooldown:F2}\t{pc.QaInputBusy}\t{pc.IsReacting}\t{Time.timeScale:F2}\t{(Time.timeScale <= 0f ? TimeControl.DescribeActiveReasons() : "")}\t{UiInputGate.Blocked}\t{gm.IsRewardSequenceRunning}\t{pc.DebugMoveStarts - st.moves0}\t{PlayerAttackInfo.SwingCount - st.sw0}\t{st.hits}\t{hv}\t{sv}\t{pv}\t{st.evt.Trim()}\t{(boss.Valid && !boss.IsDead ? pc.transform.position.y - boss.Live.min.y : 0f):F2}\t{pc.QaOnSky}\n");
            st.evt = "";
        }
    }
    // 画面の指と同じ経路の速いフリック(置く → 38px ずつ動かす → 離す)
    IEnumerator BtSwipe(PlayerController.FlickDirection f, BtRec st)
    {
        Vector2 v = f == PlayerController.FlickDirection.Up ? Vector2.up : f == PlayerController.FlickDirection.Down ? Vector2.down : f == PlayerController.FlickDirection.Backward ? Vector2.left : Vector2.right;
        var p = PlayerController.DebugPointer.pos = new Vector2(Screen.width * 0.5f, Screen.height * 0.45f);
        PlayerController.DebugPointer.prevDown = false;
        PlayerController.DebugPointer.active = true; PlayerController.DebugPointer.down = true; if (st != null) st.ptr = "down";
        yield return null;
        for (float d = 0f; d < 90f; d += 38f) { p += v * 38f; PlayerController.DebugPointer.pos = p; if (st != null) st.ptr = "move"; yield return null; }
        PlayerController.DebugPointer.down = false; if (st != null) st.ptr = "up";
        yield return null; yield return null;
        PlayerController.DebugPointer.active = false; if (st != null) st.ptr = "-";
    }
    public const string BtFramesHeader = "case\tt\tframe\tbreakT\tbreakRemain\tbroken\tedge\tpulling\trelVel\tbossAct\tjuggle\tptr\tgrounded\tattacking\tcooldown\tinputBusy\treacting\ttimeScale\tpause\tuiBlocked\tcardChoice\tmoves\tswings\thits\thurtVp\tspriteVp\tplayerVp\tevent\tdyFeet\tplayerOnSky\n";

    IEnumerator BtCase(string cs, string ch, string key, float kmh, bool startNow, string[] seq, StringBuilder frames, StringBuilder inputs, StringBuilder summary)
    {
        L($"== breaktrace {cs} ==");
        if (!BaParse(key, out int fam, out int kind)) yield break;
        yield return BfWaitEncounterEnd();
        BossManager.Instance.DebugSpawnBossForTest(fam, kind);
        SetKmh(kmh);
        BaBoss boss = null; float w = 0f; bool scaled = false;
        while (w < 15f)
        {
            yield return null; w += Time.unscaledDeltaTime;
            boss = BaFind();
            if (boss.Valid && !scaled) { boss.ScaleHp(40f); scaled = true; }
            if (boss.W != null && !boss.W.IsEntering && w > 1.5f) break;
            if ((boss.D != null || boss.M != null) && w > 4.5f) break;
        }
        if (boss == null || !boss.Valid || boss.W == null) { L($"[breaktrace] {cs}: no boss"); yield break; }
        Component bc = boss.C;
        SetKmh(kmh);
        int hits = 0;
        string evt = "";
        void OnHitW(WildBossBase b, PlayerAttackKind k, bool lethal) { if (b == bc) { hits++; evt += $"HIT({k}) "; } }
        var fates = new List<(int id, float t, PlayerController.FlickDirection d, PlayerController.FlickFate f)>();
        System.Action<int, PlayerController.FlickDirection, PlayerController.FlickFate> onFate = (id, d, f) => { fates.Add((id, Time.time, d, f)); evt += $"FATE({d}:{f}) "; };
        WildBossBase.QaLocalHit += OnHitW;
        PlayerController.QaFlickFate += onFate;
        try
        {
            yield return new WaitForSeconds(0.6f);
            yield return BaWaitCalm(boss);
            w = 0f; while ((!pc.IsGrounded || pc.IsAttacking) && w < 2f) { yield return null; w += Time.deltaTime; }
            boss.ForceBreak();
            float t0 = Time.time;
            int moves0 = pc.DebugMoveStarts, sw0 = PlayerAttackInfo.SwingCount;
            bool recording = true;
            string ptr = "-";
            float reachAt = -1f;
            float near = boss.Style == "Launch" ? 1.25f : 2.6f;
            // 毎フレームの記録(描く時 = フレームの終わり)
            IEnumerator Rec()
            {
                var eof = new WaitForEndOfFrame();
                while (recording)
                {
                    yield return eof;
                    var W = boss.W;
                    float edge = W != null && !W.IsDead ? BaEdge(boss) : -1f;
                    if (reachAt < 0f && edge >= 0f && edge <= near) reachAt = Time.time - t0;
                    var jj = boss.Juggle;
                    // 画面(横の 0〜1)での当たり判定・絵・プレイヤーの位置(リヴァイアサンが前側へ外れる件)
                    var cam = Camera.main; string hv = "-", sv = "-", pv = "-";
                    if (cam != null && W != null)
                    {
                        var hb = boss.Live; hv = $"{cam.WorldToViewportPoint(hb.min).x:F2}..{cam.WorldToViewportPoint(hb.max).x:F2}";
                        var sr = W.GetComponentsInChildren<SpriteRenderer>().Where(x => x.enabled && x.sprite != null).OrderByDescending(x => x.bounds.size.x * x.bounds.size.y).FirstOrDefault();
                        if (sr != null) sv = $"{cam.WorldToViewportPoint(sr.bounds.min).x:F2}..{cam.WorldToViewportPoint(sr.bounds.max).x:F2}";
                        pv = $"{cam.WorldToViewportPoint(pc.transform.position).x:F2}";
                    }
                    frames.Append($"{cs}\t{Time.time - t0:F3}\t{Time.frameCount}\t{(W != null ? W.BreakElapsed : 0f):F2}\t{(W != null ? W.DebugBreakRemaining : 0f):F2}\t{boss.Broken}\t{edge:F2}\t{(W != null && W.DebugBreakPulling)}\t{(W != null ? W.DebugRelVelocity : 0f):F1}\t{boss.Act}\t{(jj == null ? "-" : jj.Airborne ? "air" : jj.Anchored ? "anc" : "-")}\t{ptr}\t{pc.IsGrounded}\t{pc.IsAttacking}\t{pc.QaAttackCooldown:F2}\t{pc.QaInputBusy}\t{pc.IsReacting}\t{Time.timeScale:F2}\t{(Time.timeScale <= 0f ? TimeControl.DescribeActiveReasons() : "")}\t{UiInputGate.Blocked}\t{gm.IsRewardSequenceRunning}\t{pc.DebugMoveStarts - moves0}\t{PlayerAttackInfo.SwingCount - sw0}\t{hits}\t{hv}\t{sv}\t{pv}\t{evt.Trim()}\n");
                    evt = "";
                }
            }
            var rec = StartCoroutine(Rec());
            yield return null;
            if (!startNow) { w = 0f; while (w < 2f && BaEdge(boss) > near && boss.Broken) { yield return null; w += Time.deltaTime; } }
            float firstInputAt = -1f;
            int sent = 0, notRecv = 0, recvNoMove = 0, moveNoHit = 0, hitN = 0;
            for (int si = 0; si < seq.Length && boss.Broken && !boss.IsDead; si++)
            {
                var f = seq[si] == "U" ? PlayerController.FlickDirection.Up : seq[si] == "D" ? PlayerController.FlickDirection.Down : PlayerController.FlickDirection.Forward;
                int fa0 = fates.Count, mv0 = pc.DebugMoveStarts, swc0 = PlayerAttackInfo.SwingCount, h0 = hits;
                float sentAt = Time.time - t0;
                if (firstInputAt < 0f) firstInputAt = sentAt;
                string stateAt = $"grounded={pc.IsGrounded} attacking={pc.IsAttacking} cd={pc.QaAttackCooldown:F2} busy={pc.QaInputBusy} react={pc.IsReacting} edge={BaEdge(boss):F2} remain={boss.W.DebugBreakRemaining:F2}";
                evt += $"SEND({f}) ";
                // 指を置く → 1フレームごとに 38px 動かす → 離す(速いフリック)
                Vector2 v = f == PlayerController.FlickDirection.Up ? Vector2.up : f == PlayerController.FlickDirection.Down ? Vector2.down : Vector2.right;
                var p = PlayerController.DebugPointer.pos = new Vector2(Screen.width * 0.5f, Screen.height * 0.45f);
                PlayerController.DebugPointer.prevDown = false;
                PlayerController.DebugPointer.active = true; PlayerController.DebugPointer.down = true; ptr = "down";
                yield return null;
                for (float d = 0f; d < 90f; d += 38f) { p += v * 38f; PlayerController.DebugPointer.pos = p; ptr = "move"; yield return null; }
                PlayerController.DebugPointer.down = false; ptr = "up";
                yield return null; yield return null;
                PlayerController.DebugPointer.active = false; ptr = "-";
                float s = 0f; while (s < 0.3f) { yield return null; s += Time.deltaTime; }
                sent++;
                bool recv = fates.Count > fa0;
                string fate = recv ? string.Join("+", fates.Skip(fa0).Select(x => $"{x.d}:{x.f}")) : "none";
                bool moved = pc.DebugMoveStarts > mv0, swung = PlayerAttackInfo.SwingCount > swc0, hit = hits > h0;
                string cls = hit ? "hit" : (moved || swung) ? "move-no-hit" : recv ? "received-no-move" : "not-received";
                if (cls == "hit") hitN++; else if (cls == "move-no-hit") moveNoHit++; else if (cls == "received-no-move") recvNoMove++; else notRecv++;
                inputs.Append($"{cs}\t{si}\t{f}\t{sentAt:F2}\t{fate}\t{moved}\t{swung}\t{hit}\t{cls}\t{stateAt}\n");
            }
            // BREAK の終わりまで記録
            w = 0f; while (boss.Broken && w < 8f && !boss.IsDead) { yield return null; w += Time.deltaTime; }
            recording = false; yield return null; StopCoroutine(rec);
            float breakSec = boss.W.LastBreakSeconds;
            bool stopped = hitN <= 2 && sent <= 3;
            summary.Append($"{cs}\t{ch}\t{key}\t{(startNow ? "now" : "wait")}\t{cs.Split('|')[3]}\t{sent}\t{notRecv}\t{recvNoMove}\t{moveNoHit}\t{hitN}\t{breakSec:F2}\t{firstInputAt:F2}\t{reachAt:F2}\t{stopped}\n");
            L($"[breaktrace] {cs}: sent {sent} notRecv {notRecv} recvNoMove {recvNoMove} moveNoHit {moveNoHit} hit {hitN} break {breakSec:F2}s firstInput {firstInputAt:F2}s reach {reachAt:F2}s{(stopped ? "  <-- STOPPED" : "")}");
            // 次へ: ボスを倒して片付ける
            if (!boss.IsDead) { boss.SetHp(1); for (int i = 0; i < 20 && !boss.IsDead; i++) { yield return HaFlick(PlayerController.FlickDirection.Forward); yield return new WaitForSeconds(0.25f); } }
            if (!boss.IsDead && boss.W != null) boss.W.SendMessage("DebugKill", SendMessageOptions.DontRequireReceiver);
            yield return BfWaitEncounterEnd();
        }
        finally
        {
            WildBossBase.QaLocalHit -= OnHitW;
            PlayerController.QaFlickFate -= onFate;
            PlayerController.DebugPointer.active = false;
        }
    }
}
#endif
