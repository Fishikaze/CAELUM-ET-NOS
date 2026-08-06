using System.Collections;
using UnityEngine;

/// <summary>
/// Boss controller. Four attacks in rotation:
///   - Basic Swing: 3 forward slashes (Side), immediate down-slash follow-up
///     (Overhead) only if the 3rd slash connects.
///   - Overhead Attack: standalone single overhead swing.
///   - Combo: 2 forward slashes (visually identical to Basic Swing's) followed by an
///     unblockable low kick — the sprite tints red for the duration so the player can
///     tell this isn't the Basic Swing before the kick's threat becomes obvious.
///   - Thrust: a single forward thrust.
///
/// Animation: the boss only has Down / Thrust / Slash clips (no Idle or Walk), driven
/// as Animator BOOL parameters (Any State -> [state] transitions gated on each bool).
/// The model is deliberately simple: bool goes true, wait animBoolHoldDuration
/// (one shared, editable value — not per-clip), bool goes false. The hitbox is
/// spawned ONLY by that clip's own AnimEvent_SpawnHitbox Animation Event — there's
/// no fallback timer anymore, so an attack whose clip doesn't have that event wired
/// up will play its animation but deal no damage. Add the event in the Animation
/// window at the frame the swing should connect, calling AnimEvent_SpawnHitbox with
/// no parameters (via BossAnimationEventRelay if the Animator lives on a child
/// GameObject rather than this one).
///
/// The Combo Chain's kick has no animation clip at all, so it's the one exception —
/// it still spawns on a plain hardcoded timer (comboKickWindup/ActiveDuration),
/// since there's no bool or event to hook it into.
///
/// Architecture note — interruption handling lives in the MAIN LOOP, not in each
/// chain: if the boss gets hit mid-attack, the loop force-stops whatever coroutine is
/// running and waits for CombatTarget.State.Normal before resuming. That's also why
/// the Combo Chain's red tint, the forward-slash visual offset, and all three
/// animator bools get reset defensively at the top of the loop, not only at the end
/// of whichever chain was running — a force-stopped coroutine never reaches its own
/// cleanup code.
///
/// Phases: currentPhase only ever sits at Phase1 right now — CheckPhaseTransition is
/// the deliberate no-op stub where the half-HP run-away and Phase2 (blue attacks)
/// switch-over both plug in later, per the original brief not to build those yet.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CombatTarget))]
public class BossAI : MonoBehaviour
{
    public enum Phase { Phase1, Phase2 }

    [Header("Setup")]
    public Transform player;
    public LayerMask playerLayer;
    [Tooltip("Empty child positioned at chest height. Hitboxes spawn relative to here.")]
    public Transform hitOrigin;

    [Header("Movement")]
    public float moveSpeed = 2f;
    [Tooltip("Distance from the player at which the boss stops approaching and is close enough to attack.")]
    public float attackRange = 1.5f;

    [Header("Phase (Phase2 / half-HP behavior NOT implemented yet — see CheckPhaseTransition)")]
    public Phase currentPhase = Phase.Phase1;
    [Range(0f, 1f)] public float halfHpThreshold = 0.5f;

    [Header("Animation (boss only has Down / Thrust / Slash — no Idle/Walk)")]
    [Tooltip("Auto-found via GetComponentInChildren if left empty.")]
    public Animator animator;
    [Tooltip("Animator BOOL parameter for every forward-slash swing — used by both Basic Swing's 3 slashes and Combo's 2 slashes.")]
    public string forwardSlashAnimTrigger = "Slash";
    [Tooltip("Animator BOOL parameter for the Overhead Attack and Basic Swing's hit-confirm down slash.")]
    public string overheadAnimTrigger = "Down";
    [Tooltip("Animator BOOL parameter for the Thrust Attack.")]
    public string thrustAnimTrigger = "Thrust";
    [Tooltip("How long every animated attack's bool stays true before going back false — one shared value for Slash/Down/Thrust. The hitbox spawn timing is independent of this (driven entirely by AnimEvent_SpawnHitbox), so this only controls how long the animation plays.")]
    public float animBoolHoldDuration = 1.5f;

