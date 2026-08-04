using UnityEngine;

/// <summary>
/// Owns the Platforming &lt;-&gt; Fighting mode swap (F key by default). Enables/disables
/// the two movement controllers plus PlayerCombat so only one control scheme is
/// live at a time. Keep all mode-specific behaviour inside PlatformingController /
/// FightingController / PlayerCombat — this script is deliberately dumb.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PlatformingController))]
[RequireComponent(typeof(FightingController))]
[RequireComponent(typeof(PlayerCombat))]
public class PlayerModeController : MonoBehaviour
{
    public enum Mode { Platforming, Fighting }
    public Mode CurrentMode { get; private set; } = Mode.Platforming;

    public KeyCode swapKey = KeyCode.Q;

    private PlatformingController platforming;
    private FightingController fighting;
    private PlayerCombat combat;
    private Rigidbody2D rb;

    private void Awake()
    {
        platforming = GetComponent<PlatformingController>();
        fighting = GetComponent<FightingController>();
        combat = GetComponent<PlayerCombat>();
        rb = GetComponent<Rigidbody2D>();

        ApplyMode();
    }

    private void Update()
    {
        if (Input.GetKeyDown(swapKey))
        {
            CurrentMode = CurrentMode == Mode.Platforming ? Mode.Fighting : Mode.Platforming;
            ApplyMode();
        }
    }

    private void ApplyMode()
    {
        bool isFighting = CurrentMode == Mode.Fighting;

        platforming.enabled = !isFighting;
        fighting.enabled = isFighting;
        combat.enabled = isFighting;

        // Clear horizontal momentum on swap so a platforming sprint doesn't carry
        // straight into fight mode (or vice versa).
        if (rb != null)
        {
            rb.velocity = new Vector2(0f, rb.velocity.y);
        }
    }
}