using UnityEngine;

/// <summary>
/// Attach to anything that can be hit in fighting mode — the player and every enemy.
/// Owns health plus the shared hit-reaction state machine (Normal / Stunned / Knockdown / Grabbed).
///
/// Movement and AI scripts should check CurrentState before acting, and stand down
/// while it isn't Normal — that's the mechanism that makes stuns, knockdowns, and
/// grabs actually stop the target. See EnemyAI and FightingController for examples.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class CombatTarget : MonoBehaviour
{
    public enum State { Normal, Stunned, Knockdown, Grabbed }

    [Header("Health")]
    public float maxHealth = 100f;
    public float CurrentHealth { get; private set; }

    [Header("Blocking (only meaningful if this target can block, e.g. the player)")]
    [Tooltip("Drive this from your own input script (e.g. PlayerCombat sets it while right click is held).")]
    public bool isBlocking = false;
    [Range(0f, 1f)] public float blockDamageReduction = 0.9f;

    public State CurrentState { get; private set; } = State.Normal;
    public bool IsAvailableForCombo => CurrentState == State.Stunned || CurrentState == State.Knockdown;

    public event System.Action<HitInfo> OnHit;
    public event System.Action OnDeath;

    private Rigidbody2D rb;
    private float stateTimer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        CurrentHealth = maxHealth;
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
                CurrentState = State.Normal;
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
        if (CurrentState == State.Grabbed) return false; // already locked into another interaction

        if (isBlocking)
        {
            float blockedDamage = hit.damage * (1f - blockDamageReduction);
            CurrentHealth -= blockedDamage;

            // Small push instead of a full stun/launch — chip damage + a shove, no stun.
            if (rb != null) rb.velocity = new Vector2(hit.knockback.x * 0.15f, rb.velocity.y);

            OnHit?.Invoke(hit);
            return true;
        }

        CurrentHealth -= hit.damage;

        if (rb != null)
        {
            rb.velocity = hit.knockback;
        }

        SetState(hit.causesKnockdown ? State.Knockdown : State.Stunned, hit.stunDuration);

        OnHit?.Invoke(hit);

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

    public void ForceNormal()
    {
        CurrentState = State.Normal;
        stateTimer = 0f;
    }
}