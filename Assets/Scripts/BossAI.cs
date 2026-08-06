using System.Collections;
using UnityEngine;

/// <summary>
/// Boss controller, built around one main loop that:
///   1. Waits out any lockdown (Stunned/Knockdown/Grabbed) from getting hit.
///   2. Checks for a phase transition (STUB — see CheckPhaseTransition).
///   3. Picks and runs the next attack chain (STUB selection — see ChooseNextChain).
///
/// Architecture note — interruption handling lives in the MAIN LOOP, not in each
/// chain. If the boss gets hit mid-attack, the loop force-stops whatever chain
/// coroutine is running and waits for CombatTarget.State.Normal before resuming.
/// That means RunBasicAttackChain (and RunComboChain / RunHeavyAttackChain once
/// they're filled in) can just be written as a plain top-to-bottom sequence of
/// yield-returns — no manual "am I still allowed to keep going" checks scattered
/// through them. New chains should follow that same shape.
///
/// Phases: currentPhase only ever sits at Phase1 right now. CheckPhaseTransition is
/// where the half-HP run-away/platforming trigger and the HP<=Half switch to Phase2
/// (blue, faster versions of every attack, per the design doc) both plug in later —
/// intentionally left as a stub rather than guessed at, since neither was meant to
/// be built yet.
///
/// Attacks are tagged with AttackDirection (Side / Overhead / Unblockable) so they
/// participate in the player's directional block/parry system via
/// CombatTarget.ApplyHit — see MeleeHitbox.Initialize's attackDirection param.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CombatTarget))]
public class BossAI : MonoBehaviour
{
    public enum Phase { Phase1, Phase2 } // Half-HP run-away is a separate transition, not a Phase value — see CheckPhaseTransition.

    [Header("Setup")]
    public Transform player;
    public LayerMask playerLayer;
    [Tooltip("Empty child positioned at chest height. Hitboxes spawn relative to here, same convention as PlayerCombat.")]
    public Transform hitOrigin;

    [Header("Phase (Phase2 / half-HP behavior NOT implemented yet — see CheckPhaseTransition)")]
    public Phase currentPhase = Phase.Phase1;
    [Range(0f, 1f)] public float halfHpThreshold = 0.5f;

    [Header("Attack Telegraph — shown before choosing Triple Swing vs Overhead Attack")]
    [Tooltip("Distinct visual for 'a triple swing (Side) is coming' — make this clearly different from the overhead indicator so the player can tell them apart at a glance.")]
    public GameObject tripleSwingIndicatorPrefab;
    [Tooltip("Distinct visual for 'an overhead attack (Overhead) is coming'.")]
    public GameObject overheadIndicatorPrefab;
    [Tooltip("How long the indicator shows before the chosen attack actually begins — this is the player's real reaction window for picking a block direction.")]
    public float telegraphDuration = 0.6f;
    [Tooltip("Where the indicator appears, relative to OriginPos.")]
    public Vector2 indicatorOffset = new Vector2(0f, 2f);

    [Header("Basic Attack Chain — 3 Slashes")]
    public GameObject slashHitboxPrefab;
    public int basicChainSlashCount = 3;
    public float slashWindup = 0.25f;
    public float slashActiveDuration = 0.15f;
    public float slashRecovery = 0.2f;
    public float slashDamage = 6f;
    public float slashStun = 0.3f;
    public float slashKnockback = 2f;
    public float slashSpawnDistance = 1.2f;
    [Tooltip("Pause after the chain finishes cleanly (none of the 3 slashes connected, or the last one was blocked/parried) — deliberately long enough for the player to get a light attack in.")]
    public float chainEndLag = 0.7f;

    [Header("Standalone Overhead Attack (the OTHER telegraphed option, distinct from the Basic Chain's hit-confirm follow-up below)")]
    [Tooltip("Same swing as the Basic Chain's down-slash follow-up, reused here as its own selectable opening move — see downSlashHitboxPrefab/downSlashDamage/etc. below.")]
    public float overheadAttackEndLag = 0.7f;

