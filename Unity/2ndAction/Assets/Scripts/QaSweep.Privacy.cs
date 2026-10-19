#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// データとプライバシーの確認(2026-10-19): -qaPrivacy <dir> [-pvLangs ja,en,ar,th]。専用の保存ファイル(-fileSave)で動かすこと。
//  A 設定の「データとプライバシー」の欄(言語ごとに撮影)
//  B データの削除の画面(開発版の環境のまま = development が無ければ「接続できない」。新しいアカウントを作らないこと)
//  C 模擬の成功: ランキングの4つの記録だけ消え、ほかの保存(カード/MILE/進行/設定/広告なしパス)は1つも変わらない
//  D 模擬の失敗: 何も消えない
//  E プライバシーポリシーを開く(-noOpenUrl でブラウザは開かない)
public partial class QaSweep
{
    IEnumerator PrivacyMode()
    {
        float w = 0f;
        while (GameManager.Instance == null && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        yield return new WaitForSecondsRealtime(3.5f);
        string[] langs = Arg("-pvLangs", "ja,en,ar,th").Split(',');
        var scrollF = typeof(SettingsPanel).GetField("scroll", BindingFlags.NonPublic | BindingFlags.Instance);

        // ---- A ----
        foreach (var lang in langs)
        {
            Loc.Set(lang, false);
            SettingsPanel.OpenStatic();
            yield return new WaitForSecondsRealtime(0.6f);
            var sp = Object.FindAnyObjectByType<SettingsPanel>();
            if (sp != null && scrollF != null) scrollF.SetValue(sp, new Vector2(0f, 99999f));
            yield return new WaitForSecondsRealtime(0.3f);
            Shot($"pv_settings_{lang}");
            yield return null; yield return null;
            SettingsPanel.CloseStatic();
            yield return new WaitForSecondsRealtime(0.5f);
        }
        Loc.Set(langs[0], false);

        // ---- B 本物の経路(開発版の環境のまま) ----
        // 2026-10-20: 「前からアカウントがあったか」は初期化の後でないと分からない(以前は初期化の前に見ていて、development ができて
        //  既存のサインインの記録で Ready になった時に誤って失敗にしていた)。初期化だけならサインインしない
        var tInit = OnlineServices.InitializeServicesAsync();
        w = 0f; while (!tInit.IsCompleted && w < 20f) { yield return null; w += Time.unscaledDeltaTime; }
        bool hadAccountBefore = OnlineServices.HasStoredAccount;
        DataDeletionUi.Open();
        w = 0f; while (AccountDeletion.Busy && w < 20f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.3f);
        Shot("pv_delete_real");
        yield return null; yield return null;
        L($"[privacy] real path: state={AccountDeletion.State} id={(string.IsNullOrEmpty(AccountDeletion.PlayerId) ? "-" : OnlineServices.ShortId(AccountDeletion.PlayerId))} detail='{AccountDeletion.Detail}' env={UgsEnvironment.Name} hadAccount={hadAccountBefore}");
        Check(AccountDeletion.State == AccountDeletion.Phase.Unavailable || AccountDeletion.State == AccountDeletion.Phase.NoAccount || AccountDeletion.State == AccountDeletion.Phase.Ready,
            $"B: the deletion screen reports the server state ({AccountDeletion.State})");
        Check(hadAccountBefore || AccountDeletion.State != AccountDeletion.Phase.Ready, "B: opening the screen does not create a new anonymous account");
        DataDeletionUi.Close();
        yield return new WaitForSecondsRealtime(0.3f);

        // ---- C 模擬の成功 ----
        foreach (var lang in langs)
        {
            Loc.Set(lang, false);
            SeedRankingKeys();
            var before = SnapshotSave();
            AccountDeletion.DebugMock = "ok";
            DataDeletionUi.Open();
            w = 0f; while (AccountDeletion.State != AccountDeletion.Phase.Ready && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
            yield return new WaitForSecondsRealtime(0.3f);
            Shot($"pv_delete_{lang}");
            yield return null; yield return null;
            AccountDeletion.AskConfirm();
            yield return new WaitForSecondsRealtime(0.3f);
            Shot($"pv_confirm_{lang}");
            yield return null; yield return null;
            AccountDeletion.Run();
            w = 0f; while (AccountDeletion.Busy && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
            yield return new WaitForSecondsRealtime(0.3f);
            Shot($"pv_done_{lang}");
            yield return null; yield return null;
            var after = SnapshotSave();
            var changed = Diff(before, after);
            L($"[privacy] mock ok ({lang}): state={AccountDeletion.State} changed=[{string.Join(",", changed)}]");
            Check(AccountDeletion.State == AccountDeletion.Phase.Done, $"C {lang}: deletion completes");
            var expected = new HashSet<string> { Leaderboard.JoinedKey, Leaderboard.PendingKey, Leaderboard.VerifiedKey, Leaderboard.SentKey };
            bool onlyRanking = true; foreach (var k in changed) if (!expected.Contains(k)) onlyRanking = false;
            Check(onlyRanking && changed.Count == 4, $"C {lang}: only the 4 ranking keys change (cards/MILE/progress/settings/ad-free pass untouched): [{string.Join(",", changed)}]");
            Check(!Leaderboard.Joined, $"C {lang}: the ranking opt-in is cleared");
            DataDeletionUi.Close();
            yield return new WaitForSecondsRealtime(0.3f);
        }
        Loc.Set(langs[0], false);

        // ---- D 模擬の失敗 ----
        SeedRankingKeys();
        var b2 = SnapshotSave();
        AccountDeletion.DebugMock = "fail";
        DataDeletionUi.Open();
        w = 0f; while (AccountDeletion.State != AccountDeletion.Phase.Ready && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
        AccountDeletion.AskConfirm(); yield return null;
        AccountDeletion.Run();
        w = 0f; while (AccountDeletion.Busy && w < 5f) { yield return null; w += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.3f);
        Shot("pv_failed");
        yield return null; yield return null;
        var ch2 = Diff(b2, SnapshotSave());
        Check(AccountDeletion.State == AccountDeletion.Phase.Failed && ch2.Count == 0, $"D: a server failure deletes nothing (state {AccountDeletion.State}, changed [{string.Join(",", ch2)}])");
        GameManager.Instance.HandleBackButtonForTest(); yield return null; // 戻るで閉じる
        Check(!DataDeletionUi.IsOpen, "D: the back button closes the deletion screen");
        AccountDeletion.DebugMock = null;

        // ---- E プライバシーポリシー ----
        int o0 = PrivacyLinks.Opened;
        bool ok = PrivacyLinks.OpenPolicy();
        Check(ok && PrivacyLinks.Opened == o0 + 1, "E: the privacy policy link opens (test: not really opened)");
        L($"[privacy] policy url {PrivacyLinks.PolicyUrl}");
    }

    void SeedRankingKeys()
    {
        SaveStore.SetInt(Leaderboard.JoinedKey, 1);
        SaveStore.SetString(Leaderboard.PendingKey, "{\"items\":[{\"runId\":\"qa1\",\"stageId\":\"wasteland_road\",\"meters\":1234,\"env\":\"development\"}]}");
        SaveStore.SetString(Leaderboard.VerifiedKey, "{\"items\":[{\"runId\":\"qa1\",\"stageId\":\"wasteland_road\",\"meters\":1234,\"env\":\"development\"}]}");
        SaveStore.SetString(Leaderboard.SentKey, "qa0");
        SaveStore.Save();
    }

    Dictionary<string, string> SnapshotSave()
    {
        var d = new Dictionary<string, string>();
        foreach (var e in SaveKeys.All)
        {
            if (!SaveStore.HasKey(e.key)) { d[e.key] = "<none>"; continue; }
            switch (e.type)
            {
                case SaveType.Int: d[e.key] = SaveStore.GetInt(e.key, 0).ToString(); break;
                case SaveType.Float: d[e.key] = SaveStore.GetFloat(e.key, 0f).ToString("R"); break;
                default: d[e.key] = SaveStore.GetString(e.key, ""); break;
            }
        }
        return d;
    }

    static List<string> Diff(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        var l = new List<string>();
        foreach (var kv in a) if (!b.TryGetValue(kv.Key, out var v) || v != kv.Value) l.Add(kv.Key);
        return l;
    }
}
#endif
