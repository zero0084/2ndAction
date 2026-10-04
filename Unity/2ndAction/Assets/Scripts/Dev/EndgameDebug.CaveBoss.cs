#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// CAVE BOSS TEST(自然洞窟ボス強化の確認、2026-10-04)。DEBUGパネル → 「洞窟ボス試験…」。開発版だけ。
// 押すたびにシーンを読み直し、DEBUG RUN(保存しない)で自然洞窟を始め、関門の無い所(1,500m)で選んだボスを
// 関門と同じ流れ(BGM/ラン再開の時計/ボス区間/再戦の段階)で出す。出した後は同じページの操作で
// 段階1/2/3・必殺技・BREAK・ラン再開を強制できる。
public partial class EndgameDebug
{
    public static CaveBossKind SelectedCaveBoss = CaveBossKind.Centipede;
    public static int SelectedCaveTier = -1; // -1=初登場 / 0〜=再戦の段階
    public static string SelectedCaveChar = "swordsman";
    static (CaveBossKind kind, int tier, string ch)? pendingCave;
    public bool IsCaveBossTest { get; private set; }

    public static void LaunchCaveBoss(CaveBossKind kind, int tier, string ch)
    {
        if (Instance == null) return;
        if (Instance.Launching) { Debug.Log("[EndgameDebug] launch ignored (already launching)"); return; }
        pending = null; pendingLong = null; pendingUlt = null;
        pendingCave = (kind, tier, ch);
        Instance.Launches++;
        Debug.Log($"[EndgameDebug] LAUNCH CAVE BOSS TEST {kind} tier={tier} {ch} - reloading the scene for a clean start");
        Instance.StopAllCoroutines();
        Instance.keepAlive = false;
        DebugPanel.CloseStatic();
        SafeReset("launch");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    static bool TakePendingCave()
    {
        if (!pendingCave.HasValue) return false;
        var c = pendingCave.Value;
        pendingCave = null;
        Instance.StartCoroutine(Instance.RunLaunchCaveBoss(c.kind, c.tier, c.ch));
        return true;
    }

    public static string CaveTierLabel(int tier)
    {
        if (tier < 0) return "初登場";
        var t = BossRematchTuning.I.tiers;
        return tier < t.Count ? t[tier].label : "?";
    }

    IEnumerator RunLaunchCaveBoss(CaveBossKind kind, int tier, string ch)
    {
        Launching = true;
        IsLongCheck = false; IsUltimateTest = false; IsCaveBossTest = true;
        currentLabel = $"洞窟ボス試験 {kind} {CaveTierLabel(tier)}";
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
        gm.SetSelectedCharacter(ch);
        gm.DebugStartRunOnStage(CaveBossSafety.Stage);
        w = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 25f) { yield return null; w += Time.unscaledDeltaTime; }
        if (!gm.HasStarted) { Fail("ランを開始できませんでした"); yield break; }
        ApplyProfile(Profile.Sturdy);
        Warp(gm, 1500f);
        yield return new WaitForSeconds(1.0f);
        var bm = BossManager.Instance;
        if (bm == null) { Fail("BossManager がありません"); yield break; }
        bm.DebugCaveEncounter(kind, tier);
        Status = $"{currentLabel} 開始(DEBUG → 洞窟ボス試験 で段階/必殺技/BREAK/ラン再開)";
        Launching = false;
    }

    // ---- 戦闘中の強制操作(DEBUGパネルから) ----
    public static WildBossBase FirstLivingBoss()
    {
        foreach (var b in Object.FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (b != null && !b.IsDead && b.isActiveAndEnabled && !b.NetPuppet) return b;
        return null;
    }
}
#endif
