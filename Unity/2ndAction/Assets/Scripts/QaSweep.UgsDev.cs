#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudCode;
using Unity.Services.Core;
using Unity.Services.Leaderboards;
using UnityEngine;

// 本物の UGS(development)でのランキング・認証・データの削除の確認(2026-10-20、マスター依頼)。production では動かない。
//  -qaUgsDev <dir> -ugsLiveConfirm development-ready -udPhase <段> -ugsProfile <プレイヤー> -fileSave <専用の保存>
//   boot : オンラインを使わずに起動 → アカウントを作らない / 削除の画面を開いても作らない
//   a1   : プレイヤー A。送信(受け付け)/ 上位・自分の順位・自己ベスト・metadata / 低い記録で下がらない / 直接書き込みの拒否
//   b1   : プレイヤー B。A の記録が見える(別のプレイヤーからの確認)/ B の順位
//   a2   : A の再起動。同じ PlayerId で、サーバーの記録をもう一度取れる
//   x1   : プレイヤー C。削除の処理に他人の ID を渡す(使われないこと。結果は Editor の確認ツールでサーバーを読む)
//   down : つながらない環境(-ugsEnv qa-missing)。オンラインは使えないと出て、ゲームとセーブは動く
//   del  : A の削除(本物の画面の経路)→ 成功ならアカウントが消え、次のオンラインで新しい PlayerId / 失敗なら何も消えない
//  完全な PlayerId は <dir>/ids.txt にだけ書く(ログには先頭だけ)。PlayerId は公開の識別子で、秘密ではない
public partial class QaSweep
{
    [System.Serializable] class DelReply { public bool ok; public int cloudSave; public int leaderboards; public string message = ""; }

    void UdId(string tag, string id)
    {
        try { File.AppendAllText(Path.Combine(outDir, "ids.txt"), $"{tag}\t{id}\n"); } catch { }
    }
    static string UdRead(string dir, string tag)
    {
        try { foreach (var l in File.ReadAllLines(Path.Combine(dir, "ids.txt"))) { var p = l.Split('\t'); if (p.Length == 2 && p[0] == tag) return p[1]; } } catch { }
        return "";
    }

    IEnumerator UgsDevMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        yield return new WaitForSecondsRealtime(1f);
        string phase = Arg("-udPhase", "");
        bool safe = FileSaveStore.Active && !UgsEnvironment.IsProduction && Arg("-ugsLiveConfirm", "") == "development-ready";
        Check(safe, $"runs only on a dedicated save file, not on production ({UgsEnvironment.Describe()})");
        if (!safe) yield break;
        L($"[dev] phase {phase}: {UgsEnvironment.Describe()}");
        string shared = Arg("-udShared", outDir); // ids.txt を置く所(段をまたいで使う)
        var ugs = new UgsLeaderboardBackend();
        long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Leaderboard.Entry E(int m, string stage = "wasteland_road", string ch = "swordsman") => new Leaderboard.Entry
        {
            runId = System.Guid.NewGuid().ToString("N"), stageId = stage, characterId = ch, meters = m, playSeconds = m / 20f + 30f,
            startedUtc = now - (long)(m / 20f) - 120, committedUtc = now, env = UgsEnvironment.Name, appVersion = Application.version,
        };

        if (phase == "boot")
        {
            yield return new WaitForSecondsRealtime(8f); // ホームで待つ(30秒ごとの再送などが勝手にサインインしないか)
            bool init = UnityServices.State == ServicesInitializationState.Initialized;
            bool signed = init && AuthenticationService.Instance.IsSignedIn;
            L($"[app] boot: services initialized={init} signed in={signed} online status={OnlineServices.Status}");
            Check(!signed && OnlineServices.Status != OnlineServices.State.Ready, "boot: starting the game (single player) does not sign in / create an account");
            DataDeletionUi.Open();
            float w = 0f; while ((AccountDeletion.Busy || AccountDeletion.State == AccountDeletion.Phase.Idle) && w < 30f) { yield return null; w += Time.unscaledDeltaTime; }
            yield return new WaitForSecondsRealtime(0.5f);
            Shot("ud_boot_delete_screen");
            yield return null; yield return null;
            bool tok = UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.SessionTokenExists;
            bool signed2 = UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn;
            L($"[server] boot: deletion screen state={AccountDeletion.State} signed in={signed2} session token={tok} detail='{AccountDeletion.Detail}'");
            Check(AccountDeletion.State == AccountDeletion.Phase.NoAccount && !signed2 && !tok, "boot: opening the deletion screen on a fresh device does not create an account (NoAccount)");
            DataDeletionUi.Close();
            yield break;
        }

