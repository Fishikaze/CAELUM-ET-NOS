using System.Collections;
using UnityEngine;

/// <summary>
/// Fighting-mode ability set:
///   - Basic attack (left click): landing one hit opens a combo that lets you
///     land up to 3 more basic attacks while the enemy is locked in Stunned
///     state — TSB-style stagger juggling.
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
    public float comboStunDuration = 0.6f; // how long each hit locks the enemy
    public float comboWindow = 0.5f;       // time allowed to land the *next* hit
    public int maxComboHits = 4;           // 1 opener + 3 follow-ups, per spec
    public Vector2 comboFinisherKnockback = new Vector2(6f, 4f);

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
    public float knockdownWindup = 0.2f;
    public float knockdownRange = 1.1f;
    public float knockdownDamage = 12f;
    public float knockdownDuration = 1.4f;
    public float knockdownCooldown = 2f;

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
                EndCombo();
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

        comboTarget = target;
        comboCount++;
        comboWindowTimer = comboWindow;

        if (isFinisher)
        {
            EndCombo();
        }
    }

    private void EndCombo()
    {
        comboTarget = null;
        comboCount = 0;
        comboWindowTimer = 0f;
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

        CombatTarget target = FindTargetInRange(knockdownRange);
        if (target != null)
        {
            target.ApplyHit(new HitInfo(knockdownDamage, knockdownDuration, Vector2.zero, gameObject, causesKnockdown: true));

            comboTarget = target;
            comboCount = Mathf.Max(comboCount, 1);
            comboWindowTimer = comboWindow;
        }

        fightingController.MovementLocked = false;
        isBusy = false;
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