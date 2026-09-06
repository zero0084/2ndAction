using UnityEngine;

// NOT [RequireComponent(typeof(SpriteRenderer))] - this component lives on
// Root, but the actual SpriteRenderer lives one level down on the "Visual"
// child (see SceneBuilder.CreatePlayer). That attribute used to be correct
// back when the SpriteRenderer sat directly on this same GameObject, but
// left in place after the Root/Visual split it silently auto-added a
// second, blank SpriteRenderer onto Root the moment this component was
// added there - which Awake()'s GetComponentInChildren then found FIRST
// (before descending into Visual), so this ended up driving/animating that
// stray Root-level renderer while Visual's real one sat frozen on its
// initial sprite - two overlapping player sprites, one animating, one
// permanently stuck. Confirmed and fixed by removing this attribute.
public class PlayerAnimator : MonoBehaviour
{
    public Sprite[] runFrames;
    public Sprite[] jumpFrames;
    public Sprite[] attackFrames;
    public Sprite[] attackFramesSmall;
    public Sprite[] attackFramesLarge;
    public Sprite[] jumpStartFrames;
    public Sprite[] doubleJumpFrames;
    public Sprite[] landFrames;
    public float runFps = 10f;
    public float jumpFps = 10f;
    public float attackFps = 12f;
    public float attackFpsSmall = 14f;
    public float attackFpsLarge = 10f;
    public float jumpStartFps = 7f;
    public float doubleJumpFps = 9f;
    public float landFps = 7f;

    [Header("Brighten overlay (emphasizes white on the dark source art)")]
    public Color brightenColor = new Color(1f, 1f, 1f, 0.15f);

    enum State { Run, JumpStart, Jump, DoubleJump, Landing, Attack }

    SpriteRenderer sr;
    SpriteRenderer brightenOverlay;
    PlayerController controller;
    float frameTimer;
    int frameIndex;
    State state = State.Run;

    // One-shot timers for the transient jump/land animations - set to the
    // clip's own length whenever the matching PlayerController event fires,
    // so each plays out fully once and then falls back to the ordinary
    // grounded/airborne state below (whichever timer is still running wins,
    // with double-jump taking priority over a still-running jump-start).
    float jumpStartTimer;
    float doubleJumpTimer;
    float landTimer;

    void Awake()
    {
        // The SpriteRenderer lives on the "Visual" child, not this Root -
        // see SceneBuilder.CreatePlayer.
        sr = GetComponentInChildren<SpriteRenderer>();
        controller = GetComponent<PlayerController>();

        // Parented under the same Visual sr lives on (not this component's
        // own Root transform) so it stays exactly aligned with the main
        // sprite even if Visual's own localPosition is ever nudged for a
        // future asset.
        GameObject overlayGO = new GameObject("BrightenOverlay");
        overlayGO.transform.SetParent(sr.transform, false);
        brightenOverlay = overlayGO.AddComponent<SpriteRenderer>();
        brightenOverlay.color = brightenColor;
    }

    void OnEnable()
    {
        if (controller == null) return;
        controller.JumpStarted += OnJumpStarted;
        controller.DoubleJumped += OnDoubleJumped;
        controller.Landed += OnLanded;
    }

    void OnDisable()
    {
        if (controller == null) return;
        controller.JumpStarted -= OnJumpStarted;
        controller.DoubleJumped -= OnDoubleJumped;
        controller.Landed -= OnLanded;
    }

    void OnJumpStarted()
    {
        if (jumpStartFrames != null && jumpStartFrames.Length > 0) jumpStartTimer = jumpStartFrames.Length / jumpStartFps;
    }

    void OnDoubleJumped()
    {
        if (doubleJumpFrames != null && doubleJumpFrames.Length > 0) doubleJumpTimer = doubleJumpFrames.Length / doubleJumpFps;
    }

    void OnLanded()
    {
        if (landFrames != null && landFrames.Length > 0) landTimer = landFrames.Length / landFps;
    }

    Sprite[] GetAttackFrames(int stage)
    {
        if (stage <= 1 && attackFramesSmall != null && attackFramesSmall.Length > 0) return attackFramesSmall;
        if (stage >= 3 && attackFramesLarge != null && attackFramesLarge.Length > 0) return attackFramesLarge;
        return attackFrames;
    }

    float GetAttackFps(int stage)
    {
        if (stage <= 1) return attackFpsSmall;
        if (stage >= 3) return attackFpsLarge;
        return attackFps;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (jumpStartTimer > 0f) jumpStartTimer -= dt;
        if (doubleJumpTimer > 0f) doubleJumpTimer -= dt;
        if (landTimer > 0f) landTimer -= dt;

        bool attacking = controller != null && controller.IsAttacking && attackFrames != null && attackFrames.Length > 0;
        bool grounded = controller == null || controller.IsGrounded;
        int attackStage = controller != null ? controller.CurrentAttackStage : 2;

        State newState;
        if (attacking) newState = State.Attack;
        else if (grounded && landTimer > 0f) newState = State.Landing;
        else if (!grounded && doubleJumpTimer > 0f) newState = State.DoubleJump;
        else if (!grounded && jumpStartTimer > 0f) newState = State.JumpStart;
        else if (!grounded) newState = State.Jump;
        else newState = State.Run;

        if (newState != state)
        {
            state = newState;
            frameIndex = 0;
            frameTimer = 0f;
        }

        Sprite[] frames = state switch
        {
            State.Attack => GetAttackFrames(attackStage),
            State.Jump => jumpFrames,
            State.JumpStart => jumpStartFrames,
            State.DoubleJump => doubleJumpFrames,
            State.Landing => landFrames,
            _ => runFrames
        };
        if (frames == null || frames.Length == 0) return;

        float fps = state switch
        {
            State.Attack => GetAttackFps(attackStage),
            State.Jump => jumpFps,
            State.JumpStart => jumpStartFps,
            State.DoubleJump => doubleJumpFps,
            State.Landing => landFps,
            _ => runFps
        };

        frameTimer += dt;
        if (frameTimer >= 1f / fps)
        {
            frameTimer = 0f;
            frameIndex++;
            frameIndex = state == State.Run
                ? frameIndex % frames.Length // loop while running
                : Mathf.Min(frameIndex, frames.Length - 1); // hold last frame otherwise
        }

        sr.sprite = frames[frameIndex];

        if (brightenOverlay != null)
        {
            brightenOverlay.sprite = sr.sprite;
            brightenOverlay.sortingOrder = sr.sortingOrder + 1;
            brightenOverlay.enabled = sr.enabled;
        }
    }
}
