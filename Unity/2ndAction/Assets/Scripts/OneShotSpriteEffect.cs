using System.Collections;
using UnityEngine;

// Plays a short one-shot visual effect at a world position and then
// destroys itself: either a single static sprite that expands slightly
// while fading out, or a short frame sequence (extracted from a reference
// GIF) played once. Used for jump/land/double-jump dust puffs and the
// ascension smoke trail.
public class OneShotSpriteEffect : MonoBehaviour
{
    static Sprite softDotSprite;

    // A small soft-edged circular particle, generated at runtime (a radial
    // alpha falloff baked into a plain white sprite, tinted per-use via
    // SpriteRenderer.color) - Polish Pass 1's running-dust motes and hit
    // spark use this instead of needing new art assets, the same way
    // UiBackdrop generates its own 1x1 pixel rather than importing a file.
    public static Sprite SoftDotSprite()
    {
        if (softDotSprite != null) return softDotSprite;

        const int size = 24;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float maxDist = size * 0.5f;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float a = Mathf.Clamp01(1f - dist / maxDist);
                a *= a; // softer falloff than linear
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();

        softDotSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        return softDotSprite;
    }

    public static OneShotSpriteEffect CreateStatic(Sprite sprite, Vector3 position, float duration = 0.4f, float scale = 1f, int sortingOrder = RenderOrder.CombatFx)
    {
        return CreateStaticTinted(sprite, position, Color.white, duration, scale, sortingOrder);
    }

    // Same as CreateStatic but with an explicit start color (alpha included)
    // instead of always starting from opaque white - used for tinted dust
    // (brownish) and hit sparks (bright white at partial alpha) sharing the
    // one procedural dot sprite above.
    public static OneShotSpriteEffect CreateStaticTinted(Sprite sprite, Vector3 position, Color color, float duration = 0.4f, float scale = 1f, int sortingOrder = RenderOrder.CombatFx)
    {
        if (sprite == null) return null;

        GameObject go = new GameObject("DustFxStatic");
        go.transform.position = position;
        go.transform.localScale = Vector3.one * scale;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = sortingOrder;
        sr.color = color;

        var fx = go.AddComponent<OneShotSpriteEffect>();
        fx.StartCoroutine(fx.FadeOutRoutine(sr, duration, scale, color.a));
        return fx;
    }

    public static OneShotSpriteEffect CreateAnimated(Sprite[] frames, Vector3 position, float fps = 14f, float scale = 1f, int sortingOrder = RenderOrder.CombatFx)
    {
        if (frames == null || frames.Length == 0) return null;

        GameObject go = new GameObject("DustFxAnim");
        go.transform.position = position;
        go.transform.localScale = Vector3.one * scale;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = sortingOrder;

        var fx = go.AddComponent<OneShotSpriteEffect>();
        fx.StartCoroutine(fx.PlayFramesRoutine(sr, frames, fps));
        return fx;
    }

    IEnumerator FadeOutRoutine(SpriteRenderer sr, float duration, float startScale, float startAlpha = 1f)
    {
        float t = 0f;
        Color c = sr.color;
        while (t < duration)
        {
            t += Time.deltaTime;
            float frac = Mathf.Clamp01(t / duration);
            c.a = startAlpha * (1f - frac);
            sr.color = c;
            transform.localScale = Vector3.one * startScale * (1f + frac * 0.35f);
            yield return null;
        }
        Destroy(gameObject);
    }

    // Running-dust variant: drifts a bit (opposite the player's travel
    // direction, simulating being kicked up and left behind) while fading,
    // instead of just expanding in place like the jump/land puffs above.
    public static OneShotSpriteEffect CreateDrifting(Sprite sprite, Vector3 position, Vector3 driftVelocity, Color color, float duration = 0.3f, float scale = 1f, int sortingOrder = RenderOrder.EnvironmentFx)
    {
        if (sprite == null) return null;

        GameObject go = new GameObject("DustFxDrift");
        go.transform.position = position;
        go.transform.localScale = Vector3.one * scale;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = sortingOrder;
        sr.color = color;

        var fx = go.AddComponent<OneShotSpriteEffect>();
        fx.StartCoroutine(fx.DriftFadeRoutine(sr, duration, color.a, driftVelocity));
        return fx;
    }

    IEnumerator DriftFadeRoutine(SpriteRenderer sr, float duration, float startAlpha, Vector3 driftVelocity)
    {
        float t = 0f;
        Color c = sr.color;
        while (t < duration)
        {
            t += Time.deltaTime;
            float frac = Mathf.Clamp01(t / duration);
            c.a = startAlpha * (1f - frac);
            sr.color = c;
            transform.position += driftVelocity * Time.deltaTime;
            yield return null;
        }
        Destroy(gameObject);
    }

    IEnumerator PlayFramesRoutine(SpriteRenderer sr, Sprite[] frames, float fps)
    {
        foreach (Sprite f in frames)
        {
            sr.sprite = f;
            yield return new WaitForSeconds(1f / fps);
        }
        Destroy(gameObject);
    }

