using System;
using System.Collections;
using UnityEngine;

// OneMoreMile Presentation pass - a single shared full-screen transition
// used for every screen change in this project (TOP<->GAME, TOP<->DECK,
// RESULT->TOP), instead of each screen either cutting instantly or writing
// its own one-off fade. Deliberately map-agnostic - no clouds/grass/sky
// here, only the shared OneMoreMile UI palette (navy / thin gold line /
// pale blue-cyan glow, same family as UiBackdrop/OrnateUi) - a future
// per-map accent (sky clouds, underground dust, snow, leaves) is meant to
// layer ON TOP of this later as an optional add-on (see "将来のMap Effect"
// in the brief), never replace this common transition.
//
// Deliberately NOT used for the Level Up card sequence (RewardCardSequence)
// - that's a Gameplay-pause event, not a screen change (see the brief's own
// "Level Upには使用しない" section) - and NOT used for Player death itself
// (Death Effect/SE/BGM fade play first, untouched; only the later
// RESULT->TOP hop uses this).
//
// Rendered entirely through OnGUI - a big rotated solid-color rect (the
// Navy mask) plus two thinner rotated strips riding its leading edge (Blue
// glow, Gold line) - no shader, no extra Camera/RenderTexture, cheap enough
// for mobile. GameManager calls DrawOverlay() as the LAST line of its own
// OnGUI (the exact slot the old plain start-transition overlay used),
// which is what guarantees this draws on top of literally everything else
// on screen, including the DECK screen's separate uGUI Canvas - Canvas
// rendering always happens during the normal camera render, and OnGUI is a
// separate immediate-mode pass drawn after that, every frame, regardless of
// any Canvas sort order.
public class ScreenTransitionManager : MonoBehaviour
{
    public static ScreenTransitionManager Instance { get; private set; }

    [Header("Timing (seconds) - see the brief: total ~0.5-0.7s target")]
    public float closeDuration = 0.25f;
    public float holdDuration = 0.08f;
    public float openDuration = 0.25f;

    [Header("Gold Line")]
    public float goldLineWidth = 6f;
    [Range(0f, 1f)] public float goldLineAlpha = 0.9f;
    public float goldGlowWidth = 26f;
    [Range(0f, 1f)] public float goldGlowAlpha = 0.35f;
    public Color goldColor = new Color(0.95f, 0.83f, 0.45f);

    [Header("Blue/Cyan Glow")]
    public float blueGlowWidth = 46f;
    [Range(0f, 1f)] public float blueGlowAlpha = 0.4f;
    public Color blueColor = new Color(0.55f, 0.85f, 1f);

    [Header("Navy Mask")]
    // Same family as UiBackdrop.NavyFill - kept as its own field (not a
    // shared reference) since UiBackdrop's texture/color pair is private.
    public Color navyColor = new Color(0.06f, 0.08f, 0.17f, 1f);

    [Header("Angle / Direction")]
    // Degrees, rotates the whole wipe (mask + line + glow) around screen
    // center - 0 = a vertical boundary sweeping horizontally; the brief's
    // diagonal "top-right -> bottom-left" look sits around 30-40.
    public float transitionAngle = 32f;

