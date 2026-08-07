using System.Collections;
using UnityEngine;

/// <summary>
/// Basic melee enemy: chases the player when in detection range, attacks on
/// cooldown when in attack range, and stands still otherwise. Reads its own
/// CombatTarget.CurrentState every frame — while Stunned/Knockdown/Grabbed it
/// simply does nothing, which is what lets the player's combos lock it down.
///
/// Attacking now plays an animation instead of dealing damage directly: Attack()
/// fires the attackAnimTrigger and just holds isAttacking for attackWindup
/// seconds (total time the enemy is locked into the swing). The actual hit is
/// spawned by SpawnAttackHitbox(), which you call from an Animation Event on the
/// attack clip at the frame the swing should connect — it instantiates
/// attackHitboxPrefab (your existing hitbox/effect setup) directly in front of
/// the enemy, along its facing direction. Since the hitbox spawns on the
/// horizontal facing axis (a front-on attack, same as the player's own K kick),
/// it lines up with the player's Side block the same way any other frontal
/// attack does — no special-casing needed here for that.
///
/// The SpriteRenderer and Animator live on a child object, so facing is tracked
/// as its own field (facingSign) and applied via SpriteRenderer.flipX — not by
/// scaling this transform, which would also mirror the Rigidbody2D/Collider2D
/// that live on this (parent) object.
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
    [Tooltip("Total time the enemy is locked into the attack (covers windup + recovery) before it can move or attack again.")]
    public float attackWindup = 0.3f;
    public float attackCooldown = 1.4f;
    [Tooltip("Stun applied to the player on a successful hit.")]
    public float attackStun = 0.4f;
    public Vector2 attackKnockback = new Vector2(4f, 2f);

    [Header("Attack Hitbox")]
    [Tooltip("Prefab with a Collider2D + MeleeHitbox component, plus whatever Animator/VFX plays the swing — reuses the effect you already have set up.")]
    public GameObject attackHitboxPrefab;
    [Tooltip("LayerMask containing the player, used by the spawned hitbox to detect a hit.")]
    public LayerMask playerLayer;
    [Tooltip("How far in front of the enemy the hitbox spawns.")]
    public float attackSpawnDistance = 0.8f;
    [Tooltip("Vertical offset added to the spawn position, so the hitbox appears at roughly chest height instead of at the enemy's feet/pivot (which is usually what's causing it to spawn in the ground).")]
    public float attackSpawnHeight = 0.5f;

    [Header("Animation")]
    [Tooltip("Auto-found via GetComponentInChildren if left empty — the Animator lives on a child object.")]
    public Animator animator;
    [Tooltip("Auto-found via GetComponentInChildren if left empty — used to flip the sprite (flipX) instead of the whole transform, since the SpriteRenderer lives on a child object.")]
    public SpriteRenderer spriteRenderer;
    [Tooltip("Animator TRIGGER fired when the enemy starts its attack.")]
    public string attackAnimTrigger = "Attack";
    [Tooltip("Animator TRIGGER fired when the enemy dies (CombatTarget.OnDeath, i.e. health drops to 0 or below).")]
    public string deathAnimTrigger = "Death";

    [Header("Death")]
    [Tooltip("How long to wait after death before the enemy is removed from the scene — gives the death animation time to play.")]
    public float destroyDelay = 2f;

    private Rigidbody2D rb;
    private CombatTarget selfTarget;
    private CombatTarget playerTarget;
    private bool isAttacking;
    private float nextAttackTime;
    private bool isDead;

    // Default/unflipped art faces left, so flipX triggers when facing right (positive).
    private float facingSign = 1f;

    private float FacingSign => facingSign;
    private Vector2 FacingDir => new Vector2(FacingSign, 0f);

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        selfTarget = GetComponent<CombatTarget>();
        if (player != null) playerTarget = player.GetComponent<CombatTarget>();

        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    private void OnEnable()
    {
        selfTarget.OnDeath += HandleDeath;
    }

    private void OnDisable()
    {
        selfTarget.OnDeath -= HandleDeath;
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

        // Flip to face the player. The SpriteRenderer lives on a child object, so we
        // flip it (flipX) instead of scaling this transform — scaling the parent
        // would also mirror the Rigidbody2D/Collider2D that live here.
        facingSign = direction;
        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = facingSign > 0f;
        }
    }

    private IEnumerator Attack()
    {
        isAttacking = true;
        nextAttackTime = Time.time + attackCooldown;

        if (animator != null)
        {
            animator.SetTrigger(attackAnimTrigger);
        }

        // Holds the enemy in place for the attack's duration. The actual hit is
        // spawned by SpawnAttackHitbox() via an Animation Event partway through this
        // window, not by anything in this coroutine.
        yield return new WaitForSeconds(attackWindup);

        isAttacking = false;
    }

    /// <summary>
    /// Call this from an Animation Event on the attack clip, at the frame the swing
    /// should actually connect. Spawns attackHitboxPrefab in front of the enemy along
    /// its current facing direction.
    /// </summary>
    public void SpawnAttackHitbox()
    {
        // If the enemy got stunned/knocked down (or died) mid-swing, don't let a
        // queued animation event still land a hit.
        if (isDead || selfTarget.CurrentState != CombatTarget.State.Normal) return;

        if (attackHitboxPrefab == null)
        {
            Debug.Log("[EnemyAI] attackHitboxPrefab is not assigned in the Inspector — nothing will spawn");
            return;
        }

        Vector2 spawnPos = (Vector2)transform.position + FacingDir * attackSpawnDistance + Vector2.up * attackSpawnHeight;
        float angle = FacingSign >= 0f ? 0f : 180f;

        GameObject hitboxObj = Instantiate(attackHitboxPrefab, spawnPos, Quaternion.Euler(0f, 0f, angle));
        MeleeHitbox hitbox = hitboxObj.GetComponent<MeleeHitbox>();
        if (hitbox != null)
        {
            // knockbackForce is left at 0 here because our knockback isn't purely along
            // the facing axis (it has a vertical arc component, same as PlayerCombat's
            // slam) — OnAttackConnect applies the full Vector2 knockback directly.
            hitbox.Initialize(playerLayer, attackDamage, attackStun, 0f, gameObject, FacingDir, OnAttackConnect);
        }
    }

    private void HandleDeath()
    {
        if (isDead) return; // OnDeath could in theory fire more than once — guard against double-handling
        isDead = true;

        StopAllCoroutines();
        isAttacking = false;
        rb.velocity = Vector2.zero;

        if (animator != null)
        {
            animator.SetTrigger(deathAnimTrigger);
        }

        // Disabling stops FixedUpdate from running any further movement/attack logic.
        enabled = false;

        Destroy(gameObject, destroyDelay);
    }

    private void OnAttackConnect(CombatTarget target)
    {
        Vector2 knockback = new Vector2(attackKnockback.x * FacingSign, attackKnockback.y);

        Rigidbody2D targetRb = target.GetComponent<Rigidbody2D>();
        if (targetRb != null)
        {
            targetRb.velocity = knockback;
        }
    }
}