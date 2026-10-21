#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// 2026-10-21 コンボのつながり改修: -qaChain <dir>
//   [-chChars swordsman,dragon_lancer,gunslinger] [-chTargets mob,boss] [-chBosses Wild/BlackKnight,Wild/Cyclops,Sky/Phoenix]
//   [-chStages wasteland_road,natural_cave] [-chSpeeds 45,150] [-chGaps 0.15,0.22,0.3,0.45] [-chSeq U,F,F,F,U,F,F,F,D] [-chFps 60]
// 入力1回ごとに、次の4つを分けて記録する(13回入力して何回当たったか、だけでなく):
//   受理: R = 受け付けられずに消えた(e=時間切れ r=新しい入力に置き換え c=被弾/停止で捨てた i=何も起きなかった m=同じ向きの予約にまとめた)
//   実行: A = 受け付けたが技が出なかった / それ以外は技が出た(入力から技が出るまでの秒数も)
//   命中: H = 出た技が対象に当たった / W = 出たが外れた(その時の対象と「当たる所」のずれ dx,dy)
// 入力は本物の1回のフリックと同じく1フレームだけ(debugInjectOnce)。ヒットストップ中に入れても先行入力として覚える。
// あわせて、コンボの間の対象と「当たる所」のずれの最大(位置ずれ)と、寄せが終わった理由を記録する。
// 出力: chain.tsv(1行=1回)、chain_inputs.tsv(1行=1入力)、qa_chain.txt
public partial class QaSweep
{
    class ChRec
    {
        public int idx; public string dir; public float tIn; public string fates = "";
        public float tAcc = -1f, tExec = -1f;
        public bool accepted, dropped, land; public char dropWhy = '-';
        public int swings, hits;
        public float dx, dy; public bool anchoredAtExec;
    }

    class ChTracker
    {
        public readonly List<ChRec> recs = new List<ChRec>();
        readonly Queue<ChRec> awaitingId = new Queue<ChRec>();
        readonly Dictionary<int, ChRec> byPcId = new Dictionary<int, ChRec>();
        readonly Dictionary<int, ChRec> bySwing = new Dictionary<int, ChRec>();
        readonly List<ChRec> fifo = new List<ChRec>();
        ChRec cur;
        int prevMs, prevSc;
        public PlayerController pc;
        public System.Func<Vector3?> aim;   // 対象の狙いの点(無ければ null)
        public System.Func<bool> anchored;
        public JumpMon jumps; // 2026-10-21: 1フレームの跳び(相手の位置の急な変化)の記録
        public float maxOffX, maxOffY; public int driftFrames, anchoredFrames;

        public void Begin(PlayerController p)
        {
            pc = p;
            prevMs = pc.DebugMoveStarts; prevSc = PlayerAttackInfo.SwingCount;
            PlayerController.QaFlickFate += OnFate;
        }
        public void End() { PlayerController.QaFlickFate -= OnFate; }

        public ChRec Inject(int idx, string d)
        {
            var r = new ChRec { idx = idx, dir = d, tIn = Time.time };
            recs.Add(r); awaitingId.Enqueue(r);
            pc.debugInjectOnce = d == "U" ? PlayerController.FlickDirection.Up : d == "D" ? PlayerController.FlickDirection.Down : d == "B" ? PlayerController.FlickDirection.Backward : PlayerController.FlickDirection.Forward;
            return r;
        }

        void OnFate(int id, PlayerController.FlickDirection dir, PlayerController.FlickFate fate)
        {
            if (!byPcId.TryGetValue(id, out var r))
            {
                if (awaitingId.Count == 0) return;
                r = awaitingId.Dequeue(); byPcId[id] = r;
            }
            r.fates += fate switch
            {
                PlayerController.FlickFate.Immediate => "I", PlayerController.FlickFate.Reserved => "s", PlayerController.FlickFate.FiredLater => "F",
                PlayerController.FlickFate.Expired => "e", PlayerController.FlickFate.Replaced => "r", PlayerController.FlickFate.Cleared => "c",
                PlayerController.FlickFate.Ignored => "i", PlayerController.FlickFate.Merged => "m", _ => "L",
            };
            switch (fate)
            {
                case PlayerController.FlickFate.Immediate:
                case PlayerController.FlickFate.FiredLater:
                    if (!r.accepted) { r.accepted = true; r.tAcc = Time.time; fifo.Add(r); }
                    break;
                case PlayerController.FlickFate.LandBuffered:
                    r.land = true; r.tAcc = Time.time; fifo.Add(r);
                    break;
                case PlayerController.FlickFate.Expired: case PlayerController.FlickFate.Replaced:
                case PlayerController.FlickFate.Cleared: case PlayerController.FlickFate.Ignored: case PlayerController.FlickFate.Merged:
                    if (!r.accepted) { r.dropped = true; r.dropWhy = fate == PlayerController.FlickFate.Expired ? 'e' : fate == PlayerController.FlickFate.Replaced ? 'r' : fate == PlayerController.FlickFate.Cleared ? 'c' : fate == PlayerController.FlickFate.Merged ? 'm' : 'i'; }
                    break;
            }
        }