    [Header("Sprite Tint — disambiguates Combo's slashes from Basic Swing's identical-looking ones")]
    [Tooltip("Auto-found via GetComponentInChildren if left empty.")]
    public SpriteRenderer spriteRenderer;
    [Tooltip("Shown for the whole Combo chain — 2 slashes into an unblockable kick — so the player can tell it apart from Basic Swing before the kick lands.")]
    public Color comboTintColor = Color.red;
    [Tooltip("Corrects a pixel-art offset in the Slash clip that sits too high — shifts the sprite (not the boss's actual transform, so hitbox spawn points and collision are unaffected) down by this much for the duration of every forward-slash swing. Negative = down.")]
    public float forwardSlashVisualYOffset = -0.3f;

    [Header("Basic Swing — 3 Slashes")]
    public GameObject slashHitboxPrefab;
    public int basicChainSlashCount = 3;
    public float slashDamage = 6f;
    public float slashStun = 0.3f;
    public float slashKnockback = 2f;
    public float slashSpawnDistance = 1.2f;
    [Tooltip("Pause between each of the 3 slashes.")]
    public float slashRecovery = 0.2f;
    [Tooltip("Pause after the chain finishes cleanly (none of the 3 slashes connected, or the last one was blocked/parried) — long enough for the player to get a light attack in.")]
    public float chainEndLag = 0.7f;

    [Header("Basic Swing — Follow-up Down Slash (fires only if the 3rd slash connects); also used by the standalone Overhead Attack")]
    public GameObject downSlashHitboxPrefab;
    public float downSlashDamage = 12f;
    public float downSlashStun = 0.5f;
    public float downSlashKnockback = 1f;
    public float overheadAttackEndLag = 0.7f;

    [Header("Combo — 2 Slashes + Unblockable Kick")]
    public int comboSlashCount = 2;
    [Tooltip("Tagged Unblockable — the only way to avoid it is to be airborne when it's active. This only controls WHERE it spawns; whether a jump actually clears it also depends on this prefab's own Collider2D being sized/positioned to match comboKickHeightOffset. No dedicated kick animation exists, so this one still spawns on a plain hardcoded timer rather than an Animation Event.")]
    public GameObject comboKickHitboxPrefab;
    public float comboKickWindup = 0.3f;
    public float comboKickActiveDuration = 0.2f;
    public float comboKickDamage = 8f;
    public float comboKickStun = 0.5f;
    [Tooltip("Vertical offset (from OriginPos) the kick spawns at — negative = lower/closer to the ground.")]
    public float comboKickHeightOffset = -0.6f;
    public float comboKickSpawnDistance = 1.2f;
    public float comboEndLag = 0.7f;

    [Header("Thrust Attack — no longer part of the random rotation; only fires as a counter once the trigger below is met")]
    public GameObject thrustHitboxPrefab;
    public float thrustDamage = 10f;
    public float thrustStun = 0.4f;
    public float thrustKnockback = 3f;
    public float thrustSpawnDistance = 1.3f;
    public float thrustEndLag = 0.6f;
    [Tooltip("How many hits landing on the boss in a row (each within comboHitGapWindow of the last) trigger the Thrust counter. Not tied to a specific move — approximates 'the player's 4-hit combo' by counting any hits close enough together in time, since HitInfo doesn't carry which specific attack landed.")]
    public int thrustTriggerHitCount = 4;
    [Tooltip("Max gap between consecutive hits for them to still count toward the same combo — a gap longer than this resets the count to 0. Should be a bit more than the player's own combo window so a real combo isn't miscounted as broken.")]
    public float comboHitGapWindow = 0.6f;
    [Tooltip("How far the boss backs away from the player after landing/whiffing a Thrust.")]
    public float thrustRetreatDistance = 1.5f;
    [Tooltip("How long that backing-away movement takes.")]
    public float thrustRetreatDuration = 0.3f;

    private Rigidbody2D rb;
    private CombatTarget selfTarget;
    private CombatTarget playerTarget;
    private Color normalSpriteColor;
    private Vector3 normalSpriteLocalPosition;
    private bool chainRunning;

