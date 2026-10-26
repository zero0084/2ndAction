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
//  2026-10-26(OMM_ADMIN_BASIC の登録の後の本番どおりの確認。-udWho で名札、既定 D):
//   d1     : 端末の保存に試験用の値 + 2つのマップの記録 + コードネームの掲載と変更
//   x1     : -udTarget の相手の ID を渡しても消えない + 秘密が読めて本人の分だけ動いた
//   ddel   : 本物の画面の経路で削除(-udFail cloudcode|auth で本物のサービスで1回失敗 → 再試行)。端末の保存はランキングの4項目だけ変わる
//   dafter : 削除の後の再起動。起動・削除の画面ではアカウントを作らない → ランキングを初めて使った時に新しい PlayerId → 送信できる
//  完全な PlayerId は <dir>/ids.txt にだけ書く(ログには先頭だけ)。PlayerId は公開の識別子で、秘密ではない
public partial class QaSweep
{
    [System.Serializable] class DelReply { public bool ok; public int cloudSave; public int leaderboards; public string message = ""; }

    void UdId(string tag, string id)
    {
        try { File.AppendAllText(Path.Combine(Arg("-udShared", outDir), "ids.txt"), $"{tag}\t{id}\n"); } catch { } // 段をまたいで読む所(既定は自分の出力先)
    }
    static string UdRead(string dir, string tag)
    {
        try { foreach (var l in File.ReadAllLines(Path.Combine(dir, "ids.txt"))) { var p = l.Split('\t'); if (p.Length == 2 && p[0] == tag) return p[1]; } } catch { }
        return "";
    }

    // 起動しただけ / 削除の画面を開いただけでは、サインインもアカウントの作成もしない(新しい端末・削除の後の端末)
    IEnumerator UdNoAccountYet(string tag)
    {
        yield return new WaitForSecondsRealtime(8f); // ホームで待つ(30秒ごとの再送などが勝手にサインインしないか)
        bool init = UnityServices.State == ServicesInitializationState.Initialized;
        bool signed = init && AuthenticationService.Instance.IsSignedIn;
        L($"[app] {tag}: services initialized={init} signed in={signed} online status={OnlineServices.Status}");
        Check(!signed && OnlineServices.Status != OnlineServices.State.Ready, $"{tag}: starting the game (single player) does not sign in / create an account");
        DataDeletionUi.Open();
        float w = 0f; while ((AccountDeletion.Busy || AccountDeletion.State == AccountDeletion.Phase.Idle) && w < 30f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.5f);
        Shot($"ud_{tag}_delete_screen");
        yield return null; yield return null;
        bool tok = UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.SessionTokenExists;
        bool signed2 = UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn;
        L($"[server] {tag}: deletion screen state={AccountDeletion.State} signed in={signed2} session token={tok} detail='{AccountDeletion.Detail}'");
        Check(AccountDeletion.State == AccountDeletion.Phase.NoAccount && !signed2 && !tok, $"{tag}: opening the deletion screen does not create an account (NoAccount)");
        DataDeletionUi.Close();
        yield return new WaitForSecondsRealtime(0.3f);
    }

