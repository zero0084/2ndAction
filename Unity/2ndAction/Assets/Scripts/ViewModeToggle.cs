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
        // 2026-10-08: 縦の画面で「斜め上から見る」を選んでいる時だけ斜めのカメラ(設定 PortraitRunView)。回転/設定の変更で当て直す
        PortraitActive = PortraitRunView.UseOblique;
        ApplyCameras();
        PortraitRunView.Changed += Reapply;
        OrientationWatcher.Changed += Reapply;
    }

    void OnDestroy() { PortraitRunView.Changed -= Reapply; OrientationWatcher.Changed -= Reapply; }

    void Reapply()
    {
        if (this == null) return;
        bool want = PortraitRunView.UseOblique;
        if (want == PortraitActive) return;
        PortraitActive = want;
        ApplyCameras();
        Debug.Log($"[ViewMode] {(PortraitActive ? "oblique (portrait)" : "side")}");
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

    // 開発用の切り替え: 縦のラン表示の設定を入れ替える(縦の画面で効く)
    public void Toggle()
    {
        PortraitRunView.Set((PortraitRunView.Mode + 1) % 3); // 横から → 上下2段 → 斜め上(開発用)
        Reapply();
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
        // 2026-10-01: ホームではDEBUGパネルの中から切り替える(部屋の操作対象/本の上に重ねない)。開発版のみ。
        if (!GameManager.Instance.HasStarted || !Debug.isDebugBuild) return;

        Rect r = new Rect(Screen.width - 170f, Screen.height - 50f, 160f, 40f);
        if (GUI.Button(r, PortraitActive ? "VIEW: Portrait" : "VIEW: Landscape"))
        {
            Toggle();
        }
    }
}
