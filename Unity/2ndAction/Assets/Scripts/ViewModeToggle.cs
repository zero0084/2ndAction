using UnityEngine;

// Vertical Mode Prototype (2026-09-08) - one-key switch between the
// existing Landscape camera (CameraFollow, untouched) and the new
// PortraitCameraRig, so the two can be compared side by side without a
// separate build - "Editor上で簡単にLandscape/Portraitを切り替えて比較で
// きるように" from the brief.
//
// NOTE: Screen.orientation only actually reorients a real device (or the
// Simulator window) - it has no effect on the Editor's own Game View
// aspect ratio. When comparing modes in the Editor, also set the Game
// View tab's own aspect dropdown to a Portrait preset (e.g. "9:16" or a
// phone profile) by hand after toggling into Portrait mode.
public class ViewModeToggle : MonoBehaviour
{
    public Camera landscapeCam;
    public PortraitCameraRig portraitRig;
    public KeyCode toggleKey = KeyCode.V;

    public bool PortraitActive { get; private set; }

    void Start()
    {
        // Deliberately does NOT touch Screen.orientation here - only the
        // key-press path below does. GameManager.Awake() already sets
        // Screen.orientation from the player's own PreferredOrientation
        // setting, and Awake always runs before this Start() - unconditionally
        // overwriting it here on every scene load would fight that existing
        // system. The initial camera state (Landscape active, matching
        // PortraitActive's default false) is safe to (redundantly) apply.
        ApplyCameras();
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) Toggle();
    }

    void ApplyCameras()
    {
        if (landscapeCam != null) landscapeCam.enabled = !PortraitActive;
        AudioListener landscapeListener = landscapeCam != null ? landscapeCam.GetComponent<AudioListener>() : null;
        if (landscapeListener != null) landscapeListener.enabled = !PortraitActive;

        if (portraitRig != null) portraitRig.SetActive(PortraitActive);
    }

    void Toggle()
    {
        PortraitActive = !PortraitActive;
        ApplyCameras();
        Screen.orientation = PortraitActive ? ScreenOrientation.Portrait : ScreenOrientation.LandscapeLeft;
        Debug.Log($"[ViewMode] Switched to {(PortraitActive ? "Portrait (斜め上視点)" : "Landscape")}");
    }

    // On-device convenience (a real phone has no 'V' key) - a small
    // DebugMode-gated corner button, same convention as the existing
    // Distance Warp debug UI (Debug Mode toggled from the title screen
    // settings panel). Not meant to ship in a "real" build - this whole
    // class only exists for this camera-comparison prototype.
    void OnGUI()
    {
        if (GameManager.Instance == null || !GameManager.Instance.DebugMode) return;
        if (GameManager.Instance.IsGameOver) return;

        Rect r = new Rect(Screen.width - 170f, Screen.height - 50f, 160f, 40f);
        if (GUI.Button(r, PortraitActive ? "VIEW: Portrait" : "VIEW: Landscape"))
        {
            Toggle();
        }
    }
}
