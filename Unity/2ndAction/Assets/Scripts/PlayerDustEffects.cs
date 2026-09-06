using UnityEngine;

// Spawns short dust-puff VFX (from reference art) at the player for
// jump-start / double-jump / landing, and a rising smoke trail during the
// win-ascension flight - purely cosmetic, driven by PlayerController's
// existing jump/land/ascend events and state.
//
// Deliberately NOT [RequireComponent(typeof(PlayerController))]: SceneBuilder
// adds this component BEFORE it explicitly adds PlayerController, so that
// attribute would auto-create a PlayerController right here, and the later
// explicit AddComponent<PlayerController>() call would then add a SECOND,
// independent one on the same GameObject - both running Move() every
// frame and silently doubling the player's effective movement speed. (This
// happened - it's why this comment exists.)
public class PlayerDustEffects : MonoBehaviour
{
    // Game Feel refinement pass - Assets/Art/Effects/*.png (see
    // SceneBuilder), individual per-effect assets from the
    // OneMoreMile_GameFeel pack. jumpStartDustFrames/doubleJumpDustSprite
    // are kept as fallbacks for a scene built before this pack existed, so
    // nothing goes silently blank if these single-sprite fields aren't
    // assigned for some reason.
    public Sprite jumpPuffSprite;
    public Sprite[] jumpStartDustFrames; // legacy fallback
    public Sprite doubleJumpDustSprite; // legacy fallback
    public Sprite landingPuffSprite;
    public Sprite[] ascensionSmokeFrames;
    // A distinct blue-tinted ring (DoubleJumpRing.png) instead of just
    // reusing the same dust-colored splash as a normal jump ("通常ジャンプ
    // と二段ジャンプが視覚的に判別できれば十分" - see OnDoubleJumped).
    // Visibility Pass: brightened (was 0.55/0.85/1/0.9) - "青/水色の輝度も
    // 少し上げてください".
    public Sprite doubleJumpRingSprite;
    public Color doubleJumpRingColor = new Color(0.65f, 0.92f, 1f, 1f);
    // Run Dust.png / Grass Dust.png - alternated randomly per puff (see
    // UpdateRunDust) so a run doesn't look like the same sprite repeating.
    public Sprite runDustSprite;
    public Sprite grassDustSprite;

    // Toned down from 1f - Game Feel pass ("小さく短く控えめに" - the jump/
    // land dust read a bit large/showy at full scale for how understated
    // the rest of this game's feedback is meant to be).
    //
    // Game Feel Visibility Pass (2026-09-02): "控えめに" turned out to be
    // TOO subtle in real play - every effect below was firing correctly but
    // reading as "barely a flicker" rather than a noticeable reaction, per
    // direct on-device feedback. Jump/Landing/Run Dust below now compute
    // their scale/duration/hold off explicit constants tuned for clear
    // visibility rather than off this field, so this only still affects the
    // ascension smoke trail and the legacy double-jump fallback size.
    public float dustScale = 0.75f;
    public float ascensionPuffInterval = 0.18f;
    // One shared nudge for every dust/shadow spawn point below (all of
    // which are anchored off FeetPosition/transform.position, i.e. Root -
    // never Visual) - a single knob to correct against if a future
    // character asset's foot pivot doesn't land exactly where this
    // expects, without needing to retune each effect separately.
    public Vector3 effectOffset = Vector3.zero;

    // Polish Pass 1 - "the character actually feels like it's running on
    // this terrain" (item 1): a light, continuous kick-up trail while
    // grounded and moving, plus a very faint contact shadow that also
    // reads as a landing-spot cue while airborne. Both are deliberately
    // subtle/toggleable per the "don't make it flashy, assist the sense of
    // speed rather than draw attention to itself" brief.
    [Header("Polish Pass 1 - Running Dust (tunable)")]
    public bool runDustEnabled = true;
    public float runDustInterval = 0.11f;
    // Visibility Pass: scale/duration raised ~1.5x (was 0.22/0.28), and a
    // hold window added so each mote reads as "there" for a beat before
    // fading instead of dissolving from the first frame - "1個1個がちゃんと
    // 見える" was prioritized over quantity, so the spawn count (below,
    // UpdateRunDust) was NOT increased.
    public float runDustScale = 0.34f;
    public float runDustDuration = 0.38f;
    [Range(0f, 1f)] public float runDustHoldFraction = 0.4f;
    public Color runDustColor = new Color(0.55f, 0.45f, 0.32f, 0.7f);
    public float runDustMinSpeed = 0.5f;
    // Alpha of the tint applied over the actual Run/Grass Dust sprites (as
    // opposed to runDustColor above, which only applies to the procedural
    // fallback dot) - was 0.55, read as near-invisible against most ground
    // tiles.
    public float runDustSpriteAlpha = 0.9f;

