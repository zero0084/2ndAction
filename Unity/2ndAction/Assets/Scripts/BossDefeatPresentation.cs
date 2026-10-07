using System.Collections;
using UnityEngine;

// OneMoreMile Presentation pass - the encounter-level "Boss撃破 -> GAME
// CLEAR" beat, played once the LAST boss of a checkpoint's encounter has
// finished its own individual death presentation (see DragonController/
// MajinController.FinalHitAndDie - each boss's own Final Hit/Death/HP Bar
// fade plays independently; THIS class only starts once BossManager's
// CheckEncounterComplete sees every boss in the encounter is down).
//
// Deliberately does NOT pause gameplay or lock input - "Boss撃破後もGame-
// playを継続できる仕様を維持" per the brief - this is a HUD-level text/glow
// sequence layered over gameplay that keeps running underneath, not a
// blocking cutscene. Generic over "which milestone" the same way
// BossMilestonePresentation is - isFirstBossDefeat (checkpoint 1 only)
// picks whether GAME CLEAR + KEEP RUNNING play at all; every later
// checkpoint (2000m, 3000m, ...) only shows the short "Xm CLEAR" beat.
//
// Dragon HP/Attack/FireBreath/Reflect/Damage/Boss Spawn Distance/
// Difficulty/Card/Deck/EXP/Player Physics/Game Balance are all completely
// untouched - BossManager.CheckEncounterComplete still does everything it
// always did (IsBossPhase=false, nextBossDistance advance) before ever
// calling into this class.
public class BossDefeatPresentation : MonoBehaviour
{
    public static BossDefeatPresentation Instance { get; private set; }

    // Presentation Priority pass integration - GameManager checks this
    // before starting a Level Up, same as BossMilestonePresentation.
    // IsRunning above BossMilestonePresentation in priority makes no
    // practical difference (the two never overlap - a milestone's own
    // presentation always finishes long before that boss is ever killed),
    // but both are checked together (see GameManager.IsBossPresentationActive).
    public bool IsRunning { get; private set; }

    [Header("Screen Atmosphere")]
    public float atmosphereGlowDuration = 0.35f;
    [Range(0f, 1f)] public float atmosphereGlowAlpha = 0.22f;
    public Color atmosphereGlowGoldColor = new Color(0.95f, 0.83f, 0.45f);
    public Color atmosphereGlowBlueColor = new Color(0.55f, 0.85f, 1f);

    [Header("Milestone Clear")]
    public float milestoneClearDuration = 0.8f;
    public Color milestoneClearColor = Color.white;
    public Color milestoneClearAccentColor = new Color(0.95f, 0.83f, 0.45f);

    [Header("Game Clear (first boss defeat only)")]
    public float gameClearDuration = 1f;
    public string gameClearText = "GAME CLEAR";

    [Header("Keep Running (first boss defeat only)")]
    public float keepRunningDuration = 1f;
    public string keepRunningText = "KEEP RUNNING →";

    [Header("BGM Duck (cosmetic - never touches BgmVolumeLevel/PlayerPrefs)")]
    [Range(0f, 1f)] public float bgmDuckVolume = 0.4f;
    public float bgmDuckFadeDuration = 0.15f;
    public float bgmRecoverDuration = 0.6f;

    // No dedicated SE yet - PlaySfx is null-safe, so every field below can
    // stay unassigned without breaking anything.
    [Header("Audio (optional)")]
    public AudioClip milestoneClearSe;

    float glowAlpha;
    float milestoneAlpha;
    float milestoneScale = 1f;
    string milestoneLabel = "";
    float gameClearAlpha;
    float gameClearScale = 1f;
    float keepRunningAlpha;

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

    void LogDefeat(string message)
    {
        if (GameManager.Instance != null && GameManager.Instance.DebugMode) Debug.Log(message);
    }

    // milestoneDistance: the checkpoint distance just cleared (1000, 2000,
    // ...), used verbatim in the "Xm CLEAR" text - never hardcoded to
    // 1000m (item 9 of the brief). isFirstBossDefeat gates GAME CLEAR/KEEP
    // RUNNING (checkpoint 1 only).
    public void Play(float milestoneDistance, bool isFirstBossDefeat)
    {
        if (IsRunning) return; // shouldn't happen - BossManager only calls once per fully-cleared encounter

        // Item 12 - Player Death/Game Clear outranks Boss Defeat: if the
        // run already ended (e.g. the player died the same moment this
        // last boss did), skip the Clear text entirely rather than
        // celebrating over a death/result screen.
        if (GameManager.Instance != null && GameManager.Instance.IsGameOver)
        {
            LogDefeat("[BossDefeat] Presentation skipped - run already ended");
            return;
        }

        StartCoroutine(RunPresentation(milestoneDistance, isFirstBossDefeat));
    }

