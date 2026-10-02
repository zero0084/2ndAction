using UnityEngine;

// Keeps a background sprite centered on the camera and uniformly scaled to
// always cover the full view (like CSS background-size:cover), regardless of
// orientation or the dynamic zoom CameraFollow applies.
[DefaultExecutionOrder(100)] // CameraFollowのLateUpdateより後(1フレーム遅れのブレ防止)
[RequireComponent(typeof(SpriteRenderer))]
public class BackgroundFollower : MonoBehaviour
{
    public Camera cam;
    // Game Feel pass, section 16 - kept at its old default (1 = locked
    // exactly to the camera) for the actual in-game background: this
    // sprite is scaled to just barely cover the screen (see below), so any
    // sustained factor under 1 would eventually drift its edge into view
    // over a long enough run (this game's distance is effectively
    // unbounded) unless the background also tiled/wrapped, which it
    // doesn't yet. Left here, tunable, for whenever that's added -
    // ForegroundCloudLayer is this pass's actual "reduce the flat one-
    // image feel" fix, since its clouds wrap by construction instead.
    public float parallaxFactor = 1f;

    SpriteRenderer sr;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    void LateUpdate()
    {
        if (cam == null || sr.sprite == null) return;

        transform.position = new Vector3(cam.transform.position.x * parallaxFactor, cam.transform.position.y, 0f);

        float worldHeight = cam.orthographicSize * 2f;
        float worldWidth = worldHeight * cam.aspect;

        Vector2 spriteSize = sr.sprite.bounds.size;
        float scale = Mathf.Max(worldWidth / spriteSize.x, worldHeight / spriteSize.y);
        transform.localScale = new Vector3(scale, scale, 1f);
    }
}