        // 毎フレーム(QA のコルーチンから)
        public void Tick()
        {
            if (pc == null) return;
            // 1フレームの跳びは描画の終わりに測る(ChJumpTick)。ここ(Update の後・LateUpdate の前)ではカメラが前のフレームの位置のままで、
            // カメラとの差が1フレーム分ずれて見えていた
            int ms = pc.DebugMoveStarts;
            if (ms > prevMs)
            {
                prevMs = ms;
                // 着地の予約の↑は 0.5秒以上たっていたら出ていない(次の技の持ち主にしない)
                fifo.RemoveAll(x => x.land && Time.time - x.tAcc > 0.5f);
                fifo.RemoveAll(x => Time.time - x.tAcc > 1.2f); // 1.2秒たっても出ない予約は、新しい入力に上書きされた/出なかった(A のまま)
                // このフレームに受け付けた入力があれば、それがこの技(すぐ出る技)。無ければ一番古い予約(次の段など、後で出る技)
                ChRec owner = null;
                for (int k = fifo.Count - 1; k >= 0; k--) if (Mathf.Approximately(fifo[k].tAcc, Time.time)) { owner = fifo[k]; break; }
                if (owner == null && fifo.Count > 0) owner = fifo[0];
                if (owner != null)
                {
                    fifo.Remove(owner);
                    if (owner.land) owner.accepted = true;
                    owner.tExec = Time.time;
                    var a = aim != null ? aim() : null;
                    if (a.HasValue)
                    {
                        Vector2 sweet = pc.AerialComboSweetSpot();
                        Vector2 d = (Vector2)a.Value - (Vector2)pc.transform.position;
                        owner.dx = d.x - sweet.x; owner.dy = d.y - sweet.y;
                    }
                    owner.anchoredAtExec = anchored != null && anchored();
                }
                cur = owner;
            }
            int sc = PlayerAttackInfo.SwingCount;
            for (int s = prevSc + 1; s <= sc; s++) if (cur != null) { bySwing[s] = cur; cur.swings++; }
            prevSc = sc;
            if (anchored != null && anchored() && aim != null)
            {
                var a = aim();
                if (a.HasValue)
                {
                    anchoredFrames++;
                    Vector2 sweet = pc.AerialComboSweetSpot();
                    Vector2 off = (Vector2)a.Value - ((Vector2)pc.transform.position + sweet);
                    maxOffX = Mathf.Max(maxOffX, Mathf.Abs(off.x)); maxOffY = Mathf.Max(maxOffY, Mathf.Abs(off.y));
                    if (Mathf.Abs(off.x) > 1.4f || Mathf.Abs(off.y) > 1.2f) driftFrames++;
                }
            }
        }

        public void OnTargetHit()
        {
            if (bySwing.TryGetValue(PlayerAttackInfo.LastHitSwingId, out var r)) r.hits++;
        }

        public static string Code(ChRec r)
        {
            if (r.tExec >= 0f) return r.hits > 0 ? "H" : r.swings == 0 ? "C" : "W"; // C = 技は始まったが、攻撃が出る前に次の入力で切った
            if (r.accepted) return "A";
            if (r.dropped) return "R" + r.dropWhy;
            return "P";
        }
    }

    // 2026-10-21: 相手(雑魚/ボス)の1フレームの跳び。プレイヤーに対して / 画面(カメラ)に対して の両方で測り、
    //  0.9m/フレーム(60fps 換算)を越えたら、その時の内訳(プレイヤー/相手/カメラの動き、相手の足元の床の変化、ボスの移動の内訳、状態)を残す
    public class JumpMon
    {
        public System.Func<Transform> target;
        public System.Func<string> state;
        public System.Func<Vector4> split;   // ボス: 並走/間合いの補正/回り込み/BREAK の寄せ(WildBossBase.DebugMoveSplit)
        public readonly List<string> events = new List<string>();
        public readonly List<float> eventTimes = new List<float>();
        public float maxRelP, maxRelC;
        public int count, playerWarps; // playerWarps: プレイヤーが1フレームで6m以上動いた(穴からの救出のワープ等。跳びの数には入れない)
        bool has; double pX, tX, cX; float pY, tY, cY, tGround, stepPrev;
        string lastState = "";
        public const float Threshold = 0.9f;