    // Bugfix 2026-09-07 (Bug #001, root cause) - this coroutine used to have
    // NO exception/early-exit protection at all: IsRunning was only ever set
    // back to false at the very bottom, after every yield above it had
    // already completed successfully. If ANYTHING threw partway through (or
    // this GameObject/its dependencies got disabled mid-sequence), IsRunning
    // would stay stuck true FOREVER - and since IsRunning feeds directly
    // into GameManager.IsBossPresentationActive(), which
    // TriggerBossRewardChoice checks before ever showing the Boss Reward
    // card choice, a stuck-true IsRunning here would (a) defer THIS
    // encounter's own Boss Reward indefinitely (recoverable only via the
    // 12s bossRewardStuckTimer safety net) AND (b) - since Instance/IsRunning
    // is a single persistent flag, not reset per-encounter - permanently
    // poison every SUBSEQUENT boss encounter's Boss Reward too, each one
    // stalling behind that same 12s timeout forever after. Wrapped in
    // try/finally (BossMilestonePresentation, its sibling class, already had
    // this exact protection - this was the one Presentation class that had
    // been missed).
    IEnumerator RunPresentation(float milestoneDistance, bool isFirstBossDefeat)
    {
        IsRunning = true;
        if (GameManager.Instance != null) GameManager.Instance.LogBoss("DefeatPresentationStart");
        try
        {
            LogDefeat($"[BossDefeat] Milestone cleared: {Mathf.RoundToInt(milestoneDistance)}m");

            // ===== Item 5 - Screen Atmosphere: clear any residual Boss
            // Milestone dark overlay (almost always already 0 by this point -
            // that presentation finishes long before a boss fight ends - but
            // "念のため"), then one brief Gold/Blue glow pulse. =====
            if (BossMilestonePresentation.Instance != null) BossMilestonePresentation.Instance.ForceClearAtmosphere();
            if (AudioManager.Instance != null) AudioManager.Instance.DuckBgm(bgmDuckVolume, bgmDuckFadeDuration);
            yield return PulseGlow(atmosphereGlowDuration);

            // ===== Item 6 - "Xm CLEAR" =====
            PlaySfx(AudioManager.Se(SeId.MilestoneClear, milestoneClearSe));
            milestoneLabel = $"{Mathf.RoundToInt(milestoneDistance):N0}m CLEAR";
            yield return ShowMilestoneClear(milestoneClearDuration);

            if (isFirstBossDefeat)
            {
                // ===== Item 7 - GAME CLEAR (first boss defeat only) =====
                yield return ShowGameClear(gameClearDuration);
                LogDefeat("[BossDefeat] First game clear achieved");

                // ===== Item 8 - "this run can keep going" =====
                yield return ShowKeepRunning(keepRunningDuration);
            }

            if (AudioManager.Instance != null) AudioManager.Instance.UnduckBgm(bgmRecoverDuration);

            LogDefeat("[BossDefeat] Presentation finished");
            LogDefeat("[BossDefeat] Gameplay resumed");
        }
        finally
        {
            IsRunning = false;
            if (GameManager.Instance != null) GameManager.Instance.LogBoss("DefeatPresentationEnd");
        }
    }

