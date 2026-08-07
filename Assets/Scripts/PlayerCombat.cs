using System.Collections;
using UnityEngine;

/// <summary>
/// Fighting-mode ability set (mouse-free, WASD/JKL;F scheme):
///
///   Movement
///   - A / D: move left / right (handled by FightingController; this script
///     only reacts to A/D for dash detection).
///   - Double-tap A or D: short directional dash. After the dash ends you
///     can't dash again for a short cooldown, but you CAN attack immediately.
///   - W / S: combo direction modifiers — held alongside L to pick a slam
///     variant (see below).
///   - Space: jump (grounded only).
///
///   J — Light attack (was Q)
///   - Up to 4 hits in a stagger chain, TSB-style. The 4th (finisher) hit
///     knocks the enemy back a short distance and gives them a brief
///     i-frame window (CombatTarget.SetInvulnerable) instead of the old
///     pure "player lockout" — the enemy is untouchable for a beat rather
///     than just being fought-back-able.
///
///   K — Kick / heavy attack (was E)
///   - Two-stage: first kick lands with a small knockback and NO stun, so
///     the enemy can be knocked just out of range of (or fight back during)
///     the second kick. A short recovery after hit 1 stops K from being
///     mashed. Land the second kick within the follow-up window for a
///     bigger knockback + short stun.
///   - Dash + K (press K within a brief window right after a directional
///     dash ends): forward kick — dash forward, immediately follow with a
///     stunning kick. Blockable (goes through normal CombatTarget.ApplyHit).
///
///   L — Slam (was R)
///   - Does nothing pressed alone. Only fires while held together with W
///     (uppercut) or S (ground slam), and only while a combo target is
///     active (mid-J/K chain, target still Stunned/Knockdown-available).
///     Spawns a real hitbox with windup/recovery, same as the old R. Ground
///     slam knocks down + reuses the ground-slam raycast VFX; the uppercut
///     is the identical attack with its knockback mirrored across the
///     horizontal axis (down -> up). Ends the combo.
///
///   ; — Disrespectful kick (was T)
///   - Only usable on a target you just tech-grabbed with F. First press:
///     a boot prefab stomps down in front of the player (short cancellable
///     windup, like the grab). If it connects, a distinct floor-impact
///     particle plays and the boot sprite fades out. Second ; press within
///     a follow-up window: another boot kick, this time swept in the
///     player's facing direction — a long-distance punt with extra damage.
///
///   F — Tech grab
///   - Startup window that's cancelled if you get hit during it. On success,
///     pulls the target in front of you and knocks them down — that target
///     becomes eligible for the ; finisher for a short window afterward.
///
/// NOTE: FacingSign is still read from FightingController. Since aiming used
/// to come from the mouse, FightingController's FacingSign now needs to be
/// driven by movement input (A/D) instead — that's a change to that script,
/// not this one.
///
/// Only enabled while PlayerModeController has the player in Fighting mode.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CombatTarget))]
[RequireComponent(typeof(FightingController))]
public class PlayerCombat : MonoBehaviour
{
    // ---- Key bindings ----
    private const KeyCode LEFT_KEY = KeyCode.A;
    private const KeyCode RIGHT_KEY = KeyCode.D;
    private const KeyCode COMBO_UP_KEY = KeyCode.W;
    private const KeyCode COMBO_DOWN_KEY = KeyCode.S;
    private const KeyCode LIGHT_ATTACK_KEY = KeyCode.J;
    private const KeyCode KICK_KEY = KeyCode.K;
    private const KeyCode SLAM_KEY = KeyCode.L;
    private const KeyCode DISRESPECT_KEY = KeyCode.Semicolon;
    private const KeyCode GRAB_KEY = KeyCode.F;
    private const KeyCode JUMP_KEY = KeyCode.Space;
    // ASSUMPTION: block wasn't given a new binding since it's not a mouse
    // mechanic itself, just previously mapped to right click. Moved to
    // Left Shift (hold) — change if you want something else.
    private const KeyCode BLOCK_KEY = KeyCode.LeftShift;

    [Header("Targeting")]
    public LayerMask enemyLayer;
    [Tooltip("Empty child positioned at chest height. Hit checks originate here.")]
    public Transform hitOrigin;

    [Header("Animation")]
    [Tooltip("Auto-found via GetComponent/GetComponentInChildren if left empty.")]
    public Animator animator;
    [Tooltip("Auto-found via GetComponent/GetComponentInChildren if left empty. Used to flip the sprite (SpriteRenderer.flipX), not the whole transform — negatively scaling a transform that also carries the Rigidbody2D/Collider2D can cause physics issues, so only the visual mirrors.")]
    public SpriteRenderer spriteRenderer;
    [Tooltip("Animator BOOL parameter driven continuously by horizontal speed — true while running, false while idle.")]
    public string isMovingAnimParam = "IsMoving";
    [Tooltip("Horizontal speed above which the player counts as 'running' for animation purposes.")]
    public float runAnimThreshold = 0.1f;
    [Tooltip("Animator TRIGGER fired for the 1st/3rd hits of the J combo (the one that starts it).")]
    public string punchLeftAnimTrigger = "PunchLeft";
    [Tooltip("Animator TRIGGER fired for the 2nd/4th hits of the J combo (alternates with PunchLeft).")]
    public string punchRightAnimTrigger = "PunchRight";
    [Tooltip("Animator TRIGGER fired for every other action — Kick (both hits), Dash+Kick, Slam, Grab, and both stages of the Disrespectful Kick. One shared animation for everything that isn't a J punch, idle, or running.")]
    public string specialAnimTrigger = "Special";

    [Header("Jump")]
    public float jumpForce = 8f;
    public float groundCheckRadius = 0.2f;
    public Vector2 groundCheckOffset = new Vector2(0f, -1f);
    public LayerMask groundLayer;