        public void Tick(PlayerController pc)
        {
            var t = target != null ? target() : null;
            var cam = Camera.main;
            if (t == null || cam == null || !t.gameObject.activeInHierarchy) { has = false; return; }
            double off = FloatingOrigin.Offset;
            double px = pc.transform.position.x + off, tx = t.position.x + off, cx = cam.transform.position.x + off;
            float py = pc.transform.position.y, ty = t.position.y, cy = cam.transform.position.y;
            var tm = TerrainManager.Instance;
            float g = tm != null ? tm.GetSupportHeightAt(t.position.x) : 0f;
            string st = state != null ? state() : "";
            float dt = Time.deltaTime;
            if (has && dt > 0f)
            {
                float k = 0.0167f / Mathf.Max(0.0167f, dt);
                Vector2 dP = new Vector2((float)(px - pX), py - pY), dT = new Vector2((float)(tx - tX), ty - tY), dC = new Vector2((float)(cx - cX), cy - cY);
                float relP = (dT - dP).magnitude * k, relC = (dT - dC).magnitude * k;
                maxRelP = Mathf.Max(maxRelP, relP); maxRelC = Mathf.Max(maxRelC, relC);
                if (dP.magnitude >= 6f) { playerWarps++; if (events.Count < 40) events.Add($"t={Time.time:F2} PLAYER WARP dP=({dP.x:F1},{dP.y:F1}) pY={py:F1}"); }
                if ((relP > Threshold || relC > Threshold) && dP.magnitude < 6f)
                {
                    count++;
                    Vector4 sp = split != null ? split() : Vector4.zero;
                    string cause = Classify(dP, dT, dC, g - tGround, sp, st, lastState, pc);
                    var cf = cam.GetComponent<CameraFollow>();
                    float dStep = pc.ScreenStepOffset - stepPrev;
                    if (relC > Threshold && relP <= Threshold) cause += "+cameraOnly" + (cf != null && cf.IsShaking ? "(shake)" : "") + (Mathf.Abs(dStep) > 0.3f ? $"(step{dStep:F2})" : "");
                    if (events.Count < 40) events.Add($"t={Time.time:F2} relP={relP:F2} relC={relC:F2} dP=({dP.x:F2},{dP.y:F2}) dT=({dT.x:F2},{dT.y:F2}) dC=({dC.x:F2},{dC.y:F2}) dGround={g - tGround:F2} split=({sp.x:F2},{sp.y:F2},{sp.z:F2},{sp.w:F2}) pGround={pc.IsGrounded} state={st} prev={lastState} => {cause}");
                    eventTimes.Add(Time.time);
                }
            }
            pX = px; tX = tx; cX = cx; pY = py; tY = ty; cY = cy; tGround = g; lastState = st; stepPrev = pc.ScreenStepOffset; has = true;
        }

        static string Classify(Vector2 dP, Vector2 dT, Vector2 dC, float dGround, Vector4 sp, string st, string prev, PlayerController pc)
        {
            var c = new List<string>();
            if (Mathf.Abs(dP.y) > 0.6f && Mathf.Abs(dT.y) < 0.3f) c.Add("playerVertical");
            if (Mathf.Abs(dGround) > 0.5f && Mathf.Abs(dT.y - dGround) < 0.25f) c.Add("targetFloorStep");
            if (Mathf.Abs(sp.y) > 0.4f) c.Add("bossGapCorrection");
            if (Mathf.Abs(sp.z) > 0.4f) c.Add("bossFrontReturn");
            if (Mathf.Abs(sp.w) > 0.4f) c.Add("juggle");
            if (st != prev) c.Add($"stateChange({prev}->{st})");
            if (Mathf.Abs(dT.x - dP.x) > 0.9f && Mathf.Abs(dP.x - pc.CurrentAutoRunSpeed * Time.deltaTime) > 0.6f) c.Add("playerLungeOrKnock");
            if (c.Count == 0) c.Add("other");
            return string.Join("+", c);
        }
    }

    struct ChSum { public int n, hit, whiff, acc, rej, pend; public float latSum, latMax; public int latN; public string rejWhy; }

