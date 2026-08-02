using UnityEngine;

/// <summary>
/// Describes a single combat hit — carried from the attacker to whatever it hits.
/// Kept as a plain struct so any script (player or enemy) can build and pass one
/// without needing to reference a specific attacker class.
/// </summary>
public struct HitInfo
{
    public float damage;
    public float stunDuration;      // how long the target is locked in Stunned/Knockdown state
    public bool causesKnockdown;    // true => target goes to Knockdown instead of Stunned
    public Vector2 knockback;       // velocity applied to the target on hit
    public GameObject source;       // who threw the hit (handy for damage popups, avoiding self-hits, etc.)

    public HitInfo(float damage, float stunDuration, Vector2 knockback, GameObject source, bool causesKnockdown = false)
    {
        this.damage = damage;
        this.stunDuration = stunDuration;
        this.knockback = knockback;
        this.source = source;
        this.causesKnockdown = causesKnockdown;
    }
}