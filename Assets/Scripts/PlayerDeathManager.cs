using UnityEngine;
using UnityEngine.SceneManagement;

// Attach to the player. Respawns by reloading the current scene if either:
//  - health drops to 0 or below (hooks CombatTarget.OnDeath, which already fires
//    exactly when that happens — no need to poll HP every frame), or
//  - the player enters a trigger tagged voidZoneTag (e.g. a collider covering the
//    area below the level, for falling out of the world).
[RequireComponent(typeof(CombatTarget))]
public class PlayerDeathManager : MonoBehaviour
{
    [Tooltip("Tag used by any 'fell out of the world' trigger colliders in the level.")]
    public string voidZoneTag = "VoidZone";

    private CombatTarget selfTarget;
    private bool isRespawning;

    private void Awake()
    {
        selfTarget = GetComponent<CombatTarget>();
    }

    private void OnEnable()
    {
        selfTarget.OnDeath += HandleDeath;
    }

    private void OnDisable()
    {
        selfTarget.OnDeath -= HandleDeath;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag(voidZoneTag))
        {
            Respawn();
        }
    }

    private void HandleDeath()
    {
        Respawn();
    }

    private void Respawn()
    {
        if (isRespawning) return; // guard in case both triggers land the same frame
        isRespawning = true;

        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}