        if (phase == "down")
        {
            // サービスにつながらない時(-ugsEnv に無い環境を指定して再現): オンラインは「使えない」になり、ゲームは止まらない
            var td = OnlineServices.EnsureReadyAsync();
            yield return WaitTask(td, 60f);
            L($"[app] down: sign-in {(td.IsCompleted ? td.Result.ToString() : "timeout")} status={OnlineServices.Status} text='{OnlineServices.ErrorText}' detail='{OnlineServices.ErrorDetail}'");
            Check(td.IsCompleted && !td.Result && OnlineServices.Status == OnlineServices.State.Unavailable, "down: online is reported unavailable (no crash, no other environment)");
            var tl = ugs.Load("wasteland_road", 10);
            yield return WaitTask(tl, 30f);
            Check(tl.IsCompleted && tl.IsFaulted, "down: the ranking read fails cleanly (the ranking screen shows an error)");
            var e = E(1500);
            var tsub = ugs.Submit(e, "QA Down");
            yield return WaitTask(tsub, 30f);
            Check(tsub.IsCompleted && !tsub.IsFaulted && tsub.Result.status == Leaderboard.SubmitStatus.Failed, $"down: a ranking submit fails and stays pending ({(tsub.IsCompleted && !tsub.IsFaulted ? tsub.Result.message : "fault")})");
            DataDeletionUi.Open();
            float w = 0f; while ((AccountDeletion.Busy || AccountDeletion.State == AccountDeletion.Phase.Idle) && w < 60f) { yield return null; w += Time.unscaledDeltaTime; }
            L($"[app] down: deletion screen state={AccountDeletion.State}");
            // この端末にアカウントが無ければ「アカウントなし」(初期化はサーバーに尋ねない)、あれば「接続できません」。どちらも削除へ進まない
            Check(AccountDeletion.State == AccountDeletion.Phase.Unavailable || AccountDeletion.State == AccountDeletion.Phase.NoAccount, "down: the deletion screen does not proceed (no account / cannot connect)");
            DataDeletionUi.Close();
            yield return new WaitForSecondsRealtime(0.5f);
            Check(GameManager.Instance != null && Time.timeScale > 0f, "down: the game keeps running (home screen alive)");
            // 端末のセーブは書ける
            SaveStore.SetInt("QA.UgsDown.Probe", 7); SaveStore.Save();
            Check(SaveStore.GetInt("QA.UgsDown.Probe", 0) == 7, "down: the device save still works");
            SaveStore.DeleteKey("QA.UgsDown.Probe"); SaveStore.Save();
            yield break;
        }

        // 以降は、ONLINE を使った時のサインイン
        var ti = OnlineServices.EnsureReadyAsync();
        yield return WaitTask(ti, 40f);
        bool ready = ti.IsCompleted && ti.Result;
        string me = OnlineServices.PlayerId;
        L($"[server] sign-in: {(ready ? "ok" : "failed")} env={OnlineServices.EnvironmentName} player={OnlineServices.ShortId(me)} detail='{OnlineServices.ErrorDetail}'");
        Check(ready && OnlineServices.EnvironmentName == "development", $"{phase}: signed in on development");
        if (!ready) yield break;

        IEnumerator Send(Leaderboard.Entry e, string name, List<Leaderboard.SubmitResult> outR)
        {
            var t = ugs.Submit(e, name);
            yield return WaitTask(t);
            outR.Add(t.IsCompleted && !t.IsFaulted ? t.Result : new Leaderboard.SubmitResult(Leaderboard.SubmitStatus.Failed, "timeout"));
        }
        IEnumerator Load(string stage, List<Leaderboard.Page> outP)
        {
            var t = ugs.Load(stage, 50);
            yield return WaitTask(t);
            outP.Add(t.IsCompleted && !t.IsFaulted ? t.Result : null);
            if (t.IsFaulted) L($"[server] load failed: {t.Exception?.GetBaseException().GetType().Name}: {FirstLineOf(t.Exception?.GetBaseException().Message)}");
        }
        string Row(Leaderboard.Row r) => r == null ? "-" : $"#{r.rank} {r.meters}m '{r.name}' {r.characterId} auto={r.usedAuto} sprint={r.usedSprint} {(r.isMe ? "(me)" : OnlineServices.ShortId(r.playerId))}";

