#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

// Android のバックアップからの復元の見分け(RestoredInstallGuard、2026-11-25)の試験。エミュレーターの本物の保存で行う(実機・一般の端末では動かない)。
//  -qaInstallGuard <dir> [-igJoin 1]
//   起動時の見分けの結果・UGS のサインイン(development)・プレイヤーの識別子の先頭・ランキングの状態・進行(MILE)を書き出す。
//   -igJoin 1: ランキングに参加した状態にしておく(復元の後に消えるかを見る)
//  手順は Docs/SecurityAudit_2026-11-25(更新 → 同じ / 印を消す = 復元 → 新しいアカウント / 入れ直し → 新しいアカウント)
public partial class QaSweep
{
    IEnumerator InstallGuardMode()
    {
        bool emu = Application.platform == RuntimePlatform.Android && (SystemInfo.deviceModel.Contains("sdk") || SystemInfo.deviceModel.Contains("Emulator") || SystemInfo.deviceModel.Contains("emu"));
        Check(emu, $"runs only on the Android emulator ({SystemInfo.deviceModel})");
        if (!emu || UgsEnvironment.IsProduction) yield break;
        L($"[ig] before sign-in: joined={Leaderboard.Joined} pending={Leaderboard.PendingCount} mile={SaveStore.GetInt(GameManager.TotalMileKey, 0)}");
        var ready = OnlineServices.EnsureReadyAsync(); float w = 0f;
        while (!ready.IsCompleted && w < 40f) { yield return null; w += Time.unscaledDeltaTime; }
        bool ok = ready.IsCompleted && ready.Result;
        Check(ok, $"signed in to UGS ({OnlineServices.EnvironmentName})");
        string id = OnlineServices.PlayerId;
        L($"[ig] player {(id.Length > 6 ? id.Substring(0, 6) : id)}");
        L($"[ig] guard: {RestoredInstallGuard.LastResult} (restored={RestoredInstallGuard.RestoredFromBackup}, installed->updated {(RestoredInstallGuard.LastUpdateMs - RestoredInstallGuard.FirstInstallMs) / 1000}s)");
        if (Arg("-igJoin", "0") == "1") { SaveStore.SetInt(Leaderboard.JoinedKey, 1); SaveStore.Save(); L("[ig] marked as joined"); }
        L($"[ig] after: joined={Leaderboard.Joined} pending={Leaderboard.PendingCount}");
    }
}
#endif
