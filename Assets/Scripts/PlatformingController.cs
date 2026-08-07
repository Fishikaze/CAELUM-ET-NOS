using UnityEngine;

/// <summary>
/// Basic platformer movement: run + jump, with coyote time and jump buffering so
/// it feels forgiving, plus a variable jump height and faster-falling gravity for
/// a snappier arc. Disabled automatically by PlayerModeController while the
/// player is in Fighting mode.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlatformingController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 7f;
    public float jumpForce = 14f;

    [Header("Feel")]
    public float coyoteTime = 0.1f;
    public float jumpBufferTime = 0.1f;
    public float fallGravityMultiplier = 2.2f; // falls faster than it rises

    [Header("Ground Check")]
    [Tooltip("Empty child transform placed at the player's feet.")]
    public Transform groundCheck;
    public float groundCheckRadius = 0.15f;
    public LayerMask groundLayer;

    [Header("Wall Friction")]
    [Tooltip("If the player's Collider2D has no Physics Material 2D assigned, one with 0 friction is created and applied at runtime. This is what stops the player sticking to walls: with the default (nonzero) friction, pressing into a wall while falling generates enough friction against that vertical surface to fight gravity and stall the player mid-air instead of sliding down it. Leave this on unless you've already set up your own zero-friction material on the collider (in which case that one wins and this does nothing).")]
    public bool preventWallSticking = true;

    [Header("Animation")]
    [Tooltip("Auto-found via GetComponent/GetComponentInChildren if left empty.")]
    public Animator animator;
    [Tooltip("Auto-found via GetComponent/GetComponentInChildren if left empty. Flips via SpriteRenderer.flipX (not the whole transform), same approach as PlayerCombat uses in Fighting mode — keeps physics/collision untouched.")]
    public SpriteRenderer spriteRenderer;
    [Tooltip("Animator BOOL parameter driven continuously by horizontal speed — same parameter name PlayerCombat uses in Fighting mode, so both controllers drive the same Animator Controller state consistently regardless of which mode is active.")]
    public string isMovingAnimParam = "IsMoving";
    [Tooltip("Horizontal speed above which the player counts as 'running' for animation purposes.")]
    public float runAnimThreshold = 0.1f;

    public float FacingSign { get; private set; } = 1f;

    private Rigidbody2D rb;
    private float defaultGravityScale;
    private float coyoteTimer;
    private float jumpBufferTimer;
    private bool isGrounded;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        defaultGravityScale = rb.gravityScale;

        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (preventWallSticking)
        {
            ApplyZeroFrictionMaterial();
        }
    }

    /// <summary>
    /// Assigns a 0-friction Physics Material 2D to the player's own Collider2D,
    /// replacing whatever material (if any) is already there — including the
    /// project's default, which is the usual source of the stick since its friction
    /// is nonzero. Doesn't touch the wall/ground colliders themselves — Unity 2D
    /// friction between two colliders combines as sqrt(frictionA * frictionB), so a
    /// frictionless material on just this side is enough to zero it out either way.
    /// </summary>
    private void ApplyZeroFrictionMaterial()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col == null) col = GetComponentInChildren<Collider2D>();
        if (col == null) return;

        PhysicsMaterial2D noFriction = new PhysicsMaterial2D("PlayerNoFriction")
        {
            friction = 0f,
            bounciness = 0f
        };
        col.sharedMaterial = noFriction;
        Debug.Log("[PlatformingController] applied zero-friction material to " + col.name);
    }

    private void Update()
    {
        isGrounded = groundCheck != null &&
                     Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

        coyoteTimer = isGrounded ? coyoteTime : coyoteTimer - Time.deltaTime;
        jumpBufferTimer = Input.GetButtonDown("Jump") ? jumpBufferTime : jumpBufferTimer - Time.deltaTime;

        // Variable jump height: cut the upward velocity short if the button is
        // released early (classic Mario/Celeste-style jump).
        if (Input.GetButtonUp("Jump") && rb.velocity.y > 0f)
        {
            rb.velocity = new Vector2(rb.velocity.x, rb.velocity.y * 0.5f);
        }

        // Same flip approach as PlayerCombat's Fighting-mode version — mirrors the
        // sprite based on FacingSign (set below in FixedUpdate from horizontal input),
        // assuming default/unflipped art faces right. Flip the condition to > 0f if
        // your sprite's default orientation is actually left-facing.
        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = FacingSign < 0f;
        }

        // Running/Idle — PlayerCombat drives this same parameter while in Fighting
        // mode, but it's disabled while Platforming, so nothing was setting it here
        // before. Without this, the Animator just sits wherever it last was instead
        // of transitioning to Run at all while platforming.
        if (animator != null)
        {
            animator.SetBool(isMovingAnimParam, Mathf.Abs(rb.velocity.x) > runAnimThreshold);
        }
    }

    private void FixedUpdate()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        rb.velocity = new Vector2(horizontal * moveSpeed, rb.velocity.y);

        if (horizontal != 0f)
        {
            FacingSign = Mathf.Sign(horizontal);
        }

        if (jumpBufferTimer > 0f && coyoteTimer > 0f)
        {
            rb.velocity = new Vector2(rb.velocity.x, jumpForce);
            jumpBufferTimer = 0f;
            coyoteTimer = 0f;
        }

        rb.gravityScale = rb.velocity.y < 0f ? defaultGravityScale * fallGravityMultiplier : defaultGravityScale;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
#endif
}