    [Header("Game Feel Visibility Pass - Jump/Landing/DoubleJump (tunable)")]
    // Jump Puff (OnJumpStarted): was start 0.41/end 0.64, duration 0.2,
    // alpha 0.7 - scaled ~1.75x with a hold window added.
    public float jumpPuffStartScale = 0.72f;
    public float jumpPuffEndScale = 1.12f;
    public float jumpPuffDuration = 0.3f;
    public float jumpPuffStartAlpha = 0.9f;
    [Range(0f, 1f)] public float jumpPuffHoldFraction = 0.35f;

    // Double Jump Ring (OnDoubleJumped) - sized directly in world units
    // against the player's own collider width (1 unit, see
    // GroundFactory/CreatePlayer's BoxCollider2D) per the brief: "生成時：
    // Player横幅の1.0〜1.3倍、最大：2〜2.5倍". DoubleJumpRing.png is
    // imported at PPU == its own source pixel width (see SceneBuilder), so
    // a localScale of 1.0 already equals ~1 world unit - no extra
    // conversion needed.
    public float doubleJumpRingStartScale = 1.15f;
    public float doubleJumpRingEndScale = 2.25f;
    public float doubleJumpRingDuration = 0.35f;
    [Range(0f, 1f)] public float doubleJumpRingHoldFraction = 0.25f;

    // Landing Puff (OnLanded): was scaleMin 0.26/scaleMax 0.45, duration
    // 0.22, spreadSpeed 1.6, horizontalBias 2.2 - scaled ~1.75x, sideways
    // spread widened further per "左右方向へのScale変化を少し大きく".
    public float landingPuffScaleMin = 0.46f;
    public float landingPuffScaleMax = 0.79f;
    public float landingPuffDuration = 0.35f;
    public float landingPuffSpreadSpeed = 2.0f;
    public float landingPuffHorizontalBias = 2.6f;
    [Range(0f, 1f)] public float landingPuffHoldFraction = 0.3f;

    [Header("Polish Pass 1 - Contact Shadow (tunable)")]
    public bool contactShadowEnabled = true;
    // Assets/Art/Effects/GroundShadow.png (Game Feel refinement pass) -
    // falls back to the procedural soft dot if not assigned, so this never
    // breaks an older scene build.
    public Sprite groundShadowSprite;
    public float contactShadowWidth = 0.85f;
    public float contactShadowHeight = 0.18f;
    // Visibility Pass - Ground Shadow is ground-contact assistance, not a
    // showpiece effect, so its SIZE is deliberately left alone; alpha alone
    // bumped slightly (was 0.28) since it was reported as hard to notice.
    public Color contactShadowColor = new Color(0f, 0f, 0f, 0.4f);
    // Game Feel pass - the shadow shrinks/fades the higher above the
    // surface the player is (reaching contactShadowMinScale/Alpha at
    // contactShadowFadeHeight and above), then eases back to full size the
    // instant it lands - "ジャンプ：影は地面側に残す、高度が上がる：少し小
    // さく/薄くする、着地：元のサイズへ" per the brief. Purely a function of
    // the CURRENT height above the surface each frame, not a remembered
    // jump peak, so it reads correctly for a fall off a ledge too, not just
    // an actual jump.
    public float contactShadowFadeHeight = 3.5f;
    public float contactShadowMinScale = 0.55f;
    public float contactShadowMinAlpha = 0.35f;

    PlayerController controller;
    float ascensionPuffTimer;
    float runDustTimer;

