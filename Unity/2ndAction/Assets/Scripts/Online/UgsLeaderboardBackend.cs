using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudCode;
using Unity.Services.Leaderboards;
using Unity.Services.Leaderboards.Exceptions;
using UnityEngine;

// 本番のランキング(2026-10-08): Unity Gaming Services の Leaderboards(読む)+ Cloud Code(書く)。
//  ・書き込みは Cloud Code のスクリプト omm_submit_run(検査してから書く)/ omm_set_name(表示名の更新)だけ。
//    プレイヤーが Leaderboards へ直接書けないよう、Access Control で AddPlayerScore を禁止する(Tools/ugs/ の設定)。
//  ・読む: 上位(GetScoresAsync)と自分の周り(GetPlayerRangeAsync)。表示名・キャラ・オート/疾走は Cloud Code が書いた metadata。
//  ・同じ距離は同じ順位: 上位は一覧の中で付け直す。自分の順位は、周りの記録で同じ距離の先頭まで戻して決める
//    (同じ距離の人が周りの範囲より多い時は、範囲の先頭の順位になる = 近似)。
//  ・UGS の初期化と匿名サインインは OnlineServices(ONLINE PLAY と共通)。
public class UgsLeaderboardBackend : Leaderboard.IBackend
{
    public string Name => "UGS";
    public const string SubmitScript = "omm_submit_run", NameScript = "omm_set_name";

    [Serializable] class SubmitReply { public string status = "", message = ""; }
    [Serializable] class Meta { public string n = "", c = ""; public int a, s; }

    public async Task<Leaderboard.SubmitResult> Submit(Leaderboard.Entry e, string codename)
    {
        if (!await OnlineServices.EnsureReadyAsync()) return new Leaderboard.SubmitResult(Leaderboard.SubmitStatus.Failed, "service unavailable");
        try
        {
            var args = new Dictionary<string, object>
            {
                { "runId", e.runId }, { "stageId", e.stageId }, { "characterId", e.characterId }, { "meters", e.meters },
                { "playSeconds", e.playSeconds }, { "sprintFrom", e.sprintFrom }, { "usedAuto", e.usedAuto }, { "usedSprint", e.usedSprint },
                { "startedUtc", e.startedUtc }, { "committedUtc", e.committedUtc }, { "appVersion", e.appVersion }, { "name", Codename.Sanitize(codename) },
            };
            string json = await CloudCodeService.Instance.CallEndpointAsync(SubmitScript, args);
            var reply = JsonUtility.FromJson<SubmitReply>(json ?? "{}") ?? new SubmitReply();
            switch (reply.status)
            {
                case "accepted": return new Leaderboard.SubmitResult(Leaderboard.SubmitStatus.Accepted);
                case "not_better": return new Leaderboard.SubmitResult(Leaderboard.SubmitStatus.NotBetter, reply.message);
                case "duplicate": return new Leaderboard.SubmitResult(Leaderboard.SubmitStatus.Duplicate, reply.message);
                case "rejected": return new Leaderboard.SubmitResult(Leaderboard.SubmitStatus.Rejected, reply.message);
                default: return new Leaderboard.SubmitResult(Leaderboard.SubmitStatus.Failed, "unexpected reply");
            }
        }
        catch (CloudCodeException ex) { return new Leaderboard.SubmitResult(Leaderboard.SubmitStatus.Failed, "cloud code: " + ex.Reason); }
        catch (Exception ex) { return new Leaderboard.SubmitResult(Leaderboard.SubmitStatus.Failed, ex.GetType().Name); }
    }

    public async Task<Leaderboard.Page> Load(string stageId, int limit)
    {
        if (!await OnlineServices.EnsureReadyAsync()) throw new Exception("service unavailable");
        string board = Leaderboard.BoardId(stageId);
        string me = OnlineServices.PlayerId;
        var page = new Leaderboard.Page();
        var top = await LeaderboardsService.Instance.GetScoresAsync(board, new GetScoresOptions { Limit = limit, IncludeMetadata = true });
        page.total = top.Total;
        foreach (var r in top.Results) page.top.Add(ToRow(r, me));
        Leaderboard.AssignRanks(page.top);
        try
        {
            var around = await LeaderboardsService.Instance.GetPlayerRangeAsync(board, new GetPlayerRangeOptions { RangeLimit = 25, IncludeMetadata = true });
            var rows = new List<Leaderboard.Row>();
            foreach (var r in around.Results) rows.Add(ToRow(r, me));
            int mi = rows.FindIndex(x => x.isMe);
            if (mi >= 0)
            {
                var mine = rows[mi];
                int k = mi; while (k > 0 && rows[k - 1].meters == mine.meters) k--;
                mine.rank = around.Results[k].Rank + 1; // UGS の順位は 0 始まり、同じ点は更新の早い順に並ぶ
                var inTop = page.top.Find(x => x.isMe);
                if (inTop != null) mine.rank = inTop.rank;
                page.me = mine;
            }
        }
        catch (LeaderboardsException ex) when (ex.Reason == LeaderboardsExceptionReason.EntryNotFound || ex.Reason == LeaderboardsExceptionReason.NotFound) { page.me = null; }
        return page;
    }

    public async Task<bool> UpdateName(string codename)
    {
        if (!await OnlineServices.EnsureReadyAsync()) return false;
        try
        {
            string json = await CloudCodeService.Instance.CallEndpointAsync(NameScript, new Dictionary<string, object> { { "name", Codename.Sanitize(codename) } });
            var reply = JsonUtility.FromJson<SubmitReply>(json ?? "{}");
            return reply != null && reply.status == "ok";
        }
        catch (Exception ex) { Debug.LogWarning("[Ranking] rename failed: " + ex.GetType().Name); return false; }
    }

    static Leaderboard.Row ToRow(Unity.Services.Leaderboards.Models.LeaderboardEntry r, string me)
    {
        Meta m = null;
        if (!string.IsNullOrEmpty(r.Metadata)) { try { m = JsonUtility.FromJson<Meta>(r.Metadata); } catch { } }
        return new Leaderboard.Row
        {
            playerId = r.PlayerId, meters = (int)Math.Floor(r.Score), isMe = r.PlayerId == me,
            name = m != null && !string.IsNullOrEmpty(m.n) ? Codename.Sanitize(m.n) : "",
            characterId = m != null ? m.c : "", usedAuto = m != null && m.a != 0, usedSprint = m != null && m.s != 0,
        };
    }
}
