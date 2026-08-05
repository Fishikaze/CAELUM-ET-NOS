using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives a fighting-game style health bar with a trailing "damage flash": the main
/// bar (healthSlider) always reflects true current health instantly. An optional
/// second bar behind it (damageTrailSlider — usually tinted white via its Fill Image
/// color) briefly holds at the OLD value when you take damage, so a white sliver is
/// visible where your health used to be, then smoothly drains down to catch up with
/// the main bar.
///
/// Setup:
///   1. Two Sliders stacked in the same spot (same size/position) — damageTrailSlider
///      BEHIND healthSlider in the Hierarchy, so it only peeks out from the gap left
///      when the main bar shrinks.
///   2. On damageTrailSlider's Fill Image, set the color to white (or whatever flash
///      color you want).
///   3. Drag healthSlider, damageTrailSlider, and the CombatTarget to track (usually
///      the player) into their fields below. damageTrailSlider is optional — leave it
///      empty for a plain instant-update bar with no trail effect.
///
/// Listens to CombatTarget.OnHealthChanged rather than polling every frame.
/// </summary>
public class HealthBarUI : MonoBehaviour
{
    [Tooltip("The main UI Slider — always shows true current health, updates instantly.")]
    public Slider healthSlider;

    [Tooltip("Optional second Slider, placed BEHIND healthSlider in the Hierarchy, usually tinted white via its Fill Image color. Shows the 'just lost' chunk and drains down to catch up after a delay. Leave empty to skip the trail effect entirely.")]
    public Slider damageTrailSlider;

    [Tooltip("How long the white trail holds at the old value before it starts draining, after taking damage.")]
    public float damageTrailDelay = 0.3f;

    [Tooltip("How long the white trail takes to drain down and catch up with the main bar, once it starts.")]
    public float damageTrailDuration = 0.4f;

    [Tooltip("Which CombatTarget's health to display — usually the player. Leave empty only if this component happens to live on the same GameObject (or a child of one) that also has the CombatTarget, which is uncommon for UI.")]
    public CombatTarget target;

    private Coroutine trailRoutine;

    private void Awake()
    {
        if (target == null) target = GetComponentInParent<CombatTarget>();
        if (target == null)
        {
            Debug.Log("[HealthBarUI] no Target assigned and none found on this object or its parents — drag a CombatTarget into the Target field in the Inspector.");
        }
    }

    private void OnEnable()
    {
        if (target != null) target.OnHealthChanged += HandleHealthChanged;
    }

    private void OnDisable()
    {
        if (target != null) target.OnHealthChanged -= HandleHealthChanged;
    }

    private void HandleHealthChanged(float current, float max)
    {
        if (healthSlider == null)
        {
            Debug.Log("[HealthBarUI] Health Slider isn't assigned in the Inspector — nothing to update.");
            return;
        }

        healthSlider.maxValue = max;

        // Compare against the main bar's CURRENT displayed value (still the old value
        // at this point — we don't update it until the end of this method) to tell a
        // real hit apart from a heal or the initial Awake call.
        bool tookDamage = current < healthSlider.value;

        if (damageTrailSlider != null)
        {
            damageTrailSlider.maxValue = max;

            if (tookDamage)
            {
                // Hold the trail at the main bar's old value, so the gap between old
                // and new reads as a white sliver, then drain it down to the new value.
                if (trailRoutine != null) StopCoroutine(trailRoutine);
                trailRoutine = StartCoroutine(DrainTrail(healthSlider.value, current));
            }
            else
            {
                // Healing or initial setup — nothing was just lost, so there's no trail
                // to show. Snap it to match immediately instead of animating.
                if (trailRoutine != null) StopCoroutine(trailRoutine);
                damageTrailSlider.value = current;
            }
        }

        // Main bar always reflects true current health immediately, regardless of
        // whatever the trail bar is doing.
        healthSlider.value = current;
    }

    private IEnumerator DrainTrail(float fromValue, float toValue)
    {
        damageTrailSlider.value = fromValue;

        yield return new WaitForSeconds(damageTrailDelay);

        float t = 0f;
        while (t < damageTrailDuration)
        {
            t += Time.deltaTime;
            damageTrailSlider.value = Mathf.Lerp(fromValue, toValue, t / damageTrailDuration);
            yield return null;
        }

        damageTrailSlider.value = toValue;
        trailRoutine = null;
    }
}