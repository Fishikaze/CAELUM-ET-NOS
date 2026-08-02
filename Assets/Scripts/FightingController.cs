using UnityEngine;

/// <summary>
/// Fighting-game style ground movement: slower, more deliberate walk than
/// platforming mode. Movement is locked out entirely while PlayerCombat is
/// mid-action (dashing/attacking) or while CombatTarget says the player is
/// Stunned/Knockdown/Grabbed — that's what makes enemy hits actually stop you.
/// Disabled automatically outside Fighting mode.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CombatTarget))]
public class FightingController : MonoBehaviour
{
    public float walkSpeed = 4f;

    public float FacingSign { get; private set; } = 1f;

    /// <summary>Set by PlayerCombat while a dash/windup has movement locked out.</summary>
    public bool MovementLocked { get; set; } = false;

    private Rigidbody2D rb;
    private CombatTarget combatTarget;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        combatTarget = GetComponent<CombatTarget>();
    }

    private void FixedUpdate()
    {
        if (MovementLocked || combatTarget.CurrentState != CombatTarget.State.Normal)
        {
            return; // PlayerCombat's coroutine, or a stun/knockdown, is driving things right now
        }

        float horizontal = Input.GetAxisRaw("Horizontal");
        rb.velocity = new Vector2(horizontal * walkSpeed, rb.velocity.y);

        if (horizontal != 0f)
        {
            FacingSign = Mathf.Sign(horizontal);
        }
    }
}