        if (phase == "a1")
        {
            UdId("A", me);
            int m1 = 2000 + (int)(now % 900), m2 = m1 + 700;
            var res = new List<Leaderboard.SubmitResult>(); var pages = new List<Leaderboard.Page>();
            var e1 = E(m1); e1.usedAuto = true;
            yield return Send(e1, "QA Alpha", res);
            L($"[server] a1 submit {m1}m (auto) -> {res[0].status} {res[0].message}");
            Check(res[0].status == Leaderboard.SubmitStatus.Accepted, "a1: the server accepts a valid solo run");
            yield return Load("wasteland_road", pages);
            var p = pages[0];
            L($"[server] a1 top {(p != null ? p.total.ToString() : "-")}: {(p != null ? string.Join(" | ", p.top.ConvertAll(Row)) : "-")}");
            L($"[server] a1 my rank: {Row(p?.me)}");
            Check(p != null && p.top.Exists(x => x.isMe && x.meters == m1), "a1: Top ranking contains my record");
            Check(p?.me != null && p.me.meters == m1 && p.me.rank >= 1, "a1: My Rank / My Best are returned");
            Check(p?.me != null && p.me.name == "QA Alpha" && p.me.characterId == "swordsman" && p.me.usedAuto && !p.me.usedSprint, "a1: metadata (codename / character / auto / sprint) is returned");
            res.Clear(); yield return Send(E(m1 - 300), "QA Alpha", res);
            L($"[server] a1 lower {m1 - 300}m -> {res[0].status} {res[0].message}");
            Check(res[0].status == Leaderboard.SubmitStatus.NotBetter, "a1: a lower score is answered 'not better'");
            pages.Clear(); yield return Load("wasteland_road", pages);
            Check(pages[0]?.me != null && pages[0].me.meters == m1, $"a1: the best does not go down ({Row(pages[0]?.me)})");
            res.Clear(); var e2 = E(m2, "wasteland_road", "ninja"); e2.usedSprint = false;
            yield return Send(e2, "QA Alpha", res);
            pages.Clear(); yield return Load("wasteland_road", pages);
            L($"[server] a1 higher {m2}m -> {res[0].status}; me {Row(pages[0]?.me)}");
            Check(res[0].status == Leaderboard.SubmitStatus.Accepted && pages[0]?.me != null && pages[0].me.meters == m2 && pages[0].me.characterId == "ninja", "a1: a longer run replaces the best");
            res.Clear(); yield return Send(E(1500, "natural_cave", "archer"), "QA Alpha", res);
            L($"[server] a1 cave 1500m -> {res[0].status}");
            Check(res[0].status == Leaderboard.SubmitStatus.Accepted, "a1: a second map is accepted too");
            // 直接書き込み(Access Control で禁止)
            var td = LeaderboardsService.Instance.AddPlayerScoreAsync(Leaderboard.BoardId("sky_corridor"), 1999999);
            yield return WaitTask(td);
            string why = td.IsCompleted && td.IsFaulted ? td.Exception.GetBaseException().GetType().Name + ": " + FirstLineOf(td.Exception.GetBaseException().Message) : (td.IsCompleted ? "WRITTEN (not denied)" : "timeout");
            L($"[server] a1 direct write by the player: {why}");
            Check(td.IsCompleted && td.IsFaulted, "a1: a direct write by the player is denied (Access Control)");
            UdId("A_best", m2.ToString());
            yield break;
        }

        if (phase == "b1")
        {
            UdId("B", me);
            string a = UdRead(shared, "A"); int aBest = int.TryParse(UdRead(shared, "A_best"), out var ab) ? ab : -1;
            var res = new List<Leaderboard.SubmitResult>(); var pages = new List<Leaderboard.Page>();
            yield return Send(E(1200 + (int)(now % 300), "wasteland_road", "mage"), "QA Bravo", res);
            L($"[server] b1 submit -> {res[0].status} {res[0].message}");
            Check(res[0].status == Leaderboard.SubmitStatus.Accepted, "b1: player B's run is accepted");
            yield return Load("wasteland_road", pages);
            var p = pages[0];
            var aRow = p?.top.Find(x => x.playerId == a);
            L($"[server] b1 view: A {Row(aRow)}; me {Row(p?.me)}; total {p?.total}");
            Check(aRow != null && aRow.meters == aBest && aRow.name == "QA Alpha" && !aRow.isMe, "b1: player B sees player A's record (distance / codename)");
            Check(p?.me != null && p.me.name == "QA Bravo" && (aRow == null || p.me.rank > aRow.rank), "b1: player B's own rank is below A");
            yield break;
        }

        if (phase == "a2")
        {
            string a = UdRead(shared, "A"); int aBest = int.TryParse(UdRead(shared, "A_best"), out var ab) ? ab : -1;
            L($"[server] a2 same player after restart: {OnlineServices.ShortId(me)} (A {OnlineServices.ShortId(a)})");
            Check(me == a, "a2: after a restart the same anonymous player signs in");
            var pages = new List<Leaderboard.Page>();
            yield return Load("wasteland_road", pages);
            L($"[server] a2 my best from the cloud: {Row(pages[0]?.me)}");
            Check(pages[0]?.me != null && pages[0].me.meters == aBest, "a2: the cloud record is read back after a restart");
            yield break;
        }

