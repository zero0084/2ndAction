using UnityEngine;

// Game Feel pass, section 16 - a thin decorative cloud layer drifting past
// independently of (and slightly slower than) the camera's own pan speed,
// so it reads as a closer foreground layer than the fixed backdrop behind
// it - the actual "reduce the flat one-image feel" fix for this pass (see
// BackgroundFollower's own comment on why the main background itself stays
// locked to the camera for now). Wraps within a fixed span around the
// camera rather than tracking absolute world position, so unlike a true
// parallax offset this never drifts out of range no matter how far an
// endless run goes. Purely cosmetic - no collider, no gameplay interaction
// - and kept toward the top of the screen ("画面端・上下を中心に配置") so
// it never sits over the player or enemies, which stay lower in frame.
public class ForegroundCloudLayer : MonoBehaviour
{
    public Camera cam;
    public Sprite cloudSprite;
    public int cloudCount = 3;
    // Deliberately slow relative to the player's own run speed (5+) - this
    // is what makes the layer read as "drifting past", not "racing by".
    public float driftSpeed = 1.4f;
    // Small - "小さく短く控えめに" per the brief. cloudSprite itself
    // (TopCloud.png) is already a fairly large/wide source image, so even
    // these modest multipliers read as a real cloud, not a speck.
    public float scaleMin = 0.35f;
    public float scaleMax = 0.65f;
    public float alpha = 0.45f;
    public float verticalJitter = 1.5f;

    Transform[] clouds;
    float[] speeds;
    float spanWidth;

    void Start()
    {
        if (cam == null || cloudSprite == null) { enabled = false; return; }
        // Generous margin (2.6x the camera's own width) so a cloud wrapping
        // back around reappears well outside the visible frame, never
        // popping into view mid-screen.
        spanWidth = cam.orthographicSize * cam.aspect * 2.6f;

        clouds = new Transform[cloudCount];
        speeds = new float[cloudCount];
        for (int i = 0; i < cloudCount; i++)
        {
            GameObject go = new GameObject("ForegroundCloud" + i);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = cloudSprite;
            sr.sortingOrder = RenderOrder.EnvironmentFx;
            sr.color = new Color(1f, 1f, 1f, alpha);
            float scale = Random.Range(scaleMin, scaleMax);
            go.transform.localScale = new Vector3(scale, scale, 1f);
            clouds[i] = go.transform;
            speeds[i] = driftSpeed * Random.Range(0.7f, 1.3f);
            PlaceAt(i, Random.Range(-spanWidth * 0.5f, spanWidth * 0.5f));
        }
    }

    void PlaceAt(int i, float offsetX)
    {
        float camX = cam.transform.position.x;
        // Upper portion of the frame only (55%-85% of the way up from
        // center to the top edge) - stays clear of the player/enemies
        // below, and clear of the very top edge too.
        float y = cam.transform.position.y + cam.orthographicSize * Random.Range(0.55f, 0.85f) + Random.Range(-verticalJitter, verticalJitter);
        clouds[i].position = new Vector3(camX + offsetX, y, 0f);
    }

    void Update()
    {
        if (cam == null || clouds == null) return;
        float camX = cam.transform.position.x;
        for (int i = 0; i < clouds.Length; i++)
        {
            if (clouds[i] == null) continue;
            clouds[i].position += Vector3.right * speeds[i] * Time.deltaTime;

            float relative = clouds[i].position.x - camX;
            if (relative > spanWidth * 0.5f)
            {
                PlaceAt(i, -spanWidth * 0.5f);
            }
        }
    }
}
