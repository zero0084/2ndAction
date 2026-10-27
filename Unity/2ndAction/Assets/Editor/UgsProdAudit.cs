using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// production の読むだけの監査(2026-10-27、production へ反映する前の差分の整理)。
//  ・書き込みの道が無い: 環境の API は GET だけ(Get())。POST はエディターのサインインをサービスのトークンへ替える1回(環境に関係しない)だけ。
//  ・読む物: Cloud Code のスクリプト(手元の Tools/ugs/cloudcode と同じか)/ ランキングの表の設定 / Access Control / Secret の名前の一覧 /
//    各ランキングの掲載(最大 100 件: PlayerId は先頭6文字、表示名、metadata のコードネーム)/ 指定のプレイヤーに何が残っているか。
//  ・トークン・秘密の値は出さない。
//  入り口: 画面ありのエディターで -executeMethod UgsProdAudit.BatchAudit -ugsAuditReport <path> [-ugsAuditPlayers <file>]
public static class UgsProdAudit
{
    const string Gateway = "https://services.unity.com/api";
    const string Production = "production";
    static readonly StringBuilder report = new StringBuilder();
    static void L(string m) { Debug.Log("[UGS audit] " + m); report.AppendLine(m); }
    static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
    static string Arg(string name, string def)
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return def;
    }
    static string S6(string id) => string.IsNullOrEmpty(id) ? "-" : id.Length > 6 ? id.Substring(0, 6) + "…" : id;

    public static void BatchAudit() => Schedule();

    static bool scheduled;
    [InitializeOnLoadMethod]
    static void ResumeAfterReload() { if (Arg("-executeMethod", "") == "UgsProdAudit.BatchAudit") Schedule(); }
    static void Schedule()
    {
        if (scheduled || SessionState.GetBool("UgsProdAudit.started", false)) return;
        scheduled = true;
        double until = EditorApplication.timeSinceStartup + 180;
        void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (string.IsNullOrEmpty(CloudProjectSettings.accessToken) && EditorApplication.timeSinceStartup < until) return;
            EditorApplication.update -= Poll;
            SessionState.SetBool("UgsProdAudit.started", true);
            bool ok = false;
            try { ok = Audit(); } catch (Exception e) { L("ERROR " + e.GetType().Name + ": " + FirstLine(e.Message)); }
            L(ok ? "RESULT OK" : "RESULT FAILED");
            string path = Arg("-ugsAuditReport", "");
            if (path != "") File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
            EditorApplication.Exit(ok ? 0 : 1);
        }
        EditorApplication.update += Poll;
    }

    static HttpClient http;
    static bool Audit()
    {
        report.Clear();
        string projectId = CloudProjectSettings.projectId;
        if (string.IsNullOrEmpty(projectId)) projectId = Application.cloudProjectId;
        string genesis = CloudProjectSettings.accessToken;
        if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(genesis)) { L("not signed in / not linked"); return false; }
        http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var ex = Exchange(genesis);
        if (string.IsNullOrEmpty(ex)) { L("token exchange failed"); return false; }
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ex);

        var envs = Get($"{Gateway}/unity/v1/projects/{projectId}/environments");
        string prod = null;
        foreach (var e in (JArray)envs.json?["results"] ?? new JArray()) if ((string)e["name"] == Production) prod = (string)e["id"];
        if (prod == null) { L("production environment not found"); return false; }
        L($"READ ONLY audit of production ({prod})");
        string E(string service, string rest) => $"{Gateway}/{service}/projects/{projectId}/environments/{prod}/{rest}";

        // Cloud Code
        var list = Get(E("cloud-code/v1", "scripts?limit=100"));
        var names = list.ok ? ((JArray)list.json?["results"] ?? new JArray()).Select(x => (string)x["name"]).ToList() : new List<string>();
        L($"Cloud Code scripts: {(list.ok ? (names.Count == 0 ? "(none)" : string.Join(", ", names)) : "error " + list.status)}");
        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot, "Tools", "ugs", "cloudcode"), "*.js").OrderBy(f => f))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (!names.Contains(name)) { L($"  {name}: NOT DEPLOYED"); continue; }
            var g = Get(E("cloud-code/v1", "scripts/" + name));
            string live = g.ok ? (string)g.json?["activeScript"]?["code"] : null;
            string pars = string.Join(", ", ((JArray)g.json?["activeScript"]?["params"] ?? (JArray)g.json?["params"] ?? new JArray()).Select(p => $"{p["name"]}:{p["type"]}"));
            string code = File.ReadAllText(file, Encoding.UTF8);
            L($"  {name}: deployed; published copy {(live == null ? "NONE (not published)" : Same(live, code) ? "matches the repo file" : "DIFFERS from the repo file")}; params [{pars}]");
        }
        // Leaderboards
        var lbs = Get(E("leaderboards/v1", "leaderboards?limit=100"));
        var boards = lbs.ok ? ((JArray)lbs.json?["results"] ?? new JArray()).ToList() : new List<JToken>();
        L($"Leaderboards: {(lbs.ok ? (boards.Count == 0 ? "(none)" : string.Join(", ", boards.Select(b => (string)b["id"]))) : "error " + lbs.status)}");
        foreach (var b in boards)
        {
            string id = (string)b["id"];
            L($"  {id}: name '{b["name"]}' sort {b["sortOrder"]} update {b["updateType"]} reset {Show(b["resetConfig"])} buckets {Show(b["bucketSize"])} tiers {Show(b["tieringConfig"])}");
            var top = Get(E("leaderboards/v1", $"leaderboards/{id}/scores?limit=100&includeMetadata=true"));
            var rows = top.ok ? (JArray)top.json?["results"] ?? new JArray() : new JArray();
            L($"    entries: {(top.ok ? rows.Count.ToString() : "error " + top.status)}{(top.ok && top.json?["total"] != null ? " (total " + top.json["total"] + ")" : "")}");
            foreach (var x in rows) L($"      {S6((string)x["playerId"])} {x["score"]}m '{x["playerName"]}' {Meta(x)} updated {x["updatedTime"]}");
        }
        // Access Control
        var ac = Get($"{Gateway}/access/resource-policy/v1/projects/{projectId}/environments/{prod}/resource-policy");
        var st = ac.ok ? ((JArray)ac.json?["statements"] ?? new JArray()).Select(s => (string)s["Sid"]).ToList() : new List<string>();
        L($"Access Control statements: {(ac.ok ? (st.Count == 0 ? "(none)" : string.Join(", ", st)) : "error " + ac.status)}");
        // Secret の名前(値は読まない)
        var sec = Get(E("secret-manager/v1", "secrets"));
        L($"Secret Manager names: {(sec.ok ? sec.json?.ToString(Newtonsoft.Json.Formatting.None).Contains("OMM_ADMIN_BASIC") == true ? "OMM_ADMIN_BASIC exists" : "OMM_ADMIN_BASIC not listed" : "could not list (" + sec.status + ")")}");
        // 指定のプレイヤー
        string pf = Arg("-ugsAuditPlayers", "");
        if (pf != "" && File.Exists(pf))
            foreach (var raw in File.ReadAllLines(pf))
            {
                var parts = raw.Split('\t'); string tag = parts.Length > 1 ? parts[0] : "", pid = (parts.Length > 1 ? parts[1] : parts[0]).Trim();
                if (!Regex.IsMatch(pid, "^[A-Za-z0-9_-]{8,64}$")) continue;
                var sb = new List<string>();
                foreach (var b in boards)
                {
                    var r = Get(E("leaderboards/v1", $"leaderboards/{b["id"]}/scores/players/{pid}"));
                    sb.Add($"{b["id"]}={(r.ok ? r.json?["score"] + "m" : r.status == 404 ? "none" : "err" + r.status)}");
                }
                var cs = Get($"{Gateway}/cloud-save/v1/data/projects/{projectId}/environments/{prod}/players/{pid}/items");
                string csKeys = cs.ok ? string.Join("+", ((JArray)cs.json?["results"] ?? new JArray()).Select(x => (string)x["key"])) : cs.status == 404 ? "" : "err" + cs.status;
                var au = Get($"{Gateway}/player-identity/v1/projects/{projectId}/users/{pid}");
                L($"PLAYER {tag} {S6(pid)}: leaderboards [{string.Join(", ", sb)}]; cloud save [{(csKeys == "" ? "none" : csKeys)}]; account {(au.ok ? "EXISTS" : au.status == 404 ? "none" : "status " + au.status)}");
            }
        return true;
    }

    static string Show(JToken t) => t == null || t.Type == JTokenType.Null || (t is JObject o && !o.HasValues) ? "none" : t.ToString(Newtonsoft.Json.Formatting.None);
    static string Meta(JToken j) { var m = j?["metadata"]; return m == null ? "" : "meta " + (m.Type == JTokenType.String ? (string)m : m.ToString(Newtonsoft.Json.Formatting.None)); }
    static bool Same(string a, string b) => a.Replace("\r\n", "\n").TrimEnd() == b.Replace("\r\n", "\n").TrimEnd();

    // ---- HTTP: 環境の API は GET だけ ----
    struct Res { public bool ok; public int status; public JToken json; }
    static Res Get(string url) => Task.Run(async () =>
    {
        try
        {
            var resp = await http.GetAsync(url);
            string text = await resp.Content.ReadAsStringAsync();
            JToken j = null; try { if (!string.IsNullOrWhiteSpace(text)) j = JToken.Parse(text); } catch { }
            return new Res { ok = resp.IsSuccessStatusCode, status = (int)resp.StatusCode, json = j };
        }
        catch { return new Res { ok = false, status = 0 }; }
    }).Result;
    static string Exchange(string genesis) => Task.Run(async () =>
    {
        try
        {
            var resp = await http.PostAsync(Gateway + "/auth/v1/genesis-token-exchange/unity", new StringContent(new JObject { ["token"] = genesis }.ToString(), Encoding.UTF8, "application/json"));
            var j = JToken.Parse(await resp.Content.ReadAsStringAsync());
            return resp.IsSuccessStatusCode ? (string)j["token"] : null;
        }
        catch { return null; }
    }).Result;
    static string FirstLine(string s) { if (string.IsNullOrEmpty(s)) return ""; int i = s.IndexOf('\n'); return i > 0 ? s.Substring(0, i) : s; }
}
