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

    public void ForceShown()
    {
        transform.localScale = Vector3.one;
        if (bgSr != null) bgSr.color = bgBaseColor;
        if (fillSr != null && Time.time >= flashUntil) fillSr.color = fillBaseColor;
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

    // ===== ボス戦の強化(2026-10-01): 崩しゲージ(HPバーの下の細い帯)と段階の境目の目盛り =====
    SpriteRenderer subBg, subFill;
    float subHeight;
    readonly System.Collections.Generic.List<SpriteRenderer> ticks = new System.Collections.Generic.List<SpriteRenderer>();
    float flashUntil;

    public void EnableSub(Sprite squareSprite, float height)
    {
        if (subBg != null) return;
        subHeight = height;
        float y = -(fill != null ? fill.localScale.y : 0.24f) * 0.5f - height * 0.5f - 0.06f;
        var bg = new GameObject("SubBg"); bg.transform.SetParent(transform, false);
        bg.transform.localPosition = new Vector3(0f, y, 0f);
        bg.transform.localScale = new Vector3(maxWidth + 0.06f, height + 0.05f, 1f);
        subBg = bg.AddComponent<SpriteRenderer>(); subBg.sprite = squareSprite; subBg.color = new Color(0.08f, 0.08f, 0.1f, 0.8f); subBg.sortingOrder = 30;
        var f = new GameObject("SubFill"); f.transform.SetParent(transform, false);
        f.transform.localPosition = new Vector3(-maxWidth * 0.5f, y, 0f);
        f.transform.localScale = new Vector3(0.0001f, height, 1f);
        subFill = f.AddComponent<SpriteRenderer>(); subFill.sprite = squareSprite; subFill.color = new Color(1f, 0.82f, 0.25f, 1f); subFill.sortingOrder = 31;
    }

    // 0..1。broken=trueの間は白く点滅(BREAK中)
    public void SetSub(float f, bool broken)
    {
        if (subFill == null) return;
        f = Mathf.Clamp01(f);
        float w = maxWidth * f;
        Vector3 p = subFill.transform.localPosition;
        subFill.transform.localScale = new Vector3(Mathf.Max(0.0001f, w), subHeight, 1f);
        subFill.transform.localPosition = new Vector3(-maxWidth * 0.5f + w * 0.5f, p.y, 0f);
        Color c = broken ? Color.Lerp(Color.white, new Color(1f, 0.6f, 0.2f), Mathf.PingPong(Time.time * 6f, 1f))
                         : Color.Lerp(new Color(1f, 0.82f, 0.25f), new Color(1f, 0.35f, 0.2f), f);
        c.a = subBg != null ? subBg.color.a / 0.8f : 1f;
        subFill.color = c;
    }

    // 段階の境目(残りHPの割合)に小さな目盛り
    public void SetPhaseTicks(Sprite squareSprite, float[] thresholds)
    {
        foreach (var t in ticks) if (t != null) Destroy(t.gameObject);
        ticks.Clear();
        if (thresholds == null) return;
        float hgt = fill != null ? fill.localScale.y : 0.24f;
        foreach (float th in thresholds)
        {
            var go = new GameObject("PhaseTick"); go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(-maxWidth * 0.5f + maxWidth * Mathf.Clamp01(th), 0f, 0f);
            go.transform.localScale = new Vector3(0.05f, hgt + 0.12f, 1f);
            var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = squareSprite; sr.color = new Color(1f, 0.95f, 0.75f, 0.9f); sr.sortingOrder = 32;
            ticks.Add(sr);
        }
    }

    // 段階が変わった瞬間の光(HPバー全体を一瞬白く)
    public void Flash(float seconds) { flashUntil = Time.time + seconds; }

    void Update()
    {
        if (fillSr == null) return;
        if (Time.time < flashUntil)
        {
            float k = Mathf.PingPong(Time.time * 10f, 1f);
            Color c = Color.Lerp(fillBaseColor, Color.white, k); c.a = fillSr.color.a;
            fillSr.color = c;
        }
        else if (flashUntil > 0f)
        {
            flashUntil = 0f;
            Color c = fillBaseColor; c.a = fillSr.color.a; fillSr.color = c;
        }
        if (subBg != null && bgSr != null) { Color c = subBg.color; c.a = 0.8f * (bgBaseColor.a > 0f ? bgSr.color.a / bgBaseColor.a : 1f); subBg.color = c; }
        foreach (var t in ticks) if (t != null && bgSr != null) { Color c = t.color; c.a = 0.9f * (bgBaseColor.a > 0f ? bgSr.color.a / bgBaseColor.a : 1f); t.color = c; }
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
