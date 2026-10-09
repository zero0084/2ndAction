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
[DefaultExecutionOrder(-50)]
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
    public Vector3 positionOffset = new Vector3(-10f, 9f, -6f); // 2026-10-08: 計算で選んだ構図(プレイヤー 画面の下寄り・大きく、27m 先まで右上へ続く)
    // Where the camera aims, relative to target - biased ahead (+X) and
    // slightly up (+Y) of the player's own position so the frame naturally
    // opens up toward the oncoming course rather than centering dead-on
    // the player (who should sit in the lower third of frame, per the
    // brief).
    public Vector3 lookAtOffset = new Vector3(10f, 0f, 0f);

    public bool IsActive => cam != null && cam.enabled;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // 2026-10-08(依頼E-6): 縦画面の「斜め上から見る」。プレイヤーを画面の下寄りに、道が画面の上の奥へ続くように前方を見る。
    // 縦は「走っている地表の高さ」をゆっくり追う(ジャンプのたびに上下しない)。横は 1:1(進む向きの揺れを出さない)。
    // 見せる範囲を変えても、敵の出現/行動/攻撃の始まる距離は変わらない(判定は GameView = 横画面と同じ幅)。
    public float fov = 50f;
    public Vector2 lensShift = new Vector2(-0.15f, 0f); // 画像を左上へずらす = プレイヤーを中央寄り/下寄りに(見る向きは変えない)
    public float surfaceSmooth = 0.35f;
    float surfaceY, surfaceVel; bool surfaceInit;

    void Start()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 開発用の調整: -obqOff x,y,z -obqLook x,y,z -obqFov f
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
        {
            if (a[i] == "-obqOff") positionOffset = V(a[i + 1], positionOffset);
            if (a[i] == "-obqLook") lookAtOffset = V(a[i + 1], lookAtOffset);
            if (a[i] == "-obqShift") { var v = V(a[i + 1] + ",0", Vector3.zero); lensShift = new Vector2(v.x, v.y); }
            if (a[i] == "-obqFov" && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f)) fov = f;
        }
#endif
    }
    static Vector3 V(string s, Vector3 d)
    {
        var p = s.Split(',');
        if (p.Length != 3) return d;
        float.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x);
        float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y);
        float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z);
        return new Vector3(x, y, z);
    }

    void LateUpdate()
    {
        if (target == null) return;
        if (cam != null)
        {
            if (Mathf.Abs(cam.fieldOfView - fov) > 0.01f) cam.fieldOfView = fov;
            // 見る向きは変えずに画像だけずらす(中心をずらした透視)。プレイヤーを中央寄り/下寄りに置き、前方を広く見せる
            cam.ResetProjectionMatrix();
            var m = cam.projectionMatrix;
            m[0, 2] = lensShift.x * 2f; m[1, 2] = lensShift.y * 2f;
            cam.projectionMatrix = m;
        }
        if (Time.timeScale <= 0f && surfaceInit) return;
        var pc = PlayerController.Instance;
        float py = target.position.y;
        if (!surfaceInit) { surfaceY = py; surfaceInit = true; }
        float want = surfaceY;
        if (pc == null || pc.IsGrounded) want = py;
        else if (py < surfaceY - 1.5f) want = py;
        surfaceY = Mathf.SmoothDamp(surfaceY, want, ref surfaceVel, surfaceSmooth);
        Vector3 basePos = new Vector3(target.position.x, surfaceY, target.position.z);

        transform.position = basePos + positionOffset;
        ZigRoad.Apply(IsActive, target.position.x); // ジグザグの道(表示だけ、2026-10-09)
        Vector3 lookDir = (basePos + lookAtOffset) - transform.position;
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
        if (!active) ZigRoad.Apply(false, 0f);
        AudioListener listener = cam != null ? cam.GetComponent<AudioListener>() : null;
        if (listener != null) listener.enabled = active;
    }
}
