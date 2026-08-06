using UnityEngine;

/// <summary>
/// Put this on the SAME GameObject as the boss's Animator (the sprite child, not the
/// root) — Unity Animation Events can only call methods on components attached to
/// that exact GameObject, not a parent, so BossAI's AnimEvent_* methods aren't
/// reachable directly from a clip when the Animator lives one level below BossAI in
/// the hierarchy. This just forwards both calls up.
///
/// In the Animation window, add your two events (spawn-hitbox frame, end-of-clip
/// frame) targeting THIS component's AnimEvent_SpawnHitbox / AnimEvent_SwingComplete
/// — not BossAI's, which won't appear in the function dropdown for this GameObject.
/// </summary>
public class BossAnimationEventRelay : MonoBehaviour
{
    [Tooltip("Auto-found via GetComponentInParent if left empty.")]
    public BossAI boss;

    private void Awake()
    {
        if (boss == null) boss = GetComponentInParent<BossAI>();
        if (boss == null)
        {
            Debug.Log("[BossAnimationEventRelay] no BossAI found on this object or its parents — assign one in the Inspector.");
        }
    }

    public void AnimEvent_SpawnHitbox()
    {
        Debug.Log("[BossAnimationEventRelay] AnimEvent_SpawnHitbox received on " + gameObject.name);
        if (boss != null) boss.AnimEvent_SpawnHitbox();
    }

    // public void AnimEvent_SwingComplete()
    // {
    //     Debug.Log("[BossAnimationEventRelay] AnimEvent_SwingComplete received on " + gameObject.name);
    //     if (boss != null) boss.AnimEvent_SwingComplete();
    // }
}