        if (phase == "x1")
        {
            UdId("C", me);
            string a = UdRead(shared, "A");
            var tc = CloudCodeService.Instance.CallEndpointAsync(AccountDeletion.DeleteScript, new Dictionary<string, object> { { "playerId", a } });
            yield return WaitTask(tc);
            string got = tc.IsCompleted && !tc.IsFaulted ? tc.Result : "error " + tc.Exception?.GetBaseException().GetType().Name + ": " + FirstLineOf(tc.Exception?.GetBaseException().Message);
            L($"[server] x1 player C calls {AccountDeletion.DeleteScript} with A's id -> {got}");
            var pages = new List<Leaderboard.Page>();
            yield return Load("wasteland_road", pages);
            var aRow = pages[0]?.top.Find(x => x.playerId == a);
            L($"[server] x1 A's record afterwards: {Row(aRow)}");
            Check(aRow != null, "x1: player A's record is still there after player C passed A's id");
            yield break;
        }

        if (phase == "del")
        {
            string a = UdRead(shared, "A");
            Check(me == a, "del: signed in as player A");
            Leaderboard.Join(); // 端末のランキングの参加(削除で消える4項目のうちの1つ)
            var before = SnapshotSave();
            DataDeletionUi.Open();
            float w = 0f; while ((AccountDeletion.Busy || AccountDeletion.State == AccountDeletion.Phase.Idle) && w < 30f) { yield return null; w += Time.unscaledDeltaTime; }
            yield return new WaitForSecondsRealtime(0.4f);
            Shot("ud_del_ready");
            yield return null; yield return null;
            L($"[app] del screen: state={AccountDeletion.State} id shown={OnlineServices.ShortId(AccountDeletion.PlayerId)}");
            Check(AccountDeletion.State == AccountDeletion.Phase.Ready && AccountDeletion.PlayerId == a, "del: the screen shows player A's id");
            AccountDeletion.AskConfirm();
            yield return new WaitForSecondsRealtime(0.3f);
            Shot("ud_del_confirm");
            yield return null; yield return null;
            AccountDeletion.Run();
            w = 0f; while (AccountDeletion.Busy && w < 40f) { yield return null; w += Time.unscaledDeltaTime; }
            yield return new WaitForSecondsRealtime(0.4f);
            Shot("ud_del_result");
            yield return null; yield return null;
            var changed = Diff(before, SnapshotSave());
            L($"[server] del result: state={AccountDeletion.State} message='{AccountDeletion.Message}' detail='{AccountDeletion.Detail}' changed=[{string.Join(",", changed)}]");
            DataDeletionUi.Close();
            if (AccountDeletion.State == AccountDeletion.Phase.Done)
            {
                var expected = new HashSet<string> { Leaderboard.JoinedKey, Leaderboard.PendingKey, Leaderboard.VerifiedKey, Leaderboard.SentKey };
                bool onlyRanking = true; foreach (var k in changed) if (!expected.Contains(k)) onlyRanking = false;
                Check(onlyRanking, $"del: only the device's ranking keys changed (cards/MILE/progress/settings/purchases kept): [{string.Join(",", changed)}]");
                bool signed = AuthenticationService.Instance.IsSignedIn, tok = AuthenticationService.Instance.SessionTokenExists;
                Check(!signed && !tok, $"del: signed out and the session is forgotten (signed {signed}, token {tok})");
                var t2 = OnlineServices.EnsureReadyAsync();
                yield return WaitTask(t2, 40f);
                string nid = OnlineServices.PlayerId;
                UdId("A_new", nid);
                L($"[server] del: next online use -> player {OnlineServices.ShortId(nid)} (was {OnlineServices.ShortId(a)})");
                Check(t2.IsCompleted && t2.Result && !string.IsNullOrEmpty(nid) && nid != a, "del: the next online use signs in with a new anonymous player id");
                var pages = new List<Leaderboard.Page>();
                yield return Load("wasteland_road", pages);
                Check(pages[0] != null && !pages[0].top.Exists(x => x.playerId == a), "del: player A's record is gone from the ranking");
            }
            else
            {
                Check(changed.Count == 0, $"del (failed): nothing on the device changed: [{string.Join(",", changed)}]");
                var t3 = OnlineServices.EnsureReadyAsync();
                yield return WaitTask(t3, 40f);
                Check(OnlineServices.PlayerId == a, "del (failed): the account is kept (still player A, so the deletion can be retried)");
                var pages = new List<Leaderboard.Page>();
                yield return Load("wasteland_road", pages);
                Check(pages[0] != null && pages[0].top.Exists(x => x.playerId == a), "del (failed): player A's server record is kept");
                Check(false, "del: the deletion did not complete (" + AccountDeletion.Detail + ")");
            }
            yield break;
        }
        Check(false, "unknown -udPhase " + phase);
    }
}
#endif
