using UnityEngine;

// Put this on the SAME GameObject as the Animator (the child) — Animation Events
// can only call methods on components on that exact object, so this just
// forwards the call up to EnemyAI on the parent.
public class EnemyAnimationEvents : MonoBehaviour
{
    private EnemyAI enemyAI;

    private void Awake()
    {
        enemyAI = GetComponentInParent<EnemyAI>();
    }

    // Wire this up as the Animation Event on the attack clip's connect frame.
    public void SpawnAttackHitbox()
    {
        if (enemyAI != null)
        {
            enemyAI.SpawnAttackHitbox();
        }
    }
}