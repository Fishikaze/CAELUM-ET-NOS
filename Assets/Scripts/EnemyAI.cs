using System.Collections;
using UnityEngine;

/// <summary>
/// Basic melee enemy: chases the player when in detection range, attacks on
/// cooldown when in attack range, and stands still otherwise. Reads its own
/// CombatTarget.CurrentState every frame — while Stunned/Knockdown/Grabbed it
/// simply does nothing, which is what lets the player's combos lock it down.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CombatTarget))]
public class EnemyAI : MonoBehaviour
{
    [Header("Detection")]
    public Transform player;
    public float detectionRange = 8f;
    public float attackRange = 1.3f;
    public float moveSpeed = 2.5f;

    [Header("Attack")]
    public float attackDamage = 10f;
    public float attackWindup = 0.3f;
    public float attackCooldown = 1.4f;
    public Vector2 attackKnockback = new Vector2(4f, 2f);

    private Rigidbody2D rb;
    private CombatTarget selfTarget;
    private CombatTarget playerTarget;
    private bool isAttacking;
    private float nextAttackTime;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        selfTarget = GetComponent<CombatTarget>();
        if (player != null) playerTarget = player.GetComponent<CombatTarget>();
    }

    private void FixedUpdate()
    {
        if (player == null || playerTarget == null) return;

        // Locked down by a stun/knockdown/grab, or already mid-swing: don't drive any
        // movement of our own, but don't touch rb.velocity either. This used to zero
        // out velocity.x every physics tick here, which — since it runs the instant
        // after CombatTarget.ApplyHit sets velocity to the player's knockback — was
        // cancelling the X component of that knockback almost immediately, leaving
        // only Y. It wasn't protecting against anything real: the chase logic below
        // that actually drives velocity.x doesn't run in this branch anyway.
        if (selfTarget.CurrentState != CombatTarget.State.Normal || isAttacking)
        {
            return;
        }

        float distance = Vector2.Distance(transform.position, player.position);

        if (distance > detectionRange)
        {
            rb.velocity = new Vector2(0f, rb.velocity.y);
            return;
        }

        if (distance <= attackRange)
        {
            rb.velocity = new Vector2(0f, rb.velocity.y);
            if (Time.time >= nextAttackTime)
            {
                StartCoroutine(Attack());
            }
            return;
        }

        // Chase.
        float direction = Mathf.Sign(player.position.x - transform.position.x);
        rb.velocity = new Vector2(direction * moveSpeed, rb.velocity.y);

        // Flip to face the player — adjust the sign if your art faces left by default.
        Vector3 scale = transform.localScale;
        scale.x = Mathf.Abs(scale.x) * direction;
        transform.localScale = scale;
    }

    private IEnumerator Attack()
    {
        isAttacking = true;
        nextAttackTime = Time.time + attackCooldown;

        yield return new WaitForSeconds(attackWindup);

        // Re-check state/range after the windup — the player may have escaped,
        // or this enemy may have been knocked down mid-swing.
        if (selfTarget.CurrentState == CombatTarget.State.Normal && player != null)
        {
            float distance = Vector2.Distance(transform.position, player.position);
            if (distance <= attackRange + 0.3f)
            {
                float direction = Mathf.Sign(player.position.x - transform.position.x);
                Vector2 knockback = new Vector2(attackKnockback.x * direction, attackKnockback.y);
                playerTarget.ApplyHit(new HitInfo(attackDamage, 0.4f, knockback, gameObject));
            }
        }

        isAttacking = false;
    }
}