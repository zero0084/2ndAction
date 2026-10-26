using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudCode;
using Unity.Services.Core;
using UnityEngine;

// オンラインのデータの削除(2026-10-19、Google Play のデータの削除の要件)。設定 →「データとプライバシー」→「データの削除」。
//  消す物(サーバー): ランキングの記録(全マップ、表示名のコードネームを含む)/ 送った記録の履歴(Cloud Save の omm_runs ほか)/ 匿名のアカウント(PlayerId)
//  消さない物(この端末): カード・MILE・解放・進行・設定・コードネーム / 広告なしパス(Google Play のアカウントの購入。PlayerId とは結びついていない)
//  順番: ① サーバーのデータ(Cloud Code omm_delete_player。呼んだ本人だけ。管理者の鍵はサーバーの Secret Manager)
//        ② ①が成功した時だけ、匿名のアカウントを削除(DeleteAccountAsync)→ サインインの記録を消す
//        ③ この端末のランキングの参加/送信待ち/送信済み/自己ベストの記録を消す(後で参加し直しても、古い記録を送り直さない)
//     ①が失敗したらアカウントは消さない(消すと、残ったデータを本人が二度と消せなくなる)。メールでの申請を案内する
//  画面を開いただけでは、新しいアカウントを作らない(サインインの記録が無い端末ではサインインしない)
public static class AccountDeletion
{
    public const string DeleteScript = "omm_delete_player";
    public enum Phase { Idle, Checking, Ready, NoAccount, Confirm, Deleting, Done, Failed, Unavailable }
    public static Phase State { get; private set; } = Phase.Idle;
    public static string PlayerId { get; private set; } = "";
    public static string Message { get; private set; } = "";   // 画面の文(日本語の元の文 = 訳のキー)
    public static string Detail { get; private set; } = "";    // 開発版のログ用(秘密は含めない)
    public static int Runs { get; private set; }               // 確認用

    [Serializable] class Reply { public bool ok; public int cloudSave; public int leaderboards; public string message; }

    public static bool Busy => State == Phase.Checking || State == Phase.Deleting;
    // 2026-10-26(公開前の案内の整備): アプリの中からサーバーのデータを消せるのは、削除の仕組み(Cloud Code omm_delete_player)を配置して
    //  本物で確かめた環境だけ。今は development だけ(production は未配置)→ production につながる製品版では、削除のボタンを出さず
    //  メールでの依頼を案内する(公開中のプライバシーポリシーも「メールで依頼」)。production に配置して確かめたら ProductionReady を true に
    public const bool ProductionReady = false;
    public static bool InAppAvailable => UgsEnvironment.Name != "production" || ProductionReady;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 開発版の試験だけ: サーバーの代わり(ok = 成功 / fail = サーバーの削除の失敗)。本物のサーバー/アカウントには触れない
    public static string DebugMock;
    // 開発版の試験だけ(2026-10-26): 本物のサービスで、1回だけ失敗を起こす。次の実行は普段どおり(再試行の確認)
    //  cloudcode = 無いスクリプトの名前を呼ぶ(Cloud Code が断る → サーバーもアカウントも何も消えない)
    //  auth      = サーバーのデータを消した後、アカウントの削除の直前にサインアウトする(SDK が断る → アカウントは残り、もう一度消せる)
    public static string DebugFailOnce;
#endif
    static string TakeFailOnce()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        string f = DebugFailOnce; DebugFailOnce = null; return f ?? "";
#else
        return "";
#endif
    }
    static bool Mocked(out bool ok)
    {
        ok = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!string.IsNullOrEmpty(DebugMock)) { ok = DebugMock == "ok"; return true; }
