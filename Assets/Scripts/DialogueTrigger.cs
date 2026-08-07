using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class DialogueTrigger : MonoBehaviour
{
    [Header("Dialogue Content")]
    public DialogueLine[] lines;

    [Header("Interact Prompt")]
    [Tooltip("An 'E' icon (child SpriteRenderer) positioned above the NPC. Shown only while the player is in range, and gently bobs up and down.")]
    public SpriteRenderer interactPrompt;

    [Tooltip("How fast the prompt bobs. Keep this low for a slow, subtle motion.")]
    public float bobSpeed = 1.5f;

    [Tooltip("How far up/down the prompt moves. Keep this small for a subtle motion.")]
    public float bobAmount = 0.05f;

    [Header("Interaction")]
    public string playerTag = "Player";

    public bool IsPlayerInRange { get; private set; }

    private Vector3 promptBasePosition;

    private void Awake()
    {
        if (interactPrompt != null)
        {
            interactPrompt.enabled = false;
            promptBasePosition = interactPrompt.transform.localPosition;
        }

        // Make sure the collider used for range detection is a trigger.
        GetComponent<Collider2D>().isTrigger = true;
    }

    private void Update()
    {
        // Subtle bob while the prompt is visible.
        if (interactPrompt != null && interactPrompt.enabled)
        {
            float yOffset = Mathf.Sin(Time.time * bobSpeed) * bobAmount;
            interactPrompt.transform.localPosition = promptBasePosition + new Vector3(0f, yOffset, 0f);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag)) return;

        IsPlayerInRange = true;
        SetPrompt(true);

        PlayerInteractor interactor = other.GetComponent<PlayerInteractor>();
        if (interactor != null)
            interactor.SetCurrentTrigger(this);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag)) return;

        IsPlayerInRange = false;
        SetPrompt(false);

        PlayerInteractor interactor = other.GetComponent<PlayerInteractor>();
        if (interactor != null)
            interactor.ClearCurrentTrigger(this);
    }

    private void SetPrompt(bool state)
    {
        if (interactPrompt == null) return;

        interactPrompt.enabled = state;
        if (state)
            interactPrompt.transform.localPosition = promptBasePosition; // reset so it doesn't jump in mid-bob
    }

    public void Interact()
    {
        if (lines == null || lines.Length == 0) return;
        DialogueManager.Instance.StartDialogue(lines);
    }
}