    SpriteRenderer shadowRenderer;
    Transform shadowTransform;

    void Awake()
    {
        controller = GetComponent<PlayerController>();
        SetupShadow();
    }

    void SetupShadow()
    {
        if (!contactShadowEnabled) return;

        GameObject shadowGO = new GameObject("ContactShadow");
        shadowTransform = shadowGO.transform;
        shadowRenderer = shadowGO.AddComponent<SpriteRenderer>();
        shadowRenderer.sprite = groundShadowSprite != null ? groundShadowSprite : OneShotSpriteEffect.SoftDotSprite();
        shadowRenderer.color = contactShadowColor;
        // Behind the player sprite (sortingOrder 2 - see SceneBuilder) but
        // above the ground tiles (sortingOrder 0/unset), and NOT parented
        // to the player - it tracks the actual ground height/angle every
        // frame instead, so it stays flat on the terrain (and visible as a
        // landing-spot cue) even while the player is airborne.
        shadowRenderer.sortingOrder = RenderOrder.EnvironmentFx;
        shadowTransform.localScale = new Vector3(contactShadowWidth, contactShadowHeight, 1f);
    }

    void OnEnable()
    {
        controller.JumpStarted += OnJumpStarted;
        controller.DoubleJumped += OnDoubleJumped;
        controller.Landed += OnLanded;
    }

    void OnDisable()
    {
        controller.JumpStarted -= OnJumpStarted;
        controller.DoubleJumped -= OnDoubleJumped;
        controller.Landed -= OnLanded;
    }

    // transform.position.y IS the foot line now (groundOffset=0, foot
    // pivot - see PlayerController) - this used to subtract another 0.4
    // units on top of that, a leftover from the old center-pivot
    // convention (back when transform.y sat mid-body) that never got
    // updated when groundOffset was fixed, silently spawning jump/land
    // dust visibly underground since. No offset needed any more.
    Vector3 FeetPosition() => transform.position + effectOffset;

    // Game Feel refinement pass - CreateTweened (scale+alpha+lifetime all
    // animated together) instead of CreateAnimated's plain frame-cycling
    // (which had NO scale/alpha/position animation of its own - just
    // swapped sprites in place, which is exactly what read as "displaying
    // the effect image" rather than a natural reaction). One representative
    // frame from the source sequence, not the whole flipbook - the tween
    // itself carries the motion now. Spawned once at the jump position and
    // never touched again, so it can never "follow" the player.
    void OnJumpStarted()
    {
        Sprite sprite = jumpPuffSprite != null ? jumpPuffSprite
            : (jumpStartDustFrames != null && jumpStartDustFrames.Length > 0) ? jumpStartDustFrames[0] : null;
        // Visibility Pass - "地面を蹴った、と一目で分かる大きさ" (see
        // jumpPuffStartScale/etc above). Spawned once at the takeoff spot
        // and never re-parented/moved, so it naturally stays behind as the
        // player rises - "Playerが上へ移動したあとも、Puffが少し残る".
        OneShotSpriteEffect.CreateTweened(sprite, FeetPosition(), Color.white, duration: jumpPuffDuration, startScale: jumpPuffStartScale, endScale: jumpPuffEndScale, startAlpha: jumpPuffStartAlpha, endAlpha: 0f, sortingOrder: RenderOrder.CombatFx, holdFraction: jumpPuffHoldFraction);
    }

