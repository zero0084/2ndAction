#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

// DebugPanel: テスト用データ(2026-10-07)。通常のデータとは別の保存先で、新規ユーザーと同じ状態から遊ぶ。
//  切り替えはホームでだけ(ラン中は「ホームへ戻ってから」と表示)。リセットはテスト用データだけを消す。
public partial class DebugPanel
{
    int confirmTest; // 0=なし / 1=新規テストデータで開始 / 2=テストデータをリセット
    string testNote = "";

    void DrawTestDataPage(Rect p)
    {
        float x = p.x + 24f, y = p.y + 64f, full = p.width - 48f;
        var gm = GameManager.Instance;
        bool home = gm != null && !gm.HasStarted && !ArenaMode.Active && !NetSession.IsActive;
        var lab = UiKit.Label(13f, TextAnchor.UpperLeft, false, new Color(0.85f, 0.85f, 0.9f));
        GUI.Label(new Rect(x, y, full, 24f), $"今のデータ: {(SaveProfile.IsTest ? "テスト用データ(TEST DATA)" : "通常のデータ")}",
            UiKit.Label(17f, TextAnchor.MiddleLeft, true, SaveProfile.IsTest ? new Color(1f, 0.55f, 0.45f) : new Color(0.7f, 1f, 0.75f)));
        y += 28f;
        GUI.Label(new Rect(x, y, full, 64f),
            "テスト用データは通常のデータと別の保存先です(MILE/カード/キャラ/解放/記録/中断中のラン/チュートリアルの進み)。\n"
            + "新規ユーザーと同じ初期状態から始まり、全開放はしません。設定(音量/画面揺れ/補助/言語/マルチ)と開発用の無敵/DEBUGモードは共有です。", lab);
        y += 66f;
        if (!home)
        {
            GUI.Label(new Rect(x, y, full, 40f), "切り替えはホームでだけできます。ホームへ戻ってから開いてください。", UiKit.Label(15f, TextAnchor.UpperLeft, true, new Color(1f, 0.75f, 0.4f)));
            return;
        }
        float bh = 46f;
        if (confirmTest == 0)
        {
            if (UiKit.Button(new Rect(x, y, full, bh), "新規テストデータで開始", 18f, true, false)) confirmTest = 1;
            y += bh + 8f;
            if (UiKit.Button(new Rect(x, y, full, bh), "テストデータをリセット…", 17f, false, false)) confirmTest = 2;
            y += bh + 8f;
            if (UiKit.Button(new Rect(x, y, full, bh), SaveProfile.IsTest ? "通常データへ戻る" : "通常データへ戻る(今は通常のデータです)", 17f, false, false) && SaveProfile.IsTest)
            {
                SaveProfile.Switch(false, false);
                testNote = "通常のデータへ戻りました";
                Reload();
            }
            y += bh + 8f;
            if (UiKit.Button(new Rect(x, y, full, 40f), SaveProfile.IsTest ? "続きのテストデータで遊ぶ(今使用中)" : "前回のテストデータの続きで遊ぶ", 15f, false, false) && !SaveProfile.IsTest)
            {
                SaveProfile.Switch(true, false);
                testNote = "テスト用データ(続き)へ切り替えました";
                Reload();
            }
            y += 48f;
        }
        else
        {
            string msg = confirmTest == 1
                ? "テスト用データを空にして、新規ユーザーと同じ状態から始めます。\n通常のデータは消えません(そのまま残ります)。"
                : "テスト用データだけを消します。通常のデータ(MILE/カード/記録など)は消えません。\n" + (SaveProfile.IsTest ? "消した後、空のテスト用データ(新規ユーザーの状態)で続けます。" : "");
            GUI.Label(new Rect(x, y, full, 44f), msg, UiKit.Label(14f, TextAnchor.UpperLeft, true, new Color(1f, 0.7f, 0.55f)));
            y += 50f;
            float bw = (full - 8f) / 2f;
            if (UiKit.Button(new Rect(x, y, bw, bh), confirmTest == 1 ? "開始する" : "テストデータだけ消す", 17f, true, false))
            {
                if (confirmTest == 1) { SaveProfile.Switch(true, true); testNote = "新規テストデータで開始しました"; }
                else if (SaveProfile.IsTest) { SaveProfile.Switch(true, true); testNote = $"テスト用データを消しました({SaveProfile.LastResetDeleted}項目)"; }
                else { SaveProfile.DeleteTestData(); testNote = $"テスト用データを消しました({SaveProfile.LastResetDeleted}項目、通常のデータはそのまま)"; }
                confirmTest = 0;
                Reload();
            }
            if (UiKit.Button(new Rect(x + bw + 8f, y, bw, bh), "やめる", 17f, false, false)) confirmTest = 0;
            y += bh + 8f;
        }
        // 開発用: 全マップ/全キャラを選べる(正式な解放状態は変えない。テスト用データでは既定OFF)
        bool ua = UnlockRules.DevUnlockAll;
        if (UiKit.Button(new Rect(x, y, full, 36f), $"開発用 全マップ/全キャラ選択: {(ua ? "ON" : "OFF")}(解放の記録は変えない)", 14f, ua, false)) UnlockRules.DevUnlockAll = !ua;
        y += 42f;
        GUI.Label(new Rect(x, y, full, 20f), $"解放: マップ {string.Join(",", System.Array.FindAll(UnlockRules.NormalMaps, m => UnlockRules.OfficialStageUnlocked(m)))}{(UnlockRules.OfficialStageUnlocked(UnlockRules.Arena) ? ",arena" : "")} / 死神 {UnlockRules.ReaperMapsMet}/3 / 累計 {ProgressStats.LifetimeDistance:N0}m",
            UiKit.Label(12f, TextAnchor.MiddleLeft, false, new Color(0.8f, 0.85f, 0.95f)));
        y += 24f;
        if (!string.IsNullOrEmpty(testNote)) GUI.Label(new Rect(x, y, full, 22f), testNote, UiKit.Label(13f, TextAnchor.MiddleLeft, true, new Color(1f, 0.85f, 0.5f)));
    }

    void Reload()
    {
        SetOpen(false);
        UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }
}
#endif
