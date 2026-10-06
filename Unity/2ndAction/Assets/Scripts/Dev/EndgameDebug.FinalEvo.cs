#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// FINAL EVOLUTION TEST(2026-10-04)。DEBUGパネル → 「FINAL EVOLUTION TEST…」。開発版だけ。
// 押すたびにシーンを読み直し、DEBUG RUN(保存/記録しない)で選んだキャラ・ステージを始め、選んだカードを
// Lv9(開始時)にし、必要なら READY にしてから走り出す。READY → LEVEL UP の候補 → 選択 → ACTIVE → 終了 → READY → 再び選択 を
// すぐ確かめられる(同じページの「LEVEL UP」で3択を開ける / 「終了→LEVEL UP」で再使用の確認)。2026-10-05: 全対象カード。
public partial class EndgameDebug
{
    public static string SelectedFeChar = "swordsman";
    public static string SelectedFeStage = "wasteland_road";
    public static string SelectedFeCard = "attack_up";
    public static bool FeAwakened, FeLv9 = true, FeReady = true, FeBoss;
    static (string ch, string stage, string card, bool awake, bool lv9, bool ready, bool boss)? pendingFe;
    public bool IsFinalEvoTest { get; private set; }

    // 対象カード(FinalEvolutionTuning のデータから。#100 ULTIMATE は含まない)
    public static string[] FeCards { get { var l = new System.Collections.Generic.List<string>(); foreach (var e in FinalEvolutionTuning.I.entries) if (e != null && !string.IsNullOrEmpty(e.abilityId)) l.Add(e.abilityId); return l.ToArray(); } }
    public static int FeCardPage;
    public static bool FeForceCandidate = true; // 選んだカードの FE を候補に固定(再使用の確認)

    public static void LaunchFinalEvo(string ch, string stage, string card, bool awake, bool lv9, bool ready, bool boss)
    {
        if (Instance == null) return;
        if (Instance.Launching) { Debug.Log("[EndgameDebug] launch ignored (already launching)"); return; }
        pending = null; pendingLong = null; pendingUlt = null; pendingCave = null;
        pendingFe = (ch, stage, card, awake, lv9, ready, boss);
        Instance.Launches++;
        Debug.Log($"[EndgameDebug] LAUNCH FINAL EVOLUTION TEST {card} {ch} {stage} awakened={awake} lv9={lv9} ready={ready} boss={boss}");
        Instance.StopAllCoroutines();
        Instance.keepAlive = false;
        DebugPanel.CloseStatic();
        SafeReset("launch");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    static bool TakePendingFe()
    {
        if (!pendingFe.HasValue) return false;
        var p = pendingFe.Value;
        pendingFe = null;
        Instance.StartCoroutine(Instance.RunLaunchFinalEvo(p.ch, p.stage, p.card, p.awake, p.lv9, p.ready, p.boss));
        return true;
    }

    IEnumerator RunLaunchFinalEvo(string ch, string stage, string card, bool awake, bool lv9, bool ready, bool boss)
    {
        Launching = true;
        IsLongCheck = false; IsUltimateTest = false; IsCaveBossTest = false; IsFinalEvoTest = true;
        currentLabel = $"FINAL EVOLUTION TEST {card}";
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
        FinalEvolution.DebugForceAwakened = awake;
        gm.SetSelectedCharacter(ch);
        gm.DebugStartRunOnStage(stage);
        w = 0f;
        while (!(gm.HasStarted && !gm.CountdownActive) && w < 25f) { yield return null; w += Time.unscaledDeltaTime; }
        if (!gm.HasStarted) { Fail("ランを開始できませんでした"); yield break; }
        ApplyProfile(Profile.Sturdy);
        Warp(gm, boss ? 1955f : 1200f);
        yield return null;
        if (lv9) gm.FinalEvoTestPrepare(card);
        yield return null;
        if (ready) FinalEvolution.DebugMakeReady(card);
        if (boss)
        {
            Status = $"{currentLabel}: ボスの登場を待っています…";
            w = 0f;
            var bm = BossManager.Instance;
            while ((bm == null || bm.AliveBossCount <= 0) && w < 30f) { yield return null; w += Time.deltaTime; }
        }
        Status = $"{currentLabel} 開始(DEBUG → FINAL EVOLUTION TEST の「LEVEL UP」で3択)";
        Launching = false;
    }
}

public partial class GameManager
{
    // FINAL EVOLUTION TEST: その能力をこのランで Lv9 にする(足りない分だけ。通常の取得と同じ上限付きの経路)
    public void FinalEvoTestPrepare(string abilityId)
    {
        var c = CardDatabase.FindBaseById(abilityId);
        if (c == null) return;
        int have = GetAbilityRunStack(abilityId);
        if (have < MaxRunCardLevel) ApplyRunCardCapped(c, MaxRunCardLevel - have, "FinalEvoTest");
        // 封印(SacrificeHearts)のカードは最大HPが足りないと Lv9 まで取れない(通常の規則)→ HEART UP で余裕を作って取り直す
        if (GetAbilityRunStack(abilityId) < MaxRunCardLevel && c.effects != null && c.effects.Exists(e => e.type == EffectType.SacrificeHearts))
        {
            var hu = CardDatabase.FindBaseById("heart_up");
            if (hu != null) ApplyRunCardCapped(hu, MaxRunCardLevel - GetAbilityRunStack("heart_up"), "FinalEvoTest(hearts)");
            have = GetAbilityRunStack(abilityId);
            if (have < MaxRunCardLevel) ApplyRunCardCapped(c, MaxRunCardLevel - have, "FinalEvoTest");
        }
        Debug.Log($"[FinalEvo] test prepared: {abilityId} Lv{GetAbilityRunStack(abilityId)}");
    }
    // 開発用: 通常の LEVEL UP の3択を今すぐ開く(EXP は変えない)
    public void DebugTriggerLevelUp() => TriggerLevelUpChoice();
    public bool DebugChoiceOpen => levelUpPending;
    public string[] DebugPendingChoiceIds { get { if (pendingChoices == null) return new string[0]; var a = new string[pendingChoices.Length]; for (int i = 0; i < a.Length; i++) a[i] = pendingChoices[i] != null ? pendingChoices[i].cardId : ""; return a; } }
}
#endif
