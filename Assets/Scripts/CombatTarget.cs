using UnityEngine;

/// <summary>
/// Attach to anything that can be hit in fighting mode — the player and every enemy.
/// Owns health plus the shared hit-reaction state machine (Normal / Stunned / Knockdown / Grabbed).
///
/// Movement and AI scripts should check CurrentState before acting, and stand down
/// while it isn't Normal — that's the mechanism that makes stuns, knockdowns, and
/// grabs actually stop the target. See EnemyAI, BossAI, and FightingController for
/// examples.
///
/// Blocking is a directional parry, not a damage-reduction chip-block: a hit is
/// fully negated only when isBlocking is true AND blockDirection matches the
/// attack's own AttackDirection tag exactly (Unblockable attacks are never negated,
/// regardless of blockDirection). There's no partial/wrong-direction protection —
/// mismatched or absent blocking takes full damage. PlayerCombat drives isBlocking
/// and blockDirection from its own directional block-window input; nothing here
/// decides timing or direction, this just checks the match at the moment of impact.
///
/// Purely logic/state — no visual side effects. Hit flashes and the Stunned/Knockdown
/// tint live entirely in HitReactionVisuals, which just polls CurrentState each frame.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class CombatTarget : MonoBehaviour
{
    public enum State { Normal, Stunned, Knockdown, Grabbed }

    [Header("Health")]
    public float maxHealth = 100f;
    public float CurrentHealth { get; private set; }

    [Header("Blocking / Parry (only meaningful if this target can block, e.g. the player)")]
    [Tooltip("True while an active block window is up. Drive this from your own input script (e.g. PlayerCombat while a block window is open).")]
    public bool isBlocking = false;
    [Tooltip("Which direction the current block window is guarding — only meaningful while isBlocking is true.")]
    public AttackDirection blockDirection = AttackDirection.Side;

    public State CurrentState { get; private set; } = State.Normal;
    public bool IsAvailableForCombo => CurrentState == State.Stunned || CurrentState == State.Knockdown;

    /// <summary>
    /// While true, ApplyHit is a no-op (used for the brief i-frame window after
    /// a J-combo finisher, so the enemy can't be re-comboed instantly).
    /// </summary>
    public bool IsInvulnerable { get; private set; }

    public event System.Action<HitInfo> OnHit;
    public event System.Action OnDeath;
    [Tooltip("Fired whenever CurrentHealth changes, with (currentHealth, maxHealth) — also fired once on Awake so a UI element can initialize before any hit happens.")]
    public event System.Action<float, float> OnHealthChanged;
    [Tooltip("Fired when isBlocking + blockDirection successfully negates a hit, with the AttackDirection that was blocked — this is what a shield-particle effect should hook into.")]
    public event System.Action<AttackDirection> OnParrySuccess;

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
    /// actually connected (as opposed to being fully no-sold via i-frames or a
    /// successful parry), so attackers can decide whether it's safe to continue a combo.
    ///
    /// attackDirection defaults to Side so every existing call site (Kick/Slam/Boot in
    /// PlayerCombat, EnemyAI's basic attack) keeps compiling and behaving the same —
    /// only attacks that specifically want to participate in the parry system
    /// (currently the boss, via MeleeHitbox's attackDirection param) need to pass one.
    /// </summary>
    public bool ApplyHit(HitInfo hit, AttackDirection attackDirection = AttackDirection.Side)
    {
        if (IsInvulnerable) return false; // i-frame window — hit doesn't land at all
        if (CurrentState == State.Grabbed) return false; // already locked into another interaction

        if (isBlocking && attackDirection != AttackDirection.Unblockable && blockDirection == attackDirection)
        {
            // Full parry: no damage, no knockback, no state change at all.
            OnParrySuccess?.Invoke(attackDirection);
            return false;
        }

        CurrentHealth -= hit.damage;

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