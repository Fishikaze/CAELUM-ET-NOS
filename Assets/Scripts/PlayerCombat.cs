using System.Collections;
using UnityEngine;

/// <summary>
/// Fighting-mode ability set:
///   - Basic attack (left click): landing one hit opens a combo that lets you
///     land up to 3 more basic attacks while the enemy is locked in Stunned
///     state — TSB-style stagger juggling. Each swing has its own 0.2s cooldown,
///     and if you don't land the next hit within the combo window (or after the
///     4th hit), you're locked out of basic attacks for 1.5s — gives the enemy
///     room to fight back instead of getting permanently stunlocked.
///   - Q: short dash toward facing direction. Connects with an enemy along the
///     way -> grabs them and lands one guaranteed hit, opening a combo.
///   - E: heavy kick. Slower windup, big damage + knockback, combo finisher.
///   - R: knockdown. Puts the enemy into Knockdown state (can't act).
///   - T: combo continuer. Only connects against a Knockdown target — wakes
///     them into Stunned so basic attacks can keep chaining off it.
///   - Right click (held): block. Reduces/negates incoming damage and stun
///     via CombatTarget.isBlocking, set every frame below.
///
/// Only enabled while PlayerModeController has the player in Fighting mode.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CombatTarget))]
[RequireComponent(typeof(FightingController))]
public class PlayerCombat : MonoBehaviour
{
    // Basic attack assumed on left click since right click is taken by block.
    // Change this if you want a different binding (e.g. KeyCode.J for a
    // controller-style layout).
    private const KeyCode BASIC_ATTACK_KEY = KeyCode.Mouse0;
    private const KeyCode BLOCK_KEY = KeyCode.Mouse1;

    [Header("Targeting")]
    public LayerMask enemyLayer;
    [Tooltip("Empty child positioned at chest height. Hit checks originate here.")]
    public Transform hitOrigin;

    [Header("Basic Attack Combo (TSB-style stagger)")]
    public float basicAttackDamage = 8f;
    public float basicAttackRange = 1f;
    public float comboStunDuration = 0.6f;  // how long each hit locks the enemy
    public float comboWindow = 0.5f;        // time allowed to land the *next* hit before the combo drops
    public float attackCooldown = 0.2f;     // minimum time between individual basic-attack swings (stops mashing)
    public float comboEndLockout = 1.5f;    // can't throw a basic attack for this long after a dropped combo or the 4th hit
    public int maxComboHits = 4;            // 1 opener + 3 follow-ups, per spec
    public Vector2 comboFinisherKnockback = new Vector2(6f, 4f);
    [Tooltip("Spawned on hits 1–3 of the combo.")]
    public GameObject basicHitParticlePrefab;
    [Tooltip("Spawned on the 4th (finisher) hit instead of the basic one.")]
    public GameObject finisherHitParticlePrefab;

    [Header("Q — Dash Grab")]
    public float dashDistance = 3.5f;
    public float dashDuration = 0.15f;
    public float dashHitCheckRadius = 0.5f;
    public float dashGrabDamage = 10f;
    public float dashGrabStun = 0.5f;
    public float dashCooldown = 1.2f;

    [Header("E — Heavy Kick")]
    [Tooltip("Prefab with a Collider2D + MeleeHitbox component, plus whatever Animator/VFX plays your swing.")]
    public GameObject heavyKickHitboxPrefab;
    public float heavyKickWindup = 0.15f;
    public float heavyKickSpawnDistance = 0.8f; // how far from hitOrigin, along the aim direction, the hitbox spawns
    public float heavyKickRecovery = 0.1f;      // control returns this long after the hitbox is thrown
    public float heavyKickDamage = 16f;
    public float heavyKickKnockbackForce = 10f; // direction comes from the aim toward the mouse, not FacingSign
    public float heavyKickCooldown = 1.5f;

    [Header("R — Knockdown")]
    [Tooltip("Prefab with a Collider2D + MeleeHitbox component, plus whatever Animator/VFX plays your swing.")]
    public GameObject knockdownHitboxPrefab;
    public float knockdownWindup = 0.2f;
    public float knockdownSpawnDistance = 0.7f; // how far from hitOrigin, along the aim direction, the hitbox spawns
    public float knockdownRecovery = 0.1f;      // control returns this long after the hitbox is thrown
    public float knockdownDamage = 12f;
    public float knockdownKnockbackForce = 2f;  // small — this move's job is the knockdown, not a big launch
    public float knockdownDuration = 1.4f;
    public float knockdownCooldown = 2f;

