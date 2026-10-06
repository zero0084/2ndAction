#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// ULTIMATE TEST(#100 ULTIMATE の確認、2026-10-04)。DEBUGパネル → 「ULTIMATE TEST…」。開発版だけ。
// 押すたびにシーンを読み直し、DEBUG RUN(BEST / MILE / 解放 / CONTINUE / コレクション / 選択中のキャラを保存しない)で
// 選んだキャラ・ステージを新しく始め、カードを ULTIMATE だけ(選んだ Lv)にして Gauge 100% で始める。
//   通常: 次のボス関門まで十分ある所(3,150m)。ボス戦: 関門(2,000m)の少し手前 → ボスが出てから Gauge 100%。
public partial class EndgameDebug
{
    public static string SelectedUltChar = "swordsman";
    public static int SelectedUltLevel = 5;
    public static string SelectedUltStage = "wasteland_road";
    static (string ch, int lv, string stage, bool boss)? pendingUlt;
    public bool IsUltimateTest { get; private set; }

    public static readonly string[] UltCharacters = { "swordsman", "noble_lady", "dual_blade", "gunslinger", "dragon_lancer", "mage", "archer", "fighter", "ninja", "dragonkin", "vampire", "miko" };
    public static string UltCharLabel(string id) { var d = CharacterDatabase.FindById(id); return d != null && !string.IsNullOrEmpty(d.displayName) ? d.displayName : id; }
    public static string StageLabel(string id) => id switch { "natural_cave" => "自然洞窟", "sky_corridor" => "天空回廊", _ => "荒野街道" };

    public static void LaunchUltimate(string ch, int lv, string stage, bool boss)
    {
        if (Instance == null) return;
        if (Instance.Launching) { Debug.Log("[EndgameDebug] launch ignored (already launching)"); return; }
        pending = null; pendingLong = null;
        pendingUlt = (ch, Mathf.Clamp(lv, 1, 9), stage, boss);
        Instance.Launches++;
        Debug.Log($"[EndgameDebug] LAUNCH ULTIMATE TEST {ch} Lv{lv} {stage} boss={boss} - reloading the scene for a clean start");
        Instance.StopAllCoroutines();
        Instance.keepAlive = false;
        DebugPanel.CloseStatic();
        SafeReset("launch");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    IEnumerator RunLaunchUltimate(string ch, int lv, string stage, bool boss)
    {
        Launching = true;
        IsLongCheck = false; IsUltimateTest = true;
        currentLabel = $"ULTIMATE TEST {UltCharLabel(ch)} Lv{lv} {StageLabel(stage)}{(boss ? " ボス戦" : "")}";
        Status = $"{currentLabel} を準備中…";
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 10f) { yield return null; w += Time.unscaledDeltaTime; }
        var gm = GameManager.Instance;
        if (gm == null) { Fail("GameManager がありません"); yield break; }
        yield return null;
        w = 0f;
        while (ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning && w < 6f) { yield return null; w += Time.unscaledDeltaTime; }
        DebugRun.Begin(currentLabel);
        CurrentProfile = Profile.Sturdy;
        LaunchedRealtime = Time.realtimeSinceStartup;
        gm.SetSelectedCharacter(ch); // 選択中のキャラも「進行」なので DEBUG RUN の終わりに元へ戻る
        gm.DebugStartRunOnStage(stage);
        w = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 25f) { yield return null; w += Time.unscaledDeltaTime; }
        if (!gm.HasStarted) { Fail("ランを開始できませんでした"); yield break; }
        ApplyProfile(Profile.Sturdy);
        gm.UltimateTestPrepare(lv);
        Warp(gm, boss ? 1955f : 3150f);
        if (boss)
        {
            Status = $"{currentLabel}: ボスの登場を待っています…";
            w = 0f;
            var bm = BossManager.Instance;
            while ((bm == null || bm.AliveBossCount <= 0 || !BossBattle.AnyBossFighting) && w < 30f) { yield return null; w += Time.deltaTime; }
            yield return new WaitForSeconds(0.5f);
        }
        if (UltimateArt.Instance != null) UltimateArt.Instance.DebugSetGauge(100f);
        Debug.Log($"[EndgameDebug] ULTIMATE TEST ready: {currentLabel} d={gm.MaxDistance:F0} Lv{UltimateArt.Level} gauge={(UltimateArt.Instance != null ? UltimateArt.Instance.Gauge : 0f):F0}");
        Status = $"{currentLabel} 開始(左下のボタン / DEBUG の「発動」)";
        Launching = false;
    }
}

public partial class GameManager
{
    // ULTIMATE TEST: このキャラの基本値に戻し(キャラカード等も外す)、ULTIMATE だけを Lv で付けて Gauge 100%
    public void UltimateTestPrepare(int lv)
    {
        var def = CharacterDatabase.FindById(activeRunCharacterId);
        ApplyCharacterBaseStats(def);
        Lives = maxLives;
        var c = CardDatabase.FindBaseById(UltimateArt.CardId);
        if (c != null) ApplyCardEffectsStacked(c, Mathf.Clamp(lv, 1, 9));
        if (UltimateArt.Instance != null) UltimateArt.Instance.DebugSetGauge(100f);
        Debug.Log($"[ULTIMATE] test prepared: {activeRunCharacterId} Lv{UltimateArt.Level}");
    }
}
#endif