    // "Ring生成 -> 短時間で拡大 -> Fade Out", not following the player.
    //
    // BUG FOUND AND FIXED (Game Feel audit pass): every one-shot effect in
    // this file (this one included) was passing sortingOrder:
    // RenderOrder.EnvironmentFx (1) - the SAME depth as the ground tiles
    // and Enemy, which sits BEHIND Player (2). OneShotSpriteEffect's own
    // methods already default to RenderOrder.CombatFx (3, explicitly above
    // Player/Enemy - see that file's own comment on CombatFx) when no
    // sortingOrder is passed at all; this file overrode that default to a
    // lower value on every call, so the Double Jump Ring (spawned almost
    // exactly at the player's own position, unlike Jump/Landing which
    // spawn at the feet with some drift/spread) ended up fully covered by
    // the player sprite - not "not generated", just invisible behind
    // something bigger at the same spot. Fixed on all four calls in this
    // file that had it (Jump Puff, Double Jump Ring, Landing Puff, Run
    // Dust) - the Contact Shadow above deliberately keeps EnvironmentFx
    // (behind the character is correct for a shadow).
    void OnDoubleJumped()
    {
        Debug.Log("[GameFeel] DoubleJump detected");
        Sprite sprite = doubleJumpRingSprite != null ? doubleJumpRingSprite : doubleJumpDustSprite;
        // Visibility Pass - explicit world-unit sizing (see
        // doubleJumpRingStartScale/etc above) instead of a fraction of
        // dustScale, and a hold window so the ring is "一瞬しっかり表示"
        // before it expands and fades, rather than starting to fade the
        // instant it appears.
        OneShotSpriteEffect.CreateTweened(sprite, transform.position + effectOffset, doubleJumpRingColor, duration: doubleJumpRingDuration, startScale: doubleJumpRingStartScale, endScale: doubleJumpRingEndScale, startAlpha: 1f, endAlpha: 0f, sortingOrder: RenderOrder.CombatFx, holdFraction: doubleJumpRingHoldFraction);
        Debug.Log($"[GameFeel] DoubleJumpRing spawned (sprite=null:{sprite == null}, pos={transform.position + effectOffset})");
    }

    // "着地 -> 小さく発生 -> 横方向へ拡散 -> Fade Out" - a small left-right
    // scatter instead of one static puff (horizontalBias 2.2 flattens the
    // spread sideways rather than the even circle CreateScatterBurst
    // defaults to).
    void OnLanded()
    {
        // Visibility Pass - "着地した瞬間に左右へ広がったことが分かるサイズ"
        // (see landingPuffScaleMin/etc above).
        OneShotSpriteEffect.CreateScatterBurst(landingPuffSprite, FeetPosition(), Color.white, count: 3, duration: landingPuffDuration, scaleMin: landingPuffScaleMin, scaleMax: landingPuffScaleMax, spreadSpeed: landingPuffSpreadSpeed, horizontalBias: landingPuffHorizontalBias, sortingOrder: RenderOrder.CombatFx, holdFraction: landingPuffHoldFraction);
    }

    void Update()
    {
        UpdateContactShadow();
        UpdateRunDust();
        UpdateAscensionSmoke();
    }

    void UpdateAscensionSmoke()
    {
        if (!controller.IsAscending)
        {
            ascensionPuffTimer = 0f;
            return;
        }

        ascensionPuffTimer -= Time.deltaTime;
        if (ascensionPuffTimer <= 0f)
        {
            ascensionPuffTimer = ascensionPuffInterval;
            OneShotSpriteEffect.CreateAnimated(ascensionSmokeFrames, transform.position + effectOffset + Vector3.down * 0.3f, 12f, dustScale * 0.9f);
        }
    }

