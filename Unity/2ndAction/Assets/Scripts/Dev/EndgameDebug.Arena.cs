#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// 開発用の闘技場(2026-10-04)の起動/再戦: 毎回シーンを読み直し(前の敵/弾/設置攻撃/演出/状態/クールダウンを残さない)、
// DEBUG RUN で荒野街道を始めて、平地の闘技場に作り替え、設定(ArenaMode.Config)どおりにキャラ/ビルド/速度/敵を用意する。
public partial class EndgameDebug
{
    static bool pendingArena;
    public bool IsArena { get; private set; }

    // 2026-10-06: 闘技場は正式な機能になった(ArenaLauncher)。DEBUG パネルの入口もそちらへ
    public static void LaunchArena(string why)
    {
        if (Instance == null) return;
        pending = null; pendingLong = null; pendingUlt = null; pendingArena = false;
        Instance.StopAllCoroutines();
        Instance.keepAlive = false;
        DebugPanel.CloseStatic();
        ArenaConfigStore.Load();
        ArenaLauncher.Launch(why, true);
    }

    IEnumerator RunLaunchArena()
    {
        Launching = true;
        IsLongCheck = false; IsUltimateTest = false; IsArena = true;
        var cfg = ArenaMode.Config;
        currentLabel = "闘技場";
        Status = "闘技場を準備中…";
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        var gm = GameManager.Instance;
        if (gm == null) { Fail("GameManager がありません"); yield break; }
        yield return null;
        w = 0f;
        while (ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning && w < 6f) { yield return null; w += Time.unscaledDeltaTime; }
        DebugRun.Begin("闘技場");
        LaunchedRealtime = Time.realtimeSinceStartup;
        if (CharacterDatabase.FindById(cfg.character) == null) cfg.character = "swordsman";
        gm.SetSelectedCharacter(cfg.character); // 未所持/未解放でも選べる。選択中のキャラは DEBUG RUN の終わりに元へ戻る
        ArenaMode.Begin(); // 開始の前から: 距離/EXP/関門を止める
        GameManager.BlockExpGain = true;
        GameManager.EscapeBlocked = true;
        BossManager.SuppressGates = true;
        gm.DebugStartRunOnStage("wasteland_road");
        w = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 25f) { yield return null; w += Time.unscaledDeltaTime; }
        if (!gm.HasStarted) { Fail("ランを開始できませんでした"); ArenaMode.End(); yield break; }
        var ctl = new GameObject("ArenaController").AddComponent<ArenaController>();
        yield return ctl.Setup();
        Status = "闘技場";
        Launching = false;
    }
}
#endif