    [Header("R — Ground Slam VFX")]
    [Tooltip("Spawned at the enemy the instant the knockdown hit connects.")]
    public GameObject knockdownHitParticlePrefab;
    [Tooltip("Spawned at the ground point once the enemy has visually gone down.")]
    public GameObject groundSlamParticlePrefab;
    public LayerMask groundLayer;
    [Tooltip("How long to wait after the hit before raycasting for the ground and playing the slam — line this up with the enemy's knockdown-sink time (HitReactionVisuals' knockdownSinkDepth / sinkRiseSpeed).")]
    public float groundSlamDelay = 0.1f;
    public float groundSlamRayDistance = 5f;

    [Header("T — Combo Continuer (hits a knocked-down enemy)")]
    public float comboExtendRange = 1.3f;
    public float comboExtendDamage = 10f;
    public float comboExtendCooldown = 1f;

    private Rigidbody2D rb;
    private CombatTarget selfTarget;
    private FightingController fightingController;

    private bool isBusy; // mid dash/windup — blocks new abilities
    private CombatTarget comboTarget;
    private int comboCount;
    private float comboWindowTimer;
    private float nextBasicAttackTime;   // per-swing 0.2s gate
    private float basicAttackLockoutUntil; // 1.5s "enemy gets to fight back" window after a dropped/finished combo

