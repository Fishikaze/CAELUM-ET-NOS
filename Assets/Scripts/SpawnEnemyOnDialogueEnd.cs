using UnityEngine;

// Attach alongside DialogueTrigger on the interactable NPC/object. Once the
// dialogue started by that trigger finishes, spawns enemyPrefab (a new object) at
// the interactable's position and removes the interactable itself.
[RequireComponent(typeof(DialogueTrigger))]
public class SpawnEnemyOnDialogueEnd : MonoBehaviour
{
    public GameObject enemyPrefab;
    public GameObject newting;
    private DialogueTrigger dialogueTrigger;
    public GameObject goon;

    private void Awake()
    {
        dialogueTrigger = GetComponent<DialogueTrigger>();
    }

    private void OnEnable()
    {
        dialogueTrigger.OnDialogueFinished += HandleDialogueFinished;
    }

    private void OnDisable()
    {
        dialogueTrigger.OnDialogueFinished -= HandleDialogueFinished;
    }

    private void HandleDialogueFinished()
    {
        if (enemyPrefab != null)
        {
            enemyPrefab.SetActive(true);    
            newting.SetActive(true);
            goon.SetActive(true);

        }
        else
        {
            Debug.Log("[SpawnEnemyOnDialogueEnd] enemyPrefab is not assigned in the Inspector — nothing will spawn");
        }

        Destroy(gameObject);
    }
}