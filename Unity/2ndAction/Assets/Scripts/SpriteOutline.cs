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
    }

    void LateUpdate()
    {
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
