using System.Collections.Generic;
using UnityEngine;

// 開発版のテスト用データ(2026-10-07)。通常のセーブとは別の保存先で「新規ユーザーと同じ初期状態」から遊べる。
//
//  仕組み: SaveStore(ゲームの保存の入口)がキーを読み替える。テスト用データの時は、進行/管理情報/進行に効く開発用の値を
//          "test:" を付けたキーへ読み書きする(通常のデータのキーには一切触れない)。設定(音量/画面揺れ/補助/言語/マルチの設定等)は
//          両方で共有する(SharedKey)。リリース版は常に通常のデータ(読み替えなし)。
//  切り替え: ホームでだけ(DEBUG パネル)。今の進行を書き切ってから印を変え、セーブの起動時処理をやり直してシーンを読み直す。
//          印(SaveProfile.Active)は読み替えないキーなので、アプリを終了しても次の起動でテスト用データのまま始まる。
//  初期化: テスト用データで書いたキーは一覧(SaveProfile.TestKeys)に記録してあり、リセットはその全部と登録済みキーの "test:" 版だけを消す。
public static class SaveProfile
{
    public const string ActiveKey = "SaveProfile.Active";   // 0=通常 / 1=テスト用(読み替えない)
    public const string TestKeysKey = "SaveProfile.TestKeys"; // テスト用データで書いたキーの一覧(読み替えない)
    public const string Prefix = "test:";

    static int active = -1;
    public static bool IsTest
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (active < 0) active = Platform.Save.GetInt(ActiveKey, 0) == 1 ? 1 : 0;
            return active == 1;
#else
            return false;
#endif
        }
    }

    public static string Label => IsTest ? "TEST DATA" : "";

    // 両方で共有するキー(設定と、テスト用データの間も効いてほしい開発用の値)
    static HashSet<string> shared;
    // 開発用の値のうち、進行に効くのでテスト用データでは別に持つ(既定0 = 新規ユーザーと同じ)
    static readonly HashSet<string> profileDevKeys = new HashSet<string>
    {
        GachaStage.DevAllCardsOpenKey, SaveKeys.DevFinalDungeonAlwaysOpen, SaveKeys.DevSprintUnlockAll, UnlockRules.DevUnlockAllKey,
    };

    public static bool SharedKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return true;
        if (key == ActiveKey || key == TestKeysKey) return true;
        if (key.StartsWith("CardTest.") || key.StartsWith("Qa.") || key.StartsWith("Net.") || key.StartsWith("net.")) return true;
        if (shared == null)
        {
            var s = new HashSet<string>();
            foreach (var e in SaveKeys.All)
                if (e.cat == SaveCategory.Settings || (e.cat == SaveCategory.Dev && !profileDevKeys.Contains(e.key))) s.Add(e.key);
            foreach (var k in SaveKeys.ExtraSharedKeys) s.Add(k);
            shared = s;
        }
        return shared.Contains(key);
    }

    public static string Map(string key)
    {
        if (!IsTest || SharedKey(key)) return key;
        Remember(key);
        return Prefix + key;
    }

    // ---- テスト用データで書いたキーの一覧 ----
    static HashSet<string> testKeys;
    static HashSet<string> TestKeys
    {
        get
        {
            if (testKeys != null) return testKeys;
            testKeys = new HashSet<string>();
            foreach (var k in Platform.Save.GetString(TestKeysKey, "").Split('\n')) if (!string.IsNullOrEmpty(k)) testKeys.Add(k);
            return testKeys;
        }
    }
    static void Remember(string key)
    {
        if (TestKeys.Add(key)) Platform.Save.SetString(TestKeysKey, string.Join("\n", testKeys));
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static int LastResetDeleted { get; private set; }

    // テスト用データだけを全部消す(通常のデータのキーは読み替えずに直接 "test:" 付きを消すので触れない)
    public static int DeleteTestData()
    {
        var keys = new HashSet<string>(TestKeys);
        foreach (var e in SaveKeys.Expanded()) keys.Add(e.key);
        foreach (var k in SaveKeys.Legacy) keys.Add(k);
        int n = 0;
        foreach (var k in keys)
        {
            if (SharedKey(k)) continue;
            string pk = Prefix + k;
            if (Platform.Save.HasKey(pk)) { Platform.Save.DeleteKey(pk); n++; }
        }
        testKeys = new HashSet<string>();
        Platform.Save.DeleteKey(TestKeysKey);
        // テスト用データのバックアップ/デバッグランの控え
        try
        {
            string dir = System.IO.Path.Combine(Application.persistentDataPath, BackupSubdir(true));
            if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
            string dr = System.IO.Path.Combine(Application.persistentDataPath, DebugRunFile(true));
            if (System.IO.File.Exists(dr)) System.IO.File.Delete(dr);
        }
        catch (System.Exception e) { Debug.LogWarning("[SaveProfile] could not delete the test backups: " + e.Message); }
        Platform.Save.Save();
        LastResetDeleted = n;
        Debug.Log($"[SaveProfile] test data deleted ({n} keys). The normal data was not touched.");
        return n;
    }

    // 切り替え(ホームでだけ呼ぶ)。test=true でテスト用データ、false で通常のデータ。fresh=true ならテスト用データを消してから始める。
    public static void Switch(bool test, bool fresh)
    {
        // 今のデータの書きかけを書き切る(切り替えた後に、前のデータの値が新しいデータへ書かれないように)
        ProgressStats.Flush(true);
        SaveStore.Save();
        if (fresh) DeleteTestData();
        Platform.Save.SetInt(ActiveKey, test ? 1 : 0);
        Platform.Save.Save();
        active = test ? 1 : 0;
        testKeys = null;
        NoticeQueue.ClearAll(); // 前のデータのお知らせを持ち越さない
        SaveSystem.Boot(SaveSystem.BuildReleaseGeneration); // 新しいデータの起動時処理(テスト用が空なら新規ユーザーの初期状態を作る)+ static の読み直し
        Debug.Log($"[SaveProfile] switched to {(test ? "TEST" : "NORMAL")} data{(fresh ? " (fresh)" : "")}");
    }

    // 自動テスト用: 印だけを読み直す
    public static void ResetCacheForTests() { active = -1; testKeys = null; }
#endif

    public static string BackupSubdir(bool test) => test ? "SaveBackups_test" : "SaveBackups";
    public static string DebugRunFile(bool test) => test ? "DebugRunRestore_test.json" : "DebugRunRestore.json";
}

// 画面の隅に小さく「TEST DATA」(テスト用データの間はずっと)
public class TestDataLabel : MonoBehaviour
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<TestDataLabel>() != null) return;
        var go = new GameObject("[TestDataLabel]");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideInHierarchy;
        go.AddComponent<TestDataLabel>();
    }

    GUIStyle style;
    void OnGUI()
    {
        if (!SaveProfile.IsTest) return;
        GUI.depth = -1000;
        if (style == null) style = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        float s = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 720f); // 短い辺基準(縦画面で大きくなりすぎない、2026-10-08)
        style.fontSize = Mathf.RoundToInt(13 * s);
        var r = new Rect(Screen.width * 0.5f - 60 * s, 2 * s, 120 * s, 20 * s);
        var old = GUI.color;
        GUI.color = new Color(0.85f, 0.15f, 0.1f, 0.8f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(r, "TEST DATA", style);
        GUI.color = old;
    }
#endif
}