    [Header("Basic Attack Chain — Follow-up Down Slash (fires if the 3rd slash connects; also reused by the standalone Overhead Attack above)")]
    public GameObject downSlashHitboxPrefab;
    public float downSlashWindup = 0.15f;
    public float downSlashDamage = 12f;
    public float downSlashStun = 0.5f;
    public float downSlashKnockback = 1f;
    // No explicit "did the player dash away" check — the down slash's hitbox only
    // covers where they were standing when stunned, so dashing out in time makes it
    // whiff naturally, same as any other hitbox-vs-moved-target interaction.

    [Header("Basic Attack Chain — Counter-Thrust (NOT IMPLEMENTED — see notes below)")]
    // TODO from the design doc, only above half HP:
    //   "After getting hit by the player's 4-hit light attack while [the boss is] in
    //   block mode, immediately counter with a thrust, leaving no time for the player
    //   to attack back, forcing them to dodge or block. After the thrust there's a lag
    //   timer — long enough for the player to follow up with a dash + 2 heavy attacks
    //   if they blocked it. If not comboed within the stun duration, the boss backs off
    //   and prepares the next sequence."
    // This depends on a boss "block mode" concept that doesn't exist yet — is it a
    // state the boss enters as part of its own pattern (parryable/punishable by the
    // player), a passive stance, something else? That's a real design decision, not
    // a detail to guess at, so it's left out rather than implemented on an assumption
    // that might not match what's wanted. Once "block mode" is defined, this slots in
    // as a branch inside RunBasicAttackChain, detected via CombatTarget.OnHit and a
    // combo-hit counter on the player's side.

    [Header("Combo Chain — 1-2 Slash Opener + Unblockable Kick")]
    [Tooltip("Reuses slashHitboxPrefab/slashDamage/slashStun/slashKnockback from the Basic Chain fields above for the opener — only the count differs.")]
    public int comboMinSlashes = 1;
    public int comboMaxSlashes = 2; // inclusive
    [Tooltip("The kick itself is tagged Unblockable — the only way to avoid it is to be airborne when it's active. This only controls WHERE it spawns; whether a jump actually clears it depends on this prefab's own Collider2D being sized/positioned to match comboKickHeightOffset.")]
    public GameObject comboKickHitboxPrefab;
    public float comboKickWindup = 0.3f;
    public float comboKickActiveDuration = 0.2f;
    public float comboKickDamage = 8f;
    [Tooltip("Vertical offset (from OriginPos) the kick spawns at — negative = lower/closer to the ground.")]
    public float comboKickHeightOffset = -0.6f;
    public float comboKickSpawnDistance = 1.2f;

    [Header("Combo Chain — If the Player Jumps the Kick (chain ends early)")]
    public GameObject comboWhiffOverheadHitboxPrefab;
    public float comboWhiffOverheadWindup = 0.3f;
    public float comboWhiffOverheadDamage = 14f;
    public float comboWhiffOverheadStun = 1.2f;
    [Tooltip("Deliberately much longer than chainEndLag — dodging the kick is supposed to earn a big window if this whiffs, or a completely free punish window if it's blocked.")]
    public float comboWhiffHugeLag = 1.5f;

    [Header("Combo Chain — If the Kick Connects (knockdown + throw, then chains into Basic)")]
    [Tooltip("Player is teleported this far in the boss's facing direction (thrown to the opposite side) and knocked down there. No separate damage here — the kick's own comboKickDamage already applied on connect.")]
    public float comboThrowDistance = 3f;
    public float comboThrowKnockdownDuration = 0.8f;

    [Header("Combo Chain — Punish if the Basic-Chain Follow-up Isn't Blocked")]
    public GameObject comboPunishSlashHitboxPrefab;
    public float comboPunishWindup = 0.3f;
    public float comboPunishDamage = 20f;
    public float comboPunishStun = 1.5f;

