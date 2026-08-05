using UnityEngine;

/// <summary>
/// Lives on a melee hitbox prefab — used by K (kick), L (slam), and the ; boot
/// (stomp/punt).
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
    private Vector2 pinnedPosition;

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

        // None of these hitboxes are meant to move after they spawn — Kick/Slam/Boot
        // are all static swings. Cache wherever Instantiate placed us, and reassert it
        // every frame in LateUpdate, so an Animator clip with a stray Position curve
        // (a very common accidental cause) can't drag the hitbox away from its spawn
        // point after the fact.
        pinnedPosition = transform.position;

        Debug.Log("[MeleeHitbox] Initialize on " + gameObject.name + " — targetLayer=" + targetLayer.value
            + ", collider=" + col.GetType().Name + " enabled=" + col.enabled + " isTrigger=" + col.isTrigger
            + ", pinned at " + pinnedPosition);
    }

    private void Start()
    {
        Destroy(gameObject, selfDestructTime);
    }

    private void LateUpdate()
    {
        // Runs after Animator evaluation, so this wins over any animated Transform
        // curve on this object. If you WANT a hitbox to actually travel (e.g. a
        // future dash-attack), don't add that motion via an Animator clip on this
        // object — this will fight it. Move it via script instead, using SetPosition
        // below, which updates pinnedPosition too so this doesn't immediately undo it.
        if ((Vector2)transform.position != pinnedPosition)
        {
            transform.position = pinnedPosition;
        }
    }

    /// <summary>
    /// Moves the hitbox to a new world position — use this instead of setting
    /// transform.position directly, since it also updates the pin LateUpdate enforces.
    /// Setting transform.position alone would just get reverted next frame.
    /// </summary>
    public void SetPosition(Vector2 worldPosition)
    {
        pinnedPosition = worldPosition;
        transform.position = worldPosition;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Debug.Log("[MeleeHitbox] " + gameObject.name + " overlapped " + other.gameObject.name
            + " (layer " + LayerMask.LayerToName(other.gameObject.layer) + ")");

        if (hasHit)
        {
            Debug.Log("[MeleeHitbox] ignored — already landed a hit this swing");
            return;
        }

        if (((1 << other.gameObject.layer) & targetLayer) == 0)
        {
            Debug.Log("[MeleeHitbox] ignored — " + other.gameObject.name + "'s layer isn't in targetLayer (mask="
                + targetLayer.value + ")");
            return;
        }

        // Look up the hierarchy, not just the exact collider's own GameObject — a
        // common setup puts the collider on a child and CombatTarget on the parent,
        // which GetComponent alone would miss silently.
        CombatTarget target = other.GetComponentInParent<CombatTarget>();
        if (target == null)
        {
            Debug.Log("[MeleeHitbox] ignored — no CombatTarget found on " + other.gameObject.name + " or its parents");
            return;
        }

        hasHit = true;
        if (col != null) col.enabled = false; // one hit per swing, even if it lingers near multiple enemies

        Vector2 knockback = direction * knockbackForce;
        target.ApplyHit(new HitInfo(damage, stunDuration, knockback, source, causesKnockdown));

        Debug.Log("[MeleeHitbox] connected with " + target.name);

        HitStop.Trigger(hitStopDuration);
        onHit?.Invoke(target);
    }
}