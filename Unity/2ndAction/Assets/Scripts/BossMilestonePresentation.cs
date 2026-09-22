using System.Collections;
using UnityEngine;

// OneMoreMile Presentation pass - the short "山場" beat between normal
// running and a boss encounter: Distance pop -> brief tempo slowdown ->
// screen darkens -> WARNING -> gameplay speed restored (so the boss's own
// EXISTING entrance animation plays at normal speed) -> boss spawns
// (BossManager.StartBossPhase, completely untouched) -> boss(es) fly in and
// reveal their own HP bar / one small camera shake on arrival (see
// DragonController/MajinController.ArrivalPresentation) -> darken clears.
//
// Deliberately generic over "which milestone" (1000m, 2000m, 3000m, ...)
// rather than hardcoded to 1000m - BossManager passes isFirstEncounter so
// firstBossPresentationDuration/repeatBossPresentationDuration can differ,
// but the flow itself (Play(...)) is identical every time - one shared
// BossMilestonePresentation, not "1000m-only code" (item 13 of the brief).
//
// Boss出現距離/HP/Attack/FireBreath/Reflect/Damage/Reward/出現ルール/
// GameClear/DifficultyScaling are completely untouched - this only ever
// wraps BossManager's own StartBossPhase as an onSpawnBeat callback, the
// same "Presentation calls back into the untouched game logic exactly
// once" contract as RewardCardSequence/GameManager.ApplyUpgradeByCardId.
public class BossMilestonePresentation : MonoBehaviour
{
    public static BossMilestonePresentation Instance { get; private set; }

    [Header("Timing - overall (item 14 - Inspector tunable)")]
    public float firstBossPresentationDuration = 2f;
    public float repeatBossPresentationDuration = 1.1f;

    // How the overall duration above splits across the 3 timed beats below;
    // whatever's left over is a final hold (screen still dark) while the
    // boss's own entrance/arrival plays out.
    [Header("Timing - breakdown (fractions of the overall duration)")]
    [Range(0f, 1f)] public float milestonePopFraction = 0.18f;
    [Range(0f, 1f)] public float tempoDownFraction = 0.22f;
    [Range(0f, 1f)] public float warningFraction = 0.35f;

    [Header("Milestone Pop (Distance panel)")]
    public float milestonePopDuration = 0.4f;

    [Header("Gameplay Tempo Change")]
    [Range(0f, 1f)] public float tempoMidScale = 0.4f;

    [Header("Screen Atmosphere")]
    [Range(0f, 1f)] public float screenDarkAlpha = 0.22f;
    public Color screenDarkColor = new Color(0.04f, 0.05f, 0.1f);

    [Header("Boss Warning")]
    public float warningDuration = 0.65f;
    public string warningText = "BOSS APPROACHING";
    public Color warningGoldColor = new Color(0.95f, 0.83f, 0.45f);
    public Color warningBlueColor = new Color(0.55f, 0.85f, 1f);

    [Header("BGM Duck (cosmetic - never touches BgmVolumeLevel/PlayerPrefs)")]
    [Range(0f, 1f)] public float bgmDuckLevel = 0.65f;
    public float bgmDuckFadeDuration = 0.25f;

    // No dedicated SE yet - PlaySfx is null-safe, so every field below can
    // stay unassigned without breaking anything.
    [Header("Audio (optional)")]
    public AudioClip milestoneSe;
    public AudioClip bossWarningSe;
    public AudioClip bossAppearSe;

    [Header("Debug")]
    public bool debugLogEnabled = true;

    bool running;
    // Presentation Priority pass - GameManager checks this before starting
    // a Level Up card sequence, deferring it instead of letting the two
    // Presentations run concurrently (see GameManager.TriggerLevelUpChoice/
    // RunLevelUpChoice). Boss Spawn/Defeat outranks Level Up per the brief.
    public bool IsRunning => running;
    float darkAlpha;
    float warningAlpha;
    float warningLineProgress;