    // Game Feel refinement pass - the general-purpose "make this read as a
    // natural momentary reaction, not a sprite being displayed" primitive:
    // scale, alpha, drift, and rotation are ALL tweened together over a
    // short lifetime, eased out (fast at first, settling toward the end)
    // rather than linear. Every Jump/DoubleJump/Landing/Hit/Death effect in
    // this pass goes through this one method instead of each hand-rolling
    // its own partial tween (CreateStatic/CreateDrifting above only tween
    // some of these, which is what read as "just displaying the sprite").
    // holdFraction (0-1) - Game Feel Visibility Pass: the fraction of
    // duration spent BEFORE alpha starts easing toward endAlpha at all
    // (scale still animates the whole time). Effects that faded from the
    // very first frame read as "flickered", not "appeared then went away" -
    // "発生直後から透明にならず、しっかり見える -> Fade" per the brief.
    // Defaults to 0 (old behavior: fades from frame one) so callers that
    // don't care are unaffected.
    public static OneShotSpriteEffect CreateTweened(Sprite sprite, Vector3 position, Color color, float duration = 0.2f, float startScale = 0.6f, float endScale = 1f, float startAlpha = -1f, float endAlpha = 0f, Vector3 drift = default, float rotationDegrees = 0f, int sortingOrder = RenderOrder.CombatFx, float holdFraction = 0f)
    {
        if (sprite == null) return null;
        if (startAlpha < 0f) startAlpha = color.a; // -1 sentinel: "use color's own alpha"

        // Game Feel Visibility Pass - see GameFeelDebug's class comment.
        // Applied here (the one place every tweened effect funnels through)
        // rather than in each caller, so the toggle affects everything at
        // once without touching per-effect tuning.
        if (GameFeelDebug.VisibilityBoost)
        {
            startScale *= 2f;
            endScale *= 2f;
            duration *= 2f;
            startAlpha = 1f;
        }

        GameObject go = new GameObject("FxTweened");
        go.transform.position = position;
        go.transform.localScale = Vector3.one * startScale;
        if (rotationDegrees != 0f) go.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = sortingOrder;
        Color c = color; c.a = startAlpha;
        sr.color = c;

        var fx = go.AddComponent<OneShotSpriteEffect>();
        fx.StartCoroutine(fx.TweenRoutine(sr, duration, startScale, endScale, startAlpha, endAlpha, drift, rotationDegrees, holdFraction));
        return fx;
    }

    IEnumerator TweenRoutine(SpriteRenderer sr, float duration, float startScale, float endScale, float startAlpha, float endAlpha, Vector3 drift, float rotationDegrees, float holdFraction)
    {
        float t = 0f;
        Color c = sr.color;
        while (t < duration)
        {
            t += Time.deltaTime;
            float frac = Mathf.Clamp01(t / Mathf.Max(0.001f, duration));
            float eased = 1f - (1f - frac) * (1f - frac); // ease-out
            transform.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, eased);
            // Alpha stays at startAlpha through the hold window, then eases
            // toward endAlpha over the remainder - "しっかり見える -> Fade",
            // not a fade starting the instant the effect spawns.
            float alphaFrac = holdFraction > 0f ? Mathf.Clamp01((frac - holdFraction) / Mathf.Max(0.001f, 1f - holdFraction)) : frac;
            c.a = Mathf.Lerp(startAlpha, endAlpha, alphaFrac);
            sr.color = c;
            if (drift != Vector3.zero) transform.position += drift * Time.deltaTime;
            if (rotationDegrees != 0f) transform.Rotate(0f, 0f, rotationDegrees * Time.deltaTime);
            yield return null;
        }
        Destroy(gameObject);
    }

    // A handful of small independent particles (2-6) scattering outward
    // (biased upward, reads as "kicked up" rather than floating evenly in
    // all directions) and fading - Run Dust/Landing/Enemy Death all use
    // this instead of one single bigger sprite, per "1枚の大きなEffectとし
    // て見せず、小さなParticleを複数使う" (see the class comment).
    // horizontalBias widens the spread sideways relative to upward (1 =
    // even, >1 = wider/flatter spread - Landing's left-right fan uses this).
    // Game Feel audit fix - defaulted to EnvironmentFx (behind Player/Enemy)
    // instead of CreateTweened's own CombatFx default just above; harmless
    // today since PlayerDustEffects.OnLanded (the only caller) already
    // passes its own explicit override, but a future caller relying on the
    // default would hit the exact "invisible behind the character" bug
    // this pass just fixed elsewhere (see PlayerDustEffects.OnDoubleJumped).
    public static void CreateScatterBurst(Sprite sprite, Vector3 position, Color color, int count, float duration, float scaleMin, float scaleMax, float spreadSpeed, float horizontalBias = 1f, int sortingOrder = RenderOrder.CombatFx, float holdFraction = 0f)
    {
        if (sprite == null) return;
        for (int i = 0; i < count; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector3 dir = new Vector3(Mathf.Cos(angle) * horizontalBias, Mathf.Abs(Mathf.Sin(angle)) * 0.5f + 0.15f, 0f).normalized;
            Vector3 drift = dir * Random.Range(spreadSpeed * 0.6f, spreadSpeed);
            float scale = Random.Range(scaleMin, scaleMax);
            Vector3 offset = new Vector3(Random.Range(-0.06f, 0.06f), Random.Range(0f, 0.05f), 0f);
            CreateTweened(sprite, position + offset, color, duration, scale * 0.6f, scale, color.a, 0f, drift, Random.Range(-90f, 90f), sortingOrder, holdFraction);
        }
    }
}