    [Header("Block / Parry (hold Left Shift + A/D for Side, or + W for Overhead)")]
    [Tooltip("How long the block window — and its shield visual — stays up once triggered.")]
    public float blockWindowDuration = 0.35f;
    [Tooltip("Minimum time between block windows, so the block key can't be spammed.")]
    public float blockCooldown = 0.5f;
    [Tooltip("Shown to the left/right of the player during a Side block.")]
    public GameObject sideShieldPrefab;
    [Tooltip("Shown above the player during an Overhead block.")]
    public GameObject overheadShieldPrefab;
    [Tooltip("How far to the side (in whichever direction was pressed) the side shield appears.")]
    public float sideShieldOffset = 0.8f;
    [Tooltip("How far above the player the overhead shield appears.")]
    public float overheadShieldOffset = 1.2f;
    [Tooltip("How long the shield sprite takes to fade out once its block window ends.")]
    public float shieldFadeDuration = 0.15f;
    [Tooltip("Spawned at the shield's position when a block actually negates a hit (CombatTarget.OnParrySuccess).")]
    public GameObject parrySuccessParticlePrefab;
    [Tooltip("Played through SoundManager when a block/parry successfully negates a hit.")]
    public AudioClip blockSuccessClip;

    [Header("Dash (double-tap A/D)")]
    public float doubleTapWindow = 0.25f;
    public float dashDistance = 3f;
    public float dashDuration = 0.15f;
    public float dashCooldown = 0.8f; // time before you can dash again; attacking is NOT gated by this
    [Tooltip("Window after a dash ends during which pressing K triggers the forward-kick instead of a normal kick.")]
    public float dashKickWindow = 0.25f;

    [Header("J — Light Attack Combo")]
    public float basicAttackDamage = 8f;
    public float basicAttackRange = 1f;
    public float comboStunDuration = 0.6f;
    public float comboWindow = 0.5f;
    public float attackCooldown = 0.2f;
    public float comboEndLockout = 1.5f;
    public int maxComboHits = 4;
    public Vector2 comboFinisherKnockback = new Vector2(6f, 4f);
    [Tooltip("How long the enemy is invulnerable for after the 4th J hit.")]
    public float finisherIFrameDuration = 0.3f;
    [Tooltip("Spawned on hits 1–3 of the combo.")]
    public GameObject basicHitParticlePrefab;
    [Tooltip("Spawned on the 4th (finisher) hit instead of the basic one.")]
    public GameObject finisherHitParticlePrefab;

    [Header("K — Kick (two-stage)")]
    [Tooltip("Prefab with a Collider2D + MeleeHitbox component, plus whatever Animator/VFX plays your swing.")]
    public GameObject kickHitboxPrefab;
    public float kickRange = 1.1f;
    public float kickSpawnDistance = 0.8f;
    public float kick1Damage = 6f;
    public float kick1Knockback = 3f;
    [Tooltip("Lag after the first kick — long enough to stop mashing, short enough that the enemy (un-stunned) can swing back.")]
    public float kick1Recovery = 0.25f;
    [Tooltip("Total time from kick 1 landing in which kick 2 must land.")]
    public float kickFollowupWindow = 0.6f;
    public float kick2Damage = 10f;
    public float kick2Knockback = 8f;
    public float kick2Stun = 0.4f;
    public float kick2Recovery = 0.3f;
    public float kickCooldown = 0.2f; // gate on starting a fresh kick sequence

    [Header("Dash + K — Forward Kick")]
    public float forwardKickDashDistance = 2.5f;
    public float forwardKickDashDuration = 0.12f;
    public float forwardKickDamage = 9f;
    public float forwardKickKnockback = 4f;
    public float forwardKickStun = 0.7f;
    [Tooltip("Spawned at the enemy's position on a successful connect. Separate from basicHitParticlePrefab/finisherHitParticlePrefab so the dash-kick impact can look distinct.")]
    public GameObject forwardKickHitParticlePrefab;

    [Header("L — Slam (combo-only, held W = uppercut / held S = ground slam)")]
    [Tooltip("Prefab with a Collider2D + MeleeHitbox component, plus whatever Animator/VFX plays the swing.")]
    public GameObject slamHitboxPrefab;
    [Tooltip("Must be within this range of comboTarget to even start the windup.")]
    public float slamRange = 1.3f;
    public float slamWindup = 0.2f;
    public float slamSpawnDistance = 0.7f;
    public float slamRecovery = 0.1f;
    public float slamCooldown = 1.5f;
    public float slamDamage = 14f;
    [Tooltip("Long stun for the uppercut; also doubles as the knockdown duration for the ground slam.")]
    public float slamStunDuration = 1.4f;
    [Tooltip("Ground slam's knockback (forward + down). The uppercut is the same attack with knockback.y mirrored across the horizontal axis (i.e. flipped to forward + up).")]
    public Vector2 slamKnockback = new Vector2(2f, -6f);
    [Tooltip("Spawned at the enemy the instant a ground-slam L connects.")]
    public GameObject knockdownHitParticlePrefab;
    [Tooltip("Spawned at the ground point once the enemy has visually gone down.")]
    public GameObject groundSlamParticlePrefab;
    [Tooltip("How long to wait after the ground-slam hit before raycasting for the ground and playing the slam VFX.")]
    public float groundSlamDelay = 0.1f;
    public float groundSlamRayDistance = 5f;

    [Header("F — Tech Grab")]
    public float grabWindup = 0.3f;
    public float grabRange = 1.2f;
    public float grabPullDistance = 0.7f;
    public float grabKnockdownDuration = 1.6f;
    public float grabCooldown = 1.5f;
    [Tooltip("How long after a successful grab the ; disrespect kick remains available.")]
    public float grabWindowForDisrespect = 2f;

    [Header("; — Disrespectful Kick, stage 1: Stomp")]
    [Tooltip("Boot prefab — needs a Collider2D + MeleeHitbox, just like slamHitboxPrefab/kickHitboxPrefab.")]
    public GameObject bootPrefab;
    public float stompWindup = 0.5f;
    [Tooltip("How far in front of the player the boot's landing spot is — same convention as kickSpawnDistance/slamSpawnDistance.")]
    public float stompSpawnDistance = 0.5f;
    [Tooltip("How high above the landing spot the boot starts before stomping down onto it.")]
    public float stompDropHeight = 1.5f;
    [Tooltip("How long the downward stomp motion itself takes, from drop height down to the landing spot.")]
    public float stompDescentDuration = 0.15f;
    public float stompRecovery = 0.2f;
    public float stompDamage = 10f;
    public float stompStunDuration = 0.6f;
    [Tooltip("Distinct floor-impact VFX — only spawned if the stomp actually connects with the grabbed enemy.")]
    public GameObject stompFloorParticlePrefab;
    [Tooltip("How long the boot sprite takes to fade out after each kick (stomp or punt) — starts once the motion for that kick has finished, not on spawn.")]
    public float bootFadeDuration = 0.3f;

