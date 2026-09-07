using UnityEngine;

// Vertical Mode Prototype (2026-09-08) - "縦画面対応・斜め上視点" brief.
// A SECOND camera that exists entirely alongside the original Landscape
// Main Camera/CameraFollow (untouched) - toggled via ViewModeToggle.
// Changes nothing about how gameplay is simulated: every GameObject's real
// transform.position (X=progress, Y=height, Z=0 always) stays exactly as
// the existing horizontal-scroll logic already computes it. This is purely
// a Perspective camera positioned OFF that flat X/Y=Z0 gameplay plane and
// aimed back at it from an oblique angle, so ordinary perspective
// foreshortening does two things for free, with zero changes to any
// gameplay script: (1) things further along +X (farther ahead on the
// course) look smaller/farther away, and (2) because the camera sits
// offset in both -X (behind the player) AND -Z (off the gameplay plane)
// while looking back across both of those offsets at once, +X progress
// reads as heading diagonally into the upper area of the frame rather than
// straight up the screen - the "斜め奥へ進んでいるように見える" read the
// brief asked for, entirely from camera placement.
//
// Deliberately NOT a smoothed/dynamic follow like CameraFollow (no
// SmoothDamp, no look-around, no runtime zoom) - "カメラ位置・角度・基本
// ズームを基本的に固定" from the brief. positionOffset/lookAtOffset below
// are constant, tuned once; the only thing that ever changes at runtime is
// this rig's own world position, rigidly tracking the target 1:1 (no lag)
// so the camera's relationship to the player never drifts - it's meant to
// feel bolted directly to the player, not a soft-follow camera.
//
// NOTE (2026-09-08, disclosed per the brief's own request to report
// blockers rather than force a guess): the exact offset/look values below
// are a first-pass geometric estimate, not something verified against an
// actual rendered frame - this session had no way to capture a Unity Game
// View screenshot to iterate on visually (no screen-capture tool available
// for a native Editor window in this environment). Expect to need a real
// visual tuning pass in the Editor/on-device before these read exactly like
// the reference mockup.
public class PortraitCameraRig : MonoBehaviour
{
    public static PortraitCameraRig Instance { get; private set; }

    public Transform target;
    public Camera cam;

    [Header("Fixed rig geometry (world-space, relative to target)")]
    // Behind (-X) and above (+Y) the player, and off the Z=0 gameplay
    // plane (-Z) so there's room to view that plane from a distance at an
    // angle - see the class comment for why the -Z offset combined with
    // the lookAtOffset's own +Z=0 (aiming AT the gameplay plane, not away
    // from it) is what turns +X progress into a diagonal-into-the-screen
    // read instead of a flat sideways slide.
    public Vector3 positionOffset = new Vector3(-6f, 7f, -8f);
    // Where the camera aims, relative to target - biased ahead (+X) and
    // slightly up (+Y) of the player's own position so the frame naturally
    // opens up toward the oncoming course rather than centering dead-on
    // the player (who should sit in the lower third of frame, per the
    // brief).
    public Vector3 lookAtOffset = new Vector3(3f, 3f, 0f);

    public bool IsActive => cam != null && cam.enabled;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void LateUpdate()
    {
        if (target == null) return;

        transform.position = target.position + positionOffset;
        Vector3 lookDir = (target.position + lookAtOffset) - transform.position;
        if (lookDir.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
        }
    }

    // Called by ViewModeToggle when switching modes - enabling/disabling
    // the Camera component is what actually controls which one renders
    // (Unity only ever presents the output of enabled Camera components);
    // the AudioListener is toggled alongside it so exactly one of the two
    // cameras' listeners is ever active at a time (two simultaneous
    // AudioListeners logs a Unity warning and picks one arbitrarily).
    public void SetActive(bool active)
    {
        if (cam != null) cam.enabled = active;
        AudioListener listener = cam != null ? cam.GetComponent<AudioListener>() : null;
        if (listener != null) listener.enabled = active;
    }
}