    IEnumerator PulseGlow(float duration)
    {
        float halfIn = duration * 0.4f;
        float halfOut = duration - halfIn;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, halfIn);
            glowAlpha = Mathf.Lerp(0f, atmosphereGlowAlpha, Mathf.Clamp01(t));
            yield return null;
        }
        t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, halfOut);
            glowAlpha = Mathf.Lerp(atmosphereGlowAlpha, 0f, Mathf.Clamp01(t));
            yield return null;
        }
        glowAlpha = 0f;
    }

    IEnumerator ShowMilestoneClear(float duration)
    {
        yield return PopText(duration, a => milestoneAlpha = a, s => milestoneScale = s);
        milestoneAlpha = 0f;
    }

    IEnumerator ShowGameClear(float duration)
    {
        yield return PopText(duration, a => gameClearAlpha = a, s => gameClearScale = s);
        gameClearAlpha = 0f;
    }

    IEnumerator ShowKeepRunning(float duration)
    {
        float fadeIn = duration * 0.25f;
        float hold = duration * 0.45f;
        float fadeOut = Mathf.Max(0.05f, duration - fadeIn - hold);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, fadeIn);
            keepRunningAlpha = Mathf.Clamp01(t);
            yield return null;
        }
        keepRunningAlpha = 1f;
        if (hold > 0f) yield return new WaitForSecondsRealtime(hold);
        t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, fadeOut);
            keepRunningAlpha = 1f - Mathf.Clamp01(t);
            yield return null;
        }
        keepRunningAlpha = 0f;
    }

    // Shared "Scale 0.8 -> 1.08 -> 1.0, Alpha in -> hold -> out" shape used
    // by both Xm CLEAR and GAME CLEAR (items 6/7 use nearly identical Pop
    // choreography, just different text/duration/first-only gating).
    IEnumerator PopText(float duration, System.Action<float> setAlpha, System.Action<float> setScale)
    {
        float popIn = duration * 0.3f;
        float hold = duration * 0.4f;
        float fadeOut = Mathf.Max(0.05f, duration - popIn - hold);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, popIn);
            float f = Mathf.Clamp01(t);
            float scale = f < 0.7f ? Mathf.Lerp(0.8f, 1.08f, f / 0.7f) : Mathf.Lerp(1.08f, 1f, (f - 0.7f) / 0.3f);
            setScale(scale);
            setAlpha(f);
            yield return null;
        }
        setAlpha(1f);
        setScale(1f);
        if (hold > 0f) yield return new WaitForSecondsRealtime(hold);

        t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, fadeOut);
            setAlpha(1f - Mathf.Clamp01(t));
            yield return null;
        }
    }

    // Called by GameManager as part of its own mid-run OnGUI branch (same
    // slot as BossMilestonePresentation.DrawOverlay) - so this stops being
    // drawn the instant IsGameOver flips true (GameManager's OnGUI takes a
    // different branch then), even if this coroutine is still finishing up
    // in the background - see item 12's "同時に開始されないように" via the
    // IsGameOver guard in Play() above, and this draw-call gating for the
    // reverse timing (Player dies WHILE this is already showing).
    public void DrawOverlay()
    {
        if (glowAlpha > 0.001f)
        {
            Color prev = GUI.color;
            float halfW = Screen.width * 0.5f;
            float halfH = Screen.height * 0.5f;
            GUI.color = new Color(atmosphereGlowBlueColor.r, atmosphereGlowBlueColor.g, atmosphereGlowBlueColor.b, glowAlpha * 0.5f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), WhiteTex());
            GUI.color = new Color(atmosphereGlowGoldColor.r, atmosphereGlowGoldColor.g, atmosphereGlowGoldColor.b, glowAlpha * 0.5f);
            GUI.DrawTexture(new Rect(halfW - halfW * 0.6f, halfH - halfH * 0.6f, halfW * 1.2f, halfH * 1.2f), WhiteTex());
            GUI.color = prev;
        }

        if (milestoneAlpha > 0.001f)
        {
            DrawPopLabel(milestoneLabel, Screen.height * 0.4f, 44, milestoneScale, milestoneAlpha, milestoneClearColor, milestoneClearAccentColor);
        }

        if (gameClearAlpha > 0.001f)
        {
            DrawPopLabel(gameClearText, Screen.height * 0.4f, 56, gameClearScale, gameClearAlpha, Color.white, milestoneClearAccentColor);
        }

        if (keepRunningAlpha > 0.001f)
        {
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 22;
            style.fontStyle = FontStyle.Bold;
            style.alignment = TextAnchor.MiddleCenter;
            style.normal.textColor = new Color(0.85f, 0.9f, 1f, keepRunningAlpha);
            LocGUI.Label(new Rect(0f, Screen.height * 0.4f + 60f, Screen.width, 34f), keepRunningText, style);
        }
    }

    // White base text with a thin gold-accented glow band behind it and a
    // pop scale - same OneMoreMile vocabulary (white text + gold accent +
    // blue/cyan glow) as everywhere else, via GUIUtility.ScaleAroundPivot
    // so the pop actually grows from its own center rather than the
    // screen's.
    void DrawPopLabel(string text, float centerY, int fontSize, float scale, float alpha, Color textColor, Color accentColor)
    {
        Matrix4x4 savedMatrix = GUI.matrix;
        Vector2 pivot = new Vector2(Screen.width / 2f, centerY);
        GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), pivot);

        Color prevColor = GUI.color;
        GUI.color = new Color(accentColor.r, accentColor.g, accentColor.b, alpha * 0.3f);
        GUI.DrawTexture(new Rect(0f, centerY - fontSize * 0.5f - 6f, Screen.width, fontSize + 12f), WhiteTex());
        GUI.color = prevColor;

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = fontSize;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;
        style.normal.textColor = new Color(textColor.r, textColor.g, textColor.b, alpha);
        LocGUI.Label(new Rect(0f, centerY - fontSize * 0.7f, Screen.width, fontSize * 1.4f), text, style);

        GUI.matrix = savedMatrix;
    }
}
