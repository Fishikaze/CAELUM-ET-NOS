using UnityEngine;

/// <summary>
/// Lives on a melee hitbox prefab — used by both E (heavy kick) and R (knockdown).
/// Expected prefab setup:
///   - A Collider2D (BoxCollider2D works well) with "Is Trigger" checked —
///     this script forces it on anyway in Initialize().
///   - Whatever Animator/Animation/particle setup plays your swing effect.
///     This script doesn't touch it — just let it play.
///
/// PlayerCombat instantiates one of these, calls Initialize(), and the prefab
/// is expected to destroy itself once its swing has played out (see
/// selfDestructTime). On its first overlap with something on the target
/// layer: applies damage + knockback via CombatTarget.ApplyHit, triggers a
/// brief global hitstop, and disables its own collider so it can't land a
/// second hit during the rest of the swing.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class MeleeHitbox : MonoBehaviour
{
    [Tooltip("Destroyed automatically after this many seconds — line this up with your swing animation's length.")]
    public float selfDestructTime = 0.35f;
    public float hitStopDuration = 1f / 15f;

    private LayerMask targetLayer;
    private float damage;
    private float stunDuration;
    private float knockbackForce;
    private GameObject source;
    private Vector2 direction = Vector2.right;
    private bool causesKnockdown;
    private System.Action<CombatTarget> onHit;

    private Collider2D col;
    private bool hasHit;

    /// <summary>Call immediately after Instantiate. direction should already be normalized (e.g. aimed at the mouse).</summary>
    public void Initialize(LayerMask targetLayer, float damage, float stunDuration, float knockbackForce,
        GameObject source, Vector2 direction, System.Action<CombatTarget> onHit = null, bool causesKnockdown = false)
    {
        this.targetLayer = targetLayer;
        this.damage = damage;
        this.stunDuration = stunDuration;
        this.knockbackForce = knockbackForce;
        this.source = source;
        this.direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        this.onHit = onHit;
        this.causesKnockdown = causesKnockdown;

        col = GetComponent<Collider2D>();
        col.isTrigger = true;
    }

    private void Start()
    {
        Destroy(gameObject, selfDestructTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (hasHit) return;
        if (((1 << other.gameObject.layer) & targetLayer) == 0) return;

        CombatTarget target = other.GetComponent<CombatTarget>();
        if (target == null) return;

        hasHit = true;
        if (col != null) col.enabled = false; // one hit per swing, even if it lingers near multiple enemies

        Vector2 knockback = direction * knockbackForce;
        target.ApplyHit(new HitInfo(damage, stunDuration, knockback, source, causesKnockdown));

        HitStop.Trigger(hitStopDuration);
        onHit?.Invoke(target);
    }
}