    IEnumerator ChainMode()
    {
        HookBossLogs();
        Random.InitState(20261021);
        string[] chars = Arg("-chChars", "swordsman,dragon_lancer,gunslinger").Split(',');
        string[] targets = Arg("-chTargets", "mob,boss").Split(',');
        string[] bosses = Arg("-chBosses", "Wild/BlackKnight,Wild/Cyclops,Wild/Griffin").Split(',');
        string[] stages = Arg("-chStages", "wasteland_road,natural_cave").Split(',');
        float[] speeds = Arg("-chSpeeds", "45,150").Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        float[] gaps = Arg("-chGaps", "0.15,0.22,0.3,0.45").Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        string[] seq = Arg("-chSeq", "U,F,F,F,U,F,F,F,D").Split(',');
        int fps = int.Parse(Arg("-chFps", "60"));
        ComboRevision.Legacy = Arg("-chLegacy", "0") == "1"; // 改修前の動き(比較用)
        ComboRevision.FollowupOff = Arg("-chFollowupOff", "0") == "1"; // APK 339 の動き(今回の追加調査の分だけを切る。比較用)
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = fps;
        L($"[chain] fps={fps} screen={Screen.width}x{Screen.height} seq={string.Join("", seq)} gaps={string.Join("/", gaps)} speeds={string.Join("/", speeds)}");
        var tsv = new StringBuilder("char\ttarget\tstage\tkmh\tfps\tgap\tinputs\thit\twhiff\taccNoMove\trejected\tpending\tlatAvg\tlatMax\tmaxOffX\tmaxOffY\tdriftFrames\tanchoredFrames\tends\tlog\n");
        var itsv = new StringBuilder("char\ttarget\tstage\tkmh\tgap\ti\tdir\tcode\tfates\tlatency\tswings\thits\tdx\tdy\tanchored\n");
        var sum = new Dictionary<string, ChSum>();
        // -chCases "wasteland_road:mob@45|Wild/BlackKnight@150;natural_cave:mob@150|Cave/Bat@45" : ステージごとに 対象@速さ の組だけ(間隔は -chGaps の全部)
        string casesArg = Arg("-chCases", "");
        var cases = new Dictionary<string, List<(string key, float kmh)>>();
        if (casesArg.Length > 0)
            foreach (var part in casesArg.Split(';'))
            {
                var kv = part.Split(':'); if (kv.Length != 2) continue;
                var list = new List<(string, float)>();
                foreach (var c in kv[1].Split('|')) { var q = c.Split('@'); if (q.Length == 2) list.Add((q[0], float.Parse(q[1], System.Globalization.CultureInfo.InvariantCulture))); }
                cases[kv[0]] = list;
            }
        if (cases.Count > 0) stages = cases.Keys.ToArray();
        foreach (var ch in chars)
            foreach (var st in stages)
            {
                aeChar = ch;
                yield return AeBegin(ch, st);
                TerrainManager.Instance.pitChanceBase = 0f; TerrainManager.Instance.pitChanceMax = 0f;
                if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
                if (BossManager.Instance != null) BossManager.Instance.enabled = true;
                if (cases.TryGetValue(st, out var plan))
                {
                    foreach (var (key, kmh) in plan)
                        foreach (var gap in gaps)
                        {
                            var tr = new ChTracker();
                            var ends = new StringBuilder();
                            if (key != "mob") yield return ChBoss(key, kmh, gap, seq, tr, ends);
                            else yield return ChMob(kmh, gap, seq, tr, ends);
                            ChRow(tsv, itsv, sum, ch, key, st, kmh, fps, gap, tr, ends.ToString());
                            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "chain.tsv"), tsv.ToString());
                            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "chain_inputs.tsv"), itsv.ToString());
                        }
                    yield return AeEnd();
                    continue;
                }
                foreach (var tg in targets)
                {
                    var keys = tg == "boss" ? bosses.Where(b => (st == "natural_cave") == b.StartsWith("Cave/") || (st == "wasteland_road" && b.StartsWith("Wild/")) || (st == "sky_corridor" && b.StartsWith("Sky/"))).ToArray() : new[] { "goblin" };
                    foreach (var key in keys)
                        foreach (var kmh in speeds)
                            foreach (var gap in gaps)
                            {
                                var tr = new ChTracker();
                                var ends = new StringBuilder();
                                if (tg == "boss") yield return ChBoss(key, kmh, gap, seq, tr, ends);
                                else yield return ChMob(kmh, gap, seq, tr, ends);
                                ChRow(tsv, itsv, sum, ch, tg == "boss" ? key : "mob", st, kmh, fps, gap, tr, ends.ToString());
                                System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "chain.tsv"), tsv.ToString());
                                System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "chain_inputs.tsv"), itsv.ToString());
                            }
                }
                yield return AeEnd();
            }
        L("== chain summary: inputs / H(hit) / W(whiff) / A(accepted, no move) / R(rejected) / P(pending), latency avg/max(s)");
        foreach (var kv in sum.OrderBy(k => k.Key))
        {
            var v = kv.Value;
            L($"[chain] {kv.Key,-40} n={v.n} H={v.hit} W={v.whiff} A={v.acc} R={v.rej}({v.rejWhy}) P={v.pend} lat={(v.latN > 0 ? v.latSum / v.latN : 0f):F2}/{v.latMax:F2}");
        }
        Application.targetFrameRate = 60;
    }

    readonly StringBuilder jumpTsv = new StringBuilder("char\ttarget\tstage\tkmh\tgap\tjumps\tmaxRelPlayer\tmaxRelCamera\twhiffNearJump\tevents\n");
    void ChRow(StringBuilder tsv, StringBuilder itsv, Dictionary<string, ChSum> sum, string ch, string tg, string st, float kmh, int fps, float gap, ChTracker tr, string ends)
    {
        int h = 0, w = 0, a = 0, r = 0, p = 0, cx = 0, wj = 0; float latSum = 0f, latMax = 0f; int latN = 0;
        var lg = new StringBuilder(); var why = new StringBuilder();
        foreach (var x in tr.recs)
        {
            string c = ChTracker.Code(x);
            float lat = x.tExec >= 0f ? x.tExec - x.tIn : -1f;
            if (lat >= 0f) { latSum += lat; latN++; latMax = Mathf.Max(latMax, lat); }
            switch (c[0]) { case 'H': h++; break; case 'W': w++; break; case 'C': cx++; break; case 'A': a++; break; case 'R': r++; why.Append(c[1]); break; default: p++; break; }
            bool nearJump = false;
            if (tr.jumps != null && x.tExec >= 0f) foreach (float jt in tr.jumps.eventTimes) if (jt >= x.tExec - 0.25f && jt <= x.tExec + 0.35f) nearJump = true;
            if (c == "W" && nearJump) { wj++; c = "Wj"; }
            lg.Append($"{x.dir}:{c}{(lat >= 0f ? $"+{lat:F2}" : "")}{(c.StartsWith("W") ? $"({x.dx:F1},{x.dy:F1}{(x.anchoredAtExec ? "" : ",free")})" : "")} ");
            itsv.Append($"{ch}\t{tg}\t{st}\t{kmh:F0}\t{gap:F2}\t{x.idx}\t{x.dir}\t{c}\t{x.fates}\t{lat:F3}\t{x.swings}\t{x.hits}\t{x.dx:F2}\t{x.dy:F2}\t{x.anchoredAtExec}\n");
        }
        tsv.Append($"{ch}\t{tg}\t{st}\t{kmh:F0}\t{fps}\t{gap:F2}\t{tr.recs.Count}\t{h}\t{w}\t{a}\t{r}\t{p}\t{(latN > 0 ? latSum / latN : 0f):F3}\t{latMax:F3}\t{tr.maxOffX:F2}\t{tr.maxOffY:F2}\t{tr.driftFrames}\t{tr.anchoredFrames}\t{ends}\t{lg}\n");
        if (tr.jumps != null)
        {
            jumpTsv.Append($"{ch}\t{tg}\t{st}\t{kmh:F0}\t{gap:F2}\t{tr.jumps.count}\t{tr.jumps.maxRelP:F2}\t{tr.jumps.maxRelC:F2}\t{wj}\t{string.Join(" || ", tr.jumps.events)}\n");
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "chain_jumps.tsv"), jumpTsv.ToString());
        }
        L($"[chain] {ch,-13} {tg,-16} {st,-15} {kmh,3:F0}km/h gap={gap:F2} jumps={(tr.jumps != null ? tr.jumps.count : 0)}{(tr.jumps != null && tr.jumps.playerWarps > 0 ? $"+warp{tr.jumps.playerWarps}" : "")}(maxP {(tr.jumps != null ? tr.jumps.maxRelP : 0f):F2} maxC {(tr.jumps != null ? tr.jumps.maxRelC : 0f):F2}) Wj={wj} H={h} W={w} C={cx} A={a} R={r}({why}) P={p} lat={(latN > 0 ? latSum / latN : 0f):F2}/{latMax:F2} off={tr.maxOffX:F1},{tr.maxOffY:F1} drift={tr.driftFrames}/{tr.anchoredFrames} ends=[{ends}]  {lg}");
        string key = $"{ch} {tg} {kmh:F0} gap{gap:F2}";
        sum.TryGetValue(key, out var s);
        s.n += tr.recs.Count; s.hit += h; s.whiff += w; s.acc += a; s.rej += r; s.pend += p; s.latSum += latSum; s.latN += latN; s.latMax = Mathf.Max(s.latMax, latMax); s.rejWhy += why.ToString();
        sum[key] = s;
    }

    IEnumerator ChRunSeq(string[] seq, float gap, ChTracker tr, System.Func<bool> alive)
    {
        tr.Begin(pc);
        try
        {
            for (int i = 0; i < seq.Length && alive(); i++)
            {
                tr.Inject(i, seq[i]);
                float t = 0f;
                while (t < gap && alive()) { yield return null; tr.Tick(); t += Time.deltaTime; }
            }
            // 最後の入力の技が出て当たるまで(飛び道具が届くまで)見届ける
            float w = 0f;
            while (w < 0.8f && alive()) { yield return null; tr.Tick(); w += Time.deltaTime; }
        }
        finally { tr.End(); }
    }

    // 雑魚: 最初の打ち上げが届かない置き方(上のルート/段差の上に出た等)は試験の都合なので、置き直して3回まで
    IEnumerator ChMob(float kmh, float gap, string[] seq, ChTracker tr, StringBuilder ends)
    {
        for (int att = 0; att < 3; att++)
        {
            var t2 = new ChTracker();
            var e2 = new StringBuilder();
            yield return ChMobOnce(kmh, gap, seq, t2, e2);
            bool launched = t2.recs.Count > 0 && t2.recs[0].hits > 0;
            if (launched || att == 2)
            {
                tr.recs.AddRange(t2.recs); tr.maxOffX = t2.maxOffX; tr.maxOffY = t2.maxOffY; tr.driftFrames = t2.driftFrames; tr.anchoredFrames = t2.anchoredFrames; tr.jumps = t2.jumps;
                ends.Append(e2);
                yield break;
            }
            ends.Append("[retry: the first launch did not reach the dummy] ");
        }
    }

    IEnumerator ChMobOnce(float kmh, float gap, string[] seq, ChTracker tr, StringBuilder ends)
    {
        if (aeChar == "mage") SetPrivate(pc, "mageLevel", 0);
        float w = 0f;
        while ((pc.IsAttacking || !pc.IsGrounded) && w < 3f) { yield return null; w += Time.deltaTime; }
        yield return AeSetSpeed(kmh);
        yield return new WaitForSeconds(0.25f);
        var en = HaSpawn(Mathf.Clamp(pc.CurrentAutoRunSpeed * 0.5f, 2.5f, 20f), "goblin");
        if (en == null) { L("[chain] mob could not be placed"); yield break; }
        float upReach = AeUpReach();
        w = 0f;
        while (en != null && en.isActiveAndEnabled && w < 4f)
        {
            if (en.ComboAimPoint.x - pc.transform.position.x <= upReach + pc.CurrentAutoRunSpeed * 0.04f) break;
            yield return null; w += Time.deltaTime;
        }
        tr.aim = () => en != null && en.isActiveAndEnabled ? en.ComboAimPoint : (Vector3?)null;
        tr.anchored = () => en != null && en.isActiveAndEnabled && en.IsComboAnchored;
        tr.jumps = new JumpMon { target = () => en != null && en.isActiveAndEnabled ? en.transform : null, state = () => en == null ? "-" : (en.IsLaunchedForCombo ? "air" : "gnd") + (en.IsComboAnchored ? "*" : "") };
        void OnHit(EnemyController e, PlayerAttackKind kind, bool wasLaunched, bool killed) { if (e == en) tr.OnTargetHit(); }
        EnemyController.LocalHit += OnHit;
        string lastEnd = en.ComboEndReason;
        int bs0 = EnemyController.ComboBlockedStep, bc0 = EnemyController.ComboBlockedCeiling, du0 = EnemyController.ComboDucked;
        var watch = StartCoroutine(ChWatch(() => { if (en != null && en.ComboEndReason != lastEnd) { lastEnd = en.ComboEndReason; if (!string.IsNullOrEmpty(lastEnd)) ends.Append(lastEnd + " "); } }));
        var jtick = StartCoroutine(ChJumpTick(tr));
        try { yield return ChRunSeq(seq, gap, tr, () => en != null && en.isActiveAndEnabled); }
        finally { EnemyController.LocalHit -= OnHit; StopCoroutine(watch); StopCoroutine(jtick); }
        int bs = EnemyController.ComboBlockedStep - bs0, bc = EnemyController.ComboBlockedCeiling - bc0, du = EnemyController.ComboDucked - du0;
        if (bs + bc + du > 0) ends.Append($"[blockStep={bs} blockCeil={bc} duck={du}] ");
        w = 0f;
        while (en != null && en.isActiveAndEnabled && en.IsLaunchedForCombo && w < 3f) { yield return null; w += Time.deltaTime; }
        if (en != null && en.gameObject.activeSelf) Destroy(en.gameObject);
        yield return new WaitForSeconds(0.3f);
    }

    IEnumerator ChWatch(System.Action a) { while (true) { a(); yield return null; } }

    // 描画の終わり(カメラの LateUpdate の後)に、プレイヤー/相手/カメラの位置を測る = 画面に映る位置どうしの変化
    IEnumerator ChJumpTick(ChTracker tr) { var eof = new WaitForEndOfFrame(); while (true) { yield return eof; if (tr.jumps != null && tr.pc != null) tr.jumps.Tick(tr.pc); } }

    // 2026-10-21: 走り始めの加速(ResumeAccel)が終わるまで速さを合わせ続ける。Run の直後の1件目は、加速の途中の遅い速さに合わせた
    //  倍率のまま加速が終わり、目標の数倍の速さ(1フレームで 6〜8m)になっていた(竜騎士 × 黒騎士で打ち上げが届かなかった原因)
    IEnumerator ChSettleKmh(float kmh)
    {
        float prev = -1f; int calm = 0;
        for (float w = 0f; w < 8f && calm < 20; w += Time.deltaTime)
        {
            SetKmh(kmh);
            yield return null;
            PlayerController.DebugSpeedScale = 1f; float nat = pc.CurrentAutoRunSpeed; SetKmh(kmh);
            calm = prev > 0f && Mathf.Abs(nat - prev) < prev * 0.002f ? calm + 1 : 0;
            prev = nat;
        }
    }

    IEnumerator ChBoss(string key, float kmh, float gap, string[] seq, ChTracker tr, StringBuilder ends)
    {
        if (!BaParse(key, out int fam, out int kind)) { L($"[chain] unknown boss {key}"); yield break; }
        yield return BfWaitEncounterEnd();
        if (aeChar == "mage") SetPrivate(pc, "mageLevel", 0);
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
        if (boss == null || !boss.Valid) { L($"[chain] boss {key} not spawned"); yield break; }
        SetKmh(kmh);
        yield return new WaitForSeconds(0.6f);
        yield return BaWaitCalm(boss);
        float kmhBefore = pc.CurrentAutoRunSpeed * GameManager.KmhPerMps;
        yield return ChSettleKmh(kmh);
        L($"[chain] speed {key} target={kmh:F0} beforeSettle={kmhBefore:F0} now={pc.CurrentAutoRunSpeed * GameManager.KmhPerMps:F0} dist={pc.transform.position.x:F0}");
        w = 0f; while ((!pc.IsGrounded || pc.IsAttacking) && w < 2f) { yield return null; w += Time.deltaTime; }
        boss.ForceBreak();
        yield return null;
        yield return new WaitForSecondsRealtime(0.2f);
        float near = boss.Style == "Launch" ? 1.25f : 2.6f;
        w = 0f; while (w < 2f && BaEdge(boss) > near) { yield return null; w += Time.deltaTime; }
        Component bc = boss.C;
        tr.aim = () => boss.Valid && !boss.IsDead && boss.Juggle != null ? boss.Juggle.ComboAimPoint : (Vector3?)null;
        tr.anchored = () => boss.Valid && boss.Juggle != null && boss.Juggle.IsComboAnchored;
        tr.jumps = new JumpMon
        {
            target = () => boss.Valid && !boss.IsDead ? bc.transform : null,
            split = () => boss.W != null ? boss.W.DebugMoveSplit : Vector4.zero,
            state = () => { var j = boss.Juggle; return (boss.Broken ? "B" : "") + (boss.Warned ? "w" : "") + (boss.Recovering ? "r" : "") + (j != null && j.Airborne ? "^" : "") + (j != null && j.Anchored ? "*" : "") + ":" + boss.Act; },
        };
        void OnHitW(WildBossBase b, PlayerAttackKind k, bool lethal) { if (b == bc) tr.OnTargetHit(); }
        void OnHitO(Component b, PlayerAttackKind k) { if (b == bc) tr.OnTargetHit(); }
        WildBossBase.QaLocalHit += OnHitW; DragonController.QaLocalHit += OnHitO; MajinController.QaLocalHit += OnHitO;
        string lastEnd = boss.Juggle != null ? boss.Juggle.EndReason : "";
        bool warnedNoted = false;
        var watch = StartCoroutine(ChWatch(() =>
        {
            var j = boss.Juggle;
            if (j != null && j.EndReason != lastEnd) { lastEnd = j.EndReason; if (!string.IsNullOrEmpty(lastEnd)) ends.Append(lastEnd + " "); }
            if (!warnedNoted && boss.Warned) { warnedNoted = true; ends.Append($"<warn@{tr.recs.Count}> "); }
        }));
        var jtick = StartCoroutine(ChJumpTick(tr));
        int fs0 = BossJuggle.Slams + BossJuggle.KneelSlams, fh0 = BossJuggle.FinishHits, fp0 = BossJuggle.FinishPushes;
        try
        {
            if (Arg("-chFinishAtWarn", "0") != "1") yield return ChRunSeq(seq, gap, tr, () => boss.Valid && !boss.IsDead && boss.Broken);
            else
            {
                // 締めの合図の試験: ↑→→→ をくり返して空中のコンボを続け、合図(回復の予告)が出たら 0.15秒後に ↓ を1回。
                // ↓ が当たったか、その後に叩きつけ(締め)が入ったかを見る
                string[] loop = { "U", "F", "F", "F" };
                tr.Begin(pc);
                ChRec d = null;
                try
                {
                    int i = 0;
                    while (boss.Valid && !boss.IsDead && boss.Broken && !boss.Warned && i < 24)
                    {
                        tr.Inject(i, loop[i % loop.Length]); i++;
                        float t = 0f; while (t < gap && !boss.Warned && boss.Broken) { yield return null; tr.Tick(); t += Time.deltaTime; }
                    }
                    if (boss.Warned && boss.Broken)
                    {
                        float t = 0f; while (t < 0.15f) { yield return null; tr.Tick(); t += Time.deltaTime; }
                        bool air = !pc.IsGrounded;
                        var jj = boss.Juggle;
                        var tmz = TerrainManager.Instance;
                        float pg = tmz != null ? tmz.GetSupportHeightAt(pc.transform.position.x) : 0f, bg = tmz != null ? tmz.GetSupportHeightAt(boss.Live.center.x) : 0f;
                        string at = $"remain={(boss.W != null ? boss.W.DebugBreakRemaining : -1f):F2} anchored={(jj != null && jj.Anchored)} bossAir={(jj != null && jj.Airborne)} style={boss.Style} pY={pc.transform.position.y:F1} pGnd={pg:F1} bGnd={bg:F1} hurtY={boss.Live.min.y:F1}..{boss.Live.max.y:F1}";
                        Vector3 aimAtD = jj != null ? jj.ComboAimPoint : Vector3.zero;
                        Vector2 relAtD = (Vector2)aimAtD - (Vector2)pc.transform.position;
                        d = tr.Inject(i, "D");
                        string atExec = "";
                        t = 0f;
                        while (t < 1.0f)
                        {
                            yield return null; tr.Tick(); t += Time.deltaTime;
                            if (atExec.Length == 0 && d.tExec >= 0f) atExec = $"exec: anchored={(boss.Juggle != null && boss.Juggle.Anchored)} broken={boss.Broken} remain={(boss.W != null ? boss.W.DebugBreakRemaining : -1f):F2} pAir={!pc.IsGrounded}";
                        }
                        int slams = BossJuggle.Slams + BossJuggle.KneelSlams - fs0;
                        string endWhy = boss.Juggle != null ? boss.Juggle.EndReason : "";
                        ends.Append($"[finishAtWarn D={ChTracker.Code(d)} fates={d.fates} lat={(d.tExec >= 0f ? d.tExec - d.tIn : -1f):F2} dx={d.dx:F2} dy={d.dy:F2} playerAir={air} slams={slams} finishHits={BossJuggle.FinishHits - fh0} pushes={BossJuggle.FinishPushes - fp0} atInput[{at} rel=({relAtD.x:F1},{relAtD.y:F1})] {atExec} endReason={endWhy} dead={boss.IsDead}] ");
                    }
                    else ends.Append("[finishAtWarn: no warn] ");
                }
                finally { tr.End(); }
            }
        }
        finally { WildBossBase.QaLocalHit -= OnHitW; DragonController.QaLocalHit -= OnHitO; MajinController.QaLocalHit -= OnHitO; StopCoroutine(watch); StopCoroutine(jtick); }
        if (!boss.Broken) ends.Append($"[break ended after {tr.recs.Count} inputs] ");
        if (boss.Valid && !boss.IsDead) boss.Kill();
        yield return BfWaitEncounterEnd();
    }
}
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
// 2026-10-21: 大型ボスの膝つきの見た目(改修前 = 縦につぶす / 改修後 = 脚を折って前へ倒れる)を撮る: -qaKneelShots <dir> [-ksBosses Wild/Cyclops,…]
// ボスごとに BREAK の前(立ち)・改修前の膝つき・改修後の膝つきを1枚ずつ。kneel.tsv にボスの画面の位置(切り抜き用)と被弾判定の範囲
public partial class QaSweep
{
    IEnumerator KneelShotsMode()
    {
        HookBossLogs();
        string[] bosses = Arg("-ksBosses", "Wild/Cyclops,Wild/Golem,Wild/Hydra,Cave/Troll,Cave/CrystalGolem,Sky/Behemoth,Sky/Titan,Sky/Leviathan,Sky/SkyGolem,Sky/MechDragon").Split(',');
        var tsv = new StringBuilder("boss\tshot\tfile\tvx0\tvy0\tvx1\tvy1\thurtMinY\thurtMaxY\thurtMinX\thurtMaxX\n");
        foreach (var grp in bosses.GroupBy(b => b.Split('/')[0]))
        {
            string stage = grp.Key == "Cave" ? "natural_cave" : grp.Key == "Sky" ? "sky_corridor" : "wasteland_road";
            yield return BeginRun("swordsman", stage);
            SetGod(true);
            GameManager.BlockExpGain = true;
            if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
            TerrainManager.Instance.pitChanceBase = 0f; TerrainManager.Instance.pitChanceMax = 0f;
            TerrainManager.Instance.enemySpawnChance = 0f;
            if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
            yield return new WaitForSeconds(1f);
            foreach (var key in grp)
            {
                if (!BaParse(key, out int fam, out int kind)) continue;
                yield return BfWaitEncounterEnd();
                BossManager.Instance.DebugSpawnBossForTest(fam, kind);
                SetKmh(30f);
                BaBoss boss = null; float w = 0f;
                while (w < 15f) { yield return null; w += Time.unscaledDeltaTime; boss = BaFind(); if (boss.Valid) boss.ScaleHp(40f); if (boss.W != null && !boss.W.IsEntering && w > 2f) break; if ((boss.D != null || boss.M != null) && w > 4.5f) break; }
                if (boss == null || !boss.Valid) continue;
                yield return BaWaitCalm(boss);
                foreach (string shot in new[] { "stand", "legacy", "new" })
                {
                    if (shot != "stand")
                    {
                        ComboRevision.Legacy = shot == "legacy";
                        yield return BaWaitCalm(boss);
                        boss.ForceBreak();
                        yield return new WaitForSeconds(0.9f);
                    }
                    var cam = Camera.main;
                    var hb = boss.Live;
                    Vector3 a = cam.WorldToViewportPoint(new Vector3(hb.min.x - 2.5f, hb.min.y - 0.5f, 0f)), b = cam.WorldToViewportPoint(new Vector3(hb.max.x + 2.5f, hb.max.y + 2.5f, 0f));
                    string name = $"{key.Replace('/', '_')}_{shot}.png";
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, name));
                    tsv.Append($"{key}\t{shot}\t{name}\t{a.x:F3}\t{a.y:F3}\t{b.x:F3}\t{b.y:F3}\t{hb.min.y:F2}\t{hb.max.y:F2}\t{hb.min.x:F2}\t{hb.max.x:F2}\n");
                    L($"[kneel] {key} {shot} style={boss.Style} hurt y {hb.min.y:F2}..{hb.max.y:F2} h={hb.size.y:F2}");
                    yield return new WaitForSeconds(0.3f);
                    if (shot != "stand") { float ww = 0f; while ((boss.Broken || boss.Recovering) && ww < 8f) { yield return null; ww += Time.deltaTime; } }
                }
                ComboRevision.Legacy = false;
                System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "kneel.tsv"), tsv.ToString());
                if (boss.Valid && !boss.IsDead) boss.Kill();
                yield return BfWaitEncounterEnd();
            }
            GameManager.BlockExpGain = false;
            SetGod(false);
            yield return EndRun();
        }
    }
}
#endif
