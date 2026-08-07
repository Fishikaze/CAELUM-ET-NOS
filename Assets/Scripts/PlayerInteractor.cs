using UnityEngine;

// Attach this to the player object (the same object whose collider has the "Player" tag
// and is what DialogueTrigger's OnTriggerEnter2D/Exit2D detects).
public class PlayerInteractor : MonoBehaviour
{
    public KeyCode interactKey = KeyCode.E;

    private DialogueTrigger currentTrigger;

    private void Update()
    {
        // Don't try to start a new conversation while one is already playing.
        if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueActive)
            return;

        if (currentTrigger != null && Input.GetKeyDown(interactKey))
        {
            currentTrigger.Interact();
        }
    }

    public void SetCurrentTrigger(DialogueTrigger trigger)
    {
        currentTrigger = trigger;
    }

    public void ClearCurrentTrigger(DialogueTrigger trigger)
    {
        // Only clear if it's the same trigger we're leaving — guards against
        // overlapping trigger zones stomping on each other's enter/exit order.
        if (currentTrigger == trigger)
            currentTrigger = null;
    }
}