using UnityEngine;

// Plays the sword-swing slash VFX (frames extracted from a reference GIF) as
// a short one-shot animation during an attack. Scales up across the combo
// chain (small -> medium -> large) so the swing's visible size communicates
// how many hits deep the player is, matching the hitbox's own growth.
[RequireComponent(typeof(SpriteRenderer))]
public class AttackSlashVisual : MonoBehaviour
{
    public Sprite[] frames;
    public float fps = 22f;

    [Header("Stage 1 (first hit in a chain) - smallest swing")]
    public float baseScale = 0.75f;
    // Growth per additional combo hit.
    public float scaleStep = 0.3f;

    // Bugfix 2026-09-06 - "Attack Range Upが見た目で分からない". The Range
    // cards' AttackRangeMultiplier already drove the (invisible) attack
    // hitbox's own scale/reach - see PlayerController.ApplyComboStageToHitbox
    // - but was never passed to this VFX at all, so the swing looked
    // identical regardless of how many Range cards were stacked. Deliberately
    // reuses that SAME multiplier (rather than a separately-tuned visual
    // curve) so Hitbox Range and Visual Range can never drift apart - the
    // brief's own explicit warning. influenceX/Y let the visual "punch" be
    // tuned independently of the raw multiplier: 1.0 = exactly 1:1 with
    // AttackRangeMultiplier, 0 = ignore it entirely. X gets the full
    // influence (reads as "reaches further"), Y a smaller fraction (a
    // little more arc height too, without making the swing look like a
    // bigger blob instead of a longer one).
    [Header("Attack Range Up - visual reach (Inspector-tunable)")]
    public float rangeInfluenceX = 1f;
    public float rangeInfluenceY = 0.4f;

    SpriteRenderer sr;
    int frameIndex;
    float frameTimer;
    bool playing;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        sr.sortingOrder = RenderOrder.SlashFx;
        sr.enabled = false;
    }

    // stage 1 = first hit (smallest arc) up through the combo cap (largest,
    // the "finisher" swing). rangeMultiplier is PlayerController.
    // AttackRangeMultiplier verbatim (1.0 = no Range cards stacked).
    public void SetComboStage(int stage, float rangeMultiplier = 1f)
    {
        if (frames == null || frames.Length == 0) return;

        int step = Mathf.Max(0, stage - 1);
        float scale = baseScale + scaleStep * step;
        float rangeDelta = Mathf.Max(0f, rangeMultiplier - 1f);
        float scaleX = scale * (1f + rangeDelta * rangeInfluenceX);
        float scaleY = scale * (1f + rangeDelta * rangeInfluenceY);
        transform.localScale = new Vector3(scaleX, scaleY, 1f);

        frameIndex = 0;
        frameTimer = 0f;
        playing = true;
        sr.enabled = true;
        sr.sprite = frames[0];
    }

    void Update()
    {
        if (!playing) return;

        frameTimer += Time.deltaTime;
        if (frameTimer >= 1f / fps)
        {
            frameTimer = 0f;
            frameIndex++;
            if (frameIndex >= frames.Length)
            {
                playing = false;
                sr.enabled = false;
                return;
            }
            sr.sprite = frames[frameIndex];
        }
    }
}
