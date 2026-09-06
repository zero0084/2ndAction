using System.Collections;
using UnityEngine;

// World-space HP bar that tracks a target transform every frame (rather than
// being parented to it), so it stays upright and correctly positioned even
// while the dragon itself is animating/moving during an attack.
public class DragonHealthBar : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0f, 2.2f, 0f);
    public Transform fill;
    public float maxWidth = 2.6f;

    SpriteRenderer bgSr;
    SpriteRenderer fillSr;
    Color bgBaseColor;
    Color fillBaseColor;

    void LateUpdate()
    {
        if (target != null)
        {
            transform.position = target.position + offset;
        }
    }

    public void SetFraction(float f)
    {
        f = Mathf.Clamp01(f);
        if (fill == null) return;

        float w = maxWidth * f;
        Vector3 s = fill.localScale;
        s.x = Mathf.Max(0.0001f, w);
        fill.localScale = s;
        fill.localPosition = new Vector3(-maxWidth / 2f + w / 2f, 0f, 0f);
    }

    // Boss Milestone Presentation pass - starts fully invisible (Alpha 0,
    // ScaleY 0) so Create() can still run immediately at spawn (HP tracking
    // needs to exist from the first frame) while the bar itself only
    // actually becomes visible once the boss controller calls
    // RevealRoutine after its own entrance animation finishes -
    // "Dragon登場 -> 所定位置到達 -> Boss HP Bar表示". Only this bar's own
    // alpha/scale are touched - SetFraction (the actual HP width) is
    // untouched and keeps working normally underneath.
    public void SetHidden()
    {
        transform.localScale = new Vector3(1f, 0f, 1f);
        Color bg = bgBaseColor; bg.a = 0f; bgSr.color = bg;
        Color fc = fillBaseColor; fc.a = 0f; fillSr.color = fc;
    }

    // Boss Defeat Presentation pass - "HPが0まで減ったことを一瞬見せてから
    // Alpha 1->0" (see DragonController/MajinController's death coroutine,
    // which calls this only after a short hold at the now-empty bar).
    public IEnumerator FadeOutRoutine(float duration)
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, duration);
            float f = Mathf.Clamp01(t);
            Color bg = bgBaseColor; bg.a = Mathf.Lerp(bgBaseColor.a, 0f, f); bgSr.color = bg;
            Color fc = fillBaseColor; fc.a = Mathf.Lerp(fillBaseColor.a, 0f, f); fillSr.color = fc;
            yield return null;
        }
    }

    public IEnumerator RevealRoutine(float duration)
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, duration);
            float f = Mathf.Clamp01(t);
            transform.localScale = new Vector3(1f, f, 1f);
            Color bg = bgBaseColor; bg.a = Mathf.Lerp(0f, bgBaseColor.a, f); bgSr.color = bg;
            Color fc = fillBaseColor; fc.a = Mathf.Lerp(0f, fillBaseColor.a, f); fillSr.color = fc;
            yield return null;
        }
        transform.localScale = Vector3.one;
        bgSr.color = bgBaseColor;
        fillSr.color = fillBaseColor;
    }

    public static DragonHealthBar Create(Sprite squareSprite, Transform target, float maxWidth, float barHeight)
    {
        GameObject go = new GameObject("DragonHpBar");
        DragonHealthBar bar = go.AddComponent<DragonHealthBar>();
        bar.target = target;
        bar.maxWidth = maxWidth;

        GameObject bg = new GameObject("Background");
        bg.transform.SetParent(go.transform);
        bg.transform.localPosition = Vector3.zero;
        bg.transform.localScale = new Vector3(maxWidth + 0.08f, barHeight + 0.08f, 1f);
        SpriteRenderer bgSr = bg.AddComponent<SpriteRenderer>();
        bgSr.sprite = squareSprite;
        bgSr.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);
        bgSr.sortingOrder = 30;

        GameObject fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(go.transform);
        fillGO.transform.localScale = new Vector3(maxWidth, barHeight, 1f);
        SpriteRenderer fillSr = fillGO.AddComponent<SpriteRenderer>();
        fillSr.sprite = squareSprite;
        fillSr.color = new Color(0.85f, 0.15f, 0.15f, 1f);
        fillSr.sortingOrder = 31;

        bar.fill = fillGO.transform;
        bar.SetFraction(1f);

        bar.bgSr = bgSr;
        bar.fillSr = fillSr;
        bar.bgBaseColor = bgSr.color;
        bar.fillBaseColor = fillSr.color;

        return bar;
    }
}