    [Header("; — Disrespectful Kick, stage 2: Punt (2nd press, facing direction, circular kicking arc)")]
    [Tooltip("Window after a successful stomp during which pressing ; again triggers the punt instead of starting a new stomp.")]
    public float puntFollowupWindow = 1.2f;
    public float puntWindup = 0.15f;
    [Tooltip("Pivot-relative angle (degrees) where the arc sweep starts. 0 = straight forward (facing direction), negative = below. Mirrors automatically with facing so the swing looks the same facing either way.")]
    public float puntArcStartAngle = -150f;
    [Tooltip("Pivot-relative angle (degrees) where the sweep ends. Both this and the start angle are negative by default so the sweep passes through -90° (straight down) at its midpoint — a low kick through the bottom of the circle, not over the top.")]
    public float puntArcEndAngle = -30f;
    [Tooltip("Radius of the circular arc — fixed distance from the pivot (roughly hip height, at OriginPos) to the boot throughout the whole sweep.")]
    public float puntArcRadius = 1f;
    [Tooltip("How long the arc sweep itself takes, separate from puntWindup (before it) and puntRecovery (after).")]
    public float puntSweepDuration = 0.25f;
    [Tooltip("Stun applied on hit — needs to be nonzero (same idea as comboStunDuration on the J finisher) so the target stays locked down long enough for the knockback to actually carry them. At 0, they return to Normal almost instantly and EnemyAI's own movement immediately overwrites the knockback velocity again.")]
    public float puntStunDuration = 0.6f;
    public float puntRecovery = 0.25f;
    public float puntDamage = 20f;
    public float puntKnockback = 16f;

    private Rigidbody2D rb;
    private CombatTarget selfTarget;
    private FightingController fightingController;

    private bool isBusy; // mid dash/windup/attack sequence — blocks new top-level actions
    private CombatTarget comboTarget;
    private int comboCount;
    private float comboWindowTimer;
    private float nextBasicAttackTime;
    private float basicAttackLockoutUntil;

    private float lastATapTime = -999f;
    private float lastDTapTime = -999f;
    private float nextDashTime;
    private float dashKickWindowUntil = -999f;

    private int kickStage; // 0 = idle, 1 = waiting on follow-up
    private float kickFollowupDeadline;
    private CombatTarget kickTarget;
    private float nextKickStartTime;

    private float nextSlamTime;

    private float nextGrabTime;
    private CombatTarget grabbedTarget;
    private float grabbedTargetExpiresAt;
    private bool isChargingStomp;
    private bool cancelStompRequested;
    private int disrespectStage; // 0 = idle, 1 = stomp landed, waiting on punt follow-up
    private CombatTarget disrespectTarget;
    private float puntDeadline;

    private float nextBlockTime;
    private GameObject activeShield;
    private Coroutine blockWindowRoutine;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        selfTarget = GetComponent<CombatTarget>();
        fightingController = GetComponent<FightingController>();

        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    private void OnEnable()
    {
        selfTarget.OnParrySuccess += HandleParrySuccess;
    }

    private void OnDisable()
    {
        isBusy = false;
        fightingController.MovementLocked = false;
        selfTarget.isBlocking = false;
        EndCombo();
        grabbedTarget = null;
        kickStage = 0;
        disrespectStage = 0;
        disrespectTarget = null;
        isChargingStomp = false;

        selfTarget.OnParrySuccess -= HandleParrySuccess;
        if (blockWindowRoutine != null)
        {
            StopCoroutine(blockWindowRoutine);
            blockWindowRoutine = null;
        }
        if (activeShield != null)
        {
            Destroy(activeShield);
            activeShield = null;
        }
    }

    private void Update()
    {
        // Face the correct direction — mirrors the sprite (not the whole transform)
        // based on FacingSign, which FightingController already drives from movement
        // input. Default/unflipped art is assumed to face right, since that's the
        // "always facing right" behavior this replaces — flip this condition if your
        // sprite's default orientation is actually left-facing.
        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = FacingSign < 0f;
        }

        // Running/Idle reflects actual horizontal speed regardless of what else is
        // going on — updated unconditionally, unlike every other trigger below which
        // only fires at the moment a specific action starts.
        if (animator != null)
        {
            animator.SetBool(isMovingAnimParam, Mathf.Abs(rb.velocity.x) > runAnimThreshold);
        }

        // Directional block/parry: hold Shift and tap A/D for a Side block, or W for
        // an Overhead block. Each press opens a fresh timed block window (see
        // BlockWindowRoutine) instead of a simple "held = blocking" flag — CombatTarget
        // isBlocking/blockDirection are set for the window's duration, then clear
        // automatically once it ends.
        if (!isBusy && selfTarget.CurrentState == CombatTarget.State.Normal
            && Time.time >= nextBlockTime && Input.GetKey(BLOCK_KEY))
        {
            if (Input.GetKeyDown(LEFT_KEY))
            {
                StartBlockWindow(AttackDirection.Side, -1f);
            }
            else if (Input.GetKeyDown(RIGHT_KEY))
            {
                StartBlockWindow(AttackDirection.Side, 1f);
            }
            else if (Input.GetKeyDown(COMBO_UP_KEY))
            {
                StartBlockWindow(AttackDirection.Overhead, 0f);
            }
        }

        if (comboTarget != null)
        {
            comboWindowTimer -= Time.deltaTime;
            if (comboWindowTimer <= 0f || !comboTarget.IsAvailableForCombo)
            {
                EndComboWithLockout();
            }
        }

        if (grabbedTarget != null && Time.time >= grabbedTargetExpiresAt)
        {
            grabbedTarget = null;
        }

        // Dash double-tap detection runs even while otherwise idle-gated below, but
        // not while already busy, and not while Shift is held — that's block input
        // claiming A/D for the moment, and jumping is held back the same way here
        // (can't jump out of a block attempt). Move the jump check to its own
        // un-gated block above if you'd rather it stay available regardless.
        if (!isBusy && !Input.GetKey(BLOCK_KEY))
        {
            if (Input.GetKeyDown(LEFT_KEY))
            {
                if (Time.time - lastATapTime <= doubleTapWindow && Time.time >= nextDashTime)
                {
                    StartCoroutine(Dash(-1));
                }
                lastATapTime = Time.time;
            }

            if (Input.GetKeyDown(RIGHT_KEY))
            {
                if (Time.time - lastDTapTime <= doubleTapWindow && Time.time >= nextDashTime)
                {
                    StartCoroutine(Dash(1));
                }
                lastDTapTime = Time.time;
            }

            if (Input.GetKeyDown(JUMP_KEY))
            {
                TryJump();
            }
        }

