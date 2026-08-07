using UnityEngine;

/// <summary>
/// Ranged enemy: shoots projectiles at the player on cooldown while in range, and
/// slowly backs away if the player gets too close instead of standing still. No
/// animations — this enemy doesn't have any.
///
/// Like EnemyAI, it stands down entirely (no movement, no shooting) while its own
/// CombatTarget.CurrentState isn't Normal, and it's removed from the scene on
/// CombatTarget.OnDeath.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CombatTarget))]
public class RangedEnemyAI : MonoBehaviour
{
    [Header("Detection")]
    public Transform player;
    public float detectionRange = 10f;
    [Tooltip("If the player gets closer than this, the enemy backs away instead of standing still to shoot.")]
    public float retreatRange = 4f;
    [Tooltip("How fast the enemy backs away — kept slow/deliberate by design.")]
    public float retreatSpeed = 1.2f;

    [Header("Shooting")]
    [Tooltip("Needs a Projectile component (plus a Collider2D).")]
    public GameObject projectilePrefab;
    public float projectileSpeed = 8f;
    public float shootCooldown = 1.5f;
    public float projectileDamage = 8f;
    public float projectileStun = 0.4f;
    public Vector2 projectileKnockback = new Vector2(3f, 1f);
    [Tooltip("LayerMask containing the player, used by the projectile to detect a hit.")]
    public LayerMask playerLayer;
    [Tooltip("How far in front of the enemy the projectile spawns.")]
    public float shootSpawnDistance = 0.6f;
    [Tooltip("Vertical offset added to the spawn position, so the projectile fires from roughly chest height instead of the enemy's feet/pivot.")]
    public float shootSpawnHeight = 0.5f;

    private Rigidbody2D rb;
    private CombatTarget selfTarget;
    private bool isDead;
    private float nextShootTime;
    private float facingSign = 1f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        selfTarget = GetComponent<CombatTarget>();
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
        if (player == null) return;

        // Stunned/knockdown/grabbed: don't move or shoot, same as EnemyAI.
        if (selfTarget.CurrentState != CombatTarget.State.Normal)
        {
            return;
        }

        float distance = Vector2.Distance(transform.position, player.position);
        facingSign = Mathf.Sign(player.position.x - transform.position.x);

        if (distance > detectionRange)
        {
            rb.velocity = new Vector2(0f, rb.velocity.y);
            return;
        }

        if (distance < retreatRange)
        {
            // Kite away — moves opposite the player, slowly, while still shooting.
            rb.velocity = new Vector2(-facingSign * retreatSpeed, rb.velocity.y);
        }
        else
        {
            rb.velocity = new Vector2(0f, rb.velocity.y);
        }

        if (Time.time >= nextShootTime)
        {
            Shoot();
        }
    }

    private void Shoot()
    {
        nextShootTime = Time.time + shootCooldown;

        if (projectilePrefab == null)
        {
            Debug.Log("[RangedEnemyAI] projectilePrefab is not assigned in the Inspector — nothing will fire");
            return;
        }

        Vector2 direction = new Vector2(facingSign, 0f);
        Vector2 spawnPos = (Vector2)transform.position + direction * shootSpawnDistance + Vector2.up * shootSpawnHeight;

        GameObject projObj = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
        Projectile projectile = projObj.GetComponent<Projectile>();
        if (projectile != null)
        {
            Vector2 knockback = new Vector2(projectileKnockback.x * facingSign, projectileKnockback.y);
            projectile.Initialize(playerLayer, projectileDamage, projectileStun, knockback, gameObject, direction, projectileSpeed);
        }
        else
        {
            Debug.Log("[RangedEnemyAI] projectilePrefab has no Projectile component — it will spawn but never register a hit");
        }
    }

    private void HandleDeath()
    {
        if (isDead) return; // OnDeath could in theory fire more than once — guard against double-handling
        isDead = true;

        rb.velocity = Vector2.zero;
        enabled = false;

        Destroy(gameObject);
    }
}