    // Staged by PlayAnimatedSwing right before setting the bool true, so
    // AnimEvent_SpawnHitbox can spawn the right thing when the clip fires it —
    // Animation Events can't carry rich data, so this is how the pending swing's
    // combat values reach the event callback.
    private GameObject pendingHitboxPrefab;
    private AttackDirection pendingDirection;
    private float pendingDamage;
    private float pendingStun;
    private float pendingKnockback;
    private float pendingSpawnDistance;
    private float pendingHeightOffset;
    private System.Action<CombatTarget> pendingOnConnect;
    private bool pendingHitboxSpawned;

    // Tracks consecutive hits landing on the boss, for the Thrust counter-trigger.
    private int consecutiveHitsOnBoss;
    private float lastHitOnBossTime = -999f;
    private bool thrustPending;

    // True only during MainLoop's "approach the player" phase, before it commits to
    // an attack chain — FixedUpdate reads this to decide whether to actually move.
    private bool isChasing;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        selfTarget = GetComponent<CombatTarget>();
        if (player != null) playerTarget = player.GetComponent<CombatTarget>();

        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            normalSpriteColor = spriteRenderer.color;
            normalSpriteLocalPosition = spriteRenderer.transform.localPosition;
        }
    }

    private void OnEnable()
    {
        selfTarget.OnDeath += HandleDeath;
        selfTarget.OnHit += HandleHitOnBoss;
    }

    private void OnDisable()
    {
        selfTarget.OnDeath -= HandleDeath;
        selfTarget.OnHit -= HandleHitOnBoss;
        StopAllCoroutines();
        chainRunning = false;
        isChasing = false;
    }

    private void Start()
    {
        StartCoroutine(MainLoop());
    }

    private void Update()
    {
        // Face the player whenever able to act — purely visual. Doesn't run mid-attack
        // so a chain's facing can't get yanked out from under it partway through.
        if (player == null || selfTarget.CurrentState != CombatTarget.State.Normal || chainRunning) return;

        Vector3 scale = transform.localScale;
        scale.x = Mathf.Abs(scale.x) * FacingSign;
        transform.localScale = scale;
    }

    private void FixedUpdate()
    {
        if (rb == null) return;

        // Only actually moves under script control while isChasing is explicitly set
        // (during MainLoop's approach phase) — everything else (attacking, retreating
        // after Thrust, being locked down) either doesn't touch velocity at all or
        // drives position directly via rb.MovePosition, so this only needs to zero
        // out horizontal drift the rest of the time.
        if (!isChasing)
        {
            rb.velocity = new Vector2(0f, rb.velocity.y);
            return;
        }

        rb.velocity = new Vector2(FacingSign * moveSpeed, rb.velocity.y);
    }

    private float FacingSign => player != null ? Mathf.Sign(player.position.x - transform.position.x) : 1f;
    private Vector2 FacingDir => new Vector2(FacingSign, 0f);
    private Vector2 OriginPos => hitOrigin != null ? (Vector2)hitOrigin.position : (Vector2)transform.position;

    private IEnumerator MainLoop()
    {
        while (true)
        {
            while (selfTarget.CurrentState != CombatTarget.State.Normal)
            {
                yield return null;
            }

            // Always start from a clean state before picking the next attack — safety
            // net for the Combo tint, the forward-slash visual offset, and all three
            // animator bools, in case any were force-stopped (below) before their own
            // cleanup ran. A bool left stuck true from an interrupted swing would keep
            // the Animator parked in that state indefinitely.
            if (spriteRenderer != null)
            {
                spriteRenderer.color = normalSpriteColor;
                spriteRenderer.transform.localPosition = normalSpriteLocalPosition;
            }
            SetAnimBool(forwardSlashAnimTrigger, false);
            SetAnimBool(overheadAnimTrigger, false);
            SetAnimBool(thrustAnimTrigger, false);

            // Walk toward the player until close enough to attack. Interrupted the
            // same way everything else is — if the boss gets hit while approaching,
            // this loop exits (on the state check) and the top of MainLoop waits it
            // out before trying again.
            if (player != null)
            {
                isChasing = true;
                while (Mathf.Abs(player.position.x - transform.position.x) > attackRange
                    && selfTarget.CurrentState == CombatTarget.State.Normal)
                {
                    yield return null;
                }
                isChasing = false;
            }

            if (selfTarget.CurrentState != CombatTarget.State.Normal)
            {
                continue; // got hit while approaching — loop back to the top wait
            }

            CheckPhaseTransition();

            chainRunning = true;
            Coroutine chain = StartCoroutine(RunChainAndClearFlag(ChooseNextChain()));

            while (chainRunning)
            {
                if (selfTarget.CurrentState != CombatTarget.State.Normal)
                {
                    StopCoroutine(chain);
                    chainRunning = false;
                    break;
                }
                yield return null;
            }
        }
    }

    private IEnumerator RunChainAndClearFlag(IEnumerator chain)
    {
        yield return StartCoroutine(chain);
        chainRunning = false;
    }

    private void CheckPhaseTransition()
    {
        // TODO: half-HP run-away/platforming trigger, and the HP<=Half switch to
        // Phase2, both go here — not being built yet per the brief.
    }

    private IEnumerator ChooseNextChain()
    {
        // Thrust is no longer part of the random pool — it only fires as a counter
        // once thrustTriggerHitCount consecutive hits land on the boss (see
        // HandleHitOnBoss). Takes priority over the normal random pick when pending.
        if (thrustPending)
        {
            thrustPending = false;
            yield return StartCoroutine(RunThrustAttack());
            yield break;
        }

        // TODO: Phase2 picks from a different pool once it exists. Roughly even split
        // across the remaining three — a natural spot for weighting or
        // no-repeat-twice logic later.
        float roll = Random.value;

        if (roll < 0.4f)
        {
            yield return StartCoroutine(RunBasicAttackChain());
        }
        else if (roll < 0.65f)
        {
            yield return StartCoroutine(RunOverheadAttack());
        }
        else
        {
            yield return StartCoroutine(RunComboChain());
        }
    }

    private void SetAnimBool(string paramName, bool value)
    {
        if (animator == null || string.IsNullOrEmpty(paramName)) return;
        animator.SetBool(paramName, value);
    }

    /// <summary>
    /// Sets the given Animator bool true, waits animBoolHoldDuration, then sets it
    /// back false. The hitbox spawns ONLY via AnimEvent_SpawnHitbox being called from
    /// that clip — if the clip doesn't have that event wired up, this attack plays
    /// its animation but never deals damage. No fallback timer.
    /// </summary>
    private IEnumerator PlayAnimatedSwing(string boolParam, GameObject hitboxPrefab, AttackDirection direction,
        float damage, float stun, float knockbackForce, float spawnDistance, System.Action<CombatTarget> onConnect,
        float heightOffset = 0f)
    {
        pendingHitboxPrefab = hitboxPrefab;
        pendingDirection = direction;
        pendingDamage = damage;
        pendingStun = stun;
        pendingKnockback = knockbackForce;
        pendingSpawnDistance = spawnDistance;
        pendingHeightOffset = heightOffset;
        pendingOnConnect = onConnect;
        pendingHitboxSpawned = false;

        bool isForwardSlash = boolParam == forwardSlashAnimTrigger;

        SetAnimBool(boolParam, true);
        if (isForwardSlash && spriteRenderer != null)
        {
            spriteRenderer.transform.localPosition = normalSpriteLocalPosition + new Vector3(0f, forwardSlashVisualYOffset, 0f);
        }

        yield return new WaitForSeconds(animBoolHoldDuration);

        SetAnimBool(boolParam, false);
        if (isForwardSlash && spriteRenderer != null)
        {
            spriteRenderer.transform.localPosition = normalSpriteLocalPosition;
        }
    }

    /// <summary>
    /// Add as an Animation Event on each Slash/Down/Thrust clip, at the frame the
    /// swing should actually connect (via BossAnimationEventRelay if the Animator
    /// lives on a child GameObject rather than this one). This is the ONLY thing that
    /// spawns the hitbox for an animated attack — there's no timer fallback.
    /// </summary>
    public void AnimEvent_SpawnHitbox()
    {
        if (pendingHitboxSpawned) return; // stray double-call guard
        pendingHitboxSpawned = true;

        if (pendingHitboxPrefab == null)
        {
            Debug.Log("[BossAI] AnimEvent_SpawnHitbox fired but no hitbox prefab is staged for this swing.");
            return;
        }

        Vector2 spawnPos = OriginPos + Vector2.up * pendingHeightOffset + FacingDir * pendingSpawnDistance;
        float angle = FacingSign >= 0f ? 0f : 180f;
        GameObject obj = Instantiate(pendingHitboxPrefab, spawnPos, Quaternion.Euler(0f, 0f, angle));

        MeleeHitbox hitbox = obj.GetComponent<MeleeHitbox>();
        if (hitbox != null)
        {
            hitbox.Initialize(playerLayer, pendingDamage, pendingStun, pendingKnockback, gameObject, FacingDir,
                pendingOnConnect, causesKnockdown: false, attackDirection: pendingDirection);
        }
        else
        {
            Debug.Log("[BossAI] hitbox prefab " + pendingHitboxPrefab.name + " has no MeleeHitbox component");
        }
    }

    // ---------------- Basic Swing (3 slashes) ----------------

    private IEnumerator RunBasicAttackChain()
    {
        bool lastSlashConnected = false;

        for (int i = 0; i < basicChainSlashCount; i++)
        {
            lastSlashConnected = false;
            // Tagged Overhead for blocking purposes even though this is the forward
            // slash animation — swapped per request: side-looking attacks block as
            // Overhead, and vice versa.
            yield return StartCoroutine(PlayAnimatedSwing(forwardSlashAnimTrigger, slashHitboxPrefab,
                AttackDirection.Overhead, slashDamage, slashStun, slashKnockback, slashSpawnDistance,
                target => lastSlashConnected = true));

            bool isLastSlash = i == basicChainSlashCount - 1;
            if (!isLastSlash)
            {
                yield return new WaitForSeconds(slashRecovery);
            }
        }

        if (lastSlashConnected)
        {
            // Immediately follow with a down slash instead of the normal end-of-chain
            // lag — naturally dodgeable if the player dashes out of their stun in
            // time, since the hitbox only covers where they were standing.
            // Tagged Side for blocking purposes — swapped, same as above.
            yield return StartCoroutine(PlayAnimatedSwing(overheadAnimTrigger, downSlashHitboxPrefab,
                AttackDirection.Side, downSlashDamage, downSlashStun, downSlashKnockback, slashSpawnDistance, null));
        }

        yield return new WaitForSeconds(chainEndLag);
    }

    // ---------------- Overhead Attack (standalone) ----------------

    private IEnumerator RunOverheadAttack()
    {
        // Tagged Side for blocking purposes (swapped) even though the animation is Down.
        yield return StartCoroutine(PlayAnimatedSwing(overheadAnimTrigger, downSlashHitboxPrefab,
            AttackDirection.Side, downSlashDamage, downSlashStun, downSlashKnockback, slashSpawnDistance, null));

        yield return new WaitForSeconds(overheadAttackEndLag);
    }

    // ---------------- Combo (2 slashes + unblockable kick) ----------------

    private IEnumerator RunComboChain()
    {
        if (spriteRenderer != null) spriteRenderer.color = comboTintColor;

        for (int i = 0; i < comboSlashCount; i++)
        {
            // Tagged Overhead for blocking purposes (swapped, same as Basic Swing's slashes).
            yield return StartCoroutine(PlayAnimatedSwing(forwardSlashAnimTrigger, slashHitboxPrefab,
                AttackDirection.Overhead, slashDamage, slashStun, slashKnockback, slashSpawnDistance, null));
            yield return new WaitForSeconds(slashRecovery);
        }

        // No dedicated kick animation exists — plain hardcoded timer, no bool/event.
        // Unblockable isn't part of the Side/Overhead swap.
        yield return StartCoroutine(SwingSlashTimer(comboKickHitboxPrefab, AttackDirection.Unblockable,
            comboKickDamage, comboKickStun, 0f, comboKickSpawnDistance, comboKickWindup, comboKickActiveDuration,
            null, heightOffset: comboKickHeightOffset));

        if (spriteRenderer != null) spriteRenderer.color = normalSpriteColor;

        yield return new WaitForSeconds(comboEndLag);
    }

    // ---------------- Thrust Attack ----------------

    private IEnumerator RunThrustAttack()
    {
        // Tagged Overhead for blocking purposes (swapped, same as the other forward-facing swings).
        yield return StartCoroutine(PlayAnimatedSwing(thrustAnimTrigger, thrustHitboxPrefab,
            AttackDirection.Overhead, thrustDamage, thrustStun, thrustKnockback, thrustSpawnDistance, null));

        yield return new WaitForSeconds(thrustEndLag);

        yield return StartCoroutine(RetreatFromPlayer(thrustRetreatDistance, thrustRetreatDuration));
    }

    /// <summary>
    /// Moves the boss directly away from the player over the given distance/duration,
    /// via rb.MovePosition rather than velocity — same pattern PlayerCombat uses for
    /// its own Dash. Currently only used after Thrust.
    /// </summary>
    private IEnumerator RetreatFromPlayer(float distance, float duration)
    {
        if (rb == null) yield break;

        Vector2 awayDir = new Vector2(-FacingSign, 0f);
        Vector2 start = rb.position;
        Vector2 end = start + awayDir * distance;
        float t = 0f;
        while (t < duration)
        {
            t += Time.fixedDeltaTime;
            rb.MovePosition(Vector2.Lerp(start, end, t / duration));
            yield return new WaitForFixedUpdate();
        }
    }

    /// <summary>
    /// Plain windup-then-spawn-then-wait timer, no Animator bool involved — only used
    /// by the Combo Chain's kick, since it has no animation clip to hang an event off.
    /// </summary>
    private IEnumerator SwingSlashTimer(GameObject hitboxPrefab, AttackDirection direction, float damage, float stun,
        float knockbackForce, float spawnDistance, float windup, float activeDuration,
        System.Action<CombatTarget> onConnect, float heightOffset = 0f)
    {
        yield return new WaitForSeconds(windup);

        if (hitboxPrefab != null)
        {
            Vector2 spawnPos = OriginPos + Vector2.up * heightOffset + FacingDir * spawnDistance;
            float angle = FacingSign >= 0f ? 0f : 180f;
            GameObject obj = Instantiate(hitboxPrefab, spawnPos, Quaternion.Euler(0f, 0f, angle));

            MeleeHitbox hitbox = obj.GetComponent<MeleeHitbox>();
            if (hitbox != null)
            {
                hitbox.Initialize(playerLayer, damage, stun, knockbackForce, gameObject, FacingDir,
                    onConnect, causesKnockdown: false, attackDirection: direction);
            }
            else
            {
                Debug.Log("[BossAI] hitbox prefab " + hitboxPrefab.name + " has no MeleeHitbox component");
            }
        }
        else
        {
            Debug.Log("[BossAI] a hitbox prefab isn't assigned in the Inspector — this swing will spawn nothing");
        }

        yield return new WaitForSeconds(activeDuration);
    }

    private void HandleDeath()
    {
        Debug.Log("[BossAI] defeated");
        StopAllCoroutines();
        chainRunning = false;
        // TODO: whatever the actual defeat sequence should be — not specified yet.
    }

    /// <summary>
    /// Tracks consecutive hits landing on the boss to trigger the Thrust counter.
    /// A gap longer than comboHitGapWindow since the last hit resets the count —
    /// otherwise it climbs, and hitting thrustTriggerHitCount flags thrustPending for
    /// ChooseNextChain to consume on the boss's next turn. Doesn't distinguish which
    /// move landed (HitInfo doesn't carry that), so this approximates "the player's
    /// 4-hit combo" as any 4 hits close enough together in time — see the field
    /// tooltips above for the reasoning.
    /// </summary>
    private void HandleHitOnBoss(HitInfo hit)
    {
        if (Time.time - lastHitOnBossTime > comboHitGapWindow)
        {
            consecutiveHitsOnBoss = 0;
        }
        lastHitOnBossTime = Time.time;
        consecutiveHitsOnBoss++;

        if (consecutiveHitsOnBoss >= thrustTriggerHitCount)
        {
            thrustPending = true;
            consecutiveHitsOnBoss = 0;
        }
    }
}