        if (isBusy || selfTarget.CurrentState != CombatTarget.State.Normal)
        {
            if (Input.GetKeyDown(SLAM_KEY))
            {
                Debug.Log("[Slam] key pressed but ignored — isBusy=" + isBusy + ", CurrentState=" + selfTarget.CurrentState);
            }
            if (Input.GetKeyDown(DISRESPECT_KEY))
            {
                // StompKick() sets isBusy = true for its entire windup, so the normal
                // dispatch below (TryDisrespectKick()) never runs while charging — the
                // "press ; again to cancel" path was unreachable from there. Handle the
                // cancel here instead, before the busy-gate return, so a 2nd press during
                // the windup still lands. Anything else (a fresh stomp, or landing the
                // punt) still has to wait for isBusy to clear, same as before.
                if (isChargingStomp)
                {
                    cancelStompRequested = true;
                    Debug.Log("[Disrespect] stomp windup cancelled by 2nd press");
                }
                else
                {
                    Debug.Log("[Disrespect] key pressed but ignored — isBusy=" + isBusy + ", CurrentState=" + selfTarget.CurrentState);
                }
            }
            if (Input.GetKeyDown(GRAB_KEY))
            {
                Debug.Log("[Grab] key pressed but ignored — isBusy=" + isBusy + ", CurrentState=" + selfTarget.CurrentState);
            }
            return; // can't start a new top-level action while dashing/attacking/stunned
        }