    [Header("Heavy Attack Chain — Charge + High/Low Telegraph")]
    public float heavyChargeDuration = 0.8f;
    [Tooltip("Optional generic 'charging up' visual, shown for heavyChargeDuration before the high/low choice is even revealed.")]
    public GameObject heavyChargeIndicatorPrefab;
    public GameObject heavyHighIndicatorPrefab;
    public GameObject heavyLowIndicatorPrefab;
    [Tooltip("How long the high/low tell shows before the attack actually swings — the player's real reaction window for picking a block.")]
    public float heavyTelegraphDuration = 0.7f;

    [Header("Heavy Attack Chain — High Swing (Overhead-blockable)")]
    [Tooltip("Doc also describes 'moving backwards' as part of dodging the high swing, alongside blocking Overhead. Not modeled separately — only the direction-match block is implemented here. If you want a distance requirement on top of that (e.g. only fully safe if the player retreated far enough too), that's an addition to this branch specifically, not a small tweak.")]
    public GameObject heavyHighHitboxPrefab;
    public float heavyHighWindup = 0.2f;
    public float heavyHighActiveDuration = 0.2f;
    public float heavyHighDamage = 15f;
    public float heavyHighStun = 0.8f;

    [Header("Heavy Attack Chain — Low Sweep (Side-blockable only, no dodge option)")]
    public GameObject heavyLowHitboxPrefab;
    public float heavyLowWindup = 0.2f;
    public float heavyLowActiveDuration = 0.2f;
    public float heavyLowDamage = 15f;
    public float heavyLowStun = 0.8f;

    [Header("Heavy Attack Chain — Grab-Throw Punish (on hit, bypasses block entirely)")]
    public float heavyThrowDistance = 2f;
    public float heavyThrowDamage = 18f;
    public float heavyThrowKnockdownDuration = 1f;

    [Header("Shared — Retreat & Chain-Into-Basic Reaction Pause")]
    [Tooltip("Used whenever the boss backs off after a chain resolves (Combo Chain's blocked-punish case, Heavy Attack Chain isn't currently using this but can).")]
    public float retreatDistance = 3f;
    public float retreatDuration = 0.4f;
    [Tooltip("Pause before Combo Chain / Heavy Attack Chain immediately follow up with Basic Attack Chain — the doc calls this out explicitly ('not before the player has time to react') rather than a truly instant chain.")]
    public float chainIntoBasicReactionPause = 0.5f;

    private Rigidbody2D rb;
    private CombatTarget selfTarget;
    private CombatTarget playerTarget;