#endif
        return false;
    }

    // 画面を開いた時: この端末にアカウントがあるか / PlayerId(アカウントがある時だけサインインして ID を出す)
    public static async void Check()
    {
        if (Busy) return;
        State = Phase.Checking; Message = ""; PlayerId = "";
        if (Mocked(out _)) { await System.Threading.Tasks.Task.Yield(); PlayerId = "MOCKPLAYERID0123456789"; State = Phase.Ready; return; }
        if (!await OnlineServices.InitializeServicesAsync()) { State = Phase.Unavailable; Message = "オンラインのサービスに接続できませんでした"; Detail = OnlineServices.ErrorDetail; return; }
        if (!OnlineServices.HasStoredAccount) { State = Phase.NoAccount; return; }
        if (!await OnlineServices.EnsureReadyAsync()) { State = Phase.Unavailable; Message = "オンラインのサービスに接続できませんでした"; Detail = OnlineServices.ErrorDetail; return; }
        PlayerId = OnlineServices.PlayerId;
        State = Phase.Ready;
    }

    public static void AskConfirm() { if (State == Phase.Ready || State == Phase.Failed) State = Phase.Confirm; }
    public static void CancelConfirm() { if (State == Phase.Confirm) State = Phase.Ready; }

    // 削除の実行(確認の画面の「削除する」から)
    public static async void Run()
    {
        if (State != Phase.Confirm) return;
        if (OnlineServices.InSession || NetSession.IsActive)
        { State = Phase.Failed; Message = "マルチプレイの部屋にいる間は削除できません。部屋を出てから試してください"; return; }
        State = Phase.Deleting; Message = ""; Detail = ""; Runs++;
        if (Mocked(out bool mockOk))
        {
            await System.Threading.Tasks.Task.Delay(300);
            if (!mockOk) { Fail("サーバーのデータを削除できませんでした", "server: mock fail"); return; }
            Leaderboard.ForgetAfterDeletion();
            PlayerId = ""; State = Phase.Done; Message = "サーバー上のデータを削除しました";
            Debug.Log("[PRIVACY] online data deleted (mock)");
            return;
        }
        try
        {
            if (!await OnlineServices.EnsureReadyAsync()) { Fail("オンラインのサービスに接続できませんでした", OnlineServices.ErrorDetail); return; }
            string before = OnlineServices.PlayerId;
            string inject = TakeFailOnce();
            // ① サーバーのデータ(本人の分だけ。引数で ID を渡さない)
            string json = await CloudCodeService.Instance.CallEndpointAsync(inject == "cloudcode" ? DeleteScript + "_qa_missing" : DeleteScript, new Dictionary<string, object>());
            var r = JsonUtility.FromJson<Reply>(string.IsNullOrEmpty(json) ? "{}" : json) ?? new Reply();
            if (!r.ok) { Fail("サーバーのデータを削除できませんでした", "server: " + (r.message ?? "")); return; }
            Debug.Log($"[PRIVACY] server data deleted (cloud save {r.cloudSave}, leaderboards {r.leaderboards}); deleting the account next");
            // ② 匿名のアカウント
            if (inject == "auth") AuthenticationService.Instance.SignOut(false); // 試験: サインインの記録は残したまま
            await AuthenticationService.Instance.DeleteAccountAsync();
            var auth = AuthenticationService.Instance;
            if (auth.IsSignedIn) auth.SignOut(true);
            if (auth.SessionTokenExists) auth.ClearSessionToken();
            OnlineServices.ForgetAfterDeletion();
            // ③ この端末のランキングの記録
            Leaderboard.ForgetAfterDeletion();
            Debug.Log($"[PRIVACY] online data deleted (player {OnlineServices.ShortId(before)}, cloud save {r.cloudSave}, leaderboards {r.leaderboards})");
            PlayerId = "";
            State = Phase.Done;
            Message = "サーバー上のデータを削除しました";
        }
        catch (CloudCodeException e) { Fail("サーバーのデータを削除できませんでした", "cloud code: " + e.Reason); }
        catch (AuthenticationException e) { Fail("アカウントを削除できませんでした", "auth: " + e.ErrorCode); }
        catch (RequestFailedException e) { Fail("アカウントを削除できませんでした", "request: " + e.ErrorCode); }
        catch (Exception e) { Fail("削除できませんでした", e.GetType().Name); }
    }

    static void Fail(string msg, string detail)
    {
        State = Phase.Failed; Message = msg; Detail = detail;
        Debug.LogWarning("[PRIVACY] deletion failed: " + detail);
    }

    public static void Reset() { if (!Busy) { State = Phase.Idle; Message = ""; } }
}