    // 削除の確認用の、端末の保存の試験用の値(カード/MILE/進行/解放/設定/自己ベスト/広告なしパスの控え)。専用の保存ファイルだけに書く
    static void UdSeedLocal()
    {
        SaveStore.SetInt("TotalOwnedMile", 12345);
        SaveStore.SetString("OwnedCardsV1", "{\"stacks\":[{\"cardId\":\"attack_up\",\"level\":3,\"count\":2},{\"cardId\":\"v2|speed_up|x\",\"level\":5,\"count\":1}]}");
        SaveStore.SetString("DeckCardIds", "attack_up,attack_up");
        SaveStore.SetString("UnlockedIds", "unlock_a,unlock_b");
        SaveStore.SetString("SelectedCharacterId", "ninja");
        SaveStore.SetString(SaveKeys.StageBestPrefix + "wasteland_road", "45678.5");
        SaveStore.SetString(SaveKeys.LifetimeDistance, "98765.4");
        SaveStore.SetFloat("MasterVolume", 0.3f);
        SaveStore.SetInt("ScreenShakeEnabled", 0);
        SaveStore.SetString(NoAdsPass.Key, "{\"owned\":true,\"tokenHash\":\"qa-test\",\"grantedUtc\":1,\"lastConfirmedUtc\":1,\"absentChecks\":0,\"pending\":false}");
        SaveStore.Save();
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
            yield return UdNoAccountYet("boot");
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

        if (phase == "dafter")
        {
            // 削除の後で同じ端末を起動し直す(サインインの記録が消えている)。起動・削除の画面だけではアカウントを作らず、
            //  ランキング(Cloud の機能)を初めて使った時に新しい匿名プレイヤーになる
            string who = Arg("-udWho", "D");
            string old = UdRead(shared, who);
            yield return UdNoAccountYet("after");
            var res = new List<Leaderboard.SubmitResult>(); var pages = new List<Leaderboard.Page>();
            yield return Load("wasteland_road", pages); // ランキングの画面と同じ読み込み(中でサインインする)
            string nid = OnlineServices.PlayerId;
            L($"[server] after: first ranking use -> player {OnlineServices.ShortId(nid)} env={OnlineServices.EnvironmentName} (was {OnlineServices.ShortId(old)})");
            Check(!string.IsNullOrEmpty(nid) && nid != old && OnlineServices.EnvironmentName == "development", $"{who} after: the first cloud use issued a new anonymous player id on development");
            UdId(who + "_new", nid);
            Check(pages[0] != null, "after: the ranking can be read");
            Check(pages[0] != null && !pages[0].top.Exists(x => x.playerId == old), $"after: the deleted player's record is not in the ranking");
            Check(pages[0] != null && pages[0].me == null, "after: the new player has no record yet (nothing carried over)");
            int m = 1100 + (int)(now % 200);
            yield return Send(E(m, "wasteland_road", "swordsman"), Codename.Current, res);
            L($"[server] after: new player submit {m}m -> {res[0].status} {res[0].message}");
            Check(res[0].status == Leaderboard.SubmitStatus.Accepted, "after: the new player can send a score");
            pages.Clear(); yield return Load("wasteland_road", pages);
            L($"[server] after: new player row {Row(pages[0]?.me)}");
            Check(pages[0]?.me != null && pages[0].me.meters == m, "after: the new player's score is in the ranking");
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
            // 2026-10-26: 相手は -udTarget の名札(既定 A)。ID を渡しそうな名前を全部試す(スクリプトは引数を宣言していないので届かない)
            string tgt = Arg("-udTarget", "A");
            UdId(Arg("-udWho", "C"), me);
            string a = UdRead(shared, tgt);
            Check(!string.IsNullOrEmpty(a) && a != me, $"x1: the target player {tgt} is someone else");
            var tc = CloudCodeService.Instance.CallEndpointAsync(AccountDeletion.DeleteScript, new Dictionary<string, object> { { "playerId", a }, { "targetPlayerId", a }, { "id", a }, { "player", a } });
            yield return WaitTask(tc);
            string got = tc.IsCompleted && !tc.IsFaulted ? tc.Result : "error " + tc.Exception?.GetBaseException().GetType().Name + ": " + FirstLineOf(tc.Exception?.GetBaseException().Message);
            var rep = tc.IsCompleted && !tc.IsFaulted ? JsonUtility.FromJson<DelReply>(tc.Result ?? "{}") : null;
            L($"[server] x1 caller {OnlineServices.ShortId(me)} calls {AccountDeletion.DeleteScript} with {tgt}'s id -> ok={rep?.ok} cloudSave={rep?.cloudSave} leaderboards={rep?.leaderboards} message='{rep?.message}'");
            // 秘密(OMM_ADMIN_BASIC)はスクリプトが最初に読む。読めないと何も消さずに "not configured" を返す → ok なら読めている
            Check(rep != null && rep.ok && rep.leaderboards == 1, "x1: the script read OMM_ADMIN_BASIC and ran (it only touched the caller's own data)");
            var pages = new List<Leaderboard.Page>();
            yield return Load("wasteland_road", pages);
            var aRow = pages[0]?.top.Find(x => x.playerId == a);
            L($"[server] x1 {tgt}'s record afterwards: {Row(aRow)}");
            Check(aRow != null, $"x1: player {tgt}'s record is still there after another player passed {tgt}'s id");
            yield break;
        }

        if (phase == "d1")
        {
            // 削除の試験のプレイヤー(-udWho、既定 D): 端末の保存の試験用の値 / 2つのマップの記録 / コードネームの掲載と変更
            string who = Arg("-udWho", "D");
            UdId(who, me);
            UdSeedLocal();
            Codename.Set("QA " + who);
            int m1 = 1800 + (int)(now % 500);
            var res = new List<Leaderboard.SubmitResult>(); var pages = new List<Leaderboard.Page>();
            yield return Send(E(m1, "wasteland_road", "ninja"), Codename.Current, res);
            yield return Send(E(1300, "natural_cave", "archer"), Codename.Current, res);
            L($"[server] {who} submit {m1}m wasteland -> {res[0].status}; 1300m cave -> {res[1].status}");
            Check(res[0].status == Leaderboard.SubmitStatus.Accepted && res[1].status == Leaderboard.SubmitStatus.Accepted, $"{who}: two records accepted (two maps)");
            string renamed = Codename.Set("QA " + who + " Two");
            var tn = ugs.UpdateName(renamed);
            yield return WaitTask(tn);
            Check(tn.IsCompleted && !tn.IsFaulted && tn.Result, $"{who}: codename change reached the server (omm_set_name)");
            yield return Load("wasteland_road", pages);
            L($"[server] {who} my row: {Row(pages[0]?.me)}");
            Check(pages[0]?.me != null && pages[0].me.meters == m1 && pages[0].me.name == renamed, $"{who}: the ranking shows the record with the new codename");
            Check(pages[0]?.me != null && pages[0].me.characterId == "ninja", $"{who}: the codename change keeps the character/auto/sprint metadata");
            Leaderboard.Join();
            UdId(who + "_best", m1.ToString());
            yield break;
        }


        if (phase == "ddel")
        {
            // 2026-10-26: 本物の画面の経路で削除(-udWho 既定 D)。-udFail cloudcode|auth なら、本物のサービスで1回失敗させてから再試行する。
            //  削除の後はサインインし直さない(新しい PlayerId は、起動し直した dafter で Cloud の機能を初めて使った時に確かめる)
            string who = Arg("-udWho", "D");
            string a = UdRead(shared, who);
            Check(me == a, $"ddel: signed in as player {who}");
            var before = SnapshotSave();
            DataDeletionUi.Open();
            float w = 0f; while ((AccountDeletion.Busy || AccountDeletion.State == AccountDeletion.Phase.Idle) && w < 30f) { yield return null; w += Time.unscaledDeltaTime; }
            yield return new WaitForSecondsRealtime(0.4f);
            Shot($"ud_{who}_ready");
            yield return null; yield return null;
            L($"[app] {who} deletion screen: state={AccountDeletion.State} id shown={OnlineServices.ShortId(AccountDeletion.PlayerId)}");
            Check(AccountDeletion.State == AccountDeletion.Phase.Ready && AccountDeletion.PlayerId == a, $"{who}: the screen shows the player's own id");
            var pages = new List<Leaderboard.Page>();
            string fail = Arg("-udFail", "");
            if (fail != "")
            {
                AccountDeletion.DebugFailOnce = fail;
                AccountDeletion.AskConfirm(); yield return null;
                AccountDeletion.Run();
                w = 0f; while (AccountDeletion.Busy && w < 40f) { yield return null; w += Time.unscaledDeltaTime; }
                yield return new WaitForSecondsRealtime(0.4f);
                Shot($"ud_{who}_failed_{fail}");
                yield return null; yield return null;
                var ch = Diff(before, SnapshotSave());
                L($"[server] {who} induced failure '{fail}': state={AccountDeletion.State} message='{AccountDeletion.Message}' detail='{AccountDeletion.Detail}' changed=[{string.Join(",", ch)}]");
                Check(AccountDeletion.State == AccountDeletion.Phase.Failed && !string.IsNullOrEmpty(AccountDeletion.Message), $"fail {fail}: shown as a failure on the screen");
                Check(ch.Count == 0, $"fail {fail}: nothing on the device changed: [{string.Join(",", ch)}]");
                Check(GameManager.Instance != null && Time.timeScale > 0f && !AccountDeletion.Busy, $"fail {fail}: the app does not freeze (home alive)");
                var tr = OnlineServices.EnsureReadyAsync();
                yield return WaitTask(tr, 40f);
                Check(tr.IsCompleted && tr.Result && OnlineServices.PlayerId == a, $"fail {fail}: the account is kept (same player, so it can be retried)");
                yield return Load("wasteland_road", pages);
                bool rec = pages[0] != null && pages[0].top.Exists(x => x.playerId == a);
                L($"[server] {who} after the failure: record in the ranking={rec}");
                // cloudcode: サーバーまで届かない → 何も消えない / auth: サーバーのデータは先に消えた → アカウントだけ残る(本人がもう一度消せる)
                Check(fail == "cloudcode" ? rec : !rec, fail == "cloudcode" ? "fail cloudcode: the server data is kept" : "fail auth: the server data was deleted first; only the account is left (retry possible)");
                AccountDeletion.AskConfirm();
                Check(AccountDeletion.State == AccountDeletion.Phase.Confirm, $"fail {fail}: the delete button works again (retry)");
                if (Arg("-udRetry", "1") == "0") { AccountDeletion.CancelConfirm(); DataDeletionUi.Close(); L($"[app] {who} stopped after the failure (no retry in this run)"); yield break; }
                L($"[app] {who} retrying the deletion");
            }
            else AccountDeletion.AskConfirm();
            yield return new WaitForSecondsRealtime(0.3f);
            Shot($"ud_{who}_confirm");
            yield return null; yield return null;
            AccountDeletion.Run();
            w = 0f; while (AccountDeletion.Busy && w < 40f) { yield return null; w += Time.unscaledDeltaTime; }
            yield return new WaitForSecondsRealtime(0.4f);
            Shot($"ud_{who}_result");
            yield return null; yield return null;
            var changed = Diff(before, SnapshotSave());
            L($"[server] {who} del result: state={AccountDeletion.State} message='{AccountDeletion.Message}' detail='{AccountDeletion.Detail}' changed=[{string.Join(",", changed)}]");
            Check(AccountDeletion.State == AccountDeletion.Phase.Done, $"{who}: the deletion completed");
            var expected = new HashSet<string> { Leaderboard.JoinedKey, Leaderboard.PendingKey, Leaderboard.VerifiedKey, Leaderboard.SentKey };
            bool onlyRanking = true; foreach (var k in changed) if (!expected.Contains(k)) onlyRanking = false;
            Check(onlyRanking, $"{who}: only the device's ranking keys changed (cards/MILE/progress/unlocks/settings/best/ad-free pass kept): [{string.Join(",", changed)}]");
            bool signed = AuthenticationService.Instance.IsSignedIn, tok = AuthenticationService.Instance.SessionTokenExists;
            Check(!signed && !tok, $"{who}: signed out and the sign-in record is forgotten (signed {signed}, token {tok})");
            DataDeletionUi.Close();
            yield return new WaitForSecondsRealtime(0.5f);
            Check(GameManager.Instance != null && Time.timeScale > 0f, $"{who}: the game keeps running after the deletion");
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
