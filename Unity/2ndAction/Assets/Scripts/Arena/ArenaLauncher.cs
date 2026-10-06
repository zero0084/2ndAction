using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// 闘技場(2026-10-06 正式版。ホームから入れる練習場)の起動/再戦/退出。リリース版にも含む。
//  ・起動/再戦: 毎回シーンを読み直し(前の敵/弾/設置攻撃/演出/状態/クールダウンを残さない)、荒野街道のランを始めて平地の闘技場へ作り替え、
//    設定(ArenaMode.Config)どおりにキャラ/ビルド/速度/敵を用意する(ArenaController.Setup)。
//  ・進行は書かない: ArenaMode.Active の間は、すべての進行の保存の入口(DebugRun.BlocksSave)が止める(BEST/MILE/カード/Mastery/解放/
//    累計距離/遭遇/CONTINUE/選択中のキャラ/ステージ/キャラカード)。開始の前から Active にする(ランの開始で CONTINUE を消さない)。
//    メモリ上の値はシーンの読み直し(退出)で保存された値へ戻る。開発版の Debug Run(保存の控えと巻き戻し)には頼らない。
//  ・闘技場の設定(最後の構成)は専用のキー(SaveKeys.ArenaConfig、設定の分類)にだけ保存する。デッキ/所持には書かない。
public class ArenaLauncher : MonoBehaviour
{
    public static ArenaLauncher Instance { get; private set; }
    static bool pending, openSetup;
    public static bool Pending => pending;
    public bool Launching { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[ArenaLauncher]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<ArenaLauncher>();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // ホームの「闘技場」/ 再戦 / 設定を変えて開始。setup = 準備画面を開いた状態で始める(ホームから入った時)
    public static void Launch(string why, bool setup)
    {
        if (Instance == null || Instance.Launching) return;
        pending = true; openSetup = setup;
        ArenaConfigStore.Save(ArenaMode.Config);
        Debug.Log($"[Arena] LAUNCH ({why}) char={ArenaMode.Config.character} build={ArenaMode.Config.build.Count} speed={ArenaMode.Config.kmh}km/h mode={ArenaMode.Config.speedMode} distance={ArenaMode.Config.distance} enemies={ArenaMode.Config.enemies.Count}");
        if (!DebugRun.WritesBlocked) ProgressStats.Flush(true); // ホームから入る時: 通常の進行の書きかけを先に書いておく
        ResetStatics("launch");
        ArenaMode.BattleRunning = false; // 前の試合の計測を新しい闘技場へ持ち込まない(作り終わる前に「CLEAR」と判定しない)
        ArenaMode.BeginPending(); // 読み直しの前から進行を書かない(この後のシーン/ランの開始で CONTINUE 等を書かない)
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // 闘技場から出る(ホームへ)。シーンを読み直すので、メモリ上の進行/選択中のキャラ等は保存された値に戻る
    public static void Exit()
    {
        var gm = GameManager.Instance;
        Debug.Log("[Arena] EXIT to home");
        ArenaConfigStore.Save(ArenaMode.Config);
        if (gm != null) gm.ReturnToHome(); // ReturnToHome の中の保存(中断データ/累計距離)は Active の間は止まる
        else SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (Instance == null) return;
        if (!pending)
        {
            // 闘技場以外へ戻った: 闘技場の状態を全部戻す(通常のランへ持ち出さない)
            if (ArenaMode.Active || ArenaMode.PendingStart)
            {
                ArenaController.ResetAll(); ResetStatics("left arena");
                // メモリ上に読み込んだ進行(所持カード/Mastery/会ったボス/CONTINUE/設定)も保存された値から読み直す(闘技場の間の物を持ち出さない)
                SaveSystem.ReloadCaches();
                CardMastery.ReloadFromPrefs();
            }
            return;
        }
        pending = false;
        Instance.StartCoroutine(Instance.Run(openSetup));
    }

    // 闘技場の時だけ変える静的な状態を戻す(通常のランへ持ち出さない)
    static void ResetStatics(string why)
    {
        TimeControl.ResetAll();
        Time.timeScale = 1f;
        GameManager.BlockExpGain = false;
        GameManager.EscapeBlocked = false;
        BossManager.SuppressGates = false;
        UiInputGate.ClearLatch();
        Debug.Log($"[Arena] statics reset ({why})");
    }

    IEnumerator Run(bool setup)
    {
        Launching = true;
        var cfg = ArenaMode.Config;
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        var gm = GameManager.Instance;
        if (gm == null) { Fail("no GameManager"); yield break; }
        yield return null; // シーンの Start
        w = 0f;
        while (ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning && w < 6f) { yield return null; w += Time.unscaledDeltaTime; }
        if (CharacterDatabase.FindById(cfg.character) == null) cfg.character = gm.SelectedCharacterId;
        ArenaMode.Begin(); // 進行を書かない/距離/EXP/関門を止める(ランの開始の前から)
        GameManager.BlockExpGain = true;
        GameManager.EscapeBlocked = true;
        BossManager.SuppressGates = true;
        gm.SetSelectedCharacter(cfg.character); // 試すキャラ(Active の間は保存しない。退出の読み直しで元へ戻る)
        gm.ArenaStartRun("wasteland_road");
        w = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 25f) { yield return null; w += Time.unscaledDeltaTime; }
        if (!gm.HasStarted) { Fail("the run did not start"); ArenaMode.End(); yield break; }
        var ctl = new GameObject("ArenaController").AddComponent<ArenaController>();
        yield return ctl.Setup(setup);
        Launching = false;
    }

    void Fail(string why)
    {
        Debug.LogError("[Arena] launch failed: " + why);
        Launching = false;
    }
}

// 闘技場の最後の構成(専用のキー。試用のビルドを通常のデッキへ書かない)
public static class ArenaConfigStore
{
    static bool loaded;
    public static void Load()
    {
        if (loaded) return;
        loaded = true;
        string json = PlayerPrefs.GetString(SaveKeys.ArenaConfig, "");
        if (string.IsNullOrEmpty(json)) { ArenaMode.Config = ArenaConfig.Fresh(); return; }
        try { var c = JsonUtility.FromJson<ArenaConfig>(json); ArenaMode.Config = c != null ? c.Sanitized() : ArenaConfig.Fresh(); }
        catch { ArenaMode.Config = ArenaConfig.Fresh(); }
    }
    public static void Save(ArenaConfig c)
    {
        if (c == null) return;
        PlayerPrefs.SetString(SaveKeys.ArenaConfig, JsonUtility.ToJson(c));
        PlayerPrefs.Save();
    }
    public static void ResetLoaded() { loaded = false; }
}