    private bool chainRunning;
    [Tooltip("Set by SwingSlash's onConnect callbacks — 'did the most recent attack sequence actually land a hit on the player'. Read by chains that branch on that (Combo Chain's post-throw punish, Heavy Attack Chain's grab-throw).")]
    private bool lastSequenceHitPlayer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        selfTarget = GetComponent<CombatTarget>();
        if (player != null) playerTarget = player.GetComponent<CombatTarget>();
    }

    private void OnEnable()
    {
        selfTarget.OnDeath += HandleDeath;
    }

    private void OnDisable()
    {
        selfTarget.OnDeath -= HandleDeath;
        StopAllCoroutines();
        chainRunning = false;
    }

    private void Start()
    {
        StartCoroutine(MainLoop());
    }

    private void Update()
    {
        // Face the player whenever able to act — purely visual, matches EnemyAI's
        // flip-by-scale convention. Doesn't run mid-attack so a chain can't have its
        // facing yanked out from under it partway through a swing.
        if (player == null || selfTarget.CurrentState != CombatTarget.State.Normal || chainRunning) return;

        Vector3 scale = transform.localScale;
        scale.x = Mathf.Abs(scale.x) * FacingSign;
        transform.localScale = scale;
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

            CheckPhaseTransition();

            chainRunning = true;
            Coroutine chain = StartCoroutine(RunChainAndClearFlag(ChooseNextChain()));

            // Monitor every frame for the whole chain's duration. If the boss gets
            // locked down mid-attack (the player landed a hit), force-stop the chain
            // immediately rather than letting it keep running while stunned.
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
        // Phase2 (5-slash blue Basic Chain, blue ranged Heavy/Combo variants) both go
        // here. Deliberately a no-op stub — per the brief, not being built yet, but
        // this is the one place a future phase check needs to slot into, rather than
        // being threaded through the main loop or each chain individually.
    }

    private IEnumerator ChooseNextChain()
    {
        // TODO: Phase2 should pick from the blue-attack pool instead of this one.
        // Flat random split for now (40% telegraphed Basic/Overhead pair, 30% Combo,
        // 30% Heavy) — a natural spot for weighting or no-repeat-twice logic later.
        //
        // Combo/Heavy don't go through the generic ShowTelegraph — that was scoped
        // specifically to "the triple swing or the overhead attack" when it was asked
        // for. They have their own built-in tells instead (Heavy's charge + high/low
        // indicator; Combo's 1-2 slash opener is its own natural read, same as how
        // Basic Attack Chain's opening slashes aren't telegraphed either).
        float roll = Random.value;

        if (roll < 0.4f)
        {
            bool doOverhead = Random.value < 0.5f;
            yield return StartCoroutine(ShowTelegraph(doOverhead));
            yield return StartCoroutine(doOverhead ? RunOverheadAttack() : RunBasicAttackChain());
        }
        else if (roll < 0.7f)
        {
            yield return StartCoroutine(RunComboChain());
        }
        else
        {
            yield return StartCoroutine(RunHeavyAttackChain());
        }
    }

    private IEnumerator ShowTelegraph(bool isOverhead)
    {
        GameObject prefab = isOverhead ? overheadIndicatorPrefab : tripleSwingIndicatorPrefab;
        if (prefab == null)
        {
            Debug.Log("[BossAI] " + (isOverhead ? "overheadIndicatorPrefab" : "tripleSwingIndicatorPrefab")
                + " isn't assigned — no visual telegraph, but still pausing for telegraphDuration so the timing stays consistent.");
            yield return new WaitForSeconds(telegraphDuration);
            yield break;
        }

        GameObject indicator = Instantiate(prefab, OriginPos + indicatorOffset, Quaternion.identity, transform);
        yield return new WaitForSeconds(telegraphDuration);
        if (indicator != null) Destroy(indicator);
    }

    private IEnumerator RunOverheadAttack()
    {
        yield return new WaitForSeconds(downSlashWindup);
        yield return StartCoroutine(SwingSlash(downSlashHitboxPrefab, AttackDirection.Overhead, downSlashDamage,
            downSlashStun, downSlashKnockback, slashSpawnDistance, slashActiveDuration, null));

        yield return new WaitForSeconds(overheadAttackEndLag);
    }

    // ---------------- Basic Attack Chain ----------------

    private IEnumerator RunBasicAttackChain()
    {
        bool lastSlashConnected = false;
        lastSequenceHitPlayer = false;

        for (int i = 0; i < basicChainSlashCount; i++)
        {
            yield return new WaitForSeconds(slashWindup);

            lastSlashConnected = false;
            yield return StartCoroutine(SwingSlash(slashHitboxPrefab, AttackDirection.Side, slashDamage, slashStun,
                slashKnockback, slashSpawnDistance, slashActiveDuration, target =>
                {
                    lastSlashConnected = true;
                    lastSequenceHitPlayer = true;
                }));

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
            yield return new WaitForSeconds(downSlashWindup);
            yield return StartCoroutine(SwingSlash(downSlashHitboxPrefab, AttackDirection.Overhead, downSlashDamage,
                downSlashStun, downSlashKnockback, slashSpawnDistance, slashActiveDuration,
                target => lastSequenceHitPlayer = true));
        }

        // "Slight lag time (enough for the player to light attack if they come out of
        // block)" — the pause itself is the window; nothing else needed here.
        yield return new WaitForSeconds(chainEndLag);

        // See the "Counter-Thrust" TODO above the header fields — this is where that
        // branch would sit, conditioned on being above halfHpThreshold and on however
        // "boss block mode" ends up being defined.
    }

    /// <summary>
    /// Spawns a MeleeHitbox in front of the boss, tagged with the given
    /// AttackDirection so the player's block/parry system can check it. Same
    /// hitbox-spawn pattern PlayerCombat uses for Kick/Slam/Boot.
    ///
    /// heightOffset shifts the spawn point up/down from OriginPos before applying the
    /// forward offset — used for the Combo Chain's low kick. Note this only controls
    /// WHERE the hitbox spawns, not how tall its collider is; "a jump clears it" also
    /// depends on the prefab's own Collider2D being sized/positioned low to match.
    /// </summary>
    private IEnumerator SwingSlash(GameObject hitboxPrefab, AttackDirection direction, float damage, float stun,
        float knockbackForce, float spawnDistance, float activeDuration, System.Action<CombatTarget> onConnect,
        float heightOffset = 0f)
    {
        if (hitboxPrefab == null)
        {
            Debug.Log("[BossAI] a hitbox prefab isn't assigned in the Inspector — this swing will spawn nothing");
            yield return new WaitForSeconds(activeDuration);
            yield break;
        }

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

        yield return new WaitForSeconds(activeDuration);
    }

    // ---------------- Combo Chain ----------------

    private IEnumerator RunComboChain()
    {
        // 1-2 slash opener, reusing the Basic Chain's own slash hitbox/damage/stun.
        int slashCount = Random.Range(comboMinSlashes, comboMaxSlashes + 1);
        for (int i = 0; i < slashCount; i++)
        {
            yield return new WaitForSeconds(slashWindup);
            yield return StartCoroutine(SwingSlash(slashHitboxPrefab, AttackDirection.Side, slashDamage, slashStun,
                slashKnockback, slashSpawnDistance, slashActiveDuration, target => lastSequenceHitPlayer = true));
            yield return new WaitForSeconds(slashRecovery);
        }

        // Unblockable kick, low to the ground — see comboKickHeightOffset's tooltip
        // for what "jumping over it" actually depends on.
        yield return new WaitForSeconds(comboKickWindup);

        bool kickConnected = false;
        yield return StartCoroutine(SwingSlash(comboKickHitboxPrefab, AttackDirection.Unblockable, comboKickDamage,
            0f, 0f, comboKickSpawnDistance, comboKickActiveDuration, target => kickConnected = true,
            heightOffset: comboKickHeightOffset));

        if (!kickConnected)
        {
            // Player jumped over it — chain ends early, big-lag punish swing. If the
            // player blocks THIS (Overhead), the huge lag is entirely their free
            // window, per the doc ("if blocked player can follow up with any combo
            // comfortably") — no special-casing needed, comboWhiffHugeLag just always
            // runs regardless of whether the swing connects or gets blocked.
            yield return new WaitForSeconds(comboWhiffOverheadWindup);
            yield return StartCoroutine(SwingSlash(comboWhiffOverheadHitboxPrefab, AttackDirection.Overhead,
                comboWhiffOverheadDamage, comboWhiffOverheadStun, 0f, slashSpawnDistance, slashActiveDuration, null));

            yield return new WaitForSeconds(comboWhiffHugeLag);
            yield break;
        }

        // Kick connected — pick the player up and throw them to the opposite side,
        // then knock them down there. Unblockable bypasses the block/parry check
        // entirely, same as any grab; damage is 0 here since the kick itself already
        // dealt comboKickDamage on connect — this call is purely the knockdown.
        if (playerTarget != null && player != null)
        {
            Vector2 throwPos = (Vector2)transform.position + FacingDir * comboThrowDistance;
            player.position = throwPos;
            playerTarget.ApplyHit(new HitInfo(0f, comboThrowKnockdownDuration, Vector2.zero, gameObject,
                causesKnockdown: true), AttackDirection.Unblockable);
        }

        yield return new WaitForSeconds(chainIntoBasicReactionPause);

        lastSequenceHitPlayer = false;
        yield return StartCoroutine(RunBasicAttackChain());

        if (lastSequenceHitPlayer)
        {
            // Player didn't block the follow-up — unavoidable heavy punish, then retreat.
            yield return new WaitForSeconds(comboPunishWindup);
            yield return StartCoroutine(SwingSlash(comboPunishSlashHitboxPrefab, AttackDirection.Unblockable,
                comboPunishDamage, comboPunishStun, 0f, slashSpawnDistance, slashActiveDuration, null));
        }

        // Blocked or unblocked, the boss backs off here either way — the punish swing
        // above (if it ran) already happened before this.
        yield return StartCoroutine(RetreatFromPlayer(retreatDistance, retreatDuration));
    }

    // ---------------- Heavy Attack Chain ----------------

    private IEnumerator RunHeavyAttackChain()
    {
        GameObject chargeIndicator = null;
        if (heavyChargeIndicatorPrefab != null)
        {
            chargeIndicator = Instantiate(heavyChargeIndicatorPrefab, OriginPos + indicatorOffset, Quaternion.identity, transform);
        }
        yield return new WaitForSeconds(heavyChargeDuration);
        if (chargeIndicator != null) Destroy(chargeIndicator);

        bool isHigh = Random.value < 0.5f;

        GameObject tellPrefab = isHigh ? heavyHighIndicatorPrefab : heavyLowIndicatorPrefab;
        GameObject tell = null;
        if (tellPrefab != null)
        {
            tell = Instantiate(tellPrefab, OriginPos + indicatorOffset, Quaternion.identity, transform);
        }
        else
        {
            Debug.Log("[BossAI] " + (isHigh ? "heavyHighIndicatorPrefab" : "heavyLowIndicatorPrefab")
                + " not assigned — no visual tell, but timing still holds.");
        }
        yield return new WaitForSeconds(heavyTelegraphDuration);
        if (tell != null) Destroy(tell);

        lastSequenceHitPlayer = false;

        if (isHigh)
        {
            // Overhead-blockable. (Doc also mentions "moving backwards" as part of
            // fully dodging this — not modeled; see the field tooltip above.)
            yield return new WaitForSeconds(heavyHighWindup);
            yield return StartCoroutine(SwingSlash(heavyHighHitboxPrefab, AttackDirection.Overhead, heavyHighDamage,
                heavyHighStun, 0f, slashSpawnDistance, heavyHighActiveDuration, target => lastSequenceHitPlayer = true));
        }
        else
        {
            // Side-blockable only — no jump-dodge option for this one.
            yield return new WaitForSeconds(heavyLowWindup);
            yield return StartCoroutine(SwingSlash(heavyLowHitboxPrefab, AttackDirection.Side, heavyLowDamage,
                heavyLowStun, 0f, slashSpawnDistance, heavyLowActiveDuration, target => lastSequenceHitPlayer = true));
        }

        if (lastSequenceHitPlayer && playerTarget != null && player != null)
        {
            // Grab + throw behind the boss, heavy damage — Unblockable bypasses block
            // entirely, same as any grab.
            Vector2 throwPos = (Vector2)transform.position - FacingDir * heavyThrowDistance;
            player.position = throwPos;
            playerTarget.ApplyHit(new HitInfo(heavyThrowDamage, heavyThrowKnockdownDuration, Vector2.zero, gameObject,
                causesKnockdown: true), AttackDirection.Unblockable);
        }

        // "Instantly follows up with basic attack chain, but not before the player has
        // time to react" — this pause IS that reaction window.
        yield return new WaitForSeconds(chainIntoBasicReactionPause);
        yield return StartCoroutine(RunBasicAttackChain());
    }

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

    private void HandleDeath()
    {
        Debug.Log("[BossAI] defeated");
        StopAllCoroutines();
        chainRunning = false;
        // TODO: whatever the actual defeat sequence should be (drop loot, play an
        // animation, trigger a cutscene, etc.) — not specified yet.
    }
}