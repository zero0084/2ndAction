using UnityEngine;

// Cheap "poor-man's outline": a ring of black copies of the same sprite,
// offset slightly behind the main renderer. No custom shader needed, and it
// automatically follows whatever sprite/flip the main renderer is currently
// showing (works fine with frame-swap animation).
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteOutline : MonoBehaviour
{
    public float thickness = 0.025f;
    public Color color = Color.black;
    public int copies = 8;

    SpriteRenderer main;
    SpriteRenderer[] outlineRenderers;

    // 2026-11-01(背景を控えめにして敵を見やすく): 暗い場面(洞窟/夜/ラスダン)では、黒い縁が暗い背景に溶けて輪郭が分からないため、
    // 敵だけ縁を淡い色の1色塗り(Shaders/SpriteSolid)に替える。明るい場面は今までどおり黒。切り替えは BackgroundTone が決める。
    // Player(ReadabilityDirector の縁がある)と他の参加者には使わない。
    public static bool EnemyLightRim;
    public static Color EnemyLightColor = new Color(0.76f, 0.83f, 0.96f, 0.45f);
    static Material solidMat; static bool solidTried;
    int enemyState = -1; bool lightNow; // 敵かどうかは最初の LateUpdate で(付けた時点ではまだ敵の本体に付いていないことがある)
    Material defaultMat;

    void Awake()
    {
        main = GetComponent<SpriteRenderer>();
        outlineRenderers = new SpriteRenderer[copies];

        for (int i = 0; i < copies; i++)
        {
            float angle = (i / (float)copies) * Mathf.PI * 2f;
            GameObject go = new GameObject("Outline");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * thickness;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.color = color;
            sr.sortingOrder = main.sortingOrder - 1;

            outlineRenderers[i] = sr;
        }
        defaultMat = outlineRenderers.Length > 0 ? outlineRenderers[0].sharedMaterial : null;
    }

    void ApplyRimMode(bool light)
    {
        lightNow = light;
        if (light && !solidTried)
        {
            solidTried = true;
            var sh = Resources.Load<Shader>("Shaders/SpriteSolid");
            if (sh != null && sh.isSupported) solidMat = new Material(sh) { name = "Enemy Light Rim" };
        }
        bool use = light && solidMat != null;
        for (int i = 0; i < outlineRenderers.Length; i++)
        {
            var sr = outlineRenderers[i];
            sr.sharedMaterial = use ? solidMat : defaultMat;
            sr.color = use ? EnemyLightColor : color;
        }
    }

    void LateUpdate()
    {
        if (enemyState < 0) enemyState = GetComponentInParent<EnemyController>() != null ? 1 : 0;
        if (enemyState == 1 && EnemyLightRim != lightNow) ApplyRimMode(EnemyLightRim);
        Sprite s = main.sprite;
        bool visible = main.enabled;
        for (int i = 0; i < outlineRenderers.Length; i++)
        {
            SpriteRenderer sr = outlineRenderers[i];
            if (sr.sprite != s) sr.sprite = s;
            if (sr.flipX != main.flipX) sr.flipX = main.flipX;
            if (sr.flipY != main.flipY) sr.flipY = main.flipY;
            if (sr.enabled != visible) sr.enabled = visible;
        }
    }
}
