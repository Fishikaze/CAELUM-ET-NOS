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