        if (Input.GetKeyDown(LIGHT_ATTACK_KEY)) TryLightAttack();
        if (Input.GetKeyDown(KICK_KEY)) TryKick();
        if (Input.GetKeyDown(SLAM_KEY)) TrySlam();
        if (Input.GetKeyDown(GRAB_KEY) && Time.time >= nextGrabTime) StartCoroutine(TechGrab());
        if (Input.GetKeyDown(DISRESPECT_KEY)) TryDisrespectKick();
    }

    private float FacingSign => fightingController.FacingSign;
    private Vector2 FacingDir => new Vector2(FacingSign, 0f);
    private Vector2 OriginPos => hitOrigin != null ? (Vector2)hitOrigin.position : (Vector2)transform.position;

    private CombatTarget FindTargetInRange(float range)
    {
        Vector2 center = OriginPos + Vector2.right * FacingSign * (range * 0.5f);
        Collider2D hit = Physics2D.OverlapBox(center, new Vector2(range, 1.2f), 0f, enemyLayer);
        return hit != null ? hit.GetComponent<CombatTarget>() : null;
    }

    /// <summary>Fires PunchLeft or PunchRight — called from LandComboHit, alternating starting with Left on the first hit of a fresh combo.</summary>
    private void PlayPunchAnim(bool isLeft)
    {
        if (animator == null) return;
        animator.SetTrigger(isLeft ? punchLeftAnimTrigger : punchRightAnimTrigger);
    }

    /// <summary>Fires the shared Special trigger — called at the start of every action that isn't a J punch: Kick, Dash+Kick, Slam, Grab, and both stages of the Disrespectful Kick.</summary>
    private void PlaySpecialAnim()
    {
        if (animator == null) return;
        animator.SetTrigger(specialAnimTrigger);
    }

    // ---------------- Block / Parry ----------------

    private void StartBlockWindow(AttackDirection direction, float sideSign)
    {
        nextBlockTime = Time.time + blockCooldown;
        selfTarget.isBlocking = true;
        selfTarget.blockDirection = direction;

        Debug.Log("[Block] window opened — direction=" + direction);

        if (blockWindowRoutine != null) StopCoroutine(blockWindowRoutine);
        blockWindowRoutine = StartCoroutine(BlockWindowRoutine(direction, sideSign));
    }

    private IEnumerator BlockWindowRoutine(AttackDirection direction, float sideSign)
    {
        SpawnShield(direction, sideSign);

        float t = 0f;
        while (t < blockWindowDuration)
        {
            t += Time.deltaTime;
            if (activeShield != null)
            {
                activeShield.transform.position = GetShieldPosition(direction, sideSign);
            }
            yield return null;
        }

        selfTarget.isBlocking = false;

        if (activeShield != null)
        {
            StartCoroutine(FadeAndDestroyShield(activeShield, direction, sideSign));
            activeShield = null;
        }

        blockWindowRoutine = null;
    }

    private Vector2 GetShieldPosition(AttackDirection direction, float sideSign)
    {
        return direction == AttackDirection.Overhead
            ? OriginPos + Vector2.up * overheadShieldOffset
            : OriginPos + Vector2.right * sideSign * sideShieldOffset;
    }

    private void SpawnShield(AttackDirection direction, float sideSign)
    {
        GameObject prefab = direction == AttackDirection.Overhead ? overheadShieldPrefab : sideShieldPrefab;
        if (prefab == null)
        {
            Debug.Log("[Block] " + (direction == AttackDirection.Overhead ? "overheadShieldPrefab" : "sideShieldPrefab")
                + " is not assigned in the Inspector — no visual, but the block window is still active.");
            return;
        }

        if (activeShield != null) Destroy(activeShield); // shouldn't normally happen — cooldown keeps windows from overlapping

        activeShield = Instantiate(prefab, GetShieldPosition(direction, sideSign), Quaternion.identity);
    }

    private IEnumerator FadeAndDestroyShield(GameObject shield, AttackDirection direction, float sideSign)
    {
        SpriteRenderer sr = shield != null ? shield.GetComponentInChildren<SpriteRenderer>() : null;
        Color startColor = sr != null ? sr.color : Color.white;

        float t = 0f;
        while (t < shieldFadeDuration)
        {
            if (shield == null) yield break;
            t += Time.deltaTime;
            // Keep following while fading too, so it doesn't visibly freeze in place
            // if the player moves during the fade-out.
            shield.transform.position = GetShieldPosition(direction, sideSign);
            if (sr != null)
            {
                float a = Mathf.Lerp(startColor.a, 0f, t / shieldFadeDuration);
                sr.color = new Color(startColor.r, startColor.g, startColor.b, a);
            }
            yield return null;
        }

        if (shield != null) Destroy(shield);
    }

    private void HandleParrySuccess(AttackDirection direction)
    {
        Debug.Log("[Block] parried a " + direction + " attack");

        if (SoundManager.Instance != null && blockSuccessClip != null)
        {
            SoundManager.Instance.PlaySFX(blockSuccessClip);
        }

        if (parrySuccessParticlePrefab == null) return;

        Vector2 pos = activeShield != null ? (Vector2)activeShield.transform.position : OriginPos;
        Instantiate(parrySuccessParticlePrefab, pos, Quaternion.identity);
    }

    // ---------------- Jump ----------------

    private void TryJump()
    {
        Vector2 checkPos = OriginPos + groundCheckOffset;
        bool isGrounded = Physics2D.OverlapCircle(checkPos, groundCheckRadius, groundLayer);
        if (!isGrounded) return;

        rb.velocity = new Vector2(rb.velocity.x, jumpForce);
    }

    // ---------------- Dash (double-tap A/D) ----------------

    private IEnumerator Dash(int dir)
    {
        isBusy = true;
        fightingController.MovementLocked = true;

        Vector2 start = rb.position;
        Vector2 end = start + Vector2.right * dir * dashDistance;
        float t = 0f;

        while (t < dashDuration)
        {
            t += Time.fixedDeltaTime;
            rb.MovePosition(Vector2.Lerp(start, end, t / dashDuration));
            yield return new WaitForFixedUpdate();
        }

        nextDashTime = Time.time + dashCooldown;
        dashKickWindowUntil = Time.time + dashKickWindow;

        fightingController.MovementLocked = false;
        isBusy = false; // attacking is allowed immediately — only re-dashing is gated
    }

    // ---------------- J: Light Attack Combo ----------------

    private void TryLightAttack()
    {
        if (Time.time < basicAttackLockoutUntil)
        {
            Debug.Log("[LightAttack] blocked: combo end lockout for " + (basicAttackLockoutUntil - Time.time) + "s more");
            return;
        }
        if (Time.time < nextBasicAttackTime)
        {
            Debug.Log("[LightAttack] blocked: attack cooldown for " + (nextBasicAttackTime - Time.time) + "s more");
            return;
        }

        nextBasicAttackTime = Time.time + attackCooldown;

        if (comboTarget == null)
        {
            CombatTarget target = FindTargetInRange(basicAttackRange);
            if (target == null)
            {
                Debug.Log("[LightAttack] blocked: no target within range " + basicAttackRange + " (enemyLayer=" + enemyLayer.value + ")");
                return;
            }
            Debug.Log("[LightAttack] hit 1 on " + target.name);
            LandComboHit(target);
            return;
        }

        float dist = Vector2.Distance(OriginPos, comboTarget.transform.position);
        if (dist <= basicAttackRange + 0.3f)
        {
            Debug.Log("[LightAttack] combo hit " + (comboCount + 1) + " on " + comboTarget.name);
            LandComboHit(comboTarget);
        }
        else
        {
            Debug.Log("[LightAttack] combo target out of range (" + dist + " > " + (basicAttackRange + 0.3f) + ")");
        }
    }

    private void LandComboHit(CombatTarget target)
    {
        bool isFinisher = comboCount + 1 >= maxComboHits;

        // Alternates Left/Right starting with Left on hit 1 — comboCount is still the
        // PRE-increment value here (0 on hit 1, 1 on hit 2, ...), so even = Left.
        PlayPunchAnim(comboCount % 2 == 0);

        Vector2 knockback = isFinisher
            ? new Vector2(comboFinisherKnockback.x * FacingSign, comboFinisherKnockback.y)
            : Vector2.zero;

        target.ApplyHit(new HitInfo(basicAttackDamage, comboStunDuration, knockback, gameObject));
        SpawnHitParticles(target.transform.position, isFinisher);

        comboTarget = target;
        comboCount++;
        comboWindowTimer = comboWindow;

        if (isFinisher)
        {
            target.SetInvulnerable(finisherIFrameDuration);
            EndComboWithLockout();
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

    private void EndComboWithLockout()
    {
        EndCombo();
        basicAttackLockoutUntil = Time.time + comboEndLockout;
    }

    // ---------------- K: Kick (two-stage) / Dash+K forward kick ----------------

    private void TryKick()
    {
        if (Time.time <= dashKickWindowUntil)
        {
            Debug.Log("[Kick] firing forward kick (dash+K window)");
            StartCoroutine(ForwardKick());
            return;
        }

        if (kickStage == 0)
        {
            if (Time.time < nextKickStartTime)
            {
                Debug.Log("[Kick] blocked: on cooldown for " + (nextKickStartTime - Time.time) + "s more");
                return;
            }
            CombatTarget target = FindTargetInRange(kickRange);
            if (target == null)
            {
                Debug.Log("[Kick] blocked: no target within range " + kickRange);
                return;
            }
            Debug.Log("[Kick] firing kick 1 on " + target.name);
            StartCoroutine(KickHit(target, isSecondHit: false));
            return;
        }

        // kickStage == 1: attempting the follow-up
        if (Time.time > kickFollowupDeadline || kickTarget == null)
        {
            Debug.Log("[Kick] follow-up window missed (deadline=" + kickFollowupDeadline + ", now=" + Time.time + ", target=" + kickTarget + ") — resetting to a fresh sequence");
            kickStage = 0;
            return; // window missed; next press starts a fresh sequence
        }

        float dist = Vector2.Distance(OriginPos, kickTarget.transform.position);
        if (dist <= kickRange + 0.3f)
        {
            Debug.Log("[Kick] firing kick 2 (follow-up) on " + kickTarget.name);
            StartCoroutine(KickHit(kickTarget, isSecondHit: true));
        }
        else
        {
            Debug.Log("[Kick] follow-up whiffed: target out of range (" + dist + " > " + (kickRange + 0.3f) + ") — window still ticking");
        }
        // window just ticks down and expires if it's never landed
    }

    private IEnumerator KickHit(CombatTarget target, bool isSecondHit)
    {
        isBusy = true;
        fightingController.MovementLocked = true;
        PlaySpecialAnim();

        if (kickHitboxPrefab != null)
        {
            Vector2 spawnPos = OriginPos + FacingDir * kickSpawnDistance;
            float angle = FacingSign >= 0f ? 0f : 180f;
            GameObject hitboxObj = Instantiate(kickHitboxPrefab, spawnPos, Quaternion.Euler(0f, 0f, angle));
            MeleeHitbox hitbox = hitboxObj.GetComponent<MeleeHitbox>();
            if (hitbox != null)
            {
                float damage = isSecondHit ? kick2Damage : kick1Damage;
                float stun = isSecondHit ? kick2Stun : 0f; // hit 1 does NOT stun — enemy can fight back before hit 2
                float knockback = isSecondHit ? kick2Knockback : kick1Knockback;

                hitbox.Initialize(enemyLayer, damage, stun, knockback, gameObject, FacingDir,
                    hitTarget => OnKickConnect(hitTarget, isSecondHit));
            }
        }

        float recovery = isSecondHit ? kick2Recovery : kick1Recovery;
        yield return new WaitForSeconds(recovery);

        if (isSecondHit)
        {
            kickStage = 0;
            nextKickStartTime = Time.time + kickCooldown;
        }
        else
        {
            kickStage = 1;
            kickTarget = target;
            kickFollowupDeadline = Time.time + kickFollowupWindow;
        }

        fightingController.MovementLocked = false;
        isBusy = false;
    }

    private void OnKickConnect(CombatTarget target, bool isSecondHit)
    {
        if (isSecondHit)
        {
            // second kick stuns — makes it a valid combo target for L
            comboTarget = target;
            comboCount = Mathf.Max(comboCount, 1);
            comboWindowTimer = comboWindow;
        }
    }

    private void OnForwardKickConnect(CombatTarget target)
    {
        if (forwardKickHitParticlePrefab != null)
        {
            Instantiate(forwardKickHitParticlePrefab, target.transform.position, Quaternion.identity);
        }

        comboTarget = target;
        comboCount = Mathf.Max(comboCount, 1);
        comboWindowTimer = comboWindow;
    }

    private IEnumerator ForwardKick()
    {
        isBusy = true;
        fightingController.MovementLocked = true;
        dashKickWindowUntil = -999f; // consume the window
        PlaySpecialAnim();

        Vector2 start = rb.position;
        Vector2 end = start + Vector2.right * FacingSign * forwardKickDashDistance;
        float t = 0f;
        while (t < forwardKickDashDuration)
        {
            t += Time.fixedDeltaTime;
            rb.MovePosition(Vector2.Lerp(start, end, t / forwardKickDashDuration));
            yield return new WaitForFixedUpdate();
        }

        // Same real-hitbox pattern as K/L/; instead of an instant range check —
        // reuses kickHitboxPrefab since this is still fundamentally a kick, just
        // arriving off the back of a dash. Swap in a dedicated prefab here later if
        // you want the dash-kick's swing to look different from the standing kick.
        if (kickHitboxPrefab != null)
        {
            Vector2 spawnPos = OriginPos + FacingDir * kickSpawnDistance;
            float angle = FacingSign >= 0f ? 0f : 180f;
            GameObject hitboxObj = Instantiate(kickHitboxPrefab, spawnPos, Quaternion.Euler(0f, 0f, angle));
            MeleeHitbox hitbox = hitboxObj.GetComponent<MeleeHitbox>();
            if (hitbox != null)
            {
                hitbox.Initialize(enemyLayer, forwardKickDamage, forwardKickStun, forwardKickKnockback, gameObject, FacingDir,
                    hitTarget => OnForwardKickConnect(hitTarget));
            }
        }
        else
        {
            Debug.Log("[ForwardKick] kickHitboxPrefab is not assigned in the Inspector — nothing will spawn");
        }

        fightingController.MovementLocked = false;
        isBusy = false;
    }

    // ---------------- L: Slam (combo-only, held W/S picks the variant) ----------------

    private void TrySlam()
    {
        // L does nothing on its own — it only fires when W or S is held at the same time.
        bool up = Input.GetKey(COMBO_UP_KEY);
        bool down = Input.GetKey(COMBO_DOWN_KEY);
        if (!up && !down)
        {
            Debug.Log("[Slam] blocked: neither W nor S held");
            return;
        }

        if (Time.time < nextSlamTime)
        {
            Debug.Log("[Slam] blocked: on cooldown for " + (nextSlamTime - Time.time) + "s more");
            return;
        }

        if (comboTarget == null)
        {
            Debug.Log("[Slam] blocked: no comboTarget set — land a J hit or a second K hit first");
            return;
        }

        if (!comboTarget.IsAvailableForCombo)
        {
            Debug.Log("[Slam] blocked: comboTarget exists but state is " + comboTarget.CurrentState + " (needs Stunned or Knockdown)");
            return;
        }

        float dist = Vector2.Distance(OriginPos, comboTarget.transform.position);
        if (dist > slamRange + 0.3f)
        {
            Debug.Log("[Slam] blocked: out of range (" + dist + " > " + (slamRange + 0.3f) + ")");
            return;
        }

        // W takes priority if both happen to be held.
        bool isUppercut = up;
        Debug.Log("[Slam] firing — " + (isUppercut ? "uppercut" : "ground slam"));
        StartCoroutine(SlamRoutine(comboTarget, isUppercut));
    }

    private IEnumerator SlamRoutine(CombatTarget target, bool isUppercut)
    {
        isBusy = true;
        fightingController.MovementLocked = true;
        nextSlamTime = Time.time + slamCooldown;
        PlaySpecialAnim();

        yield return new WaitForSeconds(slamWindup);

        if (slamHitboxPrefab != null)
        {
            Vector2 spawnPos = OriginPos + FacingDir * slamSpawnDistance;
            float angle = FacingSign >= 0f ? 0f : 180f;

            // Uppercut is the ground slam mirrored across the horizontal axis: same
            // forward knockback, vertical component flipped (down -> up).
            Vector2 knockback = isUppercut
                ? new Vector2(slamKnockback.x * FacingSign, -slamKnockback.y)
                : new Vector2(slamKnockback.x * FacingSign, slamKnockback.y);

            GameObject hitboxObj = Instantiate(slamHitboxPrefab, spawnPos, Quaternion.Euler(0f, 0f, angle));
            MeleeHitbox hitbox = hitboxObj.GetComponent<MeleeHitbox>();
            if (hitbox != null)
            {
                hitbox.Initialize(enemyLayer, slamDamage, slamStunDuration, 0f, gameObject, FacingDir,
                    hitTarget => OnSlamConnect(hitTarget, isUppercut, knockback),
                    causesKnockdown: !isUppercut);
            }
        }

        yield return new WaitForSeconds(slamRecovery);

        fightingController.MovementLocked = false;
        isBusy = false;
        EndComboWithLockout();
    }

    private void OnSlamConnect(CombatTarget target, bool isUppercut, Vector2 knockback)
    {
        // MeleeHitbox applies knockback via its own `direction * knockbackForce`, but our
        // knockback isn't purely along the facing axis (it has a vertical component), so
        // we set it directly here instead of relying on the hitbox's knockbackForce param.
        Rigidbody2D targetRb = target.GetComponent<Rigidbody2D>();
        if (targetRb != null)
        {
            targetRb.velocity = knockback;
        }

        if (!isUppercut)
        {
            if (knockdownHitParticlePrefab != null)
            {
                Instantiate(knockdownHitParticlePrefab, target.transform.position, Quaternion.identity);
            }
            StartCoroutine(GroundSlamRoutine(target));
        }
    }

    private IEnumerator GroundSlamRoutine(CombatTarget target)
    {
        yield return new WaitForSeconds(groundSlamDelay);

        if (target == null || groundSlamParticlePrefab == null) yield break;

        Vector2 origin = target.transform.position;
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, groundSlamRayDistance, groundLayer);
        Vector3 groundPos = hit.collider != null ? (Vector3)hit.point : target.transform.position;

        Instantiate(groundSlamParticlePrefab, groundPos, Quaternion.identity);
    }

    // ---------------- F: Tech Grab ----------------

    private IEnumerator TechGrab()
    {
        isBusy = true;
        fightingController.MovementLocked = true;
        nextGrabTime = Time.time + grabCooldown;
        PlaySpecialAnim();

        bool cancelled = false;
        System.Action<HitInfo> cancelHandler = _ => cancelled = true;
        selfTarget.OnHit += cancelHandler;

        float t = 0f;
        while (t < grabWindup)
        {
            if (cancelled) break;
            t += Time.deltaTime;
            yield return null;
        }
        selfTarget.OnHit -= cancelHandler;

        if (cancelled)
        {
            Debug.Log("[Grab] cancelled — player was hit during windup");
        }
        else
        {
            CombatTarget target = FindTargetInRange(grabRange);
            if (target == null)
            {
                Debug.Log("[Grab] windup finished but no target found in range " + grabRange);
            }
            else
            {
                Vector2 frontPos = OriginPos + FacingDir * grabPullDistance;
                target.transform.position = frontPos;

                // Grabs bypass block by design — set the state directly rather than
                // routing through ApplyHit.
                target.SetState(CombatTarget.State.Knockdown, grabKnockdownDuration);

                grabbedTarget = target;
                grabbedTargetExpiresAt = Time.time + grabWindowForDisrespect;
                Debug.Log("[Grab] success — grabbedTarget set, expires in " + grabWindowForDisrespect + "s");
            }
        }

        fightingController.MovementLocked = false;
        isBusy = false;
    }

    // ---------------- ; : Disrespectful Kick (grab punish, stomp -> punt) ----------------

    private void TryDisrespectKick()
    {
        // NOTE: the "2nd press during windup cancels the stomp" case is now handled
        // directly in Update()'s isBusy-gated block, since isChargingStomp is only
        // ever true while isBusy is also true — this function can't be reached then.

        if (disrespectStage == 1)
        {
            if (Time.time > puntDeadline || disrespectTarget == null)
            {
                Debug.Log("[Disrespect] punt window missed (deadline=" + puntDeadline + ", now=" + Time.time + ", target=" + disrespectTarget + ") — resetting to stage 0");
                disrespectStage = 0;
                disrespectTarget = null;
                return;
            }
            Debug.Log("[Disrespect] firing punt");
            StartCoroutine(PuntKick(disrespectTarget));
            return;
        }

        if (grabbedTarget == null)
        {
            Debug.Log("[Disrespect] blocked: no grabbedTarget — land a successful F grab first");
            return;
        }

        Debug.Log("[Disrespect] firing stomp on " + grabbedTarget.name);
        StartCoroutine(StompKick(grabbedTarget));
    }

    private IEnumerator StompKick(CombatTarget expectedTarget)
    {
        isBusy = true;
        fightingController.MovementLocked = true;
        isChargingStomp = true;
        cancelStompRequested = false;
        PlaySpecialAnim();

        bool cancelledByHit = false;
        System.Action<HitInfo> cancelHandler = _ => cancelledByHit = true;
        selfTarget.OnHit += cancelHandler;

        float t = 0f;
        while (t < stompWindup)
        {
            if (cancelledByHit || cancelStompRequested) break;
            t += Time.deltaTime;
            yield return null;
        }
        selfTarget.OnHit -= cancelHandler;
        isChargingStomp = false;

        bool wasCancelled = cancelledByHit || cancelStompRequested;

        if (wasCancelled)
        {
            Debug.Log("[Disrespect] stomp cancelled (hit=" + cancelledByHit + ", requested=" + cancelStompRequested + ")");
        }
        else
        {
            if (bootPrefab == null)
            {
                Debug.Log("[Disrespect] bootPrefab is not assigned in the Inspector — nothing will spawn");
            }

            // Spawn up in the air above the landing spot, then drop it down onto that
            // spot — the boot's hitbox does its own overlap detection as it descends,
            // same as it always has, it just now arrives there instead of appearing
            // there instantly.
            Vector2 landingPos = OriginPos + FacingDir * stompSpawnDistance;
            Vector2 startPos = landingPos + Vector2.up * stompDropHeight;

            GameObject bootObj = SpawnBootKick(stompDamage, stompStunDuration, knockbackForce: 0f, causesKnockdown: false,
                spawnPosition: startPos, onConnect: hitTarget => OnStompConnect(hitTarget, expectedTarget));
            MeleeHitbox hitbox = bootObj != null ? bootObj.GetComponent<MeleeHitbox>() : null;

            float descendT = 0f;
            while (descendT < stompDescentDuration)
            {
                descendT += Time.deltaTime;
                float p = descendT / stompDescentDuration;
                if (hitbox != null) hitbox.SetPosition(Vector2.Lerp(startPos, landingPos, p));
                yield return null;
            }
            if (hitbox != null) hitbox.SetPosition(landingPos);

            if (bootObj != null) StartCoroutine(FadeAndDestroyBoot(bootObj));

            yield return new WaitForSeconds(stompRecovery);
        }

        grabbedTarget = null;

        fightingController.MovementLocked = false;
        isBusy = false;
    }

    private void OnStompConnect(CombatTarget hitTarget, CombatTarget expectedTarget)
    {
        // Only counts as landing the disrespectful stomp if the boot actually hit the
        // enemy you grabbed — not some other target that wandered into its path.
        //
        // Can't check hitTarget.CurrentState == Knockdown here: by the time this callback
        // runs, CombatTarget.ApplyHit has already changed their state (the stomp itself
        // doesn't cause a knockdown, so a real hit flips them Knockdown -> Stunned before
        // we ever see it). Tracking the specific grabbed target instead sidesteps that.
        if (hitTarget != expectedTarget)
        {
            Debug.Log("[Disrespect] boot hit " + hitTarget.name + ", but that's not the grabbed target — no floor VFX, no punt window");
            return;
        }

        if (stompFloorParticlePrefab != null)
        {
            Instantiate(stompFloorParticlePrefab, hitTarget.transform.position, Quaternion.identity);
        }

        disrespectTarget = hitTarget;
        disrespectStage = 1;
        puntDeadline = Time.time + puntFollowupWindow;
        Debug.Log("[Disrespect] stomp landed on " + hitTarget.name + " — punt window open for " + puntFollowupWindow + "s");
    }

    private IEnumerator PuntKick(CombatTarget target)
    {
        isBusy = true;
        fightingController.MovementLocked = true;
        disrespectStage = 0;
        disrespectTarget = null;
        PlaySpecialAnim();

        yield return new WaitForSeconds(puntWindup);

        // Circular kicking arc, pivoting around OriginPos (roughly hip height) — the
        // boot sweeps from puntArcStartAngle to puntArcEndAngle at a fixed radius,
        // like a roundhouse. Knockback direction stays pure FacingDir regardless of
        // exactly where along the arc the hit lands, so the "long-distance punt" is
        // always a clean horizontal send, not whatever the arc's tangent happens to be
        // at the moment of impact.
        Vector2 startPos = ArcPoint(puntArcStartAngle);
        GameObject bootObj = SpawnBootKick(puntDamage, stunDuration: puntStunDuration, knockbackForce: puntKnockback, causesKnockdown: false,
            spawnPosition: startPos, onConnect: null);
        MeleeHitbox hitbox = bootObj != null ? bootObj.GetComponent<MeleeHitbox>() : null;

        float sweepT = 0f;
        while (sweepT < puntSweepDuration)
        {
            sweepT += Time.deltaTime;
            float p = sweepT / puntSweepDuration;
            float angle = Mathf.Lerp(puntArcStartAngle, puntArcEndAngle, p);
            if (hitbox != null) hitbox.SetPosition(ArcPoint(angle));
            yield return null;
        }
        if (hitbox != null) hitbox.SetPosition(ArcPoint(puntArcEndAngle));

        if (bootObj != null) StartCoroutine(FadeAndDestroyBoot(bootObj));

        yield return new WaitForSeconds(puntRecovery);

        fightingController.MovementLocked = false;
        isBusy = false;
    }

    /// <summary>
    /// World position on the punt's circular arc at the given angle (degrees), where
    /// 0 = straight forward (facing direction), positive = rotated up/behind. Mirrors
    /// automatically with FacingSign, so the same angle values produce a matching
    /// swing shape whichever way the player is facing.
    /// </summary>
    private Vector2 ArcPoint(float angleDeg)
    {
        float worldAngle = FacingSign >= 0f ? angleDeg : 180f - angleDeg;
        float rad = worldAngle * Mathf.Deg2Rad;
        return OriginPos + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * puntArcRadius;
    }

    /// <summary>
    /// Spawns the boot prefab at the given world position and wires it up as a normal
    /// MeleeHitbox swing (facing-direction only — no mouse aim). Returns the spawned
    /// GameObject (or null if bootPrefab isn't assigned) so the caller can move it
    /// (via MeleeHitbox.SetPosition) and decide when its fade-out should start —
    /// this method no longer starts that itself, since fading needs to wait until
    /// whatever motion the caller is doing (stomp descent, punt arc) has finished.
    /// </summary>
    private GameObject SpawnBootKick(float damage, float stunDuration, float knockbackForce, bool causesKnockdown, Vector2 spawnPosition, System.Action<CombatTarget> onConnect)
    {
        if (bootPrefab == null) return null;

        float angle = FacingSign >= 0f ? 0f : 180f;

        GameObject bootObj = Instantiate(bootPrefab, spawnPosition, Quaternion.Euler(0f, 0f, angle));
        // Confirms Instantiate actually ran and shows exactly where — if this log
        // appears but nothing is visible on screen, the spawn call itself is fine and
        // the problem is the prefab (no SpriteRenderer, 0 scale, wrong sorting layer,
        // or spawning behind/inside another sprite), not this method.
        Debug.Log("[Disrespect] boot spawned: " + bootObj.name + " at " + spawnPosition);

        MeleeHitbox hitbox = bootObj.GetComponent<MeleeHitbox>();
        if (hitbox != null)
        {
            // We handle this boot's lifetime ourselves via the fade coroutine (started
            // by the caller once its motion is done), so give MeleeHitbox's own
            // auto-destroy plenty of headroom instead of fighting over which Destroy()
            // call wins.
            hitbox.selfDestructTime = bootFadeDuration + 2f;
            hitbox.Initialize(enemyLayer, damage, stunDuration, knockbackForce, gameObject, FacingDir,
                onConnect, causesKnockdown);
        }
        else
        {
            Debug.Log("[Disrespect] bootPrefab has no MeleeHitbox component — it will spawn but never register a hit");
        }

        return bootObj;
    }

    private IEnumerator FadeAndDestroyBoot(GameObject boot)
    {
        SpriteRenderer sr = boot != null ? boot.GetComponentInChildren<SpriteRenderer>() : null;
        Color startColor = sr != null ? sr.color : Color.white;

        float t = 0f;
        while (t < bootFadeDuration)
        {
            if (boot == null) yield break;
            t += Time.deltaTime;
            if (sr != null)
            {
                float a = Mathf.Lerp(startColor.a, 0f, t / bootFadeDuration);
                sr.color = new Color(startColor.r, startColor.g, startColor.b, a);
            }
            yield return null;
        }

        if (boot != null) Destroy(boot);
    }


#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (hitOrigin == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(hitOrigin.position, basicAttackRange);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere((Vector2)hitOrigin.position + groundCheckOffset, groundCheckRadius);
    }
#endif
}