    [Header("Easing (optional)")]
    public AnimationCurve closeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    public AnimationCurve openCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("SE (optional - safe if unassigned, see AudioManager.PlaySfx)")]
    public AudioClip closeSfx;
    public AudioClip openSfx;

    // True for the entire Close->Hold->Open span - callers (GameManager's
    // WasTappedOrClicked, DeckEditUI.Update) check this and ignore input
    // while true, so a double-tap can't fire a second transition or a
    // second Scene/UI flip mid-animation.
    public bool IsTransitioning { get; private set; }

    // coverage: 0 = fully uncovered, 1 = fully covered (navy fills screen).
    float coverage;
    // The Gold Line/Blue Glow only actually render while the boundary is
    // mid-sweep (Close/Open) - hidden during Hold, since at 100% coverage
    // there's no visible edge to draw it on (see HoldRoutine).
    bool showLine;

    // Survives a SceneManager.LoadScene reload (the RESULT->TOP path, which
    // is a real scene reload rather than a flag flip - see
    // PlayCloseThenReload) - plain static C# state, not tied to any
    // GameObject's lifetime, so it's still true after the old scene (and
    // this component) is destroyed and a fresh one loads in. The fresh
    // instance's Awake() below checks it and, if true, starts already
    // fully covered (no 1-frame flash of the raw fresh title screen) and
    // immediately plays its own Open half.
    static bool resumeOpenAfterReload;

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
        if (resumeOpenAfterReload)
        {
            resumeOpenAfterReload = false;
            coverage = 1f;
            showLine = false;
            IsTransitioning = true;
            StartCoroutine(OpenRoutine());
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // The main entry point every TOP<->GAME / TOP<->DECK call site uses.
    // onFullyCovered fires exactly once, the instant the screen is 100%
    // navy (before a single frame of the new screen/state has been drawn) -
    // flip HasStarted/deckEditOpen/root.SetActive/etc. INSIDE that
    // callback, never before calling this and never after.
    public void PlayTransition(Action onFullyCovered)
    {
        if (IsTransitioning) return; // ignore a double-tap rather than stacking a second run
        StartCoroutine(FullRoutine(onFullyCovered));
    }

    // Used only by the RESULT->TOP path (an actual SceneManager.LoadScene,
    // not a flag flip) - plays Close+Hold, then invokes onReload (expected
    // to call SceneManager.LoadScene) instead of continuing on to Open
    // itself; the FRESH scene's own ScreenTransitionManager instance picks
    // up the Open half via resumeOpenAfterReload above.
    public void PlayCloseThenReload(Action onReload)
    {
        if (IsTransitioning) return;
        StartCoroutine(CloseThenReloadRoutine(onReload));
    }

    IEnumerator FullRoutine(Action onFullyCovered)
    {
        IsTransitioning = true;
        yield return CloseRoutine();
        onFullyCovered?.Invoke();
        yield return HoldRoutine();
        yield return OpenRoutine();
        IsTransitioning = false;
    }

    IEnumerator CloseThenReloadRoutine(Action onReload)
    {
        IsTransitioning = true;
        yield return CloseRoutine();
        yield return HoldRoutine();
        resumeOpenAfterReload = true;
        onReload?.Invoke(); // expected to call SceneManager.LoadScene - this component is destroyed right after
    }

    IEnumerator CloseRoutine()
    {
        showLine = true;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx(AudioManager.Se(SeId.ScreenClose, closeSfx));
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, closeDuration);
            coverage = closeCurve.Evaluate(Mathf.Clamp01(t));
            yield return null;
        }
        coverage = 1f;
    }

    IEnumerator HoldRoutine()
    {
        showLine = false; // fully covered - no boundary edge visible, just solid navy
        yield return new WaitForSecondsRealtime(holdDuration);
    }

    IEnumerator OpenRoutine()
    {
        showLine = true;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx(AudioManager.Se(SeId.ScreenOpen, openSfx));
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, openDuration);
            coverage = 1f - openCurve.Evaluate(Mathf.Clamp01(t));
            yield return null;
        }
        coverage = 0f;
        showLine = false;
        IsTransitioning = false;
    }

    // Called by GameManager as the very last line of OnGUI (every branch -
    // title/gameplay/results - see DrawStartTransitionOverlay).
    public void DrawOverlay()
    {
        if (coverage <= 0.0001f) return;

        float screenDiag = Mathf.Sqrt(Screen.width * (float)Screen.width + Screen.height * (float)Screen.height);
        Vector2 pivot = new Vector2(Screen.width / 2f, Screen.height / 2f);
        // How far off-screen the mask sits at coverage=0, and how oversized
        // the mask rect is so rotating it never leaves a visible gap at any
        // angle/aspect ratio.
        float travel = screenDiag * 0.65f;
        float bigSize = screenDiag * 2.2f;

        // The mask's leading (near) edge position along local +X in the
        // rotated frame set up below - Lerp(travel, -bigSize/2, coverage)
        // slides it from fully off-screen (coverage 0) to centered/fully-
        // covering (coverage 1, where the rect's near edge has passed well
        // beyond the pivot).
        float leadingX = Mathf.Lerp(travel, -bigSize * 0.5f, coverage);

        Matrix4x4 savedMatrix = GUI.matrix;
        Color savedColor = GUI.color;

        GUIUtility.RotateAroundPivot(transitionAngle, pivot);

        Rect MaskStrip(float width) => new Rect(pivot.x + leadingX - width * 0.5f, pivot.y - bigSize * 0.5f, width, bigSize);

        if (showLine)
        {
            // Wide, faint Blue/Cyan glow first, then the narrower, brighter
            // Gold glow, then the thin Gold line itself on top - each
            // straddles the same boundary position so together they read
            // as one soft-edged line rather than 3 separate bands.
            GUI.color = new Color(blueColor.r, blueColor.g, blueColor.b, blueGlowAlpha);
            GUI.DrawTexture(MaskStrip(blueGlowWidth), WhiteTex());

            GUI.color = new Color(goldColor.r, goldColor.g, goldColor.b, goldGlowAlpha);
            GUI.DrawTexture(MaskStrip(goldGlowWidth), WhiteTex());

            GUI.color = new Color(goldColor.r, goldColor.g, goldColor.b, goldLineAlpha);
            GUI.DrawTexture(MaskStrip(goldLineWidth), WhiteTex());
        }

        // Navy mask - the big rotated rect trailing behind the line, drawn
        // last so it sits on top of the line's far (already-covered) half,
        // leaving only the near half of the glow/line visible ahead of it -
        // "Gold Lineの後ろ側を濃紺のMaskが覆っていきます".
        GUI.color = navyColor;
        GUI.DrawTexture(new Rect(pivot.x + leadingX, pivot.y - bigSize * 0.5f, bigSize, bigSize), WhiteTex());

        GUI.color = savedColor;
        GUI.matrix = savedMatrix;
    }
}
