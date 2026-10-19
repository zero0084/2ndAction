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

// Unity Gaming Services の設定(Tools/ugs)を「開発用の環境」へ配置する(2026-10-20、マスター依頼: development で本物の確認)。
//  ・production へは書かない。対象の環境の名前が production なら、どの入り口でも何もせずに止める(コードに production へ書く道が無い)。
//  ・認証は Unity エディターにサインインしている人の権限(Unity Hub のサインイン → エディターのトークン → サービスのトークンへ交換)。
//    サービスアカウントの鍵はここでは使わない・作らない。トークンはログ/ファイルへ出さない(長さも出さない)。
//  ・配置する物(1か所で管理している物をそのまま使う):
//     Cloud Code: Tools/ugs/cloudcode/*.js(パラメータはファイルの module.exports.params から読む)
//     Leaderboards: Leaderboard.Stages × Leaderboard.BoardId(ゲームと同じ一覧)。高い順・Keep latest(自己ベストの判定は Cloud Code)
//     Access Control: Tools/ugs/access/*.ac
//  ・Secret Manager(削除用の OMM_ADMIN_BASIC)は値を扱わないので、ここでは作らない(マスターが Dashboard で入れる)。
//  入り口:
//    Tools > One More Mile > UGS > Check development (read only) / Deploy to development
//    -batchmode -executeMethod UgsDevDeploy.BatchCheck|BatchDeploy|BatchVerifyPlayers
//       -ugsDeployEnv <名前>(既定 development。production は拒否)  -ugsDeployConfirm <同じ名前>(配置の時だけ必須)
//       -ugsDeployReport <path>(結果の書き出し先)  -ugsVerifyPlayers <path>(1行1つの PlayerId。サーバーに何が残っているかを読む)
public static class UgsDevDeploy
{
    const string Gateway = "https://services.unity.com/api";
    const string Production = "production";

    static readonly StringBuilder report = new StringBuilder();
    static void L(string m) { Debug.Log("[UGS deploy] " + m); report.AppendLine(m); }