    static Texture2D whiteTex;
    static Texture2D WhiteTex()
    {
        if (whiteTex == null)
        {
            whiteTex = new Texture2D(1, 1);
            whiteTex.SetPixel(0, 0, Color.white);
            whiteTex.Apply();
        }
        return whiteTex;
    }

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void PlaySfx(AudioClip clip)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx(clip);
    }

    void LogPresentation(string message)
    {
        if (debugLogEnabled) Debug.Log(message);
    }

    // milestoneDistance in meters (log only), isFirstEncounter picks which
    // overall duration to target, onSpawnBeat is BossManager's own
    // StartBossPhase - called exactly once, right as gameplay speed is
    // restored.
    public void Play(float milestoneDistance, bool isFirstEncounter, System.Action onSpawnBeat)
    {
        if (running)
        {
            // Should not happen (BossManager guards re-entry with
            // IsBossPhase before ever calling this) but fail-safe (item 17)
            // - never leave a boss spawn stranded behind an already-running
            // presentation.
            onSpawnBeat?.Invoke();
            return;
        }
        StartCoroutine(RunPresentation(milestoneDistance, isFirstEncounter, onSpawnBeat));
    }

    IEnumerator RunPresentation(float milestoneDistance, bool isFirstEncounter, System.Action onSpawnBeat)
    {
        running = true;
        bool spawned = false;
        void SpawnOnce()
        {
            if (spawned) return;
            spawned = true;
            onSpawnBeat?.Invoke();
            LogPresentation("[BossPresentation] Boss spawned");
        }

        LogPresentation($"[BossPresentation] Milestone reached: {Mathf.RoundToInt(milestoneDistance)}m");
        LogPresentation("[BossPresentation] Started");
        if (GameManager.Instance != null) GameManager.Instance.LogBoss("SpawnPresentationStart");

        if (GameManager.Instance != null) GameManager.Instance.SetPresentationDamageLock(true);

        float totalDuration = isFirstEncounter ? firstBossPresentationDuration : repeatBossPresentationDuration;
        float capturedTimeScale = Time.timeScale;
        // 高速走行中のフリーズ/ワープ調査(2026-09-22) - このクラスは
        // Time.timeScaleへ直接連続的な値(1.0→中間値→0)を書き込む独自の
        // 演出用ランプであり、TimeControl(0/1の二値のみ)には一本化して
        // いない。既存のIsBossPresentationActive()によりLevel Up/Boss
        // Reward/Pause Menuの開始はこの演出中は既に抑制されているため
        // 実害は確認していないが、記録だけは残す(挙動は変更しない)。
        FreezeDiagnostics.LogEvent($"[BossPresentation] TimeScale ramp begin captured={capturedTimeScale:F2}");

        // Fail-safe (item 17): wrapped so ANY early exit still restores
        // timeScale/damage-lock and still spawns the boss rather than
        // leaving the run stuck paused - same try/finally reasoning as
        // HitStop.Freeze.
        try
        {
            PlaySfx(milestoneSe);

            // ===== 1. Milestone Distance pop =====
            yield return MilestonePop(Mathf.Max(0.15f, totalDuration * milestonePopFraction));

            // ===== 2. Gameplay tempo change: 1.0 -> tempoMidScale -> 0 =====
            float tempoDuration = Mathf.Max(0.1f, totalDuration * tempoDownFraction);
            yield return TempoDown(tempoDuration);

            // ===== 3. Screen atmosphere darkens (fire-and-forget, overlaps
            // with the Warning beat below rather than blocking it) =====
            StartCoroutine(FadeDark(screenDarkAlpha, tempoDuration * 0.6f));

            if (AudioManager.Instance != null) AudioManager.Instance.DuckBgm(bgmDuckLevel, bgmDuckFadeDuration);

            // ===== 4. Boss Warning =====
            PlaySfx(bossWarningSe);
            yield return PlayWarning(Mathf.Max(0.2f, totalDuration * warningFraction));
            LogPresentation("[BossPresentation] Warning shown");

            // Restore normal speed BEFORE the boss actually spawns, so its
            // own existing entrance animation (DragonController/
            // MajinController.ReturnToHome, which runs on scaled
            // Time.deltaTime) plays out at normal speed instead of
            // crawling through whatever's left of the slowdown.
            // Bug #001 診断フェーズ - already a no-op while
            // DisableBossTimeScalePresentation is on (nothing above ever
            // moved it off 1 in that case either), kept unconditional for
            // clarity/symmetry with the other 3 touch-points below.
            Time.timeScale = 1f;
            if (GameManager.Instance != null) GameManager.Instance.LogBoss("TimeScale = 1");
            if (GameManager.Instance != null) GameManager.Instance.LogBoss("SpawnPresentationEnd");

            PlaySfx(bossAppearSe);
            SpawnOnce();

            // Hold the dark atmosphere a moment so the boss's own entrance/
            // HP-bar-reveal/shake is still readable against a slightly
            // darkened screen, then clear it below.
            float spent = totalDuration * (milestonePopFraction + tempoDownFraction + warningFraction);
            float holdRemaining = Mathf.Max(0f, totalDuration - spent);
            if (holdRemaining > 0f) yield return new WaitForSecondsRealtime(holdRemaining);
        }
        finally
        {
            SpawnOnce(); // no-op if already spawned above - guarantees Boss Spawn is always reached even on an early exit
            Time.timeScale = capturedTimeScale > 0f ? capturedTimeScale : 1f;
            FreezeDiagnostics.LogEvent($"[BossPresentation] TimeScale ramp end restored={Time.timeScale:F2}");
            if (GameManager.Instance != null) GameManager.Instance.SetPresentationDamageLock(false);
            if (AudioManager.Instance != null) AudioManager.Instance.UnduckBgm(bgmDuckFadeDuration);
        }

        yield return FadeDark(0f, 0.3f);
        LogPresentation("[BossPresentation] Finished");
        LogPresentation("[BossPresentation] Gameplay resumed");
        running = false;
    }

    IEnumerator MilestonePop(float duration)
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, duration);
            float f = Mathf.Clamp01(t);
            // Triangular 0 -> 1 -> 0 shape, fed straight into
            // GameManager.DrawStatPanel's own flashIntensity blend (Scale
            // 1.0->1.25->1.0, Color White->Gold->White).
            float intensity = f < 0.5f ? f / 0.5f : 1f - (f - 0.5f) / 0.5f;
            if (GameManager.Instance != null) GameManager.Instance.DistanceFlashIntensity = intensity;
            yield return null;
        }
        if (GameManager.Instance != null) GameManager.Instance.DistanceFlashIntensity = 0f;
    }

    // "TimeScale相当 1.0 -> 0.3〜0.5 -> 0" - a real Time.timeScale ramp (not
    // a fake visual slow-mo), so it stays consistent with everything else
    // in this project that already treats timeScale==0 as "paused" (Level
    // Up, HitStop). Timed with unscaledDeltaTime throughout so the ramp's
    // own pacing isn't affected by the very timeScale it's changing.
    // Bug #001 診断フェーズ (2026-09-08), 項目7 - "DisableBossTimeScalePresentation"
    // 比較Toggle。ONの間はこのメソッドがTime.timeScaleへ一切書き込まない
    // (常に1のまま) - Warning UI/暗転/BGM Duckといった他の演出ビートは
    // Play()側で完全に別処理(FadeDark/PlayWarning)のため無関係に再生され
    // 続ける。TimeScale操作自体がFreeze原因かどうかを切り分けるための、
    // 診断専用の分岐(本仕様として削除するものではない)。
    IEnumerator TempoDown(float duration)
    {
        float half = duration * 0.5f;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, half);
            if (!BossDiagnostics.DisableBossTimeScalePresentation) Time.timeScale = Mathf.Lerp(1f, tempoMidScale, Mathf.Clamp01(t));
            yield return null;
        }
        if (GameManager.Instance != null) GameManager.Instance.LogBoss($"TimeScale = {tempoMidScale:F1}");
        t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, half);
            if (!BossDiagnostics.DisableBossTimeScalePresentation) Time.timeScale = Mathf.Lerp(tempoMidScale, 0f, Mathf.Clamp01(t));
            yield return null;
        }
        if (!BossDiagnostics.DisableBossTimeScalePresentation) Time.timeScale = 0f;
        if (GameManager.Instance != null) GameManager.Instance.LogBoss("TimeScale = 0");
    }

    IEnumerator FadeDark(float target, float duration)
    {
        float start = darkAlpha;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, duration);
            darkAlpha = Mathf.Lerp(start, target, Mathf.Clamp01(t));
            yield return null;
        }
        darkAlpha = target;
    }

    IEnumerator PlayWarning(float duration)
    {
        float fadeIn = duration * 0.35f;
        float hold = duration * 0.25f;
        float fadeOut = Mathf.Max(0.05f, duration - fadeIn - hold);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, fadeIn);
            float f = Mathf.Clamp01(t);
            warningAlpha = f;
            warningLineProgress = f;
            yield return null;
        }
        warningAlpha = 1f;
        warningLineProgress = 1f;

        if (hold > 0f) yield return new WaitForSecondsRealtime(hold);

        t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, fadeOut);
            warningAlpha = 1f - Mathf.Clamp01(t);
            yield return null;
        }
        warningAlpha = 0f;
        warningLineProgress = 0f;
    }

    // Boss Defeat Presentation pass - defensive "念のため" clear, called by
    // BossDefeatPresentation right as a boss fight ends. Deliberately does
    // NOT StopAllCoroutines/touch Time.timeScale/PresentationDamageLock -
    // this presentation always finishes (including its own try/finally
    // restoring those) LONG before a boss fight actually ends in every
    // realistic case, so this is just a value overwrite in case any visual
    // trace is somehow still fading, never a state-machine intervention.
    public void ForceClearAtmosphere()
    {
        darkAlpha = 0f;
        warningAlpha = 0f;
        warningLineProgress = 0f;
    }

    // Called by GameManager as part of its own mid-run OnGUI branch (see
    // GameManager.OnGUI) - darken overlay first, then the WARNING line/text
    // on top of it.
    public void DrawOverlay()
    {
        if (darkAlpha > 0.001f)
        {
            Color prev = GUI.color;
            GUI.color = new Color(screenDarkColor.r, screenDarkColor.g, screenDarkColor.b, darkAlpha);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), WhiteTex());
            GUI.color = prev;
        }

        if (warningAlpha > 0.001f)
        {
            float centerY = Screen.height * 0.42f;
            float lineWidth = Screen.width * 0.5f * warningLineProgress;
            Color prev = GUI.color;

            // Wide, faint Blue/Cyan glow first, then the thin Gold line on
            // top - same "glow behind, line on top" vocabulary as
            // ScreenTransitionManager's Gold Slash Wipe, for a consistent
            // OneMoreMile look.
            GUI.color = new Color(warningBlueColor.r, warningBlueColor.g, warningBlueColor.b, 0.3f * warningAlpha);
            GUI.DrawTexture(new Rect(Screen.width / 2f - lineWidth / 2f, centerY - 7f, lineWidth, 14f), WhiteTex());

            GUI.color = new Color(warningGoldColor.r, warningGoldColor.g, warningGoldColor.b, warningAlpha);
            GUI.DrawTexture(new Rect(Screen.width / 2f - lineWidth / 2f, centerY - 1.5f, lineWidth, 3f), WhiteTex());

            GUI.color = prev;

            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 30;
            style.fontStyle = FontStyle.Bold;
            style.alignment = TextAnchor.MiddleCenter;
            style.normal.textColor = new Color(warningGoldColor.r, warningGoldColor.g, warningGoldColor.b, warningAlpha);
            GUI.Label(new Rect(0f, centerY - 50f, Screen.width, 40f), warningText, style);
        }
    }
}