    // Light, continuous kick-up trail while actually running on the
    // ground - deliberately capped by runDustInterval rather than spawning
    // every frame, per the "assist the sense of speed, don't flood the
    // screen" brief. Uses CurrentAutoRunSpeed (not raw position delta) so
    // it reacts the same way regardless of frame-timing noise, and turns
    // itself off during the win-ascension flight/attack lunge the same as
    // it would for any other airborne state.
    void UpdateRunDust()
    {
        if (!runDustEnabled) return;
        // GameManager.HasStarted guards against spawning dust under the
        // player while it sits static at the title screen - Move() hasn't
        // run yet at that point, but CurrentAutoRunSpeed alone (being
        // permanently "would-be" nonzero) can't tell the difference.
        if (GameManager.Instance == null || !GameManager.Instance.HasStarted) return;
        if (controller == null || !controller.IsGrounded || controller.IsAscending || controller.CurrentAutoRunSpeed < runDustMinSpeed)
        {
            runDustTimer = 0f;
            return;
        }

        runDustTimer -= Time.deltaTime;
        if (runDustTimer <= 0f)
        {
            runDustTimer = runDustInterval;
            // 2-4 small independent particles at the kick position (not one
            // bigger sprite - see the class comment), each with its own
            // small random position/scale/rotation so a run doesn't look
            // like the exact same puff repeating. Kicked backward (opposite
            // the run direction) and slightly up, then settles/fades -
            // reads as "left behind by the character", not floating with
            // it, since each one is spawned once here and never touched
            // again (no parenting to the player).
            int puffCount = Random.Range(2, 5);
            for (int i = 0; i < puffCount; i++)
            {
                Vector3 pos = FeetPosition() + new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(0f, 0.05f), 0f);
                Vector3 drift = new Vector3(Random.Range(-1.6f, -0.9f), Random.Range(0.1f, 0.35f), 0f);
                float scale = runDustScale * Random.Range(0.6f, 1f);
                // Alternated randomly between Run Dust.png/Grass Dust.png
                // (falls back to the procedural dot, tinted, if neither is
                // assigned) - already-colored painterly art doesn't need
                // runDustColor's tint, just a lighter alpha to stay subtle.
                bool useGrass = grassDustSprite != null && Random.value < 0.5f;
                Sprite sprite = useGrass ? grassDustSprite : (runDustSprite != null ? runDustSprite : OneShotSpriteEffect.SoftDotSprite());
                Color tint = (runDustSprite != null || grassDustSprite != null) ? new Color(1f, 1f, 1f, runDustSpriteAlpha) : runDustColor;
                OneShotSpriteEffect.CreateTweened(sprite, pos, tint, duration: runDustDuration, startScale: scale * 0.5f, endScale: scale, drift: drift, rotationDegrees: Random.Range(-120f, 120f), sortingOrder: RenderOrder.CombatFx, holdFraction: runDustHoldFraction);
            }
        }
    }

    // A very faint ellipse tracking the actual ground/sky-path surface
    // (and its slope angle) directly beneath the player's X - not parented
    // to the player, so it stays correctly flat on the terrain and visible
    // as a landing-spot cue even while the player is airborne, instead of
    // just riding along with the jump. Hidden when there's no surface at
    // all below (e.g. over an open pit) or during the win-ascension flight.
    void UpdateContactShadow()
    {
        if (!contactShadowEnabled || shadowTransform == null || controller == null) return;
        if (controller.IsAscending || TerrainManager.Instance == null)
        {
            shadowRenderer.enabled = false;
            return;
        }

        float x = transform.position.x;
        float? groundY = TerrainManager.Instance.GetHeightAt(x);
        float? skyY = TerrainManager.Instance.GetSkyHeightAt(x);

        float? surfaceY = groundY;
        bool onGround = true;
        if (skyY.HasValue && (!surfaceY.HasValue || Mathf.Abs(skyY.Value - transform.position.y) < Mathf.Abs(surfaceY.Value - transform.position.y)))
        {
            surfaceY = skyY;
            onGround = false;
        }

        if (!surfaceY.HasValue)
        {
            shadowRenderer.enabled = false;
            return;
        }

        shadowRenderer.enabled = true;
        shadowTransform.position = new Vector3(x, surfaceY.Value + 0.02f, 0f);
        float angle = onGround ? TerrainManager.Instance.GetSlopeAngleAt(x) : 0f;
        shadowTransform.rotation = Quaternion.Euler(0f, 0f, angle);

        // Shrinks/fades with height above this surface - grounded (height
        // ~0) reads as full size/opacity, easing back the instant the
        // player lands (this recomputes every frame purely from current
        // height, so "the instant it lands" falls out for free).
        float heightAboveSurface = Mathf.Max(0f, transform.position.y - surfaceY.Value);
        float heightFrac = contactShadowFadeHeight > 0f ? Mathf.Clamp01(heightAboveSurface / contactShadowFadeHeight) : 0f;
        float scaleMul = Mathf.Lerp(1f, contactShadowMinScale, heightFrac);
        shadowTransform.localScale = new Vector3(contactShadowWidth * scaleMul, contactShadowHeight * scaleMul, 1f);
        Color shadowColor = contactShadowColor;
        shadowColor.a *= Mathf.Lerp(1f, contactShadowMinAlpha, heightFrac);
        shadowRenderer.color = shadowColor;
    }
}