    static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
    static string Arg(string name, string def)
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return def;
    }

    [MenuItem("Tools/One More Mile/UGS/Check development (read only)")]
    static void MenuCheck() => RunAndShow(false, "development");
    [MenuItem("Tools/One More Mile/UGS/Deploy to development")]
    static void MenuDeploy()
    {
        if (!EditorUtility.DisplayDialog("UGS", "Tools/ugs の Cloud Code・Leaderboards・Access Control を development 環境へ配置します。\nproduction には書きません。", "配置する", "やめる")) return;
        RunAndShow(true, "development");
    }
    static void RunAndShow(bool deploy, string env)
    {
        report.Clear();
        bool ok = Run(deploy, env, null);
        EditorUtility.DisplayDialog("UGS", (ok ? "OK\n\n" : "FAILED\n\n") + Tail(report.ToString(), 1500), "OK");
    }

    public static void BatchCheck() => Schedule("BatchCheck");
    public static void BatchDeploy() => Schedule("BatchDeploy");
    public static void BatchVerifyPlayers() => Schedule("BatchVerifyPlayers");
    // 試験で作った記録を消す(開発用の環境だけ。-ugsDeployConfirm <環境> と、1行1つの PlayerId のファイル -ugsPurgePlayers が必要)
    public static void BatchPurgeTestPlayers() => Schedule("BatchPurgeTestPlayers");

    // -batchmode ではエディターのサインイン(トークン)が無いので、画面ありで起動して(-executeMethod)サインインを待ってから動く。
    // 起動直後のスクリプトの読み直し(ドメインの再読み込み)で待ちが消えるので、読み直しの後にも起動引数から予約し直す。
    // 120秒待ってもトークンが無ければ「サインインしていない」で終わる
    static bool scheduled;
    [InitializeOnLoadMethod]
    static void ResumeAfterReload()
    {
        string m = Arg("-executeMethod", "");
        if (m.StartsWith("UgsDevDeploy.")) Schedule(m.Substring("UgsDevDeploy.".Length));
    }
    static void Schedule(string what)
    {
        if (scheduled || SessionState.GetBool("UgsDevDeploy.started", false)) return;
        scheduled = true;
        double until = EditorApplication.timeSinceStartup + 180;
        void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (string.IsNullOrEmpty(CloudProjectSettings.accessToken) && EditorApplication.timeSinceStartup < until) return;
            EditorApplication.update -= Poll;
            SessionState.SetBool("UgsDevDeploy.started", true);
            switch (what)
            {
                case "BatchCheck": Batch(false); break;
                case "BatchDeploy": Batch(true); break;
                case "BatchVerifyPlayers": VerifyPlayers(); break;
                case "BatchPurgeTestPlayers": PurgeTestPlayers(); break;
                default: L("unknown entry " + what); Finish(false); break;
            }
        }
        EditorApplication.update += Poll;
    }

    static void VerifyPlayers()
    {
        report.Clear();
        string env = Arg("-ugsDeployEnv", "development");
        bool ok = false;
        try { ok = Verify(env, Arg("-ugsVerifyPlayers", "")); }
        catch (Exception e) { L("ERROR " + e.GetType().Name + ": " + FirstLine(e.Message)); }
        Finish(ok);
    }
    static void Batch(bool deploy)
    {
        report.Clear();
        string env = Arg("-ugsDeployEnv", "development");
        if (deploy && Arg("-ugsDeployConfirm", "") != env) { L($"refused: -ugsDeployConfirm {env} is required to deploy"); Finish(false); return; }
        bool ok = false;
        try { ok = Run(deploy, env, null); }
        catch (Exception e) { L("ERROR " + e.GetType().Name + ": " + FirstLine(e.Message)); }
        Finish(ok);
    }
    static void Finish(bool ok)
    {
        L(ok ? "RESULT OK" : "RESULT FAILED");
        string path = Arg("-ugsDeployReport", "");
        if (path != "") File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
        EditorApplication.Exit(ok ? 0 : 1); // 入り口は起動引数からだけ(メニューは RunAndShow)
    }

    // ---------------------------------------------------------------- 本体
    static bool Run(bool deploy, string envName, string _)
    {
        var ctx = Connect(envName);
        if (ctx == null) return false;
        bool ok = true;
        ok &= CloudCode(ctx, deploy);
        ok &= Leaderboards(ctx, deploy);
        ok &= AccessControl(ctx, deploy);
        SecretCheck(ctx);
        return ok;
    }

    class Ctx { public string projectId, envId, envName, prodId; public HttpClient http; }

    static Ctx Connect(string envName)
    {
        envName = (envName ?? "").Trim().ToLowerInvariant();
        if (envName == "" || envName == Production) { L($"refused: the target environment '{envName}' is not allowed (this tool never writes to production)"); return null; }
        string projectId = CloudProjectSettings.projectId;
        if (string.IsNullOrEmpty(projectId)) projectId = Application.cloudProjectId; // バッチ起動ではエディターの値が空のことがある
        if (string.IsNullOrEmpty(projectId)) { L("refused: the project is not linked to Unity Cloud"); return null; }
        string genesis = CloudProjectSettings.accessToken;
        if (string.IsNullOrEmpty(genesis)) { L("not signed in: the Unity Editor has no access token (sign in to Unity Hub / the Editor)"); return null; }

        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var ex = Send(http, HttpMethod.Post, Gateway + "/auth/v1/genesis-token-exchange/unity", new JObject { ["token"] = genesis });
        string token = ex.ok ? (string)ex.json?["token"] : null;
        if (string.IsNullOrEmpty(token)) { L($"token exchange failed ({ex.status})"); return null; }
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var envs = Send(http, HttpMethod.Get, $"{Gateway}/unity/v1/projects/{projectId}/environments", null);
        if (!envs.ok) { L($"cannot list the environments ({envs.status} {envs.brief})"); return null; }
        string envId = null, prodId = null;
        var names = new List<string>();
        foreach (var e in (JArray)envs.json["results"] ?? new JArray())
        {
            string n = (string)e["name"], id = (string)e["id"];
            names.Add($"{n}={id}");
            if (n == envName) envId = id;
            if (n == Production) prodId = id;
        }
        L($"project {projectId} ({CloudProjectSettings.projectName}), environments: {string.Join(", ", names)}");
        if (envId == null) { L($"refused: environment '{envName}' does not exist"); return null; }
        if (envId == prodId) { L("refused: the target is the production environment"); return null; }
        L($"TARGET environment = {envName} ({envId})");
        return new Ctx { projectId = projectId, envId = envId, envName = envName, prodId = prodId, http = http };
    }

    static string EnvPath(Ctx c, string service, string rest)
    {
        // どの書き込みも、この組み立てだけを通す(production の ID なら例外で止める)
        if (c.envId == c.prodId || string.IsNullOrEmpty(c.envId)) throw new InvalidOperationException("production environment");
        return $"{Gateway}/{service}/projects/{c.projectId}/environments/{c.envId}/{rest}";
    }

    // ---------------------------------------------------------------- Cloud Code
    static bool CloudCode(Ctx c, bool deploy)
    {
        string dir = Path.Combine(RepoRoot, "Tools", "ugs", "cloudcode");
        var list = Send(c.http, HttpMethod.Get, EnvPath(c, "cloud-code/v1", "scripts?limit=100"), null);
        if (!list.ok) { L($"Cloud Code: cannot list scripts ({list.status} {list.brief})"); return false; }
        var existing = new HashSet<string>();
        foreach (var s in (JArray)list.json["results"] ?? new JArray()) existing.Add((string)s["name"]);
        L($"Cloud Code scripts in {c.envName}: {(existing.Count == 0 ? "(none)" : string.Join(", ", existing))}");
        bool ok = true;
        foreach (var file in Directory.GetFiles(dir, "*.js").OrderBy(f => f))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            string code = File.ReadAllText(file, Encoding.UTF8);
            var pars = ParseParams(code, out string perr);
            if (perr != null) { L($"  {name}: params could not be read ({perr})"); ok = false; continue; }
            string ps = string.Join(", ", pars.Select(p => $"{p["name"]}:{p["type"]}{((bool)p["required"] ? "*" : "")}"));
            if (!deploy)
            {
                if (existing.Contains(name))
                {
                    var g = Send(c.http, HttpMethod.Get, EnvPath(c, "cloud-code/v1", "scripts/" + name), null);
                    string live = g.ok ? (string)g.json?["activeScript"]?["code"] : null;
                    L($"  {name}: exists; published copy {(live == null ? "NONE" : Same(live, code) ? "matches the file" : "DIFFERS from the file")}; params {string.Join(", ", ((JArray)g.json?["params"] ?? new JArray()).Select(p => $"{p["name"]}:{p["type"]}{((bool?)p["required"] == true ? "*" : "")}"))}");
                }
                else L($"  {name}: missing (would create; params {ps})");
                continue;
            }
            if (existing.Contains(name))
            {
                // 公開済みの中身とパラメータがファイルと同じなら何もしない(同じ物の公開は "already active" で拒否される)
                var g = Send(c.http, HttpMethod.Get, EnvPath(c, "cloud-code/v1", "scripts/" + name), null);
                string live = g.ok ? (string)g.json?["activeScript"]?["code"] : null;
                var liveParams = g.ok ? (JArray)g.json?["activeScript"]?["params"] ?? (JArray)g.json?["params"] : null;
                if (live != null && Same(live, code) && liveParams != null && JToken.DeepEquals(Canon(liveParams), Canon(pars))) { L($"  {name}: up to date (published copy matches the file)"); continue; }
            }
            var body = new JObject { ["params"] = pars, ["code"] = code };
            Res r;
            if (existing.Contains(name)) r = Send(c.http, new HttpMethod("PATCH"), EnvPath(c, "cloud-code/v1", "scripts/" + name), body);
            else { body["name"] = name; body["type"] = "API"; body["language"] = "JS"; r = Send(c.http, HttpMethod.Post, EnvPath(c, "cloud-code/v1", "scripts"), body); }
            if (!r.ok) { L($"  {name}: {(existing.Contains(name) ? "update" : "create")} FAILED ({r.status} {r.brief})"); ok = false; continue; }
            var pub = Send(c.http, HttpMethod.Post, EnvPath(c, "cloud-code/v1", $"scripts/{name}/publish"), new JObject());
            if (!pub.ok) { L($"  {name}: publish FAILED ({pub.status} {pub.brief})"); ok = false; continue; }
            L($"  {name}: {(existing.Contains(name) ? "updated" : "created")} + published (version {pub.json?["version"]}; params {ps})");
        }
        return ok;
    }

    static JArray Canon(JArray ps) => new JArray(ps.OrderBy(p => (string)p["name"]).Select(p => new JObject { ["name"] = p["name"], ["type"] = ((string)p["type"] ?? "").ToUpperInvariant(), ["required"] = (bool?)p["required"] ?? false }));
    static bool Same(string a, string b) => a.Replace("\r\n", "\n").TrimEnd() == b.Replace("\r\n", "\n").TrimEnd();

    // module.exports.params = { name: { type: "String", required: true }, ... };
    static JArray ParseParams(string code, out string err)
    {
        err = null;
        var arr = new JArray();
        var m = Regex.Match(code, @"module\.exports\.params\s*=\s*\{(?<b>[\s\S]*?)\};");
        if (!m.Success) { err = "no module.exports.params"; return arr; }
        string b = Regex.Replace(m.Groups["b"].Value, @"//[^\n]*", "");
        foreach (Match p in Regex.Matches(b, @"(?<n>\w+)\s*:\s*\{\s*type\s*:\s*""(?<t>\w+)""\s*,\s*required\s*:\s*(?<r>true|false)\s*\}"))
        {
            string t = p.Groups["t"].Value.ToUpperInvariant();
            if (t != "STRING" && t != "NUMERIC" && t != "BOOLEAN" && t != "JSON" && t != "ANY") { err = "type " + t; return arr; }
            arr.Add(new JObject { ["name"] = p.Groups["n"].Value, ["type"] = t, ["required"] = p.Groups["r"].Value == "true" });
        }
        if (arr.Count == 0 && Regex.IsMatch(b, @"\w+\s*:")) err = "unrecognised parameter syntax";
        return arr;
    }

    // ---------------------------------------------------------------- Leaderboards
    static bool Leaderboards(Ctx c, bool deploy)
    {
        var list = Send(c.http, HttpMethod.Get, EnvPath(c, "leaderboards/v1", "leaderboards?limit=100"), null);
        if (!list.ok) { L($"Leaderboards: cannot list ({list.status} {list.brief})"); return false; }
        var existing = new Dictionary<string, JToken>();
        foreach (var x in (JArray)list.json["results"] ?? new JArray()) existing[(string)x["id"]] = x;
        L($"Leaderboards in {c.envName}: {(existing.Count == 0 ? "(none)" : string.Join(", ", existing.Keys))}");
        bool ok = true;
        foreach (string stage in Leaderboard.Stages)
        {
            string id = Leaderboard.BoardId(stage);
            string display = "OMM Best " + string.Join(" ", stage.Split('_').Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
            if (existing.TryGetValue(id, out var cur))
            {
                string so = (string)cur["sortOrder"], ut = (string)cur["updateType"];
                bool right = so == "desc" && ut == "keepLatest" && None(cur["resetConfig"]) && None(cur["bucketSize"]) && None(cur["tieringConfig"]);
                L($"  {id}: exists (name '{cur["name"]}', sort {so}, update {ut}, reset {Show(cur["resetConfig"])}, buckets {Show(cur["bucketSize"])}, tiers {Show(cur["tieringConfig"])}) {(right ? "OK" : "SETTINGS DIFFER")}");
                if (!right) ok = false; // 設定が違う表は書き換えない(記録が消える種類の変更を避ける)。報告して止める
                continue;
            }
            if (!deploy) { L($"  {id}: missing (would create: sort desc, keepLatest, no reset/buckets/tiers)"); continue; }
            var r = Send(c.http, HttpMethod.Post, EnvPath(c, "leaderboards/v1", "leaderboards"),
                new JObject { ["id"] = id, ["name"] = display, ["sortOrder"] = "desc", ["updateType"] = "keepLatest" });
            if (!r.ok) { L($"  {id}: create FAILED ({r.status} {r.brief})"); ok = false; continue; }
            L($"  {id}: created ('{display}', sort desc, keepLatest)");
        }
        foreach (var k in existing.Keys) if (!Leaderboard.Stages.Any(s => Leaderboard.BoardId(s) == k)) L($"  {k}: not used by the game (left as it is)");
        if (!deploy)
            foreach (string stage in Leaderboard.Stages)
            {
                if (!existing.ContainsKey(Leaderboard.BoardId(stage))) continue;
                var top = Send(c.http, HttpMethod.Get, EnvPath(c, "leaderboards/v1", $"leaderboards/{Leaderboard.BoardId(stage)}/scores?limit=10"), null, quiet: true);
                var rows = top.ok ? ((JArray)top.json?["results"] ?? new JArray()).Select(x => $"{x["playerId"]} {x["score"]}m '{x["playerName"]}' {Meta(x)}") : new[] { "err " + top.status };
                L($"  top of {Leaderboard.BoardId(stage)}: {(rows.Any() ? string.Join(" | ", rows) : "(empty)")}");
            }
        return ok;
    }

    static bool None(JToken t) => t == null || t.Type == JTokenType.Null || (t is JObject o && !o.HasValues) || (t.Type == JTokenType.Integer && (int)t == 0);
    static string Show(JToken t) => None(t) ? "none" : t.ToString(Newtonsoft.Json.Formatting.None);

    // ---------------------------------------------------------------- Access Control
    static bool AccessControl(Ctx c, bool deploy)
    {
        string dir = Path.Combine(RepoRoot, "Tools", "ugs", "access");
        var want = new JArray();
        foreach (var f in Directory.GetFiles(dir, "*.ac").OrderBy(f => f))
            foreach (var s in (JArray)JObject.Parse(File.ReadAllText(f, Encoding.UTF8))["Statements"]) want.Add(s);
        string url = $"{Gateway}/access/resource-policy/v1/projects/{c.projectId}/environments/{c.envId}/resource-policy";
        if (c.envId == c.prodId) throw new InvalidOperationException("production environment");
        var g = Send(c.http, HttpMethod.Get, url, null);
        var have = new Dictionary<string, JToken>();
        if (g.ok) foreach (var s in (JArray)g.json?["statements"] ?? new JArray()) have[(string)s["Sid"]] = s;
        else if (g.status != 404) { L($"Access Control: cannot read the policy ({g.status} {g.brief})"); return false; }
        L($"Access Control statements in {c.envName}: {(have.Count == 0 ? "(none)" : string.Join(", ", have.Keys))}");
        var missing = new JArray();
        foreach (var s in want)
        {
            string sid = (string)s["Sid"];
            bool same = have.TryGetValue(sid, out var h) && JToken.DeepEquals(Norm(h), Norm(s));
            L($"  {sid}: {(same ? "present (same)" : have.ContainsKey(sid) ? "present (DIFFERENT)" : "missing")}");
            if (!same) missing.Add(s);
        }
        if (!deploy || missing.Count == 0) return true;
        var p = Send(c.http, new HttpMethod("PATCH"), url, new JObject { ["statements"] = missing });
        if (!p.ok) { L($"Access Control: upsert FAILED ({p.status} {p.brief})"); return false; }
        L($"Access Control: {missing.Count} statement(s) written");
        return true;
    }
    static JObject Norm(JToken s) => new JObject { ["Sid"] = s["Sid"], ["Action"] = s["Action"], ["Effect"] = s["Effect"], ["Principal"] = s["Principal"], ["Resource"] = s["Resource"] };

    // ---------------------------------------------------------------- Secret Manager(名前があるかだけ。値は読まない・出さない)
    static void SecretCheck(Ctx c)
    {
        var r = Send(c.http, HttpMethod.Get, EnvPath(c, "secret-manager/v1", "secrets"), null, quiet: true);
        if (!r.ok) { L($"Secret Manager: could not list the secret names ({r.status}); check OMM_ADMIN_BASIC in the Dashboard"); return; }
        var names = new List<string>();
        void Walk(JToken t)
        {
            if (t is JObject o) { foreach (var k in new[] { "name", "key" }) if (o[k] != null && o[k].Type == JTokenType.String) names.Add((string)o[k]); foreach (var p in o.Properties()) if (p.Value is JArray || p.Value is JObject) Walk(p.Value); }
            else if (t is JArray a) foreach (var x in a) Walk(x);
        }
        Walk(r.json);
        L($"Secret Manager: OMM_ADMIN_BASIC {(names.Contains("OMM_ADMIN_BASIC") ? "exists" : "NOT FOUND")} ({names.Count} secret name(s) listed)");
    }

    // ---------------------------------------------------------------- プレイヤーごとに、サーバーに残っている物を読む(試験の確認用、読むだけ)
    static bool Verify(string envName, string path)
    {
        var c = Connect(envName);
        if (c == null) return false;
        if (!File.Exists(path)) { L("no -ugsVerifyPlayers file"); return false; }
        foreach (var raw in File.ReadAllLines(path))
        {
            string pid = raw.Trim();
            if (pid == "" || pid.StartsWith("#")) continue;
            if (!Regex.IsMatch(pid, "^[A-Za-z0-9_-]{8,64}$")) { L($"skip bad id '{pid}'"); continue; }
            var sb = new List<string>();
            foreach (string stage in Leaderboard.Stages)
            {
                var r = Send(c.http, HttpMethod.Get, EnvPath(c, "leaderboards/v1", $"leaderboards/{Leaderboard.BoardId(stage)}/scores/players/{pid}"), null, quiet: true);
                sb.Add($"{stage}={(r.ok ? r.json?["score"] + "m name '" + (string)r.json?["playerName"] + "' meta " + Meta(r.json) : r.status == 404 ? "none" : "err" + r.status)}");
            }
            var cs = Send(c.http, HttpMethod.Get, $"{Gateway}/cloud-save/v1/data/projects/{c.projectId}/environments/{c.envId}/players/{pid}/items", null, quiet: true);
            string csKeys = cs.ok ? string.Join("+", ((JArray)cs.json?["results"] ?? new JArray()).Select(x => (string)x["key"])) : cs.status == 404 ? "" : "err" + cs.status;
            var au = Send(c.http, HttpMethod.Get, $"{Gateway}/player-identity/v1/projects/{c.projectId}/users/{pid}", null, quiet: true);
            L($"PLAYER {pid}: leaderboards [{string.Join(", ", sb)}]; cloud save [{(csKeys == "" ? "none" : csKeys)}]; account {(au.ok ? "EXISTS" : au.status == 404 ? "none" : "status " + au.status)}");
        }
        return true;
    }
    static void PurgeTestPlayers()
    {
        report.Clear();
        string env = Arg("-ugsDeployEnv", "development");
        bool ok = false;
        try
        {
            if (Arg("-ugsDeployConfirm", "") != env) L($"refused: -ugsDeployConfirm {env} is required");
            else
            {
                var c = Connect(env);
                string path = Arg("-ugsPurgePlayers", "");
                if (c != null && File.Exists(path))
                {
                    ok = true;
                    foreach (var raw in File.ReadAllLines(path))
                    {
                        string pid = raw.Trim();
                        if (pid == "" || pid.StartsWith("#") || !Regex.IsMatch(pid, "^[A-Za-z0-9_-]{8,64}$")) continue;
                        var r = Send(c.http, HttpMethod.Delete, EnvPath(c, "leaderboards/v1", $"leaderboards/scores/players/{pid}/purge"), null);
                        L($"purge leaderboards {pid}: {(r.ok ? "ok" : r.status == 404 ? "nothing" : "FAILED " + r.status + " " + r.brief)}");
                        if (!r.ok && r.status != 404) ok = false;
                    }
                }
            }
        }
        catch (Exception e) { L("ERROR " + e.GetType().Name + ": " + FirstLine(e.Message)); }
        Finish(ok);
    }

    static string Meta(JToken j) { var m = j?["metadata"]; return m == null ? "-" : m.Type == JTokenType.String ? (string)m : m.ToString(Newtonsoft.Json.Formatting.None); }

    // ---------------------------------------------------------------- HTTP(トークンは出さない。本文は短く)
    struct Res { public bool ok; public int status; public JToken json; public string brief; }
    static Res Send(HttpClient http, HttpMethod m, string url, JToken body, bool quiet = false)
    {
        return Task.Run(async () =>
        {
            var req = new HttpRequestMessage(m, url);
            if (body != null) req.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
            try
            {
                var resp = await http.SendAsync(req);
                string text = await resp.Content.ReadAsStringAsync();
                JToken j = null;
                try { if (!string.IsNullOrWhiteSpace(text)) j = JToken.Parse(text); } catch { }
                string brief = resp.IsSuccessStatusCode ? "" : Brief(j, text);
                return new Res { ok = resp.IsSuccessStatusCode, status = (int)resp.StatusCode, json = j, brief = brief };
            }
            catch (Exception e) { return new Res { ok = false, status = 0, brief = e.GetType().Name + ": " + FirstLine(e.Message) }; }
        }).Result;
    }
    static string Brief(JToken j, string text)
    {
        string s = j is JObject o ? $"{o["title"]} {o["detail"]} {o["message"]} {(o["errors"] ?? o["details"])?.ToString(Newtonsoft.Json.Formatting.None)}".Trim() : text ?? "";
        s = Regex.Replace(s, @"(?i)(bearer|basic|token)\s*[:=]?\s*[A-Za-z0-9._\-+/=]{12,}", "$1 ***");
        return s.Length > 600 ? s.Substring(0, 600) : s;
    }
    static string FirstLine(string s) { if (string.IsNullOrEmpty(s)) return ""; int i = s.IndexOf('\n'); return i > 0 ? s.Substring(0, i) : s; }
    static string Tail(string s, int n) => s.Length <= n ? s : "…" + s.Substring(s.Length - n);
}
