using UnityEngine;

// Attach to your projectile prefab, alongside a Collider2D (any shape — forced to
// isTrigger in Awake). Spawned and configured via Initialize() by whatever fires it
// (e.g. RangedEnemyAI).
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class Projectile : MonoBehaviour
{
    private LayerMask targetLayer;
    private float damage;
    private float stunDuration;
    private Vector2 knockback;
    private GameObject source;

    private Rigidbody2D rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        GetComponent<Collider2D>().isTrigger = true;

        // Straight-line flight by default — remove this if you want the projectile
        // to arc under gravity instead.
        rb.gravityScale = 0f;
    }

    public void Initialize(LayerMask targetLayer, float damage, float stunDuration, Vector2 knockback, GameObject source, Vector2 direction, float speed, float lifetime = 5f)
    {
        this.targetLayer = targetLayer;
        this.damage = damage;
        this.stunDuration = stunDuration;
        this.knockback = knockback;
        this.source = source;

        rb.velocity = direction.normalized * speed;

        Destroy(gameObject, lifetime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (((1 << other.gameObject.layer) & targetLayer) == 0) return;

        CombatTarget target = other.GetComponent<CombatTarget>();
        if (target != null)
        {
            target.ApplyHit(new HitInfo(damage, stunDuration, knockback, source));
        }

        Destroy(gameObject);
    }
}