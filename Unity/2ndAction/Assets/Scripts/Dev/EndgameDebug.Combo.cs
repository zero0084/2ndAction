#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// COMBO TEST(2026-10-06、開発版の DEBUG → COMBO TEST…)。DEBUG RUN(保存しない/記録しない)。
// キャラ/ステージ/COMBO/構成カードの Lv/AWAKENED/FINAL EVOLUTION/敵/ボス を選んで走り出す →
// 「A を付ける」「B を付ける」で成立 → 敵に当てて効果 → 「FE ACTIVE」「FE 終了」で ENHANCED の切り替えをすぐ確かめる。
public partial class EndgameDebug
{
    public static string SelectedComboChar = "swordsman", SelectedComboStage = "wasteland_road", SelectedCombo = "blazing_edge";
    public static int ComboLvA = 5, ComboLvB = 5, ComboPage;
    public static bool ComboAwakened, ComboFe, ComboEnemies = true, ComboBoss;
    static (string ch, string stage, string combo, int la, int lb, bool aw, bool fe, bool enemies, bool boss)? pendingCombo;
    public bool IsComboTest { get; private set; }

    public static void LaunchCombo()
    {
        if (Instance == null) return;
        if (Instance.Launching) return;
        pending = null; pendingLong = null; pendingUlt = null; pendingCave = null; pendingFe = null;
        pendingCombo = (SelectedComboChar, SelectedComboStage, SelectedCombo, ComboLvA, ComboLvB, ComboAwakened, ComboFe, ComboEnemies, ComboBoss);
        Instance.Launches++;
        Debug.Log($"[EndgameDebug] LAUNCH COMBO TEST {SelectedCombo} {SelectedComboChar} {SelectedComboStage} Lv{ComboLvA}/{ComboLvB} awakened={ComboAwakened} fe={ComboFe} enemies={ComboEnemies} boss={ComboBoss}");
        Instance.StopAllCoroutines();
        Instance.keepAlive = false;
        DebugPanel.CloseStatic();
        SafeReset("launch");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    static bool TakePendingCombo()
    {
        if (!pendingCombo.HasValue) return false;
        var p = pendingCombo.Value;
        pendingCombo = null;
        Instance.StartCoroutine(Instance.RunLaunchCombo(p.ch, p.stage, p.combo, p.la, p.lb, p.aw, p.fe, p.enemies, p.boss));
        return true;
    }

    IEnumerator RunLaunchCombo(string ch, string stage, string comboId, int la, int lb, bool aw, bool fe, bool enemies, bool boss)
    {
        Launching = true;
        IsComboTest = true;
        currentLabel = $"COMBO TEST {comboId}";
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
        ComboSystem.DebugForceAwakened = aw;
        gm.SetSelectedCharacter(ch);
        gm.DebugStartRunOnStage(stage);
        w = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 25f) { yield return null; w += Time.unscaledDeltaTime; }
        if (!gm.HasStarted) { Fail("ランを開始できませんでした"); yield break; }
        ApplyProfile(Profile.Sturdy);
        Warp(gm, boss ? 1955f : 1200f);
        yield return null;
        if (fe) { var c = ComboTuning.I.For(comboId); if (c != null) { ComboGive(c.abilities[0], la); FinalEvolution.DebugMakeReady(c.abilities[0]); } }
        if (!enemies) StartCoroutine(ComboClearEnemies());
        Status = $"{currentLabel} 開始(DEBUG → COMBO TEST の「A を付ける」「B を付ける」で成立)";
        Launching = false;
    }

    IEnumerator ComboClearEnemies()
    {
        while (IsComboTest && DebugRun.IsActive)
        {
            foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (e != null) Destroy(e.gameObject);
            yield return new WaitForSeconds(0.5f);
        }
    }

    // 能力をこのランで Lv まで(足りない分だけ。通常の上限付きの経路)
    public static void ComboGive(string abilityId, int lv)
    {
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted) return;
        var c = CardDatabase.FindBaseById(abilityId);
        if (c == null) return;
        int have = gm.GetAbilityRunStack(abilityId);
        if (have < lv) gm.DebugApplyRunCard(c, lv - have);
        gm.RecomputeCardStats();
    }

    // 敵を前に並べる(当てて効果を見る)
    public static void ComboSpawnEnemies(int n)
    {
        var tm = TerrainManager.Instance; var pc = PlayerController.Instance;
        var def = EnemyDatabase.FindById("goblin");
        if (tm == null || pc == null || def == null) return;
        for (int i = 0; i < n; i++)
        {
            float x = pc.transform.position.x + 5f + i * 1.3f;
            tm.GenerateNow(x + 20f);
            float? gy = tm.GetHeightAt(x);
            if (gy.HasValue) tm.SpawnEncounterEnemy(def, new Vector2(x, gy.Value), EnemyAiTier.T0, def.behaviorKind);
        }
    }
}

public partial class GameManager
{
    // COMBO TEST: カードをこのランで n 回分(能力ごとの上限付き)
    public void DebugApplyRunCard(CardDefinition c, int times) { if (c != null && times > 0) ApplyRunCardCapped(c, times, "ComboTest"); }
}
#endif