    private float nextQTime, nextETime, nextRTime, nextTTime;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        selfTarget = GetComponent<CombatTarget>();
        fightingController = GetComponent<FightingController>();
    }

    private void OnDisable()
    {
        // Leaving fighting mode mid-action would otherwise leave movement locked forever.
        isBusy = false;
        fightingController.MovementLocked = false;
        selfTarget.isBlocking = false;
        EndCombo();
    }

    private void Update()
    {
        // Block: simple hold-to-block. Only usable from a neutral state — you can't
        // suddenly block while mid-dash or already stunned.
        selfTarget.isBlocking = Input.GetKey(BLOCK_KEY) && !isBusy && selfTarget.CurrentState == CombatTarget.State.Normal;

        if (comboTarget != null)
        {
            comboWindowTimer -= Time.deltaTime;
            if (comboWindowTimer <= 0f || !comboTarget.IsAvailableForCombo)
            {
                EndComboWithLockout(); // didn't follow up in time — combo drops, enemy gets a breather
            }
        }

        if (isBusy || selfTarget.CurrentState != CombatTarget.State.Normal)
        {
            return; // can't start a new action while dashing/attacking/stunned
        }

        if (Input.GetKeyDown(BASIC_ATTACK_KEY)) TryBasicAttack();
        if (Input.GetKeyDown(KeyCode.Q) && Time.time >= nextQTime) StartCoroutine(DashGrab());
        if (Input.GetKeyDown(KeyCode.E) && Time.time >= nextETime) StartCoroutine(HeavyKick());
        if (Input.GetKeyDown(KeyCode.R) && Time.time >= nextRTime) StartCoroutine(Knockdown());
        if (Input.GetKeyDown(KeyCode.T) && Time.time >= nextTTime) TryComboExtend();
    }

    private float FacingSign => fightingController.FacingSign;
    private Vector2 OriginPos => hitOrigin != null ? (Vector2)hitOrigin.position : (Vector2)transform.position;

    /// <summary>
    /// Direction from OriginPos toward the mouse cursor in world space, used to aim E.
    /// Assumes an orthographic camera looking straight down the Z axis and the
    /// player sitting at Z = 0 — standard for a 2D side-scroller. Falls back to
    /// FacingSign if there's no camera or the cursor is exactly on top of the player.
    /// </summary>
    private Vector2 GetAimDirection()
    {
        if (Camera.main == null) return new Vector2(FacingSign, 0f);

        Vector3 mouseScreen = Input.mousePosition;
        mouseScreen.z = -Camera.main.transform.position.z;
        Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(mouseScreen);

        Vector2 dir = (Vector2)mouseWorld - OriginPos;
        return dir.sqrMagnitude > 0.0001f ? dir.normalized : new Vector2(FacingSign, 0f);
    }

    private CombatTarget FindTargetInRange(float range)
    {
        Vector2 center = OriginPos + Vector2.right * FacingSign * (range * 0.5f);
        Collider2D hit = Physics2D.OverlapBox(center, new Vector2(range, 1.2f), 0f, enemyLayer);
        return hit != null ? hit.GetComponent<CombatTarget>() : null;
    }

    // ---------------- Basic attack + combo ----------------

    private void TryBasicAttack()
    {
        if (Time.time < basicAttackLockoutUntil) return; // recovering from a dropped/finished combo
        if (Time.time < nextBasicAttackTime) return;      // per-swing cooldown, stops mashing

        nextBasicAttackTime = Time.time + attackCooldown;

        if (comboTarget == null)
        {
            // No combo running yet — this swing has to land to start one.
            CombatTarget target = FindTargetInRange(basicAttackRange);
            if (target == null) return;
            LandComboHit(target);
            return;
        }

        // Continuing an existing combo — must still be roughly in range of the same target.
        if (Vector2.Distance(OriginPos, comboTarget.transform.position) <= basicAttackRange + 0.3f)
        {
            LandComboHit(comboTarget);
        }
    }

    private void LandComboHit(CombatTarget target)
    {
        bool isFinisher = comboCount + 1 >= maxComboHits;

        Vector2 knockback = isFinisher
            ? new Vector2(comboFinisherKnockback.x * FacingSign, comboFinisherKnockback.y)
            : Vector2.zero; // mid-combo hits keep the target in place so the chain can continue

        target.ApplyHit(new HitInfo(basicAttackDamage, comboStunDuration, knockback, gameObject));
        SpawnHitParticles(target.transform.position, isFinisher);

        comboTarget = target;
        comboCount++;
        comboWindowTimer = comboWindow;

        if (isFinisher)
        {
            EndComboWithLockout(); // 4th hit — same breather the enemy gets from a dropped combo
        }
    }

    private void SpawnHitParticles(Vector3 position, bool isFinisher)
    {
        GameObject prefab = isFinisher ? finisherHitParticlePrefab : basicHitParticlePrefab;
        if (prefab != null)
        {
            Instantiate(prefab, position, Quaternion.identity);
        }
    }

    private void EndCombo()
    {
        comboTarget = null;
        comboCount = 0;
        comboWindowTimer = 0f;
    }

    /// <summary>Ends the combo and starts the 1.5s window before another basic attack can be thrown.</summary>
    private void EndComboWithLockout()
    {
        EndCombo();
        basicAttackLockoutUntil = Time.time + comboEndLockout;
    }

    // ---------------- Q: Dash Grab ----------------

    private IEnumerator DashGrab()
    {
        isBusy = true;
        fightingController.MovementLocked = true;
        nextQTime = Time.time + dashCooldown;

        Vector2 start = rb.position;
        Vector2 end = start + Vector2.right * FacingSign * dashDistance;
        float t = 0f;
        CombatTarget grabbed = null;

        while (t < dashDuration)
        {
            t += Time.fixedDeltaTime;
            Vector2 nextPos = Vector2.Lerp(start, end, t / dashDuration);

            // Check the swept path each step so a fast dash can't skip past a thin enemy.
            Collider2D hit = Physics2D.OverlapCircle(nextPos, dashHitCheckRadius, enemyLayer);
            if (hit != null)
            {
                grabbed = hit.GetComponent<CombatTarget>();
                rb.MovePosition((Vector2)hit.transform.position - Vector2.right * FacingSign * 0.6f);
                break;
            }

            rb.MovePosition(nextPos);
            yield return new WaitForFixedUpdate();
        }

        if (grabbed != null)
        {
            grabbed.SetState(CombatTarget.State.Grabbed, 0.3f);
            yield return new WaitForSeconds(0.15f); // brief hold before the strike lands

            grabbed.ApplyHit(new HitInfo(dashGrabDamage, dashGrabStun, Vector2.zero, gameObject));

            comboTarget = grabbed;
            comboCount = 1; // the grab hit counts as the combo opener
            comboWindowTimer = comboWindow;
        }

        fightingController.MovementLocked = false;
        isBusy = false;
    }

    // ---------------- E: Heavy Kick ----------------

    private IEnumerator HeavyKick()
    {
        isBusy = true;
        fightingController.MovementLocked = true;
        nextETime = Time.time + heavyKickCooldown;

        yield return new WaitForSeconds(heavyKickWindup);

        if (heavyKickHitboxPrefab != null)
        {
            Vector2 aimDir = GetAimDirection();
            Vector2 spawnPos = OriginPos + aimDir * heavyKickSpawnDistance;
            float angle = Mathf.Atan2(aimDir.y, aimDir.x) * Mathf.Rad2Deg;

            GameObject hitboxObj = Instantiate(heavyKickHitboxPrefab, spawnPos, Quaternion.Euler(0f, 0f, angle));
            MeleeHitbox hitbox = hitboxObj.GetComponent<MeleeHitbox>();
            if (hitbox != null)
            {
                hitbox.Initialize(enemyLayer, heavyKickDamage, comboStunDuration, heavyKickKnockbackForce,
                    gameObject, aimDir, OnHeavyKickConnect);
            }
        }

        yield return new WaitForSeconds(heavyKickRecovery);

        fightingController.MovementLocked = false;
        isBusy = false;
    }

    private void OnHeavyKickConnect(CombatTarget target)
    {
        EndCombo(); // heavy kick launches — treat it as a combo ender
    }

    // ---------------- R: Knockdown ----------------

    private IEnumerator Knockdown()
    {
        isBusy = true;
        fightingController.MovementLocked = true;
        nextRTime = Time.time + knockdownCooldown;

        yield return new WaitForSeconds(knockdownWindup);

        if (knockdownHitboxPrefab != null)
        {
            Vector2 aimDir = GetAimDirection();
            Vector2 spawnPos = OriginPos + aimDir * knockdownSpawnDistance;
            float angle = Mathf.Atan2(aimDir.y, aimDir.x) * Mathf.Rad2Deg;

            GameObject hitboxObj = Instantiate(knockdownHitboxPrefab, spawnPos, Quaternion.Euler(0f, 0f, angle));
            MeleeHitbox hitbox = hitboxObj.GetComponent<MeleeHitbox>();
            if (hitbox != null)
            {
                hitbox.Initialize(enemyLayer, knockdownDamage, knockdownDuration, knockdownKnockbackForce,
                    gameObject, aimDir, OnKnockdownConnect, causesKnockdown: true);
            }
        }

        yield return new WaitForSeconds(knockdownRecovery);

        fightingController.MovementLocked = false;
        isBusy = false;
    }

    private void OnKnockdownConnect(CombatTarget target)
    {
        comboTarget = target;
        comboCount = Mathf.Max(comboCount, 1);
        comboWindowTimer = comboWindow;

        if (knockdownHitParticlePrefab != null)
        {
            Instantiate(knockdownHitParticlePrefab, target.transform.position, Quaternion.identity);
        }

        StartCoroutine(GroundSlamRoutine(target));
    }

    /// <summary>
    /// Waits for the enemy's knockdown-sink to roughly finish, then raycasts straight
    /// down from its position to find the ground and plays the slam effect there —
    /// so the particle lands on the floor even on uneven terrain, rather than being
    /// hard-coded to a fixed Y position.
    /// </summary>
    private IEnumerator GroundSlamRoutine(CombatTarget target)
    {
        yield return new WaitForSeconds(groundSlamDelay);

        if (target == null || groundSlamParticlePrefab == null) yield break;

        Vector2 origin = target.transform.position;
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, groundSlamRayDistance, groundLayer);
        Vector3 groundPos = hit.collider != null ? (Vector3)hit.point : target.transform.position;

        Instantiate(groundSlamParticlePrefab, groundPos, Quaternion.identity);
    }

    // ---------------- T: Combo Continuer ----------------

    private void TryComboExtend()
    {
        CombatTarget target = FindTargetInRange(comboExtendRange);
        if (target == null || target.CurrentState != CombatTarget.State.Knockdown) return;

        nextTTime = Time.time + comboExtendCooldown;

        // Wakes the enemy back into Stunned so basic attacks can keep chaining off it.
        target.ApplyHit(new HitInfo(comboExtendDamage, comboStunDuration, Vector2.zero, gameObject));

        comboTarget = target;
        comboCount = Mathf.Min(comboCount + 1, maxComboHits - 1); // leaves room for at least one more basic hit
        comboWindowTimer = comboWindow;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (hitOrigin == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(hitOrigin.position, basicAttackRange);
    }
#endif
}