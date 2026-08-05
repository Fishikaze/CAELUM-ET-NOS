using UnityEngine;

/// <summary>
/// Attach to anything that can be hit in fighting mode — the player and every enemy.
/// Owns health plus the shared hit-reaction state machine (Normal / Stunned / Knockdown / Grabbed).
///
/// Movement and AI scripts should check CurrentState before acting, and stand down
/// while it isn't Normal — that's the mechanism that makes stuns, knockdowns, and
/// grabs actually stop the target. See EnemyAI and FightingController for examples.
///
/// Purely logic/state — no visual side effects. Hit flashes and the Stunned/Knockdown
/// tint live entirely in HitReactionVisuals, which just polls CurrentState each frame.
/// This used to also push color changes to a SpriteRenderer directly from SetState(),
/// which fought with HitReactionVisuals for the same SpriteRenderer.color — whichever
/// of the two ran later in a given frame would win, so the tint could flicker or get
/// silently overwritten depending on script execution order. Removed rather than
/// reconciled, since HitReactionVisuals already covers this (and covers Knockdown too,
/// which this never did — it only ever tinted for Stunned).
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class CombatTarget : MonoBehaviour
{
    public enum State { Normal, Stunned, Knockdown, Grabbed }

    [Header("Health")]
    public float maxHealth = 100f;
    public float CurrentHealth { get; private set; }

    [Header("Blocking (only meaningful if this target can block, e.g. the player)")]
    [Tooltip("Drive this from your own input script (e.g. PlayerCombat sets it while block is held).")]
    public bool isBlocking = false;
    [Range(0f, 1f)] public float blockDamageReduction = 0.9f;

    public State CurrentState { get; private set; } = State.Normal;
    public bool IsAvailableForCombo => CurrentState == State.Stunned || CurrentState == State.Knockdown;

    /// <summary>
    /// While true, ApplyHit is a no-op (used for the brief i-frame window after
    /// a J-combo finisher, so the enemy can't be re-comboed instantly).
    /// </summary>
    public bool IsInvulnerable { get; private set; }

    public event System.Action<HitInfo> OnHit;
    public event System.Action OnDeath;
    [Tooltip("Fired whenever CurrentHealth changes, with (currentHealth, maxHealth) — also fired once on Awake so a UI element can initialize before any hit happens. HealthBarUI listens to this; it's the thing to hook a health bar up to instead of polling CurrentHealth every frame.")]
    public event System.Action<float, float> OnHealthChanged;

    private Rigidbody2D rb;
    private float stateTimer;
    private float invulnerabilityTimer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        CurrentHealth = maxHealth;
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
    }

    private void Update()
    {
        // Non-Normal states are time-limited; count them down here so every caller
        // (player combo logic, enemy AI) can just poll CurrentState.
        if (CurrentState != State.Normal)
        {
            stateTimer -= Time.deltaTime;
            if (stateTimer <= 0f)
            {
                SetState(State.Normal, 0f);
            }
        }

        if (IsInvulnerable)
        {
            invulnerabilityTimer -= Time.deltaTime;
            if (invulnerabilityTimer <= 0f)
            {
                IsInvulnerable = false;
            }
        }
    }

    /// <summary>
    /// Main entry point for landing a hit on this target. Returns true if the hit
    /// actually connected (as opposed to being fully no-sold), so attackers can
    /// decide whether it's safe to continue a combo.
    /// </summary>
    public bool ApplyHit(HitInfo hit)
    {
        if (IsInvulnerable)
        {
            Debug.Log("[CombatTarget] " + gameObject.name + " ignored hit — currently invulnerable");
            return false; // i-frame window — hit doesn't land at all
        }
        if (CurrentState == State.Grabbed)
        {
            Debug.Log("[CombatTarget] " + gameObject.name + " ignored hit — currently Grabbed");
            return false; // already locked into another interaction
        }

        if (isBlocking)
        {
            float blockedDamage = hit.damage * (1f - blockDamageReduction);
            CurrentHealth -= blockedDamage;

            // Small push instead of a full stun/launch — chip damage + a shove, no stun.
            if (rb != null) rb.velocity = new Vector2(hit.knockback.x * 0.15f, rb.velocity.y);

            Debug.Log("[CombatTarget] " + gameObject.name + " blocked hit — full damage=" + hit.damage
                + ", reduction=" + blockDamageReduction + ", actual damage=" + blockedDamage
                + ", health now " + CurrentHealth + "/" + maxHealth);

            OnHit?.Invoke(hit);
            OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
            return true;
        }

        CurrentHealth -= hit.damage;

        Debug.Log("[CombatTarget] " + gameObject.name + " took " + hit.damage + " damage — health now "
            + CurrentHealth + "/" + maxHealth + " (isBlocking was false)");

        if (rb != null)
        {
            rb.velocity = hit.knockback;
        }

        SetState(hit.causesKnockdown ? State.Knockdown : State.Stunned, hit.stunDuration);

        OnHit?.Invoke(hit);
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);

        if (CurrentHealth <= 0f)
        {
            OnDeath?.Invoke();
        }

        return true;
    }

    /// <summary>Force this target into a state for a duration (used by grabs and knockdowns).</summary>
    public void SetState(State state, float duration)
    {
        CurrentState = state;
        stateTimer = duration;
    }

    /// <summary>Makes this target immune to ApplyHit for the given duration.</summary>
    public void SetInvulnerable(float duration)
    {
        IsInvulnerable = true;
        invulnerabilityTimer = duration;
    }

    public void ForceNormal()
    {
        SetState(State.Normal, 0f);
    }
}