using System.Collections;
using UnityEngine;

/// <summary>
/// Placeholder hit feedback for before real animations exist:
///   - Flashes red briefly on every hit (subscribes to CombatTarget.OnHit).
///   - Sinks into the ground while Knockdown, eases back up the moment
///     anything (e.g. PlayerCombat's T ability) brings the target out of
///     Knockdown — no combat-logic changes needed, this just watches state.
///
/// Purely visual: everything here moves an optional child "visual root"
/// transform, never the Rigidbody2D/Collider2D, so it can't affect physics,
/// ground checks, or hit-range checks. Safe to drop onto the player and every
/// enemy as-is.
/// </summary>
[RequireComponent(typeof(CombatTarget))]
public class HitReactionVisuals : MonoBehaviour
{
    [Header("Flash")]
    [Tooltip("Leave empty to auto-find a SpriteRenderer in this object or its children.")]
    public SpriteRenderer spriteRenderer;
    public Color flashColor = Color.red;
    public float flashDuration = 0.12f;

    [Header("Knockdown Sink (leave visualRoot empty to skip this effect)")]
    [Tooltip("Child transform holding the sprite/visuals — move the art here, keep the Rigidbody2D/Collider2D on the root.")]
    public Transform visualRoot;
    public float knockdownSinkDepth = 0.35f;
    public float sinkRiseSpeed = 8f;

    private CombatTarget combatTarget;
    private Color originalColor;
    private Coroutine flashRoutine;

    private CombatTarget.State lastState = CombatTarget.State.Normal;
    private Vector3 visualRestLocalPos;
    private Coroutine sinkRoutine;

    private void Awake()
    {
        combatTarget = GetComponent<CombatTarget>();

        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }
        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }

        if (visualRoot != null)
        {
            visualRestLocalPos = visualRoot.localPosition;
        }
    }

    private void OnEnable()
    {
        combatTarget.OnHit += HandleHit;
    }

    private void OnDisable()
    {
        combatTarget.OnHit -= HandleHit;
    }

    private void Update()
    {
        CombatTarget.State current = combatTarget.CurrentState;
        if (current == lastState) return;

        if (current == CombatTarget.State.Knockdown)
        {
            StartSink(goingDown: true);
        }
        else if (lastState == CombatTarget.State.Knockdown)
        {
            // Leaving Knockdown — whether from a T hit or the timer just running out —
            // means "back on your feet."
            StartSink(goingDown: false);
        }

        lastState = current;
    }

    private void HandleHit(HitInfo hit)
    {
        if (spriteRenderer == null) return;

        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        spriteRenderer.color = flashColor;
        yield return new WaitForSeconds(flashDuration);
        spriteRenderer.color = originalColor;
        flashRoutine = null;
    }

    private void StartSink(bool goingDown)
    {
        if (visualRoot == null) return;
        if (sinkRoutine != null) StopCoroutine(sinkRoutine);
        sinkRoutine = StartCoroutine(SinkRoutine(goingDown));
    }

    private IEnumerator SinkRoutine(bool goingDown)
    {
        Vector3 target = visualRestLocalPos + (goingDown ? Vector3.down * knockdownSinkDepth : Vector3.zero);

        while (Vector3.Distance(visualRoot.localPosition, target) > 0.01f)
        {
            visualRoot.localPosition = Vector3.MoveTowards(visualRoot.localPosition, target, sinkRiseSpeed * Time.deltaTime);
            yield return null;
        }

        visualRoot.localPosition = target;
        sinkRoutine = null;
    }
}